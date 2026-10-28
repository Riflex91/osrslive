namespace OSRSLiveDiagnostics;

// Purely observational route progress from the local RuneLite API. It never
// invents intermediate traversed tiles or actuates the game. The current tile
// is checked against the previous complete path before the new path replaces it.
internal sealed class RuneLiteRouteProgress
{
    internal sealed record Assessment(string Status,int? StepDelta,bool? OnPriorRoute,
        string Display,string Diagnostic);

    private string? goalId;
    private WorldTile? previousTile;
    private DateTimeOffset previousEvent;
    private int previousRemaining;
    private HashSet<WorldTile> previousPath=new();

    internal void Reset()
    {
        goalId=null;previousTile=null;previousEvent=default;
        previousRemaining=0;previousPath.Clear();
    }

    internal Assessment Observe(WorldTile current,NavigationDestination goal,
        PathStep[] path,DateTimeOffset eventAt)
    {
        if(goalId!=goal.Id)Reset();
        if(eventAt==default)throw new ArgumentException("API event timestamp required",nameof(eventAt));
        if(previousTile is not null&&eventAt<previousEvent)
            throw new InvalidOperationException("Route-progress API events must be monotonic");
        bool baseline=previousTile is null;
        bool? onPrior=baseline?null:previousPath.Contains(current);
        int? delta=baseline?null:path.Length-previousRemaining;
        bool insideGoal=current.Distance(goal.Tile)<=goal.Radius;
        // The pathfinder may return an empty route only when already inside
        // the requested goal radius. Never claim arrival from an arbitrary
        // empty array or a different plane.
        bool arrived=insideGoal&&path.Length==0;
        bool leftGoal=!baseline&&previousRemaining==0&&!insideGoal;
        string status=arrived?"ARRIVED":
            baseline?"BASELINE":
            leftGoal?"LEFT_GOAL":
            onPrior==true?"ON_PREVIOUS_ROUTE":"OFF_PREVIOUS_ROUTE";
        string trend=delta is null?"Start":
            delta<0?$"{-delta} Schritte näher":
            delta>0?$"{delta} Schritte weiter":"Reststrecke unverändert";
        string placement=baseline?"Startpunkt":onPrior==true?
            "auf vorheriger Route":"von vorheriger Route abgewichen";
        string display=arrived?"Routenverlauf: Zielradius erreicht!":
            $"Routenverlauf: {placement} · {trend}";
        string diagnostic=$"result={status} target={goal.Id.Replace(' ','_')} "+
            $"remaining_steps={path.Length} "+
            $"previous_remaining={(delta is null?"none":previousRemaining.ToString())} "+
            $"step_delta={(delta is null?"none":delta.Value.ToString())} "+
            $"on_previous_route={(onPrior is null?"unknown":onPrior.Value.ToString().ToLowerInvariant())} "+
            $"x={current.X} y={current.Y} plane={current.Plane} "+
            $"event_at_utc={eventAt.ToUniversalTime():O} "+
            "path_recalculated=true traversed_tiles_not_inferred=true game_input=false";
        goalId=goal.Id;previousTile=current;previousEvent=eventAt;
        previousRemaining=path.Length;
        previousPath=path.Select(p=>p.Tile).ToHashSet();
        return new(status,delta,onPrior,display,diagnostic);
    }
}
