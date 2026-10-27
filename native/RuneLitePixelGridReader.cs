using System.Numerics;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

// Independent per-frame pixel verification for the narrowly configured World
// Location / Consolas-12 overlay. The reference digits come from the SAME
// captured bitmap's Chunk/Region fields, optionally corroborated by glyphs
// from previously independently CONFIRMED frames in this same live session;
// never from a stored screenshot, a learned position, or OCR punctuation guesses.
// Any font, layout, or glyph ambiguity falls back to the normal fail-closed OCR.
internal static class RuneLitePixelGridReader
{
    private const int Pitch=7, Height=11, RowPitch=15, WhiteThreshold=195;
    private static readonly int[] TileDigits={4,5,6,7,10,11,12,13,16};

    internal readonly record struct Glyph(ulong Low,ulong High)
    {
        internal int Ink=>BitOperations.PopCount(Low)+BitOperations.PopCount(High);
        internal int Distance(Glyph other)=>BitOperations.PopCount(Low^other.Low)+
            BitOperations.PopCount(High^other.High);
    }

    internal readonly record struct Observation(string Chunk,string Region,int X,int Y);
    internal readonly record struct FieldQuorum(Observation Data,int XYVotes,int ChunkVotes,
        int RegionVotes,int OutlierFields);
    internal sealed record GlyphSnapshot(IReadOnlyDictionary<char,Glyph> Digits);

    // Strictly ephemeral, local to one RuneLitePositionSource/one HWND and
    // framebuffer size. Each entry comes from an accepted PIXEL_GRID_VERIFIED
    // frame only AFTER its independent second-frame confirmation. Individual
    // digits must have two different confirmed capture timestamps before they
    // can resolve an otherwise untrained singleton in a future frame.
    internal sealed class ConfirmedGlyphHistory
    {
        private readonly List<(DateTimeOffset Captured,GlyphSnapshot Snapshot)> entries=new();
        internal int Snapshots=>entries.Count;
        internal int Frames=>entries.Select(e=>e.Captured).Distinct().Count();
        internal void Clear()=>entries.Clear();
        internal void Expire(DateTimeOffset now)=>entries.RemoveAll(e=>
            e.Captured>now||now-e.Captured>TimeSpan.FromMinutes(5));
        internal void Confirm(IReadOnlyList<GlyphSnapshot> snapshots,DateTimeOffset captured)
        {
            if(captured==default||entries.Any(e=>e.Captured==captured))return;
            Expire(captured);
            foreach(var snapshot in snapshots.Take(8))
                entries.Add((captured,new GlyphSnapshot(new Dictionary<char,Glyph>(snapshot.Digits))));
            // Bounded history; no image data or character position persisted.
            while(entries.Count>96)entries.RemoveAt(0);
        }
        internal bool Matches(char digit,Glyph sample,IReadOnlyDictionary<char,Glyph> current,
            out bool contradictory)
        {
            contradictory=false;
            var compatible=entries.Where(e=>
            {
                var overlap=current.Keys.Where(k=>e.Snapshot.Digits.ContainsKey(k)).ToArray();
                return overlap.Length>=5&&overlap.All(k=>
                    current[k].Distance(e.Snapshot.Digits[k])<=2);
            }).Where(e=>e.Snapshot.Digits.ContainsKey(digit)).ToArray();
            if(compatible.Select(e=>e.Captured).Distinct().Count()<2)return false;
            if(compatible.Any(e=>sample.Distance(e.Snapshot.Digits[digit])>2)||
               current.Any(k=>k.Key!=digit&&sample.Distance(k.Value)<5))
            {contradictory=true;return false;}
            // Check separation from EVERY other labelled historical digit in
            // the compatible snapshots, including those absent from this ID.
            if(compatible.Any(e=>e.Snapshot.Digits.Any(k=>
                k.Key!=digit&&sample.Distance(k.Value)<5)))
            {contradictory=true;return false;}
            return true;
        }
    }

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

