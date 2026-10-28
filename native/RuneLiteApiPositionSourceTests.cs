namespace OSRSLiveDiagnostics;

internal static class RuneLiteApiPositionSourceTests
{
    internal static void Run()
    {
        static void Require(bool ok,string why)
        {if(!ok)throw new Exception("RuneLite API position events: "+why);}
        var now=new DateTimeOffset(2026,10,28,17,0,0,TimeSpan.Zero);
        static RuneLitePositionExportSource.Snapshot Snapshot(WorldTile tile,DateTimeOffset at)
        {
            int chunk=((tile.X>>3)<<11)|(tile.Y>>3);
            int region=((tile.X>>6)<<8)|(tile.Y>>6);
            return new(new(tile,chunk,region),at,0,true);
        }
        var tracker=new RuneLiteApiPositionTracker();
        var starting=new WorldTile(3176,3198,0);
        var old=Snapshot(starting,now.AddMinutes(-5));
        Require(tracker.Observe(old,now,"fixture").Status=="API_WAITING_FOR_NEW_EVENT",
            "an old position from a previous login is never treated as a new live event");
        var recent=Snapshot(starting,now.AddSeconds(-1));
        Require(tracker.Observe(recent,now,"fixture") is
            {Status:"API_POSITION_UPDATED",Tile:{X:3176,Y:3198,Plane:0}},
            "a recent checksum-verified client API event enters the current session");
        Require(tracker.Observe(recent,now.AddMilliseconds(800),"fixture").Diagnostic
                .Contains("new_event=false"),
            "polling the same event must not claim another real player movement");
        var stationary=tracker.Observe(recent,now.AddSeconds(90),"fixture");
        Require(stationary is {Status:"API_POSITION_LAST_KNOWN",Tile:{X:3176,Y:3198,Plane:0}}&&
                stationary.Diagnostic.Contains("screen_capture=false")&&
                stationary.Diagnostic.Contains("last_known_not_liveness_proof=true"),
            "a character standing still retains the last reported event without OCR, not a fabricated heartbeat");
        var moved=Snapshot(new(3177,3198,0),now.AddSeconds(90));
        Require(tracker.Observe(moved,now.AddSeconds(91),"fixture") is
            {Status:"API_POSITION_UPDATED",Tile:{X:3177,Y:3198,Plane:0}},
            "one-tile change is visible directly from RuneLite event without pixel quorums");
        Require(tracker.Observe(recent,now.AddSeconds(92),"fixture").Status=="PLUGIN_EXPORT_INVALID",
            "an older timestamp after a newer API event is rejected");
        tracker.Observe(moved,now.AddSeconds(91),"fixture");
        var contradiction=Snapshot(new(3178,3198,0),moved.ExportedAtUtc);
        Require(tracker.Observe(contradiction,now.AddSeconds(92),"fixture").Status=="PLUGIN_EXPORT_INVALID",
            "different locations cannot share a trusted event timestamp");
        tracker.Reset();
        Require(tracker.Observe(old,now,"fixture").Tile is null,
            "a window, profile or pause change erases prior session trust");
        Require(tracker.Observe(null,now,"fixture").Status=="PLUGIN_EXPORT_UNAVAILABLE",
            "a missing plugin file is reported without triggering OCR fallback");
    }
}
