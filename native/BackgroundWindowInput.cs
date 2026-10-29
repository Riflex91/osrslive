using System.Runtime.InteropServices;

namespace OSRSLiveDiagnostics;

internal static class BackgroundWindowInput
{
    internal sealed record ForegroundClickResult(bool Inserted,string Reason,int InputEvents);
    // Explicit human mouse activity is a safe interruption, not a failed game move.
    // Do not silently resume clicking after the button is released.
    internal static bool IsManualMouseInterruption(ForegroundClickResult result)=>
        !result.Inserted&&(result.Reason=="PHYSICAL_MOUSE_BUTTON_HELD"||
            result.Reason=="PHYSICAL_MOUSE_BUTTON_HELD_BEFORE_CLICK");
    internal sealed record MinimapZoomResult(bool Inserted,string Reason,int InputEvents);

    // Right-clicking a RuneLite minimap with the Minimap plugin's Zoom option
    // enabled resets its scale to the default 4 pixels/tile (RuneLite 1.9.0).
    // If zoom is disabled, the minimap is already at its vanilla scale.
    // Aim within the map, but away from the player-dot's exact click hotspot.
    internal static Point MinimapResetPoint(Point playerCenter)=>
        new(playerCenter.X-20,playerCenter.Y-16);

    internal static async Task<MinimapZoomResult> ResetRuneLiteMinimapZoomAsync(
        IntPtr window,Size expectedSize,Point playerCenter,
        Func<bool> shouldStop,CancellationToken ct)
    {
        if(playerCenter.X<50||playerCenter.Y<50||playerCenter.X>=expectedSize.Width-40||
           playerCenter.Y>=expectedSize.Height-40)
            return new(false,"MINIMAP_ANCHOR_INVALID",0);
        if(shouldStop()||!TryClientSize(window,out var size)||size!=expectedSize)
            return new(false,"MINIMAP_RESET_WINDOW_OR_GEOMETRY_CHANGED",0);
        GetWindowThreadProcessId(window,out uint expectedPid);
        if(expectedPid==0)return new(false,"MINIMAP_RESET_WINDOW_PID_UNAVAILABLE",0);
        if(!SameForegroundRoot(window))
        {
            if(!SetForegroundWindow(window))return new(false,"MINIMAP_RESET_FOREGROUND_ACTIVATION_DENIED",0);
            await Task.Delay(120,ct);
        }
        if(shouldStop()||!SameForegroundRoot(window))
            return new(false,"MINIMAP_RESET_RUNELITE_NOT_FOREGROUND",0);
        Point resetPoint=MinimapResetPoint(playerCenter);
        var screen=new POINT{X=resetPoint.X,Y=resetPoint.Y};
        if(!ClientToScreen(window,ref screen)||
            !HitBelongsToExpectedWindow(window,expectedPid,screen))
            return new(false,"MINIMAP_RESET_POINT_COVERED",0);
        if((GetAsyncKeyState(0x01)&0x8000)!=0||
           (GetAsyncKeyState(0x02)&0x8000)!=0)
            return new(false,"MINIMAP_RESET_PHYSICAL_BUTTON_HELD",0);
        if(!TryNormalizeAbsolute(screen.X,screen.Y,GetSystemMetrics(76),GetSystemMetrics(77),
            GetSystemMetrics(78),GetSystemMetrics(79),out int px,out int py))
            return new(false,"MINIMAP_RESET_SCREEN_MAPPING_INVALID",0);
        if(SendInput(1,[Mouse(px,py,0x0001|0x8000|0x4000)],Marshal.SizeOf<INPUT>())!=1)
            return new(false,"MINIMAP_RESET_CURSOR_MOVE_FAILED",0);
        if(!GetCursorPos(out var actual)||Math.Abs(actual.X-screen.X)>1||
           Math.Abs(actual.Y-screen.Y)>1)
            return new(false,"MINIMAP_RESET_CURSOR_UNVERIFIED",1);
        if(shouldStop()||!SameForegroundRoot(window)||
           !TryClientSize(window,out size)||size!=expectedSize||
           !HitBelongsToExpectedWindow(window,expectedPid,screen))
            return new(false,"MINIMAP_RESET_TARGET_OR_FOCUS_CHANGED",1);
        ct.ThrowIfCancellationRequested();
        // One actual right-click, never a 3D camera wheel event.
        uint clicked=SendInput(2,[Mouse(0,0,0x0008),Mouse(0,0,0x0010)],
            Marshal.SizeOf<INPUT>());
        if(clicked!=2)
        {
            SendInput(1,[Mouse(0,0,0x0010)],Marshal.SizeOf<INPUT>());
            return new(false,"MINIMAP_RESET_RIGHT_CLICK_INCOMPLETE",1+(int)clicked);
        }
        // A right-click may also open an OSRS canvas menu. Dismiss it before
        // any future waypoint click so no click can select a menu entry.
        await Task.Delay(100,ct);
        if(shouldStop()||!SameForegroundRoot(window)||
           !TryClientSize(window,out size)||size!=expectedSize)
            return new(false,"MINIMAP_RESET_FOCUS_LOST_BEFORE_MENU_DISMISS",3);
        uint dismissed=SendInput(2,[Key(0x1B,0),Key(0x1B,0x0002)],
            Marshal.SizeOf<INPUT>());
        if(dismissed!=2)
        {
            SendInput(1,[Key(0x1B,0x0002)],Marshal.SizeOf<INPUT>());
            return new(false,"MINIMAP_RESET_ESCAPE_INCOMPLETE",3+(int)dismissed);
        }
        return new(true,"RIGHT_CLICK_RESET_AND_ESCAPE_SENT_ACTUAL_ZOOM_NOT_API_EXPORTED",5);
    }

