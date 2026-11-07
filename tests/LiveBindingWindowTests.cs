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
        }
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
