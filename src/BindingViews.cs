using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal static class BindingViewText
    {
        public static string PhysicalKey(string key)
        {
            return key == "Gamepad_LeftTriggerAxis" ? "Gamepad_LeftTrigger"
                : key == "Gamepad_RightTriggerAxis" ? "Gamepad_RightTrigger" : key;
        }
        public static string[] InputKeys(string physicalKey)
        {
            return physicalKey == "Gamepad_LeftTrigger" || physicalKey == "Gamepad_RightTrigger"
                ? new[] { physicalKey, physicalKey + "Axis" } : new[] { physicalKey };
        }
        public static ActionRelations PhysicalRelations(ActionRelations source)
        {
            return new ActionRelations { Action = source.Action, Complete = source.Complete, Changed = source.Changed,
                Keys = source.Keys.Select(PhysicalKey).Distinct().ToArray(),
                DefaultKeys = source.DefaultKeys == null ? null : source.DefaultKeys.Select(PhysicalKey).Distinct().ToArray() };
        }
        public static string ActionName(string action)
        {
            return BindingWorkspace.FunctionLabel(action);
        }
        public static string Keys(IEnumerable<string> keys)
        {
            string text = string.Join(" / ", keys.Select(BindingButton.FriendlyKey));
            return text.Length == 0 ? I18n.Str("editor.unbound") : text;
        }
        public static Label Label(string text, int height)
        { return new EditorLabel { Text = text, Dock = DockStyle.Top, Height = height, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 6, 0), AutoEllipsis = true, ForeColor = EditorTheme.Ink }; }
    }

    internal sealed class ActionNameLabel : Label
    {
        public string ActionId;
        public string KeySummary;
        protected override void OnPaint(PaintEventArgs e)
        {
            EditorArtwork.ClearSurface(this, e.Graphics, BackColor);
            float scale = e.Graphics.DpiX / 96f;
            int inset = (int)(12 * scale), size = (int)(22 * scale), left = inset;
            Image icon = EditorArtwork.ActionIcon(ActionId);
            if (icon != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(icon, new Rectangle(inset, (Height - size) / 2, size, size));
                left += size + (int)(10 * scale);
            }
            int titleHeight = string.IsNullOrEmpty(KeySummary) ? Height : Height * 3 / 5;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Math.Max(0, Width - left - inset), titleHeight), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(KeySummary))
                using (var small = new Font(Font.FontFamily, 8f))
                    TextRenderer.DrawText(e.Graphics, KeySummary, small, new Rectangle(left, titleHeight, Math.Max(0, Width - left - inset), Height - titleHeight),
                        EditorTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            if (BackColor == EditorTheme.Preview)
                using (var brush = new SolidBrush(BindingStatus.Pending)) e.Graphics.FillRectangle(brush, 0, (int)(5 * scale), Math.Max(2, (int)(3 * scale)), Height - (int)(10 * scale));
        }
    }

    internal static class BindingStatus
    {
        public static readonly Color Factory = Color.FromArgb(128, 143, 153);
        public static readonly Color Custom = Color.FromArgb(64, 158, 255);
        public static readonly Color Pending = Color.FromArgb(240, 178, 78);
        public static void Dot(Graphics graphics, Color color, float x, float y, float size)
        { using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, x, y, size, size); }
    }

    internal sealed class KeyBadgeState
    {
        public int Count;
        public bool Complete, Differs, Pending, IsOffline;
        public Color Color { get { return Pending ? BindingStatus.Pending : Differs ? BindingStatus.Custom : BindingStatus.Factory; } }
        public bool Visible { get { return Count > 0 || Differs || Pending; } }
        public string Number { get { return Complete ? Count.ToString() : Count > 0 ? Count + "+" : "?"; } }
        public string Status { get { return I18n.Str(Pending ? IsOffline ? "offline.pending" : "views.pending" : Differs ? "views.savedcustom" : Complete ? "views.original" : "editor.defaultunknown"); } }
    }

    internal sealed class DeviceDiagram : ScrollableControl
    {
        readonly Dictionary<string, RectangleF> targets = new Dictionary<string, RectangleF>();
        KeyValuePair<string, RectangleF>[] hitTargets = new KeyValuePair<string, RectangleF>[0];
        readonly Dictionary<string, RectangleF> badges = new Dictionary<string, RectangleF>();
        readonly Dictionary<string, KeyBadgeState> states = new Dictionary<string, KeyBadgeState>();
        readonly Dictionary<string, string> devices = new Dictionary<string, string>();
        Func<string, KeyBadgeState> stateForKey;
        readonly Action<string> select;
        string selectedKey, controller;
        bool pad, preview;
        readonly ToolTip tip = new ToolTip();
        List<string> extraKeys = new List<string>();
        readonly Dictionary<string, RectangleF> imageBounds = new Dictionary<string, RectangleF>();
        Bitmap deviceArtwork;
        string hoveredKey;
        float scale = 1f;
        public DeviceDiagram(bool pad, string family, Func<string, KeyBadgeState> stateForKey, Action<string> select,
            string selectedKey, IEnumerable<string> knownKeys)
        {
            this.select = select;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Dock = DockStyle.Fill; BackColor = EditorTheme.Surface; TabStop = true; AutoScroll = true;
            HandleCreated += delegate { EditorArtwork.DarkScrollbars(this); };
            UpdateBindings(pad, family, stateForKey, selectedKey, knownKeys);
        }
        public void UpdateBindings(bool pad, string family, Func<string, KeyBadgeState> stateForKey,
            string selectedKey, IEnumerable<string> knownKeys)
        {
            bool sony = family != null && (family.IndexOf("sony", StringComparison.OrdinalIgnoreCase) >= 0 || family.IndexOf("playstation", StringComparison.OrdinalIgnoreCase) >= 0
                || family.IndexOf("dual", StringComparison.OrdinalIgnoreCase) >= 0 || family.Equals("PS", StringComparison.OrdinalIgnoreCase) || family.Equals("PS4", StringComparison.OrdinalIgnoreCase) || family.Equals("PS5", StringComparison.OrdinalIgnoreCase));
            string nextController = sony ? "playstation" : "xbox";
            var physical = pad ? DeviceHotspots.ForDevice(nextController).Keys : DeviceHotspots.ForDevice("keyboard").Keys.Concat(DeviceHotspots.ForDevice("mouse").Keys);
            var extras = knownKeys.Except(physical).Where(k => k != "None" && !string.IsNullOrEmpty(k)).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            bool layout = this.pad != pad || controller != nextController || !extraKeys.SequenceEqual(extras);
            this.pad = pad; this.stateForKey = stateForKey; this.selectedKey = selectedKey;
            controller = sony ? "playstation" : "xbox";
            extraKeys = extras;
            if (layout) { hoveredKey = null; tip.SetToolTip(this, ""); PerformLayout(); }
            else foreach (string key in targets.Keys) { states[key] = stateForKey(key); AddBadge(key); }
            AccessibleName = I18n.Str("views.clickkey");
            UpdateToolTip();
            Invalidate();
        }
        public void SelectKey(string key)
        { selectedKey = key; Invalidate(); }
        static Image Artwork(string device)
        { return device == "keyboard" ? EditorArtwork.Keyboard : device == "mouse" ? EditorArtwork.Mouse : device == "xbox" ? EditorArtwork.Xbox : EditorArtwork.PlayStation; }
        static RectangleF Fit(Image image, RectangleF area)
        {
            float ratio = Math.Min(area.Width / image.Width, area.Height / image.Height);
            return new RectangleF(area.X + (area.Width - image.Width * ratio) / 2, area.Y + (area.Height - image.Height * ratio) / 2, image.Width * ratio, image.Height * ratio);
        }
        Dictionary<string, RectangleF> ExtraTargets(float width, float inset)
        {
            float left = inset, top = 0, rowHeight = 36 * scale;
            var extras = new Dictionary<string, RectangleF>();
            foreach (string key in extraKeys)
            {
                float keyWidth = Math.Min(width - 2 * inset, Math.Max(48 * scale, TextRenderer.MeasureText(BindingButton.FriendlyKey(key), Font).Width + 20 * scale));
                if (left > inset && left + keyWidth > width - inset) { left = inset; top += rowHeight; }
                extras[key] = new RectangleF(left, top, keyWidth, 27 * scale); left += keyWidth + 8 * scale;
            }
            return extras;
        }
        public Bitmap CapturePreview(int width)
        {
            preview = true;
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            float inset = 10 * scale;
            var extras = ExtraTargets(width, inset);
            float extraHeight = extras.Count == 0 ? 0 : extras.Values.Max(box => box.Top) + 36 * scale;
            Image artwork = Artwork(pad ? controller : "keyboard");
            float artworkWidth = pad ? width - 2 * inset : (width - 3 * inset) * .79f;
            float artworkHeight = artworkWidth * artwork.Height / artwork.Width;
            Size = new Size(width, (int)Math.Ceiling(artworkHeight + 2 * inset + extraHeight));
            AutoScrollPosition = Point.Empty; PerformLayout();
            var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, ClientRectangle); return bitmap;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (deviceArtwork != null) { deviceArtwork.Dispose(); deviceArtwork = null; }
            if (stateForKey == null) { base.OnLayout(e); return; }
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            targets.Clear(); badges.Clear(); states.Clear(); devices.Clear(); imageBounds.Clear();
            float inset = 10 * scale;
            if (ClientSize.Width <= 3 * inset || ClientSize.Height <= 2 * inset)
            {
                hitTargets = new KeyValuePair<string, RectangleF>[0]; AutoScrollMinSize = Size.Empty;
                base.OnLayout(e); return;
            }
            float canvasWidth = preview ? ClientSize.Width : Math.Max(1, Width - SystemInformation.VerticalScrollBarWidth);
            var extras = ExtraTargets(canvasWidth, inset);
            float extraHeight = extras.Count == 0 ? 0 : extras.Values.Max(box => box.Top) + 36 * scale;
            float minimumHeight = pad && !preview ? 250 * scale : 1;
            float availableHeight = Math.Max(minimumHeight, ClientSize.Height - 2 * inset - (preview ? extraHeight : 36 * scale));
            AutoScrollMinSize = new Size(0, (int)Math.Ceiling(availableHeight + 2 * inset + (preview ? extraHeight : Math.Max(36 * scale, extraHeight))));
            // Update native scroll ranges before placing hotspots at the current offset.
            base.OnLayout(e);
            float scrollY = AutoScrollPosition.Y;
            if (pad) AddDevice(controller, new RectangleF(inset, inset + scrollY, canvasWidth - 2 * inset, availableHeight));
            else
            {
                float keyboardWidth = (canvasWidth - 3 * inset) * .79f;
                AddDevice("keyboard", new RectangleF(inset, inset + scrollY, keyboardWidth, availableHeight));
                AddDevice("mouse", new RectangleF(keyboardWidth + 2 * inset, inset + scrollY, canvasWidth - keyboardWidth - 3 * inset, availableHeight));
            }
            foreach (var item in extras)
            {
                string key = item.Key; RectangleF box = item.Value; box.Offset(0, availableHeight + 2 * inset + scrollY);
                targets[key] = box; devices[key] = "extra";
                states[key] = stateForKey(key); AddBadge(key);
            }
            hitTargets = targets.OrderBy(t => t.Value.Width * t.Value.Height).ToArray();
        }
        protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); PerformLayout(); Invalidate(); }
        void AddDevice(string device, RectangleF area)
        {
            RectangleF image = Fit(Artwork(device), area); imageBounds[device] = image;
            foreach (var item in DeviceHotspots.ForDevice(device))
            {
                string key = item.Key;
                RectangleF box = item.Value;
                targets[key] = new RectangleF(image.Left + box.X * image.Width, image.Top + box.Y * image.Height,
                    box.Width * image.Width, box.Height * image.Height);
                devices[key] = device; states[key] = stateForKey(key); AddBadge(key);
            }
        }
        void AddBadge(string key)
        {
            RectangleF box = targets[key];
            float size = (devices[key] == "keyboard" ? 13 : 17) * scale;
            RectangleF image;
            if (imageBounds.TryGetValue(devices[key], out image))
                size = Math.Min(size, devices[key] == "keyboard" ? image.Height * .06f : image.Width * (devices[key] == "mouse" ? .13f : .035f));
            float width;
            using (var font = BadgeFont(key, size))
                width = Math.Max(size, TextRenderer.MeasureText(states[key].Number, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + size * .25f);
            float x = box.Right - width * .65f, y = box.Top - size * .35f;
            if (pad && devices[key] != "extra")
            {
                switch (key)
                {
                    case "Gamepad_FaceButton_Top": case "Gamepad_DPad_Up":
                        x = box.Left + (box.Width - width) / 2; y = box.Top - size * .65f; break;
                    case "Gamepad_FaceButton_Bottom": case "Gamepad_DPad_Down":
                        x = box.Left + (box.Width - width) / 2; y = box.Bottom - size * .35f; break;
                    case "Gamepad_FaceButton_Left": case "Gamepad_DPad_Left":
                        x = box.Left - width * .65f; y = box.Top + (box.Height - size) / 2; break;
                    case "Gamepad_FaceButton_Right": case "Gamepad_DPad_Right":
                        x = box.Right - width * .35f; y = box.Top + (box.Height - size) / 2; break;
                }
            }
            badges[key] = new RectangleF(x, y, width, size);
        }
        Font BadgeFont(string key, float height)
        { return new Font("Segoe UI", 8f * height / ((devices[key] == "keyboard" ? 13 : 17) * scale), FontStyle.Bold); }
        internal PointF Position(string key)
        { var box = targets[key]; return new PointF(box.Left + box.Width / 2, box.Top + box.Height / 2); }
        string Hit(Point location)
        {
            foreach (var target in hitTargets)
                if (target.Value.Contains(location))
                {
                    if (!RoundButton(target.Key, devices[target.Key])) return target.Key;
                    float x = (location.X - target.Value.X) / target.Value.Width * 2 - 1;
                    float y = (location.Y - target.Value.Y) / target.Value.Height * 2 - 1;
                    if (x * x + y * y <= 1) return target.Key;
                }
            foreach (var badge in badges)
                if (states[badge.Key].Visible && badge.Value.Contains(location)) return badge.Key;
            return null;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); string key = Hit(e.Location); Cursor = key == null ? Cursors.Default : Cursors.Hand;
            if (key == hoveredKey) return; hoveredKey = key;
            UpdateToolTip();
            Invalidate();
        }
        void UpdateToolTip()
        {
            string key = hoveredKey;
            string text = key == null ? "" : BindingButton.FriendlyKey(key) + " · " + I18n.Str("views.count", states[key].Complete ? states[key].Count.ToString() : "≥ " + states[key].Count)
                + " · " + states[key].Status + (states[key].Complete ? "" : "\n" + I18n.Str("views.partial"));
            if (tip.GetToolTip(this) != text) tip.SetToolTip(this, text);
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoveredKey = null; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        { base.OnMouseClick(e); if (e.Button == MouseButtons.Left) { string key = Hit(e.Location); if (key != null) { Focus(); select(key); } } }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) == Keys.Left || (keyData & Keys.KeyCode) == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); var keys = targets.Keys.ToArray(); if (keys.Length == 0) return;
            int index = Array.IndexOf(keys, selectedKey);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            { select(keys[(index + (e.KeyCode == Keys.Left ? keys.Length - 1 : 1)) % keys.Length]); e.Handled = true; }
        }
        static string KeyCaption(string key, string device)
        {
            string[] digits = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
            int digit = Array.IndexOf(digits, key); if (digit >= 0) return digit.ToString();
            switch (key)
            {
                case "LeftMouseButton": return I18n.Str("key.mouseleft"); case "RightMouseButton": return I18n.Str("key.mouseright");
                case "MiddleMouseButton": return ""; case "ThumbMouseButton": return "4"; case "ThumbMouseButton2": return "5";
                case "Escape": return "Esc"; case "BackSpace": return "⌫"; case "SpaceBar": return "Space";
                case "LeftShift": case "RightShift": return "Shift"; case "LeftControl": case "RightControl": return "Ctrl";
                case "LeftAlt": case "RightAlt": return "Alt"; case "LeftCommand": case "RightCommand": return "Win"; case "Menu": return "☰";
                case "CapsLock": return "Caps"; case "Enter": return "↵"; case "Up": return "↑"; case "Down": return "↓"; case "Left": return "←"; case "Right": return "→";
                case "PageUp": return "Pg↑"; case "PageDown": return "Pg↓"; case "Home": return "Hm"; case "Insert": return "Ins"; case "Delete": return "Del";
                case "PrintScreen": return "Prt"; case "ScrollLock": return "Scr"; case "Pause": return "Pau"; case "Tilde": return "~";
                case "LeftBracket": return "["; case "RightBracket": return "]"; case "Backslash": return "\\";
                case "Semicolon": return ";"; case "Quote": return "'"; case "Comma": return ","; case "Period": return "."; case "Slash": return "/";
                case "Hyphen": return "−"; case "Equals": return "=";
                case "Gamepad_LeftTrigger": return device == "playstation" ? "L2" : "LT"; case "Gamepad_RightTrigger": return device == "playstation" ? "R2" : "RT";
                case "Gamepad_LeftShoulder": return device == "playstation" ? "L1" : "LB"; case "Gamepad_RightShoulder": return device == "playstation" ? "R1" : "RB";
                case "Gamepad_LeftThumbstick": return "L3"; case "Gamepad_RightThumbstick": return "R3";
                case "Gamepad_DPad_Up": return "↑"; case "Gamepad_DPad_Down": return "↓"; case "Gamepad_DPad_Left": return "←"; case "Gamepad_DPad_Right": return "→";
                case "Gamepad_Special_Left": return "≡"; case "Gamepad_Special_Right": return "☰";
                case "Gamepad_FaceButton_Bottom": return device == "playstation" ? "×" : "A"; case "Gamepad_FaceButton_Right": return device == "playstation" ? "○" : "B";
                case "Gamepad_FaceButton_Left": return device == "playstation" ? "□" : "X"; case "Gamepad_FaceButton_Top": return device == "playstation" ? "△" : "Y";
                default: return key;
            }
        }
        static bool RoundButton(string key, string device)
        {
            return (device == "xbox" || device == "playstation")
                && (key.StartsWith("Gamepad_FaceButton_", StringComparison.Ordinal)
                    || key.EndsWith("Thumbstick", StringComparison.Ordinal) || device == "xbox" && key.StartsWith("Gamepad_Special_", StringComparison.Ordinal));
        }
        GraphicsPath TargetOutline(string key, RectangleF box)
        {
            if (!RoundButton(key, devices[key])) return EditorArtwork.Rounded(box, 3 * scale);
            var path = new GraphicsPath(); path.AddEllipse(box); return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); EditorArtwork.PaintEffect(this, e.Graphics); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (deviceArtwork == null && ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                deviceArtwork = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(deviceArtwork))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    foreach (var item in imageBounds) graphics.DrawImage(Artwork(item.Key), item.Value);
                }
            }
            if (deviceArtwork != null) e.Graphics.DrawImageUnscaled(deviceArtwork, 0, 0);
            using (var keyFont = new Font("Segoe UI", KeyboardFontSize()))
            using (var mouseFont = new Font(Font.FontFamily, 8f))
            foreach (var item in targets)
            {
                string key = item.Key; RectangleF box = item.Value; KeyBadgeState state = states[key];
                bool active = key == selectedKey, hover = key == hoveredKey;
                if (active || hover || devices[key] == "extra")
                {
                    using (var path = TargetOutline(key, box))
                    using (var brush = new SolidBrush(Color.FromArgb(active ? 80 : 45, active ? state.Color : EditorTheme.Muted)))
                    using (var pen = new Pen(active ? EditorTheme.Ink : EditorTheme.Muted, active ? 1.5f * scale : scale))
                    { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
                }
                string caption = devices[key] == "extra" ? BindingButton.FriendlyKey(key) : KeyCaption(key, devices[key]);
                Font captionFont = devices[key] == "keyboard" ? keyFont : mouseFont;
                RectangleF captionBox = box;
                if (pad && devices[key] != "extra")
                {
                    Size text = TextRenderer.MeasureText(e.Graphics, caption, captionFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    captionBox.Inflate(Math.Max(0, text.Width - box.Width) / 2, Math.Max(0, text.Height - box.Height) / 2);
                }
                var captionBounds = Rectangle.Round(captionBox);
                const TextFormatFlags captionFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                Size captionSize = TextRenderer.MeasureText(e.Graphics, caption, captionFont, Size.Empty, captionFlags);
                float fontRatio = devices[key] == "keyboard" ? Math.Min(1, Math.Min(Math.Max(1, captionBounds.Width - 1f) / captionSize.Width, Math.Max(1, captionBounds.Height) / (float)captionSize.Height)) : 1;
                if (fontRatio < 1)
                {
                    using (var fitted = new Font(captionFont.FontFamily, captionFont.Size * fontRatio * .95f))
                        TextRenderer.DrawText(e.Graphics, caption, fitted, captionBounds, EditorTheme.Ink, captionFlags);
                }
                else TextRenderer.DrawText(e.Graphics, caption, captionFont, captionBounds, EditorTheme.Ink, captionFlags);
                if (!state.Visible) continue;
                RectangleF badge = badges[key];
                using (var path = EditorArtwork.Rounded(badge, badge.Height / 2))
                using (var brush = new SolidBrush(state.Color))
                using (var pen = new Pen(EditorTheme.Surface, scale))
                using (var numberFont = BadgeFont(key, badge.Height))
                {
                    e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path);
                    TextRenderer.DrawText(e.Graphics, state.Number, numberFont, Rectangle.Round(badge), EditorTheme.Background,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
            }
        }
        float KeyboardFontSize()
        {
            RectangleF keyboard;
            return imageBounds.TryGetValue("keyboard", out keyboard) ? Math.Max(4.5f, Math.Min(9f, keyboard.Width / scale / 65f)) : 9f;
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { tip.Dispose(); if (deviceArtwork != null) deviceArtwork.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class RelationshipButton : EditorButton
    {
        public bool Custom, Pending, Removed, Unknown, Fixed, IsOffline, KeyEnhancementOwned;
        public string DetailText, IdentityText;
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f; EditorArtwork.ClearSurface(this, e.Graphics, Hovered ? EditorTheme.Hover : EditorTheme.Floating);
            int inset = (int)(12 * scale), iconSize = (int)(24 * scale), left = inset + iconSize + (int)(12 * scale);
            Image icon = EditorArtwork.ActionIcon(ActionId);
            var iconBounds = new Rectangle(inset, (Height - iconSize) / 2, iconSize, iconSize);
            if (icon != null)
            {
                if (Removed)
                {
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[] {
                            new[] { .3f, .3f, .3f, 0f, 0f }, new[] { .59f, .59f, .59f, 0f, 0f },
                            new[] { .11f, .11f, .11f, 0f, 0f }, new[] { 0f, 0f, 0f, .38f, 0f }, new[] { 0f, 0f, 0f, 0f, 1f } }));
                        e.Graphics.DrawImage(icon, iconBounds, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
                else e.Graphics.DrawImage(icon, iconBounds);
            }
            using (var titleFont = new Font(Font, Removed ? FontStyle.Strikeout : Font.Style))
                TextRenderer.DrawText(e.Graphics, Text, titleFont, new Rectangle(left, (int)(5 * scale), Width - left - inset, IdentityText == null ? Height / 2 : Font.Height + (int)(4 * scale)), Removed ? EditorTheme.Muted : EditorTheme.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Removed)
                using (var pen = new Pen(EditorTheme.Muted, 1.5f * scale))
                    e.Graphics.DrawLine(pen, iconBounds.Left, iconBounds.Bottom, iconBounds.Right, iconBounds.Top);
            string status = DetailText ?? I18n.Str(Pending ? IsOffline ? "offline.pending" : "views.pending" : Removed ? "views.removedshort" : Fixed ? "views.fixed" : Unknown ? "editor.defaultunknown" : Custom ? "views.savedcustom" : "views.original");
            if (Pending && Removed && DetailText == null) status = I18n.Str("views.removedshort") + " · " + status;
            int y = IdentityText == null ? Height * 2 / 3 : Height - (int)(18 * scale), dot = (int)(6 * scale);
            BindingStatus.Dot(e.Graphics, Removed ? BindingStatus.Factory : Pending ? BindingStatus.Pending : KeyEnhancementOwned ? EditorTheme.KeyEnhancement : Custom ? BindingStatus.Custom : BindingStatus.Factory, left, y, dot);
            using (var statusFont = new Font(Font.FontFamily, 8f))
            {
                if (IdentityText != null)
                    TextRenderer.DrawText(e.Graphics, IdentityText, statusFont,
                        new Rectangle(left, Font.Height + (int)(10 * scale), Width - left - inset, statusFont.Height + (int)(4 * scale)),
                        EditorTheme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                var bounds = new Rectangle(left + dot + (int)(5 * scale), y - (int)(5 * scale), Width - left - dot - inset, (int)(18 * scale));
                TextRenderer.DrawText(e.Graphics, status, statusFont, bounds, Pending && !Removed ? BindingStatus.Pending : KeyEnhancementOwned && !Removed ? EditorTheme.KeyEnhancement : EditorTheme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    internal sealed class BindingLegend : Control
    {
        public bool IsOffline;
        public BindingLegend()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); EditorArtwork.PaintEffect(this, e.Graphics); float scale = e.Graphics.DpiX / 96f, left = 8 * scale;
            string[] names = { "views.original", "views.savedcustom", IsOffline ? "offline.pending" : "views.pending" };
            Color[] colors = { BindingStatus.Factory, BindingStatus.Custom, BindingStatus.Pending };
            using (var font = new Font(Font.FontFamily, 8f))
            for (int i = 0; i < names.Length; i++)
            {
                string text = I18n.Str(names[i]);
                BindingStatus.Dot(e.Graphics, colors[i], left, Height / 2f - 3 * scale, 6 * scale);
                TextRenderer.DrawText(e.Graphics, text, font, new Point((int)(left + 11 * scale), (Height - font.Height) / 2), EditorTheme.Muted);
                left += TextRenderer.MeasureText(e.Graphics, text, font).Width + 28 * scale;
            }
        }
    }

    internal sealed class KeyBindingView : EditorPanel
    {
        readonly TableLayoutPanel columns = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        readonly Panel devices = new EditorPanel(), selected = new EditorPanel();
        readonly Panel detail = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = EditorTheme.Surface };
        readonly TableLayoutPanel keyCommands = new EditorLayout { Dock = DockStyle.Bottom, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        readonly Label hint = BindingViewText.Label("", 24), heading = BindingViewText.Label("", 30), count = BindingViewText.Label("", 24);
        readonly Label notice = BindingViewText.Label("", 40);
        readonly BindingLegend legend = new BindingLegend { Dock = DockStyle.Bottom, Height = 28 };
        readonly ToolTip ownershipTip = new ToolTip();
        readonly EditorButton add = new EditorButton { Dock = DockStyle.Top }, reset = new EditorButton { Dock = DockStyle.Top },
            actionView = new EditorButton { Dock = DockStyle.Top }, keyTools = new EditorButton { Dock = DockStyle.Top };
        public BindingWorkspace Workspace;
        public event Action<string> AddFunctionRequested;
        public event Action<BindingEntry> EditBindingRequested, RemoveBindingRequested, RestoreBindingRequested;
        readonly List<RelationshipRow> actionRows = new List<RelationshipRow>();
        DeviceDiagram diagram;
        ActionRelations[] rawRelations, relations;
        Dictionary<string, ActionRelations> rawBefore, before;
        EditorSession currentSession;
        bool currentPad;
        int currentFamily;
        string selectedKey;
        Font headingFont;
        public event Action<string> EditAction;
        public event Action<string, string, bool> EditRelation;
        public event Action<string> RestoreSelectedKey;
        public event Action<string, bool> MoveSelectedKey;
        public event Action ActionViewRequested;
        public KeyBindingView()
        {
            DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = EditorTheme.Surface;
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            devices.Dock = selected.Dock = DockStyle.Fill; devices.Margin = new Padding(0, 0, 8, 0); selected.Margin = Padding.Empty;
            devices.BackColor = selected.BackColor = EditorTheme.Surface;
            columns.Controls.Add(devices, 0, 0); columns.Controls.Add(selected, 1, 0); Controls.Add(columns);
            Controls.Add(legend);
            hint.ForeColor = notice.ForeColor = EditorTheme.Muted;
            UpdateHeadingFont();
            devices.Controls.Add(hint); selected.Controls.Add(detail); selected.Controls.Add(keyCommands); selected.Controls.Add(count); selected.Controls.Add(heading);
            keyCommands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            keyCommands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            keyCommands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            keyCommands.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            keyCommands.Height = Font.Height + 24;
            add.Dock = reset.Dock = keyTools.Dock = DockStyle.Fill;
            add.Margin = reset.Margin = keyTools.Margin = new Padding(1, 3, 1, 3);
            keyCommands.Controls.Add(add, 0, 0); keyCommands.Controls.Add(reset, 1, 0); keyCommands.Controls.Add(keyTools, 2, 0);
            foreach (var command in new[] { add, reset, keyTools })
            {
                var button = command;
                button.TextChanged += delegate { ownershipTip.SetToolTip(button, button.Text); };
            }
            detail.HandleCreated += delegate { EditorArtwork.DarkScrollbars(detail); };
            detail.Controls.AddRange(new Control[] { actionView, notice });
            detail.Layout += delegate {
                // Keep the scrollbar gutter reserved when the list is short.
                int right = detail.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
                if (detail.Padding.Right != right) detail.Padding = new Padding(0, 0, right, 0);
            };
            add.Click += delegate {
                if (currentSession == null) return;
                if (AddFunctionRequested != null) { AddFunctionRequested(selectedKey); return; }
                var actions = currentSession.BindingActions(currentPad, currentFamily)
                    .Where(a => !currentSession.Relations(a, currentPad, currentFamily).Keys.Any(k => BindingViewText.PhysicalKey(k) == selectedKey));
                using (var choice = new ActionChoiceDialog(currentSession, actions, currentPad, currentFamily))
                    if (choice.ShowDialog(FindForm()) == DialogResult.OK && EditRelation != null)
                    {
                        var defaults = currentSession.Relations(choice.Result, currentPad, currentFamily).DefaultKeys;
                        string key = defaults == null ? null : defaults.FirstOrDefault(k => BindingViewText.PhysicalKey(k) == selectedKey);
                        EditRelation(choice.Result, key ?? selectedKey, true);
                    }
            };
            keyTools.Click += delegate {
                var menu = new ContextMenuStrip();
                menu.Items.Add(I18n.Str("editing.move"), null, delegate { if (MoveSelectedKey != null) MoveSelectedKey(selectedKey, false); });
                menu.Items.Add(I18n.Str("editing.swap"), null, delegate { if (MoveSelectedKey != null) MoveSelectedKey(selectedKey, true); });
                menu.Closed += delegate { BeginInvoke((Action)delegate { menu.Dispose(); }); };
                menu.Show(keyTools, new Point(0, keyTools.Height));
            };
            reset.Click += delegate { if (RestoreSelectedKey != null) RestoreSelectedKey(selectedKey); };
            actionView.Click += delegate { if (ActionViewRequested != null) ActionViewRequested(); };
        }
        void UpdateHeadingFont()
        {
            if (heading == null) return;
            Font previous = headingFont;
            heading.Font = headingFont = new Font(Font, FontStyle.Bold);
            if (keyCommands != null) keyCommands.Height = Font.Height + 24;
            if (previous != null) previous.Dispose();
        }
        protected override void OnFontChanged(EventArgs e)
        { base.OnFontChanged(e); UpdateHeadingFont(); }
        protected override void Dispose(bool disposing)
        { base.Dispose(disposing); if (disposing) { ownershipTip.Dispose(); if (headingFont != null) { headingFont.Dispose(); headingFont = null; } } }
        public void Render(EditorSession session, bool pad, int familyIndex)
        {
            if (session != null) Render(session, new BindingSnapshot(session, pad, familyIndex,
                session.ControllerFamilies.Where(f => f.Index == familyIndex).Select(f => f.Name).FirstOrDefault()));
        }
        internal void Render(EditorSession session, BindingSnapshot snapshot)
        {
            bool pad = snapshot.Gamepad;
            int familyIndex = snapshot.FamilyIndex;
            legend.IsOffline = session.IsOffline; legend.Invalidate();
            currentSession = session; currentPad = pad; currentFamily = familyIndex;
            if (selectedKey == null || pad != selectedKey.StartsWith("Gamepad_", StringComparison.Ordinal)) selectedKey = pad ? "Gamepad_FaceButton_Left" : "LeftMouseButton";
            rawRelations = snapshot.Current;
            relations = rawRelations.Select(BindingViewText.PhysicalRelations).ToArray();
            rawBefore = snapshot.Before;
            before = rawBefore.ToDictionary(r => r.Key, r => BindingViewText.PhysicalRelations(r.Value));
            string[] known = relations.SelectMany(r => r.Keys).Concat(relations.Where(r => r.DefaultKeys != null).SelectMany(r => r.DefaultKeys))
                .Concat(before.Values.SelectMany(r => r.Keys)).Concat(new[] { selectedKey }).Distinct().ToArray();
            string familyName = snapshot.Family;
            var physical = pad ? DeviceHotspots.ForDevice("xbox").Keys.Concat(DeviceHotspots.ForDevice("playstation").Keys)
                : DeviceHotspots.ForDevice("keyboard").Keys.Concat(DeviceHotspots.ForDevice("mouse").Keys);
            var states = physical.Concat(known).Distinct().ToDictionary(key => key, key => new KeyBadgeState {
                IsOffline = session.IsOffline,
                Count = relations.Count(r => r.Keys.Contains(key)), Complete = relations.All(r => r.KnowsKey(key)),
                Differs = relations.Any(r => r.DefaultKeys != null && (r.Keys.Contains(key) && !r.DefaultKeys.Contains(key)
                    || r.Complete && !r.Keys.Contains(key) && r.DefaultKeys.Contains(key))),
                Pending = relations.Any(r => r.Keys.Contains(key) != before[r.Action].Keys.Contains(key)
                    && r.KnowsKey(key) && before[r.Action].KnowsKey(key)) });
            Func<string, KeyBadgeState> state = key => states[key];
            if (diagram == null)
            {
                diagram = new DeviceDiagram(pad, familyName, state,
                    delegate(string key) { selectedKey = key; diagram.SelectKey(key); RenderSelectedKey(); }, selectedKey, known);
                devices.Controls.Add(diagram); diagram.BringToFront();
            }
            else diagram.UpdateBindings(pad, familyName, state, selectedKey, known);
            hint.Text = I18n.Str("views.clickkey"); hint.Height = Font.Height + 10;
            RenderSelectedKey();
        }

        void RenderSelectedKey()
        {
            if (Workspace != null) { RenderWorkspaceKey(); return; }
            var session = currentSession;
            bool pad = currentPad;
            keyTools.Text = I18n.Str("editing.keytools") + "…"; keyTools.Height = Font.Height + 18; keyTools.Enabled = !currentSession.IsResetPending;
            heading.Text = BindingButton.FriendlyKey(selectedKey); heading.Height = Font.Height + 16;
            var summary = new KeyRelations { Key = selectedKey,
                Current = relations.Where(r => r.Keys.Contains(selectedKey)).ToArray(),
                Added = relations.Where(r => r.DefaultKeys != null && r.Keys.Contains(selectedKey) && !r.DefaultKeys.Contains(selectedKey)).ToArray(),
                Removed = relations.Where(r => r.CanCompare && !r.Keys.Contains(selectedKey) && r.DefaultKeys.Contains(selectedKey)).ToArray(),
                CurrentComplete = relations.All(r => r.KnowsKey(selectedKey)), FactoryComplete = relations.All(r => r.DefaultKeys != null) };
            count.Text = I18n.Str("views.count", summary.CountText); count.Height = Font.Height + 8;
            var removed = summary.Removed.Concat(relations.Where(r => r.KnowsKey(selectedKey) && before[r.Action].Keys.Contains(selectedKey) && !r.Keys.Contains(selectedKey)))
                .GroupBy(r => r.Action).Select(g => g.First()).ToArray();
            var displayed = summary.Current.Concat(removed).ToArray();
            detail.SuspendLayout();
            try
            {
                while (actionRows.Count > displayed.Length)
                { int last = actionRows.Count - 1; actionRows[last].Dispose(); actionRows.RemoveAt(last); }
                for (int i = 0; i < displayed.Length; i++)
                {
                    if (i == actionRows.Count)
                    {
                        var row = new RelationshipRow();
                        row.Operation.Click += delegate {
                            if (EditRelation == null) return;
                            string action = row.Edit.ActionId; bool restore = row.Edit.Removed;
                            foreach (string key in row.InputKeys.ToArray()) EditRelation(action, key, restore);
                        };
                        row.Edit.Click += delegate { if (EditAction != null) EditAction(row.Edit.ActionId); };
                        actionRows.Add(row); detail.Controls.Add(row);
                    }
                    var relation = displayed[i]; bool isRemoved = i >= summary.Current.Length;
                    bool custom = isRemoved || summary.Added.Any(r => r.Action == relation.Action);
                    var item = actionRows[i]; var edit = item.Edit; var operation = item.Operation;
                    item.Height = Font.Height * 2 + 20;
                    operation.Width = Font.Height + 16; operation.Text = isRemoved ? "↶" : "−";
                    var raw = rawRelations.First(r => r.Action == relation.Action);
                    item.InputKeys = (isRemoved ? (raw.DefaultKeys ?? new string[0]).Concat(rawBefore[raw.Action].Keys) : raw.Keys)
                        .Where(k => BindingViewText.PhysicalKey(k) == selectedKey).ToArray();
                    bool fixedBinding = (Workspace == null || !Workspace.KeyEnhancementEnabled) && session.Defaults.FixedForAction(relation.Action, pad).Any(k => BindingViewText.PhysicalKey(k) == selectedKey);
                    operation.Enabled = !fixedBinding && (relation.Complete || !isRemoved && relation.DefaultKeys != null && !relation.DefaultKeys.Contains(selectedKey)) && !session.IsResetPending;
                    operation.AccessibleName = I18n.Str(isRemoved ? "views.restore" : "views.remove") + " " + BindingViewText.ActionName(relation.Action);
                    edit.ActionId = relation.Action; edit.Text = BindingViewText.ActionName(relation.Action);
                    edit.DetailText = null; edit.IdentityText = null; edit.KeyEnhancementOwned = false;
                    if (Workspace != null)
                    {
                        var function = Workspace.Functions.First(f => f.Id == relation.Action);
                        edit.Text = function.Label;
                        var functionEntries = Workspace.Entries(function.Id);
                        var entries = functionEntries.Where(entry => entry.Active &&
                            entry.Keys.Concat(string.IsNullOrEmpty(entry.Axis) ? new string[0] : new[] { entry.Axis })
                                .Any(key => BindingViewText.PhysicalKey(key) == selectedKey)).ToArray();
                        string bindings = string.Join(" · ", entries.Where(entry => !entry.IsNative).Select(UnifiedBindingUi.BindingText).Distinct());
                        if (function.RequiresKeyEnhancement)
                            edit.DetailText = UnifiedActionChoiceDialog.FunctionCode(function) + (bindings.Length == 0 ? "" : " · " + bindings);
                        else if (bindings.Length > 0) edit.DetailText = bindings;
                        var ownership = functionEntries.Where(entry =>
                            entry.Keys.Concat(string.IsNullOrEmpty(entry.Axis) ? new string[0] : new[] { entry.Axis })
                                .Any(key => BindingViewText.PhysicalKey(key) == selectedKey)).ToArray();
                        ownershipTip.SetToolTip(edit, function.TechnicalName + Environment.NewLine + string.Join(Environment.NewLine, ownership.Select(entry =>
                            UnifiedBindingUi.BindingText(entry) + Environment.NewLine + UnifiedBindingUi.EntryExecution(entry) + Environment.NewLine + UnifiedBindingUi.EntryPolicy(entry))));
                        string owner = ownership.Select(entry => entry.ExecutionOwner).Distinct().Count() == 1 && ownership.Length > 0 ? ownership[0].ExecutionOwner : null;
                        edit.KeyEnhancementOwned = owner == "scheduler";
                        if (owner != null)
                        {
                            string execution = I18n.Str("ownership.owner." + (owner == "scheduler" ? "scheduler" : owner == "game" ? "game" : owner == "inactive" ? "off" : "uncovered"));
                            edit.DetailText = string.IsNullOrEmpty(edit.DetailText) ? execution : edit.DetailText + " · " + execution;
                        }
                    }
                    edit.Removed = isRemoved; edit.Custom = custom; edit.Unknown = !custom && relation.DefaultKeys == null; edit.Fixed = fixedBinding; edit.IsOffline = session.IsOffline;
                    edit.Pending = relation.Keys.Contains(selectedKey) != before[relation.Action].Keys.Contains(selectedKey)
                        && relation.KnowsKey(selectedKey) && before[relation.Action].KnowsKey(selectedKey);
                    edit.AccessibleName = edit.Text + " · " + I18n.Str(edit.Pending ? session.IsOffline ? "offline.pending" : "views.pending" : isRemoved ? "views.removedshort" : custom ? "views.savedcustom" : "views.original"); edit.Invalidate();
                }
                add.Height = Font.Height + 18; add.Text = "+ " + I18n.Str("views.addaction"); add.Enabled = !session.IsResetPending;
                reset.Height = Font.Height + 16; reset.Text = I18n.Str("views.restorekey");
                reset.Enabled = summary.CurrentComplete && summary.FactoryComplete && summary.Differs && !session.IsResetPending;
                notice.Height = Font.Height * 2 + 4; notice.Text = I18n.Str("views.partial");
                actionView.Height = Font.Height + 16; actionView.Text = I18n.Str("views.actionview");
                notice.Visible = actionView.Visible = !summary.CurrentComplete || !summary.FactoryComplete;
                var order = actionRows.Cast<Control>().Concat(new Control[] { notice, actionView }).Reverse().ToArray();
                for (int i = 0; i < order.Length; i++)
                    if (detail.Controls.GetChildIndex(order[i]) != i) detail.Controls.SetChildIndex(order[i], i);
            }
            finally { detail.ResumeLayout(true); }
        }
        void RenderWorkspaceKey()
        {
            keyTools.Text = I18n.Str("editing.keytools") + "…"; keyTools.Height = Font.Height + 18; keyTools.Enabled = !currentSession.IsResetPending;
            heading.Text = BindingButton.FriendlyKey(selectedKey); heading.Height = Font.Height + 16;
            var entries = Workspace.GetSnapshot().Where(entry => entry.Keys.Concat(string.IsNullOrEmpty(entry.Axis) ? new string[0] : new[] { entry.Axis })
                .Concat(entry.Rule == null ? new string[0] : entry.Rule.Steps.SelectMany(step => step).Concat(entry.Rule.Watch))
                .Any(key => BindingViewText.PhysicalKey(key) == selectedKey)).ToList();
            foreach (var relation in rawRelations.Where(r => !r.Keys.Any(k => BindingViewText.PhysicalKey(k) == selectedKey)))
                foreach (string key in (relation.DefaultKeys ?? new string[0]).Concat(rawBefore[relation.Action].Keys).Distinct()
                    .Where(k => BindingViewText.PhysicalKey(k) == selectedKey))
                    if (!entries.Any(entry => entry.FunctionId == relation.Action && entry.IsNative && entry.Keys.Contains(key)))
                    {
                        var function = Workspace.Functions.First(item => item.Id == relation.Action);
                        if (function.NativeAction != null || function.FixedKeys != null)
                            entries.Add(new BindingEntry { FunctionId = relation.Action, Keys = new[] { key }, IsNative = true, Suppressed = true,
                                Pending = relation.RelationshipPending(rawBefore[relation.Action], key), ExecutionOwner = "inactive",
                                ReadOnly = function.FixedKeys != null, Enabled = true, IsDefault = relation.DefaultKeys != null && relation.DefaultKeys.Contains(key) });
                    }
            count.Text = I18n.Str("binding.count", entries.Count); count.Height = Font.Height + 8;
            detail.SuspendLayout();
            try
            {
                while (actionRows.Count > entries.Count) { int i = actionRows.Count - 1; actionRows[i].Dispose(); actionRows.RemoveAt(i); }
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i == actionRows.Count)
                    {
                        var added = new RelationshipRow();
                        added.Edit.Click += delegate {
                            if (added.Entry.IsNative && added.Entry.Id == null) { if (EditAction != null) EditAction(added.Entry.FunctionId); }
                            else if (EditBindingRequested != null) EditBindingRequested(added.Entry);
                        };
                        added.Operation.Click += delegate {
                            var clicked = added.Entry;
                            if (clicked.Suppressed || !clicked.Enabled) { if (RestoreBindingRequested != null) RestoreBindingRequested(clicked); }
                            else if (RemoveBindingRequested != null) RemoveBindingRequested(clicked);
                        };
                        actionRows.Add(added); detail.Controls.Add(added);
                    }
                    var row = actionRows[i]; var entry = entries[i]; row.Entry = entry; row.InputKeys = entry.Keys;
                    var function = Workspace.Functions.First(item => item.Id == entry.FunctionId);
                    row.Height = Font.Height * 3 + 24;
                    row.Edit.ActionId = entry.FunctionId; row.Edit.Text = function.Label;
                    row.Edit.IdentityText = UnifiedBindingUi.FunctionIdentity(function);
                    row.Edit.DetailText = UnifiedBindingUi.EntryDescription(entry) + " · " + UnifiedBindingUi.EntryScope(entry);
                    row.Edit.Removed = entry.Suppressed || !entry.Active; row.Edit.Pending = entry.Pending; row.Edit.Custom = !entry.IsDefault;
                    row.Edit.KeyEnhancementOwned = entry.ExecutionOwner == "scheduler"; row.Edit.IsOffline = currentSession.IsOffline;
                    row.Operation.AutoSize = true; row.Operation.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    row.Operation.Text = entry.Suppressed && entry.IsDefault ? I18n.Str("views.restore") : entry.Suppressed || !entry.Enabled ? I18n.Str("binding.enable") : I18n.Str("binding.unbind");
                    row.Operation.Enabled = !currentSession.IsResetPending && (!entry.IsNative || !entry.ReadOnly || Workspace.KeyEnhancementEnabled);
                    row.Operation.AccessibleName = row.Operation.Text + " " + UnifiedBindingUi.BindingText(entry);
                    ownershipTip.SetToolTip(row.Edit, function.TechnicalName + Environment.NewLine + UnifiedBindingUi.FunctionIdentity(function)
                        + Environment.NewLine + UnifiedBindingUi.EntryDescription(entry) + Environment.NewLine + UnifiedBindingUi.EntryScope(entry)
                        + Environment.NewLine + UnifiedBindingUi.EntryExecution(entry) + Environment.NewLine + UnifiedBindingUi.EntryPolicy(entry));
                    row.Edit.Invalidate();
                }
                add.Height = Font.Height + 18; add.Text = "+ " + I18n.Str("views.addaction"); add.Enabled = !currentSession.IsResetPending;
                reset.Height = Font.Height + 18; reset.Text = I18n.Str("views.restorekey"); reset.Enabled = !currentSession.IsResetPending && entries.Any(entry => !entry.IsDefault || entry.Suppressed || !entry.Enabled || entry.Pending);
                notice.Visible = actionView.Visible = false;
                var order = actionRows.Cast<Control>().Concat(new Control[] { notice, actionView }).Reverse().ToArray();
                for (int i = 0; i < order.Length; i++) detail.Controls.SetChildIndex(order[i], i);
            }
            finally { detail.ResumeLayout(true); }
        }

        sealed class RelationshipRow : EditorPanel
        {
            public BindingEntry Entry;
            public string[] InputKeys = new string[0];
            public readonly EditorButton Operation = new EditorButton { Dock = DockStyle.Right };
            public readonly RelationshipButton Edit = new RelationshipButton { Dock = DockStyle.Fill };
            public RelationshipRow()
            { DoubleBuffered = true; Dock = DockStyle.Top; Padding = new Padding(0, 3, 0, 3); BackColor = EditorTheme.Surface; Controls.Add(Edit); Controls.Add(Operation); }
            protected override void WndProc(ref Message message)
            { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
        }
    }

    internal sealed class ActionChoiceDialog : EditorWindow
    {
        public string Result;
        public ActionChoiceDialog(EditorSession session, IEnumerable<string> actions, bool gamepad, int family)
        {
            Text = I18n.Str("views.add"); Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 270); StartPosition = FormStartPosition.CenterParent; AllowResize = false;
            BackColor = EditorTheme.Background; ForeColor = EditorTheme.Ink;
            var combo = new EditorComboBox { Name = "actionChoice", Left = 20, Top = Caption.Height + 18, Width = 500, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string action in actions) combo.Items.Add(new ActionInfo(action));
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            var capacity = new EditorLabel { Name = "bindingCapacity", Left = 20, Top = Caption.Height + 60, Width = 500, Height = 24, ForeColor = EditorTheme.Muted };
            var current = new EditorLabel { Left = 20, Top = Caption.Height + 87, Width = 500, Height = 24, ForeColor = EditorTheme.Muted, AutoEllipsis = true };
            var hint = new EditorLabel { Name = "capacityHint", Left = 20, Top = Caption.Height + 117, Width = 500, Height = 46, ForeColor = EditorTheme.Muted };
            var ok = new EditorButton { Name = "addSelectedAction", Left = 312, Top = Caption.Height + 177, Width = 100, Height = 34, Text = I18n.Str("views.add"), Enabled = combo.Items.Count > 0 };
            ok.Click += delegate { Result = ((ActionInfo)combo.SelectedItem).Id; DialogResult = DialogResult.OK; };
            Action update = delegate {
                var selected = combo.SelectedItem as ActionInfo;
                if (selected == null) { ok.Enabled = false; return; }
                var available = session.RelationshipCapacity(selected.Id, gamepad, family);
                var relation = session.Relations(selected.Id, gamepad, family);
                capacity.Text = I18n.Str("views.bindingcount", available.Used, available.Limit);
                current.Text = I18n.Str("views.currentkeys", BindingViewText.Keys(relation.Keys));
                ok.Enabled = available.CanAdd && !session.IsResetPending;
                hint.Text = !relation.Complete ? I18n.Str("views.incomplete") : available.CanAdd ? I18n.Str("views.bindinglimit", available.Limit)
                    : I18n.Str("views.bindingfull", BindingViewText.ActionName(selected.Id), available.Limit);
                hint.ForeColor = available.CanAdd ? EditorTheme.Muted : BindingStatus.Pending;
            };
            combo.SelectedIndexChanged += delegate { update(); }; update();
            var cancel = new EditorButton { Left = 420, Top = ok.Top, Width = 100, Height = 34, Text = I18n.Str("editor.cancel"), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { combo, capacity, current, hint, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
    }
}
