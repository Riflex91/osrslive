using System.Runtime.InteropServices;

namespace OSRSLiveDiagnostics;

internal static class BackgroundWindowInput
{
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
    internal static bool Escape(IntPtr window)=>IsWindow(window)&&!IsIconic(GetAncestor(window,2))&&
        PostMessage(window,0x100,new IntPtr(0x1B),IntPtr.Zero)&&PostMessage(window,0x101,new IntPtr(0x1B),IntPtr.Zero);
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
    [DllImport("user32.dll")]private static extern IntPtr ChildWindowFromPointEx(IntPtr parent,POINT point,uint flags);
    [DllImport("user32.dll")]private static extern int MapWindowPoints(IntPtr from,IntPtr to,ref POINT point,uint count);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)]private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}
