using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PBZKeyForge
{
    public sealed class EditorConflict
    {
        public string Id, ProfileId, Action, LocalValue, DiskValue;
        public int SlotIndex, FamilyIndex;
        public bool IsGamepad;
    }

    public sealed class EditorApplyResult
    {
        public bool BindingsSaved, ActivationSaved, Success;
        public string Error, InputBackup, ActivationBackup;
        public bool PartialSuccess { get { return !Success && (BindingsSaved || ActivationSaved); } }
    }

    /// <summary>Owns disk snapshots, binding drafts and transactional application scope.</summary>
    public sealed class EditorSession
    {
        sealed class Cell
        {
            public string Id, Profile, Action, Key, DefaultKey, Slot, Family;
            public int SlotIndex, FamilyIndex;
            public bool IsGamepad;
            public BlobRecord Record;
            public BlobEntry Entry;
            public Node Keys;
            public List<Node> Item;
        }

        sealed class Draft
        {
            public Dictionary<string, string> Edits = new Dictionary<string, string>();
            public Dictionary<string, EditorConflict> Conflicts = new Dictionary<string, EditorConflict>();
            public Draft Copy()
            {
                var copy = new Draft();
                foreach (var pair in Edits) copy.Edits.Add(pair.Key, pair.Value);
                foreach (var pair in Conflicts) copy.Conflicts.Add(pair.Key, CopyConflict(pair.Value));
                return copy;
            }
        }

        byte[] diskBytes;
        SaveFile disk;
        Dictionary<string, Cell> cells;
        // Addresses survive missing overrides and provide metadata for pending additions.
        readonly Dictionary<string, Cell> addresses = new Dictionary<string, Cell>();
        Draft draft = new Draft(), previewCheckpoint, resetCheckpoint;
        readonly HashSet<string> resetExcluded = new HashSet<string>();
        string previewProfile;
        string resetActivationProfile;
        bool resetActivation;
        public string InputPath { get; private set; }
        public string SelectedProfile { get; private set; }
        public string ActiveProfile { get; private set; }
        public string SchemeError { get; private set; }
        public string PresetId { get; private set; }
        public bool ActivationPending { get; private set; }
        public bool IsResetPending { get { return resetCheckpoint != null; } }
        public string[] Profiles { get { return disk.Records.Select(r => r.ProfileId).ToArray(); } }
        public bool HasChanges { get { return ScopeChangeCount > 0 || ActivationPending || IsResetPending; } }
        public bool HasAnyDrafts { get { return draft.Edits.Count > 0 || ActivationPending || IsResetPending; } }
        public int KeyboardChangeCount { get { return draft.Edits.Keys.Count(id => !Address(id).IsGamepad && Address(id).Profile == SelectedProfile); } }
        public int GamepadChangeCount { get { return draft.Edits.Keys.Count(id => Address(id).IsGamepad && Address(id).Profile == SelectedProfile); } }
        public int ScopeChangeCount { get { return draft.Edits.Keys.Count(InScope); } }
        public EditorConflict[] Conflicts { get { return draft.Conflicts.Values.Where(c => InScope(c.Id)).Select(CopyConflict).ToArray(); } }

        // Standalone source-level integration tests inject a race at the last safe boundary.
        internal Action BeforeInputReplace;
        internal Action BeforeActivation;

        public static bool IsGameRunning
        {
            get
            {
                var processes = Process.GetProcessesByName("PBZ");
                try { return processes.Length > 0; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
        }

        EditorSession() { }

        public static EditorSession Open(string inputPath)
        {
            var session = new EditorSession { InputPath = Path.GetFullPath(inputPath) };
            session.diskBytes = File.ReadAllBytes(session.InputPath);
            session.disk = SaveFile.FromBytes(session.diskBytes);
            session.cells = Extract(session.disk);
            session.Remember(session.cells);
            session.ReadScheme();
            session.SelectedProfile = session.disk.RecordFor(session.ActiveProfile) != null ? session.ActiveProfile : session.disk.ActiveProfile;
            if (session.disk.RecordFor(session.SelectedProfile) == null)
                session.SelectedProfile = session.Profiles.FirstOrDefault();
            if (session.SelectedProfile == null) throw new InvalidDataException("The input save has no keyboard schemes.");
            return session;
        }

        void ReadScheme()
        {
            try { ActiveProfile = GameSchemeSettings.LoadForInputSave(InputPath).ProfileId; SchemeError = null; }
            catch (Exception ex) { ActiveProfile = null; SchemeError = ex.Message; }
            if (ActiveProfile == SelectedProfile) ActivationPending = false;
        }

        public void SwitchProfile(string profile)
        {
            if (disk.RecordFor(profile) == null) throw new ArgumentException("Unknown keyboard scheme.", "profile");
            EndPreview();
            Refresh();
            SelectedProfile = profile;
            ActivationPending = false;
        }

        public void Refresh()
        {
            byte[] latest = File.ReadAllBytes(InputPath);
            if (!Same(latest, diskBytes))
            {
                SaveFile incoming = SaveFile.FromBytes(latest);
                var incomingCells = Extract(incoming);
                Remember(incomingCells);
                Merge(draft, cells, incomingCells);
                if (previewCheckpoint != null) Merge(previewCheckpoint, cells, incomingCells);
                if (resetCheckpoint != null) Merge(resetCheckpoint, cells, incomingCells);
                disk = incoming;
                diskBytes = latest;
                cells = incomingCells;
                // A global reset also owns overrides that arrived after it was staged.
                if (IsResetPending)
                    foreach (var cell in cells.Values)
                        if (!draft.Edits.ContainsKey(cell.Id) && !resetExcluded.Contains(cell.Id) && cell.Key != ResetValue(cell))
                        {
                            draft.Edits[cell.Id] = ResetValue(cell);
                            draft.Conflicts[cell.Id] = Conflict(cell, ResetValue(cell), cell.Key);
                        }
            }
            ReadScheme();
        }

        static string Value(Dictionary<string, Cell> map, string id)
        {
            Cell cell;
            return map.TryGetValue(id, out cell) ? cell.Key : null;
        }

        void Merge(Draft state, Dictionary<string, Cell> oldCells, Dictionary<string, Cell> incoming)
        {
            foreach (string id in state.Edits.Keys.ToArray())
            {
                string local = state.Edits[id], old = Value(oldCells, id), next = Value(incoming, id);
                if (local == next)
                {
                    state.Edits.Remove(id);
                    state.Conflicts.Remove(id);
                }
                else if (state.Conflicts.ContainsKey(id) || old != next)
                    state.Conflicts[id] = Conflict(Address(id), local, next);
            }
        }

        void Remember(Dictionary<string, Cell> map)
        {
            foreach (var pair in map) addresses[pair.Key] = pair.Value;
        }

        Cell Address(string id) { return addresses[id]; }
        bool InScope(string id) { return IsResetPending || Address(id).Profile == SelectedProfile; }
        string Current(Cell cell)
        {
            string value;
            return draft.Edits.TryGetValue(cell.Id, out value) ? value : Value(cells, cell.Id);
        }

        public string GetKeyboard(string action, int slotIndex)
        {
            CheckSlot(slotIndex);
            return Current(KeyboardAddress(action, slotIndex));
        }

        public string GetGamepad(string action, int slotIndex)
        {
            CheckSlot(slotIndex);
            Cell cell = PadAddresses(action, slotIndex, false).FirstOrDefault();
            return cell == null ? null : Current(cell);
        }

        public string GetGamepadDefault(string action, int slotIndex)
        {
            Cell cell = PadAddresses(action, slotIndex, false).FirstOrDefault();
            return cell == null ? null : cell.DefaultKey;
        }

        public bool GamepadValuesDiffer(string action, int slotIndex)
        {
            return PadAddresses(action, slotIndex, false).Select(Current).Distinct().Skip(1).Any();
        }

        public bool HasKeyboardDraft(string profile)
        {
            return draft.Edits.Keys.Any(id => !Address(id).IsGamepad && Address(id).Profile == profile);
        }

        public bool HasGamepadDraft(string profile)
        { return draft.Edits.Keys.Any(id => Address(id).IsGamepad && Address(id).Profile == profile); }

        public bool HasProfileDraft(string profile)
        { return draft.Edits.Keys.Any(id => Address(id).Profile == profile); }

        public bool IsKeyboardChanged(string action, int slotIndex)
        {
            return draft.Edits.Keys.Any(id => !Address(id).IsGamepad && Address(id).Profile == SelectedProfile && Address(id).Action == action && Address(id).SlotIndex == slotIndex);
        }

        public bool IsGamepadChanged(string action, int slotIndex)
        {
            return draft.Edits.Keys.Any(id => Address(id).IsGamepad && Address(id).Profile == SelectedProfile && Address(id).Action == action && Address(id).SlotIndex == slotIndex);
        }

        static void CheckSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex > 2) throw new ArgumentOutOfRangeException("slotIndex");
        }

        Cell KeyboardAddress(string action, int slotIndex)
        {
            string slot = Presets.KeyboardSlots[slotIndex].ToString();
            string id = KeyboardId(SelectedProfile, action, slot, 0);
            Cell cell;
            if (!addresses.TryGetValue(id, out cell))
            {
                cell = new Cell { Id = id, Profile = SelectedProfile, Action = action, Slot = slot, SlotIndex = slotIndex, FamilyIndex = -1 };
                addresses[id] = cell;
            }
            return cell;
        }

        IEnumerable<Cell> PadAddresses(string action, int slotIndex, bool create)
        {
            string slot = "EPlayerMappableKeySlot::" + Presets.GamepadSlots[slotIndex];
            var result = new List<Cell>();
            foreach (var group in cells.Values.Where(c => c.IsGamepad && c.Profile == SelectedProfile).GroupBy(c => c.Family))
            {
                Cell template = group.FirstOrDefault(c => c.Action == action);
                if (template == null) continue;
                string id = PadId(group.Key, action, slot, 0);
                Cell cell;
                if (!addresses.TryGetValue(id, out cell))
                {
                    if (!create) continue;
                    cell = new Cell { Id = id, Profile = SelectedProfile, IsGamepad = true, Action = action, Slot = slot, SlotIndex = slotIndex, FamilyIndex = template.FamilyIndex,
                        Family = group.Key, DefaultKey = template.DefaultKey, Keys = template.Keys, Item = template.Item };
                    addresses[id] = cell;
                }
                result.Add(cell);
            }
            return result;
        }

        void EnsureEditable()
        {
            if (IsResetPending) throw new InvalidOperationException("Apply or undo the pending global reset first.");
        }

        public void SetKeyboard(string action, int slotIndex, string key)
        {
            EnsureEditable(); CheckSlot(slotIndex); EndPreview();
            Cell primary = KeyboardAddress(action, slotIndex);
            var targets = cells.Values.Where(c => !c.IsGamepad && c.Profile == SelectedProfile && c.Action == action && c.Slot == primary.Slot).ToArray();
            if (targets.Length == 0) targets = new[] { primary };
            foreach (Cell cell in targets) Edit(cell, Normalize(key));
        }

        public void SetGamepad(string action, int slotIndex, string key)
        {
            EnsureEditable(); CheckSlot(slotIndex); EndPreview();
            foreach (Cell cell in PadAddresses(action, slotIndex, key != null))
                Edit(cell, string.IsNullOrWhiteSpace(key) ? (cell.DefaultKey ?? Current(cell)) : key.Trim());
        }

        static string Normalize(string key) { return string.IsNullOrWhiteSpace(key) ? null : key.Trim(); }

        void Edit(Cell cell, string key)
        {
            if (key == Value(cells, cell.Id)) draft.Edits.Remove(cell.Id);
            else draft.Edits[cell.Id] = key;
            // Editing a conflicted cell explicitly chooses its new local value.
            draft.Conflicts.Remove(cell.Id);
        }

        public void SetActivation(bool enabled)
        {
            EnsureEditable();
            if (enabled && ActiveProfile == null) throw new InvalidOperationException("The game scheme selector is unavailable.");
            ActivationPending = enabled && SelectedProfile != ActiveProfile;
        }

        void EndPreview()
        {
            previewCheckpoint = null;
            previewProfile = null;
            PresetId = null;
        }

        public void SelectPreset(string id)
        {
            EnsureEditable();
            if (id == null)
            {
                if (previewCheckpoint != null) draft = previewCheckpoint.Copy();
                EndPreview();
                return;
            }
            Preset preset = Presets.ById(id);
            if (preset == null) throw new ArgumentException("Unknown quick configuration.", "id");
            if (preset.Vanilla) throw new InvalidOperationException("Use ResetAll for the global default reset.");
            // The CLI and editor use the same save transformation. Materialize it on
            // an isolated checkpoint so failed planning cannot alter drafts/conflicts.
            Draft checkpoint = previewCheckpoint != null && previewProfile == SelectedProfile ? previewCheckpoint.Copy() : draft.Copy();
            SaveFile planned = SaveFile.FromBytes(diskBytes);
            ApplyEdits(planned, checkpoint.Edits.ToArray());
            var before = Extract(planned);
            Presets.Apply(planned, preset, planned.RecordFor(SelectedProfile));
            var after = Extract(planned);
            Remember(after);
            if (previewCheckpoint == null || previewProfile != SelectedProfile)
            {
                previewCheckpoint = checkpoint.Copy();
                previewProfile = SelectedProfile;
            }
            draft = checkpoint.Copy();
            foreach (string changed in before.Keys.Union(after.Keys))
                if (Value(before, changed) != Value(after, changed)) Edit(Address(changed), Value(after, changed));
            PresetId = id;
        }

        public void ResetAll()
        {
            EnsureEditable();
            Refresh();
            EndPreview();
            resetCheckpoint = draft.Copy();
            resetExcluded.Clear();
            resetActivation = ActivationPending;
            resetActivationProfile = SelectedProfile;
            ActivationPending = false;
            draft = new Draft();
            foreach (Cell cell in cells.Values)
            {
                string key = ResetValue(cell);
                if (key != cell.Key) draft.Edits[cell.Id] = key;
            }
        }

        static string ResetValue(Cell cell) { return cell.IsGamepad ? (cell.DefaultKey ?? cell.Key) : null; }

        public void Undo()
        {
            Refresh();
            EndPreview();
            if (IsResetPending)
            {
                draft = resetCheckpoint;
                resetCheckpoint = null;
                resetExcluded.Clear();
                ActivationPending = resetActivation && SelectedProfile == resetActivationProfile && SelectedProfile != ActiveProfile;
            }
            else
            {
                foreach (string id in draft.Edits.Keys.Where(InScope).ToArray())
                { draft.Edits.Remove(id); draft.Conflicts.Remove(id); }
                ActivationPending = false;
            }
        }

        public void ResolveConflict(string id, bool keepLocal)
        {
            EditorConflict conflict;
            if (!draft.Conflicts.TryGetValue(id, out conflict)) return;
            if (!keepLocal)
            {
                draft.Edits.Remove(id);
                if (IsResetPending) resetExcluded.Add(id);
            }
            draft.Conflicts.Remove(id);
            if (previewCheckpoint != null && previewCheckpoint.Conflicts.ContainsKey(id))
            {
                if (!keepLocal) previewCheckpoint.Edits.Remove(id);
                previewCheckpoint.Conflicts.Remove(id);
            }
        }

        public EditorApplyResult Apply()
        {
            var result = new EditorApplyResult();
            try
            {
                if (IsGameRunning) throw new IOException("Close Phantom Blade Zero before applying changes.");
                Refresh();
                if (Conflicts.Length > 0) throw new IOException("Resolve binding conflicts before applying changes.");
                string targetProfile = SelectedProfile;
                // Validate the selector before writing bindings, so a known failure cannot cause partial success.
                GameSchemeSettings selector = ActivationPending ? GameSchemeSettings.LoadForInputSave(InputPath) : null;
                if (selector != null) GameSchemeSettings.ValueFor(targetProfile);
                var edits = draft.Edits.Where(p => InScope(p.Key)).ToArray();
                if (edits.Length > 0)
                {
                    SaveFile output = SaveFile.FromBytes(diskBytes);
                    ApplyEdits(output, edits);
                    byte[] encoded = output.ToBytes();
                    // Serialization must be stable before it may replace the live save.
                    if (!Same(encoded, SaveFile.FromBytes(encoded).ToBytes()))
                        throw new InvalidDataException("Input settings verification failed.");
                    result.InputBackup = WriteInput(encoded);
                    result.BindingsSaved = true;
                    foreach (var edit in edits) { draft.Edits.Remove(edit.Key); draft.Conflicts.Remove(edit.Key); }
                    resetCheckpoint = null;
                    resetExcluded.Clear();
                    EndPreview();
                    // Fold other schemes' drafts over the newly committed snapshot.
                    Refresh();
                }
                else if (IsResetPending)
                {
                    resetCheckpoint = null;
                    resetExcluded.Clear();
                    EndPreview();
                }
                if (selector != null && ActivationPending)
                {
                    if (BeforeActivation != null) BeforeActivation();
                    result.ActivationBackup = selector.Activate(targetProfile);
                    result.ActivationSaved = true;
                    ActivationPending = false;
                    ReadScheme();
                }
                EndPreview();
                result.Success = true;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        string WriteInput(byte[] encoded)
        {
            string temp = InputPath + ".pbzforge-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, encoded);
                if (!Same(encoded, SaveFile.Load(temp).ToBytes())) throw new InvalidDataException("Input settings read-back verification failed.");
                if (BeforeInputReplace != null) BeforeInputReplace();
                if (IsGameRunning) throw new IOException("Close Phantom Blade Zero before applying changes.");
                if (!Same(diskBytes, File.ReadAllBytes(InputPath))) throw new IOException("Input settings changed before writing; refresh before applying again.");
                string backup = Path.Combine(GameSchemeSettings.BackupDirectory(), Path.GetFileNameWithoutExtension(InputPath) + "."
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sav");
                File.WriteAllBytes(backup, diskBytes);
                // A final comparison also protects against a writer that ran while creating the backup.
                if (!Same(diskBytes, File.ReadAllBytes(InputPath))) throw new IOException("Input settings changed before replacement; refresh before applying again.");
                File.Replace(temp, InputPath, null);
                return backup;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        void ApplyEdits(SaveFile output, KeyValuePair<string, string>[] edits)
        {
            var outputCells = Extract(output);
            foreach (var edit in edits)
            {
                Cell descriptor = Address(edit.Key), target;
                if (outputCells.TryGetValue(edit.Key, out target))
                {
                    if (!target.IsGamepad)
                    {
                        if (edit.Value == null) target.Record.Entries.Remove(target.Entry);
                        else target.Entry.Key = edit.Value;
                    }
                    else if (edit.Value == null) target.Keys.Items.Remove(target.Item);
                    else SaveFile.SetField(target.Item, "Key", edit.Value);
                }
                else if (edit.Value != null && !descriptor.IsGamepad)
                {
                    BlobRecord record = output.RecordFor(descriptor.Profile);
                    if (record == null) throw new InvalidDataException("The edited keyboard scheme was removed externally.");
                    record.Entries.Add(new BlobEntry { Name = descriptor.Action, Slot = byte.Parse(descriptor.Slot), Key = edit.Value, X = "None", Y = "None" });
                }
                else if (edit.Value != null)
                {
                    Cell template = outputCells.Values.FirstOrDefault(c => c.IsGamepad && c.Profile == descriptor.Profile && c.Family == descriptor.Family && c.Action == descriptor.Action);
                    if (template == null) throw new InvalidDataException("The edited controller family was removed externally.");
                    var item = template.Item.Select(SaveFile.CloneNode).ToList();
                    SaveFile.SetField(item, "Slot", descriptor.Slot);
                    SaveFile.SetField(item, "Key", edit.Value);
                    template.Keys.Items.Add(item);
                }
            }
        }

        static EditorConflict CopyConflict(EditorConflict c)
        {
            return new EditorConflict { Id = c.Id, ProfileId = c.ProfileId, Action = c.Action, SlotIndex = c.SlotIndex,
                FamilyIndex = c.FamilyIndex, IsGamepad = c.IsGamepad, LocalValue = c.LocalValue, DiskValue = c.DiskValue };
        }

        static EditorConflict Conflict(Cell cell, string local, string incoming)
        {
            return new EditorConflict { Id = cell.Id, ProfileId = cell.Profile, Action = cell.Action, SlotIndex = cell.SlotIndex,
                FamilyIndex = cell.FamilyIndex, IsGamepad = cell.IsGamepad, LocalValue = local, DiskValue = incoming };
        }

        static string KeyboardId(string profile, string action, string slot, int ordinal)
        { return "K\u001f" + profile + "\u001f" + action + "\u001f" + slot + "\u001f" + ordinal; }
        static string PadId(string family, string action, string slot, int ordinal)
        { return "P\u001f" + family + "\u001f" + action + "\u001f" + slot + "\u001f" + ordinal; }

        static Dictionary<string, Cell> Extract(SaveFile save)
        {
            var map = new Dictionary<string, Cell>();
            foreach (BlobRecord record in save.Records)
            {
                var occurrences = new Dictionary<string, int>();
                foreach (BlobEntry entry in record.Entries)
                {
                    string group = entry.Name + "\u001f" + entry.Slot;
                    int ordinal;
                    occurrences.TryGetValue(group, out ordinal); occurrences[group] = ordinal + 1;
                    string id = KeyboardId(record.ProfileId, entry.Name, entry.Slot.ToString(), ordinal);
                    map[id] = new Cell { Id = id, Profile = record.ProfileId, Action = entry.Name, Key = entry.Key,
                        Slot = entry.Slot.ToString(), SlotIndex = Array.IndexOf(Presets.KeyboardSlots, (int)entry.Slot), FamilyIndex = -1, Entry = entry, Record = record };
                }
            }
            var familyOccurrences = new Dictionary<string, int>();
            foreach (GamepadProfile profile in save.GamepadProfiles)
                {
                    Node keys = profile.Keys;
                    string identity = FamilyIdentity(profile.Fields);
                    int familyOrdinal;
                    familyOccurrences.TryGetValue(identity, out familyOrdinal); familyOccurrences[identity] = familyOrdinal + 1;
                    identity += ":" + familyOrdinal;
                    var occurrences = new Dictionary<string, int>();
                    foreach (var item in keys.Items)
                    {
                        string action = SaveFile.Field(item, "MappingName"), slot = SaveFile.Field(item, "Slot");
                        if (action == null || slot == null) continue;
                        string group = action + "\u001f" + slot;
                        int ordinal;
                        occurrences.TryGetValue(group, out ordinal); occurrences[group] = ordinal + 1;
                        string id = PadId(identity, action, slot, ordinal);
                        map[id] = new Cell { Id = id, Profile = profile.ProfileId, IsGamepad = true, Action = action, Slot = slot, SlotIndex = Array.IndexOf(Presets.GamepadSlots, slot.Replace("EPlayerMappableKeySlot::", "")),
                            Family = identity, FamilyIndex = profile.Index, Key = SaveFile.Field(item, "Key"), DefaultKey = SaveFile.Field(item, "DefaultKey"), Keys = keys, Item = item };
                    }
                }
            return map;
        }

        static string FamilyIdentity(List<Node> family)
        {
            var text = new StringBuilder();
            foreach (Node node in family.Where(n => n.Name != "Keys")) Fingerprint(text, node);
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        static void Fingerprint(StringBuilder text, Node node)
        {
            text.Append(node.Name).Append(':').Append(node.Type).Append(':');
            if (node.Raw != null) text.Append(Convert.ToBase64String(node.Raw));
            if (node.Meta != null) foreach (string value in node.Meta) text.Append('|').Append(value);
            foreach (Node child in node.Children) Fingerprint(text, child);
            if (node.Nested != null) Fingerprint(text, node.Nested);
            foreach (var item in node.Items) foreach (Node child in item) Fingerprint(text, child);
            text.Append(';');
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
