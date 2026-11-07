using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class GuiStabilityTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static string output;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static IEnumerable<Control> All(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var item in All(child)) yield return item; } }
    static void Shot(Form form, string name)
    {
        form.Refresh();
        using (var image = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, name + ".png")); }
    }
    static void Measure(string name, Action action)
    {
        var watch = Stopwatch.StartNew(); action(); watch.Stop();
        Console.WriteLine(name + " " + watch.ElapsedMilliseconds + " ms");
    }
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void StableRefresh(MainForm form, string name)
    {
        var original = All(form).ToDictionary(control => control, control => control.Bounds);
        int moves = 0;
        EventHandler moved = delegate(object sender, EventArgs e) { moves++; var c = (Control)sender; Console.WriteLine("Moved " + c.GetType().Name + "/" + c.Name + ": " + original[c] + " -> " + c.Bounds); };
        foreach (var control in original.Keys) { control.LocationChanged += moved; control.SizeChanged += moved; }
        try { Measure(name, delegate { form.RenderSession(); form.Refresh(); }); }
        finally { foreach (var control in original.Keys) { control.LocationChanged -= moved; control.SizeChanged -= moved; } }
        Check(moves == 0, name + " moved controls during refresh: " + moves);
        Check(original.All(pair => !pair.Key.IsDisposed && pair.Key.Bounds == pair.Value), name + " changed control geometry");
    }
    static string RowId(object view, int index)
    {
        var rows = Field<System.Collections.IList>(view, "rows");
        return Field<BindingFunction>(rows[index], "Function").Id;
    }
    static void CheckTreeBackground(TreeView tree)
    {
        using (var before = new Bitmap(tree.Width, tree.Height))
        {
            tree.DrawToBitmap(before, tree.ClientRectangle);
            using (var erased = (Bitmap)before.Clone())
            {
                using (var graphics = Graphics.FromImage(erased))
                {
                    IntPtr dc = graphics.GetHdc();
                    try { SendMessage(tree.Handle, 0x0014, dc, IntPtr.Zero); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                int changed = 0;
                for (int y = 0; y < tree.Height; y += 2)
                    for (int x = 0; x < tree.Width; x += 2)
                        if (before.GetPixel(x, y) != erased.GetPixel(x, y)) changed++;
                Check(changed == 0, "Background erasure exposed an incomplete action tree frame: " + changed);
            }
        }
    }
    static async System.Threading.Tasks.Task Run(MainForm form)
    {
        Field<Timer>(form, "gameTimer").Stop();
        I18n.Current = "zh-Hans";
        form.RenderSession();
        Call(form, "EnableKeyEnhancementEditing"); form.RenderSession();
        float scale = form.CurrentAutoScaleDimensions.Width / 96f;
        foreach (var size in new[] { new Size(980, 710), new Size(1200, 720), new Size(1400, 900) })
        {
            form.ClientSize = new Size((int)(size.Width * scale), (int)(size.Height * scale));
            form.ShowActionView(false); await System.Threading.Tasks.Task.Delay(30);
            for (int i = 0; i < 3; i++) StableRefresh(form, "keys " + size + " refresh");
            form.ShowActionView(true); await System.Threading.Tasks.Task.Delay(30);
            var view = Field<UnifiedActionView>(form, "unifiedActions");
            var tree = Field<TreeView>(view, "groups"); tree.SelectedNode = tree.Nodes[0];
            var grid = Field<DataGridView>(view, "actions");
            grid.CurrentCell = grid[2, 100]; grid.FirstDisplayedScrollingRowIndex = 95;
            for (int i = 0; i < 3; i++)
            {
                StableRefresh(form, "actions " + size + " refresh");
                Check(grid.FirstDisplayedScrollingRowIndex == 95 && grid.CurrentCell.RowIndex == 100 && grid.CurrentCell.ColumnIndex == 2,
                    "Refresh lost the action viewport or selection");
            }
            Shot(form, "actions-" + size.Width);
        }
        var actions = Field<UnifiedActionView>(form, "unifiedActions");
        var actionTree = Field<TreeView>(actions, "groups");
        CheckTreeBackground(actionTree);
        typeof(Control).GetMethod("RecreateHandle", Hidden).Invoke(actionTree, null);
        CheckTreeBackground(actionTree);
        var actionGrid = Field<DataGridView>(actions, "actions");
        var workspace = Field<BindingWorkspace>(form, "currentBindings");
        string topId = RowId(actions, actionGrid.FirstDisplayedScrollingRowIndex), selectedId = RowId(actions, actionGrid.CurrentCell.RowIndex);
        workspace.AddCustomDefinition(new KeyEnhancementOutput { Kind = "lua", Code = "return true" }, "稳定定位测试");
        form.RenderSession();
        Check(RowId(actions, actionGrid.FirstDisplayedScrollingRowIndex) == topId && RowId(actions, actionGrid.CurrentCell.RowIndex) == selectedId,
            "Changing the catalog lost the action identity under the viewport or selection");
        workspace = Field<BindingWorkspace>(form, "currentBindings");
        var geometry = All(form).ToDictionary(control => control, control => control.Bounds);
        var overlay = Field<object>(form, "keyEnhancementFlame");
        overlay.GetType().GetProperty("FlameEnabled", Hidden).SetValue(overlay, true, null);
        form.Activate();
        await System.Threading.Tasks.Task.Delay(200);
        PostMessage(actionGrid.Handle, 0x200, IntPtr.Zero, new IntPtr(10 | (10 << 16)));
        await System.Threading.Tasks.Task.Delay(30);
        int quietTick = Field<int>(overlay, "tick");
        for (int i = 0; i < 12; i++)
        {
            PostMessage(actionGrid.Handle, 0x200, IntPtr.Zero, new IntPtr((10 + i) | (10 << 16)));
            await System.Threading.Tasks.Task.Delay(25);
        }
        Check(Field<int>(overlay, "tick") > quietTick, "Animation stopped during ongoing mouse input");
        await System.Threading.Tasks.Task.Delay(400);
        Check(Field<int>(overlay, "tick") > quietTick, "Animation did not resume after input settled");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            UnifiedBindingEditDialog dialog = null;
            Measure("editor construction", delegate { dialog = new UnifiedBindingEditDialog(workspace, "IA_Jump", null, "G", () => true); });
            using (dialog)
            {
                workspace.LiveEditingProvider = null; workspace.LiveEditing = true;
                Call(form, "PresentBindingWindow", dialog, form, null);
                await System.Threading.Tasks.Task.Delay(30);
                Check((bool)overlay.GetType().GetProperty("AnimationPaused", Hidden).GetValue(overlay, null), "Live editor left background animation running");
                var trigger = Field<ComboBox>(dialog, "trigger");
                var save = All(dialog).Single(control => control.Name == "saveBinding");
                Rectangle triggerBounds = trigger.RectangleToScreen(trigger.ClientRectangle), saveBounds = save.Bounds;
                Size dialogSize = dialog.Size;
                trigger.Focus();
                for (int i = 0; i < trigger.Items.Count; i++)
                {
                    int index = i;
                    Measure("trigger " + i, delegate { trigger.SelectedIndex = index; dialog.Refresh(); });
                    Check(trigger.RectangleToScreen(trigger.ClientRectangle) == triggerBounds && save.Bounds == saveBounds && dialog.Size == dialogSize,
                        "Changing trigger mode moved the chooser, save button or window");
                    Check(!Field<Panel>(dialog, "scroll").HorizontalScroll.Visible, "Trigger editor acquired a horizontal scrollbar");
                    await System.Threading.Tasks.Task.Delay(20);
                }
                var number = Field<NumericUpDown>(dialog, "damageMultiplier");
                Measure("ten tuning edits", delegate { for (int i = 0; i < 10; i++) number.Value += .01m; dialog.Refresh(); });
                Check(save.Bounds == saveBounds && dialog.Size == dialogSize, "Tuning moved the save button or window");
                Shot(dialog, "editor-" + cycle);
                ((Button)All(dialog).Single(control => control.Name == "cancel")).PerformClick();
                await System.Threading.Tasks.Task.Delay(30);
                Check(dialog.IsDisposed && !(bool)overlay.GetType().GetProperty("AnimationPaused", Hidden).GetValue(overlay, null), "Closing the editor failed to restore the main window");
                Check(geometry.All(pair => !pair.Key.IsDisposed && pair.Key.Bounds == pair.Value), "Popup round trip moved the main window's controls");
            }
        }
        Shot(form, "actions-after-popups");
        Console.WriteLine("GuiStabilityTests PASS: " + checks);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var preferences = new EditorPreferences(Path.Combine(output, "preferences.txt"));
        preferences.SaveFlameEffects(false); preferences.SaveCompanionHotkey(false);
        int result = 0;
        using (var form = new MainForm(preferences))
        {
            form.TopMost = true;
            form.UiOffline();
            form.Shown += delegate { form.BeginInvoke((Action)(async delegate {
                try { await Run(form); }
                catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                finally { form.Dispose(); Application.ExitThread(); }
            })); };
            Application.Run(form);
        }
        return result;
    }
}
