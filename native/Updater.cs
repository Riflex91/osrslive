using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OSRSLiveDiagnostics;

internal sealed record UpdateInfo(Version Version, Uri Url, string Sha256);

internal static class Updater
{
    // The manifest is release metadata, not an authentication signature.
    private static readonly Uri Manifest = new("https://raw.githubusercontent.com/Riflex91/osrslive/main/update-exe.properties");
    private static readonly HttpClient Client = new(new HttpClientHandler {AllowAutoRedirect = true})
    { Timeout = TimeSpan.FromSeconds(14) };
    private static readonly string Base = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSLiveDiagnostics");
    internal static readonly string StartupLog = Path.Combine(Base,"startup.log");
    private static readonly string OutcomePath = Path.Combine(Base,"last-update.status");
    private static void RecordOutcome(string code)
    {
        try { Directory.CreateDirectory(Base); File.WriteAllText(OutcomePath,code); }
        catch { /* Reporting must not prevent a completed update. */ }
    }
    internal static string? TakeLastOutcome()
    {
        try
        {
            if(!File.Exists(OutcomePath)) return null;
            string code=File.ReadAllText(OutcomePath).Trim();
            File.Delete(OutcomePath);
            return code switch
            {
                "APPLIED" => "Letztes Update installiert und neue EXE gestartet.",
                "RESTART_FAILED" => "Neustart nach Update fehlgeschlagen; Rollback versucht.",
                _ => "Unbekannter Updatestatus."
            };
        }
        catch { return null; }
    }
    private static void Log(string s)
    {
        try {Directory.CreateDirectory(Base);File.AppendAllText(StartupLog,$"{DateTime.Now:O} {s}\n");} catch { /* app startup must not depend on log writes */ }
    }
    internal static UpdateInfo ParseManifest(string text)
    {
        var dict=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var line in text.Split('\n'))
        {
            var cleaned=line.Trim(); if(cleaned.StartsWith('#')||cleaned.Length==0) continue;
            var split=cleaned.IndexOf('='); if(split<=0) throw new FormatException("Manifest syntax");
            if(!dict.TryAdd(cleaned[..split].Trim(),cleaned[(split+1)..].Trim())) throw new FormatException("Duplicate key");
        }
        if(!dict.TryGetValue("version",out var version) || !Version.TryParse(version,out var parsed) || parsed.Build<0)
            throw new FormatException("Invalid version");
        if(!dict.TryGetValue("exeUrl",out var url) || !Uri.TryCreate(url,UriKind.Absolute,out var uri)
            || uri.Scheme!=Uri.UriSchemeHttps || uri.Host!="github.com"
            || !(uri.AbsolutePath.Equals($"/Riflex91/osrslive/releases/download/v{parsed}/OSRS-Live-Diagnostics.exe",StringComparison.Ordinal)
               || uri.AbsolutePath.Equals($"/Riflex91/osrslive/releases/download/v{parsed}/OSRS-Play.exe",StringComparison.Ordinal))
            || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0)
            throw new FormatException("Untrusted update URL");
        if(!dict.TryGetValue("exeSha256",out var digest) || digest.Length!=64 ||
            digest.Any(c=>!Uri.IsHexDigit(c))) throw new FormatException("Invalid SHA256");
        return new UpdateInfo(parsed,uri,digest.ToLowerInvariant());
    }
    internal static async Task<bool> CheckAndUpdateAsync(Action<string> status,CancellationToken cancellationToken=default)
    {
        var current=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(Diagnostics.Version);
        try
        {
            status("Suche nach neuer Version auf GitHub ...");
            using var manifestResponse=await Client.GetAsync(Manifest,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
            manifestResponse.EnsureSuccessStatusCode();
            var manifest=await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
            var update=ParseManifest(manifest);
            if(update.Version<=current) {status($"Version {Diagnostics.Version} ist aktuell.");return false;}
            if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            status($"Lade Version {update.Version} herunter ...");
            Log($"Update available {update.Version}");
            string temp=Path.Combine(Base,"updates",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string newExe=Path.Combine(temp,"new.exe");
            string updaterExe=Path.Combine(temp,"helper.exe");
            try
            {
                using(var response=await Client.GetAsync(update.Url,HttpCompletionOption.ResponseHeadersRead,cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    if(response.Content.Headers.ContentLength is long n && (n>250_000_000 || n<10_000)) throw new InvalidDataException("Unexpected update size");
                    await using(var file=File.Create(newExe))
                    await using(var stream=await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        var buf=new byte[65536];long sum=0;int read;
                        while((read=await stream.ReadAsync(buf,cancellationToken))!=0)
                        {
                            sum+=read; if(sum>250_000_000) throw new InvalidDataException("Update too large");
                            await file.WriteAsync(buf.AsMemory(0,read),cancellationToken);
                        }
                        if(sum<10_000) throw new InvalidDataException("Unexpected update size");
                    }
                }
                await using(var file=File.OpenRead(newExe))
                {
                    var hash=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellationToken));
                    if(!hash.Equals(update.Sha256,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA256 mismatch");
                }
                // Windows cannot replace a running executable. A temporary copy of THIS trusted EXE
                // waits for the current process to exit, installs the verified EXE and restarts it.
                string original=Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path");
                File.Copy(original,updaterExe);
                var psi=new ProcessStartInfo(updaterExe) { UseShellExecute=false,WorkingDirectory=temp};
                psi.ArgumentList.Add("--apply-update");
                psi.ArgumentList.Add(Environment.ProcessId.ToString());
                psi.ArgumentList.Add(original);
                psi.ArgumentList.Add(newExe);
                psi.ArgumentList.Add(update.Sha256);
                _ = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start update helper");
                status("Update verifiziert. Anwendung wird neu gestartet ...");
                Log("Verified updater helper started");
                return true;
            }
            catch { try {Directory.Delete(temp,true);} catch {} throw; }
        }
        catch(Exception e)
        {
            Log("Update check/install failed: "+e.GetType().Name+" "+e.Message);
            status($"Updateprüfung fehlgeschlagen ({e.GetType().Name}); lokale Version läuft weiter.");
            return false;
        }
    }
    internal static int Apply(string[] args)
    {
        if(args.Length!=5 || !int.TryParse(args[1],out var oldPid))return 3;
        string original=args[2], newExe=args[3], expected=args[4];
        try
        {
            if(!Path.IsPathFullyQualified(original)||!Path.IsPathFullyQualified(newExe))return 4;
            if(expected.Length!=64||expected.Any(c=>!Uri.IsHexDigit(c)))return 5;
            var actual=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(newExe)));
            if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))return 6;
            try {using var p=Process.GetProcessById(oldPid);if(!p.WaitForExit(30000)) {Log("Old process still running");return 7;}}catch(ArgumentException){}
            string backup=original+".previous";
            if(File.Exists(backup)) File.Delete(backup);
            File.Move(original,backup);
            try {File.Move(newExe,original);} catch
            {
                if(!File.Exists(original))File.Move(backup,original);
                throw;
            }
            try
            {
                RecordOutcome("APPLIED");
                Process.Start(new ProcessStartInfo(original){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(original)!});
                Log("Update installed and relaunched successfully");
                // The backup is retained until the next successful startup for manual rollback.
                return 0;
            }
            catch
            {
                // Restart failure: restore old known-good version.
                File.Delete(original);File.Move(backup,original);
                RecordOutcome("RESTART_FAILED");
                try {Process.Start(new ProcessStartInfo(original){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(original)!});} catch {}
                throw;
            }
        }
        catch(Exception ex) {Log("Apply update failed: "+ex.GetType().Name+" "+ex.Message);return 8;}
    }
}
