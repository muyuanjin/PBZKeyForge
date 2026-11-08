using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PBZKeyForge;

public static class PresetImpactTests
{
    const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static readonly Type PlanType = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingPresetPlan");
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Plan(EditorSession session, KeyEnhancementConfiguration configuration, Preset preset, PresetApplyMode mode)
    {
        try { return PlanType.GetMethod("Create", HiddenStatic).Invoke(null, new object[] { session, configuration, preset, mode, -1 }); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static PresetImpact Review(EditorSession session, KeyEnhancementConfiguration configuration, object plan)
    { return (PresetImpact)typeof(PresetImpact).GetMethod("Create", HiddenStatic).Invoke(null, new[] { session, configuration, plan }); }
    static EditorSession PlannedSession(object plan)
    { return (EditorSession)PlanType.GetField("Session", HiddenInstance).GetValue(plan); }
    static KeyEnhancementConfiguration PlannedConfiguration(object plan)
    { return (KeyEnhancementConfiguration)PlanType.GetField("Configuration", HiddenInstance).GetValue(plan); }
    static Preset KeyboardPreset(EditorSession source, string name, KeyEnhancementConfiguration configuration = null)
    { return UserPresetStore.SelectScopes(UserPresetStore.Capture(source, name, configuration), new[] { new PresetScope() }); }
    static PresetImpactChange Change(PresetImpact impact, string action, string device)
    { return impact.Changes.Single(item => item.Label == BindingWorkspace.FunctionLabel(action) && item.Device == device); }
    static string Keyboard { get { return I18n.Str("editor.keyboard"); } }

    static void OrdinaryModes()
    {
        var target = EditorSession.CreateOffline(); target.SetKeyboard("IA_Jump", 0, "J");
        var source = EditorSession.CreateOffline(); source.SetKeyboard("IA_Guard", 1, "LeftMouseButton");
        var preset = KeyboardPreset(source, "额外防御"); var configuration = new KeyEnhancementConfiguration();
        byte[] draft = target.ExportCurrentScheme(); string original = configuration.ToJson();
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var plan = Plan(target, configuration, preset, mode); var impact = Review(target, configuration, plan);
            var guard = Change(impact, "IA_Guard", Keyboard);
            Check(guard.Before.Contains(BindingButton.FriendlyKey("RightMouseButton")) && guard.After.Contains(BindingButton.FriendlyKey("LeftMouseButton")), "Review shows current and resulting guard: " + mode);
            Check(impact.Changes.Any(item => item.Label == BindingWorkspace.FunctionLabel("IA_Jump")) == (mode == PresetApplyMode.OnlyPreset), "OnlyPreset reveals reset of unrelated custom J: " + mode);
            Check(impact.Changes.All(item => item.Device == Keyboard), "Keyboard-only import has no controller changes: " + mode);
            Check(impact.Scope.Contains(Keyboard) && impact.Scope.Contains(I18n.Str("scheme.default")), "Review identifies actual target scheme and devices: " + mode);
            var applied = EditorSession.CreateOffline(); applied.SetKeyboard("IA_Jump", 0, "J");
            PlanType.GetMethod("Apply", HiddenInstance).Invoke(plan, new object[] { applied });
            Check(applied.Relations("IA_Guard", false, -1).Keys.SequenceEqual(PlannedSession(plan).Relations("IA_Guard", false, -1).Keys), "Applying reviewed plan preserves its result: " + mode);
            if (mode == PresetApplyMode.OnlyPreset)
            {
                var jump = Change(impact, "IA_Jump", Keyboard);
                Check(jump.Before.Contains("J") && jump.After.Contains(BindingButton.FriendlyKey("SpaceBar")), "Review explicitly shows J restoring to Space");
            }
        }
        Check(target.ExportCurrentScheme().SequenceEqual(draft) && configuration.ToJson() == original, "Building and reviewing plans never mutates the source draft");
        var saved = UserPresetStore.Capture(target, "相同配置");
        var noOp = Review(target, configuration, Plan(target, configuration, saved, PresetApplyMode.Append));
        Check(noOp.Changes.Count == 0 && noOp.Effects.Count == 0, "An identical preset has no changes despite nonfactory current bindings");
        var removeSource = EditorSession.CreateOffline();
        for (int slot = 0; slot < 3; slot++) removeSource.SetKeyboard("IA_Jump", slot, "None");
        var removal = Review(target, configuration, Plan(target, configuration, KeyboardPreset(removeSource, "取消跳跃"), PresetApplyMode.Replace));
        var removed = Change(removal, "IA_Jump", Keyboard);
        Check(removed.Kind == "remove" && removed.Before.Contains("J") && removed.After == "—", "Unbinding shows a removal with its previous key");
    }

    static void RuntimeOverrides()
    {
        var target = EditorSession.CreateOffline(); var current = new KeyEnhancementConfiguration { Enabled = true };
        current.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
        var source = EditorSession.CreateOffline(); source.SetKeyboard("IA_Jump", 0, "K");
        var preset = KeyboardPreset(source, "跳跃K");
        string original = current.ToJson();
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace })
        {
            var plan = Plan(target, current, preset, mode); var review = Review(target, current, plan); var jump = Change(review, "IA_Jump", Keyboard);
            Check(jump.Before == "J" && jump.After.Contains("K"), "Review uses effective runtime J rather than factory Space: " + mode);
            Check(jump.After.Contains("J") == (mode == PresetApplyMode.Append), "Review follows selected merge mode: " + mode);
            var staged = PlannedConfiguration(plan);
            Check(staged.NativeBindings.Single(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.Contains("K"), "Reviewed keys come from actual candidate configuration: " + mode);
        }
        Check(current.ToJson() == original, "Runtime review leaves the existing override unchanged");
        var identical = KeyboardPreset(source, "相同J", current);
        var noOp = Review(target, current, Plan(target, current, identical, PresetApplyMode.Append));
        Check(!noOp.Changes.Any(item => item.Label == BindingWorkspace.FunctionLabel("IA_Jump")), "Existing runtime J is not replaced in preview by a preset's unused ordinary K mapping");
    }

