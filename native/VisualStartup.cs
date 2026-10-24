using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OSRSLiveDiagnostics;

internal enum VisualLabelKind { Text, Number, WorldCell, SkillValue }
internal sealed record VisualLabel(string Text,Rectangle Bounds,VisualLabelKind Kind=VisualLabelKind.Text);
internal sealed record VisualFrame(IntPtr Window,Rectangle ScreenBounds,
    DateTimeOffset CapturedUtc,IReadOnlyList<VisualLabel> Labels);
internal sealed record VisualDecision(string State,Rectangle? Click=null,bool MoveOnly=false);

// Only known startup screens are actionable. OCR content is never logged.
internal static class VisualStartupPolicy
{
    private static readonly Regex World=new(@"^World\s+(\d{3})$",RegexOptions.IgnoreCase);
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
            if(normalized.Any(l=>l.Kind==VisualLabelKind.WorldCell))
                return new("TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE");
            var headers=normalized.Where(l=>l.Text.Equals("World",StringComparison.OrdinalIgnoreCase)).ToArray();
            var target=Unique(l=>l.Text==preferredWorld.Value.ToString()&&headers.Any(h=>
                l.Bounds.Top>h.Bounds.Bottom&&Math.Abs(l.Bounds.Left-h.Bounds.Left)<Math.Max(30,h.Bounds.Width)));
            return target is null?new("TARGET_WORLD_NOT_UNAMBIGUOUSLY_VISIBLE"):
                new("SELECT_F2P_WORLD",target.Bounds);
        }
        bool welcome=Has("Welcome to Old School RuneScape");
        if(welcome)
        {
            var world=Unique(l=>World.IsMatch(l.Text));
            if(world is null)return new("WORLD_LABEL_NOT_RECOGNIZED");
            int selected=int.Parse(World.Match(world.Text).Groups[1].Value);
            if(selected!=preferredWorld)return new("OPEN_WORLD_SELECTION",world.Bounds);
            var play=Unique(l=>l.Text.Equals("Play Now",StringComparison.OrdinalIgnoreCase));
            return play is null?new("PLAY_BUTTON_NOT_RECOGNIZED"):
                new("PLAY_SELECTED_CHARACTER",play.Bounds);
        }
        var enter=Unique(l=>l.Text.Equals("CLICK HERE TO PLAY",StringComparison.OrdinalIgnoreCase));
        if(enter is not null && Has("Welcome"))return selectedWorldPreviouslyObserved?
            new("ENTER_GAME",enter.Bounds):new("WELCOME_WORLD_NOT_VERIFIED");
        return new("WAITING_FOR_RECOGNIZED_SCREEN");
    }
}

