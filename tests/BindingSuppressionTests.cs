using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PBZKeyForge;

class BindingSuppressionTests
{
    static int checks;
    static string output;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static BindingEntry Key(string key, string trigger = "default")
    { return new BindingEntry { Keys = new[] { key }, Trigger = trigger, Seconds = .6 }; }
    static void Export(string name, KeyEnhancementConfiguration config)
    { File.WriteAllText(Path.Combine(output, name + ".lua"), config.CompileLua(), new System.Text.UTF8Encoding(false)); }

    static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            foreach (bool pad in new[] { false, true }) Overrides(pad);
            foreach (bool runtime in new[] { false, true })
                foreach (bool swap in new[] { false, true }) Move(runtime, swap);
            FixedMovement();
            Console.WriteLine("BindingSuppressionTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void Overrides(bool pad)
    {
        var session = EditorSession.CreateOffline();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), pad, pad ? session.ControllerFamilies.First().Index : -1) { LiveEditing = true };
        string first = pad ? "Gamepad_FaceButton_Left" : "J", second = pad ? "Gamepad_FaceButton_Top" : "K";
        string prefix = pad ? "pad-" : "keyboard-";
        var original = ws.Entries("IA_Jump").First(entry => entry.IsNative);
        ws.Replace(original, Key(first)); ws.Add("IA_Jump", Key(second));
        var hold = ws.Replace(ws.Entries("IA_Jump").Single(entry => entry.IsNative && entry.Keys.Contains(first)), Key(first, "hold"));
        Check(ws.Entries("IA_Jump").Single(entry => entry.IsNative && entry.Keys.Contains(first)).Suppressed, "Hold did not suppress plain override");
        Export(prefix + "hold", config);
        ws.SetEnabled(hold, false); Export(prefix + "disabled-pass", config);
        config.Rules.Single().BlockOriginalWhenDisabled = true; Export(prefix + "disabled-block", config);
        config.Rules.Clear();
        foreach (var target in ws.Functions.Single(item => item.Id == "IA_Jump").Targets)
            config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = target.Path });
        Export(prefix + "global", config);
        config.Suppressions.Clear();
        var guard = ws.Entries("IA_Guard").First(entry => entry.IsNative); ws.Replace(guard, Key(first));
        var guardTargets = ws.Functions.Single(item => item.Id == "IA_Guard").Targets;
        config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = guardTargets[0].Path.Split('.')[0], Key = first });
        Export(prefix + "partial", config);
    }

    static void Move(bool runtime, bool swap)
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1) { LiveEditing = runtime };
        byte[] bytes = session.ExportCurrentScheme();
        var jump = ws.Entries("IA_Jump").First(entry => entry.IsNative && entry.Keys.Contains("SpaceBar"));
        ws.Replace(jump, Key("SpaceBar", "hold"));
        var rule = config.Rules.Single();
        var path = ws.Functions.Single(item => item.Id == "IA_Jump").Targets.First().Path;
        config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = path, Key = "J" });
        config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Game/Unrelated", Key = "Q" });
        config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Game/Global" });
        if (swap) ws.Add("IA_Jump", Key("J"));
        ws.MoveKey("SpaceBar", "J", swap);
        Check(rule.Keys.SequenceEqual(new[] { "J" }) && rule.Suppressions.All(item => item.Key == "J"), "Moved hold lost its keyed suppression");
        Check(config.Suppressions[0].Key == (swap ? "SpaceBar" : "J"), "Global keyed suppression did not follow move/swap");
        Check(config.Suppressions[1].Key == "Q" && config.Suppressions[2].Key == null, "Unrelated or action-wide suppression changed");
        Check(ws.Entries("IA_Jump").Single(entry => entry.IsNative && entry.Keys.Contains("J")).Suppressed, "Moved plain binding reactivated");
        if (runtime) Check(bytes.SequenceEqual(session.ExportCurrentScheme()), "Runtime move wrote native save");
        Export((runtime ? "runtime-" : "offline-") + (swap ? "swap" : "move"), config);
    }

    static void FixedMovement()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(session, config, config.Clone(), false, -1);
        ws.Replace(ws.Entries("movement:forward").Single(entry => entry.IsNative && entry.Keys.Contains("W")), Key("W", "hold"));
        ws.MoveKey("W", "J", false);
        Check(config.Rules.Count == 1 && config.Rules.Single().Keys.SequenceEqual(new[] { "J" }), "Moving fixed hold added an immediate output");
        Check(ws.Entries("movement:forward").Single(entry => entry.IsNative && entry.Keys.Contains("W")).Suppressed, "Moving fixed hold revived original W");
        config.CompileLua();
    }
}
