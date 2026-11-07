"""Verify the combat-art inventory against CL-302256 cooked weapon resources.

This checks static resources and does not launch the game. The expected Bronze
phase boundaries and bullet identities were read independently from its cooked
timeline and native action schema.
"""
import argparse
import importlib.util
import json
import math
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)


def check_phase_recipes(result, archive):
    evidence = json.loads((ROOT / "docs/complete-move-combat-art-recipes.json").read_text(encoding="utf-8"))
    catalog = {move["id"]: move for move in json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]}
    records = {record["package"]: record for record in result["records"]}
    # Each anchor belongs to the distinct retained motion/attack sequence.
    anchors = {
        "GA_Hun_BronzeSword_C": "LGAAction_MotionWarping_1",
        "GA_Hun_FengLeiJian_C": "LGAAction_MotionWarping_3",
        "GA_Hun_BaiYing_C": "LGAAction_DurationBulletWithDeflect_2",
        "GA_Hun_Double_C": "LGAAction_MotionWarping_0",
        "GA_Hun_DualHook_C": "GAAction_GlobalSlomo_C_0",
        "GA_Hun_Horizontal_C": "LGAAction_RootOffset_1",
        "GA_Hun_Iron_C": "LGAAction_MotionWarping_2",
        "GA_Hun_Jade_C_Range": "GAAction_Snap_C_0",
        "GA_Hun_Long_C": "LGAAction_RotationMode_3",
        "GA_Hun_PoJun_C": "GAAction_ModifyCameraZoom_C_1",
        "GA_Hun_Tang_C": "LGAAction_MotionWarping_2",
        "GA_Hun_WoodenSword_C": "LGAAction_RotationMode_5",
        "GA_Hun_Young_C": "LGAAction_RotationMode_5",
        "GA_Hun_Jigsaw_C": "LGAAction_MotionWarping_8",
        "GA_Hun_Chaos_C": "LGAAction_RotationMode_9",
        "GA_Hun_YuuBlade_C": "LGAAction_RotationMode_1",
        "GA_Hun_HeiMoShang_C1": "GAA_InputBuffer_C_0",
    }
    assert len(evidence["recipes"]) == 18
    seen = set()
    symbols = combat_art.ScriptObjects(archive.paks)
    assert "/Script/LegoAbility.LegoAbility.PlayTimelineByName" in {symbols.name(identity) for identity in symbols.objects}
    assert "/Script/LegoAbility.LegoAbility.GetPlayTimelineTask" in {symbols.name(identity) for identity in symbols.objects}
    for item in evidence["recipes"]:
        recipe = item["recipe"]
        actual = catalog[recipe["id"]]
        for key, value in recipe.items():
            assert actual[key] == value, (recipe["id"], key)
        package_path, class_name = recipe["abilityClass"].split(".", 1)
        name = package_path.rsplit("/", 1)[-1]
        assert class_name == name + "_C"
        assert name not in seen
        seen.add(name)
        record = records[package_path]
        assert recipe["tag"] in record["tagCandidates"]
        assert any(path.endswith("/BP_CombatArt_Base") for path in record["ancestry"])
        assert recipe["inputAction"] == "/PBZInput/InputActions/IA_CombatArt.IA_CombatArt"
        assert recipe["activeBooleanProperties"] == ["Current Shachi Reinforced"]
        assert recipe["priority"] == -5
        gate = recipe["entryWhen"]
        timeline = next(value for value in record["timelines"] if value["name"] == gate["timeline"])
        actions = timeline["actions"]
        attacks = [action for action in actions if action.get("directAttackAction")]
        assert gate["minimum_time"] < min(action["startTime"] for action in attacks)
        assert all(action["startTime"] <= gate["minimum_time"]
                   for action in actions if action["duration"] == 0 and "ChangeArmed" in action["class"]
                   and action["startTime"] < gate["before_time"])
        if name == "GA_Hun_YanYueDao_C":
            assert recipe["entryPoint"] == "ToEnd"
            assert gate["before_time"] <= min(action["startTime"] for action in attacks)
            package = combat_art.e.Package(archive.resource(archive.packages[package_path.casefold()]))
            endpoint = next(data for export_name, data in package.exports if export_name == "ToEnd")
            imports_end = struct.unpack_from("<I", package.data, 32)[0]
            called = set()
            for slot in range((imports_end - package.import_offset) // 8):
                identity = struct.unpack_from("<Q", package.data, package.import_offset + slot * 8)[0]
                if any(bytes([opcode]) + struct.pack("<i", -slot - 1) in endpoint for opcode in (0x1c, 0x46, 0x68)):
                    called.add(symbols.resolve(package, identity))
            assert {"/Script/GameplayTasks.GameplayTask.EndTask", "/Script/LegoAbility.LegoAbility.GetPlayTimelineTask",
                    "/Script/LegoAbility.LegoAbility.PlayTimeline"} <= called
            final = next(value for value in record["timelines"] if value["name"] == "NormalEnd")
            assert sum(bool(action.get("directAttackAction")) for action in final["actions"]) == 2
            continue
        assert recipe["entryPoint"] == "/Script/LegoAbility.LegoAbility:PlayTimelineByName"
        arguments = recipe["entryArguments"]
        assert arguments[:3] == [{"fname": gate["timeline"]}, 1.0, 0.0]
        assert len(arguments) == 4
        start = arguments[3]
        assert start == gate["before_time"]
        retained_attacks = [action for action in attacks if action["startTime"] >= start]
        window = recipe["phaseWindow"]
        assert window == {
            "timeline": timeline["name"], "entry_time": start,
            "attack_start": min(action["startTime"] for action in retained_attacks),
            "attack_end": max(action["startTime"] + action["duration"] for action in retained_attacks),
        }, (recipe["id"], window, retained_attacks)
        assert window["entry_time"] < window["attack_start"] < window["attack_end"]
        assert any(action["startTime"] >= start for action in attacks)
        assert all(action["startTime"] + action["duration"] < start for action in attacks if action["startTime"] < start)
        assert start < next(action["startTime"] for action in actions if action["action"] == anchors[name])
        assert any("PlayMontage" in action["class"] and action["startTime"] < start < action["startTime"] + action["duration"] for action in actions)
        assert any("ActionPhase" in action["class"] and action["startTime"] < start < action["startTime"] + action["duration"] for action in actions)
        assert not any(action["duration"] == 0 and gate["minimum_time"] < action["startTime"] < start
                       and any(token in action["class"] for token in ("GameplayEffect", "GameplayEvent", "ChangeArmed"))
                       for action in actions)
        aliases = recipe["searchAliases"]
        assert any(action["action"] in aliases for action in attacks if action["startTime"] >= start)
    assert seen == set(anchors) | {"GA_Hun_YanYueDao_C"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    args = parser.parse_args()
    audit = json.loads((ROOT / "docs/complete-move-weapon-audit.json").read_text(encoding="utf-8"))
    assert audit["build"] == "CL-302256"
    paths = sorted(item["equipmentId"] for item in audit["equipment"])
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = combat_art.extract(archive, paths)
        check_phase_recipes(result, archive)
    finally:
        archive.close()

    # The independent AssetRegistry/equipment association inventory provides
    # the expected class set; cooked grant arrays provide the actual grants.
    associated = {path for item in audit["equipment"] for path in item["relatedAbilityPaths"]}
    expected = {path.casefold() for path, record in audit["abilities"].items()
                if any(tag.startswith("Ability.Action.Hun.CombatArt.")
                       for tag in record["effectiveTagCandidates"])
                and path in associated}
    assert len(expected) == 35
    assert {record["package"].casefold() for record in result["records"]} == expected
    assert len(result["equipment"]) == 40

    bronze = next(record for record in result["records"] if record["package"].endswith("/GA_Hun_BronzeSword_C"))
    assert bronze["tagCandidates"] == ["Ability.Action.Hun.CombatArt.Atk01"]
    assert bronze["tagEvidenceOwner"].endswith("/Combo/BP_C_Base")
    config = next(item for item in result["equipment"] if item["package"].endswith("/Weapon_Hun_TianXuanQingTongJian"))
    assert len(config["equipAbilities"]) == 41
    assert bronze["package"] in config["equipAbilities"]
    assert any(path.endswith("/GA_Hun_BronzeSword_BA") for path in config["equipAbilities"])
    assert any(path.endswith("/GA_Hun_BronzeSword_B_Hold") for path in config["equipAbilities"])

    timelines = {item["name"]: item for item in bronze["timelines"]}
    assert set(timelines) == {"Main", "Failed"}
    actions = {action["action"]: action for action in timelines["Main"]["actions"]}
    assert len(actions) == 65
    setup = actions["GAAction_ChangeArmed_C_1"]
    assert math.isclose(setup["startTime"], 0.012524645775556564, abs_tol=1e-12)
    assert setup["duration"] == 0
    previous = actions["LGAAction_DurationBulletWithDeflect_3"]
    assert previous["startTime"] + previous["duration"] < 2.43
    assert actions["GAA_PBZWeaponTrail_C_2"]["startTime"] + actions["GAA_PBZWeaponTrail_C_2"]["duration"] < 2.43
    assert 2.43 < actions["LGAAction_MotionWarping_1"]["startTime"]
    for name in ("LGAAction_PlayMontage_1", "GAAction_ActionPhase_C_0", "LGAAction_GameplayTags_0",
                 "GAAction_ModifyCameraZoom_C_0", "LGAAction_RotationMode_0", "LGAAction_RotationMode_4"):
        action = actions[name]
        assert action["startTime"] < 2.43 < action["startTime"] + action["duration"]
    final_wave = actions["GAA_DelayDeflectProjectileBullet_C_1"]
    assert math.isclose(final_wave["startTime"] + final_wave["duration"], 3.5660293102264404, abs_tol=1e-10)
    assert any(ref["path"].endswith("/B_Hun_BronzeSword_C_06_Wave") for ref in final_wave["resourceReferences"])
    assert any(ref["path"].endswith("/B_Hun_BronzeSword_C_08")
               for ref in actions["LGAAction_DurationBulletWithDeflect_0"]["resourceReferences"])
    assert not any("HoldB_Release" in ref["path"] or "BronzeSword_BA_02" in ref["path"]
                   for action in actions.values() for ref in action.get("resourceReferences", []))
    assert sum(len(record["timelines"]) for record in result["records"]) > 71
    print("PASS: 40 equipment grants, 35 combat-art classes, all 18 catalog phase recipes, native/Blueprint entry functions, setup and attack boundaries")


if __name__ == "__main__":
    main()
