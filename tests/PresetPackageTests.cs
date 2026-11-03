using System;
using System.IO;
using System.Linq;
using PBZKeyForge;

public static class PresetPackageTests
{
    static int checks;
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    static string MarkdownCell(string text)
    { return text.Replace("|", "&#124;").Replace("\r", "").Replace("\n", "<br>"); }

    static void CompareDevice(EditorSession expected, EditorSession actual, bool pad, int family, string context)
    {
        foreach (string action in expected.BindingActions(pad, family).Union(actual.BindingActions(pad, family)))
        {
            var before = expected.Relations(action, pad, family);
            var after = actual.Relations(action, pad, family);
            Assert(before.Keys.OrderBy(key => key).SequenceEqual(after.Keys.OrderBy(key => key)),
                context + ": " + (pad ? "controller " + family : "keyboard") + " " + action);
        }
    }

    static void CompareScheme(EditorSession expected, EditorSession actual, string context)
    {
        CompareDevice(expected, actual, false, -1, context);
        Assert(expected.ControllerFamilies.Select(f => f.Index + ":" + f.Name)
            .SequenceEqual(actual.ControllerFamilies.Select(f => f.Index + ":" + f.Name)), context + ": controller families");
        foreach (var family in expected.ControllerFamilies) CompareDevice(expected, actual, true, family.Index, context);
    }

    static void MouseSpaceKeys(EditorSession session)
    {
        var expected = new[] {
            new[] { "IA_LightAttack", "LeftMouseButton" },
            new[] { "IA_Guard", "LeftMouseButton", "RightMouseButton" },
            new[] { "IA_HeavyAttack", "F", "RightMouseButton" },
            new[] { "IA_Dodge", "LeftShift", "SpaceBar" },
            new[] { "IA_MoveFast", "LeftShift", "SpaceBar" },
            new[] { "IA_Jump", "SpaceBar" }
        };
        foreach (var keys in expected)
            Assert(session.Relations(keys[0], false, -1).Keys.OrderBy(key => key).SequenceEqual(keys.Skip(1).OrderBy(key => key)),
                "Mouse + Space preset has incorrect bindings for " + keys[0]);
    }

    static void Verify(string input, string directory, string scratch, bool factoryInput)
    {
        Directory.CreateDirectory(scratch);
        byte[] original = File.ReadAllBytes(input);
        var source = SaveFile.FromBytes(original);
        string profile = source.ActiveProfile;
        var presets = Presets.All().Where(preset => !preset.CurrentProfileVanilla).ToArray();
        Assert(presets.Count(preset => preset.Vanilla) == 1, "The package must contain one factory layout.");
        Assert(Directory.GetFiles(directory, "*.sav").Select(Path.GetFileName).OrderBy(name => name)
            .SequenceEqual(presets.Select(p => "PBZKeyForge-" + p.Id + ".sav").OrderBy(name => name)), "Packaged preset list differs from the editor catalog.");
        string index = File.ReadAllText(Path.Combine(directory, "PRESETS.md"));
        Assert(index.Contains(profile) && index.Contains("Preset only") && index.Contains("仅用预设"), "Preset index must identify its scheme and application mode.");
        foreach (var preset in presets)
        {
            string name = "PBZKeyForge-" + preset.Id + ".sav";
            string path = Path.Combine(directory, name);
            var packagedSave = SaveFile.Load(path);
            Assert(packagedSave.ToBytes().SequenceEqual(File.ReadAllBytes(path)), name + ": round-trip differs");
            Assert(Presets.FactoryPreview(source, preset, profile).ToBytes().SequenceEqual(File.ReadAllBytes(path)), name + ": generated save differs from factory preset definition");
            string row = index.Split('\n').Single(line => line.StartsWith("| `" + name + "` |"));
            foreach (string language in new[] { "en", "zh-Hans" })
            {
                I18n.Current = language;
                Assert(row.Contains(MarkdownCell(preset.Name)) && row.Contains(MarkdownCell(preset.Desc)), name + ": index name or description differs from the editor");
            }
            I18n.Current = "zh-Hans";
            string working = Path.Combine(scratch, name);
            File.WriteAllBytes(working, original);
            var session = EditorSession.Open(working); session.SwitchProfile(profile);
            var packaged = EditorSession.Open(path); packaged.SwitchProfile(profile);
            if (preset.Id == "mouse_guard_space") MouseSpaceKeys(packaged);
            CompareScheme(packaged, session.PreviewPreset(preset.Id), preset.Id + " hover");
            if (preset.Vanilla) session.ResetAll();
            else
            {
                foreach (PresetApplyMode mode in new[] { PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
                {
                    if (!factoryInput && mode != PresetApplyMode.OnlyPreset) continue;
                    session.SelectPreset(preset.Id, mode);
                    CompareScheme(packaged, session, preset.Id + " " + mode);
                }
            }
            Assert(original.SequenceEqual(File.ReadAllBytes(working)), name + ": staging wrote the save");
            var result = session.Apply(); Assert(result.Success, name + ": " + result.Error);
            var reopened = EditorSession.Open(working);
            foreach (string scheme in reopened.Profiles)
            {
                reopened.SwitchProfile(scheme); packaged.SwitchProfile(scheme);
                CompareScheme(packaged, reopened, preset.Id + " saved " + scheme);
            }
        }
        Assert(original.SequenceEqual(File.ReadAllBytes(input)), "Preset checks changed their source input.");
    }

    public static int Main(string[] args)
    {
        string language = I18n.Current;
        try
        {
            string output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            Verify(args[0], args[1], Path.Combine(output, "factory"), true);
            var customized = SaveFile.Load(args[0]);
            var custom = new Preset(); custom.Keyboard.Add(new KeyBinding("IA_Jump", 0, "F10"));
            foreach (var record in customized.Records) Presets.Apply(customized, custom, record);
            string customInput = Path.Combine(output, "custom-input.sav"); customized.Save(customInput);
            string customDirectory = Path.Combine(output, "custom-presets");
            Directory.CreateDirectory(customDirectory);
            File.WriteAllBytes(Path.Combine(customDirectory, "PBZKeyForge-vanilla_current.sav"), customized.ToBytes());
            Assert(Cli.Run(new[] { "--gen-presets", customInput, customDirectory }) == 0, "Custom input generation failed.");
            Assert(I18n.Current == "zh-Hans", "Preset generation changed the selected language.");
            Verify(customInput, customDirectory, Path.Combine(output, "custom"), false);
            Console.WriteLine("PresetPackageTests PASS: " + checks + " checks; " + Presets.All().Count(preset => !preset.CurrentProfileVanilla)
                + " packaged presets; factory and customized inputs; one factory layout.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { I18n.Current = language; }
    }
}
