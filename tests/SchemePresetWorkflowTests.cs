using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBZKeyForge;

class SchemePresetWorkflowTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Hidden).SetValue(owner, value); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static KeyEnhancementConfiguration Configuration(MainForm form) { return (KeyEnhancementConfiguration)Field(form, "keyEnhancement"); }
    static void Stage(MainForm form, string id)
    { Call(form, "StageBindingPreset", id == null ? null : Presets.All().Single(item => item.Id == id), PresetApplyMode.Replace); }
    static void CheckEnded(MainForm form)
    {
        Check(((IDictionary)Field(form, "schemePresetSelections")).Count == 0, "applied scheme retained its preset selection");
        Check(((IDictionary)Field(form, "schemePresetPreviews")).Count == 0 && Field(form, "nativePresetPreview") == null,
            "successful apply retained a preset preview checkpoint");
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Control.CheckForIllegalCrossThreadCalls = true;
        int result = 1;
        EventHandler run = null;
        run = async delegate {
            Application.Idle -= run;
            try { await Workflow(Path.GetFullPath(args[0])); Console.WriteLine("SchemePresetWorkflowTests: " + checks + " checks passed"); result = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            Application.ExitThread();
        };
        Application.Idle += run; Application.Run(); return result;
    }

    static async Task Workflow(string output)
    {
        Check(!EditorSession.IsGameRunning, "Close the game before running isolated installation tests");
        string root = Path.Combine(output, "installation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); I18n.Current = "zh-Hans";
        using (var form = new MainForm(new EditorPreferences(Path.Combine(root, "offline-preferences.txt"))))
        {
            form.UiOffline();
            byte[] native = form.Session.ExportCurrentScheme();
            string before = Configuration(form).ToJson();
            Stage(form, "onekey"); Stage(form, "onekey_dodge"); Stage(form, null);
            Check(form.Session.ExportCurrentScheme().SequenceEqual(native) && Configuration(form).ToJson() == before,
                "canceling an ordinary preset preview changed its original draft");
            var incoming = UserPresetStore.Capture(form.Session, "首次增强", new KeyEnhancementConfiguration { Enabled = true, RuntimeHotkeys = true });
            incoming.Id = "user_first_enhanced";
            Call(form, "StageBindingPreset", incoming, PresetApplyMode.Replace);
            Check(Configuration(form).Schemes.Count == 3 && Configuration(form).RuntimeHotkeys, "first preset did not enable enhancement schemes and hotkeys");
            Stage(form, null);
            Check(Configuration(form).ToJson() == before && form.Session.ExportCurrentScheme().SequenceEqual(native),
                "canceling the first enhanced preset did not restore ordinary mode");
        }
        AuthenticatedSchemeFixture.Create(root, "DefaultPreset");
        string input = Path.Combine(root, @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
        File.WriteAllBytes(input, EditorSession.CreateOffline().ExportCurrentScheme());
        byte[] inputBytes = File.ReadAllBytes(input), sharedBytes = File.ReadAllBytes(AuthenticatedSchemeFixture.Shared(root));
        var baseline = new KeyEnhancementConfiguration { Enabled = true };
        baseline.EnsureSchemes(EditorSession.Open(input));
        var install = new KeyEnhancementInstallation(root); install.LoadConfiguration(); install.Apply(baseline);
        string schemeState = Path.Combine(install.InstallDirectory, "scheme-state.txt");
        string main = Path.Combine(install.InstallDirectory, @"Scripts\main.lua");
        var scriptTime = File.GetLastWriteTimeUtc(main);
        File.WriteAllText(schemeState, baseline.Schemes[1].Id);
        install.Apply(baseline);
        Check(File.ReadAllText(schemeState) == baseline.ActiveSchemeId, "unchanged apply did not restore the requested scheme after a runtime switch");
        Check(File.GetLastWriteTimeUtc(main) == scriptTime, "scheme-state-only apply redeployed runtime files");
        File.Delete(schemeState); install.Apply(baseline);
        Check(File.ReadAllText(schemeState) == baseline.ActiveSchemeId, "unchanged apply did not recreate missing scheme state");

        using (var form = new MainForm(new EditorPreferences(Path.Combine(root, "preferences.txt"))))
        {
            form.UiSmoke(input); form.Show();
            Set(form, "flameEffectsEnabled", false);
            ((Timer)Field(form, "gameTimer")).Stop();
            string before = Configuration(form).ToJson();
            string first = Configuration(form).SelectedSchemeId, second = Configuration(form).Schemes[1].Id;
            Stage(form, "onekey"); form.SelectProfile(second); Stage(form, "onekey_dodge");
            string secondPreview = Configuration(form).ConfigurationFor(second).ToJson();
            form.SelectProfile(first); Stage(form, null);
            Check(Configuration(form).ConfigurationFor(first).ToJson() == baseline.ConfigurationFor(first).ToJson(), "canceling A after previewing B retained A's preset bindings");
            Check(Configuration(form).ConfigurationFor(second).ToJson() == secondPreview, "canceling A changed B's preview");
            form.SelectProfile(second); Stage(form, null); form.SelectProfile(first);
            Check(Configuration(form).ToJson() == before, "canceling both scheme previews did not restore their original drafts");

            Stage(form, "onekey"); form.SelectProfile(second); Stage(form, "onekey_dodge");
            secondPreview = Configuration(form).ConfigurationFor(second).ToJson();
            form.SelectProfile(first); Stage(form, "onekey_dodge"); Stage(form, null);
            Check(Configuration(form).ConfigurationFor(first).ToJson() == baseline.ConfigurationFor(first).ToJson()
                && Configuration(form).ConfigurationFor(second).ToJson() == secondPreview,
                "replacing A's preview after visiting B lost its original checkpoint or B's edits");
            form.SelectProfile(second); Stage(form, null); form.SelectProfile(first);

            var shared = UserPresetStore.Capture(form.Session, "shared preview", baseline.CurrentOnly());
            shared.Id = "user_shared_preview"; shared.KeyEnhancement.RuntimeHotkeys = true; shared.KeyEnhancement.CustomLua = "-- preset A";
            Call(form, "StageBindingPreset", shared, PresetApplyMode.Replace);
            Check(Configuration(form).RuntimeHotkeys, "preset preview lost runtime hotkeys");
            form.SelectProfile(second); Configuration(form).CustomLua = "-- later B edit";
            form.SelectProfile(first); Stage(form, null);
            Check(!Configuration(form).RuntimeHotkeys && Configuration(form).CustomLua == "-- later B edit",
                "cancel did not restore unmodified shared settings or overwrote later shared edits");
            Configuration(form).CustomLua = baseline.CustomLua;
            Stage(form, "onekey");
            new BindingWorkspace(form.Session, Configuration(form), baseline, false, -1).Add("IA_Jump", new BindingEntry { Keys = new[] { "F9" } });
            string edited = Configuration(form).ToJson(); Stage(form, null);
            Check(Configuration(form).ToJson() == edited, "canceling an edited preview discarded manual bindings");
            Set(form, "keyEnhancement", baseline.Clone());

            Stage(form, "onekey");
            Check(Configuration(form).NativeBindings.Any(item => item.FunctionId == "IA_Guard" && item.Device == "keyboard" && item.Keys.Contains("LeftMouseButton")),
                "preset preview did not add mouse guard");
            Stage(form, null);
            Check(Configuration(form).ToJson() == before, "cancel before apply did not restore the original scheme");

            Stage(form, "onekey");
            string preview = Configuration(form).ToJson();
            string installed = File.ReadAllText(install.ConfigurationPath);
            File.WriteAllText(install.ConfigurationPath, installed + " ");
            bool applied = await (Task<bool>)Call(form, "ApplyChangesAsync");
            ((Timer)Field(form, "gameTimer")).Stop();
            Check(!applied && Configuration(form).ToJson() == preview && ((IDictionary)Field(form, "schemePresetPreviews")).Count > 0,
                "failed apply ended or changed the preset preview");
            Stage(form, null);
            Check(Configuration(form).ToJson() == before, "failed preset application could not be canceled");

            File.WriteAllText(install.ConfigurationPath, installed);
            Stage(form, "onekey");
            applied = await (Task<bool>)Call(form, "ApplyChangesAsync");
            ((Timer)Field(form, "gameTimer")).Stop();
            Check(applied, "preset retry did not apply successfully: " + Field(form, "fullFeedback"));
            CheckEnded(form);
            string accepted = Configuration(form).ToJson();
            Check(KeyEnhancementConfiguration.FromJson(File.ReadAllText(install.ConfigurationPath)).ToJson() == accepted,
                "successful apply did not persist the enhanced preset");
            Stage(form, null);
            Check(Configuration(form).ToJson() == accepted, "clearing selection reverted an installed preset");
            Stage(form, "onekey_dodge"); Stage(form, null);
            Check(Configuration(form).ToJson() == accepted, "later canceled preview reverted the accepted scheme");

            Stage(form, "onekey_dodge");
            var candidate = BindingLivePlan.Create(form.Session, Configuration(form), form.Session.ControllerFamilies.First().Index);
            Call(form, "AcceptLiveBindings", candidate, true, form.Session.ControllerFamilies.First().Index);
            CheckEnded(form);
            string liveAccepted = Configuration(form).ToJson();
            Stage(form, null);
            Check(Configuration(form).ToJson() == liveAccepted, "clearing selection reverted a confirmed live preset");

            var current = Configuration(form); current.SetEnabled(false);
            form.Session.SetKeyboard("IA_Jump", 0, "F11");
            File.WriteAllText(Path.Combine(install.InstallDirectory, "live-status.txt"), "PBZ-LIVE-1\nfixture\nready\n");
            Set(form, "lastRunning", true);
            object state = Call(form, "BindingApplication");
            Check(!(bool)state.GetType().GetField("CanApply").GetValue(state)
                && (string)state.GetType().GetField("Reason").GetValue(state) == I18n.Str("schemes.nativepending"),
                "GUI offered live apply while ordinary drafts would be skipped");
            Check(form.Session.HasAnyDrafts && form.Session.GetKeyboard("IA_Jump", 0) == "F11", "blocked live application lost the ordinary draft");
            Check(File.ReadAllBytes(input).SequenceEqual(inputBytes) && File.ReadAllBytes(AuthenticatedSchemeFixture.Shared(root)).SequenceEqual(sharedBytes),
                "enhanced preset workflow changed native save bytes");
        }
    }
}
