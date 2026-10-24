namespace OSRSLiveDiagnostics;

internal sealed record Supply(string Name,int Quantity=1);
internal sealed record F2PMethod(string Id,string Skill,int MinimumLevel,int NextLevel,
    string Location,IReadOnlyList<Supply> Supplies,string Source,string? Unlock=null,bool Combat=false);
internal sealed record ProgressionFacts(IReadOnlyDictionary<string,int>? Stock=null,
    IReadOnlySet<string>? CompletedQuests=null,bool? Ironman=null,double? PlayedHours=null,
    int? QuestPoints=null,int? TotalLevel=null,bool? EverMember=null,int? FreeGeSlots=null,
    long? Coins=null,long Reserve=0,DateTimeOffset? ObservedUtc=null);
internal sealed record F2PActivityPlan(string State,F2PMethod? Method,IReadOnlyList<string> Missing,
    string Reason,ToolUpgrade? RecommendedTool=null);
internal sealed record ToolUpgrade(string Item,int UseLevel,int EquipAttack,int? SmithingLevel);
internal sealed record Recipe(string Product,int Quantity,string Skill,int Level,
    IReadOnlyList<Supply> Inputs,IReadOnlyList<Supply> Tools,string Station);
internal sealed record QuestOpportunity(string Name,string RewardSkill,int RewardXp,
    int SkipUntilLevel,int QuestPointsRequired,IReadOnlyDictionary<string,int> SkillsRequired,
    IReadOnlyList<Supply> Supplies,bool Dangerous=false);
internal sealed record AcquisitionQuote(string Product,int MissingQuantity,long TotalCost,DateTimeOffset ObservedUtc,bool RunnerAvailable);

