using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class RelationEditorDialog : EditorWindow
    {
        readonly EditorSession session;
        readonly string action;
        readonly bool pad;
        readonly int family;
        readonly Action refresh, actionView;
        readonly ListBox keys = new ListBox();
        readonly Label info = new Label();
        readonly Button add = new EditorButton(), change = new EditorButton(), remove = new EditorButton(), restore = new EditorButton();
        public RelationEditorDialog(EditorSession session, string action, bool pad, int family, Action refresh, Action actionView)
        {
            this.session = session; this.action = action; this.pad = pad; this.family = family; this.refresh = refresh; this.actionView = actionView;
            Text = BindingViewText.ActionName(action); Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(650, 420); StartPosition = FormStartPosition.CenterParent; AllowResize = false;
            BackColor = EditorTheme.Background; ForeColor = EditorTheme.Ink;
            info.SetBounds(20, Caption.Height + 14, 610, 94); info.ForeColor = EditorTheme.Muted;
            keys.SetBounds(20, Caption.Height + 114, 610, 145); keys.BackColor = EditorTheme.Floating; keys.ForeColor = EditorTheme.Ink;
            keys.DisplayMember = "Label"; keys.BorderStyle = BorderStyle.FixedSingle;
            var buttons = new[] { add, change, remove };
            string[] text = { "views.add", "views.change", "views.remove" };
            for (int i = 0; i < buttons.Length; i++)
            { buttons[i].SetBounds(20 + i * 206, Caption.Height + 271, 198, 34); buttons[i].Text = I18n.Str(text[i]); }
            add.Click += delegate { Pick(false); }; change.Click += delegate { Pick(true); };
            remove.Click += delegate { if (keys.SelectedItem != null) Run(delegate { session.ChangeRelationship(action, (string)keys.SelectedItem, false, pad, family); }); };
            restore.SetBounds(20, Caption.Height + 319, 280, 36); restore.Text = I18n.Str("views.restore");
            restore.Click += delegate { Run(delegate { session.RestoreAction(action, pad, family); }); };
            var details = new EditorButton { Left = 310, Top = restore.Top, Width = 190, Height = 36, Text = I18n.Str("views.actionview") };
            details.Click += delegate { actionView(); Close(); };
            var close = new EditorButton { Left = 510, Top = restore.Top, Width = 120, Height = 36, Text = I18n.Str("editor.close"), DialogResult = DialogResult.Cancel };
            keys.Format += delegate(object sender, ListControlConvertEventArgs e) { e.Value = BindingButton.FriendlyKey(e.ListItem as string); };
            keys.FormattingEnabled = true; keys.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.AddRange(new Control[] { info, keys, add, change, remove, restore, details, close }); CancelButton = close;
            Render(); AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
        void UpdateButtons()
        {
            var relation = session.Relations(action, pad, family);
            string key = keys.SelectedItem as string;
            add.Enabled = !session.IsResetPending && session.RelationshipCapacity(action, pad, family).CanAdd;
            change.Enabled = remove.Enabled = !session.IsResetPending && key != null
                && !session.Defaults.FixedForAction(action, pad).Contains(key)
                && (relation.Complete || relation.DefaultKeys != null && !relation.DefaultKeys.Contains(key));
        }
        void Render()
        {
            var relation = session.Relations(action, pad, family);
            var capacity = session.RelationshipCapacity(action, pad, family);
            info.Text = I18n.Str("views.factory", relation.DefaultKeys == null ? I18n.Str("editor.defaultunknown") : BindingViewText.Keys(relation.DefaultKeys))
                + "\n" + I18n.Str("views.bindingcount", capacity.Used, capacity.Limit)
                + "\n" + (!relation.Complete ? I18n.Str("views.incomplete") : capacity.CanAdd ? I18n.Str("views.bindinglimit", capacity.Limit)
                    : I18n.Str("views.bindingfull", BindingViewText.ActionName(action), capacity.Limit));
            keys.Items.Clear(); keys.Items.AddRange(relation.Keys.Cast<object>().ToArray()); if (keys.Items.Count > 0) keys.SelectedIndex = 0;
            restore.Enabled = !session.IsResetPending && (!pad || relation.DefaultKeys != null); UpdateButtons();
        }
        void Run(Action edit)
        {
            try { edit(); refresh(); Render(); }
            catch (Exception ex) { EditorDialogs.Show(this, ex.Message, Text, MessageBoxButtons.OK); }
        }
        void Pick(bool replace)
        {
            string old = keys.SelectedItem as string;
            using (var dialog = new BindingDialog(Text, pad, old, null, false))
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.Result) && dialog.Result != "None")
                    Run(delegate { if (replace) session.ReplaceRelationship(action, old, dialog.Result, pad, family);
                        else session.ChangeRelationship(action, dialog.Result, true, pad, family); });
        }
    }

    internal sealed class BindingRecordsDialog : EditorWindow
    {
        readonly ListView actions = new RecordList(), rows = new RecordList();
        public BindingRecordsDialog(EditorSession session, bool pad, int family, Action refresh)
        {
            Text = I18n.Str("views.records"); Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1000, 650); StartPosition = FormStartPosition.CenterParent; AllowResize = false;
            MinimizeBox = false; MaximizeBox = false;
            BackColor = EditorTheme.Background; ForeColor = EditorTheme.Ink;
            var help = new Label { Left = 20, Top = Caption.Height + 14, Width = 910, Height = 58,
                Text = I18n.Str(session.IsOffline ? "offline.recordshelp" : "help.summary"), ForeColor = EditorTheme.Muted };
            var question = new BindingHelpButton("help.slots") { Left = 950, Top = help.Top, Height = 32 };
            actions.SetBounds(20, Caption.Height + 78, 960, 180);
            actions.Columns.Add(I18n.Str("col.action"), 220);
            actions.Columns.Add(I18n.Str("views.effectivekeys"), 310);
            actions.Columns.Add(I18n.Str("views.factory", "").TrimEnd(' ', ':', '：'), 260);
            actions.Columns.Add(I18n.Str("views.recordstatus"), 160);
            var entries = new Label { Left = 20, Top = Caption.Height + 274, Width = 960, Height = 28,
                Text = I18n.Str("help.entries"), ForeColor = EditorTheme.Muted };
            rows.SetBounds(20, Caption.Height + 310, 960, 180);
            rows.Columns.Add(I18n.Str("views.recordkey"), 320);
            rows.Columns.Add(I18n.Str("views.factory", "").TrimEnd(' ', ':', '：'), 320);
            rows.Columns.Add(I18n.Str("views.recordstatus"), 300);
            actions.HandleCreated += delegate { EditorArtwork.DarkScrollbars(actions); };
            rows.HandleCreated += delegate { EditorArtwork.DarkScrollbars(rows); };
            var detail = new Label { Left = 20, Top = Caption.Height + 500, Width = 960, Height = 48, ForeColor = EditorTheme.Muted };
            var edit = new EditorButton { Left = 20, Top = Caption.Height + 558, Width = 220, Height = 36,
                Text = I18n.Str("views.change"), Enabled = false };
            Action selection = delegate
            {
                var record = rows.SelectedItems.Count == 0 ? null : rows.SelectedItems[0].Tag as BindingRecord;
                edit.Enabled = record != null && record.Id != null && !record.ReadOnly && !session.IsResetPending;
                detail.Text = record != null && record.ReadOnly ? I18n.Str("views.readonlyrecord") : record == null || record.Id == null ? I18n.Str("help.inherited") : I18n.Str("views.recordid", record.Slot);
            };
            Action renderRows = delegate
            {
                string action = actions.SelectedItems.Count == 0 ? null : actions.SelectedItems[0].Tag as string;
                rows.BeginUpdate();
                try
                {
                    rows.Items.Clear();
                    if (action != null)
                    {
                        var records = session.BindingRecords(pad, family).Where(r => r.Action == action).ToList();
                        foreach (int raw in session.Defaults.RegisteredSlots(action, pad))
                            if (!records.Any(r => pad ? InputDefaults.RawSlot(r.Slot) == raw : r.Slot == raw.ToString()))
                            {
                                string factory = session.Defaults.ForRawSlot(action, pad, raw);
                                records.Add(new BindingRecord { Action = action, Slot = raw.ToString(), Key = factory, DefaultKey = factory });
                            }
                        foreach (string key in session.Defaults.FixedForAction(action, pad))
                            records.Add(new BindingRecord { Action = action, Slot = "", Key = key, DefaultKey = key });
                        foreach (var record in records.OrderBy(r => r.Slot))
                        {
                            string status = I18n.Str(record.ReadOnly ? "views.readonlyrecord" : record.Id == null ? "help.inheritshort" : record.Changed ? session.IsOffline ? "offline.pending" : "views.pending" : record.DefaultKey == null ? "editor.defaultunknown"
                                : record.Key == record.DefaultKey ? "views.original" : "views.savedcustom");
                            var item = new ListViewItem(new[] { BindingButton.FriendlyKey(record.Key),
                                record.DefaultKey == null ? I18n.Str("editor.defaultunknown") : BindingButton.FriendlyKey(record.DefaultKey), status }) { Tag = record };
                            rows.Items.Add(item);
                        }
                    }
                    if (rows.Items.Count > 0) rows.Items[0].Selected = true;
                }
                finally { rows.EndUpdate(); }
                selection();
            };
            Action render = delegate
            {
                string selectedAction = actions.SelectedItems.Count == 0 ? null : actions.SelectedItems[0].Tag as string;
                actions.BeginUpdate();
                try
                {
                    actions.Items.Clear();
                    foreach (string action in session.BindingActions(pad, family))
                    {
                        var relation = session.Relations(action, pad, family);
                        bool custom = relation.DefaultKeys != null && !relation.Keys.OrderBy(k => k).SequenceEqual(relation.DefaultKeys.OrderBy(k => k));
                        var item = new ListViewItem(new[] { BindingViewText.ActionName(action), BindingViewText.Keys(relation.Keys),
                            relation.DefaultKeys == null ? I18n.Str("editor.defaultunknown") : BindingViewText.Keys(relation.DefaultKeys),
                            I18n.Str(relation.Changed ? session.IsOffline ? "offline.pending" : "views.pending" : custom ? "views.savedcustom" : "views.original") }) { Tag = action };
                        actions.Items.Add(item); if (action == selectedAction) item.Selected = true;
                    }
                    if (actions.SelectedItems.Count == 0 && actions.Items.Count > 0) actions.Items[0].Selected = true;
                }
                finally { actions.EndUpdate(); }
                renderRows();
            };
            actions.SelectedIndexChanged += delegate { renderRows(); };
            rows.SelectedIndexChanged += delegate { selection(); };
            Action editSelected = delegate
            {
                if (rows.SelectedItems.Count == 0 || session.IsResetPending) return;
                var record = (BindingRecord)rows.SelectedItems[0].Tag;
                if (record.Id == null || record.ReadOnly) return;
                using (var dialog = new BindingDialog(BindingViewText.ActionName(record.Action), pad, record.Key,
                    record.DefaultKey == null ? null : new[] { record.DefaultKey }, pad && record.DefaultKey != null && record.Key == record.DefaultKey))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    { session.SetBindingRecord(record.Id, dialog.Result); refresh(); render(); }
            };
            edit.Click += delegate { editSelected(); }; rows.DoubleClick += delegate { editSelected(); };
            var close = new EditorButton { Left = 840, Top = edit.Top, Width = 140, Height = 36, Text = I18n.Str("editor.close"), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { help, question, actions, entries, rows, detail, edit, close }); CancelButton = close;
            render(); AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
        sealed class RecordList : ListView
        {
            public RecordList()
            {
                DoubleBuffered = true; OwnerDraw = true; View = View.Details; FullRowSelect = true;
                MultiSelect = false; HideSelection = false; BorderStyle = BorderStyle.None;
                BackColor = EditorTheme.Surface; ForeColor = EditorTheme.Ink;
            }
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e); if (Columns.Count == 0) return;
                int total = ClientSize.Width, left = total;
                for (int i = 0; i < Columns.Count; i++)
                { int width = i == Columns.Count - 1 ? left : total / Columns.Count; Columns[i].Width = width; left -= width; }
            }
            protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
            {
                using (var brush = new SolidBrush(EditorTheme.Floating)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, Rectangle.Inflate(e.Bounds, -8, 0), EditorTheme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
            protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
            {
                bool selected = e.Item.Selected;
                using (var brush = new SolidBrush(selected ? EditorTheme.Hover : e.ItemIndex % 2 == 0 ? EditorTheme.Surface : EditorTheme.RowAlternate))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                Color color = EditorTheme.Ink;
                if (e.ColumnIndex == Columns.Count - 1)
                    color = e.SubItem.Text == I18n.Str("views.pending") || e.SubItem.Text == I18n.Str("offline.pending") ? BindingStatus.Pending
                        : e.SubItem.Text == I18n.Str("views.savedcustom") ? BindingStatus.Custom : BindingStatus.Factory;
                else if (e.ColumnIndex == Columns.Count - 2) color = EditorTheme.Muted;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, Rectangle.Inflate(e.Bounds, -8, 0), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }
    }
}
