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
        WorldTile LastObserved,DateTimeOffset LastObservedEventAt,int CorridorSamples,
        bool TransitionGraceEligible,int TransitionDetourSamples,int NearEndpointLateralSamples);

    private Pending? pending;
    private DateTimeOffset started;
    private DateTimeOffset quietReplanRequestedAt;
    private bool lastWaypointConfirmed;
    private bool shortNextClick;
    private int clicks,corridorReplans;
    internal bool Active{get;private set;}
    internal NavigationDestination? Destination{get;private set;}
    internal Calibration? Geometry{get;private set;}
    internal int Clicks=>clicks;
    internal int CorridorReplans=>corridorReplans;
    internal bool WaitingForMovement=>pending is not null;
    internal bool ShortNextClick=>shortNextClick;
    internal static readonly TimeSpan EventTimeout=TimeSpan.FromSeconds(9);
    internal const int MaxClicks=40;
    internal const int MaxCorridorReplans=4;
    internal const int RecoveryPrefixSteps=6;
    internal const int MaxCorridorSamplesPerClick=5;
    internal const int MaxTransitionDetourSamples=5;
    internal const int MaxTransitionDetourDistance=4;
    internal const int MaxNearEndpointLateralSamples=1;
    internal const int MaxObservedTileJump=4;
    internal const int MaxCorridorDistance=2;
    internal static readonly TimeSpan NearWaypointStallDelay=TimeSpan.FromMilliseconds(1600);

    internal void Start(NavigationDestination destination,Calibration geometry,DateTimeOffset now)
    {
        Stop();
        if(!ValidGeometry(geometry))throw new ArgumentException("Minimap calibration invalid",nameof(geometry));
        Active=true;Destination=destination;Geometry=geometry;started=now;
    }

    internal void Stop()
    {
        Active=false;pending=null;Destination=null;Geometry=null;
        started=default;quietReplanRequestedAt=default;lastWaypointConfirmed=false;
        shortNextClick=false;
        clicks=0;corridorReplans=0;
    }

    internal static bool ValidGeometry(Calibration c)=>
        c.Window!=IntPtr.Zero&&c.ClientSize.Width>=640&&c.ClientSize.Height>=350&&
        c.PlayerCenter.X>=40&&c.PlayerCenter.X<c.ClientSize.Width-40&&
        c.PlayerCenter.Y is >=35 and <=220&&
        double.IsFinite(c.PixelsPerTile)&&c.PixelsPerTile is >=3 and <=16;

    internal static ClickPlan? Project(WorldTile from,PathStep[] path,Calibration geometry,
        int maxPrefixSteps=9)
    {
        if(!ValidGeometry(geometry)||path.Length==0||maxPrefixSteps is <2 or >9)return null;
        ClickPlan? chosen=null;
        WorldTile preceding=from;
        var prefix=new List<WorldTile>();
        foreach(var step in path.Take(maxPrefixSteps))
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

    // The safe general minimap radius starts at seven pixels; at the very
    // end of a verified route, the final walkable tile is only 4–6 pixels
    // from the player's white dot. This narrowly scoped exception is valid
    // ONLY if that one adjacent collision tile enters the selected goal
    // radius, and no transport or plane change is involved. Never extend
    // the click past the final tile or guess another map location.
    internal static ClickPlan? ProjectFinalGoalStep(WorldTile from,PathStep[] path,
        Calibration geometry,NavigationDestination destination)
    {
        if(!ValidGeometry(geometry)||path.Length!=1||
           from.Plane!=destination.Tile.Plane||
           from.Distance(destination.Tile)<=destination.Radius)return null;
        var step=path[0];
        if(step.Transport is not null||step.Tile.Plane!=from.Plane||
           from.Distance(step.Tile)!=1||
           step.Tile.Distance(destination.Tile)>destination.Radius)return null;
        int dx=step.Tile.X-from.X,dy=from.Y-step.Tile.Y;
        double radius=geometry.PixelsPerTile*Math.Sqrt(dx*dx+dy*dy);
        if(radius<3||radius>=7)return null;
        var pixel=new Point(geometry.PlayerCenter.X+(int)Math.Round(dx*geometry.PixelsPerTile),
            geometry.PlayerCenter.Y+(int)Math.Round(dy*geometry.PixelsPerTile));
        if(pixel.X<25||pixel.Y<25||pixel.X>=geometry.ClientSize.Width-25||
           pixel.Y>=geometry.ClientSize.Height-25)return null;
        return new(pixel,from,step.Tile,1,[step.Tile]);
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
        pending=new(plan,eventAt,now,0,now,plan.From,eventAt,0,
            lastWaypointConfirmed,0,0);
        lastWaypointConfirmed=false;
        quietReplanRequestedAt=default;shortNextClick=false;clicks++;
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
            pending=null;lastWaypointConfirmed=true;
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
            // An equal start/end axis does NOT imply a straight path: the
            // actual pinned A* prefix may first detour around an obstacle.
            // Allow only axis values already spanned by the verified prefix
            // for that case, never a new excursion beyond its bounding box.
            bool axisConsistent=
                (p.Plan.From.X!=p.Plan.Waypoint.X||
                    tile.X>=Math.Min(p.Plan.From.X,p.Plan.PathPrefix.Min(t=>t.X))&&
                    tile.X<=Math.Max(p.Plan.From.X,p.Plan.PathPrefix.Max(t=>t.X)))&&
                (p.Plan.From.Y!=p.Plan.Waypoint.Y||
                    tile.Y>=Math.Min(p.Plan.From.Y,p.Plan.PathPrefix.Min(t=>t.Y))&&
                    tile.Y<=Math.Max(p.Plan.From.Y,p.Plan.PathPrefix.Max(t=>t.Y)));
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
                    pending=null;lastWaypointConfirmed=true;
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
            // In two independent v1.8.32 live runs a long verified prefix
            // was followed by ONE sideways step at Chebyshev distance two
            // from the click's endpoint. A client-selected OSRS route need
            // not choose the same intermediate tiles as our public A* route.
            // Do not count a lateral step as forward progress or success,
            // and NEVER send another click merely because it was observed.
            // It may only hold the existing click briefly; an exact waypoint
            // or the existing time-limited, fresh-API quiet replan is needed
            // before another game input is permitted.
            bool nearEndpointLateral=tile.Plane==p.Plan.Waypoint.Plane&&
                p.NearEndpointLateralSamples<MaxNearEndpointLateralSamples&&
                p.LastIndex>=Math.Max(3,p.Plan.WaypointIndex/2)&&
                jump==1&&before==2&&after==2&&
                closestDistance<=MaxCorridorDistance&&
                closestIndex>=p.LastIndex&&
                now-p.SentAt<=TimeSpan.FromSeconds(5)&&
                now-p.LastProgressAt<=EventTimeout&&
                eventAt-p.LastObservedEventAt<=TimeSpan.FromSeconds(2);
            if(nearEndpointLateral)
            {
                pending=p with{LastObserved=tile,LastObservedEventAt=eventAt,
                    LastProgressAt=now,NearEndpointLateralSamples=p.NearEndpointLateralSamples+1};
                return new("NEAR_WAYPOINT_LATERAL_OBSERVED",
                    $"observed={tile.X},{tile.Y},{tile.Plane} "+
                    $"previous={p.LastObserved.X},{p.LastObserved.Y},{p.LastObserved.Plane} "+
                    $"waypoint={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                    $"tile_jump={jump} waypoint_distance_before={before} waypoint_distance_after={after} "+
                    $"closest_prefix_index={closestIndex} closest_prefix_distance={closestDistance} "+
                    $"last_exact_prefix_index={p.LastIndex} "+
                    $"lateral_samples={p.NearEndpointLateralSamples+1}/{MaxNearEndpointLateralSamples} "+
                    "side_step_is_not_route_confirmation=true no_additional_input_sent=true "+
                    "wait_for_exact_waypoint_or_bounded_fresh_api_replan=true",true);
            }
            // The real 1.8.30 run reached an exact first waypoint, sent a
            // second click, and RuneLite immediately took a short northward
            // detour before continuing west. This is NOT route confirmation.
            // After a previously verified waypoint only, observe a few
            // tightly bounded off-route plugin tiles WITHOUT sending any
            // more clicks; an exact future waypoint must still confirm the
            // action or the original nine-second timeout will stop it.
            bool shortTransitionDetour=p.TransitionGraceEligible&&
                p.LastIndex==0&&p.CorridorSamples==0&&
                p.TransitionDetourSamples<MaxTransitionDetourSamples&&
                p.Plan.WaypointIndex>=4&&
                tile.Plane==p.Plan.Waypoint.Plane&&
                jump is >=1 and <=MaxObservedTileJump&&
                closestDistance is >=3 and <=MaxTransitionDetourDistance&&
                tile.Distance(p.Plan.From)<=8&&after<=before+1&&
                now-p.SentAt<=TimeSpan.FromSeconds(4)&&
                now-p.LastProgressAt<=EventTimeout;
            if(shortTransitionDetour)
            {
                int samples=p.TransitionDetourSamples+1;
                pending=p with{LastObserved=tile,LastObservedEventAt=eventAt,
                    TransitionDetourSamples=samples};
                return new("TRANSITION_DETOUR_OBSERVED",
                    $"observed={tile.X},{tile.Y},{tile.Plane} "+
                    $"previous={p.LastObserved.X},{p.LastObserved.Y},{p.LastObserved.Plane} "+
                    $"from={p.Plan.From.X},{p.Plan.From.Y},{p.Plan.From.Plane} "+
                    $"waypoint={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
                    $"tile_jump={jump}/{MaxObservedTileJump} "+
                    $"waypoint_distance_before={before} waypoint_distance_after={after} "+
                    $"closest_prefix_distance={closestDistance}/{MaxTransitionDetourDistance} "+
                    $"detour_samples={samples}/{MaxTransitionDetourSamples} "+
                    $"time_since_click_ms={(now-p.SentAt).TotalMilliseconds:0} "+
                    "previous_waypoint_plugin_confirmed=true current_route_unverified=true "+
                    "no_additional_input_sent=true no_timeout_extension=true "+
                    "requires_future_exact_waypoint_event=true",true);
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
            pending=null;lastWaypointConfirmed=true;
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

    // The RuneLite position exporter emits position CHANGES, not a standing
    // heartbeat. Two real 1.8.29 runs stopped receiving events exactly two
    // tiles before the clicked waypoint. One narrowly bounded recovery is
    // permitted after several independently observed post-click steps and
    // a quiet period: replan from the LAST verified plugin tile while that
    // event is still <=3s old, before the normal 9s hard timeout. This is
    // not proof of idleness; the log explicitly preserves that distinction.
    // No position is inferred from the minimap or a screenshot.
    internal Feedback? RecoverNearWaypointAfterQuietPeriod(WorldTile tile,
        DateTimeOffset eventAt,DateTimeOffset now)
    {
        if(!Active||pending is null)return null;
        var p=pending;
        if(tile!=p.LastObserved||eventAt!=p.LastObservedEventAt||
           now-p.LastProgressAt>EventTimeout||
           now-p.SentAt<TimeSpan.FromSeconds(3)||
           now-eventAt<TimeSpan.Zero||now-eventAt>TimeSpan.FromSeconds(3))
            return null;
        bool nearWaypoint=p.LastObserved.Distance(p.Plan.Waypoint)==2&&
            (p.LastIndex>=2||p.CorridorSamples>=2)&&
            now-p.LastProgressAt>=NearWaypointStallDelay;
        // v1.8.34 live (29 Oct, 18:02): after a confirmed waypoint,
        // the game took three genuine off-prefix movement observations
        // 3255,3244 -> 3254,3240 -> 3255,3239. They progressed toward
        // Lumbridge but never touched the originally clicked waypoint.
        // Waiting for that exact waypoint indefinitely cannot recover.
        // A last API tile which advanced toward BOTH destination and
        // clicked waypoint can seed ONE bounded, short-hop replan after
        // a quiet window. The game may still be moving: the observer
        // has no heartbeat, so never claim stationary or arrival here.
        bool transitionDetour=p.TransitionGraceEligible&&
            p.TransitionDetourSamples>=3&&p.LastIndex==0&&
            p.CorridorSamples==0&&
            Destination is { } destination&&
            tile.Plane==p.Plan.From.Plane&&tile.Plane==destination.Tile.Plane&&
            tile.Distance(p.Plan.From)>=3&&
            tile.Distance(p.Plan.Waypoint)<p.Plan.From.Distance(p.Plan.Waypoint)&&
            tile.Distance(destination.Tile)<p.Plan.From.Distance(destination.Tile)&&
            now-eventAt>=NearWaypointStallDelay&&
            now-p.SentAt<=EventTimeout;
        if(!nearWaypoint&&!transitionDetour)return null;
        string diagnostic=$"last_confirmed={tile.X},{tile.Y},{tile.Plane} "+
            $"from={p.Plan.From.X},{p.Plan.From.Y},{p.Plan.From.Plane} "+
            $"waypoint={p.Plan.Waypoint.X},{p.Plan.Waypoint.Y},{p.Plan.Waypoint.Plane} "+
            $"waypoint_distance={tile.Distance(p.Plan.Waypoint)} "+
            $"last_exact_prefix_index={p.LastIndex} "+
            $"corridor_samples={p.CorridorSamples} "+
            $"transition_detour_samples={p.TransitionDetourSamples} "+
            $"quiet_ms={(now-(transitionDetour?eventAt:p.LastProgressAt)).TotalMilliseconds:0} "+
            $"last_api_event_age_ms={(now-eventAt).TotalMilliseconds:0} "+
            $"time_since_click_ms={(now-p.SentAt).TotalMilliseconds:0} "+
            $"recovery_attempts={corridorReplans}/{MaxCorridorReplans} "+
            "no_new_position_event_since_last_observation=true "+
            "stationary_not_independently_proven=true "+
            "source=RECENT_PLUGIN_API_TILE screenshot_position_estimation=false ";
        if(corridorReplans>=MaxCorridorReplans)
            return new("RECOVERY_LIMIT_REACHED",diagnostic+
                "no_additional_input_permitted=true",false);
        corridorReplans++;pending=null;quietReplanRequestedAt=now;
        shortNextClick=transitionDetour;
        return new(transitionDetour?"TRANSITION_DETOUR_QUIET_REPLAN":
                p.NearEndpointLateralSamples>0?
                "WAYPOINT_LATERAL_QUIET_REPLAN":"WAYPOINT_QUIET_REPLAN",diagnostic+
            $"recovery_attempts_after={corridorReplans}/{MaxCorridorReplans} "+
            $"near_endpoint_lateral_observed={p.NearEndpointLateralSamples>0} "+
            $"short_next_click={shortNextClick} "+
            "next_click_requires_verified_collision_route_and_foreground=true",true);
    }

    internal string? Timeout(DateTimeOffset now)
    {
        if(!Active)return null;
        if(clicks>=MaxClicks&&pending is null)return "CLICK_LIMIT_REACHED";
        if(now-started>TimeSpan.FromMinutes(5))return "RUN_LIMIT_REACHED";
        if(quietReplanRequestedAt!=default&&pending is null&&
           now-quietReplanRequestedAt>TimeSpan.FromSeconds(3))
            return "QUIET_REPLAN_EXPIRED";
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
        var lumbridge=new NavigationDestination("Lumbridge","safe",new(3222,3218),4);
        var finalFrom=new WorldTile(3227,3219);
        PathStep[] finalPath=[new(new(3226,3218))];
        var finalStep=RuneLiteAutoWalk.ProjectFinalGoalStep(finalFrom,finalPath,geom,lumbridge);
        Require(RuneLiteAutoWalk.Project(finalFrom,finalPath,geom) is null&&
                finalStep?.Pixel==new Point(646,96)&&
                finalStep.Waypoint==new WorldTile(3226,3218)&&finalStep.WaypointIndex==1,
            "the exact last one-tile walk into the destination radius is clickable without extending the route");
        Require(RuneLiteAutoWalk.ProjectFinalGoalStep(finalFrom,[..finalPath,..finalPath],geom,lumbridge) is null&&
                RuneLiteAutoWalk.ProjectFinalGoalStep(finalFrom,[new(new(3227,3218))],geom,lumbridge) is null&&
                RuneLiteAutoWalk.ProjectFinalGoalStep(finalFrom,[new(new(3226,3218),
                    new MapTransport(finalFrom,new(3226,3218),"Climb","ladder"))],geom,lumbridge) is null&&
                RuneLiteAutoWalk.ProjectFinalGoalStep(finalFrom,finalPath,geom,
                    lumbridge with{Radius=2}) is null,
            "only one safe adjacent collision tile that actually enters the goal radius gets the close-click exception");
        Require(BackgroundWindowInput.IsManualMouseInterruption(
                    new(false,"PHYSICAL_MOUSE_BUTTON_HELD",0))&&
                BackgroundWindowInput.IsManualMouseInterruption(
                    new(false,"PHYSICAL_MOUSE_BUTTON_HELD_BEFORE_CLICK",1))&&
                !BackgroundWindowInput.IsManualMouseInterruption(
                    new(false,"RUNELITE_NOT_FOREGROUND",0))&&
                !BackgroundWindowInput.IsManualMouseInterruption(
                    new(true,"PHYSICAL_MOUSE_BUTTON_HELD",3)),
            "mouse-held interruption must not be reported as an automated navigation defect");
        Require(BackgroundWindowInput.MouseInputStructSize()==(IntPtr.Size==8?40:28),
            "SendInput MOUSEINPUT and INPUT must match the native architecture layout");
        RuneLiteInputShield.SelfTest();
        RuneLiteQueuedStartTests.Run();
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
        WorldTile[] northPath=[new(3231,3168),new(3232,3169),
            new(3232,3170),new(3232,3171),new(3232,3172),
            new(3232,3173),new(3232,3174),new(3232,3175)];
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
        WorldTile[] diagonalPath=[new(3233,3175),new(3234,3176),
            new(3235,3177),new(3235,3178),new(3235,3179),
            new(3236,3180),new(3236,3181)];
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
        // Reconstruct the second v1.8.30 observed click: RuneLite completed
        // a verified waypoint and then initially walked north-west around
        // a collision obstacle, three or four tiles off the A* prefix.
        // Accept a few real tiles as UNVERIFIED transition observations
        // without any extra click; an exact waypoint must still arrive.
        var firstDetourFrom=new WorldTile(3259,3235);
        WorldTile[] beforeDetour=[new(3259,3234),new(3259,3233),new(3259,3232),
            new(3259,3231),new(3259,3230),new(3258,3229),new(3258,3228),new(3257,3227)];
        var priorPlan=RuneLiteAutoWalk.Project(firstDetourFrom,
            beforeDetour.Select(t=>new PathStep(t)).ToArray(),geom)!;
        var nextDetourFrom=new WorldTile(3257,3227);
        WorldTile[] afterDetour=[new(3256,3226),new(3255,3226),new(3254,3226),
            new(3253,3225),new(3252,3226),new(3251,3226),new(3250,3226),new(3249,3226)];
        var nextPlan=RuneLiteAutoWalk.Project(nextDetourFrom,
            afterDetour.Select(t=>new PathStep(t)).ToArray(),geom)!;
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(nextPlan,now,now);
        Require(walk.Observe(new(3258,3229),now.AddSeconds(.6),now.AddSeconds(.8)).Status==
                "OFF_PATH","first click without a previously confirmed waypoint may not ignore wrong-way motion");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(priorPlan,now,now);
        Require(walk.Observe(new(3259,3233),now.AddSeconds(.6),now.AddSeconds(.8)).Status==
                "PROGRESS_CONFIRMED","transition prerequisite begins with verified real movement");
        Require(walk.Observe(new(3257,3227),now.AddSeconds(2),now.AddSeconds(2.2)).Status==
                "WAYPOINT_CONFIRMED","previous RuneLite waypoint is truly reached before a transition exception");
        walk.Sent(nextPlan,now.AddSeconds(2),now.AddSeconds(2.2));
        WorldTile[] detourObserved=[new(3258,3229),new(3257,3230),
            new(3253,3230),new(3251,3229)];
        for(int i=0;i<detourObserved.Length;i++)
        {
            var observed=walk.Observe(detourObserved[i],now.AddSeconds(2.8+i*.6),
                now.AddSeconds(3+i*.6));
            Require(observed.Status=="TRANSITION_DETOUR_OBSERVED"&&observed.Continue&&
                    walk.WaitingForMovement&&walk.Clicks==2&&
                    !walk.CanDispatch(now.AddSeconds(3+i*.6),now.AddSeconds(2.8+i*.6)),
                "real bounded transition detour is observed without dispatching another click");
        }
        Require(walk.Observe(new(3249,3226),now.AddSeconds(5.4),now.AddSeconds(5.5)).Status==
                "WAYPOINT_CONFIRMED"&&!walk.WaitingForMovement,
            "only a subsequent true plugin waypoint sample confirms the circuitous click");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(priorPlan,now,now);
        walk.Observe(new(3257,3227),now.AddSeconds(2),now.AddSeconds(2));
        walk.Sent(nextPlan,now.AddSeconds(2),now.AddSeconds(2));
        Require(walk.Observe(new(3262,3232),now.AddSeconds(2.6),now.AddSeconds(2.7)).Status==
                "OFF_PATH","distant or too-large wrong-way movements still stop at the first event");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(priorPlan,now,now);
        walk.Observe(new(3257,3227),now.AddSeconds(2),now.AddSeconds(2));
        walk.Sent(nextPlan,now.AddSeconds(2),now.AddSeconds(2));
        for(int i=0;i<=RuneLiteAutoWalk.MaxTransitionDetourSamples;i++)
        {
            var alternating=i%2==0?new WorldTile(3258,3229):new WorldTile(3257,3230);
            string expected=i<RuneLiteAutoWalk.MaxTransitionDetourSamples?
                "TRANSITION_DETOUR_OBSERVED":"OFF_PATH";
            Require(walk.Observe(alternating,now.AddSeconds(2.4+i*.6),
                        now.AddSeconds(2.6+i*.6)).Status==expected,
                "a loop of unsupported transition tiles has a strict five-event limit");
        }
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(priorPlan,now,now);
        walk.Observe(new(3257,3227),now.AddSeconds(2),now.AddSeconds(2));
        walk.Sent(nextPlan,now.AddSeconds(2),now.AddSeconds(2));
        Require(walk.Observe(new(3258,3229),now.AddSeconds(2.6),now.AddSeconds(2.8)).Status==
                "TRANSITION_DETOUR_OBSERVED"&&
                walk.Timeout(now.AddSeconds(12))=="POSITION_EVENT_TIMEOUT",
            "transition grace never extends the original hard position timeout");
        // Only a sustained series of forward near-prefix observations may
        // replan before reaching the waypoint, and at most four times per arm.
        var longStart=new WorldTile(3200,3200);
        WorldTile[] longPath=[new(3201,3201),new(3202,3202),
            new(3203,3202),new(3204,3202),new(3205,3202),
            new(3206,3202),new(3207,3202),new(3208,3202)];
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
                    "bounded forward API corridor allows at most four deferred collision replans per arm");
            }
        }
        // Exact v1.8.29 live stop: from (3233,3177) toward (3237,3183),
        // three valid Plugin Hub events reached (3235,3182), then no newer
        // tile for >5s. A retry is legal ONLY during a short fresh-event
        // window and after several independently confirmed movements.
        var quietStart=new WorldTile(3233,3177);
        WorldTile[] quietPath=[new(3234,3178),new(3234,3179),
            new(3235,3180),new(3236,3181),new(3237,3182),new(3237,3183)];
        var quietPlan=RuneLiteAutoWalk.Project(quietStart,
            quietPath.Select(t=>new PathStep(t)).ToArray(),geom)!;
        Require(quietPlan.Waypoint==new WorldTile(3237,3183),
            "reconstructed stalled waypoint comes from adjacent collision steps");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(quietPlan,now,now);
        Require(walk.RecoverNearWaypointAfterQuietPeriod(quietStart,now,now.AddSeconds(4)) is null,
            "a stationary starting point with no post-click API feedback never authorizes retry");
        walk.Observe(new(3233,3179),now.AddSeconds(.6),now.AddSeconds(.8));
        walk.Observe(new(3234,3181),now.AddSeconds(1.3),now.AddSeconds(1.6));
        walk.Observe(new(3235,3182),now.AddSeconds(2),now.AddSeconds(2.3));
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3235,3182),now.AddSeconds(2),
                    now.AddSeconds(3.5)) is null&&walk.WaitingForMovement,
            "no recovery while the post-click event has only briefly gone quiet");
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3235,3182),now.AddSeconds(1.3),
                    now.AddSeconds(4.2)) is null,
            "an outdated or different event timestamp cannot impersonate the final verified tile");
        var quietRetry=walk.RecoverNearWaypointAfterQuietPeriod(new(3235,3182),
            now.AddSeconds(2),now.AddSeconds(4.2));
        Require(quietRetry?.Status=="WAYPOINT_QUIET_REPLAN"&&quietRetry.Continue&&
                !walk.WaitingForMovement&&walk.CorridorReplans==1&&
                walk.CanDispatch(now.AddSeconds(4.2),now.AddSeconds(2)),
            "two-tile near-waypoint pause may replan once using a still-recent post-click API tile");
        Require(walk.Timeout(now.AddSeconds(7.3))=="QUIET_REPLAN_EXPIRED",
            "a pending quiet replan that cannot dispatch a fresh safe click hard-stops within three seconds");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(quietPlan,now,now);
        walk.Observe(new(3233,3179),now.AddSeconds(.6),now.AddSeconds(.8));
        walk.Observe(new(3234,3181),now.AddSeconds(1.3),now.AddSeconds(1.6));
        walk.Observe(new(3235,3182),now.AddSeconds(2),now.AddSeconds(2.3));
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3235,3182),now.AddSeconds(2),
                    now.AddSeconds(5.1)) is null&&walk.WaitingForMovement,
            "an API tile older than three seconds never authorizes an automatic retry");
        Require(walk.Timeout(now.AddSeconds(12))=="POSITION_EVENT_TIMEOUT",
            "a stale near-waypoint observation must still lead to the existing safety timeout");
        walk.Stop();walk.Start(goal,geom,now);
        for(int i=0;i<=RuneLiteAutoWalk.MaxCorridorReplans;i++)
        {
            var sentAt=now.AddSeconds(i*6);
            walk.Sent(quietPlan,sentAt,sentAt);
            walk.Observe(new(3233,3179),sentAt.AddSeconds(.6),sentAt.AddSeconds(.8));
            walk.Observe(new(3234,3181),sentAt.AddSeconds(1.3),sentAt.AddSeconds(1.6));
            walk.Observe(new(3235,3182),sentAt.AddSeconds(2),sentAt.AddSeconds(2.3));
            var result=walk.RecoverNearWaypointAfterQuietPeriod(new(3235,3182),
                sentAt.AddSeconds(2),sentAt.AddSeconds(4.2));
            Require(result?.Status==(i<RuneLiteAutoWalk.MaxCorridorReplans?
                    "WAYPOINT_QUIET_REPLAN":"RECOVERY_LIMIT_REACHED"),
                "after two bounded pauses the third near-waypoint recovery is a hard stop");
        }
        // Replays of BOTH unexpected stop points in the v1.8.32 live log.
        // The game moved as intended for multiple independently exported
        // tiles, then took a single lateral step two tiles before the click
        // endpoint. One such observation must not abort or trigger a new
        // game click, but it does not count as reaching a waypoint either.
        var varrockFrom=new WorldTile(3241,3263);
        WorldTile[] varrockPrefix=[new(3240,3264),new(3240,3265),
            new(3241,3266),new(3240,3267),new(3240,3268),
            new(3240,3269),new(3240,3270),new(3241,3271)];
        var varrockPlan=RuneLiteAutoWalk.Project(varrockFrom,
            varrockPrefix.Select(t=>new PathStep(t)).ToArray(),geom)!;
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(varrockPlan,now,now);
        Require(walk.Observe(new(3241,3265),now.AddSeconds(.8),now.AddSeconds(1)).Status==
                "CORRIDOR_PROGRESS_CONFIRMED"&&
                walk.Observe(new(3240,3269),now.AddSeconds(1.8),now.AddSeconds(2)).Status==
                "PROGRESS_CONFIRMED",
            "first real v1.8.32 route has independently confirmed forward plugin tiles");
        var lateral1=walk.Observe(new(3239,3269),now.AddSeconds(2.6),now.AddSeconds(2.8));
        Require(lateral1.Status=="NEAR_WAYPOINT_LATERAL_OBSERVED"&&lateral1.Continue&&
                walk.WaitingForMovement&&walk.Clicks==1&&
                !walk.CanDispatch(now.AddSeconds(2.8),now.AddSeconds(2.6)),
            "a single late sideways tile (3239,3269) is held without a new click or false arrival");
        Require(walk.Observe(new(3241,3271),now.AddSeconds(3.4),now.AddSeconds(3.5)).Status==
                "WAYPOINT_CONFIRMED"&&!walk.WaitingForMovement,
            "a later exact waypoint from the first real lateral tile can confirm the original click");
        var grandFrom=new WorldTile(3239,3289);
        WorldTile[] grandPrefix=[new(3238,3290),new(3239,3291),
            new(3240,3292),new(3239,3293),new(3238,3294),
            new(3239,3295),new(3239,3296),new(3239,3297)];
        var grandPlan=RuneLiteAutoWalk.Project(grandFrom,
            grandPrefix.Select(t=>new PathStep(t)).ToArray(),geom)!;
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(grandPlan,now,now);
        Require(walk.Observe(new(3239,3293),now.AddSeconds(.8),now.AddSeconds(1)).Status==
                "PROGRESS_CONFIRMED"&&
                walk.Observe(new(3238,3295),now.AddSeconds(1.6),now.AddSeconds(1.8)).Status==
                "CORRIDOR_PROGRESS_CONFIRMED",
            "second real v1.8.32 route has verified late progress near its endpoint");
        var lateral2=walk.Observe(new(3237,3296),now.AddSeconds(2.4),now.AddSeconds(2.6));
        Require(lateral2.Status=="NEAR_WAYPOINT_LATERAL_OBSERVED"&&
                !walk.CanDispatch(now.AddSeconds(2.6),now.AddSeconds(2.4)),
            "the real (3237,3296) sideways tile is not immediately rejected or treated as success");
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3237,3296),
                    now.AddSeconds(2.4),now.AddSeconds(3.2)) is null&&
                walk.WaitingForMovement,
            "a lateral observation cannot trigger an immediate new click");
        var lateralRetry=walk.RecoverNearWaypointAfterQuietPeriod(new(3237,3296),
            now.AddSeconds(2.4),now.AddSeconds(4.3));
        Require(lateralRetry?.Status=="WAYPOINT_LATERAL_QUIET_REPLAN"&&
                lateralRetry.Continue&&!walk.WaitingForMovement&&
                walk.CorridorReplans==1&&walk.Clicks==1,
            "late sideways observation only allows the existing timed, limited fresh-API collision replan");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(grandPlan,now,now);
        walk.Observe(new(3239,3293),now.AddSeconds(.8),now.AddSeconds(1));
        walk.Observe(new(3238,3295),now.AddSeconds(1.6),now.AddSeconds(1.8));
        walk.Observe(new(3237,3296),now.AddSeconds(2.4),now.AddSeconds(2.6));
        Require(walk.Observe(new(3237,3297),now.AddSeconds(3),now.AddSeconds(3.2)).Status==
                "OFF_PATH",
            "a second lateral deviation from the same click still stops safely");
        walk.Stop();walk.Start(goal,geom,now);walk.Sent(grandPlan,now,now);
        walk.Observe(new(3239,3293),now.AddSeconds(.8),now.AddSeconds(1));
        walk.Observe(new(3238,3295),now.AddSeconds(1.6),now.AddSeconds(1.8));
        Require(walk.Observe(new(3236,3296),now.AddSeconds(2.4),now.AddSeconds(2.6)).Status==
                "OFF_PATH",
            "a two-tile sideways jump may never use the one-tile lateral grace");
        // Replay the actual v1.8.33 final Lumbridge click (29 October 2026):
        // the first API sample is 1 tile off the prefix, then the exporter
        // confirms the destination inside the configured four-tile radius.
        var lumbridgeLiveFrom=new WorldTile(3232,3219);
        PathStep[] lumbridgeLivePrefix=[
            new(new(3231,3218)),new(new(3230,3219)),
            new(new(3229,3218)),new(new(3228,3218)),
            new(new(3227,3219)),new(new(3226,3220))];
        var lumbridgeLivePlan=RuneLiteAutoWalk.Project(lumbridgeLiveFrom,
            lumbridgeLivePrefix,geom)!;
        Require(lumbridgeLivePlan.Waypoint==new WorldTile(3226,3220)&&
                lumbridgeLivePlan.WaypointIndex==6,
            "real v1.8.33 final click uses a verified six-step collision prefix");
        walk.Stop();walk.Start(lumbridge,geom,now);walk.Sent(lumbridgeLivePlan,now,now);
        var liveCorridor=walk.Observe(new(3228,3219),
            now.AddMilliseconds(900),now.AddSeconds(1));
        Require(liveCorridor.Status=="CORRIDOR_PROGRESS_CONFIRMED"&&
                walk.WaitingForMovement&&
                !walk.CanDispatch(now.AddSeconds(1),now.AddMilliseconds(900)),
            "live off-prefix intermediate tile may not trigger an extra click");
        var liveArrival=walk.Observe(new(3226,3220),
            now.AddMilliseconds(1800),now.AddSeconds(1.9));
        Require(liveArrival.Status=="WAYPOINT_CONFIRMED"&&
                !walk.WaitingForMovement&&
                new WorldTile(3226,3220).Distance(lumbridge.Tile)<=lumbridge.Radius&&
                new WorldTile(3226,3222).Distance(lumbridge.Tile)<=lumbridge.Radius,
            "real v1.8.33 route reaches and remains in the configured Lumbridge radius");
        // Real v1.8.34 test, 29 Oct 18:02:40-18:02:43: command 3
        // reached the clicked waypoint, then command 4 took THREE
        // independent exporter-confirmed tiles outside its A* prefix.
        // The old controller waited for (3259,3237) forever despite
        // being at (3255,3239) and advancing toward Lumbridge.
        PathStep[] preLiveDetour=[
            new(new(3252,3249)),new(new(3253,3249)),
            new(new(3254,3248)),new(new(3255,3247)),
            new(new(3256,3246)),new(new(3257,3245))];
        PathStep[] clickedLiveDetour=[
            new(new(3258,3244)),new(new(3258,3243)),
            new(new(3259,3242)),new(new(3259,3241)),
            new(new(3259,3240)),new(new(3259,3239)),
            new(new(3259,3238)),new(new(3259,3237))];
        var preLiveDetourPlan=RuneLiteAutoWalk.Project(
            new(3251,3250),preLiveDetour,geom)!;
        var clickedLiveDetourPlan=RuneLiteAutoWalk.Project(
            new(3257,3245),clickedLiveDetour,geom)!;
        Require(preLiveDetourPlan.Waypoint==new WorldTile(3257,3245)&&
                clickedLiveDetourPlan.Waypoint==new WorldTile(3259,3237),
            "real v1.8.34 clicks projected from checked consecutive collision tiles");
        walk.Stop();walk.Start(lumbridge,geom,now);
        walk.Sent(preLiveDetourPlan,now,now);
        Require(walk.Observe(new(3255,3247),now.AddSeconds(.8),now.AddSeconds(.9)).Status==
                "PROGRESS_CONFIRMED"&&
                walk.Observe(new(3257,3245),now.AddSeconds(1.6),now.AddSeconds(1.7)).Status==
                "WAYPOINT_CONFIRMED",
            "a genuinely confirmed preceding waypoint is required for transition detour grace");
        walk.Sent(clickedLiveDetourPlan,now.AddSeconds(1.6),now.AddSeconds(1.8));
        WorldTile[] liveDetourSamples=[
            new(3255,3244),new(3254,3240),new(3255,3239)];
        for(int i=0;i<liveDetourSamples.Length;i++)
        {
            var observed=walk.Observe(liveDetourSamples[i],
                now.AddSeconds(2.6+i*.8),now.AddSeconds(2.7+i*.8));
            Require(observed.Status=="TRANSITION_DETOUR_OBSERVED"&&
                    walk.WaitingForMovement&&!walk.ShortNextClick,
                "all three real off-prefix samples must be observed without premature clicks");
        }
        var finalDetourEvent=now.AddSeconds(4.2);
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3255,3239),
                    finalDetourEvent,now.AddSeconds(4.9)) is null&&
                !walk.CanDispatch(now.AddSeconds(4.9),finalDetourEvent),
            "no new click while the v1.8.34 detour may still be walking");
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3255,3239),
                    now.AddSeconds(3.4),now.AddSeconds(6)) is null,
            "a stale or mismatched event timestamp cannot authorize recovery");
        var liveQuietReplan=walk.RecoverNearWaypointAfterQuietPeriod(
            new(3255,3239),finalDetourEvent,now.AddSeconds(6));
        Require(liveQuietReplan?.Status=="TRANSITION_DETOUR_QUIET_REPLAN"&&
                liveQuietReplan.Continue&&!walk.WaitingForMovement&&
                walk.CorridorReplans==1&&walk.ShortNextClick&&
                walk.CanDispatch(now.AddSeconds(6),finalDetourEvent),
            "sustained destination-forward live detour reopens one bounded, still-fresh API replan");
        PathStep[] safeRecoveryPrefix=Enumerable.Range(1,8)
            .Select(i=>new PathStep(new WorldTile(3255-i,3239))).ToArray();
        var boundedNext=RuneLiteAutoWalk.Project(new(3255,3239),
            safeRecoveryPrefix,geom,RuneLiteAutoWalk.RecoveryPrefixSteps)!;
        var unboundedNext=RuneLiteAutoWalk.Project(new(3255,3239),
            safeRecoveryPrefix,geom)!;
        Require(boundedNext.WaypointIndex==RuneLiteAutoWalk.RecoveryPrefixSteps&&
                unboundedNext.WaypointIndex==8,
            "recovery shortens the next minimap hop to at most six adjacent collision steps");
        walk.Sent(boundedNext,finalDetourEvent,now.AddSeconds(6));
        Require(!walk.ShortNextClick&&walk.WaitingForMovement&&
                !walk.CanDispatch(now.AddSeconds(6.1),finalDetourEvent),
            "replanned click atomically consumes the shortened-hop permission and awaits new API movement");
        walk.Stop();walk.Start(lumbridge,geom,now);
        walk.Sent(preLiveDetourPlan,now,now);
        walk.Observe(new(3257,3245),now.AddSeconds(1.6),now.AddSeconds(1.7));
        walk.Sent(clickedLiveDetourPlan,now.AddSeconds(1.6),now.AddSeconds(1.8));
        for(int i=0;i<liveDetourSamples.Length;i++)
            walk.Observe(liveDetourSamples[i],now.AddSeconds(2.6+i*.8),
                now.AddSeconds(2.7+i*.8));
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3255,3239),
                    finalDetourEvent,now.AddSeconds(7.4)) is null&&
                walk.WaitingForMovement&&!walk.ShortNextClick,
            "last exporter event older than 3s never authorizes an automatic detour restart");
        walk.Stop();
        var oppositeDestination=new NavigationDestination("Opposite","safe",new(3300,3300),1);
        walk.Start(oppositeDestination,geom,now);
        walk.Sent(preLiveDetourPlan,now,now);
        walk.Observe(new(3257,3245),now.AddSeconds(1.6),now.AddSeconds(1.7));
        walk.Sent(clickedLiveDetourPlan,now.AddSeconds(1.6),now.AddSeconds(1.8));
        for(int i=0;i<liveDetourSamples.Length;i++)
            walk.Observe(liveDetourSamples[i],now.AddSeconds(2.6+i*.8),
                now.AddSeconds(2.7+i*.8));
        Require(walk.RecoverNearWaypointAfterQuietPeriod(new(3255,3239),
                    finalDetourEvent,now.AddSeconds(6)) is null&&walk.WaitingForMovement,
            "same detour moving away from the chosen destination must never be restarted");
        walk.Stop();Require(!walk.Active&&walk.Timeout(now.AddSeconds(40)) is null,
            "manual stop prevents further movement");
    }
}
