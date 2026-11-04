using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    public enum PresetApplyMode { Append, Replace, OnlyPreset }

    public class KeyBinding
    {
        public string Action;
        public int Slot;      // Logical editing column; raw slots depend on the action and device.
        public string Key;
        public KeyBinding(string a, int s, string k) { Action = a; Slot = s; Key = k; }
    }

    public sealed class PresetMapping
    {
        public string Action;
        public string Family;
        public string[] Added = new string[0], Removed = new string[0];
    }

    public sealed class PresetScope
    {
        // Null identifies keyboard/mouse; controller names match the save's Family.
        public string Family;
    }

    public class Preset
    {
        public string Id;
        public string UserName;
        public List<PresetMapping> UserMappings;
        public uint BaselineId;
        public List<PresetScope> Scopes;
        public bool IsUser { get { return UserMappings != null; } }
        public bool Vanilla;
        public bool CurrentProfileVanilla;
        public bool AdditiveGuard;
        public List<KeyBinding> Keyboard = new List<KeyBinding>();
        public List<KeyBinding> Gamepad = new List<KeyBinding>();
        public string Name { get { return IsUser ? UserName : I18n.Str("preset." + Id + ".name"); } }
        public string Desc { get { return IsUser ? I18n.Str("preset.devices", UserPresetStore.ScopeText(this)) : I18n.Str("preset." + Id + ".desc"); } }
        public override string ToString() { return Name; }
    }

    /// <summary>Built-in presets and the edit operations they map to.</summary>
    public static class Presets
    {
        public static List<Preset> All()
        {
            var list = new List<Preset>();
            list.Add(new Preset { Id = "vanilla_current", CurrentProfileVanilla = true });

            // 一键攻防
            var p1 = new Preset { Id = "onekey", AdditiveGuard = true };
            list.Add(p1);

            var mouseGuard = new Preset { Id = "mouse_guard" };
            mouseGuard.Keyboard.Add(new KeyBinding("IA_LightAttack", 0, "LeftMouseButton"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_LightAttack", 1, "None"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_LightAttack", 2, "None"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_Guard", 0, "RightMouseButton"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_Guard", 1, "LeftMouseButton"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_Guard", 2, "None"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_HeavyAttack", 0, "F"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_HeavyAttack", 1, "None"));
            mouseGuard.Keyboard.Add(new KeyBinding("IA_HeavyAttack", 2, "RightMouseButton"));
            list.Add(mouseGuard);

            var mouseGuardSpace = new Preset { Id = "mouse_guard_space" };
            mouseGuardSpace.Keyboard.AddRange(mouseGuard.Keyboard.Select(binding => new KeyBinding(binding.Action, binding.Slot, binding.Key)));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_Dodge", 0, "LeftShift"));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_Dodge", 1, "LeftShift"));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_Dodge", 2, "SpaceBar"));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_MoveFast", 0, "W"));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_MoveFast", 1, "A"));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_MoveFast", 2, "D"));
            mouseGuardSpace.Keyboard.Add(new KeyBinding("IA_Jump", 0, "SpaceBar"));
            list.Add(mouseGuardSpace);

            // 一键攻防 + 防御键改闪避/冲刺
            var p2 = new Preset { Id = "onekey_dodge" };
            // This preset deliberately moves guard away from the old defense key.
            // It is separate from the additive attack/guard preset above.
            for (int i = 0; i < 3; i++) p2.Keyboard.Add(new KeyBinding("IA_Guard", i, "LeftMouseButton"));
            p2.Gamepad.Add(new KeyBinding("IA_Guard", 0, "Gamepad_FaceButton_Left"));
            p2.Gamepad.Add(new KeyBinding("IA_Guard", 1, "Gamepad_FaceButton_Left"));
            p2.Keyboard.Add(new KeyBinding("IA_Dodge", 0, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_Dodge", 1, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_Dodge", 2, "RightMouseButton"));
            p2.Keyboard.Add(new KeyBinding("IA_MoveFast", 0, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_MoveFast", 1, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_MoveFast", 2, "RightMouseButton"));
            p2.Gamepad.Add(new KeyBinding("IA_Dodge", 0, "Gamepad_RightShoulder"));
            p2.Gamepad.Add(new KeyBinding("IA_Dodge", 1, "Gamepad_LeftShoulder"));
            p2.Gamepad.Add(new KeyBinding("IA_MoveFast", 0, "Gamepad_LeftShoulder"));
            list.Add(p2);

            // 魂系手柄布局
            var p3 = new Preset { Id = "souls" };
            p3.Gamepad.Add(new KeyBinding("IA_LightAttack", 0, "Gamepad_RightShoulder"));
            p3.Gamepad.Add(new KeyBinding("IA_HeavyAttack", 0, "Gamepad_RightTrigger"));
            p3.Gamepad.Add(new KeyBinding("IA_Guard", 0, "Gamepad_LeftShoulder"));
            p3.Gamepad.Add(new KeyBinding("IA_Dodge", 0, "Gamepad_FaceButton_Bottom"));
            p3.Gamepad.Add(new KeyBinding("IA_MoveFast", 0, "Gamepad_FaceButton_Bottom"));
            p3.Gamepad.Add(new KeyBinding("IA_Interact", 0, "Gamepad_FaceButton_Top"));
            p3.Gamepad.Add(new KeyBinding("IA_Execute", 0, "Gamepad_FaceButton_Top"));
            list.Add(p3);

            // 空格 = 高速移动
            var p4 = new Preset { Id = "space_sprint" };
            p4.Keyboard.Add(new KeyBinding("IA_MoveFast", 0, "LeftShift"));
            p4.Keyboard.Add(new KeyBinding("IA_MoveFast", 1, "LeftShift"));
            p4.Keyboard.Add(new KeyBinding("IA_MoveFast", 2, "SpaceBar"));
            list.Add(p4);

            // 空格 = 闪避
            var p5 = new Preset { Id = "space_dodge" };
            p5.Keyboard.Add(new KeyBinding("IA_Dodge", 0, "LeftShift"));
            p5.Keyboard.Add(new KeyBinding("IA_Dodge", 1, "LeftShift"));
            p5.Keyboard.Add(new KeyBinding("IA_Dodge", 2, "SpaceBar"));
            list.Add(p5);

            // 全部恢复默认
            list.Add(new Preset { Id = "vanilla", Vanilla = true });
            return list;
        }

        public static Preset ById(string id)
        {
            return All().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static SaveFile FactoryPreview(SaveFile source, Preset preset, string profile)
        {
            var result = source.Clone();
            Apply(result, ById("vanilla_current"), result.RecordFor(profile));
            if (preset.IsUser) Apply(result, preset, result.RecordFor(profile), PresetApplyMode.Replace);
            else Apply(result, preset, result.RecordFor(profile));
            return result;
        }

        public static void Apply(SaveFile save, Preset preset, BlobRecord record, PresetApplyMode mode)
        {
            var defaults = save.Defaults;
            if (preset.Vanilla || preset.CurrentProfileVanilla) { Apply(save, preset, record); return; }
            if (preset.IsUser) UserPresetStore.Validate(preset);
            if (ControllerProfiles.NeedsPresetFamilies(save, preset, record.ProfileId))
            {
                var planned = save.Clone();
                ControllerProfiles.PreparePreset(planned, preset, record.ProfileId);
                Apply(planned, preset, planned.RecordFor(record.ProfileId), mode);
                save.Props = planned.Props;
                record.Entries = planned.RecordFor(record.ProfileId).Entries;
                return;
            }
            if (mode == PresetApplyMode.OnlyPreset)
            {
                if (preset.IsUser)
                {
                    var planned = save.Clone();
                    var plannedRecord = planned.RecordFor(record.ProfileId);
                    var familiesBefore = planned.GamepadKeysFor(record.ProfileId);
                    if (preset.Scopes.Any(scope => scope.Family == null))
                        plannedRecord.Entries.RemoveAll(entry => !IsGamepadOverride(familiesBefore, entry, defaults) && !defaults.IsMirroredSlot(entry.Name, entry.Slot));
                    foreach (var family in planned.GamepadProfiles.Where(family => family.ProfileId == record.ProfileId
                        && preset.Scopes.Any(scope => scope.Family == family.Family)))
                        foreach (var item in family.Keys.Items)
                        {
                            string key = SaveFile.Field(item, "DefaultKey");
                            if (!defaults.IsMirroredSlot(SaveFile.Field(item, "MappingName"), InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))) && key != null)
                                ControllerProfiles.SetKey(planned, item, key);
                        }
                    var complete = new Preset { UserName = preset.Name, BaselineId = preset.BaselineId, Scopes = preset.Scopes,
                        UserMappings = preset.Scopes.SelectMany(scope => PresetBaseline.Actions.Select(action =>
                            preset.UserMappings.FirstOrDefault(mapping => mapping.Action == action && mapping.Family == scope.Family)
                            ?? new PresetMapping { Action = action, Family = scope.Family })).ToList() };
                    Apply(planned, complete, plannedRecord, PresetApplyMode.Replace);
                    record.Entries = plannedRecord.Entries;
                    var originalFamilies = save.GamepadKeysFor(record.ProfileId);
                    var resultFamilies = planned.GamepadKeysFor(record.ProfileId);
                    for (int family = 0; family < originalFamilies.Count; family++) ControllerProfiles.CopyKeys(save, originalFamilies[family], resultFamilies[family]);
                    return;
                }
                var onlyTarget = FactoryPreview(save, preset, record.ProfileId);
                var onlyRecord = onlyTarget.RecordFor(record.ProfileId);
                record.Entries = onlyRecord.Entries;
                var savedFamilies = save.GamepadKeysFor(record.ProfileId);
                var plannedFamilies = onlyTarget.GamepadKeysFor(record.ProfileId);
                for (int family = 0; family < savedFamilies.Count; family++) ControllerProfiles.CopyKeys(save, savedFamilies[family], plannedFamilies[family]);
                return;
            }
            var commits = new List<System.Action>();
            var families = save.GamepadKeysFor(record.ProfileId);
            var target = preset.IsUser ? null : FactoryPreview(save, preset, record.ProfileId);
            var targetRecord = target == null ? null : target.RecordFor(record.ProfileId);
            var targetFamilies = target == null ? null : target.GamepadKeysFor(record.ProfileId);
            var keyboardActions = preset.IsUser ? preset.UserMappings.Where(mapping => mapping.Family == null).Select(mapping => mapping.Action)
                : preset.Keyboard.Select(binding => binding.Action).Concat(preset.AdditiveGuard ? new[] { "IA_Guard" } : new string[0]).Distinct();
            foreach (string action in keyboardActions)
            {
                int[] rawSlots = KeyboardRawSlots(record, families, action, defaults);
                var entries = rawSlots.Select(raw => record.Entries.Where(entry => entry.Name == action && entry.Slot == raw).ToArray()).ToArray();
                var values = entries.Select((group, index) => group.Length > 0 ? group.Select(entry => entry.Key).ToList()
                    : new List<string> { defaults.ForRawSlot(action, false, rawSlots[index]) }).ToArray();
                string[] required = preset.IsUser ? UserKeys(preset, preset.UserMappings.First(mapping => mapping.Family == null && mapping.Action == action)) : defaults.ResolveKeys(action, false, targetRecord.Entries.Where(entry => entry.Name == action && !IsGamepadOverride(targetFamilies, entry, defaults))
                    .Select(entry => new KeyValuePair<int, string>(entry.Slot, entry.Key)));
                required = required.Except(defaults.FixedForAction(action, false)).ToArray();
                var changes = PlanSlots(values, required, mode == PresetApplyMode.Append,
                    I18n.Str("editor.keyboard") + " · " + I18n.Str("act." + action));
                foreach (var change in changes)
                {
                    int index = change.Key; string key = change.Value;
                    commits.Add(delegate {
                        if (entries[index].Length == 0)
                            record.Entries.Add(new BlobEntry { Name = action, Slot = (byte)rawSlots[index], Key = key, X = "None", Y = "None" });
                        else foreach (var entry in entries[index]) entry.Key = key;
                    });
                }
            }
            var padActions = preset.IsUser ? preset.UserMappings.Where(mapping => mapping.Family != null).Select(mapping => mapping.Action).Distinct()
                : preset.Gamepad.Select(binding => binding.Action).Concat(preset.AdditiveGuard ? new[] { "IA_Guard" } : new string[0]).Distinct();
            for (int family = 0; family < families.Count; family++)
            {
                var keys = families[family]; var targetKeys = targetFamilies == null ? null : targetFamilies[family];
                string familyName = save.GamepadProfiles.First(item => item.ProfileId == record.ProfileId && item.Keys == keys).Family;
                foreach (string action in padActions)
                {
                    var userMapping = preset.IsUser ? preset.UserMappings.FirstOrDefault(mapping => mapping.Family == familyName && mapping.Action == action) : null;
                    if (preset.IsUser && userMapping == null) continue;
                    int[] rawSlots = defaults.EditableRegisteredSlots(action, true);
                    var entries = rawSlots.Select(raw => keys.Items.Where(item => SaveFile.Field(item, "MappingName") == action
                        && SaveFile.Field(item, "Slot") == InputDefaults.SlotName(raw) && SaveFile.Field(item, "DefaultKey") == defaults.ForRawSlot(action, true, raw)).ToArray()).ToArray();
                    var values = entries.Select((group, index) => group.Length > 0 ? group.Select(item => SaveFile.Field(item, "Key")).ToList()
                        : new List<string> { defaults.ForRawSlot(action, true, rawSlots[index]) }).ToArray();
                    string[] required = preset.IsUser ? UserKeys(preset, userMapping) : defaults.ResolveKeys(action, true, targetKeys.Items.Where(item => SaveFile.Field(item, "MappingName") == action
                        && defaults.EditableRegisteredSlots(action, true).Contains(InputDefaults.RawSlot(SaveFile.Field(item, "Slot")))
                        && SaveFile.Field(item, "DefaultKey") == defaults.ForRawSlot(action, true, InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))))
                        .Select(item => new KeyValuePair<int, string>(InputDefaults.RawSlot(SaveFile.Field(item, "Slot")), SaveFile.Field(item, "Key"))));
                    var changes = PlanSlots(values, required, mode == PresetApplyMode.Append,
                        I18n.Str("editor.gamepad") + " #" + (family + 1) + " · " + I18n.Str("act." + action));
                    var template = ControllerProfiles.KeyTemplate(keys, action);
                    foreach (var change in changes)
                    {
                        int index = change.Key; string key = change.Value;
                        commits.Add(delegate {
                            if (entries[index].Length > 0) foreach (var item in entries[index]) ControllerProfiles.SetKey(save, item, key);
                            else
                            {
                                var item = template.Select(SaveFile.CloneNode).ToList();
                                SaveFile.SetField(item, "MappingName", action); SaveFile.SetField(item, "Slot", InputDefaults.SlotName(rawSlots[index]));
                                SaveFile.SetField(item, "DefaultKey", defaults.ForRawSlot(action, true, rawSlots[index])); SaveFile.SetField(item, "Key", key);
                                keys.Items.Add(item);
                                ControllerProfiles.InitializeStorage(save);
                            }
                        });
                    }
                }
            }
            foreach (var commit in commits) commit();
        }

        static string[] UserKeys(Preset preset, PresetMapping mapping)
        {
            return PresetBaseline.Keys(preset.BaselineId, mapping.Action, mapping.Family != null).Except(mapping.Removed).Concat(mapping.Added).Distinct().ToArray();
        }

        /// <summary>Apply to one profile's keyboard and controller records; only vanilla resets all profiles.</summary>
        public static void Apply(SaveFile sav, Preset preset, BlobRecord record)
        {
            var defaults = sav.Defaults;
            if (ControllerProfiles.NeedsPresetFamilies(sav, preset, record.ProfileId))
            {
                var planned = sav.Clone();
                ControllerProfiles.PreparePreset(planned, preset, record.ProfileId);
                Apply(planned, preset, planned.RecordFor(record.ProfileId));
                sav.Props = planned.Props;
                record.Entries = planned.RecordFor(record.ProfileId).Entries;
                return;
            }
            // ProfileId owns the relationship. Family (for example Xbox) can recur
            // independently in Default, Custom1 and Custom2, and is not shared scope.
            List<Node> gamepadKeys = sav.GamepadKeysFor(record.ProfileId);
            if (preset.AdditiveGuard)
            {
                ApplyAdditiveGuard(sav, gamepadKeys, record);
                return;
            }
            if (preset.Vanilla)
            {
                foreach (var r in sav.Records) r.Entries.RemoveAll(entry => !defaults.IsMirroredSlot(entry.Name, entry.Slot));
                foreach (var keys in sav.GamepadKeys)
                    foreach (var item in keys.Items)
                    {
                        string key = SaveFile.Field(item, "DefaultKey");
                        if (!defaults.IsMirroredSlot(SaveFile.Field(item, "MappingName"), InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))) && key != null) ControllerProfiles.SetKey(sav, item, key);
                    }
                return;
            }
            if (preset.CurrentProfileVanilla)
            {
                record.Entries.RemoveAll(entry => !defaults.IsMirroredSlot(entry.Name, entry.Slot));
                foreach (var keys in gamepadKeys)
                    foreach (var item in keys.Items)
                    {
                        string defaultKey = SaveFile.Field(item, "DefaultKey");
                        if (!defaults.IsMirroredSlot(SaveFile.Field(item, "MappingName"), InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))) && defaultKey != null) ControllerProfiles.SetKey(sav, item, defaultKey);
                    }
                return;
            }

            foreach (var group in preset.Keyboard.GroupBy(binding => binding.Action))
                if (group.Any(binding => binding.Slot >= KeyboardRawSlots(record, gamepadKeys, group.Key, defaults).Length))
                    throw new InvalidOperationException(I18n.Str("preset.capacity", I18n.Str("tab.kb")));
            foreach (Node keys in gamepadKeys)
                foreach (var group in preset.Gamepad.GroupBy(binding => binding.Action))
                    if (group.Any(binding => binding.Slot >= PadRawSlots(record, keys, group.Key, defaults).Length))
                        throw new InvalidOperationException(I18n.Str("preset.capacity", I18n.Str("tab.pad")));

            foreach (var group in preset.Keyboard.GroupBy(b => b.Action))
            {
                var slots = group.ToDictionary(b => b.Slot, b => b.Key);
                string primary = slots.ContainsKey(0) ? slots[0] : slots.Values.First();
                int[] rawSlots = KeyboardRawSlots(record, gamepadKeys, group.Key, defaults);
                for (int i = 0; i < rawSlots.Length; i++)
                {
                    string key = slots.ContainsKey(i) ? slots[i] : primary;
                    var entries = record.Entries.Where(entry => entry.Name == group.Key && entry.Slot == rawSlots[i]).ToArray();
                    if (entries.Length == 0)
                        record.Entries.Add(new BlobEntry { Name = group.Key, Key = key, X = "None", Y = "None", Slot = (byte)rawSlots[i] });
                    else foreach (BlobEntry entry in entries) entry.Key = key;
                }
            }

            foreach (var keys in gamepadKeys)
            {
                foreach (var group in preset.Gamepad.GroupBy(b => b.Action))
                {
                    var want = group.ToDictionary(b => b.Slot, b => b.Key);
                    string primary = want.ContainsKey(0) ? want[0] : want.Values.First();
                    int[] rawSlots = PadRawSlots(record, keys, group.Key, defaults);

                    // Replacement presets move every supported slot. Unknown slots and
                    // the existing records' opaque metadata belong to the save file.
                    foreach (var el in keys.Items)
                    {
                        if (SaveFile.Field(el, "MappingName") != group.Key) continue;
                        string slot = SaveFile.Field(el, "Slot") ?? "";
                        int idx = Array.IndexOf(rawSlots, InputDefaults.RawSlot(slot));
                        if (idx >= 0 && SaveFile.Field(el, "DefaultKey") == defaults.ForRawSlot(group.Key, true, rawSlots[idx]))
                            ControllerProfiles.SetKey(sav, el, want.ContainsKey(idx) ? want[idx] : primary);
                    }

                    // Slots the preset names but the action does not own yet are created,
                    // cloned from an existing entry of the same action.
                    var template = ControllerProfiles.KeyTemplate(keys, group.Key);
                    for (int index = 0; index < rawSlots.Length; index++)
                    {
                        string slotName = InputDefaults.SlotName(rawSlots[index]);
                        bool exists = keys.Items.Any(x => SaveFile.Field(x, "MappingName") == group.Key &&
                                                          SaveFile.Field(x, "Slot") == slotName && SaveFile.Field(x, "DefaultKey") == defaults.ForRawSlot(group.Key, true, rawSlots[index]));
                        if (exists) continue;
                        var el = template.Select(SaveFile.CloneNode).ToList();
                        SaveFile.SetField(el, "MappingName", group.Key);
                        SaveFile.SetField(el, "Slot", slotName);
                        SaveFile.SetField(el, "Key", want.ContainsKey(index) ? want[index] : primary);
                        SaveFile.SetField(el, "DefaultKey", defaults.ForRawSlot(group.Key, true, rawSlots[index]) ?? "None");
                        keys.Items.Add(el);
                        ControllerProfiles.InitializeStorage(sav);
                    }
                }
            }
        }

        /// <summary>Plan every device before changing anything, including other controller families.</summary>
        static void ApplyAdditiveGuard(SaveFile save, List<Node> gamepadKeys, BlobRecord record)
        {
            var defaults = save.Defaults;
            var commits = new List<System.Action>();
            int[] keyboardRawSlots = KeyboardRawSlots(record, gamepadKeys, "IA_Guard", defaults);
            var keyboard = new List<BlobEntry>[keyboardRawSlots.Length];
            var values = new List<string>[keyboardRawSlots.Length];
            for (int slot = 0; slot < keyboardRawSlots.Length; slot++)
            {
                keyboard[slot] = record.Entries.Where(e => e.Name == "IA_Guard" && e.Slot == keyboardRawSlots[slot]).ToList();
                values[slot] = keyboard[slot].Select(e => e.Key).ToList();
            }
            foreach (var change in PlanGuardSlots(values, new[] { "RightMouseButton", "LeftMouseButton" }, I18n.Str("tab.kb")))
            {
                int slot = change.Key; string key = change.Value;
                commits.Add(delegate
                {
                    if (keyboard[slot].Count == 0)
                        record.Entries.Add(new BlobEntry { Name = "IA_Guard", Slot = (byte)keyboardRawSlots[slot], Key = key, X = "None", Y = "None" });
                    else foreach (BlobEntry entry in keyboard[slot]) entry.Key = key;
                });
            }
            int familyIndex = 0;
            foreach (Node keys in gamepadKeys)
            {
                Node familyKeys = keys;
                int[] padRawSlots = PadRawSlots(record, keys, "IA_Guard", defaults);
                var items = new List<List<Node>>[padRawSlots.Length];
                values = new List<string>[padRawSlots.Length];
                for (int slot = 0; slot < padRawSlots.Length; slot++)
                {
                    string name = InputDefaults.SlotName(padRawSlots[slot]);
                    items[slot] = keys.Items.Where(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && SaveFile.Field(item, "Slot") == name && SaveFile.Field(item, "DefaultKey") == defaults.ForRawSlot("IA_Guard", true, padRawSlots[slot])).ToList();
                    values[slot] = items[slot].Select(item => SaveFile.Field(item, "Key")).ToList();
                    if (values[slot].Count == 0) values[slot].Add(defaults.ForRawSlot("IA_Guard", true, padRawSlots[slot]));
                }
                List<Node> template = ControllerProfiles.KeyTemplate(keys, "IA_Guard");
                var changes = PlanGuardSlots(values, new[] { "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left" }, I18n.Str("tab.pad") + " #" + (++familyIndex));
                foreach (var change in changes)
                {
                    int slot = change.Key; string key = change.Value;
                    // Capture this family's item lists, not the next loop iteration's lists.
                    List<List<Node>> ownedItems = items[slot];
                    List<Node> ownedTemplate = template;
                    commits.Add(delegate
                    {
                        if (ownedItems.Count == 0)
                        {
                            var item = ownedTemplate.Select(SaveFile.CloneNode).ToList();
                            SaveFile.SetField(item, "MappingName", "IA_Guard");
                            SaveFile.SetField(item, "Slot", InputDefaults.SlotName(padRawSlots[slot]));
                            SaveFile.SetField(item, "Key", key);
                            SaveFile.SetField(item, "DefaultKey", defaults.ForRawSlot("IA_Guard", true, padRawSlots[slot]) ?? "None");
                            familyKeys.Items.Add(item);
                            ControllerProfiles.InitializeStorage(save);
                        }
                        else foreach (var item in ownedItems) ControllerProfiles.SetKey(save, item, key);
                    });
                }
            }
            foreach (var commit in commits) commit();
        }

        internal static bool IsGamepadOverride(List<Node> gamepadKeys, BlobEntry entry, InputDefaultSet defaults)
        {
            if (defaults.RegisteredSlots(entry.Name, false).Contains(entry.Slot)) return false;
            if (defaults.RegisteredSlots(entry.Name, true).Contains(entry.Slot)) return true;
            if (entry.Slot > 6) return false;
            string slot = InputDefaults.SlotName(entry.Slot);
            return gamepadKeys.SelectMany(keys => keys.Items).Any(item => SaveFile.Field(item, "MappingName") == entry.Name && SaveFile.Field(item, "Slot") == slot);
        }

        static int[] KeyboardRawSlots(BlobRecord keyboard, List<Node> gamepadKeys, string action, InputDefaultSet defaults)
        {
            return defaults.Slots(action, false, keyboard.Entries.Where(entry => entry.Name == action && !IsGamepadOverride(gamepadKeys, entry, defaults)).Select(entry => (int)entry.Slot),
                gamepadKeys.SelectMany(keys => keys.Items).Where(item => SaveFile.Field(item, "MappingName") == action)
                    .Select(item => InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))));
        }

        static int[] PadRawSlots(BlobRecord keyboard, Node keys, string action, InputDefaultSet defaults)
        { return defaults.EditableRegisteredSlots(action, true); }

        static bool IsEmptyKey(string key)
        { return string.IsNullOrEmpty(key) || string.Equals(key, "None", StringComparison.OrdinalIgnoreCase); }

        static Dictionary<int, string> PlanGuardSlots(List<string>[] source, string[] required, string scope)
        { return PlanSlots(source, required, true, scope, true); }

        static Dictionary<int, string> PlanSlots(List<string>[] source, string[] required, bool append, string scope, bool guard = false)
        {
            var slots = source.Select(values => values.Where(key => !IsEmptyKey(key)).Distinct().ToList()).ToArray();
            string[] original = slots.SelectMany(values => values).Distinct().ToArray();
            Dictionary<int, string> best = null;
            int bestCost = int.MaxValue;
            var options = required.Concat(new[] { "None" }).Distinct().ToArray();
            int choices = options.Length + 1;
            int assignments = 1;
            for (int slot = 0; slot < source.Length; slot++) assignments *= choices;
            for (int assignment = 0; assignment < assignments; assignment++)
            {
                int encoded = assignment, cost = 0;
                var result = slots.Select(values => values.ToList()).ToArray();
                var plan = new Dictionary<int, string>();
                for (int slot = 0; slot < source.Length; slot++)
                {
                    int choice = encoded % choices; encoded /= choices;
                    if (choice == 0) continue;
                    string key = options[choice - 1];
                    if (slots[slot].Count == 1 && slots[slot][0] == key) continue;
                    result[slot] = IsEmptyKey(key) ? new List<string>() : new List<string> { key };
                    plan[slot] = key;
                    cost += 1000 + (slots[slot].Count == 0 ? slot : 100 + 2 - slot);
                }
                string[] retained = result.SelectMany(values => values).Distinct().ToArray();
                if (cost < bestCost && (append ? original.All(retained.Contains) : retained.All(required.Contains)) && required.All(retained.Contains))
                { best = plan; bestCost = cost; }
            }
            if (best == null) throw new InvalidOperationException(I18n.Str(guard ? "preset.capacity" : "preset.bindingsfull", scope));
            return best;
        }
    }
}


