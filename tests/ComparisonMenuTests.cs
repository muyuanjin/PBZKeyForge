using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class ComparisonMenuTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static Color Theme(string name)
    { return (Color)typeof(MainForm).Assembly.GetType("PBZKeyForge.EditorTheme").GetField(name).GetValue(null); }
    static ContextMenuStrip Open(MainForm form)
    {
        Call(form, "ShowMore"); Application.DoEvents();
        var menu = Field<ContextMenuStrip>(form, "moreMenu");
        Check(menu.Visible, "More menu is visible");
        foreach (ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
            Check(!string.IsNullOrWhiteSpace(item.Text) && item.Text == I18n.Str(item.Name), "Visible translated menu label: " + item.Name);
        return menu;
    }
    static void Selection(ToolStripMenuItem comparison, bool factory)
    {
        var choices = comparison.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
        Check(choices.Length == 2 && choices.Count(item => item.Checked) == 1, "Exactly one comparison is checked");
        Check(((ToolStripMenuItem)comparison.DropDownItems[factory ? "views.vsfactory" : "views.vssaved"]).Checked, "Current comparison is checked");
        foreach (var item in choices) Check(item.Text == I18n.Str(item.Name), "Translated comparison label: " + item.Name);
    }
    static void Capture(ContextMenuStrip menu, ToolStripMenuItem comparison, string path)
    {
        comparison.Select(); comparison.ShowDropDown(); Application.DoEvents();
        Check(comparison.DropDown.Visible, "Comparison submenu opens");
        Check(comparison.DropDown.Renderer == menu.Renderer, "Submenu inherits the themed renderer");
        Rectangle bounds = Rectangle.Union(menu.Bounds, comparison.DropDown.Bounds);
        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            bitmap.Save(path);
        }
    }
    static bool HasTextColor(ToolStrip menu, ToolStripItem item, Color color)
    {
        using (var bitmap = new Bitmap(item.Width, item.Height))
        {
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(menu.PointToScreen(item.Bounds.Location), Point.Empty, bitmap.Size);
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) return true;
            return false;
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form, new object[] { EditorSession.CreateOffline() });
                I18n.Current = "zh-Hans"; Call(form, "ApplyLanguage");
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(50, 50);
                form.Show(); form.RenderSession(); Application.DoEvents();
                var menu = Open(form);
                var comparison = (ToolStripMenuItem)menu.Items["views.schemediff"];
                Check(comparison != null, "Comparison parent has a translation key");
                bool enabledText = false, arrow = false;
                menu.Renderer.RenderItemText += delegate(object sender, ToolStripItemTextRenderEventArgs e) {
                    Check(e.TextColor == Theme(e.Item.Enabled ? "Ink" : "Muted"), "Rendered text uses readable theme color: " + e.Text);
                    if (e.Item.Enabled) enabledText = true;
                };
                menu.Renderer.RenderArrow += delegate(object sender, ToolStripArrowRenderEventArgs e) {
                    Check(e.ArrowColor == Theme(e.Item.Enabled ? "Ink" : "Muted"), "Submenu arrow is readable"); arrow = true;
                };
                Selection(comparison, false);
                Capture(menu, comparison, Path.Combine(output, "before-edits.png"));
                ((ToolStripMenuItem)comparison.DropDownItems["views.vsfactory"]).PerformClick(); menu.Close();
                Check((bool)Field<object>(form, "schemeDiff").GetType().GetProperty("CompareFactory").GetValue(Field<object>(form, "schemeDiff"), null), "Factory choice changes the actual comparison");
                menu = Open(form); Selection(comparison, true);
                Capture(menu, comparison, Path.Combine(output, "factory.png")); menu.Close();
                Call(Field<object>(form, "schemeDiff"), "SetComparison", false);
                menu = Open(form); Selection(comparison, false); menu.Close();
                menu = Open(form);
                menu.Items["editor.resetall"].Enabled = false;
                Capture(menu, comparison, Path.Combine(output, "disabled.png"));
                Check(HasTextColor(menu, menu.Items["editor.resetall"], Theme("Muted")), "Disabled label visibly uses the muted theme color");
                Check(enabledText && arrow, "Real menu paints readable labels and submenu arrows");
                menu.Close();
            }
            Console.WriteLine("ComparisonMenuTests PASS: " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
