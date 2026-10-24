using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal static class GameplayHudOcr
{
    internal static async Task<IReadOnlyList<VisualLabel>> Read(Bitmap image,OcrEngine engine)
    {
        if(image.Size!=new Size(1000,600))return Array.Empty<VisualLabel>();
        var found=new List<VisualLabel>();
        // Isolate each control's title from its green On/Off state. The native
        // outline font and textured button backgrounds defeat broad OCR even
        // after scaling. Read a high-contrast contact sheet in one OCR pass.
        var regions=new List<(Rectangle Source,Rectangle Sheet,int Scale)>();
        foreach(var box in new[]{new Rectangle(120,575,61,14),new Rectangle(182,575,61,14),
            new Rectangle(368,575,61,14),new Rectangle(430,575,87,24)})
            regions.Add((box,new Rectangle(regions.Count*390,0,box.Width*4,box.Height*4),4));
        regions.Add((new(0,547,450,28),new(0,125,1350,84),3));
        regions.Add((new(0,0,500,42),new(0,245,1500,126),3));
        regions.Add((new(740,265,255,315),new(0,400,765,945),3));
        regions.Add((new(740,0,260,235),new(800,400,780,705),3));
        regions=regions.Select(r=>(r.Source,new Rectangle(r.Sheet.X+10,r.Sheet.Y+20,r.Sheet.Width,r.Sheet.Height),r.Scale)).ToList();
        using var sheet=new Bitmap(1600,1380);
        using(var graphics=Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.White);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            foreach(var region in regions)
            {
                using var crop=Contrast(image,region.Source);
                graphics.DrawImage(crop,region.Sheet);
            }
        }
        var result=await VisualStartup.RecognizeRaw(sheet,engine);
        foreach(var line in result.Lines.Where(l=>l.Words.Count>0))
        {
            var bounds=line.Words.Select(w=>Rect(w.BoundingRect)).Aggregate(Rectangle.Union);
            var owner=regions.Where(r=>r.Sheet.Contains(bounds)).ToArray();
            if(owner.Length==1)found.Add(new(line.Text,Map(bounds,owner[0])));
            foreach(var word in line.Words)
            {
                var box=Rect(word.BoundingRect);owner=regions.Where(r=>r.Sheet.Contains(box)).ToArray();
                if(owner.Length!=1)continue;
                string text=word.Text.Trim();
                // Numeric words and known UI titles may be separate from a
                // line spanning several contact-sheet cells. Never invent a
                // character prompt by concatenating unrelated OCR words.
                if(Regex.IsMatch(text,@"^\d{1,3}$"))found.Add(new(text,Map(box,owner[0]),VisualLabelKind.Number));
                else if(new[]{"Public","Private","Trade","Report","Skills","Inventory","Magic"}.Contains(text,StringComparer.OrdinalIgnoreCase))
                    found.Add(new(text,Map(box,owner[0])));
            }
        }
        static Rectangle Rect(Windows.Foundation.Rect r)=>new((int)r.X,(int)r.Y,(int)Math.Ceiling(r.Width),(int)Math.Ceiling(r.Height));
        static Rectangle Map(Rectangle b,(Rectangle Source,Rectangle Sheet,int Scale) r)=>new(r.Source.Left+(b.Left-r.Sheet.Left)/r.Scale,
            r.Source.Top+(b.Top-r.Sheet.Top)/r.Scale,Math.Max(1,(int)Math.Ceiling(b.Width/(double)r.Scale)),Math.Max(1,(int)Math.Ceiling(b.Height/(double)r.Scale)));
        return found;
    }
    internal static Bitmap Contrast(Bitmap image,Rectangle box)
    {
        var output=new Bitmap(box.Width,box.Height);
        for(int y=0;y<box.Height;y++)for(int x=0;x<box.Width;x++)
        {
            var c=image.GetPixel(box.Left+x,box.Top+y);
            double light=.299*c.R+.587*c.G+.114*c.B;
            if(c.G>100&&c.G>c.R*1.4&&c.G>c.B*1.4)light=c.G;
            int value=255-(int)Math.Clamp((light-70)*255/125,0,255);
            output.SetPixel(x,y,Color.FromArgb(value,value,value));
        }
        return output;
    }
}
