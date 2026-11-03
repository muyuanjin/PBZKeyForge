using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    public class KeyBinding
    {
        public string Action;
        public int Slot;      // Logical editing column; raw slots depend on the action and device.
        public string Key;
        public KeyBinding(string a, int s, string k) { Action = a; Slot = s; Key = k; }
    }

    public class Preset
    {
        public string Id;
        public bool Vanilla;
        public bool CurrentProfileVanilla;
        public bool AdditiveGuard;
        public List<KeyBinding> Keyboard = new List<KeyBinding>();
        public List<KeyBinding> Gamepad = new List<KeyBinding>();
        public string Name { get { return I18n.Str("preset." + Id + ".name"); } }
        public string Desc { get { return I18n.Str("preset." + Id + ".desc"); } }
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

        /// <summary>Apply to one profile's keyboard and controller records; only vanilla resets all profiles.</summary>
        public static void Apply(SaveFile sav, Preset preset, BlobRecord record)
        {
            // ProfileId owns the relationship. Family (for example Xbox) can recur
            // independently in Default, Custom1 and Custom2, and is not shared scope.
            List<Node> gamepadKeys = sav.GamepadKeysFor(record.ProfileId);
            if (preset.AdditiveGuard)
            {
                ApplyAdditiveGuard(gamepadKeys, record);
                return;
            }
            if (preset.Vanilla)
            {
                foreach (var r in sav.Records) r.Entries = new List<BlobEntry>();
                sav.ResetGamepadToDefaults();
                return;
            }
            if (preset.CurrentProfileVanilla)
            {
                record.Entries = new List<BlobEntry>();
                foreach (var keys in gamepadKeys)
                    foreach (var item in keys.Items)
                    {
                        string defaultKey = SaveFile.Field(item, "DefaultKey");
                        if (defaultKey != null) SaveFile.SetField(item, "Key", defaultKey);
                    }
                return;
            }

            foreach (var group in preset.Keyboard.GroupBy(binding => binding.Action))
                if (group.Any(binding => binding.Slot >= KeyboardRawSlots(record, gamepadKeys, group.Key).Length))
                    throw new InvalidOperationException(I18n.Str("preset.capacity", I18n.Str("tab.kb")));
            foreach (Node keys in gamepadKeys)
                foreach (var group in preset.Gamepad.GroupBy(binding => binding.Action))
                    if (group.Any(binding => binding.Slot >= PadRawSlots(record, keys, group.Key).Length))
                        throw new InvalidOperationException(I18n.Str("preset.capacity", I18n.Str("tab.pad")));

            foreach (var group in preset.Keyboard.GroupBy(b => b.Action))
            {
                var slots = group.ToDictionary(b => b.Slot, b => b.Key);
                string primary = slots.ContainsKey(0) ? slots[0] : slots.Values.First();
                int[] rawSlots = KeyboardRawSlots(record, gamepadKeys, group.Key);
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
                    int[] rawSlots = PadRawSlots(record, keys, group.Key);

                    // Replacement presets move every supported slot. Unknown slots and
                    // the existing records' opaque metadata belong to the save file.
                    foreach (var el in keys.Items)
                    {
                        if (SaveFile.Field(el, "MappingName") != group.Key) continue;
                        string slot = SaveFile.Field(el, "Slot") ?? "";
                        int idx = Array.IndexOf(rawSlots, InputDefaults.RawSlot(slot));
                        if (idx >= 0 && SaveFile.Field(el, "DefaultKey") == InputDefaults.ForRawSlot(group.Key, true, rawSlots[idx]))
                            SaveFile.SetField(el, "Key", want.ContainsKey(idx) ? want[idx] : primary);
                    }

                    // Slots the preset names but the action does not own yet are created,
                    // cloned from an existing entry of the same action.
                    var template = keys.Items.FirstOrDefault(x => SaveFile.Field(x, "MappingName") == group.Key) ?? keys.Items.FirstOrDefault();
                    if (template == null) continue;
                    for (int index = 0; index < rawSlots.Length; index++)
                    {
                        string slotName = InputDefaults.SlotName(rawSlots[index]);
                        bool exists = keys.Items.Any(x => SaveFile.Field(x, "MappingName") == group.Key &&
                                                          SaveFile.Field(x, "Slot") == slotName && SaveFile.Field(x, "DefaultKey") == InputDefaults.ForRawSlot(group.Key, true, rawSlots[index]));
                        if (exists) continue;
                        var el = template.Select(SaveFile.CloneNode).ToList();
                        SaveFile.SetField(el, "MappingName", group.Key);
                        SaveFile.SetField(el, "Slot", slotName);
                        SaveFile.SetField(el, "Key", want.ContainsKey(index) ? want[index] : primary);
                        SaveFile.SetField(el, "DefaultKey", InputDefaults.ForRawSlot(group.Key, true, rawSlots[index]) ?? "None");
                        keys.Items.Add(el);
                    }
                }
            }
        }

        /// <summary>Plan every device before changing anything, including other controller families.</summary>
        static void ApplyAdditiveGuard(List<Node> gamepadKeys, BlobRecord record)
        {
            var commits = new List<System.Action>();
            int[] keyboardRawSlots = KeyboardRawSlots(record, gamepadKeys, "IA_Guard");
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
                int[] padRawSlots = PadRawSlots(record, keys, "IA_Guard");
                var items = new List<List<Node>>[padRawSlots.Length];
                values = new List<string>[padRawSlots.Length];
                for (int slot = 0; slot < padRawSlots.Length; slot++)
                {
                    string name = InputDefaults.SlotName(padRawSlots[slot]);
                    items[slot] = keys.Items.Where(item => SaveFile.Field(item, "MappingName") == "IA_Guard" && SaveFile.Field(item, "Slot") == name && SaveFile.Field(item, "DefaultKey") == InputDefaults.ForRawSlot("IA_Guard", true, padRawSlots[slot])).ToList();
                    values[slot] = items[slot].Select(item => SaveFile.Field(item, "Key")).ToList();
                    if (values[slot].Count == 0) values[slot].Add(InputDefaults.ForRawSlot("IA_Guard", true, padRawSlots[slot]));
                }
                List<Node> template = keys.Items.FirstOrDefault(item => SaveFile.Field(item, "MappingName") == "IA_Guard") ?? keys.Items.FirstOrDefault();
                var changes = PlanGuardSlots(values, new[] { "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left" }, I18n.Str("tab.pad") + " #" + (++familyIndex));
                if (changes.Count > 0 && template == null)
                    throw new InvalidOperationException(I18n.Str("preset.capacity", I18n.Str("tab.pad") + " #" + familyIndex));
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
                            SaveFile.SetField(item, "DefaultKey", InputDefaults.ForRawSlot("IA_Guard", true, padRawSlots[slot]) ?? "None");
                            familyKeys.Items.Add(item);
                        }
                        else foreach (var item in ownedItems) SaveFile.SetField(item, "Key", key);
                    });
                }
            }
            foreach (var commit in commits) commit();
        }

        internal static bool IsGamepadOverride(List<Node> gamepadKeys, BlobEntry entry)
        {
            if (InputDefaults.RegisteredSlots(entry.Name, false).Contains(entry.Slot)) return false;
            if (InputDefaults.RegisteredSlots(entry.Name, true).Contains(entry.Slot)) return true;
            if (entry.Slot > 6) return false;
            string slot = InputDefaults.SlotName(entry.Slot);
            return gamepadKeys.SelectMany(keys => keys.Items).Any(item => SaveFile.Field(item, "MappingName") == entry.Name && SaveFile.Field(item, "Slot") == slot);
        }

        static int[] KeyboardRawSlots(BlobRecord keyboard, List<Node> gamepadKeys, string action)
        {
            return InputDefaults.Slots(action, false, keyboard.Entries.Where(entry => entry.Name == action && !IsGamepadOverride(gamepadKeys, entry)).Select(entry => (int)entry.Slot),
                gamepadKeys.SelectMany(keys => keys.Items).Where(item => SaveFile.Field(item, "MappingName") == action)
                    .Select(item => InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))));
        }

        static int[] PadRawSlots(BlobRecord keyboard, Node keys, string action)
        { return InputDefaults.RegisteredSlots(action, true); }

        static bool IsEmptyKey(string key)
        { return string.IsNullOrEmpty(key) || string.Equals(key, "None", StringComparison.OrdinalIgnoreCase); }

        static Dictionary<int, string> PlanGuardSlots(List<string>[] source, string[] required, string scope)
        {
            var slots = source.Select(values => values.Where(key => !IsEmptyKey(key)).Distinct().ToList()).ToArray();
            string[] original = slots.SelectMany(values => values).Distinct().ToArray();
            Dictionary<int, string> best = null;
            int bestCost = int.MaxValue;
            // Slots can legally contain several serialized entries. A greedy reuse
            // can discard the only slot that preserves two distinct custom keys.
            // Enumerate keep/right/left assignments before choosing a plan.
            int assignments = 1;
            for (int slot = 0; slot < source.Length; slot++) assignments *= 3;
            for (int assignment = 0; assignment < assignments; assignment++)
            {
                int encoded = assignment, cost = 0;
                var result = slots.Select(values => values.ToList()).ToArray();
                var plan = new Dictionary<int, string>();
                for (int slot = 0; slot < source.Length; slot++)
                {
                    int choice = encoded % 3; encoded /= 3;
                    if (choice == 0) continue;
                    string key = required[choice - 1];
                    if (slots[slot].Count == 1 && slots[slot][0] == key) continue;
                    result[slot] = new List<string> { key };
                    plan[slot] = key;
                    cost += 1000 + (slots[slot].Count == 0 ? slot : 100 + 2 - slot);
                }
                string[] retained = result.SelectMany(values => values).Distinct().ToArray();
                if (cost < bestCost && original.All(retained.Contains) && required.All(retained.Contains))
                { best = plan; bestCost = cost; }
            }
            if (best == null) throw new InvalidOperationException(I18n.Str("preset.capacity", scope));
            return best;
        }
    }
}


