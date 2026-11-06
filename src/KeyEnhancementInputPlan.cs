using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public static class KeyEnhancementInputPlan
    {
        public sealed class ActionPlan
        {
            public string path;
            public string[] mappingNames;
        }
        sealed class Plan
        {
            public ActionPlan[] actions { get; set; }
            public string[] retainedNative { get; set; }
        }
        static readonly Plan Data = Load();
        public static ActionPlan[] Actions { get { return Data.actions; } }
        public static string[] Uncovered { get { return new[] { "mouse", "scroll", "touchpad", "chord", "trigger", "gamepad", "edges" }.Select(key => I18n.Str("ownership.coverage." + key)).ToArray(); } }
        public static bool Retained(string mapping)
        { return KeyEnhancementCatalog.ForMapping(mapping).All(target => Data.retainedNative.Contains(target.Path)); }
        public static bool Covers(string mapping)
        { return Data.actions.Any(action => action.mappingNames.Contains(mapping)); }
        public static bool SupportsMapping(KeyEnhancementOutput output)
        {
            return output.Kind != "mapping" || output.MappingActions != null && output.MappingActions.Length > 0
                && output.MappingActions.All(path => Data.actions.Any(action => action.path == PackagePath(path)));
        }
        public static void ValidateMapping(KeyEnhancementOutput output)
        {
            if (!SupportsMapping(output)) throw new InvalidDataException(I18n.Str("unified.unsupportedmapping",
                BindingWorkspace.FunctionLabel(output.MappingName), output.MappingName));
        }
        static string PackagePath(string path)
        { return path == null ? "" : System.Text.RegularExpressions.Regex.Replace(path, "\\.[^./]+$", ""); }
        public static bool Covers(string mapping, string key)
        {
            return Covers(mapping) && key != "Mouse2D" && key != "MouseX" && key != "MouseY"
                && !key.StartsWith("MouseScroll", System.StringComparison.Ordinal)
                && !key.StartsWith("PBZ_Touchpad_", System.StringComparison.Ordinal);
        }
        static Plan Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.KeyEnhancement.native-input-plan.json"))
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer().Deserialize<Plan>(reader.ReadToEnd());
        }
    }
}
