using System.Numerics;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

// Independent per-frame pixel verification for the narrowly configured World
// Location / Consolas-12 overlay. The reference digits come from the SAME
// captured bitmap's Chunk/Region fields, not from a stored screenshot, a
// learned player position, or OCR guesses about commas and slashed zeroes.
// Any font, layout, or glyph ambiguity falls back to the normal fail-closed OCR.
internal static class RuneLitePixelGridReader
{
    private const int Pitch=7, Height=11, RowPitch=15, WhiteThreshold=195;
    private static readonly int[] TileDigits={4,5,6,7,10,11,12,13,16};

    private readonly record struct Glyph(ulong Low,ulong High)
    {
        internal int Ink=>BitOperations.PopCount(Low)+BitOperations.PopCount(High);
        internal int Distance(Glyph other)=>BitOperations.PopCount(Low^other.Low)+
            BitOperations.PopCount(High^other.High);
    }

    internal readonly record struct Observation(string Chunk,string Region,int X,int Y);

    internal static Observation? ReadObservation(IEnumerable<string> lines)
    {
        var rows=lines.Select(s=>s.Trim()).ToArray();
        var chunks=rows.Select(s=>Regex.Match(s,@"^Chunk\s+ID\s*:?\s*(\d{6})$",RegexOptions.IgnoreCase))
            .Where(m=>m.Success).ToArray();
        var regions=rows.Select(s=>Regex.Match(s,@"^Region\s+ID\s*:?\s*(\d{5})$",RegexOptions.IgnoreCase))
            .Where(m=>m.Success).ToArray();
        // OCR may misrecognize the two commas or the zero on the Tile line.
        // Its numeric X/Y tokens are only corroborating evidence: NO character
        // from this malformed line is rewritten or accepted as a plane/comma.
        var tiles=rows.Select(s=>Regex.Match(s,
            @"^Tile\s*:?\s*(\d{4})\D{1,4}(\d{4})\D{1,4}\S{1,2}$",RegexOptions.IgnoreCase))
            .Where(m=>m.Success).ToArray();
        if(chunks.Length!=1||regions.Length!=1||tiles.Length!=1)return null;
        return new Observation(chunks[0].Groups[1].Value,regions[0].Groups[1].Value,
            int.Parse(tiles[0].Groups[1].Value),int.Parse(tiles[0].Groups[2].Value));
    }

    internal static RuneLiteGridEvidence? Read(Bitmap bitmap,IReadOnlyList<Observation> observations,
        out string diagnostic)
    {
        var elapsed=Stopwatch.StartNew();
        var agreed=observations.GroupBy(o=>o).Where(g=>g.Count()>=2).Select(g=>g.Key).ToArray();
        if(agreed.Length!=1||observations.Any(o=>agreed.Length==1&&o!=agreed[0]))
        {
            diagnostic=$"attempted=false mode=MONO7X11 observations={observations.Count} agreeing_groups={agreed.Length} reason=INSUFFICIENT_INDEPENDENT_OCR_ID_AND_XY_AGREEMENT";
            return null;
        }
        var expected=agreed[0];
        // The user-supplied panel image was tightly cropped. In the actual
        // RuneLite framebuffer the overlay can be offset substantially from
        // that crop's origin. Search the SAME 350x220 top-left area already
        // used by Windows OCR, never an unrelated part of the game screen.
        int width=Math.Min(bitmap.Width,350),height=Math.Min(bitmap.Height,220);
        if(width<119||height<41)
        {diagnostic="attempted=false mode=MONO7X11 reason=IMAGE_TOO_SMALL";return null;}

        // Build a binary bitmap once. Never retain or export captured pixels.
        var white=new bool[width*height];
        int brightPixels=0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var p=bitmap.GetPixel(x,y);
            white[y*width+x]=p.R>=WhiteThreshold&&p.G>=WhiteThreshold&&p.B>=WhiteThreshold;
            if(white[y*width+x])brightPixels++;
        }
        Glyph Cell(int originX,int originY,int row,int cell)
        {
            ulong low=0,high=0;
            int x0=originX+cell*Pitch,y0=originY+row*RowPitch;
            for(int dy=0;dy<Height;dy++)for(int dx=0;dx<Pitch;dx++)
            {
                if(!white[(y0+dy)*width+x0+dx])continue;
                int bit=dy*Pitch+dx;
                if(bit<64)low|=1UL<<bit;else high|=1UL<<(bit-64);
            }
            return new(low,high);
        }

