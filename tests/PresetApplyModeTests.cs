using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

public static class PresetApplyModeTests
{
    const string Profile = "PBZ.KeyProfile.Custom1";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static void Assert(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Keys(EditorSession session, string action, bool pad, params string[] expected)
    {
        var actual = session.Relations(action, pad, pad ? session.ControllerFamilies.First().Index : -1).Keys;
        Assert(!actual.Except(expected).Any() && !expected.Except(actual).Any(), action + ": " + string.Join("/", actual) + " != " + string.Join("/", expected));
    }
    static EditorSession Open(string path)
    { var session = EditorSession.Open(path); session.SwitchProfile(Profile); return session; }
    static byte[] OtherSchemes(SaveFile save)
    {
        var copy = SaveFile.FromBytes(save.ToBytes());
        copy.Records.RemoveAll(record => record.ProfileId == Profile);
        copy.Props.First(node => node.Name == "GamepadFamilyProfiles").Items.RemoveAll(fields => SaveFile.Field(fields, "ProfileId") == Profile);
        return copy.ToBytes();
    }
    static void Snapshot(Control control, string path)
    { using (var image = new Bitmap(control.Width, control.Height)) { control.DrawToBitmap(image, control.ClientRectangle); image.Save(path); } }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            byte[] original = File.ReadAllBytes(args[0]);
            string input = Path.Combine(output, "EnhancedInputUserSettings.sav"); File.WriteAllBytes(input, original);
            var custom = Open(input);
            custom.SetKeyboard("IA_HeavyAttack", 0, "G"); custom.SetKeyboard("IA_HeavyAttack", 1, "G"); custom.SetKeyboard("IA_HeavyAttack", 2, "G");
            custom.SetKeyboard("IA_Interact", 0, "H");
            Assert(custom.Apply().Success, "Seed custom bindings");
            byte[] saved = File.ReadAllBytes(input);
            byte[] otherSchemes = OtherSchemes(SaveFile.FromBytes(saved));
            foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
            {
                var direct = SaveFile.FromBytes(saved);
                Presets.Apply(direct, Presets.ById("mouse_guard"), direct.RecordFor(Profile), mode);
                Assert(otherSchemes.SequenceEqual(OtherSchemes(direct)), "Mode preserves other schemes: " + mode);
                byte[] once = direct.ToBytes();
                Presets.Apply(direct, Presets.ById("mouse_guard"), direct.RecordFor(Profile), mode);
                Assert(once.SequenceEqual(direct.ToBytes()), "Mode is idempotent: " + mode);
            }
            var session = Open(input);
            session.SetKeyboard("IA_Jump", 0, "F11");
            foreach (PresetApplyMode mode in Enum.GetValues(typeof(PresetApplyMode)))
            {
                session.SelectPreset("mouse_guard", mode);
                if (mode == PresetApplyMode.Append) Keys(session, "IA_HeavyAttack", false, "G", "F", "RightMouseButton");
                else Keys(session, "IA_HeavyAttack", false, "F", "RightMouseButton");
                Keys(session, "IA_Interact", false, mode == PresetApplyMode.OnlyPreset ? "E" : "H");
                Keys(session, "IA_Jump", false, mode == PresetApplyMode.OnlyPreset ? "SpaceBar" : "F11");
                Keys(session, "IA_Guard", false, "LeftMouseButton", "RightMouseButton");
                Assert(session.PresetMode == mode && saved.SequenceEqual(File.ReadAllBytes(input)), "Mode stages without writing");
            }
            session.SelectPreset("mouse_guard", PresetApplyMode.Append);
            Keys(session, "IA_HeavyAttack", false, "G", "F", "RightMouseButton");
            session.SelectPreset(null); Keys(session, "IA_HeavyAttack", false, "G"); Keys(session, "IA_Jump", false, "F11");
            session.SelectPreset("mouse_guard", PresetApplyMode.OnlyPreset);
            Assert(session.Apply().Success, "Only preset applies");
            var reopened = Open(input); Keys(reopened, "IA_HeavyAttack", false, "F", "RightMouseButton"); Keys(reopened, "IA_Interact", false, "E");
            Assert(otherSchemes.SequenceEqual(OtherSchemes(SaveFile.Load(input))), "Apply preserves other schemes");
            reopened.SelectPreset("mouse_guard", PresetApplyMode.Append);
            Assert(!reopened.HasChanges, "Duplicate keys are a no-op"); reopened.SelectPreset(null);

            // All selected controller families participate; another scheme remains untouched.
            var multi = SaveFile.FromBytes(saved);
            var fields = multi.GamepadProfiles.First(item => item.ProfileId == Profile).Fields.Select(SaveFile.CloneNode).ToList();
            SaveFile.SetField(fields, "Family", "PlayStation");
            multi.Props.First(node => node.Name == "GamepadFamilyProfiles").Items.Add(fields);
            foreach (var item in multi.GamepadKeysFor(Profile).SelectMany(keys => keys.Items).Where(item => SaveFile.Field(item, "MappingName") == "IA_Guard"))
                SaveFile.SetField(item, "Key", "Gamepad_RightShoulder");
            multi.Save(input); session = Open(input);
            session.SelectPreset("onekey", PresetApplyMode.Replace); Keys(session, "IA_Guard", true, "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left");
            Assert(session.ControllerFamilies.All(value => session.Relations("IA_Guard", true, value.Index).Keys.OrderBy(key => key)
                .SequenceEqual(new[] { "Gamepad_LeftShoulder", "Gamepad_FaceButton_Left" }.OrderBy(key => key))), "Replace updates all controller families");
            session.SelectPreset(null);
            byte[] beforePadFailure = multi.ToBytes(); bool padRejected = false;
            try { Presets.Apply(multi, Presets.ById("onekey"), multi.RecordFor(Profile), PresetApplyMode.Append); }
            catch (InvalidOperationException ex) { padRejected = ex.Message.Contains(I18n.Str("editor.gamepad")); }
            Assert(padRejected && beforePadFailure.SequenceEqual(multi.ToBytes()), "Controller capacity failure leaves earlier keyboard plan untouched");
            session.SelectPreset("onekey", PresetApplyMode.OnlyPreset); Keys(session, "IA_Interact", false, "E");
            session.SelectPreset(null); Keys(session, "IA_Guard", true, "Gamepad_RightShoulder");
            // Capacity failure must leave the selected successful preset and draft intact.
            session.SetKeyboard("IA_HeavyAttack", 0, "G"); session.SetKeyboard("IA_HeavyAttack", 1, "H"); session.SetKeyboard("IA_HeavyAttack", 2, "J");
            session.SelectPreset("mouse_guard", PresetApplyMode.Replace);
            string[] before = session.Relations("IA_HeavyAttack", false, -1).Keys;
            int count = session.ScopeChangeCount; bool rejected = false;
            try { session.SelectPreset("mouse_guard", PresetApplyMode.Append); }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains(I18n.Str("act.IA_HeavyAttack")); }
            Assert(rejected, "Full append names the action");
            Assert(session.PresetMode == PresetApplyMode.Replace && session.PresetId == "mouse_guard" && count == session.ScopeChangeCount
                && session.Relations("IA_HeavyAttack", false, -1).Keys.SequenceEqual(before), "Failure preserves draft, preset and mode");
            session.SelectPreset(null); Keys(session, "IA_HeavyAttack", false, "G", "H", "J");
            // Low-level planning is atomic even if an earlier action could have been changed.
            var full = SaveFile.Load(input); var record = full.RecordFor(Profile);
            record.Entries.RemoveAll(entry => entry.Name == "IA_HeavyAttack");
            foreach (var pair in new[] { new { Slot = 0, Key = "G" }, new { Slot = 2, Key = "H" }, new { Slot = 4, Key = "J" } })
                record.Entries.Add(new BlobEntry { Name = "IA_HeavyAttack", Slot = (byte)pair.Slot, Key = pair.Key, X = "None", Y = "None" });
            byte[] fullBytes = full.ToBytes(); rejected = false;
            try { Presets.Apply(full, Presets.ById("mouse_guard"), record, PresetApplyMode.Append); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && fullBytes.SequenceEqual(full.ToBytes()), "Capacity failure is atomic across actions");

