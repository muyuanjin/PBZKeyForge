using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        Func<Ue4ssRelease> ue4ssReleaseCheck = Ue4ssRelease.CheckLatest;

        async Task<bool> CheckRuntimeUpdate(bool explicitCheck)
        {
            if (keyEnhancementInstallation == null) return true;
            var installation = keyEnhancementInstallation;
            Ue4ssRelease latest = null;
            lblStatus.Text = I18n.Str("ue4ss.checking");
            try { latest = await Task.Run(() => ue4ssReleaseCheck()); }
            catch (Exception ex)
            {
                if (!explicitCheck)
                {
                    modalActive = true;
                    try
                    {
                        var result = EditorDialogs.Show(this, ex.Message + "\n\n" + I18n.Str("ue4ss.offline"),
                            I18n.Str("ue4ss.title"), MessageBoxButtons.YesNoCancel);
                        if (result == DialogResult.Yes) return true;
                        if (result != DialogResult.No) return false;
                    }
                    finally { EndModal(); }
                }
            }
            Ue4ssRelease current = await Task.Run(() => installation.CurrentRuntimeRelease());
            if (!explicitCheck && latest != null && !latest.IsNewerThan(current)) return true;
            modalActive = true;
            try
            {
                using (var dialog = new Ue4ssUpdateDialog(current, latest))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return true;
                    string archive;
                    if (dialog.Manual)
                    {
                        using (var file = new OpenFileDialog { Title = I18n.Str("ue4ss.manual"), Filter = "UE4SS (*.zip)|*.zip", CheckFileExists = true })
                        {
                            if (file.ShowDialog(this) != DialogResult.OK) return false;
                            archive = file.FileName;
                        }
                        latest = new Ue4ssRelease { Provider = "Manual", Version = Path.GetFileNameWithoutExtension(archive),
                            Published = "", Source = "", Asset = Path.GetFileName(archive) };
                        archive = await Task.Run(() => {
                            Ue4ssRelease.ReadPackage(archive);
                            Directory.CreateDirectory(Ue4ssRelease.CacheDirectory);
                            string cached = Path.Combine(Ue4ssRelease.CacheDirectory, "manual-" + Guid.NewGuid().ToString("N") + ".zip");
                            File.Copy(archive, cached); return cached;
                        });
                    }
                    else
                    {
                        var progress = new Progress<string>(route => lblStatus.Text = I18n.Str("ue4ss.downloading", route));
                        try { archive = await Task.Run(() => latest.DownloadPackage(route => ((IProgress<string>)progress).Report(route))); }
                        catch (Exception ex)
                        {
                            EditorDialogs.Show(this, ex.Message, I18n.Str("ue4ss.title"), MessageBoxButtons.OK);
                            return false;
                        }
                    }
                    string backup = await Task.Run(() => installation.UpdateRuntime(archive, latest));
                    if (backup != null) EditorDialogs.Show(this, I18n.Str("ue4ss.updated", backup), I18n.Str("ue4ss.title"), MessageBoxButtons.OK);
                }
            }
            finally { EndModal(); }
            return true;
        }

        async void ManageRuntimeUpdate()
        {
            if (IsApplying || keyEnhancementInstallation == null) return;
            var disabled = new System.Collections.Generic.List<Control>();
            foreach (Control control in Controls) if (control != Caption && control.Enabled) { disabled.Add(control); control.Enabled = false; }
            IsApplying = true; gameTimer.Stop(); captionClose.Enabled = false;
            try { await CheckRuntimeUpdate(true); }
            catch (Exception ex) { SetMessage("editor.failed", true, ex.Message); }
            finally
            {
                IsApplying = false; captionClose.Enabled = true;
                foreach (Control control in disabled) control.Enabled = true;
                gameTimer.Start(); RefreshKeyEnhancement(true); RenderSession();
            }
        }
    }

    internal sealed class Ue4ssUpdateDialog : EditorWindow
    {
        public bool Manual { get; private set; }
        public Ue4ssUpdateDialog(Ue4ssRelease current, Ue4ssRelease latest)
        {
            UnifiedBindingUi.Form(this, I18n.Str("ue4ss.title"), new Size(730, 340), new Size(730, 340));
            AllowResize = false; MinimizeBox = false; MaximizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            string currentVersion = current == null ? I18n.Str("ue4ss.unknown") : current.Provider + " " + current.Version;
            var label = UnifiedBindingUi.Label(I18n.Str("ue4ss.offer", currentVersion, latest == null ? I18n.Str("ue4ss.unknown") : "KintaroModding " + latest.Version));
            label.Dock = DockStyle.Fill; label.AutoEllipsis = false; root.Controls.Add(label, 0, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var skip = UnifiedBindingUi.Button(I18n.Str("ue4ss.skip"), "skipUpdate"); skip.DialogResult = DialogResult.Cancel;
            var automatic = UnifiedBindingUi.Button(I18n.Str("ue4ss.automatic"), "downloadUpdate"); automatic.DialogResult = DialogResult.OK; automatic.Enabled = latest != null;
            var manual = UnifiedBindingUi.Button(I18n.Str("ue4ss.manual"), "manualUpdate"); manual.Click += delegate { Manual = true; DialogResult = DialogResult.OK; };
            var browser = UnifiedBindingUi.Button(I18n.Str("ue4ss.website"), "releaseWebsite");
            browser.Click += delegate { Process.Start(latest == null ? "https://github.com/KintaroModding/ue4ss/releases" : latest.Source); };
            foreach (Button button in new[] { skip, automatic, manual, browser }) { button.Width = 155; buttons.Controls.Add(button); }
            root.Controls.Add(buttons, 0, 1); Controls.Add(root); CancelButton = skip;
        }
    }
}
