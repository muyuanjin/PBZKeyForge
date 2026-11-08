using System;
using System.IO;
using System.Linq;
using PBZKeyForge;

class CatalogInputPipelineTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    const string Deflect = "/PBZDeflectSystem/Input/IA_Deflect", Stance = "/PBZDeflectSystem/Input/IA_Stance";
    static void WriteGuardFixture(string directory, string name, EditorSession session, KeyEnhancementConfiguration configuration, bool pad)
    {
        var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "防御来源", configuration))).KeyEnhancement;
        var outputs = shared.Rules.SelectMany(rule => rule.Outputs).ToArray();
        Check(shared.Rules.Count == configuration.Rules.Count, name + ": sharing lost a rule");
        for (int index = 0; index < configuration.Rules.Count; index++)
        {
            var original = configuration.Rules[index]; var restored = shared.Rules[index];
            Check(original.Keys.SequenceEqual(restored.Keys) && original.Trigger == restored.Trigger && original.DefaultTrigger == restored.DefaultTrigger,
                name + ": sharing changed a guard source trigger");
            Check(original.Outputs.Select(output => output.Kind + ":" + output.Path + ":" + output.MappingName)
                .SequenceEqual(restored.Outputs.Select(output => output.Kind + ":" + output.Path + ":" + output.MappingName)),
                name + ": sharing changed a guard or attack output identity");
        }
        foreach (var output in outputs)
        {
            Check(output.Device == (pad ? "gamepad" : "keyboard"), name + ": sharing lost source device");
            if (output.Kind == "action" && (output.Path == Deflect || output.Path == Stance))
                Check(output.NativePipeline == true, name + ": an independent guard action lost its native pipeline");
            if (output.Kind == "mapping" && output.MappingName == "IA_Guard")
                Check(output.MappingActions.OrderBy(path => path).SequenceEqual(new[] { Deflect, Stance }.OrderBy(path => path)),
                    name + ": combined guard must retain both actions");
        }
        string lua = shared.CompileLua();
        foreach (string path in new[] { Deflect, Stance }.Where(path => outputs.Any(output => output.Kind == "action" && output.Path == path)))
            Check(lua.Contains("[\"mapping\"]=\"" + path + "\",[\"actions\"]={\"" + path + "\"}"),
                name + ": compilation merged an independent action into combined guard");
        if (outputs.Any(output => output.Kind == "mapping" && output.MappingName == "IA_Guard"))
            Check(lua.Contains("[\"mapping\"]=\"IA_Guard\"") && !lua.Contains("[\"mapping\"]=\"IA_Guard\",[\"actions\"]={\"" + Deflect + "\"}"),
                name + ": compilation lost combined guard identity");
        File.WriteAllText(Path.Combine(directory, (pad ? "pad-" : "keyboard-") + name + ".lua"), lua);
    }
    static void GuardSourceFixtures(string directory, bool pad)
    {
        var session = EditorSession.CreateOffline();
        var source = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, source, source.Clone(), pad, pad ? 0 : -1);
        string[] keys = pad ? new[] { "Gamepad_RightShoulder", "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left", "Gamepad_FaceButton_Top", "Gamepad_FaceButton_Right" }
            : new[] { "F9", "F10", "F11", "F12", "F8" };
        workspace.Add("action:" + Deflect, new BindingEntry { Keys = new[] { keys[0] } });
        workspace.Add("action:" + Stance, new BindingEntry { Keys = new[] { keys[1] } });
        workspace.Add("IA_Guard", new BindingEntry { Keys = new[] { keys[2] }, Trigger = "down" });
        workspace.Add("IA_LightAttack", new BindingEntry { Keys = new[] { keys[3] }, Trigger = "down" });
        workspace.Add("IA_HeavyAttack", new BindingEntry { Keys = new[] { keys[4] }, Trigger = "down" });
        Check(source.Rules.Count == 5, "Guard source fixture must include all three guard modes and both ordinary attacks");
        WriteGuardFixture(directory, "guard-sources", session, source, pad);
        foreach (string guard in new[] { "deflect", "stance", "guard" })
        foreach (string attack in new[] { "light", "heavy" })
        foreach (bool attackFirst in new[] { false, true })
        {
            var configuration = new KeyEnhancementConfiguration { Enabled = true };
            var combined = new BindingWorkspace(session, configuration, configuration.Clone(), pad, pad ? 0 : -1);
            string defenseId = guard == "guard" ? "IA_Guard" : "action:" + (guard == "deflect" ? Deflect : Stance);
            string attackId = attack == "light" ? "IA_LightAttack" : "IA_HeavyAttack";
            combined.AddFunctions(attackFirst ? new[] { attackId, defenseId } : new[] { defenseId, attackId },
                new BindingEntry { Keys = new[] { keys[0] } });
            Check(configuration.Rules.Single().DefaultTrigger && configuration.Rules.Single().Outputs.Count == 2,
                "A default same-key guard and attack must retain two distinguishable outputs");
            var first = configuration.Rules.Single().Outputs.First();
            Check(attackFirst ? first.MappingName == attackId
                : guard == "guard" ? first.MappingName == "IA_Guard" : first.Path == (guard == "deflect" ? Deflect : Stance),
                "A same-key fixture lost the selected output order");
            WriteGuardFixture(directory, guard + "-" + attack + (attackFirst ? "-attack-first" : ""), session, configuration, pad);
        }
    }
    static void AttackRepeatFixture(string directory, bool pad)
    {
        var session = EditorSession.CreateOffline();
        byte[] original = session.ExportCurrentScheme();
        var configuration = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, configuration, configuration.Clone(), pad, pad ? 0 : -1);
        string[] paths = { "/PBZInput/InputActions/IA_AttackL_Press", "/PBZInput/InputActions/IA_AttackH_Press" };
        string[] keys = pad ? new[] { "Gamepad_RightShoulder", "Gamepad_LeftShoulder" } : new[] { "F9", "F10" };
        for (int index = 0; index < paths.Length; index++)
            workspace.Add("action:" + paths[index], new BindingEntry { Keys = new[] { keys[index] }, Trigger = "pulse", Interval = .15 });
        var preset = UserPresetStore.Capture(session, "轻重击连发", configuration);
        var shared = UserPresetStore.ParseShare(UserPresetStore.Share(preset)).KeyEnhancement;
        Check(shared.Rules.Count == 2 && shared.NativeBindings.Count == 0 && shared.Schemes.Count == 0,
            "Press-only repetition must not add native overrides or unrelated scheme defaults");
        Check(preset.UserMappings.Count == 0 && !session.HasChanges && original.SequenceEqual(session.ExportCurrentScheme()),
            "Press-only repetition must preserve ordinary save bindings");
        for (int index = 0; index < paths.Length; index++)
        {
            var rule = shared.Rules[index]; var output = rule.Outputs.Single();
            Check(rule.Keys.SequenceEqual(new[] { keys[index] }) && rule.Trigger == "pulse" && !rule.DefaultTrigger
                && rule.Interval == .15 && !rule.Consume && rule.ConsumeKeys.Count == 0,
                "Sharing changed press-only repeat keys, cadence or consumption");
            Check(output.Kind == "action" && output.Path == paths[index] && output.NativePipeline == true
                && output.Device == (pad ? "gamepad" : "keyboard"), "Sharing changed the repeat input pipeline or device");
        }
        string lua = shared.CompileLua();
        foreach (string path in paths)
            Check(lua.Contains("[\"mapping\"]=\"" + path + "\",[\"actions\"]={\"" + path + "\"}"),
                "Repeat compilation must target only the requested press action");
        File.WriteAllText(Path.Combine(directory, (pad ? "pad" : "keyboard") + "-attack-repeat.lua"), lua);
    }
    static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string directory = args[0]; Directory.CreateDirectory(directory);
            if (args.Contains("--guard-only"))
            {
                foreach (bool pad in new[] { false, true }) GuardSourceFixtures(directory, pad);
                Console.WriteLine("CatalogInputPipelineTests guard sources PASS " + checks); return 0;
            }
            if (args.Contains("--attack-repeat-only"))
            {
                foreach (bool pad in new[] { false, true }) AttackRepeatFixture(directory, pad);
                Console.WriteLine("CatalogInputPipelineTests attack repeat PASS " + checks); return 0;
            }
            var session = EditorSession.CreateOffline();
            var mappingSource = File.ReadAllText("tests/input-router/fixtures/native-mappings-302256.lua");
            var mappedPaths = System.Text.RegularExpressions.Regex.Matches(mappingSource, "\\{\\\"(/[^\\\"]+)\\\",\\\"[^\\\"]+\\\",[012],")
                .Cast<System.Text.RegularExpressions.Match>().Select(match => match.Groups[1].Value).Distinct().ToArray();
            Check(mappedPaths.Length == 79 && mappedPaths.All(KeyEnhancementInputPlan.HasPipeline), "Every serialized InputAction must have a catalog pipeline.");
            foreach (var action in KeyEnhancementCatalog.Targets.Where(target => target.Kind == "action"))
            {
                var config = new KeyEnhancementConfiguration { Enabled = true };
                var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
                workspace.Add(action.Id, new BindingEntry { Keys = new[] { "F9" }, Trigger = "default" });
                bool mapped = mappedPaths.Contains(action.Path);
                Check(config.Rules[0].Outputs[0].NativePipeline == mapped && config.Rules[0].Trigger == (mapped ? "down" : "press"), "Catalog default disagrees with the serialized action source: " + action.Path);
            }
            foreach (bool pad in new[] { false, true })
            foreach (string path in new[] {
                "/SGameCombatSystem/Weapon/Input/IA_ChangeShachiWeapon",
                "/PBZInput/InputActions/IA_Execution", "/PBZInput/InputActions/IA_AttackL_Press",
                "/PBZInput/InputActions/IA_ShachiAbility" })
            {
                var config = new KeyEnhancementConfiguration { Enabled = true };
                var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
                var entry = workspace.Add("action:" + path, new BindingEntry { Keys = new[] { pad ? "Gamepad_DPad_Down" : "F9" }, Trigger = "default" });
                Check(config.Rules[0].Outputs[0].NativePipeline == true && entry.Trigger == "default", "Catalog defaults must retain mapping pipelines.");
                var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "输入管线", config))).KeyEnhancement;
                Check(shared.Rules[0].Outputs[0].NativePipeline == true && shared.Rules[0].Outputs[0].Device == (pad ? "gamepad" : "keyboard"), "Sharing lost pipeline or device identity.");
                File.WriteAllText(Path.Combine(directory, (pad ? "pad-" : "keyboard-") + path.Substring(path.LastIndexOf('/') + 1) + ".lua"), shared.CompileLua());
                var raw = new KeyEnhancementConfiguration { Enabled = true };
                var rawWorkspace = new BindingWorkspace(session, raw, raw.Clone(), pad, pad ? 0 : -1);
                rawWorkspace.AddCustom(new KeyEnhancementOutput { Kind = "action", Path = path }, new BindingEntry { Keys = new[] { "F10" }, Trigger = "down" }, "自定义输入");
                var rawShared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "自定义输入", raw))).KeyEnhancement;
                Check(rawShared.Rules[0].Outputs[0].NativePipeline == false && rawShared.CompileLua().Contains("[\"kind\"]=\"action\""), "Explicit raw continuous input must survive sharing.");
                var legacy = new KeyEnhancementConfiguration { Enabled = true };
                var rule = new KeyEnhancementRule { Trigger = "down" }; rule.Keys.Add(pad ? "Gamepad_DPad_Down" : "F9");
                rule.Outputs.Add(new KeyEnhancementOutput { Path = path }); legacy.Rules.Add(rule);
                string lua = legacy.CompileLua();
                Check(lua.Contains("[\"catalog_pipeline\"]=true") && lua.Contains("[\"device\"]=\"" + (pad ? "gamepad" : "keyboard") + "\""), "Existing catalog rules must inherit the source pipeline on their input device.");
            }
            foreach (bool pad in new[] { false, true })
            {
                var abilities = new KeyEnhancementConfiguration { Enabled = true };
                foreach (var ability in KeyEnhancementCatalog.Targets.Where(target => target.Kind == "ability"))
                foreach (string trigger in new[] { "default", "press" })
                {
                    var single = new KeyEnhancementConfiguration { Enabled = true };
                    var abilityWorkspace = new BindingWorkspace(session, single, single.Clone(), pad, pad ? 0 : -1);
                    var entry = abilityWorkspace.Add(ability.Id, new BindingEntry { Keys = new[] { pad ? "Gamepad_RightShoulder" : "G" }, Trigger = trigger });
                    var rule = single.Rules.Single(item => item.Id == entry.RuleId);
                    Check(entry.Trigger == trigger && rule.DefaultTrigger == (trigger == "default"), "Ability binding lost its trigger policy: " + ability.Tag);
                    abilities.Rules.Add(rule);
                }
                Check(abilities.Rules.Any(rule => rule.Outputs.Any(output => output.Tag == "Ability.Action.Hun.Heavy.ZhanMa.DB")), "Ability sweep must include ZhanMa charge slash.");
                var abilityShared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "能力输入", abilities))).KeyEnhancement;
                Check(abilityShared.Rules.Count == abilities.Rules.Count && abilityShared.Rules.Count(rule => rule.DefaultTrigger) * 2 == abilities.Rules.Count, "Sharing lost default versus explicit ability triggers.");
                Check(abilityShared.CompileLua().Contains("[\"default_trigger\"]=true"), "Compiled ability bindings lost the default trigger policy.");
                File.WriteAllText(Path.Combine(directory, (pad ? "pad" : "keyboard") + "-abilities.lua"), abilityShared.CompileLua());

                var config = new KeyEnhancementConfiguration { Enabled = true };
                var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
                workspace.Add("action:/PBZDeflectSystem/Input/IA_Deflect", new BindingEntry { Keys = new[] { pad ? "Gamepad_RightShoulder" : "F9" }, Trigger = "default" });
                workspace.Add("action:/PBZDeflectSystem/Input/IA_Stance", new BindingEntry { Keys = new[] { pad ? "Gamepad_LeftShoulder" : "F10" }, Trigger = "default" });
                Check(config.Rules.All(rule => rule.DefaultTrigger && rule.Trigger == "down" && rule.Outputs[0].NativePipeline == true), "Default guard inputs must track physical hold and release on both devices.");
                var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "防御输入", config))).KeyEnhancement;
                File.WriteAllText(Path.Combine(directory, (pad ? "pad" : "keyboard") + "-guard.lua"), shared.CompileLua());
                GuardSourceFixtures(directory, pad);
                AttackRepeatFixture(directory, pad);
            }
            var factory = new KeyEnhancementConfiguration { Enabled = true }; factory.EnsureSchemes(session);
            File.WriteAllText(Path.Combine(directory, "factory-schemes.lua"), factory.CompileLua());
            File.WriteAllText(Path.Combine(directory, "native-actions.lua"), "return {" + string.Join(",", KeyEnhancementInputPlan.Actions.Select(action => "\"" + action.path + "\"")) + "}");
            Console.WriteLine("CatalogInputPipelineTests PASS " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
