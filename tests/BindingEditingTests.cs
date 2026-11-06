using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

class BindingEditingTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr key, IntPtr data);
    static int checks;
    static string output;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static IEnumerable<Control> All(Control owner) { foreach (Control c in owner.Controls) { yield return c; foreach (Control child in All(c)) yield return child; } }
    static Button Button(Control owner, string name) { return All(owner).OfType<Button>().First(c => c.Name == name); }
    static BindingEntry Spec(string key, string trigger) { return new BindingEntry { Keys = new[] { key }, Trigger = trigger }; }
    static void Shot(Form form, string name)
    {
        form.Refresh(); Application.DoEvents();
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, name + ".png")); }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args.Length == 0 ? "dist/binding-editing-check" : args[0]); Directory.CreateDirectory(output);
            Recovery(); CaptureKeys(); ConfigureFromKey(); KeyRows(); Differences(); PresetsAndReset();
            Console.WriteLine("BindingEditingTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Recovery()
    {
        var session = EditorSession.CreateOffline();
        for (int i = 0; i < 3; i++) session.SetKeyboard("IA_Jump", i, new[] { "J", "K", "L" }[i]);
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1);
        ws.Add("ability:Ability.Action.Hun.SpecialDash", Spec("SpaceBar", "press"));
        config.Enabled = false;
        string before = config.ToJson(); byte[] save = session.ExportCurrentScheme(); bool rejected = false;
        try { ws.RestoreKey("SpaceBar"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Ordinary mode must reject restoring Space into full native slots");
        Check(config.ToJson() == before && save.SequenceEqual(session.ExportCurrentScheme()), "Failed restoration changed rules or native bindings");
        config.Enabled = true; ws.RestoreKey("SpaceBar");
        Check(config.Rules.Count == 1 && config.Rules[0].Outputs[0].MappingName == "IA_Jump", "Enhanced restoration failed to replace extra functions with the factory output");
        session.SetKeyboard("IA_Jump", 2, "None"); ws.RestoreKey("SpaceBar");
        Check(config.Rules.Count == 0 && session.Relations("IA_Jump", false, -1).Keys.Contains("SpaceBar"), "Successful retry did not restore Space and remove its extra rule");
        var entry = ws.Add("IA_Jump", Spec("O", "hold")); ws.SetEnabled(entry, false);
        entry = ws.Entries("IA_Jump").Single(e => e.RuleId == entry.RuleId); ws.Restore(entry);
        Check(config.Rules.Single().Enabled && config.Rules.Single().Trigger == "hold", "Re-enabling changed the rule trigger");
        Check(!session.Relations("IA_Jump", false, -1).Keys.Contains("O"), "Re-enabling introduced a native plain-key binding");
        var native = ws.Add("IA_Guard", Spec("H", "default"));
        ws.Remove(native); ws.Restore(native);
        Check(session.Relations("IA_Guard", false, -1).Keys.Contains("H"), "Restoring a removed custom native key lost its original key");
        var suppression = new KeyEnhancementSuppression { ActionPath = ws.Functions.Single(f => f.Id == "IA_Guard").Targets.First().Path, Key = "H" };
        config.Suppressions.Add(suppression);
        ws.Restore(ws.Entries("IA_Guard").Single(e => e.IsNative && e.Keys.Contains("H")));
        Check(session.Relations("IA_Guard", false, -1).Keys.Contains("H") && !config.Suppressions.Contains(suppression), "Restoring a suppressed native key removed its saved binding");
    }
    static void Message(Control c, int message, Keys key, int data) { SendMessage(c.Handle, message, (IntPtr)(int)key, (IntPtr)data); Application.DoEvents(); }
    static void CaptureKeys()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        for (int i = 0; i < 10; i++)
            using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
            {
                dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
                Message(capture, 0x100, (Keys)((int)Keys.NumPad0 + i), 0); Message(capture, 0x101, (Keys)((int)Keys.NumPad0 + i), 0);
                Button(dialog, "saveBinding").PerformClick();
                string expected = "NumPad" + new[] { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" }[i];
                Check(dialog.Result.Keys.SequenceEqual(new[] { expected }), "Invalid UE keypad identity: " + i);
            }
        Keys[] modifiers = { Keys.ControlKey, Keys.Menu, Keys.ShiftKey };
        string[] identities = { "RightControl", "RightAlt", "RightShift" };
        for (int i = 0; i < modifiers.Length; i++)
            using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
            {
                dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
                int data = i == 2 ? 0x00360000 : 0x011d0000;
                Message(capture, i == 1 ? 0x104 : 0x100, modifiers[i], data); Message(capture, 0x100, Keys.J, 0);
                Message(capture, 0x101, Keys.J, 0); Message(capture, i == 1 ? 0x105 : 0x101, modifiers[i], data);
                Button(dialog, "saveBinding").PerformClick();
                Check(dialog.Result.Keys.Length == 2 && dialog.Result.Keys.Contains(identities[i]) && dialog.Result.Keys.Contains("J"), "Lost right modifier identity: " + identities[i]);
            }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
            Message(capture, 0x100, Keys.J, 0); Message(capture, 0x100, Keys.K, 0);
            Message(capture, 0x101, Keys.J, 0); Message(capture, 0x101, Keys.K, 0);
            Button(dialog, "saveBinding").PerformClick(); Check(dialog.Result.Keys.Length == 2 && dialog.Result.Keys.Contains("J") && dialog.Result.Keys.Contains("K"), "Ordinary chord recording lost a key");
        }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
            Message(capture, 0x100, Keys.J, 0); Message(capture, 0x101, Keys.J, 0); Message(capture, 0x100, Keys.K, 0); Message(capture, 0x101, Keys.K, 0);
            Button(dialog, "saveBinding").PerformClick(); Check(dialog.Result.Keys.SequenceEqual(new[] { "K" }), "Separate recordings accumulated a stale key");
        }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
            Message(capture, 0x100, Keys.J, 0); Message(capture, 0x100, Keys.K, 0); Message(capture, 0x101, Keys.J, 0); Message(capture, 0x100, Keys.L, 0);
            Message(capture, 0x101, Keys.K, 0); Message(capture, 0x101, Keys.L, 0);
            Button(dialog, "saveBinding").PerformClick();
            Check(dialog.Result.Keys.Length == 2 && dialog.Result.Keys.Contains("K") && dialog.Result.Keys.Contains("L"), "Chord recording retained a released key");
        }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
            Message(capture, 0x201, Keys.None, 0); Message(capture, 0x202, Keys.None, 0);
            Message(capture, 0x100, Keys.K, 0); Message(capture, 0x101, Keys.K, 0);
            Button(dialog, "saveBinding").PerformClick();
            Check(dialog.Result.Keys.SequenceEqual(new[] { "K" }), "Chord recording retained a released mouse button");
        }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); var capture = Field<Control>(dialog, "capture"); capture.Focus();
            Message(capture, 0x201, Keys.None, 0); Message(capture, 0x204, Keys.None, 0); Message(capture, 0x205, Keys.None, 0);
            Message(capture, 0x100, Keys.K, 0); Message(capture, 0x101, Keys.K, 0); Message(capture, 0x202, Keys.None, 0);
            Button(dialog, "saveBinding").PerformClick();
            Check(dialog.Result.Keys.Length == 2 && dialog.Result.Keys.Contains("K") && dialog.Result.Keys.Contains("LeftMouseButton"), "Releasing one mouse button cleared another held button");
        }
        using (var dialog = new UnifiedBindingEditDialog(ws, "IA_Jump", ws.Entries("IA_Jump").First(), null, null))
        {
            dialog.Show(); Application.DoEvents(); Check(Button(dialog, "removeBinding").Visible, "Binding editor hides unbinding");
            Shot(dialog, "binding-editor"); Field<ComboBox>(dialog, "trigger").SelectedIndex = 8; Shot(dialog, "sequence-editor");
            Button(dialog, "removeBinding").PerformClick(); Check(dialog.RemoveRequested, "Unbind did not return an explicit operation");
        }
        const string dash = "ability:Ability.Action.Hun.SpecialDash";
        using (var dialog = new UnifiedBindingEditDialog(ws, dash, null, "J", null))
        {
            dialog.Show(); Application.DoEvents(); Field<CheckBox>(dialog, "nativeAbilityTrigger").Checked = false;
            Button(dialog, "saveBinding").PerformClick(); ws.Add(dash, dialog.Result);
            Check(!ws.UsesNativeTrigger(dash), "Replacing original controls left the ability's native combination active");
            Check(ws.Entries(dash).Length == 1 && ws.Session.Relations("IA_Dodge", false, -1).Keys.Length > 0, "Replacing dash removed standalone dodge");
        }
    }
    static void ConfigureFromKey()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        using (var picker = new UnifiedActionChoiceDialog(ws, "J", null))
        using (var timer = new Timer { Interval = 40 })
        {
            Exception failure = null; bool opened = false; var started = DateTime.UtcNow;
            timer.Tick += delegate {
                var editor = Application.OpenForms.OfType<UnifiedBindingEditDialog>().FirstOrDefault();
                if (editor == null)
                {
                    if ((DateTime.UtcNow - started).TotalSeconds > 5) { timer.Stop(); failure = new Exception("Key picker did not open the binding editor"); picker.Close(); }
                    return;
                }
                timer.Stop(); opened = true;
                try { Field<ComboBox>(editor, "trigger").SelectedIndex = 4; Field<NumericUpDown>(editor, "seconds").Value = .7m; Button(editor, "saveBinding").PerformClick(); }
                catch (Exception ex) { failure = ex; editor.Close(); }
            };
            picker.Show(); Application.DoEvents(); Field<TextBox>(picker, "search").Text = "IA_Jump";
            timer.Start(); Button(picker, "configureBinding").PerformClick(); timer.Stop();
            if (failure != null) throw failure;
            Check(opened && config.Rules.Single().Keys.SequenceEqual(new[] { "J" }) && config.Rules.Single().Trigger == "hold" && config.Rules.Single().Seconds == .7,
                "Key picker lost the configured complex binding");
        }
    }
    static void KeyRows()
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        {
            form.UiOffline(); form.Show(); Application.DoEvents();
            var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement"); config.Enabled = true;
            var ws = new BindingWorkspace(form.Session, config, config.Clone(), false, -1);
            var chord = Spec("J", "hold"); chord.Keys = new[] { "J", "K" }; ws.Add("IA_Jump", chord); ws.Add("IA_Jump", Spec("J", "tap"));
            form.RenderSession(); var view = Field<Control>(form, "keyBindings"); view.GetType().GetField("selectedKey", Hidden).SetValue(view, "J"); Call(view, "RenderSelectedKey");
            var rows = Field<System.Collections.IEnumerable>(view, "actionRows").Cast<object>().ToArray();
            Check(rows.Length == 2, "Two rules were merged into one removable key row"); Shot(form, "individual-key-bindings");
            var target = rows.Single(row => Field<BindingEntry>(row, "Entry").Trigger == "tap"); Field<Button>(target, "Operation").PerformClick();
            Check(config.Rules.Count == 1 && config.Rules.Single().Trigger == "hold", "Unbinding tap deleted the chord rule");
            ws.SetEnabled(ws.Entries("IA_Jump").Single(e => !e.IsNative), false); form.RenderSession(); Call(view, "RenderSelectedKey");
            rows = Field<System.Collections.IEnumerable>(view, "actionRows").Cast<object>().ToArray();
            Check(rows.Length == 1 && !Field<BindingEntry>(rows[0], "Entry").Enabled, "Disabled rule disappeared");
            Shot(form, "disabled-key-binding"); Field<Button>(rows[0], "Operation").PerformClick();
            Check(config.Rules.Single().Enabled && config.Rules.Single().Trigger == "hold", "Key-row enable changed the trigger");
        }
    }
    static void Differences()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true }; var session = EditorSession.CreateOffline();
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1); ws.Add("IA_Jump", Spec("J", "hold"));
        var baseline = config.Clone(); config.Rules.Single().Seconds = .8;
        ws = new BindingWorkspace(session, config, baseline, false, -1);
        var snapshot = Call(ws, "Snapshot"); var differences = (BindingDifference[])Call(snapshot, "Differences", false);
        Check(differences.Any(d => d.DetailsChanged && d.AfterBindings.Any(text => text.Contains("0.8"))), "Timing change missing from the summary");
        Check(differences.Single().BeforeBindings.Any(text => text.Contains("0.25")), "Summary lost original hold duration");
    }
    static void PresetsAndReset()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1); ws.Add("IA_Jump", Spec("J", "hold"));
        var preset = UserPresetStore.Capture(session, "完整配置", config); var store = new UserPresetStore(Path.Combine(output, Guid.NewGuid().ToString("N")));
        store.Save(preset, preset.Name); var loaded = store.Load().Single();
        Check(loaded.KeyEnhancement.ToJson() == config.ToJson(), "Saved preset lost enhancement settings");
        var shared = UserPresetStore.ParseShare(UserPresetStore.Share(loaded));
        Check(shared.KeyEnhancement.ToJson() == config.ToJson(), "Share roundtrip lost enhancement settings");
        Type hoverType = typeof(MainForm).Assembly.GetType("PBZKeyForge.PresetHoverPreview+PreviewWindow");
        using (var hover = (Control)Activator.CreateInstance(hoverType, Hidden, null,
            new object[] { new Font("Microsoft YaHei UI", 10f), loaded, session.PreviewPreset(loaded), false, -1, Screen.PrimaryScreen.WorkingArea }, null))
        {
            hover.CreateControl(); hover.PerformLayout();
            Check(All(hover).OfType<Label>().Any(label => label.Text.Contains("0.25")) && All(hover).OfType<Label>().Any(label => label.Text.Contains(I18n.Str("binding.preset")))
                && !All(hover).OfType<Label>().Any(label => label.Text == I18n.Str("views.nofactorydiff")), "Quick preset hover omits enhancement rules or their scope");
        }
        using (var dialog = new UserPresetDialog(session, store, PresetApplyMode.Replace))
        {
            dialog.Show(); Application.DoEvents(); Call(dialog, "Display", loaded); Application.DoEvents();
            var preview = Field<Panel>(dialog, "preview");
            Check(All(preview).OfType<Label>().Any(label => label.Text.Contains("0.25"))
                && !All(preview).OfType<Label>().Any(label => label.Text == I18n.Str("views.nofactorydiff")), "Enhanced preset preview hides its rules or claims factory equivalence");
            Shot(dialog, "enhanced-preset-preview");
        }
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preset-preferences.txt"))))
        {
            form.UiOffline(); form.Show(); Application.DoEvents(); var picker = Field<ComboBox>(form, "cbPreset"); picker.Items.Add(loaded); picker.SelectedItem = loaded; Application.DoEvents();
            Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson() == config.ToJson(), "Selecting an enhanced preset lost its rules");
            picker.SelectedIndex = 0; Application.DoEvents();
            Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Count == 0 && !Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Enabled,
                "Cancelling a preset retained its enhancement configuration");
            picker.SelectedItem = loaded; Application.DoEvents();
            picker.SelectedItem = picker.Items.OfType<Preset>().First(p => p.KeyEnhancement == null); Application.DoEvents();
            Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Count == 0, "Switching to an ordinary preset retained the previous preview's rules");
            picker.SelectedItem = loaded; Application.DoEvents();
            Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Single().Seconds = .9;
            picker.SelectedIndex = 0; Application.DoEvents();
            Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Single().Seconds == .9, "Cancelling a preset erased a manual enhancement edit");
            Call(form, "ResetAllBindings"); form.RenderSession(); var reset = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
            Check(!reset.Enabled && reset.Rules.Count == 0 && reset.Suppressions.Count == 0 && reset.Hooks.Count == 0, "Reset all left enhancement settings active");
            Shot(form, "reset-all");
        }
    }
}
