using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OSRSLiveDiagnostics;

internal enum VisualLabelKind { Text, Number, WorldCell, SkillValue, WorldNavigation, InventoryQuantity, PlayButton, EnterButton, Hitpoints, Minimap, GameHud, SkillsButton, ConnectionSignal, CharacterPrompt }
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
        action=="OPEN_SKILLS"&&kind==GameInputKind.LeftClick&&size==DockedWindowLayout.GameSize;
    internal static bool NeedsClear(bool alreadyCleared,VisibleGameObservation observation,Size size)=>
        !alreadyCleared&&size==DockedWindowLayout.GameSize&&
        observation.Stage==VisibleGameStage.InGame&&observation.TotalLevel is not null&&
        (observation.RecognizedSkills!=24||observation.Skills is null);
}


internal static class WorldFooterPolicy
{
    internal static IReadOnlyList<VisualLabel> Read(IReadOnlyList<VisualLabel> labels,Size imageSize)
    {
        var switches=labels.Where(l=>Regex.IsMatch(l.Text.Trim(),@"^Click\s*(?:to\s*)?switch$",RegexOptions.IgnoreCase)&&
            l.Bounds.Left<imageSize.Width/2&&l.Bounds.Top>imageSize.Height-85).ToArray();
        if(switches.Length!=1)return Array.Empty<VisualLabel>();
        var marker=switches[0];
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

internal sealed class VisualStartup:IDisposable
{
    private readonly Diagnostics log;
    private readonly OcrEngine? ocr;
    private DateTimeOffset lastAction=DateTimeOffset.MinValue;
    private string lastState="";
    private static string footerDiagnostic="not_read";
    private int repeatedActions;
    private bool skillsHoverCleared;
    private int pageActions;
    private string previousAction="";
    private string lastWorldGridDiagnostic="";
    private string lastSkillOcrDiagnostic="";
    private int? pendingWorldChoice;
    private Rectangle? pendingWorldChoiceBounds;
    private DateTimeOffset pendingWorldChoiceFrame;
    private Rectangle? pendingPlayBounds,pendingEnterBounds;
    private DateTimeOffset pendingPlayFrame,pendingEnterFrame;
    private string lastPostPlayDiagnostic="";
    private DateTimeOffset lastPostPlayDiagnosticAt=DateTimeOffset.MinValue;
    private int? observedSelectedWorld;
    private IntPtr observedWindow;
    private readonly BackgroundWindowCapture backgroundCapture=new();
    private readonly SessionRecovery recovery=new();
    private readonly ConnectivityWatch connectivity=new();
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
    internal bool Paused {get;set;}
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
        {backgroundCapture.Release();SetState("WAITING_FOR_WINDOW");return;}
        if(observedWindow!=target)
        {observedWindow=target;observedSelectedWorld=null;Game.Reset();Gameplay.Reset();recovery.Reset();connectivity.Reset();lastRecoveryState="";lastVerifiedHud=default;previousAction="";lastAction=DateTimeOffset.MinValue;repeatedActions=0;pageActions=0;lastWorldGridDiagnostic="";lastPostPlayDiagnostic="";lastSkillOcrDiagnostic="";skillsHoverCleared=false;ResetWorldChoice();ResetPlayButton();ResetEnter();}
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
        observed=ConnectivityWatch.Signal(observed,networkLost);
        Game.Observe(GameScreenReader.Read(observed),DateTimeOffset.UtcNow);
        DiagnoseSkillOcr(observed);
        if(Game.LastObservation?.Stage==VisibleGameStage.InGame)lastVerifiedHud=observed.CapturedUtc;
        else if(lastVerifiedHud!=default&&DateTimeOffset.UtcNow-lastVerifiedHud>TimeSpan.FromSeconds(3))
        {Gameplay.Reset();lastVerifiedHud=default;}
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
                Game.Reset();Gameplay.Reset();observedSelectedWorld=null;previousAction="";repeatedActions=0;
                ResetWorldChoice();ResetPlayButton();ResetEnter();skillsHoverCleared=false;
            }
            if(lastRecoveryState!=recoveryDecision.State)
            {lastRecoveryState=recoveryDecision.State;log.Add(Severity.INFO,"RECONNECT","STATE","state="+lastRecoveryState+" pending_transactions_preserved=true");}
            if(recoveryDecision.Click is Rectangle retry)
            {
                await Dispatch(observed,new("RECONNECT_VISIBLE_BUTTON",retry,RuntimeInput:new(GameInputKind.LeftClick,retry)),owner,ct);return;
            }
            if(!recoveryDecision.ResumeLogin){SetState(recoveryDecision.State);return;}
        }
        else if(lastRecoveryState.Length>0)
        {lastRecoveryState="";log.Add(Severity.INFO,"RECONNECT","RETURNED_TO_GAME","skills_and_character_must_be_reconfirmed=true");}
        if(Game.LastObservation?.Stage==VisibleGameStage.Connecting)
        {
            SetState("POST_PLAY_CONNECTING");
            return;
        }
        if(Game.LastObservation?.Stage==VisibleGameStage.InGame)
        {
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
        var point=new POINT();
        if(!ClientToScreen(target,ref point))return null;
        var bounds=new Rectangle(point.X,point.Y,captured.Image.Width,captured.Image.Height);
        var labels=await Recognize(captured.Image,ocr!,
            postPlay: recovery.Recovering||previousAction is "PLAY_SELECTED_CHARACTER" or "ENTER_GAME");
        ct.ThrowIfCancellationRequested();
        return new(target,bounds,captured.CapturedUtc,labels,ExecutionVision.Read(captured.Image,labels));
    }
    private static async Task<IReadOnlyList<VisualLabel>> Recognize(
        Bitmap bitmap,OcrEngine engine,bool postPlay=false)
    {
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
        // A separate enlarged footer pass avoids the tiny native bitmap font
        // being merged with copyright text by full-frame OCR.
        if(labels.Any(l=>l.Text.Contains("Welcome to Old School RuneScape",StringComparison.OrdinalIgnoreCase)))
        {
            labels.AddRange(await ReadWorldFooter(bitmap,engine));
            if(WelcomePlayButtonReader.Read(bitmap,labels) is VisualLabel playButton)
                labels.Add(playButton);
        }
        var focusedWorlds=await WorldGridOcr.Read(bitmap,engine,labels);
        labels.AddRange(focusedWorlds.Count>0?focusedWorlds:WorldGridReader.Classify(labels));
        labels.AddRange(WorldPageReader.Read(bitmap,labels));
        foreach(var focused in await GameplayHudOcr.Read(bitmap,engine))
        {
            labels.RemoveAll(l=>l.Kind==focused.Kind&&l.Text.Equals(focused.Text,StringComparison.OrdinalIgnoreCase)&&l.Bounds.IntersectsWith(focused.Bounds));
            labels.Add(focused);
        }
        labels=labels.Distinct().ToList();
        // The native chat line uses P12 raster glyphs; a complete username
        // plus ':' plus its blue input cursor is independently observable
        // without relying on unreliable enlarged Windows OCR.
        if(NativeCharacterFontReader.Read(bitmap) is VisualLabel nativePrompt)
        {
            labels.RemoveAll(l=>l.Kind==VisualLabelKind.CharacterPrompt);
            labels.Add(nativePrompt);
        }
        labels.AddRange(await SkillGridOcr.Read(bitmap,engine,labels));
        labels.AddRange(await ExecutionVision.ReadHealth(bitmap,engine,labels));
        if(NativeGameHudReader.Skills(bitmap) is VisualLabel skillsButton)labels.Add(skillsButton);
        if(NativeGameHudReader.Read(bitmap,labels) is VisualLabel hud)labels.Add(hud);
        if(GameScreenReader.Read(new(IntPtr.Zero,new Rectangle(Point.Empty,bitmap.Size),DateTimeOffset.UtcNow,labels)).Stage==VisibleGameStage.InGame)
            labels.AddRange(await ExecutionVision.ReadQuantities(bitmap,engine));
        return labels;
    }
    private static async Task<IReadOnlyList<VisualLabel>> ReadWorldFooter(Bitmap bitmap,OcrEngine engine)
    {
        var crop=new Rectangle(0,Math.Max(0,bitmap.Height-100),bitmap.Width/2,Math.Min(100,bitmap.Height));
        using var enlarged=new Bitmap(crop.Width*4,crop.Height*4);
        using(var g=Graphics.FromImage(enlarged))
        {
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(bitmap,new Rectangle(Point.Empty,enlarged.Size),crop,GraphicsUnit.Pixel);
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
        footerDiagnostic=$"world_candidates={labels.Count} switch_marker="+mapped.Any(l=>l.Text.Contains("switch",StringComparison.OrdinalIgnoreCase));
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
            GameInputKind.Escape=>BackgroundWindowInput.Escape(frame.Window),
            GameInputKind.Text=>await BackgroundWindowInput.TypeTextAsync(frame.Window,point,decision.RuntimeInput!.Text!,
                decision.RuntimeInput.Submit,()=>Paused||ObserveOnly,ct),
            _=>await BackgroundWindowInput.ClickAsync(frame.Window,point,()=>Paused||ObserveOnly,ct,kind==GameInputKind.RightClick)
        };
        if(!queued)
        {SetState("INPUT_DISPATCH_FAILED_REOBSERVING");return false;}
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
