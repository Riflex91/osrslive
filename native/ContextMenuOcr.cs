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

    // A compact, cursor-anchored second view isolates menus that flip up and
    // left at the bottom/right inventory edge. It never changes click targets.
    internal static Rectangle TightRegion(Point anchor,Size size,Rectangle? observedCancel=null)
    {
        const int width=280,height=270;
        if(size.Width<width||size.Height<height||anchor.X<0||anchor.Y<0||
            anchor.X>=size.Width||anchor.Y>=size.Height)return Rectangle.Empty;
        int y=Math.Clamp(anchor.Y-210,0,size.Height-height);
        // The first broad OCR pass sometimes sees Cancel at y=584 for a
        // bottom-row menu while this default crop ends at y=568. A smaller
        // crop must contain the ACTUAL observed footer before it can recover
        // Examine or the pixel-verified header. Never infer a Cancel position.
        if(observedCancel is Rectangle cancel&&!cancel.IsEmpty&&
            new Rectangle(Point.Empty,size).Contains(cancel)&&
            Math.Abs(cancel.Left-anchor.X)<=130&&
            cancel.Top>=anchor.Y-200&&cancel.Top<=anchor.Y+200)
            y=Math.Clamp(Math.Max(y,cancel.Bottom+8-height),0,size.Height-height);
        return new Rectangle(Math.Clamp(anchor.X-185,0,size.Width-width),
            y,width,height);
    }

    // The last inventory row can put both Examine and Cancel next to the
    // 600px bottom edge. The broader 400x430 crop has too much inventory
    // clutter, while the existing tight crop clips text on its right side.
    // Position a final narrow OCR strip using an ACTUALLY OCR-read Cancel.
    internal static Rectangle FooterRegion(Point anchor,Size size,Rectangle cancel)
    {
        const int width=320,height=160;
        if(size.Width<width||size.Height<height||anchor.X<0||anchor.Y<0||
            anchor.X>=size.Width||anchor.Y>=size.Height||cancel.IsEmpty||
            !new Rectangle(Point.Empty,size).Contains(cancel)||
            Math.Abs(cancel.Left-anchor.X)>130||
            cancel.Top<anchor.Y-200||cancel.Top>anchor.Y+200)
            return Rectangle.Empty;
        return new Rectangle(
            Math.Clamp(Math.Min(anchor.X-135,cancel.Left-28),0,size.Width-width),
            Math.Clamp(cancel.Top-135,0,size.Height-height),width,height);
    }

    // Exposed for the Windows OCR fixture: this is only raw visual evidence,
    // never an executable menu until Verify independently validates it.
    internal static Task<List<VisualLabel>> ReadFooterRows(
        Bitmap image,OcrEngine engine,Point anchor,Rectangle cancel,bool contrast)
    {
        var region=FooterRegion(anchor,image.Size,cancel);
        if(image.Size!=DockedWindowLayout.GameSize||region.IsEmpty)
            return Task.FromResult(new List<VisualLabel>());
        return ReadRegion(image,engine,region,5,contrast,nearest:true);
    }

    // A right-click in the final inventory column can appear without a
    // broad OCR Cancel hit even when native pixels changed. This bounded
    // observer is tied to that right-edge click only. It cannot authorize
    // an action without independently read header, Examine and Cancel.
    internal static Rectangle CornerRegion(Point anchor,Size size)
    {
        const int width=320,height=180;
        if(size!=DockedWindowLayout.GameSize||
            anchor.X<880||anchor.X>=size.Width||
            anchor.Y<470||anchor.Y>=size.Height)return Rectangle.Empty;
        return new Rectangle(size.Width-width,size.Height-height,width,height);
    }

    internal static Task<List<VisualLabel>> ReadCornerRows(
        Bitmap image,OcrEngine engine,Point anchor,bool contrast)
    {
        var region=CornerRegion(anchor,image.Size);
        if(region.IsEmpty)return Task.FromResult(new List<VisualLabel>());
        return ReadRegion(image,engine,region,4,contrast,nearest:true);
    }

    // A rightmost-column menu can open either below the cursor or flip
    // above it. The legacy corner crop starts at y=420 and may cut off the
    // heading of an upward-flipped menu at row 4 (anchor y~472).
    // Read bounded independent views of BOTH placements on the same frame.
    internal static Rectangle RightEdgeRegion(Point anchor,Size size,bool above)
    {
        const int width=320,height=190;
        if(size!=DockedWindowLayout.GameSize||anchor.X<880||anchor.X>=size.Width||
            anchor.Y<460||anchor.Y>560)return Rectangle.Empty;
        int top=above?anchor.Y-height+14:anchor.Y-38;
        return new Rectangle(size.Width-width,Math.Clamp(top,0,size.Height-height),width,height);
    }

    internal static Task<List<VisualLabel>> ReadRightEdgeRows(
        Bitmap image,OcrEngine engine,Point anchor,bool above,bool contrast)
    {
        var region=RightEdgeRegion(anchor,image.Size,above);
        return region.IsEmpty?Task.FromResult(new List<VisualLabel>()):
            ReadRegion(image,engine,region,5,contrast,nearest:true);
    }

    internal static async Task<IReadOnlyList<VisualLabel>> Read(
        Bitmap image,OcrEngine engine,Point anchor)
    {
        var region=Region(anchor,image.Size);
        if(region.IsEmpty||image.Size!=DockedWindowLayout.GameSize)
            return Array.Empty<VisualLabel>();
        var diagnosticCandidates=new List<VisualLabel>();
        // Broad OCR remains the primary read; each pass must independently
        // verify its own labels before anything becomes an actionable menu.
        for(int pass=0;pass<2;pass++)
        {
            var labels=await ReadRegion(image,engine,region,3,
                contrast:pass==0,nearest:pass==0);
            diagnosticCandidates.AddRange(labels.Select(l=>
                l with{Kind=VisualLabelKind.ContextMenuCandidate}));
            if(await Verify(image,engine,anchor,labels) is { } menu)return menu;
        }
        // The 400x430 view can blend the flipped menu text with inventory and
        // chat labels. Only a real Cancel OCR hit authorizes bounded tighter
        // observation of the SAME screenshot. No new mouse input is sent.
        if(diagnosticCandidates.Any(l=>l.Text.Trim().Equals("Cancel",
            StringComparison.OrdinalIgnoreCase)))
        {
            var observedCancel=diagnosticCandidates.Where(l=>
                l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase))
                .OrderBy(l=>Math.Abs(l.Bounds.Left-anchor.X)+
                    Math.Abs(l.Bounds.Top-anchor.Y))
                .Select(l=>(Rectangle?)l.Bounds).FirstOrDefault();
            var tight=TightRegion(anchor,image.Size,observedCancel);
            if(!tight.IsEmpty)
                for(int pass=0;pass<2;pass++)
                {
                    var labels=await ReadRegion(image,engine,tight,4,
                        contrast:pass==0,nearest:true);
                    diagnosticCandidates.AddRange(labels.Select(l=>
                        l with{Kind=VisualLabelKind.ContextMenuCandidate}));
                    if(await Verify(image,engine,anchor,labels,requireExamine:true)
                        is { } menu)return menu;
                }
            if(observedCancel is Rectangle cancel&&
                !FooterRegion(anchor,image.Size,cancel).IsEmpty)
                for(int pass=0;pass<2;pass++)
                {
                    // The final pass isolates the real footer and complete
                    // right-hand menu text; no action is inferred from Cancel.
                    var labels=await ReadFooterRows(image,engine,anchor,cancel,
                        contrast:pass==0);
                    diagnosticCandidates.AddRange(labels.Select(l=>
                        l with{Kind=VisualLabelKind.ContextMenuCandidate}));
                    if(await Verify(image,engine,anchor,labels,requireExamine:true)
                        is { } menu)return menu;
                }
        }
        else if(!CornerRegion(anchor,image.Size).IsEmpty)
        {
            // No Cancel survived broad OCR at the bottom-right click.
            // Re-read only original screenshot pixels in the final-column
            // menu area; never send a second click or infer any menu entry.
            for(int pass=0;pass<2;pass++)
            {
                var labels=await ReadCornerRows(image,engine,anchor,
                    contrast:pass==0);
                diagnosticCandidates.AddRange(labels.Select(l=>
                    l with{Kind=VisualLabelKind.ContextMenuCandidate}));
                if(await Verify(image,engine,anchor,labels,requireExamine:true)
                    is { } menu)return menu;
            }
        }
        // If the corner view cannot prove the menu, isolate the two actual
        // on-screen placements at higher resolution. No click is retried.
        if(!RightEdgeRegion(anchor,image.Size,above:false).IsEmpty)
            foreach(bool above in new[]{false,true})
                for(int pass=0;pass<2;pass++)
                {
                    var labels=await ReadRightEdgeRows(image,engine,anchor,above,
                        contrast:pass==0);
                    diagnosticCandidates.AddRange(labels.Select(l=>
                        l with{Kind=VisualLabelKind.ContextMenuCandidate}));
                    if(await Verify(image,engine,anchor,labels,requireExamine:true)
                        is { } menu)return menu;
                }
        // Partial OCR remains diagnostic only: never invent an Examine item,
        // a header, or a successful right-click from Cancel alone.
        return diagnosticCandidates;
    }

    private static async Task<List<VisualLabel>> ReadRegion(Bitmap image,OcrEngine engine,
        Rectangle region,int scale,bool contrast,bool nearest)
    {
        const int padding=12;
        using var crop=contrast?GameplayHudOcr.Contrast(image,region):
            image.Clone(region,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var sheet=new Bitmap(region.Width*scale+padding*2,
            region.Height*scale+padding*2);
        using(var graphics=Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode=nearest?
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
            string value=line.Text.Trim();
            // Windows OCR occasionally merges words in line.Text even when
            // its individual word boxes are separate. Join only words read
            // within this one line and only for exact known menu prefixes.
            string words=string.Join(" ",line.Words.Select(w=>w.Text.Trim())
                .Where(w=>w.Length>0));
            if(words.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase)||
                words.Equals("Choose Option",StringComparison.OrdinalIgnoreCase)||
                words.Equals("Cancel",StringComparison.OrdinalIgnoreCase))
                value=words;
            if(region.Contains(bounds)&&value.Length is >0 and <=90)
                labels.Add(new(value,bounds));
        }
        return JoinExamineFragments(labels,region);
    }

    // Windows OCR may create two OcrLines for differently coloured text on
    // ONE native menu row ("Examine" and the item name). Recombine only
    // independently recognized, uniquely aligned pixel text; never fill a
    // name from slot positions, another menu row or an item catalogue.
    internal static List<VisualLabel> JoinExamineFragments(
        IReadOnlyList<VisualLabel> source,Rectangle region)
    {
        var result=source.ToList();
        static bool ItemPart(string value)=>
            value.Length is >0 and <=50&&
            value.Any(char.IsLetterOrDigit)&&
            value.All(c=>char.IsLetterOrDigit(c)||
                c is ' ' or '-' or '\'' or '(' or ')' or '/');
        foreach(var label in source)
        {
            string value=label.Text.Trim();
            // A colour boundary may also remove the separating space within
            // ONE OCR line. Only an exact action prefix and a visibly decoded,
            // capitalized/digit-starting suffix support a correction.
            if(value.StartsWith("Examine",StringComparison.OrdinalIgnoreCase)&&
                value.Length>7&&(char.IsUpper(value[7])||char.IsDigit(value[7]))&&
                ItemPart(value[7..]))
            {
                int idx=result.IndexOf(label);
                if(idx>=0)result[idx]=label with{Text="Examine "+value[7..]};
                continue;
            }
            if(!value.Equals("Examine",StringComparison.OrdinalIgnoreCase))continue;
            var suffixes=source.Where(other=>!ReferenceEquals(other,label)&&
                region.Contains(other.Bounds)&&
                other.Bounds.Left>=label.Bounds.Right&&
                other.Bounds.Left-label.Bounds.Right<=28&&
                Math.Abs(other.Bounds.Top-label.Bounds.Top)<=5&&
                Math.Abs(other.Bounds.Bottom-label.Bounds.Bottom)<=6&&
                ItemPart(other.Text.Trim())&&
                !new[]{"Cancel","Examine","Choose Option"}.Contains(
                    other.Text.Trim(),StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if(suffixes.Length!=1)continue;
            var suffix=suffixes[0];
            if(!result.Contains(label)||!result.Contains(suffix))continue;
            var joint=Rectangle.Union(label.Bounds,suffix.Bounds);
            if(!region.Contains(joint))continue;
            result.Remove(label);
            result.Remove(suffix);
            result.Add(new("Examine "+suffix.Text.Trim(),joint));
        }
        return result;
    }

    private static async Task<IReadOnlyList<VisualLabel>?> Verify(
        Bitmap image,OcrEngine engine,Point anchor,IReadOnlyList<VisualLabel> source,
        bool requireExamine=false)
    {
        var labels=source.ToList();
        var examines=labels.Where(l=>l.Text.Trim().StartsWith("Examine ",
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if(requireExamine&&examines.Length!=1)return null;
        if(!labels.Any(l=>l.Text.Trim().Equals("Choose Option",
            StringComparison.OrdinalIgnoreCase))&&examines.Length==1)
        {
            var recovered=await RecoverHeader(image,engine,anchor,labels);
            if(recovered is null)
            {
                var cancels=labels.Where(l=>l.Text.Trim().Equals("Cancel",
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if(cancels.Length==1)
                    recovered=NativeMenuHeaderReader.ReadNearCancel(image,anchor,cancels[0]);
            }
            if(recovered is not null)labels.Add(recovered);
        }
        if(ExecutionUi.Menu(labels) is not ContextMenu parsed||parsed.Entries.Count==0)
            return null;
        var header=labels.Single(l=>l.Text.Trim().Equals("Choose Option",
            StringComparison.OrdinalIgnoreCase));
        var cancel=labels.Where(l=>l.Text.Trim().Equals("Cancel",
            StringComparison.OrdinalIgnoreCase)&&
            l.Bounds.Top>header.Bounds.Bottom&&Math.Abs(l.Bounds.Left-header.Bounds.Left)<30)
            .OrderBy(l=>l.Bounds.Top).First();
        var menuBounds=Rectangle.FromLTRB(
            Math.Max(0,header.Bounds.Left-18),Math.Max(0,header.Bounds.Top-8),
            Math.Min(image.Width,header.Bounds.Left+290),
            Math.Min(image.Height,cancel.Bounds.Bottom+8));
        menuBounds.Inflate(24,24);
        menuBounds.Intersect(new Rectangle(Point.Empty,image.Size));
        if(!menuBounds.Contains(anchor))return null;
        return labels.Where(l=>l.Bounds.Top>=header.Bounds.Top&&
            l.Bounds.Bottom<=cancel.Bounds.Bottom+2&&
            Math.Abs(l.Bounds.Left-header.Bounds.Left)<30)
            .Select(l=>l with {Kind=VisualLabelKind.ContextMenuLine}).ToArray();
    }

    // Pixel-backed header recovery for themes and ordinary menu placements.
    // The independent OCR must actually
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
        // The heading is drawn in muted brown-grey (#5d5447) on black.
        // The generic contrast transform maps BOTH nearly to white; OCR
        // therefore detects the item actions but routinely loses the title.
        // Require an independently matched, authentic heading raster.
        if(NativeMenuHeaderReader.Read(image,anchor,examine,cancel) is VisualLabel native)
            return native;
        // Other native themes may still be recognized by Windows OCR.
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
        foreach(var point in new[]{new Point(860,508),new Point(902,508),
            new Point(776,328)})
        {
            var tight=TightRegion(point,size);
            if(!new Rectangle(Point.Empty,size).Contains(tight)||!tight.Contains(point)||
                tight.Width>=Region(point,size).Width)
                throw new Exception("Tight menu OCR must retain the click in a smaller viewport crop");
        }
        var corner=CornerRegion(new Point(902,508),size);
        if(corner.IsEmpty||corner.Left!=680||corner.Top!=420||
            corner.Right!=size.Width||corner.Bottom!=size.Height||
            !corner.Contains(new Point(902,508))||
            !corner.Contains(new Rectangle(850,576,75,14))||
            CornerRegion(new Point(860,508),size)!=Rectangle.Empty||
            CornerRegion(new Point(902,328),size)!=Rectangle.Empty||
            CornerRegion(new Point(1000,508),size)!=Rectangle.Empty)
            throw new Exception("Only a final-column click permits a bounded OCR observation to the right viewport edge");
        var edgeAnchor=new Point(902,472);
        var edgeUpper=RightEdgeRegion(edgeAnchor,size,above:true);
        var edgeLower=RightEdgeRegion(edgeAnchor,size,above:false);
        if(edgeUpper.IsEmpty||!edgeUpper.Contains(edgeAnchor)||
            !edgeUpper.Contains(new Rectangle(755,350,130,20))||
            edgeLower.IsEmpty||!edgeLower.Contains(edgeAnchor)||
            !edgeLower.Contains(new Rectangle(755,565,130,20))||
            RightEdgeRegion(new Point(860,472),size,above:true)!=Rectangle.Empty||
            RightEdgeRegion(new Point(902,328),size,above:false)!=Rectangle.Empty||
            RightEdgeRegion(new Point(902,472),new Size(950,600),above:true)!=Rectangle.Empty)
            throw new Exception("Rightmost-column OCR must observe both flipped placements without leaving the verified viewport");
        var textArea=new Rectangle(680,440,320,160);
        var examineWord=new VisualLabel("Examine",new Rectangle(786,568,55,13));
        var itemWord=new VisualLabel("Logs",new Rectangle(846,568,42,13));
        var split=JoinExamineFragments(new[]{examineWord,itemWord},textArea);
        if(split.Count!=1||split[0].Text!="Examine Logs"||
            split[0].Bounds!=Rectangle.Union(examineWord.Bounds,itemWord.Bounds)||
            ExecutionUi.Menu(split) is not null)
            throw new Exception("Real same-row OCR words must form a complete Examine action without guessing an item");
        if(JoinExamineFragments(new[]{examineWord},textArea)
                .Any(l=>l.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase))||
            JoinExamineFragments(new[]{examineWord,
                itemWord with {Bounds=new Rectangle(846,548,42,13)}},textArea)
                .Any(l=>l.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase))||
            JoinExamineFragments(new[]{examineWord,itemWord,
                itemWord with{Text="Coins",Bounds=new Rectangle(852,568,40,13)}},textArea)
                .Any(l=>l.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Missing, misaligned or ambiguous names must never complete Examine");
        var fused=JoinExamineFragments(new[]{
            new VisualLabel("ExamineLogs",new Rectangle(786,568,100,13))},textArea);
        if(fused.Count!=1||fused[0].Text!="Examine Logs"||
            JoinExamineFragments(new[]{
                new VisualLabel("Examine",new Rectangle(786,568,55,13))},textArea)
                .Any(l=>l.Text.Contains("Logs",StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Only visibly decoded fused name characters may be separated");
        // Live regression: right-click at (860,508), but OCR sees the
        // footer at y=584. The original 270px crop ended at y=568 and
        // necessarily discarded the footer and adjacent Examine text.
        var footer=new Rectangle(785,584,58,12);
        var nearBottom=TightRegion(new Point(860,508),size,footer);
        if(TightRegion(new Point(860,508),size).Contains(footer)||
            !nearBottom.Contains(footer)||
            !nearBottom.Contains(new Rectangle(785,476,95,14))||
            !nearBottom.Contains(new Point(860,508))||
            nearBottom.Bottom!=size.Height)
            throw new Exception("Bottom-row focused OCR must include a visibly observed Cancel and its menu header");
        var footerStrip=FooterRegion(new Point(860,508),size,footer);
        if(footerStrip.IsEmpty||footerStrip.Right!=size.Width||
            footerStrip.Bottom!=size.Height||
            !footerStrip.Contains(footer)||
            !footerStrip.Contains(new Rectangle(785,555,120,15))||
            !footerStrip.Contains(new Rectangle(785,476,95,14))||
            !footerStrip.Contains(new Point(860,508)))
            throw new Exception("Bottom-edge menu footer OCR must include complete right-side text and clicked anchor");
        if(FooterRegion(new Point(860,508),size,new Rectangle(0,584,58,12))!=Rectangle.Empty||
            FooterRegion(new Point(860,508),size,new Rectangle(785,599,58,12))!=Rectangle.Empty||
            FooterRegion(new Point(-1,508),size,footer)!=Rectangle.Empty)
            throw new Exception("Unrelated, out-of-frame or invalid Cancel cannot authorize a footer OCR pass");
        if(TightRegion(new Point(860,508),size,new Rectangle(0,584,58,12))!=
            TightRegion(new Point(860,508),size)||
            TightRegion(new Point(860,508),size,new Rectangle(785,599,58,12))!=
            TightRegion(new Point(860,508),size))
            throw new Exception("Unrelated or out-of-bounds Cancel text cannot reposition a menu crop");
        if(Region(new Point(-1,20),size)!=Rectangle.Empty||
            Region(new Point(20,600),size)!=Rectangle.Empty||
            Region(new Point(20,20),new Size(300,300))!=Rectangle.Empty||
            TightRegion(new Point(-1,508),size)!=Rectangle.Empty)
            throw new Exception("Unsupported context-menu geometry must fail closed");
    }
}
