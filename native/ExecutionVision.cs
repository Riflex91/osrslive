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
        var slots=Enumerable.Range(0,28).Select(i=>
        {
            var box=ExecutionUi.InventorySlot(i);
            var quantities=labels.Where(l=>l.Kind==VisualLabelKind.InventoryQuantity&&l.Text.StartsWith(i+"=",StringComparison.Ordinal))
                .Select(l=>int.TryParse(l.Text[(l.Text.IndexOf('=')+1)..],out int n)?(int?)n:null).ToArray();
            var core=new Rectangle(box.X+7,box.Y+7,18,18);
            return new ObservedSlot(i,box,Signature(image,box),
                emptyEvidence[i]&&quantities.Length==0,quantities.Length==1?quantities[0]:null,
                Signature(image,core));
        }).ToArray();
        NavigationPixels? navigation=null;
        var maps=labels.Where(l=>l.Kind==VisualLabelKind.Minimap).ToArray();
        if(maps.Length==1)
        {
            var hp=labels.Where(l=>l.Kind==VisualLabelKind.Hitpoints).ToArray();
            navigation=new(HudVision.Png(image,maps[0].Bounds),maps[0].Bounds,
                hp.Length==1&&int.TryParse(hp[0].Text,out int value)?value:null);
        }
        return new(slots,Signature(image,new Rectangle(30,50,690,300)),navigation,
            inventoryActive,Signature(image,new Rectangle(746,304,220,265)),
            emptyResult?.FirstSlotDiagnostic??"inventory_inactive");
    }
    internal static async Task<IReadOnlyList<VisualLabel>> ReadHealth(Bitmap image,OcrEngine engine,IReadOnlyList<VisualLabel> labels)
    {
        if(HudVision.Minimap(image) is not Rectangle map)return Array.Empty<VisualLabel>();
        var found=new List<VisualLabel>{new("MINIMAP",map,VisualLabelKind.Minimap)};
        var detected=HudVision.HealthNumber(image,map,labels);
        var b=detected??new Rectangle(map.Left-62,map.Top+48,42,28);
        if(!new Rectangle(Point.Empty,image.Size).Contains(b)||!HudVision.HeartRightOf(image,b))return found;
        using var contrast=GameplayHudOcr.Contrast(image,b);
        using var crop=new Bitmap(b.Width*4,b.Height*4);
        using(var g=Graphics.FromImage(crop))
        {g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.DrawImage(contrast,new Rectangle(Point.Empty,crop.Size));}
        var ocr=await VisualStartup.RecognizeRaw(crop,engine);
        var words=ocr.Lines.SelectMany(l=>l.Words).Select(w=>w.Text.Trim()).ToArray();
        if(words.Length==1&&Regex.IsMatch(words[0],@"^\d{1,2}$"))found.Add(new(words[0],b,VisualLabelKind.Hitpoints));
        return found;
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
