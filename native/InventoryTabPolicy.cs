namespace OSRSLiveDiagnostics;

// Verified with the 1000x600 official OSRS client:
// Inventory = backpack icon in the FOURTH TOP-row tab near (838,282).
// The door icon near (838,582) on the LOWER row is LOGOUT.
// Never click the lower door icon while trying to read inventory.
internal static class InventoryTabPolicy
{
    internal static readonly Point[] Candidates={
        new(838,282),
        new(838,287),
        new(842,282)
    };
    internal static bool Candidate(Point point,Size viewport)=>
        viewport==DockedWindowLayout.GameSize&&
        point.X is >=826 and <=855&&point.Y is >=267 and <=298;

    // A full-frame label naming the right-side panel is not an Inventory
    // hover observation. Demand the native hover text at the top-left.
    internal static bool TooltipVerified(IReadOnlyList<VisualLabel> labels,Point point)=>
        Candidate(point,DockedWindowLayout.GameSize)&&
        labels.Any(l=>l.Kind==VisualLabelKind.Text&&
            l.Text.Trim().Equals("Inventory",StringComparison.OrdinalIgnoreCase)&&
            l.Bounds.Left>=0&&l.Bounds.Left<170&&l.Bounds.Top>=0&&l.Bounds.Top<40);

    internal static void SelfTest()
    {
        if(Candidates.Length==0||
            Candidates.Any(p=>!Candidate(p,new Size(1000,600))||p.Y>=320))
            throw new Exception("Inventory tab must be the upper backpack icon");
        var logout=new Point(838,582);
        if(Candidate(logout,DockedWindowLayout.GameSize)||
            Candidate(Candidates[0],new Size(800,600)))
            throw new Exception("Logout tab or wrong viewport cannot be Inventory");
        var good=new VisualLabel("Inventory",new Rectangle(12,9,60,15));
        var side=new VisualLabel("Inventory",new Rectangle(800,320,95,16));
        if(!TooltipVerified(new[]{good},Candidates[0])||
            TooltipVerified(new[]{side},Candidates[0])||
            TooltipVerified(new[]{good},logout))
            throw new Exception("Inventory hover must be tied to actual top-row icon");
    }
}
