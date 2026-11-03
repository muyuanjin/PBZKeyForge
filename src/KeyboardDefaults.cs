using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    /// <summary>Player-mappable registrations decoded from the CL-301524 input contexts.</summary>
    public static class InputDefaults
    {
        sealed class FactoryBinding
        {
            public string Action, Key;
            public bool Gamepad;
            public int Slot;
            public FactoryBinding(string action, bool gamepad, int slot, string key)
            { Action = action; Gamepad = gamepad; Slot = slot; Key = key; }
        }

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

        // The captured profiles resolve the shared Shadow Arm slot 2 to the controller registration.
        static readonly FactoryBinding[] EffectiveBindings = Bindings.Where(binding =>
            !(binding.Action == "IA_ShadowArm" && !binding.Gamepad && binding.Slot == 2)).ToArray();

        public static bool HasAction(string action)
        { return EffectiveBindings.Any(binding => binding.Action == action); }

        public static string[] ForAction(string action, bool gamepad)
        {
            if (!HasAction(action)) return null;
            var keys = EffectiveBindings.Where(binding => binding.Action == action && binding.Gamepad == gamepad).OrderBy(binding => binding.Slot).Select(binding => binding.Key);
            return keys.Concat(FixedForAction(action, gamepad)).Distinct().ToArray();
        }

        /// <summary>Mappings without a player-mappable registration remain active independently of overrides.</summary>
        public static string[] FixedForAction(string action, bool gamepad)
        {
            return !gamepad && action == "IA_ShadowArm_LongPress" ? new[] { "LeftControl" } : new string[0];
        }

        public static string ForRawSlot(string action, bool gamepad, int rawSlot)
        {
            FactoryBinding binding = EffectiveBindings.FirstOrDefault(item => item.Action == action && item.Gamepad == gamepad && item.Slot == rawSlot);
            return binding != null ? binding.Key : HasAction(action) ? "None" : null;
        }

        public static int[] RegisteredSlots(string action, bool gamepad)
        { return EffectiveBindings.Where(binding => binding.Action == action && binding.Gamepad == gamepad).Select(binding => binding.Slot).Distinct().OrderBy(slot => slot).ToArray(); }

        static List<int> OwnedSlots(string action, bool gamepad, IEnumerable<int> existingSlots)
        {
            return RegisteredSlots(action, gamepad).Concat(existingSlots.Where(slot => slot >= 0 && slot < SlotNames.Length - 1)).Distinct().ToList();
        }

        static int[] FillSlots(string action, bool gamepad, List<int> owned, IEnumerable<int> other)
        {
            int[] registered = RegisteredSlots(action, gamepad);
            var occupied = new HashSet<int>(other);
            for (int raw = 0; owned.Count < 3 && raw < SlotNames.Length - 1; raw++)
                if (!owned.Contains(raw) && !occupied.Contains(raw)) owned.Add(raw);
            return registered.Concat(owned.Except(registered).OrderBy(slot => slot)).Take(3).ToArray();
        }

        public static int[] Slots(string action, bool gamepad, IEnumerable<int> existingSlots, IEnumerable<int> otherDeviceSlots)
        {
            var own = existingSlots.ToArray();
            var other = otherDeviceSlots.ToArray();
            var keyboard = OwnedSlots(action, false, gamepad ? other : own);
            var controller = OwnedSlots(action, true, gamepad ? own : other);
            int[] keyboardSlots = FillSlots(action, false, keyboard, controller);
            return gamepad ? FillSlots(action, true, controller, keyboardSlots.Concat(keyboard)) : keyboardSlots;
        }

        public static int[] Slots(string action, bool gamepad)
        { return Slots(action, gamepad, new int[0], new int[0]); }

        public static string SlotName(int rawSlot)
        { return "EPlayerMappableKeySlot::" + SlotNames[rawSlot]; }

        public static int RawSlot(string slotName)
        { return Array.IndexOf(SlotNames, (slotName ?? "").Replace("EPlayerMappableKeySlot::", "")); }
    }

    public static class KeyboardDefaults
    {
        public static string[] ForAction(string action)
        { return InputDefaults.ForAction(action, false); }

        public static string ForSlot(string action, int slotIndex)
        { return InputDefaults.ForRawSlot(action, false, InputDefaults.Slots(action, false)[slotIndex]); }
    }
}