// Reviewed Wiki facts, not live prices or an assertion that any runner exists.
// Keep requirements explicit so an activity cannot silently assume bank items,
// completed quests, account age or a player-to-player trading capability.
internal static class F2PProgression
{
    internal const string Wiki="https://oldschool.runescape.wiki/w/";
    internal static readonly DateTimeOffset ReviewedUtc=new(2026,10,24,0,0,0,TimeSpan.Zero);
    private static F2PMethod M(string id,string skill,int level,int next,string location,
        Supply[] supplies,string? unlock=null,bool combat=false)=>
        new(id,skill,level,next,location,supplies,Wiki+"Free-to-play_"+
            (skill is "Attack" or "Strength" or "Defence" or "Hitpoints"?"melee":skill)+"_training",unlock,combat);
    internal static readonly F2PMethod[] Methods=
    {
        M("CUT_TREES","Woodcutting",1,15,"Lumbridge",new[]{new Supply("axe")}),
        M("CUT_OAKS","Woodcutting",15,30,"Varrock west",new[]{new Supply("axe")}),
        M("CUT_WILLOWS","Woodcutting",30,99,"Lumbridge",new[]{new Supply("axe")}),
        M("MINE_COPPER_TIN","Mining",1,15,"Lumbridge swamp east",new[]{new Supply("pickaxe")}),
        M("MINE_IRON","Mining",15,99,"Varrock south-east",new[]{new Supply("pickaxe")}),
        M("NET_SHRIMPS","Fishing",1,20,"Lumbridge swamp",new[]{new Supply("small fishing net")}),
        M("FLY_FISH","Fishing",20,99,"River Lum",new[]{new Supply("fly fishing rod"),new Supply("feather",50)}),
        M("COOK_SHRIMPS","Cooking",1,15,"Lumbridge range",new[]{new Supply("raw shrimps",10)}),
        M("COOK_TROUT","Cooking",15,25,"Lumbridge range",new[]{new Supply("raw trout",10)}),
        M("COOK_SALMON","Cooking",25,99,"Lumbridge range",new[]{new Supply("raw salmon",10)}),
        M("BURN_LOGS","Firemaking",1,15,"Lumbridge",new[]{new Supply("tinderbox"),new Supply("logs",10)}),
        M("BURN_OAK","Firemaking",15,30,"Varrock",new[]{new Supply("tinderbox"),new Supply("oak logs",10)}),
        M("BURN_WILLOW","Firemaking",30,45,"Varrock",new[]{new Supply("tinderbox"),new Supply("willow logs",10)}),
        M("BURN_MAPLE","Firemaking",45,99,"Varrock",new[]{new Supply("tinderbox"),new Supply("maple logs",10)}),
        M("LEATHER_GLOVES","Crafting",1,7,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("leather",10)}),
        M("LEATHER_BOOTS","Crafting",7,9,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("leather",10)}),
        M("LEATHER_COWL","Crafting",9,11,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("leather",10)}),
        M("LEATHER_VAMBRACES","Crafting",11,14,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("leather",10)}),
        M("LEATHER_BODY","Crafting",14,18,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("leather",10)}),
        M("LEATHER_CHAPS","Crafting",18,28,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("leather",10)}),
        M("HARDLEATHER_BODY","Crafting",28,99,"Bank",new[]{new Supply("needle"),new Supply("thread",10),new Supply("hard leather",10)}),
        M("SMELT_BRONZE","Smithing",1,18,"Lumbridge furnace",new[]{new Supply("copper ore",10),new Supply("tin ore",10)}),
        M("BRONZE_PLATEBODY","Smithing",18,33,"Varrock anvil",new[]{new Supply("hammer"),new Supply("bronze bar",5)}),
        M("IRON_PLATEBODY","Smithing",33,48,"Varrock anvil",new[]{new Supply("hammer"),new Supply("iron bar",5)}),
        M("STEEL_PLATEBODY","Smithing",48,68,"Varrock anvil",new[]{new Supply("hammer"),new Supply("steel bar",5)}),
        M("MITHRIL_PLATEBODY","Smithing",68,88,"Varrock anvil",new[]{new Supply("hammer"),new Supply("mithril bar",5)}),
        M("ADAMANT_PLATEBODY","Smithing",88,99,"Varrock anvil",new[]{new Supply("hammer"),new Supply("adamantite bar",5)}),
        // Rune platebodies require 99, so they cannot be a route *to* level 99.
        M("AIR_RUNES","Runecraft",1,9,"Air Altar",new[]{new Supply("air talisman or air tiara"),new Supply("essence",20)}),
        M("EARTH_RUNES","Runecraft",9,20,"Earth Altar",new[]{new Supply("earth talisman or earth tiara"),new Supply("essence",20)}),
        M("BODY_RUNES","Runecraft",20,99,"Body Altar",new[]{new Supply("body talisman or body tiara"),new Supply("essence",20)}),
        M("BURY_BONES","Prayer",1,99,"Bank",new[]{new Supply("bones",10)}),
        M("WIND_STRIKE","Magic",1,99,"Lumbridge cows",new[]{new Supply("air rune",20),new Supply("mind rune",20),new Supply("food",5)},combat:true),
        M("RANGED_CHICKENS","Ranged",1,99,"Lumbridge chickens",new[]{new Supply("usable bow"),new Supply("compatible arrows",50),new Supply("food",5)},combat:true),
        M("ATTACK_CHICKENS","Attack",1,99,"Lumbridge chickens",new[]{new Supply("usable melee weapon"),new Supply("food",5)},combat:true),
        M("STRENGTH_CHICKENS","Strength",1,99,"Lumbridge chickens",new[]{new Supply("usable melee weapon"),new Supply("food",5)},combat:true),
        M("DEFENCE_CHICKENS","Defence",1,99,"Lumbridge chickens",new[]{new Supply("usable melee weapon"),new Supply("food",5)},combat:true),
        M("HITPOINTS_COMBAT","Hitpoints",1,99,"Lumbridge chickens",new[]{new Supply("usable melee weapon"),new Supply("food",5)},combat:true)
    };
    internal static readonly ToolUpgrade[] Axes=
    {
        new("iron axe",1,1,16),new("steel axe",6,5,31),new("black axe",11,10,null),
        new("mithril axe",21,20,51),new("adamant axe",31,30,71),new("rune axe",41,40,86)
    };
    internal static readonly ToolUpgrade[] Pickaxes=
    {
        new("iron pickaxe",1,1,null),new("steel pickaxe",6,5,null),new("black pickaxe",11,10,null),
        new("mithril pickaxe",21,20,null),new("adamant pickaxe",31,30,null),new("rune pickaxe",41,40,null)
    };
    internal static ToolUpgrade? BestTool(string skill,int level)=>
        (skill=="Woodcutting"?Axes:skill=="Mining"?Pickaxes:Array.Empty<ToolUpgrade>())
        .Where(t=>t.UseLevel<=level).OrderByDescending(t=>t.UseLevel).FirstOrDefault();

    // Deliberately small, verified recipe graph. Unknown products have no
    // fabricated recipe; a future runner must read the station and inputs.
    internal static readonly Recipe[] Recipes=
    {
        new("bronze bar",1,"Smithing",1,new[]{new Supply("copper ore"),new Supply("tin ore")},Array.Empty<Supply>(),"furnace"),
        new("steel bar",1,"Smithing",30,new[]{new Supply("iron ore"),new Supply("coal",2)},Array.Empty<Supply>(),"furnace"),
        new("mithril bar",1,"Smithing",50,new[]{new Supply("mithril ore"),new Supply("coal",4)},Array.Empty<Supply>(),"furnace"),
        new("adamantite bar",1,"Smithing",70,new[]{new Supply("adamantite ore"),new Supply("coal",6)},Array.Empty<Supply>(),"furnace"),
        new("runite bar",1,"Smithing",85,new[]{new Supply("runite ore"),new Supply("coal",8)},Array.Empty<Supply>(),"furnace"),
        new("gold bar",1,"Smithing",40,new[]{new Supply("gold ore")},Array.Empty<Supply>(),"furnace"),
        new("diamond amulet (u)",1,"Crafting",70,new[]{new Supply("gold bar"),new Supply("diamond")},new[]{new Supply("amulet mould")},"furnace"),
        new("diamond amulet",1,"Crafting",1,new[]{new Supply("diamond amulet (u)"),new Supply("ball of wool")},Array.Empty<Supply>(),"inventory"),
        new("amulet of power",1,"Magic",57,new[]{new Supply("diamond amulet"),new Supply("cosmic rune"),new Supply("earth rune",10)},Array.Empty<Supply>(),"Lvl-4 Enchant"),
        new("rune axe",1,"Smithing",86,new[]{new Supply("runite bar")},new[]{new Supply("hammer")},"anvil")
    };
    internal static readonly QuestOpportunity[] Quests=
    {
        new("Cook's Assistant","Cooking",300,4,0,new Dictionary<string,int>(),
            new[]{new Supply("egg"),new Supply("bucket of milk"),new Supply("pot of flour")}),
        new("Doric's Quest","Mining",1300,10,0,new Dictionary<string,int>(),
            new[]{new Supply("clay",6),new Supply("copper ore",4),new Supply("iron ore",2)}),
        new("The Knight's Sword","Smithing",12725,29,0,new Dictionary<string,int>{{"Mining",10}},
            new[]{new Supply("redberry pie"),new Supply("iron bar",2),new Supply("pickaxe")},true),
        new("The Restless Ghost","Prayer",1125,9,0,new Dictionary<string,int>(),Array.Empty<Supply>()),
        new("Sheep Shearer","Crafting",150,2,0,new Dictionary<string,int>(),new[]{new Supply("ball of wool",20)}),
        new("Misthalin Mystery","Crafting",600,6,0,new Dictionary<string,int>(),Array.Empty<Supply>()),
        new("Witch's Potion","Magic",325,4,0,new Dictionary<string,int>(),
            new[]{new Supply("eye of newt"),new Supply("onion"),new Supply("burnt meat"),new Supply("rat's tail")}),
        new("Imp Catcher","Magic",875,8,0,new Dictionary<string,int>(),
            new[]{new Supply("red bead"),new Supply("yellow bead"),new Supply("black bead"),new Supply("white bead")}),
        new("Vampyre Slayer","Attack",4825,20,0,new Dictionary<string,int>(),
            new[]{new Supply("hammer"),new Supply("stake"),new Supply("garlic"),new Supply("food",5)},true)
    };
    internal static QuestOpportunity? QuestFor(string skill,IReadOnlyList<PublicOsrsSkill> levels,ProgressionFacts facts)
    {
        if(facts.CompletedQuests is null)return null;
        return Quests.Where(q=>q.RewardSkill==skill&&!facts.CompletedQuests.Contains(q.Name)&&
            levels.Any(s=>s.Name==skill&&s.Level<q.SkipUntilLevel)&&
            (q.QuestPointsRequired==0||facts.QuestPoints>=q.QuestPointsRequired)&&
            q.SkillsRequired.All(r=>levels.Any(s=>s.Name==r.Key&&s.Level>=r.Value)))
            .OrderByDescending(q=>q.RewardXp).FirstOrDefault();
    }
    internal static F2PActivityPlan Plan(string? skill,IReadOnlyList<PublicOsrsSkill>? levels,
        ProgressionFacts facts,DateTimeOffset now)
    {
        if(skill is null||!AccountGoalPlanner.FreeSkills.Contains(skill)||levels is null)
            return new("NO_F2P_ACTIVITY",null,Array.Empty<string>(),"F2P_SCOPE_OR_SKILL_UNKNOWN");
        var values=levels.Where(s=>s.Name==skill).ToArray();
        if(values.Length!=1||values[0].Level is <1 or >=99)
            return new("NO_F2P_ACTIVITY",null,Array.Empty<string>(),"SKILL_INVALID_OR_ALREADY_MAXED");
        var method=Methods.Single(m=>m.Skill==skill&&m.MinimumLevel<=values[0].Level&&values[0].Level<m.NextLevel);
        var recommended=BestTool(skill,values[0].Level);
        if(facts.ObservedUtc is null||facts.ObservedUtc>now||now-facts.ObservedUtc>TimeSpan.FromSeconds(6))
            return new("READ_PREREQUISITES",method,new[]{"fresh_inventory_bank_quest_state"},"NO_FRESH_PREREQUISITE_EVIDENCE",recommended);
        var missing=new List<string>();
        if(facts.CompletedQuests is null)missing.Add("complete_quest_log");
        var quest=QuestFor(skill,levels,facts);
        if(quest is not null)
            return new("QUEST_OPPORTUNITY",method,new[]{"quest:"+quest.Name},
                quest.Dangerous?"QUEST_RUNNER_AND_COMBAT_SURVIVAL_REQUIRED":"QUEST_RUNNER_AND_ITEM_VERIFICATION_REQUIRED");
        if(method.Unlock is not null&&facts.CompletedQuests?.Contains(method.Unlock)!=true)missing.Add("quest:"+method.Unlock);
        foreach(var supply in method.Supplies)
            if(!HasSupply(supply,facts.Stock,levels))missing.Add(supply.Name+":"+supply.Quantity);
        // Even a fully provisioned plan never claims it has an implementation.
        return new(missing.Count>0?"ACQUIRE_SUPPLIES":"RUNNER_REQUIRED",method,missing,
            method.Combat?"VERIFY_HEALTH_FOOD_TARGET_AND_COMBAT_STYLE_BEFORE_COMBAT":"VERIFY_LOCATION_AND_ACTIVITY_BEFORE_ACTION",recommended);
    }
    private static bool HasSupply(Supply supply,IReadOnlyDictionary<string,int>? stock,IReadOnlyList<PublicOsrsSkill> levels)
    {
        if(stock is null)return false;
        bool Has(string name,int amount=1)=>stock.TryGetValue(name,out int count)&&count>=amount;
        if(supply.Name=="axe")return Axes.Any(t=>Has(t.Item)&&levels.Any(s=>s.Name=="Woodcutting"&&s.Level>=t.UseLevel))||Has("bronze axe");
        if(supply.Name=="pickaxe")return Pickaxes.Any(t=>Has(t.Item)&&levels.Any(s=>s.Name=="Mining"&&s.Level>=t.UseLevel))||Has("bronze pickaxe");
        if(supply.Name=="essence")return (stock.GetValueOrDefault("rune essence")+ (long)stock.GetValueOrDefault("pure essence"))>=supply.Quantity;
        if(supply.Name.Contains(" or "))return supply.Name.Split(" or ").Any(n=>Has(n,supply.Quantity));
        // Semantic categories (food/usable weapon) require the future item
        // observer's classification; arbitrary equipment names are insufficient.
        return Has(supply.Name,supply.Quantity);
    }
    internal static string RestrictedSales(ProgressionFacts facts)
    {
        if(facts.Ironman==true)return "IRONMAN_NO_GE_ITEM_TRADING";
        if(facts.Ironman is null)return "ACCOUNT_MODE_UNKNOWN";
        if(facts.EverMember==true)return "UNRESTRICTED";
        if(facts.PlayedHours is null||facts.QuestPoints is null||facts.TotalLevel is null)
            return "RESTRICTIONS_NOT_VERIFIED";
        return facts.PlayedHours>20&&facts.QuestPoints>=10&&facts.TotalLevel>=100?
            "UNRESTRICTED":"NEW_F2P_RESTRICTED_SALES";
    }
    internal static bool CanBuyOnGe(ProgressionFacts facts)=>facts.Ironman==false&&facts.FreeGeSlots>0;
    internal static AcquisitionDecision Acquire(string product,int quantity,IReadOnlyList<PublicOsrsSkill> levels,
        ProgressionFacts facts,AcquisitionQuote? buy,AcquisitionQuote? craft,bool stationVerified,DateTimeOffset now)
    {
        if(quantity<=0||facts.Reserve<0)throw new ArgumentOutOfRangeException(nameof(quantity));
        if(facts.ObservedUtc is null||facts.ObservedUtc>now||now-facts.ObservedUtc>TimeSpan.FromSeconds(6)||
            facts.Stock is null||facts.Coins is null||facts.Coins<0)
            return new("WAIT","FRESH_BANK_INVENTORY_AND_COINS_REQUIRED");
        int owned=Math.Max(0,facts.Stock.GetValueOrDefault(product));
        if(owned>=quantity)return new("USE_OWNED","SUFFICIENT_ITEMS_ALREADY_AVAILABLE");
        bool Affordable(AcquisitionQuote? quote)=>quote is not null&&quote.Product==product&&quote.MissingQuantity==quantity-owned&&
            quote.RunnerAvailable&&quote.TotalCost>=0&&
            quote.TotalCost<=Math.Max(0,facts.Coins.Value-facts.Reserve)&&quote.ObservedUtc<=now&&
            now-quote.ObservedUtc<=TimeSpan.FromSeconds(60);
        var recipe=Recipes.SingleOrDefault(r=>r.Product==product);
        long batches=recipe is null?0:((long)quantity-owned+recipe.Quantity-1)/recipe.Quantity;
        bool canCraft=recipe is not null&&stationVerified&&Affordable(craft)&&
            levels.Any(s=>s.Name==recipe.Skill&&s.Level>=recipe.Level)&&
            recipe.Inputs.All(i=>facts.Stock.GetValueOrDefault(i.Name)>=(long)i.Quantity*batches)&&
            recipe.Tools.All(t=>facts.Stock.GetValueOrDefault(t.Name)>=t.Quantity);
        bool canBuy=CanBuyOnGe(facts)&&Affordable(buy);
        // Quotes cover the entire missing quantity, including ingredients,
        // fees and supply acquisition. No profit is inferred from guide prices.
        return canCraft&&(!canBuy||craft!.TotalCost<=buy!.TotalCost)?new("CRAFT","VERIFIED_RECIPE_AND_TOTAL_BATCH_COST"):
            canBuy?new("BUY","VERIFIED_GE_BUY_AND_TOTAL_MISSING_QUANTITY_COST"):
            new("WAIT","NO_VERIFIED_AFFORDABLE_RUNNER_AND_SUPPLY_PATH");
    }
}

