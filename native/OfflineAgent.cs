using System;

namespace OSRSLiveDiagnostics;

// Closed-world training model: never submits or synthesizes actions for official OSRS.
internal enum AgentSource { Simulation, Replay, External }
internal enum AgentAction { None, Recover, Bank, Collect }
internal enum ActionPhase { Blocked, Planned, Confirmed, Failed }

internal sealed record AgentSnapshot(
    long Tick, DateTimeOffset CapturedUtc, AgentSource Source,
    int Hitpoints, int InventoryUsed, int InventoryCapacity);

internal sealed record AgentPlan(
    long Id, long Tick, AgentAction Action, ActionPhase Phase, string Reason);

internal sealed record AgentOutcome(
    long Id, ActionPhase Phase, string Reason, AgentSnapshot After);

internal sealed class OfflineAgent
{
    private long lastTick;
    private long nextId;

    internal AgentPlan Plan(AgentSnapshot state, DateTimeOffset now)
    {
        long id = ++nextId;
        // Fail closed: unrelated or external event sources must never enable automation.
        if(state.Source == AgentSource.External)
            return new AgentPlan(id,state.Tick,AgentAction.None,ActionPhase.Blocked,"EXTERNAL_SOURCE");
        if(state.Tick <= lastTick || state.Tick < 1)
            return new AgentPlan(id,state.Tick,AgentAction.None,ActionPhase.Blocked,"NON_MONOTONIC_TICK");
        if(state.CapturedUtc > now.AddSeconds(1) || now-state.CapturedUtc > TimeSpan.FromSeconds(3))
            return new AgentPlan(id,state.Tick,AgentAction.None,ActionPhase.Blocked,"STALE_SNAPSHOT");
        if(state.Hitpoints < 0 || state.Hitpoints > 100 ||
           state.InventoryCapacity < 1 || state.InventoryCapacity > 128 ||
           state.InventoryUsed < 0 || state.InventoryUsed > state.InventoryCapacity)
            return new AgentPlan(id,state.Tick,AgentAction.None,ActionPhase.Blocked,"INVALID_STATE");

        lastTick=state.Tick;
        if(state.Hitpoints <= 25)
            return new AgentPlan(id,state.Tick,AgentAction.Recover,ActionPhase.Planned,"LOW_HEALTH_FIRST");
        if(state.InventoryUsed == state.InventoryCapacity)
            return new AgentPlan(id,state.Tick,AgentAction.Bank,ActionPhase.Planned,"INVENTORY_FULL");
        return new AgentPlan(id,state.Tick,AgentAction.Collect,ActionPhase.Planned,"SIMULATED_GATHER_GOAL");
    }

    internal static AgentSnapshot Simulate(AgentPlan plan, AgentSnapshot before)
    {
        // No I/O, network adapter, input synthesis, or game-client references.
        if(before.Source != AgentSource.Simulation ||
           plan.Phase != ActionPhase.Planned || plan.Tick != before.Tick)
            throw new InvalidOperationException("Offline actions require a valid simulation snapshot.");
        return plan.Action switch
        {
            AgentAction.Recover => before with { Hitpoints = 100 },
            AgentAction.Bank => before with { InventoryUsed = 0 },
            AgentAction.Collect => before with { InventoryUsed = before.InventoryUsed + 1 },
            _ => before
        };
    }

    internal static AgentOutcome Confirm(AgentPlan plan, AgentSnapshot before, AgentSnapshot after)
    {
        if(plan.Phase != ActionPhase.Planned || before.Source != AgentSource.Simulation ||
           after.Source != AgentSource.Simulation || plan.Tick != before.Tick ||
           after.Tick != before.Tick || after.InventoryCapacity != before.InventoryCapacity)
            return new AgentOutcome(plan.Id,ActionPhase.Failed,"INVALID_CONFIRMATION",after);

        bool observed = plan.Action switch
        {
            AgentAction.Recover => after.Hitpoints > before.Hitpoints && after.Hitpoints <= 100,
            AgentAction.Bank => before.InventoryUsed > 0 && after.InventoryUsed == 0,
            AgentAction.Collect => after.InventoryUsed == before.InventoryUsed+1 &&
                                   after.InventoryUsed <= after.InventoryCapacity,
            _ => false
        };
        return new AgentOutcome(plan.Id,observed?ActionPhase.Confirmed:ActionPhase.Failed,
            observed?"STATE_CHANGE_CONFIRMED":"EXPECTED_EFFECT_MISSING",after);
    }
}

internal static class OfflineAgentTests
{
    internal static void Run()
    {
        static void Expect(bool result, string name)
        {
            if(!result)throw new InvalidOperationException("OfflineAgent test failed: "+name);
        }
        var t=DateTimeOffset.UnixEpoch.AddDays(1);
        var planner=new OfflineAgent();
        AgentSnapshot S(long tick,int hp,int used,AgentSource src=AgentSource.Simulation) =>
            new(tick,t,src,hp,used,28);
        var gather=planner.Plan(S(1,100,0),t);
        Expect(gather.Action==AgentAction.Collect && gather.Phase==ActionPhase.Planned,"collect");
        var after=OfflineAgent.Simulate(gather,S(1,100,0));
        Expect(OfflineAgent.Confirm(gather,S(1,100,0),after).Phase==ActionPhase.Confirmed,"confirm collect");
        Expect(OfflineAgent.Confirm(gather,S(1,100,0),S(1,100,0)).Phase==ActionPhase.Failed,"must observe effect");
        var bank=planner.Plan(S(2,100,28),t);
        Expect(bank.Action==AgentAction.Bank,"bank priority");
        Expect(OfflineAgent.Confirm(bank,S(2,100,28),OfflineAgent.Simulate(bank,S(2,100,28))).Phase==ActionPhase.Confirmed,"bank confirmation");
        var heal=planner.Plan(S(3,10,28),t);
        Expect(heal.Action==AgentAction.Recover,"safety priority");
        Expect(OfflineAgent.Confirm(heal,S(3,10,28),OfflineAgent.Simulate(heal,S(3,10,28))).Phase==ActionPhase.Confirmed,"recover confirmation");
        Expect(planner.Plan(S(3,100,0),t).Phase==ActionPhase.Blocked,"duplicate tick");
        Expect(planner.Plan(S(4,100,0,AgentSource.External),t).Phase==ActionPhase.Blocked,"external blocked");
        Expect(planner.Plan(S(4,100,0) with { CapturedUtc=t.AddSeconds(-5) },t).Phase==ActionPhase.Blocked,"stale blocked");
        Expect(planner.Plan(S(4,100,0) with { InventoryUsed=29 },t).Phase==ActionPhase.Blocked,"invalid bounds");
        Expect(planner.Plan(S(4,100,0,AgentSource.Replay),t).Phase==ActionPhase.Planned,"replay plan");
        bool replayExecutionRefused=false;
        try {OfflineAgent.Simulate(new AgentPlan(1,4,AgentAction.Collect,ActionPhase.Planned,"TEST"),S(4,100,0,AgentSource.Replay));}
        catch(InvalidOperationException){replayExecutionRefused=true;}
        Expect(replayExecutionRefused,"replay not executed");
        Console.WriteLine("OFFLINE AGENT PASS: planning, priority, confirmation, stale/replay/external gates");
    }
}
