using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

internal sealed record RuneLitePositionState(string Status,WorldTile? Tile=null,
    DateTimeOffset ObservedUtc=default,string Diagnostic="");
internal sealed record RuneLiteGridEvidence(WorldTile Tile,int ChunkId,int RegionId);

// The regular World Location hub plugin renders the player's own API position.
// Read its numeric Grid Info panel, NOT its mouse-hover tile tooltip. No custom
// plugin, separate JVM, process-memory access, or atlas registration required.
internal static class RuneLiteGridReader
{
    internal static RuneLiteGridEvidence? Parse(IEnumerable<string> lines)
    {
        var text=lines.Select(s=>s.Trim()).ToArray();
        var tiles=text.Select(s=>Regex.Match(s,@"^Tile\s*:?\s*(\d{1,5})\s*,\s*(\d{1,5})\s*,\s*([0-3])$",RegexOptions.IgnoreCase)).Where(m=>m.Success).ToArray();
        var chunks=text.Select(s=>Regex.Match(s,@"^Chunk\s+ID\s*:?\s*(\d{1,7})$",RegexOptions.IgnoreCase)).Where(m=>m.Success).ToArray();
        var regions=text.Select(s=>Regex.Match(s,@"^Region\s+ID\s*:?\s*(\d{1,5})$",RegexOptions.IgnoreCase)).Where(m=>m.Success).ToArray();
        if(tiles.Length!=1||chunks.Length!=1||regions.Length!=1)return null;
        int x=int.Parse(tiles[0].Groups[1].Value),y=int.Parse(tiles[0].Groups[2].Value),plane=int.Parse(tiles[0].Groups[3].Value);
        int chunk=int.Parse(chunks[0].Groups[1].Value),region=int.Parse(regions[0].Groups[1].Value);
        if(x>16383||y>16383||chunk!=((x>>3)<<11|(y>>3))||region!=((x>>6)<<8|(y>>6)))return null;
        return new(new(x,y,plane),chunk,region);
    }
    // Different OCR treatments of one image must never resolve to different
    // valid player tiles. A matching chunk/region ID cannot distinguish two
    // tiles within the same chunk, or distinguish floors.
    internal static RuneLiteGridEvidence? ConsistentEvidence(IEnumerable<RuneLiteGridEvidence?> candidates)
    {
        var distinct=candidates.Where(c=>c is not null).Distinct().ToArray();
        return distinct.Length==1?distinct[0]:null;
    }

