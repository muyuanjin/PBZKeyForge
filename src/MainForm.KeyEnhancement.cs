using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class KeyEnhancementBar : EditorPanel
    {
        public readonly CheckBox Toggle = new EditorCheckBox { CheckedColor = EditorTheme.KeyEnhancementStrong };
        public string Status;
        public bool Applied, Pending;
        bool compact;
        public bool Compact { get { return compact; } set { compact = value; Arrange(); Invalidate(); } }
        public event EventHandler Celebrated;
        Bitmap coloredMark, disabledMark;
        public KeyEnhancementBar()
        {
            DoubleBuffered = true; BackColor = EditorTheme.Surface;
            Toggle.AutoSize = false; Toggle.Text = ""; Toggle.BackColor = BackColor;
            Controls.Add(Toggle);
            Click += delegate { if (Toggle.Enabled) Toggle.Checked = !Toggle.Checked; };
            Cursor = Cursors.Hand;
            Resize += delegate { Arrange(); };
        }
        public void Celebrate() { if (Celebrated != null) Celebrated(this, EventArgs.Empty); }
        void Arrange()
        {
            float scale = DeviceDpiScale;
            int markSize = Math.Max(1, Math.Min(Height - (int)(4 * scale), (int)((Compact ? 28 : 64) * scale)));
            Toggle.SetBounds(markSize + (int)(4 * scale), (int)(4 * scale), (int)(22 * scale), (int)(26 * scale));
        }
        float DeviceDpiScale { get { using (var g = CreateGraphics()) return g.DpiX / 96f; } }

        void PrepareMark(int size)
        {
            if (coloredMark != null && coloredMark.Width == size) return;
            DisposeMarks();
            var source = EditorArtwork.KeyEnhancementMark;
            if (source == null) return;
            coloredMark = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(coloredMark))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            disabledMark = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(disabledMark))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[] {
                    new[] { .299f, .299f, .299f, 0, 0 },
                    new[] { .587f, .587f, .587f, 0, 0 },
                    new[] { .114f, .114f, .114f, 0, 0 },
                    new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 }
                }));
                graphics.DrawImage(coloredMark, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attributes);
            }
        }

        void DisposeMarks()
        {
            if (coloredMark != null) { coloredMark.Dispose(); coloredMark = null; }
            if (disabledMark != null) { disabledMark.Dispose(); disabledMark = null; }
        }

        protected override void Dispose(bool disposing)
        { if (disposing) DisposeMarks(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = e.Graphics.DpiX / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int markSize = Math.Max(1, Math.Min(Height - (int)(4 * scale), (int)((Compact ? 28 : 64) * scale)));
            int left = markSize + (int)(30 * scale), available = Math.Max(1, Width - left);
            PrepareMark(markSize);
            var mark = Toggle.Checked ? coloredMark : disabledMark;
            if (mark != null)
                e.Graphics.DrawImageUnscaled(mark, 0, (Height - markSize) / 2);
            using (var title = new Font(Font.FontFamily, 10f, FontStyle.Bold))
            using (var badgeFont = new Font(Font.FontFamily, 8f))
            {
                available = Math.Max(1, available - (int)(28 * scale));
                int gap = (int)(6 * scale);
                int badgeWidth = TextRenderer.MeasureText(e.Graphics, I18n.Str("mod.badge"), badgeFont).Width + (int)(8 * scale);
                int titleWidth = Math.Min(TextRenderer.MeasureText(e.Graphics, I18n.Str("enhancement.title"), title).Width,
                    Math.Max(1, available - badgeWidth - gap));
                var badge = new Rectangle(left + titleWidth + gap, (int)(7 * scale), badgeWidth, (int)(18 * scale));
                TextRenderer.DrawText(e.Graphics, I18n.Str("enhancement.title"), title,
                    new Rectangle(left, (int)(4 * scale), titleWidth, (int)(24 * scale)), Toggle.Checked ? EditorTheme.KeyEnhancement : EditorTheme.Muted,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                using (var shape = EditorArtwork.Rounded(badge, 3 * scale))
                using (var pen = new Pen(Color.FromArgb(90, EditorTheme.KeyEnhancement))) e.Graphics.DrawPath(pen, shape);
                TextRenderer.DrawText(e.Graphics, I18n.Str("mod.badge"), badgeFont, badge, EditorTheme.KeyEnhancement,
                    TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (!Compact) using (var detail = new Font(Font.FontFamily, 8.5f))
                TextRenderer.DrawText(e.Graphics, Status, detail,
                    new Rectangle(markSize + (int)(4 * scale), (int)(33 * scale), Math.Max(1, Width - markSize - (int)(4 * scale)), Math.Max(1, Height - (int)(33 * scale))), Pending ? BindingStatus.Pending : EditorTheme.Muted,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    public partial class MainForm
    {
        readonly KeyEnhancementBar keyEnhancementBar = new KeyEnhancementBar();
        KeyEnhancementFlameOverlay keyEnhancementFlame;
        KeyEnhancementConfiguration keyEnhancement = new KeyEnhancementConfiguration();
        KeyEnhancementConfiguration keyEnhancementBaseline = new KeyEnhancementConfiguration();
        KeyEnhancementInstallState? keyEnhancementState;
        KeyEnhancementConfiguration resetEnhancementCheckpoint;
        bool resetEnhancementUninstallCheckpoint;
        KeyEnhancementInstallation keyEnhancementInstallation;
        EditorSession keyEnhancementSession;
        string keyEnhancementError;
        bool renderingKeyEnhancement, keyEnhancementExternalChange, keyEnhancementUninstall;

        bool KeyEnhancementPending { get { return keyEnhancement.ToJson() != keyEnhancementBaseline.ToJson() || keyEnhancementRepair || keyEnhancementUninstall; } }
        bool HasKeyEnhancementConfiguration { get { return keyEnhancement.ToJson() != new KeyEnhancementConfiguration().ToJson(); } }
        bool keyEnhancementRepair;
        bool flameEffectsEnabled;
        FlameAppearance flameAppearance;
        int previousFlameTop;

        void BuildKeyEnhancement()
        {
            keyEnhancementFlame = new KeyEnhancementFlameOverlay(this);
            keyEnhancementFlame.FrameChanged += delegate { InvalidateFlameBackground(); };
            flameAppearance = preferences.LoadFlameAppearance();
            flameEffectsEnabled = flameAppearance.Enabled;
            keyEnhancementFlame.Appearance = flameAppearance;
            keyEnhancementBar.Celebrated += delegate { keyEnhancementFlame.Celebrate(); };
            keyEnhancementBar.Margin = Padding.Empty;
            keyEnhancementBar.Toggle.CheckedChanged += delegate
            {
                if (loading || renderingKeyEnhancement) return;
                RememberBindingEdit();
                keyEnhancement.Enabled = keyEnhancementBar.Toggle.Checked;
                UpdateKeyEnhancementEffects();
                if (keyEnhancement.Enabled) keyEnhancementUninstall = false;
                RenderSession();
            };
            footer.Controls.Add(keyEnhancementBar);
        }

        void RefreshKeyEnhancement(bool force = false)
        {
            if (keyEnhancementSession != Session || Session == null || !Session.IsResetPending) resetEnhancementCheckpoint = null;
            if (keyEnhancementSession != Session)
            {
                keyEnhancementSession = Session; keyEnhancementInstallation = null; keyEnhancementState = null; keyEnhancementError = null; keyEnhancementRepair = false; keyEnhancementExternalChange = false; keyEnhancementUninstall = false; keyEnhancementBar.Applied = false;
                keyEnhancement = new KeyEnhancementConfiguration(); keyEnhancementBaseline = keyEnhancement.Clone();
                if (Session != null && !Session.IsOffline)
                {
                    try
                    {
                        keyEnhancementInstallation = new KeyEnhancementInstallation(KeyEnhancementInstallation.FindGameDirectory(Session.InputPath));
                        keyEnhancement = keyEnhancementInstallation.LoadConfiguration(); keyEnhancementBaseline = keyEnhancement.Clone();
                    }
                    catch (FileNotFoundException) { keyEnhancementError = I18n.Str("mod.unsupported"); }
                    catch (Exception ex) { keyEnhancementError = ex.Message; }
                }
            }
            if (keyEnhancementInstallation != null)
            {
                try
                {
                    if (force && keyEnhancementInstallation.ConfigurationChanged)
                    {
                        if (KeyEnhancementPending) keyEnhancementExternalChange = true;
                        else { keyEnhancement = keyEnhancementInstallation.LoadConfiguration(); keyEnhancementBaseline = keyEnhancement.Clone(); keyEnhancementExternalChange = false; }
                    }
                    if (force || !keyEnhancementState.HasValue) keyEnhancementState = keyEnhancementInstallation.ReadState();
                    string state = keyEnhancementState.Value.ToString();
                    keyEnhancementRepair = keyEnhancement.Enabled && state == "Incomplete";
                    keyEnhancementBar.Applied = state == "Enabled";
                    keyEnhancementError = state == "Conflict" ? keyEnhancementInstallation.ConflictDetail ?? I18n.Str("mod.conflict") : null;
                }
                catch (Exception ex) { keyEnhancementError = ex.Message; }
            }
            renderingKeyEnhancement = true;
            try { keyEnhancementBar.Toggle.Checked = keyEnhancement.Enabled; }
            finally { renderingKeyEnhancement = false; }
            UpdateKeyEnhancementEffects();
            bool offline = Session != null && Session.IsOffline;
            if (keyEnhancement.Enabled && keyEnhancementError != null && !messageError) SetMessage("editor.failed", true, keyEnhancementError);
            keyEnhancementBar.Pending = KeyEnhancementPending && (keyEnhancement.Enabled || keyEnhancementBaseline.Enabled || keyEnhancementUninstall);
            string status = keyEnhancementUninstall ? I18n.Str("enhancement.lifecycle.willuninstall")
                : KeyEnhancementPending && !offline && lastRunning ? I18n.Str(keyEnhancement.Enabled && keyEnhancementBaseline.Enabled && !keyEnhancementUninstall
                    ? "live.unavailable"
                    : "enhancement.lifecycle.waitexit")
                : !keyEnhancement.Enabled ? I18n.Str(KeyEnhancementPending && keyEnhancementBaseline.Enabled ? "enhancement.willoff"
                    : keyEnhancementInstallation != null && File.Exists(keyEnhancementInstallation.ConfigurationPath) ? "enhancement.lifecycle.disabled" : "enhancement.description")
                : keyEnhancementExternalChange ? I18n.Str("enhancement.configchanged")
                : keyEnhancementError != null ? I18n.Str("enhancement.applyneeded")
                : offline ? I18n.Str("enhancement.description")
                : KeyEnhancementPending ? I18n.Str("enhancement.willon") : I18n.Str("enhancement.ready");
            keyEnhancementBar.Status = status;
            if (!offline && keyEnhancement.Enabled && keyEnhancementBaseline.Enabled && !keyEnhancementUninstall && lastRunning && keyEnhancementInstallation != null)
                keyEnhancementBar.Status = status = keyEnhancementInstallation.LiveStatus().Detail;
            if (!keyEnhancement.Enabled && !KeyEnhancementPending) keyEnhancementBar.Status = I18n.Str("ownership.coverage.off");
            keyEnhancementBar.Toggle.AccessibleName = I18n.Str("enhancement.title");
            btnKeyEnhancementMore.AccessibleName = I18n.Str("enhancement.title") + " · " + I18n.Str("component.options");
            keyEnhancementBar.Toggle.Enabled = Session != null && !IsApplying;
            string coverage = Session == null ? I18n.Str("ownership.coverage.scope")
                : UnifiedBindingUi.CoverageSummary(new BindingWorkspace(Session, keyEnhancement, keyEnhancementBaseline, gamepad, SelectedFamilyIndex));
            SetTip(keyEnhancementBar, status + Environment.NewLine + coverage);
            SetTip(keyEnhancementBar.Toggle, status + Environment.NewLine + coverage);
            keyEnhancementBar.Invalidate();
        }

        void UndoKeyEnhancement(bool undoReset)
        {
            if (undoReset && resetEnhancementCheckpoint != null)
            {
                keyEnhancement = resetEnhancementCheckpoint;
                keyEnhancementUninstall = resetEnhancementUninstallCheckpoint;
                resetEnhancementCheckpoint = null;
                return;
            }
            keyEnhancement = keyEnhancementBaseline.Clone(); keyEnhancementRepair = false; keyEnhancementUninstall = false;
            if (keyEnhancementExternalChange && keyEnhancementInstallation != null)
            {
                keyEnhancement = keyEnhancementInstallation.LoadConfiguration(); keyEnhancementBaseline = keyEnhancement.Clone(); keyEnhancementExternalChange = false;
            }
        }

        void ResetAllBindings()
        {
            RememberBindingEdit();
            Session.ResetAll();
            resetEnhancementCheckpoint = Session.IsResetPending ? keyEnhancement.Clone() : null;
            resetEnhancementUninstallCheckpoint = keyEnhancementUninstall;
            keyEnhancement = new KeyEnhancementConfiguration();
            keyEnhancementUninstall = false;
        }

        KeyEnhancementConfiguration presetEnhancementCheckpoint;
        EditorSession presetEnhancementSession;
        string presetEnhancementProfile, presetEnhancementPreview;
        bool presetEnhancementUninstallCheckpoint;

        BindingPresetPlan PlanBindingPreset(Preset preset, PresetApplyMode mode)
        {
            bool preview = presetEnhancementCheckpoint != null && presetEnhancementSession == Session
                && presetEnhancementProfile == Session.SelectedProfile && Session.PresetId != null;
            var source = Session;
            if (preview && (keyEnhancement.ToJson() != presetEnhancementPreview || keyEnhancementUninstall))
            { source = Session.CopyForPreset(); source.EndPreview(); preview = false; }
            return BindingPresetPlan.Create(source, preview ? presetEnhancementCheckpoint : keyEnhancement, preset, mode, SelectedFamilyIndex);
        }

        void StageBindingPreset(Preset preset, PresetApplyMode mode)
        {
            bool preview = presetEnhancementCheckpoint != null && presetEnhancementSession == Session
                && presetEnhancementProfile == Session.SelectedProfile && Session.PresetId != null;
            bool editedPreview = preview && (keyEnhancement.ToJson() != presetEnhancementPreview || keyEnhancementUninstall);
            if (editedPreview)
            {
                preview = false;
            }
            var checkpoint = preview ? presetEnhancementCheckpoint : keyEnhancement.Clone();
            bool uninstallCheckpoint = preview ? presetEnhancementUninstallCheckpoint : keyEnhancementUninstall;
            if (preset == null)
            {
                if (editedPreview) Session.EndPreview();
                Session.SelectPreset(null, mode);
                if (preview) { keyEnhancement = checkpoint.Clone(); keyEnhancementUninstall = uninstallCheckpoint; }
                presetEnhancementCheckpoint = null;
                return;
            }
            var plan = PlanBindingPreset(preset, mode);
            RememberBindingEdit();
            plan.Apply(Session);
            keyEnhancement = plan.Configuration; keyEnhancementUninstall = false;
            presetEnhancementCheckpoint = checkpoint; presetEnhancementSession = Session;
            presetEnhancementProfile = Session.SelectedProfile; presetEnhancementPreview = keyEnhancement.ToJson();
            presetEnhancementUninstallCheckpoint = uninstallCheckpoint;
        }

        internal static Action<string, string, string> PresetOverflow(KeyEnhancementConfiguration planned)
        { return BindingPresetPlan.Overflow(planned); }

        void UpdateKeyEnhancementEffects()
        {
            keyEnhancementFlame.FlameEnabled = keyEnhancement.Enabled && flameEffectsEnabled;
        }

        void PreviewAppearance(FlameAppearance value)
        {
            flameAppearance = value.Clone(); flameEffectsEnabled = value.Enabled;
            keyEnhancementFlame.Appearance = value;
            UpdateKeyEnhancementEffects();
        }

        void ShowAppearanceSettings()
        {
            var original = flameAppearance.Clone(); bool accepted = false;
            modalActive = true;
            try
            {
                using (var dialog = new AppearanceDialog(original, PreviewAppearance))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        preferences.SaveFlameAppearance(dialog.Value);
                        PreviewAppearance(dialog.Value); accepted = true;
                    }
            }
            finally { if (!accepted) PreviewAppearance(original); EndModal(); }
        }

        internal void PaintFlameBackground(Control surface, Graphics graphics)
        {
            if (keyEnhancementFlame != null) keyEnhancementFlame.PaintBackground(surface, graphics);
            if (keyEnhancement.Enabled && flameEffectsEnabled && (surface == footer || footer.Contains(surface)))
                using (var brush = new SolidBrush(Color.FromArgb(180, EditorTheme.Surface))) graphics.FillRectangle(brush, surface.ClientRectangle);
        }

        void InvalidateFlameBackground()
        {
            if (IsDisposed || !IsHandleCreated) return;
            int rim = 50, nextTop = keyEnhancementFlame.PaintTop, top = Math.Min(previousFlameTop, nextTop);
            previousFlameTop = nextTop;
            var bands = new[] {
                new Rectangle(0, top, ClientSize.Width, ClientSize.Height - top),
                new Rectangle(0, 0, ClientSize.Width, rim),
                new Rectangle(0, rim, rim, Math.Max(0, top - rim)),
                new Rectangle(Math.Max(0, ClientSize.Width - rim), rim, rim, Math.Max(0, top - rim))
            };
            InvalidateFlameSurface(this, bands);
        }

        void InvalidateFlameSurface(Control surface, Rectangle[] bands)
        {
            if (!surface.Visible || !surface.IsHandleCreated) return;
            if (!(surface is Button) && !(surface is TextBoxBase) && !(surface is ComboBox))
            {
                Point offset = PointToClient(surface.PointToScreen(Point.Empty));
                foreach (var band in bands)
                {
                    var local = band; local.Offset(-offset.X, -offset.Y);
                    local.Intersect(surface.ClientRectangle);
                    if (local.Width <= 0 || local.Height <= 0) continue;
                    var bounds = new FlamePaintRectangle { Left = local.Left, Top = local.Top, Right = local.Right, Bottom = local.Bottom };
                    // Control.Invalidate propagates a region's bounding rectangle to transparent
                    // descendants. Submit each surface's actual bands without that propagation.
                    InvalidateRect(surface.Handle, ref bounds, true);
                }
            }
            foreach (Control child in surface.Controls) InvalidateFlameSurface(child, bands);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct FlamePaintRectangle { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr window, ref FlamePaintRectangle rectangle, bool erase);

        void ExportKeyEnhancementBundle(string path)
        { new BindingDocument { Session = Session, Configuration = keyEnhancement }.Export(path); }

        static void WriteBundleEntry(ZipArchive zip, string name, byte[] bytes)
        { using (var writer = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) writer.Write(bytes, 0, bytes.Length); }

        void ImportKeyEnhancementBundle(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                byte[] save;
                var input = zip.GetEntry("EnhancedInputUserSettings.sav");
                var config = zip.GetEntry("key-enhancement.json");
                if (input == null || config == null) throw new InvalidDataException(I18n.Str("enhancement.invalid", Path.GetFileName(path)));
                using (var reader = input.Open()) using (var copy = new MemoryStream()) { reader.CopyTo(copy); save = copy.ToArray(); }
                KeyEnhancementConfiguration imported;
                using (var reader = new StreamReader(config.Open(), Encoding.UTF8)) imported = KeyEnhancementConfiguration.FromJson(reader.ReadToEnd());
                imported.CompileLua();
                var schemeEntry = zip.GetEntry("scheme.txt"); string profile = null;
                if (schemeEntry != null) using (var reader = new StreamReader(schemeEntry.Open(), Encoding.UTF8)) profile = reader.ReadToEnd();
                var defaultsEntry = zip.GetEntry("defaults.txt"); string build = "CL-301524";
                if (defaultsEntry != null) using (var reader = new StreamReader(defaultsEntry.Open(), Encoding.UTF8)) build = reader.ReadToEnd();
                var session = EditorSession.ImportOffline(save, build == "CL-302256" ? InputDefaultSet.Current : InputDefaultSet.Legacy, profile);
                Session = session; keyEnhancementSession = session; keyEnhancementInstallation = null; keyEnhancement = imported;
                keyEnhancementBaseline = new KeyEnhancementConfiguration(); keyEnhancementError = null; keyEnhancementRepair = false; keyEnhancementUninstall = false;
                UpdateGameState();
            }
        }
    }
}
