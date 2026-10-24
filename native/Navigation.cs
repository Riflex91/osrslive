using System.Drawing.Imaging;
using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal sealed record NavigationPixels(byte[] MinimapPng,Rectangle MapBounds,int? Hitpoints);
internal sealed record WorldTile(int X,int Y,int Plane=0)
{
    internal int Distance(WorldTile other)=>Plane==other.Plane?Math.Max(Math.Abs(X-other.X),Math.Abs(Y-other.Y)):int.MaxValue;
}
internal sealed record MapLocation(WorldTile Tile,double[] Transform,Rectangle MapBounds,int Inliers,DateTimeOffset ObservedUtc)
{
    // Invert the fitted image transform: no taught route, fixed camera angle,
    // or manually entered minimap zoom is needed.
    internal Point? ClickFor(WorldTile target)
    {
        if(target.Plane!=Tile.Plane||Transform.Length!=6)return null;
        double ax=(target.X-WorldMapData.MinX)*4+2,ay=(WorldMapData.MaxY-target.Y)*4-2;
        double u=ax-Transform[2],v=ay-Transform[5],det=Transform[0]*Transform[4]-Transform[1]*Transform[3];
        if(Math.Abs(det)<.01)return null;
        var point=new Point((int)Math.Round((Transform[4]*u-Transform[1]*v)/det)+MapBounds.Left,
            (int)Math.Round((-Transform[3]*u+Transform[0]*v)/det)+MapBounds.Top);
        double dx=point.X-(MapBounds.Left+MapBounds.Width/2d),dy=point.Y-(MapBounds.Top+MapBounds.Height/2d);
        return dx*dx+dy*dy<Math.Pow(MapBounds.Width/2d-14,2)&&dx*dx+dy*dy>4?point:null;
    }
}

internal static class HudVision
{
    internal static byte[] Png(Bitmap image,Rectangle crop)
    {using var bitmap=image.Clone(crop,PixelFormat.Format32bppArgb);using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();}
    internal static Rectangle? Minimap(Bitmap image)
    {
        if(image.Size!=new Size(1000,600))return null;
        using var mat=Cv.Cv2.ImDecode(Png(image,new(740,0,260,235)),Cv.ImreadModes.Grayscale);
        using var blurred=new Cv.Mat();Cv.Cv2.GaussianBlur(mat,blurred,new(5,5),1.2);
        var circles=Cv.Cv2.HoughCircles(blurred,Cv.HoughModes.Gradient,1,45,100,32,65,88)
            .Where(c=>c.Center.X+c.Radius<=260&&c.Center.X-c.Radius>=0&&c.Center.Y-c.Radius>=0&&c.Center.Y+c.Radius<220).ToArray();
        if(circles.Length!=1)return null;
        var circle=circles[0];int r=(int)Math.Round(circle.Radius)-3;
        var box=new Rectangle(740+(int)Math.Round(circle.Center.X)-r,(int)Math.Round(circle.Center.Y)-r,r*2,r*2);
        int white=0;int cx=box.Left+r,cy=box.Top+r;
        for(int y=cy-3;y<=cy+3;y++)for(int x=cx-3;x<=cx+3;x++)
        {var c=image.GetPixel(x,y);if(c.R>220&&c.G>220&&c.B>220)white++;}
        return white>=3?box:null;
    }
    internal static Rectangle? HealthNumber(Bitmap image,Rectangle map,IReadOnlyList<VisualLabel> labels)
    {
        var area=new Rectangle(Math.Max(700,map.Left-95),Math.Max(0,map.Top+8),105,75);
        var hits=labels.Where(l=>l.Kind is VisualLabelKind.Text or VisualLabelKind.Number&&area.Contains(l.Bounds)&&
            System.Text.RegularExpressions.Regex.IsMatch(l.Text.Trim(),@"^\d{1,2}$")).OrderBy(l=>l.Bounds.Top).ToArray();
        foreach(var hit in hits)
        {
            var right=Rectangle.Intersect(new(0,0,1000,600),new(hit.Bounds.Right,hit.Bounds.Top-7,45,hit.Bounds.Height+14));
            int red=0;
            for(int y=right.Top;y<right.Bottom;y++)for(int x=right.Left;x<right.Right;x++)
            {var c=image.GetPixel(x,y);if(c.R>120&&c.R>c.G*1.5&&c.R>c.B*1.5)red++;}
            if(red<8)continue;
            var bounds=hit.Bounds;bounds.Inflate(2,2);return bounds;
        }
        return null;
    }
}

internal sealed record NavigationDestination(string Id,string Service,WorldTile Tile,int Radius=3);
internal static class NavigationDestinations
{
    internal static readonly NavigationDestination[] All=
    {
        new("Lumbridge","safe",new(3222,3218),4),new("Draynor","bank",new(3092,3243)),
        new("Varrock Westbank","bank",new(3185,3436)),new("Varrock Ostbank","bank",new(3253,3420)),
        new("Falador Ostbank","bank",new(3012,3356)),new("Falador Westbank","bank",new(2945,3369)),
        new("Al Kharid","bank",new(3269,3167)),new("Grand Exchange","ge",new(3164,3487),5),
        new("Lumbridge Hühner","chickens",new(3235,3293),5),
        new("Lumbridge Bäume","CUT_TREES",new(3230,3240),5),new("Lumbridge Eichen","CUT_OAKS",new(3205,3245),5),
        new("Draynor Weiden","CUT_WILLOWS",new(3087,3236),4),new("Lumbridge Sumpfmine","MINE_COPPER_TIN",new(3225,3147),4),
        new("Varrock Eisenmine","MINE_IRON",new(3285,3365),4),new("Lumbridge Netzfischen","NET_SHRIMPS",new(3245,3155),4),
        new("Lumbridge Fluss","FLY_FISH",new(3239,3244),4),new("Lumbridge Herd","range",new(3210,3214),3),
        new("Lumbridge Schmelzofen","furnace",new(3227,3255),3),new("Varrock Amboss","anvil",new(3187,3425),3),
        new("Lumbridge Feuerplatz","firemaking",new(3245,3227),4),
        new("Luftaltar Eingang","AIR_RUNES",new(2985,3292),3),new("Erdaltar Eingang","EARTH_RUNES",new(3305,3473),3),
        new("Körperaltar Eingang","BODY_RUNES",new(3053,3445),3)
    };
    internal static string? Service(string task)=>CombatPolicy.Specs.Any(s=>s.Id==task)?"chickens":task switch
    {
        "COOK_SHRIMPS" or "COOK_TROUT" or "COOK_SALMON"=>"range","SMELT_BRONZE"=>"furnace",
        "BRONZE_PLATEBODY" or "IRON_PLATEBODY" or "STEEL_PLATEBODY" or "MITHRIL_PLATEBODY" or "ADAMANT_PLATEBODY"=>"anvil",
        "BURN_LOGS" or "BURN_OAK" or "BURN_WILLOW" or "BURN_MAPLE"=>"firemaking",
        _=>All.Any(d=>d.Id==task||d.Service==task)?task:null
    };
}
