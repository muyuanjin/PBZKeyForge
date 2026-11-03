using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PBZKeyForge;

public static class OfflineControllerTests
{
    const string Profile = "PBZ.KeyProfile.Custom1", OtherProfile = "PBZ.KeyProfile.Custom2";
    static int checks;
    static string root;

    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    static int Family(EditorSession session, string name)
    { return session.ControllerFamilies.Single(family => family.Name == name).Index; }

    static bool Has(EditorSession session, string family, string key)
    { return session.Relations("IA_Guard", true, Family(session, family)).Keys.Contains(key); }

    static byte[] ExceptFamily(SaveFile save, string profile, string family)
    {
        var isolated = SaveFile.FromBytes(save.ToBytes());
        var container = isolated.Props.FirstOrDefault(node => node.Name == "GamepadFamilyProfiles");
        if (container != null) container.Items.RemoveAll(fields => SaveFile.Field(fields, "ProfileId") == profile && SaveFile.Field(fields, "Family") == family);
        return isolated.ToBytes();
    }

    static EditorSession OpenBytes(byte[] bytes, string name)
    {
        string path = Path.Combine(root, name + ".sav"); File.WriteAllBytes(path, bytes);
        var session = EditorSession.Open(path); session.SwitchProfile(Profile); return session;
    }

    static void CheckEmptyControllers(byte[] fixture, bool removeProperty)
    {
        var save = SaveFile.FromBytes(fixture);
        var container = save.Props.Single(node => node.Name == "GamepadFamilyProfiles");
        if (removeProperty) save.Props.Remove(container); else container.Items.Clear();
        byte[] source = save.ToBytes();
        var session = OpenBytes(source, removeProperty ? "missing-property" : "empty-list");
        int xbox = Family(session, "Xbox"), ps = Family(session, "PS");
        Assert(xbox != ps && session.ControllerFamilies.Length == 2, "Both supported families are available without saved controller records");
        Assert(Has(session, "Xbox", "Gamepad_LeftShoulder") && Has(session, "PS", "Gamepad_LeftShoulder"), "Virtual controllers inherit factory defaults");
        Assert(!session.HasChanges && source.SequenceEqual(session.ExportCurrentScheme()), "Navigation and unchanged export do not materialize controller records");
        session.SwitchProfile(OtherProfile); session.SwitchProfile(Profile);
        Assert(Family(session, "Xbox") == xbox && Family(session, "PS") == ps, "Family selection survives scheme navigation");
        session.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
        var keyboardOnly = SaveFile.FromBytes(session.ExportCurrentScheme());
        Assert(keyboardOnly.GamepadProfiles.Count == 0, "Keyboard export does not add unrelated controller records");
        session.Undo();
        session.SetGamepadFamily("IA_Guard", 0, ps, "Gamepad_FaceButton_Top");
        session.SetGamepadFamily("IA_Guard", 0, ps, "Gamepad_FaceButton_Right");
        Assert(Has(session, "PS", "Gamepad_FaceButton_Right") && !Has(session, "Xbox", "Gamepad_FaceButton_Right"), "Virtual family edits are isolated");
        byte[] exported = session.ExportCurrentScheme();
        var result = SaveFile.FromBytes(exported);
        Assert(result.GamepadProfiles.Count == 1 && result.GamepadProfiles.Single().Family == "PS"
            && result.GamepadProfiles.Single().ProfileId == Profile, "Export materializes only the edited profile and canonical PS family");
        Assert(exported.SequenceEqual(result.ToBytes()), "New family serialization is byte stable");
        var reopened = OpenBytes(exported, removeProperty ? "reopen-missing" : "reopen-empty");
        Assert(Has(reopened, "PS", "Gamepad_FaceButton_Right") && Has(reopened, "Xbox", "Gamepad_LeftShoulder"), "New family bindings survive export and reopen");
        Assert(source.SequenceEqual(File.ReadAllBytes(session.InputPath)), "Editing and exporting leave source unchanged");
        session.SwitchProfile(OtherProfile);
        Assert(!Has(session, "PS", "Gamepad_FaceButton_Right"), "Controller edits do not leak to other schemes");
    }

