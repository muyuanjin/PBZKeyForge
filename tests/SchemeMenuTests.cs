using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

public static class SchemeMenuTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static Exception uiError;
    delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static T Field<T>(MainForm form, string name) { return (T)typeof(MainForm).GetField(name, Hidden).GetValue(form); }
    static void Set(MainForm form, string name, object value) { typeof(MainForm).GetField(name, Hidden).SetValue(form, value); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Call(MainForm form, string name) { typeof(MainForm).GetMethod(name, Hidden).Invoke(form, null); }

    static ContextMenuStrip Open(MainForm form, string button)
    {
        Field<Button>(form, button).PerformClick(); Application.DoEvents();
        ContextMenuStrip found = null;
        EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr parameter) {
            var menu = Control.FromHandle(window) as ContextMenuStrip;
            if (menu != null && menu.Visible) found = menu;
            return true;
        }, IntPtr.Zero);
        Check(found != null, button + " did not open its menu.");
        return found;
    }

    static void Click(ContextMenuStrip menu, ToolStripItem item)
    {
        var point = new Point(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
        var location = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(menu.Handle, 0x200, IntPtr.Zero, location);
        SendMessage(menu.Handle, 0x201, new IntPtr(1), location);
        SendMessage(menu.Handle, 0x202, IntPtr.Zero, location);
        Application.DoEvents();
        Check(uiError == null, "Menu click raised an exception: " + uiError);
        Check(!menu.Visible && !menu.IsDisposed, "Menu was destroyed during click dispatch.");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { uiError = e.Exception; };
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            ContextMenuStrip lastOptions = null, lastOverflow = null;
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                form.UiOffline(); I18n.Current = "zh-Hans";
                var configuration = new KeyEnhancementConfiguration { Enabled = true };
                configuration.EnsureSchemes(form.Session);
                for (int i = 0; i < 9; i++) configuration.AddScheme("测试方案 " + (i + 1));
                Set(form, "keyEnhancement", configuration); Set(form, "keyEnhancementBaseline", configuration.Clone());
                Set(form, "flameEffectsEnabled", false); form.RenderSession();
                form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                ((Timer)Field<Timer>(form, "gameTimer")).Stop();
                string before = configuration.ToJson();
                if (!args.Contains("--timing-only"))
                {
                    foreach (string button in new[] { "btnMore", "btnKeyEnhancementMore", "btnResourceModMore" })
                        for (int i = 0; i < 3; i++)
                        {
                            var menu = Open(form, button);
                            menu.Close(ToolStripDropDownCloseReason.AppClicked); Application.DoEvents();
                            Check(!menu.IsDisposed && !menu.Visible, button + " was destroyed while dismissing it on the editor background.");
                            Check(uiError == null && configuration.ToJson() == before,
                                button + " dismissal raised an exception or changed the configuration.");
                        }
                    for (int i = 0; i < 4; i++)
                    {
                        var menu = Open(form, "btnSchemeOptions");
                        menu.Close(ToolStripDropDownCloseReason.AppClicked); Application.DoEvents();
                        Check(!menu.IsDisposed && !menu.Visible, "Clicking the editor background destroys the scheme menu inside Closed.");
                        Check(configuration.ToJson() == before, "Dismissing the menu changed the scheme.");
                        lastOptions = menu;
                    }
                    var rename = Open(form, "btnSchemeOptions");
                    bool answered = false;
                    using (var timer = new Timer { Interval = 25 })
                    {
                        timer.Tick += delegate {
                            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(item => item != form);
                            if (dialog != null) { answered = true; timer.Stop(); dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                        };
                        timer.Start(); Click(rename, rename.Items[0]);
                    }
                    Check(answered && configuration.ToJson() == before, "Cancelling rename changed the scheme.");
                    lastOptions = rename;
                    lastOverflow = Open(form, "btnSchemeMore");
                    var destination = lastOverflow.Items.Cast<ToolStripMenuItem>().First(item => !item.Checked);
                    string id = (string)destination.Tag; Click(lastOverflow, destination);
                    Check(configuration.SelectedSchemeId == id, "Overflow menu did not select the requested scheme.");
                    lastOverflow = Open(form, "btnSchemeMore");
                    lastOverflow.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
                    Check(!lastOverflow.IsDisposed, "Keyboard dismissal destroyed the overflow menu inside Closed.");
                }

                Call(form, "RenderSchemeBar");
                var tabs = Field<EditorTab[]>(form, "schemeTabs");
                Check(tabs.All(tab => !tab.Dirty), "Unedited schemes are marked dirty.");
                string editedId = configuration.SelectedSchemeId;
                configuration.NativeBindings.First(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.Add("F11");
                Call(form, "RenderSchemeBar");
                Check(tabs.Single(tab => (string)tab.Tag == editedId).Dirty && tabs.Where(tab => (string)tab.Tag != editedId).All(tab => !tab.Dirty),
                    "A binding change must mark only its owning scheme.");
                string otherId = configuration.Schemes.First(item => item.Id != editedId).Id;
                configuration.SelectScheme(otherId); Call(form, "RenderSchemeBar");
                Check(tabs.Single(tab => (string)tab.Tag == editedId).Dirty && !tabs.Single(tab => (string)tab.Tag == otherId).Dirty,
                    "Switching schemes lost the dirty indicator or marked the new selection dirty.");
                configuration.CustomLua = "config.example = true"; Call(form, "RenderSchemeBar");
                Check(tabs.All(tab => tab.Dirty), "Shared custom code must mark every scheme dirty.");
                configuration.CustomLua = null;
                Set(form, "keyEnhancementBaseline", configuration.Clone()); Call(form, "RenderSchemeBar");
                Check(tabs.All(tab => !tab.Dirty), "Accepting a new baseline retained dirty indicators.");
                configuration.Rules.Add(new KeyEnhancementRule { Keys = { "F8" }, Outputs = { new KeyEnhancementOutput { Kind = "scheme", Tag = "next" } } });
                Call(form, "RenderSchemeBar");
                Check(tabs.All(tab => tab.Dirty), "Shared scheme switching shortcuts must mark every scheme dirty.");

                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++) Call(form, "RenderSchemeBar");
                Console.WriteLine("12-scheme bar refresh: {0:F2} ms", watch.Elapsed.TotalMilliseconds / 10);
                if (!args.Contains("--timing-only"))
                    using (var image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "scheme-menu.png")); }
            }
            if (!args.Contains("--timing-only"))
                Check(lastOptions.IsDisposed && lastOverflow.IsDisposed, "Disposing the editor leaked its menus.");
            Check(uiError == null, "The UI thread reported an exception: " + uiError);
            Console.WriteLine("PASS: " + checks + " scheme menu and dirty indicator assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
