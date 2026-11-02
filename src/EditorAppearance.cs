using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class EditorPanel : Panel
    {
        public EditorPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void WndProc(ref Message message)
        { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
    }

    internal sealed class EditorLayout : TableLayoutPanel
    {
        public EditorLayout() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void WndProc(ref Message message)
        { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
    }

    internal static class EditorArtwork
    {
        public static readonly Image Wallpaper = Load("PBZKeyForge.Wallpaper");
        public static readonly Image Wordmark = Load("PBZKeyForge.GameWordmark");
        public static readonly Image AppMark = Load("PBZKeyForge.AppMark");
        static Image Load(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }
        public static void PaintBackground(Graphics graphics, Rectangle bounds)
        {
            if (Wallpaper != null)
            {
                float ratio = Math.Max((float)bounds.Width / Wallpaper.Width, (float)bounds.Height / Wallpaper.Height);
                float width = Wallpaper.Width * ratio, height = Wallpaper.Height * ratio;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(Wallpaper, new RectangleF((bounds.Width - width) / 2f, 0, width, height));
                using (var shade = new LinearGradientBrush(bounds, Color.FromArgb(95, 10, 13, 15), Color.FromArgb(220, 10, 13, 15), 90f))
                    graphics.FillRectangle(shade, bounds);
            }
            else graphics.Clear(EditorTheme.Background);
        }
        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (diameter <= 0) { path.AddRectangle(rect); return path; }
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        public static void DarkCaption(Form form)
        {
            try
            {
                int enabled = 1, background = 0x00151310, foreground = 0x00E1EAEE;
                DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
                DwmSetWindowAttribute(form.Handle, 35, ref background, sizeof(int));
                DwmSetWindowAttribute(form.Handle, 36, ref foreground, sizeof(int));
            }
            catch { }
        }
        public static void DarkScrollbars(Control control)
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { }
        }
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
    }

    public class EditorButton : Button
    {
        protected bool Hovered, Pressed;
        public bool Primary;
        public EditorButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; BackColor = Color.Transparent;
        }
        protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hovered = Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void WndProc(ref Message message)
        { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            float scale = e.Graphics.DpiX / 96f;
            Color fill = !Enabled ? EditorTheme.Disabled : Primary ? (Pressed ? Color.FromArgb(143, 40, 45) : Hovered ? EditorTheme.AccentHover : EditorTheme.Accent)
                : Pressed ? EditorTheme.Surface : Hovered ? EditorTheme.Hover : EditorTheme.Floating;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = EditorArtwork.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5 * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Primary ? fill : Hovered ? EditorTheme.Muted : EditorTheme.Line))
            { e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? EditorTheme.Ink : EditorTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle((int)(4 * scale), (int)(4 * scale), Width - (int)(8 * scale), Height - (int)(8 * scale)), EditorTheme.Ink, fill);
        }
    }

    internal sealed class EditorComboBox : ComboBox
    {
        bool hovered;
        public EditorComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = EditorTheme.Floating; ForeColor = EditorTheme.Ink;
            ItemHeight = 24;
            DropDownHeight = 300;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (var g = CreateGraphics()) ItemHeight = Math.Max(Font.Height + (int)(6 * g.DpiY / 96f), (int)(26 * g.DpiY / 96f));
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? EditorTheme.Hover : EditorTheme.Floating)) e.Graphics.FillRectangle(brush, e.Bounds);
            var text = new Rectangle(e.Bounds.X + 9, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, text, EditorTheme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            bool print = message.Msg == 0x317 || message.Msg == 0x318;
            if (message.Msg != 0xF && !print || !IsHandleCreated) return;
            using (var graphics = print ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle))
            {
                float scale = graphics.DpiX / 96f;
                int arrowWidth = (int)(30 * scale);
                int inset = (int)(10 * scale);
                if (DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    using (var brush = new SolidBrush(Enabled ? EditorTheme.Floating : EditorTheme.Surface)) graphics.FillRectangle(brush, ClientRectangle);
                    TextRenderer.DrawText(graphics, Text, Font, new Rectangle(inset, 0, Math.Max(1, Width - arrowWidth - inset), Height), Enabled ? EditorTheme.Ink : EditorTheme.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
                using (var brush = new SolidBrush(EditorTheme.Floating)) graphics.FillRectangle(brush, Width - arrowWidth, 0, arrowWidth, Height);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(hovered || Focused ? EditorTheme.Muted : EditorTheme.Line))
                    graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                using (var pen = new Pen(EditorTheme.Muted, 1.5f * scale))
                {
                    float center = Width - arrowWidth / 2f, y = Height / 2f;
                    graphics.DrawLines(pen, new[] { new PointF(center - 4 * scale, y - 2 * scale), new PointF(center, y + 2 * scale), new PointF(center + 4 * scale, y - 2 * scale) });
                }
            }
        }
    }

    internal sealed class EditorCheckBox : CheckBox
    {
        bool hovered;
        public EditorCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            float scale = e.Graphics.DpiX / 96f;
            int size = (int)(18 * scale), y = (Height - size) / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = EditorArtwork.Rounded(new RectangleF(1, y, size, size), 3 * scale))
            using (var brush = new SolidBrush(Checked ? Enabled ? EditorTheme.Accent : EditorTheme.Line : EditorTheme.Floating))
            using (var pen = new Pen(Checked ? EditorTheme.Accent : hovered ? EditorTheme.Muted : EditorTheme.Line))
            { e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape); }
            if (Checked)
                using (var pen = new Pen(EditorTheme.Ink, 2 * scale))
                    e.Graphics.DrawLines(pen, new[] { new PointF(5 * scale, y + 9 * scale), new PointF(8 * scale, y + 12 * scale), new PointF(14 * scale, y + 6 * scale) });
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(size + (int)(10 * scale), 0, Width - size - (int)(10 * scale), Height), Enabled ? EditorTheme.Ink : EditorTheme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class EditorMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return EditorTheme.Floating; } }
        public override Color ImageMarginGradientBegin { get { return EditorTheme.Floating; } }
        public override Color ImageMarginGradientMiddle { get { return EditorTheme.Floating; } }
        public override Color ImageMarginGradientEnd { get { return EditorTheme.Floating; } }
        public override Color MenuBorder { get { return EditorTheme.Line; } }
        public override Color MenuItemBorder { get { return EditorTheme.Line; } }
        public override Color MenuItemSelected { get { return EditorTheme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return EditorTheme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return EditorTheme.Hover; } }
        public override Color SeparatorDark { get { return EditorTheme.Line; } }
        public override Color SeparatorLight { get { return EditorTheme.Line; } }
    }

    internal static class EditorDialogs
    {
        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons)
        {
            using (var dialog = new EditorWindow { Text = title, Font = new Font("Microsoft YaHei UI", 10f), BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Ink,
                AllowResize = false, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(580, 322), AutoScaleMode = AutoScaleMode.Dpi })
            {
                var content = new EditorPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 22, 24, 12), AutoScroll = true, BackColor = EditorTheme.Surface };
                var label = new Label { Left = 24, Top = 22, AutoSize = true, MaximumSize = new Size(510, 0), Text = text, ForeColor = EditorTheme.Ink };
                content.Controls.Add(label); dialog.Controls.Add(content);
                content.HandleCreated += delegate { EditorArtwork.DarkScrollbars(content); };
                var buttonBar = new EditorPanel { Dock = DockStyle.Bottom, Height = 64, BackColor = EditorTheme.Surface, Padding = new Padding(12, 12, 24, 12) };
                var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = EditorTheme.Surface, Margin = Padding.Empty };
                DialogResult[] results = buttons == MessageBoxButtons.YesNoCancel ? new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel }
                    : buttons == MessageBoxButtons.YesNo ? new[] { DialogResult.Yes, DialogResult.No }
                    : buttons == MessageBoxButtons.OKCancel ? new[] { DialogResult.OK, DialogResult.Cancel } : new[] { DialogResult.OK };
                foreach (var result in results)
                {
                    string key = result == DialogResult.OK ? "editor.ok" : result == DialogResult.Cancel ? "editor.cancel" : result == DialogResult.Yes ? "editor.yes" : "editor.no";
                    var button = new EditorButton { Width = 112, Height = 36, Margin = new Padding(6, 0, 0, 0), Text = I18n.Str(key), DialogResult = result, Primary = result == results[0] };
                    buttonRow.Controls.Add(button);
                    if (result == DialogResult.Cancel || result == DialogResult.No) dialog.CancelButton = button;
                }
                buttonBar.Controls.Add(buttonRow); dialog.Controls.Add(buttonBar);
                dialog.AutoScaleDimensions = new SizeF(96, 96); dialog.PerformAutoScale();
                EditorArtwork.DarkCaption(dialog);
                return dialog.ShowDialog(owner);
            }
        }
    }
}
