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
        Require(outward.Scale is double less&&less<.982&&outward.Inliers>=12,
            "zoom-out is distinguished from ordinary minimap movement by stable terrain scale");
        var stationary=MinimapZoomVision.Compare(Frame(0).Pixels!.Navigation!,Frame(0).Pixels!.Navigation!);
        Require(stationary.Scale is double same&&Math.Abs(same-1)<.018,
            "unchanged terrain does not count as an outward zoom step");
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
        var initiallyOut=new ZoomDriver(images,0);
        var calibrated=await new MinimapZoomRoutine(initiallyOut,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(calibrated.Status==RoutineStatus.Completed&&initiallyOut.Level==0&&
            initiallyOut.WheelUp==1&&initiallyOut.OtherInputs==0,
            "already-zoomed-out minimap is distinguished from ignored input by a reversible upward probe");
        var disabled=new ZoomDriver(images,3,ignoreWheel:true);
        var refused=await new MinimapZoomRoutine(disabled,()=>false,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(refused.Status==RoutineStatus.Blocked&&
            refused.Reason=="MINIMAP_ZOOM_WHEEL_EFFECT_NOT_VERIFIED"&&disabled.OtherInputs==0,
            "ignored background wheel transport must never be labelled fully zoomed out");
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
        Console.WriteLine("MINIMAP ZOOM PASS: pixel-verified relative scale, minimum-stop confirmation, reversible maximum probe, ignored-input block and pause");
    }
    private sealed class ZoomDriver:IVisualGameDriver
    {
        private readonly IReadOnlyList<byte[]> images;
        private readonly bool ignoreWheel,hideMap;
        internal int Level{get;private set;}
        internal int WheelDown,WheelUp,OtherInputs;
        public VisualFrame Current{get;private set;}
        internal ZoomDriver(IReadOnlyList<byte[]> images,int initial,bool ignoreWheel=false,bool hideMap=false)
        {
            this.images=images;this.ignoreWheel=ignoreWheel;this.hideMap=hideMap;Level=initial;
            Current=NewFrame(DateTimeOffset.UtcNow);
        }
        private VisualFrame NewFrame(DateTimeOffset time)=>new(new IntPtr(5),new Rectangle(0,0,1000,600),time,
            new[]{new VisualLabel("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud)},
            new(Array.Empty<ObservedSlot>(),0,hideMap?null:
                new NavigationPixels(images[Level],new(830,10,150,150),24),Hitpoints:24));
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Current=NewFrame(Current.CapturedUtc.AddSeconds(1));return Task.FromResult(Current);}
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(input.Kind==GameInputKind.WheelDown)
            {WheelDown++;if(!ignoreWheel)Level=Math.Max(0,Level-1);}
            else if(input.Kind==GameInputKind.WheelUp)
            {WheelUp++;if(!ignoreWheel)Level=Math.Min(images.Count-1,Level+1);}
            else OtherInputs++;
            Current=NewFrame(Current.CapturedUtc.AddSeconds(1));
            return Task.CompletedTask;
        }
    }
}
