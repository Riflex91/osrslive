using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OSRSLiveDiagnostics;

internal enum VisualLabelKind { Text, Number, WorldCell, SkillValue, WorldNavigation, InventoryQuantity, PlayButton, EnterButton, Hitpoints, Minimap, GameHud, SkillsButton, ConnectionSignal, CharacterPrompt, CharacterSummary, CharacterSummaryName, ContextMenuLine, ContextMenuCandidate }
internal sealed record VisualLabel(string Text,Rectangle Bounds,VisualLabelKind Kind=VisualLabelKind.Text);
internal sealed record VisualFrame(IntPtr Window,Rectangle ScreenBounds,
    DateTimeOffset CapturedUtc,IReadOnlyList<VisualLabel> Labels,ExecutionPixels? Pixels=null);
internal sealed record VisualDecision(string State,Rectangle? Click=null,bool MoveOnly=false,GameInput? RuntimeInput=null);

// Moving the window-directed mouse to this neutral game-view coordinate after
// opening Skills removes any mouseover tooltip before the next OCR frame.
// It is outside the skills panel, minimap, chat and interactive tab buttons.
internal static class SkillsHoverPolicy
{
    internal static readonly Point RestPoint=new(500,400);
    internal static readonly Rectangle RestTarget=new(RestPoint.X-2,RestPoint.Y-2,4,4);
    internal static bool AfterOpen(string action,GameInputKind kind,Size size)=>
        (action=="OPEN_SKILLS"||action=="OPEN_SKILLS_FROM_SUMMARY")&&kind==GameInputKind.LeftClick&&size==DockedWindowLayout.GameSize;
    internal static bool NeedsClear(bool alreadyCleared,VisibleGameObservation observation,Size size)=>
        !alreadyCleared&&size==DockedWindowLayout.GameSize&&
        observation.Stage==VisibleGameStage.InGame&&observation.TotalLevel is not null&&
        (observation.RecognizedSkills!=24||observation.Skills is null);
}

internal static class NavigationStartupPolicy
{
    internal static string? GameplayBlock(bool hasSubject,RoutineResult? worldMap,RoutineResult? zoom,bool mapOpen)
    {
        if(hasSubject&&worldMap?.Status!=RoutineStatus.Completed)
            return worldMap is null?"WAITING_FOR_WORLD_MAP_LOCALIZATION":"BLOCKED_"+worldMap.Reason;
        if(hasSubject&&zoom?.Status!=RoutineStatus.Completed)
            return zoom is null?"WAITING_FOR_MINIMAP_CALIBRATION":"BLOCKED_"+zoom.Reason;
        return mapOpen?"BLOCKED_WORLD_MAP_STILL_OPEN":null;
    }
}


internal static class WorldFooterPolicy
{
    internal static VisualLabel? SwitchMarker(IReadOnlyList<VisualLabel> labels,Size imageSize)
    {
        var switches=labels.Where(l=>Regex.IsMatch(l.Text.Trim(),@"^Click\s*(?:to\s*)?switch$",RegexOptions.IgnoreCase)&&
            l.Bounds.Left<imageSize.Width/2&&l.Bounds.Top>imageSize.Height-85).ToArray();
        return switches.Length==1?switches[0]:null;
    }
    internal static IReadOnlyList<VisualLabel> Read(IReadOnlyList<VisualLabel> labels,Size imageSize)
    {
        if(SwitchMarker(labels,imageSize) is not VisualLabel marker)return Array.Empty<VisualLabel>();
        var numbers=labels.Where(l=>Regex.IsMatch(l.Text.Trim(),@"^[3-8]\d{2}$")&&
            int.Parse(l.Text.Trim())<=800&&l.Bounds.Bottom<=marker.Bounds.Top+3&&
            marker.Bounds.Top-l.Bounds.Bottom<30&&l.Bounds.Left<marker.Bounds.Right&&l.Bounds.Right>marker.Bounds.Left).ToArray();
        return numbers.Length==1?new[]{new VisualLabel("World "+numbers[0].Text.Trim(),numbers[0].Bounds)}:
            Array.Empty<VisualLabel>();
    }
}

// Only known startup screens are actionable. OCR content is never logged.
internal static class VisualStartupPolicy
{
    // The native footer may put the world and "Click to switch" on one OCR
    // line, and the pixel font's lowercase l is often read as I/1/i.
    private static readonly Regex World=new(@"^W[o0]r[lI1i]d\s*([3-8]\d{2})(?:\s+Click to switch)?$",RegexOptions.IgnoreCase);
    internal static bool PageCoverageVerified(IReadOnlyList<VisualLabel> cells)
    {
        // Calibrated 1000x600 native world table. Require validated cells from
        // both edges AND the middle before claiming that a page boundary has
        // been reached. This is deliberately stricter than target selection.
        if(cells.Count<20)return false;
        var xs=cells.Select(c=>c.Bounds.Left).ToArray();
        return xs.Min()<260&&xs.Max()>690&&
            cells.GroupBy(c=>c.Bounds.Left/40).Count()>=5;
    }
    internal static VisualDecision Decide(IReadOnlyList<VisualLabel> labels,int? preferredWorld,
        bool selectedWorldPreviouslyObserved=false)
    {
        string Normalize(string s)=>Regex.Replace(s.Trim(),@"\s+"," ");
        var normalized=labels.Select(l=>l with {Text=Normalize(l.Text)}).ToArray();
        VisualLabel? Unique(Func<VisualLabel,bool> match)
        {
            var hits=normalized.Where(match).ToArray();
            return hits.Length==1?hits[0]:null;
        }
        bool Has(string text)=>normalized.Any(l=>l.Text.Contains(text,StringComparison.OrdinalIgnoreCase));
        if(Has("password")||Has("authenticator")||Has("verification code")||Has("Passwort"))
            return new("MANUAL_AUTHENTICATION_REQUIRED");
        if(preferredWorld is null)return new("WAITING_FOR_F2P_SCAN");
        bool selection=Has("Select a world") ||
            (Has("World")&&Has("Players")&&Has("Location")&&Has("Type"));
        if(selection)
        {
            var gridTarget=Unique(l=>l.Kind==VisualLabelKind.WorldCell&&l.Text==preferredWorld.Value.ToString());
            if(gridTarget is not null)return new("SELECT_F2P_WORLD",gridTarget.Bounds);
            var verifiedCells=normalized.Where(l=>l.Kind==VisualLabelKind.WorldCell).ToArray();
            var worldIds=verifiedCells.Select(l=>int.Parse(l.Text)).ToArray();
            if(worldIds.Length>0)
            {
                string direction=preferredWorld>worldIds.Max()?"NEXT":preferredWorld<worldIds.Min()?"PREVIOUS":"";
                // A few cells from the leftmost column do NOT establish the
                // maximum world on the page. World 399 can be visible even if
                // OCR only reads 301-325. Never paginate on partial coverage.
                if(direction.Length>0&&!PageCoverageVerified(verifiedCells))
                    return new("WORLD_PAGE_COVERAGE_INSUFFICIENT_NO_BLIND_ARROW");
                var arrow=Unique(l=>l.Kind==VisualLabelKind.WorldNavigation&&l.Text==direction);
                if(direction.Length>0&&arrow is not null)
                    return new($"WORLD_PAGE_{direction}_{worldIds.Min()}_{worldIds.Max()}",arrow.Bounds);
                return new("TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE");
            }
            var headers=normalized.Where(l=>l.Text.Equals("World",StringComparison.OrdinalIgnoreCase)).ToArray();
            var target=Unique(l=>l.Text==preferredWorld.Value.ToString()&&headers.Any(h=>
                l.Bounds.Top>h.Bounds.Bottom&&Math.Abs(l.Bounds.Left-h.Bounds.Left)<Math.Max(30,h.Bounds.Width)));
            return target is null?new("TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE"):
                new("SELECT_F2P_WORLD",target.Bounds);
        }
        // The second native entry screen has a parchment welcome banner and
        // a red center tile. Process it BEFORE the first Play Now screen, since
        // both can contain "Welcome to Old School RuneScape" in OCR results.
        var nativeEnter=Unique(l=>l.Kind==VisualLabelKind.EnterButton);
        if(nativeEnter is not null)
            return selectedWorldPreviouslyObserved?
                new("ENTER_GAME",nativeEnter.Bounds):new("WELCOME_WORLD_NOT_VERIFIED");
        var secondEntry=Unique(l=>l.Text.Equals("CLICK HERE TO PLAY",StringComparison.OrdinalIgnoreCase));
        if(secondEntry is not null&&Has("Welcome")&&!Has("Play Now"))
            return selectedWorldPreviouslyObserved?
                new("ENTER_GAME",secondEntry.Bounds):new("WELCOME_WORLD_NOT_VERIFIED");
        bool welcome=Has("Welcome to Old School RuneScape");
        if(welcome)
        {
            var worlds=normalized.Where(l=>World.IsMatch(l.Text))
                .GroupBy(l=>World.Match(l.Text).Groups[1].Value).ToArray();
            // A line and its word-level OCR can describe the same footer.
            // Different world numbers remain ambiguous and never authorize input.
            var world=worlds.Length==1?worlds[0].OrderBy(l=>l.Bounds.Width).First():null;
            if(world is null)return new("WORLD_LABEL_NOT_RECOGNIZED");
            int selected=int.Parse(World.Match(world.Text).Groups[1].Value);
            if(selected!=preferredWorld)return new("OPEN_WORLD_SELECTION",world.Bounds);
            // Prefer the strongly validated native image evidence. A full-
            // frame OCR hit is still supported for alternate DPI appearances.
            var guarded=Unique(l=>l.Kind==VisualLabelKind.PlayButton);
            var play=guarded??Unique(l=>l.Text.Equals("Play Now",StringComparison.OrdinalIgnoreCase));
            return play is null?new("PLAY_BUTTON_NOT_RECOGNIZED"):
                new("PLAY_SELECTED_CHARACTER",play.Bounds);
        }
        return new("WAITING_FOR_RECOGNIZED_SCREEN");
    }
}

// A post-entry screen that remains unknown must stop in a visible,
// recoverable, fail-closed state. Only fresh recognized UI can authorize input.
internal static class PostEnterUnknownScreenPolicy
{
    internal static readonly TimeSpan DiagnosticAfter=TimeSpan.FromSeconds(12);
    internal static readonly TimeSpan BlockAfter=TimeSpan.FromSeconds(30);
    internal static bool IsUnknownAfterEnter(string previousAction,DateTimeOffset lastAction,
        DateTimeOffset now,VisibleGameStage stage)=>
        previousAction=="ENTER_GAME"&&lastAction!=DateTimeOffset.MinValue&&
        now>=lastAction&&stage==VisibleGameStage.Unknown;
    internal static bool ShouldBlock(string previousAction,DateTimeOffset lastAction,
        DateTimeOffset now,VisibleGameStage stage,string decisionState)=>
        IsUnknownAfterEnter(previousAction,lastAction,now,stage)&&
        now-lastAction>=BlockAfter&&decisionState=="WAITING_FOR_RECOGNIZED_SCREEN";
}

// In an already-open native inventory context menu the item quantities and
// character/skill OCR crops are obscured or irrelevant. Preserve the complete
// game-HUD read if independent native HUD pixels are not confirmed.
internal static class InventoryMenuOcrPolicy
{
    internal static bool IsContext(Bitmap bitmap,Point? anchor)=>
        anchor is Point point&&bitmap.Size==DockedWindowLayout.GameSize&&
        InventoryTabPolicy.Active(bitmap)&&
        Enumerable.Range(0,28).Any(i=>ExecutionUi.InventorySlot(i).Contains(point));
    internal static bool FastHud(bool inventoryContext,bool nativeHudVerified)=>
        inventoryContext&&nativeHudVerified;
}

internal sealed class VisualStartup:IDisposable
{
    private readonly Diagnostics log;
    private readonly OcrEngine? ocr;
    private DateTimeOffset lastAction=DateTimeOffset.MinValue;
    private string lastState="";
    private static string footerDiagnostic="not_read";
    private int repeatedActions;
    private bool skillsHoverCleared;
    private readonly CharacterSummaryIdentity summaryIdentity=new();
    private int pageActions;
    private string previousAction="";
    private string lastWorldGridDiagnostic="";
    private string lastSkillOcrDiagnostic="";
    private Point? contextMenuAnchor;
    private string lastHealthDiagnostic="";
    private ulong? contextMenuPanelBefore;
    private string lastContextDiagnostic="";
    private int? pendingWorldChoice;
    private Rectangle? pendingWorldChoiceBounds;
    private DateTimeOffset pendingWorldChoiceFrame;
    private Rectangle? pendingPlayBounds,pendingEnterBounds;
    private DateTimeOffset pendingPlayFrame,pendingEnterFrame;
    private string lastPostPlayDiagnostic="";
    private string lastRecoveryOcrDiagnostic="";
    private DateTimeOffset nextWorldFooterDiagnostic;
    private DateTimeOffset lastPostPlayDiagnosticAt=DateTimeOffset.MinValue;
    private WelcomePanelPixels lastWelcomePixels;
    private GameHudFooterPixels lastHudFooterPixels;
    private bool postEnterUnknownDiagnosed;
    private int? observedSelectedWorld;
    private IntPtr observedWindow;
    private readonly BackgroundWindowCapture backgroundCapture=new();
    private readonly SessionRecovery recovery=new();
    private readonly ConnectivityWatch connectivity=new();
    private RoutineResult? minimapZoomResult;
    private RoutineResult? worldMapResult;
    private bool worldMapReading;
    private string lastWorldMapDiagnostic="";
    private void ResetNavigationCalibration(){minimapZoomResult=null;worldMapResult=null;}
    private string lastRecoveryState="";
    private DateTimeOffset lastVerifiedHud;
    internal string CaptureState=>backgroundCapture.State;
    internal string CaptureGeometry=>backgroundCapture.Geometry;
    internal string CaptureAlpha=>backgroundCapture.AlphaState;
    internal readonly OfficialLiveRenderObserver RenderObserver;
    internal readonly GameObservationTracker Game=new();
    internal readonly GameplayCoordinator Gameplay;
    // Called immediately after independent in-game OCR confirmation, before
    // potentially long-running inventory/production executor work.
    internal Action? VisuallyConfirmedSession;
    private bool paused;
    internal bool Paused
    {
        get=>paused;
        set
        {
            if(paused&&!value&&minimapZoomResult?.Status==RoutineStatus.Blocked)
                ResetNavigationCalibration(); // Manual resume retries a blocked calibration.
            if(paused&&!value&&worldMapResult?.Status==RoutineStatus.Blocked)worldMapResult=null;
            paused=value;
        }
    }
    internal bool ObserveOnly {get;set;}
    internal string State {get;private set;}="WAITING_FOR_WINDOW";
    internal VisualStartup(Diagnostics log,OfficialLiveRenderObserver? renderObserver=null)
    {
        this.log=log;
        Gameplay=new(log);
        RenderObserver=renderObserver??new();
        try{ocr=OcrEngine.TryCreateFromLanguage(new Language("en-US")) ??
            OcrEngine.TryCreateFromUserProfileLanguages();}
        catch(Exception ex)
        {log.Add(Severity.WARN,"VISUAL_STARTUP","OCR_UNAVAILABLE","reason="+ex.GetType().Name);}
    }

