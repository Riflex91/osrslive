namespace OSRSLiveDiagnostics;

internal static class WorldFooterRecoveryTests
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        static void Require(bool condition,string message)
        {if(!condition)throw new Exception("World footer recovery: "+message);}
        var labels=new[]{new VisualLabel("Welcome to Old School RuneScape",new(20,20,400,30)),
            new VisualLabel("Play Now",new(300,300,100,30))};
        var initial=new VisualFrame(new IntPtr(5),new(0,0,1000,600),DateTimeOffset.UtcNow,labels);
        VisualFrame WithWorld(int world)=>initial with{CapturedUtc=initial.CapturedUtc.AddSeconds(1),
            Labels=labels.Append(new VisualLabel("World "+world,new(150,550,90,16))).ToArray()};
        foreach(int actualWorld in new[]{417,553})
        {
            int captures=0;
            var log=new List<string>();
            var recovered=await WorldFooterRecovery.Reobserve(initial,553,_=>
                Task.FromResult<VisualFrame?>(++captures==1?initial:WithWorld(actualWorld)),CancellationToken.None,
                (_,_)=>Task.CompletedTask,log.Add);
            Require(captures==2&&VisualStartupPolicy.Decide(recovered.Labels,553).State==
                (actualWorld==553?"PLAY_SELECTED_CHARACTER":"OPEN_WORLD_SELECTION")&&
                log.All(l=>l.Contains("applied_world_verified=false")),
                "only a fresh visible world can confirm or correct the recommendation");
        }
        foreach(string failure in new[]{"missing","stale","window","bounds","null"})
        {
            int captures=0;
            var blocked=await WorldFooterRecovery.Reobserve(initial,553,_=>
            {
                captures++;
                return Task.FromResult<VisualFrame?>(failure switch
                {
                    "stale"=>WithWorld(553) with{CapturedUtc=initial.CapturedUtc},
                    "window"=>WithWorld(553) with{Window=new IntPtr(9)},
                    "bounds"=>WithWorld(553) with{ScreenBounds=new(1,0,1000,600)},
                    "null"=>null,
                    _=>initial with{CapturedUtc=initial.CapturedUtc.AddSeconds(captures)}
                });
            },CancellationToken.None,(_,_)=>Task.CompletedTask);
            Require(captures==(failure is "window" or "bounds"?1:2)&&
                VisualStartupPolicy.Decide(blocked.Labels,553).Click is null,
                failure+" never authorizes a world or Play click");
        }
        int unnecessary=0;
        await WorldFooterRecovery.Reobserve(WithWorld(553),553,_=>
            {unnecessary++;return Task.FromResult<VisualFrame?>(null);},CancellationToken.None,(_,_)=>Task.CompletedTask);
        Require(unnecessary==0,"readable footer needs no extra capture");
        Console.WriteLine("WORLD FOOTER RECOVERY PASS: fresh passive retries, visible world instead of recommendation, stale/geometry rejection and bounded failure");
    }
}
