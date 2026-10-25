using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal static class ActivitySchedulerTests
{
    internal static void Run()
    {
        static void Require(bool value,string reason)
        {if(!value)throw new Exception("Activity scheduler: "+reason);}
        var now=DateTimeOffset.UtcNow;
        var skills=PublicOsrsHiscores.OrderedSkills.Select(name=>
            new PublicOsrsSkill(name,name=="Woodcutting"?15:name=="Hitpoints"?10:1,0,0)).ToArray();
        var stock=new InventoryState(new[]{new InventoryItem(0,"bronze axe",1,1,ExecutionUi.InventorySlot(0))},27,now);
        var scheduler=new ActivityScheduler();
        var candidates=ActivityScheduler.Candidates(skills,stock,true);
        var work=scheduler.Select(candidates,now);
        Require(work?.Id=="CUT_OAKS"&&work.Supplied,
            "ready level-15 woodcutting wins over unsupplied level-1 combat");
        Require(!candidates.Any(c=>c.Id=="CUT_TREES"),"respect the active method level range");
        var duringSupplyBackoff=ActivityScheduler.Candidates(skills,stock,true,allowAcquisition:false);
        Require(duringSupplyBackoff.Length>0&&duringSupplyBackoff.All(c=>c.Supplied)&&
            duringSupplyBackoff.Any(c=>c.Id=="CUT_OAKS"),
            "deferred purchase permits supplied fallback without a second acquisition chain");
        Require(!ActivityScheduler.Candidates(skills,stock,false).Any(c=>c.Combat is not null),
            "combat-disabled setting excludes combat completely");
        var maxed=skills.Select(s=>s with{Level=99}).ToArray();
        Require(ActivityScheduler.Candidates(maxed,stock,true).Length==0,"never schedule maxed skills");

        var cutting=work!;
        var fishing=new AgentActivity("NET_SHRIMPS","Fishing",1,true);
        var options=new[]{cutting,fishing};
        scheduler.Record(cutting.Id,new(RoutineStatus.Progress,"AUTOMATIC_WALK_STEP_VERIFIED"),now);
        Require(scheduler.Select(options,now)?.Id==cutting.Id,"keep the goal across verified travel progress");
        scheduler.Record(cutting.Id,new(RoutineStatus.Completed,"RESOURCE_GAIN_VERIFIED",1),now);
        Require(scheduler.Select(options,now)?.Id==fishing.Id,"reconsider priority after completed work");

        var blocked=new RoutineResult(RoutineStatus.Blocked,"VERIFIED_BANK_SEARCH_EMPTY");
        var retry=scheduler.Record(fishing.Id,blocked,now);
        Require(retry==now.AddSeconds(30),"first prerequisite block waits 30 seconds");
        Require(scheduler.Select(options,now)?.Id==cutting.Id,"defer one task and continue supplied work");
        Require(!scheduler.Available(fishing.Id,now.AddSeconds(29)),"no early retry");
        Require(scheduler.Available(fishing.Id,now.AddSeconds(30)),"retry at the precise boundary");
        scheduler.Record(cutting.Id,new(RoutineStatus.Completed,"RESOURCE_GAIN_VERIFIED"),now.AddSeconds(30));
        Require(scheduler.Select(options,now.AddSeconds(30))?.Id==fishing.Id,"deferred task is reconsidered");
        int[] delays={60,120,240,300,300};
        var time=now.AddSeconds(30);
        foreach(int seconds in delays)
        {
            retry=scheduler.Record(fishing.Id,blocked,time);
            Require(retry==time.AddSeconds(seconds),"exponential retry with five-minute cap");
            time=retry!.Value;
        }
        scheduler.Record(fishing.Id,new(RoutineStatus.Progress,"AUTOMATIC_WALK_STEP_VERIFIED"),time);
        Require(scheduler.Record(fishing.Id,blocked,time)==time.AddSeconds(300),
            "a walk alone does not reset prerequisite failures");
        scheduler.Record(fishing.Id,new(RoutineStatus.Completed,"RESOURCE_GAIN_VERIFIED"),time);
        Require(scheduler.Record(fishing.Id,blocked,time)==time.AddSeconds(30),"verified completion resets backoff");
        scheduler.Record(cutting.Id,blocked,time);
        Require(scheduler.Select(options,time) is null,"all tasks deferred means no new task");

        // Readability, safety and transaction ambiguity never justify trying
        // another activity. The coordinator still handles journals first.
        foreach(string reason in new[]{"COMBAT_DANGEROUS_SCREEN_STOPPED","TRAVEL_HEALTH_UNREADABLE_OR_BELOW_THRESHOLD",
            "CONTEXT_MENU_NOT_READABLE","FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED",
            "GE_INVOICE_CHANGED_BEFORE_CONFIRM","GAME_SESSION_LEFT_EXECUTION_SCREEN","UNRECOGNIZED_REASON"})
        {
            scheduler.Reset();scheduler.Select(new[]{cutting},now);
            Require(scheduler.Record(cutting.Id,new(RoutineStatus.Blocked,reason),now) is null,
                "safety/unknown blocker is not deferred: "+reason);
            Require(scheduler.Select(options,now)?.Id==cutting.Id,"keep blocked goal instead of switching: "+reason);
        }
        scheduler.Record(cutting.Id,new(RoutineStatus.UnknownOutcome,"UNCONFIRMED_ACTION"),now);
        Require(scheduler.Select(options,now.AddHours(1)) is null&&!scheduler.Available(cutting.Id,now.AddHours(1)),
            "unknown action result cannot expire into automatic fallback");
        scheduler.ReconcileJournal(new(RoutineStatus.Progress,"OFFER_PENDING"));
        Require(scheduler.Select(options,now) is null,"partial journal reconciliation cannot release the block");
        scheduler.ReconcileJournal(new(RoutineStatus.Completed,"VERIFIED_TRADE_RECEIPT"));
        Require(scheduler.Select(options,now)?.Id==fishing.Id,
            "verified journal completion releases scheduling after an uncertain trade");
        scheduler.Reset();
        Require(scheduler.Select(options,now)?.Id==fishing.Id,"session reset removes previous character scheduling state");

        var legacy=JsonSerializer.Deserialize<SupplyIntent>(
            "{\"Subject\":\"hash\",\"Order\":{\"Id\":\"order\",\"Kind\":\"buy\",\"Target\":\"trout\",\"Quantity\":5}}");
        Require(legacy is not null&&legacy.ActivityId is null&&legacy.Order.Quantity==5,
            "existing acquisition intents remain readable without an activity id");
        var intent=new SupplyIntent("hash",new("order","buy","trout",5),"ATTACK_CHICKENS");
        Require(JsonSerializer.Deserialize<SupplyIntent>(JsonSerializer.Serialize(intent))==intent,
            "acquisition intent retains its originating activity and order identity");
        Console.WriteLine("ACTIVITY SCHEDULER PASS: supplied work, stable travel goals, bounded retry, fallback, safety blocks, unknown outcomes, legacy supply intents");
    }
}