    internal async Task TickAsync(IntPtr target,OfficialWindowKind? kind,IntPtr owner,
        int? preferredWorld,CancellationToken ct)
    {
        if((GetAsyncKeyState(0x77)&0x8000)!=0)Paused=true; // F8 emergency pause
        if(Paused){SetState("PAUSED");return;}
        if(target==IntPtr.Zero||!IsWindow(target))
        {backgroundCapture.Release();ResetNavigationCalibration();SetState("WAITING_FOR_WINDOW");return;}
        if(observedWindow!=target)
        {observedWindow=target;observedSelectedWorld=null;Game.Reset();Gameplay.Reset();ResetNavigationCalibration();recovery.Reset();connectivity.Reset();lastRecoveryState="";lastVerifiedHud=default;previousAction="";lastAction=DateTimeOffset.MinValue;repeatedActions=0;pageActions=0;lastWorldGridDiagnostic="";lastPostPlayDiagnostic="";postEnterUnknownDiagnosed=false;lastSkillOcrDiagnostic="";contextMenuAnchor=null;contextMenuPanelBefore=null;lastContextDiagnostic="";skillsHoverCleared=false;summaryIdentity.Reset();ResetWorldChoice();ResetPlayButton();ResetEnter();}
        if(DateTimeOffset.UtcNow-lastAction<TimeSpan.FromMilliseconds(300))return;
        if(kind==OfficialWindowKind.Launcher)
        {
            backgroundCapture.Release();
            if(ObserveOnly){SetState("OBSERVATION_ONLY_LAUNCHER");return;}
            // Accessibility identifies the button; input remains an ordinary mouse click.
            // Do not read text boxes, passwords, account names, or token-bearing URLs.
            var found=await Task.Run(()=>FindLauncherPlay(target),ct);
            ct.ThrowIfCancellationRequested();
            if(found is null){SetState("LAUNCHER_PLAY_NOT_UNAMBIGUOUS");return;}
            var bounds=found.Value;
            if(!GetClientRect(target,out var client))return;
            var origin=new POINT();
            if(!ClientToScreen(target,ref origin))return;
            var frame=new VisualFrame(target,new Rectangle(origin.X,origin.Y,client.Right,client.Bottom),
                DateTimeOffset.UtcNow,Array.Empty<VisualLabel>());
            await Dispatch(frame,new("LAUNCH_SELECTED_CHARACTER",new Rectangle(
                (int)bounds.X-origin.X,(int)bounds.Y-origin.Y,(int)bounds.Width,(int)bounds.Height)),GetAncestor(target,2),ct);
            return;
        }
        if(ocr is null){SetState("OCR_LANGUAGE_UNAVAILABLE");return;}
        bool networkLost=!ObserveOnly&&await connectivity.Lost(observedSelectedWorld??preferredWorld,recovery.HasEstablished,ct);
        var observed=await Capture(target,owner,ct);
        if(observed is null)
        {SetState("CAPTURE_"+backgroundCapture.State);return;}
        if(!ObserveOnly)
        {
            bool diagnoseFooter=DateTimeOffset.UtcNow>=nextWorldFooterDiagnostic;
            observed=await WorldFooterRecovery.Reobserve(observed,preferredWorld,
                token=>Capture(target,owner,token),ct,
                diagnostic:message=>
                {
                    if(diagnoseFooter)
                    {
                        log.Add(Severity.INFO,"VISUAL_STARTUP","WORLD_FOOTER_REOBSERVING",message);
                        nextWorldFooterDiagnostic=DateTimeOffset.UtcNow.AddSeconds(10);
                    }
                });
        }
        observed=ConnectivityWatch.Signal(observed,networkLost);
        var visible=GameScreenReader.Read(observed);
        summaryIdentity.Observe(visible,observed.Labels);
        Game.Observe(summaryIdentity.Apply(visible),DateTimeOffset.UtcNow);
        DiagnoseSkillOcr(observed);
        if(Game.LastObservation?.Stage==VisibleGameStage.InGame)lastVerifiedHud=observed.CapturedUtc;
        else if(lastVerifiedHud!=default&&DateTimeOffset.UtcNow-lastVerifiedHud>TimeSpan.FromSeconds(3))
        {Gameplay.Reset();ResetNavigationCalibration();lastVerifiedHud=default;}
        DiagnosePostPlay(observed);
        if(!ObserveOnly&&Game.Confirmed is not null)
            VisuallyConfirmedSession?.Invoke();
        if(ObserveOnly)
        {
            SetState("OBSERVATION_ONLY_"+Game.LastObservation?.Stage);
            await Gameplay.InspectNavigation(observed,ct);return;
        }
        var recoveryDecision=await recovery.Evaluate(observed,Game.LastObservation!.Stage,preferredWorld,DateTimeOffset.UtcNow,SessionRecovery.Probe,ct);
        if(recoveryDecision is not null)
        {
            if(recoveryDecision.ResetSubject)
            {
                Game.Reset();Gameplay.Reset();ResetNavigationCalibration();observedSelectedWorld=null;previousAction="";repeatedActions=0;
                ResetWorldChoice();ResetPlayButton();ResetEnter();contextMenuAnchor=null;contextMenuPanelBefore=null;lastContextDiagnostic="";skillsHoverCleared=false;summaryIdentity.Reset();
            }
            if(lastRecoveryState!=recoveryDecision.State)
            {lastRecoveryState=recoveryDecision.State;log.Add(Severity.INFO,"RECONNECT","STATE","state="+lastRecoveryState+" pending_transactions_preserved=true");}
            if(recoveryDecision.Click is Rectangle retry)
            {
                await Dispatch(observed,new("RECONNECT_VISIBLE_BUTTON",retry,RuntimeInput:new(GameInputKind.LeftClick,retry)),owner,ct);return;
            }
            if(!recoveryDecision.ResumeLogin){SetState(recoveryDecision.State);return;}
        }
        else if(lastRecoveryState.Length>0&&Game.LastObservation!.Stage==VisibleGameStage.InGame)
        {lastRecoveryState="";log.Add(Severity.INFO,"RECONNECT","RETURNED_TO_GAME","skills_and_character_must_be_reconfirmed=true");}
        if(Game.LastObservation?.Stage==VisibleGameStage.Connecting)
        {
            SetState("POST_PLAY_CONNECTING");
            return;
        }
        if(Game.LastObservation?.Stage==VisibleGameStage.InGame)
        {
            // The official Character Summary displays the real account name
            // while the Skills tab displays the full 24-value raster. Require
            // two fresh summary frames, then switch to the separately verified
            // Skills icon. Do not click the Summary's Total Level as if it
            // contained individual skill cells.
            if(Game.Confirmed is null&&observed.Labels.Any(l=>l.Kind==VisualLabelKind.CharacterSummary))
            {
                if(summaryIdentity.Verified(observed.CapturedUtc) is null)
                {SetState("CONFIRMING_CHARACTER_SUMMARY");return;}
                var icon=observed.Labels.Where(l=>l.Kind==VisualLabelKind.SkillsButton).ToArray();
                if(icon.Length!=1)
                {SetState("SKILLS_BUTTON_NOT_VERIFIED_IN_SUMMARY");return;}
                await Dispatch(observed,new("OPEN_SKILLS_FROM_SUMMARY",icon[0].Bounds),owner,ct);
                return;
            }
            if(Game.Confirmed is not null)
            {
                // Adopt the session before localisation: the first gameplay
                // tick must not reset the freshly established regional hint.
                Gameplay.ConfirmSubject(Game.Confirmed);
                if(worldMapResult is null)
                {
                    SetState("WORLD_MAP_LOCALIZATION");worldMapReading=true;
                    using var mapBudget=CancellationTokenSource.CreateLinkedTokenSource(ct);
                    mapBudget.CancelAfter(TimeSpan.FromSeconds(90));
                    var mapDriver=new WindowVisualGameDriver(observed,async token=>
                    {
                        if(Paused||ObserveOnly)throw new OperationCanceledException(token);
                        var next=await Capture(target,owner,token);
                        return next is null?null:ConnectivityWatch.Signal(next,
                            await connectivity.Lost(observedSelectedWorld??preferredWorld,true,token));
                    },(f,input,token)=>Dispatch(f,new("WORLD_MAP_"+input.Kind,input.Bounds,RuntimeInput:input),owner,token),
                        action=>SetState("WORLD_MAP_INPUT_"+action.ToUpperInvariant()));
                    RoutineResult result;
                    try{result=await Gameplay.LocateWithWorldMap(mapDriver,()=>Paused||ObserveOnly,mapBudget.Token);}
                    catch(OperationCanceledException) when(!ct.IsCancellationRequested)
                    {result=new(Paused||ObserveOnly?RoutineStatus.Waiting:RoutineStatus.Blocked,"WORLD_MAP_PAUSED_OR_TIMED_OUT");}
                    finally{worldMapReading=false;}
                    if(result.Status!=RoutineStatus.Waiting)worldMapResult=result;
                    log.Add(result.Status==RoutineStatus.Completed?Severity.INFO:Severity.WARN,"WORLD_MAP","RESULT",
                        $"status={result.Status} reason={result.Reason}");
                    SetState(result.Status==RoutineStatus.Completed?"WORLD_MAP_POSITION_CONFIRMED":
                        result.Status==RoutineStatus.Waiting?"PAUSED":"BLOCKED_"+result.Reason);
                    return;
                }
                if(worldMapResult.Status!=RoutineStatus.Completed)
                {SetState("BLOCKED_"+worldMapResult.Reason);return;}
                if(minimapZoomResult is null&&Gameplay.NativePositionVerified(observed))
                {
                    minimapZoomResult=new(RoutineStatus.Completed,"NATIVE_MAP_TRANSFORM_VERIFIED_ZOOM_NOT_REQUIRED");
                    log.Add(Severity.INFO,"MINIMAP_ZOOM","RESULT","status=Completed reason="+minimapZoomResult.Reason);
                }
                if(minimapZoomResult is null)
                {
                    SetState("MINIMAP_ZOOM_VISUAL_CALIBRATION");
                    using var zoomBudget=CancellationTokenSource.CreateLinkedTokenSource(ct);
                    zoomBudget.CancelAfter(TimeSpan.FromSeconds(75));
                    var zoomDriver=new WindowVisualGameDriver(observed,async token=>
                    {
                        if(Paused||ObserveOnly)throw new OperationCanceledException(token);
                        bool lost=await connectivity.Lost(observedSelectedWorld??preferredWorld,true,token);
                        var next=await Capture(target,owner,token);
                        return next is null?null:ConnectivityWatch.Signal(next,lost);
                    },(f,input,token)=>Dispatch(f,new("MINIMAP_ZOOM_"+input.Kind,input.Bounds,RuntimeInput:input),owner,token),
                        action=>SetState("MINIMAP_ZOOM_INPUT_"+action.ToUpperInvariant()));
                    RoutineResult result;
                    try{result=await new MinimapZoomRoutine(zoomDriver,()=>Paused||ObserveOnly,
                        diagnostic:message=>log.Add(Severity.INFO,"MINIMAP_ZOOM","STEP",message)).Run(zoomBudget.Token);}
                    catch(OperationCanceledException) when(!ct.IsCancellationRequested)
                    {result=new(RoutineStatus.Blocked,"MINIMAP_ZOOM_TIMED_OUT");}
                    if(result.Status!=RoutineStatus.Waiting)
                    {
                        minimapZoomResult=result;
                        log.Add(result.Status==RoutineStatus.Completed?Severity.INFO:Severity.WARN,
                            "MINIMAP_ZOOM","RESULT",$"status={result.Status} reason={result.Reason} "+
                            $"wheel_down_steps={result.Produced} mouse_global_untouched=true");
                    }
                    SetState(result.Status==RoutineStatus.Completed?"MINIMAP_ZOOM_CONFIRMED":
                        result.Status==RoutineStatus.Waiting?"PAUSED":"BLOCKED_"+result.Reason);
                    return; // Next tick obtains a fresh frame after zoom calibration.
                }
                if(minimapZoomResult.Status!=RoutineStatus.Completed)
                {SetState("BLOCKED_"+minimapZoomResult.Reason);return;}

            }
            // A temporary loss of the Skills tab must not bypass a previous
            // navigation failure while the gameplay subject lease is alive.
            if(NavigationStartupPolicy.GameplayBlock(Gameplay.HasSubject,worldMapResult,minimapZoomResult,
                observed.Pixels?.WorldMapPanelOpen==true) is string navigationBlock)
            {SetState(navigationBlock);return;}
            if(await Gameplay.Tick(observed,Game.Confirmed,async token=>
            {
                if(Paused||ObserveOnly)throw new OperationCanceledException(token);
                bool lost=await connectivity.Lost(observedSelectedWorld??preferredWorld,true,token);
                var next=await Capture(target,owner,token);
                return next is null?null:ConnectivityWatch.Signal(next,lost);
            },(frame,input,token)=>Dispatch(frame,new("GAMEPLAY_"+input.Kind,input.Bounds,RuntimeInput:input),owner,token),ct))
            {SetState("AGENT_"+Gameplay.State);return;}
            if(Game.LastObservation.TotalLevel is null&&observed.ScreenBounds.Size==DockedWindowLayout.GameSize)
            {
                var nativeSkills=observed.Labels.SingleOrDefault(l=>l.Kind==VisualLabelKind.SkillsButton);
                var button=nativeSkills?.Bounds??new Rectangle(DockedWindowLayout.GameSize.Width-230,DockedWindowLayout.GameSize.Height-322,10,10);
                bool tooltip=observed.Labels.Any(l=>l.Text.Equals("Skills",StringComparison.OrdinalIgnoreCase)&&
                    l.Bounds.Left<200&&l.Bounds.Top<50);
                bool identified=tooltip||nativeSkills is not null;
                await Dispatch(observed,new(identified?"OPEN_SKILLS":"HOVER_SKILLS",button,!identified),owner,ct);
                return;
            }
            // Also handle a session that entered with Skills already selected:
            // one safe hover transition clears a possibly obscuring tooltip.
            // Do not loop hover inputs if OCR still misses a base level.
            if(Game.LastObservation is { } incomplete&&
                SkillsHoverPolicy.NeedsClear(skillsHoverCleared,incomplete,observed.ScreenBounds.Size))
            {
                await Dispatch(observed,new("CLEAR_SKILLS_HOVER",SkillsHoverPolicy.RestTarget,true),owner,ct);
                return;
            }
            SetState(Game.Confirmed is null?"IN_GAME_"+GameScreenReader.ReadinessReason(Game.LastObservation):"IN_GAME_GOAL_PLANNED");return;
        }
        var decision=VisualStartupPolicy.Decide(observed.Labels,preferredWorld,
            preferredWorld is not null&&observedSelectedWorld==preferredWorld);
        DiagnoseWorldGrid(observed,preferredWorld,decision);
        if(PostEnterUnknownScreenPolicy.ShouldBlock(previousAction,lastAction,DateTimeOffset.UtcNow,
            Game.LastObservation!.Stage,decision.State))
        {
            const string blocked="BLOCKED_POST_ENTER_UNRECOGNIZED_SCREEN";
            if(State!=blocked)
                log.Add(Severity.WARN,"VISUAL_STARTUP",blocked,
                    "elapsed_s="+(int)(DateTimeOffset.UtcNow-lastAction).TotalSeconds+
                    " entry_button_not_visible=true known_hud_not_visible=true "+
                    "new_input_dispatched=false screenshot_persisted=false");
            SetState(blocked);
            return;
        }
        if(decision.State=="PLAY_SELECTED_CHARACTER"&&decision.Click is Rectangle playButton)
        {
            // Must see the same button from two independent fresh frames.
            // Never convert a single fixed rectangle into an unchecked click.
            if(!PlayButtonConfirmed(playButton,observed.CapturedUtc))
            {
                SetState("AWAITING_INDEPENDENT_PLAY_BUTTON_CONFIRMATION");
                return;
            }
            ResetPlayButton();
            observedSelectedWorld=preferredWorld;
            log.Add(Severity.INFO,"VISUAL_STARTUP","WORLD_VISUALLY_CONFIRMED",
                $"world={observedSelectedWorld} independent_footer_and_play_frames=2 ping_recommendation_only=false");
        }
        else ResetPlayButton();
        if(decision.State=="ENTER_GAME"&&decision.Click is Rectangle enterButton)
        {
            // The second "Click here to play" prompt is a separate transition.
            // Verify that the same action remains visible in two fresh frames.
            if(!EnterConfirmed(enterButton,observed.CapturedUtc))
            {
                SetState("AWAITING_INDEPENDENT_ENTER_GAME_CONFIRMATION");
                return;
            }
            ResetEnter();
        }
        else ResetEnter();
        if(decision.State=="SELECT_F2P_WORLD"&&decision.Click is Rectangle worldCell)
        {
            // A target from one OCR capture is not enough to click a server.
            // Confirm the SAME world and cell on a distinct newer frame.
            if(!WorldCellConfirmed(preferredWorld,worldCell,observed.CapturedUtc))
            {
                SetState("AWAITING_INDEPENDENT_WORLD_CELL_CONFIRMATION");
                return;
            }
            ResetWorldChoice();
        }
        else ResetWorldChoice();
        if(decision.Click is null)
        {
            SetState(decision.State=="WAITING_FOR_RECOGNIZED_SCREEN"&&
                previousAction is "PLAY_SELECTED_CHARACTER" or "ENTER_GAME"?
                "POST_PLAY_WAITING_FOR_RECOGNIZABLE_SCREEN":decision.State);
            return;
        }
        await Dispatch(observed,decision,owner,ct);
    }

