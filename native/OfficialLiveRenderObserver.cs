using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OSRSLiveDiagnostics;

// Read-only, in-memory sampling of the VISIBLE official game HWND. It never
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
    internal LiveRenderSample Observe(IntPtr gameHwnd, IntPtr ownFormHwnd)
    {
        var timestamp=DateTimeOffset.UtcNow;
        if(gameHwnd==IntPtr.Zero || !IsWindow(gameHwnd))
            return Set(LiveRenderState.NoGameWindow,timestamp);
        if(IsIconic(ownFormHwnd))
            return Set(LiveRenderState.Minimized,timestamp);
        // Cross-process HWND is reparented into our WinForms form. Do not
        // read the pixels if another app or Windows login/MFA overlays it.
        IntPtr foreground=GetForegroundWindow();
        if(foreground==IntPtr.Zero || GetAncestor(foreground,2)!=ownFormHwnd)
            return Set(LiveRenderState.NotForeground,timestamp);
        if(!GetClientRect(gameHwnd,out var bounds) ||
           bounds.Right-bounds.Left<160 || bounds.Bottom-bounds.Top<120)
            return Set(LiveRenderState.TooSmall,timestamp);
        var origin=new POINT{X=0,Y=0};
        if(!ClientToScreen(gameHwnd,ref origin))
            return Set(LiveRenderState.CaptureUnavailable,timestamp);
        const int width=112,height=72;
        int x=origin.X+(bounds.Right-bounds.Left-width)/2;
        int y=origin.Y+(bounds.Bottom-bounds.Top-height)/2;
        try
        {
            using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
            using(var graphics=Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x,y,0,0,new Size(width,height),
                    CopyPixelOperation.SourceCopy);
            var colors=new int[12*8];
            int n=0;
            for(int row=0;row<8;row++)
            for(int col=0;col<12;col++)
                colors[n++]=bitmap.GetPixel(col*9+5,row*9+4).ToArgb()&0xFFFFFF;
            int changed=LiveRenderClassifier.CountChanged(colors,lastPixels);
            var state=LiveRenderClassifier.Classify(colors,lastPixels);
            int[] luminosity=colors.Select(rgb=>(
                (((rgb>>16)&255)*299+((rgb>>8)&255)*587+
                    (rgb&255)*114)/1000)).ToArray();
            double mean=luminosity.Average();
            int variance=(int)Math.Round(luminosity.Average(v=>(v-mean)*(v-mean)));
            lastPixels=colors;
            Last=new(state,variance,changed,colors.Length,timestamp);
            return Last;
        }
        catch(ExternalException){return Set(LiveRenderState.CaptureUnavailable,timestamp);}
        catch(ArgumentException){return Set(LiveRenderState.CaptureUnavailable,timestamp);}
        catch(System.ComponentModel.Win32Exception)
        {return Set(LiveRenderState.CaptureUnavailable,timestamp);}
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
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT{internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT{internal int X,Y;}
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr hwnd,out RECT rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr hwnd,ref POINT point);
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
