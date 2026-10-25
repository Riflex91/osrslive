using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

// A context menu exists only after a directed right-click. Read its visible
// native pixels in a separate focused OCR pass rather than hoping that the
// whole-frame OCR preserves tiny lines on a dark menu background.
// This reader never creates an action or an item name from a slot position.
internal static class ContextMenuOcr
{
    internal static Rectangle Region(Point anchor,Size size)
    {
        const int width=400,height=430;
        if(size.Width<width||size.Height<height||anchor.X<0||anchor.Y<0||
            anchor.X>=size.Width||anchor.Y>=size.Height)return Rectangle.Empty;
        // OSRS flips a menu horizontally/vertically near the viewport edge.
        // Retain the anchor and both potential placements in the OCR crop.
        int x=Math.Clamp(anchor.X-260,0,size.Width-width);
        int y=Math.Clamp(anchor.Y-190,0,size.Height-height);
        return new Rectangle(x,y,width,height);
    }

    internal static async Task<IReadOnlyList<VisualLabel>> Read(
        Bitmap image,OcrEngine engine,Point anchor)
    {
        var region=Region(anchor,image.Size);
        if(region.IsEmpty||image.Size!=DockedWindowLayout.GameSize)
            return Array.Empty<VisualLabel>();
        // Every OCR result must physically come from the clicked window, and
        // each pass must independently contain a header and terminal Cancel.
        // No row/index heuristic fills missing OCR lines. Keep non-actionable
        // OCR candidates solely for counting diagnostics when this fails.
        var diagnosticCandidates=new List<VisualLabel>();
        for(int pass=0;pass<2;pass++)
        {
            const int scale=3,padding=12;
            using var crop=pass==0?
                GameplayHudOcr.Contrast(image,region):
                image.Clone(region,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var sheet=new Bitmap(region.Width*scale+padding*2,
                region.Height*scale+padding*2);
            using(var graphics=Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode=pass==0?
                    System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor:
                    System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(crop,new Rectangle(padding,padding,
                    crop.Width*scale,crop.Height*scale));
            }
            var raw=await VisualStartup.RecognizeRaw(sheet,engine);
            var labels=new List<VisualLabel>();
            foreach(var line in raw.Lines.Where(l=>l.Words.Count>0))
            {
                var bounds=line.Words.Select(w=>new Rectangle(
                    (int)Math.Floor((w.BoundingRect.X-padding)/(double)scale)+region.X,
                    (int)Math.Floor((w.BoundingRect.Y-padding)/(double)scale)+region.Y,
                    Math.Max(1,(int)Math.Ceiling(w.BoundingRect.Width/scale)),
                    Math.Max(1,(int)Math.Ceiling(w.BoundingRect.Height/scale))))
                    .Aggregate(Rectangle.Union);
                if(region.Contains(bounds)&&line.Text.Length is >0 and <=90)
                    labels.Add(new(line.Text,bounds));
            }
            diagnosticCandidates.AddRange(labels.Select(l=>
                l with {Kind=VisualLabelKind.ContextMenuCandidate}));
            // Live OCR sees Examine and Cancel but frequently misses the
            // small yellow "Choose Option" heading in a 400x430 crop. Retry
            // ONLY its visible pixels at a small, anchored 220x~75 region.
            // A valid header must be read from pixels, never inferred from
            // the existence of an Examine/Cancel pair.
            if(!labels.Any(l=>l.Text.Trim().Equals("Choose Option",StringComparison.OrdinalIgnoreCase))&&
                labels.Any(l=>l.Text.Trim().StartsWith("Examine ",StringComparison.OrdinalIgnoreCase))&&
                labels.Any(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase)))
            {
                var recovered=await RecoverHeader(image,engine,anchor,labels);
                if(recovered is not null)labels.Add(recovered);
            }
            if(ExecutionUi.Menu(labels) is not ContextMenu parsed||parsed.Entries.Count==0)
                continue;
            var header=labels.Single(l=>l.Text.Trim().Equals("Choose Option",StringComparison.OrdinalIgnoreCase));
            var cancel=labels.Where(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase)&&
                l.Bounds.Top>header.Bounds.Bottom&&Math.Abs(l.Bounds.Left-header.Bounds.Left)<30)
                .OrderBy(l=>l.Bounds.Top).First();
            // The menu must sit next to (or be flipped around) the actual
            // right-click position, not elsewhere in the game window.
            var menuBounds=Rectangle.FromLTRB(
                Math.Max(0,header.Bounds.Left-18),Math.Max(0,header.Bounds.Top-8),
                Math.Min(image.Width,header.Bounds.Left+290),
                Math.Min(image.Height,cancel.Bounds.Bottom+8));
            // Near the lower/right viewport edge the native menu can flip
            // above or beside the click, leaving a small gap at its border.
            // Accept a nearby visible menu, never one elsewhere in the frame.
            menuBounds.Inflate(24,24);
            menuBounds.Intersect(new Rectangle(Point.Empty,image.Size));
            if(!menuBounds.Contains(anchor))continue;
            return labels.Where(l=>l.Bounds.Top>=header.Bounds.Top&&
                l.Bounds.Bottom<=cancel.Bounds.Bottom+2&&
                Math.Abs(l.Bounds.Left-header.Bounds.Left)<30)
                .Select(l=>l with {Kind=VisualLabelKind.ContextMenuLine}).ToArray();
        }
        // Candidate labels are deliberately NOT recognized menu lines, so
        // neither interactions nor item identification can use partial OCR.
        return diagnosticCandidates;
    }

    // This is the only header fallback. The independent OCR must actually
    // read "Choose Option" (or its two precisely aligned words) from the
    // captured frame. No geometry alone fabricates the menu's identity.
    internal static async Task<VisualLabel?> RecoverHeader(
        Bitmap image,OcrEngine engine,Point anchor,IReadOnlyList<VisualLabel> menuCandidates)
    {
        if(image.Size!=DockedWindowLayout.GameSize||anchor.X<0||anchor.Y<0||
            anchor.X>=image.Width||anchor.Y>=image.Height)return null;
        var examines=menuCandidates.Where(l=>l.Text.Trim().StartsWith("Examine ",StringComparison.OrdinalIgnoreCase)).ToArray();
        var cancels=menuCandidates.Where(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(examines.Length!=1||cancels.Length!=1)return null;
        var examine=examines[0];var cancel=cancels[0];
        if(examine.Bounds.Bottom>=cancel.Bounds.Top||
            Math.Abs(examine.Bounds.Left-cancel.Bounds.Left)>22||
            cancel.Bounds.Top-examine.Bounds.Top>125||
            Math.Abs(examine.Bounds.Left-anchor.X)>85)return null;
        // Some OSRS context menus open above the cursor. Probe bounded
        // strips near the click and immediately above the Examine line.
        var regions=new[]{
            new Rectangle(Math.Clamp(examine.Bounds.Left-16,0,image.Width-220),
                Math.Clamp(anchor.Y-35,0,image.Height-92),220,92),
            new Rectangle(Math.Clamp(examine.Bounds.Left-16,0,image.Width-220),
                Math.Clamp(examine.Bounds.Top-115,0,image.Height-140),220,140)
        };
        var found=new List<VisualLabel>();
        foreach(var region in regions.Distinct())
        {
            const int scale=4;
            using var contrast=GameplayHudOcr.Contrast(image,region);
            using var enlarged=new Bitmap(region.Width*scale+24,region.Height*scale+24);
            using(var graphics=Graphics.FromImage(enlarged))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.DrawImage(contrast,new Rectangle(12,12,region.Width*scale,region.Height*scale));
            }
            var result=await VisualStartup.RecognizeRaw(enlarged,engine);
            foreach(var line in result.Lines.Where(l=>l.Words.Count>0))
            {
                string text=System.Text.RegularExpressions.Regex.Replace(line.Text.Trim(),@"\s+"," ");
                if(!text.Equals("Choose Option",StringComparison.OrdinalIgnoreCase))continue;
                var r=line.Words.Select(w=>new Rectangle(
                    region.Left+(int)Math.Floor((w.BoundingRect.X-12)/scale),
                    region.Top+(int)Math.Floor((w.BoundingRect.Y-12)/scale),
                    Math.Max(1,(int)Math.Ceiling(w.BoundingRect.Width/scale)),
                    Math.Max(1,(int)Math.Ceiling(w.BoundingRect.Height/scale))))
                    .Aggregate(Rectangle.Union);
                // Require a real heading, above the matched Examine/Cancel
                // pair, near the same menu alignment and right-click anchor.
                if(r.Top>=examine.Bounds.Top||r.Bottom>=cancel.Bounds.Top||
                    Math.Abs(r.Left-examine.Bounds.Left)>26||
                    r.Width<38||r.Width>175||
                    r.Top<Math.Min(anchor.Y-200,examine.Bounds.Top-160))continue;
                found.Add(new("Choose Option",r,VisualLabelKind.Text));
            }
        }
        var distinct=found.OrderBy(l=>l.Bounds.Top).ToArray();
        if(distinct.Length==0)return null;
        if(distinct.Any(l=>Math.Abs(l.Bounds.Top-distinct[0].Bounds.Top)>7||
            Math.Abs(l.Bounds.Left-distinct[0].Bounds.Left)>9))return null;
        return distinct[0];
    }

    internal static void SelfTest()
    {
        var size=new Size(1000,600);
        foreach(var point in new[]{new Point(10,10),new Point(780,328),
            new Point(928,550),new Point(500,300)})
        {
            var region=Region(point,size);
            if(!new Rectangle(Point.Empty,size).Contains(region)||!region.Contains(point))
                throw new Exception("Context-menu OCR crop must include verified click and stay inside game window");
        }
        if(Region(new Point(-1,20),size)!=Rectangle.Empty||
            Region(new Point(20,600),size)!=Rectangle.Empty||
            Region(new Point(20,20),new Size(300,300))!=Rectangle.Empty)
            throw new Exception("Unsupported context-menu geometry must fail closed");
    }
}
