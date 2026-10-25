using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal sealed record MinimapScaleEvidence(double? Scale,int Inliers,string Reason);

// Local geometry only: no client memory, saved screenshot, map-coordinate
// guess or assumption that PostMessage was accepted by the game renderer.
internal static class MinimapZoomVision
{
    internal static MinimapScaleEvidence Compare(NavigationPixels before,NavigationPixels after)
    {
        if(before.MapBounds!=after.MapBounds||before.MinimapPng.Length==0||after.MinimapPng.Length==0)
            return new(null,0,"MINIMAP_BOUNDS_OR_IMAGE_CHANGED");
        using var a=Cv.Cv2.ImDecode(before.MinimapPng,Cv.ImreadModes.Grayscale);
        using var b=Cv.Cv2.ImDecode(after.MinimapPng,Cv.ImreadModes.Grayscale);
        if(a.Empty()||b.Empty()||a.Width!=b.Width||a.Height!=b.Height||
            a.Width is <100 or >190||a.Width!=a.Height)
            return new(null,0,"MINIMAP_IMAGE_INVALID");
        using var mask=new Cv.Mat(a.Size(),Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        Cv.Cv2.Circle(mask,new(a.Width/2,a.Height/2),a.Width/2-15,Cv.Scalar.White,-1);
        Cv.Cv2.Circle(mask,new(a.Width/2,a.Height/2),14,Cv.Scalar.Black,-1);
        using var sift=Cv.Features2D.SIFT.Create(650,contrastThreshold:.009);
        using var da=new Cv.Mat();using var db=new Cv.Mat();
        sift.DetectAndCompute(a,mask,out var pa,da);
        sift.DetectAndCompute(b,mask,out var pb,db);
        if(pa.Length<12||pb.Length<12||da.Empty()||db.Empty())
            return new(null,0,"MINIMAP_ZOOM_FEATURES_INSUFFICIENT");
        using var matcher=new Cv.BFMatcher(Cv.NormTypes.L2);
        var matches=matcher.KnnMatch(da,db,2)
            .Where(pair=>pair.Length==2&&pair[0].Distance<.70*pair[1].Distance)
            .Select(pair=>pair[0]).ToArray();
        if(matches.Length<12)return new(null,0,"MINIMAP_ZOOM_MATCHES_INSUFFICIENT");
        var from=matches.Select(m=>pa[m.QueryIdx].Pt).ToArray();
        var to=matches.Select(m=>pb[m.TrainIdx].Pt).ToArray();
        using var input=Cv.InputArray.Create(from);using var output=Cv.InputArray.Create(to);
        using var inliers=new Cv.Mat();
        using var fit=Cv.Cv2.EstimateAffinePartial2D(input,output,inliers,
            Cv.RobustEstimationAlgorithms.RANSAC,2.5,3000,.995,20);
        if(fit is null||fit.Empty())return new(null,0,"MINIMAP_ZOOM_GEOMETRY_UNVERIFIED");
        int count=Cv.Cv2.CountNonZero(inliers);
        if(count<12||count<matches.Length*.60)
            return new(null,count,"MINIMAP_ZOOM_GEOMETRY_UNVERIFIED");
        var accepted=from.Where((_,i)=>inliers.At<byte>(i)!=0).ToArray();
        if(accepted.Max(p=>p.X)-accepted.Min(p=>p.X)<30||
            accepted.Max(p=>p.Y)-accepted.Min(p=>p.Y)<30)
            return new(null,count,"MINIMAP_ZOOM_FEATURES_TOO_CLUSTERED");
        double scale=Math.Sqrt(Math.Pow(fit.At<double>(0,0),2)+Math.Pow(fit.At<double>(1,0),2));
        return scale is >=.35 and <=2.8?
            new(scale,count,"VERIFIED_RELATIVE_SCALE"):
            new(null,count,"MINIMAP_ZOOM_SCALE_OUT_OF_RANGE");
    }
}

// The C++ OSRS minimap may zoom with the wheel, but the renderer can reject
// background wheel messages. Probe the render child first, and if it has no
// measurable effect, try the actual osclient.exe game-window HWND. Never
// assume that successful PostMessage means the game consumed the input.
// Require two final unchanged DOWN steps after a verified scale change.
internal sealed class MinimapZoomRoutine
{
    internal const int MaxDownSteps=24;
    private const double Tolerance=.018;
    private readonly IVisualGameDriver driver;
    private readonly Func<bool> stopped;
    private readonly Func<int,CancellationToken,Task> wait;
    private readonly Action<string> diagnostic;
    internal MinimapZoomRoutine(IVisualGameDriver driver,Func<bool> stopped,
        Func<int,CancellationToken,Task>? wait=null,Action<string>? diagnostic=null)
    {this.driver=driver;this.stopped=stopped;this.wait=wait??Task.Delay;
        this.diagnostic=diagnostic??(_=>{});}

