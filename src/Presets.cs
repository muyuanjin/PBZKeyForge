using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    public class KeyBinding
    {
        public string Action;
        public int Slot;      // keyboard: 0,2,4 (index into kb slots) / gamepad: 0,1,2 (Second,Fourth,Sixth)
        public string Key;
        public KeyBinding(string a, int s, string k) { Action = a; Slot = s; Key = k; }
    }

    public class Preset
    {
        public string Id;
        public bool Vanilla;
        public List<KeyBinding> Keyboard = new List<KeyBinding>();
        public List<KeyBinding> Gamepad = new List<KeyBinding>();
        public string Name { get { return I18n.Str("preset." + Id + ".name"); } }
        public string Desc { get { return I18n.Str("preset." + Id + ".desc"); } }
        public override string ToString() { return Name; }
    }

    /// <summary>Built-in presets and the edit operations they map to.</summary>
    public static class Presets
    {
        public static readonly int[] KeyboardSlots = { 0, 2, 4 };
        public static readonly string[] GamepadSlots = { "Second", "Fourth", "Sixth" };

        public static List<Preset> All()
        {
            var list = new List<Preset>();

            // 一键攻防
            var p1 = new Preset { Id = "onekey" };
            for (int i = 0; i < 3; i++) p1.Keyboard.Add(new KeyBinding("IA_Guard", i, "LeftMouseButton"));
            p1.Gamepad.Add(new KeyBinding("IA_Guard", 0, "Gamepad_FaceButton_Left"));
            p1.Gamepad.Add(new KeyBinding("IA_Guard", 1, "Gamepad_FaceButton_Left"));
            list.Add(p1);

            // 一键攻防 + 防御键改闪避/冲刺
            var p2 = new Preset { Id = "onekey_dodge" };
            foreach (var b in p1.Keyboard) p2.Keyboard.Add(b);
            foreach (var b in p1.Gamepad) p2.Gamepad.Add(b);
            p2.Keyboard.Add(new KeyBinding("IA_Dodge", 0, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_Dodge", 1, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_Dodge", 2, "RightMouseButton"));
            p2.Keyboard.Add(new KeyBinding("IA_MoveFast", 0, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_MoveFast", 1, "LeftShift"));
            p2.Keyboard.Add(new KeyBinding("IA_MoveFast", 2, "RightMouseButton"));
            p2.Gamepad.Add(new KeyBinding("IA_Dodge", 0, "Gamepad_RightShoulder"));
            p2.Gamepad.Add(new KeyBinding("IA_Dodge", 1, "Gamepad_LeftShoulder"));
            p2.Gamepad.Add(new KeyBinding("IA_MoveFast", 0, "Gamepad_RightShoulder"));
            p2.Gamepad.Add(new KeyBinding("IA_MoveFast", 1, "Gamepad_LeftShoulder"));
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

            // 撤销一键攻防
            var p6 = new Preset { Id = "undo_onekey" };
            for (int i = 0; i < 3; i++) p6.Keyboard.Add(new KeyBinding("IA_Guard", i, "RightMouseButton"));
            p6.Gamepad.Add(new KeyBinding("IA_Guard", 0, "Gamepad_LeftShoulder"));
            p6.Gamepad.Add(new KeyBinding("IA_Guard", 1, "Gamepad_LeftShoulder"));
            list.Add(p6);

            // 全部恢复默认
            list.Add(new Preset { Id = "vanilla", Vanilla = true });
            return list;
        }

        public static Preset ById(string id)
        {
            return All().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Apply a preset to one keyboard record and every gamepad profile.</summary>
        public static void Apply(SaveFile sav, Preset preset, BlobRecord record)
        {
            if (preset.Vanilla)
            {
                foreach (var r in sav.Records) r.Entries = new List<BlobEntry>();
                sav.ResetGamepadToDefaults();
                return;
            }

            var touchedKb = new HashSet<string>(preset.Keyboard.Select(b => b.Action));
            var keep = record.Entries.Where(e => !touchedKb.Contains(e.Name)).ToList();
            foreach (var group in preset.Keyboard.GroupBy(b => b.Action))
            {
                var slots = group.ToDictionary(b => b.Slot, b => b.Key);
                string primary = slots.ContainsKey(0) ? slots[0] : slots.Values.First();
                for (int i = 0; i < 3; i++)
                {
                    string key = slots.ContainsKey(i) ? slots[i] : primary;
                    keep.Add(new BlobEntry
                    {
                        Name = group.Key,
                        Key = key,
                        X = "None",
                        Y = "None",
                        Slot = (byte)KeyboardSlots[i]
                    });
                }
            }
            record.Entries = keep;

            foreach (var keys in sav.GamepadKeys)
            {
                foreach (var group in preset.Gamepad.GroupBy(b => b.Action))
                {
                    var want = group.ToDictionary(b => b.Slot, b => b.Key);
                    string primary = want.ContainsKey(0) ? want[0] : want.Values.First();

                    // Every slot the action already owns is rewritten, so a remap really
                    // moves the action instead of leaving the old buttons alive.
                    foreach (var el in keys.Items)
                    {
                        if (SaveFile.Field(el, "MappingName") != group.Key) continue;
                        string slot = SaveFile.Field(el, "Slot") ?? "";
                        int idx = Array.IndexOf(GamepadSlots, slot.Replace("EPlayerMappableKeySlot::", ""));
                        SaveFile.SetField(el, "Key", idx >= 0 && want.ContainsKey(idx) ? want[idx] : primary);
                    }

                    // Slots the preset names but the action does not own yet are created,
                    // cloned from an existing entry of the same action.
                    var template = keys.Items.FirstOrDefault(x => SaveFile.Field(x, "MappingName") == group.Key);
                    if (template == null) continue;
                    foreach (var kv in want)
                    {
                        string slotName = "EPlayerMappableKeySlot::" + GamepadSlots[kv.Key];
                        bool exists = keys.Items.Any(x => SaveFile.Field(x, "MappingName") == group.Key &&
                                                          SaveFile.Field(x, "Slot") == slotName);
                        if (exists) continue;
                        var el = template.Select(SaveFile.CloneNode).ToList();
                        SaveFile.SetField(el, "Slot", slotName);
                        SaveFile.SetField(el, "Key", kv.Value);
                        keys.Items.Add(el);
                    }
                }
            }
        }
    }
}


