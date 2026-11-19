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
                SemVersion current;
                if (!TryCurrentVersion(currentVersion, out current)) throw new FormatException("Current version is not valid SemVer.");
                var latestUri = new Uri("https://github.com/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/latest");
                var tag = await ResolveLatestStableTagAsync(latestUri, cancellationToken).ConfigureAwait(false);
                if (tag.Status != null) return new UpdateCheckResult { Status = tag.Status.Value };
                if (tag.Version.CompareTo(current) <= 0) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate, Release = new ReleaseInfo { Tag = tag.Tag, Version = tag.Version } };

                var apiUrl = "https://api.github.com/repos/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/tags/" + Uri.EscapeDataString(tag.Tag);
                EnsureAllowedUri(new Uri(apiUrl), true);
                using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                    {
                        return new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = new ReleaseInfo { Tag = tag.Tag, Version = tag.Version, ApiLimited = true } };
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("InvalidResponse");
                    var json = await ReadLimitedTextAsync(response.Content, 2 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
                    var release = ReleaseParser.ParseStableRelease(json);
                    if (release == null || !string.Equals(release.Tag, tag.Tag, StringComparison.Ordinal) || release.Version.CompareTo(tag.Version) != 0)
                        throw new InvalidDataException("InvalidReleaseTag");
                    return new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = release };
                }
            }
            catch (Exception ex) { return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.GetBaseException().Message }; }
        }

        private async Task<LatestTagResult> ResolveLatestStableTagAsync(Uri uri, CancellationToken cancellationToken)
        {
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                EnsureAllowedUri(uri, false);
                using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    var status = (int)response.StatusCode;
                    if (status == 403 || status == 429) return LatestTagResult.ForStatus(UpdateCheckStatus.RateLimited);
                    if (status == 404) return LatestTagResult.ForStatus(UpdateCheckStatus.NoStableRelease);
                    if (status >= 300 && status < 400)
                    {
                        var location = response.Headers.Location;
                        if (location == null) throw new InvalidDataException("InvalidLatestRedirect");
                        uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                        EnsureAllowedUri(uri, false);
                        if (!IsRepositoryTagUri(uri)) throw new InvalidDataException("InvalidLatestRedirect");
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    SemVersion version;
                    var tag = ReadVerifiedTagFromUri(uri, out version);
                    return new LatestTagResult { Tag = tag, Version = version };
                }
            }
            throw new InvalidDataException("TooManyLatestRedirects");
        }

        private static bool IsRepositoryTagUri(Uri uri)
        {
            var expected = "/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/tag/";
            return uri != null && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith(expected, StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.Substring(expected.Length).Length > 0 &&
                uri.AbsolutePath.Substring(expected.Length).IndexOf('/') < 0 &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.UserInfo);
        }

        private static string ReadVerifiedTagFromUri(Uri uri, out SemVersion version)
        {
            var prefix = "/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/tag/";
            if (!IsRepositoryTagUri(uri)) throw new InvalidDataException("InvalidLatestRedirect");
            var escapedTag = uri.AbsolutePath.Substring(prefix.Length);
            if (escapedTag.IndexOf('%') >= 0 || escapedTag.IndexOf('\\') >= 0) throw new InvalidDataException("InvalidLatestRedirect");
            var tag = escapedTag;
            if (!SemVersion.TryParse(tag, out version) || !version.IsStable) throw new InvalidDataException("InvalidReleaseTag");
            return tag;
        }

        private static bool TryCurrentVersion(Version currentVersion, out SemVersion current)
        {
            current = null;
            return currentVersion != null && SemVersion.TryParse(currentVersion.Major + "." + currentVersion.Minor + "." + Math.Max(0, currentVersion.Build), out current);
        }

        private sealed class LatestTagResult
        {
            public string Tag; public SemVersion Version; public UpdateCheckStatus? Status;
            public static LatestTagResult ForStatus(UpdateCheckStatus status) => new LatestTagResult { Status = status };
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
                var updater = Path.Combine(extracted, UpdateConfiguration.UpdaterFileName);
                UpdatePackageVerifier.ValidatePluginAssembly(dll, release.Version);
                UpdatePackageVerifier.ValidateUpdaterAssembly(updater, release.Version);
                cancellationToken.ThrowIfCancellationRequested();
                var stagedDll = Path.Combine(stage, UpdateConfiguration.PluginFileName); File.Copy(dll, stagedDll);
                var stagedUpdater = Path.Combine(stage, UpdateConfiguration.UpdaterFileName); File.Copy(updater, stagedUpdater);
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
            if (release.ApiLimited)
            {
                SemVersion verified;
                if (!SemVersion.TryParse(release.Tag, out verified) || !verified.IsStable || verified.CompareTo(release.Version) != 0 ||
                    !(string.Equals(release.Tag, "v" + release.Version, StringComparison.Ordinal) || string.Equals(release.Tag, release.Version.ToString(), StringComparison.Ordinal))) throw new InvalidDataException("InvalidReleaseTag");
                var fallbackPrefix = "https://github.com/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/download/" + release.Tag + "/";
                return Tuple.Create(
                    new ReleaseAsset { Name = zipName, DownloadUrl = fallbackPrefix + zipName, Size = 0 },
                    new ReleaseAsset { Name = "ChatRelay-v" + release.Version + ".sha256", DownloadUrl = fallbackPrefix + "ChatRelay-v" + release.Version + ".sha256", Size = 0 });
            }
            var zip = release.Assets.FirstOrDefault(x => string.Equals(x.Name, zipName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, alternate, StringComparison.OrdinalIgnoreCase));
            var manifest = release.Assets.FirstOrDefault(x => string.Equals(x.Name, "ChatRelay-v" + release.Version + ".sha256", StringComparison.OrdinalIgnoreCase)) ??
                release.Assets.FirstOrDefault(x => string.Equals(x.Name, zip == null ? "" : zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase)) ?? release.Assets.FirstOrDefault(x => string.Equals(x.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            if (zip == null || manifest == null) throw new InvalidDataException("UpdateAssetMissing");
            var prefix = "https://github.com/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/download/" + release.Tag + "/";
            if (zip.Size < 0 || zip.Size > MaximumDownloadBytes || manifest.Size < 0 || manifest.Size > 1024 * 1024 ||
                (zip.Size == 0 && !release.ApiLimited) || (manifest.Size == 0 && !release.ApiLimited) || zip.DownloadUrl != prefix + zip.Name || manifest.DownloadUrl != prefix + manifest.Name) throw new InvalidDataException("InvalidPackage");
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
                    var archiveUpdater = Path.Combine(verify, UpdateConfiguration.UpdaterFileName);
                    UpdatePackageVerifier.ValidatePluginAssembly(archiveDll, release.Version);
                    UpdatePackageVerifier.ValidateUpdaterAssembly(archiveUpdater, release.Version);
                    if (!UpdatePackageVerifier.VerifySha256(dll, UpdatePackageVerifier.ComputeSha256(archiveDll))) return null;
                    if (!UpdatePackageVerifier.VerifySha256(Path.Combine(directory, UpdateConfiguration.UpdaterFileName), UpdatePackageVerifier.ComputeSha256(archiveUpdater))) return null;
                }
                finally { if (Directory.Exists(verify)) Directory.Delete(verify, true); }
                return CreatePrepared(directory, release);
            }
            catch { return null; }
        }

        private static PreparedUpdate CreatePrepared(string directory, ReleaseInfo release)
        {
            var dll = Path.Combine(directory, UpdateConfiguration.PluginFileName);
            return new PreparedUpdate { StagedDllPath = dll, StagedUpdaterPath = Path.Combine(directory, UpdateConfiguration.UpdaterFileName), PreparedDirectory = directory, DllSha256 = UpdatePackageVerifier.ComputeSha256(dll), Release = release };
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
                if (response.Content.Headers.ContentLength.HasValue && expectedSize > 0 && response.Content.Headers.ContentLength.Value != expectedSize) throw new InvalidDataException("InvalidPackage");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; long total = 0; int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read; if (total > limit || (expectedSize > 0 && total > expectedSize)) throw new InvalidDataException("InvalidPackage");
                        await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                    if (total == 0 || (expectedSize > 0 && total != expectedSize)) throw new InvalidDataException("InvalidPackage");
                }
            }
        }

        private static async Task<string> ReadLimitedTextAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
        {
            using (var input = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8192]; int count;
                while ((count = await input.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (buffer.Length + count > maximumBytes) throw new InvalidDataException("InvalidResponse");
                    buffer.Write(chunk, 0, count);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
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
