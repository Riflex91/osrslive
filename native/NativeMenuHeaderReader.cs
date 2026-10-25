using System.Drawing;

namespace OSRSLiveDiagnostics;

// Generic "Choose Option" raster from the original, unmodified OSRS menu:
// only the public UI heading was extracted from the test screenshot.
// No account, item, world or gameplay-image pixels are stored here.
// Windows OCR regularly loses the brown-grey glyphs on their black header bar.
internal static class NativeMenuHeaderReader
{
    private const int Width=93,Height=14;
    private const string HeaderBits=
        "38c000000000038000000003660000000000360030000030300000000003180180000181800000000018c00c30000c0f878787c780c63c780f1f6076666660660633330cccef03333331e330319998666678199999819f018cccc333336ccccccc0cc006c666199999c663c3c7c3e01c3e1cc78cc00000000000000180000000000000000000000c000000000000000000000060000000000000000000000300000000";
    private static readonly byte[] Template=Convert.FromHexString(HeaderBits);
    private const int InkPixels=306;
    internal static bool HasInk(int x,int y)
    {
        int index=y*Width+x;
        return (Template[index/8]&(1<<(7-index%8)))!=0;
    }

    private static bool Ink(Color c)=>
        c.R is >=66 and <=155&&c.G is >=57 and <=142&&
        c.B is >=40 and <=123&&c.R>=c.G+4&&c.G>=c.B+6;
    private static bool Backdrop(Color c)=>c.R<=28&&c.G<=28&&c.B<=28;

    internal static VisualLabel? Read(Bitmap image,Point anchor,VisualLabel examine,VisualLabel cancel)
    {
        if(image.Size!=DockedWindowLayout.GameSize||
            anchor.X<0||anchor.X>=image.Width||anchor.Y<0||anchor.Y>=image.Height||
            examine.Bounds.Top>=cancel.Bounds.Top||
            Math.Abs(examine.Bounds.Left-cancel.Bounds.Left)>26)return null;
        int minX=Math.Max(0,examine.Bounds.Left-20);
        int maxX=Math.Min(image.Width-Width,examine.Bounds.Left+12);
        int minY=Math.Max(0,Math.Max(examine.Bounds.Top-120,anchor.Y-38));
        int maxY=Math.Min(image.Height-Height,Math.Min(examine.Bounds.Top-14,anchor.Y+40));
        if(minX>maxX||minY>maxY)return null;
        int best=int.MinValue,second=int.MinValue,bestX=-1,bestY=-1;
        for(int y=minY;y<=maxY;y++)
        for(int x=minX;x<=maxX;x++)
        {
            int matchedInk=0,matchedBackground=0;
            for(int dy=0;dy<Height;dy++)
            for(int dx=0;dx<Width;dx++)
            {
                var c=image.GetPixel(x+dx,y+dy);
                if(HasInk(dx,dy))
                {if(Ink(c))matchedInk++;}
                else if(Backdrop(c))matchedBackground++;
            }
            // Both the actual glyph and the black surrounding header must
            // match. A plain brown inventory cell is never a text match.
            if(matchedInk<InkPixels*90/100||
                matchedBackground<(Width*Height-InkPixels)*95/100)continue;
            int score=matchedInk*2+matchedBackground;
            if(score>best)
            {
                if(bestX>=0&&(Math.Abs(x-bestX)>2||Math.Abs(y-bestY)>2))
                    second=Math.Max(second,best);
                best=score;bestX=x;bestY=y;
            }
            else if(bestX>=0&&(Math.Abs(x-bestX)>2||Math.Abs(y-bestY)>2))
                second=Math.Max(second,score);
        }
        if(bestX<0||(second!=int.MinValue&&best-second<45))return null;
        if(Math.Abs(bestX-examine.Bounds.Left)>22||
            bestY>=examine.Bounds.Top||bestY+Height>=cancel.Bounds.Top||
            anchor.X<bestX-20||anchor.X>bestX+Width+20||
            anchor.Y<bestY-25||anchor.Y>bestY+Height+20)
            return null;
        return new("Choose Option",new Rectangle(bestX,bestY,Width,Height));
    }

    internal static void SelfTest()
    {
        if(Template.Length!=(Width*Height+7)/8||
            Enumerable.Range(0,Height).Sum(y=>Enumerable.Range(0,Width)
                .Count(x=>HasInk(x,y)))!=InkPixels)
            throw new Exception("Native header raster mask damaged");
        using var bitmap=new Bitmap(1000,600);
        using(var g=Graphics.FromImage(bitmap))g.Clear(Color.FromArgb(70,61,51));
        const int x=715,y=335;
        using(var g=Graphics.FromImage(bitmap))
            g.FillRectangle(Brushes.Black,new Rectangle(705,326,130,26));
        for(int py=0;py<Height;py++)for(int px=0;px<Width;px++)
            if(HasInk(px,py))bitmap.SetPixel(x+px,y+py,Color.FromArgb(93,84,71));
        var examine=new VisualLabel("Examine Big bones",new Rectangle(713,400,118,12));
        var cancel=new VisualLabel("Cancel",new Rectangle(713,420,50,12));
        var anchor=new Point(776,328);
        if(Read(bitmap,anchor,examine,cancel)?.Text!="Choose Option")
            throw new Exception("Verified brown-grey native menu header not decoded");
        if(Read(bitmap,new Point(450,300),examine,cancel) is not null)
            throw new Exception("Unrelated click must not authorize a menu header");
        // A missing or damaged header must NEVER be supplied from menu
        // geometry or from Examine/Cancel alone.
        for(int py=0;py<Height;py++)for(int px=0;px<Width;px++)
            if(HasInk(px,py))bitmap.SetPixel(x+px,y+py,Color.Black);
        if(Read(bitmap,anchor,examine,cancel) is not null)
            throw new Exception("Missing native header accepted");
    }
}
