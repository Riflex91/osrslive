using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal sealed record MinimapScaleEvidence(double? Scale,int Inliers,string Reason,int RatioMatches=0,int FeatureSampling=1);

// Local geometry only: no client memory, saved screenshot, map-coordinate
// guess or assumption that PostMessage was accepted by the game renderer.
internal static class MinimapZoomVision
{
    internal static MinimapScaleEvidence Compare(NavigationPixels before,NavigationPixels after)
    {
        if(before.MinimapPng.Length==0||after.MinimapPng.Length==0)
            return new(null,0,"MINIMAP_BOUNDS_OR_IMAGE_CHANGED");
        using var rawA=Cv.Cv2.ImDecode(before.MinimapPng,Cv.ImreadModes.Grayscale);
        using var rawB=Cv.Cv2.ImDecode(after.MinimapPng,Cv.ImreadModes.Grayscale);
        if(!ImageMatchesBounds(rawA,before.MapBounds)||!ImageMatchesBounds(rawB,after.MapBounds))
            return new(null,0,"MINIMAP_IMAGE_INVALID");
        var prior=before.MapBounds;var next=after.MapBounds;
        if(Math.Abs(prior.Left+prior.Width/2d-next.Left-next.Width/2d)>4||
            Math.Abs(prior.Top+prior.Height/2d-next.Top-next.Height/2d)>4)
            return new(null,0,"MINIMAP_BOUNDS_OR_IMAGE_CHANGED");
        // Hough's border estimate can move a few pixels when terrain zooms.
        // Compare the SAME screen pixels in both independent crops. Never
        // resize one crop to the other: that would invent a zoom effect.
        var overlap=Rectangle.Intersect(prior,next);
        int side=Math.Min(overlap.Width,overlap.Height);
        if(side<116)return new(null,0,"MINIMAP_BOUNDS_OR_IMAGE_CHANGED");
        var common=new Rectangle(overlap.Left+(overlap.Width-side)/2,overlap.Top+(overlap.Height-side)/2,side,side);
        using var a=new Cv.Mat(rawA,new Cv.Rect(common.Left-prior.Left,common.Top-prior.Top,side,side));
        using var b=new Cv.Mat(rawB,new Cv.Rect(common.Left-next.Left,common.Top-next.Top,side,side));
        using var mask=new Cv.Mat(a.Size(),Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        Cv.Cv2.Circle(mask,new(a.Width/2,a.Height/2),a.Width/2-15,Cv.Scalar.White,-1);
        Cv.Cv2.Circle(mask,new(a.Width/2,a.Height/2),14,Cv.Scalar.Black,-1);
        var native=ExtractScale(a,b,mask,1);
        if(native.Scale is not null&&native.Inliers>=12)return native;
        // Tiny native patches lose stable keypoints at large zoom steps.
        // Upsample BOTH images equally, then return every keypoint to native
        // coordinates before RANSAC, spread and reprojection checks.
        using var enlargedA=new Cv.Mat();using var enlargedB=new Cv.Mat();using var enlargedMask=new Cv.Mat();
        Cv.Cv2.Resize(a,enlargedA,new(a.Width*2,a.Height*2),interpolation:Cv.InterpolationFlags.Linear);
        Cv.Cv2.Resize(b,enlargedB,new(b.Width*2,b.Height*2),interpolation:Cv.InterpolationFlags.Linear);
        Cv.Cv2.Resize(mask,enlargedMask,enlargedA.Size(),interpolation:Cv.InterpolationFlags.Nearest);
        var enlarged=ExtractScale(enlargedA,enlargedB,enlargedMask,2);
        if(native.Scale is double first&&enlarged.Scale is double second&&Math.Abs(first-second)>.035)
            return new(null,Math.Max(native.Inliers,enlarged.Inliers),"MINIMAP_ZOOM_SAMPLING_DISAGREEMENT");
        return enlarged.Scale is not null&&(native.Scale is null||enlarged.Inliers>native.Inliers)?enlarged:native;
    }
    internal static MinimapScaleEvidence ExtractScale(Cv.Mat a,Cv.Mat b,Cv.Mat mask,int sampling)
    {
        using var sift=Cv.Features2D.SIFT.Create(650,contrastThreshold:.009);
        using var da=new Cv.Mat();using var db=new Cv.Mat();
        sift.DetectAndCompute(a,mask,out var pa,da);
        sift.DetectAndCompute(b,mask,out var pb,db);
        if(pa.Length<12||pb.Length<12||da.Empty()||db.Empty())
            return new(null,0,"MINIMAP_ZOOM_FEATURES_INSUFFICIENT");
        using var matcher=new Cv.BFMatcher(Cv.NormTypes.L2);
        var forward=matcher.KnnMatch(da,db,2)
            .Where(pair=>pair.Length==2&&pair[0].Distance<.70*pair[1].Distance)
            .Select(pair=>(Before:pair[0].QueryIdx,After:pair[0].TrainIdx));
        var reverse=matcher.KnnMatch(db,da,2)
            .Where(pair=>pair.Length==2&&pair[0].Distance<.70*pair[1].Distance)
            .Select(pair=>(Before:pair[0].TrainIdx,After:pair[0].QueryIdx));
        // Ratio matching is asymmetric under substantial zoom. Recover
        // equally strict reverse matches, count each correspondence once,
        // and reject conflicting identities instead of duplicating inliers.
        var matches=UniqueCorrespondences(forward.Concat(reverse));
        var from=matches.Select(m=>new Cv.Point2f(pa[m.Before].Pt.X/sampling,pa[m.Before].Pt.Y/sampling)).ToArray();
        var to=matches.Select(m=>new Cv.Point2f(pb[m.After].Pt.X/sampling,pb[m.After].Pt.Y/sampling)).ToArray();
        return MeasureScale(from,to) with{FeatureSampling=sampling};
    }
    internal static (int Before,int After)[] UniqueCorrespondences(IEnumerable<(int Before,int After)> candidates)
    {
        var pairs=candidates.Distinct().ToArray();
        var before=pairs.GroupBy(p=>p.Before).ToDictionary(g=>g.Key,g=>g.Count());
        var after=pairs.GroupBy(p=>p.After).ToDictionary(g=>g.Key,g=>g.Count());
        return pairs.Where(p=>before[p.Before]==1&&after[p.After]==1).ToArray();
    }
    private static bool ImageMatchesBounds(Cv.Mat image,Rectangle bounds)=>
        !image.Empty()&&image.Width is >=124 and <=170&&image.Width==image.Height&&
        image.Width==bounds.Width&&image.Height==bounds.Height;
    internal static MinimapScaleEvidence MeasureScale(Cv.Point2f[] from,Cv.Point2f[] to)
    {
        // Ten/eleven matches can yield the marginal geometry supported below.
        // They still need >=10 inliers AND a second independent measurement;
        // a twelve-match prefilter would discard them before that check.
        int matches=from.Length;
        if(matches!=to.Length)return new(null,0,"MINIMAP_ZOOM_CORRESPONDENCES_INVALID",matches);
        if(matches<10)return new(null,0,"MINIMAP_ZOOM_MATCHES_INSUFFICIENT",matches);
        using var input=Cv.InputArray.Create(from);using var output=Cv.InputArray.Create(to);
        using var inliers=new Cv.Mat();
        using var fit=Cv.Cv2.EstimateAffinePartial2D(input,output,inliers,
            Cv.RobustEstimationAlgorithms.RANSAC,2.5,3000,.995,20);
        if(fit is null||fit.Empty())return new(null,0,"MINIMAP_ZOOM_GEOMETRY_UNVERIFIED",matches);
        int count=Cv.Cv2.CountNonZero(inliers);
        // Ten or eleven spatially distributed inliers may be useful, but are
        // *not* enough to authorize a scroll decision in isolation. Scroll()
        // requires an independent second frame with the same measured scale.
        if(count<10||count<matches*.60)
            return new(null,count,"MINIMAP_ZOOM_GEOMETRY_UNVERIFIED",matches);
        var accepted=from.Where((_,i)=>inliers.At<byte>(i)!=0).ToArray();
        if(accepted.Max(p=>p.X)-accepted.Min(p=>p.X)<30||
            accepted.Max(p=>p.Y)-accepted.Min(p=>p.Y)<30)
            return new(null,count,"MINIMAP_ZOOM_FEATURES_TOO_CLUSTERED",matches);
        double scale=Math.Sqrt(Math.Pow(fit.At<double>(0,0),2)+Math.Pow(fit.At<double>(1,0),2));
        return scale is >=.35 and <=2.8?
            new(scale,count,"VERIFIED_RELATIVE_SCALE",matches):
            new(null,count,"MINIMAP_ZOOM_SCALE_OUT_OF_RANGE",matches);
    }

    internal static bool ConfirmMarginal(MinimapScaleEvidence first,MinimapScaleEvidence second)=>
        first.Scale is double prior&&second.Scale is double current&&
        first.Inliers>=10&&second.Inliers>=10&&
        Math.Abs(prior-current)<=.035;
}

// The C++ OSRS minimap may zoom with the wheel, but the renderer can reject
// background wheel messages. Probe the render child first, and if it has no
// measurable effect, try the actual osclient.exe game-window HWND. Never
// assume that successful PostMessage means the game consumed the input.
// Require two final unchanged steps toward the requested limit after a
// verified scale change. Live navigation requests the maximum detail/zoom.
internal sealed class MinimapZoomRoutine
{
    internal const int MaxSteps=24;
    private const double Tolerance=.018;
    private readonly IVisualGameDriver driver;
    private readonly Func<bool> stopped;
    private readonly Func<int,CancellationToken,Task> wait;
    private readonly Action<string> diagnostic;
    private readonly bool zoomIn;
    internal MinimapZoomRoutine(IVisualGameDriver driver,Func<bool> stopped,
        Func<int,CancellationToken,Task>? wait=null,Action<string>? diagnostic=null,bool zoomIn=false)
    {this.driver=driver;this.stopped=stopped;this.wait=wait??Task.Delay;
        this.diagnostic=diagnostic??(_=>{});this.zoomIn=zoomIn;}
    private GameInputKind Direction(bool forward,bool gameWindow)=> (zoomIn==forward,gameWindow) switch
    {
        (true,true)=>GameInputKind.WheelUpGameWindow,(true,false)=>GameInputKind.WheelUp,
        (false,true)=>GameInputKind.WheelDownGameWindow,_=>GameInputKind.WheelDown
    };
    // Normalize the geometry only for the shared decision logic. Diagnostics
    // continue reporting the actual measured scale and physical wheel input.
    private double Toward(double ratio)=>zoomIn?1/ratio:ratio;

