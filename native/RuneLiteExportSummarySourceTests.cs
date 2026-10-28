using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal static class RuneLiteExportSummarySourceTests
{
    internal static void Run()
    {
        static void Require(bool okay,string detail)
        {if(!okay)throw new Exception("Additional RuneLite export summaries: "+detail);}
        DateTimeOffset now=DateTimeOffset.UtcNow;
        string root=Path.Combine(Path.GetTempPath(),"osrs-export-summary-test-"+
            Guid.NewGuid().ToString("N"));
        string profile=Path.Combine(root,"FixtureCharacter");
        Directory.CreateDirectory(profile);
        string timestamp=now.AddSeconds(-1).ToString("O");
        void Write(string name,object payload)
        {File.WriteAllText(Path.Combine(profile,name+".json"),JsonSerializer.Serialize(payload));}
        try
        {
            Write("character",new{exported_at=timestamp,format_version=1,
                account_name="FixtureCharacter",combat_level=38,total_level=456});
            Write("quests",new{exported_at=timestamp,format_version=1,
                summary=new{finished=15,in_progress=3,not_started=20},
                quests=Enumerable.Range(0,38).Select(i=>new{id=i}).ToArray()});
            Write("diaries",new{exported_at=timestamp,format_version=1,
                summary=new{tiers_complete=8,tiers_possible=48}});
            Write("combat_achievements",new{exported_at=timestamp,format_version=1,
                summary=new{total_tasks_completed=5,points=9}});
            Write("inventory",new{exported_at=timestamp,format_version=1,
                kind="inventory",item_count=1,
                items=new[]{new{slot=0,id=12345,quantity=200,name="SecretItem"}}});
            Write("equipment",new{exported_at=timestamp,format_version=1,
                kind="equipment",item_count=0,items=Array.Empty<object>()});
            Write("bank",new{exported_at=timestamp,format_version=1,
                kind="bank",item_count=0,items=Array.Empty<object>()});
            Write("ge_offers",new{exported_at=timestamp,player_name="FixtureCharacter",
                active_offers=1,empty_slots=7,slots=new object[8]});
            File.WriteAllText(Path.Combine(root,"game_quests.json"),JsonSerializer.Serialize(
                new{exported_at=timestamp,format_version=1,quest_count=4,
                    quests=Enumerable.Range(0,4).Select(i=>new{id=i}).ToArray()}));
            var all=RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root);
            Require(all.Entries.Count==9&&all.Entries.Any(e=>e.Kind=="INVENTORY"&&
                    e.Display.Contains("1 belegte"))&&
                    all.Entries.Any(e=>e.Kind=="CHARACTER"&&e.Display.Contains("456"))&&
                    all.Entries.All(e=>e.Recent)&&
                    all.Diagnostic.Contains("summary_categories=9"),
                "all nine known files should produce bounded local summaries: "+all.Diagnostic);
            Require(!all.Diagnostic.Contains("FixtureCharacter")&&
                    !all.Diagnostic.Contains("SecretItem")&&
                    !all.Diagnostic.Contains("12345")&&
                    all.Entries.All(e=>!e.Display.Contains("SecretItem")&&
                        !e.Display.Contains("12345")),
                "account names and item data must never appear in summary telemetry");
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – OtherPerson",root)
                    .Entries.Count==0,
                "files from a different character must never be opened");
            Write("character",new{exported_at=timestamp,format_version=1,
                account_name="OtherPerson",combat_level=38,total_level=456});
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Entries.All(e=>e.Kind!="CHARACTER"),
                "an embedded character identity mismatch must veto its file");
            Write("inventory",new{exported_at=timestamp,format_version=1,
                kind="inventory",item_count=5,items=Array.Empty<object>()});
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Entries.All(e=>e.Kind!="INVENTORY"),
                "item count must agree with parsed slots");
            Write("ge_offers",new{exported_at=timestamp,player_name="WrongPerson",
                active_offers=0,slots=Array.Empty<object>()});
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Entries.All(e=>e.Kind!="GE"),
                "GE snapshots cannot leak another character's offers");
            File.WriteAllText(Path.Combine(profile,"equipment.json"),"{ malformed JSON not a real snapshot");
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Entries.All(e=>e.Kind!="EQUIPMENT"),
                "malformed JSON fails closed without bringing down other summaries");
            string bankPath=Path.Combine(profile,"bank.json");
            Write("bank",new{exported_at=now.AddDays(-8).ToString("O"),
                format_version=1,kind="bank",item_count=0,items=Array.Empty<object>()});
            File.SetLastWriteTimeUtc(bankPath,now.AddDays(-8).UtcDateTime);
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Entries.Any(e=>e.Kind=="BANK"&&!e.Recent),
                "old bank exports are historical, never labelled a current inventory");
            Write("bank",new{exported_at=now.AddDays(-15).ToString("O"),
                format_version=1,kind="bank",item_count=0,items=Array.Empty<object>()});
            File.SetLastWriteTimeUtc(bankPath,now.AddDays(-15).UtcDateTime);
            Require(RuneLiteExportSummarySource.Read(now,"RuneLite – FixtureCharacter",root)
                    .Entries.All(e=>e.Kind!="BANK"),
                "ancient exported state is not treated as useable");
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,recursive:true);
        }
    }
}
