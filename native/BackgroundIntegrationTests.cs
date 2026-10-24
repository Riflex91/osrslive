using System.Runtime.InteropServices;

namespace OSRSLiveDiagnostics;

internal static class BackgroundIntegrationTests
{
    internal static int Run()
    {
        using var test=new CaptureTestForm();
        Application.Run(test);
        return test.ExitCode;
    }
    private sealed class PassiveForm:Form
    {protected override bool ShowWithoutActivation=>true;}
    private sealed class PassiveInputPanel:Panel
    {
        internal Action? Pressed;
        internal Action? RightPressed;
        internal Action<char>? Character;
        protected override void WndProc(ref Message m)
        {
            // A passive receiver isolates the message transport from WinForms'
            // normal selectable-control focus policy.
            if(m.Msg==0x201){Pressed?.Invoke();return;}
            if(m.Msg==0x202)return;
            if(m.Msg==0x204){RightPressed?.Invoke();return;}
            if(m.Msg==0x205)return;
            if(m.Msg==0x102){Character?.Invoke((char)m.WParam);return;}
            base.WndProc(ref m);
        }
    }
    private sealed class CaptureTestForm:Form
    {
        internal int ExitCode=1;
        protected override bool ShowWithoutActivation=>true;
        internal CaptureTestForm()
        {
            Text="OSRS background transport test";ShowInTaskbar=false;
            FormBorderStyle=FormBorderStyle.FixedSingle;ClientSize=new Size(320,220);
            StartPosition=FormStartPosition.Manual;Location=new Point(35,35);BackColor=Color.Blue;
            Shown+=async (_,_)=>await Verify();
        }
        private async Task Verify()
        {
            using var capture=new BackgroundWindowCapture();
            using var cover=new PassiveForm{ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,
                StartPosition=FormStartPosition.Manual,Bounds=Bounds,BackColor=Color.Red};
            var target=new PassiveInputPanel{Bounds=new Rectangle(20,20,100,80),BackColor=Color.Blue};
            Controls.Add(target);
            int clicks=0;
            int rightClicks=0;string typed="";
            target.Pressed=()=>{clicks++;target.BackColor=Color.LimeGreen;};
            target.RightPressed=()=>rightClicks++;
            target.Character=c=>typed+=c;
            try
            {
                cover.Show();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
                CapturedWindowFrame? blue=null;
                while(blue is null)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await Task.Delay(200,timeout.Token);
                    blue=capture.ReadClient(Handle);
                    if(capture.State.StartsWith("CAPTURE_UNAVAILABLE",StringComparison.Ordinal))
                        throw new NotSupportedException(capture.State);
                }
                using(blue)
                {
                    var pixel=blue.Image.GetPixel(50,50);
                    if(pixel.B<180||pixel.R>80)throw new Exception("Occluded window surface is not blue");
                }
                var foreground=GetForegroundWindow();GetCursorPos(out var cursor);
                var inputBefore=new LASTINPUTINFO{Size=(uint)Marshal.SizeOf<LASTINPUTINFO>()};GetLastInputInfo(ref inputBefore);
                bool queued=await BackgroundWindowInput.ClickAsync(Handle,new Point(50,50),()=>false,timeout.Token);
                bool rightQueued=await BackgroundWindowInput.ClickAsync(Handle,new Point(50,50),()=>false,timeout.Token,true);
                bool textQueued=await BackgroundWindowInput.TypeTextAsync(Handle,new Point(50,50),"125",false,()=>false,timeout.Token);
                await Task.Delay(150,timeout.Token);
                GetCursorPos(out var after);
                var inputAfter=new LASTINPUTINFO{Size=(uint)Marshal.SizeOf<LASTINPUTINFO>()};GetLastInputInfo(ref inputAfter);
                if(!queued||clicks!=1)throw new Exception("Background child did not receive exactly one click");
                if(!rightQueued||rightClicks!=1||!textQueued||typed!="125")throw new Exception("Background right-click/text transport failed");
                bool userInteracted=inputBefore.Tick!=inputAfter.Tick;
                if(!userInteracted&&(GetForegroundWindow()!=foreground||cursor.X!=after.X||cursor.Y!=after.Y))
                    throw new Exception($"Foreground/cursor changed: focus_same={GetForegroundWindow()==foreground} "+
                        $"cursor_same={cursor.X==after.X&&cursor.Y==after.Y}; user input can interrupt this check");
                bool green=false;
                while(!green)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await Task.Delay(200,timeout.Token);
                    using var frame=capture.ReadClient(Handle);
                    if(frame is null)continue;
                    var pixel=frame.Image.GetPixel(50,50);
                    green=pixel.G>150&&pixel.R<100&&pixel.B<100;
                }
                Console.WriteLine("BACKGROUND TRANSPORT PASS: occluded surface, child-directed left/right clicks and text, observed pixel confirmation; "+
                    (userInteracted?"cursor/focus comparison inconclusive due to concurrent user input":"cursor/focus unchanged"));
                ExitCode=0;
            }
            catch(NotSupportedException ex)
            {ExitCode=2;Console.Error.WriteLine("BACKGROUND TRANSPORT UNAVAILABLE: "+ex.Message);}
            catch(Exception ex){Console.Error.WriteLine("BACKGROUND TRANSPORT FAIL: "+ex.Message+" capture="+capture.State+
                $" events={capture.FrameEvents} copies={capture.FrameCopies} error={capture.LastFrameError}");}
            finally{capture.Dispose();cover.Close();Close();}
        }
        [StructLayout(LayoutKind.Sequential)]private struct POINT{internal int X,Y;}
        [StructLayout(LayoutKind.Sequential)]private struct LASTINPUTINFO{internal uint Size,Tick;}
        [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")]private static extern bool GetLastInputInfo(ref LASTINPUTINFO value);
    }
}
