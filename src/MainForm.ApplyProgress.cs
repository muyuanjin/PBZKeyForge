using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class ApplyActionButton : EditorButton
    {
        readonly Timer animation = new Timer { Interval = 30 };
        readonly Stopwatch elapsed = new Stopwatch();
        int angle;
        bool busy, gameRunning, liveConnected;
        public bool LiveConnected
        {
            get { return liveConnected; }
            set { if (liveConnected != value) { liveConnected = value; Invalidate(); } }
        }
        public bool GameRunning
        {
            get { return gameRunning; }
            set { if (gameRunning != value) { gameRunning = value; Invalidate(); } }
        }
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value;
                if (value) { angle = 0; elapsed.Restart(); }
                else elapsed.Stop();
                animation.Enabled = value;
                Invalidate();
            }
        }
        public ApplyActionButton()
        {
            Primary = true;
            animation.Tick += delegate
            {
                if (elapsed.ElapsedMilliseconds < 150) return;
                angle = (angle + 12) % 360;
                Invalidate();
            };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!busy && !gameRunning && !liveConnected) { base.OnPaint(e); return; }
            float scale = e.Graphics.DpiX / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color warning = liveConnected ? EditorTheme.LiveConnected : Color.FromArgb(240, 178, 78);
            bool liveAction = liveConnected && (Enabled || busy);
            Color fill = liveAction ? !busy && Pressed ? Color.FromArgb(94, 201, 145)
                : !busy && Hovered ? ControlPaint.Light(EditorTheme.LiveConnected) : EditorTheme.LiveConnected
                : busy ? EditorTheme.Accent : liveConnected ? Color.FromArgb(31, 63, 48) : EditorTheme.Disabled;
            Color foreground = liveAction ? EditorTheme.Background : busy ? EditorTheme.Ink : warning;
            using (var shape = EditorArtwork.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5 * scale))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, shape);
                if (!busy && !liveAction) using (var pen = new Pen(warning, scale)) e.Graphics.DrawPath(pen, shape);
            }
            if (!busy || elapsed.ElapsedMilliseconds < 150)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }
            float diameter = 17 * scale, left = 16 * scale;
            using (var pen = new Pen(foreground, 2 * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                e.Graphics.DrawArc(pen, left, (Height - diameter) / 2f, diameter, diameter, angle, 265);
            }
            TextRenderer.DrawText(e.Graphics, I18n.Str("editor.applying"), Font,
                new Rectangle((int)(left + diameter + 10 * scale), 0, Math.Max(0, Width - (int)(left + diameter + 20 * scale)), Height),
                foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }

    public partial class MainForm
    {
        public bool IsApplying { get; private set; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        void SetApplyControls(Control[] controls, bool enabled)
        {
            keyEnhancementFlame.AnimationPaused = !enabled || modalActive;
            SendMessage(Handle, 0xB, IntPtr.Zero, IntPtr.Zero);
            try
            {
                foreach (var control in controls) control.Enabled = enabled;
                captionClose.Enabled = enabled;
            }
            finally
            {
                SendMessage(Handle, 0xB, new IntPtr(1), IntPtr.Zero);
                Invalidate(true);
            }
        }

        public async void ApplyChanges()
        { await ApplyChangesAsync(); }

        internal async Task<bool> ApplyChangesAsync()
        {
            if (Session == null || IsApplying) return false;
            if (Session.IsOffline)
            {
                modalActive = true;
                try
                {
                    bool bundle = HasKeyEnhancementConfiguration;
                    using (var dialog = new SaveFileDialog { Title = I18n.Str(bundle ? "enhancement.exportbundle" : "preset.export"),
                        Filter = bundle ? "PBZKeyForge (*.zip)|*.zip" : I18n.Str("preset.export") + " (*.sav)|*.sav",
                        FileName = bundle ? "PBZKeyForge.zip" : "EnhancedInputUserSettings.sav", DefaultExt = bundle ? "zip" : "sav", AddExtension = true })
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                        {
                            if (bundle) ExportKeyEnhancementBundle(dialog.FileName); else Session.ExportCurrentScheme(dialog.FileName);
                            SetMessage("preset.exported", false);
                            return true;
                        }
                }
                catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
                finally { EndModal(); RenderSession(); }
                return false;
            }
            if (EditorSession.IsGameRunning)
            {
                var liveControls = Controls.Cast<Control>().Where(control => control != Caption && control.Enabled).ToArray();
                IsApplying = true; ((ApplyActionButton)btnApply).Busy = true;
                gameTimer.Stop();
                SetApplyControls(liveControls, false);
                try
                {
                    if (keyEnhancementInstallation == null || !keyEnhancementBaseline.Enabled)
                        throw new InvalidOperationException(I18n.Str("live.unavailable"));
                    if (Session.IsResetPending) throw new InvalidOperationException(I18n.Str("live.lifecycle"));
                    if (EnhancedSchemes && keyEnhancementRepair) throw new InvalidOperationException(I18n.Str("live.lifecycle"));
                    int family = SelectedFamilyIndex;
                    bool enabled = keyEnhancement.Enabled;
                    var draft = keyEnhancement.Clone(); draft.SetEnabled(true);
                    if (draft.Schemes.Count == 0) await Task.Run(() => Session.Refresh());
                    var candidate = await Task.Run(async delegate {
                        var plan = BindingLivePlan.Create(Session, draft, family);
                        await keyEnhancementInstallation.ApplyLive(plan).ConfigureAwait(false);
                        return plan;
                    });
                    AcceptLiveBindings(candidate, enabled, family);
                    SetMessage(HasDeferredComponents ? "flow.saved.deferred" : "live.saved", false);
                    return true;
                }
                catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
                finally
                {
                    SetApplyControls(liveControls, true);
                    IsApplying = false;
                    RenderSession();
                    ((ApplyActionButton)btnApply).Busy = false;
                    gameTimer.Start();
                }
                return false;
            }
            var disabled = Controls.Cast<Control>().Where(control => control != Caption && control.Enabled).ToArray();
            IsApplying = true;
            gameTimer.Stop();
            cbPreset.DroppedDown = false;
            ((ApplyActionButton)btnApply).Busy = true;
            SetApplyControls(disabled, false);
            lblStatus.Text = I18n.Str("editor.applying");
            try
            {
                if (keyEnhancement.Enabled && !keyEnhancementUninstall && keyEnhancementInstallation != null && !keyEnhancementBaseline.Enabled
                    && await Task.Run(() => keyEnhancementInstallation.ReadLoaderState() == KeyEnhancementLoaderState.Missing))
                {
                    if (EditorSession.IsGameRunning) throw new InvalidOperationException(I18n.Str("mod.running"));
                    if (!await CheckRuntimeUpdate(false)) return false;
                }
                var errors = new List<string>();
                KeyEnhancementUninstallResult uninstallResult = null;
                bool saved = false;
                var result = await Task.Run(() => Session.HasChanges && !EnhancedSchemes ? Session.Apply() : new EditorApplyResult { Success = true });
                saved = result.BindingsSaved || result.ActivationSaved;
                if (!result.Success || result.PartialSuccess) errors.Add(result.Error);
                if (ResourceModPending)
                {
                    try
                    {
                        bool enabled = resourceModDraft.Value;
                        var target = footerMod;
                        await Task.Run(delegate { target.Apply(enabled); });
                        resourceModBaseline = enabled; resourceModDraft = null; saved = true;
                        footerModState = enabled ? ResourceModState.Enabled : ResourceModState.Disabled;
                    }
                    catch (Exception ex) { errors.Add(I18n.Str("mod.title") + ": " + ex.Message); }
                }
                if (KeyEnhancementPending || EnhancedSchemes && keyEnhancement.SelectedSchemeId != keyEnhancement.ActiveSchemeId)
                {
                    try
                    {
                        if (keyEnhancementInstallation == null) throw new InvalidOperationException(keyEnhancementError ?? I18n.Str("mod.unsupported"));
                        var configuration = keyEnhancement.Clone();
                        if (EnhancedSchemes) configuration.ActiveSchemeId = configuration.SelectedSchemeId;
                        if (keyEnhancementUninstall)
                        {
                            uninstallResult = await Task.Run(() => keyEnhancementInstallation.Uninstall());
                            keyEnhancementUninstall = false;
                            configuration = keyEnhancementInstallation.LoadConfiguration(); keyEnhancement = configuration;
                        }
                        else await Task.Run(delegate { keyEnhancementInstallation.Apply(configuration); });
                        keyEnhancement = configuration; keyEnhancementBaseline = configuration.Clone(); keyEnhancementRepair = false; keyEnhancementError = null;
                        if (configuration.Schemes.Count > 0) EndEnhancementPresetPreview();
                        keyEnhancementState = configuration.Enabled ? KeyEnhancementInstallState.Enabled : KeyEnhancementInstallState.Disabled;
                        keyEnhancementExternalChange = false; saved = true;
                        if (configuration.Enabled) keyEnhancementBar.Celebrate();
                    }
                    catch (Exception ex) { errors.Add(I18n.Str("enhancement.title") + ": " + ex.Message); }
                }
                if (errors.Count > 0) SetMessage(saved ? "enhancement.partial" : "editor.failed", true, string.Join("\n", errors.ToArray()));
                else if (uninstallResult != null) SetMessage(uninstallResult.Complete ? "enhancement.lifecycle.removed" : "enhancement.lifecycle.retained", false, uninstallResult.RetainedFiles.Length);
                else SetMessage("editor.saved", false);
                if (errors.Count == 0) bindingHistory.Clear();
                RefreshFooterResourceMod(); RefreshKeyEnhancement();
                return errors.Count == 0;
            }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally
            {
                IsApplying = false;
                ConsumeSchemeDiscovery();
                SetApplyControls(disabled, true);
                RenderSession();
                ((ApplyActionButton)btnApply).Busy = false;
                gameTimer.Start();
            }
            return false;
        }
    }
}
