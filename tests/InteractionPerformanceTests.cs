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
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
    static T Property<T>(object target, string name) { return (T)target.GetType().GetProperty(name, Hidden).GetValue(target, null); }
    static object Call(object target, string name, params object[] args)
    { return target.GetType().GetMethods(Hidden).Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args); }
    static object Call(object target, string name, int parameterCount, params object[] args)
    {
        return target.GetType().GetMethods(Hidden).Single(method => method.Name == name && method.GetParameters().Length == parameterCount).Invoke(target, args);
    }
    static double Measure(string name, int iterations, Action action)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        double ms = watch.Elapsed.TotalMilliseconds / iterations;
        Console.WriteLine("{0}: {1:F2} ms", name, ms);
        return ms;
    }
    static void Profile(MainForm form)
    {
        Measure("RefreshKeyEnhancement", 5, delegate { Call(form, "RefreshKeyEnhancement", false); });
        Measure("RenderBindingViews", 5, delegate { Call(form, "RenderBindingViews"); });
        Measure("RenderSchemeBar", 5, delegate { Call(form, "RenderSchemeBar"); });
        Measure("RefreshFooterResourceMod", 5, delegate { Call(form, "RefreshFooterResourceMod", false); });
        Measure("RenderGameStatus", 5, delegate { Call(form, "RenderGameStatus"); });
        Measure("LayoutFooter", 5, delegate { Call(form, "LayoutFooter"); });
        var session = form.Session;
        var scopes = new[] { (BindingWorkspace)Call(form, "ViewWorkspace", false, -1) }
            .Concat(session.ControllerFamilies.Select(family => (BindingWorkspace)Call(form, "ViewWorkspace", true, family.Index))).ToArray();
        object[] snapshots = null;
        string sourceSignature = null;
        Measure("binding edit signature", 5, delegate { sourceSignature = Property<string>(form, "BindingEditSignature"); });
        Measure("three cached snapshots", 5, delegate { snapshots = scopes.Select(workspace => Call(workspace, "SnapshotWithSource", sourceSignature)).ToArray(); });
        var snapshotArray = Array.CreateInstance(snapshots[0].GetType(), snapshots.Length);
        Array.Copy(snapshots, snapshotArray, snapshots.Length);
        var diff = Field<object>(form, "schemeDiff");
        Measure("scheme diff render", 5, delegate { Call(diff, "Render", 2, session, snapshotArray); });
        var keys = Field<object>(form, "keyBindings");
        Measure("key view render", 5, delegate { Call(keys, "Render", 2, session, snapshots[0]); });
        var workspacePanel = Field<Control>(form, "bindingWorkspace");
        Measure("binding workspace layout", 5, delegate { workspacePanel.PerformLayout(); });
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var session = EditorSession.CreateOffline();
        bool profile = args.Length > 1 && args[1] == "--profile";
        var config = args.Length > 1 && !profile ? KeyEnhancementConfiguration.FromJson(File.ReadAllText(args[1])) : new KeyEnhancementConfiguration { Enabled = true };
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
        var preferences = new EditorPreferences(Path.Combine(output, "preferences.txt"));
        if (Environment.GetEnvironmentVariable("PBZ_GUI_NO_EFFECTS") == "1") preferences.SaveFlameEffects(false);
        using (var form = new MainForm(preferences))
        {
            typeof(MainForm).GetProperty("Session", Hidden).GetSetMethod(true).Invoke(form, new object[] { session });
            form.RenderSession();
            typeof(MainForm).GetField("keyEnhancement", Hidden).SetValue(form, config);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            if (profile) Profile(form);
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
