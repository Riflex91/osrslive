using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal enum Severity { DEBUG, INFO, WARN, ERROR }
internal sealed record LogEvent(long Id, DateTimeOffset Timestamp, long MonotonicTicks,
    Severity Level, string Source, string Event, string Detail)
{
    public string Line => $"{Timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} seq={Id} mono_ticks={MonotonicTicks} [{Level}] [{Source}] {Event}" +
        (string.IsNullOrEmpty(Detail) ? "" : " | " + Detail);
}

internal sealed class Diagnostics
{
    internal const string Version="1.8.25";
    private const int Capacity=10000;
    // Full pixel-grid decisions exceed 480 chars. Do not discard the final
    // full_matches/distinct_positions/result fields from the real file and
    // automatic clipboard export. Keep an explicit large safety bound.
    private const int MaxEventDetailChars=16_384;
    private readonly object sync=new();
    private readonly List<LogEvent> entries=new();
    private long id,dropped,errors,warnings;
    private string mode="LIVE ONLY: OFFIZIELLER JAGEX CLIENT";
    internal event Action? Changed;
    internal event Action<LogEvent>? EntryAdded;
    private static readonly Regex Secret=new(
        @"(?i)(bearer\s+|(?:authorization|token|session|password|passwd|secret|api[_-]?key|cookie)\s*[:=]\s*)([^\s,;]+)",
        RegexOptions.Compiled);
    private static readonly Regex Email=new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
        RegexOptions.IgnoreCase|RegexOptions.Compiled);
    internal string Mode {get{lock(sync)return mode;}}
    internal void SetMode(string value){lock(sync)mode=value;Notify();}
    private void Notify(){try{Changed?.Invoke();}catch{}}
    internal static string Sanitize(string? input,int max=480)
    {
        var result=(input??"").Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
        result=Secret.Replace(result,"$1[REDACTED]");
        result=Email.Replace(result,"[REDACTED_EMAIL]");
        result=new string(result.Select(c=>char.IsControl(c)?'?':c).ToArray());
        return result.Length>max?result[..max]+$"...[gekürzt: {result.Length-max} Zeichen ausgelassen]":result;
    }
    internal void Add(Severity level,string source,string kind,string details)
    {
        LogEvent added;
        lock(sync)
        {
            if(entries.Count>=Capacity){entries.RemoveAt(0);dropped++;}
            added=new LogEvent(++id,DateTimeOffset.Now,
                Stopwatch.GetTimestamp(),level,Sanitize(source,32),
                Sanitize(kind,55),Sanitize(details,MaxEventDetailChars));
            entries.Add(added);
            if(level==Severity.ERROR)errors++;
            if(level==Severity.WARN)warnings++;
            // Serialise file subscribers under the same lock as sequence IDs:
            // concurrent capture/observer threads must not invert log order.
            try{EntryAdded?.Invoke(added);}catch{} // Storage cannot interrupt gameplay.
        }
        Notify();
    }
    internal LogEvent[] Snapshot(){lock(sync)return entries.ToArray();}
    internal (long Events,long Dropped,long Errors,long Warnings) Stats()
    {
        lock(sync)return (entries.Count,dropped,errors,warnings);
    }
    internal void Clear()
    {
        lock(sync){entries.Clear();dropped=errors=warnings=0;}
        Add(Severity.INFO,"CORE","LOG_CLEARED","Logpuffer geleert");
    }
    internal string PlainLog()=>string.Join(Environment.NewLine,Snapshot().Select(x=>x.Line));
    internal string Report()
    {
        var counters=Stats();
        using var proc=Process.GetCurrentProcess();
        var sb=new StringBuilder($"OSRS PLAY | {Version} | LIVE ONLY\n");
        sb.AppendLine("Modus: "+Mode);
        sb.AppendLine($"Events: {counters.Events} | Warnungen: {counters.Warnings} | Fehler: {counters.Errors}");
        sb.AppendLine($"Puffer-Überlauf: {counters.Dropped} | RAM: {proc.WorkingSet64/1024/1024} MiB");
        sb.AppendLine("Offizieller Launcher bleibt im Originalfenster; osclient.exe wird in die Spielansicht eingebettet.");
        sb.AppendLine("Serverseitige Authentifizierung: NICHT verifiziert. Skilldaten benötigen wiederholte, konsistente OCR-Lesungen.");
        sb.AppendLine("Hintergrundaufnahme: Windows Graphics Capture; Eingaben: gezielte Fensternachrichten; F8 pausiert.");
        sb.AppendLine("Hintergrundtransport im Testfenster geprüft; Annahme durch osclient.exe noch nicht bestätigt. Minimiert pausiert die Aufnahme.");
        sb.AppendLine("Spielansicht: fest 1000x600 Pixel; Log dockt darunter an und verkleinert das Spiel nicht.");
        sb.AppendLine("Routinen: Sammeln, Produktion, Bank, GE, Hühnerkampf und automatische F2P-Kartennavigation implementiert; Live-Ende-zu-Ende noch nicht verifiziert.");
        sb.AppendLine("Navigation: automatische Minimap-Erkennung/Bildzuordnung, öffentliche Kollisionskarte, A*, Treppen und begrenzte Tür-/Torbehandlung; keine aufgezeichneten Benutzerwege.");
        sb.AppendLine("Keine Offline-Sitzungen, Sandbox-Welt oder künstliche Game-Ticks.");
        sb.AppendLine("--- LOG ---");
        foreach(var ev in Snapshot())
        {
            sb.AppendLine(ev.Line);
        }
        return sb.ToString();
    }
    internal string Json()
    {
        var s=Stats();
        using var proc=Process.GetCurrentProcess();
        return JsonSerializer.Serialize(new
        {
            schemaVersion=4,clientVersion=Version,exportedAt=DateTimeOffset.UtcNow,
            mode=Mode,executionMode="OFFICIAL_WINDOW_WGC",inputTransport="WINDOW_MESSAGES",
            backgroundTransportImplemented=true,officialBackgroundInputVerified=false,
            liveAgentAvailable=false,visualActivityRoutinesImplemented=true,officialExecutionEndToEndVerified=false,verifiedOfficialGameState=false,
            officialAuthenticationHandledBy="JAGEX_LAUNCHER",
            statistics=new{s.Events,s.Dropped,s.Errors,s.Warnings,
                processWorkingSetBytes=proc.WorkingSet64},
            events=Snapshot().Select(e=>new{e.Id,e.Timestamp,
                level=e.Level.ToString(),e.Source,e.Event,e.Detail})
        },new JsonSerializerOptions{WriteIndented=true});
    }
}
