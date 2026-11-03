using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PBZKeyForge;

public static class UserPresetTests
{
    const string Profile = "PBZ.KeyProfile.Custom1";
    static int checks;
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static string[] Keys(EditorSession session, string action)
    { return session.Relations(action, false, -1).Keys.OrderBy(key => key).ToArray(); }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] fixture = File.ReadAllBytes(args[0]);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, fixture);
            var session = EditorSession.Open(input); session.SwitchProfile(Profile);
            session.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
            session.SetKeyboard("IA_HeavyAttack", 0, "None"); session.SetKeyboard("IA_HeavyAttack", 1, "None"); session.SetKeyboard("IA_HeavyAttack", 2, "None");
            session.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Left");
            int changes = session.ScopeChangeCount;
            var preset = UserPresetStore.Capture(session, "少主剑:攻防");
            Assert(preset.UserMappings.Count == 3, "Capture only changed actions/devices");
            Assert(preset.UserMappings.Single(mapping => mapping.Action == "IA_HeavyAttack").Removed.SequenceEqual(new[] { "F" }), "Pure unbinding retained");
            Assert(preset.UserMappings.Single(mapping => mapping.Action == "IA_Guard" && mapping.Family == null).Added.SequenceEqual(new[] { "LeftMouseButton" }), "Unchanged factory key omitted");
            string code = UserPresetStore.Share(preset); Console.WriteLine("share_length=" + code.Length + " " + code);
            var decoded = UserPresetStore.ParseShare("  " + code + "  ");
            Assert(decoded.Name == preset.Name && UserPresetStore.Share(decoded) == code, "Unicode name with colon and diff survive share roundtrip");
            var store = new UserPresetStore(Path.Combine(output, "presets"));
            foreach (var old in store.Load()) store.Delete(new[] { old });
            var saved = store.Save(decoded, decoded.Name);
            Assert(store.Load().Count == 1 && store.Load()[0].Id == saved.Id, "Save/reload identity");
            bool rejected = false;
            try { store.Save(decoded, decoded.Name); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && store.Load().Count == 1, "Duplicate name does not overwrite");
            var preview = session.PreviewPreset(saved);
            Assert(Keys(preview, "IA_HeavyAttack").Length == 0 && Keys(preview, "IA_Guard").Contains("LeftMouseButton"), "Isolated factory preview includes unbinding");
            Assert(session.ScopeChangeCount == changes && fixture.SequenceEqual(File.ReadAllBytes(input)), "Capture/share/preview never apply draft");
            session.SwitchProfile("PBZ.KeyProfile.Custom2"); session.SetKeyboard("IA_Jump", 0, "F11"); session.SwitchProfile(Profile);
            byte[] exported = session.ExportCurrentScheme();
            string exportPath = Path.Combine(output, "export.sav"); File.WriteAllBytes(exportPath, exported);
            var exportedSession = EditorSession.Open(exportPath); exportedSession.SwitchProfile(Profile);
            Assert(Keys(exportedSession, "IA_HeavyAttack").Length == 0 && Keys(exportedSession, "IA_Guard").Contains("LeftMouseButton"), "Export contains selected draft");
            exportedSession.SwitchProfile("PBZ.KeyProfile.Custom2");
            Assert(!Keys(exportedSession, "IA_Jump").Contains("F11"), "Export excludes other-scheme draft");
            Assert(session.ScopeChangeCount == changes && session.HasAnyDrafts && fixture.SequenceEqual(File.ReadAllBytes(input)), "Export does not apply or clear drafts");
            rejected = false;
            try { session.ExportCurrentScheme(input); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && fixture.SequenceEqual(File.ReadAllBytes(input)), "Export cannot overwrite the source save");
            session.Undo();
            foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
            {
                session.StagePreset(saved, mode);
                Assert(Keys(session, "IA_Guard").Contains("LeftMouseButton"), "Imported preset works in " + mode);
                Assert(Keys(session, "IA_HeavyAttack").Length == (mode == PresetApplyMode.Append ? 1 : 0), "Pure unbinding obeys " + mode);
                session.SelectPreset(null);
            }
            rejected = false;
            try { UserPresetStore.ParseShare("PBZ2:bad:!broken"); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Malformed code rejected");
            rejected = false;
            try { store.Delete(new[] { Presets.ById("onekey") }); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && store.Load().Count == 1, "Builtins cannot be deleted");
            var second = store.Save(decoded, "另一个"); store.Delete(new[] { saved, second });
            Assert(store.Load().Count == 0 && session.HasAnyDrafts, "Batch deletion keeps scheme drafts");
            session.Undo();
            for (int slot = 0; slot < 3; slot++) session.SetKeyboard("IA_ShadowArm_LongPress", slot, new[] { "F8", "F9", "F10" }[slot]);
            var fixedPreset = UserPresetStore.Capture(session, "武装长按");
            session.CheckPreset(fixedPreset, PresetApplyMode.Replace);
            session.StagePreset(fixedPreset, PresetApplyMode.Replace);
            Assert(Keys(session, "IA_ShadowArm_LongPress").SequenceEqual(new[] { "F10", "F8", "F9", "LeftControl" }), "Fixed Ctrl consumes no editable slot");
            rejected = false;
            try { UserPresetStore.Share(new Preset { UserName = "不能移除固定键", BaselineId = 1,
                Scopes = new List<PresetScope> { new PresetScope() }, UserMappings = new List<PresetMapping> {
                new PresetMapping { Action = "IA_ShadowArm_LongPress", Removed = new[] { "LeftControl" } } } }); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Fixed mapping removal rejected");
            Assert(fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Source fixture unchanged");
            Console.WriteLine("UserPresetTests PASS: " + checks + " checks; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