    static KeyEnhancementConfiguration RuleConfiguration(string key, string trigger)
    {
        var configuration = new KeyEnhancementConfiguration { Enabled = true };
        var rule = new KeyEnhancementRule { Id = "repeat-light", Trigger = trigger };
        rule.Keys.Add(key);
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "ability", Tag = "Ability.Action.Hun.SpecialDash" });
        configuration.Rules.Add(rule); return configuration;
    }

    static void EnhancedEffects()
    {
        var target = EditorSession.CreateOffline();
        var off = new KeyEnhancementConfiguration(); var incoming = RuleConfiguration("F8", "hold");
        var preset = KeyboardPreset(EditorSession.CreateOffline(), "增强预设", incoming);
        var review = Review(target, off, Plan(target, off, preset, PresetApplyMode.Append));
        Check(review.Effects.Any(item => item.Contains(I18n.Str("preset.review.enhancement", I18n.Str("mod.disabled"), I18n.Str("mod.enabled")))), "Enabling enhancement is explicit in review");
        var ability = Change(review, "ability:Ability.Action.Hun.SpecialDash", Keyboard);
        Check(ability.Kind == "add" && ability.After.Contains("F8") && ability.After.Contains(I18n.Str("unified.trigger.hold")), "Review includes enhanced binding and trigger semantics");
        var edited = incoming.Clone(); edited.Rules[0].Trigger = "pulse"; edited.Rules[0].Interval = .17;
        edited.Rules[0].DamageMultiplier = 2; edited.Rules[0].WhenLua = "function(context) return context.gameplay end";
        var changed = Review(target, incoming, Plan(target, incoming, KeyboardPreset(EditorSession.CreateOffline(), "改为连发", edited), PresetApplyMode.Replace));
        var changedAbility = Change(changed, "ability:Ability.Action.Hun.SpecialDash", Keyboard);
        Check(changedAbility.Kind == "change" && changedAbility.Before.Contains(I18n.Str("unified.trigger.hold")) && changedAbility.After.Contains(I18n.Str("unified.trigger.pulse")), "Trigger replacement displays before and after");
        Check(changedAbility.After.Contains("0.17") && changedAbility.After.Contains("×2") && changedAbility.After.Contains(I18n.Str("ruleeditor.when")), "Review exposes timing, damage multiplier and activation condition");
        var globals = incoming.Clone(); globals.RuntimeHotkeys = true;
        globals.NativeAbilitySuppressions.Add("Ability.Action.Hun.SpecialDash");
        globals.Hooks.Add(new Dictionary<string, object> { { "path", "/Script/Engine.Character:CanJump" }, { "return", false } });
        globals.CustomLua = "config.review_example = true";
        globals.CustomFunctions.Add(new KeyEnhancementOutput { Id = "own", Name = "自定义标志", Kind = "property", Property = "bFlag", Value = true });
        var globalReview = Review(target, incoming, Plan(target, incoming, UserPresetStore.Capture(EditorSession.CreateOffline(), "全局设置", globals), PresetApplyMode.Replace));
        Check(globalReview.Effects.Any(item => item.StartsWith(I18n.Str("preset.review.hotkeys", "", "").Split('：')[0])), "Runtime hotkeys are included in effects");
        Check(globalReview.Effects.Any(item => item.Contains("CanJump") && item.Contains(I18n.Str("binding.scope.global"))), "Hooks are reviewed by their actual function and global scope");
        Check(globalReview.Effects.Any(item => item.Contains(I18n.Str("preset.review.lua", I18n.Str("preset.review.add"))) && item.Contains(I18n.Str("binding.scope.global"))), "Adding global custom Lua is visible");
        Check(globalReview.Effects.Any(item => item.Contains("自定义标志")), "Unbound custom function definitions are visible");
        Check(globalReview.Effects.Any(item => item.Contains(BindingWorkspace.FunctionLabel("ability:Ability.Action.Hun.SpecialDash"))), "Blocking original ability triggers is visible");
        var sameRule = incoming.Clone(); sameRule.Rules[0].Id = "equivalent-local-id";
        var same = Review(target, incoming, Plan(target, incoming, KeyboardPreset(EditorSession.CreateOffline(), "同一规则", sameRule), PresetApplyMode.Replace));
        Check(same.Changes.Count == 0 && same.Effects.Count == 0, "Locally different rule IDs do not create false binding changes");
    }

    static void ControllerIsolation()
    {
        var source = EditorSession.CreateOffline(); var target = EditorSession.CreateOffline();
        int sourcePs = source.ControllerFamilies.Single(item => item.Name == "PS").Index;
        source.SetGamepadFamily("IA_Jump", 0, sourcePs, "Gamepad_LeftShoulder");
        var preset = UserPresetStore.SelectScopes(UserPresetStore.Capture(source, "PS跳跃"), new[] { new PresetScope { Family = "PS" } });
        var current = new KeyEnhancementConfiguration();
        var review = Review(target, current, Plan(target, current, preset, PresetApplyMode.Replace));
        Check(review.Changes.Count > 0 && review.Changes.All(item => item.Device.EndsWith(" · PS")), "PS-only review excludes keyboard and Xbox");
        Check(review.Scope.Contains("PS") && !review.Scope.Contains("Xbox"), "Review scope names the affected controller family");
    }

    static object Call(MainForm form, string name, params object[] values)
    {
        try { return typeof(MainForm).GetMethod(name, HiddenInstance).Invoke(form, values); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    static void ReviewedAcceptance()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PBZ-preset-review-" + Guid.NewGuid().ToString("N"));
        foreach (bool enhanced in new[] { false, true })
        foreach (PresetApplyMode mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
        {
            var target = EditorSession.CreateOffline(); target.SetKeyboard("IA_Jump", 0, "J");
            var configuration = new KeyEnhancementConfiguration { Enabled = enhanced };
            var firstSource = EditorSession.CreateOffline(); firstSource.SetKeyboard("IA_Guard", 1, "F7");
            var first = KeyboardPreset(firstSource, "已有预设"); first.Id = "user-first";
            var source = EditorSession.CreateOffline(); source.SetKeyboard("IA_Jump", 0, "K");
            var imported = KeyboardPreset(source, "直接使用分享码");
            Check(imported.Id == null, "Imported configuration needs no library identity");
            using (var form = new MainForm(new EditorPreferences(Path.Combine(directory, enhanced + "-" + mode + ".txt"))))
            {
                typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form, new object[] { target });
                typeof(MainForm).GetField("keyEnhancement", HiddenInstance).SetValue(form, configuration);
                Call(form, "StageBindingPreset", first, PresetApplyMode.Replace);
                var before = (KeyEnhancementConfiguration)typeof(MainForm).GetField("keyEnhancement", HiddenInstance).GetValue(form);
                var plan = Call(form, "PlanBindingPreset", imported, mode);
                var review = Review(target, before, plan);
                Check(review.Changes.Any(change => change.Before.Contains("F7") && !change.After.Contains("F7")), "Review shows removal of the previous preset: " + enhanced + "/" + mode);
                string expected = PlannedConfiguration(plan).ToJson();
                byte[] expectedDraft = PlannedSession(plan).ExportCurrentScheme();
                Call(form, "StageReviewedBindingPreset", imported, mode, plan);
                var accepted = (KeyEnhancementConfiguration)typeof(MainForm).GetField("keyEnhancement", HiddenInstance).GetValue(form);
                Check(ReferenceEquals(accepted, PlannedConfiguration(plan)) && accepted.ToJson() == expected, "Main window accepts the exact reviewed configuration: " + enhanced + "/" + mode);
                Check(target.ExportCurrentScheme().SequenceEqual(expectedDraft), "Main window accepts the exact reviewed native draft: " + enhanced + "/" + mode);
                Call(form, "StageBindingPreset", null, mode);
                Check(((KeyEnhancementConfiguration)typeof(MainForm).GetField("keyEnhancement", HiddenInstance).GetValue(form)).ToJson() == configuration.ToJson(), "Clearing an unsaved import restores the initial enhancement checkpoint: " + enhanced + "/" + mode);
            }
        }
    }

    [STAThread]
    public static int Main()
    {
        try
        {
            I18n.Current = "zh-Hans";
            OrdinaryModes(); RuntimeOverrides(); EnhancedEffects(); ControllerIsolation(); ReviewedAcceptance();
            Console.WriteLine("PresetImpactTests PASS: " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
