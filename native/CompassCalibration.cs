using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

internal sealed record CompassPixels(Rectangle ClickBounds,double Degrees,int RedPixels,int BluePixels)
{
    internal bool North=>Math.Abs(Degrees)<=7.5;
}

internal static class CompassVision
{
    internal static CompassPixels? Read(Bitmap image,Rectangle map)
        =>Read(image,map,out _);
    internal static CompassPixels? Read(Bitmap image,Rectangle map,out string evidence)
    {
        evidence="native_geometry_missing";
        if(image.Size!=DockedWindowLayout.GameSize)return null;
        var area=new Rectangle(map.Left-20,Math.Max(0,map.Top-7),26,44);
        if(!new Rectangle(Point.Empty,image.Size).Contains(area))return null;
        var red=new List<Point>();var blue=new List<Point>();
        for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)
        {
            var c=image.GetPixel(x,y);
            if(c.R>100&&c.R>c.G*1.4&&c.R>c.B*1.3)red.Add(new(x,y));
            if(c.B>80&&c.B>c.R*1.3&&c.B>c.G*1.15)blue.Add(new(x,y));
        }
        evidence=$"map={map.X},{map.Y},{map.Width} roi={area.X},{area.Y},{area.Width},{area.Height} red={red.Count} blue={blue.Count}";
        if(blue.Count is <8 or >50)return null;
        double bx=blue.Average(p=>p.X),by=blue.Average(p=>p.Y);
        // The bronze compass rim can satisfy a loose red threshold. Only
        // nearby red pixels can belong to the opposite half of this needle.
        red=red.Where(p=>(p.X-bx)*(p.X-bx)+(p.Y-by)*(p.Y-by)<=100).ToList();
        evidence+=$" needle_red={red.Count}";
        if(red.Count is <20 or >85)return null;
        var all=red.Concat(blue).ToArray();
        evidence+=$" color_bounds={all.Min(p=>p.X)},{all.Min(p=>p.Y)},{all.Max(p=>p.X)-all.Min(p=>p.X)+1},{all.Max(p=>p.Y)-all.Min(p=>p.Y)+1}";
        if(all.Max(p=>p.X)-all.Min(p=>p.X)>16||all.Max(p=>p.Y)-all.Min(p=>p.Y)>16)return null;
        double rx=red.Average(p=>p.X),ry=red.Average(p=>p.Y);
        double separation=Math.Sqrt((rx-bx)*(rx-bx)+(ry-by)*(ry-by));
        if(separation is <2 or >9)return null;
        int cx=(int)Math.Round((rx+bx)/2),cy=(int)Math.Round((ry+by)/2);
        return new(new(cx-3,cy-3,7,7),Math.Atan2(rx-bx,by-ry)*180/Math.PI,red.Count,blue.Count);
    }
    internal static bool Tooltip(VisualFrame frame)=>frame.Labels.Any(l=>
        (new Rectangle(0,0,500,42).Contains(l.Bounds)||new Rectangle(740,0,260,100).Contains(l.Bounds))&&
        Regex.IsMatch(l.Text.Trim(),@"^Look\s+North(?:\s*/\s*\d+\s+more\s+options)?$",RegexOptions.IgnoreCase));
    internal static bool InputAuthorized(VisualFrame frame,GameInput input)
    {
        if(frame.ScreenBounds.Size!=DockedWindowLayout.GameSize||frame.Pixels?.WorldMapPanelOpen==true||
            GameScreenReader.Read(frame).Stage!=VisibleGameStage.InGame||frame.Pixels?.Compass is not CompassPixels compass)return false;
        var point=new Point(input.Bounds.Left+input.Bounds.Width/2,input.Bounds.Top+input.Bounds.Height/2);
        return compass.ClickBounds.Contains(point)&&
            (input.Kind==GameInputKind.Hover||input.Kind==GameInputKind.LeftClick&&Tooltip(frame));
    }
}

