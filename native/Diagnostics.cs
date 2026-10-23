using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal enum Severity { DEBUG, INFO, WARN, ERROR }
internal sealed record LogEvent(long Id, DateTimeOffset Timestamp, long MonotonicTicks,
    Severity Level, string Source, string Event, string Detail)
{
    public string Line => $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{Level}] [{Source}] {Event}" +
                          (string.IsNullOrEmpty(Detail) ? "" : " | " + Detail);
}

internal sealed class Diagnostics
{
    internal const string Version = "0.6.1";
    private const int Capacity = 10000;
    private readonly object sync = new();
    private readonly List<LogEvent> entries = new();
    private long id;
    private long dropped, errors, warnings, ticks, externalEvents;
    private bool rendering = true;
    private string mode = "NICHT VERBUNDEN";
    internal event Action? Changed;
    private static readonly Regex Secret = new(@"(?i)(bearer\s+|(?:authorization|token|session|password|passwd|secret|api[_-]?key|cookie)\s*[:=]\s*)([^\s,;]+)", RegexOptions.Compiled);
    private static readonly Regex Email = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal string Mode { get { lock(sync) return mode; } }
    internal bool Rendering { get { lock(sync) return rendering; } }
    internal void SetMode(string value) { lock(sync) mode = value; Changed?.Invoke(); }
    internal void SetRendering(bool enabled)
    {
        lock(sync) rendering = enabled;
        Add(Severity.INFO, "RENDERER", enabled ? "RENDER_ON" : "RENDER_OFF", "Spielzustandsverarbeitung bleibt aktiv");
    }
    internal static string Sanitize(string? input, int max = 480)
    {
        var result = (input ?? "").Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
        result = Secret.Replace(result, "$1[REDACTED]");
        result = Email.Replace(result, "[REDACTED_EMAIL]");
        result = new string(result.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return result.Length > max ? result[..max] + "...[gekürzt]" : result;
    }
    internal void Add(Severity severity, string source, string kind, string details)
    {
        lock(sync)
        {
            if (entries.Count >= Capacity) { entries.RemoveAt(0); dropped++; }
            var ev = new LogEvent(++id, DateTimeOffset.Now, Stopwatch.GetTimestamp(), severity,
                Sanitize(source, 32), Sanitize(kind, 55), Sanitize(details));
            entries.Add(ev);
            if (severity == Severity.ERROR) errors++;
            if (severity == Severity.WARN) warnings++;
            if (kind == "GAME_TICK") ticks++;
            if (source == "EXTERNAL") externalEvents++;
        }
        try { Changed?.Invoke(); } catch (Exception ex)
        {
            lock(sync)
            {
                if (entries.Count >= Capacity) { entries.RemoveAt(0); dropped++; }
                entries.Add(new LogEvent(++id, DateTimeOffset.Now, Stopwatch.GetTimestamp(), Severity.ERROR,
                    "CORE", "LISTENER_FAILED", Sanitize(ex.GetType().Name)));
                errors++;
            }
        }
    }
    internal LogEvent[] Snapshot() { lock(sync) return entries.ToArray(); }
    internal (long Events,long Dropped,long Errors,long Warnings,long Ticks,long External) Stats()
    {
        lock(sync) return (entries.Count,dropped,errors,warnings,ticks,externalEvents);
    }
    internal void Clear()
    {
        lock(sync) { entries.Clear(); dropped=errors=warnings=ticks=externalEvents=0; }
        Add(Severity.INFO,"CORE","LOG_CLEARED","Diagnosepuffer geleert");
    }
    internal (int Count, double AverageMs, double MaxMs) TickStats()
    {
        // A bot stop/restart is not an OSRS tick delay. Session boundaries are
        // explicit and never contribute to the reported monotonic intervals.
        var values=new List<double>();
        LogEvent? previous=null;
        foreach(var item in Snapshot())
        {
            if(item.Source=="SIMULATOR" && item.Event is "START" or "STOP")
            {
                previous=null;
                continue;
            }
            if(item.Event!="GAME_TICK" || item.Source!="SIMULATOR")continue;
            if(previous is not null && item.MonotonicTicks>previous.MonotonicTicks)
            {
                values.Add((item.MonotonicTicks-previous.MonotonicTicks)*1000d/Stopwatch.Frequency);
            }
            previous=item;
        }
        return values.Count==0?(0,0,0):
            (values.Count,Math.Round(values.Average(),2),Math.Round(values.Max(),2));
    }
    internal string PlainLog() => string.Join(Environment.NewLine,Snapshot().Select(x => x.Line));
    internal string Report()
    {
        var s=Stats(); var t=TickStats();
        var events=Snapshot(); var simWarnings=events.Count(e=>e.Level==Severity.WARN&&e.Source=="SIMULATOR");
        var proc=Process.GetCurrentProcess();
        var sb = new StringBuilder($"OSRS PLAY | {Version}\n");
        sb.AppendLine($"Modus: {Mode} | Renderer: {(Rendering ? "AN":"AUS")}");
        sb.AppendLine($"Events: {s.Events} | Ticks: {s.Ticks} | Warnungen: {s.Warnings} | Fehler: {s.Errors} | Externe Events: {s.External}");
        sb.AppendLine($"Puffer-Überlauf: {s.Dropped} | RAM: {proc.WorkingSet64/1024/1024} MiB | Threads: {proc.Threads.Count}");
        sb.AppendLine($"Tick-Abstände (monoton): Ø {t.AverageMs:0.##} ms, Max {t.MaxMs:0.##} ms, Intervalle {t.Count}");
        sb.AppendLine($"Künstliche Simulatorwarnungen: {simWarnings}; gemeldete andere Warnungen: {s.Warnings-simWarnings}");
        sb.AppendLine("Keine RuneLite- oder Jagex-Verbindung; Daten stammen nur aus dem lokalen Simulator.");
        sb.AppendLine("--- LOG ---");
        foreach(var entry in events.TakeLast(1800))
        {
            if(sb.Length+entry.Line.Length>190000) { sb.AppendLine("... gekürzt; JSON-/ZIP-Export verwenden"); break; }
            sb.AppendLine(entry.Line);
        }
        return sb.ToString();
    }
    internal string Json()
    {
        var s=Stats(); var t=TickStats();
        return JsonSerializer.Serialize(new {
            schemaVersion=2, clientVersion=Version, exportedAt=DateTimeOffset.UtcNow,
            mode=Mode, rendering=Rendering, simulationOnly=true,
            statistics = new {s.Events,s.Dropped,s.Errors,s.Warnings,s.Ticks,s.External,
                processWorkingSetBytes=Process.GetCurrentProcess().WorkingSet64,
                t.Count,t.AverageMs,t.MaxMs},
            events=Snapshot().Select(e=>new {e.Id,e.Timestamp,level=e.Level.ToString(),e.Source,e.Event,e.Detail})
        },new JsonSerializerOptions {WriteIndented=true});
    }
}

internal sealed class LocalSimulation : IDisposable
{
    private readonly Diagnostics diagnostics;
    private readonly object sync=new();
    private System.Threading.Timer? timer;
    private readonly Random random=new(1103);
    private int ticks,x=3200,y=3200,sessionNumber;
    private long generation;
    // World simulation is the single source of truth: no separate agent inventory.
    private int inventoryUsed,hitpoints=100;
    private bool agentEnabled;
    private OfflineAgent planner=new();
    private AgentPlan? pending;
    private AgentSnapshot? pendingBefore;

