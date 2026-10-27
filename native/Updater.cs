using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal sealed record UpdateInfo(Version Version, Uri Url, string Sha256);

internal static class Updater
{
    // The manifest is release metadata, not an authentication signature.
    private static readonly Uri Manifest = new("https://raw.githubusercontent.com/Riflex91/osrslive/main/update-exe.properties");
    private static readonly Uri LatestRelease = new("https://api.github.com/repos/Riflex91/osrslive/releases/latest");
    private static readonly HttpClient Client = new(new HttpClientHandler {AllowAutoRedirect = true})
    { Timeout = TimeSpan.FromSeconds(14) };
    private static readonly string Base = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSLiveDiagnostics");
    internal static readonly string StartupLog = Path.Combine(Base,"startup.log");
    private static readonly string OutcomePath = Path.Combine(Base,"last-update.status");
    private static HttpRequestMessage NewMetadataRequest(Uri source,bool releaseApi=false)
    {
        // A release's version/hash manifest changes at the SAME raw GitHub
        // URL. Intermediary CDN caches have returned an older manifest even
        // after main had advanced. A unique nonce also bypasses caches that
        // ignore client Cache-Control/Pragma request headers.
        var uri=new Uri(source.AbsoluteUri+"?nonce="+Guid.NewGuid().ToString("N"));
        var request=new HttpRequestMessage(HttpMethod.Get,uri);
        request.Headers.CacheControl=new CacheControlHeaderValue
        {NoCache=true,NoStore=true,MaxAge=TimeSpan.Zero};
        request.Headers.Pragma.ParseAdd("no-cache");
        if(releaseApi)
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("OSRS-Play-Updater/1.8");
        }
        return request;
    }
    private static UpdateInfo ParseReleaseMetadata(string json)
    {
        using var doc=JsonDocument.Parse(json);
        var release=doc.RootElement;
        if(release.GetProperty("draft").GetBoolean()||release.GetProperty("prerelease").GetBoolean())
            throw new FormatException("Latest GitHub release is not stable");
        string tag=release.GetProperty("tag_name").GetString()??"";
        if(!tag.StartsWith('v')||!Version.TryParse(tag[1..],out var version)||
           version.Build<0||version.Revision>=0)
            throw new FormatException("Invalid latest release tag");
        var candidates=release.GetProperty("assets").EnumerateArray()
            .Where(a=>a.GetProperty("name").GetString()=="OSRS-Live-Diagnostics.exe").ToArray();
        if(candidates.Length!=1)throw new FormatException("Release asset not unique");
        string digest=candidates[0].GetProperty("digest").GetString()??"";
        if(!digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Release asset lacks SHA256");
        string url=candidates[0].GetProperty("browser_download_url").GetString()??"";
        return ParseManifest($"version={version}\nexeUrl={url}\nexeSha256={digest[7..]}");
    }
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
    internal static async Task<bool> CheckAndUpdateAsync(Action<string> status,CancellationToken cancellationToken=default,
        Action<string,string>? diagnostic=null,Action<Exception>? onError=null,HttpClient? testClient=null)
    {
        var transport=testClient??Client;
        var current=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(Diagnostics.Version);
        // Assembly versions include a fourth .0 revision while the published
        // manifest uses three-part SemVer. Comparing them directly makes
        // 1.8.6 appear older than 1.8.6.0 even though they are identical.
        var installedVersion=new Version(current.Major,current.Minor,Math.Max(0,current.Build));
        var elapsed=Stopwatch.StartNew();
        try
        {
            status("Suche nach neuer Version auf GitHub ...");
            diagnostic?.Invoke("MANIFEST_REQUEST",$"current_version={current} host={Manifest.Host} channel=stable "+
                "cache_policy=unique_url_no_cache no_private_data_in_query=true");
            using var manifestRequest=NewMetadataRequest(Manifest);
            using var manifestResponse=await transport.SendAsync(manifestRequest,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
            manifestResponse.EnsureSuccessStatusCode();
            var manifest=await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
            var manifestUpdate=ParseManifest(manifest);
            diagnostic?.Invoke("MANIFEST_VERIFIED",
                $"remote_version={manifestUpdate.Version} current_version={current} asset={Path.GetFileName(manifestUpdate.Url.AbsolutePath)} manifest_characters={manifest.Length}");
            var update=manifestUpdate;
            try
            {
                // The raw file can remain behind a just-published release
                // even with a unique URL. The official release API exposes
                // the actual asset SHA256 as independent metadata.
                diagnostic?.Invoke("RELEASE_API_REQUEST",$"host={LatestRelease.Host} source=latest_stable cache_policy=unique_url_no_cache");
                using var releaseRequest=NewMetadataRequest(LatestRelease,releaseApi:true);
                using var response=await transport.SendAsync(releaseRequest,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
                response.EnsureSuccessStatusCode();
                var latest=ParseReleaseMetadata(await response.Content.ReadAsStringAsync(cancellationToken));
                if(latest.Version==manifestUpdate.Version&&latest.Sha256!=manifestUpdate.Sha256)
                    throw new InvalidDataException("Manifest and GitHub release asset SHA256 disagree");
                if(latest.Version>update.Version)
                {
                    update=latest;
                    diagnostic?.Invoke("RELEASE_AHEAD_OF_MANIFEST",
                        $"release_version={latest.Version} manifest_version={manifestUpdate.Version} asset_digest_verified=true");
                }
                else diagnostic?.Invoke("RELEASE_API_VERIFIED",
                    $"release_version={latest.Version} manifest_version={manifestUpdate.Version} version_ahead=false");
            }
            catch(Exception e) when(e is HttpRequestException or JsonException or FormatException or
                TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                diagnostic?.Invoke("RELEASE_API_UNAVAILABLE",$"reason={e.GetType().Name} fallback=verified_manifest");
            }
            if(update.Version<installedVersion)
                diagnostic?.Invoke("MANIFEST_BEHIND_LOCAL",
                    $"remote_version={update.Version} current_version={current} potential_cdn_staleness=true download_suppressed=true");
            if(update.Version<=installedVersion)
            {
                status($"Version {Diagnostics.Version} ist aktuell.");
                diagnostic?.Invoke("ALREADY_CURRENT",$"remote_version={update.Version} elapsed_ms={elapsed.ElapsedMilliseconds}");
                return false;
            }
            if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            status($"Lade Version {update.Version} herunter ...");
            diagnostic?.Invoke("DOWNLOAD_STARTED",$"remote_version={update.Version} asset={Path.GetFileName(update.Url.AbsolutePath)} max_bytes=250000000");
            Log($"Update available {update.Version}");
            string temp=Path.Combine(Base,"updates",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string newExe=Path.Combine(temp,"new.exe");
            string updaterExe=Path.Combine(temp,"helper.exe");
            try
            {
                long downloadedBytes=0;
                using(var response=await transport.GetAsync(update.Url,HttpCompletionOption.ResponseHeadersRead,cancellationToken))
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
                        downloadedBytes=sum;
                    }
                }
                await using(var file=File.OpenRead(newExe))
                {
                    var hash=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellationToken));
                    if(!hash.Equals(update.Sha256,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA256 mismatch");
                }
                diagnostic?.Invoke("DOWNLOAD_VERIFIED",
                    $"remote_version={update.Version} bytes={downloadedBytes} sha256_match=true elapsed_ms={elapsed.ElapsedMilliseconds}");
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
                using var helper=Process.Start(psi) ?? throw new InvalidOperationException("Cannot start update helper");
                status("Update verifiziert. Anwendung wird neu gestartet ...");
                diagnostic?.Invoke("HELPER_STARTED",
                    $"helper_pid={helper.Id} target_file={Path.GetFileName(original)} elapsed_ms={elapsed.ElapsedMilliseconds} restart_only_osrs_play=true");
                Log("Verified updater helper started");
                return true;
            }
            catch { try {Directory.Delete(temp,true);} catch {} throw; }
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {
            diagnostic?.Invoke("CHECK_CANCELLED",$"elapsed_ms={elapsed.ElapsedMilliseconds} closing=true");
            return false;
        }
        catch(Exception e)
        {
            Log("Update check/install failed: "+e.GetType().Name+" "+e.Message);
            diagnostic?.Invoke("CHECK_FAILED",$"elapsed_ms={elapsed.ElapsedMilliseconds} {ExceptionDiagnostic.Describe(e)}");
            status($"Updateprüfung fehlgeschlagen ({e.GetType().Name}); lokale Version läuft weiter.");
            onError?.Invoke(e);
            return false;
        }
    }
    internal static async Task SelfTestAsync()
    {
        string release=$"https://github.com/Riflex91/osrslive/releases/download/v{Diagnostics.Version}/OSRS-Play.exe";
        string valid=$"version={Diagnostics.Version}\nexeUrl={release}\nexeSha256={new string('a',64)}";
        string latestJson=JsonSerializer.Serialize(new
        {
            tag_name="v"+Diagnostics.Version,draft=false,prerelease=false,
            assets=new[]{new {name="OSRS-Live-Diagnostics.exe",
                browser_download_url=release,digest="sha256:"+new string('a',64)}}
        });
        var phases=new List<string>();
        var messages=new List<string>();
        int errors=0;
        using(var stub=new ManifestTestHandler(valid,HttpStatusCode.OK,latestJson))
        using(var http=new HttpClient(stub))
        {
            bool restart=await CheckAndUpdateAsync(messages.Add,default,
                (eventName,_)=>phases.Add(eventName),_=>errors++,http);
            bool repeatRestart=await CheckAndUpdateAsync(messages.Add,default,
                (eventName,_)=>phases.Add(eventName),_=>errors++,http);
            var expectedPhases=new[]{"MANIFEST_REQUEST","MANIFEST_VERIFIED","RELEASE_API_REQUEST",
                "RELEASE_API_VERIFIED","ALREADY_CURRENT", "MANIFEST_REQUEST","MANIFEST_VERIFIED",
                "RELEASE_API_REQUEST","RELEASE_API_VERIFIED","ALREADY_CURRENT"};
            if(restart||repeatRestart||errors!=0||stub.Requests!=2||stub.ReleaseRequests!=2||
               !stub.CacheGuardsPresent||stub.Urls.Distinct().Count()!=2||
               stub.ReleaseUrls.Distinct().Count()!=2||
               stub.Urls.Any(uri=>uri.Scheme!=Manifest.Scheme||uri.Host!=Manifest.Host||
                   uri.AbsolutePath!=Manifest.AbsolutePath||!uri.Query.StartsWith("?nonce=",StringComparison.Ordinal))||
               !phases.SequenceEqual(expectedPhases)||
               !messages.Any(s=>s.Contains("aktuell")))
                throw new Exception("Updater must recheck uncached, unique manifest URLs and preserve no-download semantics: "+
                    $"restarts={restart}/{repeatRestart} errors={errors} manifest_requests={stub.Requests} "+
                    $"api_requests={stub.ReleaseRequests} unique_urls={stub.Urls.Distinct().Count()} "+
                    $"unique_api_urls={stub.ReleaseUrls.Distinct().Count()} cache_headers={stub.CacheGuardsPresent} "+
                    $"phases={string.Join(",",phases)}");
        }
        phases.Clear();messages.Clear();
        string previous=$"version=1.8.5\nexeUrl=https://github.com/Riflex91/osrslive/releases/download/v1.8.5/OSRS-Play.exe\nexeSha256={new string('a',64)}";
        using(var stub=new ManifestTestHandler(previous,HttpStatusCode.OK,latestJson))
        using(var http=new HttpClient(stub))
        {
            bool restart=await CheckAndUpdateAsync(messages.Add,default,
                (eventName,_)=>phases.Add(eventName),_=>errors++,http);
            if(restart||errors!=0||!phases.Contains("RELEASE_AHEAD_OF_MANIFEST")||
               !phases.Contains("ALREADY_CURRENT"))
                throw new Exception("A newer release asset digest must override an older raw manifest");
        }
        phases.Clear();messages.Clear();
        using(var stub=new ManifestTestHandler(previous,HttpStatusCode.OK))
        using(var http=new HttpClient(stub))
        {
            bool restart=await CheckAndUpdateAsync(messages.Add,default,
                (eventName,_)=>phases.Add(eventName),_=>errors++,http);
            if(restart||errors!=0||!phases.Contains("RELEASE_API_UNAVAILABLE")||
               !phases.Contains("MANIFEST_BEHIND_LOCAL"))
                throw new Exception("Release API outage must keep the original verified manifest fallback");
        }
        phases.Clear();messages.Clear();
        var badDigestJson=latestJson.Replace(new string('a',64),new string('b',64),StringComparison.Ordinal);
        using(var stub=new ManifestTestHandler(valid,HttpStatusCode.OK,badDigestJson))
        using(var http=new HttpClient(stub))
        {
            bool restart=await CheckAndUpdateAsync(messages.Add,default,
                (eventName,_)=>phases.Add(eventName),_=>errors++,http);
            if(restart||errors!=1||!phases.Contains("CHECK_FAILED"))
                throw new Exception("Conflicting published asset SHA256 must stop an update check");
        }
        errors=0;phases.Clear();messages.Clear();
        using(var stub=new ManifestTestHandler("bad manifest",HttpStatusCode.OK))
        using(var http=new HttpClient(stub))
        {
            bool restart=await CheckAndUpdateAsync(messages.Add,default,
                (eventName,_)=>phases.Add(eventName),_=>errors++,http);
            if(restart||errors!=1||!phases.Contains("CHECK_FAILED")||
               !messages.Any(s=>s.Contains("fehlgeschlagen")))
                throw new Exception("Updater must report an invalid manifest instead of restarting");
        }
        if(ParseManifest(valid).Url.Host!="github.com")throw new Exception("Updater accepted wrong release host");
        try
        {
            ParseManifest(valid.Replace("github.com","other.example",StringComparison.Ordinal));
            throw new Exception("Updater accepted an untrusted release host");
        }
        catch(FormatException){}
    }
    private sealed class ManifestTestHandler(string manifest,HttpStatusCode responseCode,
        string? releaseJson=null):HttpMessageHandler
    {
        internal int Requests {get;private set;}
        internal int ReleaseRequests {get;private set;}
        internal List<Uri> Urls {get;}=new();
        internal List<Uri> ReleaseUrls {get;}=new();
        internal bool CacheGuardsPresent {get;private set;}=true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            bool api=request.RequestUri?.Host==LatestRelease.Host&&
                request.RequestUri.AbsolutePath==LatestRelease.AbsolutePath;
            if(api){ReleaseRequests++;ReleaseUrls.Add(request.RequestUri!);}
            else {Requests++;Urls.Add(request.RequestUri!);}
            CacheGuardsPresent &= request.Headers.CacheControl is {NoCache:true,NoStore:true}&&
                request.Headers.Pragma.Any(x=>x.Name.Equals("no-cache",StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(api?
                (releaseJson is null?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK):responseCode)
                {Content=new StringContent(api?(releaseJson??"unavailable"):manifest)});
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
