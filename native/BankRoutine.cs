namespace OSRSLiveDiagnostics;

internal sealed class BankRoutine
{
    private readonly GameInteractions game;
    internal BankRoutine(GameInteractions game){this.game=game;}
    internal async Task Open(CancellationToken ct)
    {
        if(ExecutionUi.Bank(game.Driver.Current.Labels))return;
        var target=await game.FindObject("Bank","Bank booth",ct);
        await game.Choose(target.Bounds,target.Action+" "+target.Name,ct);
        if(!ExecutionUi.Bank(game.Driver.Current.Labels))throw new RoutineBlockedException("BANK_NOT_OBSERVED");
    }
    internal async Task<RoutineResult> Deposit(IReadOnlySet<string> products,CancellationToken ct)
    {
        var before=await game.Inventory(ct);
        var selected=before.Items.Where(i=>products.Contains(i.Name)).Select(i=>i.Name).Distinct().ToArray();
        if(selected.Length==0)return new(RoutineStatus.Completed,"NOTHING_TO_DEPOSIT");
        await Open(ct);
        foreach(string product in selected)
        {
            var current=await game.Inventory(ct,false);
            var item=current.Find(product);
            if(item is null)continue;
            if(!ExecutionUi.Bank(game.Driver.Current.Labels))throw new RoutineBlockedException("BANK_CLOSED_DURING_DEPOSIT");
            await game.Choose(item.Bounds,"Deposit-All "+product,ct);
        }
        var after=await game.Inventory(ct,false);
        if(selected.Any(p=>after.Count(p)!=0))return new(RoutineStatus.UnknownOutcome,"DEPOSIT_NOT_CONFIRMED");
        await game.Escape(ct);
        return new(RoutineStatus.Completed,"SELECTED_PRODUCTS_DEPOSITED_TO_BANK");
    }
    internal async Task<RoutineResult> Withdraw(string item,int quantity,CancellationToken ct)
    {
        if(quantity is <1 or >28)return new(RoutineStatus.Blocked,"BOUNDED_WITHDRAWAL_REQUIRED");
        var before=await game.Inventory(ct);
        await Open(ct);
        var search=await game.FindControl("Search",new Rectangle(60,320,675,140),ct);
        await game.Click(search,ct);
        if(!ExecutionUi.BankSearchPrompt(game.Driver.Current.Labels))return new(RoutineStatus.Blocked,"BANK_SEARCH_INPUT_NOT_OBSERVED");
        await game.Driver.InputAsync(new(GameInputKind.Text,new(10,440,480,130),item,false),ct);
        if(ExecutionUi.Bank(game.Driver.Current.Labels)&&ExecutionUi.Unique(game.Driver.Current.Labels,"No items found",new Rectangle(0,20,745,410)) is not null)
        {
            await game.Escape(ct);
            return new(RoutineStatus.Blocked,"VERIFIED_BANK_SEARCH_EMPTY");
        }
        var title=ExecutionUi.Unique(game.Driver.Current.Labels,"The Bank of Gielinor")??
            throw new RoutineBlockedException("BANK_TITLE_NOT_UNAMBIGUOUS");
        Rectangle? target=null;
        int left=title.Bounds.Left+title.Bounds.Width/2-195;
        for(int row=0;row<8&&target is null;row++)for(int col=0;col<8;col++)
        {
            var box=new Rectangle(left+col*48,title.Bounds.Bottom+55+row*36,5,5);
            if(!new Rectangle(0,0,750,430).Contains(box))continue;
            await game.Driver.InputAsync(new(GameInputKind.Hover,box),ct);
            if(!game.Driver.Current.Labels.Any(l=>l.Bounds.Top<45&&l.Text.StartsWith("Withdraw-",StringComparison.OrdinalIgnoreCase)&&
                l.Text.Contains(item,StringComparison.OrdinalIgnoreCase)))continue;
            var menu=await game.OpenMenu(box,ct);
            bool exact=menu.Exact("Withdraw-X "+item) is not null;
            await game.Escape(ct);if(exact){target=box;break;}
        }
        if(target is null)return new(RoutineStatus.Blocked,"ITEM_NOT_FOUND_ON_VISIBLE_BANK_PAGE_NOT_PROOF_OF_ZERO_BANK_STOCK");
        await game.Choose(target.Value,"Withdraw-X "+item,ct);
        await game.Amount(quantity,ct);
        var after=await game.Inventory(ct,false);
        if(before.Count(item) is not int b||after.Count(item) is not int a||a-b!=quantity)
            return new(RoutineStatus.UnknownOutcome,"WITHDRAWAL_PARTIAL_OR_NOT_CONFIRMED");
        await game.Escape(ct);
        return new(RoutineStatus.Completed,"EXACT_WITHDRAWAL_OBSERVED",quantity);
    }
}
