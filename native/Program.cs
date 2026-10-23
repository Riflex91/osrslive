using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO.Compression;
using System.Text;

namespace OSRSLiveDiagnostics;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--apply-update") return Updater.Apply(args);
        if(args.Length>0 && args[0]=="--self-test") return SelfTest.Run();
        if(args.Length>0 && args[0]=="--scenario-tests")
        {
            Console.Error.WriteLine("OFFLINE_SESSIONS_REMOVED");
            return 2;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_,e)=>
            MessageBox.Show(e.Exception.GetType().Name+": "+e.Exception.Message,"OSRS Play: UI-Fehler");
        try { Application.Run(new MainWindow()); return 0; }
        catch(Exception e)
        {
            try
            {
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSLiveDiagnostics","fatal.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path,DateTimeOffset.Now+" "+e+Environment.NewLine);
            }catch{}
            MessageBox.Show("Start fehlgeschlagen: "+e.Message,"OSRS Play",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal sealed class MainWindow : Form
{
    private readonly Diagnostics core=new();
    private readonly Label header=new(), stats=new(), footer=new();
    private Button botButton=new(),pingButton=new(),hiscoresButton=new();
    private readonly CancellationTokenSource accountReadCancel=new();
    private bool accountReadBusy;
    private readonly CancellationTokenSource pingCancel=new();
    private bool pingScanning;
    private string f2pRecommendation="nicht gemessen";
    private readonly TextBox log=new(), search=new();
    private readonly ComboBox filter=new();
    private readonly CheckBox pause=new();
    private readonly TabControl gameTabs=new(){Dock=DockStyle.Fill};
    private readonly TabPage officialPage=new("Offizielles OSRS (echtes Spiel)");
    private readonly Panel officialSurface=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(17,24,39)};
    private readonly Label officialHint=new()
    {
        Dock=DockStyle.Fill,ForeColor=Color.WhiteSmoke,BackColor=Color.FromArgb(17,24,39),
        TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",11),
        Text="OSRS PLAY: LOGIN UND SPIEL IN EINEM FENSTER\n\n"+
            "1. 'Anmeldung starten' öffnet den offiziellen Jagex Launcher hier.\n"+
            "2. Hier beim offiziellen Jagex Launcher anmelden und Official Client wählen.\n"+
            "3. 'Play Now' startet das echte Spiel; OSRS Play übernimmt dessen Fenster.\n\n"+
            "Eventuell separat geöffnete Sicherheits-/Browserfenster gehören Jagex.\n"+
            "Der Live-Bot ist noch nicht mit dem offiziellen Spiel verbunden."
    };
    private OfficialClientWindowHost officialHost=null!;
    private Button launcherButton=new(),attachButton=new(),detachButton=new();
    private bool autoHostOfficial;
    private bool startupLoginAttempted;
    private long lastHostPollMs;
    private readonly System.Windows.Forms.Timer refreshTimer=new(){Interval=250};
    private readonly System.Windows.Forms.Timer metricsTimer=new(){Interval=5000};
    private volatile bool refreshNeeded=true;
    private readonly Stopwatch eventClock=Stopwatch.StartNew();
    private TimeSpan lastPerformanceElapsed=TimeSpan.Zero;
    private TimeSpan lastCpuTime=Process.GetCurrentProcess().TotalProcessorTime;
    internal MainWindow()
    {
        core.Changed+=()=>refreshNeeded=true;
        Text=$"OSRS Play v{Diagnostics.Version}";
        Width=1150; Height=800; MinimumSize=new Size(880,650);
        StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);
        BackColor=Color.FromArgb(245,247,250);
        InitializeUI();
        officialHost=new OfficialClientWindowHost(officialSurface,core);
        RefreshGameControls();
        core.SetMode("LIVE ONLY: JAGEX LAUNCHER / OFFIZIELLER OSRS-CLIENT");
        core.Add(Severity.INFO,"CORE","LIVE_ONLY",
            "offline_sessions=REMOVED sandbox=REMOVED simulator=REMOVED "+
            "live_game_adapter=NOT_IMPLEMENTED");
        core.Add(Severity.INFO,"OFFICIAL_CLIENT","HOST_READY",
            "official_osclient_window_supported=true authentication=JAGEX_LAUNCHER "+
            "live_bot_adapter=false no_simulation=true");
        core.Add(Severity.INFO,"CORE","STARTED",$"Version {Diagnostics.Version} | Live-only host for official Jagex client");
        core.Add(Severity.INFO,"CORE","PRIVACY","Keine Zugangsdaten oder Tokens im Export");
        if(Updater.TakeLastOutcome() is string outcome)
            core.Add(Severity.INFO,"UPDATER","PREVIOUS_UPDATE",outcome);
        refreshTimer.Tick+=(_,_)=>
        {
            if(autoHostOfficial && gameTabs.SelectedTab==officialPage &&
               eventClock.ElapsedMilliseconds-lastHostPollMs>1100)
            {
                lastHostPollMs=eventClock.ElapsedMilliseconds;
                TryHostOfficialWindows(false);
            }
            if(officialHost is not null && officialPage is not null && officialHost.Attached)
                officialHost.Resize();
            if(refreshNeeded){refreshNeeded=false;UpdateView();}
        };
        refreshTimer.Start();
        metricsTimer.Tick+=(_,_)=>
        {
            using var p=Process.GetCurrentProcess();
            var now=eventClock.Elapsed;
            var cpuNow=p.TotalProcessorTime;
            var intervalMs=Math.Max(1d,(now-lastPerformanceElapsed).TotalMilliseconds);
            var cpuPct=Math.Clamp((cpuNow-lastCpuTime).TotalMilliseconds*100d/
                (intervalMs*Math.Max(1,Environment.ProcessorCount)),0,100);
            var timerDelayMs=Math.Max(0d,intervalMs-metricsTimer.Interval);
            lastCpuTime=cpuNow;
            lastPerformanceElapsed=now;
            core.Add(Severity.DEBUG,"RUNTIME","PERF_SAMPLE",
                $"working_set_mib={p.WorkingSet64/1024/1024} threads={p.Threads.Count} "+
                $"cpu_pct={cpuPct.ToString("0.0",CultureInfo.InvariantCulture)} "+
                $"ui_timer_lag_ms={timerDelayMs.ToString("0",CultureInfo.InvariantCulture)} "+
                $"uptime_s={now.TotalSeconds:0}");
            if(timerDelayMs>1000)
                core.Add(Severity.WARN,"RUNTIME","UI_TIMER_DELAY",
                    $"UI-Timer mehr als 1000 ms verspätet: {timerDelayMs:0} ms");
        };
        metricsTimer.Start();
        Shown+=async (_,_)=>
        {
            // Open the actual Jagex authentication UI automatically on first
            // display. Previous releases only initialized the host and the
            // updater, so a manual button click was always required.
            StartOfficialOnStartup();
            core.Add(Severity.INFO,"UPDATER","CHECK_START","Automatische GitHub-Updateprüfung");
            var changed=await Updater.CheckAndUpdateAsync(msg=>
            {
                core.Add(Severity.INFO,"UPDATER","STATUS",msg);
                if(!IsDisposed && IsHandleCreated) BeginInvoke(()=>footer.Text=msg);
            });
            if(changed)
            {
                core.Add(Severity.INFO,"UPDATER","RESTART_REQUESTED","Verifiziertes Update; starte Hilfsprozess.");
                Close();
            }
            else core.Add(Severity.INFO,"UPDATER","CHECK_FINISHED",
                "Keine Neustartanforderung; Ergebnis in UPDATER STATUS nachlesen.");
        };
        FormClosing+=(_,_)=>{accountReadCancel.Cancel();pingCancel.Cancel();
            officialHost?.Dispose();metricsTimer.Stop();refreshTimer.Stop();
            pingCancel.Dispose();accountReadCancel.Dispose();};
    }
    private Button Button(string label,Action action)
    {
        var button=new Button {Text=label,AutoSize=true,Height=34,Padding=new Padding(7,0,7,0),Margin=new Padding(3)};
        button.Click+=(_,_)=>action();return button;
    }
    private void InitializeUI()
    {
        var outer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(14)};
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        Controls.Add(outer);
        header.Text=$"OSRS PLAY  ·  {Diagnostics.Version}  ·  LIVE ONLY / OFFIZIELLES OSRS";
        header.Font=new Font("Segoe UI",14,FontStyle.Bold);header.Dock=DockStyle.Fill;
        outer.Controls.Add(header,0,0);
        stats.Dock=DockStyle.Fill; stats.Font=new Font("Segoe UI",10);
        outer.Controls.Add(stats,0,1);
        var actionBar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
        botButton=Button("Live-Bot: Schnittstelle fehlt",ToggleBot);
        botButton.Enabled=false;
        pingButton=Button("F2P-Ping testen",TestF2PWorlds);
        hiscoresButton=Button("Echte OSRS-Skills",ReadPublicHiscores);
        launcherButton=Button("Anmeldung starten",OpenJagexLogin);
        attachButton=Button("Login/Spiel verbinden",AttachOfficialClient);
        detachButton=Button("Fenster freigeben",DetachOfficialClient);
        actionBar.Controls.AddRange(new Control[]{launcherButton,attachButton,detachButton,
            botButton,pingButton,hiscoresButton,
            Button("Log kopieren",CopyLog),Button("JSON exportieren",()=>Export(false)),
            Button("ZIP exportieren",()=>Export(true)),Button("Puffer leeren",()=>core.Clear())});
        outer.Controls.Add(actionBar,0,2);
        var content=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
        content.RowStyles.Add(new RowStyle(SizeType.Percent,58));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,42));
        gameTabs.TabPages.Add(officialPage);
        officialPage.Controls.Add(officialSurface);
        officialSurface.Controls.Add(officialHint);
        gameTabs.SelectedTab=officialPage;
        content.Controls.Add(gameTabs,0,0);
        var filters=new FlowLayoutPanel {Dock=DockStyle.Fill};
        filters.Controls.Add(new Label{Text="Filter:",AutoSize=true,TextAlign=ContentAlignment.MiddleCenter,Padding=new Padding(0,7,0,0)});
        filter.Items.AddRange(new object[]{"ALLE","INFO +","WARN +","NUR FEHLER"});filter.SelectedIndex=0;
        filter.Width=110;filter.SelectedIndexChanged+=(_,_)=>refreshNeeded=true;
        filters.Controls.Add(filter);
        filters.Controls.Add(new Label{Text="Suche:",AutoSize=true,Padding=new Padding(0,7,0,0)});
        search.Width=190;search.TextChanged+=(_,_)=>refreshNeeded=true;filters.Controls.Add(search);
        pause.Text="Ansicht pausieren";pause.AutoSize=true;
        pause.CheckedChanged+=(_,_)=>refreshNeeded=true;filters.Controls.Add(pause);
        content.Controls.Add(filters,0,1);
        log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Both;log.WordWrap=false;
        log.Dock=DockStyle.Fill;log.BackColor=Color.FromArgb(17,24,39);log.ForeColor=Color.FromArgb(219,234,254);
        log.Font=new Font("Consolas",9);
        content.Controls.Add(log,0,2);
        outer.Controls.Add(content,0,3);
        footer.Text="Live-only: Jagex Launcher und offizielles OSRS. Keine Offline-Sitzungen.";
        footer.Dock=DockStyle.Fill;footer.TextAlign=ContentAlignment.MiddleLeft;outer.Controls.Add(footer,0,4);
    }
    private void RefreshGameControls()
    {
        botButton.Enabled=false;
        botButton.Text="Live-Bot: Schnittstelle fehlt";
        footer.Text=officialHost is not null && officialHost.Attached?
            officialHost.CurrentKind==OfficialWindowKind.Launcher?
                "Originaler Jagex Launcher eingebettet. Hier anmelden und Play Now wählen.":
                "Echtes OSRS-Spiel eingebettet. Live-Bot derzeit nicht verfügbar.":
            "Anmeldung starten: offizieller Jagex Launcher und Spiel im selben Fenster.";
    }
    private void StartOfficialOnStartup()
    {
        if(startupLoginAttempted || IsDisposed)return;
        startupLoginAttempted=true;
        autoHostOfficial=true;
        try
        {
            var gameWindows=OfficialClientWindowHost.FindRunningClients();
            var launcherWindows=OfficialClientWindowHost.FindRunningLauncherWindows();
            var action=OfficialStartupPolicy.Decide(
                officialHost.Attached,gameWindows.Count,launcherWindows.Count);
            core.Add(Severity.INFO,"OFFICIAL_CLIENT","AUTO_LOGIN_START",
                $"action={action} official_games={gameWindows.Count} "+
                $"launcher_windows={launcherWindows.Count} "+
                "credentials_managed_by_jagex=true");
            switch(action)
            {
                case OfficialStartupAction.KeepAttached:
                    core.Add(Severity.INFO,"OFFICIAL_CLIENT","AUTO_EXISTING_ATTACHED",
                        "official_window_preserved=true");
                    break;
                case OfficialStartupAction.AttachExisting:
                    if(!TryHostOfficialWindows(false))
                    {
                        core.Add(Severity.WARN,"OFFICIAL_CLIENT","AUTO_ATTACH_FAILED",
                            "official_process_found=true user_can_choose_window_manually=true");
                        footer.Text="Jagex-Fenster erkannt, automatische Einbettung fehlgeschlagen. "+
                            "Bitte 'Login/Spiel verbinden' wählen.";
                    }
                    break;
                case OfficialStartupAction.AskUserToChoose:
                    core.Add(Severity.INFO,"OFFICIAL_CLIENT","AUTO_SELECTION_REQUIRED",
                        "multiple_official_windows=true duplicate_launcher_prevented=true");
                    footer.Text="Mehrere offizielle OSRS-/Jagex-Fenster gefunden. "+
                        "Bitte 'Login/Spiel verbinden' wählen.";
                    break;
                case OfficialStartupAction.StartLauncher:
                    if(OfficialClientWindowHost.TryOpenJagexLauncher())
                    {
                        core.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_START_REQUESTED",
                            "trigger=AUTOMATIC_STARTUP credentials_managed_by_jagex=true");
                        footer.Text="Jagex Launcher wird automatisch geöffnet und hier eingebettet …";
                        TryHostOfficialWindows(false);
                    }
                    else
                    {
                        core.Add(Severity.WARN,"OFFICIAL_CLIENT","LAUNCHER_NOT_FOUND",
                            "trigger=AUTOMATIC_STARTUP official_installation_required=true");
                        footer.Text="Jagex Launcher nicht gefunden. Bitte installieren "+
                            "oder auf 'Anmeldung starten' klicken.";
                    }
                    break;
            }
        }
        catch(Exception ex)
        {
            core.Add(Severity.ERROR,"OFFICIAL_CLIENT","AUTO_LOGIN_FAILED",
                "reason="+ex.GetType().Name+" manual_login_button_available=true");
            footer.Text="Automatischer Jagex-Start fehlgeschlagen. "+
                "Bitte 'Anmeldung starten' versuchen.";
        }
    }
    private void OpenJagexLogin()
    {
        gameTabs.SelectedTab=officialPage;
        autoHostOfficial=true;
        try
        {
            // Attaching an existing session takes priority over launching another.
            if(TryHostOfficialWindows(true))return;
            bool launched=OfficialClientWindowHost.TryOpenJagexLauncher();
            if(!launched)
            {
                autoHostOfficial=false;
                var answer=MessageBox.Show(this,
                    "Der offizielle Jagex Launcher ist nicht installiert oder konnte nicht "+
                    "gefunden werden. Die offizielle Downloadseite öffnen?",
                    "Jagex Launcher fehlt",MessageBoxButtons.YesNo,MessageBoxIcon.Information);
                if(answer==DialogResult.Yes)
                    Process.Start(new ProcessStartInfo("https://www.jagex.com/en-GB/launcher")
                        {UseShellExecute=true});
                return;
            }
            core.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_STARTED",
                "auto_window_host=true credentials_handled_by_jagex=true "+
                "tokens_never_read=true");
            footer.Text="Jagex Launcher startet – dessen Fenster wird automatisch hier eingebettet.";
            TryHostOfficialWindows(false);
        }
        catch(Exception ex)
        {
            autoHostOfficial=false;
            core.Add(Severity.ERROR,"OFFICIAL_CLIENT","LAUNCHER_FAILED",ex.GetType().Name);
            footer.Text="Jagex Launcher konnte nicht gestartet werden; Installation überprüfen.";
        }
    }
    private void AttachOfficialClient()
    {
        gameTabs.SelectedTab=officialPage;
        autoHostOfficial=true;
        if(!TryHostOfficialWindows(true))
            footer.Text="Noch kein offizielles Launcher-/Spielfenster sichtbar. Anmeldung starten.";
    }
    private bool TryHostOfficialWindows(bool userInitiated)
    {
        if(officialHost is null || gameTabs.SelectedTab!=officialPage)return false;
        // A parented osclient.exe is no longer a top-level window.
        // KEEP the existing game rather than alternately reattaching the launcher.
        var existing=officialHost.Attached?officialHost.CurrentKind:null;
        if(OfficialWindowTransition.Decide(existing,false,false)==
           OfficialWindowDecision.KeepGame)
            return true;
        // Only enumerate top-level game windows while we are not already
        // hosting one. The transition from Launcher -> Game is one-way.
        var clients=OfficialClientWindowHost.FindRunningClients();
        if(OfficialWindowTransition.Decide(existing,clients.Count>0,false)==
           OfficialWindowDecision.AttachGame)
        {
            var selected=PickOfficialWindow(clients,userInitiated);
            if(selected is null)return false;
            if(officialHost.Attach(selected))
            {
                officialHint.Visible=false;
                core.Add(Severity.INFO,"OFFICIAL_CLIENT","GAME_WINDOW_READY",
                    "window=OFFICIAL_OSRS origin=JAGEX_LAUNCHER "+
                    "login_status=NOT_VERIFIABLE live_agent=false");
                RefreshGameControls();
                return true;
            }
            if(userInitiated)footer.Text="Das offizielle Spielfenster ließ sich nicht einbetten (Windows-/DPI-Konflikt).";
            return false;
        }
        if(OfficialWindowTransition.Decide(existing,false,false)==
           OfficialWindowDecision.KeepLauncher)
            return true;
        var launchers=OfficialClientWindowHost.FindRunningLauncherWindows();
        if(OfficialWindowTransition.Decide(existing,false,launchers.Count>0)!=
           OfficialWindowDecision.AttachLauncher)
        {
            officialHint.Visible=true;
            return false;
        }
        var launcher=PickOfficialWindow(launchers,userInitiated);
        if(launcher is null)return false;
        if(officialHost.Attach(launcher))
        {
            officialHint.Visible=false;
            core.Add(Severity.INFO,"OFFICIAL_CLIENT","LOGIN_WINDOW_READY",
                "window=JAGEX_LAUNCHER embedded=true "+
                "oauth_prompts_may_open_external_browser=true "+
                "credentials_never_accessed_by_osrs_play=true");
            RefreshGameControls();
            return true;
        }
        if(userInitiated)footer.Text="Jagex Launcher konnte nicht im Fenster eingebettet werden.";
        return false;
    }
    private OfficialClientWindow? PickOfficialWindow(
        IReadOnlyList<OfficialClientWindow> available,bool userInitiated)
    {
        if(available.Count==1)return available[0];
        if(!userInitiated)
        {
            footer.Text="Mehrere offizielle Fenster erkannt. Bitte 'Login/Spiel verbinden' zur Auswahl nutzen.";
            return null;
        }
        using var chooser=new Form
        {
            Text="Offizielles Jagex-/OSRS-Fenster wählen",
            Width=570,Height=200,StartPosition=FormStartPosition.CenterParent,
            FormBorderStyle=FormBorderStyle.FixedDialog,
            MinimizeBox=false,MaximizeBox=false
        };
        var options=new ComboBox{Left=14,Top=18,Width=530,
            DropDownStyle=ComboBoxStyle.DropDownList};
        foreach(var w in available)
            options.Items.Add($"PID {w.ProcessId} – {w.Title}");
        options.SelectedIndex=0;
        var accept=new Button{Text="Einbetten",Left=420,Top=70,Width=120,
            DialogResult=DialogResult.OK};
        chooser.Controls.AddRange(new Control[]{options,accept});
        chooser.AcceptButton=accept;
        return chooser.ShowDialog(this)==DialogResult.OK?
            available[options.SelectedIndex]:null;
    }
    private void DetachOfficialClient()
    {
        autoHostOfficial=false;
        officialHost.Detach();
        officialHint.Visible=true;
        core.Add(Severity.INFO,"OFFICIAL_CLIENT","DETACH_REQUESTED",
            "official_process_remains_running=true credentials_not_handled=true");
        RefreshGameControls();
    }
    private void ToggleBot()
    {
        core.Add(Severity.WARN,"OFFICIAL_CLIENT","LIVE_AGENT_UNAVAILABLE",
            "reason=verified_official_world_state_and_authorized_actions_missing");
        footer.Text="Live-Bot nicht verfügbar – keine Offline-Ausweichsteuerung.";
    }
    private async void ReadPublicHiscores()
    {
        if(accountReadBusy)return;
        using var form=new Form
        {
            Text="OSRS Play – öffentliche Skilldaten",Size=new Size(435,185),
            StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,
            MinimizeBox=false,MaximizeBox=false
        };
        var info=new Label{Left=16,Top=12,Width=390,Height=37,
            Text="OSRS-Anzeigename (öffentlich) eingeben. Niemals Passwort oder Jagex-Token."};
        var name=new TextBox{Left=16,Top=54,Width=390,MaxLength=12};
        var ok=new Button{Text="Skilldaten laden",Left=186,Top=91,Width=125,DialogResult=DialogResult.OK};
        var cancel=new Button{Text="Abbrechen",Left=316,Top=91,Width=90,DialogResult=DialogResult.Cancel};
        form.Controls.AddRange(new Control[]{info,name,ok,cancel});
        form.AcceptButton=ok;form.CancelButton=cancel;
        if(form.ShowDialog(this)!=DialogResult.OK)return;
        if(!PublicOsrsHiscores.ValidName(name.Text.Trim()))
        {
            footer.Text="Ungültiger OSRS-Anzeigename. Nur Buchstaben, Ziffern, Leerzeichen, - und _.";
            return;
        }
        accountReadBusy=true;
        hiscoresButton.Enabled=false;
        core.Add(Severity.INFO,"OFFICIAL_PUBLIC","HISCORES_QUERY",
            "host=secure.runescape.com source=PUBLIC_HISCORES user_identity_redacted=true "+
            "public_read_only=true official_game_login=false");
        try
        {
            var profile=await PublicOsrsHiscores.FetchAsync(name.Text.Trim(),accountReadCancel.Token);
            var mining=profile.Skills.Single(x=>x.Name=="Mining");
            core.Add(Severity.INFO,"OFFICIAL_PUBLIC","HISCORES_RESULT",
                $"total_level={profile.TotalLevel} skills={profile.Skills.Count} "+
                $"mining_level={mining.Level} mining_xp={mining.Experience} "+
                "origin=JAGEX_PUBLIC_HISCORES authenticated_game_session=false "+
                "game_input=false public_data_only=true");
            footer.Text=$"Offizielle öffentliche Skills: Gesamtlevel {profile.TotalLevel}, "+
                $"Mining {mining.Level} ({mining.Experience} XP). Kein Spiel-Login.";
        }
        catch(OperationCanceledException)
        {
            core.Add(Severity.INFO,"OFFICIAL_PUBLIC","QUERY_CANCELLED","window_shutdown=true");
        }
        catch(Exception ex)
        {
            core.Add(Severity.WARN,"OFFICIAL_PUBLIC","QUERY_FAILED",
                ex.GetType().Name+"; no_live_game_connection=true");
            if(!IsDisposed)footer.Text="Öffentliche OSRS-Skilldaten konnten nicht gelesen werden.";
        }
        finally
        {
            accountReadBusy=false;
            if(!IsDisposed)hiscoresButton.Enabled=true;
        }
    }
    private async void TestF2PWorlds()
    {
        if(pingScanning)return;
        pingScanning=true;
        pingButton.Enabled=false;
        footer.Text="Offizielle kostenlose Standardwelten laden und TCP-Ping auf diesem PC messen …";
        core.Add(Severity.INFO,"F2P_PING","SCAN_START",
            "source=OFFICIAL_JAGEX_WORLDLIST free_only=true unrestricted_only=true "+
            "probe=TCP_CONNECT port=43594 no_login=true no_game_packets=true");
        try
        {
            var result=await OfficialF2PWorldScanner.ScanAsync(pingCancel.Token);
            core.Add(Severity.INFO,"F2P_PING","CATALOG_CHECKED",
                $"eligible={result.Eligible} reachable={result.Reachable} official_url={result.Source}");
            if(result.Fastest.Count==0)
            {
                f2pRecommendation="keine Welt erreichbar";
                core.Add(Severity.WARN,"F2P_PING","NO_REACHABLE_WORLDS",
                    "none_selected=true official_game_login_unavailable=true");
                footer.Text="Kein F2P-Server erreichbar. Keine Welt wurde gewählt.";
            }
            else
            {
                var best=result.Fastest[0];
                f2pRecommendation=$"W{best.World.WorldNumber} {best.Milliseconds:0.0} ms";
                foreach(var candidate in result.Fastest.Take(5))
                    core.Add(Severity.INFO,"F2P_PING","CANDIDATE",
                        $"world={candidate.World.WorldNumber} "+
                        $"location={candidate.World.Location} "+
                        $"tcp_latency_ms={candidate.Milliseconds.ToString("0.0",CultureInfo.InvariantCulture)} "+
                        $"samples={candidate.Samples} free=true general_purpose=true");
                core.Add(Severity.INFO,"F2P_PING","BEST_WORLD",
                    $"world={best.World.WorldNumber} "+
                    $"tcp_latency_ms={best.Milliseconds.ToString("0.0",CultureInfo.InvariantCulture)} "+
                    "measurement=THIS_WINDOWS_PC connection=PROBE_ONLY "+
                    "official_login=false auto_game_connection=false");
                footer.Text=$"F2P-Empfehlung: W{best.World.WorldNumber} "+
                    $"({best.Milliseconds:0.0} ms TCP); nur Ping, kein OSRS-Login.";
            }
            refreshNeeded=true;
        }
        catch(OperationCanceledException)
        {
            core.Add(Severity.INFO,"F2P_PING","SCAN_CANCELLED","user_or_app_shutdown=true");
        }
        catch(Exception ex)
        {
            f2pRecommendation="Liste/Messung fehlgeschlagen";
            core.Add(Severity.WARN,"F2P_PING","SCAN_FAILED",
                ex.GetType().Name+": "+ex.Message);
            if(!IsDisposed)footer.Text="F2P-Liste oder Ping-Test fehlgeschlagen. Keine Welt ausgewählt.";
        }
        finally
        {
            pingScanning=false;
            if(!IsDisposed)pingButton.Enabled=true;
        }
    }
    private void CopyLog()
    {
        try {var t=core.Report();Clipboard.SetText(t);footer.Text=$"Log kopiert ({t.Length} Zeichen) – Strg + V im Chat.";}
        catch(Exception ex){core.Add(Severity.ERROR,"CLIPBOARD","COPY_FAILED",ex.GetType().Name);MessageBox.Show(ex.Message,"Zwischenablage",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    private void Export(bool zip)
    {
        using var dialog=new SaveFileDialog {Filter=zip?"ZIP Diagnose|*.zip":"JSON Diagnose|*.json",AddExtension=true,
            FileName=$"osrs-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}"+(zip?".zip":".json")};
        if(dialog.ShowDialog(this)!=DialogResult.OK) return;
        try
        {
            if(zip)
            {
                using var file=File.Create(dialog.FileName);using var archive=new ZipArchive(file,ZipArchiveMode.Create);
                using(var writer=new StreamWriter(archive.CreateEntry("diagnostics.json").Open(),new UTF8Encoding(false)))writer.Write(core.Json());
                using(var writer=new StreamWriter(archive.CreateEntry("log.txt").Open(),new UTF8Encoding(false)))writer.Write(core.PlainLog());
            }
            else File.WriteAllText(dialog.FileName,core.Json(),new UTF8Encoding(false));
            footer.Text="Diagnose gespeichert: "+dialog.FileName;
        }
        catch(Exception ex) {core.Add(Severity.ERROR,"EXPORT","SAVE_FAILED",ex.GetType().Name);MessageBox.Show(ex.Message,"Export fehlgeschlagen");}
    }
    private void UpdateView()
    {
        var s=core.Stats();var p=Process.GetCurrentProcess();
        stats.Text=$"Offizielles OSRS: {(officialHost.Attached?
            (officialHost.CurrentKind==OfficialWindowKind.Launcher?
                "JAGEX-ANMELDUNG":"OSRS-SPIEL"):"NICHT EINGEBETTET")}"+
            " | Live-Agent: NICHT VERFÜGBAR"+
            $" | Fehler: {s.Errors} | RAM: {p.WorkingSet64/1024/1024} MiB";
        if(pause.Checked)return;
        var events=core.Snapshot().TakeLast(2000).AsEnumerable();
        if(filter.SelectedIndex==1)events=events.Where(e=>e.Level>=Severity.INFO);
        if(filter.SelectedIndex==2)events=events.Where(e=>e.Level>=Severity.WARN);
        if(filter.SelectedIndex==3)events=events.Where(e=>e.Level==Severity.ERROR);
        if(!string.IsNullOrWhiteSpace(search.Text))events=events.Where(e=>e.Line.Contains(search.Text,StringComparison.OrdinalIgnoreCase));
        var visible=string.Join(Environment.NewLine,events.Select(e=>e.Line));
        if(!string.Equals(visible,log.Text,StringComparison.Ordinal))
        {
            log.Text=visible;log.SelectionStart=log.TextLength;log.ScrollToCaret();
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
            d.Add(Severity.INFO,"CORE","PRIVACY",
                "password=abc bearer xyz user=test@example.com");
            string log=d.Snapshot().Single().Line;
            if(log.Contains("password=abc")||log.Contains("bearer xyz")||
               log.Contains("test@example.com"))
                throw new Exception("Privacy regression");
            if(d.Report().Contains("Tick-Abstände") ||
               d.Json().Contains("simulationOnly"))
                throw new Exception("Offline diagnostics still present");
            if(!d.Report().Contains("LIVE ONLY") ||
               !d.Json().Contains("liveAgentAvailable"))
                throw new Exception("Live-only export missing status");
            const string manifest="version=0.8.1\n"+
                "exeUrl=https://github.com/Riflex91/osrslive/releases/download/v0.8.1/OSRS-Play.exe\n"+
                "exeSha256=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var info=Updater.ParseManifest(manifest);
            if(info.Version.ToString()!="0.8.1")throw new Exception("Update version");
            try
            {
                Updater.ParseManifest(manifest.Replace("github.com","evil.example"));
                throw new Exception("Host accepted");
            }
            catch(FormatException){}
            OfficialF2PWorldTests.Run();
            PublicOsrsHiscoresTests.Run();
            OfficialClientWindowHostTests.Run();
            OfficialStartupPolicyTests.Run();
            if(OfficialGameSessionGate.Readiness(null)!="OFFICIAL_PROTOCOL_ADAPTER_MISSING")
                throw new Exception("Missing official adapter incorrectly accepted");
            d.Clear();
            if(d.Stats().Events!=1)throw new Exception("Clear failed");
            Console.WriteLine("LIVE ONLY PASS: automatic Jagex login startup, official host, game handoff, F2P, public skills, privacy, updater, no offline sessions");
            return 0;
        }
        catch(Exception e)
        {
            Console.Error.WriteLine("LIVE ONLY FAIL "+e);
            return 1;
        }
    }
}
