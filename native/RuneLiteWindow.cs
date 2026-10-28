using System.Diagnostics;

namespace OSRSLiveDiagnostics;

internal sealed class RuneLiteWindow:Form
{
    private readonly RuneLiteApiPositionSource source=new();
    private readonly RuneLiteMotionTrace movement=new();
    private readonly SemaphoreSlim routingSemaphore=new(1,1);
    private readonly Diagnostics log=new();
    private readonly ClientFailureLog failureLog;
    private readonly Label status=new(),position=new(),detail=new(),failureBanner=new(),updateStatus=new();
    private readonly TextBox logPreview=new();
    private readonly Button copyLog=new(),checkUpdate=new(),showExports=new();
    private readonly ComboBox navigationTarget=new();
    private readonly NavigationDestination[] navigationChoices=RuneLiteNavigationAdvisor.AvailableDestinations();
    private readonly System.Windows.Forms.Timer timer=new(){Interval=800};
    private readonly System.Windows.Forms.Timer healthTimer=new(){Interval=10000};
    private readonly System.Windows.Forms.Timer updateTimer=new(){Interval=30*60*1000};
    private readonly Stopwatch uptime=Stopwatch.StartNew();
    private readonly CancellationTokenSource closing=new();
    private bool paused,busy,updateRunning,updatePendingRestart;
    private long generation,samples;
    private string? lastStatus;
    private DateTimeOffset blockedSince;
    private DateTimeOffset lastConfirmedUtc=DateTimeOffset.UtcNow;
    private DateTimeOffset lastExportSummaryLoggedUtc;
    private WorldTile? lastVerifiedTile;
    private DateTimeOffset lastMotionEventUtc;
    private string latestPositionDescription="Noch keine bestätigte Position";
    private string routeDescription="Route: Ziel wählen und neue RuneLite-Position abwarten.";
    private Task<WorldMapData>? routeLoad;
    private WorldMapData? routeData;
    private long routeGeneration;

