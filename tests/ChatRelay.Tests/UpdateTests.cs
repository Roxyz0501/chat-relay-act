using ChatRelay;
using Advanced_Combat_Tracker;
using System;
using System.IO;
using System.IO.Compression;
internal static partial class Program
{
    private static void UpdateSafety()
    {
        SemVersion a, b; Check(SemVersion.TryParse("v1.10.0", out a) && SemVersion.TryParse("1.9.0", out b) && a.CompareTo(b) > 0, "SemVer numeric");
        Check(!SemVersion.TryParse("1.01.0", out a) && !SemVersion.TryParse("1.0.0-a..b", out a), "invalid SemVer");
        Check(UpdateService.EvaluateResponse("broken", new Version(1, 0, 0)).Status == UpdateCheckStatus.Failed, "invalid JSON");
        var json = "[{\"tag_name\":\"v9.0.0\",\"draft\":false,\"prerelease\":true},{\"tag_name\":\"v1.1.0\",\"draft\":false,\"prerelease\":false}]";
        Check(UpdateService.EvaluateResponse(json, new Version(1, 0, 0)).Release.Version.ToString() == "1.1.0", "stable only");
        Check(UpdateService.CheckFromFetcherAsync(() => { throw new IOException("offline"); }, new Version(1, 0, 0)).GetAwaiter().GetResult().Status == UpdateCheckStatus.Failed, "network failure");
        Throws(() => UpdateService.EnsureAllowedUri(new Uri("http://github.com/x"), false), "HTTP rejected");
        Throws(() => UpdateService.EnsureAllowedUri(new Uri("https://evil.githubusercontent.com/x"), false), "arbitrary host rejected");
        Throws(() => UpdatePackageVerifier.SafeDestination(artifacts, "../outside"), "Zip Slip rejected");
        var zip = Path.Combine(artifacts, "bad-" + Guid.NewGuid().ToString("N") + ".zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntry("evil.exe");
        Throws(() => UpdatePackageVerifier.ExtractValidated(zip, Path.Combine(artifacts, "extract")), "unexpected asset rejected");
        Throws(() => UpdatePackageVerifier.ExtractValidated(BuildTwoSmallEntries(), Path.Combine(artifacts, "size-check"), 2), "expanded aggregate limit enforced");
        var dll = typeof(PluginSettings).Assembly.Location;
        Check(UpdatePackageVerifier.VerifySha256(dll, UpdatePackageVerifier.ComputeSha256(dll)), "hash valid"); Check(!UpdatePackageVerifier.VerifySha256(dll, new string('0', 64)), "corrupt hash");
        var hash = UpdatePackageVerifier.ComputeSha256(dll); Check(UpdatePackageVerifier.FindManifestHash(hash + "  a.zip", "b.zip") == null, "manifest filename");
        SemVersion.TryParse(typeof(PluginSettings).Assembly.GetName().Version.ToString(3), out a); UpdatePackageVerifier.ValidatePluginAssembly(dll, a);
        var instance = new ChatRelayPlugin(); var target = Path.Combine(artifacts, "日本語 space", "ChatRelay.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllText(target, "previous");
        var registration = new ActPluginData { pluginObj = instance, pluginFile = new FileInfo(target) };
        Check(ChatRelayPlugin.ResolvePluginDllPath(new[] { registration }, instance) == target, "ACT registration priority");
        Throws(() => ChatRelayPlugin.ResolvePluginDllPath(null, instance), "missing ACT path stops update");
        Check(!ReleaseParser.Summarize("**bold** `code`").Contains("**"), "release Markdown removed");
    }
    private static string BuildTwoSmallEntries()
    {
        var path = Path.Combine(artifacts, "aggregate-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("ChatRelay.dll").Open())) writer.Write("abc");
            using (var writer = new StreamWriter(zip.CreateEntry("README.md").Open())) writer.Write("def");
        }
        return path;
    }
}
