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

internal static partial class Program
{
    private static void UpdateLifecycle()
    {
        var root = Path.Combine(artifacts, "update-lifecycle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dllSource = typeof(PluginSettings).Assembly.Location; var version = AssemblyVersion();
        var zipBytes = BuildPackage(dllSource);
        var zipName = "ChatRelay-v" + version + ".zip"; var manifestName = "ChatRelay-v" + version + ".sha256";
        var zipUrl = "https://github.com/Roxyz0501/chat-relay-act/releases/download/v" + version + "/" + zipName;
        var manifestUrl = "https://github.com/Roxyz0501/chat-relay-act/releases/download/v" + version + "/" + manifestName;
        var manifestBytes = Encoding.UTF8.GetBytes(UpdatePackageVerifier.ComputeSha256Bytes(zipBytes) + "  " + zipName + "\n");
        var release = new ReleaseInfo { Tag = "v" + version, Version = ParseSemVersion(version), Assets = new List<ReleaseAsset> { new ReleaseAsset { Name = zipName, DownloadUrl = zipUrl, Size = zipBytes.Length }, new ReleaseAsset { Name = manifestName, DownloadUrl = manifestUrl, Size = manifestBytes.Length } } };
        var handler = new SyntheticUpdateHandler(zipUrl, zipBytes, manifestUrl, manifestBytes); var cache = Path.Combine(root, "cache"); PreparedUpdate first;
        using (var service = new UpdateService(handler, cache)) first = service.DownloadAndVerifyAsync(release, CancellationToken.None).GetAwaiter().GetResult();
        Check(File.Exists(first.StagedDllPath) && File.Exists(Path.Combine(first.PreparedDirectory, "package.zip")), "synthetic package staged");
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
    private static byte[] BuildPackage(string dllPath)
    {
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var dll = archive.CreateEntry("ChatRelay.dll"); using (var input = File.OpenRead(dllPath)) using (var output = dll.Open()) input.CopyTo(output);
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


