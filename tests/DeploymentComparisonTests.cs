using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PBZKeyForge;

public static class DeploymentComparisonTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    static string Reference(KeyEnhancementConfiguration configuration)
    {
        if (!configuration.Enabled || configuration.Schemes.Count == 0) return configuration.ToJson();
        var copy = configuration.Clone(); copy.SelectScheme(copy.ActiveSchemeId); return copy.ToJson();
    }

    static void Compare(KeyEnhancementConfiguration configuration, string context)
    {
        string before = configuration.ToJson();
        Check(configuration.DeploymentJson() == Reference(configuration), context + ": deployment representation changed.");
        Check(configuration.ToJson() == before, context + ": comparing deployment changed the edited scheme.");
        var method = typeof(KeyEnhancementConfiguration).GetMethod("DeploymentJson", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null);
        foreach (bool enabled in new[] { false, true })
        {
            var reference = configuration.Clone(); reference.SetEnabled(enabled);
            Check((string)method.Invoke(configuration, new object[] { enabled }) == Reference(reference), context + ": enabled override lost edits.");
            Check(configuration.ToJson() == before, context + ": enabled override mutated the source.");
        }
    }

    public static int Main()
    {
        try
        {
            I18n.Current = "zh-Hans";
            var session = EditorSession.CreateOffline();
            var configuration = new KeyEnhancementConfiguration();
            Compare(configuration, "empty disabled configuration");
            configuration.SetEnabled(true); Compare(configuration, "enabled without schemes");
            configuration.EnsureSchemes(session); Compare(configuration, "factory scheme library");
            string original = configuration.SelectedSchemeId;
            configuration.SelectScheme(configuration.Schemes.Last().Id);
            Compare(configuration, "browsing a scheme different from the active scheme");
            string clean = configuration.DeploymentJson();
            var workspace = new BindingWorkspace(session, configuration, configuration.Clone(), false, -1);
            workspace.Add("IA_Jump", new BindingEntry { Keys = new[] { "F9" }, Trigger = "hold" });
            workspace.Add("ability:Ability.Action.Hun.SpecialDash", new BindingEntry { Keys = new[] { "G" }, Trigger = "press" });
            Check(configuration.DeploymentJson() != clean, "An inactive scheme edit was omitted from deployment.");
            Compare(configuration, "native and enhanced bindings in an inactive scheme");
            workspace.Add("scheme:next", new BindingEntry { Keys = new[] { "F6" }, Trigger = "press" });
            configuration.RuntimeHotkeys = true;
            configuration.CustomLua = "config.example = true";
            configuration.CustomFunctions.Add(new KeyEnhancementOutput { Id = "custom_jump", Name = "自定义跳跃",
                Kind = "function", Path = "/Script/Engine.Character:Jump", Args = new object[] { 7 } });
            configuration.Hooks.Add(new Dictionary<string, object> { { "path", "/Script/Test:Object" }, { "value", true } });
            configuration.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = "/Input/IA_Jump.IA_Jump", Key = "SpaceBar" });
            configuration.NativeAbilitySuppressions.Add("Ability.Action.Hun.SpecialDash");
            Compare(configuration, "shared settings and suppressions");
            configuration.AddScheme("新方案"); configuration.RenameScheme(configuration.SelectedSchemeId, "自定义方案");
            configuration.ActiveSchemeId = configuration.SelectedSchemeId;
            Compare(configuration, "scheme metadata and active scheme change");
            configuration.SetEnabled(false); Compare(configuration, "disabled retained library");
            var ordinary = new BindingWorkspace(session, configuration, configuration.Clone(), false, -1);
            ordinary.Add("ability:Ability.Action.Hun.SpecialDash", new BindingEntry { Keys = new[] { "H" }, Trigger = "hold" });
            Compare(configuration, "enhanced draft created while the library is disabled");
            configuration.SetEnabled(true); configuration.SelectScheme(original);
            Compare(configuration, "reenabled library with retained drafts");
            Console.WriteLine("DeploymentComparisonTests PASS " + checks);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
