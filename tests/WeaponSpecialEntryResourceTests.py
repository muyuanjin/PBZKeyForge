"""Verify special weapon entry grants, native qualification and paid transfers.

Reads installed CL-302256 packages without launching the game. Exact-class
execution and compiled binding behavior are tested by the Lua executor harness.
"""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("special_entries",ROOT/"build/extract-weapon-special-entries.py")
entries = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(entries)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe",type=pathlib.Path,required=True)
    parser.add_argument("--paks",type=pathlib.Path,required=True)
    parser.add_argument("--oodle",type=pathlib.Path,required=True)
    args = parser.parse_args()
    doc = json.loads((ROOT/"docs/complete-move-weapon-special-entries.json").read_text(encoding="utf8"))
    catalog = json.loads((ROOT/"assets/key-enhancement/complete-moves.json").read_text(encoding="utf8"))
    assert doc["build"] == catalog["build"] == "CL-302256"
    assert len(doc["recipes"]) == 10
    moves = {row["id"]:row for row in catalog["moves"]}
    archive = entries.combat_art.e.Archive(args.game_exe,args.paks,args.oodle,doc["build"])
    symbols = entries.combat_art.ScriptObjects(args.paks)
    cache = {}

    def package(path):
        if path not in cache:
            cache[path] = entries.inspect(archive,symbols,path)
        return cache[path][0]

    def cdo(path):
        pkg = package(path)
        return next(payload for name,payload in pkg.exports if name.startswith("Default__"))

    def function(path,name):
        return next(payload for export,payload in package(path).exports if export == name)

    def has_name(path,payload,name):
        return struct.pack("<II",package(path).names.index(name),0) in payload

    def calls(path,name):
        return next(row["calls"] for row in actual_resources[path]["functions"] if row["name"] == name)

    try:
        for path,expected in doc["equipmentEvidence"].items():
            assert entries.configuration(package(path)) == expected,path
        actual_resources = entries.read_resources(archive,symbols,doc["abilityEvidence"])
        # JSON normalizes timeline section indices and property-header keys.
        assert json.loads(json.dumps(actual_resources)) == doc["abilityEvidence"]
        for row in doc["recipes"]:
            recipe = row["recipe"]
            assert moves[recipe["id"]] == recipe,recipe["id"]
            assert recipe["activateByClass"] is True
            path = row["abilityEvidence"]
            assert path in doc["equipmentEvidence"][row["equipment"]][str(row["grantField"])],path
            assert len(recipe["weaponLabels"]) == 15
            assert "CharacterState.Space.Ground" in recipe["requiredTags"]
            if row["activationKind"] != "native-weapon-switch":
                assert "Weapon.Type."+row["weapon"] in recipe["requiredTags"]
            # An attack timeline must exist on the leaf or its actual ancestor.
            ancestor,attacks = path,[]
            while ancestor in actual_resources:
                attacks.extend(event for timeline in actual_resources[ancestor]["timelines"]
                               for event in timeline["actions"] if "Bullet" in event["class"])
                parent = actual_resources[ancestor]["parent"]
                ancestor = parent.split(".")[0] if parent else None
            if row["activationKind"] == "equipped-attack":
                assert attacks,path
            if row.get("nativeQualification"):
                qualification = row["nativeQualification"]
                reader = entries.combat_art.e.Reader(cdo(path),package(path).names)
                header = reader.header()
                fields = {index:[reader.name() for _ in range(reader.number())] for index in header}
                assert fields[qualification["abilityTagsField"]] == ["Ability.Action.Hun.Heavy.ChaosBlade.SWT"]
                assert fields[qualification["activationRequiredTagsField"]] == qualification["requiredTags"]
                assert fields[qualification["activationBlockedTagsField"]] == qualification["blockedTags"]
            if row["activationKind"] == "native-early-release":
                assert recipe["entryPoint"] == "OnRelease_5FFC903E46B76F3B1F78F7B3A63C7FB1"
                assert recipe["entryArguments"] == [0]
                assert has_name(path,function(path,recipe["entryPoint"]),"TimeHeld")
                graph = function(path,"ExecuteUbergraph_GA_Hun_GuiTouBlade_B_Hold_Start")
                for name in ("Holding","Min Duration","Current Reinforced"):
                    assert has_name(path,graph,name)
                assert "/Script/SGameInputSystem.AbilityInputComponent.ForceActivateExternalActionAbilityByClass" in calls(path,"ExecuteUbergraph_GA_Hun_GuiTouBlade_B_Hold_Start")
                for target in row["nativeSelection"]["targets"]:
                    assert target in package(path).imports
                    assert target in doc["equipmentEvidence"][row["equipment"]]["5"]
                    assert not any(move.get("abilityClass","").split(".")[0] == target for move in catalog["moves"])
                reinforced = row["nativeSelection"]["targets"][1]
                header = entries.combat_art.e.Reader(cdo(reinforced),package(reinforced).names).header()
                assert all(header[index] is True for index in (6,7,8)),"The release must retain the source's already-paid reinforcement"
            if row["activationKind"] == "native-weapon-switch":
                assert row["grantField"] == 9
                assert recipe["requiredTags"] == ["CharacterState.Space.Ground"]
                assert recipe["inputAction"] == "/SGameCombatSystem/Weapon/Input/IA_ChangeWeapon.IA_ChangeWeapon"
                assert has_name(path,cdo(path),row["nativeSelection"]["abilityTag"])
                assert has_name(path,cdo(path),"CharacterState.Talent.EnableDrawAttack")
                owner = row["nativeSelection"]["owner"]
                assert owner in actual_resources
                assert "/Script/SGameInputSystem.AbilityInputComponent.ForceActivateExternalActionAbilityByTag" in calls(owner,"ExecuteUbergraph_BP_SwitchAttack_Default")
                utility = row["nativeSelection"]["utility"]
                conditions = calls(utility,"Can Active Switch Attack")
                for name in ("GetChangeWeaponAbilityWeapon","GetCurrentWeapon","GetCurrentChangeWeaponCooldown"):
                    assert "/Script/SGameCombatSystem.CharacterWeaponsComponent."+name in conditions
                transfer = calls(utility,"Change Weapon By Ability")
                for name in ("GetChangeWeaponAbilityWeapon","SetCurrentWeapon","StartChangeWeaponCooldown"):
                    assert "/Script/SGameCombatSystem.CharacterWeaponsComponent."+name in transfer
            if row["activationKind"] == "native-paid-followup":
                assert recipe["activationRequiredTags"] == ["CharacterState.AbilityState.Horizontal.Charging"]
                assert recipe["priority"] == 2
                header = entries.combat_art.e.Reader(cdo(path),package(path).names).header()
                assert all(header[index] is True for index in row["nativeSelection"]["zeroCostFields"])
                owner = row["nativeSelection"]["owner"]
                graph = function(owner,"ExecuteUbergraph_GA_Hun_Horizontal_BladeWave")
                assert has_name(owner,graph,"CharacterState.AbilityState.Horizontal.Charging")
                assert has_name(owner,graph,recipe["tag"])
                assert "/Script/SGameInputSystem.AbilityInputComponent.ForceActivateExternalActionAbilityByTag" in calls(owner,"ExecuteUbergraph_GA_Hun_Horizontal_BladeWave")
    finally:
        archive.close()
    print("PASS: 10 special weapon entries, native grants, 3 form qualifications, paid early/release transfers and real weapon-switch ownership")


if __name__ == "__main__":
    main()
