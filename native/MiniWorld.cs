using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

// An independently implemented client/server game world. The loopback protocol
// is ours; it is NOT Jagex/OSRS's proprietary protocol.
internal sealed record MiniRock(int X, int Y, bool Available);
internal sealed record MiniWorldFrame(
    long Tick, int X, int Y, int Ore, int Capacity, long MiningXp,
    long Coins, MiniRock[] Rocks, string Outcome);
internal sealed record MiniWorldRequest(string Key, string Action, int X, int Y, bool Bot);
internal sealed record MiniWorldReply(bool Accepted, string Status, MiniWorldFrame? Frame);

internal static class MiniWorldMap
{
    internal const int Width=18, Height=12;
    internal static readonly Point Market=new(2,2);
    internal static readonly Point[] RockSites={new(13,3),new(14,4),new(12,7)};
    internal static bool Wall(int x,int y) =>
        x<0 || y<0 || x>=Width || y>=Height ||
        (x==8 && y>=5 && y<=9) || (x>=5 && x<=7 && y==8);
    internal static int Distance(int ax,int ay,int bx,int by) =>
        Math.Abs(ax-bx)+Math.Abs(ay-by);
    internal static Point[] Neighbors(Point p) => new[]{
        new Point(p.X+1,p.Y), new Point(p.X,p.Y+1),
        new Point(p.X-1,p.Y), new Point(p.X,p.Y-1)};
}

internal sealed class MiniWorldServer : IDisposable
{
    private readonly object gate=new();
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource shutdown=new();
    private readonly Task accept;
    private readonly Dictionary<Point,long> depleted=new();
    private long tick;
    private int px=4,py=4,ore;
    private long miningXp,coins=1000;
    private bool botAllowed;
    internal string Key {get;}=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    internal int Port {get;}
    internal MiniWorldServer()
    {
        listener.Start(2);
        Port=((IPEndPoint)listener.LocalEndpoint).Port;
        accept=Task.Run(AcceptLoop);
    }
    internal void AllowBot(bool enabled){lock(gate)botAllowed=enabled;}
    internal MiniWorldReply Apply(MiniWorldRequest req)
    {
        lock(gate)
        {
            if(req.Key!=Key)return new(false,"UNAUTHENTICATED",null);
            if(req.Bot && !botAllowed)
                return new(false,"BOT_STOPPED",Current("BOT_STOPPED"));
            ++tick;
            var action=req.Action.ToLowerInvariant();
            string result=action switch
            {
                "observe"=>"OBSERVED",
                "move"=>Move(req.X,req.Y),
                "mine"=>Mine(req.X,req.Y),
                "sell"=>Sell(),
                _=>"UNKNOWN_ACTION"
            };
            return new(true,result,Current(result));
        }
    }
    private string Move(int x,int y)
    {
        if(MiniWorldMap.Wall(x,y) || MiniWorldMap.Distance(px,py,x,y)!=1)
            return "MOVE_REJECTED";
        px=x;py=y;return "MOVED";
    }
    private string Mine(int x,int y)
    {
        if(ore>=6)return "INVENTORY_FULL";
        var target=new Point(x,y);
        if(!MiniWorldMap.RockSites.Contains(target) ||
           MiniWorldMap.Distance(px,py,x,y)>1 || depleted.GetValueOrDefault(target)>tick)
            return "ROCK_UNAVAILABLE";
        ore++;miningXp+=35;depleted[target]=tick+6;
        return "ORE_MINED";
    }
    private string Sell()
    {
        if(MiniWorldMap.Distance(px,py,MiniWorldMap.Market.X,MiniWorldMap.Market.Y)>1)
            return "MARKET_TOO_FAR";
        if(ore==0)return "NOTHING_TO_SELL";
        coins+=ore*25;ore=0;return "ORE_SOLD";
    }
    private MiniWorldFrame Current(string outcome) =>
        new(tick,px,py,ore,6,miningXp,coins,
            MiniWorldMap.RockSites.Select(p=>
                new MiniRock(p.X,p.Y,depleted.GetValueOrDefault(p)<=tick)).ToArray(),
            outcome);
    private async Task AcceptLoop()
    {
        while(!shutdown.IsCancellationRequested)
        {
            try
            {
                using var client=await listener.AcceptTcpClientAsync(shutdown.Token);
                client.NoDelay=true;
                using var stream=client.GetStream();
                using var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true);
                using var writer=new StreamWriter(stream,new UTF8Encoding(false),4096,true)
                    {AutoFlush=true};
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                string? line=await reader.ReadLineAsync(timeout.Token);
                if(line is null || line.Length>4096)continue;
                MiniWorldReply reply;
                try
                {
                    var request=JsonSerializer.Deserialize<MiniWorldRequest>(line);
                    reply=request is null?new(false,"EMPTY_REQUEST",null):Apply(request);
                }
                catch(JsonException){reply=new(false,"INVALID_JSON",null);}
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(),timeout.Token);
            }
            catch(OperationCanceledException){if(shutdown.IsCancellationRequested)break;}
            catch(ObjectDisposedException){break;}
            catch(IOException){}
            catch(SocketException){if(shutdown.IsCancellationRequested)break;}
        }
    }
    public void Dispose()
    {
        shutdown.Cancel();listener.Stop();
        try{accept.Wait(1000);}catch{}
        shutdown.Dispose();
    }
}

