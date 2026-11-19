using Roxyz.Act.UI;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ChatRelay
{
    public sealed class SettingsControl : UserControl
    {
        public const string SupportUrl = "https://ko-fi.com/roxyz0501";
        private readonly PluginSettings settings;
        private readonly Label headerTitle = L(), headerSubtitle = L();
        private readonly Roxyz.Act.UI.PluginTabs tabs;
        private readonly TabPage settingsTab = new TabPage(), supportTab = new TabPage();
        private readonly TextBox webhook = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill, MaxLength = 300 };
        private readonly ChannelSelectionControl channels;
        private readonly TabPage botTab = new TabPage();
        private readonly BotSettingsControl botSettings;
        internal event Action<int> TestRouteRequested;
        private readonly TabPage channelsTab = new TabPage();
        private readonly Button saveWebhook = new Button { AutoSize = true }, testButton = new Button { AutoSize = true };
        private readonly Label webhookLabel = L(), relayStatus = L();
        private string relayKey = "Disabled";
        private int sentCount, droppedCount;
        public event EventHandler TestRequested;
        private readonly PluginLanguagePage language;
        private readonly PluginUpdatePage updateTab;
        private readonly CheckBox enabled = C(), includeNames = C();
        private readonly Label hint = L();
        private string hostedLanguage;
        private UpdatePageState updateState;
        private readonly Label supportHeading = L(), supportDescription = L(), supportOptional = L(), supportUrl = L(), supportSafety = L(), supportStatus = L();
        private readonly Button supportButton = new Button();
        private bool loading = true;
        private string updateMessageKey = "NotChecked";
        private object[] updateMessageArgs = new object[0];

        public event EventHandler SettingsChanged;
        public event EventHandler CheckUpdatesRequested;
        public event EventHandler InstallUpdateRequested;
        public event EventHandler LaterRequested;
        internal ReleaseInfo AvailableRelease { get; private set; }

        public SettingsControl(PluginSettings value)
        {
            settings = value; tabs = new Roxyz.Act.UI.PluginTabs(supportTab); Dock = DockStyle.Fill; Font = new Font("Yu Gothic UI", 9f); BackColor = Color.FromArgb(244, 247, 251); Padding = new Padding(14); AutoScaleMode = AutoScaleMode.Font;
            language = new PluginLanguagePage(Localization.NormalizeLanguage(settings.Language));
            updateTab = new PluginUpdatePage(UpdateConfiguration.RepositoryName, Assembly.GetExecutingAssembly().GetName().Version.ToString(3), settings.CheckUpdatesOnStartup, settings.Language);
            enabled.Checked = settings.Enabled; includeNames.Checked = settings.IncludeNames;
            webhook.Text = settings.ReadWebhook();
            channels = new ChannelSelectionControl(settings.Channels); channelsTab.Controls.Add(channels);
            botSettings = new BotSettingsControl(settings); botTab.Controls.Add(botSettings);
            botSettings.Applied += Changed;
            botSettings.TestRoute += kind => TestRouteRequested?.Invoke(kind);
            var header = BuildHeader(); ConfigureTabs();
            settingsTab.Controls.Add(BuildSettingsLayout()); supportTab.Controls.Add(BuildSupportLayout());
            tabs.TabPages.Add(settingsTab); tabs.TabPages.Add(channelsTab); tabs.TabPages.Add(botTab); tabs.TabPages.Add(language); tabs.TabPages.Add(updateTab); tabs.TabPages.Add(supportTab);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(header, 0, 0); root.Controls.Add(tabs, 0, 1); Controls.Add(root);
            WireEvents(); ApplyLanguage(); loading = false;
        }

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 11, 18, 8) };
            headerTitle.Location = new Point(18, 9); headerTitle.ForeColor = Color.FromArgb(34, 44, 61);
            headerSubtitle.Location = new Point(20, 43); headerSubtitle.ForeColor = Color.FromArgb(103, 116, 137);
            header.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(202, 165, 87), 2f)) e.Graphics.DrawLine(pen, 0, header.Height - 2, header.Width, header.Height - 2); };
            header.Controls.Add(headerTitle); header.Controls.Add(headerSubtitle); return header;
        }

        private void ConfigureTabs()
        {
            tabs.Dock = DockStyle.Fill;
            settingsTab.BackColor = Color.White; supportTab.BackColor = Color.FromArgb(255, 249, 239);
            settingsTab.Padding = supportTab.Padding = new Padding(4);
        }

        private Control BuildSettingsLayout()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(600, 0), ColumnCount = 2, RowCount = 7, Padding = new Padding(24,20,24,20) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach (var h in new[] {0,38,38,42,44,92,76}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            Wide(layout,enabled,1); Wide(layout,includeNames,2);
            layout.Controls.Add(webhookLabel,0,3); layout.Controls.Add(webhook,1,3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill }; Secondary(saveWebhook); Secondary(testButton); buttons.Controls.Add(saveWebhook); buttons.Controls.Add(testButton); Wide(layout,buttons,4);

            hint.AutoSize = false; hint.Dock = DockStyle.Fill; Wide(layout,hint,5);
            relayStatus.AutoSize = false; relayStatus.Dock = DockStyle.Fill; Wide(layout,relayStatus,6);
            panel.Controls.Add(layout); return panel;
        }

        private Control BuildSupportLayout()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(255, 249, 239), Padding = new Padding(28), AutoScroll = true };
            var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(600, 400), ColumnCount = 1, RowCount = 7, BackColor = Color.White, Padding = new Padding(28, 23, 28, 24) };
            supportHeading.ForeColor = Color.FromArgb(194, 112, 22); supportHeading.Margin = new Padding(0, 0, 0, 10); card.Controls.Add(supportHeading, 0, 0);
            supportDescription.MaximumSize = new Size(700, 0); supportDescription.ForeColor = Color.FromArgb(61, 68, 79); supportDescription.Margin = new Padding(0, 0, 0, 8); card.Controls.Add(supportDescription, 0, 1);
            supportOptional.MaximumSize = new Size(700, 0); supportOptional.ForeColor = Color.FromArgb(117, 76, 24); supportOptional.Margin = new Padding(0, 0, 0, 18); card.Controls.Add(supportOptional, 0, 2);
            supportButton.AutoSize = false; supportButton.Size = new Size(360, 50); Primary(supportButton); supportButton.Font = new Font("Yu Gothic UI", 11f, FontStyle.Bold); supportButton.Margin = new Padding(0, 8, 0, 8); card.Controls.Add(supportButton, 0, 3);
            supportUrl.Text = SupportUrl; supportUrl.ForeColor = Color.FromArgb(126, 104, 78); supportUrl.Margin = new Padding(1, 0, 0, 8); card.Controls.Add(supportUrl, 0, 4);
            supportSafety.ForeColor = Color.FromArgb(73, 96, 128); supportSafety.Margin = new Padding(0, 0, 0, 8); card.Controls.Add(supportSafety, 0, 5); supportStatus.ForeColor = Color.FromArgb(176, 65, 56); card.Controls.Add(supportStatus, 0, 6);
            panel.Controls.Add(card); return panel;
        }

        private void WireEvents()
        {
            language.LanguageChanged += Changed; enabled.CheckedChanged += Changed; includeNames.CheckedChanged += Changed; updateTab.StartupChanged += Changed;
            channels.SelectionChanged += Changed;
            saveWebhook.Click += delegate {
                Uri uri; var value = webhook.Text.Trim();
                if (value.Length != 0 && !RelaySender.TryEndpoint(value,out uri)) { SetRelayStatus("InvalidWebhook",sentCount,droppedCount); return; }
                try { settings.SetWebhook(value); Changed(saveWebhook,EventArgs.Empty); }
                catch { SetRelayStatus("SaveFailed",sentCount,droppedCount); }
            };
            testButton.Click += delegate { TestRequested?.Invoke(this,EventArgs.Empty); };
            updateTab.CheckRequested += delegate { CheckUpdatesRequested?.Invoke(this, EventArgs.Empty); }; updateTab.InstallRequested += delegate { InstallUpdateRequested?.Invoke(this, EventArgs.Empty); }; updateTab.LaterRequested += delegate { LaterRequested?.Invoke(this, EventArgs.Empty); }; supportButton.Click += OpenSupportLink;
        }
        public void ApplyTo(PluginSettings value)
        {
            if (hostedLanguage == null) value.Language = language.LanguageCode; value.Enabled = enabled.Checked; value.IncludeNames = includeNames.Checked; value.CheckUpdatesOnStartup = updateTab.CheckOnStartup;
            value.Channels = channels.SelectedChannels; value.Normalize();
        }
        internal void SetRelayStatus(string key,int sent,int dropped) { relayKey = key; sentCount=sent; droppedCount=dropped; relayStatus.Text = T(key) + "\r\n" + T("Counts",sent,dropped); botSettings.SetStatus(relayStatus.Text); }

        internal void SetUpdateChecking() { AvailableRelease = null; SetUpdateMessage("Checking"); }
        internal void ShowUpdateResult(UpdateCheckResult result, Version current)
        {
            AvailableRelease = result?.Release;
            switch (result?.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    SetUpdateMessage(AvailableRelease.ApiLimited ? "UpdateAvailableRateLimited" : "UpdateAvailable", current.ToString(3), AvailableRelease.Version);
                    break;
                case UpdateCheckStatus.UpToDate: SetUpdateMessage("UpToDate", current.ToString(3)); break;
                case UpdateCheckStatus.RateLimited: SetUpdateMessage("UpdateRateLimited"); break;
                case UpdateCheckStatus.NoStableRelease: SetUpdateMessage("NoRelease"); break;
                default: SetUpdateMessage("CheckFailed"); break;
            }
        }
        internal void SetUpdateText(string key, string detail = null) => SetUpdateMessage(key);
        internal void ShowUpdateSkipped(string version) => SetUpdateMessage("UpdateSkipped", version);
        internal void FocusUpdateTab() { if (tabs.TabPages.Contains(updateTab)) tabs.SelectedTab = updateTab; }
        internal void SetHostedLanguage(string code)
        {
            hostedLanguage = Localization.NormalizeLanguage(code);
            foreach (var page in new TabPage[] { language, updateTab, supportTab })
                if (tabs.TabPages.Contains(page)) tabs.TabPages.Remove(page);
            ApplyLanguage();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && hostedLanguage != null) { language.Dispose(); updateTab.Dispose(); supportTab.Dispose(); }
            base.Dispose(disposing);
        }

        private void Changed(object sender, EventArgs e) { if (loading) return; ApplyTo(settings); if (ReferenceEquals(sender, language)) ApplyLanguage(); SettingsChanged?.Invoke(this, EventArgs.Empty); }
        private void ApplyLanguage()
        {
            var family = Localization.FontFamily(CurrentLanguage); ApplyFontFamily(this, family);
            headerTitle.Text = T("PluginTab"); headerTitle.Font = new Font(family, 17f, FontStyle.Bold); headerSubtitle.Text = T("Subtitle"); headerSubtitle.Font = new Font(family, 8.5f);
            tabs.SetLanguageFont(family); settingsTab.Text = T("Settings"); supportTab.Text = T("Support");
            enabled.Text=T("Enabled"); includeNames.Text=T("IncludeNames"); webhookLabel.Text=T("Webhook"); saveWebhook.Text=T("SaveWebhook"); testButton.Text=T("Test"); hint.Text=T("Hint");
            loading = true;
            botTab.Text=T("Bot"); botSettings.ApplyLanguage(CurrentLanguage);
            channelsTab.Text=T("Channels"); channels.ApplyLanguage(CurrentLanguage);
            loading = false; SetRelayStatus(relayKey,sentCount,droppedCount);
            language.SetLanguage(CurrentLanguage); updateTab.SetLanguage(CurrentLanguage);
            supportHeading.Text = T("SupportHeading"); supportHeading.Font = new Font(family, 18f, FontStyle.Bold); supportDescription.Text = T("SupportDescription"); supportOptional.Text = T("SupportBody"); supportOptional.Font = new Font(family, 9.5f, FontStyle.Bold); supportButton.Text = T("SupportButton"); supportSafety.Text = T("SupportSafety");
            RefreshUpdateMessage(); tabs.Invalidate(); Invalidate(true);
        }

        private void SetUpdateMessage(string key, params object[] args) { updateMessageKey = key; updateMessageArgs = args ?? new object[0]; RefreshUpdateMessage(); }
        private void RefreshUpdateMessage() { switch (updateMessageKey)
            {
                case "Checking": updateState = UpdatePageState.Checking; break;
                case "Preparing": updateState = UpdatePageState.Preparing; break;
                case "Prepared": updateState = UpdatePageState.Prepared; break;
                case "UpdateFailed": updateState = UpdatePageState.PrepareFailed; break;
                case "UpdateAvailable": case "UpdateAvailableRateLimited": updateState = UpdatePageState.Available; break;
                default: updateState = UpdatePageState.Ready; break;
            }
            var message = updateMessageKey == "UpdateFailed" ? T("UpdateFailed", T("UpdateProblem")) : T(updateMessageKey, updateMessageArgs);
            if (updateState == UpdatePageState.Available)
                message += Environment.NewLine + (AvailableRelease.Notes ?? T("NoReleaseNotes"));
            updateTab.ShowState(updateState, message); }
        private string T(string key, params object[] args) => Localization.Get(CurrentLanguage, key, args);
        internal string CurrentLanguage => hostedLanguage ?? language.LanguageCode;
        private void OpenSupportLink(object sender, EventArgs e) { string error; if (TryOpenSupportLink(Process.Start, out error)) { supportStatus.ForeColor = Color.FromArgb(17, 108, 75); supportStatus.Text = T("BrowserOpened"); } else { supportStatus.ForeColor = Color.FromArgb(176, 65, 56); supportStatus.Text = T("LinkFailed", T("BrowserFailure")); } }
        private static void Wide(TableLayoutPanel p, Control c, int row) { p.Controls.Add(c, 0, row); p.SetColumnSpan(c, 2); }
        private static Control SliderPanel(TrackBar s, Label v) { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty }; v.Margin = new Padding(8, 8, 0, 0); p.Controls.Add(s); p.Controls.Add(v); return p; }
        private static void ApplyFontFamily(Control root, string family) { if (root is PluginUpdatePage || root is PluginLanguagePage) return; if (!(root is PluginTabs)) root.Font = new Font(family, root.Font.Size, root.Font.Style); foreach (Control child in root.Controls) ApplyFontFamily(child, family); }
        private static CheckBox C() => new CheckBox { AutoSize = true, ForeColor = Color.FromArgb(48, 61, 80), Anchor = AnchorStyles.Left };
        private static Label L() => new Label { AutoSize = true, ForeColor = Color.FromArgb(45, 58, 77), Anchor = AnchorStyles.Left };
        private static NumericUpDown N(decimal min, decimal max, decimal inc) => new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, Width = 120, ThousandsSeparator = true, Anchor = AnchorStyles.Left };
        private static decimal Bound(NumericUpDown c, decimal v) => Math.Max(c.Minimum, Math.Min(c.Maximum, v));
        private static int LanguageIndex(string value) { switch (Localization.NormalizeLanguage(value)) { case "ja": return 1; case "zh-CN": return 2; case "ko": return 3; default: return 0; } }
        private static void Primary(Button b) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(202, 101, 20); b.ForeColor = Color.White; b.FlatAppearance.BorderColor = Color.FromArgb(164, 79, 12); b.Cursor = Cursors.Hand; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(224,126,31); b.FlatAppearance.MouseDownBackColor = Color.FromArgb(174,83,13); }
        private static void Secondary(Button b) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(244, 247, 251); b.ForeColor = Color.FromArgb(48, 61, 80); b.FlatAppearance.BorderColor = Color.FromArgb(171, 181, 196); b.Cursor = Cursors.Hand; }
        public static bool TryOpenSupportLink(Func<ProcessStartInfo, Process> opener, out string error) { try { opener(new ProcessStartInfo(SupportUrl) { UseShellExecute = true }); error = null; return true; } catch (Exception ex) { error = ex.Message; return false; } }
    }
}

