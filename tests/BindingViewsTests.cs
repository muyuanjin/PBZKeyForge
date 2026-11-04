using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class BindingViewsTests
{
    static int checks, capturedLanguages;
    const string Guard = "IA_Guard";
    static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void Shot(MainForm form, string path)
    {
        Application.DoEvents(); form.Refresh();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
    }
    static void Inside(MainForm form, Control control)
    {
        var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        Assert(control.Visible && form.ClientRectangle.Contains(bounds), control.GetType().Name + " outside client: " + bounds);
    }
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    static Control Diagram(MainForm form)
    { Application.DoEvents(); return Descendants(Field<Control>(form, "keyBindings")).Single(c => c.GetType().Name == "DeviceDiagram"); }
    static void UnobstructedDiagram(MainForm form)
    {
        var view = Field<Control>(form, "keyBindings");
        var diagram = Diagram(form);
        var hint = Field<Control>(view, "hint");
        Assert(diagram.Top >= hint.Bottom && !diagram.Bounds.IntersectsWith(hint.Bounds),
            "The device canvas must start below the instruction label.");
    }
    static void Badge(MainForm form, string key, int count, Color color)
    {
        form.RenderSession(); var states = Field<IDictionary>(Diagram(form), "states"); object state = states[key];
        Assert((int)state.GetType().GetField("Count").GetValue(state) == count, "Wrong action count for " + key);
        Assert((Color)state.GetType().GetProperty("Color").GetValue(state, null) == color, "Wrong badge color for " + key);
        Assert((bool)state.GetType().GetProperty("Visible").GetValue(state, null), "Changed zero badge must remain visible: " + key);
    }
    static void SelectKey(MainForm form, string key)
    {
        var diagram = Diagram(form);
        var point = (PointF)diagram.GetType().GetMethod("Position", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(diagram, new object[] { key });
        diagram.GetType().GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(diagram,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)point.X, (int)point.Y, 0) });
        Assert(Field<string>(Field<Control>(form, "keyBindings"), "selectedKey") == key, "Hotspot did not select " + key);
    }
    static void Apply(EditorSession session)
    { var result = session.Apply(); Assert(result.Success, result.Error); }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            string root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
            string path = Path.Combine(root, "EnhancedInputUserSettings.sav");
            byte[] fixture = File.ReadAllBytes(args[0]); File.WriteAllBytes(path, fixture);
            string shared = Path.Combine(root, "PBZSharedSetting.sav"); File.WriteAllBytes(shared, new byte[] { 1 });
            var session = EditorSession.Open(path);
            string custom = session.Profiles.First(p => p.EndsWith("Custom1"));
            string original = session.Profiles.First(p => p.EndsWith(".Default"));
            session.SwitchProfile(original);
            Assert(session.Relations("IA_OpenMapMenu", false, -1).Keys.SequenceEqual(new[] { "M", "Tab" }), "Map must retain two action-level defaults.");
            Assert(session.RelationsForKey("LeftAlt", false, -1).Current.Single().Action == "IA_MoveSlow", "Resource slow movement must contribute to physical key functions.");
            session.ChangeRelationship(Guard, "LeftMouseButton", true, false, -1);
            Assert(session.Relations(Guard, false, -1).Keys.OrderBy(k => k).SequenceEqual(new[] { "LeftMouseButton", "RightMouseButton" }), "Adding default guard must retain right mouse.");
            Assert(session.RelationshipPending(Guard, "LeftMouseButton", false, -1)
                && !session.RelationshipPending(Guard, "RightMouseButton", false, -1), "An unrelated key must not inherit an action's pending marker.");
            var addedDiff = session.SchemeDifferences(false).Single(d => d.Action == Guard && !d.Gamepad);
            Assert(addedDiff.AddedKeys.SequenceEqual(new[] { "LeftMouseButton" }) && addedDiff.RemovedKeys.Length == 0,
                "Scheme diff must count added relationships rather than created internal slots.");
            session.ChangeRelationship(Guard, "RightMouseButton", false, false, -1);
            Assert(session.Relations(Guard, false, -1).Keys.SequenceEqual(new[] { "LeftMouseButton" }), "Removing default relation must not restore it.");
            session.RestoreAction(Guard, false, -1);
            Assert(session.Relations(Guard, false, -1).Keys.SequenceEqual(new[] { "RightMouseButton" }) && !session.HasChanges, "Restore must remove explicit overrides.");
            Assert(session.SchemeDifferences(false).Length == 0, "Restored draft must have no scheme diff.");
            session.ChangeRelationship(Guard, "LeftMouseButton", true, false, -1);
            Assert(session.RelationsForKey("LeftMouseButton", false, -1).Added.Any(r => r.Action == Guard)
                && session.Relations(Guard, false, -1).Changed, "Added relationship must show factory diff and pending state.");
            var savedDiff = session.Apply(); Assert(savedDiff.Success, savedDiff.Error);
            Assert(session.RelationsForKey("LeftMouseButton", false, -1).Added.Any(r => r.Action == Guard)
                && !session.Relations(Guard, false, -1).Changed, "Saved relationship must retain factory diff without pending state.");
            Assert(session.SchemeDifferences(false).Length == 0 && session.SchemeDifferences(true).Any(d => d.Action == Guard),
                "After applying, saved comparison must clear while factory comparison retains customization.");
            session.RestoreKey("LeftMouseButton", false, -1);
            Assert(!session.RelationsForKey("LeftMouseButton", false, -1).Differs
                && session.Relations(Guard, false, -1).Keys.SequenceEqual(new[] { "RightMouseButton" }), "Restore key must retain other keys.");
            session.RestoreAction(Guard, false, -1);
            var restoredDiff = session.Apply(); Assert(restoredDiff.Success, restoredDiff.Error);
            session.SwitchProfile(custom);
            Assert(session.FactoryKeyboardKeys(Guard).SequenceEqual(new[] { "RightMouseButton" }), "Custom schemes must use the shared factory keyboard mapping.");
            session.SelectPreset("mouse_guard");
            Assert(session.Relations(Guard, false, -1).Keys.Length == 2, "Duplicate guard slots must count one action per key.");
            Assert(session.Relations("IA_HeavyAttack", false, -1).Keys.OrderBy(k => k).SequenceEqual(new[] { "F", "RightMouseButton" }), "Preset lost F heavy attack.");
            session.SetKeyboard(Guard, 2, "LeftMouseButton");
            session.ChangeRelationship(Guard, "LeftMouseButton", false, false, -1);
            Assert(!session.Relations(Guard, false, -1).Keys.Contains("LeftMouseButton"), "Remove must clear every duplicate source of the relation.");
            session.ChangeRelationship(Guard, "G", true, false, -1);
            session.ReplaceRelationship(Guard, "G", "H", false, -1);
            Assert(session.Relations(Guard, false, -1).Keys.Contains("H") && !session.Relations(Guard, false, -1).Keys.Contains("G"), "Replace key did not edit the shared relation.");
            var result = session.Apply(); Assert(result.Success, result.Error);
            var reopened = EditorSession.Open(path); reopened.SwitchProfile(custom);
            Assert(reopened.Relations(Guard, false, -1).Keys.Contains("H"), "Save/reopen lost relationship edits.");
            reopened.SetKeyboard(Guard, 0, null);
            Assert(reopened.Relations(Guard, false, -1).Complete && reopened.Relations(Guard, false, -1).Keys.Contains("RightMouseButton"),
                "Missing keyboard overrides must inherit the extracted per-slot default.");
            reopened.SetKeyboard("IA_UnregisteredTest", 0, "X");
            bool rejected = false;
            try { reopened.ChangeRelationship("IA_UnregisteredTest", "J", true, false, -1); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Unregistered actions must not invent inherited defaults.");
            reopened.Undo();
            byte[] beforePartial = File.ReadAllBytes(path);
            reopened.RestoreAction(Guard, false, -1); Apply(reopened);
            reopened.SetKeyboard(Guard, 0, "LeftMouseButton");
            Assert(reopened.RelationshipPending(Guard, "LeftMouseButton", false, -1)
                && !reopened.RelationshipPending(Guard, "RightMouseButton", false, -1), "The untouched second guard slot must retain right mouse.");
            Assert(reopened.Relations(Guard, false, -1).Keys.OrderBy(k => k).SequenceEqual(new[] { "LeftMouseButton", "RightMouseButton" }),
                "A single guard override must inherit the other registered slot.");
            Apply(reopened);
            Assert(reopened.SchemeDifferences(false).Length == 0, "Saved overrides must have no pending differences.");
            reopened.SetKeyboard(Guard, 1, "None");
            var completedPartial = reopened.SchemeDifferences(false).Single(d => d.Action == Guard);
            Assert(completedPartial.BeforeComplete && completedPartial.AfterComplete && completedPartial.RemovedKeys.Contains("RightMouseButton"),
                "Explicitly unbinding the inherited second slot must remove right mouse exactly.");
            reopened.Undo(); File.WriteAllBytes(path, beforePartial);

            // Duplicate records and a second family are supported save shapes.
            var shaped = SaveFile.Load(path);
            var duplicate = shaped.RecordFor(custom).Entries.First(e => e.Name == Guard);
            shaped.RecordFor(custom).Entries.Add(new BlobEntry { Name = Guard, Slot = duplicate.Slot, Key = "F11", X = "metadata", Y = "None" });
            var source = shaped.GamepadProfiles.First(p => p.ProfileId == custom);
            var familyFields = source.Fields.Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(familyFields, "Family", "DualSense");
            var familyKeys = familyFields.First(n => n.Name == "Keys");
            foreach (var item in familyKeys.Items.Where(i => SaveFile.Field(i, "MappingName") == Guard))
            { SaveFile.SetField(item, "Key", "Gamepad_RightShoulder"); }
            shaped.Props.First(n => n.Name == "GamepadFamilyProfiles").Items.Add(familyFields); shaped.Save(path);
            session = EditorSession.Open(path); session.SwitchProfile(custom);
            var families = session.ControllerFamilies;
            Assert(families.Select(f => f.Name).OrderBy(name => name).SequenceEqual(new[] { "DualSense", "PS", "Xbox" }),
                "Saved families and the missing supported family must stay separate within the selected scheme.");
            Assert(session.Relations(Guard, true, families[1].Index).DefaultKeys.SequenceEqual(new[] { "Gamepad_LeftShoulder" }), "Controller factory defaults must come from registered resources.");
            var firstKeys = session.BindingRecords(true, families[0].Index).Select(r => r.Key).ToArray();
            session.ChangeRelationship(Guard, "Gamepad_RightShoulder", false, true, families[1].Index);
            Assert(firstKeys.SequenceEqual(session.BindingRecords(true, families[0].Index).Select(r => r.Key)), "Relationship edit leaked to another controller family.");
            session.RestoreAction(Guard, true, families[1].Index);
            Assert(session.Relations(Guard, true, families[1].Index).Keys.SequenceEqual(new[] { "Gamepad_LeftShoulder" }), "Family restore must use the registered factory key.");
            var duplicateRecord = session.BindingRecords(false, -1).Single(r => r.Key == "F11" && r.Action == Guard);
            session.SetBindingRecord(duplicateRecord.Id, "F12");
            Assert(session.BindingRecords(false, -1).Any(r => r.Key == "F12" && r.Action == Guard), "Raw duplicate must be independently editable.");
            session.Undo();
            var padShape = SaveFile.Load(path);
            var padKeys = padShape.GamepadProfiles.First(p => p.ProfileId == custom).Fields.First(n => n.Name == "Keys");
            var padDuplicate = padKeys.Items.First(i => SaveFile.Field(i, "MappingName") == Guard).Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(padDuplicate, "Key", "Gamepad_RightShoulder"); padKeys.Items.Add(padDuplicate); padShape.Save(path);
            session = EditorSession.Open(path); session.SwitchProfile(custom);
            int guardSlot = session.BindingRecords(true, families[0].Index).First(r => r.Action == Guard).SlotIndex;
            session.SetGamepadFamily(Guard, guardSlot, families[0].Index, "None");
            Assert(session.BindingRecords(true, families[0].Index).Where(r => r.Action == Guard && r.SlotIndex == guardSlot).All(r => r.Key == "None"),
                "Editing a controller slot must update all duplicate sources within that family.");
            session.Undo();

            const string hold = "IA_ShadowArm_LongPress";
            Assert(session.Relations(hold, false, -1).Keys.SequenceEqual(new[] { "LeftControl" }), "Fixed Ctrl Hold must be in effective bindings.");
            bool fixedRejected = false;
            try { session.ChangeRelationship(hold, "LeftControl", false, false, -1); } catch (InvalidOperationException) { fixedRejected = true; }
            Assert(fixedRejected && !session.HasChanges, "Removing fixed Ctrl Hold must be rejected without a draft.");
            fixedRejected = false;
            try { session.ReplaceRelationship(hold, "LeftControl", "G", false, -1); } catch (InvalidOperationException) { fixedRejected = true; }
            Assert(fixedRejected && !session.HasChanges, "Replacing fixed Ctrl Hold must be rejected without a draft.");
            session.SetKeyboard(hold, 0, "None");
            Assert(session.Relations(hold, false, -1).Keys.Contains("LeftControl"), "An explicit unbound editable slot must not cancel fixed Ctrl Hold.");
            session.Undo();

            string missingPath = Path.Combine(root, "missing-pad.sav");
            var missingSave = SaveFile.FromBytes(fixture);
            foreach (var profile in missingSave.GamepadProfiles)
                profile.Keys.Items.RemoveAll(i => SaveFile.Field(i, "MappingName") == Guard);
            missingSave.Save(missingPath);
            var missing = EditorSession.Open(missingPath); missing.SwitchProfile(custom);
            int missingFamily = missing.ControllerFamilies[0].Index;
            Assert(missing.Relations(Guard, true, missingFamily).Keys.SequenceEqual(new[] { "Gamepad_LeftShoulder" }), "Missing controller records must inherit resource slots.");
            missing.ChangeRelationship(Guard, "Gamepad_LeftShoulder", false, true, missingFamily);
            Assert(missing.Relations(Guard, true, missingFamily).Keys.Length == 0, "Removing an inherited controller relation must unbind every registered source.");
            Apply(missing);
            missing = EditorSession.Open(missingPath); missing.SwitchProfile(custom);
            Assert(missing.Relations(Guard, true, missingFamily).Keys.Length == 0, "Inherited controller unbinding must survive saving and reopening.");
            missing.RestoreAction(Guard, true, missingFamily);
            missing.ReplaceRelationship(Guard, "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left", true, missingFamily);
            Assert(missing.Relations(Guard, true, missingFamily).Keys.SequenceEqual(new[] { "Gamepad_FaceButton_Left" }), "Replacing an inherited controller relation must not leave its original key.");

            byte[] beforeUi = File.ReadAllBytes(path);
            using (var form = new MainForm(new EditorPreferences(Path.Combine(root, "preferences.txt"))))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.UiSmoke(path); form.Show(); Application.DoEvents();
                form.SelectProfile(custom); Field<ComboBox>(form, "cbLang").SelectedIndex = 0;
                Assert(!form.ActionView, "Key view should be the default.");
                Field<PresetModeSelector>(form, "btnPresetMode").Controls.OfType<Button>().Single(option => option.Name == "modeReplace").PerformClick();
                Field<ComboBox>(form, "cbPreset").SelectedItem = Field<ComboBox>(form, "cbPreset").Items.OfType<Preset>().Single(p => p.Id == "mouse_guard");
                Assert(form.Session.PresetMode == PresetApplyMode.Replace, "Binding-view setup must replace its existing custom bindings.");
                int count = form.Session.ScopeChangeCount;
                Field<EditorTab>(form, "tabActions").PerformClick();
                Assert(form.ActionView && form.Session.ScopeChangeCount == count, "View switch changed the draft.");
                Assert(!Field<Control>(form, "keyBindings").Visible && Field<Panel>(form, "kbHost").Visible, "Action table not visible exclusively.");
                var button = (BindingButton)((Array)Field<IDictionary>(form, "kbButtons")["IA_OpenMapMenu"]).GetValue(0);
                Assert(button.DisplayText == "M", "Each inherited slot must show its own extracted default without a repeated prefix.");
                form.Session.ChangeRelationship(Guard, "G", true, false, -1); form.RenderSession();
                Field<EditorTab>(form, "tabKeys").PerformClick();
                Assert(form.Session.Relations(Guard, false, -1).Keys.Contains("G"), "Action draft missing after key navigation.");
                var keyView = Field<Control>(form, "keyBindings");
                var keyboardDiagram = Descendants(keyView).Single(c => c.GetType().Name == "DeviceDiagram");
                UnobstructedDiagram(form);
                var targets = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(keyboardDiagram, "targets");
                Assert(targets.ContainsKey("LeftMouseButton") && targets.ContainsKey("LeftControl"), "Keyboard and mouse must be clickable on the same canvas.");
                var keyboardImage = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(keyboardDiagram, "imageBounds")["keyboard"];
                foreach (var physical in new[] {
                    new { Key = "Home", X = 1321.5f, Y = 191f }, new { Key = "End", X = 1321.5f, Y = 262f },
                    new { Key = "Up", X = 1321.5f, Y = 403f }, new { Key = "Down", X = 1321.5f, Y = 475.5f } })
                {
                    var hit = targets[physical.Key];
                    float x = (hit.Left + hit.Width / 2 - keyboardImage.Left) / keyboardImage.Width * 1497;
                    float y = (hit.Top + hit.Height / 2 - keyboardImage.Top) / keyboardImage.Height * 572;
                    Assert(Math.Abs(x - physical.X) < 1 && Math.Abs(y - physical.Y) < 1, "Navigation hit target must match the actual keycap center: " + physical.Key);
                }
                foreach (string key in new[] { "LeftControl", "LeftMouseButton", "RightMouseButton", "MiddleMouseButton", "ThumbMouseButton", "ThumbMouseButton2", "LeftMouseButton" })
                {
                    keyboardDiagram = Descendants(keyView).Single(c => c.GetType().Name == "DeviceDiagram");
                    var position = (PointF)keyboardDiagram.GetType().GetMethod("Position", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(keyboardDiagram, new object[] { key });
                    keyboardDiagram.GetType().GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboardDiagram,
                        new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)position.X, (int)position.Y, 0) });
                    if (Field<string>(keyView, "selectedKey") != key) Shot(form, Path.Combine(root, "hit-failure.png"));
                    Assert(Field<string>(keyView, "selectedKey") == key, "Physical hotspot did not select " + key + "; selected " + Field<string>(keyView, "selectedKey") + "; point " + position);
                }
                SelectKey(form, "Up"); Shot(form, Path.Combine(root, "navigation-alignment.png"));
                SelectKey(form, "LeftMouseButton");
                var schemeDiff = Field<Control>(form, "schemeDiff");
                Assert(schemeDiff.Width >= keyView.Width - 2, "Scheme diff must span both columns.");
                Assert(schemeDiff.Parent == Field<Panel>(form, "bindingWorkspace"), "Whole-scheme diff must be outside both columns.");
                Assert(!Descendants(keyView).OfType<EditorTab>().Any(), "Key canvas must not require key filters or device subtabs.");
                schemeDiff.GetType().GetMethod("SetComparison").Invoke(schemeDiff, new object[] { true });
                Assert((bool)schemeDiff.GetType().GetProperty("CompareFactory").GetValue(schemeDiff, null), "Factory comparison did not switch.");
                schemeDiff.GetType().GetMethod("SetComparison").Invoke(schemeDiff, new object[] { false });
                var regular = form.Size;
                int languageCount = args.Contains("--all-languages") ? I18n.Languages.Length : 1;
                for (int lang = 0; lang < languageCount; lang++)
                {
                    capturedLanguages++;
                    Field<ComboBox>(form, "cbLang").SelectedIndex = lang;
                    form.Size = regular; form.ShowActionView(false); Application.DoEvents();
                    Inside(form, Field<Control>(form, "keyBindings")); Inside(form, Field<Control>(form, "schemeDiff"));
                    Shot(form, Path.Combine(root, "keys-" + I18n.Languages[lang] + ".png"));
                    form.ShowActionView(true); Shot(form, Path.Combine(root, "actions-" + I18n.Languages[lang] + ".png"));
                    form.Size = form.MinimumSize; form.ShowActionView(false); Application.DoEvents();
                    UnobstructedDiagram(form);
                    Inside(form, Field<Control>(form, "keyBindings")); Inside(form, Field<Button>(form, "btnApply"));
                    var minimumDiagram = Descendants(keyView).Single(c => c.GetType().Name == "DeviceDiagram");
                    var images = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(minimumDiagram, "imageBounds");
                    Assert(images.Values.All(r => r.Right <= minimumDiagram.Width + 1), "Device images must fit the canvas width.");
                    Assert(images["keyboard"].Width >= minimumDiagram.ClientSize.Width * .70f, "Minimum keyboard must use available width to keep key labels readable.");
                    Inside(form, schemeDiff);
                    Shot(form, Path.Combine(root, "minimum-" + I18n.Languages[lang] + ".png"));
                }
                form.Size = regular; Field<ComboBox>(form, "cbLang").SelectedIndex = 0; form.ShowGamepad(true);
                var familyCombo = Field<ComboBox>(form, "cbFamily"); familyCombo.SelectedIndex = 1;
                Assert(form.SelectedFamilyIndex == families[1].Index, "UI family selector chose the wrong record.");
                UnobstructedDiagram(form);
                Shot(form, Path.Combine(root, "gamepad-keys.png"));
                form.ShowActionView(true); Shot(form, Path.Combine(root, "gamepad-actions.png"));
                Assert(beforeUi.SequenceEqual(File.ReadAllBytes(path)), "Navigation or preview wrote the input save.");
                Assert(File.ReadAllBytes(shared).SequenceEqual(new byte[] { 1 }), "View changes wrote scheme activation.");
                form.Session.Undo(); form.RenderSession();
            }
            File.WriteAllBytes(path, fixture);
            using (var form = new MainForm(new EditorPreferences(Path.Combine(root, "preferences.txt"))))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.UiSmoke(path); form.Show();
                Field<ComboBox>(form, "cbLang").SelectedIndex = 0; form.SelectProfile(custom); form.ShowActionView(false);
                var demo = form.Session;
                foreach (string action in demo.BindingActions(false, -1)) demo.RestoreAction(action, false, -1);
                foreach (var family in demo.ControllerFamilies)
                    foreach (string action in demo.BindingActions(true, family.Index)) demo.RestoreAction(action, true, family.Index);
                Apply(demo);
                var gray = Color.FromArgb(128, 143, 153); var blue = Color.FromArgb(64, 158, 255); var yellow = Color.FromArgb(240, 178, 78);
                Badge(form, "LeftMouseButton", 1, gray);
                demo.ChangeRelationship(Guard, "LeftMouseButton", true, false, -1); Badge(form, "LeftMouseButton", 2, yellow);
                demo.Undo(); Badge(form, "LeftMouseButton", 1, gray);
                demo.ChangeRelationship(Guard, "LeftMouseButton", true, false, -1); Apply(demo); Badge(form, "LeftMouseButton", 2, blue);
                demo.RestoreKey("LeftMouseButton", false, -1); Badge(form, "LeftMouseButton", 1, yellow);
                Apply(demo); Badge(form, "LeftMouseButton", 1, gray);
                demo.ChangeRelationship("IA_HeavyAttack", "F", false, false, -1); Badge(form, "F", 0, yellow);
                Apply(demo); Badge(form, "F", 0, blue);
                demo.RestoreKey("F", false, -1); Badge(form, "F", 1, yellow); Apply(demo); Badge(form, "F", 1, gray);
                demo.ChangeRelationship(Guard, "LeftMouseButton", true, false, -1); Apply(demo);
                demo.ChangeRelationship("IA_HeavyAttack", "RightMouseButton", true, false, -1);
                int familyIndex = demo.ControllerFamilies[0].Index;
                string oldJump = demo.Relations("IA_Jump", true, familyIndex).Keys.First();
                string newJump = oldJump == "Gamepad_FaceButton_Right" ? "Gamepad_FaceButton_Top" : "Gamepad_FaceButton_Right";
                demo.ReplaceRelationship("IA_Jump", oldJump, newJump, true, familyIndex);
                Badge(form, "LeftMouseButton", 2, blue); Badge(form, "RightMouseButton", 2, yellow); Badge(form, "F", 1, gray);
                var changes = demo.SchemeDifferences(false);
                Assert(changes.Length == 2 && changes.Any(d => d.Gamepad) && changes.Any(d => !d.Gamepad), "Saved comparison must include both devices.");
                Assert(changes.Sum(d => d.AddedKeys.Length) == 2 && changes.Sum(d => d.RemovedKeys.Length) == 1, "Whole-scheme relationship totals are incorrect.");
                SelectKey(form, "RightMouseButton"); Shot(form, Path.Combine(root, "overview-pending.png"));
                var diff = Field<Control>(form, "schemeDiff"); diff.GetType().GetMethod("SetComparison").Invoke(diff, new object[] { true });
                Assert(demo.SchemeDifferences(true).Any(d => d.Action == Guard), "Factory comparison must include saved left mouse customization.");
                Shot(form, Path.Combine(root, "overview-factory.png"));
                diff.GetType().GetMethod("SetComparison").Invoke(diff, new object[] { false });
                form.Size = form.MinimumSize; Shot(form, Path.Combine(root, "overview-minimum.png"));
                form.ShowGamepad(true); form.RenderSession();
                Badge(form, "Gamepad_LeftTrigger", 1, gray);
                Badge(form, "Gamepad_RightTrigger", 2, gray);
                SelectKey(form, "Gamepad_RightTrigger");
                var triggerRows = Descendants(Field<Control>(form, "keyBindings")).Where(c => c.GetType().Name == "RelationshipButton").ToArray();
                Assert(triggerRows.Length == 2 && triggerRows.Any(c => c.Text == I18n.Str("act.IA_ShadowArm"))
                    && triggerRows.Any(c => c.Text == I18n.Str("act.IA_ShadowArm_LongPress")), "Physical RT must include axis Shadow Arm and digital Hold actions.");
                Assert(!Field<IDictionary>(Diagram(form), "targets").Contains("Gamepad_RightTriggerAxis"), "Axis input must not create a duplicate physical keycap.");
                var badgeBounds = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(Diagram(form), "badges");
                var keyBounds = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(Diagram(form), "targets");
                foreach (string prefix in new[] { "Gamepad_FaceButton_", "Gamepad_DPad_" })
                {
                    string top = prefix + (prefix.Contains("Face") ? "Top" : "Up"), bottom = prefix + (prefix.Contains("Face") ? "Bottom" : "Down");
                    Assert(badgeBounds[top].Top < keyBounds[top].Top && badgeBounds[bottom].Bottom > keyBounds[bottom].Bottom,
                        "Top and bottom cluster badges must spread outward.");
                    Assert(badgeBounds[prefix + "Left"].Left < keyBounds[prefix + "Left"].Left
                        && badgeBounds[prefix + "Right"].Right > keyBounds[prefix + "Right"].Right, "Left and right cluster badges must spread outward.");
                }
                Size minimumControllerSize = form.Size;
                form.Size = new Size(1720, 1280);
                Shot(form, Path.Combine(root, "controller-badges.png"));
                form.Size = minimumControllerSize; form.RenderSession();
                Assert(demo.SchemeDifferences(false).Length == 2, "Switching device lost whole-scheme differences.");
                var padDiagram = Diagram(form);
                var padImages = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(padDiagram, "imageBounds");
                Assert(padImages.Values.Single().Width >= 320, "Minimum window controller must retain a readable physical size.");
                foreach (string padKey in new[] { "Gamepad_RightTrigger", "Gamepad_RightShoulder", "Gamepad_FaceButton_Right", "Gamepad_DPad_Down" })
                {
                    var position = (PointF)padDiagram.GetType().GetMethod("Position", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(padDiagram, new object[] { padKey });
                    var scroll = (ScrollableControl)padDiagram;
                    scroll.AutoScrollPosition = new Point(0, Math.Max(0, (int)position.Y - padDiagram.Height / 2 - scroll.AutoScrollPosition.Y));
                    padDiagram.PerformLayout(); var previousScroll = scroll.AutoScrollPosition;
                    var clickPosition = (PointF)padDiagram.GetType().GetMethod("Position", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(padDiagram, new object[] { padKey });
                    Assert(padDiagram.ClientRectangle.Contains(Point.Round(clickPosition)), "Controller key must scroll into viewport before clicking: " + padKey + " " + clickPosition);
                    SelectKey(form, padKey); padDiagram = Diagram(form);
                    Assert(((ScrollableControl)padDiagram).AutoScrollPosition == previousScroll, "Selecting a controller key must preserve the diagram's scroll position.");
                }
                Shot(form, Path.Combine(root, "overview-controller.png"));
                form.ShowGamepad(false); demo.Undo();
                string[] extraKeys = Enumerable.Range(0, 10).Select(n => "NumPad" + n).Concat(new[] { "Add", "Subtract", "Multiply", "Divide", "Decimal", "NumLock" }).ToArray();
                string[] actions = demo.BindingActions(false, -1);
                for (int i = 0; i < extraKeys.Length; i++) demo.SetKeyboard(actions[i / 3], i % 3, extraKeys[i]);
                form.RenderSession(); var extraDiagram = Diagram(form);
                var extraTargets = Field<System.Collections.Generic.Dictionary<string, RectangleF>>(extraDiagram, "targets");
                Assert(extraKeys.All(k => extraTargets.ContainsKey(k) && extraTargets[k].Right <= extraDiagram.ClientSize.Width), "Keys outside the artwork must stay reachable within the canvas width.");
                Assert(extraKeys.Select(k => extraTargets[k].Top).Distinct().Count() > 1, "Extra keycaps must wrap instead of overflowing horizontally.");
                var scroller = (ScrollableControl)extraDiagram;
                scroller.AutoScrollPosition = new Point(0, (int)extraTargets[extraKeys.Last()].Bottom - scroller.AutoScrollPosition.Y);
                extraDiagram.PerformLayout(); Application.DoEvents();
                var lastPosition = (PointF)extraDiagram.GetType().GetMethod("Position", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(extraDiagram, new object[] { extraKeys.Last() });
                if (!extraDiagram.ClientRectangle.Contains(Point.Round(lastPosition))) Shot(form, Path.Combine(root, "extra-scroll-failure.png"));
                Assert(extraDiagram.ClientRectangle.Contains(Point.Round(lastPosition)), "Wrapped keys must be scrollable into the actual clickable viewport: " + lastPosition
                    + "; client " + extraDiagram.ClientRectangle + "; scroll " + scroller.AutoScrollPosition + "; extent " + scroller.AutoScrollMinSize);
                SelectKey(form, extraKeys.Last()); Shot(form, Path.Combine(root, "extra-keycaps.png"));
                demo.Undo(); form.RenderSession();
                demo.RestoreAction(Guard, false, -1); Apply(demo);
                demo.SetKeyboard(Guard, 0, "LeftMouseButton"); form.RenderSession(); SelectKey(form, "RightMouseButton");
                Assert(Descendants(Field<Control>(form, "keyBindings")).Any(c => c.GetType().Name == "RelationshipButton" && c.Text == I18n.Str("act." + Guard)),
                    "Right mouse must retain guard from its inherited slot.");
                Apply(demo); form.RenderSession();
                Badge(form, "LeftMouseButton", 2, blue);
                var leftState = Field<IDictionary>(Diagram(form), "states")["LeftMouseButton"];
                Assert((string)leftState.GetType().GetProperty("Number").GetValue(leftState, null) == "2", "Known left mouse additions must keep an exact count despite other inherited slots.");
                SelectKey(form, "LeftMouseButton");
                var guardRelation = Descendants(Field<Control>(form, "keyBindings")).Single(c => c.GetType().Name == "RelationshipButton" && c.Text == I18n.Str("act." + Guard));
                Assert((bool)guardRelation.GetType().GetField("Custom").GetValue(guardRelation)
                    && !(bool)guardRelation.GetType().GetField("Unknown").GetValue(guardRelation), "Saved left mouse guard must be a confirmed customization.");
                diff.GetType().GetMethod("SetComparison").Invoke(diff, new object[] { true });
                Assert(demo.SchemeDifferences(true).Any(d => d.Action == Guard && d.AddedKeys.Contains("LeftMouseButton")), "Factory diff must retain confirmed additions from partial overrides.");
                Shot(form, Path.Combine(root, "saved-partial-guard.png"));
                diff.GetType().GetMethod("SetComparison").Invoke(diff, new object[] { false });
                demo.SetKeyboard(Guard, 1, "J"); Badge(form, "J", 1, yellow);
                Assert(demo.SchemeDifferences(false).Single(d => d.Action == Guard).AddedKeys.SequenceEqual(new[] { "J" }),
                    "Partial saved baselines must count a confirmed nonfactory addition.");
                demo.ChangeRelationship(Guard, "J", false, false, -1);
                Assert(!demo.RelationshipPending(Guard, "J", false, -1), "Removing an unsaved confirmed addition must clear its pending relationship.");
                demo.Undo(); demo.RestoreKey("LeftMouseButton", false, -1); Badge(form, "LeftMouseButton", 1, yellow);
                Assert(demo.SchemeDifferences(false).Single(d => d.Action == Guard).RemovedKeys.Contains("LeftMouseButton"),
                    "Partial restoration must count the removed explicit left mouse relationship.");
                var removedGuard = Descendants(Field<Control>(form, "keyBindings")).Single(c => c.GetType().Name == "RelationshipButton" && c.Text == I18n.Str("act." + Guard));
                Assert((bool)removedGuard.GetType().GetField("Removed").GetValue(removedGuard)
                    && (bool)removedGuard.GetType().GetField("Pending").GetValue(removedGuard), "Confirmed partial removal must remain visible as removed and pending.");
                demo.Undo(); form.RenderSession();
                var total = Field<Label>(diff, "total");
                Assert(total.Text == "+0 / −0", "Unchanged partial saves must report zero pending relationships.");
                demo.SetKeyboard(Guard, 1, "None"); demo.SetKeyboard(Guard, 2, "None"); form.RenderSession();
                Assert(total.Text == "+0 / −1", "Unbinding the inherited right mouse slot must show an exact removal.");
                demo.Undo(); form.RenderSession();
            }
            Assert(fixture.SequenceEqual(File.ReadAllBytes(args[0])), "Original fixture modified.");
            Console.WriteLine("BindingViewsTests PASS: " + checks + " checks; " + capturedLanguages + " screenshot languages; artifacts " + root); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
