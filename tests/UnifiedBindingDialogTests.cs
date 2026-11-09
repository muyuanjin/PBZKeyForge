using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class UnifiedBindingDialogTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static string output;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static IEnumerable<Control> All(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in All(child)) yield return descendant; } }
    static void Click(Form form, string name) { All(form).OfType<Button>().Single(button => button.Name == name).PerformClick(); Application.DoEvents(); }
    static void Search(Form form, string value)
    {
        Field<TextBox>(form, "search").Text = value;
        form.GetType().GetMethod("FlushSearch", Hidden).Invoke(form, null);
        Application.DoEvents();
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = args.FirstOrDefault() ?? "dist/unified-dialog-check"; Directory.CreateDirectory(output);
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
            if (args.Contains("--layout-only"))
            {
                Layout(workspace);
                Console.WriteLine("UnifiedBindingDialogTests PASS " + checks);
                return 0;
            }
            InactiveCatalog();
            IndependentBindingSources(workspace);
            MovementControls();
            TriggerControls(workspace);
            CustomInput(workspace);
            MoreSettings(workspace);
            SequenceChords(workspace);
            SearchLanguage(workspace);
            SharedMoveNames();
            NativeAbilityTrigger(workspace);
            AdvancedTargets(workspace);
            RecursiveValues();
            CustomFunctionPath(workspace);
            Layout(workspace);
            Console.WriteLine("UnifiedBindingDialogTests PASS " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void InactiveCatalog()
    {
        var config = new KeyEnhancementConfiguration();
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        using (var dialog = new UnifiedActionChoiceDialog(workspace, "K", delegate { config.Enabled = true; return true; }))
        {
            dialog.Show(); Application.DoEvents();
            var functions = Field<ListBox>(dialog, "functions");
            Check(functions.Items.Count == workspace.FunctionChoices.Count(function => function.GroupPath[0] == "native"), "Inactive action picker omits common controls");
            Field<ComboBox>(dialog, "groups").SelectedIndex = 0; Application.DoEvents();
            Check(functions.Items.Count == workspace.FunctionChoices.Length, "All-functions category hides additional functions");
            functions.ClearSelected(); functions.SelectedIndex = Enumerable.Range(0, functions.Items.Count).First(index => ((BindingFunction)functions.Items[index]).Id == "ability:Ability.Action.Hun.SpecialDash");
            Check(Field<Label>(dialog, "status").Text.Contains(I18n.Str("unified.requiresbrief")), "Inactive picker lacks the key enhancement hint");
            Capture(dialog, "inactive-action-picker.png");
            Click(dialog, "addSelectedAction");
            Check(!config.Enabled && config.Rules.Count == 1 && dialog.Result == "ability:Ability.Action.Hun.SpecialDash", "Inactive editing enabled execution or lost the selected input");
        }
        using (var dialog = new UnifiedActionBindingsDialog(new BindingWorkspace(EditorSession.CreateOffline(), new KeyEnhancementConfiguration(), new KeyEnhancementConfiguration(), false, -1), "ability:Ability.Action.Hun.SpecialDash", null, null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<Label>(dialog, "status").Text.Contains(I18n.Str("unified.requiresbrief")), "Inactive action details lack an enable hint");
            Capture(dialog, "inactive-action-details.png");
        }
    }
    static void IndependentBindingSources(BindingWorkspace workspace)
    {
        var raw = KeyEnhancementCatalog.ForMapping("IA_Guard").First();
        Check(workspace.Entries("IA_Guard").Any(entry => entry.IsNative), "The original guard input fixture is unbound");
        using (var dialog = new UnifiedActionBindingsDialog(workspace, raw.Id, null, null))
        {
            dialog.Show(); Application.DoEvents();
            var hint = All(dialog).Single(control => control.Name == "nativeEntryHint");
            Check(hint.Visible && hint.Text.Contains(BindingWorkspace.FunctionLabel("IA_Guard")), "Raw input details do not point to the ordinary guard entry");
            Check(Field<Label>(dialog, "status").Text == I18n.Str("unified.additionalnone"), "An empty separate binding list is presented as an unbound game input");
            Capture(dialog, "raw-input-sources.png");
        }
    }
    static void MovementControls()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        using (var dialog = new UnifiedActionChoiceDialog(workspace, "I", null))
        {
            dialog.Show(); Application.DoEvents();
            var functions = Field<ListBox>(dialog, "functions");
            Check(functions.Items.Count == workspace.FunctionChoices.Length && Field<ComboBox>(dialog, "groups").SelectedIndex == 0, "Enabled action picker must open the complete catalog");
            var categories = Field<ComboBox>(dialog, "groups");
            categories.SelectedIndex = Enumerable.Range(0, categories.Items.Count).Single(index => Field<string>(categories.Items[index], "Path") == "locomotion/movement"); Application.DoEvents();
            Check(((BindingFunction)functions.Items[0]).Id == "movement:forward" && functions.Items.Count == 7, "Movement category omits directions or axes");
            ButtonsVisible(dialog);
            foreach (var button in All(dialog).OfType<Button>().Where(button => button.Visible))
            {
                Control parent = button.Parent;
                while (parent != null)
                {
                    Check(parent.RectangleToScreen(parent.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)), "Picker button clipped by its layout: " + button.Name);
                    parent = parent.Parent;
                }
            }
            Capture(dialog, "movement-action-picker.png");
            Search(dialog, "收刀");
            Check(functions.Items.Count == 3 && ((BindingFunction)functions.Items[0]).Id == "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed", "Sheathing cannot be found from the ordinary key entry");
            Check(((BindingFunction)functions.SelectedItem).Id == "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed", "Sheathing search selects a less relevant ordinary mapping");
            Search(dialog, "更换武器");
            functions.SelectedItem = functions.Items.Cast<BindingFunction>().Single(item => item.Id == "IA_SwitchWeapon");
            Search(dialog, "收刀");
            Check(((BindingFunction)functions.SelectedItem).Id == "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed", "Changing the search retains an old less relevant selection");
            Capture(dialog, "sheathe-action-picker.png");
        }
        var function = workspace.Functions.Single(item => item.Id == "movement:left");
        var original = workspace.Entries(function.Id).Single(item => item.Keys.Contains("A"));
        using (var dialog = new UnifiedBindingEditDialog(workspace, function.Id, original, "A", null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<ComboBox>(dialog, "outputMode").SelectedIndex == 1 && Field<NumericUpDown>(dialog, "outputX").Value == 0
                && Field<NumericUpDown>(dialog, "outputY").Value == -1, "Movement editor did not load the native left direction");
            Capture(dialog, "movement-left-editor.png");
        }
    }
    static void TriggerControls(BindingWorkspace workspace)
    {
        string[] triggers = { "default", "press", "release", "tap", "hold", "multi_tap", "pulse", "toggle", "sequence", "axis", "down" };
        for (int index = 0; index < triggers.Length; index++)
            using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "J", null))
            {
                dialog.Show(); Application.DoEvents();
                Field<ComboBox>(dialog, "trigger").SelectedIndex = index;
                Check(Field<NumericUpDown>(dialog, "seconds").Visible == (index == 3 || index == 4), "Duration shown for unrelated trigger");
                Check(Field<NumericUpDown>(dialog, "interval").Visible == (index == 5 || index == 6 || index == 8), "Interval shown for unrelated trigger");
                Check(Field<ComboBox>(dialog, "axis").Visible == (index == 9), "Axis picker visibility");
                if (index == 8)
                {
                    var keys = Field<List<string>>(dialog, "extraKeys"); keys.Add("O"); keys.Add("J");
                    dialog.GetType().GetField("inputChanged", Hidden).SetValue(dialog, true);
                }
                Click(dialog, "saveBinding");
                Check(dialog.DialogResult == DialogResult.OK && dialog.Result.Trigger == triggers[index], "Trigger form did not preserve selected trigger");
                if (index == 8) Check(dialog.Result.Rule.Steps.Select(step => step.Single()).SequenceEqual(new[] { "J", "O", "J" }), "Sequence lost repeated keys or order");
                if (index == 9) Check(!string.IsNullOrEmpty(dialog.Result.Axis) && dialog.Result.Keys.Length == 0, "Axis form retained hidden key conditions");
            }
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", workspace.Entries("IA_Jump").First(entry => entry.IsNative), null, null))
        {
            dialog.Show(); Application.DoEvents(); Field<List<string>>(dialog, "extraKeys").Add("LeftControl");
            Field<ComboBox>(dialog, "trigger").SelectedIndex = 9;
            Field<ComboBox>(dialog, "axis").SelectedIndex = 0;
            Capture(dialog, "edited-axis-input.png");
            Click(dialog, "saveBinding");
            Check(dialog.Result.Keys.Length == 0 && dialog.Result.Axis == "Gamepad_Left2D", "Switching an existing binding to axis saved its hidden chord");
            var changed = workspace.Replace(workspace.Entries("IA_Jump").First(entry => entry.IsNative), dialog.Result);
            Check(changed.Rule.Keys.Count == 0 && changed.Rule.Axis == "Gamepad_Left2D", "Workspace persisted hidden key conditions after axis editing");
            workspace.Remove(changed);
        }
        var custom = new KeyEnhancementRule { Trigger = "custom", EvaluateLua = "function(now,snapshot,context,state) return {active=true} end" };
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", new BindingEntry { Keys = new[] { "J" }, Trigger = "custom", Rule = custom }, null, null))
        {
            dialog.Show(); Application.DoEvents(); Click(dialog, "saveBinding");
            Check(dialog.Result.Trigger == "custom" && dialog.Result.Rule.EvaluateLua == custom.EvaluateLua, "Editing custom input silently replaced recognizer");
        }
    }
    static void MoreSettings(BindingWorkspace workspace)
    {
        var rule = new KeyEnhancementRule { ConsumeKeys = new List<string> { "Gamepad_FaceButton_Bottom" }, WhenLua = "function(context) return true end", Watch = new List<string> { "F8" } };
        var entry = new BindingEntry { Keys = new[] { "Gamepad_LeftShoulder", "Gamepad_FaceButton_Bottom" }, Rule = rule, Priority = 17, Enabled = false,
            OutputValue = new Dictionary<string, object> { { "x", -1.0 }, { "y", .5 }, { "z", 0.0 } } };
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", entry, null, null))
        {
            dialog.Show(); Application.DoEvents(); Click(dialog, "saveBinding");
            Check(!dialog.Result.Enabled && dialog.Result.Priority == 17, "Imported binding enable/priority changed");
            Check(dialog.Result.Rule.ConsumeKeys.SequenceEqual(rule.ConsumeKeys) && dialog.Result.Rule.WhenLua == rule.WhenLua && dialog.Result.Rule.Watch.Count == 0, "Rule details or hidden input normalization changed");
            Check((double)((Dictionary<string, object>)dialog.Result.OutputValue)["x"] == -1, "Output vector lost on edit");
        }
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", entry, null, null))
        {
            dialog.Show(); Application.DoEvents(); Click(dialog, "moreBindingSettings");
            Field<CheckBox>(dialog, "bindingEnabled").Checked = true; Field<NumericUpDown>(dialog, "priority").Value = -9;
            var keys = Field<CheckedListBox>(dialog, "consumeKeys");
            Check(keys.Items.Count == 2 && keys.CheckedItems.Count == 1, "Partial consume picker did not reflect physical chord");
            for (int i = 0; i < keys.Items.Count; i++) keys.SetItemChecked(i, true);
            Field<NumericUpDown>(dialog, "outputX").Value = 0; Field<NumericUpDown>(dialog, "outputY").Value = -2; Field<NumericUpDown>(dialog, "outputZ").Value = 3;
            Click(dialog, "saveBinding");
            Check(dialog.Result.Enabled && dialog.Result.Priority == -9 && dialog.Result.Rule.Consume && dialog.Result.Rule.ConsumeKeys.Count == 0, "All-key consume or more settings not saved");
            Check((double)((Dictionary<string, object>)dialog.Result.OutputValue)["y"] == -2, "Signed output vector not saved");
            Check(!rule.Consume && rule.ConsumeKeys.Count == 1, "More settings mutated original rule");
        }
        entry.Rule.Consume = true; entry.Rule.ConsumeKeys.Clear();
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", entry, null, null))
        {
            dialog.Show(); Application.DoEvents(); Click(dialog, "moreBindingSettings");
            var keys = Field<CheckedListBox>(dialog, "consumeKeys"); keys.SetItemChecked(0, false);
            Field<ComboBox>(dialog, "outputMode").SelectedIndex = 0; Click(dialog, "saveBinding");
            Check(!dialog.Result.Rule.Consume && dialog.Result.Rule.ConsumeKeys.Single() == "Gamepad_FaceButton_Bottom", "LB+A partial consume did not preserve LB");
            Check(dialog.Result.OutputValue == null, "Input inheritance not saved");
        }
    }
    static void CustomInput(BindingWorkspace workspace)
    {
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, null, null))
        {
            dialog.Show(); Application.DoEvents(); Field<ComboBox>(dialog, "trigger").SelectedIndex = 11;
            Check(Field<TextBox>(dialog, "evaluatorCode").Visible && Field<CheckedListBox>(dialog, "watchedKeys").Visible,
                "New custom input did not expose its script and key controls");
            Check(Field<TextBox>(dialog, "evaluatorCode").Lines.Length == 3, "Custom script template did not display separate editable lines");
            Field<TextBox>(dialog, "evaluatorCode").Text = "function(now,snapshot) return {active=snapshot.F8==1,pulse=true,value=1} end";
            var watched = Field<CheckedListBox>(dialog, "watchedKeys");
            int keyIndex = Enumerable.Range(0, watched.Items.Count).First(index => watched.Items[index].ToString().Contains("F8"));
            watched.SetItemChecked(keyIndex, true);
            Click(dialog, "moreBindingSettings"); Field<ComboBox>(dialog, "condition").SelectedIndex = 1;
            Click(dialog, "saveBinding");
            Check(dialog.DialogResult == DialogResult.OK && dialog.Result.Rule.Watch.Contains("F8")
                && dialog.Result.Rule.EvaluateLua.Contains("snapshot.F8") && dialog.Result.Rule.WhenLua.Contains("context.gameplay"),
                "New custom input lost its selected keys, script or condition");
            var added = workspace.Add("IA_Jump", dialog.Result);
            Check(!added.IsNative && added.Rule.Trigger == "custom", "Custom input was saved as a native slot");
            workspace.Remove(added);
        }
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); Click(dialog, "moreBindingSettings"); Field<ComboBox>(dialog, "condition").SelectedIndex = 3;
            Click(dialog, "saveBinding");
            Check(dialog.Result.Rule.WhenLua == "function(context) return not context.paused end", "A standard condition required a script or was discarded");
        }
    }
    static void SequenceChords(BindingWorkspace workspace)
    {
        var entry = new BindingEntry { Keys = new[] { "J" }, Trigger = "sequence", Rule = new KeyEnhancementRule { Trigger = "sequence", Steps = new List<string[]> { new[] { "LeftControl", "J" }, new[] { "O" } } } };
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", entry, null, null))
        {
            dialog.Show(); Application.DoEvents(); var steps = Field<ListBox>(dialog, "sequenceSteps");
            Check(steps.Items.Count == 2 && steps.Items[0].ToString().Contains("Left Ctrl"), "Sequence chord not displayed");
            Field<ComboBox>(dialog, "mainKey").SelectedIndex = -1;
            steps.SelectedIndex = 1; Click(dialog, "moveSequenceStepUp"); Click(dialog, "saveBinding");
            Check(dialog.Result.Rule.Steps[0].Single() == "O" && dialog.Result.Rule.Steps[1].SequenceEqual(new[] { "LeftControl", "J" }), "Sequence order or chord changed");
            Check(dialog.Result.Keys.OrderBy(key => key).SequenceEqual(new[] { "J", "LeftControl", "O" }), "Sequence sampling keys did not follow edited steps");
            Check(entry.Rule.Steps[0][0] == "LeftControl", "Sequence edit mutated original");
        }
    }
    static void SearchLanguage(BindingWorkspace workspace)
    {
        I18n.Current = "en";
        using (var view = new UnifiedActionView())
        {
            view.Render(workspace); Check(Field<Label>(view, "searchTitle").Text == "Search functions", "English search heading");
            I18n.Current = "zh-Hans"; view.Render(workspace);
            Check(Field<Label>(view, "searchTitle").Text == "搜索功能", "Search heading did not follow current language");
        }
    }
    static void SharedMoveNames()
    {
        const string move = "move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_BD";
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        using (var dialog = new UnifiedActionChoiceDialog(workspace, "G", null))
        {
            dialog.Show(); Application.DoEvents();
            var list = Field<ListBox>(dialog, "functions");
            Search(dialog, "焰波");
            var flame = list.Items.Cast<BindingFunction>().Where(item => item.Id == move).ToArray();
            Check(flame.Length == 1 && flame[0].Label == "炽刃 · 焰波", "Flame Wave is not its own picker entry.");
            Search(dialog, "独眼弓 凝视");
            Check(list.Items.Cast<BindingFunction>().Any(item => item.Label == "独眼弓 · 凝视"),
                "A generated weapon-qualified name is not directly searchable.");
            Search(dialog, "以逸待劳·之二");
            var placeholder = list.Items.Cast<BindingFunction>()
                .Where(item => item.Label == "别离残钩 · 以逸待劳·之二").ToArray();
            Check(placeholder.Length == 2 && placeholder.Select(item => item.Id).Distinct().Count() == 2,
                "The expanded placeholder name did not retain both distinct technical actions.");
            Search(dialog, "旋风斩击");
            var whirlwind = list.Items.Cast<BindingFunction>().Where(item => item.Id == move).ToArray();
            Check(whirlwind.Length == 1 && whirlwind[0].Label == "炽刃 · 旋风斩击", "Whirlwind Slash is not its own picker entry.");
            Search(dialog, move);
            Check(list.Items.Cast<BindingFunction>().Count(item => item.Id == move) == 1, "A technical move search duplicated every display name.");
            Search(dialog, "炽刃");
            list.ClearSelected();
            int[] indexes = Enumerable.Range(0, list.Items.Count).Where(index => ((BindingFunction)list.Items[index]).Id == move).ToArray();
            Check(indexes.Length == 2, "Blazing Edge does not expose both shared original names as separate rows.");
            foreach (int index in indexes) list.SetSelected(index, true);
            Application.DoEvents();
            Click(dialog, "addSelectedAction");
            Check(config.Rules.Count == 1 && config.Rules[0].Outputs.Count == 1 && dialog.Result == move,
                "Selecting more than one name for the same move created duplicate outputs.");
        }
        using (var details = new UnifiedActionBindingsDialog(workspace, move, "炽刃 · 焰波", null, null))
        {
            details.Show(); Application.DoEvents();
            Check(details.Text == "炽刃 · 焰波", "Action details replaced the clicked shared name with the canonical label.");
            details.Close();
        }
        using (var editor = new UnifiedBindingEditDialog(workspace, move, "炽刃 · 焰波", null, "G", null))
        {
            editor.Show(); Application.DoEvents();
            Check(editor.Text.StartsWith("炽刃 · 焰波", StringComparison.Ordinal),
                "The binding editor replaced the clicked shared name with the canonical label.");
            editor.Close();
        }
        const string dualHook = "move:Weapon.Weapon_Hun_DualHook.GA_Hun_DualHook_DB";
        var placeholderConfig = new KeyEnhancementConfiguration { Enabled = true };
        var placeholderWorkspace = new BindingWorkspace(EditorSession.CreateOffline(), placeholderConfig, placeholderConfig.Clone(), false, -1);
        using (var dialog = new UnifiedActionChoiceDialog(placeholderWorkspace, "G", null))
        {
            dialog.Show(); Application.DoEvents();
            Search(dialog, "别离残钩");
            var list = Field<ListBox>(dialog, "functions");
            list.ClearSelected();
            int[] indexes = Enumerable.Range(0, list.Items.Count)
                .Where(index => ((BindingFunction)list.Items[index]).Id == dualHook).ToArray();
            Check(indexes.Length == 4 && indexes.Select(index => ((BindingFunction)list.Items[index]).Label).Contains("别离残钩 · 以逸待劳·之二"),
                "Every expanded Dual Hook name is not independently selectable.");
            foreach (int index in indexes) list.SetSelected(index, true);
            Application.DoEvents();
            Click(dialog, "addSelectedAction");
            Check(placeholderConfig.Rules.Count == 1 && placeholderConfig.Rules[0].Outputs.Count == 1 && dialog.Result == dualHook,
                "Selecting multiple expanded aliases for one action created duplicate outputs.");
        }
    }
    static void NativeAbilityTrigger(BindingWorkspace workspace)
    {
        const string function = "ability:Ability.Action.Hun.SpecialDash";
        int changes = 0;
        using (var dialog = new UnifiedActionBindingsDialog(workspace, function, null, delegate { changes++; }))
        {
            dialog.Show(); Application.DoEvents(); var checkbox = Field<CheckBox>(dialog, "nativeAbilityTrigger");
            Check(checkbox.Visible && checkbox.Checked, "Native ability trigger defaults on");
            checkbox.Checked = false; Application.DoEvents();
            Check(!workspace.UsesNativeTrigger(function) && changes == 1, "Native trigger suppression control not applied");
            Click(dialog, "restoreAction"); Check(checkbox.Checked && workspace.UsesNativeTrigger(function), "Restore did not restore native ability trigger");
            dialog.Scale(new SizeF(1.5f, 1.5f)); dialog.ClientSize = new Size(840, 660); Application.DoEvents(); Capture(dialog, "ability-trigger-150.png");
        }
        var config = new KeyEnhancementConfiguration(); var disabled = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        using (var dialog = new UnifiedActionBindingsDialog(disabled, function, null, null))
        {
            dialog.Show(); Application.DoEvents(); Field<CheckBox>(dialog, "nativeAbilityTrigger").Checked = false; Application.DoEvents();
            Check(!config.Enabled && !disabled.UsesNativeTrigger(function) && !Field<CheckBox>(dialog, "nativeAbilityTrigger").Checked, "Disabled enhancement cannot edit its native trigger draft");
        }
    }
    static void AdvancedTargets(BindingWorkspace workspace)
    {
        var original = new KeyEnhancementOutput { Kind = "function", Path = "/Script/Test:Call", Receiver = "pawn",
            Args = new object[] { true, new Dictionary<string, object> { { "tag", "GameplayEvent.Test" } }, new object[] { 1, "text" } } };
        using (var dialog = new AdvancedFunctionDialog())
        {
            dialog.LoadTarget(original, "Editable function"); dialog.Show(); Application.DoEvents();
            Check(!Field<ComboBox>(dialog, "kind").Enabled && !Field<ComboBox>(dialog, "condition").Visible,
                "Editing a function exposed a different target kind or replaced per-binding conditions");
            Field<TextBox>(dialog, "path").Text = "/Script/Test:Changed"; Click(dialog, "saveCustomFunction");
            Check(dialog.ResultTarget.Path == "/Script/Test:Changed" && (bool)dialog.ResultTarget.Args[0]
                && (string)((Dictionary<string, object>)dialog.ResultTarget.Args[1])["tag"] == "GameplayEvent.Test"
                && ((object[])dialog.ResultTarget.Args[2]).Length == 2, "Editing a custom function lost nested typed arguments");
            Check(original.Path == "/Script/Test:Call", "Editing a function mutated its source before confirmation");
        }
        using (var dialog = new AdvancedFunctionDialog())
        {
            dialog.Show(); Application.DoEvents(); Field<TextBox>(dialog, "name").Text = "Custom jump";
            Field<TextBox>(dialog, "path").Text = "/Script/Engine.Character:Jump";
            Field<ComboBox>(dialog, "condition").SelectedIndex = 1;
            var rows = Field<DataGridView>(dialog, "arguments"); rows.Rows.Add("", "number", "2.5"); rows.Rows.Add("", "bool", "true");
            Click(dialog, "saveCustomFunction");
            Check(dialog.ResultTarget.Kind == "function" && (double)dialog.ResultTarget.Args[0] == 2.5 && (bool)dialog.ResultTarget.Args[1], "Typed function arguments");
            Check(dialog.ConditionLua.Contains("gameplay"), "Function condition");
            var entry = workspace.AddCustom(dialog.ResultTarget, new BindingEntry { Keys = new[] { "F6" }, Trigger = "default", Rule = new KeyEnhancementRule { WhenLua = dialog.ConditionLua } }, dialog.FunctionName);
            Check(workspace.Functions.Single(function => function.Id == entry.FunctionId).Label == "Custom jump", "Custom function name lost");
        }
        using (var dialog = new AdvancedFunctionDialog())
        {
            dialog.Show(); Application.DoEvents(); Field<ComboBox>(dialog, "kind").SelectedIndex = 1;
            Field<TextBox>(dialog, "name").Text = "Event"; Field<TextBox>(dialog, "path").Text = "GameplayEvent.Test";
            Field<DataGridView>(dialog, "arguments").Rows.Add("EventMagnitude", "number", "3"); Click(dialog, "saveCustomFunction");
            Check(dialog.ResultTarget.Kind == "event" && (double)((Dictionary<string, object>)dialog.ResultTarget.Payload)["EventMagnitude"] == 3, "Typed event payload");
        }
        using (var dialog = new AdvancedFunctionDialog())
        {
            dialog.Show(); Application.DoEvents(); Field<ComboBox>(dialog, "kind").SelectedIndex = 2;
            Field<TextBox>(dialog, "name").Text = "Property"; Field<TextBox>(dialog, "property").Text = "JumpMaxCount";
            Field<ComboBox>(dialog, "valueType").SelectedIndex = 1; Field<NumericUpDown>(dialog, "number").Value = 2; Click(dialog, "saveCustomFunction");
            Check(dialog.ResultTarget.Kind == "property" && dialog.ResultTarget.Property == "JumpMaxCount" && (double)dialog.ResultTarget.Value == 2, "Property fields");
        }
        using (var dialog = new AdvancedFunctionDialog())
        {
            dialog.Show(); Application.DoEvents(); Field<ComboBox>(dialog, "kind").SelectedIndex = 3;
            Field<TextBox>(dialog, "name").Text = "Return override"; Field<TextBox>(dialog, "path").Text = "/Script/Engine.Character:CanJumpInternal";
            Field<CheckBox>(dialog, "boolean").Checked = true; Field<ComboBox>(dialog, "condition").SelectedIndex = 3; Click(dialog, "saveCustomFunction");
            Check((bool)dialog.ResultHook["value"] && !(bool)((Dictionary<string, object>)dialog.ResultHook["when"])["paused"], "Hook return and condition");
        }
    }
    static void Layout(BindingWorkspace workspace)
    {
        CombatLayout(workspace);
        using (var dialog = new UnifiedActionChoiceDialog(workspace, "J", null))
        {
            dialog.Show(); Application.DoEvents(); dialog.Scale(new SizeF(1.5f, 1.5f)); dialog.ClientSize = dialog.MinimumSize;
            Search(dialog, "瞬身");
            Check(Field<ListBox>(dialog, "functions").Items.Count > 0, "Natural-language stance dash search");
            ButtonsVisible(dialog); Capture(dialog, "choice-150.png");
        }
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); dialog.Scale(new SizeF(1.5f, 1.5f)); dialog.Size = dialog.MinimumSize; Field<ComboBox>(dialog, "trigger").SelectedIndex = 8;
            Application.DoEvents();
            Capture(dialog, "binding-150.png");
            var sequenceScroll = All(dialog).OfType<ScrollableControl>().First(control => control.AutoScroll);
            sequenceScroll.ScrollControlIntoView(All(dialog).Single(control => control.Name == "moveSequenceStepDown")); Application.DoEvents();
            foreach (string name in new[] { "addSequenceStep", "editSequenceStep", "removeSequenceStep", "moveSequenceStepUp", "moveSequenceStepDown" })
            {
                var stepButton = All(dialog).OfType<Button>().Single(button => button.Name == name);
                Check(sequenceScroll.RectangleToScreen(sequenceScroll.ClientRectangle).Contains(stepButton.RectangleToScreen(stepButton.ClientRectangle)), "Sequence command clipped at 150%: " + name);
            }
            ButtonsVisible(dialog); Capture(dialog, "binding-150.png");
            Click(dialog, "moreBindingSettings");
            var scroll = All(dialog).OfType<ScrollableControl>().First(control => control.AutoScroll);
            scroll.ScrollControlIntoView(Field<ComboBox>(dialog, "outputMode")); Application.DoEvents();
            ButtonsVisible(dialog); Capture(dialog, "binding-more-150.png");
            Field<ComboBox>(dialog, "outputMode").SelectedIndex = 1; scroll.ScrollControlIntoView(Field<NumericUpDown>(dialog, "outputZ")); Application.DoEvents();
            Check(Field<NumericUpDown>(dialog, "outputZ").Width >= 40, "Vector component too narrow at 150%");
            ButtonsVisible(dialog); Capture(dialog, "binding-vector-150.png");
        }
        using (var dialog = new AdvancedFunctionDialog())
        {
            dialog.Show(); Application.DoEvents(); dialog.Scale(new SizeF(1.5f, 1.5f)); dialog.ClientSize = new Size(855, 705); Field<ComboBox>(dialog, "kind").SelectedIndex = 3;
            ButtonsVisible(dialog); Capture(dialog, "advanced-150.png");
        }
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); dialog.Scale(new SizeF(1.5f, 1.5f)); dialog.Size = dialog.MinimumSize;
            Field<ComboBox>(dialog, "trigger").SelectedIndex = 11;
            var scroll = All(dialog).OfType<ScrollableControl>().First(control => control.AutoScroll);
            scroll.ScrollControlIntoView(Field<TextBox>(dialog, "evaluatorCode")); Application.DoEvents();
            Check(Field<TextBox>(dialog, "evaluatorCode").Width > 250, "Custom evaluator is too narrow at 150%");
            ButtonsVisible(dialog); Capture(dialog, "binding-script-150.png");
        }
        using (var dialog = new AdvancedValueDialog(new Dictionary<string, object> { { "Nested", new object[] { true, 2.5, new Dictionary<string, object> { { "object", "/Game/Test.Object" }, { "class", "/Script/CoreUObject.Object" } } } } }))
        {
            dialog.Show(); Application.DoEvents(); dialog.Scale(new SizeF(1.5f, 1.5f)); dialog.ClientSize = new Size(765, 615);
            Check(dialog.RectangleToScreen(dialog.ClientRectangle).Contains(All(dialog).OfType<Button>().Single(button => button.Name == "saveValue").RectangleToScreen(All(dialog).OfType<Button>().Single(button => button.Name == "saveValue").ClientRectangle)), "Recursive value save clipped at 150%");
            Capture(dialog, "value-150.png");
        }
    }
    static void CombatLayout(BindingWorkspace workspace)
    {
        const string finisher = "move:Ability.Action.Hun.CombatArt.BronzeSword.Final";
        foreach (float scale in new[] { 1f, 1.5f })
        using (var dialog = new UnifiedBindingEditDialog(workspace, finisher,
            new BindingEntry { Keys = new[] { "G" }, Trigger = "default" }, null, null))
        {
            dialog.Show(); Application.DoEvents();
            if (scale != 1) dialog.Scale(new SizeF(scale, scale));
            Application.DoEvents();
            Capture(dialog, "combat-binding-" + (scale == 1 ? "100" : "150") + ".png");
            foreach (string name in new[] { "damageMultiplier", "startupSpeed", "recoverySpeed" })
            {
                var number = Field<NumericUpDown>(dialog, name);
                Check(number.Parent.Height <= number.Height + dialog.Font.Height * 2,
                    "Combat parameter has excessive empty space: " + name + " " + number.Parent.Height);
            }
            var scroll = All(dialog).OfType<ScrollableControl>().First(control => control.AutoScroll);
            Check(!scroll.HorizontalScroll.Visible, "Combat editor scrolls horizontally");
            var more = All(dialog).Single(control => control.Name == "moreBindingSettings");
            Check(dialog.RectangleToScreen(dialog.ClientRectangle).Contains(more.RectangleToScreen(more.ClientRectangle)),
                "The additional binding settings control is clipped at the default size");
            ButtonsVisible(dialog);
            Click(dialog, "moreBindingSettings");
            scroll.ScrollControlIntoView(Field<ComboBox>(dialog, "condition")); Application.DoEvents();
            Check(Field<NumericUpDown>(dialog, "priority").Parent.Height <= dialog.Font.Height * 4,
                "Advanced parameter has excessive empty space");
            Capture(dialog, "combat-binding-more-" + (scale == 1 ? "100" : "150") + ".png");
            dialog.Size = dialog.MinimumSize; Application.DoEvents();
            ButtonsVisible(dialog);
            Check(!scroll.HorizontalScroll.Visible, "Minimum combat editor scrolls horizontally");
            scroll.ScrollControlIntoView(Field<NumericUpDown>(dialog, "recoverySpeed")); Application.DoEvents();
            Capture(dialog, "combat-binding-minimum-" + (scale == 1 ? "100" : "150") + ".png");
        }
    }
    static void CustomFunctionPath(BindingWorkspace workspace)
    {
        var binding = workspace.AddCustom(new KeyEnhancementOutput { Kind = "property", Property = "bFlag", Value = false },
            new BindingEntry { Keys = new[] { "F4" }, Trigger = "press" }, "可编辑状态");
        using (var details = new UnifiedActionBindingsDialog(workspace, binding.FunctionId, null, null))
        using (var timer = new Timer { Interval = 40 })
        {
            bool opened = false; Exception failure = null;
            var started = DateTime.UtcNow;
            timer.Tick += delegate
            {
                var editor = Application.OpenForms.OfType<AdvancedFunctionDialog>().FirstOrDefault();
                if (editor == null)
                {
                    if ((DateTime.UtcNow - started).TotalSeconds > 5) { timer.Stop(); failure = new Exception("Custom function editor did not open from action details"); details.Close(); }
                    return;
                }
                timer.Stop(); opened = true;
                try { Field<CheckBox>(editor, "boolean").Checked = true; Click(editor, "saveCustomFunction"); }
                catch (Exception ex) { failure = ex; editor.Close(); }
            };
            details.Show(); Application.DoEvents(); timer.Start(); Click(details, "editCustomFunction"); timer.Stop();
            if (failure != null) throw failure;
            Check(opened && (bool)workspace.CustomOutput(binding.FunctionId).Value, "Action detail path did not persist custom function edits");
            Check(workspace.Entries(binding.FunctionId).Single().Keys.Single() == "F4", "Editing a function from action details changed its input");
        }
        using (var details = new UnifiedActionBindingsDialog(workspace, binding.FunctionId, null, null))
        {
            details.Show(); Application.DoEvents(); Click(details, "removeBinding");
            Check(All(details).OfType<Button>().Single(button => button.Name == "editCustomFunction").Enabled
                && workspace.Entries(binding.FunctionId).Length == 0 && workspace.CustomOutput(binding.FunctionId) != null,
                "Unbinding the last input discarded its editable function definition");
        }
    }
    static void RecursiveValues()
    {
        var source = new Dictionary<string, object> {
            { "Nested", new object[] { true, 2.5, new Dictionary<string, object> { { "object", "/Game/Test.Object" }, { "class", "/Script/CoreUObject.Object" } } } },
            { "Tag", new Dictionary<string, object> { { "tag", "GameplayEvent.Test" } } }
        };
        using (var dialog = new AdvancedValueDialog(source))
        {
            dialog.Show(); Application.DoEvents(); Click(dialog, "saveValue");
            var result = (Dictionary<string, object>)dialog.Result; var array = (object[])result["Nested"];
            Check((bool)array[0] && (double)array[1] == 2.5, "Nested scalar array values");
            Check((string)((Dictionary<string, object>)array[2])["class"] == "/Script/CoreUObject.Object", "Nested object class reference");
            Check((string)((Dictionary<string, object>)result["Tag"])["tag"] == "GameplayEvent.Test", "Nested tagged argument");
        }
        using (var dialog = new AdvancedValueDialog(new Dictionary<string, object> { { "object", "/Game/Before.Object" } }))
        {
            dialog.Show(); Application.DoEvents(); Field<TextBox>(dialog, "text").Text = "/Game/After.Object"; Field<TextBox>(dialog, "className").Text = "/Script/CoreUObject.Object"; Click(dialog, "saveValue");
            Check((string)((Dictionary<string, object>)dialog.Result)["object"] == "/Game/After.Object", "Object-reference field editing");
        }
        var configuration = new KeyEnhancementConfiguration(); configuration.Hooks.Add(new Dictionary<string, object> { { "name", "Existing" }, { "path", "/Script/Engine.Character:CanJumpInternal" }, { "value", true } });
        using (var dialog = new AdvancedFunctionDialog(configuration))
        {
            dialog.Show(); Application.DoEvents(); Field<ComboBox>(dialog, "kind").SelectedIndex = 3; Field<ComboBox>(dialog, "existingHook").SelectedIndex = 1;
            Check(Field<TextBox>(dialog, "name").Text == "Existing" && Field<CheckBox>(dialog, "boolean").Checked, "Existing hook fields not loaded");
            Click(dialog, "deleteReturnOverride"); Check(dialog.DeleteHook && dialog.HookIndex == 0 && configuration.Hooks.Count == 1, "Hook delete must stage without editing the source");
        }
    }
    static void ButtonsVisible(Form form)
    {
        Application.DoEvents(); Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        foreach (Button button in All(form).OfType<Button>().Where(button => button.Visible && (button.Name == "saveBinding" || button.Name == "saveCustomFunction" || button.Name == "addSelectedAction" || button.Name == "cancel")))
            Check(client.Contains(button.RectangleToScreen(button.ClientRectangle)), "Visible command clipped: " + button.Name + " " + button.Bounds + " in " + form.ClientSize);
    }
    static void Capture(Form form, string name)
    { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name)); } }
}
