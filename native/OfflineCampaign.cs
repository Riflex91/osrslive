using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

// Intentionally synthetic campaign: no credentials, servers, game inputs, or real account data.
internal sealed record CampaignCheckpoint(
    int Schema, string Source, int Steps, long Coins, long Reserve,
    Dictionary<string,int> Levels, string LastActivity);
internal sealed record CampaignStep(CampaignCheckpoint After, string Activity, bool LevelChanged);

internal static class OfflineCampaign
{
    internal const string Source="SYNTHETIC_FIXTURE";
    internal const int Schema=1;
    internal const int BatchSize=120;
    internal static CampaignCheckpoint Create()
    {
        var levels=GoalPlanner.Skills.ToDictionary(s=>s,s=>1,StringComparer.OrdinalIgnoreCase);
        return new(Schema,Source,0,100000,20000,levels,"none");
    }
    internal static void Validate(CampaignCheckpoint? state)
    {
        if(state is null || state.Schema!=Schema || state.Source!=Source ||
           state.Steps<0 || state.Coins<0 || state.Reserve<0 ||
           state.Coins<state.Reserve || state.Levels is null ||
           state.Levels.Count!=GoalPlanner.Skills.Length ||
           state.Levels.Any(x=>x.Value<1||x.Value>99) ||
           GoalPlanner.Skills.Any(x=>!state.Levels.ContainsKey(x)) ||
           state.LastActivity is null || state.LastActivity.Length>100)
            throw new InvalidDataException("Invalid or non-synthetic campaign checkpoint.");
    }
    internal static CampaignStep Step(CampaignCheckpoint prior)
    {
        Validate(prior);
        var profile=new GoalAccountProfile(prior.Levels,prior.Coins,prior.Reserve,
            true,new HashSet<string>());
        var methods=GoalPlanner.Skills.Select((skill,index)=>
            new GoalActivity("training_"+skill,skill,1,40000+(index%5)*2000,
                1000+index*200,0,false,null,false,null));
        var plan=GoalPlanner.Rank(profile,methods,GoalPreference.MaxSkills,DateTimeOffset.UtcNow);
        var choice=plan.Options.FirstOrDefault() ??
            throw new InvalidOperationException("Synthetic campaign has no eligible activities.");
        var nextLevels=new Dictionary<string,int>(prior.Levels,StringComparer.OrdinalIgnoreCase);
        var old=nextLevels[choice.Skill];
        nextLevels[choice.Skill]=Math.Min(99,old+1);
        // Synthetic reward: 100 fictitious coins per fixture step (not a market estimate).
        long coins=checked(prior.Coins+100);
        var after=prior with {Steps=checked(prior.Steps+1),Coins=coins,
            Levels=nextLevels,LastActivity=choice.Id};
        Validate(after);
        return new(after,choice.Id,nextLevels[choice.Skill]!=old);
    }
    internal static CampaignCheckpoint RunBatch(CampaignCheckpoint state,int count)
    {
        if(count<1||count>10000)throw new ArgumentOutOfRangeException(nameof(count));
        for(int n=0;n<count;n++)state=Step(state).After;
        return state;
    }
    internal static string CheckpointPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OSRSLiveDiagnostics","offline-campaign-synthetic.json");
    internal static CampaignCheckpoint LoadOrCreate(string path)
    {
        if(!File.Exists(path))return Create();
        try
        {
            var state=JsonSerializer.Deserialize<CampaignCheckpoint>(File.ReadAllText(path));
            Validate(state);
            return state!;
        }
        catch(Exception e) when(e is JsonException or InvalidDataException)
        {
            // Never silently overwrite a damaged checkpoint.
            throw new InvalidDataException("Synthetic checkpoint rejected; original preserved.",e);
        }
    }
    internal static void Save(string path,CampaignCheckpoint state)
    {
        Validate(state);
        string directory=Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temp=Path.Combine(directory,"."+Path.GetFileName(path)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temp,path,true);
        }
        finally {try {if(File.Exists(temp))File.Delete(temp);}catch{}}
    }
}

internal static class OfflineCampaignTests
{
    private static void Require(bool value,string label)
    {if(!value)throw new Exception("Offline campaign: "+label);}
    internal static void Run()
    {
        var initial=OfflineCampaign.Create();
        var result=OfflineCampaign.RunBatch(initial,120);
        Require(initial.Steps==0&&result.Steps==120,"step accounting");
        Require(result.Coins==initial.Coins+12000,"fictional GP accounting");
        Require(result.Levels.Values.Sum()==24+120,"progressed skills exactly one level per step");
        Require(result.Levels.Count==24&&result.Coins>=result.Reserve,"skill count and reserve");
        var folder=Path.Combine(Path.GetTempPath(),"osrslive-campaign-selftest-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"checkpoint.json");
        try
        {
            var firstHalf=OfflineCampaign.RunBatch(OfflineCampaign.Create(),60);
            OfflineCampaign.Save(path,firstHalf);
            var resumed=OfflineCampaign.RunBatch(OfflineCampaign.LoadOrCreate(path),60);
            Require(resumed.Steps==result.Steps && resumed.Coins==result.Coins &&
                resumed.Levels.All(x=>result.Levels[x.Key]==x.Value),"save/resume determinism");
            File.WriteAllText(path,"{invalid");
            bool failed=false;
            try{OfflineCampaign.LoadOrCreate(path);}catch(InvalidDataException){failed=true;}
            Require(failed,"damaged checkpoint must fail closed");
            Require(File.ReadAllText(path)=="{invalid","invalid file must be preserved");
            var maxed=OfflineCampaign.Create() with{
                Levels=GoalPlanner.Skills.ToDictionary(x=>x,x=>99,StringComparer.OrdinalIgnoreCase)
            };
            var profit=OfflineCampaign.Step(maxed);
            Require(!profit.LevelChanged && profit.After.Coins==maxed.Coins+100,
                "profit continues after maxing");
            var wrongSource=initial with{Source="LIVE"};
            bool rejected=false;
            try{OfflineCampaign.Save(path,wrongSource);}catch(InvalidDataException){rejected=true;}
            Require(rejected,"never persist live-labelled data as simulation");
            Console.WriteLine("OFFLINE CAMPAIGN PASS: goals, cash reserve, deterministic resume, corrupt reject, post-max");
        }
        finally {try{Directory.Delete(folder,true);}catch{}}
    }
}
