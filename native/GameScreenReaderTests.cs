namespace OSRSLiveDiagnostics;

internal static class GameScreenReaderTests
{
    internal static void Run()
    {
        static void Require(bool value,string message){if(!value)throw new Exception("Game reader: "+message);}
        NativeSkillFontReader.SelfTest();
        NativeCharacterFontReader.SelfTest();
        CharacterSummaryReader.SelfTest();
        var now=DateTimeOffset.UtcNow;
        var labels=new List<VisualLabel>
        {
            new("Public Private Trade Report",new Rectangle(10,675,400,10)),
            new("Example: *",new Rectangle(10,645,100,10)),
            new("Total level: 33",new Rectangle(960,640,80,10)),
            // A chat message must not override a recognized in-game HUD.
            new("Play Now",new Rectangle(10,600,80,10))
        };
        labels.AddRange(GameScreenReader.GridSkills.Select(name=>new VisualLabel(
            name+"="+(name=="Hitpoints"?10:1),new Rectangle(900,400,180,240),VisualLabelKind.SkillValue)));
        VisualFrame Frame(List<VisualLabel> source)=>new(new IntPtr(1),new Rectangle(0,0,1140,700),now,source);
        Require(GameScreenReader.ParseCharacterPrompt("Example_123: *")=="Example_123"&&
            GameScreenReader.ParseCharacterPrompt("Example_123; +")=="Example_123"&&
            GameScreenReader.ParseCharacterPrompt("Example_123: ")=="Example_123"&&
            GameScreenReader.ParseCharacterPrompt("Example_123: fishing") is null&&
            GameScreenReader.ParseCharacterPrompt("AnameThatsTooLong: *") is null,
            "pixel-cursor OCR accepts only a complete, bounded empty chat input");
        var focusedLabels=labels.Append(new VisualLabel("Example: *",
            new Rectangle(10,645,100,10),VisualLabelKind.CharacterPrompt))
            .Append(new VisualLabel("Wrong: *",new Rectangle(10,648,100,10))).ToList();
        Require(GameScreenReader.Read(Frame(focusedLabels)).Character=="Example",
            "scoped prompt outranks conflicting broad OCR without trusting unrelated chat");
        var gridLayout=new SkillGridLayout(new Rectangle(752,304,186,240),131,1);
        var original=SkillGridOcr.LevelCrop(gridLayout,1,false);
        var rescue=SkillGridOcr.LevelCrop(gridLayout,1,true);
        Require(rescue.Left<original.Left&&rescue.Right>original.Right&&
            rescue.Top==original.Top&&rescue.Bottom==original.Bottom&&
            rescue.Contains(new Point(849,320)),
            "missing-hitpoints retry broadens horizontal OCR crop but retains base-level row");
        var observation=GameScreenReader.Read(Frame(labels));
        Require(observation.Stage==VisibleGameStage.InGame&&observation.Character=="Example"&&observation.Skills?.Count==24,
            "HUD, prompt, 24 base skills, total checksum and chat isolation");
        var bad=labels.Select(l=>l.Text=="Attack=1"?l with {Text="Attack=99"}:l).ToList();
        Require(GameScreenReader.Read(Frame(bad)).Skills is null,"checksum mismatch blocks plan");
        Require(GameScreenReader.Read(Frame(labels.Where(l=>l.Text!="Mining=1").ToList())).Skills is null,
            "incomplete grid blocks plan");
        var tracker=new GameObservationTracker();
        tracker.Observe(observation,now);
        Require(tracker.Plan is null,"single observation is insufficient");
        tracker.Observe(observation,now);
        Require(tracker.Plan is null,"reusing one frame is insufficient");
        tracker.Observe(observation with {ObservedUtc=now.AddSeconds(2)},now.AddSeconds(2));
        Require(tracker.Plan?.Phase==AccountGoalPhase.TrainSkills,"fresh independent repeated evidence starts planner");
        tracker.Expire(now.AddSeconds(10));
        Require(tracker.Plan is null,"stale data cannot retain an active plan");
        tracker.Observe(observation with {ObservedUtc=now.AddSeconds(20)},now.AddSeconds(20));
        tracker.Observe(observation with {Character="Other",ObservedUtc=now.AddSeconds(22)},now.AddSeconds(22));
        Require(tracker.Plan is null,"character switch needs fresh evidence");

        var grid=new List<VisualLabel>{new("Select a world",new Rectangle(10,10,100,12))};
        int[] populations={397,420,350,444,399,310,380,408};
        for(int row=0;row<8;row++)
        {
            grid.Add(new((390+row).ToString(),new Rectangle(180,40+row*20,24,12),VisualLabelKind.Number));
            grid.Add(new(populations[row].ToString(),new Rectangle(230,40+row*20,24,12),VisualLabelKind.Number));
            grid.Add(new((450+row).ToString(),new Rectangle(300,40+row*20,24,12),VisualLabelKind.Number));
        }
        var worldCells=WorldGridReader.Classify(grid);
        Require(worldCells.Count==16&&!worldCells.Any(l=>l.Bounds.Left==230),"grid columns exclude unordered population numbers");
        grid.AddRange(worldCells);
        var choose=VisualStartupPolicy.Decide(grid,397);
        Require(choose.State=="SELECT_F2P_WORLD"&&choose.Click?.Left==180,"select across native grid columns without header alignment");

        string scratch=Path.Combine(Path.GetTempPath(),"osrs-progress-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new AccountProgressStore(scratch);
            var plan=AccountGoalPlanner.Decide(observation.Skills,false,true);
            Require(store.Save(observation,plan),"save actual visible checkpoint");
            Require(!store.Save(observation,plan),"unchanged skills do not rewrite checkpoint");
            var files=Directory.GetFiles(scratch,"*.json");
            Require(files.Length==1&&!File.ReadAllText(files[0]).Contains("Example"),"checkpoint omits raw character name");
        }
        finally
        {
            if(Directory.Exists(scratch))
            {foreach(string file in Directory.GetFiles(scratch))File.Delete(file);Directory.Delete(scratch);}
        }
        Console.WriteLine("GAME READER PASS: grid columns, HUD/prompt, complete skill checksum, independent frames, stale rejection, private checkpoint");
    }
}
