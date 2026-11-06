"""Check all main, temporary and shadow weapon execution arrays against live resources."""
import argparse
import collections
import importlib.util
import json
import pathlib
import struct
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("executions", ROOT / "build/extract-weapon-executions.py")
executions = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(executions)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle", "registry"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    expected = json.loads((ROOT / "docs/weapon-execution-audit.json").read_text(encoding="utf-8"))
    registry = json.loads(args.registry.read_text(encoding="utf-8"))["assets"]
    main_equipment = json.loads((ROOT / "docs/complete-move-weapon-audit.json").read_text(encoding="utf-8"))["equipment"]
    shadow_equipment = [{"equipmentId": row["package"], "registryClass": row["assetClass"]} for row in registry if row["assetClass"] == "PBZ_SubWeaponConfig_C"]
    assert len(main_equipment) == 40 and len(shadow_equipment) == 46
    archive = executions.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    symbols = executions.combat_art.ScriptObjects(args.paks)
    try:
        actual = executions.extract(archive, main_equipment + shadow_equipment)
        assert actual["equipment"] == expected["equipment"]
        assert actual["counts"] == {"equipment": 86, "entries": 123}
        counts = collections.Counter(entry["role"] for row in actual["equipment"] for entry in row["executionAnims"]["entries"])
        assert counts == {"ordinary-combat-execution": 91, "special-attack": 3, "native-shadow-execution": 28, "conditional-counter": 1}
        for name, property_count in (("Main", 25), ("Temp", 24), ("Sub", 19)):
            path = "/PBZCombatAsset/Prototypes/Config/PBZ_" + name + "WeaponConfig"
            package = executions.e.Package(archive.resource(archive.packages[path.casefold()]))
            properties = executions.blueprint_properties(package)
            assert len(properties) == property_count
            assert properties == expected["blueprintSchemas"][name]
        moves = {row["id"]: row for row in json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]}
        assert {recipe["executionType"] for recipe in expected["breakAttackRecipes"]} == {
            "Execution.Type.Combat.BreakAct.Iron", "Execution.Type.Combat.BreakAct.Jigsaw"}
        channel_path = "/PBZCombatAsset/Prototypes/Abilities/GA_PerformSpecialAttack"
        channel = executions.e.Package(archive.resource(archive.packages[channel_path.casefold()]))
        channel_cdo = next(data for name, data in channel.exports if name.startswith("Default__"))
        for name in ("Ability.Action.Execution.Channel.SpecialAttack", "SpecialAttack"):
            assert struct.pack("<II", channel.names.index(name), 0) in channel_cdo
        channel_start = struct.unpack_from("<I", channel.data, 32)[0]
        channel_base = "/SGameExecution/GA/GA_PerformExecutionBase"
        assert any(symbols.resolve(channel, struct.unpack_from("<Q", channel.data, channel_start + i * 72 + 40)[0]) == channel_base
                   for i, (name, _) in enumerate(channel.exports) if name.endswith("_C") and not name.startswith("Default__"))
        base = executions.e.Package(archive.resource(archive.packages[channel_base.casefold()]))
        imports_end = struct.unpack_from("<I", base.data, 32)[0]
        for function, native in (("K2_CanActivateAbility", "/Script/SGameExecution.ExecutionComponent.CanPerformExecution"),
                                 ("K2_ActivateAbility", "/Script/SGameExecution.ExecutionComponent.PerformExecution")):
            payload = next(data for name, data in base.exports if name == function)
            assert any(symbols.resolve(base, struct.unpack_from("<Q", base.data, base.import_offset + slot * 8)[0]) == native
                       and any(bytes([opcode]) + struct.pack("<i", -slot - 1) in payload for opcode in (0x1c, 0x46, 0x68))
                       for slot in range((imports_end - base.import_offset) // 8))
        sys.path.insert(0, str(ROOT / "prototype/input-router"))
        import special_dash
        mapping_path = "/PBZCombatAsset/ActorData/Players/Hun/InputMap/AIM_Hun_HeavyAttack"
        mapping = executions.e.Package(archive.resource(archive.packages[mapping_path.casefold()]))
        document = special_dash.BindingDocument(mapping.exports[0][1], mapping.names)
        routes = [(group["ActionPriority"], route) for group in document.items
                  for block in [group.get("NormalData")] + [pair["value"] for pair in (group.get("TagConditionData") or {}).get("pairs", [])]
                  for kind in ("Actions", "Combos", "InterruptLower") for route in (block or {}).get(kind) or []]
        for recipe in expected["breakAttackRecipes"]:
            assert all(moves[recipe["id"]][key] == value for key, value in recipe.items())
            assert recipe["executionChannel"] == "SpecialAttack"
            assert recipe["tag"] == "Ability.Action.Execution.Channel.SpecialAttack"
            assert recipe["inputAction"] == "/PBZInput/InputActions/IA_AttackH_Press.IA_AttackH_Press"
            assert recipe["priority"] == -5 and "entryPoint" not in recipe and "abilityClass" not in recipe
            assert any(priority == recipe["priority"] and route.get("TriggerTag") == recipe["tag"] for priority, route in routes)
            entries = [entry for row in actual["equipment"] for entry in row["executionAnims"]["entries"] if entry["executionType"] == recipe["executionType"]]
            assert len(entries) == 1 and entries[0]["executedAbility"] is None
            assert entries[0]["channel"] == recipe["executionChannel"]
            assert entries[0]["requiresEquippedWeapon"] and not entries[0]["requiresNavigation"]
            assert entries[0]["targetRequiredTags"] == [] and entries[0]["targetIgnoredTags"] == []
            assert set(entries[0].get("executorRequiredTags", [])) <= set(recipe["requiredTags"])
            path = entries[0]["executionAbility"]
            package = executions.e.Package(archive.resource(archive.packages[path.casefold()]))
            start = struct.unpack_from("<I", package.data, 32)[0]
            parents = [symbols.resolve(package, struct.unpack_from("<Q", package.data, start + i * 72 + 40)[0])
                       for i, (name, _) in enumerate(package.exports) if name.endswith("_C") and not name.startswith("Default__")]
            assert "/SGameExecution/GA/GA_ExecutionAbilityBase" in parents
            assert any(name == "ExecutionTimeline" for name, _ in package.exports)
    finally:
        archive.close()
    print("PASS: 86 native equipment arrays, 123 typed execution entries, 3 special channels, Iron/Jigsaw recipe and initialization")


if __name__ == "__main__":
    main()
