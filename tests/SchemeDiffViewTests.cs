using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

public static class SchemeDiffViewTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(owner); }
    static Control[] Rows(Control view)
    { return Field<Panel>(view, "rows").Controls.Cast<Control>().Where(c => c.GetType().Name == "DifferenceRow").ToArray(); }
    static void Compare(Control view, bool factory)
    { view.GetType().GetMethod("SetComparison").Invoke(view, new object[] { factory }); Application.DoEvents(); }
    static string[] Parts(Control row)
    { return ((IEnumerable)row.GetType().GetMethod("Parts", Hidden).Invoke(row, null)).Cast<object>().Select(p => Field<string>(p, "Text")).ToArray(); }
    static object[] Tokens(Control row)
    { return ((IEnumerable)row.GetType().GetMethod("Parts", Hidden).Invoke(row, null)).Cast<object>().ToArray(); }
    static void Click(Control row, bool content)
    {
        var area = (Rectangle)row.GetType().GetMethod("KeyArea", Hidden).Invoke(row, null);
        int x = content ? area.Left + area.Width / 2 : 4, y = row.Height / 2;
        row.GetType().GetMethod("OnMouseClick", Hidden).Invoke(row, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
        Application.DoEvents();
    }
    static void Key(Control row, Keys key)
    {
        Message message = Message.Create(row.Handle, 0x100, new IntPtr((int)key), IntPtr.Zero);
        Assert(!row.PreProcessMessage(ref message), "Row activation key must reach KeyDown instead of being consumed as a form command.");
        SendMessage(row.Handle, 0x100, new IntPtr((int)key), IntPtr.Zero); SendMessage(row.Handle, 0x101, new IntPtr((int)key), IntPtr.Zero); Application.DoEvents();
    }
    static void Shot(Control control, string path)
    {
        control.Refresh(); Application.DoEvents();
        using (var bitmap = new Bitmap(control.Width, control.Height))
        { control.DrawToBitmap(bitmap, control.ClientRectangle); bitmap.Save(path); }
    }
    static void PartialPaint(Control row, string output)
    {
        using (var bitmap = new Bitmap(row.Width, row.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            Color untouched = Color.Magenta;
            graphics.Clear(untouched);
            var clip = new Rectangle(0, 0, row.Width, 1);
            graphics.SetClip(clip);
            row.GetType().GetMethod("OnPaint", Hidden).Invoke(row, new object[] { new PaintEventArgs(graphics, clip) });
            int outside = 0;
            for (int y = 1; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() != untouched.ToArgb()) outside++;
            if (outside != 0) bitmap.Save(Path.Combine(output, "partial-paint-outside-clip.png"));
            Assert(outside == 0, "A partially exposed difference row painted outside its scroll clip: " + outside + " pixels; mode=" + Field<int>(row, "Mode"));
        }
    }
    static Bitmap ScreenImage(Control control)
    {
        Application.DoEvents(); DwmFlush();
        var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(control.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
        return bitmap;
    }
    static void ScrollPaint(string output)
    {
        var session = EditorSession.CreateOffline();
        foreach (var action in MainForm.Actions)
        {
            if (session.HasKeyboardSlot(action.Id, 0)) session.SetKeyboard(action.Id, 0, "F12");
            if (session.HasGamepadSlot(action.Id, 0, 0)) session.SetGamepadFamily(action.Id, 0, 0, "Gamepad_FaceButton_Left");
        }
        Type type = typeof(MainForm).Assembly.GetType("PBZKeyForge.SchemeDiffView");
        using (var host = new Form { ClientSize = new Size(1100, 260), ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.CenterScreen })
        using (var view = (Control)Activator.CreateInstance(type, true))
        {
            view.Font = new Font("Microsoft YaHei UI", 10); view.Dock = DockStyle.Fill;
            host.Controls.Add(view); host.Show();
            type.GetMethod("Render", new[] { typeof(EditorSession) }).Invoke(view, new object[] { session });
            Application.DoEvents();
            var rows = Field<Panel>(view, "rows");
            Assert(rows.VerticalScroll.Visible, "Scroll regression needs a list longer than its viewport.");
            foreach (int command in new[] { 1, 1, 3, 7, 0, 6 })
            {
                int previous = rows.AutoScrollPosition.Y;
                SendMessage(rows.Handle, 0x115, new IntPtr(command), IntPtr.Zero);
                int offset = -rows.AutoScrollPosition.Y;
                Assert(rows.AutoScrollPosition.Y != previous, "Native scroll message did not move the list; command=" + command);
                using (var scrolled = ScreenImage(rows))
                {
                    rows.Invalidate(true); rows.Update();
                    using (var repainted = ScreenImage(rows))
                    {
                        int different = 0;
                        // The native Form's rounded corners expose desktop compositor pixels.
                        int inset;
                        using (var graphics = rows.CreateGraphics()) inset = (int)Math.Ceiling(8 * graphics.DpiX / 96f);
                        for (int y = 0; y < scrolled.Height; y++)
                            for (int x = inset; x < scrolled.Width - inset; x++)
                            {
                                Color actual = scrolled.GetPixel(x, y), expected = repainted.GetPixel(x, y);
                                // Scaled icon edges can round one channel differently when clipped.
                                if (Math.Abs(actual.R - expected.R) > 1 || Math.Abs(actual.G - expected.G) > 1 || Math.Abs(actual.B - expected.B) > 1) different++;
                            }
                        scrolled.Save(Path.Combine(output, "scroll-" + offset + ".png"));
                        if (different != 0) repainted.Save(Path.Combine(output, "repaint-" + offset + ".png"));
                        Assert(different == 0, "Native scrolling left stale row pixels until a full repaint: " + different + "; offset=" + offset);
                    }
                }
            }
        }
    }
    static void MainScrollPaint(string output)
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "scroll-prefs"))))
        {
            form.UiOffline(); form.ShowInTaskbar = false; form.TopMost = true;
            form.Size = form.MinimumSize; form.StartPosition = FormStartPosition.CenterScreen;
            foreach (var action in MainForm.Actions)
            {
                if (form.Session.HasKeyboardSlot(action.Id, 0)) form.Session.SetKeyboard(action.Id, 0, "F12");
                if (form.Session.HasGamepadSlot(action.Id, 0, 0)) form.Session.SetGamepadFamily(action.Id, 0, 0, "Gamepad_FaceButton_Left");
            }
            form.RenderSession(); form.Show(); Application.DoEvents();
            var view = Field<Control>(form, "schemeDiff"); Compare(view, true);
            var rows = Field<Panel>(view, "rows");
            for (int step = 0; step < 20; step++)
            {
                Point point = rows.PointToScreen(new Point(rows.Width / 2, rows.Height / 2));
                int delta = step < 8 ? -120 : 120;
                int previous = rows.AutoScrollPosition.Y;
                if (step < 12)
                    SendMessage(rows.Handle, 0x20a, new IntPtr(delta << 16), new IntPtr((point.Y << 16) | (point.X & 65535)));
                else if (step < 18)
                    SendMessage(rows.Handle, 0x115, new IntPtr(new[] { 1, 1, 3, 7, 0, 6 }[step - 12]), IntPtr.Zero);
                else
                    for (int burst = 0; burst < 4; burst++)
                        SendMessage(rows.Handle, 0x20a, new IntPtr((step == 18 ? -120 : 120) << 16), new IntPtr((point.Y << 16) | (point.X & 65535)));
                Application.DoEvents();
                Assert(previous != rows.AutoScrollPosition.Y, "MainForm scroll did not move the list; step=" + step);
                var visible = Rows(view).Where(r => r.Bottom > 0 && r.Top < rows.ClientSize.Height).OrderBy(r => r.Top).ToArray();
                for (int i = 1; i < visible.Length; i++) Assert(visible[i - 1].Bottom <= visible[i].Top, "Scrolled difference row bounds overlap.");
                using (var scrolled = ScreenImage(view))
                {
                    view.Invalidate(true); view.Update();
                    using (var repainted = ScreenImage(view))
                    {
                        int different = 0;
                        for (int y = 0; y < scrolled.Height; y++)
                            for (int x = 0; x < scrolled.Width; x++)
                            {
                                Color a = scrolled.GetPixel(x, y), b = repainted.GetPixel(x, y);
                                if (Math.Abs(a.R-b.R)>1 || Math.Abs(a.G-b.G)>1 || Math.Abs(a.B-b.B)>1) different++;
                            }
                        scrolled.Save(Path.Combine(output, "main-wheel-" + step + ".png"));
                        if (different != 0) repainted.Save(Path.Combine(output, "main-repaint-" + step + ".png"));
                        Assert(different == 0, "MainForm wheel scroll differs from full repaint: " + different + " pixels; step=" + step);
                    }
                }
                if (step == 4) form.RenderSession();
            }
        }
    }
    static void Synthetic(string output)
    {
        Type type = typeof(MainForm).Assembly.GetType("PBZKeyForge.SchemeDiffView+DifferenceRow");
        using (var host = new Form { ClientSize = new Size(1100, 150), Opacity = 0, ShowInTaskbar = false })
        using (var row = (Control)Activator.CreateInstance(type, true))
        {
            row.SetBounds(0, 0, 1100, 42); row.Font = new Font("Microsoft YaHei UI", 10);
            host.Controls.Add(row); host.Show();
            var replacement = new BindingDifference { Action = "IA_HeavyAttack", BeforeKeys = new[] { "F", "RightMouseButton" }, AfterKeys = new[] { "G", "F" },
                BeforeComplete = true, AfterComplete = true, DefaultKeys = new[] { "F", "RightMouseButton" } };
            type.GetMethod("UpdateDifference").Invoke(row, new object[] { replacement, "replacement", 0, new[] { "G" } });
            Assert(Parts(row).SequenceEqual(new[] { "+G", "F", "−右键" }), "Combined must order additions, retained keys, then removals.");
            object[] tokens = Tokens(row);
            Assert(Field<Color>(tokens[0], "Color") == Color.FromArgb(240, 178, 78), "Pending addition must be yellow.");
            Assert(Field<bool>(tokens[2], "Removed") && Field<Color>(tokens[2], "Color") == Color.FromArgb(139, 145, 148), "Removal must be gray and struck through.");
            PartialPaint(row, output);
            Shot(row, Path.Combine(output, "replacement-combined.png"));
            Click(row, true); Assert(Parts(row).SequenceEqual(new[] { "+G", "−右键" }), "Changes-only must omit retained keys: mode=" + Field<int>(row, "Mode") + " " + string.Join(" ", Parts(row)));
            PartialPaint(row, output);
            Shot(row, Path.Combine(output, "replacement-changes.png"));
            Click(row, true); Assert(Parts(row).SequenceEqual(new[] { "F / G" }), "Result must list retained keys before new keys, regardless of record ordering.");
            PartialPaint(row, output);
            Shot(row, Path.Combine(output, "replacement-result.png"));
            Click(row, true); Assert(Parts(row).SequenceEqual(new[] { "F / 右键" }), "Before must show the selected baseline exactly.");
            PartialPaint(row, output);
            Shot(row, Path.Combine(output, "replacement-before.png"));
            Click(row, true);
            replacement.AfterKeys = new string[0];
            type.GetMethod("UpdateDifference").Invoke(row, new object[] { replacement, "unbound", 0, new string[0] });
            Assert(Parts(row).SequenceEqual(new[] { "无绑定", "−F", "−右键" }), "Full removal must state unbound before showing gray removed keys.");
            Shot(row, Path.Combine(output, "unbound-combined.png"));
            replacement.BeforeComplete = false; replacement.AfterComplete = false;
            replacement.AfterKeys = new[] { "G" };
            type.GetMethod("UpdateDifference").Invoke(row, new object[] { replacement, "partial", 0, new[] { "G" } });
            Assert(!Parts(row).Any(p => p.StartsWith("−")) && Parts(row).Contains("未确认"), "Incomplete baselines must not invent removals.");
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            int languageCount = args.Contains("--all-languages") ? I18n.Languages.Length : 1;
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] fixture = File.ReadAllBytes(args[0]);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, fixture);
            File.WriteAllBytes(Path.Combine(output, "PBZSharedSetting.sav"), new byte[] { 1 });
            Synthetic(output);
            ScrollPaint(output);
            MainScrollPaint(output);
            using (var form = new MainForm())
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.UiSmoke(input); form.Show(); Application.DoEvents();
                var language = Field<ComboBox>(form, "cbLang"); language.SelectedIndex = 0;
                var session = form.Session; var view = Field<Control>(form, "schemeDiff");
                session.SelectPreset("vanilla_current"); form.RenderSession();
                Assert(!session.HasChanges && !session.HasAnyDrafts && Rows(view).Length == 0,
                    "Restoring unchanged factory bindings must not show pending changes or difference rows.");
                Assert(!Field<Button>(form, "btnApply").Enabled && Field<Control>(form, "btnUndo").Visible && !Field<Control>(form, "btnUndo").Enabled,
                    "An unchanged factory preview must not offer Apply or Undo.");
                foreach (Size size in new[] { form.Size, form.MinimumSize })
                {
                    form.Size = size;
                    var bodyBounds = Field<Control>(form, "body").Bounds;
                    foreach (var preset in Presets.All().Where(preset => !preset.Vanilla))
                    {
                        session.SelectPreset(preset.Id); form.RenderSession(); Application.DoEvents();
                        Assert(Field<Control>(form, "body").Bounds == bodyBounds,
                            "Selecting a quick configuration changed the workspace layout: " + preset.Id + " at " + size);
                    }
                }
                session.SelectPreset("vanilla_current"); form.RenderSession(); Application.DoEvents();
                Shot(form, Path.Combine(output, "preview-minimum.png"));
                form.Size = new Size(1240, 840); form.RenderSession(); Application.DoEvents();
                Shot(form, Path.Combine(output, "unchanged-factory.png"));
                session.SelectPreset(null);
                session.SetKeyboard("IA_Guard", 0, "RightMouseButton"); form.RenderSession();
                Assert(Rows(view).Length == 0, "A record edit with unchanged effective keys must not appear in the scheme diff.");
                session.Undo();
                session.ChangeRelationship("IA_Guard", "LeftMouseButton", true, false, -1);
                session.RestoreAction("IA_Guard", false, -1); form.RenderSession();
                Assert(!session.HasAnyDrafts && Rows(view).Length == 0, "Restoring an edited action to its saved factory bindings must clear its draft.");
                session.ReplaceRelationship("IA_HeavyAttack", "F", "G", false, -1); form.RenderSession();
                Control row = Rows(view).Single(r => Field<BindingDifference>(r, "Difference").Action == "IA_HeavyAttack");
                Assert(Parts(row).SequenceEqual(new[] { "+G", "−F" }), "Actual session replacement must use combined presentation by default.");
                int scope = session.ScopeChangeCount; byte[] before = File.ReadAllBytes(input);
                Click(row, false); Assert(Field<int>(row, "Mode") == 0, "Clicking device/action text must not toggle the key presentation.");
                Click(row, true); Assert(Field<int>(row, "Mode") == 1, "Mouse click on binding content must enter changes-only.");
                Key(row, Keys.Enter); Assert(Field<int>(row, "Mode") == 2, "Enter must cycle the focused row.");
                Key(row, Keys.Space); Assert(Field<int>(row, "Mode") == 3, "Space must cycle the focused row.");
                Click(row, true); Assert(Field<int>(row, "Mode") == 0, "Fourth activation must return to default.");
                var tip = Field<ToolTip>(view, "tip");
                Assert(tip.GetToolTip(row).Contains("原本: F") && tip.GetToolTip(row).Contains("纯结果: G"), "Tooltip must expose full before/result without replacing the row.");
                for (int mode = 0; mode < 4; mode++)
                {
                    Shot(form, Path.Combine(output, "mode-" + mode + "-zh-Hans.png"));
                    Shot(view, Path.Combine(output, "panel-" + mode + ".png"));
                    Click(row, true);
                }
                Click(row, true); Compare(view, true);
                row = Rows(view).Single(r => Field<BindingDifference>(r, "Difference").Action == "IA_HeavyAttack");
                Assert(Field<int>(row, "Mode") == 1, "Comparison switch must preserve this action's presentation mode.");
                string profile = session.SelectedProfile;
                string other = session.Profiles.First(p => p != profile); form.SelectProfile(other);
                session.ReplaceRelationship("IA_HeavyAttack", "F", "H", false, -1); form.RenderSession();
                Assert(Field<int>(Rows(view).Single(r => Field<BindingDifference>(r, "Difference").Action == "IA_HeavyAttack"), "Mode") == 0, "Other schemes must start in default mode.");
                session.Undo(); form.SelectProfile(profile);
                row = Rows(view).Single(r => Field<BindingDifference>(r, "Difference").Action == "IA_HeavyAttack");
                Assert(Field<int>(row, "Mode") == 1, "Returning to a scheme must restore its display preference.");
                form.ShowActionView(true); form.ShowActionView(false);
                Assert(Field<int>(row, "Mode") == 1, "Changing perspective must preserve row presentation.");
                for (int index = 0; index < languageCount; index++)
                {
                    language.SelectedIndex = index; form.Size = form.MinimumSize;
                    row = Rows(view).Single(r => Field<BindingDifference>(r, "Difference").Action == "IA_HeavyAttack");
                    for (int mode = 0; mode < 4; mode++)
                    {
                        Assert(!row.AccessibleName.Contains("views.diff"), "Each display mode must be localized.");
                        Shot(view, Path.Combine(output, "minimum-" + I18n.Languages[index] + "-" + Field<int>(row, "Mode") + ".png"));
                        Click(row, true);
                    }
                }
                Assert(session.ScopeChangeCount == scope && before.SequenceEqual(File.ReadAllBytes(input)), "Display modes must not edit bindings or write the save.");
                language.SelectedIndex = 0; form.Size = new Size(1500, 1000);
                session.Undo(); session.RestoreAction("IA_HeavyAttack", false, -1); session.RestoreAction("IA_Guard", false, -1);
                Assert(session.Apply().Success, "Preparing saved factory state must succeed on the copied fixture.");
                session.ChangeRelationship("IA_HeavyAttack", "RightMouseButton", true, false, -1);
                session.ChangeRelationship("IA_Guard", "LeftMouseButton", true, false, -1);
                Assert(session.Apply().Success, "Saved custom state must succeed on the copied fixture.");
                Compare(view, true); form.RenderSession();
                foreach (Control savedRow in Rows(view))
                {
                    while (Field<int>(savedRow, "Mode") != 0) Click(savedRow, true);
                    Assert(Tokens(savedRow).Where(t => Field<string>(t, "Text").StartsWith("+")).All(t => Field<Color>(t, "Color") == Color.FromArgb(64, 158, 255)),
                        "Saved factory additions must use blue while pending additions use yellow.");
                }
                Shot(form, Path.Combine(output, "saved-custom.png")); Shot(view, Path.Combine(output, "saved-custom-panel.png"));
                session.ReplaceRelationship("IA_HeavyAttack", "F", "G", false, -1); form.RenderSession();
                row = Rows(view).Single(r => Field<BindingDifference>(r, "Difference").Action == "IA_HeavyAttack");
                Assert(Tokens(row).Any(t => Field<string>(t, "Text") == "+右键" && Field<Color>(t, "Color") == Color.FromArgb(64, 158, 255))
                    && Tokens(row).Any(t => Field<string>(t, "Text") == "+G" && Field<Color>(t, "Color") == Color.FromArgb(240, 178, 78)),
                    "One row must distinguish saved additions from pending additions.");
                Shot(view, Path.Combine(output, "mixed-status-panel.png"));
                Assert(fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Original fixture must remain unchanged.");
            }
            Console.WriteLine("SchemeDiffViewTests PASS: " + checks + " checks; four modes, row click events and keyboard messages; " + languageCount + " languages; " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