    static void CheckEmptyKeys(byte[] fixture)
    {
        var save = SaveFile.FromBytes(fixture);
        save.GamepadProfiles.Single(family => family.ProfileId == Profile).Keys.Items.Clear();
        var session = OpenBytes(save.ToBytes(), "empty-keys");
        Assert(Family(session, "Xbox") >= 0 && Has(session, "Xbox", "Gamepad_LeftShoulder"), "An existing family with no key entries remains editable");
        session.SetGamepadFamily("IA_Guard", 0, Family(session, "Xbox"), "Gamepad_FaceButton_Left");
        var output = SaveFile.FromBytes(session.ExportCurrentScheme());
        var keys = output.GamepadProfiles.Single(family => family.ProfileId == Profile).Keys.Items;
        Assert(keys.Count == 1 && SaveFile.Field(keys[0], "Key") == "Gamepad_FaceButton_Left", "Editing an empty key list creates the registered slot without unrelated entries");
        Assert(ExceptFamily(save, Profile, "Xbox").SequenceEqual(ExceptFamily(output, Profile, "Xbox")), "Empty-family edits preserve unrelated serialized data");
    }

    static void CheckOffline()
    {
        var session = EditorSession.CreateOffline(); session.SwitchProfile(Profile);
        Assert(session.IsOffline && session.InputPath == null && session.ActiveProfile == null, "Offline creation has no source path or active game selector");
        Assert(session.ControllerFamilies.Select(family => family.Name).OrderBy(name => name).SequenceEqual(new[] { "PS", "Xbox" }), "Offline session exposes Xbox and PlayStation");
        session.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
        session.SetGamepadFamily("IA_Guard", 0, Family(session, "PS"), "Gamepad_FaceButton_Left");
        var captured = UserPresetStore.Capture(session, "双防御");
        var scoped = UserPresetStore.SelectScopes(captured, captured.Scopes.Where(scope => scope.Family == "PS"));
        string code = UserPresetStore.Share(scoped);
        var imported = UserPresetStore.ParseShare(code);
        Assert(imported.Scopes.Single().Family == "PS" && imported.UserMappings.Single().Family == "PS", "Offline share retains explicit PS-only scope");
        foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
        {
            var target = EditorSession.CreateOffline(); target.SwitchProfile(Profile);
            target.SetKeyboard("IA_Jump", 0, "F12");
            target.SetGamepadFamily("IA_Guard", 0, Family(target, "Xbox"), "Gamepad_FaceButton_Right");
            target.StagePreset(imported, mode);
            Assert(Has(target, "PS", "Gamepad_FaceButton_Left"), "PS share works offline in " + mode);
            Assert(Has(target, "Xbox", "Gamepad_FaceButton_Right") && target.Relations("IA_Jump", false, -1).Keys.Contains("F12"), "PS share preserves other devices in " + mode);
            var preview = target.PreviewPreset(imported);
            Assert(Has(preview, "PS", "Gamepad_FaceButton_Left") && Has(target, "Xbox", "Gamepad_FaceButton_Right"), "Preview is isolated in " + mode);
            string exportPath = Path.Combine(root, "offline-" + mode + ".sav"); target.ExportCurrentScheme(exportPath);
            var reopened = EditorSession.Open(exportPath); reopened.SwitchProfile(Profile);
            Assert(Has(reopened, "PS", "Gamepad_FaceButton_Left") && Has(reopened, "Xbox", "Gamepad_FaceButton_Right"), "Offline export reopens in " + mode);
        }
        int count = session.ScopeChangeCount;
        var apply = session.Apply();
        Assert(!apply.Success && !apply.BindingsSaved && session.ScopeChangeCount == count, "Offline Apply cannot write or clear drafts");
        session.Refresh(); session.Undo();
        Assert(!session.HasChanges, "Offline refresh and undo need no files");
        session.SelectPreset("souls");
        Assert(session.ControllerFamilies.All(family => session.Relations("IA_LightAttack", true, family.Index).Keys.Contains("Gamepad_RightShoulder")), "Built-in controller presets cover both offline families");
        session.SwitchProfile(OtherProfile); session.SetKeyboard("IA_Jump", 0, "F11"); session.ResetAll();
        session.SwitchProfile(Profile);
        Assert(!session.HasAnyDrafts && !session.IsResetPending && Has(session, "PS", "Gamepad_LeftShoulder"), "Offline factory reset clears every scheme without requiring Apply");
        session.SetKeyboard("IA_Jump", 0, "F10");
        Assert(session.HasChanges, "Editing remains available after an offline reset");
        string gameDirectory = Path.Combine(root, "game-save-target"); Directory.CreateDirectory(gameDirectory);
        string gameInput = Path.Combine(gameDirectory, "EnhancedInputUserSettings.sav");
        byte[] original = session.ExportCurrentScheme(); File.WriteAllBytes(gameInput, original);
        File.WriteAllText(Path.Combine(gameDirectory, "PBZSharedSetting.sav"), "fixture");
        bool rejected = false;
        try { session.ExportCurrentScheme(gameInput); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && original.SequenceEqual(File.ReadAllBytes(gameInput)), "Offline export cannot bypass protected game-save application");
        string regularExport = Path.Combine(root, "replaceable-export.sav"); File.WriteAllText(regularExport, "old export");
        session.ExportCurrentScheme(regularExport);
        Assert(original.SequenceEqual(File.ReadAllBytes(regularExport)), "A normal exported file can be replaced");
    }

