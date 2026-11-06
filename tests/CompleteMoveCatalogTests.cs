using System;
using System.IO;
using System.Linq;
using PBZKeyForge;

class CompleteMoveCatalogTests
{
    const string MoveId = "move:Ability.Action.Hun.Heavy.BronzeSword.BHold.Release2";
    const string AbilityId = "ability:Ability.Action.Hun.Heavy.BronzeSword.BHold";
    static int checks;

    static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }

    static void CheckOutputs(KeyEnhancementConfiguration config)
    {
        var outputs = config.Rules.SelectMany(rule => rule.Outputs).ToArray();
        Check(outputs.Length == 2 && outputs.All(output => output.Kind == "move" && output.Tag == MoveId), "Move identities changed during persistence.");
        Check(config.Rules.All(rule => rule.Trigger == "press" && rule.Outputs.Count == 1), "One binding must directly request one complete move.");
        string lua = config.CompileLua();
        Check(lua.Contains("require(\"complete-moves\")\n"), "An older runtime must reject a complete-move configuration before applying it.");
        Check(lua.Contains("[\"kind\"]=\"move\"") && lua.Contains("[\"move_id\"]=\"" + MoveId + "\""), "Compiler lost complete-move dispatch.");
        Check(lua.Contains("[\"tag\"]=\"Ability.Action.Hun.Heavy.BronzeSword.BHold\"") && lua.Contains("GA_Hun_BronzeSword_B_Hold_C:EndSuccess"), "Compiler lost the native ability or release entry point.");
        Check(lua.Contains("[\"input_action\"]=\"/PBZInput/InputActions/IA_AttackH_PressLong.IA_AttackH_PressLong\"") && !lua.Contains("[\"force\"]=true"), "A complete move must keep its native input source and normal activation.");
        Check(lua.Contains("[\"required_tags\"]={\"CharacterState.Space.Ground\",\"Weapon.Type.BronzeSword\"}") && lua.Contains("[\"priority\"]=3"), "The complete move must preserve its ground and equipment requirements and native input priority.");
    }

    static void Run(string outputDirectory)
    {
        I18n.Current = "zh-Hans";
        var session = EditorSession.CreateOffline();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var originalSave = session.ExportCurrentScheme();
        foreach (bool pad in new[] { false, true })
        {
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            var move = workspace.Functions.Single(function => function.Id == MoveId);
            var ability = workspace.Functions.Single(function => function.Id == AbilityId);
            Check(move.Label == "天玄青铜剑 · 重攻击 · 蓄力 · 释放" && move.IdentityCode == "BHold · Release2", "Complete move must follow its weapon's action and stage naming.");
            Check(move.RequiresKeyEnhancement && move.GroupPath.SequenceEqual(ability.GroupPath) && move.LabelSource == "semantic", "Complete move lost its weapon classification or label provenance.");
            Check(new[] { "BHold", "Release2", "蓄力释放" }.All(query => workspace.FindFunctions(query).Any(function => function.Id == MoveId)), "The move is missing its action and stage search names.");
            var entry = workspace.Add(MoveId, new BindingEntry { Keys = new[] { pad ? "Gamepad_DPad_Down" : "G" } });
            Check(entry.FunctionId == MoveId && entry.Trigger == "press" && entry.Active && !entry.IsNative, "A normal binding must select complete-move execution without a hold rule.");
            Check(workspace.Entries(AbilityId).All(item => !item.Keys.Contains(pad ? "Gamepad_DPad_Down" : "G")), "Binding a complete move must not add a second ability binding.");
        }
        Check(session.ExportCurrentScheme().SequenceEqual(originalSave), "Complete-move bindings must not edit game save bytes.");
        var completeWorkspace = new BindingWorkspace(session, new KeyEnhancementConfiguration { Enabled = true }, config.Clone(), false, -1);
        foreach (var candidate in new[] {
            new[] { "Ability.Action.Hun.Light.Air.AA", "轻攻击 · 空中 · 2", "CharacterState.Space.Air", "IA_AttackL_Press" },
            new[] { "Ability.Action.Hun.Light.Air.AAA", "轻攻击 · 空中 · 3", "CharacterState.Space.Air", "IA_AttackL_Press" },
            new[] { "Ability.Action.Hun.Heavy.ComboAtk_N3H3", "重攻击 · 派生 · N3H3", "CharacterState.Space.Ground", "IA_AttackH_Press" }
        })
        {
            string id = "move:" + candidate[0];
            Check(completeWorkspace.Functions.Single(function => function.Id == id).Label == candidate[1], "The missing combo stage must follow existing action naming: " + id);
            completeWorkspace.Add(id, new BindingEntry { Keys = new[] { "H" } });
        }
        string completeLua = completeWorkspace.Configuration.CompileLua();
        Check(completeLua.Contains("Ability.Action.Hun.Light.Air.AA") && completeLua.Contains("Ability.Action.Hun.Light.Air.AAA")
            && completeLua.Contains("Ability.Action.Hun.Heavy.ComboAtk_N3H3") && !completeLua.Contains("entry_point"),
            "Self-contained combo stages must activate normally without a synthetic release entry.");
        Check(completeLua.Contains("CharacterState.Space.Air") && completeLua.Contains("CharacterState.Space.Ground")
            && completeLua.Contains("IA_AttackL_Press.IA_AttackL_Press") && completeLua.Contains("IA_AttackH_Press.IA_AttackH_Press"),
            "Self-contained combo stages lost their native space and input qualification.");
        var fireConfig = new KeyEnhancementConfiguration { Enabled = true };
        var fireWorkspace = new BindingWorkspace(session, fireConfig, fireConfig.Clone(), false, -1);
        const string fireId = "move:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA.Final";
        Check(fireWorkspace.Functions.Single(function => function.Id == fireId).Label == "火流星 · 攻击 · AAAA · 释放",
            "A Phantom Edge release must follow the existing weapon attack naming.");
        fireWorkspace.Add(fireId, new BindingEntry { Keys = new[] { "J" } });
        string fireLua = fireConfig.CompileLua();
        Check(fireLua.Contains("GA_Hun_HuoLiuXing_AAAA_C:ToRelease") && fireLua.Contains("CharacterState.WeaponUpgrade.HuoLiuXing.AAAA")
            && fireLua.Contains("Weapon.Type.ShaqiWeapon.HuoLiuXing")
            && fireLua.Contains("[\"active_boolean_properties\"]={\"Current Shachi Reinforced\"}"),
            "The Phantom Edge release must keep its unlock and the native activation's resource-qualified branch.");
        Check(!fireLua.Contains("[\"required_tags\"]={\"Ability.Action.ShaqiReinforced\"}")
            && fireLua.Contains("IA_ShachiAbility.IA_ShachiAbility"), "A complete combo release must not require the preceding combo activation.");
        var chargeConfig = new KeyEnhancementConfiguration { Enabled = true };
        var chargeWorkspace = new BindingWorkspace(session, chargeConfig, chargeConfig.Clone(), false, -1);
        const string chargeId = "move:Ability.Action.Hun.Heavy.GuiTouDao.BHold.Release";
        Check(chargeWorkspace.Functions.Single(function => function.Id == chargeId).Label == "红妆鬼斩 · 重攻击 · 蓄力 · 释放",
            "The charge selection callback must follow its weapon's existing move naming.");
        chargeWorkspace.Add(chargeId, new BindingEntry { Keys = new[] { "K" } });
        string chargeLua = chargeConfig.CompileLua();
        Check(chargeLua.Contains("GA_Hun_GuiTouBlade_B_Hold_Start_C:EventReceived_7594CBDE4CE41BEBFC9ABFBA4E2D1DF1")
            && chargeLua.Contains("[\"entry_arguments\"]={{}}") && chargeLua.Contains("CharacterState.WeaponUpgrade.GuiTouBlade.CanCharge")
            && chargeLua.Contains("Weapon.Type.GuiTouBlade")
            && chargeLua.Contains("[\"ignored_tags\"]={\"CharacterState.AbilityState.GuiTouBlade.InBHold\"}"),
            "The charge release must keep its real callback payload, unlock and excluded native state.");
        CheckOutputs(config);
        CheckOutputs(KeyEnhancementConfiguration.FromJson(config.ToJson()));
        var preset = UserPresetStore.Capture(session, "蓄力释放", config);
        CheckOutputs(UserPresetStore.ParseShare(UserPresetStore.Share(preset)).KeyEnhancement);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "complete-move.lua"), config.CompileLua(), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "fire-move.lua"), fireLua, new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "charge-move.lua"), chargeLua, new System.Text.UTF8Encoding(false));
        string bundle = Path.Combine(outputDirectory, "complete-move.zip");
        new BindingDocument { Session = session, Configuration = config }.Export(bundle);
        CheckOutputs(BindingDocument.Open(bundle).Configuration);
        var unknown = config.Clone(); unknown.Rules[0].Outputs[0].Tag = "move:unknown";
        Reject(delegate { unknown.CompileLua(); }, "An unknown complete move was accepted.");
        var forced = config.Clone(); forced.Rules[0].Outputs[0].Force = true;
        Reject(delegate { forced.CompileLua(); }, "A complete move accepted forced activation.");
    }

    static int Main(string[] args)
    {
        try
        {
            Run(args.Length == 0 ? Path.Combine("dist", "complete-move-catalog-check") : args[0]);
            Console.WriteLine("CompleteMoveCatalogTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
