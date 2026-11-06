using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class UnifiedBindingFlowTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] arguments) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, arguments); }
    static IEnumerable<Control> All(Control owner)
    {
        foreach (Control control in owner.Controls) { yield return control; foreach (var child in All(control)) yield return child; }
    }
    static Button Button(Form dialog, string name) { return All(dialog).OfType<Button>().Single(item => item.Name == name); }
    static KeyEnhancementConfiguration Config(MainForm form) { return Field<KeyEnhancementConfiguration>(form, "keyEnhancement"); }
    static BindingWorkspace Workspace(MainForm form)
    { return new BindingWorkspace(form.Session, Config(form), Field<KeyEnhancementConfiguration>(form, "keyEnhancementBaseline"), false, -1); }
    static CheckBox Toggle(MainForm form) { return Field<CheckBox>(Field<Control>(form, "keyEnhancementBar"), "Toggle"); }
    static void Switch(MainForm form) { SendMessage(Toggle(form).Handle, 0xf5, IntPtr.Zero, IntPtr.Zero); Application.DoEvents(); }
    static void Shot(Form form, string name)
    {
        form.Refresh(); Application.DoEvents();
        using (var image = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, name + ".png")); }
    }
    static void Modal(Action trigger, string typeName, Action<Form> respond)
    {
        bool found = false;
        Exception failure = null;
        var watch = Stopwatch.StartNew();
        using (var timer = new System.Windows.Forms.Timer { Interval = 40 })
        {
            timer.Tick += delegate
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.GetType().Name == typeName);
                var oldEditor = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.GetType().Name == "KeyEnhancementRuleEditor");
                if (oldEditor != null)
                {
                    timer.Stop(); failure = new Exception("Ordinary binding editing opened a separate rule editor."); oldEditor.Close(); return;
                }
                if (dialog == null)
                {
                    if (watch.ElapsedMilliseconds > 10000)
                    {
                        timer.Stop(); failure = new Exception("The ordinary binding flow did not open " + typeName);
                        foreach (var open in Application.OpenForms.Cast<Form>().Where(form => !(form is MainForm)).ToArray()) open.Close();
                    }
                    return;
                }
                timer.Stop(); found = true;
                try { respond(dialog); }
                catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
            };
            timer.Start(); trigger(); timer.Stop();
        }
        if (failure != null) throw failure;
        Check(found, "The expected ordinary dialog was not shown: " + typeName);
    }
    static void SelectFunction(Form dialog, string id)
    {
        Field<TextBox>(dialog, "search").Text = id;
        var list = Field<ListBox>(dialog, "functions");
        var function = list.Items.Cast<BindingFunction>().Single(item => item.Id == id);
        list.SelectedItem = function;
        Check(list.SelectedItem == function, "The chosen function was not retained.");
    }
    static void AddFromKey(MainForm form, string key, string functionId, bool enableInside)
    {
        var view = Field<Control>(form, "keyBindings");
        view.GetType().GetField("selectedKey", Hidden).SetValue(view, key);
        Call(view, "RenderSelectedKey"); Application.DoEvents();
        Modal(delegate { Field<Button>(view, "add").PerformClick(); }, "UnifiedActionChoiceDialog", delegate(Form dialog)
        {
            SelectFunction(dialog, functionId);
            Button(dialog, "addSelectedAction").PerformClick();
            if (enableInside)
                Check(!Config(form).Enabled && Config(form).Rules.Any(rule => rule.Keys.Contains(key)), "Inactive editing enabled execution or lost the binding");
        });
    }
    static void EditFromAction(MainForm form, string functionId, string key, string trigger)
    {
        Modal(delegate { Call(form, "ShowActionRelations", functionId); }, "UnifiedActionBindingsDialog", delegate(Form details)
        {
            Check(All(details).OfType<Button>().Any(button => button.Name == "addBinding"), "Action details do not provide the ordinary add operation.");
            Modal(delegate { Button(details, "addBinding").PerformClick(); }, "UnifiedBindingEditDialog", delegate(Form editor)
            {
                var keys = Field<ComboBox>(editor, "mainKey");
                keys.SelectedIndex = Enumerable.Range(0, keys.Items.Count).First(index => keys.Items[index].ToString() == BindingButton.FriendlyKey(key)
                    || keys.Items[index].ToString() == key || ((object)keys.Items[index]).GetType().GetField("Key") != null && (string)keys.Items[index].GetType().GetField("Key").GetValue(keys.Items[index]) == key);
                var triggers = Field<ComboBox>(editor, "trigger");
                triggers.SelectedIndex = Array.IndexOf(new[] { "default", "press", "release", "tap", "hold", "multi_tap", "pulse", "toggle", "sequence", "axis" }, trigger);
                Shot(editor, "binding-trigger-edit"); Button(editor, "saveBinding").PerformClick();
            });
            Shot(details, "action-all-bindings"); Button(details, "close").PerformClick();
        });
    }
    static void Apply(MainForm form)
    {
        var apply = Field<Button>(form, "btnApply"); Check(apply.Enabled, "The unified binding draft cannot be applied.");
        apply.PerformClick(); var watch = Stopwatch.StartNew();
        while (form.IsApplying && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(1); }
        Check(!form.IsApplying, "Unified binding apply did not finish.");
    }
    static void RestoreFromKeyView(MainForm form)
    {
        form.ShowActionView(false);
        var workspace = Workspace(form);
        var original = workspace.Entries("IA_Guard").Single(entry => entry.IsNative && entry.Keys.Contains("RightMouseButton"));
        var replacement = workspace.Replace(original, new BindingEntry { Keys = new[] { "G" }, Trigger = "tap" });
        form.RenderSession(); Application.DoEvents();
        var view = Field<Control>(form, "keyBindings");
        view.GetType().GetField("selectedKey", Hidden).SetValue(view, "RightMouseButton");
        Call(view, "RenderSelectedKey"); Application.DoEvents();
        var row = Field<System.Collections.IEnumerable>(view, "actionRows").Cast<object>().Single(item =>
            (string)Field<Control>(item, "Edit").GetType().GetField("ActionId", Hidden).GetValue(Field<Control>(item, "Edit")) == "IA_Guard");
        var restore = Field<Button>(row, "Operation");
        Check(restore.Enabled && restore.Text == I18n.Str("views.restore"), "The suppressed right-mouse guard relationship lacks an enabled restore button.");
        Shot(form, "guard-before-restore"); restore.PerformClick(); Application.DoEvents();
        var restored = Workspace(form).Entries("IA_Guard").Single(entry => entry.IsNative && entry.Keys.Contains("RightMouseButton"));
        Check(restored.Active && !restored.Suppressed, "The key-view restore button left the right-mouse guard relationship suppressed.");
        Check(Config(form).Rules.Any(rule => rule.Id == replacement.RuleId && rule.Keys.SequenceEqual(new[] { "G" }) && rule.Trigger == "tap"),
            "Restoring right-mouse guard deleted its G tap binding.");
        Shot(form, "guard-after-restore"); Workspace(form).Remove(replacement); form.RenderSession();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            bool editOnly = args.Contains("--edit-only");
            Application.EnableVisualStyles();
            if (!editOnly) Check(!EditorSession.IsGameRunning, "Close the game before fixture installation tests.");
            output = Path.GetFullPath(args[1]); if (Directory.Exists(output)) throw new Exception("Use a new output directory.");
            string root = Path.Combine(output, "游戏 fixture"), runtime = Path.Combine(root, @"platform\steam");
            Directory.CreateDirectory(runtime); File.WriteAllText(Path.Combine(runtime, "PBZ.exe"), "fixture, never launched");
            string save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(save)); File.Copy(args[0], save);
            string old = Path.Combine(runtime, @"ue4ss\Mods\PBZInputRouter\Scripts"); Directory.CreateDirectory(old);
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.legacy.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry("Scripts/main.lua");
                using (var input = entry.Open()) using (var file = File.Create(Path.Combine(old, "main.lua"))) input.CopyTo(file);
            }
            Exception failure = null;
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                form.ShowInTaskbar = false; form.UiSmoke(save);
                form.Shown += delegate { form.BeginInvoke((Action)delegate {
                    try
                    {
                        I18n.Current = "zh-Hans"; form.RenderSession(); Application.DoEvents();
                        var bar = Field<Control>(form, "keyEnhancementBar");
                        Check(!Config(form).Enabled && !Field<bool>(bar, "Pending") && !Field<Button>(form, "btnApply").Enabled,
                            "A disabled partial prototype created an unsolicited repair draft.");
                        Check(Field<string>(bar, "Status") == I18n.Str("ownership.coverage.off") && Toggle(form).Left < bar.Width / 3,
                            "The initial mode bar is not an ordinary gray explanation with its switch on the left.");
                        Shot(form, "mode-off-partial-prototype");
                        var workspace = Workspace(form);
                        string[] nativeKeys = { "F2", "F3", "F4", "F5", "F6", "F7" };
                        foreach (string key in nativeKeys)
                        {
                            if (!workspace.CanAddNative("IA_Jump")) break;
                            workspace.Add("IA_Jump", new BindingEntry { Keys = new[] { key }, Trigger = "default" });
                        }
                        Check(!workspace.CanAddNative("IA_Jump"), "The native-slot fixture did not fill its supported capacity.");
                        form.RenderSession(); Switch(form);
                        int before = workspace.Entries("IA_Jump").Length;
                        AddFromKey(form, "F9", "IA_Jump", false);
                        Check(Workspace(form).Entries("IA_Jump").Length == before + 1 && Config(form).Rules.Any(rule => rule.Outputs.Any(item => item.Kind == "mapping" && item.MappingName == "IA_Jump")),
                            "The ordinary add-function flow could not add beyond native slots.");
                        EditFromAction(form, "IA_Jump", "F10", "hold");
                        Check(Workspace(form).Entries("IA_Jump").Any(entry => !entry.IsNative && entry.Trigger == "hold" && entry.Keys.Contains("F10")),
                            "The ordinary action editor did not retain a long-press binding.");
                        int ruleCount = Config(form).Rules.Count; Switch(form);
                        Check(!Config(form).Enabled && Workspace(form).Entries("IA_Jump").Where(entry => !entry.IsNative).All(entry => !entry.Active), "Extra bindings remained active when key enhancement was closed.");
                        form.RenderSession(); Shot(form, "mode-off-extra-bindings");
                        Modal(delegate { Call(form, "ShowActionRelations", "IA_Jump"); }, "UnifiedActionBindingsDialog", delegate(Form details)
                        {
                            var label = All(Field<Control>(details, "bindings")).OfType<Label>().Single(item => item.Text.Contains("F10"));
                            Color muted = (Color)typeof(MainForm).Assembly.GetType("PBZKeyForge.EditorTheme").GetField("Muted", BindingFlags.Static | BindingFlags.Public).GetValue(null);
                            Check(label.ForeColor == muted && All(details).OfType<Button>().Any(button => button.Name == "editBinding" && button.Enabled),
                                "Disabled extra bindings are not gray and editable in ordinary action details.");
                            Shot(details, "action-extra-disabled"); Button(details, "close").PerformClick();
                        });
                        string extra = Workspace(form).Functions.First(function => function.RequiresKeyEnhancement).Id;
                        AddFromKey(form, "F11", extra, true);
                        Check(!Config(form).Enabled && Config(form).Rules.Count == ruleCount + 1 && Workspace(form).Entries(extra).Any(entry => entry.Keys.Contains("F11")),
                            "Inactive editing lost the selected function or input.");
                        Switch(form);
                        Shot(form, "unified-key-view");
                        form.ShowActionView(true); Application.DoEvents(); Shot(form, "unified-action-view");
                        var differences = Field<Control>(form, "schemeDiff");
                        var actionView = Field<Control>(form, "unifiedActions");
                        Check(differences.Visible && differences.Top >= 0 && actionView.Bottom <= differences.Top,
                            "The action view covers the existing scheme differences.");
                        var grid = Field<DataGridView>(actionView, "actions");
                        int jumpRow = Enumerable.Range(0, grid.RowCount).Single(index => Convert.ToString(grid.Rows[index].Cells[0].Value) == BindingWorkspace.FunctionLabel("IA_Jump"));
                        int beforeDirectAdd = Workspace(form).Entries("IA_Jump").Length;
                        Modal(delegate {
                            Call(actionView, "CellClick", grid, new DataGridViewCellMouseEventArgs(3, jumpRow, 5, 5,
                                new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)));
                        }, "UnifiedBindingEditDialog", delegate(Form editor) {
                            var keys = Field<ComboBox>(editor, "mainKey");
                            keys.SelectedIndex = Enumerable.Range(0, keys.Items.Count).First(index =>
                                (string)keys.Items[index].GetType().GetField("Key").GetValue(keys.Items[index]) == "F12");
                            Button(editor, "saveBinding").PerformClick();
                        });
                        Check(Workspace(form).Entries("IA_Jump").Length == beforeDirectAdd + 1
                            && Workspace(form).Entries("IA_Jump").Any(entry => entry.Keys.Contains("F12")),
                            "The action grid's add cell did not save through the ordinary binding editor.");
                        var direct = Workspace(form).Entries("IA_Jump").Single(entry => entry.Keys.Contains("F12"));
                        Modal(delegate { Call(form, "EditActionBinding", "IA_Jump", direct); }, "UnifiedBindingEditDialog", delegate(Form editor) {
                            var triggers = Field<ComboBox>(editor, "trigger"); triggers.SelectedIndex = 4;
                            Button(editor, "saveBinding").PerformClick();
                        });
                        Check(Workspace(form).Entries("IA_Jump").Count(entry => entry.Keys.Contains("F12")) == 1
                            && Workspace(form).Entries("IA_Jump").Single(entry => entry.Keys.Contains("F12")).Trigger == "hold",
                            "Direct binding editing duplicated or lost the selected binding.");
                        RestoreFromKeyView(form);
                        if (editOnly)
                        {
                            Check(!File.Exists(Path.Combine(runtime, "dwmapi.dll")) && !File.Exists(Path.Combine(old, "router-config.lua")),
                                "Editing bindings installed a loader or runtime configuration.");
                            Check(!EditorSession.IsGameRunning || !Field<Button>(form, "btnApply").Enabled,
                                "Apply must remain unavailable while the game is running.");
                            return;
                        }
                        Apply(form);
                        Check(Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation").ReadState() == KeyEnhancementInstallState.Enabled,
                            "Unified bindings were not installed: " + Field<Label>(form, "lblStatus").Text);
                        int savedRules = Config(form).Rules.Count; Switch(form); Apply(form);
                        Check(Config(form).Rules.Count == savedRules && Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation").ReadState() == KeyEnhancementInstallState.Disabled,
                            "Closing key enhancement discarded unified extra bindings.");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally
                    {
                        form.Dispose();
                    }
                }); };
                Application.Run(form);
            }
            if (failure != null) throw new Exception("Unified binding flow failed.", failure);
            Console.WriteLine("UnifiedBindingFlowTests PASS: " + checks + " assertions; screenshots=" + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
