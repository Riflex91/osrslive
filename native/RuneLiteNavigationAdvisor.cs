namespace OSRSLiveDiagnostics;

// Read-only first consumer of verified RuneLite API positions. This is NOT a
// walkable path, a minimap click, or permission to issue game actions. Real
// routes still require collision and transport checks in WorldMapData.
internal static class RuneLiteNavigationAdvisor
{
    internal sealed record Guidance(NavigationDestination Destination,
        WorldTile Current,int DeltaX,int DeltaY,int ChebyshevTiles,
        int ManhattanTiles,bool SamePlane,bool InsideGoalRadius)
    {
        internal string Display=>!SamePlane?
            $"Ziel: {Destination.Id} · andere Ebene, kein Weg berechnet":
            $"Ziel: {Destination.Id} · Luftlinie {ChebyshevTiles} Felder "+
            $"(ΔX {DeltaX:+#;-#;0}, ΔY {DeltaY:+#;-#;0}) "+
            (InsideGoalRadius?"· im Zielradius":"· Weg noch nicht geprüft");
        internal string Diagnostic=>$"target={Destination.Id.Replace(' ','_')} "+
            $"dx={DeltaX} dy={DeltaY} chebyshev_tiles={ChebyshevTiles} "+
            $"manhattan_tiles={ManhattanTiles} same_plane={SamePlane.ToString().ToLowerInvariant()} "+
            $"within_radius={InsideGoalRadius.ToString().ToLowerInvariant()} "+
            "collision_route_calculated=false game_input=false";
    }

    internal static Guidance? FromConfirmed(WorldTile? tile,NavigationDestination? destination)
    {
        if(tile is null||destination is null)return null;
        int dx=destination.Tile.X-tile.X,dy=destination.Tile.Y-tile.Y;
        bool samePlane=destination.Tile.Plane==tile.Plane;
        int chebyshev=Math.Max(Math.Abs(dx),Math.Abs(dy));
        return new(destination,tile,dx,dy,chebyshev,Math.Abs(dx)+Math.Abs(dy),
            samePlane,samePlane&&chebyshev<=destination.Radius);
    }

    internal static NavigationDestination[] AvailableDestinations()=>
        NavigationDestinations.All.Where(d=>WorldMapData.Domain(d.Tile))
            .OrderBy(d=>d.Id,StringComparer.CurrentCultureIgnoreCase).ToArray();
}
