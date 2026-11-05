using System;
using System.Drawing;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class BindingHelpButton : EditorButton
    {
        readonly ToolTip tip = new ToolTip { InitialDelay = 250, AutoPopDelay = 15000 };
        readonly string topic;
        public BindingHelpButton(string topic)
        {
            this.topic = topic; Text = "?"; Width = 28; TabStop = true;
            RefreshText();
            Click += delegate { using (var dialog = new BindingHelpDialog(topic)) dialog.ShowDialog(FindForm()); };
        }
        public void RefreshText()
        { AccessibleName = I18n.Str("help.title"); tip.SetToolTip(this, I18n.Str(topic)); }
        protected override void OnMouseEnter(EventArgs e) { RefreshText(); base.OnMouseEnter(e); }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class BindingHelpDialog : EditorWindow
    {
        public BindingHelpDialog(string topic)
            : this(I18n.Str("help.title"), I18n.Str(topic) + "\n\n" + I18n.Str("help.example")) { }
        public BindingHelpDialog(string title, string body)
        {
            Text = title; Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(660, 480); MinimumSize = new Size(450, 330);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            BackColor = EditorTheme.Background; ForeColor = EditorTheme.Ink;
            var content = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = EditorTheme.Surface };
            var text = new Label { AutoSize = true, Text = body, ForeColor = EditorTheme.Ink };
            content.Controls.Add(text);
            content.Layout += delegate {
                float scale = CurrentAutoScaleDimensions.Width / 96f; int inset = (int)(24 * scale);
                text.Location = new Point(inset + content.AutoScrollPosition.X, inset + content.AutoScrollPosition.Y);
                var maximum = new Size(Math.Max(1, content.ClientSize.Width - inset * 3), 0);
                if (text.MaximumSize != maximum) text.MaximumSize = maximum;
            };
            content.HandleCreated += delegate { EditorArtwork.DarkScrollbars(content); };
            var footer = new EditorPanel { Dock = DockStyle.Bottom, Height = 64, BackColor = EditorTheme.Background };
            var close = new EditorButton { Text = I18n.Str("editor.ok"), Width = 112, Height = 36, DialogResult = DialogResult.OK };
            footer.Controls.Add(close);
            footer.Layout += delegate {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                close.Location = new Point(footer.Width - close.Width - (int)(24 * scale), (footer.Height - close.Height) / 2);
            };
            Controls.Add(content); Controls.Add(footer); AcceptButton = CancelButton = close;
            PerformAutoScale();
        }
    }
}