    // RuneLite is a Java/AWT application. PostMessage success only shows that
    // Win32 queued messages, not that the AWT canvas accepted a game click.
    // For this explicitly armed RuneLite-only test use real Windows input,
    // never while another application owns the foreground or covers the tile.
    internal static async Task<ForegroundClickResult> ClickRuneLiteForegroundAsync(
        IntPtr window,Point position,Size expectedSize,bool activateAfterCalibration,
        Func<bool> shouldStop,CancellationToken ct)
    {
        if(shouldStop()||!TryClientSize(window,out var size)||size!=expectedSize||
            !GetClientRect(window,out var bounds)||position.X<0||position.Y<0||
            position.X>=bounds.Right||position.Y>=bounds.Bottom)
            return new(false,"WINDOW_OR_GEOMETRY_CHANGED",0);
        GetWindowThreadProcessId(window,out uint expectedPid);
        if(expectedPid==0)return new(false,"WINDOW_PID_UNAVAILABLE",0);
        if(activateAfterCalibration&&!SameForegroundRoot(window))
        {
            if(!SetForegroundWindow(window))return new(false,"FOREGROUND_ACTIVATION_DENIED",0);
            await Task.Delay(120,ct);
        }
        if(shouldStop()||!SameForegroundRoot(window))
            return new(false,"RUNELITE_NOT_FOREGROUND",0);
        var screen=new POINT{X=position.X,Y=position.Y};
        if(!ClientToScreen(window,ref screen))return new(false,"SCREEN_MAPPING_FAILED",0);
        var original=screen;
        if(!HitBelongsToExpectedWindow(window,expectedPid,screen))
            return new(false,"MINIMAP_OCCLUDED_BY_OTHER_WINDOW",0);
        if((GetAsyncKeyState(0x01)&0x8000)!=0||
           (GetAsyncKeyState(0x02)&0x8000)!=0)
            return new(false,"PHYSICAL_MOUSE_BUTTON_HELD",0);
        int virtualX=GetSystemMetrics(76),virtualY=GetSystemMetrics(77);
        int virtualWidth=GetSystemMetrics(78),virtualHeight=GetSystemMetrics(79);
        if(!TryNormalizeAbsolute(screen.X,screen.Y,virtualX,virtualY,virtualWidth,
                virtualHeight,out int absoluteX,out int absoluteY))
            return new(false,"VIRTUAL_SCREEN_BOUNDS_INVALID",0);
        var move=new INPUT[]{Mouse(absoluteX,absoluteY,0x0001|0x8000|0x4000)};
        if(shouldStop()||!SameForegroundRoot(window))
            return new(false,"FOREGROUND_LOST_BEFORE_MOVE",0);
        uint moved=SendInput(1,move,Marshal.SizeOf<INPUT>());
        if(moved!=1)return new(false,"SENDINPUT_MOVE_REJECTED",(int)moved);
        if(!GetCursorPos(out var actual)||Math.Abs(actual.X-original.X)>1||
           Math.Abs(actual.Y-original.Y)>1)
            return new(false,"CURSOR_POSITION_NOT_CONFIRMED",(int)moved);
        if(shouldStop()||!SameForegroundRoot(window)||
            !TryClientSize(window,out size)||size!=expectedSize||
            !HitBelongsToExpectedWindow(window,expectedPid,screen))
            return new(false,"FOREGROUND_OR_TARGET_LOST_BEFORE_CLICK",(int)moved);
        ct.ThrowIfCancellationRequested();
        // The user may press a physical button after the earlier preflight
        // while the cursor moves. Never combine that press with our click.
        if((GetAsyncKeyState(0x01)&0x8000)!=0||
           (GetAsyncKeyState(0x02)&0x8000)!=0)
            return new(false,"PHYSICAL_MOUSE_BUTTON_HELD_BEFORE_CLICK",(int)moved);
        // A single SendInput batch guarantees ordered DOWN and UP without
        // the user seeing a held synthetic button if an await gets cancelled.
        var click=new INPUT[]{Mouse(0,0,0x0002),Mouse(0,0,0x0004)};
        uint inserted=SendInput(2,click,Marshal.SizeOf<INPUT>());
        if(inserted!=2)
        {
            // Release defensively even if the partial batch inserted DOWN.
            SendInput(1,[Mouse(0,0,0x0004)],Marshal.SizeOf<INPUT>());
            return new(false,"SENDINPUT_BUTTON_SEQUENCE_INCOMPLETE",(int)(moved+inserted));
        }
        return new(true,"INPUT_STREAM_INSERTED_AWAIT_PLUGIN_MOVEMENT",(int)(moved+inserted));
    }

