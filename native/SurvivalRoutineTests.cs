namespace OSRSLiveDiagnostics;

internal static class SurvivalRoutineTests
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        static void Require(bool value,string reason)
        {if(!value)throw new Exception("Survival: "+reason);}
        ExecutionPixels Pixels(int? hp,int? navigationHp=null)=>new(Array.Empty<ObservedSlot>(),1,
            navigationHp is int n?new NavigationPixels(Array.Empty<byte>(),new(800,20,150,150),n):null,Hitpoints:hp);
        var empty=new InventoryState(Array.Empty<InventoryItem>(),28,DateTimeOffset.UtcNow);
        foreach(var pixels in new[]{Pixels(24),Pixels(null,24)})
        {
            var driver=new HealthDriver(pixels,24);
            Require(await SurvivalRoutine.Heal(new(driver),24,90,CancellationToken.None,empty) is null&&driver.Inputs==0,
                "verified full health permits continuation with either standalone or navigation HP");
        }
        foreach(var hp in new int?[]{null,0,25})
        {
            var driver=new HealthDriver(Pixels(hp),24);
            Require((await SurvivalRoutine.Heal(new(driver),24,90,CancellationToken.None,empty))?.Reason==
                "HEALING_REQUIRES_READABLE_LIVING_HEALTH"&&driver.Inputs==0,
                "unknown, dead or impossible health blocks without input");
        }
        Require(CombatPolicy.Hitpoints(Pixels(0,24))==0,"standalone zero cannot fall back to an older living HP value");
        var food=new InventoryState(new[]{new InventoryItem(0,"trout",1,1,ExecutionUi.InventorySlot(0))},27,DateTimeOffset.UtcNow);
        foreach(var afterHp in new int?[]{24,12,null})
        {
            var driver=new HealthDriver(Pixels(12),afterHp);
            var result=await SurvivalRoutine.Heal(new(driver),24,90,CancellationToken.None,food);
            Require(driver.Eats==1,"one verified food action, without a minimap or duplicate eating");
            Require(afterHp==24?result is null:afterHp==12?result?.Status==RoutineStatus.UnknownOutcome:
                result?.Reason=="HEALTH_LOST_DURING_HEALING","post-food HP is independently checked without a minimap");
        }
        var noMap=new HealthDriver(Pixels(24),24);
        using var atlas=new WorldNavigator(new WorldMapData());
        var travel=await new TravelRoutine(new(noMap),atlas,24).Run("bank",CancellationToken.None);
        Require(travel.Reason=="AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT"&&noMap.Inputs==0,
            "readable standalone HP does not invent a minimap, route or walking click");
        Console.WriteLine("SURVIVAL PASS: standalone/native HP parity, living-health guards, independent healing receipt, no fabricated navigation");
    }
    private sealed class HealthDriver:IVisualGameDriver
    {
        public VisualFrame Current{get;private set;}
        private readonly int? afterHp;
        private bool ate;
        internal int Inputs,Eats;
        internal HealthDriver(ExecutionPixels pixels,int? afterHp)
        {this.afterHp=afterHp;Current=new(new IntPtr(1),new(0,0,1000,600),DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),pixels);}
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Current=Current with{CapturedUtc=Current.CapturedUtc.AddSeconds(1),
                Pixels=ate?Current.Pixels! with{Hitpoints=afterHp}:Current.Pixels};
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Inputs++;
            if(input.Kind==GameInputKind.RightClick)
                Current=Current with{Labels=new[]{new VisualLabel("Choose Option",new(750,300,160,14)),
                    new VisualLabel("Eat trout",new(750,320,160,14)),new VisualLabel("Cancel",new(750,340,160,14))}};
            if(input.Kind==GameInputKind.LeftClick)
            {ate=true;Eats++;Current=Current with{Labels=Array.Empty<VisualLabel>()};}
            return Task.CompletedTask;
        }
    }
}
