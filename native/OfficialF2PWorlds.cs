using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

// Read-only world discovery and TCP connectivity measurement. This module
// DOES NOT authenticate to Jagex, speak the OSRS protocol, or play the game.
internal sealed record OfficialF2PWorld(int Id, string Location, string Activity)
{
    internal int WorldNumber => Id+300;
    internal string Hostname => $"oldschool{Id}.runescape.com";
}
internal sealed record WorldLatency(OfficialF2PWorld World, double Milliseconds, int Samples);
internal sealed record F2PWorldScan(int Eligible, int Reachable,
    IReadOnlyList<WorldLatency> Fastest, string Source);

internal static class OfficialF2PWorldScanner
{
    internal const string SourceUrl="https://oldschool.runescape.com/slu";
    internal const int GamePort=43594;
    private static readonly Regex Row=new(@"<tr\b[^>]*>[\s\S]*?</tr>",
        RegexOptions.IgnoreCase|RegexOptions.Compiled);
    private static readonly Regex Cell=new(@"<td\b[^>]*>([\s\S]*?)</td>",
        RegexOptions.IgnoreCase|RegexOptions.Compiled);
    private static readonly Regex Tags=new(@"<[^>]*>",
        RegexOptions.Compiled);
    private static readonly Regex Whitespace=new(@"\s+",
        RegexOptions.Compiled);
    private static readonly Regex WorldName=new(@"\bOld School\s+(\d+)\b",
        RegexOptions.IgnoreCase|RegexOptions.Compiled);
    private static readonly Regex PureF2P=new(@"^Free$",
        RegexOptions.IgnoreCase|RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedOrigins=new(StringComparer.OrdinalIgnoreCase)
    {
        "oldschool.runescape.com"
    };
    private static string Normalize(string input)=>
        Whitespace.Replace(WebUtility.HtmlDecode(Tags.Replace(input," ")).Trim()," ");

    internal static IReadOnlyList<OfficialF2PWorld> ParseOfficialList(string html)
    {
        if(html.Length>2_000_000)throw new InvalidDataException("Official world page exceeds size limit.");
        var worlds=new Dictionary<int,OfficialF2PWorld>();
        foreach(Match row in Row.Matches(html))
        {
            var cells=Cell.Matches(row.Value).Select(m=>Normalize(m.Groups[1].Value)).ToArray();
            if(cells.Length<5)continue;
            var match=WorldName.Match(cells[0]);
            if(!match.Success||!int.TryParse(match.Groups[1].Value,
               NumberStyles.None,CultureInfo.InvariantCulture,out int n)||
               n<1||n>500)continue;
            // This is deliberately strict: only official "Free" worlds,
            // excluding PvP, restricted total-level, events, Fresh Start, etc.
            if(!PureF2P.IsMatch(cells[3]))continue;
            string activity=cells[4];
            if(activity!="" && activity!="-" && activity!="–")continue;
            worlds[n]=new(n,cells[2],activity);
        }
        return worlds.Values.OrderBy(w=>w.Id).ToArray();
    }
    private static async Task<string> DownloadWorlds(CancellationToken ct)
    {
        using var handler=new HttpClientHandler{AllowAutoRedirect=false};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(14)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OSRS-Play/0.7.1 (F2P-world-latency-diagnostics)");
        using var response=await http.GetAsync(SourceUrl,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.RequestMessage?.RequestUri is not Uri uri ||
           uri.Scheme!=Uri.UriSchemeHttps||!AllowedOrigins.Contains(uri.Host))
            throw new InvalidDataException("World-list source is not the official HTTPS host.");
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>2_000_000)
            throw new InvalidDataException("World list exceeds size limit.");
        return await response.Content.ReadAsStringAsync(ct);
    }
    private static async Task<double?> MeasureTcp(string host,CancellationToken ct)
    {
        // Consistent measure: TCP connect RTT to the OSRS game port, without
        // sending any game data, login requests, or secrets.
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1200));
            var ip=await Dns.GetHostAddressesAsync(host,timeout.Token);
            var address=ip.FirstOrDefault(x=>x.AddressFamily==AddressFamily.InterNetwork);
            if(address is null)return null;
            using var socket=new TcpClient(AddressFamily.InterNetwork);
            var watch=Stopwatch.StartNew();
            await socket.ConnectAsync(address,GamePort,timeout.Token);
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return null;}
        catch(SocketException){return null;}
        catch(System.IO.IOException){return null;}
    }
    internal static double Median(IEnumerable<double> values)
    {
        var sorted=values.Where(x=>double.IsFinite(x)&&x>=0).Order().ToArray();
        if(sorted.Length==0)throw new ArgumentException("No successful probe samples");
        return sorted.Length%2==1?sorted[sorted.Length/2]:
            (sorted[sorted.Length/2-1]+sorted[sorted.Length/2])/2d;
    }
    internal static async Task<F2PWorldScan> ScanAsync(CancellationToken ct)
    {
        var html=await DownloadWorlds(ct);
        var worlds=ParseOfficialList(html);
        if(worlds.Count==0)
            throw new InvalidDataException("No verifiable unrestricted F2P worlds in official list.");
        using var limit=new SemaphoreSlim(8);
        var first=await Task.WhenAll(worlds.Select(async world=>
        {
            await limit.WaitAsync(ct);
            try{return (World:world,Latency:await MeasureTcp(world.Hostname,ct));}
            finally{limit.Release();}
        }));
        var reachable=first.Where(x=>x.Latency is not null)
            .OrderBy(x=>x.Latency).ToArray();
        if(reachable.Length==0)
            return new(worlds.Count,0,Array.Empty<WorldLatency>(),SourceUrl);
        // Reprobe the fastest candidates. DNS and network startup effects in a
        // single sample must not decide the preferred world.
        var finalists=reachable.Take(8);
        var measured=await Task.WhenAll(finalists.Select(async candidate=>
        {
            var sample=new List<double>{candidate.Latency!.Value};
            for(int i=0;i<3;i++)
            {
                ct.ThrowIfCancellationRequested();
                var value=await MeasureTcp(candidate.World.Hostname,ct);
                if(value is double ms)sample.Add(ms);
                await Task.Delay(70,ct);
            }
            return new WorldLatency(candidate.World,
                Math.Round(Median(sample),1),sample.Count);
        }));
        return new(worlds.Count,reachable.Length,
            measured.OrderBy(x=>x.Milliseconds).ThenBy(x=>x.World.Id).ToArray(),
            SourceUrl);
    }
}

