namespace OSRSLiveDiagnostics;

internal static class WorldPageReader
{
    internal static IReadOnlyList<VisualLabel> Read(Bitmap image,IReadOnlyList<VisualLabel> labels)
    {
        // Only the supported native 1000x600 layout, on a confirmed world-grid
        // screen. The page arrows are image controls without accessible text.
        if(image.Size!=DockedWindowLayout.GameSize||labels.Count(l=>l.Kind==VisualLabelKind.WorldCell)<20||
            !labels.Any(l=>l.Text.Contains("Select a world",StringComparison.OrdinalIgnoreCase)))
            return Array.Empty<VisualLabel>();
        var found=new List<VisualLabel>();
        foreach(bool next in new[]{false,true})
        {
            var box=new Rectangle(next?835:117,282,48,42);
            int grey=0,rows=0,minWidth=box.Width,maxWidth=0;
            for(int y=box.Top;y<box.Bottom;y++)
            {
                int row=0;
                for(int x=box.Left;x<box.Right;x++)
                {
                    var c=image.GetPixel(x,y);
                    if(c.R>95&&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<24)row++;
                }
                grey+=row;if(row>=12){rows++;minWidth=Math.Min(minWidth,row);maxWidth=Math.Max(maxWidth,row);}
            }
            if(grey is >=250 and <=1300&&rows is >=14 and <=36&&maxWidth-minWidth>=10)
                found.Add(new(next?"NEXT":"PREVIOUS",box,VisualLabelKind.WorldNavigation));
        }
        return found;
    }
}