internal sealed class CompassCalibrationRoutine
{
    // A previously missing compass may reappear after the HUD settles. This
    // only clears that one diagnostic block; Run() still requires independent
    // north observations and authorizes no click without its own tooltip.
    internal static bool MayResumeAfterFreshPixels(RoutineResult? previous,
        VisualFrame frame,DateTimeOffset blockedAt)=>
        previous is {Status:RoutineStatus.Blocked,Reason:"COMPASS_PIXEL_NEEDLE_NOT_VERIFIED"}&&
        frame.CapturedUtc>blockedAt&&
        frame.Pixels is {Compass:not null,WorldMapPanelOpen:false}&&
        GameScreenReader.Read(frame).Stage==VisibleGameStage.InGame;
    private readonly IVisualGameDriver driver;private readonly Func<bool> stopped;
    private readonly Action<string> report;private readonly Func<int,CancellationToken,Task> wait;
    internal CompassCalibrationRoutine(IVisualGameDriver driver,Func<bool> stopped,Action<string> report,
        Func<int,CancellationToken,Task>? wait=null)
    {this.driver=driver;this.stopped=stopped;this.report=report;this.wait=wait??Task.Delay;}
    internal async Task<RoutineResult> Run(CancellationToken ct)
    {
        var window=driver as WindowVisualGameDriver;var prior=window?.InputGuard;
        if(window is not null)window.InputGuard=(f,input)=>!stopped()&&(prior?.Invoke(f,input)??true)&&CompassVision.InputAuthorized(f,input);
        try{return await Execute(ct);}
        catch(OperationCanceledException) when(stopped()&&!ct.IsCancellationRequested)
        {return new(RoutineStatus.Waiting,"COMPASS_PAUSED");}
        catch(RoutineBlockedException) when(stopped())
        {return new(RoutineStatus.Waiting,"COMPASS_PAUSED");}
        catch(RoutineBlockedException ex){return new(RoutineStatus.Blocked,"COMPASS_"+ex.Message);}
        catch(Exception ex) when(ex is not OperationCanceledException)
        {return new(RoutineStatus.Blocked,"COMPASS_VISION_ERROR_"+ex.GetType().Name);}
        finally{if(window is not null)window.InputGuard=prior;}
    }
    private async Task<RoutineResult> Execute(CancellationToken ct)
    {
        if(stopped())return new(RoutineStatus.Waiting,"COMPASS_PAUSED");
        await driver.ObserveAsync(ct);
        DateTimeOffset lastSample=default;
        // Startup OCR and circular-HUD detection can transiently disagree just
        // after the Skills panel opens. Reobserve passively; absent compass
        // pixels must never turn into a guessed click location.
        for(int attempt=0;attempt<=8;attempt++)
        {
            var frame=driver.Current;
            report($"COMPASS_PIXEL_EVIDENCE sample={attempt+1} "+
                $"compass={frame.Pixels?.CompassEvidence??"unavailable"} "+
                $"minimap={frame.Pixels?.MinimapEvidence??"unavailable"} "+
                $"fresh_frame={attempt==0||frame.CapturedUtc>lastSample} no_input=true");
            if(frame.Pixels?.WorldMapPanelOpen==true)
                return new(RoutineStatus.Blocked,"COMPASS_PIXEL_NEEDLE_NOT_VERIFIED");
            if(frame.Pixels?.Compass is CompassPixels)break;
            if(attempt==8)return new(RoutineStatus.Blocked,"COMPASS_PIXEL_NEEDLE_NOT_VERIFIED");
            if(stopped())return new(RoutineStatus.Waiting,"COMPASS_PAUSED");
            lastSample=frame.CapturedUtc;
            await wait(550,ct);
            await driver.ObserveAsync(ct);
        }
        if(driver.Current.Pixels?.Compass is not CompassPixels first)
            return new(RoutineStatus.Blocked,"COMPASS_PIXEL_NEEDLE_NOT_VERIFIED");
        report($"COMPASS_BASELINE degrees={first.Degrees:F2} north={first.North} red={first.RedPixels} blue={first.BluePixels}");
        bool clicked=false;
        if(!first.North)
        {
            await driver.InputAsync(new(GameInputKind.Hover,first.ClickBounds),ct);
            for(int i=0;i<3&&!CompassVision.Tooltip(driver.Current);i++)
            {await wait(250,ct);await driver.ObserveAsync(ct);}
            if(!CompassVision.Tooltip(driver.Current)||driver.Current.Pixels?.Compass is not CompassPixels fresh)
                return new(RoutineStatus.Blocked,"COMPASS_LOOK_NORTH_CONTROL_NOT_VERIFIED");
            if(stopped())return new(RoutineStatus.Waiting,"COMPASS_PAUSED");
            await driver.InputAsync(new(GameInputKind.LeftClick,fresh.ClickBounds),ct);clicked=true;
        }
        int confirmations=0;DateTimeOffset last=driver.Current.CapturedUtc;
        for(int attempt=0;attempt<4;attempt++)
        {
            if(stopped())return new(RoutineStatus.Waiting,"COMPASS_PAUSED");
            await wait(250,ct);var frame=await driver.ObserveAsync(ct);
            if(frame.CapturedUtc<=last)continue;last=frame.CapturedUtc;
            if(frame.Pixels?.Compass is CompassPixels compass&&compass.North&&frame.Pixels.WorldMapPanelOpen==false)
            {
                report($"COMPASS_SAMPLE degrees={compass.Degrees:F2} north=true independent_frame=true");
                if(++confirmations==2)return new(RoutineStatus.Completed,clicked?
                    "COMPASS_NORTH_AFTER_CLICK_CONFIRMED":"COMPASS_ALREADY_NORTH_CONFIRMED");
            }
            else confirmations=0;
        }
        return new(RoutineStatus.Blocked,"COMPASS_NORTH_NOT_CONFIRMED");
    }
}
