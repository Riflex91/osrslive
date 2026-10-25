using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal sealed record CombatSpec(string Id,string Skill,string Style,string[] Weapons);
internal sealed record PendingCombat(string Id,string Subject,string Skill,long BeforeXp,DateTimeOffset PreparedUtc,string Phase="AttackPrepared");

internal static class CombatPolicy
{
    // Deliberately bounded first target: the level-1 chicken. Other NPCs and
    // player attack entries cannot satisfy this exact menu predicate.
    internal static readonly CombatSpec[] Specs=
    {
        new("ATTACK_CHICKENS","Attack","Chop",new[]{"iron scimitar","bronze scimitar"}),
        new("STRENGTH_CHICKENS","Strength","Slash",new[]{"iron scimitar","bronze scimitar"}),
        new("DEFENCE_CHICKENS","Defence","Block",new[]{"iron scimitar","bronze scimitar"}),
        new("RANGED_CHICKENS","Ranged","Accurate",new[]{"shortbow"}),
        new("WIND_STRIKE","Magic","Wind Strike",Array.Empty<string>())
    };
    internal static readonly string[] Food={"shrimps","cooked chicken","cooked meat","sardine","herring","trout","salmon","tuna","lobster","swordfish"};
    internal static bool Danger(IReadOnlyList<VisualLabel> labels)=>labels.Any(l=>l.Kind==VisualLabelKind.Text&&
        ((l.Bounds.Top<350&&(Regex.IsMatch(l.Text,@"\bWilderness\b|^Level:\s*\d+",RegexOptions.IgnoreCase)))||
        Regex.IsMatch(l.Text.Trim(),@"^(?:Oh dear, you are dead[!.]?|You have died[!.]?|Death's Office|You have been poisoned[!.]?)$",RegexOptions.IgnoreCase)));
    internal static bool Healthy(int? hp,int max,int percent)=>hp is >0&&max is >=10 and <=99&&hp<=max&&hp*100>=max*percent;
    internal static long? Xp(IReadOnlyList<VisualLabel> labels,string skill)
    {
        var pattern=new Regex("^"+Regex.Escape(skill)+@"\s+XP:\s*([0-9][0-9,]*)$",RegexOptions.IgnoreCase);
        var hits=labels.Where(l=>l.Kind==VisualLabelKind.Text&&l.Bounds.Left>=700&&l.Bounds.Top>=200)
            .Select(l=>pattern.Match(l.Text.Trim())).Where(m=>m.Success).ToArray();
        return hits.Length==1&&long.TryParse(hits[0].Groups[1].Value.Replace(",",""),out long xp)&&xp<=200_000_000?xp:null;
    }
    internal static string? Supply(CombatSpec spec,InventoryState inventory)
    {
        if(!inventory.Items.Any(i=>Food.Contains(i.Name)))return "trout";
        if(spec.Weapons.Length>0&&!spec.Weapons.Any(w=>inventory.Find(w) is not null))return spec.Weapons[^1];
        if(spec.Skill=="Ranged"&&inventory.Count("bronze arrow") is not >=10)return "bronze arrow";
        if(spec.Skill=="Magic"&&inventory.Count("air rune") is not >0)return "air rune";
        if(spec.Skill=="Magic"&&inventory.Count("mind rune") is not >0)return "mind rune";
        return null;
    }
}

internal sealed class CombatJournal
{
    private readonly string file,subject;
    internal CombatJournal(string file,string subject){this.file=file;this.subject=subject;}
    internal PendingCombat? Pending()
    {
        if(!File.Exists(file))return null;
        var p=JsonSerializer.Deserialize<PendingCombat>(File.ReadAllText(file));
        if(p is null||p.Subject!=subject||!CombatPolicy.Specs.Any(s=>s.Skill==p.Skill)||p.BeforeXp is <0 or >200_000_000||
            string.IsNullOrWhiteSpace(p.Id)||p.PreparedUtc>DateTimeOffset.UtcNow||p.Phase is not ("Preparing" or "AttackPrepared" or "Retreating"))
            throw new RoutineBlockedException("COMBAT_JOURNAL_INVALID_OR_OTHER_CHARACTER");
        return p;
    }
    internal void Prepare(string id,string skill,long xp,string phase="AttackPrepared")
    {
        if(File.Exists(file))throw new RoutineBlockedException("COMBAT_ALREADY_PENDING");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new PendingCombat(id,subject,skill,xp,DateTimeOffset.UtcNow,phase)));File.Move(temp,file,false);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal void MarkAttack(string id)
        =>Phase(id,"AttackPrepared");
    internal void MarkRetreat(string id)=>Phase(id,"Retreating");
    private void Phase(string id,string phase)
    {
        var pending=Pending();if(pending?.Id!=id||phase=="AttackPrepared"&&pending.Phase!="Preparing")throw new RoutineBlockedException("COMBAT_PREPARATION_ID_CHANGED");
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(pending with{Phase=phase}));File.Move(temp,file,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal void Complete(string id)
    {if(Pending()?.Id!=id)throw new RoutineBlockedException("COMBAT_JOURNAL_ID_CHANGED");File.Delete(file);}
}

