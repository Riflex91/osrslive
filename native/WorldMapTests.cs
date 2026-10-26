using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal static class WorldMapTests
{
    private static void Require(bool condition,string message)
    {if(!condition)throw new Exception("World map: "+message);}
    internal static readonly VisualLabel[] PanelLabels={
        new("Game features",new(30,30,100,12)),new("Search",new(100,400,60,12)),
        new("Gielinor Surface",new(300,400,120,12))};
    internal static void DrawStar(Bitmap bitmap,Point p)
    {
        using var g=Graphics.FromImage(bitmap);
        g.FillPolygon(Brushes.Yellow,new[]{new Point(p.X,p.Y-16),new(p.X+4,p.Y-4),new(p.X+16,p.Y),
            new(p.X+4,p.Y+4),new(p.X,p.Y+16),new(p.X-4,p.Y+4),new(p.X-16,p.Y),new(p.X-4,p.Y-4)});
    }
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    internal static async Task OcrFixture(OcrEngine engine)
    {
        using var bitmap=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Segoe UI",10))using(var orange=new Pen(Color.Orange,5))
        {
            g.Clear(Color.FromArgb(35,40,25));g.DrawLine(orange,676,12,694,28);g.DrawLine(orange,676,28,694,12);
            g.DrawString("Game features",font,Brushes.Yellow,30,30);
            g.DrawString("Search",font,Brushes.White,100,400);
            g.DrawString("Gielinor Surface",font,Brushes.White,300,400);
        }
        var labels=await WorldMapVision.ReadLabels(bitmap,engine);
        Require(WorldMapVision.Panel(bitmap,labels),"OCR separates Search and surface selector on the same toolbar row");
    }
    private static async Task RunAsync()
    {
        Require(WorldMapLocalizer.ConsensusVerified(22,32)&&WorldMapLocalizer.ConsensusVerified(26,37),
            "previous live world-map consensus cases retain their acceptance");
        Require(WorldMapLocalizer.ConsensusVerified(31,49)&&WorldMapLocalizer.ConsensusVerified(30,50),
            "strong live map evidence narrowly below 65 percent can proceed to independent visual checks");
        Require(!WorldMapLocalizer.ConsensusVerified(29,49)&&!WorldMapLocalizer.ConsensusVerified(31,54)&&
            !WorldMapLocalizer.ConsensusVerified(11,12)&&!WorldMapLocalizer.ConsensusVerified(30,29)&&
            !WorldMapLocalizer.ConsensusVerified(27,42)&&!WorldMapLocalizer.ConsensusVerified(25,43),
            "weak, insufficient or impossible geometric evidence still fails closed");
        Require(WorldMapLocalizer.DescriptorRatios.SequenceEqual(new[]{.65,.60,.55}),
            "fallbacks may only tighten the reciprocal feature ratio, never loosen it");
        var at=DateTimeOffset.UtcNow;
        var anchor=new WorldMapPosition(new WorldTile(3153,3202),20,at);
        Require(WorldMapLocalizer.UniqueVerifiedPosition(Array.Empty<WorldMapPosition>()) is null&&
            WorldMapLocalizer.UniqueVerifiedPosition(new[]{anchor,anchor with {Inliers=25}})?.Inliers==25&&
            WorldMapLocalizer.UniqueVerifiedPosition(new[]{anchor,anchor with {SurfaceTile=new(3154,3202)}}) is null,
            "stricter descriptor profiles only reinforce a unique, identical tile");
        using(var close=new Bitmap(1000,600))
        {
            using(var g=Graphics.FromImage(close))using(var pen=new Pen(Color.Orange,8))
            {g.DrawLine(pen,676,12,694,28);g.DrawLine(pen,676,28,694,12);}
            Require(WorldMapVision.CloseIcon(close),"thick native orange X is verified by both diagonals and empty side gaps");
            using(var g=Graphics.FromImage(close)){g.Clear(Color.Black);g.FillRectangle(Brushes.Orange,676,12,19,17);}
            Require(!WorldMapVision.CloseIcon(close),"solid orange scenery is not a close control");
        }
        using(var icon=new Bitmap(1000,600))
        {
            var map=new Rectangle(804,12,144,144);
            Require(WorldMapVision.Globe(icon,map,out _) is null,"blank minimap border cannot authorize map opening");
            using(var g=Graphics.FromImage(icon))
            {
                g.Clear(Color.Black);
                g.FillEllipse(Brushes.LightGray,929,133,24,24);
                g.FillEllipse(Brushes.Blue,931,135,20,20);
                g.FillEllipse(Brushes.Green,935,138,9,13);
            }
            var globe=WorldMapVision.Globe(icon,map,out _);
            Require(globe is Rectangle r&&Math.Abs(r.Left+r.Width/2-941)<=3&&Math.Abs(r.Top+r.Height/2-145)<=3,
                "colored globe with neutral rim is located despite minimap-center offset");
            using(var g=Graphics.FromImage(icon)){g.Clear(Color.Black);g.FillEllipse(Brushes.Red,929,133,24,24);}
            Require(WorldMapVision.Globe(icon,map,out _) is null,"red combat orb cannot substitute for world-map globe");
        }
        using(var bitmap=new Bitmap(1000,600))
        {
            using(var g=Graphics.FromImage(bitmap))using(var orange=new Pen(Color.Orange,5))
            {
                g.Clear(Color.FromArgb(30,60,25));g.DrawLine(orange,676,12,694,28);g.DrawLine(orange,676,28,694,12);
                g.FillRectangle(Brushes.Yellow,300,100,25,25); // gold building is not a star
            }
            Require(WorldMapVision.MarkerCandidates(bitmap,WorldMapVision.View).Count==0,"solid gold scenery must not become a player");
            DrawStar(bitmap,new(420,220));
            // A narrow highlight gap splits the raw gold mask into pieces.
            using(var g=Graphics.FromImage(bitmap))g.DrawLine(Pens.Black,404,220,436,220);
            var pixels=WorldMapVision.Read(bitmap,PanelLabels);
            Require(pixels is not null&&Math.Abs(pixels.PlayerMarker.X-420)<=1&&Math.Abs(pixels.PlayerMarker.Y-220)<=1,
                "one native panel and star give observed marker coordinates");
            Require(WorldMapVision.Read(bitmap,PanelLabels.Take(2).ToArray()) is null,"missing surface selector forbids floor assumptions");
            DrawStar(bitmap,new(520,270));
            Require(WorldMapVision.Read(bitmap,PanelLabels) is null,"two plausible stars are ambiguous");
            Require(WorldMapVision.Read(new Bitmap(900,600),PanelLabels) is null,"unsupported viewport cannot authorize a map click");
        }
        using var atlas=new WorldNavigator(new WorldMapData());
        var completed=new RoutineResult(RoutineStatus.Completed,"VERIFIED");
        var blocked=new RoutineResult(RoutineStatus.Blocked,"WORLD_MAP_CLOSE_CONTROL_NOT_VERIFIED");
        Require(NavigationStartupPolicy.FrozenCalibration(blocked,null,null)=="BLOCKED_"+blocked.Reason&&
            NavigationStartupPolicy.FrozenCalibration(null,completed,null) is null&&
            NavigationStartupPolicy.FrozenCalibration(null,null,null) is null,
            "failed calibration prevents unnecessary identity-tab input even without a fresh skill lease");
        Require(NavigationStartupPolicy.GameplayBlock(true,blocked,null,false)=="BLOCKED_"+blocked.Reason,
            "losing fresh skill confirmation must not bypass the retained subject's world-map block");
        Require(NavigationStartupPolicy.GameplayBlock(true,completed,null,false)=="WAITING_FOR_MINIMAP_CALIBRATION"&&
            NavigationStartupPolicy.GameplayBlock(true,completed,completed,true,completed)=="BLOCKED_WORLD_MAP_STILL_OPEN"&&
            NavigationStartupPolicy.GameplayBlock(true,completed,completed,false,completed) is null&&
            NavigationStartupPolicy.GameplayBlock(true,completed,completed,false)=="WAITING_FOR_COMPASS_CALIBRATION",
            "only completed navigation gates and a closed map allow the gameplay handoff");
        WorldMapMatch Fix(VisualFrame f)=>new(new(new(3222,3218),20,f.CapturedUtc),"WORLD_MAP_SURFACE_POSITION_VERIFIED");
        var driver=new MapDriver();var logs=new List<string>();
        var success=await new WorldMapRoutine(driver,atlas,()=>false,logs.Add,(_,_)=>Task.CompletedTask,Fix).Run(default);
        Require(success.Status==RoutineStatus.Completed&&driver.OpenClicks==1&&driver.CloseClicks==1&&
            logs.Count(l=>l.Contains("WORLD_MAP_SAMPLE"))==2&&logs.Any(l=>l.Contains("player_plane_verified=false")),
            "one tooltip-authorized open, two independent fixes and confirmed closure precede hint publication");
        var hidden=new MapDriver(tooltip:false);
        var shortcut=hidden.Current with{Labels=hidden.Current.Labels.Concat(new[]{new VisualLabel("Ctrl+M to open.",new(820,190,130,12))}).ToArray()};
        Require(WorldMapVision.InputAuthorized(shortcut,new(GameInputKind.LeftClick,new(939,141,7,7))),
            "exact native shortcut tooltip beside the globe independently authorizes its button");
        var distant=shortcut with{Labels=hidden.Current.Labels.Concat(new[]{new VisualLabel("Ctrl+M to open.",new(100,190,130,12))}).ToArray()};
        Require(!WorldMapVision.InputAuthorized(distant,new(GameInputKind.LeftClick,new(939,141,7,7))),
            "the same text away from the observed globe cannot authorize a click");
        var refused=await new WorldMapRoutine(hidden,atlas,()=>false,_=>{},(_,_)=>Task.CompletedTask,Fix).Run(default);
        Require(refused.Reason=="WORLD_MAP_OPEN_CONTROL_NOT_VERIFIED"&&hidden.OpenClicks==0&&hidden.CloseClicks==0,
            "geometric globe candidate alone cannot authorize a click");
        var duplicate=new MapDriver(open:true,stale:true);
        var stale=await new WorldMapRoutine(duplicate,atlas,()=>false,_=>{},(_,_)=>Task.CompletedTask,Fix).Run(default);
        Require(stale.Status!=RoutineStatus.Completed&&duplicate.OpenClicks==0&&duplicate.CloseClicks==1,
            "cached screenshots cannot establish an independent fix or closure");
        var ambiguous=new MapDriver(open:true);
        var failed=await new WorldMapRoutine(ambiguous,atlas,()=>false,_=>{},(_,_)=>Task.CompletedTask,
            _=>new(null,"WORLD_MAP_MATCH_AMBIGUOUS")).Run(default);
        Require(failed.Status==RoutineStatus.Blocked&&ambiguous.OpenClicks==0&&ambiguous.CloseClicks==1,
            "failed map matching closes only the known panel and never publishes a position");
        var missingClose=new MapDriver(open:true,closeVisible:false);
        var cannotClose=await new WorldMapRoutine(missingClose,atlas,()=>false,_=>{},(_,_)=>Task.CompletedTask,Fix).Run(default);
        Require(cannotClose.Reason=="WORLD_MAP_CLOSE_CONTROL_NOT_VERIFIED"&&missingClose.Inputs==0,
            "a readable map without its close control cannot publish a hint or click the underlying world");
        var paused=new MapDriver();
        var waiting=await new WorldMapRoutine(paused,atlas,()=>true,_=>{},(_,_)=>Task.CompletedTask,Fix).Run(default);
        Require(waiting.Status==RoutineStatus.Waiting&&paused.Inputs==0,"pause prevents even the opening hover");
        int dispatched=0;
        var oldPanel=new MapDriver(open:true).Current with{CapturedUtc=DateTimeOffset.UtcNow.AddSeconds(-5)};
        var closedFrame=new MapDriver().Current;
        var refreshing=new WindowVisualGameDriver(oldPanel,_=>Task.FromResult<VisualFrame?>(closedFrame),
            (_,_,_)=>{dispatched++;return Task.FromResult(true);},_=>{});
        refreshing.InputGuard=WorldMapVision.InputAuthorized;
        bool changedTargetBlocked=false;
        try{await refreshing.InputAsync(new(GameInputKind.LeftClick,WorldMapVision.Close),default);}
        catch(RoutineBlockedException ex){changedTargetBlocked=ex.Message=="INPUT_TARGET_NOT_VERIFIED_AFTER_REFRESH";}
        Require(changedTargetBlocked&&dispatched==0,"a panel disappearing during freshness refresh cannot turn its Close click into a game-world click");
        Console.WriteLine("WORLD MAP PASS: native panel/marker gates, scenery/ambiguity rejection, tooltip authorization, fresh fixes, closure and pause");
    }
    private sealed class MapDriver:IVisualGameDriver
    {
        private bool open,hover;private readonly bool tooltip,stale,closeVisible;
        internal int OpenClicks,CloseClicks,Inputs;
        public VisualFrame Current{get;private set;}
        internal MapDriver(bool open=false,bool tooltip=true,bool stale=false,bool closeVisible=true)
        {this.open=open;this.tooltip=tooltip;this.stale=stale;this.closeVisible=closeVisible;Current=Frame(DateTimeOffset.UtcNow);}
        private VisualFrame Frame(DateTimeOffset time)
        {
            var labels=new List<VisualLabel>{new("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud)};
            if(hover&&tooltip){labels.Add(new("Open Floating panel",new(820,175,150,12)));labels.Add(new("Ctrl+M to open.",new(820,190,130,12)));}
            var world=open?new WorldMapPixels(Array.Empty<byte>(),WorldMapVision.View,new(420,220),WorldMapVision.Close):null;
            return new(new IntPtr(5),new(0,0,1000,600),time,labels,
                new(Array.Empty<ObservedSlot>(),0,new(Array.Empty<byte>(),new(804,12,144,144),24),Hitpoints:24,
                    WorldMap:world,WorldMapPanelOpen:open,WorldMapCloseVisible:open&&closeVisible));
        }
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Current=Frame(stale?Current.CapturedUtc:Current.CapturedUtc.AddMilliseconds(300));return Task.FromResult(Current);}
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Inputs++;
            if(input.Kind==GameInputKind.Hover)hover=true;
            else if(input.Kind==GameInputKind.LeftClick&&input.Bounds==WorldMapVision.Close){CloseClicks++;open=false;}
            else if(input.Kind==GameInputKind.LeftClick&&WorldMapVision.GlobeCandidate(Current)==input.Bounds){OpenClicks++;open=true;}
            else throw new Exception("Unexpected world-map input");
            Current=Frame(Current.CapturedUtc);return Task.CompletedTask;
        }
    }
}
