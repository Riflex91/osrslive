using System.Drawing;

namespace OSRSLiveDiagnostics;

// Pixel masks for two unmodified official OSRS bitmap fonts.
// Derived from RuneStar/fonts, CC0-1.0:
// https://github.com/RuneStar/fonts (Plain 11 and Plain 12).
// No executable hooks, memory access, game-state APIs or account identifiers.
internal static class NativeSkillFontReader
{
    private static readonly string[][] Digits=new[]{
        ".###./#...#/#...#/#...#/#...#/#...#/#...#/.###.",
        ".#./##./.#./.#./.#./.#./.#./###",
        ".###./#...#/....#/...#./..#../.#.../#..../#####",
        ".##./#..#/...#/.##./...#/...#/#..#/.##.",
        "#.../#.../#.../#.#./#.#./####/..#./..#.",
        "####/#.../#.../###./...#/...#/#..#/.##.",
        "..##./.#..#/#..../#.##./##..#/#...#/#...#/.###.",
        "####/...#/..#./..#./.#../.#../#.../#...",
        ".###./#...#/#...#/.###./#...#/#...#/#...#/.###.",
        ".###./#...#/#...#/.#..#/..###/....#/....#/....#"
    }.Select(s=>s.Split('/')).ToArray();

    private static bool Yellow(Color c)=>
        c.R>160&&c.G>140&&c.B<130&&c.R>c.B*1.5&&c.G>c.B*1.5;

    internal static int? ReadLevel(Bitmap bitmap,SkillGridLayout layout,int index)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize||layout.Scale!=1||index is <0 or >=24)return null;
        int x0=layout.Bounds.Left+(index%3*62+30);
        int y0=layout.Bounds.Top+(index/3*30+15);
        var region=new Rectangle(x0,y0,30,16);
        if(!new Rectangle(Point.Empty,bitmap.Size).Contains(region))return null;
        var mask=new bool[30,16];
        for(int y=0;y<16;y++)for(int x=0;x<30;x++)
            mask[x,y]=Yellow(bitmap.GetPixel(x0+x,y0+y));
        // Segment actual visible yellow glyphs, never infer digits from the
        // stat's cell position or the Total level checksum.
        var runs=new List<(int Left,int Right)>();
        for(int x=0;x<30;x++)
        {
            bool used=false;
            for(int y=0;y<16;y++)if(mask[x,y]){used=true;break;}
            if(!used)continue;
            if(runs.Count>0&&runs[^1].Right==x)runs[^1]=(runs[^1].Left,x+1);
            else runs.Add((x,x+1));
        }
        if(runs.Count is <1 or >2)return null;
        int value=0;
        int? glyphY=null;
        foreach(var run in runs)
        {
            int top=16,bottom=0;
            for(int x=run.Left;x<run.Right;x++)
            for(int y=0;y<16;y++)if(mask[x,y])
            {top=Math.Min(top,y);bottom=Math.Max(bottom,y+1);}
            int w=run.Right-run.Left,h=bottom-top;
            if(h!=8||(glyphY is not null&&glyphY.Value!=top))return null;
            glyphY=top;
            int bestDigit=-1,bestErrors=int.MaxValue,secondErrors=int.MaxValue;
            for(int digit=0;digit<10;digit++)
            {
                var p=Digits[digit];
                if(p.Length!=h||p[0].Length!=w)continue;
                int errors=0;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    if(mask[run.Left+x,top+y]!=(p[y][x]=='#'))errors++;
                if(errors<bestErrors)
                {secondErrors=bestErrors;bestErrors=errors;bestDigit=digit;}
                else secondErrors=Math.Min(secondErrors,errors);
            }
            if(bestDigit<0||bestErrors>0||secondErrors==bestErrors)return null;
            value=value*10+bestDigit;
        }
        return value is >=1 and <=99?value:null;
    }

    internal static IReadOnlyList<VisualLabel> Read(Bitmap bitmap,SkillGridLayout layout)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize||layout.Scale!=1)return Array.Empty<VisualLabel>();
        var found=new List<VisualLabel>();
        for(int i=0;i<24;i++)
        {
            int? level=ReadLevel(bitmap,layout,i);
            if(level is int n)found.Add(new(GameScreenReader.GridSkills[i]+"="+n,
                layout.Bounds,VisualLabelKind.SkillValue));
        }
        return found;
    }

    internal static void SelfTest()
    {
        using var b=new Bitmap(1000,600);
        var layout=new SkillGridLayout(new Rectangle(752,302,186,240),131,1);
        int[] nativeLevels={23,24,1,23,1,1,23,1,2,1,1,7,11,1,2,1,1,1,1,1,1,1,1,1};
        using(var g=Graphics.FromImage(b))g.Clear(Color.FromArgb(55,55,55));
        for(int i=0;i<24;i++)
        {
            string value=nativeLevels[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            int x=layout.Bounds.Left+i%3*62+41;
            int y=layout.Bounds.Top+i/3*30+18;
            int offset=0;
            foreach(char c in value)
            {
                var lines=Digits[c-'0'];
                for(int yy=0;yy<8;yy++)for(int xx=0;xx<lines[yy].Length;xx++)
                    if(lines[yy][xx]=='#')b.SetPixel(x+offset+xx,y+yy,Color.Yellow);
                offset+=lines[0].Length+2;
            }
        }
        var read=Read(b,layout);
        if(read.Count!=24||read.Sum(v=>int.Parse(v.Text[(v.Text.IndexOf('=')+1)..]))!=131||
            read.Single(v=>v.Text.StartsWith("Hitpoints=",StringComparison.Ordinal)).Text!="Hitpoints=24")
            throw new Exception("Native P11 base-level extraction failed");
        // A damaged glyph must stay unread; a total-level difference cannot fill it.
        b.SetPixel(layout.Bounds.Left+62+41,layout.Bounds.Top+18,Color.Black);
        if(ReadLevel(b,layout,1) is not null)
            throw new Exception("Damaged Hitpoints glyph must not be accepted");
    }
}