    internal static bool TryNormalizeAbsolute(int x,int y,int left,int top,int width,int height,
        out int normalizedX,out int normalizedY)
    {
        normalizedX=normalizedY=0;
        if(width<2||height<2||x<left||y<top||
            (long)x>=((long)left+width)||(long)y>=((long)top+height))return false;
        normalizedX=(int)((long)(x-left)*65535/(width-1));
        normalizedY=(int)((long)(y-top)*65535/(height-1));
        return true;
    }

    internal static int MouseInputStructSize()=>Marshal.SizeOf<INPUT>();
    internal static bool IsRuneLiteForeground(IntPtr window)=>
        window!=IntPtr.Zero&&IsWindow(window)&&SameForegroundRoot(window);
    private static INPUT Mouse(int dx,int dy,uint flags,uint data=0)=>new(){Type=0,Data=new(){
        Mouse=new(){X=dx,Y=dy,Flags=flags,Data=data}}};
    private static INPUT Key(ushort virtualKey,uint flags)=>new(){Type=1,Data=new(){
        Keyboard=new(){VirtualKey=virtualKey,Flags=flags}}};
    private static bool SameForegroundRoot(IntPtr window)
    {
        IntPtr foreground=GetForegroundWindow();
        return foreground!=IntPtr.Zero&&GetAncestor(foreground,2)==GetAncestor(window,2);
    }
    private static bool HitBelongsToExpectedWindow(IntPtr window,uint expectedPid,POINT screen)
    {
        IntPtr hit=WindowFromPoint(screen);
        if(hit==IntPtr.Zero||GetAncestor(hit,2)!=GetAncestor(window,2))return false;
        GetWindowThreadProcessId(hit,out uint hitPid);
        return hitPid==expectedPid;
    }
    [StructLayout(LayoutKind.Sequential)]private struct MOUSEINPUT
    {internal int X,Y;internal uint Data,Flags,Time;internal IntPtr ExtraInfo;}
    [StructLayout(LayoutKind.Sequential)]private struct KEYBDINPUT
    {internal ushort VirtualKey,Scan;internal uint Flags,Time;internal IntPtr ExtraInfo;}
    [StructLayout(LayoutKind.Explicit)]private struct INPUTDATA
    {
        [FieldOffset(0)]internal MOUSEINPUT Mouse;
        [FieldOffset(0)]internal KEYBDINPUT Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]private struct INPUT
    {internal uint Type;internal INPUTDATA Data;}