internal static class MiniWorldWire
{
    internal static async Task<MiniWorldReply> Exchange(
        int port, MiniWorldRequest request,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var client=new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback,port,timeout.Token);
        using var stream=client.GetStream();
        using var writer=new StreamWriter(stream,new UTF8Encoding(false),4096,true){AutoFlush=true};
        using var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),timeout.Token);
        var response=await reader.ReadLineAsync(timeout.Token);
        return response is null || response.Length>10000
            ?new(false,"INVALID_RESPONSE",null)
            :JsonSerializer.Deserialize<MiniWorldReply>(response) ??
                new(false,"EMPTY_RESPONSE",null);
    }
}

internal static class MiniWorldPlanner
{
    private static Point? StepToward(MiniWorldFrame frame, Point target)
    {
        var origin=new Point(frame.X,frame.Y);
        var queue=new Queue<Point>();
        var first=new Dictionary<Point,Point>();
        var visited=new HashSet<Point>{origin};
        queue.Enqueue(origin);
        while(queue.Count>0)
        {
            var p=queue.Dequeue();
            if(MiniWorldMap.Distance(p.X,p.Y,target.X,target.Y)<=1)
                return p==origin?null:first[p];
            foreach(var next in MiniWorldMap.Neighbors(p))
            {
                if(MiniWorldMap.Wall(next.X,next.Y)||!visited.Add(next))continue;
                first[next]=p==origin?next:first[p];
                queue.Enqueue(next);
            }
        }
        return null;
    }
    internal static MiniWorldRequest Decide(string key,MiniWorldFrame frame,Point? manual=null)
    {
        MiniWorldRequest Cmd(string key,string action,Point p) =>
            new(key,action,p.X,p.Y,manual is null);
        if(manual is not null)
        {
            var destination=manual.Value;
            if(MiniWorldMap.Wall(destination.X,destination.Y))
                return Cmd(key,"observe",new(frame.X,frame.Y));
            if(frame.X==destination.X && frame.Y==destination.Y)
                return Cmd(key,"observe",destination);
            // A manual click aims at the tile, not its adjacent cell.
            var step=ManualStep(frame,destination);
            return step is null?Cmd(key,"observe",destination):Cmd(key,"move",step.Value);
        }
        if(frame.Ore==frame.Capacity)
        {
            var dest=MiniWorldMap.Market;
            if(MiniWorldMap.Distance(frame.X,frame.Y,dest.X,dest.Y)<=1)
                return Cmd(key,"sell",dest);
            var step=StepToward(frame,dest);
            return step is null?Cmd(key,"observe",dest):Cmd(key,"move",step.Value);
        }
        var rock=frame.Rocks.Where(r=>r.Available)
            .OrderBy(r=>MiniWorldMap.Distance(frame.X,frame.Y,r.X,r.Y))
            .FirstOrDefault();
        if(rock is null)
            return Cmd(key,"observe",new(frame.X,frame.Y));
        var goal=new Point(rock.X,rock.Y);
        if(MiniWorldMap.Distance(frame.X,frame.Y,goal.X,goal.Y)<=1)
            return Cmd(key,"mine",goal);
        var move=StepToward(frame,goal);
        return move is null?Cmd(key,"observe",goal):Cmd(key,"move",move.Value);
    }
    private static Point? ManualStep(MiniWorldFrame frame,Point target)
    {
        var origin=new Point(frame.X,frame.Y);
        var queue=new Queue<Point>();queue.Enqueue(origin);
        var visited=new HashSet<Point>{origin};
        var first=new Dictionary<Point,Point>();
        while(queue.Count>0)
        {
            var current=queue.Dequeue();
            if(current==target)return first[current];
            foreach(var next in MiniWorldMap.Neighbors(current))
            {
                if(MiniWorldMap.Wall(next.X,next.Y)||!visited.Add(next))continue;
                first[next]=current==origin?next:first[current];queue.Enqueue(next);
            }
        }
        return null;
    }
}

