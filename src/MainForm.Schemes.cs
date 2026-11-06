using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        readonly Panel schemeViewport = new EditorPanel(), schemeStrip = new EditorPanel();
        readonly EditorButton btnAddScheme = new EditorButton(), btnSaveSchemePreset = new EditorButton();
        readonly EditorButton btnSchemeMore = new EditorButton(), btnSchemeLeft = new EditorButton(), btnSchemeRight = new EditorButton();
        readonly EditorButton btnSchemeOptions = new EditorButton();
        readonly Dictionary<string, string> schemePresetNames = new Dictionary<string, string>();
        readonly Dictionary<string, string> schemePresetSelections = new Dictionary<string, string>();
        Panel schemeBar;
        int schemeOffset;
        string schemeSelection;
        bool arrangingSchemes;
        string editingScheme;

        bool EnhancedSchemes { get { return keyEnhancement.Enabled && keyEnhancement.Schemes.Count > 0; } }
        string EditingProfile { get { return EnhancedSchemes ? keyEnhancement.SelectedSchemeId : Session.SelectedProfile; } }
        string SelectedPresetId
        {
            get { string id; return EnhancedSchemes ? schemePresetSelections.TryGetValue(EditingProfile, out id) ? id : null : Session.PresetId; }
        }
        string EnhancedSchemeLabel(string id)
        {
            var item = keyEnhancement.Schemes.FirstOrDefault(scheme => scheme.Id == id);
            return item == null ? "—" : KeyEnhancementConfiguration.SchemeName(item);
        }

        Control BuildSchemeBar()
        {
            schemeBar = new EditorPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = EditorTheme.Surface };
            schemeViewport.Controls.Add(schemeStrip); schemeViewport.Margin = Padding.Empty;
            schemeViewport.MouseWheel += delegate(object sender, MouseEventArgs e) { schemeOffset = Math.Max(0, schemeOffset - e.Delta); LayoutSchemes(false); };
            btnAddScheme.Name = "addScheme"; btnAddScheme.Text = "+"; btnAddScheme.Primary = true; btnAddScheme.AccentColor = EditorTheme.KeyEnhancementStrong;
            btnAddScheme.Click += delegate { AddEnhancedScheme(); };
            btnSaveSchemePreset.Name = "saveSchemePreset"; btnSaveSchemePreset.Click += delegate { SaveSchemePreset(); };
            btnSchemeOptions.Name = "schemeOptions"; btnSchemeOptions.Text = "•••";
            btnSchemeOptions.Click += delegate { if (EnhancedSchemes) ShowSchemeMenu(btnSchemeOptions, keyEnhancement.SelectedSchemeId); };
            btnSchemeMore.Text = "•••"; btnSchemeMore.Name = "moreSchemes";
            btnSchemeMore.Click += delegate {
                var menu = new ContextMenuStrip();
                foreach (var tab in schemeTabs)
                {
                    var item = menu.Items.Add(tab.Text); item.Tag = tab.Tag;
                    ((ToolStripMenuItem)item).Checked = tab.Selected;
                    item.Click += delegate { SelectProfile((string)item.Tag); };
                }
                menu.Closed += delegate { menu.Dispose(); }; menu.Show(btnSchemeMore, new Point(0, btnSchemeMore.Height));
            };
            btnSchemeLeft.Text = "‹"; btnSchemeRight.Text = "›";
            btnSchemeLeft.Click += delegate { schemeOffset = Math.Max(0, schemeOffset - schemeViewport.Width / 2); LayoutSchemes(false); };
            btnSchemeRight.Click += delegate { schemeOffset += schemeViewport.Width / 2; LayoutSchemes(false); };
            schemeBar.Controls.AddRange(new Control[] { schemeViewport, btnAddScheme, btnSaveSchemePreset, btnSchemeOptions, btnSchemeMore, btnSchemeLeft, btnSchemeRight });
            schemeBar.Resize += delegate { LayoutSchemes(true); };
            return schemeBar;
        }

        void AlignSchemeBaseline()
        {
            string scope = EnhancedSchemes ? "enhanced:" + keyEnhancement.SelectedSchemeId : "native:" + (Session == null ? "" : Session.SelectedProfile);
            if (editingScheme != scope) { bindingWorkspaces.Clear(); editingScheme = scope; }
            if (!EnhancedSchemes) return;
            if (keyEnhancementBaseline.Schemes.Any(item => item.Id == keyEnhancement.SelectedSchemeId)
                && keyEnhancementBaseline.SelectedSchemeId != keyEnhancement.SelectedSchemeId)
                keyEnhancementBaseline.SelectScheme(keyEnhancement.SelectedSchemeId);
        }

        void RenderSchemeBar()
        {
            var ids = EnhancedSchemes ? keyEnhancement.Schemes.Select(item => item.Id).ToArray()
                : Session == null ? new string[0] : Session.Profiles;
            if (!schemeTabs.Select(tab => (string)tab.Tag).SequenceEqual(ids))
            {
                foreach (var tab in schemeTabs) tab.Dispose();
                schemeTabs = ids.Select(id => {
                    var tab = new EditorTab { Tag = id, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 11f) };
                    tab.Click += delegate { SelectProfile(id); };
                    tab.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowSchemeMenu(tab, id); };
                    schemeStrip.Controls.Add(tab); return tab;
                }).ToArray();
            }
            foreach (var tab in schemeTabs)
            {
                string id = (string)tab.Tag;
                tab.Text = EnhancedSchemes ? EnhancedSchemeLabel(id) : SchemeLabel(id);
                tab.Selected = EnhancedSchemes ? id == keyEnhancement.SelectedSchemeId : id == Session.SelectedProfile;
                tab.Badge = !Session.IsOffline && id == (EnhancedSchemes ? keyEnhancement.ActiveSchemeId : Session.ActiveProfile) ? I18n.Str("editor.current") : null;
                tab.AccentColor = EnhancedSchemes ? EditorTheme.KeyEnhancement : Color.Empty;
                if (EnhancedSchemes)
                {
                    var previous = keyEnhancementBaseline.Schemes.FirstOrDefault(item => item.Id == id);
                    tab.Dirty = previous == null || keyEnhancement.ConfigurationFor(id).ToJson() != keyEnhancementBaseline.ConfigurationFor(id).ToJson();
                }
                else tab.Dirty = Session.HasProfileDraft(id);
                tab.Enabled = !IsApplying; tab.Invalidate();
            }
            btnAddScheme.Enabled = EnhancedSchemes && !IsApplying;
            btnSchemeOptions.Enabled = EnhancedSchemes && !IsApplying && !keyEnhancement.Schemes.First(item => item.Id == keyEnhancement.SelectedSchemeId).BuiltIn;
            btnSchemeOptions.AccessibleName = I18n.Str("component.options"); SetTip(btnSchemeOptions, btnSchemeOptions.AccessibleName);
            btnSaveSchemePreset.Enabled = Session != null && !IsApplying;
            btnSaveSchemePreset.Text = I18n.Str("schemes.savepreset");
            btnAddScheme.AccessibleName = I18n.Str("schemes.add"); SetTip(btnAddScheme, I18n.Str(EnhancedSchemes ? "schemes.add" : "schemes.requires"));
            btnSchemeMore.AccessibleName = I18n.Str("schemes.more"); SetTip(btnSchemeMore, btnSchemeMore.AccessibleName);
            string selection = EnhancedSchemes ? keyEnhancement.SelectedSchemeId : Session == null ? null : Session.SelectedProfile;
            bool reveal = selection != schemeSelection; schemeSelection = selection;
            LayoutSchemes(reveal);
        }

        void LayoutSchemes(bool reveal)
        {
            if (schemeBar == null || arrangingSchemes) return;
            arrangingSchemes = true;
            try
            {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                int height = schemeBar.Height, small = (int)(34 * scale), gap = (int)(4 * scale);
                int saveWidth = TextRenderer.MeasureText(btnSaveSchemePreset.Text, Font).Width + (int)(26 * scale);
                btnSaveSchemePreset.SetBounds(Math.Max(0, schemeBar.Width - saveWidth), 0, saveWidth, height);
                btnSchemeOptions.SetBounds(btnSaveSchemePreset.Left - gap - small, 0, small, height);
                btnAddScheme.SetBounds(btnSchemeOptions.Left - gap - small, 0, small, height);
                int available = Math.Max(1, btnAddScheme.Left - gap);
                int[] widths = schemeTabs.Select(tab => Math.Max((int)(128 * scale), TextRenderer.MeasureText(tab.Text, tab.Font).Width
                    + (int)((string.IsNullOrEmpty(tab.Badge) ? 60 : 124) * scale))).ToArray();
                bool overflow = widths.Sum() > available;
                btnSchemeMore.Visible = btnSchemeLeft.Visible = btnSchemeRight.Visible = overflow;
                if (overflow)
                {
                    btnSchemeMore.SetBounds(available - small, 0, small, height);
                    btnSchemeRight.SetBounds(btnSchemeMore.Left - small, 0, small, height);
                    btnSchemeLeft.SetBounds(btnSchemeRight.Left - small, 0, small, height);
                    available = Math.Max(1, btnSchemeLeft.Left);
                }
                schemeViewport.SetBounds(0, 0, available, height);
                if (!overflow && widths.Length > 0)
                    for (int i = 0; i < widths.Length; i++) widths[i] = available / widths.Length + (i == widths.Length - 1 ? available % widths.Length : 0);
                int left = 0;
                for (int i = 0; i < schemeTabs.Length; i++) { schemeTabs[i].SetBounds(left, 0, widths[i], height); left += widths[i]; }
                if (reveal)
                {
                    var selected = schemeTabs.FirstOrDefault(tab => tab.Selected);
                    if (selected != null) { if (selected.Left < schemeOffset) schemeOffset = selected.Left; if (selected.Right > schemeOffset + available) schemeOffset = selected.Right - available; }
                }
                schemeOffset = Math.Max(0, Math.Min(schemeOffset, left - available));
                schemeStrip.SetBounds(-schemeOffset, 0, Math.Max(left, available), height);
                btnSchemeLeft.Enabled = schemeOffset > 0; btnSchemeRight.Enabled = schemeOffset + available < left;
            }
            finally { arrangingSchemes = false; }
        }

        void AddEnhancedScheme()
        {
            if (!EnhancedSchemes) return;
            modalActive = true;
            try
            {
                using (var dialog = new SchemeNameDialog(I18n.Str("schemes.add"), "", null, false))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        TryAction(delegate { RememberBindingEdit(); keyEnhancement.AddScheme(dialog.Value); AlignSchemeBaseline(); });
            }
            finally { EndModal(); RenderSession(); }
        }

        void ShowSchemeMenu(Control owner, string id)
        {
            if (!EnhancedSchemes) return;
            var scheme = keyEnhancement.Schemes.First(item => item.Id == id);
            var menu = new ContextMenuStrip();
            var rename = menu.Items.Add(I18n.Str("schemes.rename")); rename.Enabled = !scheme.BuiltIn;
            rename.Click += delegate {
                modalActive = true;
                try
                {
                    using (var dialog = new SchemeNameDialog(I18n.Str("schemes.rename"), EnhancedSchemeLabel(id), null, false))
                        if (dialog.ShowDialog(this) == DialogResult.OK) TryAction(delegate { RememberBindingEdit(); keyEnhancement.RenameScheme(id, dialog.Value); });
                }
                finally { EndModal(); RenderSession(); }
            };
            var delete = menu.Items.Add(I18n.Str("schemes.delete")); delete.Enabled = !scheme.BuiltIn && id != keyEnhancement.ActiveSchemeId;
            delete.Click += delegate {
                if (MessageBox.Show(this, I18n.Str("schemes.deleteconfirm", EnhancedSchemeLabel(id)), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    TryAction(delegate { RememberBindingEdit(); keyEnhancement.DeleteScheme(id); AlignSchemeBaseline(); });
            };
            menu.Closed += delegate { menu.Dispose(); }; menu.Show(owner, new Point(0, owner.Height));
        }

        void SaveSchemePreset()
        {
            if (Session == null) return;
            modalActive = true;
            try
            {
                var store = UserPresetStore.Default(); var library = store.Load();
                string id = EnhancedSchemes ? keyEnhancement.SelectedSchemeId : Session.SelectedProfile, name;
                if (!schemePresetNames.TryGetValue(id, out name)) name = EnhancedSchemes ? EnhancedSchemeLabel(id) : SchemeLabel(id);
                using (var dialog = new SchemeNameDialog(I18n.Str("schemes.savepreset"), name, library.Select(item => item.Name).ToArray(), true))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var existing = library.FirstOrDefault(item => string.Equals(item.Name, dialog.Value.Trim(), StringComparison.CurrentCultureIgnoreCase));
                    if (existing != null && MessageBox.Show(this, I18n.Str("schemes.overwrite", existing.Name), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    var preset = UserPresetStore.Capture(Session, dialog.Value, HasKeyEnhancementConfiguration ? keyEnhancement : null);
                    preset = UserPresetStore.SelectScopes(preset, preset.Scopes.Where(scope => scope.Family == null ? dialog.Keyboard : dialog.Gamepad));
                    if (existing == null) store.Save(preset, dialog.Value); else store.Update(existing.Id, preset, dialog.Value);
                    schemePresetNames[id] = dialog.Value.Trim();
                    SetMessage("preset.saved", false);
                    loading = true; try { PopulatePresets(); } finally { loading = false; }
                }
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally { EndModal(); RenderSession(); }
        }

        void RenderActiveEnhancedScheme(KeyEnhancementLiveStatus status)
        {
            if (status != null && status.Available && keyEnhancement.Schemes.Any(item => item.Id == status.ActiveSchemeId))
            {
                keyEnhancement.ActiveSchemeId = status.ActiveSchemeId;
                if (keyEnhancementBaseline.Schemes.Any(item => item.Id == status.ActiveSchemeId)) keyEnhancementBaseline.ActiveSchemeId = status.ActiveSchemeId;
            }
            chkActivate.Text = I18n.Str("schemes.active", EnhancedSchemeLabel(keyEnhancement.ActiveSchemeId));
            foreach (var tab in schemeTabs) tab.Badge = !Session.IsOffline && (string)tab.Tag == keyEnhancement.ActiveSchemeId ? I18n.Str("editor.current") : null;
        }
    }

    internal sealed class SchemeNameDialog : EditorWindow
    {
        readonly ComboBox name = new EditorComboBox();
        readonly CheckBox keyboard = new EditorCheckBox(), gamepad = new EditorCheckBox();
        public string Value { get { return name.Text.Trim(); } }
        public bool Keyboard { get { return keyboard.Checked; } }
        public bool Gamepad { get { return gamepad.Checked; } }
        public SchemeNameDialog(string title, string initial, string[] suggestions, bool scope)
        {
            Text = title; Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, scope ? 174 : 128); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
            BackColor = EditorTheme.Surface; ForeColor = EditorTheme.Ink;
            var label = new EditorLabel { Text = I18n.Str(scope ? "preset.name" : "schemes.name"), Left = 16, Top = 14, Width = 400, Height = 24 };
            name.SetBounds(16, 42, 408, 30); name.DropDownStyle = ComboBoxStyle.DropDown; name.MaxLength = 80;
            if (suggestions != null) name.Items.AddRange(suggestions); name.Text = initial; name.Name = "schemeName";
            keyboard.Text = I18n.Str("editor.keyboard"); gamepad.Text = I18n.Str("editor.gamepad");
            keyboard.Checked = gamepad.Checked = true; keyboard.SetBounds(16, 80, 190, 28); gamepad.SetBounds(220, 80, 190, 28);
            keyboard.Visible = gamepad.Visible = scope;
            var ok = new EditorButton { Text = I18n.Str("preset.save"), Left = 320, Top = ClientSize.Height - 44, Width = 104, Height = 32, Primary = true };
            var cancel = new EditorButton { Text = I18n.Str("editor.cancel"), Left = 208, Top = ok.Top, Width = 104, Height = 32, DialogResult = DialogResult.Cancel };
            ok.Click += delegate { if (Value.Length > 0 && (!scope || Keyboard || Gamepad)) { DialogResult = DialogResult.OK; Close(); } else name.Focus(); };
            Controls.AddRange(new Control[] { label, name, keyboard, gamepad, cancel, ok }); AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { name.Focus(); name.SelectAll(); };
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
    }
}
