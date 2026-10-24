using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OSRSLiveDiagnostics;

internal sealed record VisualLabel(string Text,Rectangle Bounds);
internal sealed record VisualFrame(IntPtr Window,Rectangle ScreenBounds,
    DateTimeOffset CapturedUtc,IReadOnlyList<VisualLabel> Labels);
internal sealed record VisualDecision(string State,Rectangle? Click=null);

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

internal sealed class VisualStartup
{
    private readonly Diagnostics log;
    private readonly OcrEngine? ocr;
    private DateTimeOffset lastAction=DateTimeOffset.MinValue;
    private string lastState="";
    private int repeatedActions;
    private string previousAction="";
    private int? observedSelectedWorld;
    private IntPtr observedWindow;
    internal bool Paused {get;set;}
    internal string State {get;private set;}="WAITING_FOR_WINDOW";
    internal VisualStartup(Diagnostics log)
    {
        this.log=log;
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
        if(target==IntPtr.Zero||!IsWindow(target)){SetState("WAITING_FOR_WINDOW");return;}
        if(observedWindow!=target){observedWindow=target;observedSelectedWorld=null;}
        if(!Foreground(owner)){SetState("WAITING_FOR_FOREGROUND");return;}
        if(DateTimeOffset.UtcNow-lastAction<TimeSpan.FromSeconds(4))return;
        var input=new LASTINPUTINFO{Size=(uint)Marshal.SizeOf<LASTINPUTINFO>()};
        if(GetLastInputInfo(ref input)&&unchecked((uint)Environment.TickCount-input.Tick)<1500)
        {SetState("WAITING_FOR_USER_INPUT_TO_FINISH");return;}
        if(kind==OfficialWindowKind.Launcher)
        {
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
        if(observed is null){SetState("CAPTURE_NOT_AVAILABLE");return;}
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
        if(!Foreground(owner)||!GetClientRect(target,out var rect))return null;
        if(rect.Right<160||rect.Bottom<120||rect.Right>OcrEngine.MaxImageDimension||
           rect.Bottom>OcrEngine.MaxImageDimension)return null;
        var point=new POINT();
        if(!ClientToScreen(target,ref point))return null;
        var bounds=new Rectangle(point.X,point.Y,rect.Right,rect.Bottom);
        // Avoid capturing overlays belonging to another app.
        foreach(var probe in new[]{new POINT{X=bounds.Left+5,Y=bounds.Top+5},
            new POINT{X=bounds.Right-5,Y=bounds.Bottom-5},
            new POINT{X=bounds.Left+bounds.Width/2,Y=bounds.Top+bounds.Height/2}})
            if(!BelongsTo(WindowFromPoint(probe),target))return null;
        var captured=DateTimeOffset.UtcNow;
        using var bitmap=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
        var labels=await Recognize(bitmap,ocr!);
        ct.ThrowIfCancellationRequested();
        return new(target,bounds,captured,labels);
    }
    private static async Task<IReadOnlyList<VisualLabel>> Recognize(Bitmap bitmap,OcrEngine engine)
    {
        using var bytes=new MemoryStream();bitmap.Save(bytes,ImageFormat.Png);
        using var stream=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(stream))
        {writer.WriteBytes(bytes.ToArray());await writer.StoreAsync();writer.DetachStream();}
        stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream);
        using var software=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
        var result=await engine.RecognizeAsync(software);
        var labels=result.Lines.Select(line=>new VisualLabel(line.Text,Union(line.Words))).ToList();
        // Standalone number tokens support the world grid; phrases remain full lines.
        labels.AddRange(result.Lines.SelectMany(l=>l.Words).Where(w=>Regex.IsMatch(w.Text,@"^\d{3}$")||
            w.Text.Equals("World",StringComparison.OrdinalIgnoreCase))
            .Select(w=>new VisualLabel(w.Text,ToRectangle(w.BoundingRect))));
        labels=labels.Distinct().ToList();
        return labels;
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
    private static Rectangle ToRectangle(Windows.Foundation.Rect r)=>
        new((int)r.X,(int)r.Y,(int)Math.Ceiling(r.Width),(int)Math.Ceiling(r.Height));
    private static Rectangle Union(IReadOnlyList<OcrWord> words)=>
        words.Select(w=>ToRectangle(w.BoundingRect)).Aggregate(Rectangle.Union);

