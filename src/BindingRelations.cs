using System;
using System.Collections.Generic;
using System.Linq;

namespace PBZKeyForge
{
    public sealed class BindingRecord
    {
        public string Id, Action, Key, DefaultKey, Slot;
        public int SlotIndex, FamilyIndex;
        public bool Changed, ReadOnly;
    }

    public sealed class ControllerFamily
    {
        public int Index;
        public string Name;
        public override string ToString() { return Name == "PS" ? "PlayStation" : Name; }
    }

    public sealed class ActionRelations
    {
        public string Action;
        public string[] Keys, DefaultKeys;
        public string[] BindingDetails;
        public string BindingSignature;
        public bool CustomBindings;
        public bool Complete, Changed;
        public bool CanCompare { get { return Complete && DefaultKeys != null; } }
        public bool KnowsKey(string key)
        { return Complete || Keys.Contains(key) || DefaultKeys != null && !DefaultKeys.Contains(key); }
        internal bool RelationshipPending(ActionRelations before, string key)
        {
            bool now = Keys.Contains(key), saved = before.Keys.Contains(key);
            return now && !saved && before.KnowsKey(key) || saved && !now && KnowsKey(key);
        }
    }

    public sealed class BindingCapacity
    {
        public int Used, Limit;
        public bool CanAdd;
    }

    public sealed class KeyRelations
    {
        public string Key;
        public ActionRelations[] Current, Added, Removed;
        public int FactoryCount;
        public bool CurrentComplete, FactoryComplete;
        public string CountText { get { return (CurrentComplete ? "" : "≥ ") + Current.Length; } }
        public string FactoryCountText { get { return (FactoryComplete ? "" : "≥ ") + FactoryCount; } }
        public bool Differs { get { return Added.Length > 0 || Removed.Length > 0; } }
    }

    public sealed class BindingDifference
    {
        public string Action, Family;
        public int FamilyIndex;
        public bool Gamepad, BeforeComplete, AfterComplete;
        public string[] BeforeKeys, AfterKeys, DefaultKeys;
        public string[] BeforeBindings, AfterBindings;
        public bool DetailsChanged;
        public string[] AddedKeys { get { return AfterKeys.Except(BeforeKeys).Where(k => BeforeComplete || DefaultKeys != null && !DefaultKeys.Contains(k)).ToArray(); } }
        public string[] RemovedKeys { get { return BeforeKeys.Except(AfterKeys).Where(k => AfterComplete || DefaultKeys != null && !DefaultKeys.Contains(k)).ToArray(); } }
    }

    internal sealed class BindingSnapshot
    {
        public readonly bool Gamepad;
        public readonly int FamilyIndex;
        public readonly string Family;
        public readonly ActionRelations[] Current;
        public readonly Dictionary<string, ActionRelations> Before;

        public BindingSnapshot(bool gamepad, int familyIndex, string family, ActionRelations[] current, Dictionary<string, ActionRelations> before)
        { Gamepad = gamepad; FamilyIndex = familyIndex; Family = family; Current = current; Before = before; }

        public BindingSnapshot(EditorSession session, bool gamepad, int familyIndex, string family)
        {
            Gamepad = gamepad; FamilyIndex = familyIndex; Family = family;
            Current = session.BindingActions(gamepad, familyIndex).Select(a => session.Relations(a, gamepad, familyIndex)).ToArray();
            Before = Current.ToDictionary(r => r.Action, r => session.RelationsBeforeEdit(r.Action, gamepad, familyIndex));
        }

        public BindingDifference[] Differences(bool factory)
        {
            var differences = new List<BindingDifference>();
            foreach (var current in Current)
            {
                var before = Before[current.Action];
                string[] keys = factory ? current.DefaultKeys : before.Keys;
                bool complete = factory ? keys != null : before.Complete;
                if (keys == null) continue;
                bool same = !keys.Except(current.Keys).Any() && !current.Keys.Except(keys).Any();
                bool detailsChanged = current.BindingSignature != null && (factory ? current.CustomBindings : current.BindingSignature != before.BindingSignature);
                if (!detailsChanged && (complete && current.Complete ? same : factory ? !current.Keys.Except(keys).Any() : !current.Changed)) continue;
                differences.Add(new BindingDifference { Action = current.Action, Gamepad = Gamepad, FamilyIndex = FamilyIndex,
                    Family = Family, BeforeKeys = keys, AfterKeys = current.Keys, DefaultKeys = current.DefaultKeys,
                    BeforeBindings = factory ? keys.Select(BindingButton.FriendlyKey).ToArray() : before.BindingDetails, AfterBindings = current.BindingDetails, DetailsChanged = detailsChanged,
                    BeforeComplete = complete, AfterComplete = current.Complete });
            }
            return differences.ToArray();
        }
    }

