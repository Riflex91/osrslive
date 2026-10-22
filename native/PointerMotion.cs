using System.Drawing;

namespace OSRSLiveDiagnostics;

// Local-only virtual pointer model for visual and performance evaluation.
// This class cannot move the operating-system cursor or interact with a game.
internal sealed record VirtualPoint(float X,float Y,int OffsetMs);
internal sealed record PointerTrajectory(
    IReadOnlyList<VirtualPoint> Points,int DurationMs,double LengthPx,double PeakSpeedPxPerSecond);

internal static class PointerMotion
{
    internal static PointerTrajectory Generate(PointF start,PointF target,int durationMs,int seed,int segments=80)
    {
        if(durationMs<150||durationMs>10000||segments<16||segments>1000)
            throw new ArgumentOutOfRangeException(nameof(durationMs),"Invalid virtual trajectory parameters");
        double dx=target.X-start.X,dy=target.Y-start.Y;
        double straight=Math.Sqrt(dx*dx+dy*dy);
        if(!double.IsFinite(straight)||straight<20||straight>10000)
            throw new ArgumentOutOfRangeException(nameof(target),"Invalid pointer distance");
        var rng=new Random(seed);
        double lateral=(rng.NextDouble()*.45+.12)*Math.Min(28,straight*.11);
        if(rng.Next(2)==0) lateral=-lateral;
        double bend=(rng.NextDouble()-.5)*.22;
        double nx=-dy/straight,ny=dx/straight;
        var points=new List<VirtualPoint>(segments+1);
        double length=0,peakSpeed=0;
        for(int i=0;i<=segments;i++)
        {
            double t=(double)i/segments;
            // Minimum-jerk (smooth at both ends); lateral deviation returns to zero.
            double ease=t*t*t*(10+t*(-15+6*t));
            double wave=Math.Sin(Math.PI*t);
            double arc=lateral*wave*wave*(1+bend*Math.Sin(2*Math.PI*t));
            float x=(float)(start.X+dx*ease+nx*arc);
            float y=(float)(start.Y+dy*ease+ny*arc);
            int time=(int)Math.Round(t*durationMs);
            if(i==0){x=start.X;y=start.Y;time=0;}
            if(i==segments){x=target.X;y=target.Y;time=durationMs;}
            if(i>0)
            {
                var prev=points[^1];
                double step=Math.Sqrt(Math.Pow(x-prev.X,2)+Math.Pow(y-prev.Y,2));
                length+=step;
                double dt=(time-prev.OffsetMs)/1000d;
                if(dt>0)peakSpeed=Math.Max(peakSpeed,step/dt);
            }
            points.Add(new VirtualPoint(x,y,time));
        }
        return new PointerTrajectory(points,durationMs,length,peakSpeed);
    }
}

internal static class PointerMotionTests
{
    internal static void Run()
    {
        var start=new PointF(20,95);
        var target=new PointF(650,95);
        var a=PointerMotion.Generate(start,target,760,1234);
        var b=PointerMotion.Generate(start,target,760,1234);
        if(a.Points.Count!=81||a.Points[0].X!=start.X||
           Math.Abs(a.Points[^1].X-target.X)>.01||a.Points[^1].OffsetMs!=760)
            throw new Exception("Pointer endpoints/timing test failed");
        if(a.LengthPx<630||a.LengthPx>850||
           a.PeakSpeedPxPerSecond<=0||!double.IsFinite(a.PeakSpeedPxPerSecond))
            throw new Exception("Pointer path metrics test failed");
        if(!a.Points.SequenceEqual(b.Points))
            throw new Exception("Pointer trace must be deterministic with same seed");
        if(!a.Points.Skip(1).Zip(a.Points,(next,prev)=>next.OffsetMs>=prev.OffsetMs).All(x=>x))
            throw new Exception("Pointer timestamps nonmonotonic");
        if(a.Points.All(p=>Math.Abs(p.Y-95)<.01))
            throw new Exception("Pointer model lacks lateral curve");
        try {PointerMotion.Generate(start,target,0,1);throw new Exception("Bad duration accepted");}
        catch(ArgumentOutOfRangeException){}
        Console.WriteLine("VIRTUAL POINTER PASS: deterministic, bounded, time-ordered, curved, virtual-only");
    }
}
