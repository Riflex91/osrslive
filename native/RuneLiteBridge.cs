using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

// A local-only IPC bridge for explicitly paired RuneLite development plugins.
// Never accepts commands from the network and never handles account credentials.
internal sealed record LiveRock(int Id,int WorldX,int WorldY,int CanvasX,int CanvasY);
internal sealed record LiveFrame(
    int Schema,long Tick,long CapturedUnixMs,bool LoggedIn,bool Focused,
    int PlayerX,int PlayerY,int Hitpoints,int InventoryUsed,int InventoryCapacity,
    long MiningXp,List<LiveRock>? Rocks);
internal sealed record LiveCommand(long Id,string Action,int RockId,int WorldX,int WorldY);
internal sealed record LiveReply(bool Connected,bool Running,string Status,LiveCommand? Command);

internal sealed class LiveMiningAgent
{
    private readonly Diagnostics diagnostics;
    private readonly object sync=new();
    private bool enabled;
    private long previousTick,requestId,cooldownTick;
    private LiveCommand? pending;
    private long pendingTick,pendingXp;
    private int pendingInventory;
    internal bool Enabled {get {lock(sync)return enabled;}}
    internal LiveMiningAgent(Diagnostics diagnostics){this.diagnostics=diagnostics;}
    internal void Start()
    {
        lock(sync)
        {
            enabled=true;previousTick=0;requestId=0;cooldownTick=0;pending=null;
            diagnostics.Add(Severity.INFO,"LIVE_AGENT","STARTED",
                "mode=RUNELITE_MINING source=LIVE_GAMESTATE actions=mine_only bank=false");
        }
    }
    internal void Stop()
    {
        lock(sync)
        {
            if(pending is not null)
                diagnostics.Add(Severity.INFO,"LIVE_AGENT","ACTION_CANCELLED",
                    "id="+pending.Id+" reason=BOT_STOPPED");
            enabled=false;pending=null;previousTick=0;
            diagnostics.Add(Severity.INFO,"LIVE_AGENT","STOPPED",
                "no_further_commands=true");
        }
    }
    internal LiveReply OnFrame(LiveFrame f)
    {
        lock(sync)
        {
            if(f.Schema!=1 || f.Rocks is null || f.Rocks.Count>48 ||
               f.Tick<1 || f.Hitpoints<0 || f.Hitpoints>150 ||
               f.InventoryCapacity<1 || f.InventoryCapacity>28 ||
               f.InventoryUsed<0 || f.InventoryUsed>f.InventoryCapacity ||
               f.MiningXp<0 ||
               f.PlayerX<0 || f.PlayerY<0 || f.PlayerX>20000 || f.PlayerY>20000 ||
               f.Rocks.Any(x=>x.Id<1||x.WorldX<0||x.WorldY<0||
                    x.CanvasX<0||x.CanvasY<0||x.CanvasX>4000||x.CanvasY>4000))
                return new(true,enabled,"INVALID_GAMESTATE",null);
            var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(Math.Abs(now-f.CapturedUnixMs)>2500)
                return new(true,enabled,"STALE_FRAME",null);
            if(!enabled)return new(true,false,"AGENT_STOPPED",null);
            if(!f.LoggedIn)
            {
                if(pending is not null)
                    diagnostics.Add(Severity.WARN,"LIVE_AGENT","ACTION_CANCELLED",
                        "reason=GAME_DISCONNECTED id="+pending.Id);
                pending=null;previousTick=0;
                return new(true,true,"WAIT_LOGIN",null);
            }
            if(f.Tick<=previousTick)
                return new(true,true,"NON_MONOTONIC_TICK",null);
            previousTick=f.Tick;
            if(f.Hitpoints<=20)
            {
                pending=null;
                diagnostics.Add(Severity.WARN,"LIVE_AGENT","HALTED_LOW_HP",
                    "hp="+f.Hitpoints+" manual_review_required=true");
                enabled=false;
                return new(true,false,"HALTED_LOW_HP",null);
            }
            if(f.InventoryUsed==f.InventoryCapacity)
            {
                pending=null;enabled=false;
                diagnostics.Add(Severity.INFO,"LIVE_AGENT","INVENTORY_FULL",
                    "used="+f.InventoryUsed+" action=STOP no_banking_implemented=true");
                return new(true,false,"INVENTORY_FULL",null);
            }
            if(pending is not null)
            {
                if(f.InventoryUsed>pendingInventory && f.MiningXp>pendingXp)
                {
                    diagnostics.Add(Severity.INFO,"LIVE_AGENT","ACTION_CONFIRMED",
                        "id="+pending.Id+" effect=INVENTORY_AND_MINING_XP "+
                        "before_slots="+pendingInventory+" after_slots="+f.InventoryUsed+
                        " xp_gained="+(f.MiningXp-pendingXp));
                    pending=null;
                    cooldownTick=f.Tick+2;
                }
                else if(f.Tick-pendingTick>=12)
                {
                    diagnostics.Add(Severity.WARN,"LIVE_AGENT","ACTION_TIMED_OUT",
                        "id="+pending.Id+" no_verified_mining_effect=true");
                    pending=null;cooldownTick=f.Tick+4;
                }
                else return new(true,true,"AWAIT_GAME_EFFECT",null);
            }
            if(!f.Focused)return new(true,true,"WAIT_WINDOW_FOCUS",null);
            if(f.Tick<cooldownTick)return new(true,true,"COOLDOWN",null);
            var rock=f.Rocks.Where(x=>
                Math.Abs(x.WorldX-f.PlayerX)+Math.Abs(x.WorldY-f.PlayerY)<=6)
                .OrderBy(x=>Math.Abs(x.WorldX-f.PlayerX)+Math.Abs(x.WorldY-f.PlayerY))
                .ThenBy(x=>x.Id).FirstOrDefault();
            if(rock is null)return new(true,true,"NO_VISIBLE_MINEABLE_ROCK",null);
            var command=new LiveCommand(++requestId,"mine",rock.Id,rock.WorldX,rock.WorldY);
            pending=command;pendingTick=f.Tick;pendingXp=f.MiningXp;
            pendingInventory=f.InventoryUsed;
            diagnostics.Add(Severity.INFO,"LIVE_AGENT","ACTION_OFFERED",
                "id="+command.Id+" game_tick="+f.Tick+" target_object="+rock.Id+
                " pos="+rock.WorldX+","+rock.WorldY+" awaiting_verified_effect=true");
            return new(true,true,"COMMAND_OFFERED",command);
        }
    }
}

