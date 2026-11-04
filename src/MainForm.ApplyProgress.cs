using System;
using System.Diagnostics;
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
        readonly Stopwatch elapsed = new Stopwatch();
        int angle;
        bool busy, gameRunning;
        public bool GameRunning
        {
            get { return gameRunning; }
            set { if (gameRunning != value) { gameRunning = value; Invalidate(); } }
        }
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value;
                if (value) { angle = 0; elapsed.Restart(); }
                else elapsed.Stop();
                animation.Enabled = value;
                Invalidate();
            }
        }
        public ApplyActionButton()
        {
            Primary = true;
            animation.Tick += delegate
            {
                if (elapsed.ElapsedMilliseconds < 150) return;
                angle = (angle + 12) % 360;
                Invalidate();
            };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!busy && !gameRunning) { base.OnPaint(e); return; }
            float scale = e.Graphics.DpiX / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color warning = Color.FromArgb(240, 178, 78);
            using (var shape = EditorArtwork.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5 * scale))
            using (var brush = new SolidBrush(busy ? EditorTheme.Accent : EditorTheme.Disabled))
            {
                e.Graphics.FillPath(brush, shape);
                if (!busy) using (var pen = new Pen(warning, scale)) e.Graphics.DrawPath(pen, shape);
            }
            if (!busy || elapsed.ElapsedMilliseconds < 150)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, busy ? EditorTheme.Ink : warning,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }
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
                finally { EndModal(); RenderSession(); }
                return;
            }
            var disabled = Controls.Cast<Control>().Where(control => control != Caption && control.Enabled).ToArray();
            IsApplying = true;
            gameTimer.Stop();
            cbPreset.DroppedDown = false;
            ((ApplyActionButton)btnApply).Busy = true;
            foreach (var control in disabled) control.Enabled = false;
            captionClose.Enabled = false;
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
                ConsumeSchemeDiscovery();
                foreach (var control in disabled) control.Enabled = true;
                captionClose.Enabled = true;
                RenderSession();
                ((ApplyActionButton)btnApply).Busy = false;
                gameTimer.Start();
            }
        }
    }
}