    private async Task Dispatch(VisualFrame frame,VisualDecision decision,IntPtr owner,CancellationToken ct)
    {
        if(previousAction==decision.State&&repeatedActions>=3)
        {SetState("ACTION_RETRY_LIMIT_"+decision.State);return;}
        var box=decision.Click!.Value;
        if(box.Width<2||box.Height<2||!new Rectangle(Point.Empty,frame.ScreenBounds.Size).Contains(box))return;
        if(!Foreground(owner)||DateTimeOffset.UtcNow-frame.CapturedUtc>TimeSpan.FromSeconds(2))return;
        var destination=new POINT{X=frame.ScreenBounds.X+box.X+box.Width/2,
            Y=frame.ScreenBounds.Y+box.Y+box.Height/2};
        if(!BelongsTo(WindowFromPoint(destination),frame.Window))return;
        if(!GetCursorPos(out var start))return;
        var expected=start;
        for(int i=1;i<=6;i++)
        {
            ct.ThrowIfCancellationRequested();
            if(Paused||!Foreground(owner)||(GetAsyncKeyState(0x77)&0x8000)!=0){Paused=true;return;}
            if(!GetCursorPos(out var actual)||Math.Abs(actual.X-expected.X)>2||Math.Abs(actual.Y-expected.Y)>2)
            {Paused=true;SetState("PAUSED_USER_MOVED_MOUSE");return;}
            expected=new POINT{X=start.X+(destination.X-start.X)*i/6,Y=start.Y+(destination.Y-start.Y)*i/6};
            if(!SetCursorPos(expected.X,expected.Y))return;
            await Task.Delay(25,ct);
        }
        if(Paused||!Foreground(owner)||(GetAsyncKeyState(0x77)&0x8000)!=0||
           !BelongsTo(WindowFromPoint(destination),frame.Window))return;
        var currentOrigin=new POINT();
        if(!GetClientRect(frame.Window,out var currentRect)||!ClientToScreen(frame.Window,ref currentOrigin)||
           new Rectangle(currentOrigin.X,currentOrigin.Y,currentRect.Right,currentRect.Bottom)!=frame.ScreenBounds)return;
        var inputs=new[]{new INPUT{Type=0,Mouse=new MOUSEINPUT{Flags=0x0002}},
            new INPUT{Type=0,Mouse=new MOUSEINPUT{Flags=0x0004}}};
        if(SendInput(2,inputs,Marshal.SizeOf<INPUT>())!=2)
        {Paused=true;SetState("INPUT_DISPATCH_FAILED");return;}
        repeatedActions=previousAction==decision.State?repeatedActions+1:1;
        previousAction=decision.State;
        lastAction=DateTimeOffset.UtcNow;
        SetState(decision.State+"_REQUESTED");
        log.Add(Severity.INFO,"VISUAL_INPUT","ACTION_DISPATCHED",
            $"action={decision.State} input=MOUSE success_requires_next_observation=true");
    }
    private void SetState(string state)
    {
        State=state;
        if(state==lastState)return;
        lastState=state;
        log.Add(Severity.INFO,"VISUAL_STARTUP","STATE",$"state={state}");
    }
    private static bool Foreground(IntPtr owner)=>!IsIconic(owner)&&GetAncestor(GetForegroundWindow(),2)==owner;
    private static bool BelongsTo(IntPtr window,IntPtr target)=>window==target||IsChild(target,window);
    [StructLayout(LayoutKind.Sequential)]private struct POINT{internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct RECT{internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct LASTINPUTINFO{internal uint Size,Tick;}
    [StructLayout(LayoutKind.Sequential)]private struct MOUSEINPUT
    {internal int X,Y;internal uint Data,Flags,Time;internal UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct INPUT{internal uint Type;internal MOUSEINPUT Mouse;}
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")]private static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr h,ref POINT p);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,INPUT[] inputs,int size);
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
