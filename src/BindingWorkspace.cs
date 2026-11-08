using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed class BindingFunction
    {
        public string Id, Label, TechnicalName, NativeAction, SearchText, LabelSource, IdentityCode;
        public string[] GroupPath = new string[0], GroupLabels = new string[0];
        public string GroupSearchText;
        public string[] SearchNames = new string[0];
        public bool RequiresKeyEnhancement;
        public string[] FixedKeys;
        public string[] FactoryKeys = new string[0];
        public object DefaultOutputValue;
        public KeyEnhancementCatalogEntry[] Targets = new KeyEnhancementCatalogEntry[0];
        public override string ToString() { return Label; }
    }

    public sealed class BindingEntry
    {
        public string Id, FunctionId, Axis, RuleId, Trigger = "default";
        public string ExecutionOwner;
        public string DisplayName;
        public bool OriginalInputBlocked;
        public bool? KeepNativeTrigger;
        public bool BypassResources, BypassRestrictions, SuperArmor, Invincible;
        public string[] RestrictionBypasses = { "equipment", "training", "space", "stance", "predecessor", "cooldown" };
        public double DamageMultiplier = 1, StartupSpeed = 1, RecoverySpeed = 1;
        public string[] Keys = new string[0];
        public double Seconds = .25, Interval = .3, Threshold = .5;
        public int Count = 2, Priority, OutputIndex = -1;
        public bool IsRuntimeBinding, SchemeScoped;
        public bool IsNative, Enabled = true, Active, Pending, Suppressed, IsDefault, Shared, ReadOnly;
        public KeyEnhancementRule Rule;
        public object OutputValue;
        public string ActionId { get { return FunctionId; } }
        public string Label { get { return DisplayName ?? BindingWorkspace.FunctionLabel(FunctionId); } }
    }

    public sealed class KeyEnhancementRequiredException : InvalidOperationException
    {
        public KeyEnhancementRequiredException() : base(I18n.Str("unified.requires")) { }
    }

    public sealed partial class BindingWorkspace
    {
        public static readonly string[] AxisKeys = { "Gamepad_Left2D", "Gamepad_LeftX", "Gamepad_LeftY", "Gamepad_Right2D", "Gamepad_RightX", "Gamepad_RightY", "Gamepad_LeftTriggerAxis", "Gamepad_RightTriggerAxis", "Mouse2D", "MouseX", "MouseY" };
        sealed class FunctionLocalization
        {
            public FunctionLocalization() { }
            public Dictionary<string, string> labels = new Dictionary<string, string>();
            public string source = "unresolved";
            public string[] aliases = new string[0];
            public LocalizationEvidence[] evidence = new LocalizationEvidence[0];
        }
        sealed class LocalizationEvidence
        {
            public string asset { get; set; }
            public string key { get; set; }
            public string row { get; set; }
        }
        static readonly Dictionary<string, FunctionLocalization> Localizations = LoadLocalizations();
        const string KeyboardMovement = "/SGameLocomotionDriver/Input/IA_KeyboardMoveInput";
        const string ChangeArmed = "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed";
        static readonly string[] Directions = { "forward", "backward", "left", "right" };
        static readonly string[][] MovementKeys = { new[] { "W", "Up" }, new[] { "S", "Down" }, new[] { "A", "Left" }, new[] { "D", "Right" } };
        readonly EditorSession session;
        readonly KeyEnhancementConfiguration config, baseline;
        readonly bool pad;
        readonly int family;
        BindingFunction[] functions;
        string functionsLanguage;
        string functionsProfile;
        string[] customFunctionNames;
        public bool KeyEnhancementEnabled { get { return config.Enabled; } }
        public EditorSession Session { get { return session; } }
        public KeyEnhancementConfiguration Configuration { get { return config; } }
        public bool Gamepad { get { return pad; } }
        bool liveEditing;
        public Func<bool> LiveEditingProvider;
        public bool LiveEditing
        {
            get { return LiveEditingProvider == null ? liveEditing : LiveEditingProvider(); }
            set { liveEditing = value; }
        }
        public Action BeforeChange;
        void Changing() { if (BeforeChange != null) BeforeChange(); }
        NativeBindingOverride NativeOverride(string functionId, KeyEnhancementConfiguration source)
        { return source.NativeBindings.FirstOrDefault(item => item.FunctionId == functionId && item.Device == (pad ? "gamepad" : "keyboard")); }
        NativeBindingOverride EditNative(string functionId)
        {
            var item = NativeOverride(functionId, config);
            if (item == null)
            {
                item = new NativeBindingOverride { FunctionId = functionId, Device = pad ? "gamepad" : "keyboard",
                    Keys = session.Relations(functionId, pad, family).Keys.ToList() };
                config.NativeBindings.Add(item);
            }
            return item;
        }
        public int FamilyIndex { get { return family; } }
        public string[] UncoveredInputs { get { return KeyEnhancementInputPlan.Uncovered; } }

        public bool UsesNativeTrigger(string functionId)
        {
            var function = Functions.First(item => item.Id == functionId);
            return NativeTriggerTags(function).All(tag => !config.NativeAbilitySuppressions.Contains(tag));
        }

        public void SetNativeTrigger(string functionId, bool enabled)
        {
            Changing();
            var function = Functions.First(item => item.Id == functionId);
            foreach (string tag in NativeTriggerTags(function))
            {
                config.NativeAbilitySuppressions.RemoveAll(item => item == tag);
                if (!enabled) config.NativeAbilitySuppressions.Add(tag);
            }
        }

        static IEnumerable<string> NativeTriggerTags(BindingFunction function)
        {
            foreach (var target in function.Targets.Where(item => item.Kind == "ability"))
            {
                yield return target.Tag;
                if (target.Tag == "Ability.Action.Hun.SpecialDash") yield return "Ability.Action.Hun.DeflectDash";
            }
        }

        public BindingWorkspace(EditorSession session, KeyEnhancementConfiguration config, KeyEnhancementConfiguration baseline, bool pad, int family)
        {
            if (session == null || config == null) throw new ArgumentNullException(session == null ? "session" : "config");
            this.session = session; this.config = config; this.baseline = baseline ?? new KeyEnhancementConfiguration(); this.pad = pad; this.family = family;
            if (config.Enabled && config.Schemes.Count > 0)
            {
                this.baseline = this.baseline.Clone(); this.baseline.SetEnabled(config.Enabled); this.baseline.EnsureSchemes(session);
                if (this.baseline.Schemes.Any(item => item.Id == config.SelectedSchemeId)) this.baseline.SelectScheme(config.SelectedSchemeId);
                else this.baseline = config.Clone();
                this.baseline.Enabled = config.Enabled;
            }
        }

        public BindingFunction[] Functions
        {
            get
            {
                var customNames = config.CustomFunctions.Concat(config.Rules.SelectMany(rule => rule.Outputs))
                    .Select(output => OutputFunction(output) + "\n" + output.Name + "\n" + output.Kind + "\n"
                        + output.Path + "\n" + output.Tag + "\n" + output.Property + "\n" + output.Code + "\n" + output.MappingName).Distinct().ToArray();
                if (functions != null && functionsLanguage == I18n.Current && functionsProfile == session.SelectedProfile + config.SelectedSchemeId + string.Join(";", config.Schemes.Select(item => item.Id + item.Name))
                    && customNames.SequenceEqual(customFunctionNames))
                    return functions;
                var result = session.BindingActions(pad, family).Select(id => new BindingFunction {
                    Id = id, NativeAction = id, Label = FunctionLabel(id), TechnicalName = id, Targets = KeyEnhancementCatalog.ForMapping(id)
                }).ToList();
                if (config.Schemes.Count > 0)
                {
                    result.Add(new BindingFunction { Id = "scheme:previous", Label = I18n.Str("schemes.previous"), TechnicalName = "scheme:previous", RequiresKeyEnhancement = true });
                    result.Add(new BindingFunction { Id = "scheme:next", Label = I18n.Str("schemes.next"), TechnicalName = "scheme:next", RequiresKeyEnhancement = true });
                    foreach (var scheme in config.Schemes)
                        result.Add(new BindingFunction { Id = "scheme:" + scheme.Id, Label = I18n.Str("schemes.switch", KeyEnhancementConfiguration.SchemeName(scheme)), TechnicalName = "scheme:" + scheme.Id, RequiresKeyEnhancement = true });
                }
                for (int i = 0; i < Directions.Length; i++)
                    result.Add(new BindingFunction { Id = "movement:" + Directions[i], Label = FunctionLabel("movement:" + Directions[i]),
                        TechnicalName = KeyboardMovement + " · " + Directions[i], RequiresKeyEnhancement = true,
                        FixedKeys = pad ? new string[0] : MovementKeys[i], DefaultOutputValue = MovementValue(i),
                        Targets = new[] { KeyEnhancementCatalog.Targets.Single(target => target.Path == KeyboardMovement) } });
                foreach (var target in KeyEnhancementCatalog.Targets)
                    result.Add(new BindingFunction { Id = target.Id, Label = FunctionLabel(target.Id), TechnicalName = target.Path ?? target.Tag,
                        RequiresKeyEnhancement = true, Targets = new[] { target } });
                foreach (var output in config.CustomFunctions.Concat(config.Rules.SelectMany(rule => rule.Outputs)))
                {
                    string id = OutputFunction(output);
                    if (!result.Any(item => item.Id == id)) result.Add(new BindingFunction { Id = id, Label = string.IsNullOrWhiteSpace(output.Name) ? FunctionLabel(id) : output.Name, TechnicalName = output.Path ?? output.Tag ?? output.Property,
                        NativeAction = output.Kind == "mapping" ? output.MappingName : null, RequiresKeyEnhancement = output.Kind != "mapping" });
                }
                foreach (var function in result)
                {
                    function.FactoryKeys = function.NativeAction != null ? session.Relations(function.NativeAction, pad, family).DefaultKeys
                        : function.FixedKeys ?? ActionCatalogLabels.FactoryKeys(function.Id, pad);
                    FunctionLocalization localization;
                    bool localized = Localizations.TryGetValue(CompleteMoveCatalog.SourceId(function.Id), out localization) && localization.labels != null && localization.labels.ContainsKey(I18n.Current);
                    function.LabelSource = localized && !ActionCatalogLabels.IsSemantic(function.Id) && function.Id != "action:" + KeyboardMovement && function.Id != "action:/SGameLocomotionDriver/Input/IA_JoystickMoveInput" ? localization.source : "semantic";
                    function.SearchNames = new[] { function.Label }.Concat(GameMoveNames.SearchNames(function.Id)).Concat(PlayerAliases(function.Id))
                        .Concat(function.NativeAction == null && localization != null ? localization.aliases ?? new string[0] : new string[0])
                        .Concat(function.NativeAction == null ? new string[0] : I18n.Translations("act." + function.NativeAction)).ToArray();
                    function.SearchText = function.Label + " " + function.TechnicalName + " " + function.Id + " " + string.Join(" ", function.SearchNames) + " " + ActionCatalogLabels.SearchAliases(function.Id);
                    if (localization != null)
                        function.SearchText += " " + string.Join(" ", localization.labels.Values.Concat(localization.aliases ?? new string[0]));
                    foreach (var target in function.Targets)
                    {
                        function.SearchText += " " + string.Join(" ", target.MappingNames.Select(FunctionLabel)) + " " + string.Join(" ", target.MappingNames);
                        function.SearchText += " " + target.Name + " " + target.Path + " " + ActionCatalogLabels.SearchAliases(target.Id);
                        FunctionLocalization related;
                        if (Localizations.TryGetValue(target.Id, out related)) function.SearchText += " " + string.Join(" ", related.labels.Values.Concat(related.aliases ?? new string[0]));
                    }
                    if (function.Id == "ability:Ability.Action.Hun.SpecialDash") function.SearchText += " 瞬身 架势瞬身 特殊闪避 stance dash special dash";
                    if (function.Id.StartsWith("movement:", StringComparison.Ordinal) || function.TechnicalName == KeyboardMovement)
                        function.SearchText += " WASD 方向 移动 前后左右 movement direction " + string.Join(" ", function.FixedKeys ?? new string[0]);
                    SetGroups(function, localization);
                    function.SearchText += " " + function.GroupSearchText + " " + string.Join(" ", PlayerAliases(Purpose(function)));
                    if (function.TechnicalName != null && function.TechnicalName.Contains("Look")) function.SearchText += " 相机 镜头 摄像机 camera look view";
                    if (function.TechnicalName != null && function.TechnicalName.Contains("Climb")) function.SearchText += " 攀爬 爬梯 梯子 climb ladder";
                }
                foreach (var function in result)
                    function.IdentityCode = function.FixedKeys != null ? "WASD · " + function.Id.Substring(9)
                        : function.Targets.Length == 1 ? function.Targets[0].Name : function.TechnicalName ?? function.Id;
                foreach (var group in result.GroupBy(function => new { function.Label, function.IdentityCode }).Where(group => group.Count() > 1))
                    foreach (var function in group) function.IdentityCode = function.TechnicalName ?? function.Id;
                foreach (var group in result.GroupBy(function => new { function.Label, function.IdentityCode }).Where(group => group.Count() > 1))
                    foreach (var function in group) function.IdentityCode = function.Id;
                functionsLanguage = I18n.Current;
                functionsProfile = session.SelectedProfile + config.SelectedSchemeId + string.Join(";", config.Schemes.Select(item => item.Id + item.Name)); customFunctionNames = customNames;
                return functions = OrderFunctions(result);
            }
        }

        public static string FunctionLabel(string id)
        { return GameMoveNames.Label(id, ActionCatalogLabels.Label(id, BaseFunctionLabel(id))); }

        static string BaseFunctionLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (CompleteMoveCatalog.Contains(id))
            {
                string sourceId = CompleteMoveCatalog.SourceId(id);
                string source = sourceId == id ? "" : ActionCatalogLabels.Prefix(sourceId, BaseFunctionLabel(sourceId));
                string prefix = CompleteMoveCatalog.LabelPrefix(id, source);
                return string.Join(" · ", new[] { prefix, CompleteMoveCatalog.LabelSuffix(id) }.Where(part => !string.IsNullOrWhiteSpace(part)));
            }
            if (id.StartsWith("movement:", StringComparison.Ordinal)) return I18n.Str("unified.movement." + id.Substring(9));
            if (id == "action:" + KeyboardMovement) return I18n.Str("unified.movement.keyboard");
            if (id == "action:/SGameLocomotionDriver/Input/IA_JoystickMoveInput") return I18n.Str("unified.movement.joystick");
            FunctionLocalization localization;
            string nativeLabel;
            if (Localizations.TryGetValue(id, out localization) && localization.labels != null && localization.labels.TryGetValue(I18n.Current, out nativeLabel) && !string.IsNullOrWhiteSpace(nativeLabel)) return nativeLabel;
            if (id.StartsWith("target:", StringComparison.Ordinal) || id.StartsWith("custom:", StringComparison.Ordinal)) return I18n.Str("unified.customtarget");
            if (!id.Contains(":"))
            {
                string translated = I18n.Str("act." + id);
                return translated == "act." + id ? HumanName(id) : translated;
            }
            var target = KeyEnhancementCatalog.Targets.FirstOrDefault(item => item.Id == id);
            string name = target == null ? id.Substring(id.LastIndexOf(':') + 1) : target.Name;
            string special = name == "IA_Deflect" ? "IA_Deflect" : name == "IA_Stance" ? "IA_Stance"
                : name == "Ability.Action.Hun.SpecialDash" ? "SpecialDash" : null;
            if (special != null)
            {
                string label = I18n.Str("unified.function." + special);
                if (label != "unified.function." + special) return label;
            }
            if (target != null && target.MappingNames.Length > 0)
            {
                string label = FunctionLabel(target.MappingNames[0]);
                string variant = name.EndsWith("_PressLong", StringComparison.Ordinal) || name.EndsWith("_LongPress", StringComparison.Ordinal) ? "hold"
                    : name.EndsWith("_Hold", StringComparison.Ordinal) || name.EndsWith("Hold", StringComparison.Ordinal) ? "down"
                    : name.EndsWith("_Press", StringComparison.Ordinal) ? "press" : null;
                if (variant != null) label += " · " + I18n.Str("unified.variant." + variant);
                return label;
            }
            if (target != null && target.Kind == "action")
            {
                string semantic = name.IndexOf("Slomo", StringComparison.OrdinalIgnoreCase) >= 0 ? "slomo"
                    : name.IndexOf("Sequence_Press", StringComparison.OrdinalIgnoreCase) >= 0 ? "sequence"
                    : name.IndexOf("TakePhoto", StringComparison.OrdinalIgnoreCase) >= 0 ? "photo"
                    : target.Path.IndexOf("/Test/", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("IA_Face", StringComparison.Ordinal) ? "test"
                    : name.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0 ? "debug" : null;
                if (semantic != null) return I18n.Str("enhancement.function." + semantic);
            }
            return HumanName(name);
        }
        public static string LabelFor(string id) { return FunctionLabel(id); }

        static Dictionary<string, FunctionLocalization> LoadLocalizations()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.KeyEnhancement.action-localization.json"))
            {
                if (stream == null) return new Dictionary<string, FunctionLocalization>();
                using (var reader = new StreamReader(stream))
                {
                    var document = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    object entries;
                    if (!document.TryGetValue("entries", out entries)) return new Dictionary<string, FunctionLocalization>();
                    var json = new JavaScriptSerializer { MaxJsonLength = 16777216 };
                    return json.Deserialize<Dictionary<string, FunctionLocalization>>(json.Serialize(entries));
                }
            }
        }

        static string HumanName(string name)
        {
            int slash = name.LastIndexOf('/'); if (slash >= 0) name = name.Substring(slash + 1);
            if (name.StartsWith("Ability.", StringComparison.Ordinal)) name = string.Join(" · ", name.Split('.').Skip(3));
            if (name.StartsWith("IA_", StringComparison.Ordinal)) name = name.Substring(3);
            return Regex.Replace(name.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
        }

        static object MovementValue(int index)
        { return new Dictionary<string, object> { { "x", index == 0 ? 1d : index == 1 ? -1d : 0d }, { "y", index == 2 ? -1d : index == 3 ? 1d : 0d }, { "z", 0d } }; }

        public BindingEntry[] Entries(string functionId) { return Entries(functionId, config, false); }
        public string[] ResourceKinds(string functionId)
        {
            var function = Functions.First(item => item.Id == functionId);
            return ResourceCostCatalog.ForOutput(function.NativeAction == null ? CreateOutput(function)
                : new KeyEnhancementOutput { Kind = "mapping", MappingActions = function.Targets.Select(target => target.Path).ToArray() });
        }
        public BindingEntry[] GetSnapshot() { return CollectEntries(config, false, null); }

        BindingEntry[] Entries(string functionId, KeyEnhancementConfiguration source, bool before)
        { return CollectEntries(source, before, functionId); }

        BindingEntry[] CollectEntries(KeyEnhancementConfiguration source, bool before, string functionId, bool allDevices = false)
        {
            var result = new List<BindingEntry>();
            var selected = functionId == null ? Functions : new[] { Functions.First(item => item.Id == functionId) };
            foreach (var function in selected)
            if (function.NativeAction != null)
            {
                var relation = before ? session.RelationsBeforeEdit(function.NativeAction, pad, family) : session.Relations(function.NativeAction, pad, family);
                var nativeOverride = NativeOverride(function.Id, source);
                if (nativeOverride != null && !source.Enabled)
                    foreach (string original in relation.Keys)
                        result.Add(new BindingEntry { Id = "save:" + function.Id + ":" + original, FunctionId = function.Id, Keys = new[] { original },
                            IsNative = true, Active = true, ExecutionOwner = "game", IsDefault = relation.DefaultKeys.Contains(original),
                            Pending = !before && session.RelationshipPending(function.NativeAction, original, pad, family),
                            ReadOnly = session.Defaults.FixedForAction(function.NativeAction, pad).Contains(original) });
                foreach (string key in nativeOverride == null ? relation.Keys : nativeOverride.Keys.ToArray())
                {
                    bool suppressed = source.Enabled && FullySuppressed(function, key, source);
                    result.Add(new BindingEntry { Id = "native:" + function.Id + ":" + key, FunctionId = function.Id, Keys = new[] { key }, IsNative = true,
                        ExecutionOwner = nativeOverride != null && !source.Enabled ? "inactive" : !source.Enabled || KeyEnhancementInputPlan.Retained(function.NativeAction) ? "game" : !KeyEnhancementInputPlan.Covers(function.NativeAction, key) ? "uncovered" : suppressed ? "inactive" : "scheduler",
                        OriginalInputBlocked = source.Enabled && KeyEnhancementInputPlan.Covers(function.NativeAction, key),
                        IsRuntimeBinding = nativeOverride != null, Active = !suppressed && (nativeOverride == null || source.Enabled), Suppressed = suppressed, IsDefault = relation.DefaultKeys != null && relation.DefaultKeys.Contains(key),
                        ReadOnly = nativeOverride == null && session.Defaults.FixedForAction(function.NativeAction, pad).Contains(key),
                        Pending = !before && (Serialize(nativeOverride) != Serialize(NativeOverride(function.Id, baseline)) || session.RelationshipPending(function.NativeAction, key, pad, family) || suppressed != (baseline.Enabled && FullySuppressed(function, key, baseline))) });
                }
            }
            foreach (var function in selected.Where(item => item.FixedKeys != null))
                foreach (string key in function.FixedKeys)
                {
                    bool suppressed = source.Enabled && FullySuppressed(function, key, source);
                    result.Add(new BindingEntry { Id = "fixed:" + function.Id + ":" + key, FunctionId = function.Id, Keys = new[] { key },
                        IsNative = true, ReadOnly = true, IsDefault = true, Active = !suppressed, Suppressed = suppressed,
                        OutputValue = function.DefaultOutputValue, ExecutionOwner = !source.Enabled ? "game" : suppressed ? "inactive" : "scheduler",
                        OriginalInputBlocked = source.Enabled,
                        Pending = !before && suppressed != (baseline.Enabled && FullySuppressed(function, key, baseline)) });
                }
            var oldRules = baseline.Rules.ToDictionary(rule => rule.Id);
            foreach (var rule in source.Rules.Where(rule => allDevices || DeviceMatches(rule)))
            {
                var indexes = Enumerable.Range(0, rule.Outputs.Count).Where(index => functionId == null || OutputFunction(rule.Outputs[index]) == functionId).ToArray();
                if (indexes.Length == 0) continue;
                KeyEnhancementRule old; oldRules.TryGetValue(rule.Id, out old);
                string serialized = Serialize(rule);
                bool pending = !before && (old == null || Serialize(old) != serialized || baseline.Enabled != config.Enabled);
                foreach (int i in indexes)
                    {
                        result.Add(new BindingEntry { Id = rule.Id + ":" + i, FunctionId = OutputFunction(rule.Outputs[i]), Keys = rule.Keys.ToArray(), Axis = rule.Axis,
                            DisplayName = rule.Outputs[i].Kind == "scheme" ? SchemeBindingLabel(source, rule.Outputs[i].Tag) : null,
                            ExecutionOwner = !source.Enabled || !rule.Enabled ? "inactive" : KeyEnhancementInputPlan.SupportsMapping(rule.Outputs[i]) ? "scheduler" : "uncovered",
                            Trigger = rule.DefaultTrigger ? "default" : rule.Trigger,
                            Seconds = rule.Seconds, Interval = rule.Interval, Threshold = rule.Threshold, Count = rule.Count, Priority = rule.Priority, BypassResources = rule.BypassResources,
                            BypassRestrictions = rule.BypassRestrictions, RestrictionBypasses = rule.RestrictionBypasses.ToArray(),
                            DamageMultiplier = rule.DamageMultiplier, StartupSpeed = rule.StartupSpeed, RecoverySpeed = rule.RecoverySpeed,
                            SuperArmor = rule.SuperArmor, Invincible = rule.Invincible,
                            RuleId = rule.Id, OutputIndex = i, Rule = new JavaScriptSerializer().Deserialize<KeyEnhancementRule>(serialized), OutputValue = rule.Outputs[i].Kind == "action" || rule.Outputs[i].Kind == "mapping" ? rule.Outputs[i].Value : null,
                            Enabled = rule.Enabled, Active = source.Enabled && rule.Enabled && KeyEnhancementInputPlan.SupportsMapping(rule.Outputs[i]), Shared = rule.Outputs.Count > 1,
                            Pending = pending });
                    }
            }
            foreach (var entry in result) entry.SchemeScoped = source.Schemes.Count > 0 && (entry.Rule == null || entry.Rule.Outputs.Any(output => output.Kind != "scheme"));
            return result.ToArray();
        }

        bool DeviceMatches(KeyEnhancementRule rule)
        {
            var keys = rule.Keys.Concat(rule.Watch).Concat(rule.Steps.SelectMany(step => step)).Concat(string.IsNullOrEmpty(rule.Axis) ? new string[0] : new[] { rule.Axis }).ToArray();
            return keys.Length == 0 || keys.Any(key => IsPad(key) == pad);
        }
        static string SchemeBindingLabel(KeyEnhancementConfiguration source, string id)
        {
            return id == "previous" ? I18n.Str("schemes.previous") : id == "next" ? I18n.Str("schemes.next")
                : I18n.Str("schemes.switch", KeyEnhancementConfiguration.SchemeName(source.Schemes.First(item => item.Id == id)));
        }
        static bool IsPad(string key) { return key.StartsWith("Gamepad_", StringComparison.Ordinal) || key.StartsWith("PBZ_Touchpad_", StringComparison.Ordinal); }
        static string OutputFunction(KeyEnhancementOutput output)
        {
            if (output.Kind == "scheme") return "scheme:" + output.Tag;
            if (output.Kind == "move") return output.Tag;
            if (!string.IsNullOrEmpty(output.FunctionId)) return output.FunctionId;
            if (!string.IsNullOrWhiteSpace(output.Id)) return "custom:" + output.Id;
            if (output.Kind == "mapping") return output.MappingName;
            if (output.Kind == "action") return "action:" + PackagePath(output.Path);
            if (output.Kind == "ability") return "ability:" + output.Tag;
            return "target:" + output.Kind + ":" + (output.Path ?? output.Tag ?? output.Property ?? output.Code ?? "");
        }
        static string PackagePath(string path)
        { return path == null ? "" : Regex.Replace(path, "\\.[^./]+$", ""); }
        static string Serialize(object value) { return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Serialize(value); }
        static KeyEnhancementRule Copy(KeyEnhancementRule rule) { return new JavaScriptSerializer().Deserialize<KeyEnhancementRule>(Serialize(rule)); }
        static IEnumerable<KeyEnhancementSuppression> AllSuppressions(KeyEnhancementConfiguration source)
        { return source.Suppressions.Concat(source.Rules.Where(rule => rule.Enabled || rule.BlockOriginalWhenDisabled).SelectMany(rule => rule.Suppressions)); }
        static bool FullySuppressed(BindingFunction function, string key, KeyEnhancementConfiguration source)
        {
            return function.Targets.Length > 0 && function.Targets.All(target => AllSuppressions(source).Any(item => PackagePath(item.ActionPath) == PackagePath(target.Path) && (string.IsNullOrEmpty(item.Key) || item.Key == key)));
        }

        public bool CanAddNative(string functionId)
        {
            var function = Functions.First(item => item.Id == functionId);
            return function.NativeAction != null && (UsesNativeOverride(functionId, null) || session.RelationshipCapacity(function.NativeAction, pad, family).CanAdd);
        }

        bool UsesNativeOverride(string functionId, BindingEntry current)
        { return config.Enabled && config.Schemes.Count > 0 || LiveEditing || (current == null ? config.Enabled && NativeOverride(functionId, config) != null : current.IsRuntimeBinding); }

        public bool UsesSaveBinding(string functionId, BindingEntry specification, BindingEntry current)
        {
            var function = Functions.First(item => item.Id == functionId);
            if (function.NativeAction == null || !IsPlainBinding(specification) || !NativeInput(specification)
                || UsesNativeOverride(functionId, current)) return false;
            if (current != null) return current.IsNative && !current.ReadOnly;
            return Entries(functionId).Any(entry => entry.IsNative && !entry.IsRuntimeBinding && entry.Keys.SequenceEqual(specification.Keys))
                || session.RelationshipCapacity(function.NativeAction, pad, family).CanAdd;
        }

        bool NativeInput(BindingEntry entry)
        { return entry.Keys.All(key => IsPad(key) == pad); }

        public bool CanUseNative(string functionId, BindingEntry entry)
        {
            return IsPlainBinding(entry) && NativeInput(entry) && (Entries(functionId).Any(item => item.IsNative && (config.Enabled || !item.IsRuntimeBinding) && item.Keys.SequenceEqual(entry.Keys))
                || Functions.First(item => item.Id == functionId).NativeAction != null && CanAddNative(functionId));
        }

        public static bool IsPlainBinding(BindingEntry entry)
        {
            var rule = entry.Rule;
            return !entry.BypassResources && !entry.BypassRestrictions && !entry.SuperArmor && !entry.Invincible && entry.DamageMultiplier == 1 && entry.StartupSpeed == 1 && entry.RecoverySpeed == 1
                && entry.Trigger == "default" && entry.Keys.Length == 1 && entry.OutputValue == null
                && entry.Priority == 0 && entry.Enabled && (rule == null || !rule.Consume && rule.ConsumeKeys.Count == 0
                    && string.IsNullOrWhiteSpace(rule.WhenLua) && string.IsNullOrWhiteSpace(rule.EvaluateLua)
                    && rule.Watch.Count == 0 && rule.Steps.Count == 0);
        }

        public BindingEntry Add(string functionId, BindingEntry specification)
        {
            Changing();
            var entry = AddCore(functionId, specification);
            if (specification.KeepNativeTrigger.HasValue) SetNativeTrigger(functionId, specification.KeepNativeTrigger.Value);
            return entry;
        }
        BindingEntry AddCore(string functionId, BindingEntry specification)
        {
            CheckInput(specification);
            var function = Functions.First(item => item.Id == functionId);
            if (function.NativeAction != null && IsPlainBinding(specification) && NativeInput(specification)
                && UsesNativeOverride(functionId, null))
            {
                var native = EditNative(functionId); native.Keys = native.Keys.Concat(specification.Keys).Distinct().ToList();
                return Entries(functionId).First(entry => entry.IsRuntimeBinding && entry.Keys.SequenceEqual(specification.Keys));
            }
            if ((function.NativeAction != null || function.FixedKeys != null) && IsPlainBinding(specification))
            {
                var existing = Entries(functionId).FirstOrDefault(entry => entry.IsNative && !entry.IsRuntimeBinding && entry.Keys[0] == specification.Keys[0]);
                if (existing != null)
                {
                    if (existing.Suppressed) RestoreNativeInput(function, specification.Keys[0]);
                    return Entries(functionId).First(entry => entry.Id == existing.Id);
                }
            }
            if (UsesSaveBinding(functionId, specification, null))
            {
                session.ChangeRelationship(function.NativeAction, specification.Keys[0], true, pad, family);
                return Entries(functionId).First(entry => entry.IsNative && !entry.IsRuntimeBinding && entry.Keys[0] == specification.Keys[0]);
            }
            var rule = NewRule(function, specification);
            config.Rules.Add(rule);
            LinkInputVariants(rule);
            return CollectEntries(config, false, functionId, true).First(entry => entry.RuleId == rule.Id);
        }

        public BindingEntry AddCustom(KeyEnhancementOutput output, BindingEntry specification, string label)
        {
            Changing(); CheckInput(specification);
            if (output == null) throw new ArgumentNullException("output");
            KeyEnhancementInputPlan.ValidateMapping(output);
            var target = new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(output));
            if (!target.NativePipeline.HasValue) target.NativePipeline = false;
            if (string.IsNullOrWhiteSpace(target.Id)) target.Id = Guid.NewGuid().ToString("N");
            if (!string.IsNullOrWhiteSpace(label)) target.Name = label.Trim();
            string id = OutputFunction(target);
            var function = new BindingFunction { Id = id, RequiresKeyEnhancement = true };
            var rule = specification.Rule == null ? new KeyEnhancementRule() : Copy(specification.Rule);
            rule.Id = Guid.NewGuid().ToString("N"); ApplySpecification(rule, specification, function, target);
            rule.Outputs = new List<KeyEnhancementOutput> { target }; rule.Suppressions.Clear();
            config.Rules.Add(rule); config.CustomFunctions.Add(target); functions = null;
            return CollectEntries(config, false, id, true).First(entry => entry.RuleId == rule.Id);
        }
        public BindingEntry AddCustomTarget(KeyEnhancementOutput output, BindingEntry specification, string label)
        { return AddCustom(output, specification, label); }
        public KeyEnhancementOutput CustomOutput(string functionId)
        {
            var output = config.CustomFunctions.Concat(config.Rules.Concat(baseline.Rules).SelectMany(rule => rule.Outputs)).First(item => OutputFunction(item) == functionId);
            return new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(output));
        }
        public string UpdateCustomOutput(string functionId, KeyEnhancementOutput output, string label)
        {
            Changing();
            var existing = CustomOutput(functionId);
            var replacement = new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(output));
            replacement.Id = string.IsNullOrWhiteSpace(existing.Id) ? Guid.NewGuid().ToString("N") : existing.Id;
            replacement.Name = label.Trim();
            var validation = new KeyEnhancementConfiguration();
            var check = new KeyEnhancementRule(); check.Keys.Add("F9"); check.Outputs.Add(replacement); validation.Rules.Add(check); validation.CompileLua();
            config.CustomFunctions.RemoveAll(item => OutputFunction(item) == functionId);
            config.CustomFunctions.Add(replacement);
            config.StoreSelectedScheme();
            foreach (var rule in config.Rules.Concat(config.Schemes.SelectMany(scheme => scheme.Rules)).Distinct())
                for (int index = 0; index < rule.Outputs.Count; index++)
                    if (OutputFunction(rule.Outputs[index]) == functionId)
                        rule.Outputs[index] = new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(replacement));
            functions = null;
            return OutputFunction(replacement);
        }
        public void DeleteCustomFunction(string functionId)
        {
            Changing();
            config.CustomFunctions.RemoveAll(item => OutputFunction(item) == functionId);
            foreach (var rule in config.Rules.ToArray())
                for (int index = rule.Outputs.Count - 1; index >= 0; index--)
                    if (OutputFunction(rule.Outputs[index]) == functionId) RemoveOutput(rule, index, functionId, false);
            config.StoreSelectedScheme();
            foreach (var scheme in config.Schemes.Where(item => item.Id != config.SelectedSchemeId))
            {
                foreach (var rule in scheme.Rules)
                {
                    rule.Outputs.RemoveAll(output => OutputFunction(output) == functionId);
                    rule.Suppressions.RemoveAll(item => item.FunctionId == functionId);
                }
                scheme.Rules.RemoveAll(rule => rule.Outputs.Count == 0);
            }
            functions = null;
        }
        public void AddHook(Dictionary<string, object> hook)
        {
            SetHook(-1, hook);
        }
        public void SetHook(int index, Dictionary<string, object> hook)
        {
            Changing();
            if (hook == null || !hook.ContainsKey("path") || string.IsNullOrWhiteSpace(Convert.ToString(hook["path"])))
                throw new ArgumentException(I18n.Str("enhancement.invalid", "Hooks[].path"));
            if (config.Hooks.Where((item, position) => position != index).Any(item => item.ContainsKey("path") && string.Equals(Convert.ToString(item["path"]), Convert.ToString(hook["path"]), StringComparison.Ordinal)))
                throw new ArgumentException(I18n.Str("unified.advanced.duplicatehook"));
            var value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Serialize(hook));
            if (index < 0) config.Hooks.Add(value);
            else config.Hooks[index] = value;
        }

        KeyEnhancementRule NewRule(BindingFunction function, BindingEntry specification)
        {
            var rule = specification.Rule == null ? new KeyEnhancementRule() : Copy(specification.Rule);
            rule.Id = Guid.NewGuid().ToString("N");
            ApplySpecification(rule, specification, function);
            rule.Outputs = new List<KeyEnhancementOutput> { CreateOutput(function) };
            SetOutputValue(rule.Outputs[0], specification.OutputValue ?? function.DefaultOutputValue);
            rule.Suppressions.Clear();
            return rule;
        }
        KeyEnhancementOutput CreateOutput(BindingFunction function)
        {
            if (function.Id.StartsWith("scheme:", StringComparison.Ordinal)) return new KeyEnhancementOutput { Kind = "scheme", Tag = function.Id.Substring(7), FunctionId = function.Id, Name = function.Label };
            if (function.NativeAction != null)
            {
                if (function.Targets.Length == 0) throw new InvalidOperationException(I18n.Str("views.incomplete"));
                var output = new KeyEnhancementOutput { Kind = "mapping", MappingName = function.NativeAction, MappingActions = function.Targets.Select(target => target.Path).ToArray(), Device = pad ? "gamepad" : "keyboard" };
                KeyEnhancementInputPlan.ValidateMapping(output);
                return output;
            }
            if (function.Targets.Length == 0)
            {
                var existing = config.CustomFunctions.Concat(config.Rules.Concat(baseline.Rules).SelectMany(item => item.Outputs)).First(output => OutputFunction(output) == function.Id);
                return new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(existing));
            }
            var selectedTarget = function.Targets.First();
            return new KeyEnhancementOutput { Kind = selectedTarget.Kind, Path = selectedTarget.Path, Tag = selectedTarget.Tag, ValueType = selectedTarget.ValueType,
                NativePipeline = selectedTarget.Kind == "action" && KeyEnhancementInputPlan.HasPipeline(selectedTarget.Path),
                Device = pad ? "gamepad" : "keyboard",
                FunctionId = function.FixedKeys == null ? null : function.Id };
        }
        string DefaultTriggerFor(BindingFunction function, KeyEnhancementOutput output = null)
        {
            var target = output ?? CreateOutput(function);
            return target.Kind == "mapping" || target.Kind == "move"
                || target.Kind == "action" && (target.NativePipeline == true || function.Id.StartsWith("custom:", StringComparison.Ordinal)
                    && PackagePath(target.Path) != ChangeArmed.Substring(7)) ? "down" : "press";
        }
        void ApplySpecification(KeyEnhancementRule rule, BindingEntry specification, BindingFunction function, KeyEnhancementOutput output = null)
        {
            if (specification.Rule != null)
            {
                var detail = Copy(specification.Rule);
                rule.Steps = detail.Steps; rule.Watch = detail.Watch; rule.WhenLua = detail.WhenLua; rule.EvaluateLua = detail.EvaluateLua;
                rule.Consume = detail.Consume; rule.ConsumeKeys = detail.ConsumeKeys;
                rule.AxisDirection = detail.AxisDirection; rule.InputGroup = detail.InputGroup;
                if (detail.Trigger == "custom" && specification.Trigger != "custom") rule.EvaluateLua = null;
            }
            rule.Keys = specification.Keys.ToList(); rule.Axis = specification.Axis;
            rule.DefaultTrigger = specification.Trigger == "default";
            rule.Trigger = rule.DefaultTrigger ? DefaultTriggerFor(function, output) : specification.Trigger;
            rule.NormalizeInput();
            rule.Seconds = specification.Seconds; rule.Interval = specification.Interval; rule.Threshold = specification.Threshold;
            rule.Count = specification.Count; rule.Priority = specification.Priority; rule.Enabled = specification.Enabled;
            rule.BypassResources = specification.BypassResources;
            rule.BypassRestrictions = specification.BypassRestrictions; rule.RestrictionBypasses = specification.RestrictionBypasses.ToArray();
            rule.SuperArmor = specification.SuperArmor; rule.Invincible = specification.Invincible;
            rule.DamageMultiplier = specification.DamageMultiplier; rule.StartupSpeed = specification.StartupSpeed; rule.RecoverySpeed = specification.RecoverySpeed;
        }

        public BindingEntry Replace(BindingEntry current, BindingEntry specification)
        {
            Changing();
            var entry = ReplaceCore(current, specification);
            if (specification.KeepNativeTrigger.HasValue) SetNativeTrigger(current.FunctionId, specification.KeepNativeTrigger.Value);
            return entry;
        }
        BindingEntry ReplaceCore(BindingEntry current, BindingEntry specification)
        {
            CheckInput(specification);
            var function = Functions.First(item => item.Id == current.FunctionId);
            if (current.IsNative && function.NativeAction != null && IsPlainBinding(specification) && NativeInput(specification)
                && UsesNativeOverride(function.Id, current))
            {
                var native = EditNative(current.FunctionId);
                native.Keys = native.Keys.Select(key => key == current.Keys[0] ? specification.Keys[0] : key).Distinct().ToList();
                return Entries(function.Id).First(entry => entry.IsRuntimeBinding && entry.Keys.SequenceEqual(specification.Keys));
            }
            if (UsesSaveBinding(function.Id, specification, current))
            {
                session.ReplaceRelationship(function.NativeAction, current.Keys[0], specification.Keys[0], pad, family);
                return Entries(function.Id).First(entry => entry.IsNative && !entry.IsRuntimeBinding && entry.Keys[0] == specification.Keys[0]);
            }
            if (current.IsNative)
            {
                var rule = NewRule(function, specification);
                rule.Suppressions.AddRange(function.Targets.Select(target => new KeyEnhancementSuppression { ActionPath = target.Path, Key = current.Keys[0], FunctionId = function.Id }));
                config.Rules.Add(rule);
                LinkInputVariants(rule);
                return CollectEntries(config, false, function.Id, true).First(entry => entry.RuleId == rule.Id);
            }
            var owner = config.Rules.Single(rule => rule.Id == current.RuleId);
            KeyEnhancementInputPlan.ValidateMapping(owner.Outputs[current.OutputIndex]);
            if (owner.Outputs.Count == 1)
            {
                ApplySpecification(owner, specification, function);
                LinkInputVariants(owner);
                SetOutputValue(owner.Outputs[0], specification.OutputValue ?? function.DefaultOutputValue);
                return CollectEntries(config, false, function.Id, true).First(entry => entry.RuleId == owner.Id);
            }
            var detached = Copy(owner); detached.Id = Guid.NewGuid().ToString("N");
            detached.Outputs = new List<KeyEnhancementOutput> { detached.Outputs[current.OutputIndex] };
            detached.Suppressions = detached.Suppressions.Where(item => OwnsSuppression(function.Id, item)).ToList();
            ApplySpecification(detached, specification, function);
            SetOutputValue(detached.Outputs[0], specification.OutputValue ?? function.DefaultOutputValue);
            RemoveOutput(owner, current.OutputIndex, function.Id);
            config.Rules.Add(detached);
            return CollectEntries(config, false, function.Id, true).First(entry => entry.RuleId == detached.Id);
        }

        static void SetOutputValue(KeyEnhancementOutput output, object value)
        {
            if (output.Kind == "action" || output.Kind == "mapping")
                output.Value = value == null ? null : new JavaScriptSerializer().DeserializeObject(Serialize(value));
        }

        public void Remove(BindingEntry current)
        {
            Changing();
            var function = Functions.First(item => item.Id == current.FunctionId);
            if (current.IsNative)
            {
                if (function.NativeAction != null && (LiveEditing || current.IsRuntimeBinding))
                { EditNative(current.FunctionId).Keys.Remove(current.Keys[0]); return; }
                if (!current.ReadOnly) { session.ChangeRelationship(function.NativeAction, current.Keys[0], false, pad, family); return; }
                config.Suppressions.AddRange(function.Targets.Select(target => new KeyEnhancementSuppression { ActionPath = target.Path, Key = current.Keys[0], FunctionId = function.Id }));
                return;
            }
            RemoveOutput(config.Rules.Single(rule => rule.Id == current.RuleId), current.OutputIndex, function.Id);
        }
        public void Remove(string entryId) { Remove(GetSnapshot().First(entry => entry.Id == entryId)); }
        public void RemoveRelationship(string functionId, string key)
        {
            var affected = Entries(functionId).Where(entry => entry.Active && UsedKeys(entry).Contains(key)).ToArray();
            foreach (var entry in affected.OrderByDescending(item => item.OutputIndex)) Remove(entry);
        }
        public void RestoreKey(string key)
        {
            Changing();
            var keys = pad ? BindingViewText.InputKeys(BindingViewText.PhysicalKey(key)) : new[] { key };
            var planned = config.Clone();
            var workspace = new BindingWorkspace(session, planned, baseline, pad, family);
            var affected = workspace.GetSnapshot().Where(entry => !entry.IsNative && UsedKeys(entry).Intersect(keys).Any()).ToArray();
            foreach (var entry in affected.OrderByDescending(item => item.OutputIndex)) workspace.Remove(entry);
            planned.Suppressions.RemoveAll(item => keys.Contains(item.Key));
            foreach (var rule in planned.Rules) rule.Suppressions.RemoveAll(item => keys.Contains(item.Key));
            foreach (var function in Functions.Where(item => item.NativeAction != null))
            {
                var item = workspace.NativeOverride(function.Id, planned);
                if (item == null && !LiveEditing) continue;
                var relation = session.Relations(function.NativeAction, pad, family);
                var original = item == null ? relation.Keys : item.Keys.ToArray();
                if (!original.Intersect(keys).Any() && !relation.DefaultKeys.Intersect(keys).Any()) continue;
                workspace.EditNative(function.Id).Keys = original.Except(keys).Concat(relation.DefaultKeys.Intersect(keys)).Distinct().ToList();
            }
            if (!LiveEditing && !(config.Enabled && config.Schemes.Count > 0)) session.RestoreKeys(keys, pad, family, config.Enabled ? (Action<string, string>)delegate(string action, string input) {
                workspace.Add(action, new BindingEntry { Keys = new[] { input }, Trigger = "default" });
            } : null);
            config.NativeBindings = planned.NativeBindings; config.Rules = planned.Rules; config.CustomFunctions = planned.CustomFunctions; config.Suppressions = planned.Suppressions;
            functions = null;
        }
        public void SetEnabled(BindingEntry entry, bool enabled)
        {
            Changing();
            if (entry.IsNative)
            {
                if (!enabled)
                {
                    var function = Functions.First(item => item.Id == entry.FunctionId);
                    var rule = NewRule(function, new BindingEntry { Keys = entry.Keys, Trigger = "down", Enabled = false });
                    rule.BlockOriginalWhenDisabled = true;
                    rule.Suppressions.AddRange(function.Targets.Select(target => new KeyEnhancementSuppression { ActionPath = target.Path, Key = entry.Keys[0], FunctionId = function.Id }));
                    config.Rules.Add(rule);
                }
                else Restore(entry);
                return;
            }
            var owner = config.Rules.Single(rule => rule.Id == entry.RuleId);
            if (owner.Outputs.Count == 1) owner.Enabled = enabled;
            else
            {
                var detached = Copy(owner); detached.Id = Guid.NewGuid().ToString("N");
                detached.Outputs = new List<KeyEnhancementOutput> { detached.Outputs[entry.OutputIndex] };
                detached.Suppressions = detached.Suppressions.Where(item => OwnsSuppression(entry.FunctionId, item)).ToList();
                detached.Enabled = enabled;
                RemoveOutput(owner, entry.OutputIndex, entry.FunctionId); config.Rules.Add(detached);
            }
        }
        void RemoveOutput(KeyEnhancementRule owner, int index, string functionId, bool keepFunction = true)
        {
            var removedOutput = owner.Outputs[index];
            if (keepFunction && !string.IsNullOrEmpty(removedOutput.Id) && !config.CustomFunctions.Any(item => item.Id == removedOutput.Id))
                config.CustomFunctions.Add(new JavaScriptSerializer().Deserialize<KeyEnhancementOutput>(Serialize(removedOutput)));
            owner.Outputs.RemoveAt(index);
            if (owner.Outputs.Count == 0) config.Rules.Remove(owner);
            else if (!owner.Outputs.Any(output => OutputFunction(output) == functionId)) owner.Suppressions.RemoveAll(item => OwnsSuppression(functionId, item) && !owner.Outputs.Any(output => OwnsSuppression(OutputFunction(output), item)));
        }
        bool OwnsSuppression(string functionId, KeyEnhancementSuppression suppression)
        {
            if (suppression.FunctionId != null) return suppression.FunctionId == functionId;
            var function = Functions.FirstOrDefault(item => item.Id == functionId);
            return function != null && (function.FixedKeys == null || !string.IsNullOrEmpty(suppression.Key) && function.FixedKeys.Contains(suppression.Key))
                && function.Targets.Any(target => PackagePath(target.Path) == PackagePath(suppression.ActionPath));
        }

        public void Restore(BindingEntry entry)
        {
            Changing();
            if (!entry.IsNative) { SetEnabled(entry, true); return; }
            var function = Functions.First(item => item.Id == entry.FunctionId);
            if (function.NativeAction != null)
            {
                var native = NativeOverride(function.Id, config);
                var keys = native == null ? session.Relations(function.NativeAction, pad, family).Keys : native.Keys.ToArray();
                if (!keys.Contains(entry.Keys[0]))
                {
                    if (config.Enabled || native != null) Add(function.Id, new BindingEntry { Keys = entry.Keys.ToArray(), Trigger = "default" });
                    else session.ChangeRelationship(function.NativeAction, entry.Keys[0], true, pad, family);
                }
            }
            RestoreNativeInput(function, entry.Keys[0]);
        }
        void RestoreNativeInput(BindingFunction function, string key)
        {
            foreach (var rule in config.Rules) rule.Suppressions.RemoveAll(item => Suppresses(function, key, item));
            config.Suppressions.RemoveAll(item => Suppresses(function, key, item));
        }
        static bool Suppresses(BindingFunction function, string key, KeyEnhancementSuppression suppression)
        { return (string.IsNullOrEmpty(suppression.Key) || suppression.Key == key) && function.Targets.Any(target => PackagePath(target.Path) == PackagePath(suppression.ActionPath)); }

        public void Restore(string functionId)
        {
            Changing();
            config.NativeBindings.RemoveAll(item => item.FunctionId == functionId && item.Device == (pad ? "gamepad" : "keyboard"));
            var function = Functions.First(item => item.Id == functionId);
            Func<KeyEnhancementSuppression, bool> owns = item => function.FixedKeys != null ? OwnsSuppression(functionId, item)
                : function.Targets.Any(target => PackagePath(target.Path) == PackagePath(item.ActionPath));
            if (function.NativeAction != null)
            {
                if (LiveEditing || config.Enabled && config.Schemes.Count > 0) EditNative(function.Id).Keys = session.Relations(function.NativeAction, pad, family).DefaultKeys.ToList();
                else session.RestoreAction(function.NativeAction, pad, family);
            }
            foreach (string tag in NativeTriggerTags(function)) config.NativeAbilitySuppressions.RemoveAll(item => item == tag);
            foreach (var rule in config.Rules.Where(DeviceMatches).ToArray())
                for (int i = rule.Outputs.Count - 1; i >= 0; i--)
                    if (OutputFunction(rule.Outputs[i]) == functionId) RemoveOutput(rule, i, functionId);
            config.Suppressions.RemoveAll(item => owns(item) && !string.IsNullOrEmpty(item.Key) && IsPad(item.Key) == pad);
            foreach (var rule in config.Rules.Where(DeviceMatches)) rule.Suppressions.RemoveAll(item => owns(item) && (string.IsNullOrEmpty(item.Key) || IsPad(item.Key) == pad));
        }

        static void CheckInput(BindingEntry specification)
        {
            if (specification == null || specification.Keys == null || specification.Keys.Any(key => string.IsNullOrWhiteSpace(key) || key == "None") ||
                specification.Keys.Length == 0 && string.IsNullOrWhiteSpace(specification.Axis) && (specification.Rule == null || specification.Rule.Trigger != "sequence" && specification.Rule.Trigger != "custom"))
                throw new ArgumentException(I18n.Str("unified.choosekey"));
        }

        internal static IEnumerable<string> UsedKeys(BindingEntry entry)
        {
            return entry.Keys.Concat(string.IsNullOrEmpty(entry.Axis) ? new string[0] : new[] { entry.Axis })
                .Concat(entry.Rule == null ? new string[0] : entry.Rule.Steps.SelectMany(step => step).Concat(entry.Rule.Watch)).Distinct();
        }

        internal BindingSnapshot Snapshot()
        {
            var functions = Functions;
            var entriesAfter = CollectEntries(config, false, null);
            var afterEntries = entriesAfter.ToLookup(entry => entry.FunctionId);
            var beforeEntries = CollectEntries(baseline, true, null).ToLookup(entry => entry.FunctionId);
            Func<BindingFunction, bool, ActionRelations> relation = (function, before) => {
                var entries = (before ? beforeEntries : afterEntries)[function.Id].ToArray();
                var native = function.NativeAction == null ? null : session.Relations(function.NativeAction, pad, family);
                var source = before ? baseline : config;
                bool blocked = NativeTriggerTags(function).Any(tag => source.NativeAbilitySuppressions.Contains(tag));
                return new ActionRelations { Action = function.Id, Keys = entries.Where(entry => entry.Active).SelectMany(UsedKeys).Distinct().ToArray(),
                    DefaultKeys = function.FixedKeys ?? (native == null ? new string[0] : native.DefaultKeys),
                    BindingDetails = entries.Select(BindingEntryText.EntryDescription).Concat(blocked ? new[] { I18n.Str("binding.native.replaced") } : new string[0]).ToArray(),
                    BindingSignature = Serialize(entries.Where(entry => !entry.IsNative || entry.Suppressed || entry.IsRuntimeBinding).Select(entry => new { entry.IsRuntimeBinding, entry.Keys, entry.Axis, entry.Trigger, entry.Seconds, entry.Interval, entry.Count, entry.Threshold, entry.Enabled, entry.Suppressed, entry.Rule })) + blocked,
                    CustomBindings = blocked || entries.Any(entry => !entry.IsNative || entry.Suppressed),
                    Complete = native == null || native.Complete, Changed = entries.Any(entry => entry.Pending) };
            };
            var current = functions.Select(function => relation(function, false)).ToArray();
            return new BindingSnapshot(pad, family, session.ControllerFamilies.Where(item => item.Index == family).Select(item => item.Name).FirstOrDefault(), current,
                functions.ToDictionary(function => function.Id, function => relation(function, true)), entriesAfter);
        }
    }
}
