using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OSRSLiveDiagnostics;

// Hosts the UNMODIFIED official C++ game window, once launched/authenticated
// through the Jagex Launcher. This is Windows UI composition, NOT OSRS protocol,
// credential forwarding, game-state instrumentation or game input automation.
internal enum OfficialWindowKind { Launcher, Game }
internal sealed record OfficialClientWindow(
    int ProcessId,IntPtr Handle,string Title,OfficialWindowKind Kind);

internal sealed class OfficialClientWindowHost : IDisposable
{
    private const int GWL_STYLE=-16;
    private const long WS_CHILD=0x40000000,WS_POPUP=unchecked((long)0x80000000),
        WS_CAPTION=0x00C00000,WS_THICKFRAME=0x00040000;
    private const uint SWP_NOZORDER=0x0004,SWP_NOACTIVATE=0x0010,SWP_FRAMECHANGED=0x0020;
    private readonly Control panel;
    private readonly Diagnostics diagnostics;
    private IntPtr attached,originalParent;
    private long originalStyle;
    private Rectangle originalBounds;
    internal OfficialWindowKind? CurrentKind {get;private set;}
    internal bool Attached => attached!=IntPtr.Zero&&IsWindow(attached);
    internal OfficialClientWindowHost(Control panel,Diagnostics diagnostics)
    {
        this.panel=panel;
        this.diagnostics=diagnostics;
        panel.Resize+=(_,_)=>Resize();
    }

    internal static bool SupportedProcessName(string? name)=>
        string.Equals(name,"osclient",StringComparison.OrdinalIgnoreCase);
    internal static bool SupportedLauncherProcessName(string? name)=>
        string.Equals(name,"JagexLauncher",StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name,"Jagex Launcher",StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<OfficialClientWindow> FindRunningClients()=>
        FindRunningWindows(OfficialWindowKind.Game);
    internal static IReadOnlyList<OfficialClientWindow> FindRunningLauncherWindows()=>
        FindRunningWindows(OfficialWindowKind.Launcher);
    private static IReadOnlyList<OfficialClientWindow> FindRunningWindows(
        OfficialWindowKind kind)
    {
        var matches=new List<OfficialClientWindow>();
        EnumWindows((hwnd,_) =>
        {
            if(!IsWindowVisible(hwnd) || GetWindow(hwnd,4)!=IntPtr.Zero)return true;
            GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid==0||pid==Environment.ProcessId)return true;
            try
            {
                using var p=Process.GetProcessById(checked((int)pid));
                if(kind==OfficialWindowKind.Game?
                    !SupportedProcessName(p.ProcessName):
                    !SupportedLauncherProcessName(p.ProcessName))return true;
                int len=GetWindowTextLength(hwnd);
                if(len<1)return true;
                var title=new StringBuilder(Math.Min(len+1,260));
                GetWindowText(hwnd,title,title.Capacity);
                matches.Add(new(p.Id,hwnd,title.ToString(),kind));
            }
            catch(ArgumentException){}
            catch(System.ComponentModel.Win32Exception){}
            catch(InvalidOperationException){}
            return true;
        },IntPtr.Zero);
        return matches;
    }

    internal static bool TryOpenJagexLauncher()
    {
        string? pf86=Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        string? pf=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots=new[]{pf86,pf,local is null?null:Path.Combine(local,"Programs")};
        foreach(var root in roots)
        {
            if(string.IsNullOrWhiteSpace(root))continue;
            foreach(string filename in new[]{"JagexLauncher.exe","Jagex Launcher.exe"})
            {
                string path=Path.Combine(root,"Jagex Launcher",filename);
                if(!File.Exists(path))continue;
                Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
                return true;
            }
        }
        return false;
    }

