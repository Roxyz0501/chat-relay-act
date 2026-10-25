using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatRelay
{
    public sealed class ChatRelayPlugin : IActPluginV1
    {
        private readonly object gate = new object();
        private readonly UpdateService updates = new UpdateService();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly HashSet<Task> updateTasks = new HashSet<Task>();
        private PluginSettings settings;
        private SettingsPersistence persistence;
        private SettingsSaveCoordinator saveCoordinator;
        private SettingsControl control;
        private Label status;
        private string settingsPath;
        private RelaySender sender;
        private HashSet<int> selected = new HashSet<int>();
        private bool forwarding, includeNames, stopped, checking, preparing;
        private string relayLanguage;
        private System.Windows.Forms.Timer statusTimer, saveTimer;
        private UpdateCheckResult lastUpdate;

        public void InitPlugin(TabPage page, Label statusLabel)
        {
            status = statusLabel;
            UpdateService.CleanupAppliedPackages(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roxyz0501", "ChatRelay", "PreparedUpdates"), Assembly.GetExecutingAssembly().GetName().Version);
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "ChatRelay.xml");
            var existed = File.Exists(settingsPath);
            settings = PluginSettings.Load(settingsPath);
            var savedSnapshot = existed ? SettingsPersistence.Serialize(settings) : null;
            settings.InitializeLanguageIfMissing(CultureInfo.CurrentUICulture); settings.Normalize();
            persistence = new SettingsPersistence(settings, settingsPath, savedSnapshot);
            saveCoordinator = new SettingsSaveCoordinator(persistence, DateTime.UtcNow);
            control = new SettingsControl(settings); page.Text = "Chat Relay"; page.Controls.Add(control);
            control.SettingsChanged += SettingsChanged;
            control.TestRequested += Test;
            control.CheckUpdatesRequested += delegate { StartCheckUpdates(true); };
            control.InstallUpdateRequested += Install;
            control.LaterRequested += delegate { if (lastUpdate?.Release == null) return; settings.SkippedVersion = lastUpdate.Release.Version.ToString(); control.ShowUpdateSkipped(settings.SkippedVersion); };
            Configure();
            ActGlobals.oFormActMain.OnLogLineRead += LogLineRead;
            statusTimer = new System.Windows.Forms.Timer { Interval = 500 }; statusTimer.Tick += RefreshStatus; statusTimer.Start();
            saveTimer = new System.Windows.Forms.Timer { Interval = (int)SettingsSaveCoordinator.Interval.TotalMilliseconds }; saveTimer.Tick += SaveSettings; saveTimer.Start();
            status.Text = T("Started");
            if (settings.CheckUpdatesOnStartup) StartCheckUpdates(false);
        }

        private string T(string key, params object[] args) => Localization.Get(settings?.Language ?? "en", key, args);
        private void SaveSettings(object source, EventArgs e) { try { saveCoordinator?.Poll(DateTime.UtcNow); } catch { if (!stopped && status != null) status.Text = T("SaveFailed"); } }
        private void SettingsChanged(object source, EventArgs e) { if (!stopped) Configure(); }
        private void Configure()
        {
            RelaySender old;
            lock (gate)
            {
                if (stopped) return;
                old = sender; sender = null;
                forwarding = settings.Enabled; includeNames = settings.IncludeNames; selected = new HashSet<int>(settings.Channels); relayLanguage = settings.Language;
                Uri endpoint; if (RelaySender.TryEndpoint(settings.ReadWebhook(), out endpoint)) sender = new RelaySender(endpoint);
            }
            old?.Stop();
            RefreshStatus(null, EventArgs.Empty);
        }

        private void LogLineRead(bool isImport, LogLineEventArgs args)
        {
            if (isImport || args == null) return;
            lock (gate)
            {
                if (stopped || !forwarding || sender == null) return;
                ChatMessage message;
                if (!ChatParser.TryParseEvent(isImport, args.originalLogLine, args.logLine, out message)) return;
                if (selected.Contains(message.Channel)) sender.Enqueue(ChatParser.Format(message, relayLanguage, includeNames));
            }
        }
        private void Test(object source, EventArgs e)
        {
            lock (gate) { if (sender == null) { control.SetRelayStatus("InvalidWebhook",0,0); return; } sender.Enqueue(T("TestMessage")); }
        }
        private void RefreshStatus(object source, EventArgs e)
        {
            if (control == null || control.IsDisposed || stopped) return;
            lock (gate) control.SetRelayStatus(sender == null ? (forwarding ? "InvalidWebhook" : "Disabled") : !forwarding && sender.Sent == 0 && sender.Dropped == 0 ? "Disabled" : sender.StatusKey, sender?.Sent ?? 0, sender?.Dropped ?? 0);
        }

        private void StartCheckUpdates(bool manual)
        {
            lock (gate)
            {
                if (checking || preparing || stopped) return;
                checking = true;
                if (control != null && !control.IsDisposed) control.SetUpdateChecking();
                TrackUpdateTask(Task.Run(() => CheckUpdatesAsync(manual)));
            }
        }
        private async Task CheckUpdatesAsync(bool manual)
        {
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version;
                var result = await updates.CheckAsync(current, cancellation.Token).ConfigureAwait(false);
                lock (gate) { if (stopped) return; lastUpdate = result; }
                PostUi(() =>
                {
                    control.ShowUpdateResult(result, current);
                    if (result.Status == UpdateCheckStatus.UpdateAvailable)
                    {
                        if (!manual && settings.SkippedVersion == result.Release.Version.ToString()) control.ShowUpdateSkipped(settings.SkippedVersion);
                        else { control.FocusUpdateTab(); status.Text = T("UpdateAvailable", current.ToString(3), result.Release.Version); }
                    }
                });
            }
            finally { lock (gate) checking = false; }
        }
        private void Install(object source, EventArgs e)
        {
            lock (gate)
            {
                if (preparing || stopped || lastUpdate?.Status != UpdateCheckStatus.UpdateAvailable) return;
                preparing = true;
                try
                {
                    var release = lastUpdate.Release;

                    control.SetUpdateText("Preparing");
                    TrackUpdateTask(Task.Run(() => InstallAsync(release)));
                }
                catch
                {
                    preparing = false;
                    control.SetUpdateText("UpdateFailed", T("UpdateProblem"));
                    status.Text = T("UpdateProblem");
                }
            }
        }
        private async Task InstallAsync(ReleaseInfo release)
        {
            try
            {
                var package = await updates.DownloadAndVerifyAsync(release, cancellation.Token).ConfigureAwait(false);
                lock (gate) { if (stopped) return; }
                var targetPath = ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this);
                PostUi(() => { control.SetPreparedUpdate(package.StagedDllPath, targetPath); status.Text = T("Prepared"); });
            }
            catch { PostUi(() => { control.SetUpdateText("UpdateFailed", T("UpdateProblem")); status.Text = T("UpdateProblem"); }); }
            finally { lock (gate) preparing = false; }
        }
        private void TrackUpdateTask(Task task)
        {
            updateTasks.Add(task);
            task.ContinueWith(t => { lock (gate) updateTasks.Remove(t); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        private void PostUi(Action action)
        {
            var target = control;
            if (target == null || target.IsDisposed || !target.IsHandleCreated) return;
            try { target.BeginInvoke((MethodInvoker)delegate { if (!stopped && !target.IsDisposed) action(); }); } catch (InvalidOperationException) { }
        }
        internal static string ResolvePluginDllPath(IEnumerable<ActPluginData> plugins, IActPluginV1 instance)
        {
            var registration = plugins?.FirstOrDefault(x => x != null && ReferenceEquals(x.pluginObj, instance));
            return NormalizePluginPath(registration?.pluginFile?.FullName);
        }
        internal static string NormalizePluginPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("PluginPathUnavailable");
            Uri uri; if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile) value = uri.LocalPath;
            if (!Path.IsPathRooted(value)) throw new InvalidOperationException("PluginPathUnavailable");
            var path = Path.GetFullPath(value);
            if (!string.Equals(Path.GetFileName(path), UpdateConfiguration.PluginFileName, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidOperationException("PluginPathUnavailable");
            return path;
        }

        public void DeInitPlugin()
        {
            RelaySender old;
            Task[] pending;
            lock (gate)
            {
                if (stopped) return;
                stopped = true; forwarding = false; old = sender; sender = null;
                pending = updateTasks.ToArray();
            }
            ActGlobals.oFormActMain.OnLogLineRead -= LogLineRead;
            StopTimer(statusTimer, RefreshStatus); StopTimer(saveTimer, SaveSettings);
            cancellation.Cancel();
            old?.Stop();
            try { Task.WaitAll(pending); } catch (AggregateException ex) { if (ex.InnerExceptions.Any(x => !(x is OperationCanceledException))) { } }
            var saveFailed = false;
            try { saveCoordinator?.Flush(); } catch { saveFailed = true; }
            control.SettingsChanged -= SettingsChanged;
            control.TestRequested -= Test;
            updates.Dispose(); cancellation.Dispose();
            control?.Dispose();
            if (status != null) status.Text = T(saveFailed ? "SaveFailed" : "Stopped");
        }

        private static void StopTimer(System.Windows.Forms.Timer value, EventHandler handler)
        {
            if (value == null) return;
            value.Stop(); value.Tick -= handler; value.Dispose();
        }
    }
}




