using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace OSRSLiveDiagnostics;

internal sealed record CapturedWindowFrame(Bitmap Image,DateTimeOffset CapturedUtc):IDisposable
{public void Dispose()=>Image.Dispose();}

// Captures the host window's compositor surface, never the desktop underneath
// other applications. Only the official game's client rectangle is returned.
internal sealed class BackgroundWindowCapture:IDisposable
{
    private readonly object sync=new();
    private IDirect3DDevice? device;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private Bitmap? latest;
    private DateTimeOffset latestUtc,lastConversion;
    private IntPtr root;
    private IntPtr clientWindow;
    private Size rootSize;
    private int converting,generation;
    internal string State {get;private set;}="WAITING_FOR_FRAME";
    internal int FrameEvents,FrameCopies;
    internal string LastFrameError="none";
    internal string Geometry {get;private set;}="unknown";

    internal CapturedWindowFrame? ReadClient(IntPtr window)
    {
        if(window==IntPtr.Zero||!IsWindow(window)) {State="NO_WINDOW";return null;}
        IntPtr owner=GetAncestor(window,2);
        if(IsIconic(owner)){State="MINIMIZED_NO_FRESH_FRAME";return null;}
        if(!GetWindowRect(owner,out var native)){State="BOUNDS_UNAVAILABLE";return null;}
        var extended=native;
        if(DwmGetWindowAttribute(owner,9,out extended,Marshal.SizeOf<RECT>())!=0)extended=native;
        Size expected=new(extended.Right-extended.Left,extended.Bottom-extended.Top);
        bool refreshStatic;
        lock(sync)refreshStatic=latest is not null&&DateTimeOffset.UtcNow-latestUtc>TimeSpan.FromMilliseconds(750);
        if(root!=owner||clientWindow!=window||rootSize!=expected||pool is null||refreshStatic)
        {
            Stop();
            clientWindow=window;
            Start(owner,expected);
            return null;
        }
        if(!GetClientRect(window,out var client))return null;
        var origin=new POINT();
        if(!ClientToScreen(window,ref origin))return null;
        lock(sync)
        {
            if(latest is null||DateTimeOffset.UtcNow-latestUtc>TimeSpan.FromSeconds(3))
            {State=LastFrameError=="none"?"WAITING_FOR_FRESH_FRAME":"FRAME_ERROR_"+LastFrameError;return null;}
            // Windows versions differ in whether invisible resize borders are
            // present. Match actual surface dimensions rather than guessing.
            RECT matched=extended;
            if(latest.Size!=expected)
            {
                if(latest.Width!=native.Right-native.Left||latest.Height!=native.Bottom-native.Top)
                {State="FRAME_GEOMETRY_CHANGED";return null;}
                matched=native;
            }
            var crop=new Rectangle(origin.X-matched.Left,origin.Y-matched.Top,client.Right,client.Bottom);
            Geometry=$"frame={latest.Width}x{latest.Height} client={client.Right}x{client.Bottom} crop={crop.X},{crop.Y},{crop.Width},{crop.Height}";
            if(!new Rectangle(Point.Empty,latest.Size).Contains(crop)||crop.Width<1||crop.Height<1)
            {State="CLIENT_CLIPPED";return null;}
            State="BACKGROUND_FRAME_READY";
            return new(latest.Clone(crop,PixelFormat.Format32bppArgb),latestUtc);
        }
    }
    private void Start(IntPtr owner,Size expected)
    {
        try
        {
            LastFrameError="none";
            if(!GraphicsCaptureSession.IsSupported())throw new NotSupportedException("Windows Graphics Capture unsupported");
            device=CreateDevice();
            var item=CreateItem(owner);
            root=owner;rootSize=expected;
            pool=Direct3D11CaptureFramePool.CreateFreeThreaded(device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
            int currentGeneration=generation;
            pool.FrameArrived+=(sender,_)=>OnFrame(sender,currentGeneration);
            session=pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled=false;
            session.StartCapture();
            State="WAITING_FOR_FRAME";
        }
        catch(Exception ex){State="CAPTURE_UNAVAILABLE_"+ex.GetType().Name;Stop();}
    }
    private async void OnFrame(Direct3D11CaptureFramePool sender,int currentGeneration)
    {
        Interlocked.Increment(ref FrameEvents);
        Direct3D11CaptureFrame? frame=null;
        bool entered=false;
        try
        {
            if(currentGeneration!=generation)return;
            frame=sender.TryGetNextFrame();
            if(!IsWindow(clientWindow)||GetAncestor(clientWindow,2)!=root||IsIconic(root))return;
            if(frame is null||DateTimeOffset.UtcNow-lastConversion<TimeSpan.FromMilliseconds(250)||
                Interlocked.CompareExchange(ref converting,1,0)!=0)return;
            entered=true;
            lastConversion=DateTimeOffset.UtcNow;
            var systemTime=frame.SystemRelativeTime;
            if(systemTime<=TimeSpan.Zero)return;
            var age=TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp()/Stopwatch.Frequency)-systemTime;
            if(age<TimeSpan.FromMilliseconds(-100)||age>TimeSpan.FromSeconds(3))
            {LastFrameError="STALE_AGE_MS_"+Math.Round(age.TotalMilliseconds);return;}
            if(age<TimeSpan.Zero)age=TimeSpan.Zero; // compositor timestamp can lead QPC by one refresh
            var capturedUtc=DateTimeOffset.UtcNow-age;
            using var software=await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface,BitmapAlphaMode.Ignore);
            using var converted=SoftwareBitmap.Convert(software,BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore);
            var buffer=new Windows.Storage.Streams.Buffer((uint)(converted.PixelWidth*converted.PixelHeight*4));
            converted.CopyToBuffer(buffer);
            var bytes=new byte[buffer.Length];
            using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(bytes);
            var bitmap=new Bitmap(converted.PixelWidth,converted.PixelHeight,PixelFormat.Format32bppArgb);
            var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try{Marshal.Copy(bytes,0,data.Scan0,bytes.Length);}finally{bitmap.UnlockBits(data);}
            lock(sync)
            {
                if(currentGeneration!=generation){bitmap.Dispose();return;}
                latest?.Dispose();latest=bitmap;latestUtc=capturedUtc;
                LastFrameError="none";Interlocked.Increment(ref FrameCopies);
            }
        }
        catch(Exception ex){if(currentGeneration==generation){LastFrameError=ex.GetType().Name+"_"+ex.HResult.ToString("X");State="FRAME_UNAVAILABLE_"+LastFrameError;}}
        finally{frame?.Dispose();if(entered)Interlocked.Exchange(ref converting,0);}
    }
    private static GraphicsCaptureItem CreateItem(IntPtr window)
    {
        IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,item=IntPtr.Zero;
        try
        {
            const string className="Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className,className.Length,out name));
            var iid=typeof(IGraphicsCaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name,ref iid,out factory));
            var interop=(IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            var itemIid=new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            interop.CreateForWindow(window,ref itemIid,out item);
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if(item!=IntPtr.Zero)Marshal.Release(item);
            if(factory!=IntPtr.Zero)Marshal.Release(factory);
            if(name!=IntPtr.Zero)WindowsDeleteString(name);
        }
    }
    private static IDirect3DDevice CreateDevice()
    {
        IntPtr native=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,inspectable=IntPtr.Zero;
        try
        {
            int hr=D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out native,out _,out context);
            if(hr<0)hr=D3D11CreateDevice(IntPtr.Zero,5,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out native,out _,out context);
            Marshal.ThrowExceptionForHR(hr);
            var iid=new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native,in iid,out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            foreach(var pointer in new[]{inspectable,dxgi,context,native})if(pointer!=IntPtr.Zero)Marshal.Release(pointer);
        }
    }
    private void Stop()
    {
        lock(sync){generation++;latest?.Dispose();latest=null;}
        var oldSession=session;var oldPool=pool;var oldDevice=device;
        session=null;pool=null;device=null;root=IntPtr.Zero;clientWindow=IntPtr.Zero;lastConversion=default;
        // Closing a capture session may wait for compositor work. Never wait
        // on the form's message thread, particularly during WM_DESTROY.
        if(oldSession is not null||oldPool is not null||oldDevice is not null)
            _=Task.Run(()=>
            {
                try{oldSession?.Dispose();}catch{}
                try{oldPool?.Dispose();}catch{}
                try{oldDevice?.Dispose();}catch{}
            });
    }
    public void Dispose()=>Stop();
    internal void Release(){if(pool is not null)Stop();}
    internal async Task<CapturedWindowFrame?> ReadClientAsync(IntPtr window,CancellationToken ct)
    {
        for(int i=0;i<25;i++)
        {
            ct.ThrowIfCancellationRequested();
            var frame=ReadClient(window);
            if(frame is not null)return frame;
            if(State.Contains("MINIMIZED")||State.Contains("UNAVAILABLE")||State=="NO_WINDOW")return null;
            await Task.Delay(40,ct);
        }
        return null;
    }
    [ComImport,Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(IntPtr window,ref Guid iid,out IntPtr item);
        void CreateForMonitor(IntPtr monitor,ref Guid iid,out IntPtr item);
    }
    [StructLayout(LayoutKind.Sequential)]private struct POINT{internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct RECT{internal int Left,Top,Right,Bottom;}
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDevice(IntPtr adapter,uint driver,IntPtr software,
        uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out uint level,out IntPtr context);
    [DllImport("d3d11.dll")]private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr device);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string source,int length,out IntPtr value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")]private static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref POINT point);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out RECT value,int size);
}
