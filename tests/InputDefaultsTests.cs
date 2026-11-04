using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using PBZKeyForge;

public static class InputDefaultsTests
{
    public sealed class Source
    {
        public string context;
        public int mappingIndex;
        public string[] triggers;
    }

    public sealed class Binding
    {
        public string action, device, key;
        public int rawSlot;
        public Source[] sources;
    }

    public sealed class FixedMapping
    {
        public string inputAction, device, key;
        public string[] actions;
        public Source[] sources;
    }

    public sealed class Dataset
    {
        public string build;
        public string[] profiles;
        public bool profileDefaultsIdentical;
        public Binding[] bindings;
        public Binding[] effectiveBindings;
        public FixedMapping[] fixedMappings;
    }

    static int checks;
    static InputDefaultSet Defaults;

    static void Assert(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    static string Identity(string action, bool gamepad, int slot, string key)
    { return action + "|" + gamepad + "|" + slot + "|" + key; }

    static HashSet<string> Rows(IEnumerable<Binding> bindings)
    {
        return new HashSet<string>(bindings.Select(binding =>
            Identity(binding.action, binding.device == "Gamepad", binding.rawSlot, binding.key)));
    }

    static void ModelMatchesResources(Dataset data)
    {
        Assert((data.build == "CL-301524" || data.build == "CL-302256") && data.profileDefaultsIdentical, "Dataset build and profile scope");
        Assert(data.profiles.SequenceEqual(new[] { "InputUserSettings.Profiles.Default", "PBZ.KeyProfile.Custom1", "PBZ.KeyProfile.Custom2" }), "Three resource profiles");
        Assert(data.bindings.Length == 60 && Rows(data.bindings).Count == 60, "60 unique resource registrations");

        var actual = new HashSet<string>();
        var bindings = (IEnumerable)typeof(InputDefaultSet).GetField("Bindings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        int count = 0;
        foreach (object binding in bindings)
        {
            Type type = binding.GetType();
            string action = (string)type.GetField("Action").GetValue(binding);
            string key = (string)type.GetField("Key").GetValue(binding);
            bool gamepad = (bool)type.GetField("Gamepad").GetValue(binding);
            int slot = (int)type.GetField("Slot").GetValue(binding);
            count++;
            Assert(actual.Add(Identity(action, gamepad, slot, key)), "Duplicate model registration: " + action);
        }
        Assert(count == 60 && actual.SetEquals(Rows(data.bindings)), "Model registrations must exactly match the resource dataset");

        Assert(data.effectiveBindings.Length == 59 && Rows(data.effectiveBindings).Count == 59, "59 effective profile registrations");
        foreach (string action in data.effectiveBindings.Select(binding => binding.action).Distinct())
        {
            Assert(Defaults.HasAction(action), "Known resource action: " + action);
            foreach (bool gamepad in new[] { false, true })
            {
                Binding[] expected = data.effectiveBindings.Where(binding => binding.action == action && (binding.device == "Gamepad") == gamepad).OrderBy(binding => binding.rawSlot).ToArray();
                Assert(Defaults.RegisteredSlots(action, gamepad).SequenceEqual(expected.Select(binding => binding.rawSlot)), "Registered raw slots: " + action);
                string[] keys = expected.Select(binding => binding.key).Concat(Defaults.FixedForAction(action, gamepad)).Distinct().ToArray();
                string[] actualKeys = Defaults.ForAction(action, gamepad);
                Assert(actualKeys.Length == actualKeys.Distinct().Count() && actualKeys.SequenceEqual(keys), "Action keys must deduplicate actual resource slots: " + action);
                for (int slot = 0; slot < 8; slot++)
                {
                    Binding binding = expected.SingleOrDefault(item => item.rawSlot == slot);
                    Assert(Defaults.ForRawSlot(action, gamepad, slot) == (binding == null ? "None" : binding.key), "Raw slot default: " + action + "/" + slot);
                }
            }
        }
        Assert(!Defaults.HasAction("IA_UnregisteredTestAction") && Defaults.ForAction("IA_UnregisteredTestAction", false) == null, "Unknown action must remain distinct from a known action without device registrations");
        Assert(data.bindings.All(binding => binding.sources.Length != 0 && binding.sources.All(source => source.context.EndsWith(".uasset") && source.mappingIndex >= 0)), "Every registration retains resource provenance");
    }

    static void SpecialCases(Dataset data)
    {
        Assert(Defaults.RegisteredSlots("IA_Crouch", false).SequenceEqual(new[] { 1 }) && Defaults.RegisteredSlots("IA_Crouch", true).SequenceEqual(new[] { 0 }), "Crouch uses keyboard slot 1 and controller slot 0");
        Assert(Defaults.ForRawSlot("IA_Crouch", false, 1) == "C" && Defaults.ForRawSlot("IA_Crouch", true, 0) == "Gamepad_LeftThumbstick", "Crouch key/device association");
        Assert(Defaults.RegisteredSlots("IA_OpenMapMenu", false).SequenceEqual(new[] { 0, 2 }), "Map preserves distinct keyboard slots");
        Assert(Defaults.ForRawSlot("IA_OpenMapMenu", false, 0) == "M" && Defaults.ForRawSlot("IA_OpenMapMenu", false, 2) == "Tab", "Map per-slot defaults");
        Assert(Defaults.ForAction("IA_OpenMapMenu", false).SequenceEqual(new[] { "M", "Tab" }), "Map action defaults retain M and Tab");
        bool current = data.build == "CL-302256";
        Assert(Defaults.RegisteredSlots("IA_ShadowArm", false).SequenceEqual(current ? new[] { 0, 2 } : new[] { 0 })
            && Defaults.RegisteredSlots("IA_ShadowArm", true).SequenceEqual(current ? new[] { 1, 3 } : new[] { 1, 2, 3 }), "Build-specific Shadow Arm shared slot ownership");
        Assert(Defaults.EditableRegisteredSlots("IA_ShadowArm", false).SequenceEqual(new[] { 0 })
            && Defaults.Slots("IA_ShadowArm", false).SequenceEqual(new[] { 0, 4, 5 }), "Mirror is reserved while extra keyboard slots remain editable");
        Assert(data.bindings.Count(b => b.action == "IA_ShadowArm" && b.rawSlot == 2) == 2
            && data.effectiveBindings.Count(b => b.action == "IA_ShadowArm" && b.rawSlot == 2) == 1, "Resource inputs retain both contexts while the effective profile has a single shared identity");
        FixedMapping longPress = data.fixedMappings.Single(mapping => mapping.inputAction == "/PBZInput/InputActions/IA_ShachiAbility_LongPress" && mapping.device == "KeyboardMouse" && mapping.key == "LeftControl");
        Assert(longPress.actions.SequenceEqual(new[] { "IA_ShadowArm_LongPress" }) && longPress.sources.Any(source => source.context.EndsWith("/IMC_Default.uasset") && source.mappingIndex == 31 && source.triggers.Contains("InputTriggerHold")), "Fixed Ctrl long press retains action, context and trigger provenance");
        Assert(Defaults.RegisteredSlots("IA_ShadowArm_LongPress", false).Length == 0, "Fixed Ctrl mapping must not become a registered keyboard slot");
        Assert(Defaults.FixedForAction("IA_ShadowArm_LongPress", false).SequenceEqual(new[] { "LeftControl" }) && Defaults.ForAction("IA_ShadowArm_LongPress", false).SequenceEqual(new[] { "LeftControl" }), "Fixed Ctrl mapping contributes to action defaults");
        Assert(Defaults.ForRawSlot("IA_ShadowArm_LongPress", false, 0) == "None" && Defaults.ForRawSlot("IA_ShadowArm_LongPress", true, 0) == "Gamepad_RightTrigger", "Long press fixed mapping and controller registration remain distinct");
    }

    static void FixtureMatchesResources(Dataset data, string fixture)
    {
        byte[] original = File.ReadAllBytes(fixture);
        SaveFile save = SaveFile.FromBytes(original);
        var expected = Rows(data.bindings.Where(binding => binding.device == "Gamepad"));
        Assert(expected.Count == 30 && save.GamepadProfiles.Count == 3, "Three controller fixture profiles with 30 defaults each");
        int count = 0;
        foreach (GamepadProfile profile in save.GamepadProfiles)
        {
            Assert(data.profiles.Contains(profile.ProfileId) && profile.Family == "Xbox", "Fixture profile and family ownership");
            var actual = new HashSet<string>();
            foreach (var item in profile.Keys.Items)
            {
                string action = SaveFile.Field(item, "MappingName");
                int slot = InputDefaults.RawSlot(SaveFile.Field(item, "Slot"));
                string key = SaveFile.Field(item, "DefaultKey");
                Assert(actual.Add(Identity(action, true, slot, key)), "Duplicate fixture registration: " + profile.ProfileId + "/" + action);
                count++;
            }
            Assert(actual.SetEquals(expected), "Fixture defaults and slots must exactly match resource registrations: " + profile.ProfileId);
        }
        Assert(count == 90, "All 90 fixture default records checked");
        Assert(original.SequenceEqual(save.ToBytes()) && original.SequenceEqual(File.ReadAllBytes(fixture)), "Fixture remains byte-identical and is never written");
    }

    public static int Main(string[] args)
    {
        try
        {
            var serializer = new JavaScriptSerializer();
            Dataset data = serializer.Deserialize<Dataset>(File.ReadAllText(args[0]));
            Defaults = data.build == "CL-302256" ? InputDefaultSet.Current : InputDefaultSet.Legacy;
            ModelMatchesResources(data);
            SpecialCases(data);
            FixtureMatchesResources(data, args[1]);
            Console.WriteLine("InputDefaultsTests: " + checks + " checks passed; 60 resource registrations and 90 fixture defaults match.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
