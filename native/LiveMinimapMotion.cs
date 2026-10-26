using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

// Observes changes between two renders produced by the same game client.
// This deliberately does NOT yield a WorldTile or authorize a navigation click:
// pixels per world tile and the actual player floor remain uncalibrated.
internal sealed record LiveMinimapMotion(double? ShiftX,double? ShiftY,int Inliers,string Reason,
    int Correspondences=0,double? Scale=null);

internal static class LiveMinimapMotionVision
{
    internal static LiveMinimapMotion Compare(NavigationPixels first,NavigationPixels second)
    {
        if(first.MinimapPng.Length==0||second.MinimapPng.Length==0)
            return new(null,null,0,"LIVE_MINIMAP_IMAGE_MISSING");
        using var rawA=Cv.Cv2.ImDecode(first.MinimapPng,Cv.ImreadModes.Grayscale);
        using var rawB=Cv.Cv2.ImDecode(second.MinimapPng,Cv.ImreadModes.Grayscale);
        if(!Valid(rawA,first.MapBounds)||!Valid(rawB,second.MapBounds))
            return new(null,null,0,"LIVE_MINIMAP_IMAGE_INVALID");
        double dx=(first.MapBounds.Left+first.MapBounds.Width/2d)-
            (second.MapBounds.Left+second.MapBounds.Width/2d);
        double dy=(first.MapBounds.Top+first.MapBounds.Height/2d)-
            (second.MapBounds.Top+second.MapBounds.Height/2d);
        if(Math.Abs(dx)>24||Math.Abs(dy)>24||Math.Abs(rawA.Width-rawB.Width)>8)
            return new(null,null,0,"LIVE_MINIMAP_LAYOUT_CHANGED");

        // Use equal-size *central* crops without scaling. A Hough circle may
        // vary by 1-2 pixels across frames; resizing would invent motion.
        int side=Math.Min(rawA.Width,rawB.Width);
        int cropAx=(rawA.Width-side)/2,cropAy=(rawA.Height-side)/2;
        int cropBx=(rawB.Width-side)/2,cropBy=(rawB.Height-side)/2;
        using var a=new Cv.Mat(rawA,new Cv.Rect(cropAx,cropAy,side,side));
        using var b=new Cv.Mat(rawB,new Cv.Rect(cropBx,cropBy,side,side));
        using var mask=new Cv.Mat(a.Size(),Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        Cv.Cv2.Circle(mask,new(side/2,side/2),side/2-16,Cv.Scalar.White,-1);
        Cv.Cv2.Circle(mask,new(side/2,side/2),14,Cv.Scalar.Black,-1);

        using var sift=Cv.Features2D.SIFT.Create(650,contrastThreshold:.009);
        using var da=new Cv.Mat();using var db=new Cv.Mat();
        sift.DetectAndCompute(a,mask,out var pa,da);
        sift.DetectAndCompute(b,mask,out var pb,db);
        if(pa.Length<9||pb.Length<9||da.Empty()||db.Empty())
            return new(null,null,0,"LIVE_MINIMAP_FEATURES_INSUFFICIENT");
        using var matcher=new Cv.BFMatcher(Cv.NormTypes.L2);
        var forward=matcher.KnnMatch(da,db,2)
            .Where(pair=>pair.Length==2&&pair[0].Distance<.70*pair[1].Distance)
            .Select(pair=>pair[0]).ToArray();
        var backwards=matcher.KnnMatch(db,da,2)
            .Where(pair=>pair.Length==2&&pair[0].Distance<.70*pair[1].Distance)
            .Select(pair=>(Before:pair[0].TrainIdx,After:pair[0].QueryIdx)).ToHashSet();
        var pairs=forward.Where(pair=>backwards.Contains((pair.QueryIdx,pair.TrainIdx))).ToArray();
        if(pairs.Length<9)return new(null,null,0,"LIVE_MINIMAP_MATCHES_INSUFFICIENT",pairs.Length);
        var from=pairs.Select(p=>pa[p.QueryIdx].Pt).ToArray();
        var to=pairs.Select(p=>pb[p.TrainIdx].Pt).ToArray();
        using var input=Cv.InputArray.Create(from);using var output=Cv.InputArray.Create(to);
        using var inliers=new Cv.Mat();
        using var fit=Cv.Cv2.EstimateAffinePartial2D(input,output,inliers,
            Cv.RobustEstimationAlgorithms.RANSAC,2.5,3000,.995,20);
        if(fit is null||fit.Empty())return new(null,null,0,"LIVE_MINIMAP_GEOMETRY_UNVERIFIED",pairs.Length);
        int count=Cv.Cv2.CountNonZero(inliers);
        if(count<9||count<pairs.Length*.70)
            return new(null,null,count,"LIVE_MINIMAP_CONSENSUS_INSUFFICIENT",pairs.Length);
        var spread=from.Where((_,i)=>inliers.At<byte>(i)!=0).ToArray();
        if(spread.Max(p=>p.X)-spread.Min(p=>p.X)<32||spread.Max(p=>p.Y)-spread.Min(p=>p.Y)<32)
            return new(null,null,count,"LIVE_MINIMAP_LANDMARKS_CLUSTERED",pairs.Length);
        double a00=fit.At<double>(0,0),a10=fit.At<double>(1,0);
        double scale=Math.Sqrt(a00*a00+a10*a10);
        double degrees=Math.Atan2(a10,a00)*180/Math.PI;
        if(scale is <.965 or >1.035||Math.Abs(degrees)>3)
            return new(null,null,count,"LIVE_MINIMAP_ZOOM_OR_ORIENTATION_CHANGED",pairs.Length,scale);
        // Convert the fitted *crop-local* translation back to the common game
        // viewport. Hough may report e.g. a 150px circle and then a 144px
        // circle around the SAME stationary terrain. Without this correction
        // its changed crop origin fabricates approximately four pixels of
        // player movement even though the RANSAC inliers are genuine.
        // Evaluate displacement at the minimap centre, NOT by treating the
        // affine intercept as motion: even a tiny RANSAC rotation makes the
        // intercept depend on the arbitrary origin of the cropped patch.
        double center=side/2d;
        double tx=fit.At<double>(0,0)*center+fit.At<double>(0,1)*center+
            fit.At<double>(0,2)-center+second.MapBounds.Left+cropBx-first.MapBounds.Left-cropAx;
        double ty=fit.At<double>(1,0)*center+fit.At<double>(1,1)*center+
            fit.At<double>(1,2)-center+second.MapBounds.Top+cropBy-first.MapBounds.Top-cropAy;
        if(Math.Abs(tx)>side/2d-18||Math.Abs(ty)>side/2d-18)
            return new(null,null,count,"LIVE_MINIMAP_DISPLACEMENT_TOO_LARGE",pairs.Length,scale);

        // A plausible transform must also map independent terrain edges.
        // SIFT consensus alone is insufficient among repeating objects/dots.
        using var warped=new Cv.Mat();
        Cv.Cv2.WarpAffine(a,warped,fit,b.Size(),Cv.InterpolationFlags.Linear);
        using var actualEdges=new Cv.Mat();using var predictedEdges=new Cv.Mat();
        using var expanded=new Cv.Mat();using var agreed=new Cv.Mat();
        Cv.Cv2.Canny(b,actualEdges,45,110);
        Cv.Cv2.Canny(warped,predictedEdges,45,110);
        Cv.Cv2.BitwiseAnd(actualEdges,mask,actualEdges);
        using var kernel=Cv.Cv2.GetStructuringElement(Cv.MorphShapes.Rect,new(3,3));
        Cv.Cv2.Dilate(predictedEdges,expanded,kernel);
        Cv.Cv2.BitwiseAnd(actualEdges,expanded,agreed);
        int edges=Cv.Cv2.CountNonZero(actualEdges);
        if(edges<65||Cv.Cv2.CountNonZero(agreed)<edges*.40)
            return new(null,null,count,"LIVE_MINIMAP_EDGES_DISAGREE",pairs.Length,scale);
        return new(tx,ty,count,"VERIFIED_VISUAL_SHIFT_ONLY",pairs.Length,scale);
    }

    private static bool Valid(Cv.Mat image,Rectangle bounds)=>
        !image.Empty()&&image.Width is >=124 and <=170&&image.Width==image.Height&&
        bounds.Width==image.Width&&bounds.Height==image.Height;
}

internal sealed class LiveMinimapMotionTracker
{
    private NavigationPixels? previous;
    private DateTimeOffset previousUtc;
    private IntPtr previousWindow;
    private Rectangle previousScreenBounds;

