using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal static class GameplayHudOcr
{
    internal static async Task<IReadOnlyList<VisualLabel>> Read(Bitmap image,OcrEngine engine)
    {
        if(image.Size!=new Size(1000,600))return Array.Empty<VisualLabel>();
        var found=new List<VisualLabel>();
        // Native bitmap text gets lost in whole-frame OCR. Read the chat/HUD
        // separately at 4x and retain source coordinates and word semantics.
        foreach(var box in new[]{new Rectangle(0,500,550,100),new Rectangle(740,0,260,235)})
        {
            using var enlarged=new Bitmap(box.Width*4,box.Height*4);
            using(var graphics=Graphics.FromImage(enlarged))
            {graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;graphics.DrawImage(image,new Rectangle(Point.Empty,enlarged.Size),box,GraphicsUnit.Pixel);}
            var result=await VisualStartup.RecognizeRaw(enlarged,engine);
            Rectangle Source(Windows.Foundation.Rect r)=>new(box.Left+(int)(r.X/4),box.Top+(int)(r.Y/4),Math.Max(1,(int)Math.Ceiling(r.Width/4)),Math.Max(1,(int)Math.Ceiling(r.Height/4)));
            foreach(var line in result.Lines.Where(l=>l.Words.Count>0))
            {
                found.Add(new(line.Text,line.Words.Select(w=>Source(w.BoundingRect)).Aggregate(Rectangle.Union)));
                foreach(var word in line.Words)
                    if(Regex.IsMatch(word.Text,@"^\d{1,3}$"))found.Add(new(word.Text,Source(word.BoundingRect),VisualLabelKind.Number));
            }
        }
        return found;
    }
}
