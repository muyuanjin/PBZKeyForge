using System;
using System.Linq;

namespace PBZKeyForge
{
    public sealed partial class EditorSession
    {
        internal EditorSession CopyForPreset()
        {
            var copy = new EditorSession { diskBytes = (byte[])diskBytes.Clone(), disk = SaveFile.FromBytes(diskBytes, Defaults),
                InputPath = InputPath, SelectedProfile = SelectedProfile, ActiveProfile = ActiveProfile, SchemeError = SchemeError };
            foreach (var pair in familyIndices) copy.familyIndices[pair.Key] = pair.Value;
            copy.cells = copy.Extract(copy.disk); copy.Remember(copy.cells);
            foreach (var pair in addresses) if (!copy.addresses.ContainsKey(pair.Key)) copy.addresses[pair.Key] = pair.Value;
            copy.RestoreDraft(CaptureDraft());
            return copy;
        }

        internal void ApplyPresetPlan(EditorSession plan)
        {
            Remember(plan.addresses);
            foreach (var pair in plan.familyIndices) familyIndices[pair.Key] = pair.Value;
            RestoreDraft(plan.CaptureDraft());
        }

        sealed class EditCheckpoint
        {
            public Draft Draft, Preview, Reset;
            public byte[] Bytes;
            public InputDefaultSet Defaults;
            public string Preset, PreviewProfile, ResetProfile, ActivationProfile, Signature;
            public bool Activation, ResetActivation;
            public PresetApplyMode Mode;
            public string[] Excluded;
        }
        internal object CaptureDraft()
        {
            return new EditCheckpoint { Bytes = (byte[])diskBytes.Clone(), Defaults = Defaults, Mode = PresetMode, Draft = draft.Copy(), Preview = previewCheckpoint == null ? null : previewCheckpoint.Copy(),
                Reset = resetCheckpoint == null ? null : resetCheckpoint.Copy(), Preset = PresetId, PreviewProfile = previewProfile,
                ResetProfile = resetActivationProfile, Activation = ActivationPending, ActivationProfile = SelectedProfile, Signature = DraftSignature,
                ResetActivation = resetActivation, Excluded = resetExcluded.ToArray() };
        }
        internal bool MatchesDraft(object state)
        {
            var saved = (EditCheckpoint)state;
            return saved.Signature == DraftSignature && (!IsOffline || diskBytes.SequenceEqual(saved.Bytes));
        }
        internal void RestoreDraft(object state)
        {
            var saved = (EditCheckpoint)state; PresetMode = saved.Mode;
            if (IsOffline && !diskBytes.SequenceEqual(saved.Bytes))
            {
                diskBytes = (byte[])saved.Bytes.Clone(); disk = SaveFile.FromBytes(diskBytes, saved.Defaults);
                cells = Extract(disk); Remember(cells);
            }
            draft = saved.Draft.Copy(); previewCheckpoint = saved.Preview == null ? null : saved.Preview.Copy();
            resetCheckpoint = saved.Reset == null ? null : saved.Reset.Copy(); PresetId = saved.Preset; previewProfile = saved.PreviewProfile;
            resetActivationProfile = saved.ResetProfile;
            ActivationPending = saved.Activation && saved.ActivationProfile == SelectedProfile && SelectedProfile != ActiveProfile;
            resetActivation = saved.ResetActivation;
            resetExcluded.Clear(); foreach (string id in saved.Excluded) resetExcluded.Add(id);
        }
        internal void AcceptLiveDraft(int familyIndex)
        {
            foreach (string id in draft.Edits.Keys.Where(id => Address(id).Profile == SelectedProfile && (!Address(id).IsGamepad || Address(id).FamilyIndex == familyIndex)).ToArray())
            { draft.Edits.Remove(id); draft.Conflicts.Remove(id); draft.PresetOverrideEdits.Remove(id); }
            previewCheckpoint = null; previewProfile = null; PresetId = null;
        }
        internal string DraftSignature
        { get { return string.Join("\n", draft.Edits.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value)) + "|" + (ActivationPending ? SelectedProfile : "") + "|" + IsResetPending
            + "|" + string.Join("\n", draft.PresetOverrideEdits.OrderBy(id => id)); } }

        internal void PreservePresetOverride(string action, bool gamepad, int family)
        {
            foreach (string id in BindingDraftIds(action, gamepad, family)) draft.PresetOverrideEdits.Add(id);
        }

        internal bool HasIndependentBindingDraft(string action, bool gamepad, int family)
        { return BindingDraftIds(action, gamepad, family).Any(id => !draft.PresetOverrideEdits.Contains(id)); }

        System.Collections.Generic.IEnumerable<string> BindingDraftIds(string action, bool gamepad, int family)
        {
            return draft.Edits.Keys.Where(id => Address(id).Profile == SelectedProfile && Address(id).Action == action
                && Address(id).IsGamepad == gamepad && (!gamepad || Address(id).FamilyIndex == family));
        }
    }
}
