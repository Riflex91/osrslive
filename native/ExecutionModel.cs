using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal enum GameInputKind
{
    Hover, LeftClick, RightClick, Escape, Text,
    WheelDown, WheelUp, WheelDownGameWindow, WheelUpGameWindow
}
internal sealed record GameInput(GameInputKind Kind,Rectangle Bounds,string? Text=null,bool Submit=true);
internal sealed record ObservedSlot(int Index,Rectangle Bounds,ulong Signature,bool Empty,int? Quantity=null,ulong CoreSignature=0,string? IdentityDigest=null);
internal sealed record InventoryItem(int Slot,string Name,int? Quantity,ulong Signature,Rectangle Bounds);
internal sealed record InventoryState(IReadOnlyList<InventoryItem> Items,int EmptySlots,DateTimeOffset ObservedUtc)
{
    internal int? Count(string name)
    {
        var items=Items.Where(i=>i.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(items.Any(i=>i.Quantity is null))return null;
        long total=items.Sum(i=>(long)i.Quantity!.Value);
        return total<=int.MaxValue?(int)total:null;
    }
    internal InventoryItem? Find(string name)=>Items.FirstOrDefault(i=>i.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
}
internal sealed record ExecutionPixels(IReadOnlyList<ObservedSlot> Slots,ulong SceneSignature,NavigationPixels? Navigation=null,
    bool InventoryTabActive=false,ulong InventoryPanelSignature=0,string InventorySlot0Diagnostic="",
    IReadOnlyList<string>? InventorySlotDiagnostics=null,int? Hitpoints=null,string HealthEvidence="");
internal sealed record MenuEntry(string Text,Rectangle Bounds);
internal sealed record ContextMenu(IReadOnlyList<MenuEntry> Entries)
{
    internal MenuEntry? Exact(string text)
    {
        var hits=Entries.Where(e=>e.Text.Equals(text,StringComparison.OrdinalIgnoreCase)).ToArray();
        return hits.Length==1?hits[0]:null;
    }
}
internal sealed record TrainingSpec(string Id,string Skill,string Product,string? Tool,
    string? ObjectAction,string? ObjectName,string? Input=null,string? ProductionChoice=null,
    string? SecondInput=null,int InputPerOutput=1);
internal enum RoutineStatus { Waiting, Progress, Completed, Blocked, UnknownOutcome }
internal sealed record RoutineResult(RoutineStatus Status,string Reason,int Produced=0);
internal sealed record TradeOrder(string Id,string Item,int Quantity,long UnitPrice,bool Sell,
    long CoinReserve,DateTimeOffset PriceObservedUtc);
internal sealed record SceneTarget(Rectangle Bounds,string Action,string Name);

internal static class ExecutionCatalog
{
    internal static readonly TrainingSpec[] Training=
    {
        new("CUT_TREES","Woodcutting","logs","axe","Chop down","Tree"),
        new("CUT_OAKS","Woodcutting","oak logs","axe","Chop down","Oak"),
        new("CUT_WILLOWS","Woodcutting","willow logs","axe","Chop down","Willow"),
        // Native Rocks/Altar labels do not identify the resulting material.
        // Generic interactions are confirmed by their actual inventory yield;
        // an alternative ore/rune is reported as such, never as the plan's SKU.
        new("MINE_COPPER_TIN","Mining","copper ore","pickaxe","Mine","Copper rocks"),
        new("MINE_IRON","Mining","iron ore","pickaxe","Mine","Iron rocks"),
        new("NET_SHRIMPS","Fishing","raw shrimps","small fishing net","Net","Fishing spot"),
        new("FLY_FISH","Fishing","raw trout","fly fishing rod","Lure","Fishing spot",SecondInput:"feather"),
        new("COOK_SHRIMPS","Cooking","shrimps",null,"Use","Range","raw shrimps","Raw shrimps"),
        new("COOK_TROUT","Cooking","trout",null,"Use","Range","raw trout","Raw trout"),
        new("COOK_SALMON","Cooking","salmon",null,"Use","Range","raw salmon","Raw salmon"),
        new("BURN_LOGS","Firemaking","ashes","tinderbox",null,null,"logs"),
        new("BURN_OAK","Firemaking","ashes","tinderbox",null,null,"oak logs"),
        new("BURN_WILLOW","Firemaking","ashes","tinderbox",null,null,"willow logs"),
        new("BURN_MAPLE","Firemaking","ashes","tinderbox",null,null,"maple logs"),
        new("LEATHER_GLOVES","Crafting","leather gloves","needle",null,null,"leather","Leather gloves","thread"),
        new("LEATHER_BOOTS","Crafting","leather boots","needle",null,null,"leather","Leather boots","thread"),
        new("LEATHER_COWL","Crafting","leather cowl","needle",null,null,"leather","Leather cowl","thread"),
        new("LEATHER_VAMBRACES","Crafting","leather vambraces","needle",null,null,"leather","Leather vambraces","thread"),
        new("LEATHER_BODY","Crafting","leather body","needle",null,null,"leather","Leather body","thread"),
        new("LEATHER_CHAPS","Crafting","leather chaps","needle",null,null,"leather","Leather chaps","thread"),
        new("HARDLEATHER_BODY","Crafting","hardleather body","needle",null,null,"hard leather","Hardleather body","thread"),
        new("SMELT_BRONZE","Smithing","bronze bar",null,"Smelt","Furnace","copper ore","Bronze bar","tin ore"),
        new("BRONZE_PLATEBODY","Smithing","bronze platebody","hammer","Smith","Anvil","bronze bar","Bronze platebody",InputPerOutput:5),
        new("IRON_PLATEBODY","Smithing","iron platebody","hammer","Smith","Anvil","iron bar","Iron platebody",InputPerOutput:5),
        new("STEEL_PLATEBODY","Smithing","steel platebody","hammer","Smith","Anvil","steel bar","Steel platebody",InputPerOutput:5),
        new("MITHRIL_PLATEBODY","Smithing","mithril platebody","hammer","Smith","Anvil","mithril bar","Mithril platebody",InputPerOutput:5),
        new("ADAMANT_PLATEBODY","Smithing","adamant platebody","hammer","Smith","Anvil","adamantite bar","Adamant platebody",InputPerOutput:5),
        new("AIR_RUNES","Runecraft","air rune",null,"Craft-rune","Air altar","rune essence"),
        new("EARTH_RUNES","Runecraft","earth rune",null,"Craft-rune","Earth altar","rune essence"),
        new("BODY_RUNES","Runecraft","body rune",null,"Craft-rune","Body altar","rune essence"),
        new("BURY_BONES","Prayer","",null,null,null,"bones","Bury")
    };
    internal static bool Stackable(string item)=>item is "coins" or "thread" or "feather" or "air rune" or "mind rune" or
        "earth rune" or "water rune" or "fire rune" or "body rune" or "cosmic rune" or "nature rune" or "chaos rune" or
        "bronze arrow" or "iron arrow" or "steel arrow" or "mithril arrow" or "adamant arrow";
    internal static string Normalize(string text)=>Regex.Replace(text.Trim().ToLowerInvariant(),@"\s+"," ");
}

internal static class ExecutionUi
{
    // Native resizable inventory: four columns, seven rows. Use only after
    // observing the Inventory tab and verifying slot context menus.
    internal static Rectangle InventorySlot(int index)=>new(760+index%4*42,312+index/4*36,32,32);
    internal static ContextMenu? Menu(IReadOnlyList<VisualLabel> labels)
    {
        // Focused OCR is tied to the most recent right-click and must not be
        // mixed with unrelated whole-frame OCR. Ordinary fixture/UI parsing
        // remains available when a focused menu was not captured.
        var focused=labels.Where(l=>l.Kind==VisualLabelKind.ContextMenuLine).ToArray();
        var source=focused.Length>0?focused:
            labels.Where(l=>l.Kind==VisualLabelKind.Text).ToArray();
        var headers=source.Where(l=>l.Text.Trim().Equals("Choose Option",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(headers.Length!=1)return null;
        var h=headers[0];
        var cancel=source.Where(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase)&&
            l.Bounds.Top>h.Bounds.Bottom&&l.Bounds.Top<h.Bounds.Bottom+450&&Math.Abs(l.Bounds.Left-h.Bounds.Left)<30)
            .OrderBy(l=>l.Bounds.Top).FirstOrDefault();
        if(cancel is null)return null;
        var entries=source.Where(l=>l.Bounds.Top>=h.Bounds.Bottom&&
            l.Bounds.Bottom<=cancel.Bounds.Bottom+2&&Math.Abs(l.Bounds.Left-h.Bounds.Left)<30)
            .Select(l=>new MenuEntry(Regex.Replace(l.Text.Trim(),@"\s+"," "),l.Bounds)).ToArray();
        return new(entries);
    }
    internal static VisualLabel? Unique(IReadOnlyList<VisualLabel> labels,string text,Rectangle? region=null)
    {
        var hits=labels.Where(l=>l.Kind==VisualLabelKind.Text&&l.Text.Trim().Equals(text,StringComparison.OrdinalIgnoreCase)&&
            (region is null||region.Value.Contains(l.Bounds))).ToArray();
        return hits.Length==1?hits[0]:null;
    }
    internal static bool Bank(IReadOnlyList<VisualLabel> labels)=>
        labels.Any(l=>l.Text.Trim().Equals("The Bank of Gielinor",StringComparison.OrdinalIgnoreCase))&&
        labels.Any(l=>l.Text.Contains("Withdraw",StringComparison.OrdinalIgnoreCase)||l.Text.Contains("Rearrange mode",StringComparison.OrdinalIgnoreCase));
    internal static bool Ge(IReadOnlyList<VisualLabel> labels)=>labels.Any(l=>l.Text.Trim().Equals("Grand Exchange",StringComparison.OrdinalIgnoreCase));
    internal static bool AmountPrompt(IReadOnlyList<VisualLabel> labels)=>labels.Any(l=>
        l.Bounds.Top>420&&Regex.IsMatch(l.Text.Trim(),@"^(?:Enter (?:amount|quantity|price)|How many (?:would you like to|do you wish to) (?:withdraw|deposit|buy|sell|make))[:?.]?$",RegexOptions.IgnoreCase));
    internal static bool ItemSearchPrompt(IReadOnlyList<VisualLabel> labels)=>labels.Any(l=>l.Bounds.Top>420&&
        l.Text.Trim().Equals("What would you like to buy?",StringComparison.OrdinalIgnoreCase));
    internal static bool BankSearchPrompt(IReadOnlyList<VisualLabel> labels)=>Bank(labels)&&labels.Any(l=>l.Bounds.Top>420&&
        l.Text.Trim().StartsWith("Show items whose names contain",StringComparison.OrdinalIgnoreCase));
    internal static bool Production(IReadOnlyList<VisualLabel> labels)=>labels.Any(l=>l.Bounds.Top>400&&
        (l.Text.Contains("How many",StringComparison.OrdinalIgnoreCase)||l.Text.Contains("What would you like",StringComparison.OrdinalIgnoreCase)))||
        labels.Any(l=>l.Text.Trim().Equals("Smithing",StringComparison.OrdinalIgnoreCase))||
        labels.Any(l=>l.Text.Trim().Equals("Smelting",StringComparison.OrdinalIgnoreCase));
    internal static bool Tooltip(IReadOnlyList<VisualLabel> labels,string expected,Point hovered)=>labels.Any(l=>
        l.Kind==VisualLabelKind.Text&&l.Text.Trim().Equals(expected,StringComparison.OrdinalIgnoreCase)&&
        (l.Bounds.Top<45||Math.Abs(l.Bounds.Left-hovered.X)<100&&Math.Abs(l.Bounds.Top-hovered.Y)<80));
}
