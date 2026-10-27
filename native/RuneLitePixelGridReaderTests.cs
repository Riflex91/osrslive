namespace OSRSLiveDiagnostics;

internal static class RuneLitePixelGridReaderTests
{
    private static readonly Dictionary<char,string[]> Digits=new()
    {
        ['0']=["01110","10001","10011","10101","11001","10001","01110"],
        ['1']=["00100","01100","00100","00100","00100","00100","01110"],
        ['2']=["01110","10001","00001","00010","00100","01000","11111"],
        ['3']=["11110","00001","00001","01110","00001","00001","11110"],
        ['4']=["00010","00110","01010","10010","11111","00010","00010"],
        ['5']=["11111","10000","10000","11110","00001","00001","11110"],
        ['6']=["01110","10000","10000","11110","10001","10001","01110"],
        ['7']=["11111","00001","00010","00100","01000","01000","01000"],
        ['8']=["01110","10001","10001","01110","10001","10001","01110"],
        ['9']=["01110","10001","10001","01111","00001","00001","01110"]
    };

    private static Bitmap Fixture(int plane=0)
    {
        var image=new Bitmap(145,82);
        using(var g=Graphics.FromImage(image))g.Clear(Color.FromArgb(21,26,32));
        string[] lines={"Tile3143, 3210, "+plane,"Chunk ID   803217","Region ID   12594"};
        for(int row=0;row<lines.Length;row++)for(int col=0;col<lines[row].Length;col++)
        {
            int x0=7+col*7,y0=13+row*15;
            char symbol=lines[row][col];
            if(Digits.TryGetValue(symbol,out var digit))
            {
                for(int y=0;y<digit.Length;y++)for(int x=0;x<5;x++)
                    if(digit[y][x]=='1')image.SetPixel(x0+x+1,y0+y+2,Color.White);
            }
            else if(symbol==',')
            {
                image.SetPixel(x0+2,y0+8,Color.White);
                image.SetPixel(x0+3,y0+8,Color.White);
                image.SetPixel(x0+2,y0+9,Color.White);
                image.SetPixel(x0+3,y0+9,Color.White);
                image.SetPixel(x0+3,y0+10,Color.White);
            }
            else if(char.IsLetter(symbol))
            {
                for(int y=2;y<9;y++)image.SetPixel(x0+3,y0+y,Color.White);
            }
        }
        return image;
    }

    internal static void Run()
    {
        static void Require(bool valid,string detail)
        {if(!valid)throw new Exception("RuneLite pixel-grid OCR regression: "+detail);}
        var raw=new[]{"Tile3143» 3210, o","Chunk ID 803217","Region ID 12594"};
        var observation=RuneLitePixelGridReader.ReadObservation(raw);
        Require(observation is {X:3143,Y:3210,Chunk:"803217",Region:"12594"},
            "strict OCR coordinates and IDs are extracted without interpreting the wrong punctuation or plane");
        Require(RuneLiteGridReader.Parse(raw) is null,"ordinary OCR must still reject the malformed plane");
        Require(RuneLitePixelGridReader.ReadObservation(new[]{"Tile3143» 321ø, Ø",raw[1],raw[2]}) is null,
            "a partly illegible Y coordinate cannot train the pixel decoder");
        using(var bitmap=Fixture())
        {
            var candidates=new[]{observation!.Value,observation.Value};
            var result=RuneLitePixelGridReader.Read(bitmap,candidates,out string diagnostic);
            Require(result?.Tile==new WorldTile(3143,3210,0)&&diagnostic.Contains("PIXEL_GRID_VERIFIED"),
                "independently trained digit pixels, commas and strict IDs must verify a plane-0 bitmap");
            Require(RuneLitePixelGridReader.Read(bitmap,candidates.Take(1).ToArray(),out _) is null,
                "one OCR reading cannot authorize a pixel-derived position");
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation.Value,observation.Value with{X=3142}},out _) is null,
                "two disagreeing OCR positions cannot authorize a pixel-derived position");
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation.Value with{Chunk="803218"},observation.Value with{Chunk="803218"}},out _) is null,
                "wrong chunk identifier must fail pixel-coordinate checksum");
            // Both bitmap separators are independently checked for matching
            // commas, rather than normalizing any OCR letter into punctuation.
            for(int y=0;y<11;y++)for(int x=0;x<7;x++)
                bitmap.SetPixel(7+8*7+x,13+y,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(bitmap,candidates,out _) is null,
                "a missing visual comma must reject the position");
        }
        using(var bitmap=Fixture(2))
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation!.Value,observation.Value},out _)?.Tile.Plane==2,
                "the pixel decoder must preserve a real nonzero floor instead of guessing zero");
        using(var bitmap=Fixture())
        {
            for(int y=0;y<11;y++)for(int x=0;x<7;x++)
                bitmap.SetPixel(7+16*7+x,13+y,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(bitmap,new[]{observation!.Value,observation.Value},out _) is null,
                "an unreadable floor glyph must never be inferred from the current map region");
        }
        Console.WriteLine("RUNELITE PIXEL GRID PASS: per-frame digit templates, visible commas, two matching OCR IDs, X/Y agreement, checksum, actual floor and fail-closed mutations");
    }
}
