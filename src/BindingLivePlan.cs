using System;
using System.Linq;

namespace PBZKeyForge
{
    internal sealed class BindingDraftConflictException : InvalidOperationException
    {
        internal BindingDraftConflictException(string action) : base(I18n.Str("live.rawconflict", I18n.Str("act." + action))) { }
    }

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
            if (configuration.Schemes.Count > 0)
            {
                if (session.HasAnyDrafts) throw new InvalidOperationException(I18n.Str("schemes.nativepending"));
                var candidate = configuration.Clone(); candidate.ActiveSchemeId = candidate.SelectedSchemeId;
                candidate.CompileLua(); return candidate;
            }
            if (session.ActivationPending || session.SelectedProfile != session.ActiveProfile || session.SchemeError != null)
                throw new InvalidOperationException(I18n.Str("live.scope"));
            if (session.Conflicts.Length > 0) throw new InvalidOperationException(I18n.Str("editor.conflictinfo"));
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
                    var existing = result.NativeBindings.FirstOrDefault(item => item.FunctionId == action && item.Device == device);
                    if (existing != null)
                    {
                        if (session.HasIndependentBindingDraft(action, pad, family)
                            && !current.Keys.OrderBy(key => key).SequenceEqual(existing.Keys.OrderBy(key => key)))
                            throw new BindingDraftConflictException(action);
                        continue;
                    }
                    result.NativeBindings.Add(new NativeBindingOverride { FunctionId = action, Device = device, Keys = current.Keys.ToList() });
                }
            }
            result.CompileLua(); return result;
        }
    }
}
