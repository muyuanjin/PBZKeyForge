using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal static class EditorTheme
    {
        public static readonly Color Background = Color.FromArgb(16, 19, 21);
        public static readonly Color Surface = Color.FromArgb(25, 29, 32);
        public static readonly Color Floating = Color.FromArgb(34, 39, 43);
        public static readonly Color RowAlternate = Color.FromArgb(28, 32, 35);
        public static readonly Color Hover = Color.FromArgb(43, 49, 53);
        public static readonly Color Ink = Color.FromArgb(238, 234, 225);
        public static readonly Color Muted = Color.FromArgb(173, 178, 177);
        public static readonly Color Line = Color.FromArgb(52, 58, 61);
        public static readonly Color Accent = Color.FromArgb(173, 54, 59);
        public static readonly Color AccentHover = Color.FromArgb(196, 74, 78);
        public static readonly Color KeyEnhancement = Color.FromArgb(193, 152, 255);
        public static readonly Color KeyEnhancementStrong = Color.FromArgb(120, 65, 190);
        public static readonly Color Amber = Accent;
        public static readonly Color Preview = Color.FromArgb(49, 43, 28);
        public static readonly Color Green = Color.FromArgb(172, 189, 167);
        public static readonly Color LiveConnected = Color.FromArgb(116, 224, 165);
        public static readonly Color Disabled = Color.FromArgb(45, 50, 53);
    }

    public sealed class EditorTab : EditorButton
    {
        bool selected, dirty;
        string badge;
        public bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        public bool Dirty { get { return dirty; } set { if (dirty != value) { dirty = value; Invalidate(); } } }
        public string Badge { get { return badge; } set { if (badge != value) { badge = value; Invalidate(); } } }
        public EditorTab()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = EditorTheme.Surface;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            EditorArtwork.ClearSurface(this, e.Graphics, Selected ? EditorTheme.Floating : Hovered ? EditorTheme.RowAlternate : BackColor);
            float scale = e.Graphics.DpiX / 96f;
            int inset = (int)Math.Round(12 * scale);
            int underline = Math.Max(1, (int)Math.Round(3 * scale));
            var bounds = new Rectangle(inset, 0, Math.Max(0, Width - inset * 2), Height - underline);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            string text = Text;
            int badgeGap = (int)Math.Round(8 * scale);
            int badgeWidth = string.IsNullOrEmpty(Badge) ? 0 : TextRenderer.MeasureText(Badge, Font).Width + (int)Math.Round(10 * scale);
            int dirtyWidth = Dirty ? (int)(18 * scale) : 0;
            int textWidth = Math.Min(TextRenderer.MeasureText(text, Font).Width, Math.Max(20, bounds.Width - badgeWidth - badgeGap - dirtyWidth));
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(bounds.X, bounds.Y, textWidth, bounds.Height), Selected ? EditorTheme.Ink : EditorTheme.Muted, flags);
            if (Dirty)
                using (var brush = new SolidBrush(BindingStatus.Pending))
                    e.Graphics.FillEllipse(brush, bounds.X + textWidth + 5 * scale, Height / 2f - 3 * scale, 6 * scale, 6 * scale);
            if (badgeWidth > 0)
            {
                int badgeHeight = (int)Math.Round(24 * scale);
                var badge = new Rectangle(bounds.X + textWidth + dirtyWidth + badgeGap, (Height - badgeHeight) / 2, badgeWidth, badgeHeight);
                using (var brush = new SolidBrush(Color.FromArgb(43, 53, 46))) e.Graphics.FillRectangle(brush, badge);
                TextRenderer.DrawText(e.Graphics, Badge, Font, badge, EditorTheme.Green, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            using (var pen = new Pen(Selected ? AccentColor.IsEmpty ? EditorTheme.Amber : AccentColor : EditorTheme.Line, Selected ? underline : 1))
                e.Graphics.DrawLine(pen, 0, Height - (underline + 1) / 2 - 1, Width, Height - (underline + 1) / 2 - 1);
            int focusInset = (int)Math.Round(4 * scale);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(focusInset, focusInset, Width - focusInset * 2, Height - focusInset * 3));
        }
    }

    public sealed class PresetModeSelector : TableLayoutPanel
    {
        sealed class ModeOption : EditorButton
        {
            public bool Selected;
            protected override void OnPaint(PaintEventArgs e)
            {
                Color fill = Hovered ? EditorTheme.Hover : Selected ? EditorTheme.Floating : EditorTheme.Surface;
                EditorArtwork.ClearSurface(this, e.Graphics, fill);
                using (var pen = new Pen(EditorTheme.Line))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                if (Selected && Enabled)
                    using (var pen = new Pen(EditorTheme.Accent, Math.Max(1, 2 * e.Graphics.DpiY / 96f)))
                        e.Graphics.DrawLine(pen, 1, Height - 2, Width - 2, Height - 2);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled && Selected ? EditorTheme.Ink : EditorTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 7, Height - 7));
            }
        }
        readonly ModeOption[] options = { new ModeOption(), new ModeOption(), new ModeOption() };
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };
        static readonly string[] keys = { "preset.append", "preset.replace", "preset.only" };
        PresetApplyMode mode;
        public event EventHandler ModeChanged;
        public PresetApplyMode Mode
        {
            get { return mode; }
            set { mode = value; for (int i = 0; i < options.Length; i++) { options[i].Selected = i == (int)mode; options[i].Invalidate(); } }
        }
        public PresetModeSelector()
        {
            DoubleBuffered = true; BackColor = EditorTheme.Surface;
            ColumnCount = 3; RowCount = 1; Margin = Padding.Empty;
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                options[i].Dock = DockStyle.Fill; options[i].Margin = Padding.Empty;
                options[i].Name = "mode" + ((PresetApplyMode)i).ToString();
                options[i].Click += delegate {
                    if (mode == (PresetApplyMode)index) return;
                    Mode = (PresetApplyMode)index;
                    if (ModeChanged != null) ModeChanged(this, EventArgs.Empty);
                };
                Controls.Add(options[i], i, 0);
            }
            RefreshLabels(); Mode = PresetApplyMode.OnlyPreset;
        }
        public void RefreshLabels()
        {
            AccessibleName = I18n.Str("preset.mode");
            for (int i = 0; i < options.Length; i++)
            {
                options[i].Text = I18n.Str(keys[i]); options[i].AccessibleName = AccessibleName + ": " + options[i].Text;
                tips.SetToolTip(options[i], I18n.Str(i == 2 ? "preset.only.scopehelp" : keys[i] + ".help"));
            }
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = options.Max(option => TextRenderer.MeasureText(option.Text, Font).Width) + Font.Height * 2;
            return new Size(width * 3, Font.Height + Math.Max(3, Font.Height / 5));
        }
        protected override void Dispose(bool disposing)
        { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }

    public sealed class BindingButton : EditorButton
    {
        string value;
        bool changed, mixed;
        public string[] DefaultKeys;
        public bool UsesDefault;
        public bool ActionLevelDefault;
        public bool Unavailable;
        public bool IsOffline;
        public bool FactoryMatch { get { return UsesDefault || !Mixed && DefaultKeys != null && DefaultKeys.Contains(Value); } }
        public bool EmptyBinding { get { return !Mixed && (UsesDefault ? DefaultKeys != null && !DefaultKeys.Any(k => !string.IsNullOrEmpty(k) && k != "None") : Value == "None"); } }
        public Color StatusColor { get { return Changed ? BindingStatus.Pending : FactoryMatch ? BindingStatus.Factory : BindingStatus.Custom; } }
        public string StatusText { get { return I18n.Str(Changed ? IsOffline ? "offline.pending" : "views.pending" : UsesDefault && DefaultKeys == null ? "editor.defaultunknown" : FactoryMatch ? "views.original" : "views.savedcustom"); } }
        public string DisplayText { get { return Unavailable ? "—" : Mixed ? I18n.Str("editor.mixed") : UsesDefault ? DefaultValue(DefaultKeys) : FriendlyKey(Value); } }
        public string Value { get { return value; } set { if (this.value != value) { this.value = value; Invalidate(); } } }
        public bool Changed { get { return changed; } set { if (changed != value) { changed = value; Invalidate(); } } }
        public bool Mixed { get { return mixed; } set { if (mixed != value) { mixed = value; Invalidate(); } } }
        public BindingButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Margin = new Padding(12, 5, 12, 5);
            Dock = DockStyle.Fill;
            AccessibleRole = AccessibleRole.PushButton;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            EditorArtwork.ClearSurface(this, e.Graphics, BackColor);
            float scale = e.Graphics.DpiX / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool quiet = Unavailable || EmptyBinding && FactoryMatch && !Changed;
            bool emphasize = Hovered && Enabled || Focused;
            Color fill = Changed ? EditorTheme.Preview : emphasize ? EditorTheme.Hover : quiet ? BackColor : EditorTheme.Floating;
            using (var shape = EditorArtwork.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4 * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Changed ? BindingStatus.Pending : emphasize ? EditorTheme.Muted : EditorTheme.Line))
            { e.Graphics.FillPath(brush, shape); if (!quiet || emphasize) e.Graphics.DrawPath(pen, shape); }
            string text = EmptyBinding || Unavailable ? "—" : DisplayText;
            Color ink = Unavailable || !Enabled ? EditorTheme.Muted : quiet ? BindingStatus.Factory : FactoryMatch && !Changed ? EditorTheme.Muted : StatusColor;
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle((int)(14 * scale), 0, Width - (int)(28 * scale), Height), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (!Unavailable && (!quiet || emphasize))
                BindingStatus.Dot(e.Graphics, StatusColor, 8 * scale, Height / 2f - 2.5f * scale, 5 * scale);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 7, Height - 7));
        }

        public static string FriendlyKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return I18n.Str("editor.default");
            switch (key)
            {
                case "None": return I18n.Str("editor.unbound");
                case "LeftMouseButton": return I18n.Str("key.mouseleft");
                case "RightMouseButton": return I18n.Str("key.mouseright");
                case "MiddleMouseButton": return I18n.Str("key.mousemiddle");
                case "ThumbMouseButton": return I18n.Str("key.mouse4");
                case "ThumbMouseButton2": return I18n.Str("key.mouse5");
                case "LeftShift": return "Left Shift";
                case "RightShift": return "Right Shift";
                case "LeftControl": return "Left Ctrl";
                case "RightControl": return "Right Ctrl";
                case "LeftAlt": return "Left Alt";
                case "RightAlt": return "Right Alt";
                case "SpaceBar": return I18n.Str("key.space");
                case "Zero": return "0"; case "One": return "1"; case "Two": return "2";
                case "Three": return "3"; case "Four": return "4"; case "Five": return "5";
                case "Six": return "6"; case "Seven": return "7"; case "Eight": return "8"; case "Nine": return "9";
                case "Gamepad_FaceButton_Bottom": return "A / ×";
                case "Gamepad_FaceButton_Right": return "B / ○";
                case "Gamepad_FaceButton_Left": return "X / □";
                case "Gamepad_FaceButton_Top": return "Y / △";
                case "Gamepad_LeftShoulder": return "LB / L1";
                case "Gamepad_RightShoulder": return "RB / R1";
                case "Gamepad_LeftTrigger": case "Gamepad_LeftTriggerAxis": return "LT / L2";
                case "Gamepad_RightTrigger": case "Gamepad_RightTriggerAxis": return "RT / R2";
                case "Gamepad_LeftThumbstick": return "LS / L3";
                case "Gamepad_RightThumbstick": return "RS / R3";
                case "Gamepad_LeftStick_Up": return "LS ↑";
                case "Gamepad_LeftStick_Down": return "LS ↓";
                case "Gamepad_LeftStick_Left": return "LS ←";
                case "Gamepad_LeftStick_Right": return "LS →";
                case "Gamepad_RightStick_Up": return "RS ↑";
                case "Gamepad_RightStick_Down": return "RS ↓";
                case "Gamepad_RightStick_Left": return "RS ←";
                case "Gamepad_RightStick_Right": return "RS →";
                case "Gamepad_Special_Left": return "View / Share";
                case "Gamepad_Special_Right": return "Menu / Options";
                case "Gamepad_DPad_Up": return "D-pad ↑";
                case "Gamepad_DPad_Down": return "D-pad ↓";
                case "Gamepad_DPad_Left": return "D-pad ←";
                case "Gamepad_DPad_Right": return "D-pad →";
                default: return key;
            }
        }
        public static string DefaultLabel(string[] keys)
        {
            return I18n.Str("editor.default") + " | " + DefaultValue(keys);
        }
        public static string DefaultValue(string[] keys)
        {
            string text = keys == null ? I18n.Str("editor.defaultunknown") : string.Join(" / ", keys.Where(key => !string.IsNullOrEmpty(key) && key != "None").Select(FriendlyKey).Distinct());
            return text.Length == 0 ? I18n.Str("editor.unbound") : text;
        }
    }

    internal sealed class BindingTable : TableLayoutPanel
    {
        public BindingTable() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { base.OnPaintBackground(e); if (BackColor.A == 255) EditorArtwork.PaintEffect(this, e.Graphics); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // WinForms' Single cell border uses a dark system colour. Redraw only
            // those one-pixel lines in the same quiet separator colour as the shell.
            using (var pen = new Pen(EditorTheme.Line))
            {
                int y = 0;
                e.Graphics.DrawLine(pen, 0, y, Width, y);
                foreach (int height in GetRowHeights()) { y += height; e.Graphics.DrawLine(pen, 0, y, Width, y); }
            }
        }
    }

    internal sealed class KeyCaptureBox : Control
    {
        public string CapturePrompt;
        public event Action<string> ModifierCaptured;
        public event Action<string> MouseCaptured;
        public event Action<string> KeyReleased;
        bool armed;
        public KeyCaptureBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) != Keys.Tab; }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            bool wasArmed = armed && Focused;
            Focus(); base.OnMouseDown(e);
            if (!wasArmed || MouseCaptured == null) return;
            string key = MouseKey(e.Button);
            if (key != null) MouseCaptured(key);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            string key = MouseKey(e.Button);
            if (Focused && armed && key != null && KeyReleased != null) KeyReleased(key);
        }
        static string MouseKey(MouseButtons button)
        {
            return button == MouseButtons.Left ? "LeftMouseButton" : button == MouseButtons.Right ? "RightMouseButton"
                : button == MouseButtons.Middle ? "MiddleMouseButton" : button == MouseButtons.XButton1 ? "ThumbMouseButton"
                : button == MouseButtons.XButton2 ? "ThumbMouseButton2" : null;
        }
        protected override void OnGotFocus(EventArgs e) { armed = true; base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { armed = false; base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            EditorArtwork.ClearSurface(this, e.Graphics, BackColor);
            using (var pen = new Pen(Focused ? EditorTheme.Accent : EditorTheme.Line))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, armed ? CapturePrompt ?? I18n.Str("editor.capturing") : Text, Font, Rectangle.Inflate(ClientRectangle, -8, -2), Enabled ? armed ? EditorTheme.Ink : ForeColor : EditorTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
        protected override void WndProc(ref Message message)
        {
            if (Focused && armed && (message.Msg == 0x101 || message.Msg == 0x105) && KeyReleased != null)
            {
                int code = message.WParam.ToInt32(); long data = message.LParam.ToInt64();
                string released = code == 0x10 ? (((data >> 16) & 0xff) == 0x36 ? "RightShift" : "LeftShift")
                    : code == 0x11 ? ((data & 0x01000000) != 0 ? "RightControl" : "LeftControl")
                    : code == 0x12 ? ((data & 0x01000000) != 0 ? "RightAlt" : "LeftAlt") : BindingDialog.KeyFromCode((Keys)code);
                KeyReleased(released);
            }
            if (Focused && armed && (message.Msg == 0x100 || message.Msg == 0x104))
            {
                int virtualKey = message.WParam.ToInt32();
                long data = message.LParam.ToInt64();
                int scanCode = (int)((data >> 16) & 0xff);
                bool extended = (data & 0x01000000) != 0;
                string key = virtualKey == 0x10 ? (scanCode == 0x36 ? "RightShift" : "LeftShift")
                    : virtualKey == 0x11 ? (extended ? "RightControl" : "LeftControl")
                    : virtualKey == 0x12 ? (extended ? "RightAlt" : "LeftAlt") : null;
                if (key != null && ModifierCaptured != null)
                {
                    ModifierCaptured(key);
                    return;
                }
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class BindingDialog : EditorWindow
    {
        readonly ComboBox keys = new EditorComboBox { CenterText = true };
        readonly GamepadCapture gamepadCapture = new GamepadCapture();
        public string Result;
        sealed class Choice
        {
            public string Key;
            public string[] DefaultKeys;
            public override string ToString() { return Key == "" ? BindingButton.DefaultLabel(DefaultKeys) : BindingButton.FriendlyKey(Key); }
        }
        public BindingDialog(string action, bool gamepad, string value, string[] defaultKeys, bool isDefault)
        {
            Text = I18n.Str("editor.editbinding", action);
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(510, 262);
            AllowResize = false;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;
            BackColor = EditorTheme.Background;
            ForeColor = EditorTheme.Ink;
            var help = new Label { Left = 20, Top = 18, Width = 430, Height = 45, Text = I18n.Str(gamepad ? "editor.padinput" : "editor.keyinput") };
            var question = new BindingHelpButton("help.defaults") { Left = 460, Top = 18, Width = 30, Height = 32 };
            keys.SetBounds(20, 72, 470, 30);
            keys.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string key in gamepad ? KeyNames.Gamepad : KeyNames.Keyboard) keys.Items.Add(new Choice { Key = key, DefaultKeys = defaultKeys });
            keys.Text = isDefault ? BindingButton.DefaultLabel(defaultKeys) : BindingButton.FriendlyKey(value);
            for (int i = 0; i < keys.Items.Count; i++) if (((Choice)keys.Items[i]).Key == (isDefault ? "" : value ?? "")) keys.SelectedIndex = i;
            var capture = new KeyCaptureBox { Left = 20, Top = 120, Width = 470, Height = 34, Text = I18n.Str("editor.capture"), BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Muted };
            capture.ModifierCaptured += SelectKey;
            capture.MouseCaptured += SelectKey;
            capture.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs e) { e.IsInputKey = e.KeyCode != Keys.Tab; };
            capture.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Tab)
                { SelectNextControl(capture, !e.Shift, true, true, true); e.SuppressKeyPress = true; return; }
                string key = KeyFromCode(e.KeyCode);
                if (key != null) SelectKey(key);
                e.SuppressKeyPress = true;
            };
            capture.Text = I18n.Str("flow.record"); capture.CapturePrompt = I18n.Str("flow.record.active");
            gamepadCapture.Pressed += SelectKey;
            capture.GotFocus += delegate { if (gamepad) gamepadCapture.Start(Handle); };
            capture.LostFocus += delegate { gamepadCapture.Stop(); };
            var clear = new EditorButton { Left = 20, Top = 176, Width = 165, Height = 33, Text = I18n.Str("editor.clear") };
            clear.Click += delegate { Result = null; DialogResult = DialogResult.OK; };
            var ok = new EditorButton { Left = 290, Top = 176, Width = 96, Height = 33, Text = I18n.Str("editor.ok"), Primary = true };
            ok.Click += delegate
            {
                var choice = keys.SelectedItem as Choice;
                Result = choice != null && keys.Text == choice.ToString() ? choice.Key : keys.Text.Trim();
                if (Result == I18n.Str("editor.default")) Result = null;
                else if (Result == I18n.Str("editor.unbound")) Result = "None";
                DialogResult = DialogResult.OK;
            };
            var cancel = new EditorButton { Left = 394, Top = 176, Width = 96, Height = 33, Text = I18n.Str("editor.cancel"), DialogResult = DialogResult.Cancel };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { help, question, keys, capture, clear, ok, cancel });
            foreach (Control control in Controls) if (control != Caption) control.Top += Caption.Height;
            AutoScaleDimensions = new SizeF(96f, 96f);
            PerformAutoScale();
            EditorArtwork.DarkCaption(this);
        }
        void SelectKey(string key)
        {
            for (int i = 0; i < keys.Items.Count; i++) if (((Choice)keys.Items[i]).Key == key) { keys.SelectedIndex = i; return; }
            keys.Text = key;
        }
        protected override void OnShown(EventArgs e)
        { base.OnShown(e); keys.Focus(); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0xff) gamepadCapture.ReadRawInput(message.LParam);
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) gamepadCapture.Dispose(); base.Dispose(disposing); }
        internal static string KeyFromCode(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z || key >= Keys.F1 && key <= Keys.F12) return key.ToString();
            string[] digits = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
            if (key >= Keys.D0 && key <= Keys.D9) return digits[(int)key - (int)Keys.D0];
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "NumPad" + digits[(int)key - (int)Keys.NumPad0];
            switch (key)
            {
                case Keys.Space: return "SpaceBar"; case Keys.ShiftKey: case Keys.LShiftKey: return "LeftShift";
                case Keys.RShiftKey: return "RightShift"; case Keys.ControlKey: case Keys.LControlKey: return "LeftControl";
                case Keys.RControlKey: return "RightControl"; case Keys.Menu: case Keys.LMenu: return "LeftAlt";
                case Keys.RMenu: return "RightAlt"; case Keys.Back: return "BackSpace";
                case Keys.OemOpenBrackets: return "LeftBracket"; case Keys.OemCloseBrackets: return "RightBracket";
                case Keys.OemSemicolon: return "Semicolon"; case Keys.OemQuotes: return "Quote";
                case Keys.Oemcomma: return "Comma"; case Keys.OemPeriod: return "Period";
                case Keys.OemQuestion: return "Slash"; case Keys.OemPipe: return "Backslash";
                case Keys.Oemtilde: return "Tilde"; case Keys.Capital: return "CapsLock";
                case Keys.Prior: return "PageUp"; case Keys.Next: return "PageDown";
                default: return key.ToString();
            }
        }
    }
}
