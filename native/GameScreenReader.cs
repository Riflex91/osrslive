using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

// Grid columns are established from repeated ascending world IDs, not from
// the sorting header (which does not align with the native client's columns).
internal static class WorldGridReader
{
    internal static IReadOnlyList<VisualLabel> Classify(IReadOnlyList<VisualLabel> labels)
    {
        var title=labels.FirstOrDefault(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase));
        if(title is null)return Array.Empty<VisualLabel>();
        var candidates=labels.Where(l=>l.Kind==VisualLabelKind.Number&&
            l.Bounds.Top>title.Bounds.Bottom&&l.Bounds.Top<title.Bounds.Bottom+600&&
            int.TryParse(l.Text,out int n)&&n is >=301 and <=800).OrderBy(l=>l.Bounds.Left).ToArray();
        var columns=new List<List<VisualLabel>>();
        foreach(var item in candidates)
        {
            var column=columns.FirstOrDefault(c=>Math.Abs(c.Average(l=>l.Bounds.Left)-item.Bounds.Left)<=
                Math.Max(5,item.Bounds.Height*.7));
            if(column is null){column=new();columns.Add(column);}
            column.Add(item);
        }
        var found=new List<VisualLabel>();
        foreach(var column in columns)
        {
            var ordered=column.GroupBy(l=>l.Bounds.Top).Select(g=>g.First()).OrderBy(l=>l.Bounds.Top).ToArray();
            if(ordered.Length<6)continue;
            int ascending=0;
            for(int i=1;i<ordered.Length;i++)
            {
                int delta=int.Parse(ordered[i].Text)-int.Parse(ordered[i-1].Text);
                if(delta is >=1 and <=12)ascending++;
            }
            if(ascending<(ordered.Length-1)*.85)continue;
            found.AddRange(ordered.Select(l=>l with {Kind=VisualLabelKind.WorldCell}));
        }
        return found;
    }
}

internal enum VisibleGameStage { Unknown, Login, WorldSelection, Connecting, Welcome, InGame }
internal sealed record VisibleGameObservation(VisibleGameStage Stage,string? Character,
    IReadOnlyList<PublicOsrsSkill>? Skills,int? TotalLevel,int RecognizedSkills,DateTimeOffset ObservedUtc,int? ReadLevelSum=null);
internal sealed record SkillGridLayout(Rectangle Bounds,int TotalLevel,double Scale);

