namespace OSRSLiveDiagnostics;

internal static class ExecutionRoutinesTests
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        static void Require(bool b,string reason){if(!b)throw new Exception("Execution routines: "+reason);}
        var now=DateTimeOffset.UtcNow;
        InventoryState Stock(DateTimeOffset time,params (string Item,int? Count)[] amounts)=>new(amounts.Select((a,i)=>
            new InventoryItem(i,a.Item,a.Count,(ulong)i+1,ExecutionUi.InventorySlot(i))).ToArray(),28-amounts.Length,time);
        var before=Stock(now,("coins",1000),("copper ore",2),("tin ore",2));
        var after=Stock(now.AddSeconds(1),("coins",1000),("copper ore",1),("tin ore",1),("bronze bar",1));
        var bronze=F2PProgression.Recipes.Single(r=>r.Product=="bronze bar");
        Require(TrainingRoutines.VerifyRecipe(bronze,before,after).Status==RoutineStatus.Completed,"full recipe delta");
        Require(TrainingRoutines.VerifyRecipe(bronze,before,Stock(now.AddSeconds(1),("coins",1000),("copper ore",1),("tin ore",2),("bronze bar",1))).Status==RoutineStatus.UnknownOutcome,
            "product appearance alone cannot prove complete recipe");
        Require(TrainingRoutines.VerifyRecipe(bronze,before,after with{ObservedUtc=now}).Status!=RoutineStatus.Completed,"reused result frame rejected");
        var smith=ExecutionCatalog.Training.Single(s=>s.Id=="IRON_PLATEBODY");
        Require(TrainingRoutines.Verify(smith,Stock(now,("iron bar",5)),Stock(now.AddSeconds(1),("iron platebody",1))).Status==RoutineStatus.Completed,
            "five-bar manufacturing output");
        Require(TrainingRoutines.Verify(smith,Stock(now,("iron bar",5)),Stock(now.AddSeconds(1),("iron bar",1),("iron platebody",1))).Status!=RoutineStatus.Completed,
            "wrong recipe quantity is not success");
        var net=ExecutionCatalog.Training.Single(s=>s.Id=="NET_SHRIMPS");
        Require(TrainingRoutines.Verify(net,Stock(now),Stock(now.AddSeconds(1),("raw anchovies",1))).Produced==1,"mixed fishing catch verified");
        var cut=ExecutionCatalog.Training.Single(s=>s.Id=="CUT_TREES");
        Require(TrainingRoutines.Verify(cut,Stock(now,("logs",1)),Stock(now.AddSeconds(1),("logs",1))).Status==RoutineStatus.Waiting,
            "an acknowledged click without a yield is not progress");
        var rune=ExecutionCatalog.Training.Single(s=>s.Id=="AIR_RUNES");
        Require(TrainingRoutines.Verify(rune,Stock(now,("rune essence",2)),Stock(now.AddSeconds(1),("air rune",4))).Status==RoutineStatus.Completed,
            "runecraft multi-rune output verified with essence consumption");
        Require(TrainingRoutines.Verify(rune,Stock(now,("rune essence",2)),Stock(now.AddSeconds(1),("rune essence",2),("air rune",4))).Status!=RoutineStatus.Completed,
            "unrelated rune gift is not crafting progress");
        Require(Stock(now,("coins",null)).Count("coins") is null,"unknown stack stays unknown");
        var skills=new[]{new PublicOsrsSkill("Woodcutting",1,0,0)};
        Require(!GameplayCoordinator.Supplied(cut,Stock(now,("rune axe",1)),skills),"owned high-tier tool cannot bypass skill");
        Require(GameplayCoordinator.Supplied(cut,Stock(now,("bronze axe",1)),skills),"starter tool permits baseline training");
        VisualLabel L(string text,int x,int y,int width=180)=>new(text,new(x,y,width,14));
        var menu=new[]{L("Choose Option",30,30),L("Use Logs",30,50),L("Drop Logs",30,66),L("Examine Logs",30,82),L("Cancel",30,98),
            L("Deposit-All Logs",400,50)};
        ContextMenuOcr.SelfTest();
        NativeMenuHeaderReader.SelfTest();
        InventoryTabPolicy.SelfTest();
        NativeInventoryEmptyReader.SelfTest();
        var wrongPanel=new InventoryGridDriver(true,active:false);
        bool panelBlocked=false;
        try {await new GameInteractions(wrongPanel).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {panelBlocked=ex.Message=="INVENTORY_TAB_NOT_VISUALLY_OPEN";}
        Require(panelBlocked&&wrongPanel.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
            "Inventory panel must be independently verified before reading slots or right-clicking");
        var emptyDriver=new InventoryGridDriver(true);
        var fullyEmpty=await new GameInteractions(emptyDriver).Inventory(CancellationToken.None);
        Require(fullyEmpty.EmptySlots==28&&fullyEmpty.Items.Count==0&&
            emptyDriver.Inputs.Any(i=>i.Kind==GameInputKind.LeftClick)&&
            emptyDriver.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
            "a visually and independently verified empty inventory skips all right clicks");
        var unknownDriver=new InventoryGridDriver(false);
        bool unknownBlocked=false;
        try{await new GameInteractions(unknownDriver).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){unknownBlocked=ex.Message=="CONTEXT_MENU_NOT_READABLE";}
        Require(unknownBlocked&&unknownDriver.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1,
            "missing context menu alone must NEVER mark an unverified slot empty");
        var tabDriver=new InventoryTabDriver();
        await new GameInteractions(tabDriver).OpenInventory(CancellationToken.None);
        Require(tabDriver.Inputs.Count(i=>i.Kind==GameInputKind.LeftClick)==1&&
            tabDriver.Inputs.Where(i=>i.Kind==GameInputKind.LeftClick)
                .All(i=>i.Bounds.Top>=265&&i.Bounds.Top<302),
            "Inventory opening must click upper-row backpack, never bottom-row Logout");
        var wrongTooltip=new InventoryTabDriver(misplacedTooltip:true);
        bool noWrongClick=false;
        try {await new GameInteractions(wrongTooltip).OpenInventory(CancellationToken.None);}
        catch(RoutineBlockedException ex) {noWrongClick=ex.Message=="INVENTORY_TAB_NOT_VERIFIED";}
        Require(noWrongClick&&wrongTooltip.Inputs.All(i=>i.Kind!=GameInputKind.LeftClick),
            "misleading Inventory text inside a panel must not authorize a tab click");
        var focusedMenu=menu.Where(l=>l.Bounds.Left<300)
            .Select(l=>l with {Kind=VisualLabelKind.ContextMenuLine}).ToArray();
        var conflictingFrame=focusedMenu.Concat(new[]{
            L("Choose Option",400,30),L("Examine Different Item",400,66),L("Cancel",400,98)
        }).ToArray();
        Require(ExecutionUi.Menu(conflictingFrame)?.Exact("Examine Logs") is not null&&
            ExecutionUi.Menu(conflictingFrame)?.Exact("Examine Different Item") is null,
            "focused right-click menu cannot be contaminated by unrelated global OCR");
        var corruptFocused=focusedMenu.Where(l=>l.Text!="Cancel").Concat(menu).ToArray();
        Require(ExecutionUi.Menu(corruptFocused) is null,
            "incomplete focused menu fails closed instead of falling back to another source");
        // Partial OCR candidates may be logged as COUNTS, but must never be
        // used to authorize an item menu, even if a complete header/cancel
        // pair is present as unverified candidate glyphs.
        var diagnosticOnly=menu.Select(l=>
            l with {Kind=VisualLabelKind.ContextMenuCandidate}).ToArray();
        Require(ExecutionUi.Menu(diagnosticOnly) is null&&
            diagnosticOnly.Count(l=>l.Kind==VisualLabelKind.ContextMenuLine)==0,
            "partial context-menu OCR diagnostic evidence is non-actionable");
        var parsed=ExecutionUi.Menu(menu);
        Require(parsed?.Exact("Drop Logs") is not null&&parsed.Exact("Deposit-All Logs") is null,"unrelated OCR cannot enter a context menu");
        Require(ExecutionUi.Menu(menu.Append(L("Choose Option",400,5)).ToArray()) is null,"ambiguous menu headers blocked");
        var fake=new ScriptedDriver(menu);
        var interactions=new GameInteractions(fake);
        bool blocked=false;
        try{await interactions.Choose(new(100,100,5,5),"Deposit-All Logs",CancellationToken.None);}
        catch(RoutineBlockedException){blocked=true;}
        Require(blocked&&fake.Inputs.Select(i=>i.Kind).SequenceEqual(new[]{
                GameInputKind.RightClick,GameInputKind.LeftClick,GameInputKind.Hover})&&
            fake.Inputs[1].Bounds==menu.Single(l=>l.Text=="Cancel").Bounds&&
            fake.Inputs[2].Bounds.Contains(new Point(500,400)),
            "missing target clicks ONLY verified Cancel then moves mouse away; no Escape");
        fake=new ScriptedDriver(menu);
        await new GameInteractions(fake).Choose(new(100,100,5,5),"Use Logs",CancellationToken.None);
        Require(fake.Inputs.Select(i=>i.Kind).SequenceEqual(new[]{GameInputKind.RightClick,GameInputKind.LeftClick}),"observe menu before exact selection");
        var targetMissing=new ScriptedDriver(menu);
        bool missingUseBlocked=false;
        try
        {
            await new GameInteractions(targetMissing).UseItem(
                new InventoryItem(0,"logs",1,1,ExecutionUi.InventorySlot(0)),
                new Rectangle(220,150,12,12),"Range",CancellationToken.None);
        }
        catch(RoutineBlockedException ex)
        {missingUseBlocked=ex.Message=="USE_TARGET_NOT_VERIFIED";}
        Require(missingUseBlocked&&targetMissing.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==2&&
            targetMissing.Inputs.Any(i=>i.Kind==GameInputKind.LeftClick&&
                i.Bounds==menu.Single(l=>l.Text=="Cancel").Bounds)&&
            targetMissing.Inputs.All(i=>i.Kind!=GameInputKind.Escape),
            "unverified Use target closes via real Cancel, not Escape");
        var late=new ScriptedDriver(menu,delayObservations:2);
        var lateResult=await new GameInteractions(late).OpenMenu(new(100,100,5,5),CancellationToken.None);
        Require(lateResult.Exact("Use Logs") is not null&&late.Observations>=2&&
            late.Inputs.Count==1&&late.Inputs[0].Kind==GameInputKind.RightClick,
            "menu may appear in a later independently observed frame without another right-click");
        var never=new ScriptedDriver(menu,delayObservations:10);
        blocked=false;
        try{await new GameInteractions(never).OpenMenu(new(100,100,5,5),CancellationToken.None);}
        catch(RoutineBlockedException ex){blocked=ex.Message=="CONTEXT_MENU_NOT_READABLE";}
        Require(blocked&&never.Inputs.Count==1&&never.Observations==3,
            "unreadable context menu blocks after bounded observation without repeating click");
        var baseSlots=Enumerable.Range(0,28).Select(i=>
            new ObservedSlot(i,ExecutionUi.InventorySlot(i),(ulong)i+1,i>0)).ToArray();
        var basePixels=new ExecutionPixels(baseSlots,1,InventoryTabActive:true,
            InventoryPanelSignature:813);
        var chromeOnly=basePixels with{InventoryPanelSignature=927};
        var oneTransient=chromeOnly with {Slots=baseSlots.Select((v,i)=>
            i==27?v with {Signature=88777}:v).ToArray()};
        var clickedChanged=chromeOnly with {Slots=baseSlots.Select((v,i)=>
            i==0?v with {Signature=88777}:v).ToArray()};
        var severalChanged=chromeOnly with {Slots=baseSlots.Select((v,i)=>
            i is 6 or 27?v with {Signature=88777}:v).ToArray()};
        Require(GameInteractions.InventoryRestored(basePixels,chromeOnly,0)&&
            GameInteractions.InventoryRestored(basePixels,oneTransient,0)&&
            !GameInteractions.InventoryRestored(basePixels,clickedChanged,0)&&
            !GameInteractions.InventoryRestored(basePixels,severalChanged,0)&&
            !GameInteractions.InventoryRestored(basePixels,chromeOnly with{InventoryTabActive=false},0),
            "Cancel confirmation tolerates hover chrome but not occupied slot, popup or tab changes");
        var spriteSlots=baseSlots.Select((v,i)=>
            v with{CoreSignature=(ulong)(i+500)}).ToArray();
        var spriteBefore=basePixels with{Slots=spriteSlots};
        var hoverBorders=spriteBefore with{Slots=spriteSlots.Select((v,i)=>
            v with{Signature=v.Signature+5000}).ToArray()};
        var coveredSprites=spriteBefore with{Slots=spriteSlots.Select((v,i)=>
            i<5?v with{Signature=v.Signature+100,CoreSignature=v.CoreSignature+100}:v)
            .ToArray()};
        var changedItem=spriteBefore with{Slots=spriteSlots.Select((v,i)=>
            i==0?v with{Signature=v.Signature+100,CoreSignature=v.CoreSignature+100}:v)
            .ToArray()};
        var hoverMatch=GameInteractions.InventoryMatch(spriteBefore,hoverBorders,0);
        Require(hoverMatch.Restored&&hoverMatch.Exact==0&&hoverMatch.Core==28&&
            hoverMatch.ChangedSlots=="none"&&
            !GameInteractions.InventoryRestored(spriteBefore,coveredSprites,0)&&
            GameInteractions.InventoryMatch(spriteBefore,coveredSprites,0).ChangedSlots=="0,1,2,3,4"&&
            !GameInteractions.InventoryRestored(spriteBefore,changedItem,0),
            "all hover-sensitive outer pixels may change but authentic item interiors must match; overlays fail");
        var cancellable=new InventoryMenuDriver(menu,closes:true);
        var checkedInventory=await new GameInteractions(cancellable).Inventory(CancellationToken.None);
        int firstRightClick=cancellable.Inputs.FindIndex(i=>i.Kind==GameInputKind.RightClick);
        int lastNeutralBeforeClick=cancellable.Inputs.FindLastIndex(firstRightClick-1,
            i=>i.Kind==GameInputKind.Hover&&i.Bounds.Contains(new Point(500,400)));
        Require(firstRightClick>0&&lastNeutralBeforeClick==firstRightClick-1,
            "inventory slot baseline must be acquired after neutralizing the tab-hover position");
        Require(checkedInventory.Items.Count==1&&checkedInventory.Items[0].Name=="logs"&&
            checkedInventory.EmptySlots==27&&
            cancellable.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1&&
            cancellable.Inputs.Count(i=>i.Kind==GameInputKind.LeftClick)==2&&
            cancellable.Inputs.All(i=>i.Kind!=GameInputKind.Escape),
            "menu Cancel restores verified inventory before scanning other slots"+
            $" items={checkedInventory.Items.Count} name={(checkedInventory.Items.FirstOrDefault()?.Name??"none")}"+
            $" empty={checkedInventory.EmptySlots} right={cancellable.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)}"+
            $" left={cancellable.Inputs.Count(i=>i.Kind==GameInputKind.LeftClick)}");
        var stuckMenu=new InventoryMenuDriver(menu,closes:false);
        bool stuckBlocked=false;
        try{await new GameInteractions(stuckMenu).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {stuckBlocked=ex.Message=="CONTEXT_MENU_CANCEL_NOT_CONFIRMED";}
        Require(stuckBlocked&&stuckMenu.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1&&
            stuckMenu.Inputs.Count(i=>i.Kind==GameInputKind.LeftClick)==2&&
            stuckMenu.Inputs.All(i=>i.Kind!=GameInputKind.Escape),
            "menu that survives a queued Cancel must block BEFORE any second right-click");
        var noCancelMenu=new ContextMenu(new[]{
            new MenuEntry("Examine Logs",new Rectangle(30,82,180,14))
        });
        var noCancelDriver=new InventoryMenuDriver(menu,closes:true);
        bool noCancelBlocked=false;
        try{await new GameInteractions(noCancelDriver).DismissMenu(noCancelMenu,CancellationToken.None);}
        catch(RoutineBlockedException ex){noCancelBlocked=ex.Message=="CONTEXT_CANCEL_NOT_VERIFIED";}
        Require(noCancelBlocked&&noCancelDriver.Inputs.Count==0,
            "missing or ambiguous Cancel never authorizes an invented click");
        var order=new TradeOrder("test-buy","iron axe",1,100,false,500,now);
        var facts=new ProgressionFacts(Ironman:false,FreeGeSlots:1);
        Require(TradeRoutine.Validate(order,facts,Stock(now,("coins",600)),now) is null,"bounded affordable purchase");
        Require(TradeRoutine.Validate(order,facts,Stock(now,("coins",599)),now)=="COIN_RESERVE_OR_OFFER_CAP","reserve exact boundary");
        Require(TradeRoutine.Validate(order,facts with{Ironman=true},before,now)=="NORMAL_ACCOUNT_MODE_REQUIRED","ironman blocked");
        Require(TradeRoutine.Validate(order,facts with{FreeGeSlots=0},before,now)=="F2P_GE_SLOT_REQUIRED","busy offer slot blocked");
        Require(TradeRoutine.Validate(order with{PriceObservedUtc=now.AddMinutes(-2)},facts,before,now)=="PRICE_STALE","old quote blocked");
        Require(TradeRoutine.Validate(order,facts,Stock(now,("coins",null)),now)=="EXACT_INVENTORY_COIN_BALANCE_REQUIRED","unknown coin quantity blocked");
        var sale=order with{Sell=true,Item="oak logs"};
        Require(TradeRoutine.Validate(sale,facts,Stock(now,("oak logs",1)),now)=="RESTRICTED_F2P_SALE_NOT_ALLOWED","new account restriction unknown");
        Require(TradeRoutine.Validate(sale,facts with{PlayedHours=21,QuestPoints=10,TotalLevel=100},Stock(now,("oak logs",1)),now) is null,
            "all restrictions explicitly satisfied");
        var invoice=new[]{L("Grand Exchange",100,10),L("Buy offer",100,30),L("iron axe",100,50),
            L("Quantity: 1",100,75),L("Price per item: 100 coins",100,100),L("Total price: 100 coins",100,125),L("Confirm",100,160)};
        Require(GeReader.Invoice(invoice,"iron axe") is GeInvoice inv&&GeReader.Matches(inv,order),"invoice product/qty/price/total bound");
        Require(!GeReader.Matches(GeReader.Invoice(invoice,"iron axe")!,order with{Quantity=2}),"changed quantity cannot commit");
        Require(GeReader.Invoice(invoice,"rune axe") is null,"wrong SKU cannot commit");
        Require(!GeReader.DetailMatches(invoice,order),"prepared offer is not a collectible submitted offer");
        var purchase=TradeRoutine.VerifyReceipt(order,Stock(now,("coins",600)),Stock(now.AddSeconds(1),("coins",500),("iron axe",1)));
        Require(purchase.Status==RoutineStatus.Completed,"purchase requires item and coin receipt");
        Require(TradeRoutine.VerifyReceipt(order,Stock(now,("coins",600)),Stock(now.AddSeconds(1),("coins",600),("iron axe",1))).Status!=RoutineStatus.Completed,
            "item gift cannot masquerade as a purchase");
        Require(TradeRoutine.VerifyReceipt(order with{Quantity=2},Stock(now,("coins",700)),Stock(now.AddSeconds(1),("coins",600),("iron axe",1))).Status==RoutineStatus.UnknownOutcome,
            "partial fill retains journal");
        var sell=order with{Sell=true,Item="leather gloves",UnitPrice=50};
        Require(TradeRoutine.VerifyReceipt(sell,Stock(now,("coins",100),("leather gloves",1)),Stock(now.AddSeconds(1),("coins",149))).Status==RoutineStatus.Completed,
            "sale receipt includes per-item rounded GE fee");
        string directory=Path.Combine(Path.GetTempPath(),"osrs-executor-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var journal=new TradeJournal(Path.Combine(directory,"trade.json"),"test-subject");
            journal.Prepare(order,before,new(300,150,5,5));
            var restored=new TradeJournal(Path.Combine(directory,"trade.json"),"test-subject");
            Require(restored.Pending()?.Order.Id==order.Id&&restored.Pending()?.Before.Count("coins")==1000,"restart preserves intent and baseline");
            blocked=false;try{restored.Prepare(order,before,new(300,150,5,5));}catch(RoutineBlockedException){blocked=true;}
            Require(blocked,"existing intent prevents duplicate submission");
            restored.Complete(order.Id);Require(restored.Pending() is null,"only verified completion clears pending intent");
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        Console.WriteLine("EXECUTION ROUTINES PASS: context selection, independent yields, recipe deltas, supply gates, GE invoices/receipts/partial fills, durable deduplication");
    }
    private sealed class InventoryMenuDriver:IVisualGameDriver
    {
        private readonly IReadOnlyList<VisualLabel> menu;
        private readonly bool closes;
        internal readonly List<GameInput> Inputs=new();
        private readonly ExecutionPixels before;
        private readonly ExecutionPixels covered;
        public VisualFrame Current {get;private set;}
        internal InventoryMenuDriver(IReadOnlyList<VisualLabel> labels,bool closes)
        {
            menu=labels;this.closes=closes;
            var slots=Enumerable.Range(0,28).Select(i=>
                new ObservedSlot(i,ExecutionUi.InventorySlot(i),(ulong)i+1,i>0)).ToArray();
            before=new ExecutionPixels(slots,1,InventoryTabActive:true,InventoryPanelSignature:813);
            covered=before with {InventoryPanelSignature=914,
                Slots=slots.Select((v,i)=>i<3?v with{Signature=v.Signature+1000}:v).ToArray()};
            Current=new(new IntPtr(1),new Rectangle(0,0,1000,600),
                DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),before);
        }
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            Current=Current with {CapturedUtc=Current.CapturedUtc.AddMilliseconds(200)};
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            Inputs.Add(input);
            if(input.Kind==GameInputKind.Hover)
                Current=Current with {Labels=new VisualLabel[]{
                    new("Inventory",new Rectangle(12,8,70,14))}};
            else if(input.Kind==GameInputKind.RightClick)
                Current=Current with {Labels=menu,Pixels=covered};
            else if(input.Kind==GameInputKind.LeftClick&&Inputs.Any(i=>i.Kind==GameInputKind.RightClick)&&
                menu.Any(l=>l.Text=="Cancel"&&l.Bounds==input.Bounds)&&closes)
                // Only panel chrome has changed. Slot sprites are identical
                // to the pre-menu frame even when the whole-panel hash differs.
                Current=Current with {Labels=Array.Empty<VisualLabel>(),
                    Pixels=before with{InventoryPanelSignature=999}};
            Current=Current with {CapturedUtc=Current.CapturedUtc.AddMilliseconds(200)};
            return Task.CompletedTask;
        }
    }

    private sealed class InventoryGridDriver:IVisualGameDriver
    {
        internal readonly List<GameInput> Inputs=new();
        public VisualFrame Current {get;private set;}
        internal InventoryGridDriver(bool pixelVerifiedEmpty,bool active=true)
        {
            var slots=Enumerable.Range(0,28).Select(i=>
                new ObservedSlot(i,ExecutionUi.InventorySlot(i),(ulong)i+1,pixelVerifiedEmpty)).ToArray();
            Current=new(new IntPtr(1),new Rectangle(0,0,1000,600),
                DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),
                new ExecutionPixels(slots,1,InventoryTabActive:active));
        }
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            Current=Current with {CapturedUtc=Current.CapturedUtc.AddSeconds(1)};
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            Inputs.Add(input);
            if(input.Kind==GameInputKind.Hover)
                Current=Current with{Labels=new[]{new VisualLabel("Inventory",new Rectangle(12,8,80,12))}};
            Current=Current with{CapturedUtc=Current.CapturedUtc.AddSeconds(1)};
            return Task.CompletedTask;
        }
    }

    private sealed class InventoryTabDriver:IVisualGameDriver
    {
        private readonly bool misplacedTooltip;
        internal readonly List<GameInput> Inputs=new();
        public VisualFrame Current {get;private set;}=
            new(new IntPtr(1),new Rectangle(0,0,1000,600),DateTimeOffset.UtcNow,
                Array.Empty<VisualLabel>());
        internal InventoryTabDriver(bool misplacedTooltip=false)
        {this.misplacedTooltip=misplacedTooltip;}
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)=>
            Task.FromResult(Current);
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            Inputs.Add(input);
            if(input.Kind==GameInputKind.Hover)
                Current=Current with{
                    Labels=new VisualLabel[]{new("Inventory",
                        misplacedTooltip?new Rectangle(800,320,75,12):new Rectangle(11,8,65,12))}
                };
            Current=Current with{CapturedUtc=DateTimeOffset.UtcNow};
            return Task.CompletedTask;
        }
    }
    private sealed class ScriptedDriver:IVisualGameDriver
    {
        public VisualFrame Current {get;private set;}
        internal readonly List<GameInput> Inputs=new();
        private readonly IReadOnlyList<VisualLabel> menu;
        private readonly int delayObservations;
        private bool waitingForMenu;
        internal int Observations {get;private set;}
        internal ScriptedDriver(IReadOnlyList<VisualLabel> menu,int delayObservations=0)
        {
            this.menu=menu;this.delayObservations=delayObservations;
            Current=new(new IntPtr(1),new(0,0,1000,600),DateTimeOffset.UtcNow,menu);
        }
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            Observations++;
            if(waitingForMenu&&Observations>=delayObservations)
            {
                waitingForMenu=false;
                Current=Current with {Labels=menu};
            }
            Current=Current with {CapturedUtc=Current.CapturedUtc.AddMilliseconds(200)};
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            Inputs.Add(input);
            if(input.Kind==GameInputKind.RightClick&&delayObservations>0)
            {
                waitingForMenu=true;Observations=0;
                Current=Current with {Labels=Array.Empty<VisualLabel>()};
            }
            if(input.Kind==GameInputKind.LeftClick&&
                menu.Any(l=>l.Text=="Cancel"&&l.Bounds==input.Bounds))
                Current=Current with {Labels=Array.Empty<VisualLabel>()};
            Current=Current with{CapturedUtc=Current.CapturedUtc.AddMilliseconds(200)};
            return Task.CompletedTask;
        }
    }
}
