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
    static bool captureImages = true;
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
        var tables = Descendants(Field<Panel>(form, hostName)).OfType<TableLayoutPanel>().Where(t => t.ColumnCount == 5).ToArray();
        var header = tables.First(t => t.Controls.Count == 5);
        var body = tables.First(t => t.Controls.Count > 5);
        Assert(header.GetColumnWidths().SequenceEqual(body.GetColumnWidths()), "Fixed header and binding columns diverged at native DPI.");
        for (int column = 2; column < 5; column++)
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
        if (!captureImages) return;
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
    static void ClickButton(MainForm form, string name)
    {
        var button = Field<Button>(form, name);
        Assert(button.Visible && button.Enabled, "Button cannot be clicked: " + name);
        button.PerformClick(); Application.DoEvents();
        if (name == "btnApply")
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (form.IsApplying && elapsed.Elapsed.TotalSeconds < 15) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
            Assert(!form.IsApplying, "Apply did not complete.");
        }
    }
    static void ClickProfile(MainForm form, string profile)
    {
        var tab = Field<EditorTab[]>(form, "schemeTabs").Single(button => (string)button.Tag == profile);
        Assert(tab.Visible && tab.Enabled, "Scheme tab cannot be clicked: " + profile);
        tab.PerformClick(); Application.DoEvents();
        Assert(form.Session.SelectedProfile == profile && tab.Selected, "Scheme tab did not select its own profile: " + profile);
    }
    static void ClickDevice(MainForm form, bool gamepad)
    {
        ClickButton(form, gamepad ? "tabPad" : "tabKb");
        Assert(Field<Panel>(form, "padHost").Visible == gamepad && Field<Panel>(form, "kbHost").Visible != gamepad,
            "Device tab did not show exactly its binding table.");
    }
    static void RespondDialog(MainForm owner, Action trigger, Action<Form> respond, string expectedType)
    {
        Exception failure = null; bool answered = false;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using (var timer = new Timer { Interval = 30 })
        {
            timer.Tick += delegate
            {
                if (elapsed.Elapsed.TotalSeconds > 15)
                {
                    // An unexpected native modal window cannot be closed through
                    // OpenForms. End this isolated test process with a finite failure.
                    Console.Error.WriteLine("FAIL: modal response timed out after 15 seconds: " + (expectedType ?? "conflict confirmation"));
                    Environment.Exit(1);
                }
                Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(candidate => candidate != owner);
                if (dialog == null) return;
                timer.Stop();
                try
                {
                    Assert(dialog.Modal && owner.OwnedForms.Contains(dialog), "Action did not open an owned modal dialog.");
                    if (expectedType != null) Assert(dialog.GetType().Name == expectedType, "Unexpected modal dialog: " + dialog.GetType().Name);
                    dialog.ShowInTaskbar = false; dialog.Opacity = 0;
                    respond(dialog); answered = true;
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start(); trigger();
        }
        if (failure != null) throw failure;
        Assert(answered, "Control event did not open its expected dialog.");
        Assert(!Application.OpenForms.Cast<Form>().Any(candidate => candidate != owner), "Modal dialog remained open after responding.");
        Application.DoEvents();
    }
    static Button BindingCell(MainForm form, bool gamepad, string action, int slot)
    {
        var buttons = Field<IDictionary>(form, gamepad ? "padButtons" : "kbButtons");
        return (Button)((Array)buttons[action]).GetValue(slot);
    }
    static void ClickBindingCell(Button button)
    {
        var scroll = button.Parent;
        while (scroll != null && (!(scroll is ScrollableControl) || !((ScrollableControl)scroll).AutoScroll)) scroll = scroll.Parent;
        Assert(scroll != null, "Binding table has no scrollable viewport.");
        ((ScrollableControl)scroll).ScrollControlIntoView(button); Application.DoEvents();
        Assert(button.Visible && button.Enabled, "Binding cell is not available for clicking.");
        Point center = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
        Assert(scroll.RectangleToScreen(scroll.ClientRectangle).Contains(center), "Scrolled binding cell is outside its viewport.");
        button.PerformClick();
    }
    static void BindingAction(MainForm form, bool gamepad, string action, int slot, string key, bool clear)
    {
        ClickDevice(form, gamepad);
        RespondDialog(form, delegate { ClickBindingCell(BindingCell(form, gamepad, action, slot)); }, delegate(Form dialog)
        {
            if (clear)
                Descendants(dialog).OfType<Button>().Single(button => button.Text == I18n.Str("editor.clear")).PerformClick();
            else
            {
                var choices = Field<ComboBox>(dialog, "keys");
                choices.SelectedIndex = Enumerable.Range(0, choices.Items.Count).Single(index =>
                    (string)choices.Items[index].GetType().GetField("Key").GetValue(choices.Items[index]) == key);
                ((Button)dialog.AcceptButton).PerformClick();
            }
        }, "BindingDialog");
    }
    static void AllBindingCellsCancel(MainForm form, string input, string shared)
    {
        byte[] inputBefore = File.ReadAllBytes(input), sharedBefore = File.ReadAllBytes(shared);
        foreach (bool gamepad in new[] { false, true })
        {
            ClickDevice(form, gamepad);
            var buttons = Field<IDictionary>(form, gamepad ? "padButtons" : "kbButtons");
            int visited = 0, unavailable = 0;
            foreach (DictionaryEntry entry in buttons)
                for (int slot = 0; slot < ((Array)entry.Value).Length; slot++)
                {
                    string action = (string)entry.Key; int binding = slot;
                    var cell = (BindingButton)BindingCell(form, gamepad, action, binding);
                    if (!cell.Enabled)
                    {
                        Assert(cell.Unavailable && cell.DisplayText == "—", "Unavailable slots must show a disabled placeholder.");
                        unavailable++; continue;
                    }
                    string before = gamepad ? form.Session.GetGamepad(action, slot) : form.Session.GetKeyboard(action, slot);
                    RespondDialog(form, delegate { ClickBindingCell(BindingCell(form, gamepad, action, binding)); }, delegate(Form dialog)
                    {
                        Assert(Field<ComboBox>(dialog, "keys").Items.Count > 0, "Binding dialog has no key choices.");
                        Assert(dialog.Text.Contains(I18n.Str("act." + action)), "Binding cell opened another action's dialog: " + action);
                        Assert(Field<ComboBox>(dialog, "keys").Text == (cell.UsesDefault ? BindingButton.DefaultLabel(cell.DefaultKeys) : cell.DisplayText), "Binding dialog did not show the clicked slot's value.");
                        ((Button)dialog.CancelButton).PerformClick();
                    }, "BindingDialog");
                    Assert((gamepad ? form.Session.GetGamepad(action, slot) : form.Session.GetKeyboard(action, slot)) == before,
                        "Cancel changed binding: " + action + "/" + slot);
                    Assert(!form.Session.HasAnyDrafts, "Cancel created a draft: " + action + "/" + slot);
                    visited++;
                }
            Assert(visited + unavailable == buttons.Count * 3 && visited > 0, "Binding cell coverage missed a row or slot.");
            Console.WriteLine("Opened and cancelled " + visited + (gamepad ? " gamepad" : " keyboard") + " binding cells.");
        }
        Assert(inputBefore.SequenceEqual(File.ReadAllBytes(input)) && sharedBefore.SequenceEqual(File.ReadAllBytes(shared)), "Opening/cancelling binding cells wrote save files.");
        ClickDevice(form, false);
    }
    static void ClearBindingOverrides(MainForm form, string input, string shared)
    {
        byte[] inputBefore = File.ReadAllBytes(input), sharedBefore = File.ReadAllBytes(shared);
        string originalKeyboard = form.Session.GetKeyboard("IA_Jump", 0), originalPad = form.Session.GetGamepad("IA_Jump", 0);
        BindingAction(form, false, "IA_Jump", 0, "F7", false);
        Assert(form.Session.GetKeyboard("IA_Jump", 0) == "F7", "Keyboard editor did not stage override before clear.");
        BindingAction(form, false, "IA_Jump", 0, null, true);
        Assert(form.Session.GetKeyboard("IA_Jump", 0) == null, "Clear override did not remove keyboard override.");
        if (form.Session.HasChanges) ClickButton(form, "btnUndo");
        Assert(form.Session.GetKeyboard("IA_Jump", 0) == originalKeyboard && !form.Session.HasAnyDrafts, "Undo button did not restore original keyboard state.");
        string padKey = form.Session.GetGamepadDefault("IA_Jump", 0) == "Gamepad_RightTrigger" ? "Gamepad_LeftTrigger" : "Gamepad_RightTrigger";
        BindingAction(form, true, "IA_Jump", 0, padKey, false);
        Assert(form.Session.GetGamepad("IA_Jump", 0) == padKey, "Controller editor did not stage override before clear.");
        BindingAction(form, true, "IA_Jump", 0, null, true);
        Assert(form.Session.GetGamepad("IA_Jump", 0) == form.Session.GetGamepadDefault("IA_Jump", 0), "Controller clear did not restore game default.");
        if (form.Session.HasChanges) ClickButton(form, "btnUndo");
        Assert(form.Session.GetGamepad("IA_Jump", 0) == originalPad && !form.Session.HasAnyDrafts, "Undo button did not restore original controller state.");
        Assert(inputBefore.SequenceEqual(File.ReadAllBytes(input)) && sharedBefore.SequenceEqual(File.ReadAllBytes(shared)), "Clearing draft overrides wrote save files.");
        ClickDevice(form, false);
    }
    static void UnboundBindings(MainForm form, string input, string shared, string output, int languageCount)
    {
        byte[] before = File.ReadAllBytes(input), sharedBefore = File.ReadAllBytes(shared);
        var language = Field<ComboBox>(form, "cbLang");
        language.SelectedIndex = 0;
        Assert(((BindingButton)BindingCell(form, false, "IA_HeavyAttack", 0)).DisplayText == "F", "Inherited keyboard slots must display their resource default key without a repeated prefix.");
        Assert(KeyboardDefaults.ForAction("IA_OpenMapMenu").SequenceEqual(new[] { "M", "Tab" }), "Map action defaults must retain both registered resource keys.");
        Assert(KeyboardDefaults.ForAction("IA_ShadowArm_LongPress").SequenceEqual(new[] { "LeftControl" }), "Holding Phantom Edges must retain the action-level Ctrl default.");
        Assert(BindingButton.DefaultLabel(new[] { "None" }) == "默认 | 无绑定", "Explicitly empty defaults must show unbound.");
        Assert(((BindingButton)BindingCell(form, false, "IA_Guard", 2)).EmptyBinding, "Empty factory slots must use a quiet placeholder.");
        foreach (bool gamepad in new[] { false, true })
        {
            var defaultButton = (BindingButton)BindingCell(form, gamepad, "IA_HeavyAttack", 0);
            ClickDevice(form, gamepad);
            RespondDialog(form, delegate { ClickBindingCell(defaultButton); }, delegate(Form dialog)
            {
                Assert(Field<ComboBox>(dialog, "keys").Text == BindingButton.DefaultLabel(defaultButton.DefaultKeys), "Default dialog choice must identify the same concrete key as its table cell.");
                ((Button)dialog.CancelButton).PerformClick();
            }, "BindingDialog");
        }
        ClickDevice(form, false);
        Shot(form, output, "keyboard-defaults");
        ClickDevice(form, true); Shot(form, output, "gamepad-defaults");
        for (int index = 0; index < languageCount; index++)
        {
            language.SelectedIndex = index;
            foreach (bool gamepad in new[] { false, true })
            {
                BindingAction(form, gamepad, "IA_Guard", 0, "None", false);
                var button = (BindingButton)BindingCell(form, gamepad, "IA_Guard", 0);
                Assert(button.Value == "None" && button.DisplayText == I18n.Str("editor.unbound"), "Explicit unbinding must be localized and distinct from default.");
                RespondDialog(form, delegate { ClickBindingCell(button); }, delegate(Form dialog)
                {
                    var choices = Field<ComboBox>(dialog, "keys");
                    Assert(choices.Text == I18n.Str("editor.unbound"), "Reopening unbound must keep the explicit unbound choice.");
                    var capture = Descendants(dialog).Single(control => control.GetType().Name == "KeyCaptureBox");
                    Assert(!(capture is TextBoxBase) && capture.ContextMenuStrip == null, "Capture area must not be a text editor or expose an edit menu.");
                    VisibleInsideForm(dialog, capture);
                    foreach (var action in Descendants(dialog).OfType<Button>().Where(control => control.Visible)) VisibleInsideForm(dialog, action);
                    if (!gamepad)
                    {
                        capture.Focus();
                        SendMessage(capture.Handle, 0x100, new IntPtr((int)Keys.F9), new IntPtr(1));
                        Assert(choices.Text == "F9", "Capture control must receive keyboard input.");
                        SendMessage(capture.Handle, 0x204, IntPtr.Zero, new IntPtr(8 | 8 << 16));
                        SendMessage(capture.Handle, 0x205, IntPtr.Zero, new IntPtr(8 | 8 << 16));
                        Assert(choices.Text == BindingButton.FriendlyKey("RightMouseButton"), "Right click must capture a mouse binding.");
                        SendMessage(capture.Handle, 0x7b, capture.Handle, new IntPtr(-1));
                        SendMessage(capture.Handle, 0x100, new IntPtr((int)Keys.ShiftKey), new IntPtr(1L | 0x36L << 16));
                        Assert(choices.Text == BindingButton.FriendlyKey("RightShift"), "Capture must preserve modifier side.");
                    }
                    choices.SelectedIndex = 1;
                    if (index == 0) Shot(dialog, output, gamepad ? "unbound-gamepad-dialog" : "unbound-keyboard-dialog");
                    ((Button)dialog.CancelButton).PerformClick();
                }, "BindingDialog");
                Assert(button.Value == "None", "Cancelling capture must preserve unbinding.");
                BindingAction(form, gamepad, "IA_Guard", 0, "", false);
                Assert((gamepad ? form.Session.GetGamepad("IA_Guard", 0) == form.Session.GetGamepadDefault("IA_Guard", 0) : form.Session.GetKeyboard("IA_Guard", 0) == null), "Selecting default must restore inheritance rather than unbind.");
                string display = ((BindingButton)BindingCell(form, gamepad, "IA_Guard", 0)).DisplayText;
                string expectedDefault = gamepad ? form.Session.GetGamepadDefault("IA_Guard", 0) : form.Session.GetKeyboardDefault("IA_Guard", 0);
                Assert(display == BindingButton.DefaultValue(new[] { expectedDefault }), "Inherited cells must display their exact resource slot default in every language.");
                if (form.Session.HasChanges) ClickButton(form, "btnUndo");
            }
        }
        language.SelectedIndex = 0; ClickDevice(form, false);
        BindingAction(form, false, "IA_Guard", 0, "None", false);
        Shot(form, output, "unbound-draft"); ClickButton(form, "btnUndo");
        Assert(before.SequenceEqual(File.ReadAllBytes(input)) && sharedBefore.SequenceEqual(File.ReadAllBytes(shared)), "Binding dialogs must not write saves before Apply.");
    }
    static void ResolveConflictButton(MainForm form, DialogResult answer)
    {
        var link = Field<LinkLabel>(form, "btnConflicts");
        Assert(link.Visible && link.Enabled && form.Session.Conflicts.Length == 1, "Conflict action is unavailable for one unresolved conflict.");
        RespondDialog(form, delegate
        {
            // LinkLabel has no PerformClick. Dispatch its actual Click event; the
            // registered UI handler owns opening and resolving the confirmation.
            typeof(Control).GetMethod("OnClick", Hidden).Invoke(link, new object[] { EventArgs.Empty });
        }, delegate(Form dialog)
        {
            var button = Descendants(dialog).OfType<Button>().Single(candidate => candidate.DialogResult == answer);
            VisibleInsideForm(dialog, button); button.PerformClick();
        }, null);
    }
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
                    var capture = Descendants(dialog).First(c => c.GetType().Name == "KeyCaptureBox");
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
            bool controlsOnly = args.Contains("--controls-only");
            int languageCount = args.Contains("--all-languages") ? I18n.Languages.Length : 1;
            captureImages = !controlsOnly;
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
                form.UiSmoke(input); form.ShowActionView(true); form.Show(); Application.DoEvents();
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
                if (args.Contains("--bindings-only"))
                {
                    Field<ComboBox>(form, "cbLang").SelectedIndex = 0;
                    AllBindingCellsCancel(form, input, shared);
                    UnboundBindings(form, input, shared, output, languageCount);
                    ClearBindingOverrides(form, input, shared);
                    Console.WriteLine("PASS: binding controls; " + checks + " checks; " + languageCount + " languages.");
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
                for (int i = 0; i < languageCount; i++)
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
                ClickProfile(form, other);
                string otherPadJump = form.Session.GetGamepad("IA_Jump", 0);
                ClickProfile(form, editing);
                foreach (string profile in form.Session.Profiles) ClickProfile(form, profile);
                ClickProfile(form, editing);
                AllBindingCellsCancel(form, input, shared);
                ClearBindingOverrides(form, input, shared);
                string[] originalDodge = Enumerable.Range(0, 3).Select(i => form.Session.GetKeyboard("IA_Dodge", i)).ToArray();
                presets.SelectedItem = presets.Items.OfType<Preset>().First(p => p.Id == "onekey_dodge");
                Assert(form.Session.PresetId == "onekey_dodge" && apply.Enabled, "Selecting quick configuration did not create a draft.");
                presets.SelectedItem = presets.Items.OfType<Preset>().First(p => p.Id == "onekey");
                Assert(Enumerable.Range(0, 3).All(i => form.Session.GetKeyboard("IA_Dodge", i) == originalDodge[i]), "Quick configurations accumulated an earlier dodge preview.");
                EditKeyboard(form, "IA_Jump", 0, "F8", output);
                ClickDevice(form, true);
                EditKeyboard(form, "IA_Jump", 0, "Gamepad_Special_Right", output, true);
                ClickProfile(form, other);
                Assert(form.Session.GetGamepad("IA_Jump", 0) == otherPadJump, "A controller draft leaked into another scheme.");
                form.Session.SetGamepad("IA_Jump", 0, otherPadJump == "Gamepad_RightTrigger" ? "Gamepad_LeftTrigger" : "Gamepad_RightTrigger"); form.RenderSession();
                ClickProfile(form, editing);
                Assert(form.Session.GetGamepad("IA_Jump", 0) == "Gamepad_Special_Right", "Scheme navigation lost the selected controller draft.");
                ClickDevice(form, false);
                Assert(form.Session.PresetId == null, "Manual edit did not finish the configuration preview.");
                ActionsAlign(form, true);
                Assert(initialInput.SequenceEqual(File.ReadAllBytes(input)) && initialShared.SequenceEqual(File.ReadAllBytes(shared)), "Drafting or language selection wrote to disk.");

                // A real external disk change must appear after switching away and back.
                var disk = SaveFile.Load(input);
                var record = disk.RecordFor(editing);
                record.Entries.RemoveAll(e => e.Name == "IA_Interact" && e.Slot == 0);
                record.Entries.Add(new BlobEntry { Name = "IA_Interact", Key = "F9", X = "None", Y = "None", Slot = 0 });
                disk.Save(input);
                ClickProfile(form, other); ClickProfile(form, editing);
                Assert(form.Session.GetKeyboard("IA_Jump", 0) == "F8", "Scheme switching lost a local draft.");
                Assert(form.Session.GetKeyboard("IA_Interact", 0) == "F9", "Scheme switching did not read the latest disk binding.");
                Shot(form, output, "draft-zh-Hans");
                var activate = Field<CheckBox>(form, "chkActivate");
                activate.Checked = true;
                Assert(form.Session.ActivationPending, "Activation checkbox did not stage activation.");
                Shot(form, output, "draft-activate-zh-Hans");
                activate.Checked = false;
                Assert(!form.Session.ActivationPending, "Unchecking activation left a hidden intention.");
                ClickDevice(form, true); ColumnsAlign(form, "padHost"); Shot(form, output, "gamepad-zh-Hans");
                ClickDevice(form, false);
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
                ClickButton(form, "btnUndo");
                Assert(!form.Session.IsResetPending && presets.Enabled, "Undo reset did not restore the editor.");
                form.Session.SetKeyboard("IA_Jump", 0, "F10"); form.RenderSession();
                for (int i = 0; i < languageCount; i++)
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
                ClickButton(form, "btnRefresh");
                Assert(form.Session.Conflicts.Length == 1 && !apply.Enabled, "Refresh must show a same-binding conflict and block Apply.");
                Shot(form, output, "conflict-zh-Hans");
                byte[] conflictedInput = File.ReadAllBytes(input);
                string conflictId = form.Session.Conflicts.Single().Id;
                Console.WriteLine("Conflict button: Cancel, use game, then keep draft.");
                ResolveConflictButton(form, DialogResult.Cancel);
                Assert(form.Session.Conflicts.Single().Id == conflictId && form.Session.GetKeyboard("IA_Jump", 0) == "F10" && !apply.Enabled,
                    "Cancelling conflict resolution lost the draft or unblocked Apply.");
                ResolveConflictButton(form, DialogResult.No);
                Assert(form.Session.Conflicts.Length == 0 && form.Session.GetKeyboard("IA_Jump", 0) == "F11" && !form.Session.HasChanges && !apply.Enabled,
                    "Adopting disk did not clear the selected conflict and its draft.");
                Assert(conflictedInput.SequenceEqual(File.ReadAllBytes(input)), "Conflict Cancel/Use game wrote disk before Apply.");
                BindingAction(form, false, "IA_Jump", 0, "F8", false);
                disk = SaveFile.Load(input); record = disk.RecordFor(editing);
                record.Entries.First(e => e.Name == "IA_Jump" && e.Slot == 0).Key = "F9"; disk.Save(input);
                ClickButton(form, "btnRefresh");
                Assert(form.Session.Conflicts.Length == 1 && !apply.Enabled, "Second external edit did not produce a resolvable conflict.");
                byte[] beforeLocalChoice = File.ReadAllBytes(input);
                ResolveConflictButton(form, DialogResult.Yes);
                Assert(form.Session.Conflicts.Length == 0 && form.Session.GetKeyboard("IA_Jump", 0) == "F8" && apply.Enabled,
                    "Keep draft did not retain the local binding and enable Apply.");
                Assert(beforeLocalChoice.SequenceEqual(File.ReadAllBytes(input)), "Keep draft wrote disk before Apply.");
                ClickButton(form, "btnApply");
                Assert(SaveFile.Load(input).RecordFor(editing).Entries.First(e => e.Name == "IA_Jump" && e.Slot == 0).Key == "F8",
                    "Apply after Keep draft did not save the chosen binding.");
                OtherProfilesUnchanged(beforeLocalChoice, File.ReadAllBytes(input), editing);
                Assert(initialShared.SequenceEqual(File.ReadAllBytes(shared)), "Conflict resolution changed the active scheme unexpectedly.");
                ClickProfile(form, other); ClickButton(form, "btnUndo"); ClickProfile(form, editing);
                Assert(!form.Session.HasAnyDrafts, "UI undo did not discard the remaining other-scheme draft.");
                byte[] validShared = File.ReadAllBytes(shared);
                try
                {
                    byte[] invalidShared = (byte[])validShared.Clone(); invalidShared[invalidShared.Length - 1] ^= 1;
                    File.WriteAllBytes(shared, invalidShared); ClickButton(form, "btnRefresh");
                    var status = Field<Label>(form, "lblStatus");
                    Assert(form.Session.ActiveProfile == null && status.Text.Contains(I18n.Str("editor.schemeunknown")), "Ordinary refresh hid the persistent unknown-scheme explanation.");
                    Assert(!activate.Enabled, "Unknown selector still allowed activation.");
                    Shot(form, output, "unknown-scheme-zh-Hans");
                }
                finally { File.WriteAllBytes(shared, validShared); ClickButton(form, "btnRefresh"); }
            }
            Console.WriteLine("PASS: " + checks + " UI assertions; artifacts: " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