    static void CheckEmptyKeyPresets(byte[] fixture)
    {
        var template = SaveFile.FromBytes(fixture);
        var xbox = template.GamepadProfiles.Single(family => family.ProfileId == Profile);
        xbox.Keys.Items.Clear();
        var psFields = xbox.Fields.Select(SaveFile.CloneNode).ToList();
        SaveFile.SetField(psFields, "Family", "PS");
        template.Props.Single(node => node.Name == "GamepadFamilyProfiles").Items.Add(psFields);
        var psOnly = new Preset { UserName = "PS防御", BaselineId = 1,
            Scopes = new List<PresetScope> { new PresetScope { Family = "PS" } },
            UserMappings = new List<PresetMapping> { new PresetMapping { Action = "IA_Guard", Family = "PS", Added = new[] { "Gamepad_FaceButton_Left" } } } };
        psOnly = UserPresetStore.ParseShare(UserPresetStore.Share(psOnly));
        foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
        {
            foreach (string presetId in new[] { "souls", "onekey" })
            {
                var save = SaveFile.FromBytes(template.ToBytes());
                if (presetId == "souls" && mode == PresetApplyMode.Append)
                {
                    byte[] before = save.ToBytes(); bool rejected = false;
                    try { Presets.Apply(save, Presets.ById(presetId), save.RecordFor(Profile), mode); }
                    catch (InvalidOperationException) { rejected = true; }
                    Assert(rejected && before.SequenceEqual(save.ToBytes()), "Append respects the single sprint slot and leaves empty-family input unchanged");
                    continue;
                }
                Presets.Apply(save, Presets.ById(presetId), save.RecordFor(Profile), mode);
                var session = OpenBytes(save.ToBytes(), "empty-preset-" + presetId + mode);
                string action = presetId == "souls" ? "IA_LightAttack" : "IA_Guard";
                string key = presetId == "souls" ? "Gamepad_RightShoulder" : "Gamepad_FaceButton_Left";
                Assert(session.ControllerFamilies.All(family => session.Relations(action, true, family.Index).Keys.Contains(key)), "Built-in " + presetId + " works with empty controller keys in " + mode);
            }
            var target = SaveFile.FromBytes(template.ToBytes());
            Presets.Apply(target, psOnly, target.RecordFor(Profile), mode);
            var reopened = OpenBytes(target.ToBytes(), "empty-ps-import-" + mode);
            Assert(Has(reopened, "PS", "Gamepad_FaceButton_Left") && Has(reopened, "Xbox", "Gamepad_LeftShoulder"), "PS-only preset works with empty keys and preserves Xbox in " + mode);
            Assert(target.GamepadProfiles.Single(family => family.ProfileId == Profile && family.Family == "Xbox").Keys.Items.Count == 0,
                "PS-only import does not materialize unrelated empty Xbox keys in " + mode);
        }
        var direct = SaveFile.FromBytes(template.ToBytes());
        Presets.Apply(direct, Presets.ById("souls"), direct.RecordFor(Profile));
        Assert(direct.GamepadProfiles.Where(family => family.ProfileId == Profile).All(family => family.Keys.Items.Count > 0), "Direct built-in application creates mappings for empty families");
    }

