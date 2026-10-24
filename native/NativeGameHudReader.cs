namespace OSRSLiveDiagnostics;

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
    internal static VisualLabel? Read(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        if(image.Size!=new Size(1000,600)||!labels.Any(l=>l.Kind==VisualLabelKind.Minimap)||Skills(image) is null)return null;
        // Independent regions identify the native in-game layout. This is an
        // image marker, not fabricated OCR text or an account/auth assertion.
        bool Light(Color c)=>c.R>140&&c.G>140&&c.B>120;
        bool State(Color c)=>c.G>80&&c.G>c.R*1.3&&c.G>c.B*1.3||c.R>110&&c.R>c.B*1.4;
        foreach(int x in new[]{120,182,368})
            if(Count(image,new(x,575,61,14),Light)<8||Count(image,new(x,589,61,11),State)<3)return null;
        var report=new Rectangle(430,575,87,24);
        if(Count(image,report,c=>c.R>55&&c.R>c.G*1.3&&c.R>c.B*1.4)<300||Count(image,report,Light)<15)return null;
        return new("NATIVE_GAME_HUD",new(0,575,517,25),VisualLabelKind.GameHud);
    }
}
