"""Extract independent projectile follow-ups from equipped native timelines."""
import argparse
import copy
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)
SHADOW_SPEC = importlib.util.spec_from_file_location("shadow_entries", ROOT / "build/extract-shadow-move-entries.py")
shadow_entries = importlib.util.module_from_spec(SHADOW_SPEC)
SHADOW_SPEC.loader.exec_module(shadow_entries)

# Each entry starts after the preceding attack has finished and before the
# original motion/launch sequence. Conditional variants stay in that sequence.
PHASES = {
    "GA_Hun_Horizontal_C": ("BladeWave", "Main", 2.16, 0.001, ["catalog.followup", "catalog.bladewave"]),
    "GA_Hun_HeiMoShang_C1": ("BladeWave", "Exit", 2.1, 0.60001, ["catalog.followup", "catalog.bladewave"]),
    "GA_Hun_Jade_C_Range": ("Stage2", "Main", 1.59, 0.001, ["catalog.followup", "2"]),
}

# End at the selected motion's recovery, before the next independent attack.
# Native Move Cancel Ability performs the same normal end and montage cleanup
# used by the game's action-cancel path.
BOUNDED_PHASES = {
    "move:WeaponPhase.GA_Hun_Horizontal_C.BladeWave": {"timeline": "Main", "minimum_time": 2.853},
    "move:WeaponPhase.GA_Hun_HeiMoShang_C1.BladeWave": {"timeline": "Exit", "minimum_time": 3.15},
    "move:WeaponPhase.GA_Hun_Jade_C_Range.Stage2": {"timeline": "Main", "minimum_time": 3.1},
    "move:WeaponPhase.GA_Hun_Double_JueYing_C.Crow": {"timelines": ["Main", "NearTimeline"], "minimum_time": 1.81},
    "move:WeaponPhase.GA_Hun_Double_JueYing_C_Air.Crow": {"timeline": "Main", "minimum_time": 1.81},
    "move:WeaponPhase.GA_Hun_LuanYing_P2_CA.Stage2": {"timeline": "Main", "minimum_time": 1.5},
    "move:WeaponPhase.GA_Hun_ShuangYue_C.Spin": {"timeline": "Main", "minimum_time": 4.25},
    "move:WeaponPhase.GA_Hun_SlitheringSword_C.Stage2": {"timeline": "Perfect", "minimum_time": 4.4},
    "move:WeaponPhase.GA_Hun_ZiMingJian_New_CC.Stage2": {"timeline": "Main", "minimum_time": 2.1},
    "move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitStage2": {"timeline": "HasSawHit", "minimum_time": 3.1},
}


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8"))


