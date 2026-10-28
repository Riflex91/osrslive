namespace OSRSLiveDiagnostics;

// The player dot stays at the minimap centre while the map moves beneath it.
// The old 15px ring detector found a movable map icon, not the player.
// This anchor is derived from the user-verified fixed-mode RuneLite HUD.
internal static class RuneLiteMinimapMarker
{
    // Client coordinates WITHOUT the Windows title bar. The user's red cross
    // is at (665,137) in a screenshot with a roughly 27px title bar.
    internal static readonly Point FixedModePlayerCenter=new(665,110);

    internal static bool TryFixedCenter(Size client,out Point center,out string diagnostic)
    {
        center=Point.Empty;
        // Only the observed fixed-mode layout. An open side panel, stretched
        // HUD or different scaling requires explicit manual calibration.
        if(client.Width is <766 or >778||client.Height is <525 or >545)
        {
            diagnostic=$"anchor_result=UNSUPPORTED_LAYOUT client={client.Width}x{client.Height} "+
                "manual_user_verification_required=true ring_detection_disabled=true";
            return false;
        }
        center=FixedModePlayerCenter;
        diagnostic=$"anchor_result=FIXED_MODE_USER_VERIFIED center_x={center.X} center_y={center.Y} "+
            $"client={client.Width}x{client.Height} "+
            "user_reference_full_screenshot=665,137 title_bar_excluded=true "+
            "ring_detection_disabled=true pixel_color_not_used=true world_tile_from_plugin_only=true";
        return true;
    }

    internal static bool TryLocate(Bitmap image,out Point center,out string diagnostic)=>
        TryFixedCenter(image.Size,out center,out diagnostic);

    internal static bool WithinVerifiedCentre(Point candidate,Point expected)=>
        Math.Abs(candidate.X-expected.X)<=7&&Math.Abs(candidate.Y-expected.Y)<=7;
}

internal static class RuneLiteMinimapMarkerTests
{
    internal static void Run()
    {
        static void Require(bool pass,string reason)
        {if(!pass)throw new Exception("RuneLite minimap anchor: "+reason);}
        using var frame=new Bitmap(773,534);
        using(var g=Graphics.FromImage(frame))g.Clear(Color.DarkGreen);
        Require(RuneLiteMinimapMarker.TryLocate(frame,out var actual,out var diagnostic)&&
                actual==new Point(665,110)&&diagnostic.Contains("FIXED_MODE_USER_VERIFIED"),
            "user's red cross (665,137 with title bar) maps to fixed client pixel 665,110");
        // The old algorithm incorrectly tracked this circular terrain/map icon.
        using(var g=Graphics.FromImage(frame))
        {
            g.FillEllipse(Brushes.White,672,131,14,14);
            g.DrawEllipse(Pens.Black,672,131,14,14);
        }
        Require(RuneLiteMinimapMarker.TryLocate(frame,out actual,out _)&&
                actual==new Point(665,110),
            "a circular map icon at 679,138 cannot override the fixed player centre");
        Require(RuneLiteMinimapMarker.TryFixedCenter(new(768,528),out actual,out _)&&
                actual==new Point(665,110),"raw RuneLite screenshot geometry is supported");
        Require(!RuneLiteMinimapMarker.TryFixedCenter(new(1000,534),out _,out var why)&&
                why.Contains("UNSUPPORTED_LAYOUT"),
            "a sidebar or unfamiliar HUD requires manual verification");
        Require(RuneLiteMinimapMarker.WithinVerifiedCentre(new(666,112),new(665,110))&&
                !RuneLiteMinimapMarker.WithinVerifiedCentre(new(644,113),new(665,110))&&
                !RuneLiteMinimapMarker.WithinVerifiedCentre(new(679,138),new(665,110)),
            "the old wrong calibration must not authorize new clicks");
    }
}
