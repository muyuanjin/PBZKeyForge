"""Check shadow release recipes against the installed CL-302256 resources.

The test reads grants, inherited callbacks, timeline state/fee events and prepared
projectiles. It does not launch the game or establish runtime combat behavior.
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    args = parser.parse_args()
    evidence = json.loads((ROOT / "docs/complete-move-shadow-recipes.json").read_text(encoding="utf-8"))
    catalog = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))
    audit = json.loads((ROOT / "docs/complete-move-shadow-audit.json").read_text(encoding="utf-8"))
    assert evidence["build"] == catalog["build"] == audit["build"] == "CL-302256"
    assert len(evidence["recipes"]) == 32
    moves = {move["id"]: move for move in catalog["moves"]}
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, evidence["build"])
    symbols, cache = combat_art.ScriptObjects(args.paks), {}

    def info(path):
        path = path.split(".")[0]
        if path in cache:
            return cache[path]
        package = combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        exports, parent = [], None
        for index, (name, _) in enumerate(package.exports):
            _, cls, superclass = struct.unpack_from("<QQQ", package.data, start + index * 72 + 24)
            class_name = symbols.resolve(package, cls)
            exports.append({"name": name, "class": class_name, "index": index})
            if class_name.endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, superclass)
        value = {"package": package, "exports": exports, "parent": parent}
        cache[path] = value
        return value

    def state_names(package, payload):
        names = set()
        for offset in range(len(payload) - 7):
            index, number = struct.unpack_from("<II", payload, offset)
            if number == 0 and index < len(package.names):
                name = package.names[index]
                if name.startswith(("CharacterState.", "GameplayEvent.")):
                    names.add(name)
        return names

    def equipment_grants(package):
        reader = combat_art.e.Reader(package.exports[0][1], package.names)
        header, grants = reader.header(), []
        for field, zero in header.items():
            if field > 6:
                break
            if zero:
                continue
            if field in (0, 1):
                for _ in range(reader.number()):
                    reader.name()
            elif field in (2, 3, 5, 6):
                objects = [package.object_name(reader.number()) for _ in range(reader.number())]
                if field in (5, 6):
                    grants.extend(objects)
            else:
                raise ValueError("Unsupported shadow config field before ability grants: " + str(field))
        return grants

    try:
        native_names = {symbols.name(identity) for identity in symbols.objects}
        for name in ("/Script/SGameCombatSystem.BulletInterface.GetData",
                     "/Script/SGameCombatSystem.BulletInterface.GetStatus",
                     "/Script/SGameInputSystem.SGameInputUtils.GetSGameInputComponent",
                     "/Script/SGameInputSystem.SGameInputComponent.IsHolding"):
            assert name in native_names, name
        grants = set()
        families = {row["weapon"] for kind in ("recipes", "nativeConditionalAttacks", "nativeContextualAttacks",
                                              "activeContextualAttacks", "nativeAutomaticPhases")
                    for row in evidence.get(kind, [])}
        for family in audit["families"]:
            if family["id"] not in families:
                continue
            for variant in family["configurationVariants"]:
                grants.update(equipment_grants(info(variant["package"])["package"]))
        for path, expected in evidence["abilityEvidence"].items():
            current = info(path)
            package = current["package"]
            timelines = {row["name"]: combat_art.timeline(package, row["index"])
                         for row in current["exports"] if row["class"] == "/Script/LegoAbility.LGATimeline"}
            for kind in ("resourceFeeEvents", "nativeStateEvents", "preparedProjectiles", "directAttackEvents"):
                for event in expected.get(kind, []):
                    actual = next(item for item in timelines[event["timeline"]]["actions"]
                                  if item["action"] == event["action"])
                    assert math.isclose(actual["startTime"], event["startTime"], abs_tol=1e-8)
                    assert math.isclose(actual["duration"], event["duration"], abs_tol=1e-8)
                    assert math.isclose(actual["triggerTimeOffset"], event["triggerOffset"], abs_tol=1e-8)
                    action = current["exports"][actual["actionPointer"] - 1]
                    assert action["class"] == event["class"]
                    payload = package.exports[action["index"]][1]
                    if kind == "nativeStateEvents":
                        assert set(event["tags"]) <= state_names(package, payload)
                    elif kind == "preparedProjectiles":
                        reader = combat_art.e.Reader(payload, package.names)
                        reader.header()
                        assert package.object_name(reader.number()) == event["bulletData"]
                        assert reader.name() == event["variable"]
            if expected.get("inheritsEvidence"):
                assert current["parent"].split(".")[0] == expected["inheritsEvidence"]
            for fee in expected.get("nativeFunctionFees", []):
                assert any(item["name"] == fee["callback"] for item in info(fee["owner"])["exports"])
        for row in evidence["recipes"]:
            expected = row["recipe"]
            actual = moves[expected["id"]]
            for key in ("tag", "abilityClass", "entryPoint", "entryArguments", "prepareInput",
                        "releaseInputBeforeEntry", "entryWhen", "activeBooleanProperties", "priority"):
                assert actual.get(key) == expected.get(key), (expected["id"], key)
            assert set(expected["requiredTags"]) <= set(actual["requiredTags"])
            assert row["abilityEvidence"] in grants
            callback = expected["entryPoint"].split(":")[-1]
            assert any(item["name"] == callback and item["class"] == "/Script/CoreUObject.Function"
                       for item in info(row["callbackOwner"])["exports"]), callback
            condition = actual["entryWhen"]
            native_timelines, ancestor = {}, row["abilityEvidence"]
            while ancestor and ancestor.casefold() in archive.packages:
                current = info(ancestor)
                for item in current["exports"]:
                    if item["class"] == "/Script/LegoAbility.LGATimeline" and item["name"] not in native_timelines:
                        native_timelines[item["name"]] = combat_art.timeline(current["package"], item["index"])
                ancestor = current["parent"]
            timeline = native_timelines[condition["timeline"]]
            boundaries = [0]
            for event in timeline["actions"]:
                boundaries.extend((event["startTime"], event["startTime"] + event["duration"],
                                   event["startTime"] + event["triggerTimeOffset"]))
            for key in ("minimum_time", "before_time"):
                if key in condition:
                    if expected["id"] == "move:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser.Shoot" and key == "minimum_time":
                        assert condition[key] == .056
                        continue
                    assert any(math.isclose(condition[key], value, abs_tol=1e-7) for value in boundaries), (expected["id"], key)
            if expected["id"] == "move:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser.Shoot":
                assert condition["timeline"] == "Loop"
                ability = info(row["abilityEvidence"])
                eyes = [action for action in timeline["actions"]
                        if action["action"] in row["nativeSelection"]["retainedUpgradeEvents"]]
                assert len(eyes) == 2
                assert [action["startTime"] for action in eyes] == [.05339889973402023, .055938899517059326]
                assert max(action["startTime"] + action["triggerTimeOffset"] for action in eyes) < condition["minimum_time"]
                assert condition["minimum_time"] < max(action["startTime"] for action in eyes) + .001
                tag = row["nativeSelection"]["upgradeEffectTag"]
                assert tag not in actual["requiredTags"], "Eye generation remains conditional on the real optional upgrade"
                for action in eyes:
                    payload = ability["package"].exports[action["actionPointer"] - 1][1]
                    assert tag in state_names(ability["package"], payload)
                    assert any(ref["path"].endswith("/BD_DuYan_Bow_Eye")
                               for ref in combat_art.object_references(ability["package"], payload))
            if condition.get("before_time"):
                assert condition["before_time"] > condition.get("minimum_time", 0)
                assert condition["cancel_on_expire"]
            variable = condition.get("object_variable")
            if variable:
                assert variable["status"] == 1
                assert variable["getter"] == "/Script/SGameCombatSystem.BulletInterface:GetData"
                assert variable["status_getter"] == "/Script/SGameCombatSystem.BulletInterface:GetStatus"
                assert any(item["variable"] == variable["key"] and
                           variable["object"].split(".")[0] == item["bulletData"]
                           for item in evidence["abilityEvidence"][row["abilityEvidence"]]["preparedProjectiles"])
            if actual.get("releaseInputBeforeEntry") and row["weapon"] == "TieKuangQuanTao":
                assert condition["completed_after_property"] == "AttackStartTime"
                assert "CharacterState.WeaponUpgrade.TieKuangQuanTao.CanCharge" in actual["requiredTags"]
        for row in evidence.get("nativeConditionalAttacks", []):
            expected = row["recipe"]
            actual = moves[expected["id"]]
            for key in ("tag", "abilityClass", "inputAction", "requiredTags", "activationAnyTags", "priority"):
                assert actual.get(key) == expected.get(key), (expected["id"], key)
            assert not actual.get("entryPoint") and not actual.get("prepareInput")
            assert actual["activationAnyTags"] == ["Ability.Action.Hun.Dash", "CharacterState.Native.Attitude.Sprinting"]
            assert row["abilityEvidence"] in grants
            current = info(row["abilityEvidence"])
            assert all(tag in current["package"].names for tag in actual["activationAnyTags"])
            callback = row["nativeSelection"]["completedCallback"]
            assert any(item["name"] == callback and item["class"] == "/Script/CoreUObject.Function"
                       for item in current["exports"])
            name = row["nativeSelection"]["completionTimeline"]
            timeline = next(combat_art.timeline(current["package"], item["index"])
                            for item in current["exports"]
                            if item["name"] == name and item["class"] == "/Script/LegoAbility.LGATimeline")
            classes = [current["exports"][action["actionPointer"] - 1]["class"] for action in timeline["actions"]]
            assert not any("JumpTimeline" in cls for cls in classes)
            assert sum("GAA_ShachiBulletInstant" in cls for cls in classes) == 1
            assert sum("LGAAction_DurationBulletWithDeflect" in cls for cls in classes) == 2
            maximum = max(action["startTime"] + action["duration"] for action in timeline["actions"])
            assert math.isclose(maximum, row["nativeSelection"]["maximumTimelineTime"], abs_tol=1e-7)
        for row in evidence.get("nativeContextualAttacks", []):
            expected = row["recipe"]
            actual = moves[expected["id"]]
            for key in ("sourceId", "tag", "abilityClass", "inputAction", "requiredTags", "priority"):
                assert actual.get(key) == expected.get(key), (expected["id"], key)
            assert not actual.get("entryPoint") and not actual.get("prepareInput")
            assert row["abilityEvidence"] in grants
            current = info(row["abilityEvidence"])
            assert actual["tag"] in combat_art.cdo_tag_candidates(current["package"])
            branch = row["nativeSelection"]
            assert branch["perfectEvent"] in current["package"].names
            assert any(item["name"] == branch["perfectCallback"] and item["class"] == "/Script/CoreUObject.Function"
                       for item in current["exports"])
            for name in (branch["completionTimeline"], branch["perfectTimeline"]):
                timeline = next(combat_art.timeline(current["package"], item["index"])
                                for item in current["exports"]
                                if item["name"] == name and item["class"] == "/Script/LegoAbility.LGATimeline")
                classes = [current["exports"][action["actionPointer"] - 1]["class"] for action in timeline["actions"]]
                assert sum("GAA_ShachiBulletInstant" in cls for cls in classes) == 1
                assert any("Bullet" in cls and "ShachiBullet" not in cls for cls in classes)
                if name == branch["completionTimeline"]:
                    maximum = max(action["startTime"] + action["duration"] for action in timeline["actions"])
                    assert math.isclose(maximum, branch["maximumTimelineTime"], abs_tol=1e-7)
        for row in evidence.get("activeContextualAttacks", []):
            expected = row["recipe"]
            actual = moves[expected["id"]]
            for key in ("sourceId", "tag", "abilityClass", "activeAbilityClasses", "entryPoint", "entryWhen",
                        "inputAction", "requiredTags", "category", "priority", "usageKey"):
                assert actual.get(key) == expected.get(key), (expected["id"], key)
            assert not actual.get("prepareInput")
            assert row["abilityEvidence"] in grants
            current = info(row["abilityEvidence"])
            names = current["package"].names
            callback = actual["entryPoint"].split(":")[-1]
            assert any(item["name"] == callback and item["class"] == "/Script/CoreUObject.Function"
                       for item in info(row["callbackOwner"])["exports"])
            condition = actual["entryWhen"]
            assert condition["timeline"] == "Loop"
            charge = condition["property_sum_above"]
            assert charge["threshold_property"] == "Thredhold" and "Thredhold" in names
            for path in charge["properties"]:
                assert path[0] == "ElementalChargeData" and path[0] in names
                assert path[1] in names
            branch = row["nativeSelection"]
            assert branch["upgradeTag"] in names
            assert any(item["name"] == branch["completedCallback"] for item in current["exports"])
            for timeline in (condition["timeline"], branch["completionTimeline"], branch["upgradedCompletionTimeline"]):
                assert any(item["name"] == timeline and item["class"] == "/Script/LegoAbility.LGATimeline"
                           for item in current["exports"])
        for row in evidence.get("nativeAutomaticPhases", []):
            assert row["abilityEvidence"] in grants
            current = info(row["abilityEvidence"])
            package = current["package"]
            timelines = {item["name"]: combat_art.timeline(package,item["index"])
                         for item in current["exports"] if item["class"] == "/Script/LegoAbility.LGATimeline"}
            event = next(item for item in timelines[row["originTimeline"]]["actions"]
                         if item["action"] == row["selectionEvent"]["action"])
            assert event == row["selectionEvent"]
            action = current["exports"][event["actionPointer"] - 1]
            assert action["class"] == row["nativeAction"]
            reader = combat_art.e.Reader(package.exports[action["index"]][1],package.names)
            # Serialized zero override: this action jumps at hit time, not when the action ends.
            assert reader.header() == {1: False, 2: True, 3: False, 4: False}
            assert row["jumpOnEnd"] is False
            assert reader.name() == row["timeline"]
            assert math.isclose(reader.number("<f"),row["startTimeSeconds"],abs_tol=1e-7)
            tags = [reader.name() for _ in range(reader.number())]
            assert tags == row["hitResultTags"] == ["Bullet.HitResult.DirectHit"]
            for attack in row["attackEvents"]:
                actual = next(item for item in timelines[row["timeline"]]["actions"] if item["action"] == attack["action"])
                for key in ("actionPointer", "startTime", "duration", "triggerTimeOffset", "endTriggerTimeOffset"):
                    assert actual[key] == attack[key]
                assert "Bullet" in current["exports"][actual["actionPointer"]-1]["class"]
            owner = info(row["nativeAction"])
            for callback in ("SetBulletHitResult", "EventReceived_B8BCB1254533B1C4853AD09CE61F1DF3"):
                assert any(item["name"] == callback for item in owner["exports"])
    finally:
        archive.close()
    print("PASS: 32 shadow phase recipes, shield rush/counter, thunder counter, 3 automatic Saw phases, grants, callbacks, fees and projectiles")


if __name__ == "__main__":
    main()
