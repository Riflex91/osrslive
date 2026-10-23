using System;

namespace OSRSLiveDiagnostics;

// An automatic startup must never open a duplicate launcher when an official
// window already exists. The official client takes precedence over the launcher.
internal enum OfficialStartupAction
{
    KeepAttached,
    AttachExisting,
    AskUserToChoose,
    StartLauncher
}

internal static class OfficialStartupPolicy
{
    internal static OfficialStartupAction Decide(bool alreadyAttached,
        int officialGames,int officialLaunchers)
    {
        if(alreadyAttached)return OfficialStartupAction.KeepAttached;
        if(officialGames<0||officialLaunchers<0)
            throw new ArgumentOutOfRangeException(nameof(officialGames));
        if(officialGames>1)return OfficialStartupAction.AskUserToChoose;
        if(officialGames==1)return OfficialStartupAction.AttachExisting;
        if(officialLaunchers>1)return OfficialStartupAction.AskUserToChoose;
        if(officialLaunchers==1)return OfficialStartupAction.AttachExisting;
        return OfficialStartupAction.StartLauncher;
    }
}

internal static class OfficialStartupPolicyTests
{
    internal static void Run()
    {
        static void Check(bool attached,int games,int launchers,
            OfficialStartupAction expected)
        {
            var actual=OfficialStartupPolicy.Decide(attached,games,launchers);
            if(actual!=expected)
                throw new Exception($"Startup policy: games={games} launchers={launchers} actual={actual} expected={expected}");
        }
        Check(false,0,0,OfficialStartupAction.StartLauncher);
        Check(false,1,0,OfficialStartupAction.AttachExisting);
        Check(false,0,1,OfficialStartupAction.AttachExisting);
        Check(false,1,1,OfficialStartupAction.AttachExisting);
        Check(false,0,2,OfficialStartupAction.AskUserToChoose);
        Check(false,2,1,OfficialStartupAction.AskUserToChoose);
        Check(true,0,0,OfficialStartupAction.KeepAttached);
        Check(true,1,1,OfficialStartupAction.KeepAttached);
        Console.WriteLine("OFFICIAL AUTO-START PASS: no duplicate launch, existing game first, multiple-window selection");
    }
}
