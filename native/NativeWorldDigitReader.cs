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
        Console.WriteLine("NATIVE WORLD DIGIT PASS: glyph masks, 19px row pitch, 24 visible rows, fail-closed matches");
    }
}
