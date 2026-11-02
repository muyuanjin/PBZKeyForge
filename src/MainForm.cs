using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public static class KeyNames
    {
        public static readonly string[] Keyboard = new[] {
            "", "LeftMouseButton","RightMouseButton","MiddleMouseButton","ThumbMouseButton","ThumbMouseButton2",
            "LeftShift","RightShift","LeftControl","RightControl","LeftAlt","RightAlt","SpaceBar","Tab","CapsLock",
            "Escape","Enter","BackSpace","LeftBracket","RightBracket","Semicolon","Quote","Comma","Period","Slash","Backslash","Tilde",
            "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
            "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Nine",
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
            "Up","Down","Left","Right","Home","End","PageUp","PageDown","Insert","Delete",
            "NumPadZero","NumPadOne","NumPadTwo","NumPadThree","NumPadFour","NumPadFive","NumPadSix","NumPadSeven","NumPadEight","NumPadNine"
        };

        public static readonly string[] Gamepad = new[] {
            "", "Gamepad_FaceButton_Bottom","Gamepad_FaceButton_Right","Gamepad_FaceButton_Left","Gamepad_FaceButton_Top",
            "Gamepad_LeftShoulder","Gamepad_RightShoulder","Gamepad_LeftTrigger","Gamepad_RightTrigger",
            "Gamepad_LeftTriggerAxis","Gamepad_RightTriggerAxis","Gamepad_LeftThumbstick","Gamepad_RightThumbstick",
            "Gamepad_DPad_Up","Gamepad_DPad_Down","Gamepad_DPad_Left","Gamepad_DPad_Right",
            "Gamepad_Special_Left","Gamepad_Special_Right"
        };
    }

    public class ActionInfo
    {
        public string Id;
        public ActionInfo(string id) { Id = id; }
        public string Label { get { return I18n.Str("act." + Id) + "   " + Id; } }
        public override string ToString() { return Label; }
    }

    public class MainForm : Form
    {
        public static readonly List<ActionInfo> Actions = new[]
        {
            "IA_LightAttack","IA_HeavyAttack","IA_Guard","IA_Dodge","IA_Jump","IA_Crouch","IA_MoveFast",
            "IA_LockTarget","IA_Interact","IA_Execute","IA_ShadowArm","IA_ShadowArm_LongPress",
            "IA_SwitchWeapon","IA_SwitchShadowArm","IA_UltimateSkill","IA_Sense_Default",
            "IA_OpenCharacterMenu","IA_OpenMapMenu","IA_UseTopItem","IA_UseBottomItem"
        }.Select(s => new ActionInfo(s)).ToList();

        TextBox txtPath = new TextBox();
        ComboBox cbLang = new ComboBox();
        ComboBox cbScheme = new ComboBox();
        Button btnActivate = new Button();
        Label lblStatus = new Label();
        Label lblWarn = new Label();
        Label lblFile = new Label();
        Label lblScheme = new Label();
        Label lblPreset = new Label();
        TabControl tabs = new TabControl();
        TabPage tabKb = new TabPage(), tabPad = new TabPage();
        Dictionary<string, ComboBox[]> kbBoxes = new Dictionary<string, ComboBox[]>();
        Dictionary<string, ComboBox[]> padBoxes = new Dictionary<string, ComboBox[]>();
        ComboBox cbPreset = new ComboBox();
        Button btnBrowse = new Button(), btnAuto = new Button(), btnLoad = new Button();
        Button btnApply = new Button(), btnSave = new Button(), btnAbout = new Button();

        const string NoKey = "—";
        Label[] stepLabels;
        Button btnQuick = new Button();
        int step = 0;
        SaveFile sav;
        string selectedProfile;
        bool loading;

        public MainForm()
        {
            I18n.Current = I18n.Detect();
            Build();
            ApplyLanguage();
            FormClosing += delegate { };
        }

        void Build()
        {
            Width = 1040; Height = 780;
            MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = true;

            var top = new Panel { Dock = DockStyle.Top, Height = 176 };

            lblFile.Left = 12; lblFile.Top = 14; lblFile.Width = 96;
            txtPath.Left = 112; txtPath.Top = 11; txtPath.Width = 600;
            btnBrowse.Left = 720; btnBrowse.Top = 9; btnBrowse.Width = 80;
            btnBrowse.Click += delegate { Browse(); };
            btnAuto.Left = 806; btnAuto.Top = 9; btnAuto.Width = 96;
            btnAuto.Click += delegate { AutoFind(); };
            btnLoad.Left = 908; btnLoad.Top = 9; btnLoad.Width = 96;
            btnLoad.Click += delegate { LoadFile(); };
            cbLang.Left = 908; cbLang.Top = 44; cbLang.Width = 96;
            cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var l in I18n.Languages) cbLang.Items.Add(I18n.DisplayName(l));
            cbLang.SelectedIndexChanged += delegate
            {
                if (cbLang.SelectedIndex < 0) return;
                I18n.Current = I18n.Languages[cbLang.SelectedIndex];
                ApplyLanguage();
            };

            lblScheme.Left = 12; lblScheme.Top = 48; lblScheme.Width = 96;
            cbScheme.Left = 112; cbScheme.Top = 45; cbScheme.Width = 380;
            cbScheme.DropDownStyle = ComboBoxStyle.DropDownList;
            cbScheme.SelectedIndexChanged += delegate { OnSchemeChanged(); };
            btnActivate.Left = 500; btnActivate.Top = 43; btnActivate.Width = 150;
            btnActivate.Click += delegate { ActivateScheme(); };
            btnAbout.Left = 660; btnAbout.Top = 43; btnAbout.Width = 100;
            btnAbout.Click += delegate { MessageBox.Show(I18n.Str("about.text"), I18n.Str("about.title")); };

            lblWarn.Left = 112; lblWarn.Top = 78; lblWarn.Width = 900; lblWarn.Height = 44;
            lblWarn.ForeColor = Color.Firebrick;

            // Numbered step strip: 1 find the save, 2 pick a preset, 3 apply, 4 save.
            stepLabels = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                var l = new Label
                {
                    AutoSize = true,
                    Left = 12 + i * 190,
                    Top = 136,
                    Font = new Font("Microsoft YaHei UI", 10f)
                };
                stepLabels[i] = l;
                top.Controls.Add(l);
            }

            Controls.Add(tabs);

            top.Controls.AddRange(new Control[] { lblFile, txtPath, btnBrowse, btnAuto, btnLoad,
                lblScheme, cbScheme, btnActivate, btnAbout, cbLang, lblWarn });
            Controls.Add(top);

            BuildKbTab();
            BuildPadTab();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(tabKb);
            tabs.TabPages.Add(tabPad);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 104 };

            // The one thing a first-time user needs: guard on the attack key, in one click.
            btnQuick.Left = 12; btnQuick.Top = 8; btnQuick.Width = 260; btnQuick.Height = 44;
            btnQuick.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            btnQuick.BackColor = Color.FromArgb(204, 65, 37);
            btnQuick.ForeColor = Color.White;
            btnQuick.FlatStyle = FlatStyle.Flat;
            btnQuick.FlatAppearance.BorderSize = 0;
            btnQuick.Click += delegate { QuickOneKey(); };

            btnSave.Left = 284; btnSave.Top = 8; btnSave.Width = 150; btnSave.Height = 44;
            btnSave.Font = new Font("Microsoft YaHei UI", 10f);
            btnSave.Click += delegate { SaveAll(); };

            lblPreset.Left = 452; lblPreset.Top = 22; lblPreset.Width = 60;
            cbPreset.Left = 516; cbPreset.Top = 19; cbPreset.Width = 330;
            cbPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            btnApply.Left = 854; btnApply.Top = 17; btnApply.Width = 110; btnApply.Height = 28;
            btnApply.Click += delegate { ApplyPreset(); };

            lblStatus.Left = 12; lblStatus.Top = 68; lblStatus.Width = 990; lblStatus.Height = 28;

            bottom.Controls.AddRange(new Control[] { btnQuick, btnSave, lblPreset, cbPreset, btnApply, lblStatus });
            Controls.Add(bottom);

            Shown += delegate { UpdateSteps(); if (sav == null) AutoFind(); };
        }

        /// <summary>Highlight where the user is in the four-step flow.</summary>
        void UpdateSteps()
        {
            if (stepLabels == null) return;
            string[] names = { I18n.Str("step.find"), I18n.Str("step.preset"), I18n.Str("step.apply"), I18n.Str("step.save") };
            for (int i = 0; i < stepLabels.Length; i++)
            {
                bool active = i == step;
                stepLabels[i].Text = (i + 1) + ". " + names[i];
                stepLabels[i].Font = new Font("Microsoft YaHei UI", 10f, active ? FontStyle.Bold : FontStyle.Regular);
                stepLabels[i].ForeColor = active ? Color.FromArgb(204, 65, 37) : SystemColors.GrayText;
            }
        }

        void OnKeyTextChanged(object sender, EventArgs e)
        {
            var cb = sender as ComboBox;
            if (cb == null) return;
            cb.ForeColor = KeyOf(cb) == "" ? SystemColors.GrayText : SystemColors.WindowText;
        }

        /// <summary>Apply the guard-on-attack preset and tell the user the last step.</summary>
        void QuickOneKey()
        {
            if (sav == null) { lblStatus.Text = I18n.Str("status.needfile"); step = 0; UpdateSteps(); return; }
            var preset = Presets.All().FirstOrDefault(p => p.Id == "onekey");
            if (preset == null) return;
            var rec = sav.RecordFor(selectedProfile);
            if (rec == null) { lblStatus.Text = I18n.Str("status.needfile"); return; }
            Presets.Apply(sav, preset, rec);
            FillFromSave();
            step = 3;
            UpdateSteps();
            lblStatus.Text = I18n.Str("status.quick");
        }

        void BuildKbTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 4,
                Padding = new Padding(6)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.Controls.Add(Head("槽 0"), 1, 0);
            grid.Controls.Add(Head("槽 2"), 2, 0);
            grid.Controls.Add(Head("槽 4"), 3, 0);
            int row = 1;
            foreach (var a in Actions)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                var lbl = new Label { Text = a.Label, AutoSize = true, Anchor = AnchorStyles.Left };
                lbl.Tag = "action:" + a.Id;
                grid.Controls.Add(lbl, 0, row);
                var arr = new ComboBox[3];
                for (int i = 0; i < 3; i++)
                {
                    var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200, Anchor = AnchorStyles.Left, Text = NoKey, ForeColor = SystemColors.GrayText };
                    cb.Items.AddRange(KeyNames.Keyboard);
                    cb.TextChanged += OnKeyTextChanged;
                    arr[i] = cb;
                    grid.Controls.Add(cb, i + 1, row);
                }
                kbBoxes[a.Id] = arr;
                row++;
            }
            grid.RowCount = row;
            host.Controls.Add(grid);
            tabKb.Controls.Add(host);
        }

        void BuildPadTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 4,
                Padding = new Padding(6)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.Controls.Add(Head("Second"), 1, 0);
            grid.Controls.Add(Head("Fourth"), 2, 0);
            grid.Controls.Add(Head("Sixth"), 3, 0);
            int row = 1;
            foreach (var a in Actions)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                var lbl = new Label { Text = a.Label, AutoSize = true, Anchor = AnchorStyles.Left };
                lbl.Tag = "action:" + a.Id;
                grid.Controls.Add(lbl, 0, row);
                var arr = new ComboBox[3];
                for (int i = 0; i < 3; i++)
                {
                    var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200, Anchor = AnchorStyles.Left, Text = NoKey, ForeColor = SystemColors.GrayText };
                    cb.Items.AddRange(KeyNames.Gamepad);
                    cb.TextChanged += OnKeyTextChanged;
                    arr[i] = cb;
                    grid.Controls.Add(cb, i + 1, row);
                }
                padBoxes[a.Id] = arr;
                row++;
            }
            grid.RowCount = row;
            host.Controls.Add(grid);
            tabPad.Controls.Add(host);
        }

        static Label Head(string s) { return new Label { Text = s, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) }; }

        void ApplyLanguage()
        {
            Text = I18n.Str("app.title");
            lblFile.Text = I18n.Str("file.label");
            lblScheme.Text = "Scheme";
            btnBrowse.Text = I18n.Str("btn.browse");
            btnAuto.Text = I18n.Str("btn.autofind");
            btnLoad.Text = I18n.Str("btn.load");
            tabKb.Text = I18n.Str("tab.kb");
            tabPad.Text = I18n.Str("tab.pad");
            lblPreset.Text = I18n.Str("preset.label");
            btnApply.Text = I18n.Str("btn.apply");
            btnSave.Text = I18n.Str("btn.save");
            btnQuick.Text = I18n.Str("btn.quick");
            UpdateSteps();
            lblWarn.Text = I18n.Str("warn.editor");
            btnActivate.Text = I18n.Str("btn.activate");
            btnAbout.Text = I18n.Str("btn.about");
            lblStatus.Text = I18n.Str("status.hint");
            if (cbLang.SelectedIndex < 0)
                cbLang.SelectedIndex = Array.IndexOf(I18n.Languages, I18n.Current);
            RebuildPresetList();
            RebuildActionLabels();
            RebuildSchemeList();
        }

        void RebuildActionLabels()
        {
            foreach (var tab in new[] { tabKb, tabPad })
                foreach (var c in AllControls(tab))
                {
                    var lbl = c as Label;
                    if (lbl != null && lbl.Tag is string && ((string)lbl.Tag).StartsWith("action:"))
                        lbl.Text = I18n.Str("act." + ((string)lbl.Tag).Substring(7)) + "   " + ((string)lbl.Tag).Substring(7);
                }
        }

        static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var x in AllControls(c)) yield return x;
            }
        }

        void RebuildPresetList()
        {
            int keep = cbPreset.SelectedIndex;
            cbPreset.Items.Clear();
            foreach (var p in Presets.All()) cbPreset.Items.Add(p);
            cbPreset.SelectedIndex = keep >= 0 && keep < cbPreset.Items.Count ? keep : 0;
        }

        void RebuildSchemeList()
        {
            string keep = selectedProfile;
            cbScheme.Items.Clear();
            if (sav == null) return;
            foreach (var r in sav.Records)
            {
                string label = SchemeLabel(r.ProfileId) + (r.ProfileId == sav.ActiveProfile ? "  ★" : "");
                cbScheme.Items.Add(new SchemeItem { Id = r.ProfileId, Label = label });
            }
            int idx = 0;
            for (int i = 0; i < cbScheme.Items.Count; i++)
                if (((SchemeItem)cbScheme.Items[i]).Id == (keep ?? sav.ActiveProfile)) idx = i;
            cbScheme.SelectedIndex = idx;
        }

        static string SchemeLabel(string id)
        {
            if (id.EndsWith(".Default")) return I18n.Str("scheme.default");
            if (id.EndsWith("Custom1")) return I18n.Str("scheme.custom1");
            if (id.EndsWith("Custom2")) return I18n.Str("scheme.custom2");
            return id;
        }

        class SchemeItem
        {
            public string Id;
            public string Label;
            public override string ToString() { return Label; }
        }

        void Browse()
        {
            var d = new OpenFileDialog { Filter = "EnhancedInputUserSettings.sav|*.sav|All files|*.*" };
            if (d.ShowDialog() == DialogResult.OK) { txtPath.Text = d.FileName; LoadFile(); }
        }

        void AutoFind()
        {
            var hits = new List<string>();
            try
            {
                var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                var roots = new List<string>();
                if (!string.IsNullOrEmpty(steam)) roots.Add(steam.Replace('/', '\\'));
                foreach (var drv in DriveInfo.GetDrives())
                {
                    if (drv.DriveType != DriveType.Fixed) continue;
                    var p = Path.Combine(drv.Name, @"SteamLibrary\steamapps\common\PBZ");
                    if (Directory.Exists(p)) roots.Add(p);
                }
                foreach (var r in roots)
                {
                    var c1 = Path.Combine(r, @"steamapps\common\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
                    if (File.Exists(c1)) { hits.Add(c1); continue; }
                    var c2 = Path.Combine(r, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
                    if (File.Exists(c2)) hits.Add(c2);
                }
            }
            catch (Exception ex) { lblStatus.Text = ex.Message; return; }
            if (hits.Count == 0) { lblStatus.Text = I18n.Str("status.notfound"); return; }
            txtPath.Text = hits[0];
            LoadFile();
        }

        void LoadFile()
        {
            try
            {
                loading = true;
                sav = SaveFile.Load(txtPath.Text);
                selectedProfile = sav.ActiveProfile;
                RebuildSchemeList();
                FillFromSave();
                loading = false;
                step = 1; UpdateSteps();
                lblStatus.Text = I18n.Str("status.loaded", sav.ActiveProfile,
                    sav.ActiveRecord == null ? 0 : sav.ActiveRecord.Entries.Count, sav.GamepadKeys.Count);
            }
            catch (Exception ex) { lblStatus.Text = I18n.Str("status.loadfail", ex.Message); sav = null; }
        }

        void FillFromSave()
        {
            foreach (var kv in kbBoxes) foreach (var cb in kv.Value) { cb.Text = NoKey; cb.ForeColor = SystemColors.GrayText; }
            foreach (var kv in padBoxes) foreach (var cb in kv.Value) { cb.Text = NoKey; cb.ForeColor = SystemColors.GrayText; }
            if (sav == null) return;

            var rec = sav.RecordFor(selectedProfile);
            if (rec != null)
                foreach (var e in rec.Entries)
                {
                    ComboBox[] arr;
                    if (!kbBoxes.TryGetValue(e.Name, out arr)) continue;
                    int idx = Array.IndexOf(Presets.KeyboardSlots, (int)e.Slot);
                    if (idx >= 0) { arr[idx].Text = e.Key; arr[idx].ForeColor = SystemColors.WindowText; }
                }
            foreach (var keys in sav.GamepadKeys)
                foreach (var el in keys.Items)
                {
                    string mn = SaveFile.Field(el, "MappingName");
                    ComboBox[] arr;
                    if (mn == null || !padBoxes.TryGetValue(mn, out arr)) continue;
                    string slot = SaveFile.Field(el, "Slot");
                    if (slot == null) continue;
                    int idx = Array.IndexOf(Presets.GamepadSlots, slot.Replace("EPlayerMappableKeySlot::", ""));
                    if (idx >= 0 && (arr[idx].Text == NoKey || string.IsNullOrEmpty(arr[idx].Text)))
                    {
                        arr[idx].Text = SaveFile.Field(el, "Key");
                        arr[idx].ForeColor = SystemColors.WindowText;
                    }
                }
        }

        static string KeyOf(ComboBox cb)
        {
            string v = (cb.Text ?? "").Trim();
            return v == NoKey ? "" : v;
        }

        void OnSchemeChanged()
        {
            if (loading || sav == null || cbScheme.SelectedIndex < 0) return;
            CommitGrid();
            selectedProfile = ((SchemeItem)cbScheme.SelectedItem).Id;
            FillFromSave();
            lblStatus.Text = I18n.Str("status.scheme", SchemeLabel(selectedProfile));
        }

        /// <summary>Copy the grid into the in-memory record for the selected scheme.</summary>
        void CommitGrid()
        {
            if (loading || sav == null || selectedProfile == null) return;
            var rec = sav.RecordFor(selectedProfile);
            if (rec == null) return;

            var keep = rec.Entries.Where(e => !kbBoxes.ContainsKey(e.Name)).ToList();
            foreach (var a in Actions)
            {
                var arr = kbBoxes[a.Id];
                string v0 = KeyOf(arr[0]), v1 = KeyOf(arr[1]), v2 = KeyOf(arr[2]);
                if (v0 == "" && v1 == "" && v2 == "") continue;
                if (v0 == "") v0 = v1 != "" ? v1 : v2;
                if (v1 == "") v1 = v0;
                if (v2 == "") v2 = v0;
                string[] vals = { v0, v1, v2 };
                for (int k = 0; k < 3; k++)
                    keep.Add(new BlobEntry
                    {
                        Name = a.Id,
                        Key = vals[k],
                        X = "None",
                        Y = "None",
                        Slot = (byte)Presets.KeyboardSlots[k]
                    });
            }
            rec.Entries = keep;

            foreach (var keys in sav.GamepadKeys)
                foreach (var a in Actions)
                {
                    var arr = padBoxes[a.Id];
                    for (int k = 0; k < 3; k++)
                    {
                        string want = KeyOf(arr[k]);
                        if (want == "") continue;
                        string slotName = "EPlayerMappableKeySlot::" + Presets.GamepadSlots[k];
                        var el = keys.Items.FirstOrDefault(x =>
                            SaveFile.Field(x, "MappingName") == a.Id && SaveFile.Field(x, "Slot") == slotName);
                        if (el == null)
                        {
                            var src = keys.Items.FirstOrDefault(x => SaveFile.Field(x, "MappingName") == a.Id);
                            if (src == null) continue;
                            el = src.Select(SaveFile.CloneNode).ToList();
                            SaveFile.SetField(el, "Slot", slotName);
                            keys.Items.Add(el);
                        }
                        SaveFile.SetField(el, "Key", want);
                    }
                }
        }

        void ActivateScheme()
        {
            if (sav == null || cbScheme.SelectedIndex < 0) { lblStatus.Text = I18n.Str("status.needfile"); return; }
            string id = ((SchemeItem)cbScheme.SelectedItem).Id;
            sav.SetActiveProfile(id);
            RebuildSchemeList();
            lblStatus.Text = I18n.Str("status.activated", SchemeLabel(id));
        }

        void ApplyPreset()
        {
            if (sav == null) { lblStatus.Text = I18n.Str("status.needfile"); return; }
            var p = cbPreset.SelectedItem as Preset;
            if (p == null) return;
            var rec = sav.RecordFor(selectedProfile);
            if (rec == null) { lblStatus.Text = I18n.Str("status.needfile"); return; }
            Presets.Apply(sav, p, rec);
            FillFromSave();
            step = 3; UpdateSteps();
            lblStatus.Text = I18n.Str("status.applied");
        }

        void SaveAll()
        {
            if (sav == null) { lblStatus.Text = I18n.Str("status.needfile"); return; }
            if (System.Diagnostics.Process.GetProcessesByName("PBZ").Length > 0)
            { lblStatus.Text = I18n.Str("status.running"); return; }
            try
            {
                CommitGrid();
                string bak = txtPath.Text + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                if (File.Exists(txtPath.Text)) File.Copy(txtPath.Text, bak, true);
                sav.Save(txtPath.Text);
                var chk = SaveFile.Load(txtPath.Text);
                sav = chk;
                FillFromSave();
                lblStatus.Text = I18n.Str("status.saved", Path.GetFileName(bak));
            }
            catch (Exception ex) { lblStatus.Text = I18n.Str("status.savefail", ex.Message); }
        }

        public void UiSmoke(string path)
        {
            txtPath.Text = path;
            LoadFile();
        }

        public void AutoApply(string inPath, string outPath, string presetId, string scheme, bool activate)
        {
            txtPath.Text = inPath;
            LoadFile();
            if (scheme != null)
            {
                selectedProfile = scheme;
                RebuildSchemeList();
            }
            var p = Presets.ById(presetId);
            var rec = sav.RecordFor(selectedProfile);
            Presets.Apply(sav, p, rec);
            if (activate) sav.SetActiveProfile(selectedProfile);
            File.WriteAllBytes(outPath, new byte[0]);
            File.Delete(outPath);
            sav.Save(outPath);
            Console.WriteLine("autoapply preset=" + presetId + " scheme=" + selectedProfile + " -> " + outPath);
        }
    }
}










