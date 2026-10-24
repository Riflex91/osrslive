using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal sealed record GeField(long Value,Rectangle Bounds);
internal sealed record GeInvoice(string Item,int Quantity,long UnitPrice,long Total,bool Sell,Rectangle Confirm);

internal static class GeReader
{
    internal static GeField? Field(IReadOnlyList<VisualLabel> labels,string caption)
    {
        var full=labels.Where(l=>l.Kind==VisualLabelKind.Text&&Regex.IsMatch(l.Text.Trim(),"^"+Regex.Escape(caption)+@"\s*:?\s*[\d,]+(?:\s+coins)?$",RegexOptions.IgnoreCase)).ToArray();
        if(full.Length==1)
        {
            var match=Regex.Match(full[0].Text,@"([\d,]+)(?:\s+coins)?$");
            if(long.TryParse(match.Groups[1].Value.Replace(",",""),out long n))return new(n,full[0].Bounds);
        }
        var header=ExecutionUi.Unique(labels,caption)??ExecutionUi.Unique(labels,caption+":");
        if(header is null)return null;
        var values=labels.Where(l=>l.Kind==VisualLabelKind.Text&&l.Bounds.Top>=header.Bounds.Bottom&&
            l.Bounds.Top<header.Bounds.Bottom+55&&Math.Abs(l.Bounds.Left-header.Bounds.Left)<80&&
            Regex.IsMatch(l.Text.Trim(),@"^[\d,]+(?:\s+coins)?$",RegexOptions.IgnoreCase)).ToArray();
        if(values.Length!=1)return null;
        string text=Regex.Match(values[0].Text,@"[\d,]+").Value.Replace(",","");
        return long.TryParse(text,out long result)?new(result,values[0].Bounds):null;
    }
    internal static GeInvoice? Invoice(IReadOnlyList<VisualLabel> labels,string expected)
    {
        if(!ExecutionUi.Ge(labels))return null;
        bool buy=ExecutionUi.Unique(labels,"Buy offer") is not null,sell=ExecutionUi.Unique(labels,"Sell offer") is not null;
        if(buy==sell||ExecutionUi.Unique(labels,expected) is null)return null;
        var quantity=Field(labels,"Quantity");var price=Field(labels,"Price per item");var total=Field(labels,"Total price");
        var confirm=ExecutionUi.Unique(labels,"Confirm");
        if(quantity is null||price is null||total is null||confirm is null||quantity.Value is <1 or >int.MaxValue)return null;
        return new(expected,(int)quantity.Value,price.Value,total.Value,sell,confirm.Bounds);
    }
    internal static bool Matches(GeInvoice invoice,TradeOrder order)
    {
        try{return invoice.Item.Equals(order.Item,StringComparison.OrdinalIgnoreCase)&&invoice.Quantity==order.Quantity&&
            invoice.UnitPrice==order.UnitPrice&&invoice.Total==checked(order.Quantity*order.UnitPrice)&&invoice.Sell==order.Sell;}
        catch(OverflowException){return false;}
    }
    internal static bool DetailMatches(IReadOnlyList<VisualLabel> labels,TradeOrder order)=>ExecutionUi.Ge(labels)&&
        ExecutionUi.Unique(labels,order.Item) is not null&&
        ExecutionUi.Unique(labels,order.Sell?"Sell offer":"Buy offer") is not null&&
        Field(labels,"Quantity")?.Value==order.Quantity&&Field(labels,"Price per item")?.Value==order.UnitPrice&&
        ExecutionUi.Unique(labels,"Confirm") is null;
}

internal sealed record PendingTrade(TradeOrder Order,InventoryState Before,string SubjectHash,Rectangle SlotAnchor);
internal sealed class TradeJournal
{
    private readonly string path;
    internal readonly string SubjectHash;
    internal TradeJournal(string path,string subjectHash="test"){this.path=path;SubjectHash=subjectHash;}
    internal PendingTrade? Pending()
    {
        if(!File.Exists(path))return null;
        // A corrupt journal must block trading, not silently permit a duplicate.
        return JsonSerializer.Deserialize<PendingTrade>(File.ReadAllText(path))??throw new RoutineBlockedException("TRADE_JOURNAL_INVALID");
    }
    internal void Prepare(TradeOrder order,InventoryState before,Rectangle slotAnchor)
    {
        if(Pending() is not null)throw new RoutineBlockedException("PREVIOUS_TRADE_REQUIRES_RECONCILIATION");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(new PendingTrade(order,before,SubjectHash,slotAnchor)));
        File.Move(temporary,path,false);
    }
    internal void Complete(string id)
    {
        if(Pending()?.Order.Id!=id)throw new RoutineBlockedException("TRADE_JOURNAL_ID_MISMATCH");
        File.Delete(path);
    }
}

