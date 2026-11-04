using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PBZKeyForge
{
    internal static class ControllerProfiles
    {
        internal static readonly string[] SupportedFamilies = { "Xbox", "PS" };
        static readonly byte[] FactoryBytes = LoadFactoryBytes();
        static readonly List<Node> FactoryProfileFields = FactorySave().GamepadProfiles.First().Fields;

        static byte[] LoadFactoryBytes()
        {
            using (var stream = typeof(ControllerProfiles).Assembly.GetManifestResourceStream("PBZKeyForge.vanilla-base.sav"))
            using (var output = new MemoryStream())
            {
                if (stream == null) throw new InvalidDataException("The factory input template is missing.");
                stream.CopyTo(output);
                return output.ToArray();
            }
        }

        internal static SaveFile FactorySave()
        { return FactorySave(InputDefaultSet.Legacy); }

        internal static SaveFile FactorySave(InputDefaultSet defaults)
        {
            var save = SaveFile.FromBytes(FactoryBytes, defaults);
            save.ClearKeyboardOverrides();
            save.ResetGamepadToDefaults();
            foreach (var keys in save.GamepadKeys)
                keys.Items.RemoveAll(item => defaults.HasAction(SaveFile.Field(item, "MappingName"))
                    && !defaults.RegisteredSlots(SaveFile.Field(item, "MappingName"), true).Contains(InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))));
            return save;
        }

        internal static GamepadProfile Create(string profileId, string family, InputDefaultSet defaults)
        {
            var fields = FactoryProfileFields.Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(fields, "ProfileId", profileId);
            SaveFile.SetField(fields, "Family", family);
            var keys = fields.First(node => node.Name == "Keys");
            keys.Items.RemoveAll(item => defaults.HasAction(SaveFile.Field(item, "MappingName"))
                && !defaults.RegisteredSlots(SaveFile.Field(item, "MappingName"), true).Contains(InputDefaults.RawSlot(SaveFile.Field(item, "Slot"))));
            return new GamepadProfile { ProfileId = profileId, Family = family, Fields = fields, Keys = keys };
        }

        internal static IEnumerable<List<Node>> EditingKeys(GamepadProfile profile, InputDefaultSet defaults)
        {
            foreach (var item in profile.Keys.Items) yield return item;
            foreach (var item in FactoryProfileFields.First(node => node.Name == "Keys").Items)
            {
                string action = SaveFile.Field(item, "MappingName"), slot = SaveFile.Field(item, "Slot"), defaultKey = SaveFile.Field(item, "DefaultKey");
                if ((!defaults.HasAction(action) || defaults.RegisteredSlots(action, true).Contains(InputDefaults.RawSlot(slot)))
                    && !profile.Keys.Items.Any(existing => SaveFile.Field(existing, "MappingName") == action
                    && SaveFile.Field(existing, "Slot") == slot && SaveFile.Field(existing, "DefaultKey") == defaultKey))
                    yield return item.Select(SaveFile.CloneNode).ToList();
            }
        }

        internal static List<Node> KeyTemplate(Node keys, string action)
        {
            var factory = FactoryProfileFields.First(node => node.Name == "Keys").Items;
            return keys.Items.FirstOrDefault(item => SaveFile.Field(item, "MappingName") == action) ?? keys.Items.FirstOrDefault()
                ?? factory.FirstOrDefault(item => SaveFile.Field(item, "MappingName") == action) ?? factory.First();
        }

        internal static void InitializeStorage(SaveFile save)
        {
            var version = save.Props.FirstOrDefault(node => node.Name == "GamepadFamilyStorageVersion");
            if (version == null)
            {
                version = SaveFile.CloneNode(FactorySave().Props.First(node => node.Name == "GamepadFamilyStorageVersion"));
                save.Props.Insert(save.Props.FindIndex(node => node.IsTerm), version);
            }
            else if (BitConverter.ToInt32(version.Raw, 0) == 0) version.Raw = BitConverter.GetBytes(1);
        }

        internal static void SetKey(SaveFile save, List<Node> item, string key)
        {
            if (SaveFile.Field(item, "Key") == key) return;
            SaveFile.SetField(item, "Key", key);
            InitializeStorage(save);
        }

        internal static void CopyKeys(SaveFile save, Node destination, Node source)
        {
            string[] names = { "MappingName", "Slot", "DefaultKey", "Key" };
            if (!destination.Items.SelectMany(item => names.Select(name => SaveFile.Field(item, name)))
                .SequenceEqual(source.Items.SelectMany(item => names.Select(name => SaveFile.Field(item, name)))))
                InitializeStorage(save);
            destination.Items = source.Items;
        }

        internal static IEnumerable<GamepadProfile> ForEditing(SaveFile save)
        {
            var existing = save.GamepadProfiles;
            foreach (var profile in existing) yield return profile;
            int index = existing.Count;
            foreach (var record in save.Records)
                foreach (string family in SupportedFamilies)
                    if (!existing.Any(profile => profile.ProfileId == record.ProfileId && profile.Family == family))
                    {
                        var profile = Create(record.ProfileId, family, save.Defaults);
                        profile.Index = index++;
                        yield return profile;
                    }
        }

        internal static GamepadProfile Ensure(SaveFile save, string profileId, string family)
        {
            var existing = save.GamepadProfiles.FirstOrDefault(profile => profile.ProfileId == profileId && profile.Family == family);
            if (existing != null) return existing;
            if (!SupportedFamilies.Contains(family)) throw new InvalidOperationException(I18n.Str("preset.familymissing"));
            var container = save.Props.FirstOrDefault(node => node.Name == "GamepadFamilyProfiles");
            if (container == null)
            {
                container = SaveFile.CloneNode(FactorySave().Props.First(node => node.Name == "GamepadFamilyProfiles"));
                container.Items.Clear();
                save.Props.Insert(save.Props.FindIndex(node => node.IsTerm), container);
            }
            var created = Create(profileId, family, save.Defaults);
            container.Items.Add(created.Fields);
            InitializeStorage(save);
            return save.GamepadProfiles.Last();
        }

        internal static void PreparePreset(SaveFile save, Preset preset, string profileId)
        {
            foreach (string family in PresetFamilies(preset)) Ensure(save, profileId, family);
        }

        internal static bool NeedsPresetFamilies(SaveFile save, Preset preset, string profileId)
        {
            return PresetFamilies(preset).Any(family => !save.GamepadProfiles.Any(profile => profile.ProfileId == profileId && profile.Family == family));
        }

        static IEnumerable<string> PresetFamilies(Preset preset)
        {
            return preset.IsUser ? preset.Scopes.Where(scope => scope.Family != null).Select(scope => scope.Family)
                : preset.AdditiveGuard || preset.Gamepad.Count > 0 ? SupportedFamilies : new string[0];
        }
    }
}
