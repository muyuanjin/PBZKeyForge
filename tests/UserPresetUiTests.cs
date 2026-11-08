using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class UserPresetUiTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
    static Button Button(Control owner, string name) { return All(owner).OfType<Button>().Single(button => button.Name == name); }
    static void Wait(Func<bool> predicate, string message)
    {
        var clock = Stopwatch.StartNew(); bool satisfied = predicate();
        while (!satisfied && clock.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(10); satisfied = predicate(); }
        Assert(satisfied, message);
    }
    static void Import(UserPresetDialog dialog, string code)
    {
        if (Field<TextBox>(dialog, "shareBox").Text == code) Field<TextBox>(dialog, "shareBox").Text = "";
        Field<TextBox>(dialog, "shareBox").Text = code;
        Assert(!Button(dialog, "usePreset").Enabled, "Editing immediately invalidates the accepted plan");
        Wait(() => Field<Preset>(dialog, "displayed") != null, "Pasting automatically previews without another button");
    }
    static string Copy(UserPresetDialog dialog)
    {
        Assert(Button(dialog, "copyShare").Visible && Button(dialog, "copyShare").Enabled, "Sharing is available for the selected source");
        Button(dialog, "copyShare").PerformClick();
        Assert(Field<Label>(dialog, "status").Text == I18n.Str("preset.copied"), "Sharing reports success");
        string expected = Field<string>(dialog, "shareCode"), copied = null;
        Wait(() => (copied = Clipboard.GetText()) == expected, "Clipboard contains the scoped share code");
        return copied;
    }
    static void Shot(Control owner, string path)
    { using (var image = new Bitmap(owner.Width, owner.Height)) { owner.DrawToBitmap(image, owner.ClientRectangle); image.Save(path); } }
    static string[] Contents(UserPresetDialog dialog)
    { return Field<DataGridView>(dialog, "grid").Rows.Cast<DataGridViewRow>().Select(row => string.Join("|", row.Cells.Cast<DataGridViewCell>().Select(cell => Convert.ToString(cell.Value)))).ToArray(); }
    static void RowsFit(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        foreach (DataGridViewCell cell in row.Cells)
        {
            string value = Convert.ToString(cell.Value); if (value.Length == 0) continue;
            var style = cell.InheritedStyle;
            int width = Math.Max(1, cell.Size.Width - style.Padding.Horizontal - 6);
            int textHeight = TextRenderer.MeasureText(value, style.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Assert(row.Height >= textHeight + style.Padding.Vertical, "Visible preset row keeps its full text at the current DPI");
        }
    }
    static void Fits(UserPresetDialog dialog)
    {
        Assert(Field<TextBox>(dialog, "shareBox").Visible && Field<CheckedListBox>(dialog, "list").Visible, "Import and library remain available together");
        foreach (string name in new[] { "pasteShare", "deleteSelected", "copyShare", "exportPreset", "renamePreset", "usePreset" })
        {
            var button = Button(dialog, name);
            if (!button.Visible) continue;
            Assert(button.Parent.ClientRectangle.Contains(button.Bounds), "Action fits minimum size: " + name);
            Assert(TextRenderer.MeasureText(button.Text, button.Font).Width < button.Width, "Action label fits minimum size: " + name);
        }
        var grid = Field<DataGridView>(dialog, "grid");
        RowsFit(grid);
        Assert(grid.Visible && grid.Parent.ClientRectangle.Contains(grid.Bounds), "Preset contents fit their viewport");
        Assert(!All(grid).OfType<HScrollBar>().Any(bar => bar.Visible), "Contents wrap without horizontal scrolling");
        Assert(!All(dialog).Any(control => control is PresetModeSelector), "Library browsing has no application-mode selector");
    }
    static void CaptureSizes(UserPresetDialog dialog, string output, string name)
    {
        Size regular = dialog.Size; Shot(dialog, Path.Combine(output, name + "-regular.png"));
        dialog.Size = dialog.MinimumSize; Application.DoEvents(); Shot(dialog, Path.Combine(output, name + "-minimum.png")); Fits(dialog);
        dialog.Size = regular; Application.DoEvents();
    }
    static void Delete(UserPresetDialog dialog, DialogResult answer)
    {
        bool answered = false;
        using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
        {
            timer.Tick += delegate {
                var confirm = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form != dialog && All(form).OfType<Button>().Any(button => button.DialogResult == answer));
                if (confirm == null) return;
                timer.Stop(); All(confirm).OfType<Button>().Single(button => button.DialogResult == answer).PerformClick(); answered = true;
            };
            timer.Start(); Button(dialog, "deleteSelected").PerformClick();
        }
        Assert(answered, "Delete opens confirmation"); Application.DoEvents();
    }
    static void Show(UserPresetDialog dialog)
    { dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents(); }

    [STAThread]
    public static int Main(string[] args)
    {
        string clipboard = null;
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            if (Clipboard.ContainsText()) clipboard = Clipboard.GetText();
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] fixture = File.ReadAllBytes(args[0]); string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, fixture);
            var session = EditorSession.Open(input); session.SwitchProfile("PBZ.KeyProfile.Custom1"); session.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
            var imported = UserPresetStore.Capture(session, "少主剑常用"); string code = UserPresetStore.Share(imported);
            var store = new UserPresetStore(Path.Combine(output, "presets")); store.Delete(store.Load()); store.Save(imported, "本地攻防"); store.Save(imported, "手柄攻防");
            byte[] draft = session.ExportCurrentScheme();
            Assert(typeof(UserPresetDialog).GetConstructors().All(ctor => ctor.GetParameters().Length == 1 && ctor.GetParameters()[0].ParameterType == typeof(UserPresetStore)),
                "Preset manager requires only its library, without editor callbacks");
            using (var dialog = new UserPresetDialog(store))
            {
                Show(dialog);
                Assert(Field<TextBox>(dialog, "shareBox").Focused, "Opening focuses share-code input");
                Assert(Field<CheckedListBox>(dialog, "list").SelectedItem is Preset && Button(dialog, "usePreset").Enabled && !Button(dialog, "saveImport").Visible, "Opening a populated library selects a readable favorite without an import-save action");
                Assert(dialog.CancelButton == dialog.captionClose, "The title bar closes the manager");
                Import(dialog, code);
                var grid = Field<DataGridView>(dialog, "grid"); var rows = Contents(dialog); RowsFit(grid);
                Assert(grid.Name == "presetContentsTable" && rows.Length > imported.UserMappings.Count, "Preview includes complete preset contents, including unchanged default actions");
                Assert(rows.Any(row => row.Contains(BindingWorkspace.FunctionLabel("IA_Guard")) && row.Contains(BindingButton.FriendlyKey("LeftMouseButton"))), "Preview shows the preset's own guard binding");
                Assert(!grid.Columns.Contains("before") && !grid.Columns.Contains("after"), "Library contents have no draft comparison columns");
                Assert(store.Load().Count == 2 && session.ExportCurrentScheme().SequenceEqual(draft), "Preview changes neither library nor draft even when both have identical contents");
                Field<TextBox>(dialog, "previewName").Text = "";
                Assert(Button(dialog, "usePreset").Enabled && !Button(dialog, "saveImport").Enabled, "Choosing an import needs no name or intermediate favorite");
                Field<TextBox>(dialog, "previewName").Text = "收藏的攻防";
                CaptureSizes(dialog, output, "preset-import");
                var scopes = Field<ComboBox>(dialog, "shareScope"); int allLength = Copy(dialog).Length;
                scopes.SelectedItem = scopes.Items.Cast<object>().Single(item => item.ToString() == I18n.Str("preset.keyboardonly"));
                string keyboardCode = Copy(dialog);
                Assert(UserPresetStore.ParseShare(keyboardCode).Scopes.Single().Family == null && keyboardCode.Length < allLength, "Keyboard-only sharing excludes controller scopes");
                Assert(Contents(dialog).SequenceEqual(rows), "Sharing range leaves the complete contents preview intact");
                Button(dialog, "saveImport").PerformClick(); Application.DoEvents();
                Assert(store.Load().Count == 3 && Field<CheckedListBox>(dialog, "list").SelectedItem is Preset, "Favoriting saves and selects the library item");
                Assert(Field<CheckedListBox>(dialog, "list").CheckedItems.Count == 0 && dialog.AppliedPreset == null, "Favoriting neither checks deletion nor requests loading");
                Assert(session.ExportCurrentScheme().SequenceEqual(draft) && !Button(dialog, "saveImport").Visible, "Favoriting leaves the editor draft intact");
                Field<TextBox>(dialog, "previewName").Text = "重命名攻防"; Button(dialog, "renamePreset").PerformClick(); Application.DoEvents();
                Assert(store.Load().Any(item => item.Name == "重命名攻防") && store.Load().Count == 3, "Renaming updates the existing favorite");
                Assert(Contents(dialog).SequenceEqual(rows), "Renaming preserves the complete preset contents");
                CaptureSizes(dialog, output, "preset-manager");
                var list = Field<CheckedListBox>(dialog, "list");
                for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); Application.DoEvents();
                Delete(dialog, DialogResult.Cancel); Assert(store.Load().Count == 3, "Cancel deletion keeps favorites");
                Delete(dialog, DialogResult.OK);
                Assert(store.Load().Count == 0 && session.ExportCurrentScheme().SequenceEqual(draft), "Deleting favorites leaves the editor draft intact");
                Assert(!Button(dialog, "usePreset").Enabled, "An empty library has no stale selection");
                Shot(dialog, Path.Combine(output, "preset-manager-empty.png"));
                Import(dialog, code); Field<TextBox>(dialog, "shareBox").Text += "!";
                Assert(!Button(dialog, "usePreset").Enabled && !Button(dialog, "saveImport").Enabled, "Editing code immediately invalidates preceding actions");
                Wait(() => !Field<System.Windows.Forms.Timer>(dialog, "importTimer").Enabled, "Invalid import finishes parsing");
                Assert(Field<Preset>(dialog, "displayed") == null && !Button(dialog, "copyShare").Enabled, "Invalid code cannot retain an old preview or share action");
                Import(dialog, code); dialog.captionClose.PerformClick();
                Assert(dialog.AppliedPreset == null && session.ExportCurrentScheme().SequenceEqual(draft), "Closing after browsing imports never loads them");
            }
            using (var dialog = new UserPresetDialog(store))
            {
                Show(dialog); Import(dialog, code); Field<TextBox>(dialog, "previewName").Text = "";
                Button(dialog, "usePreset").PerformClick();
                Assert(dialog.DialogResult == DialogResult.OK && dialog.AppliedPreset != null && dialog.AppliedPreset.Id == null && store.Load().Count == 0,
                    "Load only returns the chosen import without inserting a favorite");
                Assert(session.ExportCurrentScheme().SequenceEqual(draft), "Returning a preset does not apply it to an editor");
            }
            var psSource = EditorSession.CreateOffline(); int ps = psSource.ControllerFamilies.Single(item => item.Name == "PS").Index;
            psSource.SetGamepadFamily("IA_Jump", 0, ps, "Gamepad_LeftShoulder");
            var psPreset = UserPresetStore.SelectScopes(UserPresetStore.Capture(psSource, "PS跳跃"), new[] { new PresetScope { Family = "PS" } });
            using (var dialog = new UserPresetDialog(store))
            {
                Show(dialog); Import(dialog, UserPresetStore.Share(psPreset));
                Assert(Contents(dialog).Length > 1 && Contents(dialog).All(row => row.Contains("PS")), "PS-only preview shows the whole selected family without keyboard rows");
                Shot(dialog, Path.Combine(output, "preset-manager-second-family.png"));
            }
            Assert(fixture.SequenceEqual(File.ReadAllBytes(input)) && fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Library actions leave source saves untouched");
            Console.WriteLine("UserPresetUiTests PASS: " + checks + " checks; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (clipboard == null) Clipboard.Clear(); else Clipboard.SetText(clipboard); }
    }
}
