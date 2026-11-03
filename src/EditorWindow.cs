using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public class EditorWindow : Form
    {
        protected readonly Panel Caption = new EditorPanel();
        readonly Label captionTitle = new CaptionLabel();
        public readonly Button captionMinimize = new CaptionButton(0);
        public readonly Button captionMaximize = new CaptionButton(1);
        public readonly Button captionClose = new CaptionButton(2);
        readonly ContextMenuStrip windowMenu = new ContextMenuStrip();
        readonly ToolStripMenuItem menuRestore = new ToolStripMenuItem();
        readonly ToolStripMenuItem menuMinimize = new ToolStripMenuItem();
        readonly ToolStripMenuItem menuMaximize = new ToolStripMenuItem();
        readonly ToolStripMenuItem menuClose = new ToolStripMenuItem();
        public bool AllowResize = true;
        Rectangle normalBounds;
        FormWindowState previousWindowState;
        bool restoringBounds;

        public EditorWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            Padding = new Padding(1);
            windowMenu.Font = Font; windowMenu.BackColor = EditorTheme.Floating; windowMenu.ForeColor = EditorTheme.Ink;
            windowMenu.Renderer = new ToolStripProfessionalRenderer(new EditorMenuColors());
            windowMenu.ShowImageMargin = false; windowMenu.Padding = new Padding(5);
            Caption.Dock = DockStyle.Top; Caption.Height = 32; Caption.BackColor = EditorTheme.Background;
            captionTitle.Dock = DockStyle.Fill; captionTitle.TextAlign = ContentAlignment.MiddleLeft; captionTitle.AutoEllipsis = true;
            captionTitle.Padding = new Padding(12, 0, 0, 0); captionTitle.ForeColor = EditorTheme.Muted;
            captionTitle.Font = new Font("Microsoft YaHei UI", 9f);
            captionClose.Dock = captionMaximize.Dock = captionMinimize.Dock = DockStyle.Right;
            captionClose.Width = captionMaximize.Width = captionMinimize.Width = 46;
            windowMenu.Items.AddRange(new ToolStripItem[] { menuRestore, menuMinimize, menuMaximize, new ToolStripSeparator(), menuClose });
            foreach (ToolStripItem item in windowMenu.Items) item.Padding = new Padding(10, 6, 10, 6);
            menuRestore.Click += delegate { WindowState = FormWindowState.Normal; };
            menuMinimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            menuMaximize.Click += delegate { ToggleMaximize(); };
            menuClose.Click += delegate { Close(); };
            windowMenu.Opening += delegate
            {
                menuRestore.Text = I18n.Str("editor.restore");
                menuMinimize.Text = I18n.Str("editor.minimize");
                menuMaximize.Text = I18n.Str("editor.maximize");
                menuClose.Text = I18n.Str("editor.close");
                menuRestore.Enabled = WindowState != FormWindowState.Normal;
                menuMinimize.Enabled = WindowState != FormWindowState.Minimized;
                menuMaximize.Enabled = MaximizeBox && WindowState != FormWindowState.Maximized;
            };
            Caption.Controls.Add(captionTitle); Caption.Controls.Add(captionMinimize); Caption.Controls.Add(captionMaximize); Caption.Controls.Add(captionClose);
            Controls.Add(Caption);
            captionMinimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            captionMaximize.Click += delegate { ToggleMaximize(); };
            captionClose.Click += delegate { Close(); };
            MouseEventHandler move = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
                else if (e.Button == MouseButtons.Right) ShowSystemMenu(Cursor.Position);
            };
            Caption.MouseDown += move; captionTitle.MouseDown += move;
            Caption.DoubleClick += delegate { ToggleMaximize(); }; captionTitle.DoubleClick += delegate { ToggleMaximize(); };
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                value.Style &= ~(0x00C00000 | 0x00080000 | 0x00020000 | 0x00010000);
                if (AllowResize) value.Style |= 0x00040000;
                else value.Style &= ~0x00040000;
                return value;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) windowMenu.Dispose();
            base.Dispose(disposing);
        }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (Caption != null && e.Control != Caption && Controls.Contains(Caption)) Caption.SendToBack();
        }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (captionTitle != null) captionTitle.Text = Text;
            if (captionMinimize != null) captionMinimize.AccessibleName = I18n.Str("editor.minimize");
            if (captionMaximize != null) captionMaximize.AccessibleName = I18n.Str(WindowState == FormWindowState.Maximized ? "editor.restore" : "editor.maximize");
            if (captionClose != null) captionClose.AccessibleName = I18n.Str("editor.close");
        }
        protected override void OnShown(EventArgs e)
        {
            captionMinimize.Visible = MinimizeBox; captionMaximize.Visible = MaximizeBox;
            LayoutChrome();
            base.OnShown(e);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // The client owns the entire frame, including its activation appearance.
            int policy = 1; // DWMNCRP_DISABLED
            DwmSetWindowAttribute(Handle, 2, ref policy, sizeof(int));
            LayoutChrome();
        }
        void LayoutChrome()
        {
            if (!IsHandleCreated) return;
            using (var graphics = CreateGraphics())
            {
                float scale = graphics.DpiX / 96f;
                Caption.Height = (int)Math.Round(32 * scale);
                captionMinimize.Width = captionMaximize.Width = captionClose.Width = (int)Math.Round(46 * scale);
                captionTitle.Padding = new Padding((int)Math.Round(12 * scale), 0, 0, 0);
            }
        }
        protected override void OnResize(EventArgs e)
        {
            if (!restoringBounds && WindowState == FormWindowState.Normal)
            {
                if (previousWindowState != FormWindowState.Normal && normalBounds.Width > 0)
                {
                    // WinForms includes the hidden thick frame when restoring its cached client size.
                    restoringBounds = true;
                    Rectangle restored = normalBounds;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        try { if (!IsDisposed && WindowState == FormWindowState.Normal) Bounds = restored; }
                        finally { restoringBounds = false; normalBounds = Bounds; }
                    });
                }
                else normalBounds = Bounds;
            }
            previousWindowState = WindowState;
            base.OnResize(e);
            if (captionMaximize != null) { captionMaximize.Invalidate(); captionMaximize.AccessibleName = I18n.Str(WindowState == FormWindowState.Maximized ? "editor.restore" : "editor.maximize"); }
        }
        protected override void OnLocationChanged(EventArgs e)
        {
            if (!restoringBounds && WindowState == FormWindowState.Normal && previousWindowState == FormWindowState.Normal) normalBounds = Bounds;
            base.OnLocationChanged(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
        }
        void ToggleMaximize()
        {
            if (MaximizeBox) WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.Space)) { ShowSystemMenu(PointToScreen(new Point(0, Caption.Height))); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        void ShowSystemMenu(Point position)
        {
            windowMenu.Show(position);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x83) { message.Result = IntPtr.Zero; return; }
            // WS_THICKFRAME retains native sizing, but must not paint a native border.
            if (message.Msg == 0x85 || message.Msg == 0xAE || message.Msg == 0xAF)
            { message.Result = IntPtr.Zero; return; }
            if (message.Msg == 0x86)
            {
                message.LParam = new IntPtr(-1); // Process activation without repainting the frame.
                base.WndProc(ref message); return;
            }
            if (message.Msg == 0x24)
            {
                var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(MonitorFromWindow(Handle, 2), ref monitor))
                {
                    var info = (MinMaxInfo)Marshal.PtrToStructure(message.LParam, typeof(MinMaxInfo));
                    info.MaxPosition.X = monitor.Work.Left - monitor.Monitor.Left; info.MaxPosition.Y = monitor.Work.Top - monitor.Monitor.Top;
                    info.MaxSize.X = monitor.Work.Right - monitor.Work.Left; info.MaxSize.Y = monitor.Work.Bottom - monitor.Work.Top;
                    info.MinTrackSize.X = MinimumSize.Width; info.MinTrackSize.Y = MinimumSize.Height;
                    Marshal.StructureToPtr(info, message.LParam, false); message.Result = IntPtr.Zero; return;
                }
            }
            base.WndProc(ref message);
            if (message.Msg == 0x84 && AllowResize && WindowState == FormWindowState.Normal)
                message.Result = (IntPtr)ResizeHit(message.LParam);
        }
        int ResizeHit(IntPtr position)
        {
            long packed = position.ToInt64();
            Point point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            if (point.X < 0 || point.Y < 0 || point.X >= ClientSize.Width || point.Y >= ClientSize.Height) return 1;
            int edge = Math.Max(5, (int)(6 * CurrentAutoScaleDimensions.Width / 96f));
            bool left = point.X < edge, right = point.X >= ClientSize.Width - edge, top = point.Y < edge, bottom = point.Y >= ClientSize.Height - edge;
            return top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
        }
        internal static bool PassResizeHit(Control control, ref Message message)
        {
            if (message.Msg != 0x84) return false;
            var window = control.FindForm() as EditorWindow;
            if (window == null || !window.AllowResize || window.WindowState != FormWindowState.Normal || window.ResizeHit(message.LParam) == 1) return false;
            // Child windows cover most client edges. Let Windows continue the hit test at the frame.
            message.Result = (IntPtr)(-1); return true;
        }
        [StructLayout(LayoutKind.Sequential)] struct WinPoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo { public WinPoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public WinRect Monitor, Work; public int Flags; }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }

    internal sealed class CaptionLabel : Label
    {
        protected override void WndProc(ref Message message)
        { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
    }

    internal sealed class CaptionButton : EditorButton
    {
        readonly int kind;
        public CaptionButton(int value) { kind = value; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Hovered ? kind == 2 ? EditorTheme.Accent : EditorTheme.Hover : EditorTheme.Background);
            float scale = e.Graphics.DpiX / 96f, size = 10 * scale, x = (Width - size) / 2, y = (Height - size) / 2;
            using (var pen = new Pen(EditorTheme.Ink, Math.Max(1, scale)))
            {
                if (kind == 0) e.Graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2);
                else if (kind == 2) { e.Graphics.DrawLine(pen, x, y, x + size, y + size); e.Graphics.DrawLine(pen, x + size, y, x, y + size); }
                else if (FindForm().WindowState == FormWindowState.Maximized)
                {
                    e.Graphics.DrawRectangle(pen, x + 3 * scale, y, size - 3 * scale, size - 3 * scale);
                    using (var brush = new SolidBrush(Hovered ? EditorTheme.Hover : EditorTheme.Background)) e.Graphics.FillRectangle(brush, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
                    e.Graphics.DrawRectangle(pen, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
                }
                else e.Graphics.DrawRectangle(pen, x, y, size, size);
            }
        }
    }
}
