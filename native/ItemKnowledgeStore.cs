using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal sealed record ItemMenuEvidence(ObservedSlot Slot,InventoryItem Item,ContextMenu Menu);

// A catalogue of observed facts, never an authority to click a stored action.
// Pixel identities are reusable only inside the current verified session.
internal sealed class ItemKnowledgeStore:IDisposable
{
    private sealed record Identity(string Name,DateTimeOffset VerifiedUtc,bool Ambiguous=false);
    private readonly string path;
    private readonly Action<string> report;
    private readonly Dictionary<string,Identity> identities=new(StringComparer.Ordinal);
    private readonly HashSet<string> unverifiedReported=new(StringComparer.Ordinal);
    private SqliteConnection? connection;
    private bool unavailable;
    private string session=Guid.NewGuid().ToString("N");
    private bool referenceAttempted;
    private readonly HashSet<string> canonical=new(
        ExecutionCatalog.Training.SelectMany(s=>new[]{s.Product,s.Input,s.SecondInput,s.Tool})
            .Where(s=>!string.IsNullOrEmpty(s)&&s is not ("axe" or "pickaxe")).Select(s=>s!)
            .Concat(F2PProgression.Axes.Select(t=>t.Item)).Concat(F2PProgression.Pickaxes.Select(t=>t.Item))
            .Concat(CombatPolicy.Food).Concat(CombatPolicy.Specs.SelectMany(s=>s.Weapons))
            .Concat(new[]{"coins","pure essence","bronze arrow","air rune","mind rune"}),StringComparer.OrdinalIgnoreCase);
    internal static readonly TimeSpan Lifetime=TimeSpan.FromMinutes(5);
    internal static bool ReferenceRefreshRequired(int cachedNames,DateTimeOffset newest,DateTimeOffset now)=>
        cachedNames<1000||newest>now||now-newest>=TimeSpan.FromDays(7);
    internal ItemKnowledgeStore(string path,Action<string>? report=null)
    {this.path=path;this.report=report??(_=>{});}
    internal void ResetSession(){identities.Clear();unverifiedReported.Clear();session=Guid.NewGuid().ToString("N");}
    internal async Task EnsureReference(CancellationToken ct)
    {
        if(referenceAttempted||unavailable)return;
        referenceAttempted=true;
        try
        {
            var db=Open();DateTimeOffset newest=default;
            int cachedNames=0;
            using(var command=db.CreateCommand())
            {
                command.CommandText="SELECT normalized_name,fetched_utc FROM reference_items";
                using var reader=command.ExecuteReader();
                while(reader.Read())
                {
                    canonical.Add(reader.GetString(0));
                    cachedNames++;
                    if(DateTimeOffset.TryParse(reader.GetString(1),out var time)&&time>newest)newest=time;
                }
            }
            var now=DateTimeOffset.UtcNow;
            if(!ReferenceRefreshRequired(cachedNames,newest,now))
            {
                report($"ITEM_REFERENCE_READY names={cachedNames} source=CACHED_WIKI_MAPPING age_hours={(int)(now-newest).TotalHours}");
                return;
            }
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(12)};
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OSRS-Play/1.7.2 (+https://github.com/Riflex91/osrslive; item-name-reference)");
            using var response=await http.GetAsync("https://prices.runescape.wiki/api/v1/osrs/mapping",ct);
            response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var entries=json.RootElement.EnumerateArray().Select(e=>(Id:e.GetProperty("id").GetInt32(),
                Name:ExecutionCatalog.Normalize(e.GetProperty("name").GetString()!)))
                .Where(e=>e.Id>0&&e.Name.Length is >0 and <=100).ToArray();
            if(entries.Length<1000)throw new InvalidDataException("ITEM_REFERENCE_INCOMPLETE");
            using var transaction=db.BeginTransaction();
            foreach(var entry in entries)
                Execute(db,transaction,"INSERT INTO reference_items(item_id,normalized_name,fetched_utc) VALUES($id,$name,$time) "+
                    "ON CONFLICT(item_id) DO UPDATE SET normalized_name=excluded.normalized_name,fetched_utc=excluded.fetched_utc",
                    ("$id",entry.Id),("$name",entry.Name),("$time",now.ToString("O")));
            transaction.Commit();
            foreach(var entry in entries)canonical.Add(entry.Name);
            report($"ITEM_REFERENCE_READY names={entries.Length} source=OSRS_WIKI_MAPPING");
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {report("ITEM_REFERENCE_UNAVAILABLE reason=TIMEOUT cached_or_builtin_names_only=true");}
        catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException)
        {report("ITEM_REFERENCE_UNAVAILABLE reason="+ex.GetType().Name+" previously_verified_names_only=true");}
        catch(Exception ex) when(ex is SqliteException or IOException or UnauthorizedAccessException)
        {Disable(ex);}
    }
    private static bool Digest(string? digest)=>digest is {Length:64}&&digest.All(Uri.IsHexDigit);
    internal InventoryItem? Recognize(ObservedSlot slot,DateTimeOffset now)
    {
        if(slot.Empty||slot.Index is <0 or >=28||slot.Bounds!=ExecutionUi.InventorySlot(slot.Index)||
            !Digest(slot.IdentityDigest)||!identities.TryGetValue(slot.IdentityDigest!,out var known)||
            known.Ambiguous||known.VerifiedUtc>now||now-known.VerifiedUtc>Lifetime)return null;
        // Names may be reused. Stock quantities are always from this frame.
        return new(slot.Index,known.Name,ExecutionCatalog.Stackable(known.Name)?slot.Quantity:1,
            slot.Signature,slot.Bounds);
    }
    internal void Learn(IReadOnlyList<ItemMenuEvidence> evidence,DateTimeOffset now)
    {
        var accepted=evidence.Where(e=>!e.Slot.Empty&&Digest(e.Slot.IdentityDigest)&&
            e.Slot.Index is >=0 and <28&&e.Menu.Exact("Cancel") is not null&&
            e.Menu.Entries.Count(x=>x.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase))==1&&
            e.Slot.Index==e.Item.Slot&&e.Slot.Bounds==ExecutionUi.InventorySlot(e.Slot.Index)&&
            e.Slot.Signature==e.Item.Signature&&e.Item.Name.Length is >0 and <=50&&
            e.Menu.Entries.Count(x=>x.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase)&&
                ExecutionCatalog.Normalize(x.Text[8..])==e.Item.Name)==1).ToArray();
        foreach(var e in accepted)
        {
            string key=e.Slot.IdentityDigest!;
            if(identities.TryGetValue(key,out var prior)&&canonical.Contains(prior.Name)&&
                (prior.Ambiguous||prior.Name!=e.Item.Name))identities[key]=prior with{Ambiguous=true};
            else identities[key]=new(e.Item.Name,now,Ambiguous:!canonical.Contains(e.Item.Name));
            // The same pixel identity may be read in several activity cycles.
            // Keep every observation, but report an untrusted OCR spelling
            // only once per identity/spelling in this session.
            if(!canonical.Contains(e.Item.Name)&&unverifiedReported.Add(key+":"+e.Item.Name))
                report("ITEM_NAME_UNVERIFIED cache_authorized=false repeat_suppressed=true");
        }
        if(accepted.Length==0||unavailable)return;
        try
        {
            var db=Open();using var transaction=db.BeginTransaction();
            foreach(var e in accepted)
            {
                Execute(db,transaction,"INSERT INTO items(name,first_seen_utc,last_seen_utc) VALUES($name,$time,$time) "+
                    "ON CONFLICT(name) DO UPDATE SET last_seen_utc=excluded.last_seen_utc",
                    ("$name",e.Item.Name),("$time",now.ToString("O")));
                foreach(string action in e.Menu.Entries.Select(a=>a.Text).Where(a=>a.Length<=120&&
                    !a.Equals("Cancel",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal))
                    Execute(db,transaction,"INSERT INTO menu_actions(item_name,context,action,last_seen_utc) VALUES($name,'inventory',$action,$time) "+
                        "ON CONFLICT(item_name,context,action) DO UPDATE SET last_seen_utc=excluded.last_seen_utc",
                        ("$name",e.Item.Name),("$action",action),("$time",now.ToString("O")));
                Execute(db,transaction,"INSERT INTO visual_observations(session_id,digest,item_name,profile,verified_utc,ambiguous) "+
                    "VALUES($session,$digest,$name,'native-1000x600-rgba32-v1',$time,$ambiguous) "+
                    "ON CONFLICT(session_id,digest) DO UPDATE SET item_name=excluded.item_name,verified_utc=excluded.verified_utc,ambiguous=excluded.ambiguous",
                    ("$session",session),("$digest",e.Slot.IdentityDigest!),("$name",e.Item.Name),
                    ("$time",now.ToString("O")),("$ambiguous",identities[e.Slot.IdentityDigest!].Ambiguous?1:0));
            }
            transaction.Commit();
        }
        catch(Exception ex) when(ex is SqliteException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Disable(ex);
        }
    }
    private SqliteConnection Open()
    {
        if(connection is not null)return connection;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=path,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false,DefaultTimeout=1}.ToString());
        try
        {
            db.Open();
            using(var version=db.CreateCommand())
            {
                version.CommandText="PRAGMA user_version";
                if(Convert.ToInt32(version.ExecuteScalar()) is not (0 or 1 or 2))
                    throw new InvalidDataException("ITEM_DATABASE_SCHEMA_UNSUPPORTED");
            }
            Execute(db,null,"PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; "+
                "CREATE TABLE IF NOT EXISTS items(name TEXT PRIMARY KEY,first_seen_utc TEXT NOT NULL,last_seen_utc TEXT NOT NULL); "+
                "CREATE TABLE IF NOT EXISTS menu_actions(item_name TEXT NOT NULL REFERENCES items(name),context TEXT NOT NULL,action TEXT NOT NULL,last_seen_utc TEXT NOT NULL,PRIMARY KEY(item_name,context,action)); "+
                    "CREATE TABLE IF NOT EXISTS visual_observations(session_id TEXT NOT NULL,digest TEXT NOT NULL,item_name TEXT NOT NULL REFERENCES items(name),profile TEXT NOT NULL,verified_utc TEXT NOT NULL,ambiguous INTEGER NOT NULL,PRIMARY KEY(session_id,digest)); "+
                    "CREATE TABLE IF NOT EXISTS reference_items(item_id INTEGER PRIMARY KEY,normalized_name TEXT NOT NULL,fetched_utc TEXT NOT NULL); PRAGMA user_version=2;");
            connection=db;return db;
        }
        catch{db.Dispose();throw;}
    }
    private void Disable(Exception ex)
    {
        unavailable=true;connection?.Dispose();connection=null;
        report("ITEM_DATABASE_UNAVAILABLE reason="+ex.GetType().Name+" verified_session_cache_only=true");
    }
    private static void Execute(SqliteConnection db,SqliteTransaction? transaction,string sql,params (string Name,object Value)[] args)
    {
        using var command=db.CreateCommand();command.Transaction=transaction;command.CommandText=sql;
        foreach(var arg in args)command.Parameters.AddWithValue(arg.Name,arg.Value);
        command.ExecuteNonQuery();
    }
    public void Dispose(){connection?.Dispose();connection=null;}
}
