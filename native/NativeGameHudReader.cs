namespace OSRSLiveDiagnostics;

// Only the native chat-control bar and Report button can corroborate the
// Skills icon when the minimap's Hough-circle detector cannot find a circle.
// No absent minimap or server authentication is ever synthesized.
internal readonly record struct GameHudFooterPixels(
    bool Public,bool Private,bool Trade,bool ReportBackground,bool ReportCaption)
{
    internal bool Verified=>Public&&Private&&Trade&&ReportBackground&&ReportCaption;
}

internal static class NativeGameHudReader
{
    internal static readonly Rectangle SkillsButton=new(756,265,35,36);
    private static int Count(Bitmap image,Rectangle area,Func<Color,bool> match)
    {
        int n=0;for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)if(match(image.GetPixel(x,y)))n++;
        return n;
    }
    internal static VisualLabel? Skills(Bitmap image)
    {
        if(image.Size!=new Size(1000,600))return null;
        bool Red(Color c)=>c.R>100&&c.R>c.G*1.4&&c.R>c.B*1.4;
        bool Green(Color c)=>c.G>85&&c.G>c.R*1.3&&c.G>c.B*1.3;
        bool Blue(Color c)=>c.B>90&&c.B>c.R*1.3&&c.B>c.G*1.15;
        if(Count(image,SkillsButton,Red)<8||Count(image,SkillsButton,Green)<8||Count(image,SkillsButton,Blue)<8)return null;
        double Centre(Func<Color,bool> match)
        {long xSum=0;int n=0;for(int y=SkillsButton.Top;y<SkillsButton.Bottom;y++)for(int x=SkillsButton.Left;x<SkillsButton.Right;x++)if(match(image.GetPixel(x,y))){xSum+=x;n++;}return xSum/(double)n;}
        return Centre(Red)<Centre(Green)&&Centre(Green)<Centre(Blue)?new("SKILLS_ICON",SkillsButton,VisualLabelKind.SkillsButton):null;
    }
    // The official 1000x600 HUD draws a small pale, circular Character
    // Summary icon immediately beside Skills. Check the actual annular pixels
    // and its dark centre; this is never an authorization from tab geometry.
    // The caller must additionally require the native HUD and a valid skill
    // grid before requesting an identity-only tab switch.
    internal static VisualLabel? CharacterSummaryTab(Bitmap image)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return null;
        const int centreX=806,centreY=282;
        int bright=0,darkCentre=0;
        var quadrants=new int[4];
        for(int y=274;y<=290;y++)for(int x=796;x<=816;x++)
        {
            var c=image.GetPixel(x,y);
            int radius=(x-centreX)*(x-centreX)+(y-centreY)*(y-centreY);
            if(radius<=16&&Math.Min(c.R,Math.Min(c.G,c.B))<130)darkCentre++;
            if(radius is <24 or >115||Math.Min(c.R,Math.Min(c.G,c.B))<=185)continue;
            bright++;
            int quadrant=(x>centreX?1:0)+(y>centreY?2:0);
            quadrants[quadrant]++;
        }
        if(bright is <85 or >180||darkCentre<10||quadrants.Any(n=>n<16))return null;
        return new("CHARACTER_SUMMARY_ICON",CharacterSummaryReader.Tab,VisualLabelKind.CharacterSummaryButton);
    }
    internal static GameHudFooterPixels InspectFooter(Bitmap image)
    {
        if(image.Size!=DockedWindowLayout.GameSize)return default;
        bool Light(Color c)=>c.R>140&&c.G>140&&c.B>120;
        bool State(Color c)=>c.G>80&&c.G>c.R*1.3&&c.G>c.B*1.3||
            c.R>110&&c.R>c.B*1.4;
        bool Button(int x)=>Count(image,new(x,575,61,14),Light)>=8&&
            Count(image,new(x,589,61,11),State)>=3;
        var report=new Rectangle(430,575,87,24);
        bool background=Count(image,report,c=>
            c.R>55&&c.R>c.G*1.3&&c.R>c.B*1.4)>=300;
        bool caption=Count(image,report,Light)>=15;
        return new(Button(120),Button(182),Button(368),background,caption);
    }
    internal static VisualLabel? Read(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(image.Size!=DockedWindowLayout.GameSize||Skills(image) is null||
            !InspectFooter(image).Verified)return null;
        // Five independent footer regions plus the coloured Skills icon
        // identify the actual native game HUD even when Hough minimap
        // detection temporarily fails. A missing minimap remains missing:
        // navigation/health callers never receive a fabricated map.
        bool map=labels.Any(l=>l.Kind==VisualLabelKind.Minimap);
        return new(map?"NATIVE_GAME_HUD":"NATIVE_GAME_HUD_NO_MINIMAP",
            new(0,575,517,25),VisualLabelKind.GameHud);
    }
    internal static void SelfTest()
    {
        using var image=new Bitmap(1000,600);
        var noMap=Array.Empty<VisualLabel>();
        if(Read(image,noMap) is not null||InspectFooter(image).Verified)
            throw new Exception("Native HUD: blank capture cannot be a game HUD.");
        using(var g=Graphics.FromImage(image))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.Red,SkillsButton.X+3,SkillsButton.Y+12,6,5);
            g.FillRectangle(Brushes.Green,SkillsButton.X+15,SkillsButton.Y+12,6,5);
            g.FillRectangle(Brushes.Blue,SkillsButton.X+25,SkillsButton.Y+12,6,5);
        }
        if(Skills(image) is null||Read(image,noMap) is not null)
            throw new Exception("Native HUD: Skills icon alone cannot confirm gameplay.");
        using(var g=Graphics.FromImage(image))
        {
            foreach(int x in new[]{120,182,368})
            {
                g.FillRectangle(Brushes.White,x+4,579,7,3);
                g.FillRectangle(Brushes.Green,x+4,590,5,2);
            }
            g.FillRectangle(Brushes.DarkRed,432,577,65,9);
            g.FillRectangle(Brushes.White,435,578,6,4);
        }
        var verified=Read(image,noMap);
        if(!InspectFooter(image).Verified||verified?.Kind!=VisualLabelKind.GameHud||
            verified.Text!="NATIVE_GAME_HUD_NO_MINIMAP")
            throw new Exception("Native HUD: verified Skills and footer must survive a missing minimap.");
        if(CharacterSummaryTab(image) is not null)
            throw new Exception("Native HUD: missing Summary icon must not be inferred from other HUD anchors.");
        // Simulate the light annular icon as an independent pixel marker.
        for(int y=274;y<=290;y++)for(int x=796;x<=816;x++)
        {
            int r=(x-806)*(x-806)+(y-282)*(y-282);
            if(r is >=45 and <=80)image.SetPixel(x,y,Color.FromArgb(249,244,231));
        }
        if(CharacterSummaryTab(image)?.Kind!=VisualLabelKind.CharacterSummaryButton)
            throw new Exception("Native HUD: verified Summary raster was not found.");
        image.SetPixel(806,282,Color.White);
        // A broad white overlay cannot be mistaken for the annular icon.
        using(var g=Graphics.FromImage(image))
            g.FillRectangle(Brushes.White,796,274,21,17);
        if(CharacterSummaryTab(image) is not null)
            throw new Exception("Native HUD: featureless white region must not identify a Summary tab.");
        var frame=new VisualFrame(new IntPtr(1),new Rectangle(0,0,1000,600),
            DateTimeOffset.UtcNow,new[]{verified});
        if(GameScreenReader.Read(frame).Stage!=VisibleGameStage.InGame)
            throw new Exception("Native HUD: verified footer must classify only the game stage.");
        if(ExecutionVision.Read(image,new[]{verified})?.Navigation is not null)
            throw new Exception("Native HUD: no minimap may be invented for navigation.");
        using(var g=Graphics.FromImage(image))
            g.FillRectangle(Brushes.Black,430,575,87,24);
        if(Read(image,noMap) is not null||InspectFooter(image).Verified)
            throw new Exception("Native HUD: missing Report evidence must reject gameplay.");
        using var unsupported=new Bitmap(950,600);
        if(Read(unsupported,noMap) is not null||InspectFooter(unsupported).Verified)
            throw new Exception("Native HUD: unsupported geometry must reject gameplay.");
    }
}
