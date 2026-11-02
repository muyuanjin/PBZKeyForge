using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

// Runs only on an installed-layout COPY of the saves. Usage: <copied input.sav> <artifact directory>.
public static class EditorUiTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    [DllImport("shcore.dll")]
    static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
    static void ColumnsAlign(MainForm form, string hostName)
    {
        var tables = Descendants(Field<Panel>(form, hostName)).OfType<TableLayoutPanel>().Where(t => t.ColumnCount == 4).ToArray();
        var header = tables.First(t => t.Controls.Count == 4);
        var body = tables.First(t => t.Controls.Count > 4);
        Assert(header.GetColumnWidths().SequenceEqual(body.GetColumnWidths()), "Fixed header and binding columns diverged at native DPI.");
        for (int column = 1; column < 4; column++)
        {
            var label = (Label)header.GetControlFromPosition(column, 0);
            var button = body.GetControlFromPosition(column, 0);
            Assert(label.TextAlign == ContentAlignment.MiddleCenter && label.Padding.Left == label.Padding.Right,
                "Binding column heading must be horizontally centered without asymmetric padding.");
            Rectangle headingBounds = BoundsInForm(form, label), buttonBounds = BoundsInForm(form, button);
            Assert(Math.Abs((headingBounds.Left + headingBounds.Right) - (buttonBounds.Left + buttonBounds.Right)) <= 2,
                "Binding heading is not centered over the binding buttons.");
        }
    }
    static void Shot(Form form, string directory, string name)
    {
        form.Refresh(); Application.DoEvents();
        using (var image = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(directory, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    static void VisibleInsideForm(Form form, Control control)
    {
        Assert(control.Visible, control.Name + " unexpectedly hidden.");
        Rectangle rect = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        Assert(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= form.ClientSize.Width && rect.Bottom <= form.ClientSize.Height,
            control.GetType().Name + " is outside the client area: " + rect);
    }
    static Rectangle BoundsInForm(Form form, Control control)
    { return form.RectangleToClient(control.Parent.RectangleToScreen(control.Bounds)); }
    static void WindowChrome(MainForm form, string output)
    {
        Assert(form.FormBorderStyle == FormBorderStyle.None, "Main window still uses the default Windows frame.");
        var minimize = (Button)form.GetType().GetField("captionMinimize").GetValue(form);
        var maximize = (Button)form.GetType().GetField("captionMaximize").GetValue(form);
        var close = (Button)form.GetType().GetField("captionClose").GetValue(form);
        Rectangle min = BoundsInForm(form, minimize), max = BoundsInForm(form, maximize), end = BoundsInForm(form, close);
        Assert(min.Size == max.Size && max.Size == end.Size && min.Top == max.Top && max.Top == end.Top,
            "Window control buttons differ in size or vertical alignment.");
        Assert(min.Right == max.Left && max.Right == end.Left, "Window control buttons must form a continuous row.");
        VisibleInsideForm(form, minimize); VisibleInsideForm(form, maximize); VisibleInsideForm(form, close);
        Size restored = form.Size;
        maximize.PerformClick(); Application.DoEvents();
        Assert(form.WindowState == FormWindowState.Maximized, "Maximize button did not maximize the window.");
        Assert(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "Maximized window covers the taskbar or escapes its working area.");
        ActionsAlign(form, false); ColumnsAlign(form, "kbHost");
        Shot(form, output, "maximized-zh-Hans");
        maximize.PerformClick(); Application.DoEvents();
        Assert(form.WindowState == FormWindowState.Normal && form.Size == restored, "Restore button lost the previous window size.");
        minimize.PerformClick(); Application.DoEvents();
        Assert(form.WindowState == FormWindowState.Minimized, "Minimize button did not minimize the window.");
        form.WindowState = FormWindowState.Normal; form.Activate(); Application.DoEvents();
        Assert(form.Size == restored, "Minimize and restore lost the previous window size.");
        Point corner = form.PointToScreen(new Point(1, 1));
        long point = (ushort)corner.X | ((long)(ushort)corner.Y << 16);
        Assert(SendMessage(form.Handle, 0x84, IntPtr.Zero, new IntPtr(point)).ToInt32() == 13,
            "Custom frame does not expose its top-left resize handle.");
    }
    static void ActionsAlign(MainForm form, bool dirty)
    {
        var language = BoundsInForm(form, Field<Control>(form, "cbLang"));
        foreach (string name in new[] { "btnRefresh", "btnMore" })
        {
            var button = BoundsInForm(form, Field<Control>(form, name));
            Assert(Math.Abs(language.Top - button.Top) <= 1 && Math.Abs(language.Height - button.Height) <= 1,
                "Toolbar controls do not share a common top edge and height: " + name);
        }
        if (dirty)
        {
            var undo = BoundsInForm(form, Field<Control>(form, "btnUndo"));
            var apply = BoundsInForm(form, Field<Control>(form, "btnApply"));
            Assert(undo.Top == apply.Top && undo.Height == apply.Height && undo.Width == apply.Width,
                "Undo and Apply must use equal dimensions and alignment.");
        }
        var backup = Field<Control>(form, "lblBackup");
        Assert(backup.Top >= 0 && backup.Bottom <= backup.Parent.ClientSize.Height && backup.Height >= backup.Font.Height,
            "Backup caption is clipped in the footer.");
    }
    static void DialogButtonsVisible(Form owner)
    {
        Exception failure = null;
        using (var timer = new Timer { Interval = 120 })
        {
            timer.Tick += delegate
            {
                timer.Stop();
                try
                {
                    Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(candidate => candidate != owner);
                    Assert(dialog != null, "Custom dialog did not open.");
                    Button[] buttons = Descendants(dialog).OfType<Button>().Where(button => button.DialogResult != DialogResult.None).ToArray();
                    Assert(buttons.Length == 3, "Confirmation dialog has " + buttons.Length + " action buttons; expected three.");
                    foreach (Button button in buttons) VisibleInsideForm(dialog, button);
                    buttons.First(button => button.DialogResult == DialogResult.Cancel).PerformClick();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(candidate => candidate != owner);
                    if (dialog != null) dialog.Close();
                }
            };
            timer.Start();
            Type type = typeof(MainForm).Assembly.GetType("PBZKeyForge.EditorDialogs");
            MethodInfo show = type.GetMethod("Show", BindingFlags.Static | BindingFlags.Public);
            string text = String.Join(" ", Enumerable.Repeat("A long confirmation message that may scroll on a scaled display.", 14));
            object result = show.Invoke(null, new object[] { owner, text, "Confirmation", MessageBoxButtons.YesNoCancel });
            Assert((DialogResult)result == DialogResult.Cancel, "Dialog did not return the selected cancel action.");
        }
        if (failure != null) throw failure;
    }
    static void OtherProfilesUnchanged(byte[] beforeBytes, byte[] afterBytes, string editedProfile)
    {
        var before = SaveFile.FromBytes(beforeBytes);
        var after = SaveFile.FromBytes(afterBytes);
        Assert(before.Records.Count == after.Records.Count, "Editing one scheme changed the profile inventory.");
        for (int i = 0; i < before.Records.Count; i++)
            if (before.Records[i].ProfileId == editedProfile) after.Records[i] = before.Records[i];
        var beforeFamilies = before.Props.First(n => n.Name == "GamepadFamilyProfiles");
        var afterFamilies = after.Props.First(n => n.Name == "GamepadFamilyProfiles");
        Assert(beforeFamilies.Items.Count == afterFamilies.Items.Count, "Editing one scheme changed the controller profile inventory.");
        for (int i = 0; i < beforeFamilies.Items.Count; i++)
            if (SaveFile.Field(beforeFamilies.Items[i], "ProfileId") == editedProfile)
                afterFamilies.Items[i] = beforeFamilies.Items[i];
        Assert(before.ToBytes().SequenceEqual(after.ToBytes()),
            "Applying the selected scheme changed another scheme or unrelated save data.");
    }
    static void EditKeyboard(MainForm form, string action, int slot, string key, string output, bool gamepad = false)
    {
        var buttons = Field<IDictionary>(form, gamepad ? "padButtons" : "kbButtons");
        var button = (Button)((Array)buttons[action]).GetValue(slot);
        Exception failure = null;
        using (var timer = new Timer { Interval = 100 })
        {
            timer.Tick += delegate
            {
                timer.Stop();
                Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "BindingDialog");
                try
                {
                    if (dialog == null) throw new Exception("Binding cell did not open the editor.");
                    dialog.ShowInTaskbar = false; dialog.Opacity = 0;
                    Assert(dialog.FormBorderStyle == FormBorderStyle.None, "Binding dialog did not share the custom window frame.");
                    var choices = Field<ComboBox>(dialog, "keys");
                    var capture = Descendants(dialog).OfType<TextBox>().First(c => c.GetType().Name == "KeyCaptureBox");
                    string[] modifiers = { "LeftShift", "RightShift", "LeftControl", "RightControl", "LeftAlt", "RightAlt" };
                    int[] virtualKeys = { 0x10, 0x10, 0x11, 0x11, 0x12, 0x12 }, scans = { 0x2a, 0x36, 0x1d, 0x1d, 0x38, 0x38 };
                    for (int m = 0; !gamepad && m < modifiers.Length; m++)
                    {
                        long data = 1L | ((long)scans[m] << 16) | (m == 3 || m == 5 ? 1L << 24 : 0);
                        SendMessage(capture.Handle, m >= 4 ? 0x104 : 0x100, new IntPtr(virtualKeys[m]), new IntPtr(data));
                        Assert((string)choices.SelectedItem.GetType().GetField("Key").GetValue(choices.SelectedItem) == modifiers[m], "Modifier capture changed sides: " + modifiers[m]);
                    }
                    int index = -1;
                    for (int i = 0; i < choices.Items.Count; i++)
                        if ((string)choices.Items[i].GetType().GetField("Key").GetValue(choices.Items[i]) == key) index = i;
                    if (index < 0) throw new Exception("Key missing from the binding editor: " + key);
                    choices.SelectedIndex = index;
                    Shot(dialog, output, gamepad ? "gamepad-dialog-zh-Hans" : "binding-dialog-zh-Hans");
                    ((Button)dialog.AcceptButton).PerformClick();
                }
                catch (Exception ex) { failure = ex; if (dialog != null) dialog.Close(); }
            };
            timer.Start(); button.PerformClick();
        }
        if (failure != null) throw failure;
        Assert((gamepad ? form.Session.GetGamepad(action, slot) : form.Session.GetKeyboard(action, slot)) == key,
            "Binding dialog did not update the selected scheme's draft.");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            int awareness;
            Assert(GetProcessDpiAwareness(IntPtr.Zero, out awareness) == 0 && awareness == 1, "UI host must use the application's System DPI manifest.");
            string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            byte[] initialInput = File.ReadAllBytes(input);
            string shared = Path.Combine(Path.GetDirectoryName(input), "PBZSharedSetting.sav");
            byte[] initialShared = File.ReadAllBytes(shared);
            using (var form = new MainForm())
            {
                // DrawToBitmap captures controls without flashing every test state on the user's desktop.
                form.ShowInTaskbar = false; form.Opacity = 0;
                form.UiSmoke(input); form.Show(); Application.DoEvents();
                Assert(!Field<Label>(form, "lblPath").Text.Contains(Path.GetPathRoot(input)),
                    "The save-location caption must not expose the user's absolute path.");
                if (args.Length > 2 && args[2] == "--dialog-only")
                {
                    DialogButtonsVisible(form);
                    Console.WriteLine("PASS: dialog actions are visible and clickable at native DPI.");
                    return 0;
                }
                if (args.Length > 2 && args[2] == "--capture")
                {
                    PropertyInfo active = typeof(EditorSession).GetProperty("ActiveProfile");
                    active.GetSetMethod(true).Invoke(form.Session, new object[] { form.Session.SelectedProfile });
                    typeof(EditorSession).GetProperty("SchemeError").GetSetMethod(true).Invoke(form.Session, new object[] { null });
                    form.RenderSession(); Application.DoEvents();
                    Shot(form, output, "screenshot");
                    return 0;
                }
                float dpiScale;
                using (var graphics = form.CreateGraphics()) { dpiScale = graphics.DpiX / 96f; Console.WriteLine("Native UI DPI: " + graphics.DpiX); }
                Size regularSize = new Size((int)(1280 * dpiScale), (int)(800 * dpiScale));
                var apply = Field<Button>(form, "btnApply");
                var language = Field<ComboBox>(form, "cbLang");
                var presets = Field<ComboBox>(form, "cbPreset");
                Assert(presets.Items.Count > 0 && presets.Items.Cast<object>().Skip(1).All(item => item is Preset),
                    "Quick setup list contains a status placeholder instead of an action.");
                Assert(!presets.Items.OfType<Preset>().Any(preset => preset.Id == "undo_onekey"),
                    "Ambiguous partial-restore action must not appear in quick setup.");
                DialogButtonsVisible(form);
                Assert(!apply.Enabled, "Clean editor must not allow an empty apply.");
                language.SelectedIndex = 0; form.Size = regularSize; Application.DoEvents();
                WindowChrome(form, output);
                for (int i = 0; i < I18n.Languages.Length; i++)
                {
                    language.SelectedIndex = i; form.Size = regularSize; Application.DoEvents();
                    VisibleInsideForm(form, language); VisibleInsideForm(form, presets); VisibleInsideForm(form, apply);
                    ColumnsAlign(form, "kbHost");
                    ActionsAlign(form, false);
                    Shot(form, output, "clean-" + I18n.Languages[i]);
                    form.Size = form.MinimumSize; Application.DoEvents();
                    VisibleInsideForm(form, language); VisibleInsideForm(form, presets); VisibleInsideForm(form, apply);
                    ColumnsAlign(form, "kbHost");
                    ActionsAlign(form, false);
                    Shot(form, output, "minimum-" + I18n.Languages[i]);
                }
                language.SelectedIndex = 0; form.Size = regularSize;
                string editing = form.Session.Profiles.First(p => p.EndsWith("Custom1"));
                string other = form.Session.Profiles.First(p => p != editing);
                form.SelectProfile(other);
                string otherPadJump = form.Session.GetGamepad("IA_Jump", 0);
                form.SelectProfile(editing);
                string[] originalDodge = Enumerable.Range(0, 3).Select(i => form.Session.GetKeyboard("IA_Dodge", i)).ToArray();
                presets.SelectedItem = presets.Items.OfType<Preset>().First(p => p.Id == "onekey_dodge");
                Assert(form.Session.PresetId == "onekey_dodge" && apply.Enabled, "Selecting quick configuration did not create a draft.");
                presets.SelectedItem = presets.Items.OfType<Preset>().First(p => p.Id == "onekey");
                Assert(Enumerable.Range(0, 3).All(i => form.Session.GetKeyboard("IA_Dodge", i) == originalDodge[i]), "Quick configurations accumulated an earlier dodge preview.");
                EditKeyboard(form, "IA_Jump", 0, "F8", output);
                form.ShowGamepad(true);
                EditKeyboard(form, "IA_Jump", 0, "Gamepad_Special_Right", output, true);
                form.SelectProfile(other);
                Assert(form.Session.GetGamepad("IA_Jump", 0) == otherPadJump, "A controller draft leaked into another scheme.");
                form.Session.SetGamepad("IA_Jump", 0, otherPadJump == "Gamepad_RightTrigger" ? "Gamepad_LeftTrigger" : "Gamepad_RightTrigger"); form.RenderSession();
                form.SelectProfile(editing);
                Assert(form.Session.GetGamepad("IA_Jump", 0) == "Gamepad_Special_Right", "Scheme navigation lost the selected controller draft.");
                form.ShowGamepad(false);
                Assert(form.Session.PresetId == null, "Manual edit did not finish the configuration preview.");
                ActionsAlign(form, true);
                Assert(initialInput.SequenceEqual(File.ReadAllBytes(input)) && initialShared.SequenceEqual(File.ReadAllBytes(shared)), "Drafting or language selection wrote to disk.");

                // A real external disk change must appear after switching away and back.
                var disk = SaveFile.Load(input);
                var record = disk.RecordFor(editing);
                record.Entries.RemoveAll(e => e.Name == "IA_Interact" && e.Slot == 0);
                record.Entries.Add(new BlobEntry { Name = "IA_Interact", Key = "F9", X = "None", Y = "None", Slot = 0 });
                disk.Save(input);
                form.SelectProfile(other); form.SelectProfile(editing);
                Assert(form.Session.GetKeyboard("IA_Jump", 0) == "F8", "Scheme switching lost a local draft.");
                Assert(form.Session.GetKeyboard("IA_Interact", 0) == "F9", "Scheme switching did not read the latest disk binding.");
                Shot(form, output, "draft-zh-Hans");
                var activate = Field<CheckBox>(form, "chkActivate");
                activate.Checked = true;
                Assert(form.Session.ActivationPending, "Activation checkbox did not stage activation.");
                Shot(form, output, "draft-activate-zh-Hans");
                activate.Checked = false;
                Assert(!form.Session.ActivationPending, "Unchecking activation left a hidden intention.");
                form.ShowGamepad(true); ColumnsAlign(form, "padHost"); Shot(form, output, "gamepad-zh-Hans");
                form.ShowGamepad(false);
                Assert(apply.Enabled, "Draft apply unexpectedly disabled.");
                apply.PerformClick(); Application.DoEvents();
                Assert(SaveFile.Load(input).RecordFor(editing).Entries.Any(e => e.Name == "IA_Jump" && e.Slot == 0 && e.Key == "F8"), "Apply button did not save the binding draft.");
                Assert(SaveFile.Load(input).RecordFor(editing).Entries.Any(e => e.Name == "IA_Interact" && e.Slot == 0 && e.Key == "F9"), "Apply lost the unrelated external binding.");
                Assert(initialShared.SequenceEqual(File.ReadAllBytes(shared)), "Editing an inactive scheme unexpectedly activated it.");
                OtherProfilesUnchanged(initialInput, File.ReadAllBytes(input), editing);
                var savedPadGroups = SaveFile.Load(input).GamepadKeysFor(editing);
                Assert(savedPadGroups.Count > 0 && savedPadGroups.All(keys => keys.Items.Any(item => SaveFile.Field(item, "MappingName") == "IA_Jump"
                    && SaveFile.Field(item, "Slot") == "EPlayerMappableKeySlot::Second" && SaveFile.Field(item, "Key") == "Gamepad_Special_Right")),
                    "Apply did not save the selected scheme's controller edit.");
                Assert(form.Session.HasGamepadDraft(other), "Applying one scheme discarded another scheme's controller draft.");
                Assert(!apply.Enabled && !form.Session.HasChanges, "Successful apply left stale dirty UI.");
                form.Session.ResetAll(); form.RenderSession();
                Assert(form.Session.IsResetPending && !presets.Enabled, "Global reset did not lock further quick edits.");
                Shot(form, output, "reset-zh-Hans");
                form.Session.Undo(); form.RenderSession();
                Assert(!form.Session.IsResetPending && presets.Enabled, "Undo reset did not restore the editor.");
                form.Session.SetKeyboard("IA_Jump", 0, "F10"); form.RenderSession();
                for (int i = 0; i < I18n.Languages.Length; i++)
                {
                    language.SelectedIndex = i; form.Size = form.MinimumSize; Application.DoEvents();
                    VisibleInsideForm(form, apply); VisibleInsideForm(form, presets);
                    ActionsAlign(form, true);
                    Shot(form, output, "minimum-draft-" + I18n.Languages[i]);
                }
                language.SelectedIndex = 0; form.Size = regularSize;
                string fakeGame = Path.Combine(output, "PBZ.exe");
                File.Copy(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "SchemeActivationTests.exe"), fakeGame, true);
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fakeGame, "--hold")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                {
                    try
                    {
                        Assert(process.StandardOutput.ReadLine() == "ready", "Game-process fixture did not start.");
                        form.RenderSession();
                        Assert(!apply.Enabled && form.Session.HasChanges, "Running game must block applying while preserving drafts.");
                        Shot(form, output, "running-zh-Hans");
                    }
                    finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
                }
                form.RenderSession();
                Assert(apply.Enabled, "Exited game left Apply disabled.");
                disk = SaveFile.Load(input); record = disk.RecordFor(editing);
                record.Entries.First(e => e.Name == "IA_Jump" && e.Slot == 0).Key = "F11"; disk.Save(input);
                form.RefreshFromDisk();
                Assert(form.Session.Conflicts.Length == 1 && !apply.Enabled, "Refresh must show a same-binding conflict and block Apply.");
                Shot(form, output, "conflict-zh-Hans");
                form.Session.Undo(); form.RenderSession();
                byte[] validShared = File.ReadAllBytes(shared);
                try
                {
                    byte[] invalidShared = (byte[])validShared.Clone(); invalidShared[invalidShared.Length - 1] ^= 1;
                    File.WriteAllBytes(shared, invalidShared); form.RefreshFromDisk();
                    var status = Field<Label>(form, "lblStatus");
                    Assert(form.Session.ActiveProfile == null && status.Text.Contains(I18n.Str("editor.schemeunknown")), "Ordinary refresh hid the persistent unknown-scheme explanation.");
                    Assert(!activate.Enabled, "Unknown selector still allowed activation.");
                    Shot(form, output, "unknown-scheme-zh-Hans");
                }
                finally { File.WriteAllBytes(shared, validShared); form.RefreshFromDisk(); }
            }
            Console.WriteLine("PASS: " + checks + " UI assertions; artifacts: " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