internal static class NativeCharacterFontReader
{
    private sealed record Glyph(char Symbol,int Width,int Height,int Advance,int Left,int Top,string Pattern)
    {
        internal readonly string[] Rows=Pattern.Split('/');
    }

    // P12 (chatbox) glyphs, unaltered from the CC0 native pixel-font source.
    private static readonly Glyph[] Glyphs=new Glyph[]
    {
        new('A',6,10,8,1,2,"..##../.#..#./#....#/#....#/######/#....#/#....#/#....#/#....#/#....#"),
        new('B',5,10,7,1,2,"####./#...#/#...#/#...#/####./#...#/#...#/#...#/#...#/####."),
        new('C',5,10,7,1,2,"..##./.#..#/#..../#..../#..../#..../#..../#..../.#..#/..##."),
        new('D',5,10,7,1,2,"###../#..#./#...#/#...#/#...#/#...#/#...#/#...#/#...#/####."),
        new('E',5,10,6,1,2,"#####/#..../#..../#..../###../#..../#..../#..../#..../#####"),
        new('F',5,10,6,1,2,"#####/#..../#..../#..../###../#..../#..../#..../#..../#...."),
        new('G',6,10,8,1,2,"..###./.#...#/#...../#...../#..##./#....#/#....#/#....#/.#...#/..###."),
        new('H',5,10,7,1,2,"#...#/#...#/#...#/#...#/#####/#...#/#...#/#...#/#...#/#...#"),
        new('I',3,10,3,0,2,"###/.#./.#./.#./.#./.#./.#./.#./.#./###"),
        new('J',6,10,7,1,2,"######/...#../...#../...#../...#../...#../...#../#..#../#..#../.##..."),
        new('K',6,10,7,1,2,"#...#./#..#../#.#.../##..../#...../##..../#.#.../#..#../#...#./#....#"),
        new('L',5,10,6,1,2,"#..../#..../#..../#..../#..../#..../#..../#..../#..../#####"),
        new('M',7,10,9,1,2,"#.....#/#.....#/##...##/#.#.#.#/#..#..#/#.....#/#.....#/#.....#/#.....#/#.....#"),
        new('N',6,10,8,1,2,"#....#/##...#/#.#..#/#.#..#/#..#.#/#..#.#/#..#.#/#...##/#...##/#....#"),
        new('O',6,10,8,1,2,"..##../.#..#./#....#/#....#/#....#/#....#/#....#/#....#/.#..#./..##.."),
        new('P',5,10,7,1,2,"###../#..#./#...#/#...#/#..#./###../#..../#..../#..../#...."),
        new('Q',6,10,8,1,2,"..##../.#..#./#....#/#....#/#....#/#....#/#.#..#/#..#.#/.#..#./..##.#"),
        new('R',5,10,7,1,2,"####./#...#/#...#/#...#/#..#./###../#.#../#..#./#...#/#...#"),
        new('S',5,10,7,1,2,"..##./.#..#/#..../.#.../..#../...#./....#/....#/#..#./.##.."),
        new('T',5,10,5,0,2,"#####/..#../..#../..#../..#../..#../..#../..#../..#../..#.."),
        new('U',6,10,8,1,2,"#....#/#....#/#....#/#....#/#....#/#....#/#....#/#....#/.#..##/..##.#"),
        new('V',5,10,7,1,2,"#...#/#...#/#...#/#...#/#...#/.#.#./.#.#./.#.#./..#../..#.."),
        new('W',7,10,9,1,2,"#.....#/#.....#/#.....#/#.....#/#..#..#/#..#..#/#..#..#/#..#..#/.#.#.#./..###.."),
        new('X',5,10,7,1,2,"#...#/#...#/.#.#./.#.#./..#../.#.#./.#.#./#...#/#...#/#...#"),
        new('Y',5,10,7,1,2,"#...#/#...#/#...#/.#.#./..#../..#../..#../..#../..#../..#.."),
        new('Z',5,10,7,1,2,"#####/....#/...#./...#./..#../.#.../.#.../#..../#..../#####"),
        new('a',5,6,7,1,6,".###./#...#/.####/#...#/#...#/.####"),
        new('b',5,10,7,1,2,"#..../#..../#..../#..../###../#..#./#...#/#...#/#...#/####."),
        new('c',4,6,6,1,6,".###/#.../#.../#.../#.../.###"),
        new('d',5,10,7,1,2,"....#/....#/....#/....#/..###/.#..#/#...#/#...#/#...#/.####"),
        new('e',5,6,7,1,6,".###./#...#/#...#/####./#..../.####"),
        new('f',5,11,6,1,4,"..##./.#..#/.#.../.#.../###../.#.../.#.../.#.../.#.../.#.../#...."),
        new('g',5,10,7,1,6,".###./#...#/#...#/#...#/#...#/.####/....#/....#/#...#/.###."),
        new('h',5,10,7,1,2,"#..../#..../#..../#..../#.##./##..#/#...#/#...#/#...#/#...#"),
        new('i',1,7,3,1,5,"#/./#/#/#/#/#"),
        new('j',4,11,6,1,5,"...#/..../...#/...#/...#/...#/...#/...#/#..#/#..#/.##."),
        new('k',5,10,6,1,2,"#..../#..../#..../#..../#..#./#.#../##.../#.#../#..#./#...#"),
        new('l',1,10,3,1,2,"#/#/#/#/#/#/#/#/#/#"),
        new('m',7,6,9,1,6,".##.##./#..#..#/#..#..#/#..#..#/#..#..#/#..#..#"),
        new('n',5,6,7,1,6,"#.##./##..#/#...#/#...#/#...#/#...#"),
        new('o',5,6,7,1,6,".###./#...#/#...#/#...#/#...#/.###."),
        new('p',5,10,7,1,6,".###./#...#/#...#/#...#/#...#/####./#..../#..../#..../#...."),
        new('q',5,10,7,1,6,"..##./.#..#/#...#/#...#/.#..#/..###/....#/....#/....#/....#"),
        new('r',3,6,5,1,6,"#.#/##./#../#../#../#.."),
        new('s',5,6,7,1,6,".####/#..../.###./....#/....#/####."),
        new('t',3,9,5,1,3,"#../#../#../###/#../#../#../#../.##"),
        new('u',5,6,7,1,6,"#...#/#...#/#...#/#...#/#...#/.###."),
        new('v',5,6,7,1,6,"#...#/#...#/.#.#./.#.#./..#../..#.."),
        new('w',5,6,7,1,6,"#...#/#...#/#.#.#/#.#.#/#.#.#/.###."),
        new('x',5,6,7,1,6,"#...#/.#.#./..#../.#.#./.#.#./#...#"),
        new('y',5,10,7,1,6,"#...#/#...#/#...#/#...#/.####/....#/....#/....#/#..#./.##.."),
        new('z',5,6,7,1,6,"#####/...#./..#../.#.../#..../#####"),
        new('0',6,10,8,1,2,"..##../.#..#./#..#.#/#..#.#/#..#.#/#.#..#/#.#..#/#.#..#/.#..#./..##.."),
        new('1',5,10,6,1,2,"..#../.##../#.#../..#../..#../..#../..#../..#../..#../#####"),
        new('2',6,10,8,1,2,".###../#...#./.....#/.....#/....#./...#../..#.../.#..../#...../######"),
        new('3',5,10,7,1,2,".###./#...#/....#/....#/.###./....#/....#/....#/#...#/.###."),
        new('4',6,10,7,1,2,"#...../#...../#...../#...../#..#../#..#../#..#../######/...#../...#.."),
        new('5',5,10,7,1,2,"#####/#..../#..../###../...#./....#/....#/....#/#...#/.###."),
        new('6',6,10,8,1,2,"..##../.#..#./#...../#...../#.##../##..#./#....#/#....#/.#..#./..##.."),
        new('7',5,10,7,1,2,"#####/....#/...#./...#./..#../..#../.#.../.#.../#..../#...."),
        new('8',6,10,8,1,2,"..##../.#..#./#....#/.#..#./..##../.#..#./#....#/#....#/.#..#./..##.."),
        new('9',6,10,8,1,2,".####./#....#/#....#/#....#/.#...#/..####/.....#/.....#/.....#/.....#"),
        new(' ',0,0,3,0,0,""),
        new('_',7,1,9,1,11,"#######"),
        new('-',4,1,6,1,7,"####"),
        new(':',1,6,3,1,6,"#/././././#"),
        new('*',7,6,9,1,2,"...#.../...#.../#######/...#.../..#.#../.#...#."),
    };
    private static readonly Dictionary<char,Glyph> ByChar=Glyphs.ToDictionary(g=>g.Symbol);
    private static readonly char[] NameChars="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 _-".ToCharArray();
    private const int RegionWidth=240,RegionTop=550,RegionHeight=25;

