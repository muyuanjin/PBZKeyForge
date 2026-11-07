using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PBZKeyForge;

class BindingTriggerContractTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static KeyEnhancementRule Rule(KeyEnhancementConfiguration config, BindingEntry entry)
    { return config.Rules.Single(item => item.Id == entry.RuleId); }
    static BindingEntry Key(string key, string trigger = "default")
    { return new BindingEntry { Keys = new[] { key }, Trigger = trigger }; }
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    static void Run(string[] args)
    {
        I18n.Current = "zh-Hans";
        string directory = args[0]; Directory.CreateDirectory(directory);
        const string move = "move:Ability.Action.Hun.Light.Air.AA";
        const string ability = "ability:Ability.Action.Hun.Heavy.BronzeSword.BHold";
        foreach (bool pad in new[] { false, true })
        {
            string key = pad ? "Gamepad_DPad_Down" : "G";
            var session = EditorSession.CreateOffline();
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            foreach (var ids in new[] { new[] { move, ability }, new[] { ability, move } })
            {
                config.Rules.Clear();
                var entries = workspace.AddFunctions(ids, Key(key));
                Check(entries.Length == 2 && entries.All(entry => entry.Trigger == "default"), "Mixed defaults must retain their UI semantics.");
                Check(Rule(config, entries.Single(entry => entry.FunctionId == move)).Trigger == "down", "A complete move must remain continuous in mixed selection.");
                Check(Rule(config, entries.Single(entry => entry.FunctionId == ability)).Trigger == "press", "A normal ability must stay one-shot in mixed selection, regardless of selection order.");
                File.WriteAllText(Path.Combine(directory, (pad ? "pad" : "keyboard") + "-mixed.lua"), config.CompileLua());
            }
            config.Rules.Clear();
            workspace.AddFunctions(new[] { move, ability }, Key(key, "press"));
            Check(config.Rules.Count == 1 && config.Rules[0].Trigger == "press" && !config.Rules[0].DefaultTrigger,
                "Explicit mixed triggers must retain one shared rule.");
            config.Rules.Clear();
            var explicitMapping = workspace.AddFunctions(new[] { "IA_Jump", "IA_Guard" }, Key(key, "down"));
            Check(explicitMapping.All(entry => entry.Trigger == "down" && !entry.Rule.DefaultTrigger),
                "Explicit continuous mapping triggers must not reopen as defaults.");
            var copiedMapping = workspace.CopyBinding(explicitMapping[0], ability);
            Check(Rule(config, copiedMapping).Trigger == "down" && !Rule(config, copiedMapping).DefaultTrigger,
                "Copying an explicit continuous mapping to an ability must preserve its trigger.");
            config.Rules.Clear();
            var all = workspace.AddFunctions(CompleteMoveCatalog.Targets.Select(item => item.Id), Key(key));
            Check(all.Length == 119 && config.Rules.Count == 1 && config.Rules[0].Trigger == "down", "Every complete move must share the continuous default.");
            config.Rules.Clear();
            var property = workspace.AddCustom(new KeyEnhancementOutput { Kind = "property", Property = "bFlag", Value = true }, Key(key), "状态");
            var repeated = workspace.Add(property.FunctionId, Key(key));
            Check(Rule(config, property).Trigger == "press" && Rule(config, repeated).Trigger == "press", "Custom property defaults must agree at first and subsequent addition.");
            var replaced = workspace.Replace(repeated, Key(key));
            Check(Rule(config, replaced).Trigger == "press", "Editing a custom property must not make it run every frame.");
            var customMove = workspace.AddCustom(new KeyEnhancementOutput { Kind = "move", Tag = move }, Key(key), "完整招式");
            config.Rules.Clear();
            var mixedCustom = workspace.AddFunctions(new[] { customMove.FunctionId, property.FunctionId }, Key(key));
            Check(Rule(config, mixedCustom.Single(entry => entry.FunctionId == customMove.FunctionId)).Trigger == "down"
                && Rule(config, mixedCustom.Single(entry => entry.FunctionId == property.FunctionId)).Trigger == "press",
                "Named custom functions must use their actual output kind in multi-select.");
            var imported = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "触发设置", config))).KeyEnhancement;
            Check(imported.Rules.Count == 2 && imported.Rules.All(rule => rule.DefaultTrigger), "Sharing must preserve distinct defaults.");
            var importedWorkspace = new BindingWorkspace(session, imported, imported.Clone(), pad, pad ? 0 : -1);
            var defaultEntry = importedWorkspace.Entries(property.FunctionId).Single();
            var copied = importedWorkspace.CopyBinding(defaultEntry, move);
            Check(Rule(imported, copied).DefaultTrigger && Rule(imported, copied).Trigger == "down", "Copying a default binding must resolve the destination default.");
            foreach (var output in new[] {
                new KeyEnhancementOutput { Kind="action", Path="/PBZInput/InputActions/IA_Jump.IA_Jump" },
                new KeyEnhancementOutput { Kind="action", Path="/SGameCombatSystem/Weapon/Input/IA_ChangeArmed.IA_ChangeArmed" },
                new KeyEnhancementOutput { Kind="ability", Tag="Ability.Action.Hun.Heavy.BronzeSword.BHold" },
                new KeyEnhancementOutput { Kind="event", Tag="Event.Trigger" },
                new KeyEnhancementOutput { Kind="function", Path="/Script/Engine.Character:Jump" },
                new KeyEnhancementOutput { Kind="lua", Code="function() return true end" },
                new KeyEnhancementOutput { Kind="property", Property="bFlag", Value=true }
            })
            {
                config.Rules.Clear();
                string expected = output.Kind == "action" && !output.Path.Contains("IA_ChangeArmed") ? "down" : "press";
                var first = workspace.AddCustom(output, Key(key), output.Kind);
                var second = workspace.Add(first.FunctionId, Key(key));
                var edited = workspace.Replace(second, Key(key));
                Check(Rule(config, first).Trigger == expected && Rule(config, edited).Trigger == expected
                    && first.Trigger == "default" && edited.Trigger == "default",
                    "A custom " + output.Kind + " must preserve the same default through adding, reopening and editing.");
            }
        }
        Console.WriteLine("BindingTriggerContractTests PASS " + checks);
    }
}
