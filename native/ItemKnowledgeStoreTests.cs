using Microsoft.Data.Sqlite;

namespace OSRSLiveDiagnostics;

internal static class ItemKnowledgeStoreTests
{
    internal static void Run()
    {
        static void Require(bool value,string reason)
        {if(!value)throw new Exception("Item database: "+reason);}
        string directory=Path.Combine(Path.GetTempPath(),"osrs-item-db-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"items.sqlite");
        var now=DateTimeOffset.UtcNow;
        var slot=new ObservedSlot(0,ExecutionUi.InventorySlot(0),1,false,17,2,new string('A',64));
        Require(ItemKnowledgeStore.ReferenceRefreshRequired(1,now,now)&&
            !ItemKnowledgeStore.ReferenceRefreshRequired(1000,now,now)&&
            ItemKnowledgeStore.ReferenceRefreshRequired(1000,now.AddHours(1),now)&&
            ItemKnowledgeStore.ReferenceRefreshRequired(1000,now.AddDays(-8),now),
            "partial, future-dated and stale Wiki catalogues cannot skip a refresh");
        ItemMenuEvidence Evidence(string name,ObservedSlot? s=null)
        {
            var observed=s??slot;
            var menu=new ContextMenu(new[]{new MenuEntry("Use "+name,new(750,320,140,14)),
                new MenuEntry("Examine "+name,new(750,340,140,14)),new MenuEntry("Cancel",new(750,360,140,14))});
            return new(observed,new(observed.Index,name,ExecutionCatalog.Stackable(name)?observed.Quantity:1,
                observed.Signature,observed.Bounds),menu);
        }
        try
        {
            var sessionReports=new List<string>();
            using(var store=new ItemKnowledgeStore(path,sessionReports.Add))
            {
                Require(store.Recognize(slot,now) is null,"unknown image is never guessed from a catalogue");
                store.Learn(new[]{Evidence("coins")},now);
                Require(store.Recognize(slot,now)?.Name=="coins","directly verified identity is reusable");
                Require(store.Recognize(slot with{Quantity=null},now)?.Quantity is null,
                    "unknown fresh quantity cannot inherit an old balance");
                Require(store.Recognize(slot with{Quantity=23},now)?.Quantity==23,"quantities come from the fresh frame");
                Require(store.Recognize(slot with{IdentityDigest=new string('B',64)},now) is null,
                    "changed complete image must be rescanned even if sampled signature matches");
                Require(store.Recognize(slot with{Empty=true},now) is null&&
                    store.Recognize(slot with{Bounds=new(0,0,32,32)},now) is null,
                    "empty slots and unsupported geometry cannot reuse item names");
                Require(store.Recognize(slot,now.AddSeconds(-1)) is null&&
                    store.Recognize(slot,now+ItemKnowledgeStore.Lifetime+TimeSpan.FromSeconds(1)) is null,
                    "future or expired evidence is rejected");
                store.Learn(new[]{Evidence("coins")},now);
                using(var db=new SqliteConnection("Data Source="+path+";Mode=ReadOnly;Pooling=False"))
                {
                    db.Open();using var query=db.CreateCommand();
                    query.CommandText="SELECT COUNT(*) FROM items";
                    Require(Convert.ToInt64(query.ExecuteScalar())==1,"catalogue is persistent and deduplicated");
                    query.CommandText="SELECT COUNT(*) FROM menu_actions WHERE item_name='coins' AND action<>'Cancel'";
                    Require(Convert.ToInt64(query.ExecuteScalar())==2,"actual menu actions are stored without duplicate Cancel controls");
                }
                store.Learn(new[]{Evidence("logs")},now);
                Require(store.Recognize(slot,now) is null,"conflicting names for identical pixels invalidate recognition");
                store.ResetSession();
                Require(store.Recognize(slot,now) is null,"session reset removes recognition authorization");
                store.Learn(new[]{Evidence("bi9 bones")},now);
                Require(store.Recognize(slot,now) is null,"unverified OCR spelling cannot become a reusable item identity");
                store.Learn(new[]{Evidence("bi9 bones")},now.AddMilliseconds(100));
                Require(sessionReports.Count(m=>m.StartsWith("ITEM_NAME_UNVERIFIED",StringComparison.Ordinal))==1,
                    "repeated unverified identity is reported only once per session");
                store.Learn(new[]{Evidence("logs")},now);
                Require(store.Recognize(slot,now)?.Name=="logs","a later directly observed canonical name can replace an untrusted spelling");
                store.ResetSession();
                store.Learn(new[]{Evidence("bi9 bones")},now);
                Require(sessionReports.Count(m=>m.StartsWith("ITEM_NAME_UNVERIFIED",StringComparison.Ordinal))==2,
                    "new session can report an independently observed unverified identity again");
                store.Learn(new[]{Evidence("logs")},now);
            }
            using(var reopened=new ItemKnowledgeStore(path))
                Require(reopened.Recognize(slot,now) is null,"persistent catalogue cannot impersonate a fresh session observation");
            using(var future=new SqliteConnection("Data Source="+path+";Pooling=False"))
            {
                future.Open();using var command=future.CreateCommand();command.CommandText="PRAGMA user_version=3";command.ExecuteNonQuery();
            }
            var schemaReports=new List<string>();
            using(var futureStore=new ItemKnowledgeStore(path,schemaReports.Add))futureStore.Learn(new[]{Evidence("logs")},now);
            Require(schemaReports.Count==1,"unsupported future schema is reported instead of overwritten");
            string broken=Path.Combine(directory,"broken.sqlite");File.WriteAllText(broken,"not a database");
            var reports=new List<string>();
            using(var unavailable=new ItemKnowledgeStore(broken,reports.Add))
            {
                unavailable.Learn(new[]{Evidence("logs")},now);
                Require(reports.Count==1&&unavailable.Recognize(slot,now)?.Name=="logs",
                    "storage failure is reported and preserves only directly observed in-memory evidence");
            }
            using var original=new Bitmap(1000,600);
            var first=ExecutionVision.InventoryDigests(original);
            original.SetPixel(ExecutionUi.InventorySlot(0).X+1,ExecutionUi.InventorySlot(0).Y+1,Color.White);
            var second=ExecutionVision.InventoryDigests(original);
            Require(first[0]!=second[0]&&first.Skip(1).SequenceEqual(second.Skip(1)),
                "SHA-256 includes pixels between legacy three-pixel sample points and isolates changed slots");
        }
        finally{Directory.Delete(directory,true);}
        Console.WriteLine("ITEM DATABASE PASS: durable items/actions, full-pixel identity, fresh quantities, conflicts, TTL/session isolation, unavailable storage");
    }
}
