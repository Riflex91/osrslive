namespace OSRSLiveDiagnostics;

// First opt-in movement transport for the regular Jagex-Launcher RuneLite.
// World coordinates come exclusively from the verified Plugin Hub API; the
// separately calibrated minimap is used ONLY to project a short click.
// A posted Windows message is never counted as movement until a newer API
// tile event confirms progress along the previously calculated collision path.
internal sealed class RuneLiteAutoWalk
{
    internal sealed record Calibration(IntPtr Window,Size ClientSize,Point PlayerCenter,
        double PixelsPerTile);
    internal sealed record ClickPlan(Point Pixel,WorldTile From,WorldTile Waypoint,
        int WaypointIndex,WorldTile[] PathPrefix);
    internal sealed record Feedback(string Status,string Diagnostic,bool Continue);

    private sealed record Pending(ClickPlan Plan,DateTimeOffset EventAt,
        DateTimeOffset SentAt,int LastIndex,DateTimeOffset LastProgressAt);

    private Pending? pending;
    private DateTimeOffset started;
    private int clicks;
    internal bool Active{get;private set;}
    internal NavigationDestination? Destination{get;private set;}
    internal Calibration? Geometry{get;private set;}
    internal int Clicks=>clicks;
    internal bool WaitingForMovement=>pending is not null;
    internal static readonly TimeSpan EventTimeout=TimeSpan.FromSeconds(9);
    internal const int MaxClicks=40;

    internal void Start(NavigationDestination destination,Calibration geometry,DateTimeOffset now)
    {
        Stop();
        if(!ValidGeometry(geometry))throw new ArgumentException("Minimap calibration invalid",nameof(geometry));
        Active=true;Destination=destination;Geometry=geometry;started=now;
    }

    internal void Stop()
    {
        Active=false;pending=null;Destination=null;Geometry=null;
        started=default;clicks=0;
    }

    internal static bool ValidGeometry(Calibration c)=>
        c.Window!=IntPtr.Zero&&c.ClientSize.Width>=640&&c.ClientSize.Height>=350&&
        c.PlayerCenter.X>=40&&c.PlayerCenter.X<c.ClientSize.Width-40&&
        c.PlayerCenter.Y is >=35 and <=220&&
        double.IsFinite(c.PixelsPerTile)&&c.PixelsPerTile is >=3 and <=16;

    internal static ClickPlan? Project(WorldTile from,PathStep[] path,Calibration geometry)
    {
        if(!ValidGeometry(geometry)||path.Length==0)return null;
        ClickPlan? chosen=null;
        WorldTile preceding=from;
        var prefix=new List<WorldTile>();
        foreach(var step in path.Take(9))
        {
            // The first movement release never clicks through doors, ladders,
            // stairs, teleports, plane changes or non-adjacent collision edges.
            if(step.Transport is not null||step.Tile.Plane!=from.Plane||
               preceding.Distance(step.Tile)!=1)break;
            prefix.Add(step.Tile);preceding=step.Tile;
            double px=(step.Tile.X-from.X)*geometry.PixelsPerTile;
            double py=(from.Y-step.Tile.Y)*geometry.PixelsPerTile;
            double radius=Math.Sqrt(px*px+py*py);
            if(radius<7||radius>34)continue;
            var position=new Point(geometry.PlayerCenter.X+(int)Math.Round(px),
                geometry.PlayerCenter.Y+(int)Math.Round(py));
            if(position.X<25||position.Y<25||
               position.X>=geometry.ClientSize.Width-25||
               position.Y>=geometry.ClientSize.Height-25)continue;
            chosen=new ClickPlan(position,from,step.Tile,prefix.Count,[..prefix]);
        }
        return chosen;
    }

    internal bool CanDispatch(DateTimeOffset now,DateTimeOffset eventAt)=>
        Active&&pending is null&&clicks<MaxClicks&&
        now-started<TimeSpan.FromMinutes(5)&&eventAt!=default&&
        now-eventAt>=TimeSpan.Zero&&now-eventAt<=TimeSpan.FromSeconds(3);

    internal void Sent(ClickPlan plan,DateTimeOffset eventAt,DateTimeOffset now)
    {
        if(!CanDispatch(now,eventAt))throw new InvalidOperationException("Stale or busy movement dispatch");
        pending=new(plan,eventAt,now,0,now);clicks++;
    }

