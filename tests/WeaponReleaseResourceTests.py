"""Check main-weapon release preparation against original cooked resources."""
import argparse
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    catalog = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))
    moves = {row["id"]: row for row in catalog["moves"]}
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, catalog["build"])
    symbols = combat_art.ScriptObjects(args.paks)

    def package(path):
        return combat_art.e.Package(archive.resource(archive.packages[path.split(".")[0].casefold()]))

    def exports(pkg):
        start = struct.unpack_from("<I", pkg.data, 32)[0]
        for index, (name, payload) in enumerate(pkg.exports):
            _, cls, _ = struct.unpack_from("<QQQ", pkg.data, start + index * 72 + 24)
            yield index, name, payload, symbols.resolve(pkg, cls)

    def calls(pkg, function):
        payload = next(data for name, data in pkg.exports if name == function)
        end = struct.unpack_from("<I", pkg.data, 32)[0]
        result = set()
        for slot in range((end - pkg.import_offset) // 8):
            identity = struct.unpack_from("<Q", pkg.data, pkg.import_offset + slot * 8)[0]
            if any(bytes([opcode]) + struct.pack("<i", -slot - 1) in payload for opcode in (0x1c, 0x46, 0x68)):
                result.add(symbols.resolve(pkg, identity))
        return result

    def timeline(pkg, name):
        index = next(index for index, actual, _, cls in exports(pkg)
                     if actual == name and cls == "/Script/LegoAbility.LGATimeline")
        return combat_art.timeline(pkg, index)

    def has_attack(pkg, name):
        rows = list(exports(pkg))
        return any("Bullet" in rows[action["actionPointer"] - 1][3]
                   for action in timeline(pkg, name)["actions"])

    try:
        bronze = moves["move:Ability.Action.Hun.Heavy.BronzeSword.BHold.Release1"]
        assert bronze["entryPoint"] == "EndFail" and not bronze.get("prepareInput")
        current = package(bronze["abilityClass"])
        assert any(name == "EndFail" and cls == "/Script/CoreUObject.Function"
                   for _, name, _, cls in exports(current))
        assert has_attack(current, "Release1")
        weapon = package("/PBZCombatAsset/ActorData/Players/Hun/Weapon/BronzeSword/Weapon_Hun_TianXuanQingTongJian")
        assert bronze["abilityClass"].split(".")[0] in combat_art.equipment_fields(weapon)["equipAbilities"]

        charge = moves["move:Ability.Action.Hun.Heavy.FengLeiJian.BHold.Release"]
        assert charge["completionTimeline"] == "End_Success" and not charge.get("entryPoint")
        assert charge["prepareInput"] == "/PBZInput/InputActions/IA_AttackH_Hold.IA_AttackH_Hold"
        assert "CharacterState.WeaponUpgrade.BaiShanFengLeiJian.BHold" in charge["requiredTags"]
        current = package(charge["abilityClass"])
        assert has_attack(current, "End_Success")
        graph_name = "ExecuteUbergraph_GA_Hun_FengLeiJian_B_Hold"
        graph = next(data for name, data in current.exports if name == graph_name)
        actual_calls = calls(current, graph_name)
        assert "/Script/SGameInputSystem.AbilityTask_WaitSInputHold.CreateTask" in actual_calls
        assert "/Script/SGameAttributes.AttributeUtils.ModifyCharacterAttributeValue" in actual_calls
        assert struct.pack("<d", 350) in graph and struct.pack("<d", .34) in graph
        for tag in ("CharacterState.AbilityState.BaiShanFengLeiJian.Thunder",
                    "GameplayEvent.Ability.BaiShanFengLeiJian.GainCharge"):
            assert struct.pack("<II", current.names.index(tag), 0) in graph
        weapon = package("/PBZCombatAsset/ActorData/Players/Hun/Weapon/BaiShanFengLeiJian/Weapon_Hun_BaiShanFengLeiJian")
        assert charge["abilityClass"].split(".")[0] in combat_art.equipment_fields(weapon)["equipAbilities"]

        release = moves["move:Ability.Action.Hun.Light.SlitheringSword.DA.End"]
        assert release["entryWhen"] == {"timeline": "Loop"} and release["releaseInputBeforeEntry"]
        assert release["activationRequiredTags"] == ["CharacterState.AbilityState.Stance.HoldingStanceKey"]
        current = package(release["abilityClass"])
        assert any(name == release["entryPoint"] and cls == "/Script/CoreUObject.Function"
                   for _, name, _, cls in exports(current))
        assert has_attack(current, "Main") and has_attack(current, "Loop")
        assert timeline(current, "End")["actions"] and not has_attack(current, "End")
        assert "/Script/LegoAbility.LegoAbility.PlayTimeline" in calls(current, "Go End")
        assert "/Script/GameplayTasks.GameplayTask.EndTask" in calls(current, "Go End")
        assert "/Script/GameplayAbilities.GameplayAbility.BP_RemoveGameplayEffectFromOwnerWithHandle" in calls(current, "K2_OnEndAbility")
        weapon = package("/PBZCombatAsset/ActorData/Players/Hun/Weapon/SlitheringSword/Weapon_Hun_SlitheringSword")
        assert release["abilityClass"].split(".")[0] in combat_art.equipment_fields(weapon)["equipAbilities"]
    finally:
        archive.close()
    print("WeaponReleaseResourceTests PASS: bronze early release, native storm charge and sword-blossom preparation/cleanup")


if __name__ == "__main__":
    main()