def write(path, value):
    (ROOT / path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def extract(records, templates):
    result = []
    for record in records:
        leaf = record["package"].rsplit("/", 1)[-1]
        if leaf not in PHASES:
            continue
        suffix, branch, entry, setup, parts = PHASES[leaf]
        timeline = next(value for value in record["timelines"] if value["name"] == branch)
        attacks = [action for action in timeline["actions"] if action.get("directAttackAction")]
        prior = [action for action in attacks if action["startTime"] < entry]
        identity = "move:WeaponPhase." + leaf + "." + suffix
        completion = BOUNDED_PHASES[identity]["minimum_time"]
        retained = [action for action in attacks if entry <= action["startTime"] < completion]
        assert prior and retained and all(action["startTime"] + action["duration"] < entry for action in prior)
        assert any("DelayDeflectProjectileBullet" in action["class"] for action in retained)
        initialization = [action for action in timeline["actions"]
                          if action["duration"] == 0 and action["startTime"] < entry
                          and any(token in action["class"] for token in ("ChangeArmed", "GameplayEffect", "GameplayEvent"))]
        assert all(action["startTime"] <= setup for action in initialization)
        template = templates["move:" + leaf + ".CombatArt.Final"]
        move = {key: copy.deepcopy(template[key]) for key in (
            "sourceId", "tag", "abilityClass", "inputAction", "requiredTags", "activeBooleanProperties",
            "priority", "weaponLabels", "category")}
        move.update({
            "id": identity,
            "activateByClass": True,
            "entryPoint": "/Script/LegoAbility.LegoAbility:PlayTimeline",
            "entryArguments": [{"current_timeline": True}, 1.0, 0.0, entry, {"fname": "None"}],
            "entryWhen": {"timeline": branch, "minimum_time": setup, "before_time": entry},
            "phaseWindow": {"timeline": branch, "entry_time": entry,
                            "attack_start": min(action["startTime"] for action in retained),
                            "attack_end": max(action["startTime"] + action["duration"] for action in retained)},
            "code": "CombatArt · " + leaf.removeprefix("GA_Hun_") + " · " + suffix,
            "labelParts": ["moves.combatart"] + parts,
            "searchAliases": list(dict.fromkeys([leaf, suffix] + list(template["weaponLabels"].values())
                                                + [action["action"] for action in retained]
                                                + [ref["path"].rsplit("/", 1)[-1] for action in retained
                                                   for ref in action.get("resourceReferences", [])])),
        })
        if leaf == "GA_Hun_Horizontal_C":
            move["searchAliases"] += ["交叉剑气", "交叉刀波", "Cross blade wave"]
        proof = {
            "package": record["package"], "equipment": record["equipment"],
            "normalActivation": "Existing equipped exact-class normal activation preserves native GAS, branch selection, combat-art cost and initialization.",
            "nativeTimelineSelection": [branch], "activeInstanceOnly": False,
            "minimumSetupTime": setup,
            "timelineEvidence": [{"timeline": branch, "retainedAttackActions": retained, "entryTime": entry}],
            "priorAttackEnd": max(action["startTime"] + action["duration"] for action in prior),
            "initializationActions": initialization,
            "cleanup": "Original ability and timeline end retain montage, projectile, tags, chain, warp and state cleanup.",
        }
        result.append({"recipe": move, "evidence": proof})
    assert len(result) == len(PHASES)
    return result


def bound_phases(items, records):
    records = {record["package"]: record for record in records}
    for item in items:
        move, proof = item["recipe"], item["evidence"]
        condition = BOUNDED_PHASES.get(move["id"])
        if not condition:
            continue
        move["completionWhen"] = copy.deepcopy(condition)
        names = condition.get("timelines", [condition.get("timeline")])
        cutoff = condition["minimum_time"]
        record = records[move["abilityClass"].split(".")[0]]
        evidence_by_name = {row["timeline"]: row for row in proof["timelineEvidence"]}
        for window in move.get("phaseWindows", [move.get("phaseWindow")]):
            if window["timeline"] not in names:
                continue
            timeline = next(row for row in record["timelines"] if row["name"] == window["timeline"])
            attacks = [action for action in timeline["actions"] if action.get("directAttackAction")]
            retained = [action for action in attacks if action["startTime"] + action["duration"] >= window["entry_time"]
                        and action["startTime"] < cutoff]
            omitted = [action for action in attacks if action["startTime"] >= cutoff]
            assert retained and omitted, move["id"]
            assert all(action["startTime"] + action["duration"] < cutoff for action in retained), move["id"]
            window["attack_start"] = min(max(window["entry_time"], action["startTime"]) for action in retained)
            window["attack_end"] = max(action["startTime"] + action["duration"] for action in retained)
            evidence_by_name[window["timeline"]]["retainedAttackActions"] = retained
            evidence_by_name[window["timeline"]]["omittedLaterAttackActions"] = omitted
            evidence_by_name[window["timeline"]]["normalCompletionTime"] = cutoff
        proof["cleanup"] = "After the selected attack and recovery, original Move Cancel Ability stops BlockMovement and normally ends this instance; native OnEnd cleans montage, timeline, phase, rotation, warp and tags. Already emitted projectiles retain their normal lifetime."
        if move["id"] == "move:WeaponPhase.GA_Hun_HeiMoShang_C1.BladeWave":
            timeline = next(row for row in record["timelines"] if row["name"] == "Exit")
            cleanup = [action for action in timeline["actions"] if "RemoveGameplayEffect" in action["class"]]
            assert len(cleanup) == 1 and cleanup[0]["startTime"] > cutoff
            effect = cleanup[0]["resourceReferences"][0]["path"]
            assert effect.endswith("/GE_Hun_HeiMoShang_Buff")
            move["completionEffects"] = [{"effect": effect + "." + effect.rsplit("/", 1)[-1] + "_C", "stacks": -1}]
            proof["retainedEndConsumption"] = cleanup
    return items


def extract_crow_finals(records, templates):
    result = []
    for leaf in ("GA_Hun_Double_JueYing_C", "GA_Hun_Double_JueYing_C_Air"):
        template = templates["move:WeaponPhase." + leaf + ".Crow"]
        record = next(record for record in records if record["package"].endswith("/" + leaf))
        move = {key: copy.deepcopy(template[key]) for key in (
            "sourceId", "tag", "abilityClass", "inputAction", "requiredTags", "activeBooleanProperties",
            "priority", "weaponLabels", "category")}
        entry, setup = 1.82, .001
        branches = ["Main"] if leaf.endswith("_Air") else ["Main", "NearTimeline"]
        gate = {"minimum_time": setup, "before_time": entry}
        gate["timeline" if len(branches) == 1 else "timelines"] = branches[0] if len(branches) == 1 else branches
        move.update({"id": "move:WeaponPhase." + leaf + ".Final", "activateByClass": True,
            "entryPoint": "/Script/LegoAbility.LegoAbility:PlayTimeline",
            "entryArguments": [{"current_timeline": True}, 1.0, 0.0, entry, {"fname": "None"}],
            "entryWhen": gate, "code": "CombatArt · " + leaf.removeprefix("GA_Hun_") + " · Final",
            "labelParts": template["labelParts"][:-2] + ["moves.finisher"],
            "searchAliases": [leaf, "Final", "BD_GA_Hun_JueYing_C_End"] + list(template["weaponLabels"].values())})
        windows, evidence = [], []
        for branch in branches:
            timeline = next(row for row in record["timelines"] if row["name"] == branch)
            attacks = [action for action in timeline["actions"] if action.get("directAttackAction")]
            retained = [action for action in attacks if action["startTime"] >= entry]
            assert len(retained) == 1 and any(ref["path"].endswith("/BD_GA_Hun_JueYing_C_End") for ref in retained[0]["resourceReferences"])
            assert all(action["startTime"] + action["duration"] < entry for action in attacks if action not in retained)
            windows.append({"timeline": branch, "entry_time": entry,
                            "attack_start": retained[0]["startTime"],
                            "attack_end": retained[0]["startTime"] + retained[0]["duration"]})
            evidence.append({"timeline": branch, "entryTime": entry, "retainedAttackActions": retained})
            assert all(action["startTime"] <= setup for action in timeline["actions"] if "ChangeArmed" in action["class"])
        move["phaseWindow" if len(windows) == 1 else "phaseWindows"] = windows[0] if len(windows) == 1 else windows
        result.append({"recipe": move, "evidence": {"package": record["package"], "equipment": record["equipment"],
            "sourceRecipeId": template["id"], "activeInstanceOnly": False, "minimumSetupTime": setup,
            "normalActivation": "Normal exact-class activation pays the original combat-art fee and performs ChangeArmed before entering the final melee motion.",
            "nativeTimelineSelection": branches, "timelineEvidence": evidence,
            "omittedCrowEvents": "SpawnCrow and DeattachCrow belong to the omitted projectile stage. The independent End uses only its own original melee bullet; it does not create or launch a crow.",
            "cleanup": "Original ability and timeline complete the final melee montage and normal OnEnd cleanup."}})
    return result


def extract_lute(archive):
    symbols = combat_art.ScriptObjects(archive.paks)
    source = read("docs/shadow-move-entries.json")
    template = next(move for move in source["moves"] if move["id"] == "move:Shadow.Lute.GA_Hun_Lute_A")
    equipment = [item for item in source["configurations"] if item["family"] == "Lute"]
    result = []
    for number, leaf, entry_time in ((1, "GA_Hun_Lute_A", 1.2), (2, "GA_Hun_Lute_AA", 1.35)):
        path = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/Lute/" + leaf
        owners = []
        for item in equipment:
            package = combat_art.e.Package(archive.resource(archive.packages[item["equipmentId"].casefold()]))
            grants = shadow_entries.configuration(package)
            assert path in grants["equipAbilities"]
            owners.append(item["equipmentId"])
        package = combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        classes = [symbols.resolve(package, struct.unpack_from("<Q", package.data, start + index * 72 + 32)[0])
                   for index in range(len(package.exports))]
        index = next(index for index, (name, _) in enumerate(package.exports)
                     if name == "Main" and classes[index] == "/Script/LegoAbility.LGATimeline")
        timeline = combat_art.timeline(package, index)
        for action in timeline["actions"]:
            action["class"] = classes[action["actionPointer"] - 1]
            action["directAttackAction"] = any(token in action["class"] for token in ("Bullet", "Damage", "HitBox")) \
                and "ShachiBulletInstant" not in action["class"]
            action["resourceReferences"] = combat_art.object_references(package, package.exports[action["actionPointer"] - 1][1])
        costs = [action for action in timeline["actions"] if "GAA_ShachiBulletInstant" in action["class"]]
        assert len(costs) == 1 and costs[0]["duration"] == 0
        setup = costs[0]["startTime"] + 0.001
        retained = [action for action in timeline["actions"] if action["directAttackAction"] and action["startTime"] >= entry_time]
        assert len(retained) == 2 and max(action["startTime"] for action in retained) - min(action["startTime"] for action in retained) < 0.00001
        earlier = [action for action in timeline["actions"] if action["directAttackAction"] and action["startTime"] < entry_time]
        assert len(earlier) == 2 and all(action["startTime"] + action["duration"] < entry_time for action in earlier)
        assert all(action["startTime"] <= setup for action in timeline["actions"]
                   if action["duration"] == 0 and action["startTime"] < entry_time
                   and any(token in action["class"] for token in ("GameplayEffect", "GameplayEvent", "ChangeArmed", "ShachiBulletInstant")))
        tag = "Ability.Action.Hun.ShachiAbility.Default" if number == 1 else "Ability.Action.Hun.ShachiAbility.PiPa.AA"
        move = {
            "id": "move:ShadowPhase." + leaf + ".Stage2", "sourceId": "ability:" + tag, "tag": tag,
            "abilityClass": path + "." + leaf + "_C", "activateByClass": True,
            "entryPoint": "/Script/LegoAbility.LegoAbility:PlayTimeline",
            "entryArguments": [{"current_timeline": True}, 1.0, 0.0, entry_time, {"fname": "None"}],
            "entryWhen": {"timeline": "Main", "minimum_time": setup, "before_time": entry_time},
            "inputAction": template["inputAction"],
            "requiredTags": ["CharacterState.Space.Ground", "Weapon.Type.ShaqiWeapon.Lute"],
            "activeBooleanProperties": ["Current Shachi Reinforced"], "priority": -5,
            "code": leaf.removeprefix("GA_Hun_") + " · Stage2",
            "labelParts": ["catalog.attack", str(number), "catalog.followup", "2"],
            "weaponLabels": template["weaponLabels"], "category": template["category"],
            "searchAliases": [leaf, "Stage2"] + list(template["weaponLabels"].values()),
            "phaseWindow": {"timeline": "Main", "entry_time": entry_time,
                            "attack_start": min(action["startTime"] for action in retained),
                            "attack_end": max(action["startTime"] + action["duration"] for action in retained)},
        }
        result.append({"recipe": move, "evidence": {
            "package": path, "equipment": owners, "nativeTimelineSelection": ["Main"],
            "normalActivation": "Normal exact-class BP_ShachiAbility_Base_Magic activation retains Shachi/Bullet Enough, Current Shachi Reinforced, initial fee and original timeline.",
            "minimumSetupTime": setup, "costAction": costs[0],
            "timelineEvidence": [{"timeline": "Main", "entryTime": entry_time, "retainedAttackActions": retained}],
            "priorAttackEnd": max(action["startTime"] + action["duration"] for action in earlier),
            "cleanup": "Original K2_OnEndAbility removes armed shadow weapon, accessory endure handle, timers, ShaqiReinforced and Anger.StanceForbidRegen; inherited native end clears montage, phase and projectile tasks.",
        }})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=pathlib.Path, required=True)
    args = parser.parse_args()
    audit = read("docs/complete-move-weapon-audit.json")
    catalog = read("assets/key-enhancement/complete-moves.json")
    templates = {move["id"]: move for move in catalog["moves"]}
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, audit["build"])
    try:
        resources = combat_art.extract(archive, sorted(item["equipmentId"] for item in audit["equipment"]))
        additions = extract(resources["records"], templates) + extract_crow_finals(resources["records"], templates)
        shadow_additions = extract_lute(archive)
    finally:
        archive.close()
    evidence = read("docs/complete-move-additional-weapon-phases.json")
    identities = {item["recipe"]["id"] for item in additions}
    evidence["recipes"] = [item for item in evidence["recipes"] if item["recipe"]["id"] not in identities] + additions
    bound_phases(evidence["recipes"], resources["records"])
    identities.update(item["recipe"]["id"] for item in shadow_additions)
    catalog["moves"] = [move for move in catalog["moves"] if move["id"] not in identities] + [item["recipe"] for item in additions + shadow_additions]
    replacements = {item["recipe"]["id"]: item["recipe"] for item in evidence["recipes"]}
    catalog["moves"] = [replacements.get(move["id"], move) for move in catalog["moves"]]
    restrictions = read("docs/complete-move-restriction-audit.json")
    for item in additions:
        move = item["recipe"]
        leaf = item["evidence"]["package"].rsplit("/", 1)[-1]
        source = item["evidence"].get("sourceRecipeId", "move:" + leaf + ".CombatArt.Final")
        entry = copy.deepcopy(restrictions["recipes"][source])
        entry.update({"id": move["id"], "nativeEntryWhen": move["entryWhen"], "existingActivation": "normal_exact_class"})
        restrictions["recipes"][move["id"]] = entry
        group = restrictions["groups"][entry["group"]]
        if move["id"] not in group:
            group.append(move["id"])
    for item in shadow_additions:
        move = item["recipe"]
        entry = copy.deepcopy(restrictions["recipes"]["move:GA_Hun_Horizontal_C.CombatArt.Final"])
        entry.update({"id": move["id"], "abilityClass": move["abilityClass"], "abilityTag": move["tag"],
                      "nativeEntryWhen": move["entryWhen"], "existingActivation": "normal_exact_class",
                      "evidence": ["docs/complete-move-shadow-projectile-phases.json"],
                      "reason": "Normal activation preserves native resource qualification; wait for the original one-shot SpiritShadow cost before entering the second strum. Both simultaneous projectile variants and native cleanup remain in the original timeline."})
        entry["eligibilityGates"][1]["tag"] = "Weapon.Type.ShaqiWeapon.Lute"
        restrictions["recipes"][move["id"]] = entry
        group = restrictions["groups"][entry["group"]]
        if move["id"] not in group:
            group.append(move["id"])
    restrictions["summary"]["recipes"] = len(restrictions["recipes"])
    restrictions["scope"] = "%d protected phase recipes; selection overrides are separate from original attack initialization and from resource bypass" % len(restrictions["recipes"])
    restrictions["summary"]["groupCounts"] = {key: len(values) for key, values in restrictions["groups"].items()}
    restrictions["summary"]["fullEffectsNeedExternalCombatContext"] = sum(
        entry["fullEffectsNeedExternalCombatContext"] for entry in restrictions["recipes"].values())
    write("docs/complete-move-additional-weapon-phases.json", evidence)
    write("docs/complete-move-shadow-projectile-phases.json", {
        "build": audit["build"], "runtimeVerified": False,
        "evidenceType": "Static actual shadow EquipAbilities, original timeline cost, separate strum motions and conditional projectile resources",
        "recipes": shadow_additions})
    write("assets/key-enhancement/complete-moves.json", catalog)
    write("docs/complete-move-restriction-audit.json", restrictions)
    print("Read %d equipped combat-art classes; wrote %d weapon and %d shadow projectile follow-ups" %
          (len(resources["records"]), len(additions), len(shadow_additions)))


if __name__ == "__main__":
    main()
