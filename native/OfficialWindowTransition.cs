using System;

namespace OSRSLiveDiagnostics;

// Stable, monotonic window handoff: Launcher -> Game.
// An embedded game no longer appears in EnumWindows' top-level results,
// so it must NEVER be "rediscovered" by scanning for the launcher.
internal enum OfficialWindowDecision
{
    KeepGame,
    KeepLauncher,
    AttachGame,
    AttachLauncher,
    WaitForLauncher
}

internal static class OfficialWindowTransition
{
    internal static OfficialWindowDecision Decide(
        OfficialWindowKind? currentlyEmbedded,bool gameTopLevelVisible,bool launcherTopLevelVisible)
    {
        if(currentlyEmbedded==OfficialWindowKind.Game)
            return OfficialWindowDecision.KeepGame;
        if(gameTopLevelVisible)
            return OfficialWindowDecision.AttachGame;
        if(currentlyEmbedded==OfficialWindowKind.Launcher)
            return OfficialWindowDecision.KeepLauncher;
        if(launcherTopLevelVisible)
            return OfficialWindowDecision.AttachLauncher;
        return OfficialWindowDecision.WaitForLauncher;
    }
}

internal static class OfficialWindowTransitionTests
{
    private static void Assert(bool value,string name)
    {
        if(!value)throw new Exception("Official window transition test: "+name);
    }
    internal static void Run()
    {
        // Exact regression from the 2026-10-23 v0.8.0 user log:
        // after SetParent, Game is no longer top-level but Launcher is.
        for(int n=0;n<25;n++)
            Assert(OfficialWindowTransition.Decide(
                OfficialWindowKind.Game,false,true)==OfficialWindowDecision.KeepGame,
                "embedded official game must never fall back to launcher");
        Assert(OfficialWindowTransition.Decide(
            OfficialWindowKind.Launcher,true,false)==OfficialWindowDecision.AttachGame,
            "real OSRS client should take over from launcher");
        Assert(OfficialWindowTransition.Decide(
            OfficialWindowKind.Launcher,false,false)==OfficialWindowDecision.KeepLauncher,
            "embedded launcher missing from top-level enumeration is not a disconnect");
        Assert(OfficialWindowTransition.Decide(
            null,true,true)==OfficialWindowDecision.AttachGame,
            "already running game takes priority over launcher");
        Assert(OfficialWindowTransition.Decide(
            null,false,true)==OfficialWindowDecision.AttachLauncher,
            "start with real launcher window");
        Assert(OfficialWindowTransition.Decide(
            null,false,false)==OfficialWindowDecision.WaitForLauncher,
            "never pretend a connection is active");
        Console.WriteLine("OFFICIAL WINDOW TRANSITION PASS: stable game, launcher handoff, no 1.2s oscillation");
    }
}
