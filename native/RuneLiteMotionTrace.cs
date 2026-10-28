namespace OSRSLiveDiagnostics;

// Read-only telemetry for genuine RuneLite position-change events. The
// current companion passes the exported event time, not a screenshot time.
// Motion between samples is OBSERVED, never inferred as a traversed route:
// the player can walk several tiles, teleport, or change planes in gaps.
internal sealed class RuneLiteMotionTrace
{
    private WorldTile? previous;
    private DateTimeOffset previousFrame;
    private long previousSample;
    private int confirmedFrames,stableFrames,changes,nonAdjacent;

    internal (int ConfirmedFrames,int StableFrames,int Changes,int NonAdjacent) Statistics=>(
        confirmedFrames,stableFrames,changes,nonAdjacent);

    internal void Reset()
    {
        previous=null;previousFrame=default;previousSample=0;
        confirmedFrames=stableFrames=changes=nonAdjacent=0;
    }

    internal string? Observe(WorldTile tile,DateTimeOffset capturedAt,long sample)
    {
        // The source enforces player binding, event checksum and monotonicity.
        // This ledger also refuses duplicate and reversed event timestamps.
        if(capturedAt==default||sample<=previousSample||
           (previous is not null&&capturedAt<=previousFrame))
            return $"result=REJECTED_NON_MONOTONIC sample={sample} "+
                "not_a_navigation_input=true";

        var old=previous;
        var oldFrame=previousFrame;
        var oldSample=previousSample;
        previous=tile;previousFrame=capturedAt;previousSample=sample;
        confirmedFrames++;
        if(old is null)
            return $"result=FIRST_CONFIRMED sample={sample} "+
                $"x={tile.X} y={tile.Y} plane={tile.Plane} "+
                "source=RUNELITE_PLUGIN_API_EVENT autonomous_input=false";

        if(old==tile)
        {
            stableFrames++;
            return null;
        }

        changes++;
        int dx=tile.X-old.X,dy=tile.Y-old.Y;
        int manhattan=Math.Abs(dx)+Math.Abs(dy);
        int planeDelta=tile.Plane-old.Plane;
        bool nonAdjacentChange=manhattan>1||planeDelta!=0;
        if(nonAdjacentChange)nonAdjacent++;
        return $"result=CHANGED sample={sample} previous_sample={oldSample} "+
            $"from={old.X},{old.Y},{old.Plane} "+
            $"to={tile.X},{tile.Y},{tile.Plane} dx={dx} dy={dy} "+
            $"plane_delta={planeDelta} manhattan_tiles={manhattan} "+
            $"sample_gap={sample-oldSample} capture_gap_ms={(capturedAt-oldFrame).TotalMilliseconds:0} "+
            $"non_adjacent_observation={nonAdjacentChange.ToString().ToLowerInvariant()} "+
            "route_between_samples_unknown=true autonomous_input=false";
    }
}
