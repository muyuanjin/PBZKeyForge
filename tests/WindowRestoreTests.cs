using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBZKeyForge;

internal static class WindowRestoreTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static IEnumerable<Control> All(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (Control item in All(child)) yield return item; } }
    static async Task Restore(MainForm form, FormWindowState state, string name)
    {
        form.WindowState = state;
        await Task.Delay(100);
        Rectangle bounds = form.Bounds;
        var controls = All(form).ToDictionary(control => control, control => control.Bounds);
        int moves = 0;
        EventHandler moved = delegate { moves++; };
        foreach (var control in controls.Keys) { control.SizeChanged += moved; control.LocationChanged += moved; }
        try
        {
            form.WindowState = FormWindowState.Minimized;
            await Task.Delay(100);
            Check(moves == 0, name + ": minimizing relaid out child controls " + moves + " times.");
            var watch = Stopwatch.StartNew();
            form.WindowState = state; form.Activate();
            await Task.Delay(1);
            form.Update();
            Console.WriteLine(name + ": restore=" + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
            Check(form.Bounds == bounds, name + ": restoring changed the window bounds.");
            Check(moves == 0, name + ": restoring relaid out unchanged child controls " + moves + " times.");
            Check(controls.All(pair => !pair.Key.IsDisposed && pair.Key.Bounds == pair.Value), name + ": child bounds changed.");
        }
        finally { foreach (var control in controls.Keys) { control.SizeChanged -= moved; control.LocationChanged -= moved; } }
    }
    static async Task Run(MainForm form, string output)
    {
        I18n.Current = "zh-Hans";
        typeof(MainForm).GetMethod("EnableKeyEnhancementEditing", Hidden).Invoke(form, null);
        form.RenderSession();
        await Task.Delay(200);
        for (int i = 0; i < 3; i++) await Restore(form, FormWindowState.Normal, "keys " + i);
        form.ShowActionView(true);
        await Restore(form, FormWindowState.Normal, "actions");
        await Restore(form, FormWindowState.Maximized, "maximized actions");
        form.WindowState = FormWindowState.Normal;
        await Task.Delay(100);
        form.ShowActionView(false); form.ShowGamepad(true);
        await Restore(form, FormWindowState.Normal, "controller");
        form.ShowGamepad(false);
        using (var image = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "restored.png")); }
        Size original = form.Size;
        var root = form.Controls.OfType<TableLayoutPanel>().Single();
        Size originalLayout = root.Size;
        form.Size = new Size(original.Width + 60, original.Height + 40);
        await Task.Delay(100);
        Check(root.Size == new Size(originalLayout.Width + 60, originalLayout.Height + 40), "Layout remained suspended after restoring.");
        await Restore(form, FormWindowState.Normal, "resized");
        Console.WriteLine("WindowRestoreTests PASS: " + checks + " checks");
    }
    [STAThread] public static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var preferences = new EditorPreferences(Path.Combine(output, "preferences.txt"));
        preferences.SaveCompanionHotkey(false);
        int result = 0;
        using (var form = new MainForm(preferences))
        {
            form.UiOffline();
            form.Shown += async delegate {
                try { await Run(form, output); }
                catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
                finally { form.Dispose(); Application.ExitThread(); }
            };
            Application.Run(form);
        }
        return result;
    }
}