    internal bool Running {get {lock(sync)return timer!=null;}}
    internal bool AgentEnabled {get {lock(sync)return agentEnabled;}}
    internal LocalSimulation(Diagnostics diagnostics)=>this.diagnostics=diagnostics;

    internal void SetAgentEnabled(bool enabled)
    {
        lock(sync)
        {
            if(enabled && timer==null)throw new InvalidOperationException("Start simulation first.");
            if(enabled)
            {
                planner=new OfflineAgent();
                pending=null;pendingBefore=null;
                // Preserve real simulation inventory; only reset planner state.
            }
            else
            {
                if(pending is not null)
                    diagnostics.Add(Severity.INFO,"OFFLINE_AGENT","ACTION_CANCELLED",
                        $"id={pending.Id} action={pending.Action} reason=AGENT_DISABLED");
                pending=null;pendingBefore=null;
            }
            agentEnabled=enabled;
        }
        diagnostics.Add(Severity.INFO,"OFFLINE_AGENT",enabled?"ENABLED":"DISABLED",
            "Nur lokaler Simulator; kein OSRS-Zugriff. Eine gemeinsame Weltzustandsquelle.");
    }

    private AgentSnapshot Observe(int tick)=>
        new(tick,DateTimeOffset.UtcNow,AgentSource.Simulation,hitpoints,inventoryUsed,28);

