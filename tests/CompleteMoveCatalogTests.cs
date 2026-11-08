using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
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
        Check(config.Rules.All(rule => rule.Trigger == "down" && rule.Outputs.Count == 1), "Default complete-move bindings must keep requesting native activation while held.");
        Check(config.Rules.All(rule => rule.RestrictionBypasses.SequenceEqual(new[] { "equipment", "training", "space", "stance", "predecessor", "cooldown" })),
            "Complete-move sharing must preserve the default restriction selections.");
        string lua = config.CompileLua();
        Check(lua.Contains("[\"kind\"]=\"move\"") && lua.Contains("[\"move_id\"]=\"" + MoveId + "\""), "Compiler lost complete-move dispatch.");
        Check(lua.Contains("[\"tag\"]=\"Ability.Action.Hun.Heavy.BronzeSword.BHold\"") && lua.Contains("GA_Hun_BronzeSword_B_Hold_C:EndSuccess"), "Compiler lost the native ability or release entry point.");
        Check(lua.Contains("[\"input_action\"]=\"/PBZInput/InputActions/IA_AttackH_PressLong.IA_AttackH_PressLong\"") && !lua.Contains("[\"force\"]=true"), "A complete move must keep its native input source and normal activation.");
        Check(lua.Contains("[\"required_tags\"]={\"CharacterState.Space.Ground\",\"Weapon.Type.BronzeSword\"}") && lua.Contains("[\"priority\"]=3"), "The complete move must preserve its ground and equipment requirements and native input priority.");
    }

    static void CheckMissingSource(EditorSession session)
    {
        const string standaloneId = "move:Weapon.Weapon_Hun_TianXuanQingTongJian.GA_Hun_Air_Tang_AA";
        var moves = (IDictionary)typeof(CompleteMoveCatalog).GetField("Moves", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        object move = moves[standaloneId];
        var sourceField = move.GetType().GetField("sourceId");
        object originalSource = sourceField.GetValue(move);
        try
        {
            foreach (string source in new string[] { null, "" })
            {
                sourceField.SetValue(move, source);
                Check(CompleteMoveCatalog.SourceId(standaloneId) == standaloneId,
                    "A move without a source must use its own non-null lookup identity.");
                foreach (bool pad in new[] { false, true })
                {
                    var config = new KeyEnhancementConfiguration { Enabled = true };
                    var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
                    var standalone = workspace.Functions.Single(function => function.Id == standaloneId);
                    Check(standalone.Label == "天玄青铜剑 · 轻攻击 · AA"
                        && standalone.GroupPath.SequenceEqual(new[] { "weapons", "Weapon_007", "IA_LightAttack" })
                        && standalone.LabelSource == "semantic",
                        "A move without a source must retain its own weapon label and category on both devices.");
                }
            }
        }
        finally { sourceField.SetValue(move, originalSource); }
    }

    static void Run(string outputDirectory)
    {
        I18n.Current = "zh-Hans";
        Check(CompleteMoveCatalog.SourceId(MoveId) == AbilityId,
            "A move with an explicit source must preserve its ability identity.");
        Check(CompleteMoveCatalog.Targets.All(target => !string.IsNullOrEmpty(CompleteMoveCatalog.SourceId(target.Id))),
            "Every catalog move must provide a non-empty lookup identity.");
        var session = EditorSession.CreateOffline();
        CheckMissingSource(session);
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
            Check(entry.FunctionId == MoveId && entry.Trigger == "default" && entry.Active && !entry.IsNative, "A normal binding must select the complete move's continuous default without a hold threshold.");
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
        Check(fireWorkspace.Functions.Single(function => function.Id == fireId).Label == "火流星 · 铁花飞溅 · 攻击 · 释放",
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
        Check(chargeWorkspace.Functions.Single(function => function.Id == chargeId).Label == "红妆鬼斩 · 火照黄泉 · 蓄力 · 释放",
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
        var explicitPress = KeyEnhancementConfiguration.FromJson("{\"Enabled\":true,\"Rules\":[{\"Id\":\"explicit-move\",\"Trigger\":\"press\",\"Keys\":[\"G\"],\"Outputs\":[{\"Kind\":\"move\",\"Tag\":\"" + MoveId + "\"}]}]}");
        Check(explicitPress.Rules.Single().Trigger == "press"
            && explicitPress.CompileLua().Contains("[\"trigger\"]=\"press\""),
            "JSON must preserve the selected on-press move trigger.");
        var once = new KeyEnhancementConfiguration { Enabled = true };
        var onceWorkspace = new BindingWorkspace(session, once, once.Clone(), false, -1);
        var onceEntry = onceWorkspace.Add(MoveId, new BindingEntry { Keys = new[] { "G" }, Trigger = "press" });
        Check(onceEntry.Trigger == "press" && once.Rules.Single().DefaultTrigger == false,
            "An explicitly selected press must remain distinct from the complete move's default.");
        var onceClone = once.Clone();
        var oncePreset = UserPresetStore.Capture(session, "按下释放", once);
        var onceShared = UserPresetStore.ParseShare(UserPresetStore.Share(oncePreset)).KeyEnhancement;
        foreach (var copy in new[] { onceClone, onceShared })
        {
            Check(copy.Rules.Single().DefaultTrigger == false && copy.Rules.Single().Trigger == "press"
                && copy.CompileLua().Contains("[\"trigger\"]=\"press\""),
                "Cloning and preset sharing must preserve an explicit on-press complete-move binding.");
        }
        Directory.CreateDirectory(outputDirectory);
        foreach (bool pad in new[] { false, true })
        foreach (string trigger in new[] { "default", "press", "tap", "release" })
        {
            var multi = new KeyEnhancementConfiguration { Enabled = true };
            var multiWorkspace = new BindingWorkspace(session, multi, multi.Clone(), pad, pad ? 0 : -1);
            string[] ids = { "move:Ability.Action.Hun.Light.Air.AA", "move:Ability.Action.Hun.Light.Air.AAA" };
            var entries = multiWorkspace.AddFunctions(ids, new BindingEntry { Keys = new[] { pad ? "Gamepad_DPad_Down" : "G" }, Trigger = trigger });
            var rule = multi.Rules.Single();
            Check(entries.Length == 2 && entries.All(entry => entry.Trigger == trigger)
                && rule.DefaultTrigger == (trigger == "default") && rule.Trigger == (trigger == "default" ? "down" : trigger),
                "Multi-select must preserve default and explicit triggers on both devices.");
            var sharedMulti = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "空中连招", multi))).KeyEnhancement;
            Check(sharedMulti.Rules.Single().DefaultTrigger == rule.DefaultTrigger && sharedMulti.Rules.Single().Trigger == rule.Trigger,
                "Sharing must preserve multi-select trigger semantics.");
            File.WriteAllText(Path.Combine(outputDirectory, "multi-" + (pad ? "pad" : "keyboard") + "-" + trigger + ".lua"), sharedMulti.CompileLua());
        }
        var phaseConfig = new KeyEnhancementConfiguration { Enabled = true };
        var phaseWorkspace = new BindingWorkspace(session, phaseConfig, phaseConfig.Clone(), false, -1);
        foreach (var target in CompleteMoveCatalog.Targets)
        {
            var function = phaseWorkspace.Functions.Single(item => item.Id == target.Id);
            Check(function.GroupLabels.All(label => label != null), "A move's category labels must remain usable when its weapon label is absent: " + target.Id);
            Check(!function.Label.Contains("moves.") && !function.Label.Contains("catalog.") && !function.Label.StartsWith(" · "), "A stage label contains an untranslated key or empty prefix: " + target.Id);
            phaseWorkspace.Add(target.Id, new BindingEntry { Keys = new[] { "F8" } });
        }
        const string finalId = "move:Ability.Action.Hun.CombatArt.BronzeSword.Final";
        Check(phaseWorkspace.Functions.Single(item => item.Id == finalId).Label == "天玄青铜剑 · 大象无形 · 终结攻击", "The combat-art finisher must have its own weapon and phase label.");
        const string ghostId = "move:Accessory.RandomDrink.GhostFire.Launch";
        var ghost = phaseWorkspace.Functions.Single(item => item.Id == ghostId);
        Check(ghost.Label == "开光玉佩 · 鬼火 · 释放" && ghost.GroupPath[0] == "accessories" && ghost.GroupLabels[1] == "开光玉佩",
            "An accessory action must use its official name and accessory category.");
        Check(phaseWorkspace.FindFunctions("守静笃").Any(item => item.Id == MoveId), "The official charge name must find its complete release.");
        Check(phaseWorkspace.FindFunctions("交叉剑气").Any(item => item.Id == "move:WeaponPhase.GA_Hun_Horizontal_C.BladeWave"),
            "The cross blade waves must be separately searchable and bindable.");
        foreach (string leaf in new[] { "GA_Hun_Lute_A", "GA_Hun_Lute_AA" })
            Check(phaseWorkspace.Functions.Single(item => item.Id == "move:ShadowPhase." + leaf + ".Stage2").Label.StartsWith("催心琵琶 · "),
                "Each separate second strum must retain its native weapon name.");
        foreach (string weapon in new[] { "羽弓", "游龙叉", "独眼弓", "铁拳套", "巨凶斧", "长柄金瓜", "虎炮" })
            Check(phaseWorkspace.Functions.Any(item => item.Id.StartsWith("move:") && item.Label.StartsWith(weapon + " · ")), "Missing weapon-specific release label: " + weapon);
        string phaseLua = phaseConfig.CompileLua();
        foreach (var callback in new[] {
            "BrutalAxe/GA_BrutalAxe_Normal.GA_BrutalAxe_Normal_C:To Release",
            "TieKuangQuanTao/NewCharge/GA_TieKuangQuanTao_ChargeBase.GA_TieKuangQuanTao_ChargeBase_C:OnTick_3144DF4743C1B1DB0E46BC9DAC7F59DB",
            "TigerCannon/GA_TigerCannon.GA_TigerCannon_C:OnRelease_860334054751DF415069E4AEE4367276"
        })
            Check(phaseLua.Contains("/Abilities/ShachiAbility/" + callback), "Inherited callbacks must compile to their declaring UFunction owner: " + callback);
        Check(phaseLua.Contains("IA_CombatArt.IA_CombatArt") && !phaseLua.Contains("IA_UltimateSkill.IA_UltimateSkill"), "A combat-art recipe must use the actual native InputAction asset.");
        Check(phaseLua.Contains("[\"prepare_input\"]") && phaseLua.Contains("[\"entry_when\"]")
            && phaseLua.Contains("[\"release_input_before_entry\"]=true") && phaseLua.Contains("[\"completed_after_property\"]=\"AttackStartTime\""),
            "Compilation must retain native preparation and natural gauntlet release completion.");
        Check(phaseLua.Contains("[\"completion_when\"]") && phaseLua.Contains("[\"completion_effects\"]")
            && phaseLua.Contains("GE_Hun_HeiMoShang_Buff.GE_Hun_HeiMoShang_Buff_C"),
            "Independent phases must retain their normal completion boundary and native end consumption.");
        Check(phaseLua.Contains("[\"active_ability_classes\"]") && phaseLua.Contains("GA_Accessory_RandomDrink_GhostFire_Lv3_C")
            && phaseLua.Contains("_Lv1_C:Targeting Tick"), "An accessory must reuse the native active buff's release callback.");
        const string thunderId = "move:Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_04.Training";
        var thunder = phaseWorkspace.Functions.Single(item => item.Id == thunderId);
        Check(thunder.Label == "雷霆枪 · 雷霆再临 · 条件追击" && thunder.GroupLabels[1] == "雷霆枪", "A training follow-up must preserve its official move name and weapon group.");
        const string thunderCounterId = "move:Ability.Action.Hun.ShachiAbility.ThunderSpear.DeflectCounter.Native";
        var thunderCounter = phaseWorkspace.Functions.Single(item => item.Id == thunderCounterId);
        Check(thunderCounter.Label == "雷霆枪 · 反击" && thunderCounter.GroupLabels[1] == "雷霆枪",
            "The native counter must remain in its real weapon group independently of its generic input source.");
        Check(phaseLua.Contains("[\"activation_required_tags\"]") && phaseLua.Contains("CharacterState.WeaponUpgrade.ThunderSpear.CA"),
            "A training follow-up must retain predecessor qualification separately from continuing upgrade state.");
        var tigerCounter = phaseWorkspace.Functions.Single(item => item.Id == "move:Ability.Action.Hun.ShachiAbility.TigerCannon.Counter");
        Check(tigerCounter.Label == "虎炮 · 反击" && tigerCounter.GroupLabels[1] == "虎炮"
            && phaseLua.Contains("[\"execution_channel\"]=\"ShachiWeapon\"")
            && phaseLua.Contains("[\"execution_type\"]=\"Execution.Type.Combat.ShachiWeapon.TigerCannon.Counter\""),
            "The cannon counter must preserve its weapon identity and exact native execution-channel qualification.");
        var counters = phaseWorkspace.Functions.Where(item => item.Id.StartsWith("move:ContextualCounter.", StringComparison.Ordinal)).ToArray();
        Check(counters.Length == 31 && counters.All(item => item.GroupPath.SequenceEqual(new[] { "defense", "contextual" })
            && item.GroupLabels[1] == "情境反制" && !string.IsNullOrWhiteSpace(item.IdentityCode)),
            "Contextual counters must remain separately identified in their translated defense group.");
        Check(phaseLua.Contains("[\"activate_by_class\"]=true") && phaseLua.Contains("[\"completion_timeline\"]=\"End_Success\""),
            "Compilation must retain exact native counter selection and natural resource-qualified charge completion.");
        File.WriteAllText(Path.Combine(outputDirectory, "phase-moves.lua"), phaseLua, new System.Text.UTF8Encoding(false));
        using (var dialog = new UnifiedActionChoiceDialog(phaseWorkspace, "F8", null))
        {
            dialog.Show(); Application.DoEvents();
            var search = (TextBox)typeof(UnifiedActionChoiceDialog).GetField("search", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            foreach (string query in new[] { "天玄青铜剑", "羽弓", "虎炮", "铁拳套", "开光玉佩", "雷霆再临", "情境反制", "白山风雷剑", "交叉剑气", "催心琵琶" })
            {
                search.Text = query; Application.DoEvents();
                if (query == "开光玉佩")
                {
                    var details = (TextBox)typeof(UnifiedActionChoiceDialog).GetField("selectionDetails", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                    Check(details.Text.StartsWith("饮药触发鬼火效果后使用"), "An accessory's real use condition must appear before technical details.");
                }
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(outputDirectory, query + ".png"));
                }
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "complete-move.lua"), config.CompileLua(), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "fire-move.lua"), fireLua, new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "charge-move.lua"), chargeLua, new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "json-press-move.lua"), explicitPress.CompileLua(), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "explicit-press-move.lua"), onceShared.CompileLua(), new System.Text.UTF8Encoding(false));
        string bundle = Path.Combine(outputDirectory, "complete-move.zip");
        new BindingDocument { Session = session, Configuration = config }.Export(bundle);
        CheckOutputs(BindingDocument.Open(bundle).Configuration);
        var unknown = config.Clone(); unknown.Rules[0].Outputs[0].Tag = "move:unknown";
        Reject(delegate { unknown.CompileLua(); }, "An unknown complete move was accepted.");
        var forced = config.Clone(); forced.Rules[0].Outputs[0].Force = true;
        Reject(delegate { forced.CompileLua(); }, "A complete move accepted forced activation.");
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Run(args.Length == 0 ? Path.Combine("dist", "complete-move-catalog-check") : args[0]);
            Console.WriteLine("CompleteMoveCatalogTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
