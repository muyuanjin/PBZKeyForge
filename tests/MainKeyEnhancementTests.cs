using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class MainKeyEnhancementTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string JumpPath = "/PBZInput/InputActions/IA_Jump.IA_Jump";
    static int checks;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Fields).GetValue(owner); }
    static object Call(object owner, string name, params object[] arguments)
    { return owner.GetType().GetMethod(name, Fields).Invoke(owner, arguments); }
    static bool Pending(MainForm form)
    { return (bool)typeof(MainForm).GetProperty("KeyEnhancementPending", Fields).GetValue(form, null); }
    static KeyEnhancementConfiguration Configuration(MainForm form)
    { return Field<KeyEnhancementConfiguration>(form, "keyEnhancement"); }
    static void Toggle(CheckBox checkbox)
    { SendMessage(checkbox.Handle, 0xf5, IntPtr.Zero, IntPtr.Zero); Application.DoEvents(); }
    static void Apply(MainForm form)
    {
        var button = Field<Button>(form, "btnApply");
        Check(button.Enabled, "The pending operation cannot be applied.");
        button.PerformClick();
        var watch = Stopwatch.StartNew();
        while (form.IsApplying && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(1); }
        Check(!form.IsApplying, "Apply did not finish.");
        Application.DoEvents();
    }
    static KeyEnhancementRule Rule()
    {
        var rule = new KeyEnhancementRule { Id = "fixture-jump", Trigger = "press" };
        rule.Keys.Add("F9");
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "action", Path = JumpPath });
        return rule;
    }
    static IEnumerable<Control> All(Control owner)
    {
        foreach (Control control in owner.Controls)
        {
            yield return control;
            foreach (var child in All(control)) yield return child;
        }
    }
    static void Shot(MainForm form, string output, string name)
    {
        form.Refresh(); Application.DoEvents();
        var bar = Field<Control>(form, "keyEnhancementBar");
        foreach (var control in new[] { bar, Field<Control>(bar, "Toggle") })
        {
            var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Check(control.Visible && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= form.ClientSize.Width && bounds.Bottom <= form.ClientSize.Height,
                "The key enhancement bar control is clipped: " + control.GetType().Name + " " + bounds);
        }
        var toggle = Field<Control>(bar, "Toggle");
        Check(toggle.Left < bar.Width / 3, "The key enhancement switch must be on the left of its bar.");
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
    }
    static void CheckDisabledEntry(MainForm form)
    {
        var bar = Field<Control>(form, "keyEnhancementBar");
        Check(!Pending(form) && !Field<Button>(form, "btnApply").Enabled, "Disabled key enhancement must not stage an installation repair.");
        Check(All(bar).OfType<Button>().Single(button => button.Visible).Text == "…", "The mode bar must expose its component options.");
        Check(Field<string>(bar, "Status") == I18n.Str("ownership.coverage.off") && !Field<bool>(bar, "Pending"), "Disabled key enhancement does not explain native execution and inactive complex bindings.");
    }
    static byte[] EntryBytes(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Check(entry != null, "Offline export omits " + name);
        using (var input = entry.Open())
        using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Check(!EditorSession.IsGameRunning, "Close Phantom Blade Zero before running installation assertions.");
            string output = Path.GetFullPath(args[1]);
            if (Directory.Exists(output)) throw new Exception("Use a new output directory.");
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "游戏 fixture"), runtime = Path.Combine(root, @"platform\steam");
            Directory.CreateDirectory(runtime); File.WriteAllText(Path.Combine(runtime, "PBZ.exe"), "fixture, never launched");
            string save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(save)); File.Copy(args[0], save);
            byte[] original = File.ReadAllBytes(save);
            Exception failure = null;
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                form.ShowInTaskbar = false; form.TopMost = true; form.UiSmoke(save);
                typeof(MainForm).GetField("ue4ssReleaseCheck", Fields).SetValue(form, (Func<Ue4ssRelease>)(() => Ue4ssRelease.Bundled));
                form.Shown += delegate { form.BeginInvoke((Action)delegate {
                    try
                    {
                        I18n.Current = "zh-Hans"; form.RenderSession(); Application.DoEvents();
                        var bar = Field<Control>(form, "keyEnhancementBar");
                        var toggle = Field<CheckBox>(bar, "Toggle");
                        var apply = Field<Button>(form, "btnApply");
                        var installation = Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation");
                        Check(installation != null && !toggle.Checked && !Pending(form) && !apply.Enabled, "The initial main window is not clean and disabled.");
                        CheckDisabledEntry(form);
                        Field<Button>(form, "btnMore").PerformClick(); Application.DoEvents();
                        var topMenu = Field<ContextMenuStrip>(form, "moreMenu");
                        Check(topMenu.Items["enhancement.lifecycle.uninstall"] == null && topMenu.Items["editor.appearance"] == null, "Component commands remain in the window menu.");
                        topMenu.Close();
                        Field<Button>(form, "btnKeyEnhancementMore").PerformClick(); Application.DoEvents();
                        var enhancementMenu = Field<ContextMenuStrip>(form, "keyEnhancementMenu");
                        Check(enhancementMenu.Visible && enhancementMenu.Items["enhancement.flames"] != null && enhancementMenu.Items["component.openfolder"] != null, "Key enhancement component menu is missing.");
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                            bitmap.Save(Path.Combine(output, "enhancement-menu.png"));
                        }
                        enhancementMenu.Close();
                        bool appearanceOpened = false;
                        using (var closeAppearance = new System.Windows.Forms.Timer { Interval = 75 })
                        {
                            closeAppearance.Tick += delegate {
                                var dialog = Application.OpenForms.Cast<Form>().OfType<AppearanceDialog>().FirstOrDefault();
                                if (dialog == null) return;
                                closeAppearance.Stop(); appearanceOpened = true;
                                Check(dialog.Text == I18n.Str("enhancement.flames"), "Flame settings have an unrelated window title.");
                                dialog.DialogResult = DialogResult.Cancel;
                            };
                            closeAppearance.Start();
                            enhancementMenu.Items["enhancement.flames"].PerformClick();
                        }
                        Check(appearanceOpened && !Pending(form), "Flame settings did not open from the component menu or staged game changes.");
                        Field<Button>(form, "btnResourceModMore").PerformClick(); Application.DoEvents();
                        var resourceMenu = Field<ContextMenuStrip>(form, "resourceModMenu");
                        Check(resourceMenu.Visible && resourceMenu.Items["component.openfolder"] != null && resourceMenu.Items["component.uninstall"] != null, "Resource Mod component menu is missing.");
                        resourceMenu.Close();
                        Shot(form, output, "main-off");
                        Size regular = form.Size; form.Size = form.MinimumSize; Application.DoEvents();
                        Shot(form, output, "main-minimum"); form.Size = regular; Application.DoEvents();
                        Toggle(toggle);
                        Check(toggle.Checked && Pending(form) && apply.Enabled && !form.Session.HasChanges, "Enabling did not stage a key-enhancement-only draft.");
                        Check(!Directory.Exists(installation.InstallDirectory) && !File.Exists(Path.Combine(runtime, "dwmapi.dll")), "Toggling installed files before Apply.");
                        Configuration(form).Rules.Add(Rule()); form.RenderSession();
                        string staged = Configuration(form).ToJson();
                        form.SelectProfile(form.Session.Profiles.Last()); form.RefreshFromDisk();
                        Check(Configuration(form).ToJson() == staged && toggle.Checked, "Switching schemes or refreshing lost global key enhancement rules.");
                        Shot(form, output, "main-pending");
                        form.Session.SetKeyboard("IA_Jump", 0, "F7");
                        Configuration(form).CustomFunctions.Add(new KeyEnhancementOutput { Id = "reset-property", Name = "reset fixture", Kind = "property", Property = "bFlag", Value = true });
                        string beforeReset = Configuration(form).ToJson();
                        Call(form, "ResetAllBindings"); form.RenderSession();
                        Check(form.Session.IsResetPending && Configuration(form).ToJson() == new KeyEnhancementConfiguration().ToJson(), "Global reset did not stage factory bindings and enhancement configuration.");
                        Field<Button>(form, "btnUndo").PerformClick(); Application.DoEvents();
                        Check(!form.Session.IsResetPending && form.Session.GetKeyboard("IA_Jump", 0) == "F7", "Undo reset lost the ordinary binding draft.");
                        Check(Configuration(form).ToJson() == beforeReset && Pending(form) && toggle.Checked, "Undo reset lost the complete enhancement draft.");
                        Check(original.SequenceEqual(File.ReadAllBytes(save)) && !Directory.Exists(installation.InstallDirectory), "Reset and undo wrote game files before Apply.");
                        enhancementMenu.Items["enhancement.lifecycle.uninstall"].PerformClick();
                        string beforeUninstallReset = Configuration(form).ToJson();
                        Call(form, "ResetAllBindings"); form.RenderSession();
                        Field<Button>(form, "btnUndo").PerformClick(); Application.DoEvents();
                        Check(Field<bool>(form, "keyEnhancementUninstall") && Configuration(form).ToJson() == beforeUninstallReset,
                            "Undo reset lost the pending enhancement uninstall or disabled configuration.");
                        Field<Button>(form, "btnUndo").PerformClick(); Application.DoEvents();
                        Check(!Field<bool>(form, "keyEnhancementUninstall") && Configuration(form).ToJson() == beforeReset && toggle.Checked,
                            "Undo uninstall did not restore the preceding enhancement draft.");
                        Field<Button>(form, "btnUndo").PerformClick(); Application.DoEvents();
                        Check(!toggle.Checked && !Pending(form) && Configuration(form).Rules.Count == 0 && !apply.Enabled, "Undo did not restore the saved key enhancement configuration.");
                        Toggle(toggle); Configuration(form).Rules.Add(Rule()); form.RenderSession();
                        typeof(MainForm).GetField("ue4ssReleaseCheck", Fields).SetValue(form, (Func<Ue4ssRelease>)(() => { throw new IOException("fixture: network unavailable"); }));
                        bool offlineOffered = false;
                        using (var cancelOffline = new System.Windows.Forms.Timer { Interval = 50 })
                        {
                            cancelOffline.Tick += delegate {
                                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window != form);
                                if (dialog == null) return;
                                var cancel = All(dialog).OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.Cancel);
                                if (cancel == null) return;
                                offlineOffered = true; cancelOffline.Stop(); cancel.PerformClick();
                            };
                            cancelOffline.Start(); Apply(form);
                        }
                        Check(offlineOffered && Pending(form) && !Directory.Exists(installation.InstallDirectory) && original.SequenceEqual(File.ReadAllBytes(save)),
                            "Cancelling a failed update check changed the installation, save, or draft.");
                        var newerRelease = Ue4ssRelease.Bundled; newerRelease.Version = "test-new-release"; newerRelease.Published = "2099-01-01T00:00:00Z";
                        typeof(MainForm).GetField("ue4ssReleaseCheck", Fields).SetValue(form, (Func<Ue4ssRelease>)(() => newerRelease));
                        bool updateOffered = false;
                        using (var skipUpdate = new System.Windows.Forms.Timer { Interval = 50 })
                        {
                            skipUpdate.Tick += delegate {
                                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window.GetType().Name == "Ue4ssUpdateDialog");
                                if (dialog == null) return;
                                updateOffered = true; skipUpdate.Stop();
                                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                                {
                                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                    bitmap.Save(Path.Combine(output, "ue4ss-update.png"));
                                }
                                All(dialog).OfType<Button>().First(button => button.Name == "skipUpdate").PerformClick();
                            };
                            skipUpdate.Start(); Apply(form);
                        }
                        Check(updateOffered, "Apply did not ask before a newer UE4SS release.");
                        typeof(MainForm).GetField("ue4ssReleaseCheck", Fields).SetValue(form, (Func<Ue4ssRelease>)(() => Ue4ssRelease.Bundled));
                        Check(installation.ReadState() == KeyEnhancementInstallState.Enabled && !Pending(form) && !apply.Enabled,
                            "Apply did not finish installing key enhancement: " + Field<Label>(form, "lblStatus").Text);
                        Check(original.SequenceEqual(File.ReadAllBytes(save)), "A key-enhancement-only apply changed the input save.");
                        Check(Field<bool>(bar, "Applied") && Field<string>(bar, "Status") == I18n.Str("enhancement.ready"), "Applied key enhancement did not return to its normal editing state.");
                        Shot(form, output, "main-installed");
                        int repeatedUpdateChecks = 0;
                        typeof(MainForm).GetField("ue4ssReleaseCheck", Fields).SetValue(form, (Func<Ue4ssRelease>)(() => {
                            repeatedUpdateChecks++; return Ue4ssRelease.Bundled;
                        }));
                        Configuration(form).CustomLua = "\n";
                        form.RenderSession(); Apply(form);
                        Check(repeatedUpdateChecks == 0 && !Pending(form), "Editing installed bindings must not wait for an online runtime check.");
                        Toggle(toggle);
                        string foreign = Path.Combine(installation.InstallDirectory, "foreign.txt"); File.WriteAllText(foreign, "preserve");
                        form.Session.SetKeyboard("IA_Interact", 0, "F9"); form.RenderSession();
                        Apply(form);
                        Check(!form.Session.HasChanges && form.Session.GetKeyboard("IA_Interact", 0) == "F9", "The binding part of a partial apply was not saved.");
                        Check(Pending(form) && apply.Enabled && Field<string>(form, "messageKey") == "enhancement.partial", "Partial apply lost the remaining key enhancement draft or its outcome.");
                        Check(File.ReadAllText(foreign) == "preserve" && File.ReadAllText(Path.Combine(runtime, @"ue4ss\Mods\mods.txt")).Contains("PBZInputRouter : 1"), "Failed disable removed files or pretended to finish.");
                        byte[] committed = File.ReadAllBytes(save); DateTime committedTime = File.GetLastWriteTimeUtc(save);
                        File.Delete(foreign); form.RefreshFromDisk();
                        Check(Field<string>(form, "keyEnhancementError") == null && Pending(form), "Refreshing after resolving a conflict left a stale error or lost the draft.");
                        Apply(form);
                        Check(installation.ReadState() == KeyEnhancementInstallState.Disabled && !Pending(form)
                            && File.Exists(installation.ConfigurationPath) && installation.LoaderSuspended, "Disable did not retain configuration and suspend the standalone loader: "
                            + installation.ReadState() + "; pending=" + Pending(form) + "; " + Field<Label>(form, "lblStatus").Text);
                        Check(committed.SequenceEqual(File.ReadAllBytes(save)) && committedTime == File.GetLastWriteTimeUtc(save), "Retry rewrote an already committed input save.");
                        Check(Configuration(form).Rules.Count == 1, "Disable discarded saved rules.");
                        File.WriteAllText(Path.Combine(installation.InstallDirectory, "enabled.txt"), ""); form.RefreshFromDisk();
                        Check(!toggle.Checked && !Pending(form) && !apply.Enabled, "A disabled incomplete installation staged an unsolicited repair.");
                        Toggle(toggle);
                        Check(Pending(form) && apply.Enabled, "Explicitly enabling an incomplete installation did not allow repair.");
                        Apply(form);
                        Check(installation.ReadState() == KeyEnhancementInstallState.Enabled && !Pending(form), "Explicit enable did not repair the installation.");
                        Toggle(toggle); Apply(form);
                        Check(!File.Exists(Path.Combine(installation.InstallDirectory, "enabled.txt")) && !Pending(form), "Explicit disable left its activation marker.");
                        Field<Button>(form, "btnKeyEnhancementMore").PerformClick(); Application.DoEvents();
                        Field<ContextMenuStrip>(form, "keyEnhancementMenu").Items["enhancement.lifecycle.uninstall"].PerformClick();
                        Field<ContextMenuStrip>(form, "keyEnhancementMenu").Close();
                        Check(Pending(form) && Directory.Exists(installation.InstallDirectory), "Uninstall must stage until Apply.");
                        Apply(form);
                        Check(!Directory.Exists(installation.InstallDirectory) && !File.Exists(Path.Combine(runtime, "dwmapi.dll")) && Configuration(form).Rules.Count == 0, "Component uninstall did not clear the owned runtime and rules.");
                        form.NewOfflineScheme(); Application.DoEvents();
                        Check(form.Session.IsOffline && Field<KeyEnhancementInstallation>(form, "keyEnhancementInstallation") == null, "Offline editing retained an installation target.");
                        Toggle(toggle); Configuration(form).Rules.Add(Rule()); form.Session.SetKeyboard("IA_Jump", 0, "F11"); form.RenderSession();
                        Check(apply.Text == I18n.Str("enhancement.exportbundle"), "Offline editing does not advertise configuration export.");
                        string bundle = Path.Combine(output, "offline-config.zip"); Call(form, "ExportKeyEnhancementBundle", bundle);
                        using (var stream = File.OpenRead(bundle))
                        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                        {
                            byte[] saveBytes = EntryBytes(archive, "EnhancedInputUserSettings.sav");
                            string json = System.Text.Encoding.UTF8.GetString(EntryBytes(archive, "key-enhancement.json"));
                            string lua = System.Text.Encoding.UTF8.GetString(EntryBytes(archive, "router-config.lua"));
                            var exported = KeyEnhancementConfiguration.FromJson(json);
                            Check(exported.Enabled && exported.Rules.Count == 1 && lua == exported.CompileLua(), "Offline key enhancement data and generated Lua do not agree.");
                            Check(SaveFile.FromBytes(saveBytes).RecordFor(form.Session.SelectedProfile).Entries.Any(entry => entry.Name == "IA_Jump" && entry.Key == "F11"), "Offline bundle omitted its binding draft.");
                        }
                        Check(committed.SequenceEqual(File.ReadAllBytes(save)) && installation.ReadState() == KeyEnhancementInstallState.Disabled, "Offline export changed the earlier game installation.");
                        Shot(form, output, "main-offline");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally
                    {
                        form.Dispose();
                    }
                }); };
                Application.Run(form);
            }
            if (failure != null) throw new Exception("Main key enhancement UI assertion failed.", failure);
            Console.WriteLine("MainKeyEnhancementTests PASS: " + checks + " assertions; screenshots=" + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