    static int? StorageVersion(SaveFile save)
    {
        var version = save.Props.SingleOrDefault(node => node.Name == "GamepadFamilyStorageVersion");
        return version == null ? (int?)null : BitConverter.ToInt32(version.Raw, 0);
    }

    static void CheckUninitializedStorage(byte[] fixture)
    {
        var psOnly = new Preset { UserName = "PS防御", BaselineId = 1,
            Scopes = new List<PresetScope> { new PresetScope { Family = "PS" } },
            UserMappings = new List<PresetMapping> { new PresetMapping { Action = "IA_Guard", Family = "PS", Added = new[] { "Gamepad_FaceButton_Left" } } } };
        foreach (bool missingVersion in new[] { false, true })
            foreach (bool emptyExistingKeys in new[] { false, true })
            {
                var save = SaveFile.FromBytes(fixture);
                var version = save.Props.Single(node => node.Name == "GamepadFamilyStorageVersion");
                if (missingVersion) save.Props.Remove(version); else version.Raw = BitConverter.GetBytes(0);
                if (emptyExistingKeys)
                {
                    foreach (var family in save.GamepadProfiles) family.Keys.Items.Clear();
                    var fields = save.GamepadProfiles.Single(family => family.ProfileId == Profile).Fields.Select(SaveFile.CloneNode).ToList();
                    SaveFile.SetField(fields, "Family", "PS");
                    save.Props.Single(node => node.Name == "GamepadFamilyProfiles").Items.Add(fields);
                }
                else save.Props.RemoveAll(node => node.Name == "GamepadFamilyProfiles");
                byte[] original = save.ToBytes();
                var session = OpenBytes(original, "storage-" + missingVersion + emptyExistingKeys);
                Assert(original.SequenceEqual(session.ExportCurrentScheme()), "Browsing does not initialize controller storage");
                session.SetKeyboard("IA_Jump", 0, "F11");
                Assert(StorageVersion(SaveFile.FromBytes(session.ExportCurrentScheme())) == (missingVersion ? (int?)null : 0), "Keyboard-only export preserves the missing or zero controller version");
                session.Undo();
                session.SetGamepadFamily("IA_Guard", 0, Family(session, emptyExistingKeys ? "Xbox" : "PS"), "Gamepad_FaceButton_Left");
                var output = SaveFile.FromBytes(session.ExportCurrentScheme());
                Assert(StorageVersion(output) == 1, "Manual controller export initializes missing or zero storage version");
                Assert(original.SequenceEqual(File.ReadAllBytes(session.InputPath)), "Storage initialization never mutates the source save");
                foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
                {
                    var planned = SaveFile.FromBytes(original);
                    Presets.Apply(planned, psOnly, planned.RecordFor(Profile), mode);
                    Assert(StorageVersion(planned) == 1, "Scoped preset initializes controller storage in " + mode);
                }
                var builtin = SaveFile.FromBytes(original);
                Presets.Apply(builtin, Presets.ById("onekey"), builtin.RecordFor(Profile));
                Assert(StorageVersion(builtin) == 1, "Direct additive preset initializes controller storage");
                builtin = SaveFile.FromBytes(original);
                Presets.Apply(builtin, Presets.ById("souls"), builtin.RecordFor(Profile));
                Assert(StorageVersion(builtin) == 1, "Direct replacement preset initializes controller storage");
            }
    }

    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
            byte[] fixture = File.ReadAllBytes(args[0]);
            CheckEmptyControllers(fixture, false); CheckEmptyControllers(fixture, true);
            CheckEmptyKeys(fixture); CheckOffline(); CheckEmptyKeyPresets(fixture);
            CheckUninitializedStorage(fixture);
            Assert(fixture.SequenceEqual(File.ReadAllBytes(args[0])), "The fixture remains unchanged");
            Console.WriteLine("OfflineControllerTests PASS: " + checks + " checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
