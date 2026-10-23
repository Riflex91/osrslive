using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace OSRSLiveDiagnostics;

// Public, read-only OSRS highscores. This is live Jagex-hosted player data,
// NOT an authenticated game session or authorization to issue actions.
internal sealed record PublicOsrsSkill(string Name,int Level,long Experience,int Rank);
internal sealed record PublicOsrsProfile(string DisplayName,int TotalLevel,
    IReadOnlyList<PublicOsrsSkill> Skills,DateTimeOffset FetchedUtc);

internal static class PublicOsrsHiscores
{
    internal const string Source="https://secure.runescape.com/m=hiscore_oldschool/index_lite.ws";
    internal static readonly string[] OrderedSkills =
    {
        "Attack","Defence","Strength","Hitpoints","Ranged","Prayer","Magic",
        "Cooking","Woodcutting","Fletching","Fishing","Firemaking","Crafting",
        "Smithing","Mining","Herblore","Agility","Thieving","Slayer","Farming",
        "Runecraft","Hunter","Construction","Sailing"
    };
    private static readonly Regex DisplayName=new(@"^[A-Za-z0-9 _-]{1,12}$",
        RegexOptions.CultureInvariant|RegexOptions.Compiled);
    internal static bool ValidName(string name)=>DisplayName.IsMatch(name);

    internal static PublicOsrsProfile Parse(string name,string csv,DateTimeOffset when)
    {
        if(!ValidName(name) || csv.Length>50_000)
            throw new FormatException("Invalid player name or response size.");
        var rows=csv.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries);
        if(rows.Length<OrderedSkills.Length+1)
            throw new FormatException("Incomplete OSRS skill response.");
        int[] ParseRow(string text)
        {
            var fields=text.Trim().Split(',');
            if(fields.Length!=3 || fields.Any(x=>!int.TryParse(x,
                NumberStyles.Integer,CultureInfo.InvariantCulture,out _)))
                throw new FormatException("Malformed OSRS highscore row.");
            return fields.Select(x=>int.Parse(x,CultureInfo.InvariantCulture)).ToArray();
        }
        var summary=ParseRow(rows[0]);
        if(summary[1]<24 || summary[1]>2376)
            throw new FormatException("Unexpected total level.");
        var skills=new List<PublicOsrsSkill>(OrderedSkills.Length);
        for(int i=0;i<OrderedSkills.Length;i++)
        {
            var values=rows[i+1].Split(',');
            if(values.Length!=3 ||
               !int.TryParse(values[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out int rank) ||
               !int.TryParse(values[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out int level) ||
               !long.TryParse(values[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out long xp) ||
               level<1 || level>99 || xp < -1)
                throw new FormatException("Invalid OSRS skill row.");
            skills.Add(new(OrderedSkills[i],level,xp,rank));
        }
        if(skills.Sum(s=>s.Level)!=summary[1])
            throw new FormatException("Total level disagrees with 24 skills.");
        return new(name,summary[1],skills,when);
    }
    internal static async Task<PublicOsrsProfile> FetchAsync(string displayName,CancellationToken ct)
    {
        displayName=displayName.Trim();
        if(!ValidName(displayName))throw new ArgumentException("Display name not valid.");
        using var handler=new HttpClientHandler{AllowAutoRedirect=false};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "OSRS-Play/0.7.2 (public-hiscore-lookup-user-request)");
        using var response=await http.GetAsync(
            Source+"?player="+Uri.EscapeDataString(displayName),
            HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode==HttpStatusCode.NotFound)
            throw new InvalidOperationException("Kein öffentlicher Hiscore-Eintrag für diesen Namen.");
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>50_000)
            throw new FormatException("Hiscore response unexpectedly large.");
        var csv=await response.Content.ReadAsStringAsync(ct);
        return Parse(displayName,csv,DateTimeOffset.UtcNow);
    }
}

internal interface IAuthorizedOfficialGameAdapter
{
    // Future Jagex-approved adapter must provide authenticated world events.
    // Public hiscores and TCP connect probing DO NOT satisfy this contract.
    bool IsAuthenticated {get;}
    bool HasVerifiedWorldState {get;}
    int AuthenticatedWorld {get;}
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}

internal static class OfficialGameSessionGate
{
    internal static string Readiness(IAuthorizedOfficialGameAdapter? adapter)
    {
        if(adapter is null)return "OFFICIAL_PROTOCOL_ADAPTER_MISSING";
        if(!adapter.IsAuthenticated)return "OFFICIAL_AUTHENTICATION_MISSING";
        if(!adapter.HasVerifiedWorldState)return "OFFICIAL_WORLD_STATE_MISSING";
        if(adapter.AuthenticatedWorld<301 || adapter.AuthenticatedWorld>800)
            return "OFFICIAL_WORLD_ID_INVALID";
        return "OFFICIAL_SESSION_READY";
    }
}

internal static class PublicOsrsHiscoresTests
{
    private static void Require(bool value,string reason)
    {if(!value)throw new Exception("Official OSRS API: "+reason);}
    internal static void Run()
    {
        Require(PublicOsrsHiscores.OrderedSkills.Length==24,"sailing and skills");
        var lines=new List<string>{"1,24,0"};
        lines.AddRange(PublicOsrsHiscores.OrderedSkills.Select(_=>"1,1,0"));
        var parsed=PublicOsrsHiscores.Parse("Example",string.Join("\n",lines),
            DateTimeOffset.UnixEpoch);
        Require(parsed.TotalLevel==24&&parsed.Skills.Single(x=>x.Name=="Sailing").Level==1,
            "24-skill public account profile");
        Require(parsed.Skills.Single(x=>x.Name=="Mining").Level==1,"mining order");
        bool invalid=false;
        try{PublicOsrsHiscores.Parse("Example","1,24,0",DateTimeOffset.UnixEpoch);}
        catch(FormatException){invalid=true;}
        Require(invalid,"incomplete profile rejected");
        Require(!PublicOsrsHiscores.ValidName("abc?passwd=x"),"invalid display name");
        Require(OfficialGameSessionGate.Readiness(null)=="OFFICIAL_PROTOCOL_ADAPTER_MISSING",
            "public APIs cannot pass official login gate");
        Console.WriteLine("PUBLIC OSRS DATA PASS: 24 skills, Mining, Sailing, incomplete rejection, no fake login");
    }
}
