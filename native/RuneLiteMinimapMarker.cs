namespace OSRSLiveDiagnostics;

// The WHITE player dot stays at the minimap centre while the map moves.
// This anchor uses the real intersection of the user's red guide lines,
// not the earlier misread location or a nearby circular map icon.
internal static class RuneLiteMinimapMarker
{
    // In the user's ORIGINAL 768px-wide marked image the RED LINES intersect
    // near (640,110). WGC's 773px-wide capture places the WHITE DOT around
    // (645,111). Its client rectangle already has the correct crop origin:
    // do NOT subtract the window titlebar a second time.
    internal static Point FixedModePlayerCenter(Size client)=>
        new(client.Width-128,111);

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
        center=FixedModePlayerCenter(client);
        diagnostic=$"anchor_result=FIXED_MODE_USER_VERIFIED center_x={center.X} center_y={center.Y} "+
            $"client={client.Width}x{client.Height} "+
            "user_red_cross_screenshot=640,110 capture_titlebar_double_subtraction=false "+
            "ring_detection_disabled=true white_dot_requires_confirmation=true "+
            "world_tile_from_plugin_only=true";
        return true;
    }

    internal static bool TryLocate(Bitmap image,out Point center,out string diagnostic)
    {
        if(!TryFixedCenter(image.Size,out center,out diagnostic))return false;
        // An unrendered minimap, login screen or wrong HUD layout must not
        // authorize movement from the dimensions alone. White player-dot
        // evidence is used ONLY to validate click geometry, never a WorldTile.
        if(!RuneLiteMinimapSetupDialog.IsWhitePlayerMark(image,center))
        {
            diagnostic+=" white_dot_result=NOT_VISIBLE automatic_calibration_rejected=true";
            center=Point.Empty;
            return false;
        }
        diagnostic+=" white_dot_result=VISIBLE_NEAR_FIXED_CENTER";
        return true;
    }

    internal static bool WithinVerifiedCentre(Point candidate,Point expected)=>
        Math.Abs(candidate.X-expected.X)<=3&&Math.Abs(candidate.Y-expected.Y)<=3;
}

internal static class RuneLiteMinimapMarkerTests
{
    internal static void Run()
    {
        static void Require(bool pass,string reason)
        {if(!pass)throw new Exception("RuneLite minimap anchor: "+reason);}
        using var frame=new Bitmap(773,534);
        using(var g=Graphics.FromImage(frame))g.Clear(Color.DarkGreen);
        Require(RuneLiteMinimapMarker.TryFixedCenter(frame.Size,out var expected,out var diagnostic)&&
                expected==new Point(645,111)&&diagnostic.Contains("FIXED_MODE_USER_VERIFIED"),
            "real white player dot follows width-128, not the mistaken 665,110 anchor");
        Require(!RuneLiteMinimapMarker.TryLocate(frame,out _,out diagnostic)&&
                diagnostic.Contains("NOT_VISIBLE"),
            "a green but empty minimap cannot arm movement from dimensions alone");
        frame.SetPixel(644,111,Color.White);
        frame.SetPixel(645,111,Color.White);
        Require(RuneLiteMinimapMarker.TryLocate(frame,out var actual,out diagnostic)&&
                actual==expected&&diagnostic.Contains("VISIBLE_NEAR_FIXED_CENTER"),
            "white player dot near the true centre confirms the fixed geometry");
        // The old algorithm incorrectly tracked this circular terrain/map icon.
        using(var g=Graphics.FromImage(frame))
        {
            g.FillEllipse(Brushes.White,672,131,14,14);
            g.DrawEllipse(Pens.Black,672,131,14,14);
        }
        Require(RuneLiteMinimapMarker.TryLocate(frame,out actual,out _)&&
                actual==new Point(645,111),
            "a circular map icon at 679,138 cannot override the actual white player dot");
        Require(RuneLiteMinimapMarker.TryFixedCenter(new(768,528),out actual,out _)&&
                actual==new Point(640,111),"original 768px-wide red-cross geometry is supported");
        Require(!RuneLiteMinimapMarker.TryFixedCenter(new(1000,534),out _,out var why)&&
                why.Contains("UNSUPPORTED_LAYOUT"),
            "a sidebar or unfamiliar HUD requires manual verification");
        Require(RuneLiteMinimapMarker.WithinVerifiedCentre(new(644,113),expected)&&
                !RuneLiteMinimapMarker.WithinVerifiedCentre(new(666,114),expected)&&
                !RuneLiteMinimapMarker.WithinVerifiedCentre(new(679,138),expected),
            "the actual prior white-dot selection is allowed, but v1.8.26 false centres are rejected");
    }
}
