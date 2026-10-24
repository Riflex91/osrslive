using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal sealed record RecoveryDecision(string State,bool ResetSubject=false,bool ResumeLogin=false,Rectangle? Click=null);
internal sealed class SessionRecovery
{
    private bool established,recovering;
    private DateTimeOffset began,nextProbe,reachableUntil;
    private int failures;
    private Rectangle? candidate;private DateTimeOffset candidateTime;
    internal bool Recovering=>recovering;
    internal bool HasEstablished=>established;
    internal void Reset(){established=false;recovering=false;began=nextProbe=reachableUntil=default;failures=0;candidate=null;}
    internal static bool Disconnected(IReadOnlyList<VisualLabel> labels)=>labels.Any(l=>l.Kind==VisualLabelKind.ConnectionSignal)||labels.Any(l=>l.Kind==VisualLabelKind.Text&&
        l.Bounds.Top<450&&l.Bounds.Bottom<500&&Regex.IsMatch(l.Text.Trim(),
            @"^(?:Connection lost[.!]?|Attempting to reestablish(?: the connection)?[.!]*|You have been disconnected[.!]?|Error connecting to server[.!]?|No reply from loginserver[.!]?|Please try using a different world[.!]?)$",RegexOptions.IgnoreCase));
    internal static TimeSpan Backoff(int failure)=>TimeSpan.FromSeconds(Math.Min(30,2*Math.Pow(2,Math.Min(4,failure))));
    internal async Task<RecoveryDecision?> Evaluate(VisualFrame frame,VisibleGameStage stage,int? world,DateTimeOffset now,
        Func<int?,CancellationToken,Task<bool>> probe,CancellationToken ct)
    {
        if(stage==VisibleGameStage.InGame)
        {established=true;recovering=false;failures=0;candidate=null;return null;}
        if(!established&&stage!=VisibleGameStage.Disconnected)return null;
        if(stage is not (VisibleGameStage.Login or VisibleGameStage.Welcome or VisibleGameStage.Connecting or VisibleGameStage.Disconnected)&&!recovering)return null;
        bool reset=!recovering;
        if(reset){recovering=true;began=now;nextProbe=now;reachableUntil=default;candidate=null;}
        if(frame.Labels.Any(l=>l.Bounds.Top<450&&Regex.IsMatch(l.Text.Trim(),@"\b(?:password|authenticator|verification code)\b",RegexOptions.IgnoreCase)))
            return new("RECONNECT_MANUAL_AUTHENTICATION_REQUIRED",reset);
        if((stage is VisibleGameStage.Disconnected or VisibleGameStage.Connecting)&&now-began<TimeSpan.FromSeconds(8))
            return new("RECONNECT_WAITING_FOR_CLIENT_RECOVERY",reset);
        if(now<nextProbe)return new("RECONNECT_BACKOFF",reset);
        if(now>=reachableUntil)
        {
            if(!await probe(world,ct))
            {nextProbe=now+Backoff(failures++);return new("RECONNECT_NETWORK_UNREACHABLE",reset);}
            reachableUntil=now.AddSeconds(5);failures=0;
        }
        if(stage is VisibleGameStage.Login or VisibleGameStage.Welcome)
            return new("RECONNECT_NETWORK_READY_RESUME_LOGIN",reset,true);
        var buttons=frame.Labels.Where(l=>l.Kind==VisualLabelKind.Text&&l.Bounds.Left>=150&&l.Bounds.Right<=850&&l.Bounds.Top>=80&&l.Bounds.Bottom<=480&&
            Regex.IsMatch(l.Text.Trim(),@"^(?:Try again|Reconnect|Click here to retry)$",RegexOptions.IgnoreCase)).ToArray();
        if(buttons.Length==1)
        {
            var b=buttons[0].Bounds;
            if(candidate==b&&frame.CapturedUtc-candidateTime>=TimeSpan.FromMilliseconds(250))
            {candidate=null;nextProbe=now.AddSeconds(10);return new("RECONNECT_VISIBLE_BUTTON",reset,Click:b);}
            if(candidate!=b){candidate=b;candidateTime=frame.CapturedUtc;}
            return new("RECONNECT_CONFIRMING_VISIBLE_BUTTON",reset);
        }
        candidate=null;nextProbe=now.AddSeconds(2);return new("RECONNECT_WAITING_FOR_KNOWN_SCREEN",reset);
    }
    internal static async Task<bool> Probe(int? world,CancellationToken ct)
    {
        if(world is null or <301 or >800||!NetworkInterface.GetIsNetworkAvailable())return false;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(1500);
        using var client=new TcpClient();
        try{await client.ConnectAsync($"oldschool{world.Value-300}.runescape.com",OfficialF2PWorldScanner.GamePort,deadline.Token);return true;}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return false;}
        catch(SocketException){return false;}
    }
}

internal sealed class ConnectivityWatch
{
    private DateTimeOffset nextCheck;private int failures;private bool lost;
    internal void Reset(){nextCheck=default;failures=0;lost=false;}
    internal async Task<bool> Lost(int? world,bool established,DateTimeOffset now,Func<bool> localNetwork,
        Func<int?,CancellationToken,Task<bool>> probe,CancellationToken ct)
    {
        if(!localNetwork()){lost=true;nextCheck=now;return true;}
        if(!established)return false;
        if(now<nextCheck)return lost;
        nextCheck=now.AddSeconds(10);
        if(await probe(world,ct)){failures=0;lost=false;}
        else{failures++;if(failures>=2)lost=true;}
        return lost;
    }
    internal Task<bool> Lost(int? world,bool established,CancellationToken ct)=>
        Lost(world,established,DateTimeOffset.UtcNow,NetworkInterface.GetIsNetworkAvailable,SessionRecovery.Probe,ct);
    internal static VisualFrame Signal(VisualFrame frame,bool lost)=>lost?frame with{Labels=frame.Labels.Append(
        new VisualLabel("NETWORK_UNREACHABLE",new(0,0,2,2),VisualLabelKind.ConnectionSignal)).ToArray()}:frame;
}

internal static class StartupTiming
{
    internal static TimeSpan SameActionDelay(string state)=>state switch
    {"LAUNCH_SELECTED_CHARACTER" or "PLAY_SELECTED_CHARACTER"=>TimeSpan.FromSeconds(8),"ENTER_GAME"=>TimeSpan.FromSeconds(4),_=>TimeSpan.FromMilliseconds(700)};
    internal static bool CanDispatch(string previous,string next,DateTimeOffset last,DateTimeOffset now)=>
        previous!=next||now-last>=SameActionDelay(next);
}
