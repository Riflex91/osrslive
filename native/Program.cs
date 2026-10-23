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
        if(args.Length>0 && args[0]=="--scenario-tests")
        {
            Console.Error.WriteLine("OFFLINE_SESSIONS_REMOVED");
            return 2;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>
            MessageBox.Show(e.Exception.GetType().Name+": "+e.Exception.Message,
                "OSRS Play: UI-Fehler",MessageBoxButtons.OK,MessageBoxIcon.Error);
        try{Application.Run(new MainWindow());return 0;}
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

// One-window, zero-buttons shell. ONLY the unmodified official Jagex
// Launcher and osclient.exe are displayed. We never forge a game session or
// emit inputs while the official state/action adapter is missing.
internal sealed class MainWindow : Form
{
    private readonly Diagnostics log=new();
    private readonly Label heading=new(),status=new(),footer=new();
    private readonly TextBox eventLog=new();
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

    internal MainWindow()
    {
        runtimeLogFile=Path.Combine(logDirectory,
            "last-session.log");
        Text=$"OSRS Play {Diagnostics.Version} – offizielles OSRS";
        Width=1140;Height=800;MinimumSize=new Size(810,580);
        StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);
        BackColor=Color.FromArgb(245,247,250);
        BuildUI();
        windowHost=new OfficialClientWindowHost(gameSurface,log);
        log.Changed+=()=>changePending=true;
        log.SetMode("LIVE ONLY: ORIGINALER JAGEX LAUNCHER UND OSRS");
        log.Add(Severity.INFO,"CORE","STARTED",
            $"version={Diagnostics.Version} mode=LIVE_ONLY controls=ZERO "+
            "game_source=OFFICIAL_JAGEX");
        log.Add(Severity.INFO,"CORE","PRIVACY",
            "credentials_and_oauth_handled_only_by_jagex=true");
        log.Add(Severity.INFO,"LIVE_AGENT","GATED",
            "official_game_state_adapter=MISSING authorized_action_adapter=MISSING "+
            "no_automated_gameplay=true");
        if(Updater.TakeLastOutcome() is string result)
            log.Add(Severity.INFO,"UPDATER","PREVIOUS_UPDATE",result);

        uiTimer.Tick+=(_,_) =>
        {
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
        liveObservationTimer.Tick+=(_,_)=>ObserveOfficialGame();
        liveObservationTimer.Start();
        diagnosticsTimer.Tick+=(_,_) =>
        {
            using var p=Process.GetCurrentProcess();
            log.Add(Severity.DEBUG,"RUNTIME","HEALTH",
                $"osrs_window={(windowHost.Attached?windowHost.CurrentKind.ToString():"none")} "+
                $"render_state={liveObserver.Last.State} "+
                $"frame_sample_age_s={(DateTimeOffset.UtcNow-liveObserver.Last.ObservedUtc).TotalSeconds:0.0} "+
                $"working_set_mib={p.WorkingSet64/1024/1024} "+
                $"uptime_s={uptime.Elapsed.TotalSeconds:0} "+
                "authenticated=false_unverified agent_actions=DISABLED");
            PersistLog();
        };
        diagnosticsTimer.Start();
        Shown+=async (_,_)=>await StartAutomaticallyAsync();
        FormClosing+=(_,_) =>
        {
            stopping=true;followWindows=false;
            closing.Cancel();uiTimer.Stop();diagnosticsTimer.Stop();
            liveObservationTimer.Stop();liveObservationTimer.Dispose();
            windowHost.Dispose();liveObserver.Reset();
            log.Add(Severity.INFO,"CORE","SHUTDOWN",
                "jagex_process_not_terminated=true");
            PersistLog();
            closing.Dispose();
        };
    }

    private void BuildUI()
    {
        var layout=new TableLayoutPanel
        {
            Dock=DockStyle.Fill,Padding=new Padding(11),
            ColumnCount=1,RowCount=5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,180));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        Controls.Add(layout);
        heading.Text=$"OSRS PLAY {Diagnostics.Version}  ·  AUTOMATISCHER LIVE-BETRIEB";
        heading.Dock=DockStyle.Fill;heading.Font=new Font("Segoe UI",13,FontStyle.Bold);
        layout.Controls.Add(heading,0,0);
        status.Dock=DockStyle.Fill;status.Font=new Font("Segoe UI",10);
        status.Text="Starte offizielle Jagex-Anmeldung …";
        layout.Controls.Add(status,0,1);
        gameSurface.Controls.Add(emptyHint);
        layout.Controls.Add(gameSurface,0,2);
        eventLog.Dock=DockStyle.Fill;eventLog.ReadOnly=true;
        eventLog.Multiline=true;eventLog.ScrollBars=ScrollBars.Vertical;
        eventLog.WordWrap=false;
        eventLog.BackColor=Color.FromArgb(18,25,38);
        eventLog.ForeColor=Color.FromArgb(220,230,240);
        eventLog.Font=new Font("Consolas",9);
        layout.Controls.Add(eventLog,0,3);
        footer.Dock=DockStyle.Fill;footer.TextAlign=ContentAlignment.MiddleLeft;
        footer.Text="Keine OSRS-Play-Schaltflächen · Live-Agent wartet auf echte Daten-/Aktionsschnittstelle";
        layout.Controls.Add(footer,0,4);
    }

    private async Task StartAutomaticallyAsync()
    {
        if(starting)return;
        starting=true;
        bool updating=false;
        try
        {
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
                StartOfficialLauncherIfNeeded();
                _=MeasureF2PAutomatically();
            }
        }
    }

    private void ObserveOfficialGame()
    {
        if(stopping || !IsHandleCreated)return;
        var frame=liveObserver.Observe(windowHost.VisibleGameHandle,Handle);
        if(frame.State!=lastReportedRenderState)
        {
            lastReportedRenderState=frame.State;
            log.Add(Severity.INFO,"OFFICIAL_OBSERVER","RENDER_STATE_CHANGED",
                $"state={frame.State} pixels_sampled={frame.SampleCells} "+
                $"variance={frame.LuminanceVariance} changed={frame.ChangedCells} "+
                "origin=VISIBLE_OSCLIENT_FRAME auth_verified=false "+
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
                            "auto_start_requested=true gate="+
                            LiveAgentStartGate.Evaluate(null)+
                            " game_window_visible=true "+
                            "pixel_observation_cannot_verify_login=true");
                        log.Add(Severity.WARN,"LIVE_AGENT","START_BLOCKED",
                            "reason=NO_VERIFIED_OFFICIAL_WORLD_AND_INPUT "+
                            "game_window_visible=true automatic_gameplay=false");
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
                    "reason=NO_WORLD_SELECTION_ADAPTER");
                log.Add(Severity.WARN,"OFFICIAL_CLIENT","AUTO_WORLD_ENTRY_BLOCKED",
                    $"recommended_world={preferredWorld} "+
                    "reason=NO_VERIFIED_OFFICIAL_WORLD_SELECTION_INTERFACE");
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
            "  |  Login: NICHT VERIFIZIERT | Bot: GESPERRT";
        if(!string.IsNullOrEmpty(footer.Text) &&
           footer.Text.Contains("nicht gefunden",StringComparison.OrdinalIgnoreCase))
        {
            // Keep the actionable installation explanation.
        }
        else footer.Text=windowHost.Attached &&
            windowHost.CurrentKind==OfficialWindowKind.Game?
            "Echtes OSRS eingebettet. Weltenwahl/Live-Bot mangels Adapter nicht automatisch möglich.":
            "Jagex-Anmeldung erfolgt im Original-Launcher. OSRS Play fordert kein Passwort an.";
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
        // Developer diagnostics shortcut without visible toolbar/buttons.
        if(keyData==(Keys.Control|Keys.Shift|Keys.C))
        {
            try{Clipboard.SetText(log.Report());}catch{}
            return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }

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
            if(OfficialGameSessionGate.Readiness(null)!="OFFICIAL_PROTOCOL_ADAPTER_MISSING")
                throw new Exception("Missing official game adapter must not allow bot");
            OfficialWindowTransitionTests.Run();
            OfficialClientWindowHostTests.Run();
            OfficialStartupPolicyTests.Run();
            OfficialLiveObserverTests.Run();
            OfficialF2PWorldTests.Run();
            PublicOsrsHiscoresTests.Run();
            const string manifest="version=0.9.1\n"+
                "exeUrl=https://github.com/Riflex91/osrslive/releases/download/v0.9.1/OSRS-Play.exe\n"+
                "exeSha256=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            if(Updater.ParseManifest(manifest).Version.ToString()!="0.9.1")
                throw new Exception("Updater parser failed");
            Console.WriteLine("AUTO LIVE ONLY PASS: pixel render detection, login ownership, F2P scan, verified bot start gate, no buttons, privacy");
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine("AUTO LIVE ONLY FAIL "+ex);
            return 1;
        }
    }
}
