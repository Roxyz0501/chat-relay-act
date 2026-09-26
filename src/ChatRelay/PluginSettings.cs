using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace ChatRelay
{
    [Serializable]
    public sealed class PluginSettings
    {
        public string Language { get; set; }
        public bool Enabled { get; set; }
        public int[] Channels { get; set; } = new int[0];
        public string ProtectedWebhook { get; set; } = "";
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string SkippedVersion { get; set; } = "";
        public bool IncludeNames { get; set; } = true;
        public void Normalize() { Channels = (Channels ?? new int[0]).Where(ChatParser.IsSupported).Distinct().ToArray(); if (!string.IsNullOrWhiteSpace(Language)) Language = Localization.NormalizeLanguage(Language); }
        public bool InitializeLanguageIfMissing(CultureInfo culture) { if (!string.IsNullOrWhiteSpace(Language)) return false; Language = Localization.MapCulture(culture); return true; }
        public string ReadWebhook() { try { return string.IsNullOrEmpty(ProtectedWebhook) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedWebhook), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        public void SetWebhook(string value) { ProtectedWebhook = string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        public static PluginSettings Load(string path) { try { using (var stream = File.OpenRead(path)) { var value = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream); value.Normalize(); return value; } } catch { return new PluginSettings(); } }
        public void Save(string path) { Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(path)); var temp = path + ".tmp"; using (var stream = File.Create(temp)) new XmlSerializer(typeof(PluginSettings)).Serialize(stream, this); if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path); }
    }
}
