using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PBZKeyForge;

class GameMoveNameTests
{
    public sealed class Effect
    {
        public string key, kind;
        public Dictionary<string, string> labels;
    }
    public sealed class Entry
    {
        public Dictionary<string, string> labels;
        public string[] labelParts = new string[0];
        public Dictionary<string, string[]> aliases;
        public Effect[] effects = new Effect[0];
    }
    public sealed class Document { public Dictionary<string, Entry> entries; }
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object value, string name)
    { return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }

    static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        Document document;
        using (var stream = typeof(BindingWorkspace).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.game-move-names.json"))
        using (var reader = new StreamReader(stream))
            document = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd());
        Check(document.entries.ContainsKey("ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser")
            && document.entries.ContainsKey("move:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser.Shoot"),
            "Both native and prepared laser entrances must have official move names.");
        var languages = new[] { "zh-Hans", "en", "ja", "ko", "ru", "zh-Hant", "fr", "de", "es-ES", "pt-BR", "it", "th", "vi", "pt-PT", "es-419" };
        var session = EditorSession.CreateOffline();
        foreach (string language in languages)
        {
            I18n.Current = language;
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
            var functions = workspace.Functions.ToDictionary(item => item.Id);
            foreach (var pair in document.entries)
            {
                BindingFunction function;
                Check(functions.TryGetValue(pair.Key, out function), "A native move name points to a missing entrance: " + pair.Key);
                if (pair.Value.labels != null && pair.Value.labels.Count > 0)
                {
                    string label;
                    Check(pair.Value.labels.TryGetValue(language, out label) && !string.IsNullOrWhiteSpace(label), "Official move name missing in " + language + ": " + pair.Key);
                    string expected = string.Join(" · ", new[] { label }.Concat(pair.Value.labelParts.Select(part => I18n.Str(part))));
                    Check(function.Label == expected, "The picker replaced an official game name: " + pair.Key + "/" + language);
                }
                foreach (var effect in pair.Value.effects)
                {
                    string name;
                    Check(effect.labels.TryGetValue(language, out name) && !string.IsNullOrWhiteSpace(name), "An upgrade has no native name: " + effect.key);
                    Check(function.SearchText.Contains(name), "An upgrade cannot be found by its game name: " + effect.key + "/" + language);
                }
                if (pair.Value.aliases != null && pair.Value.aliases.ContainsKey(language))
                    foreach (string alias in pair.Value.aliases[language])
                        Check(function.SearchText.Contains(alias), "A native alias is not searchable: " + alias);
            }
        }
        I18n.Current = "zh-Hans";
        foreach (bool pad in new[] { false, true })
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            const string laser = "ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser";
            var match = workspace.FindFunctions("千目凝视");
            Check(match.Any(item => item.Id == laser), "Thousand-Eye Gaze must find its actual native laser entrance on both devices.");
            Check(match.All(item => !item.Id.Contains("LaserEyes")), "A passive upgrade must not become a fabricated ability entrance.");
            Check(BindingWorkspace.FunctionLabel(laser).Contains("死光"), "The native laser entrance must retain the first unlocked official move name.");
            using (var dialog = new UnifiedActionChoiceDialog(workspace, pad ? "Gamepad_DPad_Down" : "G", null))
            {
                dialog.Show(); Application.DoEvents();
                Field<TextBox>(dialog, "search").Text = "千目凝视"; Application.DoEvents();
                var list = Field<ListBox>(dialog, "functions");
                list.SelectedIndex = Enumerable.Range(0, list.Items.Count).First(index => ((BindingFunction)list.Items[index]).Id == laser);
                Application.DoEvents();
                string details = Field<TextBox>(dialog, "selectionDetails").Text;
                Check(details.Contains("千目凝视") && details.Contains("解锁后随招式生效") && details.Contains("眼球"), "The picker must explain the native upgrade and its eye-spawn effect.");
                Check(details.StartsWith("修行升级：千目凝视"), "The searched upgrade must appear before unrelated upgrade details.");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(directory, pad ? "thousand-eye-pad.png" : "thousand-eye-keyboard.png"));
                }
            }
        }
        Console.WriteLine("GameMoveNameTests PASS " + checks + "; named entrances=" + document.entries.Count + "; languages=15");
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { Application.EnableVisualStyles(); Run(args[0]); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
