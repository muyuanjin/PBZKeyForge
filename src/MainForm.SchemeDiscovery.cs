using System;
using System.Threading.Tasks;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        EditorSession discoverySession, queuedDiscoverySession, completedDiscoverySession;
        Exception completedDiscoveryFailure;
        EditorSession attemptedDiscoverySession;
        SchemeDiscoveryRequiredException attemptedDiscoveryRequest;

        bool SchemeProbePending
        { get { return Session != null && (discoverySession == Session || queuedDiscoverySession == Session); } }

        void RenderSchemeDiscoveryPending()
        {
            if (IsApplying) return;
            RenderGameStatus();
            SetTip(chkActivate, I18n.Str("editor.discovering"));
        }

        async void StartSchemeDiscovery(bool allowRetry = true)
        {
            if (IsDisposed || !Visible || Session == null || Session.IsOffline || !Session.SchemeDiscoveryRequired) return;
            if (!allowRetry && attemptedDiscoverySession == Session
                && Session.SchemeDiscoveryRequest.SameExecutableVersion(attemptedDiscoveryRequest)) return;
            if (discoverySession != null)
            {
                if (discoverySession != Session) { queuedDiscoverySession = Session; RenderSchemeDiscoveryPending(); }
                return;
            }
            var captured = Session;
            string input = captured.InputPath;
            discoverySession = captured;
            attemptedDiscoverySession = captured;
            attemptedDiscoveryRequest = captured.SchemeDiscoveryRequest;
            RenderSchemeDiscoveryPending();
            Exception failure = null;
            try { await Task.Run(delegate { GameSchemeSettings.LoadForInputSave(input); }); }
            catch (Exception ex) { failure = ex; }
            discoverySession = null;
            if (IsDisposed || Disposing) return;
            if (Session == captured)
            {
                completedDiscoverySession = captured;
                completedDiscoveryFailure = failure;
                ConsumeSchemeDiscovery();
            }
            var queued = queuedDiscoverySession;
            queuedDiscoverySession = null;
            if (queued != null && queued == Session && queued != captured) StartSchemeDiscovery();
        }

        void ConsumeSchemeDiscovery()
        {
            if (IsApplying || modalActive) return;
            var completed = completedDiscoverySession;
            var failure = completedDiscoveryFailure;
            completedDiscoverySession = null;
            completedDiscoveryFailure = null;
            if (IsDisposed || Disposing || completed == null || completed != Session) return;
            try { completed.RefreshSchemeAfterDiscovery(failure); }
            catch (Exception ex) { SetMessage("editor.refreshfail", true, ex.Message); }
            RenderSession();
        }

        void EndModal()
        {
            modalActive = false;
            ConsumeSchemeDiscovery();
        }
    }
}