    internal void Reset(){previous=null;previousUtc=default;previousWindow=IntPtr.Zero;previousScreenBounds=default;}

    // No persistent frames, disk writes, world coordinates or mouse input.
    internal LiveMinimapMotion? Observe(VisualFrame frame)
    {
        if(frame.Pixels is not {WorldMapPanelOpen:false,Navigation:NavigationPixels pixels}||
            frame.Pixels.Compass is {North:false})
        {Reset();return null;}
        if(previous is null)
        {Remember(frame,pixels);return null;}
        if(frame.Window!=previousWindow||frame.ScreenBounds!=previousScreenBounds)
        {Remember(frame,pixels);return null;}
        if(frame.CapturedUtc<=previousUtc||frame.CapturedUtc-previousUtc<TimeSpan.FromMilliseconds(750))return null;
        if(frame.CapturedUtc-previousUtc>TimeSpan.FromSeconds(12))
        {Remember(frame,pixels);return null;}
        var result=LiveMinimapMotionVision.Compare(previous,pixels);
        Remember(frame,pixels);
        return result;
    }

    private void Remember(VisualFrame frame,NavigationPixels pixels)
    {previous=pixels;previousUtc=frame.CapturedUtc;previousWindow=frame.Window;previousScreenBounds=frame.ScreenBounds;}
}
