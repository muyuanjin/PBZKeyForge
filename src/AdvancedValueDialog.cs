using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public sealed class AdvancedValueDialog : EditorWindow
    {
        static readonly string[] Types = { "string", "number", "bool", "object", "struct", "array", "fname", "tag", "receiver", "value", "phase" };
        readonly ComboBox type = new EditorComboBox();
        readonly TextBox text = UnifiedBindingUi.Search(), className = UnifiedBindingUi.Search();
        readonly NumericUpDown number = new NumericUpDown();
        readonly CheckBox boolean = new EditorCheckBox();
        readonly ListBox members = new ListBox();
        readonly TextBox fieldName = UnifiedBindingUi.Search();
        readonly Label status = UnifiedBindingUi.Label("");
        readonly List<Member> values = new List<Member>();
        readonly Panel textRow, classRow, numberRow, booleanRow, memberRow;
        public object Result { get; private set; }
        public AdvancedValueDialog(object value)
        {
            UnifiedBindingUi.Form(this, T("editvalue"), new Size(670, 560), new Size(510, 410));
            var root = UnifiedBindingUi.Grid(1); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Padding = new Padding(16, 10, 16, 12); root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var fields = new EditorLayout { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 }; fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            type.DropDownStyle = ComboBoxStyle.DropDownList; foreach (string id in Types) type.Items.Add(T("type." + id)); Row(fields, T("valuetype"), type, 40);
            textRow = Row(fields, T("value"), text, 40); classRow = Row(fields, T("objectclass"), className, 40);
            number.Minimum = -1000000000; number.Maximum = 1000000000; number.DecimalPlaces = 5; number.BackColor = EditorTheme.Floating; number.ForeColor = EditorTheme.Ink;
            numberRow = Row(fields, T("value"), number, 40); boolean.Text = T("true"); booleanRow = Row(fields, T("value"), boolean, 40);
            var memberRoot = UnifiedBindingUi.Grid(1); memberRoot.AutoSize = true; memberRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            memberRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); memberRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); memberRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            members.Dock = DockStyle.Fill; members.BackColor = EditorTheme.Floating; members.ForeColor = EditorTheme.Ink; members.IntegralHeight = false; members.DisplayMember = "Display";
            members.DoubleClick += delegate { EditMember(); }; memberRoot.Controls.Add(members, 0, 0);
            fieldName.Dock = DockStyle.Fill; fieldName.AccessibleName = T("field"); memberRoot.Controls.Add(fieldName, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            var add = UnifiedBindingUi.Button(T("addvalue"), "addValue"); add.Click += delegate { AddMember(); };
            var edit = UnifiedBindingUi.Button(UnifiedBindingUi.Text("edit"), "editValue"); edit.Click += delegate { EditMember(); };
            var remove = UnifiedBindingUi.Button(I18n.Str("views.remove"), "removeValue"); remove.Click += delegate { if (members.SelectedItem is Member) { values.Remove((Member)members.SelectedItem); RefreshMembers(); } };
            buttons.Controls.AddRange(new Control[] { add, edit, remove }); memberRoot.Controls.Add(buttons, 0, 2); memberRow = Row(fields, T("members"), memberRoot, 295);
            var scroll = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.HandleCreated += delegate { EditorArtwork.DarkScrollbars(scroll); };
            scroll.Controls.Add(fields); root.Controls.Add(scroll, 0, 0); status.ForeColor = EditorTheme.Muted; root.Controls.Add(status, 0, 1);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
            var accept = UnifiedBindingUi.Button(I18n.Str("editor.ok"), "saveValue"); accept.Primary = true; accept.Click += delegate { Save(); };
            var cancel = UnifiedBindingUi.Button(I18n.Str("editor.cancel"), "cancel"); cancel.DialogResult = DialogResult.Cancel; commands.Controls.Add(accept); commands.Controls.Add(cancel); root.Controls.Add(commands, 0, 2);
            Controls.Add(root); AcceptButton = accept; CancelButton = cancel;
            type.SelectedIndexChanged += delegate { UpdateFields(); }; LoadValue(value); UpdateFields();
        }
        static string T(string key) { return I18n.Str("unified.advanced." + key); }
        static Panel Row(TableLayoutPanel fields, string title, Control input, int height)
        {
            var row = UnifiedBindingUi.Grid(2); row.AutoSize = true; row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            row.Controls.Add(UnifiedBindingUi.Label(title), 0, 0); input.Dock = DockStyle.Fill; input.Margin = new Padding(4); row.Controls.Add(input, 1, 0);
            int index = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); fields.Controls.Add(row, 0, index); return row;
        }
        void LoadValue(object value)
        {
            string selected = "string";
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                selected = dictionary.Contains("object") ? "object" : Types.Where(id => id == "fname" || id == "tag" || id == "receiver" || id == "value" || id == "phase").FirstOrDefault(dictionary.Contains) ?? "struct";
                if (selected == "struct") foreach (DictionaryEntry item in dictionary) values.Add(new Member { Key = Convert.ToString(item.Key), Value = item.Value });
                else { text.Text = Convert.ToString(dictionary[selected], CultureInfo.InvariantCulture); if (dictionary.Contains("class")) className.Text = Convert.ToString(dictionary["class"]); }
            }
            else if (value is bool) { selected = "bool"; boolean.Checked = (bool)value; }
            else if (value is double || value is int || value is float || value is decimal || value is long)
            { selected = "number"; number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, Convert.ToDecimal(value, CultureInfo.InvariantCulture))); }
            else if (value is IEnumerable && !(value is string))
            { selected = "array"; foreach (object item in (IEnumerable)value) values.Add(new Member { Value = item }); }
            else text.Text = Convert.ToString(value, CultureInfo.InvariantCulture);
            type.SelectedIndex = Array.IndexOf(Types, selected); RefreshMembers();
        }
        void UpdateFields()
        {
            string selected = Types[Math.Max(0, type.SelectedIndex)]; bool composite = selected == "struct" || selected == "array";
            textRow.Visible = !composite && selected != "bool" && selected != "number" && selected != "value" && selected != "phase";
            classRow.Visible = selected == "object"; numberRow.Visible = selected == "number"; booleanRow.Visible = selected == "bool";
            memberRow.Visible = composite; fieldName.Visible = selected == "struct"; RefreshMembers();
        }
        void AddMember()
        {
            bool named = Types[type.SelectedIndex] == "struct"; string key = fieldName.Text.Trim();
            if (named && (key.Length == 0 || values.Any(member => member.Key == key))) { status.Text = T("needfield"); fieldName.Focus(); return; }
            using (var dialog = new AdvancedValueDialog(""))
                if (dialog.ShowDialog(this) == DialogResult.OK) { values.Add(new Member { Key = key, Value = dialog.Result }); fieldName.Clear(); RefreshMembers(); }
        }
        void EditMember()
        {
            var member = members.SelectedItem as Member; if (member == null) return;
            using (var dialog = new AdvancedValueDialog(member.Value))
                if (dialog.ShowDialog(this) == DialogResult.OK) { member.Value = dialog.Result; RefreshMembers(); }
        }
        void RefreshMembers()
        {
            if (type.SelectedIndex < 0) return; bool named = Types[type.SelectedIndex] == "struct";
            int index = members.SelectedIndex; members.Items.Clear();
            for (int i = 0; i < values.Count; i++) { values[i].Display = (named ? values[i].Key : (i + 1).ToString(CultureInfo.InvariantCulture)) + "  ·  " + Describe(values[i].Value); members.Items.Add(values[i]); }
            if (members.Items.Count > 0) members.SelectedIndex = Math.Max(0, Math.Min(index, members.Items.Count - 1));
        }
        void Save()
        {
            string selected = Types[type.SelectedIndex];
            if (selected == "string") Result = text.Text;
            else if (selected == "number") Result = (double)number.Value;
            else if (selected == "bool") Result = boolean.Checked;
            else if (selected == "array") Result = values.Select(member => member.Value).ToArray();
            else if (selected == "struct")
            {
                if (values.Any(member => string.IsNullOrWhiteSpace(member.Key)) || values.Select(member => member.Key).Distinct().Count() != values.Count) { status.Text = T("needfield"); return; }
                Result = values.ToDictionary(member => member.Key, member => member.Value);
            }
            else
            {
                var dictionary = new Dictionary<string, object> { { selected, selected == "value" || selected == "phase" ? (object)true : text.Text } };
                if (selected == "object" && !string.IsNullOrWhiteSpace(className.Text)) dictionary["class"] = className.Text.Trim(); Result = dictionary;
            }
            DialogResult = DialogResult.OK; Close();
        }
        public static string Describe(object value)
        {
            if (value is IDictionary) return T("type.struct") + " (" + ((IDictionary)value).Count.ToString(CultureInfo.InvariantCulture) + ")";
            if (value is IEnumerable && !(value is string)) return T("type.array") + " (" + ((IEnumerable)value).Cast<object>().Count().ToString(CultureInfo.InvariantCulture) + ")";
            return Convert.ToString(value, CultureInfo.CurrentCulture);
        }
        sealed class Member { public string Key, Display; public object Value; public override string ToString() { return Display; } }
    }
}
