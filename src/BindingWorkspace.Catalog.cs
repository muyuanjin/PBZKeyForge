using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PBZKeyForge
{
    public sealed partial class BindingWorkspace
    {
        sealed class SearchHit
        {
            internal BindingFunction Function;
            internal int Score, Index;
            internal bool DisplayMatched;
        }
        static readonly string[] CatalogRoots = { "native", "locomotion", "targeting", "offense", "defense", "weapons", "shadow", "accessories", "interaction", "scene", "interface", "schemes", "custom", "debug" };
        static readonly string[] PlayerMappings = {
            "IA_MoveFast", "IA_MoveSlow", "IA_Jump", "IA_Crouch", "IA_LightAttack", "IA_HeavyAttack", "IA_Guard", "IA_Dodge",
            "IA_LockTarget", "IA_Execute", "IA_UltimateSkill", "IA_SwitchWeapon", "IA_ShadowArm", "IA_ShadowArm_LongPress",
            "IA_SwitchShadowArm", "IA_Interact", "IA_UseTopItem", "IA_UseBottomItem", "IA_Sense_Default", "IA_OpenCharacterMenu", "IA_OpenMapMenu"
        };

        string lastSearchQuery;
        BindingFunction[] lastSearchResult;
        string lastChoiceSearchQuery;
        BindingFunction[] lastChoiceSearchResult;
        BindingFunction[] functionChoices, functionChoicesSource;
        string functionChoicesLanguage;

        public BindingFunction[] FindFunctions(string query)
        {
            query = (query ?? "").Trim();
            var functions = Functions;
            if (query.Length == 0) return functions;
            string[] terms = SearchTerms(query);
            if (terms.Length == 0) return functions;
            string normalized = string.Join(" ", terms);
            if (normalized == lastSearchQuery && lastSearchResult != null) return lastSearchResult;
            var hits = new List<SearchHit>();
            for (int index = 0; index < functions.Length; index++)
            {
                int score = SearchScore(functions[index], normalized, terms);
                if (score >= 0) hits.Add(new SearchHit { Function = functions[index], Score = score, Index = index });
            }
            lastSearchQuery = normalized;
            lastSearchResult = hits.OrderBy(hit => hit.Score)
                .ThenBy(hit => hit.Function.NativeAction != null ? 0 : hit.Function.Id.StartsWith("ability:", StringComparison.Ordinal) ? 2 : 1)
                .ThenBy(hit => hit.Index).Select(hit => hit.Function).ToArray();
            return lastSearchResult;
        }

        public BindingFunction[] FunctionChoices
        {
            get
            {
                var source = Functions;
                if (functionChoices != null && object.ReferenceEquals(functionChoicesSource, source)
                    && functionChoicesLanguage == I18n.Current) return functionChoices;
                var result = new List<BindingFunction>();
                foreach (var function in source)
                    foreach (string label in GameMoveNames.DisplayNames(function.Id, function.Label))
                        result.Add(DisplayChoice(function, label));
                functionChoices = result.ToArray();
                functionChoicesSource = source;
                functionChoicesLanguage = I18n.Current;
                lastChoiceSearchQuery = null;
                lastChoiceSearchResult = null;
                return functionChoices;
            }
        }

        static BindingFunction DisplayChoice(BindingFunction function, string label)
        {
            string searchText = function.SearchText + " " + label;
            return new BindingFunction
            {
                Id = function.Id,
                Label = label,
                TechnicalName = function.TechnicalName,
                NativeAction = function.NativeAction,
                SearchText = searchText,
                LabelSource = function.LabelSource,
                IdentityCode = function.IdentityCode,
                GroupPath = function.GroupPath,
                GroupLabels = function.GroupLabels,
                GroupSearchText = function.GroupSearchText,
                SearchNames = function.SearchNames,
                GroupPathText = function.GroupPathText,
                LabelLower = NormalizeSearch(label),
                GroupSearchTextLower = function.GroupSearchTextLower,
                SearchTextLower = NormalizeSearch(searchText),
                SearchNamesLower = function.SearchNamesLower,
                RequiresKeyEnhancement = function.RequiresKeyEnhancement,
                FixedKeys = function.FixedKeys,
                FactoryKeys = function.FactoryKeys,
                DefaultOutputValue = function.DefaultOutputValue,
                Targets = function.Targets
            };
        }

        public BindingFunction[] FindFunctionChoices(string query)
        {
            query = (query ?? "").Trim();
            var choices = FunctionChoices;
            if (query.Length == 0) return choices;
            string[] terms = SearchTerms(query);
            if (terms.Length == 0) return choices;
            string normalized = string.Join(" ", terms);
            if (normalized == lastChoiceSearchQuery && lastChoiceSearchResult != null) return lastChoiceSearchResult;
            var hits = new List<SearchHit>();
            for (int index = 0; index < choices.Length; index++)
            {
                int score = SearchScore(choices[index], normalized, terms);
                if (score < 0) continue;
                hits.Add(new SearchHit
                {
                    Function = choices[index],
                    Score = score,
                    Index = index,
                    DisplayMatched = ContainsAll(choices[index].LabelLower, terms)
                });
            }
            var ordered = hits.OrderBy(hit => hit.Score)
                .ThenBy(hit => hit.Function.NativeAction != null ? 0 : hit.Function.Id.StartsWith("ability:", StringComparison.Ordinal) ? 2 : 1)
                .ThenBy(hit => hit.Index).ToArray();
            var displayed = new HashSet<string>(ordered.Where(hit => hit.DisplayMatched).Select(hit => hit.Function.Id), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<BindingFunction>();
            foreach (var hit in ordered)
            {
                if (displayed.Contains(hit.Function.Id))
                {
                    if (!hit.DisplayMatched) continue;
                }
                else if (!seen.Add(hit.Function.Id)) continue;
                result.Add(hit.Function);
            }
            lastChoiceSearchQuery = normalized;
            lastChoiceSearchResult = result.ToArray();
            return lastChoiceSearchResult;
        }

        public BindingFunction FindFunctionChoice(string id, string label)
        {
            var function = Functions.First(item => item.Id == id);
            if (string.IsNullOrWhiteSpace(label)) return function;
            return FunctionChoices.FirstOrDefault(item => item.Id == id && item.Label == label) ?? function;
        }

        static string[] SearchTerms(string query)
        {
            var terms = new List<string>(); int start = -1;
            for (int index = 0; index <= query.Length; index++)
            {
                bool whitespace = index == query.Length || char.IsWhiteSpace(query[index]);
                if (!whitespace) { if (start < 0) start = index; continue; }
                if (start >= 0)
                {
                    terms.Add(NormalizeSearch(query.Substring(start, index - start))); start = -1;
                }
            }
            terms.RemoveAll(term => term.Length == 0);
            return terms.ToArray();
        }

        static string NormalizeSearch(string value)
        { return (value ?? "").ToLowerInvariant(); }

        static bool ContainsAll(string value, string[] terms)
        {
            if (value == null || value.Length == 0) return false;
            foreach (string term in terms)
                if (value.IndexOf(term, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        static int CountTerms(string value, string[] terms)
        {
            if (value == null || value.Length == 0) return 0;
            int count = 0;
            foreach (string term in terms) if (value.IndexOf(term, StringComparison.Ordinal) >= 0) count++;
            return count;
        }

        static int SearchScore(BindingFunction function, string query, string[] terms)
        {
            if (!ContainsAll(function.SearchTextLower, terms)) return -1;
            if (string.Equals(function.Id, query, StringComparison.OrdinalIgnoreCase)) return 0;
            int labelHits = CountTerms(function.LabelLower, terms);
            bool labelMatch = labelHits == terms.Length;
            bool nameMatch = function.SearchNamesLower.Any(name => ContainsAll(name, terms));
            int score;
            if (terms.Length == 1)
            {
                if (function.SearchNamesLower.Any(name => string.Equals(name, query, StringComparison.Ordinal)))
                    return 1000000;
                else if (labelMatch && (function.LabelLower.StartsWith(query, StringComparison.Ordinal)
                    || function.SearchNamesLower.Any(name => name.StartsWith(query, StringComparison.Ordinal)))) score = 2;
                else if (labelMatch || nameMatch) score = 3;
                else if (ContainsAll(function.GroupSearchTextLower, terms)) score = 4;
                else score = 5;
            }
            else if (labelMatch) score = 2;
            else if (nameMatch) score = 3;
            else if (ContainsAll(function.GroupSearchTextLower, terms)) score = 4;
            else score = 5;
            string label = function.LabelLower ?? "";
            int labelPosition = label.Length;
            foreach (string term in terms)
            {
                int position = label.IndexOf(term, StringComparison.Ordinal);
                if (position >= 0) labelPosition = Math.Min(labelPosition, position);
            }
            return score * 1000000 + Math.Min(99, terms.Length - labelHits) * 100000
                + Math.Min(9999, label.Length) * 10 + Math.Min(9, labelPosition);
        }

        static string[] PlayerAliases(string id)
        {
            string aliases;
            switch (id)
            {
                case "IA_LightAttack": aliases = "普攻;轻击;普通攻击;Light Attack"; break;
                case "IA_HeavyAttack": aliases = "重击;蓄力;Heavy Attack"; break;
                case "IA_Guard": aliases = "防御;格挡;架势;弹反;Guard;Block;Parry"; break;
                case "IA_Dodge": aliases = "躲避;翻滚;闪躲;Dodge;Evade"; break;
                case "IA_Jump": aliases = "跳;跳跃;Jump"; break;
                case "IA_Crouch": aliases = "下蹲;蹲下;潜行;Crouch;Sneak"; break;
                case "IA_MoveFast": aliases = "冲刺;跑步;奔跑;疾跑;Sprint;Run"; break;
                case "IA_MoveSlow": aliases = "走路;慢走;步行;Walk"; break;
                case "IA_LockTarget": aliases = "锁定;锁敌;Lock Target;Target Lock"; break;
                case "IA_SwitchWeapon": aliases = "切换武器;更换武器;Switch Weapon"; break;
                case "IA_SwitchShadowArm": aliases = "切换影之武装;切换杀气武器;Switch Phantom Edge"; break;
                case "IA_ShadowArm": aliases = "杀气武器;影之武装;Phantom Edge"; break;
                case "IA_UseTopItem": case "IA_UseBottomItem": aliases = "药品;治疗;回血;喝药;药;Heal;Medicine;Potion"; break;
                case "IA_Interact": aliases = "互动;交互;使用;Interact"; break;
                case "IA_Execute": aliases = "处决;终结;Execute;Execution"; break;
                case "IA_UltimateSkill": aliases = "战技;绝技;Combat Art"; break;
                case "IA_Sense_Default": aliases = "鹰眼;侦察;感知;洞察;Sense"; break;
                case "IA_OpenMapMenu": aliases = "地图;Map"; break;
                case "IA_OpenCharacterMenu": aliases = "角色菜单;菜单;背包;Character Menu"; break;
                case "movement:forward": aliases = "前进;向前;WASD;Forward"; break;
                case "movement:backward": aliases = "后退;向后;WASD;Backward"; break;
                case "movement:left": aliases = "左移;向左;WASD;Strafe Left"; break;
                case "movement:right": aliases = "右移;向右;WASD;Strafe Right"; break;
                default: aliases = ""; break;
            }
            return aliases.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        static string WeaponLabel(FunctionLocalization localization)
        {
            if (localization == null || localization.labels.Count == 0) return "";
            string label;
            if (!localization.labels.TryGetValue(I18n.Current, out label)) label = localization.labels["en"];
            int separator = label.IndexOf(" · ", StringComparison.Ordinal);
            return separator < 0 ? label : label.Substring(0, separator);
        }

        static string Purpose(BindingFunction function)
        {
            if (function.NativeAction != null) return function.NativeAction;
            if (function.FixedKeys != null) return "movement";
            string[] category = ActionCatalogLabels.Category(function.Id);
            return category.Length == 0 ? "custom" : category[category.Length - 1];
        }

        static string PurposeLabel(string purpose)
        {
            if (purpose == "IA_CameraRotate") return I18n.Str("unified.groups.camera");
            if (purpose.StartsWith("IA_", StringComparison.Ordinal)) return FunctionLabel(purpose);
            return I18n.Str("unified.groups." + purpose);
        }

        void SetGroups(BindingFunction function, FunctionLocalization localization)
        {
            string[] path = function.Id.StartsWith("scheme:", StringComparison.Ordinal) ? new[] { "schemes" } : function.NativeAction != null ? new[] { "native" }
                : function.FixedKeys != null ? new[] { "locomotion", "movement" } : ActionCatalogLabels.Category(function.Id);
            if (path.Length == 0) path = new[] { "custom" };
            string[] labels = path.Select(PurposeLabel).ToArray();
            if (path.Length > 1 && (path[0] == "weapons" || path[0] == "shadow" || path[0] == "accessories") && path[1] != "equipment" && path[1] != "special")
                labels[1] = CompleteMoveCatalog.Contains(function.Id)
                    ? CompleteMoveCatalog.LabelPrefix(function.Id, WeaponLabel(localization)) : WeaponLabel(localization);
            if (path.Length > 1 && (path[0] == "weapons" || path[0] == "shadow" || path[0] == "accessories"))
            {
                int separator = labels[1].IndexOf(" · ", StringComparison.Ordinal);
                if (separator >= 0) labels[1] = labels[1].Substring(0, separator);
            }
            if (path[0] == "custom") labels[0] = I18n.Str("unified.customtarget");
            if (path[0] == "schemes") labels[0] = I18n.Str("schemes.group");
            function.GroupPath = path; function.GroupLabels = labels;
            function.GroupPathText = string.Join("/", path); function.GroupSearchText = string.Join(" ", labels);
        }

        static int MappingOrder(string mapping)
        {
            if (mapping == "movement") return 0;
            int index = Array.IndexOf(PlayerMappings, mapping);
            if (index >= 0) return index + 1;
            string[] remaining = { "camera", "medicine", "equipment", "special", "climb", "ship", "menus", "tabs", "skip", "qte", "photo", "debug", "test", "legacy" };
            index = Array.IndexOf(remaining, mapping); return index >= 0 ? PlayerMappings.Length + index + 1 : 100;
        }

        BindingFunction[] OrderFunctions(IEnumerable<BindingFunction> source)
        {
            return source.OrderBy(function => Array.IndexOf(CatalogRoots, function.GroupPath[0]))
                .ThenBy(function => function.GroupPath[0] == "native" ? MappingOrder(Purpose(function)) : 0)
                .ThenBy(function => function.GroupPath.Length > 1 && (function.GroupPath[0] == "weapons" || function.GroupPath[0] == "shadow") ? function.GroupPath[1] == "equipment" ? "" : NaturalKey(function.GroupPath[1]) : "", StringComparer.Ordinal)
                .ThenBy(function => function.GroupPath.Length > 1 && function.GroupPath[0] != "weapons" && function.GroupPath[0] != "shadow" ? MappingOrder(function.GroupPath[1]) : 0)
                .ThenBy(function => function.GroupPath.Length > 2 ? MappingOrder(function.GroupPath[2]) : 0)
                .ThenBy(function => LeafOrder(function))
                .ThenBy(function => InputVariantOrder(function))
                .ThenBy(function => NaturalKey(function.Id), StringComparer.Ordinal).ToArray();
        }

        int LeafOrder(BindingFunction function)
        {
            string id = function.Id;
            string name = id.Substring(Math.Max(id.LastIndexOf('/'), id.LastIndexOf(':')) + 1);
            if (function.GroupPath[0] == "native" || function.GroupPath[0] == "custom" || function.GroupPath[0] == "debug") return 0;
            if (id.StartsWith("movement:", StringComparison.Ordinal)) return pad ? 2 + Array.IndexOf(Directions, id.Substring(9)) : Array.IndexOf(Directions, id.Substring(9));
            if (id == "action:" + KeyboardMovement) return pad ? 1 : 4;
            if (id == "action:/SGameLocomotionDriver/Input/IA_JoystickMoveInput") return pad ? 0 : 5;
            if (id == "action:/Game/PBZContent/Input/InputActions/IA_MoveInput") return 6;
            if (function.GroupPath[0] == "scene")
            {
                if (name == "IA_ShipForward") return 0;
                if (name == "IA_ShipBackword") return 1;
                if (name == "IA_ShipRight") return 2;
                if (name == "IA_ShipLook") return 3;
                if (name == "IA_ClimblMove" || name == "IA_ClimbMoveGamePad") return name.EndsWith("GamePad", StringComparison.Ordinal) == pad ? 0 : 1;
                if (id.StartsWith("action:", StringComparison.Ordinal) && name.StartsWith("IA_Climb", StringComparison.Ordinal)) return 2;
                if (id.Contains("SideRunning.Ground")) return 10;
                if (id.Contains("SideRunning.FromJump")) return 11;
                if (function.GroupPath[1] == "climb") return 5;
                if (name == "IA_NextTab") return 0;
                if (name == "IA_PreviousTab") return 1;
            }
            if (id.StartsWith("action:", StringComparison.Ordinal))
            {
                if (name.Contains("ChangeLockTarget"))
                {
                    if (name.EndsWith("Next", StringComparison.Ordinal)) return 10;
                    if (name.EndsWith("Previous", StringComparison.Ordinal)) return 11;
                    return name.Contains("Joystick") == pad ? 20 : 21;
                }
                if (name.Contains("LookInput") || name.Contains("MoveInput"))
                {
                    if (name.Contains("Joystick") || name.Contains("JoyStick")) return pad ? 0 : 1;
                    if (name.Contains("Mouse") || name.Contains("Keyboard")) return pad ? 1 : 0;
                    return 2;
                }
                return 0;
            }
            return ActionCatalogLabels.Order(id);
        }

        static int InputVariantOrder(BindingFunction function)
        {
            if (!function.Id.StartsWith("action:", StringComparison.Ordinal)) return 0;
            string name = function.Id.Substring(function.Id.LastIndexOf('/') + 1);
            if (name.EndsWith("_PressLong", StringComparison.Ordinal) || name.EndsWith("_LongPress", StringComparison.Ordinal)) return 2;
            if (name.EndsWith("_Hold", StringComparison.Ordinal) || name.EndsWith("Hold", StringComparison.Ordinal)) return 1;
            return 0;
        }

        static string NaturalKey(string value)
        { return Regex.Replace(value ?? "", "[0-9]+", match => match.Value.PadLeft(8, '0')); }
    }
}