internal sealed class CombatRoutine
{
    private readonly GameInteractions game;private readonly CombatJournal journal;private readonly WorldNavigator atlas;
    private readonly int maxHp;private readonly Action<string> report;
    private bool reportedUnreadableHealth;
    internal CombatRoutine(GameInteractions game,CombatJournal journal,WorldNavigator atlas,int maxHp,Action<string>? report=null)
    {
        this.game=game;this.journal=journal;this.atlas=atlas;this.maxHp=maxHp;this.report=report??(_=>{});
        if(game.Driver is WindowVisualGameDriver driver)driver.Guard=_=>Check();
    }
    private int? Hp=>game.Driver.Current.Pixels?.Navigation?.Hitpoints;
    private void Check()
    {
        if(CombatPolicy.Danger(game.Driver.Current.Labels))throw new RoutineBlockedException("COMBAT_DANGEROUS_SCREEN_STOPPED");
        if(!CombatPolicy.Healthy(Hp,maxHp,1))
        {
            if(!reportedUnreadableHealth)
            {
                reportedUnreadableHealth=true;
                var frame=game.Driver.Current;
                report("COMBAT_HEALTH_EVIDENCE "+
                    $"minimap_marker={frame.Labels.Any(l=>l.Kind==VisualLabelKind.Minimap)} "+
                    $"navigation_pixels={frame.Pixels?.Navigation is not null} "+
                    $"hp_ocr_marker={frame.Labels.Any(l=>l.Kind==VisualLabelKind.Hitpoints)} "+
                    $"hp_navigation_readable={frame.Pixels?.Navigation?.Hitpoints is not null} "+
                    $"native_hud_marker={frame.Labels.Any(l=>l.Kind==VisualLabelKind.GameHud)} "+
                    "screenshot_persisted=false no_combat_input=true");
            }
            throw new RoutineBlockedException("COMBAT_HEALTH_UNREADABLE_OR_DEAD");
        }
    }
    internal async Task<RoutineResult> Run(CombatSpec spec,string id,IReadOnlyList<PublicOsrsSkill> skills,CancellationToken ct)
    {
        if(journal.Pending() is not null)return await Resume(ct);
        if(!skills.Any(s=>s.Name==spec.Skill&&s.Level is >=1 and <99))return new(RoutineStatus.Blocked,"COMBAT_SKILL_MAXED_OR_MISSING");
        await game.Driver.ObserveAsync(ct);Check();
        var inventory=await game.Inventory(ct);
        if(inventory.EmptySlots<2)return new(RoutineStatus.Blocked,"COMBAT_NEEDS_TWO_FREE_SLOTS_FOR_LOADOUT");
        if(CombatPolicy.Supply(spec,inventory) is string missing)return new(RoutineStatus.Blocked,"COMBAT_SUPPLY_REQUIRED_"+missing);
        var healing=await SurvivalRoutine.Heal(game,maxHp,90,ct,inventory);if(healing is not null)return healing;
        long before=await ReadXp(spec.Skill,ct);
        journal.Prepare(id,spec.Skill,before,"Preparing");
        await game.OpenInventory(ct);
        if(spec.Weapons.Length>0)
        {
            var weapon=spec.Weapons.Select(w=>inventory.Find(w)).First(i=>i is not null)!;
            await Equip(weapon,ct);
        }
        if(spec.Skill=="Ranged")await Equip(inventory.Find("bronze arrow")!,ct);
        if(spec.Skill!="Magic")await SelectStyle(spec,ct);
        await game.OpenInventory(ct);
        // Food slots may have changed while equipping a previously held weapon.
        inventory=await game.Inventory(ct,false);
        Check();if(!CombatPolicy.Healthy(Hp,maxHp,90))return new(RoutineStatus.Blocked,"HEALTH_CHANGED_DURING_COMBAT_PREPARATION");
        var target=await FindChicken(spec.Skill=="Magic",ct);
        // Persist before the only attack/cast click. A pause, crash or lost
        // receipt resumes monitoring; it never blindly sends a second attack.
        journal.MarkAttack(id);
        await game.Click(target,ct);
        return await Monitor(journal.Pending()!,inventory,ct);
    }
    internal async Task<RoutineResult> Resume(CancellationToken ct)
    {
        var pending=journal.Pending()??throw new RoutineBlockedException("NO_PENDING_COMBAT");
        report("RECONCILING_EXISTING_COMBAT");
        if(pending.Phase=="Retreating")return await Retreat(pending,ct);
        if(pending.Phase=="Preparing")
        {
            // An interrupted preparation has not sent an attack. Reconcile
            // worn supplies before allowing the planner to purchase anything.
            var spec=CombatPolicy.Specs.Single(s=>s.Skill==pending.Skill);
            foreach(string weapon in spec.Weapons)if(await RemoveIfEquipped(weapon,ct))break;
            if(spec.Skill=="Ranged")await RemoveIfEquipped("bronze arrow",ct);
            journal.Complete(pending.Id);
            return new(RoutineStatus.Progress,"INTERRUPTED_LOADOUT_PREPARATION_RECOVERED_NO_ATTACK_SENT");
        }
        Check();return await Monitor(pending,await game.Inventory(ct),ct);
    }
    private async Task Equip(InventoryItem item,CancellationToken ct)
    {
        await game.Choose(item.Bounds,"Wield "+item.Name,ct);
        var tab=await game.FindControl("Worn Equipment",new(760,265,235,330),ct);
        await game.Click(tab,ct);
        // Exact equipment Remove entry proves an equipped item, rather than
        // assuming that disappearing from inventory means it was equipped.
        bool found=false;
        for(int row=0;row<6&&!found;row++)for(int col=0;col<5&&!found;col++)
        {
            Check();var box=new Rectangle(803+col*33,315+row*39,4,4);
            var menu=await game.OpenMenu(box,ct);found=menu.Exact("Remove "+item.Name) is not null;
            await game.Escape(ct);
        }
        if(!found)throw new RoutineBlockedException("EQUIPPED_WEAPON_OR_AMMO_NOT_VERIFIED");
        await game.OpenInventory(ct);
    }
    private async Task SelectStyle(CombatSpec spec,CancellationToken ct)
    {
        var tab=await game.FindControl("Combat Options",new(760,265,235,330),ct);await game.Click(tab,ct);
        var button=ExecutionUi.Unique(game.Driver.Current.Labels,spec.Style,new(760,290,235,280))??
            throw new RoutineBlockedException("COMBAT_STYLE_BUTTON_NOT_IDENTIFIED");
        await game.Driver.InputAsync(new(GameInputKind.Hover,button.Bounds),ct);Check();
        if(!game.Driver.Current.Labels.Any(l=>l.Bounds.Left>=700&&l.Bounds.Top>=250&&
            l.Text.Contains(spec.Skill+" XP",StringComparison.OrdinalIgnoreCase)))
            throw new RoutineBlockedException("COMBAT_STYLE_SKILL_TOOLTIP_REQUIRED");
        await game.Click(button.Bounds,ct);
    }
    private async Task<Rectangle> FindChicken(bool spell,CancellationToken ct)
    {
        // Only hover verified scenery. Attack-level suffix is part of the exact
        // menu entry, including spell target binding for the magic variant.
        for(int row=0;row<7;row++)for(int col=0;col<11;col++)
        {
            Check();var box=new Rectangle(48+col*64,63+row*45,5,5);
            await game.Driver.InputAsync(new(GameInputKind.Hover,box),ct);
            if(!game.Driver.Current.Labels.Any(l=>l.Bounds.Top<45&&
                Regex.IsMatch(l.Text,@"^Attack Chicken \(level-1\)(?:\s*/.*)?$",RegexOptions.IgnoreCase)))continue;
            if(spell)
            {
                var tab=await game.FindControl("Magic",new(760,265,235,330),ct);await game.Click(tab,ct);
                var wind=await game.FindControl("Wind Strike",new(775,300,220,290),ct);await game.Click(wind,ct);
            }
            var menu=await game.OpenMenu(box,ct);
            var hit=menu.Exact(spell?"Cast Wind Strike -> Chicken (level-1)":"Attack Chicken (level-1)");
            if(hit is not null){Check();return hit.Bounds;}
            await game.Escape(ct);
        }
        throw new RoutineBlockedException("LEVEL_ONE_CHICKEN_NOT_IDENTIFIED");
    }
    private async Task<long> ReadXp(string skill,CancellationToken ct)
    {
        var tab=await game.FindControl("Skills",new(760,265,235,330),ct);await game.Click(tab,ct);
        var layout=GameScreenReader.LocateSkills(game.Driver.Current.Labels,new(1000,600))??
            throw new RoutineBlockedException("COMBAT_SKILLS_PANEL_NOT_READABLE");
        int i=Array.IndexOf(GameScreenReader.GridSkills,skill);
        var box=new Rectangle((int)(layout.Bounds.Left+(i%3*62+25)*layout.Scale),
            (int)(layout.Bounds.Top+(i/3*30+12)*layout.Scale),5,5);
        await game.Driver.InputAsync(new(GameInputKind.Hover,box),ct);
        long xp=CombatPolicy.Xp(game.Driver.Current.Labels,skill)??throw new RoutineBlockedException("COMBAT_EXACT_SKILL_XP_TOOLTIP_REQUIRED");
        var time=game.Driver.Current.CapturedUtc;
        await Task.Delay(250,ct);await game.Driver.ObserveAsync(ct);
        if(game.Driver.Current.CapturedUtc<=time||CombatPolicy.Xp(game.Driver.Current.Labels,skill)!=xp)
            throw new RoutineBlockedException("COMBAT_XP_NEEDS_INDEPENDENT_CONFIRMATION");
        return xp;
    }
    private async Task<RoutineResult> Monitor(PendingCombat pending,InventoryState inventory,CancellationToken ct)
    {
        var food=inventory.Items.Where(i=>CombatPolicy.Food.Contains(i.Name)).ToList();
        await game.OpenInventory(ct);
        for(int poll=0;poll<30;poll++)
        {
            await Task.Delay(600,ct);await game.Driver.ObserveAsync(ct);Check();
            report("COMBAT_MONITORING_HP_"+Hp);
            if(!CombatPolicy.Healthy(Hp,maxHp,70))
            {
                var bite=food.FirstOrDefault();
                if(bite is null)return await Retreat(pending,ct);
                int before=Hp!.Value;await game.Choose(bite.Bounds,"Eat "+bite.Name,ct);food.Remove(bite);
                await Task.Delay(1800,ct);await game.Driver.ObserveAsync(ct);Check();
                if(Hp<=before)return await Retreat(pending,ct);
            }
        }
        long after=await ReadXp(pending.Skill,ct);
        Check();await game.OpenInventory(ct);
        if(after>pending.BeforeXp)
        {
            // Put this bounded executor's loadout back in inventory. This also
            // keeps the procurement planner from buying an already worn weapon.
            var spec=CombatPolicy.Specs.Single(s=>s.Skill==pending.Skill);
            foreach(var weapon in spec.Weapons)
                if(await RemoveIfEquipped(weapon,ct))break;
            if(spec.Skill=="Ranged")await RemoveIfEquipped("bronze arrow",ct);
            journal.Complete(pending.Id);
            // A receipt for XP, NOT a claim that the NPC died or loot belongs
            // to this character. No unattended ground-item collection here.
            return new(RoutineStatus.Completed,"COMBAT_SKILL_XP_GAIN_VERIFIED",(int)(after-pending.BeforeXp));
        }
        if(DateTimeOffset.UtcNow-pending.PreparedUtc>TimeSpan.FromMinutes(3))
            return new(RoutineStatus.UnknownOutcome,"COMBAT_NO_XP_AFTER_THREE_MINUTES_REVIEW_REQUIRED");
        return new(RoutineStatus.Waiting,"COMBAT_PENDING_NO_XP_RECEIPT_YET");
    }
    internal async Task<bool> RemoveIfEquipped(string name,CancellationToken ct)
    {
        var tab=await game.FindControl("Worn Equipment",new(760,265,235,330),ct);await game.Click(tab,ct);
        for(int row=0;row<6;row++)for(int col=0;col<5;col++)
        {
            var box=new Rectangle(803+col*33,315+row*39,4,4);
            var menu=await game.OpenMenu(box,ct);var remove=menu.Exact("Remove "+name);
            if(remove is not null)
            {
                await game.Click(remove.Bounds,ct);
                var inventory=await game.Inventory(ct);
                if(inventory.Find(name) is null)throw new RoutineBlockedException("REMOVED_EQUIPMENT_NOT_IN_INVENTORY");
                return true;
            }
            await game.Escape(ct);
        }
        return false;
    }
    private async Task<RoutineResult> Retreat(PendingCombat pending,CancellationToken ct)
    {
        journal.MarkRetreat(pending.Id);
        report("COMBAT_RETREATING_TO_SAFE_CHECKPOINT");
        var result=await new TravelRoutine(game,atlas,maxHp,report,1).Run("safe",ct);
        if(result.Status!=RoutineStatus.Completed)return result;
        var spec=CombatPolicy.Specs.Single(s=>s.Skill==pending.Skill);
        foreach(var weapon in spec.Weapons)if(await RemoveIfEquipped(weapon,ct))break;
        if(spec.Skill=="Ranged")await RemoveIfEquipped("bronze arrow",ct);
        journal.Complete(pending.Id);
        return new(RoutineStatus.Progress,"COMBAT_RETREAT_DESTINATION_VERIFIED_REPLAN_SUPPLIES");
    }
}
