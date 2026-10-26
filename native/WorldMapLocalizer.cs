using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal static class WorldMapLocalizer
{
    // A large, geometrically consistent cluster can contain a few additional
    // unmatched UI labels. Only high-count matches may use the narrow 60–65%
    // margin; spread, orientation, image-edge agreement, uniqueness and two
    // independent map observations still have to pass separately.
    internal static bool ConsensusVerified(int inliers,int pairs)=>
        pairs>=12&&inliers>=12&&inliers<=pairs&&
        (inliers>=pairs*.65||(inliers>=30&&inliers>=pairs*.60));

    internal static WorldMapMatch Locate(WorldMapData data,VisualFrame frame)
    {
        if(frame.Pixels?.WorldMap is not WorldMapPixels pixels)return new(null,"WORLD_MAP_OR_MARKER_NOT_VERIFIED");
        using var image=Cv.Cv2.ImDecode(pixels.MapPng,Cv.ImreadModes.Grayscale);
        if(image.Empty()||image.Width!=pixels.ViewBounds.Width||image.Height!=pixels.ViewBounds.Height||
            !pixels.ViewBounds.Contains(pixels.PlayerMarker))return new(null,"WORLD_MAP_IMAGE_INVALID");
        using var mask=new Cv.Mat(image.Size(),Cv.MatType.CV_8UC1,Cv.Scalar.White);
        var marker=new Cv.Point(pixels.PlayerMarker.X-pixels.ViewBounds.Left,pixels.PlayerMarker.Y-pixels.ViewBounds.Top);
        Cv.Cv2.Circle(mask,marker,30,Cv.Scalar.Black,-1);
        Cv.Cv2.Rectangle(mask,new(image.Width-40,0,40,40),Cv.Scalar.Black,-1);
        // Small circular map symbols repeat at unrelated locations. Their
        // edges are UI decoration, not distinctive terrain landmarks.
        foreach(var icon in Cv.Cv2.HoughCircles(image,Cv.HoughModes.Gradient,1,10,100,12,4,10))
            Cv.Cv2.Circle(mask,new Cv.Point((int)Math.Round(icon.Center.X),(int)Math.Round(icon.Center.Y)),
                (int)Math.Ceiling(icon.Radius)+4,Cv.Scalar.Black,-1);
        using var sift=Cv.Features2D.SIFT.Create(1400,contrastThreshold:.009);
        using var descriptors=new Cv.Mat();sift.DetectAndCompute(image,mask,out var points,descriptors);
        if(points.Length<20||descriptors.Empty())return new(null,"WORLD_MAP_FEATURES_INSUFFICIENT",points.Length);
        using var matcher=new Cv.BFMatcher(Cv.NormTypes.L2);
        var accepted=new List<WorldMapPosition>();int bestPairs=0,bestInliers=0;string rejection="WORLD_MAP_NO_VERIFIED_MATCH";
        foreach(var reference in data.References.Where(r=>r.Plane==0))
        {
            if(reference.Descriptors.Rows<20)continue;
            // Require reciprocal distinctive matches. Map icons and labels
            // repeat across the atlas and can otherwise swamp real terrain
            // matches with one-way nearest neighbours.
            var reverse=matcher.KnnMatch(reference.Descriptors,descriptors,2)
                .Where(p=>p.Length==2&&p[0].Distance<.65*p[1].Distance)
                .ToDictionary(p=>p[0].QueryIdx,p=>p[0].TrainIdx);
            var pairs=matcher.KnnMatch(descriptors,reference.Descriptors,2)
                .Where(p=>p.Length==2&&p[0].Distance<.65*p[1].Distance&&
                    reverse.TryGetValue(p[0].TrainIdx,out int query)&&query==p[0].QueryIdx).Select(p=>p[0]).ToArray();
            bestPairs=Math.Max(bestPairs,pairs.Length);if(pairs.Length<12)continue;
            var from=pairs.Select(p=>points[p.QueryIdx].Pt).ToArray();
            var to=pairs.Select(p=>reference.Points[p.TrainIdx].Pt).ToArray();
            using var input=Cv.InputArray.Create(from);using var output=Cv.InputArray.Create(to);using var inliers=new Cv.Mat();
            using var fit=Cv.Cv2.EstimateAffinePartial2D(input,output,inliers,Cv.RobustEstimationAlgorithms.RANSAC,2,3000,.995,20);
            if(fit is null||fit.Empty())continue;
            int count=Cv.Cv2.CountNonZero(inliers);bestInliers=Math.Max(bestInliers,count);
            if(!ConsensusVerified(count,pairs.Length)){rejection="WORLD_MAP_CONSENSUS_NOT_VERIFIED";continue;}
            var spread=from.Where((_,i)=>inliers.At<byte>(i)!=0).ToArray();
            if(spread.Max(p=>p.X)-spread.Min(p=>p.X)<90||spread.Max(p=>p.Y)-spread.Min(p=>p.Y)<90)
            {rejection="WORLD_MAP_SPREAD_NOT_VERIFIED";continue;}
            double a=fit.At<double>(0,0),b=fit.At<double>(0,1),c=fit.At<double>(1,0),d=fit.At<double>(1,1);
            double scale=Math.Sqrt(a*a+c*c);
            // The surface world map is north-up; rotated/foreign map panels
            // cannot silently become a world-coordinate source.
            if(scale is <.35 or >8||a<=0||d<=0||Math.Abs(b)>scale*.04||Math.Abs(c)>scale*.04)
            {rejection="WORLD_MAP_ORIENTATION_NOT_VERIFIED";continue;}
            using var warped=new Cv.Mat();
            Cv.Cv2.WarpAffine(reference.Image,warped,fit,image.Size(),Cv.InterpolationFlags.Linear|Cv.InterpolationFlags.WarpInverseMap);
            if(!WorldNavigator.EdgesAgree(image,warped,mask)){rejection="WORLD_MAP_EDGES_NOT_VERIFIED";continue;}
            double x=a*marker.X+b*marker.Y+fit.At<double>(0,2),y=c*marker.X+d*marker.Y+fit.At<double>(1,2);
            var tile=new WorldTile(WorldMapData.MinX+(int)Math.Floor(x/4),WorldMapData.MaxY-1-(int)Math.Floor(y/4));
            if(!WorldMapData.Domain(tile))continue;
            accepted.Add(new(tile,count,frame.CapturedUtc));
        }
        if(accepted.Count!=1)return new(null,accepted.Count==0?rejection:"WORLD_MAP_MATCH_AMBIGUOUS",points.Length,bestPairs,bestInliers);
        return new(accepted[0],"WORLD_MAP_SURFACE_POSITION_VERIFIED",points.Length,bestPairs,bestInliers);
    }
}

