using System.Runtime.InteropServices;

namespace OSRSLiveDiagnostics;

// Optional, foreground-only physical-input shield. It is never armed at app
// startup and never uses BlockInput (which could lock the entire desktop).
// This best-effort Windows hook suppresses only physical game-targeted mouse
// buttons/wheel and keyboard input. Synthetic SendInput events are passed
// through, and Esc/Alt-Tab/Windows keys remain escape hatches. The OS can
// remove a slow low-level hook: do NOT treat this as a security guarantee.
internal sealed class RuneLiteInputShield:IDisposable
{
    private const int WhKeyboardLl=13,WhMouseLl=14;
    private const int WmMouseMove=0x200;
    private const uint LlMouseInjected=0x01,LlKeyboardInjected=0x10;
    private readonly HookProc mouseCallback,keyboardCallback;
    private readonly Action emergencyStop;
    private IntPtr mouseHook,keyboardHook,target;
    private bool queuedEmergencyStop;
    private long blockedMouseEvents,blockedKeyboardEvents;
    internal bool Active=>mouseHook!=IntPtr.Zero&&keyboardHook!=IntPtr.Zero;
    internal (long Mouse,long Keyboard) BlockedCounts=>(
        Interlocked.Read(ref blockedMouseEvents),Interlocked.Read(ref blockedKeyboardEvents));

    internal RuneLiteInputShield(Action emergencyStop)
    {
        this.emergencyStop=emergencyStop;
        mouseCallback=MouseHook;keyboardCallback=KeyboardHook;
    }

    internal (bool Enabled,string Reason) Enable(IntPtr runeLiteWindow)
    {
        Disable();
        Interlocked.Exchange(ref blockedMouseEvents,0);
        Interlocked.Exchange(ref blockedKeyboardEvents,0);
        if(runeLiteWindow==IntPtr.Zero||
           !BackgroundWindowInput.IsRuneLiteForeground(runeLiteWindow))
            return (false,"RUNELITE_NOT_FOREGROUND");
        IntPtr module=GetModuleHandle(null);
        if(module==IntPtr.Zero)return (false,"APPLICATION_MODULE_UNAVAILABLE");
        target=runeLiteWindow;
        mouseHook=SetWindowsHookEx(WhMouseLl,mouseCallback,module,0);
        if(mouseHook==IntPtr.Zero)
        {
            target=IntPtr.Zero;
            return (false,"MOUSE_HOOK_INSTALL_FAILED_"+Marshal.GetLastWin32Error());
        }
        keyboardHook=SetWindowsHookEx(WhKeyboardLl,keyboardCallback,module,0);
        if(keyboardHook==IntPtr.Zero)
        {
            string reason="KEYBOARD_HOOK_INSTALL_FAILED_"+Marshal.GetLastWin32Error();
            Disable();return (false,reason);
        }
        return (true,"PHYSICAL_FOREGROUND_GAME_INPUT_FILTER_ACTIVE_BEST_EFFORT");
    }

    internal void Disable()
    {
        // Release hooks BEFORE clearing target. If unhook fails, callbacks
        // still see a zero target and must pass all inputs through.
        target=IntPtr.Zero;queuedEmergencyStop=false;
        if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
        if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
    }

    private IntPtr MouseHook(int nCode,IntPtr wParam,IntPtr lParam)
    {
        try
        {
            if(nCode>=0&&target!=IntPtr.Zero&&
               BackgroundWindowInput.IsRuneLiteForeground(target))
            {
                var data=Marshal.PtrToStructure<MouseData>(lParam);
                IntPtr hit=WindowFromPoint(data.Point);
                bool inside=hit!=IntPtr.Zero&&
                    GetAncestor(hit,2)==GetAncestor(target,2);
                if(ShouldBlockMouse(wParam.ToInt64(),data.Flags,inside))
                {
                    Interlocked.Increment(ref blockedMouseEvents);
                    return new IntPtr(1);
                }
            }
        }
        catch{ /* Never let an input hook exception block system input. */ }
        return CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
    }

    private IntPtr KeyboardHook(int nCode,IntPtr wParam,IntPtr lParam)
    {
        try
        {
            if(nCode>=0&&target!=IntPtr.Zero&&
               BackgroundWindowInput.IsRuneLiteForeground(target))
            {
                var data=Marshal.PtrToStructure<KeyboardData>(lParam);
                // Esc immediately ends the voluntary foreground shield; do
                // not suppress Esc or the normal Windows focus shortcuts.
                if(data.VirtualKey==0x1B&&(data.Flags&LlKeyboardInjected)==0&&
                   !queuedEmergencyStop&&(wParam.ToInt64() is 0x100 or 0x104))
                {
                    queuedEmergencyStop=true;
                    emergencyStop();
                }
                if(ShouldBlockKeyboard(data.VirtualKey,data.Flags))
                {
                    Interlocked.Increment(ref blockedKeyboardEvents);
                    return new IntPtr(1);
                }
            }
        }
        catch{ /* Fail open on any unexpected callback problem. */ }
        return CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
    }

    internal static bool ShouldBlockMouse(long message,uint flags,bool insideGame)=>
        insideGame&&(flags&LlMouseInjected)==0&&message!=WmMouseMove;

    internal static bool ShouldBlockKeyboard(uint key,uint flags)
    {
        if((flags&LlKeyboardInjected)!=0)return false;
        // Explicit safety escape / Windows task switching. Never trap the
        // operator in an unresponsive game or block secure attention keys.
        if(key is 0x1B or 0x09 or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B)
            return false;
        return true;
    }

    internal static void SelfTest()
    {
        static void Require(bool value,string reason)
        {if(!value)throw new Exception("RuneLite physical input shield: "+reason);}
        Require(ShouldBlockMouse(0x201,0,true)&&ShouldBlockMouse(0x20A,0,true)&&
                !ShouldBlockMouse(0x200,0,true)&&!ShouldBlockMouse(0x201,1,true)&&
                !ShouldBlockMouse(0x201,0,false),
            "only physical game-targeted clicks and wheel events are suppressed");
        Require(ShouldBlockKeyboard(0x57,0)&&ShouldBlockKeyboard(0x0D,0)&&
                !ShouldBlockKeyboard(0x57,0x10)&&!ShouldBlockKeyboard(0x1B,0)&&
                !ShouldBlockKeyboard(0x09,0)&&!ShouldBlockKeyboard(0x12,0)&&
                !ShouldBlockKeyboard(0x5B,0)&&!ShouldBlockKeyboard(0x7B,0),
            "synthetic clicks and emergency Esc, Alt-Tab and system keys are never swallowed");
    }

    public void Dispose()=>Disable();

    private delegate IntPtr HookProc(int nCode,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint
    {internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseData
    {internal NativePoint Point;internal uint MouseDataValue,Flags,Time;internal IntPtr ExtraInfo;}
    [StructLayout(LayoutKind.Sequential)]private struct KeyboardData
    {internal uint VirtualKey,ScanCode,Flags,Time;internal IntPtr ExtraInfo;}

    [DllImport("user32.dll",SetLastError=true)]
    private static extern IntPtr SetWindowsHookEx(int hookType,HookProc proc,
        IntPtr module,uint threadId);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint screenPoint);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