    public sealed partial class EditorSession
    {
        public ControllerFamily[] ControllerFamilies
        {
            get { return cells.Values.Where(cell => cell.IsGamepad && cell.Profile == SelectedProfile && cell.FamilyName != null)
                .GroupBy(cell => cell.FamilyIndex).Select(group => new ControllerFamily { Index = group.Key, Name = group.First().FamilyName }).ToArray(); }
        }

        public BindingRecord[] BindingRecords(bool gamepad, int familyIndex)
        { return BindingRecords(gamepad, familyIndex, false); }

        BindingRecord[] BindingRecords(bool gamepad, int familyIndex, bool beforeEdit, string action = null)
        {
            Func<Cell, string> value = c => beforeEdit ? Value(cells, c.Id) : Current(c);
            return addresses.Values.Where(c => c.Profile == SelectedProfile && c.IsGamepad == gamepad
                && (!gamepad || c.FamilyIndex == familyIndex) && (action == null || c.Action == action) && value(c) != null)
                .Select(c => new BindingRecord { Id = c.Id, Action = c.Action, Key = value(c), DefaultKey = c.DefaultKey,
                    Slot = c.Slot, SlotIndex = c.SlotIndex, FamilyIndex = c.FamilyIndex, Changed = !beforeEdit && draft.Edits.ContainsKey(c.Id),
                    ReadOnly = Defaults.IsMirroredSlot(c.Action, c.IsGamepad ? InputDefaults.RawSlot(c.Slot) : int.Parse(c.Slot)) })
                .OrderBy(c => c.Action).ThenBy(c => c.Slot).ThenBy(c => c.Id).ToArray();
        }

        public void SetBindingRecord(string id, string key)
        {
            EnsureEditable();
            Cell cell = Address(id);
            if (cell.Profile != SelectedProfile) throw new InvalidOperationException("Select the binding's scheme first.");
            if (Defaults.IsMirroredSlot(cell.Action, cell.IsGamepad ? InputDefaults.RawSlot(cell.Slot) : int.Parse(cell.Slot)))
                throw new InvalidOperationException(I18n.Str("views.readonlyrecord"));
            EndPreview();
            Edit(cell, Normalize(key) ?? (cell.IsGamepad ? cell.DefaultKey ?? Current(cell) : null));
        }

        public void SetGamepadFamily(string action, int slotIndex, int familyIndex, string key)
        {
            EnsureEditable(); CheckSlot(slotIndex);
            var owner = ControllerFamilies.FirstOrDefault(f => f.Index == familyIndex);
            if (owner == null) throw new ArgumentException("Choose a controller family.", "familyIndex");
            if (!HasGamepadSlot(action, slotIndex, familyIndex)) throw new InvalidOperationException(I18n.Str("views.capacity"));
            string slot = InputDefaults.SlotName(PadRawSlots(SelectedProfile, cells.Values.First(c => c.IsGamepad && c.Profile == SelectedProfile && c.FamilyIndex == familyIndex).Family, action)[slotIndex]);
            string defaultKey = Defaults.ForRawSlot(action, true, InputDefaults.RawSlot(slot));
            var targets = addresses.Values.Where(c => c.IsGamepad && c.Profile == SelectedProfile && c.Action == action
                && c.FamilyIndex == familyIndex && c.Slot == slot && c.DefaultKey == defaultKey && Current(c) != null).ToArray();
            if (targets.Length == 0)
                targets = PadAddresses(action, slotIndex, !string.IsNullOrWhiteSpace(key)).Where(c => c.FamilyIndex == familyIndex).ToArray();
            EndPreview();
            foreach (var cell in targets) Edit(cell, Normalize(key) ?? cell.DefaultKey ?? Current(cell));
        }

        public string[] FactoryKeyboardKeys(string action)
        {
            return Defaults.ForAction(action, false);
        }

        Cell KeyboardAddressForRaw(string action, int raw)
        {
            string slot = raw.ToString(), id = KeyboardId(SelectedProfile, action, slot, 0);
            Cell cell;
            if (!addresses.TryGetValue(id, out cell))
            {
                cell = new Cell { Id = id, Profile = SelectedProfile, Action = action, Slot = slot,
                    SlotIndex = Array.IndexOf(KeyboardRawSlots(SelectedProfile, action), raw), FamilyIndex = -1,
                    DefaultKey = Defaults.ForRawSlot(action, false, raw) };
                addresses[id] = cell;
            }
            return cell;
        }

