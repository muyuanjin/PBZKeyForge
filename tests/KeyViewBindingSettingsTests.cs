using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class KeyViewBindingSettingsTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Dash = "ability:Ability.Action.Hun.SpecialDash";
    static int checks;
    static string output;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static IEnumerable<Control> All(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in All(child)) yield return descendant; } }
    static Button Button(Control parent, string name) { return All(parent).OfType<Button>().Single(item => item.Name == name); }
    static void Click(Control parent, string name) { Button(parent, name).PerformClick(); Application.DoEvents(); }
    static void Select(UnifiedActionChoiceDialog picker, params string[] ids)
    {
        var list = Field<ListBox>(picker, "functions");
        Field<ComboBox>(picker, "groups").SelectedIndex = 0;
        list.ClearSelected();
        for (int i = 0; i < list.Items.Count; i++) if (ids.Contains(((BindingFunction)list.Items[i]).Id)) list.SetSelected(i, true);
        Application.DoEvents();
        Check(list.SelectedItems.Count == ids.Length, "Picker selection is incomplete");
    }
    static void Shot(Form form, string name)
    {
        form.Refresh(); Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png")); }
    }
    static void OpenEditor(Action open, Action<UnifiedBindingEditDialog> edit)
    {
        Exception failure = null; bool opened = false; var started = DateTime.UtcNow;
        using (var timer = new Timer { Interval = 50 })
        {
            timer.Tick += delegate {
                var dialog = Application.OpenForms.OfType<UnifiedBindingEditDialog>().FirstOrDefault();
                if (dialog == null)
                {
                    if ((DateTime.UtcNow - started).TotalSeconds < 8) return;
                    timer.Stop(); failure = new Exception("Binding click did not open its settings editor");
                    foreach (var modal in Application.OpenForms.Cast<Form>().Where(form => form.Modal).ToArray()) modal.Close();
                    return;
                }
                timer.Stop(); opened = true;
                try { edit(dialog); }
                catch (Exception ex) { failure = ex; }
                finally { if (!dialog.IsDisposed && dialog.Visible) dialog.Close(); }
            };
            timer.Start(); open(); timer.Stop();
        }
        if (failure != null) throw failure;
        Check(opened, "Binding click did not open its settings editor");
    }
    static void Picker(bool enabled, bool pad)
    {
        var config = new KeyEnhancementConfiguration { Enabled = enabled };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), pad, pad ? 0 : -1);
        string key = pad ? "Gamepad_FaceButton_Left" : "F8";
        using (var picker = new UnifiedActionChoiceDialog(workspace, key, null))
        {
            picker.Show(); Application.DoEvents(); Select(picker, Dash);
            var keep = Field<CheckBox>(picker, "nativeAbilityTrigger");
            Check(keep.Visible && keep.Checked && keep.Text == I18n.Str("unified.nativeAbilityTrigger"), "Key picker lacks the original-trigger checkbox");
            string before = config.ToJson(); keep.Checked = false;
            Check(before == config.ToJson(), "Picker changed the draft before adding a binding");
            if (enabled && !pad) Shot(picker, "key-picker-original-trigger");
            Click(picker, "addSelectedAction");
            Check(!workspace.UsesNativeTrigger(Dash) && workspace.Entries(Dash).Single().Keys.SequenceEqual(new[] { key }), "Quick add lost the selected original-trigger policy");
            Check(config.Enabled == enabled, "Editing implicitly enabled enhancement");
        }
        using (var picker = new UnifiedActionChoiceDialog(workspace, key, null))
        {
            picker.Show(); Application.DoEvents(); Select(picker, Dash);
            var keep = Field<CheckBox>(picker, "nativeAbilityTrigger");
            Check(!keep.Checked, "Picker did not reload the saved original-trigger policy");
            string before = config.ToJson(); keep.Checked = true; Click(picker, "cancel");
            Check(config.ToJson() == before, "Cancelling the picker changed the original-trigger policy");
        }
        var current = workspace.Entries(Dash).Single();
        using (var editor = new UnifiedBindingEditDialog(workspace, Dash, current, null, null))
        {
            editor.Show(); Application.DoEvents();
            var keep = Field<CheckBox>(editor, "nativeAbilityTrigger");
            Check(keep.Visible && !keep.Checked, "Existing binding editor did not reload its original-trigger policy");
            keep.Checked = true; Click(editor, "saveBinding"); workspace.Replace(current, editor.Result);
            Check(workspace.UsesNativeTrigger(Dash), "Existing binding editor did not save its original-trigger policy");
        }
        workspace.SetNativeTrigger(Dash, false);
        using (var picker = new UnifiedActionChoiceDialog(workspace, key, null))
        {
            picker.Show(); Application.DoEvents(); Select(picker, Dash);
            OpenEditor(delegate { Click(picker, "configureBinding"); }, delegate(UnifiedBindingEditDialog editor) {
                var keep = Field<CheckBox>(editor, "nativeAbilityTrigger");
                Check(keep.Visible && !keep.Checked, "Configured addition lost the picker's original-trigger policy");
                keep.Checked = true; Click(editor, "saveBinding");
            });
            Check(workspace.UsesNativeTrigger(Dash), "Configured addition did not save its original-trigger policy");
        }
    }
    static void MixedSelection()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        string other = workspace.Functions.First(item => item.Id != Dash && item.Targets.Any(target => target.Kind == "ability"
            && target.Tag != "Ability.Action.Hun.DeflectDash")).Id;
        workspace.SetNativeTrigger(Dash, false);
        using (var picker = new UnifiedActionChoiceDialog(workspace, "F8", null))
        {
            picker.Show(); Application.DoEvents(); Select(picker, Dash, other, "IA_Jump");
            Check(Field<CheckBox>(picker, "nativeAbilityTrigger").CheckState == CheckState.Indeterminate, "Mixed policies are shown as one policy");
            Click(picker, "addSelectedAction");
            Check(!workspace.UsesNativeTrigger(Dash) && workspace.UsesNativeTrigger(other), "Adding mixed functions overwrote their distinct policies");
        }
        using (var picker = new UnifiedActionChoiceDialog(workspace, "F9", null))
        {
            picker.Show(); Application.DoEvents(); Select(picker, Dash, other, "IA_Jump");
            Field<CheckBox>(picker, "nativeAbilityTrigger").Checked = false;
            OpenEditor(delegate { Click(picker, "configureBinding"); }, delegate(UnifiedBindingEditDialog editor) {
                Check(Field<CheckBox>(editor, "nativeAbilityTrigger").Visible && !Field<CheckBox>(editor, "nativeAbilityTrigger").Checked,
                    "Mixed native/ability selection hid its original-trigger settings");
                Click(editor, "saveBinding");
            });
            Check(!workspace.UsesNativeTrigger(Dash) && !workspace.UsesNativeTrigger(other), "Batch settings did not reach every ability");
        }
        using (var picker = new UnifiedActionChoiceDialog(workspace, "F10", null))
        {
            picker.Show(); Application.DoEvents(); Select(picker, "IA_Jump");
            Check(!Field<CheckBox>(picker, "nativeAbilityTrigger").Visible, "Ordinary mapping shows an unrelated ability setting");
        }
    }
    static object[] Rows(Control view)
    { return Field<IEnumerable>(view, "actionRows").Cast<object>().ToArray(); }
    static void SelectKey(Control view, string key)
    { view.GetType().GetField("selectedKey", Hidden).SetValue(view, key); Call(view, "RenderSelectedKey"); Application.DoEvents(); }
    static void ClickBinding(Button button)
    {
        Check(button.Visible && button.Enabled && button.Width > 0, "Binding settings command is unavailable");
        button.PerformClick();
        Application.DoEvents();
    }
    static void KeyRows()
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        {
            form.UiOffline(); form.Show(); Application.DoEvents();
            Call(form, "EnableKeyEnhancementEditing"); form.RenderSession();
            var workspace = Field<BindingWorkspace>(form, "currentBindings");
            var view = Field<Control>(form, "keyBindings");
            string move = workspace.Functions.First(item => item.Id.StartsWith("move:", StringComparison.Ordinal)).Id;
            string action = workspace.Functions.First(item => item.Id.StartsWith("action:", StringComparison.Ordinal)).Id;
            workspace.Add(Dash, new BindingEntry { Keys = new[] { "F8" }, KeepNativeTrigger = false });
            workspace.Add(move, new BindingEntry { Keys = new[] { "F8" } });
            workspace.Add(action, new BindingEntry { Keys = new[] { "F8" }, Trigger = "tap" });
            workspace.Add("IA_Jump", new BindingEntry { Keys = new[] { "F8", "K" }, Trigger = "hold", Enabled = false });
            workspace.AddFunctions(new[] { "IA_Guard", "IA_HeavyAttack" }, new BindingEntry { Keys = new[] { "F8" }, Trigger = "press" });
            workspace.Add("IA_Interact", new BindingEntry { Trigger = "sequence", Rule = new KeyEnhancementRule {
                Trigger = "sequence", Steps = new List<string[]> { new[] { "F8" }, new[] { "K" } } } });
            string custom = workspace.AddCustom(new KeyEnhancementOutput { Kind = "property", Property = "bFlag", Value = false },
                new BindingEntry { Trigger = "custom", Rule = new KeyEnhancementRule { Trigger = "custom", Watch = new List<string> { "F8" },
                    EvaluateLua = "function(now,snapshot) return {active=snapshot.F8==1} end" } }, "自定义状态").FunctionId;
            form.RenderSession(); SelectKey(view, "F8");
            Check(Rows(view).Length == 8, "Key view merged distinct bindings");
            foreach (string function in new[] { Dash, move, action, "IA_Jump", "IA_Guard", "IA_HeavyAttack", "IA_Interact", custom })
            {
                var row = Rows(view).Single(item => Field<BindingEntry>(item, "Entry").FunctionId == function);
                ((ScrollableControl)Field<Button>(row, "Settings").Parent.Parent).ScrollControlIntoView(Field<Button>(row, "Settings"));
                OpenEditor(delegate { ClickBinding(Field<Button>(row, "Settings")); }, delegate(UnifiedBindingEditDialog editor) {
                    Check(Field<BindingEntry>(editor, "current").FunctionId == function, "Settings opened a different binding");
                    Check(Field<Label>(editor, "scopeHint").Text == I18n.Str("schemes.scope"), "Enhanced binding editor shows a different storage scope");
                    if (function == Dash)
                    {
                        var keep = Field<CheckBox>(editor, "nativeAbilityTrigger");
                        Check(keep.Visible && !keep.Checked, "Existing key binding lost its saved policy"); keep.Checked = true;
                        Shot(editor, "existing-key-binding-settings");
                    }
                    Field<NumericUpDown>(editor, "seconds").Value = .8m;
                    Click(editor, "saveBinding");
                });
                Check(Math.Abs(workspace.Entries(function).Single(item => !item.IsNative).Seconds - .8) < .001,
                    "Key settings did not update the selected binding");
                SelectKey(view, "F8");
            }
            Check(workspace.UsesNativeTrigger(Dash), "Editing a key binding did not change its original-trigger policy");
            using (var editor = new UnifiedBindingEditDialog(workspace, "scheme:next", null, "F10", null))
            {
                editor.Show(); Application.DoEvents();
                Check(Field<Label>(editor, "scopeHint").Text == I18n.Str("binding.scope.global"), "Scheme-switch binding lost its shared scope");
            }
            SelectKey(view, "F8");
            var dashRow = Rows(view).Single(item => Field<BindingEntry>(item, "Entry").FunctionId == Dash);
            OpenEditor(delegate { ClickBinding(Field<Button>(dashRow, "Edit")); }, delegate(UnifiedBindingEditDialog editor) {
                Check(Field<CheckBox>(editor, "nativeAbilityTrigger").Checked, "Clicking the binding card did not reload its settings");
                Click(editor, "cancel");
            });
            foreach (string key in new[] { "LeftMouseButton", "W" })
            {
                SelectKey(view, key);
                var row = Rows(view).First(item => Field<BindingEntry>(item, "Entry").IsNative);
                OpenEditor(delegate { ClickBinding(Field<Button>(row, "Settings")); }, delegate(UnifiedBindingEditDialog editor) {
                    Check(Field<BindingEntry>(editor, "current").IsNative, "Native/fixed binding did not open its own settings"); Click(editor, "cancel");
                });
            }
            SelectKey(view, "F8"); Shot(form, "key-view-settings");
            form.Size = form.MinimumSize; Application.DoEvents();
            foreach (var row in Rows(view))
            {
                var card = Field<Button>(row, "Edit"); var settings = Field<Button>(row, "Settings"); var operation = Field<Button>(row, "Operation");
                Check(card.Width > 60 && !card.Bounds.IntersectsWith(settings.Bounds) && !settings.Bounds.IntersectsWith(operation.Bounds), "Key settings overlap the binding card at minimum size");
            }
            Shot(form, "key-view-settings-minimum");
            form.GetType().GetField("gamepad", Hidden).SetValue(form, true); form.RenderSession();
            workspace = Field<BindingWorkspace>(form, "currentBindings");
            workspace.Add("IA_Jump", new BindingEntry { Axis = "Gamepad_LeftTriggerAxis", Trigger = "axis" });
            form.RenderSession(); SelectKey(view, "Gamepad_LeftTrigger");
            var axisRow = Rows(view).Single(item => Field<BindingEntry>(item, "Entry").Axis == "Gamepad_LeftTriggerAxis");
            OpenEditor(delegate { ClickBinding(Field<Button>(axisRow, "Settings")); }, delegate(UnifiedBindingEditDialog editor) {
                Check(Field<BindingEntry>(editor, "current").Axis == "Gamepad_LeftTriggerAxis", "Controller axis settings opened another binding");
                Field<NumericUpDown>(editor, "threshold").Value = .7m; Click(editor, "saveBinding");
            });
            Check(Math.Abs(workspace.Entries("IA_Jump").Single(item => item.Axis == "Gamepad_LeftTriggerAxis").Threshold - .7) < .001,
                "Controller axis settings were not saved");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args.FirstOrDefault() ?? "dist/key-view-settings-check"); Directory.CreateDirectory(output);
            foreach (bool enabled in new[] { false, true }) foreach (bool pad in new[] { false, true }) Picker(enabled, pad);
            MixedSelection(); KeyRows();
            Console.WriteLine("KeyViewBindingSettingsTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
