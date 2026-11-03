using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
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
    static void Mode(PresetModeSelector selector, PresetApplyMode mode)
    { selector.Controls.OfType<Button>().Single(button => button.Name == "mode" + mode).PerformClick(); Application.DoEvents(); }
    static string CopyShare(UserPresetDialog dialog)
    {
        Assert(Button(dialog, "copyShare").Enabled && Field<string>(dialog, "shareCode") != null, "Sharing is available for selected scope");
        Button(dialog, "copyShare").PerformClick();
        Assert(Field<Label>(dialog, "status").Text == I18n.Str("preset.copied"), "Copy succeeds: " + Field<Label>(dialog, "status").Text);
        string code = Clipboard.GetText();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (code != Field<string>(dialog, "shareCode") && watch.ElapsedMilliseconds < 1000)
        { Application.DoEvents(); System.Threading.Thread.Sleep(20); code = Clipboard.GetText(); }
        Assert(code == Field<string>(dialog, "shareCode"), "Clipboard matches selected scope code");
        return code;
    }
    static void Shot(Control owner, string path)
    { using (var image = new Bitmap(owner.Width, owner.Height)) { owner.DrawToBitmap(image, owner.ClientRectangle); image.Save(path); } }
    static void Delete(UserPresetDialog dialog, DialogResult answer)
    {
        bool answered = false;
        using (var timer = new Timer { Interval = 50 })
        {
            timer.Tick += delegate {
                var confirm = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form != dialog);
                if (confirm == null) return;
                timer.Stop(); All(confirm).OfType<Button>().Single(button => button.DialogResult == answer).PerformClick(); answered = true;
            };
            timer.Start(); Button(dialog, "deleteSelected").PerformClick();
        }
        Assert(answered, "Delete opens confirmation"); Application.DoEvents();
    }
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
            var session = EditorSession.Open(input); session.SwitchProfile("PBZ.KeyProfile.Custom1");
            session.SetKeyboard("IA_Guard", 0, "LeftMouseButton");
            var imported = UserPresetStore.Capture(session, "少主剑常用"); string code = UserPresetStore.Share(imported);
            var store = new UserPresetStore(Path.Combine(output, "presets")); store.Delete(store.Load());
            store.Save(imported, "本地攻防"); store.Save(imported, "手柄攻防");
            int draft = session.ScopeChangeCount;
            using (var dialog = new UserPresetDialog(session, store, PresetApplyMode.Append))
            {
                dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
                var currentName = Field<TextBox>(dialog, "nameBox");
                var saveCurrent = Button(dialog, "saveCurrent");
                Assert(currentName.Focused, "Opening the manager focuses the name field");
                Assert(saveCurrent.Enabled && !((EditorButton)saveCurrent).Primary, "Empty-name save stays interactive and visually secondary");
                Assert(Field<ToolTip>(dialog, "tips").GetToolTip(saveCurrent) == I18n.Str("preset.entername"), "Hovering an empty-name save explains what to enter");
                if (args.Contains("--native-guidance"))
                {
                    Point originalPointer = Cursor.Position;
                    bool appeared = false;
                    PopupEventHandler observed = delegate { appeared = true; };
                    var tooltip = Field<ToolTip>(dialog, "tips"); tooltip.Popup += observed;
                    try
                    {
                        dialog.Opacity = 1; dialog.TopMost = true; dialog.Activate(); Application.DoEvents();
                        Cursor.Position = saveCurrent.PointToScreen(new Point(saveCurrent.Width / 2, saveCurrent.Height / 2));
                        var hoverTime = System.Diagnostics.Stopwatch.StartNew();
                        while (!appeared && hoverTime.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Application.DoEvents();
                        var card = saveCurrent.Parent.Parent;
                        using (var image = new Bitmap(card.Width, card.Height))
                        {
                            using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(card.PointToScreen(Point.Empty), Point.Empty, image.Size);
                            image.Save(Path.Combine(output, "preset-name-hover.png"));
                        }
                        Assert(appeared, "Native hover opens name guidance on the gray interactive save button");
                    }
                    finally
                    {
                        tooltip.Popup -= observed; tooltip.Hide(saveCurrent); Cursor.Position = originalPointer;
                        dialog.TopMost = false; dialog.Opacity = 0;
                    }
                }
                saveCurrent.PerformClick(); Application.DoEvents();
                Assert(currentName.Focused && Field<Label>(dialog, "nameHint").Text == I18n.Str("preset.entername") && store.Load().Count == 2,
                    "Empty save focuses name, explains inline and never creates a preset");
                Shot(dialog, Path.Combine(output, "preset-name-required.png"));
                currentName.Text = "少主剑常用"; Application.DoEvents();
                Assert(((EditorButton)saveCurrent).Primary && Field<Label>(dialog, "nameHint").Text == "" && string.IsNullOrEmpty(Field<ToolTip>(dialog, "tips").GetToolTip(saveCurrent)),
                    "Typing a name highlights saving and clears the missing-name guidance");
                Shot(dialog, Path.Combine(output, "preset-name-ready.png"));
                currentName.Text = "  "; saveCurrent.PerformClick(); Application.DoEvents();
                Assert(!((EditorButton)saveCurrent).Primary && currentName.Focused && store.Load().Count == 2, "Whitespace behaves like an empty name");
                currentName.Text = "";
                Size regular = dialog.Size;
                Assert(Button(dialog, "usePreset").Enabled && session.PresetId == null, "First preset is browsable without application");
                Field<TextBox>(dialog, "shareBox").Text = code; Button(dialog, "previewShare").PerformClick();
                Assert(Field<TextBox>(dialog, "previewName").Text == imported.Name && !Field<TextBox>(dialog, "previewName").ReadOnly, "Imported name is editable");
                Assert(session.ScopeChangeCount == draft && session.PresetId == null && store.Load().Count == 2, "Import preview changes neither draft nor library");
                var scopes = Field<ComboBox>(dialog, "shareScope");
                Assert(scopes.Items.Count == 5 && scopes.Items.Cast<object>().Select(item => item.ToString()).OrderBy(name => name)
                    .SequenceEqual(new[] { I18n.Str("preset.alldevices"), I18n.Str("preset.keyboardonly"), I18n.Str("preset.controllersonly"), "Xbox", "PS" }.OrderBy(name => name)),
                    "Share choices include keyboard and both editable controller families");
                Assert(All(Field<Panel>(dialog, "preview")).OfType<Label>().Any(label => label.Text == I18n.Str("preset.devices", UserPresetStore.ScopeText(UserPresetStore.ParseShare(code)))), "Import shows included devices before application");
                int allLength = CopyShare(dialog).Length;
                scopes.SelectedIndex = 1; string keyboardCode = CopyShare(dialog);
                var sharedKeyboard = UserPresetStore.ParseShare(keyboardCode);
                Assert(sharedKeyboard.Scopes.Count == 1 && sharedKeyboard.Scopes[0].Family == null && keyboardCode.Length < allLength, "Keyboard copy omits controller scope and shortens code");
                Assert(Field<Label>(dialog, "shareLength").Text == I18n.Str("preset.sharelength", keyboardCode.Length), "Character count includes code and name");
                scopes.SelectedItem = scopes.Items.Cast<object>().Single(item => item.ToString() == "Xbox");
                var sharedPad = UserPresetStore.ParseShare(CopyShare(dialog));
                Assert(sharedPad.Scopes.Single().Family == "Xbox" && sharedPad.UserMappings.Count == 0, "Unchanged controller can be shared with explicit reset scope");
                Assert(session.ScopeChangeCount == draft && store.Load().Count == 2 && session.PresetId == null, "Sharing device selection never changes library or draft");
                scopes.SelectedIndex = 0;
                Field<TextBox>(dialog, "previewName").Text = "导入少主剑";
                dialog.Size = dialog.MinimumSize; Application.DoEvents();
                Assert(Field<TextBox>(dialog, "previewName").Text == "导入少主剑", "Resize preserves imported name edit");
                Assert(UserPresetStore.ParseShare(CopyShare(dialog)).Name == "导入少主剑", "Copy share includes edited name");
                Assert(Button(dialog, "saveImport").Visible && Button(dialog, "saveImport").Parent.ClientRectangle.Contains(Button(dialog, "saveImport").Bounds), "Imported save action fits and belongs to import section");
                Shot(dialog, Path.Combine(output, "preset-import-minimum.png"));
                Button(dialog, "saveImport").PerformClick(); Application.DoEvents();
                Assert(!Button(dialog, "saveImport").Visible, "Library details hide import-only save action");
                Assert(store.Load().Count == 3 && session.ScopeChangeCount == draft && session.PresetId == null, "Save import only adds to library");
                dialog.Size = regular; Application.DoEvents();
                Shot(dialog, Path.Combine(output, "preset-manager-regular.png"));
                dialog.Size = dialog.MinimumSize; Application.DoEvents();
                foreach (string name in new[] { "previewShare", "copyShare", "usePreset", "deleteSelected", "saveCurrent", "exportGame" })
                {
                    var button = Button(dialog, name);
                    Assert(button.Visible && button.Parent.ClientRectangle.Contains(button.Bounds), "Button fits minimum: " + name);
                    Assert(TextRenderer.MeasureText(button.Text, button.Font).Width < button.Width, "Label fits minimum: " + name);
                }
                Shot(dialog, Path.Combine(output, "preset-manager-minimum.png"));
                Assert(!Field<Panel>(dialog, "preview").HorizontalScroll.Visible, "Preview fits without horizontal scrolling");
                var modeButton = Field<PresetModeSelector>(dialog, "modeButton");
                foreach (Button option in modeButton.Controls)
                {
                    Assert(option.Visible && modeButton.ClientRectangle.Contains(option.Bounds), "All three modes fit the local selector");
                    Assert(TextRenderer.MeasureText(option.Text, option.Font).Width < option.Width, "All mode names are readable");
                }
                foreach (string action in new[] { "saveCurrent", "exportGame" })
                {
                    var inputCenter = Field<TextBox>(dialog, "nameBox").PointToScreen(new Point(0, Field<TextBox>(dialog, "nameBox").Height / 2));
                    var button = Button(dialog, action); var buttonCenter = button.PointToScreen(new Point(0, button.Height / 2));
                    Assert(Math.Abs(inputCenter.Y - buttonCenter.Y) <= 3, "Current scheme input and action centers align: " + action);
                }
                Assert(modeButton.Parent.Parent == Button(dialog, "usePreset").Parent.Parent && scopes.Parent == Button(dialog, "copyShare").Parent, "Mode belongs to applying; device filter belongs to copying");
                Mode(modeButton, PresetApplyMode.OnlyPreset);
                Assert(modeButton.Mode == PresetApplyMode.OnlyPreset && Field<Label>(dialog, "modeHelp").Text == I18n.Str("preset.only.scopehelp"), "Only preset tooltip explains scope isolation");
                Mode(modeButton, PresetApplyMode.Append);
                Assert(modeButton.Mode == PresetApplyMode.Append && session.ScopeChangeCount == draft, "Direct mode selection never applies");
                var list = Field<CheckedListBox>(dialog, "list");
                for (int index = 0; index < list.Items.Count; index++) list.SetItemChecked(index, true); Application.DoEvents();
                Assert(Button(dialog, "copyShare").Enabled && Button(dialog, "usePreset").Enabled && Button(dialog, "deleteSelected").Enabled, "Deletion checks do not interrupt selected preset browsing");
                Delete(dialog, DialogResult.Cancel); Assert(store.Load().Count == 3, "Cancel deletion keeps library");
                Delete(dialog, DialogResult.OK); Assert(store.Load().Count == 0 && session.ScopeChangeCount == draft, "Batch deletion keeps draft");
                Assert(Button(dialog, "saveCurrent").Enabled && !((EditorButton)Button(dialog, "saveCurrent")).Primary && !Button(dialog, "usePreset").Enabled, "Empty library offers name guidance without invalid application");
                Assert(All(Field<Panel>(dialog, "preview")).OfType<Label>().Any(label => label.Text == I18n.Str("preset.emptyhelp")), "Empty library explains saving and importing");
                Shot(dialog, Path.Combine(output, "preset-manager-empty.png"));
                Field<TextBox>(dialog, "nameBox").Text = "当前方案";
                Assert(((EditorButton)Button(dialog, "saveCurrent")).Primary, "A name highlights Save current scheme");
                Shot(dialog, Path.Combine(output, "preset-save-current.png"));
                Button(dialog, "saveCurrent").PerformClick(); Application.DoEvents();
                Assert(store.Load().Count == 1 && session.ScopeChangeCount == draft, "Save current captures draft without applying");
                Mode(Field<PresetModeSelector>(dialog, "modeButton"), PresetApplyMode.Replace); Button(dialog, "usePreset").PerformClick();
                Assert(dialog.AppliedPreset != null && session.PresetId == dialog.AppliedPreset.Id && session.PresetMode == PresetApplyMode.Replace, "Explicit use stages selected mode");
            }
            using (var dialog = new UserPresetDialog(session, store, PresetApplyMode.Append))
            {
                dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
                var list = Field<CheckedListBox>(dialog, "list"); list.SetItemChecked(0, true); Application.DoEvents();
                draft = session.ScopeChangeCount; Delete(dialog, DialogResult.OK);
                Assert(session.PresetId == null && session.ScopeChangeCount == draft, "Deleting active preset retains staged bindings");
                for (int slot = 0; slot < 3; slot++) session.SetKeyboard("IA_Guard", slot, new[] { "F8", "F9", "F10" }[slot]);
                Field<TextBox>(dialog, "shareBox").Text = code; Button(dialog, "previewShare").PerformClick();
                draft = session.ScopeChangeCount; Button(dialog, "usePreset").PerformClick();
                Assert(store.Load().Count == 0 && session.ScopeChangeCount == draft && session.PresetId == null,
                    "Failed imported append neither saves nor changes draft");
            }
            var multi = SaveFile.FromBytes(fixture);
            var fields = multi.GamepadProfiles.First(item => item.ProfileId == session.SelectedProfile).Fields.Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(fields, "Family", "PS"); multi.Props.First(node => node.Name == "GamepadFamilyProfiles").Items.Add(fields);
            string multiPath = Path.Combine(output, "multiple-families.sav"); multi.Save(multiPath);
            var secondFamilySession = EditorSession.Open(multiPath); secondFamilySession.SwitchProfile(session.SelectedProfile);
            int secondFamily = secondFamilySession.ControllerFamilies.Single(item => item.Name == "PS").Index;
            secondFamilySession.SetGamepadFamily("IA_Guard", 0, secondFamily, "Gamepad_FaceButton_Left");
            var padPreset = UserPresetStore.Capture(secondFamilySession, "第二家族"); secondFamilySession.Undo();
            using (var dialog = new UserPresetDialog(secondFamilySession, store, PresetApplyMode.Replace))
            {
                dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
                Field<TextBox>(dialog, "shareBox").Text = UserPresetStore.Share(padPreset); Button(dialog, "previewShare").PerformClick();
                var scopes = Field<ComboBox>(dialog, "shareScope");
                Assert(scopes.Items.Cast<object>().Any(item => item.ToString() == I18n.Str("preset.controllersonly")), "Multiple controller families can be shared together");
                scopes.SelectedItem = scopes.Items.Cast<object>().Single(item => item.ToString() == I18n.Str("preset.controllersonly"));
                Assert(UserPresetStore.ParseShare(CopyShare(dialog)).Scopes.Select(scope => scope.Family).OrderBy(family => family).SequenceEqual(new[] { "PS", "Xbox" }), "Controllers-only copy excludes keyboard");
                scopes.SelectedItem = scopes.Items.Cast<object>().Single(item => item.ToString() == "PS");
                Assert(UserPresetStore.ParseShare(CopyShare(dialog)).Scopes.Single().Family == "PS", "One selected controller family is copied");
                var panel = Field<Panel>(dialog, "preview");
                Assert(All(panel).OfType<Label>().Any(label => label.Text == I18n.Str("editor.gamepad") + " · PS"), "Diagram identifies changed second family");
                var image = (Bitmap)All(panel).OfType<PictureBox>().Single().Image; bool blue = false;
                for (int y = 0; y < image.Height && !blue; y += 2)
                    for (int x = 0; x < image.Width && !blue; x += 2) blue = image.GetPixel(x, y).ToArgb() == Color.FromArgb(64, 158, 255).ToArgb();
                Assert(blue, "Second-family diagram contains its changed blue badge");
                Shot(dialog, Path.Combine(output, "preset-manager-second-family.png"));
            }
            Assert(fixture.SequenceEqual(File.ReadAllBytes(input)) && fixture.SequenceEqual(File.ReadAllBytes(args[0])), "UI never writes source saves");
            Console.WriteLine("UserPresetUiTests PASS: " + checks + " checks; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (clipboard != null) Clipboard.SetText(clipboard); }
    }
}
