namespace OSRSLiveDiagnostics;

internal enum AccountGoalPhase { AwaitVerifiedSkills, TrainSkills, MembershipRequired, BuildWealthAndGear }
internal sealed record AccountGoalDecision(AccountGoalPhase Phase,string? Skill,string Reason);
internal sealed record ItemRequirement(int ItemId,int Quantity);
internal sealed record AcquisitionOption(string Method,long CoinCost,bool CanExecute,
    bool PrerequisitesVerified,bool PriceFresh);
internal sealed record AcquisitionDecision(string Method,string Reason);

// Planning is separate from execution. Hiscores may inform a plan but cannot
// establish the active character, bank contents, trade completion or skills in-session.
internal static class AccountGoalPlanner
{
    internal static readonly IReadOnlySet<string> FreeSkills=new HashSet<string>(StringComparer.Ordinal)
    {
        "Attack","Defence","Strength","Hitpoints","Ranged","Prayer","Magic",
        "Cooking","Woodcutting","Fishing","Firemaking","Crafting","Smithing","Mining","Runecraft"
    };
    internal static AccountGoalDecision Decide(IReadOnlyList<PublicOsrsSkill>? skills,
        bool membersWorld,bool verifiedActiveCharacter)
    {
        if(!verifiedActiveCharacter||skills is null ||
            skills.Count!=PublicOsrsHiscores.OrderedSkills.Length ||
            skills.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=skills.Count ||
            skills.Any(s=>s.Level is <1 or >99)||
            PublicOsrsHiscores.OrderedSkills.Any(name=>!skills.Any(s=>s.Name==name)))
            return new(AccountGoalPhase.AwaitVerifiedSkills,null,"ACTIVE_CHARACTER_SKILLS_NOT_VERIFIED");
        if(skills.All(s=>s.Level==99))
            return new(AccountGoalPhase.BuildWealthAndGear,null,"ALL_SKILLS_99");
        var accessible=skills.Where(s=>s.Level<99&&(membersWorld||FreeSkills.Contains(s.Name)))
            // Hitpoints progresses through combat; do not invent a standalone training activity.
            .OrderBy(s=>s.Name=="Hitpoints"?1:0).ThenBy(s=>s.Level)
            .ThenBy(s=>Array.IndexOf(PublicOsrsHiscores.OrderedSkills,s.Name)).FirstOrDefault();
        if(accessible is not null)
            return new(AccountGoalPhase.TrainSkills,accessible.Name,"LOWEST_ACCESSIBLE_SKILL_WITH_PROGRESS_REQUIRED");
        return new(AccountGoalPhase.MembershipRequired,null,"MEMBERS_SKILLS_REMAIN_BELOW_99");
    }

    internal static AcquisitionDecision Acquire(ItemRequirement item,
        IReadOnlyDictionary<int,int>? verifiedOwned,long? verifiedCoins,long reserveCoins,
        IReadOnlyList<AcquisitionOption> options)
    {
        if(item.ItemId<=0||item.Quantity<=0||reserveCoins<0)
            throw new ArgumentOutOfRangeException(nameof(item));
        if(verifiedOwned is null||verifiedCoins is null||verifiedCoins<0)
            return new("WAIT","INVENTORY_BANK_OR_BALANCE_NOT_VERIFIED");
        if(verifiedOwned.TryGetValue(item.ItemId,out int count)&&count>=item.Quantity)
            return new("USE_OWNED","SUFFICIENT_ITEMS_ALREADY_AVAILABLE");
        long budget=Math.Max(0,verifiedCoins.Value-reserveCoins);
        var viable=options.Where(o=>o.Method is "BUY" or "CRAFT"&&o.CanExecute&&
            o.PrerequisitesVerified&&o.PriceFresh&&o.CoinCost>=0&&o.CoinCost<=budget)
            .OrderBy(o=>o.CoinCost).ThenBy(o=>o.Method,StringComparer.Ordinal).FirstOrDefault();
        return viable is null?new("WAIT","NO_VERIFIED_AFFORDABLE_BUY_OR_CRAFT_PATH"):
            new(viable.Method,"LOWEST_VERIFIED_COST_WITH_RESERVE_PRESERVED");
    }
}

internal static class AccountGoalPlannerTests
{
    internal static void Run()
    {
        static void Require(bool value,string reason){if(!value)throw new Exception("Account goal: "+reason);}
        var skills=PublicOsrsHiscores.OrderedSkills.Select(n=>new PublicOsrsSkill(n,99,13034431,1)).ToArray();
        Require(AccountGoalPlanner.Decide(skills,false,false).Phase==AccountGoalPhase.AwaitVerifiedSkills,
            "public skill data does not verify active session");
        Require(AccountGoalPlanner.Decide(skills,false,true).Phase==AccountGoalPhase.BuildWealthAndGear,
            "wealth begins only after all skills max");
        var incomplete=skills.Select(s=>s.Name=="Agility"?s with {Level=1}:s).ToArray();
        Require(AccountGoalPlanner.Decide(incomplete,false,true).Phase==AccountGoalPhase.MembershipRequired,
            "F2P max is not account max");
        Require(AccountGoalPlanner.Decide(incomplete,true,true).Skill=="Agility","members availability");
        incomplete=skills.Select(s=>s.Name=="Mining"?s with {Level=12}:s).ToArray();
        Require(AccountGoalPlanner.Decide(incomplete,false,true).Skill=="Mining","train remaining F2P skill");
        var owned=new Dictionary<int,int>{{1,1}};
        var paths=new[]{new AcquisitionOption("BUY",100,true,true,true),
            new AcquisitionOption("CRAFT",60,true,true,true)};
        Require(AccountGoalPlanner.Acquire(new(1,1),owned,500,200,paths).Method=="USE_OWNED","do not buy duplicates");
        Require(AccountGoalPlanner.Acquire(new(2,1),owned,500,200,paths).Method=="CRAFT","choose feasible lower cost");
        Require(AccountGoalPlanner.Acquire(new(2,1),owned,250,200,paths).Method=="WAIT","preserve coin reserve");
        Require(AccountGoalPlanner.Acquire(new(2,1),null,500,0,paths).Method=="WAIT","unknown stock blocks purchase");
        Require(AccountGoalPlanner.Acquire(new(2,1),owned,500,0,
            paths.Select(p=>p with {PriceFresh=false}).ToArray()).Method=="WAIT","stale prices block acquisition");
        Console.WriteLine("ACCOUNT GOAL PASS: all-99 transition, F2P/member limits, verified stock, buy/craft, reserves, stale prices");
    }
}
