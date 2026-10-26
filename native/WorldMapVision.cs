using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal sealed record WorldMapPixels(byte[] MapPng,Rectangle ViewBounds,Point PlayerMarker,Rectangle CloseBounds);
// Surface coordinates are a search hint, NOT evidence for the player's floor.
internal sealed record WorldMapPosition(WorldTile SurfaceTile,int Inliers,DateTimeOffset ObservedUtc);
internal sealed record WorldMapMatch(WorldMapPosition? Position,string Reason,int Features=0,int Matches=0,int Inliers=0,
    string MatchProfiles="");

internal static class WorldMapVision
{
    internal static readonly Rectangle View=new(192,4,508,386);
    internal static readonly Rectangle Close=new(671,8,28,25);
    private static string Compact(string text)=>Regex.Replace(text.ToLowerInvariant(),@"\s+","").Trim('\'', '’', '‘');
    internal static bool Panel(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return false;
        bool Label(string text,Rectangle area)=>labels.Any(l=>Compact(l.Text)==Compact(text)&&area.Contains(l.Bounds));
        if(!Label("Gielinor Surface",new(200,390,330,60))||!Label("Search",new(20,390,215,60))||
            !(Label("Game features",new(0,0,190,70))||Label("You are here",new(0,250,190,145))))return false;
        // Reading the map is safe even if its close control is unreadable.
        // Closing remains separately gated on the observed X pixels.
        return true;
    }
    internal static bool CloseIcon(Bitmap image)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return false;
        var points=new List<Point>();
        for(int y=Close.Top;y<Close.Bottom;y++)for(int x=Close.Left;x<Close.Right;x++)
        {var c=image.GetPixel(x,y);if(c.R>180&&c.G>50&&c.G<190&&c.B<75)points.Add(new(x,y));}
        if(points.Count is <35 or >500)return false;
        int left=points.Min(p=>p.X),right=points.Max(p=>p.X),top=points.Min(p=>p.Y),bottom=points.Max(p=>p.Y);
        if(right-left<15||bottom-top<15)return false;
        int diagonalA=0,diagonalB=0,cardinal=0;
        foreach(var p in points)
        {
            double x=2d*(p.X-left)/(right-left)-1,y=2d*(p.Y-top)/(bottom-top)-1;
            if(Math.Abs(x-y)<.55)diagonalA++;
            if(Math.Abs(x+y)<.55)diagonalB++;
            if(Math.Abs(x)>.7&&Math.Abs(y)<.2||Math.Abs(y)>.7&&Math.Abs(x)<.2)cardinal++;
        }
        return diagonalA>=points.Count*.3&&diagonalB>=points.Count*.3&&cardinal<=8;
    }
    private static int CloseOrangePixels(Bitmap image)
    {
        int orange=0;
        for(int y=Close.Top;y<Close.Bottom;y++)for(int x=Close.Left;x<Close.Right;x++)
        {var c=image.GetPixel(x,y);if(c.R>180&&c.G>50&&c.G<190&&c.B<75)orange++;}
        return orange;
    }
    internal static async Task<IReadOnlyList<VisualLabel>> ReadLabels(Bitmap image,OcrEngine engine)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return Array.Empty<VisualLabel>();
        var crops=new[]{new Rectangle(0,0,190,90),new Rectangle(0,390,665,55),new Rectangle(760,120,240,150)};
        int factor=(int)Math.Min(3,(OcrEngine.MaxImageDimension-20)/665);
        if(factor<1)return Array.Empty<VisualLabel>();
        using var sheet=new Bitmap(665*factor+20,295*factor+80);
        var regions=new List<(Rectangle Source,Rectangle Sheet)>();int top=10;
        using(var graphics=Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.White);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            foreach(var crop in crops)
            {
                using var cleaned=GameplayHudOcr.Contrast(image,crop);
                // The shortcut hint is red; luminance-only cleanup makes it
                // almost white. Preserve visible red glyphs as text too.
                for(int y=0;y<crop.Height;y++)for(int x=0;x<crop.Width;x++)
                {
                    var c=image.GetPixel(crop.X+x,crop.Y+y);
                    if(c.R>150&&c.R>c.G*1.4&&c.R>c.B*1.4)cleaned.SetPixel(x,y,Color.Black);
                }
                var dest=new Rectangle(10,top,crop.Width*factor,crop.Height*factor);
                graphics.DrawImage(cleaned,dest);regions.Add((crop,dest));top+=dest.Height+20;
            }
        }
        var result=await VisualStartup.RecognizeRaw(sheet,engine);var labels=new List<VisualLabel>();
        foreach(var line in result.Lines.Where(l=>l.Words.Count>0))
        {
            for(int start=0;start<line.Words.Count;start++)for(int length=1;length<=6&&start+length<=line.Words.Count;length++)
            {
                var words=line.Words.Skip(start).Take(length).ToArray();string text=string.Join(" ",words.Select(w=>w.Text));
                if(!new[]{"search","gielinorsurface","gamefeatures","youarehere","openfloatingpanel","ctrl+mtoopen"}
                    .Contains(Compact(text).TrimEnd('.')))continue;
                var b=words.Select(w=>new Rectangle((int)w.BoundingRect.X,(int)w.BoundingRect.Y,
                    (int)Math.Ceiling(w.BoundingRect.Width),(int)Math.Ceiling(w.BoundingRect.Height))).Aggregate(Rectangle.Union);
                var owner=regions.Where(r=>r.Sheet.Contains(b)).ToArray();if(owner.Length!=1)continue;
                var r=owner[0];
                labels.Add(new(text,new(r.Source.X+(b.X-r.Sheet.X)/factor,r.Source.Y+(b.Y-r.Sheet.Y)/factor,
                    (int)Math.Ceiling(b.Width/(double)factor),(int)Math.Ceiling(b.Height/(double)factor))));
            }
        }
        return labels.Distinct().ToArray();
    }
    internal static IReadOnlyList<Rectangle> MarkerCandidates(Bitmap image,Rectangle area)
        =>MarkerCandidates(image,area,out _);
    private static IReadOnlyList<Rectangle> MarkerCandidates(Bitmap image,Rectangle area,out string diagnostic)
    {
        diagnostic="none";
        if(!new Rectangle(Point.Empty,image.Size).Contains(area))return Array.Empty<Rectangle>();
        int width=area.Width,height=area.Height;
        var gold=new bool[width*height];var seen=new bool[gold.Length];var queue=new int[gold.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {var c=image.GetPixel(area.X+x,area.Y+y);gold[y*width+x]=c.R>185&&c.G>135&&c.B<100&&c.R<c.G*1.65;}
        // Animated marker highlights and its dark outline can interrupt the
        // yellow raster by a few pixels. Join only these narrow gaps before
        // component analysis; broad scenery still fails the shape/fill gates.
        var expanded=new bool[gold.Length];var joined=new bool[gold.Length];
        const int radius=3;
        for(int y=radius;y<height-radius;y++)for(int x=radius;x<width-radius;x++)
            for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)
                expanded[y*width+x]|=gold[(y+dy)*width+x+dx];
        for(int y=radius;y<height-radius;y++)for(int x=radius;x<width-radius;x++)
        {
            bool filled=true;
            for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)filled&=expanded[(y+dy)*width+x+dx];
            joined[y*width+x]=filled;
        }
        gold=joined;
        var found=new List<Rectangle>();
        var components=new List<(Rectangle Box,int Count)>();
        for(int i=0;i<gold.Length;i++)
        {
            if(!gold[i]||seen[i])continue;
            int head=0,tail=1,count=0,minX=width,maxX=0,minY=height,maxY=0;queue[0]=i;seen[i]=true;
            while(head<tail)
            {
                int p=queue[head++],x=p%width,y=p/width;count++;
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    int xx=x+dx,yy=y+dy;if(xx<0||yy<0||xx>=width||yy>=height)continue;
                    int n=yy*width+xx;if(!gold[n]||seen[n])continue;seen[n]=true;queue[tail++]=n;
                }
            }
            var box=new Rectangle(area.X+minX,area.Y+minY,maxX-minX+1,maxY-minY+1);
            if(count>=15&&box.Width is >=8 and <=60&&box.Height is >=8 and <=60)components.Add((box,count));
            if(count is >=60 and <=1200&&box.Width is >=16 and <=50&&box.Height is >=16 and <=50&&
                box.Width/(double)box.Height is >=.55 and <=1.8&&count<box.Width*box.Height*.65&&
                !box.IntersectsWith(Close))found.Add(box);
        }
        diagnostic=string.Join(";",components.OrderByDescending(c=>c.Box.Width*c.Box.Height).Take(5)
            .Select(c=>$"{c.Box.X},{c.Box.Y},{c.Box.Width},{c.Box.Height}:{c.Count}"));
        return found;
    }
    internal static WorldMapPixels? Read(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(!Panel(image,labels))return null;
        var markers=MarkerCandidates(image,View);
        if(markers.Count!=1)return null;
        var marker=markers[0];
        return new(HudVision.Png(image,View),View,
            new(marker.Left+marker.Width/2,marker.Top+marker.Height/2),Close);
    }
    internal static string Describe(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return "native_geometry=false";
        string components="none";
        var stars=Panel(image,labels)?MarkerCandidates(image,View,out components):Array.Empty<Rectangle>();
        return $"panel={Panel(image,labels)} close_icon={CloseIcon(image)} close_orange_pixels={CloseOrangePixels(image)} stars={stars.Count} "+
            $"surface_labels={labels.Count(l=>Compact(l.Text)=="gielinorsurface")} search_labels={labels.Count(l=>Compact(l.Text)=="search")} "+
            $"feature_labels={labels.Count(l=>Compact(l.Text)=="gamefeatures")} "+
            $"open_labels={labels.Count(l=>Compact(l.Text)=="openfloatingpanel")} shortcut_labels={labels.Count(l=>Compact(l.Text).TrimEnd('.')=="ctrl+mtoopen")} "+
            $"star_bounds={string.Join(";",stars.Take(3).Select(b=>$"{b.X},{b.Y},{b.Width},{b.Height}"))} gold_components={components}";
    }
    internal static Rectangle? GlobeCandidate(VisualFrame frame)
    {
        if(frame.ScreenBounds.Size!=DockedWindowLayout.GameSize||
            !frame.Labels.Any(l=>l.Kind==VisualLabelKind.GameHud)||frame.Pixels?.Navigation is not NavigationPixels map)return null;
        if(frame.Pixels.WorldMapButton is Rectangle verified)return verified;
        var box=map.MapBounds;
        var point=new Point(box.Left+box.Width/2+66,box.Top+box.Height/2+60);
        var candidate=new Rectangle(point.X-3,point.Y-3,7,7);
        return new Rectangle(Point.Empty,DockedWindowLayout.GameSize).Contains(candidate)?candidate:null;
    }
    internal static Rectangle? Globe(Bitmap image,Rectangle map,out string evidence)
    {
        evidence="globe_not_verified";
        if(image.Size!=DockedWindowLayout.GameSize)return null;
        int proposedX=map.Left+map.Width/2+66,proposedY=map.Top+map.Height/2+60;
        var region=new Rectangle(proposedX-23,proposedY-23,47,47);
        if(!new Rectangle(Point.Empty,image.Size).Contains(region))return null;
        var bits=new byte[47*47];
        for(int y=0;y<47;y++)for(int x=0;x<47;x++)
        {
            var c=image.GetPixel(region.Left+x,region.Top+y);int flags=0;
            if(c.G>75&&c.G>c.R*1.12&&c.G>c.B*1.13)flags|=1;
            if(c.B>90&&c.B>c.R*1.18&&c.B>c.G*1.05)flags|=2;
            if(Math.Min(c.R,Math.Min(c.G,c.B))>115&&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<75)flags|=4;
            if(c.R<95&&c.G<95&&c.B<95)flags|=8;
            bits[y*47+x]=(byte)flags;
        }
        var candidates=new List<(int X,int Y,int Score,int Green,int Blue,int Rim,int Dark)>();
        (int Score,int Green,int Blue,int Rim,int Dark) diagnostic=default;
        for(int cy=15;cy<=31;cy++)for(int cx=15;cx<=31;cx++)
        {
            int green=0,blue=0,rim=0,dark=0;
            for(int y=-9;y<=9;y++)for(int x=-9;x<=9;x++)
                if(x*x+y*y<=81){var p=bits[(cy+y)*47+cx+x];if((p&1)!=0)green++;if((p&2)!=0)blue++;}
            for(int n=0;n<24;n++)
            {
                double angle=n*Math.PI/12;
                int rx=(int)Math.Round(Math.Cos(angle)*11),ry=(int)Math.Round(Math.Sin(angle)*11);
                if((bits[(cy+ry)*47+cx+rx]&4)!=0)rim++;
                rx=(int)Math.Round(Math.Cos(angle)*14);ry=(int)Math.Round(Math.Sin(angle)*14);
                if((bits[(cy+ry)*47+cx+rx]&8)!=0)dark++;
            }
            int diagnosticScore=Math.Min(green,40)+Math.Min(blue,40)+rim*3+dark;
            if(diagnosticScore>diagnostic.Score)diagnostic=(diagnosticScore,green,blue,rim,dark);
            if(green>=8&&blue>=8&&rim>=6&&dark>=4)
                candidates.Add((region.Left+cx,region.Top+cy,green+blue+rim*3,green,blue,rim,dark));
        }
        if(candidates.Count==0){evidence+=$" green={diagnostic.Green} blue={diagnostic.Blue} rim={diagnostic.Rim} dark={diagnostic.Dark}";return null;}
        var best=candidates.MaxBy(c=>c.Score);
        evidence=$"green={best.Green} blue={best.Blue} rim={best.Rim} dark={best.Dark}";
        if(candidates.Any(c=>Math.Max(Math.Abs(c.X-best.X),Math.Abs(c.Y-best.Y))>6&&c.Score>=best.Score*.92))
        {evidence+=" ambiguous=true";return null;}
        return new(best.X-3,best.Y-3,7,7);
    }
    internal static bool OpenTooltip(VisualFrame frame,Rectangle target)
    {
        var area=Rectangle.Intersect(new(0,0,1000,600),new(target.Left-230,target.Top-35,290,150));
        // Either exact native tooltip line identifies the world-map control.
        // The shortcut is useful when OCR loses the adjacent title line.
        return frame.Labels.Any(l=>area.Contains(l.Bounds)&&
            Compact(l.Text).TrimEnd('.') is "openfloatingpanel" or "ctrl+mtoopen");
    }
    internal static bool InputAuthorized(VisualFrame frame,GameInput input)
    {
        if(frame.ScreenBounds.Size!=DockedWindowLayout.GameSize||
            GameScreenReader.Read(frame).Stage!=VisibleGameStage.InGame)return false;
        if(input.Kind==GameInputKind.LeftClick&&input.Bounds==Close)
            return frame.Pixels is {WorldMapPanelOpen:true,WorldMapCloseVisible:true};
        if(GlobeCandidate(frame) is not Rectangle candidate)return false;
        var point=new Point(input.Bounds.Left+input.Bounds.Width/2,input.Bounds.Top+input.Bounds.Height/2);
        if(!candidate.Contains(point)||frame.Pixels?.WorldMapPanelOpen==true)return false;
        return input.Kind==GameInputKind.Hover||input.Kind==GameInputKind.LeftClick&&
            (frame.Pixels?.WorldMapButton is not null||OpenTooltip(frame,input.Bounds));
    }
}
