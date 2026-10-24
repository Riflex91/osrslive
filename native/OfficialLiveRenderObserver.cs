using System;
using System.Collections.Generic;
using System.Drawing;


using System.Windows.Forms;

namespace OSRSLiveDiagnostics;

// Read-only sampling of compositor frames from the official game. It never
// captures the Jagex Launcher, password/MFA windows, or background desktops.
// Pixels are never stored or exported. A rendered picture does NOT prove login.
internal enum LiveRenderState
{
    NoGameWindow, NotForeground, Minimized, TooSmall,
    CaptureUnavailable, UniformPixels, VisibleStatic, VisibleChanging
}
internal sealed record LiveRenderSample(LiveRenderState State,int LuminanceVariance,
    int ChangedCells,int SampleCells,DateTimeOffset ObservedUtc);

internal static class LiveRenderClassifier
{
    // Compare small in-memory color samples. No OCR, no game data and no
    // persistent image data is kept.
    internal static LiveRenderState Classify(IReadOnlyList<int> pixels,
        IReadOnlyList<int>? previous)
    {
        if(pixels.Count<24)return LiveRenderState.CaptureUnavailable;
        double sum=0,sumSquares=0;
        foreach(int rgb in pixels)
        {
            double luminance=(((rgb>>16)&255)*299+
                ((rgb>>8)&255)*587+(rgb&255)*114)/1000.0;
            sum+=luminance;sumSquares+=luminance*luminance;
        }
        double variance=Math.Max(0,sumSquares/pixels.Count-
            (sum/pixels.Count)*(sum/pixels.Count));
        if(variance<18)return LiveRenderState.UniformPixels;
        if(previous is null || previous.Count!=pixels.Count)
            return LiveRenderState.VisibleStatic;
        int changed=0;
        for(int i=0;i<pixels.Count;i++)
        {
            int before=previous[i],after=pixels[i];
            int distance=Math.Abs(((before>>16)&255)-((after>>16)&255))+
                Math.Abs(((before>>8)&255)-((after>>8)&255))+
                Math.Abs((before&255)-(after&255));
            if(distance>42)changed++;
        }
        return changed>=Math.Max(4,pixels.Count/30)?
            LiveRenderState.VisibleChanging:LiveRenderState.VisibleStatic;
    }
    internal static int CountChanged(IReadOnlyList<int> current,
        IReadOnlyList<int>? previous)
    {
        if(previous is null || previous.Count!=current.Count)return 0;
        int changed=0;
        for(int i=0;i<current.Count;i++)
        {
            int delta=Math.Abs(((current[i]>>16)&255)-((previous[i]>>16)&255))+
                Math.Abs(((current[i]>>8)&255)-((previous[i]>>8)&255))+
                Math.Abs((current[i]&255)-(previous[i]&255));
            if(delta>42)changed++;
        }
        return changed;
    }
}

internal sealed class OfficialLiveRenderObserver
{
    private int[]? lastPixels;
    internal LiveRenderSample Last {get;private set;}=
        new(LiveRenderState.NoGameWindow,0,0,0,DateTimeOffset.UtcNow);
    internal void MarkUnavailable(LiveRenderState state)=>Set(state,DateTimeOffset.UtcNow);
    internal void ObserveBitmap(Bitmap bitmap,DateTimeOffset capturedUtc)
    {
        var colors=new int[96];
        for(int row=0;row<8;row++)
        for(int col=0;col<12;col++)
            colors[row*12+col]=bitmap.GetPixel(bitmap.Width/4+(col*2+1)*bitmap.Width/48,
                bitmap.Height/4+(row*2+1)*bitmap.Height/32).ToArgb()&0xFFFFFF;
        int changed=LiveRenderClassifier.CountChanged(colors,lastPixels);
        var state=LiveRenderClassifier.Classify(colors,lastPixels);
        var values=colors.Select(rgb=>(((rgb>>16)&255)*299+((rgb>>8)&255)*587+(rgb&255)*114)/1000d).ToArray();
        double mean=values.Average();
        Last=new(state,(int)Math.Round(values.Average(v=>(v-mean)*(v-mean))),changed,96,capturedUtc);
        lastPixels=colors;
    }
    internal void Reset()
    {
        lastPixels=null;
        Last=new(LiveRenderState.NoGameWindow,0,0,0,DateTimeOffset.UtcNow);
    }
    private LiveRenderSample Set(LiveRenderState state,DateTimeOffset when)
    {
        if(state is LiveRenderState.NoGameWindow or LiveRenderState.NotForeground
            or LiveRenderState.Minimized or LiveRenderState.CaptureUnavailable)
            lastPixels=null;
        Last=new(state,0,0,0,when);
        return Last;
    }

}