internal static class GameScreenReader
{
    // Native resizable skill panel: 3 columns x 8 rows. Numbers are base levels,
    // not boosted/drained current levels. Layout is anchored to "Total level".
    internal static readonly string[] GridSkills=
    {
        "Attack","Hitpoints","Mining","Strength","Agility","Smithing",
        "Defence","Herblore","Fishing","Ranged","Thieving","Cooking",
        "Prayer","Crafting","Firemaking","Magic","Fletching","Woodcutting",
        "Runecraft","Slayer","Farming","Construction","Hunter","Sailing"
    };
    private static readonly Regex Total=new(@"\bTotal\s*level\s*[:;]?\s*(\d{2,4})\b",RegexOptions.IgnoreCase);
    private static readonly Regex Prompt=new(@"^([A-Za-z0-9 _-]{1,12})\s*:\s*[*|]?$",RegexOptions.CultureInvariant);
    internal static SkillGridLayout? LocateSkills(IReadOnlyList<VisualLabel> labels,Size frameSize)
    {
        var totals=labels.Where(l=>l.Kind==VisualLabelKind.Text&&Total.IsMatch(l.Text)&&
            l.Bounds.Left>frameSize.Width*.5&&l.Bounds.Top>frameSize.Height*.5).ToArray();
        if(totals.Length!=1)return null;
        var anchor=totals[0];
        int total=int.Parse(Total.Match(anchor.Text).Groups[1].Value);
        if(total<32||total>2376)return null;
        double scale=Math.Clamp(Math.Round(anchor.Bounds.Height/10d),1,2);
        int width=(int)Math.Round(186*scale),height=(int)Math.Round(240*scale);
        var bounds=new Rectangle((int)Math.Round(anchor.Bounds.Left+anchor.Bounds.Width/2d-width/2d),
            (int)Math.Round(anchor.Bounds.Top-246*scale),width,height);
        return new Rectangle(Point.Empty,frameSize).Contains(bounds)?new(bounds,total,scale):null;
    }
    internal static VisibleGameObservation Read(VisualFrame frame)
    {
        var labels=frame.Labels;
        bool Has(string text)=>labels.Any(l=>l.Kind==VisualLabelKind.Text&&l.Text.Contains(text,StringComparison.OrdinalIgnoreCase));
        VisibleGameObservation Result(VisibleGameStage stage)=>new(stage,null,null,null,0,frame.CapturedUtc);
        var filters=labels.Where(l=>l.Bounds.Top>frame.ScreenBounds.Height-40&&
            l.Bounds.Left<frame.ScreenBounds.Width*.55).Select(l=>l.Text).ToArray();
        string row=string.Join(" ",filters);
        if(!new[]{"Public","Private","Trade","Report"}.All(s=>Regex.IsMatch(row,@"\b"+s+@"\b",RegexOptions.IgnoreCase)))
        {
            if(Has("Select a world"))return Result(VisibleGameStage.WorldSelection);
            if(Has("Connecting to server")||Has("Please wait"))return Result(VisibleGameStage.Connecting);
            if(Has("CLICK HERE TO PLAY"))return Result(VisibleGameStage.Welcome);
            if(Has("Play Now"))return Result(VisibleGameStage.Login);
            return Result(VisibleGameStage.Unknown);
        }
        var prompts=labels.Where(l=>l.Kind==VisualLabelKind.Text&&
            l.Bounds.Left<frame.ScreenBounds.Width*.45&&
            l.Bounds.Top>=frame.ScreenBounds.Height-90&&l.Bounds.Bottom<=frame.ScreenBounds.Height-20)
            .Select(l=>Prompt.Match(l.Text.Trim())).Where(m=>m.Success)
            .Select(m=>m.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string? character=prompts.Length==1&&PublicOsrsHiscores.ValidName(prompts[0])?prompts[0]:null;
        var grid=LocateSkills(labels,frame.ScreenBounds.Size);
        var values=labels.Where(l=>l.Kind==VisualLabelKind.SkillValue).ToArray();
        var skills=new List<PublicOsrsSkill>();
        foreach(string name in GridSkills)
        {
            var hits=values.Where(l=>l.Text.StartsWith(name+"=",StringComparison.Ordinal)).ToArray();
            if(hits.Length!=1||!int.TryParse(hits[0].Text[(name.Length+1)..],out int level)||level is <1 or >99)continue;
            skills.Add(new(name,level,-1,-1));
        }
        bool consistent=grid is not null&&skills.Count==24&&skills.Sum(s=>s.Level)==grid.TotalLevel;
        return new(VisibleGameStage.InGame,character,consistent?skills:null,grid?.TotalLevel,skills.Count,frame.CapturedUtc,
            skills.Count>0?skills.Sum(s=>s.Level):null);
    }
}

internal static class SkillGridOcr
{
    internal static async Task<IReadOnlyList<VisualLabel>> Read(Bitmap bitmap,OcrEngine engine,IReadOnlyList<VisualLabel> labels)
    {
        var layout=GameScreenReader.LocateSkills(labels,bitmap.Size);
        if(layout is null)return Array.Empty<VisualLabel>();
        // A contact sheet gives the tiny base-level digits to OCR at useful size.
        // Icons, current levels and chat are excluded from these crops.
        const int tileWidth=100,tileHeight=80;
        using var sheet=new Bitmap(tileWidth*6,tileHeight*4);
        using(var graphics=Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            for(int i=0;i<24;i++)
            {
                var crop=new Rectangle((int)Math.Round(layout.Bounds.Left+(i%3*62+44)*layout.Scale),
                    (int)Math.Round(layout.Bounds.Top+(i/3*30+14)*layout.Scale),
                    (int)Math.Round(18*layout.Scale),(int)Math.Round(15*layout.Scale));
                if(!new Rectangle(Point.Empty,bitmap.Size).Contains(crop))return Array.Empty<VisualLabel>();
                graphics.DrawImage(bitmap,new Rectangle(i%6*tileWidth+10,i/6*tileHeight+10,72,60),crop,GraphicsUnit.Pixel);
            }
        }
        var result=await VisualStartup.RecognizeRaw(sheet,engine);
        var numbers=new Dictionary<int,List<int>>();
        foreach(var line in result.Lines)
        foreach(var word in line.Words)
        {
            string text=word.Text.Trim().Replace('I','1').Replace('l','1').Replace('|','1').Replace('O','0');
            if(!int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out int level)||level is <1 or >99)continue;
            int col=(int)(word.BoundingRect.X+word.BoundingRect.Width/2)/tileWidth;
            int row=(int)(word.BoundingRect.Y+word.BoundingRect.Height/2)/tileHeight;
            if(col is <0 or >=6||row is <0 or >=4)continue;
            int index=row*6+col;
            if(!numbers.TryGetValue(index,out var values)){values=new();numbers[index]=values;}
            values.Add(level);
        }
        return numbers.Where(p=>p.Value.Count==1).Select(p=>new VisualLabel(
            GameScreenReader.GridSkills[p.Key]+"="+p.Value[0],layout.Bounds,VisualLabelKind.SkillValue)).ToArray();
    }
}

