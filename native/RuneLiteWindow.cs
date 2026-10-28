using System.Diagnostics;

namespace OSRSLiveDiagnostics;

internal sealed class RuneLiteWindow:Form
{
    private readonly RuneLiteApiPositionSource source=new();
    private readonly RuneLiteMotionTrace movement=new();
    private readonly RuneLiteRouteProgress routeProgress=new();
    private readonly RuneLiteAutoWalk autoWalk=new();
    private readonly SemaphoreSlim routingSemaphore=new(1,1);
    private readonly Diagnostics log=new();
    private readonly ClientFailureLog failureLog;
    private readonly Label status=new(),position=new(),detail=new(),failureBanner=new(),updateStatus=new();
    private readonly TextBox logPreview=new();
    private readonly Button copyLog=new(),checkUpdate=new(),showExports=new();
    private readonly Button startWalk=new(),stopWalk=new();
    private readonly ComboBox navigationTarget=new();
    private readonly NavigationDestination[] navigationChoices=RuneLiteNavigationAdvisor.AvailableDestinations();
    private readonly System.Windows.Forms.Timer timer=new(){Interval=800};
    private readonly System.Windows.Forms.Timer healthTimer=new(){Interval=10000};
    private readonly System.Windows.Forms.Timer updateTimer=new(){Interval=30*60*1000};
    private readonly Stopwatch uptime=Stopwatch.StartNew();
    private readonly CancellationTokenSource closing=new();
    private bool paused,busy,updateRunning,updatePendingRestart,calibrating,clickInProgress;
    private string? autoWalkWindowTitle;
    private long generation,samples;
    private string? lastStatus;
    private DateTimeOffset blockedSince;
    private DateTimeOffset lastConfirmedUtc=DateTimeOffset.UtcNow;
    private DateTimeOffset lastExportSummaryLoggedUtc;
    private WorldTile? lastVerifiedTile;
    private DateTimeOffset lastMotionEventUtc;
    private DateTimeOffset lastPositionApiEventUtc;
    private string latestPositionDescription="Noch keine bestätigte Position";
    private string routeDescription="Route: Ziel wählen und neue RuneLite-Position abwarten.";
    private string routeProgressDescription="Routenverlauf: Noch keine Vergleichsposition.";
    private string autoWalkDescription="Automatik: AUS · Zum Test aktivieren und Minimap kalibrieren.";
    private Task<WorldMapData>? routeLoad;
    private WorldMapData? routeData;
    private long routeGeneration;