        int visited=0,firstCommaShapes=0,matchingCommas=0,emptySeparators=0;
        int commaCandidates=0,idCandidates=0,matched=0;
        int unknownTileDigits=0,uniqueRepeatedDigitLayouts=0,unknownSingletonRejects=0;
        var accepted=new HashSet<RuneLiteGridEvidence>();
        for(int oy=0;oy<=height-(2*RowPitch+Height);oy++)
        for(int ox=0;ox<=width-(16*Pitch+Pitch);ox++)
        {
            visited++;
            var firstComma=Cell(ox,oy,0,8);var secondComma=Cell(ox,oy,0,14);
            // Two actual matching comma rasters and two visibly empty space
            // cells are required; no OCR punctuation substitutions occur.
            if(firstComma.Ink is <3 or >9)continue;
            firstCommaShapes++;
            if(firstComma.Distance(secondComma)>2)continue;
            matchingCommas++;
            if(Cell(ox,oy,0,9).Ink>1||Cell(ox,oy,0,15).Ink>1)continue;
            emptySeparators++;
            // The comma has a low descender, not an upper-row digit/period.
            if((firstComma.Low&((1UL<<56)-1))!=0||firstComma.High==0)continue;
            if(Enumerable.Range(0,4).Any(i=>Cell(ox,oy,0,i).Ink<5))continue;
            commaCandidates++;

            var reference=new Dictionary<char,Glyph>();
            bool idsValid=true;
            foreach(var (digits,start,row) in new[]{(expected.Chunk,11,1),(expected.Region,12,2)})
            for(int i=0;i<digits.Length;i++)
            {
                var sample=Cell(ox,oy,row,start+i);
                if(sample.Ink is <5 or >44){idsValid=false;break;}
                if(reference.TryGetValue(digits[i],out var previous))
                {if(previous.Distance(sample)>2)idsValid=false;}
                else reference[digits[i]]=sample;
            }
            if(!idsValid||reference.Count<5)continue;
            var templates=reference.ToArray();
            for(int i=0;i<templates.Length&&idsValid;i++)
            for(int j=i+1;j<templates.Length;j++)
                if(templates[i].Value.Distance(templates[j].Value)<5)idsValid=false;
            if(!idsValid)continue;
            idCandidates++;

            string expectedTile=expected.X.ToString("D4")+expected.Y.ToString("D4");
            // IDs do not always contain every tile digit. Permit an otherwise
            // unseen X/Y glyph ONLY when it appears twice on the same tile row
            // with matching independent pixel shapes and is visibly distinct
            // from all ID-trained glyphs. A singleton cannot be identified
            // from two OCR guesses alone and must remain blocked.
            var unknown=new Dictionary<char,(Glyph Raster,int Repetitions)>();
            bool digitsValid=true;
            for(int i=0;i<8;i++)
            {
                var sample=Cell(ox,oy,0,TileDigits[i]);
                char expectedDigit=expectedTile[i];
                if(!reference.ContainsKey(expectedDigit))
                {
                    if(sample.Ink is <5 or >44){digitsValid=false;break;}
                    if(unknown.TryGetValue(expectedDigit,out var previous))
                    {
                        if(previous.Raster.Distance(sample)>2){digitsValid=false;break;}
                        unknown[expectedDigit]=(previous.Raster,previous.Repetitions+1);
                    }
                    else unknown[expectedDigit]=(sample,1);
                    continue;
                }
                int best=int.MaxValue,second=int.MaxValue;char digit='?';
                foreach(var template in templates)
                {
                    int difference=sample.Distance(template.Value);
                    if(difference<best){second=best;best=difference;digit=template.Key;}
                    else if(difference<second)second=difference;
                }
                if(best>2||second-best<5||digit!=expectedDigit){digitsValid=false;break;}
            }
            if(!digitsValid)continue;
            if(unknown.Count>0)
            {
                unknownTileDigits+=unknown.Count;
                if(unknown.Values.Any(v=>v.Repetitions<2))
                {unknownSingletonRejects++;continue;}
                var unique=unknown.ToArray();
                for(int i=0;i<unique.Length&&digitsValid;i++)
                {
                    foreach(var known in templates)
                        if(unique[i].Value.Raster.Distance(known.Value)<5)digitsValid=false;
                    for(int j=i+1;j<unique.Length;j++)
                        if(unique[i].Value.Raster.Distance(unique[j].Value.Raster)<5)digitsValid=false;
                }
                if(!digitsValid)continue;
                uniqueRepeatedDigitLayouts++;
            }
            // The floor is never inferred from the OCR's 'o'/'Ø'. It still
            // requires an actual same-frame digit template from an ID line.
            var floorSample=Cell(ox,oy,0,16);
            int floorBest=int.MaxValue,floorSecond=int.MaxValue;char floorDigit='?';
            foreach(var template in templates)
            {
                int difference=floorSample.Distance(template.Value);
                if(difference<floorBest)
                {floorSecond=floorBest;floorBest=difference;floorDigit=template.Key;}
                else if(difference<floorSecond)floorSecond=difference;
            }
            int plane=floorDigit-'0';
            if(floorBest>2||floorSecond-floorBest<5||plane is <0 or >3)continue;
            int x=expected.X,y=expected.Y;
            var evidence=RuneLiteGridReader.Parse(new[]{
                $"Tile {x}, {y}, {plane}",$"Chunk ID {expected.Chunk}",$"Region ID {expected.Region}"});
            if(evidence is null)continue;
            matched++;accepted.Add(evidence);
        }
        diagnostic=$"attempted=true mode=MONO7X11 threshold={WhiteThreshold} "+
            $"roi={width}x{height} bright_pixels={brightPixels} "+
            $"ocr_agreements={observations.Count(o=>o==expected)} anchors_tested={visited} "+
            $"first_comma_shapes={firstCommaShapes} matching_comma_pairs={matchingCommas} "+
            $"empty_separator_pairs={emptySeparators} comma_layouts={commaCandidates} "+
            $"digit_templates={idCandidates} unknown_tile_digits={unknownTileDigits} "+
            $"unknown_singleton_rejects={unknownSingletonRejects} repeated_unknown_layouts={uniqueRepeatedDigitLayouts} "+
            $"full_matches={matched} distinct_positions={accepted.Count} "+
            $"elapsed_ms={elapsed.ElapsedMilliseconds} "+
            $"result={(accepted.Count==1?"PIXEL_GRID_VERIFIED":"NOT_RELEASED")}";
        return accepted.Count==1?accepted.Single():null;
    }
}