        static string[] DistinctKeys(IEnumerable<string> keys)
        { return keys.Where(k => !string.IsNullOrEmpty(k) && k != "None").Distinct().ToArray(); }

        public ActionRelations Relations(string action, bool gamepad, int familyIndex)
        { return Relations(action, gamepad, familyIndex, false); }

        public ActionRelations RelationsBeforeEdit(string action, bool gamepad, int familyIndex)
        { return Relations(action, gamepad, familyIndex, true); }

        BindingRecord[] ActionBindingRecords(string action, bool gamepad, int familyIndex, bool beforeEdit)
        {
            return BindingRecords(gamepad, familyIndex, beforeEdit, action).Where(r => !r.ReadOnly).Where(r => !gamepad || !Defaults.HasAction(action)
                || Defaults.EditableRegisteredSlots(action, true).Contains(InputDefaults.RawSlot(r.Slot))
                    && r.DefaultKey == Defaults.ForRawSlot(action, true, InputDefaults.RawSlot(r.Slot))).ToArray();
        }

        ActionRelations Relations(string action, bool gamepad, int familyIndex, bool beforeEdit)
        {
            var records = ActionBindingRecords(action, gamepad, familyIndex, beforeEdit);
            string[] defaults;
            bool complete;
            if (gamepad)
            {
                bool known = Defaults.HasAction(action);
                defaults = known ? Defaults.ForAction(action, true)
                    : records.Any(r => r.DefaultKey == null) ? null : DistinctKeys(records.Select(r => r.DefaultKey));
                complete = known || records.Length > 0;
            }
            else
            {
                defaults = FactoryKeyboardKeys(action);
                complete = defaults != null;
            }
            string[] keys = Defaults.ResolveKeys(action, gamepad, records.Select(record =>
                new KeyValuePair<int, string>(gamepad ? InputDefaults.RawSlot(record.Slot) : int.Parse(record.Slot), record.Key)));
            return new ActionRelations { Action = action, Keys = keys, DefaultKeys = defaults, Complete = complete,
                Changed = !beforeEdit && (records.Any(r => r.Changed) || draft.Edits.Keys.Any(id => Address(id).Profile == SelectedProfile
                    && Address(id).Action == action && Address(id).IsGamepad == gamepad && (!gamepad || Address(id).FamilyIndex == familyIndex))) };
        }

        public string[] BindingActions(bool gamepad, int familyIndex)
        {
            return MainForm.Actions.Select(a => a.Id).Union(BindingRecords(gamepad, familyIndex).Select(r => r.Action))
                .Union(BindingRecords(gamepad, familyIndex, true).Select(r => r.Action)).ToArray();
        }

        public bool RelationshipPending(string action, string key, bool gamepad, int familyIndex)
        {
            var current = Relations(action, gamepad, familyIndex);
            var before = RelationsBeforeEdit(action, gamepad, familyIndex);
            return current.RelationshipPending(before, key);
        }

        internal BindingSnapshot[] BindingSnapshots()
        {
            return new[] { new BindingSnapshot(this, false, -1, null) }.Concat(ControllerFamilies.Select(f =>
                new BindingSnapshot(this, true, f.Index, f.Name))).ToArray();
        }

        public BindingDifference[] SchemeDifferences(bool factory)
        { return BindingSnapshots().SelectMany(snapshot => snapshot.Differences(factory)).ToArray(); }

        public KeyRelations RelationsForKey(string key, bool gamepad, int familyIndex)
        {
            var relations = BindingActions(gamepad, familyIndex).Select(a => Relations(a, gamepad, familyIndex)).ToArray();
            return new KeyRelations { Key = key, Current = relations.Where(r => r.Keys.Contains(key)).ToArray(),
                Added = relations.Where(r => r.DefaultKeys != null && r.Keys.Contains(key) && !r.DefaultKeys.Contains(key)).ToArray(),
                Removed = relations.Where(r => r.CanCompare && !r.Keys.Contains(key) && r.DefaultKeys.Contains(key)).ToArray(),
                FactoryCount = relations.Count(r => r.DefaultKeys != null && r.DefaultKeys.Contains(key)),
                CurrentComplete = relations.All(r => r.KnowsKey(key)), FactoryComplete = relations.All(r => r.DefaultKeys != null) };
        }

        public void RestoreKey(string key, bool gamepad, int familyIndex)
        {
            RestoreKeys(new[] { key }, gamepad, familyIndex);
        }

