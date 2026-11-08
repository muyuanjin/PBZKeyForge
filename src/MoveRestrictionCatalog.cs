using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    internal static class MoveRestrictionCatalog
    {
        sealed class Group
        {
            public string[] requiredTags = new string[0], ignoredTags = new string[0];
        }
        sealed class Entry
        {
            public Dictionary<string, Group> groups;
            public Dictionary<string, object> preparation;
        }
        sealed class Document
        {
            public Dictionary<string, string> classificationTags;
            public Dictionary<string, Entry> entries;
        }
        static readonly Document Data = Load();
        internal static Dictionary<string, string> ClassificationTags { get { return Data.classificationTags; } }

        internal static void Apply(Dictionary<string, object> target, KeyEnhancementOutput output, bool restrictions)
        {
            string identity = output.Kind == "move" ? output.Tag : output.Kind == "ability" ? "ability:" + output.Tag
                : output.Kind == "action" ? "action:" + (output.Path ?? "").Split('.')[0] : output.FunctionId;
            Entry entry;
            if (identity == null || !Data.entries.TryGetValue(identity, out entry)) return;
            object value;
            if (entry.preparation.TryGetValue("nativeHoldInputs", out value)) target["native_hold_inputs"] = value;
            if (entry.preparation.TryGetValue("nativeHoldVariants", out value)) target["native_hold_variants"] = value;
            if (entry.preparation.TryGetValue("automaticReleaseWhen", out value)) target["native_release_when"] = value;
            if (entry.preparation.TryGetValue("abilityClass", out value)) target["native_preparation_class"] = value;
            if (restrictions)
            {
                target["restriction_groups"] = entry.groups.ToDictionary(pair => pair.Key, pair => (object)new Dictionary<string, object> {
                    {"required_tags", pair.Value.requiredTags}, {"ignored_tags", pair.Value.ignoredTags}
                });
                if (entry.preparation.ContainsKey("equipmentSlot") && entry.preparation.TryGetValue("equipmentConfigs", out value)) target["equipment_configs"] = value;
                if (entry.preparation.TryGetValue("equipmentSlot", out value)) target["equipment_slot"] = value;
                if (entry.preparation.TryGetValue("nativeActivation", out value)) target["native_activation"] = value;
                if (entry.preparation.TryGetValue("nativePreparationEntry", out value)) target["native_preparation_entry"] = value;
                if (entry.preparation.TryGetValue("armedState", out value)) target["armed_state"] = value;
                if (entry.preparation.TryGetValue("nativeStateEffects", out value)) target["native_state_effects"] = value;
                if (entry.preparation.TryGetValue("snakeProjectile", out value)) target["snake_projectile"] = value;
                if (entry.preparation.TryGetValue("snakeNormal", out value)) target["snake_normal"] = value;
            }
        }

        static Document Load()
        {
            const string resource = "PBZKeyForge.KeyEnhancement.move-restrictions.json";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidDataException(resource);
                using (var reader = new StreamReader(stream))
                    return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd());
            }
        }
    }
}
