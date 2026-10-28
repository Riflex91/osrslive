using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal static class RuneLitePositionTests
{
    internal static void Run()
    {
        static void Require(bool ok,string why){if(!ok)throw new Exception("RuneLite Grid Info: "+why);}
        // Values independently calculated for x=3141, y=3212, plane=0.
        var lines=new[]{"Tile 3141, 3212, 0","Chunk ID 803217","Region ID 12594"};
        var good=RuneLiteGridReader.Parse(lines);
        Require(good?.Tile==new WorldTile(3141,3212),"player coordinates and redundant IDs");
        Require(RuneLiteGridReader.Parse(new[]{"Tile3141, 3212, 0",lines[1],lines[2]})?.Tile==good!.Tile,"OCR can merge adjacent label and numeric columns");
        Require(RuneLiteGridReader.Parse(new[]{lines[0],"Chunk ID803217","Region ID12594"})==good,"OCR can merge both ID labels with their values");
        Require(RuneLiteGridReader.Parse(new[]{lines[0],"Chunk ID:803217","Region ID:12594"})==good,"OCR can omit spaces after colons");
        Require(RuneLiteGridReader.Parse(new[]{"Tile 3141, 3212, o",lines[1],lines[2]}) is null,"ambiguous plane is never guessed");
        Require(RuneLiteGridReader.Parse(new[]{"Tile3141» 3212» o",lines[1],lines[2]}) is null,"ambiguous live glyphs must never be silently rewritten");
        Require(RuneLiteGridReader.Parse(lines.Take(1)) is null,"hover-only tooltip is not the player panel");
        Require(RuneLiteGridReader.Parse(new[]{lines[0],"Chunk ID 803218",lines[2]}) is null,"bad chunk checksum");
        Require(RuneLiteGridReader.Parse(new[]{lines[0],lines[1],"Region ID 12595"}) is null,"bad region checksum");
        Require(RuneLiteGridReader.Parse(lines.Append("Tile 3200, 3200, 0")) is null,"two tile candidates are ambiguous");
        Require(RuneLiteGridReader.Parse(new[]{"Tile 3141, 3212, 4",lines[1],lines[2]}) is null,"invalid plane");
        var now=DateTimeOffset.UtcNow;var confirm=new RuneLiteOverlayConfirmation();var size=new Size(1000,600);
        Require(confirm.Observe(new(1),size,now,now,good).Tile is null,"one frame is insufficient");
        Require(confirm.Observe(new(1),size,now.AddMilliseconds(500),now.AddMilliseconds(500),good).Tile==good!.Tile,"fresh second frame");
        Require(confirm.Observe(new(1),size,now.AddMilliseconds(500),now.AddMilliseconds(600),good).Tile is null,"same frame is not independent");
        confirm.Reset();confirm.Observe(new(1),size,now,now,good);
        Require(confirm.Observe(new(2),size,now.AddMilliseconds(500),now.AddMilliseconds(500),good).Tile is null,"window change resets confirmation");
        Require(confirm.Observe(new(2),size,now.AddSeconds(1),now.AddSeconds(1),null).Tile is null,"missing panel immediately clears the position");
        Require(confirm.Observe(new(2),size,now,now.AddSeconds(3),good).Status=="STALE_FRAME","late OCR cannot confirm an old capture");
        Require(RuneLiteGridReader.Parse(new[]{"Tile 3141, 3212, 2",lines[1],lines[2]})?.Tile.Plane==2,"explicit player plane is preserved");
        var next=RuneLiteGridReader.Parse(new[]{"Tile 3142, 3212, 0",lines[1],lines[2]});
        Require(next is not null&&next.Tile!=good!.Tile,"same chunk and region may contain different tiles");
        Require(RuneLiteGridReader.ConsistentEvidence(new RuneLiteGridEvidence?[]{null,good,null,good})==good,"nonconflicting OCR variants");
        Require(RuneLiteGridReader.ConsistentEvidence(new RuneLiteGridEvidence?[]{good,next}) is null,"conflicting valid OCR variants reject the frame");
        Require(RuneLiteGridReader.ConsistentEvidence(new RuneLiteGridEvidence?[]{null,null}) is null,"missing OCR evidence rejects the frame");
        // Read-only movement diagnostics consume exclusively independently
        // confirmed tile observations. A gap is NOT a reconstructed route.
        var motion=new RuneLiteMotionTrace();
        var initialMotion=motion.Observe(new WorldTile(3141,3212,0),now,2);
        Require(initialMotion is not null&&initialMotion.Contains("FIRST_CONFIRMED")&&
                initialMotion.Contains("autonomous_input=false"),
            "movement log starts only after the caller supplies a confirmed tile");
        Require(motion.Observe(new WorldTile(3141,3212,0),now.AddSeconds(1),3) is null,
            "stable confirmed frames increment counters without duplicate movement events");
        var movedMotion=motion.Observe(new WorldTile(3142,3212,0),now.AddSeconds(2),6);
        Require(movedMotion is not null&&movedMotion.Contains("dx=1 dy=0")&&
                movedMotion.Contains("sample_gap=3")&&
                movedMotion.Contains("capture_gap_ms=1000")&&
                movedMotion.Contains("non_adjacent_observation=false"),
            "adjacent verified movement includes direction and observation latency");
        var skippedMotion=motion.Observe(new WorldTile(3158,3204,0),now.AddSeconds(3),9);
        Require(skippedMotion is not null&&skippedMotion.Contains("non_adjacent_observation=true")&&
                skippedMotion.Contains("route_between_samples_unknown=true"),
            "large jumps are observations, never invented traversed steps");
        var planeMotion=motion.Observe(new WorldTile(3158,3204,1),now.AddSeconds(4),11);
        Require(planeMotion is not null&&planeMotion.Contains("plane_delta=1")&&
                planeMotion.Contains("non_adjacent_observation=true"),
            "floor changes are reported without inventing stairs or teleport events");
        Require(motion.Observe(new WorldTile(3199,3199,0),now.AddSeconds(3),10)?.Contains("REJECTED_NON_MONOTONIC")==true&&
                motion.Statistics==(5,1,3,2),
            "out-of-order positions cannot modify verified movement telemetry");
        motion.Reset();
        Require(motion.Statistics==(0,0,0,0)&&
                motion.Observe(new WorldTile(3158,3204,1),now.AddSeconds(5),12)?.Contains("FIRST_CONFIRMED")==true,
            "user pause, lost client or source reset starts an isolated movement session");
        RuneLitePixelGridReaderTests.Run();
        RuneLitePositionExportSourceTests.Run();
        RuneLiteApiPositionSourceTests.Run();
        RuneLiteExportSummarySourceTests.Run();
        RuneLiteNavigationAdvisorTests.Run();
        RuneLiteRouteCoachTests.Run();
        Console.WriteLine("RUNELITE GRID PASS: player panel only, chunk/region checks, fresh independent frames, window changes, missing/stale rejection; no game input");
    }
    internal static async Task OcrFixture(OcrEngine engine)
    {
        using var bitmap=new Bitmap(800,400);
        using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Arial",12))
        {
            g.Clear(Color.Black);g.FillRectangle(Brushes.DarkSlateGray,8,28,240,80);
            g.DrawString("Tile",font,Brushes.White,14,32);g.DrawString("3141, 3212, 0",font,Brushes.White,100,32);
            g.DrawString("Chunk ID",font,Brushes.White,14,56);g.DrawString("803217",font,Brushes.White,140,56);
            g.DrawString("Region ID",font,Brushes.White,14,80);g.DrawString("12594",font,Brushes.White,140,80);
        }
        var zero=await RuneLiteGridReader.Read(bitmap,engine);
        if(zero is not null&&zero.Tile!=new WorldTile(3141,3212))throw new Exception("RuneLite OCR must never guess an ambiguous plane");
        Console.WriteLine(zero is null
            ?"RUNELITE ZERO-PLANE LIMITATION: ambiguous OCR rejected; live plane-0 readiness remains unverified"
            :"RUNELITE ZERO-PLANE synthetic coordinates read exactly; not a live readiness test");
        using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Arial",12))
        {
            g.FillRectangle(Brushes.DarkSlateGray,95,28,150,26);
            g.DrawString("3141, 3212, 2",font,Brushes.White,100,32);
        }
        var read=await RuneLiteGridReader.Read(bitmap,engine);
        if(read?.Tile!=new WorldTile(3141,3212,2))throw new Exception("RuneLite unambiguous plane-2 OCR fixture failed");
        Console.WriteLine("RUNELITE GRID OCR PASS: unambiguous plane-2 fixture, exact integers and checksum validation; no live plane-0 success claimed");
    }
}