        public void RestoreKeys(IEnumerable<string> keys, bool gamepad, int familyIndex, Action<string, string> addOverflow = null)
        {
            EnsureEditable();
            var relations = keys.Distinct().Select(key => RelationsForKey(key, gamepad, familyIndex)).ToArray();
            if (relations.Any(relation => !relation.CurrentComplete || !relation.FactoryComplete)) throw new InvalidOperationException(I18n.Str("views.incomplete"));
            Draft previous = draft, previousPreview = previewCheckpoint;
            string previousProfile = previewProfile, previousPreset = PresetId;
            draft = draft.Copy();
            try
            {
                foreach (var relation in relations)
                    foreach (var action in relation.Added) ChangeRelationship(action.Action, relation.Key, false, gamepad, familyIndex);
                foreach (var relation in relations)
                    foreach (var action in relation.Removed)
                        if (addOverflow != null && !RelationshipCapacity(action.Action, gamepad, familyIndex).CanAdd)
                            addOverflow(action.Action, relation.Key);
                        else ChangeRelationship(action.Action, relation.Key, true, gamepad, familyIndex);
            }
            catch
            {
                draft = previous; previewCheckpoint = previousPreview; previewProfile = previousProfile; PresetId = previousPreset;
                throw;
            }
        }

        public void RestoreAction(string action, bool gamepad, int familyIndex)
        {
            EnsureEditable(); EndPreview();
            var current = Relations(action, gamepad, familyIndex);
            if (current.CanCompare && SameKeys(current.Keys, current.DefaultKeys)) return;
            foreach (var record in ActionBindingRecords(action, gamepad, familyIndex, false))
            {
                var cell = Address(record.Id);
                Edit(cell, gamepad ? cell.DefaultKey ?? Current(cell) : null);
            }
            var restored = Relations(action, gamepad, familyIndex);
            var before = RelationsBeforeEdit(action, gamepad, familyIndex);
            if (restored.Complete && before.Complete && SameKeys(restored.Keys, before.Keys))
                foreach (string id in draft.Edits.Keys.Where(id => Address(id).Profile == SelectedProfile && Address(id).Action == action
                    && Address(id).IsGamepad == gamepad && (!gamepad || Address(id).FamilyIndex == familyIndex)).ToArray())
                { draft.Edits.Remove(id); draft.Conflicts.Remove(id); }
        }

        public void ChangeRelationship(string action, string key, bool add, bool gamepad, int familyIndex)
        {
            EnsureEditable();
            if (string.IsNullOrEmpty(key) || key == "None") throw new ArgumentException("Choose a button.", "key");
            var relation = Relations(action, gamepad, familyIndex);
            if (!relation.Complete && (add || relation.DefaultKeys == null || relation.DefaultKeys.Contains(key)))
                throw new InvalidOperationException(I18n.Str("views.incomplete"));
            if (relation.Keys.Contains(key) == add) return;
            if (!add && Defaults.FixedForAction(action, gamepad).Contains(key))
                throw new InvalidOperationException(I18n.Str("views.fixedbinding"));
            var records = ActionBindingRecords(action, gamepad, familyIndex, false);
            string family = gamepad ? cells.Values.First(c => c.IsGamepad && c.Profile == SelectedProfile && c.FamilyIndex == familyIndex).Family : null;
            int[] rawSlots = gamepad ? PadRawSlots(SelectedProfile, family, action) : KeyboardRawSlots(SelectedProfile, action);
            Func<BindingRecord, int> rawSlot = r => gamepad ? InputDefaults.RawSlot(r.Slot) : int.Parse(r.Slot);
            if (!add)
            {
                EndPreview();
                foreach (var record in records.Where(r => r.Key == key)) Edit(Address(record.Id), "None");
                if (!gamepad)
                    foreach (int raw in Defaults.EditableRegisteredSlots(action, false).Where(s => Defaults.ForRawSlot(action, false, s) == key
                        && !records.Any(r => r.Slot == s.ToString())))
                    { var cell = KeyboardAddressForRaw(action, raw); Edit(cell, "None"); }
                else
                    foreach (int raw in Defaults.EditableRegisteredSlots(action, true).Where(s => Defaults.ForRawSlot(action, true, s) == key
                        && !records.Any(r => rawSlot(r) == s)))
                        SetGamepadFamily(action, Array.IndexOf(rawSlots, raw), familyIndex, "None");
                return;
            }
            int available = AvailableRelationshipSlot(action, gamepad, familyIndex);
            if (available < 0)
                throw new InvalidOperationException(I18n.Str("views.bindingfull", BindingViewText.ActionName(action), RelationshipCapacity(action, gamepad, familyIndex).Limit));
            if (gamepad) SetGamepadFamily(action, available, familyIndex, key); else SetKeyboard(action, available, key);
        }

