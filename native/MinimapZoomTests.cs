using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal static class MinimapZoomTests
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        static void Require(bool condition,string message)
        {if(!condition)throw new Exception("Minimap zoom: "+message);}
        Require(!MinimapZoomRoutine.NavigationZoomIn,
            "live navigation evaluates the broader maximum zoom-out view while both directions remain testable");
        var corners=Enumerable.Range(0,10).Select(i=>new Cv.Point2f(20+i%5*20,30+i/5*70)).ToArray();
        var scaled=corners.Select(p=>new Cv.Point2f(p.X*.85f+12,p.Y*.85f+9)).ToArray();
        var ten=MinimapZoomVision.MeasureScale(corners,scaled);
        Require(ten.Scale is double scale&&Math.Abs(scale-.85)<.001&&ten.Inliers==10&&ten.RatioMatches==10,
            "ten exact distributed correspondences reach marginal geometry instead of a contradictory twelve-match prefilter");
        Require(MinimapZoomVision.MeasureScale(corners.Take(9).ToArray(),scaled.Take(9).ToArray()).Scale is null&&
            MinimapZoomVision.MeasureScale(corners.Select(p=>new Cv.Point2f(p.X*.1f,p.Y*.1f)).ToArray(),
                scaled.Select(p=>new Cv.Point2f(p.X*.1f,p.Y*.1f)).ToArray()).Scale is null,
            "nine matches or tightly clustered evidence cannot authorize zoom");
        var unique=MinimapZoomVision.UniqueCorrespondences(new[]{(0,0),(0,0),(1,1),(1,2),(2,3),(3,3),(4,4)});
        Require(unique.SequenceEqual(new[]{(0,0),(4,4)}),
            "bidirectional evidence counts duplicate pairs once and discards both forms of conflicting feature identity");
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
            // CV_64FC1 needs double writes, including zeros. Writing an int
            // leaves half a matrix element uninitialized and makes fixtures
            // depend on whatever native memory happened to contain.
            affine.Set(0,0,1/factor);affine.Set(0,1,0d);affine.Set(0,2,256-75/factor);
            affine.Set(1,0,0d);affine.Set(1,1,1/factor);affine.Set(1,2,256-75/factor);
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
            "zoom-out is geometrically measurable with at least ten distributed inliers; marginal results still need a second frame: "+outward);
        using(var originalA=Cv.Cv2.ImDecode(images[3],Cv.ImreadModes.Grayscale))
        using(var originalB=Cv.Cv2.ImDecode(images[2],Cv.ImreadModes.Grayscale))
        using(var enlargedA=new Cv.Mat())using(var enlargedB=new Cv.Mat())
        using(var mask=new Cv.Mat(300,300,Cv.MatType.CV_8UC1,Cv.Scalar.Black))
        {
            Cv.Cv2.Resize(originalA,enlargedA,new(300,300));Cv.Cv2.Resize(originalB,enlargedB,new(300,300));
            Cv.Cv2.Circle(mask,new(150,150),120,Cv.Scalar.White,-1);
            Cv.Cv2.Circle(mask,new(150,150),28,Cv.Scalar.Black,-1);
            var measured=MinimapZoomVision.ExtractScale(enlargedA,enlargedB,mask,2);
            var identity=MinimapZoomVision.ExtractScale(enlargedA,enlargedA,mask,2);
            Require(measured.Scale is double enlargedScale&&Math.Abs(enlargedScale-.87/1.05)<.02&&
                identity.Scale is double unchangedScale&&Math.Abs(unchangedScale-1)<.001&&measured.FeatureSampling==2,
                "equal image upsampling preserves real relative zoom and does not invent an identity-frame zoom");
        }
        var stationary=MinimapZoomVision.Compare(Frame(0).Pixels!.Navigation!,Frame(0).Pixels!.Navigation!);
        Require(stationary.Scale is double same&&Math.Abs(same-1)<.018,
            "unchanged terrain does not count as an outward zoom step");
        using(var native=Cv.Cv2.ImDecode(images[2],Cv.ImreadModes.Grayscale))
        using(var padded=new Cv.Mat())
        {
            Cv.Cv2.CopyMakeBorder(native,padded,7,3,7,3,Cv.BorderTypes.Constant,Cv.Scalar.Black);
            var original=Frame(2).Pixels!.Navigation!;
            var bounds=original.MapBounds;
            var shifted=original with{MinimapPng=padded.ToBytes(".png"),
                MapBounds=new(bounds.Left-7,bounds.Top-7,160,160)};
            var unchanged=MinimapZoomVision.Compare(original,shifted);
            var zoomed=MinimapZoomVision.Compare(Frame(3).Pixels!.Navigation!,shifted);
            Require(unchanged.Scale is double identity&&Math.Abs(identity-1)<.001&&
                zoomed.Scale is double smaller&&smaller<.982,
                "small Hough crop jitter preserves identity and real zoom in the common unscaled screen region");
            Require(MinimapZoomVision.Compare(original,shifted with{
                MapBounds=new(bounds.Left+10,bounds.Top,160,160)}).Scale is null,
                "a materially moved map cannot be treated as border jitter");
            Require(MinimapZoomVision.Compare(original,shifted with{
                MapBounds=bounds}).Reason=="MINIMAP_IMAGE_INVALID",
                "raster and declared screen bounds must agree before alignment");
        }
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
        var delayedMap=new ZoomDriver(images,3,postInputFrame:(frame,sample)=>sample<=4?
            frame with{Pixels=frame.Pixels! with{Navigation=null,MinimapEvidence="hough_raw=0 inside_roi=0 white_centres=0"}}:frame);
        var delayedLog=new List<string>();
        var delayed=await new MinimapZoomRoutine(delayedMap,()=>false,(_,_)=>Task.CompletedTask,delayedLog.Add)
            .Run(CancellationToken.None);
        Require(delayed.Status==RoutineStatus.Completed&&delayedMap.Level==0&&
            delayedMap.WheelDown==fromMiddle.WheelDown&&
            delayedLog.Any(line=>line.Contains("sample=4 frame_rejected=MINIMAP_MISSING")&&
                line.Contains("minimap_evidence=hough_raw=0"))&&
            delayedLog.Any(line=>line.Contains("sample=5 scale=")&&line.Contains("VERIFIED_RELATIVE_SCALE")),
            "four missing map frames allow one passive recovery without a second wheel impulse");
        foreach(string fault in new[]{"MINIMAP_MISSING","GAME_SCREEN_UNVERIFIED","CAPTURE_NOT_INDEPENDENT","MINIMAP_BOUNDS_INVALID"})
        {
            VisualFrame Damage(VisualFrame frame)=>fault switch
            {
                "MINIMAP_MISSING"=>frame with{Pixels=frame.Pixels! with{Navigation=null,
                    MinimapEvidence="hough_raw=0 inside_roi=0 white_centres=0"}},
                "GAME_SCREEN_UNVERIFIED"=>frame with{Labels=Array.Empty<VisualLabel>()},
                "CAPTURE_NOT_INDEPENDENT"=>frame with{CapturedUtc=frame.CapturedUtc.AddSeconds(-2)},
                _=>frame with{Pixels=frame.Pixels! with{Navigation=frame.Pixels.Navigation! with{MapBounds=new(830,10,90,90)}}}
            };
            var log=new List<string>();
            var recovering=new ZoomDriver(images,3,postInputFrame:(frame,sample)=>sample==1?Damage(frame):frame);
            var result=await new MinimapZoomRoutine(recovering,()=>false,(_,_)=>Task.CompletedTask,log.Add).Run(CancellationToken.None);
            Require(result.Status==RoutineStatus.Completed&&recovering.WheelDown==fromMiddle.WheelDown&&
                log.Any(line=>line.Contains("frame_rejected="+fault)&&line.Contains("passive_only=true")),
                fault+" retries a new observation without an extra wheel impulse");
            var persistent=new ZoomDriver(images,3,postInputFrame:(frame,_)=>Damage(frame));
            var blocked=await new MinimapZoomRoutine(persistent,()=>false,(_,_)=>Task.CompletedTask).Run(CancellationToken.None);
            Require(blocked.Status==RoutineStatus.Blocked&&blocked.Reason=="MINIMAP_ZOOM_POST_SCROLL_"+fault&&
                persistent.WheelDown==1&&persistent.PostInputObservations==(fault=="MINIMAP_MISSING"?6:3),
                fault+" remains blocked after bounded passive samples");
        }
        foreach(string fault in new[]{"WINDOW_CHANGED","SCREEN_BOUNDS_CHANGED","SESSION_LEFT_GAME"})
        {
            var boundary=new ZoomDriver(images,3,postInputFrame:(frame,_)=>fault switch
            {
                "WINDOW_CHANGED"=>frame with{Window=new IntPtr(9)},
                "SCREEN_BOUNDS_CHANGED"=>frame with{ScreenBounds=new(1,0,1000,600)},
                _=>frame with{Labels=new[]{new VisualLabel("Connection lost",new(10,10,100,20))}}
            });
            var result=await new MinimapZoomRoutine(boundary,()=>false,(_,_)=>Task.CompletedTask).Run(CancellationToken.None);
            Require(result.Status==RoutineStatus.Blocked&&result.Reason=="MINIMAP_ZOOM_POST_SCROLL_"+fault&&
                boundary.WheelDown==1&&boundary.PostInputObservations==1,
                fault+" stops immediately without further input or observation");
        }
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
        bool stop=false;
        var pausedDuringRecovery=new ZoomDriver(images,3,postInputFrame:(frame,_)=>
        {stop=true;return frame with{Pixels=frame.Pixels! with{Navigation=null}};});
        var interrupted=await new MinimapZoomRoutine(pausedDuringRecovery,()=>stop,
            (_,_)=>Task.CompletedTask).Run(CancellationToken.None);
        Require(interrupted.Status==RoutineStatus.Waiting&&pausedDuringRecovery.WheelDown==1&&
            pausedDuringRecovery.PostInputObservations==1,"pause during missing-map recovery prevents all further input and captures");
        foreach(var scenario in new[]{(Initial:1,Root:false),(Initial:4,Root:false),(Initial:1,Root:true),(Initial:4,Root:true)})
        {
            var inward=new ZoomDriver(images,scenario.Initial,rootOnly:scenario.Root);
            var result=await new MinimapZoomRoutine(inward,()=>false,(_,_)=>Task.CompletedTask,zoomIn:true).Run(CancellationToken.None);
            Require(result.Status==RoutineStatus.Completed&&result.Reason=="MINIMAP_FULLY_ZOOMED_IN_VISUALLY_CONFIRMED"&&
                inward.Level==images.Count-1&&inward.OtherInputs==0&&
                (scenario.Root?inward.GameWindowUp>=3:inward.WheelUp>=3),
                "maximum zoom is confirmed from middle or upper stop on both input routes: "+scenario);
            if(scenario.Initial==images.Count-1)
                Require(inward.WheelDown+inward.GameWindowDown>=1,"an already maximum map requires a reversible outward probe");
        }
        var ignoredInward=new ZoomDriver(images,4,ignoreWheel:true);
        var refusedInward=await new MinimapZoomRoutine(ignoredInward,()=>false,(_,_)=>Task.CompletedTask,zoomIn:true).Run(CancellationToken.None);
        Require(refusedInward.Status==RoutineStatus.Blocked&&refusedInward.Reason=="MINIMAP_ZOOM_BACKGROUND_WHEEL_NOT_OBSERVED"&&
            ignoredInward.WheelUp==1&&ignoredInward.GameWindowUp==1&&ignoredInward.WheelDown==1&&ignoredInward.GameWindowDown==1,
            "ignored inputs cannot certify maximum zoom even when the image looks stationary");
        var pausedInward=new ZoomDriver(images,1);
        Require((await new MinimapZoomRoutine(pausedInward,()=>true,(_,_)=>Task.CompletedTask,zoomIn:true).Run(CancellationToken.None)).Status==RoutineStatus.Waiting&&
            pausedInward.WheelUp==0,"pause prevents zoom-in input");
        Console.WriteLine("MINIMAP ZOOM PASS: stable/low-feature geometry, passive frame retries, both input routes, minimum/maximum stop confirmation and pause");
    }
    private sealed class ZoomDriver:IVisualGameDriver
    {
        private readonly IReadOnlyList<byte[]> images;
        private readonly bool ignoreWheel,hideMap,rootOnly,corruptFirstPostInputFrame;
        private bool corruptNextFrame;
        private readonly Func<VisualFrame,int,VisualFrame>? postInputFrame;
        private int sampleAfterInput;
        internal int PostInputObservations;
        internal int Level{get;private set;}
        internal int WheelDown,WheelUp,GameWindowDown,GameWindowUp,OtherInputs;
        public VisualFrame Current{get;private set;}
        internal ZoomDriver(IReadOnlyList<byte[]> images,int initial,bool ignoreWheel=false,bool hideMap=false,
            bool rootOnly=false,bool corruptFirstPostInputFrame=false,Func<VisualFrame,int,VisualFrame>? postInputFrame=null)
        {
            this.images=images;this.ignoreWheel=ignoreWheel;this.hideMap=hideMap;
            this.rootOnly=rootOnly;this.corruptFirstPostInputFrame=corruptFirstPostInputFrame;Level=initial;
            this.postInputFrame=postInputFrame;
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
            if(WheelDown+WheelUp+GameWindowDown+GameWindowUp>0)
            {
                PostInputObservations++;
                Current=postInputFrame?.Invoke(Current,++sampleAfterInput)??Current;
            }
            corruptNextFrame=false;
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            sampleAfterInput=0;
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
