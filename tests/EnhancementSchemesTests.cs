using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class EnhancementSchemesTests
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    static BindingWorkspace Workspace(EditorSession session, KeyEnhancementConfiguration config)
    { return new BindingWorkspace(session, config, config.Clone(), false, -1); }
    static BindingEntry Key(string key) { return new BindingEntry { Keys = new[] { key } }; }
    static bool Has(KeyEnhancementConfiguration config, string action, string key)
    { return config.NativeBindings.Single(item => item.FunctionId == action && item.Device == "keyboard").Keys.Contains(key); }
    static KeyEnhancementConfiguration ApplyPreset(EditorSession session, KeyEnhancementConfiguration config, Preset preset, PresetApplyMode mode)
    {
        var plan = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingPresetPlan").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { session, config, preset, mode, -1 });
        return (KeyEnhancementConfiguration)plan.GetType().GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(plan);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        I18n.Current = "zh-Hans";
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var session = EditorSession.CreateOffline(); byte[] original = session.ExportCurrentScheme();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        config.EnsureSchemes(session);
        Check(config.Schemes.Count == 3, "initial schemes missing");
        Check(config.NativeBindings.Count >= MainForm.Actions.Count * 2, "native baseline was not materialized");
        File.WriteAllText(Path.Combine(output, "factory-schemes.lua"), config.CompileLua());
        Check(config.NativeBindings.Any(item => item.FunctionId == "IA_MoveSlow" && item.Device == "gamepad" && item.Keys.Count == 0),
            "factory snapshot lost the absent controller mapping");
        string first = config.SelectedSchemeId, second = config.Schemes[1].Id;
        var workspace = Workspace(session, config);
        workspace.Add("IA_Jump", Key("F9"));
        workspace.Add("ability:Ability.Action.Hun.SpecialDash", new BindingEntry { Keys = new[] { "G" }, Trigger = "press" });
        config.NativeAbilitySuppressions.Add("Ability.Action.Hun.SpecialDash");
        Check(Has(config, "IA_Jump", "F9"), "enhanced ordinary edit missing");
        Check(!session.HasAnyDrafts && original.SequenceEqual(session.ExportCurrentScheme()), "enhanced edits changed native save");
        config.SelectScheme(second);
        Check(!Has(config, "IA_Jump", "F9") && config.Rules.Count == 0 && config.NativeAbilitySuppressions.Count == 0, "scheme contents leaked");
        string signature = config.DeploymentJson();
        config.SelectScheme(first);
        Check(config.DeploymentJson() == signature, "browsing changed deployment");
        string added = config.AddScheme("乱莺剑");
        Workspace(session, config).Add("IA_Jump", Key("F10"));
        Workspace(session, config).Add("scheme:next", new BindingEntry { Keys = new[] { "F6" }, Trigger = "press" });
        config.SelectScheme(second);
        Check(config.Rules.Any(rule => rule.Outputs.Any(item => item.Kind == "scheme")), "switch binding was not shared");
        config.SelectScheme(first);
        Check(!Has(config, "IA_Jump", "F10") && config.Rules.Any(rule => rule.Keys.Contains("G")), "copied scheme shared mutable contents");
        config.SelectScheme(added); config.RenameScheme(added, "乱莺剑 · 实战");
        string json = config.ToJson(); var restored = KeyEnhancementConfiguration.FromJson(json);
        Check(restored.Schemes.Count == 4 && restored.SelectedSchemeId == added && Has(restored, "IA_Jump", "F10"), "JSON lost schemes or selection");
        restored.CustomLua = "_G.PBZSchemeScriptRuns = (_G.PBZSchemeScriptRuns or 0) + 1; config.marker = true";
        string compiled = restored.CompileLua();
        File.WriteAllText(Path.Combine(output, "schemes.lua"), compiled);
        string language = I18n.Current; I18n.Current = "en";
        Check(compiled == restored.CompileLua(), "language changed installed Lua"); I18n.Current = language;
        var bundle = new BindingDocument { Session = session, Configuration = restored };
        string zip = Path.Combine(output, "schemes.zip"); bundle.Export(zip);
        var reopened = BindingDocument.Open(zip);
        Check(reopened.Configuration.Schemes.Count == 4 && reopened.Configuration.SelectedSchemeId == added, "ZIP lost scheme library");
        Check(original.SequenceEqual(reopened.Session.ExportCurrentScheme()), "ZIP changed native fixture");
        var preset = UserPresetStore.Capture(session, "方案草稿", restored);
        Check(preset.KeyEnhancement.Schemes.Count == 0 && Has(preset.KeyEnhancement, "IA_Jump", "F10"), "preset did not capture selected draft only");
        Check(!preset.KeyEnhancement.Rules.Any(rule => rule.Outputs.Any(item => item.Kind == "scheme")), "preset contains unrelated management bindings");
        var store = new UserPresetStore(Path.Combine(output, "presets-" + Guid.NewGuid().ToString("N")));
        var saved = store.Save(preset, "方案草稿");
        preset.KeyEnhancement.NativeAbilitySuppressions.Clear();
        var updated = store.Update(saved.Id, preset, "方案草稿");
        Check(updated.Id == saved.Id && store.Load().Count == 1 && store.Load()[0].KeyEnhancement.NativeAbilitySuppressions.Count == 0, "quick save did not update preset in place");
        string beforeApply = session.SelectedProfile;
        var live = BindingLivePlan.Create(session, restored, -1);
        Check(live.ActiveSchemeId == added && live.SelectedSchemeId == added && session.SelectedProfile == beforeApply, "live switch depends on native activation");
        var normal = restored.Clone(); normal.SetEnabled(false);
        Check(normal.NativeBindings.Count == 0 && normal.Rules.Count == 0, "ordinary mode still exposes enhanced bindings");
        Workspace(session, normal).Add("IA_Jump", Key("F11"));
        Check(session.HasAnyDrafts && normal.Schemes.Count == 4 && normal.Schemes.All(item => !item.NativeBindings.Any(binding => binding.Keys.Contains("F11"))), "ordinary edits changed simulated schemes");
        var liveDraft = normal.Clone(); liveDraft.SetEnabled(true);
        string draftBefore = normal.ToJson(); byte[] nativeDraft = session.ExportCurrentScheme();
        bool rejected = false;
        try { BindingLivePlan.Create(session, liveDraft, -1); }
        catch (InvalidOperationException ex) { rejected = ex.Message == I18n.Str("schemes.nativepending"); }
        Check(rejected && normal.ToJson() == draftBefore && nativeDraft.SequenceEqual(session.ExportCurrentScheme()),
            "live planning silently skipped or cleared ordinary drafts");
        session.Undo(); normal.SetEnabled(true);
        Check(Has(normal, "IA_Jump", "F10") && normal.SelectedSchemeId == added, "mode switch lost enhanced bindings");
        var sparse = new Preset { UserName = "独立动作", BaselineId = PresetBaseline.CurrentId,
            UserMappings = new System.Collections.Generic.List<PresetMapping>(), Scopes = preset.Scopes,
            KeyEnhancement = new KeyEnhancementConfiguration { Enabled = true } };
        var planned = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingPresetPlan").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { session, restored, sparse, PresetApplyMode.Replace, -1 });
        var plannedConfig = (KeyEnhancementConfiguration)planned.GetType().GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(planned);
        Check(plannedConfig.Schemes.Count == 4 && plannedConfig.NativeBindings.Count == restored.NativeBindings.Count && Has(plannedConfig, "IA_Jump", "F10"), "sparse preset lost complete native baseline");
        Check(original.SequenceEqual(session.ExportCurrentScheme()), "preset planning wrote native state");
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var firstPreset = ApplyPreset(session, new KeyEnhancementConfiguration(), preset, mode);
            Check(firstPreset.Enabled && firstPreset.Schemes.Count == 3 && firstPreset.SelectedSchemeId == session.SelectedProfile,
                "first enhanced preset did not initialize independent schemes");
            Check(Has(firstPreset, "IA_Jump", "F10") && firstPreset.NativeBindings.Count >= MainForm.Actions.Count * 2,
                "first enhanced preset lost its keys or complete baseline");
            Check(!session.HasAnyDrafts && original.SequenceEqual(session.ExportCurrentScheme()), "first enhanced preset changed ordinary drafts");
            Check(firstPreset.AddScheme("预设副本") == firstPreset.SelectedSchemeId, "first enhanced preset cannot add a scheme");
        }
        var factoryPreset = Presets.All().Single(item => item.Id == "vanilla_current");
        foreach (bool hotkeys in new[] { true, false })
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var source = restored.Clone(); source.RuntimeHotkeys = !hotkeys;
            var incoming = UserPresetStore.Capture(session, "运行时热键", source.CurrentOnly());
            incoming.KeyEnhancement.RuntimeHotkeys = hotkeys;
            var result = ApplyPreset(session, source, incoming, mode);
            Check(result.RuntimeHotkeys == hotkeys && KeyEnhancementConfiguration.FromJson(result.ToJson()).RuntimeHotkeys == hotkeys,
                "enhanced preset lost runtime hotkeys: " + mode + "/" + hotkeys);
        }
        var factorySource = restored.Clone();
        new BindingWorkspace(session, factorySource, factorySource.Clone(), true, session.ControllerFamilies.First().Index)
            .Add("IA_Jump", Key("Gamepad_RightShoulder"));
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var factory = ApplyPreset(session, factorySource, factoryPreset, mode);
            foreach (var binding in factory.NativeBindings)
            {
                bool pad = binding.Device == "gamepad";
                int family = pad ? session.ControllerFamilies.First().Index : -1;
                Check(binding.Keys.OrderBy(key => key).SequenceEqual(session.Relations(binding.FunctionId, pad, family).DefaultKeys.OrderBy(key => key)),
                    "factory preset retained enhanced keys: " + binding.FunctionId + "/" + binding.Device);
            }
            Check(factory.ConfigurationFor(first).ToJson() == restored.ConfigurationFor(first).ToJson(), "factory reset changed another enhanced scheme");
            Check(original.SequenceEqual(session.ExportCurrentScheme()), "enhanced factory reset changed native bytes");
        }
        var history = restored.Clone();
        Workspace(session, history).Add("scheme:" + added, new BindingEntry { Keys = new[] { "F7" }, Trigger = "press" });
        var historicalBaseline = history.Clone();
        history.DeleteScheme(added);
        var historicalWorkspace = new BindingWorkspace(session, history, historicalBaseline, false, -1);
        var beforeEntries = (BindingEntry[])typeof(BindingWorkspace).GetMethod("CollectEntries", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(historicalWorkspace, new object[] { historicalBaseline, true, null, false });
        Check(beforeEntries.Any(entry => entry.FunctionId == "scheme:" + added && entry.Label.Contains("乱莺剑 · 实战")),
            "deleted scheme lost its historical switch binding label");
        Check(typeof(BindingWorkspace).GetMethod("Snapshot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(historicalWorkspace, null) != null,
            "deleted scheme prevented baseline snapshot construction");
        restored.DeleteScheme(added);
        Check(restored.Schemes.Count == 3 && restored.SelectedSchemeId != added, "delete did not select a retained scheme");
        Check(original.SequenceEqual(session.ExportCurrentScheme()), "scheme operations wrote native data");

        Application.EnableVisualStyles();
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences"))))
        {
            form.UiOffline(); I18n.Current = "zh-Hans"; form.Show(); Application.DoEvents();
            var add = (Button)Field(form, "btnAddScheme"); var save = (Button)Field(form, "btnSaveSchemePreset");
            Check(!add.Enabled && save.Visible && save.Enabled, "ordinary controls have wrong availability");
            var enhanced = (KeyEnhancementConfiguration)Field(form, "keyEnhancement"); enhanced.Enabled = true;
            form.GetType().GetField("flameEffectsEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, false);
            form.RenderSession(); Application.DoEvents();
            Check(add.Enabled && ((EditorTab[])Field(form, "schemeTabs")).Length == 3, "enhanced initial tabs missing");
            Check(add.Parent == save.Parent && add.Right <= save.Left, "fixed controls overlap");
            Capture(form, Path.Combine(output, "three-schemes.png"));
            for (int i = 0; i < 9; i++) enhanced.AddScheme("战斗方案 " + (i + 1));
            form.RenderSession(); Application.DoEvents();
            var more = (Button)Field(form, "btnSchemeMore");
            Check(((EditorTab[])Field(form, "schemeTabs")).Length == 12 && more.Visible && add.Visible && save.Visible, "overflow hides schemes or fixed controls");
            Capture(form, Path.Combine(output, "many-schemes.png"));
            form.ClientSize = new Size(980, 710); Application.DoEvents();
            Check(save.RectangleToScreen(save.ClientRectangle).Right <= form.RectangleToScreen(form.ClientRectangle).Right, "preset save is clipped at minimum size");
            var selectedTab = ((EditorTab[])Field(form, "schemeTabs")).Single(tab => tab.Selected);
            var viewport = (Control)Field(form, "schemeViewport");
            Check(selectedTab.RectangleToScreen(selectedTab.ClientRectangle).Right <= viewport.RectangleToScreen(viewport.ClientRectangle).Right
                && selectedTab.RectangleToScreen(selectedTab.ClientRectangle).Left >= viewport.RectangleToScreen(viewport.ClientRectangle).Left,
                "selected scheme is clipped after shrinking window");
            Capture(form, Path.Combine(output, "minimum.png"));
            string selected = enhanced.SelectedSchemeId; enhanced.SetEnabled(false); form.RenderSession();
            Check(((EditorTab[])Field(form, "schemeTabs")).Length == 3 && !add.Enabled, "ordinary mode retained simulated scheme tabs");
            enhanced.SetEnabled(true); form.RenderSession();
            Check(enhanced.SelectedSchemeId == selected && ((EditorTab[])Field(form, "schemeTabs")).Length == 12, "mode switch lost enhanced selection");
            enhanced.SetEnabled(false);
            form.GetType().GetField("keyEnhancementBaseline", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, enhanced.Clone());
            ((System.Collections.ICollection)Field(form, "bindingHistory")).GetType().GetMethod("Clear").Invoke(Field(form, "bindingHistory"), null);
            form.Close();
        }
        Console.WriteLine("EnhancementSchemesTests: " + checks + " checks passed");
    }
    static void Capture(Form form, string path)
    { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path); } }
}