    internal static VisualLabel? Read(Bitmap bitmap)
    {
        if(bitmap.Size!=DockedWindowLayout.GameSize)return null;
        var white=new bool[RegionWidth,RegionHeight];
        var blue=new bool[RegionWidth,RegionHeight];
        for(int y=0;y<RegionHeight;y++)for(int x=0;x<RegionWidth;x++)
        {
            Color c=bitmap.GetPixel(x,RegionTop+y);
            int mx=Math.Max(c.R,Math.Max(c.G,c.B));
            int mn=Math.Min(c.R,Math.Min(c.G,c.B));
            white[x,y]=mn>140&&mx-mn<65;
            blue[x,y]=c.B>170&&c.B>c.R+60&&c.B>c.G+60;
        }

        var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(int baseline in Enumerable.Range(565,5))
        foreach(int startPen in Enumerable.Range(7,8))
        {
            var states=new List<(int Pen,string Name)>{(startPen,"")};
            for(int length=1;length<=12&&states.Count>0;length++)
            {
                var next=new List<(int Pen,string Name)>();
                foreach(var state in states)
                foreach(char c in NameChars)
                {
                    var glyph=ByChar[c];
                    if(Score(white,state.Pen,baseline,glyph)!=0)continue;
                    int nextPen=state.Pen+glyph.Advance;
                    string name=state.Name+c;
                    if(PublicOsrsHiscores.ValidName(name))
                        next.Add((nextPen,name));
                }
                if(next.Count>128)break; // ambiguous image: fail closed
                states=next;
                foreach(var state in states)
                {
                    var colon=ByChar[':'];
                    if(Score(white,state.Pen,baseline,colon)!=0)continue;
                    int starPen=state.Pen+colon.Advance+ByChar[' '].Advance;
                    if(Score(blue,starPen,baseline,ByChar['*'])!=0)continue;
                    candidates.Add(state.Name);
                    if(candidates.Count>1)return null;
                }
            }
        }
        if(candidates.Count!=1)return null;
        string selected=candidates.Single();
        return new(selected+": *",new Rectangle(7,554,224,21),VisualLabelKind.CharacterPrompt);
    }

