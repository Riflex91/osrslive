namespace OSRSLiveDiagnostics;

// Pixel-based backup for the official 1000x600 "Play Now" screen.
// Full-frame Windows OCR can read the welcome heading and world footer while
// missing the white stylized button text. Coordinates are only SEARCH REGIONS;
// neither the account name nor the glyph pixels are stored or logged.
internal static class WelcomePlayButtonReader
{
    internal static Rectangle ButtonTextBounds=>new(440,236,120,34);

    private static bool Dark(Color c)=>c.R<38&&c.G<45&&c.B<30;
    private static bool White(Color c)=>
        Math.Min(c.R,Math.Min(c.G,c.B))>155&&
        Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<38;
    private static bool Yellow(Color c)=>
        c.R>135&&c.G>95&&c.B<130&&c.R>c.B*1.35;

    internal static VisualLabel? Read(Bitmap bitmap,IReadOnlyList<VisualLabel> labels)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize)return null;
        if(!labels.Any(l=>l.Text.Contains("Welcome to Old School RuneScape",
            StringComparison.OrdinalIgnoreCase)))return null;
        if(labels.Any(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase)||
            l.Text.Contains("password",StringComparison.OrdinalIgnoreCase)||
            l.Text.Contains("authenticator",StringComparison.OrdinalIgnoreCase)))return null;

        // The native Play Now rectangle is a thick, closed dark border centered
        // at (500,263). A dark dialog alone is insufficient evidence.
        static bool Line(Bitmap image,IEnumerable<Point> pts,double threshold)=>
            pts.Count(p=>Dark(image.GetPixel(p.X,p.Y)))>=
                Math.Ceiling(pts.Count()*threshold);
        if(!Line(bitmap,Enumerable.Range(0,23).Select(i=>new Point(415+i*8,230)),.85)||
           !Line(bitmap,Enumerable.Range(0,23).Select(i=>new Point(415+i*8,295)),.85)||
           !Line(bitmap,Enumerable.Range(0,17).Select(i=>new Point(391,239+i*3)),.85)||
           !Line(bitmap,Enumerable.Range(0,17).Select(i=>new Point(607,239+i*3)),.85))
            return null;

        // Require bright "Play Now"-like foreground above, and the separate
        // yellow character-caption pixels below. Never transcribe that caption.
        int white=0,yellow=0;
        for(int y=240;y<263;y++)
        for(int x=445;x<556;x++)
            if(White(bitmap.GetPixel(x,y)))white++;
        for(int y=270;y<287;y++)
        for(int x=433;x<570;x++)
            if(Yellow(bitmap.GetPixel(x,y)))yellow++;
        if(white is <130 or >1300||yellow is <45 or >1200)return null;
        // Pixel evidence is distinct from OCR evidence; the world footer is
        // still verified by the startup policy before authorizing any click.
        return new VisualLabel("Play Now",ButtonTextBounds,VisualLabelKind.PlayButton);
    }

    internal static void SelfTest()
    {
        using var blank=new Bitmap(1000,600);
        var welcome=new[]{new VisualLabel("Welcome to Old School RuneScape",
            new Rectangle(400,200,200,15))};
        if(Read(blank,welcome) is not null)
            throw new Exception("Play Now: welcome heading on a blank screen cannot authorize input.");
        using var fixture=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(fixture))
        {
            g.Clear(Color.FromArgb(42,58,26));
            using var border=new Pen(Color.FromArgb(8,11,4),5);
            g.DrawRectangle(border,391,230,216,65);
            using var playFont=new Font("Segoe UI",20,FontStyle.Bold);
            using var captionFont=new Font("Segoe UI",15,FontStyle.Bold);
            g.DrawString("Play Now",playFont,Brushes.White,448,236);
            g.DrawString("Selected character",captionFont,Brushes.Yellow,432,268);
        }
        if(Read(fixture,welcome)?.Kind!=VisualLabelKind.PlayButton)
            throw new Exception("Play Now: native centered border + two text regions not detected.");
        if(Read(fixture,Array.Empty<VisualLabel>()) is not null||
           Read(fixture,welcome.Append(new VisualLabel("Select a world",
               new Rectangle(20,20,120,20))).ToArray()) is not null)
            throw new Exception("Play Now: no click without welcome or on world-selection screen.");
        Console.WriteLine("WELCOME PLAY BUTTON PASS: centered border, contrast, text regions, blank/menu rejection");
    }
}