    internal static bool TryClientSize(IntPtr window,out Size size)
    {
        size=Size.Empty;
        if(!IsWindow(window)||IsIconic(GetAncestor(window,2))||
            !GetClientRect(window,out var bounds))return false;
        size=new(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
        return size.Width>0&&size.Height>0;
    }
    internal static bool Move(IntPtr window,Point point)
    {
        if(!IsWindow(window)||!GetClientRect(window,out var bounds)||point.X<0||point.Y<0||
            point.X>=bounds.Right||point.Y>=bounds.Bottom)return false;
        var mapped=new POINT{X=point.X,Y=point.Y};
        IntPtr destination=Resolve(window,ref mapped);
        GetWindowThreadProcessId(window,out uint expectedPid);
        GetWindowThreadProcessId(destination,out uint actualPid);
        return expectedPid!=0&&expectedPid==actualPid&&PostMessage(destination,0x200,IntPtr.Zero,
            new IntPtr((mapped.Y&0xFFFF)<<16|(mapped.X&0xFFFF)));
    }
    // WM_MOUSEWHEEL expects screen coordinates in lParam, unlike WM_MOUSEMOVE.
    // Most directed input goes to the render child, but OSRS may handle wheel
    // messages on its game-window HWND instead. The caller probes the latter
    // only after a fresh screenshot showed the child message had no effect.
    // Both targets are checked against osclient.exe's window PID, and neither
    // path changes the global cursor, focus, or foreground.
    internal static bool Wheel(IntPtr window,Point position,bool down,Func<bool> shouldStop,
        bool gameWindow=false)
    {
        if(shouldStop()||!IsWindow(window)||IsIconic(GetAncestor(window,2))||
            !GetClientRect(window,out var bounds)||position.X<0||position.Y<0||
            position.X>=bounds.Right||position.Y>=bounds.Bottom)return false;
        var local=new POINT{X=position.X,Y=position.Y};
        IntPtr destination=gameWindow?window:Resolve(window,ref local);
        GetWindowThreadProcessId(window,out uint expectedPid);
        GetWindowThreadProcessId(destination,out uint actualPid);
        if(expectedPid==0||expectedPid!=actualPid||shouldStop())return false;
        var localParam=new IntPtr((local.Y&0xFFFF)<<16|(local.X&0xFFFF));
        var screen=new POINT{X=local.X,Y=local.Y};
        if(!ClientToScreen(destination,ref screen))return false;
        IntPtr screenParam=new((screen.Y&0xFFFF)<<16|(screen.X&0xFFFF));
        int delta=down?-120:120;
        IntPtr wheelParam=new((delta&0xFFFF)<<16);
        return !shouldStop()&&PostMessage(destination,0x200,IntPtr.Zero,localParam)&&
            PostMessage(destination,0x20A,wheelParam,screenParam);
    }
    private static IntPtr Resolve(IntPtr window,ref POINT point)
    {
        IntPtr destination=window;
        for(int depth=0;depth<8;depth++)
        {
            var child=ChildWindowFromPointEx(destination,point,7);
            if(child==IntPtr.Zero||child==destination)break;
            MapWindowPoints(destination,child,ref point,1);
            destination=child;
        }
        return destination;
    }
    internal static async Task<bool> ClickAsync(IntPtr window,Point position,Func<bool> shouldStop,CancellationToken ct,bool right=false)
    {
        if(shouldStop()||!IsWindow(window)||IsIconic(GetAncestor(window,2))||
            !GetClientRect(window,out var bounds)||position.X<0||position.Y<0||
            position.X>=bounds.Right||position.Y>=bounds.Bottom)return false;
        var point=new POINT{X=position.X,Y=position.Y};
        IntPtr destination=Resolve(window,ref point);
        GetWindowThreadProcessId(window,out uint expectedPid);
        GetWindowThreadProcessId(destination,out uint actualPid);
        if(expectedPid==0||expectedPid!=actualPid||shouldStop())return false;
        IntPtr coordinates=new((point.Y&0xFFFF)<<16|(point.X&0xFFFF));
        if(!PostMessage(destination,0x200,IntPtr.Zero,coordinates))return false;
        if(!PostMessage(destination,right?0x204u:0x201u,new IntPtr(right?2:1),coordinates))return false;
        bool released=false;
        try
        {
            await Task.Delay(65,ct);
            released=PostMessage(destination,right?0x205u:0x202u,IntPtr.Zero,coordinates);
        }
        finally{if(!released)PostMessage(destination,right?0x205u:0x202u,IntPtr.Zero,coordinates);}
        return released;
    }
    // Mouse input is routed to the official game's child HWND. Escape must
    // reach that same child; posting VK_ESCAPE to the embed host HWND can
    // report success without actually dismissing the context menu.
    internal static bool Escape(IntPtr window,Point gamePoint)
    {
        if(!IsWindow(window)||IsIconic(GetAncestor(window,2))||
            !GetClientRect(window,out var bounds)||gamePoint.X<0||gamePoint.Y<0||
            gamePoint.X>=bounds.Right||gamePoint.Y>=bounds.Bottom)return false;
        var mapped=new POINT{X=gamePoint.X,Y=gamePoint.Y};
        IntPtr destination=Resolve(window,ref mapped);
        GetWindowThreadProcessId(window,out uint expectedPid);
        GetWindowThreadProcessId(destination,out uint actualPid);
        if(expectedPid==0||expectedPid!=actualPid)return false;
        // Ordinary WM_KEYDOWN/WM_KEYUP, no global keyboard or focus stealing.
        const long escapeDown=0x00010001L; // repeat count=1, scan code=1
        const long escapeUp=0xC0010001L;   // prior key + transition bits
        return PostMessage(destination,0x100,new IntPtr(0x1B),new IntPtr(escapeDown))&&
            PostMessage(destination,0x101,new IntPtr(0x1B),new IntPtr(escapeUp));
    }
    internal static async Task<bool> TypeTextAsync(IntPtr window,Point anchor,string text,bool submit,Func<bool> stop,CancellationToken ct)
    {
        if(stop()||!IsWindow(window)||IsIconic(GetAncestor(window,2))||text.Length is <1 or >50||text.Any(c=>c<32||c>126))return false;
        var point=new POINT{X=anchor.X,Y=anchor.Y};var target=Resolve(window,ref point);
        GetWindowThreadProcessId(window,out uint expected);GetWindowThreadProcessId(target,out uint actual);
        if(expected==0||expected!=actual)return false;
        foreach(char c in text)
        {
            ct.ThrowIfCancellationRequested();if(stop())return false;
            if(!PostMessage(target,0x102,new IntPtr(c),IntPtr.Zero))return false;
            await Task.Delay(20,ct);
        }
        if(!submit)return true;
        return !stop()&&PostMessage(target,0x100,new IntPtr(0x0D),IntPtr.Zero)&&PostMessage(target,0x101,new IntPtr(0x0D),IntPtr.Zero);
    }
    [StructLayout(LayoutKind.Sequential)]private struct POINT{internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct RECT{internal int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref POINT point);
    [DllImport("user32.dll")]private static extern IntPtr ChildWindowFromPointEx(IntPtr parent,POINT point,uint flags);
    [DllImport("user32.dll")]private static extern int MapWindowPoints(IntPtr from,IntPtr to,ref POINT point,uint count);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,[In] INPUT[] inputs,int structSize);
    [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)]private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}
