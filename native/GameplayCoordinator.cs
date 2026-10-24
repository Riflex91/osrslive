using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace OSRSLiveDiagnostics;

internal sealed record AgentSettings(bool Ironman=false,long ReserveCoins=500,bool Enabled=true,bool CombatEnabled=true);
internal sealed record AgentJob(string Id,string Kind,string Target,int Quantity=1,long? UnitPrice=null);
internal sealed record SupplyIntent(string Subject,AgentJob Order);

internal sealed class GameplayCoordinator:IDisposable
{
    private readonly Diagnostics log;
    private readonly WikiPriceService prices=new();
    private readonly WorldNavigator navigator=new();
    private readonly string directory;
    private VisibleGameObservation? subject;
    private DateTimeOffset leaseUntil,nextAttempt;
    private bool uncertain;
    private DateTimeOffset nextInspection;
    internal string State {get;private set;}="WAITING_FOR_SKILLS";
    internal bool HasSubject=>subject is not null&&DateTimeOffset.UtcNow<leaseUntil;
    internal GameplayCoordinator(Diagnostics log)
    {this.log=log;directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSPlay","agent");}
    internal void Reset(){subject=null;uncertain=false;leaseUntil=default;nextAttempt=default;Set("WAITING_FOR_SKILLS");}
    private void Set(string state)
    {
        if(State==state)return;State=state;
        log.Add(Severity.INFO,"GAMEPLAY","STATE","state="+state);
    }
    private T Read<T>(string file,T fallback)
    {
        string path=Path.Combine(directory,file);
        return File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path))??throw new RoutineBlockedException("AGENT_CONFIG_INVALID"):fallback;
    }
    internal async Task InspectNavigation(VisualFrame frame,CancellationToken ct)
    {
        if(DateTimeOffset.UtcNow<nextInspection)return;
        nextInspection=DateTimeOffset.UtcNow.AddSeconds(10);
        var pixels=frame.Pixels?.Navigation;
        if(pixels is null)
        {
            log.Add(Severity.INFO,"NAVIGATION","OBSERVATION_ONLY","minimap_identified=false labels="+frame.Labels.Count);
            return;
        }
        await navigator.Ready(Set,ct);
        if(DateTimeOffset.UtcNow-frame.CapturedUtc>TimeSpan.FromSeconds(3))return;
        var position=navigator.Locate(frame);
        log.Add(Severity.INFO,"NAVIGATION","OBSERVATION_ONLY",$"minimap_identified=true hp={pixels.Hitpoints?.ToString()??"unknown"} "+
            $"position_identified={position is not null} x={position?.Tile.X} y={position?.Tile.Y} plane={position?.Tile.Plane} inliers={position?.Inliers} labels={frame.Labels.Count} no_input=true");
    }
    internal async Task<bool> Tick(VisualFrame frame,VisibleGameObservation? confirmed,
        Func<CancellationToken,Task<VisualFrame?>> capture,
        Func<VisualFrame,GameInput,CancellationToken,Task<bool>> dispatch,CancellationToken stopping)
    {
        if(confirmed is not null)
        {
            if(subject?.Character!=confirmed.Character)Reset();
            subject=confirmed;leaseUntil=confirmed.ObservedUtc.AddMinutes(4);
        }
        var current=GameScreenReader.Read(frame);
        if(current.Character is not null&&subject is not null&&current.Character!=subject.Character){Reset();return false;}
        if(!HasSubject||subject?.Skills is null)return false;
        // Refresh the skill panel before starting another potentially long
        // visual inspection. Do not extend an old skill lease with chat alone.
        if(leaseUntil-DateTimeOffset.UtcNow<TimeSpan.FromMinutes(3))return false;
        if(DateTimeOffset.UtcNow<nextAttempt)return true;
        var settings=Read("settings.json",new AgentSettings());
        if(!settings.Enabled){Set("DISABLED_IN_SETTINGS");return true;}
        if(settings.ReserveCoins<0)throw new RoutineBlockedException("NEGATIVE_RESERVE");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(stopping);
        budget.CancelAfter(TimeSpan.FromMinutes(3));
        var driver=new WindowVisualGameDriver(frame,capture,async (f,input,ct)=>HasSubject&&
            (GameScreenReader.Read(f).Character is not string name||name==subject.Character)&&await dispatch(f,input,ct),
            action=>Set("INPUT_"+action.ToUpperInvariant()));
        var game=new GameInteractions(driver);
        RoutineResult result;
        string? queuedId=null;
        int retrySeconds=10;
        try
        {
            Directory.CreateDirectory(directory);
            using var exclusive=new FileStream(Path.Combine(directory,"execution.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var queued=Read<AgentJob?>("job.json",null);queuedId=queued?.Id;
            string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject.Character!.ToUpperInvariant())));
            var journal=new TradeJournal(Path.Combine(directory,"pending-trade.json"),hash);
            var combatJournal=new CombatJournal(Path.Combine(directory,"pending-combat.json"),hash);
            var atlas=navigator;
            int maxHp=subject.Skills.Single(s=>s.Name=="Hitpoints").Level;
            var training=new TrainingRoutines(game);var bank=new BankRoutine(game);
            if(journal.Pending() is PendingTrade pending)
            {
                if(queuedId!=pending.Order.Id)queuedId=null;
                Set("RECONCILING_EXISTING_TRADE");result=await new TradeRoutine(game,journal).Resume(budget.Token);
            }
            else if(combatJournal.Pending() is PendingCombat encounter)
            {
                if(queuedId!=encounter.Id)queuedId=null;
                if(uncertain){Set("UNCERTAIN_COMBAT_REQUIRES_REVIEW");return true;}
                result=await new CombatRoutine(game,combatJournal,atlas,maxHp,Set).Resume(budget.Token);
            }
            else if(uncertain){Set("UNCERTAIN_RESULT_REQUIRES_REVIEW");return true;}
            else if(queued is null&&Read<SupplyIntent?>("pending-supply.json",null) is SupplyIntent supply)
            {
                if(supply.Subject!=hash)throw new RoutineBlockedException("SUPPLY_INTENT_BELONGS_TO_OTHER_CHARACTER");
                var stock=await game.Inventory(budget.Token);
                if(stock.Count(supply.Order.Target) is int have&&have>=supply.Order.Quantity)
                    result=new(RoutineStatus.Completed,"PENDING_SUPPLY_ALREADY_IN_INVENTORY");
                else result=await Go(game,atlas,maxHp,"ge",budget.Token)??await Trade(game,journal,supply.Order,settings,budget.Token);
                if(result.Status==RoutineStatus.Completed)File.Delete(Path.Combine(directory,"pending-supply.json"));
            }
            else if(queued is not null)
            {
                if(queued.Quantity is <1 or >1000||string.IsNullOrWhiteSpace(queued.Id))throw new RoutineBlockedException("JOB_INVALID");
                Set("JOB_"+queued.Kind.ToUpperInvariant());
                string? destination=queued.Kind switch
                {"train" or "combat"=>queued.Target,"craft"=>CraftStation(queued.Target),"withdraw" or "deposit"=>"bank","buy" or "sell"=>"ge",_=>null};
                var travel=destination is null?null:await Go(game,atlas,maxHp,destination,budget.Token);
                result=travel??(queued.Kind switch
                {
                    "train" when queued.Quantity==1=>await training.Run(ExecutionCatalog.Training.Single(s=>s.Id==queued.Target),subject.Skills,budget.Token),
                    "train"=>new(RoutineStatus.Blocked,"TRAINING_JOB_IS_ONE_VERIFIED_STEP"),
                    "craft"=>await training.Craft(F2PProgression.Recipes.Single(r=>r.Product==queued.Target),queued.Quantity,subject.Skills,budget.Token),
                    "withdraw"=>await bank.Withdraw(queued.Target,queued.Quantity,budget.Token),
                    "deposit"=>await bank.Deposit(new HashSet<string>(StringComparer.OrdinalIgnoreCase){queued.Target},budget.Token),
                    "buy" or "sell"=>await Trade(game,journal,queued,settings,budget.Token),
                    "combat" when queued.Quantity==1&&settings.CombatEnabled=>
                        await new CombatRoutine(game,combatJournal,atlas,maxHp,Set).Run(CombatPolicy.Specs.Single(s=>s.Id==queued.Target),queued.Id,subject.Skills,budget.Token),
                    "travel" when queued.Quantity==1=>await new TravelRoutine(game,atlas,maxHp,Set).Run(queued.Target,budget.Token),
                    "combat" or "travel"=>new(RoutineStatus.Blocked,"COMBAT_DISABLED_OR_SINGLE_STEP_REQUIRED"),
                    _=>new(RoutineStatus.Blocked,"UNKNOWN_JOB_KIND")
                });
            }
            else
            {
                Set("READING_INVENTORY_FOR_AUTOMATIC_ACTIVITY");
                var inventory=await game.Inventory(budget.Token);
                var candidates=ExecutionCatalog.Training.Where(spec=>F2PProgression.Methods.Any(m=>m.Id==spec.Id&&
                    subject.Skills.Any(s=>s.Name==m.Skill&&s.Level>=m.MinimumLevel&&s.Level<m.NextLevel)))
                    .OrderBy(spec=>subject.Skills.Single(s=>s.Name==spec.Skill).Level).ToArray();
                var ready=candidates.FirstOrDefault(s=>Supplied(s,inventory,subject.Skills));
                var combat=settings.CombatEnabled?CombatPolicy.Specs
                    .Where(s=>subject.Skills.Any(level=>level.Name==s.Skill&&level.Level<99))
                    .OrderBy(s=>subject.Skills.Single(level=>level.Name==s.Skill).Level).FirstOrDefault():null;
                var products=new HashSet<string>(ExecutionCatalog.Training.Select(s=>s.Product).Where(p=>p.Length>0),StringComparer.OrdinalIgnoreCase);
                if(inventory.EmptySlots<2&&inventory.Items.Any(i=>products.Contains(i.Name)))
                    result=await Go(game,atlas,maxHp,"bank",budget.Token)??await bank.Deposit(products,budget.Token);
                else if(combat is not null&&(ready is null||subject.Skills.Single(s=>s.Name==combat.Skill).Level<=subject.Skills.Single(s=>s.Name==ready.Skill).Level))
                {
                    Set("COMBAT_"+combat.Id);
                    if(CombatPolicy.Supply(combat,inventory) is string combatMissing)
                    {
                        // Check worn stock before purchasing another copy.
                        var wornCandidates=combatMissing=="bronze arrow"?new[]{"bronze arrow"}:
                            combat.Weapons.Contains(combatMissing)?combat.Weapons:Array.Empty<string>();
                        bool recovered=false;
                        if(wornCandidates.Length>0)
                        {
                            var fighter=new CombatRoutine(game,combatJournal,atlas,maxHp,Set);
                            foreach(var worn in wornCandidates)if(await fighter.RemoveIfEquipped(worn,budget.Token)){recovered=true;break;}
                        }
                        result=recovered?new(RoutineStatus.Progress,"WORN_COMBAT_SUPPLY_RECOVERED_BEFORE_PURCHASE"):
                            await Replenish(game,journal,atlas,maxHp,combatMissing,settings,hash,budget.Token);
                    }
                    else result=await Go(game,atlas,maxHp,combat.Id,budget.Token)??
                            await new CombatRoutine(game,combatJournal,atlas,maxHp,Set).Run(combat,Guid.NewGuid().ToString("N"),subject.Skills,budget.Token);
                }
                else if(ready is not null)
                {Set("TRAINING_"+ready.Id);result=await Go(game,atlas,maxHp,ready.Id,budget.Token)??await training.Run(ready,subject.Skills,budget.Token);}
                else
                {
                    if(inventory.EmptySlots==0&&inventory.Items.Any(i=>products.Contains(i.Name)))
                        result=await Go(game,atlas,maxHp,"bank",budget.Token)??await bank.Deposit(products,budget.Token);
                    else
                    {
                        var next=candidates.FirstOrDefault();
                        string? missing=next is null?null:Missing(next,inventory,subject.Skills);
                        if(missing is null)result=new(RoutineStatus.Blocked,"NO_SUPPORTED_SUPPLIED_ACTIVITY");
                        else
                        {
                            result=await Replenish(game,journal,atlas,maxHp,missing,settings,hash,budget.Token);
                        }
                    }
                }
            }
            uncertain=result.Status==RoutineStatus.UnknownOutcome;
            retrySeconds=result.Status==RoutineStatus.Progress?1:result.Status==RoutineStatus.Completed?2:10;
            Set(result.Status.ToString().ToUpperInvariant()+"_"+result.Reason);
            log.Add(result.Status is RoutineStatus.Blocked or RoutineStatus.UnknownOutcome?Severity.WARN:Severity.INFO,
                "GAMEPLAY","ROUTINE_RESULT",$"status={result.Status} reason={result.Reason} produced={result.Produced}");
            if(result.Status==RoutineStatus.Completed&&queuedId is not null)
            {
                var stillQueued=Read<AgentJob?>("job.json",null);
                if(stillQueued?.Id==queuedId)File.Delete(Path.Combine(directory,"job.json"));
            }
        }
        catch(OperationCanceledException) when(!stopping.IsCancellationRequested)
        {Set("ROUTINE_TIMEOUT_STOPPED");uncertain=true;}
        catch(RoutineBlockedException ex){Set("BLOCKED_"+ex.Message);}
        catch(IOException){Set("ANOTHER_AGENT_OR_JOURNAL_IO_BUSY");}
        catch(Exception ex)
        {Set("ROUTINE_ERROR_"+ex.GetType().Name);log.Add(Severity.WARN,"GAMEPLAY","ROUTINE_ERROR","reason="+ex.GetType().Name);uncertain=true;}
        nextAttempt=DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
        return true;
    }
    private async Task<RoutineResult> Replenish(GameInteractions game,TradeJournal journal,WorldNavigator atlas,int hp,
        string item,AgentSettings settings,string hash,CancellationToken ct)
    {
        Set("REPLENISHING_"+item.Replace(' ','_').ToUpperInvariant());
        int quantity=item=="bronze arrow"?50:item is "air rune" or "mind rune"?50:CombatPolicy.Food.Contains(item)?5:1;
        var result=await Go(game,atlas,hp,"bank",ct)??await new BankRoutine(game).Withdraw(item,Math.Min(quantity,28),ct);
        // Preserve the acquisition intent across the bank -> GE journey. A
        // completed walk must not send the planner back to the bank forever.
        if(result.Reason=="VERIFIED_BANK_SEARCH_EMPTY"&&!settings.Ironman)
        {
            var order=new AgentJob(Guid.NewGuid().ToString("N"),"buy",item,quantity);
            string path=Path.Combine(directory,"pending-supply.json");
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,JsonSerializer.Serialize(new SupplyIntent(hash,order)));File.Move(temp,path,false);}
            finally{if(File.Exists(temp))File.Delete(temp);}
            result=await Go(game,atlas,hp,"ge",ct)??await Trade(game,journal,order,settings,ct);
            if(result.Status==RoutineStatus.Completed)File.Delete(path);
        }
        return result;
    }
    private async Task<RoutineResult?> Go(GameInteractions game,WorldNavigator atlas,int hp,string destination,CancellationToken ct)
    {
        if(NavigationDestinations.Service(destination) is null)return null;
        var result=await new TravelRoutine(game,atlas,hp,Set).Run(destination,ct);
        if(result.Status!=RoutineStatus.Completed)return result;
        return null;
    }
    private static string? CraftStation(string product)=>F2PProgression.Recipes.FirstOrDefault(r=>r.Product==product)?.Station switch
    {"furnace"=>"furnace","anvil"=>"anvil",_=>null};
    private async Task<RoutineResult> Trade(GameInteractions game,TradeJournal journal,AgentJob job,AgentSettings settings,CancellationToken ct)
    {
        bool sell=job.Kind=="sell";
        var quote=await prices.Get(job.Target,sell,ct);
        if(quote is null||job.Quantity>quote.BuyLimit)return new(RoutineStatus.Blocked,"FRESH_F2P_PRICE_AND_BUY_LIMIT_REQUIRED");
        if(job.UnitPrice is not null&&job.UnitPrice!=quote.Price)return new(RoutineStatus.Blocked,"REQUESTED_PRICE_DIFFERS_FROM_FRESH_QUOTE");
        var order=new TradeOrder(job.Id,job.Target,job.Quantity,quote.Price,sell,settings.ReserveCoins,quote.TransactionUtc);
        // Account mode is a configuration assumption, never reported as visual
        // verification. New-account restricted sales remain blocked unless facts
        // are actually available. The visible GE must also admit the offer.
        var facts=new ProgressionFacts(Ironman:settings.Ironman);
        return await new TradeRoutine(game,journal).Run(order,facts,ct);
    }
    internal static bool Supplied(TrainingSpec spec,InventoryState inv,IReadOnlyList<PublicOsrsSkill> skills)=>
        Missing(spec,inv,skills) is null&&(spec.Input is not null||inv.EmptySlots>0);
    internal static string? Missing(TrainingSpec spec,InventoryState inv,IReadOnlyList<PublicOsrsSkill> skills)
    {
        if(spec.Tool=="axe"&&!inv.Items.Any(i=>i.Name=="bronze axe"||F2PProgression.Axes.Any(t=>t.Item==i.Name&&skills.Any(s=>s.Name=="Woodcutting"&&s.Level>=t.UseLevel))))return "iron axe";
        if(spec.Tool=="pickaxe"&&!inv.Items.Any(i=>i.Name=="bronze pickaxe"||F2PProgression.Pickaxes.Any(t=>t.Item==i.Name&&skills.Any(s=>s.Name=="Mining"&&s.Level>=t.UseLevel))))return "iron pickaxe";
        if(spec.Tool is not null&&spec.Tool is not ("axe" or "pickaxe")&&inv.Find(spec.Tool) is null)return spec.Tool;
        if(spec.Input is not null&&!(spec.Skill=="Runecraft"&&inv.Count("pure essence") is >0)&&
            (inv.Count(spec.Input) is not int n||n<spec.InputPerOutput))return spec.Input;
        if(spec.SecondInput is not null&&(inv.Count(spec.SecondInput) is not int second||second<1))return spec.SecondInput;
        return null;
    }
    public void Dispose(){prices.Dispose();navigator.Dispose();}
}
