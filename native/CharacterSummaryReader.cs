using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

// Reads only the official Character Summary side panel. The highlighted
// summary tab and the name-colored heading must both be present. Never
// promote arbitrary chat, NPC, or player names to the account identity.
internal static class CharacterSummaryReader
{
    internal static readonly Rectangle Tab=new(793,266,35,33);
    internal static readonly Rectangle NameRegion=new(776,322,166,27);
    private static readonly Regex Name=new(@"^[A-Za-z0-9 _-]{1,12}$",RegexOptions.CultureInvariant);

    internal static bool IsOpen(Bitmap bitmap)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize)return false;
        int red=0,gold=0;
        for(int y=Tab.Top;y<Tab.Bottom;y++)
        for(int x=Tab.Left;x<Tab.Right;x++)
        {
            var c=bitmap.GetPixel(x,y);
            if(c.R>65&&c.R>c.G*1.6&&c.R>c.B*1.55)red++;
        }
        for(int y=NameRegion.Top;y<NameRegion.Bottom;y++)
        for(int x=NameRegion.Left;x<NameRegion.Right;x++)
        {
            var c=bitmap.GetPixel(x,y);
            if(c.R>160&&c.G>95&&c.G<220&&c.B<150&&
                c.R>c.G*1.25&&c.G>c.B*1.4)gold++;
        }
        return red>=80&&gold>=40;
    }

    internal static void SelfTest()
    {
        using var bitmap=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(50,44,38));
            g.FillRectangle(new SolidBrush(Color.FromArgb(106,36,27)),Tab);
            g.FillRectangle(new SolidBrush(Color.FromArgb(255,152,31)),
                new Rectangle(NameRegion.X+20,NameRegion.Y+7,75,5));
        }
        if(!IsOpen(bitmap))throw new Exception("Verified native summary panel not detected");
        bitmap.SetPixel(NameRegion.X+20,NameRegion.Y+7,Color.FromArgb(50,44,38));
        if(ParseName("Example_29")!="Example_29"||
            ParseName("Example_29: *") is not null||
            ParseName("MuchTooLongAccountName") is not null)
            throw new Exception("Summary identity must be an isolated OSRS display name");
        var tracker=new CharacterSummaryIdentity();
        var now=DateTimeOffset.UtcNow;
        var summaryLabels=new[]{
            new VisualLabel("CHARACTER_SUMMARY",Tab,VisualLabelKind.CharacterSummary),
            new VisualLabel("Example_29",NameRegion,VisualLabelKind.CharacterSummaryName)
        };
        var frame=new VisibleGameObservation(VisibleGameStage.InGame,null,null,null,0,now);
        tracker.Observe(frame,summaryLabels);
        if(tracker.Verified(now) is not null)throw new Exception("One summary frame cannot verify identity");
        tracker.Observe(frame,summaryLabels);
        if(tracker.Verified(now) is not null)throw new Exception("Repeated timestamp cannot verify identity");
        tracker.Observe(frame with {ObservedUtc=now.AddSeconds(1)},summaryLabels);
        if(tracker.Apply(frame with {ObservedUtc=now.AddSeconds(2)}).Character!="Example_29")
            throw new Exception("Independent summary frames not retained across Skills tab");
        if(tracker.Apply(frame with {ObservedUtc=now.AddSeconds(2),Character="Different"}).Character is not null)
            throw new Exception("Conflicting identity not rejected");
        if(tracker.Verified(now.AddSeconds(32)) is not null)
            throw new Exception("Stale summary identity accepted");
        tracker.Observe(frame with {Stage=VisibleGameStage.Disconnected},Array.Empty<VisualLabel>());
        if(tracker.Verified(now.AddSeconds(2)) is not null)
            throw new Exception("Disconnected account retained summary identity");
        if(GameScreenReader.LocateSkills(summaryLabels.Append(
            new VisualLabel("Total level: 131",new Rectangle(840,365,110,12))).ToArray(),new Size(1000,600)) is not null)
            throw new Exception("Summary total level was confused with a Skills raster");
        var fallback=new CharacterSummaryFallback();
        var skills=GameScreenReader.GridSkills.Select(name=>new PublicOsrsSkill(name,
            name=="Hitpoints"?24:1,-1,-1)).ToArray();
        var unassigned=new VisibleGameObservation(VisibleGameStage.InGame,null,skills,
            skills.Sum(s=>s.Level),24,now);
        var verifiedButtons=new[]{
            new VisualLabel("NATIVE_GAME_HUD",new Rectangle(0,575,517,25),VisualLabelKind.GameHud),
            new VisualLabel("CHARACTER_SUMMARY_ICON",Tab,VisualLabelKind.CharacterSummaryButton)
        };
        if(fallback.ShouldOpen(unassigned,verifiedButtons)||fallback.ShouldOpen(unassigned,verifiedButtons))
            throw new Exception("One skill screenshot cannot authorize opening the Summary tab");
        if(!fallback.ShouldOpen(unassigned with {ObservedUtc=now.AddSeconds(1)},verifiedButtons))
            throw new Exception("Two independent complete skill frames must enable identity fallback");
        fallback.Reset();
        if(fallback.ShouldOpen(unassigned,verifiedButtons.Take(1).ToArray())||
            fallback.ShouldOpen(unassigned with {ObservedUtc=now.AddSeconds(1)},verifiedButtons))
            throw new Exception("An unrecognized Summary icon must invalidate prior authorization");
        fallback.Reset();
        if(fallback.ShouldOpen(unassigned,verifiedButtons)||
            fallback.ShouldOpen(unassigned with {ObservedUtc=now.AddSeconds(9)},verifiedButtons))
            throw new Exception("Stale skill frames cannot authorize a Summary click");
        fallback.Reset();
        if(fallback.ShouldOpen(unassigned,verifiedButtons)||
            fallback.ShouldOpen(unassigned with {Character="Example_29",ObservedUtc=now.AddSeconds(1)},verifiedButtons)||
            fallback.ShouldOpen(unassigned with {ObservedUtc=now.AddSeconds(2)},verifiedButtons))
            throw new Exception("Known identity must invalidate a pending Summary fallback");
        fallback.Reset();
        if(fallback.ShouldOpen(unassigned,verifiedButtons)||
            fallback.ShouldOpen(unassigned with {TotalLevel=1,ObservedUtc=now.AddSeconds(1)},verifiedButtons))
            throw new Exception("An inconsistent total must not authorize identity fallback");
    }

    internal static string? ParseName(string candidate)
    {
        string name=candidate.Trim();
        return Name.IsMatch(name)&&PublicOsrsHiscores.ValidName(name)?name:null;
    }

    internal static async Task<IReadOnlyList<VisualLabel>> Read(Bitmap image,OcrEngine engine)
    {
        if(!IsOpen(image))return Array.Empty<VisualLabel>();
        var marker=new VisualLabel("CHARACTER_SUMMARY",Tab,VisualLabelKind.CharacterSummary);
        using var cropped=GameplayHudOcr.Contrast(image,NameRegion);
        var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int pass=0;pass<2&&candidates.Count==0;pass++)
        {
            const int scale=5;
            using var sheet=new Bitmap(cropped.Width*scale+40,cropped.Height*scale+40);
            using(var g=Graphics.FromImage(sheet))
            {
                g.Clear(Color.White);
                g.InterpolationMode=pass==0?
                    System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor:
                    System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(cropped,new Rectangle(20,20,cropped.Width*scale,cropped.Height*scale));
            }
            var read=await VisualStartup.RecognizeRaw(sheet,engine);
            foreach(var line in read.Lines)
            {
                var text=ParseName(line.Text);
                if(text is not null)candidates.Add(text);
            }
        }
        if(candidates.Count!=1)return new[]{marker};
        return new[]{marker,new VisualLabel(candidates.Single(),NameRegion,VisualLabelKind.CharacterSummaryName)};
    }
}

