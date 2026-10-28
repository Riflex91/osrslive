namespace OSRSLiveDiagnostics;

// A read-only route coach: the player walks manually. The next step always
// comes from the pinned collision map rather than an inferred straight line.
internal static class RuneLiteRouteCoach
{
    internal sealed record Hint(string Display,string Diagnostic,PathStep? Next,
        int RemainingSteps,bool Arrived);

    internal static Hint FromPath(WorldTile current,NavigationDestination goal,
        PathStep[] path)
    {
        if(path.Length==0)
            return new($"Route: {goal.Id} · Zielradius erreicht!",
                $"target={goal.Id.Replace(' ','_')} result=ARRIVED remaining_steps=0 "+
                "collision_path_verified=true game_input=false",null,0,true);
        var next=path[0];
        int dx=next.Tile.X-current.X,dy=next.Tile.Y-current.Y;
        string direction=next.Transport is not null?
            $"{next.Transport.Action} {next.Transport.Object}":
            (dx,dy) switch
            {
                (0,1)=>"Norden",(0,-1)=>"Süden",(1,0)=>"Osten",(-1,0)=>"Westen",
                (1,1)=>"Nordosten",(1,-1)=>"Südosten",
                (-1,1)=>"Nordwesten",(-1,-1)=>"Südwesten",
                _=>"Sonderübergang"
            };
        return new($"Route: {goal.Id} · {path.Length} Schritte · "+
                $"nächste Kachel {next.Tile.X},{next.Tile.Y},{next.Tile.Plane} ({direction})",
            $"target={goal.Id.Replace(' ','_')} result=ROUTE_READY remaining_steps={path.Length} "+
                $"next_x={next.Tile.X} next_y={next.Tile.Y} next_plane={next.Tile.Plane} "+
                $"transport={(next.Transport is not null).ToString().ToLowerInvariant()} "+
                "collision_path_verified=true game_input=false",next,path.Length,false);
    }
}
