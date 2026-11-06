using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        sealed class BindingCheckpoint
        {
            public EditorSession Session;
            public object Draft;
            public KeyEnhancementConfiguration Configuration;
            public bool? Resource;
            public bool Uninstall;
            public string Signature;
        }
        readonly Stack<BindingCheckpoint> bindingHistory = new Stack<BindingCheckpoint>();
        Panel feedbackPanel;
        Label feedbackText;
        Button feedbackDetails;
        string fullFeedback;
        bool restoringEdit;
        bool companionVisible;
        bool hotkeyRegistered;
        bool companionRecording, companionHotkeyEnabled, companionHotkeyConflict;
        IntPtr companionReturnWindow;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);

        string BindingEditSignature
        { get { return Session.DraftSignature + "\n" + keyEnhancement.ToJson() + "|" + resourceModDraft + "|" + keyEnhancementUninstall; } }

        bool HasBindingHistory
        { get { return bindingHistory.Any(saved => saved.Session == Session); } }

        bool MatchesBindingEdit(BindingCheckpoint saved)
        { return saved.Session == Session && saved.Signature == BindingEditSignature && Session.MatchesDraft(saved.Draft); }

        void RememberBindingEdit()
        {
            if (Session == null || restoringEdit || IsApplying) return;
            string signature = BindingEditSignature;
            if (bindingHistory.Count > 0 && MatchesBindingEdit(bindingHistory.Peek())) return;
            bindingHistory.Push(new BindingCheckpoint { Session = Session, Draft = Session.CaptureDraft(), Configuration = keyEnhancement.Clone(),
                Resource = resourceModDraft, Uninstall = keyEnhancementUninstall, Signature = signature });
        }

        void DiscardUnchangedBindingEdit()
        {
            if (Session != null && bindingHistory.Count > 0 && MatchesBindingEdit(bindingHistory.Peek())) bindingHistory.Pop();
        }

        void UndoBindingEdit()
        {
            while (bindingHistory.Count > 0 && bindingHistory.Peek().Session != Session) bindingHistory.Pop();
            if (bindingHistory.Count == 0)
            { bool reset = Session.IsResetPending; Session.Undo(); resourceModDraft = null; UndoKeyEnhancement(reset); return; }
            var saved = bindingHistory.Pop(); restoringEdit = true;
            try { Session.RestoreDraft(saved.Draft); keyEnhancement.ReplaceWith(saved.Configuration); resourceModDraft = saved.Resource; keyEnhancementUninstall = saved.Uninstall; }
            finally { restoringEdit = false; }
        }

        void BuildEditingSupport()
        {
            companionHotkeyEnabled = preferences.LoadCompanionHotkey();
            feedbackPanel = new NoticeCard { Dock = DockStyle.Top, Height = 44, Visible = false, Padding = new Padding(52, 6, 12, 6) };
            feedbackDetails = UnifiedBindingUi.Button(I18n.Str("feedback.details"), "feedbackDetails");
            feedbackDetails.ForeColor = NoticeCard.Accent;
            feedbackDetails.Click += delegate { ShowFullFeedback(); };
            var dismiss = UnifiedBindingUi.Button("×", "dismissFeedback"); dismiss.AccessibleName = I18n.Str("editor.close"); dismiss.MinimumSize = new Size(34, 30); dismiss.Click += delegate { feedbackDismissed = true; feedbackPanel.Visible = false; RenderGameStatus(); };
            feedbackText = new Label { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent, ForeColor = Color.FromArgb(255, 222, 157), UseMnemonic = false };
            feedbackPanel.Controls.Add(feedbackText); feedbackPanel.Controls.Add(feedbackDetails); feedbackPanel.Controls.Add(dismiss);
            feedbackPanel.Layout += delegate {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                int closeWidth = (int)(30 * scale), detailsWidth = Math.Max((int)(84 * scale), feedbackDetails.PreferredSize.Width);
                dismiss.SetBounds(feedbackPanel.Width - (int)(12 * scale) - closeWidth, (feedbackPanel.Height - closeWidth) / 2, closeWidth, closeWidth);
                feedbackDetails.SetBounds(dismiss.Left - (int)(10 * scale) - detailsWidth, (feedbackPanel.Height - (int)(34 * scale)) / 2, detailsWidth, (int)(34 * scale));
                feedbackText.SetBounds((int)(52 * scale), (int)(10 * scale), Math.Max(1, feedbackDetails.Left - (int)(64 * scale)), feedbackPanel.Height - (int)(20 * scale));
            };
            body.Controls.Add(feedbackPanel); body.Controls.SetChildIndex(bindingWorkspace, 0); body.Controls.SetChildIndex(feedbackPanel, 1);
            var agent = new EditorButton { Name = "agentHelp", Text = "AI / CLI", Width = 100, Height = 34, Top = 5, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            agent.Click += delegate { ShowAgentHelp(); };
            SetTip(agent, I18n.Str("ai.title")); viewBar.Controls.Add(agent);
            viewBar.Layout += delegate {
                float scale = CurrentAutoScaleDimensions.Width / 96f;
                agent.SetBounds(viewBar.Width - (int)(290 * scale), (int)(5 * scale), (int)(100 * scale), (int)(34 * scale));
            };
            lblStatus.Cursor = Cursors.Hand; lblStatus.Click += delegate { if (messageError) ShowFullFeedback(); else ShowFeedbackDialog(lblStatus.Text, I18n.Str("feedback.title")); };
            lblGame.Cursor = Cursors.Hand; lblGame.Click += delegate { ShowLiveConnectionHelp(); };
            HandleCreated += delegate {
                RefreshCompanionHotkey();
            };
            HandleDestroyed += delegate { if (hotkeyRegistered) UnregisterHotKey(Handle, 0x5042); hotkeyRegistered = false; };
        }

        void ShowFullFeedback()
        { ShowFeedbackDialog(fullFeedback ?? lblStatus.Text, I18n.Str("feedback.title")); }

        void ShowFeedbackDialog(string content, string title)
        {
            bool wasModal = modalActive; modalActive = true;
            try
            {
                using (var dialog = new EditorWindow())
                {
                    UnifiedBindingUi.Form(dialog, title, new Size(760, 480), new Size(480, 300));
                    var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                        Text = content, BackColor = EditorTheme.Surface, ForeColor = EditorTheme.Ink, BorderStyle = BorderStyle.None };
                    var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
                    var copy = UnifiedBindingUi.Button(I18n.Str("feedback.copy"), "copyFeedback"); copy.Click += delegate { Clipboard.SetText(text.Text); };
                    var close = UnifiedBindingUi.Button(I18n.Str("editor.close"), "close"); close.Click += delegate { dialog.Close(); };
                    buttons.Controls.Add(close); buttons.Controls.Add(copy); dialog.Controls.Add(text); dialog.Controls.Add(buttons);
                    Form owner = this;
                    while (owner.OwnedForms.Any(window => window.Visible)) owner = owner.OwnedForms.Last(window => window.Visible);
                    dialog.ShowDialog(owner);
                }
            }
            finally { if (wasModal) modalActive = true; else EndModal(); }
        }

        void ShowAgentHelp()
        {
            modalActive = true;
            try
            {
                string source = Session == null ? "new" : Session.InputPath;
                string snapshot = null;
                if (Session != null && (Session.IsOffline || Session.HasAnyDrafts || KeyEnhancementPending))
                {
                    string directory = Path.Combine(Path.GetTempPath(), "PBZKeyForge-Agent", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory); snapshot = Path.Combine(directory, "current-bindings.zip");
                    new BindingDocument { Session = Session, Configuration = keyEnhancement }.Export(snapshot);
                }
                string device = gamepad && Session != null ? Session.ControllerFamilies.First(item => item.Index == SelectedFamilyIndex).Name : "keyboard";
                string prompt = AgentPrompt.Create(AppDomain.CurrentDomain.BaseDirectory, source, snapshot,
                    Session == null ? null : Session.SelectedProfile, Session == null ? null : Session.ActiveProfile,
                    device, Session != null && !Session.IsOffline && EditorSession.IsGameRunning, keyEnhancement.Enabled);
                using (var dialog = new AgentPromptDialog(prompt)) dialog.ShowDialog(this);
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally { EndModal(); RenderSession(); }
        }

        void ManageCustomFunctions()
        {
            if (currentBindings == null) return;
            modalActive = true;
            try
            {
                using (var dialog = new AdvancedFunctionDialog(keyEnhancement))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        TryAction(delegate {
                            if (dialog.DeleteHook) keyEnhancement.Hooks.RemoveAt(dialog.HookIndex);
                            else if (dialog.ResultHook != null) currentBindings.SetHook(dialog.HookIndex, dialog.ResultHook);
                            else currentBindings.AddCustomDefinition(dialog.ResultTarget, dialog.FunctionName);
                        });
            }
            finally { EndModal(); }
        }

        internal void SetCompanionRecording(bool recording)
        {
            companionRecording = recording;
            RefreshCompanionHotkey();
        }

        static bool ConflictsWithCompanionHotkey(KeyEnhancementConfiguration configuration)
        {
            return configuration.Enabled && configuration.Rules.Where(rule => rule.Enabled).Any(rule =>
                (rule.Trigger == "sequence" ? rule.Steps : new List<string[]> { rule.Keys.Concat(rule.Watch).ToArray() })
                .Any(keys => keys.Contains("K") && keys.Any(key => key == "LeftControl" || key == "RightControl")
                    && keys.Any(key => key == "LeftAlt" || key == "RightAlt")));
        }

        void RefreshCompanionHotkey()
        {
            bool conflict = ConflictsWithCompanionHotkey(keyEnhancement) || ConflictsWithCompanionHotkey(keyEnhancementBaseline);
            if (conflict && !companionHotkeyConflict && companionHotkeyEnabled && !messageError)
                SetMessage("live.hotkeyconflict", false);
            companionHotkeyConflict = conflict;
            if (!IsHandleCreated || IsDisposed) return;
            bool wanted = companionHotkeyEnabled && !companionRecording && !conflict;
            if (!wanted && hotkeyRegistered) { UnregisterHotKey(Handle, 0x5042); hotkeyRegistered = false; }
            else if (wanted && !hotkeyRegistered)
            {
                hotkeyRegistered = RegisterHotKey(Handle, 0x5042, 0x4003, (uint)Keys.K);
                if (!hotkeyRegistered && !messageError) SetMessage("live.hotkeybusy", false);
            }
        }

        void ToggleCompanion()
        {
            if (companionVisible && WindowState != FormWindowState.Minimized && Visible)
            { companionVisible = false; TopMost = false; WindowState = FormWindowState.Minimized;
                if (companionReturnWindow != IntPtr.Zero && companionReturnWindow != Handle) SetForegroundWindow(companionReturnWindow);
                return; }
            companionReturnWindow = GetForegroundWindow();
            companionVisible = true; TopMost = true;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show(); Activate(); SetForegroundWindow(Handle);
            if (messageKey == null) SetMessage("live.overlayhelp", false);
            RenderSession();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z) && !modalActive && !companionRecording && CanUndoBinding
                && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown))
            { UndoFromBindingEditor(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x312 && message.WParam.ToInt32() == 0x5042) { ToggleCompanion(); return; }
            base.WndProc(ref message);
        }
    }
}
