namespace OSRSLiveDiagnostics;

internal sealed class TrainingRoutines
{
    private readonly GameInteractions game;
    internal TrainingRoutines(GameInteractions game){this.game=game;}
    internal async Task<RoutineResult> Run(TrainingSpec spec,IReadOnlyList<PublicOsrsSkill> skills,CancellationToken ct)
    {
        var method=F2PProgression.Methods.Single(m=>m.Id==spec.Id);
        if(!skills.Any(s=>s.Name==spec.Skill&&s.Level>=method.MinimumLevel&&s.Level<99))
            return new(RoutineStatus.Blocked,"TRAINING_LEVEL_REQUIRED_OR_ALREADY_MAXED");
        var before=await game.Inventory(ct);
        if(spec.Skill=="Runecraft"&&before.Count("rune essence")==0&&before.Count("pure essence") is >0)
            spec=spec with{Input="pure essence"};
        InventoryItem? Tool(string name)=>name switch
        {
            "axe"=>before.Items.FirstOrDefault(i=>(i.Name=="bronze axe"||F2PProgression.Axes.Any(t=>t.Item==i.Name&&
                skills.Any(s=>s.Name=="Woodcutting"&&s.Level>=t.UseLevel)))),
            "pickaxe"=>before.Items.FirstOrDefault(i=>(i.Name=="bronze pickaxe"||F2PProgression.Pickaxes.Any(t=>t.Item==i.Name&&
                skills.Any(s=>s.Name=="Mining"&&s.Level>=t.UseLevel)))),
            _=>before.Find(name)
        };
        if(spec.Tool is not null&&Tool(spec.Tool) is null)return new(RoutineStatus.Blocked,"TOOL_NOT_IN_VERIFIED_INVENTORY");
        if(spec.Input is not null&&(before.Count(spec.Input) is not int count||count<spec.InputPerOutput))
            return new(RoutineStatus.Blocked,"INPUT_MATERIAL_REQUIRED");
        if(spec.SecondInput is not null&&(before.Count(spec.SecondInput) is not int second||second<1))
            return new(RoutineStatus.Blocked,"SECOND_INPUT_REQUIRED");
        if(before.EmptySlots==0&&spec.Input is null)return new(RoutineStatus.Blocked,"BANK_PRODUCTS_REQUIRED");
        if(spec.ProductionChoice=="Bury")
            await game.Choose(before.Find(spec.Input!)!.Bounds,"Bury "+spec.Input,ct);
        else if(spec.Skill=="Firemaking")
            await game.UseItem(Tool("tinderbox")!,before.Find(spec.Input!)!.Bounds,spec.Input!,ct);
        else if(spec.Skill=="Crafting")
        {
            await game.UseItem(Tool("needle")!,before.Find(spec.Input!)!.Bounds,spec.Input!,ct);
            await StartProduction(spec.ProductionChoice!,ct);
        }
        else if(spec.Input is not null)
        {
            // Smithing uses a bar on the anvil; cooking uses fish on the range.
            // Smelting has its own exact Furnace menu action.
            var station=await game.FindObject(spec.ObjectAction!,spec.ObjectName!,ct);
            if(spec.ObjectAction is "Smelt" or "Craft-rune")await game.Choose(station.Bounds,station.Action+" "+station.Name,ct);
            else await game.UseItem(before.Find(spec.Input)!,station.Bounds,station.Name,ct);
            if(spec.ProductionChoice is not null)await StartProduction(spec.ProductionChoice,ct);
        }
        else
        {
            var target=await game.FindObject(spec.ObjectAction!,spec.ObjectName!,ct);
            await game.Choose(target.Bounds,target.Action+" "+target.Name,ct);
        }
        // A queued interaction is not a receipt. Re-read changed inventory
        // slots; names and quantities are confirmed through context menus.
        for(int attempt=0;attempt<3;attempt++)
        {
            await Task.Delay(700,ct);await game.Driver.ObserveAsync(ct);
            var after=await game.Inventory(ct);
            var result=Verify(spec,before,after);
            if(result.Status==RoutineStatus.Completed)return result;
        }
        return new(RoutineStatus.UnknownOutcome,"NO_VERIFIED_TRAINING_RESULT_DO_NOT_REPEAT_BLINDLY");
    }
    internal async Task StartProduction(string product,CancellationToken ct)
    {
        if(!ExecutionUi.Production(game.Driver.Current.Labels))
            throw new RoutineBlockedException("PRODUCTION_DIALOG_NOT_VERIFIED");
        var label=ExecutionUi.Unique(game.Driver.Current.Labels,product);
        if(label is null&&product.EndsWith("platebody",StringComparison.OrdinalIgnoreCase))
            label=ExecutionUi.Unique(game.Driver.Current.Labels,"Plate body")??ExecutionUi.Unique(game.Driver.Current.Labels,"Platebody");
        if(label is null)throw new RoutineBlockedException("EXACT_PRODUCT_NOT_IDENTIFIED");
        // Prefer a single output. Some production interfaces expose the action
        // by a space, others by a hyphen; never accept a generic unrelated Make.
        var menu=await game.OpenMenu(label.Bounds,ct);
        var entry=menu.Exact("Make-1 "+product)??menu.Exact("Make 1 "+product)??
            menu.Exact("Cook-1 "+product)??menu.Exact("Cook 1 "+product)??menu.Exact("Make 1")??menu.Exact("Make-1")??
            menu.Exact("Cook 1")??menu.Exact("Cook-1");
        if(entry is null){await game.Escape(ct);throw new RoutineBlockedException("MAKE_ONE_ACTION_NOT_OBSERVED");}
        await game.Click(entry.Bounds,ct);
    }
    internal static RoutineResult Verify(TrainingSpec spec,InventoryState before,InventoryState after)
    {
        if(after.ObservedUtc<=before.ObservedUtc)return new(RoutineStatus.Waiting,"INDEPENDENT_RESULT_FRAME_REQUIRED");
        if(spec.Input is null)
        {
            string[] products=spec.Skill=="Mining"?new[]{"copper ore","tin ore","iron ore","silver ore","coal","gold ore","mithril ore","adamantite ore","runite ore"}:
                spec.Id=="NET_SHRIMPS"?new[]{"raw shrimps","raw anchovies"}:
                spec.Id=="FLY_FISH"?new[]{"raw trout","raw salmon"}:new[]{spec.Product};
            int gain=0;
            foreach(string product in products)
            {
                if(before.Count(product) is not int b||after.Count(product) is not int a)
                    return new(RoutineStatus.Waiting,"EXACT_OUTPUT_COUNT_REQUIRED");
                gain+=a-b;
            }
            return gain>0?new(RoutineStatus.Completed,spec.Skill=="Mining"?"ACTUAL_ORE_GAIN_OBSERVED_TYPE_NOT_ASSUMED":"RESOURCE_GAIN_OBSERVED",gain):
                new(RoutineStatus.Waiting,"NO_RESOURCE_GAIN_YET");
        }
        if(before.Count(spec.Input) is not int initial||after.Count(spec.Input) is not int remaining)
            return new(RoutineStatus.Waiting,"EXACT_INPUT_COUNT_REQUIRED");
        int used=initial-remaining;
        if(spec.Skill=="Runecraft")
        {
            var runes=new[]{"air rune","mind rune","water rune","earth rune","fire rune","body rune"};
            int gain=0;
            foreach(string rune in runes)
            {
                if(before.Count(rune) is not int b||after.Count(rune) is not int a)return new(RoutineStatus.Waiting,"EXACT_RUNE_OUTPUT_REQUIRED");
                gain+=a-b;
            }
            return used>0&&gain>=used?new(RoutineStatus.Completed,"ACTUAL_RUNE_GAIN_AND_ESSENCE_CONSUMPTION_OBSERVED",gain):
                new(RoutineStatus.Waiting,"RUNE_YIELD_NOT_CONFIRMED");
        }
        if(spec.ProductionChoice=="Bury"||spec.Skill=="Firemaking")
            return used==1?new(RoutineStatus.Completed,"EXACT_SELECTED_ITEM_CONSUMPTION_OBSERVED",1):
                new(RoutineStatus.Waiting,"EXPECTED_SINGLE_ITEM_CONSUMPTION_MISSING");
        if(before.Count(spec.Product) is not int oldProduct||after.Count(spec.Product) is not int newProduct)
            return new(RoutineStatus.Waiting,"EXACT_PRODUCT_COUNT_REQUIRED");
        int made=newProduct-oldProduct;
        return made>0&&used==made*spec.InputPerOutput?new(RoutineStatus.Completed,"INPUT_AND_PRODUCT_DELTA_MATCH",made):
            new(RoutineStatus.Waiting,"INPUT_AND_PRODUCT_DELTA_NOT_CONFIRMED");
    }
    internal async Task<RoutineResult> Craft(Recipe recipe,int quantity,IReadOnlyList<PublicOsrsSkill> skills,CancellationToken ct)
    {
        if(quantity!=1)return new(RoutineStatus.Blocked,"SINGLE_RECIPE_STEP_REQUIRED");
        if(!skills.Any(s=>s.Name==recipe.Skill&&s.Level>=recipe.Level))return new(RoutineStatus.Blocked,"RECIPE_LEVEL_REQUIRED");
        var before=await game.Inventory(ct);
        if(recipe.Inputs.Any(i=>before.Count(i.Name) is not int count||count<i.Quantity)||recipe.Tools.Any(t=>before.Find(t.Name) is null))
            return new(RoutineStatus.Blocked,"COMPLETE_RECIPE_INPUTS_AND_TOOLS_REQUIRED");
        if(recipe.Station=="inventory"&&recipe.Inputs.Count==2)
            await game.UseItem(before.Find(recipe.Inputs[1].Name)!,before.Find(recipe.Inputs[0].Name)!.Bounds,recipe.Inputs[0].Name,ct);
        else if(recipe.Station is "furnace" or "anvil")
        {
            string verb=recipe.Station=="furnace"?"Smelt":"Smith",stationName=recipe.Station=="furnace"?"Furnace":"Anvil";
            var station=await game.FindObject(verb,stationName,ct);
            if(recipe.Station=="anvil")await game.UseItem(before.Find(recipe.Inputs[0].Name)!,station.Bounds,station.Name,ct);
            else await game.Choose(station.Bounds,station.Action+" "+station.Name,ct);
            await StartProduction(recipe.Product,ct);
        }
        else if(recipe.Station=="Lvl-4 Enchant")
        {
            var magic=await game.FindControl("Magic",new Rectangle(755,260,240,335),ct);
            await game.Click(magic,ct);
            var spell=await game.FindControl("Lvl-4 Enchant",new Rectangle(775,290,220,280),ct);
            await game.Click(spell,ct);
            // Selecting an item spell should expose inventory. The exact Cast
            // menu binds the selected spell to the original named ingredient.
            var ingredient=before.Find("diamond amulet")!;
            await game.Choose(ingredient.Bounds,"Cast Lvl-4 Enchant -> diamond amulet",ct);
        }
        else return new(RoutineStatus.Blocked,"SPELL_OR_STATION_RUNNER_UNSUPPORTED");
        await Task.Delay(700,ct);await game.Driver.ObserveAsync(ct);
        var after=await game.Inventory(ct);
        return VerifyRecipe(recipe,before,after);
    }
    internal static RoutineResult VerifyRecipe(Recipe recipe,InventoryState before,InventoryState after)
    {
        if(after.ObservedUtc<=before.ObservedUtc)return new(RoutineStatus.Waiting,"INDEPENDENT_RESULT_FRAME_REQUIRED");
        if(before.Count(recipe.Product) is not int b||after.Count(recipe.Product) is not int a||a-b!=recipe.Quantity)
            return new(RoutineStatus.UnknownOutcome,"RECIPE_PRODUCT_NOT_CONFIRMED");
        foreach(var input in recipe.Inputs)
            if(before.Count(input.Name) is not int initial||after.Count(input.Name) is not int remaining||initial-remaining!=input.Quantity)
                return new(RoutineStatus.UnknownOutcome,"RECIPE_INPUT_DELTA_MISMATCH");
        return new(RoutineStatus.Completed,"ALL_RECIPE_INPUT_AND_OUTPUT_DELTAS_MATCH",recipe.Quantity);
    }
}
