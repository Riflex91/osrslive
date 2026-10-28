namespace OSRSLiveDiagnostics;

internal static class RuneLiteRouteProgressTests
{
    internal static void Run()
    {
        static void Require(bool okay,string why)
        {if(!okay)throw new Exception("RuneLite live route progress: "+why);}
        var goal=new NavigationDestination("Test Bank","bank",new(3205,3200),0);
        var other=new NavigationDestination("Other Bank","bank",new(3208,3200),0);
        var t0=new DateTimeOffset(2026,10,28,18,22,0,TimeSpan.Zero);
        var progress=new RuneLiteRouteProgress();
        var initial=progress.Observe(new(3200,3200),goal,
            [new(new(3201,3200)),new(new(3202,3200)),new(new(3203,3200)),
                new(new(3204,3200)),new(new(3205,3200))],t0);
        Require(initial is {Status:"BASELINE",StepDelta:null,OnPriorRoute:null}&&
                initial.Diagnostic.Contains("traversed_tiles_not_inferred=true"),
            "first route must establish a baseline, not fabricate progress");
        var onRoute=progress.Observe(new(3201,3200),goal,
            [new(new(3202,3200)),new(new(3203,3200)),
                new(new(3204,3200)),new(new(3205,3200))],t0.AddSeconds(1));
        Require(onRoute is {Status:"ON_PREVIOUS_ROUTE",StepDelta:-1,OnPriorRoute:true}&&
                onRoute.Display.Contains("1 Schritte näher"),
            "stepping onto the previous path reduces the A* remaining length");
        var detour=progress.Observe(new(3201,3201),goal,
            [new(new(3202,3201)),new(new(3203,3201)),new(new(3204,3201)),
                new(new(3204,3200)),new(new(3205,3200))],t0.AddSeconds(2));
        Require(detour is {Status:"OFF_PREVIOUS_ROUTE",StepDelta:1,OnPriorRoute:false}&&
                detour.Display.Contains("abgewichen")&&
                detour.Diagnostic.Contains("step_delta=1"),
            "a genuinely off-path reported tile is labelled and its reroute measured");
        var arrived=progress.Observe(goal.Tile,goal,[],t0.AddSeconds(3));
        Require(arrived is {Status:"ARRIVED",StepDelta:-5}&&
                arrived.Display.Contains("Zielradius erreicht"),
            "arrival requires an actual API coordinate in the goal radius");
        var exit=progress.Observe(new(3206,3200),goal,
            [new(new(3205,3200))],t0.AddSeconds(4));
        Require(exit is {Status:"LEFT_GOAL",StepDelta:1},
            "leaving a reached goal is recorded instead of keeping arrived state");
        var newGoal=progress.Observe(new(3206,3200),other,
            [new(new(3207,3200)),new(new(3208,3200))],t0.AddSeconds(5));
        Require(newGoal is {Status:"BASELINE",StepDelta:null},
            "choosing a different navigation target starts an independent progress series");
        progress.Reset();
        Require(progress.Observe(new(3206,3200),other,
            [new(new(3207,3200))],t0.AddSeconds(6)).Status=="BASELINE",
            "pause or source loss invalidates route-history comparisons");
    }
}
