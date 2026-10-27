using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal sealed class RuneLiteWindow:Form
{
    private readonly RuneLitePositionSource source=new();
    private readonly Diagnostics log=new();
    private readonly ClientFailureLog failureLog;
    private readonly Label status=new(),position=new(),detail=new(),failureBanner=new();
    private readonly TextBox logPreview=new();
    private readonly Button copyLog=new();
    private readonly System.Windows.Forms.Timer timer=new(){Interval=800};
    private readonly System.Windows.Forms.Timer healthTimer=new(){Interval=10000};
    private readonly Stopwatch uptime=Stopwatch.StartNew();
    private readonly CancellationTokenSource closing=new();
    private bool paused,busy;
    private long generation,samples;
    private string? lastStatus;
    private DateTimeOffset blockedSince;

    internal RuneLiteWindow()
    {
        failureLog=new ClientFailureLog(log,ClientFailureLog.SessionPath("runelite"));
        log.SetMode("RUNELITE_COMPANION_READ_ONLY");
        Text=$"OSRS Play {Diagnostics.Version} – RuneLite";ClientSize=new(940,700);
        MinimumSize=new(760,560);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(18,25,38);ForeColor=Color.WhiteSmoke;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),ColumnCount=1,RowCount=8};
        panel.RowStyles.Add(new(SizeType.Absolute,42));panel.RowStyles.Add(new(SizeType.Absolute,42));
        panel.RowStyles.Add(new(SizeType.Absolute,60));panel.RowStyles.Add(new(SizeType.Absolute,85));
        panel.RowStyles.Add(new(SizeType.Absolute,76));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.RowStyles.Add(new(SizeType.Absolute,44));panel.RowStyles.Add(new(SizeType.Absolute,28));
        panel.Controls.Add(new Label{Text="OSRS PLAY · RUNELITE · DIAGNOSE",Dock=DockStyle.Fill,
            Font=new("Segoe UI",16,FontStyle.Bold)},0,0);
        status.Dock=position.Dock=detail.Dock=failureBanner.Dock=DockStyle.Fill;
        status.Font=new("Segoe UI",12,FontStyle.Bold);position.Font=new("Segoe UI",20,FontStyle.Bold);
        detail.Font=new("Segoe UI",10);failureBanner.Font=new("Segoe UI",11,FontStyle.Bold);
        failureBanner.ForeColor=Color.FromArgb(255,105,105);
        failureBanner.Text="VERBESSERUNG MUSS EINGEBAUT WERDEN: RuneLite-Automatik ist noch nicht implementiert.";
        panel.Controls.Add(status,0,1);panel.Controls.Add(position,0,2);
        panel.Controls.Add(detail,0,3);panel.Controls.Add(failureBanner,0,4);
        logPreview.Dock=DockStyle.Fill;logPreview.ReadOnly=true;
        logPreview.Multiline=true;logPreview.ScrollBars=ScrollBars.Vertical;
        logPreview.WordWrap=false;logPreview.Font=new("Consolas",9);
        logPreview.BackColor=Color.FromArgb(12,18,29);logPreview.ForeColor=Color.FromArgb(220,230,240);
        logPreview.AccessibleName="Chronologisches Diagnoseprotokoll";
        panel.Controls.Add(logPreview,0,5);

        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};
        var launcher=new Button{Text="Jagex Launcher öffnen",AutoSize=true};
        launcher.Click+=(_,_)=>
        {
            log.Add(Severity.INFO,"RUNELITE","LAUNCHER_OPEN_REQUEST","origin=UI");
            try
            {
                if(!OfficialClientWindowHost.TryOpenJagexLauncher())
                    failureLog.ReportFailure("RUNELITE","LAUNCHER_NOT_FOUND","launcher_open_not_executed=true");
            }
            catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception)
            {failureLog.ReportFailure("RUNELITE","LAUNCHER_OPEN_FAILED",ExceptionDiagnostic.Describe(ex));}
            UpdateFailureBanner();RefreshLogPreview();
        };
        var pause=new Button{Text="Verbindung pausieren",AutoSize=true};
        pause.Click+=(_,_)=>
        {
            paused=!paused;generation++;source.Reset();position.Text="Position unbekannt";
            status.Text=paused?"Verbindung pausiert":"Warte auf neue Positionsbilder";
            log.Add(Severity.INFO,"RUNELITE",paused?"USER_PAUSED":"USER_RESUMED",
                $"generation={generation} old_position_discarded=true");
            pause.Text=paused?"Verbindung fortsetzen":"Verbindung pausieren";
            _=RefreshPosition();RefreshLogPreview();
        };
        copyLog.Text="Gesamte Logdatei kopieren";copyLog.AutoSize=true;
        copyLog.Click+=(_,_)=>{failureLog.CopyToClipboard();UpdateFailureBanner();RefreshLogPreview();};
        buttons.Controls.Add(launcher);buttons.Controls.Add(pause);buttons.Controls.Add(copyLog);
        panel.Controls.Add(buttons,0,6);
        panel.Controls.Add(new Label{
            Text="Lokales Sitzungslog: "+Path.GetFileName(failureLog.FilePath)+" · keine Screenshots · keine Spielaktionen",
            Dock=DockStyle.Fill,ForeColor=Color.LightGray,Font=new("Segoe UI",9)
        },0,7);
        Controls.Add(panel);

        log.Add(Severity.INFO,"CORE","STARTED",
            $"version={Diagnostics.Version} {BuildIdentity.Log} backend=RUNELITE_COMPANION profile_expected=auto");
        log.Add(Severity.INFO,"CORE","CAPABILITIES",
            "read_only=true automated_game_actions=false source=WORLD_LOCATION_GRID_INFO "+
            "fresh_frames_required=2 no_memory_access=true no_screenshots_written=true");
        log.Add(Severity.INFO,"CORE","PRIVACY",
            "credentials_and_authentication_handled_by_jagex=true local_log_only=true clipboard_on_failure=true");

        timer.Tick+=async(_,_)=>await RefreshPosition();timer.Start();
        healthTimer.Tick+=(_,_)=>
        {
            using var process=Process.GetCurrentProcess();
            log.Add(Severity.DEBUG,"RUNTIME","HEALTH",
                $"uptime_s={uptime.Elapsed.TotalSeconds:0} samples={samples} paused={paused} "+
                $"busy={busy} status={lastStatus??"none"} working_set_mib={process.WorkingSet64/1024/1024} "+
                $"log_storage={failureLog.StorageStatus} clipboard={failureLog.ClipboardStatus}");
            RefreshLogPreview();
        };
        healthTimer.Start();
        Shown+=async(_,_)=>
        {
            await RefreshPosition();
            if(IsDisposed||closing.IsCancellationRequested)return;
            failureLog.ReportFailure("RUNELITE","AUTOMATION_NOT_IMPLEMENTED",
                "official_client_read_only=true automatic_tasks_not_executed=true");
            UpdateFailureBanner();RefreshLogPreview();
        };
    }

    private async Task RefreshPosition()
    {
        if(busy||closing.IsCancellationRequested)return;
        busy=true;
        long currentGeneration=generation,sequence=++samples;
        RuneLitePositionState state;
        try
        {
            int pass=0;
            state=paused?new("PAUSED",Diagnostic:"user_requested_pause=true"):
                await source.ReadAsync(closing.Token,rows=>
                {
                    pass++;
                    string variant=pass switch{1=>"nearest_2x",2=>"nearest_3x",3=>"bicubic_4x",_=>"contrast_4x"};
                    var gridRows=rows.Where(r=>Regex.IsMatch(r,@"^(Tile|Chunk\s+ID|Region\s+ID)",RegexOptions.IgnoreCase)).Take(4).ToArray();
                    log.Add(Severity.DEBUG,"RUNELITE_OCR","PASS",
                        $"sample={sequence} variant={variant} grid_rows={gridRows.Length} "+
                        $"exact_panel_valid={RuneLiteGridReader.Parse(rows) is not null} text={string.Join(" || ",gridRows)}");
                });
        }
        catch(OperationCanceledException){return;}
        catch(Exception ex)
        {state=new("INTERNAL_ERROR",Diagnostic:ExceptionDiagnostic.Describe(ex));}
        finally{busy=false;}
        if(IsDisposed||closing.IsCancellationRequested)return;
        if(currentGeneration!=generation)
        {source.Reset();log.Add(Severity.WARN,"RUNELITE","OBSOLETE_RESULT_DISCARDED",
            $"sample={sequence} old_generation={currentGeneration} current_generation={generation}");return;}
        if(paused)state=new("PAUSED",Diagnostic:"user_requested_pause=true");

        log.Add(Severity.DEBUG,"RUNELITE","POSITION_SAMPLE",
            $"sample={sequence} result={state.Status} {state.Diagnostic} "+
            (state.Tile is WorldTile tile?$"x={tile.X} y={tile.Y} plane={tile.Plane}":"position=NOT_RELEASED"));
        if(lastStatus!=state.Status)
        {
            log.Add(Severity.INFO,"RUNELITE","STATUS_CHANGE",
                $"previous={lastStatus??"none"} current={state.Status} sample={sequence}");
            lastStatus=state.Status;blockedSince=DateTimeOffset.UtcNow;
        }
        bool stalledSecondFrame=state.Status=="WAITING_FOR_SECOND_FRAME"&&
            DateTimeOffset.UtcNow-blockedSince>=TimeSpan.FromSeconds(8);
        if(stalledSecondFrame||(FailureLogPolicy.RuneLiteBlocked(state.Status)&&
            (state.Status is "INTERNAL_ERROR" or "OCR_LANGUAGE_UNAVAILABLE" or "MULTIPLE_RUNELITE_CLIENTS"||
             DateTimeOffset.UtcNow-blockedSince>=TimeSpan.FromSeconds(4))))
            failureLog.ReportFailure("RUNELITE",state.Status,
                $"sample={sequence} {state.Diagnostic} position_not_released=true stalled_second_frame={stalledSecondFrame}");

        status.Text=state.Status switch
        {
            "OVERLAY_CONFIRMED"=>"RuneLite-Koordinatenanzeige aus zwei Bildern bestätigt",
            "PAUSED"=>"Verbindung pausiert",
            "GRID_INFO_NOT_READABLE"=>"World Location · Grid Info konnte nicht gelesen werden",
            "WAITING_FOR_SECOND_FRAME"=>"Warte auf ein zweites unabhängiges Bild",
            "RUNELITE_NOT_RUNNING"=>"RuneLite ist nicht gestartet oder nicht eindeutig erkannt",
            "STALE_FRAME"=>"Positionsbild ist nicht mehr aktuell",
            "CAPTURE_UNAVAILABLE"=>"RuneLite-Bild momentan nicht verfügbar",
            "OCR_LANGUAGE_UNAVAILABLE"=>"Windows-Texterkennung ist nicht verfügbar",
            "MULTIPLE_RUNELITE_CLIENTS"=>"Mehrere Positionsclients – bitte einen schließen",
            "INTERNAL_ERROR"=>"Interner Fehler bei der Positionsprüfung",
            _=>"Positionsdaten konnten nicht bestätigt werden"
        };
        position.Text=state.Tile is WorldTile p?$"X {p.X}    Y {p.Y}    Ebene {p.Plane}":"Position unbekannt";
        detail.Text=state.Tile is not null?
            "Quelle: World Location · eigene Spielerposition mit Chunk-/Region-Prüfung. Noch keine autonome Ausführung.":
            "RuneLite über den offiziellen Jagex Launcher starten. World Location: Grid Info aktiv, Type = Unique ID.\n"+
            "Alle OCR-Varianten, Aufnahmeinformationen und Ablehnungsgründe stehen im Sitzungslog.";
        UpdateFailureBanner();RefreshLogPreview();
    }

    private void UpdateFailureBanner()
    {
        string reason=failureLog.LastFailureCode??"RUNELITE/AUTOMATION_NOT_IMPLEMENTED";
        string copied=failureLog.ClipboardStatus=="COPIED"?
            "Log wurde in die Zwischenablage kopiert.":
            "Log-Kopie ausstehend/fehlgeschlagen – bitte Kopierknopf nutzen.";
        failureBanner.Text="VERBESSERUNG MUSS EINGEBAUT WERDEN · "+reason+"\n"+copied;
        copyLog.Text=failureLog.ClipboardStatus=="COPIED"?"Log erneut kopieren":"Gesamte Logdatei kopieren";
    }

    private void RefreshLogPreview()
    {
        string display=string.Join(Environment.NewLine,log.Snapshot().TakeLast(150).Select(e=>e.Line));
        if(logPreview.Text==display)return;
        logPreview.Text=display;logPreview.SelectionStart=logPreview.TextLength;logPreview.ScrollToCaret();
    }

    internal void ReportUnhandled(Exception ex)
    {
        failureLog.ReportFailure("RUNELITE_UI","UNHANDLED_EXCEPTION",ExceptionDiagnostic.Describe(ex));
        UpdateFailureBanner();RefreshLogPreview();
    }

    internal void VerifyDiagnosticLayout()
    {
        if(!failureBanner.Text.Contains("VERBESSERUNG MUSS EINGEBAUT WERDEN")||
           failureBanner.ForeColor!=Color.FromArgb(255,105,105)||
           !logPreview.ReadOnly||!logPreview.Multiline||copyLog.Text.Length==0)
            throw new Exception("RuneLite diagnostic GUI does not show a red improvement banner and read-only log");
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            closing.Cancel();timer.Dispose();healthTimer.Dispose();
            log.Add(Severity.INFO,"CORE","SHUTDOWN","no_game_process_terminated=true");
            failureLog.Dispose();source.Dispose();closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
