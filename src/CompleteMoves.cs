using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public static class CompleteMoveCatalog
    {
        sealed class Move
        {
            public string id, sourceId, tag, abilityClass, entryPoint, inputAction, prepareInput, code, category, usageKey;
            public string executionChannel, executionType, completionTimeline;
            public string[] labelParts = new string[0];
            public string[] searchAliases = new string[0];
            public string[] requiredTags = new string[0];
            public string[] activationRequiredTags = new string[0];
            public string[] activationAnyTags = new string[0];
            public string[] ignoredTags = new string[0];
            public string[] activeBooleanProperties = new string[0];
            public string[] activeAbilityClasses = new string[0];
            public object[] entryArguments = new object[0];
            public Dictionary<string, object> entryWhen;
            public Dictionary<string, string> weaponLabels;
            public bool releaseInputBeforeEntry, activateByClass, activeCallbackOnly;
            public int priority;
        }
        sealed class Document
        {
            public Move[] moves;
        }
        static readonly Dictionary<string, Move> Moves = Load();

        public static KeyEnhancementCatalogEntry[] Targets
        {
            get
            {
                return Moves.Values.Select(move => new KeyEnhancementCatalogEntry {
                    Id = move.id, Kind = "move", Tag = move.id, Name = move.code
                }).ToArray();
            }
        }

        public static bool Contains(string id) { return id != null && Moves.ContainsKey(id); }
        internal static bool SupportsPhaseSpeed(string id)
        { Move move; return id == null || !Moves.TryGetValue(id, out move) || !move.activeCallbackOnly; }
        internal static string[] ResourceKinds(string id)
        {
            Move move;
            if (id == null || !Moves.TryGetValue(id, out move)) return new string[0];
            var activation = string.IsNullOrEmpty(move.abilityClass) ? ResourceCostCatalog.ForTag(move.tag) : ResourceCostCatalog.ForClass(move.abilityClass);
            return activation.Concat(move.activeAbilityClasses.SelectMany(ResourceCostCatalog.ForClass)).Distinct().ToArray();
        }
        public static string SourceId(string id)
        { Move move; return id != null && Moves.TryGetValue(id, out move) ? move.sourceId : id; }
        internal static string LabelSuffix(string id)
        { return string.Join(" · ", Moves[id].labelParts.Select(part => I18n.Str(part))); }
        internal static string LabelPrefix(string id, string fallback)
        {
            string label;
            return Moves[id].weaponLabels != null && Moves[id].weaponLabels.TryGetValue(I18n.Current, out label) ? label : fallback;
        }
        internal static string SearchAliases(string id)
        { Move move; return id != null && Moves.TryGetValue(id, out move) ? string.Join(" ", move.searchAliases) : ""; }
        internal static string Category(string id)
        { Move move; return id != null && Moves.TryGetValue(id, out move) ? move.category : null; }
        internal static string Usage(string id)
        { Move move; return id != null && Moves.TryGetValue(id, out move) && !string.IsNullOrEmpty(move.usageKey) ? I18n.Str(move.usageKey) : ""; }

        internal static Dictionary<string, object> Target(KeyEnhancementOutput output)
        {
            Move move;
            if (output.Tag == null || !Moves.TryGetValue(output.Tag, out move))
                throw new InvalidDataException(I18n.Str("enhancement.invalid", "Output.Tag: " + output.Tag));
            if (output.Force) throw new InvalidDataException(I18n.Str("enhancement.invalid", "Output.Force: move"));
            var target = new Dictionary<string, object> {
                { "kind", "move" }, { "move_id", move.id }, { "tag", move.tag },
                { "input_action", move.inputAction }, { "required_tags", move.requiredTags }, { "priority", move.priority }
            };
            if (!string.IsNullOrEmpty(move.abilityClass)) target["ability_class"] = move.abilityClass;
            if (move.activateByClass) target["activate_by_class"] = true;
            if (!string.IsNullOrEmpty(move.prepareInput)) target["prepare_input"] = move.prepareInput;
            if (!string.IsNullOrEmpty(move.completionTimeline)) target["completion_timeline"] = move.completionTimeline;
            if (move.ignoredTags.Length > 0) target["ignored_tags"] = move.ignoredTags;
            if (move.activationRequiredTags.Length > 0) target["activation_required_tags"] = move.activationRequiredTags;
            if (move.activationAnyTags.Length > 0) target["activation_any_tags"] = move.activationAnyTags;
            if (!string.IsNullOrEmpty(move.executionType))
            {
                target["execution_channel"] = move.executionChannel;
                target["execution_type"] = move.executionType;
            }
            if (!string.IsNullOrEmpty(move.entryPoint))
            {
                if (move.activeAbilityClasses.Length > 0) target["active_ability_classes"] = move.activeAbilityClasses;
                if (move.activeCallbackOnly) target["active_callback_only"] = true;
                target["entry_point"] = move.entryPoint.StartsWith("/", StringComparison.Ordinal)
                    ? move.entryPoint : move.abilityClass + ":" + move.entryPoint;
                if (move.entryArguments.Length > 0) target["entry_arguments"] = move.entryArguments;
                if (move.entryWhen != null) target["entry_when"] = move.entryWhen;
                if (move.releaseInputBeforeEntry) target["release_input_before_entry"] = true;
                if (move.activeBooleanProperties.Length > 0)
                    target["active_boolean_properties"] = move.activeBooleanProperties;
            }
            return target;
        }

        static Dictionary<string, Move> Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.KeyEnhancement.complete-moves.json"))
            {
                if (stream == null) throw new InvalidDataException("PBZKeyForge.KeyEnhancement.complete-moves.json");
                using (var reader = new StreamReader(stream))
                    return new JavaScriptSerializer().Deserialize<Document>(reader.ReadToEnd()).moves.ToDictionary(move => move.id, StringComparer.Ordinal);
            }
        }
    }
}
