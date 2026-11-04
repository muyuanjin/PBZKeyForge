using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class SchemeDiffView : Panel
    {
        sealed class DifferenceRows : Panel
        {
            public DifferenceRows() { DoubleBuffered = true; ResizeRedraw = true; }
            protected override void OnScroll(ScrollEventArgs e)
            { base.OnScroll(e); Invalidate(true); }
            protected override void OnMouseWheel(MouseEventArgs e)
            { base.OnMouseWheel(e); Invalidate(true); }
            protected override void WndProc(ref Message message)
            { if (!EditorWindow.PassResizeHit(this, ref message)) base.WndProc(ref message); }
        }
        readonly Panel rows = new DifferenceRows { Dock = DockStyle.Fill, AutoScroll = true };
        readonly TableLayoutPanel header = new EditorLayout { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 1 };
        readonly Label title = new Label(), total = new Label();
        readonly EditorTab saved = new EditorTab(), factory = new EditorTab();
        readonly ToolTip tip = new ToolTip();
        readonly Label empty = BindingViewText.Label("", 30);
        readonly Dictionary<string, int> rowModes = new Dictionary<string, int>();
        EditorSession session;
        bool compareFactory;
        static string RowId(string profile, BindingDifference difference)
        { return profile + "\u001f" + difference.Gamepad + "\u001f" + difference.FamilyIndex + "\u001f" + difference.Family + "\u001f" + difference.Action; }
        public bool CompareFactory { get { return compareFactory; } }
        public void SetComparison(bool value) { compareFactory = value; if (session != null) Render(session); }
        public SchemeDiffView()
        {
            DoubleBuffered = true; BackColor = EditorTheme.Surface; Dock = DockStyle.Bottom;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            title.Dock = total.Dock = DockStyle.Fill; title.TextAlign = ContentAlignment.MiddleLeft;
            total.TextAlign = ContentAlignment.MiddleRight; total.AutoEllipsis = title.AutoEllipsis = true;
            title.Padding = new Padding(8, 0, 0, 0); total.Padding = new Padding(0, 0, 10, 0);
            saved.Dock = factory.Dock = DockStyle.Fill;
            saved.Margin = factory.Margin = Padding.Empty;
            saved.Click += delegate { SetComparison(false); }; factory.Click += delegate { SetComparison(true); };
            header.Controls.Add(title, 0, 0); header.Controls.Add(saved, 1, 0); header.Controls.Add(factory, 2, 0); header.Controls.Add(total, 3, 0);
            Controls.Add(rows); Controls.Add(header);
            rows.BackColor = EditorTheme.Surface;
            empty.ForeColor = EditorTheme.Muted; rows.Controls.Add(empty);
            rows.HandleCreated += delegate { EditorArtwork.DarkScrollbars(rows); };
        }
        public void Render(EditorSession value)
        {
            if (value != null) Render(value, value.BindingSnapshots());
        }
        internal void Render(EditorSession value, BindingSnapshot[] snapshots)
        {
            session = value;
            header.Height = Font.Height + 18;
            title.Text = I18n.Str("views.schemediff"); saved.Text = I18n.Str("views.vssaved"); factory.Text = I18n.Str("views.vsfactory");
            saved.Selected = !compareFactory; factory.Selected = compareFactory;
            tip.SetToolTip(saved, I18n.Str("views.savedhelp")); tip.SetToolTip(factory, factory.Text);
            var changes = snapshots.SelectMany(s => s.Differences(compareFactory)).ToArray();
            bool incomplete = compareFactory ? snapshots.Any(s => s.Current.Any(r => !r.CanCompare))
                : changes.Any(d => !d.BeforeComplete || !d.AfterComplete);
            int added = changes.Sum(d => d.AddedKeys.Length);
            int removed = changes.Sum(d => d.RemovedKeys.Length);
            total.Text = (incomplete ? "≥ " : "") + "+" + added + " / −" + removed;
            tip.SetToolTip(total, I18n.Str("views.relationcounts") + (incomplete ? "\n" + I18n.Str("views.partial") : ""));
            empty.Text = I18n.Str(incomplete ? "views.partialdiff" : compareFactory || session.IsOffline ? "views.nofactorydiff" : "views.nosaveddiff");
            empty.Height = Font.Height + 20; empty.Visible = changes.Length == 0;
            rows.SuspendLayout();
            try
            {
                var existing = rows.Controls.OfType<DifferenceRow>().ToList();
                while (existing.Count > changes.Length)
                { int last = existing.Count - 1; tip.SetToolTip(existing[last], null); existing[last].Dispose(); existing.RemoveAt(last); }
                var reversed = changes.Reverse().ToArray();
                for (int i = 0; i < reversed.Length; i++)
                {
                    if (i == existing.Count)
                    {
                        var addedRow = new DifferenceRow { Dock = DockStyle.Top };
                        addedRow.ModeChanged += delegate {
                            rowModes[addedRow.Identity] = addedRow.Mode;
                            SetRowTip(addedRow);
                        };
                        rows.Controls.Add(addedRow); existing.Add(addedRow);
                    }
                    var row = existing[i]; var change = reversed[i];
                    string identity = RowId(session.SelectedProfile, change); int mode;
                    rowModes.TryGetValue(identity, out mode);
                    row.Height = Font.Height + 18;
                    var snapshot = snapshots.Single(s => s.Gamepad == change.Gamepad && s.FamilyIndex == change.FamilyIndex);
                    var before = snapshot.Before[change.Action];
                    var current = snapshot.Current.First(r => r.Action == change.Action);
                    row.UpdateDifference(change, identity, mode, change.AddedKeys.Where(key => current.RelationshipPending(before, key)));
                    SetRowTip(row);
                }
            }
            finally { rows.ResumeLayout(true); }
        }
        static string DeviceName(BindingDifference diff)
        { return I18n.Str(diff.Gamepad ? "editor.gamepad" : "editor.keyboard") + (diff.Gamepad ? " · " + new ControllerFamily { Name = diff.Family } : ""); }
        static string KeyText(string[] keys, bool complete)
        { return complete ? BindingViewText.Keys(keys) : (keys.Length == 0 ? I18n.Str("editor.defaultunknown") : "≥ " + BindingViewText.Keys(keys)); }
        void SetRowTip(DifferenceRow row)
        {
            var change = row.Difference;
            tip.SetToolTip(row, DeviceName(change) + " · " + BindingViewText.ActionName(change.Action) + "\n"
                + I18n.Str("views.diffbefore") + ": " + KeyText(change.BeforeKeys, change.BeforeComplete) + "\n"
                + I18n.Str("views.diffresult") + ": " + KeyText(DifferenceRow.ResultKeys(change), change.AfterComplete) + "\n"
                + row.ModeName + "\n" + I18n.Str("views.diffcycle"));
        }
        sealed class DifferenceRow : Control
        {
            public BindingDifference Difference;
            public string Identity;
            public int Mode;
            public event Action ModeChanged;
            HashSet<string> pendingKeys = new HashSet<string>();
            static readonly Color[] ModeColors = { Color.Empty, Color.FromArgb(89, 161, 219), Color.FromArgb(61, 175, 154), Color.FromArgb(171, 132, 208) };
            static readonly Color RemovedColor = Color.FromArgb(139, 145, 148);
            const TextFormatFlags LabelFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
            const TextFormatFlags Flags = LabelFlags | TextFormatFlags.NoPadding;
            public string ModeName { get { return I18n.Str(new[] { "views.diffcombined", "views.diffchanges", "views.diffresult", "views.diffbefore" }[Mode]); } }
            public DifferenceRow()
            {
                DoubleBuffered = true; BackColor = EditorTheme.Surface; TabStop = true; AccessibleRole = AccessibleRole.PushButton;
                SetStyle(ControlStyles.Selectable, true);
            }
            public void UpdateDifference(BindingDifference difference, string identity, int mode, IEnumerable<string> pending)
            {
                Difference = difference; Identity = identity; Mode = mode; pendingKeys = new HashSet<string>(pending);
                AccessibleName = BindingViewText.ActionName(difference.Action) + " · " + ModeName + ": " + string.Join(" ", Parts().Select(p => p.Text));
                AccessibleDescription = I18n.Str("views.diffcycle"); Invalidate();
            }
            public static string[] ResultKeys(BindingDifference difference)
            { return difference.BeforeKeys.Where(difference.AfterKeys.Contains).Concat(difference.AfterKeys.Except(difference.BeforeKeys)).Distinct().ToArray(); }
            sealed class Part
            {
                public string Text; public Color Color; public bool Removed;
                public Part(string text, Color color, bool removed) { Text = text; Color = color; Removed = removed; }
            }
            Part[] Parts()
            {
                var diff = Difference; var parts = new List<Part>();
                if (Mode == 2 || Mode == 3)
                    parts.Add(new Part(KeyText(Mode == 2 ? ResultKeys(diff) : diff.BeforeKeys, Mode == 2 ? diff.AfterComplete : diff.BeforeComplete), EditorTheme.Ink, false));
                else
                {
                    foreach (string key in diff.AddedKeys)
                        parts.Add(new Part("+" + BindingButton.FriendlyKey(key), pendingKeys.Contains(key) ? BindingStatus.Pending : BindingStatus.Custom, false));
                    if (Mode == 0)
                    {
                        foreach (string key in diff.BeforeKeys.Where(diff.AfterKeys.Contains))
                            parts.Add(new Part(BindingButton.FriendlyKey(key), EditorTheme.Ink, false));
                        foreach (string key in diff.AfterKeys.Except(diff.BeforeKeys).Except(diff.AddedKeys))
                            parts.Add(new Part(BindingButton.FriendlyKey(key), EditorTheme.Ink, false));
                        if (diff.AfterComplete && diff.AfterKeys.Length == 0) parts.Add(new Part(I18n.Str("editor.unbound"), EditorTheme.Ink, false));
                    }
                    foreach (string key in diff.RemovedKeys)
                        parts.Add(new Part("−" + BindingButton.FriendlyKey(key), RemovedColor, true));
                    if (!diff.BeforeComplete || !diff.AfterComplete) parts.Add(new Part(I18n.Str("editor.defaultunknown"), EditorTheme.Muted, false));
                }
                return parts.ToArray();
            }
            Rectangle KeyArea()
            {
                float scale = DeviceDpiScale(); int inset = (int)(10 * scale);
                return new Rectangle(Width * 45 / 100 + inset, (int)(3 * scale), Math.Max(0, Width * 55 / 100 - 2 * inset), Math.Max(0, Height - (int)(6 * scale)));
            }
            float DeviceDpiScale() { using (var graphics = CreateGraphics()) return graphics.DpiX / 96f; }
            void CycleMode()
            {
                Mode = (Mode + 1) % 4;
                AccessibleName = BindingViewText.ActionName(Difference.Action) + " · " + ModeName + ": " + string.Join(" ", Parts().Select(p => p.Text));
                if (ModeChanged != null) ModeChanged(); Invalidate();
            }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Cursor = KeyArea().Contains(e.Location) ? Cursors.Hand : Cursors.Default; }
            protected override void OnMouseClick(MouseEventArgs e)
            { base.OnMouseClick(e); if (e.Button == MouseButtons.Left && KeyArea().Contains(e.Location)) { Focus(); CycleMode(); } }
            protected override bool IsInputKey(Keys keyData)
            { Keys key = keyData & Keys.KeyCode; return key == Keys.Enter || key == Keys.Space || base.IsInputKey(keyData); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { CycleMode(); e.Handled = e.SuppressKeyPress = true; }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); float scale = e.Graphics.DpiX / 96f;
                int inset = (int)(10 * scale), iconSize = (int)(18 * scale);
                var diff = Difference;
                var device = new Rectangle(inset, 0, Width * 22 / 100 - inset, Height);
                var action = new Rectangle(Width * 22 / 100 + iconSize + inset, 0, Width * 23 / 100 - iconSize - inset, Height);
                var keys = KeyArea();
                TextRenderer.DrawText(e.Graphics, DeviceName(diff), Font, device, EditorTheme.Muted, LabelFlags);
                Image icon = EditorArtwork.ActionIcon(diff.Action);
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(Width * 22 / 100, (Height - iconSize) / 2, iconSize, iconSize));
                TextRenderer.DrawText(e.Graphics, BindingViewText.ActionName(diff.Action), Font, action, EditorTheme.Ink, LabelFlags);
                if (keys.Width > 0 && keys.Height > 0)
                {
                    int left = keys.Left + (int)(8 * scale), gap = (int)(16 * scale);
                    if (Mode != 0)
                    {
                        Color color = ModeColors[Mode];
                        using (var pen = new Pen(color)) e.Graphics.DrawRectangle(pen, keys.X, keys.Y, keys.Width - 1, keys.Height - 1);
                        int labelWidth = Math.Min(keys.Width * 45 / 100, TextRenderer.MeasureText(e.Graphics, ModeName, Font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(14 * scale));
                        var label = new Rectangle(left - (int)(3 * scale), keys.Top + (int)(3 * scale), labelWidth, Math.Max(1, keys.Height - (int)(6 * scale)));
                        using (var brush = new SolidBrush(Color.FromArgb(65, color))) e.Graphics.FillRectangle(brush, label);
                        TextRenderer.DrawText(e.Graphics, ModeName, Font, label, color, Flags | TextFormatFlags.HorizontalCenter);
                        left = label.Right + gap;
                    }
                    var clip = e.Graphics.Save(); e.Graphics.SetClip(keys, System.Drawing.Drawing2D.CombineMode.Intersect);
                    using (var removedFont = new Font(Font, Font.Style | FontStyle.Strikeout))
                    foreach (var part in Parts())
                    {
                        int width = TextRenderer.MeasureText(e.Graphics, part.Text, part.Removed ? removedFont : Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                        int available = keys.Right - left - (int)(5 * scale); if (available <= 0) break;
                        TextRenderer.DrawText(e.Graphics, part.Text, part.Removed ? removedFont : Font,
                            new Rectangle(left, keys.Top, Math.Min(width + 1, available), keys.Height), part.Color, Flags);
                        left += width + gap;
                    }
                    e.Graphics.Restore(clip);
                    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(keys, -2, -2));
                }
                using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, inset, Height - 1, Width - inset, Height - 1);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}
