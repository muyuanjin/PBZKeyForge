using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using PBZKeyForge;

class NativeStatePreparationTests
{
    public sealed class Effect
    {
        public string effectClass, qualificationTag, equipmentTag;
        public string[] requiredBypassKinds;
        public RequirementScope[] requirementScopes;
        public string[] replacedEffectClasses;
    }
    public sealed class RequirementScope { public string effectClass; public bool application, removal; }
    public sealed class Target
    {
        public Effect[] native_state_effects;
        public bool snake_projectile;
        public bool snake_normal;
        public Dictionary<string, object> native_activation;
    }
    public sealed class CatalogEntry { public Dictionary<string, object> preparation; }
    public sealed class CatalogDocument { public Dictionary<string, CatalogEntry> entries; }
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16777216 };
    const string Snake = "move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_RA";
    const string Yuu = "move:Weapon.Weapon_Hun_YuuBlade.GA_Hun_YuuBlade_B";
    const string YuuFinal = "move:GA_Hun_YuuBlade_C.CombatArt.Final";
    static readonly string[] EffectIdentities = LoadIdentities();
    static readonly string[] Identities = EffectIdentities.Concat(new[] { Snake }).ToArray();
    static int checks;
    static string[] LoadIdentities()
    {
        using (var stream = typeof(KeyEnhancementConfiguration).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.move-restrictions.json"))
        using (var reader = new StreamReader(stream))
            return Json.Deserialize<CatalogDocument>(reader.ReadToEnd()).entries
                .Where(pair => pair.Value.preparation.ContainsKey("nativeStateEffects"))
                .Select(pair => pair.Key).OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
    }
    static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception(reason); }
    static Target Apply(string identity, bool restrictions)
    {
        var target = new Dictionary<string, object>();
        var type = typeof(KeyEnhancementConfiguration).Assembly.GetType("PBZKeyForge.MoveRestrictionCatalog");
        type.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
            new object[] { target, new KeyEnhancementOutput { Kind = identity.StartsWith("ability:") ? "ability" : "move",
                Tag = identity.StartsWith("ability:") ? identity.Substring(8) : identity }, restrictions });
        return Json.Deserialize<Target>(Json.Serialize(target));
    }
    static void Catalog()
    {
        var source = Json.Deserialize<CatalogDocument>(File.ReadAllText("assets/key-enhancement/move-restrictions.json"));
        var expected = source.entries.Where(pair => pair.Value.preparation.ContainsKey("nativeStateEffects"))
            .Select(pair => pair.Key).OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
        Check(EffectIdentities.SequenceEqual(expected), "The built catalog omitted a native preparation from the current source");
        foreach (var identity in EffectIdentities)
        {
            var target = Apply(identity, true);
            Check(target.native_state_effects != null && target.native_state_effects.Length > 0,
                identity + " omitted its original native effect");
            foreach (var effect in target.native_state_effects)
            {
                Check((effect.effectClass.StartsWith("/PBZCombatAsset/") && effect.effectClass.EndsWith("_C")
                    || effect.effectClass.StartsWith("/Script/"))
                    && effect.equipmentTag.StartsWith("Weapon.Type.") && effect.qualificationTag.Length > 0,
                    identity + " omitted effect ownership or native qualification");
                Check(effect.requiredBypassKinds.Length == 1, identity + " merged independent restriction choices");
                if (effect.effectClass.EndsWith("GE_Weapon_Long_Buff_Lv3_C"))
                    Check(effect.requirementScopes != null && effect.requirementScopes.Length == 1
                        && effect.requirementScopes[0].application && !effect.requirementScopes[0].removal
                        && effect.replacedEffectClasses != null && effect.replacedEffectClasses.Length == 2
                        && effect.replacedEffectClasses[0].EndsWith("GE_Weapon_Long_Buff_Lv1_C")
                        && effect.replacedEffectClasses[1].EndsWith("GE_Weapon_Long_Buff_Lv2_C"),
                        identity + " lost native application or original tier replacement");
            }
            Check(Apply(identity, false).native_state_effects == null,
                identity + " prepared a restricted native state with the master disabled");
        }
        var ordinary = Apply(Yuu, true).native_state_effects.Single();
        var final = Apply(YuuFinal, true);
        Check(ordinary.requiredBypassKinds.SequenceEqual(new[] { "predecessor" })
            && final.native_state_effects.Single().requiredBypassKinds.SequenceEqual(new[] { "stance" }),
            "Ordinary YuuBlade B and native Exit need distinct preparation choices");
        Check(final.native_activation != null && Convert.ToString(final.native_activation["abilityClass"])
            .EndsWith("GA_Hun_YuuBlade_C.GA_Hun_YuuBlade_C_C"), "YuuBlade Exit omitted normal C activation");
        Check(Apply(Snake, true).snake_projectile && !Apply(Snake, false).snake_projectile,
            "SnakeDual projectile preparation must follow its restriction master");
        Check(!Apply("move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_B", true).snake_projectile,
            "Ordinary SnakeDual B must retain the native normal branch");
        Check(Apply("move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_B", true).snake_normal
            && !Apply("move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_B", false).snake_normal,
            "SnakeDual normal state must use native retrieval only with bypass enabled");
    }
    static void Persistence(string directory)
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        foreach (bool pad in new[] { false, true })
        for (int index = 0; index < Identities.Length; index++)
        {
            var rule = new KeyEnhancementRule { Id = (pad ? "pad-" : "keyboard-") + index,
                BypassRestrictions = true, RestrictionBypasses = new[] { "stance", "predecessor" },
                BypassResources = pad, DamageMultiplier = pad ? 2.5 : 1.5,
                StartupSpeed = 1.25, RecoverySpeed = 1.75, Trigger = "down", DefaultTrigger = true };
            rule.Keys.Add(pad ? "Gamepad_FaceButton_Left" : "F9");
            string identity = Identities[index];
            rule.Outputs.Add(new KeyEnhancementOutput { Kind = identity.StartsWith("ability:") ? "ability" : "move",
                Tag = identity.StartsWith("ability:") ? identity.Substring(8) : identity, Device = pad ? "gamepad" : "keyboard" });
            config.Rules.Add(rule);
        }
        var path = Path.Combine(directory, "native-state-effects.json");
        File.WriteAllText(path, config.ToJson());
        var restored = KeyEnhancementConfiguration.FromJson(File.ReadAllText(path));
        var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(
            EditorSession.CreateOffline(), "Native state preparations", restored))).KeyEnhancement;
        Check(shared.Rules.Count == Identities.Length * 2, "Save/share lost a device or native state binding");
        foreach (var rule in shared.Rules)
        {
            bool pad = rule.Outputs.Single().Device == "gamepad";
            Check(rule.BypassRestrictions && rule.RestrictionBypasses.SequenceEqual(new[] { "stance", "predecessor" })
                && rule.BypassResources == pad && rule.DamageMultiplier == (pad ? 2.5 : 1.5)
                && rule.StartupSpeed == 1.25 && rule.RecoverySpeed == 1.75,
                "Save/share changed independent bypass or attack tuning");
            Check(rule.Keys.Single() == (pad ? "Gamepad_FaceButton_Left" : "F9"), "Save/share changed the input device");
        }
        File.WriteAllText(Path.Combine(directory, "native-state-effects.lua"), shared.CompileLua());
        File.WriteAllLines(Path.Combine(directory, "native-state-effects.txt"), Identities);
    }
    static int Main(string[] args)
    {
        try
        {
            I18n.Current = "zh-Hans";
            string directory = Path.GetFullPath(args.FirstOrDefault() ?? "dist/native-state-preparation-check");
            Directory.CreateDirectory(directory);
            Catalog(); Persistence(directory);
            Console.WriteLine("NativeStatePreparationTests PASS " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
