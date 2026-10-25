using System.Drawing;

namespace OSRSLiveDiagnostics;

// Recognize empty inventory slots from the current *selected* inventory
// panel. OSRS paints a mottled stone background; it is not a single flat
// RGB rectangle. A missing context menu is NEVER used as proof of emptiness.
internal static class NativeInventoryEmptyReader
{
    private sealed record Cell(int Index,Color Background,int StrongPixels,
        int SaturatedPixels,int MeanDeviation,bool PlausiblyBlank);
    internal sealed record Result(bool[] Empty,string FirstSlotDiagnostic,IReadOnlyList<string> SlotDiagnostics);

    private static int Difference(Color a,Color b)=>
        Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));

    private static int Median(IEnumerable<int> source)
    {
        var sorted=source.Order().ToArray();
        return sorted[sorted.Length/2];
    }

    private static Cell Score(Bitmap bitmap,int index)
    {
        var r=ExecutionUi.InventorySlot(index);
        var corners=new List<Color>(64);
        foreach(int ox in new[]{4,24})foreach(int oy in new[]{4,24})
            for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++)
                corners.Add(bitmap.GetPixel(r.X+ox+dx,r.Y+oy+dy));
        var bg=Color.FromArgb(Median(corners.Select(c=>(int)c.R)),
            Median(corners.Select(c=>(int)c.G)),
            Median(corners.Select(c=>(int)c.B)));
        int strong=0,saturated=0,total=0;
        const int x0=7,y0=7,width=18,height=18;
        for(int y=y0;y<y0+height;y++)for(int x=x0;x<x0+width;x++)
        {
            var c=bitmap.GetPixel(r.X+x,r.Y+y);
            int d=Difference(c,bg);
            total+=d;
            if(d>52)strong++;
            int hi=Math.Max(c.R,Math.Max(c.G,c.B));
            int lo=Math.Min(c.R,Math.Min(c.G,c.B));
            if(hi>147&&hi-lo>60)saturated++;
        }
        int mean=total/(width*height);
        // Deliberately conservative: a large/dark icon or quantity glyph
        // must not become "empty" just because other items share its colour.
        bool tone=bg.R is >=18 and <=155&&bg.G is >=16 and <=150&&
            bg.B is >=15 and <=145&&
            Math.Max(bg.R,Math.Max(bg.G,bg.B))-
            Math.Min(bg.R,Math.Min(bg.G,bg.B))<=62;
        bool plausible=tone&&strong<=22&&saturated<=10&&mean<=32;
        return new(index,bg,strong,saturated,mean,plausible);
    }

    internal static Result Classify(Bitmap bitmap)
    {
        var empty=new bool[28];
        if(bitmap.Size!=DockedWindowLayout.GameSize)
            return new(empty,"wrong_viewport",Array.Empty<string>());
        var cells=Enumerable.Range(0,28).Select(i=>Score(bitmap,i)).ToArray();
        var compatible=cells.Where(c=>c.PlausiblyBlank).ToArray();
        foreach(var cell in compatible)
        {
            // Consistency among different cells rejects a lone smooth icon,
            // tile overlays, and accidental low-contrast game scenery.
            int peers=compatible.Count(other=>
                Difference(other.Background,cell.Background)<=18&&
                Math.Abs(other.MeanDeviation-cell.MeanDeviation)<=13);
            if(peers>=8)empty[cell.Index]=true;
        }
        // Report the cell under the actual directed right-click, not always
        // slot zero. This is diagnostic evidence, never proof of an empty item.
        var samples=cells.Select(cell=>
        {
            int peers=compatible.Count(other=>
                Difference(other.Background,cell.Background)<=18&&
                Math.Abs(other.MeanDeviation-cell.MeanDeviation)<=13);
            return $"slot{cell.Index}_pixels=strong:{cell.StrongPixels},saturated:{cell.SaturatedPixels},"+
                $"mean:{cell.MeanDeviation},peers:{peers},plausible:{cell.PlausiblyBlank}";
        }).ToArray();
        return new(empty,samples[0],samples);
    }

    internal static bool[] Read(Bitmap bitmap)=>Classify(bitmap).Empty;

    internal static void SelfTest()
    {
        using var sample=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(sample))g.Clear(Color.Black);
        var rng=new Random(51721);
        for(int i=0;i<28;i++)
        {
            var r=ExecutionUi.InventorySlot(i);
            // Textured brown/stone background: formerly the >12 changed
            // pixels rule incorrectly rejected every slot.
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                int grain=rng.Next(-14,15);
                sample.SetPixel(r.X+x,r.Y+y,Color.FromArgb(
                    Math.Clamp(82+grain,0,255),Math.Clamp(73+grain,0,255),
                    Math.Clamp(62+grain,0,255)));
            }
        }
        var item=ExecutionUi.InventorySlot(0);
        using(var g=Graphics.FromImage(sample))
        {
            g.FillEllipse(Brushes.OrangeRed,new Rectangle(item.X+8,item.Y+7,16,17));
            g.FillRectangle(Brushes.Yellow,new Rectangle(item.X+5,item.Y+4,9,6));
        }
        var read=Read(sample);
        var metrics=Classify(sample);
        if(metrics.SlotDiagnostics.Count!=28||
            metrics.SlotDiagnostics[0]!=metrics.FirstSlotDiagnostic||
            !metrics.SlotDiagnostics[3].StartsWith("slot3_pixels=",StringComparison.Ordinal)||
            metrics.SlotDiagnostics[3].Contains("slot0_pixels",StringComparison.Ordinal))
            throw new Exception("Inventory pixel diagnostics must identify the clicked slot");
        if(read[0]||read.Skip(1).Count(x=>x)!=27)
            throw new Exception("Textured native inventory must reject foreground icons but identify 27 blanks");
        // End-to-end: only the visually selected upper Inventory panel can
        // surface these blank hints through the ordinary vision pipeline.
        using(var g=Graphics.FromImage(sample))
            g.FillRectangle(new SolidBrush(Color.FromArgb(125,31,28)),InventoryTabPolicy.Icon);
        var vision=ExecutionVision.Read(sample,Array.Empty<VisualLabel>());
        if(vision is null||!vision.InventoryTabActive||
            vision.Slots[0].Empty||vision.Slots.Skip(1).Count(slot=>slot.Empty)!=27)
            throw new Exception("Native inventory emptiness not plumbed through live ExecutionVision");
        if(ExecutionVision.SlotDiagnostic(vision,3)!=metrics.SlotDiagnostics[3]||
            ExecutionVision.SlotDiagnostic(vision,0)!=metrics.FirstSlotDiagnostic||
            ExecutionVision.SlotDiagnostic(vision,28)!="invalid_slot")
            throw new Exception("Directed inventory pixel log must describe the clicked slot");
        using(var g=Graphics.FromImage(sample))g.FillRectangle(Brushes.Black,InventoryTabPolicy.Icon);
        var inactive=ExecutionVision.Read(sample,Array.Empty<VisualLabel>());
        if(inactive is null||inactive.InventoryTabActive||inactive.Slots.Any(slot=>slot.Empty))
            throw new Exception("Inactive Inventory panel must never present empty slot evidence");
        for(int i=1;i<=20;i++)
        {
            var r=ExecutionUi.InventorySlot(i);
            using var g=Graphics.FromImage(sample);
            g.FillEllipse(Brushes.OrangeRed,new Rectangle(r.X+8,r.Y+7,16,17));
        }
        if(Read(sample).Any(x=>x))
            throw new Exception("Fewer than eight supported blank cells must never count as empty");
        using var fullOfSameIcons=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(fullOfSameIcons))g.Clear(Color.FromArgb(82,73,62));
        for(int i=0;i<28;i++)
        {
            var r=ExecutionUi.InventorySlot(i);
            using var g=Graphics.FromImage(fullOfSameIcons);
            g.FillEllipse(Brushes.OrangeRed,new Rectangle(r.X+8,r.Y+7,16,17));
        }
        if(Read(fullOfSameIcons).Any(x=>x))
            throw new Exception("Repeated item sprites must not be mistaken for repeated blank cells");
        using var falsePanel=new Bitmap(800,600);
        if(Read(falsePanel).Any(x=>x)||Classify(falsePanel).FirstSlotDiagnostic!="wrong_viewport")
            throw new Exception("Wrong viewport must not authorize inventory emptiness");
    }
}
