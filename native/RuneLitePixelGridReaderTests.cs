namespace OSRSLiveDiagnostics;

internal static class RuneLitePixelGridReaderTests
{
    private static readonly Dictionary<char,string[]> Digits=new()
    {
        ['0']=["01110","10001","10011","10101","11001","10001","01110"],
        ['1']=["00100","01100","00100","00100","00100","00100","01110"],
        ['2']=["01110","10001","00001","00010","00100","01000","11111"],
        ['3']=["11110","00001","00001","01110","00001","00001","11110"],
        ['4']=["00010","00110","01010","10010","11111","00010","00010"],
        ['5']=["11111","10000","10000","11110","00001","00001","11110"],
        // The synthetic 6 must have at least five distinct pixels from 8
        // and 5, matching the conservative separation already observed for
        // the real live ID glyphs. The old test-only 6 differed from 8 by 3.
        ['6']=["00110","01000","10000","11110","10001","10001","01110"],
        ['7']=["11111","00001","00010","00100","01000","01000","01000"],
        ['8']=["01110","10001","10001","01110","10001","10001","01110"],
        ['9']=["11110","10001","10001","11111","00001","00001","00001"]
    };

    private static Bitmap Fixture(int plane=0,int offsetX=0,int offsetY=0,int tileX=3143,int tileY=3210)
    {
        var image=new Bitmap(145+offsetX,82+offsetY);
        using(var g=Graphics.FromImage(image))g.Clear(Color.FromArgb(21,26,32));
        int chunk=((tileX>>3)<<11)|(tileY>>3),region=((tileX>>6)<<8)|(tileY>>6);
        string[] lines={$"Tile{tileX}, {tileY}, {plane}",$"Chunk ID   {chunk:D6}",$"Region ID   {region:D5}"};
        for(int row=0;row<lines.Length;row++)for(int col=0;col<lines[row].Length;col++)
        {
            int x0=7+offsetX+col*7,y0=13+offsetY+row*15;
            char symbol=lines[row][col];
            if(Digits.TryGetValue(symbol,out var digit))
            {
                for(int y=0;y<digit.Length;y++)for(int x=0;x<5;x++)
                    if(digit[y][x]=='1')image.SetPixel(x0+x+1,y0+y+2,Color.White);
            }
            else if(symbol==',')
            {
                image.SetPixel(x0+2,y0+8,Color.White);
                image.SetPixel(x0+3,y0+8,Color.White);
                image.SetPixel(x0+2,y0+9,Color.White);
                image.SetPixel(x0+3,y0+9,Color.White);
                image.SetPixel(x0+3,y0+10,Color.White);
            }
            else if(char.IsLetter(symbol))
            {
                for(int y=2;y<9;y++)image.SetPixel(x0+3,y0+y,Color.White);
            }
        }
        return image;
    }