    internal RuneLiteWindow()
    {
        failureLog=new ClientFailureLog(log,ClientFailureLog.SessionPath("runelite"));
        log.SetMode("RUNELITE_COMPANION_OPT_IN_WALK_TEST");
        Text=$"OSRS Play {Diagnostics.Version} – RuneLite";ClientSize=new(940,700);
        MinimumSize=new(760,560);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(18,25,38);ForeColor=Color.WhiteSmoke;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),ColumnCount=1,RowCount=8};
        panel.RowStyles.Add(new(SizeType.Absolute,42));panel.RowStyles.Add(new(SizeType.Absolute,42));
        panel.RowStyles.Add(new(SizeType.Absolute,60));panel.RowStyles.Add(new(SizeType.Absolute,165));
        panel.RowStyles.Add(new(SizeType.Absolute,76));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.RowStyles.Add(new(SizeType.Absolute,94));panel.RowStyles.Add(new(SizeType.Absolute,42));
        panel.Controls.Add(new Label{Text="OSRS PLAY · RUNELITE · DIAGNOSE",Dock=DockStyle.Fill,
            Font=new("Segoe UI",16,FontStyle.Bold)},0,0);
        status.Dock=position.Dock=detail.Dock=failureBanner.Dock=DockStyle.Fill;
        status.Font=new("Segoe UI",12,FontStyle.Bold);position.Font=new("Segoe UI",20,FontStyle.Bold);
        detail.Font=new("Segoe UI",9);failureBanner.Font=new("Segoe UI",11,FontStyle.Bold);
        failureBanner.ForeColor=Color.FromArgb(255,105,105);
        failureBanner.Text="VERBESSERUNG MUSS EINGEBAUT WERDEN, wenn ein Bewegungstest scheitert. " +
            "Automatische Bewegung ist AUS, bis du sie ausdrücklich freigibst.";
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
        navigationTarget.AccessibleName="Ziel für die RuneLite-Navigation und optionale Automatik";
        navigationTarget.Items.Add("Navigationsziel wählen");
        foreach(var destination in navigationChoices)navigationTarget.Items.Add(destination.Id);
        navigationTarget.SelectedIndex=0;
        navigationTarget.SelectedIndexChanged+=(_,_)=>
        {
            if(autoWalk.Active)StopAutoWalk("TARGET_CHANGED",false);
            routeProgress.Reset();
            routeProgressDescription="Routenverlauf: Neuer Zielvergleich startet.";
            var selected=SelectedDestination();
            log.Add(Severity.INFO,"RUNELITE_NAV","TARGET_SELECTED",
                $"target={(selected is null?"NONE":selected.Id.Replace(' ','_'))} "+
                $"auto_walk_active={autoWalk.Active.ToString().ToLowerInvariant()} collision_route_calculated=false");
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
            if(autoWalk.Active)StopAutoWalk("USER_PAUSED",false);
            paused=!paused;generation++;source.Reset();movement.Reset();
            lastMotionEventUtc=default;lastRouteEventUtc=default;
            lastPositionApiEventUtc=default;routeProgress.Reset();
            routeProgressDescription="Routenverlauf: Verbindung pausiert.";
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
        startWalk.Text="Automatisch gehen (Test)";startWalk.AutoSize=true;
        startWalk.Click+=async(_,_)=>await BeginAutoWalkAsync();
        stopWalk.Text="AUTOMATIK STOPP";stopWalk.AutoSize=true;stopWalk.Enabled=false;
        stopWalk.BackColor=Color.FromArgb(145,35,35);stopWalk.ForeColor=Color.White;
        stopWalk.Click+=(_,_)=>StopAutoWalk("USER_STOPPED",false);
        buttons.Controls.Add(launcher);buttons.Controls.Add(pause);buttons.Controls.Add(copyLog);
        buttons.Controls.Add(checkUpdate);buttons.Controls.Add(navigationTarget);buttons.Controls.Add(showExports);
        buttons.Controls.Add(startWalk);buttons.Controls.Add(stopWalk);
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
            "read_only_by_default=true automated_game_actions=EXPLICIT_FOREGROUND_MOUSE_OPT_IN "+
            "input_transport=WIN32_SENDINPUT_VERIFIED_RUNELITE_FOREGROUND "+
            "clicks_require_fresh_api_events=true source=PLUGIN_HUB_POSITION_EVENTS "+
            "plugin_required=true plugin_install_manual=true screen_captures_for_position=false "+
            "navigation=API_EVENT_COLLISION_ROUTE_AND_SHORT_WAYPOINTS "+
            "minimap_geometry=user_calibrated_in_memory extra_exports=LOCAL_SUMMARY_ONLY "+
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
                $"autowalk_active={autoWalk.Active} autowalk_waiting={autoWalk.WaitingForMovement} "+
                $"autowalk_clicks={autoWalk.Clicks} log_storage={failureLog.StorageStatus} "+
                $"clipboard={failureLog.ClipboardStatus}");
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
            log.Add(Severity.INFO,"RUNELITE_AUTOWALK","DISARMED_AT_STARTUP",
                "user_opt_in_required=true no_unattended_clicks=true calibration_required=true");
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
                if(autoWalk.Active)StopAutoWalk("UPDATE_RESTART",false);
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
        if(autoWalk.Active)
        {
            if(autoWalkWindowTitle!=source.TrustedWindowTitle)
                StopAutoWalk("RUNELITE_TITLE_CHANGED",true,"player_title_changed=true no_more_input=true");
            else if(autoWalk.Clicks>0&&!clickInProgress&&
                !BackgroundWindowInput.IsRuneLiteForeground(source.TrustedWindowHandle))
                StopAutoWalk("RUNELITE_FOREGROUND_LOST",true,
                    "active_user_window_changed=true no_more_clicks=true input_not_targeted_elsewhere=true");
            else if(state.Status is "RUNELITE_NOT_RUNNING" or "MULTIPLE_RUNELITE_CLIENTS" or
                "PLUGIN_EXPORT_UNAVAILABLE" or "PLUGIN_EXPORT_INVALID" or "INTERNAL_ERROR")
                StopAutoWalk("API_SOURCE_LOST",true,$"sample={sequence} state={state.Status}");
            else if(state.Status=="API_POSITION_UPDATED"&&
                state.ObservedUtc>lastMotionEventUtc&&state.Tile is WorldTile walked)
            {
                var feedback=autoWalk.Observe(walked,state.ObservedUtc,DateTimeOffset.UtcNow);
                if(feedback.Status=="OFF_PATH")
                    StopAutoWalk("UNEXPECTED_API_TILE",true,feedback.Diagnostic);
                else if(feedback.Status!="IGNORED")
                {
                    log.Add(Severity.INFO,"RUNELITE_AUTOWALK",feedback.Status,feedback.Diagnostic);
                    autoWalkDescription=feedback.Status is "WAYPOINT_CONFIRMED" or "WAYPOINT_NEAR_CONFIRMED" or "WAYPOINT_CORRIDOR_REPLAN"?
                        "Automatik: RuneLite-API-Fortschritt bestätigt · prüfe neue Kollisionsroute.":
                        "Automatik: echte Kachelbewegung bestätigt · warte auf Wegpunkt.";
                }
            }
            if(autoWalk.Timeout(DateTimeOffset.UtcNow) is string fault)
                StopAutoWalk(fault,true,$"sample={sequence} status={state.Status} "+
                    $"awaiting_move={autoWalk.WaitingForMovement}");
        }
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
                log.Add(Severity.INFO,"RUNELITE_MOTION","OBSERVATION",observation+
                    $" autowalk_active={autoWalk.Active.ToString().ToLowerInvariant()} "+
                    $"autowalk_pending_click={autoWalk.WaitingForMovement.ToString().ToLowerInvariant()}");
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
            lastPositionApiEventUtc=default;routeProgress.Reset();
            routeDescription="Route: Position aus dieser Sitzung nicht verfügbar.";
            routeProgressDescription="Routenverlauf: Warte auf eine neue API-Position.";
        }
        lastVerifiedTile=state.Tile;
        if(state.Tile is not null)lastPositionApiEventUtc=state.ObservedUtc;
        latestPositionDescription=state.Tile is not null?
            (state.Status=="API_POSITION_UPDATED"?
                "Quelle: offizielles RuneLite-API-Ereignis · kürzlich aktualisiert.":
                "Quelle: letzter RuneLite-API-Stand · Plugin meldet im Stillstand keine Heartbeats.")+
            " Keine Screenshotprüfung für Positionen; Spieleingaben nur bei aktivem Test.":
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

    private async Task BeginAutoWalkAsync()
    {
        if(calibrating||autoWalk.Active||paused||closing.IsCancellationRequested)return;
        var goal=SelectedDestination();
        if(goal is null)
        {
            MessageBox.Show(this,"Bitte zunächst ein Navigationsziel auswählen.",
                "Automatisches Gehen",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        IntPtr window=source.TrustedWindowHandle;
        if(lastVerifiedTile is null||window==IntPtr.Zero||
            !BackgroundWindowInput.TryClientSize(window,out var size))
        {
            failureLog.ReportFailure("RUNELITE_AUTOWALK","START_REQUIRES_PLUGIN_POSITION",
                "recent_accepted_runelite_api_position_and_trusted_window_required=true no_input_sent=true");
            UpdateFailureBanner();RefreshLogPreview();return;
        }
        calibrating=true;startWalk.Enabled=false;
        try
        {
            log.Add(Severity.INFO,"RUNELITE_AUTOWALK","CALIBRATION_REQUEST",
                $"target={goal.Id.Replace(' ','_')} client_width={size.Width} client_height={size.Height} "+
                "position_source=PLUGIN_API user_verified_fixed_minimap_anchor=true "+
                "minimap_reset_4px_per_tile_requested=true camera_zoom_unchanged=true "+
                "screenshot_for_click_geometry_only=true screenshot_stored=false");
            // The game image is displayed temporarily for the user's choice
            // of the actual minimap centre. No file, screenshot or pixels are
            // added to the diagnostic log or saved to disk.
            using var capture=new BackgroundWindowCapture();
            CapturedWindowFrame? frame=null;
            for(int attempt=0;attempt<18&&!closing.IsCancellationRequested;attempt++)
            {
                frame=capture.ReadClient(window);
                if(frame is not null)break;
                await Task.Delay(200,closing.Token);
            }
            if(frame is null)
            {
                failureLog.ReportFailure("RUNELITE_AUTOWALK","MINIMAP_CALIBRATION_CAPTURE_FAILED",
                    $"capture_state={Diagnostics.Sanitize(capture.State)} "+
                    $"frame_error={Diagnostics.Sanitize(capture.LastFrameError)} no_input_sent=true");
                UpdateFailureBanner();return;
            }
            using(frame)
            {
                if(frame.Image.Size!=size||frame.CapturedUtc<DateTimeOffset.UtcNow.AddSeconds(-3))
                {
                    failureLog.ReportFailure("RUNELITE_AUTOWALK","MINIMAP_FRAME_GEOMETRY_INVALID",
                        $"capture={frame.Image.Width}x{frame.Image.Height} window={size.Width}x{size.Height} "+
                        "screenshot_not_saved=true no_input_sent=true");
                    UpdateFailureBanner();return;
                }
                using var setup=new RuneLiteMinimapSetupDialog(frame.Image);
                if(setup.ShowDialog(this)!=DialogResult.OK||setup.PlayerCenter is not Point center)
                {
                    log.Add(Severity.INFO,"RUNELITE_AUTOWALK","CALIBRATION_CANCELLED",
                        "no_input_sent=true no_screenshot_saved=true");
                    return;
                }
                if(closing.IsCancellationRequested||IsDisposed||paused||
                   source.TrustedWindowHandle!=window||SelectedDestination()?.Id!=goal.Id||
                   !BackgroundWindowInput.TryClientSize(window,out var updatedSize)||updatedSize!=size)
                {
                    failureLog.ReportFailure("RUNELITE_AUTOWALK","CALIBRATION_CONTEXT_CHANGED",
                        "window_destination_or_pause_changed=true no_input_sent=true");
                    UpdateFailureBanner();return;
                }
                log.Add(Severity.INFO,"RUNELITE_AUTOWALK","MINIMAP_ANCHOR_CALIBRATED",
                    $"method={setup.SelectionSource} center_x={center.X} center_y={center.Y} "+
                    $"geometry_diagnostic={setup.MarkerDiagnostic} "+
                    "user_reviewed_preview=true screenshot_stored=false "+
                    "no_world_position_from_pixels=true world_tile_source=PLUGIN_API");
                var resetPoint=BackgroundWindowInput.MinimapResetPoint(center);
                log.Add(Severity.INFO,"RUNELITE_MINIMAP","ZOOM_RESET_REQUEST",
                    $"expected_pixels_per_tile=4 reset_point={resetPoint.X},{resetPoint.Y} "+
                    "method=RUNELITE_MINIMAP_RIGHT_CLICK user_approved_foreground_mouse=true "+
                    "camera_zoom_unchanged=true plugin_api_zoom_not_exported=true");
                string? calibratedWindowTitle=source.TrustedWindowTitle;
                var zoom=await BackgroundWindowInput.ResetRuneLiteMinimapZoomAsync(window,size,
                    center,
                    ()=>paused||closing.IsCancellationRequested||
                        source.TrustedWindowHandle!=window||
                        source.TrustedWindowTitle!=calibratedWindowTitle||
                        SelectedDestination()?.Id!=goal.Id,
                    closing.Token);
                if(!zoom.Inserted)
                {
                    failureLog.ReportFailure("RUNELITE_MINIMAP","MINIMAP_ZOOM_RESET_NOT_COMPLETED",
                        $"center={center.X},{center.Y} reason={zoom.Reason} "+
                        $"input_events={zoom.InputEvents} no_autowalk_click_sent=true");
                    UpdateFailureBanner();return;
                }
                log.Add(Severity.INFO,"RUNELITE_MINIMAP","ZOOM_RESET_INPUT_SENT",
                    $"expected_pixels_per_tile=4 result={zoom.Reason} "+
                    $"input_events={zoom.InputEvents} foreground_runelite_only=true "+
                    "actual_minimap_zoom_unverified_without_plugin_export=true camera_untouched=true");
                var geometry=new RuneLiteAutoWalk.Calibration(window,size,center,setup.PixelsPerTile);
                autoWalk.Start(goal,geometry,DateTimeOffset.UtcNow);
                autoWalkWindowTitle=source.TrustedWindowTitle;
                stopWalk.Enabled=true;
                autoWalkDescription="Automatik: AKTIV · Vordergrund-RuneLite und bestätigte Bewegung erforderlich.";
                log.Add(Severity.INFO,"RUNELITE_AUTOWALK","ARMED",
                    $"target={goal.Id.Replace(' ','_')} minimap_center={center.X},{center.Y} "+
                    $"pixels_per_tile={setup.PixelsPerTile:0.0} client={size.Width}x{size.Height} "+
                    "north_up_confirmed_by_user=true minimap_reset_rightclick_dispatched=true "+
                    "first_calibrated_click_last_known_api_max_age_s=60 first_click_window_s=12 "+
                    "later_clicks_require_event_age_s_at_most=3 "+
                    "click_limit=40 corridor_replan_limit=2 runtime_limit_s=300 verify_each_waypoint=true "+
                    $"marker_selection_method={setup.SelectionSource} expected_minimap_scale=4 "+
                    "foreground_mouse_consent=true sendinput_not_postmessage=true "+
                    "source=PLUGIN_API no_screenshot_saved=true");
                _=RefreshRouteAsync();
            }
        }
        catch(OperationCanceledException) when(closing.IsCancellationRequested){}
        catch(Exception ex)
        {
            failureLog.ReportFailure("RUNELITE_AUTOWALK","CALIBRATION_EXCEPTION",
                ExceptionDiagnostic.Describe(ex)+" no_input_sent=true");
            UpdateFailureBanner();
        }
        finally
        {
            calibrating=false;
            if(!IsDisposed)startWalk.Enabled=!autoWalk.Active;
            if(!IsDisposed){UpdateDetailText();RefreshLogPreview();}
        }
    }

    private async Task MaybeSendWalkClickAsync(WorldTile tile,NavigationDestination goal,
        PathStep[] path,DateTimeOffset eventAt)
    {
        if(!autoWalk.Active||clickInProgress||paused)return;
        if(autoWalk.Destination?.Id!=goal.Id)
        {StopAutoWalk("TARGET_CHANGED",false);return;}
        if(path.Length==0)
        {
            if(tile.Distance(goal.Tile)<=goal.Radius)StopAutoWalk("TARGET_REACHED",false);
            return;
        }
        var now=DateTimeOffset.UtcNow;
        if(!autoWalk.CanDispatch(now,eventAt))
        {
            var age=eventAt==default?(double?)null:(now-eventAt).TotalMilliseconds;
            log.Add(Severity.INFO,"RUNELITE_AUTOWALK","CLICK_DEFERRED",
                $"target={goal.Id.Replace(' ','_')} "+
                $"api_event_age_ms={(age is null?"none":age.Value.ToString("F0",System.Globalization.CultureInfo.InvariantCulture))} "+
                $"clicks={autoWalk.Clicks} waiting_for_waypoint={autoWalk.WaitingForMovement} "+
                "initial_click_calibration_override_max_age_s=60 first_click_window_s=12 "+
                "later_clicks_require_fresh_api_event_s=3 no_input_sent=true");
            autoWalkDescription="Automatik: wartet auf freigegebenes Plugin-Ereignis; ggf. eine Kachel bewegen.";
            return;
        }
        var geometry=autoWalk.Geometry!;
        if(source.TrustedWindowHandle!=geometry.Window||
            autoWalkWindowTitle!=source.TrustedWindowTitle||
            !BackgroundWindowInput.TryClientSize(geometry.Window,out var size)||
            size!=geometry.ClientSize)
        {
            StopAutoWalk("GAME_WINDOW_CHANGED",true,
                "trusted_runeLite_process_or_calibrated_client_size_changed=true no_input_sent=true");
            return;
        }
        var plan=RuneLiteAutoWalk.Project(tile,path,geometry);
        if(plan is null)
        {
            StopAutoWalk("WAYPOINT_PROJECTION_UNAVAILABLE",true,
                $"target={goal.Id.Replace(' ','_')} current={tile.X},{tile.Y},{tile.Plane} "+
                $"remaining_steps={path.Length} next_transport={path[0].Transport is not null} "+
                "projection_did_not_pass_safe_minimap_radius_or_walkable_edge=true no_input_sent=true");
            return;
        }
        clickInProgress=true;
        bool manuallyAttestedFirstClick=autoWalk.Clicks==0&&
            now-eventAt>TimeSpan.FromSeconds(3)&&
            autoWalk.CalibratedFirstClickAllowed(now,eventAt);
        autoWalk.Sent(plan,eventAt,now);
        log.Add(Severity.INFO,"RUNELITE_AUTOWALK","CLICK_INTENT",
            $"command={autoWalk.Clicks} target={goal.Id.Replace(' ','_')} "+
            $"from={tile.X},{tile.Y},{tile.Plane} waypoint={plan.Waypoint.X},{plan.Waypoint.Y},{plan.Waypoint.Plane} "+
            $"waypoint_index={plan.WaypointIndex} click_x={plan.Pixel.X} click_y={plan.Pixel.Y} "+
            $"api_event_age_ms={(now-eventAt).TotalMilliseconds:0} "+
            $"manually_calibrated_first_click={manuallyAttestedFirstClick.ToString().ToLowerInvariant()} "+
            "transport=SENDINPUT_FOREGROUND_ONLY input_not_yet_verified=true screenshot_position_estimation=false");
        try
        {
            var sent=await BackgroundWindowInput.ClickRuneLiteForegroundAsync(
                geometry.Window,plan.Pixel,geometry.ClientSize,autoWalk.Clicks==1,
                ()=>!autoWalk.Active||paused||closing.IsCancellationRequested||
                    source.TrustedWindowHandle!=geometry.Window||
                    autoWalkWindowTitle!=source.TrustedWindowTitle,
                closing.Token);
            if(!autoWalk.Active)return;
            if(!sent.Inserted)
            {
                StopAutoWalk("FOREGROUND_CLICK_NOT_SENT",true,
                    $"command={autoWalk.Clicks} click_x={plan.Pixel.X} click_y={plan.Pixel.Y} "+
                    $"reason={sent.Reason} input_events={sent.InputEvents} "+
                    "input_not_verified=true no_position_claim=true window_focus_required=true");
                return;
            }
            autoWalkDescription="Automatik: Windows-Mauseingabe gesendet · überprüfe RuneLite-API-Bewegung.";
            log.Add(Severity.INFO,"RUNELITE_AUTOWALK","FOREGROUND_INPUT_INSERTED",
                $"command={autoWalk.Clicks} input_events={sent.InputEvents} transport=WIN32_SENDINPUT "+
                "foreground_and_target_checked=true awaiting_newer_api_event=true "+
                "injection_is_not_proof_of_game_movement=true click_status=UNVERIFIED");
        }
        catch(OperationCanceledException) when(closing.IsCancellationRequested){}
        catch(Exception ex)
        {
            if(autoWalk.Active)StopAutoWalk("WINDOW_CLICK_EXCEPTION",true,ExceptionDiagnostic.Describe(ex));
        }
        finally{clickInProgress=false;}
    }

    private void StopAutoWalk(string reason,bool failed,string details="")
    {
        if(!autoWalk.Active)return;
        int commands=autoWalk.Clicks;
        bool pending=autoWalk.WaitingForMovement;
        autoWalk.Stop();
        autoWalkWindowTitle=null;
        stopWalk.Enabled=false;startWalk.Enabled=!calibrating;
        autoWalkDescription=reason=="TARGET_REACHED"?
            "Automatik: Zielradius laut RuneLite-API erreicht!":
            failed?"Automatik: FEHLER – gestoppt; bitte rotes Diagnosefeld beachten.":
                "Automatik: gestoppt · eventuelle letzte Spielbewegung manuell abbrechen.";
        string diagnostic=$"reason={reason} commands_posted_or_attempted={commands} "+
            $"pending_verification={pending} no_more_clicks=true in_game_last_click_may_continue=true {details}";
        log.Add(failed?Severity.WARN:Severity.INFO,"RUNELITE_AUTOWALK",
            failed?"SAFETY_STOP":"STOPPED",diagnostic);
        if(failed)
            failureLog.ReportFailure("RUNELITE_AUTOWALK",reason,diagnostic);
        UpdateDetailText();UpdateFailureBanner();RefreshLogPreview();
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
        var eventAt=lastPositionApiEventUtc;
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
                if(autoWalk.Active)StopAutoWalk("NO_COLLISION_ROUTE",true,
                    $"target={destination.Id.Replace(' ','_')} x={tile.X} y={tile.Y} plane={tile.Plane}");
                routeProgress.Reset();
                routeProgressDescription="Routenverlauf: Ohne geprüften Weg nicht messbar.";
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
                if(eventAt!=default)
                {
                    var progress=routeProgress.Observe(tile,destination,path,eventAt);
                    routeProgressDescription=progress.Display;
                    log.Add(Severity.INFO,"RUNELITE_ROUTE","PROGRESS",
                        progress.Diagnostic+" source=PLUGIN_HUB_EXPORT");
                }
                if(autoWalk.Active)
                    await MaybeSendWalkClickAsync(tile,destination,path,eventAt);
            }
            UpdateDetailText();RefreshLogPreview();
        }
        catch(OperationCanceledException) when(closing.IsCancellationRequested){}
        catch(Exception ex)
        {
            if(IsDisposed||closing.IsCancellationRequested||request!=routeGeneration)return;
            routeLoad=null;routeDescription="Route: geprüfte Kartendaten derzeit nicht verfügbar.";
            routeProgress.Reset();
            routeProgressDescription="Routenverlauf: Wartet auf verifizierte Kartendaten.";
            failureLog.ReportFailure("RUNELITE_ROUTE","ROUTE_DATA_UNAVAILABLE",
                ExceptionDiagnostic.Describe(ex));
            if(autoWalk.Active)StopAutoWalk("ROUTE_DATA_UNAVAILABLE",true,
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
            routeDescription+Environment.NewLine+routeProgressDescription+Environment.NewLine+
            autoWalkDescription+Environment.NewLine+
            (others.Length>0?"Lokale Exportdaten: "+others:
                "Lokale Exportdaten: noch keine verifizierten Zusatzdateien.");
    }

    private void UpdateFailureBanner()
    {
        string? reason=failureLog.LastFailureCode;
        if(reason is null)
        {
            failureBanner.Text="VERBESSERUNG MUSS EINGEBAUT WERDEN, falls der erste " +
                "automatische Bewegungstest scheitert. Noch nicht live bestätigt.";
            copyLog.Text="Gesamte Logdatei kopieren";
            return;
        }
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
           startWalk.Text.Length==0||stopWalk.Text.Length==0||stopWalk.Enabled||
           showExports.Text.Length==0||
           checkUpdate.Text.Length==0||!updateTimer.Enabled||updateTimer.Interval!=30*60*1000||
           !updateStatus.Text.Contains("Updateprüfung"))
            throw new Exception("RuneLite GUI lacks diagnostics or automatic/manual updater controls");
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            if(autoWalk.Active)StopAutoWalk("WINDOW_CLOSING",false);
            closing.Cancel();timer.Dispose();healthTimer.Dispose();updateTimer.Dispose();
            routeData?.Dispose();routingSemaphore.Dispose();
            log.Add(Severity.INFO,"CORE","SHUTDOWN","no_game_process_terminated=true");
            failureLog.Dispose();source.Dispose();closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
