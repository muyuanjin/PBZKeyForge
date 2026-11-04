using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class RefreshScopeTests
{
    static int checks;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static void Tick(MainForm form)
    { typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Hidden).Invoke(Field<System.Windows.Forms.Timer>(form, "gameTimer"), new object[] { EventArgs.Empty }); }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    static void TableSize(MainForm form, string hostName)
    {
        var tables = Descendants(Field<Panel>(form, hostName)).OfType<TableLayoutPanel>().Where(t => t.ColumnCount == 5).ToArray();
        var header = tables.Single(t => t.Controls.Count == 5);
        var body = tables.Single(t => t.Controls.Count > 5);
        float scale;
        using (var graphics = form.CreateGraphics()) scale = graphics.DpiX / 96f;
        Assert(header.Height == (int)(36 * scale) && header.GetRowHeights().Single() == header.Height,
            "A lazily created header was scaled more than once or retained an oversized row.");
        Assert(body.GetRowHeights().All(height => height == (int)(42 * scale)), "Lazily created binding rows have the wrong DPI scale.");
        Assert(header.GetColumnWidths().SequenceEqual(body.GetColumnWidths()), "Lazy table columns do not align at the current DPI.");
        var heading = (Label)header.GetControlFromPosition(2, 0);
        Assert(heading.Padding.Left == heading.Padding.Right, "The binding-column heading has asymmetric padding.");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--fake-game") { Thread.Sleep(30000); return 0; }
        Process game = null;
        try
        {
            Application.EnableVisualStyles();
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav");
            byte[] original = File.ReadAllBytes(args[0]); File.WriteAllBytes(input, original);
            File.WriteAllBytes(Path.Combine(output, "PBZSharedSetting.sav"), new byte[] { 1 });
            string fakeGame = Path.Combine(output, "PBZ.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, fakeGame, true);
            File.Copy(typeof(MainForm).Assembly.Location, Path.Combine(output, "PBZKeyForge.exe"), true);
            game = Process.Start(new ProcessStartInfo(fakeGame, "--fake-game") { UseShellExecute = false, CreateNoWindow = true });
            using (var beforeShown = new MainForm())
            {
                beforeShown.Opacity = 0; beforeShown.ShowInTaskbar = false; beforeShown.UiSmoke(input);
                beforeShown.ShowActionView(true); beforeShown.Show(); Application.DoEvents();
                TableSize(beforeShown, "kbHost");
                beforeShown.Width -= 80; beforeShown.RenderSession(); Application.DoEvents();
                TableSize(beforeShown, "kbHost");
            }
            using (var form = new MainForm())
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.UiSmoke(input);
                Assert(Field<bool>(form, "lastRunning"), "Opening an installed save must query the current process state.");
                form.Show(); Application.DoEvents();
                var apply = Field<Button>(form, "btnApply");
                Assert(!apply.Enabled && apply.Text == I18n.Str("editor.applyrunning"), "Running game did not explain the disabled Apply button.");
                Assert((bool)apply.GetType().GetProperty("GameRunning").GetValue(apply, null), "Running game did not use the blocked appearance.");
                Assert(Field<Label>(form, "lblStatus").Text == I18n.Str("editor.exittoapply"), "Scheme authentication feedback hid the instruction to exit the game.");
                using (var image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, form.ClientRectangle); image.Save(Path.Combine(output, "game-running.png")); }
                var keyboard = Field<IDictionary>(form, "kbButtons"); var controller = Field<IDictionary>(form, "padButtons");
                Assert(keyboard.Count == 0 && controller.Count == 0, "Hidden action tables were created during startup.");
                var view = Field<Control>(form, "keyBindings");
                var diagram = Field<Control>(view, "diagram");
                object relations = Field<object>(view, "relations");
                object before = Field<object>(view, "before");
                object hits = Field<object>(diagram, "hitTargets");
                foreach (string key in new[] { "F", "RightMouseButton", "LeftMouseButton" })
                {
                    Point point = Point.Round((PointF)diagram.GetType().GetMethod("Position", Hidden).Invoke(diagram, new object[] { key }));
                    typeof(Control).GetMethod("OnMouseClick", Hidden).Invoke(diagram, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
                    Assert(Field<string>(view, "selectedKey") == key, "Selecting a key did not refresh the details.");
                    Assert(ReferenceEquals(relations, Field<object>(view, "relations")) && ReferenceEquals(before, Field<object>(view, "before")), "Selecting a key recomputed binding data.");
                    Assert(ReferenceEquals(hits, Field<object>(diagram, "hitTargets")), "Selecting a key rebuilt layout hit targets.");
                }
                typeof(MainForm).GetField("lastRunning", Hidden).SetValue(form, false);
                form.RenderSession();
                Assert(!Field<bool>(form, "lastRunning"), "Rendering queried processes instead of using the detected state.");
                relations = Field<object>(view, "relations");
                Tick(form);
                Assert(Field<bool>(form, "lastRunning"), "The process timer failed to detect the running game.");
                Assert(ReferenceEquals(relations, Field<object>(view, "relations")), "Process-state detection rebuilt binding data.");
                form.ShowGamepad(true); form.ShowActionView(true);
                Assert(controller.Count == MainForm.Actions.Count && keyboard.Count == 0, "The first controller-table visit created the hidden keyboard table.");
                TableSize(form, "padHost");
                form.ShowGamepad(false);
                Assert(keyboard.Count == MainForm.Actions.Count, "The keyboard table was not created on its first visit.");
                TableSize(form, "kbHost");
                var button = ((BindingButton[])keyboard["IA_Guard"])[0];
                string saved = button.Value;
                form.ShowActionView(false);
                form.Session.SetKeyboard("IA_Guard", 0, "G"); form.RenderSession();
                Assert(button.Value == saved, "A hidden action table was refreshed.");
                form.ShowActionView(true);
                Assert(button.Value == "G" && button.Changed, "Returning to the action table retained stale bindings.");
                form.ShowGamepad(true); form.Session.SetGamepadFamily("IA_Guard", 0, form.SelectedFamilyIndex, "Gamepad_FaceButton_Top"); form.RenderSession();
                var pad = ((BindingButton[])controller["IA_Guard"])[0];
                Assert(pad.Value == "Gamepad_FaceButton_Top", "The visible controller table was not refreshed.");
                form.ShowGamepad(false); form.ShowActionView(false); form.Session.Undo(); form.RenderSession();
                Assert(ReferenceEquals(diagram, Field<Control>(view, "diagram")), "Switching views replaced the device diagram.");
                game.Kill(); game.WaitForExit(); Tick(form);
                Assert(!Field<bool>(form, "lastRunning") && apply.Text == I18n.Str("editor.apply"), "Game exit did not restore the Apply label.");
                Assert(!(bool)apply.GetType().GetProperty("GameRunning").GetValue(apply, null), "Game exit retained the blocked appearance.");
                Assert(!apply.Enabled, "Game exit enabled Apply without a draft.");
                form.Session.SetKeyboard("IA_Guard", 0, "G"); form.RenderSession();
                Assert(apply.Enabled, "Game exit did not allow applying a draft.");
                form.Session.Undo(); form.RenderSession();
                form.UiOffline(); Tick(form);
                Assert(!Field<bool>(form, "lastRunning") && Field<Label>(form, "lblGame").Text.Contains(I18n.Str("offline.mode")), "Offline editing retained a game-running status.");
                Assert(apply.Enabled && apply.Text == I18n.Str("offline.export"), "Offline export retained the game-running button state.");
                Assert(original.SequenceEqual(File.ReadAllBytes(input)), "Display updates wrote the input save.");
            }
            Console.WriteLine("RefreshScopeTests PASS: " + checks + " checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (game != null) { if (!game.HasExited) game.Kill(); game.Dispose(); } }
    }
}
