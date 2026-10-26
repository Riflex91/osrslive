using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal sealed record MapLocalization(MapLocation? Location,string Status,int Features=0,
    int Candidates=0,int BestInliers=0,int BestRatioMatches=0);

internal sealed class WorldNavigator:IDisposable
{
    internal WorldMapData? Data{get;private set;}
    internal WorldNavigator(WorldMapData? data=null){Data=data;}
    private readonly Dictionary<(WorldTile,WorldTile),int> stalls=new();
    private WorldMapPosition? worldMapHint;
    private readonly List<WorldMapData.ReferenceMap> localReferences=new();
    internal void ClearWorldMapHint()
    {worldMapHint=null;foreach(var reference in localReferences)reference.Dispose();localReferences.Clear();}
    internal void SetWorldMapHint(WorldMapPosition hint)
    {
        ClearWorldMapHint();worldMapHint=hint;
        if(Data is null)return;
        int x=(hint.SurfaceTile.X-WorldMapData.MinX)*4+2,y=(WorldMapData.MaxY-hint.SurfaceTile.Y)*4-2;
        foreach(var reference in Data.MinimapReferences)
        {
            var area=Cv.Rect.Intersect(new(x-reference.OriginX-256,y-reference.OriginY-256,512,512),new(0,0,reference.Image.Width,reference.Image.Height));
            if(area.Width<128||area.Height<128)continue;
            using var region=new Cv.Mat(reference.Image,area);
            localReferences.Add(new(reference.Plane,region.Clone(),reference.OriginX+area.X,reference.OriginY+area.Y));
        }
    }
    internal async Task Ready(Action<string> report,CancellationToken ct)
    {Data??=await WorldMapData.Load(report,ct);}
    internal int Stall(WorldTile from,WorldTile to)
    {var key=(from,to);stalls.TryGetValue(key,out int n);return stalls[key]=n+1;}
    internal MapLocation? Locate(VisualFrame frame)=>LocateDetailed(frame).Location;
    // Failure categories contain only feature counts, never the captured
    // minimap image or unverified coordinates. Navigation must fail closed.
    internal MapLocalization LocateDetailed(VisualFrame frame)
    {
        var native=LocateAtSampling(frame,1);
        // An ambiguous valid match must not become unique by trying another
        // sampling. Only feature/match failures get one additional analysis.
        if(native.Location is not null||native.Status is not ("NO_VERIFIED_MAP_MATCH" or "MINIMAP_FEATURES_INSUFFICIENT"))return native;
        var enlarged=LocateAtSampling(frame,2);
        if(enlarged.Location is null&&(enlarged.Status is "NO_VERIFIED_MAP_MATCH" or "MINIMAP_FEATURES_INSUFFICIENT")&&
            worldMapHint is not null&&DateTimeOffset.UtcNow-worldMapHint.ObservedUtc is var age&&age>=TimeSpan.Zero&&age<TimeSpan.FromMinutes(5))
        {
            var local=LocateAtSampling(frame,1,local:true);
            if(local.Location is not null||local.Status=="MAP_MATCH_AMBIGUOUS")return local;
            local=LocateAtSampling(frame,2,local:true);
            if(local.Location is not null||local.Status=="MAP_MATCH_AMBIGUOUS"||local.BestRatioMatches>enlarged.BestRatioMatches)enlarged=local;
        }
        return enlarged with{Features=Math.Max(native.Features,enlarged.Features),
            BestInliers=Math.Max(native.BestInliers,enlarged.BestInliers),
            BestRatioMatches=Math.Max(native.BestRatioMatches,enlarged.BestRatioMatches)};
    }
    internal MapLocalization LocateAtSampling(VisualFrame frame,int sampling,bool local=false)
    {
        if(sampling is not (1 or 2))throw new ArgumentOutOfRangeException(nameof(sampling));
        if(Data is null)return new(null,"REFERENCE_MAP_NOT_READY");
        if(Enumerable.Range(0,4).Any(p=>!Data.MinimapReferences.Any(r=>r.Plane==p)))return new(null,"MINIMAP_REFERENCES_INCOMPLETE");
        if(local&&(worldMapHint is null||DateTimeOffset.UtcNow-worldMapHint.ObservedUtc is var age&&
            (age<TimeSpan.Zero||age>=TimeSpan.FromMinutes(5))))return new(null,"WORLD_MAP_HINT_EXPIRED");
        if(frame.Pixels?.Navigation is not NavigationPixels pixels)return new(null,"MINIMAP_NOT_IDENTIFIED");
        if(pixels.MinimapPng.Length==0)return new(null,"MINIMAP_IMAGE_INVALID");
        using var image=Cv.Cv2.ImDecode(pixels.MinimapPng,Cv.ImreadModes.Grayscale);
        if(image.Empty()||image.Width is <100 or >190||image.Height!=image.Width)
            return new(null,"MINIMAP_IMAGE_INVALID");
        using var mask=new Cv.Mat(image.Size(),Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        Cv.Cv2.Circle(mask,new(image.Width/2,image.Height/2),image.Width/2-10,Cv.Scalar.White,-1);
        Cv.Cv2.Circle(mask,new(image.Width/2,image.Height/2),8,Cv.Scalar.Black,-1);
        using var featureImage=new Cv.Mat();using var featureMask=new Cv.Mat();
        Cv.Cv2.Resize(image,featureImage,new(image.Width*sampling,image.Height*sampling),interpolation:Cv.InterpolationFlags.Linear);
        Cv.Cv2.Resize(mask,featureMask,featureImage.Size(),interpolation:Cv.InterpolationFlags.Nearest);
        using var sift=Cv.Features2D.SIFT.Create(500,contrastThreshold:.01);
        using var descriptors=new Cv.Mat();sift.DetectAndCompute(featureImage,featureMask,out var points,descriptors);
        if(points.Length<8||descriptors.Empty())return new(null,"MINIMAP_FEATURES_INSUFFICIENT",points.Length);
        var candidates=new List<MapLocation>();
        int bestInliers=0,bestPairs=0;
        using var matcher=new Cv.BFMatcher(Cv.NormTypes.L2);
        foreach(var reference in local?localReferences:Data.MinimapReferences)
        {
            if(reference.Descriptors.Rows<8)continue;
            var pairs=matcher.KnnMatch(descriptors,reference.Descriptors,2)
                .Where(p=>p.Length==2&&p[0].Distance<.65*p[1].Distance).Select(p=>p[0]).ToArray();
            bestPairs=Math.Max(bestPairs,pairs.Length);
            if(pairs.Length<8)continue;
            // Fit in original screenshot pixels, preserving native spread,
            // reprojection, click coordinates and the independent edge test.
            var from=pairs.Select(p=>new Cv.Point2f(points[p.QueryIdx].Pt.X/sampling,points[p.QueryIdx].Pt.Y/sampling)).ToArray();
            var to=pairs.Select(p=>reference.Points[p.TrainIdx].Pt).ToArray();
            using var input=Cv.InputArray.Create(from);using var output=Cv.InputArray.Create(to);using var inliers=new Cv.Mat();
            using var affine=Cv.Cv2.EstimateAffinePartial2D(input,output,inliers,Cv.RobustEstimationAlgorithms.RANSAC,2,3000,.995,20);
            if(affine is null||affine.Empty())continue;
            int count=Cv.Cv2.CountNonZero(inliers);bestInliers=Math.Max(bestInliers,count);
            if(count<8||count<pairs.Length*.65)continue;
            var accepted=from.Where((_,i)=>inliers.At<byte>(i)!=0).ToArray();
            if(accepted.Max(p=>p.X)-accepted.Min(p=>p.X)<25||accepted.Max(p=>p.Y)-accepted.Min(p=>p.Y)<25)continue;
            var t=new[]{affine.At<double>(0,0),affine.At<double>(0,1),affine.At<double>(0,2),affine.At<double>(1,0),affine.At<double>(1,1),affine.At<double>(1,2)};
            double scale=Math.Sqrt(t[0]*t[0]+t[3]*t[3]);if(scale is <.35 or >3)continue;
            double cx=image.Width/2d,cy=image.Height/2d,px=t[0]*cx+t[1]*cy+t[2],py=t[3]*cx+t[4]*cy+t[5];
            var tile=new WorldTile(WorldMapData.MinX+(int)Math.Floor((px+reference.OriginX)/4),
                WorldMapData.MaxY-1-(int)Math.Floor((py+reference.OriginY)/4),reference.Plane);
            if(!WorldMapData.Domain(tile))continue;
            // RANSAC consensus must also agree with visible map edges. This
            // rejects a coincidental feature cluster in repeated scenery.
            using var warped=new Cv.Mat();
            Cv.Cv2.WarpAffine(reference.Image,warped,affine,image.Size(),Cv.InterpolationFlags.Linear|Cv.InterpolationFlags.WarpInverseMap);
            if(!EdgesAgree(image,warped,mask))continue;
            t[2]+=reference.OriginX;t[5]+=reference.OriginY;
            candidates.Add(new(tile,t,pixels.MapBounds,count,frame.CapturedUtc));
        }
        var ranked=candidates.OrderByDescending(c=>c.Inliers).ToArray();
        if(ranked.Length==0)return new(null,"NO_VERIFIED_MAP_MATCH",points.Length,0,bestInliers,bestPairs);
        if(ranked.Length>1&&ranked[0].Inliers<ranked[1].Inliers*1.35)
            return new(null,"MAP_MATCH_AMBIGUOUS",points.Length,ranked.Length,bestInliers,bestPairs);
        return new(ranked[0],"VERIFIED",points.Length,ranked.Length,bestInliers,bestPairs);
    }
    internal static bool EdgesAgree(Cv.Mat actual,Cv.Mat reference,Cv.Mat mask)
    {
        using var a=new Cv.Mat();using var b=new Cv.Mat();using var expanded=new Cv.Mat();using var overlap=new Cv.Mat();
        Cv.Cv2.Canny(actual,a,45,110);Cv.Cv2.Canny(reference,b,45,110);Cv.Cv2.BitwiseAnd(a,mask,a);
        using var kernel=Cv.Cv2.GetStructuringElement(Cv.MorphShapes.Rect,new(3,3));Cv.Cv2.Dilate(b,expanded,kernel);
        Cv.Cv2.BitwiseAnd(a,expanded,overlap);int edges=Cv.Cv2.CountNonZero(a);
        return edges>=70&&Cv.Cv2.CountNonZero(overlap)>=edges*.45;
    }
    public void Dispose(){ClearWorldMapHint();Data?.Dispose();Data=null;}
}

internal sealed class TravelRoutine
{
    private readonly GameInteractions game;private readonly WorldNavigator navigator;private readonly int hp,minimumPercent;
    private readonly Action<string> report;private readonly Func<int,CancellationToken,Task> wait;
    internal TravelRoutine(GameInteractions game,WorldNavigator navigator,int hp,Action<string>? report=null,int minimumPercent=70,
        Func<int,CancellationToken,Task>? wait=null)
    {this.game=game;this.navigator=navigator;this.hp=hp;this.report=report??(_=>{});this.minimumPercent=minimumPercent;this.wait=wait??Task.Delay;}
    internal async Task<RoutineResult> Run(string target,CancellationToken ct)
    {
        await navigator.Ready(report,ct);await game.Driver.ObserveAsync(ct);
        if(minimumPercent>1)
        {var healing=await SurvivalRoutine.Heal(game,hp,minimumPercent,ct);if(healing is not null)return healing;}
        Health();
        // The inventory panel may have just closed, and native HUD/minimap
        // recognition can take another frame to settle. Re-observe passively
        // up to twice; never click, guess a map tile or relax SIFT consensus.
        var firstCheck=await ObserveLocation(0,ct);
        if(firstCheck.Location is null)
        {
            report($"NAVIGATION_LOCALIZATION_{firstCheck.Status} features={firstCheck.Features} "+
                $"candidates={firstCheck.Candidates} inliers={firstCheck.BestInliers} "+
                $"best_ratio_matches={firstCheck.BestRatioMatches} phase=initial no_input=true");
            return new(RoutineStatus.Blocked,"AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT");
        }
        var first=firstCheck.Location;
        var secondCheck=await ObserveLocation(650,ct);
        var second=secondCheck.Location;
        if(second is null||second.ObservedUtc<=first.ObservedUtc||second.Tile!=first.Tile)
        {
            report($"NAVIGATION_LOCALIZATION_{(second is null?secondCheck.Status:"POSITION_CHANGED_OR_FRAME_NOT_FRESH")} "+
                $"features={secondCheck.Features} candidates={secondCheck.Candidates} "+
                $"inliers={secondCheck.BestInliers} best_ratio_matches={secondCheck.BestRatioMatches} "+
                "phase=stationary_confirmation no_input=true");
            return new(RoutineStatus.Blocked,"AUTOMATIC_POSITION_NEEDS_TWO_STATIONARY_FRAMES");
        }
        string service=NavigationDestinations.Service(target)??target;
        var goals=NavigationDestinations.All.Where(d=>d.Id==target||d.Service==service).OrderBy(d=>Math.Abs(d.Tile.X-second.Tile.X)+Math.Abs(d.Tile.Y-second.Tile.Y)).ToArray();
        if(goals.Length==0)return new(RoutineStatus.Blocked,"UNKNOWN_TRAVEL_DESTINATION");
        PathStep[]? path=null;NavigationDestination? goal=null;
        foreach(var candidate in goals)
        {
            var route=navigator.Data!.FindPath(second.Tile,candidate.Tile,candidate.Radius,ct);
            if(route is not null&&(path is null||route.Length<path.Length)){path=route;goal=candidate;}
        }
        if(path is null||goal is null)return new(RoutineStatus.Blocked,"NO_WALKABLE_F2P_PATH_FOUND");
        report($"PATH_{second.Tile.X}_{second.Tile.Y}_{second.Tile.Plane}_TO_{goal.Id}_TILES_{path.Length}");
        if(path.Length==0)return new(RoutineStatus.Completed,"TRAVEL_DESTINATION_VERIFIED");
        if(path[0].Transport is MapTransport transport)
        {
            var stairs=await game.FindObject(transport.Action,transport.Object,ct);
            await game.Choose(stairs.Bounds,stairs.Action+" "+stairs.Name,ct);
            return await Confirm(second,transport.To,goal,ct);
        }
        PathStep? hop=null;Point? click=null;var previous=second.Tile;
        foreach(var step in path.Take(12))
        {
            if(step.Transport is not null)break;
            var projected=second.ClickFor(step.Tile);if(projected is null)break;
            hop=step;click=projected;
            // Do not skip over a door/gate or around a collision-map corner.
            if(navigator.Data!.Boundary(previous,step.Tile))break;
            previous=step.Tile;
        }
        if(hop is null||click is null)return new(RoutineStatus.Blocked,"NEXT_PATH_TILE_NOT_INSIDE_VISIBLE_MINIMAP");
        await game.Click(new(click.Value.X-1,click.Value.Y-1,3,3),ct);
        return await Confirm(second,hop.Tile,goal,ct);
    }
    private async Task<MapLocalization> ObserveLocation(int initialDelayMs,CancellationToken ct)
    {
        if(initialDelayMs>0){await wait(initialDelayMs,ct);await game.Driver.ObserveAsync(ct);Health();}
        var observation=navigator.LocateDetailed(game.Driver.Current);
        for(int retry=0;retry<2&&observation.Location is null;retry++)
        {
            var previous=game.Driver.Current.CapturedUtc;
            await wait(450,ct);await game.Driver.ObserveAsync(ct);Health();
            if(game.Driver.Current.CapturedUtc<=previous)continue;
            observation=navigator.LocateDetailed(game.Driver.Current);
        }
        return observation;
    }
    private async Task<RoutineResult> Confirm(MapLocation before,WorldTile hop,NavigationDestination goal,CancellationToken ct)
    {
        MapLocation? arrived=null,last=null;
        for(int poll=0;poll<18;poll++)
        {
            await wait(600,ct);await game.Driver.ObserveAsync(ct);Health();last=navigator.Locate(game.Driver.Current);
            if(last is null){arrived=null;continue;}
            if(last.ObservedUtc<=before.ObservedUtc||!Arrived(before.Tile,hop,last.Tile)){arrived=null;continue;}
            if(arrived is not null&&last.Tile==arrived.Tile&&last.ObservedUtc-arrived.ObservedUtc>=TimeSpan.FromMilliseconds(600))
                return last.Tile.Distance(goal.Tile)<=goal.Radius?new(RoutineStatus.Completed,"TRAVEL_DESTINATION_VERIFIED"):
                    new(RoutineStatus.Progress,"AUTOMATIC_WALK_STEP_VERIFIED",last.Tile.Distance(before.Tile));
            if(arrived?.Tile!=last.Tile)arrived=last;
        }
        if(last is null)return new(RoutineStatus.UnknownOutcome,"TRAVEL_POSITION_LOST_STOP_NO_BLIND_RETRY");
        if(last.Tile.Distance(before.Tile)>1)return new(RoutineStatus.Progress,"MOVED_REPLAN_FROM_OBSERVED_POSITION");
        int attempt=navigator.Stall(before.Tile,hop);
        if(attempt>2)return new(RoutineStatus.UnknownOutcome,"TRAVEL_OBSTRUCTION_UNRESOLVED_AFTER_TWO_ATTEMPTS");
        foreach(string name in new[]{"Gate","Door"})
        {
            try
            {
                var door=await game.FindObject("Open",name,ct);await game.Choose(door.Bounds,"Open "+name,ct);
                return new(RoutineStatus.Progress,"OBSTRUCTION_ACTION_SENT_RELOCALISE_BEFORE_NEXT_WALK");
            }
            catch(RoutineBlockedException ex) when(ex.Message=="REQUIRED_OBJECT_NOT_IDENTIFIED_IN_VISIBLE_SCENE"){}
        }
        return new(RoutineStatus.Blocked,"NO_VISIBLE_OPENABLE_OBSTRUCTION_PATH_REMAINS_PENDING");
    }
    internal static bool Arrived(WorldTile start,WorldTile hop,WorldTile current)=>current!=start&&
        (start.Distance(hop)<=2?current==hop:current.Distance(hop)<=1&&current.Distance(start)>1);
    private void Health()
    {
        if(CombatPolicy.Danger(game.Driver.Current.Labels))throw new RoutineBlockedException("TRAVEL_DANGEROUS_SCREEN_STOPPED");
        if(!CombatPolicy.Healthy(CombatPolicy.Hitpoints(game.Driver.Current.Pixels),hp,minimumPercent))
            throw new RoutineBlockedException("TRAVEL_HEALTH_UNREADABLE_OR_BELOW_THRESHOLD");
    }
}
