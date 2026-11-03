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
            Assert(session.GetKeyboard("IA_LightAttack", 0) == "LeftMouseButton" && session.GetKeyboard("IA_Guard", 1) == "LeftMouseButton", "Left click must light attack and guard.");
            Assert(session.GetKeyboard("IA_HeavyAttack", 2) == "RightMouseButton" && session.GetKeyboard("IA_Guard", 0) == "RightMouseButton", "Right click must heavy attack and guard.");
            Assert(session.GetKeyboard("IA_HeavyAttack", 0) == "F" && session.GetKeyboard("IA_HeavyAttack", 1) == "None", "F must retain heavy attack without duplicate slots.");
            session.SelectPreset(null);
            Assert(!session.HasChanges && fixture.SequenceEqual(File.ReadAllBytes(path)), "Cancelling preview changed the save or left a draft.");
            session.SelectPreset("mouse_guard");
            var result = session.Apply(); Assert(result.Success, result.Error);
            var after = SaveFile.Load(path);
            Assert(after.RecordFor(Profile).Entries.Where(entry => entry.Name == "IA_HeavyAttack").OrderBy(entry => entry.Slot)
                .Select(entry => entry.Slot + ":" + entry.Key).SequenceEqual(new[] { "0:F", "2:None", "4:RightMouseButton" }),
                "Heavy attack must override each registered raw slot and preserve explicit unbinding.");
            Assert(after.RecordFor(Profile).Entries.Where(entry => entry.Name == "IA_Guard").OrderBy(entry => entry.Slot)
                .Select(entry => entry.Slot + ":" + entry.Key).SequenceEqual(new[] { "0:RightMouseButton", "2:LeftMouseButton", "4:None" }),
                "Mouse guard must use the action's raw keyboard slots without touching controller slots.");
            Assert(after.ToBytes().SequenceEqual(File.ReadAllBytes(path)), "Preset output must roundtrip identically.");
            var reopened = EditorSession.Open(path); reopened.SwitchProfile(Profile);
            Assert(reopened.GetKeyboard("IA_HeavyAttack", 0) == "F" && reopened.GetKeyboard("IA_HeavyAttack", 2) == "RightMouseButton", "Save/reopen lost F or right-click heavy attack.");
            byte[] once = after.ToBytes();
            Presets.Apply(after, Presets.ById("mouse_guard"), after.RecordFor(Profile));
            Assert(once.SequenceEqual(after.ToBytes()), "Repeated application must be idempotent.");
            string[] actions = { "IA_LightAttack", "IA_HeavyAttack", "IA_Guard" };
            after.RecordFor(Profile).Entries.RemoveAll(entry => actions.Contains(entry.Name));
            Assert(before.ToBytes().SequenceEqual(after.ToBytes()), "Preset changed other actions, profiles, controller bindings or unrelated data.");
            var direct = SaveFile.FromBytes(fixture);
            Presets.Apply(direct, Presets.ById("mouse_guard"), direct.RecordFor(Profile));
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
