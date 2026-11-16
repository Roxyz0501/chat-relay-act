using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace ChatRelay
{
    public sealed class ChannelRoute
    {
        public int Kind { get; set; }
        public string ChannelId { get; set; }
    }

    [Serializable]
    public sealed class PluginSettings
    {
        public string Language { get; set; }
        public bool Enabled { get; set; }
        public int[] Channels { get; set; } = new int[0];
        public string ProtectedWebhook { get; set; } = "";
        public bool BotMode { get; set; }
        public string ProtectedBotToken { get; set; } = "";
        public string DefaultChannelId { get; set; } = "";
        public ChannelRoute[] Routes { get; set; } = new ChannelRoute[0];
        public string HomeWorld { get; set; } = "";
        public bool PlayerIcons { get; set; } = true;
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string SkippedVersion { get; set; } = "";
        public bool IncludeNames { get; set; } = true;
        public void Normalize() { Channels = (Channels ?? new int[0]).Where(ChatParser.IsSupported).Distinct().ToArray(); if (!string.IsNullOrWhiteSpace(Language)) Language = Localization.NormalizeLanguage(Language); }
        public bool InitializeLanguageIfMissing(CultureInfo culture) { if (!string.IsNullOrWhiteSpace(Language)) return false; Language = Localization.MapCulture(culture); return true; }
        public string ReadWebhook() => Unprotect(ProtectedWebhook);
        public string ReadBotToken() => Unprotect(ProtectedBotToken);
        private static string Unprotect(string secret) { try { return string.IsNullOrEmpty(secret) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        public void SetWebhook(string value) { if (value != ReadWebhook()) ProtectedWebhook = Protect(value); }
        public void SetBotToken(string value) { if (value != ReadBotToken()) ProtectedBotToken = Protect(value); }
        private static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        public static PluginSettings Load(string path) { try { using (var stream = File.OpenRead(path)) { var value = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream); value.Normalize(); return value; } } catch { return new PluginSettings(); } }
        public void Save(string path) { new AtomicSettingsFileStore().WriteAtomic(path, Encoding.UTF8.GetBytes(SettingsPersistence.Serialize(this))); }
    }
}