    internal Feedback Observe(WorldTile tile,DateTimeOffset eventAt,DateTimeOffset now)
    {
        if(!Active||pending is null||eventAt<=pending.EventAt||eventAt<=pending.SentAt)
            return new("IGNORED","no_post_click_api_event=true",false);
        var p=pending;
        int index=Array.FindIndex(p.Plan.PathPrefix,t=>t==tile)+1;
        if(index<=p.LastIndex)
            return new("OFF_PATH","tile_not_a_forward_step_on_dispatched_route=true "+
                $"from={p.Plan.From.X},{p.Plan.From.Y},{p.Plan.From.Plane} "+
                $"observed={tile.X},{tile.Y},{tile.Plane} expected={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                $"event_gap_ms={(eventAt-p.EventAt).TotalMilliseconds:0} input_was_only_posted=true",false);
        bool reached=index>=p.Plan.WaypointIndex-1;
        if(reached)
        {
            pending=null;
            return new("WAYPOINT_CONFIRMED",
                $"observed={tile.X},{tile.Y},{tile.Plane} expected={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                $"observed_path_index={index} waypoint_index={p.Plan.WaypointIndex} "+
                $"event_gap_ms={(eventAt-p.EventAt).TotalMilliseconds:0} confirmed_by=NEW_PLUGIN_API_EVENT",true);
        }
        pending=p with{LastIndex=index,LastProgressAt=now};
        return new("PROGRESS_CONFIRMED",
            $"observed={tile.X},{tile.Y},{tile.Plane} observed_path_index={index} "+
            $"waypoint_index={p.Plan.WaypointIndex} confirmed_by=NEW_PLUGIN_API_EVENT",true);
    }

    internal string? Timeout(DateTimeOffset now)
    {
        if(!Active)return null;
        if(clicks>=MaxClicks&&pending is null)return "CLICK_LIMIT_REACHED";
        if(now-started>TimeSpan.FromMinutes(5))return "RUN_LIMIT_REACHED";
        if(pending is not null&&now-pending.LastProgressAt>EventTimeout)
            return "POSITION_EVENT_TIMEOUT";
        if(clicks==0&&now-started>TimeSpan.FromSeconds(20))
            return "START_EVENT_TIMEOUT";
        return null;
    }
}

internal static class RuneLiteAutoWalkTests
{
    internal static void Run()
    {
        static void Require(bool value,string reason)
        {if(!value)throw new Exception("RuneLite auto-walk: "+reason);}
        var goal=new NavigationDestination("Test destination","safe",new(3210,3210),1);
        var geom=new RuneLiteAutoWalk.Calibration(new IntPtr(123),new(820,530),new(650,92),4);
        var origin=new WorldTile(3200,3200);
        var east=Enumerable.Range(1,8).Select(i=>new PathStep(new(3200+i,3200))).ToArray();
        var plan=RuneLiteAutoWalk.Project(origin,east,geom)!;
        Require(plan is not null&&plan.Pixel==new Point(682,92)&&plan.Waypoint==new WorldTile(3208,3200),
            "bounded minimap projection uses only verified path tiles");
        Require(RuneLiteAutoWalk.Project(origin,[],geom) is null,"no path means no input");
        var transport=new PathStep(new(3201,3200),
            new MapTransport(origin,new(3201,3200),"Climb","ladder"));
        Require(RuneLiteAutoWalk.Project(origin,[transport,..east.Skip(1)],geom) is null,
            "a transport cannot be passed by a raw minimap click");
        Require(RuneLiteAutoWalk.Project(origin,[new(new(3208,3208))],geom) is null,
            "unverified jumping edges cannot be clicked");
        Require(!RuneLiteAutoWalk.ValidGeometry(geom with{PlayerCenter=new(10,90)})&&
                !RuneLiteAutoWalk.ValidGeometry(geom with{PixelsPerTile=0}),
            "off-screen or uncalibrated geometry is not accepted");
        using(var screenshot=new Bitmap(820,530))
        {
            Require(!RuneLiteMinimapSetupDialog.IsWhitePlayerMark(screenshot,new(650,92)),
                "blank image cannot be a confirmed player minimap marker");
            screenshot.SetPixel(650,92,Color.White);
            screenshot.SetPixel(651,92,Color.White);
            Require(RuneLiteMinimapSetupDialog.IsWhitePlayerMark(screenshot,new(650,92)),
                "manual minimap calibration requires real visible light pixels");
        }
        var walk=new RuneLiteAutoWalk();var now=DateTimeOffset.UtcNow;
        walk.Start(goal,geom,now);
        Require(!walk.CanDispatch(now,now.AddSeconds(-4)),"an old event never authorizes a click");
        walk.Sent(plan,now,now);
        Require(!walk.CanDispatch(now,now),"never send a second click before event confirmation");
        Require(walk.Observe(new(3202,3200),now.AddMilliseconds(650),now.AddMilliseconds(700)).Status=="PROGRESS_CONFIRMED",
            "API must verify incremental on-path progress");
        Require(walk.Observe(new(3207,3200),now.AddSeconds(2),now.AddSeconds(2)).Status=="WAYPOINT_CONFIRMED",
            "API tile near the selected waypoint enables replanning");
        Require(walk.CanDispatch(now.AddSeconds(2),now.AddSeconds(2)),
            "a fresh new event authorizes only a new independently planned click");
        walk.Sent(plan,now.AddSeconds(2),now.AddSeconds(2));
        Require(walk.Observe(new(3202,3200),now.AddSeconds(2),now.AddSeconds(2)).Status=="IGNORED",
            "a pre-click event cannot corroborate a new input");
        Require(walk.Observe(new(3202,3201),now.AddSeconds(3),now.AddSeconds(3)).Status=="OFF_PATH",
            "unexpected real API coordinates stop the walk");
        Require(walk.Timeout(now.AddSeconds(12))=="POSITION_EVENT_TIMEOUT",
            "stalled plugin feedback is an explicit input failure");
        walk.Stop();Require(!walk.Active&&walk.Timeout(now.AddSeconds(40)) is null,
            "manual stop prevents further movement");
    }
}
