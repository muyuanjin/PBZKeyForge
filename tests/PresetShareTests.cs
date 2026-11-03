using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PBZKeyForge;

public static class PresetShareTests
{
    const string Profile = "PBZ.KeyProfile.Custom1";
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Code(params byte[] envelope)
    { return "PBZ2:少主剑:" + Convert.ToBase64String(envelope).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    static byte[] Payload(string code)
    { var text = code.Substring(code.LastIndexOf(':') + 1).Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '=')); }
    static void Reject(string code, bool upgrade)
    {
        try { UserPresetStore.ParseShare(code); throw new Exception("Accepted unsupported or incomplete code"); }
        catch (InvalidDataException ex) { Assert(!upgrade || ex.Message == I18n.Str("preset.shareupgrade"), "Unsupported semantics asks for upgrade"); }
    }
    static void SameKeys(EditorSession session, string action, string family, params string[] expected)
    {
        int index = family == null ? -1 : session.ControllerFamilies.Single(item => item.Name == family).Index;
        var actual = session.Relations(action, family != null, index).Keys;
        Assert(!actual.Except(expected).Any() && !expected.Except(actual).Any(), action + "/" + family + ": " + string.Join("/", actual));
    }
    static EditorSession Open(string input)
    { var session = EditorSession.Open(input); session.SwitchProfile(Profile); return session; }
    static void OutsideScopes(EditorSession session, byte[] beforeBytes, Preset preset)
    {
        var before = SaveFile.FromBytes(beforeBytes); var after = SaveFile.FromBytes(session.ExportCurrentScheme());
        if (preset.Scopes.Any(scope => scope.Family == null))
        {
            var beforeEntries = before.RecordFor(Profile).Entries;
            var afterEntries = after.RecordFor(Profile).Entries;
            Func<BlobEntry, bool> sharedPad = entry => InputDefaults.RegisteredSlots(entry.Name, true).Contains(entry.Slot);
            Assert(beforeEntries.Where(sharedPad).Select(entry => entry.Name + "/" + entry.Slot + "/" + entry.Key)
                .SequenceEqual(afterEntries.Where(sharedPad).Select(entry => entry.Name + "/" + entry.Slot + "/" + entry.Key)), "Keyboard-only preserves shared controller overrides");
            after.RecordFor(Profile).Entries = beforeEntries;
        }
        foreach (var scope in preset.Scopes.Where(scope => scope.Family != null))
            after.GamepadProfiles.Single(item => item.ProfileId == Profile && item.Family == scope.Family).Keys.Items
                = before.GamepadProfiles.Single(item => item.ProfileId == Profile && item.Family == scope.Family).Keys.Items;
        Assert(after.ToBytes().SequenceEqual(beforeBytes), "Unselected devices, other profiles and unrelated bytes remain identical");
    }
    public static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] original = File.ReadAllBytes(args[0]);
            var multi = SaveFile.FromBytes(original);
            var fields = multi.GamepadProfiles.Single(item => item.ProfileId == Profile).Fields.Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(fields, "Family", "PS"); multi.Props.Single(node => node.Name == "GamepadFamilyProfiles").Items.Add(fields);
            string input = Path.Combine(output, "multi.sav"); multi.Save(input); byte[] fixture = File.ReadAllBytes(input);

            // Independent protobuf vector: baseline=1, keyboard scope, action=3, packed added=[1].
            string golden = Code(0, 8, 1, 18, 7, 18, 5, 8, 3, 18, 1, 1);
            var one = UserPresetStore.ParseShare(golden);
            Assert(one.BaselineId == 1 && one.Scopes.Count == 1 && one.Scopes[0].Family == null, "Golden vector has explicit keyboard scope");
            Assert(one.UserMappings.Single().Action == "IA_Guard" && one.UserMappings[0].Added.SequenceEqual(new[] { "LeftMouseButton" }), "Stable action/key numbers");
            Assert(UserPresetStore.Share(one) == golden, "Encoder matches independent protobuf vector and omits empty arrays");
            Assert(Payload(golden)[0] == 0, "Tiny protobuf stays uncompressed");
            var unknownMetadata = Payload(golden).Concat(new byte[] { 120, 1 }).ToArray();
            Assert(UserPresetStore.Share(UserPresetStore.ParseShare(Code(unknownMetadata))) == golden, "Unknown metadata can be skipped");
            Reject(Code(0, 8, 99, 18, 0), true);
            Reject(Code(0, 8, 1, 18, 2, 8, 3), true);
            Reject(Code(0, 8, 1, 18, 7, 18, 5, 8, 99, 18, 1, 1), true);
            Reject(Code(0, 8, 1, 18, 8, 18, 6, 8, 3, 18, 2, 208, 15), true);
            Reject(Code(2, 8, 1, 18, 0), true);
            Reject(Code(0, 8, 1, 18, 7, 18, 5, 8, 3, 18, 1), false);
            Reject(Code(0, 8, 1), false);
            Reject(Code(0, 8, 1, 18, 0, 18, 0), false);
            Reject("PBZ3:test:anything", false);

            var session = Open(input);
            session.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
            for (int slot = 0; slot < 3; slot++) session.SetKeyboard("IA_HeavyAttack", slot, "None");
            session.SetGamepadFamily("IA_Guard", 0, session.ControllerFamilies.Single(item => item.Name == "Xbox").Index, "Gamepad_FaceButton_Left");
            var captured = UserPresetStore.Capture(session, "少主剑:攻防");
            Assert(captured.Scopes.Count == 3 && captured.UserMappings.Count == 3, "Capture records unchanged device scopes separately from diff");
            foreach (string scope in new[] { "all", "keyboard", "Xbox", "PS" })
            {
                var value = UserPresetStore.SelectScopes(captured, captured.Scopes.Where(item => scope == "all" || (scope == "keyboard" ? item.Family == null : item.Family == scope)));
                string code = UserPresetStore.Share(value); var decoded = UserPresetStore.ParseShare(code);
                Assert(decoded.Scopes.Select(item => item.Family).OrderBy(item => item).SequenceEqual(value.Scopes.Select(item => item.Family).OrderBy(item => item)), "Scope survives " + scope);
                Assert(UserPresetStore.Share(decoded) == code, "Canonical roundtrip " + scope);
                Console.WriteLine(scope + " chars=" + code.Length + " " + code);
            }
            var store = new UserPresetStore(Path.Combine(output, "library"));
            var keyboard = UserPresetStore.SelectScopes(captured, captured.Scopes.Where(scope => scope.Family == null));
            var saved = store.Save(keyboard, "键鼠");
            Assert(store.Load().Single().Scopes.Single().Family == null && store.Load().Single().BaselineId == 1, "Library retains explicit scope and baseline");
            store.Delete(new[] { saved });

            var pad = UserPresetStore.SelectScopes(captured, captured.Scopes.Where(scope => scope.Family == "Xbox"));
            var emptyPad = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.SelectScopes(captured, captured.Scopes.Where(scope => scope.Family == "PS"))));
            Assert(emptyPad.UserMappings.Count == 0 && emptyPad.Scopes.Single().Family == "PS", "Empty diff retains reset scope");
            foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
            {
                var target = Open(input); target.SetKeyboard("IA_Interact", 0, "F11");
                foreach (var family in target.ControllerFamilies) target.SetGamepadFamily("IA_Guard", 0, family.Index, "Gamepad_FaceButton_Top");
                byte[] beforeScope = target.ExportCurrentScheme();
                target.StagePreset(keyboard, mode);
                OutsideScopes(target, beforeScope, keyboard);
                SameKeys(target, "IA_Guard", null, "RightMouseButton", "LeftMouseButton");
                SameKeys(target, "IA_HeavyAttack", null, mode == PresetApplyMode.Append ? new[] { "F" } : new string[0]);
                SameKeys(target, "IA_Interact", null, mode == PresetApplyMode.OnlyPreset ? "E" : "F11");
                foreach (var family in target.ControllerFamilies) SameKeys(target, "IA_Guard", family.Name, "Gamepad_FaceButton_Top", "Gamepad_LeftShoulder");
                target.SelectPreset(null);
                if (mode == PresetApplyMode.Append)
                {
                    int count = target.ScopeChangeCount; bool full = false;
                    try { target.StagePreset(pad, mode); } catch (InvalidOperationException) { full = true; }
                    Assert(full && target.ScopeChangeCount == count && target.PresetId == null, "Controller append rejects full slots without changing any scope");
                }
                else target.StagePreset(pad, mode);
                OutsideScopes(target, beforeScope, pad);
                SameKeys(target, "IA_Interact", null, "F11");
                SameKeys(target, "IA_Guard", "PS", "Gamepad_FaceButton_Top", "Gamepad_LeftShoulder");
                SameKeys(target, "IA_Guard", "Xbox", mode == PresetApplyMode.Append
                    ? new[] { "Gamepad_FaceButton_Top", "Gamepad_LeftShoulder" }
                    : new[] { "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left" });
            }
            var reset = Open(input); reset.SetKeyboard("IA_Interact", 0, "F11");
            foreach (var family in reset.ControllerFamilies) reset.SetGamepadFamily("IA_Guard", 0, family.Index, "Gamepad_FaceButton_Top");
            byte[] beforeReset = reset.ExportCurrentScheme();
            reset.StagePreset(emptyPad, PresetApplyMode.OnlyPreset);
            OutsideScopes(reset, beforeReset, emptyPad);
            SameKeys(reset, "IA_Guard", "PS", "Gamepad_LeftShoulder");
            SameKeys(reset, "IA_Guard", "Xbox", "Gamepad_FaceButton_Top", "Gamepad_LeftShoulder");
            SameKeys(reset, "IA_Interact", null, "F11");

            var dense = new Preset { UserName = "大量改键", BaselineId = 1, Scopes = new List<PresetScope> { new PresetScope() },
                UserMappings = PresetBaseline.Actions.Select(action => new PresetMapping { Action = action, Added = new[] { "F8" } }).ToList() };
            string denseCode = UserPresetStore.Share(dense);
            Assert(Payload(denseCode)[0] == 1 && UserPresetStore.ParseShare(denseCode).UserMappings.Count == 21, "Larger repeated data selects DEFLATE and roundtrips");
            var literals = new Preset { UserName = "额外按键", BaselineId = 1, Scopes = new List<PresetScope> { new PresetScope { Family = "CustomFamily" } },
                UserMappings = new List<PresetMapping> { new PresetMapping { Action = "IA_Guard", Family = "CustomFamily", Added = new[] { "MouseScrollUp" } } } };
            var decodedLiterals = UserPresetStore.ParseShare(UserPresetStore.Share(literals));
            Assert(decodedLiterals.Scopes.Single().Family == "CustomFamily" && decodedLiterals.UserMappings.Single().Added.Single() == "MouseScrollUp", "Typed engine keys and save family names retain literal identities");
            foreach (string action in PresetBaseline.Actions)
                foreach (bool gamepad in new[] { false, true })
                    Assert(PresetBaseline.Keys(1, action, gamepad).OrderBy(key => key).SequenceEqual(InputDefaults.ForAction(action, gamepad).OrderBy(key => key)), "Baseline 1 matches verified resource defaults " + action + "/" + gamepad);
            var missing = Open(input); int before = missing.ScopeChangeCount;
            bool rejected = false; try { missing.StagePreset(literals, PresetApplyMode.OnlyPreset); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && missing.ScopeChangeCount == before, "Missing included family rejects even Only preset atomically");
            Assert(original.SequenceEqual(File.ReadAllBytes(args[0])) && fixture.SequenceEqual(File.ReadAllBytes(input)), "All checks preserve source saves");
            Console.WriteLine("PresetShareTests PASS: " + checks + " checks; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
