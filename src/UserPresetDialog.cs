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
        readonly EditorSession session;
        readonly UserPresetStore store;
        readonly EditorPreferences preferences;
        readonly TextBox nameBox = new PresetNameBox(), shareBox = new TextBox(), previewName = new TextBox();
        readonly CheckedListBox list = new CheckedListBox();
        readonly Panel preview = new EditorPanel();
        readonly Label status = new Label(), shareLength = new Label();
        readonly Button delete = new EditorButton(), share = new EditorButton(), saveImport = new EditorButton(), use = new EditorButton();
        readonly PresetModeSelector modeButton = new PresetModeSelector();
        readonly Label modeHelp = new Label(), libraryCount = new Label(), nameHint = new Label();
        readonly ComboBox shareScope = new EditorComboBox();
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000, ShowAlways = true };
        string shareCode;
        Preset displayed;
        bool refreshing, rendering;
        public PresetApplyMode ChosenMode { get; private set; }
        public Preset AppliedPreset { get; private set; }

        public UserPresetDialog(EditorSession session, UserPresetStore store, PresetApplyMode mode)
            : this(session, store, mode, EditorPreferences.Default()) { }

        public UserPresetDialog(EditorSession session, UserPresetStore store, PresetApplyMode mode, EditorPreferences preferences)
        {
            this.preferences = preferences;
            this.session = session; this.store = store; ChosenMode = mode;
            Text = I18n.Str("preset.manage"); Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(960, 800); MinimumSize = new Size(850, 740);
            StartPosition = FormStartPosition.CenterParent; BackColor = EditorTheme.Surface; ForeColor = EditorTheme.Ink;
            MinimizeBox = false; MaximizeBox = false;
            var root = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20, 12, 20, 12), Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 140, 0, 94, 48 }) root.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            Controls.Add(root);

            var currentCard = Card(4); currentCard.Margin = new Padding(0, 0, 0, 12);
            foreach (int height in new[] { 28, 26, 40, 24 }) currentCard.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            currentCard.Controls.Add(Heading("preset.currenttitle"), 0, 0);
            currentCard.Controls.Add(Label("preset.currenthelp"), 0, 1);
            var current = CreateRow(4);
            current.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            current.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148)); current.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            current.Controls.Add(Label("preset.name"), 0, 0); StyleInput(nameBox); nameBox.MaxLength = 80; nameBox.Name = "currentPresetName";
            nameBox.AccessibleName = I18n.Str("preset.name"); current.Controls.Add(NameEntry(), 1, 0);
            var save = Button("preset.save", delegate {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    nameHint.Text = I18n.Str("preset.entername"); nameHint.ForeColor = BindingStatus.Pending;
                    nameBox.Focus(); nameBox.Parent.Invalidate(); return;
                }
                Run(delegate {
                    var preset = store.Save(UserPresetStore.Capture(session, nameBox.Text), nameBox.Text);
                    Reload(preset.Id); Message("preset.saved");
                });
            });
            save.Name = "saveCurrent"; save.ForeColor = EditorTheme.Muted;
            tips.SetToolTip(save, I18n.Str("preset.entername"));
            nameBox.TextChanged += delegate {
                bool ready = !string.IsNullOrWhiteSpace(nameBox.Text);
                ((EditorButton)save).Primary = ready; save.ForeColor = ready ? EditorTheme.Ink : EditorTheme.Muted;
                nameHint.Text = ""; tips.SetToolTip(save, ready ? null : I18n.Str("preset.entername"));
                save.Invalidate(); nameBox.Parent.Invalidate();
            };
            nameBox.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { save.PerformClick(); e.Handled = e.SuppressKeyPress = true; }
            };
            Shown += delegate { nameBox.Focus(); };
            current.Controls.Add(save, 2, 0);
            var export = Button("preset.export", delegate { Export(); }); export.Name = "exportGame"; current.Controls.Add(export, 3, 0);
            nameHint.Dock = DockStyle.Fill; nameHint.ForeColor = EditorTheme.Muted; nameHint.TextAlign = ContentAlignment.MiddleLeft;
            nameHint.Margin = new Padding(88, 0, 0, 0);
            currentCard.Controls.Add(current, 0, 2); currentCard.Controls.Add(nameHint, 0, 3); root.Controls.Add(currentCard, 0, 0);

            var content = CreateRow(2); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 71));
            var library = Card(4); library.Margin = new Padding(0, 0, 12, 12);
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); library.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            library.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); library.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            library.Controls.Add(Heading("preset.mine"), 0, 0);
            libraryCount.Dock = DockStyle.Fill; libraryCount.ForeColor = EditorTheme.Muted; library.Controls.Add(libraryCount, 0, 1);
            list.Dock = DockStyle.Fill; list.Margin = new Padding(0, 4, 0, 8); list.BackColor = EditorTheme.Floating; list.ForeColor = EditorTheme.Ink; list.BorderStyle = BorderStyle.None;
            list.CheckOnClick = false; list.IntegralHeight = false; list.SelectedIndexChanged += delegate { if (!refreshing) SelectLibrary(); };
            list.ItemCheck += delegate { if (!refreshing) BeginInvoke((Action)UpdateLibraryChecks); };
            library.Controls.Add(list, 0, 2);
            delete.Dock = DockStyle.Fill; delete.Margin = Padding.Empty; delete.Name = "deleteSelected";
            delete.Click += delegate { DeleteSelected(); }; library.Controls.Add(delete, 0, 3); content.Controls.Add(library, 0, 0);

            var detail = Card(7); detail.Margin = new Padding(0, 0, 0, 12);
            foreach (int height in new[] { 28, 36, 0, 40, 28, 44, 36 })
                detail.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            detail.Controls.Add(Heading("preset.detailtitle"), 0, 0);
            var detailName = CreateRow(2); detailName.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); detailName.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detailName.Controls.Add(Label("preset.name"), 0, 0); StyleInput(previewName); previewName.MaxLength = 80; previewName.AccessibleName = I18n.Str("preset.name");
            detailName.Controls.Add(InputHost(previewName), 1, 0); detail.Controls.Add(detailName, 0, 1);
            previewName.TextChanged += delegate { if (!rendering) UpdateShare(); };
            preview.Dock = DockStyle.Fill; preview.AutoScroll = true; preview.BackColor = EditorTheme.Background; preview.Margin = new Padding(0, 6, 0, 6); detail.Controls.Add(preview, 0, 2);
            preview.Layout += delegate { LayoutPreview(); };
            preview.HandleCreated += delegate { EditorArtwork.DarkScrollbars(preview); };
            var sharing = CreateRow(4);
            sharing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); sharing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sharing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118)); sharing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            sharing.Controls.Add(Label("preset.sharescope"), 0, 0);
            shareScope.Dock = DockStyle.Fill; shareScope.DropDownStyle = ComboBoxStyle.DropDownList; shareScope.Margin = new Padding(0, 5, 8, 4);
            shareScope.Name = "shareScope"; shareScope.AccessibleName = I18n.Str("preset.sharescope"); tips.SetToolTip(shareScope, I18n.Str("preset.sharehelp"));
            shareScope.SelectedIndexChanged += delegate { if (!rendering) UpdateShare(); }; sharing.Controls.Add(shareScope, 1, 0);
            share.Text = I18n.Str("preset.share"); share.Dock = DockStyle.Fill; share.Margin = new Padding(0, 3, 8, 3); share.Name = "copyShare";
            share.Click += delegate { Run(delegate { if (shareCode != null) { Clipboard.SetText(shareCode); Message("preset.copied"); } }); };
            sharing.Controls.Add(share, 2, 0); shareLength.Dock = DockStyle.Fill; shareLength.TextAlign = ContentAlignment.MiddleRight;
            shareLength.ForeColor = EditorTheme.Muted; sharing.Controls.Add(shareLength, 3, 0); detail.Controls.Add(sharing, 0, 3);
            var modeRow = CreateRow(2); modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var modeHeading = Label("preset.mode"); modeHeading.Font = new Font(Font.FontFamily, 9f);
            modeRow.Controls.Add(modeHeading, 0, 0); modeButton.Dock = DockStyle.None; modeButton.Anchor = AnchorStyles.Left;
            modeButton.AutoSize = true; modeButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modeButton.Font = new Font(Font.FontFamily, 9f); modeButton.Margin = Padding.Empty; modeButton.Name = "presetMode";
            modeButton.ModeChanged += delegate { Run(delegate {
                ChosenMode = modeButton.Mode; UpdateMode(); preferences.SavePresetMode(ChosenMode);
            }); };
            modeRow.Controls.Add(modeButton, 1, 0); detail.Controls.Add(modeRow, 0, 4);
            modeHelp.Dock = DockStyle.Fill; modeHelp.ForeColor = EditorTheme.Muted; modeHelp.TextAlign = ContentAlignment.MiddleLeft; modeHelp.Margin = new Padding(0, 4, 0, 4);
            detail.Controls.Add(modeHelp, 0, 5);
            var actions = CreateRow(2); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 212));
            use.Dock = DockStyle.Fill; use.Margin = Padding.Empty; use.Name = "usePreset"; ((EditorButton)use).Primary = true;
            use.Click += delegate { Run(delegate {
                var value = displayed;
                if (value.Id == null)
                {
                    session.CheckPreset(value, ChosenMode);
                    value = store.Save(value, previewName.Text);
                }
                session.StagePreset(value, ChosenMode); AppliedPreset = value; DialogResult = DialogResult.OK; Close();
            }); };
            actions.Controls.Add(use, 1, 0); detail.Controls.Add(actions, 0, 6);
            content.Controls.Add(detail, 1, 0); root.Controls.Add(content, 0, 1);

            var importCard = Card(3); importCard.Margin = Padding.Empty;
            importCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); importCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            importCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            importCard.Controls.Add(Heading("preset.importtitle"), 0, 0);
            var import = CreateRow(3); import.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); import.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84)); import.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            StyleInput(shareBox); shareBox.Margin = new Padding(0, 5, 8, 4); shareBox.AccessibleName = I18n.Str("preset.paste"); import.Controls.Add(InputHost(shareBox), 0, 0);
            var inspect = Button("preset.preview", delegate { Run(delegate { Display(UserPresetStore.ParseShare(shareBox.Text)); Message("preset.importhelp"); }); }); inspect.Name = "previewShare";
            import.Controls.Add(inspect, 1, 0);
            saveImport.Text = I18n.Str("preset.saveimport"); saveImport.Dock = DockStyle.Fill; saveImport.Margin = new Padding(8, 2, 0, 2); saveImport.Name = "saveImport";
            saveImport.Click += delegate { Run(delegate { var value = store.Save(displayed, previewName.Text); Reload(value.Id); Message("preset.saved"); }); };
            import.Controls.Add(saveImport, 2, 0); importCard.Controls.Add(import, 0, 1); root.Controls.Add(importCard, 0, 2);
            var footer = CreateRow(2); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            status.Dock = DockStyle.Fill; status.ForeColor = EditorTheme.Muted; status.Text = I18n.Str("preset.emptyhelp"); status.TextAlign = ContentAlignment.MiddleLeft; status.Margin = new Padding(0, 4, 12, 0);
            footer.Controls.Add(status, 0, 0);
            var close = Button("editor.close", delegate { Close(); }); close.Margin = new Padding(0, 6, 0, 2); close.DialogResult = DialogResult.Cancel; CancelButton = close;
            footer.Controls.Add(close, 1, 0); root.Controls.Add(footer, 0, 3);
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale(); Reload(null); UpdateMode();
        }

        EditorLayout Card(int rows)
        {
            var card = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Padding = new Padding(12, 8, 12, 8), BackColor = EditorTheme.Floating };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
            return card;
        }
        Label Heading(string key)
        { var label = Label(key); label.ForeColor = EditorTheme.Ink; label.Font = new Font(Font, FontStyle.Bold); return label; }
        static EditorLayout CreateRow(int columns)
        {
            var layout = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); return layout;
        }
        Label Label(string key)
        { return new Label { Dock = DockStyle.Fill, Text = I18n.Str(key), ForeColor = EditorTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }; }
        Control NameEntry()
        {
            var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 2), BackColor = EditorTheme.Background };
            nameBox.Dock = DockStyle.None; nameBox.BorderStyle = BorderStyle.None;
            nameBox.BackColor = host.BackColor; nameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            host.Controls.Add(nameBox);
            host.Layout += delegate {
                int inset = Math.Max(8, Font.Height / 2);
                nameBox.SetBounds(inset, Math.Max(0, (host.ClientSize.Height - nameBox.PreferredHeight) / 2), Math.Max(0, host.ClientSize.Width - inset * 2), nameBox.PreferredHeight);
            };
            host.Paint += delegate(object sender, PaintEventArgs e) {
                bool highlight = nameBox.Focused || string.IsNullOrWhiteSpace(nameBox.Text);
                using (var pen = new Pen(highlight ? EditorTheme.Accent : EditorTheme.Line, highlight ? 1.5f * e.Graphics.DpiX / 96f : 1))
                    e.Graphics.DrawRectangle(pen, 1, 1, Math.Max(0, host.Width - 3), Math.Max(0, host.Height - 3));
            };
            host.Click += delegate { nameBox.Focus(); };
            nameBox.GotFocus += delegate { host.Invalidate(); }; nameBox.LostFocus += delegate { host.Invalidate(); };
            return host;
        }
        static Control InputHost(TextBox input)
        {
            var host = new Panel { Dock = DockStyle.Fill, Margin = input.Margin, BackColor = EditorTheme.Floating };
            input.Dock = DockStyle.None; input.Margin = Padding.Empty; input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            host.Controls.Add(input);
            host.Layout += delegate { input.SetBounds(0, Math.Max(0, (host.ClientSize.Height - input.PreferredHeight) / 2), host.ClientSize.Width, input.PreferredHeight); };
            return host;
        }
        static void StyleInput(TextBox input)
        { input.Dock = DockStyle.Fill; input.BackColor = EditorTheme.Floating; input.ForeColor = EditorTheme.Ink; input.BorderStyle = BorderStyle.FixedSingle; input.Margin = new Padding(0, 5, 8, 4); }
        Button Button(string key, Action action)
        {
            var button = new EditorButton { Text = I18n.Str(key), Dock = DockStyle.Fill, Height = 36, Margin = new Padding(0, 2, 8, 2) };
            button.Click += delegate { action(); }; return button;
        }
        void Run(Action action)
        { try { action(); } catch (Exception ex) { status.Text = ex.Message; status.ForeColor = BindingStatus.Pending; } }
        void Message(string key) { status.Text = I18n.Str(key); status.ForeColor = EditorTheme.Muted; }
        void UpdateMode()
        {
            string[] keys = { "preset.append", "preset.replace", "preset.only" };
            modeButton.Mode = ChosenMode; modeButton.RefreshLabels();
            modeHelp.Text = I18n.Str(ChosenMode == PresetApplyMode.OnlyPreset ? "preset.only.scopehelp" : keys[(int)ChosenMode] + ".help");
        }

        Preset SelectedShare()
        {
            if (displayed == null || shareScope.SelectedIndex < 0) return null;
            var value = UserPresetStore.SelectScopes(displayed, ((ShareChoice)shareScope.SelectedItem).Scopes);
            value.UserName = previewName.Text; return value;
        }

        void UpdateShare()
        {
            shareCode = null; shareLength.Text = "";
            try
            {
                var value = SelectedShare();
                if (value != null) { shareCode = UserPresetStore.Share(value); shareLength.Text = I18n.Str("preset.sharelength", shareCode.Length); }
            }
            catch (InvalidDataException) { }
            share.Enabled = shareCode != null;
            var row = (TableLayoutPanel)shareLength.Parent;
            row.ColumnStyles[0].Width = TextRenderer.MeasureText(I18n.Str("preset.sharescope"), Font).Width + 8;
            row.ColumnStyles[2].Width = TextRenderer.MeasureText(share.Text, Font).Width + 24;
            row.ColumnStyles[3].Width = TextRenderer.MeasureText(I18n.Str("preset.sharelength", 9999), Font).Width + 12;
        }
        void Reload(string selectedId)
        {
            refreshing = true;
            try
            {
                list.Items.Clear();
                foreach (var preset in store.Load()) list.Items.Add(preset, preset.Id == selectedId);
                for (int index = 0; index < list.Items.Count; index++) if (((Preset)list.Items[index]).Id == selectedId) list.SelectedIndex = index;
                if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
            }
            finally { refreshing = false; }
            SelectLibrary();
        }
        void SelectLibrary()
        {
            UpdateLibraryChecks();
            Display(list.SelectedItem as Preset);
        }
        void UpdateLibraryChecks()
        {
            delete.Enabled = list.CheckedItems.Count > 0;
            delete.Text = I18n.Str("preset.delete") + (list.CheckedItems.Count > 0 ? " (" + list.CheckedItems.Count + ")" : "");
            libraryCount.Text = I18n.Str("preset.libraryhint", list.Items.Count);
        }
        void Display(Preset preset)
        {
            if (rendering) return;
            rendering = true;
            try
            {
                displayed = preset;
                if (preset != null && preset.Id == null)
                {
                    refreshing = true;
                    try { list.SelectedIndex = -1; } finally { refreshing = false; }
                }
                foreach (Control control in preview.Controls.Cast<Control>().ToArray()) control.Dispose();
                previewName.Text = preset == null ? "" : preset.Name; previewName.ReadOnly = preset == null || preset.Id != null;
                share.Enabled = use.Enabled = modeButton.Enabled = shareScope.Enabled = preset != null;
                saveImport.Visible = preset != null && preset.Id == null; saveImport.Enabled = saveImport.Visible;
                shareScope.Items.Clear(); shareCode = null; shareLength.Text = "";
                if (preset != null)
                {
                    if (preset.Scopes.Count > 1)
                        shareScope.Items.Add(new ShareChoice { Name = I18n.Str("preset.alldevices"), Scopes = preset.Scopes });
                    foreach (var scope in preset.Scopes.Where(scope => scope.Family == null))
                        shareScope.Items.Add(new ShareChoice { Name = I18n.Str("preset.keyboardonly"), Scopes = new[] { scope }.ToList() });
                    var pads = preset.Scopes.Where(scope => scope.Family != null).ToList();
                    if (pads.Count > 1 && preset.Scopes.Any(scope => scope.Family == null))
                        shareScope.Items.Add(new ShareChoice { Name = I18n.Str("preset.controllersonly"), Scopes = pads });
                    foreach (var scope in pads) shareScope.Items.Add(new ShareChoice { Name = scope.Family, Scopes = new[] { scope }.ToList() });
                    shareScope.SelectedIndex = 0;
                }
                use.Text = I18n.Str(preset != null && preset.Id == null ? "preset.saveapply" : "preset.stage");
                if (preset == null)
                {
                    preview.Controls.Add(new Label { Text = I18n.Str("preset.emptyhelp"), AutoSize = true, ForeColor = EditorTheme.Muted });
                    return;
                }
                var snapshot = session.PreviewPreset(preset);
                int top = 8;
                var scopeLabel = new Label { Text = I18n.Str("preset.devices", UserPresetStore.ScopeText(preset)),
                    AutoSize = true, ForeColor = EditorTheme.Muted };
                preview.Controls.Add(scopeLabel); top += scopeLabel.Height + 12;
                var title = new Label { Text = I18n.Str("preset.previewchanges"), Left = 10, Top = top, Width = Math.Max(100, preview.ClientSize.Width - 28), ForeColor = BindingStatus.Custom, AutoSize = true };
                preview.Controls.Add(title); top += title.Height + 12;
                foreach (var difference in snapshot.SchemeDifferences(true))
                {
                    var label = new Label { Text = I18n.Str(difference.Gamepad ? "editor.gamepad" : "editor.keyboard")
                        + (difference.Gamepad ? " · " + difference.Family : "") + " · " + BindingViewText.ActionName(difference.Action)
                        + "\n" + BindingViewText.Keys(difference.BeforeKeys) + " → " + BindingViewText.Keys(difference.AfterKeys),
                        Left = 10, Top = top, ForeColor = BindingStatus.Custom, AutoSize = true, MaximumSize = new Size(Math.Max(120, preview.ClientSize.Width - 32), 0) };
                    preview.Controls.Add(label); top += label.Height + 12;
                }
                if (snapshot.SchemeDifferences(true).Length == 0)
                    preview.Controls.Add(new Label { Text = I18n.Str("views.nofactorydiff"), Left = 10, Top = top, AutoSize = true, ForeColor = BindingStatus.Factory });
                bool pad = !preset.Scopes.Any(scope => scope.Family == null)
                    || preset.UserMappings.Any(mapping => mapping.Family != null) && !preset.UserMappings.Any(mapping => mapping.Family == null);
                var family = snapshot.ControllerFamilies.FirstOrDefault(item => pad && preset.Scopes.Any(scope => scope.Family == item.Name)
                    && preset.UserMappings.Any(mapping => mapping.Family == item.Name))
                    ?? snapshot.ControllerFamilies.FirstOrDefault(item => pad && preset.Scopes.Any(scope => scope.Family == item.Name))
                    ?? snapshot.ControllerFamilies.FirstOrDefault();
                preview.Controls.Add(new Label { Text = I18n.Str(pad ? "editor.gamepad" : "editor.keyboard") + (pad && family != null ? " · " + family.Name : ""),
                    AutoSize = true, ForeColor = BindingStatus.Factory });
                int familyIndex = family == null ? -1 : family.Index;
                var relations = snapshot.BindingActions(pad, familyIndex).Select(action => BindingViewText.PhysicalRelations(snapshot.Relations(action, pad, familyIndex))).ToArray();
                var keys = relations.SelectMany(relation => relation.Keys.Concat(relation.DefaultKeys ?? new string[0])).Distinct().ToArray();
                Func<string, KeyBadgeState> state = key => new KeyBadgeState { Count = relations.Count(relation => relation.Keys.Contains(key)),
                    Complete = relations.All(relation => relation.KnowsKey(key)),
                    Differs = relations.Any(relation => relation.DefaultKeys != null && relation.Keys.Contains(key) != relation.DefaultKeys.Contains(key)) };
                using (var diagram = new DeviceDiagram(pad, family == null ? null : family.Name, state, delegate { }, null, keys))
                {
                    diagram.Font = Font; diagram.Size = new Size(1000, 330); diagram.CreateControl();
                    var bitmap = new Bitmap(diagram.Width, diagram.Height); diagram.DrawToBitmap(bitmap, diagram.ClientRectangle);
                    int width = Math.Max(120, preview.ClientSize.Width - 28);
                    var picture = new PictureBox { Left = 10, Top = top + 8, Width = width, Height = (int)(width * .33f), Image = bitmap, SizeMode = PictureBoxSizeMode.Zoom };
                    picture.Disposed += delegate { bitmap.Dispose(); }; preview.Controls.Add(picture);
                }
            }
            finally { rendering = false; LayoutPreview(); UpdateShare(); }
        }

        protected override void Dispose(bool disposing)
        { if (disposing) tips.Dispose(); base.Dispose(disposing); }
        void LayoutPreview()
        {
            if (rendering) return;
            rendering = true;
            try
            {
                int width = Math.Max(100, preview.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 24), top = 8;
                foreach (Control control in preview.Controls)
                {
                    var label = control as Label;
                    int height;
                    if (label != null)
                    {
                        label.MaximumSize = new Size(width, 0);
                        height = label.GetPreferredSize(new Size(width, 0)).Height;
                    }
                    else height = (int)(width * .33f);
                    control.SetBounds(10, top + preview.AutoScrollPosition.Y, width, height);
                    top += height + 12;
                }
                preview.AutoScrollMinSize = new Size(0, top);
            }
            finally { rendering = false; }
        }
        void DeleteSelected()
        {
            Run(delegate {
                var presets = list.CheckedItems.Cast<Preset>().ToArray(); if (presets.Length == 0) return;
                if (EditorDialogs.Show(this, I18n.Str("preset.deleteconfirm", string.Join("\n", presets.Select(preset => preset.Name))), I18n.Str("preset.delete"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                store.Delete(presets);
                if (presets.Any(preset => preset.Id == session.PresetId)) session.EndPreview();
                Reload(null); Message("preset.deleted");
            });
        }
        void Export()
        {
            Run(delegate {
                using (var picker = new SaveFileDialog { Title = I18n.Str("preset.export"), Filter = I18n.Str("preset.export") + " (*.sav)|*.sav", FileName = "EnhancedInputUserSettings.sav", DefaultExt = "sav", AddExtension = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) { session.ExportCurrentScheme(picker.FileName); Message("preset.exported"); }
            });
        }
    }
}
