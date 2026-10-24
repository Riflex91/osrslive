namespace OSRSLiveDiagnostics;

internal sealed class RoutineBlockedException:Exception
{internal RoutineBlockedException(string reason):base(reason){}}

internal interface IVisualGameDriver
{
    VisualFrame Current {get;}
    Task<VisualFrame> ObserveAsync(CancellationToken ct);
    Task InputAsync(GameInput input,CancellationToken ct);
}

internal sealed class WindowVisualGameDriver:IVisualGameDriver
{
    private readonly Func<CancellationToken,Task<VisualFrame?>> capture;
    private readonly Func<VisualFrame,GameInput,CancellationToken,Task<bool>> dispatch;
    private readonly Action<string> report;
    public VisualFrame Current {get;private set;}
    internal Action<VisualFrame>? Guard;
    internal WindowVisualGameDriver(VisualFrame first,Func<CancellationToken,Task<VisualFrame?>> capture,
        Func<VisualFrame,GameInput,CancellationToken,Task<bool>> dispatch,Action<string> report)
    {Current=first;this.capture=capture;this.dispatch=dispatch;this.report=report;}
    public async Task<VisualFrame> ObserveAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var next=await capture(ct);
        if(next is null||next.Window!=Current.Window||next.ScreenBounds!=Current.ScreenBounds||
            DateTimeOffset.UtcNow-next.CapturedUtc>TimeSpan.FromSeconds(2))
            throw new RoutineBlockedException("FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED");
        var stage=GameScreenReader.Read(next).Stage;
        if(stage!=VisibleGameStage.InGame)
            throw new RoutineBlockedException(stage==VisibleGameStage.Unknown?"GAMEPLAY_HUD_NOT_VERIFIED":"GAME_SESSION_LEFT_EXECUTION_SCREEN");
        Current=next;Guard?.Invoke(next);return next;
    }
    public async Task InputAsync(GameInput input,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(DateTimeOffset.UtcNow-Current.CapturedUtc>TimeSpan.FromSeconds(2))await ObserveAsync(ct);
        Guard?.Invoke(Current);
        if(!await dispatch(Current,input,ct))throw new RoutineBlockedException("INPUT_NOT_QUEUED");
        report(input.Kind.ToString());
        var sent=DateTimeOffset.UtcNow;
        for(int i=0;i<4;i++)
        {
            await Task.Delay(120,ct);
            await ObserveAsync(ct);
            if(Current.CapturedUtc>sent)return;
        }
        throw new RoutineBlockedException("NO_INDEPENDENT_POST_ACTION_FRAME");
    }
}