internal sealed class VisualStartup:IDisposable
{
    private readonly Diagnostics log;
    private readonly OcrEngine? ocr;
    private DateTimeOffset lastAction=DateTimeOffset.MinValue;
    private string lastState="";
    private int repeatedActions;
    private string previousAction="";
    private int? observedSelectedWorld;
    private IntPtr observedWindow;
    private readonly BackgroundWindowCapture backgroundCapture=new();
    internal string CaptureState=>backgroundCapture.State;
    internal readonly OfficialLiveRenderObserver RenderObserver;
    internal readonly GameObservationTracker Game=new();
    internal bool Paused {get;set;}
    internal bool ObserveOnly {get;set;}
    internal string State {get;private set;}="WAITING_FOR_WINDOW";
    internal VisualStartup(Diagnostics log,OfficialLiveRenderObserver? renderObserver=null)
    {
        this.log=log;
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
        if(observedWindow!=target){observedWindow=target;observedSelectedWorld=null;Game.Reset();}
        if(DateTimeOffset.UtcNow-lastAction<TimeSpan.FromSeconds(4))return;
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
                (int)bounds.X-origin.X,(int)bounds.Y-origin.Y,(int)bounds.Width,(int)bounds.Height)),owner,ct);
            return;
        }
        if(ocr is null){SetState("OCR_LANGUAGE_UNAVAILABLE");return;}
        var observed=await Capture(target,owner,ct);
        if(observed is null){SetState("CAPTURE_"+backgroundCapture.State);return;}
        Game.Observe(GameScreenReader.Read(observed),DateTimeOffset.UtcNow);
        if(ObserveOnly){SetState("OBSERVATION_ONLY_"+Game.LastObservation?.Stage);return;}
        if(Game.LastObservation?.Stage==VisibleGameStage.InGame)
        {
            if(Game.LastObservation.TotalLevel is null&&observed.ScreenBounds.Size==DockedWindowLayout.GameSize)
            {
                var button=new Rectangle(DockedWindowLayout.GameSize.Width-230,DockedWindowLayout.GameSize.Height-322,10,10);
                bool tooltip=observed.Labels.Any(l=>l.Text.Equals("Skills",StringComparison.OrdinalIgnoreCase)&&
                    l.Bounds.Left<200&&l.Bounds.Top<50);
                await Dispatch(observed,new(tooltip?"OPEN_SKILLS":"HOVER_SKILLS",button,!tooltip),owner,ct);
                return;
            }
            SetState(Game.Plan is null?"IN_GAME_READING_SKILLS":"IN_GAME_GOAL_PLANNED");return;
        }
        var decision=VisualStartupPolicy.Decide(observed.Labels,preferredWorld,
            preferredWorld is not null&&observedSelectedWorld==preferredWorld);
        if(decision.State=="PLAY_SELECTED_CHARACTER")observedSelectedWorld=preferredWorld;
        if(decision.Click is null){SetState(decision.State);return;}
        await Dispatch(observed,decision,owner,ct);
    }

    private static System.Windows.Rect? FindLauncherPlay(IntPtr target)
    {
        // UI Automation of a foreign process may block. Keep it off the UI thread.
        var root=AutomationElement.FromHandle(target);
        var images=root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Image));
        if(!images.Cast<AutomationElement>().Any(e=>!e.Current.IsOffscreen&&
            e.Current.Name.Contains("Old School RuneScape",StringComparison.OrdinalIgnoreCase)))return null;
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
        var labels=await Recognize(captured.Image,ocr!);
        ct.ThrowIfCancellationRequested();
        return new(target,bounds,captured.CapturedUtc,labels);
    }
    private static async Task<IReadOnlyList<VisualLabel>> Recognize(Bitmap bitmap,OcrEngine engine)
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
        labels.AddRange(WorldGridReader.Classify(labels));
        labels.AddRange(await SkillGridOcr.Read(bitmap,engine,labels));
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
            Console.WriteLine("OCR FIXTURE PASS: Windows OCR -> known-screen policy, no capture or input");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("OCR FIXTURE FAIL: "+ex);return 1;}
    }
    private async Task Dispatch(VisualFrame frame,VisualDecision decision,IntPtr owner,CancellationToken ct)
    {
        if(previousAction==decision.State&&repeatedActions>=3)
        {SetState("ACTION_RETRY_LIMIT_"+decision.State);return;}
        var box=decision.Click!.Value;
        if(box.Width<2||box.Height<2||!new Rectangle(Point.Empty,frame.ScreenBounds.Size).Contains(box))return;
        if(Paused||ObserveOnly||GetAncestor(frame.Window,2)!=owner||
            DateTimeOffset.UtcNow-frame.CapturedUtc>TimeSpan.FromSeconds(2))return;
        var currentOrigin=new POINT();
        if(!GetClientRect(frame.Window,out var currentRect)||!ClientToScreen(frame.Window,ref currentOrigin)||
           new Rectangle(currentOrigin.X,currentOrigin.Y,currentRect.Right,currentRect.Bottom)!=frame.ScreenBounds)return;
        var point=new Point(box.X+box.Width/2,box.Y+box.Height/2);
        bool queued=decision.MoveOnly?BackgroundWindowInput.Move(frame.Window,point):
            await BackgroundWindowInput.ClickAsync(frame.Window,point,()=>Paused||ObserveOnly,ct);
        if(!queued)
        {Paused=true;SetState("INPUT_DISPATCH_FAILED");return;}
        repeatedActions=previousAction==decision.State?repeatedActions+1:1;
        previousAction=decision.State;
        lastAction=DateTimeOffset.UtcNow;
        SetState(decision.State+"_REQUESTED");
        log.Add(Severity.INFO,"VISUAL_INPUT","ACTION_DISPATCHED",
            $"action={decision.State} input=WINDOW_MESSAGE background=true global_cursor_moved=false "+
            "success_requires_next_observation=true");
    }
    private void SetState(string state)
    {
        State=state;
        if(state==lastState)return;
        lastState=state;
        log.Add(Severity.INFO,"VISUAL_STARTUP","STATE",$"state={state}");
    }
    public void Dispose()=>backgroundCapture.Dispose();
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
        var welcome=new[]{Label("Welcome to Old School RuneScape",10),Label("Play Now",60),Label("World 417",250)};
        Require(VisualStartupPolicy.Decide(welcome,null).Click is null,"wait for measured world");
        Require(VisualStartupPolicy.Decide(welcome,417).State=="PLAY_SELECTED_CHARACTER","matching world before play");
        Require(VisualStartupPolicy.Decide(welcome,397).State=="OPEN_WORLD_SELECTION","wrong world is corrected before login");
        Require(VisualStartupPolicy.Decide(welcome.Append(Label("password",100)).ToArray(),417).Click is null,
            "credentials screens are manual");
        Require(VisualStartupPolicy.Decide(new[]{Label("Play Now",20)},417).Click is null,"isolated text is not a welcome screen");
        var grid=new[]{Label("Select a world",10),Label("World",40),Label("397",80)};
        Require(VisualStartupPolicy.Decide(grid,397).State=="SELECT_F2P_WORLD","select visible measured world");
        Require(VisualStartupPolicy.Decide(grid.Append(Label("397",120)).ToArray(),397).Click is null,
            "ambiguous player counts cannot become a world click");
        Require(VisualStartupPolicy.Decide(grid,417).Click is null,"offscreen world never gets guessed");
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome",10),Label("CLICK HERE TO PLAY",80)},417,true).State=="ENTER_GAME",
            "welcome transition");
        Require(VisualStartupPolicy.Decide(new[]{Label("Welcome",10),Label("CLICK HERE TO PLAY",80)},417).Click is null,
            "resumed welcome requires independently observed selected world");
        Require(VisualStartupPolicy.Decide(grid.Select(l=>l.Text=="397"?l with {Bounds=new Rectangle(500,80,50,25)}:l).ToArray(),397).Click is null,
            "population numbers outside world column are not targets");
        Console.WriteLine("VISUAL STARTUP PASS: measured-world ordering, known-screen guards, ambiguous/offscreen rejection, manual authentication");
    }
}
