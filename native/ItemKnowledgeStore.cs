using Microsoft.Data.Sqlite;

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
    private SqliteConnection? connection;
    private bool unavailable;
    private string session=Guid.NewGuid().ToString("N");
    internal static readonly TimeSpan Lifetime=TimeSpan.FromMinutes(5);
    internal ItemKnowledgeStore(string path,Action<string>? report=null)
    {this.path=path;this.report=report??(_=>{});}
    internal void ResetSession(){identities.Clear();session=Guid.NewGuid().ToString("N");}
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
            if(identities.TryGetValue(key,out var prior)&&
                (prior.Ambiguous||prior.Name!=e.Item.Name))
                identities[key]=prior with{Ambiguous=true};
            else identities[key]=new(e.Item.Name,now);
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
                    "ON CONFLICT(session_id,digest) DO UPDATE SET verified_utc=excluded.verified_utc,ambiguous=excluded.ambiguous",
                    ("$session",session),("$digest",e.Slot.IdentityDigest!),("$name",e.Item.Name),
                    ("$time",now.ToString("O")),("$ambiguous",identities[e.Slot.IdentityDigest!].Ambiguous?1:0));
            }
            transaction.Commit();
        }
        catch(Exception ex) when(ex is SqliteException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            unavailable=true;connection?.Dispose();connection=null;
            report("ITEM_DATABASE_UNAVAILABLE reason="+ex.GetType().Name+" verified_session_cache_only=true");
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
                if(Convert.ToInt32(version.ExecuteScalar()) is not (0 or 1))
                    throw new InvalidDataException("ITEM_DATABASE_SCHEMA_UNSUPPORTED");
            }
            Execute(db,null,"PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; "+
                "CREATE TABLE IF NOT EXISTS items(name TEXT PRIMARY KEY,first_seen_utc TEXT NOT NULL,last_seen_utc TEXT NOT NULL); "+
                "CREATE TABLE IF NOT EXISTS menu_actions(item_name TEXT NOT NULL REFERENCES items(name),context TEXT NOT NULL,action TEXT NOT NULL,last_seen_utc TEXT NOT NULL,PRIMARY KEY(item_name,context,action)); "+
                "CREATE TABLE IF NOT EXISTS visual_observations(session_id TEXT NOT NULL,digest TEXT NOT NULL,item_name TEXT NOT NULL REFERENCES items(name),profile TEXT NOT NULL,verified_utc TEXT NOT NULL,ambiguous INTEGER NOT NULL,PRIMARY KEY(session_id,digest)); PRAGMA user_version=1;");
            connection=db;return db;
        }
        catch{db.Dispose();throw;}
    }
    private static void Execute(SqliteConnection db,SqliteTransaction? transaction,string sql,params (string Name,object Value)[] args)
    {
        using var command=db.CreateCommand();command.Transaction=transaction;command.CommandText=sql;
        foreach(var arg in args)command.Parameters.AddWithValue(arg.Name,arg.Value);
        command.ExecuteNonQuery();
    }
    public void Dispose(){connection?.Dispose();connection=null;}
}