    private static System.Windows.Rect? FindLauncherPlay(IntPtr target)
    {
        // UI Automation of a foreign process may block. Keep it off the UI thread.
        var root=AutomationElement.FromHandle(target);
        var images=root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Image));
        if(!images.Cast<AutomationElement>().Any(e=>!e.Current.IsOffscreen&&
            (e.Current.Name.Contains("Old School RuneScape",StringComparison.OrdinalIgnoreCase)||
             e.Current.HelpText.Contains("Old School RuneScape",StringComparison.OrdinalIgnoreCase))))return null;
        var buttons=root.FindAll(TreeScope.Descendants,new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
            new OrCondition(new PropertyCondition(AutomationElement.NameProperty,"Spielen"),
                new PropertyCondition(AutomationElement.NameProperty,"Play"))));
        if(buttons.Count!=1)return null;
        var button=buttons[0];
        return button.Current.IsEnabled&&!button.Current.IsOffscreen?button.Current.BoundingRectangle:null;
    }

    private async Task<VisualFrame?> Capture(IntPtr target,IntPtr owner,CancellationToken ct)
    {
        using var captured=await backgroundCapture.ReadClientAsync(target,ct);
        if(captured is null)
        {
            RenderObserver.MarkUnavailable(backgroundCapture.State.Contains("MINIMIZED")?
                LiveRenderState.Minimized:LiveRenderState.CaptureUnavailable);
            return null;
        }
        RenderObserver.ObserveBitmap(captured.Image,captured.CapturedUtc);
        lastWelcomePixels=NativeWelcomePanelReader.Inspect(captured.Image);
        lastHudFooterPixels=NativeGameHudReader.InspectFooter(captured.Image);
        var point=new POINT();
        if(!ClientToScreen(target,ref point))return null;
        var bounds=new Rectangle(point.X,point.Y,captured.Image.Width,captured.Image.Height);
        long ocrStart=System.Diagnostics.Stopwatch.GetTimestamp();
        var labels=await Recognize(captured.Image,ocr!,
            postPlay: recovery.Recovering||previousAction is "PLAY_SELECTED_CHARACTER" or "ENTER_GAME",
            contextAnchor:contextMenuAnchor,worldMapProbe:worldMapReading||ObserveOnly);
        ct.ThrowIfCancellationRequested();
        var ocrElapsed=System.Diagnostics.Stopwatch.GetElapsedTime(ocrStart);
        if(contextMenuAnchor is Point&&ocrElapsed>=TimeSpan.FromMilliseconds(1400))
            log.Add(Severity.INFO,"GAMEPLAY","MENU_OCR_TIMING",
                $"duration_ms={(int)ocrElapsed.TotalMilliseconds} "+
                $"frame_age_ms={(int)(DateTimeOffset.UtcNow-captured.CapturedUtc).TotalMilliseconds} "+
                $"native_hud={labels.Any(l=>l.Kind==VisualLabelKind.GameHud)} "+
                $"inventory_active={InventoryTabPolicy.Active(captured.Image)} "+
                "screenshot_persisted=false");
        var pixels=ExecutionVision.Read(captured.Image,labels);
        if(worldMapReading||ObserveOnly)
        {
            var diagnostic=WorldMapVision.Describe(captured.Image,labels)+$" globe={pixels?.WorldMapButton?.ToString()??"none"} "+pixels?.WorldMapButtonEvidence;
            if(diagnostic!=lastWorldMapDiagnostic)
            {lastWorldMapDiagnostic=diagnostic;log.Add(Severity.INFO,"WORLD_MAP","FRAME",diagnostic);}
        }
        if(GameScreenReader.Read(new(target,bounds,captured.CapturedUtc,labels)).Stage==VisibleGameStage.Unknown&&
            PostPlayScreenReader.RecoveryDiagnostic!="not_read"&&lastRecoveryOcrDiagnostic!=PostPlayScreenReader.RecoveryDiagnostic)
        {
            lastRecoveryOcrDiagnostic=PostPlayScreenReader.RecoveryDiagnostic;
            log.Add(Severity.INFO,"RECONNECT","FOCUSED_OCR_DIAGNOSTIC",lastRecoveryOcrDiagnostic);
        }
        if(contextMenuAnchor is null&&pixels is not null&&labels.Any(l=>l.Kind==VisualLabelKind.GameHud))
        {
            string health=$"hp={CombatPolicy.Hitpoints(pixels)?.ToString()??"unknown"} "+
                $"navigation_available={pixels.Navigation is not null} evidence={pixels.HealthEvidence}";
            if(health!=lastHealthDiagnostic)
            {lastHealthDiagnostic=health;log.Add(Severity.INFO,"GAMEPLAY","HEALTH_OBSERVATION",health);}
        }
        if(contextMenuAnchor is Point anchor)
        {
            var menu=ExecutionUi.Menu(labels);
            int focused=labels.Count(l=>l.Kind==VisualLabelKind.ContextMenuLine);
            var rawCrop=labels.Where(l=>l.Kind==VisualLabelKind.ContextMenuCandidate).ToArray();
            int cropHeaders=rawCrop.Count(l=>l.Text.Trim().Equals("Choose Option",StringComparison.OrdinalIgnoreCase));
            int cropCancels=rawCrop.Count(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase));
            int cropExamines=rawCrop.Count(l=>l.Text.Trim().StartsWith("Examine ",StringComparison.OrdinalIgnoreCase));
            int examinePrefixes=rawCrop.Count(l=>l.Text.Trim().StartsWith("Examin",StringComparison.OrdinalIgnoreCase));
            int examineStems=rawCrop.Count(l=>l.Text.Trim().Equals("Examine",StringComparison.OrdinalIgnoreCase));
            int examineUnspaced=rawCrop.Count(l=>
                l.Text.Trim().StartsWith("Examine",StringComparison.OrdinalIgnoreCase)&&
                l.Text.Trim().Length>7&&!char.IsWhiteSpace(l.Text.Trim()[7]));
            var cancelLines=rawCrop.Where(l=>
                l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase)).ToArray();
            string cancelTops=string.Join(",",cancelLines.Select(l=>l.Bounds.Top)
                .Distinct().OrderBy(y=>y));
            // Report only bounding-box heights from the 75 native pixels
            // above an observed Cancel, never chat or inventory item text.
            int? footerY=cancelLines.Length>0?cancelLines.Min(l=>l.Bounds.Top):null;
            string footerRows=footerY is int bottom?
                string.Join(",",rawCrop.Where(l=>l.Bounds.Top>=bottom-75&&
                    l.Bounds.Top<bottom&&Math.Abs(l.Bounds.Left-anchor.X)<=160)
                    .Select(l=>l.Bounds.Top).Distinct().OrderBy(y=>y).Take(12)):"none";
            // Pixel positions only: reveal whether right-edge OCR is reading
            // an upward or downward popup, without logging item names.
            string cropPositions=string.Join(",",rawCrop.Take(8)
                .Select(l=>$"{l.Bounds.Left}:{l.Bounds.Top}"));
            int headers=labels.Count(l=>l.Text.Trim().Equals("Choose Option",StringComparison.OrdinalIgnoreCase));
            int cancels=labels.Count(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase));
            // The panel hash is from the frame BEFORE the right-click. A
            // changed image indicates rendering changed, NOT that a game
            // action succeeded. Never expose raw screenshots or item text.
            bool? changed=contextMenuPanelBefore is ulong hash&&pixels is not null?
                pixels.InventoryPanelSignature!=hash:null;
            int empty=pixels?.Slots.Count(slot=>slot.Empty)??0;
            bool slotZero=pixels?.Slots.FirstOrDefault()?.Empty??false;
            string diagnostic=$"anchor_x={anchor.X} anchor_y={anchor.Y} focused_lines={focused} "+
                $"header_candidates={headers} cancel_candidates={cancels} parsed={menu is not null} "+
                $"entries={menu?.Entries.Count??0} crop_xy={(cropPositions.Length==0?"none":cropPositions)} "+
                $"inventory_tab_active={pixels?.InventoryTabActive??false} "+
                $"empty_candidates={empty} slot0_empty={slotZero} panel_changed={changed?.ToString()??"unknown"} "+
                $"empty_pixel_quality={pixels?.InventorySlot0Diagnostic??"unavailable"} "+
                $"crop_raw_lines={rawCrop.Length} crop_headers={cropHeaders} "+
                $"crop_cancels={cropCancels} crop_examines={cropExamines} "+
                $"crop_examine_prefixes={examinePrefixes} crop_examine_stems={examineStems} "+
                $"crop_examine_unspaced={examineUnspaced} "+
                $"crop_cancel_y={(cancelTops.Length>0?cancelTops:"none")} "+
                $"crop_above_cancel_ys={(footerRows.Length>0?footerRows:"none")} image_phase=AFTER_RIGHT_CLICK";
            if(diagnostic!=lastContextDiagnostic)
            {
                lastContextDiagnostic=diagnostic;
                log.Add(Severity.INFO,"GAMEPLAY","CONTEXT_MENU_DIAGNOSTIC",diagnostic);
            }
        }
        return new(target,bounds,captured.CapturedUtc,labels,pixels);
    }
    // A complete, pixel-backed menu needs one dedicated OCR source; the
    // expensive whole-screen and minimap/Hp passes otherwise cause a valid
    // two-second-old capture to be rejected before Cancel can be clicked.
    // The stage is separately verified by native Skills/footer pixels.
    internal static async Task<IReadOnlyList<VisualLabel>?> FocusedInventoryMenu(
        Bitmap bitmap,OcrEngine engine,Point? contextAnchor,bool postPlay=false)
    {
        if(postPlay||!InventoryMenuOcrPolicy.IsContext(bitmap,contextAnchor)||
            NativeGameHudReader.Read(bitmap,Array.Empty<VisualLabel>()) is not VisualLabel verifiedHud)
            return null;
        var focused=new List<VisualLabel>{verifiedHud};
        if(NativeGameHudReader.Skills(bitmap) is VisualLabel skills)
            focused.Add(skills);
        if(NativeCharacterFontReader.Read(bitmap) is VisualLabel character)
            focused.Add(character);
        focused.AddRange(await ContextMenuOcr.Read(bitmap,engine,contextAnchor!.Value));
        return ExecutionUi.Menu(focused) is not null?focused.Distinct().ToArray():null;
    }
    private static async Task<IReadOnlyList<VisualLabel>> Recognize(
        Bitmap bitmap,OcrEngine engine,bool postPlay=false,Point? contextAnchor=null,bool worldMapProbe=false)
    {
        // Missing/ambiguous menu evidence still uses the complete OCR path
        // and never authorizes a click from geometry or inventory alone.
        if(await FocusedInventoryMenu(bitmap,engine,contextAnchor,postPlay) is { } menuOnly)
            return menuOnly;
        using var enlarged=bitmap.Width*2<=OcrEngine.MaxImageDimension&&bitmap.Height*2<=OcrEngine.MaxImageDimension?
            new Bitmap(bitmap.Width*2,bitmap.Height*2):null;
        Bitmap input=bitmap;
        int scale=1;
        if(enlarged is not null)
        {
            using var graphics=Graphics.FromImage(enlarged);
            graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.DrawImage(bitmap,new Rectangle(Point.Empty,enlarged.Size));
            input=enlarged;scale=2;
        }
        var result=await RecognizeRaw(input,engine);
        Rectangle Map(Windows.Foundation.Rect r)=>new((int)(r.X/scale),(int)(r.Y/scale),
            (int)Math.Ceiling(r.Width/scale),(int)Math.Ceiling(r.Height/scale));
        var labels=result.Lines.Where(l=>l.Words.Count>0).Select(line=>new VisualLabel(line.Text,
            line.Words.Select(w=>Map(w.BoundingRect)).Aggregate(Rectangle.Union))).ToList();
        labels.AddRange(result.Lines.SelectMany(l=>l.Words).Where(w=>Regex.IsMatch(w.Text,@"^\d{1,4}$")||
            w.Text.Equals("World",StringComparison.OrdinalIgnoreCase))
            .Select(w=>new VisualLabel(w.Text,Map(w.BoundingRect),Regex.IsMatch(w.Text,@"^\d+$")?
                VisualLabelKind.Number:VisualLabelKind.Text)));
        labels=labels.Distinct().ToList();
        if(worldMapProbe)labels.AddRange(await WorldMapVision.ReadLabels(bitmap,engine));
        if(postPlay)
        {
            // Full-frame OCR often misses the next native login prompt.
            // Only add exact, centered UI phrases from a focused second pass.
            var focused=await PostPlayScreenReader.Read(bitmap,engine,labels);
            foreach(var phrase in focused)
                if(!labels.Any(l=>l.Text.Equals(phrase.Text,StringComparison.OrdinalIgnoreCase)&&
                    l.Bounds.IntersectsWith(phrase.Bounds)))
                    labels.Add(phrase);
            if(NativeWelcomePanelReader.Read(bitmap,labels) is VisualLabel native)
            {
                labels.Add(native);
                // The banner itself is image-confirmed. This marker is
                // intentionally generic; no account text is interpreted.
                if(!labels.Any(l=>l.Text.Equals("Welcome",StringComparison.OrdinalIgnoreCase)))
                    labels.Add(new("Welcome",new Rectangle(300,20,400,35)));
            }
        }
        var focusedWorlds=await WorldGridOcr.Read(bitmap,engine,labels);
        labels.AddRange(focusedWorlds.Count>0?focusedWorlds:WorldGridReader.Classify(labels));
        labels.AddRange(WorldPageReader.Read(bitmap,labels));
        bool inventoryContext=InventoryMenuOcrPolicy.IsContext(bitmap,contextAnchor);
        bool nativeHudVerified=false;
        if(inventoryContext)
        {
            // A directed inventory right-click has its own menu OCR below.
            // First check the actual minimap, tab icon and chat-footer pixels.
            // If all native HUD anchors survive the overlay, expensive
            // chat/skill/quantity OCR cannot add action evidence for this
            // already-open menu and need not delay its screenshot.
            labels.AddRange(await ExecutionVision.ReadHealth(bitmap,engine,labels));
            if(NativeGameHudReader.Skills(bitmap) is VisualLabel nativeSkills)
                labels.Add(nativeSkills);
            if(NativeGameHudReader.Read(bitmap,labels) is VisualLabel nativeHud)
            {
                labels.Add(nativeHud);
                nativeHudVerified=true;
            }
        }
        if(!InventoryMenuOcrPolicy.FastHud(inventoryContext,nativeHudVerified))
            foreach(var focused in await GameplayHudOcr.Read(bitmap,engine))
            {
                labels.RemoveAll(l=>l.Kind==focused.Kind&&l.Text.Equals(focused.Text,StringComparison.OrdinalIgnoreCase)&&l.Bounds.IntersectsWith(focused.Bounds));
                labels.Add(focused);
            }
        labels=labels.Distinct().ToList();
        if(!inventoryContext&&GameScreenReader.Read(new(IntPtr.Zero,
            new Rectangle(Point.Empty,bitmap.Size),DateTimeOffset.UtcNow,labels)).Stage==VisibleGameStage.Unknown)
            foreach(var recovered in await PostPlayScreenReader.Read(bitmap,engine,labels,recovery:true))
                if(!labels.Any(l=>l.Kind==recovered.Kind&&l.Text.Equals(recovered.Text,StringComparison.OrdinalIgnoreCase)&&
                    l.Bounds.IntersectsWith(recovered.Bounds)))
                    labels.Add(recovered);
        // Run after focused recovery too: a newly recovered welcome anchor
        // must get the same footer and pixel-backed Play verification as a
        // welcome phrase recognized by the full-frame pass.
        if(labels.Any(l=>l.Text.Contains("Welcome to Old School RuneScape",StringComparison.OrdinalIgnoreCase)))
        {
            labels.AddRange(await ReadWorldFooter(bitmap,engine));
            if(WelcomePlayButtonReader.Read(bitmap,labels) is VisualLabel playButton)
                labels.Add(playButton);
        }
        // Native P12 chat glyphs remain usable without any OCR inference.
        if(NativeCharacterFontReader.Read(bitmap) is VisualLabel nativePrompt)
        {
            labels.RemoveAll(l=>l.Kind==VisualLabelKind.CharacterPrompt);
            labels.Add(nativePrompt);
        }
        if(!inventoryContext)
        {
            labels.AddRange(await CharacterSummaryReader.Read(bitmap,engine));
            labels.AddRange(await SkillGridOcr.Read(bitmap,engine,labels));
            labels.AddRange(await ExecutionVision.ReadHealth(bitmap,engine,labels));
            if(NativeGameHudReader.Skills(bitmap) is VisualLabel skillsButton)
                labels.Add(skillsButton);
            if(NativeGameHudReader.Read(bitmap,labels) is VisualLabel hud)
                labels.Add(hud);
            if(GameScreenReader.Read(new(IntPtr.Zero,new Rectangle(Point.Empty,bitmap.Size),
                DateTimeOffset.UtcNow,labels)).Stage==VisibleGameStage.InGame)
                labels.AddRange(await ExecutionVision.ReadQuantities(bitmap,engine));
        }
        // While a menu is open, item-stack digits may be obscured or confused
        // with menu OCR. The neutral pre-click and post-Cancel captures still
        // read quantities normally and validate the final inventory deltas.
        if(contextAnchor is Point anchor)
            // Even a global "Choose Option / Cancel" pair can miss an item's
            // Examine entry. The focused view must get its own chance to read
            // all visible lines instead of treating the slot as empty.
            labels.AddRange(await ContextMenuOcr.Read(bitmap,engine,anchor));
        return labels;
    }
    private static async Task<IReadOnlyList<VisualLabel>> ReadWorldFooter(Bitmap bitmap,OcrEngine engine,bool smooth=false)
    {
        var crop=new Rectangle(0,Math.Max(0,bitmap.Height-100),bitmap.Width/2,Math.Min(100,bitmap.Height));
        using var cleaned=smooth?GameplayHudOcr.Contrast(bitmap,crop):null;
        using var enlarged=new Bitmap(crop.Width*4,crop.Height*4);
        using(var g=Graphics.FromImage(enlarged))
        {
            g.InterpolationMode=smooth?System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic:
                System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(cleaned??bitmap,new Rectangle(Point.Empty,enlarged.Size),
                cleaned is null?crop:new Rectangle(Point.Empty,crop.Size),GraphicsUnit.Pixel);
        }
        var result=await RecognizeRaw(enlarged,engine);
        var labels=new List<VisualLabel>();
        var mapped=new List<VisualLabel>();
        foreach(var line in result.Lines)
        {
            var bounds=line.Words.Select(w=>new Rectangle(crop.X+(int)(w.BoundingRect.X/4),
                crop.Y+(int)(w.BoundingRect.Y/4),(int)Math.Ceiling(w.BoundingRect.Width/4),
                (int)Math.Ceiling(w.BoundingRect.Height/4))).Aggregate(Rectangle.Union);
            mapped.Add(new(line.Text,bounds));
            string joined=string.Join("",line.Words.Select(w=>w.Text));
            var match=Regex.Match(joined,@"^W[o0]r[lI1i|]d([3-8]\d{2})(?:Clicktoswitch)?$",RegexOptions.IgnoreCase);
            if(match.Success)labels.Add(new("World "+match.Groups[1].Value,bounds));
        }
        labels.AddRange(WorldFooterPolicy.Read(mapped,bitmap.Size));
        if(NativeWorldDigitReader.ReadFooter(bitmap,mapped) is VisualLabel nativeWorld)
            labels.Add(nativeWorld);
        footerDiagnostic=$"world_candidates={labels.Count} switch_marker="+mapped.Any(l=>l.Text.Contains("switch",StringComparison.OrdinalIgnoreCase))+
            $" contrast={smooth}";
        if(labels.Count==0&&!smooth)return await ReadWorldFooter(bitmap,engine,smooth:true);
        return labels;
    }
    internal static async Task<OcrResult> RecognizeRaw(Bitmap bitmap,OcrEngine engine)
    {
        using var bytes=new MemoryStream();bitmap.Save(bytes,ImageFormat.Png);
        using var stream=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(stream))
        {writer.WriteBytes(bytes.ToArray());await writer.StoreAsync();writer.DetachStream();}
        stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream);
        using var software=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
        return await engine.RecognizeAsync(software);
    }
    internal static async Task<int> OcrSelfTest()
    {
        try
        {
            var engine=OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages();
            if(engine is null)
            {
                Console.Error.WriteLine("OCR FIXTURE FAIL: no Windows OCR recognizer available; languages="+
                    string.Join(",",OcrEngine.AvailableRecognizerLanguages.Select(l=>l.LanguageTag)));
                return 2;
            }
            await WorldMapTests.OcrFixture(engine);
            await WorldGridOcr.OcrFixture(engine);
            using var bitmap=new Bitmap(1000,400);
            using(var g=Graphics.FromImage(bitmap))
            using(var font=new Font("Segoe UI",24))
            {
                g.Clear(Color.Black);
                g.DrawString("Welcome to Old School RuneScape",font,Brushes.White,20,30);
                g.DrawString("Play Now",font,Brushes.White,20,120);
                g.DrawString("World 417",font,Brushes.White,20,300);
            }
            var labels=await Recognize(bitmap,engine);
            var decision=VisualStartupPolicy.Decide(labels,417);
            if(decision.State!="PLAY_SELECTED_CHARACTER")
                throw new InvalidOperationException("OCR-to-policy fixture failed: "+decision.State);
            using var native=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(native))
            using(var font=new Font("Segoe UI",10))
            {
                g.Clear(Color.Black);
                // Regression: OCR dropped the literal World but retained the
                // three digits and the smaller second line on the real client.
                g.DrawString("397",font,Brushes.White,180,550);
                g.DrawString("Click switch",font,Brushes.White,150,568);
            }
            var footer=await ReadWorldFooter(native,engine);
            if(!footer.Any(l=>l.Text=="World 397"))
                throw new InvalidOperationException("Native footer OCR regression");
            using var disconnect=new Bitmap(1000,600);
            using(var graphics=Graphics.FromImage(disconnect))
            using(var small=new Font("Segoe UI",10))
            {
                graphics.Clear(Color.FromArgb(37,48,25));
                graphics.DrawString("You were disconnected from the server.",small,Brushes.Yellow,350,235);
                graphics.DrawString("Try again",small,Brushes.White,450,305);
            }
            var recovery=await Recognize(disconnect,engine);
            var recovering=await Recognize(disconnect,engine,postPlay:true);
            if(GameScreenReader.Read(new(new IntPtr(5),new(0,0,1000,600),DateTimeOffset.UtcNow,recovery)).Stage!=VisibleGameStage.Disconnected||
                recovery.Count(l=>l.Text.Equals("Try again",StringComparison.OrdinalIgnoreCase))!=1||
                !SessionRecovery.Disconnected(recovering))
                throw new InvalidOperationException("Focused disconnected-screen OCR regression");
            using var worldGrid=new Bitmap(1000,600);
            var gridLabels=new List<VisualLabel>{new("Select a world",new(20,5,100,15))};
            using(var g=Graphics.FromImage(worldGrid))
            using(var font=new Font("Segoe UI",9))
            {
                g.Clear(Color.Black);
                for(int col=0;col<5;col++)for(int row=0;row<25;row++)
                {
                    string id=(301+col*25+row).ToString();
                    var bounds=new Rectangle(80+col*93,40+row*19,26,13);
                    g.DrawString(id,font,Brushes.White,bounds.Location);
                    if(id!="399")gridLabels.Add(new(id,bounds,VisualLabelKind.Number));
                }
            }
            var worldRead=await WorldGridOcr.Read(worldGrid,engine,gridLabels);
            if(!worldRead.Any(l=>l.Text=="399"))throw new InvalidOperationException("Focused world-grid OCR regression");
            using var contextFixture=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(contextFixture))
            using(var font=new Font("Segoe UI",14))
            {
                g.Clear(Color.Black);
                g.DrawString("Choose Option",font,Brushes.White,40,40);
                g.DrawString("Use Logs",font,Brushes.White,40,68);
                g.DrawString("Examine Logs",font,Brushes.White,40,96);
                g.DrawString("Cancel",font,Brushes.White,40,124);
            }
            var contextLabels=await Recognize(contextFixture,engine);
            if(ExecutionUi.Menu(contextLabels)?.Exact("Use Logs") is null)
                throw new InvalidOperationException("Context-menu OCR regression");
            var focusedContext=await ContextMenuOcr.Read(contextFixture,engine,new Point(60,72));
            if(ExecutionUi.Menu(focusedContext)?.Exact("Use Logs") is null||
                !focusedContext.All(l=>l.Kind==VisualLabelKind.ContextMenuLine))
                throw new InvalidOperationException("Right-click anchored focused context-menu OCR regression");
            // Reproduce an item menu near the lower-right inventory edge,
            // where the native popup may be flipped in both directions.
            using var bottomRightMenu=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(bottomRightMenu))
            using(var font=new Font("Segoe UI",14))
            {
                g.Clear(Color.Black);
                g.DrawString("Choose Option",font,Brushes.White,770,380);
                g.DrawString("Use Logs",font,Brushes.White,770,408);
                g.DrawString("Examine Logs",font,Brushes.White,770,436);
                g.DrawString("Cancel",font,Brushes.White,770,464);
            }
            var cornerMenu=await ContextMenuOcr.Read(bottomRightMenu,engine,new Point(860,508));
            if(ExecutionUi.Menu(cornerMenu)?.Exact("Examine Logs") is null)
                throw new InvalidOperationException("Bottom-right flipped menu OCR regression");
            // Reproduce the real slot-22 geometry: the game's Cancel OCR
            // line is at y~584, below the previous tight crop bottom (568).
            // All item names still have to come from independently read pixels.
            using var lowerEdgeMenu=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(lowerEdgeMenu))
            using(var font=new Font("Segoe UI",14))
            {
                g.Clear(Color.Black);
                g.DrawString("Choose Option",font,Brushes.White,770,472);
                g.DrawString("Use Logs",font,Brushes.White,770,492);
                g.DrawString("Inspect Logs",font,Brushes.White,770,512);
                g.DrawString("Drop Logs",font,Brushes.White,770,532);
                g.DrawString("Examine Logs",font,Brushes.White,770,552);
                g.DrawString("Cancel",font,Brushes.White,770,572);
            }
            var lowerEdgeRead=await ContextMenuOcr.Read(lowerEdgeMenu,engine,new Point(860,508));
            var lowerEdgeParsed=ExecutionUi.Menu(lowerEdgeRead);
            if(lowerEdgeParsed?.Exact("Examine Logs") is null||
                lowerEdgeParsed.Exact("Cancel") is null||
                lowerEdgeRead.Any(l=>l.Kind!=VisualLabelKind.ContextMenuLine))
                throw new InvalidOperationException("Bottom-edge menu OCR must retain Examine and Cancel without inventing entries");
            // Regression for the live slot-25 stale capture: with real native
            // HUD/footer/Inventory evidence, OCR only the visible menu.
            using var nativeContext=new Bitmap(lowerEdgeMenu);
            using(var g=Graphics.FromImage(nativeContext))
            using(var selected=new SolidBrush(Color.FromArgb(125,31,28)))
            {
                g.FillRectangle(selected,InventoryTabPolicy.Icon);
                g.FillRectangle(Brushes.Red,NativeGameHudReader.SkillsButton.X+3,
                    NativeGameHudReader.SkillsButton.Y+12,6,5);
                g.FillRectangle(Brushes.Green,NativeGameHudReader.SkillsButton.X+15,
                    NativeGameHudReader.SkillsButton.Y+12,6,5);
                g.FillRectangle(Brushes.Blue,NativeGameHudReader.SkillsButton.X+25,
                    NativeGameHudReader.SkillsButton.Y+12,6,5);
                foreach(int x in new[]{120,182,368})
                {
                    g.FillRectangle(Brushes.White,x+4,579,7,3);
                    g.FillRectangle(Brushes.Green,x+4,590,5,2);
                }
                g.FillRectangle(Brushes.DarkRed,432,577,65,9);
                g.FillRectangle(Brushes.White,435,578,6,4);
            }
            var slot25Anchor=new Point(818,544);
            var focused25=await FocusedInventoryMenu(nativeContext,engine,slot25Anchor);
            if(focused25 is null||
                ExecutionUi.Menu(focused25)?.Exact("Examine Logs") is null||
                ExecutionUi.Menu(focused25)?.Exact("Cancel") is null||
                !focused25.Any(l=>l.Kind==VisualLabelKind.GameHud)||
                GameScreenReader.Read(new VisualFrame(IntPtr.Zero,
                    new Rectangle(0,0,1000,600),DateTimeOffset.UtcNow,focused25)).Stage!=VisibleGameStage.InGame||
                focused25.Any(l=>l.Kind==VisualLabelKind.Minimap||l.Kind==VisualLabelKind.Hitpoints))
                throw new InvalidOperationException("Verified inventory menu must use complete focused OCR and preserve native InGame evidence without inventing health");
            // The live HUD can be independently verified while Hough returns
            // no minimap. Read a red health icon + real OCR digits without
            // turning it into navigation pixels or trusting the skill level.
            using var healthWithoutMap=new Bitmap(nativeContext);
            using(var g=Graphics.FromImage(healthWithoutMap))
            using(var font=new Font("Segoe UI",18))
            {
                g.DrawString("24",font,Brushes.White,715,70);
                g.FillEllipse(Brushes.Red,770,82,20,18);
            }
            if(HudVision.Minimap(healthWithoutMap) is not null)
                throw new InvalidOperationException("Standalone health fixture unexpectedly has a minimap");
            var redHealth=await ExecutionVision.ReadHealth(healthWithoutMap,engine,
                Array.Empty<VisualLabel>());
            var offsetMap=new Rectangle(830,25,150,150);
            var healthCandidates=ExecutionVision.HealthNumberCandidates(healthWithoutMap,
                Array.Empty<VisualLabel>(),offsetMap);
            if(healthCandidates.Count!=2||
                healthCandidates[1]!=HudVision.StandaloneHealthNumber(healthWithoutMap)||
                HudVision.HeartRightOf(healthWithoutMap,healthCandidates[0]))
                throw new InvalidOperationException("An unusable map-relative HP crop must retain the independent red-orb fallback");
            var independentHp=ExecutionVision.Read(healthWithoutMap,redHealth);
            if(redHealth.Count(l=>l.Kind==VisualLabelKind.Hitpoints)!=1||
                redHealth.Single(l=>l.Kind==VisualLabelKind.Hitpoints).Text!="24"||
                independentHp?.Hitpoints!=24||independentHp.Navigation is not null)
                throw new InvalidOperationException("Pixel-verified red health icon must independently OCR 24 without minimap navigation");
            using var compactHealth=new Bitmap(healthWithoutMap);
            using(var g=Graphics.FromImage(compactHealth))g.FillRectangle(Brushes.Black,700,65,68,45);
            var compactDigits=new[]{".###./#...#/....#/...#./..#../.#.../#..../#####",
                "#.../#.../#.../#.#./#.#./####/..#./..#."};
            int compactX=750;
            foreach(var pattern in compactDigits)
            {
                var rows=pattern.Split('/');
                for(int y=0;y<rows.Length;y++)for(int x=0;x<rows[y].Length;x++)
                    if(rows[y][x]=='#')compactHealth.SetPixel(compactX+x,85+y,Color.Lime);
                compactX+=rows[0].Length+2;
            }
            var compactRead=await ExecutionVision.ReadHealth(compactHealth,engine,Array.Empty<VisualLabel>());
            if(compactRead.SingleOrDefault(l=>l.Kind==VisualLabelKind.Hitpoints)?.Text!="24")
                throw new InvalidOperationException("Native green P11 HP digits beside the orb must remain complete and decode as 24");
            using var noHealthOrb=new Bitmap(healthWithoutMap);
            using(var g=Graphics.FromImage(noHealthOrb))
                g.FillRectangle(Brushes.Black,760,75,40,35);
            var absentHeart=await ExecutionVision.ReadHealth(noHealthOrb,engine,
                Array.Empty<VisualLabel>());
            if(absentHeart.Any(l=>l.Kind==VisualLabelKind.Hitpoints))
                throw new InvalidOperationException("Visible number alone without red orb must never become HP");
            using var noHudForHp=new Bitmap(healthWithoutMap);
            using(var g=Graphics.FromImage(noHudForHp))
                g.FillRectangle(Brushes.Black,430,575,87,24);
            if((await ExecutionVision.ReadHealth(noHudForHp,engine,Array.Empty<VisualLabel>()))
                .Any(l=>l.Kind==VisualLabelKind.Hitpoints))
                throw new InvalidOperationException("Unverified HUD must never authorize standalone health OCR");
            // Never activate the shortcut from just an Inventory plate or from
            // partial menu text. The original complete OCR path remains.
            using var missingHudFooter=new Bitmap(nativeContext);
            using(var g=Graphics.FromImage(missingHudFooter))
                g.FillRectangle(Brushes.Black,430,575,87,24);
            if(await FocusedInventoryMenu(missingHudFooter,engine,slot25Anchor) is not null||
                await FocusedInventoryMenu(nativeContext,engine,slot25Anchor,postPlay:true) is not null)
                throw new InvalidOperationException("Unverified native HUD or post-login screen cannot bypass full-screen OCR");
            using var missingMenuHeading=new Bitmap(nativeContext);
            using(var g=Graphics.FromImage(missingMenuHeading))
                g.FillRectangle(Brushes.Black,755,467,225,32);
            if(await FocusedInventoryMenu(missingMenuHeading,engine,slot25Anchor) is not null)
                throw new InvalidOperationException("Incomplete menu without visible header cannot use fast inventory OCR");
            // The slot-22 footer was recognized four times, but its
            // Examine line remained unreadable. Exercise a separate 5x
            // native-pixel OCR strip with text that extends beyond the
            // previous 955px right edge of the tight context crop.
            using var farRightRows=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(farRightRows))
            using(var font=new Font("Segoe UI",14))
            {
                g.Clear(Color.Black);
                g.DrawString("Examine Logs",font,Brushes.White,850,552);
                g.DrawString("Cancel",font,Brushes.White,850,572);
            }
            var farRightCancel=new Rectangle(850,576,75,14);
            var footerContrast=await ContextMenuOcr.ReadFooterRows(
                farRightRows,engine,new Point(860,508),farRightCancel,contrast:true);
            var footerOriginal=await ContextMenuOcr.ReadFooterRows(
                farRightRows,engine,new Point(860,508),farRightCancel,contrast:false);
            bool FooterHasBoth(IReadOnlyList<VisualLabel> found)=>
                found.Any(l=>l.Text.Trim().Equals("Examine Logs",StringComparison.OrdinalIgnoreCase))&&
                found.Any(l=>l.Text.Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase));
            if(!FooterHasBoth(footerContrast)&&!FooterHasBoth(footerOriginal))
                throw new InvalidOperationException("Right-edge footer OCR must independently read Examine and Cancel pixels");
            if(ExecutionUi.Menu(footerContrast) is not null||
                ExecutionUi.Menu(footerOriginal) is not null)
                throw new InvalidOperationException("Examine and Cancel alone cannot invent a missing menu header");
            // At final-column slot 23 even broad OCR Cancel can be absent.
            // The cursor-specific corner crop must independently see a
            // complete native-pixel menu, without requiring a pre-read Cancel.
            var cornerContrast=await ContextMenuOcr.ReadCornerRows(
                lowerEdgeMenu,engine,new Point(902,508),contrast:true);
            var cornerOriginal=await ContextMenuOcr.ReadCornerRows(
                lowerEdgeMenu,engine,new Point(902,508),contrast:false);
            if(ExecutionUi.Menu(cornerContrast)?.Exact("Examine Logs") is null&&
                ExecutionUi.Menu(cornerOriginal)?.Exact("Examine Logs") is null)
                throw new InvalidOperationException("Last-column independent menu OCR must read real Examine and Cancel");
            // Live slot 19: (902,472) can render the context menu upward.
            // The old corner crop y=420..600 loses an upper heading even
            // though the right-click changes the inventory-panel pixels.
            using var flippedRowFour=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(flippedRowFour))
            using(var font=new Font("Segoe UI",14))
            {
                g.Clear(Color.Black);
                g.DrawString("Choose Option",font,Brushes.White,755,350);
                g.DrawString("Use Logs",font,Brushes.White,755,375);
                g.DrawString("Examine Logs",font,Brushes.White,755,400);
                g.DrawString("Cancel",font,Brushes.White,755,425);
            }
            var rowFourAnchor=new Point(902,472);
            var aboveContrast=await ContextMenuOcr.ReadRightEdgeRows(
                flippedRowFour,engine,rowFourAnchor,above:true,contrast:true);
            var aboveOriginal=await ContextMenuOcr.ReadRightEdgeRows(
                flippedRowFour,engine,rowFourAnchor,above:true,contrast:false);
            bool HasFullMenu(IReadOnlyList<VisualLabel> labels)=>
                ExecutionUi.Menu(labels)?.Exact("Examine Logs") is not null&&
                ExecutionUi.Menu(labels)?.Exact("Cancel") is not null;
            if(!HasFullMenu(aboveContrast)&&!HasFullMenu(aboveOriginal))
                throw new InvalidOperationException("Slot-19 upper right edge must OCR real header, Examine and Cancel");
            var verifiedUpper=await ContextMenuOcr.Read(flippedRowFour,engine,rowFourAnchor);
            if(ExecutionUi.Menu(verifiedUpper)?.Exact("Examine Logs") is null)
                throw new InvalidOperationException("Slot-19 flipped menu requires independently verified full menu");
            // The lower alternative still recognizes menus opening down.
            using var downwardRowFour=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(downwardRowFour))
            using(var font=new Font("Segoe UI",14))
            {
                g.Clear(Color.Black);
                g.DrawString("Choose Option",font,Brushes.White,755,456);
                g.DrawString("Use Logs",font,Brushes.White,755,480);
                g.DrawString("Examine Logs",font,Brushes.White,755,504);
                g.DrawString("Cancel",font,Brushes.White,755,528);
            }
            var belowContrast=await ContextMenuOcr.ReadRightEdgeRows(
                downwardRowFour,engine,rowFourAnchor,above:false,contrast:true);
            var belowOriginal=await ContextMenuOcr.ReadRightEdgeRows(
                downwardRowFour,engine,rowFourAnchor,above:false,contrast:false);
            if(!HasFullMenu(belowContrast)&&!HasFullMenu(belowOriginal))
                throw new InvalidOperationException("Slot-19 lower right edge must OCR real header, Examine and Cancel");
            using var missingUpperHeading=new Bitmap(flippedRowFour);
            using(var g=Graphics.FromImage(missingUpperHeading))
                g.FillRectangle(Brushes.Black,new Rectangle(744,344,235,31));
            var noUpperHeader=await ContextMenuOcr.ReadRightEdgeRows(
                missingUpperHeading,engine,rowFourAnchor,above:true,contrast:true);
            if(ExecutionUi.Menu(noUpperHeader) is not null)
                throw new InvalidOperationException("Edge menu without a visible header is not actionable");
            var emptyCorner=await ContextMenuOcr.ReadCornerRows(
                farRightRows,engine,new Point(902,508),contrast:true);
            if(ExecutionUi.Menu(emptyCorner) is not null)
                throw new InvalidOperationException("Cancel/Examine without a real menu header is not an action");
            using var cornerWithoutHeader=new Bitmap(bottomRightMenu);
            using(var g=Graphics.FromImage(cornerWithoutHeader))
                g.FillRectangle(Brushes.Black,new Rectangle(745,370,225,35));
            var missingCornerHeader=await ContextMenuOcr.Read(cornerWithoutHeader,engine,new Point(860,508));
            if(ExecutionUi.Menu(missingCornerHeader) is not null)
                throw new InvalidOperationException("Bottom-right menu header cannot be inferred from Cancel");
            // Simulate real-client OCR: the menu body is readable, but the
            // heading was entirely missed by the broad recognition pass.
            // Only independently reading pixels that actually spell the
            // heading may recover it.
            var menuBody=new[]{
                new VisualLabel("Examine Logs",new Rectangle(40,96,135,19)),
                new VisualLabel("Cancel",new Rectangle(40,124,85,19))
            };
            var recoveredHeader=await ContextMenuOcr.RecoverHeader(
                contextFixture,engine,new Point(60,72),menuBody);
            if(recoveredHeader is null||recoveredHeader.Text!="Choose Option"||
                recoveredHeader.Bounds.Top>=menuBody[0].Bounds.Top)
                throw new InvalidOperationException("Focused header-only OCR recovery regression");
            using var noHeader=new Bitmap(contextFixture);
            using(var g=Graphics.FromImage(noHeader))g.FillRectangle(Brushes.Black,15,25,245,40);
            if(await ContextMenuOcr.RecoverHeader(noHeader,engine,new Point(60,72),menuBody) is not null)
                throw new InvalidOperationException("Header cannot be inferred from Examine/Cancel alone");
            using var inventoryFixture=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(inventoryFixture))
            using(var font=new Font("Segoe UI",8))
            {
                g.Clear(Color.Black);
                var b=ExecutionUi.InventorySlot(0);g.DrawString("125",font,Brushes.Yellow,b.Location);
                var other=ExecutionUi.InventorySlot(1);g.DrawString("12K",font,Brushes.Yellow,other.Location);
            }
            var quantities=await ExecutionVision.ReadQuantities(inventoryFixture,engine);
            if(!quantities.Any(l=>l.Text=="0=125")||quantities.Any(l=>l.Text.StartsWith("1=")))
                throw new InvalidOperationException("Exact inventory quantity OCR regression");
            using var hudFixture=new Bitmap(1000,600);
            using(var g=Graphics.FromImage(hudFixture))
            using(var font=new Font("Segoe UI",8,FontStyle.Regular))
            {
                g.Clear(Color.FromArgb(48,56,35));
                foreach(var item in new[]{("Public",120),("Private",182),("Trade",368),("Report",430)})
                    g.DrawString(item.Item1,font,Brushes.White,item.Item2,575);
                g.DrawString("Example: *",font,Brushes.White,10,556);
            }
            var focusedHud=await GameplayHudOcr.Read(hudFixture,engine);
            var hudObservation=GameScreenReader.Read(new(new(1),new(0,0,1000,600),DateTimeOffset.UtcNow,focusedHud));
            if(hudObservation.Stage!=VisibleGameStage.InGame||hudObservation.Character!="Example"||hudObservation.Skills is not null)
                throw new InvalidOperationException("Focused chat controls / character prompt OCR regression: "+hudObservation.Stage+" fixture="+
                    string.Join(";",focusedHud.Select(l=>l.Text+"@"+l.Bounds)));
            using var skillFixture=new Bitmap(1000,600);
            int[] nativeLevels={23,24,1,23,1,1,23,1,2,1,1,7,11,1,2,1,1,1,1,1,1,1,1,1};
            using(var g=Graphics.FromImage(skillFixture))
            using(var font=new Font("Segoe UI",8))
            {
                g.Clear(Color.FromArgb(100,90,60));
                for(int i=0;i<24;i++)
                {
                    g.DrawString("99",font,Brushes.Yellow,786+i%3*62,304+i/3*30);
                    g.DrawString(nativeLevels[i].ToString(),font,Brushes.Yellow,794+i%3*62,316+i/3*30);
                }
            }
            var skillLabels=new[]{new VisualLabel("Total level: 131",new(805,550,80,10))};
            var readSkills=await SkillGridOcr.Read(skillFixture,engine,skillLabels);
            if(readSkills.Count!=24||readSkills.Sum(l=>int.Parse(l.Text[(l.Text.IndexOf('=')+1)..]))!=131)
                throw new InvalidOperationException("Yellow base-skill digit contact sheet regression: "+readSkills.Count+"/24");
            Console.WriteLine("OCR FIXTURE PASS: Windows OCR -> known-screen policy, no capture or input");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("OCR FIXTURE FAIL: "+ex);return 1;}
    }
    private async Task<bool> Dispatch(VisualFrame frame,VisualDecision decision,IntPtr owner,CancellationToken ct)
    {
        if(decision.RuntimeInput is null&&!StartupTiming.CanDispatch(previousAction,decision.State,lastAction,DateTimeOffset.UtcNow))
        {SetState("WAITING_FOR_TRANSITION_"+decision.State);return false;}
        if(decision.State.StartsWith("WORLD_PAGE_")&&pageActions>=12)
        {SetState("WORLD_PAGE_LIMIT_REACHED");return false;}
        if(decision.RuntimeInput is null&&previousAction==decision.State&&repeatedActions>=3)
        {SetState("ACTION_RETRY_LIMIT_"+decision.State);return false;}
        var box=decision.Click!.Value;
        if(box.Width<2||box.Height<2||!new Rectangle(Point.Empty,frame.ScreenBounds.Size).Contains(box))return false;
        if(Paused||ObserveOnly||GetAncestor(frame.Window,2)!=owner)return false;
        if(DateTimeOffset.UtcNow-frame.CapturedUtc>TimeSpan.FromSeconds(2))
        {SetState("INPUT_FRAME_TOO_OLD");return false;}
        var currentOrigin=new POINT();
        if(!GetClientRect(frame.Window,out var currentRect)||!ClientToScreen(frame.Window,ref currentOrigin)||
           new Rectangle(currentOrigin.X,currentOrigin.Y,currentRect.Right,currentRect.Bottom)!=frame.ScreenBounds)return false;
        var point=new Point(box.X+box.Width/2,box.Y+box.Height/2);
        var kind=decision.RuntimeInput?.Kind??(decision.MoveOnly?GameInputKind.Hover:GameInputKind.LeftClick);
        if(kind==GameInputKind.Text)
        {
            var input=decision.RuntimeInput!;
            bool allowed=input.Submit?ExecutionUi.AmountPrompt(frame.Labels)&&Regex.IsMatch(input.Text??"",@"^[1-9]\d{0,8}$"):
                (ExecutionUi.ItemSearchPrompt(frame.Labels)||ExecutionUi.BankSearchPrompt(frame.Labels))&&
                Regex.IsMatch(input.Text??"",@"^[a-zA-Z0-9 ()'\-]{1,50}$");
            if(!allowed)return false;
        }
        bool queued=kind switch
        {
            GameInputKind.Hover=>BackgroundWindowInput.Move(frame.Window,point),
            GameInputKind.Escape=>BackgroundWindowInput.Escape(
                frame.Window,contextMenuAnchor??SkillsHoverPolicy.RestPoint),
            GameInputKind.Text=>await BackgroundWindowInput.TypeTextAsync(frame.Window,point,decision.RuntimeInput!.Text!,
                decision.RuntimeInput.Submit,()=>Paused||ObserveOnly,ct),
            GameInputKind.WheelDown or GameInputKind.WheelUp or
                GameInputKind.WheelDownGameWindow or GameInputKind.WheelUpGameWindow=>
                BackgroundWindowInput.Wheel(frame.Window,point,
                    kind is GameInputKind.WheelDown or GameInputKind.WheelDownGameWindow,
                    ()=>Paused||ObserveOnly,
                    kind is GameInputKind.WheelDownGameWindow or GameInputKind.WheelUpGameWindow),
            _=>await BackgroundWindowInput.ClickAsync(frame.Window,point,()=>Paused||ObserveOnly,ct,kind==GameInputKind.RightClick)
        };
        if(!queued)
        {SetState("INPUT_DISPATCH_FAILED_REOBSERVING");return false;}
        // Capture the BEFORE-right-click inventory evidence here. The
        // subsequent screenshot may contain a context menu overlay covering
        // the same slot; only this baseline is usable for blank classification.
        if(kind==GameInputKind.RightClick&&frame.Pixels is { InventoryTabActive:true } baseline)
        {
            int slot=Enumerable.Range(0,28).FirstOrDefault(i=>
                ExecutionUi.InventorySlot(i).Contains(point),-1);
            if(slot>=0)
                log.Add(Severity.INFO,"GAMEPLAY","INVENTORY_PRE_CLICK_DIAGNOSTIC",
                    $"slot={slot} empty_candidates={baseline.Slots.Count(s=>s.Empty)} "+
                    $"slot_empty={baseline.Slots[slot].Empty} "+
                    $"empty_pixel_quality={ExecutionVision.SlotDiagnostic(baseline,slot)}"+
                    " image_phase=BEFORE_RIGHT_CLICK");
        }
        // Context OCR only runs after a successful directed right-click, and
        // the anchor is invalidated by the very next non-right-click input.
        // Captured frames are always independent of the dispatched input.
        contextMenuAnchor=kind==GameInputKind.RightClick?point:null;
        contextMenuPanelBefore=kind==GameInputKind.RightClick?frame.Pixels?.InventoryPanelSignature:null;
        lastContextDiagnostic="";
        // A click on the Skills tab leaves the game mouse over the tab.
        // Clear the hover BEFORE the next capture; never click in the world.
        bool afterOpen=SkillsHoverPolicy.AfterOpen(decision.State,kind,frame.ScreenBounds.Size);
        if(afterOpen&&!BackgroundWindowInput.Move(frame.Window,SkillsHoverPolicy.RestPoint))
        {
            skillsHoverCleared=false;
            SetState("SKILLS_HOVER_CLEAR_FAILED_REOBSERVING");
            return false;
        }
        if(afterOpen||decision.State=="CLEAR_SKILLS_HOVER"&&kind==GameInputKind.Hover)
        {
            skillsHoverCleared=true;
            log.Add(Severity.INFO,"VISUAL_INPUT","SKILLS_HOVER_CLEARED",
                "cursor=NEUTRAL_GAME_VIEW tooltip_clear=REQUESTED independent_next_frame_required=true");
        }
        repeatedActions=previousAction==decision.State?repeatedActions+1:1;
        if(decision.State.StartsWith("WORLD_PAGE_"))pageActions++;
        previousAction=decision.State;
        lastAction=DateTimeOffset.UtcNow;
        if(decision.State=="ENTER_GAME")postEnterUnknownDiagnosed=false;
        SetState(decision.State+"_REQUESTED");
        log.Add(Severity.INFO,"VISUAL_INPUT","ACTION_DISPATCHED",
            $"action={decision.State} input=WINDOW_MESSAGE background=true global_cursor_moved=false "+
            "success_requires_next_observation=true");
        return true;
    }
    private void ResetEnter()
    {
        pendingEnterBounds=null;
        pendingEnterFrame=default;
    }
    private bool EnterConfirmed(Rectangle bounds,DateTimeOffset captured)
    {
        if(pendingEnterBounds==bounds&&captured-pendingEnterFrame<TimeSpan.FromSeconds(10))
            return captured-pendingEnterFrame>=TimeSpan.FromMilliseconds(250);
        pendingEnterBounds=bounds;
        pendingEnterFrame=captured;
        return false;
    }
    private void DiagnoseSkillOcr(VisualFrame frame)
    {
        var read=Game.LastObservation;
        if(read?.Stage!=VisibleGameStage.InGame||read.TotalLevel is null)
        {lastSkillOcrDiagnostic="";return;}
        if(read.RecognizedSkills==24&&read.Character is not null)
        {lastSkillOcrDiagnostic="";return;}
        // Log skill names only, never raw character text or OCR strings.
        var values=frame.Labels.Where(l=>l.Kind==VisualLabelKind.SkillValue).ToArray();
        var missing=GameScreenReader.GridSkills.Where(name=>!values.Any(l=>l.Text.StartsWith(name+"=",StringComparison.Ordinal)));
        int focused=frame.Labels.Count(l=>l.Kind==VisualLabelKind.CharacterPrompt);
        string diagnostic="missing_skills="+string.Join(",",missing)+
            " focused_prompt_candidates="+focused+
            " character_prompt_seen="+(read.Character is not null);
        if(diagnostic==lastSkillOcrDiagnostic)return;
        lastSkillOcrDiagnostic=diagnostic;
        log.Add(Severity.INFO,"GAME_OBSERVER","OCR_GAPS",diagnostic);
    }
    private void DiagnosePostPlay(VisualFrame observed)
    {
        if(previousAction is not ("PLAY_SELECTED_CHARACTER" or "ENTER_GAME"))return;
        // Privacy rule: only known UI booleans and counts; no arbitrary OCR,
        // chat, character names, screenshots or image hashes in log output.
        var labels=observed.Labels;
        bool welcome=labels.Any(l=>l.Text.Contains("Welcome",StringComparison.OrdinalIgnoreCase));
        bool enter=labels.Any(l=>l.Text.Equals(PostPlayScreenReader.ClickText,StringComparison.OrdinalIgnoreCase));
        bool nativeCard=labels.Any(l=>l.Kind==VisualLabelKind.EnterButton);
        bool connecting=labels.Any(l=>l.Text.Contains("Connecting to server",StringComparison.OrdinalIgnoreCase)||
            l.Text.Equals("Please wait",StringComparison.OrdinalIgnoreCase));
        bool chat=labels.Any(l=>l.Kind==VisualLabelKind.GameHud)||labels.Any(l=>l.Text.Equals("Public",StringComparison.OrdinalIgnoreCase))&&
            labels.Any(l=>l.Text.Equals("Private",StringComparison.OrdinalIgnoreCase));
        string result=$"stage={Game.LastObservation?.Stage} welcome_marker={welcome} "+
            $"enter_marker={enter} native_enter_card={nativeCard} connecting_marker={connecting} chat_controls={chat} "+
            $"labels={labels.Count} decision_source=VISIBLE_OCR auth_verified=false";
        DateTimeOffset now=DateTimeOffset.UtcNow;
        if(!postEnterUnknownDiagnosed&&
            PostEnterUnknownScreenPolicy.IsUnknownAfterEnter(previousAction,lastAction,now,
                Game.LastObservation?.Stage??VisibleGameStage.Unknown)&&
            now-lastAction>=PostEnterUnknownScreenPolicy.DiagnosticAfter)
        {
            postEnterUnknownDiagnosed=true;
            var px=lastWelcomePixels;
            var footer=lastHudFooterPixels;
            log.Add(Severity.INFO,"VISUAL_STARTUP","POST_ENTER_PIXEL_DIAGNOSTIC",
                $"elapsed_s={(int)(now-lastAction).TotalSeconds} parchment_banner={px.Banner} "+
                $"red_center={px.RedCenter} side_tiles={px.SideTiles} white_caption={px.Caption} "+
                $"minimap_marker={labels.Any(l=>l.Kind==VisualLabelKind.Minimap)} "+
                $"skills_icon_marker={labels.Any(l=>l.Kind==VisualLabelKind.SkillsButton)} "+
                $"native_hud_marker={labels.Any(l=>l.Kind==VisualLabelKind.GameHud)} "+
                $"footer_public={footer.Public} footer_private={footer.Private} "+
                $"footer_trade={footer.Trade} report_background={footer.ReportBackground} "+
                $"report_caption={footer.ReportCaption} "+
                "screenshot_persisted=false retry_input=false");
        }
        if(result==lastPostPlayDiagnostic&&now-lastPostPlayDiagnosticAt<TimeSpan.FromSeconds(20))
            return;
        lastPostPlayDiagnostic=result;
        lastPostPlayDiagnosticAt=now;
        log.Add(Severity.INFO,"VISUAL_STARTUP","POST_PLAY_SCREEN_DIAGNOSTIC",result);
    }

    private void ResetPlayButton()
    {
        pendingPlayBounds=null;
        pendingPlayFrame=default;
    }
    private bool PlayButtonConfirmed(Rectangle bounds,DateTimeOffset captured)
    {
        if(pendingPlayBounds==bounds&&captured-pendingPlayFrame<TimeSpan.FromSeconds(10))
            return captured-pendingPlayFrame>=TimeSpan.FromMilliseconds(250);
        pendingPlayBounds=bounds;
        pendingPlayFrame=captured;
        return false;
    }

    private void ResetWorldChoice()
    {
        pendingWorldChoice=null;
        pendingWorldChoiceBounds=null;
        pendingWorldChoiceFrame=default;
    }
    private bool WorldCellConfirmed(int? world,Rectangle bounds,DateTimeOffset frameTime)
    {
        if(world is null)return false;
        if(pendingWorldChoice==world&&pendingWorldChoiceBounds==bounds&&frameTime-pendingWorldChoiceFrame<TimeSpan.FromSeconds(10))
            return frameTime-pendingWorldChoiceFrame>=TimeSpan.FromMilliseconds(250);
        // Always keep the most recent unconfirmed candidate, including when
        // the screenshot timestamp or cell changed.
        pendingWorldChoice=world;
        pendingWorldChoiceBounds=bounds;
        pendingWorldChoiceFrame=frameTime;
        return false;
    }

    private void DiagnoseWorldGrid(VisualFrame frame,int? desiredWorld,VisualDecision decision)
    {
        // Summaries only: never log full OCR text, chat, screenshots or pixels.
        if(GameScreenReader.Read(frame).Stage!=VisibleGameStage.WorldSelection)
        {
            lastWorldGridDiagnostic="";
            return;
        }
        var cells=frame.Labels.Where(l=>l.Kind==VisualLabelKind.WorldCell&&
            int.TryParse(l.Text,out int n)&&n is >=301 and <=800)
            .Select(l=>int.Parse(l.Text)).ToArray();
        int raw=frame.Labels.Count(l=>l.Kind==VisualLabelKind.Number&&
            int.TryParse(l.Text,out int n)&&n is >=301 and <=800);
        int next=frame.Labels.Count(l=>l.Kind==VisualLabelKind.WorldNavigation&&l.Text=="NEXT");
        int previous=frame.Labels.Count(l=>l.Kind==VisualLabelKind.WorldNavigation&&l.Text=="PREVIOUS");
        string summary=$"validated_cells={cells.Length} raw_world_like_numbers={raw} "+
            $"range={(cells.Length>0?cells.Min().ToString()+"-"+cells.Max():"unknown")} "+
            $"target={desiredWorld?.ToString()??"none"} "+
            $"target_cells={cells.Count(n=>n==desiredWorld)} next_arrows={next} previous_arrows={previous} "+
            $"native_ocr={WorldGridOcr.NativeDiagnostic.Replace(' ','_')} decision={decision.State}";
        if(summary==lastWorldGridDiagnostic)return;
        lastWorldGridDiagnostic=summary;
        log.Add(Severity.INFO,"VISUAL_STARTUP","WORLD_GRID_DIAGNOSTIC",summary);
    }

    private void SetState(string state)
    {
        State=state;
        if(state==lastState)return;
        lastState=state;
        log.Add(Severity.INFO,"VISUAL_STARTUP","STATE",$"state={state}");
        if(state.StartsWith("CAPTURE_")&&state.Contains("CLIPPED"))
            log.Add(Severity.WARN,"VISUAL_STARTUP","CAPTURE_GEOMETRY",backgroundCapture.Geometry);
        if(state=="WORLD_LABEL_NOT_RECOGNIZED")log.Add(Severity.WARN,"VISUAL_STARTUP","WORLD_FOOTER_OCR",
            footerDiagnostic);
    }
    public void Dispose(){backgroundCapture.Dispose();Gameplay.Dispose();}
    [StructLayout(LayoutKind.Sequential)]private struct POINT{internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct RECT{internal int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr h,ref POINT p);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
}

