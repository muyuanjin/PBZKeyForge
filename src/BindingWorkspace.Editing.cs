using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed partial class BindingWorkspace
    {
        public string AddCustomDefinition(KeyEnhancementOutput output, string label)
        {
            var candidate = new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(output));
            candidate.Id = Guid.NewGuid().ToString("N"); candidate.Name = label;
            var check = new KeyEnhancementConfiguration(); var rule = new KeyEnhancementRule(); rule.Keys.Add("F9"); rule.Outputs.Add(candidate);
            check.Rules.Add(rule); check.CompileLua(); Changing(); config.CustomFunctions.Add(candidate); functions = null;
            return OutputFunction(candidate);
        }
        void LinkInputVariants(KeyEnhancementRule rule)
        {
            if (rule.Trigger != "tap" && rule.Trigger != "hold") return;
            var variants = RulesFor(config).Where(item => item.Axis == rule.Axis && item.Keys.OrderBy(key => key).SequenceEqual(rule.Keys.OrderBy(key => key))
                && (item.Trigger == "tap" || item.Trigger == "hold")).ToArray();
            if (variants.Length < 2) return;
            string group = variants.Select(item => item.InputGroup).FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? Guid.NewGuid().ToString("N");
            foreach (var item in variants) { item.InputGroup = group; item.Seconds = rule.Seconds; }
        }

        public BindingEntry[] AddFunctions(IEnumerable<string> functionIds, BindingEntry specification)
        {
            var ids = functionIds.Distinct().ToArray();
            if (ids.Length == 0) return new BindingEntry[0];
            if (ids.Length == 1) return new[] { Add(ids[0], specification) };
            CheckInput(specification); Changing();
            var selected = ids.Select(id => Functions.First(function => function.Id == id)).ToArray();
            var rules = new List<KeyEnhancementRule>();
            foreach (var group in selected.GroupBy(function => specification.Trigger == "default" ? DefaultTriggerFor(function) : specification.Trigger))
            {
                var rule = NewRule(group.First(), specification); rule.Outputs.Clear();
                foreach (var function in group)
                {
                    var output = CreateOutput(function); SetOutputValue(output, specification.OutputValue ?? function.DefaultOutputValue);
                    rule.Outputs.Add(output);
                }
                rules.Add(rule);
            }
            var check = config.Clone(); check.Rules.AddRange(rules); check.CompileLua();
            foreach (var rule in rules) { AddRule(rule); LinkInputVariants(rule); }
            if (specification.KeepNativeTrigger.HasValue)
                foreach (var function in selected) SetNativeTrigger(function.Id, specification.KeepNativeTrigger.Value);
            return CollectEntries(config, false, null, true).Where(entry => rules.Any(rule => rule.Id == entry.RuleId)).ToArray();
        }

        public BindingEntry CopyBinding(BindingEntry entry, string targetFunction)
        {
            var copy = new JavaScriptSerializer().Deserialize<BindingEntry>(Serialize(entry));
            copy.KeepNativeTrigger = null;
            return Add(targetFunction, copy);
        }

        public void MoveKey(string from, string to, bool swap)
        {
            if (string.IsNullOrEmpty(to) || to == "None" || from == to) return;
            if (EnhancementEditing.Pad(from) != EnhancementEditing.Pad(to)) throw new ArgumentException(I18n.Str("editing.samedevice"));
            Changing();
            var checkpoint = session.CaptureDraft(); var saved = config.Clone();
            Func<string, string> move = key => key == from ? to : swap && key == to ? from : key;
            Action<IEnumerable<KeyEnhancementSuppression>> moveSuppressions = items => {
                foreach (var item in items.Where(item => !string.IsNullOrEmpty(item.Key)))
                {
                    // Fixed input sources stay on their original physical keys.
                    if (Functions.Any(function => function.FixedKeys != null && function.FixedKeys.Contains(item.Key)
                        && function.Targets.Any(target => PackagePath(target.Path) == PackagePath(item.ActionPath)))) continue;
                    item.Key = move(item.Key);
                }
            };
            try
            {
                foreach (var function in Functions.Where(item => item.NativeAction != null))
                {
                    var native = NativeOverride(function.Id, config);
                    var keys = native != null && config.Enabled ? native.Keys.ToArray() : session.Relations(function.Id, pad, family).Keys;
                    if (!keys.Any(key => move(key) != key)) continue;
                    if (config.Enabled || LiveEditing) EditNative(function.Id).Keys = keys.Select(move).Distinct().ToList();
                    else
                    {
                        var desired = keys.Select(move).Distinct().ToArray();
                        foreach (var key in keys.Except(desired)) session.ChangeRelationship(function.Id, key, false, pad, family);
                        foreach (var key in desired.Except(keys)) session.ChangeRelationship(function.Id, key, true, pad, family);
                    }
                }
                foreach (var rule in RulesFor(config))
                {
                    rule.Keys = rule.Keys.Select(move).Distinct().ToList(); rule.Watch = rule.Watch.Select(move).Distinct().ToList();
                    rule.ConsumeKeys = rule.ConsumeKeys.Select(move).Distinct().ToList();
                    rule.Steps = rule.Steps.Select(step => step.Select(move).Distinct().ToArray()).ToList();
                    if (rule.Axis != null) rule.Axis = move(rule.Axis);
                    moveSuppressions(rule.Suppressions);
                }
                moveSuppressions(SuppressionsFor(config));
                foreach (var function in Functions.Where(item => item.FixedKeys != null))
                    foreach (var key in function.FixedKeys.Where(key => move(key) != key))
                    {
                        var native = Entries(function.Id).First(entry => entry.IsNative && entry.Keys.Contains(key));
                        if (native.Suppressed) continue;
                        Replace(native, new BindingEntry { Keys = new[] { move(key) }, Trigger = "down", OutputValue = function.DefaultOutputValue });
                    }
                config.CompileLua();
            }
            catch { session.RestoreDraft(checkpoint); config.ReplaceWith(saved); throw; }
        }
    }

    public static class EnhancementConfigurationCopy
    {
        public static void ReplaceWith(this KeyEnhancementConfiguration target, KeyEnhancementConfiguration source)
        {
            var copy = source.Clone();
            foreach (var field in typeof(KeyEnhancementConfiguration).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                field.SetValue(target, field.GetValue(copy));
        }
    }
}
