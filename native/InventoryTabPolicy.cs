namespace OSRSLiveDiagnostics;

// Anchors measured from the embedded official 1000x600 OSRS client. The
// Inventory backpack icon is the fourth LOWER-row tab (the fourth upper-row
// tab is Equipment). Never sweep or click inside a panel to find this control.
internal static class InventoryTabPolicy
{
    internal static readonly Point[] Candidates={
        new(838,582),
        new(838,577),
        new(842,582)
    };
    internal static bool Candidate(Point point,Size viewport)=>
        viewport==DockedWindowLayout.GameSize&&
        point.X is >=828 and <=853&&point.Y is >=565 and <=594;

    // Read only the *current* "Inventory" hover label at the game viewport
    // top-left. A generic OCR label inside the right panel isn't proof that
    // the cursor is over the correct tab.
    internal static bool TooltipVerified(IReadOnlyList<VisualLabel> labels,Point point)=>
        Candidate(point,DockedWindowLayout.GameSize)&&
        labels.Any(l=>l.Kind==VisualLabelKind.Text&&
            l.Text.Trim().Equals("Inventory",StringComparison.OrdinalIgnoreCase)&&
            l.Bounds.Left>=0&&l.Bounds.Left<170&&l.Bounds.Top>=0&&l.Bounds.Top<40);

    internal static void SelfTest()
    {
        var candidates=Candidates;
        if(candidates.Length==0||candidates.Any(p=>!Candidate(p,new Size(1000,600)))||
            candidates.Any(p=>p.Y<=545))
            throw new Exception("Inventory tab must be searched on bottom row");
        var upper=new Point(838,282);
        if(Candidate(upper,DockedWindowLayout.GameSize)||
            Candidate(candidates[0],new Size(800,600)))
            throw new Exception("Equipment/other viewport cannot identify the Inventory tab");
        var good=new VisualLabel("Inventory",new Rectangle(12,9,60,15));
        var rightPanel=new VisualLabel("Inventory",new Rectangle(800,320,95,16));
        if(!TooltipVerified(new[]{good},candidates[0])||
            TooltipVerified(new[]{rightPanel},candidates[0])||
            TooltipVerified(new[]{good},upper))
            throw new Exception("Inventar hover evidence must be strictly anchored to bottom tab");
    }
}