internal static class VisualStartupTests
{
    internal static void Run()
    {
        static VisualLabel Label(string text,int y)=>new(text,new Rectangle(20,y,200,25));
        static void Require(bool value,string reason){if(!value)throw new Exception("Visual startup: "+reason);}
        var incompleteSkills=new VisibleGameObservation(VisibleGameStage.InGame,null,null,131,23,DateTimeOffset.UtcNow,107);
        Require(SkillsHoverPolicy.RestPoint==new Point(500,400)&&
            SkillsHoverPolicy.RestTarget.Contains(SkillsHoverPolicy.RestPoint),
            "neutral game-view mouse target");
        Require(!new Rectangle(740,260,260,340).Contains(SkillsHoverPolicy.RestPoint)&&
            !new Rectangle(0,450,740,150).Contains(SkillsHoverPolicy.RestPoint),
            "hover target must not cover skill panel or chat");
        Require(SkillsHoverPolicy.AfterOpen("OPEN_SKILLS",GameInputKind.LeftClick,new(1000,600))&&
            !SkillsHoverPolicy.AfterOpen("HOVER_SKILLS",GameInputKind.Hover,new(1000,600))&&
            !SkillsHoverPolicy.AfterOpen("OPEN_SKILLS",GameInputKind.LeftClick,new(800,600)),
            "only a verified native Skills click triggers immediate mouse clearing");
        Require(SkillsHoverPolicy.NeedsClear(false,incompleteSkills,new(1000,600))&&
            !SkillsHoverPolicy.NeedsClear(true,incompleteSkills,new(1000,600))&&
            !SkillsHoverPolicy.NeedsClear(false,incompleteSkills with {TotalLevel=null},new(1000,600))&&
            !SkillsHoverPolicy.NeedsClear(false,incompleteSkills with {RecognizedSkills=24,
                Skills=GameScreenReader.GridSkills.Select(n=>new PublicOsrsSkill(n,1,-1,-1)).ToArray()},new(1000,600)),
            "already-open partial Skills panel gets one hover clearing, never an infinite retry");
        var welcome=new[]{Label("Welcome to Old School RuneScape",10),Label("Play Now",60),Label("World 417",250)};
        Require(VisualStartupPolicy.Decide(welcome,null).Click is null,"wait for measured world");
        Require(VisualStartupPolicy.Decide(welcome,417).State=="PLAY_SELECTED_CHARACTER","matching world before play");
        var guardedButton=new VisualLabel("Play Now",WelcomePlayButtonReader.ButtonTextBounds,
            VisualLabelKind.PlayButton);
        var welcomeWithoutPlay=new[]{welcome[0],welcome[2]};
        Require(VisualStartupPolicy.Decide(welcomeWithoutPlay,417).Click is null,
            "do not guess Play Now click without OCR or pixel confirmation");
        Require(VisualStartupPolicy.Decide(welcomeWithoutPlay.Append(guardedButton).ToArray(),417)
            .State=="PLAY_SELECTED_CHARACTER",
            "strongly validated image button is actionable when full-frame OCR misses text");
        Require(VisualStartupPolicy.Decide(welcome,397).State=="OPEN_WORLD_SELECTION","wrong world is corrected before login");
        foreach(string footer in new[]{"World397","WorId 397","Wor1d 397 Click to switch","World 397 Click to switch"})
            Require(VisualStartupPolicy.Decide(new[]{welcome[0],welcome[1],Label(footer,250)},397).State=="PLAY_SELECTED_CHARACTER",
                "native combined footer and pixel-font l: "+footer);
        Require(VisualStartupPolicy.Decide(welcome.Append(Label("World 417",260)).ToArray(),417).State=="PLAY_SELECTED_CHARACTER",
            "duplicate line/word world evidence");
        Require(VisualStartupPolicy.Decide(welcome.Append(Label("World 397",260)).ToArray(),417).Click is null,
            "conflicting footer worlds block input");
        Require(VisualStartupPolicy.Decide(welcome.Append(Label("password",100)).ToArray(),417).Click is null,
            "credentials screens are manual");
        Require(VisualStartupPolicy.Decide(new[]{Label("Play Now",20)},417).Click is null,"isolated text is not a welcome screen");
        var grid=new[]{Label("Select a world",10),Label("World",40),Label("397",80)};
        var nativeFooter=new[]{new VisualLabel("397",new Rectangle(180,550,25,12)),
            new VisualLabel("Click switch",new Rectangle(155,565,75,10))};
        Require(WorldFooterPolicy.Read(nativeFooter,new(1000,600)).Single().Text=="World 397","dropped World word in native pixel OCR");
        Require(WorldFooterPolicy.Read(nativeFooter.Append(new VisualLabel("399",new Rectangle(181,536,25,12))).ToArray(),new(1000,600)).Count==0,
            "two nearby numbers cannot identify current world");
        Require(WorldFooterPolicy.Read(new[]{nativeFooter[0]},new(1000,600)).Count==0,"bare number never identifies footer");
        Require(VisualStartupPolicy.Decide(grid,397).State=="SELECT_F2P_WORLD","select visible measured world");
        Require(VisualStartupPolicy.Decide(grid.Append(Label("397",120)).ToArray(),397).Click is null,
            "ambiguous player counts cannot become a world click");
        Require(VisualStartupPolicy.Decide(grid,417).Click is null,"offscreen world never gets guessed");
        var partialPage=new[]{Label("Select a world",10),
            new VisualLabel("301",new(180,40,25,12),VisualLabelKind.WorldCell),
            new VisualLabel("325",new(180,490,25,12),VisualLabelKind.WorldCell),
            new VisualLabel("NEXT",new(835,282,48,42),VisualLabelKind.WorldNavigation)};
        Require(VisualStartupPolicy.Decide(partialPage,399).State==
            "WORLD_PAGE_COVERAGE_INSUFFICIENT_NO_BLIND_ARROW",
            "partially read left column cannot page away from visible world 399");
        var fullPageCells=Enumerable.Range(0,5).SelectMany(col=>
            Enumerable.Range(0,5).Select(row=>new VisualLabel(
                (397+col*17+row).ToString(),
                new Rectangle(180+col*130,40+row*19,25,12),VisualLabelKind.WorldCell))).ToArray();
        var paged=new[]{Label("Select a world",10)}
            .Concat(fullPageCells).Append(new VisualLabel("NEXT",new(835,282,48,42),
                VisualLabelKind.WorldNavigation)).ToArray();
        Require(VisualStartupPolicy.PageCoverageVerified(fullPageCells),"complete world-page coverage confirmed");
        Require(VisualStartupPolicy.Decide(paged,553).State.StartsWith("WORLD_PAGE_NEXT_"),
            "next page requires verified columns across full table");
        Require(VisualStartupPolicy.Decide(paged,405).Click is null,
            "missing OCR within displayed range does not page blindly");
        Require(VisualStartupPolicy.Decide(paged.Take(paged.Length-1).ToArray(),553).Click is null,
            "no page click without independently identified arrow");
        // Regression: a partial OCR contact-sheet pass must not erase the
        // original verified column; a missing world may be added only after
        // the combined ascending sequence is checked.
        var coarse=Enumerable.Range(301,6).Select((n,i)=>
            new VisualLabel(n.ToString(),new(180,40+i*19,25,12),VisualLabelKind.WorldCell)).ToArray();
        var focused=new[]{new VisualLabel("307",new(180,40+6*19,25,12),VisualLabelKind.Number)};
        var titleOnly=new[]{Label("Select a world",5)};
        Require(WorldGridOcr.MergeValidated(coarse,Array.Empty<VisualLabel>(),titleOnly).Count==6,
            "empty focused OCR retains original validated column");
        Require(WorldGridOcr.MergeValidated(coarse,focused,titleOnly).Count==7,
            "partial focused OCR adds a world only after ascending-column validation");
        Require(WorldGridOcr.MergeValidated(coarse,new[]{new VisualLabel("399",coarse[2].Bounds,VisualLabelKind.Number)},titleOnly).Count==6,
            "contradictory number cannot override an existing verified cell");
        Require(WorldGridOcr.MergeValidated(coarse,
            new[]{new VisualLabel("399",new(550,130,25,12),VisualLabelKind.Number)},titleOnly).Count==6,
            "isolated unrelated number cannot join a verified world column");
        // Fixed-native geometry is a SEARCH REGION only, not a trusted
        // world-number mapping. It requires visible raster-pill evidence.
        using var blankNative=new Bitmap(1000,600);
        var nativeHeader=new[]{new VisualLabel("Select a world",new(135,2,119,16))};
        Require(!WorldGridOcr.HasNativeTable(blankNative,nativeHeader),
            "world title alone cannot activate native OCR coordinates");
        using var nativePills=new Bitmap(1000,600);
        using(var paint=Graphics.FromImage(nativePills))
        {
            paint.Clear(Color.Black);
            for(int col=0;col<7;col++)
            for(int row=0;row<24;row++)
            {
                var bounds=WorldGridOcr.NativeNumberRegion(col,row);
                paint.FillRectangle(Brushes.LightGray,bounds);
                paint.FillRectangle(Brushes.Black,bounds.X+4,bounds.Y+2,3,8);
                paint.FillRectangle(Brushes.Black,bounds.X+13,bounds.Y+2,3,8);
                paint.FillRectangle(Brushes.Black,bounds.X+21,bounds.Y+2,2,8);
            }
        }
        Require(WorldGridOcr.HasNativeTable(nativePills,nativeHeader),
            "verified native world grid enables isolated black-digit OCR");
        Require(WorldGridOcr.NativeNumberRegion(4,1).Contains(new Point(560,60)),
            "world 399 screenshot location is in native fifth-column second-row crop");
        using var arrowImage=new Bitmap(1000,600);
        var pageLabels=Enumerable.Range(301,25).Select(n=>new VisualLabel(n.ToString(),new(180,40,25,12),VisualLabelKind.WorldCell))
            .Append(Label("Select a world",5)).ToArray();
        Require(WorldPageReader.Read(arrowImage,pageLabels).Count==0,"blank arrow area blocks pagination");
        using(var g=Graphics.FromImage(arrowImage))g.FillRectangle(Brushes.Gray,835,290,48,20);
        Require(WorldPageReader.Read(arrowImage,pageLabels).Count==0,"flat grey patch is not an arrow");
        var postEnterAt=DateTimeOffset.UtcNow;
        Require(!PostEnterUnknownScreenPolicy.ShouldBlock("ENTER_GAME",postEnterAt,postEnterAt.AddSeconds(29),
            VisibleGameStage.Unknown,"WAITING_FOR_RECOGNIZED_SCREEN"),
            "post-entry unknown is allowed a bounded transition window");
        Require(PostEnterUnknownScreenPolicy.ShouldBlock("ENTER_GAME",postEnterAt,postEnterAt.AddSeconds(31),
            VisibleGameStage.Unknown,"WAITING_FOR_RECOGNIZED_SCREEN"),
            "prolonged unrecognized post-entry screen blocks without another click");
        Require(!PostEnterUnknownScreenPolicy.ShouldBlock("PLAY_SELECTED_CHARACTER",postEnterAt,postEnterAt.AddSeconds(31),
            VisibleGameStage.Unknown,"WAITING_FOR_RECOGNIZED_SCREEN")&&
            !PostEnterUnknownScreenPolicy.ShouldBlock("ENTER_GAME",postEnterAt,postEnterAt.AddSeconds(31),
                VisibleGameStage.Connecting,"WAITING_FOR_RECOGNIZED_SCREEN")&&
            !PostEnterUnknownScreenPolicy.ShouldBlock("ENTER_GAME",postEnterAt,postEnterAt.AddSeconds(31),
                VisibleGameStage.InGame,"WAITING_FOR_RECOGNIZED_SCREEN")&&
            !PostEnterUnknownScreenPolicy.ShouldBlock("ENTER_GAME",postEnterAt,postEnterAt.AddSeconds(31),
                VisibleGameStage.Unknown,"MANUAL_AUTHENTICATION_REQUIRED")&&
            !PostEnterUnknownScreenPolicy.ShouldBlock("ENTER_GAME",postEnterAt,postEnterAt.AddSeconds(31),
                VisibleGameStage.Unknown,"ENTER_GAME"),
            "known stage, guarded prompt and different actions cannot be timed out");
        using(var menuScreen=new Bitmap(1000,600))
        {
            using(var p=Graphics.FromImage(menuScreen))
            using(var selected=new SolidBrush(Color.FromArgb(125,31,28)))
                p.FillRectangle(selected,InventoryTabPolicy.Icon);
            Require(InventoryMenuOcrPolicy.IsContext(menuScreen,new Point(902,544))&&
                InventoryMenuOcrPolicy.IsContext(menuScreen,new Point(776,436))&&
                !InventoryMenuOcrPolicy.IsContext(menuScreen,new Point(500,400))&&
                !InventoryMenuOcrPolicy.IsContext(menuScreen,new Point(838,582)),
                "OCR fast path requires real selected Inventory tab and an actual item-slot right-click");
            Require(!InventoryMenuOcrPolicy.FastHud(true,false)&&
                InventoryMenuOcrPolicy.FastHud(true,true)&&
                !InventoryMenuOcrPolicy.FastHud(false,true),
                "full HUD OCR must remain available unless native pixels independently verify the HUD");
            using(var p=Graphics.FromImage(menuScreen))
                p.FillRectangle(Brushes.Black,InventoryTabPolicy.Icon);
            Require(!InventoryMenuOcrPolicy.IsContext(menuScreen,new Point(902,544)),
                "unselected Inventory tab cannot bypass full visual OCR");
            using var wrongMenuViewport=new Bitmap(950,600);
            Require(!InventoryMenuOcrPolicy.IsContext(wrongMenuViewport,new Point(902,544)),
                "unverified capture geometry cannot use inventory menu fast path");
        }
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome",10),Label("CLICK HERE TO PLAY",80)},417,true).State=="ENTER_GAME",
            "welcome transition");
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome",10),
            new VisualLabel(PostPlayScreenReader.ClickText,new Rectangle(410,267,170,24))},553,true)
            .State=="ENTER_GAME","focused post-play text allows the separately verified second entry");
        var nativeEntry=new VisualLabel(PostPlayScreenReader.ClickText,
            NativeWelcomePanelReader.ButtonTextBounds,VisualLabelKind.EnterButton);
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome to Old School RuneScape",10),
            nativeEntry},553,true).State=="ENTER_GAME",
            "native second entry takes precedence over Play Now welcome screen without world footer");
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome to Old School RuneScape",10),
            nativeEntry},553,false).Click is null,
            "native second entry never skips previously verified world guard");
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome to Old School RuneScape",10),
            nativeEntry,Label("password",100)},553,true).Click is null,
            "manual auth screen overrides native second entry tile");
        Require(VisualStartupPolicy.Decide(new[]{new VisualLabel(PostPlayScreenReader.ClickText,
            new Rectangle(410,267,170,24))},553,true).Click is null,
            "second-stage command alone never clicks without Welcome anchor");
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome",10),Label("CLICK HERE TO PLAY",80)},417).Click is null,
            "resumed welcome requires independently observed selected world");
        Require(VisualStartupPolicy.Decide(grid.Select(l=>l.Text=="397"?l with {Bounds=new Rectangle(500,80,50,25)}:l).ToArray(),397).Click is null,
            "population numbers outside world column are not targets");
        Console.WriteLine("VISUAL STARTUP PASS: measured-world ordering, known-screen guards, ambiguous/offscreen rejection, manual authentication");
    }
}
