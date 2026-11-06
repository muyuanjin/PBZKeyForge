using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    internal static partial class UnifiedBindingUi
    {
        internal static Func<string, KeyBadgeState> PresetKeyStates(EditorSession snapshot, Preset preset, bool pad, int family, out string[] keys)
        {
            var actions = snapshot.BindingActions(pad, family);
            var relations = actions.Select(action => BindingViewText.PhysicalRelations(snapshot.Relations(action, pad, family))).ToArray();
            keys = relations.SelectMany(relation => relation.Keys.Concat(relation.DefaultKeys ?? new string[0])).Distinct().ToArray();
            if (preset.KeyEnhancement == null)
                return key => new KeyBadgeState { Count = relations.Count(relation => relation.Keys.Contains(key)),
                    Complete = relations.All(relation => relation.KnowsKey(key)),
                    Differs = relations.Any(relation => relation.DefaultKeys != null && relation.Keys.Contains(key) != relation.DefaultKeys.Contains(key)) };
            var workspace = new BindingWorkspace(snapshot, preset.KeyEnhancement, new KeyEnhancementConfiguration(), pad, family);
            var entries = workspace.GetSnapshot();
            var physical = workspace.Snapshot().Current.Select(BindingViewText.PhysicalRelations).ToArray();
            keys = keys.Concat(entries.SelectMany(BindingWorkspace.UsedKeys).Select(BindingViewText.PhysicalKey)).Distinct().ToArray();
            var customKeys = new HashSet<string>(entries.Where(entry => !entry.IsNative).SelectMany(BindingWorkspace.UsedKeys).Select(BindingViewText.PhysicalKey));
            return key => new KeyBadgeState { Count = physical.Count(relation => relation.Keys.Contains(key)),
                Complete = physical.All(relation => relation.KnowsKey(key)),
                Differs = customKeys.Contains(key) || physical.Any(relation => relation.DefaultKeys != null
                    && (relation.Keys.Contains(key) && !relation.DefaultKeys.Contains(key)
                        || relation.Complete && !relation.Keys.Contains(key) && relation.DefaultKeys.Contains(key))) };
        }
    }
}