    private static int Score(bool[,] mask,int pen,int baseline,Glyph glyph)
    {
        if(pen<0||pen+glyph.Advance>=RegionWidth)return int.MaxValue;
        int top=baseline-12;
        if(top<RegionTop||baseline+4>=RegionTop+RegionHeight)return int.MaxValue;
        int errors=0;
        for(int y=top;y<=baseline+4;y++)
        for(int x=pen;x<pen+glyph.Advance;x++)
        {
            int gx=x-(pen+glyph.Left),gy=y-(top+glyph.Top);
            bool on=gy>=0&&gy<glyph.Height&&gx>=0&&gx<glyph.Width&&
                glyph.Rows[gy][gx]=='#';
            if(mask[x,y-RegionTop]!=on)errors++;
            if(errors>0)return errors; // exact native raster glyph only
        }
        return 0;
    }

    internal static void SelfTest()
    {
        using var bitmap=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(bitmap))g.Clear(Color.FromArgb(32,67,19));
        const int penStart=10,baseline=567;
        int pen=penStart;
        foreach(char ch in "Example_29: *")
        {
            var glyph=ByChar[ch];
            var color=ch=='*'?Color.FromArgb(144,144,255):Color.White;
            for(int y=0;y<glyph.Height;y++)
            for(int x=0;x<glyph.Width;x++)if(glyph.Rows[y][x]=='#')
                bitmap.SetPixel(pen+glyph.Left+x,baseline-12+glyph.Top+y,color);
            pen+=glyph.Advance;
        }
        var read=Read(bitmap);
        if(GameScreenReader.ParseCharacterPrompt(read?.Text??"")!="Example_29")
            throw new Exception("Native P12 prompt glyphs / colored cursor rejected");
        bitmap.SetPixel(10+1+2,567-12+2,Color.Black);
        if(Read(bitmap) is not null)
            throw new Exception("Damaged native character glyph must not authorize a name");
    }
}
