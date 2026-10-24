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
        if(args.Length>0 && args[0]=="--navigation-self-test")return WorldNavigationDataTests.Run().GetAwaiter().GetResult();
        if(args.Length>0 && args[0]=="--third-party-notices")
        {
            var assembly=typeof(Program).Assembly;
            foreach(string name in assembly.GetManifestResourceNames().Where(n=>n.Contains(".Licenses.")))
            {Console.WriteLine(name);using var reader=new StreamReader(assembly.GetManifestResourceStream(name)!);Console.WriteLine(reader.ReadToEnd());}
            return 0;
        }
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
// Launcher and osclient.exe are displayed. Visual gameplay executors share
// the same guarded capture and window-directed input transport.
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
    private readonly System.Windows.Forms.Timer periodicUpdateTimer=new(){Interval=30*60*1000};
    private bool periodicUpdateRunning;
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
    private DateTimeOffset lastLauncherOpenRequest=DateTimeOffset.MinValue;
    private DateTimeOffset lastLauncherRestoreRequest=DateTimeOffset.MinValue;
    private DateTimeOffset launcherCloseRequestTime=DateTimeOffset.MinValue;
    private bool launcherCloseRequested,launcherCloseConfirmed,launcherCloseWarningLogged;
    private bool launcherRestoreFailureLogged;
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
        visualStartup.VisuallyConfirmedSession=CloseLauncherAfterVisualSessionConfirmation;
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
            "startup_input=WINDOW_MESSAGES capture=WINDOWS_GRAPHICS_CAPTURE gameplay_routines=VISUAL_LOCAL_EXECUTORS "+
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
                if(!stopping)
                {
                    ObserveOfficialGame();
                    ReportGameObservation();
                    CloseLauncherAfterVisualSessionConfirmation();
                }
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
                $"uptime_s={uptime.Elapsed.TotalSeconds:0} capture={visualStartup.CaptureState} capture_alpha={visualStartup.CaptureAlpha} "+
                $"authenticated=false_unverified agent_state={visualStartup.Gameplay.State}");
            PersistLog();
        };
        diagnosticsTimer.Start();
        if(startAutomatically&&!observeOnly)
        {
            periodicUpdateTimer.Tick+=async (_,_)=>await CheckForIdleUpdateAsync();
            periodicUpdateTimer.Start();
        }
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
            periodicUpdateTimer.Stop();periodicUpdateTimer.Dispose();
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
            $"activity={visualStartup.Game.Activity?.Method?.Id??"none"} prerequisites={visualStartup.Game.Activity?.State??"none"} "+
            $"recommended_tool={visualStartup.Game.Activity?.RecommendedTool?.Item.Replace(' ','_')??"none"} "+
            $"scope=F2P_PROPOSAL executor={visualStartup.Gameplay.State}";
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
        footer.Text="Live-Agent wartet auf konsistente Skills und sichtbare Interaktionen";
        var toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1};
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        var jobButton=new Button{Text="Aufgabe …",AutoSize=true};
        jobButton.Click+=(_,_)=>
        {
            visualStartup.Paused=true;changePending=true;
            using var dialog=new AgentJobDialog();
            if(dialog.ShowDialog(this)==DialogResult.OK)visualStartup.Gameplay.Reset();
        };
        toolbar.Controls.Add(jobButton,4,0);
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
        // Explicit outer bounds keep Windows' screen-dependent tracking limit from
        // silently shrinking the dock on a small desktop (including CI runners).
        var desiredClient=DockedWindowLayout.ClientSize(logRequestedVisible);
        MinimumSize=Size.Empty;MaximumSize=Size.Empty;
        var fixedOuter=SizeFromClientSize(desiredClient);
        MaximumSize=fixedOuter;MinimumSize=fixedOuter;
        ClientSize=desiredClient;
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
            throw new Exception($"Hiding log resized the game instead of removing the dock: viewport={gameSurface.ClientSize} client={ClientSize} shownHeight={shownHeight}");
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

    private async Task CheckForIdleUpdateAsync()
    {
        if(periodicUpdateRunning||stopping||!followWindows||visualStartup.ObserveOnly)return;
        // A running OSRS or Jagex login session must not be interrupted by an
        // unattended release. Check again when the client is closed or on the
        // next application start (which already checks before launching).
        if(windowHost.Attached||visualTickRunning||
           OfficialClientWindowHost.FindRunningClients().Count>0||
           OfficialClientWindowHost.LauncherProcessRunning())
        {
            log.Add(Severity.INFO,"UPDATER","PERIODIC_UPDATE_DEFERRED",
                "official_client_or_launcher_active=true retry_in_minutes=30 next_start_checks=true");
            return;
        }
        periodicUpdateRunning=true;
        try
        {
            log.Add(Severity.INFO,"UPDATER","PERIODIC_CHECK_START",
                "no_official_session=true verified_release_only=true");
            bool updated=await Updater.CheckAndUpdateAsync(message=>
                log.Add(Severity.INFO,"UPDATER","STATUS",message),closing.Token);
            if(updated&&!stopping)
            {
                log.Add(Severity.INFO,"UPDATER","RESTART_REQUESTED",
                    "reason=NEW_VERIFIED_RELEASE_IDLE_SESSION official_session_active=false");
                Close();
            }
        }
        catch(OperationCanceledException) when(stopping){}
        catch(Exception ex)
        {
            log.Add(Severity.WARN,"UPDATER","PERIODIC_CHECK_FAILED",
                "reason="+ex.GetType().Name);
        }
        finally{periodicUpdateRunning=false;}
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
        if(stopping||visualStartup.ObserveOnly||windowHost.Attached)return;
        try
        {
            var games=OfficialClientWindowHost.FindRunningClients();
            var launchers=OfficialClientWindowHost.FindRunningLauncherWindows();
            if(games.Count>0||launchers.Count>0)
            {
                FollowOfficialWindows();
                return;
            }
            // A launcher may already be running in the tray without a usable
            // top-level HWND. Reopening its official executable normally asks
            // the single-instance launcher to show its UI.
            if(DateTimeOffset.UtcNow-lastLauncherOpenRequest<TimeSpan.FromSeconds(25))return;
            lastLauncherOpenRequest=DateTimeOffset.UtcNow;
            bool running=OfficialClientWindowHost.LauncherProcessRunning();
            log.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_AVAILABILITY_CHECK",
                $"games={games.Count} windows={launchers.Count} process_running={running}");
            if(OfficialClientWindowHost.TryOpenJagexLauncher())
            {
                log.Add(Severity.INFO,"OFFICIAL_CLIENT",
                    running?"LAUNCHER_REOPEN_REQUESTED":"LAUNCHER_START_REQUESTED",
                    "official_executable=true credentials_handled_by_jagex=true");
            }
            else
            {
                log.Add(Severity.WARN,"OFFICIAL_CLIENT","LAUNCHER_NOT_FOUND",
                    $"running_process={running} official_installation_path_missing=true");
                footer.Text="Jagex Launcher nicht gefunden – offizielle Installation erforderlich.";
            }
        }
        catch(Exception ex)
        {
            log.Add(Severity.WARN,"OFFICIAL_CLIENT","AUTO_START_FAILED",
                "reason="+ex.GetType().Name);
            footer.Text="Jagex Launcher konnte nicht automatisch geöffnet werden.";
        }
    }

    private void EnsureLauncherRestored(OfficialClientWindow launcher)
    {
        if(visualStartup.ObserveOnly)return;
        bool minimized=OfficialClientWindowHost.LauncherIsMinimized(launcher);
        if(minimized&&DateTimeOffset.UtcNow-lastLauncherRestoreRequest<TimeSpan.FromSeconds(5))return;
        if(minimized)lastLauncherRestoreRequest=DateTimeOffset.UtcNow;
        bool success=OfficialClientWindowHost.TryRestoreLauncher(launcher);
        if(success)
        {
            launcherRestoreFailureLogged=false;
            if(minimized)log.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_RESTORE_REQUESTED",
                $"pid={launcher.ProcessId} reason=MINIMIZED accessibility_requires_visible_button=true");
        }
        else if(!launcherRestoreFailureLogged)
        {
            launcherRestoreFailureLogged=true;
            log.Add(Severity.WARN,"OFFICIAL_CLIENT","LAUNCHER_RESTORE_FAILED",
                $"pid={launcher.ProcessId} valid_official_window_required=true");
        }
    }

    private void FollowOfficialWindows()
    {
        if(stopping || windowHost is null)return;
        if(windowHost.Attached && windowHost.CurrentKind==OfficialWindowKind.Game)
            return; // Embedded game is absent from top-level enumeration.
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
                    launcherCloseRequested=false;
                    launcherCloseConfirmed=false;
                    launcherCloseWarningLogged=false;
                    log.Add(Severity.INFO,"OFFICIAL_CLIENT","GAME_WINDOW_READY",
                        $"pid={chosen.ProcessId} kind=Game official_osrs=true "+
                        "login_state=UNVERIFIED world_selection=NOT_VERIFIED");
                    liveObserver.Reset();
                    if(!loggedBlockedAgent)
                    {
                        loggedBlockedAgent=true;
                        log.Add(Severity.INFO,"LIVE_AGENT","AUTO_START_EVALUATED",
                            "goal=MAX_ALL_SKILLS_THEN_WEALTH_AND_GEAR "+
                            "state=WAITING_FOR_CONSISTENT_SKILLS_AND_VISIBLE_ACTIONS "+
                            "game_window_visible=true pixel_observation_cannot_verify_login=true");
                        log.Add(Severity.INFO,"VISUAL_STARTUP","AUTO_START",
                            $"input_enabled={!visualStartup.ObserveOnly} transport=WINDOW_MESSAGES "+
                            "gameplay_routines=VISUAL_LOCAL_EXECUTORS F8=PAUSE");
                    }
                    changePending=true;
                }
                return;
            }
            if(windowHost.Attached && windowHost.CurrentKind==OfficialWindowKind.Launcher)
            {
                var tracked=OfficialClientWindowHost.FindRunningLauncherWindows()
                    .FirstOrDefault(c=>c.Handle==windowHost.VisibleWindowHandle);
                if(tracked is not null)EnsureLauncherRestored(tracked);
                return;
            }
            var launchers=OfficialClientWindowHost.FindRunningLauncherWindows();
            if(launchers.Count>0)
            {
                if(launchers.Count>1)
                    log.Add(Severity.WARN,"OFFICIAL_CLIENT","MULTIPLE_LAUNCHERS",
                        $"count={launchers.Count} selecting_lowest_pid=true");
                var chosen=launchers.OrderBy(c=>c.ProcessId).First();
                if(windowHost.Attach(chosen))
                {
                    emptyHint.Visible=true;
                    emptyHint.Text="Jagex Launcher wird im Originalfenster gesteuert.\n\nWarte auf den offiziellen Spielclient …";
                    log.Add(Severity.INFO,"OFFICIAL_CLIENT","LOGIN_WINDOW_READY",
                        $"pid={chosen.ProcessId} login=JAGEX "+
                        "automatic_window_tracking=true embedding=false original_size_preserved=true");
                    EnsureLauncherRestored(chosen);
                    changePending=true;
                }
            }
            else StartOfficialLauncherIfNeeded();
        }
        catch(Exception ex)
        {
            log.Add(Severity.WARN,"OFFICIAL_CLIENT","AUTO_WINDOW_ATTACH_FAILED",
                "reason="+ex.GetType().Name);
        }
    }

    private void CloseLauncherAfterVisualSessionConfirmation()
    {
        if(visualStartup.ObserveOnly||stopping||!windowHost.Attached||
           windowHost.CurrentKind!=OfficialWindowKind.Game)return;
        // Once WM_CLOSE was sent, checking whether its HWND disappears must
        // not depend on the Skills panel remaining visible in later frames.
        if(!launcherCloseRequested &&
           !LauncherLifecyclePolicy.MayClose(windowHost.CurrentKind,
               visualStartup.Game.Confirmed,DateTimeOffset.UtcNow))return;
        if(!launcherCloseRequested)
        {
            launcherCloseRequested=true;
            launcherCloseRequestTime=DateTimeOffset.UtcNow;
            var result=windowHost.RequestTrackedLauncherClose();
            log.Add(result==LauncherCloseResult.Failed||result==LauncherCloseResult.WindowMismatch?
                Severity.WARN:Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_CLOSE_AFTER_VISUAL_LOGIN",
                $"result={result} character_and_24_skills_consistent=true "+
                "server_auth_verified=false force_terminate=false");
            launcherCloseConfirmed=result is LauncherCloseResult.NotTracked or LauncherCloseResult.AlreadyClosed or
                LauncherCloseResult.Failed or LauncherCloseResult.WindowMismatch;
            return;
        }
        if(launcherCloseConfirmed)return;
        if(!windowHost.TrackedLauncherWindowOpen)
        {
            launcherCloseConfirmed=true;
            log.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_WINDOW_CLOSED_CONFIRMED",
                "window_gone=true process_exit_not_assumed=true official_game_retained=true");
        }
        else if(!launcherCloseWarningLogged &&
                DateTimeOffset.UtcNow-launcherCloseRequestTime>TimeSpan.FromSeconds(10))
        {
            launcherCloseWarningLogged=true;
            log.Add(Severity.WARN,"OFFICIAL_CLIENT","LAUNCHER_CLOSE_NOT_CONFIRMED",
                "launcher_window_still_present=true no_forced_termination=true");
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
            "JAGEX LAUNCHER IM ORIGINALFENSTER":
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
            "Visuelle Routinen: "+visualStartup.Gameplay.State+" | F8 pausiert. Live-Prüfung ausstehend.":
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
            WelcomePlayButtonReader.SelfTest();
            PostPlayScreenReader.SelfTest();
            NativeWelcomePanelReader.SelfTest();
            NativeWorldDigitReader.SelfTest();
            AccountGoalPlannerTests.Run();
            F2PProgressionTests.Run();
            ExecutionRoutinesTests.Run();
            CombatNavigationTests.Run();
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
