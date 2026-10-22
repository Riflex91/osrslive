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
    internal const string Version = "0.3.0";
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
        var data = Snapshot().Where(x => x.Event == "GAME_TICK").ToArray();
        if (data.Length < 2) return (0,0,0);
        var intervals = data.Zip(data.Skip(1), (a,b) =>
            (b.MonotonicTicks-a.MonotonicTicks)*1000d/Stopwatch.Frequency).ToArray();
        return (intervals.Length, Math.Round(intervals.Average(),2), Math.Round(intervals.Max(),2));
    }
    internal string PlainLog() => string.Join(Environment.NewLine,Snapshot().Select(x => x.Line));
    internal string Report()
    {
        var s=Stats(); var t=TickStats();
        var events=Snapshot(); var simWarnings=events.Count(e=>e.Level==Severity.WARN&&e.Source=="SIMULATOR");
        var proc=Process.GetCurrentProcess();
        var sb = new StringBuilder($"OSRS DIAGNOSTICS | {Version}\n");
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
    private System.Threading.Timer? timer;
    private readonly Random random = new(1103);
    private int ticks, x=3200,y=3200;
    internal bool Running => timer!=null;
    internal LocalSimulation(Diagnostics diagnostics) => this.diagnostics=diagnostics;
    internal void Start()
    {
        if(timer != null) return;
        ticks=0; diagnostics.SetMode("SIMULATION (keine OSRS-Verbindung)");
        diagnostics.Add(Severity.INFO,"SIMULATOR","START","Lokaler Testmodus");
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                var n=Interlocked.Increment(ref ticks);
                x+=random.Next(-1,2);y+=random.Next(-1,2);
                diagnostics.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK",$"tick={n} x={x} y={y} plane=0");
                if(n%7==0) diagnostics.Add(Severity.INFO,"SIMULATOR","NPC_SPAWNED","npc_id=301 near=player");
                if(n%11==0) diagnostics.Add(Severity.INFO,"SIMULATOR","INVENTORY_CHANGED",$"slots_used={Math.Min(28,n/11)}");
                if(n%19==0) diagnostics.Add(Severity.WARN,"SIMULATOR","SIMULATED_DELAY","Künstliche Warnung für Testzwecke; keine reale Verzögerung");
            }
            catch(Exception ex) {diagnostics.Add(Severity.ERROR,"SIMULATOR","TICK_FAILED",ex.GetType().Name);}
        },null,0,600);
    }
    internal void Stop()
    {
        var previous=Interlocked.Exchange(ref timer,null);
        if(previous == null) return;
        previous.Dispose();
        diagnostics.SetMode("NICHT VERBUNDEN");
        diagnostics.Add(Severity.INFO,"SIMULATOR","STOP","Testmodus angehalten");
    }
    public void Dispose()=>Stop();
}
