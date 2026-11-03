using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class ApplyActionButton : EditorButton
    {
        readonly Timer animation = new Timer { Interval = 30 };
        int angle;
        bool busy;
        public bool Busy
        {
            get { return busy; }
            set { busy = value; animation.Enabled = value; Invalidate(); }
        }
        public ApplyActionButton()
        {
            Primary = true;
            animation.Tick += delegate { angle = (angle + 12) % 360; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!busy) { base.OnPaint(e); return; }
            float scale = e.Graphics.DpiX / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = EditorArtwork.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5 * scale))
            using (var brush = new SolidBrush(EditorTheme.Accent)) e.Graphics.FillPath(brush, shape);
            float diameter = 17 * scale, left = 16 * scale;
            using (var pen = new Pen(EditorTheme.Ink, 2 * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                e.Graphics.DrawArc(pen, left, (Height - diameter) / 2f, diameter, diameter, angle, 265);
            }
            TextRenderer.DrawText(e.Graphics, I18n.Str("editor.applying"), Font,
                new Rectangle((int)(left + diameter + 10 * scale), 0, Math.Max(0, Width - (int)(left + diameter + 20 * scale)), Height),
                EditorTheme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }

    public partial class MainForm
    {
        public bool IsApplying { get; private set; }

        public async void ApplyChanges()
        {
            if (Session == null || IsApplying) return;
            if (Session.IsOffline)
            {
                modalActive = true;
                try
                {
                    using (var dialog = new SaveFileDialog { Title = I18n.Str("preset.export"), Filter = I18n.Str("preset.export") + " (*.sav)|*.sav",
                        FileName = "EnhancedInputUserSettings.sav", DefaultExt = "sav", AddExtension = true })
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                        {
                            Session.ExportCurrentScheme(dialog.FileName);
                            SetMessage("preset.exported", false);
                        }
                }
                catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
                finally { modalActive = false; RenderSession(); }
                return;
            }
            var disabled = Controls.Cast<Control>().Where(control => control != Caption && control.Enabled).ToArray();
            IsApplying = true;
            gameTimer.Stop();
            cbPreset.DroppedDown = false;
            foreach (var control in disabled) control.Enabled = false;
            captionClose.Enabled = false;
            ((ApplyActionButton)btnApply).Busy = true;
            lblStatus.Text = I18n.Str("editor.applying");
            try
            {
                var result = await Task.Run(() => Session.Apply());
                if (result.PartialSuccess) SetMessage("editor.partial", true, result.Error);
                else if (!result.Success) SetMessage("editor.failed", true, result.Error);
                else SetMessage("editor.saved", false);
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally
            {
                IsApplying = false;
                ((ApplyActionButton)btnApply).Busy = false;
                foreach (var control in disabled) control.Enabled = true;
                captionClose.Enabled = true;
                gameTimer.Start();
                RenderSession();
            }
        }
    }
}