    // Simulated environment resolves an action only on a later game tick.
    // Every 17th action is intentionally dropped to exercise timeout recovery.
    private bool ResolvePending(int tick)
    {
        if(pending is null || pendingBefore is null)return true;
        if(tick<=pending.Tick)return false;
        var plan=pending;
        var before=pendingBefore;
        if(plan.Id%17==0)
        {
            if(tick-plan.Tick<3)
            {
                diagnostics.Add(Severity.DEBUG,"OFFLINE_AGENT","ACTION_PENDING",
                    $"id={plan.Id} tick={tick} simulated_ack_drop=true");
                return false;
            }
            diagnostics.Add(Severity.WARN,"OFFLINE_AGENT","ACTION_TIMED_OUT",
                $"id={plan.Id} action={plan.Action} synthetic_fault=true no_state_change");
            pending=null;pendingBefore=null;
            return true;
        }
        var simulated=OfflineAgent.Simulate(plan,before);
        bool inventoryChanged=inventoryUsed!=simulated.InventoryUsed;
        bool healthChanged=hitpoints!=simulated.Hitpoints;
        inventoryUsed=simulated.InventoryUsed;
        hitpoints=simulated.Hitpoints;
        var outcome=OfflineAgent.Confirm(plan,before,Observe(tick));
        diagnostics.Add(outcome.Phase==ActionPhase.Confirmed?Severity.INFO:Severity.ERROR,
            "OFFLINE_AGENT",outcome.Phase==ActionPhase.Confirmed?"ACTION_CONFIRMED":"ACTION_FAILED",
            $"id={outcome.Id} planned_tick={plan.Tick} confirmed_tick={tick} action={plan.Action} reason={outcome.Reason} hp={hitpoints} inventory={inventoryUsed}/28");
        if(inventoryChanged)
            diagnostics.Add(Severity.INFO,"SIMULATOR","INVENTORY_CHANGED",
                $"slots_used={inventoryUsed} capacity=28 source=SHARED_WORLD");
        if(healthChanged)
            diagnostics.Add(Severity.INFO,"SIMULATOR","HEALTH_CHANGED",
                $"hp={hitpoints} source=SHARED_WORLD");
        pending=null;pendingBefore=null;
        return true;
    }

    private void AgentTick(int tick)
    {
        lock(sync)
        {
            if(!agentEnabled)return;
            if(!ResolvePending(tick))return;
            if(tick%23==0)
            {
                hitpoints=15;
                diagnostics.Add(Severity.INFO,"SIMULATOR","HEALTH_CHANGED",
                    $"hp={hitpoints} reason=SYNTHETIC_DAMAGE source=SHARED_WORLD");
            }
            var snapshot=Observe(tick);
            var decision=planner.Plan(snapshot,DateTimeOffset.UtcNow);
            if(decision.Phase==ActionPhase.Blocked)
            {
                diagnostics.Add(Severity.WARN,"OFFLINE_AGENT","PLAN_BLOCKED",
                    $"id={decision.Id} tick={tick} reason={decision.Reason}");
                return;
            }
            pending=decision;pendingBefore=snapshot;
            diagnostics.Add(Severity.DEBUG,"OFFLINE_AGENT","ACTION_PLANNED",
                $"id={decision.Id} requested_tick={tick} action={decision.Action} reason={decision.Reason} hp={hitpoints} inventory={inventoryUsed}/28");
        }
    }

    internal void Start()
    {
        lock(sync)
        {
            if(timer is not null)return;
            ticks=0;x=3200;y=3200;
            hitpoints=100;inventoryUsed=0;agentEnabled=false;
            pending=null;pendingBefore=null;planner=new OfflineAgent();
            int run_id=++sessionNumber;
            long token=++generation;
            diagnostics.SetMode("SIMULATION (keine OSRS-Verbindung)");
            diagnostics.Add(Severity.INFO,"SIMULATOR","START",
                $"run_id={session} Lokaler Testmodus; gemeinsamer Inventarzustand");
            // Schedule only AFTER publishing the Timer field, avoiding a race on tick #1.
            timer=new System.Threading.Timer(_=>
            {
                lock(sync)
                {
                    // A callback that was already queued by a prior session cannot
                    // mutate or log ticks after stop/restart.
                    if(token!=generation || timer is null)return;
                    try
                    {
                        int tick=++ticks;
                        x+=random.Next(-1,2);y+=random.Next(-1,2);
                        diagnostics.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK",
                            $"run_id={session} tick={tick} x={x} y={y} plane=0");
                        if(tick%7==0)
                            diagnostics.Add(Severity.INFO,"SIMULATOR","NPC_SPAWNED",
                                "npc_id=301 near=player");
                        if(tick%19==0)
                            diagnostics.Add(Severity.WARN,"SIMULATOR","SIMULATED_DELAY",
                                "Künstliche Warnung für Testzwecke; keine reale Verzögerung");
                        AgentTick(tick);
                    }
                    catch(Exception ex)
                    {
                        diagnostics.Add(Severity.ERROR,"SIMULATOR","TICK_FAILED",
                            ex.GetType().Name);
                    }
                }
            },null,Timeout.Infinite,600);
            timer.Change(0,600);
        }
    }

    internal void Stop()
    {
        System.Threading.Timer? previous;
        lock(sync)
        {
            previous=timer;
            if(previous is null)return;
            // Invalidate all callbacks before releasing the simulation lock.
            generation++;
            timer=null;agentEnabled=false;
            if(pending is not null)
                diagnostics.Add(Severity.INFO,"OFFLINE_AGENT","ACTION_CANCELLED",
                    $"id={pending.Id} action={pending.Action} planned_tick={pending.Tick} "+
                    $"run_id={sessionNumber} reason=BOT_STOPPED");
            pending=null;pendingBefore=null;
            diagnostics.SetMode("NICHT VERBUNDEN");
            diagnostics.Add(Severity.INFO,"SIMULATOR","STOP",
                $"run_id={sessionNumber} Testmodus angehalten");
        }
        previous.Dispose();
    }

    public void Dispose()=>Stop();
}
