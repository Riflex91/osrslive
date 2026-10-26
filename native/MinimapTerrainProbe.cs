using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

// A diagnostic-only comparison of visible terrain against static, public
// reference imagery. No client memory access, no image persistence, and no
// authorization to click or to turn an atlas peak into a world position.
internal sealed record MinimapTerrainEvidence(string Status,double BestScore=0,
    double RunnerUpScore=0,int Peaks=0,WorldTile? UnverifiedCandidate=null,
    double BestReferenceScale=0);
internal sealed record MinimapTerrainRepeatEvidence(string Status,int? CandidateShiftTiles=null,
    bool? SameUnverifiedPlane=null,bool IndependentFrame=false);

internal static class MinimapTerrainProbe
{
    // The maximum zoom-out minimap showed about 2 atlas pixels per native
    // screenshot pixel. Scan a small bounded scale family, never infer zoom
    // or a usable navigation transform from the scores.
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

        var peaks=new List<(double Score,WorldTile Tile,double Scale)>();
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
                if(referenceVariation.Val0>=7&&WorldMapData.Domain(tile))peaks.Add((best,tile,scale));
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
            leader.Tile,leader.Scale);
    }
}
