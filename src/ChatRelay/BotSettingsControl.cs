using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ChatRelay
{
    internal sealed class BotSettingsControl : UserControl
    {
        private readonly PluginSettings settings;
        private readonly CheckBox bot = new CheckBox { AutoSize = true }, faces = new CheckBox { AutoSize = true };
        private readonly TextBox token = Input(256), common = Input(20), world = Input(24);
        private readonly Label tokenLabel = Caption(), commonLabel = Caption(), worldLabel = Caption(), hint = Caption(), notice = Caption(), delivery = Caption();
        private readonly DataGridView grid = new DataGridView {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect, BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly Button apply = new Button { AutoSize = true }, test = new Button { AutoSize = true };
        private string language;
        internal event EventHandler Applied;
        internal event Action<int> TestRoute;
        private static TextBox Input(int max) => new TextBox { Dock = DockStyle.Fill, MaxLength = max };
        private static Label Caption() => new Label { AutoSize = true, Anchor = AnchorStyles.Left };

        internal BotSettingsControl(PluginSettings settings)
        {
            this.settings = settings;
            Dock = DockStyle.Fill; BackColor = Color.White; AutoScroll = true;
            var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 590, MinimumSize = new Size(420,590),
                ColumnCount = 2, RowCount = 10, Padding = new Padding(12) };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,43)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,57));
            foreach (int h in new[]{32,32,32,32,32,52,224,40,32,52}) form.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            bot.Checked = settings.BotMode; faces.Checked = settings.PlayerIcons;
            token.UseSystemPasswordChar = true; token.Text = settings.ReadBotToken();
            common.Text = settings.DefaultChannelId; world.Text = settings.HomeWorld;
            Span(form,bot,0); form.Controls.Add(tokenLabel,0,1); form.Controls.Add(token,1,1);
            form.Controls.Add(commonLabel,0,2); form.Controls.Add(common,1,2);
            form.Controls.Add(worldLabel,0,3); form.Controls.Add(world,1,3); Span(form,faces,4);
            hint.AutoSize = false; hint.Dock = DockStyle.Fill; Span(form,hint,5);
            grid.Columns.Add(new DataGridViewTextBoxColumn { ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { MaxInputLength = 20, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (var id in ChatParser.Channels)
            {
                var route = (settings.Routes ?? new ChannelRoute[0]).FirstOrDefault(r => r != null && r.Kind == id);
                grid.Rows[grid.Rows.Add("",route?.ChannelId)].Tag = id;
            }
            Span(form,grid,6);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            buttons.Controls.Add(apply); buttons.Controls.Add(test); Span(form,buttons,7);
            notice.ForeColor = Color.FromArgb(135,66,25); Span(form,notice,8); Span(form,delivery,9); Controls.Add(form);
            apply.Click += delegate { Apply(); };
            test.Click += delegate { if (Apply() && grid.CurrentRow != null) TestRoute?.Invoke((int)grid.CurrentRow.Tag); };
        }
        private static void Span(TableLayoutPanel form, Control c, int row)
        { form.Controls.Add(c,0,row); form.SetColumnSpan(c,2); }
        internal void SetStatus(string text) { if (delivery.Text != text) delivery.Text = text; }

        private bool Apply()
        {
            grid.EndEdit();
            var secret = token.Text.Trim(); var destination = common.Text.Trim(); var home = world.Text.Trim();
            var routes = new List<ChannelRoute>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                var id = Convert.ToString(row.Cells[1].Value).Trim();
                if (id.Length == 0) continue;
                if (!BotWebhooks.IsChannelId(id))
                { grid.CurrentCell = row.Cells[1]; notice.Text = T("InvalidChannel"); return false; }
                routes.Add(new ChannelRoute { Kind = (int)row.Tag, ChannelId = id });
            }
            if (destination.Length != 0 && !BotWebhooks.IsChannelId(destination))
            { common.Focus(); notice.Text = T("InvalidChannel"); return false; }
            if (bot.Checked && (secret.Length == 0 || (destination.Length == 0 && routes.Count == 0)))
            { notice.Text = T("BotSetup"); return false; }
            if (secret.Length != 0 && !Regex.IsMatch(secret, @"\A[A-Za-z0-9._-]+\z"))
            { token.Focus(); notice.Text = T("BotSetup"); return false; }
            if (home.Length != 0 && !Regex.IsMatch(home, @"\A[A-Za-z]{2,24}\z"))
            { world.Focus(); notice.Text = T("InvalidWorld"); return false; }
            bool changed = settings.BotMode != bot.Checked || settings.PlayerIcons != faces.Checked ||
                (settings.HomeWorld ?? "") != home || (settings.DefaultChannelId ?? "") != destination ||
                settings.ReadBotToken() != secret || !(settings.Routes ?? new ChannelRoute[0]).Where(r=>r!=null)
                    .Select(r=>r.Kind + "/" + r.ChannelId).SequenceEqual(routes.Select(r=>r.Kind + "/" + r.ChannelId));
            if (changed)
            {
                try { settings.SetBotToken(secret); }
                catch { notice.Text = T("SaveFailed"); return false; }
                settings.BotMode = bot.Checked; settings.PlayerIcons = faces.Checked; settings.HomeWorld = home;
                settings.DefaultChannelId = destination; settings.Routes = routes.ToArray();
                Applied?.Invoke(this,EventArgs.Empty);
            }
            notice.Text = T("Applied");
            return true;
        }
        private string T(string key) => Localization.Get(language,key);
        internal void ApplyLanguage(string lang)
        {
            language = lang;
            bot.Text = T("BotMode"); faces.Text = T("Faces"); tokenLabel.Text = T("BotToken");
            commonLabel.Text = T("CommonChannel"); worldLabel.Text = T("HomeWorld"); hint.Text = T("RoutingHint");
            grid.Columns[0].HeaderText = T("Channels"); grid.Columns[1].HeaderText = T("OverrideChannel");
            foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = T(ChatParser.ChannelKey((int)row.Tag));
            apply.Text = T("ApplyBot"); test.Text = T("TestRoute"); notice.Text = "";
        }
    }
}
