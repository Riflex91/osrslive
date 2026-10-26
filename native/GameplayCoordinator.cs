using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace OSRSLiveDiagnostics;

internal sealed record AgentSettings(bool Ironman=false,long ReserveCoins=500,bool Enabled=true,bool CombatEnabled=true);
internal sealed record AgentJob(string Id,string Kind,string Target,int Quantity=1,long? UnitPrice=null);
internal sealed record SupplyIntent(string Subject,AgentJob Order,string? ActivityId=null);

internal sealed class GameplayCoordinator:IDisposable
{
    private readonly Diagnostics log;
    private readonly WikiPriceService prices=new();
    private readonly WorldNavigator navigator=new();
    private readonly LiveMinimapMotionTracker liveMinimap=new();
    private readonly ActivityScheduler activities=new();
    private readonly string directory;
    private readonly ItemKnowledgeStore itemKnowledge;
    private VisibleGameObservation? subject;
    private DateTimeOffset leaseUntil,nextAttempt;
    private bool uncertain;
    private DateTimeOffset nextInspection;
    private DateTimeOffset nextLiveMotionLog;
    internal string State {get;private set;}="WAITING_FOR_SKILLS";
    internal bool HasSubject=>subject is not null&&DateTimeOffset.UtcNow<leaseUntil;
    internal GameplayCoordinator(Diagnostics log)
    {
        this.log=log;directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSPlay","agent");
        itemKnowledge=new(Path.Combine(directory,"items.sqlite"),message=>log.Add(
            message.StartsWith("ITEM_REFERENCE_READY",StringComparison.Ordinal)?Severity.INFO:Severity.WARN,"ITEM_DATABASE","STATUS",message));
    }
    internal void Reset(){subject=null;uncertain=false;leaseUntil=default;nextAttempt=default;activities.Reset();itemKnowledge.ResetSession();navigator.ClearWorldMapHint();liveMinimap.Reset();Set("WAITING_FOR_SKILLS");}
    internal void ConfirmSubject(VisibleGameObservation confirmed)
    {
        if(subject?.Character!=confirmed.Character)Reset();
        subject=confirmed;leaseUntil=confirmed.ObservedUtc.AddMinutes(4);
    }
    internal async Task<RoutineResult> LocateWithWorldMap(IVisualGameDriver driver,Func<bool> stopped,CancellationToken ct)
    {
        await navigator.Ready(Set,ct);
        if(navigator.LocateDetailed(driver.Current).Location is not null)
            return new(RoutineStatus.Completed,"NATIVE_POSITION_VERIFIED_WORLD_MAP_NOT_NEEDED");
        return await new WorldMapRoutine(driver,navigator,stopped,
            message=>log.Add(Severity.INFO,"WORLD_MAP","OBSERVATION",message)).Run(ct);
    }
    internal bool NativePositionVerified(VisualFrame frame)=>navigator.LocateDetailed(frame).Location is not null;
    // A new activity that first needs travel must not open 17 inventory item
    // context menus merely to discover, again, that its starting tile is
    // unknown. Pending trade/combat journals are reconciled before this gate.
    internal static string? NewActivityPositionBlock(MapLocalization observed)=>
        observed.Location is null?"AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT":null;
    internal void ObserveLiveMinimapMotion(VisualFrame frame)
    {
        var shift=liveMinimap.Observe(frame);
        if(shift is null||DateTimeOffset.UtcNow<nextLiveMotionLog)return;
        nextLiveMotionLog=DateTimeOffset.UtcNow.AddSeconds(5);
        log.Add(Severity.INFO,"NAVIGATION","LIVE_MINIMAP_MOTION",
            $"reason={shift.Reason} pairs={shift.Correspondences} inliers={shift.Inliers} "+
            $"terrain_dx_px={shift.ShiftX?.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)??"unknown"} "+
            $"terrain_dy_px={shift.ShiftY?.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)??"unknown"} "+
            "world_tile_unverified=true no_input=true");
    }
    internal void ClearLiveMinimapMotion()=>liveMinimap.Reset();
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
        if(frame.Pixels?.WorldMapPanelOpen==true)
        {
            var map=WorldMapLocalizer.Locate(navigator.Data!,frame);
            log.Add(Severity.INFO,"WORLD_MAP","OBSERVATION_ONLY",$"reason={map.Reason} features={map.Features} "+
                $"matches={map.Matches} inliers={map.Inliers} surface_x={map.Position?.SurfaceTile.X} surface_y={map.Position?.SurfaceTile.Y} no_input=true");
        }
        var position=navigator.Locate(frame);
        log.Add(Severity.INFO,"NAVIGATION","OBSERVATION_ONLY",$"minimap_identified=true hp={pixels.Hitpoints?.ToString()??"unknown"} "+
            $"position_identified={position is not null} x={position?.Tile.X} y={position?.Tile.Y} plane={position?.Tile.Plane} inliers={position?.Inliers} labels={frame.Labels.Count} no_input=true");
    }
    internal async Task<bool> Tick(VisualFrame frame,VisibleGameObservation? confirmed,
        Func<CancellationToken,Task<VisualFrame?>> capture,
        Func<VisualFrame,GameInput,CancellationToken,Task<bool>> dispatch,CancellationToken stopping)
    {
        if(confirmed is not null)
            ConfirmSubject(confirmed);
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
        var game=new GameInteractions(driver,itemKnowledge,message=>log.Add(Severity.INFO,"ITEM_DATABASE","INVENTORY_READ",message));
        driver.Guard=f=>
        {
            if(f.Pixels?.WorldMapPanelOpen==true)throw new RoutineBlockedException("WORLD_MAP_OBSCURES_GAMEPLAY");
            if(f.Pixels?.Compass is {North:false})throw new RoutineBlockedException("COMPASS_DIRECTION_CHANGED");
        };
        RoutineResult result;
        string? activityId=null;
        bool reconcilingJournal=false;
        int retrySeconds=10;
        try
        {
            Directory.CreateDirectory(directory);
            using var exclusive=new FileStream(Path.Combine(directory,"execution.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject.Character!.ToUpperInvariant())));
            var journal=new TradeJournal(Path.Combine(directory,"pending-trade.json"),hash);
            var combatJournal=new CombatJournal(Path.Combine(directory,"pending-combat.json"),hash);
            SupplyIntent? pendingSupply=null;
            var atlas=navigator;
            int maxHp=subject.Skills.Single(s=>s.Name=="Hitpoints").Level;
            var training=new TrainingRoutines(game);var bank=new BankRoutine(game);
            if(journal.Pending() is PendingTrade pending)
            {
                reconcilingJournal=true;
                Set("RECONCILING_EXISTING_TRADE");result=await new TradeRoutine(game,journal).Resume(budget.Token);
            }
            else if(combatJournal.Pending() is PendingCombat encounter)
            {
                if(uncertain){Set("UNCERTAIN_COMBAT_REQUIRES_REVIEW");return true;}
                reconcilingJournal=true;
                result=await new CombatRoutine(game,combatJournal,atlas,maxHp,Set).Resume(budget.Token);
            }
            else if(uncertain){Set("UNCERTAIN_RESULT_REQUIRES_REVIEW");return true;}
            else if(NewActivityPositionBlock(atlas.LocateDetailed(driver.Current)) is string positionBlock)
            {
                // Read-only preflight: pause new work before expensive inventory
                // scans and right-clicks. Existing pending journals above are
                // deliberately not treated as replaceable new activities.
                result=new(RoutineStatus.Blocked,positionBlock);
            }
            else if((pendingSupply=ReadSupply(hash)) is SupplyIntent supply&&
                activities.Available(supply.ActivityId??"SUPPLY_"+supply.Order.Id,DateTimeOffset.UtcNow))
            {
                activityId=supply.ActivityId??"SUPPLY_"+supply.Order.Id;
                var stock=await game.Inventory(budget.Token);
                if(stock.Count(supply.Order.Target) is int have&&have>=supply.Order.Quantity)
                    result=new(RoutineStatus.Completed,"PENDING_SUPPLY_ALREADY_IN_INVENTORY");
                else result=await Go(game,atlas,maxHp,"ge",budget.Token)??await Trade(game,journal,supply.Order,settings,budget.Token);
                if(result.Status==RoutineStatus.Completed)File.Delete(Path.Combine(directory,"pending-supply.json"));
            }
            else
            {
                Set("READING_INVENTORY_FOR_AUTOMATIC_ACTIVITY");
                await itemKnowledge.EnsureReference(budget.Token);
                var inventory=await game.Inventory(budget.Token);
                // An unsubmitted purchase intent may wait while supplied work
                // proceeds. Never replace it with a second acquisition chain.
                var candidates=ActivityScheduler.Candidates(subject.Skills,inventory,settings.CombatEnabled,
                    allowAcquisition:pendingSupply is null);
                var activity=activities.Select(candidates,DateTimeOffset.UtcNow);
                var products=new HashSet<string>(ExecutionCatalog.Training.Select(s=>s.Product).Where(p=>p.Length>0),StringComparer.OrdinalIgnoreCase);
                if(inventory.EmptySlots<2&&inventory.Items.Any(i=>products.Contains(i.Name)))
                    result=await Go(game,atlas,maxHp,"bank",budget.Token)??await bank.Deposit(products,budget.Token);
                else if(activity?.Combat is CombatSpec combat)
                {
                    activityId=activity.Id;
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
                            await Replenish(game,journal,atlas,maxHp,combatMissing,settings,hash,activity.Id,budget.Token);
                    }
                    else result=await Go(game,atlas,maxHp,combat.Id,budget.Token)??
                            await new CombatRoutine(game,combatJournal,atlas,maxHp,Set).Run(combat,Guid.NewGuid().ToString("N"),subject.Skills,budget.Token);
                }
                else if(activity?.Training is TrainingSpec ready&&activity.Supplied)
                {activityId=activity.Id;Set("TRAINING_"+ready.Id);result=await Go(game,atlas,maxHp,ready.Id,budget.Token)??await training.Run(ready,subject.Skills,budget.Token);}
                else
                {
                    if(inventory.EmptySlots==0&&inventory.Items.Any(i=>products.Contains(i.Name)))
                        result=await Go(game,atlas,maxHp,"bank",budget.Token)??await bank.Deposit(products,budget.Token);
                    else
                    {
                        var next=activity?.Training;
                        string? missing=next is null?null:Missing(next,inventory,subject.Skills);
                        if(missing is null)result=new(RoutineStatus.Blocked,
                            activity is null&&(candidates.Length>0||pendingSupply is not null)?
                            "ACTIVITIES_DEFERRED_WAITING_FOR_RETRY":"NO_SUPPORTED_SUPPLIED_ACTIVITY");
                        else
                        {
                            activityId=activity!.Id;
                            result=await Replenish(game,journal,atlas,maxHp,missing,settings,hash,activity.Id,budget.Token);
                        }
                    }
                }
            }
            uncertain=result.Status==RoutineStatus.UnknownOutcome;
            if(reconcilingJournal)activities.ReconcileJournal(result);
            if(activityId is not null)RecordActivity(activityId,result);
            retrySeconds=result.Reason=="AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT"?60:
                result.Status==RoutineStatus.Progress?1:result.Status==RoutineStatus.Completed?2:10;
            Set(result.Status.ToString().ToUpperInvariant()+"_"+result.Reason);
            if(result.Status==RoutineStatus.Blocked&&result.Reason=="HEALING_REQUIRES_READABLE_LIVING_HEALTH")
                log.Add(Severity.WARN,"GAMEPLAY","HEALTH_EVIDENCE",
                    $"hp_standalone={driver.Current.Pixels?.Hitpoints?.ToString()??"unknown"} "+
                    $"hp_navigation={driver.Current.Pixels?.Navigation?.Hitpoints?.ToString()??"unknown"} max_hp={maxHp} "+
                    $"navigation_available={driver.Current.Pixels?.Navigation is not null} "+
                    $"evidence={driver.Current.Pixels?.HealthEvidence??"unavailable"}");
            log.Add(result.Status is RoutineStatus.Blocked or RoutineStatus.UnknownOutcome?Severity.WARN:Severity.INFO,
                "GAMEPLAY","ROUTINE_RESULT",$"status={result.Status} reason={result.Reason} produced={result.Produced}");
        }
        catch(OperationCanceledException) when(!stopping.IsCancellationRequested)
        {Set("ROUTINE_TIMEOUT_STOPPED");uncertain=true;}
        catch(RoutineBlockedException ex)
        {if(activityId is not null)RecordActivity(activityId,new(RoutineStatus.Blocked,ex.Message));Set("BLOCKED_"+ex.Message);}
        catch(IOException){Set("ANOTHER_AGENT_OR_JOURNAL_IO_BUSY");}
        catch(Exception ex)
        {Set("ROUTINE_ERROR_"+ex.GetType().Name);log.Add(Severity.WARN,"GAMEPLAY","ROUTINE_ERROR","reason="+ex.GetType().Name);uncertain=true;}
        nextAttempt=DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
        return true;
    }
    private void RecordActivity(string id,RoutineResult result)
    {
        if(activities.Record(id,result,DateTimeOffset.UtcNow) is DateTimeOffset retry)
            log.Add(Severity.INFO,"GAMEPLAY","ACTIVITY_DEFERRED",$"activity={id} reason={result.Reason} retry_utc={retry:O}");
    }
    private SupplyIntent? ReadSupply(string hash)
    {
        var supply=Read<SupplyIntent?>("pending-supply.json",null);
        if(supply is not null&&supply.Subject!=hash)
            throw new RoutineBlockedException("SUPPLY_INTENT_BELONGS_TO_OTHER_CHARACTER");
        return supply;
    }
    private async Task<RoutineResult> Replenish(GameInteractions game,TradeJournal journal,WorldNavigator atlas,int hp,
        string item,AgentSettings settings,string hash,string activityId,CancellationToken ct)
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
            try{File.WriteAllText(temp,JsonSerializer.Serialize(new SupplyIntent(hash,order,activityId)));File.Move(temp,path,false);}
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
    public void Dispose(){prices.Dispose();navigator.Dispose();itemKnowledge.Dispose();}
}
