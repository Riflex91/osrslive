namespace OSRSLiveDiagnostics;

internal static class SurvivalRoutine
{
    internal static async Task<RoutineResult?> Heal(GameInteractions game,int maximum,int percent,CancellationToken ct,
        InventoryState? inventory=null)
    {
        int? hp=game.Driver.Current.Pixels?.Navigation?.Hitpoints;
        if(!CombatPolicy.Healthy(hp,maximum,1))return new(RoutineStatus.Blocked,"HEALING_REQUIRES_READABLE_LIVING_HEALTH");
        if(CombatPolicy.Healthy(hp,maximum,percent))return null;
        inventory??=await game.Inventory(ct);
        var food=inventory.Items.Where(i=>CombatPolicy.Food.Contains(i.Name)).ToArray();
        foreach(var bite in food.Take(10))
        {
            int before=game.Driver.Current.Pixels!.Navigation!.Hitpoints!.Value;
            await game.Choose(bite.Bounds,"Eat "+bite.Name,ct);
            await Task.Delay(1800,ct);await game.Driver.ObserveAsync(ct);
            hp=game.Driver.Current.Pixels?.Navigation?.Hitpoints;
            if(!CombatPolicy.Healthy(hp,maximum,1))return new(RoutineStatus.Blocked,"HEALTH_LOST_DURING_HEALING");
            if(hp<=before)return new(RoutineStatus.UnknownOutcome,"FOOD_HEALING_NOT_CONFIRMED");
            if(CombatPolicy.Healthy(hp,maximum,percent))return null;
        }
        return new(RoutineStatus.Blocked,"MORE_FOOD_REQUIRED_FOR_HEALTH_THRESHOLD");
    }
}
