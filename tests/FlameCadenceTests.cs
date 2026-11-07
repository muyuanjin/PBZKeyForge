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

public static class FlameCadenceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, null); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    static async Task Sample(MainForm form, object effect, string name, int inputInterval, bool measureOnly)
    {
        var times = new List<double>();
        var clock = Stopwatch.StartNew();
        EventHandler changed = delegate { times.Add(clock.Elapsed.TotalMilliseconds); };
        var frameEvent = effect.GetType().GetEvent("FrameChanged", Hidden);
        frameEvent.GetAddMethod(true).Invoke(effect, new object[] { changed });
        try
        {
            using (var mouse = new Timer { Interval = Math.Max(1, inputInterval) })
            {
                int position = 0;
                mouse.Tick += delegate { PostMessage(form.Handle, 0x200, IntPtr.Zero, new IntPtr((20 + position++ % 30) | (20 << 16))); };
                if (inputInterval > 0) mouse.Start();
                await Task.Delay(10000);
            }
        }
        finally { frameEvent.GetRemoveMethod(true).Invoke(effect, new object[] { changed }); }
        var gaps = times.Skip(1).Select((time, i) => time - times[i]).OrderBy(value => value).ToArray();
        double max = Math.Max(gaps.Length == 0 ? 10000 : gaps.Last(),
            times.Count == 0 ? 10000 : Math.Max(times[0], clock.Elapsed.TotalMilliseconds - times.Last()));
        Console.WriteLine(name + ": frames=" + times.Count + " max-gap=" + max.ToString("F1")
            + " p95=" + (gaps.Length == 0 ? 10000 : gaps[(int)((gaps.Length - 1) * .95)]).ToString("F1") + " ms");
        if (!measureOnly)
        {
            Check(times.Count >= 50, name + " stopped producing animation frames");
            Check(max < 180, name + " visibly stalled for " + max.ToString("F1") + " ms");
        }
    }

    static async Task Run(MainForm form, string output, bool measureOnly)
    {
        Call(form, "EnableKeyEnhancementEditing"); form.RenderSession();
        var effect = Field<object>(form, "keyEnhancementFlame");
        effect.GetType().GetProperty("FlameEnabled", Hidden).SetValue(effect, true, null);
        await Task.Delay(1000);
        await Sample(form, effect, "idle with status polling", 0, measureOnly);
        await Sample(form, effect, "mouse movement every two seconds", 2000, measureOnly);
        await Sample(form, effect, "continuous mouse movement", 50, measureOnly);
        typeof(EditorSession).GetProperty("InputPath").GetSetMethod(true).Invoke(form.Session, new object[] { Path.Combine(output, "fixture.sav") });
        File.Copy("samples/vanilla-base.sav", form.Session.InputPath, true);
        for (int i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew(); Call(form, "UpdateGameState");
            Console.WriteLine("status poll " + watch.ElapsedMilliseconds + " ms");
        }
        await Sample(form, effect, "closed game with status polling", 0, measureOnly);
        using (var shot = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size)); shot.Save(Path.Combine(output, "flames.png")); }
        Console.WriteLine(measureOnly ? "FlameCadenceTests measurements complete" : "FlameCadenceTests PASS");
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
            form.UiOffline(); I18n.Current = "zh-Hans"; form.TopMost = true;
            form.Shown += delegate { form.BeginInvoke((Action)(async delegate {
                try { await Run(form, output, args.Contains("--measure")); }
                catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
                finally { form.Dispose(); Application.ExitThread(); }
            })); };
            Application.Run(form);
        }
        return result;
    }
}
