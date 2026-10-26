using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal static class LiveMinimapMotionTests
{
    private static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Live minimap motion: "+message);}

    internal static void Run()
    {
        using var landscape=Landscape(42);
        using var moved=Shift(landscape,-7,9);
        var bounds=new Rectangle(830,10,144,144);
        NavigationPixels Render(Cv.Mat image,Rectangle? region=null)=>
            new(image.ToBytes(".png"),region??bounds,25);
        var first=Render(landscape);
        var second=Render(moved);
        var motion=LiveMinimapMotionVision.Compare(first,second);
        Require(motion.Reason=="VERIFIED_VISUAL_SHIFT_ONLY"&&motion.Inliers>=9&&
            motion.ShiftX is double x&&Math.Abs(x+7)<1.5&&
            motion.ShiftY is double y&&Math.Abs(y-9)<1.5,
            "same-render terrain translation must preserve direction and native pixel scale: "+motion);
        var stationary=LiveMinimapMotionVision.Compare(first,first);
        Require(stationary.Reason=="VERIFIED_VISUAL_SHIFT_ONLY"&&
            Math.Abs(stationary.ShiftX!.Value)<.6&&Math.Abs(stationary.ShiftY!.Value)<.6,
            "same position yields near-zero terrain displacement");
        // Hough finds the very same stationary HUD scene at slightly
        // different sizes/offsets (150px -> 144px). A crop-local affine can
        // correctly fit +5px while the *actual* viewport motion is zero.
        using(var scene=Landscape(91,176))
        using(var cropA=new Cv.Mat(scene,new Cv.Rect(4,4,150,150)))
        using(var cropB=new Cv.Mat(scene,new Cv.Rect(7,2,144,144)))
        {
            var a=Render(cropA,new(830,10,150,150));
            var b=Render(cropB,new(833,8,144,144));
            var stable=LiveMinimapMotionVision.Compare(a,b);
            Require(stable.Reason=="VERIFIED_VISUAL_SHIFT_ONLY"&&
                stable.ShiftX is double sx&&Math.Abs(sx)<1.2&&
                stable.ShiftY is double sy&&Math.Abs(sy)<1.2,
                "changed map circle crop must not produce fabricated player movement: "+stable);
        }
        // Independent large translated windows approximate the user's real
        // manual route (+49,+42 terrain pixels). The 144px circle can retain
        // enough distinctive overlap for SIFT plus its separate edge gate.
        using(var panorama=Landscape(177,300))
        using(var beforeWalk=new Cv.Mat(panorama,new Cv.Rect(100,100,144,144)))
        using(var afterWalk=new Cv.Mat(panorama,new Cv.Rect(51,58,144,144)))
        {
            var walked=LiveMinimapMotionVision.Compare(Render(beforeWalk),Render(afterWalk));
            Require(walked.Reason=="VERIFIED_VISUAL_SHIFT_ONLY"&&
                walked.ShiftX is double wx&&Math.Abs(wx-49)<2&&
                walked.ShiftY is double wy&&Math.Abs(wy-42)<2,
                "large but overlapping manual movement retains its observed direction: "+walked);
        }
        using var unrelated=Landscape(2026);
        Require(LiveMinimapMotionVision.Compare(first,Render(unrelated)).ShiftX is null,
            "unrelated scenery must not yield a movement receipt");
        using var blank=new Cv.Mat(144,144,Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        Require(LiveMinimapMotionVision.Compare(first,Render(blank)).ShiftX is null,
            "blank frames must not yield movement");
        Require(LiveMinimapMotionVision.Compare(first,Render(moved,new(760,10,144,144))).ShiftX is null,
            "a changed HUD position cannot be interpreted as terrain motion");
        using var zoom=Zoom(landscape,1.20);
        Require(LiveMinimapMotionVision.Compare(first,Render(zoom)).ShiftX is null,
            "zoom must not be mistaken for walking");

        var tracker=new LiveMinimapMotionTracker();
        VisualFrame Frame(NavigationPixels pixels,DateTimeOffset at,bool panel=false)=>
            new(new IntPtr(1),new(0,0,1000,600),at,Array.Empty<VisualLabel>(),
                new(Array.Empty<ObservedSlot>(),0,pixels,WorldMapPanelOpen:panel));
        var now=DateTimeOffset.UtcNow;
        Require(tracker.Observe(Frame(first,now)) is null&&
            tracker.Observe(Frame(second,now)) is null,
            "a repeated screenshot timestamp is never independent evidence");
        Require(tracker.Observe(Frame(second,now.AddSeconds(1)))?.Reason=="VERIFIED_VISUAL_SHIFT_ONLY",
            "two distinct in-session samples are required");
        Require(tracker.Observe(Frame(second,now.AddSeconds(2),panel:true)) is null&&
            tracker.Observe(Frame(second,now.AddSeconds(3))) is null,
            "opening the world map invalidates the live-minimap baseline");
        tracker.Reset();
        Require(tracker.Observe(Frame(second,now.AddSeconds(4))) is null,
            "session reset must discard previous image bytes");
        Require(tracker.Observe(new VisualFrame(new IntPtr(1),new(40,0,1000,600),now.AddSeconds(5),
            Array.Empty<VisualLabel>(),new(Array.Empty<ObservedSlot>(),0,second))) is null,
            "moved game viewport must reset the motion baseline instead of reporting a shift");
        // The 11.25 px/tile value comes from one real manually walked route.
        // It may annotate *relative* distance only after full world-map,
        // compass and zoom confirmation. It must never yield WorldTile.
        tracker.Reset();
        Require(tracker.Observe(Frame(first,now.AddSeconds(10)),provisionalScaleConfirmed:true) is null&&
            tracker.ProvisionalDisplacementTiles is null,
            "a known screenshot only establishes a baseline, not movement");
        Require(tracker.Observe(Frame(second,now.AddSeconds(11)),provisionalScaleConfirmed:true)?.ShiftX is double&&
            tracker.ProvisionalDisplacementTiles is {} estimate&&
            Math.Abs(estimate.East-7d/LiveMinimapMotionTracker.ProvisionalPixelsPerTile)<.15&&
            Math.Abs(estimate.North-9d/LiveMinimapMotionTracker.ProvisionalPixelsPerTile)<.15&&
            tracker.DisplacementSamples==1,
            "observed terrain displacement becomes explicitly provisional north/east distance");
        Require(tracker.Observe(Frame(second,now.AddSeconds(12)),provisionalScaleConfirmed:false) is null&&
            tracker.ProvisionalDisplacementTiles is null,
            "lost zoom/compass/world-map permission revokes even the provisional distance");
        tracker.Reset();
        tracker.Observe(Frame(first,now.AddSeconds(20)),provisionalScaleConfirmed:true);
        tracker.Observe(Frame(second,now.AddSeconds(21)),provisionalScaleConfirmed:true);
        Require(tracker.ProvisionalDisplacementTiles is not null&&
            tracker.Observe(Frame(second,now.AddSeconds(22),panel:true),provisionalScaleConfirmed:true) is null&&
            tracker.ProvisionalDisplacementTiles is null,
            "opening a world map revokes the relative movement chain");
        tracker.Observe(Frame(first,now.AddSeconds(30)),provisionalScaleConfirmed:true);
        Require(tracker.Observe(Frame(second,now.AddSeconds(44)),provisionalScaleConfirmed:true) is null&&
            tracker.ProvisionalDisplacementTiles is null,
            "a capture gap longer than twelve seconds cannot silently bridge movement");
        Console.WriteLine("LIVE MINIMAP MOTION PASS: reciprocal terrain tracking, crop-origin compensation, layout/zoom guards, provisional read-only distance resets and no world-position claim");
    }

    private static Cv.Mat Landscape(int seed,int side=144)
    {
        var image=new Cv.Mat(side,side,Cv.MatType.CV_8UC1,new Cv.Scalar(74));
        var rng=new Random(seed);
        for(int i=0;i<280;i++)
        {
            var point=new Cv.Point(rng.Next(12,side-12),rng.Next(12,side-12));
            var tint=new Cv.Scalar(rng.Next(20,220));
            if(i%3==0)
                Cv.Cv2.Line(image,point,new Cv.Point(point.X+rng.Next(-5,6),point.Y+rng.Next(-5,6)),tint,1);
            else Cv.Cv2.Circle(image,point,rng.Next(1,4),tint,-1);
        }
        return image;
    }

    private static Cv.Mat Shift(Cv.Mat image,double x,double y)
    {
        using var fit=new Cv.Mat(2,3,Cv.MatType.CV_64FC1);
        fit.Set(0,0,1d);fit.Set(0,1,0d);fit.Set(0,2,x);
        fit.Set(1,0,0d);fit.Set(1,1,1d);fit.Set(1,2,y);
        var shifted=new Cv.Mat();Cv.Cv2.WarpAffine(image,shifted,fit,image.Size());return shifted;
    }

    private static Cv.Mat Zoom(Cv.Mat image,double scale)
    {
        using var fit=new Cv.Mat(2,3,Cv.MatType.CV_64FC1);
        fit.Set(0,0,scale);fit.Set(0,1,0d);fit.Set(0,2,(1-scale)*image.Width/2d);
        fit.Set(1,0,0d);fit.Set(1,1,scale);fit.Set(1,2,(1-scale)*image.Height/2d);
        var scaled=new Cv.Mat();Cv.Cv2.WarpAffine(image,scaled,fit,image.Size());return scaled;
    }
}
