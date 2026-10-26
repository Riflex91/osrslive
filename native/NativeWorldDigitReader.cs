using System.Drawing;

namespace OSRSLiveDiagnostics;

// Small, deterministic pixel-font reader for the official 1000x600 world list.
// It is only invoked after WorldGridOcr.HasNativeTable verifies the real table.
// Glyph templates are seven-column binary masks from the visible native font,
// not a mapping from a cell's position to an assumed world number.
internal static class NativeWorldDigitReader
{
    private const int Rows=24;
    private const int Pitch=19;
    private static readonly int[] DigitOffsets={3,11,20};
    // 7 x 11 dark-pixel templates. The background color of a world row (grey,
    // gold, red) must not be used to infer the text or target world.
    private static readonly string[][] Glyphs=(
        new[]{
            "......./.###.../##.##../#..###./#..###./#.#.##./#.#.##./##..##./##..##./##.##../.###...",
            "......./..##.../.###.../####.../..##.../..##.../..##.../..##.../..##.../..##.../#######",
            "......./####.../#..##../....##./....##./...##../..##.../.##..../##...../#....../######.",
            "......./.####../##..##./....##./....##./.####../....##./....##./....##./##..##./.####..",
            "......./##...../##...../##...../##...../##.##../##.##../##.##../#######/...##../...##..",
            "......./######./##...../##...../####.../...##../....##./....##./....##./##..##./.####..",
            "......./.###.../##.##../#....../#....../####.../##.##../#...##./#...##./##.##../.###...",
            "......./######./....##./...##../...##../..##.../..##.../.##..../.##..../##...../##.....",
            "......./.###.../##.##../#...##./##.##../.###.../##.##../#...##./#...##./##.##../#######",
            "......./#####../#...##./#...##./#...##./##..##./.#####./....##./....##./....##./....##."
        }).Select(p=>p.Split('/')).ToArray();

