using System;
using System.Collections.Generic;
using System.Linq;

namespace OSRSLiveDiagnostics;

// A strategy-only model. It never connects to OSRS, sends input, or assumes success.
internal enum GoalPreference { Balanced, MaxSkills, MaxGold }
internal sealed record GoalAccountProfile(
    IReadOnlyDictionary<string,int> Levels, long Coins, long CashReserve,
    bool Member, IReadOnlySet<string> Unlocks);
internal sealed record GoalActivity(
    string Id, string Skill, int MinLevel, int XpPerHour, long NetGpPerHour,
    long UpfrontGp, bool MembersOnly, string? Unlock, bool MarketLinked,
    DateTimeOffset? QuoteAtUtc);
internal sealed record GoalChoice(string Id, string Skill, double Score,
    int XpPerHour, long NetGpPerHour);
internal sealed record GoalRanking(IReadOnlyList<GoalChoice> Options,int Rejected,string Status);

internal static class GoalPlanner
{
    internal const int MaxTotalLevel=2376;
    internal static readonly string[] Skills={
        "Attack","Strength","Defence","Ranged","Prayer","Magic","Runecraft",
        "Construction","Hitpoints","Agility","Herblore","Thieving","Crafting",
        "Fletching","Slayer","Hunter","Mining","Smithing","Fishing","Cooking",
        "Firemaking","Woodcutting","Farming","Sailing"
    };
    private static readonly HashSet<string> ValidSkills=new(Skills,StringComparer.OrdinalIgnoreCase);

    internal static GoalRanking Rank(GoalAccountProfile account,
        IEnumerable<GoalActivity> activities,GoalPreference preference,DateTimeOffset now)
    {
        if(account.Coins<0||account.CashReserve<0||account.Levels.Count!=24||
           account.Levels.Any(x=>!ValidSkills.Contains(x.Key)||x.Value<1||x.Value>99)||
           Skills.Any(s=>!account.Levels.ContainsKey(s)))
            return new(Array.Empty<GoalChoice>(),0,"INVALID_ACCOUNT_STATE");

        var ranking=new List<GoalChoice>();int rejected=0;
        bool maxed=account.Levels.Values.All(level=>level==99);
        double skillWeight=maxed?0:preference switch
        {
            GoalPreference.MaxSkills=>.85,GoalPreference.MaxGold=>.15,_=>.50
        };
        double gpWeight=1-skillWeight;
        foreach(var a in activities)
        {
            if(string.IsNullOrWhiteSpace(a.Id)||!ValidSkills.Contains(a.Skill)||
               a.MinLevel<1||a.MinLevel>99||a.XpPerHour<0||a.UpfrontGp<0||
               a.MembersOnly&&!account.Member||
               !account.Levels.TryGetValue(a.Skill,out int level)||level<a.MinLevel||
               (a.Unlock is not null&&!account.Unlocks.Contains(a.Unlock))||
               a.UpfrontGp>Math.Max(0,account.Coins-account.CashReserve)||
               (a.MarketLinked&&(a.QuoteAtUtc is null||
                   a.QuoteAtUtc>now.AddSeconds(30)||
                   now-a.QuoteAtUtc>TimeSpan.FromMinutes(15))))
            {rejected++;continue;}
            double deficit=(99-level)/98d;
            double xp=Math.Min(2,a.XpPerHour/100000d)*deficit;
            double gp=Math.Clamp(a.NetGpPerHour/200000d,-2,2);
            double score=skillWeight*xp+gpWeight*gp;
            if(score<=0){rejected++;continue;}
            ranking.Add(new(a.Id,a.Skill,Math.Round(score,4),a.XpPerHour,a.NetGpPerHour));
        }
        ranking.Sort((a,b)=>{int cmp=b.Score.CompareTo(a.Score);
            return cmp!=0?cmp:string.CompareOrdinal(a.Id,b.Id);});
        return new(ranking,rejected,ranking.Count==0?"NO_ELIGIBLE_ACTIVITY":"ESTIMATE_ONLY");
    }
}

internal static class GoalPlannerTests
{
    private static void Assert(bool ok,string what)
    {if(!ok)throw new Exception("Goal planner: "+what);}
    internal static void Run()
    {
        var now=DateTimeOffset.UnixEpoch.AddDays(100);
        var levels=GoalPlanner.Skills.ToDictionary(x=>x,x=>1,StringComparer.OrdinalIgnoreCase);
        levels["Mining"]=40;levels["Fishing"]=55;
        var profile=new GoalAccountProfile(levels,100000,20000,true,new HashSet<string>());
        var activities=new[]
        {
            new GoalActivity("ore","Mining",15,35000,90000,5000,false,null,true,now),
            new GoalActivity("fish","Fishing",45,60000,45000,1000,false,null,true,now),
            new GoalActivity("locked","Mining",15,100000,100000,0,false,"QUEST_X",false,null),
            new GoalActivity("expensive","Mining",15,100000,100000,90000,false,null,true,now),
            new GoalActivity("stale","Mining",15,100000,100000,0,false,null,true,now.AddHours(-2)),
            new GoalActivity("level_gate","Smithing",15,100000,100000,0,false,null,false,null)
        };
        var gold=GoalPlanner.Rank(profile,activities,GoalPreference.MaxGold,now);
        var xp=GoalPlanner.Rank(profile,activities,GoalPreference.MaxSkills,now);
        Assert(gold.Options.Count==2&&gold.Rejected==4&&gold.Options[0].Id=="ore","gold ranking/gates");
        Assert(xp.Options[0].Id=="fish","skill ranking");
        var all99=GoalPlanner.Skills.ToDictionary(x=>x,x=>99,StringComparer.OrdinalIgnoreCase);
        Assert(GoalPlanner.Rank(profile with {Levels=all99},activities,GoalPreference.MaxSkills,now)
            .Options[0].Id=="ore","post-max GP optimization");
        Assert(GoalPlanner.Rank(profile with {Levels=new Dictionary<string,int>()},
            activities,GoalPreference.Balanced,now).Status=="INVALID_ACCOUNT_STATE","fail closed");
        Assert(GoalPlanner.Skills.Length==24&&GoalPlanner.MaxTotalLevel==2376,"Sailing+2376");
        Assert(GoalPlanner.Rank(profile with {Member=false},new[]{
            new GoalActivity("member","Mining",1,1000,3000,0,true,null,false,null)
        },GoalPreference.MaxGold,now).Options.Count==0,"membership");
        Console.WriteLine("GOAL PLANNER PASS: 24 skills, maxing vs gold, 99->gold, budget, quest, stale price, member");
    }
}
