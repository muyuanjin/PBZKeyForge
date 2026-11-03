using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public static class KeyNames
    {
        public static readonly string[] Keyboard = new[] {
            "", "None", "LeftMouseButton","RightMouseButton","MiddleMouseButton","ThumbMouseButton","ThumbMouseButton2",
            "LeftShift","RightShift","LeftControl","RightControl","LeftAlt","RightAlt","SpaceBar","Tab","CapsLock",
            "Escape","Enter","BackSpace","LeftBracket","RightBracket","Semicolon","Quote","Comma","Period","Slash","Backslash","Tilde",
            "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
            "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Nine",
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
            "Up","Down","Left","Right","Home","End","PageUp","PageDown","Insert","Delete",
            "NumPadZero","NumPadOne","NumPadTwo","NumPadThree","NumPadFour","NumPadFive","NumPadSix","NumPadSeven","NumPadEight","NumPadNine"
        };
        public static readonly string[] Gamepad = new[] {
            "", "None", "Gamepad_FaceButton_Bottom","Gamepad_FaceButton_Right","Gamepad_FaceButton_Left","Gamepad_FaceButton_Top",
            "Gamepad_LeftShoulder","Gamepad_RightShoulder","Gamepad_LeftTrigger","Gamepad_RightTrigger",
            "Gamepad_LeftTriggerAxis","Gamepad_RightTriggerAxis","Gamepad_LeftThumbstick","Gamepad_RightThumbstick",
            "Gamepad_DPad_Up","Gamepad_DPad_Down","Gamepad_DPad_Left","Gamepad_DPad_Right","Gamepad_Special_Left","Gamepad_Special_Right"
        };
    }

    public class ActionInfo
    {
        public string Id;
        public ActionInfo(string id) { Id = id; }
        public string Label { get { return I18n.Str("act." + Id); } }
        public override string ToString() { return Label; }
    }

    public partial class MainForm : EditorWindow
    {
        public static readonly List<ActionInfo> Actions = new[] {
            "IA_LightAttack","IA_HeavyAttack","IA_Guard","IA_Dodge","IA_Jump","IA_Crouch","IA_MoveFast","IA_MoveSlow",
            "IA_LockTarget","IA_Interact","IA_Execute","IA_ShadowArm","IA_ShadowArm_LongPress",
            "IA_SwitchWeapon","IA_SwitchShadowArm","IA_UltimateSkill","IA_Sense_Default",
            "IA_OpenCharacterMenu","IA_OpenMapMenu","IA_UseTopItem","IA_UseBottomItem"
        }.Select(s => new ActionInfo(s)).ToList();

        public EditorSession Session { get; private set; }
        readonly ComboBox cbLang = new EditorComboBox(), cbPreset = new PresetPicker();
        readonly Button btnPresetManager = new EditorButton();
        readonly Button btnRefresh = new EditorButton(), btnMore = new EditorButton(), btnApply = new PrimaryActionButton();
        readonly EditorTab[] schemeTabs = { new EditorTab(), new EditorTab(), new EditorTab() };
        readonly EditorTab tabKb = new EditorTab(), tabPad = new EditorTab();
        readonly Label lblTitle = new Label(), lblPath = new Label(), lblGame = new Label();
        readonly Label lblPreset = new Label();
        readonly PresetApplyButton btnPresetMode = new PresetApplyButton();
        PresetApplyMode presetCycleCursor = PresetApplyMode.Append;
        PresetApplyMode presetMode = PresetApplyMode.Append;
        readonly Label lblStatus = new Label(), lblScope = new Label(), lblBackup = new Label(), lblEmpty = new Label();
        readonly Button btnUndo = new EditorButton();
        readonly LinkLabel btnConflicts = new LinkLabel(), btnChoose = new LinkLabel();
        readonly CheckBox chkActivate = new EditorCheckBox();
        readonly ToolTip tip = new ToolTip();
        readonly Panel body = new EditorPanel(), empty = new EditorPanel(), footer = new EditorPanel();
        readonly Panel kbHost = new EditorPanel(), padHost = new EditorPanel();
        readonly Dictionary<string, BindingButton[]> kbButtons = new Dictionary<string, BindingButton[]>();
        readonly Dictionary<string, BindingButton[]> padButtons = new Dictionary<string, BindingButton[]>();
        readonly List<Label> actionLabels = new List<Label>(), headings = new List<Label>();
        readonly List<Label> factoryLabels = new List<Label>();
        readonly Timer gameTimer = new Timer();
        bool loading, gamepad, modalActive, lastRunning;
        ContextMenuStrip moreMenu;
        string messageKey;
        object[] messageArgs;
        bool messageError;
        Bitmap backgroundCache;
        PresetHoverPreview presetHoverPreview;

        public MainForm()
        {
            I18n.Current = I18n.Detect();
            Build();
            ApplyLanguage();
            HandleCreated += delegate { EditorArtwork.DarkCaption(this); };
            Shown += delegate { if (Session == null) AutoFind(false); };
            Activated += delegate { if (Session != null && !loading && !modalActive) RefreshFromDisk(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!ConfirmDiscard()) e.Cancel = true; };
            gameTimer.Interval = 1500;
            gameTimer.Tick += delegate { if (EditorSession.IsGameRunning != lastRunning) RenderSession(); };
            gameTimer.Start();
        }

        void Build()
        {
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            ClientSize = new Size(1240, 840);
            MinimumSize = new Size(980, 710);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = EditorTheme.Background;
            ForeColor = EditorTheme.Ink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var root = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16, 6, 16, 6), Margin = Padding.Empty, BackColor = Color.Transparent };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int[] heights = { 72, 22, 36, 44, 0, 94 };
            for (int rowIndex = 0; rowIndex < heights.Length; rowIndex++)
                root.RowStyles.Add(rowIndex == 4 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, heights[rowIndex]));
            Controls.Add(root);

            var header = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            var brand = new EditorPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
            // Alpha bounds: app mark y=22..486/512; wordmark y=12..366/378.
            // Their visible centers align within 0.3 logical pixels, including image padding.
            var appMark = new PictureBox { Left = 0, Top = 8, Width = 54, Height = 54, Image = EditorArtwork.AppMark, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            var wordmark = new PictureBox { Left = 66, Top = 5, Width = 148, Height = 62, Image = EditorArtwork.Wordmark, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            lblTitle.SetBounds(232, 22, 420, 28); lblTitle.TextAlign = ContentAlignment.MiddleLeft; lblTitle.AutoEllipsis = true;
            lblTitle.Font = new Font(Font.FontFamily, 10.5f); lblTitle.ForeColor = EditorTheme.Ink; lblTitle.BackColor = Color.Transparent;
            brand.Controls.AddRange(new Control[] { appMark, wordmark, lblTitle });
            brand.Resize += delegate { lblTitle.Width = Math.Max(40, brand.ClientSize.Width - lblTitle.Left - 12); };
            cbLang.Dock = DockStyle.Fill; cbLang.Margin = new Padding(4, 30, 8, 26); cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string language in I18n.Languages) cbLang.Items.Add(I18n.DisplayName(language));
            cbLang.SelectedIndexChanged += delegate { if (loading || cbLang.SelectedIndex < 0) return; I18n.Current = I18n.Languages[cbLang.SelectedIndex]; ApplyLanguage(); };
            StyleButton(btnRefresh, false); btnRefresh.Margin = new Padding(4, 28, 4, 28); btnRefresh.Click += delegate { RefreshFromDisk(); };
            StyleButton(btnMore, false); btnMore.Margin = new Padding(4, 28, 0, 28); btnMore.Text = "•••"; btnMore.Click += delegate { ShowMore(); };
            header.Controls.Add(brand, 0, 0);
            cbLang.Dock = btnRefresh.Dock = btnMore.Dock = DockStyle.None;
            Control[] headerControls = { cbLang, btnRefresh, btnMore };
            var headerCells = new Panel[3];
            for (int i = 0; i < headerCells.Length; i++)
            {
                headerCells[i] = new EditorPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
                headerCells[i].Controls.Add(headerControls[i]);
                header.Controls.Add(headerCells[i], i + 1, 0);
            }
            bool arrangingHeader = false;
            Action alignHeaderControls = delegate
            {
                if (arrangingHeader) return;
                arrangingHeader = true;
                try
                {
                    float scale = CurrentAutoScaleDimensions.Width / 96f;
                    int gap = (int)(8 * scale), height = cbLang.Height;
                    for (int i = 0; i < headerControls.Length; i++)
                        headerControls[i].SetBounds(gap / 2, Math.Max(0, (headerCells[i].ClientSize.Height - height) / 2),
                            Math.Max(20, headerCells[i].ClientSize.Width - gap), height);
                }
                finally { arrangingHeader = false; }
            };
            foreach (var cell in headerCells) cell.Resize += delegate { alignHeaderControls(); };
            cbLang.SizeChanged += delegate { alignHeaderControls(); };
            root.Controls.Add(header, 0, 0);

            var pathBar = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            lblPath.Dock = DockStyle.Fill; lblPath.TextAlign = ContentAlignment.MiddleLeft; lblPath.AutoEllipsis = true; lblPath.ForeColor = EditorTheme.Muted; lblPath.BackColor = Color.Transparent;
            lblGame.Dock = DockStyle.Fill; lblGame.TextAlign = ContentAlignment.MiddleRight; lblGame.AutoEllipsis = true; lblGame.BackColor = Color.Transparent;
            pathBar.Controls.Add(lblPath, 0, 0); pathBar.Controls.Add(lblGame, 1, 0);
            pathBar.Paint += delegate(object s, PaintEventArgs e) { using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, 0, pathBar.Height - 1, pathBar.Width, pathBar.Height - 1); };
            root.Controls.Add(pathBar, 0, 1);

            var schemeBar = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            for (int i = 0; i < 3; i++)
            {
                schemeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
                var button = schemeTabs[i]; button.Dock = DockStyle.Fill; button.Margin = Padding.Empty;
                button.Font = new Font(Font.FontFamily, 12f);
                button.Click += delegate { if (button.Tag != null) SelectProfile((string)button.Tag); };
                schemeBar.Controls.Add(button, i, 0);
            }
            root.Controls.Add(schemeBar, 0, 2);

            var toolbar = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            var devices = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            devices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tabKb.Dock = DockStyle.Fill; tabKb.Margin = Padding.Empty; tabKb.Click += delegate { ShowGamepad(false); };
            tabPad.Dock = DockStyle.Fill; tabPad.Margin = Padding.Empty; tabPad.Click += delegate { ShowGamepad(true); };
            devices.Controls.Add(tabKb, 0, 0); devices.Controls.Add(tabPad, 1, 0); toolbar.Controls.Add(devices, 0, 0);
            var presets = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            presets.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            lblPreset.Dock = DockStyle.Fill; lblPreset.TextAlign = ContentAlignment.MiddleRight; lblPreset.Margin = new Padding(0, 0, 12, 0);
            cbPreset.Dock = DockStyle.Fill; cbPreset.Margin = new Padding(0, 10, 12, 8); cbPreset.DropDownStyle = ComboBoxStyle.DropDownList; cbPreset.DropDownWidth = 570;
            cbPreset.SelectedIndexChanged += delegate
            {
                if (loading || Session == null) return;
                TryAction(delegate {
                    var preset = cbPreset.SelectedItem as Preset;
                    if (preset == null) Session.SelectPreset(null, presetMode); else Session.StagePreset(preset, presetMode);
                    presetCycleCursor = presetMode;
                });
            };
            btnPresetManager.Dock = DockStyle.Fill; btnPresetManager.Margin = new Padding(0, 7, 8, 7); btnPresetManager.Click += delegate { ManagePresets(); };
            btnPresetMode.Dock = DockStyle.Fill; btnPresetMode.Margin = new Padding(0, 7, 8, 7);
            btnPresetMode.Click += delegate {
                if (loading || Session == null) return;
                var mode = (PresetApplyMode)(((int)presetCycleCursor + 1) % 3);
                presetCycleCursor = mode;
                TryAction(delegate {
                    var preset = cbPreset.SelectedItem as Preset;
                    if (preset != null) Session.StagePreset(preset, mode);
                    presetMode = mode;
                });
            };
            presets.Controls.Add(btnPresetMode, 2, 0); presets.Controls.Add(btnPresetManager, 3, 0);
            presets.Controls.Add(lblPreset, 0, 0); presets.Controls.Add(cbPreset, 1, 0); toolbar.Controls.Add(presets, 1, 0); root.Controls.Add(toolbar, 0, 3);
            presetHoverPreview = new PresetHoverPreview(cbPreset, delegate { return Session; }, delegate { return gamepad; }, delegate { return SelectedFamilyIndex; });

            body.Dock = DockStyle.Fill; body.Margin = Padding.Empty;
            BuildBindingTable(kbHost, kbButtons, false); BuildBindingTable(padHost, padButtons, true);
            kbHost.Dock = DockStyle.Fill; padHost.Dock = DockStyle.Fill; body.Controls.Add(kbHost); body.Controls.Add(padHost);
            BuildBindingViews();
            empty.Dock = DockStyle.Fill; empty.BackColor = EditorTheme.Background;
            lblEmpty.TextAlign = ContentAlignment.MiddleCenter; lblEmpty.Dock = DockStyle.Fill; lblEmpty.ForeColor = EditorTheme.Muted;
            btnChoose.Dock = DockStyle.Bottom; btnChoose.Height = 70; btnChoose.TextAlign = ContentAlignment.TopCenter; StyleLink(btnChoose); btnChoose.Click += delegate { Browse(); };
            empty.Controls.Add(lblEmpty); empty.Controls.Add(btnChoose); body.Controls.Add(empty); root.Controls.Add(body, 0, 4);

            footer.Dock = DockStyle.Fill; footer.Margin = Padding.Empty; footer.BackColor = EditorTheme.Surface;
            footer.Paint += delegate(object s, PaintEventArgs e) { using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            chkActivate.SetBounds(8, 12, 700, 30); chkActivate.AutoEllipsis = true;
            chkActivate.CheckedChanged += delegate { if (!loading && Session != null) TryAction(delegate { Session.SetActivation(chkActivate.Checked); }); };
            lblScope.SetBounds(8, 46, 700, 24); lblScope.AutoEllipsis = true; lblScope.Font = new Font(Font.FontFamily, 9f);
            btnConflicts.TextAlign = ContentAlignment.MiddleLeft; btnConflicts.AutoEllipsis = true; StyleLink(btnConflicts);
            btnConflicts.Font = lblScope.Font; btnConflicts.Click += delegate { ResolveConflicts(); };
            lblStatus.SetBounds(8, 72, 900, 23); lblStatus.AutoEllipsis = true; lblStatus.Font = new Font(Font.FontFamily, 9f);
            StyleButton(btnUndo, false); btnUndo.Dock = DockStyle.None;
            btnUndo.Click += delegate { TryAction(delegate { Session.Undo(); }); };
            StyleButton(btnApply, true); btnApply.Dock = DockStyle.None; btnApply.SetBounds(890, 15, 196, 46); btnApply.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold); btnApply.Click += delegate { ApplyChanges(); };
            lblBackup.SetBounds(890, 64, 196, 23); lblBackup.TextAlign = ContentAlignment.MiddleCenter; lblBackup.AutoEllipsis = true; lblBackup.ForeColor = EditorTheme.Muted; lblBackup.Font = new Font(Font.FontFamily, 8.5f);
            footer.Controls.AddRange(new Control[] { chkActivate, lblScope, btnConflicts, lblStatus, btnUndo, btnApply, lblBackup });
            footer.Resize += delegate { LayoutFooter(); };
            root.Controls.Add(footer, 0, 5);
            AutoScaleDimensions = new SizeF(96f, 96f);
            PerformAutoScale();
            alignHeaderControls();
            LayoutFooter();
        }

        void LayoutFooter()
        {
            float scale = CurrentAutoScaleDimensions.Width / 96f;
            int actionWidth = (int)(184 * scale), actionHeight = (int)(44 * scale), gap = (int)(12 * scale);
            btnApply.SetBounds(footer.ClientSize.Width - actionWidth - gap, (int)(14 * scale), actionWidth, actionHeight);
            btnUndo.SetBounds(btnApply.Left - gap - actionWidth, btnApply.Top, actionWidth, actionHeight);
            int left = gap, scopeWidth = Math.Max(100, btnUndo.Left - gap * 2);
            chkActivate.SetBounds(left, (int)(12 * scale), scopeWidth, (int)(28 * scale));
            lblScope.SetBounds(left, (int)(44 * scale), scopeWidth, (int)(24 * scale));
            btnConflicts.Bounds = lblScope.Bounds;
            lblBackup.SetBounds(btnApply.Left, (int)(64 * scale), actionWidth, (int)(20 * scale));
            lblStatus.SetBounds(left, (int)(70 * scale), Math.Max(0, btnApply.Left - gap - left), (int)(20 * scale));
        }

        static void StyleButton(Button button, bool primary)
        {
            button.Dock = DockStyle.Fill; button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = EditorTheme.Line; button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.BackColor = Color.Transparent; button.ForeColor = EditorTheme.Ink; button.Cursor = Cursors.Hand;
        }
        static void StyleLink(LinkLabel link)
        {
            link.LinkColor = EditorTheme.Muted; link.ActiveLinkColor = EditorTheme.Amber; link.VisitedLinkColor = EditorTheme.Muted; link.LinkBehavior = LinkBehavior.HoverUnderline;
        }
        void BuildBindingTable(Panel host, Dictionary<string, BindingButton[]> boxes, bool pad)
        {
            host.BackColor = EditorTheme.Line; host.Padding = new Padding(1);
            var scroll = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = EditorTheme.Surface };
            scroll.HandleCreated += delegate { EditorArtwork.DarkScrollbars(scroll); };
            var grid = new BindingTable { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, RowCount = Actions.Count, Margin = Padding.Empty, BackColor = EditorTheme.Surface, CellBorderStyle = TableLayoutPanelCellBorderStyle.None, Font = new Font(Font.FontFamily, 10.5f) };
            grid.CellPaint += delegate(object sender, TableLayoutCellPaintEventArgs e)
            {
                var rowLabel = grid.GetControlFromPosition(0, e.Row);
                Color color = rowLabel == null ? EditorTheme.Surface : rowLabel.BackColor;
                using (var brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush, e.CellBounds);
                using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            };
            SetColumns(grid);
            int row = 0;
            foreach (var action in Actions)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var label = new ActionNameLabel { ActionId = action.Id, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, Padding = new Padding(15, 0, 4, 0), AutoEllipsis = true, Tag = action.Id };
                actionLabels.Add(label); SetTip(label, action.Id); grid.Controls.Add(label, 0, row);
                var factoryLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty, AutoEllipsis = true,
                    ForeColor = EditorTheme.Muted, Tag = action.Id };
                factoryLabels.Add(factoryLabel); grid.Controls.Add(factoryLabel, 1, row);
                var buttons = new BindingButton[3];
                for (int slot = 0; slot < 3; slot++)
                {
                    int binding = slot;
                    var button = new BindingButton();
                    button.Click += delegate
                    {
                        if (Session == null || Session.IsResetPending) return;
                        using (var dialog = new BindingDialog(action.Label, pad, button.Value, button.DefaultKeys, button.UsesDefault))
                        {
                            modalActive = true;
                            try
                            {
                                if (dialog.ShowDialog(this) == DialogResult.OK)
                                    TryAction(delegate { if (pad) Session.SetGamepadFamily(action.Id, binding, SelectedFamilyIndex, dialog.Result); else Session.SetKeyboard(action.Id, binding, dialog.Result); });
                            }
                            finally { modalActive = false; }
                        }
                    };
                    buttons[slot] = button; grid.Controls.Add(button, slot + 2, row);
                }
                boxes[action.Id] = buttons; row++;
            }
            scroll.Controls.Add(grid);
            var headerViewport = new EditorPanel { Dock = DockStyle.Top, Height = 36, BackColor = EditorTheme.Floating };
            var header = new BindingTable { ColumnCount = 5, BackColor = headerViewport.BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
            SetColumns(header);
            for (int i = 0; i < 5; i++)
            {
                var label = new Label { Dock = DockStyle.Fill, TextAlign = i == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter,
                    Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty, Padding = i == 0 ? new Padding(15, 0, 0, 0) : Padding.Empty, Tag = i };
                headings.Add(label); header.Controls.Add(label, i, 0);
                if (i == 1 || i == 2)
                {
                    var question = new BindingHelpButton(i == 1 ? "help.defaults" : "help.slots") { Dock = DockStyle.Right, Width = 24 };
                    label.Controls.Add(question); label.Padding = new Padding(0, 0, 24, 0);
                }
            }
            headerViewport.Controls.Add(header);
            // Use the laid-out body width, including the scrollbar's actual effect.
            // System scrollbar metrics may already be DPI scaled before WinForms
            // scales control padding, so reserving that metric as padding scales twice.
            Action alignHeader = delegate { header.SetBounds(grid.Left, 0, grid.Width, headerViewport.ClientSize.Height); };
            grid.SizeChanged += delegate { alignHeader(); };
            headerViewport.SizeChanged += delegate { alignHeader(); };
            host.Layout += delegate { alignHeader(); };
            host.Controls.Add(scroll); host.Controls.Add(headerViewport);
            host.Controls.Add(new BindingLegend { Dock = DockStyle.Bottom, Height = 28, BackColor = EditorTheme.Surface });
        }
        static void SetColumns(TableLayoutPanel table)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            for (int i = 0; i < 3; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        }

        void ApplyLanguage()
        {
            loading = true;
            try
            {
                Text = "PBZKeyForge · " + I18n.Str("app.title"); lblTitle.Text = I18n.Str("editor.subtitle");
                btnRefresh.Text = I18n.Str("editor.refresh"); btnApply.Text = I18n.Str("editor.apply");
                lblPreset.Text = I18n.Str("editor.quick"); btnUndo.Text = I18n.Str("editor.undo");
                lblBackup.Text = I18n.Str("editor.backup");
                lblEmpty.Text = I18n.Str("editor.empty"); btnChoose.Text = I18n.Str("editor.choose");
                SetTip(btnMore, I18n.Str("editor.more"));
                foreach (var label in actionLabels) label.Text = I18n.Str("act." + label.Tag);
                foreach (var label in headings) label.Text = (int)label.Tag == 0 ? I18n.Str("col.action") : (int)label.Tag == 1
                    ? I18n.Str("views.factory", "").TrimEnd(' ', ':', '：') : I18n.Str("editor.binding", (int)label.Tag - 1);
                cbLang.SelectedIndex = Array.IndexOf(I18n.Languages, I18n.Current);
                PopulatePresets(); btnPresetManager.Text = I18n.Str("preset.manage");
            }
            finally { loading = false; }
            RenderSession();
            foreach (var row in kbButtons.Values.Concat(padButtons.Values)) foreach (var button in row) button.Invalidate();
        }

        public void RenderSession()
        {
            loading = true;
            SuspendLayout(); body.SuspendLayout(); footer.SuspendLayout();
            try
            {
                bool loaded = Session != null;
                lastRunning = EditorSession.IsGameRunning;
                lblGame.Text = "●  " + I18n.Str(lastRunning ? "editor.running" : "editor.closed"); lblGame.ForeColor = lastRunning ? EditorTheme.Amber : EditorTheme.Green;
                lblPath.Text = loaded ? I18n.Str("editor.location", DisplaySaveDirectory(Session.InputPath)) : I18n.Str("editor.nofile");
                SetTip(lblPath, loaded ? Session.InputPath : I18n.Str("editor.empty"));
                empty.Visible = !loaded; kbHost.Visible = loaded && actionView && !gamepad; padHost.Visible = loaded && actionView && gamepad; if (!loaded) empty.BringToFront();
                RenderBindingViews();
                tabKb.Text = I18n.Str("editor.keyboard"); tabPad.Text = I18n.Str("editor.gamepad"); tabKb.Selected = !gamepad; tabPad.Selected = gamepad;
                tabKb.Dirty = loaded && Session.KeyboardChangeCount > 0; tabPad.Dirty = loaded && Session.GamepadChangeCount > 0;
                tabKb.Enabled = loaded; tabPad.Enabled = loaded;
                string[] labels = { I18n.Str("scheme.default"), I18n.Str("scheme.custom1"), I18n.Str("scheme.custom2") };
                string[] profiles = loaded ? Session.Profiles : new string[0];
                for (int i = 0; i < schemeTabs.Length; i++)
                {
                    var button = schemeTabs[i]; button.Tag = i < profiles.Length ? profiles[i] : null; button.Text = i < profiles.Length ? SchemeLabel(profiles[i]) : labels[i];
                    button.Selected = loaded && (string)button.Tag == Session.SelectedProfile;
                    button.Badge = loaded && (string)button.Tag == Session.ActiveProfile ? I18n.Str("editor.current") : null;
                    button.Dirty = loaded && button.Tag != null && Session.HasProfileDraft((string)button.Tag); button.Enabled = loaded;
                }
                cbPreset.Enabled = loaded && !Session.IsResetPending;
                btnPresetManager.Enabled = cbPreset.Enabled;
                int presetIndex = 0;
                if (loaded && Session.PresetId != null)
                {
                    for (int i = 1; i < cbPreset.Items.Count; i++) if (((Preset)cbPreset.Items[i]).Id == Session.PresetId) presetIndex = i;
                }
                cbPreset.SelectedIndex = presetIndex;
                if (loaded && Session.PresetId != null && presetMode != Session.PresetMode) presetMode = presetCycleCursor = Session.PresetMode;
                bool restoringFactory = loaded && Session.PresetId == "vanilla_current";
                string[] modeKeys = { "preset.append", "preset.replace", "preset.only" };
                btnPresetMode.Mode = presetMode;
                btnPresetMode.Text = I18n.Str(modeKeys[(int)presetMode]) + " ↻";
                btnPresetMode.AccessibleName = I18n.Str("preset.mode") + ": " + I18n.Str(modeKeys[(int)presetMode]);
                btnPresetMode.Enabled = loaded && !Session.IsResetPending && !restoringFactory;
                SetTip(btnPresetMode, I18n.Str("preset.mode") + ": " + I18n.Str(modeKeys[(int)presetMode]) + "\n"
                    + I18n.Str(restoringFactory ? "preset.restore.help" : presetMode == PresetApplyMode.OnlyPreset
                        && cbPreset.SelectedItem is Preset && ((Preset)cbPreset.SelectedItem).IsUser ? "preset.only.scopehelp" : modeKeys[(int)presetMode] + ".help") + "\n"
                    + I18n.Str("preset.nextmode", I18n.Str(modeKeys[((int)presetCycleCursor + 1) % 3])));
                foreach (var action in Actions)
                    for (int slot = 0; slot < 3; slot++)
                    {
                        var kb = kbButtons[action.Id][slot]; var pad = padButtons[action.Id][slot];
                        var padRecords = loaded ? Session.BindingRecords(true, SelectedFamilyIndex).Where(r => r.Action == action.Id && r.SlotIndex == slot
                            && r.DefaultKey == InputDefaults.ForRawSlot(action.Id, true, InputDefaults.RawSlot(r.Slot))).ToArray() : new BindingRecord[0];
                        var kbRecords = loaded ? Session.BindingRecords(false, -1).Where(r => r.Action == action.Id && r.SlotIndex == slot).ToArray() : new BindingRecord[0];
                        kb.Value = kbRecords.Select(r => r.Key).FirstOrDefault(); pad.Value = padRecords.Select(r => r.Key).FirstOrDefault();
                        string keyboardDefault = loaded ? Session.GetKeyboardDefault(action.Id, slot) : null;
                        kb.DefaultKeys = keyboardDefault == null ? null : new[] { keyboardDefault }; kb.ActionLevelDefault = false; kb.UsesDefault = kb.Value == null;
                        string padDefault = loaded ? Session.GetGamepadDefault(action.Id, slot, SelectedFamilyIndex) : null;
                        pad.DefaultKeys = padRecords.Length == 0 ? padDefault == null ? null : new[] { padDefault }
                            : padRecords.Any(r => r.DefaultKey == null) ? null : padRecords.Select(r => r.DefaultKey).Distinct().ToArray();
                        pad.UsesDefault = padRecords.Length == 0 || padRecords.All(r => r.Key == r.DefaultKey);
                        kb.Changed = loaded && Session.IsKeyboardChanged(action.Id, slot); pad.Changed = padRecords.Any(r => r.Changed);
                        pad.Mixed = padRecords.Select(r => r.Key).Distinct().Skip(1).Any(); kb.Mixed = kbRecords.Select(r => r.Key).Distinct().Skip(1).Any();
                        kb.Unavailable = loaded && !Session.HasKeyboardSlot(action.Id, slot);
                        pad.Unavailable = loaded && !Session.HasGamepadSlot(action.Id, slot, SelectedFamilyIndex);
                        kb.Enabled = loaded && !Session.IsResetPending && !kb.Unavailable;
                        pad.Enabled = loaded && !Session.IsResetPending && !pad.Unavailable;
                        kb.AccessibleName = action.Label + " " + I18n.Str("editor.binding", slot + 1) + ": " + kb.DisplayText + " · " + kb.StatusText;
                        pad.AccessibleName = action.Label + " " + I18n.Str("editor.binding", slot + 1) + ": " + pad.DisplayText + " · " + pad.StatusText;
                        SetTip(kb, action.Id + "\n" + kb.DisplayText + " · " + kb.StatusText + (kb.UsesDefault ? "\n" + I18n.Str("editor.keyboarddefaulthelp") : ""));
                        SetTip(pad, action.Id + "\n" + pad.DisplayText + " · " + pad.StatusText + (pad.Mixed ? "\n" + I18n.Str("editor.mixedhelp") : ""));
                        kb.Invalidate(); pad.Invalidate();
                    }
                for (int i = 0; i < actionLabels.Count; i++)
                {
                    var source = i < Actions.Count ? kbButtons : padButtons;
                    string action = (string)actionLabels[i].Tag;
                    string[] defaults = !loaded ? null : i < Actions.Count ? Session.FactoryKeyboardKeys(action) : Session.Relations(action, true, SelectedFamilyIndex).DefaultKeys;
                    actionLabels[i].Text = BindingViewText.ActionName(action);
                    var actionLabel = actionLabels[i] as ActionNameLabel;
                    if (actionLabel != null)
                    {
                        string keys = !loaded ? "" : BindingViewText.Keys(Session.Relations(action, i >= Actions.Count, SelectedFamilyIndex).Keys);
                        actionLabel.KeySummary = loaded ? I18n.Str("help.keysummary", keys) : "";
                        SetTip(actionLabel, action + "\n" + actionLabel.KeySummary); actionLabel.Invalidate();
                    }
                    factoryLabels[i].Text = defaults == null ? I18n.Str("editor.defaultunknown") : BindingViewText.Keys(defaults);
                    SetTip(factoryLabels[i], I18n.Str("views.factory", factoryLabels[i].Text));
                    Color rowColor = source[(string)actionLabels[i].Tag].Any(b => b.Changed) ? EditorTheme.Preview : i % 2 == 0 ? EditorTheme.Surface : EditorTheme.RowAlternate;
                    actionLabels[i].BackColor = rowColor;
                    factoryLabels[i].BackColor = rowColor;
                    foreach (var button in source[(string)actionLabels[i].Tag]) if (button.BackColor != rowColor) button.BackColor = rowColor;
                }
                chkActivate.Text = I18n.Str("editor.activate", loaded ? SchemeLabel(Session.SelectedProfile) : "—");
                chkActivate.Checked = loaded && (Session.SelectedProfile == Session.ActiveProfile || Session.ActivationPending);
                chkActivate.Enabled = loaded && !Session.IsResetPending && Session.ActiveProfile != null && Session.SelectedProfile != Session.ActiveProfile;
                SetTip(chkActivate, loaded && Session.SchemeError != null ? I18n.Str("status.schemeunknown") + "\n" + Session.SchemeError : I18n.Str("editor.activatehelp"));
                btnApply.Enabled = loaded && Session.HasChanges && !lastRunning && Session.Conflicts.Length == 0; btnUndo.Visible = loaded && Session.HasChanges;
                lblScope.Text = !loaded ? "" : Session.IsResetPending ? I18n.Str("editor.resetscope", Session.ScopeChangeCount)
                    : I18n.Str(Session.HasChanges ? "editor.scope" : "editor.clean", SchemeLabel(Session.SelectedProfile), Session.KeyboardChangeCount, Session.GamepadChangeCount) + (Session.ActivationPending ? I18n.Str("editor.activationscope") : "");
                lblScope.ForeColor = loaded && Session.HasChanges ? BindingStatus.Pending : EditorTheme.Muted;
                int conflictCount = loaded ? Session.Conflicts.Length : 0;
                btnConflicts.Visible = conflictCount > 0; btnConflicts.Text = I18n.Str("editor.conflicts", conflictCount);
                lblScope.Visible = conflictCount == 0;
                lblStatus.Text = conflictCount > 0 ? I18n.Str("editor.conflictinfo") : loaded && Session.IsResetPending ? I18n.Str("editor.resetinfo")
                    : loaded && Session.SchemeError != null ? I18n.Str("editor.schemeunknown")
                    + (messageKey != null && messageKey != "editor.refreshed" ? " " + I18n.Str(messageKey, messageArgs) : "")
                    : loaded && lastRunning ? I18n.Str("editor.exittoapply") : messageKey != null ? I18n.Str(messageKey, messageArgs) : I18n.Str("editor.ready");
                lblStatus.ForeColor = messageError ? Color.FromArgb(238, 171, 156) : EditorTheme.Muted; SetTip(lblStatus, lblStatus.Text);
            }
            finally { footer.ResumeLayout(true); body.ResumeLayout(true); ResumeLayout(true); loading = false; }
        }

        void SetTip(Control control, string text)
        {
            if (tip.GetToolTip(control) != text) tip.SetToolTip(control, text);
        }

        static string SchemeLabel(string id)
        {
            if (id == null) return "—";
            if (id.EndsWith(".Default")) return I18n.Str("scheme.default");
            if (id.EndsWith("Custom1")) return I18n.Str("scheme.custom1");
            if (id.EndsWith("Custom2")) return I18n.Str("scheme.custom2");
            return id;
        }

        static string DisplaySaveDirectory(string inputPath)
        {
            string directory = Path.GetDirectoryName(inputPath);
            if (string.IsNullOrEmpty(directory)) return directory;
            directory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(directory);
            DirectoryInfo parent = Directory.GetParent(directory);
            return parent == null ? leaf : "…" + Path.DirectorySeparatorChar + parent.Name + Path.DirectorySeparatorChar + leaf;
        }
        void TryAction(Action action)
        {
            try { action(); messageKey = null; messageError = false; }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            RenderSession();
        }
        void SetMessage(string key, bool error, params object[] args)
        {
            messageKey = key; messageArgs = args; messageError = error;
        }
        public void SelectProfile(string profile) { if (Session != null) TryAction(delegate { Session.SwitchProfile(profile); }); }
        public void ShowGamepad(bool show) { gamepad = show; RenderSession(); }
        public void RefreshFromDisk()
        {
            if (loading || modalActive) return;
            if (Session == null) { AutoFind(false); return; }
            try { Session.Refresh(); SetMessage("editor.refreshed", false); }
            catch (Exception ex) { SetMessage("editor.refreshfail", true, ex.Message); }
            RenderSession();
        }
        public void ApplyChanges()
        {
            if (Session == null) return;
            try
            {
                var result = Session.Apply(); messageError = !result.Success;
                if (result.PartialSuccess) SetMessage("editor.partial", true, result.Error);
                else if (!result.Success) SetMessage("editor.failed", true, result.Error);
                else SetMessage("editor.saved", false);
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            RenderSession();
        }
        void ResolveConflicts()
        {
            if (Session == null) return;
            modalActive = true;
            try
            {
                foreach (var conflict in Session.Conflicts)
                {
                    string device = SchemeLabel(conflict.ProfileId) + " · " + I18n.Str(conflict.IsGamepad ? "editor.gamepad" : "editor.keyboard");
                    string detail = I18n.Str("editor.resolve", device, I18n.Str("act." + conflict.Action), conflict.SlotIndex + 1,
                        BindingButton.FriendlyKey(conflict.LocalValue), BindingButton.FriendlyKey(conflict.DiskValue));
                    var answer = EditorDialogs.Show(this, detail, I18n.Str("editor.conflicttitle"), MessageBoxButtons.YesNoCancel);
                    if (answer == DialogResult.Cancel) break;
                    Session.ResolveConflict(conflict.Id, answer == DialogResult.Yes);
                }
            }
            finally { modalActive = false; RenderSession(); }
        }
        bool ConfirmDiscard()
        {
            if (Session == null || !Session.HasAnyDrafts && !Session.HasChanges) return true;
            modalActive = true;
            try { return EditorDialogs.Show(this, I18n.Str("editor.discard"), I18n.Str("editor.drafttitle"), MessageBoxButtons.YesNo) == DialogResult.Yes; }
            finally { modalActive = false; }
        }
        void Browse()
        {
            if (!ConfirmDiscard()) return;
            using (var dialog = new OpenFileDialog { Filter = "EnhancedInputUserSettings.sav|EnhancedInputUserSettings.sav|*.sav|*.sav|All files|*.*", Title = I18n.Str("editor.choose") })
            {
                modalActive = true;
                try { if (dialog.ShowDialog(this) == DialogResult.OK) OpenPath(dialog.FileName); }
                finally { modalActive = false; }
            }
        }
        void OpenPath(string path) { TryAction(delegate { var next = EditorSession.Open(path); Session = next; }); }
        void AutoFind(bool confirm)
        {
            if (confirm && !ConfirmDiscard()) return;
            try
            {
                var roots = new List<string>();
                var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steam))
                {
                    roots.Add(steam.Replace('/', '\\'));
                    string libraries = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(libraries))
                        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\""))
                            roots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
                }
                foreach (var drive in DriveInfo.GetDrives()) if (drive.DriveType == DriveType.Fixed) roots.Add(Path.Combine(drive.Name, "SteamLibrary"));
                string found = roots.Select(root => Path.Combine(root, @"steamapps\common\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav")).FirstOrDefault(File.Exists);
                if (found != null) OpenPath(found);
                else { SetMessage("editor.empty", false); RenderSession(); }
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); RenderSession(); }
        }
        void ShowMore()
        {
            if (moreMenu == null) moreMenu = CreateMoreMenu();
            foreach (ToolStripItem item in moreMenu.Items)
                if (item is ToolStripMenuItem) item.Text = I18n.Str(item.Name);
            moreMenu.Items["editor.resetall"].Enabled = Session != null && !Session.IsResetPending;
            moreMenu.Show(btnMore, new Point(btnMore.Width - moreMenu.PreferredSize.Width, btnMore.Height));
        }

        ContextMenuStrip CreateMoreMenu()
        {
            var menu = new ContextMenuStrip { Font = Font, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink,
                Renderer = new ToolStripProfessionalRenderer(new EditorMenuColors()), ShowImageMargin = false, Padding = new Padding(5) };
            menu.Items.Add(I18n.Str("editor.changefile"), null, delegate { Browse(); }).Name = "editor.changefile";
            menu.Items.Add(I18n.Str("editor.rediscover"), null, delegate { AutoFind(true); }).Name = "editor.rediscover";
            menu.Items.Add(new ToolStripSeparator());
            var reset = menu.Items.Add(I18n.Str("editor.resetall"), null, delegate
            {
                if (Session == null) return;
                modalActive = true;
                try
                {
                    if (EditorDialogs.Show(this, I18n.Str("editor.resetconfirm"), I18n.Str("editor.resetall"), MessageBoxButtons.OKCancel) == DialogResult.OK)
                        TryAction(delegate { Session.ResetAll(); });
                }
                finally { modalActive = false; }
            });
            reset.Name = "editor.resetall";
            menu.Items.Add(I18n.Str("editor.openbackup"), null, delegate
            {
                try { Process.Start("explorer.exe", GameSchemeSettings.BackupDirectory()); }
                catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); RenderSession(); }
            }).Name = "editor.openbackup";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.Str("btn.about"), null, delegate { modalActive = true; try { EditorDialogs.Show(this, I18n.Str("about.text"), "PBZKeyForge " + Program.Version, MessageBoxButtons.OK); } finally { modalActive = false; } }).Name = "btn.about";
            foreach (ToolStripItem item in menu.Items) item.Padding = new Padding(10, 6, 10, 6);
            // WinForms still uses the menu after Closed while dispatching an item click.
            // Keep it alive across openings; MainForm.Dispose owns its lifetime.
            return menu;
        }

        void PopulatePresets()
        {
            cbPreset.Items.Clear(); cbPreset.Items.Add(I18n.Str("editor.nopreset"));
            foreach (var preset in Presets.All().Where(preset => !preset.Vanilla)) cbPreset.Items.Add(preset);
            try { foreach (var preset in UserPresetStore.Default().Load()) cbPreset.Items.Add(preset); }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
        }

        void ManagePresets()
        {
            if (Session == null) return;
            modalActive = true;
            try
            {
                using (var dialog = new UserPresetDialog(Session, UserPresetStore.Default(), presetMode))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) presetMode = presetCycleCursor = dialog.ChosenMode;
                }
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally
            {
                modalActive = false; loading = true;
                try { PopulatePresets(); } finally { loading = false; RenderSession(); }
            }
        }

        public void UiSmoke(string path) { OpenPath(path); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (backgroundCache == null || backgroundCache.Size != ClientSize)
            {
                if (backgroundCache != null) backgroundCache.Dispose();
                backgroundCache = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(backgroundCache)) EditorArtwork.PaintBackground(graphics, ClientRectangle);
            }
            Rectangle clip = Rectangle.Intersect(ClientRectangle, Rectangle.Ceiling(e.Graphics.ClipBounds));
            if (clip.Width > 0 && clip.Height > 0) e.Graphics.DrawImage(backgroundCache, clip, clip, GraphicsUnit.Pixel);
        }
        public void AutoApply(string inPath, string outPath, string presetId, string scheme, bool activate)
        {
            if (activate) throw new ArgumentException("Use --activate <installed.sav> <profileId> separately.");
            var save = SaveFile.Load(inPath); string profile = scheme ?? save.ActiveProfile; var record = save.RecordFor(profile);
            if (record == null) throw new ArgumentException("Unknown scheme: " + profile);
            var preset = Presets.ById(presetId); if (preset == null) throw new ArgumentException("Unknown preset: " + presetId);
            Presets.Apply(save, preset, record); save.Save(outPath);
            Console.WriteLine("autoapply preset=" + presetId + " scheme=" + profile + " -> " + outPath);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { gameTimer.Dispose(); tip.Dispose(); if (presetHoverPreview != null) presetHoverPreview.Dispose(); if (moreMenu != null) moreMenu.Dispose(); if (backgroundCache != null) backgroundCache.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