    internal static void Run()
    {
        static void Require(bool valid,string detail)
        {if(!valid)throw new Exception("RuneLite pixel-grid OCR regression: "+detail);}
        var raw=new[]{"Tile3143» 3210, o","Chunk ID 803217","Region ID 12594"};
        var observation=RuneLitePixelGridReader.ReadObservation(raw);
        Require(observation is {X:3143,Y:3210,Chunk:"803217",Region:"12594"},
            "strict OCR coordinates and IDs are extracted without interpreting the wrong punctuation or plane");
        Require(RuneLiteGridReader.Parse(raw) is null,"ordinary OCR must still reject the malformed plane");
        Require(RuneLitePixelGridReader.ReadObservation(new[]{"Tile3143» 321ø, Ø",raw[1],raw[2]}) is null,
            "a partly illegible Y coordinate cannot train the pixel decoder");
        using(var bitmap=Fixture())
        {
            var candidates=new[]{observation!.Value,observation.Value};
            var result=RuneLitePixelGridReader.Read(bitmap,candidates,out string diagnostic);
            Require(result?.Tile==new WorldTile(3143,3210,0)&&diagnostic.Contains("PIXEL_GRID_VERIFIED"),
                "independently trained digit pixels, commas and strict IDs must verify a plane-0 bitmap");
            Require(RuneLitePixelGridReader.Read(bitmap,candidates.Take(1).ToArray(),out _) is null,
                "one OCR reading cannot authorize a pixel-derived position");
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation.Value,observation.Value with{X=3142}},out _) is null,
                "two disagreeing OCR positions cannot authorize a pixel-derived position");
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation.Value with{Chunk="803218"},observation.Value with{Chunk="803218"}},out _) is null,
                "wrong chunk identifier must fail pixel-coordinate checksum");
            // Both bitmap separators are independently checked for matching
            // commas, rather than normalizing any OCR letter into punctuation.
            for(int y=0;y<11;y++)for(int x=0;x<7;x++)
                bitmap.SetPixel(7+8*7+x,13+y,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(bitmap,candidates,out _) is null,
                "a missing visual comma must reject the position");
        }
        using(var bitmap=Fixture(2))
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation!.Value,observation.Value},out _)?.Tile.Plane==2,
                "the pixel decoder must preserve a real nonzero floor instead of guessing zero");
        // The supplied full-window screenshot has exactly the same thresholded
        // 131x55 panel pixels as the cropped screenshot, translated +14,+53.
        // Neither private image is embedded in this synthetic regression.
        using(var offsetBitmap=Fixture(offsetX:14,offsetY:53))
            Require(RuneLitePixelGridReader.Read(offsetBitmap,
                new[]{observation!.Value,observation.Value},out _)?.Tile==new WorldTile(3143,3210,0),
                "the observed full-window offset must decode without assuming a cropped-panel origin");
        using(var offsetBitmap=Fixture(offsetX:118,offsetY:82))
        {
            var shifted=RuneLitePixelGridReader.Read(offsetBitmap,
                new[]{observation!.Value,observation.Value},out string detail);
            Require(shifted?.Tile==new WorldTile(3143,3210,0)&&detail.Contains("PIXEL_GRID_VERIFIED"),
                "a cropped screenshot origin must not be mistaken for the real RuneLite overlay offset");
        }
        var moved=RuneLitePixelGridReader.ReadObservation(new[]{
            "Tile3144» 3211» o","Chunk ID 805265","Region ID 12594"});
        Require(moved is {X:3144,Y:3211,Chunk:"805265",Region:"12594"},
            "moved position must have an independent OCR hypothesis and both exact IDs");
        using(var movedBitmap=Fixture(tileX:3144,tileY:3211))
        {
            var result=RuneLitePixelGridReader.Read(movedBitmap,
                new[]{moved!.Value,moved.Value},out string movedDiagnostic);
            Require(result?.Tile==new WorldTile(3144,3211,0)&&
                    movedDiagnostic.Contains("repeated_unknown_layouts=")&&
                    !movedDiagnostic.Contains("repeated_unknown_layouts=0 "),
                "unknown ID digit 3 is allowed only when two Tile glyphs independently agree");
            // Break the second '3' in the Tile line. OCR alone may continue
            // claiming it, but two visibly different pixel shapes must fail.
            for(int dy=0;dy<11;dy++)for(int dx=0;dx<7;dx++)
                movedBitmap.SetPixel(7+10*7+dx,13+dy,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(movedBitmap,
                    new[]{moved.Value,moved.Value},out _) is null,
                "OCR claims cannot override a missing repeated unknown Tile glyph");
        }
        var singleton=RuneLitePixelGridReader.ReadObservation(new[]{
            "Tile3147» 3208» o","Chunk ID 805265","Region ID 12594"});
        Require(singleton is not null,"new moved position must retain valid OCR IDs");
        using(var bitmap=Fixture(tileX:3147,tileY:3208))
        {
            var result=RuneLitePixelGridReader.Read(bitmap,
                new[]{singleton!.Value,singleton.Value},out string diagnostic);
            Require(result is null&&diagnostic.Contains("unknown_singleton_rejects=")&&
                    !diagnostic.Contains("unknown_singleton_rejects=0 "),
                "a once-only unknown digit must remain blocked despite agreeing OCR passes");
        }
        // Real 1.8.8 movement: on 3144/3212, the ID 805265 visibly
        // contains '6'; at 3139/3216 the IDs 803218 and 12594 do NOT.
        // Two separate confirmed images may train a session-local reference
        // for this otherwise single-occurrence coordinate digit.
        var historical=new RuneLitePixelGridReader.ConfirmedGlyphHistory();
        var initial=RuneLitePixelGridReader.ReadObservation(new[]{
            "Tile3144» 3212» o","Chunk ID 805265","Region ID 12594"});
        var target=RuneLitePixelGridReader.ReadObservation(new[]{
            "Tile3139» 3216» o","Chunk ID 803218","Region ID 12594"});
        Require(initial is not null&&target is not null,"moving OCR fixtures must be parseable");
        using(var targetBitmap=Fixture(tileX:3139,tileY:3216))
        {
            var candidates=new[]{target!.Value,target.Value};
            Require(RuneLitePixelGridReader.Read(targetBitmap,candidates,out _) is null,
                "a new singleton must fail without independently confirmed earlier glyphs");
            var confirmer=new RuneLiteOverlayConfirmation();
            DateTimeOffset start=new(2026,10,27,20,0,0,TimeSpan.Zero);
            using(var trainingBitmap=Fixture(tileX:3144,tileY:3212))
            for(int i=0;i<3;i++)
            {
                IReadOnlyList<RuneLitePixelGridReader.GlyphSnapshot>? pending=null;
                var training=RuneLitePixelGridReader.Read(trainingBitmap,
                    new[]{initial!.Value,initial.Value},out _,onVerified:s=>pending=s);
                Require(training is not null&&pending is not null,
                    "per-frame glyph training must pass the ordinary independent pixel checks");
                var at=start+TimeSpan.FromSeconds(i);
                var status=confirmer.Observe(new IntPtr(73),trainingBitmap.Size,at,at+TimeSpan.FromMilliseconds(100),training);
                if(status.Tile is not null)historical.Confirm(pending!,at);
                if(i<2)Require(RuneLitePixelGridReader.Read(targetBitmap,candidates,out _,
                        history:historical) is null,
                    "an unconfirmed or single confirmed frame cannot train a singleton");
            }
            var recovered=RuneLitePixelGridReader.Read(targetBitmap,candidates,
                out string historyDiagnostic,history:historical);
            Require(recovered?.Tile==new WorldTile(3139,3216,0)&&
                    historyDiagnostic.Contains("historical_glyph_matches=")&&
                    !historyDiagnostic.Contains("historical_glyph_matches=0 "),
                "two separately confirmed training frames may verify the moved singleton in pixels");
            // Damage exactly the once-only 6 on the Tile line: historical
            // evidence may never override an actual contradictory glyph.
            for(int dy=0;dy<11;dy++)for(int dx=0;dx<7;dx++)
                targetBitmap.SetPixel(7+13*7+dx,13+dy,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(targetBitmap,candidates,out _,
                    history:historical) is null,
                "cached templates cannot excuse an absent current-frame singleton glyph");
            historical.Clear();
            Require(historical.Frames==0&&historical.Snapshots==0,
                "a client/window/session reset must discard every cached glyph");
        }
        // Actual 1.8.7 log: one nearest pass reads Region 12594 but lacks
        // the plane character; the other has all Tile digits yet misreads
        // Region as 12534. Two bicubic passes independently corroborate the
        // correct region, while chunk/XY each have two exact readings.
        string[][] partialPasses=
        [
            ["Tile3145„ 3210»","Chunk ID 805265","Region ID 12594"],
            ["Tile3145» 3210» o","Chunk ID 805265","Region ID 12534"],
            ["Tile3145, 321Ø, Ø","Chunk ID 8Ø5265","Region ID 12594"],
            ["Tile3145, 321ø,","Chunk ID 86265","Region ID 12594"]
        ];
        var fields=RuneLitePixelGridReader.ReadFieldQuorum(partialPasses,out var fieldDiagnostic);
        Require(fields is {Data:{X:3145,Y:3210,Chunk:"805265",Region:"12594"},
                XYVotes:2,ChunkVotes:2,RegionVotes:3,OutlierFields:1}&&
                fieldDiagnostic.Contains("CHECKSUM_VERIFIED"),
            "two independent votes per field can be combined without changing damaged glyphs");
        using(var movedBitmap=Fixture(tileX:3145,tileY:3210))
        {
            var isolated=partialPasses.Select(RuneLitePixelGridReader.ReadObservation)
                .Where(o=>o is not null).Select(o=>o!.Value).ToArray();
            Require(isolated.Length<2,
                "the regression must exercise a frame with fewer than two valid complete OCR lines");
            var verified=RuneLitePixelGridReader.Read(movedBitmap,isolated,out string detail,fields);
            Require(verified?.Tile==new WorldTile(3145,3210,0)&&
                    detail.Contains("evidence_source=FIELD_QUORUM"),
                "independent numeric field votes plus same-frame visible glyphs confirm the tile");
        }
        // Real 1.8.10 log, sample 43: a GE/Pot tooltip shares the Region
        // screen row. Each pass still has an exact 5-digit Region prefix;
        // the 2 nearest passes have clean 6-digit Chunk IDs. The full OCR
        // observations remain deliberately invalid, but separated suffixes
        // must not discard a separately checksummed and pixel-proved ID.
        string[][] tooltipPasses=
        [
            ["Tile3152„ 3197» o","Chunk ID 807311","Region ID 12593 12"],
            ["Tile3152» 3197» o","Chunk ID 807311","Region ID 12593 12"],
            ["Tile 3152, 3197, ø","Chunk ID 8ø7311","Region ID 12593 Pot (GE: 12 gp)"],
            ["Tile 3152, 3197, ø","Chunk ID 8ø7311 Pot (GE: 12 gp)","Region ID 12593 Pot (GE: 12 gp)"]
        ];
        Require(RuneLitePixelGridReader.ReadObservation(tooltipPasses[0]) is null,
            "the legacy whole-row OCR parser must not mistake tooltip text for an exact panel row");
        var tooltipQuorum=RuneLitePixelGridReader.ReadFieldQuorum(tooltipPasses,out string tooltipDiagnostic);
        Require(tooltipQuorum is {Data:{X:3152,Y:3197,Chunk:"807311",Region:"12593"},
                XYVotes:4,ChunkVotes:2,RegionVotes:4}&&
                tooltipDiagnostic.Contains("region_ignored_suffixes=4")&&
                tooltipDiagnostic.Contains("chunk_ignored_suffixes=0"),
            "only whitespace-separated extra game text may be ignored after exact fixed-width IDs: "+tooltipDiagnostic);
        // The fourth chunk ID above is OCR-damaged (8ø7311), so its Pot
        // suffix MUST NOT be counted. Check an otherwise complete chunk ID
        // with a legitimate separated tooltip independently.
        var chunkTooltipPasses=tooltipPasses.Select(rows=>rows.ToArray()).ToArray();
        chunkTooltipPasses[0][1]="Chunk ID 807311 Pot (GE: 12 gp)";
        var chunkTooltipQuorum=RuneLitePixelGridReader.ReadFieldQuorum(chunkTooltipPasses,out string chunkTooltipDiagnostic);
        Require(chunkTooltipQuorum is {Data:{X:3152,Y:3197,Chunk:"807311",Region:"12593"}}&&
                chunkTooltipDiagnostic.Contains("chunk_ignored_suffixes=1"),
            "a complete six-digit Chunk ID may keep its value with a separated tooltip: "+chunkTooltipDiagnostic);
        using(var tooltipBitmap=Fixture(tileX:3152,tileY:3197))
        {
            var proven=RuneLitePixelGridReader.Read(tooltipBitmap,Array.Empty<RuneLitePixelGridReader.Observation>(),
                out string tooltipPixelDiagnostic,tooltipQuorum);
            Require(proven?.Tile==new WorldTile(3152,3197,0)&&
                    tooltipPixelDiagnostic.Contains("PIXEL_GRID_VERIFIED"),
                "a tooltip-tainted OCR quorum still requires real same-frame digit pixels and commas");
        }
        string[][] tooLongId=
        [
            ["Tile3152„ 3197» o","Chunk ID 807311","Region ID 1259312"],
            ["Tile3152» 3197» o","Chunk ID 807311","Region ID 1259312"]
        ];
        Require(RuneLitePixelGridReader.ReadFieldQuorum(tooLongId,out string tooLongDiagnostic) is null&&
                tooLongDiagnostic.Contains("region_readings=0"),
            "adjacent digits may not be silently truncated into a valid Region ID");
        string[][] contradictorySuffix=
        [
            ["Tile3152„ 3197» o","Chunk ID 807311","Region ID 12593 Pot"],
            ["Tile3152» 3197» o","Chunk ID 807311","Region ID 12593 Pot"],
            ["Tile3152, 3197, ø","Chunk ID 807311","Region ID 12594 Pot"],
            ["Tile3152, 3197, ø","Chunk ID 807311","Region ID 12594 Pot"]
        ];
        Require(RuneLitePixelGridReader.ReadFieldQuorum(contradictorySuffix,out string suffixSplitDiagnostic) is null&&
                suffixSplitDiagnostic.Contains("region_quorums=2"),
            "conflicting suffixed Region IDs must remain blocked");
        // Real movement OCR can read '315Ø' in EVERY pass. Repeated IDs
        // alone only narrow the tile to an 8x8 chunk. The new independent
        // pixel path must match ALL eight labelled XY glyphs exactly and
        // uniquely, with real commas and a separately visible floor.
        string[][] damagedXY=
        [
            ["Tile315Ø„ 3196» o","Chunk ID 805263","Region ID 12593"],
            ["Tile315Ø» 3196» o","Chunk ID 805263","Region ID 12593"],
            ["Tile 315Ø, 3196, ø","Chunk ID 8Ø5263","Region ID 12593"],
            ["Tile315Ø, 3196, ø","Chunk ID 8ø5263","Region ID 12593"]
        ];
        Require(RuneLitePixelGridReader.ReadFieldQuorum(damagedXY,out _) is null,
            "no damaged Tile digit may become a numeric field vote");
        var onlyIds=RuneLitePixelGridReader.ReadIdQuorum(damagedXY,out string idDiagnostic);
        Require(onlyIds is {Chunk:"805263",Region:"12593",ChunkVotes:2,RegionVotes:4,TileLabelVotes:4}&&
                idDiagnostic.Contains("candidate_tiles=64"),
            "complete independent ID fields can constrain but not guess an unreadable XY pair: "+idDiagnostic);
        using(var idBitmap=Fixture(tileX:3150,tileY:3196))
        {
            var fromPixels=RuneLitePixelGridReader.Read(idBitmap,
                Array.Empty<RuneLitePixelGridReader.Observation>(),out string idRasterDiagnostic,
                idQuorum:onlyIds);
            Require(fromPixels?.Tile==new WorldTile(3150,3196,0)&&
                    idRasterDiagnostic.Contains("ID_ONLY_PIXEL_ENUMERATION")&&
                    idRasterDiagnostic.Contains("distinct_positions=1")&&
                    idRasterDiagnostic.Contains("PIXEL_GRID_VERIFIED"),
                "real labelled pixels and two real commas must uniquely determine a tile within the 64-cell chunk: "+idRasterDiagnostic);
            // The comma pixels are not optional even when two IDs agree.
            for(int dy=0;dy<11;dy++)for(int dx=0;dx<7;dx++)
                idBitmap.SetPixel(7+14*7+dx,13+dy,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(idBitmap,
                    Array.Empty<RuneLitePixelGridReader.Observation>(),out _,idQuorum:onlyIds) is null,
                "ID-only evidence must fail if a visible comma is missing");
        }
        var oneIdPass=RuneLitePixelGridReader.ReadIdQuorum(damagedXY.Take(1).ToArray(),out _);
        Require(oneIdPass is null,"one OCR frame cannot certify ID-only candidate enumeration");
        var numericXY=damagedXY.Select(a=>a.ToArray()).ToArray();
        numericXY[0][0]="Tile3150» 3196» o";
        Require(RuneLitePixelGridReader.ReadFieldQuorum(numericXY,out _) is null,
            "one correct XY OCR reading alone cannot establish an independent field quorum");
        var loneNumeric=RuneLitePixelGridReader.ReadIdQuorum(numericXY,out string numericDiagnostic);
        Require(loneNumeric is {SingleXY:(3150,3196)}&&
                numericDiagnostic.Contains("xy_readings=1")&&
                numericDiagnostic.Contains("candidate_tiles=64"),
            "one complete XY reading matching both corroborated IDs may be checked pixel by pixel: "+numericDiagnostic);
        using(var idBitmap=Fixture(tileX:3150,tileY:3196))
        {
            var fullLine=RuneLitePixelGridReader.ReadObservation(numericXY[0]);
            Require(fullLine is not null,
                "live sample 12 has one fully parseable OCR observation that cannot justify a position alone");
            var proven=RuneLitePixelGridReader.Read(idBitmap,new[]{fullLine!.Value},out string lonePixelDiagnostic,
                idQuorum:loneNumeric);
            Require(proven?.Tile==new WorldTile(3150,3196,0)&&
                    lonePixelDiagnostic.Contains("single_xy_matches_pixels=true")&&
                    lonePixelDiagnostic.Contains("ID_ONLY_PIXEL_ENUMERATION"),
                "one matching OCR observation must still require every independent digit pixel: "+lonePixelDiagnostic);
            var contradictoryFullLine=fullLine.Value with{Chunk="803215"};
            Require(RuneLitePixelGridReader.Read(idBitmap,new[]{contradictoryFullLine},out _,
                    idQuorum:loneNumeric) is null,
                "a complete observation with contradictory ID must veto the one-XY fallback");
        }
        var loneContradiction=numericXY.Select(a=>a.ToArray()).ToArray();
        loneContradiction[0][0]="Tile3151» 3196» o";
        var loneContradictoryIds=RuneLitePixelGridReader.ReadIdQuorum(loneContradiction,out _);
        Require(loneContradictoryIds is {SingleXY:(3151,3196)},
            "a wrong but in-chunk lone XY reading is not rejected before the independent pixels");
        using(var trueTileBitmap=Fixture(tileX:3150,tileY:3196))
        {
            Require(RuneLitePixelGridReader.Read(trueTileBitmap,
                    Array.Empty<RuneLitePixelGridReader.Observation>(),out string disagreement,
                    idQuorum:loneContradictoryIds) is null&&
                    disagreement.Contains("single_xy_matches_pixels=false")&&
                    disagreement.Contains("result=NOT_RELEASED"),
                "one contradictory numeric OCR reading must veto even uniquely verified true pixels: "+disagreement);
        }
        var outsideChunk=numericXY.Select(a=>a.ToArray()).ToArray();
        outsideChunk[0][0]="Tile3140» 3196» o";
        Require(RuneLitePixelGridReader.ReadIdQuorum(outsideChunk,out string outsideDiagnostic) is null&&
                outsideDiagnostic.Contains("SINGLE_XY_CHECKSUM_REJECTED"),
            "a lone numeric OCR pair contradicting the independently established chunk must veto the fallback");
        var repeatedNumeric=numericXY.Select(a=>a.ToArray()).ToArray();
        repeatedNumeric[1][0]="Tile3150» 3196» o";
        Require(RuneLitePixelGridReader.ReadIdQuorum(repeatedNumeric,out string repeatedDiagnostic) is null&&
                repeatedDiagnostic.Contains("xy_readings=2"),
            "two complete XY OCR readings must use the ordinary quorum path, not the fallback");
        var incorrectIds=damagedXY.Select(a=>a.ToArray()).ToArray();
        foreach(var pass in incorrectIds)pass[2]="Region ID 12594";
        Require(RuneLitePixelGridReader.ReadIdQuorum(incorrectIds,out string wrongIdDiagnostic) is null&&
                wrongIdDiagnostic.Contains("CHECKSUM_REJECTED"),
            "valid-looking independent IDs with an impossible chunk-to-region mapping must fail");
        using(var incompleteBitmap=Fixture(tileX:3140,tileY:3199))
        {
            var incompleteId=RuneLitePixelGridReader.ReadIdQuorum(new[]{
                new[]{"Tile314Ø» 3199» o","Chunk ID 803215","Region ID 12593"},
                new[]{"Tile314Ø» 3199» o","Chunk ID 803215","Region ID 12593"}},out _);
            Require(incompleteId is not null&&RuneLitePixelGridReader.Read(incompleteBitmap,
                    Array.Empty<RuneLitePixelGridReader.Observation>(),out _,idQuorum:incompleteId) is null,
                "an unknown unlabelled singleton may not be inferred from one bitmap and two ID votes");
        }
        string[][] conflicting=
        [
            ["Tile3145„ 3210»","Chunk ID 805265","Region ID 12594"],
            ["Tile3145» 3210» o","Chunk ID 805265","Region ID 12534"],
            ["Tile3145, 3210, o","Chunk ID 805265","Region ID 12534"],
            ["Tile3145, 3210, o","Chunk ID 805265","Region ID 12594"]
        ];
        Require(RuneLitePixelGridReader.ReadFieldQuorum(conflicting,out string splitDiagnostic) is null&&
                splitDiagnostic.Contains("region_quorums=2"),
            "two competing region quorums must fail closed");
        string[][] badChecksum=
        [
            ["Tile3145„ 3210»","Chunk ID 805265","Region ID 12534"],
            ["Tile3145» 3210» o","Chunk ID 805265","Region ID 12534"]
        ];
        Require(RuneLitePixelGridReader.ReadFieldQuorum(badChecksum,out string mismatchDiagnostic) is null&&
                mismatchDiagnostic.Contains("CHECKSUM_REJECTED"),
            "consistent OCR cannot override the mathematical Region ID checksum");
        using(var bitmap=Fixture())
        {
            for(int y=0;y<11;y++)for(int x=0;x<7;x++)
                bitmap.SetPixel(7+16*7+x,13+y,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation!.Value,observation.Value},out _) is null,
                "an unreadable floor glyph must never be inferred from the current map region");
        }
        Console.WriteLine("RUNELITE PIXEL GRID PASS: per-frame and confirmed-session ID templates, visible commas, two OCR ID/X/Y quorums, checksum, actual floor and fail-closed mutations");
    }
}
