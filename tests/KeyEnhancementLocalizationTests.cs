using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using PBZKeyForge;

class KeyEnhancementLocalizationTests
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); Console.Error.WriteLine(ex.StackTrace); return 1; }
    }

    static void Run()
    {
        Dictionary<string, object> document;
        using (var stream = typeof(BindingWorkspace).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.action-localization.json"))
        {
            Check(stream != null, "The native localization catalog was not embedded.");
            using (var reader = new StreamReader(stream))
                document = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        }
        var entries = (Dictionary<string, object>)document["entries"];
        Dictionary<string, object> details;
        using (var stream = typeof(BindingWorkspace).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.action-details.json"))
        using (var reader = new StreamReader(stream))
            details = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd())["entries"];
        var translations = (Dictionary<string, Dictionary<string, string>>)typeof(I18n).GetField("T", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Check(KeyEnhancementCatalog.Targets.Length == 334 && entries.Count == 334, "The native catalog must cover all 334 targets.");
        var sources = entries.Values.Select(value => Convert.ToString(((Dictionary<string, object>)value)["source"])).ToArray();
        Check(sources.All(source => source == "native" || source == "native-category" || source == "unresolved"),
            "A localization entry has an unknown source kind.");
        foreach (string language in I18n.Languages)
        {
            I18n.Current = language;
            var session = EditorSession.CreateOffline();
            var config = new KeyEnhancementConfiguration();
            var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
            Check(workspace.Functions.Where(item => item.FixedKeys != null).All(item => item.LabelSource == "semantic"
                && !string.IsNullOrWhiteSpace(item.Label) && !item.Label.StartsWith("unified.")), language + ": directional movement translation missing");
            Check(I18n.Str("ownership.source.fixed") != "ownership.source.fixed", language + ": fixed mapping provenance missing");
            foreach (var target in KeyEnhancementCatalog.Targets)
            {
                var function = workspace.Functions.Single(item => item.Id == target.Id);
                Check(function.GroupLabels.All(label => !string.IsNullOrWhiteSpace(label) && !label.StartsWith("unified.")),
                    language + ": player category has no translation: " + target.Id);
                var entry = (Dictionary<string, object>)entries[target.Id];
                var detail = (Dictionary<string, object>)details[target.Id];
                string source = Convert.ToString(entry["source"]);
                var labels = (Dictionary<string, object>)entry["labels"];
                Check(!string.IsNullOrWhiteSpace(function.Label) && function.Label != target.Name && function.Label != target.Id
                    && !function.Label.Contains("IA_") && !function.Label.Contains("Ability.") && !function.Label.Contains("/Game/"),
                    language + ": technical code displayed as a label for " + target.Id);
                if (target.Name == "IA_KeyboardMoveInput" || target.Name == "IA_JoystickMoveInput")
                    Check(function.Label.StartsWith(I18n.Str("unified.movement." + (target.Name == "IA_KeyboardMoveInput" ? "keyboard" : "joystick")), StringComparison.Ordinal)
                        && function.LabelSource == "semantic", language + ": movement axis lacks its device identity");
                else if (!ActionCatalogLabels.IsSemantic(target.Id))
                {
                    Check(labels.ContainsKey(language) && !string.IsNullOrWhiteSpace(Convert.ToString(labels[language])), language + ": native translation missing for " + target.Id);
                    Check(function.Label == Convert.ToString(labels[language]) && function.LabelSource == source,
                        language + ": native label/source lost for " + target.Id);
                }
                else
                {
                    Check(function.LabelSource == "semantic", language + ": semantic detail presented as an official name: " + target.Id);
                    foreach (string part in ((System.Collections.IEnumerable)detail["parts"]).Cast<object>().Select(Convert.ToString).Where(part => part.StartsWith("@")))
                        Check(translations[language].ContainsKey(part.Substring(1)) && !string.IsNullOrWhiteSpace(translations[language][part.Substring(1)]),
                            language + ": action detail silently falls back to another language: " + part);
                    if (Convert.ToString(detail["prefix"]) == "weapon")
                        Check(function.Label.StartsWith(Convert.ToString(labels[language]).Split(new[] { " · " }, StringSplitOptions.None)[0], StringComparison.Ordinal),
                            language + ": official weapon name lost: " + target.Id);
                    if (Convert.ToString(detail["prefix"]) == "native" && labels.ContainsKey(language))
                        Check(function.Label.StartsWith(Convert.ToString(labels[language]), StringComparison.Ordinal),
                            language + ": native move title lost before its stage detail: " + target.Id);
                }
            }
            Check(workspace.Functions.Where(item => item.Id.StartsWith("ability:")).Select(item => item.Label).Distinct().Count() == 236,
                language + ": ability labels repeat categories instead of identifying each move");
        }
        I18n.Current = "en";
        Check(BindingWorkspace.FunctionLabel("ability:Ability.Action.Hun.SpecialDash") == "FLASH STEP", "SpecialDash lost its native player-facing name.");
        Console.WriteLine("KeyEnhancementLocalizationTests PASS " + assertions + "; 334 targets, 15 languages");
    }
}