internal sealed class MiniWorldClient : IDisposable
{
    private readonly Diagnostics log;
    private readonly MiniWorldServer server;
    private readonly CancellationTokenSource shutdown=new();
    private readonly Task polling;
    private readonly object gate=new();
    private MiniWorldFrame? state;
    private Point? manual;
    private bool botRunning;
    private string connection="CONNECTING";
    internal MiniWorldFrame? Snapshot {get {lock(gate)return state;}}
    internal string Connection {get {lock(gate)return connection;}}
    internal bool BotRunning {get {lock(gate)return botRunning;}}
    internal MiniWorldClient(Diagnostics log)
    {
        this.log=log;
        server=new MiniWorldServer();
        log.SetMode("EIGENER CLIENT (LOKALE TESTWELT)");
        log.Add(Severity.INFO,"MINI_WORLD","START",
            "own_client=true transport=TCP_LOOPBACK game_server=LOCAL_SANDBOX official_osrs=false");
        polling=Task.Run(Run);
    }
    internal void SetBot(bool enable)
    {
        lock(gate)
        {
            server.AllowBot(enable);
            botRunning=enable;
            manual=null;
        }
        log.Add(Severity.INFO,"MINI_BOT",enable?"STARTED":"STOPPED",
            "server_authorized="+enable.ToString().ToLowerInvariant()+
            " mode=LOCAL_WORLD no_official_osrs_connection=true");
    }
    internal void MoveManually(Point destination)
    {
        lock(gate)
        {
            if(botRunning)return;
            if(!MiniWorldMap.Wall(destination.X,destination.Y))manual=destination;
        }
    }
    private async Task Run()
    {
        var token=shutdown.Token;
        while(!token.IsCancellationRequested)
        {
            try
            {
                MiniWorldRequest request;
                lock(gate)
                {
                    var observed=state;
                    if(observed is null)request=new(server.Key,"observe",0,0,false);
                    else if(botRunning)request=MiniWorldPlanner.Decide(server.Key,observed);
                    else if(manual is Point target)
                    {
                        request=MiniWorldPlanner.Decide(server.Key,observed,target);
                        if(observed.X==target.X&&observed.Y==target.Y)manual=null;
                    }
                    else request=new(server.Key,"observe",0,0,false);
                }
                var outcome=await MiniWorldWire.Exchange(server.Port,request,token);
                lock(gate)
                {
                    connection=outcome.Accepted?"LOCAL_SANDBOX_CONNECTED":outcome.Status;
                    if(outcome.Frame is not null)state=outcome.Frame;
                }
                if(outcome.Accepted && outcome.Frame is not null)
                    log.Add(Severity.DEBUG,"MINI_WORLD","GAME_TICK",
                        $"tick={outcome.Frame.Tick} x={outcome.Frame.X} y={outcome.Frame.Y} "+
                        $"ore={outcome.Frame.Ore} xp={outcome.Frame.MiningXp} coins={outcome.Frame.Coins} "+
                        "authoritative=true sandbox_only=true");
                if(outcome.Accepted && request.Action is ("mine" or "sell"))
                    log.Add(Severity.INFO,"MINI_WORLD",outcome.Status,
                        $"tick={outcome.Frame?.Tick} ore={outcome.Frame?.Ore} xp={outcome.Frame?.MiningXp} "+
                        $"coins={outcome.Frame?.Coins} source=LOCAL_SANDBOX");
                else if(!outcome.Accepted)
                    log.Add(Severity.WARN,"MINI_WORLD","COMMAND_REJECTED",outcome.Status);
                await Task.Delay(600,token);
            }
            catch(OperationCanceledException){break;}
            catch(Exception ex)
            {
                lock(gate){connection="DISCONNECTED";}
                log.Add(Severity.WARN,"MINI_WORLD","TRANSPORT_ERROR",ex.GetType().Name);
                try{await Task.Delay(750,token);}catch(OperationCanceledException){break;}
            }
        }
    }
    public void Dispose()
    {
        shutdown.Cancel();
        server.AllowBot(false);
        server.Dispose();
        log.Add(Severity.INFO,"MINI_WORLD","STOP","world_session_ended=true");
        try{polling.Wait(1200);}catch{}
        shutdown.Dispose();
    }
}

