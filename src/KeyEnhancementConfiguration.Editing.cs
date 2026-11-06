using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    public sealed class NativeBindingOverride
    {
        public string FunctionId, Device;
        public List<string> Keys = new List<string>();
    }

    public static class EnhancementEditing
    {
        public static bool Pad(string key)
        { return key.StartsWith("Gamepad_", StringComparison.Ordinal) || key.StartsWith("PBZ_Touchpad_", StringComparison.Ordinal); }

        public static KeyEnhancementConfiguration SelectDevices(KeyEnhancementConfiguration source, bool keyboard, bool gamepad)
        {
            var result = source.Clone();
            Func<string, bool> selected = key => Pad(key) ? gamepad : keyboard;
            result.Rules.RemoveAll(rule => !rule.Keys.Concat(rule.Watch).Concat(rule.Steps.SelectMany(step => step))
                .Concat(string.IsNullOrEmpty(rule.Axis) ? new string[0] : new[] { rule.Axis }).All(selected));
            result.NativeBindings.RemoveAll(item => item.Device == "gamepad" ? !gamepad : !keyboard);
            result.Suppressions.RemoveAll(item => !string.IsNullOrEmpty(item.Key) && !selected(item.Key));
            return result;
        }

        public static KeyEnhancementConfiguration Append(KeyEnhancementConfiguration current, KeyEnhancementConfiguration incoming)
        {
            var result = current.Clone(); var added = incoming.Clone();
            result.Enabled |= added.Enabled;
            var inputGroups = new Dictionary<string, string>();
            foreach (var rule in added.Rules)
            {
                var existing = result.Rules.FirstOrDefault(item => item.Id == rule.Id);
                if (existing != null)
                {
                    var a = new KeyEnhancementConfiguration(); a.Rules.Add(existing);
                    var b = new KeyEnhancementConfiguration(); b.Rules.Add(rule);
                    if (a.ToJson() == b.ToJson()) continue;
                    rule.Id = Guid.NewGuid().ToString("N");
                }
                if (!string.IsNullOrEmpty(rule.InputGroup))
                {
                    string group;
                    if (inputGroups.TryGetValue(rule.InputGroup, out group)) rule.InputGroup = group;
                    else if (current.Rules.Any(item => item.InputGroup == rule.InputGroup))
                    { inputGroups[rule.InputGroup] = group = Guid.NewGuid().ToString("N"); rule.InputGroup = group; }
                }
                result.Rules.Add(rule);
            }
            foreach (var target in added.CustomFunctions)
            {
                var existing = result.CustomFunctions.FirstOrDefault(item => item.Id == target.Id);
                if (existing == null) result.CustomFunctions.Add(target);
                else if (new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(existing)
                    != new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(target))
                    throw new InvalidOperationException(I18n.Str("editing.customconflict", target.Name ?? target.Id));
            }
            foreach (var item in added.NativeBindings)
            {
                var existing = result.NativeBindings.FirstOrDefault(value => value.FunctionId == item.FunctionId && value.Device == item.Device);
                if (existing == null) result.NativeBindings.Add(item);
                else existing.Keys = existing.Keys.Concat(item.Keys).Distinct().ToList();
            }
            result.Suppressions.AddRange(added.Suppressions.Where(item => !result.Suppressions.Any(old => old.ActionPath == item.ActionPath && old.Key == item.Key && old.FunctionId == item.FunctionId)));
            result.NativeAbilitySuppressions = result.NativeAbilitySuppressions.Concat(added.NativeAbilitySuppressions).Distinct().ToList();
            foreach (var hook in added.Hooks)
            {
                var old = result.Hooks.FirstOrDefault(item => Convert.ToString(item["path"]) == Convert.ToString(hook["path"]));
                if (old == null) result.Hooks.Add(hook);
                else if (new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(old) != new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(hook))
                    throw new InvalidOperationException(I18n.Str("unified.advanced.duplicatehook"));
            }
            if (!string.IsNullOrWhiteSpace(added.CustomLua) && result.CustomLua != added.CustomLua)
                result.CustomLua = (result.CustomLua ?? "") + "\n" + added.CustomLua;
            result.CompileLua(); return result;
        }

        public static KeyEnhancementConfiguration ReplaceDevices(KeyEnhancementConfiguration current, KeyEnhancementConfiguration incoming, IEnumerable<PresetScope> scopes)
        {
            bool keyboard = scopes.Any(scope => scope.Family == null), gamepad = scopes.Any(scope => scope.Family != null);
            if (keyboard && gamepad) return incoming.Clone();
            var retained = SelectDevices(current, !keyboard, !gamepad);
            retained.Rules.RemoveAll(rule => !rule.Keys.Concat(rule.Watch).Concat(rule.Steps.SelectMany(step => step))
                .Concat(string.IsNullOrEmpty(rule.Axis) ? new string[0] : new[] { rule.Axis }).Any());
            retained.Suppressions.RemoveAll(item => string.IsNullOrEmpty(item.Key));
            retained.CustomFunctions.RemoveAll(item => !retained.Rules.Any(rule => rule.Outputs.Any(target => target.Id == item.Id || target.FunctionId == "custom:" + item.Id)));
            retained.Hooks.Clear(); retained.NativeAbilitySuppressions.Clear(); retained.CustomLua = null;
            retained.Enabled = current.Enabled; retained.RuntimeHotkeys = incoming.RuntimeHotkeys;
            return Append(retained, incoming);
        }
    }
}
