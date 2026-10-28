using System.Diagnostics;

namespace OSRSLiveDiagnostics;

// The official RuneLite plugin emits a position EVENT on tile changes, not
// a continuous location heartbeat. Read that API event directly: a standing
// character does not make the last event incorrect just because it is old.
// Never use screenshots, OCR, mouse clicks or process-memory inspection.
internal sealed class RuneLiteApiPositionTracker
{
    private RuneLitePositionExportSource.Snapshot? accepted;
    private static readonly TimeSpan StartupWindow=TimeSpan.FromSeconds(10);

    internal void Reset()=>accepted=null;

    internal RuneLitePositionState Observe(RuneLitePositionExportSource.Snapshot? value,
        DateTimeOffset now,string exportDiagnostic)
    {
        if(value is null)
        {
            Reset();
            return new("PLUGIN_EXPORT_UNAVAILABLE",Diagnostic:exportDiagnostic+
                " source=PLUGIN_HUB_EXPORT screen_fallback=false");
        }
        var current=value.Value;
        bool newEvent=false;
        if(current.ExportedAtUtc>now.AddSeconds(2))
        {
            Reset();
            return new("PLUGIN_EXPORT_INVALID",Diagnostic:
                "result=FUTURE_EVENT_REJECTED screen_fallback=false");
        }
        if(accepted is null)
        {
            // An export from an earlier OSRS session is not proof of a
            // currently logged-in player. Wait for a new real API event.
            if(now-current.ExportedAtUtc>StartupWindow)
                return new("API_WAITING_FOR_NEW_EVENT",Diagnostic:
                    $"export_age_s={(now-current.ExportedAtUtc).TotalSeconds:0} "+
                    "previous_session_not_assumed=true move_one_tile_to_sync=true "+
                    "screen_fallback=false");
            accepted=current;
            newEvent=true;
        }
        else if(current.ExportedAtUtc>accepted.Value.ExportedAtUtc)
        {
            accepted=current;
            newEvent=true;
        }
        else if(current.ExportedAtUtc<accepted.Value.ExportedAtUtc||
                current.Position!=accepted.Value.Position)
        {
            Reset();
            return new("PLUGIN_EXPORT_INVALID",Diagnostic:
                "result=EVENT_ROLLBACK_OR_SAME_TIMESTAMP_CONTRADICTION "+
                "screen_fallback=false");
        }

        var age=now-accepted.Value.ExportedAtUtc;
        bool currentEvent=age<=TimeSpan.FromSeconds(3);
        string status=currentEvent?"API_POSITION_UPDATED":"API_POSITION_LAST_KNOWN";
        return new(status,accepted.Value.Position.Tile,accepted.Value.ExportedAtUtc,
            $"source=PLUGIN_HUB_EXPORT event_age_s={Math.Max(0,age.TotalSeconds):0} "+
            $"event_recent={currentEvent.ToString().ToLowerInvariant()} "+
            $"new_event={newEvent.ToString().ToLowerInvariant()} "+
            "position_source=RUNELITE_API_EVENT screen_capture=false ocr_passes=0 "+
            "pixel_gate=false event_driven=true heartbeat_available=false "+
            "last_known_not_liveness_proof=true");
    }
}

internal sealed class RuneLiteApiPositionSource:IDisposable
{
    private readonly RuneLiteApiPositionTracker tracker=new();
    private IntPtr lastWindow;
    private string? lastTitle;
    // Only the previously path-verified, uniquely identified RuneLite HWND.
    // Callers must additionally require a recent accepted API tile event.
    internal IntPtr TrustedWindowHandle=>lastWindow;
    internal string? TrustedWindowTitle=>lastTitle;
    private DateTimeOffset lastExportScanUtc;
    internal RuneLiteExportSummarySource.Result AdditionalExports{get;private set;}=
        new([],"stage=ADDITIONAL_EXPORTS result=NOT_SCANNED redacted=true");
    internal DateTimeOffset LastExportScanUtc=>lastExportScanUtc;

    internal void Reset()
    {
        lastWindow=default;lastTitle=null;tracker.Reset();
        lastExportScanUtc=default;
        AdditionalExports=new([],"stage=ADDITIONAL_EXPORTS result=RESET redacted=true");
    }

    internal Task<RuneLitePositionState> ReadAsync(CancellationToken ct=default,
        Action<string[]>? diagnostic=null,Action<string>? pixelDiagnostic=null)
    {
        ct.ThrowIfCancellationRequested();
        int scanned=0,rejected=0;
        var windows=new List<(IntPtr Handle,string Title)>();
        foreach(var process in Process.GetProcessesByName("RuneLite"))using(process)
        {
            scanned++;
            try
            {
                string expected=Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),"RuneLite","RuneLite.exe");
                if(!process.HasExited&&process.MainWindowHandle!=IntPtr.Zero&&
                    string.Equals(process.MainModule?.FileName,expected,
                        StringComparison.OrdinalIgnoreCase))
                    windows.Add((process.MainWindowHandle,process.MainWindowTitle));
                else rejected++;
            }
            catch(Exception ex) when(ex is InvalidOperationException or
                System.ComponentModel.Win32Exception){rejected++;}
        }
        string processDiagnostic=$"processes={scanned} trusted_windows={windows.Count} rejected={rejected} "+
            "capture_requested=false";
        if(windows.Count!=1)
        {
            Reset();
            return Task.FromResult(new RuneLitePositionState(windows.Count==0?
                "RUNELITE_NOT_RUNNING":"MULTIPLE_RUNELITE_CLIENTS",
                Diagnostic:processDiagnostic));
        }
        try
        {
            var window=windows[0];
            if(lastWindow!=window.Handle||lastTitle!=window.Title)
            {
                tracker.Reset();
                lastExportScanUtc=default;
                AdditionalExports=new([],"stage=ADDITIONAL_EXPORTS result=PROFILE_CHANGED redacted=true");
                lastWindow=window.Handle;lastTitle=window.Title;
            }
            var now=DateTimeOffset.UtcNow;
            if(lastExportScanUtc==default||now-lastExportScanUtc>=TimeSpan.FromSeconds(10))
            {
                lastExportScanUtc=now;
                AdditionalExports=RuneLiteExportSummarySource.Read(now,window.Title);
            }
            var (snapshot,exportDiagnostic)=RuneLitePositionExportSource.Read(now,window.Title);
            pixelDiagnostic?.Invoke(exportDiagnostic);
            var result=tracker.Observe(snapshot,now,exportDiagnostic);
            return Task.FromResult(result with{Diagnostic=processDiagnostic+" "+result.Diagnostic});
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            Reset();
            return Task.FromResult(new RuneLitePositionState("INTERNAL_ERROR",
                Diagnostic:processDiagnostic+" "+ExceptionDiagnostic.Describe(ex)));
        }
    }

    public void Dispose(){}
}
