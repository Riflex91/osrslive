using System;
using System.Diagnostics;

namespace OSRSLiveDiagnostics;

// Anchors requests to an elapsed, monotonic clock. No back-to-back catch-up bursts.
internal static class MiniWorldCadence
{
    internal static int NextDelay(ref long deadlineMs, long elapsedMs, int intervalMs=600)
    {
        if(intervalMs<1 || intervalMs>10000 || elapsedMs<0)
            throw new ArgumentOutOfRangeException(nameof(intervalMs));
        deadlineMs+=intervalMs;
        if(elapsedMs>deadlineMs+intervalMs)
            deadlineMs=elapsedMs;
        return (int)Math.Clamp(deadlineMs-elapsedMs,0,intervalMs);
    }
}

// A command is NOT evidence of success. Check changes in the authoritative
// server snapshot, and reject false or incomplete success acknowledgements.
internal static class MiniWorldActionAudit
{
    internal static bool IsConfirmed(MiniWorldRequest request,MiniWorldFrame before,
        MiniWorldReply response)
    {
        var after=response.Frame;
        if(!response.Accepted || after is null || after.Tick<=before.Tick)return false;
        return request.Action switch
        {
            "move" => response.Status=="MOVED" &&
                after.X==request.X && after.Y==request.Y &&
                MiniWorldMap.Distance(before.X,before.Y,after.X,after.Y)==1,
            "mine" => response.Status=="ORE_MINED" &&
                after.Ore==before.Ore+1 && after.MiningXp>before.MiningXp &&
                after.Coins==before.Coins,
            "sell" => response.Status=="ORE_SOLD" &&
                before.Ore>0 && after.Ore==0 &&
                after.Coins>before.Coins && after.MiningXp==before.MiningXp,
            _ => false
        };
    }
}

internal static class MiniWorldActionAuditTests
{
    private static void Assert(bool pass,string description)
    {
        if(!pass)throw new Exception("MiniWorldActionAudit: "+description);
    }
    internal static void Run()
    {
        long deadline=0;
        Assert(MiniWorldCadence.NextDelay(ref deadline,10)==590,
            "first network processing time must be deducted");
        Assert(MiniWorldCadence.NextDelay(ref deadline,612)==588,
            "following deadline must use monotonic clock");
        Assert(MiniWorldCadence.NextDelay(ref deadline,3900)==0,
            "late event must not trigger catch-up burst");
        Assert(MiniWorldCadence.NextDelay(ref deadline,3901)==599,
            "resume from fresh monotonic deadline");

        var start=new MiniWorldFrame(10,4,4,0,6,0,1000,
            Array.Empty<MiniRock>(),"OBSERVED");
        var move=new MiniWorldRequest("fixture","move",5,4,true);
        var moved=new MiniWorldReply(true,"MOVED",start with {Tick=11,X=5});
        Assert(MiniWorldActionAudit.IsConfirmed(move,start,moved),"observed movement");
        Assert(!MiniWorldActionAudit.IsConfirmed(move,start,
            moved with {Frame=start with {Tick=11}}),"no phantom movement");

        var mine=new MiniWorldRequest("fixture","mine",13,3,true);
        var mined=new MiniWorldReply(true,"ORE_MINED",start with
            {Tick=11,Ore=1,MiningXp=35});
        Assert(MiniWorldActionAudit.IsConfirmed(mine,start,mined),"ore plus XP");
        Assert(!MiniWorldActionAudit.IsConfirmed(mine,start,
            mined with {Frame=start with {Tick=11,Ore=1}}),"missing XP not success");

        var sale=new MiniWorldRequest("fixture","sell",2,2,true);
        var stocked=start with{Ore=6,MiningXp=210};
        var sold=new MiniWorldReply(true,"ORE_SOLD",stocked with
            {Tick=11,Ore=0,Coins=1150});
        Assert(MiniWorldActionAudit.IsConfirmed(sale,stocked,sold),"verified sale");
        Assert(!MiniWorldActionAudit.IsConfirmed(sale,stocked,
            sold with {Frame=stocked with {Tick=11,Ore=0}}),
            "zero revenue not sale");
        Assert(!MiniWorldActionAudit.IsConfirmed(sale,stocked,
            new(false,"BOT_STOPPED",null)),"stopped bot not a confirmation");
        Console.WriteLine("MINI WORLD ACTION AUDIT PASS: stable cadence, move, XP, inventory, GP, stop");
    }
}