// This fallback authorizes only opening the verified, read-only Character
// Summary tab. It does not establish an identity, create a gameplay subject,
// or bypass the two-frame account/skill verification in GameObservationTracker.
internal sealed class CharacterSummaryFallback
{
    private string? signature;
    private DateTimeOffset lastFrame;
    private int frames;

    internal void Reset(){signature=null;lastFrame=default;frames=0;}

    internal bool ShouldOpen(VisibleGameObservation observation,IReadOnlyList<VisualLabel> labels)
    {
        var skills=observation.Skills;
        if(observation.Stage!=VisibleGameStage.InGame||observation.Character is not null||
            skills is null||skills.Count!=24||observation.RecognizedSkills!=24||
            observation.TotalLevel is null||skills.Sum(s=>s.Level)!=observation.TotalLevel||
            skills.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=24||
            labels.Count(l=>l.Kind==VisualLabelKind.GameHud)!=1||
            labels.Count(l=>l.Kind==VisualLabelKind.CharacterSummaryButton)!=1||
            labels.Any(l=>l.Kind==VisualLabelKind.CharacterSummary))
        {Reset();return false;}

        string current=string.Join(",",skills.OrderBy(s=>s.Name,StringComparer.Ordinal)
            .Select(s=>s.Name+"="+s.Level));
        DateTimeOffset at=observation.ObservedUtc;
        if(current!=signature||at<=lastFrame||at-lastFrame>TimeSpan.FromSeconds(8))
        {signature=current;frames=1;}
        else if(at-lastFrame>=TimeSpan.FromMilliseconds(500))frames++;
        lastFrame=at;
        return frames>=2;
    }
}

