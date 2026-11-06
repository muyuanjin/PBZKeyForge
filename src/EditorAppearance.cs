using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal class EditorPanel : Panel
    {
        public EditorPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); if (BackColor.A == 255) EditorArtwork.PaintEffect(this, e.Graphics); }
        protected override void WndProc(ref Message message)
        { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
    }

    internal sealed class EditorLayout : TableLayoutPanel
    {
        public EditorLayout() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); if (BackColor.A == 255) EditorArtwork.PaintEffect(this, e.Graphics); }
        protected override void WndProc(ref Message message)
        { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
    }

    internal class EditorLabel : Label
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); if (BackColor.A == 255) EditorArtwork.PaintEffect(this, e.Graphics); }
    }

    internal sealed class EditorLinkLabel : LinkLabel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); if (BackColor.A == 255) EditorArtwork.PaintEffect(this, e.Graphics); }
    }

    internal static class EditorArtwork
    {
        internal static void PaintEffect(Control surface, Graphics graphics)
        {
            var window = surface.FindForm() as MainForm;
            if (window != null) window.PaintFlameBackground(surface, graphics);
        }

        internal static void ClearSurface(Control surface, Graphics graphics, Color color)
        {
            graphics.Clear(color);
            PaintEffect(surface, graphics);
        }
        public static readonly Image Wallpaper = Load("PBZKeyForge.Wallpaper");
        public static readonly Image Wordmark = Load("PBZKeyForge.GameWordmark");
        public static readonly Image AppMark = Load("PBZKeyForge.AppMark");
        public static readonly Image KeyEnhancementMark = Load("PBZKeyForge.KeyEnhancementMark");
        public static readonly Image Mouse = Load("PBZKeyForge.Device.mouse");
        public static readonly Image Keyboard = Load("PBZKeyForge.Device.keyboard");
        static readonly Lazy<Image> xbox = new Lazy<Image>(() => Load("PBZKeyForge.Device.xbox"));
        static readonly Lazy<Image> playStation = new Lazy<Image>(() => Load("PBZKeyForge.Device.playstation"));
        public static Image Xbox { get { return xbox.Value; } }
        public static Image PlayStation { get { return playStation.Value; } }
        static readonly Dictionary<string, Image> actionIcons = new Dictionary<string, Image>();
        public static Image ActionIcon(string action)
        {
            if (string.IsNullOrEmpty(action)) return null;
            Image icon;
            if (!actionIcons.TryGetValue(action, out icon))
            { icon = Load("PBZKeyForge.Action." + action); actionIcons[action] = icon; }
            return icon;
        }
        static Image Load(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var source = Image.FromStream(stream))
                {
                    var bitmap = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImageUnscaled(source, 0, 0);
                    return bitmap;
                }
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
            foreach (Control child in control.Controls)
                if (child is ScrollBar) DarkScrollbars(child);
        }
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
    }

    public class EditorButton : Button
    {
        public bool Multiline;
        public string ActionId;
        protected bool Hovered, Pressed;
        public bool Primary;
        public Color AccentColor = Color.Empty;
        public EditorButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; BackColor = Color.Transparent; ForeColor = EditorTheme.Ink;
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
            if (Enabled && Primary && !AccentColor.IsEmpty) fill = Pressed ? ControlPaint.Dark(AccentColor) : Hovered ? ControlPaint.Light(AccentColor) : AccentColor;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = EditorArtwork.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5 * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Primary ? fill : Hovered ? EditorTheme.Muted : EditorTheme.Line))
            { e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape); }
            Rectangle textBounds = ClientRectangle;
            var actionIcon = EditorArtwork.ActionIcon(ActionId);
            if (actionIcon != null)
            {
                int size = (int)(24 * scale), inset = (int)(12 * scale);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(actionIcon, new Rectangle(inset, (Height - size) / 2, size, size));
                textBounds = new Rectangle(inset + size + (int)(10 * scale), 0, Math.Max(0, Width - inset * 2 - size - (int)(10 * scale)), Height);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : EditorTheme.Muted,
                (actionIcon == null ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter
                | (Multiline ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine) | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle((int)(4 * scale), (int)(4 * scale), Width - (int)(8 * scale), Height - (int)(8 * scale)), EditorTheme.Ink, fill);
        }
    }

    internal class EditorComboBox : ComboBox
    {
        bool hovered;
        public bool CenterText;
        [StructLayout(LayoutKind.Sequential)]
        struct ComboBoxInfo
        {
            public int Size;
            public Rectangle Item, Button;
            public int ButtonState;
            public IntPtr ComboHandle, EditHandle, ListHandle;
        }
        [DllImport("user32.dll")]
        static extern bool GetComboBoxInfo(IntPtr handle, ref ComboBoxInfo info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetWindowLong(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        static extern int SetWindowLong(IntPtr handle, int index, int value);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        [StructLayout(LayoutKind.Sequential)]
        struct PaintInfo
        {
            public IntPtr Dc;
            public int Erase;
            public int Left, Top, Right, Bottom;
            public int Restore, Update;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
        }
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr window, out PaintInfo paint);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr window, ref PaintInfo paint);
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
            AlignEdit();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); AlignEdit(); }
        void AlignEdit()
        {
            if (!CenterText || !IsHandleCreated || DropDownStyle != ComboBoxStyle.DropDown) return;
            var info = new ComboBoxInfo { Size = Marshal.SizeOf(typeof(ComboBoxInfo)) };
            if (!GetComboBoxInfo(Handle, ref info)) return;
            SetWindowLong(info.EditHandle, -16, (GetWindowLong(info.EditHandle, -16) & ~3) | 1);
            using (var graphics = CreateGraphics())
            {
                int inset = (int)(10 * graphics.DpiX / 96f), arrowWidth = (int)(30 * graphics.DpiX / 96f);
                SetWindowPos(info.EditHandle, IntPtr.Zero, inset, (Height - Font.Height) / 2,
                    Math.Max(1, Width - arrowWidth - inset * 2), Font.Height, 0x14);
            }
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? EditorTheme.Hover : EditorTheme.Floating)) e.Graphics.FillRectangle(brush, e.Bounds);
            var text = new Rectangle(e.Bounds.X + 9, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height);
            var action = Items[e.Index] as ActionInfo;
            Image icon = action == null ? null : EditorArtwork.ActionIcon(action.Id);
            if (icon != null)
            {
                int size = Math.Min(e.Bounds.Height - 4, Font.Height + 4);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(icon, new Rectangle(text.Left, text.Top + (text.Height - size) / 2, size, size));
                text.X += size + 8; text.Width = Math.Max(0, text.Width - size - 8);
            }
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, text, EditorTheme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | (CenterText ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x201 && IsHandleCreated)
            {
                int x = (short)(message.LParam.ToInt64() & 0xffff);
                using (var graphics = CreateGraphics())
                    if (x >= Width - (int)(30 * graphics.DpiX / 96f))
                    { Focus(); DroppedDown = !DroppedDown; message.Result = IntPtr.Zero; return; }
            }
            if (message.Msg == 0xF && IsHandleCreated)
            {
                PaintInfo paint;
                IntPtr dc = BeginPaint(Handle, out paint);
                try
                {
                    using (var graphics = Graphics.FromHdc(dc))
                    using (var buffer = BufferedGraphicsManager.Current.Allocate(graphics, ClientRectangle))
                    {
                        IntPtr bufferedDc = buffer.Graphics.GetHdc();
                        try
                        {
                            var print = Message.Create(Handle, 0x318, bufferedDc, new IntPtr(0xC));
                            base.WndProc(ref print);
                        }
                        finally { buffer.Graphics.ReleaseHdc(bufferedDc); }
                        PaintChrome(buffer.Graphics); buffer.Render();
                    }
                }
                finally { EndPaint(Handle, ref paint); }
                message.Result = IntPtr.Zero; return;
            }
            if (message.Msg == 0x14 && IsHandleCreated) { message.Result = new IntPtr(1); return; }
            base.WndProc(ref message);
            if ((message.Msg == 0x317 || message.Msg == 0x318) && IsHandleCreated)
                using (var graphics = Graphics.FromHdc(message.WParam)) PaintChrome(graphics);
        }
        void PaintChrome(Graphics graphics)
        {
            float scale = graphics.DpiX / 96f;
            int arrowWidth = (int)(30 * scale);
            int inset = (int)(10 * scale);
            if (CenterText && DropDownStyle == ComboBoxStyle.DropDown)
            {
                var state = graphics.Save();
                graphics.ExcludeClip(new Rectangle(inset, (Height - Font.Height) / 2, Math.Max(1, Width - arrowWidth - inset * 2), Font.Height));
                using (var brush = new SolidBrush(EditorTheme.Floating)) graphics.FillRectangle(brush, ClientRectangle);
                graphics.Restore(state);
            }
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

    internal sealed class EditorCheckBox : CheckBox
    {
        bool hovered;
        public Color CheckedColor = EditorTheme.Accent;
        public EditorCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            EditorArtwork.ClearSurface(this, e.Graphics, BackColor);
            float scale = e.Graphics.DpiX / 96f;
            int size = (int)(18 * scale), y = (Height - size) / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = EditorArtwork.Rounded(new RectangleF(1, y, size, size), 3 * scale))
            using (var brush = new SolidBrush(Checked ? Enabled ? CheckedColor : EditorTheme.Line : EditorTheme.Floating))
            using (var pen = new Pen(Checked ? CheckedColor : hovered ? EditorTheme.Muted : EditorTheme.Line))
            { e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape); }
            if (Checked)
                using (var pen = new Pen(EditorTheme.Ink, 2 * scale))
                    e.Graphics.DrawLines(pen, new[] { new PointF(5 * scale, y + 9 * scale), new PointF(8 * scale, y + 12 * scale), new PointF(14 * scale, y + 6 * scale) });
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(size + (int)(10 * scale), 0, Width - size - (int)(10 * scale), Height), Enabled ? EditorTheme.Ink : EditorTheme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class EditorMenuRenderer : ToolStripProfessionalRenderer
    {
        public EditorMenuRenderer() : base(new EditorMenuColors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? EditorTheme.Ink : EditorTheme.Muted;
            if (e.Item.Enabled) base.OnRenderItemText(e);
            else TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, e.TextFormat);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? EditorTheme.Ink : EditorTheme.Muted;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle bounds = e.ImageRectangle;
            using (var pen = new Pen(e.Item.Enabled ? EditorTheme.Ink : EditorTheme.Muted, Math.Max(1.5f, e.Graphics.DpiX / 64f)))
                e.Graphics.DrawLines(pen, new[] {
                    new Point(bounds.Left + bounds.Width / 5, bounds.Top + bounds.Height / 2),
                    new Point(bounds.Left + bounds.Width * 2 / 5, bounds.Top + bounds.Height * 3 / 4),
                    new Point(bounds.Left + bounds.Width * 4 / 5, bounds.Top + bounds.Height / 4)
                });
        }
    }

    internal sealed class EditorMenuColors : ProfessionalColorTable
    {
        public EditorMenuColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return EditorTheme.Floating; } }
        public override Color ImageMarginGradientBegin { get { return EditorTheme.Floating; } }
        public override Color ImageMarginGradientMiddle { get { return EditorTheme.Floating; } }
        public override Color ImageMarginGradientEnd { get { return EditorTheme.Floating; } }
        public override Color MenuBorder { get { return EditorTheme.Line; } }
        public override Color MenuItemBorder { get { return EditorTheme.Line; } }
        public override Color MenuItemSelected { get { return EditorTheme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return EditorTheme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return EditorTheme.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return EditorTheme.Hover; } }
        public override Color MenuItemPressedGradientMiddle { get { return EditorTheme.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return EditorTheme.Hover; } }
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
