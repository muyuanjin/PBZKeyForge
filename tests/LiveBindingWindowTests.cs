using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

class LiveBindingWindowTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string ActionId = "move:Ability.Action.Hun.CombatArt.BronzeSword.Final";
    static int checks;
    static bool availabilityOnly;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Hidden).SetValue(owner, value); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (Control item in All(child)) yield return item; } }
    static void Click(Form form, string name)
    { All(form).OfType<Button>().Single(button => button.Name == name).PerformClick(); Application.DoEvents(); }
    static void Wait(Func<bool> completed, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!completed() && watch.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(10); }
        Check(completed(), message);
    }

    [STAThread] static int Main(string[] args)
    {
        if (args.Contains("--fixture")) { Console.WriteLine("ready"); Console.ReadLine(); return 0; }
        availabilityOnly = args.Contains("--apply-availability-only");
        try { Run(Path.GetFullPath(args[0])); Console.WriteLine("LiveBindingWindowTests PASS " + checks); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void Run(string output)
    {
        Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
        Check(!Directory.Exists(output), "Use a fresh test output directory.");
        string root = Path.Combine(output, "game");
        string runtime = Path.Combine(root, @"platform\steam");
        string save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
        Directory.CreateDirectory(runtime); Directory.CreateDirectory(Path.GetDirectoryName(save));
        File.Copy("samples/vanilla-base.sav", save);
        string shared = Path.Combine(Path.GetDirectoryName(save), "PBZSharedSetting.sav");
        File.WriteAllText(shared, "fixture, never edited");
        byte[] saveBytes = File.ReadAllBytes(save), sharedBytes = File.ReadAllBytes(shared);
        string fixture = Path.Combine(runtime, "PBZ.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, fixture);
        using (var process = Process.Start(new ProcessStartInfo(fixture, "--fixture") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true }))
        try
        {
            Check(process.StandardOutput.ReadLine() == "ready", "Game process fixture did not start.");
            Windows(output, root, save);
            Check(File.ReadAllBytes(save).SequenceEqual(saveBytes) && File.ReadAllBytes(shared).SequenceEqual(sharedBytes),
                "Live window operations changed game saves.");
        }
        finally { process.StandardInput.WriteLine(); process.WaitForExit(); }
    }

    static void Windows(string output, string root, string save)
    {
        var install = new KeyEnhancementInstallation(root);
        Directory.CreateDirectory(Path.Combine(install.InstallDirectory, "Scripts"));
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        using (var responder = new System.Windows.Forms.Timer { Interval = 50 })
        {
            form.UiSmoke(save); form.Show(); Application.DoEvents();
            Field<System.Windows.Forms.Timer>(form, "gameTimer").Stop();
            var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
            config.Enabled = true; config.EnsureSchemes(form.Session);
            var baseline = Field<KeyEnhancementConfiguration>(form, "keyEnhancementBaseline");
            baseline.ReplaceWith(config);
            File.WriteAllText(install.ConfigurationPath, config.ToJson());
            File.WriteAllText(Path.Combine(install.InstallDirectory, @"Scripts\router-config.lua"), config.CompileLua());
            File.WriteAllText(Path.Combine(install.InstallDirectory, "scheme-state.txt"), config.ActiveSchemeId);
            install.LoadConfiguration();
            Set(form, "keyEnhancementInstallation", install); Set(form, "keyEnhancementRepair", false); Set(form, "lastRunning", true);
            Set(form, "keyEnhancementState", KeyEnhancementInstallState.Enabled);
            string status = Path.Combine(install.InstallDirectory, "live-status.txt");
            Action heartbeat = delegate { File.WriteAllText(status, "PBZ-LIVE-1\nfixture\nready\n"); };
            heartbeat();
            if (availabilityOnly) { ApplyAvailability(form, config, baseline, install, status, output); return; }
            bool accept = true; int requests = 0; string seen = "";
            responder.Tick += delegate {
                heartbeat();
                string path = Path.Combine(install.InstallDirectory, "live-request.id");
                string id;
                try { id = File.Exists(path) ? File.ReadAllText(path) : ""; }
                catch (IOException) { return; }
                if (id == "" || id == seen || id.StartsWith("cancel-")) return;
                seen = id; requests++;
                File.WriteAllText(Path.Combine(install.InstallDirectory, "live-response.txt"), id + "\n" + (accept ? "ok" : "error") + "\nfixture rejection");
            };
            responder.Start(); form.RenderSession(); Application.DoEvents();
            var workspace = Field<BindingWorkspace>(form, "currentBindings");
            workspace.Add(ActionId, new BindingEntry { Keys = new[] { "G" }, Trigger = "press" });
            baseline.ReplaceWith(config);
            File.WriteAllText(install.ConfigurationPath, config.ToJson());
            File.WriteAllText(Path.Combine(install.InstallDirectory, @"Scripts\router-config.lua"), config.CompileLua());
            install.LoadConfiguration(); form.RenderSession();

            using (var dialog = OpenEditor(form))
            {
                Check(!dialog.Modal && Field<MainForm>(dialog, "directHost") == form, "Did not open the live modeless editor.");
                Click(dialog, "cancel");
                Closed(form, dialog, "Close button");
                Check(requests == 0, "Closing the editor submitted a live request.");
            }
            using (var dialog = OpenEditor(form))
            {
                Call(dialog, "ProcessDialogKey", Keys.Escape); Application.DoEvents();
                Closed(form, dialog, "Escape");
            }
            using (var dialog = OpenEditor(form))
            {
                dialog.captionClose.PerformClick(); Application.DoEvents();
                Closed(form, dialog, "Caption close");
            }
            using (var dialog = OpenEditor(form))
            {
                Click(dialog, "saveBinding");
                Closed(form, dialog, "Unchanged binding");
                Check(requests == 0, "An unchanged binding submitted a live request.");
            }
            form.WindowState = FormWindowState.Normal;
            using (var dialog = OpenEditor(form))
            {
                Field<NumericUpDown>(dialog, "damageMultiplier").Value = 2;
                Click(dialog, "saveBinding");
                Wait(() => dialog.IsDisposed, "Applied live editor did not close.");
                Closed(form, dialog, "Successful apply");
                Check(requests == 1 && config.Rules.Single().DamageMultiplier == 2 && baseline.Rules.Single().DamageMultiplier == 2,
                    "Successful apply did not accept the changed multiplier.");
                Check(!Field<bool>(form, "companionVisible"), "Successful apply did not request returning to the game.");
                Check(form.WindowState == FormWindowState.Normal && !form.TopMost,
                    "Returning to the game minimized the editor or left it above the game.");
            }
            form.WindowState = FormWindowState.Normal; accept = false;
            using (var dialog = OpenEditor(form))
            {
                Field<NumericUpDown>(dialog, "damageMultiplier").Value = 3;
                Click(dialog, "saveBinding");
                Wait(() => !form.IsApplying && dialog.Enabled, "Rejected apply did not finish.");
                Check(!dialog.IsDisposed && dialog.Visible && Field<bool>(form, "modalActive"), "Rejected apply closed the editor.");
                Check(config.Rules.Single().DamageMultiplier == 3 && baseline.Rules.Single().DamageMultiplier == 2,
                    "Rejected apply lost the draft or accepted it as saved.");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "rejected-binding.png")); }
                accept = true; Click(dialog, "saveBinding");
                Wait(() => dialog.IsDisposed, "Retried apply did not close the editor.");
                Closed(form, dialog, "Retried apply");
                Check(requests == 3 && baseline.Rules.Single().DamageMultiplier == 3, "Retry did not save the retained draft.");
            }
            form.WindowState = FormWindowState.Normal;
            Set(form, "keyEnhancementState", KeyEnhancementInstallState.Enabled); Set(form, "keyEnhancementRepair", false);
            Call(form, "ShowActionRelations", ActionId); Application.DoEvents();
            using (var details = form.OwnedForms.OfType<UnifiedActionBindingsDialog>().Single())
            {
                Click(details, "close"); Closed(form, details, "Action details return");
            }
            responder.Stop();
            ApplyAvailability(form, config, baseline, install, status, output);
        }
    }

    static void ApplyAvailability(MainForm form, KeyEnhancementConfiguration config, KeyEnhancementConfiguration baseline,
        KeyEnhancementInstallation install, string status, string output)
    {
        // This fixture supplies installation state directly; focus changes must not
        // replace it with a disk discovery while checking status-only rendering.
        Set(form, "loading", true);
        form.WindowState = FormWindowState.Normal;
        Set(form, "keyEnhancementState", KeyEnhancementInstallState.Enabled);
        Set(form, "keyEnhancementRepair", false);
        string scheme = Path.Combine(install.InstallDirectory, "scheme-state.txt");
        var apply = Field<Button>(form, "btnApply");
        Call(form, "RenderGameStatus");
        Check(!apply.Enabled, "An unchanged live configuration enabled apply.");
        int enabledTransitions = 0;
        EventHandler changed = delegate { if (apply.Enabled) enabledTransitions++; };
        apply.EnabledChanged += changed;
        foreach (string value in new[] { "", "PBZ.KeyProfile.", config.ActiveSchemeId })
        {
            File.WriteAllText(scheme, value);
            Call(form, "RenderGameStatus");
            Check(!apply.Enabled, "An incomplete or unchanged scheme report enabled apply: " + value);
        }
        File.Delete(scheme); Call(form, "RenderGameStatus");
        Check(!apply.Enabled, "A missing scheme report enabled apply: selected=" + config.SelectedSchemeId + " active=" + config.ActiveSchemeId
            + " baseline=" + baseline.ActiveSchemeId + " changed=" + (config.DeploymentJson() != baseline.DeploymentJson())
            + " repair=" + Field<bool>(form, "keyEnhancementRepair") + " state=" + Field<KeyEnhancementInstallState?>(form, "keyEnhancementState"));
        File.WriteAllText(scheme, config.ActiveSchemeId);
        using (var locked = new FileStream(scheme, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Call(form, "RenderGameStatus");
            Check(!apply.Enabled, "An unreadable scheme report enabled apply.");
        }
        Check(enabledTransitions == 0, "The clean apply button briefly enabled during status refresh.");
        apply.EnabledChanged -= changed;
        using (var bitmap = new Bitmap(apply.Width, apply.Height))
        { apply.DrawToBitmap(bitmap, apply.ClientRectangle); bitmap.Save(Path.Combine(output, "apply-clean.png")); }

        string original = config.ActiveSchemeId;
        string other = config.Schemes.First(item => item.Id != original).Id;
        File.WriteAllText(scheme, other); Call(form, "RenderGameStatus");
        Check(apply.Enabled && config.ActiveSchemeId == other && baseline.ActiveSchemeId == other,
            "A confirmed runtime scheme switch did not update the active scheme and enable switching back.");
        config.SelectScheme(other); baseline.SelectScheme(other); Call(form, "RenderGameStatus");
        Check(!apply.Enabled, "Selecting the confirmed active scheme left apply enabled.");
        File.WriteAllText(scheme, ""); Call(form, "RenderGameStatus");
        Check(!apply.Enabled && config.ActiveSchemeId == other, "A partial report discarded the confirmed runtime scheme.");

        config.RuntimeHotkeys = !config.RuntimeHotkeys; Call(form, "RenderGameStatus");
        Check(apply.Enabled, "A real configuration edit could not be applied with an incomplete scheme report.");
        using (var bitmap = new Bitmap(apply.Width, apply.Height))
        { apply.DrawToBitmap(bitmap, apply.ClientRectangle); bitmap.Save(Path.Combine(output, "apply-pending.png")); }
        config.RuntimeHotkeys = baseline.RuntimeHotkeys; Call(form, "RenderGameStatus");
        Check(!apply.Enabled, "Reverting the configuration edit left apply enabled.");

        File.WriteAllText(status, "PBZ-LIVE-1\nfixture\nsuspended\nfixture tuning error"); Call(form, "RenderGameStatus");
        Check(apply.Enabled, "A suspended runtime lost its apply-to-retry action.");
        File.WriteAllText(status, "PBZ-LIVE-1\nfixture\ndisabled\n"); Call(form, "RenderGameStatus");
        Check(apply.Enabled, "A disabled runtime lost its resume action.");
        File.WriteAllText(status, "PBZ-LIVE-1\nfixture\nready\n"); Call(form, "RenderGameStatus");
        Check(!apply.Enabled, "A recovered unchanged runtime left apply enabled.");
        config.SelectScheme(original); baseline.SelectScheme(original);
        File.WriteAllText(scheme, original); Call(form, "RenderGameStatus");
        Check(!apply.Enabled, "Returning to the original scheme left apply enabled.");
        Set(form, "keyEnhancementRepair", true);
        Set(form, "keyEnhancementState", KeyEnhancementInstallState.Incomplete);
        foreach (string state in new[] { "ready", "disabled", "suspended", "ready" })
        {
            File.WriteAllText(status, "PBZ-LIVE-1\nfixture\n" + state + "\nfixture detail"); Call(form, "RenderGameStatus");
            Check(!apply.Enabled && apply.Text == I18n.Str("enhancement.update.button"),
                "A pending Mod update changed its button label or enabled live apply: " + state);
        }
        Set(form, "lastRunning", false); Call(form, "RenderGameStatus");
        Check(apply.Enabled && apply.Text == I18n.Str("enhancement.update.button"), "A Mod update after game exit must have an explicit enabled action.");
        Call(form, "RefreshKeyEnhancement", false);
        Check(Field<Label>(form, "lblStatus").Text == I18n.Str("enhancement.update.hint")
            && Field<string>(Field<Control>(form, "keyEnhancementBar"), "Status") == I18n.Str("enhancement.update.hint"),
            "The Mod update must explain why applying is needed even without binding edits.");
        Check(Field<Label>(form, "lblScope").Text == I18n.Str("enhancement.update.button"), "A Mod update must not claim there are no pending changes.");
        Call(form, "SetMessage", "enhancement.partial", true, new object[] { "Mod 目录中有其他或被改动的文件，请先移走这些文件再重试。\nG:\\SteamLibrary\\steamapps\\common\\PBZ\\platform\\steam\\ue4ss\\Mods\\PBZInputRouter\\Scripts\\backend.lua" });
        foreach (Size size in new[] { new Size(1280, 900), form.MinimumSize })
        {
            form.Size = size; Call(form, "RenderGameStatus");
            var panel = Field<Panel>(form, "feedbackPanel");
            var footer = Field<Panel>(form, "footer");
            Check(panel.Visible && footer.ClientRectangle.Contains(panel.Bounds) && panel.Width > footer.Width * .9,
                "The error notice must occupy a separate full-width footer row: visible=" + panel.Visible + " footer=" + footer.ClientRectangle + " notice=" + panel.Bounds);
            foreach (string control in new[] { "btnApply", "lblScope", "chkActivate" })
                Check(!panel.Bounds.IntersectsWith(Field<Control>(form, control).Bounds), "The error notice overlaps " + control);
            Check(panel.Controls.Cast<Control>().All(item => panel.ClientRectangle.Contains(item.Bounds)), "Error notice actions are clipped.");
            using (var bitmap = new Bitmap(footer.Width, footer.Height))
            { footer.DrawToBitmap(bitmap, footer.ClientRectangle); bitmap.Save(Path.Combine(output, "mod-update-error-" + size.Width + ".png")); }
        }
        Set(form, "keyEnhancementState", KeyEnhancementInstallState.Conflict);
        Set(form, "keyEnhancementError", "foreign file conflict");
        foreach (bool running in new[] { false, true })
        {
            Set(form, "lastRunning", running); Call(form, "RenderGameStatus");
            Check(!apply.Enabled, "An unresolved installation conflict enabled apply.");
        }
        Set(form, "keyEnhancementRepair", false); Set(form, "keyEnhancementState", KeyEnhancementInstallState.Enabled);
        Set(form, "loading", false);
    }

    static UnifiedBindingEditDialog OpenEditor(MainForm form)
    {
        Field<System.Windows.Forms.Timer>(form, "gameTimer").Stop();
        Set(form, "keyEnhancementState", KeyEnhancementInstallState.Enabled); Set(form, "keyEnhancementRepair", false);
        var workspace = Field<BindingWorkspace>(form, "currentBindings");
        Call(form, "EditActionBinding", ActionId, workspace.Entries(ActionId).Single(item => item.Rule != null));
        Application.DoEvents();
        return form.OwnedForms.OfType<UnifiedBindingEditDialog>().Single();
    }
    static void Closed(MainForm form, Form dialog, string operation)
    {
        Check(dialog.IsDisposed && !form.OwnedForms.Contains(dialog), operation + " left the binding window open.");
        Check(!Field<bool>(form, "modalActive") && Field<Control>(form, "bindingWorkspace").Enabled,
            operation + " did not restore the main window controls.");
    }
}
