using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed class KeyEnhancementSuppression
    {
        public string ActionPath, Key, FunctionId;
    }

    public sealed class KeyEnhancementOutput
    {
        public string Kind = "action", Path, Tag, Receiver = "pawn", ValueType, Property, Code, InputAction, MappingName, Device;
        public bool? NativePipeline;
        public string Id, Name, FunctionId;
        public string[] MappingActions;
        public double? Clamp;
        public List<string> Modifiers = new List<string>(), Triggers = new List<string>();
        public object Value, Payload;
        public object[] Args;
        public bool Force;
        public int Priority = -5;
    }

    public sealed class KeyEnhancementRule
    {
        public string InputGroup, AxisDirection = "both";
        public string Id = Guid.NewGuid().ToString("N"), Axis, Trigger = "press", WhenLua, EvaluateLua;
        public bool Enabled = true, Consume, BlockOriginalWhenDisabled;
        public bool DefaultTrigger, BypassResources;
        public double DamageMultiplier = 1, StartupSpeed = 1, RecoverySpeed = 1;
        public List<string> Keys = new List<string>(), ConsumeKeys = new List<string>(), Watch = new List<string>();
        public List<string[]> Steps = new List<string[]>();
        public double Seconds = 0.25, Interval = 0.3, Threshold = 0.5;
        public int Count = 2, Priority;
        public List<KeyEnhancementOutput> Outputs = new List<KeyEnhancementOutput>();
        public List<KeyEnhancementSuppression> Suppressions = new List<KeyEnhancementSuppression>();

        public void NormalizeInput()
        {
            if (Trigger != "sequence") Steps.Clear();
            if (Trigger != "custom") { Watch.Clear(); EvaluateLua = null; }
            if (Trigger == "axis" || Trigger == "custom") Keys.Clear();
            if (Trigger != "axis") Axis = null;
        }
    }

    public sealed partial class KeyEnhancementConfiguration
    {
        public List<NativeBindingOverride> NativeBindings = new List<NativeBindingOverride>();
        public bool Enabled, RuntimeHotkeys;
        public List<KeyEnhancementRule> Rules = new List<KeyEnhancementRule>();
        public List<KeyEnhancementOutput> CustomFunctions = new List<KeyEnhancementOutput>();
        public List<KeyEnhancementSuppression> Suppressions = new List<KeyEnhancementSuppression>();
        public List<Dictionary<string, object>> Hooks = new List<Dictionary<string, object>>();
        public List<string> NativeAbilitySuppressions = new List<string>();
        public string CustomLua;
        public static readonly string[] Triggers = { "down", "press", "release", "tap", "hold", "pulse", "multi_tap", "sequence", "axis", "toggle", "custom" };

        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 16777216 }; }
        public string ToJson() { StoreSelectedScheme(); return Serializer().Serialize(this); }
        public static KeyEnhancementConfiguration FromJson(string text)
        {
            var result = Serializer().Deserialize<KeyEnhancementConfiguration>(text);
            if (result == null || result.Rules == null || result.Suppressions == null || result.Hooks == null)
                throw Invalid("Rules, Suppressions, Hooks: []");
            result.ValidateStructure();
            result.ValidateSchemes();
            if (result.Enabled && result.Schemes.Count > 0) result.LoadSelectedScheme();
            return result;
        }
        public KeyEnhancementConfiguration Clone() { return FromJson(ToJson()); }
        public string ToLua() { return CompileLua(); }

        public string CompileLua()
        {
            if (!Enabled || Schemes.Count == 0) return CompileSingleLua();
            StoreSelectedScheme(); ValidateSchemes();
            var text = new StringBuilder("local schemes = {}\nlocal order = {}\n");
            foreach (var scheme in Schemes)
            {
                var current = ConfigurationFor(scheme.Id);
                current.CustomLua = null;
                text.Append("schemes[").Append(Lua(scheme.Id)).Append("] = (function()\n").Append(current.CompileSingleLua()).Append("\nend)()\n");
                text.Append("schemes[").Append(Lua(scheme.Id)).Append("].scheme_id = ").Append(Lua(scheme.Id)).Append("\n");
                text.Append("schemes[").Append(Lua(scheme.Id)).Append("].scheme_name = ").Append(Lua(scheme.Name ?? scheme.Id)).Append("\n");
                text.Append("order[#order+1] = ").Append(Lua(scheme.Id)).Append("\n");
            }
            text.Append("local configure = function(config)\n").Append(CustomLua ?? "").Append("\nreturn config\nend\n");
            text.Append("for _, scheme in pairs(schemes) do scheme.configure_scheme = configure end\n");
            text.Append("local config = schemes[").Append(Lua(ActiveSchemeId ?? SelectedSchemeId)).Append("]\n");
            text.Append("config.schemes = schemes\nconfig.scheme_order = order\nreturn config\n");
            return text.ToString();
        }

        string CompileSingleLua()
        {
            ValidateStructure();
            var targets = new Dictionary<string, object>();
            var bindings = new List<object>();
            var suppressions = new List<object>();
            foreach (var item in Suppressions) suppressions.Add(Suppression(item));
            foreach (var rule in Rules)
            {
                if (!rule.Enabled)
                {
                    if (rule.BlockOriginalWhenDisabled) foreach (var item in rule.Suppressions) suppressions.Add(Suppression(item));
                    continue;
                }
                if (!Triggers.Contains(rule.Trigger)) throw Invalid("Trigger=" + rule.Trigger);
                if (rule.Outputs.Count == 0) throw Invalid(rule.Id + ".Outputs: count >= 1");
                if (rule.Trigger == "custom" && string.IsNullOrWhiteSpace(rule.EvaluateLua)) throw Invalid(rule.Id + ".EvaluateLua: function");
                if (rule.Trigger == "sequence" && rule.Steps.Count == 0) throw Invalid(rule.Id + ".Steps: count >= 1");
                if (rule.Trigger == "axis" && string.IsNullOrWhiteSpace(rule.Axis)) throw Invalid(rule.Id + ".Axis: FKey");
                if (rule.Trigger != "custom" && rule.Trigger != "sequence" && rule.Keys.Count == 0 && string.IsNullOrWhiteSpace(rule.Axis))
                    throw Invalid(rule.Id + ".Keys / Axis: FKey");
                if (!Finite(rule.Seconds) || rule.Seconds < 0 || !Finite(rule.Interval) || rule.Interval <= 0 || !Finite(rule.Threshold) || rule.Threshold < 0 || rule.Count < 1)
                    throw Invalid(rule.Id + ": Seconds >= 0, Interval > 0, Threshold >= 0, Count >= 1");
                var outputs = new List<object>();
                var devices = rule.Keys.Concat(string.IsNullOrEmpty(rule.Axis) ? new string[0] : new[] { rule.Axis })
                    .Select(key => key.StartsWith("Gamepad_", StringComparison.Ordinal) || key.StartsWith("PBZ_Touchpad_", StringComparison.Ordinal) ? "gamepad" : "keyboard").Distinct().ToArray();
                foreach (var output in rule.Outputs)
                {
                    string name = "output_" + targets.Count.ToString(CultureInfo.InvariantCulture);
                    targets.Add(name, Target(output, Enabled, devices.Length == 1 ? devices[0] : null));
                    var reference = new Dictionary<string, object> { { "target", name } };
                    if ((output.Kind == "action" || output.Kind == "mapping") && output.Value != null) reference["value"] = output.Value;
                    outputs.Add(reference);
                }
                var binding = new Dictionary<string, object> {
                    { "id", rule.Id }, { "keys", rule.Keys }, { "trigger", rule.Trigger == "custom" ? "down" : rule.Trigger },
                    { "seconds", rule.Seconds }, { "interval", rule.Interval }, { "threshold", rule.Threshold },
                    { "count", rule.Count }, { "priority", rule.Priority }, { "outputs", outputs },
                    { "consume", rule.ConsumeKeys.Count > 0 ? (object)rule.ConsumeKeys : rule.Consume }
                };
                if (!string.IsNullOrEmpty(rule.InputGroup)) binding["input_group"] = rule.InputGroup;
                if (rule.BypassResources) binding["bypass_resources"] = true;
                if (!Finite(rule.DamageMultiplier) || rule.DamageMultiplier <= 0 || rule.DamageMultiplier > 100
                    || !Finite(rule.StartupSpeed) || rule.StartupSpeed < .1 || rule.StartupSpeed > 10
                    || !Finite(rule.RecoverySpeed) || rule.RecoverySpeed < .1 || rule.RecoverySpeed > 10)
                    throw Invalid(rule.Id + ": DamageMultiplier > 0 and <= 100; StartupSpeed, RecoverySpeed: 0.1..10");
                if ((rule.StartupSpeed != 1 || rule.RecoverySpeed != 1)
                    && rule.Outputs.All(output => output.Kind == "move" && !CompleteMoveCatalog.SupportsPhaseSpeed(output.Tag)))
                    throw Invalid(rule.Id + ": StartupSpeed, RecoverySpeed: 1 (" + string.Join(", ", rule.Outputs.Select(output => output.Tag)) + ")");
                if (rule.DamageMultiplier != 1) binding["damage_multiplier"] = rule.DamageMultiplier;
                if (rule.StartupSpeed != 1) binding["startup_speed"] = rule.StartupSpeed;
                if (rule.RecoverySpeed != 1) binding["recovery_speed"] = rule.RecoverySpeed;
                binding["axis_direction"] = rule.AxisDirection;
                if (!string.IsNullOrWhiteSpace(rule.Axis)) binding["axis"] = rule.Axis;
                if (rule.Watch.Count > 0) binding["watch"] = rule.Watch;
                if (rule.Steps.Count > 0) binding["steps"] = rule.Steps;
                if (!string.IsNullOrWhiteSpace(rule.WhenLua)) binding["when"] = new LuaExpression(rule.WhenLua);
                if (!string.IsNullOrWhiteSpace(rule.EvaluateLua)) binding["evaluate"] = new LuaExpression(rule.EvaluateLua);
                bindings.Add(binding);
                foreach (var item in rule.Suppressions) suppressions.Add(Suppression(item));
            }
            var blocked = Suppressions.Concat(Rules.Where(rule => rule.Enabled || rule.BlockOriginalWhenDisabled).SelectMany(rule => rule.Suppressions)).ToArray();
            foreach (var item in NativeBindings)
            {
                var actions = KeyEnhancementCatalog.ForMapping(item.FunctionId).Select(target => target.Path).ToArray();
                foreach (string key in item.Keys)
                {
                    var output = new KeyEnhancementOutput { Kind = "mapping", MappingName = item.FunctionId, Device = item.Device,
                        MappingActions = actions.Where(path => !blocked.Any(suppression =>
                            (string.IsNullOrWhiteSpace(suppression.Key) || suppression.Key == key)
                            && System.Text.RegularExpressions.Regex.Replace(suppression.ActionPath, "\\.[^./]+$", "") == System.Text.RegularExpressions.Regex.Replace(path, "\\.[^./]+$", ""))).ToArray() };
                    if (!KeyEnhancementInputPlan.SupportsMapping(output)) continue;
                    string name = "native_override_" + targets.Count;
                    var target = (Dictionary<string, object>)Target(output, true);
                    target["native_override"] = true;
                    targets.Add(name, target);
                    bindings.Add(new Dictionary<string, object> { {"id", name + ":" + key}, {"keys", new[] {key}}, {"trigger", "down"}, {"outputs", new[] {name}}, {"priority", -1000000} });
                }
            }
            var config = new Dictionary<string, object> {
                { "enabled", Enabled }, { "runtime_hotkeys", RuntimeHotkeys }, { "dynamic_capture", true }, { "require_foreground", true },
                { "targets", targets }, { "bindings", bindings }, { "suppressions", suppressions }, { "hooks", Hooks },
                { "ability_suppressions", NativeAbilitySuppressions }
            };
            config["native_bindings"] = NativeBindings.Select(item => new Dictionary<string, object> {
                {"function", item.FunctionId}, {"device", item.Device}, {"keys", item.Keys},
                {"fixed_keys", InputDefaultSet.Current.FixedForAction(item.FunctionId, item.Device == "gamepad")},
                {"actions", KeyEnhancementCatalog.ForMapping(item.FunctionId).Select(target => target.Path).ToArray()}
            }).ToArray();
            config["native_actions"] = KeyEnhancementInputPlan.Actions.Select(action => action.path).ToArray();
            if (Rules.Any(rule => rule.Enabled && (rule.BypassResources || rule.DamageMultiplier != 1 || rule.StartupSpeed != 1 || rule.RecoverySpeed != 1))) config["resource_inputs"] = ResourceCostCatalog.InputTags;
            config["unified_native"] = true;
            return "local config = " + Lua(config) + "\n" + (CustomLua ?? "") + "\nreturn config\n";
        }

        void ValidateStructure()
        {
            if (NativeBindings == null || NativeBindings.Any(item => item == null || item.Keys == null || item.Keys.Any(string.IsNullOrWhiteSpace)
                || !new[] { "keyboard", "gamepad" }.Contains(item.Device) || KeyEnhancementCatalog.ForMapping(item.FunctionId).Length == 0)
                || NativeBindings.GroupBy(item => item.FunctionId + ":" + item.Device).Any(group => group.Count() > 1)) throw Invalid("NativeBindings");
            if (CustomFunctions == null || CustomFunctions.Any(output => output == null || string.IsNullOrWhiteSpace(output.Id))
                || CustomFunctions.GroupBy(output => output.Id).Any(group => group.Count() > 1)) throw Invalid("CustomFunctions[].Id");
            if (Rules == null || Suppressions == null || Hooks == null || NativeAbilitySuppressions == null) throw Invalid("Rules, Suppressions, Hooks, NativeAbilitySuppressions: []");
            if (NativeAbilitySuppressions.Any(string.IsNullOrWhiteSpace)) throw Invalid("NativeAbilitySuppressions[]: GameplayTag");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in Rules)
            {
                if (rule == null) throw Invalid("Rules[]: null");
                if (!new[] { "both", "positive", "negative" }.Contains(rule.AxisDirection) || rule.Axis != null && rule.Axis.EndsWith("2D", StringComparison.Ordinal) && rule.AxisDirection != "both") throw Invalid(rule.Id + ".AxisDirection");
                if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) throw Invalid("Rules[].Id: " + (rule.Id ?? "null"));
                if (rule.Keys == null || rule.Steps == null || rule.Outputs == null || rule.Suppressions == null || rule.ConsumeKeys == null || rule.Watch == null)
                    throw Invalid(rule.Id + ": Keys, Steps, Outputs, Suppressions, ConsumeKeys, Watch: []");
                if (rule.Outputs.Any(item => item == null) || rule.Suppressions.Any(item => item == null) || rule.Steps.Any(item => item == null))
                    throw Invalid(rule.Id + ": Outputs[] / Suppressions[] / Steps[]: null");
            }
            if (Suppressions.Any(item => item == null) || Hooks.Any(item => item == null)) throw Invalid("Suppressions[] / Hooks[]: null");
            var hookPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hook in Hooks)
            {
                object path;
                if (!hook.TryGetValue("path", out path) || string.IsNullOrWhiteSpace(Convert.ToString(path)))
                    throw Invalid("Hooks[].path: UFunction");
                if (!hookPaths.Add(Convert.ToString(path))) throw Invalid(I18n.Str("unified.advanced.duplicatehook"));
            }
        }

        static InvalidDataException Invalid(string detail) { return new InvalidDataException(I18n.Str("enhancement.invalid", detail)); }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static object Suppression(KeyEnhancementSuppression item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ActionPath)) throw Invalid("Suppression.ActionPath: InputAction");
            var result = new Dictionary<string, object> { { "action", item.ActionPath } };
            if (!string.IsNullOrWhiteSpace(item.Key)) result["key"] = item.Key;
            return result;
        }
        static object Target(KeyEnhancementOutput output, bool requireScheduledMapping, string device = null)
        {
            if (output.Kind == "move") return CompleteMoveCatalog.Target(output);
            if (!new[] { "action", "mapping", "ability", "event", "function", "property", "lua", "scheme" }.Contains(output.Kind))
                throw Invalid("Output.Kind=" + output.Kind);
            if ((output.Kind == "action" || output.Kind == "function") && string.IsNullOrWhiteSpace(output.Path)) throw Invalid("Output.Path: InputAction / UFunction");
            if ((output.Kind == "ability" || output.Kind == "event") && string.IsNullOrWhiteSpace(output.Tag)) throw Invalid("Output.Tag: GameplayTag");
            if (output.Kind == "property" && string.IsNullOrWhiteSpace(output.Property)) throw Invalid("Output.Property: FName");
            if (output.Kind == "lua" && string.IsNullOrWhiteSpace(output.Code)) throw Invalid("Output.Code: function");
            var result = new Dictionary<string, object> { { "kind", output.Kind } };
            if (output.Kind == "scheme") result["scheme"] = output.Tag;
            if (output.Kind == "mapping")
            {
                if (string.IsNullOrWhiteSpace(output.MappingName) || output.MappingActions == null || output.MappingActions.Length == 0)
                    throw Invalid("Output.MappingName / MappingActions");
                if (requireScheduledMapping) KeyEnhancementInputPlan.ValidateMapping(output);
                result["mapping"] = output.MappingName;
                result["actions"] = output.MappingActions;
                result["device"] = output.Device;
                result["fixed_keys"] = InputDefaultSet.Current.FixedForAction(output.MappingName, output.Device == "gamepad");
            }
            if (output.Path != null) result["path"] = output.Path;
            if (output.Tag != null) result["tag"] = output.Tag;
            if (output.Receiver != null) result["receiver"] = output.Receiver;
            if (output.Kind == "action")
            {
                if ((output.NativePipeline ?? (string.IsNullOrEmpty(output.Id) && KeyEnhancementInputPlan.HasPipeline(output.Path)))
                    && (output.Modifiers == null || output.Modifiers.Count == 0)
                    && (output.Triggers == null || output.Triggers.Count == 0))
                {
                    result["kind"] = "mapping";
                    result["mapping"] = output.Path;
                    result["actions"] = new[] { output.Path };
                    result["device"] = output.Device ?? device;
                    result["catalog_pipeline"] = true;
                }
                if (output.ValueType != null) result["value_type"] = output.ValueType;
                if (output.Clamp.HasValue) result["clamp"] = output.Clamp.Value;
                if (output.Modifiers != null && output.Modifiers.Count > 0) result["modifiers"] = output.Modifiers;
                if (output.Triggers != null && output.Triggers.Count > 0) result["triggers"] = output.Triggers;
            }
            if (output.Kind == "ability")
            {
                result["priority"] = output.Priority; result["force"] = output.Force;
                string input = output.InputAction ?? ResourceCostCatalog.InputForTag(output.Tag);
                if (input != null) result["input_action"] = input;
            }
            if (output.Kind == "function") result["args"] = output.Args ?? new object[0];
            if (output.Kind == "event" && output.Payload != null) result["payload"] = output.Payload;
            if (output.Kind == "property") { result["property"] = output.Property; result["value"] = output.Value; }
            if (output.Kind == "lua") result["run"] = new LuaExpression(output.Code);
            return result;
        }

        sealed class LuaExpression
        {
            public readonly string Text;
            public LuaExpression(string text) { Text = text; }
        }
        static string Lua(object value)
        {
            if (value == null) return "nil";
            var expression = value as LuaExpression;
            if (expression != null) return "(" + expression.Text + ")";
            var text = value as string;
            if (text != null) return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\0", "\\000") + "\"";
            if (value is bool) return (bool)value ? "true" : "false";
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                var entries = new List<string>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    object item = entry.Value;
                    string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                    if ((key == "evaluate" || key == "when" || key == "run") && item is string && ((string)item).TrimStart().StartsWith("function", StringComparison.Ordinal)) item = new LuaExpression((string)item);
                    entries.Add("[" + Lua(key) + "]=" + Lua(item));
                }
                return "{" + string.Join(",", entries) + "}";
            }
            var sequence = value as IEnumerable;
            if (sequence != null) return "{" + string.Join(",", sequence.Cast<object>().Select(Lua)) + "}";
            if (value is double && !Finite((double)value) || value is float && (float.IsNaN((float)value) || float.IsInfinity((float)value))) throw Invalid("Lua: NaN / Infinity");
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    public sealed class KeyEnhancementCatalogEntry
    {
        public string Id, Kind, Path, Tag, Name, ValueType;
        public string[] MappingNames = new string[0];
        public override string ToString() { return Name; }
    }

    public static class KeyEnhancementCatalog
    {
        static KeyEnhancementCatalogEntry[] targets;
        public static KeyEnhancementCatalogEntry[] Targets { get { return targets ?? (targets = Load()); } }
        public static KeyEnhancementCatalogEntry[] ForMapping(string mappingId)
        { return Targets.Where(entry => entry.Kind == "action" && entry.MappingNames.Contains(mappingId)).ToArray(); }
        static KeyEnhancementCatalogEntry[] Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.KeyEnhancement.actions.json"))
            {
                if (stream == null) throw new InvalidDataException(I18n.Str("enhancement.invalid", "PBZKeyForge.KeyEnhancement.actions.json"));
                using (var reader = new StreamReader(stream))
                {
                    var data = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    var result = new List<KeyEnhancementCatalogEntry>();
                    foreach (Dictionary<string, object> entry in (IEnumerable)data["inputActions"])
                    {
                        string path = (string)entry["path"];
                        result.Add(new KeyEnhancementCatalogEntry { Id = "action:" + path, Kind = "action", Path = path, Name = path.Substring(path.LastIndexOf('/') + 1), ValueType = entry.ContainsKey("value_type") ? (string)entry["value_type"] : null,
                            MappingNames = entry.ContainsKey("mappingNames") ? ((IEnumerable)entry["mappingNames"]).Cast<string>().ToArray() : new string[0] });
                    }
                    foreach (Dictionary<string, object> entry in (IEnumerable)data["abilities"])
                    {
                        string tag = (string)entry["tag"];
                        result.Add(new KeyEnhancementCatalogEntry { Id = "ability:" + tag, Kind = "ability", Tag = tag, Name = tag });
                    }
                    return result.Concat(CompleteMoveCatalog.Targets).ToArray();
                }
            }
        }
    }
}
