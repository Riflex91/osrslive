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
        // The synthetic 6 must have at least five distinct pixels from 8
        // and 5, matching the conservative separation already observed for
        // the real live ID glyphs. The old test-only 6 differed from 8 by 3.
        ['6']=["00110","01000","10000","11110","10001","10001","01110"],
        ['7']=["11111","00001","00010","00100","01000","01000","01000"],
        ['8']=["01110","10001","10001","01110","10001","10001","01110"],
        ['9']=["11110","10001","10001","11111","00001","00001","00001"]
    };

    private static Bitmap Fixture(int plane=0,int offsetX=0,int offsetY=0,int tileX=3143,int tileY=3210)
    {
        var image=new Bitmap(145+offsetX,82+offsetY);
        using(var g=Graphics.FromImage(image))g.Clear(Color.FromArgb(21,26,32));
        int chunk=((tileX>>3)<<11)|(tileY>>3),region=((tileX>>6)<<8)|(tileY>>6);
        string[] lines={$"Tile{tileX}, {tileY}, {plane}",$"Chunk ID   {chunk:D6}",$"Region ID   {region:D5}"};
        for(int row=0;row<lines.Length;row++)for(int col=0;col<lines[row].Length;col++)
        {
            int x0=7+offsetX+col*7,y0=13+offsetY+row*15;
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
        // The supplied full-window screenshot has exactly the same thresholded
        // 131x55 panel pixels as the cropped screenshot, translated +14,+53.
        // Neither private image is embedded in this synthetic regression.
        using(var offsetBitmap=Fixture(offsetX:14,offsetY:53))
            Require(RuneLitePixelGridReader.Read(offsetBitmap,
                new[]{observation!.Value,observation.Value},out _)?.Tile==new WorldTile(3143,3210,0),
                "the observed full-window offset must decode without assuming a cropped-panel origin");
        using(var offsetBitmap=Fixture(offsetX:118,offsetY:82))
        {
            var shifted=RuneLitePixelGridReader.Read(offsetBitmap,
                new[]{observation!.Value,observation.Value},out string detail);
            Require(shifted?.Tile==new WorldTile(3143,3210,0)&&detail.Contains("PIXEL_GRID_VERIFIED"),
                "a cropped screenshot origin must not be mistaken for the real RuneLite overlay offset");
        var moved=RuneLitePixelGridReader.ReadObservation(new[]{
            "Tile3144» 3211» o","Chunk ID 805265","Region ID 12594"});
        Require(moved is {X:3144,Y:3211,Chunk:"805265",Region:"12594"},
            "moved position must have an independent OCR hypothesis and both exact IDs");
        using(var movedBitmap=Fixture(tileX:3144,tileY:3211))
        {
            var result=RuneLitePixelGridReader.Read(movedBitmap,
                new[]{moved!.Value,moved.Value},out string movedDiagnostic);
            Require(result?.Tile==new WorldTile(3144,3211,0)&&
                    movedDiagnostic.Contains("repeated_unknown_layouts=")&&
                    !movedDiagnostic.Contains("repeated_unknown_layouts=0 "),
                "unknown ID digit 3 is allowed only when two Tile glyphs independently agree");
            // Break the second '3' in the Tile line. OCR alone may continue
            // claiming it, but two visibly different pixel shapes must fail.
            for(int dy=0;dy<11;dy++)for(int dx=0;dx<7;dx++)
                movedBitmap.SetPixel(7+10*7+dx,13+dy,Color.FromArgb(21,26,32));
            Require(RuneLitePixelGridReader.Read(movedBitmap,
                    new[]{moved.Value,moved.Value},out _) is null,
                "OCR claims cannot override a missing repeated unknown Tile glyph");
        }
        var singleton=RuneLitePixelGridReader.ReadObservation(new[]{
            "Tile3147» 3208» o","Chunk ID 805265","Region ID 12594"});
        Require(singleton is not null,"new moved position must retain valid OCR IDs");
        using(var bitmap=Fixture(tileX:3147,tileY:3208))
        {
            var result=RuneLitePixelGridReader.Read(bitmap,
                new[]{singleton!.Value,singleton.Value},out string diagnostic);
            Require(result is null&&diagnostic.Contains("unknown_singleton_rejects=")&&
                    !diagnostic.Contains("unknown_singleton_rejects=0 "),
                "a once-only unknown digit must remain blocked despite agreeing OCR passes");
        }
        }
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
