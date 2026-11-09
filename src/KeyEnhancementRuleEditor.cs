using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public sealed class KeyEnhancementRuleEditor : EditorWindow
    {
        static readonly Color Blue = Color.FromArgb(112, 189, 220);
        static readonly string[] Triggers = { "press", "down", "release", "tap", "hold", "multi_tap", "sequence", "pulse", "toggle", "axis", "custom" };
        readonly ListBox rules = new ListBox(), outputs = new ListBox();
        readonly TextBox search = Input(), keys = Input(), axis = Input(), sequence = Input(), when = Input(), evaluate = Input();
        readonly TextBox replacements = Input(true), globalSuppressions = Input(true), json = Input(true);
        readonly NumericUpDown seconds = Number(.25m), interval = Number(.3m), count = Number(2), threshold = Number(.5m), priority = Number(0);
        readonly ComboBox trigger = new EditorComboBox();
        readonly CheckBox ruleEnabled = new EditorCheckBox(), consume = new EditorCheckBox();
        readonly TextBox consumeKeys = Input();
        readonly Label status = new Label(), locked = new Label();
        readonly Button enable = new EditorButton(), save = new EditorButton();
        readonly RuleTabs tabs = new RuleTabs();
        readonly Dictionary<Control, int> parameterRows = new Dictionary<Control, int>();
        TableLayoutPanel ruleTable;
        readonly List<Control> editing = new List<Control>();
        readonly ToolTip tips = new ToolTip();
        readonly Action enableRequested;
        string inputContext, actionContext;
        KeyEnhancementRule selected;
        bool editable, loading, rebuilding, jsonDirty;
        public KeyEnhancementConfiguration Configuration { get; private set; }

        public KeyEnhancementRuleEditor(KeyEnhancementConfiguration config, bool editable, Action enableRequested)
        {
            Configuration = config.Clone(); this.editable = editable; this.enableRequested = enableRequested;
            Text = S("title"); Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; BackColor = EditorTheme.Background; ForeColor = EditorTheme.Ink;
            ClientSize = new Size(1180, 790); MinimumSize = new Size(940, 660); StartPosition = FormStartPosition.CenterParent;
            var root = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16, 8, 16, 12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            var shared = Label(S("shared")); shared.ForeColor = Blue; root.Controls.Add(shared, 0, 0);
            var gate = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            gate.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); gate.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            locked.Text = S("locked"); locked.Dock = DockStyle.Fill; locked.TextAlign = ContentAlignment.MiddleLeft; locked.ForeColor = EditorTheme.Muted;
            enable.Text = S("enable"); enable.AutoSize = true; enable.Height = 32;
            enable.Click += delegate { if (enableRequested != null) enableRequested(); this.editable = true; Configuration.SetEnabled(true); RefreshGate(); };
            gate.Controls.Add(locked, 0, 0); gate.Controls.Add(enable, 1, 0); root.Controls.Add(gate, 0, 1);
            var split = new SplitContainer { Size = new Size(1148, 590), Dock = DockStyle.Fill, SplitterDistance = 265, BackColor = EditorTheme.Background, FixedPanel = FixedPanel.Panel1 };
            split.Panel1MinSize = 205; split.Panel2MinSize = 480;
            var left = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            search.Dock = DockStyle.Fill; tips.SetToolTip(search, S("search")); search.TextChanged += delegate { if ((tabs.SelectedIndex == 2 ? ReadJson() : SaveRule())) RefreshRules(selected); };
            rules.Dock = DockStyle.Fill; rules.BackColor = EditorTheme.Surface; rules.ForeColor = EditorTheme.Ink; rules.BorderStyle = BorderStyle.None;
            rules.DrawMode = DrawMode.OwnerDrawFixed; rules.ItemHeight = 70; rules.IntegralHeight = false; rules.DrawItem += DrawRule;
            rules.SelectedIndexChanged += SelectRule;
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = EditorTheme.Background, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
            AddButton(commands, S("add"), delegate
            {
                if (!SaveRule()) return;
                var rule = new KeyEnhancementRule(); if (!string.IsNullOrEmpty(inputContext)) rule.Keys.Add(inputContext);
                KeyEnhancementCatalogEntry[] candidates = new KeyEnhancementCatalogEntry[0];
                if (!string.IsNullOrEmpty(actionContext))
                {
                    candidates = KeyEnhancementCatalog.ForMapping(actionContext);
                    if (candidates.Length == 1)
                    { var target = candidates[0]; rule.Outputs.Add(new KeyEnhancementOutput { Kind = target.Kind, Path = target.Path, ValueType = target.ValueType }); }
                }
                Configuration.Rules.Add(rule); RefreshRules(rule);
                if (candidates.Length > 1) AddOutputs();
            }, true);
            AddButton(commands, S("duplicate"), DuplicateRule, true);
            AddButton(commands, S("delete"), delegate { if (selected == null) return; Configuration.Rules.Remove(selected); selected = null; RefreshRules(null); }, true);
            AddButton(commands, S("toggle"), delegate { if (selected == null || !SaveRule()) return; selected.Enabled = !selected.Enabled; RefreshRules(selected); }, true);
            AddButton(commands, S("all"), delegate { if (!SaveRule()) return; inputContext = actionContext = null; search.Text = ""; Text = S("title"); RefreshRules(selected); }, false);
            left.Controls.Add(search, 0, 0); left.Controls.Add(rules, 0, 1); left.Controls.Add(commands, 0, 2); split.Panel1.Controls.Add(left);
            tabs.Dock = DockStyle.Fill;
            Panel basic = Page(S("rule")), suppress = Page(S("baseline")), advanced = Page(S("json"));
            tabs.TabPages.AddRange(new[] { basic, suppress, advanced });
            BuildBasic(basic);
            var suppressRoot = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            suppressRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            suppressRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); suppressRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            suppressRoot.Controls.Add(Label(S("suppressionhint")), 0, 0); globalSuppressions.Dock = DockStyle.Fill;
            suppressRoot.Controls.Add(globalSuppressions, 0, 1); suppress.Controls.Add(suppressRoot); editing.Add(globalSuppressions);
            var jsonRoot = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            jsonRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            jsonRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); jsonRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            jsonRoot.Controls.Add(Label(S("jsonhint")), 0, 0); json.Dock = DockStyle.Fill; json.Font = new Font("Consolas", 10f); jsonRoot.Controls.Add(json, 0, 1); advanced.Controls.Add(jsonRoot); editing.Add(json);
            json.TextChanged += delegate { if (!loading) jsonDirty = true; };
            tabs.Selecting += delegate(object sender, RuleTabEventArgs e)
            {
                if (loading) return;
                if (tabs.SelectedIndex == 2 && !ReadJson()) { e.Cancel = true; return; }
                if (tabs.SelectedIndex != 2 && !SaveRule()) { e.Cancel = true; return; }
                if (e.TabPageIndex == 2) { loading = true; try { json.Text = Configuration.ToJson(); jsonDirty = false; } finally { loading = false; } }
            };
            split.Panel2.Controls.Add(tabs); root.Controls.Add(split, 0, 2);
            var footer = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = EditorTheme.Muted; status.AutoEllipsis = true;
            var footerButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            AddButton(footerButtons, S("import"), Import, true); AddButton(footerButtons, S("export"), Export, false);
            AddButton(footerButtons, S("copy"), delegate { if (SaveAll()) Clipboard.SetText(Configuration.ToJson()); }, false);
            save.Text = S("done"); save.Width = 130; save.Height = 34; save.ForeColor = Blue;
            save.Click += delegate { if (!editable) { DialogResult = DialogResult.Cancel; Close(); return; } if (!SaveAll()) return; DialogResult = DialogResult.OK; Close(); };
            footerButtons.Controls.Add(save); footer.Controls.Add(status, 0, 0); footer.Controls.Add(footerButtons, 1, 0); root.Controls.Add(footer, 0, 3);
            Controls.Add(root); globalSuppressions.Text = SuppressionText(Configuration.Suppressions);
            RefreshRules(null); RefreshGate();
        }

        static string S(string key) { return I18n.Str("ruleeditor." + key); }
        public void FocusInput(string key)
        {
            inputContext = key; actionContext = null;
            Text = S("title") + " · " + BindingButton.FriendlyKey(key);
            search.Text = ""; RefreshRules(null);
        }
        public void FocusAction(string actionId)
        {
            actionContext = actionId;
            Text = S("title") + " · " + BindingViewText.ActionName(actionId);
            search.Text = ""; RefreshRules(null);
            status.Text = BindingViewText.ActionName(actionId) + " · " + S("catalog");
        }
        static TextBox Input(bool multiline = false)
        {
            var text = new TextBox { Multiline = multiline, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink,
                BorderStyle = BorderStyle.FixedSingle, ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
                WordWrap = !multiline, AcceptsReturn = multiline, AcceptsTab = multiline };
            text.HandleCreated += delegate { EditorArtwork.DarkScrollbars(text); }; return text;
        }
        static NumericUpDown Number(decimal value)
        { return new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 3, Value = value, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink, BorderStyle = BorderStyle.FixedSingle }; }
        static Label Label(string text)
        { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = EditorTheme.Ink, AutoEllipsis = true, Padding = new Padding(4) }; }
        static Panel Page(string text) { return new EditorPanel { Text = text, BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Ink, Padding = new Padding(10) }; }
        void AddButton(Control parent, string text, Action action, bool changes)
        {
            var button = new EditorButton { Text = text, AutoSize = true, Height = 32, Margin = new Padding(0, 2, 6, 3) };
            button.Click += delegate
            {
                if (changes && !RequireEditing()) return;
                if (changes && tabs.SelectedIndex == 2 && !ReadJson()) return;
                Run(action);
                if (changes && tabs.SelectedIndex == 2)
                { loading = true; try { json.Text = Configuration.ToJson(); jsonDirty = false; } finally { loading = false; } }
            };
            if (changes) { editing.Add(button); tips.SetToolTip(button, S("locked")); }
            parent.Controls.Add(button);
        }
        void BuildBasic(Control page)
        {
            var scroll = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = EditorTheme.Surface };
            scroll.HandleCreated += delegate { EditorArtwork.DarkScrollbars(scroll); };
            var table = new EditorLayout { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(2), BackColor = EditorTheme.Surface };
            ruleTable = table;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ruleEnabled.Text = S("enabled"); Row(table, "", ruleEnabled, 35);
            var inputRow = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            keys.Dock = DockStyle.Fill; inputRow.Controls.Add(keys, 0, 0);
            var record = new EditorButton { Text = S("record"), Dock = DockStyle.Fill };
            record.Click += delegate
            {
                if (!RequireEditing()) return;
                using (var dialog = new BindingDialog(S("input"), false, "None", new string[0], false))
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != "None")
                        keys.Text = string.IsNullOrWhiteSpace(keys.Text) ? dialog.Result : keys.Text + " + " + dialog.Result;
            };
            inputRow.Controls.Add(record, 1, 0); editing.Add(record); Row(table, S("input"), inputRow, 37); editing.Add(keys);
            var help = Label(S("keyhint")); help.ForeColor = EditorTheme.Muted; Row(table, "", help, 42);
            trigger.DropDownStyle = ComboBoxStyle.DropDownList;
            string[] names = S("triggers").Split('|');
            for (int i = 0; i < Triggers.Length; i++) trigger.Items.Add(i < names.Length ? names[i] : Triggers[i]);
            trigger.SelectedIndex = 0; Row(table, S("trigger"), trigger, 36); trigger.SelectedIndexChanged += delegate { ShowParameters(); };
            Row(table, S("sequence"), sequence, 36); tips.SetToolTip(sequence, S("sequencehint"));
            Row(table, S("axis"), axis, 36); Row(table, S("seconds"), seconds, 36); Row(table, S("interval"), interval, 36);
            Row(table, S("count"), count, 36); count.DecimalPlaces = 0;
            Row(table, S("threshold"), threshold, 36); Row(table, S("priority"), priority, 36); priority.DecimalPlaces = 0;
            consume.Text = S("consume"); Row(table, "", consume, 34); Row(table, S("consumekeys"), consumeKeys, 36);
            tips.SetToolTip(consumeKeys, S("consumehint"));
            outputs.Dock = DockStyle.Fill; outputs.BackColor = EditorTheme.Floating; outputs.ForeColor = EditorTheme.Ink; outputs.BorderStyle = BorderStyle.FixedSingle;
            outputs.DisplayMember = "Text"; Row(table, S("outputs"), outputs, 130); outputs.DoubleClick += delegate { EditOutput(); };
            var outputButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            AddButton(outputButtons, S("catalog"), AddOutputs, true); AddButton(outputButtons, S("custom"), EditOutput, true);
            AddButton(outputButtons, S("removeoutput"), delegate
            {
                if (outputs.SelectedIndex < 0) return;
                outputs.Items.RemoveAt(outputs.SelectedIndex);
                if (outputs.Items.Count == 0 && selected != null)
                { Configuration.Rules.Remove(selected); selected = null; RefreshRules(null); }
            }, true);
            Row(table, "", outputButtons, 70);
            Row(table, S("replace"), replacements, 88); tips.SetToolTip(replacements, S("replacehint"));
            Row(table, S("when"), when, 38); Row(table, S("evaluate"), evaluate, 38);
            tips.SetToolTip(when, S("luahint")); tips.SetToolTip(evaluate, S("luahint"));
            scroll.Controls.Add(table); page.Controls.Add(scroll);
            scroll.ClientSizeChanged += delegate { table.Width = Math.Max(100, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth); };
        }
        void Row(TableLayoutPanel table, string title, Control control, int height)
        {
            int index = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            parameterRows[control] = index;
            var label = Label(title); label.ForeColor = EditorTheme.Muted; table.Controls.Add(label, 0, index);
            control.Dock = DockStyle.Fill; control.Margin = new Padding(3, 4, 3, 4); table.Controls.Add(control, 1, index);
            if (!(control is Label) && !(control is Panel) && control != outputs) editing.Add(control);
        }
        bool RequireEditing()
        {
            if (editable) return true;
            status.Text = S("locked"); status.ForeColor = Blue; enable.Focus(); return false;
        }
        void RefreshGate()
        {
            locked.Text = editable ? S("draft") : S("locked"); enable.Visible = !editable;
            foreach (Control control in editing)
            {
                var input = control as TextBox; if (input != null) input.ReadOnly = !editable;
                var check = control as CheckBox; if (check != null) check.AutoCheck = editable;
                if (control is ComboBox || control is NumericUpDown) control.Enabled = editable;
                control.ForeColor = editable ? EditorTheme.Ink : EditorTheme.Muted;
                if (!editable) tips.SetToolTip(control, S("locked"));
            }
            save.Text = editable ? S("done") : I18n.Str("editor.close"); ShowParameters();
        }
        void ShowParameters()
        {
            string value = trigger.SelectedIndex >= 0 ? Triggers[trigger.SelectedIndex] : "press";
            Parameter(sequence, value == "sequence"); Parameter(axis, value == "axis");
            Parameter(seconds, value == "tap" || value == "hold");
            Parameter(interval, value == "multi_tap" || value == "sequence" || value == "pulse");
            Parameter(count, value == "multi_tap"); Parameter(threshold, value == "axis");
            Parameter(evaluate, value == "custom");
        }
        void Parameter(Control control, bool visible)
        {
            int row; if (ruleTable == null || !parameterRows.TryGetValue(control, out row)) return;
            ruleTable.RowStyles[row].Height = visible ? 36 : 0;
            control.Visible = visible; control.Enabled = editable;
            Control label = ruleTable.GetControlFromPosition(0, row); if (label != null) label.Visible = visible;
        }
        void RefreshRules(KeyEnhancementRule preferred)
        {
            rebuilding = true;
            try
            {
                rules.Items.Clear(); string query = search.Text.Trim();
                foreach (var rule in Configuration.Rules)
                    if (rule == preferred && rule.Outputs.Count == 0 || MatchesActionContext(rule) && (query.Length == 0 || Summary(rule).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || rule.Trigger.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || rule.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)) rules.Items.Add(rule);
                int index = preferred == null ? -1 : rules.Items.IndexOf(preferred);
                if (index < 0 && rules.Items.Count > 0) index = 0;
                rules.SelectedIndex = index; selected = index < 0 ? null : (KeyEnhancementRule)rules.Items[index];
                LoadRule(); status.Text = S("total") + " " + Configuration.Rules.Count.ToString(CultureInfo.InvariantCulture);
            }
            finally { rebuilding = false; }
        }
        void SelectRule(object sender, EventArgs e)
        {
            if (rebuilding) return;
            if (tabs.SelectedIndex == 2 && jsonDirty)
            {
                string requestedId = rules.SelectedItem is KeyEnhancementRule ? ((KeyEnhancementRule)rules.SelectedItem).Id : null;
                if (!ReadJson()) return;
                RefreshRules(Configuration.Rules.FirstOrDefault(rule => rule.Id == requestedId)); return;
            }
            KeyEnhancementRule next = rules.SelectedItem as KeyEnhancementRule;
            if (!SaveRule()) { rebuilding = true; rules.SelectedItem = selected; rebuilding = false; return; }
            selected = next; LoadRule(); rules.Invalidate();
        }
        void LoadRule()
        {
            loading = true;
            try
            {
                var rule = selected ?? new KeyEnhancementRule();
                ruleEnabled.Checked = rule.Enabled; keys.Text = string.Join(" + ", rule.Keys); axis.Text = rule.Axis ?? "";
                trigger.SelectedIndex = Math.Max(0, Array.IndexOf(Triggers, rule.Trigger));
                sequence.Text = string.Join(" > ", rule.Steps.Select(step => string.Join(" + ", step)));
                SetNumber(seconds, rule.Seconds); SetNumber(interval, rule.Interval); SetNumber(threshold, rule.Threshold); SetNumber(count, rule.Count); SetNumber(priority, rule.Priority);
                consume.Checked = rule.Consume; consumeKeys.Text = string.Join(" + ", rule.ConsumeKeys);
                when.Text = rule.WhenLua ?? ""; evaluate.Text = rule.EvaluateLua ?? "";
                replacements.Text = SuppressionText(rule.Suppressions); outputs.Items.Clear();
                foreach (var output in rule.Outputs) outputs.Items.Add(new OutputItem(output));
            }
            finally { loading = false; ShowParameters(); }
        }
        static void SetNumber(NumericUpDown control, double value) { control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, (decimal)value)); }
        bool SaveRule()
        {
            if (loading || !editable || selected == null) return true;
            return Run(delegate
            {
                selected.Enabled = ruleEnabled.Checked; selected.Keys = ParseKeys(keys.Text).ToList(); selected.Axis = axis.Text.Trim();
                if (selected.Trigger != Triggers[trigger.SelectedIndex]) selected.DefaultTrigger = false;
                selected.Trigger = Triggers[trigger.SelectedIndex];
                selected.Steps = sequence.Text.Split(new[] { '>', '→' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseKeys).ToList();
                selected.Seconds = (double)seconds.Value; selected.Interval = (double)interval.Value; selected.Threshold = (double)threshold.Value;
                selected.Count = (int)count.Value; selected.Priority = (int)priority.Value; selected.Consume = consume.Checked; selected.ConsumeKeys = ParseKeys(consumeKeys.Text).ToList();
                selected.WhenLua = when.Text.Trim(); selected.EvaluateLua = evaluate.Text.Trim();
                selected.Outputs = outputs.Items.Cast<OutputItem>().Select(item => item.Output).ToList(); selected.Suppressions = ParseSuppressions(replacements.Text);
                Configuration.Suppressions = ParseSuppressions(globalSuppressions.Text);
            });
        }
        bool SaveAll()
        {
            if (tabs.SelectedIndex == 2) { if (!ReadJson()) return false; }
            else if (!SaveRule()) return false;
            return Run(delegate
            {
                if (tabs.SelectedIndex != 2 && editable) Configuration.Suppressions = ParseSuppressions(globalSuppressions.Text);
                Configuration.CompileLua();
            });
        }
        bool ReadJson()
        {
            if (!jsonDirty || !editable) return true;
            return Run(delegate
            {
                Configuration = KeyEnhancementConfiguration.FromJson(json.Text); Configuration.CompileLua(); jsonDirty = false;
                selected = null; globalSuppressions.Text = SuppressionText(Configuration.Suppressions); RefreshRules(null);
            });
        }
        void DuplicateRule()
        {
            if (selected == null || !SaveRule()) return;
            var clone = Configuration.Clone().Rules.Single(rule => rule.Id == selected.Id); clone.Id = Guid.NewGuid().ToString("N");
            Configuration.Rules.Add(clone); RefreshRules(clone);
        }
        static string[] ParseKeys(string text)
        { return text.Split(new[] { '+', ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(key => key.Trim()).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).ToArray(); }
        static string SuppressionText(IEnumerable<KeyEnhancementSuppression> values)
        { return string.Join(Environment.NewLine, values.Select(value => value.ActionPath + " | " + (value.Key ?? ""))); }
        static List<KeyEnhancementSuppression> ParseSuppressions(string text)
        {
            var result = new List<KeyEnhancementSuppression>();
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(new[] { '|' }, 2);
                if (string.IsNullOrWhiteSpace(parts[0])) throw new FormatException(S("suppressionhint"));
                result.Add(new KeyEnhancementSuppression { ActionPath = parts[0].Trim(), Key = parts.Length > 1 ? parts[1].Trim() : "" });
            }
            return result;
        }
        static string Summary(KeyEnhancementRule rule)
        {
            string input = rule.Trigger == "sequence" ? string.Join(" > ", rule.Steps.Select(step => string.Join(" + ", step)))
                : rule.Trigger == "axis" ? rule.Axis : string.Join(" + ", rule.Keys);
            int triggerIndex = Array.IndexOf(Triggers, rule.Trigger); string[] triggerNames = S("triggers").Split('|');
            return input + " · " + (triggerIndex >= 0 ? triggerNames[triggerIndex] : rule.Trigger) + " → " + string.Join(" + ", rule.Outputs.Select(OutputName));
        }
        bool MatchesActionContext(KeyEnhancementRule rule)
        {
            if (!string.IsNullOrEmpty(inputContext) && !rule.Keys.Any(MatchesInput) && !MatchesInput(rule.Axis) && !rule.Steps.Any(step => step.Any(MatchesInput)) && !rule.Watch.Any(MatchesInput)) return false;
            if (string.IsNullOrEmpty(actionContext)) return true;
            var candidates = KeyEnhancementCatalog.ForMapping(actionContext);
            if (candidates.Length == 0) return true;
            return rule.Outputs.Any(output => output.Kind == "action" && candidates.Any(entry => ActionPath(entry.Path) == ActionPath(output.Path))) || rule == selected && rule.Outputs.Count == 0;
        }
        bool MatchesInput(string key) { return key != null && BindingViewText.PhysicalKey(key) == BindingViewText.PhysicalKey(inputContext); }
        static string ActionPath(string path)
        { if (path == null) return ""; int dot = path.LastIndexOf('.'); return dot > path.LastIndexOf('/') ? path.Substring(0, dot) : path; }
        static string OutputName(KeyEnhancementOutput output)
        {
            if (!string.IsNullOrWhiteSpace(output.Name)) return output.Name;
            if (!string.IsNullOrWhiteSpace(output.FunctionId)) return BindingWorkspace.FunctionLabel(output.FunctionId);
            if (output.Kind == "mapping") return BindingWorkspace.FunctionLabel(output.MappingName);
            if (output.Kind == "action") return BindingWorkspace.FunctionLabel("action:" + ActionPath(output.Path));
            if (output.Kind == "ability") return BindingWorkspace.FunctionLabel("ability:" + output.Tag);
            if (output.Kind == "move") return BindingWorkspace.FunctionLabel(output.Tag);
            string name = !string.IsNullOrEmpty(output.Tag) ? output.Tag : !string.IsNullOrEmpty(output.Path) ? output.Path : output.Property ?? output.Kind ?? "";
            return name;
        }
        void DrawRule(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return; var rule = (KeyEnhancementRule)rules.Items[e.Index]; bool chosen = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(chosen ? EditorTheme.Hover : EditorTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            var text = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 8, e.Bounds.Width - 22, 25);
            TextRenderer.DrawText(e.Graphics, Summary(rule).Split(new[] { " → " }, StringSplitOptions.None)[0], Font, text, rule.Enabled ? EditorTheme.Ink : EditorTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            text.Y += 27; using (var small = new Font(Font.FontFamily, 8.5f)) TextRenderer.DrawText(e.Graphics, (rule.Enabled ? "● " : "○ ") + string.Join(" + ", rule.Outputs.Select(OutputName)), small, text, chosen ? Blue : EditorTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var pen = new Pen(EditorTheme.Line)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }
        sealed class OutputItem
        {
            public KeyEnhancementOutput Output;
            public OutputItem(KeyEnhancementOutput value) { Output = value; }
            public string Text { get { return Output.Kind + " · " + OutputName(Output); } }
            public override string ToString() { return Text; }
        }
        sealed class RuleTabEventArgs : EventArgs
        {
            public int TabPageIndex;
            public bool Cancel;
        }
        sealed class RuleTabs : Panel
        {
            public readonly List<Panel> TabPages = new List<Panel>();
            public event EventHandler<RuleTabEventArgs> Selecting;
            readonly FlowLayoutPanel bar = new FlowLayoutPanel();
            readonly List<EditorTab> buttons = new List<EditorTab>();
            int index;
            public int SelectedIndex
            {
                get { return index; }
                set
                {
                    if (value == index) return;
                    var args = new RuleTabEventArgs { TabPageIndex = value };
                    if (Selecting != null) Selecting(this, args); if (args.Cancel) return;
                    index = value; Display();
                }
            }
            public RuleTabs() { BackColor = EditorTheme.Background; }
            protected override void OnHandleCreated(EventArgs e)
            {
                if (buttons.Count == 0)
                {
                    bar.Dock = DockStyle.Top; bar.Height = 38; bar.WrapContents = false; bar.Margin = Padding.Empty;
                    for (int i = 0; i < TabPages.Count; i++)
                    {
                        int next = i; Panel page = TabPages[i]; page.Dock = DockStyle.Fill; Controls.Add(page);
                        var button = new EditorTab { Text = page.Text, Width = 155, Height = 36, Margin = Padding.Empty };
                        button.Click += delegate { SelectedIndex = next; }; buttons.Add(button); bar.Controls.Add(button);
                    }
                    Controls.Add(bar); Display();
                }
                base.OnHandleCreated(e);
            }
            void Display()
            {
                for (int i = 0; i < TabPages.Count; i++) { TabPages[i].Visible = i == index; if (i < buttons.Count) { buttons[i].Selected = i == index; buttons[i].Invalidate(); } }
            }
        }
        void AddOutputs()
        {
            if (selected == null) return;
            using (var dialog = new TargetPicker(string.IsNullOrEmpty(actionContext) ? null : KeyEnhancementCatalog.ForMapping(actionContext)))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    foreach (var entry in dialog.Selection)
                        outputs.Items.Add(new OutputItem(new KeyEnhancementOutput { Kind = entry.Kind, Path = entry.Path, Tag = entry.Tag, ValueType = entry.ValueType }));
        }
        void EditOutput()
        {
            if (!RequireEditing() || selected == null) return;
            int index = outputs.SelectedIndex;
            var original = index < 0 ? new KeyEnhancementOutput { Kind = "function", Receiver = "pawn", Path = "/Script/Engine.Character:Jump" } : ((OutputItem)outputs.Items[index]).Output;
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            using (var dialog = new JsonDialog(S("custom"), serializer.Serialize(original), S("targethint")))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    Run(delegate
                    {
                        var output = serializer.Deserialize<KeyEnhancementOutput>(dialog.Value);
                        if (output == null) throw new FormatException(S("invalid"));
                        if (index < 0) outputs.Items.Add(new OutputItem(output)); else outputs.Items[index] = new OutputItem(output);
                    });
        }
        void Import()
        {
            using (var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var imported = KeyEnhancementConfiguration.FromJson(File.ReadAllText(dialog.FileName)); imported.CompileLua();
                    Configuration = imported; selected = null; globalSuppressions.Text = SuppressionText(Configuration.Suppressions); RefreshRules(null);
                    if (tabs.SelectedIndex == 2) { loading = true; json.Text = Configuration.ToJson(); loading = false; jsonDirty = false; }
                }
        }
        void Export()
        {
            if (!SaveAll()) return;
            using (var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "PBZKeyForge-input-rules.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, Configuration.ToJson(), new UTF8Encoding(false));
        }
        bool Run(Action action)
        {
            try { action(); status.ForeColor = EditorTheme.Muted; return true; }
            catch (Exception ex) { status.ForeColor = EditorTheme.AccentHover; status.Text = S("invalid") + " " + ex.Message; tips.SetToolTip(status, status.Text); return false; }
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }

        sealed class TargetPicker : EditorWindow
        {
            readonly CheckedListBox choices = new CheckedListBox();
            readonly HashSet<KeyEnhancementCatalogEntry> chosen = new HashSet<KeyEnhancementCatalogEntry>();
            bool refreshing;
            public KeyEnhancementCatalogEntry[] Selection { get { return chosen.ToArray(); } }
            public TargetPicker(KeyEnhancementCatalogEntry[] suggested)
            {
                var configuration = new KeyEnhancementConfiguration { Enabled = true };
                var workspace = new BindingWorkspace(EditorSession.CreateOffline(), configuration, configuration.Clone(), false, -1);
                var suggestedIds = new HashSet<string>((suggested ?? new KeyEnhancementCatalogEntry[0]).Select(entry => entry.Id));
                Text = S("catalog"); Font = new Font("Microsoft YaHei UI", 10f); ClientSize = new Size(790, 620); MinimumSize = new Size(580, 400); StartPosition = FormStartPosition.CenterParent; BackColor = EditorTheme.Background;
                var layout = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14, 8, 14, 12) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                layout.Controls.Add(Label(S("cataloghint")), 0, 0);
                var filterRow = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 2 };
                filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
                var filter = Input(); filter.Dock = DockStyle.Fill; filterRow.Controls.Add(filter, 0, 0);
                var all = new EditorButton { Text = S("catalogall"), Dock = DockStyle.Fill }; filterRow.Controls.Add(all, 1, 0); layout.Controls.Add(filterRow, 0, 1);
                bool showAll = suggested == null || suggested.Length == 0; all.Visible = !showAll;
                choices.Dock = DockStyle.Fill; choices.CheckOnClick = true; choices.IntegralHeight = false; choices.BackColor = EditorTheme.Floating; choices.ForeColor = EditorTheme.Ink; choices.DisplayMember = "Name";
                choices.HandleCreated += delegate { EditorArtwork.DarkScrollbars(choices); };
                choices.ItemCheck += delegate(object sender, ItemCheckEventArgs e) { if (refreshing) return; var entry = ((CatalogItem)choices.Items[e.Index]).Entry; if (e.NewValue == CheckState.Checked) chosen.Add(entry); else chosen.Remove(entry); };
                Action refresh = delegate
                {
                    refreshing = true; choices.BeginUpdate();
                    try
                    {
                        choices.Items.Clear(); string term = filter.Text.Trim();
                        foreach (var function in workspace.FindFunctionChoices(term))
                            if (function.Targets.Length == 1 && function.Id == function.Targets[0].Id && (showAll || suggestedIds.Contains(function.Id)))
                                choices.Items.Add(new CatalogItem { Function = function }, chosen.Contains(function.Targets[0]));
                    }
                    finally { choices.EndUpdate(); refreshing = false; }
                };
                all.Click += delegate { showAll = true; all.Visible = false; refresh(); };
                filter.TextChanged += delegate { refresh(); }; refresh(); layout.Controls.Add(choices, 0, 2);
                var ok = new EditorButton { Text = S("addoutputs"), Dock = DockStyle.Right, Width = 160, DialogResult = DialogResult.OK }; layout.Controls.Add(ok, 0, 3); Controls.Add(layout); AcceptButton = ok;
            }
            sealed class CatalogItem
            {
                public BindingFunction Function;
                public KeyEnhancementCatalogEntry Entry { get { return Function.Targets[0]; } }
                public string Name { get { return Function.Label + " · " + UnifiedActionChoiceDialog.FunctionCode(Function) + " · " + string.Join(" / ", Function.GroupLabels); } }
                public override string ToString() { return Name; }
            }
        }
        sealed class JsonDialog : EditorWindow
        {
            readonly TextBox text = Input(true);
            public string Value { get { return text.Text; } }
            public JsonDialog(string title, string value, string hint)
            {
                Text = title; Font = new Font("Microsoft YaHei UI", 10f); ClientSize = new Size(770, 570); MinimumSize = new Size(560, 380); StartPosition = FormStartPosition.CenterParent;
                var layout = new EditorLayout { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 8, 14, 12) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var explanation = Label(hint); layout.Controls.Add(explanation, 0, 0); text.Text = value; text.Font = new Font("Consolas", 10f); text.Dock = DockStyle.Fill; layout.Controls.Add(text, 0, 1);
                var ok = new EditorButton { Text = S("done"), Dock = DockStyle.Right, Width = 130 };
                ok.Click += delegate
                {
                    try
                    {
                        var output = new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(text.Text);
                        var check = new KeyEnhancementConfiguration(); var rule = new KeyEnhancementRule(); rule.Keys.Add("F9"); rule.Outputs.Add(output); check.Rules.Add(rule); check.CompileLua();
                        DialogResult = DialogResult.OK; Close();
                    }
                    catch (Exception ex) { explanation.Text = S("invalid") + " " + ex.Message; explanation.ForeColor = EditorTheme.AccentHover; }
                };
                layout.Controls.Add(ok, 0, 2); Controls.Add(layout);
            }
        }
    }
}
