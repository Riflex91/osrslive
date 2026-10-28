namespace OSRSLiveDiagnostics;

// A user click can queue the *calibration dialog* while the exporter still
// holds an old position.json. Queuing never authorizes game input; only a
// newly observed, post-request Plugin Hub tile in the same verified client
// may show the ordinary consent/calibration UI. It expires automatically.
internal sealed record RuneLiteQueuedStart(string DestinationId,long Generation,
    IntPtr Window,string? WindowTitle,DateTimeOffset RequestedAt)
{
    internal static readonly TimeSpan MaxWait=TimeSpan.FromMinutes(2);

    internal bool IsExpired(DateTimeOffset now)=>
        now-RequestedAt>MaxWait;

    internal bool SameContext(string? destinationId,long generation,IntPtr window,
        string? windowTitle)=>
        destinationId==DestinationId&&generation==Generation&&
        (Window==IntPtr.Zero||Window==window&&WindowTitle==windowTitle);

    internal bool CanOpenCalibration(RuneLitePositionState state,string? destinationId,
        long generation,IntPtr window,string? windowTitle,DateTimeOffset now)=>
        !IsExpired(now)&&SameContext(destinationId,generation,window,windowTitle)&&
        window!=IntPtr.Zero&&state.Status=="API_POSITION_UPDATED"&&
        state.Tile is not null&&state.ObservedUtc>RequestedAt&&
        now-state.ObservedUtc>=TimeSpan.Zero&&
        now-state.ObservedUtc<=TimeSpan.FromSeconds(3);
}

internal static class RuneLiteQueuedStartTests
{
    internal static void Run()
    {
        static void Require(bool condition,string reason)
        {if(!condition)throw new Exception("RuneLite queued start: "+reason);}
        var now=DateTimeOffset.UtcNow;
        var window=new IntPtr(123);
        var queued=new RuneLiteQueuedStart("Lumbridge",2,window,"RuneLite - Test",now);
        var oldTile=new RuneLitePositionState("API_WAITING_FOR_NEW_EVENT",
            Diagnostic:"last_old_plugin_event_only=true");
        var preRequestEvent=new RuneLitePositionState("API_POSITION_UPDATED",
            new WorldTile(3210,3210),now.AddSeconds(-1));
        var freshEvent=new RuneLitePositionState("API_POSITION_UPDATED",
            new WorldTile(3210,3211),now.AddSeconds(1));
        Require(!queued.CanOpenCalibration(oldTile,"Lumbridge",2,window,"RuneLite - Test",
                    now.AddSeconds(1))&&
                !queued.CanOpenCalibration(preRequestEvent,"Lumbridge",2,window,
                    "RuneLite - Test",now.AddSeconds(1)),
            "historical or pre-click coordinates never open a walking dialog");
        Require(queued.CanOpenCalibration(freshEvent,"Lumbridge",2,window,
                    "RuneLite - Test",now.AddSeconds(1.2)),
            "only a post-request freshly exported tile may open calibration");
        Require(!queued.CanOpenCalibration(freshEvent,"Varrock",2,window,
                    "RuneLite - Test",now.AddSeconds(1.2))&&
                !queued.CanOpenCalibration(freshEvent,"Lumbridge",3,window,
                    "RuneLite - Test",now.AddSeconds(1.2))&&
                !queued.CanOpenCalibration(freshEvent,"Lumbridge",2,new IntPtr(999),
                    "RuneLite - Test",now.AddSeconds(1.2))&&
                !queued.CanOpenCalibration(freshEvent,"Lumbridge",2,window,
                    "RuneLite - Other",now.AddSeconds(1.2)),
            "target, generation, HWND and character title are bound to the original request");
        Require(!queued.CanOpenCalibration(freshEvent,"Lumbridge",2,window,
                    "RuneLite - Test",now.AddMinutes(3))&&queued.IsExpired(now.AddMinutes(3)),
            "a waiting request expires and cannot silently arm a later session");
    }
}
