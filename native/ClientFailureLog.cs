using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OSRSLiveDiagnostics;

// An append-only local session transcript, independent of the bounded GUI
// preview. Failures copy the actual UTF-8 file, not a truncated text box.
internal sealed class ClientFailureLog : IDisposable
{
    private static readonly UTF8Encoding Utf8=new(false);
    private readonly Diagnostics diagnostics;
    private readonly Action<string> clipboard;
    private readonly object fileSync=new();
    private readonly HashSet<string> reported=new(StringComparer.Ordinal);
    private bool disposed;

    internal string FilePath { get; }
    internal string? LastFailureCode { get; private set; }
    internal string ClipboardStatus { get; private set; }="NOT_COPIED";
    internal string StorageStatus { get; private set; }="NOT_READY";
    internal bool HasFailure=>LastFailureCode is not null;

    internal static string SessionPath(string backend)
    {
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OSRSPlay","live-logs");
        return Path.Combine(directory,$"{backend}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    }

    internal ClientFailureLog(Diagnostics diagnostics,string filePath,Action<string>? clipboard=null)
    {
        this.diagnostics=diagnostics;
        FilePath=filePath;
        this.clipboard=clipboard??(content=>Clipboard.SetDataObject(content,true,3,80));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath,
                $"OSRS PLAY {Diagnostics.Version} | session_start={DateTimeOffset.Now:O}{Environment.NewLine}"+
                $"build={BuildIdentity.Log} monotonic_frequency_hz={Stopwatch.Frequency}{Environment.NewLine}"+
                "Chronologisches lokales Diagnoseprotokoll; kein Prozessspeicher, keine Screenshots oder Zugangsdaten."+
                Environment.NewLine+"--- EVENTS ---"+Environment.NewLine,Utf8);
            StorageStatus="READY";
        }
        catch(Exception ex) when(IsStorageError(ex)){StorageStatus="ERROR_"+ex.GetType().Name;}
        diagnostics.EntryAdded+=Append;
    }

    private static bool IsStorageError(Exception ex)=>ex is IOException or UnauthorizedAccessException or
        System.Security.SecurityException or ArgumentException;

    private void Append(LogEvent ev)
    {
        if(disposed)return;
        try
        {
            lock(fileSync)File.AppendAllText(FilePath,ev.Line+Environment.NewLine,Utf8);
            StorageStatus="READY";
        }
        catch(Exception ex) when(IsStorageError(ex)){StorageStatus="ERROR_"+ex.GetType().Name;}
    }

    internal bool ReportFailure(string source,string code,string details)
    {
        // A persistent BLOCKED state may be seen thousands of times per hour.
        // Each distinct cause gets one automatic copy, not clipboard thrashing.
        string key=Diagnostics.Sanitize(source,32)+"/"+Diagnostics.Sanitize(code,55);
        if(!reported.Add(key))return false;
        LastFailureCode=key;
        diagnostics.Add(Severity.ERROR,"FAILURE_REPORT","IMPROVEMENT_REQUIRED",
            $"source={source} cause={code} details={details} action=NOT_EXECUTED");
        CopyToClipboard("AUTO_FAILURE");
        return true;
    }

    internal bool CopyToClipboard(string origin="MANUAL")
    {
        diagnostics.Add(Severity.INFO,"CLIPBOARD","COPY_REQUEST",
            $"origin={origin} complete_session_file=true file={Path.GetFileName(FilePath)}");
        if(StorageStatus!="READY")
        {
            ClipboardStatus="FAILED_STORAGE_"+StorageStatus;
            return false; // Never present a stale/incomplete file as the complete log.
        }
        try
        {
            string content;
            lock(fileSync)content=File.ReadAllText(FilePath,Utf8);
            clipboard(content);
            ClipboardStatus="COPIED";
            return true;
        }
        catch(Exception ex) when(IsStorageError(ex)||ex is ExternalException or InvalidOperationException)
        {
            ClipboardStatus="FAILED_"+ex.GetType().Name;
            diagnostics.Add(Severity.WARN,"CLIPBOARD","COPY_FAILED",
                $"origin={origin} reason={ex.GetType().Name} retry_available=true");
            return false;
        }
    }

    public void Dispose()
    {
        if(disposed)return;
        diagnostics.EntryAdded-=Append;
        disposed=true;
    }
}

