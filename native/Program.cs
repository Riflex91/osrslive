using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace OSRSLiveDiagnostics;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--apply-update")return Updater.Apply(args);
        if(args.Length>0 && args[0]=="--self-test")return SelfTest.Run();
        if(args.Length>0 && args[0]=="--ocr-self-test")return VisualStartup.OcrSelfTest().GetAwaiter().GetResult();
        if(args.Length>0 && args[0]=="--scenario-tests")
        {
            Console.Error.WriteLine("OFFLINE_SESSIONS_REMOVED");
            return 2;
        }
        ApplicationConfiguration.Initialize();
        if(args.Contains("--background-self-test"))return BackgroundIntegrationTests.Run();
        if(args.Contains("--layout-self-test"))return WindowLayoutTests.Run();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>
            MessageBox.Show(e.Exception.GetType().Name+": "+e.Exception.Message,
                "OSRS Play: UI-Fehler",MessageBoxButtons.OK,MessageBoxIcon.Error);
        try{Application.Run(new MainWindow(!args.Contains("--ui-smoke-test"),args.Contains("--observe-only")));return 0;}
        catch(Exception ex)
        {
            try
            {
                var file=Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    "OSRSLiveDiagnostics","fatal.log");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.AppendAllText(file,DateTimeOffset.Now+" "+ex+Environment.NewLine);
            }
            catch{}
            MessageBox.Show("Start fehlgeschlagen: "+ex.Message,
                "OSRS Play",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}

// One-window shell with diagnostic controls. ONLY the unmodified official Jagex
// Launcher and osclient.exe are displayed. Startup mouse input is isolated
// from the still-missing gameplay state/action routines.
internal sealed class MainWindow : Form
{
    private readonly Diagnostics log=new();
    private readonly Label heading=new(),status=new(),footer=new();
    private readonly TextBox eventLog=new();
    private readonly TableLayoutPanel layout=new();
    private readonly Button toggleLog=new(),copyLog=new(),pauseAutomation=new();
    private readonly Panel gameSurface=new()
        {Dock=DockStyle.Fill,BackColor=Color.FromArgb(18,25,38)};
    private readonly Label emptyHint=new()
    {
        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,
        BackColor=Color.FromArgb(18,25,38),ForeColor=Color.WhiteSmoke,
        Font=new Font("Segoe UI",12,FontStyle.Regular),
        Text="OSRS PLAY – LIVE ONLY\n\n"+
             "Offizieller Jagex Launcher und OSRS werden automatisch eingebettet.\n"+
             "Anmeldung und eventuelle MFA erfolgen nur in Jagex.\n\n"+
             "Suche nach der offiziellen Spielsitzung …"
    };
    private readonly CancellationTokenSource closing=new();
    private readonly System.Windows.Forms.Timer uiTimer=new(){Interval=250};
    private readonly System.Windows.Forms.Timer diagnosticsTimer=new(){Interval=10000};
    private readonly Stopwatch uptime=Stopwatch.StartNew();
    private OfficialClientWindowHost windowHost=null!;
    private readonly OfficialLiveRenderObserver liveObserver=new();
    private readonly VisualStartup visualStartup;
    private bool visualTickRunning;
    private readonly bool autoStartupEnabled;
    private bool pauseHotkeyRegistered;
    private readonly AccountProgressStore progressStore=new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSPlay","progress"));
    private string lastGameDiagnostic="",lastGoalDiagnostic="";
    private bool logRequestedVisible=true;
    private readonly System.Windows.Forms.Timer liveObservationTimer=new(){Interval=2000};
    private LiveRenderState lastReportedRenderState=LiveRenderState.NoGameWindow;
    private bool followWindows,starting,scanning,stopping;
    private bool loggedBlockedAgent;
    private long lastWindowCheckMs;
    private string f2pSummary="F2P-Welt: noch nicht gemessen";
    private int? preferredWorld;
    private bool changePending=true;
    private readonly object runtimeLogSync=new();
    private readonly string logDirectory=Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OSRSPlay","live-logs");
    private readonly string runtimeLogFile;

