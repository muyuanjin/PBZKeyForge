using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class PendingEditsDialog : EditorWindow
    {
        public PendingEditsDialog(Func<string> summary, Func<Task<bool>> apply, Func<bool> canApplyNow, bool offline)
        {
            bool canApply = canApplyNow();
            UnifiedBindingUi.Form(this, I18n.Str("editor.drafttitle"), new Size(660, 270), new Size(540, 240));
            MinimizeBox = MaximizeBox = false;
            var root = UnifiedBindingUi.Grid(1); root.Padding = new Padding(20); root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = UnifiedBindingUi.Label(I18n.Str("flow.close.question")); title.AutoSize = true; title.Font = new Font(Font, FontStyle.Bold);
            var detail = UnifiedBindingUi.Label(summary() + "\n\n" + I18n.Str(canApply ? "flow.close.hint" : "flow.close.unavailable"));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.RightToLeft };
            var save = UnifiedBindingUi.Button(I18n.Str(offline ? "flow.close.export" : "flow.close.apply"), "applyAndContinue"); save.Primary = true; save.Visible = canApply;
            var discard = UnifiedBindingUi.Button(I18n.Str("flow.close.discard"), "discardEdits"); discard.DialogResult = DialogResult.OK;
            var stay = UnifiedBindingUi.Button(I18n.Str("flow.close.stay"), "keepEditing"); stay.DialogResult = DialogResult.Cancel;
            bool busy = false;
            save.Click += async delegate {
                busy = true; buttons.Enabled = false;
                try
                {
                    if (await apply()) { DialogResult = DialogResult.OK; Close(); }
                    else detail.Text = summary() + "\n\n" + I18n.Str("flow.close.remaining");
                }
                finally { busy = false; buttons.Enabled = true; save.Enabled = canApplyNow(); }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy && DialogResult != DialogResult.OK) e.Cancel = true; };
            buttons.Controls.Add(save); buttons.Controls.Add(discard); buttons.Controls.Add(stay);
            root.Controls.Add(title, 0, 0); root.Controls.Add(detail, 0, 1); root.Controls.Add(buttons, 0, 2); Controls.Add(root);
            CancelButton = stay;
        }
    }
}
