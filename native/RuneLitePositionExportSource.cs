using System.Globalization;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

// The unmodified, Plugin-Hub-reviewed "OSRS Toolkit Exporter" plugin writes
// position.json from client.getLocalPlayer().getWorldLocation() on tile changes.
// This reader NEVER installs a plugin, accesses RuneLite process memory, reads
// other exported data or reveals account names and paths in diagnostics.
internal static class RuneLitePositionExportSource
{
    internal readonly record struct Snapshot(RuneLiteGridEvidence Position,
        DateTimeOffset ExportedAtUtc,double AgeMs,bool Fresh);
    private static readonly TimeSpan FreshFor=TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxCorroborationAge=TimeSpan.FromHours(24);

    // WGC may return a buffered image captured shortly BEFORE the plugin
    // wrote the next tile. That is a normal transition, not unreadable Grid
    // Info. Await a genuinely newer captured frame instead of OCR-reading
    // an obsolete image or accidentally accepting the previous location.
    internal static string FrameDecision(DateTimeOffset exportedAtUtc,
        DateTimeOffset capturedAtUtc)
    {
        if(capturedAtUtc<exportedAtUtc)return "AWAIT_NEW_FRAME";
        if(capturedAtUtc-exportedAtUtc<=TimeSpan.FromSeconds(6))return "DIRECT";
        return "SCREEN_CROSSCHECK";
    }

    internal static (Snapshot? Data,string Diagnostic) Read(DateTimeOffset now,
        string windowTitle,string? directoryOverride=null)
    {
        string root=directoryOverride??Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".runelite","plugin-data","position-exporter");
        if(!Directory.Exists(root))
            return (null,"stage=POSITION_EXPORT result=NOT_INSTALLED plugin=OSRS_TOOLKIT_EXPORTER "+
                "automatic_plugin_install=false fallback=SCREEN_OCR");

        int files=0,bound=0,invalid=0;
        var candidates=new List<Snapshot>();
        try
        {
            foreach(string subdir in Directory.EnumerateDirectories(root))
            {
                if((File.GetAttributes(subdir)&FileAttributes.ReparsePoint)!=0)continue;
                string name=Path.GetFileName(subdir);
                if(!MatchesWindowTitle(windowTitle,name))continue;
                bound++;
                string path=Path.Combine(subdir,"position.json");
                if(!File.Exists(path))continue;
                files++;
                var info=new FileInfo(path);
                if((info.Attributes&FileAttributes.ReparsePoint)!=0||info.Length is <20 or >4096)
                {invalid++;continue;}
                var writtenAt=new DateTimeOffset(info.LastWriteTimeUtc,TimeSpan.Zero);
                if(now-writtenAt>MaxCorroborationAge||writtenAt-now>TimeSpan.FromSeconds(2))
                {invalid++;continue;}
                using var doc=JsonDocument.Parse(File.ReadAllText(path));
                var obj=doc.RootElement;
                if(obj.ValueKind!=JsonValueKind.Object||
                   obj.GetProperty("player_name").GetString()!=name||
                   !DateTimeOffset.TryParse(obj.GetProperty("exported_at").GetString(),
                       CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,
                       out var timestamp))
                {invalid++;continue;}
                timestamp=timestamp.ToUniversalTime();
                double age=(now-timestamp).TotalMilliseconds;
                if(age< -2000||age>MaxCorroborationAge.TotalMilliseconds||
                   Math.Abs((timestamp-writtenAt).TotalSeconds)>10)
                {invalid++;continue;}
                int x=obj.GetProperty("x").GetInt32(),y=obj.GetProperty("y").GetInt32();
                int plane=obj.GetProperty("plane").GetInt32();
                int region=obj.GetProperty("region_id").GetInt32();
                int regionX=obj.GetProperty("region_x").GetInt32();
                int regionY=obj.GetProperty("region_y").GetInt32();
                int world=obj.GetProperty("world").GetInt32();
                if(x is <0 or >16383||y is <0 or >16383||plane is <0 or >3||
                   world is <1 or >9999||region!=((x>>6)<<8|(y>>6))||
                   regionX!=(x&63)||regionY!=(y&63))
                {invalid++;continue;}
                string chunk=$"{((x>>3)<<11|(y>>3)):D6}";
                var evidence=RuneLiteGridReader.Parse(new[]{
                    $"Tile {x}, {y}, {plane}",$"Chunk ID {chunk}",$"Region ID {region:D5}"});
                if(evidence is null){invalid++;continue;}
                bool fresh=age<=FreshFor.TotalMilliseconds&&
                    now-writtenAt<=FreshFor&&writtenAt<=now.AddSeconds(2);
                candidates.Add(new(evidence,timestamp,age,fresh));
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or FormatException or
            ArgumentException or KeyNotFoundException)
        {
            return (null,$"stage=POSITION_EXPORT result=READ_UNAVAILABLE type={ex.GetType().Name} "+
                "path_and_account_name_redacted=true fallback=SCREEN_OCR");
        }
        if(candidates.Count!=1)
            return (null,$"stage=POSITION_EXPORT result={(candidates.Count==0?"NO_VERIFIED_FILE":"AMBIGUOUS_ACCOUNT")} "+
                $"matching_title_dirs={bound} files={files} invalid={invalid} valid={candidates.Count} "+
                "path_and_account_name_redacted=true fallback=SCREEN_OCR");
        var selected=candidates[0];
        return (selected,$"stage=POSITION_EXPORT result={(selected.Fresh?"FRESH_CLIENT_COORDINATE":"STALE_REQUIRES_LIVE_SCREEN_CROSSCHECK")} "+
            $"file_age_ms={selected.AgeMs:0} matching_title_dirs={bound} "+
            "source=OFFICIAL_RUNELITE_PLUGIN_HUB client_api=true player_name_redacted=true "+
            "network_transfer=false game_input=false");
    }

    internal static bool MatchesWindowTitle(string title,string character)
    {
        if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(character))return false;
        // RuneLite's ordinary Jagex-Launcher window titles include the logged
        // in display name after a delimiter. NEVER use a substring match.
        return new[]{" - "," – "," — "}.Any(separator=>
            title.StartsWith("RuneLite"+separator,StringComparison.OrdinalIgnoreCase)&&
            string.Equals(title[("RuneLite"+separator).Length..],character,
                StringComparison.OrdinalIgnoreCase));
    }
}
