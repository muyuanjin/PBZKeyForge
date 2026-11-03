using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

public static class BindingDialogInteractionTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct ComboInfo
    { public int Size; public Rect Item, Button; public int State; public IntPtr Combo, Edit, List; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr window, ref ComboInfo info);
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static void Assert(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static IEnumerable<Control> All(Control host)
    { foreach (Control c in host.Controls) { yield return c; foreach (Control child in All(c)) yield return child; } }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Click(Control control, int x, int y)
    {
        IntPtr point = new IntPtr(x | y << 16);
        SendMessage(control.Handle, 0x201, new IntPtr(1), point);
        SendMessage(control.Handle, 0x202, IntPtr.Zero, point);
        Application.DoEvents();
    }
    static void ScreenShot(Form form, string output, string name)
    {
        form.BringToFront(); form.Activate(); Application.DoEvents(); form.Refresh(); DwmFlush();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
    }
    static void DialogCheck(MainForm owner, string output, string language)
    {
        Type type = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingDialog");
        using (var dialog = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { I18n.Str("act.IA_Jump"), false, null, new[] { "SpaceBar" }, true }, null))
        using (var timer = new Timer { Interval = 80 })
        {
            dialog.TopMost = true;
            Exception failure = null;
            timer.Tick += delegate {
                timer.Stop();
                try
                {
                    var combo = Field<ComboBox>(dialog, "keys"); var capture = All(dialog).Single(c => c.GetType().Name == "KeyCaptureBox");
                    Assert(combo.ContainsFocus && !capture.ContainsFocus, "Dialog opened with capture active.");
                    string initial = combo.Text;
                    var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
                    Assert(GetComboBoxInfo(combo.Handle, ref info), "No native editable combo information.");
                    for (int pass = 0; pass < 3; pass++)
                    {
                        int width; using (var graphics = combo.CreateGraphics()) width = (int)(30 * graphics.DpiX / 96f);
                        Click(combo, combo.Width - width / 2, combo.Height / 2);
                        Assert(combo.DroppedDown && !capture.Focused && combo.Text == initial, "Visible dropdown arrow failed to open or recorded a mouse binding.");
                        combo.DroppedDown = false; Application.DoEvents();
                    }
                    ScreenShot(dialog, output, "dialog-idle-" + language);
                    Click(capture, 10, 10);
                    Assert(capture.Focused && combo.Text == initial, "Enabling capture immediately recorded left mouse.");
                    SendMessage(capture.Handle, 0x100, new IntPtr((int)Keys.F9), new IntPtr(1));
                    Assert(combo.Text == "F9", "Armed capture failed to record a key.");
                    Click(capture, 10, 10);
                    Assert(combo.Text == BindingButton.FriendlyKey("LeftMouseButton"), "Second click did not record left mouse.");
                    ScreenShot(dialog, output, "dialog-listening-" + language);
                    Click(combo, combo.Width - 20, combo.Height / 2);
                    Assert(combo.DroppedDown && !capture.Focused, "Opening dropdown failed to stop capture.");
                    combo.DroppedDown = false; combo.Focus();
                    SendMessage(info.Edit, 0xB1, IntPtr.Zero, new IntPtr(-1));
                    SendMessage(info.Edit, 0x102, new IntPtr((int)'G'), new IntPtr(1));
                    Assert(combo.Text == "G" && !capture.Focused, "Typing in the key-name field was intercepted by capture.");
                    Click(capture, 10, 10);
                    Assert(combo.Text == "G", "Returning to capture recorded its activation click.");
                    SendMessage(capture.Handle, 0x100, new IntPtr((int)Keys.Tab), new IntPtr(1));
                    Assert(!capture.Focused && combo.Text == "G", "Tab did not leave capture without binding Tab.");
                    combo.SelectedIndex = Enumerable.Range(0, combo.Items.Count).Single(i =>
                        (string)combo.Items[i].GetType().GetField("Key").GetValue(combo.Items[i]) == "None");
                    ((Button)dialog.AcceptButton).PerformClick();
                    Assert((string)type.GetField("Result").GetValue(dialog) == "None", "Explicit unbind was confused with the empty factory default.");
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start(); dialog.ShowDialog(owner);
            if (failure != null) throw failure;
        }
    }
    static void ButtonStyles(string output)
    {
        using (var form = new Form { ClientSize = new Size(430, 290), BackColor = Color.FromArgb(25, 29, 32), TopMost = true })
        {
            var cases = new[] {
                new BindingButton { UsesDefault = true, DefaultKeys = new[] { "E" } },
                new BindingButton { Value = "G", DefaultKeys = new[] { "E" } },
                new BindingButton { Value = "G", DefaultKeys = new[] { "E" }, Changed = true },
                new BindingButton { UsesDefault = true, DefaultKeys = new[] { "None" } },
                new BindingButton { Value = "None", DefaultKeys = new[] { "E" }, Changed = true }
            };
            for (int i = 0; i < cases.Length; i++)
            {
                cases[i].Dock = DockStyle.None; cases[i].SetBounds(150, 10 + i * 53, 250, 43);
                cases[i].BackColor = form.BackColor; form.Controls.Add(cases[i]);
                form.Controls.Add(new Label { Text = i < 3 ? cases[i].StatusText : i == 3 ? "原厂空绑定" : "明确解绑（待应用）", ForeColor = Color.White,
                    Bounds = new Rectangle(10, 10 + i * 53, 140, 43), TextAlign = ContentAlignment.MiddleLeft });
            }
            form.Show(); Application.DoEvents();
            Assert(cases[0].DisplayText == "E" && cases[0].StatusColor == Color.FromArgb(128, 143, 153), "Factory binding retained the prefix or wrong status.");
            Assert(cases[1].StatusColor == Color.FromArgb(64, 158, 255), "Saved customization did not use the key-view blue.");
            Assert(cases[1].StatusText == "已修改", "Saved customization must use the short modified label.");
            Assert(cases[2].StatusColor == Color.FromArgb(240, 178, 78), "Pending customization did not use the key-view yellow.");
            Assert(cases[3].EmptyBinding && cases[4].EmptyBinding && cases[3].StatusColor != cases[4].StatusColor, "Empty inheritance and pending unbind lost their distinction.");
            using (var bitmap = new Bitmap(cases[3].Width, cases[3].Height))
            {
                cases[3].DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Assert(bitmap.GetPixel(bitmap.Width / 2, 1).ToArgb() == form.BackColor.ToArgb(), "Factory empty binding still draws a prominent button border.");
            }
            ScreenShot(form, output, "binding-states");
        }
    }
    static void RecordsCheck(MainForm owner, string output)
    {
        var session = owner.Session;
        session.SwitchProfile(session.Profiles.First(p => p.EndsWith("Custom1")));
        session.RestoreAction("IA_Guard", false, -1); session.RestoreAction("IA_HeavyAttack", false, -1);
        session.SetKeyboard("IA_Guard", 0, "None"); session.SetKeyboard("IA_HeavyAttack", 1, "None");
        Type type = typeof(MainForm).Assembly.GetType("PBZKeyForge.BindingRecordsDialog");
        using (var dialog = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { session, false, -1, (Action)delegate { owner.RenderSession(); } }, null))
        using (var timer = new Timer { Interval = 100 })
        {
            dialog.TopMost = true; Exception failure = null;
            timer.Tick += delegate {
                timer.Stop();
                try
                {
                    var actions = Field<ListView>(dialog, "actions"); var rows = Field<ListView>(dialog, "rows");
                    foreach (var example in new[] { new[] { "IA_Guard", "RightMouseButton" }, new[] { "IA_HeavyAttack", "F" } })
                    {
                        foreach (ListViewItem item in actions.Items) item.Selected = false;
                        var action = actions.Items.Cast<ListViewItem>().Single(i => (string)i.Tag == example[0]);
                        action.Selected = true; action.EnsureVisible(); Application.DoEvents();
                        Assert(action.SubItems[1].Text == BindingButton.FriendlyKey(example[1]), "Action summary mistook one unbound entry for an unbound action.");
                        Assert(rows.Items.Cast<ListViewItem>().Any(i => i.SubItems[0].Text == I18n.Str("editor.unbound")), "Entry table lost the explicit unbound record.");
                        Assert(rows.Items.Cast<ListViewItem>().Any(i => i.SubItems[0].Text == BindingButton.FriendlyKey(example[1])), "Entry table omitted the inherited binding that keeps the action available.");
                        if (example[0] == "IA_Guard") Assert(rows.Items.Count == 2, "Details duplicated a registered guard input when adding inherited entries.");
                        Assert(rows.Columns[0].Text == I18n.Str("views.recordkey") && actions.Columns[1].Text == I18n.Str("views.effectivekeys"), "Details lack plain-language column headings.");
                        ScreenShot(dialog, output, "details-" + example[0] + "-" + I18n.Current);
                    }
                    var help = All(dialog).OfType<Button>().Single(c => c.GetType().Name == "BindingHelpButton");
                    typeof(Control).GetMethod("OnMouseEnter", Hidden).Invoke(help, new object[] { EventArgs.Empty });
                    Assert(Field<ToolTip>(help, "tip").GetToolTip(help) == I18n.Str("help.slots"), "Question button has no localized hover explanation.");
                    using (var dismiss = new Timer { Interval = 100 })
                    {
                        Exception tutorialFailure = null;
                        dismiss.Tick += delegate {
                            dismiss.Stop();
                            var tutorial = dialog.OwnedForms.SingleOrDefault();
                            try
                            {
                                Assert(tutorial != null && All(tutorial).OfType<Label>().Any(l => l.Text.Contains(I18n.Str("help.example"))), "Question button did not open the example tutorial.");
                                var tutorialText = All(tutorial).OfType<Label>().Single(l => l.Text.Contains(I18n.Str("help.example")));
                                Assert(tutorialText.Right <= tutorialText.Parent.ClientSize.Width && !((ScrollableControl)tutorialText.Parent).HorizontalScroll.Visible,
                                    "Tutorial text does not wrap inside its viewport.");
                                foreach (var button in All(tutorial).OfType<Button>().Where(c => c.Visible))
                                    Assert(tutorial.RectangleToScreen(tutorial.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)), "Tutorial action is clipped.");
                                ScreenShot(tutorial, output, "tutorial-" + I18n.Current);
                                tutorial.Size = tutorial.MinimumSize; Application.DoEvents();
                                Assert(tutorialText.Right <= tutorialText.Parent.ClientSize.Width && !((ScrollableControl)tutorialText.Parent).HorizontalScroll.Visible,
                                    "Resizing the tutorial causes horizontal clipping.");
                            }
                            catch (Exception ex) { tutorialFailure = ex; }
                            finally { if (tutorial != null) tutorial.Close(); }
                        };
                        dismiss.Start(); help.PerformClick(); if (tutorialFailure != null) throw tutorialFailure;
                    }
                    dialog.Close();
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start(); dialog.ShowDialog(owner); if (failure != null) throw failure;
        }
        session.Undo(); owner.RenderSession();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            var languages = args.Contains("--all-languages") ? I18n.Languages : I18n.Languages.Take(1).ToArray();
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] original = File.ReadAllBytes(args[0]); string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, original);
            I18n.Current = "zh-Hans"; ButtonStyles(output);
            using (var form = new MainForm { TopMost = true })
            {
                form.UiSmoke(input); form.Show(); form.ShowActionView(true); form.Size = form.MinimumSize; Application.DoEvents();
                foreach (string language in languages)
                {
                    Field<ComboBox>(form, "cbLang").SelectedIndex = Array.IndexOf(I18n.Languages, language);
                    DialogCheck(form, output, language);
                    if (language == "zh-Hans" || language == "ru") RecordsCheck(form, output);
                    ScreenShot(form, output, "actions-" + language);
                }
                Assert(!form.Session.HasAnyDrafts, "Dialog checks changed the editor draft.");
            }
            Assert(original.SequenceEqual(File.ReadAllBytes(input)) && original.SequenceEqual(File.ReadAllBytes(args[0])), "Interaction checks wrote an input save.");
            Console.WriteLine("BindingDialogInteractionTests PASS: " + checks + " checks; " + languages.Length + " languages; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