    // Different OCR scales may damage DIFFERENT fields. Require each complete
    // numeric field to be read identically in at least two passes, then check
    // the independent Chunk/Region checksums before consulting real pixels.
    // This never repairs a character ('Ø', 'o', '»') or accepts its meaning.
    internal static FieldQuorum? ReadFieldQuorum(IReadOnlyList<string[]> passes,out string diagnostic)
    {
        var xy=new List<(int X,int Y)>();var chunks=new List<string>();
        var regions=new List<string>();
        foreach(var pass in passes)
        {
            var rows=pass.Select(s=>s.Trim()).ToArray();
            var tile=rows.Select(s=>Regex.Match(s,
                @"^Tile\s*:?\s*(\d{4})\D{1,4}(\d{4})(?:\D.*)?$",RegexOptions.IgnoreCase))
                .Where(m=>m.Success).ToArray();
            var chunk=rows.Select(s=>Regex.Match(s,
                @"^Chunk\s+ID\s*:?\s*(\d{6})$",RegexOptions.IgnoreCase))
                .Where(m=>m.Success).ToArray();
            var region=rows.Select(s=>Regex.Match(s,
                @"^Region\s+ID\s*:?\s*(\d{5})$",RegexOptions.IgnoreCase))
                .Where(m=>m.Success).ToArray();
            if(tile.Length==1)xy.Add((int.Parse(tile[0].Groups[1].Value),int.Parse(tile[0].Groups[2].Value)));
            if(chunk.Length==1)chunks.Add(chunk[0].Groups[1].Value);
            if(region.Length==1)regions.Add(region[0].Groups[1].Value);
        }
        var xyGroups=xy.GroupBy(v=>v).Where(g=>g.Count()>=2).ToArray();
        var chunkGroups=chunks.GroupBy(v=>v).Where(g=>g.Count()>=2).ToArray();
        var regionGroups=regions.GroupBy(v=>v).Where(g=>g.Count()>=2).ToArray();
        if(xyGroups.Length!=1||chunkGroups.Length!=1||regionGroups.Length!=1)
        {
            diagnostic=$"stage=FIELD_QUORUM result=NOT_READY passes={passes.Count} "+
                $"xy_readings={xy.Count} chunk_readings={chunks.Count} region_readings={regions.Count} "+
                $"xy_quorums={xyGroups.Length} chunk_quorums={chunkGroups.Length} region_quorums={regionGroups.Length}";
            return null;
        }
        var (x,y)=xyGroups[0].Key;
        string chunkValue=chunkGroups[0].Key,regionValue=regionGroups[0].Key;
        if(x>16383||y>16383||int.Parse(chunkValue)!=((x>>3)<<11|(y>>3))||
           int.Parse(regionValue)!=((x>>6)<<8|(y>>6)))
        {
            diagnostic=$"stage=FIELD_QUORUM result=CHECKSUM_REJECTED passes={passes.Count} "+
                $"xy_votes={xyGroups[0].Count()} chunk_votes={chunkGroups[0].Count()} "+
                $"region_votes={regionGroups[0].Count()}";
            return null;
        }
        int outliers=xy.Count-xyGroups[0].Count()+chunks.Count-chunkGroups[0].Count()+
            regions.Count-regionGroups[0].Count();
        var result=new FieldQuorum(new Observation(chunkValue,regionValue,x,y),
            xyGroups[0].Count(),chunkGroups[0].Count(),regionGroups[0].Count(),outliers);
        diagnostic=$"stage=FIELD_QUORUM result=CHECKSUM_VERIFIED passes={passes.Count} "+
            $"xy_votes={result.XYVotes} chunk_votes={result.ChunkVotes} "+
            $"region_votes={result.RegionVotes} rejected_field_outliers={outliers}";
        return result;
    }

