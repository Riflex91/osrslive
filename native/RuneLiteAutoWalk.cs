namespace OSRSLiveDiagnostics;

// First opt-in movement transport for the regular Jagex-Launcher RuneLite.
// World coordinates come exclusively from the verified Plugin Hub API; the
// separately calibrated minimap is used ONLY to project a short click.
// A posted Windows message is never counted as movement until a newer API
// tile event confirms progress along or in a tightly bounded corridor around
// the previously calculated collision path. An off-prefix observation alone
// never authorizes an additional click while the first click is still active.
internal sealed class RuneLiteAutoWalk
{
    internal sealed record Calibration(IntPtr Window,Size ClientSize,Point PlayerCenter,
        double PixelsPerTile);
    internal sealed record ClickPlan(Point Pixel,WorldTile From,WorldTile Waypoint,
        int WaypointIndex,WorldTile[] PathPrefix);
    internal sealed record Feedback(string Status,string Diagnostic,bool Continue);

    private sealed record Pending(ClickPlan Plan,DateTimeOffset EventAt,
        DateTimeOffset SentAt,int LastIndex,DateTimeOffset LastProgressAt,
        WorldTile LastObserved,DateTimeOffset LastObservedEventAt,int CorridorSamples);

    private Pending? pending;
    private DateTimeOffset started;
    private int clicks,corridorReplans;
    internal bool Active{get;private set;}
    internal NavigationDestination? Destination{get;private set;}
    internal Calibration? Geometry{get;private set;}
    internal int Clicks=>clicks;
    internal int CorridorReplans=>corridorReplans;
    internal bool WaitingForMovement=>pending is not null;
    internal static readonly TimeSpan EventTimeout=TimeSpan.FromSeconds(9);
    internal const int MaxClicks=40;
    internal const int MaxCorridorReplans=2;
    internal const int MaxCorridorSamplesPerClick=5;
    internal const int MaxObservedTileJump=4;
    internal const int MaxCorridorDistance=2;

    internal void Start(NavigationDestination destination,Calibration geometry,DateTimeOffset now)
    {
        Stop();
        if(!ValidGeometry(geometry))throw new ArgumentException("Minimap calibration invalid",nameof(geometry));
        Active=true;Destination=destination;Geometry=geometry;started=now;
    }

