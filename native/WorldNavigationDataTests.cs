using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal static class WorldNavigationDataTests
{
    internal static async Task<int> Run()
    {
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var data=await WorldMapData.Load(Console.WriteLine,timeout.Token);
            using var navigator=new WorldNavigator(data);
            using(var readOnlyRoutes=await WorldMapData.LoadRoutingOnly(
                Console.WriteLine,timeout.Token))
            {
                var first=readOnlyRoutes.FindPath(new(3222,3218),
                    new(3164,3487),3,timeout.Token);
                if(readOnlyRoutes.Regions.Count<30||
                   readOnlyRoutes.References.Count!=0||
                   readOnlyRoutes.MinimapReferences.Count!=0||
                   first is null||first.Length==0)
                    throw new Exception("RuneLite companion must load usable routing without image references");
                Console.WriteLine($"RUNELITE ROUTING ONLY PASS: {first.Length} steps, no image downloads");
            }
            foreach(var pair in new[]{(new WorldTile(3222,3218),new WorldTile(3164,3487)),
                (new WorldTile(3092,3243),new WorldTile(3269,3167)),(new WorldTile(3185,3436),new WorldTile(3012,3356)),
                (new WorldTile(3209,3220,2),new WorldTile(3092,3243))})
            {
                var path=data.FindPath(pair.Item1,pair.Item2,3,timeout.Token);
                if(path is null||path.Length==0)throw new Exception("Public collision map has no expected path: "+pair);
                Console.WriteLine($"PUBLIC PATH PASS: {pair.Item1} -> {pair.Item2}, {path.Length} steps, {path.Count(s=>s.Transport is not null)} transitions");
            }
            var reference=data.MinimapReferences.Single(r=>r.Plane==0);
            if(ReferenceEquals(reference,data.References.Single(r=>r.Plane==0)))
                throw new Exception("Minimap and world-map references must remain independent");
            try{using var invalid=WorldMapData.DecodeMinimapReference(new byte[]{1,2,3},0);
                throw new Exception("Corrupt native minimap asset accepted");}
            catch(RoutineBlockedException ex) when(ex.Message=="MINIMAP_REFERENCE_HASH_MISMATCH"){}
            var tile=new WorldTile(3222,3218);double px=(tile.X-WorldMapData.MinX)*4+2,py=(WorldMapData.MaxY-tile.Y)*4-2;
            // A zoomed-out atlas crop exercises the separate masked terrain
            // comparator. Even an exact synthetic hit is NEVER handed to the
            // route executor; a candidate alone has no movement authority.
            using(var affine=new Cv.Mat(2,3,Cv.MatType.CV_64FC1))
            {
                const double zoomOutScale=1.95;
                affine.Set(0,0,zoomOutScale);affine.Set(0,1,0d);affine.Set(0,2,px-zoomOutScale*75);
                affine.Set(1,0,0d);affine.Set(1,1,zoomOutScale);affine.Set(1,2,py-zoomOutScale*75);
                using var thumbnail=new Cv.Mat();
                Cv.Cv2.WarpAffine(reference.Image,thumbnail,affine,new(150,150),
                    Cv.InterpolationFlags.Linear|Cv.InterpolationFlags.WarpInverseMap);
                Cv.Cv2.Circle(thumbnail,new(75,75),4,Cv.Scalar.White,-1);
                var pixels=new NavigationPixels(thumbnail.ToBytes(".png"),new(830,10,150,150),24);
                var terrain=MinimapTerrainProbe.Examine(pixels,data.MinimapReferences);
                if(terrain.Status!="TERRAIN_COMPARISON_ONLY"||terrain.UnverifiedCandidate is null||
                    terrain.UnverifiedCandidate.Distance(tile)>2||terrain.BestScore<.45||
                    MinimapTerrainProbe.Examine(null,data.MinimapReferences).UnverifiedCandidate is not null)
                    throw new Exception("Zoom-out terrain diagnostic cannot find its synthetic atlas texture without authorizing position: "+terrain);
                var observedAt=DateTimeOffset.UtcNow;
                var surface=new WorldMapPosition(tile,20,observedAt.AddSeconds(-5));
                var stable=MinimapTerrainProbe.CompareReadOnly(terrain,observedAt,
                    terrain,observedAt.AddMilliseconds(700));
                var tooSoon=MinimapTerrainProbe.CompareReadOnly(terrain,observedAt,
                    terrain,observedAt.AddMilliseconds(100));
                var shifted=MinimapTerrainProbe.CompareReadOnly(terrain,observedAt,
                    terrain with{UnverifiedCandidate=terrain.UnverifiedCandidate with{X=terrain.UnverifiedCandidate.X+6}},
                    observedAt.AddMilliseconds(700));
                var changedFloor=MinimapTerrainProbe.CompareReadOnly(terrain,observedAt,
                    terrain with{UnverifiedCandidate=terrain.UnverifiedCandidate with{Plane=1}},
                    observedAt.AddMilliseconds(700));
                if(MinimapTerrainProbe.SurfaceHintDistance(terrain,surface) is not int hintDifference||hintDifference>2||
                    stable.Status!="REPEATED_TERRAIN_COMPARISON_ONLY"||stable.CandidateShiftTiles!=0||
                    stable.SameUnverifiedPlane!=true||!stable.IndependentFrame||
                    tooSoon.Status!="SECOND_FRAME_NOT_INDEPENDENT"||
                    shifted.CandidateShiftTiles!=6||changedFloor.SameUnverifiedPlane!=false||
                    MinimapTerrainProbe.SurfaceHintDistance(terrain,null) is not null||
                    !GameplayCoordinator.TerrainDiagnosticEvidence(1,terrain,surface)
                        .Contains("candidate_unverified=true no_input=true navigation_authorized=false"))
                    throw new Exception("Read-only two-frame and independent surface-hint diagnostics must not authorize input");
                var preview=MinimapTerrainProbe.PreviewCardinalStepsReadOnly(terrain,observedAt,
                    terrain,observedAt.AddMilliseconds(700),surface);
                if(preview.Status!="PROJECTION_STABLE_DIAGNOSTIC_ONLY"||
                    preview.EastDx is not (>=12 and <=21)||Math.Abs(preview.EastDy??100)>5||
                    preview.NorthDy is not (>=-21 and <=-12)||Math.Abs(preview.NorthDx??100)>5||
                    preview.MaxFrameDriftPx!=0)
                    throw new Exception("Zoom-out minimap geometry projected the wrong compass direction or tile distance: "+preview);
                if(MinimapTerrainProbe.PreviewCardinalStepsReadOnly(terrain,observedAt,
                    terrain,observedAt.AddMilliseconds(100),surface).Status!="PROJECTION_INDEPENDENT_FRAMES_REQUIRED"||
                    MinimapTerrainProbe.PreviewCardinalStepsReadOnly(terrain,observedAt,
                    terrain,observedAt.AddMilliseconds(700),null).Status!="PROJECTION_WORLD_MAP_HINT_NOT_CORROBORATED"||
                    MinimapTerrainProbe.PreviewCardinalStepsReadOnly(terrain,observedAt,
                    terrain with{UnverifiedCandidate=terrain.UnverifiedCandidate with{Plane=1}},
                    observedAt.AddMilliseconds(700),surface).Status!="PROJECTION_CANDIDATES_DIVERGED"||
                    MinimapTerrainProbe.PreviewCardinalStepsReadOnly(terrain,observedAt,
                    terrain with{RunnerUpScore=terrain.BestScore-.04},
                    observedAt.AddMilliseconds(700),surface).Status!="PROJECTION_PEAK_NOT_DISTINCT")
                    throw new Exception("Projection diagnostic must reject stale, missing, divergent or ambiguous evidence");
                // Native minimap bounds need not be 150x150. Verify that the
                // normalized template centre does not silently become a HUD
                // click offset when the detected minimap crops to 142px.
                using(var smaller=new Cv.Mat())
                {
                    Cv.Cv2.Resize(thumbnail,smaller,new(142,142));
                    var smallPixels=new NavigationPixels(smaller.ToBytes(".png"),new(834,14,142,142),24);
                    var smallTerrain=MinimapTerrainProbe.Examine(smallPixels,data.MinimapReferences);
                    var smallPreview=MinimapTerrainProbe.PreviewCardinalStepsReadOnly(smallTerrain,observedAt,
                        smallTerrain,observedAt.AddMilliseconds(700),surface);
                    if(smallPreview.Status!="PROJECTION_STABLE_DIAGNOSTIC_ONLY"||
                        smallPreview.EastDx is not (>=11 and <=21)||
                        smallPreview.NorthDy is not (>=-21 and <=-11)||
                        MinimapTerrainProbe.PreviewCardinalStepsReadOnly(terrain,observedAt,
                            smallTerrain,observedAt.AddMilliseconds(700),surface).Status!="PROJECTION_GEOMETRY_CHANGED")
                        throw new Exception("Resized native HUD bounds break the read-only atlas-to-screen transform: "+smallPreview);
                }
                Console.WriteLine($"TERRAIN PROJECTION PASS: east_dx={preview.EastDx} north_dy={preview.NorthDy} "+
                    "independent_hint_required=true navigation_authorized=false");
                using(var emptyTerrain=new Cv.Mat(150,150,Cv.MatType.CV_8UC1,Cv.Scalar.Black))
                {
                    var empty=MinimapTerrainProbe.Examine(pixels with{MinimapPng=emptyTerrain.ToBytes(".png")},data.MinimapReferences);
                    if(empty.Status!="MINIMAP_TERRAIN_TOO_UNIFORM"||empty.UnverifiedCandidate is not null)
                        throw new Exception("Flat minimap must not yield a terrain candidate");
                }
                Console.WriteLine($"TERRAIN PROBE PASS: masked global map scan best={terrain.BestScore:F3} "+
                    $"runner_up={terrain.RunnerUpScore:F3} movement_authorized=false");
            }
            foreach(var variant in new[]{(Angle:0d,Scale:1d),(Angle:37d,Scale:1.2d),(Angle:-60d,Scale:.85d)})
            {
                double angle=variant.Angle*Math.PI/180,c=Math.Cos(angle)*variant.Scale,s=Math.Sin(angle)*variant.Scale;
                using var affine=new Cv.Mat(2,3,Cv.MatType.CV_64FC1);
                affine.Set(0,0,c);affine.Set(0,1,-s);affine.Set(0,2,px-c*75+s*75);
                affine.Set(1,0,s);affine.Set(1,1,c);affine.Set(1,2,py-s*75-c*75);
                using var minimap=new Cv.Mat();Cv.Cv2.WarpAffine(reference.Image,minimap,affine,new(150,150),Cv.InterpolationFlags.Linear|Cv.InterpolationFlags.WarpInverseMap);
                // Simulate moving NPC/player dots and the central marker.
                Cv.Cv2.Circle(minimap,new(75,75),2,Cv.Scalar.White,-1);Cv.Cv2.Circle(minimap,new(98,52),2,Cv.Scalar.White,-1);
                var frame=new VisualFrame(new(1),new(0,0,1000,600),DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),
                    new(Array.Empty<ObservedSlot>(),0,new(minimap.ToBytes(".png"),new(830,10,150,150),10)));
                var found=navigator.Locate(frame);
                if(found is null||found.Tile.Distance(tile)>1)throw new Exception("Automatic map localisation failed for rotation/zoom: "+variant+" found="+found?.Tile);
                if(navigator.LocateDetailed(frame).Status!="VERIFIED")
                    throw new Exception("Navigation diagnostics must distinguish an accepted map match");
                if(navigator.LocateDetailed(frame).BestRatioMatches<8)
                    throw new Exception("Confirmed matches must also report the initial feature-pair evidence");
                Console.WriteLine($"REFERENCE MATCH PASS: rotation={variant.Angle} scale={variant.Scale} world={found.Tile} inliers={found.Inliers}");
                var sampled=navigator.LocateAtSampling(frame,2);
                var enlarged=sampled.Location;
                if((variant.Angle==0&&enlarged is null)||enlarged is not null&&
                    (enlarged.Tile.Distance(tile)>1||enlarged.MapBounds!=found.MapBounds||
                    enlarged.ClickFor(new(tile.X+4,tile.Y)) is not Point enlargedClick||
                    found.ClickFor(new(tile.X+4,tile.Y)) is not Point nativeClick||
                    Math.Abs(enlargedClick.X-nativeClick.X)>2||Math.Abs(enlargedClick.Y-nativeClick.Y)>2))
                    throw new Exception("Upsampled feature extraction changed world/click coordinates or failed the native edge check.");
                Console.WriteLine($"UPSAMPLED FEATURE CHECK: rotation={variant.Angle} status={sampled.Status} native_success_preserved=true");
                if(variant.Angle==0)
                {
                    using var memory=new MemoryStream(minimap.ToBytes(".png"));using var patch=new Bitmap(memory);
                    using var fixture=new Bitmap(1000,600);using(var graphics=Graphics.FromImage(fixture))
                    {
                        graphics.Clear(Color.FromArgb(35,35,35));
                        using var clip=new System.Drawing.Drawing2D.GraphicsPath();clip.AddEllipse(830,10,150,150);
                        graphics.SetClip(clip);graphics.DrawImage(patch,830,10,150,150);graphics.ResetClip();
                        using var border=new Pen(Color.Goldenrod,4);graphics.DrawEllipse(border,829,9,152,152);
                    }
                    var map=HudVision.Minimap(fixture);
                    if(map is null||Math.Abs(map.Value.Left-830)>5||Math.Abs(map.Value.Top-10)>5)
                        throw new Exception("Automatic circular HUD detection failed: "+map);
                    var hud=new VisualLabel("NATIVE_GAME_HUD",new Rectangle(0,575,517,25),VisualLabelKind.GameHud);
                    if(ExecutionVision.Read(fixture,new[]{hud})?.Navigation?.MapBounds!=map||
                        ExecutionVision.Read(fixture,Array.Empty<VisualLabel>())?.Navigation is not null)
                        throw new Exception("A missing minimap OCR label is restored only with native HUD plus a unique pixel-verified circle");
                    Console.WriteLine("AUTOMATIC HUD PASS: minimap circle plus central player marker; no manual calibration");
                    var driver=new StationaryDriver(frame);
                    var result=await new TravelRoutine(new(driver),navigator,10,wait:(_,_)=>Task.CompletedTask).Run("Grand Exchange",timeout.Token);
                    if(result.Status==RoutineStatus.Completed||driver.Walks!=1)throw new Exception("Unmoved character was reported as arrived or clicked repeatedly");
                    Console.WriteLine("UNMOVED CHARACTER PASS: one waypoint click; no arrival receipt and no blind click loop");
                    var lost=frame with{Pixels=frame.Pixels! with{Navigation=null,Hitpoints=10}};
                    var recovering=new RecoveringDriver(lost,frame,restoreAfter:2);
                    var passive=await new TravelRoutine(new(recovering),navigator,10,minimumPercent:1,
                        wait:(_,_)=>Task.CompletedTask).Run("Grand Exchange",timeout.Token);
                    if(passive.Status==RoutineStatus.Completed||recovering.Walks!=1||recovering.Observations<3)
                        throw new Exception("Transient missing minimap must be re-observed before one verified route click");
                    var missing=new RecoveringDriver(lost,frame,restoreAfter:int.MaxValue);
                    var reports=new List<string>();
                    var blocked=await new TravelRoutine(new(missing),navigator,10,reports.Add,minimumPercent:1,
                        wait:(_,_)=>Task.CompletedTask).Run("Grand Exchange",timeout.Token);
                    if(blocked.Reason!="AUTOMATIC_WORLD_POSITION_NOT_CONFIDENT"||missing.Walks!=0||
                        !reports.Any(s=>s.Contains("MINIMAP_NOT_IDENTIFIED",StringComparison.Ordinal)))
                        throw new Exception("Persistently absent minimap must block without any click and report the actual reason");
                }
            }
            using var blank=new Cv.Mat(150,150,Cv.MatType.CV_8UC1,Cv.Scalar.Black);
            using(var large=new Cv.Mat(data.References.Single(r=>r.Plane==0).Image,new Cv.Rect((int)px-170,(int)py-210,WorldMapVision.View.Width,WorldMapVision.View.Height)))
            {
                var view=WorldMapVision.View;
                var map=new WorldMapPixels(large.ToBytes(".png"),view,new(view.Left+170,view.Top+210),WorldMapVision.Close);
                var surfaceFrame=new VisualFrame(new IntPtr(1),new(0,0,1000,600),DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),
                    new(Array.Empty<ObservedSlot>(),0,WorldMap:map,WorldMapPanelOpen:true));
                var fix=WorldMapLocalizer.Locate(data,surfaceFrame);
                if(fix.Position is null||fix.Position.SurfaceTile.Distance(tile)>1)
                    throw new Exception("Large-map localization must use the off-centre player marker instead of the map centre: "+fix);
                navigator.SetWorldMapHint(fix.Position);
                using var small=new Cv.Mat(reference.Image,new Cv.Rect((int)px-75,(int)py-75,150,150));
                var minimapFrame=surfaceFrame with{Pixels=new(Array.Empty<ObservedSlot>(),0,
                    new(small.ToBytes(".png"),new(830,10,150,150),10))};
                var global=navigator.LocateAtSampling(minimapFrame,1).Location;
                var local=navigator.LocateAtSampling(minimapFrame,1,local:true).Location;
                if(global is null||local is null||local.Tile!=global.Tile||
                    local.ClickFor(new(tile.X+4,tile.Y))!=global.ClickFor(new(tile.X+4,tile.Y)))
                    throw new Exception("Regional references must preserve absolute world coordinates and native minimap clicks");
                using(var regionalData=new WorldMapData())
                using(var regional=new WorldNavigator(regionalData))
                {
                    var area=new Cv.Rect((int)px-256,(int)py-256,512,512);
                    foreach(var r in data.MinimapReferences)
                    {using var roi=new Cv.Mat(r.Image,area);regionalData.MinimapReferences.Add(new(r.Plane,roi.Clone(),area.X,area.Y));}
                    regional.SetWorldMapHint(fix.Position);
                    var shifted=regional.LocateAtSampling(minimapFrame,1,local:true).Location;
                    if(shifted is null||shifted.Tile!=global.Tile||shifted.ClickFor(new(tile.X+4,tile.Y))!=global.ClickFor(new(tile.X+4,tile.Y)))
                        throw new Exception("A regional atlas with nonzero origin must preserve world and click coordinates after hint cropping");
                    regional.SetWorldMapHint(fix.Position with{ObservedUtc=DateTimeOffset.UtcNow.AddMinutes(-6)});
                    if(regional.LocateAtSampling(minimapFrame,1,local:true).Status!="WORLD_MAP_HINT_EXPIRED")
                        throw new Exception("An expired world-map hint must not reuse regional descriptors");
                    regionalData.MinimapReferences.Single(r=>r.Plane==3).Dispose();
                    regionalData.MinimapReferences.RemoveAll(r=>r.Plane==3);
                    if(regional.LocateDetailed(minimapFrame).Status!="MINIMAP_REFERENCES_INCOMPLETE")
                        throw new Exception("Missing floor references cannot silently select a remaining floor");
                }
                navigator.ClearWorldMapHint();
                Console.WriteLine($"WORLD MAP REFERENCE PASS: off-centre marker, surface={fix.Position.SurfaceTile}, inliers={fix.Inliers}, regional minimap coordinates preserved");
            }
            var unknown=new VisualFrame(new(1),new(0,0,1000,600),DateTimeOffset.UtcNow,Array.Empty<VisualLabel>(),
                new(Array.Empty<ObservedSlot>(),0,new(blank.ToBytes(".png"),new(830,10,150,150),10)));
            if(navigator.Locate(unknown) is not null)throw new Exception("Black minimap produced a false position");
            if(navigator.LocateAtSampling(unknown,2).Location is not null)
                throw new Exception("Upsampling a blank minimap cannot create a world position");
            if(navigator.LocateDetailed(unknown).Status!="MINIMAP_FEATURES_INSUFFICIENT")
                throw new Exception("Feature-poor minimap must be diagnosed without invented coordinates");
            var duplicate=new WorldMapData.ReferenceMap(1,reference.Image.Clone());data.MinimapReferences.Add(duplicate);
            using var crop=new Cv.Mat(reference.Image,new Cv.Rect((int)px-75,(int)py-75,150,150));
            var ambiguous=unknown with{Pixels=new(Array.Empty<ObservedSlot>(),0,new(crop.ToBytes(".png"),new(830,10,150,150),10))};
            if(navigator.Locate(ambiguous) is not null)throw new Exception("Ambiguous floors produced a false position");
            if(navigator.LocateDetailed(ambiguous).Status!="MAP_MATCH_AMBIGUOUS")
                throw new Exception("Ambiguous floors must expose an ambiguity reason without a position");
            navigator.SetWorldMapHint(new(tile,20,DateTimeOffset.UtcNow));
            if(navigator.LocateAtSampling(ambiguous,1,local:true).Status!="MAP_MATCH_AMBIGUOUS")
                throw new Exception("A surface map hint cannot choose between visually indistinguishable player floors");
            navigator.ClearWorldMapHint();
            data.MinimapReferences.Remove(duplicate);duplicate.Dispose();
            Console.WriteLine("AUTOMATIC NAVIGATION DATA PASS: pinned public assets, collision routes, upper-floor stairs, rotated/zoomed reference matching, unknown-map rejection. No live game actions tested.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("AUTOMATIC NAVIGATION DATA FAIL: "+ex);return 1;}
    }
    private sealed class RecoveringDriver:IVisualGameDriver
    {
        private readonly VisualFrame missing,restored;
        private readonly int restoreAfter;
        public VisualFrame Current{get;private set;}
        internal int Walks,Observations;
        internal RecoveringDriver(VisualFrame missing,VisualFrame restored,int restoreAfter)
        {this.missing=missing;this.restored=restored;this.restoreAfter=restoreAfter;Current=missing;}
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Observations++;
            Current=(Observations>=restoreAfter?restored:missing) with{
                CapturedUtc=Current.CapturedUtc.AddSeconds(1)};
            return Task.FromResult(Current);
        }
        public Task InputAsync(GameInput input,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();if(input.Kind==GameInputKind.LeftClick)Walks++;return Task.CompletedTask;}
    }
    private sealed class StationaryDriver:IVisualGameDriver
    {
        public VisualFrame Current{get;private set;}internal int Walks;
        internal StationaryDriver(VisualFrame frame){Current=frame;}
        public Task<VisualFrame> ObserveAsync(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Current=Current with{CapturedUtc=Current.CapturedUtc.AddSeconds(1)};return Task.FromResult(Current);}
        public Task InputAsync(GameInput input,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();if(input.Kind==GameInputKind.LeftClick)Walks++;return Task.CompletedTask;}
    }
}
