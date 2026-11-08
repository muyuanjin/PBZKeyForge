using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class PresetLoadDialog : EditorWindow
    {
        readonly PresetModeSelector modes = new PresetModeSelector();
        readonly PresetReviewGrid grid = new PresetReviewGrid();
        readonly Label scope = new EditorLabel(), summary = new EditorLabel(), help = new EditorLabel(), empty = new EditorLabel();
        readonly EditorButton confirm = new EditorButton();
        readonly Func<PresetApplyMode, PresetImpact> review;
        PresetImpact impact;
        public PresetApplyMode ChosenMode { get { return modes.Mode; } }

        internal PresetLoadDialog(Preset preset, PresetApplyMode mode, Func<PresetApplyMode, PresetImpact> review)
        {
            this.review = review;
            Text = I18n.Str("preset.load.title"); Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(900, 600); MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
            BackColor = EditorTheme.Surface; ForeColor = EditorTheme.Ink;
            var layout = new EditorLayout { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 34, 30, 42, 48, 30, 0, 32, 50 })
                layout.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            layout.Controls.Add(new EditorLabel { Text = I18n.Str("preset.load.heading", preset.Name), Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, AutoEllipsis = true }, 0, 0);
            scope.Name = "loadPresetTarget"; scope.Dock = DockStyle.Fill; scope.AutoEllipsis = true; scope.ForeColor = EditorTheme.Muted; layout.Controls.Add(scope, 0, 1);
            modes.Name = "loadPresetMode"; modes.Dock = DockStyle.Fill; modes.Mode = mode; modes.Margin = new Padding(0, 2, 0, 6);
            layout.Controls.Add(modes, 0, 2);
            help.Dock = DockStyle.Fill; help.ForeColor = EditorTheme.Muted; layout.Controls.Add(help, 0, 3);
            summary.Name = "loadPresetSummary"; summary.Dock = DockStyle.Fill; layout.Controls.Add(summary, 0, 4);
            var changes = new EditorPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            grid.Name = "loadPresetChanges"; grid.Dock = DockStyle.Fill;
            empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = EditorTheme.Muted;
            changes.Controls.AddRange(new Control[] { grid, empty }); layout.Controls.Add(changes, 0, 5);
            layout.Controls.Add(new EditorLabel { Text = I18n.Str("preset.review.boundary"), Dock = DockStyle.Fill, ForeColor = EditorTheme.Muted }, 0, 6);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0) };
            confirm.Name = "loadPresetConfirm"; confirm.Text = I18n.Str("preset.review.accept"); confirm.Primary = true; confirm.AutoSize = true;
            confirm.MinimumSize = new Size(144, 34); confirm.Margin = Padding.Empty;
            var cancel = new EditorButton { Text = I18n.Str("editor.cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(108, 34), Margin = new Padding(0, 0, 10, 0) };
            actions.Controls.AddRange(new Control[] { confirm, cancel }); layout.Controls.Add(actions, 0, 7); Controls.Add(layout);
            modes.ModeChanged += delegate { RefreshImpact(); };
            confirm.Click += delegate {
                if (impact == null || impact.Accept == null) return;
                try { impact.Accept(); DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { summary.Text = ex.Message; summary.ForeColor = BindingStatus.Pending; }
            };
            AcceptButton = confirm; CancelButton = cancel;
            Shown += delegate { grid.FitRows(); };
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale(); RefreshImpact();
        }

        void RefreshImpact()
        {
            impact = null; confirm.Enabled = false; grid.Rows.Clear(); summary.ForeColor = EditorTheme.Ink;
            help.Text = I18n.Str(modes.Mode == PresetApplyMode.OnlyPreset ? "preset.only.scopehelp" : modes.Mode == PresetApplyMode.Append ? "preset.append.help" : "preset.replace.help");
            try
            {
                var next = review(ChosenMode);
                scope.Text = I18n.Str("preset.review.scope", next.Scope);
                summary.Text = I18n.Str("preset.review.summary", next.Changes.Count(item => item.Kind == "add"), next.Changes.Count(item => item.Kind == "change"), next.Changes.Count(item => item.Kind == "remove"));
                foreach (var change in next.Changes)
                {
                    int index = grid.Rows.Add(I18n.Str("preset.review." + change.Kind), change.Label + Environment.NewLine + change.Device, change.Before, change.After);
                    grid.Rows[index].Cells[3].Style.ForeColor = change.Kind == "add" ? EditorTheme.Green : BindingStatus.Pending;
                }
                foreach (string effect in next.Effects) grid.Rows.Add("", I18n.Str("preset.review.effects"), "", effect);
                impact = next; confirm.Enabled = next.Accept != null; empty.Text = I18n.Str("preset.review.none");
            }
            catch (Exception ex) { summary.Text = ex.Message; summary.ForeColor = BindingStatus.Pending; empty.Text = ""; }
            grid.Visible = grid.Rows.Count > 0; empty.Visible = !grid.Visible; grid.FitRows();
        }
    }
}