// Auto-start is guarded by independent evidence from an APPROVED game adapter.
// A screenshot, Ping, launcher window or hi-score page never pass this gate.
internal sealed record LiveAgentPrerequisites(bool Authenticated,bool VerifiedPlayer,
    bool VerifiedWorld,bool WorldIsFreeToPlay,bool HasApprovedActionAdapter);

internal static class LiveAgentStartGate
{
    internal static string Evaluate(LiveAgentPrerequisites? value)
    {
        if(value is null)return "OFFICIAL_ADAPTER_MISSING";
        if(!value.Authenticated)return "AUTHENTICATION_NOT_VERIFIED";
        if(!value.VerifiedPlayer)return "PLAYER_SESSION_NOT_VERIFIED";
        if(!value.VerifiedWorld)return "OFFICIAL_WORLD_NOT_VERIFIED";
        if(!value.WorldIsFreeToPlay)return "WORLD_NOT_F2P";
        if(!value.HasApprovedActionAdapter)return "AUTHORIZED_ACTION_ADAPTER_MISSING";
        return "READY_FOR_EXPLICITLY_AUTHORIZED_AGENT";
    }
}

internal static class OfficialLiveObserverTests
{
    private static void Require(bool valid,string message)
    {if(!valid)throw new Exception("Official live observer: "+message);}
    internal static void Run()
    {
        int[] mostlyDark=Enumerable.Repeat(0,96).ToArray();
        Require(LiveRenderClassifier.Classify(mostlyDark,null)==
            LiveRenderState.UniformPixels,"blank frame is not gameplay");
        int[] first=Enumerable.Range(0,96)
            .Select(i=>(i%2==0?0xD0F8A2:0x173041)).ToArray();
        Require(LiveRenderClassifier.Classify(first,null)==
            LiveRenderState.VisibleStatic,"first vivid frame observed but auth unknown");
        Require(LiveRenderClassifier.Classify(first,first)==
            LiveRenderState.VisibleStatic,"unchanged game picture");
        int[] changed=first.Select(rgb=>rgb==0xD0F8A2?0x304090:0xD0F8A2).ToArray();
        Require(LiveRenderClassifier.Classify(changed,first)==
            LiveRenderState.VisibleChanging,"real pixel changes tracked");
        Require(LiveAgentStartGate.Evaluate(null)=="OFFICIAL_ADAPTER_MISSING",
            "pixel evidence cannot start bot");
        var partial=new LiveAgentPrerequisites(true,true,true,true,false);
        Require(LiveAgentStartGate.Evaluate(partial)=="AUTHORIZED_ACTION_ADAPTER_MISSING",
            "no game action access");
        Require(LiveAgentStartGate.Evaluate(partial with {WorldIsFreeToPlay=false})
            =="WORLD_NOT_F2P","free-world requirement");
        Require(LiveAgentStartGate.Evaluate(partial with {HasApprovedActionAdapter=true})
            =="READY_FOR_EXPLICITLY_AUTHORIZED_AGENT","all preconditions present");
        Console.WriteLine("OFFICIAL LIVE OBSERVER PASS: real frame evidence, privacy, authenticated-session and F2P gates");
    }
}