        public BindingCapacity RelationshipCapacity(string action, bool gamepad, int familyIndex)
        {
            string family = gamepad ? cells.Values.First(c => c.IsGamepad && c.Profile == SelectedProfile && c.FamilyIndex == familyIndex).Family : null;
            int[] rawSlots = gamepad ? PadRawSlots(SelectedProfile, family, action) : KeyboardRawSlots(SelectedProfile, action);
            var relation = Relations(action, gamepad, familyIndex);
            return new BindingCapacity {
                Used = relation.Keys.Except(Defaults.FixedForAction(action, gamepad)).Count(),
                Limit = rawSlots.Count(raw => !gamepad || Defaults.EditableRegisteredSlots(action, true).Contains(raw)),
                CanAdd = relation.Complete && AvailableRelationshipSlot(action, gamepad, familyIndex) >= 0 };
        }

        int AvailableRelationshipSlot(string action, bool gamepad, int familyIndex)
        {
            var records = ActionBindingRecords(action, gamepad, familyIndex, false);
            string family = gamepad ? cells.Values.First(c => c.IsGamepad && c.Profile == SelectedProfile && c.FamilyIndex == familyIndex).Family : null;
            int[] rawSlots = gamepad ? PadRawSlots(SelectedProfile, family, action) : KeyboardRawSlots(SelectedProfile, action);
            Func<BindingRecord, int> rawSlot = r => gamepad ? InputDefaults.RawSlot(r.Slot) : int.Parse(r.Slot);
            for (int slot = 0; slot < rawSlots.Length; slot++)
            {
                int raw = rawSlots[slot];
                if (gamepad && !Defaults.EditableRegisteredSlots(action, true).Contains(raw)) continue;
                var group = records.Where(r => rawSlot(r) == raw).ToArray();
                string inheritedKey = Defaults.ForRawSlot(action, gamepad, raw);
                string[] provided = group.Length == 0 ? new[] { inheritedKey ?? "None" } : group.Select(r => r.Key).ToArray();
                var remaining = records.Where(r => rawSlot(r) != raw).Select(r => r.Key)
                    .Concat(Defaults.EditableRegisteredSlots(action, gamepad).Where(s => s != raw && !records.Any(r => rawSlot(r) == s))
                        .Select(s => Defaults.ForRawSlot(action, gamepad, s)))
                    .Concat(Defaults.FixedForAction(action, gamepad));
                if (provided.All(k => k == "None" || string.IsNullOrEmpty(k) || remaining.Contains(k)))
                    return slot;
            }
            return -1;
        }

        public void ReplaceRelationship(string action, string oldKey, string newKey, bool gamepad, int familyIndex)
        {
            EnsureEditable();
            if (string.IsNullOrEmpty(newKey) || newKey == "None") throw new ArgumentException("Choose a button.", "newKey");
            var relation = Relations(action, gamepad, familyIndex);
            if (!relation.Complete && (relation.DefaultKeys == null || relation.DefaultKeys.Contains(oldKey)))
                throw new InvalidOperationException(I18n.Str("views.incomplete"));
            if (!relation.Keys.Contains(oldKey)) return;
            var records = ActionBindingRecords(action, gamepad, familyIndex, false);
            if (Defaults.FixedForAction(action, gamepad).Contains(oldKey)) throw new InvalidOperationException(I18n.Str("views.fixedbinding"));
            EndPreview();
            foreach (var record in records.Where(r => r.Key == oldKey)) Edit(Address(record.Id), newKey);
            if (!gamepad)
                foreach (int raw in Defaults.EditableRegisteredSlots(action, false).Where(s => Defaults.ForRawSlot(action, false, s) == oldKey
                    && !records.Any(r => r.Slot == s.ToString())))
                { var cell = KeyboardAddressForRaw(action, raw); Edit(cell, newKey); }
            else
            {
                string family = cells.Values.First(c => c.IsGamepad && c.Profile == SelectedProfile && c.FamilyIndex == familyIndex).Family;
                int[] slots = PadRawSlots(SelectedProfile, family, action);
                foreach (int raw in Defaults.EditableRegisteredSlots(action, true).Where(s => Defaults.ForRawSlot(action, true, s) == oldKey
                    && !records.Any(r => InputDefaults.RawSlot(r.Slot) == s)))
                    SetGamepadFamily(action, Array.IndexOf(slots, raw), familyIndex, newKey);
            }
        }
    }
}
