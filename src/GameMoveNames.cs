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
            return DisplayNames(id, fallback)[0];
        }

        internal static string[] DisplayNames(string id, string fallback)
        {
            return DisplayNames(id, fallback, I18n.Current);
        }

        static string[] DisplayNames(string id, string fallback, string language)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return new[] { fallback };
            return DisplayNames(id, entry, fallback, language);
        }

        static string[] DisplayNames(string id, Entry entry, string fallback, string language)
        {
            var names = new List<string>();
            string primary = Text(entry.labels, language);
            string suffix = string.Join(" · ", entry.labelParts.Select(part => I18n.Str(part)));
            string primaryName = primary.Length == 0 ? "" : AppendSuffix(primary, suffix);
            string prefix = NamePrefix(id, primary);
            AddName(names, primaryName);
            foreach (string name in EffectNames(entry, language))
            {
                if (name.Length == 0) continue;
                AddName(names, AppendSuffix(QualifyName(name, prefix), suffix));
            }
            if (names.Count == 0) names.Add(fallback ?? "");
            return names.ToArray();
        }

        static string NamePrefix(string id, string primary)
        {
            string prefix = CompleteMoveCatalog.LabelPrefix(id, "");
            if (!string.IsNullOrEmpty(prefix)) return prefix;
            int separator = primary.IndexOf(" · ", StringComparison.Ordinal);
            return separator < 0 ? primary : primary.Substring(0, separator);
        }

        static string QualifyName(string name, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)
                || name.Equals(prefix, StringComparison.Ordinal)
                || name.StartsWith(prefix + " · ", StringComparison.Ordinal)) return name;
            return prefix + " · " + name;
        }

        static string AppendSuffix(string name, string suffix)
        {
            return string.IsNullOrEmpty(suffix) || name.EndsWith(" · " + suffix, StringComparison.Ordinal)
                ? name : name + " · " + suffix;
        }

        static IEnumerable<string> EffectNames(Entry entry, string language)
        {
            string[] variants = entry.aliases == null || !entry.aliases.ContainsKey(language)
                ? new string[0] : entry.aliases[language].Where(IsVariant).ToArray();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in entry.effects)
            {
                string name = Text(effect.labels, language);
                if (name.Length == 0) continue;
                string variant = variants.FirstOrDefault(value => ExpandVariants(value).Contains(name, StringComparer.Ordinal));
                if (variant == null)
                {
                    yield return name;
                    continue;
                }
                used.Add(variant);
                foreach (string expanded in ExpandVariants(variant)) yield return expanded;
            }
            foreach (string variant in variants.Where(value => !used.Contains(value)))
                foreach (string expanded in ExpandVariants(variant)) yield return expanded;
        }

        static bool IsVariant(string value)
        {
            return value != null && value.IndexOf('{') >= 0 && value.IndexOf('}', value.IndexOf('{') + 1) > 0;
        }

        static IEnumerable<string> ExpandVariants(string text)
        {
            int open = text.IndexOf('{'), close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                yield return text;
                yield break;
            }
            string before = text.Substring(0, open), after = text.Substring(close + 1);
            foreach (string option in text.Substring(open + 1, close - open - 1).Split('|'))
                foreach (string expanded in ExpandVariants(before + option + after)) yield return expanded;
        }

        static void AddName(List<string> names, string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }

        internal static string[] SearchNames(string id)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return new string[0];
            return (entry.labels == null ? Enumerable.Empty<string>() : entry.labels.Values)
                .Concat(entry.aliases == null ? Enumerable.Empty<string>()
                    : entry.aliases.Values.SelectMany(names => names).SelectMany(ExpandVariants))
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
            return Text(labels, I18n.Current);
        }

        static string Text(Dictionary<string, string> labels, string language)
        {
            string label;
            return labels != null && labels.TryGetValue(language, out label) ? label ?? "" : "";
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
