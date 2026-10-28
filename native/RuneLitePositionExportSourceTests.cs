using System.Globalization;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal static class RuneLitePositionExportSourceTests
{
    internal static void Run()
    {
        static void Require(bool okay,string detail)
        {if(!okay)throw new Exception("RuneLite position-export integration: "+detail);}

        var now=DateTimeOffset.UtcNow;
        string root=Path.Combine(Path.GetTempPath(),"osrs-position-export-test-"+Guid.NewGuid().ToString("N"));
        string characterDir=Path.Combine(root,"FixtureCharacter");
        Directory.CreateDirectory(characterDir);
        string path=Path.Combine(characterDir,"position.json");
        try
        {
            static string Json(DateTimeOffset when,int region=12593,int x=3155,int y=3186,
                string player="FixtureCharacter")=>JsonSerializer.Serialize(new
                {
                    exported_at=when.ToString("O",CultureInfo.InvariantCulture),
                    player_name=player,world=301,plane=0,x,y,region_id=region,
                    region_x=x&63,region_y=y&63
                });
            File.WriteAllText(path,Json(now.AddMilliseconds(-500)));
            var (fresh,detail)=RuneLitePositionExportSource.Read(now,
                "RuneLite – FixtureCharacter",root);
            Require(fresh is {Fresh:true,Position:{Tile:{X:3155,Y:3186,Plane:0},
                    ChunkId:807310,RegionId:12593}}&&
                    detail.Contains("FRESH_CLIENT_COORDINATE")&&
                    !detail.Contains("FixtureCharacter"),
                "the official Plugin Hub position.json contains an independent checksum-valid coordinate: "+detail);
            Require(RuneLitePositionExportSource.FrameDecision(now,
                    now.AddMilliseconds(-200))=="AWAIT_NEW_FRAME"&&
                    RuneLitePositionExportSource.FrameDecision(now,now)=="DIRECT"&&
                    RuneLitePositionExportSource.FrameDecision(now,
                        now.AddSeconds(3))=="DIRECT"&&
                    RuneLitePositionExportSource.FrameDecision(now,
                        now.AddSeconds(7))=="SCREEN_CROSSCHECK",
                "a newer export needs a newer frame; an old frame is never a Grid Info OCR failure");
            var (mismatch,titleDiagnostic)=RuneLitePositionExportSource.Read(now,
                "RuneLite – AnotherCharacter",root);
            Require(mismatch is null&&titleDiagnostic.Contains("NO_VERIFIED_FILE")&&
                    !titleDiagnostic.Contains("FixtureCharacter"),
                "never confuse the JSON of another character, or leak a character name in diagnostics");
            Require(!RuneLitePositionExportSource.MatchesWindowTitle("RuneLite – WrongFixtureCharacter",
                    "FixtureCharacter")&&
                    RuneLitePositionExportSource.MatchesWindowTitle("RuneLite - FixtureCharacter",
                    "FixtureCharacter"),
                "window matching must compare the complete display name after RuneLite's separator");
            File.WriteAllText(path,Json(now.AddMilliseconds(-500),region:12594));
            Require(RuneLitePositionExportSource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Data is null,
                "a mismatching region checksum must veto a plausible player coordinate");
            File.WriteAllText(path,Json(now.AddMilliseconds(-500),player:"DifferentPerson"));
            Require(RuneLitePositionExportSource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Data is null,
                "the export's private account name must match the filename and trusted RuneLite title");
            File.WriteAllText(path,"{ invalid JSON with enough bytes to exercise the parser");
            var malformed=RuneLitePositionExportSource.Read(now,"RuneLite – FixtureCharacter",root);
            Require(malformed.Data is null&&malformed.Diagnostic.Contains("READ_UNAVAILABLE")&&
                    !malformed.Diagnostic.Contains("FixtureCharacter"),
                "bad JSON must safely fall back to screen OCR without leaking file paths");
            File.WriteAllText(path,Json(now.AddMinutes(-3)));
            File.SetLastWriteTimeUtc(path,now.AddMinutes(-3).UtcDateTime);
            var (old,oldDiagnostic)=RuneLitePositionExportSource.Read(now,
                "RuneLite – FixtureCharacter",root);
            Require(old is {Fresh:false,Position:{Tile:{X:3155,Y:3186,Plane:0}}}&&
                    oldDiagnostic.Contains("LAST_RECORDED_POSITION_EVENT"),
                "event-based exporter retains its last exact tile without triggering screen OCR");
            File.WriteAllText(path,Json(now.AddDays(-3)));
            File.SetLastWriteTimeUtc(path,now.AddDays(-3).UtcDateTime);
            Require(RuneLitePositionExportSource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Data is null,
                "multi-day-old exports cannot be passed off as live measurements");
            File.WriteAllText(path,Json(now.AddMinutes(3)));
            File.SetLastWriteTimeUtc(path,now.UtcDateTime);
            Require(RuneLitePositionExportSource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Data is null,
                "future-dated exports are rejected without guessing the clock offset");
        }
        finally
        {
            // Only the test-created GUID-scoped directory, never a real
            // RuneLite profile or any other filesystem location.
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
        }
    }
}
