using System;
using System.Linq;

namespace PBZKeyForge
{
    public static class BindingLivePlan
    {
        public static KeyEnhancementConfiguration RestoreBindings(EditorSession session, KeyEnhancementConfiguration configuration, int familyIndex)
        {
            var candidate = configuration.Clone();
            candidate.NativeBindings.Clear(); candidate.Rules.Clear(); candidate.Suppressions.Clear(); candidate.NativeAbilitySuppressions.Clear();
            foreach (bool pad in new[] { false, true })
            {
                int family = pad ? familyIndex : -1;
                if (pad && !session.ControllerFamilies.Any(item => item.Index == family)) continue;
                foreach (string action in session.BindingActions(pad, family))
                    candidate.NativeBindings.Add(new NativeBindingOverride { FunctionId = action, Device = pad ? "gamepad" : "keyboard",
                        Keys = session.Relations(action, pad, family).DefaultKeys.ToList() });
            }
            candidate.CompileLua(); return candidate;
        }

        public static KeyEnhancementConfiguration Create(EditorSession session, KeyEnhancementConfiguration configuration, int familyIndex)
        {
            if (session.ActivationPending || session.SelectedProfile != session.ActiveProfile || session.SchemeError != null)
                throw new InvalidOperationException(I18n.Str("live.scope"));
            var result = configuration.Clone();
            foreach (bool pad in new[] { false, true })
            {
                int family = pad ? familyIndex : -1;
                if (pad && !session.ControllerFamilies.Any(item => item.Index == family)) continue;
                foreach (string action in session.BindingActions(pad, family))
                {
                    var current = session.Relations(action, pad, family); var before = session.RelationsBeforeEdit(action, pad, family);
                    if (current.Keys.OrderBy(key => key).SequenceEqual(before.Keys.OrderBy(key => key))) continue;
                    string device = pad ? "gamepad" : "keyboard";
                    if (result.NativeBindings.Any(item => item.FunctionId == action && item.Device == device)) continue;
                    result.NativeBindings.Add(new NativeBindingOverride { FunctionId = action, Device = device, Keys = current.Keys.ToList() });
                }
            }
            result.CompileLua(); return result;
        }
    }
}
