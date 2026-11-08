using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PBZKeyForge;

#if KEY_ENHANCEMENT_STANDALONE
namespace PBZKeyForge
{
    internal static class I18n
    {
        public static string Str(string key, params object[] values) { return key + ": " + string.Join(",", values); }
    }
}
#endif

class KeyEnhancementConfigurationTests
{
    static int assertions;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static void Main(string[] args)
    {
        var config = new KeyEnhancementConfiguration();
        Check(!config.Enabled && config.Rules.Count == 0, "Default must be disabled and empty.");
        Check(!config.RuntimeHotkeys && config.CompileLua().Contains("[\"runtime_hotkeys\"]=false"), "Default must not reserve F7/F8.");
        Check(config.CompileLua().Contains("[\"unified_native\"]=true") && KeyEnhancementInputPlan.Actions.Length == 40,
            "The compiler must request runtime ownership of registered gameplay input.");
        Check(KeyEnhancementInputPlan.Covers("IA_Execute") && !KeyEnhancementInputPlan.Covers("IA_Jump", "MouseScrollUp")
            && KeyEnhancementInputPlan.Retained("IA_Map"), "Gameplay, unsupported source and native menu domains were confused.");
        Check(KeyEnhancementCatalog.Targets.Count(t => t.Kind == "action") == 98, "Action catalog incomplete.");
        Check(KeyEnhancementCatalog.Targets.Count(t => t.Kind == "ability") == 227, "Ability catalog incomplete.");
        Check(KeyEnhancementCatalog.ForMapping("IA_Guard").Length == 2 && KeyEnhancementCatalog.ForMapping("IA_LightAttack").Length == 3,
            "Player-mappable identities must resolve every native action.");
        Check(KeyEnhancementCatalog.ForMapping("IA_Jump").Any(entry => entry.Name == "IA_Interaction_SpecialMove"),
            "Context-specific native actions must remain discoverable.");
        config.Enabled = true;
        var rule = new KeyEnhancementRule { Id = "replace", Trigger = "hold", Seconds = .4 };
        rule.Keys.Add("K");
        rule.Outputs.Add(new KeyEnhancementOutput { Path = "/Input/IA_Jump.IA_Jump" });
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "function", Path = "/Script/Engine.Character:Jump", Args = new object[] { "quote\" slash\\ line\n", new Dictionary<string, object> { { "value", true } } } });
        rule.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Input/IA_Jump.IA_Jump", Key = "SpaceBar" });
        config.Rules.Add(rule);
        config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Input/IA_Disabled.IA_Disabled" });
        config.Hooks.Add(new Dictionary<string, object> { { "path", "/Script/Test:Object" }, { "value", false } });
        config.CustomLua = "config.marker = 'preserved'";
        var copy = config.Clone();
        Check(copy.ToJson() == config.ToJson(), "JSON round trip lost configuration.");
        copy.Rules[0].Keys[0] = "J";
        Check(config.Rules[0].Keys[0] == "K", "Clone shares editable lists.");
        string lua = config.CompileLua();
        Check(lua.Contains("SpaceBar") && lua.Contains("IA_Disabled"), "Replacement and independent suppression missing.");
        rule.Enabled = false;
        string disabled = config.CompileLua();
        Check(!disabled.Contains("SpaceBar") && disabled.Contains("IA_Disabled"), "Disabled rule kept its replacement suppression.");
        rule.Enabled = true;
        rule.Trigger = "custom";
        bool rejected = false;
        try { config.CompileLua(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Custom trigger without evaluator was accepted.");
        rule.EvaluateLua = "function(now, snapshot) local active=snapshot.K==1; return {active=active,pulse=active,value=active and 1 or 0} end";
        rule.WhenLua = "function(context) return context.gameplay end";
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "property", Property = "bFlag", Value = false });
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "lua", Code = "function(backend,event) return event.binding end" });
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "ability", Tag = "Ability.Action.Test", InputAction = "/Input/IA_Jump.IA_Jump" });
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "event", Tag = "GameplayEvent.Test", Payload = new Dictionary<string, object> { { "EventMagnitude", 2 } } });
        config.Hooks[0]["evaluate"] = "function() return false end";
        lua = config.CompileLua();
        if (args.Length > 0) File.WriteAllText(args[0], lua, new System.Text.UTF8Encoding(false));
        Check(lua.Contains("[\"value\"]=false") && lua.Contains("function(now, snapshot)"), "Typed or custom target compilation lost values.");
        var nativeDash = new KeyEnhancementOutput { Kind = "ability", Tag = "Ability.Action.Hun.SpecialDash" };
        rule.Outputs.Add(nativeDash);
        string nativeDashLua = config.CompileLua();
        Check(nativeDashLua.Contains("[\"kind\"]=\"ability\"")
            && nativeDashLua.Contains("[\"tag\"]=\"Ability.Action.Hun.SpecialDash\"")
            && !nativeDashLua.Contains("[\"mapping\"]=\"IA_Dodge\""),
            "SpecialDash lost its exact ability identity or was replaced with the generic dodge action.");
        rule.Outputs.Remove(nativeDash);
        Check(KeyEnhancementConfiguration.Triggers.Length == 11, "Trigger menu must cover ten built-ins and custom.");
        copy = config.Clone();
        copy.Rules.Add(new KeyEnhancementRule { Id = copy.Rules[0].Id, Enabled = false });
        rejected = false;
        try { KeyEnhancementConfiguration.FromJson(copy.ToJson()); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Disabled duplicate rule ID was accepted.");
        Check(new KeyEnhancementOutput().ValueType == null && KeyEnhancementCatalog.Targets.First(t => t.Name == "IA_MoveInput").ValueType == null,
            "Axis actions must preserve their native values when the catalog has no type metadata.");
        config.NativeAbilitySuppressions.Add("Ability.Action.Hun.SpecialDash");
        config.NativeAbilitySuppressions.Add("Ability.Action.Hun.DeflectDash");
        var direction = new Dictionary<string, object> { { "x", 0 }, { "y", -2 }, { "z", 0 } };
        rule.Outputs.Add(new KeyEnhancementOutput { Kind = "mapping", MappingName = "IA_Jump", MappingActions = KeyEnhancementCatalog.ForMapping("IA_Jump").Select(target => target.Path).ToArray(), Value = direction });
        rule.Outputs[0].Value = new Dictionary<string, object> { { "x", -1 }, { "y", 0 }, { "z", 0 } };
        copy = config.Clone();
        Check(copy.NativeAbilitySuppressions.SequenceEqual(config.NativeAbilitySuppressions), "Native ability suppressions lost on JSON round trip.");
        lua = config.CompileLua();
        if (args.Length > 0) File.WriteAllText(args[0], lua, new System.Text.UTF8Encoding(false));
        Check(lua.Contains("[\"ability_suppressions\"]={\"Ability.Action.Hun.SpecialDash\",\"Ability.Action.Hun.DeflectDash\"}"),
            "Native ability suppressions were not compiled.");
        Check(lua.Contains("[\"value\"]={[\"x\"]=0,[\"y\"]=-2,[\"z\"]=0}"), "Mapping output direction was not compiled.");
        copy.Hooks.Add(new Dictionary<string, object> { { "path", "/Script/Test:Object" }, { "value", true } });
        rejected = false;
        try { copy.CompileLua(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Duplicate hooks survived configuration compilation.");
        var identities = new KeyEnhancementConfiguration { Enabled = true };
        foreach (var ability in KeyEnhancementCatalog.Targets.Where(target => target.Kind == "ability"))
        {
            var identity = new KeyEnhancementRule { Id = "identity_" + identities.Rules.Count, Trigger = "press" };
            identity.Keys.Add("F9");
            identity.Outputs.Add(new KeyEnhancementOutput { Kind = "ability", Tag = ability.Tag });
            identities.Rules.Add(identity);
        }
        int abilityCount = KeyEnhancementCatalog.Targets.Count(target => target.Kind == "ability");
        string identityLua = identities.CompileLua();
        int compiledAbilities = System.Text.RegularExpressions.Regex.Matches(identityLua, "\\[\"kind\"\\]=\"ability\"").Count;
        Check(compiledAbilities == abilityCount,
            "Ability identity changed during compilation: expected " + abilityCount + " exact ability targets, got " + compiledAbilities + ".");
        Console.WriteLine("KeyEnhancementConfigurationTests: " + assertions + " assertions passed");
    }
}
