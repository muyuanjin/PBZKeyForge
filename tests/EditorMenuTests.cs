using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using PBZKeyForge;

// Exercises the real menu mouse-message path, including closure after click dispatch.
public static class EditorMenuTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static readonly List<Exception> errors = new List<Exception>();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    delegate bool EnumWindow(IntPtr window, IntPtr param);
    [DllImport("user32.dll")]
    static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr param);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int size);

    static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    static T Field<T>(MainForm form, string name)
    { return (T)typeof(MainForm).GetField(name, Hidden).GetValue(form); }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
    static ContextMenuStrip Open(MainForm form)
    {
        Field<Button>(form, "btnMore").PerformClick();
        Application.DoEvents();
        var menu = Field<ContextMenuStrip>(form, "moreMenu");
        Assert(menu != null && menu.Visible && !menu.IsDisposed, "More did not open a live menu.");
        return menu;
    }
    static void MouseClick(ContextMenuStrip menu, string key)
    {
        Console.WriteLine("Click: " + key);
        var item = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == I18n.Str(key));
        Assert(item.Enabled, "Menu item unexpectedly disabled: " + key);
        var point = new Point(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
        IntPtr location = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(menu.Handle, 0x200, IntPtr.Zero, location);
        SendMessage(menu.Handle, 0x201, (IntPtr)1, location);
        SendMessage(menu.Handle, 0x202, IntPtr.Zero, location);
        Application.DoEvents();
        Assert(errors.Count == 0, "Unhandled menu click exception: " + (errors.Count == 0 ? "" : errors[0].ToString()));
        Assert(!menu.IsDisposed, "Menu was disposed before WinForms completed the click: " + key);
        Assert(!menu.Visible, "Menu did not close after selecting: " + key);
    }
    static void DialogClick(MainForm form, string key, DialogResult result, bool nativeFilePicker)
    {
        Exception failure = null;
        bool answered = false;
        var menu = Open(form);
        using (var timer = new Timer { Interval = 75 })
        {
            int ticks = 0;
            timer.Tick += delegate
            {
                try
                {
                    Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form);
                    if (dialog != null)
                    {
                        timer.Stop();
                        if (key == "btn.about")
                            Assert(dialog.Text == "PBZKeyForge " + typeof(Program).Assembly.GetName().Version.ToString(3),
                                "About shows a stale version.");
                        Descendants(dialog).OfType<Button>().Single(button => button.DialogResult == result).PerformClick();
                        answered = true;
                    }
                    else if (nativeFilePicker)
                    {
                        IntPtr popup = IntPtr.Zero;
                        EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr param)
                        {
                            var name = new StringBuilder(128);
                            GetClassName(window, name, name.Capacity);
                            if (name.ToString() == "#32770") popup = window;
                            return true;
                        }, IntPtr.Zero);
                        if (popup != IntPtr.Zero)
                        {
                            timer.Stop();
                            SendMessage(popup, 0x10, IntPtr.Zero, IntPtr.Zero);
                            answered = true;
                        }
                    }
                    if (++ticks > 80)
                    {
                        timer.Stop();
                        throw new Exception("Expected dialog did not open: " + key);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form);
                    if (dialog != null) dialog.Close();
                }
            };
            timer.Start();
            MouseClick(menu, key);
        }
        if (failure != null) throw failure;
        Assert(answered, "Menu action did not show its dialog: " + key);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { errors.Add(e.Exception); };
            string input = Path.GetFullPath(args[0]);
            byte[] before = File.ReadAllBytes(input);
            ContextMenuStrip owned;
            using (var form = new MainForm())
            {
                form.ShowInTaskbar = false;
                form.UiSmoke(input); form.Show(); Application.DoEvents();
                // The reported failure requires mouse-up/auto-close; PerformClick alone misses it.
                owned = Open(form);
                MouseClick(owned, "editor.openbackup");
                Assert(!Field<bool>(form, "messageError"), "Explorer launch reported an error.");
                Assert(Directory.Exists(GameSchemeSettings.BackupDirectory()), "Backup directory is missing.");
                for (int i = 0; i < 3; i++) MouseClick(Open(form), "editor.openbackup");
                DialogClick(form, "btn.about", DialogResult.OK, false);
                DialogClick(form, "editor.changefile", DialogResult.Cancel, true);
                Assert(form.Session.InputPath == input, "Cancelling the file picker changed the save.");
                DialogClick(form, "editor.resetall", DialogResult.Cancel, false);
                Assert(!form.Session.HasChanges, "Cancelling reset created changes.");
                DialogClick(form, "editor.resetall", DialogResult.OK, false);
                Assert(form.Session.IsResetPending, "Reset did not stage a preview.");
                var pending = Open(form);
                Assert(!pending.Items.Cast<ToolStripItem>().Single(i => i.Text == I18n.Str("editor.resetall")).Enabled,
                    "Reset must be disabled while reset is pending.");
                pending.Close(); Application.DoEvents();
                Field<Button>(form, "btnUndo").PerformClick();
                Assert(!form.Session.IsResetPending && !form.Session.HasChanges, "Undo did not cancel the reset.");
                MouseClick(Open(form), "editor.rediscover");
                form.UiSmoke(input);
                // A pending draft must survive cancelling Change save and Find game again.
                form.Session.SetKeyboard("IA_Jump", 0, "F8"); form.RenderSession();
                DialogClick(form, "editor.changefile", DialogResult.No, false);
                DialogClick(form, "editor.rediscover", DialogResult.No, false);
                Assert(form.Session.InputPath == input && form.Session.GetKeyboard("IA_Jump", 0) == "F8",
                    "Cancelling navigation discarded the draft.");
                Field<Button>(form, "btnUndo").PerformClick();
                var language = Field<ComboBox>(form, "cbLang");
                int languageCount = args.Contains("--all-languages") ? I18n.Languages.Length : 1;
                for (int i = 0; i < languageCount; i++)
                {
                    language.SelectedIndex = i;
                    var menu = Open(form);
                    Assert(menu == owned, "More replaced its menu instead of retaining ownership.");
                    foreach (var key in new[] { "editor.changefile", "editor.rediscover", "editor.resetall", "editor.openbackup", "btn.about" })
                        Assert(menu.Items.Cast<ToolStripItem>().Any(item => item.Text == I18n.Str(key)), "Menu language is stale: " + key);
                    menu.Close(); Application.DoEvents();
                    Assert(!menu.IsDisposed, "Dismissing the menu disposed it.");
                }
                Field<Button>(form, "btnRefresh").PerformClick();
                Assert(before.SequenceEqual(File.ReadAllBytes(input)), "Menu actions unexpectedly wrote the input save.");
                form.captionClose.PerformClick(); Application.DoEvents();
                Assert(form.IsDisposed, "Window close button did not close the clean form.");
            }
            Assert(owned.IsDisposed, "Form disposal leaked its menu.");
            Assert(errors.Count == 0, "UI thread reported an unhandled exception.");
            Console.WriteLine("PASS: " + checks + " menu assertions; all five menu actions exercised through mouse messages.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
