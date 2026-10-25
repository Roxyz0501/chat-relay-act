using System.Linq;

namespace ChatRelay
{
    internal static class UpdateConfiguration
    {
        public const string RepositoryOwner = "Roxyz0501";
        public const string RepositoryName = "chat-relay-act";
        public const string PluginAssemblyName = "ChatRelay";
        public const string PluginFileName = "ChatRelay.dll";

        public static bool IsConfigured => IsSafeSegment(RepositoryOwner) && IsSafeSegment(RepositoryName);

        public static string ReleasesApiUrl => "https://api.github.com/repos/" + RepositoryOwner + "/" + RepositoryName + "/releases?per_page=10";

        private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value) &&
            value.All(x => char.IsLetterOrDigit(x) || x == '-' || x == '_' || x == '.');
    }
}
