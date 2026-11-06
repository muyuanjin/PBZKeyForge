using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public static class ActionCatalogLabels
    {
        sealed class Detail
        {
            public string section, prefix, variant, category;
            public string[] parts = new string[0], aliases = new string[0];
            public string[] factoryKeyboardKeys = new string[0], factoryGamepadKeys = new string[0];
            public bool semantic, nativeMapping;
            public int order;
        }
        sealed class Document
        {
            public Dictionary<string, Detail> entries;
        }
        static readonly Dictionary<string, Detail> Details = Load();

        public static string Label(string id, string nativeLabel)
        {
            Detail detail;
            if (id == null || !id.Contains(":") || !Details.TryGetValue(id, out detail) || detail.parts.Length == 0) return nativeLabel;
            var parts = detail.parts.Select(part => part.StartsWith("@", StringComparison.Ordinal) ? I18n.Str(part.Substring(1)) : part);
            string prefix = Prefix(id, nativeLabel);
            return string.Join(" · ", (string.IsNullOrWhiteSpace(prefix) ? Enumerable.Empty<string>() : new[] { prefix }).Concat(parts));
        }

        internal static string Prefix(string id, string nativeLabel)
        {
            Detail detail;
            if (id == null || !Details.TryGetValue(id, out detail)) return nativeLabel;
            string prefix = detail.prefix == "none" ? "" : nativeLabel;
            if (detail.prefix == "weapon")
            {
                int separator = (prefix ?? "").IndexOf(" · ", StringComparison.Ordinal);
                if (separator >= 0) prefix = prefix.Substring(0, separator);
            }
            return prefix;
        }

        public static string SearchAliases(string id)
        {
            if (CompleteMoveCatalog.Contains(id)) return CompleteMoveCatalog.SearchAliases(id);
            Detail detail;
            if (id == null || !Details.TryGetValue(id, out detail)) return "";
            return string.Join(" ", detail.aliases.Concat(new[] { detail.variant ?? "" }));
        }

        public static bool IsSemantic(string id)
        { Detail detail; return CompleteMoveCatalog.Contains(id) || id != null && Details.TryGetValue(id, out detail) && detail.semantic; }

        public static string Section(string id)
        { Detail detail; return id != null && Details.TryGetValue(CompleteMoveCatalog.SourceId(id), out detail) ? detail.section : ""; }

        public static string[] Category(string id)
        {
            Detail detail;
            return id != null && Details.TryGetValue(CompleteMoveCatalog.SourceId(id), out detail) && !string.IsNullOrEmpty(detail.category)
                ? detail.category.Split('/') : new string[0];
        }

        public static bool HasNativeMapping(string id)
        { Detail detail; return id != null && Details.TryGetValue(id, out detail) && detail.nativeMapping; }

        internal static int Order(string id)
        { Detail detail; return id != null && Details.TryGetValue(CompleteMoveCatalog.SourceId(id), out detail) ? detail.order : 0; }

        public static string[] FactoryKeys(string id, bool pad)
        {
            Detail detail;
            if (id == null || !Details.TryGetValue(id, out detail)) return new string[0];
            return (pad ? detail.factoryGamepadKeys : detail.factoryKeyboardKeys).ToArray();
        }

        static Dictionary<string, Detail> Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.KeyEnhancement.action-details.json"))
            {
                if (stream == null) throw new InvalidDataException("PBZKeyForge.KeyEnhancement.action-details.json");
                using (var reader = new StreamReader(stream))
                    return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd()).entries;
            }
        }
    }
}
