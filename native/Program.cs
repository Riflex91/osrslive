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
        if(args.Length>0 && args[0]=="--scenario-tests") return SelfTest.RunScenarios();
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
    private readonly LocalSimulation sim;
    private readonly BotController bot;
    private readonly MiniWorldClient mini;
    private readonly Label header=new(), stats=new(), footer=new();
    private Button botButton=new(), renderButton=new(), campaignButton=new(), pingButton=new(), hiscoresButton=new();
    private readonly CancellationTokenSource accountReadCancel=new();
    private bool accountReadBusy;
    private readonly CancellationTokenSource pingCancel=new();
    private bool pingScanning;
    private string f2pRecommendation="nicht gemessen";
    private bool campaignRunning;
    private readonly TextBox log=new(), search=new();
    private readonly ComboBox filter=new();
    private readonly CheckBox pause=new();
    private readonly MiniWorldCanvas preview=new();
    private readonly TabControl gameTabs=new(){Dock=DockStyle.Fill};
    private readonly TabPage officialPage=new("Offizielles OSRS (echtes Spiel)");
    private readonly TabPage sandboxPage=new("Lokale Testwelt");
    private readonly Panel officialSurface=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(17,24,39)};
    private readonly Label officialHint=new()
    {
        Dock=DockStyle.Fill,ForeColor=Color.WhiteSmoke,BackColor=Color.FromArgb(17,24,39),
        TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",11),
        Text="OFFIZIELLER OSRS-CLIENT\n\n"+
            "1. 'Jagex Login' öffnet den offiziellen Jagex Launcher.\n"+
            "2. Dort anmelden und 'Play Now' für den Official Client wählen.\n"+
            "3. Zurück in OSRS Play 'Client einbetten' anklicken.\n\n"+
            "Keine Zugangsdaten in OSRS Play eingeben.\n"+
            "Der Bot steuert diese Live-Sitzung noch nicht."
    };
    private OfficialClientWindowHost officialHost=null!;
    private Button launcherButton=new(),attachButton=new(),detachButton=new();
    private readonly System.Windows.Forms.Timer refreshTimer=new(){Interval=250};
    private readonly System.Windows.Forms.Timer metricsTimer=new(){Interval=5000};
    private volatile bool refreshNeeded=true;
    private readonly Stopwatch eventClock=Stopwatch.StartNew();
    private TimeSpan lastPerformanceElapsed=TimeSpan.Zero;
    private TimeSpan lastCpuTime=Process.GetCurrentProcess().TotalProcessorTime;
    internal MainWindow()
    {
        sim=new LocalSimulation(core);
        bot=new BotController(core,sim);
        core.Changed+=()=>refreshNeeded=true;
        mini=new MiniWorldClient(core);
        Text=$"OSRS Play v{Diagnostics.Version}";
        Width=1150; Height=800; MinimumSize=new Size(880,650);
        StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);
        BackColor=Color.FromArgb(245,247,250);
        InitializeUI();
        officialHost=new OfficialClientWindowHost(officialSurface,core);
        RefreshGameControls();
        core.Add(Severity.INFO,"OFFICIAL_CLIENT","HOST_READY",
            "official_osclient_window_supported=true authentication=JAGEX_LAUNCHER "+
            "live_bot_adapter=false sandbox_is_separate=true");
        core.Add(Severity.INFO,"CORE","STARTED",$"Version {Diagnostics.Version} | Standalone Windows EXE, keine Jagex-Verbindung");
        core.Add(Severity.INFO,"CORE","PRIVACY","Keine Zugangsdaten oder Tokens im Export");
        if(Updater.TakeLastOutcome() is string outcome)
            core.Add(Severity.INFO,"UPDATER","PREVIOUS_UPDATE",outcome);
        refreshTimer.Tick+=(_,_)=>
        {
            if(core.Rendering && gameTabs.SelectedTab==sandboxPage)preview.Invalidate();
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
        FormClosing+=(_,_)=>{accountReadCancel.Cancel();pingCancel.Cancel();officialHost?.Dispose();mini.Dispose();bot.Dispose();sim.Dispose();metricsTimer.Stop();refreshTimer.Stop();pingCancel.Dispose();accountReadCancel.Dispose();};
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
        header.Text=$"OSRS PLAY  ·  {Diagnostics.Version}  ·  offizieller Client + Testumgebung";
        header.Font=new Font("Segoe UI",14,FontStyle.Bold);header.Dock=DockStyle.Fill;
        outer.Controls.Add(header,0,0);
        stats.Dock=DockStyle.Fill; stats.Font=new Font("Segoe UI",10);
        outer.Controls.Add(stats,0,1);
        var actionBar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
        botButton=Button("Bot starten",ToggleBot);
        botButton.BackColor=Color.FromArgb(218,242,225);
        renderButton=Button("Grafik ausschalten",ToggleRender);
        campaignButton=Button("Strategie-Langzeittest",RunCampaign);
        pingButton=Button("F2P-Ping testen",TestF2PWorlds);
        hiscoresButton=Button("Echte OSRS-Skills",ReadPublicHiscores);
        launcherButton=Button("Jagex Login",OpenJagexLogin);
        attachButton=Button("Client einbetten",AttachOfficialClient);
        detachButton=Button("Client freigeben",DetachOfficialClient);
        actionBar.Controls.AddRange(new Control[]{launcherButton,attachButton,detachButton,
            botButton,renderButton,pingButton,hiscoresButton,
            campaignButton,Button("Mining → GE testen",RunMiningGe),
            Button("Log kopieren",CopyLog),Button("JSON exportieren",()=>Export(false)),
            Button("ZIP exportieren",()=>Export(true)),Button("Puffer leeren",()=>core.Clear())});
        outer.Controls.Add(actionBar,0,2);
        var content=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
        content.RowStyles.Add(new RowStyle(SizeType.Percent,58));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,42));
        preview.Dock=DockStyle.Fill;
        preview.Current=()=>mini.Snapshot;
        preview.Connection=()=>mini.Connection;
        preview.TileSelected=point=>
        {
            if(mini.BotRunning){footer.Text="Manuelle Bewegung nur bei gestopptem Bot.";return;}
            mini.MoveManually(point);
            footer.Text=$"Bewegungsziel {point.X},{point.Y} – lokale Spielwelt.";
        };
        gameTabs.TabPages.AddRange(new[]{officialPage,sandboxPage});
        officialPage.Controls.Add(officialSurface);
        officialSurface.Controls.Add(officialHint);
        sandboxPage.Controls.Add(preview);
        gameTabs.SelectedTab=officialPage;
        gameTabs.SelectedIndexChanged+=(_,_)=>RefreshGameControls();
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
        footer.Text="Für echte OSRS-Anmeldung: Jagex Login öffnen, Official Client starten und einbetten.";
        footer.Dock=DockStyle.Fill;footer.TextAlign=ContentAlignment.MiddleLeft;outer.Controls.Add(footer,0,4);
    }
    private void RefreshGameControls()
    {
        bool sandbox=gameTabs.SelectedTab==sandboxPage;
        botButton.Enabled=sandbox;
        botButton.Text=sandbox?(mini.BotRunning?"Bot stoppen":"Bot starten"):
            "Live-Agent: nicht verbunden";
        renderButton.Enabled=sandbox;
        if(!sandbox)
            footer.Text=officialHost is not null && officialHost.Attached?
                "Echtes OSRS-Spielfenster eingebettet. Login über Jagex; automatische Spielsteuerung noch nicht verfügbar.":
                "Jagex Launcher starten, dort anmelden und anschließend Official Client einbetten.";
    }
    private void OpenJagexLogin()
    {
        try
        {
            bool opened=OfficialClientWindowHost.TryOpenJagexLauncher();
            if(!opened)
            {
                var answer=MessageBox.Show(this,
                    "Jagex Launcher wurde an den üblichen Installationsorten nicht gefunden. "+
                    "Die offizielle Downloadseite im Browser öffnen?",
                    "Jagex Launcher nicht gefunden",MessageBoxButtons.YesNo,MessageBoxIcon.Information);
                if(answer==DialogResult.Yes)
                    Process.Start(new ProcessStartInfo("https://www.jagex.com/en-GB/launcher")
                        {UseShellExecute=true});
                return;
            }
            core.Add(Severity.INFO,"OFFICIAL_CLIENT","LAUNCHER_OPENED",
                "login_handled_externally_by_jagex=true no_credentials_in_osrs_play=true");
            footer.Text="Im Jagex Launcher anmelden, Official Client auswählen und auf Play Now klicken.";
        }
        catch(Exception ex)
        {
            core.Add(Severity.ERROR,"OFFICIAL_CLIENT","LAUNCHER_FAILED",ex.GetType().Name);
            footer.Text="Jagex Launcher konnte nicht gestartet werden; Installation überprüfen.";
        }
    }
    private void AttachOfficialClient()
    {
        var available=OfficialClientWindowHost.FindRunningClients();
        if(available.Count==0)
        {
            footer.Text="Kein laufendes offizielles osclient.exe-Fenster gefunden. Im Jagex Launcher erst Play Now anklicken.";
            core.Add(Severity.INFO,"OFFICIAL_CLIENT","WINDOW_NOT_FOUND",
                "expected_executable=osclient.exe action=open_official_launcher");
            return;
        }
        OfficialClientWindow chosen=available[0];
        if(available.Count>1)
        {
            using var chooser=new Form
            {
                Text="Offiziellen OSRS-Client auswählen",Width=550,Height=205,
                StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,
                MinimizeBox=false,MaximizeBox=false
            };
            var options=new ComboBox{Left=14,Top=18,Width=505,DropDownStyle=ComboBoxStyle.DropDownList};
            foreach(var candidate in available)
                options.Items.Add($"PID {candidate.ProcessId} – {candidate.Title}");
            options.SelectedIndex=0;
            var accept=new Button{Text="Einbetten",Left=390,Top=69,Width=128,DialogResult=DialogResult.OK};
            chooser.Controls.Add(options);chooser.Controls.Add(accept);
            chooser.AcceptButton=accept;
            if(chooser.ShowDialog(this)!=DialogResult.OK)return;
            chosen=available[options.SelectedIndex];
        }
        if(!officialHost.Attach(chosen))
        {
            footer.Text="Windows-Andocken fehlgeschlagen. Der offizielle Client läuft weiterhin separat.";
            core.Add(Severity.WARN,"OFFICIAL_CLIENT","ATTACH_FAILED",
                "possible_dpi_or_window_restriction=true no_auth_interception=true");
            return;
        }
        gameTabs.SelectedTab=officialPage;
        officialHint.Visible=false;
        footer.Text="Echtes OSRS eingebettet. Login und Spielsitzung gehören weiterhin Jagex.";
        RefreshGameControls();
    }
    private void DetachOfficialClient()
    {
        officialHost.Detach();
        officialHint.Visible=true;
        core.Add(Severity.INFO,"OFFICIAL_CLIENT","DETACH_REQUESTED",
            "official_game_remains_running=true");
        RefreshGameControls();
    }
    private void ToggleBot()
    {
        if(gameTabs.SelectedTab!=sandboxPage)
        {
            core.Add(Severity.WARN,"OFFICIAL_CLIENT","LIVE_AGENT_UNAVAILABLE",
                "reason=verified_game_state_and_authorized_input_adapter_missing");
            footer.Text="Live-Agent noch nicht verfügbar: Es fehlen echte Spielzustands- und Aktionsschnittstellen.";
            return;
        }
        try
        {
            bool started=!mini.BotRunning;
            mini.SetBot(started);
            botButton.Text=started?"Bot stoppen":"Bot starten";
            botButton.BackColor=started?Color.FromArgb(255,226,221):Color.FromArgb(218,242,225);
            footer.Text=started?
                "Autonomer Agent läuft in der eigenständigen lokalen Spielwelt.":
                "Agent gestoppt. Klicke eine Kachel zur manuellen Bewegung.";
            preview.Invalidate();
        }
        catch(Exception ex)
        {
            core.Add(Severity.ERROR,"MINI_BOT","CONTROL_FAILED",ex.GetType().Name+": "+ex.Message);
            footer.Text="Botsteuerung fehlgeschlagen – siehe Log.";
        }
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
    private void LogGoalPlanFixture()
    {
        var skills=GoalPlanner.Skills.ToDictionary(s=>s,s=>1,StringComparer.OrdinalIgnoreCase);
        skills["Mining"]=40;skills["Fishing"]=55;
        var state=new GoalAccountProfile(skills,100000,20000,true,new HashSet<string>());
        var now=DateTimeOffset.UtcNow;
        var activities=new[]
        {
            new GoalActivity("example_mining","Mining",15,35000,90000,5000,false,null,true,now),
            new GoalActivity("example_fishing","Fishing",45,60000,45000,1000,false,null,true,now)
        };
        var plan=GoalPlanner.Rank(state,activities,GoalPreference.Balanced,now);
        core.Add(Severity.INFO,"GOAL_PLANNER","LONG_TERM_GOAL",
            $"max_total={GoalPlanner.MaxTotalLevel} skill_count={GoalPlanner.Skills.Length} "+
            "objective=MAX_SKILLS_AND_GP synthetic_fixture=true");
        foreach(var option in plan.Options.Take(2))
            core.Add(Severity.INFO,"GOAL_PLANNER","ESTIMATED_ACTIVITY",
                $"activity={option.Id} score={option.Score} skill={option.Skill} "+
                $"xp_per_hour={option.XpPerHour} gp_per_hour={option.NetGpPerHour} "+
                "hypothetical_values=true no_game_execution=true");
    }
    private async void RunCampaign()
    {
        if(campaignRunning)return;
        campaignRunning=true;campaignButton.Enabled=false;
        core.Add(Severity.INFO,"CAMPAIGN","START",
            "Offline-Langzeittest: nur fiktive Skills und GP, keine Spielverbindung.");
        try
        {
            var outcome=await Task.Run(()=>
            {
                var path=OfflineCampaign.CheckpointPath;
                bool wasSaved=File.Exists(path);
                var before=OfflineCampaign.LoadOrCreate(path);
                var after=OfflineCampaign.RunBatch(before,OfflineCampaign.BatchSize);
                OfflineCampaign.Save(path,after);
                return (before,after,wasSaved);
            });
            core.Add(Severity.INFO,"CAMPAIGN",outcome.wasSaved?"RESUMED":"CREATED",
                $"initial_step={outcome.before.Steps} initial_gp={outcome.before.Coins} source=SYNTHETIC_FIXTURE");
            core.Add(Severity.INFO,"CAMPAIGN","CHECKPOINT_SAVED",
                $"steps={outcome.after.Steps} total_level={outcome.after.Levels.Values.Sum()} "+
                $"synthetic_gp={outcome.after.Coins} "+
                $"activity={outcome.after.LastActivity} simulated_only=true");
            footer.Text="Strategie-Checkpoint gespeichert (nur Offline-Testdaten).";
        }
        catch(Exception ex)
        {
            core.Add(Severity.ERROR,"CAMPAIGN","FAILED",ex.GetType().Name+": "+ex.Message);
            footer.Text="Strategie-Langzeittest fehlgeschlagen; Checkpoint erhalten.";
        }
        finally {campaignRunning=false;campaignButton.Enabled=true;}
    }
    private void RunMiningGe()
    {
        try
        {
            core.Add(Severity.INFO,"MINING_GE","SUITE_START",
                "Fuenf deterministische Offline-Szenarien; keine Spielverbindung.");
            foreach(var outcome in MiningScenario.Verify())
            {
                core.Add(Severity.INFO,"MINING_GE","SCENARIO_RESULT",outcome.Summary);
                foreach(var ev in outcome.Trace)
                {
                    if(ev.Code is "ORE_MINED" or "MOVE" or "ROCK_WAIT")continue;
                    core.Add(ev.Code is "ROUTE_ABORT" or "OFFER_REJECTED" ?
                        Severity.WARN:Severity.INFO,"MINING_GE",ev.Code,
                        $"scenario={outcome.Name} tick={ev.Tick} {ev.Detail}");
                }
            }
            core.Add(Severity.INFO,"MINING_GE","SUITE_PASS",
                "5 Offline-Szenarien bestanden; Erzbuchhaltung, Verkauf und Navigation verifiziert.");
            footer.Text="Mining → GE: Offline-Szenarien bestanden. Log kopieren.";
        }
        catch(Exception ex)
        {
            core.Add(Severity.ERROR,"MINING_GE","SUITE_FAILED",
                ex.GetType().Name+": "+ex.Message);
            footer.Text="Offline-Simulation fehlgeschlagen. Log kopieren.";
        }
    }
    private void ToggleRender()
    {
        core.SetRendering(!core.Rendering);
        renderButton.Text=core.Rendering?"Grafik ausschalten":"Grafik einschalten";
        preview.DrawWorld=core.Rendering;
        preview.Invalidate();
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
        if(gameTabs.SelectedTab==officialPage)
            stats.Text=$"ECHTES OSRS: {(officialHost.Attached?"EINGEBETTET":"NICHT EINGEBETTET")}"+
                " | Login: Jagex Launcher | Live-Agent: NICHT VERFÜGBAR"+
                $" | Fehler: {s.Errors} | RAM: {p.WorkingSet64/1024/1024} MiB";
        else
        {
            var world=mini.Snapshot;
            stats.Text=$"Sandbox-Bot: {(mini.BotRunning?"LÄUFT":"GESTOPPT")} | Server: {mini.Connection}"+
                $" | Tick: {world?.Tick??0} | Erz: {world?.Ore??0}/{world?.Capacity??6}"+
                $" | XP: {world?.MiningXp??0} | GP: {world?.Coins??0}"+
                $" | Fehler: {s.Errors} | RAM: {p.WorkingSet64/1024/1024} MiB";
        }
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
    internal static int RunScenarios()
    {
        try
        {
            foreach(var r in MiningScenario.Verify())
                Console.WriteLine("SCENARIO "+r.Summary);
            Console.WriteLine("SCENARIO SUITE PASS (5 synthetic environments)");
            return 0;
        }
        catch(Exception e)
        {
            Console.Error.WriteLine("SCENARIO SUITE FAIL "+e);
            return 1;
        }
    }
    internal static int Run()
    {
        try
        {
            var log=new Diagnostics();
            log.Add(Severity.INFO,"T","HELLO","password=abc bearer xyz user=test@example.com");
            var line=log.Snapshot().Single().Line;
            if(line.Contains("abc")||line.Contains("xyz")||line.Contains("test@example.com"))throw new Exception("Redaction test");
            for(int i=1;i<=3;i++){log.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK","tick="+i);Thread.Sleep(4);}
            if(log.Stats().Ticks!=3||log.TickStats().Count!=2)throw new Exception("Tick test");
            if(!log.Report().Contains("Tick-Abstände") || !log.Json().Contains("simulationOnly"))throw new Exception("Export test");
            const string manifest="version=0.3.2\nexeUrl=https://github.com/Riflex91/osrslive/releases/download/v0.3.2/OSRS-Live-Diagnostics.exe\nexeSha256=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var info=Updater.ParseManifest(manifest);
            if(info.Version.ToString()!="0.3.2")throw new Exception("Version parse");
            var playManifest=manifest.Replace("OSRS-Live-Diagnostics.exe","OSRS-Play.exe");
            if(Updater.ParseManifest(playManifest).Version!=info.Version)
                throw new Exception("OSRS Play update URL not recognized");
            try {Updater.ParseManifest(manifest.Replace("github.com","evil.example"));throw new Exception("Host accepted");}
            catch(FormatException){}
            try {Updater.ParseManifest(manifest.Replace("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","notahash"));throw new Exception("Hash accepted");}
            catch(FormatException){}
            OfflineAgentTests.Run();
            SessionMetricsTests.Run();
            BotControllerTests.Run();
            MiniWorldTests.Run();
            OfficialF2PWorldTests.Run();
            PublicOsrsHiscoresTests.Run();
            MiniWorldActionAuditTests.Run();
            OfficialClientWindowHostTests.Run();
            GoalPlannerTests.Run();
            OfflineCampaignTests.Run();
            if(RunScenarios()!=0)throw new Exception("Mining/GE scenario suite failed");
            log.SetRendering(false);if(log.Rendering) throw new Exception("Renderer test");
            log.Clear();if(log.Stats().Ticks!=0)throw new Exception("Clear test");
            Console.WriteLine("SELFTEST PASS: real osclient.exe window host, launcher-owned login, real public OSRS skill data contract, authenticated-session gate, monotonic action audit, official F2P world list and TCP ping filters, own TCP client, navigation, mining, selling, server stop, session-aware timing, OSRS Play bot Start/Stop, long-term strategy, campaign persistence, offline agent, mining/GE, redaction, metrics, JSON, report, strict manifest, renderer, clear");
            return 0;
        }catch(Exception ex) {Console.Error.WriteLine("SELFTEST FAIL "+ex);return 1;}
    }
}
