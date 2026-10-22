using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace ChatRelay
{
    internal interface ISettingsFileStore
    {
        void WriteAtomic(string path, byte[] contents);
    }

    internal sealed class AtomicSettingsFileStore : ISettingsFileStore
    {
        internal Action BeforeCommit;

        public void WriteAtomic(string path, byte[] contents)
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(true);
                }
                BeforeCommit?.Invoke();
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }

    internal sealed class SettingsPersistence
    {
        private readonly object gate = new object();
        private readonly PluginSettings settings;
        private readonly string path;
        private readonly ISettingsFileStore store;
        private string savedSnapshot;
        private int successfulWrites;

        internal SettingsPersistence(PluginSettings settings, string path, string savedSnapshot, ISettingsFileStore store = null)
        {
            this.settings = settings ?? throw new ArgumentNullException("settings");
            this.path = path ?? throw new ArgumentNullException("path");
            this.savedSnapshot = savedSnapshot;
            this.store = store ?? new AtomicSettingsFileStore();
        }

        internal int SuccessfulWrites { get { lock (gate) return successfulWrites; } }
        internal bool HasChanges { get { lock (gate) return savedSnapshot == null || !string.Equals(savedSnapshot, Serialize(settings), StringComparison.Ordinal); } }

        internal bool SaveIfChanged()
        {
            lock (gate)
            {
                var snapshot = Serialize(settings);
                if (savedSnapshot != null && string.Equals(savedSnapshot, snapshot, StringComparison.Ordinal)) return false;
                store.WriteAtomic(path, Encoding.UTF8.GetBytes(snapshot));
                savedSnapshot = snapshot;
                successfulWrites++;
                return true;
            }
        }

        internal static string Serialize(PluginSettings settings)
        {
            settings.Normalize();
            var serializer = new XmlSerializer(typeof(PluginSettings));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, settings);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    internal sealed class SettingsSaveCoordinator
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
        private readonly SettingsPersistence persistence;
        private DateTime nextCheck;

        internal SettingsSaveCoordinator(SettingsPersistence persistence, DateTime now)
        {
            this.persistence = persistence;
            nextCheck = now + Interval;
        }

        internal bool Poll(DateTime now)
        {
            if (now < nextCheck) return false;
            nextCheck = now + Interval;
            return persistence.SaveIfChanged();
        }

        internal bool Flush() => persistence.SaveIfChanged();
    }
}
