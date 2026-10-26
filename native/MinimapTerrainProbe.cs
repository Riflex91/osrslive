using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

// A diagnostic-only comparison of visible terrain against static, public
// reference imagery. No client memory access, no image persistence, and no
// authorization to click or to turn an atlas peak into a world position.
internal sealed record MinimapTerrainEvidence(string Status,double BestScore=0,
    double RunnerUpScore=0,int Peaks=0,WorldTile? UnverifiedCandidate=null,
    double BestReferenceScale=0,MinimapTerrainProjection? UnverifiedProjection=null);
internal sealed record MinimapTerrainRepeatEvidence(string Status,int? CandidateShiftTiles=null,
    bool? SameUnverifiedPlane=null,bool IndependentFrame=false);
// Deliberately separate from the verified SIFT MapLocation. This object is
// available only to a read-only diagnostic; it is never returned by Navigator.
internal sealed record MinimapTerrainProjection(double[] AtlasFromNativePixels,Rectangle MinimapBounds);
internal sealed record MinimapTerrainProjectionPreview(string Status,int? EastDx=null,
    int? EastDy=null,int? NorthDx=null,int? NorthDy=null,int? MaxFrameDriftPx=null);

internal static class MinimapTerrainProbe
{
    // The maximum zoom-out minimap showed about 2 atlas pixels per native
    // screenshot pixel. Scan a bounded family; all projections are hypothetical.
    internal static readonly double[] ReferenceScales={1.80,1.95,2.10};

    // Surface X/Y alone can be compared to a two-frame world-map observation.
    // The map does not verify the player's floor. These results remain
    // diagnostic only, regardless of distance or score.
    internal static int? SurfaceHintDistance(MinimapTerrainEvidence probe,WorldMapPosition? hint)=>
        probe.UnverifiedCandidate is WorldTile p&&hint is not null?
            Math.Max(Math.Abs(p.X-hint.SurfaceTile.X),Math.Abs(p.Y-hint.SurfaceTile.Y)):null;

