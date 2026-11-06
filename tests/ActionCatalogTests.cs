using System;
using System.Collections.Generic;
using System.Linq;
using PBZKeyForge;

class ActionCatalogTests
{
    static int checks;
    static readonly string[] Ordinary = {
        "IA_LightAttack", "IA_HeavyAttack", "IA_Guard", "IA_Dodge", "IA_Jump", "IA_Crouch", "IA_MoveFast", "IA_MoveSlow",
        "IA_LockTarget", "IA_Interact", "IA_Execute", "IA_ShadowArm", "IA_ShadowArm_LongPress", "IA_SwitchWeapon",
        "IA_SwitchShadowArm", "IA_UltimateSkill", "IA_Sense_Default", "IA_OpenCharacterMenu", "IA_OpenMapMenu", "IA_UseTopItem", "IA_UseBottomItem"
    };
    static readonly string[] Roots = { "native", "locomotion", "targeting", "offense", "defense", "weapons", "shadow", "interaction", "scene", "interface", "custom", "debug" };
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Action(string path) { return "action:" + path; }
    static string Input(string name) { return Action("/PBZInput/InputActions/" + name); }
    static string Locomotion(string name) { return Action("/SGameLocomotionDriver/Input/" + name); }
    static string Targeting(string name) { return Action("/SGameCombatSystem/Targeting/" + name); }
    static BindingFunction[] Search(BindingWorkspace workspace, string query) { return workspace.FindFunctions(query).ToArray(); }
    static void First(BindingWorkspace workspace, string query, string expected)
    {
        var result = Search(workspace, query);
        Check(result.Length > 0 && result[0].Id == expected, "Search '" + query + "' must lead with " + expected + ", got " + (result.Length == 0 ? "no result" : result[0].Id));
    }
    static void Contains(BindingWorkspace workspace, string query, string expected)
    {
        Check(Search(workspace, query).Any(item => item.Id == expected), "Search '" + query + "' hides " + expected);
    }
    static BindingWorkspace Workspace(bool pad, KeyEnhancementConfiguration config)
    { return new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), pad, pad ? 0 : -1); }

    static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void Run()
    {
        I18n.Current = "zh-Hans";
        Check(KeyEnhancementCatalog.Targets.Length == 334 + CompleteMoveCatalog.Targets.Length && KeyEnhancementCatalog.Targets.Count(item => item.Kind == "action") == 98
            && KeyEnhancementCatalog.Targets.Count(item => item.Kind == "ability") == 236, "The audited underlying action inventory changed.");
        foreach (bool pad in new[] { false, true })
        {
            var config = new KeyEnhancementConfiguration();
            var workspace = Workspace(pad, config);
            var ids = workspace.Functions.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            OrdinaryControls(workspace);
            PlayerGroups(workspace);
            CompleteInventory(workspace);
            DisabledOrder(workspace);
            PlayerSearch(workspace);
            config.Enabled = true;
            Check(ids.SequenceEqual(workspace.Functions.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal)), "Enabling key enhancement removed or renamed a function.");
            OrdinaryControls(workspace);
            PlayerGroups(workspace);
            CompleteInventory(workspace);
            EnabledOrder(workspace, pad);
            PlayerSearch(workspace);
            VariantNames(workspace);
            AuditedNames(workspace);
            SceneAndDebug(workspace);
            HierarchicalOrder(workspace);
            PlayerLeafOrder(workspace);
            config.Enabled = false;
            DisabledOrder(workspace);
            Check(ids.SequenceEqual(workspace.Functions.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal)), "Disabling key enhancement removed or renamed a function.");
        }
        UnknownFactoryKeys();
        Console.WriteLine("ActionCatalogTests PASS " + checks);
    }

    static void OrdinaryControls(BindingWorkspace workspace)
    {
        var controls = workspace.Functions.Where(item => !item.RequiresKeyEnhancement).ToArray();
        Check(controls.Select(item => item.Id).OrderBy(id => id).SequenceEqual(Ordinary.OrderBy(id => id)), "The 21 ordinary save mappings must remain independent player entry points.");
        foreach (string id in Ordinary)
        {
            var function = controls.Single(item => item.Id == id);
            Check(function.NativeAction == id && function.GroupPath[0] == "native", id + " lost its native identity or common-control group.");
            Check(function.Label == I18n.Str("act." + id), id + " lost the official ordinary-control name.");
            var expectedKeys = InputDefaults.ForAction(id, workspace.Gamepad).OrderBy(key => key).ToArray();
            Check(workspace.Entries(id).Where(item => item.IsNative).SelectMany(item => item.Keys).Distinct().OrderBy(key => key).SequenceEqual(expectedKeys),
                id + " lost or invented original binding entries for the selected device.");
            Check(function.FactoryKeys.OrderBy(key => key).SequenceEqual(expectedKeys), id + " shows incorrect factory keys for the selected device.");
            First(workspace, function.Label, id);
            First(workspace, id, id);
        }
        var guard = controls.Single(item => item.Id == "IA_Guard");
        Check(guard.Targets.Select(item => item.Name).OrderBy(name => name).SequenceEqual(new[] { "IA_Deflect", "IA_Stance" }), "The ordinary guard entry lost one of its native input pipelines.");
    }

    static void PlayerGroups(BindingWorkspace workspace)
    {
        var functions = workspace.Functions;
        Check(functions.Where(function => function.GroupPath[0] == "native").Select(function => function.Id).OrderBy(id => id)
            .SequenceEqual(Ordinary.OrderBy(id => id)), "Standard controls must exactly match ordinary mode, without selected enhanced actions");
        string[] roots = { "locomotion", "targeting", "offense", "defense", "weapons", "shadow", "interaction", "scene", "interface", "debug" };
        int[] counts = { 16, 8, 48, 13, 105, 79, 13, 17, 17, 22 };
        for (int i = 0; i < roots.Length; i++)
            Check(functions.Count(function => function.GroupPath[0] == roots[i]) == counts[i] + CompleteMoveCatalog.Targets.Count(move => ActionCatalogLabels.Category(move.Id)[0] == roots[i]), "Incomplete player category: " + roots[i]);
        foreach (var target in KeyEnhancementCatalog.Targets)
            Check(ActionCatalogLabels.Category(target.Id).Length >= 2 && functions.Single(function => function.Id == target.Id).GroupPath[0] != "custom",
                "A known target has no audited player category: " + target.Id);
        foreach (var function in functions.Where(item => item.RequiresKeyEnhancement))
        {
            string id = function.Id, expected = null;
            if (id.StartsWith("movement:") || id == Locomotion("IA_JoystickMoveInput") || id == Locomotion("IA_KeyboardMoveInput")) expected = "locomotion/movement";
            const string light = "ability:Ability.Action.Hun.Light.", heavy = "ability:Ability.Action.Hun.Heavy.";
            if (id.StartsWith(light) && new[] { "A", "AA", "AAA", "AAAA", "AAAAA", "AAAAAA", "Snap", "ADash", "Air" }
                .Contains(id.Substring(light.Length).Split('.')[0])) expected = "offense/IA_LightAttack";
            if (id.StartsWith(heavy) && !id.Substring(heavy.Length).Contains(".")) expected = "offense/IA_HeavyAttack";
            if (id.StartsWith("ability:Ability.Action.Hun.CombatArt.")) expected = "offense/IA_UltimateSkill";
            if (id.StartsWith("ability:Ability.Action.Execution.")) expected = id.EndsWith(".SpecialAttack") ? "offense/special" : "offense/IA_Execute";
            if (id.EndsWith(".UseItem")) expected = "interaction/medicine";
            if (id.StartsWith("action:/PBZDeflectSystem/Input/") && !id.Contains("Debug")) expected = id.EndsWith("IA_Deflect") || id.EndsWith("IA_Stance") ? "defense/IA_Guard" : "defense/IA_Dodge";
            if (id == "ability:Ability.Action.Hun.SpecialDash" || id == "ability:Ability.Action.Hun.DeflectDash") expected = "defense/IA_Dodge";
            if (id.StartsWith("action:/SGameCombatSystem/Weapon/Input/") && !id.EndsWith("IA_ChangeShachiWeapon")) expected = "weapons/equipment";
            if (expected != null) Check(string.Join("/", function.GroupPath) == expected, "Related player operations are split across categories: " + id);
        }
        Check(functions.Where(function => function.GroupPath[0] == "weapons" && function.GroupPath[1] != "equipment").Select(function => function.GroupPath[1]).Distinct().Count() > 10,
            "Weapon moves lost their equipment identities");
    }

    static void CompleteInventory(BindingWorkspace workspace)
    {
        var functions = workspace.Functions;
        Check(functions.Select(item => item.Id).Distinct().Count() == functions.Length, "Catalog contains duplicate function identities.");
        Check(functions.All(item => item.GroupPath.Length > 0 && item.GroupPath.Length == item.GroupLabels.Length
            && Roots.Contains(item.GroupPath[0]) && item.GroupLabels.All(label => !string.IsNullOrWhiteSpace(label) && !label.StartsWith("unified."))), "A catalog entry lost its translated classification.");
        foreach (var target in KeyEnhancementCatalog.Targets)
        {
            Check(functions.Count(item => item.Id == target.Id) == 1, "Underlying target was lost or duplicated: " + target.Id);
            Contains(workspace, target.Id, target.Id);
        }
    }

    static void DisabledOrder(BindingWorkspace workspace)
    {
        Check(workspace.Functions.Take(21).All(item => !item.RequiresKeyEnhancement)
            && workspace.Functions.Take(21).Select(item => item.Id).OrderBy(id => id).SequenceEqual(Ordinary.OrderBy(id => id)),
            "Ordinary mode starts with locked key enhancement entries instead of its 21 usable controls.");
        Check(!Search(workspace, "")[0].RequiresKeyEnhancement, "The ordinary function picker's default selection requires enhancement.");
    }

    static void EnabledOrder(BindingWorkspace workspace, bool pad)
    {
        if (!pad)
            Check(workspace.Functions.Where(item => string.Join("/", item.GroupPath) == "locomotion/movement").Take(4).Select(item => item.Id).SequenceEqual(new[] { "movement:forward", "movement:backward", "movement:left", "movement:right" }), "Keyboard directions must lead the complete movement category.");
        else
            Check(!workspace.Functions[0].Id.StartsWith("movement:") && workspace.Functions[0].Id != Locomotion("IA_KeyboardMoveInput"), "The controller catalog prioritizes keyboard movement over controller controls.");
        int firstDebug = Array.FindIndex(workspace.Functions, item => item.GroupPath[0] == "debug");
        Check(firstDebug > 0 && Ordinary.All(id => Array.FindIndex(workspace.Functions, item => item.Id == id) < firstDebug), "Internal input debugging hides ordinary controls.");
    }

    static void PlayerSearch(BindingWorkspace workspace)
    {
        First(workspace, "Light Attack", "IA_LightAttack");
        First(workspace, "Heavy Attack", "IA_HeavyAttack");
        First(workspace, "防御", "IA_Guard");
        First(workspace, "Sprint", "IA_MoveFast");
        First(workspace, "切换武器", "IA_SwitchWeapon");
        First(workspace, "更换武器", "IA_SwitchWeapon");
        First(workspace, "收刀", Action("/SGameCombatSystem/Weapon/Input/IA_ChangeArmed"));
        Contains(workspace, "IA_Deflect", "IA_Guard");
        Contains(workspace, "完美防御", Action("/PBZDeflectSystem/Input/IA_Deflect"));
        Contains(workspace, "慢走", "IA_MoveSlow");
        Contains(workspace, "走路", "IA_MoveSlow");
        Contains(workspace, "冲刺", "IA_MoveFast");
        Check(Search(workspace, "奔跑").Length > 0, "The running control cannot be found using the player term.");
        Check(Search(workspace, "爬梯").Any(item => item.TechnicalName != null && item.TechnicalName.Contains("Climb")), "Ladder controls cannot be found by their player purpose.");
        Check(Search(workspace, "攀爬").Any(item => item.TechnicalName != null && item.TechnicalName.Contains("Climb")), "Climbing controls cannot be found by their player purpose.");
        Check(Search(workspace, "相机").Any(item => item.TechnicalName != null && item.TechnicalName.Contains("Look")), "Camera controls cannot be found by their player purpose.");
        Check(Search(workspace, "镜头").Any(item => item.TechnicalName != null && item.TechnicalName.Contains("Look")), "Camera controls cannot be found by their ordinary synonym.");
        Check(Search(workspace, I18n.Str("unified.groups.defense")).Any(item => item.GroupPath[0] == "defense"), "The function picker cannot search the visible player category.");
        foreach (string id in new[] { "ability:Ability.Action.Hun.SpecialDash", Action("/PBZDeflectSystem/Input/IA_Deflect") }) First(workspace, id, id);
    }

    static void DistinctNames(BindingWorkspace workspace, string description, params string[] ids)
    {
        var names = ids.Select(id => workspace.Functions.Single(item => item.Id == id).Label).ToArray();
        Check(names.Distinct().Count() == ids.Length, description + " are indistinguishable without reading technical paths.");
    }

    static void VariantNames(BindingWorkspace workspace)
    {
        DistinctNames(workspace, "Light attack press/held/long-press inputs", Input("IA_AttackL_Press"), Input("IA_AttackL_Hold"), Input("IA_AttackL_PressLong"));
        DistinctNames(workspace, "Heavy attack press/held/long-press inputs", Input("IA_AttackH_Press"), Input("IA_AttackH_Hold"), Input("IA_AttackH_PressLong"));
        DistinctNames(workspace, "Phantom Edge press/held/long-press inputs", Input("IA_ShachiAbility"), Input("IA_ShachiAbility_Hold"), Input("IA_ShachiAbility_LongPress"));
        DistinctNames(workspace, "Dodge, held dodge and perfect dodge", Action("/PBZDeflectSystem/Input/IA_Dodge"), Action("/PBZDeflectSystem/Input/IA_DodgeHold"), Action("/PBZDeflectSystem/Input/IA_PerfectDodge"));
        DistinctNames(workspace, "Held and toggle sprint", Locomotion("IA_Sprint"), Locomotion("IA_SprintSwitch"));
        DistinctNames(workspace, "Target switching directions and devices", Targeting("IA_MouseChangeLockTargetNext"), Targeting("IA_MouseChangeLockTargetPrevious"), Targeting("IA_MouseChangeLockTargetAxis"), Targeting("IA_JoystickChangeLockTargetAxis"));
        DistinctNames(workspace, "Mouse and stick camera inputs", Locomotion("IA_MouseLookInput"), Locomotion("IA_JoyStickLookInput"));
        DistinctNames(workspace, "Normal and held combat-art inputs", Input("IA_CombatArt"), Input("IA_CombatArt_Hold"));
        DistinctNames(workspace, "Next and previous menu tabs", Action("/Game/PBZContent/Input/InputActions/IA_NextTab"), Action("/Game/PBZContent/Input/InputActions/IA_PreviousTab"));
        DistinctNames(workspace, "Weapon switch and its transition input", Action("/SGameCombatSystem/Weapon/Input/IA_ChangeWeapon"), Action("/SGameCombatSystem/Weapon/Input/IA_ChangingWeapon"));
        DistinctNames(workspace, "Sense input device variants", Action("/Game/PBZContent/Input/InputActions/IA_Sense_Default"), Action("/Game/PBZContent/Input/InputActions/IA_Sense_DualSense"), Action("/Game/PBZContent/Input/InputActions/IA_Sense_TouchPad_Touch"));
        var discard = workspace.Functions.Single(item => item.Id == "ability:Ability.Action.Hun.TempWeapon.Ultimate");
        Check(discard.Label.Contains("丢弃") && !discard.Label.Contains("绝技"), "Discarding a temporary weapon is mislabeled as a combat art.");
        var next = workspace.Functions.Single(item => item.Id == Targeting("IA_MouseChangeLockTargetNext"));
        var previous = workspace.Functions.Single(item => item.Id == Targeting("IA_MouseChangeLockTargetPrevious"));
        Check(next.Label.Contains("下一个") && previous.Label.Contains("上一个"), "Target switching directions are not named from the player's perspective.");
        foreach (var branch in workspace.Functions.Where(item => item.Id.StartsWith("ability:", StringComparison.Ordinal)).GroupBy(item => string.Join("/", item.GroupPath)))
            Check(branch.Select(item => item.Label).Distinct().Count() == branch.Count(), "Ability branch '" + branch.Key + "' repeats category labels instead of identifying its moves.");
    }

    static void AuditedNames(BindingWorkspace workspace)
    {
        const string ability = "ability:Ability.Action.Hun.";
        foreach (string id in new[] { "Heavy.Double.ATB", "Heavy.Double.ATBB", "Heavy.Double.ATBBB", "Light.Double.BTA",
            "Heavy.Tang.AAAAAB", "Heavy.Tang.BBBB", "Light.Tang.AAAAAA", "Special.Double.YingSha.C", "Special.Double.YingSha.CB",
            "Special.Long.LieZhan.C", "Special.Long.LieZhan.CB", "Special.Tang.ShunSha.C", "Special.Tang.ShunSha.CB", "Special.Tang.GlobalAbility",
            "Special.Tang.Buff", "Special.Double.Buff", "Heavy.Long.BD", "Heavy.Long.BSup", "Light.Long.AAAAA" })
        {
            var function = workspace.Functions.Single(item => item.Id == ability + id);
            Check(string.Join("/", function.GroupPath) == "weapons/special" && !function.Label.Contains("黑伤") && !function.Label.Contains("长形"),
                "A shared weapon entry claims one weapon: " + id);
        }
        var wave = workspace.Functions.Single(item => item.Id == ability + "Heavy.BronzeSword.Nsp");
        Check(wave.GroupPath.Last() == "IA_LightAttack" && !wave.Label.Contains("重攻击"), "The sword wave is still assigned to its internal Heavy prefix.");
        foreach (string code in new[] { "B", "BB" })
            Check(!BindingWorkspace.FunctionLabel(ability + "Heavy.BronzeSword.Deflect." + code).Contains("拼刀"), "A special block is mislabeled as a deflect.");
        Check(BindingWorkspace.FunctionLabel(ability + "Special.Double.Clone").StartsWith("绝影"), "The clone ability has the wrong upgraded weapon.");
        Check(BindingWorkspace.FunctionLabel(ability + "Heavy.Tang.AAAAABB").StartsWith("黑魔伤"), "The upgraded follow-up has the wrong weapon.");
        Check(BindingWorkspace.FunctionLabel(ability + "Heavy.Long.CounterB").StartsWith("炽刃"), "An upgraded long-blade follow-up uses the base weapon name.");
        Check(BindingWorkspace.FunctionLabel(ability + "Heavy.Long.BD").Contains("增益"), "A weapon buff is named as the entire attack move.");
        Check(!BindingWorkspace.FunctionLabel(ability + "Heavy.Horizontal.CriticalAttack").Contains("充能"), "An attack is still named as a charging process.");
        var formation = workspace.Functions.Single(item => item.Id == ability + "Heavy.ZiMingJian.DB.FirstTime");
        Check(formation.Label.Contains("布局") && formation.GroupPath.Last() == "special", "Entering a weapon stance is presented as an ordinary heavy attack.");
        Check(BindingWorkspace.FunctionLabel(ability + "ShachiAbility.ZhuoBiDun.Parry").Contains("推击"), "An active shield shove is presented as a counterattack.");
        Check(!BindingWorkspace.FunctionLabel(ability + "ShachiAbility.JinGua.SprintAir").Contains("冲刺"), "A selection condition is presented as the ability's movement effect.");
        Check(BindingWorkspace.FunctionLabel("action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed") == "收起武器", "The sheathe input promises a draw action.");
        Check(!BindingWorkspace.FunctionLabel("action:/Game/PBZContent/Input/InputActions/IA_Interaction_SpecialMove").Contains("跳跃"), "A dedicated interaction promises the whole special movement pipeline.");
        Check(!BindingWorkspace.FunctionLabel("action:/Game/PBZContent/Input/InputActions/IA_MoveInput").Contains("中速"), "The generic movement input promises a speed tier.");
    }

    static void SceneAndDebug(BindingWorkspace workspace)
    {
        foreach (string name in new[] { "IA_ShipForward", "IA_ShipBackword", "IA_ShipRight", "IA_ShipLook" })
        {
            var function = workspace.Functions.Single(item => item.Id == Action("/Game/PBZContent/Input/InputActions/" + name));
            Check(function.GroupPath[0] == "scene" && function.Label.Contains("船"), name + " hides its boat context among ordinary movement.");
        }
        foreach (var target in KeyEnhancementCatalog.Targets.Where(item => item.Kind == "action" && (item.Path.Contains("/Test/") || item.Path.Contains("/OldInputActions/") || item.Name.Contains("Debug") || item.Name.Contains("Slomo"))))
            Check(workspace.Functions.Single(item => item.Id == target.Id).GroupPath[0] == "debug", "Internal input is mixed with ordinary gameplay: " + target.Id);
        var functions = workspace.Functions;
        int debug = Array.FindIndex(functions, item => item.GroupPath[0] == "debug");
        Check(debug >= 0 && functions.Skip(debug).All(item => item.GroupPath[0] == "debug"), "Debug and old assets are not collected at the end of the catalog.");
    }

    static void HierarchicalOrder(BindingWorkspace workspace)
    {
        var functions = workspace.Functions;
        var paths = functions.SelectMany(item => Enumerable.Range(1, item.GroupPath.Length).Select(length => string.Join("/", item.GroupPath.Take(length)))).Distinct();
        foreach (string path in paths)
        {
            int[] positions = Enumerable.Range(0, functions.Length).Where(index => string.Join("/", functions[index].GroupPath) == path
                || string.Join("/", functions[index].GroupPath).StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            Check(positions.Length == positions[positions.Length - 1] - positions[0] + 1, "The table splits the tree branch '" + path + "' into disjoint ranges.");
        }
        foreach (var weapon in functions.Where(item => item.GroupPath[0] == "weapons").GroupBy(item => item.GroupPath[1]))
        {
            var moves = weapon.ToArray();
            int firstHeavy = Array.FindIndex(moves, item => item.GroupPath.Contains("IA_HeavyAttack"));
            int lastLight = Array.FindLastIndex(moves, item => item.GroupPath.Contains("IA_LightAttack"));
            int firstSpecial = Array.FindIndex(moves, item => item.GroupPath.Contains("IA_UltimateSkill"));
            int lastHeavy = Array.FindLastIndex(moves, item => item.GroupPath.Contains("IA_HeavyAttack"));
            Check(firstHeavy < 0 || lastLight < 0 || lastLight < firstHeavy, weapon.Key + " puts heavy attacks before light attacks.");
            Check(firstSpecial < 0 || lastHeavy < 0 || lastHeavy < firstSpecial, weapon.Key + " scatters special moves before ordinary attacks.");
        }
    }

    static void Before(BindingWorkspace workspace, string first, string second, string purpose)
    {
        int left = Array.FindIndex(workspace.Functions, item => item.Id == first);
        int right = Array.FindIndex(workspace.Functions, item => item.Id == second);
        Check(left >= 0 && right > left, purpose + " must precede its variant or follow-up: " + first + " / " + second);
    }

    static void PlayerLeafOrder(BindingWorkspace workspace)
    {
        Check(workspace.Functions.Single(item => item.Id == Locomotion("IA_SprintSwitch")).GroupPath
            .SequenceEqual(workspace.Functions.Single(item => item.Id == Locomotion("IA_Sprint")).GroupPath),
            "Held and toggle sprint are split into unrelated categories.");
        Before(workspace, Locomotion("IA_Sprint"), Locomotion("IA_SprintSwitch"), "Held sprint");
        Before(workspace, Targeting("IA_LockTarget"), Targeting("IA_MouseChangeLockTargetNext"), "Target lock");
        Before(workspace, Targeting("IA_MouseChangeLockTargetNext"), Targeting("IA_MouseChangeLockTargetPrevious"), "Next target");
        Before(workspace, Targeting("IA_MouseChangeLockTargetPrevious"), Targeting("IA_JoystickChangeLockTargetAxis"), "Discrete target selection");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.Atk04", "ability:Ability.Action.Hun.Heavy.DashAtk01", "Ground attacks");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.DashAtk01", "ability:Ability.Action.Hun.Heavy.AirAtk01", "Sprint attack");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.AirAtk01", "ability:Ability.Action.Hun.Heavy.ComboAtk_N1H1", "Air attack");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.AirAtk01", "ability:Ability.Action.Hun.Heavy.Atk01_Hold", "Ordinary attacks");
        Before(workspace, "ability:Ability.Action.Hun.Dash", "ability:Ability.Action.Hun.AirDash", "Ordinary dodge");
        Before(workspace, "ability:Ability.Action.Hun.AirDash", "ability:Ability.Action.Hun.ContinuousDash.Dash_02", "Single air dodge");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.BrutalAxe.Main", "ability:Ability.Action.Hun.ShachiAbility.BrutalAxe.Sprinting", "Normal Phantom Edge attack");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.JinGua.Main", "ability:Ability.Action.Hun.ShachiAbility.JinGua.Air", "Normal Phantom Edge attack");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.JinGua.Air", "ability:Ability.Action.Hun.ShachiAbility.JinGua.ExtraAttack", "Initial Phantom Edge attack");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Normal", "ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Dash", "Normal cannon attack");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Dash", "ability:Ability.Action.Hun.ShachiAbility.TigerCannon.QuickCharge", "Sprint cannon attack");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.TigerCannon.QuickCharge", "ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Ultimate", "Charged cannon attack");
        Before(workspace, "ability:Ability.Action.Hun.ShachiAbility.LionHead.FireBall", "ability:Ability.Action.Hun.ShachiAbility.LionHead.DoubleFireBall", "Single fireball");
        Before(workspace, "ability:Ability.Action.Hun.SpecialMove.SideRunning.Ground", "ability:Ability.Action.Hun.SpecialMove.SideRunning.FromJump", "Ground wall run");
        Before(workspace, Action("/Game/PBZContent/Input/InputActions/IA_ClimblMove"), "ability:Ability.SpecialMove.ClimbLadder", "Climbing input");
        string ship = "/Game/PBZContent/Input/InputActions/";
        Before(workspace, Action(ship + "IA_ShipForward"), Action(ship + "IA_ShipBackword"), "Boat forward");
        Before(workspace, Action(ship + "IA_ShipBackword"), Action(ship + "IA_ShipRight"), "Boat backward");
        Before(workspace, Action(ship + "IA_ShipRight"), Action(ship + "IA_ShipLook"), "Boat movement");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.SlitheringSword.DB1", "ability:Ability.Action.Hun.Heavy.SlitheringSword.DB2", "Unknown DB variant 1");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.SlitheringSword.DB2", "ability:Ability.Action.Hun.Heavy.SlitheringSword.DB3", "Unknown DB variant 2");
        foreach (string id in new[] { "ability:Ability.Action.Hun.Heavy.SlitheringSword.DB1", "ability:Ability.Action.Hun.Light.SlitheringSword.DA" })
            Check(!workspace.Functions.Single(item => item.Id == id).Label.Contains("冲刺"), "An unknown DA/DB variant was relabeled as a sprint attack.");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.ChaosBlade.ToBing", "ability:Ability.Action.Hun.Heavy.ChaosBlade.ToHunDun", "Unranked weapon form");
        Before(workspace, "ability:Ability.Action.Hun.Heavy.ChaosBlade.ToHunDun", "ability:Ability.Action.Hun.Heavy.ChaosBlade.ToWuLi", "Unranked weapon form");
    }

    static void UnknownFactoryKeys()
    {
        var baseline = EditorSession.CreateOffline();
        var save = SaveFile.FromBytes(baseline.ExportCurrentScheme());
        const string unknown = "IA_FixtureUnknownAction";
        save.RecordFor(baseline.SelectedProfile).Entries.Add(new BlobEntry { Name = unknown, Slot = 0, Key = "F8", X = "None", Y = "None" });
        var imported = EditorSession.ImportOffline(save.ToBytes(), InputDefaultSet.Legacy, baseline.SelectedProfile);
        var config = new KeyEnhancementConfiguration();
        var workspace = new BindingWorkspace(imported, config, config.Clone(), false, -1);
        var function = workspace.Functions.Single(item => item.Id == unknown);
        Check(function.NativeAction == unknown && function.FactoryKeys == null, "An imported mapping's unknown factory default was presented as an empty binding.");
        Check(workspace.Entries(unknown).Single().Keys.SequenceEqual(new[] { "F8" }), "An imported mapping lost its saved binding when its factory default is unknown.");
    }
}