            File.WriteAllBytes(input, saved);
            using (var form = new MainForm())
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.UiSmoke(input); form.Show(); Application.DoEvents();
                Field<ComboBox>(form, "cbLang").SelectedIndex = 0;
                form.Session.SwitchProfile(Profile); form.RenderSession();
                var button = Field<PresetApplyButton>(form, "btnPresetMode");
                Assert(button.Mode == PresetApplyMode.Append && button.Visible && button.Enabled, "Compact mode defaults to Append");
                var picker = Field<ComboBox>(form, "cbPreset");
                Assert(button.Parent == picker.Parent, "Mode belongs beside preset selector");
                picker.SelectedItem = picker.Items.Cast<object>().OfType<Preset>().Single(preset => preset.Id == "mouse_guard");
                foreach (var mode in new[] { PresetApplyMode.Replace, PresetApplyMode.OnlyPreset, PresetApplyMode.Append })
                {
                    button.PerformClick(); Application.DoEvents();
                    Assert(button.Mode == mode && form.Session.PresetMode == mode, "Cycle click updates selected mode");
                    Keys(form.Session, "IA_Interact", false, mode == PresetApplyMode.OnlyPreset ? "E" : "H");
                }
                Snapshot(form, Path.Combine(output, "quick-modes-regular.png"));
                form.Size = form.MinimumSize; Application.DoEvents();
                Assert(button.Parent.ClientRectangle.Contains(button.Bounds), "Compact mode fits minimum window");
                Assert(TextRenderer.MeasureText(button.Text, button.Font).Width < button.Width, "Mode label fits");
                Assert(Field<ToolTip>(form, "tip").GetToolTip(button).Contains(I18n.Str("preset.append.help")), "Current mode detail appears on hover");
                Snapshot(form, Path.Combine(output, "quick-modes-minimum.png"));
                form.Session.SelectPreset(null);
                for (int slot = 0; slot < 3; slot++) form.Session.SetKeyboard("IA_HeavyAttack", slot, new[] { "G", "H", "J" }[slot]);
                form.Session.SelectPreset("mouse_guard", PresetApplyMode.Replace); form.RenderSession();
                button.PerformClick(); Assert(button.Mode == PresetApplyMode.OnlyPreset, "Cycle reaches OnlyPreset");
                button.PerformClick();
                Assert(button.Mode == PresetApplyMode.OnlyPreset && form.Session.PresetMode == PresetApplyMode.OnlyPreset,
                    "Failed mode click keeps successful mode");
                Assert(Field<ToolTip>(form, "tip").GetToolTip(button).Contains(I18n.Str("preset.nextmode", I18n.Str("preset.replace"))), "Failed cycle tooltip offers following mode");
                button.PerformClick(); Assert(button.Mode == PresetApplyMode.Replace, "Failed append does not trap cycling");
                picker.SelectedItem = picker.Items.Cast<object>().OfType<Preset>().Single(preset => preset.Id == "vanilla_current");
                Assert(!button.Enabled, "Restore factory is independent of mode");
                var userPreset = new Preset { Id = "user_test", UserName = "我的攻防", BaselineId = 1,
                    Scopes = new System.Collections.Generic.List<PresetScope> { new PresetScope() }, UserMappings = new System.Collections.Generic.List<PresetMapping> {
                    new PresetMapping { Action = "IA_Guard", Added = new[] { "LeftMouseButton" } } } };
                picker.Items.Add(userPreset);
                int headingHeight = SendMessage(picker.Handle, 0x154, new IntPtr(picker.Items.Count - 1), IntPtr.Zero).ToInt32();
                int plainHeight = SendMessage(picker.Handle, 0x154, new IntPtr(3), IntPtr.Zero).ToInt32();
                Assert(headingHeight > plainHeight, "First custom item includes a separate group heading");
                picker.SelectedItem = userPreset;
                Assert(form.Session.PresetId == userPreset.Id && button.Enabled, "Custom quick preset uses the shared application path");
                form.Session.Undo();
            }
            Assert(original.SequenceEqual(File.ReadAllBytes(args[0])), "Source fixture unchanged");
            Console.WriteLine("PresetApplyModeTests PASS: " + checks + " checks; " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