// Session-bound identity from two *different* image timestamps. The summary
// and the skill grid occupy different tabs, so the evidence is joined only
// briefly, in the same window, without persisting an account name in logs.
internal sealed class CharacterSummaryIdentity
{
    private string? pending,verified;
    private DateTimeOffset pendingAt,verifiedAt;
    internal string? Verified(DateTimeOffset now)=>
        verified is not null&&now>=verifiedAt&&now-verifiedAt<=TimeSpan.FromSeconds(30)?verified:null;
    internal void Reset(){pending=null;verified=null;pendingAt=default;verifiedAt=default;}

    internal void Observe(VisibleGameObservation observation,IReadOnlyList<VisualLabel> labels)
    {
        if(observation.Stage is VisibleGameStage.Login or VisibleGameStage.Welcome or
            VisibleGameStage.WorldSelection or VisibleGameStage.Disconnected)
        {Reset();return;}
        if(observation.Stage!=VisibleGameStage.InGame)return;
        var matches=labels.Where(l=>l.Kind==VisualLabelKind.CharacterSummaryName)
            .Select(l=>CharacterSummaryReader.ParseName(l.Text))
            .Where(n=>n is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(matches.Length!=1||!labels.Any(l=>l.Kind==VisualLabelKind.CharacterSummary))return;
        string name=matches[0]!;
        var at=observation.ObservedUtc;
        if(pending==name&&at>pendingAt.AddMilliseconds(300)&&at-pendingAt<TimeSpan.FromSeconds(8))
        {verified=name;verifiedAt=at;}
        else if(pending!=name||at-pendingAt>=TimeSpan.FromSeconds(8))
        {pending=name;verified=null;}
        pendingAt=at;
    }

    internal VisibleGameObservation Apply(VisibleGameObservation observation)
    {
        if(observation.Stage!=VisibleGameStage.InGame)return observation;
        string? name=Verified(observation.ObservedUtc);
        if(name is null)return observation;
        if(observation.Character is not null&&
            !observation.Character.Equals(name,StringComparison.OrdinalIgnoreCase))
            return observation with {Character=null}; // conflicting identity: block
        return observation with {Character=name};
    }
}
