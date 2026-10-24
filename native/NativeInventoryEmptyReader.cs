using System.Drawing;

namespace OSRSLiveDiagnostics;

// Conservative pixel-only evidence for an unoccupied native inventory slot.
// A right-click on an empty slot normally produces NO "Choose Option" menu.
// Do not infer emptiness merely because context OCR or window input failed.
internal static class NativeInventoryEmptyReader
{
    private sealed record Candidate(int Index,Color Background);

    private static Color Median(List<Color> values)
    {
        static int Mid(IEnumerable<int> v)
        {
            var ordered=v.Order().ToArray();
            return ordered[ordered.Length/2];
        }
        return Color.FromArgb(Mid(values.Select(c=>(int)c.R)),Mid(values.Select(c=>(int)c.G)),
            Mid(values.Select(c=>(int)c.B)));
    }

    private static bool Similar(Color a,Color b,int threshold)=>
        Math.Abs(a.R-b.R)<=threshold&&Math.Abs(a.G-b.G)<=threshold&&
        Math.Abs(a.B-b.B)<=threshold;

    private static Candidate? CandidateSlot(Bitmap image,int index)
    {
        var rect=ExecutionUi.InventorySlot(index);
        if(!new Rectangle(Point.Empty,image.Size).Contains(rect))return null;
        var corners=new List<Color>();
        // Sample the slot's unoccupied inner corners, away from its border.
        foreach(int ox in new[]{4,24})foreach(int oy in new[]{4,24})
        for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++)
            corners.Add(image.GetPixel(rect.X+ox+dx,rect.Y+oy+dy));
        var background=Median(corners);
        // An empty OSRS slot is subdued stone/brown/grey background, not an
        // icon or a bright context-menu overlay. This guard is intentionally
        // restrictive: unknown-looking slots remain unclassified.
        if(background.R is <20 or >145||background.G is <18 or >145||
            background.B is <16 or >135||Math.Max(background.R,
            Math.Max(background.G,background.B))-Math.Min(background.R,
            Math.Min(background.G,background.B))>55)return null;
        int nonBackground=0;
        for(int y=8;y<24;y++)for(int x=8;x<24;x++)
        {
            if(!Similar(image.GetPixel(rect.X+x,rect.Y+y),background,26))
                nonBackground++;
            if(nonBackground>12)return null;
        }
        return new(index,background);
    }

    internal static bool[] Read(Bitmap bitmap)
    {
        var result=new bool[28];
        if(bitmap.Size!=DockedWindowLayout.GameSize)return result;
        var candidates=Enumerable.Range(0,28).Select(i=>CandidateSlot(bitmap,i))
            .Where(c=>c is not null).Cast<Candidate>().ToArray();
        // Distinct graphical items can repeat across many slots; reject all
        // groups smaller than eight and require a common low-contrast ground.
        foreach(var c in candidates)
        {
            var matches=candidates.Where(other=>
                Similar(c.Background,other.Background,12)).ToArray();
            if(matches.Length>=8)result[c.Index]=true;
        }
        return result;
    }

    internal static void SelfTest()
    {
        using var sample=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(sample))g.Clear(Color.Black);
        foreach(int i in Enumerable.Range(0,28))
        {
            var r=ExecutionUi.InventorySlot(i);
            using var g=Graphics.FromImage(sample);
            g.FillRectangle(new SolidBrush(Color.FromArgb(74,68,59)),r);
        }
        // Different item sprites and quantity text must never be classified as
        // the empty canvas just because neighboring inventory cells are empty.
        var occupied=ExecutionUi.InventorySlot(0);
        using(var g=Graphics.FromImage(sample))
        {
            g.FillEllipse(Brushes.OrangeRed,new Rectangle(occupied.X+8,occupied.Y+7,16,17));
            g.FillRectangle(Brushes.Yellow,new Rectangle(occupied.X+5,occupied.Y+4,9,6));
        }
        var read=Read(sample);
        if(read[0]||read.Skip(1).Count(x=>x)!=27)
            throw new Exception("Native empty inventory pixels / foreground-item rejection failed");
        for(int i=1;i<=20;i++)
        {
            var r=ExecutionUi.InventorySlot(i);
            using var g=Graphics.FromImage(sample);
            g.FillEllipse(Brushes.OrangeRed,new Rectangle(r.X+8,r.Y+7,16,17));
        }
        var onlySeven=Read(sample);
        if(onlySeven.Any(x=>x)&&onlySeven.Skip(21).Count(x=>x)<8)
            throw new Exception("Too few matched empty slots must never authorize emptiness");
        using var falsePanel=new Bitmap(800,600);
        if(Read(falsePanel).Any(x=>x))
            throw new Exception("Unsupported game size cannot authorize empty inventory");
    }
}