    internal static MinimapTerrainRepeatEvidence CompareReadOnly(MinimapTerrainEvidence first,
        DateTimeOffset firstCapturedUtc,MinimapTerrainEvidence second,DateTimeOffset secondCapturedUtc)
    {
        if(secondCapturedUtc-firstCapturedUtc<TimeSpan.FromMilliseconds(550))
            return new("SECOND_FRAME_NOT_INDEPENDENT");
        if(first.UnverifiedCandidate is not WorldTile a||second.UnverifiedCandidate is not WorldTile b)
            return new("CANDIDATE_MISSING_IN_AT_LEAST_ONE_FRAME",IndependentFrame:true);
        // Do not use WorldTile.Distance here: it returns int.MaxValue for
        // different planes, which would hide the actual X/Y variation.
        return new("REPEATED_TERRAIN_COMPARISON_ONLY",
            Math.Max(Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y)),a.Plane==b.Plane,true);
    }

    // Preflight a reversible geometric model without granting input authority.
    // The independent large world map is the anchor; a terrain peak by itself
    // cannot establish the player's floor, centre, scale or movement receipt.
    internal static MinimapTerrainProjectionPreview PreviewCardinalStepsReadOnly(
        MinimapTerrainEvidence first,DateTimeOffset firstUtc,
        MinimapTerrainEvidence second,DateTimeOffset secondUtc,WorldMapPosition? surfaceHint)
    {
        var repeat=CompareReadOnly(first,firstUtc,second,secondUtc);
        if(!repeat.IndependentFrame||repeat.CandidateShiftTiles is null)
            return new("PROJECTION_INDEPENDENT_FRAMES_REQUIRED");
        if(repeat.SameUnverifiedPlane!=true||repeat.CandidateShiftTiles>2)
            return new("PROJECTION_CANDIDATES_DIVERGED");
        if(surfaceHint is null||SurfaceHintDistance(first,surfaceHint) is not int distance1||
            SurfaceHintDistance(second,surfaceHint) is not int distance2||distance1>2||distance2>2)
            return new("PROJECTION_WORLD_MAP_HINT_NOT_CORROBORATED");
        if(first.UnverifiedCandidate is not {Plane:0} a||second.UnverifiedCandidate is not {Plane:0} b||
            first.UnverifiedProjection is not { } p||second.UnverifiedProjection is not { } q)
            return new("PROJECTION_SURFACE_OR_TRANSFORM_MISSING");
        if(first.Status!="TERRAIN_COMPARISON_ONLY"||second.Status!="TERRAIN_COMPARISON_ONLY"||
            first.BestScore<.40||second.BestScore<.40||
            first.BestScore-first.RunnerUpScore<.15||second.BestScore-second.RunnerUpScore<.15)
            return new("PROJECTION_PEAK_NOT_DISTINCT");
        if(Math.Abs(first.BestReferenceScale-second.BestReferenceScale)>.001||
            p.MinimapBounds!=q.MinimapBounds||p.AtlasFromNativePixels.Length!=6||
            q.AtlasFromNativePixels.Length!=6)
            return new("PROJECTION_GEOMETRY_CHANGED");

        var anchor=surfaceHint.SurfaceTile;
        var steps=new[]{anchor with{X=anchor.X+8},anchor with{X=anchor.X-8},
            anchor with{Y=anchor.Y+8},anchor with{Y=anchor.Y-8}};
        // MapLocation's inverse projection is shared with the existing route
        // code, but zero inliers and a local variable never reach that code.
        var firstMap=new MapLocation(a,p.AtlasFromNativePixels,p.MinimapBounds,0,firstUtc);
        var secondMap=new MapLocation(b,q.AtlasFromNativePixels,q.MinimapBounds,0,secondUtc);
        var firstClicks=steps.Select(firstMap.ClickFor).ToArray();
        var secondClicks=steps.Select(secondMap.ClickFor).ToArray();
        if(firstClicks.Any(c=>c is null)||secondClicks.Any(c=>c is null))
            return new("PROJECTION_OUTSIDE_SAFE_MINIMAP_RADIUS");
        var center=new Point(p.MinimapBounds.Left+p.MinimapBounds.Width/2,
            p.MinimapBounds.Top+p.MinimapBounds.Height/2);
        int eastDx=secondClicks[0]!.Value.X-center.X,eastDy=secondClicks[0]!.Value.Y-center.Y;
        int northDx=secondClicks[2]!.Value.X-center.X,northDy=secondClicks[2]!.Value.Y-center.Y;
        int maxDrift=Enumerable.Range(0,steps.Length).Max(i=>Math.Max(
            Math.Abs(firstClicks[i]!.Value.X-secondClicks[i]!.Value.X),
            Math.Abs(firstClicks[i]!.Value.Y-secondClicks[i]!.Value.Y)));
        // Both signs and the approximate cardinal axes must agree with the
        // separately pixel-verified north-facing compass.
        bool cardinal=eastDx is >=8 and <=30&&Math.Abs(eastDy)<=7&&
            northDy is >=-30 and <=-8&&Math.Abs(northDx)<=7&&
            secondClicks[1]!.Value.X<center.X-8&&
            secondClicks[3]!.Value.Y>center.Y+8;
        string status=!cardinal?"PROJECTION_AXES_NOT_NORTH_ALIGNED":
            maxDrift>3?"PROJECTION_FRAME_DRIFT_DIAGNOSTIC_ONLY":
            "PROJECTION_STABLE_DIAGNOSTIC_ONLY";
        return new(status,eastDx,eastDy,northDx,northDy,maxDrift);
    }

    internal static MinimapTerrainEvidence Examine(NavigationPixels? pixels,
        IReadOnlyList<WorldMapData.ReferenceMap> references)
    {
        if(pixels is null||pixels.MinimapPng.Length==0)return new("MINIMAP_MISSING");
        if(references.Count!=4||Enumerable.Range(0,4).Any(p=>references.Count(r=>r.Plane==p)!=1))
            return new("REFERENCE_PLANES_INCOMPLETE");
        using var native=Cv.Cv2.ImDecode(pixels.MinimapPng,Cv.ImreadModes.Grayscale);
        if(native.Empty()||native.Width!=native.Height||native.Width is <124 or >170||
            native.Width!=pixels.MapBounds.Width||native.Height!=pixels.MapBounds.Height)
            return new("MINIMAP_BOUNDS_INVALID");
        using var mini=new Cv.Mat();
        Cv.Cv2.Resize(native,mini,new(150,150),interpolation:Cv.InterpolationFlags.Linear);
        using var mask=new Cv.Mat(mini.Size(),Cv.MatType.CV_8UC1,Cv.Scalar.Black);
        Cv.Cv2.Circle(mask,new(75,75),60,Cv.Scalar.White,-1);
        Cv.Cv2.Circle(mask,new(75,75),17,Cv.Scalar.Black,-1);
        Cv.Cv2.MeanStdDev(mini,out _,out var variation,mask);
        if(variation.Val0<7)return new("MINIMAP_TERRAIN_TOO_UNIFORM");

        var peaks=new List<(double Score,WorldTile Tile,double Scale,MinimapTerrainProjection Projection)>();
        foreach(var reference in references)
        foreach(double scale in ReferenceScales)
        {
            using var resized=new Cv.Mat();
            Cv.Cv2.Resize(reference.Image,resized,
                new((int)Math.Round(reference.Image.Width/scale),(int)Math.Round(reference.Image.Height/scale)),
                interpolation:Cv.InterpolationFlags.Linear);
            if(resized.Width<mini.Width||resized.Height<mini.Height)continue;
            using var scores=new Cv.Mat();
            Cv.Cv2.MatchTemplate(resized,mini,scores,Cv.TemplateMatchModes.CCoeffNormed,mask);
            // Uniform atlas regions can produce non-finite normalized
            // coefficients. They are invalid evidence, not perfect matches.
            using(var valid=new Cv.Mat())using(var invalid=new Cv.Mat())
            {
                Cv.Cv2.InRange(scores,new Cv.Scalar(-1),new Cv.Scalar(1),valid);
                Cv.Cv2.BitwiseNot(valid,invalid);
                scores.SetTo(new Cv.Scalar(-1),invalid);
            }
            // Inspect multiple *spatially separated* maxima. The runner-up
            // must not be the adjacent pixel of the first terrain peak.
            for(int n=0;n<6;n++)
            {
                Cv.Cv2.MinMaxLoc(scores,out _,out double best,out _,out var at);
                if(!double.IsFinite(best)||best<=0)break;
                using var candidatePatch=new Cv.Mat(resized,new Cv.Rect(at.X,at.Y,mini.Width,mini.Height));
                Cv.Cv2.MeanStdDev(candidatePatch,out _,out var referenceVariation,mask);
                double atlasX=(at.X+75.5)*reference.Image.Width/resized.Width-.5+reference.OriginX;
                double atlasY=(at.Y+75.5)*reference.Image.Height/resized.Height-.5+reference.OriginY;
                var tile=new WorldTile(WorldMapData.MinX+(int)Math.Floor(atlasX/4),
                    WorldMapData.MaxY-1-(int)Math.Floor(atlasY/4),reference.Plane);
                if(referenceVariation.Val0>=7&&WorldMapData.Domain(tile))
                {
                    // OpenCV resize uses pixel-centre coordinates. Convert
                    // from the *native* HUD crop (which can be 124..170px),
                    // not from the normalized 150px comparison thumbnail.
                    double factorX=reference.Image.Width/(double)resized.Width;
                    double factorY=reference.Image.Height/(double)resized.Height;
                    double nativeToMini=150d/native.Width;
                    var transform=new[]{factorX*nativeToMini,0d,
                        reference.OriginX+(at.X+nativeToMini*.5)*factorX-.5,
                        0d,factorY*nativeToMini,
                        reference.OriginY+(at.Y+nativeToMini*.5)*factorY-.5};
                    peaks.Add((best,tile,scale,
                        new MinimapTerrainProjection(transform,pixels.MapBounds)));
                }
                Cv.Cv2.Circle(scores,at,24,new Cv.Scalar(-1),-1);
            }
        }
        if(peaks.Count==0)return new("NO_TERRAIN_CANDIDATES");
        var ranked=peaks.OrderByDescending(p=>p.Score).ToArray();
        var leader=ranked[0];
        double second=ranked.Where(p=>p.Tile.Plane!=leader.Tile.Plane||
            p.Tile.Distance(leader.Tile)>12).Select(p=>p.Score).DefaultIfEmpty(0).Max();
        // Even a visually distinctive leader remains *unverified* here.
        // SIFT's independent path and its existing movement gates are untouched.
        return new("TERRAIN_COMPARISON_ONLY",leader.Score,second,ranked.Length,
            leader.Tile,leader.Scale,leader.Projection);
    }
}
