using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class PresetPreferenceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Choose(Control selector, PresetApplyMode mode)
    { ((Button)selector.Controls.Find("mode" + mode, false).Single()).PerformClick(); Application.DoEvents(); }

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
            using (var dialog = new UserPresetDialog(session, store, preferences.LoadPresetMode(), preferences))
            {
                dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
                Field<CheckedListBox>(dialog, "list").SetItemChecked(0, true); Application.DoEvents();
                Choose(Field<Control>(dialog, "modeButton"), PresetApplyMode.Replace);
                Assert(dialog.ChosenMode == PresetApplyMode.Replace && preferences.LoadPresetMode() == PresetApplyMode.Replace,
                    "Manager selection persists without applying a preset");
            }
            using (var reopened = new MainForm(preferences))
                Assert(Field<PresetApplyMode>(reopened, "presetMode") == PresetApplyMode.Replace, "Manager and main window share preference");
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
