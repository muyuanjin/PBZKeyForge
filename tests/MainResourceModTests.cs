using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class MainResourceModTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Toggle(CheckBox checkbox) { SendMessage(checkbox.Handle, 0xf5, IntPtr.Zero, IntPtr.Zero); Application.DoEvents(); }
    static void Wait(MainForm form)
    {
        var watch = Stopwatch.StartNew();
        while (form.IsApplying && watch.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(1); }
        Assert(!form.IsApplying, "Apply did not complete."); Application.DoEvents();
    }
    static System.Collections.Generic.IEnumerable<Control> All(Control root)
    { foreach (Control child in root.Controls) { yield return child; foreach (var item in All(child)) yield return item; } }
    static void Shot(MainForm form, string output, string name)
    {
        form.BringToFront(); form.Activate(); form.Refresh(); Application.DoEvents();
        var card = Field<CheckBox>(form, "chkPerfectDeflectNoRapidClickPenalty"); var help = Field<Button>(form, "btnModHelp");
        using (var graphics = card.CreateGraphics())
        {
            object header = card.GetType().GetMethod("MeasureHeader").Invoke(card, new object[] { graphics });
            var title = (Rectangle)header.GetType().GetField("Title").GetValue(header);
            var badge = (Rectangle)header.GetType().GetField("Badge").GetValue(header);
            Assert(title.Right <= badge.Left && help.Left > card.Left + badge.Right && help.Right <= card.Right,
                "Checkbox, badge and help must remain in one horizontal header.");
            Assert(card.Parent.GetChildAtPoint(new Point(help.Left + help.Width / 2, help.Top + help.Height / 2)) == help,
                "Help button is covered.");
            Assert(TextRenderer.MeasureText(graphics, card.Text, card.Font).Width <= title.Width, "Protection title is clipped.");
            var bounds = (Rectangle)card.GetType().GetProperty("DetailBounds").GetValue(card, null);
            using (var font = new Font(card.Font.FontFamily, 9f))
                Assert(TextRenderer.MeasureText(I18n.Str("mod.warning"), font, new Size(bounds.Width, int.MaxValue), TextFormatFlags.WordBreak).Height <= bounds.Height,
                    "Rapid-click explanation is clipped.");
        }
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
    }
    static void Help(MainForm form, string output)
    {
        bool opened = false, pending = Field<bool?>(form, "resourceModDraft").HasValue;
        using (var close = new System.Windows.Forms.Timer { Interval = 75 })
        {
            close.Tick += delegate {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "BindingHelpDialog");
                if (dialog == null) return;
                close.Stop(); opened = true;
                string text = string.Join("\n", All(dialog).OfType<Label>().Select(l => l.Text));
                Assert(text.Contains(I18n.Str("mod.protectionhelp")) && text.Contains(I18n.Str("mod.note")), "Help omits the simple protection rules.");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(dialog.Location, Point.Empty, dialog.Size);
                    bitmap.Save(Path.Combine(output, "protection-help.png"));
                }
                dialog.Close();
            };
            close.Start(); Field<Button>(form, "btnModHelp").PerformClick(); close.Stop();
        }
        Assert(opened && Field<bool?>(form, "resourceModDraft").HasValue == pending, "Help changed the draft.");
    }
    static void ConfirmDiscard(MainForm form)
    {
        using (var answer = new System.Windows.Forms.Timer { Interval = 50 })
        {
            answer.Tick += delegate {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form);
                if (dialog != null) All(dialog).OfType<Button>().Single(b => b.DialogResult == DialogResult.Yes).PerformClick();
            };
            answer.Start(); Assert((bool)typeof(MainForm).GetMethod("ConfirmDiscard", Hidden).Invoke(form, null), "Discard was not confirmed.");
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            if (Directory.Exists(output)) throw new Exception("Use a new screenshot directory.");
            Directory.CreateDirectory(output);
            bool layoutOnly = args.Contains("--layout-only");
            var mod = new PerfectDeflectNoRapidClickPenaltyMod(root); if (!layoutOnly) mod.Apply(false);
            string save = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
            byte[] original = File.ReadAllBytes(save);
            Exception failure = null;
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                form.ShowInTaskbar = false; form.TopMost = true; form.UiSmoke(save);
                form.Shown += delegate { form.BeginInvoke(new Action(delegate {
                    try
                    {
                        var card = Field<CheckBox>(form, "chkPerfectDeflectNoRapidClickPenalty");
                        var apply = Field<Button>(form, "btnApply"); var undo = Field<Button>(form, "btnUndo");
                        form.RenderSession(); Application.DoEvents();
                        Assert(!card.Checked && card.Enabled && !apply.Enabled, "Protection must default off.");
                        Assert(typeof(MainForm).GetField("btnModSettings", Hidden) == null, "Settings entry remains.");
                        Field<Button>(form, "btnMore").PerformClick(); Application.DoEvents();
                        var menu = Field<ContextMenuStrip>(form, "moreMenu");
                        Assert(menu.Visible && menu.Items["mod.menu"] == null, "More menu retained the removed settings entry.");
                        menu.Close();
                        Shot(form, output, "main-disabled");
                        Size regular = form.Size; form.Size = form.MinimumSize; Application.DoEvents(); Shot(form, output, "main-disabled-minimum");
                        form.Size = regular; Application.DoEvents(); Help(form, output);
                        if (layoutOnly) { form.Close(); return; }
                        Toggle(card);
                        Assert(card.Checked && apply.Enabled && undo.Visible && !form.Session.HasChanges, "Toggle must stage a mod-only draft.");
                        Assert(mod.ReadState() == ResourceModState.Disabled, "Toggle installed resources before Apply.");
                        form.SelectProfile(form.Session.Profiles.Last()); form.RefreshFromDisk();
                        Assert(card.Checked && apply.Enabled, "Scheme switching or refresh discarded a global draft.");
                        undo.PerformClick(); Assert(!card.Checked && !apply.Enabled, "Undo did not clear the draft.");
                        Toggle(card); apply.PerformClick(); Wait(form);
                        Assert(mod.ReadState() == ResourceModState.Enabled && card.Checked && !apply.Enabled, "Apply did not enable protection.");
                        Assert(original.SequenceEqual(File.ReadAllBytes(save)), "Mod-only Apply rewrote the save.");
                        Shot(form, output, "main-enabled");
                        Toggle(card);
                        string payload = Directory.GetFiles(mod.InstallDirectory, "*.ucas").Single();
                        using (var held = new FileStream(payload, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            apply.PerformClick(); Wait(form);
                            Assert(!card.Checked && apply.Enabled, "Locked uninstall lost the pending draft.");
                        }
                        apply.PerformClick(); Wait(form);
                        Assert(mod.ReadState() == ResourceModState.Disabled && !apply.Enabled, "Unlock retry did not complete.");
                        Toggle(card); apply.PerformClick(); Wait(form);
                        File.Delete(Directory.GetFiles(mod.InstallDirectory, "*.ucas").Single()); form.RefreshFromDisk();
                        Assert(card.Checked && apply.Enabled, "Incomplete installation did not stage repair.");
                        apply.PerformClick(); Wait(form); Assert(mod.ReadState() == ResourceModState.Enabled, "Repair failed.");
                        Toggle(card); form.Session.SetKeyboard("IA_Interact", 0, "F9"); form.RenderSession();
                        string foreign = Path.Combine(mod.InstallDirectory, "foreign.txt"); File.WriteAllText(foreign, "preserve");
                        apply.PerformClick(); Wait(form);
                        Assert(!form.Session.HasChanges && form.Session.GetKeyboard("IA_Interact", 0) == "F9", "Bindings were not saved before resource failure.");
                        Assert(Field<string>(form, "messageKey") == "mod.partial" && apply.Enabled, "Partial success lost the mod retry: message=" + Field<string>(form, "messageKey") + "; enabled=" + apply.Enabled + "; draft=" + Field<bool?>(form, "resourceModDraft"));
                        byte[] committed = File.ReadAllBytes(save); DateTime committedTime = File.GetLastWriteTimeUtc(save); File.Delete(foreign); form.RefreshFromDisk();
                        apply.PerformClick(); Wait(form);
                        Assert(mod.ReadState() == ResourceModState.Disabled && committed.SequenceEqual(File.ReadAllBytes(save)) && File.GetLastWriteTimeUtc(save) == committedTime, "Resource retry rewrote the committed save.");
                        Toggle(card); ConfirmDiscard(form);
                        var previous = form.Session;
                        typeof(MainForm).GetMethod("OpenPath", Hidden).Invoke(form, new object[] { save, true });
                        Assert(form.Session != previous && !card.Checked && !apply.Enabled, "Reopening the same path retained a discarded draft.");
                        form.Close();
                    }
                    catch (Exception ex)
                    {
                        failure = ex; typeof(MainForm).GetField("resourceModDraft", Hidden).SetValue(form, null);
                        form.Session.Undo(); form.Close();
                    }
                })); };
                Application.Run(form);
            }
            if (layoutOnly) Assert(original.SequenceEqual(File.ReadAllBytes(save)), "Layout checks changed the save.");
            else File.WriteAllBytes(save, original);
            if (failure != null) throw new Exception("UI assertion failed.", failure);
            Console.WriteLine("MainResourceModTests PASS: " + checks + " assertions; screenshots=" + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
