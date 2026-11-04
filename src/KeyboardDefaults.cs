using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    /// <summary>Factory registrations and independent editing slots for one native input implementation.</summary>
    public sealed class InputDefaultSet
    {
        sealed class FactoryBinding
        {
            public string Action, Key;
            public bool Gamepad;
            public int Slot;
            public FactoryBinding(string action, bool gamepad, int slot, string key)
            { Action = action; Gamepad = gamepad; Slot = slot; Key = key; }
        }

        public static readonly InputDefaultSet Legacy = new InputDefaultSet(false);
        public static readonly InputDefaultSet Current = new InputDefaultSet(true);
        readonly bool shadowArmMirror;

        InputDefaultSet(bool shadowArmMirror) { this.shadowArmMirror = shadowArmMirror; }

        public bool IsMirroredSlot(string action, int rawSlot)
        { return shadowArmMirror && action == "IA_ShadowArm" && rawSlot == 2; }

        static readonly string[] SlotNames = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Unspecified" };
        static readonly FactoryBinding[] Bindings =
        {
            new FactoryBinding("IA_SwitchWeapon", false, 0, "One"),
            new FactoryBinding("IA_SwitchWeapon", true, 1, "Gamepad_DPad_Left"),
            new FactoryBinding("IA_SwitchWeapon", false, 2, "One"),
            new FactoryBinding("IA_SwitchWeapon", true, 3, "Gamepad_DPad_Left"),
            new FactoryBinding("IA_SwitchShadowArm", false, 0, "Two"),
            new FactoryBinding("IA_SwitchShadowArm", true, 1, "Gamepad_DPad_Right"),
            new FactoryBinding("IA_Jump", false, 0, "SpaceBar"),
            new FactoryBinding("IA_Jump", true, 1, "Gamepad_FaceButton_Bottom"),
            new FactoryBinding("IA_LightAttack", false, 0, "LeftMouseButton"),
            new FactoryBinding("IA_LightAttack", true, 1, "Gamepad_FaceButton_Left"),
            new FactoryBinding("IA_HeavyAttack", false, 0, "F"),
            new FactoryBinding("IA_HeavyAttack", true, 1, "Gamepad_FaceButton_Top"),
            new FactoryBinding("IA_HeavyAttack", false, 2, "F"),
            new FactoryBinding("IA_LockTarget", false, 0, "MiddleMouseButton"),
            new FactoryBinding("IA_LockTarget", true, 1, "Gamepad_RightThumbstick"),
            new FactoryBinding("IA_Crouch", true, 0, "Gamepad_LeftThumbstick"),
            new FactoryBinding("IA_UltimateSkill", false, 0, "Q"),
            new FactoryBinding("IA_UltimateSkill", true, 1, "Gamepad_LeftTriggerAxis"),
            new FactoryBinding("IA_ShadowArm", false, 0, "LeftControl"),
            new FactoryBinding("IA_ShadowArm", true, 1, "Gamepad_RightTriggerAxis"),
            new FactoryBinding("IA_ShadowArm", false, 2, "LeftControl"),
            new FactoryBinding("IA_ShadowArm", true, 3, "Gamepad_RightTriggerAxis"),
            new FactoryBinding("IA_HeavyAttack", true, 3, "Gamepad_FaceButton_Top"),
            new FactoryBinding("IA_LightAttack", false, 2, "LeftMouseButton"),
            new FactoryBinding("IA_LightAttack", true, 3, "Gamepad_FaceButton_Left"),
            new FactoryBinding("IA_HeavyAttack", false, 4, "F"),
            new FactoryBinding("IA_HeavyAttack", true, 5, "Gamepad_FaceButton_Top"),
            new FactoryBinding("IA_LightAttack", false, 4, "LeftMouseButton"),
            new FactoryBinding("IA_LightAttack", true, 5, "Gamepad_FaceButton_Left"),
            new FactoryBinding("IA_UltimateSkill", false, 2, "Q"),
            new FactoryBinding("IA_UltimateSkill", true, 3, "Gamepad_LeftTriggerAxis"),
            new FactoryBinding("IA_Crouch", false, 1, "C"),
            new FactoryBinding("IA_ShadowArm_LongPress", true, 0, "Gamepad_RightTrigger"),
            new FactoryBinding("IA_Guard", false, 0, "RightMouseButton"),
            new FactoryBinding("IA_Guard", true, 1, "Gamepad_LeftShoulder"),
            new FactoryBinding("IA_Guard", false, 2, "RightMouseButton"),
            new FactoryBinding("IA_Guard", true, 3, "Gamepad_LeftShoulder"),
            new FactoryBinding("IA_Dodge", false, 0, "LeftShift"),
            new FactoryBinding("IA_Dodge", true, 1, "Gamepad_RightShoulder"),
            new FactoryBinding("IA_Dodge", false, 2, "LeftShift"),
            new FactoryBinding("IA_Dodge", true, 3, "Gamepad_RightShoulder"),
            new FactoryBinding("IA_Execute", false, 0, "E"),
            new FactoryBinding("IA_Execute", true, 1, "Gamepad_FaceButton_Right"),
            new FactoryBinding("IA_MoveSlow", false, 0, "LeftAlt"),
            new FactoryBinding("IA_MoveFast", false, 0, "LeftShift"),
            new FactoryBinding("IA_MoveFast", true, 1, "Gamepad_RightShoulder"),
            new FactoryBinding("IA_OpenCharacterMenu", true, 0, "Gamepad_Special_Right"),
            new FactoryBinding("IA_OpenMapMenu", false, 0, "M"),
            new FactoryBinding("IA_OpenMapMenu", true, 1, "Gamepad_Special_Left"),
            new FactoryBinding("IA_OpenMapMenu", false, 2, "Tab"),
            new FactoryBinding("IA_OpenCharacterMenu", false, 1, "I"),
            new FactoryBinding("IA_Sense_Default", false, 0, "V"),
            new FactoryBinding("IA_Sense_Default", true, 1, "Gamepad_Special_Left"),
            new FactoryBinding("IA_UseBottomItem", true, 0, "Gamepad_DPad_Down"),
            new FactoryBinding("IA_UseTopItem", true, 0, "Gamepad_DPad_Up"),
            new FactoryBinding("IA_UseBottomItem", false, 1, "R"),
            new FactoryBinding("IA_UseTopItem", false, 1, "T"),
            new FactoryBinding("IA_Interact", false, 0, "E"),
            new FactoryBinding("IA_Interact", true, 1, "Gamepad_FaceButton_Right"),
            new FactoryBinding("IA_ShadowArm", true, 2, "Gamepad_RightTriggerAxis"),
        };

        IEnumerable<FactoryBinding> EffectiveBindings
        {
            get { return Bindings.Where(binding => binding.Action != "IA_ShadowArm" || binding.Slot != 2
                || binding.Gamepad != shadowArmMirror); }
        }

        public bool HasAction(string action)
        { return EffectiveBindings.Any(binding => binding.Action == action); }

        public string[] ForAction(string action, bool gamepad)
        {
            if (!HasAction(action)) return null;
            var keys = EffectiveBindings.Where(binding => binding.Action == action && binding.Gamepad == gamepad).OrderBy(binding => binding.Slot).Select(binding => binding.Key);
            return keys.Concat(FixedForAction(action, gamepad)).Distinct().ToArray();
        }

        /// <summary>Mappings without a player-mappable registration remain active independently of overrides.</summary>
        public string[] FixedForAction(string action, bool gamepad)
        {
            return !gamepad && action == "IA_ShadowArm_LongPress" ? new[] { "LeftControl" } : new string[0];
        }

        public string ForRawSlot(string action, bool gamepad, int rawSlot)
        {
            FactoryBinding binding = EffectiveBindings.FirstOrDefault(item => item.Action == action && item.Gamepad == gamepad && item.Slot == rawSlot);
            return binding != null ? binding.Key : HasAction(action) ? "None" : null;
        }

        public int[] RegisteredSlots(string action, bool gamepad)
        { return EffectiveBindings.Where(binding => binding.Action == action && binding.Gamepad == gamepad).Select(binding => binding.Slot).Distinct().OrderBy(slot => slot).ToArray(); }

        public int[] EditableRegisteredSlots(string action, bool gamepad)
        { return RegisteredSlots(action, gamepad).Where(slot => !IsMirroredSlot(action, slot)).ToArray(); }

        public string[] ResolveKeys(string action, bool gamepad, IEnumerable<KeyValuePair<int, string>> overrides)
        {
            var saved = overrides.Where(binding => !IsMirroredSlot(action, binding.Key)).ToArray();
            var inherited = EditableRegisteredSlots(action, gamepad).Where(slot => !saved.Any(binding => binding.Key == slot))
                .Select(slot => ForRawSlot(action, gamepad, slot));
            return saved.Select(binding => binding.Value).Concat(inherited).Concat(FixedForAction(action, gamepad))
                .Where(key => !string.IsNullOrEmpty(key) && key != "None").Distinct().ToArray();
        }

        List<int> OwnedSlots(string action, bool gamepad, IEnumerable<int> existingSlots)
        {
            return EditableRegisteredSlots(action, gamepad).Concat(existingSlots.Where(slot => slot >= 0 && slot < SlotNames.Length - 1 && !IsMirroredSlot(action, slot))).Distinct().ToList();
        }

        int[] FillSlots(string action, bool gamepad, List<int> owned, IEnumerable<int> other)
        {
            int[] registered = EditableRegisteredSlots(action, gamepad);
            var occupied = new HashSet<int>(other);
            for (int raw = 0; owned.Count < 3 && raw < SlotNames.Length - 1; raw++)
                if (!owned.Contains(raw) && !occupied.Contains(raw) && !IsMirroredSlot(action, raw)) owned.Add(raw);
            return registered.Concat(owned.Except(registered).OrderBy(slot => slot)).Take(3).ToArray();
        }

        public int[] Slots(string action, bool gamepad, IEnumerable<int> existingSlots, IEnumerable<int> otherDeviceSlots)
        {
            var own = existingSlots.ToArray();
            var other = otherDeviceSlots.ToArray();
            var keyboard = OwnedSlots(action, false, gamepad ? other : own);
            var controller = OwnedSlots(action, true, gamepad ? own : other);
            int[] keyboardSlots = FillSlots(action, false, keyboard, controller);
            return gamepad ? FillSlots(action, true, controller, keyboardSlots.Concat(keyboard)) : keyboardSlots;
        }

        public int[] Slots(string action, bool gamepad)
        { return Slots(action, gamepad, new int[0], new int[0]); }

        public static string SlotName(int rawSlot)
        { return "EPlayerMappableKeySlot::" + SlotNames[rawSlot]; }

        public static int RawSlot(string slotName)
        { return Array.IndexOf(SlotNames, (slotName ?? "").Replace("EPlayerMappableKeySlot::", "")); }
    }

    /// <summary>The frozen launch-build defaults used by byte-only and offline callers.</summary>
    public static class InputDefaults
    {
        public static bool HasAction(string action) { return InputDefaultSet.Legacy.HasAction(action); }
        public static string[] ForAction(string action, bool gamepad) { return InputDefaultSet.Legacy.ForAction(action, gamepad); }
        public static string[] FixedForAction(string action, bool gamepad) { return InputDefaultSet.Legacy.FixedForAction(action, gamepad); }
        public static string ForRawSlot(string action, bool gamepad, int rawSlot) { return InputDefaultSet.Legacy.ForRawSlot(action, gamepad, rawSlot); }
        public static int[] RegisteredSlots(string action, bool gamepad) { return InputDefaultSet.Legacy.RegisteredSlots(action, gamepad); }
        public static string[] ResolveKeys(string action, bool gamepad, IEnumerable<KeyValuePair<int, string>> overrides)
        { return InputDefaultSet.Legacy.ResolveKeys(action, gamepad, overrides); }
        public static int[] Slots(string action, bool gamepad, IEnumerable<int> existingSlots, IEnumerable<int> otherDeviceSlots)
        { return InputDefaultSet.Legacy.Slots(action, gamepad, existingSlots, otherDeviceSlots); }
        public static int[] Slots(string action, bool gamepad) { return InputDefaultSet.Legacy.Slots(action, gamepad); }
        public static string SlotName(int rawSlot) { return InputDefaultSet.SlotName(rawSlot); }
        public static int RawSlot(string slotName) { return InputDefaultSet.RawSlot(slotName); }
    }

    public static class KeyboardDefaults
    {
        public static string[] ForAction(string action)
        { return InputDefaults.ForAction(action, false); }

        public static string ForSlot(string action, int slotIndex)
        { return InputDefaults.ForRawSlot(action, false, InputDefaults.Slots(action, false)[slotIndex]); }
    }
}