internal static class FailureLogPolicy
{
    internal static bool RuneLiteBlocked(string status)=>status is
        "GRID_INFO_NOT_READABLE" or "STALE_FRAME" or "RUNELITE_NOT_RUNNING" or
        "CAPTURE_UNAVAILABLE" or "OCR_LANGUAGE_UNAVAILABLE" or
        "MULTIPLE_RUNELITE_CLIENTS" or "INTERNAL_ERROR";

    internal static bool LegacyFailure(LogEvent ev)
    {
        if(ev.Source is "FAILURE_REPORT" or "CLIPBOARD")return false;
        if(ev.Level==Severity.ERROR)return true;
        return ev.Level==Severity.WARN &&
            (ev.Event.Contains("FAILED",StringComparison.OrdinalIgnoreCase)||
             ev.Event.Contains("BLOCKED",StringComparison.OrdinalIgnoreCase)||
             ev.Detail.Contains("BLOCKED_",StringComparison.OrdinalIgnoreCase));
    }

    internal static void SelfTest()
    {
        var d=new Diagnostics();string? copied=null;
        string file=Path.Combine(Path.GetTempPath(),"osrs-failure-selftest-"+Guid.NewGuid().ToString("N")+".log");
        try
        {
            using var report=new ClientFailureLog(d,file,value=>copied=value);
            d.Add(Severity.DEBUG,"RUNELITE_OCR","PASS","Tile 3142» 3212» o");
            d.Add(Severity.DEBUG,"PRIVACY","REDACTION","password=secret token=abcdef user=test@example.com");
            if(!report.ReportFailure("RUNELITE","GRID_INFO_NOT_READABLE","position_rejected=true"))
                throw new Exception("First failure must trigger an automatic log copy");
            if(copied is null||copied!=File.ReadAllText(file,Encoding.UTF8)||!copied.Contains("Tile 3142» 3212» o")||
               copied.Contains("password=secret")||copied.Contains("token=abcdef")||copied.Contains("test@example.com"))
                throw new Exception("Clipboard must receive the complete real session log");
            if(report.ReportFailure("RUNELITE","GRID_INFO_NOT_READABLE","same_block=true"))
                throw new Exception("Persistent failures must not overwrite the clipboard repeatedly");
            d.Add(Severity.INFO,"RUNELITE","NEXT_SAMPLE","late_sample=true");
            if(!report.CopyToClipboard()||copied is null||copied!=File.ReadAllText(file,Encoding.UTF8)||
                !copied.Contains("late_sample=true"))
                throw new Exception("Manual recopy must include the latest appended events");
        }
        finally{if(File.Exists(file))File.Delete(file);}
        string failedFile=Path.Combine(Path.GetTempPath(),"osrs-clipboard-selftest-"+Guid.NewGuid().ToString("N")+".log");
        try
        {
            using var failed=new ClientFailureLog(new Diagnostics(),failedFile,
                _=>throw new ExternalException("busy"));
            if(failed.ReportFailure("TEST","FAILED_ACTION","action_not_executed=true")==false||
               failed.ClipboardStatus!="FAILED_ExternalException")
                throw new Exception("Busy clipboard must be reported without crashing the client");
        }
        finally{if(File.Exists(failedFile))File.Delete(failedFile);}
        if(!FailureLogPolicy.RuneLiteBlocked("GRID_INFO_NOT_READABLE")||
           FailureLogPolicy.RuneLiteBlocked("WAITING_FOR_SECOND_FRAME")||
           FailureLogPolicy.RuneLiteBlocked("PAUSED")||
           !FailureLogPolicy.LegacyFailure(new(1,DateTimeOffset.Now,0,Severity.WARN,"NAV","BLOCKED_MAP",""))||
           FailureLogPolicy.LegacyFailure(new(2,DateTimeOffset.Now,0,Severity.INFO,"CORE","STARTED","")))
            throw new Exception("Failure classification does not distinguish transient/intentional states");
    }
}

internal static class ExceptionDiagnostic
{
    // Method/IL trace is actionable but does not include local source paths,
    // raw exception payloads or arguments (which could hold account data).
    internal static string Describe(Exception ex)
    {
        var frames=new StackTrace(ex,false).GetFrames()?.Take(7)
            .Select(f=>$"{f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name}@{f.GetILOffset()}")
            .ToArray()??Array.Empty<string>();
        return $"exception={ex.GetType().Name} hresult=0x{ex.HResult:X8} "+
            $"message={Diagnostics.Sanitize(ex.Message,120)} stack={string.Join(">",frames)} "+
            $"inner={ex.InnerException?.GetType().Name??"none"}";
    }
}
