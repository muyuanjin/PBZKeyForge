using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PBZKeyForge;

// Compile against the application sources with /main:EditorSessionTests; see tests/README.md.
// Every write is made to a freshly created fixture copy; original game saves are never inputs.
public static class EditorSessionTests
{
    const string Custom1 = "PBZ.KeyProfile.Custom1", Custom2 = "PBZ.KeyProfile.Custom2";
    static string fixture, root;
    static int assertions;
    static readonly int[] GuardKeyboardSlots = { 0, 2, 4 };
    static readonly int[] GuardGamepadSlots = { 1, 3 };

    static int[] KeyboardSlots(SaveFile save, string profile, string action)
    {
        return InputDefaults.Slots(action, false, save.RecordFor(profile).Entries.Where(entry => entry.Name == action && entry.Slot < 7).Select(entry => (int)entry.Slot),
            save.GamepadKeysFor(profile).SelectMany(keys => keys.Items).Where(item => SaveFile.Field(item, "MappingName") == action)
                .Select(item => InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))));
    }

    static int[] GamepadSlots(SaveFile save, string profile, Node keys, string action)
    { return InputDefaults.RegisteredSlots(action, true); }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string name, string existing, IntPtr security);

    static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Assertion failed: " + message);
    }

    static string NewInput(string name)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "EnhancedInputUserSettings.sav");
        var save = SaveFile.Load(fixture);
        SetKeyboard(save, Custom1, "IA_Guard", 0, "RightMouseButton");
        SetKeyboard(save, Custom1, "IA_Jump", 0, "SpaceBar");
        SetKeyboard(save, Custom2, "IA_Jump", 0, "SpaceBar");
        save.Save(path);
        return path;
    }

    static void SetKeyboard(SaveFile save, string profile, string action, int slot, string value)
    {
        var record = save.RecordFor(profile);
        var entry = record.Entries.FirstOrDefault(e => e.Name == action && e.Slot == slot);
        if (entry == null) { entry = new BlobEntry { Name = action, Slot = (byte)slot, X = "None", Y = "None" }; record.Entries.Add(entry); }
        entry.Key = value;
    }

    static string Keyboard(string path, string profile, string action, int slot)
    {
        var entry = SaveFile.Load(path).RecordFor(profile).Entries.FirstOrDefault(e => e.Name == action && e.Slot == slot);
        return entry == null ? null : entry.Key;
    }

    static void ExternalKeyboard(string path, string profile, string action, int slot, string key)
    {
        var save = SaveFile.Load(path); SetKeyboard(save, profile, action, slot, key); save.Save(path);
    }

    static EditorSession Open(string path)
    {
        var session = EditorSession.Open(path); session.SwitchProfile(Custom1); return session;
    }

    static byte[] ProfileSnapshot(SaveFile save, string profile)
    {
        SaveFile isolated = SaveFile.FromBytes(save.ToBytes());
        isolated.Records.RemoveAll(record => record.ProfileId != profile);
        Node profiles = isolated.Props.First(node => node.Name == "GamepadFamilyProfiles");
        profiles.Items.RemoveAll(fields => SaveFile.Field(fields, "ProfileId") != profile);
        return isolated.ToBytes();
    }

    static List<Node> PadFor(SaveFile save)
    { return save.GamepadKeysFor(Custom1); }

    static void AddFamily(SaveFile save, string profile, string family)
    {
        var original = save.GamepadProfiles.First(item => item.ProfileId == profile);
        var fields = original.Fields.Select(SaveFile.CloneNode).ToList();
        SaveFile.SetField(fields, "Family", family);
        save.Props.First(node => node.Name == "GamepadFamilyProfiles").Items.Add(fields);
    }

    static void ApplyTarget(SaveFile save, Preset preset)
    {
        string defaultId = "InputUserSettings.Profiles.Default";
        byte[] beforeDefault = ProfileSnapshot(save, defaultId), beforeCustom2 = ProfileSnapshot(save, Custom2);
        try { Presets.Apply(save, preset, save.RecordFor(Custom1)); }
        finally
        {
            Assert(beforeDefault.SequenceEqual(ProfileSnapshot(save, defaultId)), preset.Id + " leaves Default keyboard and gamepad fields byte-identical");
            Assert(beforeCustom2.SequenceEqual(ProfileSnapshot(save, Custom2)), preset.Id + " leaves Custom2 keyboard and gamepad fields byte-identical");
        }
    }

    static void ExternalPad(string path, string profile, string action, int slot, string value)
    {
        var save = SaveFile.Load(path);
        string name = InputDefaults.SlotName(GamepadSlots(save, profile, save.GamepadKeysFor(profile).First(), action)[slot]);
        var item = save.GamepadKeysFor(profile).First().Items.First(entry => SaveFile.Field(entry, "MappingName") == action && SaveFile.Field(entry, "Slot") == name);
        SaveFile.SetField(item, "Key", value); save.Save(path);
    }

    static void RefreshAndConflicts()
    {
        string path = NewInput("refresh"); var session = Open(path);
        session.SetKeyboard("IA_Guard", 0, "F8");
        ExternalKeyboard(path, Custom1, "IA_Jump", 0, "F9");
        session.Refresh();
        Assert(session.GetKeyboard("IA_Jump", 0) == "F9", "refresh reads external binding");
        Assert(session.GetKeyboard("IA_Guard", 0) == "F8", "refresh keeps unrelated local edit");
        Assert(session.Conflicts.Length == 0, "unrelated edit is not a conflict");
        ExternalKeyboard(path, Custom1, "IA_Guard", 0, "F10"); session.Refresh();
        Assert(session.Conflicts.Length == 1, "same-binding edits conflict");
        Assert(session.Conflicts[0].DiskValue == "F10" && session.Conflicts[0].LocalValue == "F8", "conflict reports both actual values");
        session.Refresh(); session.Refresh();
        Assert(session.Conflicts.Length == 1, "unchanged repeated refresh retains unresolved conflict");
        ExternalKeyboard(path, Custom2, "IA_Jump", 0, "F11"); session.Refresh();
        Assert(session.Conflicts.Length == 1, "unrelated subsequent update retains conflict");
        byte[] before = File.ReadAllBytes(path);
        var rejected = session.Apply();
        Assert(!rejected.Success && !rejected.PartialSuccess, "conflicted apply rejects before writing");
        Assert(before.SequenceEqual(File.ReadAllBytes(path)), "conflicted apply leaves exact input bytes");
        session.ResolveConflict(session.Conflicts[0].Id, false);
        Assert(session.GetKeyboard("IA_Guard", 0) == "F10" && session.Conflicts.Length == 0, "choose game adopts latest key");
        session.SetKeyboard("IA_Guard", 0, "F8");
        ExternalKeyboard(path, Custom1, "IA_Guard", 0, "F12"); session.Refresh();
        session.ResolveConflict(session.Conflicts[0].Id, true);
        var applied = session.Apply();
        Assert(applied.Success && applied.BindingsSaved, "explicit local resolution can save");
        Assert(Keyboard(path, Custom1, "IA_Guard", 0) == "F8", "local conflict choice committed");
        Assert(Keyboard(path, Custom1, "IA_Jump", 0) == "F9", "save preserves external unrelated key");
        Assert(Keyboard(path, Custom2, "IA_Jump", 0) == "F11", "save preserves other scheme external key");
        Assert(applied.InputBackup != null && File.Exists(applied.InputBackup), "binding apply creates backup outside save directory");
        Assert(!Path.GetFullPath(applied.InputBackup).StartsWith(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase), "backup stored outside game directory");
    }

    static void ScopeAndSwitch()
    {
        string path = NewInput("scope"); var session = Open(path);
        session.SetKeyboard("IA_Guard", 0, "F8"); session.SwitchProfile(Custom2);
        Assert(session.HasKeyboardDraft(Custom1), "switch retains other scheme draft");
        Assert(!session.HasChanges && session.HasAnyDrafts, "inactive scheme draft excluded from apply scope");
        session.SetKeyboard("IA_Jump", 0, "F9");
        ExternalKeyboard(path, Custom1, "IA_Jump", 0, "F10");
        var saved = session.Apply();
        Assert(saved.Success && session.HasKeyboardDraft(Custom1), "apply retains other keyboard draft");
        Assert(Keyboard(path, Custom1, "IA_Guard", 0) == "RightMouseButton", "apply does not submit invisible keyboard edits");
        Assert(Keyboard(path, Custom2, "IA_Jump", 0) == "F9", "apply submits selected scheme");
        session.SwitchProfile(Custom1);
        Assert(session.GetKeyboard("IA_Guard", 0) == "F8", "switch restores scheme draft");
        Assert(session.GetKeyboard("IA_Jump", 0) == "F10", "switch reads latest disk");
        session.Undo();
        Assert(!session.HasAnyDrafts && session.GetKeyboard("IA_Guard", 0) == "RightMouseButton", "undo restores disk for current scope");
        Assert(!session.Apply().BindingsSaved, "clean apply does not rewrite input");
    }

    static void Preview()
    {
        string path = NewInput("preview"); var session = Open(path);
        session.SetKeyboard("IA_Jump", 0, "F8");
        session.SelectPreset("onekey_dodge");
        Assert(session.GetKeyboard("IA_Dodge", 2) == "RightMouseButton", "first preview adds right click dodge");
        session.SelectPreset("onekey");
        Assert(session.GetKeyboard("IA_Dodge", 2) == null, "successive preset replaces previous effect instead of accumulating");
        Assert(session.GetKeyboard("IA_Jump", 0) == "F8", "preset keeps prior unrelated manual edit");
        ExternalKeyboard(path, Custom1, "IA_MoveFast", 0, "F9"); session.Refresh();
        session.SelectPreset(null);
        Assert(session.GetKeyboard("IA_Guard", 0) == "RightMouseButton", "cancel preview restores original binding");
        Assert(session.GetKeyboard("IA_MoveFast", 0) == "F9", "cancel preview retains external update merged into checkpoint");
        Assert(session.GetKeyboard("IA_Jump", 0) == "F8", "cancel preview retains original draft");
        session.SelectPreset("onekey_dodge"); session.SetKeyboard("IA_Jump", 0, "F10");
        Assert(session.PresetId == null, "manual edit ends preview labeling");
        session.SelectPreset("onekey");
        Assert(session.GetKeyboard("IA_Dodge", 2) == "RightMouseButton", "after manual edit new preview starts at current draft");
        session.SwitchProfile(Custom2);
        Assert(session.PresetId == null, "switch ends preview");
        session.SwitchProfile(Custom1);
        Assert(session.GetKeyboard("IA_Jump", 0) == "F10", "switch retains committed preview plus hand edit as draft");

        string conflictPath = NewInput("preview-conflict"); var conflicted = Open(conflictPath);
        conflicted.SetKeyboard("IA_Jump", 0, "F8"); conflicted.SelectPreset("onekey");
        ExternalKeyboard(conflictPath, Custom1, "IA_Jump", 0, "F9"); conflicted.Refresh();
        Assert(conflicted.Conflicts.Length == 1, "preview exposes conflict in preexisting draft");
        conflicted.SelectPreset(null);
        Assert(conflicted.Conflicts.Length == 1, "preview cancellation does not hide checkpoint conflict");
    }

    static void ScopedPadAndMetadata()
    {
        string path = NewInput("metadata"); var save = SaveFile.Load(path);
        Assert(save.GamepadProfiles.Count == 3 && save.GamepadProfiles.All(profile => profile.Family == "Xbox"), "real fixture has three Xbox records, not three device families");
        Assert(save.GamepadProfiles.Select(profile => profile.ProfileId).SequenceEqual(save.Records.Select(record => record.ProfileId)), "each fixture gamepad record explicitly belongs to one keyboard profile");
        AddFamily(save, Custom1, "PlayStation");
        var guard = save.RecordFor(Custom1).Entries.First(e => e.Name == "IA_Guard" && e.Slot == 0);
        guard.X = "metadata-X"; guard.Y = "metadata-Y"; guard.Pad = new byte[] { 1, 2, 3, 4, 5 };
        save.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = "IA_Guard", Slot = 9, Key = "UnknownKeyboardKey", X = "unknown", Y = "None" });
        for (int index = 0; index < PadFor(save).Count; index++)
        {
            var item = PadFor(save)[index].Items.First(e => SaveFile.Field(e, "MappingName") == "IA_Guard");
            SaveFile.SetField(item, "Key", index == 0 ? "Gamepad_LeftShoulder" : "Gamepad_RightShoulder");
            SaveFile.SetField(item, "DefaultKey", "Gamepad_LeftShoulder");
            var unknown = item.Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(unknown, "Slot", "EPlayerMappableKeySlot::UnknownFutureSlot");
            SaveFile.SetField(unknown, "Key", "UnknownPadKey"); PadFor(save)[index].Items.Add(unknown);
        }
        save.Save(path);
        byte[] beforeDefault = ProfileSnapshot(save, "InputUserSettings.Profiles.Default"), beforeCustom2 = ProfileSnapshot(save, Custom2);
        var session = Open(path);
        Assert(session.GamepadValuesDiffer("IA_Guard", 0), "same-profile family differences exposed without mixing other profiles");
        session.SetKeyboard("IA_Jump", 0, "F8"); Assert(session.Apply().Success, "keyboard-only save succeeds");
        var current = SaveFile.Load(path);
        Assert(SaveFile.Field(PadFor(current)[0].Items.First(e => SaveFile.Field(e, "MappingName") == "IA_Guard"), "Key") == "Gamepad_LeftShoulder", "unedited current-profile Xbox family preserved");
        Assert(SaveFile.Field(PadFor(current)[1].Items.First(e => SaveFile.Field(e, "MappingName") == "IA_Guard"), "Key") == "Gamepad_RightShoulder", "unedited same-profile second family remains different");
        session.SelectPreset("onekey_dodge"); Assert(session.Apply().Success, "replacement preset applies through session");
        current = SaveFile.Load(path); guard = current.RecordFor(Custom1).Entries.First(e => e.Name == "IA_Guard" && e.Slot == 0);
        Assert(guard.X == "metadata-X" && guard.Y == "metadata-Y" && guard.Pad.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "preset changes binding key and preserves existing opaque metadata");
        Assert(current.RecordFor(Custom1).Entries.Any(e => e.Slot == 9 && e.Key == "UnknownKeyboardKey"), "preset preserves unsupported keyboard slot");
        Assert(PadFor(current).All(k => k.Items.Any(e => SaveFile.Field(e, "Key") == "UnknownPadKey")), "preset preserves unsupported pad slots in every matching family");
        Assert(beforeDefault.SequenceEqual(ProfileSnapshot(current, "InputUserSettings.Profiles.Default")) && beforeCustom2.SequenceEqual(ProfileSnapshot(current, Custom2)), "GUI preset leaves both other schemes byte-identical");
        session.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Top"); session.SwitchProfile(Custom2);
        Assert(!session.IsGamepadChanged("IA_Guard", 0) && session.GetGamepad("IA_Guard", 0) != "Gamepad_FaceButton_Top" && session.HasGamepadDraft(Custom1), "switch shows selected profile pad and preserves previous profile draft");
        Assert(!session.GamepadValuesDiffer("IA_Guard", 0), "different profiles are not presented as differing controller families");
        session.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Bottom");
        Assert(session.Apply().Success && session.HasGamepadDraft(Custom1), "applying Custom2 pad retains Custom1 pad draft");
        Assert(SaveFile.Field(PadFor(SaveFile.Load(path))[0].Items.First(e => SaveFile.Field(e, "MappingName") == "IA_Guard"), "Key") != "Gamepad_FaceButton_Top", "Custom2 apply does not submit Custom1 pad edits");
        session.SwitchProfile(Custom1); Assert(session.GetGamepad("IA_Guard", 0) == "Gamepad_FaceButton_Top", "returning to Custom1 restores its separate pad draft");
        Assert(session.Apply().Success, "Custom1 pad commits only its own profile");
        beforeCustom2 = ProfileSnapshot(SaveFile.Load(path), Custom2);
        session.SetGamepad("IA_Guard", 0, null); Assert(session.Apply().Success, "pad clear restores defaults");
        current = SaveFile.Load(path);
        Assert(PadFor(current).All(k => k.Items.Where(e => SaveFile.Field(e, "MappingName") == "IA_Guard" && SaveFile.Field(e, "Slot") == "EPlayerMappableKeySlot::Second").All(e => SaveFile.Field(e, "Key") == SaveFile.Field(e, "DefaultKey"))), "clear uses each matching family's own default");
        Assert(beforeDefault.SequenceEqual(ProfileSnapshot(current, "InputUserSettings.Profiles.Default")) && beforeCustom2.SequenceEqual(ProfileSnapshot(current, Custom2)), "manual edit and clear preserve other profiles exactly");
        session.SwitchProfile(Custom1); session.SetKeyboard("IA_Guard", 0, null); Assert(session.Apply().Success, "clear keyboard override applies");
        Assert(Keyboard(path, Custom1, "IA_Guard", 0) == null, "clear removes override rather than binding empty string");
    }

    static void ExplicitUnbinding()
    {
        string path = NewInput("unbinding");
        var before = SaveFile.Load(path);
        AddFamily(before, Custom1, "PlayStation"); before.Save(path);
        var session = Open(path);
        session.SetKeyboard("IA_Guard", 0, "None");
        session.SetKeyboard("IA_Guard", 1, "None");
        session.SetGamepad("IA_Guard", 0, "None");
        Assert(!session.IsGamepadDefault("IA_Guard", 0), "explicit unbinding differs from controller defaults");
        session.SwitchProfile(Custom2); session.SwitchProfile(Custom1);
        Assert(session.GetKeyboard("IA_Guard", 0) == "None" && session.GetGamepad("IA_Guard", 0) == "None", "unbound drafts survive navigation");
        Assert(session.Apply().Success, "explicit unbinding applies");
        var saved = SaveFile.Load(path);
        Assert(Keyboard(path, Custom1, "IA_Guard", 0) == "None" && Keyboard(path, Custom1, "IA_Guard", 2) == "None", "unbinding preserves an explicit override for existing and missing keyboard slots");
        Assert(PadFor(saved).All(keys => keys.Items.Where(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::Second").All(item => SaveFile.Field(item, "Key") == "None")), "unbinding persists in each controller family");
        Assert(ProfileSnapshot(before, Custom2).SequenceEqual(ProfileSnapshot(saved, Custom2)) && ProfileSnapshot(before, "InputUserSettings.Profiles.Default").SequenceEqual(ProfileSnapshot(saved, "InputUserSettings.Profiles.Default")), "unbinding preserves other schemes");
        session = Open(path);
        Assert(session.GetKeyboard("IA_Guard", 1) == "None" && session.GetGamepad("IA_Guard", 0) == "None", "reopening keeps explicit unbinding");
        session.SetKeyboard("IA_Guard", 0, null); session.SetKeyboard("IA_Guard", 1, null);
        session.SetGamepad("IA_Guard", 0, null);
        Assert(session.IsGamepadDefault("IA_Guard", 0) && session.Apply().Success, "clear override restores controller defaults after unbinding");
        before.RecordFor(Custom1).Entries.RemoveAll(entry => entry.Name == "IA_Guard" && (entry.Slot == 0 || entry.Slot == 2));
        Assert(before.ToBytes().SequenceEqual(File.ReadAllBytes(path)), "restoring defaults removes keyboard overrides while preserving all other save data");
        session.SetKeyboard("IA_Guard", 0, "None"); session.SetGamepad("IA_Guard", 0, "None"); session.Undo();
        Assert(!session.HasAnyDrafts && session.GetKeyboard("IA_Guard", 0) == null, "undo cancels unbinding without writing");
    }

    static void ProfilePadMerging()
    {
        string path = NewInput("profile-pad-merge"); var session = Open(path);
        session.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Top");
        ExternalPad(path, Custom2, "IA_Guard", 0, "Gamepad_RightTrigger"); session.Refresh();
        Assert(session.Conflicts.Length == 0 && session.GetGamepad("IA_Guard", 0) == "Gamepad_FaceButton_Top", "same action edited in another profile does not conflict with local pad draft");
        byte[] other = ProfileSnapshot(SaveFile.Load(path), Custom2);
        Assert(session.Apply().Success && other.SequenceEqual(ProfileSnapshot(SaveFile.Load(path), Custom2)), "pad apply preserves latest external other-profile pad change exactly");

        session.SetGamepad("IA_Guard", 0, "Gamepad_RightShoulder");
        ExternalPad(path, Custom1, "IA_Guard", 0, "Gamepad_FaceButton_Bottom"); session.Refresh(); session.Refresh();
        EditorConflict conflict = session.Conflicts.Single();
        Assert(conflict.IsGamepad && conflict.ProfileId == Custom1 && conflict.LocalValue == "Gamepad_RightShoulder" && conflict.DiskValue == "Gamepad_FaceButton_Bottom", "pad conflict retains both device and real owning profile on repeated refresh");
        session.SwitchProfile(Custom2);
        Assert(session.Conflicts.Length == 0 && session.GetGamepad("IA_Guard", 0) == "Gamepad_RightTrigger", "other profile shows its latest own pad values and hides unrelated conflict");
        session.SetGamepad("IA_Guard", 0, "Gamepad_LeftTrigger"); session.Undo();
        Assert(!session.HasGamepadDraft(Custom2) && session.HasGamepadDraft(Custom1), "undo only selected profile's pad draft");
        session.SetKeyboard("IA_Jump", 0, "F9"); Assert(session.Apply().Success, "hidden other-profile conflict does not block current apply");
        session.SwitchProfile(Custom1);
        Assert(session.Conflicts.Single().Id == conflict.Id && session.GetGamepad("IA_Guard", 0) == "Gamepad_RightShoulder", "returning restores previous profile's pad conflict and local draft");
        session.ResolveConflict(conflict.Id, false); Assert(!session.HasChanges, "adopting game resolves only the selected pad conflict");

        string previewPath = NewInput("profile-pad-preview"); var preview = Open(previewPath);
        preview.SetGamepad("IA_Jump", 0, "Gamepad_RightShoulder");
        preview.SwitchProfile(Custom2); preview.SetGamepad("IA_Jump", 0, "Gamepad_LeftTrigger"); preview.SwitchProfile(Custom1);
        preview.SelectPreset("onekey_dodge"); preview.SelectPreset("onekey");
        Assert(preview.GetGamepad("IA_Dodge", 1) != "Gamepad_LeftShoulder", "consecutive preview removes previous pad preset effect");
        ExternalPad(previewPath, Custom1, "IA_MoveFast", 0, "Gamepad_RightTrigger");
        ExternalPad(previewPath, Custom2, "IA_Guard", 0, "Gamepad_FaceButton_Top"); preview.Refresh(); preview.SelectPreset(null);
        Assert(preview.GetGamepad("IA_Jump", 0) == "Gamepad_RightShoulder" && preview.GetGamepad("IA_MoveFast", 0) == "Gamepad_RightTrigger", "pad preview cancellation restores checkpoint draft plus latest unrelated update");
        Assert(preview.Apply().Success && preview.HasGamepadDraft(Custom2), "preview cancellation and apply preserve other profile's pad draft");
        preview.SwitchProfile(Custom2);
        Assert(preview.GetGamepad("IA_Jump", 0) == "Gamepad_LeftTrigger" && preview.GetGamepad("IA_Guard", 0) == "Gamepad_FaceButton_Top", "other profile retains draft and its own external checkpoint merge");
        preview.Undo();
        Assert(!preview.HasAnyDrafts, "per-profile undo clears only remaining other-profile draft");
    }

    static void ProfilePresetAndUnsupportedData()
    {
        var scopedReset = SaveFile.Load(fixture);
        ApplyTarget(scopedReset, Presets.ById("vanilla_current"));
        Assert(scopedReset.RecordFor(Custom1).Entries.Count == 0, "current-scheme reset clears its keyboard overrides");
        Assert(scopedReset.GamepadKeysFor(Custom1).SelectMany(keys => keys.Items).All(item => SaveFile.Field(item, "Key") == SaveFile.Field(item, "DefaultKey")), "current-scheme reset restores each controller family's defaults");

        var resetPreview = Open(NewInput("current-scheme-reset-preview"));
        resetPreview.SelectPreset("vanilla_current");
        Assert(!resetPreview.HasChanges && !resetPreview.HasAnyDrafts, "restoring effective factory bindings does not create a draft");
        resetPreview.SelectPreset(null);
        resetPreview.SetKeyboard("IA_Guard", 0, "F8");
        resetPreview.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Top");
        resetPreview.SelectPreset("vanilla_current");
        Assert(resetPreview.GetKeyboard("IA_Guard", 0) == "RightMouseButton", "restoring a draft to its saved effective keys clears the draft without changing records");
        Assert(resetPreview.GetGamepad("IA_Guard", 0) != "Gamepad_FaceButton_Top", "current-scheme reset previews the controller default");
        resetPreview.SelectPreset(null);
        Assert(resetPreview.GetKeyboard("IA_Guard", 0) == "F8" && resetPreview.GetGamepad("IA_Guard", 0) == "Gamepad_FaceButton_Top", "cancel restores the pre-reset draft");

        foreach (string id in new[] { "vanilla_current", "onekey", "onekey_dodge", "souls" })
        {
            var save = SaveFile.Load(fixture); ApplyTarget(save, Presets.ById(id));
            string path = NewInput("profile-preset-" + id); var session = Open(path);
            byte[] beforeDefault = ProfileSnapshot(SaveFile.Load(path), "InputUserSettings.Profiles.Default"), beforeCustom2 = ProfileSnapshot(SaveFile.Load(path), Custom2);
            session.SelectPreset(id); Assert(session.Apply().Success, id + " GUI apply succeeds");
            var current = SaveFile.Load(path);
            Assert(beforeDefault.SequenceEqual(ProfileSnapshot(current, "InputUserSettings.Profiles.Default")) && beforeCustom2.SequenceEqual(ProfileSnapshot(current, Custom2)), id + " GUI preserves other schemes' keyboard and pad records exactly");
        }
        string malformedPath = NewInput("missing-pad-profile"); var malformed = SaveFile.Load(malformedPath);
        malformed.Props.First(node => node.Name == "GamepadFamilyProfiles").Items[0].RemoveAll(node => node.Name == "ProfileId");
        malformed.Save(malformedPath); byte[] original = File.ReadAllBytes(malformedPath);
        bool rejected = false; try { Open(malformedPath); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected && original.SequenceEqual(File.ReadAllBytes(malformedPath)), "missing explicit pad ProfileId rejects editor without writing");
        foreach (string id in new[] { "onekey", "onekey_dodge", "souls", "space_sprint", "space_dodge", "vanilla_current", "vanilla" })
        {
            malformed = SaveFile.FromBytes(original); rejected = false;
            try { Presets.Apply(malformed, Presets.ById(id), malformed.RecordFor(Custom1)); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && original.SequenceEqual(malformed.ToBytes()), id + " rejects ambiguous pad ownership atomically");
        }
    }

    static void PresetMetadataParity()
    {
        foreach (Preset preset in Presets.All().Where(item => !item.Vanilla && !item.AdditiveGuard))
        {
            var source = SaveFile.Load(fixture); AddFamily(source, Custom1, "PlayStation");
            foreach (string action in preset.Keyboard.Select(binding => binding.Action).Distinct())
            {
                source.RecordFor(Custom1).Entries.RemoveAll(entry => entry.Name == action);
                source.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = action, Slot = 0, Key = "F8", X = "first-X", Y = "first-Y", Pad = new byte[] { 1, 3, 5, 7, 9 } });
                source.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = action, Slot = 0, Key = "F9", X = "duplicate-X", Y = "duplicate-Y", Pad = new byte[] { 2, 4, 6, 8, 0 } });
                source.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = action, Slot = 2, Key = "F10", X = "other-slot-X", Y = "other-slot-Y" });
                source.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = action, Slot = 9, Key = "FutureKeyboardGuard", X = "unknown-X", Y = "unknown-Y" });
                source.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = action, Slot = 7, Key = "UnspecifiedKeyboardGuard", X = "unspecified-X", Y = "unspecified-Y" });
            }
            foreach (Node keys in PadFor(source))
                foreach (string action in preset.Gamepad.Select(binding => binding.Action).Distinct())
                {
                    var template = keys.Items.First(item => SaveFile.Field(item, "MappingName") == action).Select(SaveFile.CloneNode).ToList();
                    keys.Items.RemoveAll(item => SaveFile.Field(item, "MappingName") == action);
                    for (int index = 0; index < 4; index++)
                    {
                        var item = template.Select(SaveFile.CloneNode).ToList();
                        SaveFile.SetField(item, "Slot", "EPlayerMappableKeySlot::" + (index < 2 ? "Second" : index == 2 ? "Sixth" : "FutureSlot"));
                        SaveFile.SetField(item, "Key", index == 3 ? "FuturePadGuard" : "Gamepad_RightTrigger");
                        int raw = InputDefaults.RawSlot(SaveFile.Field(item, "Slot"));
                        SaveFile.SetField(item, "DefaultKey", raw >= 0 && InputDefaults.RegisteredSlots(action, true).Contains(raw) ? InputDefaults.ForRawSlot(action, true, raw) : "MetadataSentinel" + index); keys.Items.Add(item);
                    }
                }
            byte[] sourceBytes = source.ToBytes();
            var expected = SaveFile.FromBytes(sourceBytes); ApplyTarget(expected, preset);
            foreach (var group in preset.Keyboard.GroupBy(binding => binding.Action))
                for (int index = 0; index < 3; index++)
                {
                    string key = group.Any(binding => binding.Slot == index) ? group.First(binding => binding.Slot == index).Key : group.First().Key;
                    var matching = expected.RecordFor(Custom1).Entries.Where(entry => entry.Name == group.Key && entry.Slot == KeyboardSlots(source, Custom1, group.Key)[index]).ToArray();
                    Assert(matching.Length > 0 && matching.All(entry => entry.Key == key), preset.Id + " replaces every duplicate supported keyboard slot and supplies missing slots");
                }
            foreach (Node keys in PadFor(expected))
                foreach (var group in preset.Gamepad.GroupBy(binding => binding.Action))
                    foreach (var item in keys.Items.Where(item => SaveFile.Field(item, "MappingName") == group.Key))
                    {
                        int familyIndex = PadFor(expected).IndexOf(keys);
                        int index = Array.IndexOf(GamepadSlots(source, Custom1, PadFor(source)[familyIndex], group.Key), InputDefaults.RawSlot(SaveFile.Field(item, "Slot")));
                        var original = PadFor(source)[familyIndex].Items.FirstOrDefault(before => SaveFile.Field(before, "MappingName") == group.Key && SaveFile.Field(before, "Slot") == SaveFile.Field(item, "Slot"));
                        string key = index < 0 ? SaveFile.Field(original, "Key") : group.Any(binding => binding.Slot == index) ? group.First(binding => binding.Slot == index).Key : group.First().Key;
                        Assert(SaveFile.Field(item, "Key") == key, preset.Id + " moves all supported pad slots and preserves unknown slot");
                    }

            // Restore only existing binding values and discard only newly added slots.
            // The entire remaining serialization must match, including opaque fields,
            // duplicate entries, unknown slots, ordering and both unrelated profiles.
            var metadataOnly = SaveFile.FromBytes(expected.ToBytes());
            for (int profile = 0; profile < source.Records.Count; profile++)
            {
                BlobRecord before = source.Records[profile], after = metadataOnly.Records[profile];
                Assert(after.Entries.Count >= before.Entries.Count, preset.Id + " preserves original keyboard entry count");
                after.Entries.RemoveRange(before.Entries.Count, after.Entries.Count - before.Entries.Count);
                for (int index = 0; index < before.Entries.Count; index++)
                {
                    BlobEntry original = before.Entries[index], current = after.Entries[index];
                    bool editable = before.ProfileId == Custom1 && preset.Keyboard.Any(binding => binding.Action == original.Name) && Array.IndexOf(KeyboardSlots(source, before.ProfileId, original.Name), (int)original.Slot) >= 0;
                    Assert(editable || original.Key == current.Key, preset.Id + " preserves unknown and unrelated keyboard keys exactly");
                    current.Key = original.Key;
                }
            }
            for (int family = 0; family < source.GamepadKeys.Count; family++)
            {
                Node before = source.GamepadKeys[family], after = metadataOnly.GamepadKeys[family];
                Assert(after.Items.Count >= before.Items.Count, preset.Id + " preserves original controller entry count");
                after.Items.RemoveRange(before.Items.Count, after.Items.Count - before.Items.Count);
                for (int index = 0; index < before.Items.Count; index++)
                {
                    var original = before.Items[index];
                    bool editable = source.GamepadProfiles[family].ProfileId == Custom1 && preset.Gamepad.Any(binding => binding.Action == SaveFile.Field(original, "MappingName")) && Array.IndexOf(GamepadSlots(source, source.GamepadProfiles[family].ProfileId, before, SaveFile.Field(original, "MappingName")), InputDefaults.RawSlot(SaveFile.Field(original, "Slot"))) >= 0;
                    Assert(editable || SaveFile.Field(original, "Key") == SaveFile.Field(after.Items[index], "Key"), preset.Id + " preserves unknown and unrelated controller keys exactly");
                    SaveFile.SetField(after.Items[index], "Key", SaveFile.Field(original, "Key"));
                }
            }
            Assert(sourceBytes.SequenceEqual(metadataOnly.ToBytes()), preset.Id + " changes only supported binding values and appends necessary slots; all metadata stays exact");
            string path = NewInput("preset-metadata-parity-" + preset.Id); File.WriteAllBytes(path, sourceBytes);
            var session = Open(path); session.SelectPreset(preset.Id);
            Assert(session.Apply().Success, preset.Id + " GUI metadata fixture applies");
            var actual = SaveFile.Load(path);
            var actions = source.RecordFor(Custom1).Entries.Select(entry => entry.Name).Union(expected.RecordFor(Custom1).Entries.Select(entry => entry.Name)).Distinct();
            foreach (string action in actions.Where(InputDefaults.HasAction))
            {
                Func<SaveFile, string[]> keys = save => InputDefaults.ResolveKeys(action, false, save.RecordFor(Custom1).Entries
                    .Where(entry => entry.Name == action && !Presets.IsGamepadOverride(save.GamepadKeysFor(Custom1), entry))
                    .Select(entry => new KeyValuePair<int, string>(entry.Slot, entry.Key)));
                Assert(!keys(expected).Except(keys(actual)).Any() && !keys(actual).Except(keys(expected)).Any(), preset.Id + " GUI and CLI have the same effective keyboard bindings: " + action);
            }
            for (int family = 0; family < actual.GamepadKeysFor(Custom1).Count; family++)
                foreach (string action in expected.GamepadKeysFor(Custom1)[family].Items.Select(item => SaveFile.Field(item, "MappingName")).Distinct().Where(InputDefaults.HasAction))
                {
                    Func<SaveFile, string[]> keys = save => InputDefaults.ResolveKeys(action, true, save.GamepadKeysFor(Custom1)[family].Items
                        .Where(item => SaveFile.Field(item, "MappingName") == action && InputDefaults.ForRawSlot(action, true, InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))) == SaveFile.Field(item, "DefaultKey"))
                        .Select(item => new KeyValuePair<int, string>(InputDefaults.RawSlot(SaveFile.Field(item, "Slot")), SaveFile.Field(item, "Key"))));
                    Assert(!keys(expected).Except(keys(actual)).Any() && !keys(actual).Except(keys(expected)).Any(), preset.Id + " GUI and CLI have the same effective controller bindings: " + action);
                }
            ApplyTarget(actual, preset);
            foreach (var save in new[] { expected, actual })
            {
                save.RecordFor(Custom1).Entries = save.RecordFor(Custom1).Entries.OrderBy(entry => entry.Name).ThenBy(entry => entry.Slot).ToList();
                foreach (var keys in save.GamepadKeysFor(Custom1))
                    keys.Items = keys.Items.OrderBy(item => SaveFile.Field(item, "MappingName")).ThenBy(item => SaveFile.Field(item, "Slot")).ToList();
            }
            Assert(expected.ToBytes().SequenceEqual(actual.ToBytes()), preset.Id + " preserves metadata and unrelated data when unchanged bindings retain their saved records");
        }
    }

    static void ResetAndRace()
    {
        string path = NewInput("reset"); var session = Open(path);
        session.SetKeyboard("IA_Guard", 0, "F8"); session.SetGamepad("IA_Guard", 0, "Gamepad_RightShoulder");
        session.SwitchProfile(Custom2); session.SetKeyboard("IA_Jump", 0, "F9");
        session.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Top"); session.ResetAll();
        Assert(session.IsResetPending && session.HasChanges, "global reset is an explicit draft");
        bool rejected = false; try { session.SetKeyboard("IA_Jump", 0, "F10"); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "reset locks manual editing until apply or undo");
        ExternalKeyboard(path, Custom1, "IA_MoveFast", 0, "F11"); session.Refresh();
        Assert(session.Conflicts.Any(c => c.Action == "IA_MoveFast"), "global reset marks newly arrived external override as conflict");
        session.Undo();
        Assert(!session.IsResetPending && session.GetKeyboard("IA_Jump", 0) == "F9", "reset undo restores current original draft");
        Assert(session.GetGamepad("IA_Guard", 0) == "Gamepad_FaceButton_Top", "reset undo restores Custom2 pad draft");
        session.SwitchProfile(Custom1);
        Assert(session.GetKeyboard("IA_Guard", 0) == "F8", "reset undo restores all schemes' original drafts");
        Assert(session.GetGamepad("IA_Guard", 0) == "Gamepad_RightShoulder", "reset undo restores separate Custom1 pad draft");
        Assert(session.GetKeyboard("IA_MoveFast", 0) == "F11", "reset undo includes latest external update");
        session.ResetAll(); Assert(session.Apply().Success, "global reset applies all scopes");
        var save = SaveFile.Load(path);
        Assert(save.Records.All(r => r.Entries.Count == 0), "reset clears all keyboard records");
        Assert(save.GamepadKeys.All(k => k.Items.All(e => SaveFile.Field(e, "Key") == SaveFile.Field(e, "DefaultKey"))), "reset restores each controller default");
        Assert(!session.HasAnyDrafts, "successful reset clears prior superseded drafts");

        string exclusionPath = NewInput("reset-exclusion"); var exclusion = Open(exclusionPath);
        exclusion.ResetAll(); ExternalKeyboard(exclusionPath, Custom1, "IA_Guard", 0, "F8"); exclusion.Refresh();
        exclusion.ResolveConflict(exclusion.Conflicts.First(c => c.Action == "IA_Guard").Id, false);
        ExternalKeyboard(exclusionPath, Custom2, "IA_Jump", 0, "F9"); exclusion.Refresh();
        Assert(exclusion.GetKeyboard("IA_Guard", 0) == "F8" && !exclusion.Conflicts.Any(c => c.Action == "IA_Guard"), "reset adopt-game choice survives unrelated later refresh");
        foreach (var conflict in exclusion.Conflicts) exclusion.ResolveConflict(conflict.Id, true);
        Assert(exclusion.Apply().Success && Keyboard(exclusionPath, Custom1, "IA_Guard", 0) == "F8", "global reset preserves explicitly accepted external key");

        string racePath = NewInput("race"); var race = Open(racePath); race.SetKeyboard("IA_Guard", 0, "F8");
        race.BeforeInputReplace = delegate { ExternalKeyboard(racePath, Custom1, "IA_Jump", 0, "F9"); };
        var result = race.Apply();
        Assert(!result.Success && !result.BindingsSaved, "last moment external writer prevents replacement");
        Assert(Keyboard(racePath, Custom1, "IA_Guard", 0) == "RightMouseButton" && Keyboard(racePath, Custom1, "IA_Jump", 0) == "F9", "failed write preserves external save");
        Assert(race.GetKeyboard("IA_Guard", 0) == "F8", "failed save retains local draft");
        race.BeforeInputReplace = null;
        Assert(race.Apply().Success && Keyboard(racePath, Custom1, "IA_Jump", 0) == "F9", "retry merges new disk data before saving");
        Assert(Directory.GetFiles(Path.GetDirectoryName(racePath), "*.tmp").Length == 0, "failed and successful writes leave no temp files");
    }

    static void AuthenticatedApplication(string sourceInstall, string newInstall)
    {
        sourceInstall = Path.GetFullPath(sourceInstall); newInstall = Path.GetFullPath(newInstall);
        Assert(!Directory.Exists(newInstall), "authenticated test directory must be new");
        string sourceInput = Path.Combine(sourceInstall, "ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav");
        string sourceShared = Path.Combine(sourceInstall, "ProjectPBZ\\Saved\\PBZSharedSetting.sav");
        byte[] sourceInputBytes = File.ReadAllBytes(sourceInput), sourceSharedBytes = File.ReadAllBytes(sourceShared);
        string saved = Path.Combine(newInstall, "ProjectPBZ\\Saved"), platform = Path.Combine(newInstall, "platform\\steam");
        Directory.CreateDirectory(saved); Directory.CreateDirectory(platform);
        string executable = Path.Combine(platform, "PBZ.exe");
        if (!CreateHardLink(executable, Path.Combine(sourceInstall, "platform\\steam\\PBZ.exe"), IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        string input = Path.Combine(saved, "EnhancedInputUserSettings.sav"), shared = Path.Combine(saved, "PBZSharedSetting.sav");
        File.WriteAllBytes(input, sourceInputBytes); File.WriteAllBytes(shared, sourceSharedBytes);
        GameSchemeSettings.Load(shared, executable).Activate(Custom2);
        var session = Open(input);
        Assert(session.ActiveProfile == Custom2 && session.SchemeError == null, "authenticated session reads actual selector");
        session.SwitchProfile(Custom2); session.SetGamepad("IA_Guard", 0, "Gamepad_RightTrigger"); session.SwitchProfile(Custom1);
        session.SetKeyboard("IA_Jump", 0, "F8"); session.SetGamepad("IA_Guard", 0, "Gamepad_FaceButton_Top"); session.SetActivation(true);
        byte[] otherPadBeforePartial = ProfileSnapshot(SaveFile.Load(input), Custom2);
        session.BeforeActivation = delegate { GameSchemeSettings.Load(shared, executable).Activate("InputUserSettings.Profiles.Default"); };
        var partial = session.Apply();
        Assert(!partial.Success && partial.PartialSuccess && partial.BindingsSaved && !partial.ActivationSaved, "stale selector after input save is accurately reported as partial success");
        Assert(Keyboard(input, Custom1, "IA_Jump", 0) == "F8", "partial success has saved intended binding");
        Assert(session.KeyboardChangeCount == 0 && session.GamepadChangeCount == 0 && session.ActivationPending, "partial success clears selected keyboard and pad drafts and retains activation intention");
        Assert(SaveFile.Field(SaveFile.Load(input).GamepadKeysFor(Custom1)[0].Items.First(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::Second"), "Key") == "Gamepad_FaceButton_Top", "partial success commits selected profile pad binding");
        Assert(session.HasGamepadDraft(Custom2) && otherPadBeforePartial.SequenceEqual(ProfileSnapshot(SaveFile.Load(input), Custom2)), "partial success preserves other profile pad draft without submitting it");
        byte[] savedInput = File.ReadAllBytes(input); DateTime inputTime = File.GetLastWriteTimeUtc(input);
        session.BeforeActivation = null;
        var retried = session.Apply();
        Assert(retried.Success && retried.ActivationSaved && !retried.BindingsSaved, "partial retry only applies remaining activation");
        Assert(savedInput.SequenceEqual(File.ReadAllBytes(input)) && inputTime == File.GetLastWriteTimeUtc(input), "partial retry never rewrites already committed input");
        Assert(session.ActiveProfile == Custom1 && !session.ActivationPending && !session.HasChanges, "successful retry updates real marker and clears intended scope");
        Assert(session.HasGamepadDraft(Custom2), "activation retry preserves other profile pending pad draft");

        session.SwitchProfile(Custom2); session.SetKeyboard("IA_Jump", 0, "F9"); session.SetActivation(true);
        byte[] authenticatedShared = File.ReadAllBytes(shared); byte[] broken = (byte[])authenticatedShared.Clone(); broken[broken.Length - 1] ^= 1; File.WriteAllBytes(shared, broken);
        byte[] beforeFailure = File.ReadAllBytes(input);
        var failure = session.Apply();
        Assert(!failure.Success && !failure.PartialSuccess && !failure.BindingsSaved, "known authentication failure prevents both writes");
        Assert(beforeFailure.SequenceEqual(File.ReadAllBytes(input)) && session.GetKeyboard("IA_Jump", 0) == "F9", "preflight authentication failure preserves input and draft");
        File.WriteAllBytes(shared, authenticatedShared);
        Assert(session.Apply().Success && session.ActiveProfile == Custom2, "restored selector permits idempotent full retry");
        session.SwitchProfile(Custom1); session.SetActivation(true);
        byte[] beforeOnlyActivation = File.ReadAllBytes(input);
        var activationOnly = session.Apply();
        Assert(activationOnly.Success && activationOnly.ActivationSaved && !activationOnly.BindingsSaved, "activate-only flow works without binding edits");
        Assert(beforeOnlyActivation.SequenceEqual(File.ReadAllBytes(input)), "activate-only flow preserves input bytes");
        session.SwitchProfile(Custom2); session.SetKeyboard("IA_Jump", 0, "F10");
        Assert(session.Apply().Success && session.ActiveProfile == Custom1, "editing inactive scheme does not activate it");
        session.SetActivation(true); session.SwitchProfile(Custom1);
        Assert(!session.ActivationPending, "scheme switch drops previous hidden activation intention");
        Assert(sourceInputBytes.SequenceEqual(File.ReadAllBytes(sourceInput)) && sourceSharedBytes.SequenceEqual(File.ReadAllBytes(sourceShared)), "authenticated source save copies remain unchanged");
        Console.WriteLine("Authenticated test copies: " + newInstall);
    }

    static void ConfigureGuard(SaveFile save, string[] keyboard, Func<int, string[]> gamepad)
    {
        var record = save.RecordFor(Custom1);
        record.Entries.RemoveAll(e => e.Name == "IA_Guard" && Array.IndexOf(GuardKeyboardSlots, (int)e.Slot) >= 0);
        for (int slot = 0; slot < 3; slot++)
            if (keyboard[slot] != null) record.Entries.Add(new BlobEntry { Name = "IA_Guard", Slot = (byte)GuardKeyboardSlots[slot], Key = keyboard[slot], X = "guard-metadata-" + slot, Y = "None", Pad = new byte[] { 5, 4, 3, 2, 1 } });
        for (int family = 0; family < save.GamepadKeys.Count; family++)
        {
            var keys = save.GamepadKeys[family];
            var template = keys.Items.First(item => SaveFile.Field(item, "MappingName") == "IA_Guard").Select(SaveFile.CloneNode).ToList();
            keys.Items.RemoveAll(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && Array.IndexOf(GuardGamepadSlots, InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))) >= 0);
            string[] wanted = gamepad(family);
            for (int slot = 0; slot < GuardGamepadSlots.Length; slot++)
                if (wanted[slot] != null)
                {
                    var item = template.Select(SaveFile.CloneNode).ToList();
                    SaveFile.SetField(item, "Slot", InputDefaults.SlotName(GuardGamepadSlots[slot]));
                    SaveFile.SetField(item, "Key", wanted[slot]);
                    SaveFile.SetField(item, "DefaultKey", InputDefaults.ForRawSlot("IA_Guard", true, GuardGamepadSlots[slot])); keys.Items.Add(item);
                }
        }
    }

    static string[] GuardKeyboard(SaveFile save)
    { return save.RecordFor(Custom1).Entries.Where(e => e.Name == "IA_Guard" && Array.IndexOf(GuardKeyboardSlots, (int)e.Slot) >= 0).Select(e => e.Key).ToArray(); }

    static string[] GuardPad(Node keys)
    { return keys.Items.Where(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && Array.IndexOf(GuardGamepadSlots, InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))) >= 0).Select(item => SaveFile.Field(item, "Key")).ToArray(); }

    static bool ContainsBoth(string[] keys, string first, string second)
    { return keys.Contains(first) && keys.Contains(second); }

    static void AdditiveGuardPresets()
    {
        Preset onekey = Presets.ById("onekey");
        var empty = SaveFile.Load(fixture);
        ConfigureGuard(empty, new string[3], delegate { return new[] { "Gamepad_LeftShoulder", "Gamepad_LeftShoulder", "Gamepad_LeftShoulder" }; });
        ApplyTarget(empty, onekey);
        Assert(ContainsBoth(GuardKeyboard(empty), "RightMouseButton", "LeftMouseButton"), "empty default gains attack guard while retaining right-click guard");
        Assert(PadFor(empty).All(keys => ContainsBoth(GuardPad(keys), "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left")), "every default controller retains LB and gains X guard");
        byte[] stable = empty.ToBytes(); ApplyTarget(empty, onekey);
        Assert(stable.SequenceEqual(empty.ToBytes()), "additive preset is byte-idempotent");
        Assert(stable.SequenceEqual(SaveFile.FromBytes(stable).ToBytes()), "additive output roundtrips byte-identically");

        var old = SaveFile.Load(fixture);
        ConfigureGuard(old, new[] { "LeftMouseButton", "LeftMouseButton", "LeftMouseButton" }, delegate { return new[] { "Gamepad_FaceButton_Left", "Gamepad_FaceButton_Left", "Gamepad_FaceButton_Left" }; });
        old.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = "IA_Guard", Slot = 9, Key = "FutureKeyboardGuard", X = "opaque", Y = "None" });
        foreach (Node keys in old.GamepadKeys)
        {
            var unknown = keys.Items.First(item => SaveFile.Field(item, "MappingName") == "IA_Guard").Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(unknown, "Slot", "EPlayerMappableKeySlot::UnknownFutureSlot"); SaveFile.SetField(unknown, "Key", "FuturePadGuard"); keys.Items.Add(unknown);
        }
        ApplyTarget(old, onekey);
        Assert(ContainsBoth(GuardKeyboard(old), "RightMouseButton", "LeftMouseButton"), "legacy all-left scheme repairs right-click defense");
        Assert(PadFor(old).All(keys => ContainsBoth(GuardPad(keys), "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left")), "legacy all-X controller repairs LB defense");
        Assert(old.RecordFor(Custom1).Entries.Where(e => e.Slot != 9 && e.Name == "IA_Guard").All(e => e.X.StartsWith("guard-metadata-") && e.Pad.SequenceEqual(new byte[] { 5, 4, 3, 2, 1 })), "reusing duplicate slots preserves entry metadata");
        Assert(old.RecordFor(Custom1).Entries.Any(e => e.Slot == 9 && e.Key == "FutureKeyboardGuard"), "additive planning preserves unknown keyboard slot");
        Assert(PadFor(old).All(keys => keys.Items.Any(item => SaveFile.Field(item, "Key") == "FuturePadGuard")), "additive planning preserves unknown controller slots");

        var custom = SaveFile.Load(fixture); AddFamily(custom, Custom1, "PlayStation");
        ConfigureGuard(custom, new[] { "RightMouseButton", "F8", "None" }, delegate { return new[] { "Gamepad_LeftShoulder", "None" }; });
        ApplyTarget(custom, onekey);
        Assert(GuardKeyboard(custom).Contains("F8") && ContainsBoth(GuardKeyboard(custom), "RightMouseButton", "LeftMouseButton"), "custom keyboard binding remains alongside both guard buttons");
        Assert(PadFor(custom).All(keys => ContainsBoth(GuardPad(keys), "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left")), "empty registered controller slot supplies capacity");

        var fullCustomPad = SaveFile.Load(fixture);
        ConfigureGuard(fullCustomPad, new string[3], delegate { return new[] { "Gamepad_RightShoulder", "Gamepad_RightShoulder" }; });
        byte[] fullCustomBefore = fullCustomPad.ToBytes(); bool fullCustomRejected = false;
        try { ApplyTarget(fullCustomPad, onekey); } catch (InvalidOperationException) { fullCustomRejected = true; }
        Assert(fullCustomRejected && fullCustomBefore.SequenceEqual(fullCustomPad.ToBytes()), "two registered controller slots cannot retain a custom key and add two more keys");

        var fullKeyboard = SaveFile.Load(fixture);
        ConfigureGuard(fullKeyboard, new[] { "F8", "F9", "F10" }, delegate { return new[] { "Gamepad_LeftShoulder", "Gamepad_LeftShoulder", "Gamepad_LeftShoulder" }; });
        byte[] fullBefore = fullKeyboard.ToBytes(); bool rejected = false;
        try { ApplyTarget(fullKeyboard, onekey); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && fullBefore.SequenceEqual(fullKeyboard.ToBytes()), "full keyboard capacity rejects atomically without discarding distinct keys");
        var fullPad = SaveFile.Load(fixture); AddFamily(fullPad, Custom1, "PlayStation");
        ConfigureGuard(fullPad, new string[3], delegate(int family) { return family == 3 ? new[] { "Gamepad_RightShoulder", "Gamepad_RightTrigger", "Gamepad_FaceButton_Top" } : new[] { "Gamepad_LeftShoulder", "Gamepad_LeftShoulder", "Gamepad_LeftShoulder" }; });
        fullBefore = fullPad.ToBytes(); rejected = false;
        try { ApplyTarget(fullPad, onekey); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && fullBefore.SequenceEqual(fullPad.ToBytes()), "late-family capacity failure leaves earlier devices completely unchanged");

        string guiPath = NewInput("additive-gui-cli"); var guiSource = SaveFile.Load(fixture); AddFamily(guiSource, Custom1, "PlayStation");
        ConfigureGuard(guiSource, new[] { "RightMouseButton", "F8", "None" }, delegate { return new[] { "Gamepad_LeftShoulder", "None" }; });
        guiSource.Save(guiPath);
        var expected = SaveFile.Load(guiPath); ApplyTarget(expected, onekey);
        var gui = Open(guiPath); gui.SelectPreset("onekey", PresetApplyMode.Append); Assert(gui.HasChanges && gui.KeyboardChangeCount > 0 && gui.GamepadChangeCount > 0, "GUI materializes actual additive changes for both devices");
        Assert(gui.Apply().Success, "GUI additive preset applies through shared planner");
        Assert(expected.ToBytes().SequenceEqual(File.ReadAllBytes(guiPath)), "GUI and CLI planner produce corresponding byte-identical output");
        Assert(ContainsBoth(GuardKeyboard(SaveFile.Load(guiPath)), "RightMouseButton", "LeftMouseButton"), "saved GUI output independently retains both mouse guard buttons");

        string failedPath = NewInput("additive-failed-preview"); fullKeyboard.Save(failedPath);
        var failedPreview = Open(failedPath); failedPreview.SetKeyboard("IA_Jump", 0, "F8"); ExternalKeyboard(failedPath, Custom1, "IA_Jump", 0, "F9"); failedPreview.Refresh();
        failedPreview.SelectPreset("onekey_dodge"); string conflictId = failedPreview.Conflicts.Single().Id;
        rejected = false; try { failedPreview.SelectPreset("onekey", PresetApplyMode.Append); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && failedPreview.PresetId == "onekey_dodge" && failedPreview.GetKeyboard("IA_Guard", 0) == "LeftMouseButton" && failedPreview.GetKeyboard("IA_Dodge", 2) == "RightMouseButton", "rejected additive switch preserves complete previous preview");
        Assert(failedPreview.Conflicts.Single().Id == conflictId && failedPreview.GetKeyboard("IA_Jump", 0) == "F8", "rejected additive switch preserves pending conflict and draft");
        failedPreview.SelectPreset(null);
        Assert(failedPreview.GetKeyboard("IA_Guard", 0) == "F8" && failedPreview.Conflicts.Single().Id == conflictId, "failed preview selection does not corrupt cancellation checkpoint");

        var replacing = SaveFile.Load(fixture);
        ConfigureGuard(replacing, new[] { "RightMouseButton", "RightMouseButton", "RightMouseButton" }, delegate { return new[] { "Gamepad_LeftShoulder", "Gamepad_LeftShoulder", "Gamepad_LeftShoulder" }; });
        ApplyTarget(replacing, Presets.ById("onekey_dodge"));
        Assert(GuardKeyboard(replacing).All(key => key == "LeftMouseButton") && PadFor(replacing).All(keys => GuardPad(keys).All(key => key == "Gamepad_FaceButton_Left")), "onekey_dodge retains explicit replacement semantics");
        Assert(replacing.RecordFor(Custom1).Entries.Any(e => e.Name == "IA_Dodge" && e.Key == "RightMouseButton") && PadFor(replacing).All(keys => keys.Items.Any(item => SaveFile.Field(item, "MappingName") == "IA_Dodge" && SaveFile.Field(item, "Key") == "Gamepad_LeftShoulder")), "onekey_dodge still moves old defense buttons to dodge");
        Assert(PadFor(replacing).All(keys => keys.Items.Where(item => SaveFile.Field(item, "MappingName") == "IA_MoveFast").All(item => SaveFile.Field(item, "Key") == "Gamepad_LeftShoulder")), "onekey_dodge replaces the single registered sprint slot with LB");
        Assert(Presets.ById("undo_onekey") == null, "ambiguous partial-restore preset is removed");
    }

    static void RegisteredControllerDefaults()
    {
        string path = NewInput("registered-controller-defaults");
        var save = SaveFile.Load(path);
        var invalid = PadFor(save).First().Items.First(item => SaveFile.Field(item, "MappingName") == "IA_Crouch");
        SaveFile.SetField(invalid, "DefaultKey", "Gamepad_RightShoulder");
        SaveFile.SetField(invalid, "Key", "Gamepad_DPad_Up"); save.Save(path);
        var session = Open(path);
        Assert(session.GetGamepad("IA_Crouch", 0) == null && session.GetGamepadDefault("IA_Crouch", 0) == "Gamepad_LeftThumbstick", "a family row with another DefaultKey does not override the registered controller mapping");
        session.SetGamepad("IA_Crouch", 0, "Gamepad_FaceButton_Top");
        Assert(session.Apply().Success, "a registered slot with no matching family default can be added");
        save = SaveFile.Load(path);
        Assert(PadFor(save).First().Items.Any(item => SaveFile.Field(item, "MappingName") == "IA_Crouch" && SaveFile.Field(item, "DefaultKey") == "Gamepad_RightShoulder" && SaveFile.Field(item, "Key") == "Gamepad_DPad_Up"), "nonmatching family defaults remain byte-preserved raw records");
        Assert(Open(path).GetGamepad("IA_Crouch", 0) == "Gamepad_FaceButton_Top", "the matching registered family default supplies the effective current value");
        byte[] before = save.ToBytes();
        var extra = new Preset { Id = "unregistered-pad-slot" };
        extra.Gamepad.Add(new KeyBinding("IA_Guard", 2, "Gamepad_DPad_Up"));
        bool rejected = false; try { Presets.Apply(save, extra, save.RecordFor(Custom1)); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && before.SequenceEqual(save.ToBytes()), "presets reject unregistered family-only controller slots atomically");
        var malformed = SaveFile.Load(fixture);
        foreach (Node keys in PadFor(malformed)) keys.Items.RemoveAll(item => SaveFile.Field(item, "MappingName") == "IA_MoveFast");
        ApplyTarget(malformed, Presets.ById("onekey_dodge"));
        Assert(PadFor(malformed).All(keys => keys.Items.Any(item => SaveFile.Field(item, "MappingName") == "IA_MoveFast" && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::Second" && SaveFile.Field(item, "DefaultKey") == "Gamepad_RightShoulder" && SaveFile.Field(item, "Key") == "Gamepad_LeftShoulder")), "replacement preset supplies a missing registered sprint family slot");
    }

    static void SharedBlobOwnership()
    {
        string path = NewInput("shared-blob-ownership");
        var save = SaveFile.Load(path);
        var record = save.RecordFor(Custom1);
        record.Entries.Add(new BlobEntry { Name = "IA_Crouch", Slot = 0, Key = "F8", X = "None", Y = "None" });
        SetKeyboard(save, Custom1, "IA_Crouch", 1, "C");
        save.Save(path);
        var session = Open(path);
        Assert(session.GetKeyboard("IA_Crouch", 0) == "C" && session.GetKeyboardRawSlot("IA_Crouch", 0) == 1, "a keyboard-valued Blob override of controller raw slot 0 remains controller-owned");
        Assert(session.GetGamepad("IA_Crouch", 0) == "Gamepad_LeftThumbstick", "the active family CurrentKey takes precedence over the shared Blob override");
        Assert(!Enumerable.Range(0, 3).Select(index => session.GetKeyboard("IA_Crouch", index)).Contains("F8"), "shared controller overrides never appear as keyboard bindings");
        session.SetKeyboard("IA_Crouch", 0, "F9"); session.SetGamepad("IA_Crouch", 0, "Gamepad_FaceButton_Top");
        Assert(session.KeyboardChangeCount == 1 && session.GamepadChangeCount == 1, "each device's edit is counted in its own draft scope");
        Assert(session.Apply().Success, "shared Blob and family edits apply");
        save = SaveFile.Load(path);
        Assert(save.RecordFor(Custom1).Entries.Any(entry => entry.Name == "IA_Crouch" && entry.Slot == 0 && entry.Key == "F8" && entry.X == "None" && entry.Y == "None" && entry.Pad.All(value => value == 0)), "family edits preserve the original shared controller override and hardware metadata");
        Assert(save.RecordFor(Custom1).Entries.Any(entry => entry.Name == "IA_Crouch" && entry.Slot == 1 && entry.Key == "F9"), "keyboard edits target the real keyboard registration");
        session = Open(path); session.ResetAll();
        Assert(session.Apply().Success, "global reset removes shared controller overrides through their Blob storage");
        Assert(!SaveFile.Load(path).RecordFor(Custom1).Entries.Any(entry => entry.Name == "IA_Crouch"), "global reset clears both Blob device categories");
        Assert(Open(path).GetGamepad("IA_Crouch", 0) == "Gamepad_LeftThumbstick", "global reset restores the controller family default");

        path = NewInput("shared-blob-missing-family-slot"); save = SaveFile.Load(path);
        save.RecordFor(Custom1).Entries.Add(new BlobEntry { Name = "IA_Crouch", Slot = 0, Key = "None", X = "None", Y = "None" });
        foreach (Node keys in PadFor(save)) keys.Items.RemoveAll(item => SaveFile.Field(item, "MappingName") == "IA_Crouch");
        save.Save(path); session = Open(path);
        Assert(session.GetKeyboard("IA_Crouch", 0) == null && session.GetKeyboardDefault("IA_Crouch", 0) == "C", "missing family rows do not reclassify shared controller overrides as keyboard slots");
        Assert(session.GetGamepadDefault("IA_Crouch", 0) == "Gamepad_LeftThumbstick", "a missing family row inherits the projected controller default instead of Blob None");
        session.SetGamepad("IA_Crouch", 0, "Gamepad_FaceButton_Top");
        Assert(session.Apply().Success && Open(path).GetGamepad("IA_Crouch", 0) == "Gamepad_FaceButton_Top", "a missing family slot can be edited without altering its shared Blob override");
    }

    static void LimitedSlotCapacity()
    {
        string path = NewInput("limited-keyboard-slots");
        var save = SaveFile.Load(path);
        foreach (Node keys in PadFor(save))
        {
            var template = keys.Items.First(item => SaveFile.Field(item, "MappingName") == "IA_Jump");
            for (int raw = 2; raw <= 6; raw++)
            {
                var item = template.Select(SaveFile.CloneNode).ToList();
                SaveFile.SetField(item, "Slot", InputDefaults.SlotName(raw));
                SaveFile.SetField(item, "DefaultKey", "None"); keys.Items.Add(item);
            }
        }
        save.Save(path);
        var session = Open(path);
        Assert(session.HasKeyboardSlot("IA_Jump", 0) && !session.HasKeyboardSlot("IA_Jump", 1), "keyboard reports only the raw slots remaining after controller ownership");
        Assert(session.GetKeyboard("IA_Jump", 1) == null && session.GetKeyboardDefault("IA_Jump", 1) == null && session.GetKeyboardRawSlot("IA_Jump", 1) == -1, "unavailable keyboard columns have no binding, default or raw slot");
        bool rejected = false;
        try { session.SetKeyboard("IA_Jump", 1, "F8"); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && !session.HasAnyDrafts, "editing an unavailable keyboard column reports capacity without a draft");
        byte[] before = save.ToBytes();
        var preset = new Preset { Id = "limited-capacity-test" };
        preset.Keyboard.Add(new KeyBinding("IA_Jump", 0, "F8")); preset.Keyboard.Add(new KeyBinding("IA_Jump", 1, "F9"));
        rejected = false;
        try { Presets.Apply(save, preset, save.RecordFor(Custom1)); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && before.SequenceEqual(save.ToBytes()), "presets reject unavailable keyboard columns before changing bindings");

        path = NewInput("limited-controller-slots"); save = SaveFile.Load(path);
        for (int raw = 2; raw <= 6; raw++) SetKeyboard(save, Custom1, "IA_Jump", raw, "F8");
        save.Save(path); session = Open(path);
        Assert(session.HasGamepadSlot("IA_Jump", 0) && !session.HasGamepadSlot("IA_Jump", 1), "controller reports only its remaining registered raw slot");
        Assert(session.GetGamepad("IA_Jump", 1) == null && session.GetGamepadDefault("IA_Jump", 1) == null && session.GetGamepadRawSlot("IA_Jump", 1) == -1, "unavailable controller columns return no value without indexing beyond capacity");
        rejected = false;
        try { session.SetGamepad("IA_Jump", 1, "Gamepad_DPad_Up"); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && !session.HasAnyDrafts, "editing an unavailable controller column reports capacity without a draft");
    }

    static void ResourceSlotRoundtrip()
    {
        Assert(InputDefaults.RawSlot("EPlayerMappableKeySlot::Unspecified") == 7 && InputDefaults.SlotName(7) == "EPlayerMappableKeySlot::Unspecified", "slot 7 retains the native Unspecified enum identity");
        Assert(InputDefaults.Slots("IA_Guard", false, new[] { 7 }, new int[0]).SequenceEqual(new[] { 0, 2, 4 }), "Unspecified keyboard records do not occupy ordinary editing slots");
        Assert(InputDefaults.Slots("IA_Guard", true, new[] { 7 }, new int[0]).SequenceEqual(new[] { 1, 3, 5 }), "Unspecified controller records do not occupy ordinary editing slots");
        Assert(InputDefaults.Slots("IA_Jump", false, new int[0], new[] { 1, 2, 3, 4, 5, 6 }).SequenceEqual(new[] { 0 }), "exhausted real slots never allocate Unspecified as an extra slot");
        string path = NewInput("resource-slots");
        var session = Open(path);
        foreach (string action in new[] { "IA_Crouch", "IA_OpenCharacterMenu", "IA_UseBottomItem", "IA_UseTopItem" })
        {
            Assert(session.GetKeyboardRawSlot(action, 0) == 1 && session.GetGamepadRawSlot(action, 0) == 0, action + " uses resource registration order rather than device parity");
            session.SetKeyboard(action, 0, "F8");
            session.SetGamepad(action, 0, "Gamepad_FaceButton_Top");
        }
        Assert(session.GetKeyboardDefault("IA_Guard", 2) == "None" && !session.HasGamepadSlot("IA_Guard", 2) && session.GetGamepadDefault("IA_Guard", 2) == null, "only keyboard can allocate an extra guard slot without a factory registration");
        Assert(Enumerable.Range(0, 3).Select(index => session.GetGamepadRawSlot("IA_ShadowArm", index)).SequenceEqual(new[] { 1, 2, 3 }), "all three registered shadow-arm controller slots remain editable");
        bool rejected = false; try { session.SetGamepad("IA_Guard", 2, "Gamepad_DPad_Up"); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && !session.IsGamepadChanged("IA_Guard", 2), "unregistered controller slot creation rejects without a draft");
        var result = session.Apply(); Assert(result.Success, "resource slot edits apply: " + result.Error);
        var saved = SaveFile.Load(path);
        foreach (string action in new[] { "IA_Crouch", "IA_OpenCharacterMenu", "IA_UseBottomItem", "IA_UseTopItem" })
        {
            Assert(saved.RecordFor(Custom1).Entries.Any(entry => entry.Name == action && entry.Slot == 1 && entry.Key == "F8"), action + " keyboard edit preserves raw slot 1");
            Assert(PadFor(saved).All(keys => keys.Items.Any(item => SaveFile.Field(item, "MappingName") == action && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::First" && SaveFile.Field(item, "Key") == "Gamepad_FaceButton_Top")), action + " controller edit preserves raw slot 0");
        }
        Assert(PadFor(saved).All(keys => !keys.Items.Any(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::Sixth")), "rejected extra controller slots are not serialized");
        var extraTemplate = PadFor(saved).First().Items.First(item => SaveFile.Field(item, "MappingName") == "IA_Guard").Select(SaveFile.CloneNode).ToList();
        SaveFile.SetField(extraTemplate, "Slot", "EPlayerMappableKeySlot::Sixth");
        SaveFile.SetField(extraTemplate, "DefaultKey", "None"); SaveFile.SetField(extraTemplate, "Key", "Gamepad_DPad_Up");
        PadFor(saved).First().Items.Add(extraTemplate); saved.Save(path);
        var reopened = Open(path);
        Assert(reopened.GetKeyboard("IA_Crouch", 0) == "F8" && reopened.GetGamepad("IA_Crouch", 0) == "Gamepad_FaceButton_Top", "non-parity edits survive reopening");
        Assert(!reopened.HasGamepadSlot("IA_Guard", 2) && reopened.GetGamepad("IA_Guard", 2) == null, "saved unregistered controller rows remain outside ordinary editing columns");
        reopened.SetKeyboard("IA_Jump", 0, "F9"); Assert(reopened.Apply().Success, "unrelated edit preserves extra controller rows");
        Assert(SaveFile.Load(path).GamepadKeysFor(Custom1).First().Items.Any(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::Sixth" && SaveFile.Field(item, "DefaultKey") == "None" && SaveFile.Field(item, "Key") == "Gamepad_DPad_Up"), "saved extra controller row keeps its raw metadata");

    }

    public static int Main(string[] args)
    {
        try
        {
            fixture = Path.GetFullPath(args.Length > 0 ? args[0] : "samples\\vanilla-base.sav");
            byte[] original = File.ReadAllBytes(fixture);
            root = Path.Combine(Path.GetTempPath(), "PBZKeyForge-editor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Assert(original.SequenceEqual(SaveFile.Load(fixture).ToBytes()), "new byte API preserves fixture roundtrip");
            RefreshAndConflicts(); ScopeAndSwitch(); Preview(); ScopedPadAndMetadata(); ExplicitUnbinding(); ProfilePadMerging(); ProfilePresetAndUnsupportedData(); PresetMetadataParity(); ResetAndRace(); AdditiveGuardPresets(); ResourceSlotRoundtrip(); LimitedSlotCapacity(); SharedBlobOwnership(); RegisteredControllerDefaults();
            if (args.Length >= 3) AuthenticatedApplication(args[1], args[2]);
            Assert(original.SequenceEqual(File.ReadAllBytes(fixture)), "original fixture unchanged");
            Console.WriteLine("EditorSessionTests PASS: " + assertions + " assertions; copies: " + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