    internal async Task<RoutineResult> Run(CancellationToken ct)
    {
        try{return await Execute(ct);}
        catch(OperationCanceledException) when(stopped()&&!ct.IsCancellationRequested)
        {return new(RoutineStatus.Waiting,"MINIMAP_ZOOM_PAUSED");}
        catch(RoutineBlockedException ex) when(stopped())
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
        if(!Usable(second)||second.CapturedUtc<=initial.CapturedUtc||
            second.Pixels!.Navigation!.MapBounds!=initial.Pixels!.Navigation!.MapBounds)
            return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_INDEPENDENT_BASELINE_NOT_VERIFIED");
        bool observedWheelEffect=false,gameWindowTarget=false;
        int unchanged=0,scrolls=0;
        for(int step=0;step<MaxDownSteps;step++)
        {
            if(stopped())return new(RoutineStatus.Waiting,"MINIMAP_ZOOM_PAUSED");
            var before=driver.Current;
            var down=await Scroll(before,gameWindowTarget?
                GameInputKind.WheelDownGameWindow:GameInputKind.WheelDown,ct);
            scrolls++;
            if(down.Scale is not double ratio)
                return new(RoutineStatus.Blocked,down.Reason,scrolls);
            if(ratio<1-Tolerance){observedWheelEffect=true;unchanged=0;continue;}
            if(ratio>1+Tolerance)
                return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED",scrolls);
            if(!observedWheelEffect)
            {
                // An ignored render-child wheel message and a fully zoomed-out
                // minimap look identical. Try the game HWND independently;
                // never double-post one wheel gesture to two targets.
                var gameWindowDown=await Scroll(driver.Current,GameInputKind.WheelDownGameWindow,ct);
                scrolls++;
                if(gameWindowDown.Scale is not double windowRatio)
                    return new(RoutineStatus.Blocked,gameWindowDown.Reason,scrolls);
                if(windowRatio<1-Tolerance)
                {observedWheelEffect=true;gameWindowTarget=true;unchanged=0;continue;}
                if(windowRatio>1+Tolerance)
                    return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED",scrolls);
                // Both DOWN routes failed to show movement. A single UP on
                // each route can distinguish the minimum zoom stop from an
                // ignored background wheel, without certifying either blindly.
                var up=await Scroll(driver.Current,GameInputKind.WheelUp,ct);
                if(up.Scale is not double upRatio)
                    return new(RoutineStatus.Blocked,up.Reason,scrolls);
                if(upRatio>1+Tolerance)
                {observedWheelEffect=true;unchanged=0;continue;}
                if(upRatio<1-Tolerance)
                    return new(RoutineStatus.Blocked,"MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED",scrolls);
                var windowUp=await Scroll(driver.Current,GameInputKind.WheelUpGameWindow,ct);
                if(windowUp.Scale is not double windowUpRatio)
                    return new(RoutineStatus.Blocked,windowUp.Reason,scrolls);
                if(windowUpRatio>1+Tolerance)
                {observedWheelEffect=true;gameWindowTarget=true;unchanged=0;continue;}
                return new(RoutineStatus.Blocked,windowUpRatio<1-Tolerance?
                    "MINIMAP_ZOOM_WHEEL_DIRECTION_UNEXPECTED":
                    "MINIMAP_ZOOM_BACKGROUND_WHEEL_NOT_OBSERVED",scrolls);
            }
            if(++unchanged>=2)
                return new(RoutineStatus.Completed,"MINIMAP_FULLY_ZOOMED_OUT_VISUALLY_CONFIRMED",scrolls);
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
        var after=await driver.ObserveAsync(ct);
        if(!Usable(after)||after.Window!=before.Window||after.ScreenBounds!=before.ScreenBounds||
            after.CapturedUtc<=before.CapturedUtc)
            return new(null,0,"MINIMAP_ZOOM_POST_SCROLL_FRAME_NOT_VERIFIED");
        var evidence=MinimapZoomVision.Compare(before.Pixels.Navigation,after.Pixels!.Navigation!);
        diagnostic($"input={direction} target={(direction is GameInputKind.WheelDownGameWindow or GameInputKind.WheelUpGameWindow?"GAME_WINDOW":"RENDER_CHILD")} "+
            $"scale={(evidence.Scale is double scale?scale.ToString("F4",System.Globalization.CultureInfo.InvariantCulture):"unknown")} "+
            $"inliers={evidence.Inliers} evidence={evidence.Reason}");
        return evidence;
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
