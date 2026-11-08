using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    internal static class MoveInsightCatalog
    {
        sealed class Entry
        {
            public string id;
            public int attackNodes;
            public double[] physical, poise, element, anger, chaos, lightning, fire, ice, @true;
            public string[] unresolvedBulletData = new string[0];
        }
        sealed class Document
        {
            public Entry[] moves;
        }
        sealed class Category
        {
            internal string Key;
            internal Func<Entry, double[]> Values;
        }
        static readonly Category[] Categories = {
            new Category { Key = "physical", Values = entry => entry.physical },
            new Category { Key = "poise", Values = entry => entry.poise },
            new Category { Key = "element", Values = entry => entry.element },
            new Category { Key = "anger", Values = entry => entry.anger },
            new Category { Key = "chaos", Values = entry => entry.chaos },
            new Category { Key = "lightning", Values = entry => entry.lightning },
            new Category { Key = "fire", Values = entry => entry.fire },
            new Category { Key = "ice", Values = entry => entry.ice },
            new Category { Key = "true", Values = entry => entry.@true }
        };
        static readonly Category[] DamageCategories = {
            new Category { Key = "physical", Values = entry => entry.physical },
            new Category { Key = "element", Values = entry => entry.element },
            new Category { Key = "fire", Values = entry => entry.fire },
            new Category { Key = "ice", Values = entry => entry.ice },
            new Category { Key = "lightning", Values = entry => entry.lightning },
            new Category { Key = "chaos", Values = entry => entry.chaos },
            new Category { Key = "anger", Values = entry => entry.anger },
            new Category { Key = "true", Values = entry => entry.@true }
        };
        static readonly Dictionary<string, Entry> Entries = Load();

        internal static string Summary(string id)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return "";
            string primary = "";
            string primaryLabel = "";
            foreach (var category in DamageCategories)
            {
                primary = Values(category.Values(entry));
                if (primary.Length == 0) continue;
                primaryLabel = I18n.Str("moves.insight." + category.Key);
                break;
            }
            string poise = Values(entry.poise);
            string nodes = entry.attackNodes == 0
                ? I18n.Str("moves.insight.nonodes")
                : I18n.Str("moves.insight.nodes", entry.attackNodes);
            string first = primary.Length == 0 ? I18n.Str("moves.insight.none") : primaryLabel + " " + primary;
            string second = poise.Length == 0
                ? nodes
                : I18n.Str("moves.insight.poise") + " " + poise + " · " + nodes;
            return first + Environment.NewLine + second;
        }

        internal static string Details(string id)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return "";
            var lines = new List<string> { I18n.Str("unified.grid.insight") };
            foreach (var category in Categories)
            {
                double[] values = category.Values(entry);
                if (values == null || values.Length == 0) continue;
                lines.Add(I18n.Str("moves.insight." + category.Key) + ": "
                    + string.Join(" / ", values.Select(FormatValue)));
            }
            lines.Add(entry.attackNodes == 0
                ? I18n.Str("moves.insight.nonodes")
                : I18n.Str("moves.insight.nodes", entry.attackNodes));
            if (entry.unresolvedBulletData != null && entry.unresolvedBulletData.Length > 0)
                lines.Add(I18n.Str("moves.insight.details.incomplete"));
            lines.Add(I18n.Str("moves.insight.details.source"));
            lines.Add(I18n.Str("moves.insight.details.damage"));
            lines.Add(I18n.Str("moves.insight.details.nodes"));
            return string.Join(Environment.NewLine, lines);
        }

        internal static string SearchText(string id)
        {
            Entry entry;
            if (id == null || !Entries.TryGetValue(id, out entry)) return "";
            return entry.attackNodes + " "
                + string.Join(" ", Categories.SelectMany(category => category.Values(entry) ?? new double[0]).Select(FormatValue));
        }

        static string Values(double[] values)
        {
            if (values == null || values.Length == 0) return "";
            double[] sorted = values.Where(value => !double.IsNaN(value) && !double.IsInfinity(value)).OrderBy(value => value).ToArray();
            if (sorted.Length == 0) return "";
            return sorted.Length == 1 ? FormatValue(sorted[0]) : FormatValue(sorted[0]) + "–" + FormatValue(sorted[sorted.Length - 1]);
        }

        static string FormatValue(double value)
        {
            string format = Math.Abs(value) >= 10 ? "0.#" : Math.Abs(value) >= 1 ? "0.##" : "0.###";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        static Dictionary<string, Entry> Load()
        {
            const string resource = "PBZKeyForge.KeyEnhancement.move-insights.json";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidDataException(resource);
                using (var reader = new StreamReader(stream))
                {
                    var document = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd());
                    return document.moves.ToDictionary(entry => entry.id, StringComparer.Ordinal);
                }
            }
        }
    }
}