internal sealed class WorldMapRoutine
{
    private readonly IVisualGameDriver driver;private readonly WorldNavigator navigator;
    private readonly Func<bool> stopped;private readonly Action<string> report;
    private readonly Func<int,CancellationToken,Task> wait;
    private readonly Func<VisualFrame,WorldMapMatch> locate;
    internal WorldMapRoutine(IVisualGameDriver driver,WorldNavigator navigator,Func<bool> stopped,
        Action<string> report,Func<int,CancellationToken,Task>? wait=null,Func<VisualFrame,WorldMapMatch>? locate=null)
    {this.driver=driver;this.navigator=navigator;this.stopped=stopped;this.report=report;this.wait=wait??Task.Delay;
        this.locate=locate??(frame=>WorldMapLocalizer.Locate(navigator.Data!,frame));}
    internal async Task<RoutineResult> Run(CancellationToken ct)
    {
        var window=driver as WindowVisualGameDriver;var previous=window?.InputGuard;
        if(window is not null)window.InputGuard=(frame,input)=>!stopped()&&
            (previous?.Invoke(frame,input)??true)&&WorldMapVision.InputAuthorized(frame,input);
        try{return await Execute(ct);}
        catch(OperationCanceledException) when(stopped()&&!ct.IsCancellationRequested)
        {return new(RoutineStatus.Waiting,"WORLD_MAP_PAUSED");}
        catch(RoutineBlockedException ex){return new(RoutineStatus.Blocked,"WORLD_MAP_"+ex.Message);}
        catch(Exception ex) when(ex is not OperationCanceledException)
        {return new(RoutineStatus.Blocked,"WORLD_MAP_VISION_ERROR_"+ex.GetType().Name);}
        finally{if(window is not null)window.InputGuard=previous;}
    }
    private async Task<RoutineResult> Execute(CancellationToken ct)
    {
        if(stopped())return new(RoutineStatus.Waiting,"WORLD_MAP_PAUSED");
        await navigator.Ready(report,ct);
        await driver.ObserveAsync(ct);
        if(driver.Current.Pixels?.WorldMapPanelOpen!=true)
        {
            var target=WorldMapVision.GlobeCandidate(driver.Current);
            if(target is null)return new(RoutineStatus.Blocked,"WORLD_MAP_GLOBE_CANDIDATE_MISSING");
            report($"WORLD_MAP_OPEN_CANDIDATE x={target.Value.Left+target.Value.Width/2} y={target.Value.Top+target.Value.Height/2} hover_only=true");
            await driver.InputAsync(new(GameInputKind.Hover,target.Value),ct);
            for(int attempt=0;attempt<3&&driver.Current.Pixels?.WorldMapButton is null&&!WorldMapVision.OpenTooltip(driver.Current,target.Value);attempt++)
            {await wait(250,ct);await driver.ObserveAsync(ct);}
            if(driver.Current.Pixels?.WorldMapButton is Rectangle freshButton)target=freshButton;
            else if(!WorldMapVision.OpenTooltip(driver.Current,target.Value))return new(RoutineStatus.Blocked,"WORLD_MAP_OPEN_CONTROL_NOT_VERIFIED");
            if(stopped())return new(RoutineStatus.Waiting,"WORLD_MAP_PAUSED");
            await driver.InputAsync(new(GameInputKind.LeftClick,target.Value),ct);
        }
        WorldMapPosition? previous=null,confirmed=null;
        var window=driver.Current.Window;var bounds=driver.Current.ScreenBounds;
        for(int sample=0;sample<4;sample++)
        {
            if(stopped())return new(RoutineStatus.Waiting,"WORLD_MAP_PAUSED");
            await wait(300,ct);await driver.ObserveAsync(ct);
            if(driver.Current.Window!=window||driver.Current.ScreenBounds!=bounds)
                return new(RoutineStatus.Blocked,"WORLD_MAP_WINDOW_CHANGED");
            var match=locate(driver.Current);
            report($"WORLD_MAP_SAMPLE sample={sample+1} reason={match.Reason} features={match.Features} matches={match.Matches} inliers={match.Inliers}");
            var position=match.Position;
            if(position is not null&&previous is not null&&position.ObservedUtc>previous.ObservedUtc&&
                position.SurfaceTile==previous.SurfaceTile)
            {confirmed=position;break;}
            previous=position;
        }
        // Close only an independently recognized panel, even on match failure.
        if(stopped())return new(RoutineStatus.Waiting,"WORLD_MAP_PAUSED");
        if(driver.Current.Pixels?.WorldMapPanelOpen==true)
        {
            if(driver.Current.Pixels.WorldMapCloseVisible!=true)
                return new(RoutineStatus.Blocked,"WORLD_MAP_CLOSE_CONTROL_NOT_VERIFIED");
            await driver.InputAsync(new(GameInputKind.LeftClick,WorldMapVision.Close),ct);
            int closed=0;DateTimeOffset last=default;
            for(int attempt=0;attempt<4&&closed<2;attempt++)
            {
                await wait(200,ct);await driver.ObserveAsync(ct);
                var frame=driver.Current;
                if(frame.CapturedUtc<=last)continue;last=frame.CapturedUtc;
                closed=frame.Pixels is {WorldMapPanelOpen:false,WorldMapCloseVisible:false}?closed+1:0;
            }
            if(closed<2)return new(RoutineStatus.Blocked,"WORLD_MAP_CLOSURE_NOT_CONFIRMED");
        }
        else return new(RoutineStatus.Blocked,"WORLD_MAP_CLOSURE_NOT_CONFIRMED");
        if(confirmed is null)return new(RoutineStatus.Blocked,"WORLD_MAP_POSITION_NOT_CONFIRMED");
        navigator.SetWorldMapHint(confirmed);
        report($"WORLD_MAP_POSITION_CONFIRMED surface_x={confirmed.SurfaceTile.X} surface_y={confirmed.SurfaceTile.Y} "+
            $"inliers={confirmed.Inliers} independent_frames=2 player_plane_verified=false");
        return new(RoutineStatus.Completed,"WORLD_MAP_SURFACE_POSITION_CONFIRMED");
    }
}
