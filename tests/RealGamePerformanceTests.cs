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

public static class RealGamePerformanceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object target, string name) { return target.GetType().GetField(name, Hidden).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, Hidden).SetValue(target, value); }
    static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
    static void Measure(string name, Action action)
    { var watch = Stopwatch.StartNew(); action(); Console.WriteLine("{0}: {1:F2} ms", name, watch.Elapsed.TotalMilliseconds); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Apply(MainForm form, string name)
    {
        int ticks = 0; long maximum = 0, previous = 0;
        var watch = Stopwatch.StartNew();
        var installation = (KeyEnhancementInstallation)Get(form, "keyEnhancementInstallation");
        using (var files = new FileSystemWatcher(installation.InstallDirectory))
        using (var timer = new System.Windows.Forms.Timer { Interval = 16 })
        {
            files.Changed += delegate(object sender, FileSystemEventArgs change) {
                if (change.Name == "live-request.id" || change.Name == "live-response.txt" || change.Name == "configuration.json")
                    Console.WriteLine("  {0} at {1} ms", change.Name, watch.ElapsedMilliseconds);
            };
            files.Created += delegate(object sender, FileSystemEventArgs change) {
                if (change.Name == "live-request.id" || change.Name == "live-response.txt" || change.Name == "configuration.json")
                    Console.WriteLine("  {0} at {1} ms", change.Name, watch.ElapsedMilliseconds);
            };
            files.EnableRaisingEvents = true;
            timer.Tick += delegate { long now = watch.ElapsedMilliseconds; maximum = Math.Max(maximum, now - previous); previous = now; ticks++; };
            timer.Start(); form.ApplyChanges();
            while (form.IsApplying && watch.ElapsedMilliseconds < 90000) { Application.DoEvents(); Thread.Sleep(1); }
            maximum = Math.Max(maximum, watch.ElapsedMilliseconds - previous);
        }
        Check(!form.IsApplying, "Apply timed out");
        Check(!(bool)Get(form, "messageError"), "Apply failed: " + Get(form, "fullFeedback"));
        Console.WriteLine("{0}: {1:F2} ms; UI max gap={2} ms; UI ticks={3}", name, watch.Elapsed.TotalMilliseconds, maximum, ticks);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Control.CheckForIllegalCrossThreadCalls = true;
        int result = 1;
        EventHandler run = null;
        run = delegate {
            Application.Idle -= run;
            result = Run(args);
            Application.ExitThread();
        };
        Application.Idle += run;
        Application.Run();
        return result;
    }

    static int Run(string[] args)
    {
        string game = null;
        KeyEnhancementConfiguration original = null;
        bool restoreRequired = false;
        int result = 1;
        try
        {
            I18n.Current = "zh-Hans";
            game = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            bool live = args.Contains("--live");
            Directory.CreateDirectory(output);
            var installation = new KeyEnhancementInstallation(game);
            original = installation.LoadConfiguration();
            Console.WriteLine("Original configuration characters=" + original.ToJson().Length);
            Check(original.Enabled, "Enable key enhancement before this measurement");
            Check(EditorSession.IsGameRunning == live, "Game running state does not match the measurement mode");
            string input = Path.Combine(game, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
            byte[] inputBytes = File.ReadAllBytes(input);
            string shared = Path.Combine(Path.GetDirectoryName(input), "PBZSharedSetting.sav");
            byte[] sharedBytes = File.ReadAllBytes(shared);
            string lua = Path.Combine(installation.InstallDirectory, @"Scripts\router-config.lua");
            byte[] jsonBytes = File.ReadAllBytes(installation.ConfigurationPath), luaBytes = File.ReadAllBytes(lua);
            File.WriteAllBytes(Path.Combine(output, "configuration.json"), jsonBytes);
            File.WriteAllBytes(Path.Combine(output, "router-config.lua"), luaBytes);
            restoreRequired = true;
            Measure("installation state inspection", delegate { Console.WriteLine("state=" + installation.ReadState()); });
            var preferences = new EditorPreferences(Path.Combine(output, "preferences.txt"));
            if (args.Contains("--no-effects")) preferences.SaveFlameEffects(false);
            using (var form = new MainForm(preferences))
            {
                Set(form, "modalActive", true);
                Measure("open real save and render", delegate { form.UiSmoke(input); form.ShowInTaskbar = false; form.Show(); Application.DoEvents(); });
                form.ShowActionView(true); Application.DoEvents();
                for (int i = 0; i < 3; i++) Measure("action view refresh " + i, form.RenderSession);
                var workspace = (BindingWorkspace)Get(form, "currentBindings");
                Measure("open add-binding editor", delegate {
                    using (var editor = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "F10", () => true))
                    {
                        editor.Shown += delegate { editor.BeginInvoke((Action)delegate { editor.Close(); }); };
                        editor.ShowDialog(form);
                    }
                });
                Measure("confirm enhanced binding and refresh", delegate {
                    var entry = workspace.Add("IA_Jump", new BindingEntry { Keys = new[] { "F10" }, Trigger = "hold" });
                    form.RenderSession(); workspace.Remove(entry); form.RenderSession();
                });
                for (int i = 0; i < 3; i++)
                {
                    var candidate = original.Clone();
                    var edits = new BindingWorkspace(form.Session, candidate, original, false, -1);
                    edits.Add("IA_Jump", new BindingEntry { Keys = new[] { "F10" }, Trigger = "hold", Seconds = .25 + i * .1 });
                    Set(form, "keyEnhancement", candidate); form.RenderSession();
                    Apply(form, (live ? "live" : "ordinary") + " apply " + i);
                }
                Set(form, "keyEnhancement", original.Clone()); form.RenderSession();
                Apply(form, (live ? "live" : "ordinary") + " restore");
                using (var image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "measured-ui.png")); }
                Check(File.ReadAllBytes(input).SequenceEqual(inputBytes), "Input save changed during enhancement-only apply");
                Check(File.ReadAllBytes(shared).SequenceEqual(sharedBytes), "Scheme selector changed during enhancement-only apply");
                Check(installation.LoadConfiguration().ToJson() == original.ToJson(), "Original enhancement configuration was not restored");
                form.Dispose();
            }
            Console.WriteLine("Real game installation measurements PASS; gameRunning=" + live);
            result = 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); }
        finally
        {
            if (restoreRequired)
            {
                try
                {
                    var installation = new KeyEnhancementInstallation(game);
                    if (installation.LoadConfiguration().ToJson() != original.ToJson())
                    {
                        if (EditorSession.IsGameRunning) installation.ApplyLive(original).GetAwaiter().GetResult();
                        else installation.Apply(original);
                        Check(installation.LoadConfiguration().ToJson() == original.ToJson(), "Configuration restoration failed");
                        Console.WriteLine("Original configuration restored after measurement");
                    }
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine("Configuration restoration failed; original JSON/Lua remain in the measurement directory: " + error.Message);
                    result = 1;
                }
            }
        }
        return result;
    }
}