    internal MainWindow(bool startAutomatically=true,bool observeOnly=false)
    {
        autoStartupEnabled=startAutomatically;
        visualStartup=new VisualStartup(log,liveObserver);
        visualStartup.ObserveOnly=observeOnly;
        runtimeLogFile=Path.Combine(logDirectory,
            !startAutomatically?"ui-smoke-test.log":observeOnly?"observation-session.log":"last-session.log");
        Text=$"OSRS Play {Diagnostics.Version} – offizielles OSRS";
        AutoScaleMode=AutoScaleMode.None;
        FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
        ClientSize=DockedWindowLayout.ClientSize(true);
        StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);
        BackColor=Color.FromArgb(245,247,250);
        BuildUI();
        windowHost=new OfficialClientWindowHost(gameSurface,log);
        log.Changed+=()=>changePending=true;
        log.SetMode("LIVE ONLY: ORIGINALER JAGEX LAUNCHER UND OSRS");
        log.Add(Severity.INFO,"CORE","STARTED",
            $"version={Diagnostics.Version} mode=LIVE_ONLY controls=DIAGNOSTICS "+
            "game_source=OFFICIAL_JAGEX");
        log.Add(Severity.INFO,"CORE","PRIVACY",
            "credentials_and_oauth_handled_only_by_jagex=true");
        log.Add(Severity.INFO,"LIVE_AGENT","CAPABILITIES",
            "startup_input=WINDOW_MESSAGES capture=WINDOWS_GRAPHICS_CAPTURE gameplay_routines=MISSING "+
            "goal=MAX_ALL_SKILLS_THEN_WEALTH_AND_GEAR");
        if(Updater.TakeLastOutcome() is string result)
            log.Add(Severity.INFO,"UPDATER","PREVIOUS_UPDATE",result);

