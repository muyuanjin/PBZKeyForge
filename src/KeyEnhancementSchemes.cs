using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PBZKeyForge
{
    public sealed class KeyEnhancementScheme
    {
        public string Id, Name;
        public bool BuiltIn;
        public List<NativeBindingOverride> NativeBindings = new List<NativeBindingOverride>();
        public List<KeyEnhancementRule> Rules = new List<KeyEnhancementRule>();
        public List<KeyEnhancementSuppression> Suppressions = new List<KeyEnhancementSuppression>();
        public List<string> NativeAbilitySuppressions = new List<string>();
    }

    public sealed partial class KeyEnhancementConfiguration
    {
        public List<KeyEnhancementScheme> Schemes = new List<KeyEnhancementScheme>();
        public List<KeyEnhancementRule> SchemeSwitchBindings = new List<KeyEnhancementRule>();
        public string SelectedSchemeId, ActiveSchemeId;

        public string DeploymentJson()
        {
            if (!Enabled || Schemes.Count == 0) return ToJson();
            StoreSelectedScheme();
            var active = Schemes.First(item => item.Id == ActiveSchemeId);
            return CanonicalJson(new KeyEnhancementConfiguration {
                Enabled = Enabled, RuntimeHotkeys = RuntimeHotkeys,
                NativeBindings = active.NativeBindings, Rules = active.Rules.Concat(SchemeSwitchBindings).ToList(),
                Suppressions = active.Suppressions, NativeAbilitySuppressions = active.NativeAbilitySuppressions,
                CustomFunctions = CustomFunctions, Hooks = Hooks, CustomLua = CustomLua,
                Schemes = Schemes, SchemeSwitchBindings = SchemeSwitchBindings,
                SelectedSchemeId = ActiveSchemeId, ActiveSchemeId = ActiveSchemeId
            });
        }

        internal string DeploymentJson(bool enabled)
        {
            if (enabled == Enabled) return DeploymentJson();
            var copy = Clone(); copy.SetEnabled(enabled); return copy.DeploymentJson();
        }

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            if (Schemes.Count == 0) { Enabled = enabled; return; }
            if (!enabled)
            {
                StoreSelectedScheme(); Enabled = false;
                NativeBindings = new List<NativeBindingOverride>(); Rules = new List<KeyEnhancementRule>();
                Suppressions = new List<KeyEnhancementSuppression>(); NativeAbilitySuppressions = new List<string>();
            }
            else
            {
                var draft = CurrentOnly(); Enabled = true; LoadSelectedScheme();
                Rules.AddRange(draft.Rules); Suppressions.AddRange(draft.Suppressions);
                NativeAbilitySuppressions = NativeAbilitySuppressions.Concat(draft.NativeAbilitySuppressions).Distinct().ToList();
                foreach (var item in draft.NativeBindings)
                {
                    NativeBindings.RemoveAll(existing => existing.FunctionId == item.FunctionId && existing.Device == item.Device);
                    NativeBindings.Add(item);
                }
            }
        }

        public static string SchemeName(KeyEnhancementScheme scheme)
        {
            if (!scheme.BuiltIn && !string.IsNullOrEmpty(scheme.Name)) return scheme.Name;
            return I18n.Str(scheme.Id == "InputUserSettings.Profiles.Default" ? "scheme.default"
                : scheme.Id == "PBZ.KeyProfile.Custom1" ? "scheme.custom1" : "scheme.custom2");
        }

        static bool SwitchRule(KeyEnhancementRule rule)
        { return rule.Outputs.Count > 0 && rule.Outputs.All(output => output.Kind == "scheme"); }

        public KeyEnhancementConfiguration CurrentOnly()
        {
            var current = new KeyEnhancementConfiguration { Enabled = Enabled, RuntimeHotkeys = RuntimeHotkeys,
                NativeBindings = NativeBindings, Rules = Rules.Where(rule => !SwitchRule(rule)).ToList(),
                Suppressions = Suppressions, NativeAbilitySuppressions = NativeAbilitySuppressions,
                CustomFunctions = CustomFunctions, Hooks = Hooks, CustomLua = CustomLua };
            return Serializer().Deserialize<KeyEnhancementConfiguration>(Serializer().Serialize(current));
        }

        public void StoreSelectedScheme()
        {
            if (!Enabled || Schemes == null || Schemes.Count == 0) return;
            var selected = Schemes.FirstOrDefault(item => item.Id == SelectedSchemeId);
            if (selected == null) throw Invalid("SelectedSchemeId");
            SchemeSwitchBindings = Rules.Where(SwitchRule).ToList();
            selected.NativeBindings = NativeBindings;
            selected.Rules = Rules.Where(rule => !SwitchRule(rule)).ToList();
            selected.Suppressions = Suppressions;
            selected.NativeAbilitySuppressions = NativeAbilitySuppressions;
        }

        void LoadSelectedScheme()
        {
            var selected = Schemes.First(item => item.Id == SelectedSchemeId);
            NativeBindings = selected.NativeBindings; Rules = selected.Rules.Concat(SchemeSwitchBindings).ToList();
            Suppressions = selected.Suppressions; NativeAbilitySuppressions = selected.NativeAbilitySuppressions;
        }

        public void SelectScheme(string id)
        {
            if (!Schemes.Any(item => item.Id == id)) throw Invalid("Scheme=" + id);
            StoreSelectedScheme(); SelectedSchemeId = id; LoadSelectedScheme();
        }

        public KeyEnhancementConfiguration ConfigurationFor(string id)
        {
            StoreSelectedScheme();
            var item = Schemes.First(scheme => scheme.Id == id);
            var current = new KeyEnhancementConfiguration { Enabled = Enabled, RuntimeHotkeys = RuntimeHotkeys,
                NativeBindings = item.NativeBindings, Rules = item.Rules.Concat(SchemeSwitchBindings).ToList(),
                Suppressions = item.Suppressions, NativeAbilitySuppressions = item.NativeAbilitySuppressions,
                CustomFunctions = CustomFunctions, Hooks = Hooks, CustomLua = CustomLua };
            return Serializer().Deserialize<KeyEnhancementConfiguration>(Serializer().Serialize(current));
        }

        public void ReplaceCurrent(KeyEnhancementConfiguration value)
        {
            RuntimeHotkeys = value.RuntimeHotkeys;
            NativeBindings = value.NativeBindings; Rules = value.Rules.Where(rule => !SwitchRule(rule)).Concat(SchemeSwitchBindings).ToList();
            Suppressions = value.Suppressions; NativeAbilitySuppressions = value.NativeAbilitySuppressions;
            var definitions = CustomFunctions.ToList();
            foreach (var definition in value.CustomFunctions)
            {
                var existing = definitions.FirstOrDefault(item => item.Id == definition.Id);
                if (existing == null) definitions.Add(definition);
                else if (CanonicalJson(existing) != CanonicalJson(definition))
                    throw new InvalidOperationException(I18n.Str("editing.customconflict", definition.Name ?? definition.Id));
            }
            CustomFunctions = definitions; Hooks = value.Hooks; CustomLua = value.CustomLua;
            StoreSelectedScheme();
        }

        public void EnsureSchemes(EditorSession session)
        {
            if (Schemes.Count > 0) return;
            var inherited = CurrentOnly();
            foreach (string profile in session.Profiles)
            {
                var source = session.CopyForPreset(); source.SwitchProfile(profile);
                var content = inherited.Clone();
                foreach (bool pad in new[] { false, true })
                {
                    var family = source.ControllerFamilies.FirstOrDefault();
                    if (pad && family == null) continue;
                    foreach (string action in source.BindingActions(pad, pad ? family.Index : -1))
                    {
                        string device = pad ? "gamepad" : "keyboard";
                        if (!content.NativeBindings.Any(item => item.FunctionId == action && item.Device == device))
                            content.NativeBindings.Add(new NativeBindingOverride { FunctionId = action, Device = device,
                                Keys = source.Relations(action, pad, pad ? family.Index : -1).Keys.ToList() });
                    }
                }
                Schemes.Add(new KeyEnhancementScheme { Id = profile, BuiltIn = true, Name = SchemeName(new KeyEnhancementScheme { Id = profile, BuiltIn = true }), NativeBindings = content.NativeBindings,
                    Rules = content.Rules, Suppressions = content.Suppressions, NativeAbilitySuppressions = content.NativeAbilitySuppressions });
            }
            SelectedSchemeId = Schemes.Any(item => item.Id == session.SelectedProfile) ? session.SelectedProfile : Schemes[0].Id;
            ActiveSchemeId = Schemes.Any(item => item.Id == session.ActiveProfile) ? session.ActiveProfile : SelectedSchemeId;
            LoadSelectedScheme();
        }

        public string AddScheme(string name)
        {
            if (!Enabled || Schemes.Count == 0) throw new KeyEnhancementRequiredException();
            name = CheckedSchemeName(name); StoreSelectedScheme(); var copy = CurrentOnly();
            string id = "scheme_" + Guid.NewGuid().ToString("N");
            Schemes.Add(new KeyEnhancementScheme { Id = id, Name = name, NativeBindings = copy.NativeBindings,
                Rules = copy.Rules, Suppressions = copy.Suppressions, NativeAbilitySuppressions = copy.NativeAbilitySuppressions });
            SelectScheme(id); return id;
        }

        public void RenameScheme(string id, string name)
        {
            var item = Schemes.First(scheme => scheme.Id == id);
            if (item.BuiltIn) throw Invalid("BuiltIn");
            item.Name = CheckedSchemeName(name, id);
        }

        string CheckedSchemeName(string name, string except = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 80) throw Invalid(I18n.Str("preset.entername"));
            if (Schemes.Any(item => item.Id != except && string.Equals(SchemeName(item), name, StringComparison.CurrentCultureIgnoreCase)))
                throw Invalid(I18n.Str("preset.duplicatename"));
            return name;
        }

        public void DeleteScheme(string id)
        {
            var item = Schemes.First(scheme => scheme.Id == id);
            if (item.BuiltIn || id == ActiveSchemeId) throw Invalid(I18n.Str("schemes.deleteactive"));
            StoreSelectedScheme(); Schemes.Remove(item);
            foreach (var rule in SchemeSwitchBindings.ToArray())
            { rule.Outputs.RemoveAll(output => output.Kind == "scheme" && output.Tag == id); if (rule.Outputs.Count == 0) SchemeSwitchBindings.Remove(rule); }
            if (SelectedSchemeId == id) SelectedSchemeId = Schemes[0].Id;
            LoadSelectedScheme();
        }

        void ValidateSchemes()
        {
            if (Schemes == null || SchemeSwitchBindings == null) throw Invalid("Schemes");
            if (Schemes.Count == 0) return;
            if (Schemes.Any(item => item == null || string.IsNullOrWhiteSpace(item.Id) || !Regex.IsMatch(item.Id, "^[A-Za-z0-9_.]+$")
                || item.NativeBindings == null || item.Rules == null || item.Suppressions == null || item.NativeAbilitySuppressions == null)
                || Schemes.GroupBy(item => item.Id).Any(group => group.Count() > 1)
                || !Schemes.Any(item => item.Id == SelectedSchemeId) || !Schemes.Any(item => item.Id == ActiveSchemeId)) throw Invalid("Schemes");
            foreach (var rule in Rules.Concat(SchemeSwitchBindings).Concat(Schemes.SelectMany(item => item.Rules)))
                foreach (var output in rule.Outputs.Where(output => output.Kind == "scheme"))
                    if (rule.Outputs.Any(item => item.Kind != "scheme") || output.Tag != "next" && output.Tag != "previous" && !Schemes.Any(item => item.Id == output.Tag))
                        throw Invalid("Scheme output");
        }
    }
}
