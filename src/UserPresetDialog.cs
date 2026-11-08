using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class PresetPicker : EditorComboBox
    {
        public PresetPicker() { DrawMode = DrawMode.OwnerDrawVariable; }
        bool StartsGroup(int index)
        {
            var preset = index >= 0 && index < Items.Count ? Items[index] as Preset : null;
            var previous = index > 0 ? Items[index - 1] as Preset : null;
            return preset != null && (previous == null || previous.IsUser != preset.IsUser);
        }
        protected override void OnMeasureItem(MeasureItemEventArgs e)
        { e.ItemHeight = ItemHeight + (StartsGroup(e.Index) ? Font.Height + 8 : 0); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool group = StartsGroup(e.Index) && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Rectangle bounds = e.Bounds;
            if (group)
            {
                int height = Font.Height + 8;
                using (var brush = new SolidBrush(EditorTheme.Surface)) e.Graphics.FillRectangle(brush, new Rectangle(bounds.X, bounds.Y, bounds.Width, height));
                TextRenderer.DrawText(e.Graphics, I18n.Str(((Preset)Items[e.Index]).IsUser ? "preset.mine" : "preset.builtin"), Font,
                    new Rectangle(bounds.X + 9, bounds.Y, bounds.Width - 18, height), EditorTheme.Muted, TextFormatFlags.VerticalCenter);
                bounds.Y += height; bounds.Height -= height;
            }
            using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? EditorTheme.Hover : EditorTheme.Floating)) e.Graphics.FillRectangle(brush, bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(bounds.X + 16, bounds.Y, bounds.Width - 24, bounds.Height),
                EditorTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class PresetNameBox : TextBox
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, string text);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(Handle, 0x1501, new IntPtr(1), I18n.Str("preset.nameexample"));
        }
    }

    public sealed class UserPresetDialog : EditorWindow
    {
        sealed class ShareChoice
        {
            public string Name;
            public System.Collections.Generic.List<PresetScope> Scopes;
            public override string ToString() { return Name; }
        }
        readonly UserPresetStore store;
        readonly TextBox shareBox = new TextBox(), previewName = new PresetNameBox();
        readonly CheckedListBox list = new CheckedListBox();
        readonly PresetContentsGrid grid = new PresetContentsGrid();
        readonly Label emptyPreview = new Label(), sourceLabel = new Label(), contentsSummary = new Label();
        readonly Label status = new Label(), shareLength = new Label(), libraryCount = new Label();
        readonly Button delete = new EditorButton(), share = new EditorButton(), saveImport = new EditorButton();
        readonly Button rename = new EditorButton(), export = new EditorButton(), use = new EditorButton();
        readonly ComboBox shareScope = new EditorComboBox();
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000, ShowAlways = true };
        readonly Timer importTimer = new Timer { Interval = 250 };
        string shareCode, sharePayload;
        Preset displayed;
        bool refreshing, rendering, imported;
        public Preset AppliedPreset { get; private set; }

        public UserPresetDialog(UserPresetStore store)
        {
            this.store = store;
            Text = I18n.Str("preset.manage"); Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(1080, 660); MinimumSize = new Size(960, 600);
            StartPosition = FormStartPosition.CenterParent; BackColor = EditorTheme.Surface; ForeColor = EditorTheme.Ink;
            MinimizeBox = false; MaximizeBox = false;
            SuspendLayout();
            var root = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
                Padding = new Padding(16, 12, 16, 8), Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            Controls.Add(root);
            var library = Card(4); library.Name = "presetLibrarySection"; library.Margin = new Padding(0, 0, 18, 0);
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); library.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            library.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); library.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            library.Controls.Add(Heading("preset.layout.library"), 0, 0);
            libraryCount.Dock = DockStyle.Fill; libraryCount.ForeColor = EditorTheme.Muted; library.Controls.Add(libraryCount, 0, 1);
            list.Name = "presetLibrary"; list.Dock = DockStyle.Fill; list.Margin = new Padding(0, 6, 0, 12);
            list.BackColor = EditorTheme.Surface; list.ForeColor = EditorTheme.Ink; list.BorderStyle = BorderStyle.None;
            list.CheckOnClick = false; list.IntegralHeight = false;
            list.SelectedIndexChanged += delegate { if (!refreshing) SelectLibrary(); };
            list.ItemCheck += delegate { if (!refreshing && IsHandleCreated) BeginInvoke((Action)UpdateLibraryChecks); };
            library.Controls.Add(list, 0, 2);
            ConfigureAction(delete, "deleteSelected", "preset.delete", DeleteSelected); delete.Anchor = AnchorStyles.Left;
            library.Controls.Add(delete, 0, 3); root.Controls.Add(library, 0, 0);

            var detail = Card(7); detail.Name = "presetDetailSection"; detail.Margin = Padding.Empty;
            foreach (int height in new[] { 58, 48, 30, 46, 0, 46, 24 })
                detail.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            var importRow = Row(3); importRow.Name = "importShareSection"; importRow.Margin = new Padding(0, 0, 0, 12);
            importRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); importRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            importRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var importLabel = Label("preset.layout.import"); importLabel.AutoSize = true; importLabel.Margin = new Padding(0, 0, 12, 0);
            importRow.Controls.Add(importLabel, 0, 0);
            StyleInput(shareBox); shareBox.MaxLength = 0; shareBox.Name = "shareCodeInput";
            shareBox.AccessibleName = I18n.Str("preset.layout.importcue"); importRow.Controls.Add(InputHost(shareBox), 1, 0);
            var paste = new EditorButton(); ConfigureAction(paste, "pasteShare", "preset.flow.paste", delegate {
                Run(delegate { if (Clipboard.ContainsText()) shareBox.Text = Clipboard.GetText(); ParseImport(); });
            }); importRow.Controls.Add(paste, 2, 0); detail.Controls.Add(importRow, 0, 0);
            shareBox.TextChanged += delegate {
                if (rendering) return;
                importTimer.Stop(); imported = true; ClearLibrarySelection(); Display(null);
                if (!string.IsNullOrWhiteSpace(shareBox.Text)) importTimer.Start();
            };
            shareBox.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { ParseImport(); e.Handled = e.SuppressKeyPress = true; }
            };
            importTimer.Tick += delegate { ParseImport(); };
            var nameRow = Row(3); nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            StyleInput(previewName); previewName.Name = "previewPresetName"; previewName.MaxLength = 80;
            previewName.AccessibleName = I18n.Str("preset.layout.name"); previewName.Font = new Font(Font.FontFamily, 14f, FontStyle.Bold);
            nameRow.Controls.Add(InputHost(previewName), 0, 0);
            ConfigureAction(saveImport, "saveImport", "preset.layout.favorite", delegate { Run(delegate {
                var saved = store.Save(displayed, previewName.Text); imported = false; Reload(saved.Id); Message("preset.saved");
            }); }); nameRow.Controls.Add(saveImport, 1, 0);
            ConfigureAction(rename, "renamePreset", "preset.contents.rename", delegate { Run(delegate {
                var saved = store.Update(displayed.Id, displayed, previewName.Text); Reload(saved.Id); Message("preset.saved");
            }); }); nameRow.Controls.Add(rename, 2, 0);
            previewName.TextChanged += delegate { if (!rendering) UpdateShareName(); }; detail.Controls.Add(nameRow, 0, 1);
            sourceLabel.Dock = DockStyle.Fill; sourceLabel.ForeColor = EditorTheme.Muted; sourceLabel.AutoEllipsis = true;
            detail.Controls.Add(sourceLabel, 0, 2);
            var contentsRow = Row(3); contentsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            contentsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); contentsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var contentsTitle = Heading("preset.contents.title"); contentsTitle.AutoSize = true; contentsTitle.Margin = new Padding(0, 0, 12, 0);
            contentsRow.Controls.Add(contentsTitle, 0, 0); contentsSummary.Dock = DockStyle.Fill; contentsSummary.ForeColor = EditorTheme.Muted;
            contentsSummary.TextAlign = ContentAlignment.MiddleLeft; contentsRow.Controls.Add(contentsSummary, 1, 0);
            ConfigureAction(use, "usePreset", "preset.contents.load", delegate {
                if (displayed == null) return;
                AppliedPreset = displayed; DialogResult = DialogResult.OK; Close();
            }); contentsRow.Controls.Add(use, 2, 0); detail.Controls.Add(contentsRow, 0, 3);
            var preview = new EditorPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Name = "presetContents" };
            grid.Name = "presetContentsTable"; grid.Dock = DockStyle.Fill; preview.Controls.Add(grid);
            emptyPreview.Dock = DockStyle.Fill; emptyPreview.ForeColor = EditorTheme.Muted; emptyPreview.TextAlign = ContentAlignment.MiddleCenter;
            emptyPreview.BackColor = EditorTheme.Surface; emptyPreview.Text = I18n.Str("preset.layout.empty");
            preview.Controls.Add(emptyPreview); detail.Controls.Add(preview, 0, 4);
            var sharing = Row(4); sharing.Margin = new Padding(0, 8, 0, 0);
            sharing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); sharing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sharing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); sharing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var scopeLabel = Label("preset.sharescope"); scopeLabel.AutoSize = true; scopeLabel.Margin = new Padding(0, 0, 10, 0);
            sharing.Controls.Add(scopeLabel, 0, 0); shareScope.Name = "shareScope"; shareScope.Dock = DockStyle.Fill;
            shareScope.DropDownStyle = ComboBoxStyle.DropDownList; shareScope.Margin = new Padding(0, 4, 8, 4);
            shareScope.SelectedIndexChanged += delegate { if (!rendering) UpdateShare(); }; sharing.Controls.Add(shareScope, 1, 0);
            ConfigureAction(export, "exportPreset", "preset.layout.exportbutton", Export); sharing.Controls.Add(export, 2, 0);
            ConfigureAction(share, "copyShare", "preset.share", delegate { Run(delegate {
                if (shareCode != null) { Clipboard.SetText(shareCode); Message("preset.copied"); }
            }); }); sharing.Controls.Add(share, 3, 0); detail.Controls.Add(sharing, 0, 5);
            tips.SetToolTip(shareScope, I18n.Str("preset.sharehelp")); tips.SetToolTip(export, I18n.Str("preset.contents.export"));
            shareLength.Dock = DockStyle.Fill; shareLength.ForeColor = EditorTheme.Muted; shareLength.TextAlign = ContentAlignment.MiddleRight;
            detail.Controls.Add(shareLength, 0, 6); root.Controls.Add(detail, 1, 0);
            status.Dock = DockStyle.Fill; status.ForeColor = EditorTheme.Muted; status.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(status, 0, 1); root.SetColumnSpan(status, 2);
            captionClose.DialogResult = DialogResult.Cancel; CancelButton = captionClose;
            AutoScaleDimensions = new SizeF(96, 96); ResumeLayout(true);
            Reload(null); Shown += delegate { shareBox.Focus(); };
        }

        static EditorLayout Card(int rows)
        {
            var card = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return card;
        }
        static EditorLayout Row(int columns)
        {
            var row = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); return row;
        }
        Label Heading(string key)
        { var label = Label(key); label.ForeColor = EditorTheme.Ink; label.Font = new Font(Font, FontStyle.Bold); return label; }
        static Label Label(string key)
        { return new Label { Dock = DockStyle.Fill, Text = I18n.Str(key), ForeColor = EditorTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }; }
        static void ConfigureAction(Button button, string name, string key, Action action)
        {
            button.Name = name; button.Text = I18n.Str(key); button.Anchor = AnchorStyles.Right;
            button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(12, 5, 12, 5); button.Margin = new Padding(4, 2, 0, 2);
            button.Click += delegate { action(); };
        }
        static void StyleInput(TextBox input)
        { input.BackColor = EditorTheme.Floating; input.ForeColor = EditorTheme.Ink; input.BorderStyle = BorderStyle.FixedSingle; input.Margin = new Padding(0, 5, 8, 4); }
        static Control InputHost(TextBox input)
        {
            var host = new Panel { Dock = DockStyle.Fill, Margin = input.Margin, BackColor = input.BackColor };
            input.Margin = Padding.Empty; input.Anchor = AnchorStyles.Left | AnchorStyles.Right; host.Controls.Add(input);
            host.Layout += delegate { input.SetBounds(0, Math.Max(0, (host.ClientSize.Height - input.PreferredHeight) / 2), host.ClientSize.Width, input.PreferredHeight); };
            return host;
        }
        void Run(Action action)
        { try { action(); } catch (Exception ex) { status.Text = ex.Message; status.ForeColor = BindingStatus.Pending; } }
        void Message(string key) { status.Text = I18n.Str(key); status.ForeColor = EditorTheme.Muted; }
        void ClearLibrarySelection()
        { refreshing = true; try { list.ClearSelected(); } finally { refreshing = false; } }
        void ParseImport()
        {
            importTimer.Stop(); imported = true; ClearLibrarySelection(); Display(null);
            if (string.IsNullOrWhiteSpace(shareBox.Text)) return;
            Run(delegate { Display(UserPresetStore.ParseShare(shareBox.Text)); });
        }
        void SelectLibrary()
        { importTimer.Stop(); imported = false; UpdateLibraryChecks(); Display(list.SelectedItem as Preset); }
        void Reload(string selectedId)
        {
            var values = store.Load(); refreshing = true;
            try {
                list.Items.Clear(); foreach (var value in values) list.Items.Add(value, false);
                for (int index = 0; index < list.Items.Count; index++) if (((Preset)list.Items[index]).Id == selectedId) list.SelectedIndex = index;
                if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
            }
            finally { refreshing = false; }
            UpdateLibraryChecks(); SelectLibrary();
        }
        void UpdateLibraryChecks()
        {
            delete.Enabled = list.CheckedItems.Count > 0;
            delete.Text = I18n.Str("preset.delete") + (list.CheckedItems.Count > 0 ? " (" + list.CheckedItems.Count + ")" : "");
            libraryCount.Text = I18n.Str("preset.libraryhint", list.Items.Count);
        }
        void Display(Preset preset)
        {
            rendering = true;
            try {
                displayed = preset; status.Text = "";
                previewName.Text = preset == null ? "" : preset.Name; previewName.Enabled = preset != null;
                sourceLabel.Text = preset == null ? "" : I18n.Str(imported ? "preset.layout.imported" : "preset.layout.library") + " · " + UserPresetStore.ScopeText(preset);
                saveImport.Visible = imported; rename.Visible = !imported;
                shareScope.Enabled = preset != null; shareScope.Items.Clear(); shareCode = sharePayload = null;
                grid.ShowPreset(preset); contentsSummary.Text = preset == null ? "" : I18n.Str("preset.contents.summary", grid.BindingCount, grid.SettingCount);
                grid.Visible = preset != null; emptyPreview.Visible = preset == null;
                if (preset != null) {
                    if (preset.Scopes.Count > 1) shareScope.Items.Add(new ShareChoice { Name = I18n.Str("preset.alldevices"), Scopes = preset.Scopes });
                    foreach (var scope in preset.Scopes.Where(item => item.Family == null))
                        shareScope.Items.Add(new ShareChoice { Name = I18n.Str("preset.keyboardonly"), Scopes = new[] { scope }.ToList() });
                    var pads = preset.Scopes.Where(item => item.Family != null).ToList();
                    if (pads.Count > 1 && preset.Scopes.Any(item => item.Family == null))
                        shareScope.Items.Add(new ShareChoice { Name = I18n.Str("preset.controllersonly"), Scopes = pads });
                    foreach (var scope in pads) shareScope.Items.Add(new ShareChoice { Name = scope.Family, Scopes = new[] { scope }.ToList() });
                    shareScope.SelectedIndex = 0;
                }
            }
            finally { rendering = false; }
            UpdateShare();
        }
        void UpdateShare()
        {
            sharePayload = null;
            if (displayed != null && shareScope.SelectedIndex >= 0)
                Run(delegate { sharePayload = PresetShare.EncodePayload(UserPresetStore.SelectScopes(displayed, ((ShareChoice)shareScope.SelectedItem).Scopes)); });
            UpdateShareName();
        }
        void UpdateShareName()
        {
            shareCode = null; shareLength.Text = "";
            try { if (sharePayload != null) { shareCode = PresetShare.WithName(previewName.Text, sharePayload); shareLength.Text = I18n.Str("preset.sharelength", shareCode.Length); } }
            catch (InvalidDataException) { }
            share.Enabled = export.Enabled = shareCode != null;
            saveImport.Enabled = imported && displayed != null && !string.IsNullOrWhiteSpace(previewName.Text);
            rename.Enabled = !imported && displayed != null && !string.IsNullOrWhiteSpace(previewName.Text) && previewName.Text.Trim() != displayed.Name;
            use.Enabled = displayed != null;
        }
        void DeleteSelected()
        {
            Run(delegate {
                var values = list.CheckedItems.Cast<Preset>().ToArray(); if (values.Length == 0) return;
                if (EditorDialogs.Show(this, I18n.Str("preset.deleteconfirm", string.Join("\n", values.Select(value => value.Name))), I18n.Str("preset.delete"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                store.Delete(values); imported = false; Reload(null); Message("preset.deleted");
            });
        }
        void Export()
        {
            if (shareCode == null) return;
            Run(delegate {
                using (var picker = new SaveFileDialog { Title = I18n.Str("preset.contents.export"), Filter = "PBZKeyForge (*.txt)|*.txt", FileName = "PBZKeyForge-preset.txt", DefaultExt = "txt", AddExtension = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(picker.FileName, shareCode, new System.Text.UTF8Encoding(false)); Message("preset.contents.exported"); }
            });
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { importTimer.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
    }
}