    internal async Task<RoutineResult> Run(CancellationToken ct)
    {
        try{return await Execute(ct);}
        catch(OperationCanceledException) when(stopped()&&!ct.IsCancellationRequested)
        {return new(RoutineStatus.Waiting,"MINIMAP_ZOOM_PAUSED");}
        catch(RoutineBlockedException) when(stopped())
        {return new(RoutineStatus.Waiting,"MINIMAP_ZOOM_PAUSED");}
        catch(RoutineBlockedException ex)
        {return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_INPUT_OR_FRAME_"+ex.Message);}
        catch(Exception ex) when(ex is not OperationCanceledException)
        {return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_VISION_ERROR_"+ex.GetType().Name);}
    }
    private async Task<RoutineResult> Execute(CancellationToken ct)
    {
        if(stopped())return new(RoutineStatus.Waiting,"MINIMAP_ZOOM_PAUSED");
        // Require an actual recognized game screen and a fresh second frame
        // with the same unique pixel-backed minimap, before any scroll.
        var initial=driver.Current;
        for(int attempt=0;attempt<2&&!Usable(initial);attempt++)
        {
            await wait(300,ct);
            initial=await driver.ObserveAsync(ct);
        }
        if(!Usable(initial))return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_NATIVE_HUD_OR_MAP_NOT_VERIFIED");
        await wait(280,ct);
        var second=await driver.ObserveAsync(ct);
        if(!Usable(second)||second.Window!=initial.Window||second.ScreenBounds!=initial.ScreenBounds||
            second.CapturedUtc<=initial.CapturedUtc||
            second.Pixels!.Navigation!.MapBounds!=initial.Pixels!.Navigation!.MapBounds)
            return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_INDEPENDENT_BASELINE_NOT_VERIFIED");
        bool observedWheelEffect=false,gameWindowTarget=false;
        int unchanged=0,scrolls=0;
        diagnostic("requested_limit="+(zoomIn?"MAXIMUM_ZOOM_IN":"MINIMUM_ZOOM_OUT"));
        for(int step=0;step<MaxSteps;step++)
        {
            if(stopped())return new(RoutineStatus.Waiting,"MINIMAP_ZOOM_PAUSED");
            var before=driver.Current;
            var down=await Scroll(before,Direction(true,gameWindowTarget),ct);
            scrolls++;
            if(down.Scale is not double observedRatio)
                return new(RoutineStatus.Blocked,down.Reason,scrolls);
            double ratio=Toward(observedRatio);
            if(ratio<1-Tolerance){observedWheelEffect=true;unchanged=0;continue;}
            if(ratio>1+Tolerance)
                return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED",scrolls);
            if(!observedWheelEffect)
            {
                // An ignored render-child wheel message and a zoom limit
                // minimap look identical. Try the game HWND independently;
                // never double-post one wheel gesture to two targets.
                var gameWindowDown=await Scroll(driver.Current,Direction(true,true),ct);
                scrolls++;
                if(gameWindowDown.Scale is not double observedWindowRatio)
                    return new(RoutineStatus.Blocked,gameWindowDown.Reason,scrolls);
                double windowRatio=Toward(observedWindowRatio);
                if(windowRatio<1-Tolerance)
                {observedWheelEffect=true;gameWindowTarget=true;unchanged=0;continue;}
                if(windowRatio>1+Tolerance)
                    return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED",scrolls);
                // Both forward routes failed to show movement. One reverse
                // probe on each route distinguishes the requested limit from an
                // ignored background wheel, without certifying either blindly.
                var up=await Scroll(driver.Current,Direction(false,false),ct);
                if(up.Scale is not double observedUpRatio)
                    return new(RoutineStatus.Blocked,up.Reason,scrolls);
                double upRatio=Toward(observedUpRatio);
                if(upRatio>1+Tolerance)
                {observedWheelEffect=true;unchanged=0;continue;}
                if(upRatio<1-Tolerance)
                    return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED",scrolls);
                var windowUp=await Scroll(driver.Current,Direction(false,true),ct);
                if(windowUp.Scale is not double observedWindowUpRatio)
                    return new(RoutineStatus.Blocked,windowUp.Reason,scrolls);
                double windowUpRatio=Toward(observedWindowUpRatio);
                if(windowUpRatio>1+Tolerance)
                {observedWheelEffect=true;gameWindowTarget=true;unchanged=0;continue;}
                return new(RoutineStatus.Blocked,windowUpRatio<1-Tolerance?
                    "MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED":
                    "MINIMAP_ZOOM_BACKGROUND_WHEEL_NOT_OBSERVED",scrolls);
            }
            if(++unchanged>=2)
                return new(RoutineStatus.Completed,zoomIn?"MINIMAP_FULLY_ZOOMED_IN_VISUALLY_CONFIRMED":
                    "MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED",scrolls);
        }
        return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_LIMIT_NOT_REACHED",scrolls);
    }
    private async Task<MinimapScaleEvidence> Scroll(VisualFrame before,GameInputKind direction,CancellationToken ct)
    {
        if(!Usable(before))return new(null,0,"MINIMAP_ZOOM_NATIVE_HUD_OR_MAP_NOT_VERIFIED");
        var map=before.Pixels!.Navigation!.MapBounds;
        var centre=new Rectangle(map.Left+map.Width/2-2,map.Top+map.Height/2-2,5,5);
        await driver.InputAsync(new(direction,centre),ct);
        if(stopped())throw new OperationCanceledException(ct);
        await wait(250,ct);
        MinimapScaleEvidence? tentative=null,last=null;
        string? rejectedFrame=null;
        DateTimeOffset lastCapture=before.CapturedUtc;
        for(int sample=1;sample<=3;sample++)
        {
            if(stopped())throw new OperationCanceledException(ct);
            var after=await driver.ObserveAsync(ct);
            string? frameReason=FrameRejection(before,after,lastCapture);
            if(frameReason is not null)
            {
                rejectedFrame=frameReason;
                diagnostic($"input={direction} sample={sample} frame_rejected={frameReason} "+
                    $"stage={GameScreenReader.Read(after).Stage} native_hud={after.Labels.Any(l=>l.Kind==VisualLabelKind.GameHud)} "+
                    $"navigation_available={after.Pixels?.Navigation is not null} "+
                    $"same_window={after.Window==before.Window} same_bounds={after.ScreenBounds==before.ScreenBounds} "+
                    $"capture_delta_ms={(after.CapturedUtc-before.CapturedUtc).TotalMilliseconds:F0} "+
                    $"frame_age_ms={(DateTimeOffset.UtcNow-after.CapturedUtc).TotalMilliseconds:F0} passive_only=true");
                // A changed target or a recognized login/disconnect screen is
                // a session boundary, not a transient missing minimap.
                if(frameReason is "WINDOW_CHANGED" or "SCREEN_BOUNDS_CHANGED" or "SESSION_LEFT_GAME")
                    return new(null,0,"MINIMAP_ZOOM_POST_SCROLL_"+frameReason);
                tentative=null;
                if(after.CapturedUtc>lastCapture)lastCapture=after.CapturedUtc;
                if(sample<3)await wait(200,ct);
                continue;
            }
            rejectedFrame=null;
            lastCapture=after.CapturedUtc;
            var afterMap=after.Pixels!.Navigation!;
            var evidence=MinimapZoomVision.Compare(before.Pixels.Navigation,afterMap);
            last=evidence;
            diagnostic($"input={direction} target={(direction is GameInputKind.WheelDownGameWindow or GameInputKind.WheelUpGameWindow?"GAME_WINDOW":"RENDER_CHILD")} "+
                $"sample={sample} scale={(evidence.Scale is double scale?scale.ToString("F4",System.Globalization.CultureInfo.InvariantCulture):"unknown")} "+
                $"inliers={evidence.Inliers} matches={evidence.RatioMatches} sampling={evidence.FeatureSampling} evidence={evidence.Reason} "+
                $"before_bounds={map.X},{map.Y},{map.Width} after_bounds={afterMap.MapBounds.X},{afterMap.MapBounds.Y},{afterMap.MapBounds.Width}");
            if(evidence.Scale is not null)
            {
                if(evidence.Inliers>=12||
                    (tentative is not null&&MinimapZoomVision.ConfirmMarginal(tentative,evidence)))
                    return evidence;
                tentative=evidence;
            }
            else tentative=null;
            if(sample<3)await wait(200,ct);
        }
        return rejectedFrame is not null?
            new(null,0,"MINIMAP_ZOOM_POST_SCROLL_"+rejectedFrame):tentative is not null?
            new(null,tentative.Inliers,"MINIMAP_ZOOM_MARGINAL_GEOMETRY_NOT_RECONFIRMED",tentative.RatioMatches):
            last??new(null,0,"MINIMAP_ZOOM_NO_FRESH_COMPARISON");
    }
    private static string? FrameRejection(VisualFrame before,VisualFrame after,DateTimeOffset lastCapture)
    {
        if(after.Window!=before.Window)return "WINDOW_CHANGED";
        if(after.ScreenBounds!=before.ScreenBounds)return "SCREEN_BOUNDS_CHANGED";
        var stage=GameScreenReader.Read(after).Stage;
        if(stage is not (VisibleGameStage.InGame or VisibleGameStage.Unknown))return "SESSION_LEFT_GAME";
        if(after.CapturedUtc<=lastCapture)return "CAPTURE_NOT_INDEPENDENT";
        if(stage!=VisibleGameStage.InGame)return "GAME_SCREEN_UNVERIFIED";
        if(!after.Labels.Any(l=>l.Kind==VisualLabelKind.GameHud))return "NATIVE_HUD_MISSING";
        if(after.Pixels?.Navigation is null)return "MINIMAP_MISSING";
        return Usable(after)?null:"MINIMAP_BOUNDS_INVALID";
    }
    private static bool Usable(VisualFrame f)=>
        f.ScreenBounds.Size==DockedWindowLayout.GameSize&&
        GameScreenReader.Read(f).Stage==VisibleGameStage.InGame&&
        f.Labels.Any(l=>l.Kind==VisualLabelKind.GameHud)&&
        f.Pixels?.Navigation is NavigationPixels map&&
        map.MapBounds is {Width:>=124 and <=170,Height:>=124 and <=170}&&
        map.MapBounds.Width==map.MapBounds.Height&&
        new Rectangle(Point.Empty,DockedWindowLayout.GameSize).Contains(map.MapBounds);
}
