using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal static class ExecutionVision
{
    internal static ExecutionPixels? Read(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return null;
        bool inventoryActive=InventoryTabPolicy.Active(image);
        var emptyResult=inventoryActive?NativeInventoryEmptyReader.Classify(image):null;
        var emptyEvidence=emptyResult?.Empty??new bool[28];
        var digests=inventoryActive?InventoryDigests(image):null;
        var slots=Enumerable.Range(0,28).Select(i=>
        {
            var box=ExecutionUi.InventorySlot(i);
            var quantities=labels.Where(l=>l.Kind==VisualLabelKind.InventoryQuantity&&l.Text.StartsWith(i+"=",StringComparison.Ordinal))
                .Select(l=>int.TryParse(l.Text[(l.Text.IndexOf('=')+1)..],out int n)?(int?)n:null).ToArray();
            var core=new Rectangle(box.X+7,box.Y+7,18,18);
            return new ObservedSlot(i,box,Signature(image,box),
                emptyEvidence[i]&&quantities.Length==0,quantities.Length==1?quantities[0]:null,
                Signature(image,core),digests?[i]);
        }).ToArray();
        NavigationPixels? navigation=null;
        var hpLabels=labels.Where(l=>l.Kind==VisualLabelKind.Hitpoints).ToArray();
        int? hitpoints=hpLabels.Length==1&&int.TryParse(hpLabels[0].Text,out int value)&&
            value is >=1 and <=99?value:null;
        var maps=labels.Where(l=>l.Kind==VisualLabelKind.Minimap).ToArray();
        if(maps.Length==1)
            navigation=new(HudVision.Png(image,maps[0].Bounds),maps[0].Bounds,hitpoints);
        else if(maps.Length==0&&labels.Any(l=>l.Kind==VisualLabelKind.GameHud)&&
            HudVision.Minimap(image) is Rectangle verifiedMap)
        {
            // A focused inventory/menu OCR frame can preserve the native
            // game HUD but omit the minimap label. Only the independently
            // pixel-verified unique player-centred circle may restore it.
            // Ambiguous/minimap-free frames still provide no coordinates.
            navigation=new(HudVision.Png(image,verifiedMap),verifiedMap,hitpoints);
        }
        // Diagnostic counts identify missing/ambiguous red pixels without
        // exposing image data, character names or guessed health values.
        int redIcons=hitpoints is null?HudVision.RedHealthIconCandidates(image).Count:0;
        return new(slots,Signature(image,new Rectangle(30,50,690,300)),navigation,
            inventoryActive,Signature(image,new Rectangle(746,304,220,265)),
            emptyResult?.FirstSlotDiagnostic??"inventory_inactive",
            emptyResult?.SlotDiagnostics,hitpoints,
            $"red_icon_components={redIcons} hp_labels={hpLabels.Length}");
    }
    internal static string SlotDiagnostic(ExecutionPixels pixels,int slot)
    {
        if(slot is <0 or >=28)return "invalid_slot";
        if(pixels.InventorySlotDiagnostics is { Count:28 } all)return all[slot];
        return slot==0?pixels.InventorySlot0Diagnostic:"not_sampled";
    }
    internal static string[] InventoryDigests(Bitmap image)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return Array.Empty<string>();
        var result=new string[28];
        var data=image.LockBits(new Rectangle(Point.Empty,image.Size),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes=new byte[32*32*4];
            for(int i=0;i<28;i++)
            {
                var box=ExecutionUi.InventorySlot(i);
                for(int row=0;row<box.Height;row++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        IntPtr.Add(data.Scan0,(box.Y+row)*data.Stride+box.X*4),bytes,row*box.Width*4,box.Width*4);
                result[i]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            }
        }
        finally{image.UnlockBits(data);}
        return result;
    }
    internal static async Task<IReadOnlyList<VisualLabel>> ReadHealth(Bitmap image,OcrEngine engine,IReadOnlyList<VisualLabel> labels)
    {
        var found=new List<VisualLabel>();
        var minimap=HudVision.Minimap(image);
        if(minimap is Rectangle map)found.Add(new("MINIMAP",map,VisualLabelKind.Minimap));
        foreach(var b in HealthNumberCandidates(image,labels,minimap))
        {
            if(!new Rectangle(Point.Empty,image.Size).Contains(b)||
                !HudVision.HeartRightOf(image,b))continue;
            // The small native HP digits use the same public P11 bitmap
            // font as skill levels, but change colour with health. Decode
            // exact visible glyphs before trying general-purpose OCR.
            int? native=NativeSkillFontReader.ReadNumber(image,b,c=>
                c.G>160&&c.G>c.R*1.4&&c.G>c.B*1.4||
                c.R>160&&c.R>c.G*1.4&&c.R>c.B*1.4||
                c.R>160&&c.G>140&&c.B<130);
            if(native is int hp)
            {found.Add(new(hp.ToString(),b,VisualLabelKind.Hitpoints));break;}
            using var contrast=GameplayHudOcr.Contrast(image,b);
            using var crop=new Bitmap(b.Width*4,b.Height*4);
            using(var g=Graphics.FromImage(crop))
            {g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.DrawImage(contrast,new Rectangle(Point.Empty,crop.Size));}
            var ocr=await VisualStartup.RecognizeRaw(crop,engine);
            var words=ocr.Lines.SelectMany(l=>l.Words).Select(w=>w.Text.Trim()).ToArray();
            if(words.Length!=1||!Regex.IsMatch(words[0],@"^\d{1,2}$"))continue;
            found.Add(new(words[0],b,VisualLabelKind.Hitpoints));
            break;
        }
        return found;
    }
    internal static IReadOnlyList<Rectangle> HealthNumberCandidates(Bitmap image,
        IReadOnlyList<VisualLabel> labels,Rectangle? minimap)
    {
        var candidates=new List<Rectangle>();
        if(minimap is Rectangle map)
            candidates.Add(HudVision.HealthNumber(image,map,labels)??
                new Rectangle(map.Left-62,map.Top+48,42,28));
        // A detected minimap does not guarantee that its relative digit crop
        // contains the HP text. If that crop fails, independently verified
        // native HUD + red-orb pixels still provide the standalone OCR path.
        if(NativeGameHudReader.Read(image,labels) is not null&&
            HudVision.StandaloneHealthNumber(image) is Rectangle standalone)
            candidates.Add(standalone);
        return candidates.Distinct().ToArray();
    }
    internal static ulong Signature(Bitmap image,Rectangle box)
    {
        ulong hash=14695981039346656037;
        for(int y=box.Top;y<box.Bottom;y+=3)for(int x=box.Left;x<box.Right;x+=3)
        {var c=image.GetPixel(x,y);hash=unchecked((hash^(uint)c.ToArgb())*1099511628211);}
        return hash;
    }
    internal static async Task<IReadOnlyList<VisualLabel>> ReadQuantities(Bitmap image,OcrEngine engine)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return Array.Empty<VisualLabel>();
        const int tw=176,th=56;
        using var sheet=new Bitmap(tw*7,th*4);
        using(var g=Graphics.FromImage(sheet))
        {
            g.Clear(Color.Black);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for(int i=0;i<28;i++)
            {
                var b=ExecutionUi.InventorySlot(i);b.Height=12;
                g.DrawImage(image,new Rectangle(i%7*tw+4,i/7*th+4,128,48),b,GraphicsUnit.Pixel);
            }
        }
        var result=await VisualStartup.RecognizeRaw(sheet,engine);
        var values=new Dictionary<int,List<int>>();
        foreach(var word in result.Lines.SelectMany(l=>l.Words))
        {
            // K/M abbreviations are intentionally not turned into exact stock.
            if(!Regex.IsMatch(word.Text,@"^[1-9]\d{0,8}$")||!int.TryParse(word.Text,out int n))continue;
            int col=(int)(word.BoundingRect.X+word.BoundingRect.Width/2)/tw;
            int row=(int)(word.BoundingRect.Y+word.BoundingRect.Height/2)/th;
            int i=row*7+col;if(col>=7||i>=28)continue;
            if(!values.TryGetValue(i,out var hits)){hits=new();values[i]=hits;}
            hits.Add(n);
        }
        return values.Where(v=>v.Value.Count==1).Select(v=>new VisualLabel(v.Key+"="+v.Value[0],
            ExecutionUi.InventorySlot(v.Key),VisualLabelKind.InventoryQuantity)).ToArray();
    }
}
