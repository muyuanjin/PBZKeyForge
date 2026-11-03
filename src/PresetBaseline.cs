using System;
using System.IO;

namespace PBZKeyForge
{
    public static class PresetBaseline
    {
        public const uint CurrentId = 1; // CL-301524

        // Permanent IDs are one-based positions. Append only; never reorder or reuse.
        public static readonly string[] Actions = {
            "IA_LightAttack", "IA_HeavyAttack", "IA_Guard", "IA_Dodge", "IA_Jump", "IA_Crouch", "IA_MoveFast", "IA_MoveSlow",
            "IA_LockTarget", "IA_Interact", "IA_Execute", "IA_ShadowArm", "IA_ShadowArm_LongPress", "IA_SwitchWeapon",
            "IA_SwitchShadowArm", "IA_UltimateSkill", "IA_Sense_Default", "IA_OpenCharacterMenu", "IA_OpenMapMenu",
            "IA_UseTopItem", "IA_UseBottomItem"
        };

        // Frozen action-level defaults for baseline 1, independent of the live UI/default table.
        static readonly string[] Keyboard1 = {
            "LeftMouseButton", "F", "RightMouseButton", "LeftShift", "SpaceBar", "C", "LeftShift", "LeftAlt",
            "MiddleMouseButton", "E", "E", "LeftControl", "LeftControl", "One", "Two", "Q", "V", "I", "M Tab", "T", "R"
        };
        static readonly string[] Gamepad1 = {
            "Gamepad_FaceButton_Left", "Gamepad_FaceButton_Top", "Gamepad_LeftShoulder", "Gamepad_RightShoulder",
            "Gamepad_FaceButton_Bottom", "Gamepad_LeftThumbstick", "Gamepad_RightShoulder", "", "Gamepad_RightThumbstick",
            "Gamepad_FaceButton_Right", "Gamepad_FaceButton_Right", "Gamepad_RightTriggerAxis", "Gamepad_RightTrigger",
            "Gamepad_DPad_Left", "Gamepad_DPad_Right", "Gamepad_LeftTriggerAxis", "Gamepad_Special_Left",
            "Gamepad_Special_Right", "Gamepad_Special_Left", "Gamepad_DPad_Up", "Gamepad_DPad_Down"
        };

        public static void Require(uint id)
        { if (id != 1) throw new InvalidDataException(I18n.Str("preset.shareupgrade")); }

        public static string[] Keys(uint id, string action, bool gamepad)
        {
            Require(id);
            int index = Array.IndexOf(Actions, action);
            if (index < 0) throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
            return (gamepad ? Gamepad1[index] : Keyboard1[index]).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
