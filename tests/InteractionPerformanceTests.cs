using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class InteractionPerformanceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
    static double Measure(string name, int iterations, Action action)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        double ms = watch.Elapsed.TotalMilliseconds / iterations;
        Console.WriteLine("{0}: {1:F2} ms", name, ms);
        return ms;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var session = EditorSession.CreateOffline();
        var config = args.Length > 1 ? KeyEnhancementConfiguration.FromJson(File.ReadAllText(args[1])) : new KeyEnhancementConfiguration { Enabled = true };
        var baseline = config.Clone();
        var workspace = new BindingWorkspace(session, config, baseline, false, -1);
        if (args.Length > 2)
        {
            string functionId = args[2];
            var binding = workspace.Entries(functionId).FirstOrDefault();
            var timer = Stopwatch.StartNew();
            using (var dialog = new UnifiedBindingEditDialog(workspace, functionId, binding, "G", () => true))
            {
                Console.WriteLine("observed editor constructor: {0:F2} ms", timer.Elapsed.TotalMilliseconds);
                timer.Restart(); dialog.Show(); Application.DoEvents();
                Console.WriteLine("observed editor first frame: {0:F2} ms", timer.Elapsed.TotalMilliseconds);
                using (var image = new Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "observed-editor.png")); }
            }
            return 0;
        }
        workspace.Add("IA_Jump", new BindingEntry { Keys = new[] { "J" }, Trigger = "hold" });
        Measure("workspace catalog", 5, delegate { var value = new BindingWorkspace(session, config, baseline, false, -1).Functions; });
        Measure("workspace snapshot", 5, delegate { Call(workspace, "Snapshot"); });
        UnifiedBindingEditDialog editor = null;
        Measure("binding editor create/dispose", 3, delegate { using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "K", () => true)) { } });
        var opening = Stopwatch.StartNew();
        editor = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "K", () => true);
        Console.WriteLine("binding editor constructor: {0:F2} ms", opening.Elapsed.TotalMilliseconds);
        opening.Restart(); editor.Show(); Application.DoEvents();
        Console.WriteLine("binding editor first frame: {0:F2} ms", opening.Elapsed.TotalMilliseconds);
        using (var image = new Bitmap(editor.Width, editor.Height))
        { editor.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "binding-editor.png")); }
        opening.Restart(); editor.Dispose();
        Console.WriteLine("binding editor disposal: {0:F2} ms", opening.Elapsed.TotalMilliseconds);
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        {
            typeof(MainForm).GetProperty("Session", Hidden).GetSetMethod(true).Invoke(form, new object[] { session });
            form.RenderSession();
            typeof(MainForm).GetField("keyEnhancement", Hidden).SetValue(form, config);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            Measure("key view refresh", 5, form.RenderSession);
            form.ShowActionView(true); Application.DoEvents();
            Measure("action view refresh", 5, form.RenderSession);
            Measure("add binding and refresh", 5, delegate {
                var current = (BindingWorkspace)typeof(MainForm).GetField("currentBindings", Hidden).GetValue(form);
                var added = current.Add("IA_Jump", new BindingEntry { Keys = new[] { "K" }, Trigger = "hold" });
                form.RenderSession(); current.Remove(added); form.RenderSession();
            });
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "interaction.png")); }
            form.Dispose();
        }
        return 0;
    }
}