    internal static RuneLiteGridEvidence? Read(Bitmap bitmap,IReadOnlyList<Observation> observations,
        out string diagnostic,FieldQuorum? fieldQuorum=null,
        ConfirmedGlyphHistory? history=null,Action<IReadOnlyList<GlyphSnapshot>>? onVerified=null)
    {
        var elapsed=Stopwatch.StartNew();
        var agreed=observations.GroupBy(o=>o).Where(g=>g.Count()>=2).Select(g=>g.Key).ToArray();
        Observation expected;
        if(fieldQuorum is FieldQuorum quorum)
        {
            expected=quorum.Data;
            // A conflicting complete, mathematically valid observation is
            // stronger than a field quorum and must veto its use.
            if(observations.Any(o=>o!=expected&&RuneLiteGridReader.Parse(new[]{
                $"Tile {o.X}, {o.Y}, 0",$"Chunk ID {o.Chunk}",$"Region ID {o.Region}"}) is not null))
            {
                diagnostic="attempted=false mode=MONO7X11 reason=VALID_CONTRADICTORY_OCR_POSITION";
                return null;
            }
        }
        else if(agreed.Length!=1||observations.Any(o=>agreed.Length==1&&o!=agreed[0]))
        {
            diagnostic=$"attempted=false mode=MONO7X11 observations={observations.Count} agreeing_groups={agreed.Length} reason=INSUFFICIENT_INDEPENDENT_OCR_ID_AND_XY_AGREEMENT";
            return null;
        }
        else expected=agreed[0];
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
        int historicalGlyphMatches=0,historicalContradictions=0;
        var accepted=new HashSet<RuneLiteGridEvidence>();
        var acceptedSnapshots=new List<GlyphSnapshot>();
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
            var historical=new HashSet<char>();
            bool digitsValid=true;
            for(int i=0;i<8;i++)
            {
                var sample=Cell(ox,oy,0,TileDigits[i]);
                char expectedDigit=expectedTile[i];
                if(!reference.ContainsKey(expectedDigit))
                {
                    if(sample.Ink is <5 or >44){digitsValid=false;break;}
                    bool contradictory=false;
                    if(history is not null&&history.Matches(expectedDigit,sample,reference,out contradictory))
                    {historical.Add(expectedDigit);continue;}
                    if(history is not null&&contradictory)
                    {historicalContradictions++;digitsValid=false;break;}
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
            if(historical.Count>0)historicalGlyphMatches+=historical.Count;
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
            if(acceptedSnapshots.Count<8)
                acceptedSnapshots.Add(new GlyphSnapshot(new Dictionary<char,Glyph>(reference)));
        }
        diagnostic=$"attempted=true mode=MONO7X11 threshold={WhiteThreshold} "+
            $"roi={width}x{height} bright_pixels={brightPixels} "+
            $"ocr_agreements={observations.Count(o=>o==expected)} "+
            $"evidence_source={(fieldQuorum is null?"FULL_OBSERVATIONS":"FIELD_QUORUM")} "+
            $"field_xy_votes={fieldQuorum?.XYVotes??0} field_chunk_votes={fieldQuorum?.ChunkVotes??0} "+
            $"field_region_votes={fieldQuorum?.RegionVotes??0} anchors_tested={visited} "+
            $"first_comma_shapes={firstCommaShapes} matching_comma_pairs={matchingCommas} "+
            $"empty_separator_pairs={emptySeparators} comma_layouts={commaCandidates} "+
            $"digit_templates={idCandidates} unknown_tile_digits={unknownTileDigits} "+
            $"unknown_singleton_rejects={unknownSingletonRejects} repeated_unknown_layouts={uniqueRepeatedDigitLayouts} "+
            $"historical_glyph_matches={historicalGlyphMatches} historical_contradictions={historicalContradictions} "+
            $"trusted_history_frames={history?.Frames??0} trusted_history_snapshots={history?.Snapshots??0} "+
            $"full_matches={matched} distinct_positions={accepted.Count} "+
            $"elapsed_ms={elapsed.ElapsedMilliseconds} "+
            $"result={(accepted.Count==1?"PIXEL_GRID_VERIFIED":"NOT_RELEASED")}";
        if(accepted.Count!=1)return null;
        onVerified?.Invoke(acceptedSnapshots);
        return accepted.Single();
    }
}
