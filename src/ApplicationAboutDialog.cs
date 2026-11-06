using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class ApplicationAboutDialog : EditorWindow
    {
        readonly TextBox status;
        readonly Button check, update;
        readonly Func<bool> canRestart;
        ApplicationRelease latest;
        bool busy;
        public bool RestartForUpdate;
        public ApplicationAboutDialog(Func<bool> canRestart)
        {
            this.canRestart = canRestart;
            UnifiedBindingUi.Form(this, I18n.Str("app.about"), new Size(760, 480), new Size(680, 450));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 50, 60, 50, 65, 0, 56 }) root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "PBZKeyForge  " + Program.Version, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = EditorTheme.Ink }, 0, 0);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = I18n.Str("app.intro"), ForeColor = EditorTheme.Muted, UseMnemonic = false }, 0, 1);
            var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            Link(links, "GitHub", ProjectLinks.Project); Link(links, "Nexus Mods", ProjectLinks.Nexus); Link(links, "Kintaro UE4SS", ProjectLinks.Ue4ss); Link(links, "UE4SS", ProjectLinks.Ue4ssUpstream);
            root.Controls.Add(links, 0, 2);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = I18n.Str("app.credits"), ForeColor = EditorTheme.Muted, UseMnemonic = false }, 0, 3);
            status = new TextBox { Name = "updateStatus", Dock = DockStyle.Fill, Text = I18n.Str("app.updatehint"), ForeColor = EditorTheme.Ink,
                BackColor = EditorTheme.Background, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
            root.Controls.Add(status, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            update = UnifiedBindingUi.Button(I18n.Str("app.install"), "installUpdate"); update.Enabled = false;
            ((EditorButton)update).Primary = true; ((EditorButton)update).AccentColor = EditorTheme.KeyEnhancementStrong;
            check = UnifiedBindingUi.Button(I18n.Str("app.check"), "checkUpdate");
            var close = UnifiedBindingUi.Button(I18n.Str("editor.close"), "close"); close.Click += delegate { Close(); };
            check.Click += async delegate { await Check(); };
            update.Click += async delegate { await Install(); };
            buttons.Controls.Add(update); buttons.Controls.Add(check); buttons.Controls.Add(close); root.Controls.Add(buttons, 0, 5);
            Controls.Add(root); CancelButton = close;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        }
        void Link(FlowLayoutPanel host, string text, string url)
        {
            var link = new LinkLabel { AutoSize = true, Text = text + " ↗", Margin = new Padding(0, 4, 18, 4), LinkColor = EditorTheme.KeyEnhancement, ActiveLinkColor = EditorTheme.Ink };
            link.LinkClicked += delegate { try { ProjectLinks.Open(url); } catch (Exception ex) { ShowFailure(ex.Message); } }; host.Controls.Add(link);
        }
        async Task Check()
        {
            busy = true; check.Enabled = update.Enabled = false; status.Text = I18n.Str("app.checking");
            try
            {
                latest = await Task.Run(() => ApplicationRelease.CheckLatest());
                bool newer = latest.IsNewerThan(Program.Version);
                status.ForeColor = newer ? NoticeCard.Accent : EditorTheme.Green;
                status.Text = I18n.Str(newer ? "app.available" : "app.current", latest.Version);
                update.Enabled = newer && canRestart != null;
            }
            catch (Exception ex) { ShowFailure(ex.Message); }
            finally { busy = false; check.Enabled = true; }
        }
        async Task Install()
        {
            if (latest == null || canRestart == null || !canRestart()) return;
            busy = true; check.Enabled = update.Enabled = false; status.Text = I18n.Str("app.downloading");
            try
            {
                string manifest = await Task.Run(() => ApplicationUpdate.Prepare(latest, AppDomain.CurrentDomain.BaseDirectory));
                ApplicationUpdate.StartHelper(manifest); RestartForUpdate = true;
            }
            catch (Exception ex) { ShowFailure(ex.Message); }
            finally { busy = false; check.Enabled = true; update.Enabled = latest != null && canRestart != null; }
            if (RestartForUpdate) Close();
        }
        public void ShowFailure(string error)
        {
            status.ForeColor = NoticeCard.Accent;
            status.Text = I18n.Str("app.failed", error);
        }
    }
}
