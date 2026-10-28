using System.Globalization;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

// Privacy-preserving adapter for the optional exports the user has enabled
// in the official OSRS Toolkit Exporter. NO item names/ids, wealth, offers,
// quest names, account names, JSON text or paths leave this reader. Its
// summaries are shown locally only, not copied into the diagnostic stream.
internal static class RuneLiteExportSummarySource
{
    internal sealed record Summary(string Kind,string Display,DateTimeOffset ExportedUtc,
        double AgeMinutes,bool Recent);
    internal sealed record Result(IReadOnlyList<Summary> Entries,string Diagnostic);
    private static readonly (string File,string Kind)[] Files=
    [
        ("character.json","CHARACTER"),("quests.json","QUESTS"),
        ("diaries.json","DIARIES"),("combat_achievements.json","COMBAT"),
        ("inventory.json","INVENTORY"),("equipment.json","EQUIPMENT"),
        ("bank.json","BANK"),("ge_offers.json","GE"),
    ];

    internal static Result Read(DateTimeOffset now,string windowTitle,
        string? directoryOverride=null)
    {
        string root=directoryOverride??Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".runelite","plugin-data","position-exporter");
        if(!Directory.Exists(root))
            return new([],"stage=ADDITIONAL_EXPORTS result=PLUGIN_DIRECTORY_ABSENT files=0 redacted=true");
        int invalid=0,missing=0,bound=0;
        var collected=new List<Summary>();
        try
        {
            foreach(var dir in Directory.EnumerateDirectories(root))
            {
                if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
                string character=Path.GetFileName(dir);
                if(!RuneLitePositionExportSource.MatchesWindowTitle(windowTitle,character))continue;
                bound++;
                // Never aggregate data from two plausible character folders.
                if(bound!=1)break;
                foreach(var (file,kind) in Files)
                {
                    var entry=ReadOne(Path.Combine(dir,file),kind,character,now);
                    if(entry is null){if(File.Exists(Path.Combine(dir,file)))invalid++;else missing++;}
                    else collected.Add(entry);
                }
                // The game-wide quest catalog has no personal data and is
                // written outside the character folder by the same plugin.
                var game=ReadOne(Path.Combine(root,"game_quests.json"),"GAME_QUESTS",character,now);
                if(game is null)
                {if(File.Exists(Path.Combine(root,"game_quests.json")))invalid++;else missing++;}
                else collected.Add(game);
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new([],"stage=ADDITIONAL_EXPORTS result=DIRECTORY_UNAVAILABLE "+
                $"type={ex.GetType().Name} redacted=true");
        }
        if(bound!=1)return new([],"stage=ADDITIONAL_EXPORTS "+
            $"result={(bound==0?"NO_MATCHING_CHARACTER":"AMBIGUOUS_CHARACTER")} "+
            "private_path_and_account_redacted=true");
        return new(collected,$"stage=ADDITIONAL_EXPORTS result=BOUND "+
            $"summary_categories={collected.Count} absent={missing} rejected={invalid} "+
            "names_items_gp_quests_json_redacted=true saved_snapshots_not_live_state=true");
    }

