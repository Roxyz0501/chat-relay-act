using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace ChatRelay
{
    internal static class UpdatePackageVerifier
    {
        internal const long MaximumPackageEntryBytes = 50L * 1024 * 1024;
        internal const long MaximumPackageExpandedBytes = 60L * 1024 * 1024;
        private static readonly string[] ExpectedPackageFiles = { UpdateConfiguration.PluginFileName, "README.md" };

        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static bool VerifySha256(string path, string expected) =>
            !string.IsNullOrWhiteSpace(expected) && string.Equals(ComputeSha256(path), expected.Trim(), StringComparison.OrdinalIgnoreCase);

        internal static string ComputeSha256Bytes(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static string FindManifestHash(string manifest, string assetName)
        {
            foreach (var raw in (manifest ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length < 64) continue;
                var hash = line.Substring(0, 64);
                if (!hash.All(IsHex)) continue;
                var name = line.Substring(64).Trim().TrimStart('*');
                if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) return hash.ToLowerInvariant();
            }
            return null;
        }

        public static string SafeDestination(string root, string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName)) throw new InvalidDataException("Invalid ZIP entry path.");
            var normalized = entryName.Replace('/', Path.DirectorySeparatorChar);
            var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var destination = Path.GetFullPath(Path.Combine(rootPath, normalized));
            if (!destination.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP entry escapes the staging directory.");
            return destination;
        }

        public static void ExtractValidated(string zipPath, string destinationRoot, long maximumExpandedBytes = MaximumPackageExpandedBytes, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (maximumExpandedBytes < 0) throw new ArgumentOutOfRangeException("maximumExpandedBytes");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var expected = new System.Collections.Generic.HashSet<string>(ExpectedPackageFiles, StringComparer.Ordinal);
                if (archive.Entries.Count != expected.Count) throw new InvalidDataException("InvalidPackage");
                long totalExpandedBytes = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!expected.Remove(entry.FullName)) throw new InvalidDataException("InvalidPackage");
                    if (entry.Length < 0 || entry.Length > MaximumPackageEntryBytes) throw new InvalidDataException("Update package entry is too large.");
                    totalExpandedBytes += entry.Length;
                    if (totalExpandedBytes > maximumExpandedBytes) throw new InvalidDataException("Update package expands beyond the allowed total size.");
                }
                if (expected.Count != 0) throw new InvalidDataException("InvalidPackage");
                if (Directory.Exists(destinationRoot) && Directory.EnumerateFileSystemEntries(destinationRoot).Any()) throw new IOException("The extraction directory is not empty.");

                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(destinationRoot);
                long writtenTotal = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = SafeDestination(destinationRoot, entry.FullName);
                    using (var input = entry.Open())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long writtenEntry = 0;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            writtenEntry += read; writtenTotal += read;
                            if (writtenEntry > MaximumPackageEntryBytes || writtenTotal > maximumExpandedBytes || writtenEntry > entry.Length)
                                throw new InvalidDataException("Update package expands beyond the allowed total size.");
                            output.Write(buffer, 0, read);
                        }
                        if (writtenEntry != entry.Length) throw new InvalidDataException("InvalidPackage");
                    }
                }
            }
        }

        public static void ValidatePluginAssembly(string dllPath, SemVersion releaseVersion)
        {
            ValidateAssembly(dllPath, UpdateConfiguration.PluginAssemblyName, releaseVersion);
        }

        private static void ValidateAssembly(string path, string expectedName, SemVersion releaseVersion)
        {
            var name = AssemblyName.GetAssemblyName(path);
            if (!string.Equals(name.Name, expectedName, StringComparison.Ordinal)) throw new InvalidDataException("InvalidPackage");
            var actual = name.Version;
            if (releaseVersion == null || actual.Major != releaseVersion.Major || actual.Minor != releaseVersion.Minor || actual.Build != releaseVersion.Patch)
                throw new InvalidDataException("InvalidPackage");
            if (!string.Equals(FileVersionInfo.GetVersionInfo(path).CompanyName, "Roxyz0501", StringComparison.Ordinal))
                throw new InvalidDataException("InvalidPackage");
        }

        private static bool IsHex(char value) => (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');
    }
}


