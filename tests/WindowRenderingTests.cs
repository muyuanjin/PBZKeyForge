using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

public static class WindowRenderingTests
{
    static int checks;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window, IntPtr area, IntPtr region, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static Control Diagram(MainForm form)
    { return Descendants(Field<Control>(form, "keyBindings")).Single(c => c.GetType().Name == "DeviceDiagram"); }
    static void SelectKey(Control diagram, string key)
    {
        Point point = Point.Round((PointF)diagram.GetType().GetMethod("Position", Hidden).Invoke(diagram, new object[] { key }));
        typeof(Control).GetMethod("OnMouseClick", Hidden).Invoke(diagram, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
    }
    static void Frame(EditorWindow form, string output, string state)
    {
        Application.DoEvents();
        var main = form as MainForm;
        if (main != null)
        {
            var footer = Field<Control>(main, "footer");
            var status = Field<Control>(main, "lblStatus"); var backup = Field<Control>(main, "lblBackup");
            Assert(!status.Bounds.IntersectsWith(backup.Bounds), state + ": status overlaps the backup hint.");
            Assert(footer.ClientRectangle.Contains(backup.Bounds) && backup.Height >= backup.Font.Height,
                state + ": backup hint is clipped by the footer.");
        }
        // Force synchronous frame painting as Windows does on focus and title changes.
        RedrawWindow(form.Handle, IntPtr.Zero, IntPtr.Zero, 0x501);
        DwmFlush();
        IntPtr dc = GetWindowDC(form.Handle);
        try
        {
            int bright = 0;
            for (int x = 16; x < form.Width - 16; x += 4)
            {
                uint color = GetPixel(dc, x, 1);
                if ((color & 255) > 180 && ((color >> 8) & 255) > 180 && ((color >> 16) & 255) > 180) bright++;
            }
            Assert(bright < 4, state + ": native light frame painted over custom chrome (" + bright + " samples).");
            uint background = (uint)(form.BackColor.R | form.BackColor.G << 8 | form.BackColor.B << 16);
            bool solid = true;
            for (int x = 16; x < form.Width - 16; x += 8)
            {
                for (int y = 1; y < form.Padding.Top; y++) solid &= GetPixel(dc, x, y) == background;
                for (int y = form.Height - form.Padding.Bottom; y < form.Height - 1; y++) solid &= GetPixel(dc, x, y) == background;
            }
            for (int y = 16; y < form.Height - 16; y += 8)
            {
                for (int x = 1; x < form.Padding.Left; x++) solid &= GetPixel(dc, x, y) == background;
                for (int x = form.Width - form.Padding.Right; x < form.Width - 1; x++) solid &= GetPixel(dc, x, y) == background;
            }
            Assert(solid, state + ": wallpaper leaked into the window frame padding.");
        }
        finally { ReleaseDC(form.Handle, dc); }
        using (var image = new Bitmap(form.Width, form.Height))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(form.Location, Point.Empty, image.Size);
            image.Save(Path.Combine(output, state + ".png"));
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] original = File.ReadAllBytes(args[0]);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, original);
            var startupPreferences = new EditorPreferences(Path.Combine(output, "startup-preferences", "mode.txt"));
            startupPreferences.SaveInputPath(input);
            using (var startup = MainForm.OpenDefault(startupPreferences))
            {
                bool readyBeforeShow = false;
                startup.Load += delegate { readyBeforeShow = startup.Session != null; };
                startup.StartPosition = FormStartPosition.Manual; startup.TopMost = true;
                startup.Size = startup.MinimumSize; startup.Location = Screen.PrimaryScreen.WorkingArea.Location;
                startup.Show(); Application.DoEvents();
                Assert(readyBeforeShow, "Automatic discovery must finish before the first visible frame.");
                Assert(startup.Session.InputPath == input, "Startup did not restore the remembered save.");
                Assert(!startup.Session.HasAnyDrafts && !startup.Session.HasChanges, "Starting the editor created pending changes.");
                Frame(startup, output, "startup");
                startup.Close();
            }
            using (var form = new MainForm())
            {
                form.UiSmoke(input); form.StartPosition = FormStartPosition.Manual; form.TopMost = true;
                form.Size = form.MinimumSize; form.Location = Screen.PrimaryScreen.WorkingArea.Location;
                form.Show(); form.Activate(); Application.DoEvents();
                var diagram = Diagram(form); IntPtr handle = diagram.Handle;
                var controls = Descendants(Field<Control>(form, "keyBindings")).ToArray();
                var diff = Field<Control>(form, "schemeDiff"); var diffControls = Descendants(diff).ToArray();
                int removed = 0;
                Field<Control>(form, "keyBindings").ControlRemoved += delegate { removed++; };
                foreach (var control in controls) control.ControlRemoved += delegate { removed++; };
                for (int i = 0; i < 5; i++) form.RefreshFromDisk();
                Assert(ReferenceEquals(diagram, Diagram(form)) && diagram.Handle == handle, "Refreshing unchanged data recreated the device diagram.");
                Assert(controls.All(c => !c.IsDisposed) && diffControls.All(c => !c.IsDisposed) && removed == 0, "Refreshing unchanged data destroyed binding or comparison controls.");
                foreach (string key in new[] { "RightMouseButton", "F", "LeftMouseButton" })
                {
                    SelectKey(diagram, key); Application.DoEvents();
                    Assert(ReferenceEquals(diagram, Diagram(form)) && diagram.ContainsFocus && GetFocus() == handle, "Selecting a key recreated the diagram or lost keyboard focus.");
                }
                foreach (var binding in new[] { new[] { "F", "IA_HeavyAttack" }, new[] { "RightMouseButton", "IA_Guard" } })
                {
                    form.Session.RestoreAction(binding[1], false, -1); form.RenderSession();
                    SelectKey(diagram, binding[0]); Application.DoEvents();
                    var edit = Descendants(Field<Control>(form, "keyBindings")).OfType<Button>().Single(c =>
                        c.GetType().Name == "RelationshipButton" && (string)c.GetType().GetField("ActionId").GetValue(c) == binding[1]);
                    var operation = edit.Parent.Controls.OfType<Button>().Single(c => c != edit);
                    Assert(operation.Enabled, "Reused relationship row disabled removal: " + binding[0]);
                    Point hover = Point.Round((PointF)diagram.GetType().GetMethod("Position", Hidden).Invoke(diagram, new object[] { binding[0] }));
                    typeof(Control).GetMethod("OnMouseMove", Hidden).Invoke(diagram, new object[] { new MouseEventArgs(MouseButtons.None, 0, hover.X, hover.Y, 0) });
                    var tooltip = Field<ToolTip>(diagram, "tip"); string oldTip = tooltip.GetToolTip(diagram);
                    operation.PerformClick(); Application.DoEvents();
                    Assert(!form.Session.Relations(binding[1], false, -1).Keys.Contains(binding[0]), "Reused relationship row edited a previous key or action.");
                    Assert(tooltip.GetToolTip(diagram) != oldTip && tooltip.GetToolTip(diagram).Contains(I18n.Str("views.pending")), "Device tooltip retained old relationship status after refresh.");
                }
                diffControls = Descendants(diff).ToArray();
                form.RefreshFromDisk();
                Assert(diffControls.All(c => !c.IsDisposed), "Refreshing pending differences recreated comparison rows.");
                form.Session.Undo(); form.RenderSession(); SelectKey(diagram, "LeftMouseButton");
                Frame(form, output, "selected");
                for (int i = 0; i < 3; i++)
                {
                    SendMessage(form.Handle, 0x86, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(form.Handle, 0x86, new IntPtr(1), IntPtr.Zero);
                    SendMessage(form.Handle, 0xAE, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(form.Handle, 0xAF, IntPtr.Zero, IntPtr.Zero);
                    form.Text = "PBZKeyForge " + i;
                    Frame(form, output, "activation-" + i);
                }
                using (var dialog = new EditorWindow { TopMost = true, AllowResize = false, BackColor = form.BackColor, Size = new Size(300, 150), StartPosition = FormStartPosition.CenterParent })
                using (var timer = new Timer { Interval = 150 })
                {
                    Exception failure = null;
                    timer.Tick += delegate {
                        timer.Stop();
                        try { Frame(dialog, output, "dialog"); }
                        catch (Exception ex) { failure = ex; }
                        finally { dialog.Close(); }
                    };
                    timer.Start(); dialog.ShowDialog(form);
                    if (failure != null) throw failure;
                }
                Application.DoEvents();
                Assert(ReferenceEquals(diagram, Diagram(form)), "Returning from a modal dialog recreated the diagram.");
                Frame(form, output, "modal-return");
                Size normal = form.Size;
                form.captionMaximize.PerformClick(); Application.DoEvents(); Frame(form, output, "maximized");
                Assert(form.WindowState == FormWindowState.Maximized && Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "Custom chrome broke maximize bounds.");
                form.captionMaximize.PerformClick(); Application.DoEvents();
                Assert(form.WindowState == FormWindowState.Normal && form.Size == normal, "Restoring the window changed its size.");
                Point corner = form.PointToScreen(new Point(1, 1));
                long packed = (ushort)corner.X | ((long)(ushort)corner.Y << 16);
                Assert(SendMessage(form.Handle, 0x84, IntPtr.Zero, new IntPtr(packed)).ToInt32() == 13, "Custom frame lost its top-left resize handle.");
                form.captionMinimize.PerformClick(); Application.DoEvents();
                Assert(form.WindowState == FormWindowState.Minimized, "Minimize stopped working.");
                form.WindowState = FormWindowState.Normal; form.Activate(); Application.DoEvents();
                Frame(form, output, "restored");
                form.ShowActionView(true); form.RefreshFromDisk(); form.ShowActionView(false);
                Assert(ReferenceEquals(diagram, Diagram(form)), "Changing perspectives recreated the diagram.");
                form.ShowGamepad(true); Application.DoEvents();
                var controller = Diagram(form); form.RefreshFromDisk();
                Assert(ReferenceEquals(controller, Diagram(form)), "Refreshing the controller recreated the diagram.");
                form.ShowGamepad(false); Application.DoEvents();
                Frame(form, output, "keyboard-return");
                Assert(!form.Session.HasAnyDrafts && original.SequenceEqual(File.ReadAllBytes(input)), "Rendering checks changed bindings or wrote the save.");
            }
            Assert(original.SequenceEqual(File.ReadAllBytes(args[0])), "Original fixture changed.");
            Console.WriteLine("WindowRenderingTests PASS: " + checks + " checks; visible window screenshots: " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