internal static class F2PProgressionTests
{
    internal static void Run()
    {
        static void Require(bool b,string m){if(!b)throw new Exception("F2P progression: "+m);}
        foreach(string skill in AccountGoalPlanner.FreeSkills)
        for(int level=1;level<99;level++)
            Require(F2PProgression.Methods.Count(m=>m.Skill==skill&&m.MinimumLevel<=level&&level<m.NextLevel)==1,
                $"complete unambiguous route {skill} {level}");
        Require(F2PProgression.BestTool("Mining",40)?.Item=="adamant pickaxe","rune pickaxe unlocks at 41 mining");
        Require(F2PProgression.BestTool("Mining",41)?.EquipAttack==40,"use level differs from wield level");
        Require(F2PProgression.Pickaxes.All(t=>t.SmithingLevel is null),"no invented pickaxe smithing");
        Require(F2PProgression.Axes.Single(t=>t.Item=="black axe").SmithingLevel is null,"black axe has no smithing recipe");
        var now=DateTimeOffset.UtcNow;
        var levels=new[]{new PublicOsrsSkill("Mining",15,0,0)};
        Require(F2PProgression.Plan("Mining",levels,new(),now).State=="READ_PREREQUISITES","never assume bank contents");
        Require(F2PProgression.Plan("Agility",levels,new(),now).Method is null,"membership skill blocked");
        var facts=new ProgressionFacts(new Dictionary<string,int>{{"rune pickaxe",1}},new HashSet<string>{"Doric's Quest"},ObservedUtc:now);
        Require(F2PProgression.Plan("Mining",levels,facts,now).State=="ACQUIRE_SUPPLIES","cannot use a too-high-tier owned tool");
        facts=facts with{Stock=new Dictionary<string,int>{{"iron pickaxe",1}}};
        Require(F2PProgression.Plan("Mining",levels,facts,now).State=="RUNNER_REQUIRED","supplied plan is not an executed action");
        Require(F2PProgression.Plan("Mining",levels,facts with{ObservedUtc=now.AddSeconds(-7)},now).State=="READ_PREREQUISITES","old stock discarded");
        var trade=new ProgressionFacts(Ironman:false,PlayedHours:20,QuestPoints:10,TotalLevel:100,FreeGeSlots:3);
        Require(F2PProgression.RestrictedSales(trade)=="NEW_F2P_RESTRICTED_SALES","strictly over 20 hours");
        Require(F2PProgression.RestrictedSales(trade with{PlayedHours=20.1})=="UNRESTRICTED","all three gates required");
        Require(F2PProgression.RestrictedSales(trade with{PlayedHours=21,QuestPoints=9})!="UNRESTRICTED","quest points gate");
        Require(F2PProgression.CanBuyOnGe(trade),"new F2P sale restrictions do not block ordinary purchases");
        Require(!F2PProgression.CanBuyOnGe(trade with{Ironman=true}),"ironman buys require separate bond exception");
        Require(!F2PProgression.CanBuyOnGe(trade with{FreeGeSlots=0}),"GE slot allocation");
        var smith=new[]{new PublicOsrsSkill("Smithing",86,0,0)};
        var supplies=new ProgressionFacts(new Dictionary<string,int>{{"runite bar",1},{"hammer",1}},
            Ironman:false,FreeGeSlots:3,Coins:1000,Reserve:200,ObservedUtc:now);
        var cheap=new AcquisitionQuote("rune axe",1,100,now,true);var expensive=new AcquisitionQuote("rune axe",1,500,now,true);
        Require(F2PProgression.Acquire("rune axe",1,smith,supplies,expensive,cheap,true,now).Method=="CRAFT","cheaper verified craft");
        Require(F2PProgression.Acquire("rune axe",1,smith,supplies,cheap,expensive,true,now).Method=="BUY","cheaper verified buy");
        Require(F2PProgression.Acquire("rune axe",2,smith,supplies,null,cheap with{MissingQuantity=2},true,now).Method=="WAIT","full batch materials required");
        Require(F2PProgression.Acquire("black axe",1,smith,supplies,null,cheap,true,now).Method=="WAIT","no fabricated black axe recipe");
        Require(F2PProgression.Acquire("rune axe",1,smith,supplies with{Ironman=true},cheap,null,true,now).Method=="WAIT","ironman cannot buy normal items");
        Require(F2PProgression.Acquire("rune axe",1,smith,supplies,cheap with{ObservedUtc=now.AddMinutes(-2)},null,true,now).Method=="WAIT","price freshness");
        Require(F2PProgression.Acquire("rune axe",1,smith,supplies,cheap with{RunnerAvailable=false},null,true,now).Method=="WAIT","missing executor blocks trading");
        Require(F2PProgression.Acquire("rune axe",1,smith,supplies,cheap with{Product="iron axe"},null,true,now).Method=="WAIT","quote must match product");
        var questFacts=new ProgressionFacts(CompletedQuests:new HashSet<string>());
        Require(F2PProgression.QuestFor("Smithing",new[]{new PublicOsrsSkill("Smithing",1,0,0),new PublicOsrsSkill("Mining",9,0,0)},questFacts) is null,
            "Knight's Sword requires 10 mining");
        Require(F2PProgression.QuestFor("Mining",new[]{new PublicOsrsSkill("Mining",1,0,0)},questFacts)?.Name=="Doric's Quest",
            "quest XP can skip low-level grind");
        Console.WriteLine("F2P PROGRESSION PASS: all 15 skill routes 1-98, tool use/wield levels, recipes, fresh supplies, account-mode and trade gates");
    }
}
