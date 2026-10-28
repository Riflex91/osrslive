namespace OSRSLiveDiagnostics;

// The default RuneLite fixed-mode minimap draws the player's locator as a
// distinctive small dark 15px ring around a bright center. This detector
// identifies ONLY the on-screen marker for click geometry; it cannot infer
// a world tile, a RuneLite login, or a camera/zoom level from pixels.
internal static class RuneLiteMinimapMarker
{
    private static readonly Point[] Ring=
    [
        new(0,-7),new(0,7),new(-7,0),new(7,0),
        new(-6,-4),new(6,-4),new(-6,4),new(6,4),
        new(-4,-6),new(4,-6),new(-4,6),new(4,6),
        new(-5,-5),new(5,-5),new(-5,5),new(5,5)
    ];

    internal static bool TryLocate(Bitmap image,out Point center,out string diagnostic)
    {
        center=Point.Empty;
        if(image.Width<640||image.Height<350)
        {
            diagnostic="marker_result=UNSUPPORTED_CLIENT_GEOMETRY";return false;
        }
        // RuneLite fixed-mode UI: a limited top-right minimap search, NOT
        // arbitrary circular markers over the whole image or game viewport.
        int minX=Math.Max(8,image.Width-145),maxX=Math.Min(image.Width-9,image.Width-70);
        int minY=70,maxY=Math.Min(image.Height-9,154);
        var plausible=new List<(Point Center,int Score)>();
        for(int y=minY;y<=maxY;y++)
        for(int x=minX;x<=maxX;x++)
        {
            int ring=0;
            foreach(var p in Ring)
            {
                var c=image.GetPixel(x+p.X,y+p.Y);
                if(c.R<=45&&c.G<=45&&c.B<=45)ring++;
            }
            if(ring<15)continue;
            int bright=0;
            for(int dy=-3;dy<=3;dy++)
            for(int dx=-3;dx<=3;dx++)
            {
                var c=image.GetPixel(x+dx,y+dy);
                if(c.R>=155&&c.G>=155&&c.B>=155)bright++;
            }
            if(bright>=12)plausible.Add((new(x,y),ring*100+bright));
        }
        if(plausible.Count==0)
        {diagnostic="marker_result=NOT_FOUND automatic_pixel_guess_rejected=true";return false;}
        var ranked=plausible.OrderByDescending(v=>v.Score).ToArray();
        var best=ranked[0];
        if(ranked.Skip(1).Any(v=>Math.Max(Math.Abs(v.Center.X-best.Center.X),
            Math.Abs(v.Center.Y-best.Center.Y))>2))
        {
            diagnostic=$"marker_result=AMBIGUOUS candidates={ranked.Length} automatic_pixel_guess_rejected=true";
            return false;
        }
        center=best.Center;
        diagnostic=$"marker_result=AUTO_FOUND center_x={center.X} center_y={center.Y} "+
            $"ring_dark_samples={best.Score/100}/16 bright_center_pixels={best.Score%100}/49 "+
            "location_only=true world_tile_from_plugin_only=true";
        return true;
    }
}

internal static class RuneLiteMinimapMarkerTests
{
    internal static void Run()
    {
        static void Require(bool pass,string reason)
        {if(!pass)throw new Exception("RuneLite minimap marker: "+reason);}
        using var frame=new Bitmap(773,534);
        using(var g=Graphics.FromImage(frame))g.Clear(Color.DarkGreen);
        Require(!RuneLiteMinimapMarker.TryLocate(frame,out _,out _),
            "green scenery is never accepted as the minimap player marker");
        static void Mark(Bitmap image,Point point)
        {
            using(var g=Graphics.FromImage(image))
            {
                using var core=new SolidBrush(Color.FromArgb(191,191,191));
                g.FillEllipse(core,point.X-6,point.Y-6,13,13);
            }
            foreach(var offset in new[]{new Point(0,-7),new Point(0,7),new Point(-7,0),new Point(7,0),
                new Point(-6,-4),new Point(6,-4),new Point(-6,4),new Point(6,4),
                new Point(-4,-6),new Point(4,-6),new Point(-4,6),new Point(4,6),
                new Point(-5,-5),new Point(5,-5),new Point(-5,5),new Point(5,5)})
                image.SetPixel(point.X+offset.X,point.Y+offset.Y,Color.Black);
        }
        var expected=new Point(666,111);
        Mark(frame,expected);
        Require(RuneLiteMinimapMarker.TryLocate(frame,out var actual,out _)&&actual==expected,
            "the distinct ring centered at the user's observed RuneLite minimap position is found");
        Mark(frame,new Point(638,115));
        Require(!RuneLiteMinimapMarker.TryLocate(frame,out _,out var why)&&why.Contains("AMBIGUOUS"),
            "two plausible minimap circles must require manual review");
    }
}