internal static class OfficialF2PWorldTests
{
    internal static void Run()
    {
        const string sample = """
        <table>
        <tr class="server-list__row"><td>Old School 97</td><td>99 players</td>
        <td>Germany</td><td>Free</td><td>-</td></tr>
        <tr><td>Old School 98</td><td>101 players</td>
        <td>Germany</td><td>Free</td><td>Forestry</td></tr>
        <tr><td>Old School 113</td><td>80 players</td>
        <td>Germany</td><td>Free</td><td>500 skill total</td></tr>
        <tr><td>Old School 99</td><td>180 players</td>
        <td>Germany</td><td>Members</td><td>-</td></tr>
        <tr><td>Old School 84</td><td>90 players</td>
        <td>Germany</td><td>Free</td><td>-</td></tr>
        <tr><td>Old School 117</td><td>90 players</td>
        <td>United States</td><td>Free</td><td>-</td></tr>
        </table>
        """;
        var selected=OfficialF2PWorldScanner.ParseOfficialList(sample);
        if(selected.Count!=3||!selected.Any(w=>w.WorldNumber==397)||
           !selected.Any(w=>w.WorldNumber==384)||
           !selected.Any(w=>w.WorldNumber==417))
            throw new Exception("F2P list parser included restricted or member worlds.");
        if(selected.Any(w=>w.WorldNumber==398 || w.WorldNumber==413 || w.WorldNumber==399))
            throw new Exception("Unsafe or member world accepted.");
        if(selected.Single(w=>w.Id==97).Hostname!="oldschool97.runescape.com")
            throw new Exception("Official host naming mismatch.");
        if(OfficialF2PWorldScanner.Median(new[]{25d,14d,17d})!=17 ||
           OfficialF2PWorldScanner.Median(new[]{10d,20d})!=15)
            throw new Exception("Median latency test.");
        if(OfficialF2PWorldScanner.ParseOfficialList("<html><body>unknown</body></html>").Count!=0)
            throw new Exception("Unverified world-list must fail closed.");
        Console.WriteLine("OFFICIAL F2P PING PASS: official-list parsing, no member/event/level worlds, TCP hostname, median");
    }
}
