using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    internal sealed class BindingPresetPlan
    {
        internal EditorSession Session;
        internal KeyEnhancementConfiguration Configuration;

        internal static BindingPresetPlan Create(EditorSession session, KeyEnhancementConfiguration current, Preset preset, PresetApplyMode mode, int selectedFamily = -1)
        {
            if (current.Enabled || preset.KeyEnhancement != null && preset.KeyEnhancement.Enabled)
            {
                var source = current.Clone(); source.SetEnabled(true);
                source.EnsureSchemes(session);
                var plan = CreateCurrent(session, source.CurrentOnly(), preset, mode, selectedFamily);
                foreach (var original in source.NativeBindings)
                {
                    if (plan.Configuration.NativeBindings.Any(item => item.FunctionId == original.FunctionId && item.Device == original.Device)) continue;
                    bool pad = original.Device == "gamepad";
                    var families = session.ControllerFamilies.Where(item => !preset.IsUser || preset.Scopes.Any(scope => scope.Family == item.Name)).ToArray();
                    var family = families.FirstOrDefault(item => item.Index == selectedFamily) ?? families.FirstOrDefault();
                    bool included = !preset.IsUser || preset.Scopes.Any(scope => (scope.Family != null) == pad);
                    bool covered = preset.CurrentProfileVanilla || (preset.IsUser ? preset.UserMappings.Any(item => item.Action == original.FunctionId && item.Family == (pad && family != null ? family.Name : null))
                        : (pad ? preset.Gamepad : preset.Keyboard).Any(item => item.Action == original.FunctionId) || preset.AdditiveGuard && original.FunctionId == "IA_Guard");
                    string[] keys = included && (covered || mode == PresetApplyMode.OnlyPreset) && (!pad || family != null)
                        ? plan.Session.Relations(original.FunctionId, pad, pad ? family.Index : -1).Keys : original.Keys.ToArray();
                    plan.Configuration.NativeBindings.Add(new NativeBindingOverride { FunctionId = original.FunctionId, Device = original.Device, Keys = keys.ToList() });
                }
                var library = source.Clone(); library.ReplaceCurrent(plan.Configuration);
                return new BindingPresetPlan { Session = session.CopyForPreset(), Configuration = library };
            }
            return CreateCurrent(session, current, preset, mode, selectedFamily);
        }

        static BindingPresetPlan CreateCurrent(EditorSession session, KeyEnhancementConfiguration current, Preset preset, PresetApplyMode mode, int selectedFamily)
        {
            var planned = preset.KeyEnhancement == null ? current.Clone() : mode == PresetApplyMode.Append
                ? EnhancementEditing.Append(current, preset.KeyEnhancement) : EnhancementEditing.ReplaceDevices(current, preset.KeyEnhancement, preset.Scopes);
            var copy = session.CopyForPreset();
            var overflow = planned.Enabled ? Overflow(planned) : null;
            copy.StagePreset(preset, mode, overflow == null ? null : (Action<string, string, string>)delegate(string action, string key, string family) {
                if (!planned.NativeBindings.Any(item => item.FunctionId == action && item.Device == (family == null ? "keyboard" : "gamepad"))) overflow(action, key, family);
            });
            var factory = session.PreviewPreset(preset);
            foreach (bool pad in new[] { false, true })
            {
                bool included = !preset.IsUser || preset.Scopes.Any(scope => (scope.Family != null) == pad);
                if (!included) continue;
                var families = session.ControllerFamilies.Where(item => !preset.IsUser || preset.Scopes.Any(scope => scope.Family == item.Name)).ToArray();
                var selected = families.FirstOrDefault(item => item.Index == selectedFamily) ?? families.FirstOrDefault();
                int family = pad ? selected.Index : -1;
                string familyName = pad ? selected.Name : null;
                string device = pad ? "gamepad" : "keyboard";
                foreach (string action in session.BindingActions(pad, family))
                {
                    var incoming = preset.KeyEnhancement == null ? null : preset.KeyEnhancement.NativeBindings.FirstOrDefault(item => item.FunctionId == action && item.Device == device);
                    var existing = current.NativeBindings.FirstOrDefault(item => item.FunctionId == action && item.Device == device);
                    bool covered = preset.CurrentProfileVanilla || (preset.IsUser ? preset.UserMappings.Any(item => item.Action == action && item.Family == familyName)
                        : (pad ? preset.Gamepad : preset.Keyboard).Any(item => item.Action == action) || preset.AdditiveGuard && action == "IA_Guard");
                    var binding = planned.NativeBindings.FirstOrDefault(value => value.FunctionId == action && value.Device == device);
                    if (incoming != null)
                    {
                        if (mode == PresetApplyMode.Append)
                            binding.Keys = (existing == null ? session.Relations(action, pad, family).Keys : existing.Keys.ToArray()).Concat(incoming.Keys).Distinct().ToList();
                    }
                    else if (binding != null && (covered || mode == PresetApplyMode.OnlyPreset))
                    {
                        var required = factory.Relations(action, pad, family).Keys;
                        binding.Keys = mode == PresetApplyMode.Append ? binding.Keys.Concat(required).Distinct().ToList() : required.ToList();
                    }
                    if (binding != null && (incoming != null || covered || mode == PresetApplyMode.OnlyPreset))
                        copy.PreservePresetOverride(action, pad, family);
                }
            }
            if (current.Schemes.Count > 0 && planned.Schemes.Count == 0)
            {
                var retained = current.Clone(); planned.Schemes = retained.Schemes;
                planned.SelectedSchemeId = retained.SelectedSchemeId; planned.ActiveSchemeId = retained.ActiveSchemeId;
                planned.SchemeSwitchBindings = retained.SchemeSwitchBindings;
            }
            planned.CompileLua();
            return new BindingPresetPlan { Session = copy, Configuration = planned };
        }

        internal static Action<string, string, string> Overflow(KeyEnhancementConfiguration planned)
        {
            return delegate(string action, string key, string family) {
                var output = new KeyEnhancementOutput { Kind = "mapping", MappingName = action,
                    MappingActions = KeyEnhancementCatalog.ForMapping(action).Select(target => target.Path).ToArray(), Device = family == null ? "keyboard" : "gamepad" };
                KeyEnhancementInputPlan.ValidateMapping(output);
                if (planned.Rules.Any(rule => rule.Enabled && rule.Trigger == "down" && rule.Keys.SequenceEqual(new[] { key })
                    && string.IsNullOrWhiteSpace(rule.Axis) && string.IsNullOrWhiteSpace(rule.WhenLua) && string.IsNullOrWhiteSpace(rule.EvaluateLua)
                    && !rule.Consume && rule.ConsumeKeys.Count == 0 && rule.Watch.Count == 0 && rule.Steps.Count == 0
                    && rule.Outputs.Any(item => item.Kind == "mapping" && item.MappingName == action && item.Device == output.Device && item.Value == null))) return;
                var added = new KeyEnhancementRule { Trigger = "down" }; added.Keys.Add(key); added.Outputs.Add(output); planned.Rules.Add(added);
            };
        }

        internal void Apply(EditorSession target)
        { target.ApplyPresetPlan(Session); }
    }
}
