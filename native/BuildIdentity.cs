using System.Reflection;

namespace OSRSLiveDiagnostics;

// Immutable build provenance embedded at compile time, not inferred from the
// release version (PR test builds deliberately share the 1.7.2 version).
// No user/account/client information is included in these attributes.
internal static class BuildIdentity
{
    private static string Value(string key,string fallback)=>
        typeof(BuildIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(a=>a.Key==key)?.Value??fallback;

    internal static string Run=>Value("OsrsBuildRun","local");
    internal static string Revision=>Value("OsrsBuildSha","unknown");
    internal static string Channel=>Value("OsrsBuildChannel","local");
    internal static string ShortRevision=>
        Revision.Length>=8?Revision[..8]:Revision;
    internal static string Display=>Channel=="pull_request"?
        "PR-TEST run "+Run+" / "+ShortRevision:
        Channel=="push"?"CI run "+Run+" / "+ShortRevision:"lokaler Build";
    internal static string Log=>
        "build_channel="+Channel+" build_run="+Run+" build_commit="+Revision;

    internal static void SelfTest()
    {
        if(string.IsNullOrWhiteSpace(Run)||string.IsNullOrWhiteSpace(Revision)||
            string.IsNullOrWhiteSpace(Channel)||string.IsNullOrWhiteSpace(ShortRevision))
            throw new Exception("Missing build provenance");
        if(Channel=="pull_request"&&(Run=="local"||Revision=="unknown"))
            throw new Exception("Unidentified PR test build");
    }
}
