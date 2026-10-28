namespace OSRSLiveDiagnostics;

internal static class RuneLiteRouteCoachTests
{
    internal static void Run()
    {
        static void Require(bool okay,string why)
        {if(!okay)throw new Exception("RuneLite read-only route coach: "+why);}
        var start=new WorldTile(3200,3200);
        var bank=new NavigationDestination("Bank Test","bank",new(3205,3202),1);
        var steps=new[]{new PathStep(new(3201,3201)),new PathStep(new(3202,3201))};
        var hint=RuneLiteRouteCoach.FromPath(start,bank,steps);
        Require(hint is {RemainingSteps:2,Arrived:false,Next:{Tile:{X:3201,Y:3201}}}&&
                hint.Display.Contains("Nordosten")&&
                hint.Diagnostic.Contains("game_input=false"),
            "route suggests the first collision-validated step without issuing input");
        var arrival=RuneLiteRouteCoach.FromPath(new(3205,3202),bank,[]);
        Require(arrival is {Arrived:true,RemainingSteps:0,Next:null}&&
                arrival.Display.Contains("Zielradius erreicht"),
            "empty path is arrival when the pathfinder starts within goal radius");
        var transport=new MapTransport(start,new(3200,3200,1),"Climb-up","Stairs");
        var stairs=RuneLiteRouteCoach.FromPath(start,bank,[new(transport.To,transport)]);
        Require(stairs.Display.Contains("Climb-up Stairs")&&
                stairs.Diagnostic.Contains("transport=true"),
            "typed stair transitions are described instead of invented walking directions");
    }
}
