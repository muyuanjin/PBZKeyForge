using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class VisualPositionTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static string output;
    static int checks;
    static bool desktopShots;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args)
    { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static IEnumerable<Control> All(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in All(child)) yield return descendant; } }
    static Rectangle Bounds(Control control)
    { return control.RectangleToScreen(control.ClientRectangle); }
    static void Same(RectangleF before, RectangleF after, string message)
    {
        Check(Math.Abs(before.X - after.X) < .1 && Math.Abs(before.Y - after.Y) < .1
            && Math.Abs(before.Width - after.Width) < .1 && Math.Abs(before.Height - after.Height) < .1,
            message + ": " + before + " -> " + after);
    }
    static void Shot(Form form, string name)
    {
        Application.DoEvents(); form.Refresh();
        using (var image = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, name + ".png")); }
        if (desktopShots && name == "trigger-editor")
        {
            bool topMost = form.TopMost;
            try
            {
                form.TopMost = true; form.Activate(); form.Refresh(); Application.DoEvents();
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(form.Location, Point.Empty, image.Size);
                    image.Save(Path.Combine(output, name + "-desktop.png"));
                }
            }
            finally { form.TopMost = topMost; }
        }
    }
    static void Render(MainForm form) { form.RenderSession(); Application.DoEvents(); }
    static BindingWorkspace Workspace(MainForm form) { return Field<BindingWorkspace>(form, "currentBindings"); }
    static Dictionary<string, RectangleF> Images(MainForm form)
    {
        var diagram = Field<Control>(Field<Control>(form, "keyBindings"), "diagram");
        Point origin = diagram.PointToScreen(Point.Empty);
        return Field<Dictionary<string, RectangleF>>(diagram, "imageBounds").ToDictionary(pair => pair.Key,
            pair => new RectangleF(origin.X + pair.Value.X, origin.Y + pair.Value.Y, pair.Value.Width, pair.Value.Height));
    }
    static void SameImages(Dictionary<string, RectangleF> before, MainForm form, string operation)
    { var after = Images(form); foreach (var pair in before) Same(pair.Value, after[pair.Key], operation + " moved " + pair.Key); }
    static Dictionary<string, Rectangle> Commands(MainForm form)
    {
        var view = Field<Control>(form, "keyBindings");
        var result = new[] { "add", "reset", "keyTools" }.ToDictionary(name => name, name => Bounds(Field<Control>(view, name)));
        foreach (string name in new[] { "btnApply", "lblBackup" }) result[name] = Bounds(Field<Control>(form, name));
        return result;
    }
    static void SameCommands(Dictionary<string, Rectangle> before, MainForm form)
    {
        var after = Commands(form); var view = Field<Control>(form, "keyBindings");
        foreach (var pair in before) Same(pair.Value, after[pair.Key], "Command moved: " + pair.Key);
        foreach (string name in new[] { "add", "reset", "keyTools" })
        {
            var button = Field<Control>(view, name);
            Check(button.Visible && button.Parent.ClientRectangle.Contains(button.Bounds), "Command is clipped: " + name);
            Check(Bounds(view).Contains(Bounds(button)), "Command is outside the key view: " + name);
        }
    }
    static void SelectKey(MainForm form, string key)
    {
        var diagram = Field<Control>(Field<Control>(form, "keyBindings"), "diagram");
        var box = Field<Dictionary<string, RectangleF>>(diagram, "targets")[key];
        Call(diagram, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1,
            (int)(box.X + box.Width / 2), (int)(box.Y + box.Height / 2), 0));
        Application.DoEvents();
    }
    static MainForm Open(Size size, string name)
    {
        var preferences = new EditorPreferences(Path.Combine(output, name, "preferences.txt"));
        preferences.SaveFlameEffects(false); preferences.SaveCompanionHotkey(false);
        var form = new MainForm(preferences); form.UiOffline(); I18n.Current = "zh-Hans";
        form.StartPosition = FormStartPosition.Manual; form.Location = Point.Empty; form.Show(); form.Size = size;
        Field<Timer>(form, "gameTimer").Stop(); Render(form); return form;
    }
    static void MainLayout(Size size)
    {
        string name = size.Width + "x" + size.Height;
        using (var form = Open(size, name))
        {
            SelectKey(form, "LeftMouseButton");
            var images = Images(form); var commands = Commands(form);
            var view = Field<Control>(form, "keyBindings");
            int rowWidth = ((Control)Field<IList>(view, "actionRows")[0]).Width;
            Workspace(form).Add("IA_Guard", new BindingEntry { Keys = new[] { "LeftMouseButton" }, Trigger = "default" }); Render(form);
            SameImages(images, form, "First edit"); SameCommands(commands, form);
            Workspace(form).Add("IA_Jump", new BindingEntry { Keys = new[] { "LeftMouseButton" }, Trigger = "default" }); Render(form);
            SameImages(images, form, "Third binding"); SameCommands(commands, form);
            Check(Field<IList>(view, "actionRows").Cast<Control>().All(row => row.Width == rowWidth), "Scrollbar changed binding row widths");
            Shot(form, name + "-bindings");
            Call(form, "UndoBindingEdit"); Call(form, "UndoBindingEdit"); Render(form);
            SameImages(images, form, "Undo last edit"); SameCommands(commands, form);
            Call(Field<Control>(form, "schemeDiff"), "SetComparison", true); Application.DoEvents();
            SameImages(images, form, "Comparison"); SameCommands(commands, form);
            Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Enabled = true; Render(form);
            Workspace(form).Add("IA_Jump", new BindingEntry { Keys = new[] { "NumPadOne" }, Trigger = "hold", Seconds = .5 }); Render(form);
            SameImages(images, form, "First extra input");
            foreach (string key in new[] { "NumPadTwo", "NumPadThree", "NumPadFour", "NumPadFive", "NumPadSix", "NumPadSeven", "NumPadEight", "NumPadNine" })
                Workspace(form).Add("IA_Jump", new BindingEntry { Keys = new[] { key }, Trigger = "hold", Seconds = .5 });
            Render(form); SameImages(images, form, "Wrapped extra inputs"); SameCommands(commands, form);
            Call(form, "SetMessage", "editor.failed", true, new object[] { "无法应用：custom.lua" }); Render(form);
            SameImages(images, form, "Error feedback"); SameCommands(commands, form);
            var notice = Field<Control>(form, "feedbackPanel"); var footer = Field<Control>(form, "footer");
            Check(notice.Visible && Bounds(footer).Contains(Bounds(notice)), "Feedback must stay inside the footer");
            Check(!Bounds(notice).IntersectsWith(Bounds(Field<Control>(form, "bindingWorkspace"))), "Feedback covers the editor");
            foreach (string buttonName in new[] { "feedbackDetails", "dismissFeedback" })
            {
                var button = All(notice).Single(control => control.Name == buttonName);
                Check(notice.ClientRectangle.Contains(button.Bounds) && button.Height >= button.Font.Height, "Feedback button is clipped: " + buttonName);
            }
            Shot(form, name + "-feedback");
            ((Button)All(notice).Single(control => control.Name == "dismissFeedback")).PerformClick();
            Check(!notice.Visible && Field<Control>(form, "lblStatus").Visible, "Dismissing feedback must restore the status");
            SameImages(images, form, "Dismiss feedback");
            form.ShowGamepad(true); Application.DoEvents(); var padImages = Images(form);
            Workspace(form).Add("IA_Guard", new BindingEntry { Keys = new[] { "Gamepad_FaceButton_Left" }, Trigger = "default" }); Render(form);
            SameImages(padImages, form, "Controller edit");
            form.ShowActionView(true); Application.DoEvents();
            var actions = Field<Control>(form, "unifiedActions"); var grid = Field<DataGridView>(actions, "actions");
            var groups = Field<TreeView>(actions, "groups"); groups.SelectedNode = groups.Nodes[0];
            Rectangle gridBounds = Bounds(grid), groupBounds = Bounds(groups);
            var search = Field<TextBox>(actions, "search");
            foreach (string query in new[] { "IA_Jump", "zz_no_such_action_zz", "" })
            {
                search.Text = query; Application.DoEvents();
                Same(gridBounds, Bounds(grid), "Search moved the grid"); Same(groupBounds, Bounds(groups), "Search moved navigation");
            }
            Field<KeyEnhancementConfiguration>(form, "keyEnhancement").Enabled = false; Render(form);
            Same(gridBounds, Bounds(grid), "Enhancement toggle moved the grid");
            Check(grid.DisplayedRowCount(false) >= 8, "Too few visible actions at " + size + ": " + grid.DisplayedRowCount(false) + "; grid=" + grid.ClientSize);
            Shot(form, name + "-actions");
            StatusIndicator(Field<Control>(form, "lblGame"));
            form.Dispose();
        }
    }
    static void StatusIndicator(Control label)
    {
        Rectangle? marker = null;
        foreach (string key in new[] { "live.connected", "live.paused", "live.waiting", "flow.notconnected" })
        {
            label.Text = I18n.Str(key); label.ForeColor = Color.FromArgb(116, 224, 165);
            using (var image = new Bitmap(label.Width, label.Height))
            {
                label.DrawToBitmap(image, label.ClientRectangle); var pixels = new List<Point>();
                for (int x = 0; x < 18; x++) for (int y = 0; y < image.Height; y++)
                    if (image.GetPixel(x, y).ToArgb() == label.ForeColor.ToArgb()) pixels.Add(new Point(x, y));
                Check(pixels.Count > 0, "Status indicator is missing for " + key);
                var bounds = Rectangle.FromLTRB(pixels.Min(point => point.X), pixels.Min(point => point.Y), pixels.Max(point => point.X) + 1, pixels.Max(point => point.Y) + 1);
                if (marker.HasValue) Same(marker.Value, bounds, "Status indicator moved"); else marker = bounds;
            }
        }
    }
    static void TriggerLayout()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "G", () => true))
        {
            dialog.Show(); Application.DoEvents(); var trigger = Field<ComboBox>(dialog, "trigger");
            Rectangle bounds = Bounds(trigger), save = Bounds(All(dialog).Single(control => control.Name == "saveBinding"));
            var scroll = All(dialog).OfType<Panel>().First(panel => panel.AutoScroll);
            for (int i = 0; i < trigger.Items.Count; i++)
            {
                trigger.SelectedIndex = i; Application.DoEvents();
                Same(bounds, Bounds(trigger), "Trigger chooser moved for mode " + i);
                Same(save, Bounds(All(dialog).Single(control => control.Name == "saveBinding")), "Save button moved");
                Check(!scroll.HorizontalScroll.Visible, "Trigger editor requires horizontal scrolling");
            }
            Shot(dialog, "trigger-editor"); dialog.Dispose();
        }
    }
    static void DetailPosition()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        for (int i = 1; i <= 12; i++) workspace.Add("IA_Guard", new BindingEntry { Keys = new[] { "F" + i }, Trigger = "hold", Seconds = .5 });
        using (var dialog = new UnifiedActionBindingsDialog(workspace, "IA_Guard", () => true, null))
        {
            dialog.Show(); Application.DoEvents(); var list = Field<FlowLayoutPanel>(dialog, "bindings");
            var row = list.Controls[8]; var toggle = All(row).OfType<Button>().Single(button => button.Name == "toggleBinding");
            toggle.Focus(); Application.DoEvents();
            Point position = list.AutoScrollPosition; string id = ((BindingEntry)row.Tag).Id;
            Check(position.Y < 0, "Detail fixture must scroll before editing");
            int width = row.Width; Shot(dialog, "detail-before");
            toggle.PerformClick(); Application.DoEvents();
            Check(!toggle.IsDisposed && toggle.Focused, "Toggle lost its control or keyboard focus");
            Check(list.AutoScrollPosition == position && row.Width == width, "Toggling changed the viewport");
            Check(!workspace.Entries("IA_Guard").Single(entry => entry.Id == id).Enabled, "Toggle did not disable the selected binding");
            toggle.PerformClick(); Application.DoEvents();
            Check(workspace.Entries("IA_Guard").Single(entry => entry.Id == id).Enabled, "Reused row retained a stale binding state");
            Check(list.AutoScrollPosition == position, "Repeated toggle moved the viewport");
            var anchor = list.Controls.Cast<Control>().First(control => control.Bottom > 0);
            int anchorTop = anchor.Top;
            workspace.Remove((BindingEntry)list.Controls[1].Tag); Call(dialog, "RenderBindings"); Application.DoEvents();
            Check(anchor.Top == anchorTop, "Removing a preceding row moved the reading anchor");
            Check(toggle.Focused, "Updating another row lost keyboard focus");
            All(row).OfType<Button>().Single(button => button.Name == "removeBinding").PerformClick(); Application.DoEvents();
            Check(toggle.IsDisposed && list.ContainsFocus, "Removing the focused binding did not focus its neighbor");
            Check(list.AutoScrollPosition.Y < 0, "Removing a binding reset the viewport");
            Shot(dialog, "detail-after"); dialog.Dispose();
        }
    }
    static void DifferenceReview()
    {
        using (var form = Open(new Size(1200, 720), "review"))
        {
            Workspace(form).Add("IA_Guard", new BindingEntry { Keys = new[] { "LeftMouseButton" }, Trigger = "default" }); Render(form);
            form.ShowActionView(true); Application.DoEvents();
            var diff = Field<Control>(form, "schemeDiff"); var before = Bounds(Field<Control>(form, "unifiedActions"));
            bool opened = false; Exception failure = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            using (var timer = new Timer { Interval = 50 })
            {
                timer.Tick += delegate
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window != form && window.Text == I18n.Str("views.schemediff"));
                    if (dialog == null)
                    {
                        if (DateTime.UtcNow > deadline)
                        {
                            timer.Stop(); failure = new Exception("Difference review did not open");
                            foreach (var window in Application.OpenForms.Cast<Form>().Where(window => window != form).ToArray()) window.Close();
                        }
                        return;
                    }
                    timer.Stop(); opened = true;
                    try
                    {
                        var review = All(dialog).Single(control => control.GetType().Name == "SchemeDiffView");
                        var rows = Field<Control>(review, "rows");
                        Check(rows.Visible && rows.Height > form.Font.Height * 5, "Expanded review hides its differences");
                        int differences = rows.Controls.Cast<Control>().Count(control => control.GetType().Name == "DifferenceRow");
                        Check(differences == 1, "Expanded review duplicates device scopes: " + differences);
                        Check(Field<bool>(form, "modalActive"), "Review allows background refresh");
                        ((Button)Field<Control>(review, "factory")).PerformClick(); Application.DoEvents();
                        Shot(dialog, "difference-review");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                };
                timer.Start(); ((Button)Field<Control>(diff, "title")).PerformClick();
            }
            Check(opened && failure == null, "Difference review failed: " + failure);
            Check((bool)diff.GetType().GetProperty("CompareFactory").GetValue(diff, null), "Review lost the comparison choice");
            Check(!Field<bool>(form, "modalActive"), "Review did not restore editing");
            Same(before, Bounds(Field<Control>(form, "unifiedActions")), "Review moved the action view");
            form.Dispose();
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); output = Path.GetFullPath(args.Length > 0 ? args[0] : "dist/visual-position-tests"); Directory.CreateDirectory(output);
            desktopShots = args.Contains("--desktop-shots");
            foreach (var size in new[] { new Size(980, 710), new Size(1200, 720), new Size(1400, 900) }) MainLayout(size);
            TriggerLayout(); DetailPosition(); DifferenceReview(); Console.WriteLine("VisualPositionTests PASS: " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
