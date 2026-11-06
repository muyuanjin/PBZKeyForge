"""Verify Tiger Counter's native channel, grants, initialization and fee resources.

Reads the installed CL-302256 package and reflected native signature. This does
not launch the game or establish observed combat behavior.
"""
import argparse
import importlib.util
import json
import math
import pathlib
import struct
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("ability_sources", ROOT / "build/extract-ability-sources.py")
sources = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sources)
combat_art = sources.combat_art
EXECUTION_SPEC = importlib.util.spec_from_file_location("weapon_executions", ROOT / "build/extract-weapon-executions.py")
weapon_executions = importlib.util.module_from_spec(EXECUTION_SPEC)
EXECUTION_SPEC.loader.exec_module(weapon_executions)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    args = parser.parse_args()
    evidence = json.loads((ROOT / "docs/complete-move-counter-contexts.json").read_text(encoding="utf-8"))
    catalog = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))
    expected = evidence["tigerCounter"]
    assert evidence["build"] == catalog["build"] == "CL-302256"
    move = next(item for item in catalog["moves"] if item["id"] == expected["id"])
    for key in ("tag", "sourceId", "inputAction", "executionChannel", "executionType"):
        assert move[key] == expected[key], key
    assert expected["upgradeTag"] in move["requiredTags"]
    assert not any(move.get(key) for key in ("entryPoint", "prepareInput", "entryWhen", "abilityClass"))
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, evidence["build"])
    symbols = combat_art.ScriptObjects(args.paks)

    def package(path):
        return combat_art.e.Package(archive.resource(archive.packages[path.split(".")[0].casefold()]))

    def exports(pkg):
        start = struct.unpack_from("<I", pkg.data, 32)[0]
        for index, (name, payload) in enumerate(pkg.exports):
            _, cls, superclass = struct.unpack_from("<QQQ", pkg.data, start + index * 72 + 24)
            yield index, name, payload, symbols.resolve(pkg, cls), symbols.resolve(pkg, superclass)

    def calls(pkg, function):
        payload = next(data for name, data in pkg.exports if name == function)
        imports_end = struct.unpack_from("<I", pkg.data, 32)[0]
        result = set()
        for slot in range((imports_end - pkg.import_offset) // 8):
            identity = struct.unpack_from("<Q", pkg.data, pkg.import_offset + slot * 8)[0]
            if any(bytes([opcode]) + struct.pack("<i", -slot - 1) in payload for opcode in (0x1c, 0x46, 0x68)):
                result.add(symbols.resolve(pkg, identity))
        return result

    try:
        components, unknowns = sources.effect_components(package(expected["upgrade"]), symbols)
        assert not unknowns, unknowns
        grant = next(row for row in components if row.get("activation") == "grant-and-activate")
        assert grant["grants"] == [{"ability": expected["passive"], "level": {"value": 1.0}, "inputId": -1}]
        tags = next(row["targetTags"] for row in components if row.get("targetTags"))
        assert tags["addedTags"] == [expected["upgradeTag"]]
        equipment = package(expected["equipment"])
        tiger_entries = weapon_executions.execution_array(equipment, 20)["entries"]
        tiger_counter = next(row for row in tiger_entries if row["executionType"] == expected["executionType"])
        assert tiger_counter["channel"] == expected["executionChannel"]
        assert tiger_counter["executionAbility"] == expected["attack"]
        assert tiger_counter["requiresEquippedWeapon"] and not tiger_counter["requiresNavigation"]
        assert tiger_counter["targetRequiredTags"] == [] and tiger_counter["targetIgnoredTags"] == []
        assert any(row["executionType"] == expected["rejectedExecutionType"] for row in tiger_entries)

        channel_ability = package(expected["channelAbility"])
        assert any(parent == expected["channelBase"] for _, _, _, cls, parent in exports(channel_ability)
                   if cls.endswith("BlueprintGeneratedClass"))
        channel_cdo = next(data for name, data in channel_ability.exports if name.startswith("Default__"))
        for name in (expected["tag"], expected["executionChannel"]):
            assert struct.pack("<II", channel_ability.names.index(name), 0) in channel_cdo
        channel = package(expected["channelBase"])
        assert "/Script/SGameExecution.ExecutionComponent.CanPerformExecution" in calls(channel, "K2_CanActivateAbility")
        assert "/Script/SGameExecution.ExecutionComponent.PerformExecution" in calls(channel, "K2_ActivateAbility")
        execution = package(expected["executionBase"])
        initialize = next(data for name, data in execution.exports if name == "InitializeExecutionInfo")
        assert struct.pack("<II", execution.names.index("CurrentExecutionInfo"), 0) in initialize
        attack = package(expected["attack"])
        assert any(parent == "/PBZCombatAsset/Prototypes/Abilities/Base/PBZ_ShachiWeaponExecutionBase"
                   for _, _, _, cls, parent in exports(attack) if cls.endswith("BlueprintGeneratedClass"))
        timeline_index = next(index for index, name, _, cls, _ in exports(attack)
                              if name == expected["timeline"] and cls == "/Script/LegoAbility.LGATimeline")
        timeline = combat_art.timeline(attack, timeline_index)
        for kind in ("fee", "bullet"):
            row = expected[kind]
            event = next(item for item in timeline["actions"] if item["action"] == row["action"])
            assert math.isclose(event["startTime"], row["time"], abs_tol=1e-7)
            assert event["duration"] == 0
        fee_index = next(index for index, name, _, cls, _ in exports(attack)
                         if name == expected["fee"]["action"] and cls == expected["fee"]["class"])
        reader = combat_art.e.Reader(attack.exports[fee_index][1], attack.names)
        assert reader.header() == {0: False}
        assert reader.number("<d") == expected["fee"]["value"]
        action = package(expected["fee"]["class"])
        assert expected["fee"]["property"] in action.names
        assert "/Script/SGameAttributes.AttributeUtils.ModifyCharacterAttributeValue" in calls(action, "Comsume")
        assert expected["bullet"]["resource"] in attack.imports
        native_names = {symbols.name(identity) for identity in symbols.objects}
        assert "/Script/SGameExecution.ExecutionComponent.GetResolvedExecutionType" in native_names

        bai = evidence["baiYingAssault"]
        bai_move = next(item for item in catalog["moves"] if item["id"] == bai["id"])
        assert bai_move["tag"] == "Ability.Action.Execution.Channel.SpecialAttack"
        for field in ("executionChannel", "executionType"):
            assert bai_move[field] == bai[field]
        assert bai["upgradeTag"] in bai_move["requiredTags"]
        bai_weapon = package(bai["equipment"])
        bai_entry = next(row for row in weapon_executions.execution_array(bai_weapon, 26)["entries"]
                         if row["executionType"] == bai["executionType"])
        assert bai_entry["channel"] == bai_move["executionChannel"] == "SpecialAttack"
        assert bai_entry["executionAbility"] == bai["ability"] and bai_entry["executedAbility"] is None
        assert bai_entry["executorRequiredTags"] == [bai["upgradeTag"]]
        for key in ("targetRequiredTags", "targetIgnoredTags", "requiresEquippedWeapon", "requiresNavigation"):
            assert bai_entry[key] == bai[key], key
        bai_channel = package(bai["channelAbility"])
        bai_channel_cdo = next(data for name, data in bai_channel.exports if name.startswith("Default__"))
        for name in (bai_move["tag"], bai["executionChannel"]):
            assert struct.pack("<II", bai_channel.names.index(name), 0) in bai_channel_cdo
        assert any(parent == expected["channelBase"] for _, _, _, cls, parent in exports(bai_channel)
                   if cls.endswith("BlueprintGeneratedClass"))
        bai_ability = package(bai["ability"])
        assert any(parent == bai["parent"] for _, _, _, cls, parent in exports(bai_ability)
                   if cls.endswith("BlueprintGeneratedClass"))
        assert bai["attackResource"] in bai_ability.imports and bai["targetEffect"] in bai_ability.imports
        assert "/Script/GameplayAbilities.AbilitySystemComponent.BP_ApplyGameplayEffectToTarget" in calls(
            bai_ability, "ExecuteUbergraph_GA_Hun_BaiYing_SpeicalAssault")

        snake = evidence["snakeDeflect"]
        snake_move = next(item for item in catalog["moves"] if item["id"] == snake["id"])
        assert snake_move["activateByClass"] and not snake_move.get("entryPoint")
        assert snake_move["abilityClass"] == snake["ability"] + ".GA_Hun_SnakeDual_A_Deflect_C"
        assert snake["normalState"] in snake_move["requiredTags"]
        assert snake["ability"] in combat_art.equipment_fields(package(snake["equipment"]))["equipAbilities"]
        snake_ability = package(snake["ability"])
        assert any(parent == snake["parent"] for _, _, _, cls, parent in exports(snake_ability)
                   if cls.endswith("BlueprintGeneratedClass"))
        assert snake["attackResource"] in snake_ability.imports
        assert not any("Execution" in path or "Payload" in path for path in snake_ability.imports)
        assert any(name == snake["timeline"] and cls == "/Script/LegoAbility.LGATimeline"
                   for _, name, _, cls, _ in exports(snake_ability))
        sys.path.insert(0, str(ROOT / "prototype/input-router"))
        import special_dash
        input_config = package("/PBZCombatAsset/ActorData/Players/Hun/InputConfig_Hun")
        config_reader = combat_art.e.Reader(input_config.exports[0][1], input_config.names)
        assert config_reader.header() == {0: False, 1: False, 2: False}
        for _ in range(config_reader.number()):
            config_reader.number()
        assert config_reader.number() == 0
        input_bindings = {input_config.object_name(config_reader.number()): input_config.object_name(config_reader.number())
                          for _ in range(config_reader.number())}
        for row, item in ((expected, move), (bai, bai_move)):
            assert item["inputAction"] == row["inputAction"]
            assert item["priority"] == row["priority"]
            assert input_bindings[item["inputAction"].split(".")[0]] == row["originalInputMap"]
            native_map = package(row["originalInputMap"])
            native_document = special_dash.BindingDocument(native_map.exports[0][1], native_map.names)
            native_routes = [(group["ActionPriority"], trigger) for group in native_document.items
                             for block in [group.get("NormalData")] + [pair["value"] for pair in (group.get("TagConditionData") or {}).get("pairs", [])]
                             for kind in ("Actions", "Combos", "InterruptLower")
                             for trigger in (block or {}).get(kind) or []]
            assert any(priority == item["priority"] and route.get("TriggerTag") == item["tag"]
                       for priority, route in native_routes)
        input_map = package(snake["originalInputMap"])
        document = special_dash.BindingDocument(input_map.exports[0][1], input_map.names)
        routes = []
        for group in document.items:
            blocks = [group.get("NormalData")] + [row["value"] for row in (group.get("TagConditionData") or {}).get("pairs", [])]
            for block in blocks:
                for key in ("Actions", "Combos", "InterruptLower"):
                    routes.extend((block or {}).get(key) or [])
        assert not any(route.get("TriggerTag") == snake_move["tag"] for route in routes)
        assert any(snake_move["tag"] in (route.get("RequiredTags") or [])
                   and route.get("TriggerTag") == "Ability.Action.Hun.Light.AA" for route in routes)

        dual = evidence["dualHookGrab"]
        source = package(dual["source"])
        grab = package(dual["ability"])
        for name in (dual["sourceImpactEvent"], dual["targetRequestEvent"], dual["originalActivationTag"],
                     "Bullet.Tag.Feature.Deflect.CanDodgeAfterDeflect", "Bullet.Tag.Feature.Deflect.PowerAttack", "CharacterType.Humanoid", "Driver.Driving"):
            assert name in source.names
        source_calls = calls(source, "ExecuteUbergraph_GA_Hun_DualHook_DB")
        assert "/Script/SGameInputSystem.AbilityInputComponent.ForceActivateExternalActionAbilityByTag" in source_calls
        assert "/Script/GameplayAbilities.AbilityAsync_WaitGameplayEvent.WaitGameplayEventToActor" in source_calls
        assert "/Script/SGameCombatSystem.TargetingUtils.GetPrimaryTargetingData" in calls(grab, "ExecuteUbergraph_GA_Hun_DualHook_GEx")
        grab_base = package("/SGameExecution/GA/GA_ExecutionAbilityBase_Grab")
        assert dual["successEvent"] in grab_base.names
        assert "GrabSuccess" in grab.names
    finally:
        archive.close()

    binary = args.game_exe.read_bytes()
    pe = struct.unpack_from("<I", binary, 60)[0]
    count = struct.unpack_from("<H", binary, pe + 6)[0]
    optional_size = struct.unpack_from("<H", binary, pe + 20)[0]
    image_base = struct.unpack_from("<Q", binary, pe + 24 + 24)[0]
    sections = [struct.unpack_from("<8sIIII", binary, pe + 24 + optional_size + index * 40)
                for index in range(count)]

    def offset(va):
        rva = va - image_base
        return next(raw + rva - address for _, virtual, address, size, raw in sections
                    if address <= rva < address + max(virtual, size))

    for key, name, flags in (("channelPropertyMetadata", "Channel", 0x14),
                             ("returnPropertyMetadata", "ReturnValue", 0x19)):
        at = offset(int(expected["native"][key], 16))
        name_at = offset(struct.unpack_from("<Q", binary, at)[0])
        assert binary[name_at:name_at + len(name) + 1] == name.encode("ascii") + b"\0"
        assert struct.unpack_from("<I", binary, at + 24)[0] == flags
    print("CounterContextResourceTests PASS: Tiger/BaiYing channels, SnakeDual normal attack and real DualHook target handoff")


if __name__ == "__main__":
    main()
