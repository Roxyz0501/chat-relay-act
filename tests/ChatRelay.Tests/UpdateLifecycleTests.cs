using System.Reflection;
using ChatRelay;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static partial class Program
{
    private static void UpdateApiEfficiency()
    {
        var version = AssemblyVersion(); var dll = typeof(PluginSettings).Assembly.Location;
        var updater = typeof(ChatRelay.Updater.Program).Assembly.Location; var zip = BuildPackage(dll, updater);
        var tag = "v" + version; var zipName = "ChatRelay-v" + version + ".zip"; var manifestName = "ChatRelay-v" + version + ".sha256";
        var zipUrl = "https://github.com/Roxyz0501/chat-relay-act/releases/download/" + tag + "/" + zipName;
        var manifestUrl = "https://github.com/Roxyz0501/chat-relay-act/releases/download/" + tag + "/" + manifestName;
        var manifest = Encoding.UTF8.GetBytes(UpdatePackageVerifier.ComputeSha256Bytes(zip) + "  " + zipName + "\n");

        var unchangedHandler = new LatestCheckHandler(tag, 200, zipUrl, zip, manifestUrl, manifest);
        using (var service = new UpdateService(unchangedHandler, Path.Combine(artifacts, "same-version-" + Guid.NewGuid().ToString("N"))))
        {
            var result = service.CheckAsync(new Version(SemVersionMajor(version), SemVersionMinor(version), SemVersionPatch(version)), CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Status == UpdateCheckStatus.UpToDate, "latest release HEAD detects unchanged version");
            Check(unchangedHandler.ApiRequests == 0 && unchangedHandler.Requests == 2, "unchanged version uses zero REST calls and follows the public tag redirect");
        }

        var normalHandler = new LatestCheckHandler(tag, 200, zipUrl, zip, manifestUrl, manifest);
        using (var service = new UpdateService(normalHandler, Path.Combine(artifacts, "rest-success-" + Guid.NewGuid().ToString("N"))))
        {
            var result = service.CheckAsync(new Version(SemVersionMajor(version), Math.Max(0, SemVersionMinor(version) - 1), 0), CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Status == UpdateCheckStatus.UpdateAvailable && !result.Release.ApiLimited && result.Release.Assets.Count == 2, "successful release API metadata parsed for verified tag");
            Check(normalHandler.ApiRequests == 1, "newer version makes one tag-specific REST call");
        }

        foreach (var code in new[] { 403, 429 })
        {
            var handler = new LatestCheckHandler(tag, code, zipUrl, zip, manifestUrl, manifest);
            var root = Path.Combine(artifacts, "rate-limit-" + code + "-" + Guid.NewGuid().ToString("N"));
            using (var service = new UpdateService(handler, root))
            {
                var result = service.CheckAsync(new Version(SemVersionMajor(version), Math.Max(0, SemVersionMinor(version) - 1), 0), CancellationToken.None).GetAwaiter().GetResult();
                Check(result.Status == UpdateCheckStatus.UpdateAvailable && result.Release.ApiLimited, "API " + code + " retains latest verified tag");
                var prepared = service.DownloadAndVerifyAsync(result.Release, CancellationToken.None).GetAwaiter().GetResult();
                Check(File.Exists(prepared.StagedDllPath) && File.Exists(prepared.StagedUpdaterPath), "API " + code + " fallback package verified");
                Check(handler.ApiRequests == 1 && handler.Requests == 5, "API " + code + " does not retry and downloads two fixed assets");
            }
        }

        var headLimited = new LatestCheckHandler(tag, 200, zipUrl, zip, manifestUrl, manifest, latestStatus: 429);
        using (var service = new UpdateService(headLimited, Path.Combine(artifacts, "head-rate-limit-" + Guid.NewGuid().ToString("N"))))
        {
            Check(service.CheckAsync(new Version(1, 0, 0), CancellationToken.None).GetAwaiter().GetResult().Status == UpdateCheckStatus.RateLimited, "latest endpoint rate limit has its own state");
            Check(headLimited.ApiRequests == 0 && headLimited.Requests == 1, "latest endpoint rate limit is not retried through REST");
        }

        var untrusted = new LatestCheckHandler(tag, 200, zipUrl, zip, manifestUrl, manifest, "https://github.com/attacker/chat-relay/releases/tag/v" + version);
        using (var service = new UpdateService(untrusted, Path.Combine(artifacts, "untrusted-" + Guid.NewGuid().ToString("N"))))
        {
            Check(service.CheckAsync(new Version(1, 0, 0), CancellationToken.None).GetAwaiter().GetResult().Status == UpdateCheckStatus.Failed, "latest redirect to another repository rejected");
            Check(untrusted.ApiRequests == 0 && untrusted.Requests == 1, "untrusted latest redirect causes no REST calls");
        }

        var prerelease = new LatestCheckHandler(tag, 200, zipUrl, zip, manifestUrl, manifest, "https://github.com/Roxyz0501/chat-relay-act/releases/tag/v1.4.0-beta.1");
        using (var service = new UpdateService(prerelease, Path.Combine(artifacts, "prerelease-" + Guid.NewGuid().ToString("N"))))
            Check(service.CheckAsync(new Version(1, 0, 0), CancellationToken.None).GetAwaiter().GetResult().Status == UpdateCheckStatus.Failed && prerelease.ApiRequests == 0, "prerelease redirect rejected before REST");
    }

    private static int SemVersionMajor(string value) { SemVersion v; SemVersion.TryParse(value, out v); return v.Major; }
    private static int SemVersionMinor(string value) { SemVersion v; SemVersion.TryParse(value, out v); return v.Minor; }
    private static int SemVersionPatch(string value) { SemVersion v; SemVersion.TryParse(value, out v); return v.Patch; }

    private static void UpdateLifecycle()
    {
        var root = Path.Combine(artifacts, "update-lifecycle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dllSource = typeof(PluginSettings).Assembly.Location; var version = AssemblyVersion();
        var updaterSource = typeof(ChatRelay.Updater.Program).Assembly.Location;
        var zipBytes = BuildPackage(dllSource, updaterSource);
        var zipName = "ChatRelay-v" + version + ".zip"; var manifestName = "ChatRelay-v" + version + ".sha256";
        var zipUrl = "https://github.com/Roxyz0501/chat-relay-act/releases/download/v" + version + "/" + zipName;
        var manifestUrl = "https://github.com/Roxyz0501/chat-relay-act/releases/download/v" + version + "/" + manifestName;
        var manifestBytes = Encoding.UTF8.GetBytes(UpdatePackageVerifier.ComputeSha256Bytes(zipBytes) + "  " + zipName + "\n");
        var release = new ReleaseInfo { Tag = "v" + version, Version = ParseSemVersion(version), Assets = new List<ReleaseAsset> { new ReleaseAsset { Name = zipName, DownloadUrl = zipUrl, Size = zipBytes.Length }, new ReleaseAsset { Name = manifestName, DownloadUrl = manifestUrl, Size = manifestBytes.Length } } };
        var handler = new SyntheticUpdateHandler(zipUrl, zipBytes, manifestUrl, manifestBytes); var cache = Path.Combine(root, "cache"); PreparedUpdate first;
        using (var service = new UpdateService(handler, cache)) first = service.DownloadAndVerifyAsync(release, CancellationToken.None).GetAwaiter().GetResult();
        Check(File.Exists(first.StagedDllPath) && File.Exists(first.StagedUpdaterPath) && File.Exists(Path.Combine(first.PreparedDirectory, "package.zip")), "synthetic package staged");
        Check(handler.Requests == 2, "two bounded HTTP asset reads");
        var snapshot = Snapshot(first.PreparedDirectory);
        using (var service = new UpdateService(new SyntheticUpdateHandler(zipUrl, zipBytes, manifestUrl, manifestBytes), cache))
        {
            var reused = service.DownloadAndVerifyAsync(release, CancellationToken.None).GetAwaiter().GetResult();
            Check(reused.StagedDllPath == first.StagedDllPath, "same version reuses cache");
        }
        Check(Snapshot(first.PreparedDirectory) == snapshot, "cache reuse has no writes");

        var badRoot = Path.Combine(root, "bad-cache"); var badHandler = new SyntheticUpdateHandler(zipUrl, zipBytes, manifestUrl, Encoding.UTF8.GetBytes(new string('0',64) + "  " + zipName));
        using (var service = new UpdateService(badHandler, badRoot)) Throws(() => service.DownloadAndVerifyAsync(release, CancellationToken.None).GetAwaiter().GetResult(), "bad manifest rejected");
        Check(Directory.Exists(badRoot) && Directory.GetFileSystemEntries(badRoot).Length == 0, "failed preparation removes staging files");
        using (var service = new UpdateService(new SyntheticUpdateHandler(zipUrl, zipBytes, manifestUrl, manifestBytes), badRoot))
            Check(File.Exists(service.DownloadAndVerifyAsync(release, CancellationToken.None).GetAwaiter().GetResult().StagedDllPath), "preparation succeeds after failure");

        var cancelRoot = Path.Combine(root, "cancel-cache"); var blocking = new SyntheticUpdateHandler(zipUrl, zipBytes, manifestUrl, manifestBytes, true); var cts = new CancellationTokenSource(); Task<PreparedUpdate> pending;
        using (var service = new UpdateService(blocking, cancelRoot))
        {
            pending = service.DownloadAndVerifyAsync(release, cts.Token); blocking.ReadStarted.Wait(5000); cts.Cancel();
            Throws(() => pending.GetAwaiter().GetResult(), "download cancellation propagated");
        }
        Check(Directory.Exists(cancelRoot) && Directory.GetFileSystemEntries(cancelRoot).Length == 0, "cancellation removes staging files");

        Thread.Sleep(150);
        Check(Snapshot(first.PreparedDirectory) == snapshot, "no writes after preparation task completes");
        UpdateService.CleanupAppliedPackages(cache, new Version(ParseSemVersion(version).Major, ParseSemVersion(version).Minor, ParseSemVersion(version).Patch));
        Check(!Directory.Exists(first.PreparedDirectory), "applied version cache cleaned on next startup");
        try { Directory.Delete(root, true); } catch { }
    }

    private static SemVersion ParseSemVersion(string value) { SemVersion result; if (!SemVersion.TryParse(value, out result)) throw new InvalidDataException("Invalid test version."); return result; }
    private static string AssemblyVersion() => typeof(PluginSettings).Assembly.GetName().Version.ToString(3);
    private static byte[] BuildPackage(string dllPath, string updaterPath)
    {
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var dll = archive.CreateEntry("ChatRelay.dll"); using (var input = File.OpenRead(dllPath)) using (var output = dll.Open()) input.CopyTo(output);
                var updater = archive.CreateEntry("ChatRelay.Updater.exe"); using (var input = File.OpenRead(updaterPath)) using (var output = updater.Open()) input.CopyTo(output);
                var readme = archive.CreateEntry("README.md"); using (var writer = new StreamWriter(readme.Open())) writer.Write("synthetic test package");
            }
            return memory.ToArray();
        }
    }
    private static string Snapshot(string directory) => string.Join("|", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).Select(x => x.Substring(directory.TrimEnd(Path.DirectorySeparatorChar).Length + 1) + ":" + new FileInfo(x).Length + ":" + UpdatePackageVerifier.ComputeSha256(x)));

    private sealed class SyntheticUpdateHandler : HttpMessageHandler
    {
        private readonly string zipUrl, manifestUrl; private readonly byte[] zip, manifest; private readonly bool blockZip; public int Requests; public readonly ManualResetEventSlim ReadStarted = new ManualResetEventSlim(false);
        public SyntheticUpdateHandler(string zipUrl, byte[] zip, string manifestUrl, byte[] manifest, bool blockZip = false) { this.zipUrl=zipUrl; this.zip=zip; this.manifestUrl=manifestUrl; this.manifest=manifest; this.blockZip=blockZip; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests); var isZip = request.RequestUri.AbsoluteUri == zipUrl; var bytes = isZip ? zip : request.RequestUri.AbsoluteUri == manifestUrl ? manifest : null;
            if (bytes == null) throw new InvalidDataException("Unexpected synthetic URL.");
            HttpContent content = isZip && blockZip ? (HttpContent)new BlockingContent(bytes, ReadStarted) : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class LatestCheckHandler : HttpMessageHandler
    {
        private readonly string tag, zipUrl, manifestUrl, redirect;
        private readonly int apiStatus, latestStatus;
        private readonly byte[] zip, manifest;
        public int Requests, ApiRequests;
        public LatestCheckHandler(string tag, int apiStatus, string zipUrl, byte[] zip, string manifestUrl, byte[] manifest, string redirect = null, int latestStatus = 302)
        { this.tag=tag; this.apiStatus=apiStatus; this.zipUrl=zipUrl; this.zip=zip; this.manifestUrl=manifestUrl; this.manifest=manifest; this.redirect=redirect; this.latestStatus=latestStatus; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (request.Method == HttpMethod.Head && request.RequestUri.AbsoluteUri == "https://github.com/Roxyz0501/chat-relay-act/releases/latest")
            {
                if (latestStatus != 302) return Task.FromResult(new HttpResponseMessage((HttpStatusCode)latestStatus) { RequestMessage = request });
                var response = new HttpResponseMessage(HttpStatusCode.Redirect) { RequestMessage = request };
                response.Headers.Location = new Uri(redirect ?? ("https://github.com/Roxyz0501/chat-relay-act/releases/tag/" + tag));
                return Task.FromResult(response);
            }
            if (request.Method == HttpMethod.Head && request.RequestUri.AbsoluteUri == "https://github.com/Roxyz0501/chat-relay-act/releases/tag/" + tag)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsoluteUri == "https://api.github.com/repos/Roxyz0501/chat-relay-act/releases/tags/" + Uri.EscapeDataString(tag))
            {
                Interlocked.Increment(ref ApiRequests);
                if (apiStatus != 200) return Task.FromResult(new HttpResponseMessage((HttpStatusCode)apiStatus) { Content = new StringContent("rate limit") });
                var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false, ["name"] = tag, ["body"] = "stable update",
                    ["assets"] = new object[] {
                        new Dictionary<string, object> { ["name"] = Path.GetFileName(zipUrl), ["browser_download_url"] = zipUrl, ["size"] = zip.Length },
                        new Dictionary<string, object> { ["name"] = Path.GetFileName(manifestUrl), ["browser_download_url"] = manifestUrl, ["size"] = manifest.Length }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsoluteUri == zipUrl) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsoluteUri == manifestUrl) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) });
            throw new InvalidDataException("Unexpected latest-check request: " + request.Method + " " + request.RequestUri);
        }
    }
    private sealed class BlockingContent : HttpContent
    {
        private readonly byte[] data; private readonly ManualResetEventSlim started;
        public BlockingContent(byte[] data, ManualResetEventSlim started) { this.data=data; this.started=started; Headers.ContentLength=data.Length; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => stream.WriteAsync(data, 0, data.Length);
        protected override bool TryComputeLength(out long length) { length=data.Length; return true; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new BlockingReadStream(data, started));
    }
    private sealed class BlockingReadStream : MemoryStream
    {
        private readonly ManualResetEventSlim started; public BlockingReadStream(byte[] data, ManualResetEventSlim started) : base(data) { this.started=started; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            started.Set(); var tcs = new TaskCompletionSource<int>(); cancellationToken.Register(() => tcs.TrySetCanceled()); return tcs.Task;
        }
    }
}