internal sealed class TradeRoutine
{
    private readonly GameInteractions game;
    private readonly TradeJournal journal;
    internal TradeRoutine(GameInteractions game,TradeJournal journal){this.game=game;this.journal=journal;}
    internal static string? Validate(TradeOrder order,ProgressionFacts facts,InventoryState inventory,DateTimeOffset now,bool requireSlot=true)
    {
        if(order.Quantity is <1 or >1000||order.UnitPrice is <1 or >999999999||order.CoinReserve<0)
            return "ORDER_LIMIT_EXCEEDED";
        if(order.PriceObservedUtc>now||now-order.PriceObservedUtc>TimeSpan.FromSeconds(60))return "PRICE_STALE";
        if(facts.Ironman!=false)return "NORMAL_ACCOUNT_MODE_REQUIRED";
        if(requireSlot&&facts.FreeGeSlots is not (>=1 and <=3))return "F2P_GE_SLOT_REQUIRED";
        if(order.Sell)
        {
            if(inventory.Count(order.Item) is not int stock||stock<order.Quantity)return "SELL_STOCK_NOT_VERIFIED";
            if(RestrictedItems.Contains(order.Item)&&F2PProgression.RestrictedSales(facts)!="UNRESTRICTED")
                return "RESTRICTED_F2P_SALE_NOT_ALLOWED";
        }
        else
        {
            if(inventory.Count("coins") is not int coins)return "EXACT_INVENTORY_COIN_BALANCE_REQUIRED";
            try
            {
                long total=checked(order.Quantity*order.UnitPrice);
                if(coins-order.CoinReserve<total)return "COIN_RESERVE_OR_OFFER_CAP";
            }
            catch(OverflowException){return "ORDER_OVERFLOW";}
        }
        return null;
    }
    // Account restriction list is explicit. Unknown account age never releases
    // a restricted resource sale; ordinary unrestricted manufactured items can
    // still be traded on a normal account.
    private static readonly IReadOnlySet<string> RestrictedItems=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "oak logs","willow logs","yew logs","raw shrimps","shrimps","raw anchovies","anchovies","raw lobster","lobster",
        "clay","soft clay","copper ore","tin ore","iron ore","silver ore","gold ore","coal","mithril ore","adamantite ore",
        "runite ore","ashes","vial","vial of water","jug of water","fishing bait","feather","eye of newt","wine of zamorak",
        "air rune","water rune","earth rune","fire rune","mind rune","chaos rune","cowhide","leather","red spiders' eggs",
        "hard leather","steel bar","redberries"
    };
    internal async Task<RoutineResult> Run(TradeOrder order,ProgressionFacts facts,CancellationToken ct)
    {
        if(journal.Pending() is not null)return new(RoutineStatus.UnknownOutcome,"PREVIOUS_TRADE_REQUIRES_RECONCILIATION_NO_DUPLICATE_SUBMISSION");
        var before=await game.Inventory(ct);
        string? blocked=Validate(order,facts,before,DateTimeOffset.UtcNow,false);
        if(blocked is not null)return new(RoutineStatus.Blocked,blocked);
        if(!ExecutionUi.Ge(game.Driver.Current.Labels))
        {
            var clerk=await game.FindObject("Exchange","Grand Exchange clerk",ct);
            await game.Choose(clerk.Bounds,clerk.Action+" "+clerk.Name,ct);
        }
        if(!ExecutionUi.Ge(game.Driver.Current.Labels))return new(RoutineStatus.Blocked,"GE_NOT_OBSERVED");
        var control=await game.FindControl(order.Sell?"Sell":"Buy",new Rectangle(150,35,590,350),ct);
        facts=facts with{FreeGeSlots=1}; // this specific visible empty offer control
        await game.Click(control,ct);
        if(order.Sell)
            await game.Choose(before.Find(order.Item)!.Bounds,"Offer "+order.Item,ct);
        else
        {
            if(!ExecutionUi.ItemSearchPrompt(game.Driver.Current.Labels))return new(RoutineStatus.Blocked,"GE_SEARCH_PROMPT_REQUIRED");
            await game.Driver.InputAsync(new(GameInputKind.Text,new(10,440,480,130),order.Item,false),ct);
            var item=ExecutionUi.Unique(game.Driver.Current.Labels,order.Item,new Rectangle(0,420,745,180));
            if(item is null)return new(RoutineStatus.Blocked,"EXACT_GE_SEARCH_RESULT_NOT_UNAMBIGUOUS");
            await game.Click(item.Bounds,ct);
        }
        await SetField("Quantity",order.Quantity,ct);
        await SetField("Price per item",checked((int)order.UnitPrice),ct);
        var invoice=GeReader.Invoice(game.Driver.Current.Labels,order.Item);
        if(invoice is null||!GeReader.Matches(invoice,order))return new(RoutineStatus.Blocked,"GE_INVOICE_ITEM_QUANTITY_PRICE_TOTAL_MISMATCH");
        var current=await game.Inventory(ct,false);
        blocked=Validate(order,facts,current,DateTimeOffset.UtcNow);
        if(blocked is not null)return new(RoutineStatus.Blocked,blocked);
        // Inventory inspection dismisses menus; re-read the invoice immediately
        // before committing. Write intent durably before the one Confirm click.
        invoice=GeReader.Invoice(game.Driver.Current.Labels,order.Item);
        if(invoice is null||!GeReader.Matches(invoice,order))return new(RoutineStatus.Blocked,"GE_INVOICE_CHANGED_BEFORE_CONFIRM");
        journal.Prepare(order,before,control);
        await game.Click(invoice.Confirm,ct);
        return await Collect(order,before,control,ct);
    }
    internal async Task<RoutineResult> Resume(CancellationToken ct)
    {
        var pending=journal.Pending()??throw new RoutineBlockedException("NO_PENDING_TRADE");
        if(pending.SubjectHash!=journal.SubjectHash)return new(RoutineStatus.Blocked,"PENDING_TRADE_BELONGS_TO_ANOTHER_CHARACTER");
        if(!ExecutionUi.Ge(game.Driver.Current.Labels))
        {
            var clerk=await game.FindObject("Exchange","Grand Exchange clerk",ct);
            await game.Choose(clerk.Bounds,clerk.Action+" "+clerk.Name,ct);
        }
        return await Collect(pending.Order,pending.Before,pending.SlotAnchor,ct);
    }
    private async Task SetField(string caption,int value,CancellationToken ct)
    {
        var field=GeReader.Field(game.Driver.Current.Labels,caption)??throw new RoutineBlockedException("GE_FIELD_NOT_READABLE");
        if(field.Value==value)return;
        await game.Click(field.Bounds,ct);await game.Amount(value,ct);
        var updated=GeReader.Field(game.Driver.Current.Labels,caption);
        if(updated?.Value!=value)throw new RoutineBlockedException("GE_FIELD_UPDATE_NOT_CONFIRMED");
    }
    private async Task<RoutineResult> Collect(TradeOrder order,InventoryState before,Rectangle slotAnchor,CancellationToken ct)
    {
        for(int i=0;i<6;i++)
        {
            if(!ExecutionUi.Ge(game.Driver.Current.Labels))return new(RoutineStatus.UnknownOutcome,"GE_CLOSED_AFTER_SUBMISSION_JOURNAL_RETAINED");
            if(!GeReader.DetailMatches(game.Driver.Current.Labels,order))
            {
                await game.Click(slotAnchor,ct);
                if(!GeReader.DetailMatches(game.Driver.Current.Labels,order))
                    return new(RoutineStatus.UnknownOutcome,"EXACT_OFFER_SLOT_DETAILS_NOT_VERIFIED_JOURNAL_RETAINED");
            }
            var collect=ExecutionUi.Unique(game.Driver.Current.Labels,"Collect");
            if(collect is not null)
            {
                await game.Click(collect.Bounds,ct);
                var after=await game.Inventory(ct,false);
                var result=VerifyReceipt(order,before,after);
                if(result.Status==RoutineStatus.Completed)journal.Complete(order.Id);
                return result;
            }
            await Task.Delay(1000,ct);await game.Driver.ObserveAsync(ct);
        }
        return new(RoutineStatus.UnknownOutcome,"OFFER_PENDING_OR_PARTIAL_JOURNAL_RETAINED_NO_RESUBMISSION");
    }
    internal static RoutineResult VerifyReceipt(TradeOrder order,InventoryState before,InventoryState after)
    {
        if(after.ObservedUtc<=before.ObservedUtc)return new(RoutineStatus.UnknownOutcome,"FRESH_RECEIPT_REQUIRED");
        if(before.Count(order.Item) is not int oldItems||after.Count(order.Item) is not int newItems||
            before.Count("coins") is not int oldCoins||after.Count("coins") is not int newCoins)
            return new(RoutineStatus.UnknownOutcome,"EXACT_ITEM_AND_COIN_RECEIPT_REQUIRED");
        if(!order.Sell&&newItems-oldItems==order.Quantity&&oldCoins-newCoins>0&&oldCoins-newCoins<=order.Quantity*order.UnitPrice&&
            newCoins>=order.CoinReserve)return new(RoutineStatus.Completed,"GE_PURCHASE_AND_COIN_DELTA_VERIFIED",order.Quantity);
        long minimumAfterTax=(order.UnitPrice-order.UnitPrice*2/100)*order.Quantity;
        if(order.Sell&&oldItems-newItems==order.Quantity&&newCoins-oldCoins>=minimumAfterTax)
            return new(RoutineStatus.Completed,"GE_SALE_ITEM_AND_NET_COIN_DELTA_VERIFIED",order.Quantity);
        return new(RoutineStatus.UnknownOutcome,"GE_PARTIAL_OR_MISMATCHED_RECEIPT_JOURNAL_RETAINED");
    }
}
