using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using PBZKeyForge;

class BundleImportTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Hidden).SetValue(owner, value); }
    static void Call(MainForm form, string method, string path)
    { try { typeof(MainForm).GetMethod(method, Hidden).Invoke(form, new object[] { path }); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
    static void Write(ZipArchive zip, string name, byte[] bytes)
    { using (var output = zip.CreateEntry(name).Open()) output.Write(bytes, 0, bytes.Length); }
    static byte[] Read(ZipArchive zip, string name)
    { using (var input = zip.GetEntry(name).Open()) using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); } }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args.Length == 0 ? "dist/bundle-import-check" : args[0]); Directory.CreateDirectory(output);
            foreach (var defaults in new[] { InputDefaultSet.Legacy, InputDefaultSet.Current }) RoundTrip(output, defaults);
            Console.WriteLine("BundleImportTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void RoundTrip(string directory, InputDefaultSet defaults)
    {
        string suffix = defaults == InputDefaultSet.Current ? "current" : "legacy";
        var save = SaveFile.FromBytes(File.ReadAllBytes("samples/vanilla-base.sav"), defaults);
        byte[] opaque = { 0x50, 0x42, 0x5A, 0x7F, 0x80, 0xE1, 0xFF, 0x00 };
        save.BlobRest = save.BlobRest.Concat(opaque).ToArray();
        string profile = save.Records.Last().ProfileId;
        var session = EditorSession.ImportOffline(save.ToBytes(), defaults, profile);
        session.SetKeyboard("IA_Jump", 0, "F7");
        foreach (var family in session.ControllerFamilies) session.SetGamepadFamily("IA_Guard", 0, family.Index, "Gamepad_FaceButton_Right");
        byte[] expectedSave = session.ExportCurrentScheme();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        config.CustomFunctions.Add(new KeyEnhancementOutput { Id="unbound-property", Name="状态", Kind="property", Property="bFlag", Value=true });
        var rule = new KeyEnhancementRule { Id = "bundle-combo", Trigger = "hold", Seconds = .4, ConsumeKeys = new System.Collections.Generic.List<string> { "J" }, WhenLua = "function(context) return not context.paused end" };
        rule.Keys.Add("LeftControl"); rule.Keys.Add("J"); rule.Outputs.Add(new KeyEnhancementOutput { Kind = "mapping", MappingName = "IA_Jump", MappingActions = KeyEnhancementCatalog.ForMapping("IA_Jump").Select(target => target.Path).ToArray() }); config.Rules.Add(rule);
        using (var source = new MainForm(new EditorPreferences(Path.Combine(directory, suffix + "-source-preferences.txt"))))
        {
            typeof(MainForm).GetProperty("Session", Hidden).SetValue(source, session, null); Set(source, "keyEnhancement", config);
            string bundle = Path.Combine(directory, suffix + ".zip"); Call(source, "ExportKeyEnhancementBundle", bundle);
            using (var zip = ZipFile.OpenRead(bundle))
            {
                Check(Read(zip, "EnhancedInputUserSettings.sav").SequenceEqual(expectedSave), "Export bundle altered ordinary save bytes: " + suffix);
                Check(Encoding.UTF8.GetString(Read(zip, "scheme.txt")) == profile, "Export omitted selected profile");
                Check(Encoding.UTF8.GetString(Read(zip, "defaults.txt")) == (suffix == "current" ? "CL-302256" : "CL-301524"), "Export omitted default rule set");
            }
            using (var target = new MainForm(new EditorPreferences(Path.Combine(directory, suffix + "-target-preferences.txt"))))
            {
                target.UiOffline(); var previous = target.Session; Call(target, "ImportKeyEnhancementBundle", bundle);
                Check(!object.ReferenceEquals(previous, target.Session) && target.Session.IsOffline, "Import did not create an offline editor session");
                Check(target.Session.SelectedProfile == profile && object.ReferenceEquals(target.Session.Defaults, defaults), "Import changed selected profile or default rules");
                Check(target.Session.GetKeyboard("IA_Jump", 0) == "F7", "Ordinary binding lost on import");
                Check(target.Session.ExportCurrentScheme().SequenceEqual(expectedSave), "Import lost ordinary bindings or unknown save bytes");
                var importedSave = SaveFile.FromBytes(target.Session.ExportCurrentScheme(), defaults);
                Check(importedSave.BlobRest.Skip(importedSave.BlobRest.Length - opaque.Length).SequenceEqual(opaque), "Unknown tail bytes changed");
                Check(Field<KeyEnhancementConfiguration>(target, "keyEnhancement").ToJson() == config.ToJson(), "Complex rules changed on import");
                var workspace = new BindingWorkspace(target.Session, Field<KeyEnhancementConfiguration>(target, "keyEnhancement"), config.Clone(), false, -1);
                Check(workspace.Functions.Any(function => function.Id == "custom:unbound-property") && workspace.Entries("custom:unbound-property").Length == 0,
                    "Bundle import discarded an unbound custom function");
                Check(target.Session.InputPath == null, "Imported bundle was attached to a real game path");
                InvalidBundles(target, directory, suffix, expectedSave);
                target.Session.SetKeyboard("IA_Jump", 0, "F8");
                typeof(MainForm).GetMethod("ResetAllBindings", Hidden).Invoke(target, null);
                Check(Field<KeyEnhancementConfiguration>(target, "keyEnhancement").ToJson() == new KeyEnhancementConfiguration().ToJson(), "Global reset retained imported enhancement rules");
                foreach (string resetProfile in target.Session.Profiles)
                {
                    target.Session.SwitchProfile(resetProfile);
                    Check(target.Session.Relations("IA_Jump", false, -1).Keys.SequenceEqual(new[] { "SpaceBar" }), "Imported jump did not reset to factory: " + suffix + " " + resetProfile);
                    foreach (var family in target.Session.ControllerFamilies)
                        Check(target.Session.Relations("IA_Guard", true, family.Index).Keys.SequenceEqual(new[] { "Gamepad_LeftShoulder" }), "Imported controller guard did not reset to factory");
                }
                var resetSave = SaveFile.FromBytes(target.Session.ExportCurrentScheme(), defaults);
                Check(resetSave.BlobRest.SequenceEqual(save.BlobRest), "Offline reset altered unknown tail data");
                Check(target.Session.IsOffline && object.ReferenceEquals(target.Session.Defaults, defaults) && !target.Session.HasAnyDrafts && !target.Session.IsResetPending,
                    "Offline reset must complete in memory without requiring Apply");
                target.Session.SetKeyboard("IA_Jump", 0, "F9");
                Check(target.Session.Relations("IA_Jump", false, -1).Keys.Contains("F9"), "Offline editing is blocked after factory reset");
                Check(source.Session.ExportCurrentScheme().SequenceEqual(expectedSave), "Reset altered the source session");
            }
        }
    }
    static void InvalidBundles(MainForm target, string directory, string suffix, byte[] save)
    {
        target.Session.SetKeyboard("IA_Jump", 0, "F6");
        byte[] draft = target.Session.ExportCurrentScheme();
        foreach (string failure in new[] { "missing-config", "invalid-config", "invalid-save", "invalid-ability", "invalid-hook" })
        {
            string path = Path.Combine(directory, suffix + "-" + failure + ".zip");
            using (var stream = File.Create(path)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Write(zip, "EnhancedInputUserSettings.sav", failure == "invalid-save" ? new byte[] { 1, 2, 3 } : save);
                var invalid = new KeyEnhancementConfiguration();
                if (failure == "invalid-ability")
                {
                    var rule = new KeyEnhancementRule(); rule.Keys.Add("J"); rule.Outputs.Add(new KeyEnhancementOutput { Kind = "ability" }); invalid.Rules.Add(rule);
                }
                if (failure == "invalid-hook") invalid.Hooks.Add(new System.Collections.Generic.Dictionary<string, object> { { "path", "" } });
                if (failure != "missing-config") Write(zip, "key-enhancement.json", Encoding.UTF8.GetBytes(failure == "invalid-config" ? "{" : invalid.ToJson()));
            }
            var session = target.Session; string config = Field<KeyEnhancementConfiguration>(target, "keyEnhancement").ToJson(); bool failed = false;
            try { Call(target, "ImportKeyEnhancementBundle", path); } catch { failed = true; }
            Check(failed && object.ReferenceEquals(session, target.Session) && config == Field<KeyEnhancementConfiguration>(target, "keyEnhancement").ToJson(), "Invalid bundle replaced current editor state: " + failure);
            Check(target.Session.HasChanges && target.Session.ExportCurrentScheme().SequenceEqual(draft), "Invalid bundle lost the existing save draft: " + failure);
            bool cliFailed = false;
            try { BindingDocument.Open(path); } catch { cliFailed = true; }
            Check(cliFailed, "Document import accepted an invalid bundle: " + failure);
        }
    }
}