internal sealed class GameInteractions
{
    internal readonly IVisualGameDriver Driver;
    private readonly Dictionary<int,InventoryItem> cache=new();
    private readonly Dictionary<int,ulong> emptyCache=new();
    private Point? inventoryTab;
    internal GameInteractions(IVisualGameDriver driver){Driver=driver;}
    private static Rectangle At(Point p)=>new(p.X-2,p.Y-2,5,5);
    internal Task Click(Rectangle bounds,CancellationToken ct)=>Driver.InputAsync(new(GameInputKind.LeftClick,bounds),ct);
    internal Task Escape(CancellationToken ct)=>Driver.InputAsync(new(GameInputKind.Escape,new(1,1,4,4)),ct);
    internal async Task<ContextMenu> OpenMenu(Rectangle box,CancellationToken ct)
    {
        // Only one directed right-click. Windows Graphics Capture may deliver
        // an independent frame before OSRS has painted its context menu, so
        // allow a few bounded observations without inventing a menu or
        // resending the potentially unsafe click.
        await Driver.InputAsync(new(GameInputKind.RightClick,box),ct);
        for(int attempt=0;attempt<4;attempt++)
        {
            if(ExecutionUi.Menu(Driver.Current.Labels) is ContextMenu menu)
                return menu;
            if(attempt==3)break;
            await Task.Delay(150,ct);
            await Driver.ObserveAsync(ct);
        }
        throw new RoutineBlockedException("CONTEXT_MENU_NOT_READABLE");
    }
    internal async Task Choose(Rectangle box,string action,CancellationToken ct)
    {
        var menu=await OpenMenu(box,ct);
        var hit=menu.Exact(action);
        if(hit is null){await Escape(ct);throw new RoutineBlockedException("EXACT_CONTEXT_ACTION_NOT_FOUND");}
        await Click(hit.Bounds,ct);
    }
    internal async Task OpenInventory(CancellationToken ct)
    {
        // Inventory is the backpack icon in the TOP row at y~282.
        // The door-shaped icon in the bottom row at y~582 is Logout.
        // Never search the full panel, and demand the actual hover tooltip
        // before clicking this verified icon.
        var points=inventoryTab is Point p?new[]{p}:InventoryTabPolicy.Candidates;
        foreach(var point in points)
        {
            await Driver.InputAsync(new(GameInputKind.Hover,At(point)),ct);
            if(!InventoryTabPolicy.TooltipVerified(Driver.Current.Labels,point))continue;
            inventoryTab=point;
            await Click(At(point),ct);
            return;
        }
        inventoryTab=null;
        throw new RoutineBlockedException("INVENTORY_TAB_NOT_VERIFIED");
    }
    internal async Task<InventoryState> Inventory(CancellationToken ct,bool ensureTab=true)
    {
        if(ensureTab)await OpenInventory(ct);
        if(Driver.Current.Pixels is null)throw new RoutineBlockedException("UNSUPPORTED_INVENTORY_LAYOUT");
        // Empty cells do not reliably open context menus in OSRS. Only
        // independently matching native background pixels (not missing OCR)
        // may identify an empty slot. Unknown cells still require a readable
        // actual item menu, or fail closed.
        var verifiedEmpty=new Dictionary<int,ulong>();
        var first=Driver.Current;
        if(first.Pixels!.Slots.Any(slot=>slot.Empty))
        {
            await Task.Delay(180,ct);
            var second=await Driver.ObserveAsync(ct);
            if(second.CapturedUtc>first.CapturedUtc&&second.Pixels is not null)
                foreach(var before in first.Pixels.Slots.Where(slot=>slot.Empty))
                {
                    var after=second.Pixels.Slots[before.Index];
                    if(after.Empty&&after.Signature==before.Signature&&
                        after.Quantity is null)
                        verifiedEmpty[before.Index]=before.Signature;
                }
        }
        var items=new List<InventoryItem>();int empty=0;
        for(int i=0;i<28;i++)
        {
            var before=Driver.Current.Pixels!.Slots[i];
            if(cache.TryGetValue(i,out var cached)&&cached.Signature==before.Signature)
            {items.Add(cached);continue;}
            if(emptyCache.TryGetValue(i,out ulong emptyHash)&&emptyHash==before.Signature){empty++;continue;}
            if(verifiedEmpty.TryGetValue(i,out ulong backgroundHash)&&
                backgroundHash==before.Signature&&before.Empty)
            {
                emptyCache[i]=before.Signature;cache.Remove(i);empty++;
                continue;
            }
            var menu=await OpenMenu(before.Bounds,ct);
            var examines=menu.Entries.Where(e=>e.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(examines.Length==0&&menu.Entries.Count==1&&menu.Exact("Cancel") is not null)
            {emptyCache[i]=before.Signature;cache.Remove(i);empty++;}
            else if(examines.Length==1)
            {
                string name=ExecutionCatalog.Normalize(examines[0].Text[8..]);
                if(name.Length==0||name.Length>50)throw new RoutineBlockedException("INVALID_ITEM_NAME");
                var item=new InventoryItem(i,name,ExecutionCatalog.Stackable(name)?before.Quantity:1,before.Signature,before.Bounds);
                cache[i]=item;emptyCache.Remove(i);items.Add(item);
            }
            else{await Escape(ct);throw new RoutineBlockedException("AMBIGUOUS_OR_NON_INVENTORY_SLOT");}
            await Escape(ct);
        }
        // Inventory movement while it was being inspected invalidates the whole
        // observation. Menu overlays are gone before comparing signatures.
        if(items.Any(item=>Driver.Current.Pixels!.Slots[item.Slot].Signature!=item.Signature||
                ExecutionCatalog.Stackable(item.Name)&&Driver.Current.Pixels.Slots[item.Slot].Quantity!=item.Quantity)||
            emptyCache.Where(e=>!items.Any(i=>i.Slot==e.Key)).Any(e=>Driver.Current.Pixels!.Slots[e.Key].Signature!=e.Value))
            throw new RoutineBlockedException("INVENTORY_CHANGED_DURING_READ");
        return new(items,empty,Driver.Current.CapturedUtc);
    }
    internal async Task<SceneTarget> FindObject(string action,string name,CancellationToken ct)
    {
        string target=action+" "+name;
        string? generic=action=="Mine"?"Rocks":action=="Craft-rune"?"Altar":null;
        // Search by hovering only; no blind walking or clicks on unverified
        // scenery. A readable right-click entry is required before the action.
        for(int row=0;row<7;row++)for(int col=0;col<11;col++)
        {
            var box=At(new Point(50+col*64,65+row*45));
            await Driver.InputAsync(new(GameInputKind.Hover,box),ct);
            var options=new[]{name,generic}.Where(n=>n is not null).ToArray();
            bool hint=Driver.Current.Labels.Any(l=>l.Bounds.Top<45&&options.Any(n=>
                l.Text.Equals(action+" "+n,StringComparison.OrdinalIgnoreCase)||l.Text.StartsWith(action+" "+n+" /",StringComparison.OrdinalIgnoreCase)));
            if(!hint)continue;
            var menu=await OpenMenu(box,ct);
            string? found=options.FirstOrDefault(n=>menu.Exact(action+" "+n) is not null);
            await Escape(ct);if(found is not null)return new(box,action,found);
        }
        throw new RoutineBlockedException("REQUIRED_OBJECT_NOT_IDENTIFIED_IN_VISIBLE_SCENE");
    }
    internal async Task Amount(int amount,CancellationToken ct)
    {
        if(amount<=0||!ExecutionUi.AmountPrompt(Driver.Current.Labels))throw new RoutineBlockedException("AMOUNT_DIALOG_NOT_VERIFIED");
        await Driver.InputAsync(new(GameInputKind.Text,new(15,450,450,120),amount.ToString(System.Globalization.CultureInfo.InvariantCulture)),ct);
    }
    internal async Task<Rectangle> FindControl(string name,Rectangle region,CancellationToken ct)
    {
        var direct=ExecutionUi.Unique(Driver.Current.Labels,name,region);
        if(direct is not null)return direct.Bounds;
        for(int y=region.Top+12;y<region.Bottom;y+=24)for(int x=region.Left+12;x<region.Right;x+=24)
        {
            var p=new Point(x,y);
            await Driver.InputAsync(new(GameInputKind.Hover,At(p)),ct);
            if(ExecutionUi.Tooltip(Driver.Current.Labels,name,p))return At(p);
        }
        throw new RoutineBlockedException("CONTROL_NOT_IDENTIFIED_"+name.Replace(' ','_').ToUpperInvariant());
    }
    internal async Task UseItem(InventoryItem source,Rectangle destination,string target,CancellationToken ct)
    {
        await Choose(source.Bounds,"Use "+source.Name,ct);
        // Selecting Use must be visible before the second object is clicked.
        var menu=await OpenMenu(destination,ct);
        string expected="Use "+source.Name+" -> "+target;
        var hit=menu.Exact(expected);
        if(hit is null){await Escape(ct);throw new RoutineBlockedException("USE_TARGET_NOT_VERIFIED");}
        await Click(hit.Bounds,ct);
    }
}
