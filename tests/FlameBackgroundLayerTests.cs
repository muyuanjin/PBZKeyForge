using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

internal static class FlameBackgroundLayerTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr context);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr context, int x, int y);

    static object Field(object owner, string name)
    { return owner.GetType().GetField(name, Hidden).GetValue(owner); }

    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    static Bitmap Capture(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppPArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    static void CheckForeground(Control control, object effect, MethodInfo setActive, string name, string output)
    {
        setActive.Invoke(effect, new object[] { false });
        using (var plain = Capture(control))
        {
            setActive.Invoke(effect, new object[] { true });
            using (var burning = Capture(control))
            {
                Color[] colors = { Color.FromArgb(238, 234, 225), Color.FromArgb(173, 178, 177), Color.FromArgb(52, 58, 61) };
                foreach (Color color in colors)
                {
                    int foreground = 0, changed = 0;
                    for (int y = 0; y < plain.Height; y++)
                        for (int x = 0; x < plain.Width; x++)
                            if (plain.GetPixel(x, y).ToArgb() == color.ToArgb())
                            {
                                foreground++;
                                if (plain.GetPixel(x, y) != burning.GetPixel(x, y)) changed++;
                            }
                    Check(changed == 0, name + " paints flames over foreground color " + color + ": " + changed + "/" + foreground);
                }
                burning.Save(Path.Combine(output, name + ".png"));
            }
        }
    }

    static void CheckBackground(MainForm form, Control control, Bitmap screenshot, Bitmap frame, Point local, string name)
    {
        Point host = form.PointToClient(control.PointToScreen(local));
        Check(screenshot.GetPixel(local.X, local.Y) == frame.GetPixel(host.X, host.Y),
            name + " interrupts or misaligns the background at " + local + ": " + screenshot.GetPixel(local.X, local.Y) + " instead of " + frame.GetPixel(host.X, host.Y) + ".");
    }

    static void CheckSingleBlend(Control control, object effect, MethodInfo setActive, Point point, string name)
    {
        setActive.Invoke(effect, new object[] { false });
        using (var plain = Capture(control))
        {
            setActive.Invoke(effect, new object[] { true });
            using (var burning = Capture(control))
            {
                Color background = plain.GetPixel(point.X, point.Y), actual = burning.GetPixel(point.X, point.Y);
                int red = (208 * 160 + background.R * 95 + 127) / 255;
                int green = (62 * 160 + background.G * 95 + 127) / 255;
                int blue = (255 * 160 + background.B * 95 + 127) / 255;
                Check(Math.Abs(actual.R - red) <= 2 && Math.Abs(actual.G - green) <= 2 && Math.Abs(actual.B - blue) <= 2,
                    name + " must composite the translucent frame exactly once: " + actual);
            }
        }
    }

    static void CheckTreeInput(TreeView tree)
    {
        TreeNode original = tree.SelectedNode, branch = null;
        foreach (TreeNode node in tree.Nodes)
            if (node.Nodes.Count > 0) { branch = node; break; }
        Check(branch != null, "The category tree has no expandable category.");
        tree.SelectedNode = branch; branch.Collapse(); branch.EnsureVisible(); tree.Focus();
        SendMessage(tree.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
        Check(branch.IsExpanded, "Keyboard expansion of a category stopped working.");
        SendMessage(tree.Handle, 0x0100, (IntPtr)Keys.Left, IntPtr.Zero);
        Check(!branch.IsExpanded, "Keyboard collapse of a category stopped working.");
        Point glyph = Point.Empty;
        int middle = branch.Bounds.Top + branch.Bounds.Height / 2;
        for (int x = 0; x < branch.Bounds.Left; x++)
            if ((tree.HitTest(x, middle).Location & TreeViewHitTestLocations.PlusMinus) != 0)
            { glyph = new Point(x, middle); break; }
        Check(glyph != Point.Empty, "The category expansion glyph no longer matches its native hit area.");
        IntPtr position = (IntPtr)(glyph.X | glyph.Y << 16);
        SendMessage(tree.Handle, 0x0201, (IntPtr)1, position);
        SendMessage(tree.Handle, 0x0202, IntPtr.Zero, position);
        Check(branch.IsExpanded, "Clicking the category expansion glyph stopped working.");
        branch.Collapse(); tree.SelectedNode = original; original.EnsureVisible();
    }

    static void CheckFooterBackground(MainForm form, Bitmap frame, string output)
    {
        var bar = (Control)Field(form, "keyEnhancementBar");
        var toggle = (CheckBox)Field(bar, "Toggle");
        Check(toggle.Checked, "The footer fixture must enable key enhancement through its checkbox.");
        foreach (Color color in new[] { Color.FromArgb(160, 208, 62, 255), Color.FromArgb(160, 62, 208, 100) })
        {
            using (var graphics = Graphics.FromImage(frame)) graphics.Clear(color);
            using (var barShot = Capture(bar))
            using (var toggleShot = Capture(toggle))
            {
                Color background = barShot.GetPixel(toggle.Left - 1, toggle.Top);
                Check(toggleShot.GetPixel(0, 0) == background,
                    "The enhancement checkbox background must match its surrounding footer: " + toggleShot.GetPixel(0, 0) + " instead of " + background);
                foreach (string name in new[] { "chkPerfectDeflectNoRapidClickPenalty", "chkActivate" })
                    using (var screenshot = Capture((Control)Field(form, name)))
                        Check(screenshot.GetPixel(0, 0) == background, name + " must share the enhancement footer background.");
                barShot.Save(Path.Combine(output, "footer-background-" + color.G + ".png"));
            }
        }
        using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.FromArgb(160, 208, 62, 255));
    }

    static void CheckLegend(Control legend, Bitmap frame, object effect, MethodInfo setActive, string output)
    {
        setActive.Invoke(effect, new object[] { false });
        using (var plain = Capture(legend))
        {
            setActive.Invoke(effect, new object[] { true });
            using (var first = Capture(legend))
            {
                using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.FromArgb(160, 62, 208, 100));
                using (var next = Capture(legend))
                {
                    foreach (Color color in new[] { Color.FromArgb(128, 143, 153), Color.FromArgb(64, 158, 255), Color.FromArgb(240, 178, 78), Color.FromArgb(173, 178, 177) })
                    {
                        int foreground = 0, changed = 0;
                        for (int y = 0; y < first.Height; y++)
                            for (int x = 0; x < first.Width; x++)
                                if (plain.GetPixel(x, y).ToArgb() == color.ToArgb())
                                {
                                    foreground++;
                                    if (next.GetPixel(x, y).ToArgb() != color.ToArgb() || first.GetPixel(x, y).ToArgb() != color.ToArgb()) changed++;
                                }
                        Check(foreground > 0 && changed == 0, "A legend label or dot changes with the flame frame: " + color + "; changed=" + changed + "/" + foreground);
                    }
                    Check(first.GetPixel(2, 2) != next.GetPixel(2, 2), "The legend stopped displaying the animated flame background");
                    first.Save(Path.Combine(output, "legend-frame-one.png"));
                    next.Save(Path.Combine(output, "legend-frame-two.png"));
                }
            }
        }
        legend.FindForm().TopMost = true; legend.FindForm().Activate(); legend.Refresh(); Application.DoEvents();
        using (var previous = Capture(legend))
        {
            Point dot = Point.Empty;
            for (int y = 0; y < previous.Height && dot == Point.Empty; y++)
                for (int x = 0; x < previous.Width; x++)
                    if (previous.GetPixel(x, y).ToArgb() == Color.FromArgb(240, 178, 78).ToArgb()) { dot = new Point(x, y); break; }
            Check(dot != Point.Empty, "The pending legend dot is missing");
            IntPtr windowContext = GetDC(legend.Handle);
            uint originalDot = GetPixel(windowContext, dot.X, dot.Y);
            Check(originalDot == (uint)(240 | 178 << 8 | 78 << 16), "The visible legend dot does not match its rendered color");
            bool painted = false, preserved = false;
            PaintEventHandler painting = delegate
            {
                painted = true;
                preserved = GetPixel(windowContext, dot.X, dot.Y) == originalDot;
            };
            legend.Paint += painting;
            try
            {
                legend.Invalidate(); legend.Update();
                Check(painted, "The flame legend did not repaint");
                Check(preserved, "The legend disappears from its window between background and foreground painting");
            }
            finally { legend.Paint -= painting; ReleaseDC(legend.Handle, windowContext); }
        }
        using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.FromArgb(160, 208, 62, 255));
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string output = Path.GetFullPath(args.Length == 0 ? "dist/flame-background-layers" : args[0]);
            Directory.CreateDirectory(output);
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                typeof(MainForm).GetProperty("Session", Hidden).GetSetMethod(true)
                    .Invoke(form, new object[] { EditorSession.CreateOffline() });
                form.RenderSession(); form.ShowInTaskbar = false; form.Show(); form.ShowActionView(true);
                ((CheckBox)Field(Field(form, "keyEnhancementBar"), "Toggle")).Checked = true;
                Application.DoEvents();
                var effect = Field(form, "keyEnhancementFlame");
                var timer = (Timer)Field(effect, "animation"); timer.Stop();
                effect.GetType().GetMethod("RetirePendingFrame", Hidden).Invoke(effect, null);
                var frameField = effect.GetType().GetField("frame", Hidden);
                var original = (Bitmap)frameField.GetValue(effect);
                var heightField = effect.GetType().GetField("frameHeight", Hidden);
                object originalHeight = heightField.GetValue(effect); heightField.SetValue(effect, 100);
                var setActive = effect.GetType().GetProperty("IsActive", Hidden).GetSetMethod(true);
                var view = (Control)Field(form, "unifiedActions");
                var grid = (DataGridView)Field(view, "actions");
                var tree = (TreeView)Field(view, "groups");
                CheckTreeInput(tree);
                using (var frame = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppPArgb))
                {
                    using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.FromArgb(160, 208, 62, 255));
                    frameField.SetValue(effect, frame);
                    try
                    {
                        setActive.Invoke(effect, new object[] { true });
                        CheckFooterBackground(form, frame, output);
                        CheckForeground(grid, effect, setActive, "grid-foreground", output);
                        CheckForeground(tree, effect, setActive, "tree-foreground", output);
                        CheckForeground((Control)Field(form, "footer"), effect, setActive, "footer-foreground", output);
                        form.ShowActionView(false); Application.DoEvents(); setActive.Invoke(effect, new object[] { true });
                        CheckLegend((Control)Field(Field(form, "keyBindings"), "legend"), frame, effect, setActive, output);
                        form.ShowActionView(true); Application.DoEvents(); setActive.Invoke(effect, new object[] { true });
                        Rectangle alphaHeader = grid.GetCellDisplayRectangle(1, -1, false);
                        CheckSingleBlend(grid, effect, setActive, new Point(alphaHeader.Right - 10, alphaHeader.Bottom - 5), "The header background");
                        Rectangle alphaCell = grid.GetCellDisplayRectangle(0, 1, false);
                        CheckSingleBlend(grid, effect, setActive, new Point(alphaCell.Right - 10, alphaCell.Bottom - 5), "The cell background");
                        CheckSingleBlend(tree, effect, setActive, new Point(2, tree.TopNode.NextVisibleNode.Bounds.Bottom - 2), "The category indentation");
                        var apply = (Control)Field(form, "btnApply");
                        setActive.Invoke(effect, new object[] { false });
                        using (var plain = Capture(apply))
                        {
                            setActive.Invoke(effect, new object[] { true });
                            using (var burning = Capture(apply))
                                for (int y = 6; y < plain.Height - 6; y++)
                                    for (int x = 6; x < plain.Width - 6; x++)
                                        Check(plain.GetPixel(x, y) == burning.GetPixel(x, y), "The Apply button is tinted by the background.");
                        }
                        foreach (Control child in grid.Controls)
                        {
                            if (!(child is ScrollBar) || !child.Visible) continue;
                            setActive.Invoke(effect, new object[] { false });
                            using (var plain = Capture(child))
                            {
                                setActive.Invoke(effect, new object[] { true });
                                using (var burning = Capture(child))
                                {
                                    int white = 0;
                                    for (int y = 0; y < plain.Height; y++)
                                        for (int x = 0; x < plain.Width; x++)
                                        {
                                            Color pixel = burning.GetPixel(x, y);
                                            Check(pixel == plain.GetPixel(x, y), "The scrollbar is tinted by the background.");
                                            if (pixel.R > 230 && pixel.G > 230 && pixel.B > 230) white++;
                                        }
                                    Check(white < plain.Width * plain.Height / 10, "The action scrollbar uses a white system surface.");
                                }
                            }
                        }
                        for (int y = 0; y < frame.Height; y++)
                            for (int x = 0; x < frame.Width; x++)
                                frame.SetPixel(x, y, Color.FromArgb(80 + x % 100, 60 + y % 100, 190));
                        foreach (int firstRow in new[] { 0, 5 })
                        {
                            grid.FirstDisplayedScrollingRowIndex = firstRow;
                            using (var screenshot = Capture(grid))
                            {
                                screenshot.Save(Path.Combine(output, "grid-coordinates-" + firstRow + ".png"));
                                Rectangle header = grid.GetCellDisplayRectangle(1, -1, false);
                                CheckBackground(form, grid, screenshot, frame, new Point(header.Right - 10, header.Bottom - 5), "The column header");
                                Rectangle cell = grid.GetCellDisplayRectangle(0, firstRow + 1, false);
                                CheckBackground(form, grid, screenshot, frame, new Point(cell.Right - 10, cell.Bottom - 5), "The action cell");
                            }
                        }
                        using (var screenshot = Capture(tree))
                        {
                            TreeNode node = tree.TopNode.NextVisibleNode;
                            Check(node != null, "The category tree has no visible nested group.");
                            CheckBackground(form, tree, screenshot, frame, new Point(2, node.Bounds.Bottom - 2), "The category indentation");
                            screenshot.Save(Path.Combine(output, "tree-coordinates.png"));
                        }
                    }
                    finally { frameField.SetValue(effect, original); heightField.SetValue(effect, originalHeight); setActive.Invoke(effect, new object[] { false }); }
                }
            }
            Console.WriteLine("FlameBackgroundLayerTests PASS ({0} checks)", checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
