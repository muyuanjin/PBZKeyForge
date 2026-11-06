using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class NoticeCard : EditorPanel
    {
        internal static readonly Color Accent = Color.FromArgb(245, 187, 65);
        readonly Timer animation = new Timer { Interval = 80 };
        readonly Stopwatch elapsed = Stopwatch.StartNew();
        public NoticeCard()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(49, 43, 29);
            animation.Tick += delegate { if (FindForm() != null && FindForm().WindowState != FormWindowState.Minimized) Invalidate(); };
        }
        protected override void OnVisibleChanged(EventArgs e)
        { base.OnVisibleChanged(e); animation.Enabled = Visible; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(EditorTheme.Surface);
            float scale = e.Graphics.DpiX / 96f;
            double pulse = (Math.Sin(elapsed.Elapsed.TotalSeconds * Math.PI / 2) + 1) / 2;
            var bounds = new RectangleF(1, 3 * scale, Math.Max(1, Width - 2), Math.Max(1, Height - 6 * scale));
            using (var shape = EditorArtwork.Rounded(bounds, 6 * scale))
            using (var fill = new SolidBrush(Color.FromArgb(49 + (int)(pulse * 10), 43 + (int)(pulse * 6), 29)))
            using (var border = new Pen(Color.FromArgb(150 + (int)(pulse * 80), 240, 178, 62), scale))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
            }
            int size = Math.Min((int)(25 * scale), Math.Max((int)(12 * scale), Height - (int)(10 * scale)));
            var icon = new Rectangle((int)(6 * scale), (Height - size) / 2, size, size);
            using (var brush = new SolidBrush(Accent)) e.Graphics.FillEllipse(brush, icon);
            TextRenderer.DrawText(e.Graphics, "!", Font, icon, Color.FromArgb(36, 30, 19),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
}
