using System.Diagnostics;
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
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_,e)=>
            MessageBox.Show(e.Exception.GetType().Name+": "+e.Exception.Message,"OSRS Diagnostics: UI Fehler");
        try { Application.Run(new MainWindow()); return 0; }
        catch(Exception e)
        {
            try
            {
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSLiveDiagnostics","fatal.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path,DateTimeOffset.Now+" "+e+Environment.NewLine);
            }catch{}
            MessageBox.Show("Start fehlgeschlagen: "+e.Message,"OSRS Diagnostics",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal sealed class MainWindow : Form
{
    private readonly Diagnostics core=new();
    private readonly LocalSimulation sim;
    private readonly Label header=new(), stats=new(), footer=new();
    private Button simButton=new(), renderButton=new();
    private readonly TextBox log=new(), search=new();
    private readonly ComboBox filter=new();
    private readonly CheckBox pause=new();
    private readonly Panel preview=new();
    private readonly System.Windows.Forms.Timer refreshTimer=new(){Interval=250};
    private readonly System.Windows.Forms.Timer metricsTimer=new(){Interval=5000};
    private bool refreshNeeded=true, shuttingDown;
    private readonly Stopwatch eventClock=Stopwatch.StartNew();
    internal MainWindow()
    {
        sim=new LocalSimulation(core);
        core.Changed+=()=>refreshNeeded=true;
        Text=$"OSRS Live Diagnostics v{Diagnostics.Version}";
        Width=1150; Height=760; MinimumSize=new Size(860,600);
        StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);
        BackColor=Color.FromArgb(245,247,250);
        InitializeUI();
        core.Add(Severity.INFO,"CORE","STARTED",$"Version {Diagnostics.Version} | Standalone Windows EXE, keine Jagex-Verbindung");
        core.Add(Severity.INFO,"CORE","PRIVACY","Keine Zugangsdaten oder Tokens im Export");
        refreshTimer.Tick+=(_,_)=>{if(refreshNeeded){refreshNeeded=false;UpdateView();}};
        refreshTimer.Start();
        metricsTimer.Tick+=(_,_)=>
        {
            var p=Process.GetCurrentProcess();
            core.Add(Severity.DEBUG,"RUNTIME","PERF_SAMPLE",$"working_set_mib={p.WorkingSet64/1024/1024} threads={p.Threads.Count} uptime_s={eventClock.Elapsed.TotalSeconds:0}");
        };
        metricsTimer.Start();
        Shown+=async (_,_)=>
        {
            core.Add(Severity.INFO,"UPDATER","CHECK_START","Automatische GitHub-Updateprüfung");
            var changed=await Updater.CheckAndUpdateAsync(msg=>
            {
                if(!IsDisposed && IsHandleCreated) BeginInvoke(()=>footer.Text=msg);
            });
            if(changed)
            {
                shuttingDown=true;
                Close();
            }
            else core.Add(Severity.INFO,"UPDATER","CHECK_DONE","Lokale Version aktiv; Prüfung abgeschlossen");
        };
        FormClosing+=(_,_)=>{sim.Dispose();metricsTimer.Stop();refreshTimer.Stop();};
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
        header.Text=$"OSRS LIVE DIAGNOSTICS  ·  {Diagnostics.Version}  ·  kein OSRS-Spielclient";
        header.Font=new Font("Segoe UI",14,FontStyle.Bold);header.Dock=DockStyle.Fill;
        outer.Controls.Add(header,0,0);
        stats.Dock=DockStyle.Fill; stats.Font=new Font("Segoe UI",10);
        outer.Controls.Add(stats,0,1);
        var actionBar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
        simButton=Button("Simulation starten",ToggleSim);
        renderButton=Button("Grafik ausschalten",ToggleRender);
        actionBar.Controls.AddRange(new Control[]{simButton,renderButton,
            Button("Log kopieren",CopyLog),Button("JSON exportieren",()=>Export(false)),
            Button("ZIP exportieren",()=>Export(true)),Button("Puffer leeren",()=>core.Clear())});
        outer.Controls.Add(actionBar,0,2);
        var content=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,152));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        preview.Dock=DockStyle.Fill;preview.BackColor=Color.FromArgb(228,235,245);
        preview.Paint+=(s,e)=>
        {
            e.Graphics.Clear(preview.BackColor);
            var title=core.Rendering?"LOKALE GRAFIKVORSCHAU":"RENDERER AUS";
            TextRenderer.DrawText(e.Graphics,title,new Font("Segoe UI",18,FontStyle.Bold),new Point(24,32),Color.FromArgb(29,56,91));
            TextRenderer.DrawText(e.Graphics,"Simulation / kein OSRS-Rendering · Ereignisverarbeitung unabhängig",Font,
                new Point(26,76),Color.FromArgb(70,90,114));
        };
        content.Controls.Add(preview,0,0);
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
        footer.Text="Eigenständige EXE. Automatische Updateprüfung beim Start. Keine Jagex-Verbindung.";
        footer.Dock=DockStyle.Fill;footer.TextAlign=ContentAlignment.MiddleLeft;outer.Controls.Add(footer,0,4);
    }
    private void ToggleSim()
    {
        if(sim.Running)sim.Stop();else sim.Start();
        simButton.Text=sim.Running?"Simulation stoppen":"Simulation starten";
    }
    private void ToggleRender()
    {
        core.SetRendering(!core.Rendering);
        renderButton.Text=core.Rendering?"Grafik ausschalten":"Grafik einschalten";
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
        stats.Text=$"{core.Mode} | Renderer: {(core.Rendering?"AN":"AUS")} | Events: {s.Events} | Ticks: {s.Ticks} | Fehler: {s.Errors} | Warnungen: {s.Warnings} | RAM: {p.WorkingSet64/1024/1024} MiB";
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
            var log=new Diagnostics();
            log.Add(Severity.INFO,"T","HELLO","password=abc bearer xyz user=test@example.com");
            var line=log.Snapshot().Single().Line;
            if(line.Contains("abc")||line.Contains("xyz")||line.Contains("test@example.com"))throw new Exception("Redaction test");
            for(int i=1;i<=3;i++){log.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK","tick="+i);Thread.Sleep(4);}
            if(log.Stats().Ticks!=3||log.TickStats().Count!=2)throw new Exception("Tick test");
            if(!log.Report().Contains("Tick-Abstände") || !log.Json().Contains("simulationOnly"))throw new Exception("Export test");
            const string manifest="version=0.3.1\nexeUrl=https://github.com/Riflex91/osrslive/releases/download/v0.3.1/OSRS-Live-Diagnostics.exe\nexeSha256=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var info=Updater.ParseManifest(manifest);
            if(info.Version.ToString()!="0.3.1")throw new Exception("Version parse");
            try {Updater.ParseManifest(manifest.Replace("github.com","evil.example"));throw new Exception("Host accepted");}
            catch(FormatException){}
            try {Updater.ParseManifest(manifest.Replace("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","notahash"));throw new Exception("Hash accepted");}
            catch(FormatException){}
            log.SetRendering(false);if(log.Rendering) throw new Exception("Renderer test");
            log.Clear();if(log.Stats().Ticks!=0)throw new Exception("Clear test");
            Console.WriteLine("SELFTEST PASS: redaction, metrics, JSON, report, strict manifest, renderer, clear");
            return 0;
        }catch(Exception ex) {Console.Error.WriteLine("SELFTEST FAIL "+ex);return 1;}
    }
}
