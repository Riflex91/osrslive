namespace OSRSLiveDiagnostics;

internal sealed class RuneLiteWindow:Form
{
    private readonly RuneLitePositionSource source=new();
    private readonly Label status=new(),position=new(),detail=new();
    private readonly System.Windows.Forms.Timer timer=new(){Interval=800};
    private bool paused,busy;
    private long generation;
    private readonly CancellationTokenSource closing=new();
    internal RuneLiteWindow()
    {
        Text=$"OSRS Play {Diagnostics.Version} – RuneLite";ClientSize=new(800,380);
        MinimumSize=new(700,400);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(18,25,38);ForeColor=Color.WhiteSmoke;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(24),ColumnCount=1,RowCount=6};
        panel.RowStyles.Add(new(SizeType.Absolute,45));panel.RowStyles.Add(new(SizeType.Absolute,45));
        panel.RowStyles.Add(new(SizeType.Absolute,65));panel.RowStyles.Add(new(SizeType.Percent,100));
        panel.RowStyles.Add(new(SizeType.Absolute,45));panel.RowStyles.Add(new(SizeType.Absolute,30));
        panel.Controls.Add(new Label{Text="OSRS PLAY · RUNELITE",Dock=DockStyle.Fill,
            Font=new("Segoe UI",16,FontStyle.Bold)},0,0);
        status.Dock=position.Dock=detail.Dock=DockStyle.Fill;
        status.Font=new("Segoe UI",12,FontStyle.Bold);position.Font=new("Segoe UI",20,FontStyle.Bold);
        detail.Font=new("Segoe UI",10);panel.Controls.Add(status,0,1);panel.Controls.Add(position,0,2);panel.Controls.Add(detail,0,3);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};
        var launcher=new Button{Text="Jagex Launcher öffnen",AutoSize=true};
        launcher.Click+=(_,_)=>
        {
            try{if(!OfficialClientWindowHost.TryOpenJagexLauncher())MessageBox.Show("Jagex Launcher wurde nicht gefunden.");}
            catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception)
            {MessageBox.Show("Jagex Launcher konnte nicht geöffnet werden.");}
        };
        var pause=new Button{Text="Verbindung pausieren",AutoSize=true};
        pause.Click+=(_,_)=>
        {
            paused=!paused;generation++;source.Reset();position.Text="Position unbekannt";
            status.Text=paused?"Verbindung pausiert":"Warte auf neue Positionsbilder";
            pause.Text=paused?"Verbindung fortsetzen":"Verbindung pausieren";_ = RefreshPosition();
        };
        buttons.Controls.Add(launcher);buttons.Controls.Add(pause);panel.Controls.Add(buttons,0,4);
        panel.Controls.Add(new Label{Text="Positionsprüfung · Noch keine automatischen Spielaktionen",Dock=DockStyle.Fill},0,5);
        Controls.Add(panel);timer.Tick+=async(_,_)=>await RefreshPosition();timer.Start();Shown+=async(_,_)=>await RefreshPosition();
    }
    private async Task RefreshPosition()
    {
        if(busy)return;busy=true;
        long currentGeneration=generation;
        RuneLitePositionState state;
        try{state=paused?new("PAUSED"):await source.ReadAsync(closing.Token);}
        catch(OperationCanceledException){return;}
        finally{busy=false;}
        if(IsDisposed||closing.IsCancellationRequested)return;
        if(currentGeneration!=generation){source.Reset();return;}
        if(paused)state=new("PAUSED");
        status.Text=state.Status switch
        {
            "OVERLAY_CONFIRMED"=>"RuneLite-Koordinatenanzeige aus zwei Bildern bestätigt",
            "PAUSED"=>"Verbindung pausiert",
            "GRID_INFO_NOT_READABLE"=>"Warte auf World Location · Grid Info",
            "WAITING_FOR_SECOND_FRAME"=>"Warte auf ein zweites unabhängiges Bild",
            "RUNELITE_NOT_RUNNING"=>"RuneLite ist nicht gestartet",
            "STALE_FRAME"=>"Positionsbild ist nicht mehr aktuell",
            "CAPTURE_UNAVAILABLE"=>"RuneLite-Bild momentan nicht verfügbar",
            "OCR_LANGUAGE_UNAVAILABLE"=>"Windows-Texterkennung ist nicht verfügbar",
            "MULTIPLE_RUNELITE_CLIENTS"=>"Mehrere Positionsclients – bitte einen Testclient schließen",
            _=>"Positionsdaten konnten nicht bestätigt werden"
        };
        position.Text=state.Tile is WorldTile tile?$"X {tile.X}    Y {tile.Y}    Ebene {tile.Plane}":"Position unbekannt";
        detail.Text=state.Tile is not null?"Quelle: World Location · eigene Spielerposition mit Chunk-/Region-Prüfung":
            "RuneLite über den offiziellen Jagex Launcher starten. In World Location: Grid Info aktivieren, Grid Info Type = Unique ID.\n"+
            "Unbekannte oder alte Positionen werden nicht weiterverwendet.";
    }
    protected override void Dispose(bool disposing){if(disposing){closing.Cancel();timer.Dispose();source.Dispose();}base.Dispose(disposing);}
}
