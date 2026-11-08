using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using PBZKeyForge;

class AllMoveBindingContractTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Main(string[] args)
    {
        I18n.Current = "zh-Hans";
        Directory.CreateDirectory(args[0]);
        var speed = typeof(CompleteMoveCatalog).GetMethod("SupportsPhaseSpeed", BindingFlags.Static | BindingFlags.NonPublic);
        var session = EditorSession.CreateOffline();
        byte[] save = session.ExportCurrentScheme();
        foreach (bool pad in new[] { false, true })
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            var functions = workspace.Functions.Where(function => function.Targets.Any(target => target.Kind == "move" || target.Kind == "ability")).ToArray();
            Check(functions.Length >= CompleteMoveCatalog.Targets.Length, "The matrix must include every move and generic ability entrance.");
            var expected = new Dictionary<string, BindingEntry>();
            var groups = new Dictionary<int, List<string>>();
            var specifications = new Dictionary<int, BindingEntry>();
            for (int index = 0; index < functions.Length; index++)
            {
                var function = functions[index];
                bool hasSpeed = function.Targets.All(target => target.Kind != "move" || (bool)speed.Invoke(null, new object[] { target.Tag }));
                int group = index % 4;
                var entry = new BindingEntry {
                    Keys = new[] { pad ? "Gamepad_DPad_Down" : "G" }, Trigger = "default",
                    BypassResources = (group & 1) != 0, BypassRestrictions = (group & 2) != 0,
                    RestrictionBypasses = group == 2 ? new[] { "space", "training" } : new[] { "equipment", "training", "space", "stance", "predecessor", "cooldown" },
                    DamageMultiplier = 1.25 + group * .25, StartupSpeed = hasSpeed ? .5 + group * .5 : 1,
                    RecoverySpeed = hasSpeed ? 2.5 - group * .5 : 1
                };
                int batch = group + (hasSpeed ? 0 : 4);
                if (!groups.ContainsKey(batch)) { groups.Add(batch, new List<string>()); specifications.Add(batch, entry); }
                groups[batch].Add(function.Id);
                expected.Add(function.Id, entry);
            }
            foreach (int batch in groups.Keys) workspace.AddFunctions(groups[batch], specifications[batch]);
            var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "招式参数", config))).KeyEnhancement;
            var restored = KeyEnhancementConfiguration.FromJson(shared.ToJson());
            Check(restored.Rules.Sum(rule => rule.Outputs.Count) == functions.Length, "Sharing or JSON dropped a move binding.");
            foreach (var rule in restored.Rules)
            foreach (var output in rule.Outputs)
            {
                string id = output.Kind == "move" ? output.Tag : "ability:" + output.Tag;
                BindingEntry entry;
                Check(expected.TryGetValue(id, out entry), "A move identity changed in sharing: " + id);
                Check(rule.DefaultTrigger && rule.BypassResources == entry.BypassResources && rule.BypassRestrictions == entry.BypassRestrictions
                    && rule.RestrictionBypasses.SequenceEqual(entry.RestrictionBypasses) && rule.DamageMultiplier == entry.DamageMultiplier
                    && rule.StartupSpeed == entry.StartupSpeed && rule.RecoverySpeed == entry.RecoverySpeed,
                    "A move lost independent settings: " + id);
            }
            File.WriteAllText(Path.Combine(args[0], pad ? "all-moves-pad.lua" : "all-moves-keyboard.lua"), restored.CompileLua());
            foreach (var rule in restored.Rules)
            {
                rule.BypassRestrictions = true;
                rule.RestrictionBypasses = new[] { "equipment", "training", "space", "stance", "predecessor", "cooldown" };
            }
            File.WriteAllText(Path.Combine(args[0], pad ? "all-restrictions-pad.lua" : "all-restrictions-keyboard.lua"), restored.CompileLua());
            Console.WriteLine((pad ? "Controller" : "Keyboard") + " entrances=" + functions.Length);
        }
        Check(save.SequenceEqual(session.ExportCurrentScheme()), "Binding effects changed the native input save.");
        Console.WriteLine("AllMoveBindingContractTests PASS " + checks);
    }
}