internal static class MiniWorldTests
{
    private static void Require(bool yes,string name)
    {if(!yes)throw new Exception("Mini client test failed: "+name);}
    internal static void Run()
    {
        using var server=new MiniWorldServer();
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token=cancellation.Token;
        MiniWorldReply Execute(string key,string action,int x=0,int y=0,bool bot=false) =>
            MiniWorldWire.Exchange(server.Port,new(key,action,x,y,bot),token)
                .GetAwaiter().GetResult();
        var no=Execute("invalid","observe");
        Require(!no.Accepted && no.Frame is null,"auth rejects other local process");
        var initial=Execute(server.Key,"observe");
        Require(initial.Accepted && initial.Frame is not null &&
            initial.Frame.X==4 && initial.Frame.Y==4,"real TCP snapshot");
        var blocked=Execute(server.Key,"move",5,4,true);
        Require(!blocked.Accepted && blocked.Status=="BOT_STOPPED","fail closed when stopped");
        server.AllowBot(true);
        MiniWorldFrame frame=initial.Frame!;
        bool sold=false;
        for(int n=0;n<180;n++)
        {
            var action=MiniWorldPlanner.Decide(server.Key,frame);
            var response=Execute(server.Key,action.Action,action.X,action.Y,true);
            Require(response.Accepted && response.Frame is not null,"server reply");
            frame=response.Frame;
            if(response.Status=="ORE_SOLD"){sold=true;break;}
        }
        Require(sold && frame.Coins>1000 && frame.MiningXp>=210 && frame.Ore==0,
            "bot must mine, navigate and sell on authoritative server");
        server.AllowBot(false);
        var stop=Execute(server.Key,"mine",13,3,true);
        Require(!stop.Accepted && stop.Status=="BOT_STOPPED","hard stop gate");
        Require(Execute(server.Key,"move",50,50).Status=="MOVE_REJECTED","bounds rejection");
        Console.WriteLine("STANDALONE CLIENT PASS: TCP auth, authoritative state, routing, mining, sale, server-side stop");
    }
}
