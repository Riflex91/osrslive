namespace OSRSLiveDiagnostics;

internal static class CombatNavigationTests
{
    internal static void Run()
    {
        static void Require(bool b,string message){if(!b)throw new Exception("Combat/navigation: "+message);}
        Require(ExecutionUi.InventorySlot(0)==new Rectangle(760,312,32,32)&&ExecutionUi.InventorySlot(27)==new Rectangle(886,528,32,32),
            "native 1000x600 inventory grid spans all four observed columns");
        using(var captured=BackgroundWindowCapture.OpaqueBitmap(2,1,new byte[]{12,34,200,0,99,88,77,13}))
        {
            Require(captured.GetPixel(0,0)==Color.FromArgb(255,200,34,12),"ignore-alpha surface retains RGB as opaque pixels");
            using var composite=new Bitmap(2,1);using(var graphics=Graphics.FromImage(composite))graphics.DrawImageUnscaled(captured,0,0);
            Require(composite.GetPixel(1,0)==Color.FromArgb(255,77,88,99),"OCR preprocessing cannot erase undefined-alpha window pixels");
        }
        using var data=new WorldMapData();data.Regions[(50,50)]=new byte[8192];
        void Flag(WorldTile t,int flag)
        {int bit=(t.Plane*4096+(t.Y&63)*64+(t.X&63))*4+flag;data.Regions[(50,50)][bit/8]|=(byte)(1<<(bit%8));}
        var a=new WorldTile(3220,3220);var b=new WorldTile(3221,3220);var c=new WorldTile(3221,3221);
        Flag(a,1);Flag(b,0);
        Require(data.Move(a,b)&&data.Move(b,a),"east/west collision bits read symmetrically");
        Require(!TravelRoutine.Arrived(a,b,a)&&TravelRoutine.Arrived(a,b,b),"adjacent doorway requires movement, not proximity to its far side");
        Require(!data.Move(a,c),"diagonal cannot cut blocked corner");
        var path=data.FindPath(a,c,0);Require(path?.Length==2&&path[0].Tile==b,"A* goes around a blocked diagonal");
        Require(data.FindPath(a,new(3230,3230),0) is null,"unreachable tiles have no invented path");
        Require(data.FindPath(a,new(3220,3520),0) is null,"Wilderness destination rejected");
        var upper=a with{Plane=1};data.Transports.Add(new(a,upper,"Climb-up","Staircase"));
        Require(data.FindPath(a,upper,0)?.Single().Transport?.Action=="Climb-up","stairs are typed plane transitions");
        Require(!data.Move(a,upper),"walking cannot change plane");
        var location=new MapLocation(a,new[]{1d,0d,(a.X-WorldMapData.MinX)*4+2-75,0d,1d,(WorldMapData.MaxY-a.Y)*4-2-75},new(830,10,150,150),12,DateTimeOffset.UtcNow);
        Require(location.ClickFor(b)==new Point(909,85),"world tile projects into minimap through fitted affine");
        Require(location.ClickFor(new(3300,3220)) is null,"distant destination cannot click outside map");
        Require(location.ClickFor(upper) is null,"plane mismatch cannot create screen click");
        Require(NavigationDestinations.Service("ATTACK_CHICKENS")=="chickens","combat destination resolves automatically");
        Require(NavigationDestinations.All.Any(d=>d.Service=="bank")&&NavigationDestinations.All.Any(d=>d.Service=="ge"),"built-in bank and GE goals");
        Require(CombatPolicy.Healthy(7,10,70)&&!CombatPolicy.Healthy(6,10,70)&&!CombatPolicy.Healthy(null,10,70),"health thresholds and unknown OCR");
        Require(!CombatPolicy.Healthy(11,10,70)&&!CombatPolicy.Healthy(0,10,1),"impossible health and death rejected");
        var labels=new[]{new VisualLabel("Attack XP: 1,234",new(810,380,120,15))};
        Require(CombatPolicy.Xp(labels,"Attack")==1234&&CombatPolicy.Xp(labels,"Strength") is null,"XP bound to named skill");
        Require(CombatPolicy.Xp(labels.Concat(labels).ToArray(),"Attack") is null,"ambiguous XP rejected");
        Require(CombatPolicy.Xp(new[]{new VisualLabel("Attack XP: 1,234",new(30,500,120,15))},"Attack") is null,"chat text cannot supply XP");
        Require(CombatPolicy.Danger(new[]{new VisualLabel("Wilderness",new(900,150,90,15))}),"danger area detected");
        string directory=Path.Combine(Path.GetTempPath(),"osrs-combat-test-"+Guid.NewGuid().ToString("N"));string file=Path.Combine(directory,"pending.json");
        try
        {
            var journal=new CombatJournal(file,"subject-A");journal.Prepare("job-1","Attack",10);
            var restored=new CombatJournal(file,"subject-A");Require(restored.Pending()?.BeforeXp==10,"restart retains encounter baseline");
            bool blocked=false;try{restored.Prepare("job-2","Attack",11);}catch(RoutineBlockedException){blocked=true;}
            Require(blocked,"pending attack cannot be resubmitted");
            blocked=false;try{new CombatJournal(file,"subject-B").Pending();}catch(RoutineBlockedException){blocked=true;}
            Require(blocked,"encounter belongs to one character");
            blocked=false;try{restored.Complete("job-2");}catch(RoutineBlockedException){blocked=true;}
            Require(blocked&&File.Exists(file),"wrong completion preserves journal");
            restored.Complete("job-1");Require(restored.Pending() is null,"matching receipt clears intent");
            restored.Prepare("job-3","Ranged",30,"Preparing");
            Require(restored.Pending()?.Phase=="Preparing","equipment preparation survives interruption");
            restored.MarkAttack("job-3");Require(restored.Pending()?.Phase=="AttackPrepared","attack intent committed before input");
            restored.MarkRetreat("job-3");Require(restored.Pending()?.Phase=="Retreating","multi-hop retreat survives next planner tick");
            blocked=false;try{restored.MarkAttack("job-3");}catch(RoutineBlockedException){blocked=true;}
            Require(blocked,"retreat cannot turn into a second attack");restored.Complete("job-3");
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        Console.WriteLine("COMBAT/NAVIGATION PASS: public collision format, A*, no corner cuts/Wilderness, stairs, affine clicks, health/XP guards, durable combat deduplication");
    }
}