    private static Summary? ReadOne(string path,string kind,string character,DateTimeOffset now)
    {
        try
        {
            if(!File.Exists(path))return null;
            var file=new FileInfo(path);
            if((file.Attributes&FileAttributes.ReparsePoint)!=0||file.Length is <20 or >2_000_000)
                return null;
            var written=new DateTimeOffset(file.LastWriteTimeUtc,TimeSpan.Zero);
            if(written-now>TimeSpan.FromSeconds(2)||now-written>TimeSpan.FromDays(14))return null;
            using var doc=JsonDocument.Parse(File.ReadAllText(path),new JsonDocumentOptions{MaxDepth=32});
            var obj=doc.RootElement;
            if(obj.ValueKind!=JsonValueKind.Object||
               !DateTimeOffset.TryParse(obj.GetProperty("exported_at").GetString(),
                   CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var exported))
                return null;
            exported=exported.ToUniversalTime();
            var age=now-exported;
            if(age<TimeSpan.FromSeconds(-2)||age>TimeSpan.FromDays(14)||
               Math.Abs((exported-written).TotalSeconds)>60)return null;
            // For exports with an embedded identity, verify it exactly.
            if(kind=="CHARACTER"&&obj.GetProperty("account_name").GetString()!=character)return null;
            if(kind=="GE"&&obj.GetProperty("player_name").GetString()!=character)return null;
            if(kind is not "GE"&&obj.GetProperty("format_version").GetInt32()!=1)return null;

            string label=kind switch
            {
                "CHARACTER"=>$"Charakter · Gesamtlevel {Bounded(obj,"total_level",0,3000)} · "+
                    $"Kampflevel {Bounded(obj,"combat_level",0,200)}",
                "QUESTS"=>QuestsLabel(obj),
                "DIARIES"=>$"Tagebücher · {Bounded(obj.GetProperty("summary"),"tiers_complete",0,100)}/"+
                    $"{Bounded(obj.GetProperty("summary"),"tiers_possible",0,100)} Stufen",
                "COMBAT"=>$"Kampferfolge · {Bounded(obj.GetProperty("summary"),"total_tasks_completed",0,10000)} Aufgaben",
                "INVENTORY" or "EQUIPMENT" or "BANK"=>ItemsLabel(obj,kind),
                "GE"=>GeLabel(obj),
                "GAME_QUESTS"=>GameQuestsLabel(obj),
                _=>throw new InvalidDataException("Unknown fixed export category")
            };
            return new(kind,label,exported,age.TotalMinutes,age<=TimeSpan.FromMinutes(5));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or FormatException or ArgumentException or
            KeyNotFoundException or InvalidDataException or OverflowException)
        {return null;}
    }

    private static int Bounded(JsonElement o,string property,int min,int max)
    {
        int value=o.GetProperty(property).GetInt32();
        if(value<min||value>max)throw new InvalidDataException("Out-of-range exported count");
        return value;
    }

    private static string ItemsLabel(JsonElement obj,string kind)
    {
        if(!string.Equals(obj.GetProperty("kind").GetString(),kind,
            StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Wrong export kind");
        int count=Bounded(obj,"item_count",0,3000);
        if(obj.GetProperty("items").GetArrayLength()!=count)
            throw new InvalidDataException("Item count checksum mismatch");
        return (kind switch{"INVENTORY"=>"Inventar","EQUIPMENT"=>"Ausrüstung",_=>"Bank"})+
            $" · {count} belegte Plätze";
    }

    private static string QuestsLabel(JsonElement obj)
    {
        var counts=obj.GetProperty("summary");
        int done=Bounded(counts,"finished",0,500);
        int started=Bounded(counts,"in_progress",0,500);
        int unstarted=Bounded(counts,"not_started",0,500);
        if(obj.GetProperty("quests").GetArrayLength()!=done+started+unstarted)
            throw new InvalidDataException("Quest summary mismatch");
        return $"Quests · {done} erledigt";
    }

    private static string GeLabel(JsonElement obj)
    {
        int active=Bounded(obj,"active_offers",0,8);
        int empty=Bounded(obj,"empty_slots",0,8);
        if(obj.GetProperty("slots").GetArrayLength()!=8||active+empty>8)
            throw new InvalidDataException("Invalid Grand Exchange slots");
        return $"Markthalle · {active} aktive Angebote";
    }

    private static string GameQuestsLabel(JsonElement obj)
    {
        int count=Bounded(obj,"quest_count",0,2000);
        if(obj.GetProperty("quests").GetArrayLength()!=count)
            throw new InvalidDataException("Game quest catalog mismatch");
        return $"Quest-Katalog · {count} Einträge";
    }
}
