using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class EnhancedPresetShareTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args)
    { try { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (Control nested in All(child)) yield return nested; } }
    static Button Button(Control owner, string name) { return All(owner).OfType<Button>().Single(button => button.Name == name); }
    static void Inspect(UserPresetDialog dialog, string code)
    { Field<TextBox>(dialog, "shareBox").Text = code; Button(dialog, "previewShare").PerformClick(); Application.DoEvents(); }
    static void Shot(Form form, string name)
    { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png")); } }
    static BindingEntry Key(string key, string trigger) { return new BindingEntry { Keys = new[] { key }, Trigger = trigger }; }
    static MainForm Host(EditorSession session, KeyEnhancementConfiguration config, string name)
    {
        var form = new MainForm(new EditorPreferences(Path.Combine(output, name + ".txt")));
        typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form, new object[] { session });
        typeof(MainForm).GetField("keyEnhancement", Hidden).SetValue(form, config); return form;
    }
    static Preset Capture(KeyEnhancementConfiguration config, string name)
    { return UserPresetStore.Capture(EditorSession.CreateOffline(), name, config); }

    static void Roundtrip(KeyEnhancementConfiguration config, string name, int maxLength)
    {
        var preset = Capture(config, name);
        string code = UserPresetStore.Share(preset);
        Check(UserPresetStore.ParseShare(code).KeyEnhancement.ToJson() == config.ToJson(), "Compact roundtrip: " + name);
        Check(code.Length <= maxLength, "Compact share length: " + name);
        Console.WriteLine(name + ": " + code.Length + " chars");
    }
    static void Codec()
    {
        var initial = new KeyEnhancementConfiguration { Enabled = true };
        new BindingWorkspace(EditorSession.CreateOffline(), initial, initial.Clone(), false, -1).Add("IA_Jump", Key("J", "hold"));
        string initialJson = initial.ToJson();
        string initialCode = UserPresetStore.Share(Capture(initial, "初次分享"));
        Check(initial.ToJson() == initialJson && UserPresetStore.ParseShare(initialCode).KeyEnhancement.ToJson() == initialJson,
            "First share preserves configuration serialization and draft signatures");
        // Enhanced raw envelope, baseline 1, explicit keyboard scope, field 5 = { Enabled: true }.
        string golden = "PBZ2:gold:" + Convert.ToBase64String(new byte[] { 2, 8, 1, 18, 0, 42, 1, 1 }).TrimEnd('=');
        var goldenPreset = UserPresetStore.ParseShare(golden);
        Check(goldenPreset.KeyEnhancement.Enabled && goldenPreset.KeyEnhancement.Rules.Count == 0, "Independent compact-format vector");
        Check(UserPresetStore.Share(goldenPreset) == golden, "Compact encoder matches independent vector");
        Roundtrip(new KeyEnhancementConfiguration { Enabled = true }, "只开启增强", 33);
        var native = new KeyEnhancementConfiguration { Enabled = true };
        native.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
        Roundtrip(native, "跳跃改J", 42);
        var simple = new KeyEnhancementConfiguration { Enabled = true };
        new BindingWorkspace(EditorSession.CreateOffline(), simple, simple.Clone(), false, -1).Add("IA_Guard", Key("F9", "hold"));
        Roundtrip(simple, "长按防御", 80);
        var dense = new KeyEnhancementConfiguration { Enabled = true, RuntimeHotkeys = true, CustomLua = "config.example = true" };
        foreach (string trigger in KeyEnhancementConfiguration.Triggers)
        {
            var rule = new KeyEnhancementRule { Trigger = trigger, Seconds = .4, Interval = .125, Threshold = .75, Count = 3, Priority = -8,
                WhenLua = "function() return true end", InputGroup = "11111111222233334444555555555555", Consume = true, BlockOriginalWhenDisabled = true };
            rule.Keys.Add("F9"); rule.ConsumeKeys.Add("F9");
            if (trigger == "sequence") rule.Steps.AddRange(new[] { new[] { "J" }, new[] { "K", "Gamepad_LeftShoulder" } });
            if (trigger == "axis") { rule.Axis = "Gamepad_LeftX"; rule.AxisDirection = "negative"; }
            if (trigger == "custom") { rule.Watch.Add("F10"); rule.EvaluateLua = "function() return {active=true} end"; }
            rule.Outputs.Add(new KeyEnhancementOutput { Kind = "mapping", MappingName = "IA_Jump", Device = "keyboard",
                MappingActions = KeyEnhancementCatalog.ForMapping("IA_Jump").Select(item => item.Path).ToArray(), Value = new Dictionary<string, object> { { "x", .4 }, { "y", -1 } } });
            rule.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Input/CustomAction", Key = "MouseScrollUp", FunctionId = "custom:property" });
            dense.Rules.Add(rule);
        }
        dense.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "action", Path = "/Input/CustomAction", ValueType = "Axis1D", Clamp = .7, Modifiers = new List<string> { "negate" }, Triggers = null, Receiver = "controller" });
        dense.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "ability", Tag = "Ability.Action.Hun.SpecialDash", InputAction = "/Input/CustomAction", Force = true, Priority = 4 });
        dense.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "event", Tag = "Custom.Event", Payload = new Dictionary<string, object> { { "value", 123456789.123456789m }, { "enabled", false } } });
        dense.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "function", Path = "/Script/Engine.Character:Jump", Args = new object[] { null, true, "中文: text", new[] { 1, 2 }, new Dictionary<string, object> { { "double", Math.PI }, { "large", long.MinValue } } } });
        dense.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "property", Id = "property", Name = "未绑定的自定义功能", Property = "bFlag", Value = false });
        dense.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "lua", Code = "function() return true end" });
        dense.CustomFunctions.Add(dense.Rules[0].Outputs[dense.Rules[0].Outputs.Count - 2]);
        dense.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Input/CustomAction" });
        dense.NativeAbilitySuppressions.Add("Ability.Action.Hun.SpecialDash");
        dense.Hooks.Add(new Dictionary<string, object> { { "path", "/Script/Example:Function" }, { "return", false }, { "args", new object[] { "x", 5 } } });
        Roundtrip(dense, "复杂规则与脚本", 1100);
        dense.Enabled = false; dense.Rules[0].Enabled = false; dense.Rules[0].Id = Guid.NewGuid().ToString("D");
        Roundtrip(dense, "停用配置", 1100);
        var fields = new[] { typeof(KeyEnhancementConfiguration), typeof(KeyEnhancementRule), typeof(KeyEnhancementOutput), typeof(KeyEnhancementSuppression), typeof(NativeBindingOverride) };
        var codec = typeof(MainForm).Assembly.GetType("PBZKeyForge.EnhancementShare");
        var schemas = (Dictionary<Type, string[]>)codec.GetField("Fields", Hidden).GetValue(null);
        foreach (Type type in fields) Check(!type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(field => field.Name).Except(schemas[type]).Any(), "Share schema covers " + type.Name);
    }
    static void Overrides()
    {
        var source = EditorSession.CreateOffline(); source.SetKeyboard("IA_Jump", 0, "K");
        var preset = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(source, "普通跳跃K")));
        preset.Id = "user_native";
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
            config.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
            using (var form = Host(session, config, "override-" + mode))
            {
                Call(form, "StageBindingPreset", preset, mode);
                var result = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
                var entries = new BindingWorkspace(session, result, result.Clone(), false, -1).Entries("IA_Jump");
                Check(entries.Any(entry => entry.Keys.Contains("K")), "Incoming K is effective: " + mode);
                Check(entries.Any(entry => entry.Keys.Contains("J")) == (mode == PresetApplyMode.Append), "Retain J only when appending: " + mode);
                typeof(EditorSession).GetProperty("ActiveProfile").GetSetMethod(true).Invoke(session, new object[] { session.SelectedProfile });
                var candidate = BindingLivePlan.Create(session, result, session.ControllerFamilies.First().Index);
                Check(candidate.NativeBindings.Single(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.Contains("K"), "Live application sends incoming K: " + mode);
                Call(form, "StageBindingPreset", null, mode);
                Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").ToJson() == config.ToJson(), "Cancel restores overrides: " + mode);
            }
        }
        var current = new KeyEnhancementConfiguration { Enabled = true };
        var pad = new BindingWorkspace(EditorSession.CreateOffline(), current, current.Clone(), true, 0);
        pad.Add("IA_Jump", Key("Gamepad_FaceButton_Left", "hold"));
        var incoming = new KeyEnhancementConfiguration { Enabled = true };
        new BindingWorkspace(EditorSession.CreateOffline(), incoming, incoming.Clone(), false, -1).Add("IA_Guard", Key("F9", "hold"));
        var keyboardPreset = UserPresetStore.SelectScopes(Capture(incoming, "仅键鼠"), new[] { new PresetScope() });
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        using (var form = Host(EditorSession.CreateOffline(), current.Clone(), "scope-" + mode))
        {
            Call(form, "StageBindingPreset", keyboardPreset, mode);
            Check(Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Rules.Any(rule => rule.Keys.Contains("Gamepad_FaceButton_Left")), "Keyboard-only keeps pad rules: " + mode);
        }
        var padSource = EditorSession.CreateOffline();
        padSource.SetGamepadFamily("IA_Jump", 0, padSource.ControllerFamilies.Single(item => item.Name == "PS").Index, "Gamepad_LeftShoulder");
        var padPreset = UserPresetStore.Capture(padSource, "PS跳跃"); padPreset.Id = "user_ps";
        var planner = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingPresetPlan");
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var target = EditorSession.CreateOffline();
            var overlay = new KeyEnhancementConfiguration { Enabled = true };
            overlay.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "gamepad", Keys = new List<string> { "Gamepad_FaceButton_Top" } });
            int ps = target.ControllerFamilies.Single(item => item.Name == "PS").Index;
            var plan = planner.GetMethod("Create", Hidden).Invoke(null, new object[] { target, overlay, padPreset, mode, ps });
            Check(Field<KeyEnhancementConfiguration>(plan, "Configuration").NativeBindings.Single().Keys.Contains("Gamepad_LeftShoulder"), "Selected PS family determines the shared runtime binding: " + mode);
        }
    }
    static void Dialogs()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        config.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
        var preset = Capture(config, "跳跃改J");
        var store = new UserPresetStore(Path.Combine(output, "preview-library"));
        using (var dialog = new UserPresetDialog(session, store, PresetApplyMode.Replace))
        {
            dialog.Show(); Application.DoEvents(); Inspect(dialog, UserPresetStore.Share(preset));
            Check(All(Field<Panel>(dialog, "preview")).OfType<Label>().Any(label => label.Text.Contains("→ J")), "Preview shows native override");
            object[] diagramArgs = { session.PreviewPreset(preset), preset, false, -1, null };
            var diagramState = (Delegate)typeof(MainForm).Assembly.GetType("PBZKeyForge.UnifiedBindingUi").GetMethod("PresetKeyStates", Hidden).Invoke(null, diagramArgs);
            var jump = diagramState.DynamicInvoke("J"); var space = diagramState.DynamicInvoke("SpaceBar");
            Check(Field<int>(jump, "Count") == 1 && Field<bool>(jump, "Differs") && Field<int>(space, "Count") == 0 && Field<bool>(space, "Differs"), "Diagram marks added J and removed Space");
            Shot(dialog, "native-override-preview");
            dialog.ClientSize = new Size(850, 740); Application.DoEvents(); Shot(dialog, "native-override-minimum");
            Field<TextBox>(dialog, "shareBox").Text = "bad share";
            Check(!Button(dialog, "usePreset").Enabled && !Button(dialog, "saveImport").Visible && !Button(dialog, "copyShare").Enabled, "Editing code invalidates old import");
            Button(dialog, "previewShare").PerformClick();
            Check(!Button(dialog, "usePreset").Enabled && store.Load().Count == 0, "Invalid share cannot save old preview");
        }
        var current = new KeyEnhancementConfiguration { Enabled = true };
        current.CustomFunctions.Add(new KeyEnhancementOutput { Id = "same", Name = "我的功能", Kind = "property", Property = "bFlag", Value = true });
        var incoming = current.Clone(); incoming.CustomFunctions[0].Name = "朋友功能"; incoming.CustomFunctions[0].Value = false;
        var conflict = Capture(incoming, "朋友预设"); var retryStore = new UserPresetStore(Path.Combine(output, "retry-library"));
        using (var host = Host(session, current, "retry"))
        using (var dialog = new UserPresetDialog(session, retryStore, PresetApplyMode.Append, new EditorPreferences(Path.Combine(output, "retry-mode.txt")), () => current, null,
            (value, mode) => Call(host, "StageBindingPreset", value, mode)))
        {
            dialog.Show(); Application.DoEvents(); Inspect(dialog, UserPresetStore.Share(conflict));
            Button(dialog, "usePreset").PerformClick();
            Check(dialog.Visible && retryStore.Load().Count == 0 && Field<KeyEnhancementConfiguration>(host, "keyEnhancement").ToJson() == current.ToJson(), "Failed enhanced append does not save or stage");
            var selector = Field<PresetModeSelector>(dialog, "modeButton");
            selector.Controls.OfType<Button>().Single(button => button.Name == "modeReplace").PerformClick();
            Button(dialog, "usePreset").PerformClick();
            Check(dialog.DialogResult == DialogResult.OK && retryStore.Load().Count == 1, "Correct mode and retry without duplicate-name error");
        }
        var full = EditorSession.CreateOffline(); for (int slot = 0; slot < 3; slot++) full.SetKeyboard("IA_Guard", slot, new[] { "J", "K", "L" }[slot]);
        var source = EditorSession.CreateOffline(); source.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
        var disabled = UserPresetStore.Capture(source, "关闭增强时的预设", new KeyEnhancementConfiguration());
        var enabled = new KeyEnhancementConfiguration { Enabled = true };
        using (var host = Host(full, enabled, "full"))
        using (var dialog = new UserPresetDialog(full, new UserPresetStore(Path.Combine(output, "full-library")), PresetApplyMode.Append,
            new EditorPreferences(Path.Combine(output, "full-mode.txt")), () => enabled, null, (value, mode) => Call(host, "StageBindingPreset", value, mode)))
        {
            dialog.Show(); Application.DoEvents(); Inspect(dialog, UserPresetStore.Share(disabled)); Button(dialog, "usePreset").PerformClick();
            Check(dialog.DialogResult == DialogResult.OK && Field<KeyEnhancementConfiguration>(host, "keyEnhancement").Rules.Any(rule => rule.Keys.Contains("LeftMouseButton")), "Merged enabled plan accepts overflow from disabled preset");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        string clipboard = null;
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args[0]); if (Directory.Exists(output)) throw new Exception("Use a new output directory."); Directory.CreateDirectory(output);
            if (Clipboard.ContainsText()) clipboard = Clipboard.GetText();
            Codec(); Overrides(); Dialogs(); Console.WriteLine("EnhancedPresetShareTests PASS: " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (clipboard == null) Clipboard.Clear(); else Clipboard.SetText(clipboard); }
    }
}
