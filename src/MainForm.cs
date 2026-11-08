using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
            "Gamepad_LeftStick_Up","Gamepad_LeftStick_Down","Gamepad_LeftStick_Left","Gamepad_LeftStick_Right",
            "Gamepad_RightStick_Up","Gamepad_RightStick_Down","Gamepad_RightStick_Left","Gamepad_RightStick_Right",
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
        readonly Button btnRefresh = new EditorButton(), btnMore = new EditorButton(), btnApply = new ApplyActionButton();
        EditorTab[] schemeTabs = new EditorTab[0];
        readonly EditorTab tabKb = new EditorTab(), tabPad = new EditorTab();
        readonly Label lblTitle = new EditorLabel(), lblPath = new EditorLabel(), lblGame = new ConnectionStatusLabel();
        readonly Label lblPreset = new EditorLabel();
        readonly PresetModeSelector btnPresetMode = new PresetModeSelector();
        readonly EditorPreferences preferences;
        PresetApplyMode presetMode;
        readonly Label lblStatus = new EditorLabel(), lblScope = new EditorLabel(), lblBackup = new EditorLabel(), lblEmpty = new EditorLabel(), lblOffline = new EditorLabel();
        readonly Button btnUndo = new EditorButton();
        readonly LinkLabel btnConflicts = new EditorLinkLabel(), btnChoose = new EditorLinkLabel();
        readonly CheckBox chkActivate = new EditorCheckBox();
        readonly ToolTip tip = new ToolTip();
        readonly Panel body = new EditorPanel(), empty = new EditorPanel(), footer = new EditorPanel();
        readonly Panel kbHost = new EditorPanel(), padHost = new EditorPanel();
        readonly BindingLegend kbLegend = new BindingLegend(), padLegend = new BindingLegend();
        readonly Dictionary<string, BindingButton[]> kbButtons = new Dictionary<string, BindingButton[]>();
        readonly Dictionary<string, BindingButton[]> padButtons = new Dictionary<string, BindingButton[]>();
        readonly List<Label>[] actionLabels = { new List<Label>(), new List<Label>() };
        readonly List<Label>[] factoryLabels = { new List<Label>(), new List<Label>() };
        readonly List<Label> headings = new List<Label>();
        readonly Timer gameTimer = new Timer();
        bool loading, gamepad, modalActive, lastRunning;
        bool compactLayout;
        ContextMenuStrip moreMenu;
        string messageKey;
        object[] messageArgs;
        bool messageError;
        Bitmap backgroundCache;
        PresetHoverPreview presetHoverPreview;

        public MainForm() : this(EditorPreferences.Default()) { }

        public static MainForm OpenDefault()
        {
            return OpenDefault(EditorPreferences.Default());
        }

        public static MainForm OpenDefault(EditorPreferences preferences)
        {
            var initialSession = Task.Run(delegate {
                return SaveDiscovery.Open(preferences, true, false) ?? EditorSession.CreateOffline();
            });
            var form = new MainForm(preferences);
            try { form.Session = initialSession.GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                form.NewOfflineScheme();
                form.SetMessage("editor.failed", true, ex.Message);
            }
            form.RenderSession();
            return form;
        }

        public MainForm(EditorPreferences preferences)
        {
            this.preferences = preferences;
            presetMode = preferences.LoadPresetMode();
            I18n.Current = I18n.Detect();
            SuspendLayout();
            try { Build(); ApplyLanguage(); }
            finally { ResumeLayout(true); }
            HandleCreated += delegate { EditorArtwork.DarkCaption(this); };
            Shown += delegate {
                StartSchemeDiscovery();
                Activated += delegate {
                    UpdateGameState();
                    if (Session != null && !Session.IsOffline && !loading && !modalActive) RefreshFromDisk(false);
                };
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (IsApplying || !updatingApplication && !ConfirmDiscard()) e.Cancel = true; };
            gameTimer.Interval = 1500;
            gameTimer.Tick += delegate { if (keyEnhancementFlame.ApplicationActive) UpdateGameState(); };
            gameTimer.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            if (Session == null) AutoFind(false);
            UpdateGameState();
            base.OnLoad(e);
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
            root.SuspendLayout();
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int[] heights = { 72, 22, 36, 64, 0, 96 };
            for (int rowIndex = 0; rowIndex < heights.Length; rowIndex++)
                root.RowStyles.Add(rowIndex == 4 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, heights[rowIndex]));

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
            StyleButton(btnRefresh, false); btnRefresh.Margin = new Padding(4, 28, 4, 28); btnRefresh.Click += delegate { if (Session != null && Session.IsOffline) Browse(); else RefreshFromDisk(); };
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
            lblGame.Font = new Font(Font, FontStyle.Bold);
            pathBar.Controls.Add(lblPath, 0, 0); pathBar.Controls.Add(lblGame, 1, 0);
            pathBar.Paint += delegate(object s, PaintEventArgs e) { using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, 0, pathBar.Height - 1, pathBar.Width, pathBar.Height - 1); };
            root.Controls.Add(pathBar, 0, 1);

            root.Controls.Add(BuildSchemeBar(), 0, 2);

            var toolbar = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            var devices = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            devices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tabKb.Dock = DockStyle.Fill; tabKb.Margin = Padding.Empty; tabKb.Click += delegate { ShowGamepad(false); };
            tabPad.Dock = DockStyle.Fill; tabPad.Margin = Padding.Empty; tabPad.Click += delegate { ShowGamepad(true); };
            devices.Controls.Add(tabKb, 0, 0); devices.Controls.Add(tabPad, 1, 0); toolbar.Controls.Add(devices, 0, 0);
            var presets = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            presets.RowStyles.Add(new RowStyle(SizeType.Percent, 56.25f));
            presets.RowStyles.Add(new RowStyle(SizeType.Percent, 43.75f));
            presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            lblPreset.Dock = DockStyle.Fill; lblPreset.TextAlign = ContentAlignment.MiddleRight; lblPreset.Margin = new Padding(0, 0, 12, 0);
            cbPreset.Dock = DockStyle.Fill; cbPreset.Margin = new Padding(0, 3, 8, 3); cbPreset.DropDownStyle = ComboBoxStyle.DropDownList; cbPreset.DropDownWidth = 570;
            cbPreset.SelectedIndexChanged += delegate
            {
                if (loading || Session == null) return;
                TryAction(delegate {
                    var preset = cbPreset.SelectedItem as Preset;
                    StageBindingPreset(preset, presetMode);
                });
            };
            btnPresetManager.Dock = DockStyle.Fill; btnPresetManager.Margin = new Padding(0, 3, 8, 3); btnPresetManager.Click += delegate { ManagePresets(); };
            btnPresetMode.Dock = DockStyle.None; btnPresetMode.Anchor = AnchorStyles.Left;
            btnPresetMode.AutoSize = true; btnPresetMode.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnPresetMode.Font = new Font(Font.FontFamily, 9f); btnPresetMode.Margin = new Padding(0, 0, 8, 2);
            btnPresetMode.ModeChanged += delegate {
                if (loading || Session == null) return;
                var mode = btnPresetMode.Mode;
                TryAction(delegate {
                    var preset = cbPreset.SelectedItem as Preset;
                    if (preset != null) StageBindingPreset(preset, mode);
                    presetMode = mode;
                    preferences.SavePresetMode(mode);
                });
            };
            var modeLabel = new EditorLabel { Text = I18n.Str("preset.mode"), Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight, ForeColor = EditorTheme.Muted, Font = new Font(Font.FontFamily, 9f), Margin = new Padding(0, 0, 12, 0), Name = "presetModeLabel" };
            presets.Controls.Add(modeLabel, 0, 1);
            presets.Controls.Add(btnPresetMode, 1, 1);
            presets.Controls.Add(btnPresetManager, 2, 0);
            presets.Controls.Add(lblPreset, 0, 0); presets.Controls.Add(cbPreset, 1, 0); toolbar.Controls.Add(presets, 1, 0); root.Controls.Add(toolbar, 0, 3);
            presetHoverPreview = new PresetHoverPreview(cbPreset, delegate { return Session; }, delegate { return gamepad; }, delegate { return SelectedFamilyIndex; });

            body.Dock = DockStyle.Fill; body.Margin = Padding.Empty;
            kbHost.Dock = DockStyle.Fill; padHost.Dock = DockStyle.Fill;
            BuildBindingViews();
            empty.Dock = DockStyle.Fill; empty.BackColor = EditorTheme.Background;
            lblEmpty.TextAlign = ContentAlignment.MiddleCenter; lblEmpty.Dock = DockStyle.Fill; lblEmpty.ForeColor = EditorTheme.Muted;
            btnChoose.Dock = DockStyle.Bottom; btnChoose.Height = 70; btnChoose.TextAlign = ContentAlignment.TopCenter; StyleLink(btnChoose); btnChoose.Click += delegate { Browse(); };
            empty.Controls.Add(lblEmpty); empty.Controls.Add(btnChoose); body.Controls.Add(empty); root.Controls.Add(body, 0, 4);

            footer.Dock = DockStyle.Fill; footer.Margin = Padding.Empty; footer.BackColor = EditorTheme.Surface;
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                float scale = e.Graphics.DpiX / 96f;
                using (var pen = new Pen(EditorTheme.Line))
                {
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
                    int divider = (chkPerfectDeflectNoRapidClickPenalty.Right + chkActivate.Left) / 2;
                    e.Graphics.DrawLine(pen, divider, (int)(12 * scale), divider, footer.Height - (int)(12 * scale));
                }
            };
            chkActivate.SetBounds(8, 12, 700, 30); chkActivate.AutoEllipsis = true;
            chkActivate.CheckedChanged += delegate { if (!loading && Session != null) TryAction(delegate { Session.SetActivation(chkActivate.Checked); }); };
            lblOffline.AutoEllipsis = true; lblOffline.TextAlign = ContentAlignment.MiddleLeft;
            lblScope.SetBounds(8, 46, 700, 24); lblScope.AutoEllipsis = true; lblScope.Font = new Font(Font.FontFamily, 9f);
            btnConflicts.TextAlign = ContentAlignment.MiddleLeft; btnConflicts.AutoEllipsis = true; StyleLink(btnConflicts);
            btnConflicts.Font = lblScope.Font; btnConflicts.Click += delegate { ResolveConflicts(); };
            lblStatus.SetBounds(8, 72, 900, 23); lblStatus.AutoEllipsis = false; lblStatus.Font = new Font(Font.FontFamily, 9f);
            StyleButton(btnUndo, false); btnUndo.Dock = DockStyle.None;
            btnUndo.Click += delegate { UndoFromBindingEditor(); };
            StyleButton(btnApply, true); btnApply.Dock = DockStyle.None; btnApply.SetBounds(890, 15, 196, 46); btnApply.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold); btnApply.Click += delegate { ApplyChanges(); };
            lblBackup.SetBounds(890, 64, 196, 23); lblBackup.TextAlign = ContentAlignment.MiddleCenter; lblBackup.AutoEllipsis = true; lblBackup.ForeColor = EditorTheme.Muted; lblBackup.Font = new Font(Font.FontFamily, 8.5f);
            chkPerfectDeflectNoRapidClickPenalty.CheckedChanged += delegate { StageResourceMod(); };
            footer.Controls.AddRange(new Control[] { chkActivate, lblOffline, lblScope, btnConflicts, lblStatus, btnUndo, btnApply, lblBackup, chkPerfectDeflectNoRapidClickPenalty });
            footer.Resize += delegate { LayoutFooter(); };
            BuildKeyEnhancement();
            BuildModMenus();
            BuildEditingSupport();
            root.Controls.Add(footer, 0, 5);
            root.ResumeLayout(false);
            Controls.Add(root);
            AutoScaleDimensions = new SizeF(96f, 96f);
            PerformAutoScale();
            Action arrangeMainLayout = delegate {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                compactLayout = ClientSize.Height / scale < 800;
                root.RowStyles[0].Height = (compactLayout ? 48 : 72) * scale;
                root.RowStyles[3].Height = (compactLayout ? 56 : 64) * scale;
                root.RowStyles[5].Height = ((compactLayout ? 84 : 96) + (feedbackPanel != null && feedbackPanel.Visible ? 44 : 0)) * scale;
                appMark.SetBounds(0, (int)(6 * scale), (int)((compactLayout ? 36 : 54) * scale), (int)((compactLayout ? 36 : 54) * scale));
                wordmark.SetBounds((int)((compactLayout ? 46 : 66) * scale), (int)(3 * scale), (int)((compactLayout ? 106 : 148) * scale), (int)((compactLayout ? 44 : 62) * scale));
                lblTitle.SetBounds((int)((compactLayout ? 164 : 232) * scale), (int)((compactLayout ? 10 : 22) * scale), Math.Max(40, brand.Width - (int)((compactLayout ? 176 : 244) * scale)), (int)(28 * scale));
                keyEnhancementBar.Compact = chkPerfectDeflectNoRapidClickPenalty.Compact = compactLayout;
                LayoutFooter();
            };
            root.Resize += delegate { arrangeMainLayout(); };
            arrangeMainLayout();
            alignHeaderControls();
            LayoutFooter();
        }

        void LayoutFooter()
        {
            float scale = CurrentAutoScaleDimensions.Width / 96f;
            int gap = (int)(12 * scale);
            int usable = Math.Max(1, footer.ClientSize.Width - gap * 2);
            int contentHeight = (int)((compactLayout ? 84 : 96) * scale);
            int noticeHeight = feedbackPanel != null && feedbackPanel.Visible ? (int)(44 * scale) : 0;
            var layout = footer.Parent as TableLayoutPanel;
            if (layout != null && layout.RowStyles[5].Height != contentHeight + noticeHeight)
                layout.RowStyles[5].Height = contentHeight + noticeHeight;
            int actionWidth = (int)(usable * .18f), actionHeight = (int)(44 * scale);
            int keyEnhancementWidth = compactLayout ? (int)(usable * .30f) : Math.Max((int)(280 * scale), (int)(usable * .24f));
            int modWidth = compactLayout ? keyEnhancementWidth : (int)(usable * .34f);
            int componentTop = (int)((compactLayout ? 4 : 6) * scale);
            int componentHeight = compactLayout ? (int)(32 * scale) : contentHeight - (int)(12 * scale);
            keyEnhancementBar.SetBounds(gap, componentTop, keyEnhancementWidth - gap, componentHeight);
            chkPerfectDeflectNoRapidClickPenalty.SetBounds(gap + keyEnhancementWidth, componentTop, modWidth - gap, componentHeight);
            btnApply.SetBounds(footer.ClientSize.Width - actionWidth - gap, compactLayout ? (int)(6 * scale) : (contentHeight - actionHeight) / 2, actionWidth, actionHeight);
            int undoWidth = (int)(70 * scale), secondaryTop = btnApply.Bottom + (int)(2 * scale);
            btnUndo.SetBounds(btnApply.Left, secondaryTop, undoWidth, (int)(18 * scale));
            using (var graphics = chkPerfectDeflectNoRapidClickPenalty.CreateGraphics())
            {
                var header = chkPerfectDeflectNoRapidClickPenalty.MeasureHeader(graphics);
                Rectangle options = header.Options;
                options.Offset(chkPerfectDeflectNoRapidClickPenalty.Location);
                btnResourceModMore.Bounds = options;
            }
            btnKeyEnhancementMore.SetBounds(keyEnhancementBar.Width - (int)(28 * scale), (int)(4 * scale), (int)(24 * scale), (int)(24 * scale));
            int activationLeft = chkPerfectDeflectNoRapidClickPenalty.Right + gap * (compactLayout ? 1 : 2);
            int scopeWidth = Math.Max(1, btnApply.Left - gap - activationLeft);
            chkActivate.SetBounds(activationLeft, (int)((compactLayout ? 4 : 10) * scale), scopeWidth, (int)((compactLayout ? 30 : 28) * scale));
            lblOffline.Bounds = chkActivate.Bounds;
            int textLeft = compactLayout ? gap : activationLeft;
            int textWidth = compactLayout ? Math.Max(1, btnApply.Left - gap * 2) : scopeWidth;
            lblScope.SetBounds(textLeft, (int)((compactLayout ? 38 : 36) * scale), textWidth,
                compactLayout ? Math.Max(lblScope.Font.Height + 2, (int)(20 * scale)) : (int)(20 * scale));
            btnConflicts.Bounds = lblScope.Bounds;
            int backupLeft = btnUndo.Right + (int)(4 * scale);
            lblBackup.SetBounds(backupLeft, secondaryTop, Math.Max(1, btnApply.Right - backupLeft), (int)(18 * scale));
            lblStatus.SetBounds(textLeft, (int)((compactLayout ? 59 : 57) * scale), textWidth,
                Math.Max(lblStatus.Font.Height + 2, compactLayout ? (int)(21 * scale) : contentHeight - (int)(61 * scale)));
            if (feedbackPanel != null) feedbackPanel.SetBounds(gap, contentHeight, usable, (int)(44 * scale));
            footer.Invalidate();
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
            grid.SuspendLayout();
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
                actionLabels[pad ? 1 : 0].Add(label); SetTip(label, action.Id); grid.Controls.Add(label, 0, row);
                var factoryLabel = new EditorLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty, AutoEllipsis = true,
                    ForeColor = EditorTheme.Muted, Tag = action.Id };
                factoryLabels[pad ? 1 : 0].Add(factoryLabel); grid.Controls.Add(factoryLabel, 1, row);
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
                            finally { EndModal(); }
                        }
                    };
                    buttons[slot] = button; grid.Controls.Add(button, slot + 2, row);
                }
                boxes[action.Id] = buttons; row++;
            }
            grid.ResumeLayout(false);
            scroll.Controls.Add(grid);
            var headerViewport = new EditorPanel { Dock = DockStyle.Top, Height = 36, BackColor = EditorTheme.Floating };
            var header = new BindingTable { ColumnCount = 5, RowCount = 1, BackColor = headerViewport.BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
            header.SuspendLayout();
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            SetColumns(header);
            for (int i = 0; i < 5; i++)
            {
                var label = new EditorLabel { Dock = DockStyle.Fill, TextAlign = i == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter,
                    Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty, Padding = i == 0 ? new Padding(15, 0, 0, 0) : Padding.Empty, Tag = i };
                label.Text = HeadingText(i); headings.Add(label); header.Controls.Add(label, i, 0);
                if (i == 1 || i == 2)
                {
                    var question = new BindingHelpButton(i == 1 ? "help.defaults" : "help.slots") { Dock = DockStyle.Right, Width = 24 };
                    label.Controls.Add(question); label.Padding = new Padding(24, 0, 24, 0);
                }
            }
            header.ResumeLayout(false);
            headerViewport.Controls.Add(header);
            host.Controls.Add(scroll); host.Controls.Add(headerViewport);
            var legend = pad ? padLegend : kbLegend;
            legend.Dock = DockStyle.Bottom; legend.Height = 28; legend.BackColor = EditorTheme.Surface;
            host.Controls.Add(legend);
            float scale = CurrentAutoScaleDimensions.Width / 96f;
            if (scale != 1f) host.Scale(new SizeF(scale, scale));
            // Align after scaling so resize handlers use the final header height.
            Action alignHeader = delegate { header.SetBounds(grid.Left, 0, grid.Width, headerViewport.ClientSize.Height); };
            grid.SizeChanged += delegate { alignHeader(); };
            headerViewport.SizeChanged += delegate { alignHeader(); };
            host.Layout += delegate { alignHeader(); };
            alignHeader();
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
                foreach (var label in actionLabels.SelectMany(labels => labels)) label.Text = I18n.Str("act." + label.Tag);
                foreach (var label in headings) label.Text = HeadingText((int)label.Tag);
                cbLang.SelectedIndex = Array.IndexOf(I18n.Languages, I18n.Current);
                PopulatePresets(); btnPresetManager.Text = I18n.Str("preset.manage");
            }
            finally { loading = false; }
            if (Session != null) RenderSession();
            foreach (var row in kbButtons.Values.Concat(padButtons.Values)) foreach (var button in row) button.Invalidate();
        }

        public void RenderSession()
        {
            if (IsApplying) return;
            loading = true;
            SuspendLayout(); body.SuspendLayout(); footer.SuspendLayout();
            try
            {
                bool loaded = Session != null;
                bool offline = loaded && Session.IsOffline;
                kbLegend.IsOffline = padLegend.IsOffline = offline;
                lblPath.Text = offline ? I18n.Str("offline.location") : loaded ? I18n.Str("editor.location", DisplaySaveDirectory(Session.InputPath)) : I18n.Str("editor.nofile");
                SetTip(lblPath, offline ? I18n.Str("offline.help") : loaded ? Session.InputPath : I18n.Str("editor.empty"));
                btnRefresh.Text = I18n.Str(offline ? "editor.choose" : "editor.refresh");
                lblBackup.Text = I18n.Str(offline ? "offline.exporthelp" : "editor.backup");
                empty.Visible = !loaded; if (!loaded) empty.BringToFront();
                RefreshKeyEnhancement();
                RenderBindingViews();
                tabKb.Text = I18n.Str("editor.keyboard"); tabPad.Text = I18n.Str("editor.gamepad"); tabKb.Selected = !gamepad; tabPad.Selected = gamepad;
                tabKb.Dirty = loaded && keyboardBindingChanges > 0; tabPad.Dirty = loaded && gamepadBindingChanges > 0;
                tabKb.Enabled = loaded; tabPad.Enabled = loaded;
                RenderSchemeBar();
                cbPreset.Enabled = loaded && !Session.IsResetPending;
                btnPresetManager.Enabled = !IsApplying;
                int presetIndex = 0;
                if (loaded && SelectedPresetId != null)
                {
                    for (int i = 1; i < cbPreset.Items.Count; i++) if (((Preset)cbPreset.Items[i]).Id == SelectedPresetId) presetIndex = i;
                }
                cbPreset.SelectedIndex = presetIndex;
                bool restoringFactory = loaded && SelectedPresetId == "vanilla_current";
                btnPresetMode.RefreshLabels(); btnPresetMode.Mode = presetMode;
                btnPresetMode.Enabled = loaded && !Session.IsResetPending && !restoringFactory;
                var modeHeading = btnPresetMode.Parent.Controls.Find("presetModeLabel", false).FirstOrDefault();
                if (modeHeading != null) modeHeading.Text = I18n.Str("preset.mode");
                chkActivate.Text = I18n.Str("editor.activate", loaded ? SchemeLabel(Session.SelectedProfile) : "—");
                chkActivate.Visible = !offline; lblOffline.Visible = offline;
                lblOffline.Text = I18n.Str("offline.mode");
                SetTip(lblOffline, I18n.Str("offline.help"));
                chkActivate.Checked = loaded && (Session.SelectedProfile == Session.ActiveProfile || Session.ActivationPending);
                chkActivate.Enabled = loaded && !Session.IsResetPending && Session.ActiveProfile != null && Session.SelectedProfile != Session.ActiveProfile;
                if (EnhancedSchemes && !offline)
                {
                    chkActivate.Visible = true; lblOffline.Visible = false;
                    chkActivate.Text = I18n.Str("schemes.active", EnhancedSchemeLabel(keyEnhancement.ActiveSchemeId));
                    chkActivate.Checked = keyEnhancement.SelectedSchemeId == keyEnhancement.ActiveSchemeId;
                    chkActivate.Enabled = false;
                }
                SetTip(chkActivate, loaded && Session.SchemeDiscoveryRequired && SchemeProbePending ? I18n.Str("editor.discovering")
                    : loaded && Session.SchemeError != null ? I18n.Str("status.schemeunknown") + "\n" + Session.SchemeError : I18n.Str("editor.activatehelp"));
                RefreshFooterResourceMod();
                btnUndo.Visible = loaded;
                btnUndo.Enabled = loaded && (HasBindingHistory || Session.HasChanges || ResourceModPending || KeyEnhancementPending);
                LayoutFooter();
                int conflictCount = loaded ? Session.Conflicts.Length : 0;
                btnConflicts.Visible = conflictCount > 0; btnConflicts.Text = I18n.Str("editor.conflicts", conflictCount);
                lblScope.Visible = conflictCount == 0;
                RenderGameStatus();
            }
            finally
            {
                footer.ResumeLayout(true); body.ResumeLayout(true); ResumeLayout(true); loading = false;
            }
        }

        static string HeadingText(int column)
        {
            return column == 0 ? I18n.Str("col.action") : column == 1
                ? I18n.Str("views.factory", "").TrimEnd(' ', ':', '：') : I18n.Str("editor.binding", column - 1);
        }

        void RenderActionTable(BindingSnapshot snapshot)
        {
            var host = gamepad ? padHost : kbHost;
            var buttons = gamepad ? padButtons : kbButtons;
            if (buttons.Count == 0)
            {
                host.SuspendLayout();
                try
                {
                    BuildBindingTable(host, buttons, gamepad);
                }
                finally { host.ResumeLayout(true); }
            }
            var records = Session.BindingRecords(gamepad, SelectedFamilyIndex).ToLookup(r => r.Action);
            var labels = actionLabels[gamepad ? 1 : 0];
            var defaults = factoryLabels[gamepad ? 1 : 0];
            for (int row = 0; row < Actions.Count; row++)
            {
                var action = Actions[row];
                for (int slot = 0; slot < 3; slot++)
                {
                    var button = buttons[action.Id][slot];
                    var bindings = records[action.Id].Where(r => r.SlotIndex == slot && (!gamepad
                        || r.DefaultKey == Session.Defaults.ForRawSlot(action.Id, true, InputDefaults.RawSlot(r.Slot)))).ToArray();
                    button.IsOffline = Session.IsOffline;
                    button.Value = bindings.Select(r => r.Key).FirstOrDefault();
                    string defaultKey = gamepad ? Session.GetGamepadDefault(action.Id, slot, SelectedFamilyIndex) : Session.GetKeyboardDefault(action.Id, slot);
                    button.DefaultKeys = !gamepad || bindings.Length == 0 ? defaultKey == null ? null : new[] { defaultKey }
                        : bindings.Any(r => r.DefaultKey == null) ? null : bindings.Select(r => r.DefaultKey).Distinct().ToArray();
                    button.ActionLevelDefault = false;
                    button.UsesDefault = gamepad ? bindings.Length == 0 || bindings.All(r => r.Key == r.DefaultKey) : button.Value == null;
                    button.Changed = gamepad ? bindings.Any(r => r.Changed) : Session.IsKeyboardChanged(action.Id, slot);
                    button.Mixed = bindings.Select(r => r.Key).Distinct().Skip(1).Any();
                    button.Unavailable = gamepad ? !Session.HasGamepadSlot(action.Id, slot, SelectedFamilyIndex) : !Session.HasKeyboardSlot(action.Id, slot);
                    button.Enabled = !Session.IsResetPending && !button.Unavailable;
                    button.AccessibleName = action.Label + " " + I18n.Str("editor.binding", slot + 1) + ": " + button.DisplayText + " · " + button.StatusText;
                    SetTip(button, action.Id + "\n" + button.DisplayText + " · " + button.StatusText
                        + (!gamepad && button.UsesDefault ? "\n" + I18n.Str("editor.keyboarddefaulthelp") : gamepad && button.Mixed ? "\n" + I18n.Str("editor.mixedhelp") : ""));
                    button.Invalidate();
                }
                var relation = snapshot.Current.First(r => r.Action == action.Id);
                var label = (ActionNameLabel)labels[row];
                label.Text = BindingViewText.ActionName(action.Id);
                label.KeySummary = I18n.Str("help.keysummary", BindingViewText.Keys(relation.Keys));
                SetTip(label, action.Id + "\n" + label.KeySummary); label.Invalidate();
                defaults[row].Text = relation.DefaultKeys == null ? I18n.Str("editor.defaultunknown") : BindingViewText.Keys(relation.DefaultKeys);
                SetTip(defaults[row], I18n.Str("views.factory", defaults[row].Text));
                Color rowColor = buttons[action.Id].Any(b => b.Changed) ? EditorTheme.Preview : row % 2 == 0 ? EditorTheme.Surface : EditorTheme.RowAlternate;
                label.BackColor = defaults[row].BackColor = rowColor;
                foreach (var button in buttons[action.Id]) if (button.BackColor != rowColor) button.BackColor = rowColor;
            }
        }

        void UpdateGameState()
        {
            if (IsApplying) return;
            bool running = Session != null && !Session.IsOffline && EditorSession.IsGameRunning;
            if (running == lastRunning) { RenderGameStatus(); return; }
            lastRunning = running;
            RenderGameStatus();
        }

        void RenderGameStatus()
        {
            RefreshCompanionHotkey();
            bool loaded = Session != null, offline = loaded && Session.IsOffline;
            bool gameRunning = loaded && !offline && lastRunning;
            var application = BindingApplication();
            lblScope.Text = !loaded ? "" : PendingBindingSummary();
            SetTip(lblScope, lblScope.Text);
            lblScope.ForeColor = loaded && (Session.HasChanges || ResourceModPending || KeyEnhancementPending) ? BindingStatus.Pending : EditorTheme.Muted;
            var liveStatus = application.Connection;
            bool liveConfigured = gameRunning && liveStatus != null;
            bool live = application.Live;
            bool waiting = liveConfigured && liveStatus.Waiting;
            bool neverConnected = liveConfigured && liveStatus.NeverConnected;
            var diagnosticStatus = liveStatus ?? (!offline && keyEnhancementBaseline.Enabled && keyEnhancementInstallation != null
                ? keyEnhancementInstallation.LiveStatus() : null);
            runtimeFeedback = diagnosticStatus == null ? null : diagnosticStatus.ErrorDetail;
            RefreshFeedback();
            if (liveConfigured)
            {
                RenderLiveEnhancementStatus(liveStatus);
            }
            lblGame.Text = I18n.Str(offline ? "offline.mode" : neverConnected ? "flow.notconnected" : waiting ? "live.waiting" : live
                ? liveStatus.RoutingEnabled ? "live.connected" : "live.paused" : lastRunning ? "editor.running" : "editor.closed");
            lblGame.ForeColor = offline ? EditorTheme.Muted : live && liveStatus.RoutingEnabled ? EditorTheme.LiveConnected
                : neverConnected ? EditorTheme.Muted : waiting ? Color.FromArgb(240, 178, 78) : lastRunning ? EditorTheme.Amber : EditorTheme.Green;
            SetTip(lblGame, (liveConfigured && !liveStatus.Suspended ? liveStatus.Detail : lblGame.Text) + "\n" + I18n.Str("flow.connection.open"));
            ((ApplyActionButton)btnApply).GameRunning = gameRunning && !live && !neverConnected;
            ((ApplyActionButton)btnApply).LiveConnected = live && liveStatus.RoutingEnabled;
            btnApply.Text = KeyEnhancementUpdatePending ? I18n.Str("enhancement.update.button")
                : I18n.Str(offline ? HasKeyEnhancementConfiguration ? "enhancement.exportbundle" : "offline.export" : live ? "live.apply" : neverConnected ? "flow.notconnected" : waiting ? "live.waiting" : gameRunning ? "editor.applyrunning" : "editor.apply");
            btnApply.Enabled = application.CanApply;
            SetTip(btnApply, runtimeFeedback != null ? I18n.Str("flow.failed.details") : KeyEnhancementUpdatePending ? KeyEnhancementUpdateHint : application.Reason ?? btnApply.Text);
            chkActivate.Enabled = !EnhancedSchemes && !gameRunning && !IsApplying;
            if (EnhancedSchemes) RenderActiveEnhancedScheme();
            UpdateResourceModAvailability();
            lblStatus.Text = loaded && Session.Conflicts.Length > 0 ? I18n.Str("editor.conflictinfo")
                : messageError && messageKey != null ? I18n.Str("flow.failed.details")
                : runtimeFeedback != null ? I18n.Str("flow.failed.details")
                : KeyEnhancementUpdatePending ? KeyEnhancementUpdateHint
                : live && (messageKey == "live.saved" || messageKey == "flow.saved.deferred") && !application.CanApply
                    ? I18n.Str(HasDeferredComponents ? "flow.saved.deferred" : "live.saved")
                : application.Reason != null ? application.Reason
                : live ? !liveStatus.RoutingEnabled ? liveStatus.Detail : I18n.Str("live.ready")
                : waiting || neverConnected ? liveStatus.Detail
                : gameRunning ? I18n.Str("editor.exittoapply") : loaded && Session.IsResetPending ? I18n.Str("editor.resetinfo")
                : loaded && !EnhancedSchemes && Session.SchemeDiscoveryRequired && SchemeProbePending ? I18n.Str("editor.discovering")
                : loaded && !EnhancedSchemes && Session.SchemeError != null ? I18n.Str("editor.schemeunknown")
                + (messageKey != null && messageKey != "editor.refreshed" ? " " + I18n.Str(messageKey, messageArgs) : "")
                : messageKey != null ? I18n.Str(messageKey, messageArgs) : I18n.Str(offline ? "offline.help" : "editor.ready");
            lblStatus.ForeColor = messageError ? Color.FromArgb(238, 171, 156) : liveConfigured
                ? live && liveStatus.RoutingEnabled ? EditorTheme.LiveConnected : neverConnected ? EditorTheme.Muted
                : waiting ? Color.FromArgb(240, 178, 78) : EditorTheme.Amber : EditorTheme.Muted;
            SetTip(lblStatus, lblStatus.Text);
            lblStatus.Visible = !(messageError || runtimeFeedback != null) || feedbackDismissed;
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
            RememberBindingEdit();
            try { action(); messageKey = null; messageError = false; feedbackPanel.Visible = false; }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            DiscardUnchangedBindingEdit();
            RenderSession();
        }
        void SetMessage(string key, bool error, params object[] args)
        {
            messageKey = key; messageArgs = args; messageError = error;
            if (error) feedbackDismissed = false;
            if (feedbackPanel != null) RefreshFeedback();
        }
        public void SelectProfile(string profile)
        {
            if (Session == null) return;
            TryAction(delegate {
                if (EnhancedSchemes) { keyEnhancement.SelectScheme(profile); AlignSchemeBaseline(); }
                else Session.SwitchProfile(profile);
            });
        }
        public void ShowGamepad(bool show) { gamepad = show; RenderSession(); }
        public void RefreshFromDisk() { RefreshFromDisk(true); }

        void RefreshFromDisk(bool allowDiscoveryRetry)
        {
            if (loading || modalActive || IsApplying) return;
            if (Session == null) { AutoFind(false); return; }
            if (Session.IsOffline) return;
            bool changed = true;
            var previousModState = footerModState;
            bool previousModReady = footerModReady;
            string previousModError = footerModError;
            var previousEnhancementState = keyEnhancementState;
            bool previousRepair = keyEnhancementRepair;
            string previousEnhancementError = keyEnhancementError;
            try
            {
                changed = Session.RefreshIfChanged() || keyEnhancementInstallation != null && keyEnhancementInstallation.ConfigurationChanged;
                if (allowDiscoveryRetry || changed) SetMessage("editor.refreshed", false);
            }
            catch (Exception ex) { SetMessage("editor.refreshfail", true, ex.Message); }
            RefreshFooterResourceMod(true);
            RefreshKeyEnhancement(true);
            if (!allowDiscoveryRetry && !changed && previousModState == footerModState && previousModReady == footerModReady
                && previousModError == footerModError && previousEnhancementState == keyEnhancementState
                && previousRepair == keyEnhancementRepair && previousEnhancementError == keyEnhancementError)
            { RenderGameStatus(); StartSchemeDiscovery(false); return; }
            RenderSession();
            StartSchemeDiscovery(allowDiscoveryRetry);
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
            finally { EndModal(); RenderSession(); }
        }
        bool ConfirmDiscard()
        {
            if (!HasPendingEdits) return true;
            modalActive = true;
            try
            {
                using (var dialog = new PendingEditsDialog(PendingBindingSummary, async delegate {
                    if (!await ApplyChangesAsync()) return false;
                    return Session.IsOffline ? !Session.Profiles.Any(profile => profile != Session.SelectedProfile && Session.HasProfileDraft(profile)) : !HasPendingEdits;
                }, delegate { return BindingApplication().CanApply; }, Session.IsOffline))
                    return dialog.ShowDialog(this) == DialogResult.OK;
            }
            finally { EndModal(); }
        }
        void Browse()
        {
            if (!ConfirmDiscard()) return;
            using (var dialog = new OpenFileDialog { Filter = "EnhancedInputUserSettings.sav / *.zip|EnhancedInputUserSettings.sav;*.zip|*.sav|*.sav|*.zip|*.zip|All files|*.*", Title = I18n.Str("editor.choose") })
            {
                modalActive = true;
                try
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (string.Equals(Path.GetExtension(dialog.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
                            TryAction(delegate { ImportKeyEnhancementBundle(dialog.FileName); });
                        else OpenPath(dialog.FileName);
                    }
                }
                finally { EndModal(); }
            }
        }
        void OpenPath(string path, bool remember = true)
        {
            TryAction(delegate {
                Session = EditorSession.Open(path, false);
                UpdateGameState();
                if (remember) preferences.SaveInputPath(Session.InputPath);
            });
            StartSchemeDiscovery();
        }
        public void NewOfflineScheme()
        {
            if (!ConfirmDiscard()) return;
            TryAction(delegate { Session = EditorSession.CreateOffline(); UpdateGameState(); });
        }
        void AutoFind(bool confirm)
        {
            if (confirm && !ConfirmDiscard()) return;
            try
            {
                var found = SaveDiscovery.Open(preferences, !confirm, false);
                if (found != null) { Session = found; UpdateGameState(); messageKey = null; messageError = false; RenderSession(); }
                else if (Session == null) NewOfflineScheme();
                else { SetMessage("editor.empty", false); RenderSession(); }
            }
            catch (Exception ex) { if (Session == null) NewOfflineScheme(); SetMessage("editor.failed", true, ex.Message); RenderSession(); }
            StartSchemeDiscovery();
        }

        void ShowMore()
        {
            if (moreMenu == null) moreMenu = CreateMoreMenu();
            foreach (ToolStripItem item in moreMenu.Items)
                if (item is ToolStripMenuItem) item.Text = I18n.Str(item.Name);
            var comparison = (ToolStripMenuItem)moreMenu.Items["views.schemediff"];
            foreach (ToolStripMenuItem choice in comparison.DropDownItems)
            {
                choice.Text = I18n.Str(choice.Name);
                choice.Checked = schemeDiff.CompareFactory == (choice.Name == "views.vsfactory");
            }
            moreMenu.Items["editor.resetall"].Enabled = Session != null && !Session.IsResetPending;
            moreMenu.Items["flow.restore.live"].Visible = Session != null && !Session.IsOffline && lastRunning && keyEnhancementBaseline.Enabled;
            moreMenu.Show(btnMore, new Point(btnMore.Width - moreMenu.PreferredSize.Width, btnMore.Height));
        }

        ContextMenuStrip CreateMoreMenu()
        {
            var menu = new ContextMenuStrip { Font = Font, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink,
                Renderer = new EditorMenuRenderer(), ShowImageMargin = false, Padding = new Padding(5) };
            menu.Items.Add(I18n.Str("offline.new"), null, delegate { NewOfflineScheme(); }).Name = "offline.new";
            menu.Items.Add(I18n.Str("editor.changefile"), null, delegate { Browse(); }).Name = "editor.changefile";
            menu.Items.Add(I18n.Str("editor.rediscover"), null, delegate { AutoFind(true); }).Name = "editor.rediscover";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.Str("ai.title"), null, delegate { ShowAgentHelp(); }).Name = "ai.title";
            menu.Items.Add(I18n.Str("editing.managecustom"), null, delegate { ManageCustomFunctions(); }).Name = "editing.managecustom";
            menu.Items.Add(I18n.Str("live.overlay"), null, delegate { ToggleCompanion(); }).Name = "live.overlay";
            var companionShortcut = new ToolStripMenuItem(I18n.Str("live.hotkey")) { Name = "live.hotkey", Checked = companionHotkeyEnabled,
                ToolTipText = companionHotkeyConflict ? I18n.Str("live.hotkeyconflict") : "Ctrl+Alt+K" };
            companionShortcut.Click += delegate {
                TryAction(delegate {
                    preferences.SaveCompanionHotkey(!companionHotkeyEnabled);
                    companionHotkeyEnabled = !companionHotkeyEnabled;
                    RefreshCompanionHotkey();
                    if (companionHotkeyEnabled && companionHotkeyConflict) SetMessage("live.hotkeyconflict", false);
                });
            };
            menu.Items.Add(companionShortcut);
            menu.Items.Add(I18n.Str("flow.restore.live"), null, delegate {
                if (Session == null) return;
                modalActive = true;
                try
                {
                    if (EditorDialogs.Show(this, I18n.Str("flow.restore.confirm"), I18n.Str("flow.restore.live"), MessageBoxButtons.OKCancel) == DialogResult.OK)
                        TryAction(RestoreLiveBindings);
                }
                finally { EndModal(); }
            }).Name = "flow.restore.live";
            var comparison = new ToolStripMenuItem(I18n.Str("views.schemediff")) { Name = "views.schemediff" };
            foreach (bool factoryComparison in new[] { false, true })
            {
                bool factory = factoryComparison;
                string key = factory ? "views.vsfactory" : "views.vssaved";
                var choice = new ToolStripMenuItem(I18n.Str(key)) { Name = key, Checked = schemeDiff.CompareFactory == factory };
                choice.Click += delegate { schemeDiff.SetComparison(factory); bindingWorkspace.PerformLayout(); };
                comparison.DropDownItems.Add(choice);
            }
            menu.Items.Add(comparison);
            var reset = menu.Items.Add(I18n.Str("editor.resetall"), null, delegate
            {
                if (Session == null) return;
                modalActive = true;
                try
                {
                    if (EditorDialogs.Show(this, I18n.Str("editor.resetconfirm"), I18n.Str("editor.resetall"), MessageBoxButtons.OKCancel) == DialogResult.OK)
                        TryAction(ResetAllBindings);
                }
                finally { EndModal(); }
            });
            reset.Name = "editor.resetall";
            menu.Items.Add(I18n.Str("editor.openbackup"), null, delegate
            {
                try { Process.Start("explorer.exe", GameSchemeSettings.BackupDirectory()); }
                catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); RenderSession(); }
            }).Name = "editor.openbackup";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.Str("app.about"), null, delegate { ShowApplicationAbout(); }).Name = "app.about";
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
            modalActive = true;
            try
            {
                using (var dialog = new UserPresetDialog(UserPresetStore.Default()))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.AppliedPreset != null)
                        ConfirmPresetLoad(dialog.AppliedPreset);
                }
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally
            {
                EndModal(); loading = true;
                try { PopulatePresets(); } finally { loading = false; RenderSession(); }
            }
        }

        void ConfirmPresetLoad(Preset preset)
        {
            if (Session == null) NewOfflineScheme();
            if (Session == null) return;
            using (var dialog = new PresetLoadDialog(preset, presetMode, delegate(PresetApplyMode mode) {
                var plan = PlanBindingPreset(preset, mode);
                var impact = PresetImpact.Create(Session, keyEnhancement, plan);
                impact.Accept = delegate { StageReviewedBindingPreset(preset, mode, plan); };
                return impact;
            }))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                presetMode = dialog.ChosenMode; preferences.SavePresetMode(presetMode);
                btnApply.Select();
            }
        }

        public void UiSmoke(string path) { OpenPath(path, false); }
        public void UiOffline() { NewOfflineScheme(); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (backgroundCache == null || backgroundCache.Size != ClientSize)
            {
                if (backgroundCache != null) backgroundCache.Dispose();
                backgroundCache = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(backgroundCache)) EditorArtwork.PaintBackground(graphics, ClientRectangle);
            }
            e.Graphics.DrawImageUnscaled(backgroundCache, 0, 0);
            PaintFlameBackground(this, e.Graphics);
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
            if (disposing) { if (keyEnhancementFlame != null) keyEnhancementFlame.Dispose(); gameTimer.Dispose(); tip.Dispose(); if (presetHoverPreview != null) presetHoverPreview.Dispose(); if (moreMenu != null) moreMenu.Dispose(); if (keyEnhancementMenu != null) keyEnhancementMenu.Dispose(); if (resourceModMenu != null) resourceModMenu.Dispose(); if (backgroundCache != null) backgroundCache.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
