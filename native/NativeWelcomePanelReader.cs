namespace OSRSLiveDiagnostics;

// Pixel-geometry fallback for the second official OSRS entry screen shown
// after Play Now. Coordinates are client-local 1000x600 search regions, never
// an inferred account identity or unconditionally clickable screen position.
internal readonly record struct WelcomePanelPixels(bool Banner,bool RedCenter,bool SideTiles,bool Caption)
{
    internal bool Actionable=>Banner&&RedCenter&&SideTiles&&Caption;
}

internal static class NativeWelcomePanelReader
{
    internal static readonly Rectangle ButtonTextBounds=new(446,322,134,29);
    private static readonly Rectangle Banner=new(300,20,400,35);
    private static readonly Rectangle RedPanel=new(375,299,210,76);
    private static readonly Rectangle LeftPanel=new(127,299,213,76);
    private static readonly Rectangle RightPanel=new(620,299,210,76);
    private static readonly Rectangle WhiteText=new(445,323,134,28);

    private static bool Parchment(Color c)=>c.R>130&&c.G>105&&c.B>65&&c.R>c.G&&c.G>c.B;
    private static bool Red(Color c)=>c.R>55&&c.R>c.G*1.5&&c.R>c.B*1.7&&c.G<100;
    private static bool White(Color c)=>c.R>135&&c.G>135&&c.B>130&&
        Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<45;

    private static int Count(Bitmap image,Rectangle area,Func<Color,bool> predicate)
    {
        int matches=0;
        for(int y=area.Top;y<area.Bottom;y+=2)
        for(int x=area.Left;x<area.Right;x+=2)
            if(predicate(image.GetPixel(x,y)))matches++;
        return matches;
    }
    private static bool Fraction(Bitmap image,Rectangle area,Func<Color,bool> predicate,double threshold)
    {
        int samples=((area.Width+1)/2)*((area.Height+1)/2);
        return Count(image,area,predicate)>=samples*threshold;
    }
    // Reveal only structural pixel evidence for a stalled transition, never
    // OCR text, the image itself, character names or credentials.
    internal static WelcomePanelPixels Inspect(Bitmap image)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return default;
        bool banner=Fraction(image,Banner,Parchment,.70);
        bool redCenter=Fraction(image,RedPanel,Red,.65);
        bool sideTiles=!Fraction(image,LeftPanel,Red,.15)&&!Fraction(image,RightPanel,Red,.15);
        int white=Count(image,WhiteText,White);
        return new(banner,redCenter,sideTiles,white is >=45 and <=420);
    }
    internal static VisualLabel? Read(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return null;
        if(labels.Any(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase)||
            l.Text.Contains("password",StringComparison.OrdinalIgnoreCase)||
            l.Text.Contains("authenticator",StringComparison.OrdinalIgnoreCase)||
            l.Text.Contains("verification code",StringComparison.OrdinalIgnoreCase)))
            return null;
        // Require the same four independent native pixel regions as before.
        // Inspection itself never authorizes clicks or bypasses credential UI.
        return Inspect(image).Actionable?
            new("CLICK HERE TO PLAY",ButtonTextBounds,VisualLabelKind.EnterButton):null;
    }

    internal static void SelfTest()
    {
        var labels=Array.Empty<VisualLabel>();
        using var empty=new Bitmap(1000,600);
        if(Read(empty,labels) is not null)
            throw new Exception("Native second entry: blank screen was accepted.");
        using var screen=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(screen))
        {
            g.Clear(Color.Black);
            using var parchment=new SolidBrush(Color.FromArgb(229,197,131));
            using var red=new SolidBrush(Color.FromArgb(92,18,8));
            using var side=new SolidBrush(Color.FromArgb(67,59,45));
            g.FillRectangle(parchment,new Rectangle(225,12,510,46));
            g.FillRectangle(side,LeftPanel);
            g.FillRectangle(red,RedPanel);
            g.FillRectangle(side,RightPanel);
            using var font=new Font("Segoe UI",14,FontStyle.Bold);
            g.DrawString("CLICK HERE TO PLAY",font,Brushes.White,449,322);
        }
        if(Read(screen,labels)?.Kind!=VisualLabelKind.EnterButton||!Inspect(screen).Actionable)
            throw new Exception("Native second entry: expected parchment + red tile + caption.");
        if(Read(screen,new[]{new VisualLabel("password",new Rectangle(10,10,80,12))}) is not null)
            throw new Exception("Native second entry: credential screen cannot be actionable.");
        if(Read(screen,new[]{new VisualLabel("Select a world",new Rectangle(10,10,120,12))}) is not null)
            throw new Exception("Native second entry: world list cannot be actionable.");
        using(var g=Graphics.FromImage(screen))g.FillRectangle(Brushes.Black,WhiteText);
        if(Read(screen,labels) is not null||Inspect(screen).Caption)
            throw new Exception("Native second entry: red tile without bright caption must not be clicked.");
        using(var g=Graphics.FromImage(screen))g.FillRectangle(Brushes.Black,Banner);
        if(Read(screen,labels) is not null||Inspect(screen).Banner)
            throw new Exception("Native second entry: missing parchment must block action.");
        using var wrongSize=new Bitmap(950,600);
        if(Read(wrongSize,labels) is not null)
            throw new Exception("Native second entry: unsupported viewport accepted.");
        Console.WriteLine("NATIVE SECOND ENTRY PASS: parchment, red tile, white caption, auth/geometry guards");
    }
}
