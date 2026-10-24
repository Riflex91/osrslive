using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal static class WorldGridOcr
{
    internal static async Task<IReadOnlyList<VisualLabel>> Read(Bitmap bitmap,OcrEngine engine,IReadOnlyList<VisualLabel> labels)
    {
        // Infer the lattice from already validated ascending world columns.
        // A crop alone cannot authorize a click: every resulting column is
        // subjected to WorldGridReader's ascending-world validation again.
        var cells=WorldGridReader.Classify(labels);
        // In the native client the world digits are black on grey/gold pills.
        // Full-frame Windows OCR can yield 0 validated columns even when the
        // whole table is visible. Read isolated high-contrast digits only after
        // verifying the fixed native table geometry.
        if(cells.Count<6)return await ReadNativeTable(bitmap,engine,labels);
        var columns=new List<List<VisualLabel>>();
        foreach(var cell in cells.OrderBy(c=>c.Bounds.Left))
        {
            var column=columns.FirstOrDefault(c=>Math.Abs(c[0].Bounds.Left-cell.Bounds.Left)<8);
            if(column is null){column=new();columns.Add(column);}
            column.Add(cell);
        }
        var rowGaps=columns.SelectMany(c=>c.OrderBy(l=>l.Bounds.Top).Zip(c.OrderBy(l=>l.Bounds.Top).Skip(1),
            (a,b)=>b.Bounds.Top-a.Bounds.Top)).Where(d=>d is >=15 and <=25).Order().ToArray();
        var columnGaps=columns.Zip(columns.Skip(1),(a,b)=>b[0].Bounds.Left-a[0].Bounds.Left)
            .Where(d=>d is >=70 and <=110).Order().ToArray();
        if(rowGaps.Length<3||columnGaps.Length==0)return Array.Empty<VisualLabel>();
        int dy=rowGaps[rowGaps.Length/2],dx=columnGaps[columnGaps.Length/2];
        int top=cells.Min(c=>c.Bounds.Top),left=cells.Min(c=>c.Bounds.Left);
        var title=labels.First(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase));
        while(left-dx>title.Bounds.Left)left-=dx;
        var crops=new List<Rectangle>();
        for(int x=left;x<bitmap.Width-title.Bounds.Left;x+=dx)
        for(int y=top;y<Math.Min(bitmap.Height-40,top+25*dy);y+=dy)
        {
            var crop=new Rectangle(x-2,y-2,30,16);
            if(new Rectangle(Point.Empty,bitmap.Size).Contains(crop))crops.Add(crop);
        }
        const int tw=128,th=72,cols=7;
        using var sheet=new Bitmap(cols*tw,((crops.Count+cols-1)/cols)*th);
        if(sheet.Height>OcrEngine.MaxImageDimension)return Array.Empty<VisualLabel>();
        using(var g=Graphics.FromImage(sheet))
        {
            g.Clear(Color.Black);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for(int i=0;i<crops.Count;i++)g.DrawImage(bitmap,new Rectangle(i%cols*tw+4,i/cols*th+4,120,64),crops[i],GraphicsUnit.Pixel);
        }
        var result=await VisualStartup.RecognizeRaw(sheet,engine);
        var numbers=new Dictionary<int,List<string>>();
        foreach(var word in result.Lines.SelectMany(l=>l.Words))
        {
            if(!int.TryParse(word.Text,out int n)||n is <301 or >800)continue;
            int col=(int)(word.BoundingRect.X+word.BoundingRect.Width/2)/tw;
            int row=(int)(word.BoundingRect.Y+word.BoundingRect.Height/2)/th;
            if(col>=cols)continue;
            int i=row*cols+col;if(i>=crops.Count)continue;
            if(!numbers.TryGetValue(i,out var hits)){hits=new();numbers[i]=hits;}
            hits.Add(n.ToString());
        }
        var extra=numbers.Where(n=>n.Value.Count==1).Select(n=>new VisualLabel(n.Value[0],crops[n.Key],VisualLabelKind.Number));
        // The focused pass is best-effort. Earlier versions discarded ALL
        // coarse numbers even when this pass read only a few digits, making a
        // previously verified grid disappear and disabling page navigation.
        return MergeValidated(cells,extra.ToArray(),labels);
    }

    // Calibrated to the official 1000x600 OSRS world-selection view
    // (seven columns and 25 rows). These coordinates never justify an input
    // by themselves: the visual table guard and ascending OCR columns must
    // both succeed. No world number is inferred from an index/position.
    internal static readonly int[] NativeColumns={176,269,362,455,548,641,734};
    internal static Rectangle NativeNumberRegion(int column,int row)
    {
        if(column is <0 or >=7||row is <0 or >=25)
            throw new ArgumentOutOfRangeException();
        int y=34+(int)Math.Round(row*18.25,MidpointRounding.AwayFromZero);
        return new Rectangle(NativeColumns[column]+4,y+3,26,12);
    }

    internal static bool HasNativeTable(Bitmap bitmap,IReadOnlyList<VisualLabel> labels)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize||
            labels.Count(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase)&&
                l.Bounds.Top<42&&l.Bounds.Left is >=70 and <=300)!=1)
            return false;
        // Reject a plain black/synthetic screen or a shifted/rescaled layout.
        // Each world-number pill must contain both black glyph pixels and a
        // light background. Sample multiple rows and columns.
        int validColumns=0;
        for(int c=0;c<7;c++)
        {
            int validRows=0;
            for(int row=0;row<25;row++)
            {
                var box=NativeNumberRegion(c,row);
                int black=0,light=0;
                for(int y=box.Top;y<box.Bottom;y+=2)
                for(int x=box.Left;x<box.Right;x+=2)
                {
                    var color=bitmap.GetPixel(x,y);
                    if(color.R<90&&color.G<90&&color.B<90)black++;
                    if(color.R>105&&color.G>105&&color.B>90)light++;
                }
                if(black>=8&&light>=7)validRows++;
            }
            if(validRows>=11)validColumns++;
        }
        return validColumns>=5;
    }

    internal static async Task<IReadOnlyList<VisualLabel>> ReadNativeTable(
        Bitmap bitmap,OcrEngine engine,IReadOnlyList<VisualLabel> labels)
    {
        if(!HasNativeTable(bitmap,labels))return Array.Empty<VisualLabel>();
        const int tw=144,th=64,columns=7,rows=25;
        using var sheet=new Bitmap(columns*tw,rows*th);
        using(var canvas=Graphics.FromImage(sheet))
        {
            canvas.Clear(Color.White);
            canvas.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            canvas.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for(int row=0;row<rows;row++)
            for(int col=0;col<columns;col++)
            {
                var source=NativeNumberRegion(col,row);
                using var isolated=new Bitmap(source.Width,source.Height);
                // Preserve only dark raster-font strokes. World flags, star
                // icons and gold/silver pill backgrounds cannot leak into OCR.
                for(int y=0;y<source.Height;y++)
                for(int x=0;x<source.Width;x++)
                {
                    var pixel=bitmap.GetPixel(source.Left+x,source.Top+y);
                    isolated.SetPixel(x,y,pixel.R<90&&pixel.G<90&&pixel.B<90?
                        Color.Black:Color.White);
                }
                canvas.DrawImage(isolated,new Rectangle(col*tw+12,row*th+5,112,52));
            }
        }
        var result=await VisualStartup.RecognizeRaw(sheet,engine);
        var hits=new Dictionary<(int,int),List<int>>();
        foreach(var word in result.Lines.SelectMany(l=>l.Words))
        {
            if(word.Text.Length!=3||!int.TryParse(word.Text,out int n)||n is <301 or >800)
                continue;
            int col=(int)(word.BoundingRect.X+word.BoundingRect.Width/2)/tw;
            int row=(int)(word.BoundingRect.Y+word.BoundingRect.Height/2)/th;
            if(col is <0 or >=7||row is <0 or >=25)continue;
            if(!hits.TryGetValue((col,row),out var values))
            {values=new();hits[(col,row)]=values;}
            values.Add(n);
        }
        var numbers=hits.Where(h=>h.Value.Distinct().Count()==1)
            .Select(h=>new VisualLabel(h.Value[0].ToString(),
                NativeNumberRegion(h.Key.Item1,h.Key.Item2),VisualLabelKind.Number)).ToArray();
        var verified=WorldGridReader.Classify(labels.Concat(numbers).ToArray());
        // A lone "399" is NOT enough. Require a validated ascending column,
        // the same guard used by ordinary world-grid recognition.
        return verified.Count>=6?verified:Array.Empty<VisualLabel>();
    }

    internal static IReadOnlyList<VisualLabel> MergeValidated(
        IReadOnlyList<VisualLabel> coarse,IReadOnlyList<VisualLabel> focused,
        IReadOnlyList<VisualLabel> sourceLabels)
    {
        if(coarse.Count==0||focused.Count==0)return coarse;
        // Ignore duplicate/conflicting readings at an already validated cell.
        // Extra cells are only admitted after revalidation of the entire
        // ascending column; no isolated three-digit number authorizes a click.
        var missing=focused.Where(candidate=>!coarse.Any(existing=>
            Math.Abs(existing.Bounds.Left-candidate.Bounds.Left)<=12 &&
            Math.Abs(existing.Bounds.Top-candidate.Bounds.Top)<=12)).ToArray();
        if(missing.Length==0)return coarse;
        var candidates=coarse.Select(c=>c with{Kind=VisualLabelKind.Number})
            .Concat(missing).ToArray();
        var title=sourceLabels.Where(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(title.Length!=1)return coarse;
        var verified=WorldGridReader.Classify(title.Concat(candidates).ToArray());
        return verified.Count>coarse.Count?verified:coarse;
    }
}
