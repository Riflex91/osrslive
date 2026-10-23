using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OSRSLiveDiagnostics;

// Purpose-built minimalist 2D client viewport, not screen capture or RuneLite.
internal sealed class MiniWorldCanvas : Control
{
        [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<MiniWorldFrame?>? Current {get;set;}
        [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string>? Connection {get;set;}
        [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<Point>? TileSelected {get;set;}
        [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool DrawWorld {get;set;}=true;
    internal MiniWorldCanvas()
    {
        DoubleBuffered=true;
        BackColor=Color.FromArgb(20,28,35);
        Cursor=Cursors.Cross;
        SetStyle(ControlStyles.ResizeRedraw|ControlStyles.UserPaint|
            ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    private (int Size,int Left,int Top) Area()
    {
        int tile=Math.Max(6,Math.Min((ClientSize.Width-20)/MiniWorldMap.Width,
            (ClientSize.Height-46)/MiniWorldMap.Height));
        int left=(ClientSize.Width-tile*MiniWorldMap.Width)/2;
        int top=8;
        return(tile,left,top);
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if(e.Button!=MouseButtons.Left||!DrawWorld)return;
        var (size,left,top)=Area();
        int x=(e.X-left)/size,y=(e.Y-top)/size;
        if(e.X<left||e.Y<top||x<0||y<0||x>=MiniWorldMap.Width||y>=MiniWorldMap.Height)return;
        TileSelected?.Invoke(new Point(x,y));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using var labelFont=new Font("Segoe UI",9,FontStyle.Bold);
        using var small=new Font("Segoe UI",8);
        using var gridPen=new Pen(Color.FromArgb(45,70,84),1);
        using var fieldBrush=new SolidBrush(Color.FromArgb(39,64,49));
        using var wallBrush=new SolidBrush(Color.FromArgb(78,87,91));
        using var rockBrush=new SolidBrush(Color.FromArgb(171,184,197));
        using var emptyBrush=new SolidBrush(Color.FromArgb(82,102,96));
        using var playerBrush=new SolidBrush(Color.FromArgb(250,194,60));
        using var marketBrush=new SolidBrush(Color.FromArgb(72,148,202));
        var snapshot=Current?.Invoke();
        if(!DrawWorld)
        {
            TextRenderer.DrawText(g,"GRAFIK DEAKTIVIERT – Spielzustand wird weiter verarbeitet",
                labelFont,new Point(14,15),Color.WhiteSmoke);
            return;
        }
        var (size,left,top)=Area();
        for(int y=0;y<MiniWorldMap.Height;y++)
        for(int x=0;x<MiniWorldMap.Width;x++)
        {
            var cell=new Rectangle(left+x*size,top+y*size,size,size);
            g.FillRectangle(MiniWorldMap.Wall(x,y)?wallBrush:fieldBrush,cell);
            g.DrawRectangle(gridPen,cell);
        }
        var market=MiniWorldMap.Market;
        g.FillRectangle(marketBrush,
            new Rectangle(left+market.X*size+2,top+market.Y*size+2,size-4,size-4));
        foreach(var site in MiniWorldMap.RockSites)
        {
            var observed=snapshot?.Rocks.FirstOrDefault(r=>r.X==site.X&&r.Y==site.Y);
            bool active=observed?.Available??true;
            g.FillEllipse(active?rockBrush:emptyBrush,
                left+site.X*size+size*.16f,top+site.Y*size+size*.16f,
                size*.68f,size*.68f);
        }
        if(snapshot is not null)
        {
            g.FillEllipse(playerBrush,
                left+snapshot.X*size+size*.15f,top+snapshot.Y*size+size*.15f,
                size*.7f,size*.7f);
            var info=$"LOCAL SANDBOX  |  Tick {snapshot.Tick}  |  Erz {snapshot.Ore}/{snapshot.Capacity}"+
                $"  |  Mining-XP {snapshot.MiningXp}  |  Gold {snapshot.Coins}";
            TextRenderer.DrawText(g,info,small,new Point(left,top+size*MiniWorldMap.Height+5),
                Color.FromArgb(225,231,236));
        }
        else
        {
            TextRenderer.DrawText(g,"Verbinde lokalen Spielserver …",labelFont,
                new Point(left+10,top+10),Color.WhiteSmoke);
        }
        if(Connection?.Invoke() is string status && status!="LOCAL_SANDBOX_CONNECTED")
            TextRenderer.DrawText(g,status,small,new Point(8,ClientSize.Height-18),
                Color.OrangeRed);
    }
}
