namespace OSRSLiveDiagnostics;

internal static class SessionRecoveryTests
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        static void Require(bool b,string message){if(!b)throw new Exception("Reconnect: "+message);}
        var time=DateTimeOffset.UtcNow;
        VisualFrame Frame(double seconds,params VisualLabel[] labels)=>new(new(1),new(0,0,1000,600),time.AddSeconds(seconds),labels);
        var hud=new VisualLabel("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud);
        var retry=new VisualLabel("Try again",new(420,300,110,25));
        var lost=new VisualLabel("Connection lost.",new(300,100,200,20));
        var serverDisconnect=new VisualLabel("You were disconnected from the server.",new(385,230,260,15));
        var startupRetry=new SessionRecovery();
        var disconnectFrame=Frame(0,serverDisconnect,retry);
        Require(GameScreenReader.Read(disconnectFrame).Stage==VisibleGameStage.Disconnected&&
            GameScreenReader.Read(Frame(0,serverDisconnect with{Bounds=new(10,530,260,15)})).Stage==VisibleGameStage.Unknown,
            "exact live C++ disconnect message is an overlay, never a chat instruction");
        Require(SessionRecovery.IsDisconnectText("You were disconnected fromthe server .")&&
            !SessionRecovery.IsDisconnectText("You were disconnected frorn the server.")&&
            !SessionRecovery.IsDisconnectText("You were disconnected from the server. Click a link"),
            "word-boundary OCR variation preserves exact letters and rejects changed or appended text");
        Task<bool> Reachable(int? _,CancellationToken ct)=>Task.FromResult(true);
        var startupGrace=await startupRetry.Evaluate(disconnectFrame,GameScreenReader.Read(disconnectFrame).Stage,
            553,time,Reachable,default);
        var transientUnknown=await startupRetry.Evaluate(Frame(1,retry),VisibleGameStage.Unknown,
            553,time.AddSeconds(1),Reachable,default);
        Require(transientUnknown?.State=="RECONNECT_WAITING_FOR_KNOWN_SCREEN"&&transientUnknown.Click is null&&
            startupRetry.Recovering,"temporary unknown startup recovery screen retains recovery without clicking or claiming a returned game");
        var startupCandidate=await startupRetry.Evaluate(Frame(9,serverDisconnect,retry),VisibleGameStage.Disconnected,
            553,time.AddSeconds(9),Reachable,default);
        var startupConfirmed=await startupRetry.Evaluate(Frame(9.5,serverDisconnect,retry),VisibleGameStage.Disconnected,
            553,time.AddSeconds(9.5),Reachable,default);
        Require(startupGrace?.ResetSubject==true&&startupGrace.Click is null&&startupCandidate?.Click is null&&
            startupConfirmed?.Click==retry.Bounds,"starting on the actual disconnected screen still requires grace, connectivity and two fresh button frames");
        var pixelPlay=Frame(20,new VisualLabel("Play Now",new(400,230,200,60),VisualLabelKind.PlayButton));
        var pixelEnter=Frame(11,new VisualLabel("CLICK HERE TO PLAY",new(350,230,300,60),VisualLabelKind.EnterButton));
        Require(GameScreenReader.Read(pixelPlay).Stage==VisibleGameStage.Login&&
            GameScreenReader.Read(pixelEnter).Stage==VisibleGameStage.Welcome&&
            (await startupRetry.Evaluate(pixelPlay,GameScreenReader.Read(pixelPlay).Stage,
                553,time.AddSeconds(20),Reachable,default))?.ResumeLogin==true,
            "independently pixel-verified native entry buttons resume guarded startup when full-frame text is missing");
        var policy=new SessionRecovery();int probes=0;bool reachable=false;
        Task<bool> Probe(int? world,CancellationToken ct){ct.ThrowIfCancellationRequested();probes++;return Task.FromResult(reachable);}
        Require(await policy.Evaluate(Frame(0,hud),VisibleGameStage.InGame,553,time,Probe,default) is null,"running game is untouched");
        var first=await policy.Evaluate(Frame(1,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(1),Probe,default);
        Require(first?.ResetSubject==true&&first.Click is null&&probes==0,"clear skill lease and let native client recover first");
        var offline=await policy.Evaluate(Frame(9,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(9),Probe,default);
        Require(offline?.State=="RECONNECT_NETWORK_UNREACHABLE"&&probes==1,"offline probe never sends login click");
        var waiting=await policy.Evaluate(Frame(10,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(10),Probe,default);
        Require(waiting?.State=="RECONNECT_BACKOFF"&&probes==1,"no tight offline probe loop");
        reachable=true;
        var candidate=await policy.Evaluate(Frame(11,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(11),Probe,default);
        Require(candidate?.Click is null&&probes==2,"one visible button frame is insufficient");
        var reused=await policy.Evaluate(Frame(11,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(11.4),Probe,default);
        Require(reused?.Click is null,"same screenshot cannot confirm retry");
        var confirmed=await policy.Evaluate(Frame(11.7,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(11.7),Probe,default);
        Require(confirmed?.Click==retry.Bounds&&probes==2,"one retry on fresh independent frame and cached connectivity");
        var cooldown=await policy.Evaluate(Frame(12,lost,retry),VisibleGameStage.Disconnected,553,time.AddSeconds(12),Probe,default);
        Require(cooldown?.Click is null&&cooldown?.State=="RECONNECT_BACKOFF","avoid duplicate reconnect while handshake is pending");
        Require(await policy.Evaluate(Frame(13,hud),VisibleGameStage.InGame,553,time.AddSeconds(13),Probe,default) is null&&!policy.Recovering,"return to fresh in-game binding");
        var login=await policy.Evaluate(Frame(14,new VisualLabel("Play Now",new(400,250,120,25))),VisibleGameStage.Login,553,time.AddSeconds(14),Probe,default);
        Require(login?.ResumeLogin==true&&login.ResetSubject,"logout resumes normal guarded startup");
        int beforeAuth=probes;
        var auth=await policy.Evaluate(Frame(15,new VisualLabel("Password",new(400,200,100,25))),VisibleGameStage.Login,553,time.AddSeconds(15),Probe,default);
        Require(auth?.State=="RECONNECT_MANUAL_AUTHENTICATION_REQUIRED"&&auth.Click is null&&probes==beforeAuth,"no credential entry or auth retry");
        Require(GameScreenReader.Read(Frame(1,hud,lost)).Stage==VisibleGameStage.Disconnected,"disconnect overlay overrides still-visible HUD");
        Require(GameScreenReader.Read(Frame(1,hud,lost with{Bounds=new(10,530,200,20)})).Stage==VisibleGameStage.InGame,"chat cannot fake disconnect overlay");
        Require(StartupTiming.CanDispatch("PLAY_SELECTED_CHARACTER","ENTER_GAME",time,time.AddMilliseconds(400)),"next visible screen need not wait four seconds");
        Require(!StartupTiming.CanDispatch("PLAY_SELECTED_CHARACTER","PLAY_SELECTED_CHARACTER",time,time.AddSeconds(1)),"unchanged Play screen cannot be spammed");
        Require(SessionRecovery.Backoff(20)==TimeSpan.FromSeconds(30),"network retries capped at thirty seconds");
        var watch=new ConnectivityWatch();reachable=false;int startProbes=probes;
        Require(!await watch.Lost(553,true,time,()=>true,Probe,default),"one unreachable game-port probe is not an outage");
        Require(!await watch.Lost(553,true,time.AddSeconds(1),()=>true,Probe,default)&&probes==startProbes+1,"heartbeat has no busy retry loop");
        Require(await watch.Lost(553,true,time.AddSeconds(10),()=>true,Probe,default),"two failed probes stop gameplay even if disconnected text is unreadable");
        Require(GameScreenReader.Read(ConnectivityWatch.Signal(Frame(0,hud),true)).Stage==VisibleGameStage.Disconnected,"transport outage overrides frozen HUD");
        reachable=true;Require(!await watch.Lost(553,true,time.AddSeconds(20),()=>true,Probe,default),"connectivity return clears outage");
        Require(await watch.Lost(553,true,time.AddSeconds(21),()=>false,Probe,default),"disabled network interface stops immediately");
        watch.Reset();reachable=false;
        Require(!await watch.Lost(553,true,time.AddSeconds(22),()=>true,Probe,default),"new window does not inherit previous outage or failed-probe count");
        using var image=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(image))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.Red,760,285,5,10);g.FillRectangle(Brushes.Lime,769,274,5,21);g.FillRectangle(Brushes.Blue,779,281,5,14);
            foreach(int x in new[]{120,182,368}){g.FillRectangle(Brushes.White,x+10,578,20,3);g.FillRectangle(Brushes.Lime,x+20,590,10,5);}
            g.FillRectangle(Brushes.Maroon,430,575,87,24);g.FillRectangle(Brushes.White,460,583,20,3);
        }
        var marker=NativeGameHudReader.Read(image,new[]{new VisualLabel("MINIMAP",new(810,5,150,150),VisualLabelKind.Minimap)});
        Require(marker?.Kind==VisualLabelKind.GameHud&&NativeGameHudReader.Skills(image) is not null,"multi-region native HUD and chart icon");
        var withoutMap=NativeGameHudReader.Read(image,Array.Empty<VisualLabel>());
        Require(withoutMap?.Kind==VisualLabelKind.GameHud&&
            withoutMap.Text=="NATIVE_GAME_HUD_NO_MINIMAP",
            "fully verified footer plus Skills icon remains an independent HUD marker when map OCR is absent");
        using(var g=Graphics.FromImage(image))
            g.FillRectangle(Brushes.Black,430,575,87,24);
        Require(NativeGameHudReader.Read(image,Array.Empty<VisualLabel>()) is null,
            "button colors without the independently verified Report footer do not prove HUD");
        var observation=GameScreenReader.Read(Frame(0,marker!));
        var tracker=new GameObservationTracker();tracker.Observe(observation,time);tracker.Observe(observation with{ObservedUtc=time.AddSeconds(1)},time.AddSeconds(1));
        Require(observation.Stage==VisibleGameStage.InGame&&tracker.Confirmed is null,"image HUD never invents character identity or skills");
        Console.WriteLine("SESSION RECOVERY PASS: offline backoff, native recovery grace, guarded retry, logout restart, auth exclusion, overlay priority, fast transitions and native HUD gates");
    }
}