    internal RuneLiteWindow()
    {
        failureLog=new ClientFailureLog(log,ClientFailureLog.SessionPath("runelite"));
        log.SetMode("RUNELITE_COMPANION_READ_ONLY");
        Text=$"OSRS Play {Diagnostics.Version} – RuneLite";ClientSize=new(940,700);
        MinimumSize=new(760,560);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(18,25,38);ForeColor=Color.WhiteSmoke;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),ColumnCount=1,RowCount=8};
        panel.RowStyles.Add(new(SizeType.Absolute,42));panel.RowStyles.Add(new(SizeType.Absolute,42));
        panel.RowStyles.Add(new(SizeType.Absolute,60));panel.RowStyles.Add(new(SizeType.Absolute,110));
        panel.RowStyles.Add(new(SizeType.Absolute,76));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.RowStyles.Add(new(SizeType.Absolute,76));panel.RowStyles.Add(new(SizeType.Absolute,42));
        panel.Controls.Add(new Label{Text="OSRS PLAY · RUNELITE · DIAGNOSE",Dock=DockStyle.Fill,
            Font=new("Segoe UI",16,FontStyle.Bold)},0,0);
        status.Dock=position.Dock=detail.Dock=failureBanner.Dock=DockStyle.Fill;
        status.Font=new("Segoe UI",12,FontStyle.Bold);position.Font=new("Segoe UI",20,FontStyle.Bold);
        detail.Font=new("Segoe UI",9);failureBanner.Font=new("Segoe UI",11,FontStyle.Bold);
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
        navigationTarget.DropDownStyle=ComboBoxStyle.DropDownList;
        navigationTarget.Width=182;
        navigationTarget.AccessibleName="Leseziel für die RuneLite-Positionsnavigation";
        navigationTarget.Items.Add("Navigationsziel wählen");
        foreach(var destination in navigationChoices)navigationTarget.Items.Add(destination.Id);
        navigationTarget.SelectedIndex=0;
        navigationTarget.SelectedIndexChanged+=(_,_)=>
        {
            var selected=SelectedDestination();
            log.Add(Severity.INFO,"RUNELITE_NAV","TARGET_SELECTED",
                $"target={(selected is null?"NONE":selected.Id.Replace(' ','_'))} "+
                "advice_only=true movement_input=false collision_route_calculated=false");
            _=RefreshRouteAsync();UpdateDetailText();RefreshLogPreview();
        };
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
            paused=!paused;generation++;source.Reset();movement.Reset();
            lastMotionEventUtc=default;lastRouteEventUtc=default;
            position.Text="Position unbekannt";
            lastVerifiedTile=null;latestPositionDescription="Verbindung pausiert";
            routeGeneration++;routeDescription="Route: Verbindung pausiert.";
            if(!paused)lastConfirmedUtc=DateTimeOffset.UtcNow;
            status.Text=paused?"Verbindung pausiert":"Warte auf neues RuneLite-Positionsereignis";
            log.Add(Severity.INFO,"RUNELITE",paused?"USER_PAUSED":"USER_RESUMED",
                $"generation={generation} old_position_discarded=true");
            pause.Text=paused?"Verbindung fortsetzen":"Verbindung pausieren";
            UpdateDetailText();_=RefreshPosition();RefreshLogPreview();
        };
        copyLog.Text="Gesamte Logdatei kopieren";copyLog.AutoSize=true;
        copyLog.Click+=(_,_)=>{failureLog.CopyToClipboard();UpdateFailureBanner();RefreshLogPreview();};
        checkUpdate.Text="Jetzt Updates prüfen";checkUpdate.AutoSize=true;
        checkUpdate.Click+=async(_,_)=>await CheckForUpdatesAsync("MANUAL");
        showExports.Text="Zusatzdaten ansehen";showExports.AutoSize=true;
        showExports.Click+=(_,_)=>
        {
            var entries=source.AdditionalExports.Entries;
            string shown=entries.Count==0?
                "Noch keine passenden lokalen JSON-Snapshots gefunden. RuneLite muss eingeloggt sein.":
                string.Join(Environment.NewLine,entries.Select(e=>
                    $"{e.Display} · {Math.Max(0,e.AgeMinutes):0} Minuten alt"+
                    (e.Recent?"":" (historischer Stand)")));
            log.Add(Severity.INFO,"RUNELITE_EXPORTS","LOCAL_SUMMARY_OPENED",
                $"categories={entries.Count} raw_items_names_prices_and_json_excluded=true");
            MessageBox.Show(this,shown,"RuneLite · lokale Zusatzdaten",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            RefreshLogPreview();
        };
        buttons.Controls.Add(launcher);buttons.Controls.Add(pause);buttons.Controls.Add(copyLog);
        buttons.Controls.Add(checkUpdate);buttons.Controls.Add(navigationTarget);buttons.Controls.Add(showExports);
        panel.Controls.Add(buttons,0,6);
        updateStatus.Text="Automatische Updateprüfung beim Start und alle 30 Minuten · RuneLite bleibt geöffnet.\n"+
            "Sitzungslog: "+Path.GetFileName(failureLog.FilePath);
        updateStatus.Dock=DockStyle.Fill;updateStatus.ForeColor=Color.LightGray;
        updateStatus.Font=new("Segoe UI",9);
        panel.Controls.Add(updateStatus,0,7);
        Controls.Add(panel);

        log.Add(Severity.INFO,"CORE","STARTED",
            $"version={Diagnostics.Version} {BuildIdentity.Log} backend=RUNELITE_COMPANION profile_expected=auto");
        log.Add(Severity.INFO,"CORE","CAPABILITIES",
            "read_only=true automated_game_actions=false source=PLUGIN_HUB_POSITION_EVENTS "+
            "plugin_required=true plugin_install_manual=true screen_captures_for_position=false "+
            "navigation=API_EVENT_COLLISION_ROUTE_GUIDANCE extra_exports=LOCAL_SUMMARY_ONLY "+
            "no_memory_access=true no_screenshots_written=true");
        log.Add(Severity.INFO,"CORE","PRIVACY",
            "credentials_and_authentication_handled_by_jagex=true local_log_only=true clipboard_on_failure=true");
        if(Updater.TakeLastOutcome() is string previousUpdate)
            log.Add(Severity.INFO,"UPDATER","PREVIOUS_UPDATE",previousUpdate);
        log.Add(Severity.INFO,"UPDATER","CHECK_SCHEDULED",
            "startup=true interval_minutes=30 manual_button=true runeLite_process_untouched=true");

        timer.Tick+=async(_,_)=>await RefreshPosition();timer.Start();
        healthTimer.Tick+=(_,_)=>
        {
            using var process=Process.GetCurrentProcess();
            var motion=movement.Statistics;
            log.Add(Severity.DEBUG,"RUNTIME","HEALTH",
                $"uptime_s={uptime.Elapsed.TotalSeconds:0} samples={samples} paused={paused} "+
                $"busy={busy} status={lastStatus??"none"} working_set_mib={process.WorkingSet64/1024/1024} "+
                $"confirmed_position_frames={motion.ConfirmedFrames} stable_frames={motion.StableFrames} "+
                $"observed_tile_changes={motion.Changes} non_adjacent_observations={motion.NonAdjacent} "+
                $"log_storage={failureLog.StorageStatus} clipboard={failureLog.ClipboardStatus}");
            RefreshLogPreview();
        };
        healthTimer.Start();
        updateTimer.Tick+=async(_,_)=>await CheckForUpdatesAsync("PERIODIC");
        updateTimer.Start();
        Shown+=async(_,_)=>await CheckForUpdatesAsync("STARTUP");
        Shown+=async(_,_)=>
        {
            await RefreshPosition();
            if(IsDisposed||closing.IsCancellationRequested)return;
            failureLog.ReportFailure("RUNELITE","AUTOMATION_NOT_IMPLEMENTED",
                "official_client_read_only=true automatic_tasks_not_executed=true");
            UpdateFailureBanner();RefreshLogPreview();
        };
    }

    private async Task CheckForUpdatesAsync(string trigger)
    {
        if(updateRunning||updatePendingRestart||closing.IsCancellationRequested||IsDisposed)return;
        updateRunning=true;checkUpdate.Enabled=false;
        log.Add(Severity.INFO,"UPDATER","CHECK_START",
            $"trigger={trigger} current_version={Diagnostics.Version} verified_release_only=true independent_of_runelite=true");
        try
        {
            bool restart=await Updater.CheckAndUpdateAsync(message=>
            {
                log.Add(Severity.INFO,"UPDATER","STATUS",message);
                if(!IsDisposed)updateStatus.Text=message+"\nSitzungslog: "+Path.GetFileName(failureLog.FilePath);
                if(!IsDisposed)RefreshLogPreview();
            },closing.Token,(phase,details)=>
            {
                log.Add(phase=="CHECK_FAILED"?Severity.ERROR:Severity.INFO,"UPDATER",phase,details);
            },error=>
            {
                if(IsDisposed)return;
                failureLog.ReportFailure("UPDATER","UPDATE_CHECK_FAILED",ExceptionDiagnostic.Describe(error));
                UpdateFailureBanner();RefreshLogPreview();
            });
            if(closing.IsCancellationRequested||IsDisposed)return;
            if(restart)
            {
                updatePendingRestart=true;
                log.Add(Severity.INFO,"UPDATER","RESTART_REQUESTED",
                    $"trigger={trigger} reason=VERIFIED_NEW_VERSION runeLite_process_untouched=true");
                Close(); // The verified helper replaces only this EXE after it exits.
                return;
            }
            log.Add(Severity.INFO,"UPDATER","CHECK_FINISHED",$"trigger={trigger} restart_required=false");
        }
        catch(OperationCanceledException) when(closing.IsCancellationRequested){}
        catch(Exception ex)
        {
            if(!IsDisposed&&!closing.IsCancellationRequested)
            {
                failureLog.ReportFailure("UPDATER","UPDATE_UI_FAILED",ExceptionDiagnostic.Describe(ex));
                UpdateFailureBanner();RefreshLogPreview();
            }
        }
        finally
        {
            updateRunning=false;
            if(!IsDisposed)checkUpdate.Enabled=true;
        }
    }

    private async Task RefreshPosition()
    {
        if(busy||closing.IsCancellationRequested)return;
        busy=true;
        long currentGeneration=generation,sequence=++samples;
        RuneLitePositionState state;
        try
        {
            state=paused?new("PAUSED",Diagnostic:"user_requested_pause=true"):
                await source.ReadAsync(closing.Token,pixelDiagnostic:details=>
                    log.Add(Severity.DEBUG,"RUNELITE_API","EVENT_VALIDATION",
                    $"sample={sequence} {details}"));
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
        if(source.LastExportScanUtc!=default&&
           source.LastExportScanUtc!=lastExportSummaryLoggedUtc)
        {
            lastExportSummaryLoggedUtc=source.LastExportScanUtc;
            log.Add(Severity.INFO,"RUNELITE_EXPORTS","SUMMARY_SCAN",
                $"sample={sequence} {source.AdditionalExports.Diagnostic}");
        }
        // A RuneLite API event is counted exactly once. Re-reading an
        // unchanged position.json must not create synthetic motion frames.
        if(state.Tile is WorldTile confirmedTile)
        {
            string? observation=null;
            if(state.ObservedUtc>lastMotionEventUtc)
            {
                observation=movement.Observe(confirmedTile,state.ObservedUtc,sequence);
                lastMotionEventUtc=state.ObservedUtc;
            }
            if(observation is not null)
                log.Add(Severity.INFO,"RUNELITE_MOTION","OBSERVATION",observation);
            var guidance=RuneLiteNavigationAdvisor.FromConfirmed(confirmedTile,SelectedDestination());
            if(guidance is not null&&observation is not null)
                log.Add(Severity.INFO,"RUNELITE_NAV","API_EVENT_GUIDANCE",
                    $"sample={sequence} {guidance.Diagnostic}");
        }
        else if(state.Status is "RUNELITE_NOT_RUNNING" or "MULTIPLE_RUNELITE_CLIENTS" or
            "PLUGIN_EXPORT_UNAVAILABLE" or "PLUGIN_EXPORT_INVALID" or "INTERNAL_ERROR")
        {
            if(movement.Statistics.ConfirmedFrames>0)
            {
                log.Add(Severity.INFO,"RUNELITE_MOTION","RESET",
                    $"reason={state.Status} api_event_history_discarded=true");
                movement.Reset();
                lastMotionEventUtc=default;
            }
        }
        if(lastStatus!=state.Status)
        {
            log.Add(Severity.INFO,"RUNELITE","STATUS_CHANGE",
                $"previous={lastStatus??"none"} current={state.Status} sample={sequence}");
            lastStatus=state.Status;blockedSince=DateTimeOffset.UtcNow;
        }
        if(state.Tile is not null)lastConfirmedUtc=DateTimeOffset.UtcNow;
        else if(!paused&&state.Status!="API_WAITING_FOR_NEW_EVENT"&&
            DateTimeOffset.UtcNow-lastConfirmedUtc>=TimeSpan.FromSeconds(10))
            failureLog.ReportFailure("RUNELITE","POSITION_NOT_CONFIRMED_TIMEOUT",
                $"sample={sequence} latest_status={state.Status} "+
                $"unconfirmed_s={(DateTimeOffset.UtcNow-lastConfirmedUtc).TotalSeconds:0} {state.Diagnostic}");
        bool stalledSecondFrame=state.Status=="WAITING_FOR_SECOND_FRAME"&&
            DateTimeOffset.UtcNow-blockedSince>=TimeSpan.FromSeconds(8);
        if(stalledSecondFrame||(FailureLogPolicy.RuneLiteBlocked(state.Status)&&
            (state.Status is "INTERNAL_ERROR" or "OCR_LANGUAGE_UNAVAILABLE" or "MULTIPLE_RUNELITE_CLIENTS"||
             DateTimeOffset.UtcNow-blockedSince>=TimeSpan.FromSeconds(4))))
            failureLog.ReportFailure("RUNELITE",state.Status,
                $"sample={sequence} {state.Diagnostic} position_not_released=true stalled_second_frame={stalledSecondFrame}");

        status.Text=state.Status switch
        {
            "API_POSITION_UPDATED"=>"RuneLite-API · neue Position empfangen",
            "API_POSITION_LAST_KNOWN"=>"RuneLite-API · letzter Positionsstand (kein Heartbeat)",
            "API_WAITING_FOR_NEW_EVENT"=>"RuneLite-API · bitte im Spiel eine Kachel bewegen",
            "PLUGIN_EXPORT_UNAVAILABLE"=>"RuneLite-Plugin-Export nicht erreichbar",
            "PLUGIN_EXPORT_INVALID"=>"RuneLite-Positionsereignis ungültig",
            "PAUSED"=>"Verbindung pausiert",
            "GRID_INFO_NOT_READABLE"=>"World Location · Grid Info konnte nicht gelesen werden",
            "WAITING_FOR_EXPORT_FRAME"=>"Neuer Positions-Export erkannt · warte auf aktuelles RuneLite-Bild",
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
        if(state.Tile is null&&lastVerifiedTile is not null)
        {
            routeGeneration++;lastRouteEventUtc=default;
            routeDescription="Route: Position aus dieser Sitzung nicht verfügbar.";
        }
        lastVerifiedTile=state.Tile;
        latestPositionDescription=state.Tile is not null?
            (state.Status=="API_POSITION_UPDATED"?
                "Quelle: offizielles RuneLite-API-Ereignis · kürzlich aktualisiert.":
                "Quelle: letzter RuneLite-API-Stand · Plugin meldet im Stillstand keine Heartbeats.")+
            " Keine Screenshotprüfung und keine Spieleingaben.":
            state.Status=="API_WAITING_FOR_NEW_EVENT"?
                "Export stammt möglicherweise aus einer früheren Sitzung · einmal im Spiel bewegen.":
                "RuneLite über Jagex Launcher starten · Plugin Hub: OSRS Toolkit Exporter aktivieren.";
        UpdateDetailText();
        if(state.Tile is not null&&state.ObservedUtc!=default&&
            state.ObservedUtc==lastMotionEventUtc&&
            state.Status=="API_POSITION_UPDATED"&&
            lastRouteEventUtc!=state.ObservedUtc)
        {
            lastRouteEventUtc=state.ObservedUtc;
            _=RefreshRouteAsync();
        }
        UpdateFailureBanner();RefreshLogPreview();
    }

    private NavigationDestination? SelectedDestination()=>
        navigationTarget.SelectedIndex is >0&&navigationTarget.SelectedIndex<=navigationChoices.Length?
            navigationChoices[navigationTarget.SelectedIndex-1]:null;

    private DateTimeOffset lastRouteEventUtc;

    private async Task RefreshRouteAsync()
    {
        long request=++routeGeneration;
        var destination=SelectedDestination();
        var tile=lastVerifiedTile;
        if(paused||destination is null||tile is null)
        {
            routeDescription=destination is null?"Route: Ziel auswählen.":
                "Route: Warte auf ein neues RuneLite-API-Positionsereignis.";
            UpdateDetailText();
            return;
        }
        routeDescription="Route: lade geprüfte Kollisionsdaten und plane Weg …";
        UpdateDetailText();
        try
        {
            routeLoad??=WorldMapData.LoadRoutingOnly(s=>
                log.Add(Severity.INFO,"RUNELITE_ROUTE","MAP",s),closing.Token);
            var data=await routeLoad;
            if(IsDisposed||closing.IsCancellationRequested||request!=routeGeneration)return;
            routeData=data;
            await routingSemaphore.WaitAsync(closing.Token);
            PathStep[]? path;
            try
            {
                path=await Task.Run(()=>data.FindPath(tile,destination.Tile,
                    destination.Radius,closing.Token),closing.Token);
            }
            finally{routingSemaphore.Release();}
            if(IsDisposed||closing.IsCancellationRequested||request!=routeGeneration)return;
            if(path is null)
            {
                routeDescription="Route: Kein geprüfter Weg in den verfügbaren F2P-Kollisionsdaten.";
                log.Add(Severity.WARN,"RUNELITE_ROUTE","NO_ROUTE",
                    $"target={destination.Id.Replace(' ','_')} from={tile.X},{tile.Y},{tile.Plane} "+
                    "manual_walking_only=true no_unverified_shortcut=true");
            }
            else
            {
                var guide=RuneLiteRouteCoach.FromPath(tile,destination,path);
                routeDescription=guide.Display;
                log.Add(Severity.INFO,"RUNELITE_ROUTE","ROUTE_RECALCULATED",
                    guide.Diagnostic+" source=PLUGIN_HUB_EXPORT");
            }
            UpdateDetailText();RefreshLogPreview();
        }
        catch(OperationCanceledException) when(closing.IsCancellationRequested){}
        catch(Exception ex)
        {
            if(IsDisposed||closing.IsCancellationRequested||request!=routeGeneration)return;
            routeLoad=null;routeDescription="Route: geprüfte Kartendaten derzeit nicht verfügbar.";
            failureLog.ReportFailure("RUNELITE_ROUTE","ROUTE_DATA_UNAVAILABLE",
                ExceptionDiagnostic.Describe(ex));
            UpdateDetailText();UpdateFailureBanner();RefreshLogPreview();
        }
    }

    private void UpdateDetailText()
    {
        var guide=RuneLiteNavigationAdvisor.FromConfirmed(lastVerifiedTile,SelectedDestination());
        string navigation=guide?.Display??(SelectedDestination() is null?
            "Navigation: Ziel unten auswählen; Entfernung nur Luftlinie, keine Route.":
            "Navigation: Warte auf bestätigte RuneLite-Spielerposition.");
        var summaries=source.AdditionalExports.Entries.Where(e=>e.Kind is
            "INVENTORY" or "BANK" or "QUESTS" or "GE" or "CHARACTER").Take(5)
            .Select(e=>e.Display+(e.Recent?"":" (älterer Stand)"));
        string others=string.Join(" · ",summaries);
        detail.Text=latestPositionDescription+Environment.NewLine+navigation+Environment.NewLine+
            routeDescription+Environment.NewLine+
            (others.Length>0?"Lokale Exportdaten: "+others:
                "Lokale Exportdaten: noch keine verifizierten Zusatzdateien.");
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
           !logPreview.ReadOnly||!logPreview.Multiline||copyLog.Text.Length==0||
           navigationTarget.DropDownStyle!=ComboBoxStyle.DropDownList||navigationChoices.Length<10||
           showExports.Text.Length==0||
           checkUpdate.Text.Length==0||!updateTimer.Enabled||updateTimer.Interval!=30*60*1000||
           !updateStatus.Text.Contains("Updateprüfung"))
            throw new Exception("RuneLite GUI lacks diagnostics or automatic/manual updater controls");
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            closing.Cancel();timer.Dispose();healthTimer.Dispose();updateTimer.Dispose();
            routeData?.Dispose();routingSemaphore.Dispose();
            log.Add(Severity.INFO,"CORE","SHUTDOWN","no_game_process_terminated=true");
            failureLog.Dispose();source.Dispose();closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
