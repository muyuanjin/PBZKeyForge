using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class ApplyResponsivenessTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Assert(bool condition, string text) { checks++; if (!condition) throw new Exception(text); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
    static void PumpUntil(Func<bool> complete)
    {
        var elapsed = Stopwatch.StartNew();
        while (!complete() && elapsed.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(1); }
        Assert(complete(), "Timed out waiting for apply.");
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Assert(!EditorSession.IsGameRunning, "Close Phantom Blade Zero before running write tests.");
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav");
            File.Copy(args[0], input, true);
            byte[] original = File.ReadAllBytes(input);
            using (var form = new MainForm())
            using (var reached = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.UiSmoke(input); form.Show(); Application.DoEvents();
                I18n.Current = "zh-Hans";
                form.Session.SetKeyboard("IA_Jump", 0, "F9"); form.RenderSession();
                int writes = 0;
                typeof(EditorSession).GetField("BeforeInputReplace", Hidden).SetValue(form.Session, (Action)delegate {
                    Interlocked.Increment(ref writes); reached.Set();
                    if (!release.WaitOne(10000)) throw new IOException("Test writer was not released.");
                });
                var apply = Field<Button>(form, "btnApply");
                apply.PerformClick(); PumpUntil(() => reached.WaitOne(0));
                Assert(form.IsApplying && !apply.Enabled && !form.captionClose.Enabled, "Busy apply disables editing and close.");
                Assert((bool)apply.GetType().GetProperty("Busy").GetValue(apply, null), "Apply displays busy spinner.");
                int firstAngle = Field<int>(apply, "angle");
                PumpUntil(() => Field<int>(apply, "angle") != firstAngle);
                Assert(original.SequenceEqual(File.ReadAllBytes(input)), "Busy writer has not replaced input yet.");
                using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, form.ClientRectangle); image.Save(Path.Combine(output, "applying.png")); }
                form.ApplyChanges();
                Assert(writes == 1, "A second apply cannot start while busy.");
                form.Close(); Assert(!form.IsDisposed && form.Visible, "Close waits for apply to finish.");
                release.Set(); PumpUntil(() => !form.IsApplying);
                Assert(!form.Session.HasChanges && form.captionClose.Enabled, "Successful apply restores controls and clears draft.");
                Assert(!(bool)apply.GetType().GetProperty("Busy").GetValue(apply, null), "Spinner stops after completion.");
                Assert(EditorSession.Open(input).GetKeyboard("IA_Jump", 0) == "F9", "Applied key persisted.");

                form.Session.SetKeyboard("IA_Jump", 0, "F10"); form.RenderSession();
                typeof(EditorSession).GetField("BeforeInputReplace", Hidden).SetValue(form.Session, (Action)delegate { throw new IOException("Write interrupted."); });
                apply.PerformClick(); PumpUntil(() => !form.IsApplying);
                Assert(form.Session.HasChanges && apply.Enabled && form.captionClose.Enabled, "Failed apply restores controls and retains draft for retry.");
                Assert(Field<Label>(form, "lblStatus").Text.Contains("Write interrupted."), "Failed apply explains the error.");
                Assert(EditorSession.Open(input).GetKeyboard("IA_Jump", 0) == "F9", "Failed replacement preserves the saved key.");
                typeof(EditorSession).GetField("BeforeInputReplace", Hidden).SetValue(form.Session, null);
                apply.PerformClick(); PumpUntil(() => !form.IsApplying);
                Assert(!form.Session.HasChanges && EditorSession.Open(input).GetKeyboard("IA_Jump", 0) == "F10", "Retry saves the remaining draft.");
            }
            Console.WriteLine("PASS: " + checks + " apply responsiveness assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
