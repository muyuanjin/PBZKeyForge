using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    internal static class GameMoveNames
    {
        sealed class Effect
        {
            public string kind;
            public Dictionary<string, string> labels, descriptionLabels;
        }
        sealed class Entry
        {
            public Dictionary<string, string> labels;
            public string[] labelParts = new string[0];
            public Dictionary<string, string[]> aliases;
            public Effect[] effects = new Effect[0];
        }
        sealed class Document
        {
            public Dictionary<string, Entry> entries;
        }
        static readonly Dictionary<string, Entry> Entries = Load();

        internal static string Label(string id, string fallback)
        {
            Entry entry;
            string label;
            if (id == null || !Entries.TryGetValue(id, out entry) || entry.labels == null
                || !entry.labels.TryGetValue(I18n.Current, out label) || string.IsNullOrWhiteSpace(label)) return fallback;
            return string.Join(" · ", new[] { label }.Concat(entry.labelParts.Select(part => I18n.Str(part))));
        }

        internal static string[] SearchNames(string id)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return new string[0];
            return (entry.labels == null ? Enumerable.Empty<string>() : entry.labels.Values)
                .Concat(entry.aliases == null ? Enumerable.Empty<string>() : entry.aliases.Values.SelectMany(names => names))
                .Concat(entry.effects.Where(effect => effect.labels != null).SelectMany(effect => effect.labels.Values))
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
        }

        internal static string Details(string id, string query = null)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return "";
            query = (query ?? "").Trim();
            var effects = entry.effects.Select(effect => new { Effect = effect, Name = Text(effect.labels) })
                .Where(item => item.Name.Length > 0)
                .OrderByDescending(item => query.Length > 0 && item.Effect.labels.Values.Any(name => name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            return string.Join(Environment.NewLine, effects.Select(item =>
                (item.Effect.kind == "upgrade" ? I18n.Str("moves.nativeupgrade", item.Name) : item.Name)
                + (Text(item.Effect.descriptionLabels).Length == 0 ? "" : Environment.NewLine + Text(item.Effect.descriptionLabels))));
        }

        static string Text(Dictionary<string, string> labels)
        {
            string label;
            return labels != null && labels.TryGetValue(I18n.Current, out label) ? label ?? "" : "";
        }

        static Dictionary<string, Entry> Load()
        {
            const string resource = "PBZKeyForge.KeyEnhancement.game-move-names.json";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidDataException(resource);
                using (var reader = new StreamReader(stream))
                    return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd()).entries;
            }
        }
    }
}
