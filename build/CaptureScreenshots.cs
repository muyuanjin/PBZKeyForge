using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

internal static class ScreenshotEntry
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) {
                return new AssemblyName(request.Name).Name == "PBZKeyForge" ? Assembly.LoadFrom(Path.GetFullPath(args[0])) : null;
            };
            Assembly.LoadFrom(Path.GetFullPath(args[0]));
            return (int)typeof(ScreenshotEntry).Assembly.GetType("CaptureScreenshots").GetMethod("Run").Invoke(null, new object[] { args });
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; }
    }
}

internal static class CaptureScreenshots
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)]
    struct ComboInfo
    {
        public int Size;
        public Rectangle Item, Button;
        public int State;
        public IntPtr Combo, Edit, List;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr handle, ref ComboInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Point pointer = Cursor.Position;
        Directory.CreateDirectory(args[2]);
        using (var form = new MainForm(new EditorPreferences(Path.Combine(args[2], "capture-mode.txt"))))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            if (args.Length > 5 && !string.IsNullOrEmpty(args[5])) form.UiSmoke(args[5]);
            form.Show();
            try
            {
                if (form.Session == null) form.UiSmoke(Path.Combine(args[1], "samples", "vanilla-base.sav"));
                byte[] original = File.ReadAllBytes(form.Session.InputPath);
                var language = Field<ComboBox>(form, "cbLang");
                language.SelectedIndex = Array.IndexOf(I18n.Languages, args[3]);
                form.Session.SwitchProfile("PBZ.KeyProfile.Custom1");
                form.Session.StagePreset(Presets.ById(args[4]), PresetApplyMode.OnlyPreset);
                form.RenderSession();
                // Keep the public capture independent of personal preset names and paths.
                var picker = Field<ComboBox>(form, "cbPreset");
                for (int index = picker.Items.Count - 1; index >= 0; index--)
                    if (picker.Items[index] is Preset && ((Preset)picker.Items[index]).IsUser) picker.Items.RemoveAt(index);
                Field<Label>(form, "lblPath").Text = I18n.Str("editor.location", @"…\ProjectPBZ\Saved");
                Field<ToolTip>(form, "tip").RemoveAll();
                Field<Timer>(form, "gameTimer").Stop();
                var work = Screen.FromControl(form).WorkingArea;
                float scale;
                using (var graphics = form.CreateGraphics()) scale = graphics.DpiX / 96f;
                form.Size = new Size(Math.Min(work.Width - 24, (int)(1280 * scale)), Math.Min(work.Height - 24, (int)(840 * scale)));
                form.Location = new Point(work.Left + (work.Width - form.Width) / 2, work.Top + (work.Height - form.Height) / 2);
                if (!work.Contains(form.Bounds)) throw new InvalidOperationException("The desktop is too small for the editor. Use a larger display or lower Windows scaling.");
                form.TopMost = true; form.Activate(); form.Refresh();
                Cursor.Position = form.PointToScreen(new Point(16, 16));

                Exception failure = null;
                bool complete = false;
                var elapsed = Stopwatch.StartNew();
                using (var timer = new Timer { Interval = 80 })
                {
                    timer.Tick += delegate
                    {
                        try
                        {
                            if (elapsed.ElapsedMilliseconds > 5000) throw new InvalidOperationException("The preset dropdown did not become visible.");
                            var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
                            if (!picker.DroppedDown || !GetComboBoxInfo(picker.Handle, ref info) || !IsWindowVisible(info.List)) return;
                            // Start the list at its built-in header, independent of current selection.
                            SendMessage(info.List, 0x197, IntPtr.Zero, IntPtr.Zero);
                            NativeRect list;
                            if (!GetWindowRect(info.List, out list)) throw new InvalidOperationException("Cannot locate the expanded preset list.");
                            var bounds = Rectangle.Union(form.Bounds, Rectangle.FromLTRB(list.Left, list.Top, list.Right, list.Bottom));
                            if (!work.Contains(bounds)) throw new InvalidOperationException("The expanded preset list extends beyond the display.");
                            form.Update(); DwmFlush();
                            string path = Path.Combine(args[2], "main-" + args[3] + ".png");
                            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                            {
                                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                                bitmap.Save(path, ImageFormat.Png);
                            }
                            Console.WriteLine("Captured " + args[3] + ": " + path + " (preset dropdown open)");
                            complete = true; timer.Stop(); picker.DroppedDown = false;
                        }
                        catch (Exception ex) { failure = ex; complete = true; timer.Stop(); picker.DroppedDown = false; }
                    };
                    timer.Start();
                    var click = new IntPtr(((picker.Height / 2) << 16) | (picker.Width - 12));
                    PostMessage(picker.Handle, 0x201, new IntPtr(1), click);
                    PostMessage(picker.Handle, 0x202, IntPtr.Zero, click);
                    while (!complete) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                }
                if (failure != null) throw failure;
                if (!original.SequenceEqual(File.ReadAllBytes(form.Session.InputPath))) throw new IOException("The source save changed during capture.");
            }
            finally
            {
                Cursor.Position = pointer;
                if (form.Session != null) form.Session.Undo();
                form.Close();
            }
        }
        return 0;
    }
}
