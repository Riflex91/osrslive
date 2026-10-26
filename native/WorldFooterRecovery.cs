namespace OSRSLiveDiagnostics;

// Recover tiny footer OCR from new captures only. This helper never sends
// input and never substitutes a ping recommendation for the visible world.
internal static class WorldFooterRecovery
{
    internal static async Task<VisualFrame> Reobserve(VisualFrame initial,int? preferredWorld,
        Func<CancellationToken,Task<VisualFrame?>> capture,CancellationToken ct,
        Func<int,CancellationToken,Task>? wait=null,Action<string>? diagnostic=null)
    {
        wait??=Task.Delay;
        var current=initial;
        for(int attempt=1;attempt<=2&&
            VisualStartupPolicy.Decide(current.Labels,preferredWorld).State=="WORLD_LABEL_NOT_RECOGNIZED";attempt++)
        {
            diagnostic?.Invoke($"attempt={attempt} recommended_world={preferredWorld} applied_world_verified=false passive_only=true");
            await wait(200,ct);
            var next=await capture(ct);
            if(next is null)continue;
            if(next.Window!=initial.Window||next.ScreenBounds!=initial.ScreenBounds)
                break; // Retain the original non-actionable observation.
            if(next.CapturedUtc<=current.CapturedUtc)continue;
            current=next;
        }
        return current;
    }
}
