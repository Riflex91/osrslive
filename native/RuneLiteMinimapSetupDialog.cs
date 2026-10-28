namespace OSRSLiveDiagnostics;

internal enum RuneLiteCameraZoomPreset { Far, Medium, Near, Unchanged }

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
    private readonly ComboBox cameraZoom=new();
    private readonly Button accept=new();
    private Point? selected;
    private bool selectedAutomatically;
    private readonly string markerDiagnostic;

    internal Point? PlayerCenter=>DialogResult==DialogResult.OK?selected:null;
    internal double PixelsPerTile=>(double)pixelsPerTile.Value;
    internal RuneLiteCameraZoomPreset CameraZoom=>
        (RuneLiteCameraZoomPreset)Math.Max(0,cameraZoom.SelectedIndex);
    internal string SelectionSource=>selectedAutomatically?"AUTO_RING_MARKER":"USER_SELECTED";
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
            Text="Der Spielerpunkt wird automatisch gesucht. Bitte die gelbe Markierung überprüfen; " +
                 "nur bei falscher Erkennung den Punkt manuell anklicken. " +
                 "RuneLite muss im Spiel sein; Minimap-Norden oben, Minimap-Zoom auf Standard (4 px/Tile). " +
                 "Das Bild dient nur zur Eingabegeometrie, nicht zur Positionserkennung. " +
                 "Nach Bestätigung wird RuneLite in den Vordergrund geholt und die echte Windows-Maus " +
                 "für kurze, einzeln überwachte Spielklicks bewegt. Andere Programme dürfen den Fokus " +
                 "nicht übernehmen; andernfalls stoppt die Automatik.",
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
                (IsWhitePlayerMark(screenshot,selected.Value)?"weiße Spielermarkierung sichtbar":
                    "keine weiße Spielermarkierung erkannt – bitte genauer klicken");
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
        bool foundAutomatically=RuneLiteMinimapMarker.TryLocate(screenshot,out var found,out var detected);
        markerDiagnostic=detected;
        if(foundAutomatically){selected=found;selectedAutomatically=true;}
        hint.AutoSize=true;hint.Text=foundAutomatically?
            $"Spielerpunkt automatisch erkannt: x={found.X}, y={found.Y} · gelben Kreis prüfen.":
            "Keine eindeutige Erkennung – bitte den echten Spielerpunkt im Bild anklicken.";
        hint.ForeColor=Color.LightYellow;
        var hintRow=new FlowLayoutPanel{Dock=DockStyle.Top,Height=28};hintRow.Controls.Add(hint);
        var controls=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        controls.Controls.Add(new Label{Text="Pixel pro Weltkachel (Zoom heraus: zunächst 4):",
            AutoSize=true,Margin=new(0,8,5,0)});
        pixelsPerTile.Minimum=3;pixelsPerTile.Maximum=16;pixelsPerTile.DecimalPlaces=1;
        pixelsPerTile.Increment=.5m;pixelsPerTile.Value=4;
        controls.Controls.Add(pixelsPerTile);
        controls.Controls.Add(new Label{Text="3D-Kamera-Zoom:",AutoSize=true,Margin=new(8,8,5,0)});
        cameraZoom.DropDownStyle=ComboBoxStyle.DropDownList;cameraZoom.Width=174;
        cameraZoom.Items.AddRange(["Weit heraus", "Mittel", "Nah", "Unverändert"]);
        cameraZoom.SelectedIndex=0;
        controls.Controls.Add(cameraZoom);
        north.Text="Minimap zeigt Norden oben, Standard-Zoom (4 Pixel/Tile)";
        north.AutoSize=true;north.CheckedChanged+=(_,_)=>accept.Enabled=CanAccept();
        controls.Controls.Add(north);
        foregroundConsent.Text="Ich erlaube für diesen Test Kamera-Scrollen sowie echte Mausbewegungen und Klicks im Vordergrund-RuneLite.";
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
        IsWhitePlayerMark(screenshot,point);
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
