using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace OSRSLiveDiagnostics;

// Second-pass OCR only after an independently verified Play Now action.
// Deliberately returns a few known non-secret UI phrases, never raw OCR text,
// account names, chat, tokens, or screenshots. A candidate phrase alone does
// not authorize input: VisualStartupPolicy also requires a Welcome anchor and
// the earlier verified world selection.
internal static class PostPlayScreenReader
{
    private static readonly Rectangle Region=new(180,100,640,415);
    private static readonly Regex ClickHere=new(
        @"^click\s+here\s+to\s+play\s*[!.]?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex Welcome=new(
        @"^welcome(?:\s+to\s+(?:old\s+school\s+)?runescape)?\s*[!.]?$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex Connecting=new(
        @"^(?:connecting\s+to\s+server|please\s+wait|loading(?:\s*\.\s*\.\s*\.)?)\s*[!.]?$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    internal const string ClickText="CLICK HERE TO PLAY";
    internal const string WelcomeText="Welcome";
    internal const string ConnectingText="Connecting to server";

    private static string Normalize(string input)=>
        Regex.Replace(input.Trim(),@"\s+"," ");

    internal static IReadOnlyList<VisualLabel> MatchKnownPhrases(
        IReadOnlyList<VisualLabel> labels,Size size)
    {
        if(size!=DockedWindowLayout.GameSize)return Array.Empty<VisualLabel>();
        var output=new List<VisualLabel>();
        foreach(var label in labels)
        {
            // Only consider centered post-login UI, never chat or sidebars.
            if(!Region.Contains(label.Bounds)||label.Bounds.Width<30||label.Bounds.Height<6)
                continue;
            string line=Normalize(label.Text);
            string? known=ClickHere.IsMatch(line)?ClickText:
                Welcome.IsMatch(line)?WelcomeText:
                Connecting.IsMatch(line)?ConnectingText:null;
            if(known is null)continue;
            output.Add(new(known,label.Bounds));
        }
        // If two disconnected regions claim the same actionable phrase, don't
        // choose one; let the runtime remain in observation-only state.
        if(output.Count(l=>l.Text==ClickText)>1)
            output.RemoveAll(l=>l.Text==ClickText);
        if(output.Count(l=>l.Text==WelcomeText)>1)
            output.RemoveAll(l=>l.Text==WelcomeText);
        return output;
    }

    internal static async Task<IReadOnlyList<VisualLabel>> Read(
        Bitmap bitmap,OcrEngine engine,IReadOnlyList<VisualLabel> existing)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize||
            existing.Any(l=>l.Text.Contains("password",StringComparison.OrdinalIgnoreCase)||
                l.Text.Contains("authenticator",StringComparison.OrdinalIgnoreCase)||
                l.Text.Contains("verification code",StringComparison.OrdinalIgnoreCase)||
                l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase)))
            return Array.Empty<VisualLabel>();
        // Only this bounded region is enlarged. This avoids inventory, private
        // chat, account name and footer content being sent into this OCR pass.
        int factor=(int)Math.Clamp((long)OcrEngine.MaxImageDimension/Region.Width,1L,3L);
        using var enlarged=new Bitmap(Region.Width*factor,Region.Height*factor);
        using(var g=Graphics.FromImage(enlarged))
        {
            g.Clear(Color.Black);
            g.InterpolationMode=InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=PixelOffsetMode.Half;
            g.DrawImage(bitmap,new Rectangle(Point.Empty,enlarged.Size),Region,GraphicsUnit.Pixel);
        }
        var result=await VisualStartup.RecognizeRaw(enlarged,engine);
        var candidates=new List<VisualLabel>();
        foreach(var line in result.Lines)
        {
            if(line.Words.Count==0)continue;
            var union=line.Words.Select(w=>new Rectangle(
                Region.X+(int)Math.Floor(w.BoundingRect.X/factor),
                Region.Y+(int)Math.Floor(w.BoundingRect.Y/factor),
                (int)Math.Ceiling(w.BoundingRect.Width/factor),
                (int)Math.Ceiling(w.BoundingRect.Height/factor))).Aggregate(Rectangle.Union);
            // Each phrase must occur within one OCR line, not through an
            // arbitrary combination of unrelated screen text.
            candidates.Add(new(string.Join(" ",line.Words.Select(w=>w.Text)),union));
        }
        return MatchKnownPhrases(candidates,bitmap.Size);
    }

    internal static void SelfTest()
    {
        var labels=new[]{
            new VisualLabel("Click  here to   play!",new Rectangle(410,265,180,25)),
            new VisualLabel("Welcome to RuneScape",new Rectangle(395,211,210,20)),
            new VisualLabel("Connecting to server",new Rectangle(405,315,180,18)),
            new VisualLabel("Click here to play",new Rectangle(15,555,150,18)),
            new VisualLabel("Password",new Rectangle(380,240,150,20))
        };
        var result=MatchKnownPhrases(labels,DockedWindowLayout.GameSize);
        if(!result.Any(l=>l.Text==ClickText)||!result.Any(l=>l.Text==WelcomeText)||
            !result.Any(l=>l.Text==ConnectingText)||result.Count!=3)
            throw new Exception("Post-play OCR: centered exact phrases were not isolated.");
        if(MatchKnownPhrases(labels,new Size(1024,768)).Count!=0)
            throw new Exception("Post-play OCR accepted an unsupported viewport.");
        var dup=new[]{
            new VisualLabel(ClickText,new Rectangle(400,250,150,20)),
            new VisualLabel(ClickText,new Rectangle(410,320,150,20))
        };
        if(MatchKnownPhrases(dup,DockedWindowLayout.GameSize).Count!=0)
            throw new Exception("Post-play OCR accepted ambiguous actionable text.");
        if(MatchKnownPhrases(new[]{new VisualLabel("Click the link here to play",
            new Rectangle(400,270,220,20))},DockedWindowLayout.GameSize).Count!=0)
            throw new Exception("Post-play OCR fabricated a command from unrelated text.");
        Console.WriteLine("POST PLAY READER PASS: exact centered phrases, ambiguity/viewport/privacy guards");
    }
}
