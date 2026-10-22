using System;
using System.Collections.Generic;
using System.Linq;

namespace OSRSLiveDiagnostics;

// Closed offline toy world; neither OSRS packets nor operating-system input.
internal sealed record MiningCase(string Name,bool SlowRocks=false,bool BlockRoad=false,
    bool RejectOffer=false,bool CutOffGe=false,int Trips=1);
internal sealed record MiningTrace(int Tick,string Code,string Detail);
internal sealed record MiningOutcome(string Name,bool Completed,bool SafeAbort,int Ticks,
    int Trips,int Mined,int Sold,int Inventory,int Coins,int Steps,int DetourSteps,
    int RockWaits,int RejectedOffers,IReadOnlyList<MiningTrace> Trace)
{
    internal string Summary =>
        $"scenario={Name} complete={Completed} safe_abort={SafeAbort} ticks={Ticks} " +
        $"trips={Trips} mined={Mined} sold={Sold} inventory={Inventory}/28 coins={Coins} " +
        $"steps={Steps} detour={DetourSteps} rock_wait={RockWaits} rejected={RejectedOffers}";
}
internal static class MiningScenario
{
    internal static MiningOutcome Run(MiningCase setting)
    {
        const int price=45;
        var log=new List<MiningTrace>();
        var rocks=new int[]{2,2,2};
        var respawn=new int[3];
        var position=(X:0,Y:0);
        var mode="MINING";
        int tick=0,inventory=0,mined=0,sold=0,coins=0,trips=0;
        int steps=0,detours=0,rockWaits=0,rejected=0,offers=0,pending=-1,stuck=0;
        bool abort=false;
        void Emit(string code,string detail)=>log.Add(new(tick,code,detail));
        bool BannedTile((int X,int Y) tile)=>
            tile.X==6 && (setting.CutOffGe || setting.BlockRoad && tile.Y==0);
        (int X,int Y)? NextStep((int X,int Y) goal)
        {
            var q=new Queue<(int X,int Y)>();
            var seen=new HashSet<(int,int)>{position};
            var prev=new Dictionary<(int,int),(int,int)>();
            var directions=new (int X,int Y)[]{(1,0),(-1,0),(0,1),(0,-1)};
            q.Enqueue(position);
            while(q.Count>0)
            {
                var p=q.Dequeue();
                if(p==goal)
                {
                    while(prev.TryGetValue(p,out var before))
                    {
                        if(before==position)return p;
                        p=before;
                    }
                    return null;
                }
                foreach(var d in directions)
                {
                    var next=(p.X+d.X,p.Y+d.Y);
                    if(next.Item1< -1||next.Item1>13||next.Item2< -2||next.Item2>2||
                       BannedTile(next)||!seen.Add(next))continue;
                    prev[next]=p;q.Enqueue(next);
                }
            }
            return null;
        }
        void Move((int X,int Y) target,string arrived)
        {
            var next=NextStep(target);
            if(next is null)
            {
                stuck++;
                Emit("ROUTE_BLOCKED","stuck_ticks="+stuck);
                if(stuck>=8){abort=true;Emit("ROUTE_ABORT","inventory_preserved="+inventory);}
                return;
            }
            stuck=0;
            if(Math.Abs(next.Value.X-position.X)+Math.Abs(next.Value.Y-position.Y)!=1)
                throw new InvalidOperationException("Illegal movement.");
            position=next.Value;steps++;
            if(position.Y!=0)detours++;
            Emit("MOVE",$"x={position.X} y={position.Y}");
            if(position==target){mode=arrived;Emit("ARRIVED","state="+mode);}
        }
        Emit("SCENARIO_BEGIN",$"name={setting.Name} offline=true synthetic_price={price}");
        while(tick<600 && mode!="DONE" && !abort)
        {
            tick++;
            if(mode=="MINING")
            {
                if(position!=(0,0))throw new InvalidOperationException("Not at mine.");
                if(inventory==28)mode="TO_GE";
                else
                {
                    for(int i=0;i<3;i++)
                        if(rocks[i]==0 && tick>=respawn[i])rocks[i]=2;
                    var rock=Array.FindIndex(rocks,x=>x>0);
                    if(rock<0){rockWaits++;Emit("ROCK_WAIT","all_depleted=true");}
                    else
                    {
                        inventory++;mined++;rocks[rock]--;
                        Emit("ORE_MINED",$"rock={rock} inventory={inventory}");
                        if(rocks[rock]==0)
                        {
                            respawn[rock]=tick+(setting.SlowRocks?35:8);
                            Emit("ROCK_DEPLETED",$"rock={rock} respawn={respawn[rock]}");
                        }
                    }
                    if(inventory==28){mode="TO_GE";Emit("INVENTORY_FULL","28/28");}
                }
            }
            else if(mode=="TO_GE")Move((12,0),"SELL");
            else if(mode=="RETURN")Move((0,0),"MINING");
            else if(mode=="SELL")
            {
                if(position!=(12,0))throw new InvalidOperationException("Not at GE.");
                if(pending>=0)
                {
                    if(tick-pending<2)Emit("SALE_PENDING","qty="+inventory);
                    else
                    {
                        var qty=inventory;
                        inventory=0;sold+=qty;coins+=qty*price;trips++;pending=-1;
                        Emit("SALE_CONFIRMED",$"qty={qty} coins={coins} inventory=0");
                        mode=trips>=setting.Trips?"DONE":"RETURN";
                    }
                }
                else
                {
                    offers++;
                    if(setting.RejectOffer && offers==1)
                    {
                        rejected++;
                        Emit("OFFER_REJECTED","inventory_preserved="+inventory);
                    }
                    else
                    {
                        pending=tick;
                        Emit("OFFER_SUBMITTED","qty="+inventory);
                    }
                }
            }
            if(inventory<0||inventory>28||mined!=sold+inventory||coins!=sold*price)
                throw new InvalidOperationException("Ore or coin ledger mismatch.");
        }
        if(mode!="DONE" && !abort){abort=true;Emit("TICK_LIMIT","max=600");}
        Emit("SCENARIO_END",$"completed={mode=="DONE"} abort={abort} coins={coins}");
        return new MiningOutcome(setting.Name,mode=="DONE",abort,tick,trips,mined,sold,
            inventory,coins,steps,detours,rockWaits,rejected,log);
    }
    internal static MiningOutcome[] Verify()
    {
        var cases=new[]{
            new MiningCase("baseline",Trips:2),
            new MiningCase("rock-depletion",SlowRocks:true),
            new MiningCase("route-detour",BlockRoad:true),
            new MiningCase("ge-rejection",RejectOffer:true),
            new MiningCase("no-route",CutOffGe:true)
        };
        var result=cases.Select(Run).ToArray();
        static void Check(bool c,string name)
        {
            if(!c)throw new InvalidOperationException("Mining scenario test failed: "+name);
        }
        Check(result[0].Completed && result[0].Trips==2 && result[0].Mined==56 &&
              result[0].Sold==56 && result[0].Coins==2520 && result[0].Inventory==0,"baseline");
        Check(result[1].Completed && result[1].RockWaits>0,"rock regeneration");
        Check(result[2].Completed && result[2].DetourSteps>0,"route detour");
        Check(result[3].Completed && result[3].RejectedOffers==1 &&
              result[3].Sold==28 && result[3].Coins==1260,"offer retry");
        Check(!result[4].Completed && result[4].SafeAbort &&
              result[4].Inventory==28 && result[4].Sold==0 && result[4].Coins==0,"no route safety");
        var replay=Run(cases[0]);
        Check(result[0].Summary==replay.Summary &&
              result[0].Trace.SequenceEqual(replay.Trace),"deterministic replay");
        foreach(var r in result)
        {
            var offers=r.Trace.Where(e=>e.Code=="OFFER_SUBMITTED").ToArray();
            var sales=r.Trace.Where(e=>e.Code=="SALE_CONFIRMED").ToArray();
            Check(sales.Length==r.Trips,"sale tally");
            for(int i=0;i<sales.Length;i++)
                Check(sales[i].Tick-offers[i].Tick>=2,"sale effect must be delayed");
        }
        return result;
    }
}