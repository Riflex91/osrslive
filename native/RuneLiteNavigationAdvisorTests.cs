namespace OSRSLiveDiagnostics;

internal static class RuneLiteNavigationAdvisorTests
{
    internal static void Run()
    {
        static void Require(bool ok,string reason)
        {if(!ok)throw new Exception("RuneLite navigation advice: "+reason);}
        var location=new WorldTile(3159,3179,0);
        var bank=new NavigationDestination("Test Bank","bank",new(3163,3182,0),1);
        var result=RuneLiteNavigationAdvisor.FromConfirmed(location,bank);
        Require(result is {DeltaX:4,DeltaY:3,ChebyshevTiles:4,ManhattanTiles:7,
                SamePlane:true,InsideGoalRadius:false}&&
                result.Display.Contains("Luftlinie 4")&&
                result.Diagnostic.Contains("collision_route_calculated=false"),
            "only exact displacement from confirmed API coordinates may be shown");
        Require(RuneLiteNavigationAdvisor.FromConfirmed(new(3162,3182),bank) is
            {InsideGoalRadius:true},"target radius must be respected");
        Require(RuneLiteNavigationAdvisor.FromConfirmed(new(3163,3182,1),bank) is
            {InsideGoalRadius:false,SamePlane:false},
            "a matching X/Y on another plane is never arrival");
        Require(RuneLiteNavigationAdvisor.FromConfirmed(null,bank) is null&&
                RuneLiteNavigationAdvisor.FromConfirmed(location,null) is null,
            "unconfirmed positions must never feed navigation");
        Require(RuneLiteNavigationAdvisor.AvailableDestinations().Length>=10&&
                RuneLiteNavigationAdvisor.AvailableDestinations()
                    .All(d=>WorldMapData.Domain(d.Tile)),
            "only published on-map waypoints can be selectable");
    }
}
