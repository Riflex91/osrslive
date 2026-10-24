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
        if(cells.Count<6)return Array.Empty<VisualLabel>();
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
        // Replace coarse numeric readings in cells where the focused pass read
        // a value, to avoid duplicate top coordinates in a column.
        return WorldGridReader.Classify(labels.Where(l=>l.Kind!=VisualLabelKind.Number).Concat(extra).ToArray());
    }
}
