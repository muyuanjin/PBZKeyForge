using System;
using System.Drawing;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public sealed class AppearanceDialog : EditorWindow
    {
        readonly CheckBox enabled = new EditorCheckBox { Name = "flamesEnabled", CheckedColor = EditorTheme.KeyEnhancement };
        readonly TrackBar speed = new TrackBar { Name = "flameSpeed", Minimum = 5, Maximum = 300, TickFrequency = 50 };
        readonly TrackBar height = new TrackBar { Name = "flameHeight", Minimum = 5, Maximum = 100, TickFrequency = 10 };
        readonly TrackBar brightness = new TrackBar { Name = "flameBrightness", Minimum = 0, Maximum = 300, TickFrequency = 50 };
        readonly TrackBar transparency = new TrackBar { Name = "flameTransparency", Minimum = 0, Maximum = 100, TickFrequency = 10 };
        readonly TrackBar hue = new TrackBar { Name = "flameHue", Minimum = -180, Maximum = 180, TickFrequency = 60 };
        readonly Action<FlameAppearance> preview;
        readonly Timer previewTimer = new Timer { Interval = 60 };
        bool loading;

        public FlameAppearance Value
        {
            get { return new FlameAppearance { Enabled = enabled.Checked, Speed = speed.Value, Height = height.Value,
                Brightness = brightness.Value, Transparency = transparency.Value, Hue = hue.Value }; }
        }

        public AppearanceDialog(FlameAppearance initial, Action<FlameAppearance> preview)
        {
            this.preview = preview;
            UnifiedBindingUi.Form(this, I18n.Str("enhancement.flames"), new Size(660, 505), new Size(600, 505));
            var root = UnifiedBindingUi.Grid(1); root.Padding = new Padding(18, 10, 18, 12); root.RowCount = 8;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            enabled.Text = I18n.Str("enhancement.flames"); enabled.ForeColor = EditorTheme.KeyEnhancement;
            enabled.Dock = DockStyle.Fill; root.Controls.Add(enabled, 0, 0); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            AddSlider(root, speed, "speed", 1); AddSlider(root, height, "height", 2);
            AddSlider(root, brightness, "brightness", 3); AddSlider(root, transparency, "transparency", 4);
            AddSlider(root, hue, "hue", 5);
            var hint = UnifiedBindingUi.Label(I18n.Str("appearance.flame.hint")); hint.ForeColor = EditorTheme.Muted; hint.AutoEllipsis = false;
            root.Controls.Add(hint, 0, 6); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var accept = UnifiedBindingUi.Button(I18n.Str("editor.ok"), "saveAppearance"); accept.Primary = true; accept.DialogResult = DialogResult.OK;
            var cancel = UnifiedBindingUi.Button(I18n.Str("editor.cancel"), "cancelAppearance"); cancel.DialogResult = DialogResult.Cancel;
            var reset = UnifiedBindingUi.Button(I18n.Str("appearance.flame.reset"), "resetAppearance"); reset.Click += delegate { SetValue(new FlameAppearance()); Changed(); };
            buttons.Controls.Add(accept); buttons.Controls.Add(cancel); buttons.Controls.Add(reset);
            root.Controls.Add(buttons, 0, 7); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(root); AcceptButton = accept; CancelButton = cancel;
            enabled.CheckedChanged += delegate { Changed(); };
            previewTimer.Tick += delegate {
                previewTimer.Stop();
                if (preview != null) preview(Value);
            };
            FormClosing += delegate { previewTimer.Stop(); };
            SetValue(initial);
        }

        void SetValue(FlameAppearance value)
        {
            var settings = value.Clone(); settings.Clamp(); loading = true;
            enabled.Checked = settings.Enabled; speed.Value = settings.Speed; height.Value = settings.Height;
            brightness.Value = settings.Brightness; transparency.Value = settings.Transparency;
            hue.Value = settings.Hue;
            loading = false; EnableSliders();
        }

        void AddSlider(TableLayoutPanel root, TrackBar slider, string key, int index)
        {
            var row = UnifiedBindingUi.Grid(3); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            row.Controls.Add(UnifiedBindingUi.Label(I18n.Str("appearance.flame." + key)), 0, 0);
            slider.Dock = DockStyle.Fill; slider.BackColor = EditorTheme.Surface; slider.Margin = new Padding(0, 3, 0, 0);
            slider.AccessibleName = I18n.Str("appearance.flame." + key); row.Controls.Add(slider, 1, 0);
            var value = UnifiedBindingUi.Label(""); value.TextAlign = ContentAlignment.MiddleRight; row.Controls.Add(value, 2, 0);
            string unit = key == "hue" ? "°" : "%";
            slider.ValueChanged += delegate { value.Text = slider.Value + unit; Changed(); };
            value.Text = slider.Value + unit; root.Controls.Add(row, 0, index); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        }

        void EnableSliders()
        { speed.Enabled = height.Enabled = brightness.Enabled = transparency.Enabled = hue.Enabled = enabled.Checked; }

        void Changed()
        {
            if (loading) return;
            EnableSliders();
            previewTimer.Start();
        }

        protected override void Dispose(bool disposing)
        { if (disposing) previewTimer.Dispose(); base.Dispose(disposing); }
    }
}