    internal void Stop()
    {
        Active=false;pending=null;Destination=null;Geometry=null;
        started=default;clicks=0;corridorReplans=0;
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

    // A successful, explicit on-screen minimap calibration provides a narrow
    // one-shot starting attestation. The official exporter has no heartbeat:
    // a stationary player may have a correct, older last API tile. We allow
    // ONE first click, promptly after calibration, from an API event already
    // accepted in this running companion session. No further click can use
    // this exception; every subsequent click requires a <=3-second API tile.
    internal bool CalibratedFirstClickAllowed(DateTimeOffset now,DateTimeOffset eventAt)=>
        Active&&clicks==0&&now-started<=TimeSpan.FromSeconds(12)&&
        eventAt!=default&&now-eventAt>=TimeSpan.Zero&&
        now-eventAt<=TimeSpan.FromSeconds(60);

    internal bool CanDispatch(DateTimeOffset now,DateTimeOffset eventAt)=>
        Active&&pending is null&&clicks<MaxClicks&&
        now-started<TimeSpan.FromMinutes(5)&&eventAt!=default&&
        now-eventAt>=TimeSpan.Zero&&
        (now-eventAt<=TimeSpan.FromSeconds(3)||
         CalibratedFirstClickAllowed(now,eventAt));

    internal void Sent(ClickPlan plan,DateTimeOffset eventAt,DateTimeOffset now)
    {
        if(!CanDispatch(now,eventAt))throw new InvalidOperationException("Stale or busy movement dispatch");
        pending=new(plan,eventAt,now,0,now,plan.From,eventAt,0);clicks++;
    }

    internal Feedback Observe(WorldTile tile,DateTimeOffset eventAt,DateTimeOffset now)
    {
        if(!Active||pending is null||eventAt<=pending.EventAt||eventAt<=pending.SentAt||
           eventAt<=pending.LastObservedEventAt)
            return new("IGNORED","no_post_click_api_event=true",false);
        var p=pending;
        int index=Array.FindIndex(p.Plan.PathPrefix,t=>t==tile)+1;
        // A reread of the same exporter event is NOT a new move. Even a
        // genuinely later event at the same tile is no backwards motion.
        if(tile==p.LastObserved||(index==p.LastIndex&&index>0))
            return new("IGNORED","tile_unchanged_since_last_confirmed_progress=true",false);
        if(index<p.LastIndex&&index>0)
            return new("OFF_PATH","previously_confirmed_route_tile_revisited=true "+
                $"observed={tile.X},{tile.Y},{tile.Plane} last_index={p.LastIndex} current_index={index}",false);
        if(index==0&&p.LastIndex>0&&
            tile.Plane==p.Plan.Waypoint.Plane&&tile.Distance(p.Plan.Waypoint)<=1&&
            p.LastIndex>=Math.Max(2,p.Plan.WaypointIndex/2)&&
            tile.Distance(p.LastObserved)<=MaxObservedTileJump&&
            tile.Distance(p.Plan.Waypoint)<p.LastObserved.Distance(p.Plan.Waypoint))
        {
            pending=null;
            return new("WAYPOINT_NEAR_CONFIRMED",
                $"observed={tile.X},{tile.Y},{tile.Plane} waypoint={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                $"one_tile_endpoint_tolerance=true last_verified_path_index={p.LastIndex} "+
                "requires_fresh_collision_replan=true confirmed_by=NEW_PLUGIN_API_EVENT",true);
        }
        // Live 1.8.28 events often skip TWO TO FOUR tiles between exporter
        // samples, even after a successful SendInput. RuneLite may take the
        // nearby in-game path instead of the pinned public A* prefix. Reject
        // wrong-direction, backward, long-jump and distant observations, but
        // wait for the SAME waypoint rather than interrupting the in-flight
        // click with an early replan. A new click still requires a fresh map
        // route, a newly observed plugin tile, and an empty pending slot.
        if(index==0)
        {
            int jump=tile.Distance(p.LastObserved);
            int before=p.LastObserved.Distance(p.Plan.Waypoint);
            int after=tile.Distance(p.Plan.Waypoint);
            int closestIndex=0,closestDistance=int.MaxValue;
            for(int i=Math.Max(0,p.LastIndex-1);i<p.Plan.PathPrefix.Length;i++)
            {
                int distance=tile.Distance(p.Plan.PathPrefix[i]);
                if(distance<closestDistance){closestDistance=distance;closestIndex=i+1;}
            }
            bool axisConsistent=
                (p.Plan.From.X!=p.Plan.Waypoint.X||tile.X==p.Plan.From.X)&&
                (p.Plan.From.Y!=p.Plan.Waypoint.Y||tile.Y==p.Plan.From.Y);
            bool safeCorridor=tile.Plane==p.Plan.Waypoint.Plane&&
                jump is >=1 and <=MaxObservedTileJump&&after<before&&
                closestDistance<=MaxCorridorDistance&&axisConsistent&&
                (p.LastIndex>=2||tile.Distance(p.Plan.From)>=2)&&
                now-p.LastProgressAt<=EventTimeout;
            if(safeCorridor)
            {
                int samples=p.CorridorSamples+1;
                string diagnostic=$"observed={tile.X},{tile.Y},{tile.Plane} "+
                    $"previous={p.LastObserved.X},{p.LastObserved.Y},{p.LastObserved.Plane} "+
                    $"waypoint={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                    $"tile_jump={jump}/{MaxObservedTileJump} waypoint_distance_before={before} "+
                    $"waypoint_distance_after={after} closest_prefix_index={closestIndex} "+
                    $"closest_prefix_distance={closestDistance}/{MaxCorridorDistance} "+
                    $"last_exact_prefix_index={p.LastIndex} corridor_samples={samples}/{MaxCorridorSamplesPerClick} "+
                    $"event_gap_ms={(eventAt-p.LastObservedEventAt).TotalMilliseconds:0} "+
                    "source=NEW_PLUGIN_API_EVENT screen_position_not_used=true";
                if(after<=1&&(p.LastIndex>=2||p.CorridorSamples>=1||
                    tile.Distance(p.Plan.From)>=Math.Max(2,p.Plan.WaypointIndex/2)))
                {
                    pending=null;
                    return new("WAYPOINT_NEAR_CONFIRMED",diagnostic+
                        " one_tile_endpoint_tolerance=true next_click_requires_fresh_collision_route=true",true);
                }
                if(samples>=MaxCorridorSamplesPerClick)
                {
                    if(corridorReplans<MaxCorridorReplans)
                    {
                        corridorReplans++;pending=null;
                        return new("WAYPOINT_CORRIDOR_REPLAN",diagnostic+
                            $" corridor_replans={corridorReplans}/{MaxCorridorReplans} "+
                            "bounded_cumulative_progress=true next_click_requires_fresh_collision_route=true",true);
                    }
                    return new("OFF_PATH",diagnostic+
                        " corridor_replan_limit_reached=true",false);
                }
                pending=p with{LastObserved=tile,LastObservedEventAt=eventAt,
                    LastProgressAt=now,CorridorSamples=samples};
                return new("CORRIDOR_PROGRESS_CONFIRMED",diagnostic+
                    " waiting_for_same_waypoint=true no_additional_input_sent=true",true);
            }
            return new("OFF_PATH",
                $"tile_outside_verified_forward_corridor=true observed={tile.X},{tile.Y},{tile.Plane} "+
                $"previous={p.LastObserved.X},{p.LastObserved.Y},{p.LastObserved.Plane} "+
                $"waypoint={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                $"tile_jump={jump} waypoint_distance_before={before} waypoint_distance_after={after} "+
                $"closest_prefix_index={closestIndex} closest_prefix_distance={closestDistance} "+
                $"last_exact_prefix_index={p.LastIndex} corridor_samples={p.CorridorSamples} "+
                $"axis_consistent={axisConsistent} event_gap_ms={(eventAt-p.LastObservedEventAt).TotalMilliseconds:0}",false);
        }
        bool reached=index>=p.Plan.WaypointIndex-1;
        if(reached)
        {
            pending=null;
            return new("WAYPOINT_CONFIRMED",
                $"observed={tile.X},{tile.Y},{tile.Plane} expected={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                $"observed_path_index={index} waypoint_index={p.Plan.WaypointIndex} "+
                $"event_gap_ms={(eventAt-p.EventAt).TotalMilliseconds:0} confirmed_by=NEW_PLUGIN_API_EVENT",true);
        }
        pending=p with{LastIndex=index,LastProgressAt=now,
            LastObserved=tile,LastObservedEventAt=eventAt};
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
        Require(BackgroundWindowInput.MouseInputStructSize()==(IntPtr.Size==8?40:28),
            "SendInput MOUSEINPUT and INPUT must match the native architecture layout");
        Require(BackgroundWindowInput.MinimapResetPoint(new(645,111))==new Point(625,95),
            "zoom-reset click is inside the minimap, not the 3D world viewport");
        Require(BackgroundWindowInput.TryNormalizeAbsolute(-1920,0,-1920,-100,3840,2160,
                    out int firstX,out int firstY)&&firstX==0&&firstY>0&&
                BackgroundWindowInput.TryNormalizeAbsolute(1919,2059,-1920,-100,3840,2160,
                    out int endX,out int endY)&&endX==65535&&endY==65535&&
                !BackgroundWindowInput.TryNormalizeAbsolute(1920,100,-1920,-100,3840,2160,
                    out _,out _),
            "foreground click mapping respects negative-origin multi-monitor virtual desktops");
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
        Require(walk.CalibratedFirstClickAllowed(now,now.AddSeconds(-33))&&
                walk.CanDispatch(now,now.AddSeconds(-33)),
            "explicit manual minimap calibration allows exactly one carefully bounded first click from a same-session last-known API tile");
        Require(!walk.CanDispatch(now,now.AddSeconds(-61))&&
                !walk.CanDispatch(now.AddSeconds(13),now.AddSeconds(-4)),
            "no override for events older than a minute or more than 12 seconds after calibration");
        walk.Sent(plan,now.AddSeconds(-33),now);
        Require(!walk.CanDispatch(now,now),"never send a second click before event confirmation");
        Require(walk.Observe(new(3202,3200),now.AddMilliseconds(650),now.AddMilliseconds(700)).Status=="PROGRESS_CONFIRMED",
            "API must verify incremental on-path progress");
        Require(walk.Observe(new(3202,3200),now.AddMilliseconds(650),now.AddMilliseconds(850)).Status=="IGNORED"&&
                walk.Observe(new(3202,3200),now.AddMilliseconds(920),now.AddMilliseconds(950)).Status=="IGNORED",
            "reread exporter events and a repeated same-tile event cannot cause a false off-path stop");
        Require(walk.Observe(new(3207,3200),now.AddSeconds(2),now.AddSeconds(2)).Status=="WAYPOINT_CONFIRMED",
            "API tile near the selected waypoint enables replanning");
        Require(walk.CanDispatch(now.AddSeconds(2),now.AddSeconds(2)),
            "a fresh new event authorizes only a new independently planned click");
        Require(!walk.CalibratedFirstClickAllowed(now.AddSeconds(2),now.AddSeconds(-7))&&
                !walk.CanDispatch(now.AddSeconds(2),now.AddSeconds(-7)),
            "after first dispatch, an old event can never authorize another click");
        walk.Sent(plan,now.AddSeconds(2),now.AddSeconds(2));
        Require(walk.Observe(new(3202,3200),now.AddSeconds(2),now.AddSeconds(2)).Status=="IGNORED",
            "a pre-click event cannot corroborate a new input");
        Require(walk.Observe(new(3202,3201),now.AddSeconds(3),now.AddSeconds(3)).Status=="OFF_PATH",
            "unexpected real API coordinates stop the walk");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(plan,now,now);
        Require(walk.Observe(new(3205,3200),now.AddSeconds(1),now.AddSeconds(1)).Status=="PROGRESS_CONFIRMED"&&
                walk.Observe(new(3209,3200),now.AddSeconds(2),now.AddSeconds(2)).Status=="WAYPOINT_NEAR_CONFIRMED"&&
                !walk.WaitingForMovement,
            "one-tile overshoot near a far waypoint after confirmed path progress is replanned safely");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(plan,now,now);
        Require(walk.Observe(new(3200,3201),now.AddSeconds(1),now.AddSeconds(1)).Status=="OFF_PATH",
            "early off-route observations are still rejected even if close to the start");
        // Exact live 1.8.27 geometry: RuneLite confirmed four A* prefix
        // steps but then used one adjacent forward tile not on the prefix.
        var liveOrigin=new WorldTile(3242,3188);
        PathStep[] livePrefix=[
            new(new(3243,3189)),new(new(3244,3190)),
            new(new(3244,3191)),new(new(3244,3192)),
            new(new(3244,3193)),new(new(3244,3194)),
            new(new(3243,3194)),new(new(3242,3194)),new(new(3242,3195))];
        var livePlan=RuneLiteAutoWalk.Project(liveOrigin,livePrefix,geom)!;
        Require(livePlan is not null&&livePlan.Waypoint==new WorldTile(3242,3195)&&
                livePlan.WaypointIndex==9,
            "live-test corridor waypoint is built entirely from adjacent collision steps");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(livePlan,now,now);
        Require(walk.Observe(new(3244,3192),now.AddSeconds(1),now.AddSeconds(1)).Status=="PROGRESS_CONFIRMED",
            "first on-prefix progress must be independently confirmed before a detour");
        var corridor=walk.Observe(new(3243,3193),now.AddSeconds(2),now.AddSeconds(2));
        Require(corridor.Status=="CORRIDOR_PROGRESS_CONFIRMED"&&corridor.Continue&&
                walk.CorridorReplans==0&&walk.WaitingForMovement&&
                !walk.CanDispatch(now.AddSeconds(2),now.AddSeconds(2)),
            "live one-tile detour is accepted without interrupting the still-moving game click");
        Require(walk.Observe(new(3242,3194),now.AddSeconds(3),now.AddSeconds(3)).Status==
                "WAYPOINT_CONFIRMED"&&!walk.WaitingForMovement,
            "after an off-prefix observation a real API waypoint sample releases the next click");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(livePlan,now,now);
        walk.Observe(new(3244,3192),now.AddSeconds(1),now.AddSeconds(1));
        Require(walk.Observe(new(3245,3193),now.AddSeconds(2),now.AddSeconds(2)).Status=="OFF_PATH",
            "one adjacent tile moving no closer to the waypoint is still a hard stop");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(livePlan,now,now);
        walk.Observe(new(3243,3189),now.AddSeconds(1),now.AddSeconds(1));
        Require(walk.Observe(new(3244,3189),now.AddSeconds(2),now.AddSeconds(2)).Status=="OFF_PATH",
            "an early unverified sideways step must never trigger the new detour replan");
        Require(walk.Timeout(now.AddSeconds(12))=="POSITION_EVENT_TIMEOUT",
            "stalled plugin feedback is an explicit input failure");
        // Real 1.8.28 first off-prefix events: public A* wanted east+north,
        // RuneLite moved 2 tiles north, then continued near that route.
        var northStart=new WorldTile(3230,3167);
        var northPath=new[]{new WorldTile(3231,3168),new(3232,3169),
            new(3232,3170),new(3232,3171),new(3232,3172),
            new(3232,3173),new(3232,3174),new(3232,3175)};
        var northPlan=RuneLiteAutoWalk.Project(northStart,
            northPath.Select(t=>new PathStep(t)).ToArray(),geom)!;
        Require(northPlan.Waypoint==new WorldTile(3232,3175),
            "recreate the second-click destination from the real 1.8.28 test");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(northPlan,now,now);
        Require(walk.Observe(new(3230,3169),now.AddSeconds(1),now.AddSeconds(1)).Status==
                "CORRIDOR_PROGRESS_CONFIRMED"&&walk.WaitingForMovement,
            "two-tile first exporter sample in nearby RuneLite corridor is real progress, not a stop");
        Require(walk.Observe(new(3230,3171),now.AddSeconds(2),now.AddSeconds(2)).Status==
                "CORRIDOR_PROGRESS_CONFIRMED"&&walk.Clicks==1,
            "a second still-forward game update must not cause an extra foreground click");
        Require(walk.Observe(new(3232,3175),now.AddSeconds(3),now.AddSeconds(3)).Status==
                "WAYPOINT_CONFIRMED"&&!walk.WaitingForMovement,
            "only a later independently observed waypoint releases the pending command");
        // In another real test, the FIRST post-click event was four tiles
        // ahead and two tiles away from the A* path, still closer to target.
        var diagonalStart=new WorldTile(3232,3174);
        var diagonalPath=new[]{new WorldTile(3233,3175),new(3234,3176),
            new(3235,3177),new(3235,3178),new(3235,3179),
            new(3236,3180),new(3236,3181)};
        var diagonalPlan=RuneLiteAutoWalk.Project(diagonalStart,
            diagonalPath.Select(t=>new PathStep(t)).ToArray(),geom)!;
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(diagonalPlan,now,now);
        Require(walk.Observe(new(3233,3178),now.AddSeconds(1),now.AddSeconds(1)).Status==
                "CORRIDOR_PROGRESS_CONFIRMED",
            "four-tile API sampling gap within two tiles of planned route is accepted");
        Require(walk.Observe(new(3236,3181),now.AddSeconds(2),now.AddSeconds(2)).Status==
                "WAYPOINT_CONFIRMED","later exact path waypoint still confirms progress");
        var eastNorthStart=new WorldTile(3216,3163);
        var eastNorthPath=Enumerable.Range(1,8)
            .Select(i=>new PathStep(new WorldTile(3216+i,3164))).ToArray();
        var eastNorthPlan=RuneLiteAutoWalk.Project(eastNorthStart,eastNorthPath,geom)!;
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(eastNorthPlan,now,now);
        Require(walk.Observe(new(3220,3163),now.AddSeconds(1),now.AddSeconds(1)).Status==
                "CORRIDOR_PROGRESS_CONFIRMED",
            "eastbound RuneLite movement one tile south of prefix remains a bounded corridor");
        Require(walk.Observe(new(3224,3164),now.AddSeconds(2),now.AddSeconds(2)).Status==
                "WAYPOINT_CONFIRMED","a subsequent exact waypoint closes that pending click");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(eastNorthPlan,now,now);
        Require(walk.Observe(new(3222,3163),now.AddSeconds(1),now.AddSeconds(1)).Status==
                "OFF_PATH","a six-tile jump in one exporter event remains a hard stop");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(plan,now,now);
        Require(walk.Observe(new(3202,3201),now.AddSeconds(1),now.AddSeconds(1)).Status==
                "OFF_PATH","pure east click must not silently accept unrelated northward drift");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(eastNorthPlan,now,now);
        Require(walk.Observe(new(3216,3166),now.AddSeconds(1),now.AddSeconds(1)).Status==
                "OFF_PATH","jump far from future collision-prefix tiles remains blocked");
        // Only a sustained series of forward near-prefix observations may
        // replan before reaching the waypoint, and at most twice per arm.
        var longStart=new WorldTile(3200,3200);
        var longPath=new[]{new WorldTile(3201,3201),new(3202,3202),
            new(3203,3202),new(3204,3202),new(3205,3202),
            new(3206,3202),new(3207,3202),new(3208,3202)};
        var longPlan=RuneLiteAutoWalk.Project(longStart,
            longPath.Select(t=>new PathStep(t)).ToArray(),geom)!;
        WorldTile[] offSamples=[new(3202,3200),new(3203,3200),new(3204,3200),
            new(3205,3200),new(3206,3200)];
        walk.Stop();walk.Start(goal,geom,now);
        for(int i=0;i<=RuneLiteAutoWalk.MaxCorridorReplans;i++)
        {
            var sentAt=now.AddSeconds(i*7);
            walk.Sent(longPlan,sentAt,sentAt);
            for(int j=0;j<offSamples.Length;j++)
            {
                var result=walk.Observe(offSamples[j],sentAt.AddSeconds(j+1),sentAt.AddSeconds(j+1));
                string expected=j<offSamples.Length-1?"CORRIDOR_PROGRESS_CONFIRMED":
                    i<RuneLiteAutoWalk.MaxCorridorReplans?"WAYPOINT_CORRIDOR_REPLAN":"OFF_PATH";
                Require(result.Status==expected,
                    "bounded forward API corridor allows at most two deferred collision replans per arm");
            }
        }
        walk.Stop();Require(!walk.Active&&walk.Timeout(now.AddSeconds(40)) is null,
            "manual stop prevents further movement");
    }
}
