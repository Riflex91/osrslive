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
        // After a directed tab click or hover Windows Graphics Capture can
        // deliver a transient frame whose native HUD/footer OCR is incomplete.
        // Never accept that frame as gameplay evidence, and NEVER dispatch
        // another input to recover. Re-observe at most twice, requiring a
        // fresh, independently recognized InGame frame from the same window.
        DateTimeOffset lastUnknown=default;
        for(int attempt=0;attempt<3;attempt++)
        {
            if(attempt>0)await Task.Delay(150,ct);
            var next=await capture(ct);
            if(next is null||next.Window!=Current.Window||next.ScreenBounds!=Current.ScreenBounds||
                DateTimeOffset.UtcNow-next.CapturedUtc>TimeSpan.FromSeconds(2))
                throw new RoutineBlockedException("FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED");
            var stage=GameScreenReader.Read(next).Stage;
            if(stage==VisibleGameStage.InGame)
            {
                if(lastUnknown!=default&&next.CapturedUtc<=lastUnknown)continue;
                Current=next;Guard?.Invoke(next);return next;
            }
            if(stage!=VisibleGameStage.Unknown)
                throw new RoutineBlockedException("GAME_SESSION_LEFT_EXECUTION_SCREEN");
            lastUnknown=next.CapturedUtc;
            if(attempt==0)
            {
                string footer=string.Join(" ",next.Labels.Where(l=>
                    l.Bounds.Top>next.ScreenBounds.Height-40&&
                    l.Bounds.Left<next.ScreenBounds.Width*.55).Select(l=>l.Text));
                int chatControls=new[]{"Public","Private","Trade","Report"}
                    .Count(term=>footer.Contains(term,StringComparison.OrdinalIgnoreCase));
                report("HUD_REOBSERVING after_unknown=true "+
                    $"native_hud={next.Labels.Any(l=>l.Kind==VisualLabelKind.GameHud)} "+
                    $"minimap={next.Labels.Any(l=>l.Kind==VisualLabelKind.Minimap)} "+
                    $"skills_icon={next.Labels.Any(l=>l.Kind==VisualLabelKind.SkillsButton)} "+
                    $"chat_control_labels={chatControls}");
            }
        }
        throw new RoutineBlockedException("GAMEPLAY_HUD_NOT_VERIFIED");
    }
    internal void Diagnostic(string message)=>report(message);
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
    // ESC controls an interface and cannot reliably cancel the native
    // right-click popup. Click ONLY verified Cancel and observe actual closure.
    // The full panel hash includes hover chrome; rely on item-slot pixels.
    // Distinguish a transient border/hover effect from a still-overlaid
    // context menu. The latter obscures many item interiors, not just chrome.
    internal sealed record InventoryMatchEvidence(
        bool Valid,int Exact,int Core,int Stable,bool ClickedExact,bool ClickedCore,bool Restored,
        string ChangedSlots="none");
    internal static InventoryMatchEvidence InventoryMatch(
        ExecutionPixels? baseline,ExecutionPixels? observed,int clickedSlot)
    {
        if(baseline is null||observed is null||!baseline.InventoryTabActive||
            !observed.InventoryTabActive||baseline.Slots.Count!=28||observed.Slots.Count!=28||
            clickedSlot is <0 or >=28)
            return new(false,0,0,0,false,false,false);
        int exact=0,core=0,stable=0;
        var changed=new List<int>();
        bool clickedExact=false,clickedCore=false;
        for(int i=0;i<28;i++)
        {
            var a=baseline.Slots[i];var b=observed.Slots[i];
            bool e=a.Signature==b.Signature;
            bool c=a.CoreSignature!=0&&b.CoreSignature!=0&&
                a.CoreSignature==b.CoreSignature;
            if(e)exact++;
            if(c)core++;
            if(e||c)stable++;
            else changed.Add(i);
            if(i==clickedSlot){clickedExact=e;clickedCore=c;}
        }
        // A native popup over the item grid covers several icon interiors.
        // Permit a single transient edge/slot effect, never a changed
        // clicked item or a continuing overlay.
        bool restored=(clickedExact||clickedCore)&&stable>=27;
        return new(true,exact,core,stable,clickedExact,clickedCore,restored,
            changed.Count==0?"none":string.Join(",",changed));
    }
    internal static bool InventoryRestored(
        ExecutionPixels? baseline,ExecutionPixels? observed,int clickedSlot)=>
        InventoryMatch(baseline,observed,clickedSlot).Restored;
    internal static bool InventoryBaselineStable(ExecutionPixels? a,ExecutionPixels? b)=>
        a is { InventoryTabActive:true }&&b is { InventoryTabActive:true }&&
        a.Slots.Count==28&&b.Slots.Count==28&&
        Enumerable.Range(0,28).All(i=>a.Slots[i].Signature==b.Slots[i].Signature);

    internal async Task DismissMenu(ContextMenu menu,CancellationToken ct,
        ExecutionPixels? expectedInventory=null,int clickedSlot=-1)
    {
        var cancel=menu.Exact("Cancel");
        if(cancel is null||cancel.Bounds.IsEmpty||
            !new Rectangle(Point.Empty,DockedWindowLayout.GameSize).Contains(cancel.Bounds))
            throw new RoutineBlockedException("CONTEXT_CANCEL_NOT_VERIFIED");
        var point=new Point(cancel.Bounds.Left+cancel.Bounds.Width/2,
            cancel.Bounds.Top+cancel.Bounds.Height/2);
        if(Driver is WindowVisualGameDriver logger)
            logger.Diagnostic($"CONTEXT_CANCEL_TARGET x={point.X} y={point.Y}");
        var prior=Driver.Current.CapturedUtc;
        await Click(cancel.Bounds,ct);
        // Moving, without clicking, outside the inventory removes native
        // hover highlighting, so the original slots are comparable again.
        await Driver.InputAsync(new(GameInputKind.Hover,new Rectangle(498,398,5,5)),ct);
        var last=Driver.Current.CapturedUtc;
        int stable=0;
        for(int attempt=0;attempt<6;attempt++)
        {
            if(attempt>0)
            {
                await Task.Delay(150,ct);
                await Driver.ObserveAsync(ct);
            }
            var frame=Driver.Current;
            if(frame.CapturedUtc<=prior||frame.CapturedUtc<last)continue;
            var slotMatch=expectedInventory is null?null:
                InventoryMatch(expectedInventory,frame.Pixels,clickedSlot);
            bool? pixelsRestored=slotMatch?.Restored;
            bool menuParsed=ExecutionUi.Menu(frame.Labels) is not null;
            bool menuTextVisible=frame.Labels.Any(l=>
                l.Kind==VisualLabelKind.ContextMenuLine||
                l.Kind==VisualLabelKind.Text&&
                l.Text.Trim().Equals("Choose Option",StringComparison.OrdinalIgnoreCase));
            if(Driver is WindowVisualGameDriver trace)
                trace.Diagnostic($"CONTEXT_CANCEL_OBSERVATION attempt={attempt+1} "+
                    $"tab_active={frame.Pixels?.InventoryTabActive??false} "+
                    $"slots_restored={pixelsRestored?.ToString()??"not_applicable"} "+
                    $"full_matches={slotMatch?.Exact??-1} core_matches={slotMatch?.Core??-1} "+
                    $"stable_slots={slotMatch?.Stable??-1} "+
                    $"clicked_full_same={slotMatch?.ClickedExact??false} "+
                    $"clicked_core_same={slotMatch?.ClickedCore??false} "+
                    $"changed_slots={slotMatch?.ChangedSlots??"unknown"} "+
                    $"menu_parsed={menuParsed} menu_text_seen={menuTextVisible}");
            if((pixelsRestored??!menuTextVisible)&&!menuParsed&&!menuTextVisible)
            {if(++stable>=2)return;}
            else stable=0;
        }
        throw new RoutineBlockedException("CONTEXT_MENU_CANCEL_NOT_CONFIRMED");
    }

    internal async Task Choose(Rectangle box,string action,CancellationToken ct)
    {
        var menu=await OpenMenu(box,ct);
        var hit=menu.Exact(action);
        if(hit is null)
        {
            await DismissMenu(menu,ct);
            throw new RoutineBlockedException("EXACT_CONTEXT_ACTION_NOT_FOUND");
        }
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
        // Baseline and post-Cancel screenshots must be taken with the mouse
        // in the SAME neutral location. Previously the initial reference
        // image was captured while the mouse hovered on the Inventory tab,
        // but after Cancel it was moved to (500,400). Differences in hover
        // decoration then appeared as permanently changed inventory cells.
        // Only a mouse movement; never click into the world.
        await Driver.InputAsync(new(GameInputKind.Hover,new Rectangle(498,398,5,5)),ct);
        if(Driver.Current.Pixels is null)throw new RoutineBlockedException("UNSUPPORTED_INVENTORY_LAYOUT");
        if(!Driver.Current.Pixels.InventoryTabActive)
            throw new RoutineBlockedException("INVENTORY_TAB_NOT_VISUALLY_OPEN");
        // Empty cells do not reliably open context menus in OSRS. Only
        // independently matching native background pixels (not missing OCR)
        // may identify an empty slot. Unknown cells still require a readable
        // actual item menu, or fail closed.
        var verifiedEmpty=new Dictionary<int,ulong>();
        // Immediately after opening Inventory, item sprites can finish
        // rendering on later captured frames. A single early reference was
        // observed to mismatch slots 2 and 3 after a valid Cancel. Require
        // THREE consecutive neutral-position frames with identical slot
        // pixels before inspecting items; otherwise fail closed.
        var first=Driver.Current;
        VisualFrame? second=null;
        int stablePairs=0;
        for(int attempt=0;attempt<5;attempt++)
        {
            await Task.Delay(180,ct);
            var observed=await Driver.ObserveAsync(ct);
            if(observed.CapturedUtc>first.CapturedUtc&&
                InventoryBaselineStable(first.Pixels,observed.Pixels))
            {
                if(++stablePairs>=2){second=observed;break;}
            }
            else stablePairs=0;
            first=observed;
        }
        if(second is null)throw new RoutineBlockedException("INVENTORY_BASELINE_NOT_STABLE");
        foreach(var before in first.Pixels!.Slots.Where(slot=>slot.Empty))
        {
            var after=second.Pixels!.Slots[before.Index];
            if(after.Empty&&after.Signature==before.Signature&&
                after.Quantity is null)
                verifiedEmpty[before.Index]=before.Signature;
        }
        var items=new List<InventoryItem>();int empty=0;
        for(int i=0;i<28;i++)
        {
            var beforeFrame=Driver.Current;
            var before=beforeFrame.Pixels!.Slots[i];
            if(cache.TryGetValue(i,out var cached)&&cached.Signature==before.Signature)
            {items.Add(cached);continue;}
            if(emptyCache.TryGetValue(i,out ulong emptyHash)&&emptyHash==before.Signature){empty++;continue;}
            if(verifiedEmpty.TryGetValue(i,out ulong backgroundHash)&&
                backgroundHash==before.Signature&&before.Empty)
            {
                emptyCache[i]=before.Signature;cache.Remove(i);empty++;
                continue;
            }
            // PostMessage queues pointer movement and a mouse button press;
            // the native client may process them in the same input poll.
            // Stage the cursor over this exact slot and await an independent
            // frame before the ONE right-click. Keep beforeFrame from the
            // neutral cursor position for post-Cancel pixel comparison.
            await Driver.InputAsync(new(GameInputKind.Hover,before.Bounds),ct);
            if(Driver.Current.Pixels is not { InventoryTabActive:true } hovered||
                hovered.Slots.Count!=28)
                throw new RoutineBlockedException("INVENTORY_TAB_LOST_BEFORE_RIGHT_CLICK");
            var menu=await OpenMenu(before.Bounds,ct);
            var examines=menu.Entries.Where(e=>e.Text.StartsWith("Examine ",StringComparison.OrdinalIgnoreCase)).ToArray();
            // A Cancel-only OCR result may be an occupied item's partially
            // recognized menu. Empty slots are authorized ONLY by the two
            // independent native-pixel observations above, never by the
            // absence of a readable Examine line.
            if(examines.Length==1)
            {
                string name=ExecutionCatalog.Normalize(examines[0].Text[8..]);
                if(name.Length==0||name.Length>50)throw new RoutineBlockedException("INVALID_ITEM_NAME");
                var item=new InventoryItem(i,name,ExecutionCatalog.Stackable(name)?before.Quantity:1,before.Signature,before.Bounds);
                cache[i]=item;emptyCache.Remove(i);items.Add(item);
            }
            else
            {
                await DismissMenu(menu,ct,beforeFrame.Pixels,i);
                throw new RoutineBlockedException("AMBIGUOUS_OR_NON_INVENTORY_SLOT");
            }
            await DismissMenu(menu,ct,beforeFrame.Pixels,i);
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
            await DismissMenu(menu,ct);if(found is not null)return new(box,action,found);
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
        if(hit is null)
        {
            await DismissMenu(menu,ct);
            throw new RoutineBlockedException("USE_TARGET_NOT_VERIFIED");
        }
        await Click(hit.Bounds,ct);
    }
}
