using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class PresetContentsGrid : DataGridView
    {
        int measuredWidth;
        internal int BindingCount { get; private set; }
        internal int SettingCount { get; private set; }

        internal PresetContentsGrid()
        {
            DoubleBuffered = true; ReadOnly = true;
            AllowUserToAddRows = AllowUserToDeleteRows = AllowUserToResizeRows = AllowUserToResizeColumns = false;
            RowHeadersVisible = false; MultiSelect = false;
            BorderStyle = BorderStyle.None; CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            BackgroundColor = EditorTheme.Surface; GridColor = EditorTheme.Line;
            EnableHeadersVisualStyles = false; ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Muted,
                SelectionBackColor = EditorTheme.Floating, SelectionForeColor = EditorTheme.Muted,
                Padding = new Padding(6), WrapMode = DataGridViewTriState.True
            };
            DefaultCellStyle = new DataGridViewCellStyle {
                BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Ink,
                SelectionBackColor = EditorTheme.Hover, SelectionForeColor = EditorTheme.Ink,
                Padding = new Padding(6), WrapMode = DataGridViewTriState.True,
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            AlternatingRowsDefaultCellStyle.BackColor = EditorTheme.RowAlternate;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect; ScrollBars = ScrollBars.Vertical;
            string[] names = { "device", "action", "keys", "trigger", "settings" };
            float[] weights = { 13, 24, 19, 18, 26 };
            for (int i = 0; i < names.Length; i++)
                Columns.Add(new DataGridViewTextBoxColumn { Name = names[i], HeaderText = I18n.Str("preset.contents." + names[i]), FillWeight = weights[i], SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        internal void ShowPreset(Preset preset)
        {
            Rows.Clear(); BindingCount = SettingCount = 0;
            if (preset == null) return;
            var config = preset.KeyEnhancement;
            foreach (var scope in preset.Scopes)
            {
                string device = scope.Family ?? I18n.Str("editor.keyboard");
                foreach (string action in PresetBaseline.Actions)
                {
                    var mapping = preset.UserMappings.FirstOrDefault(item => item.Action == action && item.Family == scope.Family);
                    var keys = PresetBaseline.Keys(preset.BaselineId, action, scope.Family != null).AsEnumerable();
                    if (mapping != null) keys = keys.Except(mapping.Removed).Concat(mapping.Added).Distinct();
                    var native = config == null ? null : config.NativeBindings.FirstOrDefault(item => item.FunctionId == action && item.Device == (scope.Family == null ? "keyboard" : "gamepad"));
                    if (native != null) keys = native.Keys;
                    Binding(device, BindingWorkspace.FunctionLabel(action), Alternatives(keys), I18n.Str("unified.trigger.default"), "");
                }
            }
            if (config != null)
            {
                int ruleIndex = 0;
                foreach (var native in config.NativeBindings.Where(item => !PresetBaseline.Actions.Contains(item.FunctionId)))
                    Binding(I18n.Str(native.Device == "gamepad" ? "editor.gamepad" : "editor.keyboard"), BindingWorkspace.FunctionLabel(native.FunctionId), Alternatives(native.Keys), I18n.Str("unified.trigger.default"), "");
                foreach (var rule in config.Rules)
                {
                    var inputKeys = rule.Keys.Concat(rule.Watch).Concat(rule.Steps.SelectMany(step => step)).Concat(string.IsNullOrEmpty(rule.Axis) ? new string[0] : new[] { rule.Axis }).Distinct().ToArray();
                    string devices = string.Join(" / ", inputKeys.Select(key => I18n.Str(EnhancementEditing.Pad(key) ? "editor.gamepad" : "editor.keyboard")).Distinct());
                    if (devices.Length == 0) devices = I18n.Str("binding.scope.global");
                    string input = rule.Trigger == "sequence" ? string.Join(" → ", rule.Steps.Select(Keys)) : !string.IsNullOrEmpty(rule.Axis) ? BindingButton.FriendlyKey(rule.Axis) : Keys(rule.Keys);
                    string trigger = I18n.Str("unified.trigger." + (rule.DefaultTrigger ? "default" : rule.Trigger));
                    if (rule.Trigger == "hold" || rule.Trigger == "tap") trigger += " " + Number(rule.Seconds) + " s";
                    if (rule.Trigger == "multi_tap") trigger += " ×" + rule.Count;
                    if (rule.Trigger == "pulse" || rule.Trigger == "multi_tap" || rule.Trigger == "sequence") trigger += " " + Number(rule.Interval) + " s";
                    foreach (var output in rule.Outputs)
                    {
                        Rows.Insert(ruleIndex++, devices, OutputLabel(output), input, trigger, RuleSettings(rule, output, config.Enabled));
                        BindingCount++;
                    }
                }
                Setting("enhancement.title", State(config.Enabled));
                Setting("preset.contents.hotkeys", State(config.RuntimeHotkeys));
                foreach (var output in config.CustomFunctions)
                    Setting("unified.customtarget", OutputLabel(output) + Environment.NewLine + Serialize(output));
                foreach (var suppression in config.Suppressions)
                    Suppression(suppression);
                foreach (var rule in config.Rules)
                    foreach (var suppression in rule.Suppressions)
                        Suppression(suppression, I18n.Str("unified.priority") + " " + rule.Priority + " · " + State(rule.Enabled || rule.BlockOriginalWhenDisabled));
                foreach (string tag in config.NativeAbilitySuppressions)
                    Setting("unified.nativeAbilityTrigger", BindingWorkspace.FunctionLabel("ability:" + tag) + " · " + State(false));
                foreach (var hook in config.Hooks)
                    Setting("unified.advanced.existinghooks", Serialize(hook));
                if (!string.IsNullOrWhiteSpace(config.CustomLua)) Setting("unified.advanced.script", config.CustomLua);
            }
            FitRows();
        }

        static string OutputLabel(KeyEnhancementOutput output)
        {
            if (!string.IsNullOrWhiteSpace(output.Name)) return output.Name;
            string id = output.FunctionId;
            if (string.IsNullOrEmpty(id))
            {
                if (output.Kind == "move") id = output.Tag;
                else if (output.Kind == "scheme") id = "scheme:" + output.Tag;
                else if (output.Kind == "mapping") id = output.MappingName;
                else if (output.Kind == "action") id = "action:" + System.Text.RegularExpressions.Regex.Replace(output.Path ?? "", "\\.[^./]+$", "");
                else if (output.Kind == "ability") id = "ability:" + output.Tag;
                else id = output.Path ?? output.Property ?? output.Tag ?? output.Code ?? output.Kind;
            }
            return BindingWorkspace.FunctionLabel(id);
        }
        static string RuleSettings(KeyEnhancementRule rule, KeyEnhancementOutput output, bool enabled)
        {
            var parts = new List<string>();
            if (!enabled || !rule.Enabled) parts.Add(I18n.Str("unified.inactive"));
            if (rule.Priority != 0) parts.Add(I18n.Str("unified.priority") + " " + rule.Priority);
            if (rule.Consume || rule.ConsumeKeys.Count > 0) parts.Add(I18n.Str("unified.consume") + ": " + Keys(rule.ConsumeKeys));
            if (rule.BypassResources) parts.Add(I18n.Str("binding.resources.enabled"));
            if (rule.BypassRestrictions) parts.Add(I18n.Str("binding.restrictions.enabled", string.Join(" / ", rule.RestrictionBypasses.Select(kind => I18n.Str("binding.restrictions." + kind)))));
            if (rule.SuperArmor) parts.Add(I18n.Str("binding.effects.superArmor"));
            if (rule.Invincible) parts.Add(I18n.Str("binding.effects.invincible"));
            if (rule.DamageMultiplier != 1) parts.Add(I18n.Str("binding.tuning.damage") + " ×" + Number(rule.DamageMultiplier));
            if (rule.StartupSpeed != 1) parts.Add(I18n.Str("binding.tuning.startup") + " ×" + Number(rule.StartupSpeed));
            if (rule.RecoverySpeed != 1) parts.Add(I18n.Str("binding.tuning.recovery") + " ×" + Number(rule.RecoverySpeed));
            if (rule.Trigger == "axis") { parts.Add(I18n.Str("unified.threshold") + ": " + Number(rule.Threshold)); parts.Add(I18n.Str("editing.direction") + ": " + I18n.Str("editing." + rule.AxisDirection)); }
            if (!string.IsNullOrWhiteSpace(rule.WhenLua)) parts.Add(I18n.Str("ruleeditor.when") + ": " + rule.WhenLua);
            if (!string.IsNullOrWhiteSpace(rule.EvaluateLua)) parts.Add(I18n.Str("ruleeditor.evaluate") + ": " + rule.EvaluateLua);
            if (rule.Watch.Count > 0) parts.Add(I18n.Str("unified.watchKeys") + ": " + Keys(rule.Watch));
            if (output.Value != null) parts.Add(I18n.Str("unified.advanced.value") + ": " + Serialize(output.Value));
            if (output.Args != null || output.Payload != null) parts.Add(I18n.Str("unified.advanced.arguments") + ": " + Serialize(output.Payload ?? output.Args));
            if (!string.IsNullOrWhiteSpace(output.Code)) parts.Add(I18n.Str("unified.advanced.script") + ": " + output.Code);
            return string.Join(Environment.NewLine, parts);
        }
        void Binding(string device, string action, string keys, string trigger, string settings)
        { Rows.Add(device, action, keys, trigger, settings); BindingCount++; }
        void Setting(string key, string value)
        { Rows.Add(I18n.Str("binding.scope.global"), I18n.Str(key), "", "", value); SettingCount++; }
        void Suppression(KeyEnhancementSuppression item, string settings = null)
        {
            Rows.Add("", BindingWorkspace.FunctionLabel(item.FunctionId ?? "action:" + item.ActionPath), string.IsNullOrEmpty(item.Key) ? "—" : BindingButton.FriendlyKey(item.Key), I18n.Str("unified.removed"), settings ?? "");
            SettingCount++;
        }
        static string Alternatives(IEnumerable<string> keys)
        { var values = keys.Select(BindingButton.FriendlyKey).ToArray(); return values.Length == 0 ? "—" : string.Join(" / ", values); }
        static string Keys(IEnumerable<string> keys)
        { var values = keys.Select(BindingButton.FriendlyKey).ToArray(); return values.Length == 0 ? "—" : string.Join(" + ", values); }
        static string Number(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        static string State(bool value) { return I18n.Str(value ? "mod.enabled" : "mod.disabled"); }
        static string Serialize(object value) { return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Serialize(value); }
        internal void FitRows()
        {
            if (Rows.Count == 0 || ClientSize.Width <= 0) return;
            AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);
            measuredWidth = ClientSize.Width; ClearSelection();
        }
        protected override void OnVisibleChanged(EventArgs e)
        { base.OnVisibleChanged(e); if (Visible) FitRows(); }
        protected override void OnSizeChanged(EventArgs e)
        { base.OnSizeChanged(e); if (IsHandleCreated && ClientSize.Width != measuredWidth) FitRows(); }
    }
}