internal sealed class GameObservationTracker
{
    private string? candidate;
    private int repetitions;
    private DateTimeOffset candidateTimestamp;
    internal VisibleGameObservation? LastObservation {get;private set;}
    internal AccountGoalDecision? Plan {get;private set;}
    internal F2PActivityPlan? Activity {get;private set;}
    internal VisibleGameObservation? Confirmed {get;private set;}
    internal void Reset(){candidate=null;repetitions=0;candidateTimestamp=default;LastObservation=null;Confirmed=null;Plan=null;Activity=null;}
    internal void Observe(VisibleGameObservation observation,DateTimeOffset now)
    {
        LastObservation=observation;
        if(observation.Stage!=VisibleGameStage.InGame||observation.Character is null||
            observation.Skills is null||now-observation.ObservedUtc>TimeSpan.FromSeconds(3)||
            observation.ObservedUtc>now)
        {candidate=null;repetitions=0;Plan=null;Confirmed=null;Activity=null;return;}
        string signature=observation.Character.ToUpperInvariant()+":"+
            string.Join(",",observation.Skills.OrderBy(s=>s.Name,StringComparer.Ordinal).Select(s=>s.Name+"="+s.Level));
        if(signature==candidate)
        {
            if(observation.ObservedUtc-candidateTimestamp<TimeSpan.FromMilliseconds(500))return;
            repetitions++;
        }
        else{candidate=signature;repetitions=1;}
        candidateTimestamp=observation.ObservedUtc;
        if(repetitions<2){Plan=null;Confirmed=null;Activity=null;return;}
        Confirmed=observation;
        // The user requested F2P. This is a proposal, never a world/auth assertion.
        Plan=AccountGoalPlanner.Decide(observation.Skills,false,true);
        // Inventory, bank, quest and GE observers are not implemented yet.
        // Unknown facts stay unknown; the plan exposes exactly what is missing.
        Activity=F2PProgression.Plan(Plan.Skill,observation.Skills,new(),now);
    }
    internal void Expire(DateTimeOffset now)
    {
        if(LastObservation is not null&&now-LastObservation.ObservedUtc>TimeSpan.FromSeconds(6))
        {Plan=null;Confirmed=null;Activity=null;candidate=null;repetitions=0;}
    }
}

internal sealed class AccountProgressStore
{
    private readonly string directory;
    private string? lastSaved;
    internal AccountProgressStore(string directory){this.directory=Path.GetFullPath(directory);}
    internal bool Save(VisibleGameObservation observation,AccountGoalDecision plan)
    {
        if(observation.Character is null||observation.Skills is null)return false;
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(observation.Character.ToUpperInvariant()))).ToLowerInvariant();
        var skills=observation.Skills.ToDictionary(s=>s.Name,s=>s.Level);
        string contentKey=key+":"+JsonSerializer.Serialize(skills);
        if(contentKey==lastSaved)return false;
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,key+".json");
        var data=new{schemaVersion=1,characterHash=key,observedUtc=observation.ObservedUtc,
            source="REPEATED_VISUAL_OCR",serverAuthenticationVerified=false,
            totalLevel=observation.TotalLevel,skills,goal=plan.Phase.ToString(),nextSkill=plan.Skill};
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
        lastSaved=contentKey;
        return true;
    }
}
