using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public static class ResourceCostCatalog
    {
        sealed class Document
        {
            public Dictionary<string, string[]> classes, tags, inputTags;
        }
        static readonly Document Data = Load();
        internal static Dictionary<string, string[]> InputTags { get { return Data.inputTags; } }
        internal static bool IsCombatInput(string path)
        { return path != null && Data.inputTags.ContainsKey(System.Text.RegularExpressions.Regex.Replace(path, "\\.[^./]+$", "")); }
        internal static string[] ForClass(string path)
        { string[] kinds; return path != null && Data.classes.TryGetValue(path, out kinds) ? kinds : new string[0]; }
        internal static string[] ForTag(string tag)
        { string[] kinds; return tag != null && Data.tags.TryGetValue(tag, out kinds) ? kinds : new string[0]; }
        internal static string InputForTag(string tag)
        {
            var inputs = Data.inputTags.Where(item => item.Value.Contains(tag)).Select(item => item.Key).ToArray();
            return inputs.Length == 1 ? inputs[0] + "." + inputs[0].Substring(inputs[0].LastIndexOf('/') + 1) : null;
        }
        public static string[] ForOutput(KeyEnhancementOutput output)
        {
            if (output.Kind == "move") return CompleteMoveCatalog.ResourceKinds(output.Tag);
            if (output.Kind == "ability") return ForTag(output.Tag);
            var paths = output.Kind == "mapping" ? output.MappingActions ?? new string[0]
                : output.Kind == "action" ? new[] { output.Path } : new string[0];
            return paths.Where(path => path != null).SelectMany(path => {
                string[] tags;
                return Data.inputTags.TryGetValue(System.Text.RegularExpressions.Regex.Replace(path, "\\.[^./]+$", ""), out tags)
                    ? tags.SelectMany(ForTag) : new string[0];
            }).Distinct().ToArray();
        }
        public static string Describe(IEnumerable<string> kinds)
        { return string.Join(" / ", kinds.Distinct().Select(kind => I18n.Str("binding.resources." + kind))); }
        static Document Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.KeyEnhancement.resource-costs.json"))
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd());
        }
    }
}
