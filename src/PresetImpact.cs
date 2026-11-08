using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed class PresetImpactChange
    {
        public string Kind, Label, Device, Before, After;
    }

    public sealed class PresetImpact
    {
        public List<PresetImpactChange> Changes = new List<PresetImpactChange>();
        public List<string> Effects = new List<string>();
        public string Scope;
        internal Action Accept;

        internal static PresetImpact Create(EditorSession currentSession, KeyEnhancementConfiguration current, BindingPresetPlan plan)
        {
            var result = new PresetImpact();
            var before = current.Clone(); var after = plan.Configuration.Clone();
            CompareDevice(result, currentSession, before, plan.Session, after, false, -1, -1, I18n.Str("editor.keyboard"));
            var beforeFamilies = currentSession.ControllerFamilies;
            var afterFamilies = plan.Session.ControllerFamilies;
            foreach (string name in beforeFamilies.Select(item => item.Name).Union(afterFamilies.Select(item => item.Name)))
            {
                var oldFamily = beforeFamilies.FirstOrDefault(item => item.Name == name);
                var newFamily = afterFamilies.FirstOrDefault(item => item.Name == name);
                CompareDevice(result, currentSession, before, plan.Session, after, true,
                    oldFamily == null ? -1 : oldFamily.Index, newFamily == null ? -1 : newFamily.Index,
                    I18n.Str("editor.gamepad") + " · " + name);
            }
            CompareEffects(result, before, after);
            var scheme = after.Schemes.FirstOrDefault(item => item.Id == after.SelectedSchemeId)
                ?? new KeyEnhancementScheme { Id = currentSession.SelectedProfile, Name = currentSession.SelectedProfile, BuiltIn = true };
            string schemeName = KeyEnhancementConfiguration.SchemeName(scheme);
            var devices = result.Changes.Select(item => item.Device).Distinct().ToArray();
            result.Scope = schemeName + (devices.Length == 0 ? "" : " · " + string.Join(" / ", devices));
            return result;
        }

        static void CompareDevice(PresetImpact result, EditorSession oldSession, KeyEnhancementConfiguration before,
            EditorSession newSession, KeyEnhancementConfiguration after, bool pad, int oldFamily, int newFamily, string device)
        {
            var oldEntries = pad && oldFamily < 0 ? new BindingEntry[0]
                : new BindingWorkspace(oldSession, before, before, pad, oldFamily).GetSnapshot();
            var newEntries = pad && newFamily < 0 ? new BindingEntry[0]
                : new BindingWorkspace(newSession, after, after, pad, newFamily).GetSnapshot();
            var oldGroups = oldEntries.ToLookup(item => item.FunctionId);
            var newGroups = newEntries.ToLookup(item => item.FunctionId);
            foreach (string function in oldGroups.Select(group => group.Key).Union(newGroups.Select(group => group.Key)))
            {
                var oldValues = oldGroups[function].ToArray(); var newValues = newGroups[function].ToArray();
                var oldSignatures = oldValues.Select(Signature).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                var newSignatures = newValues.Select(Signature).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                if (oldSignatures.SequenceEqual(newSignatures)) continue;
                string oldText = Describe(oldValues), newText = Describe(newValues);
                if (oldText == newText)
                    result.Effects.Add(I18n.Str("preset.review.conditions", device + " · " + (newValues.FirstOrDefault() ?? oldValues[0]).Label));
                result.Changes.Add(new PresetImpactChange {
                    Kind = !oldSignatures.Except(newSignatures).Any() && oldSignatures.Length < newSignatures.Length ? "add"
                        : !newSignatures.Except(oldSignatures).Any() && newSignatures.Length < oldSignatures.Length ? "remove" : "change",
                    Label = (newValues.FirstOrDefault() ?? oldValues[0]).Label, Device = device, Before = oldText, After = newText
                });
            }
        }

        static string Serialize(object value)
        { return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Serialize(value); }

        static string Signature(BindingEntry entry)
        {
            if (entry.Rule == null)
                return Serialize(new { entry.Keys, entry.Trigger, entry.Active, entry.Enabled, entry.Suppressed, entry.OutputValue });
            var rule = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<KeyEnhancementRule>(Serialize(entry.Rule));
            rule.Id = null; rule.InputGroup = null;
            rule.Outputs = new[] { rule.Outputs[entry.OutputIndex] }.ToList();
            return Serialize(new { rule, entry.Active });
        }

        static string Describe(IEnumerable<BindingEntry> entries)
        {
            var descriptions = entries.Select(DescribeEntry).ToArray();
            return descriptions.Length == 0 ? "—" : string.Join(Environment.NewLine, descriptions);
        }

        static string DescribeEntry(BindingEntry entry)
        {
            var parts = new List<string> { BindingEntryText.EntryDescription(entry) };
            if (entry.BypassResources) parts.Add(I18n.Str("binding.resources.enabled"));
            if (entry.BypassRestrictions) parts.Add(I18n.Str("binding.restrictions.enabled",
                string.Join(" / ", entry.RestrictionBypasses.Select(kind => I18n.Str("binding.restrictions." + kind)))));
            if (entry.SuperArmor) parts.Add(I18n.Str("binding.effects.superArmor"));
            if (entry.Invincible) parts.Add(I18n.Str("binding.effects.invincible"));
            if (entry.DamageMultiplier != 1) parts.Add(I18n.Str("binding.tuning.damage") + " ×" + Number(entry.DamageMultiplier));
            if (entry.StartupSpeed != 1) parts.Add(I18n.Str("binding.tuning.startup") + " ×" + Number(entry.StartupSpeed));
            if (entry.RecoverySpeed != 1) parts.Add(I18n.Str("binding.tuning.recovery") + " ×" + Number(entry.RecoverySpeed));
            if (entry.OutputValue != null) parts.Add(I18n.Str("unified.advanced.value") + ": " + Serialize(entry.OutputValue));
            var rule = entry.Rule;
            if (rule != null)
            {
                if (rule.Trigger == "axis")
                {
                    parts.Add(I18n.Str("unified.threshold") + ": " + Number(rule.Threshold));
                    parts.Add(I18n.Str("editing.direction") + ": " + I18n.Str("editing." + rule.AxisDirection));
                }
                if (!string.IsNullOrWhiteSpace(rule.WhenLua)) parts.Add(I18n.Str("ruleeditor.when") + ": " + rule.WhenLua);
                if (!string.IsNullOrWhiteSpace(rule.EvaluateLua)) parts.Add(I18n.Str("ruleeditor.evaluate") + ": " + rule.EvaluateLua);
                if (rule.Watch.Count > 0) parts.Add(I18n.Str("unified.watchKeys") + ": " + string.Join(" + ", rule.Watch.Select(BindingButton.FriendlyKey)));
                var output = rule.Outputs[entry.OutputIndex];
                if (entry.OutputValue == null && output.Value != null) parts.Add(I18n.Str("unified.advanced.value") + ": " + Serialize(output.Value));
                if (output.Args != null || output.Payload != null) parts.Add(I18n.Str("unified.advanced.arguments") + ": " + Serialize(output.Payload ?? output.Args));
                if (!string.IsNullOrWhiteSpace(output.Code)) parts.Add(I18n.Str("unified.advanced.script") + ": " + output.Code);
            }
            return string.Join(" · ", parts);
        }

        static string Number(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        static string State(bool value) { return I18n.Str(value ? "mod.enabled" : "mod.disabled"); }

        static void CompareEffects(PresetImpact result, KeyEnhancementConfiguration before, KeyEnhancementConfiguration after)
        {
            if (before.Enabled != after.Enabled)
                result.Effects.Add(GlobalEffect(I18n.Str("preset.review.enhancement", State(before.Enabled), State(after.Enabled))));
            if (before.RuntimeHotkeys != after.RuntimeHotkeys)
                result.Effects.Add(GlobalEffect(I18n.Str("preset.review.hotkeys", State(before.RuntimeHotkeys), State(after.RuntimeHotkeys))));
            if (before.CustomLua != after.CustomLua && !(string.IsNullOrWhiteSpace(before.CustomLua) && string.IsNullOrWhiteSpace(after.CustomLua)))
                result.Effects.Add(GlobalEffect(I18n.Str("preset.review.lua", I18n.Str("preset.review." +
                    (string.IsNullOrWhiteSpace(before.CustomLua) ? "add" : string.IsNullOrWhiteSpace(after.CustomLua) ? "remove" : "change"))))
                    + EffectValues(before.CustomLua, after.CustomLua));
            foreach (string id in before.CustomFunctions.Select(CustomIdentity).Union(after.CustomFunctions.Select(CustomIdentity)))
            {
                var oldValue = before.CustomFunctions.FirstOrDefault(item => CustomIdentity(item) == id);
                var newValue = after.CustomFunctions.FirstOrDefault(item => CustomIdentity(item) == id);
                if (Serialize(oldValue) == Serialize(newValue)) continue;
                result.Effects.Add(GlobalEffect(I18n.Str("preset.review.custom", I18n.Str("preset.review." + (oldValue == null ? "add" : newValue == null ? "remove" : "change"))
                    + " · " + ((newValue ?? oldValue).Name ?? id))) + EffectValues(oldValue == null ? null : Serialize(oldValue), newValue == null ? null : Serialize(newValue)));
            }
            if (Serialize(before.Hooks) != Serialize(after.Hooks))
            {
                string oldHooks = Hooks(before), newHooks = Hooks(after);
                if (oldHooks == newHooks) newHooks += " · " + I18n.Str("preset.review.updated");
                result.Effects.Add(GlobalEffect(I18n.Str("preset.review.hooks", oldHooks, newHooks)) + EffectValues(Serialize(before.Hooks), Serialize(after.Hooks)));
            }
            string oldSuppressions = Suppressions(before), newSuppressions = Suppressions(after);
            if (oldSuppressions != newSuppressions)
                result.Effects.Add(I18n.Str("preset.review.suppressions", oldSuppressions, newSuppressions));
        }

        static string GlobalEffect(string text) { return text + " · " + I18n.Str("binding.scope.global"); }

        static string EffectValues(string before, string after)
        {
            return Environment.NewLine + I18n.Str("preset.review.before") + ": " + (string.IsNullOrWhiteSpace(before) ? "—" : before)
                + Environment.NewLine + I18n.Str("preset.review.after") + ": " + (string.IsNullOrWhiteSpace(after) ? "—" : after);
        }

        static string CustomIdentity(KeyEnhancementOutput output)
        { return output.Id ?? output.FunctionId ?? output.Path ?? output.Tag ?? output.Property ?? output.Code; }

        static string Hooks(KeyEnhancementConfiguration config)
        { return config.Hooks.Count == 0 ? "—" : string.Join(" / ", config.Hooks.Select(item => Convert.ToString(item["path"]))); }

        static string Suppressions(KeyEnhancementConfiguration config)
        {
            var items = config.Suppressions.Concat(config.Rules.Where(rule => rule.Enabled || rule.BlockOriginalWhenDisabled).SelectMany(rule => rule.Suppressions))
                .Select(item => BindingWorkspace.FunctionLabel(string.IsNullOrEmpty(item.FunctionId) ? "action:" + item.ActionPath : item.FunctionId)
                    + (string.IsNullOrEmpty(item.Key) ? "" : " · " + BindingButton.FriendlyKey(item.Key)))
                .Concat(config.NativeAbilitySuppressions.Select(tag => BindingWorkspace.FunctionLabel("ability:" + tag)))
                .Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return items.Length == 0 ? "—" : string.Join(" / ", items);
        }
    }
}
