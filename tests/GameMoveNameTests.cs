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
    public sealed class Move
    {
        public string id, sourceId;
        public Dictionary<string, string> weaponLabels;
        public string[] labelParts = new string[0];
    }
    public sealed class MoveDocument { public Move[] moves; }
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Call(object owner, string name) { owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null); }
    static T Field<T>(object value, string name)
    { return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }
    static T LoadResource<T>(string resource) where T : class
    {
        using (var stream = typeof(BindingWorkspace).Assembly.GetManifestResourceStream(resource))
        {
            if (stream == null) throw new InvalidDataException(resource);
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<T>(reader.ReadToEnd());
        }
    }
    static Dictionary<string, Move> LoadMoves()
    {
        var moves = new Dictionary<string, Move>(StringComparer.Ordinal);
        foreach (string name in new[] { "complete-moves.json", "weapon-moves.json", "shadow-moves.json" })
            foreach (var move in LoadResource<MoveDocument>("PBZKeyForge.KeyEnhancement." + name).moves)
                moves.Add(move.id, move);
        return moves;
    }
    static string WeaponLabel(Move move, string language)
    {
        string label;
        return move.weaponLabels != null && move.weaponLabels.TryGetValue(language, out label)
            ? label ?? "" : "";
    }
    static string ExpectedPrefix(string id, Entry entry, string language, Dictionary<string, Move> moves, string primary)
    {
        primary = primary ?? "";
        Move move;
        if (moves.TryGetValue(id, out move))
        {
            string own = WeaponLabel(move, language);
            if (!string.IsNullOrEmpty(own)) return own;
        }
        string[] sources = moves.Values.Where(item => item.sourceId == id)
            .Select(item => WeaponLabel(item, language)).Where(label => !string.IsNullOrEmpty(label))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (sources.Length == 1) return sources[0];
        int separator = primary.IndexOf(" · ", StringComparison.Ordinal);
        return separator < 0 ? primary : primary.Substring(0, separator);
    }
    static string QualifyName(string name, string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || name == prefix
            || name.StartsWith(prefix + " · ", StringComparison.Ordinal)) return name;
        return prefix + " · " + name;
    }
    static string AppendSuffix(string name, string suffix)
    {
        return string.IsNullOrEmpty(suffix) || name.EndsWith(" · " + suffix, StringComparison.Ordinal)
            ? name : name + " · " + suffix;
    }
    static bool IsVariant(string value)
    {
        return value != null && value.IndexOf('{') >= 0 && value.IndexOf('}', value.IndexOf('{') + 1) > 0;
    }
    static IEnumerable<string> ExpandVariants(string text)
    {
        int open = text.IndexOf('{'), close = open < 0 ? -1 : text.IndexOf('}', open + 1);
        if (open < 0 || close < 0)
        {
            yield return text;
            yield break;
        }
        string before = text.Substring(0, open), after = text.Substring(close + 1);
        foreach (string option in text.Substring(open + 1, close - open - 1).Split('|'))
            foreach (string expanded in ExpandVariants(before + option + after)) yield return expanded;
    }
    static void AddName(List<string> names, string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
    }
    static string[] CandidateNames(string id, Entry entry, string language, Dictionary<string, Move> moves)
    {
        var names = new List<string>();
        string primary = "";
        bool hasPrimary = entry.labels != null && entry.labels.TryGetValue(language, out primary)
            && !string.IsNullOrWhiteSpace(primary);
        string suffix = string.Join(" · ", (entry.labelParts ?? new string[0]).Select(part => I18n.Str(part)).ToArray());
        string prefix = ExpectedPrefix(id, entry, language, moves, primary);
        if (hasPrimary) AddName(names, AppendSuffix(primary, suffix));
        string[] variants = entry.aliases == null || !entry.aliases.ContainsKey(language)
            ? new string[0] : entry.aliases[language].Where(IsVariant).ToArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in entry.effects)
        {
            string name;
            if (effect.labels == null || !effect.labels.TryGetValue(language, out name)
                || string.IsNullOrWhiteSpace(name)) continue;
            string variant = variants.FirstOrDefault(value => ExpandVariants(value).Contains(name, StringComparer.Ordinal));
            if (variant == null)
            {
                AddName(names, AppendSuffix(QualifyName(name, prefix), suffix));
                continue;
            }
            used.Add(variant);
            foreach (string expanded in ExpandVariants(variant))
                AddName(names, AppendSuffix(QualifyName(expanded, prefix), suffix));
        }
        foreach (string variant in variants.Where(value => !used.Contains(value)))
            foreach (string expanded in ExpandVariants(variant))
                AddName(names, AppendSuffix(QualifyName(expanded, prefix), suffix));
        return names.ToArray();
    }

    static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        Document document = LoadResource<Document>("PBZKeyForge.KeyEnhancement.game-move-names.json");
        var moves = LoadMoves();
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
            var choices = workspace.FunctionChoices.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var pair in document.entries)
            {
                BindingFunction function;
                Check(functions.TryGetValue(pair.Key, out function), "A native move name points to a missing entrance: " + pair.Key);
                string[] candidates = CandidateNames(pair.Key, pair.Value, language, moves);
                BindingFunction[] displayChoices;
                Check(choices.TryGetValue(pair.Key, out displayChoices), "A named move has no display choices: " + pair.Key);
                string[] labels = displayChoices.Select(item => item.Label).ToArray();
                Check(labels.SequenceEqual(candidates, StringComparer.Ordinal),
                    "A move did not expose every original name as its own picker entry: " + pair.Key + "/" + language);
                Check(labels.Distinct(StringComparer.Ordinal).Count() == labels.Length,
                    "A move exposed the same official label more than once: " + pair.Key + "/" + language);
                if (pair.Value.labels != null && pair.Value.labels.Count > 0)
                {
                    string label;
                    Check(pair.Value.labels.TryGetValue(language, out label) && !string.IsNullOrWhiteSpace(label), "Official move name missing in " + language + ": " + pair.Key);
                    Check(function.Label == candidates[0], "The picker replaced an official game name: " + pair.Key + "/" + language);
                }
                foreach (var effect in pair.Value.effects)
                {
                    string name;
                    Check(effect.labels.TryGetValue(language, out name) && !string.IsNullOrWhiteSpace(name), "An upgrade has no native name: " + effect.key);
                    Check(function.SearchText.Contains(name), "An upgrade cannot be found by its game name: " + effect.key + "/" + language);
                }
                if (pair.Value.aliases != null && pair.Value.aliases.ContainsKey(language))
                    foreach (string alias in pair.Value.aliases[language])
                        Check(ExpandVariants(alias).All(value => function.SearchText.Contains(value)),
                            "A native alias is not searchable in its visible form: " + alias);
                if (candidates.Length > 1)
                    foreach (string candidate in candidates)
                        Check(workspace.FindFunctionChoices(candidate).Any(item => item.Id == pair.Key && item.Label == candidate),
                            "A shared name is not independently searchable by its full displayed label: " + pair.Key + "/" + candidate);
            }
        }
        I18n.Current = "zh-Hans";
        const string chirenMove = "move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_BD";
        foreach (bool pad in new[] { false, true })
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            const string laser = "ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser";
            var match = workspace.FindFunctions("千目凝视");
            Check(match.Any(item => item.Id == laser), "Thousand-Eye Gaze must find its actual native laser entrance on both devices.");
            Check(match.All(item => !item.Id.Contains("LaserEyes")), "A passive upgrade must not become a fabricated ability entrance.");
            Check(BindingWorkspace.FunctionLabel(laser).Contains("死光"), "The native laser entrance must retain the first unlocked official move name.");
            var flame = workspace.FindFunctions("焰波");
            Check(flame.Any(item => item.Id == chirenMove), "Blazing Edge's Flame Wave is not searchable.");
            Check(workspace.FindFunctions("炽刃 焰波").Any(item => item.Id == chirenMove), "Weapon and move names must be searchable together.");
            var flameChoices = workspace.FindFunctionChoices("焰波").Where(item => item.Id == chirenMove).ToArray();
            var whirlwindChoices = workspace.FindFunctionChoices("旋风斩击").Where(item => item.Id == chirenMove).ToArray();
            Check(flameChoices.Length == 1 && flameChoices[0].Label == "炽刃 · 焰波"
                && whirlwindChoices.Length == 1 && whirlwindChoices[0].Label == "炽刃 · 旋风斩击",
                "A shared move search must return only the matching display entry.");
            Check(workspace.FindFunctionChoices(chirenMove).Count(item => item.Id == chirenMove) == 1,
                "A technical move identity must resolve to one visible picker row.");
            Check(workspace.FindFunctionChoices("独眼弓 凝视").Any(item => item.Label == "独眼弓 · 凝视"),
                "A generated weapon-qualified name is not directly searchable.");
            using (var dialog = new UnifiedActionChoiceDialog(workspace, pad ? "Gamepad_DPad_Down" : "G", null))
            {
                dialog.Show(); Application.DoEvents();
                Field<TextBox>(dialog, "search").Text = "千目凝视"; Call(dialog, "FlushSearch"); Application.DoEvents();
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
            using (var dialog = new UnifiedActionChoiceDialog(workspace, pad ? "Gamepad_DPad_Right" : "H", null))
            {
                dialog.Show(); Application.DoEvents();
                var searchBox = Field<TextBox>(dialog, "search");
                var list = Field<ListBox>(dialog, "functions");
                searchBox.Text = "焰波"; Call(dialog, "FlushSearch"); Application.DoEvents();
                var visible = list.Items.Cast<BindingFunction>().Where(item => item.Id == chirenMove).ToArray();
                Check(visible.Length == 1 && visible[0].Label == "炽刃 · 焰波", "The picker still shows shared move names as one ambiguous row.");
            }
        }
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
            var names = workspace.FunctionChoices.Where(item => item.Id == chirenMove).ToArray();
            workspace.AddFunctions(names.Select(item => item.Id), new BindingEntry { Keys = new[] { "F12" }, Trigger = "press" });
            Check(names.Select(item => item.Id).Distinct().Count() == 1
                && config.Rules.Count == 1 && config.Rules[0].Outputs.Count == 1,
                "Selecting multiple names for one underlying move must create only one output.");
        }
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
            var choices = workspace.FunctionChoices.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.ToArray());
            var shared = choices.Where(pair => pair.Value.Length > 1).ToArray();
            Check(shared.Length > 0, "The shared-name sweep found no shared action IDs.");
            foreach (string id in document.entries.Keys)
                Check(workspace.FindFunctionChoices(id).Count(item => item.Id == id) == 1,
                    "A technical identity did not resolve to exactly one display row: " + id);
            var duplicateLabels = workspace.FunctionChoices.GroupBy(item => item.Label, StringComparer.Ordinal)
                .Select(group => new { Label = group.Key, Ids = group.Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray() })
                .Where(item => item.Ids.Length > 1).ToArray();
            Check(duplicateLabels.Length > 0, "The catalog should retain distinct actions with identical official names.");
            foreach (var duplicate in duplicateLabels)
            {
                string[] found = workspace.FindFunctionChoices(duplicate.Label)
                    .Where(item => item.Label == duplicate.Label).Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
                Check(duplicate.Ids.All(id => found.Contains(id, StringComparer.Ordinal)),
                    "A name shared by different technical actions lost one of its rows: " + duplicate.Label);
            }
            const string dualHook = "move:Weapon.Weapon_Hun_DualHook.GA_Hun_DualHook_DB";
            const string slitheringSword = "move:Weapon.Weapon_Hun_SlitheringSword.GA_Hun_SlitheringSword_DB4";
            Check(choices[dualHook].Any(item => item.Label == "别离残钩 · 以逸待劳·之二"),
                "The native placeholder alias did not expand to its second Dual Hook label.");
            Check(choices[slitheringSword].Any(item => item.Label == "灵蛇软剑 · 以柔克刚·之三"),
                "The native placeholder alias did not expand to its third Slithering Sword label.");
            foreach (var pair in shared)
            {
                config.Rules.Clear();
                workspace.AddFunctions(pair.Value.Select(item => item.Id), new BindingEntry { Keys = new[] { "F12" }, Trigger = "press" });
                Check(config.Rules.Count == 1 && config.Rules[0].Outputs.Count == 1,
                    "Selecting every display name for a shared action created duplicate outputs: " + pair.Key);
            }
            Console.WriteLine("GameMoveNameTests shared IDs=" + shared.Length
                + "; shared rows=" + shared.Sum(pair => pair.Value.Length)
                + "; duplicate labels across actions=" + duplicateLabels.Length
                + "; placeholder IDs=" + document.entries.Count(pair => pair.Value.aliases != null
                    && pair.Value.aliases.Values.SelectMany(values => values).Any(IsVariant)));
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
