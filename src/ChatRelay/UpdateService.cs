using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatRelay
{
    internal sealed class UpdateService : IDisposable
    {
        private const long MaximumDownloadBytes = 100L * 1024 * 1024;
        private static readonly SemaphoreSlim PreparationGate = new SemaphoreSlim(1, 1);
        private readonly HttpClient client;
        private readonly string preparedRoot;

        public UpdateService() : this(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roxyz0501", "ChatRelay", "PreparedUpdates")) { }
        internal UpdateService(HttpMessageHandler handler, string root)
        {
            preparedRoot = Path.GetFullPath(root);
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }, true) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ChatRelay/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
        {
            if (!UpdateConfiguration.IsConfigured) return new UpdateCheckResult { Status = UpdateCheckStatus.RepositoryNotConfigured };
            try
            {
                EnsureAllowedUri(new Uri(UpdateConfiguration.ReleasesApiUrl), true);
                return await CheckFromFetcherAsync(async () =>
                {
                    using (var response = await client.GetAsync(UpdateConfiguration.ReleasesApiUrl, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested(); return json;
                    }
                }, currentVersion).ConfigureAwait(false);
            }
            catch (Exception ex) { return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.GetBaseException().Message }; }
        }

        internal static async Task<UpdateCheckResult> CheckFromFetcherAsync(Func<Task<string>> fetcher, Version currentVersion)
        {
            try { if (fetcher == null) throw new ArgumentNullException("fetcher"); return EvaluateResponse(await fetcher().ConfigureAwait(false), currentVersion); }
            catch (Exception ex) { return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.GetBaseException().Message }; }
        }

        internal static UpdateCheckResult EvaluateResponse(string json, Version currentVersion)
        {
            try
            {
                var release = ReleaseParser.ParseStableReleases(json).FirstOrDefault();
                if (release == null) return new UpdateCheckResult { Status = UpdateCheckStatus.NoStableRelease };
                SemVersion current;
                if (currentVersion == null || !SemVersion.TryParse(currentVersion.Major + "." + currentVersion.Minor + "." + Math.Max(0, currentVersion.Build), out current)) throw new FormatException("Current version is not valid SemVer.");
                return new UpdateCheckResult { Status = release.Version.CompareTo(current) > 0 ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate, Release = release };
            }
            catch (Exception ex) { return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.GetBaseException().Message }; }
        }

        public async Task<PreparedUpdate> DownloadAndVerifyAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            if (release == null) throw new ArgumentNullException("release");
            await PreparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string stage = null;
            try
            {
                var assets = ResolveAssets(release);
                Directory.CreateDirectory(preparedRoot);
                PruneOldPackages(release.Version);
                var final = Path.Combine(preparedRoot, "v" + release.Version);
                var cached = TryReadCached(final, release);
                if (cached != null) return cached;
                if (Directory.Exists(final)) Directory.Delete(final, true);
                stage = Path.Combine(preparedRoot, ".staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                var zipPath = Path.Combine(stage, "package.zip"); var manifestPath = Path.Combine(stage, "package.sha256");
                await DownloadAsync(assets.Item1.DownloadUrl, zipPath, assets.Item1.Size, MaximumDownloadBytes, cancellationToken).ConfigureAwait(false);
                await DownloadAsync(assets.Item2.DownloadUrl, manifestPath, assets.Item2.Size, 1024 * 1024, cancellationToken).ConfigureAwait(false);
                var expected = UpdatePackageVerifier.FindManifestHash(File.ReadAllText(manifestPath, Encoding.UTF8), assets.Item1.Name);
                if (!UpdatePackageVerifier.VerifySha256(zipPath, expected)) throw new InvalidDataException("HashFailed");
                var extracted = Path.Combine(stage, "files");
                UpdatePackageVerifier.ExtractValidated(zipPath, extracted, cancellationToken: cancellationToken);
                var dll = Path.Combine(extracted, UpdateConfiguration.PluginFileName);
                UpdatePackageVerifier.ValidatePluginAssembly(dll, release.Version);
                cancellationToken.ThrowIfCancellationRequested();
                var stagedDll = Path.Combine(stage, "ChatRelay.dll"); File.Copy(dll, stagedDll);
                Directory.Delete(extracted, true);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stage, final); stage = null;
                PruneOldPackages(release.Version);
                return CreatePrepared(final, release);
            }
            finally
            {
                if (stage != null) { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { } }
                PreparationGate.Release();
            }
        }

        private Tuple<ReleaseAsset, ReleaseAsset> ResolveAssets(ReleaseInfo release)
        {
            var zipName = "ChatRelay-v" + release.Version + ".zip"; var alternate = "ChatRelay-" + release.Version + ".zip";
            var zip = release.Assets.FirstOrDefault(x => string.Equals(x.Name, zipName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, alternate, StringComparison.OrdinalIgnoreCase));
            var manifest = release.Assets.FirstOrDefault(x => string.Equals(x.Name, "ChatRelay-v" + release.Version + ".sha256", StringComparison.OrdinalIgnoreCase)) ??
                release.Assets.FirstOrDefault(x => string.Equals(x.Name, zip == null ? "" : zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase)) ?? release.Assets.FirstOrDefault(x => string.Equals(x.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            if (zip == null || manifest == null) throw new InvalidDataException("UpdateAssetMissing");
            var prefix = "https://github.com/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/download/" + release.Tag + "/";
            if (zip.Size <= 0 || zip.Size > MaximumDownloadBytes || manifest.Size <= 0 || manifest.Size > 1024 * 1024 || zip.DownloadUrl != prefix + zip.Name || manifest.DownloadUrl != prefix + manifest.Name) throw new InvalidDataException("InvalidPackage");
            return Tuple.Create(zip, manifest);
        }

        private PreparedUpdate TryReadCached(string directory, ReleaseInfo release)
        {
            try
            {
                var zip = Path.Combine(directory, "package.zip"); var manifest = Path.Combine(directory, "package.sha256"); var dll = Path.Combine(directory, "ChatRelay.dll");
                if (!File.Exists(zip) || !File.Exists(manifest) || !File.Exists(dll)) return null;
                var assets = ResolveAssets(release); var expected = UpdatePackageVerifier.FindManifestHash(File.ReadAllText(manifest, Encoding.UTF8), assets.Item1.Name);
                if (!UpdatePackageVerifier.VerifySha256(zip, expected)) return null;
                var verify = Path.Combine(directory, ".verify-" + Guid.NewGuid().ToString("N"));
                try
                {
            UpdatePackageVerifier.ExtractValidated(zip, verify);
                    var archiveDll = Path.Combine(verify, UpdateConfiguration.PluginFileName);
                    UpdatePackageVerifier.ValidatePluginAssembly(archiveDll, release.Version);
                    if (!UpdatePackageVerifier.VerifySha256(dll, UpdatePackageVerifier.ComputeSha256(archiveDll))) return null;
                }
                finally { if (Directory.Exists(verify)) Directory.Delete(verify, true); }
                return CreatePrepared(directory, release);
            }
            catch { return null; }
        }

        private static PreparedUpdate CreatePrepared(string directory, ReleaseInfo release)
        {
            var dll = Path.Combine(directory, UpdateConfiguration.PluginFileName);
            return new PreparedUpdate { StagedDllPath = dll, PreparedDirectory = directory, DllSha256 = UpdatePackageVerifier.ComputeSha256(dll), Release = release };
        }

        private void PruneOldPackages(SemVersion keep)
        {
            foreach (var directory in Directory.GetDirectories(preparedRoot))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".staging-", StringComparison.Ordinal) || name.StartsWith(".verify-", StringComparison.Ordinal)) { try { Directory.Delete(directory, true); } catch { } continue; }
                SemVersion version;
                if (name.StartsWith("v", StringComparison.Ordinal) && SemVersion.TryParse(name.Substring(1), out version) && version.CompareTo(keep) < 0) { try { Directory.Delete(directory, true); } catch { } }
            }
        }

        internal static void CleanupAppliedPackages(string root, Version installed)
        {
            if (!Directory.Exists(root) || installed == null) return;
            SemVersion current; if (!SemVersion.TryParse(installed.Major + "." + installed.Minor + "." + Math.Max(0, installed.Build), out current)) return;
            foreach (var directory in Directory.GetDirectories(root))
            {
                SemVersion staged; var name = Path.GetFileName(directory);
                if (name.StartsWith("v", StringComparison.Ordinal) && SemVersion.TryParse(name.Substring(1), out staged) && staged.CompareTo(current) <= 0) { try { Directory.Delete(directory, true); } catch { } }
            }
        }

        private async Task DownloadAsync(string url, string destination, long expectedSize, long limit, CancellationToken cancellationToken)
        {
            var uri = new Uri(url); EnsureAllowedUri(uri, false); HttpResponseMessage response = null;
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var code = (int)response.StatusCode; if (code < 300 || code >= 400) break;
                var location = response.Headers.Location; response.Dispose(); response = null;
                if (location == null) throw new InvalidDataException("InvalidPackage"); uri = location.IsAbsoluteUri ? location : new Uri(uri, location); EnsureAllowedUri(uri, false);
            }
            if (response == null) throw new InvalidDataException("InvalidPackage");
            using (response)
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != expectedSize) throw new InvalidDataException("InvalidPackage");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; long total = 0; int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read; if (total > limit || total > expectedSize) throw new InvalidDataException("InvalidPackage");
                        await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                    if (total != expectedSize) throw new InvalidDataException("InvalidPackage");
                }
            }
        }

        internal static void EnsureAllowedUri(Uri uri, bool api)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Only HTTPS update URLs are allowed.");
            var host = uri.Host.ToLowerInvariant(); var allowed = api ? host == "api.github.com" : host == "github.com" || host == "objects.githubusercontent.com" || host == "release-assets.githubusercontent.com";
            if (!allowed) throw new InvalidDataException("Unexpected update host.");
        }

        public void Dispose() { client.Dispose(); }
    }
}
