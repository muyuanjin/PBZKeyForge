using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using PBZKeyForge;

class BindingReviewTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Call(object owner, string name, params object[] args)
    { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetProperty(name, Hidden).SetValue(owner, value, null); }
    static void Write(ZipArchive zip, string name, byte[] bytes)
    { using (var stream = zip.CreateEntry(name).Open()) stream.Write(bytes, 0, bytes.Length); }

    [STAThread] static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans"; Application.EnableVisualStyles();
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            PresetOverrides(output); OfflineReset(output); ActivationHistory();
            Console.WriteLine("BindingReviewTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void PresetOverrides(string output)
    {
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preset-preferences.txt"))))
        {
            form.UiOffline();
            var config = new KeyEnhancementConfiguration { Enabled = true };
            config.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new List<string> { "J" } });
            var preset = new Preset { Id = "review", UserName = "Jump", BaselineId = 1,
                Scopes = new List<PresetScope> { new PresetScope() },
                UserMappings = new List<PresetMapping> { new PresetMapping { Action = "IA_Jump", Added = new[] { "K" }, Removed = new[] { "SpaceBar" } } }, KeyEnhancement = config };
            Call(form, "StageBindingPreset", preset, PresetApplyMode.Replace);
            var session = form.Session; Set(session, "ActiveProfile", session.SelectedProfile);
            Check(session.Relations("IA_Jump", false, -1).Keys.SequenceEqual(new[] { "K" }), "Preset did not stage ordinary K");
            var staged = Field<KeyEnhancementConfiguration>(form, "keyEnhancement");
            int family = session.ControllerFamilies.First().Index;
            var plan = BindingLivePlan.Create(session, staged, family);
            Check(plan.NativeBindings.Single(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.SequenceEqual(new[] { "J" }), "Preset runtime override J became K");
            Check(staged.ToJson() == config.ToJson(), "Live plan mutated staged preset");
            var ordinary = BindingLivePlan.Create(session, new KeyEnhancementConfiguration(), family);
            Check(ordinary.NativeBindings.Single(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.SequenceEqual(new[] { "K" }), "Ordinary save edit was not projected into live plan");
            session.SetGamepadFamily("IA_Jump", 0, family, "Gamepad_FaceButton_Left");
            var devices = BindingLivePlan.Create(session, staged, family);
            Check(devices.NativeBindings.Any(item => item.FunctionId == "IA_Jump" && item.Device == "gamepad" && item.Keys.Contains("Gamepad_FaceButton_Left")), "Keyboard override prevented controller projection");
            object presetDraft = Call(session, "CaptureDraft");
            session.SetKeyboard("IA_Jump", 0, "L");
            string json = staged.ToJson(); byte[] save = session.ExportCurrentScheme();
            bool rejected = false;
            try { BindingLivePlan.Create(session, staged, family); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && session.GetKeyboard("IA_Jump", 0) == "L" && session.HasChanges, "Raw edit after preset was silently discarded");
            Check(staged.ToJson() == json && session.ExportCurrentScheme().SequenceEqual(save), "Rejected raw edit changed draft or configuration");
            Call(session, "RestoreDraft", presetDraft);
            Check(BindingLivePlan.Create(session, staged, family).NativeBindings.First(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.Contains("J"), "Undo lost preset override precedence");
            session.SetKeyboard("IA_Jump", 0, "J");
            Check(BindingLivePlan.Create(session, staged, family).NativeBindings.First(item => item.FunctionId == "IA_Jump" && item.Device == "keyboard").Keys.Contains("J"), "Matching raw edit was unnecessarily rejected");
        }
        var rawSession = EditorSession.CreateOffline(); rawSession.SetKeyboard("IA_Jump", 0, "K");
        rawSession = EditorSession.ImportOffline(rawSession.ExportCurrentScheme(), rawSession.Defaults, rawSession.SelectedProfile);
        Set(rawSession, "ActiveProfile", rawSession.SelectedProfile); rawSession.SetKeyboard("IA_Jump", 0, "L");
        var overrideConfig = new KeyEnhancementConfiguration { Enabled = true };
        overrideConfig.NativeBindings.Add(new NativeBindingOverride { FunctionId = "IA_Jump", Device = "keyboard", Keys = new System.Collections.Generic.List<string> { "J" } });
        bool failed = false;
        try { BindingLivePlan.Create(rawSession, overrideConfig, rawSession.ControllerFamilies.First().Index); } catch (InvalidOperationException) { failed = true; }
        Check(failed && rawSession.HasChanges && rawSession.GetKeyboard("IA_Jump", 0) == "L", "Existing J override swallowed raw K-to-L edit");
    }

    static void OfflineReset(string output)
    {
        var session = EditorSession.CreateOffline(); session.SetKeyboard("IA_Jump", 0, "K");
        byte[] original = session.ExportCurrentScheme(); string bundle = Path.Combine(output, "ordinary.zip");
        using (var stream = File.Create(bundle)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Write(zip, "EnhancedInputUserSettings.sav", original);
            Write(zip, "key-enhancement.json", Encoding.UTF8.GetBytes(new KeyEnhancementConfiguration().ToJson()));
            Write(zip, "scheme.txt", Encoding.UTF8.GetBytes(session.SelectedProfile));
            Write(zip, "defaults.txt", Encoding.UTF8.GetBytes("CL-302256"));
        }
        using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "reset-preferences.txt"))))
        {
            form.UiOffline(); Call(form, "ImportKeyEnhancementBundle", bundle); form.Show(); Application.DoEvents();
            Check(form.Session.GetKeyboard("IA_Jump", 0) == "K" && !form.Session.HasChanges, "Import must start clean with custom K");
            Call(form, "TryAction", new Action(delegate { Call(form, "ResetAllBindings"); })); Application.DoEvents();
            Check(form.Session.Relations("IA_Jump", false, -1).Keys.SequenceEqual(new[] { "SpaceBar" }), "Offline reset missed factory key");
            Check(Field<Button>(form, "btnUndo").Visible && Field<Button>(form, "btnUndo").Enabled, "Offline reset disabled undo");
            Check(((System.Collections.ICollection)Field<object>(form, "bindingHistory")).Count == 1, "Offline reset discarded checkpoint");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "offline-reset-undo.png")); }
            Call(form, "TryAction", new Action(delegate { }));
            Check(((System.Collections.ICollection)Field<object>(form, "bindingHistory")).Count == 1, "Unchanged action damaged baseline checkpoint");
            Call(form, "UndoBindingEdit"); form.RenderSession(); Application.DoEvents();
            Check(form.Session.ExportCurrentScheme().SequenceEqual(original), "Undo did not restore exact imported bytes");
            Check(Field<Button>(form, "btnUndo").Visible && !Field<Button>(form, "btnUndo").Enabled, "Exhausted undo must retain its disabled position");
        }
    }

    static void ActivationHistory()
    {
        var session = EditorSession.CreateOffline();
        Set(session, "ActiveProfile", session.Profiles.Last());
        Set(session, "ActivationPending", true);
        object checkpoint = Call(session, "CaptureDraft");
        Set(session, "ActivationPending", false); Call(session, "RestoreDraft", checkpoint);
        Check(session.ActivationPending, "Undo lost activation on the original scheme");
        string other = session.Profiles[1]; session.SwitchProfile(other);
        Call(session, "RestoreDraft", checkpoint);
        Check(session.SelectedProfile == other && !session.ActivationPending, "Undo transferred activation to the browsed scheme");
        session.SwitchProfile(session.Profiles.First());
        Set(session, "ActiveProfile", session.SelectedProfile); Call(session, "RestoreDraft", checkpoint);
        Check(!session.ActivationPending, "Undo restaged an already active scheme");
    }
}
