using System;
using System.IO;
using System.Linq;
using PBZKeyForge;

class CatalogInputPipelineTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string directory = args[0]; Directory.CreateDirectory(directory);
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
            var factory = new KeyEnhancementConfiguration { Enabled = true }; factory.EnsureSchemes(session);
            File.WriteAllText(Path.Combine(directory, "factory-schemes.lua"), factory.CompileLua());
            File.WriteAllText(Path.Combine(directory, "native-actions.lua"), "return {" + string.Join(",", KeyEnhancementInputPlan.Actions.Select(action => "\"" + action.path + "\"")) + "}");
            Console.WriteLine("CatalogInputPipelineTests PASS " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