    internal bool Attach(OfficialClientWindow window)
    {
        if(Attached && attached==window.Handle && CurrentKind==window.Kind)return true;
        // Verify the target with the actual OS process, never by the window title
        // alone (which may be controlled by unrelated applications).
        bool valid=window.Kind==OfficialWindowKind.Game?
            FindRunningClients().Any(c=>c.Handle==window.Handle&&c.ProcessId==window.ProcessId):
            FindRunningLauncherWindows().Any(c=>c.Handle==window.Handle&&c.ProcessId==window.ProcessId);
        if(window.Handle==IntPtr.Zero||!IsWindow(window.Handle)||!valid)return false;
        Detach();
        var hwnd=window.Handle;
        originalParent=GetParent(hwnd);
        originalStyle=GetWindowLongPtr(hwnd,GWL_STYLE).ToInt64();
        GetWindowRect(hwnd,out var bounds);
        originalBounds=Rectangle.FromLTRB(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
        long embedded=(originalStyle&~(WS_POPUP|WS_CAPTION|WS_THICKFRAME))|WS_CHILD;
        try
        {
            SetWindowLongPtr(hwnd,GWL_STYLE,new IntPtr(embedded));
            SetParent(hwnd,panel.Handle);
            if(GetParent(hwnd)!=panel.Handle)
                throw new InvalidOperationException("Windows denied cross-process window hosting.");
            attached=hwnd;
            CurrentKind=window.Kind;
            Resize();
            ShowWindow(hwnd,5);
            diagnostics.SetMode(window.Kind==OfficialWindowKind.Game?
                "OFFIZIELLES OSRS (SPIELFENSTER EINGEBETTET)":
                "OFFIZIELLER JAGEX LAUNCHER (ANMELDUNG EINGEBETTET)");
            diagnostics.Add(Severity.INFO,"OFFICIAL_CLIENT","WINDOW_ATTACHED",
                $"pid={window.ProcessId} kind={window.Kind} login=JAGEX_LAUNCHER "+
                "authenticated_status=NOT_OBSERVABLE "+
                "game_state_adapter=false automated_input=false");
            return true;
        }
        catch
        {
            if(IsWindow(hwnd))
            {
                SetParent(hwnd,originalParent);
                SetWindowLongPtr(hwnd,GWL_STYLE,new IntPtr(originalStyle));
                RestoreBounds(hwnd);
            }
            attached=IntPtr.Zero;CurrentKind=null;
            diagnostics.Add(Severity.WARN,"OFFICIAL_CLIENT","WINDOW_ATTACH_FAILED",
                $"kind={window.Kind} reason=WIN32_PARENTING_REJECTED "+
                "external_app_remains_open=true");
            return false;
        }
    }
    internal void Resize()
    {
        if(!Attached)return;
        SetWindowPos(attached,IntPtr.Zero,0,0,
            Math.Max(1,panel.ClientSize.Width),Math.Max(1,panel.ClientSize.Height),
            SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
    }
    private void RestoreBounds(IntPtr hwnd)
    {
        SetWindowPos(hwnd,IntPtr.Zero,originalBounds.X,originalBounds.Y,
            Math.Max(320,originalBounds.Width),Math.Max(200,originalBounds.Height),
            SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
    }
    internal void Detach()
    {
        var hwnd=attached;
        if(hwnd==IntPtr.Zero)return;
        attached=IntPtr.Zero;
        var previousKind=CurrentKind;
        CurrentKind=null;
        if(IsWindow(hwnd))
        {
            SetParent(hwnd,originalParent);
            SetWindowLongPtr(hwnd,GWL_STYLE,new IntPtr(originalStyle));
            RestoreBounds(hwnd);
            ShowWindow(hwnd,5);
        }
        diagnostics.SetMode("OFFIZIELLES OSRS (FENSTER GETRENNT)");
        diagnostics.Add(Severity.INFO,"OFFICIAL_CLIENT","WINDOW_DETACHED",
            $"kind={previousKind} official_process_not_terminated=true "+
            "login_still_owned_by_jagex=true");
    }
    public void Dispose()=>Detach();

    private delegate bool EnumWindowProc(IntPtr hwnd,IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback,IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int capacity);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetParent(IntPtr hwnd,IntPtr parent);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd,int code);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct RECT {internal int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
}

internal static class OfficialClientWindowHostTests
{
    internal static void Run()
    {
        if(!OfficialClientWindowHost.SupportedProcessName("osclient") ||
           !OfficialClientWindowHost.SupportedProcessName("OSCLIENT") ||
           OfficialClientWindowHost.SupportedProcessName("RuneLite") ||
           OfficialClientWindowHost.SupportedProcessName("JagexLauncher") ||
           !OfficialClientWindowHost.SupportedLauncherProcessName("JagexLauncher") ||
           !OfficialClientWindowHost.SupportedLauncherProcessName("Jagex Launcher") ||
           OfficialClientWindowHost.SupportedLauncherProcessName("chrome") ||
           OfficialClientWindowHost.SupportedLauncherProcessName("msedge") ||
           OfficialClientWindowHost.SupportedLauncherProcessName("osclient"))
            throw new Exception("Only official launcher and game processes may be hosted.");
        Console.WriteLine("OFFICIAL WINDOW HOST PASS: trusted launcher/game process filters, no browser interception");
    }
}
