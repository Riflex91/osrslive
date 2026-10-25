namespace OSRSLiveDiagnostics;

internal sealed record AgentActivity(string Id,string Skill,int Level,bool Supplied,
    TrainingSpec? Training=null,CombatSpec? Combat=null);

// Session-local scheduling only. Pending transactions and unknown outcomes
// are handled by the coordinator before any new activity may be selected.
internal sealed class ActivityScheduler
{
    private sealed record Deferral(int Failures,DateTimeOffset RetryUtc);
    private readonly Dictionary<string,Deferral> deferred=new(StringComparer.Ordinal);
    private string? active;
    private bool unknownOutcome;

    internal void Reset(){deferred.Clear();active=null;unknownOutcome=false;}
    internal bool Available(string id,DateTimeOffset now)=>
        !unknownOutcome&&(!deferred.TryGetValue(id,out var d)||now>=d.RetryUtc);
    internal void ReconcileJournal(RoutineResult result)
    {
        if(result.Status!=RoutineStatus.Completed)return;
        if(active is not null)deferred.Remove(active);
        active=null;unknownOutcome=false;
    }

    internal static AgentActivity[] Candidates(IReadOnlyList<PublicOsrsSkill> skills,
        InventoryState inventory,bool combatEnabled,bool allowAcquisition=true)
    {
        var training=ExecutionCatalog.Training.Where(spec=>F2PProgression.Methods.Any(m=>m.Id==spec.Id&&
                skills.Any(s=>s.Name==m.Skill&&s.Level>=m.MinimumLevel&&s.Level<m.NextLevel)))
            .Select(spec=>new AgentActivity(spec.Id,spec.Skill,skills.Single(s=>s.Name==spec.Skill).Level,
                GameplayCoordinator.Supplied(spec,inventory,skills),Training:spec));
        var combat=CombatPolicy.Specs.Where(spec=>combatEnabled&&
                skills.Any(s=>s.Name==spec.Skill&&s.Level<99))
            .Select(spec=>new AgentActivity(spec.Id,spec.Skill,skills.Single(s=>s.Name==spec.Skill).Level,
                CombatPolicy.Supply(spec,inventory) is null,Combat:spec));
        return training.Concat(combat).Where(c=>allowAcquisition||c.Supplied).ToArray();
    }

    internal AgentActivity? Select(IReadOnlyList<AgentActivity> candidates,DateTimeOffset now)
    {
        if(unknownOutcome)return null;
        var available=candidates.Where(c=>Available(c.Id,now)).ToArray();
        // Finish a verified journey/supply chain before reconsidering priority.
        // A transient safety/readability block must not trigger another task.
        var continuing=available.FirstOrDefault(c=>c.Id==active);
        if(continuing is not null)return continuing;
        var next=available.OrderByDescending(c=>c.Supplied).ThenBy(c=>c.Level)
            .ThenBy(c=>Array.IndexOf(PublicOsrsHiscores.OrderedSkills,c.Skill))
            .ThenBy(c=>c.Id,StringComparer.Ordinal).FirstOrDefault();
        active=next?.Id;
        return next;
    }

    internal DateTimeOffset? Record(string id,RoutineResult result,DateTimeOffset now)
    {
        if(result.Status==RoutineStatus.UnknownOutcome){unknownOutcome=true;return null;}
        if(result.Status==RoutineStatus.Completed)
        {deferred.Remove(id);if(active==id)active=null;return null;}
        if(result.Status!=RoutineStatus.Blocked||!CanDefer(result.Reason))return null;
        int failures=deferred.TryGetValue(id,out var previous)?Math.Min(previous.Failures+1,5):1;
        var retry=now.AddSeconds(Math.Min(300,30*(1<<(failures-1))));
        deferred[id]=new(failures,retry);
        if(active==id)active=null;
        return retry;
    }

    // These failures establish a missing prerequisite or absent destination
    // before a productive action. Health, identity, OCR, transport errors and
    // unknown post-action results are deliberately excluded.
    private static bool CanDefer(string reason)=>reason is
        "VERIFIED_BANK_SEARCH_EMPTY" or "COIN_RESERVE_OR_OFFER_CAP" or
        "FRESH_F2P_PRICE_AND_BUY_LIMIT_REQUIRED" or "F2P_GE_SLOT_REQUIRED" or
        "TOOL_NOT_IN_VERIFIED_INVENTORY" or "INPUT_MATERIAL_REQUIRED" or
        "SECOND_INPUT_REQUIRED" or "NO_WALKABLE_F2P_PATH_FOUND" or
        "REQUIRED_OBJECT_NOT_IDENTIFIED_IN_VISIBLE_SCENE";
}
