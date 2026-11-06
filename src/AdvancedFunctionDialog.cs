using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public sealed class AdvancedFunctionDialog : EditorWindow
    {
        readonly ComboBox kind = new EditorComboBox(), receiver = new EditorComboBox(), condition = new EditorComboBox(), valueType = new EditorComboBox();
        readonly TextBox name = UnifiedBindingUi.Search(), path = UnifiedBindingUi.Search(), property = UnifiedBindingUi.Search(), stringValue = UnifiedBindingUi.Search();
        readonly NumericUpDown number = new NumericUpDown();
        readonly CheckBox boolean = new EditorCheckBox();
        readonly CheckBox hookExpression = new EditorCheckBox();
        readonly TextBox whenCode = UnifiedBindingUi.Search();
        readonly TextBox code = UnifiedBindingUi.Search();
        readonly DataGridView arguments = new DataGridView();
        readonly ComboBox existingHook = new EditorComboBox();
        readonly EditorButton deleteHook = UnifiedBindingUi.Button("", "deleteReturnOverride");
        readonly KeyEnhancementConfiguration configuration;
        readonly EditorButton compound = UnifiedBindingUi.Button("", "editCompoundValue");
        object compoundValue;
        bool editingTarget;
        readonly Label message = UnifiedBindingUi.Label("");
        readonly Dictionary<Control, Panel> rows = new Dictionary<Control, Panel>();
        readonly string[] kinds = { "function", "event", "property", "hook", "lua" };
        readonly string[] valueTypes = { "bool", "number", "string", "object", "struct", "array" };
        public KeyEnhancementOutput ResultTarget { get; private set; }
        public Dictionary<string, object> ResultHook { get; private set; }
        public string FunctionName { get { return name.Text.Trim(); } }
        public string ConditionLua { get; private set; }
        public int HookIndex { get { return existingHook.SelectedIndex - 1; } }
        public bool DeleteHook { get; private set; }

        public AdvancedFunctionDialog() : this(null) { }
        public AdvancedFunctionDialog(KeyEnhancementConfiguration configuration)
        {
            this.configuration = configuration;
            UnifiedBindingUi.Form(this, T("title"), new Size(760, 700), new Size(570, 470));
            var root = UnifiedBindingUi.Grid(1); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Padding = new Padding(16, 10, 16, 12); root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var scroll = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.HandleCreated += delegate { EditorArtwork.DarkScrollbars(scroll); };
            var fields = new EditorLayout { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 }; fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            kind.DropDownStyle = ComboBoxStyle.DropDownList; foreach (string value in kinds) kind.Items.Add(T("kind." + value));
            Row(fields, T("kind"), kind, 40); Row(fields, T("name"), name, 40); Row(fields, T("path"), path, 40); Row(fields, T("property"), property, 40);
            existingHook.DropDownStyle = ComboBoxStyle.DropDownList; existingHook.Items.Add(T("newhook"));
            if (configuration != null) foreach (var hook in configuration.Hooks) existingHook.Items.Add(Convert.ToString(hook.ContainsKey("name") ? hook["name"] : hook["path"]));
            existingHook.SelectedIndex = 0; existingHook.SelectedIndexChanged += delegate { LoadHook(); }; Row(fields, T("existinghooks"), existingHook, 40);
            deleteHook.Text = T("deletehook"); deleteHook.Click += delegate { if (HookIndex < 0) return; DeleteHook = true; DialogResult = DialogResult.OK; Close(); }; Row(fields, "", deleteHook, 40);
            receiver.DropDownStyle = ComboBoxStyle.DropDownList; foreach (string value in new[] { "pawn", "controller", "subsystem", "ability_input", "ability_system" }) receiver.Items.Add(value); receiver.SelectedIndex = 0;
            Row(fields, T("receiver"), receiver, 40);
            condition.DropDownStyle = ComboBoxStyle.DropDownList; foreach (string value in new[] { "always", "gameplay", "foreground", "unpaused", "expression" }) condition.Items.Add(T("condition." + value)); condition.SelectedIndex = 0; Row(fields, T("condition"), condition, 40);
            whenCode.Multiline = true; whenCode.AcceptsReturn = true; whenCode.ScrollBars = ScrollBars.Vertical; whenCode.Font = new Font("Consolas", 10f); whenCode.Text = "function(context) return true end"; Row(fields, T("condition.expression"), whenCode, 95);
            hookExpression.Text = T("returnexpression"); hookExpression.CheckedChanged += delegate { UpdateFields(); }; Row(fields, "", hookExpression, 38);
            valueType.DropDownStyle = ComboBoxStyle.DropDownList; foreach (string value in valueTypes) valueType.Items.Add(T("type." + value)); valueType.SelectedIndex = 0; Row(fields, T("valuetype"), valueType, 40);
            boolean.Text = T("true"); Row(fields, T("value"), boolean, 38);
            number.Minimum = -1000000000; number.Maximum = 1000000000; number.DecimalPlaces = 4; number.BackColor = EditorTheme.Floating; number.ForeColor = EditorTheme.Ink; Row(fields, T("value"), number, 40);
            Row(fields, T("value"), stringValue, 40);
            compound.Text = T("editvalue"); compound.Click += delegate
            {
                object initial = compoundValue ?? (valueType.SelectedIndex == 3 ? (object)new Dictionary<string, object> { { "object", "" } } : valueType.SelectedIndex == 4 ? (object)new Dictionary<string, object>() : new object[0]);
                using (var dialog = new AdvancedValueDialog(initial))
                    if (dialog.ShowDialog(this) == DialogResult.OK) { compoundValue = dialog.Result; compound.Text = AdvancedValueDialog.Describe(compoundValue); }
            };
            Row(fields, T("value"), compound, 42);
            arguments.BackgroundColor = EditorTheme.Floating; arguments.BorderStyle = BorderStyle.None; arguments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            arguments.DefaultCellStyle.BackColor = EditorTheme.Floating; arguments.DefaultCellStyle.ForeColor = EditorTheme.Ink; arguments.DefaultCellStyle.SelectionBackColor = EditorTheme.Hover;
            arguments.DefaultCellStyle.SelectionForeColor = EditorTheme.Ink; arguments.RowHeadersVisible = false; arguments.EnableHeadersVisualStyles = false;
            arguments.ColumnHeadersDefaultCellStyle.BackColor = EditorTheme.Surface; arguments.ColumnHeadersDefaultCellStyle.ForeColor = EditorTheme.Ink;
            arguments.Columns.Add(new DataGridViewTextBoxColumn { Name = "field", HeaderText = T("field") });
            var typeColumn = new DataGridViewComboBoxColumn { Name = "type", HeaderText = T("valuetype"), FlatStyle = FlatStyle.Flat };
            foreach (string value in new[] { "bool", "number", "string", "fname", "tag", "receiver", "object", "struct", "array", "value", "phase" }) typeColumn.Items.Add(value);
            arguments.Columns.Add(typeColumn); arguments.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = T("value") });
            arguments.DefaultValuesNeeded += delegate(object sender, DataGridViewRowEventArgs e) { e.Row.Cells["type"].Value = "string"; };
            arguments.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { message.Text = T("invalid"); e.ThrowException = false; };
            arguments.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs e) { if (e.ColumnIndex == 1 || e.ColumnIndex == 2) arguments.Rows[e.RowIndex].Tag = null; };
            var argumentHost = UnifiedBindingUi.Grid(1); argumentHost.AutoSize = true; argumentHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            argumentHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); argumentHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            arguments.Dock = DockStyle.Fill; argumentHost.Controls.Add(arguments, 0, 0);
            var editArgument = UnifiedBindingUi.Button(T("editvalue"), "editArgumentValue"); editArgument.Dock = DockStyle.Fill; editArgument.Click += delegate { EditArgument(); }; argumentHost.Controls.Add(editArgument, 0, 1);
            arguments.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 2) EditArgument(); };
            Row(fields, T("arguments"), argumentHost, 230); rows[arguments] = rows[argumentHost];
            code.Multiline = true; code.AcceptsReturn = true; code.AcceptsTab = true; code.ScrollBars = ScrollBars.Vertical; code.Font = new Font("Consolas", 10f);
            code.Text = UnifiedBindingUi.ScriptText("function(backend, event)\n    \nend"); Row(fields, T("script"), code, 180);
            scroll.Controls.Add(fields); root.Controls.Add(scroll, 0, 0); message.ForeColor = EditorTheme.Muted; message.Text = T("hint"); root.Controls.Add(message, 0, 1);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
            var accept = UnifiedBindingUi.Button(I18n.Str("editor.ok"), "saveCustomFunction"); accept.Primary = true; accept.Click += delegate { Save(); };
            var cancel = UnifiedBindingUi.Button(I18n.Str("editor.cancel"), "cancel"); cancel.DialogResult = DialogResult.Cancel; commands.Controls.Add(accept); commands.Controls.Add(cancel); root.Controls.Add(commands, 0, 2);
            Controls.Add(root); AcceptButton = accept; CancelButton = cancel; kind.SelectedIndexChanged += delegate { UpdateFields(); }; valueType.SelectedIndexChanged += delegate { UpdateFields(); }; condition.SelectedIndexChanged += delegate { UpdateFields(); }; kind.SelectedIndex = 0;
        }
        static string T(string key) { return I18n.Str("unified.advanced." + key); }
        public void LoadTarget(KeyEnhancementOutput output, string label)
        {
            int selected = Array.IndexOf(kinds, output.Kind);
            if (selected < 0 || output.Kind == "hook") throw new ArgumentException("Output.Kind=" + output.Kind);
            editingTarget = true; kind.SelectedIndex = selected; kind.Enabled = false;
            Text = UnifiedBindingUi.Text("editFunction"); name.Text = label;
            path.Text = output.Path ?? output.Tag; property.Text = output.Property;
            if (!receiver.Items.Contains(output.Receiver)) receiver.Items.Add(output.Receiver ?? "pawn");
            receiver.SelectedItem = output.Receiver ?? "pawn";
            if (output.Kind == "lua") code.Text = UnifiedBindingUi.ScriptText(output.Code);
            if (output.Kind == "property") LoadOutputValue(output.Value);
            arguments.Rows.Clear();
            if (output.Kind == "function") foreach (object value in output.Args ?? new object[0]) AddArgument("", value);
            var payload = output.Payload as System.Collections.IDictionary;
            if (output.Kind == "event" && payload != null)
                foreach (System.Collections.DictionaryEntry value in payload) AddArgument(Convert.ToString(value.Key), value.Value);
            UpdateFields();
        }
        void AddArgument(string field, object value)
        {
            string type = value is bool ? "bool" : value is string ? "string" : value is System.Collections.IDictionary ? "struct"
                : value is System.Collections.IEnumerable ? "array" : "number";
            int index = arguments.Rows.Add(field, type, AdvancedValueDialog.Describe(value));
            arguments.Rows[index].Tag = value;
        }
        void LoadOutputValue(object value)
        {
            if (value is bool) { valueType.SelectedIndex = 0; boolean.Checked = (bool)value; }
            else if (value is string || value == null) { valueType.SelectedIndex = 2; stringValue.Text = Convert.ToString(value); }
            else if (value is System.Collections.IDictionary) { valueType.SelectedIndex = 4; compoundValue = value; compound.Text = AdvancedValueDialog.Describe(value); }
            else if (value is System.Collections.IEnumerable) { valueType.SelectedIndex = 5; compoundValue = value; compound.Text = AdvancedValueDialog.Describe(value); }
            else { valueType.SelectedIndex = 1; number.Value = Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
        }
        void Row(TableLayoutPanel fields, string label, Control control, int height)
        {
            var row = UnifiedBindingUi.Grid(2); row.AutoSize = true; row.Dock = DockStyle.Fill; row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            row.Controls.Add(UnifiedBindingUi.Label(label), 0, 0); control.Dock = DockStyle.Fill; control.Margin = new Padding(4); row.Controls.Add(control, 1, 0);
            int index = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); fields.Controls.Add(row, 0, index); rows[control] = row;
        }
        void UpdateFields()
        {
            string value = kinds[Math.Max(0, kind.SelectedIndex)]; bool scalar = value == "property" || value == "hook" && !hookExpression.Checked;
            rows[path].Visible = value != "lua" && value != "property"; rows[property].Visible = value == "property"; rows[receiver].Visible = value == "function" || value == "property";
            rows[existingHook].Visible = value == "hook" && configuration != null && configuration.Hooks.Count > 0;
            rows[deleteHook].Visible = value == "hook" && HookIndex >= 0;
            rows[condition].Visible = !editingTarget; rows[whenCode].Visible = !editingTarget && condition.SelectedIndex == 4; rows[hookExpression].Visible = value == "hook";
            rows[valueType].Visible = scalar; rows[boolean].Visible = scalar && valueType.SelectedIndex == 0; rows[number].Visible = scalar && valueType.SelectedIndex == 1; rows[stringValue].Visible = scalar && valueType.SelectedIndex == 2; rows[compound].Visible = scalar && valueType.SelectedIndex >= 3;
            rows[arguments].Visible = value == "event" || value == "function"; rows[code].Visible = value == "lua" || value == "hook" && hookExpression.Checked;
            path.AccessibleName = value == "event" ? T("eventtag") : T("path");
        }
        object Value()
        { return valueType.SelectedIndex == 0 ? (object)boolean.Checked : valueType.SelectedIndex == 1 ? (object)(double)number.Value : valueType.SelectedIndex == 2 ? (object)stringValue.Text : compoundValue ?? (valueType.SelectedIndex == 3 ? (object)new Dictionary<string, object> { { "object", "" } } : valueType.SelectedIndex == 4 ? (object)new Dictionary<string, object>() : new object[0]); }
        void EditArgument()
        {
            arguments.EndEdit(); var row = arguments.CurrentRow; if (row == null || row.IsNewRow) return;
            object value;
            try { value = ArgumentValue(row); } catch (Exception) { value = ""; }
            using (var dialog = new AdvancedValueDialog(value))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    row.Tag = dialog.Result; row.Cells["value"].Value = AdvancedValueDialog.Describe(dialog.Result);
                    row.Cells["type"].Value = dialog.Result is System.Collections.IDictionary ? "struct" : dialog.Result is object[] ? "array" : dialog.Result is bool ? "bool" : dialog.Result is double ? "number" : "string";
                }
        }
        void Save()
        {
            try
            {
                arguments.EndEdit(); string selected = kinds[kind.SelectedIndex];
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException(T("needname"));
                if (selected != "lua" && selected != "property" && string.IsNullOrWhiteSpace(path.Text)) throw new InvalidOperationException(T("needpath"));
                object when = null; ConditionLua = null;
                if (condition.SelectedIndex == 1) { when = "gameplay"; ConditionLua = "function(context) return context.gameplay end"; }
                if (condition.SelectedIndex == 2) { when = "foreground"; ConditionLua = "function(context) return context.foreground end"; }
                if (condition.SelectedIndex == 3) { when = new Dictionary<string, object> { { "paused", false } }; ConditionLua = "function(context) return not context.paused end"; }
                if (condition.SelectedIndex == 4) { if (string.IsNullOrWhiteSpace(whenCode.Text)) throw new InvalidOperationException(T("invalid")); ConditionLua = whenCode.Text.Trim(); when = ConditionLua; }
                if (selected == "hook")
                {
                    ResultHook = new Dictionary<string, object> { { "name", name.Text.Trim() }, { "path", path.Text.Trim() }, { "value", Value() } }; if (when != null) ResultHook["when"] = when;
                    if (hookExpression.Checked) { ResultHook.Remove("value"); ResultHook["evaluate"] = code.Text.Trim(); }
                }
                else
                {
                    ResultTarget = new KeyEnhancementOutput { Kind = selected, Receiver = Convert.ToString(receiver.SelectedItem), Name = name.Text.Trim() };
                    if (selected == "function") { ResultTarget.Path = path.Text.Trim(); ResultTarget.Args = Arguments(false).Select(pair => pair.Value).ToArray(); }
                    if (selected == "event") { ResultTarget.Tag = path.Text.Trim(); ResultTarget.Payload = Arguments(true).ToDictionary(pair => pair.Key, pair => pair.Value); }
                    if (selected == "property") { if (string.IsNullOrWhiteSpace(property.Text)) throw new InvalidOperationException(T("needpath")); ResultTarget.Property = property.Text.Trim(); ResultTarget.Value = Value(); }
                    if (selected == "lua") ResultTarget.Code = code.Text;
                    var validation = new KeyEnhancementConfiguration(); var rule = new KeyEnhancementRule(); rule.Keys.Add("F9"); rule.Outputs.Add(ResultTarget); validation.Rules.Add(rule); validation.CompileLua();
                }
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { message.Text = T("invalid") + " " + ex.Message; message.ForeColor = EditorTheme.AccentHover; }
        }
        IEnumerable<KeyValuePair<string, object>> Arguments(bool named)
        {
            foreach (DataGridViewRow row in arguments.Rows)
            {
                if (row.IsNewRow) continue;
                string field = Convert.ToString(row.Cells["field"].Value);
                if (named && string.IsNullOrWhiteSpace(field)) throw new FormatException(T("needfield"));
                yield return new KeyValuePair<string, object>(field, ArgumentValue(row));
            }
        }
        object ArgumentValue(DataGridViewRow row)
        {
            if (row.Tag != null) return row.Tag;
            string text = Convert.ToString(row.Cells["value"].Value), type = Convert.ToString(row.Cells["type"].Value);
            if (type == "bool") return bool.Parse(text);
            if (type == "number") return double.Parse(text, CultureInfo.InvariantCulture);
            if (type == "fname" || type == "tag" || type == "receiver" || type == "object") return new Dictionary<string, object> { { type, text } };
            if (type == "value" || type == "phase") return new Dictionary<string, object> { { type, true } };
            if (type == "struct") return new Dictionary<string, object>();
            if (type == "array") return new object[0];
            return text;
        }
        void LoadHook()
        {
            if (configuration == null || HookIndex < 0) { UpdateFields(); return; }
            var hook = configuration.Hooks[HookIndex]; path.Text = Convert.ToString(hook["path"]); name.Text = Convert.ToString(hook.ContainsKey("name") ? hook["name"] : hook["path"]);
            hookExpression.Checked = hook.ContainsKey("evaluate"); if (hookExpression.Checked) code.Text = UnifiedBindingUi.ScriptText(Convert.ToString(hook["evaluate"]));
            object value = hook.ContainsKey("value") ? hook["value"] : false;
            if (value is bool) { valueType.SelectedIndex = 0; boolean.Checked = (bool)value; }
            else if (value is string) { valueType.SelectedIndex = 2; stringValue.Text = (string)value; }
            else if (value is System.Collections.IDictionary) { valueType.SelectedIndex = 4; compoundValue = value; }
            else if (value is System.Collections.IEnumerable) { valueType.SelectedIndex = 5; compoundValue = value; }
            else { valueType.SelectedIndex = 1; number.Value = Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
            condition.SelectedIndex = 0;
            if (hook.ContainsKey("when"))
            {
                if (Convert.ToString(hook["when"]) == "gameplay") condition.SelectedIndex = 1;
                else if (Convert.ToString(hook["when"]) == "foreground") condition.SelectedIndex = 2;
                else if (hook["when"] is System.Collections.IDictionary && ((System.Collections.IDictionary)hook["when"]).Contains("paused") && Equals(((System.Collections.IDictionary)hook["when"])["paused"], false)) condition.SelectedIndex = 3;
                else if (hook["when"] is string && ((string)hook["when"]).TrimStart().StartsWith("function", StringComparison.Ordinal)) { condition.SelectedIndex = 4; whenCode.Text = UnifiedBindingUi.ScriptText((string)hook["when"]); }
            }
            UpdateFields();
        }
    }
}
