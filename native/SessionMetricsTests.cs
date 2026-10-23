using System;
using System.Threading;

namespace OSRSLiveDiagnostics;

internal static class SessionMetricsTests
{
    private static void Ensure(bool valid,string message)
    {if(!valid)throw new Exception("Session metrics: "+message);}
    internal static void Run()
    {
        var log=new Diagnostics();
        log.Add(Severity.INFO,"SIMULATOR","START","session=1");
        log.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK","session=1 tick=1");
        Thread.Sleep(8);
        log.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK","session=1 tick=2");
        log.Add(Severity.INFO,"SIMULATOR","STOP","session=1");
        Thread.Sleep(85); // intentional pause must not count toward TickStats
        log.Add(Severity.INFO,"SIMULATOR","START","session=2");
        log.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK","session=2 tick=1");
        Thread.Sleep(9);
        log.Add(Severity.DEBUG,"SIMULATOR","GAME_TICK","session=2 tick=2");
        var interval=log.TickStats();
        Ensure(interval.Count==2,"two independent session intervals");
        Ensure(interval.MaxMs<70 && interval.AverageMs<70,
            "idle interval after STOP was incorrectly included");
        Ensure(log.Stats().Ticks==4,"tick total includes both sessions");
        // No tick events or action confirmations must appear after a stopped bot.
        using var sim=new LocalSimulation(log);
        using var bot=new BotController(log,sim);
        bot.Start();
        Thread.Sleep(100);
        bot.Stop();
        int ticksAtStop=log.Snapshot().Count(x=>x.Source=="SIMULATOR"&&x.Event=="GAME_TICK");
        int confirmationsAtStop=log.Snapshot().Count(x=>x.Source=="OFFLINE_AGENT"&&x.Event=="ACTION_CONFIRMED");
        Thread.Sleep(700);
        Ensure(log.Snapshot().Count(x=>x.Source=="SIMULATOR"&&x.Event=="GAME_TICK")==ticksAtStop,
            "a callback emitted GAME_TICK after STOP");
        Ensure(log.Snapshot().Count(x=>x.Source=="OFFLINE_AGENT"&&x.Event=="ACTION_CONFIRMED")==confirmationsAtStop,
            "a pending action completed after STOP");
        // A future restart must start a new session at tick=1.
        bot.Start();
        Thread.Sleep(75);
        bot.Stop();
        var sessionStarts=log.Snapshot().Where(x=>x.Source=="SIMULATOR"&&x.Event=="START").ToArray();
        Ensure(sessionStarts.Length==4,"session start markers");
        Ensure(sessionStarts[^1].Detail.Contains("session=2"),"new session id");
        Console.WriteLine("SESSION METRICS PASS: cross-run exclusion, stop callback barrier, new session");
    }
}
