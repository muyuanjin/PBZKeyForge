using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class DialogPositionTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                form.UiOffline();
                form.StartPosition = FormStartPosition.Manual; form.Show(); Application.DoEvents();
                var workspace = (BindingWorkspace)typeof(MainForm).GetField("currentBindings", Hidden).GetValue(form);
                string action = workspace.Functions.First().Id;
                int index = 0;
                foreach (var screen in Screen.AllScreens)
                {
                    Rectangle area = screen.WorkingArea;
                    form.WindowState = FormWindowState.Normal;
                    form.Bounds = new Rectangle(area.Left + 80, area.Top + 60, 900, 720);
                    Application.DoEvents();
                    Modeless(form, workspace, action, output, index + "-normal");
                    form.Location = new Point(area.Right - 220, area.Bottom - 180);
                    Application.DoEvents();
                    Modeless(form, workspace, action, output, index + "-edge");
                    form.Bounds = new Rectangle(area.Left + 80, area.Top + 60, 900, 720);
                    form.WindowState = FormWindowState.Maximized; Application.DoEvents();
                    Modeless(form, workspace, action, output, index + "-maximized");
                    form.WindowState = FormWindowState.Normal;
                    using (var dialog = new UnifiedBindingEditDialog(workspace, action, null, null, null))
                    {
                        dialog.Shown += delegate { Positioned(dialog, form, "modal"); dialog.BeginInvoke((Action)dialog.Close); };
                        dialog.ShowDialog(form);
                    }
                    index++;
                }
                using (var manual = new EditorWindow { StartPosition = FormStartPosition.Manual, Location = form.Location, Size = new Size(400, 300) })
                {
                    Point expected = manual.Location;
                    manual.Show(form); Application.DoEvents();
                    Check(manual.Location == expected, "Manual window positioning changed.");
                }
            }
            Console.WriteLine("DialogPositionTests PASS " + checks + " screens=" + Screen.AllScreens.Length);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void Modeless(MainForm owner, BindingWorkspace workspace, string action, string output, string name)
    {
        using (var dialog = new UnifiedBindingEditDialog(workspace, action, null, null, null))
        {
            typeof(MainForm).GetMethod("PresentBindingWindow", Hidden).Invoke(owner, new object[] { dialog, owner, null });
            Application.DoEvents();
            Check(!dialog.Modal && dialog.Owner == owner, name + ": modeless binding owner changed.");
            Positioned(dialog, owner, name);
            using (var nested = new UnifiedActionBindingsDialog(workspace, action, null, null))
            {
                nested.Show(dialog); Application.DoEvents();
                Positioned(nested, dialog, name + " nested");
                nested.Close();
            }
            Rectangle bounds = Screen.FromControl(owner).Bounds;
            using (var image = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                image.Save(Path.Combine(output, name + ".png"));
            }
            dialog.Close(); Application.DoEvents();
        }
    }

    static void Positioned(Form dialog, Form owner, string name)
    {
        Rectangle area = Screen.FromControl(owner).WorkingArea;
        int x = Math.Max(area.Left, Math.Min(owner.Left + (owner.Width - dialog.Width) / 2, area.Right - dialog.Width));
        int y = Math.Max(area.Top, Math.Min(owner.Top + (owner.Height - dialog.Height) / 2, area.Bottom - dialog.Height));
        Check(Screen.FromControl(dialog).DeviceName == Screen.FromControl(owner).DeviceName,
            name + ": dialog opened on a different monitor: owner=" + owner.Bounds + " dialog=" + dialog.Bounds);
        if (dialog.Modal)
            Check(area.Contains(dialog.Bounds), name + ": modal dialog extends outside the owner working area.");
        else
            Check(Math.Abs(dialog.Left - x) <= 1 && Math.Abs(dialog.Top - y) <= 1,
                name + ": dialog was not centered and kept in the owner working area: expected=" + new Point(x, y) + " actual=" + dialog.Location);
    }
}
