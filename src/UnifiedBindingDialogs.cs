using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal static partial class UnifiedBindingUi
    {
        internal static string Text(string key) { return I18n.Str("unified." + key); }
        internal static readonly Color Blue = Color.FromArgb(124, 190, 214);
        internal static string KeyEnhancementHint { get { return I18n.Str("enhancement.title") + " · " + Text("requiresbrief"); } }
        internal static void PaintKeyEnhancementMark(Graphics graphics, Rectangle bounds)
        {
            var state = graphics.Save();
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 16f, bounds.Height / 16f);
            using (var shape = new System.Drawing.Drawing2D.GraphicsPath())
            {
                shape.AddBezier(8, 1, 10, 5, 6, 6, 11, 8);
                shape.AddBezier(11, 8, 12, 6, 12, 5, 12, 5);
                shape.AddBezier(12, 5, 18, 13, 10, 17, 6, 14);
                shape.AddBezier(6, 14, 1, 11, 4, 6, 8, 1);
                using (var brush = new SolidBrush(EditorTheme.KeyEnhancement)) graphics.FillPath(brush, shape);
                using (var pen = new Pen(Color.FromArgb(225, 216, 255), 1.2f)) graphics.DrawBezier(pen, 7, 13, 5, 10, 9, 10, 8, 7);
            }
            graphics.Restore(state);
        }
        internal static EditorLayout Grid(int columns)
        { return new EditorLayout { Dock = DockStyle.Fill, ColumnCount = columns, Margin = Padding.Empty, BackColor = EditorTheme.Surface, AutoSizeMode = AutoSizeMode.GrowAndShrink }; }
        internal static Label Label(string text)
        { return new EditorLabel { Text = text, ForeColor = EditorTheme.Ink, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(4, 0, 4, 0) }; }
        internal static TextBox Search()
        { return new TextBox { Dock = DockStyle.Fill, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink, BorderStyle = BorderStyle.FixedSingle }; }
        internal static string ScriptText(string value)
        { return (value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"); }
        internal static EditorButton Button(string text, string name)
        { return new EditorButton { Text = text, Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(78, 34), Margin = new Padding(4) }; }
        internal static void Form(EditorWindow form, string title, Size size, Size minimum)
        {
            form.Text = title; form.Font = new Font("Microsoft YaHei UI", 10f); form.AutoScaleMode = AutoScaleMode.Dpi;
            form.AutoScaleDimensions = new SizeF(96, 96); form.BackColor = EditorTheme.Background; form.ForeColor = EditorTheme.Ink;
            form.ClientSize = size; form.MinimumSize = minimum; form.StartPosition = FormStartPosition.CenterParent;
        }
        internal static string BindingText(BindingEntry entry)
        { return BindingEntryText.BindingText(entry); }
        internal static string EntryScope(BindingEntry entry)
        { return entry.SchemeScoped ? I18n.Str("schemes.scope") : I18n.Str(entry.IsNative && !entry.IsRuntimeBinding && !entry.ReadOnly ? "binding.scope.scheme"
            : entry.Keys.Concat(new[] { entry.Axis ?? "" }).Any(key => key.StartsWith("Gamepad_", StringComparison.Ordinal)) ? "binding.scope.gamepad" : "binding.scope.global"); }
        internal static string EntryDescription(BindingEntry entry)
        { return BindingEntryText.EntryDescription(entry); }
        internal static string FunctionIdentity(BindingFunction function)
        {
            string kind = function.NativeAction != null ? Text("nativefunction")
                : function.FixedKeys != null ? I18n.Str("unified.groups.movement")
                : function.Id.StartsWith("move:", StringComparison.Ordinal) ? CompleteMoveCatalog.KindLabel(function.Id)
                : function.Id.StartsWith("action:", StringComparison.Ordinal) ? I18n.Str("binding.function.action")
                : function.Id.StartsWith("ability:", StringComparison.Ordinal) ? I18n.Str("binding.function.ability") : Text("customtarget");
            return kind + " · " + UnifiedActionChoiceDialog.FunctionCode(function);
        }
        internal static bool UsesKey(BindingEntry entry, string key)
        {
            return key != null && entry.Keys.Concat(new[] { entry.Axis ?? "" })
                .Concat(entry.Rule == null ? new string[0] : entry.Rule.Steps.SelectMany(step => step).Concat(entry.Rule.Watch))
                .Any(input => BindingViewText.PhysicalKey(input) == BindingViewText.PhysicalKey(key));
        }
        internal static bool ShowsIndependentBindings(BindingFunction function)
        { return function.RequiresKeyEnhancement && function.NativeAction == null && function.FixedKeys == null; }
        internal static bool HasNativeTrigger(BindingFunction function)
        { return function.Targets.Any(target => target.Kind == "ability"); }
        internal static string[] PresetEnhancementDescriptions(EditorSession session, Preset preset)
        {
            if (preset.KeyEnhancement == null) return new string[0];
            var entries = new[] { false, true }.SelectMany(padDevice => new BindingWorkspace(session, preset.KeyEnhancement,
                new KeyEnhancementConfiguration(), padDevice, -1).GetSnapshot()).Where(entry => !entry.IsNative)
                .GroupBy(entry => entry.Id).Select(group => group.First());
            return preset.KeyEnhancement.NativeBindings.Select(item => I18n.Str(item.Device == "gamepad" ? "editor.gamepad" : "editor.keyboard") + " · "
                    + BindingWorkspace.FunctionLabel(item.FunctionId) + " · " + BindingViewText.Keys(session.Defaults.ForAction(item.FunctionId, item.Device == "gamepad"))
                    + " → " + BindingViewText.Keys(item.Keys.ToArray()) + (preset.KeyEnhancement.Enabled ? "" : " · " + Text("inactive")))
                .Concat(entries.Select(entry => entry.Label + " · " + EntryDescription(entry)))
                .Concat(preset.KeyEnhancement.NativeAbilitySuppressions.Select(tag => BindingWorkspace.FunctionLabel("ability:" + tag) + " · " + I18n.Str("binding.native.replaced")))
                .Concat(preset.KeyEnhancement.Hooks.Select(hook => I18n.Str("unified.advanced.kind.hook") + " · " + Convert.ToString(hook["path"])))
                .Concat(string.IsNullOrWhiteSpace(preset.KeyEnhancement.CustomLua) ? new string[0] : new[] { I18n.Str("unified.advanced.kind.lua") }).ToArray();
        }

        internal static string NativeEntryHint(BindingWorkspace workspace, BindingFunction function)
        {
            if (!ShowsIndependentBindings(function)) return "";
            string[] labels = NativeEntries(workspace, function).Select(item => item.Label).Distinct().ToArray();
            return Text("additionalhint") + (labels.Length == 0 ? "" : Environment.NewLine + I18n.Str("unified.nativeentryhint", string.Join(" / ", labels)));
        }
        internal static BindingFunction[] NativeEntries(BindingWorkspace workspace, BindingFunction function)
        {
            var mappings = new HashSet<string>(function.Targets.SelectMany(target => target.MappingNames));
            return workspace.Functions.Where(item => item.NativeAction != null && mappings.Contains(item.NativeAction)).ToArray();
        }
        internal static string EntryState(BindingEntry entry)
        {
            if (entry.Suppressed) return Text("removed");
            if (entry.ExecutionOwner == "inactive") return I18n.Str("ownership.owner.off");
            if (entry.ExecutionOwner == "uncovered") return I18n.Str("ownership.owner.uncovered");
            if (!entry.Enabled || !entry.Active) return Text("inactive");
            string state = entry.Pending ? I18n.Str("views.pending") : entry.IsDefault ? I18n.Str("views.original") : I18n.Str("views.savedcustom");
            return entry.ExecutionOwner == "scheduler" ? state + " · " + I18n.Str("ownership.owner.scheduler") : state;
        }
        internal static string EntryExecution(BindingEntry entry)
        {
            string owner = entry.ExecutionOwner == "scheduler" ? "scheduler" : entry.ExecutionOwner == "game" ? "game"
                : entry.ExecutionOwner == "inactive" ? "off" : "uncovered";
            return I18n.Str("ownership.entry.format", I18n.Str(entry.IsNative && !entry.IsRuntimeBinding ? entry.ReadOnly ? "ownership.source.fixed" : "ownership.source.save" : "ownership.source.rule"), I18n.Str("ownership.owner." + owner));
        }
        internal static string EntryPolicy(BindingEntry entry)
        {
            if (entry.ExecutionOwner == "inactive") return "";
            if (entry.ExecutionOwner != "scheduler") return I18n.Str("ownership.policy.original");
            var parts = new List<string>();
            if (entry.OriginalInputBlocked) parts.Add(I18n.Str("ownership.policy.replaced"));
            if (entry.BypassResources) parts.Add(I18n.Str("binding.resources.enabled"));
            if (entry.BypassRestrictions) parts.Add(I18n.Str("binding.restrictions.enabled", BindingRestrictionKinds.Describe(entry.RestrictionBypasses)));
            if (entry.DamageMultiplier != 1) parts.Add(I18n.Str("binding.tuning.damage") + " ×" + entry.DamageMultiplier.ToString("0.##"));
            if (entry.SuperArmor) parts.Add(I18n.Str("binding.effects.superArmor"));
            if (entry.Invincible) parts.Add(I18n.Str("binding.effects.invincible"));
            if (entry.StartupSpeed != 1) parts.Add(I18n.Str("binding.tuning.startup") + " ×" + entry.StartupSpeed.ToString("0.##"));
            if (entry.RecoverySpeed != 1) parts.Add(I18n.Str("binding.tuning.recovery") + " ×" + entry.RecoverySpeed.ToString("0.##"));
            if (entry.Rule != null && entry.Rule.ConsumeKeys.Count > 0)
                parts.Add(I18n.Str("ownership.policy.consumesome", string.Join(" + ", entry.Rule.ConsumeKeys.Select(BindingButton.FriendlyKey))));
            else if (entry.Rule != null && entry.Rule.Consume) parts.Add(I18n.Str("ownership.policy.consumeall"));
            else parts.Add(I18n.Str("ownership.policy.coexist"));
            return string.Join(" · ", parts);
        }
        internal static string CoverageSummary(BindingWorkspace workspace)
        {
            if (!workspace.KeyEnhancementEnabled) return I18n.Str("ownership.coverage.off");
            return I18n.Str("ownership.coverage.details") + Environment.NewLine + I18n.Str("ownership.coverage.registered") + Environment.NewLine + I18n.Str("ownership.coverage.scope")
                + (workspace.UncoveredInputs.Length == 0 ? "" : Environment.NewLine + I18n.Str("ownership.coverage.uncovered", string.Join(Environment.NewLine, workspace.UncoveredInputs)));
        }
    }

    public sealed class UnifiedActionChoiceDialog : EditorWindow
    {
        sealed class FunctionGroup
        {
            internal string Path, Label;
            public override string ToString() { return Label; }
        }
        readonly BindingWorkspace workspace;
        readonly string key;
        readonly Func<bool> enableRequested;
        readonly Action changed;
        readonly TextBox search = UnifiedBindingUi.Search();
        readonly ComboBox groups = new EditorComboBox();
        readonly ListBox functions = new ListBox();
        readonly EditorButton accept = UnifiedBindingUi.Button(I18n.Str("views.add"), "addSelectedAction");
        readonly Label status = new EditorLabel { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(620, 0), ForeColor = EditorTheme.Muted };
        readonly ToolTip tip = new ToolTip();
        readonly TextBox selectionDetails = new TextBox { Name = "selectedFunctionDetails", ReadOnly = true, Multiline = true,
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Muted, Margin = new Padding(4, 6, 4, 6) };
        Dictionary<string, BindingEntry[]> assigned = new Dictionary<string, BindingEntry[]>();
        readonly BindingEntry template;
        readonly CheckBox nativeAbilityTrigger = new EditorCheckBox { Name = "nativeAbilityTrigger", Dock = DockStyle.Fill, AutoSize = true, ThreeState = true };
        string selectedGroup = "native";
        int hoveredFunction = -1;
        bool changingGroups;
        public string Result { get; private set; }
        public UnifiedActionChoiceDialog(BindingWorkspace workspace, string key, Func<bool> enableRequested)
            : this(workspace, key, enableRequested, null) { }
        public UnifiedActionChoiceDialog(BindingWorkspace workspace, string key, Func<bool> enableRequested, Action changed)
            : this(workspace, key, enableRequested, changed, null) { }
        public UnifiedActionChoiceDialog(BindingWorkspace workspace, string key, Func<bool> enableRequested, Action changed, BindingEntry template)
        {
            this.template = template;
            this.workspace = workspace; this.key = key; this.enableRequested = enableRequested; this.changed = changed;
            selectedGroup = workspace.KeyEnhancementEnabled ? "" : "native";
            UnifiedBindingUi.Form(this, I18n.Str("views.addaction") + " · " + BindingButton.FriendlyKey(key), new Size(760, 700), new Size(560, 520));
            var root = UnifiedBindingUi.Grid(1); root.Padding = new Padding(16, 10, 16, 12); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(UnifiedBindingUi.Label(I18n.Str("editing.multiselect")), 0, 0); root.Controls.Add(search, 0, 1);
            groups.Name = "functionGroup"; groups.Dock = DockStyle.Fill; groups.DropDownStyle = ComboBoxStyle.DropDownList;
            groups.Items.Add(new FunctionGroup { Path = "", Label = I18n.Str("unified.groups.all") + " (" + workspace.Functions.Length + ")" });
            var paths = new HashSet<string>();
            foreach (var function in workspace.Functions)
                for (int depth = 1; depth <= function.GroupPath.Length; depth++)
                {
                    string path = string.Join("/", function.GroupPath.Take(depth));
                    if (paths.Add(path))
                    {
                        int count = workspace.Functions.Count(item => string.Join("/", item.GroupPath) == path || string.Join("/", item.GroupPath).StartsWith(path + "/", StringComparison.Ordinal));
                        groups.Items.Add(new FunctionGroup { Path = path, Label = string.Join(" / ", function.GroupLabels.Take(depth)) + " (" + count + ")" });
                    }
                }
            groups.SelectedIndex = Enumerable.Range(0, groups.Items.Count).FirstOrDefault(index => ((FunctionGroup)groups.Items[index]).Path == selectedGroup, 0);
            groups.SelectedIndexChanged += delegate
            {
                if (changingGroups) return;
                var selected = groups.SelectedItem as FunctionGroup; selectedGroup = selected == null ? "" : selected.Path; Populate();
            };
            root.Controls.Add(groups, 0, 2);
            functions.Dock = DockStyle.Fill; functions.BackColor = EditorTheme.Surface; functions.ForeColor = EditorTheme.Ink; functions.BorderStyle = BorderStyle.FixedSingle;
            functions.DrawMode = DrawMode.OwnerDrawFixed; functions.ItemHeight = Font.Height * 3 + 20; functions.IntegralHeight = false;
            functions.SelectionMode = SelectionMode.MultiExtended;
            functions.DrawItem += DrawFunction; functions.SelectedIndexChanged += delegate { UpdateSelection(); }; functions.DoubleClick += delegate { AddSelected(); };
            functions.MouseMove += delegate(object sender, MouseEventArgs e) {
                int index = functions.IndexFromPoint(e.Location); if (index == hoveredFunction) return; hoveredFunction = index;
                var function = index < 0 ? null : (BindingFunction)functions.Items[index];
                tip.SetToolTip(functions, function == null ? "" : function.Label + Environment.NewLine + function.TechnicalName
                    + Environment.NewLine + string.Join(" / ", function.GroupLabels) + Environment.NewLine + AssignmentText(function, true));
            };
            functions.HandleCreated += delegate { EditorArtwork.DarkScrollbars(functions); }; root.Controls.Add(functions, 0, 3);
            selectionDetails.HandleCreated += delegate { EditorArtwork.DarkScrollbars(selectionDetails); };
            root.Controls.Add(selectionDetails, 0, 4);
            var footer = UnifiedBindingUi.Grid(2); footer.AutoSize = true; footer.RowCount = 4;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < 4; row++) footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status.ForeColor = EditorTheme.Muted; status.AutoSize = true; footer.Controls.Add(status, 0, 0); footer.SetColumnSpan(status, 2);
            nativeAbilityTrigger.Text = UnifiedBindingUi.Text("nativeAbilityTrigger");
            tip.SetToolTip(nativeAbilityTrigger, UnifiedBindingUi.Text("nativeAbilityHelp"));
            footer.Controls.Add(nativeAbilityTrigger, 0, 1); footer.SetColumnSpan(nativeAbilityTrigger, 2);
            var custom = UnifiedBindingUi.Button(UnifiedBindingUi.Text("addcustom"), "addCustomFunction"); custom.Click += delegate { AddCustom(); };
            var configure = UnifiedBindingUi.Button(I18n.Str("binding.configure"), "configureBinding");
            configure.Click += delegate {
                var selected = functions.SelectedItems.Cast<BindingFunction>().ToArray();
                var function = selected.FirstOrDefault(UnifiedBindingUi.HasNativeTrigger) ?? selected.FirstOrDefault(); if (function == null) return;
                using (var dialog = new UnifiedBindingEditDialog(workspace, function.Id, template, key, enableRequested, functions.SelectedItems.Count > 1))
                {
                    dialog.NativeTriggerChoice = NativeTriggerChoice;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        try { workspace.AddFunctions(selected.Select(item => item.Id), dialog.Result); Result = function.Id; if (changed != null) changed(); DialogResult = DialogResult.OK; Close(); }
                        catch (Exception ex) { status.Text = ex.Message; }
                }
            };
            var cancel = UnifiedBindingUi.Button(I18n.Str("editor.cancel"), "cancel"); cancel.DialogResult = DialogResult.Cancel;
            foreach (var button in new[] { custom, configure, cancel, accept }) button.Dock = DockStyle.Fill;
            footer.Controls.Add(custom, 0, 2); footer.Controls.Add(configure, 1, 2); footer.Controls.Add(cancel, 0, 3); footer.Controls.Add(accept, 1, 3); root.Controls.Add(footer, 0, 5);
            accept.Click += delegate { AddSelected(); }; search.TextChanged += delegate { functions.SelectedIndex = -1; Populate(); }; Controls.Add(root); AcceptButton = accept; CancelButton = cancel; Populate();
        }
        void Populate()
        {
            assigned = workspace.GetSnapshot().Where(entry => !entry.Suppressed).GroupBy(entry => entry.FunctionId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            hoveredFunction = -1; tip.SetToolTip(functions, "");
            string selected = functions.SelectedItem is BindingFunction ? ((BindingFunction)functions.SelectedItem).Id : null;
            functions.BeginUpdate(); functions.Items.Clear(); string query = search.Text.Trim();
            changingGroups = true;
            groups.SelectedIndex = Enumerable.Range(0, groups.Items.Count).FirstOrDefault(index => ((FunctionGroup)groups.Items[index]).Path == (query.Length > 0 ? "" : selectedGroup), 0);
            groups.Enabled = query.Length == 0; changingGroups = false;
            foreach (var function in workspace.FindFunctions(query))
                if (query.Length > 0 || string.IsNullOrEmpty(selectedGroup) || string.Join("/", function.GroupPath) == selectedGroup
                    || string.Join("/", function.GroupPath).StartsWith(selectedGroup + "/", StringComparison.Ordinal)) functions.Items.Add(function);
            functions.SelectedIndex = selected == null ? -1 : Enumerable.Range(0, functions.Items.Count).FirstOrDefault(index => ((BindingFunction)functions.Items[index]).Id == selected, -1);
            if (functions.SelectedIndex < 0 && functions.Items.Count > 0)
                functions.SelectedIndex = query.Length > 0 ? 0 : Enumerable.Range(0, functions.Items.Count).FirstOrDefault(index => !((BindingFunction)functions.Items[index]).RequiresKeyEnhancement, 0);
            functions.EndUpdate(); UpdateSelection();
        }
        void UpdateSelection()
        {
            var function = functions.SelectedItem as BindingFunction; accept.Enabled = function != null;
            var nativeFunctions = functions.SelectedItems.Cast<BindingFunction>().Where(UnifiedBindingUi.HasNativeTrigger).ToArray();
            nativeAbilityTrigger.Visible = nativeFunctions.Length > 0;
            bool[] choices = nativeFunctions.Select(item => workspace.UsesNativeTrigger(item.Id)).Distinct().ToArray();
            nativeAbilityTrigger.ThreeState = choices.Length > 1;
            nativeAbilityTrigger.CheckState = choices.Length == 1 ? choices[0] ? CheckState.Checked : CheckState.Unchecked : CheckState.Indeterminate;
            status.Text = functions.SelectedItems.Cast<BindingFunction>().Any(item => item.RequiresKeyEnhancement) && !workspace.KeyEnhancementEnabled ? UnifiedBindingUi.Text("requiresbrief") : "";
            status.ForeColor = function != null && function.RequiresKeyEnhancement && !workspace.KeyEnhancementEnabled ? EditorTheme.KeyEnhancement : EditorTheme.Muted;
            selectionDetails.Text = function == null ? "" : UnifiedBindingUi.FunctionIdentity(function) + Environment.NewLine
                + string.Join(" / ", function.GroupLabels) + Environment.NewLine + AssignmentText(function, true);
            string usage = function == null ? "" : CompleteMoveCatalog.Usage(function.Id);
            string nativeDetails = function == null ? "" : GameMoveNames.Details(function.Id, search.Text);
            if (nativeDetails.Length > 0) selectionDetails.Text = nativeDetails + Environment.NewLine + selectionDetails.Text;
            if (usage.Length > 0) selectionDetails.Text = usage + Environment.NewLine + selectionDetails.Text;
            tip.SetToolTip(selectionDetails, function == null ? "" : function.TechnicalName);
        }
        string AssignmentText(BindingFunction function, bool full)
        {
            BindingEntry[] entries;
            if (!assigned.TryGetValue(function.Id, out entries)) return I18n.Str("editor.unbound");
            bool here = entries.Any(entry => UnifiedBindingUi.UsesKey(entry, key));
            var ordered = entries.OrderByDescending(entry => UnifiedBindingUi.UsesKey(entry, key));
            return I18n.Str(here ? "binding.assignedhere" : "binding.assigned", string.Join(full ? Environment.NewLine : " / ",
                ordered.Select(entry => UnifiedBindingUi.EntryDescription(entry) + (full ? " · " + UnifiedBindingUi.EntryScope(entry) : "")).Distinct()));
        }
        void AddSelected()
        {
            var function = functions.SelectedItem as BindingFunction; if (function == null) return;
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var entry = template == null ? new BindingEntry { FunctionId = function.Id, Keys = new[] { key }, Trigger = "default" }
                : serializer.Deserialize<BindingEntry>(serializer.Serialize(template));
            entry.KeepNativeTrigger = NativeTriggerChoice;
            try { workspace.AddFunctions(functions.SelectedItems.Cast<BindingFunction>().Select(item => item.Id), entry); Result = function.Id; if (changed != null) changed(); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { status.Text = ex.Message; status.ForeColor = EditorTheme.AccentHover; }
        }
        bool? NativeTriggerChoice
        { get { return !nativeAbilityTrigger.Visible || nativeAbilityTrigger.CheckState == CheckState.Indeterminate ? (bool?)null : nativeAbilityTrigger.Checked; } }
        void AddCustom()
        {
            using (var dialog = new AdvancedFunctionDialog(workspace.Configuration))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try
                    {
                        if (dialog.ResultHook != null || dialog.DeleteHook)
                        {
                            if (dialog.DeleteHook) workspace.Configuration.Hooks.RemoveAt(dialog.HookIndex);
                            else workspace.SetHook(dialog.HookIndex, dialog.ResultHook);
                            if (changed != null) changed(); DialogResult = DialogResult.OK; Close(); return;
                        }
                        var rule = new KeyEnhancementRule { WhenLua = dialog.ConditionLua };
                        if (key == null)
                        {
                            workspace.AddCustomDefinition(dialog.ResultTarget, dialog.FunctionName);
                            if (changed != null) changed(); DialogResult = DialogResult.OK; Close(); return;
                        }
                        var entry = workspace.AddCustom(dialog.ResultTarget, new BindingEntry { Keys = new[] { key }, Trigger = "default", Rule = rule }, dialog.FunctionName);
                        Result = entry.FunctionId; if (changed != null) changed(); DialogResult = DialogResult.OK; Close();
                    }
                    catch (Exception ex) { status.Text = ex.Message; status.ForeColor = EditorTheme.AccentHover; }
        }
        void DrawFunction(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return; var function = (BindingFunction)functions.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? EditorTheme.Hover : EditorTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            bool gated = function.RequiresKeyEnhancement && !workspace.KeyEnhancementEnabled;
            var rect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 5, e.Bounds.Width - 24, Font.Height + 5);
            if (function.RequiresKeyEnhancement)
            {
                int markSize = Font.Height;
                UnifiedBindingUi.PaintKeyEnhancementMark(e.Graphics, new Rectangle(rect.Left, rect.Top, markSize, markSize));
                rect.X += markSize + 5; rect.Width -= markSize + 5;
            }
            TextRenderer.DrawText(e.Graphics, function.Label, Font, rect, gated ? EditorTheme.Muted : EditorTheme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            rect.Y += Font.Height + 4;
            using (var small = new Font(Font.FontFamily, 8.5f))
            {
                TextRenderer.DrawText(e.Graphics, UnifiedBindingUi.FunctionIdentity(function), small, rect, EditorTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                rect.Y += Font.Height + 4;
                BindingEntry[] entries;
                bool bound = assigned.TryGetValue(function.Id, out entries);
                TextRenderer.DrawText(e.Graphics, AssignmentText(function, false), small, rect,
                    bound && entries.Any(entry => entry.Active && UnifiedBindingUi.UsesKey(entry, key)) ? UnifiedBindingUi.Blue : EditorTheme.Muted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
        internal static string FunctionCode(BindingFunction function)
        {
            if (function.IdentityCode != null) return function.IdentityCode.TrimStart().StartsWith("function", StringComparison.Ordinal) ? "Lua" : function.IdentityCode;
            if (function.FixedKeys != null) return "WASD · " + function.Id.Substring(9);
            if (function.Targets.Length == 1) return function.Targets[0].Name;
            string code = function.TechnicalName ?? function.Id;
            if (code.TrimStart().StartsWith("function", StringComparison.Ordinal)) return "Lua";
            int slash = code.LastIndexOf('/'); return slash < 0 ? code : code.Substring(slash + 1);
        }
    }

    public sealed class UnifiedActionBindingsDialog : EditorWindow
    {
        BindingWorkspace workspace;
        readonly BindingFunction function;
        readonly Func<bool> enableRequested;
        readonly Action changed;
        readonly FlowLayoutPanel bindings = new FlowLayoutPanel();
        readonly FlowLayoutPanel nativeEntries = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Name = "relatedNativeEntries" };
        readonly Label status = new EditorLabel { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(620, 0), ForeColor = EditorTheme.Muted };
        readonly ToolTip tip = new ToolTip();
        readonly CheckBox nativeAbilityTrigger = new EditorCheckBox();
        readonly EditorButton editCustomFunction = UnifiedBindingUi.Button(UnifiedBindingUi.Text("editFunction"), "editCustomFunction");
        bool updatingNativeAbilityTrigger;
        public UnifiedActionBindingsDialog(BindingWorkspace workspace, string functionId, Func<bool> enableRequested, Action changed)
        {
            this.workspace = workspace; this.function = workspace.Functions.First(item => item.Id == functionId); this.enableRequested = enableRequested; this.changed = changed;
            UnifiedBindingUi.Form(this, function.Label, new Size(760, 420), new Size(560, 300));
            var root = UnifiedBindingUi.Grid(1); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Padding = new Padding(16, 10, 16, 12); root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, function.RequiresKeyEnhancement ? Font.Height * 2 + 16 : 42)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = UnifiedBindingUi.Label(function.RequiresKeyEnhancement ? function.Label + Environment.NewLine + UnifiedBindingUi.FunctionIdentity(function) : UnifiedBindingUi.Text("bindings")); heading.Font = new Font(Font, FontStyle.Bold); root.Controls.Add(heading, 0, 0);
            if (function.RequiresKeyEnhancement)
            {
                heading.Padding = new Padding(26, 4, 4, 4);
                heading.ForeColor = workspace.KeyEnhancementEnabled ? EditorTheme.Ink : EditorTheme.Muted;
                heading.Paint += delegate(object sender, PaintEventArgs e) { UnifiedBindingUi.PaintKeyEnhancementMark(e.Graphics, new Rectangle(4, (heading.Height - 16) / 2, 16, 16)); };
            }
            bool hasAbility = UnifiedBindingUi.HasNativeTrigger(function);
            string originalHint = UnifiedBindingUi.NativeEntryHint(workspace, function);
            var nativeSettings = UnifiedBindingUi.Grid(1); nativeSettings.AutoSize = true; nativeSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); nativeSettings.Visible = hasAbility || originalHint.Length > 0;
            nativeAbilityTrigger.Name = "nativeAbilityTrigger"; nativeAbilityTrigger.Text = UnifiedBindingUi.Text("nativeAbilityTrigger"); nativeAbilityTrigger.Dock = DockStyle.Fill;
            nativeAbilityTrigger.Visible = hasAbility;
            nativeSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, hasAbility ? 36 : 0)); nativeSettings.Controls.Add(nativeAbilityTrigger, 0, 0);
            string originalControls = function.Id == "ability:Ability.Action.Hun.SpecialDash" ? I18n.Str("binding.original.dash")
                : function.FactoryKeys != null && function.FactoryKeys.Length > 0 ? I18n.Str("binding.original", BindingViewText.Keys(function.FactoryKeys)) : UnifiedBindingUi.Text("nativeAbilityHelp");
            var nativeHelp = UnifiedBindingUi.Label(originalControls); nativeHelp.ForeColor = EditorTheme.Muted; nativeHelp.Visible = hasAbility;
            nativeSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, hasAbility ? 52 : 0)); nativeSettings.Controls.Add(nativeHelp, 0, 1);
            var originalHelp = UnifiedBindingUi.Label(originalHint); originalHelp.Name = "nativeEntryHint"; originalHelp.ForeColor = EditorTheme.Muted; originalHelp.Visible = originalHint.Length > 0; originalHelp.AutoSize = true;
            nativeSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize)); nativeSettings.Controls.Add(originalHelp, 0, 2);
            nativeSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize)); nativeSettings.Controls.Add(nativeEntries, 0, 3); root.Controls.Add(nativeSettings, 0, 1);
            nativeAbilityTrigger.CheckedChanged += delegate
            {
                if (updatingNativeAbilityTrigger) return;
                bool value = nativeAbilityTrigger.Checked;
                updatingNativeAbilityTrigger = true; nativeAbilityTrigger.Checked = workspace.UsesNativeTrigger(function.Id); updatingNativeAbilityTrigger = false;
                Apply(delegate { workspace.SetNativeTrigger(function.Id, value); });
            };
            bindings.Dock = DockStyle.Fill; bindings.AutoScroll = true; bindings.WrapContents = false; bindings.FlowDirection = FlowDirection.TopDown; bindings.BackColor = EditorTheme.Surface; bindings.HandleCreated += delegate { EditorArtwork.DarkScrollbars(bindings); };
            bindings.SizeChanged += delegate { SizeRows(); }; root.Controls.Add(bindings, 0, 2);
            status.ForeColor = EditorTheme.Muted; root.Controls.Add(status, 0, 4);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            var add = UnifiedBindingUi.Button(UnifiedBindingUi.Text("addbinding"), "addBinding"); add.Click += delegate { Edit(null); };
            var restore = UnifiedBindingUi.Button(I18n.Str("views.restore"), "restoreAction"); restore.Click += delegate { Apply(delegate { workspace.Restore(function.Id); }); };
            if (function.NativeAction == null && function.Targets.Length == 0 && !function.Id.StartsWith("scheme:", StringComparison.Ordinal))
            {
                var editFunction = editCustomFunction;
                editFunction.Click += delegate
                {
                    using (var dialog = new AdvancedFunctionDialog())
                    {
                        dialog.LoadTarget(workspace.CustomOutput(function.Id), function.Label);
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                            try
                            {
                                workspace.UpdateCustomOutput(function.Id, dialog.ResultTarget, dialog.FunctionName);
                                if (changed != null) changed(); Close();
                            }
                            catch (Exception ex) { status.Text = ex.Message; }
                    }
                };
                footer.Controls.Add(editFunction);
                var deleteFunction = UnifiedBindingUi.Button(I18n.Str("binding.deletefunction"), "deleteCustomFunction");
                deleteFunction.Click += delegate {
                    workspace.DeleteCustomFunction(function.Id); if (changed != null) changed(); Close();
                };
                footer.Controls.Add(deleteFunction);
            }
            var close = UnifiedBindingUi.Button(I18n.Str("editor.close"), "close");
            Shown += delegate {
                var owner = Owner;
                while (owner != null && !(owner is MainForm)) owner = owner.Owner;
                var host = owner as MainForm;
                if (host == null) { close.Click += delegate { Close(); }; return; }
                bool pad = workspace.Gamepad; int family = workspace.FamilyIndex;
                host.AttachLiveBindingDialog(this, close, status, delegate {
                    workspace = host.BindingDialogWorkspace(pad, family); RenderBindings();
                });
            };
            footer.Controls.AddRange(new Control[] { add, restore, close }); root.Controls.Add(footer, 0, 5); Controls.Add(root); tip.SetToolTip(heading, function.TechnicalName); RenderBindings();
            AcceptButton = close;
            ClientSize = new Size(760, Math.Max(320, Math.Min(620, bindings.Controls.Count * (Font.Height * 3 + 36) + 180 + (nativeSettings.Visible ? nativeSettings.PreferredSize.Height : 0))));
        }
        void RenderBindings()
        {
            foreach (Control child in nativeEntries.Controls.Cast<Control>().ToArray()) child.Dispose();
            if (UnifiedBindingUi.ShowsIndependentBindings(function))
                foreach (var related in UnifiedBindingUi.NativeEntries(workspace, function))
                {
                    var target = related;
                    var effective = workspace.Entries(target.Id).Where(entry => entry.Active && !entry.Suppressed).Select(UnifiedBindingUi.BindingText).Distinct().ToArray();
                    var link = new EditorLinkLabel { Name = "openNativeEntry", Tag = target.Id, AutoSize = true, Margin = new Padding(4, 3, 4, 3),
                        LinkColor = UnifiedBindingUi.Blue, ActiveLinkColor = EditorTheme.Ink, Text = target.Label + " · " + (effective.Length == 0 ? I18n.Str("editor.unbound") : string.Join(" / ", effective)) };
                    link.LinkClicked += delegate {
                        Form owner = this;
                        while (owner != null && !(owner is MainForm)) owner = owner.Owner;
                        var host = owner as MainForm;
                        if (host != null && workspace.LiveEditing)
                        {
                            host.PresentBindingWindow(new UnifiedActionBindingsDialog(workspace, target.Id, enableRequested, changed), this, RenderBindings);
                            return;
                        }
                        using (var dialog = new UnifiedActionBindingsDialog(workspace, target.Id, enableRequested, changed)) dialog.ShowDialog(this);
                        RenderBindings();
                    };
                    tip.SetToolTip(link, I18n.Str("binding.related.open")); nativeEntries.Controls.Add(link);
                }
            updatingNativeAbilityTrigger = true; nativeAbilityTrigger.Checked = workspace.UsesNativeTrigger(function.Id); updatingNativeAbilityTrigger = false;
            var previousRows = bindings.Controls.Cast<TableLayoutPanel>().ToArray();
            var anchor = previousRows.FirstOrDefault(row => row.Bottom > 0);
            string anchorId = anchor == null ? null : ((BindingEntry)anchor.Tag).Id;
            int anchorTop = anchor == null ? 0 : anchor.Top;
            var focusedRow = previousRows.FirstOrDefault(row => row.ContainsFocus);
            string focusName = focusedRow == null ? null : focusedRow.GetControlFromPosition(1, 0).Controls.Cast<Control>().Where(control => control.Focused).Select(control => control.Name).FirstOrDefault();
            var existing = previousRows.ToDictionary(row => ((BindingEntry)row.Tag).Id);
            var entries = workspace.Entries(function.Id);
            bindings.SuspendLayout();
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i]; TableLayoutPanel row;
                    if (!existing.TryGetValue(entry.Id, out row)) { row = CreateBindingRow(); bindings.Controls.Add(row); }
                    row.Tag = entry;
                    var text = (Label)row.GetControlFromPosition(0, 0);
                    text.Text = UnifiedBindingUi.EntryDescription(entry) + Environment.NewLine + UnifiedBindingUi.EntryScope(entry) + Environment.NewLine + UnifiedBindingUi.EntryState(entry);
                    text.ForeColor = entry.Suppressed || !entry.Active ? EditorTheme.Muted : EditorTheme.Ink;
                    var operations = row.GetControlFromPosition(1, 0);
                    operations.Controls["toggleBinding"].Text = I18n.Str(entry.Enabled ? "binding.disable" : "binding.enable");
                    operations.Controls["removeBinding"].Text = I18n.Str(entry.Suppressed ? "views.restore" : "binding.unbind");
                    string explanation = UnifiedBindingUi.EntryExecution(entry) + Environment.NewLine + UnifiedBindingUi.EntryPolicy(entry);
                    tip.SetToolTip(text, function.TechnicalName + Environment.NewLine + explanation + (entry.Shared ? Environment.NewLine + UnifiedBindingUi.Text("shared") : ""));
                    if (bindings.Controls.GetChildIndex(row) != i) bindings.Controls.SetChildIndex(row, i);
                }
                foreach (var row in previousRows.Where(row => !entries.Any(entry => entry.Id == ((BindingEntry)row.Tag).Id)))
                { tip.SetToolTip(row.GetControlFromPosition(0, 0), null); row.Dispose(); }
                SizeRows();
            }
            finally { bindings.ResumeLayout(true); }
            var currentRows = bindings.Controls.Cast<TableLayoutPanel>().ToArray();
            if (focusedRow != null && focusedRow.IsDisposed && currentRows.Length > 0)
            {
                var next = currentRows[Math.Min(Array.IndexOf(previousRows, focusedRow), currentRows.Length - 1)];
                var button = next.GetControlFromPosition(1, 0).Controls[focusName ?? "editBinding"];
                if (button != null) button.Focus();
            }
            if (anchorId != null && currentRows.Length > 0)
            {
                var next = currentRows.FirstOrDefault(row => ((BindingEntry)row.Tag).Id == anchorId)
                    ?? currentRows[Math.Min(Array.IndexOf(previousRows, anchor), currentRows.Length - 1)];
                bindings.AutoScrollPosition = new Point(0, Math.Max(0, next.Top - bindings.AutoScrollPosition.Y - anchorTop));
            }
            editCustomFunction.Enabled = true;
            status.Text = bindings.Controls.Count == 0 ? UnifiedBindingUi.ShowsIndependentBindings(function) ? UnifiedBindingUi.Text("additionalnone") : I18n.Str("editor.unbound") : ""; SizeRows();
            if (!workspace.KeyEnhancementEnabled && function.RequiresKeyEnhancement) status.Text = UnifiedBindingUi.KeyEnhancementHint;
            status.ForeColor = !workspace.KeyEnhancementEnabled && function.RequiresKeyEnhancement ? EditorTheme.KeyEnhancement : EditorTheme.Muted;
            string[] uncovered = workspace.UncoveredInputs.Where(input => input.IndexOf(function.Id, StringComparison.OrdinalIgnoreCase) >= 0
                || !string.IsNullOrEmpty(function.TechnicalName) && input.IndexOf(function.TechnicalName, StringComparison.OrdinalIgnoreCase) >= 0
                || input.IndexOf(UnifiedActionChoiceDialog.FunctionCode(function), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (workspace.KeyEnhancementEnabled && uncovered.Length > 0) status.Text = I18n.Str("ownership.coverage.uncovered", string.Join(" · ", uncovered));
            else if (workspace.KeyEnhancementEnabled && entries.Any(entry => entry.ExecutionOwner == "uncovered")) status.Text = I18n.Str("ownership.owner.uncovered");
            tip.SetToolTip(status, UnifiedBindingUi.CoverageSummary(workspace));
        }
        TableLayoutPanel CreateBindingRow()
        {
            var row = UnifiedBindingUi.Grid(2); row.Dock = DockStyle.None; row.AutoSize = false; row.Height = Font.Height * 3 + 28; row.Padding = new Padding(4); row.Margin = new Padding(0, 0, 0, 8);
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var text = UnifiedBindingUi.Label(""); text.Name = "bindingExecution"; row.Controls.Add(text, 0, 0);
            var operations = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            var toggle = UnifiedBindingUi.Button("", "toggleBinding");
            toggle.Click += delegate { var item = (BindingEntry)row.Tag; Apply(delegate { workspace.SetEnabled(item, !item.Enabled); }); };
            var edit = UnifiedBindingUi.Button(UnifiedBindingUi.Text("edit"), "editBinding"); edit.Click += delegate { Edit((BindingEntry)row.Tag); };
            var remove = UnifiedBindingUi.Button("", "removeBinding");
            remove.Click += delegate { var item = (BindingEntry)row.Tag; Apply(delegate { if (item.Suppressed) workspace.Restore(item); else workspace.Remove(item); }); };
            var tools = UnifiedBindingUi.Button("⋯", "bindingTools"); tools.MinimumSize = new Size(34, 34); tools.AccessibleName = I18n.Str("editing.copy");
            operations.Controls.AddRange(new Control[] { toggle, edit, remove, tools }); row.Controls.Add(operations, 1, 0);
            var menu = new ContextMenuStrip();
            menu.Items.Add(I18n.Str("editing.copy"), null, delegate {
                var item = (BindingEntry)row.Tag;
                using (var picker = new UnifiedActionChoiceDialog(workspace, item.Keys.FirstOrDefault(), enableRequested, changed, item)) picker.ShowDialog(this);
                RenderBindings();
            });
            tools.Click += delegate { menu.Show(tools, new Point(0, tools.Height)); };
            row.ContextMenuStrip = text.ContextMenuStrip = menu;
            row.Disposed += delegate { menu.Dispose(); };
            return row;
        }
        void SizeRows()
        { foreach (Control row in bindings.Controls) row.Width = Math.Max(260, bindings.Width - SystemInformation.VerticalScrollBarWidth - 8); }
        void Edit(BindingEntry entry)
        {
            using (var dialog = new UnifiedBindingEditDialog(workspace, function.Id, entry, null, enableRequested))
                if (dialog.ShowDialog(this) == DialogResult.OK) Apply(delegate { if (dialog.RemoveRequested) workspace.Remove(entry); else if (entry == null) workspace.Add(function.Id, dialog.Result); else workspace.Replace(entry, dialog.Result); });
        }
        void Apply(Action operation)
        {
            try { operation(); if (changed != null) changed(); RenderBindings(); }
            catch (Exception ex) { status.Text = ex.Message; status.ForeColor = EditorTheme.AccentHover; }
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    public sealed class UnifiedBindingEditDialog : EditorWindow
    {
        static readonly string[] TriggerIds = { "default", "press", "release", "tap", "hold", "multi_tap", "pulse", "toggle", "sequence", "axis", "down", "custom" };
        BindingWorkspace workspace;
        readonly BindingFunction function;
        BindingEntry current;
        MainForm directHost;
        readonly bool multipleFunctions;
        readonly ComboBox mainKey = new EditorComboBox(), trigger = new EditorComboBox();
        readonly ComboBox additionalKey = new EditorComboBox();
        readonly CheckBox nativeAbilityTrigger = new EditorCheckBox();
        internal bool? NativeTriggerChoice
        {
            get { return !nativeAbilityTrigger.Visible || nativeAbilityTrigger.CheckState == CheckState.Indeterminate ? (bool?)null : nativeAbilityTrigger.Checked; }
            set { nativeAbilityTrigger.CheckState = value.HasValue ? value.Value ? CheckState.Checked : CheckState.Unchecked : CheckState.Indeterminate; }
        }
        readonly FlowLayoutPanel combination = new FlowLayoutPanel();
        readonly FlowLayoutPanel combinationCommands = new FlowLayoutPanel();
        readonly Panel keyRow;
        readonly NumericUpDown seconds = Number(.25m), interval = Number(.3m), count = Number(2), threshold = Number(.5m);
        readonly Panel secondsRow, intervalRow, countRow, thresholdRow;
        readonly KeyCaptureBox capture = new KeyCaptureBox();
        readonly Label status = new EditorLabel { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(620, 0), ForeColor = EditorTheme.Muted };
        readonly ToolTip tip = new ToolTip();
        readonly List<string> extraKeys = new List<string>();
        readonly ComboBox axis = new EditorComboBox();
        readonly ComboBox axisDirection = new EditorComboBox();
        readonly Panel axisDirectionRow;
        readonly Panel axisRow;
        readonly CheckBox bindingEnabled = new EditorCheckBox(), consume = new EditorCheckBox();
        readonly CheckBox bypassResources = new EditorCheckBox { Name = "bypassResources", AutoSize = true };
        readonly CheckBox bypassRestrictions = new EditorCheckBox { Name = "bypassRestrictions", AutoSize = true };
        readonly CheckedListBox restrictionBypasses = new CheckedListBox { Name = "restrictionBypasses", CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
        readonly CheckBox superArmor = new EditorCheckBox { Name = "superArmor", AutoSize = true };
        readonly CheckBox invincible = new EditorCheckBox { Name = "invincible", AutoSize = true };
        readonly NumericUpDown damageMultiplier = Number(1), startupSpeed = Number(1), recoverySpeed = Number(1);
        readonly CheckedListBox consumeKeys = new CheckedListBox();
        readonly NumericUpDown priority = Number(0), outputX = Number(0), outputY = Number(0), outputZ = Number(0);
        readonly ComboBox outputMode = new EditorComboBox();
        readonly ComboBox condition = new EditorComboBox();
        readonly TextBox conditionCode = UnifiedBindingUi.Search(), evaluatorCode = UnifiedBindingUi.Search();
        readonly CheckedListBox watchedKeys = new CheckedListBox();
        readonly Panel conditionExpressionRow;
        readonly Panel evaluatorSettings = UnifiedBindingUi.Grid(1);
        readonly Panel moreSettings = UnifiedBindingUi.Grid(1), sequenceSettings = UnifiedBindingUi.Grid(1);
        readonly ListBox sequenceSteps = new ListBox();
        readonly List<string> retainedConsume = new List<string>();
        readonly List<string> consumeInputs = new List<string>();
        bool consumeInitialized;
        readonly Panel outputVector;
        readonly HashSet<string> heldKeys = new HashSet<string>();
        readonly Label preview = UnifiedBindingUi.Label("");
        readonly Label scopeHint = UnifiedBindingUi.Label("");
        bool updatingConsume, consumptionChanged;
        bool recording, inputChanged;
        readonly Timer modeTimer = new Timer { Interval = 1500 };
        readonly Panel scroll;
        readonly TableLayoutPanel fields;
        readonly GamepadCapture gamepadCapture = new GamepadCapture();
        public BindingEntry Result { get; private set; }
        internal BindingEntry CurrentBinding { get { return current; } }
        public bool RemoveRequested { get; private set; }
        public UnifiedBindingEditDialog(BindingWorkspace workspace, string functionId, BindingEntry current, string initialKey, Func<bool> enableRequested, bool multipleFunctions = false)
        {
            SuspendLayout();
            moreSettings.SuspendLayout(); sequenceSettings.SuspendLayout(); evaluatorSettings.SuspendLayout();
            this.workspace = workspace; this.function = workspace.Functions.First(item => item.Id == functionId); this.current = current; this.multipleFunctions = multipleFunctions;
            UnifiedBindingUi.Form(this, function.Label + " · " + UnifiedBindingUi.FunctionIdentity(function), new Size(640, 600), new Size(500, 440));
            var root = UnifiedBindingUi.Grid(1); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Padding = new Padding(16, 10, 16, 12); root.RowCount = 4;
            root.SuspendLayout();
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scroll = new EditorPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = EditorTheme.Surface }; scroll.HandleCreated += delegate { EditorArtwork.DarkScrollbars(scroll); };
            fields = new EditorLayout { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 }; fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.SizeChanged += delegate {
                int width = Math.Max(1, scroll.Width - SystemInformation.VerticalScrollBarWidth);
                fields.MaximumSize = new Size(width, 0);
                status.MaximumSize = new Size(width, 0);
            };
            fields.SuspendLayout(); scroll.SuspendLayout();
            scopeHint.Name = "bindingScope"; AddField(fields, scopeHint, 24);
            nativeAbilityTrigger.Name = "nativeAbilityTrigger"; nativeAbilityTrigger.Text = UnifiedBindingUi.Text("nativeAbilityTrigger"); nativeAbilityTrigger.ThreeState = multipleFunctions;
            tip.SetToolTip(nativeAbilityTrigger, UnifiedBindingUi.Text("nativeAbilityHelp"));
            nativeAbilityTrigger.Checked = workspace.UsesNativeTrigger(functionId);
            AddField(fields, nativeAbilityTrigger, 38); ShowField(nativeAbilityTrigger, UnifiedBindingUi.HasNativeTrigger(function));
            string[] resourceKinds = workspace.ResourceKinds(functionId);
            bypassResources.Text = I18n.Str("binding.resources.bypass", ResourceCostCatalog.Describe(resourceKinds));
            bypassResources.Checked = current != null && current.BypassResources;
            AddField(fields, bypassResources, 32); ShowField(bypassResources, resourceKinds.Length > 0 || bypassResources.Checked || multipleFunctions);
            bypassRestrictions.Text = I18n.Str("binding.restrictions.bypass");
            bypassRestrictions.Checked = current != null && current.BypassRestrictions;
            restrictionBypasses.BackColor = EditorTheme.Surface; restrictionBypasses.ForeColor = EditorTheme.Ink;
            foreach (string kind in BindingRestrictionKinds.All)
                restrictionBypasses.Items.Add(BindingRestrictionKinds.Label(kind), current == null || current.RestrictionBypasses.Contains(kind));
            AddField(fields, bypassRestrictions, 32);
            AddField(fields, restrictionBypasses, 0);
            restrictionBypasses.Font = Font;
            SizeRestrictionChoices();
            Shown += delegate { SizeRestrictionChoices(); };
            ShowField(restrictionBypasses, bypassRestrictions.Checked);
            tip.SetToolTip(bypassRestrictions, I18n.Str("binding.restrictions.help"));
            damageMultiplier.Name = "damageMultiplier"; startupSpeed.Name = "startupSpeed"; recoverySpeed.Name = "recoverySpeed";
            damageMultiplier.Minimum = .01m; damageMultiplier.Maximum = 100;
            startupSpeed.Minimum = recoverySpeed.Minimum = .1m; startupSpeed.Maximum = recoverySpeed.Maximum = 10;
            SetNumber(damageMultiplier, current == null ? 1 : current.DamageMultiplier);
            SetNumber(startupSpeed, current == null ? 1 : current.StartupSpeed); SetNumber(recoverySpeed, current == null ? 1 : current.RecoverySpeed);
            bool combat = multipleFunctions || function.Targets.Any(target => target.Kind == "ability" || target.Kind == "move" || ResourceCostCatalog.IsCombatInput(target.Path)) || resourceKinds.Length > 0;
            ShowField(bypassRestrictions, combat || bypassRestrictions.Checked);
            bool phaseSpeed = multipleFunctions || function.Targets.Any(target => target.Kind != "move" || CompleteMoveCatalog.SupportsPhaseSpeed(target.Tag));
            var damageRow = Parameter(I18n.Str("binding.tuning.damage"), damageMultiplier); AddField(fields, damageRow, 0); ShowField(damageRow, combat);
            superArmor.Text = I18n.Str("binding.effects.superArmor"); invincible.Text = I18n.Str("binding.effects.invincible");
            superArmor.Checked = current != null && current.SuperArmor; invincible.Checked = current != null && current.Invincible;
            tip.SetToolTip(superArmor, I18n.Str("binding.effects.help")); tip.SetToolTip(invincible, I18n.Str("binding.effects.help"));
            AddField(fields, superArmor, 32); ShowField(superArmor, combat || superArmor.Checked);
            AddField(fields, invincible, 32); ShowField(invincible, combat || invincible.Checked);
            var startupRow = Parameter(I18n.Str("binding.tuning.startup"), startupSpeed); AddField(fields, startupRow, 0); ShowField(startupRow, combat && phaseSpeed);
            var recoveryRow = Parameter(I18n.Str("binding.tuning.recovery"), recoverySpeed); AddField(fields, recoveryRow, 0); ShowField(recoveryRow, combat && phaseSpeed);
            trigger.DropDownStyle = ComboBoxStyle.DropDownList; trigger.Name = "bindingTrigger";
            foreach (string id in TriggerIds) trigger.Items.Add(UnifiedBindingUi.Text("trigger." + id));
            AddField(fields, Parameter(UnifiedBindingUi.Text("trigger"), trigger), 0);
            preview.ForeColor = EditorTheme.Muted; preview.Name = "triggerPreview"; AddField(fields, preview, 32);
            mainKey.DropDownStyle = ComboBoxStyle.DropDownList; mainKey.Dock = DockStyle.Fill; mainKey.Name = "bindingKey";
            mainKey.Items.AddRange(DeviceKeys().Select(key => new KeyChoice(key)).ToArray());
            mainKey.SelectedIndexChanged += delegate
            {
                inputChanged = true; RefreshConsumeKeys();
            };
            keyRow = Parameter(UnifiedBindingUi.Text("key"), mainKey); AddField(fields, keyRow, 0);
            capture.Text = I18n.Str("flow.record"); capture.BackColor = EditorTheme.Floating; capture.ForeColor = EditorTheme.Muted; capture.Dock = DockStyle.Fill; capture.Name = "recordBinding";
            capture.CapturePrompt = I18n.Str("flow.record.active");
            capture.ModifierCaptured += Captured; capture.MouseCaptured += Captured; capture.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.KeyCode == Keys.Tab) return; string key = FromKey(e.KeyCode); if (key != null) Captured(key); e.SuppressKeyPress = true; };
            capture.KeyReleased += delegate(string key) { heldKeys.Remove(key); };
            gamepadCapture.Pressed += delegate(string key) { if (TriggerIds[Math.Max(0, trigger.SelectedIndex)] != "axis") Captured(key); };
            gamepadCapture.Released += delegate(string key) { heldKeys.Remove(key); };
            gamepadCapture.Axis += CapturedAxis;
            capture.GotFocus += delegate { recording = true; heldKeys.Clear(); gamepadCapture.Start(Handle); foreach (var form in Application.OpenForms.OfType<MainForm>()) form.SetCompanionRecording(true); };
            capture.LostFocus += delegate { recording = false; heldKeys.Clear(); gamepadCapture.Stop(); foreach (var form in Application.OpenForms.OfType<MainForm>()) form.SetCompanionRecording(false); };
            AddField(fields, capture, 38);
            combination.FlowDirection = FlowDirection.LeftToRight; combination.WrapContents = true; combination.AutoSize = true; combination.Dock = DockStyle.Fill;
            AddField(fields, combination, 0);
            var addKey = UnifiedBindingUi.Button(UnifiedBindingUi.Text("addcombination"), "addCombination");
            additionalKey.DropDownStyle = ComboBoxStyle.DropDownList; additionalKey.Width = 270; additionalKey.Visible = false; additionalKey.Name = "additionalKey";
            additionalKey.Items.AddRange(mainKey.Items.Cast<KeyChoice>().Select(choice => new KeyChoice(choice.Key)).ToArray());
            additionalKey.SelectionChangeCommitted += delegate {
                var choice = additionalKey.SelectedItem as KeyChoice;
                if (choice != null && choice.Key != SelectedKey() && !extraKeys.Contains(choice.Key)) extraKeys.Add(choice.Key);
                additionalKey.Visible = false; inputChanged = true; RenderCombination();
            };
            addKey.Click += delegate { additionalKey.Visible = true; additionalKey.Focus(); additionalKey.DroppedDown = true; };
            combinationCommands.Dock = DockStyle.Fill; combinationCommands.AutoSize = true; combinationCommands.Controls.Add(addKey); combinationCommands.Controls.Add(additionalKey); AddField(fields, combinationCommands, 0);
            secondsRow = Parameter(UnifiedBindingUi.Text("duration"), seconds); AddField(fields, secondsRow, 0);
            intervalRow = Parameter(UnifiedBindingUi.Text("interval"), interval); AddField(fields, intervalRow, 0);
            count.DecimalPlaces = 0; count.Minimum = 1; count.Increment = 1; countRow = Parameter(UnifiedBindingUi.Text("count"), count); AddField(fields, countRow, 0);
            thresholdRow = Parameter(UnifiedBindingUi.Text("threshold"), threshold); AddField(fields, thresholdRow, 0);
            axis.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string key in new[] { "Gamepad_Left2D", "Gamepad_LeftX", "Gamepad_LeftY", "Gamepad_Right2D", "Gamepad_RightX", "Gamepad_RightY", "Gamepad_LeftTriggerAxis", "Gamepad_RightTriggerAxis", "Mouse2D", "MouseX", "MouseY" }) axis.Items.Add(new KeyChoice(key));
            axisRow = Parameter(UnifiedBindingUi.Text("axis"), axis); AddField(fields, axisRow, 0);
            axisDirection.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string id in new[] { "both", "positive", "negative" }) axisDirection.Items.Add(I18n.Str("editing." + id));
            string direction = current != null && current.Rule != null ? current.Rule.AxisDirection : "both";
            axisDirection.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "both", "positive", "negative" }, direction));
            axisDirectionRow = Parameter(I18n.Str("editing.direction"), axisDirection); AddField(fields, axisDirectionRow, 0);
            var sequenceHelp = UnifiedBindingUi.Label(UnifiedBindingUi.Text("sequencehelp")); sequenceHelp.Name = "sequenceHelp"; sequenceHelp.ForeColor = EditorTheme.Muted; AddField(fields, sequenceHelp, 48);
            fields.Tag = sequenceHelp;
            BuildSequence(); AddField(fields, sequenceSettings, 0);
            var customFields = (TableLayoutPanel)evaluatorSettings; customFields.AutoSize = true; customFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ConfigureScript(evaluatorCode); evaluatorCode.Name = "inputEvaluator";
            evaluatorCode.Text = UnifiedBindingUi.ScriptText(current != null && current.Rule != null && !string.IsNullOrWhiteSpace(current.Rule.EvaluateLua)
                ? current.Rule.EvaluateLua : "function(now, snapshot, context, state)\n    return {active=false, pulse=false, value=1}\nend");
            AddField(customFields, UnifiedBindingUi.Label(I18n.Str("ruleeditor.evaluate")), 32); AddField(customFields, evaluatorCode, 150);
            AddField(customFields, UnifiedBindingUi.Label(UnifiedBindingUi.Text("watchKeys")), 32);
            watchedKeys.CheckOnClick = true; watchedKeys.IntegralHeight = false; watchedKeys.BackColor = EditorTheme.Floating; watchedKeys.ForeColor = EditorTheme.Ink;
            var watched = current != null && current.Rule != null ? current.Rule.Watch : new List<string>();
            foreach (string key in watched.Concat(KeyNames.Keyboard).Concat(KeyNames.Gamepad).Concat(AxisKeys).Where(key => !string.IsNullOrEmpty(key) && key != "None").Distinct())
                watchedKeys.Items.Add(new KeyChoice(key), watched.Contains(key));
            AddField(customFields, watchedKeys, 130); AddField(fields, evaluatorSettings, 0);
            var more = UnifiedBindingUi.Button(UnifiedBindingUi.Text("moreSettings"), "moreBindingSettings");
            more.Click += delegate {
                moreSettings.Visible = !moreSettings.Visible; RefreshConsumeKeys();
                if (moreSettings.Visible) { scroll.PerformLayout(); scroll.ScrollControlIntoView(bindingEnabled); }
            };
            var moreButtonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; moreButtonRow.Controls.Add(more); AddField(fields, moreButtonRow, 0);
            var advancedFields = (TableLayoutPanel)moreSettings; advancedFields.AutoSize = true; advancedFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bindingEnabled.Text = UnifiedBindingUi.Text("bindingEnabled"); bindingEnabled.Checked = current == null || current.Enabled;
            AddField(advancedFields, bindingEnabled, 36);
            priority.Minimum = -10000; priority.Maximum = 10000; priority.DecimalPlaces = 0; priority.Increment = 1; priority.Value = current == null ? 0 : current.Priority;
            AddField(advancedFields, Parameter(UnifiedBindingUi.Text("priority"), priority), 0);
            condition.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string value in new[] { "always", "gameplay", "foreground", "unpaused", "expression" }) condition.Items.Add(I18n.Str("unified.advanced.condition." + value));
            AddField(advancedFields, Parameter(I18n.Str("unified.advanced.condition"), condition), 0);
            ConfigureScript(conditionCode); conditionCode.Name = "bindingCondition";
            conditionCode.Text = UnifiedBindingUi.ScriptText(current != null && current.Rule != null && !string.IsNullOrWhiteSpace(current.Rule.WhenLua)
                ? current.Rule.WhenLua : "function(context) return true end");
            var conditionFields = UnifiedBindingUi.Grid(1); conditionFields.AutoSize = true; conditionFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(conditionFields, conditionCode, 115); conditionExpressionRow = conditionFields; AddField(advancedFields, conditionExpressionRow, 0);
            condition.SelectedIndexChanged += delegate { conditionExpressionRow.Visible = condition.SelectedIndex == 4; };
            string initialCondition = current != null && current.Rule != null ? current.Rule.WhenLua : null;
            condition.SelectedIndex = string.IsNullOrWhiteSpace(initialCondition) ? 0
                : initialCondition == "function(context) return context.gameplay end" ? 1
                : initialCondition == "function(context) return context.foreground end" ? 2
                : initialCondition == "function(context) return not context.paused end" ? 3 : 4;
            consume.Text = UnifiedBindingUi.Text("consume"); consume.Checked = current != null && current.Rule != null && (current.Rule.Consume || current.Rule.ConsumeKeys.Count > 0);
            if (current != null && current.Rule != null) retainedConsume.AddRange(current.Rule.ConsumeKeys.Except(
                current.Keys.Concat(current.Rule.Watch).Concat(current.Rule.Steps.SelectMany(step => step)).Concat(string.IsNullOrEmpty(current.Axis) ? new string[0] : new[] { current.Axis })));
            consume.CheckedChanged += delegate
            {
                consumptionChanged = true; RefreshConsumeKeys();
                if (consume.Checked && consumeKeys.CheckedItems.Count == 0)
                {
                    updatingConsume = true;
                    try { for (int i = 0; i < consumeKeys.Items.Count; i++) consumeKeys.SetItemChecked(i, true); }
                    finally { updatingConsume = false; }
                }
            }; AddField(advancedFields, consume, 36);
            consumeKeys.CheckOnClick = true; consumeKeys.IntegralHeight = false; consumeKeys.BackColor = EditorTheme.Floating; consumeKeys.ForeColor = EditorTheme.Ink; consumeKeys.DisplayMember = "Label";
            consumeKeys.ItemCheck += delegate { if (!updatingConsume) consumptionChanged = true; }; AddField(advancedFields, consumeKeys, 130);
            var consumeHelp = UnifiedBindingUi.Label(UnifiedBindingUi.Text("consumeHelp")); consumeHelp.ForeColor = EditorTheme.Muted; AddField(advancedFields, consumeHelp, 48);
            outputMode.DropDownStyle = ComboBoxStyle.DropDownList; outputMode.Items.Add(UnifiedBindingUi.Text("outputInherit")); outputMode.Items.Add(UnifiedBindingUi.Text("outputFixed"));
            AddField(advancedFields, Parameter(UnifiedBindingUi.Text("outputValue"), outputMode), 0);
            var vector = UnifiedBindingUi.Grid(3); vector.AutoSize = true; for (int i = 0; i < 3; i++) vector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            NumericUpDown[] components = { outputX, outputY, outputZ }; string[] componentNames = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++) { components[i].Minimum = -10000; components[i].Maximum = 10000; vector.Controls.Add(Parameter(componentNames[i], components[i]), i, 0); }
            outputVector = vector; AddField(advancedFields, vector, 0);
            outputMode.SelectedIndexChanged += delegate { outputVector.Visible = outputMode.SelectedIndex == 1; };
            LoadOutput(current == null ? function.DefaultOutputValue : current.OutputValue); moreSettings.Visible = false; AddField(fields, moreSettings, 0);
            if (current != null && current.Rule != null)
            {
                string summary = UnifiedBindingUi.EntryPolicy(current);
                if (!string.IsNullOrWhiteSpace(current.Rule.WhenLua)) summary += " · " + I18n.Str("unified.advanced.condition");
                if (current.OutputValue != null) summary += " · " + UnifiedBindingUi.Text("outputFixed");
                more.Text += " · " + I18n.Str("views.savedcustom"); tip.SetToolTip(more, summary);
            }
            scroll.Controls.Add(fields); root.Controls.Add(scroll, 0, 0);
            status.ForeColor = EditorTheme.Muted; root.Controls.Add(status, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
            var accept = UnifiedBindingUi.Button(I18n.Str("editor.ok"), "saveBinding"); accept.Primary = true; accept.Click += delegate { Save(); };
            var cancel = UnifiedBindingUi.Button(I18n.Str("editor.cancel"), "cancel"); cancel.DialogResult = DialogResult.Cancel;
            cancel.Click += delegate { Close(); }; buttons.Controls.Add(accept); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 3);
            if (current != null)
            {
                var remove = UnifiedBindingUi.Button(I18n.Str("binding.unbind"), "removeBinding");
                remove.Click += delegate { RemoveRequested = true; DialogResult = DialogResult.OK; Close(); }; buttons.Controls.Add(remove);
            }
            Controls.Add(root); AcceptButton = accept; CancelButton = cancel;
            string[] initialKeys = current != null && current.Trigger == "sequence" && current.Rule != null && current.Rule.Steps.Count > 0
                ? current.Rule.Steps.SelectMany(step => step).ToArray() : current == null ? new string[0] : current.Keys;
            SetKey(initialKeys.Length > 0 ? initialKeys[0] : initialKey); extraKeys.AddRange(initialKeys.Skip(1));
            trigger.SelectedIndex = Math.Max(0, Array.IndexOf(TriggerIds, current == null ? "default" : current.Trigger));
            if (current != null) { SetNumber(seconds, current.Seconds); SetNumber(interval, current.Interval); SetNumber(count, current.Count); SetNumber(threshold, current.Threshold); SetAxis(current.Axis); }
            else axis.SelectedIndex = 0;
            inputChanged = consumptionChanged = consumeInitialized = false; trigger.SelectedIndexChanged += delegate { inputChanged = true; Parameters(); RefreshConsumeKeys(); }; axis.SelectedIndexChanged += delegate { UpdateAxisDirection(); RefreshConsumeKeys(); }; RenderCombination(); Parameters(); RefreshConsumeKeys();
            watchedKeys.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke((Action)RefreshConsumeKeys); };
            foreach (var number in new[] { seconds, interval, count, threshold, damageMultiplier, startupSpeed, recoverySpeed }) number.ValueChanged += delegate { UpdatePreview(); };
            bypassResources.CheckedChanged += delegate { UpdatePreview(); };
            bypassRestrictions.CheckedChanged += delegate { ShowField(restrictionBypasses, bypassRestrictions.Checked); UpdatePreview(); };
            restrictionBypasses.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke((Action)UpdatePreview); };
            superArmor.CheckedChanged += delegate { UpdatePreview(); }; invincible.CheckedChanged += delegate { UpdatePreview(); };
            priority.ValueChanged += delegate { UpdatePreview(); };
            bindingEnabled.CheckedChanged += delegate { UpdatePreview(); }; consume.CheckedChanged += delegate { UpdatePreview(); };
            condition.SelectedIndexChanged += delegate { UpdatePreview(); }; outputMode.SelectedIndexChanged += delegate { UpdatePreview(); };
            mainKey.SelectedIndexChanged += delegate { UpdatePreview(); };
            tip.SetToolTip(capture, capture.Text);
            bool liveMode = workspace.LiveEditing;
            modeTimer.Tick += delegate {
                bool liveNow = workspace.LiveEditing;
                if (liveNow != liveMode) { liveMode = liveNow; UpdatePreview(); }
                if (directHost != null) accept.Text = I18n.Str(liveNow ? "flow.applyreturn" : "editor.ok");
            };
            Shown += delegate {
                directHost = workspace.LiveEditing ? Owner as MainForm : null;
                if (directHost != null && workspace.LiveEditing)
                {
                    accept.Text = I18n.Str("flow.applyreturn");
                    cancel.Text = I18n.Str("editor.close");
                    status.Text = I18n.Str("flow.draft.hint");
                }
                UpdatePreview(); modeTimer.Start();
            };
            status.Cursor = Cursors.Hand;
            status.Click += delegate { if (directHost != null) directHost.ShowBindingFeedback(); };
            moreSettings.ResumeLayout(false); sequenceSettings.ResumeLayout(false); evaluatorSettings.ResumeLayout(false);
            fields.ResumeLayout(false); scroll.ResumeLayout(false); root.ResumeLayout(false);
            ResumeLayout(true);
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z) && !recording && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown)
                && UndoLiveEdit()) return true;
            return base.ProcessCmdKey(ref message, keyData);
        }
        bool UndoLiveEdit()
        {
            if (directHost == null || !directHost.CanUndoBinding) return false;
            string previousId = current == null ? null : current.Id;
            directHost.UndoFromBindingEditor();
            workspace = directHost.BindingDialogWorkspace(workspace.Gamepad, workspace.FamilyIndex);
            current = workspace.Entries(function.Id).FirstOrDefault(item => item.Id == previousId)
                ?? workspace.Entries(function.Id).FirstOrDefault(item => item.Active && !item.Suppressed);
            var rule = current == null ? null : current.Rule;
            extraKeys.Clear(); heldKeys.Clear(); retainedConsume.Clear(); consumeInputs.Clear();
            var keys = current == null ? new string[0] : current.Keys;
            SetKey(keys.FirstOrDefault()); extraKeys.AddRange(keys.Skip(1));
            trigger.SelectedIndex = Math.Max(0, Array.IndexOf(TriggerIds, current == null ? "default" : current.Trigger));
            SetNumber(seconds, current == null ? .25 : current.Seconds); SetNumber(interval, current == null ? .3 : current.Interval);
            SetNumber(count, current == null ? 2 : current.Count); SetNumber(threshold, current == null ? .5 : current.Threshold);
            SetAxis(current == null ? null : current.Axis);
            axisDirection.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "both", "positive", "negative" }, rule == null ? "both" : rule.AxisDirection));
            sequenceSteps.Items.Clear();
            if (rule != null) foreach (var step in rule.Steps) sequenceSteps.Items.Add(new StepChoice(step));
            evaluatorCode.Text = UnifiedBindingUi.ScriptText(rule != null && !string.IsNullOrWhiteSpace(rule.EvaluateLua)
                ? rule.EvaluateLua : "function(now, snapshot, context, state)\n    return {active=false, pulse=false, value=1}\nend");
            for (int i = 0; i < watchedKeys.Items.Count; i++) watchedKeys.SetItemChecked(i, rule != null && rule.Watch.Contains(((KeyChoice)watchedKeys.Items[i]).Key));
            bindingEnabled.Checked = current == null || current.Enabled; priority.Value = current == null ? 0 : current.Priority;
            string when = rule == null ? null : rule.WhenLua;
            conditionCode.Text = UnifiedBindingUi.ScriptText(when ?? "");
            condition.SelectedIndex = string.IsNullOrWhiteSpace(when) ? 0 : when == "function(context) return context.gameplay end" ? 1
                : when == "function(context) return context.foreground end" ? 2 : when == "function(context) return not context.paused end" ? 3 : 4;
            nativeAbilityTrigger.Checked = workspace.UsesNativeTrigger(function.Id);
            bypassResources.Checked = current != null && current.BypassResources;
            bypassRestrictions.Checked = current != null && current.BypassRestrictions;
            for (int index = 0; index < BindingRestrictionKinds.All.Length; index++)
                restrictionBypasses.SetItemChecked(index, current == null || current.RestrictionBypasses.Contains(BindingRestrictionKinds.All[index]));
            superArmor.Checked = current != null && current.SuperArmor; invincible.Checked = current != null && current.Invincible;
            SetNumber(damageMultiplier, current == null ? 1 : current.DamageMultiplier);
            SetNumber(startupSpeed, current == null ? 1 : current.StartupSpeed); SetNumber(recoverySpeed, current == null ? 1 : current.RecoverySpeed);
            LoadOutput(current == null ? function.DefaultOutputValue : current.OutputValue);
            consume.Checked = rule != null && (rule.Consume || rule.ConsumeKeys.Count > 0);
            if (rule != null) retainedConsume.AddRange(rule.ConsumeKeys.Except(keys.Concat(rule.Watch).Concat(rule.Steps.SelectMany(step => step)).Concat(string.IsNullOrEmpty(current.Axis) ? new string[0] : new[] { current.Axis })));
            inputChanged = consumptionChanged = consumeInitialized = false;
            RenderCombination(); Parameters(); RefreshConsumeKeys();
            status.Text = I18n.Str("flow.draft.hint"); return true;
        }
        static NumericUpDown Number(decimal value)
        { return new NumericUpDown { Minimum = 0, Maximum = 10000, DecimalPlaces = 3, Increment = .05m, Value = value, Dock = DockStyle.Fill, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink }; }
        static void SetNumber(NumericUpDown control, double value) { control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, (decimal)value)); }
        static void ConfigureScript(TextBox control)
        { control.Multiline = true; control.AcceptsReturn = true; control.AcceptsTab = true; control.ScrollBars = ScrollBars.Vertical; control.Font = new Font("Consolas", 10f); }
        static void AddField(TableLayoutPanel table, Control control, int height)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Fill; control.Margin = new Padding(3);
            if (height > 0) control.MinimumSize = new Size(0, height);
            if (control is Label)
            {
                control.AutoSize = height == 0;
                if (height > 0) control.Height = height;
            }
            var flow = control as FlowLayoutPanel; if (flow != null) flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.Controls.Add(control, 0, row);
        }
        static void ShowField(Control control, bool visible)
        {
            control.Visible = visible;
        }
        void SizeRestrictionChoices()
        {
            int height = restrictionBypasses.ItemHeight * restrictionBypasses.Items.Count + 4;
            restrictionBypasses.MinimumSize = new Size(0, height);
            restrictionBypasses.MaximumSize = new Size(0, height);
            restrictionBypasses.Height = height;
        }
        static Panel Parameter(string title, Control input)
        {
            var row = UnifiedBindingUi.Grid(2); row.SuspendLayout(); row.MinimumSize = new Size(0, 30); row.Size = new Size(200, 30);
            float inputWidth = input is ComboBox ? 55 : 35;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - inputWidth)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, inputWidth));
            var label = UnifiedBindingUi.Label(title);
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); row.Controls.Add(label, 0, 0);
            input.Dock = DockStyle.None; input.Anchor = AnchorStyles.Left | AnchorStyles.Right; input.Margin = Padding.Empty;
            row.Controls.Add(input, 1, 0); row.ResumeLayout(false); return row;
        }
        void LoadOutput(object value)
        {
            outputMode.SelectedIndex = value == null ? 0 : 1;
            var vector = value as System.Collections.IDictionary;
            NumericUpDown[] components = { outputX, outputY, outputZ }; string[] names = { "x", "y", "z" };
            for (int i = 0; i < components.Length; i++)
            {
                object component = vector == null ? (i == 0 ? value : null) : vector[names[i]];
                if (component != null) SetNumber(components[i], Convert.ToDouble(component, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        void RefreshConsumeKeys()
        {
            var selected = consumeKeys.CheckedItems.Cast<KeyChoice>().Select(choice => choice.Key).ToList();
            var keys = new List<string>();
            if (trigger.SelectedIndex == Array.IndexOf(TriggerIds, "axis"))
            { var selectedAxis = axis.SelectedItem as KeyChoice; if (selectedAxis != null) keys.Add(selectedAxis.Key); }
            else if (trigger.SelectedIndex == Array.IndexOf(TriggerIds, "custom"))
                keys.AddRange(watchedKeys.CheckedItems.Cast<KeyChoice>().Select(choice => choice.Key));
            else if (trigger.SelectedIndex == Array.IndexOf(TriggerIds, "sequence") && sequenceSteps.Items.Count > 0)
                keys.AddRange(sequenceSteps.Items.Cast<StepChoice>().SelectMany(step => step.Keys));
            else { if (!string.IsNullOrEmpty(SelectedKey())) keys.Add(SelectedKey()); keys.AddRange(extraKeys); }
            keys = keys.Distinct().ToList();
            bool selectAll = false;
            if (!consumeInitialized && current != null && current.Rule != null)
            { selected = current.Rule.ConsumeKeys.ToList(); selectAll = current.Rule.Consume; }
            else if (consumeInitialized && !keys.SequenceEqual(consumeInputs))
            {
                selectAll = consume.Checked && consumeInputs.Count > 0 && consumeInputs.All(selected.Contains);
                for (int i = 0; i < consumeInputs.Count; i++)
                    if (selected.Contains(consumeInputs[i]) && !keys.Contains(consumeInputs[i]))
                    {
                        selected.Remove(consumeInputs[i]);
                        if (i < keys.Count) selected.Add(keys[i]);
                    }
                consumptionChanged = true;
            }
            consumeInputs.Clear(); consumeInputs.AddRange(keys); consumeInitialized = true;
            keys.AddRange(retainedConsume);
            updatingConsume = true; consumeKeys.BeginUpdate();
            try
            {
                var inputs = keys.Distinct().ToArray();
                if (!consumeKeys.Items.Cast<KeyChoice>().Select(choice => choice.Key).SequenceEqual(inputs))
                {
                    consumeKeys.Items.Clear();
                    consumeKeys.Items.AddRange(inputs.Select(key => new KeyChoice(key)).ToArray());
                }
                for (int i = 0; i < inputs.Length; i++)
                {
                    bool check = selectAll || selected.Contains(inputs[i]);
                    if (consumeKeys.GetItemChecked(i) != check) consumeKeys.SetItemChecked(i, check);
                }
                consumeKeys.Enabled = consume.Checked;
            }
            finally { consumeKeys.EndUpdate(); updatingConsume = false; }
        }
        void BuildSequence()
        {
            var table = (TableLayoutPanel)sequenceSettings; table.AutoSize = true; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sequenceSteps.BackColor = EditorTheme.Floating; sequenceSteps.ForeColor = EditorTheme.Ink; sequenceSteps.IntegralHeight = false;
            sequenceSteps.Name = "sequenceSteps";
            if (current != null && current.Rule != null)
                foreach (string[] step in current.Rule.Steps) sequenceSteps.Items.Add(new StepChoice(step));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            var add = UnifiedBindingUi.Button(UnifiedBindingUi.Text("addStep"), "addSequenceStep");
            var edit = UnifiedBindingUi.Button(UnifiedBindingUi.Text("edit"), "editSequenceStep");
            var remove = UnifiedBindingUi.Button(I18n.Str("views.remove"), "removeSequenceStep");
            var up = UnifiedBindingUi.Button("↑", "moveSequenceStepUp"); var down = UnifiedBindingUi.Button("↓", "moveSequenceStepDown");
            add.Click += delegate
            {
                string key = SelectedKey(); var keys = (key == null ? new string[0] : new[] { key }).Concat(extraKeys).Distinct().ToArray();
                EditSequenceStep(-1, keys);
            };
            edit.Click += delegate { int index = sequenceSteps.SelectedIndex; if (index >= 0) EditSequenceStep(index, ((StepChoice)sequenceSteps.Items[index]).Keys); };
            sequenceSteps.DoubleClick += delegate { edit.PerformClick(); };
            remove.Click += delegate { int index = sequenceSteps.SelectedIndex; if (index < 0) return; sequenceSteps.Items.RemoveAt(index); if (sequenceSteps.Items.Count > 0) sequenceSteps.SelectedIndex = Math.Min(index, sequenceSteps.Items.Count - 1); inputChanged = true; RefreshConsumeKeys(); };
            up.Click += delegate { MoveSequenceStep(-1); }; down.Click += delegate { MoveSequenceStep(1); };
            buttons.Controls.AddRange(new Control[] { add, edit, remove, up, down }); AddField(table, buttons, 0);
            AddField(table, sequenceSteps, 85);
        }
        void MoveSequenceStep(int offset)
        {
            int index = sequenceSteps.SelectedIndex, target = index + offset; if (index < 0 || target < 0 || target >= sequenceSteps.Items.Count) return;
            object step = sequenceSteps.Items[index]; sequenceSteps.Items.RemoveAt(index); sequenceSteps.Items.Insert(target, step); sequenceSteps.SelectedIndex = target; inputChanged = true;
        }
        void EditSequenceStep(int index, string[] selected)
        {
            using (var dialog = new EditorWindow())
            {
                UnifiedBindingUi.Form(dialog, UnifiedBindingUi.Text("stepChord"), new Size(450, 490), new Size(360, 340));
                var layout = UnifiedBindingUi.Grid(1); layout.Padding = new Padding(12); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var keys = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BackColor = EditorTheme.Floating, ForeColor = EditorTheme.Ink };
                foreach (string key in selected.Concat(DeviceKeys()).Distinct()) keys.Items.Add(new KeyChoice(key), selected.Contains(key));
                layout.Controls.Add(keys, 0, 0); var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var save = UnifiedBindingUi.Button(I18n.Str("editor.ok"), "saveSequenceStep"); var cancel = UnifiedBindingUi.Button(I18n.Str("editor.cancel"), "cancel"); cancel.DialogResult = DialogResult.Cancel;
                save.Click += delegate { if (keys.CheckedItems.Count > 0) { dialog.DialogResult = DialogResult.OK; dialog.Close(); } };
                buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 1); dialog.Controls.Add(layout); dialog.CancelButton = cancel; dialog.AcceptButton = save;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var step = new StepChoice(keys.CheckedItems.Cast<KeyChoice>().Select(choice => choice.Key).ToArray());
                if (index < 0) index = sequenceSteps.Items.Add(step); else sequenceSteps.Items[index] = step;
                sequenceSteps.SelectedIndex = index; inputChanged = true; RefreshConsumeKeys();
            }
        }
        sealed class StepChoice
        {
            public readonly string[] Keys;
            public StepChoice(string[] keys) { Keys = keys.ToArray(); }
            public override string ToString() { return string.Join(" + ", Keys.Select(BindingButton.FriendlyKey)); }
        }
        void UpdateAxisDirection()
        {
            var choice = axis.SelectedItem as KeyChoice;
            axisDirection.Enabled = choice == null || !choice.Key.EndsWith("2D", StringComparison.Ordinal);
            if (!axisDirection.Enabled) axisDirection.SelectedIndex = 0;
        }
        void Parameters()
        {
            scroll.SuspendLayout(); fields.SuspendLayout();
            try
            {
                UpdateAxisDirection();
                string id = TriggerIds[Math.Max(0, trigger.SelectedIndex)];
                secondsRow.Visible = id == "tap" || id == "hold"; intervalRow.Visible = id == "multi_tap" || id == "pulse" || id == "sequence";
                countRow.Visible = id == "multi_tap"; thresholdRow.Visible = axisRow.Visible = id == "axis";
                axisDirectionRow.Visible = id == "axis";
                var table = (TableLayoutPanel)axisRow.Parent; ShowField((Control)table.Tag, false);
                sequenceSettings.Visible = id == "sequence";
                evaluatorSettings.Visible = id == "custom";
                bool keys = id != "axis" && id != "custom" && id != "sequence";
                ShowField(keyRow, keys); ShowField(capture, keys || id == "sequence" || id == "axis"); ShowField(combination, keys); ShowField(combinationCommands, keys);
                UpdatePreview();
            }
            finally { fields.ResumeLayout(true); scroll.ResumeLayout(true); }
        }
        string[] SelectedRestrictionBypasses()
        { return restrictionBypasses.CheckedIndices.Cast<int>().Select(index => BindingRestrictionKinds.All[index]).ToArray(); }
        void UpdatePreview()
        {
            string id = TriggerIds[Math.Max(0, trigger.SelectedIndex)];
            preview.Text = I18n.Str("binding.triggerhelp." + id);
            bool plain = id == "default" && extraKeys.Count == 0 && bindingEnabled.Checked && priority.Value == 0 && condition.SelectedIndex == 0 && !consume.Checked && outputMode.SelectedIndex == 0;
            var specification = new BindingEntry { Keys = string.IsNullOrEmpty(SelectedKey()) ? new string[0] : new[] { SelectedKey() }, Trigger = plain ? "default" : "press",
                BypassResources = bypassResources.Checked, BypassRestrictions = bypassRestrictions.Checked,
                RestrictionBypasses = SelectedRestrictionBypasses(), SuperArmor = superArmor.Checked, Invincible = invincible.Checked, DamageMultiplier = (double)damageMultiplier.Value,
                StartupSpeed = (double)startupSpeed.Value, RecoverySpeed = (double)recoverySpeed.Value };
            bool native = !multipleFunctions && workspace.UsesSaveBinding(function.Id, specification, current);
            bindingEnabled.Enabled = true;
            bool schemeScoped = workspace.KeyEnhancementEnabled && workspace.Configuration.Schemes.Count > 0 && !function.Id.StartsWith("scheme:", StringComparison.Ordinal);
            scopeHint.Text = I18n.Str(schemeScoped ? "schemes.scope" : native ? "binding.scope.scheme" : (specification.Keys.Concat(extraKeys).Any(key => key.StartsWith("Gamepad_", StringComparison.Ordinal))
                || id == "axis" && axis.SelectedItem is KeyChoice && ((KeyChoice)axis.SelectedItem).Key.StartsWith("Gamepad_", StringComparison.Ordinal)) ? "binding.scope.gamepad" : "binding.scope.global");
        }
        void Captured(string key)
        {
            if (!recording) return;
            if (heldKeys.Contains(key)) return;
            extraKeys.Clear(); extraKeys.AddRange(heldKeys);
            heldKeys.Add(key);
            SetKey(key); extraKeys.Remove(key); RenderCombination();
            if (TriggerIds[Math.Max(0, trigger.SelectedIndex)] == "sequence")
            {
                string[] step = new[] { key }.Concat(extraKeys).Distinct().ToArray();
                if (extraKeys.Count > 0 && sequenceSteps.Items.Count > 0) sequenceSteps.Items[sequenceSteps.Items.Count - 1] = new StepChoice(step);
                else sequenceSteps.Items.Add(new StepChoice(step));
                sequenceSteps.SelectedIndex = sequenceSteps.Items.Count - 1;
            }
            inputChanged = true;
        }
        void CapturedAxis(string key, double value)
        {
            if (!recording || TriggerIds[Math.Max(0, trigger.SelectedIndex)] != "axis") return;
            SetAxis(key);
            axisDirection.SelectedIndex = value < 0 ? 2 : 1;
            inputChanged = true; UpdatePreview();
        }
        void RenderCombination()
        {
            scroll.SuspendLayout(); fields.SuspendLayout(); combination.SuspendLayout();
            try
            {
                foreach (Control control in combination.Controls.Cast<Control>().ToArray()) control.Dispose();
                foreach (string key in extraKeys.ToArray())
                {
                    string value = key; var chip = UnifiedBindingUi.Button(BindingButton.FriendlyKey(key) + "  ×", "combinationKey");
                    chip.Click += delegate { extraKeys.Remove(value); inputChanged = true; RenderCombination(); }; combination.Controls.Add(chip);
                }
                RefreshConsumeKeys(); UpdatePreview();
            }
            finally { combination.ResumeLayout(true); fields.ResumeLayout(true); scroll.ResumeLayout(true); }
        }
        string SelectedKey() { var choice = mainKey.SelectedItem as KeyChoice; return choice == null ? null : choice.Key; }
        IEnumerable<string> DeviceKeys()
        {
            return (workspace.Gamepad ? KeyNames.Gamepad.Concat(KeyNames.Keyboard) : KeyNames.Keyboard.Concat(KeyNames.Gamepad))
                .Where(key => !string.IsNullOrEmpty(key) && key != "None").Distinct();
        }
        void SetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) { mainKey.SelectedIndex = -1; return; }
            int index = Enumerable.Range(0, mainKey.Items.Count).FirstOrDefault(i => ((KeyChoice)mainKey.Items[i]).Key == key, -1);
            if (index < 0) index = mainKey.Items.Add(new KeyChoice(key)); mainKey.SelectedIndex = index;
        }
        void SetAxis(string key)
        { int index = Enumerable.Range(0, axis.Items.Count).FirstOrDefault(i => ((KeyChoice)axis.Items[i]).Key == key, -1); axis.SelectedIndex = index < 0 ? 0 : index; }
        async void Save()
        {
            RefreshConsumeKeys();
            string key = SelectedKey(); string id = TriggerIds[Math.Max(0, trigger.SelectedIndex)];
            if (string.IsNullOrEmpty(key) && id != "axis" && id != "custom" && !(id == "sequence" && sequenceSteps.Items.Count > 0))
            { status.Text = UnifiedBindingUi.Text("choosekey"); mainKey.Focus(); return; }
            string[] orderedKeys = (string.IsNullOrEmpty(key) ? new string[0] : new[] { key }).Concat(extraKeys).ToArray();
            var entry = new BindingEntry { FunctionId = function.Id, Keys = id == "axis" || id == "custom" ? new string[0] : orderedKeys.Distinct().ToArray(),
                Trigger = id, Seconds = (double)seconds.Value, Interval = (double)interval.Value, Count = (int)count.Value, Threshold = (double)threshold.Value,
                Axis = id == "axis" && axis.SelectedItem is KeyChoice ? ((KeyChoice)axis.SelectedItem).Key : null };
            if (id == "axis") entry.Rule = new KeyEnhancementRule { AxisDirection = new[] { "both", "positive", "negative" }[Math.Max(0, axisDirection.SelectedIndex)] };
            entry.KeepNativeTrigger = NativeTriggerChoice;
            if (current != null) { entry.Rule = current.Rule == null ? entry.Rule : CloneRule(current.Rule); entry.Enabled = current.Enabled; entry.Priority = current.Priority; }
            if (id == "axis") entry.Rule.AxisDirection = new[] { "both", "positive", "negative" }[Math.Max(0, axisDirection.SelectedIndex)];
            if (id == "sequence")
            {
                var rule = current != null && current.Rule != null ? CloneRule(current.Rule) : new KeyEnhancementRule();
                if (sequenceSteps.Items.Count > 0) rule.Steps = sequenceSteps.Items.Cast<StepChoice>().Select(step => step.Keys.ToArray()).ToList();
                else if (inputChanged || current == null || current.Trigger != "sequence") rule.Steps = orderedKeys.Select(value => new[] { value }).ToList();
                rule.Trigger = "sequence"; entry.Rule = rule;
                entry.Keys = rule.Steps.SelectMany(step => step).Distinct().ToArray();
            }
            entry.Priority = (int)priority.Value; entry.Enabled = bindingEnabled.Checked;
            entry.BypassResources = bypassResources.Checked;
            entry.BypassRestrictions = bypassRestrictions.Checked; entry.RestrictionBypasses = SelectedRestrictionBypasses();
            entry.SuperArmor = superArmor.Checked; entry.Invincible = invincible.Checked;
            entry.DamageMultiplier = (double)damageMultiplier.Value; entry.StartupSpeed = (double)startupSpeed.Value; entry.RecoverySpeed = (double)recoverySpeed.Value;
            entry.OutputValue = outputMode.SelectedIndex == 0 ? null : new Dictionary<string, object> { { "x", (double)outputX.Value }, { "y", (double)outputY.Value }, { "z", (double)outputZ.Value } };
            if (consumptionChanged)
            {
                entry.Rule = entry.Rule == null ? new KeyEnhancementRule() : CloneRule(entry.Rule);
                var selectedKeys = consumeKeys.CheckedItems.Cast<KeyChoice>().Select(choice => choice.Key).ToList();
                entry.Rule.Consume = consume.Checked && selectedKeys.Count > 0 && selectedKeys.Count == consumeKeys.Items.Count;
                entry.Rule.ConsumeKeys = consume.Checked && !entry.Rule.Consume ? selectedKeys : new List<string>();
            }
            string when = condition.SelectedIndex == 0 ? null
                : condition.SelectedIndex == 1 ? "function(context) return context.gameplay end"
                : condition.SelectedIndex == 2 ? "function(context) return context.foreground end"
                : condition.SelectedIndex == 3 ? "function(context) return not context.paused end" : conditionCode.Text.Trim();
            if (condition.SelectedIndex == 4 && string.IsNullOrWhiteSpace(when) || id == "custom" && string.IsNullOrWhiteSpace(evaluatorCode.Text))
            { status.Text = I18n.Str("unified.advanced.invalid"); return; }
            if (entry.Rule != null || when != null || id == "custom")
            {
                entry.Rule = entry.Rule == null ? new KeyEnhancementRule() : CloneRule(entry.Rule);
                entry.Rule.WhenLua = when;
                if (id == "custom")
                {
                    entry.Rule.Trigger = "custom"; entry.Rule.EvaluateLua = evaluatorCode.Text.Trim();
                    entry.Rule.Watch = watchedKeys.CheckedItems.Cast<KeyChoice>().Select(choice => choice.Key).ToList();
                }
            }
            if (entry.Rule != null)
            {
                entry.Rule = CloneRule(entry.Rule); entry.Rule.Trigger = id == "default" ? "down" : id;
                entry.Rule.Keys = entry.Keys.ToList(); entry.Rule.Axis = entry.Axis; entry.Rule.NormalizeInput();
                entry.Keys = entry.Rule.Keys.ToArray();
            }
            Result = entry;
            if (directHost != null)
            {
                var staged = directHost.StageBindingFromDialog(workspace, function.Id, current, entry);
                if (staged == null) { status.Text = I18n.Str("flow.failed.details"); return; }
                current = staged;
                if (!workspace.LiveEditing) { DialogResult = DialogResult.OK; Close(); return; }
                Enabled = false;
                try
                {
                    var state = directHost.BindingApplication();
                    if (!state.CanApply)
                    {
                        status.Text = state.Reason ?? I18n.Str("flow.test.hint");
                        if (!state.Live || state.Reason != null && !state.Deferred) return;
                    }
                    else if (!await directHost.ApplyChangesAsync()) { status.Text = I18n.Str("flow.failed.details"); return; }
                }
                finally { Enabled = true; }
                DialogResult = DialogResult.OK; Close(); directHost.ReturnFromBindingEditor();
                return;
            }
            DialogResult = DialogResult.OK; Close();
        }
        static KeyEnhancementRule CloneRule(KeyEnhancementRule rule)
        { var config = new KeyEnhancementConfiguration(); config.Rules.Add(rule); return config.Clone().Rules[0]; }
        static string FromKey(Keys key)
        {
            return BindingDialog.KeyFromCode(key);
        }
        internal static readonly string[] AxisKeys = BindingWorkspace.AxisKeys;
        sealed class KeyChoice
        {
            public string Key;
            public string Label
            {
                get
                {
                    string prefix = Key.StartsWith("Gamepad_Left", StringComparison.Ordinal) && (Key.EndsWith("2D", StringComparison.Ordinal) || Key == "Gamepad_LeftX" || Key == "Gamepad_LeftY") ? "leftStick"
                        : Key.StartsWith("Gamepad_Right", StringComparison.Ordinal) && (Key.EndsWith("2D", StringComparison.Ordinal) || Key == "Gamepad_RightX" || Key == "Gamepad_RightY") ? "rightStick"
                        : Key == "MouseX" || Key == "MouseY" || Key == "Mouse2D" ? "mouseAxis" : null;
                    if (prefix == null) return BindingButton.FriendlyKey(Key);
                    string component = Key.EndsWith("2D", StringComparison.Ordinal) ? "XY" : Key.Substring(Key.Length - 1);
                    return UnifiedBindingUi.Text(prefix) + " " + component + " · " + Key;
                }
            }
            public KeyChoice(string key) { Key = key; }
            public override string ToString() { return Label; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0xff) gamepadCapture.ReadRawInput(message.LParam);
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing) { if (disposing) { gamepadCapture.Dispose(); modeTimer.Dispose(); tip.Dispose(); } base.Dispose(disposing); }
    }

    internal static class UnifiedEnumerable
    {
        internal static int FirstOrDefault(this IEnumerable<int> values, Func<int, bool> predicate, int fallback)
        { foreach (int value in values) if (predicate(value)) return value; return fallback; }
    }
}
