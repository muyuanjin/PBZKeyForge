using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

public static class PresetHoverPreviewTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
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
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr handle, ref Point point);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr handle, ref Point point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref NativeRect rectangle);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static IEnumerable<Control> Descendants(Control owner)
    {
        foreach (Control control in owner.Controls)
        { yield return control; foreach (Control child in Descendants(control)) yield return child; }
    }
    static void Shot(Control control, string path)
    {
        using (var bitmap = new Bitmap(control.Width, control.Height))
        { control.DrawToBitmap(bitmap, control.ClientRectangle); bitmap.Save(path); }
    }
    static bool PumpUntil(Func<bool> condition, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            if (condition()) return true;
            System.Threading.Thread.Sleep(10);
        }
        return condition();
    }
    static Point ItemPoint(ComboBox picker, string id, out Rectangle bounds)
    {
        var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
        Assert(GetComboBoxInfo(picker.Handle, ref info), "Dropdown list handle is unavailable.");
        int item = picker.Items.Cast<object>().Select((value, index) => new { value, index })
            .Single(pair => pair.value is Preset && ((Preset)pair.value).Id == id).index;
        SendMessage(info.List, 0x197, new IntPtr(item), IntPtr.Zero);
        var rectangle = new NativeRect(); SendMessage(info.List, 0x198, new IntPtr(item), ref rectangle);
        Point point = new Point(rectangle.Left + 30, rectangle.Top + (rectangle.Bottom - rectangle.Top) / 2);
        ClientToScreen(info.List, ref point);
        NativeRect list; GetWindowRect(info.List, out list);
        bounds = Rectangle.FromLTRB(list.Left, list.Top, list.Right, list.Bottom);
        Assert(bounds.Contains(point), "Hover target must be visible inside the scrolled dropdown: " + id);
        return point;
    }
    static void HoverItem(ComboBox picker, string id, out Rectangle bounds)
    {
        Point point = ItemPoint(picker, id, out bounds); Cursor.Position = point;
        var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) }; GetComboBoxInfo(picker.Handle, ref info);
        ScreenToClient(info.List, ref point);
        PostMessage(info.List, 0x200, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xffff)));
    }
    static void NativeHover(MainForm form, int count, string output)
    {
        var picker = Field<ComboBox>(form, "cbPreset");
        var hover = Field<object>(form, "presetHoverPreview");
        form.Opacity = 1; form.TopMost = true; form.Activate(); Application.DoEvents();
        Point header = picker.PointToScreen(new Point(picker.Width - 12, picker.Height / 2));
        Cursor.Position = header;
        Exception failure = null; bool finished = false; int stage = 0;
        Rectangle listBounds = Rectangle.Empty;
        var elapsed = Stopwatch.StartNew(); var overall = Stopwatch.StartNew();
        using (var driver = new Timer { Interval = 50 })
        {
            driver.Tick += delegate
            {
                try
                {
                    if (overall.ElapsedMilliseconds > 7000) throw new Exception("Native hover timed out at stage " + stage + "; dropdown=" + picker.DroppedDown + "; popup=" + (Field<Control>(hover, "popup") != null) + "; hovered=" + (Field<Preset>(hover, "hovered") == null ? "null" : Field<Preset>(hover, "hovered").Id));
                    var popup = Field<Control>(hover, "popup");
                    var hovered = Field<Preset>(hover, "hovered");
                    if (stage == 0 && picker.DroppedDown && elapsed.ElapsedMilliseconds >= 600)
                    { HoverItem(picker, "mouse_guard", out listBounds); stage = 1; }
                    else if (stage == 1 && popup != null && popup.Visible && hovered != null && hovered.Id == "mouse_guard")
                    {
                        Assert(picker.DroppedDown && Field<Preset>(hover, "hovered").Id == "mouse_guard", "Hover closed the dropdown or displayed the wrong preset.");
                        Assert(!popup.Bounds.IntersectsWith(listBounds), "Preview covers dropdown options.");
                        Rectangle capture = Rectangle.Union(form.Bounds, popup.Bounds);
                        using (var image = new Bitmap(capture.Width, capture.Height))
                        {
                            using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(capture.Location, Point.Empty, capture.Size);
                            image.Save(Path.Combine(output, "dropdown-hover.png"));
                        }
                        Cursor.Position = header;
                        var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) }; GetComboBoxInfo(picker.Handle, ref info);
                        PostMessage(info.List, 0x2A3, IntPtr.Zero, IntPtr.Zero);
                        stage = 2; elapsed.Restart();
                    }
                    else if (stage == 2 && popup == null && elapsed.ElapsedMilliseconds >= 600)
                    { HoverItem(picker, "mouse_guard", out listBounds); stage = 3; }
                    else if (stage == 3 && popup != null && popup.Visible)
                    { HoverItem(picker, "mouse_guard_space", out listBounds); stage = 4; }
                    else if (stage == 4 && popup != null && popup.Visible && hovered != null && hovered.Id == "mouse_guard_space")
                    {
                        Assert(!popup.Bounds.IntersectsWith(listBounds), "Mouse + Space preview covers dropdown options.");
                        Rectangle capture = Rectangle.Union(form.Bounds, popup.Bounds);
                        using (var image = new Bitmap(capture.Width, capture.Height))
                        {
                            using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(capture.Location, Point.Empty, capture.Size);
                            image.Save(Path.Combine(output, "mouse-space-hover.png"));
                        }
                        HoverItem(picker, "souls", out listBounds); stage = 5;
                    }
                    else if (stage == 5 && popup != null && popup.Visible && hovered != null && hovered.Id == "souls")
                    {
                        Assert(form.Session.PresetId == null && form.Session.ScopeChangeCount == count, "Hover changed the draft.");
                        SendMessage(picker.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                        stage = 6;
                    }
                    else if (stage == 6 && !picker.DroppedDown)
                    {
                        Assert(picker.SelectedIndex == 0 && form.Session.PresetId == null && form.Session.ScopeChangeCount == count, "Cancelling hover selected a preset or changed the draft.");
                        Assert(Field<Control>(hover, "popup") == null, "Closing the dropdown left its preview visible.");
                        finished = true; driver.Stop();
                    }
                }
                catch (Exception ex)
                { failure = new Exception("Native dropdown stage " + stage, ex); finished = true; driver.Stop(); picker.DroppedDown = false; }
            };
            driver.Start();
            var click = new IntPtr(((picker.Height / 2) << 16) | (picker.Width - 12));
            PostMessage(picker.Handle, 0x201, new IntPtr(1), click);
            PostMessage(picker.Handle, 0x202, IntPtr.Zero, click);
            bool completed = PumpUntil(delegate { return finished; }, 9000);
            if (!completed || failure != null)
            { picker.DroppedDown = false; form.Session.Undo(); }
            if (failure != null) throw failure;
            Assert(completed, "Native mouse hover did not finish.");
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Point cursor = Cursor.Position;
        try
        {
            Application.EnableVisualStyles();
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] fixture = File.ReadAllBytes(args[0]);
            var customized = SaveFile.FromBytes(fixture);
            var savedPreset = new Preset(); savedPreset.Keyboard.Add(new KeyBinding("IA_Jump", 0, "F10"));
            foreach (var record in customized.Records) Presets.Apply(customized, savedPreset, record);
            byte[] savedBytes = customized.ToBytes();
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, savedBytes);
            using (var form = new MainForm())
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.UiSmoke(input); form.Show(); form.Activate(); Application.DoEvents();
                Field<ComboBox>(form, "cbLang").SelectedIndex = 0;
                var session = form.Session;
                Assert(session.Relations("IA_Jump", false, -1).Keys.Contains("F10"), "The fixture must contain a saved custom jump binding.");
                session.SetKeyboard("IA_Jump", 0, "F11"); form.RenderSession();
                int count = session.ScopeChangeCount; string profile = session.SelectedProfile;
                Type windowType = typeof(MainForm).Assembly.GetType("PBZKeyForge.PresetHoverPreview+PreviewWindow");
                foreach (var preset in Presets.All())
                {
                    var snapshot = session.PreviewPreset(preset.Id);
                    Assert(snapshot.SelectedProfile == profile, "Preview changed the selected scheme: " + preset.Id);
                    Assert(snapshot.Relations("IA_Jump", false, -1).Keys.SequenceEqual(new[] { "SpaceBar" }),
                        "Preset preview must not include unrelated saved or draft jump bindings: " + preset.Id);
                    Assert(session.ScopeChangeCount == count && session.GetKeyboard("IA_Jump", 0) == "F11" && session.PresetId == null,
                        "Preview modified the source draft: " + preset.Id);
                    if (preset.Id == "mouse_guard")
                    {
                        Assert(snapshot.Relations("IA_Guard", false, -1).Keys.Contains("LeftMouseButton"), "Mouse preview is missing left guard.");
                        Assert(snapshot.Relations("IA_HeavyAttack", false, -1).Keys.Contains("RightMouseButton") && snapshot.Relations("IA_HeavyAttack", false, -1).Keys.Contains("F"), "Mouse preview must include right heavy and F.");
                    }
                    if (preset.Id != "mouse_guard" && preset.Id != "mouse_guard_space" && preset.Id != "souls") continue;
                    using (var popup = (Control)Activator.CreateInstance(windowType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                        new object[] { form.Font, preset, snapshot, preset.Id == "souls", -1, Screen.PrimaryScreen.WorkingArea }, null))
                    {
                        popup.CreateControl(); popup.PerformLayout(); Application.DoEvents();
                        Assert(popup.Width < form.Width && Descendants(popup).OfType<PictureBox>().Single().Image != null, "Preview must contain a smaller device diagram.");
                        Assert(Descendants(popup).OfType<Label>().Any(label => label.Text.Contains("→")), "Preview must compare factory keys with the result.");
                        Assert(Descendants(popup).OfType<Label>().Where(label => label.Text.Contains("→")).All(label => label.ForeColor == Color.FromArgb(64, 158, 255)),
                            "All preset changes must use one blue, independent of save status or device.");
                        Assert(!Descendants(popup).OfType<Label>().Any(label => label.ForeColor == Color.FromArgb(240, 178, 78)),
                            "Preset preview must not show pending-state yellow.");
                        Assert(!Descendants(popup).OfType<Label>().Any(label => label.Text.Contains("F11") || label.Text.Contains("F10")),
                            "Preset description includes an unrelated user binding.");
                        foreach (var label in Descendants(popup).OfType<Label>())
                            Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "Preview text is clipped: " + label.Text);
                        Shot(popup, Path.Combine(output, preset.Id + ".png"));
                    }
                }
                session.SelectPreset("mouse_guard");
                int selectedCount = session.ScopeChangeCount;
                var alternative = session.PreviewPreset("space_dodge");
                Assert(!alternative.Relations("IA_Guard", false, -1).Keys.Contains("LeftMouseButton"), "Hover stacked the selected quick configuration onto the candidate.");
                Assert(session.PresetId == "mouse_guard" && session.ScopeChangeCount == selectedCount && session.Relations("IA_Guard", false, -1).Keys.Contains("LeftMouseButton"),
                    "Hovering another quick configuration changed the selected preview.");
                session.SelectPreset(null); form.RenderSession();
                if (!args.Contains("--render-only")) NativeHover(form, count, output);
                Assert(savedBytes.SequenceEqual(File.ReadAllBytes(input)), "Hover preview wrote the copied save.");
                session.Undo();
            }
            Assert(fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Original fixture changed.");
            Console.WriteLine("PresetHoverPreviewTests PASS: " + checks + " checks; " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Cursor.Position = cursor; }
    }
}
