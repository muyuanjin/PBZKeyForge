using System;
using System.IO;
using System.Linq;
using PBZKeyForge;

public static class MouseGuardPresetTests
{
    const string Profile = "PBZ.KeyProfile.Custom1";
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static int Main(string[] args)
    {
        try
        {
            byte[] fixture = File.ReadAllBytes(args[0]);
            var before = SaveFile.FromBytes(fixture);
            string root = Path.Combine(Path.GetTempPath(), "PBZKeyForge-mouse-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "EnhancedInputUserSettings.sav");
            File.WriteAllBytes(path, fixture);
            var session = EditorSession.Open(path); session.SwitchProfile(Profile);
            session.SelectPreset("mouse_guard");
            Assert(session.GamepadChangeCount == 0, "Preview changed controller bindings.");
            Assert(session.Relations("IA_LightAttack", false, -1).Keys.SequenceEqual(new[] { "LeftMouseButton" }), "Left click must light attack.");
            Assert(session.Relations("IA_Guard", false, -1).Keys.OrderBy(key => key).SequenceEqual(new[] { "LeftMouseButton", "RightMouseButton" }), "Both mouse buttons must guard.");
            Assert(session.Relations("IA_HeavyAttack", false, -1).Keys.OrderBy(key => key).SequenceEqual(new[] { "F", "RightMouseButton" }), "F and right click must heavy attack.");
            session.SelectPreset(null);
            Assert(!session.HasChanges && fixture.SequenceEqual(File.ReadAllBytes(path)), "Cancelling preview changed the save or left a draft.");
            session.SelectPreset("mouse_guard");
            var result = session.Apply(); Assert(result.Success, result.Error);
            var after = SaveFile.Load(path);
            Assert(after.RecordFor(Profile).Entries.Where(entry => entry.Name == "IA_HeavyAttack" || entry.Name == "IA_Guard")
                .Where(entry => !before.RecordFor(Profile).Entries.Any(old => old.Name == entry.Name && old.Slot == entry.Slot && old.Key == entry.Key))
                .All(entry => !InputDefaults.RegisteredSlots(entry.Name, true).Contains(entry.Slot)), "Mouse preset writes only keyboard slots.");
            Assert(after.ToBytes().SequenceEqual(File.ReadAllBytes(path)), "Preset output must roundtrip identically.");
            var reopened = EditorSession.Open(path); reopened.SwitchProfile(Profile);
            Assert(reopened.Relations("IA_HeavyAttack", false, -1).Keys.OrderBy(key => key).SequenceEqual(new[] { "F", "RightMouseButton" }), "Save/reopen lost F or right-click heavy attack.");
            byte[] once = after.ToBytes();
            Presets.Apply(after, Presets.ById("mouse_guard"), after.RecordFor(Profile), PresetApplyMode.Replace);
            Assert(once.SequenceEqual(after.ToBytes()), "Repeated application must be idempotent.");
            string[] actions = { "IA_LightAttack", "IA_HeavyAttack", "IA_Guard" };
            after.RecordFor(Profile).Entries.RemoveAll(entry => actions.Contains(entry.Name) && !InputDefaults.RegisteredSlots(entry.Name, true).Contains(entry.Slot));
            before.RecordFor(Profile).Entries.RemoveAll(entry => actions.Contains(entry.Name) && !InputDefaults.RegisteredSlots(entry.Name, true).Contains(entry.Slot));
            Assert(before.ToBytes().SequenceEqual(after.ToBytes()), "Preset changed other actions, profiles, controller bindings or unrelated data.");
            var direct = SaveFile.FromBytes(fixture);
            Presets.Apply(direct, Presets.ById("mouse_guard"), direct.RecordFor(Profile), PresetApplyMode.Replace);
            Assert(once.SequenceEqual(direct.ToBytes()), "GUI session and direct preset application differ.");
            var languages = args.Contains("--all-languages") ? I18n.Languages : I18n.Languages.Take(1).ToArray();
            foreach (string language in languages)
            {
                I18n.Current = language;
                Assert(!Presets.ById("mouse_guard").Name.StartsWith("preset.") && !Presets.ById("mouse_guard").Desc.StartsWith("preset."), "Missing preset translation: " + language);
            }
            Assert(fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Original fixture changed.");
            Console.WriteLine("MouseGuardPresetTests PASS: preview, F preservation, save/reopen, scope isolation, idempotence, GUI/direct parity; " + languages.Length + " languages.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
