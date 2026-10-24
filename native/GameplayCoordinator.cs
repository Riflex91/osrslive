using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace OSRSLiveDiagnostics;

internal sealed record AgentSettings(bool Ironman=false,long ReserveCoins=500,bool Enabled=true);
internal sealed record AgentJob(string Id,string Kind,string Target,int Quantity=1,long? UnitPrice=null);

internal sealed class GameplayCoordinator:IDisposable
{
    private readonly Diagnostics log;
    private readonly WikiPriceService prices=new();
    private readonly string directory;
    private VisibleGameObservation? subject;
    private DateTimeOffset leaseUntil,nextAttempt;
    private bool uncertain;
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
    internal async Task<bool> Tick(VisualFrame frame,VisibleGameObservation? confirmed,
        Func<CancellationToken,Task<VisualFrame?>> capture,
        Func<VisualFrame,GameInput,CancellationToken,Task<bool>> dispatch,CancellationToken stopping)
    {
        if(confirmed is not null)
        {
            if(subject?.Character!=confirmed.Character)Reset();
            subject=confirmed;leaseUntil=confirmed.ObservedUtc.AddMinutes(2);
        }
        var current=GameScreenReader.Read(frame);
        if(current.Character is not null&&subject is not null&&current.Character!=subject.Character){Reset();return false;}
        if(!HasSubject||subject?.Skills is null)return false;
        if(DateTimeOffset.UtcNow<nextAttempt)return true;
        var settings=Read("settings.json",new AgentSettings());
        if(!settings.Enabled){Set("DISABLED_IN_SETTINGS");return true;}
        if(settings.ReserveCoins<0)throw new RoutineBlockedException("NEGATIVE_RESERVE");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(stopping);
        budget.CancelAfter(TimeSpan.FromSeconds(90));
        var driver=new WindowVisualGameDriver(frame,capture,async (f,input,ct)=>HasSubject&&await dispatch(f,input,ct),
            action=>Set("INPUT_"+action.ToUpperInvariant()));
        var game=new GameInteractions(driver);
        RoutineResult result;
        string? queuedId=null;
        try
        {
            Directory.CreateDirectory(directory);
            using var exclusive=new FileStream(Path.Combine(directory,"execution.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var queued=Read<AgentJob?>("job.json",null);queuedId=queued?.Id;
            string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject.Character!.ToUpperInvariant())));
            var journal=new TradeJournal(Path.Combine(directory,"pending-trade.json"),hash);
            var training=new TrainingRoutines(game);var bank=new BankRoutine(game);
            if(journal.Pending() is PendingTrade pending)
            {
                if(queuedId!=pending.Order.Id)queuedId=null;
                Set("RECONCILING_EXISTING_TRADE");result=await new TradeRoutine(game,journal).Resume(budget.Token);
            }
            else if(uncertain){Set("UNCERTAIN_RESULT_REQUIRES_REVIEW");return true;}
            else if(queued is not null)
            {
                if(queued.Quantity is <1 or >1000||string.IsNullOrWhiteSpace(queued.Id))throw new RoutineBlockedException("JOB_INVALID");
                Set("JOB_"+queued.Kind.ToUpperInvariant());
                result=queued.Kind switch
                {
                    "train" when queued.Quantity==1=>await training.Run(ExecutionCatalog.Training.Single(s=>s.Id==queued.Target),subject.Skills,budget.Token),
                    "train"=>new(RoutineStatus.Blocked,"TRAINING_JOB_IS_ONE_VERIFIED_STEP"),
                    "craft"=>await training.Craft(F2PProgression.Recipes.Single(r=>r.Product==queued.Target),queued.Quantity,subject.Skills,budget.Token),
                    "withdraw"=>await bank.Withdraw(queued.Target,queued.Quantity,budget.Token),
                    "deposit"=>await bank.Deposit(new HashSet<string>(StringComparer.OrdinalIgnoreCase){queued.Target},budget.Token),
                    "buy" or "sell"=>await Trade(game,journal,queued,settings,budget.Token),
                    _=>new(RoutineStatus.Blocked,"UNKNOWN_JOB_KIND")
                };
            }
            else
            {
                Set("READING_INVENTORY_FOR_AUTOMATIC_ACTIVITY");
                var inventory=await game.Inventory(budget.Token);
                var candidates=ExecutionCatalog.Training.Where(spec=>F2PProgression.Methods.Any(m=>m.Id==spec.Id&&
                    subject.Skills.Any(s=>s.Name==m.Skill&&s.Level>=m.MinimumLevel&&s.Level<m.NextLevel)))
                    .OrderBy(spec=>subject.Skills.Single(s=>s.Name==spec.Skill).Level).ToArray();
                var ready=candidates.FirstOrDefault(s=>Supplied(s,inventory,subject.Skills));
                if(ready is not null)
                {Set("TRAINING_"+ready.Id);result=await training.Run(ready,subject.Skills,budget.Token);}
                else
                {
                    var products=new HashSet<string>(ExecutionCatalog.Training.Select(s=>s.Product).Where(p=>p.Length>0),StringComparer.OrdinalIgnoreCase);
                    if(inventory.EmptySlots==0&&inventory.Items.Any(i=>products.Contains(i.Name)))
                        result=await bank.Deposit(products,budget.Token);
                    else
                    {
                        var next=candidates.FirstOrDefault();
                        string? missing=next is null?null:Missing(next,inventory,subject.Skills);
                        if(missing is null)result=new(RoutineStatus.Blocked,"NO_SUPPORTED_SUPPLIED_ACTIVITY");
                        else
                        {
                            Set("REPLENISHING_"+missing.Replace(' ','_').ToUpperInvariant());
                            result=await bank.Withdraw(missing,1,budget.Token);
                            // Only an explicit empty *search* is proof that the
                            // bank lacks the item. A hidden page is not.
                            if(result.Reason=="VERIFIED_BANK_SEARCH_EMPTY"&&!settings.Ironman)
                                result=await Trade(game,journal,new(Guid.NewGuid().ToString("N"),"buy",missing),settings,budget.Token);
                        }
                    }
                }
            }
            uncertain=result.Status==RoutineStatus.UnknownOutcome;
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
        nextAttempt=DateTimeOffset.UtcNow.AddSeconds(10);
        return true;
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
    public void Dispose()=>prices.Dispose();
}