        uiTimer.Tick+=(_,_) =>
        {
            bool hadPlan=visualStartup.Game.Plan is not null;
            visualStartup.Game.Expire(DateTimeOffset.UtcNow);
            if(hadPlan&&visualStartup.Game.Plan is null)changePending=true;
            if(followWindows && uptime.ElapsedMilliseconds-lastWindowCheckMs>1050)
            {
                lastWindowCheckMs=uptime.ElapsedMilliseconds;
                FollowOfficialWindows();
            }
            if(windowHost.Attached)windowHost.Resize();
            if(changePending)
            {
                changePending=false;
                RefreshUI();
            }
        };
        uiTimer.Start();
        liveObservationTimer.Tick+=async (_,_)=>
        {
            if(visualTickRunning||stopping||!followWindows)return;
            visualTickRunning=true;
            try
            {
                await visualStartup.TickAsync(windowHost.VisibleWindowHandle,
                    windowHost.CurrentKind,Handle,preferredWorld,closing.Token);
            }
            catch(OperationCanceledException){}
            catch(Exception ex)
            {
                log.Add(Severity.WARN,"VISUAL_STARTUP","OBSERVATION_FAILED",
                    "reason="+ex.GetType().Name);
            }
            finally
            {
                visualTickRunning=false;changePending=true;
                if(!stopping){ObserveOfficialGame();ReportGameObservation();}
            }
        };
        liveObservationTimer.Start();
        diagnosticsTimer.Tick+=(_,_) =>
        {
            using var p=Process.GetCurrentProcess();
            log.Add(Severity.DEBUG,"RUNTIME","HEALTH",
                $"osrs_window={(windowHost.Attached?windowHost.CurrentKind.ToString():"none")} "+
                $"render_state={liveObserver.Last.State} "+
                $"frame_sample_age_s={(DateTimeOffset.UtcNow-liveObserver.Last.ObservedUtc).TotalSeconds:0.0} "+
                $"working_set_mib={p.WorkingSet64/1024/1024} "+
                $"uptime_s={uptime.Elapsed.TotalSeconds:0} capture={visualStartup.CaptureState} "+
                "authenticated=false_unverified agent_actions=DISABLED");
            PersistLog();
        };
        diagnosticsTimer.Start();
        if(startAutomatically)Shown+=async (_,_)=>await StartAutomaticallyAsync();
        else Shown+=(_,_)=>
        {
            visualStartup.Paused=true;
            status.Text="UI-Test: keine Updates, kein Clientstart, keine Eingaben";
        };
        FormClosing+=(_,_) =>
        {
            stopping=true;followWindows=false;
            closing.Cancel();uiTimer.Stop();diagnosticsTimer.Stop();
            liveObservationTimer.Stop();liveObservationTimer.Dispose();
            visualStartup.Dispose();windowHost.Dispose();liveObserver.Reset();
            log.Add(Severity.INFO,"CORE","SHUTDOWN",
                "jagex_process_not_terminated=true");
            PersistLog();
            closing.Dispose();
        };
    }

    private void ReportGameObservation()
    {
        var observed=visualStartup.Game.LastObservation;
        if(observed is not null)
        {
            string diagnostic=$"stage={observed.Stage} skill_reads={observed.RecognizedSkills}/24 "+
                $"total_level={observed.TotalLevel?.ToString()??"unknown"} read_sum={observed.ReadLevelSum?.ToString()??"unknown"} "+
                $"character_prompt_seen={observed.Character is not null} source=VISIBLE_OCR server_auth_verified=false";
            if(diagnostic!=lastGameDiagnostic)
            {lastGameDiagnostic=diagnostic;log.Add(Severity.INFO,"GAME_OBSERVER","STATE",diagnostic);}
        }
        var confirmed=visualStartup.Game.Confirmed;
        var plan=visualStartup.Game.Plan;
        string goal=plan is null?"state=WAITING_FOR_CONSISTENT_VISIBLE_SKILLS":
            $"phase={plan.Phase} skill={plan.Skill??"none"} reason={plan.Reason} "+
            "scope=F2P_PROPOSAL executor=NOT_IMPLEMENTED";
        if(goal!=lastGoalDiagnostic)
        {lastGoalDiagnostic=goal;log.Add(Severity.INFO,"ACCOUNT_GOAL","DECISION",goal);}
        if(confirmed is not null&&plan is not null)
        {
            try
            {
                if(progressStore.Save(confirmed,plan))log.Add(Severity.INFO,"ACCOUNT_PROGRESS","SAVED",
                    $"total_level={confirmed.TotalLevel} skills=24 source=REPEATED_VISUAL_OCR character_name_persisted=false");
            }
            catch(Exception ex){log.Add(Severity.WARN,"ACCOUNT_PROGRESS","SAVE_FAILED","reason="+ex.GetType().Name);}
        }
    }

    private void BuildUI()
    {
        layout.Dock=DockStyle.Fill;layout.Padding=new Padding(11);
        layout.ColumnCount=1;layout.RowCount=5;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,DockedWindowLayout.HeaderHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,DockedWindowLayout.StatusHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,DockedWindowLayout.GameSize.Height));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,DockedWindowLayout.LogHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,DockedWindowLayout.ToolbarHeight));
        Controls.Add(layout);
        heading.Text=$"OSRS PLAY {Diagnostics.Version}  ·  AUTOMATISCHER LIVE-BETRIEB";
        heading.Dock=DockStyle.Fill;heading.Font=new Font("Segoe UI",13,FontStyle.Bold);
        layout.Controls.Add(heading,0,0);
        status.Dock=DockStyle.Fill;status.Font=new Font("Segoe UI",10);
        status.Text="Starte offizielle Jagex-Anmeldung …";
        layout.Controls.Add(status,0,1);
        gameSurface.Margin=Padding.Empty;
        gameSurface.Controls.Add(emptyHint);
        layout.Controls.Add(gameSurface,0,2);
        eventLog.Dock=DockStyle.Fill;eventLog.ReadOnly=true;
        eventLog.Multiline=true;eventLog.ScrollBars=ScrollBars.Vertical;
        eventLog.WordWrap=true;
        eventLog.BackColor=Color.FromArgb(18,25,38);
        eventLog.ForeColor=Color.FromArgb(220,230,240);
        eventLog.Font=new Font("Consolas",9);
        layout.Controls.Add(eventLog,0,3);
        footer.Dock=DockStyle.Fill;footer.TextAlign=ContentAlignment.MiddleLeft;
        footer.Text="Live-Agent wartet auf echte Daten-/Aktionsschnittstelle";
        var toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toggleLog.Text="Log ausblenden";toggleLog.AutoSize=true;
        toggleLog.AccessibleName="Log ausblenden oder einblenden";
        toggleLog.Click+=(_,_)=>SetLogVisible(!eventLog.Visible);
        copyLog.Text="Log kopieren";copyLog.AutoSize=true;
        copyLog.AccessibleName="Diagnosebericht in die Zwischenablage kopieren";
        copyLog.Click+=(_,_)=>CopyLog();
        pauseAutomation.Text="Automatik pausieren (F8)";pauseAutomation.AutoSize=true;
        pauseAutomation.Click+=(_,_)=>visualStartup.Paused=!visualStartup.Paused;
        toolbar.Controls.Add(footer,0,0);
        toolbar.Controls.Add(pauseAutomation,1,0);
        toolbar.Controls.Add(toggleLog,2,0);
        toolbar.Controls.Add(copyLog,3,0);
        layout.Controls.Add(toolbar,0,4);
        AdjustLogLayout();
    }

    private void SetLogVisible(bool visible)
    {
        logRequestedVisible=visible;
        AdjustLogLayout();
        log.Add(Severity.INFO,"UI","LOG_VISIBILITY_CHANGED",$"visible={visible} logging_continues=true");
    }
    private void AdjustLogLayout()
    {
        layout.SuspendLayout();
        eventLog.Visible=logRequestedVisible;
        layout.RowStyles[3].Height=logRequestedVisible?DockedWindowLayout.LogHeight:0;
        ClientSize=DockedWindowLayout.ClientSize(logRequestedVisible);
        toggleLog.Text=logRequestedVisible?"Log ausblenden":"Log einblenden";
        layout.ResumeLayout(true);
        if(IsHandleCreated&&Bottom>Screen.FromControl(this).WorkingArea.Bottom)
            Top=Math.Max(Screen.FromControl(this).WorkingArea.Top,Screen.FromControl(this).WorkingArea.Bottom-Height);
    }

    private void CopyLog()
    {
        try
        {
            // Snapshot the report independently of the visible 420-line preview.
            Clipboard.SetDataObject(log.Report(),true,3,80);
            log.Add(Severity.INFO,"UI","LOG_COPIED","sanitized_diagnostic_report=true");
            copyLog.Text="Log kopiert";
        }
        catch(System.Runtime.InteropServices.ExternalException)
        {
            log.Add(Severity.WARN,"UI","LOG_COPY_FAILED","reason=CLIPBOARD_BUSY retry_available=true");
            copyLog.Text="Erneut kopieren";
        }
    }

    internal void VerifyDockedLayout()
    {
        PerformLayout();layout.PerformLayout();
        if(gameSurface.ClientSize!=DockedWindowLayout.GameSize)
            throw new Exception($"Unexpected game viewport {gameSurface.ClientSize}");
        if(FormBorderStyle!=FormBorderStyle.FixedSingle||MaximizeBox)
            throw new Exception("Window must have a fixed border and disabled maximize");
        int shownHeight=ClientSize.Height;
        if(eventLog.Top<gameSurface.Bottom)throw new Exception("Log overlaps the game viewport");
        SetLogVisible(false);PerformLayout();layout.PerformLayout();
        if(gameSurface.ClientSize!=DockedWindowLayout.GameSize||ClientSize.Height!=shownHeight-DockedWindowLayout.LogHeight)
            throw new Exception("Hiding log resized the game instead of removing the dock");
        SetLogVisible(true);PerformLayout();layout.PerformLayout();
        if(gameSurface.ClientSize!=DockedWindowLayout.GameSize||ClientSize.Height!=shownHeight)
            throw new Exception("Showing log did not restore fixed dock geometry");
    }

    private async Task StartAutomaticallyAsync()
    {
        if(starting)return;
        starting=true;
        bool updating=false;
        try
        {
            if(visualStartup.ObserveOnly)
            {
                log.Add(Severity.INFO,"CORE","OBSERVATION_ONLY","updates_skipped=true no_input=true");
                return;
            }
            log.Add(Severity.INFO,"UPDATER","CHECK_START","verified_release_only=true");
            bool updated=await Updater.CheckAndUpdateAsync(message=>
            {
                log.Add(Severity.INFO,"UPDATER","STATUS",message);
            });
            if(updated)
            {
                updating=true;
                log.Add(Severity.INFO,"UPDATER","RESTART_REQUESTED",
                    "do_not_start_jagex_in_obsolete_process=true");
                Close();
                return;
            }
            log.Add(Severity.INFO,"UPDATER","CHECK_FINISHED",
                "official_login_and_f2p_scan_starting=true");
        }
        catch(Exception ex)
        {
            log.Add(Severity.WARN,"UPDATER","CHECK_FAILED",
                "reason="+ex.GetType().Name);
        }
        finally
        {
            if(!updating && !stopping && !IsDisposed)
            {
                followWindows=true;
                if(visualStartup.ObserveOnly)FollowOfficialWindows();
                else{StartOfficialLauncherIfNeeded();_=MeasureF2PAutomatically();}
            }
        }
    }

    private void ObserveOfficialGame()
    {
        if(stopping || !IsHandleCreated)return;
        var frame=liveObserver.Last;
        if(frame.State!=lastReportedRenderState)
        {
            lastReportedRenderState=frame.State;
            log.Add(Severity.INFO,"OFFICIAL_OBSERVER","RENDER_STATE_CHANGED",
                $"state={frame.State} pixels_sampled={frame.SampleCells} "+
                $"variance={frame.LuminanceVariance} changed={frame.ChangedCells} "+
                "origin=WINDOWS_GRAPHICS_CAPTURE auth_verified=false "+
                "world_verified=false screenshot_persisted=false");
        }
        // This has no access to the actual authenticated player state,
        // and must therefore never enable real gameplay automation.
        changePending=true;
    }
    private void StartOfficialLauncherIfNeeded()
    {
        if(windowHost.Attached)return;
        try
        {
            var games=OfficialClientWindowHost.FindRunningClients();
            var launchers=OfficialClientWindowHost.FindRunningLauncherWindows();
            log.Add(Severity.INFO,"OFFICIAL_CLIENT","AUTO_LOGIN_START",
                $"games={games.Count} launchers={launchers.Count} "+
                "login_owner=JAGEX_LAUNCHER");
            if(games.Count>0||launchers.Count>0)
            {
                FollowOfficialWindows();
                return;
            }
            if(OfficialClientWindowHost.TryOpenJagexLauncher())
            {
                log.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_START_REQUESTED",
                    "automatic=true credentials_handled_by_jagex=true");
                FollowOfficialWindows();
            }
            else
            {
                log.Add(Severity.WARN,"OFFICIAL_CLIENT","LAUNCHER_NOT_FOUND",
                    "official_jagex_launcher_must_be_installed=true");
                footer.Text="Jagex Launcher nicht gefunden – offizielle Installation erforderlich.";
            }
        }
        catch(Exception ex)
        {
            log.Add(Severity.ERROR,"OFFICIAL_CLIENT","AUTO_START_FAILED",
                "reason="+ex.GetType().Name);
            footer.Text="Jagex Launcher konnte nicht automatisch geöffnet werden.";
        }
    }

    private void FollowOfficialWindows()
    {
        if(stopping || windowHost is null)return;
        if(windowHost.Attached && windowHost.CurrentKind==OfficialWindowKind.Game)
            return; // Never reattach launcher after a game has become a child HWND.
        try
        {
            var games=OfficialClientWindowHost.FindRunningClients();
            if(games.Count>0)
            {
                if(games.Count>1)
                    log.Add(Severity.WARN,"OFFICIAL_CLIENT","MULTIPLE_GAME_WINDOWS",
                        $"count={games.Count} selecting_lowest_pid=true no_new_client_started=true");
                var chosen=games.OrderBy(c=>c.ProcessId).First();
                if(windowHost.Attach(chosen))
                {
                    emptyHint.Visible=false;
                    log.Add(Severity.INFO,"OFFICIAL_CLIENT","GAME_WINDOW_READY",
                        $"pid={chosen.ProcessId} kind=Game official_osrs=true "+
                        "login_state=UNVERIFIED world_selection=NOT_VERIFIED");
                    liveObserver.Reset();
                    if(!loggedBlockedAgent)
                    {
                        loggedBlockedAgent=true;
                        log.Add(Severity.INFO,"LIVE_AGENT","AUTO_START_EVALUATED",
                            "goal=MAX_ALL_SKILLS_THEN_WEALTH_AND_GEAR "+
                            "state=WAITING_FOR_VERIFIED_SKILLS_AND_ACTIVITY_RUNNERS "+
                            "game_window_visible=true pixel_observation_cannot_verify_login=true");
                        log.Add(Severity.INFO,"VISUAL_STARTUP","AUTO_START",
                            $"input_enabled={!visualStartup.ObserveOnly} transport=WINDOW_MESSAGES "+
                            "gameplay_routines=MISSING F8=PAUSE");
                    }
                    changePending=true;
                }
                return;
            }
            if(windowHost.Attached && windowHost.CurrentKind==OfficialWindowKind.Launcher)
                return;
            var launchers=OfficialClientWindowHost.FindRunningLauncherWindows();
            if(launchers.Count>0)
            {
                if(launchers.Count>1)
                    log.Add(Severity.WARN,"OFFICIAL_CLIENT","MULTIPLE_LAUNCHERS",
                        $"count={launchers.Count} selecting_lowest_pid=true");
                var chosen=launchers.OrderBy(c=>c.ProcessId).First();
                if(windowHost.Attach(chosen))
                {
                    emptyHint.Visible=false;
                    log.Add(Severity.INFO,"OFFICIAL_CLIENT","LOGIN_WINDOW_READY",
                        $"pid={chosen.ProcessId} login=JAGEX "+
                        "automatic_window_host=true");
                    changePending=true;
                }
            }
        }
        catch(Exception ex)
        {
            log.Add(Severity.WARN,"OFFICIAL_CLIENT","AUTO_WINDOW_ATTACH_FAILED",
                "reason="+ex.GetType().Name);
        }
    }

    private async Task MeasureF2PAutomatically()
    {
        if(scanning)return;
        scanning=true;
        log.Add(Severity.INFO,"F2P_PING","AUTO_SCAN_START",
            "official_worldlist=true free_only=true tcp_probe_only=true");
        try
        {
            var scan=await OfficialF2PWorldScanner.ScanAsync(closing.Token);
            if(stopping || IsDisposed)return;
            log.Add(Severity.INFO,"F2P_PING","CATALOG_CHECKED",
                $"eligible={scan.Eligible} reachable={scan.Reachable}");
            foreach(var item in scan.Fastest.Take(5))
                log.Add(Severity.INFO,"F2P_PING","CANDIDATE",
                    $"world={item.World.WorldNumber} "+
                    $"location={item.World.Location} "+
                    $"tcp_ms={item.Milliseconds.ToString("0.0",CultureInfo.InvariantCulture)}");
            if(scan.Fastest.Count==0)
            {
                f2pSummary="F2P-Ping: keine erreichbare Welt";
                log.Add(Severity.WARN,"F2P_PING","NO_REACHABLE_WORLDS",
                    "auto_world_entry=false");
            }
            else
            {
                var best=scan.Fastest[0];
                preferredWorld=best.World.WorldNumber;
                f2pSummary=$"Beste F2P-Welt W{preferredWorld}: "+
                    $"{best.Milliseconds:0.0} ms TCP";
                log.Add(Severity.INFO,"F2P_PING","BEST_WORLD",
                    $"world={preferredWorld} "+
                    $"tcp_latency_ms={best.Milliseconds.ToString("0.0",CultureInfo.InvariantCulture)} "+
                    "measurement=THIS_WINDOWS_PC applied_in_game=false "+
                    "selection=VISUAL_STARTUP_PENDING");
                log.Add(Severity.INFO,"OFFICIAL_CLIENT","AUTO_WORLD_ENTRY_PENDING",
                    $"recommended_world={preferredWorld} "+
                    "confirmation=NEXT_VISIBLE_SCREEN");
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex)
        {
            f2pSummary="F2P-Prüfung fehlgeschlagen";
            log.Add(Severity.WARN,"F2P_PING","SCAN_FAILED",
                "reason="+ex.GetType().Name);
        }
        finally
        {
            scanning=false;
            changePending=true;
        }
    }

    private void RefreshUI()
    {
        string window=windowHost.Attached?
            windowHost.CurrentKind==OfficialWindowKind.Game?"OSRS-SPIEL SICHTBAR":
            "JAGEX-ANMELDUNG SICHTBAR":
            "WARTE AUF OFFIZIELLEN CLIENT";
        string render=windowHost.Attached &&
            windowHost.CurrentKind==OfficialWindowKind.Game?
            " | Livebild: "+liveObserver.Last.State:"";
        status.Text=$"{window}{render}  |  {f2pSummary}"+
            $"  |  Automatik: {visualStartup.State}";
        var plan=visualStartup.Game.Plan;
        if(plan is not null)status.Text+=$" | Zielvorschlag: {plan.Skill??plan.Phase.ToString()}";
        if(!autoStartupEnabled)status.Text="UI-Test: keine Updates, kein Clientstart, keine Eingaben";
        pauseAutomation.Text=visualStartup.Paused?"Automatik fortsetzen":"Automatik pausieren (F8)";
        if(!string.IsNullOrEmpty(footer.Text) &&
           footer.Text.Contains("nicht gefunden",StringComparison.OrdinalIgnoreCase))
        {
            // Keep the actionable installation explanation.
        }
        else footer.Text=windowHost.Attached &&
            windowHost.CurrentKind==OfficialWindowKind.Game?
            visualStartup.ObserveOnly?"Nur Beobachtung: keine Mauseingaben. Zielvorschläge führen noch keine Spielaktionen aus.":
            "Hintergrundstart per Fensternachrichten; Zielvorschlag aus Skills. Spielroutinen fehlen.":
            "Jagex-Anmeldung erfolgt im Original-Launcher. OSRS Play fordert kein Passwort an.";
        if(!eventLog.Visible)return;
        var events=log.Snapshot().TakeLast(420);
        string display=string.Join(Environment.NewLine,events.Select(e=>e.Line));
        if(eventLog.Text!=display)
        {
            eventLog.Text=display;
            eventLog.SelectionStart=eventLog.TextLength;
            eventLog.ScrollToCaret();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if(keyData==Keys.F8){visualStartup.Paused=true;changePending=true;return true;}
        // Same diagnostic report as the visible button, including when hidden.
        if(keyData==(Keys.Control|Keys.Shift|Keys.C))
        {
            CopyLog();
            return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        pauseHotkeyRegistered=RegisterHotKey(Handle,0x5253,0x4000,0x77);
        if(!pauseHotkeyRegistered)
            log.Add(Severity.WARN,"VISUAL_INPUT","F8_HOTKEY_UNAVAILABLE",
                "pause_button_available=true movement_polling_available=true");
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if(pauseHotkeyRegistered){UnregisterHotKey(Handle,0x5253);pauseHotkeyRegistered=false;}
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312 && m.WParam.ToInt32()==0x5253)
        {
            visualStartup.Paused=true;changePending=true;
            log.Add(Severity.INFO,"VISUAL_INPUT","PAUSED","source=F8");
            return;
        }
        base.WndProc(ref m);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]
    private static extern bool RegisterHotKey(IntPtr handle,int id,uint modifiers,uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]
    private static extern bool UnregisterHotKey(IntPtr handle,int id);

    private void PersistLog()
    {
        try
        {
            lock(runtimeLogSync)
            {
                Directory.CreateDirectory(logDirectory);
                var temporary=runtimeLogFile+".tmp";
                File.WriteAllText(temporary,log.Report(),new UTF8Encoding(false));
                File.Move(temporary,runtimeLogFile,true);
            }
        }
        catch(Exception)
        {
            // Diagnostic storage failure must not affect an official session.
        }
    }
}

internal static class SelfTest
{
    internal static int Run()
    {
        try
        {
            var d=new Diagnostics();
            d.Add(Severity.INFO,"SELFTEST","PRIVACY",
                "password=secret bearer abc123 user=test@example.com");
            var cleaned=d.Snapshot().Single().Line;
            if(cleaned.Contains("password=secret")||
               cleaned.Contains("bearer abc123")||
               cleaned.Contains("test@example.com"))
                throw new Exception("Privacy sanitization failed");
            if(!d.Report().Contains("LIVE ONLY") || d.Json().Contains("simulationOnly"))
                throw new Exception("Diagnostics are not live-only");
            var longLog=new Diagnostics();
            for(int i=0;i<1800;i++)longLog.Add(Severity.INFO,"REPORT","EVENT",new string('x',300));
            longLog.Add(Severity.ERROR,"REPORT","LATEST_ERROR","MOST_RECENT_DIAGNOSTIC");
            if(!longLog.Report().Contains("MOST_RECENT_DIAGNOSTIC"))
                throw new Exception("Copied report must include newest errors beyond preview/export limits");
            if(OfficialGameSessionGate.Readiness(null)!="OFFICIAL_PROTOCOL_ADAPTER_MISSING")
                throw new Exception("Missing official game adapter must not allow bot");
            OfficialWindowTransitionTests.Run();
            OfficialClientWindowHostTests.Run();
            OfficialStartupPolicyTests.Run();
            OfficialLiveObserverTests.Run();
            OfficialF2PWorldTests.Run();
            PublicOsrsHiscoresTests.Run();
            VisualStartupTests.Run();
            AccountGoalPlannerTests.Run();
            GameScreenReaderTests.Run();
            const string manifest="version=0.9.1\n"+
                "exeUrl=https://github.com/Riflex91/osrslive/releases/download/v0.9.1/OSRS-Play.exe\n"+
                "exeSha256=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            if(Updater.ParseManifest(manifest).Version.ToString()!="0.9.1")
                throw new Exception("Updater parser failed");
            Console.WriteLine("AUTO LIVE ONLY PASS: pixel render detection, login ownership, F2P scan, verified bot start gate, diagnostics controls, privacy");
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine("AUTO LIVE ONLY FAIL "+ex);
            return 1;
        }
    }
}