internal sealed class RuneLiteLoopbackBridge : IDisposable
{
    internal const int Port=46893;
    private readonly Diagnostics diagnostics;
    private readonly LiveMiningAgent agent;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource shutdown=new();
    private readonly Task acceptLoop;
    internal string PairingCode {get;}
    private long lastSeenUnixMs;
    internal bool RecentlyConnected =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-Interlocked.Read(ref lastSeenUnixMs)<4000;
    internal RuneLiteLoopbackBridge(Diagnostics diagnostics,LiveMiningAgent agent)
    {
        this.diagnostics=diagnostics;this.agent=agent;
        PairingCode=GetOrCreatePairingCode();
        listener=new TcpListener(IPAddress.Loopback,Port);
        listener.Start(8);
        acceptLoop=Task.Run(AcceptLoop);
        diagnostics.Add(Severity.INFO,"LIVE_BRIDGE","LISTENING",
            "loopback_only=true port="+Port+" protocol=v1 no_credentials=true");
    }
    private static string GetOrCreatePairingCode()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OSRSPlay");
        Directory.CreateDirectory(dir);
        var file=Path.Combine(dir,"bridge-pairing.key");
        if(File.Exists(file))
        {
            var old=File.ReadAllText(file).Trim();
            if(old.Length==32&&old.All(Uri.IsHexDigit))return old;
        }
        var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        // Never expose this key in log exports or remote services.
        File.WriteAllText(file,code);
        return code;
    }
    private async Task AcceptLoop()
    {
        while(!shutdown.IsCancellationRequested)
        {
            try
            {
                var client=await listener.AcceptTcpClientAsync(shutdown.Token);
                _=Task.Run(()=>HandleConnection(client));
            }
            catch(OperationCanceledException){break;}
            catch(ObjectDisposedException){break;}
            catch(Exception ex)
            {
                diagnostics.Add(Severity.WARN,"LIVE_BRIDGE","ACCEPT_FAILED",ex.GetType().Name);
            }
        }
    }
    private async Task HandleConnection(TcpClient client)
    {
        using(client)
        {
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                timeout.CancelAfter(2000);
                using var stream=client.GetStream();
                var header=new List<byte>();
                while(header.Count<4096)
                {
                    var one=new byte[1];
                    if(await stream.ReadAsync(one,timeout.Token)==0)return;
                    header.Add(one[0]);
                    if(header.Count>=4 && header[^4]==13&&header[^3]==10&&
                       header[^2]==13&&header[^1]==10)break;
                }
                if(header.Count>=4096)return;
                string h=Encoding.ASCII.GetString(header.ToArray());
                var lines=h.Split("\r\n",StringSplitOptions.None);
                if(lines[0]!="POST /snapshot HTTP/1.1")return;
                string? supplied=null;int length=-1;
                foreach(var line in lines.Skip(1))
                {
                    int colon=line.IndexOf(':');
                    if(colon<1)continue;
                    var key=line[..colon].Trim();
                    var value=line[(colon+1)..].Trim();
                    if(key.Equals("X-OSRS-Play-Key",StringComparison.OrdinalIgnoreCase))supplied=value;
                    if(key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))
                        int.TryParse(value,out length);
                }
                if(supplied is null||supplied.Length!=PairingCode.Length||
                   !CryptographicOperations.FixedTimeEquals(
                     Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(PairingCode)))
                {
                    await Send(stream,403,new LiveReply(false,false,"PAIRING_REJECTED",null),timeout.Token);
                    return;
                }
                if(length<2||length>12000)
                {
                    await Send(stream,400,new LiveReply(false,false,"BAD_SIZE",null),timeout.Token);
                    return;
                }
                var bytes=new byte[length];int offset=0;
                while(offset<length)
                {
                    int read=await stream.ReadAsync(bytes.AsMemory(offset,length-offset),timeout.Token);
                    if(read==0)return;
                    offset+=read;
                }
                var frame=JsonSerializer.Deserialize<LiveFrame>(bytes,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive=true });
                if(frame is null)throw new JsonException("No frame");
                Interlocked.Exchange(ref lastSeenUnixMs,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var response=agent.OnFrame(frame);
                await Send(stream,200,response,timeout.Token);
            }
            catch(Exception ex) when(ex is IOException or JsonException or
                                       OperationCanceledException or SocketException)
            {
                if(!shutdown.IsCancellationRequested)
                    diagnostics.Add(Severity.DEBUG,"LIVE_BRIDGE","FRAME_REJECTED",
                        ex.GetType().Name);
            }
        }
    }
    private static async Task Send(NetworkStream stream,int code,LiveReply payload,CancellationToken ct)
    {
        var json=JsonSerializer.Serialize(payload,new JsonSerializerOptions{
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        var body=Encoding.UTF8.GetBytes(json);
        var head=Encoding.ASCII.GetBytes(
            "HTTP/1.1 "+code+(code==200?" OK":" Rejected")+"\r\n"+
            "Content-Type: application/json\r\n"+
            "Content-Length: "+body.Length+"\r\n"+
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head,ct);
        await stream.WriteAsync(body,ct);
        await stream.FlushAsync(ct);
    }
    public void Dispose()
    {
        shutdown.Cancel();
        listener.Stop();
        try{acceptLoop.Wait(1500);}catch{}
        shutdown.Dispose();
    }
}

internal static class LiveMiningTests
{
    private static void Check(bool result,string label)
    {if(!result)throw new Exception("LiveMining test: "+label);}
    internal static void Run()
    {
        var log=new Diagnostics();
        var agent=new LiveMiningAgent(log);
        var t=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        LiveFrame S(long tick,int slots=0,long xp=0,bool focused=true,bool logged=true)=>
            new(1,tick,t,logged,focused,3200,3200,99,slots,28,xp,
                new List<LiveRock>{new(1234,3201,3200,100,100)});
        Check(agent.OnFrame(S(1)).Status=="AGENT_STOPPED","default stopped");
        agent.Start();
        var command=agent.OnFrame(S(1)).Command;
        Check(command is not null && command.Action=="mine","first real command contract");
        Check(agent.OnFrame(S(2)).Status=="AWAIT_GAME_EFFECT","await effect");
        Check(agent.OnFrame(S(3,1,20)).Command is null,"observed effect cooldown");
        Check(log.Snapshot().Any(x=>x.Event=="ACTION_CONFIRMED"),"independent XP & inventory");
        Check(agent.OnFrame(S(2)).Status=="NON_MONOTONIC_TICK","duplicate tick");
        Check(agent.OnFrame(S(5,1,20,false)).Command is null,"focus barrier");
        agent.Stop();
        Check(!agent.Enabled,"stop");
        agent.Start();
        Check(agent.OnFrame(S(1,28,0)).Status=="INVENTORY_FULL","bank is not implemented");
        agent.Start();
        Check(agent.OnFrame(S(1,0,0,true,false)).Status=="WAIT_LOGIN","login gate");
        agent.Start();
        Check(agent.OnFrame(S(1) with { CapturedUnixMs=t-10000 }).Status=="STALE_FRAME",
            "stale frame");
        agent.Start();
        Check(agent.OnFrame(S(1) with { Rocks=new List<LiveRock>{new(-1,0,0,0,0)} })
            .Status=="INVALID_GAMESTATE","invalid rock");
        Console.WriteLine("LIVE BRIDGE CONTRACT PASS: gated commands, confirmed effect, stop, focus, stale, inventory");
    }
}
