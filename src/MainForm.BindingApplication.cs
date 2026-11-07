using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        internal sealed class BindingApplicationState
        {
            public bool Live, CanApply, Deferred;
            public KeyEnhancementLiveStatus Connection;
            public string Reason;
        }

        int keyboardBindingChanges, gamepadBindingChanges;
        bool feedbackDismissed;
        bool HasPendingEdits
        { get { return Session != null && (Session.HasAnyDrafts || Session.HasChanges || ResourceModPending || KeyEnhancementPending); } }
        bool HasDeferredComponents
        { get { return ResourceModPending || keyEnhancementUninstall || keyEnhancement.Enabled != keyEnhancementBaseline.Enabled || keyEnhancementRepair; } }
        bool LiveBindingPending
        {
            get
            {
                var draft = keyEnhancement.Clone(); draft.SetEnabled(keyEnhancementBaseline.Enabled);
                return draft.DeploymentJson() != keyEnhancementBaseline.DeploymentJson();
            }
        }

        internal BindingApplicationState BindingApplication()
        {
            var state = new BindingApplicationState();
            if (Session == null) return state;
            if (Session.IsOffline) { state.CanApply = !IsApplying; return state; }
            if (!lastRunning)
            {
                state.CanApply = !IsApplying && Session.Conflicts.Length == 0 && (Session.HasChanges || ResourceModPending || KeyEnhancementPending
                    || EnhancedSchemes && keyEnhancement.SelectedSchemeId != keyEnhancement.ActiveSchemeId);
                return state;
            }
            state.Connection = keyEnhancementBaseline.Enabled && keyEnhancementInstallation != null ? keyEnhancementInstallation.LiveStatus() : null;
            state.Live = state.Connection != null && state.Connection.Available;
            state.Deferred = HasDeferredComponents;
            state.Reason = !state.Live ? state.Connection == null ? I18n.Str("live.unavailable") : state.Connection.Detail
                : Session.IsResetPending ? I18n.Str("live.lifecycle")
                : keyEnhancement.Schemes.Count > 0 && Session.HasAnyDrafts ? I18n.Str("schemes.nativepending")
                : EnhancedSchemes && keyEnhancementRepair ? I18n.Str("live.lifecycle")
                : keyEnhancement.Schemes.Count == 0 && (Session.SelectedProfile != Session.ActiveProfile || Session.ActivationPending || Session.SchemeError != null) ? I18n.Str("live.scope")
                : Session.Conflicts.Length > 0 ? I18n.Str("editor.conflictinfo") : null;
            state.CanApply = !IsApplying && state.Live && state.Reason == null
                && (Session.ScopeChangeCount > 0 || LiveBindingPending || keyEnhancement.Enabled && keyEnhancement.SelectedSchemeId != state.Connection.ActiveSchemeId || !state.Connection.RoutingEnabled);
            if (state.Reason == null && state.Deferred) state.Reason = I18n.Str(state.CanApply ? "flow.deferred.withbindings" : "flow.deferred.only");
            return state;
        }

        string PendingBindingSummary()
        {
            if (Session.IsResetPending) return I18n.Str("editor.resetscope", Session.ScopeChangeCount);
            string summary = keyboardBindingChanges + gamepadBindingChanges > 0
                ? I18n.Str("flow.pending", keyboardBindingChanges, gamepadBindingChanges)
                : KeyEnhancementPending && LiveBindingPending ? I18n.Str("flow.pending.advanced") : I18n.Str("flow.clean");
            if (HasDeferredComponents) summary += " · " + I18n.Str("flow.components.pending");
            if (Session.ActivationPending) summary += I18n.Str("editor.activationscope");
            return summary;
        }

        void AcceptLiveBindings(KeyEnhancementConfiguration candidate, bool desiredEnabled, int family)
        {
            if (candidate.Schemes.Count == 0) Session.AcceptLiveDraft(family);
            keyEnhancementBaseline.ReplaceWith(candidate);
            keyEnhancement.ReplaceWith(candidate); keyEnhancement.SetEnabled(desiredEnabled);
            bindingWorkspaces.Clear();
            if (candidate.Schemes.Count > 0) { EndEnhancementPresetPreview(); return; }
            // Applied save drafts become runtime bindings. Keep earlier undo steps in
            // that same representation so an undo cannot reintroduce a save edit.
            foreach (var saved in bindingHistory.Where(item => item.Session == Session))
            {
                var previous = Session.CopyForPreset(); previous.RestoreDraft(saved.Draft);
                if (!previous.IsResetPending && !previous.ActivationPending && previous.SelectedProfile == previous.ActiveProfile
                    && previous.SchemeError == null && previous.Conflicts.Length == 0)
                {
                    try { saved.Configuration = BindingLivePlan.Create(previous, saved.Configuration, family); }
                    catch (BindingDraftConflictException) { continue; }
                    previous.AcceptLiveDraft(family); saved.Draft = previous.CaptureDraft();
                    saved.Signature = previous.DraftSignature + "\n" + saved.Configuration.ToJson() + "|" + saved.Resource + "|" + saved.Uninstall;
                }
            }
        }

        internal BindingWorkspace BindingDialogWorkspace(bool pad, int family)
        { return ViewWorkspace(pad, family); }

        internal BindingEntry StageBindingFromDialog(BindingWorkspace workspace, string action, BindingEntry current, BindingEntry result)
        {
            BindingEntry staged = null;
            TryAction(delegate { staged = current == null ? workspace.Add(action, result) : workspace.Replace(current, result); });
            return staged;
        }

        internal void ReturnFromBindingEditor() { ReturnToGame(); }
        internal bool CanUndoBinding { get { return HasBindingHistory && !IsApplying; } }
        internal void UndoFromBindingEditor()
        { restoringEdit = true; try { TryAction(UndoBindingEdit); } finally { restoringEdit = false; } }
        internal void ShowBindingFeedback() { ShowFullFeedback(); }

        internal void PresentBindingWindow(EditorWindow dialog, Form owner, Action completed)
        {
            bool previousModal = modalActive; modalActive = true;
            keyEnhancementFlame.AnimationPaused = true;
            Control[] locked = owner == this ? Controls.Cast<Control>().Where(control => control != Caption && control.Enabled).ToArray() : new Control[] { owner };
            foreach (var control in locked) control.Enabled = false;
            dialog.FormClosed += delegate {
                foreach (var control in locked) if (!control.IsDisposed) control.Enabled = true;
                if (!IsDisposed)
                {
                    if (completed != null) completed();
                    if (previousModal) modalActive = true; else EndModal();
                    RenderSession();
                }
            };
            dialog.Show(owner);
        }

        internal void AttachLiveBindingDialog(EditorWindow dialog, Button primary, Label status, Action refresh)
        {
            var timer = new Timer { Interval = 500 };
            bool busy = false;
            Action update = delegate {
                if (busy || dialog.IsDisposed) return;
                var state = BindingApplication();
                primary.Text = I18n.Str(state.Live ? state.CanApply ? "flow.applyreturn" : "flow.return" : "editor.close");
                ((EditorButton)primary).Primary = state.Live;
                primary.Enabled = !IsApplying;
                if (state.Live || state.Connection != null)
                {
                    status.Text = messageError ? I18n.Str("flow.failed.details")
                        : state.Reason ?? (state.CanApply ? I18n.Str("flow.draft.hint") : I18n.Str("flow.test.hint"));
                    status.ForeColor = messageError ? NoticeCard.Accent : EditorTheme.Muted;
                }
            };
            primary.Click += async delegate {
                var state = BindingApplication();
                if (!state.Live) { dialog.Close(); return; }
                if (HasPendingEdits && !state.CanApply && state.Reason != null && !state.Deferred)
                { status.Text = state.Reason; return; }
                busy = true; dialog.Enabled = false;
                try
                {
                    if (state.CanApply && !await ApplyChangesAsync())
                    { status.Text = I18n.Str("flow.failed.details"); ShowFullFeedback(); return; }
                    dialog.Close(); ReturnToGame();
                }
                finally { if (!dialog.IsDisposed) dialog.Enabled = true; busy = false; update(); }
            };
            status.Cursor = Cursors.Hand;
            status.Click += delegate { if (messageError) ShowFullFeedback(); else if (BindingApplication().Connection != null) ShowLiveConnectionHelp(); };
            dialog.KeyPreview = true;
            dialog.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Control && e.KeyCode == Keys.Z && HasBindingHistory && !IsApplying)
                { UndoFromBindingEditor(); refresh(); update(); e.SuppressKeyPress = true; }
            };
            timer.Tick += delegate { update(); };
            update(); timer.Start();
            dialog.FormClosed += delegate { timer.Dispose(); };
        }

        void ReturnToGame()
        {
            if (companionReturnWindow == IntPtr.Zero || companionReturnWindow == Handle)
                foreach (var process in Process.GetProcessesByName("PBZ"))
                    using (process) if (process.MainWindowHandle != IntPtr.Zero) { companionReturnWindow = process.MainWindowHandle; break; }
            companionVisible = true;
            ToggleCompanion();
        }

        void ShowLiveConnectionHelp()
        {
            var state = BindingApplication();
            string content = state.Connection == null ? I18n.Str("live.unavailable") : state.Connection.Detail;
            if (state.Connection != null && state.Connection.ErrorDetail != null && content != state.Connection.ErrorDetail)
                content += "\n\n" + state.Connection.ErrorDetail;
            content += "\n\n" + I18n.Str("flow.connection.help");
            if (keyEnhancementInstallation != null)
                content += "\n\n" + keyEnhancementInstallation.InstallDirectory + "\n" + I18n.Str("flow.connection.files")
                    + "\n" + Path.Combine(keyEnhancementInstallation.InstallDirectory, "live-status.txt");
            ShowFeedbackDialog(content, I18n.Str("flow.connection.title"));
        }

        void RestoreLiveBindings()
        {
            RememberBindingEdit();
            keyEnhancement.ReplaceWith(BindingLivePlan.RestoreBindings(Session, keyEnhancement, SelectedFamilyIndex));
        }
    }
}
