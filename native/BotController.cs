namespace OSRSLiveDiagnostics;

// Owns the lifecycle of the in-process offline agent and tick simulation.
// This engine never logs into OSRS, controls a game process, or emits input.
internal sealed class BotController : IDisposable
{
    private readonly Diagnostics diagnostics;
    private readonly LocalSimulation simulation;
    private readonly object gate=new();
    internal BotController(Diagnostics diagnostics,LocalSimulation simulation)
    {this.diagnostics=diagnostics;this.simulation=simulation;}
    internal bool Running {get {lock(gate)return simulation.Running&&simulation.AgentEnabled;}}
    internal void Start()
    {
        lock(gate)
        {
            if(Running)
            {
                diagnostics.Add(Severity.INFO,"BOT","ALREADY_RUNNING","mode=OFFLINE_SIMULATION");
                return;
            }
            if(simulation.Running)simulation.Stop();
            simulation.Start();
            try
            {
                simulation.SetAgentEnabled(true);
                diagnostics.Add(Severity.INFO,"BOT","STARTED",
                    "mode=OFFLINE_SIMULATION agent=enabled real_game_input=false");
            }
            catch {simulation.Stop();throw;}
        }
    }
    internal void Stop()
    {
        lock(gate)
        {
            if(!simulation.Running)
            {
                diagnostics.Add(Severity.INFO,"BOT","ALREADY_STOPPED","mode=OFFLINE_SIMULATION");
                return;
            }
            simulation.Stop();
            diagnostics.Add(Severity.INFO,"BOT","STOPPED",
                "mode=OFFLINE_SIMULATION real_game_input=false");
        }
    }
    public void Dispose(){lock(gate){if(simulation.Running)Stop();}}
}

internal static class BotControllerTests
{
    internal static void Run()
    {
        var d=new Diagnostics();
        using var sim=new LocalSimulation(d);
        using var bot=new BotController(d,sim);
        if(bot.Running)throw new Exception("Bot initially active");
        bot.Start();
        if(!bot.Running||!sim.Running||!sim.AgentEnabled)throw new Exception("Bot did not start");
        bot.Start();
        if(d.Snapshot().Count(x=>x.Source=="BOT"&&x.Event=="STARTED")!=1)
            throw new Exception("Second start created new session");
        bot.Stop();
        if(bot.Running||sim.Running||sim.AgentEnabled)throw new Exception("Bot did not stop");
        bot.Stop();
        bot.Start();
        if(!bot.Running)throw new Exception("Bot restart failed");
        bot.Stop();
        if(d.Snapshot().Count(x=>x.Source=="BOT"&&x.Event=="STOPPED")!=2)
            throw new Exception("Stop counter");
        Console.WriteLine("BOT CONTROL PASS: start, double start, stop, double stop, restart");
    }
}
