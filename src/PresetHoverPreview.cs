using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class PresetHoverPreview : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ComboInfo
        {
            public int Size;
            public Rectangle Item, Button;
            public int ButtonState;
            public IntPtr Combo, Edit, List;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct MouseTracking { public int Size, Flags; public IntPtr Window; public int HoverTime; }
        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr handle, ref ComboInfo info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
        [DllImport("user32.dll")] static extern bool TrackMouseEvent(ref MouseTracking tracking);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        readonly ComboBox picker;
        readonly Func<EditorSession> session;
        readonly Func<bool> gamepad;
        readonly Func<int> family;
        readonly ListMouseWindow list;
        PreviewWindow popup;
        Preset hovered;

        sealed class ListMouseWindow : NativeWindow
        {
            readonly PresetHoverPreview owner;
            public ListMouseWindow(PresetHoverPreview owner) { this.owner = owner; }
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x200)
                {
                    var tracking = new MouseTracking { Size = Marshal.SizeOf(typeof(MouseTracking)), Flags = 2, Window = Handle };
                    TrackMouseEvent(ref tracking);
                    long position = message.LParam.ToInt64();
                    owner.Hover(Handle, new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff)));
                }
                else if (message.Msg == 0x2A3) owner.CheckPointer(null, EventArgs.Empty);
            }
        }
        public PresetHoverPreview(ComboBox picker, Func<EditorSession> session, Func<bool> gamepad, Func<int> family)
        {
            this.picker = picker; this.session = session; this.gamepad = gamepad; this.family = family;
            list = new ListMouseWindow(this);
            picker.DropDown += Start; picker.DropDownClosed += Closed;
        }
        void Start(object sender, EventArgs e)
        {
            var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
            if (!GetComboBoxInfo(picker.Handle, ref info)) return;
            if (list.Handle != IntPtr.Zero) list.ReleaseHandle();
            list.AssignHandle(info.List);
        }
        void Closed(object sender, EventArgs e)
        {
            DisposePopup();
            if (list.Handle != IntPtr.Zero) list.ReleaseHandle();
        }
        void Hide()
        { if (popup != null) popup.Hide(); }

        void DisposePopup()
        {
            if (popup != null) { popup.Dispose(); popup = null; }
            hovered = null;
        }
        void CheckPointer(object sender, EventArgs e)
        {
            Point pointer = Cursor.Position;
            if (popup != null && popup.Visible && popup.Bounds.Contains(pointer)) return;
            NativeRect rectangle;
            if (list.Handle != IntPtr.Zero && GetWindowRect(list.Handle, out rectangle)
                && Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom).Contains(pointer)) return;
            Hide();
        }
        void MonitorPointer(Control control)
        {
            control.MouseLeave += CheckPointer;
            foreach (Control child in control.Controls) MonitorPointer(child);
        }
        void Hover(IntPtr handle, Point local)
        {
            if (!picker.DroppedDown || !picker.Enabled || session() == null) { Hide(); return; }
            int item = SendMessage(handle, 0x1A9, IntPtr.Zero, new IntPtr((local.Y << 16) | (local.X & 0xffff))).ToInt32();
            int index = item & 0xffff;
            Preset candidate = (item >> 16) == 0 && index < picker.Items.Count ? picker.Items[index] as Preset : null;
            if (candidate == null) { Hide(); return; }
            bool reuse = ReferenceEquals(candidate, hovered) && popup != null;
            if (reuse && popup.Visible) return;
            NativeRect rectangle; GetWindowRect(handle, out rectangle);
            Rectangle anchor = Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            Rectangle work = Screen.FromControl(picker).WorkingArea;
            int leftSpace = anchor.Left - work.Left - 8, rightSpace = work.Right - anchor.Right - 8;
            bool left = leftSpace >= rightSpace;
            var available = new Rectangle(work.Left, work.Top, Math.Max(1, Math.Max(leftSpace, rightSpace)), work.Height);
            if (!reuse)
            {
                DisposePopup(); hovered = candidate;
                try
                {
                    var snapshot = session().PreviewPreset(candidate);
                    bool pad = candidate.IsUser ? !candidate.Scopes.Any(scope => scope.Family == null)
                        || candidate.UserMappings.Any(mapping => mapping.Family != null) && !candidate.UserMappings.Any(mapping => mapping.Family == null)
                        : candidate.Gamepad.Count > 0 && candidate.Keyboard.Count == 0 || candidate.Gamepad.Count == 0 && candidate.Keyboard.Count == 0 && gamepad();
                    popup = new PreviewWindow(picker.Font, candidate, snapshot, pad, family(), available);
                }
                catch (Exception ex) { popup = new PreviewWindow(picker.Font, candidate.Name, ex.Message); }
                MonitorPointer(popup);
            }
            int x = left ? anchor.Left - popup.Width - 8 : anchor.Right + 8;
            int y = Math.Max(work.Top, Math.Min(anchor.Top + local.Y - popup.Height / 3, work.Bottom - popup.Height));
            popup.ShowAt(picker.FindForm().Handle, new Point(x, y));
        }
        public void Dispose()
        {
            Closed(null, EventArgs.Empty);
            picker.DropDown -= Start; picker.DropDownClosed -= Closed;
        }

        internal sealed class PreviewWindow : Panel
        {
            readonly PictureBox picture = new PictureBox { Dock = DockStyle.Top, SizeMode = PictureBoxSizeMode.Zoom };
            IntPtr owner;
            [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
            protected override CreateParams CreateParams
            {
                get
                {
                    var value = base.CreateParams;
                    value.Style = (value.Style & ~0x50000000) | unchecked((int)0x80000000);
                    value.ExStyle |= 0x08000088; value.Parent = owner;
                    return value;
                }
            }
            public void ShowAt(IntPtr owner, Point location)
            {
                this.owner = owner; Location = location;
                CreateControl(); PerformLayout(); Show();
                SetWindowPos(Handle, new IntPtr(-1), location.X, location.Y, Width, Height, 0x50);
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
                base.WndProc(ref message);
            }
            public PreviewWindow(Font font, string title, string message)
            {
                Font = font; DoubleBuffered = true;
                BackColor = EditorTheme.Floating; ForeColor = EditorTheme.Ink;
                float scale; using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
                Padding = new Padding((int)(12 * scale));
                Width = (int)(560 * scale);
                if (!string.IsNullOrEmpty(message))
                {
                    var text = TextLabel(title + "\n\n" + message, Width - Padding.Horizontal);
                    Controls.Add(text); Height = text.Height + Padding.Vertical;
                }
            }
            public PreviewWindow(Font font, Preset preset, EditorSession snapshot, bool pad, int familyIndex, Rectangle work)
                : this(font, preset.Name, "")
            {
                float scale; using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
                Width = Math.Min(Width, work.Width);
                int width = ClientSize.Width - Padding.Horizontal;
                var title = TextLabel(preset.Name, width);
                var description = TextLabel(preset.Desc, width); description.ForeColor = EditorTheme.Muted;
                var comparison = TextLabel("● " + I18n.Str("preset.previewchanges"), width);
                comparison.ForeColor = BindingStatus.Custom;
                var factory = TextLabel("● " + I18n.Str("views.original"), width);
                factory.ForeColor = BindingStatus.Factory;
                var rows = new EditorPanel { Dock = DockStyle.Top, AutoScroll = true };
                var differences = snapshot.SchemeDifferences(true);
                int rowHeight = 0;
                foreach (var difference in differences.Reverse())
                {
                    string before = BindingViewText.Keys(difference.BeforeKeys);
                    string after = BindingViewText.Keys(difference.AfterKeys);
                    var label = TextLabel(I18n.Str(difference.Gamepad ? "editor.gamepad" : "editor.keyboard") + " · "
                        + BindingViewText.ActionName(difference.Action) + ": " + before + " → " + after, width - (int)(20 * scale));
                    label.ForeColor = BindingStatus.Custom;
                    rows.Controls.Add(label); rowHeight += label.Height;
                }
                if (differences.Length == 0)
                {
                    var label = TextLabel(I18n.Str("views.nofactorydiff"), width);
                    rows.Controls.Add(label); rowHeight += label.Height;
                }
                var selectedFamily = preset.IsUser && pad ? snapshot.ControllerFamilies.FirstOrDefault(item => preset.Scopes.Any(scope => scope.Family == item.Name)
                    && preset.UserMappings.Any(mapping => mapping.Family == item.Name))
                    ?? snapshot.ControllerFamilies.FirstOrDefault(item => preset.Scopes.Any(scope => scope.Family == item.Name)) : null;
                selectedFamily = selectedFamily ?? snapshot.ControllerFamilies.FirstOrDefault(item => item.Index == familyIndex) ?? snapshot.ControllerFamilies.FirstOrDefault();
                var deviceLabel = TextLabel(I18n.Str(pad ? "editor.gamepad" : "editor.keyboard") + (pad && selectedFamily != null ? " · " + selectedFamily.Name : ""), width);
                deviceLabel.ForeColor = BindingStatus.Factory;
                int index = selectedFamily == null ? -1 : selectedFamily.Index;
                var relations = snapshot.BindingActions(pad, index).Select(action => BindingViewText.PhysicalRelations(snapshot.Relations(action, pad, index))).ToArray();
                var keys = relations.SelectMany(relation => relation.Keys.Concat(relation.DefaultKeys ?? new string[0])).Distinct().ToArray();
                Func<string, KeyBadgeState> state = key => new KeyBadgeState {
                    Count = relations.Count(relation => relation.Keys.Contains(key)), Complete = relations.All(relation => relation.KnowsKey(key)),
                    Differs = relations.Any(relation => relation.DefaultKeys != null && relation.Keys.Contains(key) != relation.DefaultKeys.Contains(key)) };
                using (var diagram = new DeviceDiagram(pad, selectedFamily == null ? null : selectedFamily.Name, state, delegate { }, null, keys))
                {
                    diagram.Font = font; diagram.Size = new Size((int)(1000 * scale), (int)(330 * scale));
                    diagram.CreateControl(); diagram.PerformLayout();
                    var image = new Bitmap(diagram.Width, diagram.Height);
                    diagram.DrawToBitmap(image, diagram.ClientRectangle); picture.Image = image;
                }
                picture.Height = (int)(width * .33f);
                int fixedHeight = Padding.Vertical + title.Height + description.Height + picture.Height + deviceLabel.Height + comparison.Height + factory.Height;
                rows.Height = Math.Max(Font.Height * 2, Math.Min(rowHeight, Math.Min((int)(260 * scale), work.Height - fixedHeight - (int)(40 * scale))));
                Controls.Add(rows); Controls.Add(comparison); Controls.Add(factory); Controls.Add(picture); Controls.Add(deviceLabel); Controls.Add(description); Controls.Add(title);
                Height = fixedHeight + rows.Height;
            }
            Label TextLabel(string text, int width)
            {
                var label = new Label { Text = text, Font = Font, Dock = DockStyle.Top, ForeColor = ForeColor,
                    BackColor = BackColor, UseMnemonic = false, Padding = new Padding(0, 4, 0, 4), Width = width };
                label.Height = label.GetPreferredSize(new Size(width, 0)).Height;
                return label;
            }
            protected override void OnPaint(PaintEventArgs e)
            { base.OnPaint(e); using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
            protected override void Dispose(bool disposing)
            {
                if (disposing && picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
                base.Dispose(disposing);
            }
        }
    }
}