    private static void IncreaseTextContrast(Bitmap bitmap)
    {
        // Work only on an in-memory OCR copy. The semitransparent panel can
        // otherwise dominate the antialiased comma and slashed zero glyphs.
        var area=new Rectangle(Point.Empty,bitmap.Size);
        var bits=bitmap.LockBits(area,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try
        {
            if(bits.Stride<=0)return; // GDI's bottom-up buffers are not assumed contiguous here.
            int length=checked(bits.Stride*bitmap.Height);
            var bytes=new byte[length];Marshal.Copy(bits.Scan0,bytes,0,length);
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
            {
                int i=y*bits.Stride+x*4;
                int luma=(29*bytes[i]+150*bytes[i+1]+77*bytes[i+2])>>8;
                byte gray=(byte)Math.Clamp((luma-110)*3+128,0,255);
                bytes[i]=bytes[i+1]=bytes[i+2]=gray;bytes[i+3]=255;
            }
            Marshal.Copy(bytes,0,bits.Scan0,length);
        }
        finally{bitmap.UnlockBits(bits);}
    }

    internal static async Task<RuneLiteGridEvidence?> Read(Bitmap bitmap,OcrEngine engine,
        Action<string[]>? diagnostic=null,Action<string>? pixelDiagnostic=null)
    {
        // Fresh profiles place Grid Info at the canvas top-left. Read only the
        // small overlay region, excluding chat and account panes. No image saved.
        using var crop=bitmap.Clone(new Rectangle(0,0,Math.Min(350,bitmap.Width),Math.Min(220,bitmap.Height)),
            PixelFormat.Format32bppArgb);
        var evidenceCandidates=new List<RuneLiteGridEvidence?>();
        var observations=new List<RuneLitePixelGridReader.Observation>();
        foreach(var (scale,contrast) in new[]{(2,false),(3,false),(4,false),(4,true)})
        {
            using var enlarged=new Bitmap(crop.Width*scale,crop.Height*scale);
            using(var g=Graphics.FromImage(enlarged))
            {
                g.InterpolationMode=scale==4?System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic:
                    System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(crop,new Rectangle(Point.Empty,enlarged.Size));
            }
            if(contrast)IncreaseTextContrast(enlarged);
            var result=await VisualStartup.RecognizeRaw(enlarged,engine);
            var rows=new List<List<OcrWord>>();
            foreach(var word in result.Lines.SelectMany(l=>l.Words).OrderBy(w=>w.BoundingRect.Y))
            {
                double cy=word.BoundingRect.Y+word.BoundingRect.Height/2;
                var row=rows.FirstOrDefault(r=>Math.Abs(cy-r.Average(w=>w.BoundingRect.Y+w.BoundingRect.Height/2))<=Math.Max(5,word.BoundingRect.Height*.55));
                if(row is null){row=new();rows.Add(row);}row.Add(word);
            }
            var text=rows.Select(r=>string.Join(" ",r.OrderBy(w=>w.BoundingRect.X).Select(w=>w.Text))).ToArray();
            diagnostic?.Invoke(text);
            if(RuneLitePixelGridReader.ReadObservation(text) is { } observation)observations.Add(observation);
            var evidence=Parse(text);
            evidenceCandidates.Add(evidence);
            // The cheap, existing paths still get an agreement check. Avoid
            // expensive extra OCR unless neither has produced a valid panel.
            if(scale==3&&evidenceCandidates.Any(e=>e is not null))break;
        }
        var raster=RuneLitePixelGridReader.Read(crop,observations,out string rasterDiagnostic);
        pixelDiagnostic?.Invoke(rasterDiagnostic);
        // A valid pixel reading cannot override a different valid OCR reading.
        // The existing two-independent-fresh-frames gate remains unchanged.
        evidenceCandidates.Add(raster);
        return ConsistentEvidence(evidenceCandidates);
    }
}

internal sealed class RuneLiteOverlayConfirmation
{
    private IntPtr window;private Size size;private DateTimeOffset captured;private RuneLiteGridEvidence? previous;
    internal void Reset(){window=default;size=default;captured=default;previous=null;}
    internal RuneLitePositionState Observe(IntPtr target,Size bounds,DateTimeOffset timestamp,DateTimeOffset now,RuneLiteGridEvidence? evidence)
    {
        if(evidence is null){Reset();return new("GRID_INFO_NOT_READABLE");}
        if(timestamp>now||now-timestamp>TimeSpan.FromSeconds(2)||timestamp<=captured)
        {Reset();return new("STALE_FRAME");}
        bool independent=window==target&&size==bounds&&previous==evidence&&
            timestamp-captured>=TimeSpan.FromMilliseconds(300)&&timestamp-captured<=TimeSpan.FromSeconds(3);
        window=target;size=bounds;captured=timestamp;previous=evidence;
        return independent?new("OVERLAY_CONFIRMED",evidence.Tile,timestamp):new("WAITING_FOR_SECOND_FRAME");
    }
}

internal sealed class RuneLitePositionSource:IDisposable
{
    private readonly BackgroundWindowCapture capture=new();
    private readonly RuneLiteOverlayConfirmation confirmation=new();
    internal void Reset()=>confirmation.Reset();
    private string CaptureDiagnostic=>$"capture_state={capture.State} frame_events={capture.FrameEvents} "+
        $"frame_copies={capture.FrameCopies} capture_error={capture.LastFrameError} "+
        $"geometry={capture.Geometry} alpha={capture.AlphaState}";
    private readonly OcrEngine? engine=OcrEngine.TryCreateFromLanguage(new Language("en-US"))??OcrEngine.TryCreateFromUserProfileLanguages();
    internal async Task<RuneLitePositionState> ReadAsync(CancellationToken ct=default,
        Action<string[]>? diagnostic=null,Action<string>? pixelDiagnostic=null)
    {
        if(engine is null)return new("OCR_LANGUAGE_UNAVAILABLE",Diagnostic:"language=en-US/fallback unavailable");
        var windows=new List<IntPtr>();
        int scanned=0,rejected=0;
        foreach(var process in Process.GetProcessesByName("RuneLite"))using(process)
        {
            scanned++;
            try
            {
                string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RuneLite","RuneLite.exe");
                if(!process.HasExited&&process.MainWindowHandle!=IntPtr.Zero&&
                    string.Equals(process.MainModule?.FileName,expected,StringComparison.OrdinalIgnoreCase))windows.Add(process.MainWindowHandle);
                else rejected++;
            }
            catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){rejected++;}
        }
        string windowDiagnostic=$"processes={scanned} trusted_windows={windows.Count} rejected={rejected}";
        if(windows.Count!=1){confirmation.Reset();return new(windows.Count==0?"RUNELITE_NOT_RUNNING":"MULTIPLE_RUNELITE_CLIENTS",
            Diagnostic:windowDiagnostic);}
        try
        {
            using var frame=await capture.ReadClientAsync(windows[0],ct);
            if(frame is null){confirmation.Reset();return new("CAPTURE_UNAVAILABLE",
                Diagnostic:windowDiagnostic+" "+CaptureDiagnostic);}
            int passes=0,validPasses=0,labelRows=0;
            var evidence=await RuneLiteGridReader.Read(frame.Image,engine,rows=>
            {
                passes++;
                if(RuneLiteGridReader.Parse(rows) is not null)validPasses++;
                labelRows+=rows.Count(row=>Regex.IsMatch(row,@"^(Tile|Chunk\s+ID|Region\s+ID)",RegexOptions.IgnoreCase));
                diagnostic?.Invoke(rows);
            },pixelDiagnostic);
            var now=DateTimeOffset.UtcNow;
            var state=confirmation.Observe(windows[0],frame.Image.Size,frame.CapturedUtc,now,evidence);
            return state with{Diagnostic=windowDiagnostic+" "+CaptureDiagnostic+
                $" client={frame.Image.Width}x{frame.Image.Height} age_ms={(now-frame.CapturedUtc).TotalMilliseconds:0} "+
                $"ocr_passes={passes} valid_passes={validPasses} label_rows={labelRows} "+
                $"candidate_consistent={evidence is not null} frame_confirmed={state.Tile is not null}"};
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {confirmation.Reset();return new("INTERNAL_ERROR",
            Diagnostic:windowDiagnostic+" "+CaptureDiagnostic+
            " "+ExceptionDiagnostic.Describe(ex));}
    }
    public void Dispose()=>capture.Dispose();
}

internal static class RuneLitePositionProbe
{
    internal static async Task<int> Run()
    {
        using var source=new RuneLitePositionSource();
        bool confirmed=false;
        for(int i=0;i<12;i++)
        {
            var state=await source.ReadAsync(diagnostic:rows=>
            {
                // Only the three plugin labels; never dump arbitrary OCR text.
                foreach(string row in rows.Where(r=>Regex.IsMatch(r,@"^(Tile|Chunk\s+ID\s|Region\s+ID\s)",RegexOptions.IgnoreCase)))
                    Console.WriteLine("GRID_OCR "+row);
            });
            Console.WriteLine($"RUNELITE_PROBE {i+1} {state.Status}"+
                (state.Tile is WorldTile t?$" x={t.X} y={t.Y} plane={t.Plane}":""));
            confirmed|=state.Tile is not null;
            await Task.Delay(800);
        }
        Console.WriteLine("RUNELITE_PROBE no game input; no screenshots persisted");
        return confirmed?0:2;
    }
}