    internal static IReadOnlyList<VisualLabel> Read(Bitmap bitmap)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize)return Array.Empty<VisualLabel>();
        var found=new List<VisualLabel>();
        for(int column=0;column<WorldGridOcr.NativeColumns.Length;column++)
        for(int row=0;row<Rows;row++)
        {
            int x0=WorldGridOcr.NativeColumns[column],y0=34+row*Pitch;
            // Read the bitmap ONCE. Sliding the glyph pattern itself is cheap;
            // repeat GetPixel for thousands of template matches would be slow.
            var dark=new bool[33,20];
            for(int x=0;x<33;x++)
            for(int y=0;y<20;y++)
            {
                Color p=bitmap.GetPixel(x0+x,y0+y);
                dark[x,y]=p.R<90&&p.G<90&&p.B<90;
            }
            int number=0;
            bool valid=true;
            for(int position=0;position<3;position++)
            {
                int best=int.MaxValue,second=int.MaxValue,bestDigit=-1;
                for(int digit=0;digit<10;digit++)
                {
                    int minDistance=int.MaxValue;
                    foreach(int dx in new[]{-1,0,1})
                    foreach(int dy in new[]{-2,-1,0,1,2})
                    {
                        int errors=0;
                        int ox=DigitOffsets[position]+dx,oy=4+dy;
                        var template=Glyphs[digit];
                        for(int ty=0;ty<11;ty++)
                        for(int tx=0;tx<7;tx++)
                            if(dark[ox+tx,oy+ty]!=(template[ty][tx]=='#'))errors++;
                        minDistance=Math.Min(minDistance,errors);
                    }
                    if(minDistance<best)
                    {
                        second=best;
                        best=minDistance;
                        bestDigit=digit;
                    }
                    else if(minDistance<second)second=minDistance;
                }
                // Fail closed on damaged/obscured digits and near ties. A
                // single plausible digit never authorizes a world click.
                if(best>8||second-best<2){valid=false;break;}
                number=number*10+bestDigit;
            }
            if(valid&&number is >=301 and <=800)
                found.Add(new VisualLabel(number.ToString(),
                    WorldGridOcr.NativeNumberRegion(column,row),VisualLabelKind.Number));
        }
        return found;
    }

    // The login footer uses the same native bold digits in white. A fresh,
    // separately OCR-read switch caption anchors the search; the requested
    // world and any previous world selection are deliberately not inputs.
    internal static VisualLabel? ReadFooter(Bitmap bitmap,IReadOnlyList<VisualLabel> labels)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize||
            WorldFooterPolicy.SwitchMarker(labels,bitmap.Size) is not VisualLabel marker)return null;
        int centre=marker.Bounds.Left+marker.Bounds.Width/2;
        int minX=Math.Max(0,centre+2),maxX=Math.Min(bitmap.Width-32,centre+30);
        int minY=Math.Max(0,marker.Bounds.Top-27),maxY=Math.Min(bitmap.Height-11,marker.Bounds.Top-10);
        if(maxX<minX||maxY<minY)return null;
        var white=new bool[maxX-minX+32,maxY-minY+11];
        for(int y=0;y<white.GetLength(1);y++)for(int x=0;x<white.GetLength(0);x++)
        {
            var c=bitmap.GetPixel(minX+x,minY+y);
            white[x,y]=c.R>200&&c.G>200&&c.B>200;
        }
        int Digit(int x,int y)
        {
            int matched=-1;
            for(int digit=0;digit<10;digit++)
            {
                bool exact=true;
                for(int yy=0;yy<11&&exact;yy++)for(int xx=0;xx<7;xx++)
                    if(white[x+xx,y+yy]!=(Glyphs[digit][yy][xx]=='#')){exact=false;break;}
                if(exact){if(matched!=-1)return -1;matched=digit;}
            }
            return matched;
        }
        var hits=new List<(int Number,Rectangle Bounds)>();
        for(int y=0;y<=maxY-minY;y++)for(int x=0;x<=maxX-minX;x++)
        {
            int a=Digit(x,y);if(a is <3 or >8)continue;
            foreach(int first in new[]{8,9})foreach(int second in new[]{8,9})
            {
                int b=Digit(x+first,y),c=Digit(x+first+second,y);
                if(b<0||c<0)continue;
                int number=a*100+b*10+c;
                if(number is <301 or >800)continue;
                bool end=true;
                for(int yy=0;yy<11&&end;yy++)for(int xx=7;xx<11;xx++)
                    if(white[x+first+second+xx,y+yy]){end=false;break;}
                if(end)hits.Add((number,new(minX+x,minY+y,first+second+7,11)));
            }
        }
        if(hits.Select(h=>h.Number).Distinct().Count()!=1)return null;
        var hit=hits[0];
        if(hits.Any(h=>Math.Abs(h.Bounds.Left-hit.Bounds.Left)>2||Math.Abs(h.Bounds.Top-hit.Bounds.Top)>2))return null;
        return new("World "+hit.Number,hit.Bounds);
    }

    internal static void SelfTest()
    {
        if(Glyphs.Length!=10||Glyphs.Any(g=>g.Length!=11||g.Any(r=>r.Length!=7||
           r.Any(ch=>ch is not ('#' or '.')))))
            throw new Exception("Native world-digit glyph template dimensions invalid.");
        if(WorldGridOcr.NativeNumberRegion(4,1).Left!=551||
           WorldGridOcr.NativeNumberRegion(4,1).Top!=57)
            throw new Exception("World grid region does not cover the visible world 399 cell.");
        if(WorldGridOcr.NativeNumberRegion(0,23).Top!=475)
            throw new Exception("Native world grid row spacing must equal 19 pixels.");
        using var blank=new Bitmap(1000,600);
        using(var footer=new Bitmap(1000,600))
        {
            using(var graphics=Graphics.FromImage(footer))graphics.Clear(Color.FromArgb(35,45,25));
            var marker=new VisualLabel("Click to switch",new Rectangle(145,565,70,10));
            int pen=194,top=547;
            for(int position=0;position<3;position++)
            {
                var glyph=Glyphs["455"[position]-'0'];
                int offset=new[]{0,8,17}[position];
                for(int y=0;y<11;y++)for(int x=0;x<7;x++)
                    if(glyph[y][x]=='#')footer.SetPixel(pen+offset+x,top+y,Color.White);
            }
            if(ReadFooter(footer,new[]{marker})?.Text!="World 455"||ReadFooter(footer,Array.Empty<VisualLabel>()) is not null||
                ReadFooter(footer,new[]{marker,marker with{Bounds=new(280,565,70,10)}}) is not null)
                throw new Exception("Native footer requires exact visible digits and one independently recognized switch marker.");
            footer.SetPixel(pen,top+1,Color.Black);
            if(ReadFooter(footer,new[]{marker}) is not null)
                throw new Exception("Damaged native footer digits must not be corrected from a requested world.");
        }
        var header=new[]{new VisualLabel("Select a world",new Rectangle(134,0,119,16))};
        if(WorldGridOcr.HasNativeTable(blank,header)||Read(blank).Count!=0)
            throw new Exception("A blank window must never identify a world table.");
        using var fixture=TableFixture(new[]{301,326,350,374,398,424,448});
        if(!WorldGridOcr.HasNativeTable(fixture,header))
            throw new Exception("Native row/pill guard rejected aligned synthetic world table.");
        var decoded=Read(fixture);
        if(!decoded.Any(c=>c.Text=="399"&&c.Bounds==WorldGridOcr.NativeNumberRegion(4,1)))
            throw new Exception("Independent digit decoder did not read synthetic fifth-column world 399.");
        if(!WorldGridReader.Classify(header.Concat(decoded).ToArray()).Any(c=>c.Text=="399"))
            throw new Exception("Ascending world column validation rejected verified world 399.");
        Console.WriteLine("NATIVE WORLD DIGIT PASS: glyph masks, 19px row pitch, 24 visible rows, verified 399, fail-closed matches");
    }
    internal static Bitmap TableFixture(int[] firstWorld)
    {
        var fixture=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(fixture))g.Clear(Color.Black);
        for(int column=0;column<7;column++)
        for(int row=0;row<24;row++)
        {
            int value=firstWorld[column]+row;
            var box=WorldGridOcr.NativeNumberRegion(column,row);
            using(var g=Graphics.FromImage(fixture))g.FillRectangle(Brushes.LightGray,box);
            string digits=value.ToString();
            for(int position=0;position<3;position++)
            {
                var glyph=Glyphs[digits[position]-'0'];
                for(int y=0;y<11;y++)
                for(int x=0;x<7;x++)
                    if(glyph[y][x]=='#')fixture.SetPixel(
                        WorldGridOcr.NativeColumns[column]+DigitOffsets[position]+x,
                        34+row*Pitch+4+y,Color.Black);
            }
        }
        return fixture;
    }
}
