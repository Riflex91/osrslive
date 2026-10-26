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
        Require(GameplayCoordinator.NewActivityPositionBlock(new(null,"NO_VERIFIED_MAP_MATCH"))==
            "AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT",
            "unknown world position blocks NEW activity before inventory context-menu input");
        var preflight=GameplayCoordinator.LocalizationEvidence("before_zoom",
            new(null,"NO_VERIFIED_MAP_MATCH",137,0,0,1));
        Require(preflight.Contains("phase=before_zoom status=NO_VERIFIED_MAP_MATCH")&&
            preflight.Contains("features=137 candidates=0 inliers=0 best_ratio_matches=1")&&
            preflight.EndsWith("no_input=true",StringComparison.Ordinal),
            "passive zoom comparison reports match counts without inventing a world tile or input");
        var position=new MapLocation(new(3153,3202),new[]{1d,0d,900d,0d,1d,1200d},
            new(804,12,144,144),20,now);
        Require(GameplayCoordinator.NewActivityPositionBlock(new(position,"VERIFIED")) is null,
            "verified map location permits normal downstream independent travel checks");
        VisualLabel L(string text,int x,int y,int width=180)=>new(text,new(x,y,width,14));
        var menu=new[]{L("Choose Option",30,30),L("Use Logs",30,50),L("Drop Logs",30,66),L("Examine Logs",30,82),L("Cancel",30,98),
            L("Deposit-All Logs",400,50)};
        ContextMenuOcr.SelfTest();
        NativeMenuHeaderReader.SelfTest();
        InventoryTabPolicy.SelfTest();
        NativeInventoryEmptyReader.SelfTest();
        // Real regression: just after opening Inventory, one independent
        // post-hover frame temporarily lacked recognizable HUD/footer labels.
        // Re-observe pixels without sending another click, then require a
        // NEW InGame frame before continuing with the inventory scan.
        var hudLabels=new[]{new VisualLabel("NATIVE_GAME_HUD",
            new Rectangle(0,575,517,25),VisualLabelKind.GameHud)};
        var initialHudFrame=new VisualFrame(new IntPtr(11),new Rectangle(0,0,1000,600),
            DateTimeOffset.UtcNow,hudLabels);
        int transientCaptures=0,transientDispatches=0;
        var reports=new List<string>();
        var transientHud=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            transientCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                CapturedUtc=DateTimeOffset.UtcNow,
                Labels=transientCaptures==1?Array.Empty<VisualLabel>():hudLabels});
        },(_,input,token)=>{
            transientDispatches++;return Task.FromResult(true);
        },reports.Add);
        var recoveredHud=await transientHud.ObserveAsync(CancellationToken.None);
        Require(transientCaptures==2&&transientDispatches==0&&
            GameScreenReader.Read(recoveredHud).Stage==VisibleGameStage.InGame&&
            reports.Count==1&&reports[0].StartsWith("HUD_REOBSERVING",StringComparison.Ordinal),
            "one transient unrecognized HUD frame is re-observed without any new input");
        int missingHudCaptures=0;
        var missingHud=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            missingHudCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                CapturedUtc=DateTimeOffset.UtcNow,
                Labels=Array.Empty<VisualLabel>()});
        },(_,input,token)=>Task.FromResult(true),_=>{});
        bool missingHudBlocked=false;
        try {await missingHud.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex){missingHudBlocked=ex.Message=="GAMEPLAY_HUD_NOT_VERIFIED";}
        Require(missingHudBlocked&&missingHudCaptures==3&&
            ReferenceEquals(missingHud.Current,initialHudFrame),
            "three unreadable HUD frames block without replacing the last verified frame");
        int staleCaptures=0,staleDispatches=0;
        var staleReports=new List<string>();
        var staleOnce=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            staleCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                CapturedUtc=staleCaptures==1?DateTimeOffset.UtcNow.AddSeconds(-5):
                    DateTimeOffset.UtcNow,Labels=hudLabels});
        },(_,input,token)=>{
            staleDispatches++;return Task.FromResult(true);
        },staleReports.Add);
        var refreshed=await staleOnce.ObserveAsync(CancellationToken.None);
        Require(staleCaptures==2&&staleDispatches==0&&
            GameScreenReader.Read(refreshed).Stage==VisibleGameStage.InGame&&
            staleReports.Count==1&&staleReports[0].StartsWith("FRAME_REOBSERVING",StringComparison.Ordinal),
            "slow OCR rejects stale captured pixels and observes a fresh frame without another click");
        int persistentlyStaleCount=0;
        var persistentlyStaleReports=new List<string>();
        var persistentlyStale=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            persistentlyStaleCount++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                CapturedUtc=DateTimeOffset.UtcNow.AddSeconds(-5)});
        },(_,input,token)=>Task.FromResult(true),persistentlyStaleReports.Add);
        bool staleBlocked=false;
        try{await persistentlyStale.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {staleBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(staleBlocked&&persistentlyStaleCount==3&&
            ReferenceEquals(persistentlyStale.Current,initialHudFrame)&&
            persistentlyStaleReports.Any(s=>s.Contains("reason=PERSISTENT_STALE_OCR_CAPTURE")),
            "persistently stale pixel captures must block without replacing the verified frame");
        int wrongWindowCount=0;
        var wrongWindowReports=new List<string>();
        var wrongWindow=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            wrongWindowCount++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                Window=new IntPtr(12),CapturedUtc=DateTimeOffset.UtcNow});
        },(_,input,token)=>Task.FromResult(true),wrongWindowReports.Add);
        bool geometryBlocked=false;
        try{await wrongWindow.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {geometryBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(geometryBlocked&&wrongWindowCount==1&&
            wrongWindowReports.Single().Contains("reason=GAME_WINDOW_CHANGED"),
            "new window handles remain an immediate hard stop without retry");
        var movedViewportReports=new List<string>();
        var movedViewport=new WindowVisualGameDriver(initialHudFrame,_=>
            Task.FromResult<VisualFrame?>(initialHudFrame with{
                ScreenBounds=new(6,0,1000,600),CapturedUtc=DateTimeOffset.UtcNow}),
            (_,input,token)=>Task.FromResult(true),movedViewportReports.Add);
        bool movedViewportBlocked=false;
        try{await movedViewport.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {movedViewportBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(movedViewportBlocked&&movedViewportReports.Single().Contains("reason=GAME_VIEWPORT_CHANGED")&&
            movedViewportReports.Single().Contains("origin_changed=True"),
            "changed viewport origin remains an immediate stop with a distinct privacy-safe diagnostic");
        int preInputMoveCaptures=0,preInputMoveDispatches=0;
        var preInputMoveReports=new List<string>();
        var preInputMove=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            preInputMoveCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                ScreenBounds=new(preInputMoveCaptures<=2?12:13,8,1000,600),CapturedUtc=DateTimeOffset.UtcNow});
        },(_,input,token)=>{
            preInputMoveDispatches++;return Task.FromResult(true);
        },preInputMoveReports.Add){AllowPreInputOriginReanchor=true};
        var anchored=await preInputMove.ObserveAsync(CancellationToken.None);
        Require(preInputMoveCaptures==2&&preInputMoveDispatches==0&&
            anchored.ScreenBounds==new Rectangle(12,8,1000,600)&&
            anchored.CapturedUtc>initialHudFrame.CapturedUtc&&
            preInputMoveReports.Single().Contains("independent_fresh_ingame_frames=2"),
            "before any attempted input a stable move may reanchor from two independent InGame frames without clicks");
        bool secondPreInputMoveBlocked=false;
        try{await preInputMove.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {secondPreInputMoveBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(secondPreInputMoveBlocked&&preInputMoveCaptures==3&&preInputMoveDispatches==0&&
            ReferenceEquals(preInputMove.Current,anchored),
            "a second origin shift still blocks, even before any attempted input");
        int unstableMoveCaptures=0;
        var unstableMove=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            unstableMoveCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                ScreenBounds=new(unstableMoveCaptures==1?12:13,8,1000,600),
                CapturedUtc=DateTimeOffset.UtcNow});
        },(_,input,token)=>Task.FromResult(true),_=>{}){AllowPreInputOriginReanchor=true};
        bool unstableMoveBlocked=false;
        try{await unstableMove.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {unstableMoveBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(unstableMoveBlocked&&unstableMoveCaptures==2&&
            ReferenceEquals(unstableMove.Current,initialHudFrame),
            "moving-again viewport cannot be reanchored from inconsistent origins");
        var repeatedTimestamp=DateTimeOffset.UtcNow;
        var duplicateMove=new WindowVisualGameDriver(initialHudFrame,_=>
            Task.FromResult<VisualFrame?>(initialHudFrame with{
                ScreenBounds=new(12,8,1000,600),CapturedUtc=repeatedTimestamp}),
            (_,input,token)=>Task.FromResult(true),_=>{}){AllowPreInputOriginReanchor=true};
        bool duplicateMoveBlocked=false;
        try{await duplicateMove.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {duplicateMoveBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(duplicateMoveBlocked&&ReferenceEquals(duplicateMove.Current,initialHudFrame),
            "two screenshots with identical timestamps do not establish a stable new origin");
        int movedAfterInputCaptures=0,movedAfterInputDispatches=0;
        var movedAfterInputReports=new List<string>();
        var beforeInputMove=initialHudFrame with{CapturedUtc=DateTimeOffset.UtcNow};
        var movedAfterInput=new WindowVisualGameDriver(beforeInputMove,_=>
        {
            movedAfterInputCaptures++;
            return Task.FromResult<VisualFrame?>(beforeInputMove with{
                ScreenBounds=new(12,8,1000,600),CapturedUtc=DateTimeOffset.UtcNow});
        },(_,input,token)=>{
            movedAfterInputDispatches++;return Task.FromResult(true);
        },movedAfterInputReports.Add){AllowPreInputOriginReanchor=true};
        bool postInputMoveBlocked=false;
        try{await movedAfterInput.InputAsync(new(GameInputKind.Hover,new(900,140,8,8)),CancellationToken.None);}
        catch(RoutineBlockedException ex)
        {postInputMoveBlocked=ex.Message=="FRAME_WINDOW_GEOMETRY_OR_FRESHNESS_CHANGED";}
        Require(postInputMoveBlocked&&movedAfterInputCaptures==1&&movedAfterInputDispatches==1&&
            ReferenceEquals(movedAfterInput.Current,beforeInputMove)&&
            movedAfterInputReports.Any(s=>s.Contains("reason=GAME_VIEWPORT_CHANGED")),
            "after the first attempted input a moved window always blocks without reanchoring");
        int reusedHudCaptures=0;
        var reusedTime=DateTimeOffset.UtcNow;
        var reusedHud=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            reusedHudCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                CapturedUtc=reusedHudCaptures<3?reusedTime:reusedTime.AddMilliseconds(200),
                Labels=reusedHudCaptures==1?Array.Empty<VisualLabel>():hudLabels});
        },(_,input,token)=>Task.FromResult(true),_=>{});
        var freshHud=await reusedHud.ObserveAsync(CancellationToken.None);
        Require(reusedHudCaptures==3&&freshHud.CapturedUtc>reusedTime,
            "a reused timestamp cannot turn a transient Unknown frame into new HUD evidence");
        int otherScreenCaptures=0;
        var otherScreen=new WindowVisualGameDriver(initialHudFrame,_=>
        {
            otherScreenCaptures++;
            return Task.FromResult<VisualFrame?>(initialHudFrame with{
                CapturedUtc=DateTimeOffset.UtcNow,
                Labels=new[]{new VisualLabel("Select a world",new Rectangle(10,10,140,14))}});
        },(_,input,token)=>Task.FromResult(true),_=>{});
        bool leftGameBlocked=false;
        try{await otherScreen.ObserveAsync(CancellationToken.None);}
        catch(RoutineBlockedException ex){leftGameBlocked=ex.Message=="GAME_SESSION_LEFT_EXECUTION_SCREEN";}
        Require(leftGameBlocked&&otherScreenCaptures==1,
            "world selection or another known non-game screen must abort immediately");
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
        var lateEmptyDriver=new InventoryGridDriver(true,lateEmptyFirstSlot:true);
        var lateEmpty=await new GameInteractions(lateEmptyDriver).Inventory(CancellationToken.None);
        Require(lateEmpty.EmptySlots==28&&lateEmpty.Items.Count==0&&
            lateEmptyDriver.Observations>=4&&
            lateEmptyDriver.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
            "empty consensus appearing after the baseline is reverified twice, without a right-click");
        var unstableLateEmpty=new InventoryGridDriver(true,lateEmptyFirstSlot:true,
            lateEmptyDisappears:true);
        bool unstableLateBlocked=false;
        try{await new GameInteractions(unstableLateEmpty).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){unstableLateBlocked=ex.Message=="INVENTORY_LATE_EMPTY_NOT_STABLE";}
        Require(unstableLateBlocked&&unstableLateEmpty.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
            "a late empty classification that disappears must block before any menu attempt");
        var unknownDriver=new InventoryGridDriver(false);
        bool unknownBlocked=false;
        try{await new GameInteractions(unknownDriver).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){unknownBlocked=ex.Message=="CONTEXT_MENU_NOT_READABLE";}
        int unknownRight=unknownDriver.Inputs.FindIndex(i=>i.Kind==GameInputKind.RightClick);
        Require(unknownBlocked&&unknownRight>0&&
            unknownDriver.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1&&
            unknownDriver.Inputs[unknownRight-1].Kind==GameInputKind.Hover&&
            ExecutionUi.InventorySlot(0).Contains(new Point(
                unknownDriver.Inputs[unknownRight-1].Bounds.Left+16,
                unknownDriver.Inputs[unknownRight-1].Bounds.Top+16)),
            "unverified slots get one staged hover and one right-click; a missing menu never proves empty");
        var settling=new InventoryGridDriver(false,changeFirstFrame:true);
        bool settlingMenuBlocked=false;
        try{await new GameInteractions(settling).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){settlingMenuBlocked=ex.Message=="CONTEXT_MENU_NOT_READABLE";}
        Require(settlingMenuBlocked&&settling.ObservationsAtRightClick>=3&&
            settling.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1,
            "changed slots 2/3 during initial rendering require three matching neutral frames before one click");
        var changing=new InventoryGridDriver(false,changeEveryFrame:true);
        bool unstableBlocked=false;
        try{await new GameInteractions(changing).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){unstableBlocked=ex.Message=="INVENTORY_BASELINE_NOT_STABLE";}
        Require(unstableBlocked&&changing.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
            "continually changing inventory pixels must block before any right-click");
        var lostTab=new InventoryGridDriver(false,loseTabOnSlotHover:true);
        bool lostTabBlocked=false;
        try{await new GameInteractions(lostTab).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){lostTabBlocked=ex.Message=="INVENTORY_TAB_LOST_BEFORE_RIGHT_CLICK";}
        Require(lostTabBlocked&&lostTab.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
            "loss of the selected Inventory tab during staging must prevent the right-click");
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
        Require(GameInteractions.InventoryBaselineStable(basePixels,basePixels)&&
            !GameInteractions.InventoryBaselineStable(basePixels,severalChanged)&&
            !GameInteractions.InventoryBaselineStable(basePixels,oneTransient)&&
            !GameInteractions.InventoryBaselineStable(basePixels,basePixels with{InventoryTabActive=false}),
            "inventory reference must stabilize every slot before the first right-click");
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
        Require(firstRightClick>1&&lastNeutralBeforeClick==firstRightClick-2&&
            cancellable.Inputs[firstRightClick-1].Kind==GameInputKind.Hover&&
            cancellable.Inputs[firstRightClick-1].Bounds==ExecutionUi.InventorySlot(0),
            "neutral baseline must precede a target hover and exactly one right-click");
        Require(checkedInventory.Items.Count==1&&checkedInventory.Items[0].Name=="logs"&&
            checkedInventory.EmptySlots==27&&
            cancellable.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1&&
            cancellable.Inputs.Count(i=>i.Kind==GameInputKind.LeftClick)==2&&
            cancellable.Inputs.All(i=>i.Kind!=GameInputKind.Escape),
            "menu Cancel restores verified inventory before scanning other slots"+
            $" items={checkedInventory.Items.Count} name={(checkedInventory.Items.FirstOrDefault()?.Name??"none")}"+
            $" empty={checkedInventory.EmptySlots} right={cancellable.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)}"+
            $" left={cancellable.Inputs.Count(i=>i.Kind==GameInputKind.LeftClick)}");
        var cancelOnlyLabels=new[]{L("Choose Option",30,30),L("Cancel",30,98)};
        string cacheDirectory=Path.Combine(Path.GetTempPath(),"osrs-cache-integration-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var knowledge=new ItemKnowledgeStore(Path.Combine(cacheDirectory,"items.sqlite"));
            var cold=new InventoryMenuDriver(menu,true,new string('A',64));
            await new GameInteractions(cold,knowledge).Inventory(CancellationToken.None);
            var warm=new InventoryMenuDriver(menu,true,new string('A',64),cold.Current.CapturedUtc.AddSeconds(1));
            var reused=await new GameInteractions(warm,knowledge).Inventory(CancellationToken.None);
            Require(reused.Items.Single().Name=="logs"&&warm.Inputs.All(i=>i.Kind!=GameInputKind.RightClick),
                "a new task-cycle interaction reuses a full-pixel identity without opening any item menu");
            var changed=new InventoryMenuDriver(menu,true,new string('B',64),warm.Current.CapturedUtc.AddSeconds(1));
            await new GameInteractions(changed,knowledge).Inventory(CancellationToken.None);
            Require(changed.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1,
                "changed full pixels force menu inspection despite unchanged legacy sampled signature");
            knowledge.ResetSession();
            var partial=new InventoryMenuDriver(menu,true,new string('A',64),unreadableSecond:true);
            bool partialBlocked=false;
            try{await new GameInteractions(partial,knowledge).Inventory(CancellationToken.None);}
            catch(RoutineBlockedException ex){partialBlocked=ex.Message=="CONTEXT_MENU_NOT_READABLE";}
            Require(partialBlocked&&knowledge.Recognize(partial.Current.Pixels!.Slots[0],partial.Current.CapturedUtc)?.Name=="logs"&&
                knowledge.Recognize(partial.Current.Pixels.Slots[1],partial.Current.CapturedUtc) is null,
                "a later slot failure preserves only earlier independently confirmed identities, never the unreadable item");
        }
        finally{if(Directory.Exists(cacheDirectory))Directory.Delete(cacheDirectory,true);}
        var cancelOnlyDriver=new InventoryMenuDriver(cancelOnlyLabels,closes:true);
        bool cancelOnlyBlocked=false;
        try{await new GameInteractions(cancelOnlyDriver).Inventory(CancellationToken.None);}
        catch(RoutineBlockedException ex){cancelOnlyBlocked=ex.Message=="AMBIGUOUS_OR_NON_INVENTORY_SLOT";}
        Require(cancelOnlyBlocked&&
            cancelOnlyDriver.Inputs.Count(i=>i.Kind==GameInputKind.RightClick)==1&&
            cancelOnlyDriver.Inputs.Any(i=>i.Kind==GameInputKind.LeftClick&&
                i.Bounds==cancelOnlyLabels[1].Bounds),
            "Cancel-only menu must be safely dismissed, never treated as an empty inventory slot");
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
        private readonly bool unreadableSecond;
        internal readonly List<GameInput> Inputs=new();
        private readonly ExecutionPixels before;
        private readonly ExecutionPixels covered;
        public VisualFrame Current {get;private set;}
        internal InventoryMenuDriver(IReadOnlyList<VisualLabel> labels,bool closes,string? digest=null,DateTimeOffset? captured=null,bool unreadableSecond=false)
        {
            menu=labels;this.closes=closes;this.unreadableSecond=unreadableSecond;
            var slots=Enumerable.Range(0,28).Select(i=>
                new ObservedSlot(i,ExecutionUi.InventorySlot(i),(ulong)i+1,i>0&&!(unreadableSecond&&i==1),
                    IdentityDigest:i==0?digest:unreadableSecond&&i==1?new string('B',64):null)).ToArray();
            before=new ExecutionPixels(slots,1,InventoryTabActive:true,InventoryPanelSignature:813);
            covered=before with {InventoryPanelSignature=914,
                Slots=slots.Select((v,i)=>i<3?v with{Signature=v.Signature+1000}:v).ToArray()};
            Current=new(new IntPtr(1),new Rectangle(0,0,1000,600),
                captured??DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),before);
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
                Current=Current with {Labels=unreadableSecond&&input.Bounds==ExecutionUi.InventorySlot(1)?
                    Array.Empty<VisualLabel>():menu,Pixels=covered};
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
        private readonly bool loseTabOnSlotHover,changeFirstFrame,changeEveryFrame;
        private readonly bool lateEmptyFirstSlot,lateEmptyDisappears;
        internal int Observations {get;private set;}
        internal int ObservationsAtRightClick {get;private set;}=-1;
        internal InventoryGridDriver(bool pixelVerifiedEmpty,bool active=true,bool loseTabOnSlotHover=false,
            bool changeFirstFrame=false,bool changeEveryFrame=false,bool lateEmptyFirstSlot=false,
            bool lateEmptyDisappears=false)
        {
            this.loseTabOnSlotHover=loseTabOnSlotHover;
            this.changeFirstFrame=changeFirstFrame;
            this.changeEveryFrame=changeEveryFrame;
            this.lateEmptyFirstSlot=lateEmptyFirstSlot;
            this.lateEmptyDisappears=lateEmptyDisappears;
            var slots=Enumerable.Range(0,28).Select(i=>
                new ObservedSlot(i,ExecutionUi.InventorySlot(i),(ulong)i+1,
                    pixelVerifiedEmpty&&!(lateEmptyFirstSlot&&i==0))).ToArray();
            Current=new(new IntPtr(1),new Rectangle(0,0,1000,600),
                DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),
                new ExecutionPixels(slots,1,InventoryTabActive:active));
        }
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            Observations++;
            if(changeEveryFrame||changeFirstFrame&&Observations==1)
            {
                var pixels=Current.Pixels!;
                Current=Current with{Pixels=pixels with{Slots=pixels.Slots.Select((slot,i)=>
                    i is 2 or 3?slot with{Signature=slot.Signature+100}:slot).ToArray()}};
            }
            if(lateEmptyFirstSlot&&(Observations==2||lateEmptyDisappears&&Observations==3))
            {
                var pixels=Current.Pixels!;
                bool empty=Observations==2;
                Current=Current with{Pixels=pixels with{Slots=pixels.Slots.Select((slot,i)=>
                    i==0?slot with{Empty=empty}:slot).ToArray()}};
            }
            Current=Current with {CapturedUtc=Current.CapturedUtc.AddSeconds(1)};
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {
            Inputs.Add(input);
            if(input.Kind==GameInputKind.RightClick)ObservationsAtRightClick=Observations;
            if(input.Kind==GameInputKind.Hover)
            {
                Current=Current with{Labels=new[]{new VisualLabel("Inventory",new Rectangle(12,8,80,12))}};
                if(loseTabOnSlotHover&&input.Bounds==ExecutionUi.InventorySlot(0))
                {
                    var pixels=Current.Pixels!;
                    Current=Current with{Pixels=pixels with{InventoryTabActive=false}};
                }
            }
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
