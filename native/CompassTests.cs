namespace OSRSLiveDiagnostics;

internal static class CompassTests
{
    private static void Require(bool ok,string message){if(!ok)throw new Exception("Compass: "+message);}
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var map=new Rectangle(803,5,150,150);
        using(var image=new Bitmap(1000,600))
        {
            Require(CompassVision.Read(image,map) is null,"blank pixels cannot prove a compass direction");
            foreach(int angle in new[]{0,90,180,-45})
            {
                using(var g=Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);g.TranslateTransform(798,24);g.RotateTransform(angle);
                    g.FillPolygon(Brushes.Red,new[]{new Point(0,-7),new(-4,1),new(4,1)});
                    g.FillRectangle(Brushes.Blue,-2,2,5,4);
                }
                image.SetPixel(784,36,Color.FromArgb(140,65,55)); // bronze bezel outside the pointer
                var reading=CompassVision.Read(image,map);
                Require(reading is not null&&reading.North==(angle==0),"native red/blue needle separates north from rotated headings");
            }
            using(var g=Graphics.FromImage(image)){g.Clear(Color.Black);g.FillRectangle(Brushes.Red,794,17,9,12);}
            Require(CompassVision.Read(image,map) is null,"a red ornament without the opposite blue pointer is rejected");
        }
        var first=new CompassDriver(90);var log=new List<string>();
        var result=await new CompassCalibrationRoutine(first,()=>false,log.Add,(_,_)=>Task.CompletedTask).Run(default);
        Require(result.Reason=="COMPASS_NORTH_AFTER_CLICK_CONFIRMED"&&first.Clicks==1&&first.Hovers==1&&log.Count(l=>l.Contains("COMPASS_SAMPLE"))==2,
            "one tooltip-verified click and two fresh north frames confirm rotation");
        var north=new CompassDriver(0);
        var already=await new CompassCalibrationRoutine(north,()=>false,_=>{},(_,_)=>Task.CompletedTask).Run(default);
        Require(already.Reason=="COMPASS_ALREADY_NORTH_CONFIRMED"&&north.Clicks==0&&north.Hovers==0,
            "already-north scenes need only passive confirmation");
        var noTooltip=new CompassDriver(90,tooltip:false);
        var missing=await new CompassCalibrationRoutine(noTooltip,()=>false,_=>{},(_,_)=>Task.CompletedTask).Run(default);
        Require(missing.Reason=="COMPASS_LOOK_NORTH_CONTROL_NOT_VERIFIED"&&noTooltip.Clicks==0,
            "needle geometry alone cannot authorize a camera click");
        var ignored=new CompassDriver(90,effective:false);
        var failed=await new CompassCalibrationRoutine(ignored,()=>false,_=>{},(_,_)=>Task.CompletedTask).Run(default);
        Require(failed.Reason=="COMPASS_NORTH_NOT_CONFIRMED"&&ignored.Clicks==1,
            "an ignored camera input is not retried or reported successful");
        var stale=new CompassDriver(0,stale:true);
        Require((await new CompassCalibrationRoutine(stale,()=>false,_=>{},(_,_)=>Task.CompletedTask).Run(default)).Status==RoutineStatus.Blocked,
            "one cached north image cannot establish two confirmations");
        var paused=new CompassDriver(90);
        Require((await new CompassCalibrationRoutine(paused,()=>true,_=>{},(_,_)=>Task.CompletedTask).Run(default)).Status==RoutineStatus.Waiting&&paused.Hovers==0,
            "pause prevents even a hover");
        var text=new VisualLabel("Look North",new(4,4,80,12));
        var verified=paused.Current with{Labels=paused.Current.Labels.Append(text).ToArray()};
        var input=new GameInput(GameInputKind.LeftClick,verified.Pixels!.Compass!.ClickBounds);
        Require(CompassVision.InputAuthorized(verified,input)&&
            !CompassVision.InputAuthorized(verified with{Pixels=verified.Pixels with{WorldMapPanelOpen=true}},input)&&
            !CompassVision.InputAuthorized(verified with{Pixels=verified.Pixels with{Compass=null}},input),
            "fresh compass pixels, native HUD and an unobscured target are all required");
        int dispatched=0;
        var refresh=new WindowVisualGameDriver(verified with{CapturedUtc=DateTimeOffset.UtcNow.AddSeconds(-5)},
            _=>Task.FromResult<VisualFrame?>(verified with{CapturedUtc=DateTimeOffset.UtcNow,Pixels=verified.Pixels with{Compass=null}}),
            (_,_,_)=>{dispatched++;return Task.FromResult(true);},_=>{});
        refresh.InputGuard=CompassVision.InputAuthorized;
        bool blocked=false;
        try{await refresh.InputAsync(input,default);}catch(RoutineBlockedException ex){blocked=ex.Message=="INPUT_TARGET_NOT_VERIFIED_AFTER_REFRESH";}
        Require(blocked&&dispatched==0,"a disappearing compass during refresh cannot send a stale camera click");
        Console.WriteLine("COMPASS PASS: native heading, exact tooltip, one click, independent north frames, stale target and pause guards");
    }
    private sealed class CompassDriver:IVisualGameDriver
    {
        private double angle;private bool hover;private readonly bool tooltip,effective,stale;
        internal int Clicks,Hovers;
        public VisualFrame Current{get;private set;}
        internal CompassDriver(double angle,bool tooltip=true,bool effective=true,bool stale=false)
        {this.angle=angle;this.tooltip=tooltip;this.effective=effective;this.stale=stale;Current=Frame(DateTimeOffset.UtcNow);}
        private VisualFrame Frame(DateTimeOffset time)
        {
            var labels=new List<VisualLabel>{new("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud)};
            if(hover&&tooltip)labels.Add(new("Look North",new(4,4,80,12)));
            return new(new IntPtr(5),new(0,0,1000,600),time,labels,
                new(Array.Empty<ObservedSlot>(),0,new(Array.Empty<byte>(),new(803,5,150,150),24),
                    Compass:new(new(795,21,7,7),angle,41,16)));
        }
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Current=Frame(stale?Current.CapturedUtc:Current.CapturedUtc.AddMilliseconds(200));return Task.FromResult(Current);}
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(input.Kind==GameInputKind.Hover){hover=true;Hovers++;}
            else if(input.Kind==GameInputKind.LeftClick){Clicks++;if(effective)angle=0;}
            else throw new Exception("Unexpected compass action");
            Current=Frame(Current.CapturedUtc);return Task.CompletedTask;
        }
    }
}
