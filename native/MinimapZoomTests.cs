using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal static class MinimapZoomTests
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        static void Require(bool condition,string message)
        {if(!condition)throw new Exception("Minimap zoom: "+message);}
        // Distinct stationary terrain corners retain enough native features
        // for a geometrically verified scale. No actual OSRS account/input.
        using var texture=new Cv.Mat(512,512,Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        var random=new Random(11743);
        for(int n=0;n<1800;n++)
        {
            var p=new Cv.Point(random.Next(512),random.Next(512));
            var tone=new Cv.Scalar(random.Next(45,240));
            Cv.Cv2.Circle(texture,p,random.Next(1,5),tone,-1);
        }
        var zoom=new[]{.60,.72,.87,1.05,1.25};
        var images=new List<byte[]>();
        foreach(double factor in zoom)
        {
            using var affine=new Cv.Mat(2,3,Cv.MatType.CV_64FC1);
            affine.Set(0,0,1/factor);affine.Set(0,1,0);affine.Set(0,2,256-75/factor);
            affine.Set(1,0,0);affine.Set(1,1,1/factor);affine.Set(1,2,256-75/factor);
            using var image=new Cv.Mat();
            Cv.Cv2.WarpAffine(texture,image,affine,new(150,150),
                Cv.InterpolationFlags.Linear|Cv.InterpolationFlags.WarpInverseMap);
            images.Add(image.ToBytes(".png"));
        }
        VisualFrame Frame(int level)=>new(new IntPtr(5),new Rectangle(0,0,1000,600),
            DateTimeOffset.UtcNow,new[]{new VisualLabel("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud)},
            new(Array.Empty<ObservedSlot>(),0,new(images[level],new(830,10,150,150),24),Hitpoints:24));
        var outward=MinimapZoomVision.Compare(Frame(3).Pixels!.Navigation!,Frame(2).Pixels!.Navigation!);
        Require(outward.Scale is double less&&less<.982&&outward.Inliers>=10,
            "zoom-out is geometrically measurable with at least ten distributed inliers; marginal results still need a second frame");
        var stationary=MinimapZoomVision.Compare(Frame(0).Pixels!.Navigation!,Frame(0).Pixels!.Navigation!);
        Require(stationary.Scale is double same&&Math.Abs(same-1)<.018,
            "unchanged terrain does not count as an outward zoom step");
        Require(MinimapZoomVision.ConfirmMarginal(
                new(.855,11,"VERIFIED_RELATIVE_SCALE",16),
                new(.863,10,"VERIFIED_RELATIVE_SCALE",15))&&
            !MinimapZoomVision.ConfirmMarginal(
                new(.85,11,"VERIFIED_RELATIVE_SCALE",16),
                new(.97,10,"VERIFIED_RELATIVE_SCALE",15))&&
            !MinimapZoomVision.ConfirmMarginal(
                new(.85,9,"VERIFIED_RELATIVE_SCALE",16),
                new(.85,10,"VERIFIED_RELATIVE_SCALE",15)),
            "marginal ten-to-eleven-inlier geometry requires two consistent independent measurements");
        using(var blank=new Cv.Mat(150,150,Cv.MatType.CV_8UC1,Cv.Scalar.Black))
        {
            var invalid=Frame(0).Pixels!.Navigation! with{MinimapPng=blank.ToBytes(".png")};
            Require(MinimapZoomVision.Compare(invalid,invalid).Scale is null,
                "blank minimaps cannot certify a zoom limit");
        }
        var fromMiddle=new ZoomDriver(images,3);
        var completed=await new MinimapZoomRoutine(fromMiddle,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(completed.Status==RoutineStatus.Completed&&fromMiddle.Level==0&&
            fromMiddle.WheelDown>=5&&fromMiddle.OtherInputs==0,
            "changed scale followed by two ineffective downward notches verifies the lower limit without clicks");
        var transient=new ZoomDriver(images,3,corruptFirstPostInputFrame:true);
        var transientLog=new List<string>();
        var retried=await new MinimapZoomRoutine(transient,()=>false,
            (_,_)=>Task.CompletedTask,transientLog.Add).Run(CancellationToken.None);
        Require(retried.Status==RoutineStatus.Completed&&transient.Level==0&&
            transientLog.Any(line=>line.Contains("sample=1")&&line.Contains("scale=unknown"))&&
            transientLog.Any(line=>line.Contains("sample=2")&&line.Contains("VERIFIED_RELATIVE_SCALE")),
            "transient invalid post-wheel map is passively reobserved without a second wheel impulse");
        var initiallyOut=new ZoomDriver(images,0);
        var calibrated=await new MinimapZoomRoutine(initiallyOut,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(calibrated.Status==RoutineStatus.Completed&&initiallyOut.Level==0&&
            initiallyOut.WheelUp==1&&initiallyOut.GameWindowDown==1&&initiallyOut.OtherInputs==0,
            "already-zoomed-out minimap is distinguished from ignored input by a reversible upward probe");
        var rootOnly=new ZoomDriver(images,3,rootOnly:true);
        var rootDiagnostics=new List<string>();
        var recovered=await new MinimapZoomRoutine(rootOnly,()=>false,
            (_,_)=>Task.CompletedTask,rootDiagnostics.Add).Run(CancellationToken.None);
        Require(recovered.Status==RoutineStatus.Completed&&rootOnly.Level==0&&
            rootOnly.WheelDown==1&&rootOnly.GameWindowDown>=5&&
            rootOnly.WheelUp==0&&rootOnly.OtherInputs==0&&
            rootDiagnostics.Any(line=>line.Contains("target=GAME_WINDOW")&&line.Contains("scale=")),
            "ignored render-child wheel falls back to the verified game-window route");
        var rootAlreadyOut=new ZoomDriver(images,0,rootOnly:true);
        var rootRecovered=await new MinimapZoomRoutine(rootAlreadyOut,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(rootRecovered.Status==RoutineStatus.Completed&&rootAlreadyOut.Level==0&&
            rootAlreadyOut.GameWindowUp==1&&rootAlreadyOut.WheelUp==1,
            "root-only wheel route can verify an initially fully zoomed-out minimap");
        var disabled=new ZoomDriver(images,3,ignoreWheel:true);
        var refused=await new MinimapZoomRoutine(disabled,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(refused.Status==RoutineStatus.Blocked&&
            refused.Reason=="MINIMAP_ZOOM_BACKGROUND_WHEEL_NOT_OBSERVED"&&
            disabled.GameWindowDown==1&&disabled.GameWindowUp==1&&disabled.OtherInputs==0,
            "ignored wheel messages on both background routes must never certify the zoom stop");
        var noMinimap=new ZoomDriver(images,3,hideMap:true);
        var missing=await new MinimapZoomRoutine(noMinimap,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(missing.Status==RoutineStatus.Blocked&&noMinimap.WheelDown==0,
            "unverified minimap geometry cannot receive a wheel command");
        var paused=new ZoomDriver(images,3);
        var waiting=await new MinimapZoomRoutine(paused,()=>true,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(waiting.Status==RoutineStatus.Waiting&&paused.WheelDown==0,
            "F8/pause prevents calibration input");
        Console.WriteLine("MINIMAP ZOOM PASS: stable/low-feature geometry, passive frame retries, child/game-window routes, minimum-stop confirmation and pause");
    }
    private sealed class ZoomDriver:IVisualGameDriver
    {
        private readonly IReadOnlyList<byte[]> images;
        private readonly bool ignoreWheel,hideMap,rootOnly,corruptFirstPostInputFrame;
        private bool corruptNextFrame;
        internal int Level{get;private set;}
        internal int WheelDown,WheelUp,GameWindowDown,GameWindowUp,OtherInputs;
        public VisualFrame Current{get;private set;}
        internal ZoomDriver(IReadOnlyList<byte[]> images,int initial,bool ignoreWheel=false,bool hideMap=false,
            bool rootOnly=false,bool corruptFirstPostInputFrame=false)
        {
            this.images=images;this.ignoreWheel=ignoreWheel;this.hideMap=hideMap;
            this.rootOnly=rootOnly;this.corruptFirstPostInputFrame=corruptFirstPostInputFrame;Level=initial;
            Current=NewFrame(DateTimeOffset.UtcNow);
        }
        private VisualFrame NewFrame(DateTimeOffset time,bool invalidMap=false)=>new(new IntPtr(5),new Rectangle(0,0,1000,600),time,
            new[]{new VisualLabel("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud)},
            new(Array.Empty<ObservedSlot>(),0,hideMap?null:
                new NavigationPixels(invalidMap?Array.Empty<byte>():images[Level],new(830,10,150,150),24),Hitpoints:24));
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Current=NewFrame(Current.CapturedUtc.AddSeconds(1),corruptNextFrame);
            corruptNextFrame=false;
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(input.Kind==GameInputKind.WheelDown)
            {WheelDown++;if(!ignoreWheel&&!rootOnly)Level=Math.Max(0,Level-1);}
            else if(input.Kind==GameInputKind.WheelUp)
            {WheelUp++;if(!ignoreWheel&&!rootOnly)Level=Math.Min(images.Count-1,Level+1);}
            else if(input.Kind==GameInputKind.WheelDownGameWindow)
            {GameWindowDown++;if(!ignoreWheel)Level=Math.Max(0,Level-1);}
            else if(input.Kind==GameInputKind.WheelUpGameWindow)
            {GameWindowUp++;if(!ignoreWheel)Level=Math.Min(images.Count-1,Level+1);}
            else OtherInputs++;
            Current=NewFrame(Current.CapturedUtc.AddSeconds(1));
            corruptNextFrame=corruptFirstPostInputFrame;
            return Task.CompletedTask;
        }
    }
}
