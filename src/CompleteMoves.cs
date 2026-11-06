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
            public string id, sourceId, tag, abilityClass, entryPoint, inputAction, code;
            public string[] labelParts = new string[0];
            public string[] searchAliases = new string[0];
            public string[] requiredTags = new string[0];
            public string[] ignoredTags = new string[0];
            public string[] activeBooleanProperties = new string[0];
            public object[] entryArguments = new object[0];
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
        public static string SourceId(string id)
        { Move move; return id != null && Moves.TryGetValue(id, out move) ? move.sourceId : id; }
        internal static string LabelSuffix(string id)
        { return string.Join(" · ", Moves[id].labelParts.Select(part => I18n.Str(part))); }
        internal static string SearchAliases(string id)
        { Move move; return id != null && Moves.TryGetValue(id, out move) ? string.Join(" ", move.searchAliases) : ""; }

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
            if (move.ignoredTags.Length > 0) target["ignored_tags"] = move.ignoredTags;
            if (!string.IsNullOrEmpty(move.entryPoint))
            {
                target["ability_class"] = move.abilityClass;
                target["entry_point"] = move.abilityClass + ":" + move.entryPoint;
                if (move.entryArguments.Length > 0) target["entry_arguments"] = move.entryArguments;
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
