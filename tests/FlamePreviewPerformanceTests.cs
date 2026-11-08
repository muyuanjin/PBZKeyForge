using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class FlamePreviewPerformanceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Hidden).GetValue(owner); }

    sealed class PaintTiming : NativeWindow, IDisposable
    {
        internal readonly string Name;
        internal long Ticks { get; private set; }
        internal int Count { get; private set; }
        internal PaintTiming(Control control)
        { Name = control.GetType().Name + ":" + control.Name; AssignHandle(control.Handle); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg != 0xF) { base.WndProc(ref message); return; }
            long start = Stopwatch.GetTimestamp();
            base.WndProc(ref message);
            Ticks += Stopwatch.GetTimestamp() - start; Count++;
        }
        public void Dispose() { ReleaseHandle(); }
    }

    static void Attach(Control control, List<PaintTiming> timings)
    {
        if (!control.Visible) return;
        timings.Add(new PaintTiming(control));
        foreach (Control child in control.Controls) Attach(child, timings);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
        {
            typeof(MainForm).GetProperty("Session", Hidden).GetSetMethod(true).Invoke(form, new object[] { EditorSession.CreateOffline() });
            form.RenderSession(); form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(80, 70);
            form.Shown += delegate { form.BeginInvoke((Action)delegate {
                var overlay = Field(form, "keyEnhancementFlame");
                ((CheckBox)Field(Field(form, "keyEnhancementBar"), "Toggle")).Checked = true;
                var pauseProperty = overlay.GetType().GetProperty("AnimationPaused", Hidden);
                pauseProperty.SetValue(overlay, true, null);
                int pausedTick = (int)Field(overlay, "tick");
                var paused = Stopwatch.StartNew();
                while (paused.ElapsedMilliseconds < 120) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                if ((int)Field(overlay, "tick") != pausedTick || !(bool)overlay.GetType().GetProperty("IsActive", Hidden).GetValue(overlay, null))
                    throw new Exception("Pausing work must freeze the visible flame background.");
                pauseProperty.SetValue(overlay, false, null);
                SendMessage(form.Handle, 0x1C, IntPtr.Zero, IntPtr.Zero);
                int backgroundTick = (int)Field(overlay, "tick");
                var background = Stopwatch.StartNew();
                while (background.ElapsedMilliseconds < 220) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                if ((int)Field(overlay, "tick") != backgroundTick || Field(overlay, "pendingFrame") != null
                    || !(bool)overlay.GetType().GetProperty("IsActive", Hidden).GetValue(overlay, null))
                    throw new Exception("An inactive application must retain its flame background without rendering new frames.");
                using (var screenshot = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size)); screenshot.Save(Path.Combine(output, "background-paused.png")); }
                SendMessage(form.Handle, 0x1C, new IntPtr(1), IntPtr.Zero);
                Console.WriteLine("Background flame: zero animation ticks and no pending render.");
                foreach (bool actions in new[] { false, true })
                {
                    form.ShowActionView(actions);
                    foreach (int height in new[] { 30, 100 })
                    {
                        var settings = new FlameAppearance { Height = height };
                        overlay.GetType().GetProperty("Appearance", Hidden).SetValue(overlay, settings, null);
                        var timings = new List<PaintTiming>(); Attach(form, timings);
                        int frames = 0, beats = 0, step = 0; long lastBeat = 0, maxDelay = 0;
                        var watch = Stopwatch.StartNew();
                        EventHandler changed = delegate { frames++; };
                        var frameEvent = overlay.GetType().GetEvent("FrameChanged", Hidden);
                        frameEvent.GetAddMethod(true).Invoke(overlay, new object[] { changed });
                        using (var dialog = new AppearanceDialog(settings, value => overlay.GetType().GetProperty("Appearance", Hidden).SetValue(overlay, value, null)))
                        using (var timer = new Timer { Interval = 16 })
                        {
                            var slider = (TrackBar)Field(dialog, "hue");
                            timer.Tick += delegate {
                                long now = watch.ElapsedMilliseconds;
                                if (beats > 0) maxDelay = Math.Max(maxDelay, now - lastBeat);
                                beats++; lastBeat = now; slider.Value = -180 + (++step * 13) % 361;
                                if (now >= 2200) dialog.Close();
                            };
                            dialog.Shown += delegate { timer.Start(); };
                            dialog.ShowDialog(form);
                        }
                        watch.Stop(); frameEvent.GetRemoveMethod(true).Invoke(overlay, new object[] { changed });
                        timings.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
                        Console.WriteLine("{0} {1}x{2} height={3}% frames={4} elapsed={5}ms fps={6:F1} inputTicks={7} maxInputGap={8}ms",
                            actions ? "actions" : "keys", form.ClientSize.Width, form.ClientSize.Height, height, frames, watch.ElapsedMilliseconds,
                            frames * 1000.0 / watch.ElapsedMilliseconds, beats, maxDelay);
                        for (int index = 0; index < Math.Min(8, timings.Count); index++)
                            Console.WriteLine("  {0} paints={1} total={2:F1}ms", timings[index].Name, timings[index].Count, timings[index].Ticks * 1000.0 / Stopwatch.Frequency);
                        foreach (var timing in timings) timing.Dispose();
                        if (frames < 2 || beats < 5) throw new Exception("Live preview did not make progress.");
                    }
                }
                overlay.GetType().GetProperty("FlameEnabled", Hidden).SetValue(overlay, false, null);
                form.Hide(); Application.ExitThread();
            }); };
            Application.Run(form);
        }
        return 0;
    }
}
