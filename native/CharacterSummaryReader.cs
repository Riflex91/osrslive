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
