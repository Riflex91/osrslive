namespace OSRSLiveDiagnostics;

// One-time, user-supervised calibration of RuneLite's minimap on a volatile
// compositor frame. No captured pixels or account information are persisted.
internal sealed class RuneLiteMinimapSetupDialog:Form
{
    private readonly Bitmap screenshot;
    private readonly PictureBox preview=new();
    private readonly Label hint=new();
    private readonly NumericUpDown pixelsPerTile=new();
    private readonly CheckBox north=new();
    private readonly CheckBox foregroundConsent=new();
    private readonly Button accept=new();
    private Point? selected;
    private bool selectedAutomatically;
    private readonly string markerDiagnostic;
    private readonly Point? verifiedFixedCenter;

    internal Point? PlayerCenter=>DialogResult==DialogResult.OK?selected:null;
    internal double PixelsPerTile=>(double)pixelsPerTile.Value;
    internal string SelectionSource=>selectedAutomatically?"AUTO_FIXED_LAYOUT_WHITE_DOT":"USER_SELECTED_WHITE_DOT";
    internal string MarkerDiagnostic=>markerDiagnostic;

    internal RuneLiteMinimapSetupDialog(Bitmap currentWindow)
    {
        screenshot=(Bitmap)currentWindow.Clone();
        Text="RuneLite-Minimap für automatisches Gehen kalibrieren";
        StartPosition=FormStartPosition.CenterParent;
        ClientSize=new(980,760);MinimumSize=new(790,610);
        BackColor=Color.FromArgb(18,25,38);ForeColor=Color.WhiteSmoke;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(14),ColumnCount=1,RowCount=4};
        layout.RowStyles.Add(new(SizeType.Absolute,111));
        layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.Absolute,145));
        layout.RowStyles.Add(new(SizeType.Absolute,50));
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,AutoSize=false,
            Text="Der Spielerpunkt ist der KLEINE WEISSE PUNKT mittig auf der Minimap – " +
                 "kein gelbes Kartenicon. Die roten Linien deines Originalbildes kreuzen sich bei " +
                 "x≈640/y≈110; im aktuell 773 Pixel breiten RuneLite-Bild bei x≈645/y≈111. " +
                 "Der gelbe Kreis muss genau den WEISSEN PUNKT umschließen. " +
                 "Ohne sichtbaren weißen Punkt kann der Test nicht starten. " +
                 "Minimap muss Norden oben zeigen. Der MINIMAP-Zoom wird per Rechtsklick " +
                 "auf 4 Pixel pro Kachel zurückgesetzt; die 3D-Kamera bleibt unverändert. " +
                 "Nach Zustimmung erfolgt eine kontrollierte Eingabe nur im Vordergrund-RuneLite.",
            Font=new("Segoe UI",10,FontStyle.Bold)},0,0);
        preview.Dock=DockStyle.Fill;preview.SizeMode=PictureBoxSizeMode.Zoom;
        preview.Image=screenshot;preview.BackColor=Color.Black;
        preview.MouseClick+=(_,e)=>
        {
            double scale=Math.Min(preview.ClientSize.Width/(double)screenshot.Width,
                preview.ClientSize.Height/(double)screenshot.Height);
            if(scale<=0)return;
            double left=(preview.ClientSize.Width-screenshot.Width*scale)/2;
            double top=(preview.ClientSize.Height-screenshot.Height*scale)/2;
            int x=(int)Math.Floor((e.X-left)/scale),y=(int)Math.Floor((e.Y-top)/scale);
            if(x<0||y<0||x>=screenshot.Width||y>=screenshot.Height)return;
            selected=new(x,y);selectedAutomatically=false;
            hint.Text=$"Manuell gewählt: x={x}, y={y} · " +
                (ValidSelection()?"WEISSER Spielerpunkt bestätigt":
                    verifiedFixedCenter is Point point?
                        $"NICHT DER WEISSE SPIELERPUNKT – bitte bei x≈{point.X}, y≈{point.Y} klicken":
                        "KEIN WEISSER SPIELERPUNKT – bitte dort anklicken");
            hint.ForeColor=ValidSelection()?Color.LightYellow:Color.OrangeRed;
            accept.Enabled=CanAccept();preview.Invalidate();
        };
        preview.Paint+=(_,e)=>
        {
            if(selected is not Point point)return;
            double scale=Math.Min(preview.ClientSize.Width/(double)screenshot.Width,
                preview.ClientSize.Height/(double)screenshot.Height);
            float x=(float)((preview.ClientSize.Width-screenshot.Width*scale)/2+point.X*scale);
            float y=(float)((preview.ClientSize.Height-screenshot.Height*scale)/2+point.Y*scale);
            using var pen=new Pen(Color.Yellow,2);
            e.Graphics.DrawEllipse(pen,x-9,y-9,18,18);
            e.Graphics.DrawLine(pen,x-13,y,x-5,y);
            e.Graphics.DrawLine(pen,x+5,y,x+13,y);
        };
        layout.Controls.Add(preview,0,1);
        if(RuneLiteMinimapMarker.TryFixedCenter(screenshot.Size,out var expected,out _))
            verifiedFixedCenter=expected;
        bool foundAutomatically=RuneLiteMinimapMarker.TryLocate(screenshot,out var found,out var detected);
        markerDiagnostic=detected;
        if(foundAutomatically)
        {
            selected=found;selectedAutomatically=true;
        }
        hint.AutoSize=true;hint.Text=foundAutomatically?
            $"Weißer Minimap-Spielerpunkt bei x={found.X}, y={found.Y} bestätigt · gelben Kreis prüfen.":
            "Weißer Spielerpunkt nicht sicher erkannt – bitte echten WEISSEN PUNKT manuell anklicken.";
        hint.ForeColor=Color.LightYellow;
        var hintRow=new FlowLayoutPanel{Dock=DockStyle.Top,Height=28};hintRow.Controls.Add(hint);
        var controls=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        controls.Controls.Add(new Label{Text="Minimap-Maßstab (per Rechtsklick auf Standard):",
            AutoSize=true,Margin=new(0,8,5,0)});
        pixelsPerTile.Minimum=4;pixelsPerTile.Maximum=4;pixelsPerTile.DecimalPlaces=1;
        pixelsPerTile.Value=4;pixelsPerTile.Enabled=false;
        controls.Controls.Add(pixelsPerTile);
        north.Text="Die Minimap zeigt Norden oben (für die Bewegungsprojektion).";
        north.AutoSize=true;north.CheckedChanged+=(_,_)=>accept.Enabled=CanAccept();
        controls.Controls.Add(north);
        foregroundConsent.Text="Ich erlaube den Minimap-Zoom-Reset per Rechtsklick sowie echte Mausbewegungen und Spielklicks im Vordergrund-RuneLite.";
        foregroundConsent.AutoSize=true;
        foregroundConsent.CheckedChanged+=(_,_)=>accept.Enabled=CanAccept();
        controls.Controls.Add(foregroundConsent);
        var optCol=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};
        optCol.RowStyles.Add(new(SizeType.Absolute,29));optCol.RowStyles.Add(new(SizeType.Percent,100));
        optCol.Controls.Add(hintRow,0,0);optCol.Controls.Add(controls,0,1);
        layout.Controls.Add(optCol,0,2);
        accept.Text="Kalibrierung übernehmen";accept.AutoSize=true;accept.Enabled=false;
        accept.Click+=(_,_)=>
        {
            if(!CanAccept())return;
            DialogResult=DialogResult.OK;Close();
        };
        var cancel=new Button{Text="Abbrechen",AutoSize=true,DialogResult=DialogResult.Cancel};
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        footer.Controls.Add(cancel);footer.Controls.Add(accept);
        layout.Controls.Add(footer,0,3);
        Controls.Add(layout);AcceptButton=accept;CancelButton=cancel;
    }

    private bool ValidSelection()=>selected is Point point&&
        RuneLiteAutoWalk.ValidGeometry(new(new IntPtr(1),screenshot.Size,point,(double)pixelsPerTile.Value))&&
        (verifiedFixedCenter is Point anchor?
            RuneLiteMinimapMarker.WithinVerifiedCentre(point,anchor)&&
            IsWhitePlayerMark(screenshot,point):
            point.Y is >=70 and <=165&&point.X>=screenshot.Width-360&&
            point.X<=screenshot.Width-60&&IsWhitePlayerMark(screenshot,point));
    private bool CanAccept()=>north.Checked&&foregroundConsent.Checked&&ValidSelection();

    internal static bool IsWhitePlayerMark(Bitmap image,Point point)
    {
        if(point.X<4||point.Y<4||point.X>=image.Width-4||point.Y>=image.Height-4)return false;
        int white=0;
        for(int y=point.Y-3;y<=point.Y+3;y++)
        for(int x=point.X-3;x<=point.X+3;x++)
        {
            var c=image.GetPixel(x,y);
            if(c.R>=210&&c.G>=210&&c.B>=210)white++;
        }
        return white>=2;
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)screenshot.Dispose();
        base.Dispose(disposing);
    }
}
