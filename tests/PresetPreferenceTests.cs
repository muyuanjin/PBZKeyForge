using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class PresetPreferenceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Choose(Control selector, PresetApplyMode mode)
    { ((Button)selector.Controls.Find("mode" + mode, false).Single()).PerformClick(); Application.DoEvents(); }

    static object Call(object owner, string name, params object[] args)
    { try { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (Control nested in All(child)) yield return nested; } }
    static Button Button(Control owner, string name)
    { return All(owner).OfType<Button>().Single(button => button.Name == name); }
    sealed class Step
    {
        public string Type; public Action<Form> Inspect;
        public Step(string type, Action<Form> inspect) { Type = type; Inspect = inspect; }
    }
    static void Modals(Action open, params Step[] steps)
    {
        int next = 0; Exception failure = null; var watch = Stopwatch.StartNew();
        using (var timer = new Timer { Interval = 50 })
        {
            timer.Tick += delegate {
                var dialog = Application.OpenForms.Cast<Form>().LastOrDefault(form => form.GetType().Name != "MainForm");
                if (watch.ElapsedMilliseconds > 10000)
                {
                    failure = new Exception("Modal flow timed out at " + next); timer.Stop();
                    if (dialog != null) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                    return;
                }
                if (next >= steps.Length || dialog == null || dialog.GetType().Name != steps[next].Type) return;
                timer.Stop();
                try { steps[next++].Inspect(dialog); }
                catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                if (failure == null && next < steps.Length) timer.Start();
            };
            timer.Start(); open(); timer.Stop();
        }
        if (failure != null) throw failure;
        Assert(next == steps.Length, "All expected dialogs were shown");
    }
    static void Import(UserPresetDialog dialog, Preset preset)
    {
        Field<TextBox>(dialog, "shareBox").Text = UserPresetStore.Share(preset);
        var watch = Stopwatch.StartNew();
        while (!Button(dialog, "usePreset").Enabled && watch.ElapsedMilliseconds < 3000)
        { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        Assert(Button(dialog, "usePreset").Enabled, "Share is ready to load");
    }
    static void ManagerAndLoading(string output, EditorPreferences preferences, EditorSession session, UserPresetStore store)
    {
        var savedMode = preferences.LoadPresetMode();
        using (var dialog = new UserPresetDialog(store))
        {
            dialog.Show(); Application.DoEvents();
            Field<CheckedListBox>(dialog, "list").SelectedIndex = 0; Application.DoEvents();
            Assert(!All(dialog).Any(control => control is PresetModeSelector), "Library browsing has no application mode");
            dialog.Close();
            Assert(dialog.AppliedPreset == null && preferences.LoadPresetMode() == savedMode, "Browsing does not load or change the application preference");
        }
        var source = EditorSession.CreateOffline(); source.SetKeyboard("IA_Jump", 0, "K");
        var preset = UserPresetStore.Capture(source, "跳跃K");
        using (var form = new MainForm(preferences))
        {
            form.RenderSession();
            Assert(form.Session == null && Field<Button>(form, "btnPresetManager").Enabled, "Library is available with no open save");
            Modals(() => Call(form, "ManagePresets"), new Step("UserPresetDialog", dialog => {
                Assert(form.Session == null, "Opening the library does not create an editing session"); dialog.Close();
            }));
            Assert(form.Session == null, "Closing the library keeps the empty editor unchanged");
            Modals(() => Call(form, "ManagePresets"),
                new Step("UserPresetDialog", dialog => {
                    Assert(form.Session == null, "Importing starts without a session"); Import((UserPresetDialog)dialog, preset);
                    Assert(form.Session == null, "Import preview does not create a session"); Button(dialog, "usePreset").PerformClick();
                }),
                new Step("PresetLoadDialog", dialog => {
                    Assert(form.Session != null && form.Session.IsOffline && !form.Session.HasAnyDrafts, "Selecting load creates an offline target before confirmation");
                    ((Button)dialog.CancelButton).PerformClick();
                }));
            Assert(form.Session.IsOffline && !form.Session.HasAnyDrafts, "Cancelling load preserves the new offline defaults");
            typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form, new object[] { session });
            session.ResetAll(); form.RenderSession();
            Assert(form.Session.IsResetPending && Field<Button>(form, "btnPresetManager").Enabled, "Library remains available during a pending global reset");
            Modals(() => Call(form, "ManagePresets"), new Step("UserPresetDialog", dialog => dialog.Close()));
            Assert(form.Session.IsResetPending, "Browsing does not discard the pending reset");
            session.Undo();
        }
        using (var form = new MainForm(preferences))
        {
            typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form, new object[] { session });
            string before = Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson();
            Modals(() => Call(form, "ConfirmPresetLoad", preset), new Step("PresetLoadDialog", dialog => {
                Assert(All(dialog).OfType<DataGridView>().Single(grid => grid.Name == "loadPresetChanges").Rows.Count > 0, "Load confirmation shows current-to-result changes");
                Choose(All(dialog).Single(control => control.Name == "loadPresetMode"), PresetApplyMode.Append);
                Assert(!session.HasAnyDrafts && Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson() == before, "Changing application mode only refreshes the preview");
                Assert(preferences.LoadPresetMode() == savedMode, "Unconfirmed mode is not persisted");
                ((Button)dialog.CancelButton).PerformClick();
            }));
            Assert(!session.HasAnyDrafts && Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson() == before && preferences.LoadPresetMode() == savedMode,
                "Cancelling load changes neither bindings, configuration nor preference");
            Modals(() => Call(form, "ConfirmPresetLoad", preset), new Step("PresetLoadDialog", dialog => {
                Choose(All(dialog).Single(control => control.Name == "loadPresetMode"), PresetApplyMode.Replace);
                Assert(!session.HasAnyDrafts, "Review leaves the session unchanged until confirmation");
                using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, dialog.ClientRectangle); image.Save(Path.Combine(output, "preset-load-confirmation.png")); }
                Button(dialog, "loadPresetConfirm").PerformClick();
            }));
            Assert(session.Relations("IA_Jump", false, -1).Keys.Contains("K") && preferences.LoadPresetMode() == PresetApplyMode.Replace,
                "Confirmation stages the preset and persists its application mode");
            session.SelectPreset(null);
            Modals(() => Call(form, "SaveSchemePreset"), new Step("SchemeNameDialog", dialog => {
                var sourceLabel = All(dialog).Single(control => control.Name == "savePresetSource");
                Assert(sourceLabel.Visible && dialog.PointToClient(sourceLabel.PointToScreen(Point.Empty)).Y >= ((EditorWindow)dialog).captionClose.Parent.Bottom, "The actual save flow displays its source below the title bar");
                using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, dialog.ClientRectangle); image.Save(Path.Combine(output, "preset-save-current-scheme.png")); }
                ((Button)dialog.CancelButton).PerformClick();
            }));
        }
        var type = typeof(MainForm).Assembly.GetType("PBZKeyForge.SchemeNameDialog");
        using (var dialog = (Form)Activator.CreateInstance(type, new object[] { I18n.Str("schemes.savepreset"), "", new string[0], true, "当前方案" }))
        {
            Modals(() => dialog.ShowDialog(), new Step("SchemeNameDialog", shown => {
            Assert(All(dialog).Single(control => control.Name == "savePresetSource").Text.Contains("当前方案"), "Saving identifies the source editing scheme");
            Assert(!All(dialog).OfType<TextBox>().Any(), "Saving the current scheme does not require a share code");
            ((Button)dialog.AcceptButton).PerformClick(); Assert(dialog.Visible, "Saving requires a preset name");
            Field<ComboBox>(dialog, "name").Text = "直接保存"; Field<CheckBox>(dialog, "gamepad").Checked = false;
            using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, dialog.ClientRectangle); image.Save(Path.Combine(output, "preset-save-source.png")); }
            ((Button)dialog.AcceptButton).PerformClick();
            }));
            Assert(dialog.DialogResult == DialogResult.OK && (string)type.GetProperty("Value").GetValue(dialog, null) == "直接保存", "Name and device selection confirm a direct save");
            var captured = UserPresetStore.SelectScopes(UserPresetStore.Capture(session, "直接保存"), new[] { new PresetScope() });
            store.Save(captured, "直接保存");
            Assert(store.Load().Any(item => item.Name == "直接保存" && item.Scopes.All(scope => scope.Family == null)), "The selected scheme can be saved without importing a share");
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            var preferences = new EditorPreferences(Path.Combine(output, Guid.NewGuid().ToString("N"), "preset-mode.txt"));
            Assert(preferences.LoadPresetMode() == PresetApplyMode.OnlyPreset, "First launch defaults to OnlyPreset");
            byte[] original = File.ReadAllBytes(args[0]);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, original);
            foreach (var mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
            {
                using (var form = new MainForm(preferences))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.UiSmoke(input); form.Show(); Application.DoEvents();
                    var selector = Field<Control>(form, "btnPresetMode");
                    Assert(Field<PresetApplyMode>(form, "presetMode") == preferences.LoadPresetMode(), "Window restores stored selection");
                    Choose(selector, mode);
                    Assert(preferences.LoadPresetMode() == mode, "Selection persists immediately: " + mode);
                    using (var image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, form.ClientRectangle); image.Save(Path.Combine(output, "mode-" + mode + ".png")); }
                    form.Size = form.MinimumSize; Application.DoEvents();
                    var picker = Field<ComboBox>(form, "cbPreset");
                    Assert(selector.Height <= picker.Height * .75 && selector.Font.SizeInPoints < picker.Font.SizeInPoints,
                        "Application modes are visibly shorter and smaller than preset selection");
                    Assert(selector.Width < picker.Width * .65, "Auxiliary modes keep a compact content width");
                    foreach (Button option in selector.Controls)
                        Assert(option.Visible && TextRenderer.MeasureText(option.Text, option.Font).Width < option.Width, "Compact choices keep all mode names readable");
                    using (var image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, form.ClientRectangle); image.Save(Path.Combine(output, "mode-minimum-" + mode + ".png")); }
                }
                using (var reopened = new MainForm(preferences))
                    Assert(Field<PresetApplyMode>(reopened, "presetMode") == mode, "New window remembers " + mode);
            }
            var session = EditorSession.Open(input);
            var store = new UserPresetStore(Path.Combine(output, Guid.NewGuid().ToString("N")));
            store.Save(UserPresetStore.Capture(session, "测试预设"), "测试预设");
            ManagerAndLoading(output, preferences, session, store);
            Assert(original.SequenceEqual(File.ReadAllBytes(input)) && !session.HasAnyDrafts, "Preference changes never edit bindings or save");
            foreach (var mode in new[] { PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
            {
                session.StagePreset(Presets.ById("mouse_guard_space"), mode);
                string exported = Path.Combine(output, "motion-" + mode + ".sav");
                File.WriteAllBytes(exported, session.ExportCurrentScheme());
                var reopened = EditorSession.Open(exported); reopened.SwitchProfile(session.SelectedProfile);
                foreach (var keys in new[] {
                    new[] { "IA_MoveFast", "W", "A", "D" }, new[] { "IA_Dodge", "LeftShift", "SpaceBar" },
                    new[] { "IA_Jump", "SpaceBar" }, new[] { "IA_LightAttack", "LeftMouseButton" },
                    new[] { "IA_HeavyAttack", "F", "RightMouseButton" }, new[] { "IA_Guard", "LeftMouseButton", "RightMouseButton" } })
                {
                    Assert(reopened.Relations(keys[0], false, -1).Keys.OrderBy(key => key).SequenceEqual(keys.Skip(1).OrderBy(key => key)),
                        "Exported motion preset: " + mode + " " + keys[0]);
                }
                session.SelectPreset(null);
            }
            Console.WriteLine("PresetPreferenceTests PASS: " + checks + " checks; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
