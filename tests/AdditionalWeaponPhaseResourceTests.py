"""Check independent combat-art phases against the installed cooked resources."""
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
    read = lambda path: json.loads((ROOT / path).read_text(encoding="utf-8"))
    evidence = read("docs/complete-move-additional-weapon-phases.json")
    catalog = {move["id"]: move for move in read("assets/key-enhancement/complete-moves.json")["moves"]}
    audit = read("docs/complete-move-weapon-audit.json")
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, evidence["build"])
    try:
        result = combat_art.extract(archive, sorted(item["equipmentId"] for item in audit["equipment"]))
        records = {record["package"]: record for record in result["records"]}
        assert len(records) == 35 and len(result["equipment"]) == 40
        assert len(catalog) == 148 and len(evidence["recipes"]) == 24
        for item in evidence["recipes"]:
            move, proof = item["recipe"], item["evidence"]
            assert catalog[move["id"]] == move
            record = records[proof["package"]]
            assert move["abilityClass"] == record["package"] + "." + record["package"].rsplit("/", 1)[1] + "_C"
            assert move["tag"] in record["tagCandidates"]
            assert move["activeBooleanProperties"] == ["Current Shachi Reinforced"]
            assert move["inputAction"] == "/PBZInput/InputActions/IA_CombatArt.IA_CombatArt"
            assert move["entryPoint"] == "/Script/LegoAbility.LegoAbility:PlayTimeline"
            args = move["entryArguments"]
            assert len(args) == 5 and args[:3] == [{"current_timeline": True}, 1.0, 0.0]
            assert args[4] == {"fname": "None"}
            gate = move["entryWhen"]
            assert 0 < gate["minimum_time"] < args[3] == gate["before_time"]
            branches = gate.get("timelines", [gate.get("timeline")])
            assert branches == proof["nativeTimelineSelection"]
            if proof["activeInstanceOnly"]:
                assert move["activeAbilityClasses"] == [move["abilityClass"]]
                assert not move.get("activateByClass") and not move.get("activeCallbackOnly")
            else:
                assert move["activateByClass"] and not move.get("activeAbilityClasses")
            windows = move.get("phaseWindows", [move.get("phaseWindow")])
            assert len(windows) == len(branches)
            for name, window, retained in zip(branches, windows, proof["timelineEvidence"]):
                timeline = next(value for value in record["timelines"] if value["name"] == name)
                completion = move.get("completionWhen", {})
                cutoff = completion.get("minimum_time", float("inf")) if name in completion.get(
                    "timelines", [completion.get("timeline")]) else float("inf")
                attacks = [a for a in timeline["actions"] if a.get("directAttackAction")
                           and a["startTime"] + a["duration"] >= args[3] and a["startTime"] < cutoff]
                assert json.loads(json.dumps(attacks)) == retained["retainedAttackActions"], move["id"]
                assert window == {"timeline": name, "entry_time": args[3],
                                  "attack_start": min(max(args[3], a["startTime"]) for a in attacks),
                                  "attack_end": max(a["startTime"] + a["duration"] for a in attacks)}
                assert all(a["startTime"] <= gate["minimum_time"] for a in timeline["actions"]
                           if "ChangeArmed" in a["class"] and a["duration"] == 0 and a["startTime"] < args[3])
                if cutoff != float("inf"):
                    assert all(a["startTime"] + a["duration"] < cutoff for a in attacks), move["id"]
                    omitted = [a for a in timeline["actions"] if a.get("directAttackAction") and a["startTime"] >= cutoff]
                    assert json.loads(json.dumps(omitted)) == retained["omittedLaterAttackActions"] and omitted, move["id"]
                    assert retained["normalCompletionTime"] == cutoff

        # These projectile follow-ups precede the separately exposed finishers.
        # Their original launch tasks and native cleanup must remain in the
        # retained timeline, including every conditional flying-ring variant.
        projectile_phases = {
            "GA_Hun_Horizontal_C": ("BladeWave", "Main", 2.16, 2, ["B_Hun_Horizontal_C_04", "B_Hun_Horizontal_C_05"]),
            "GA_Hun_HeiMoShang_C1": ("BladeWave", "Exit", 2.1, 3, ["BD_Hun_HeiMoShang_C_03_New", "BD_Hun_HeiMoShang_C_04_New", "BD_Hun_HeiMoShang_C_05_New"]),
            "GA_Hun_Jade_C_Range": ("Stage2", "Main", 1.59, 10, []),
        }
        for leaf, (suffix, branch, entry_time, expected_count, bullet_names) in projectile_phases.items():
            move = catalog["move:WeaponPhase." + leaf + "." + suffix]
            record = records[move["abilityClass"].split(".")[0]]
            timeline = next(value for value in record["timelines"] if value["name"] == branch)
            launches = [action for action in timeline["actions"]
                        if "DelayDeflectProjectileBullet" in action["class"] and entry_time <= action["startTime"] < move["completionWhen"]["minimum_time"]]
            assert move["entryArguments"][3] == entry_time and len(launches) == expected_count
            assert set(bullet_names) <= {ref["path"].rsplit("/", 1)[-1]
                                         for action in launches for ref in action["resourceReferences"]}
            assert all(action["startTime"] + action["duration"] < entry_time for action in timeline["actions"]
                       if action.get("directAttackAction") and action["startTime"] < entry_time)
            package = combat_art.e.Package(archive.resource(archive.packages[record["package"].casefold()]))
            assert not any(action["duration"] == 0 and move["entryWhen"]["minimum_time"] < action["startTime"] < entry_time
                           and any(token in action["class"] for token in ("GameplayEffect", "GameplayEvent", "ChangeArmed"))
                           for action in timeline["actions"])
            final = catalog["move:" + leaf + ".CombatArt.Final"]
            assert move["phaseWindow"]["attack_start"] < final["phaseWindow"]["attack_start"]
            assert move["phaseWindow"]["attack_end"] < move["completionWhen"]["minimum_time"] < final["phaseWindow"]["attack_start"]
        infernal = catalog["move:WeaponPhase.GA_Hun_HeiMoShang_C1.BladeWave"]
        assert "CharacterState.AbilityState.HeiMoShang.Buff" in infernal["requiredTags"]
        assert infernal["entryWhen"] == {"timeline": "Exit", "minimum_time": 0.60001, "before_time": 2.1}
        assert infernal["completionEffects"] == [{
            "effect": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/HeiMoShang/GE_Hun_HeiMoShang_Buff.GE_Hun_HeiMoShang_Buff_C",
            "stacks": -1}]
        infernal_proof = next(item["evidence"] for item in evidence["recipes"] if item["recipe"]["id"] == infernal["id"])
        exit_timeline = next(t for t in records[infernal["abilityClass"].split(".")[0]]["timelines"] if t["name"] == "Exit")
        assert infernal_proof["retainedEndConsumption"] == [a for a in exit_timeline["actions"] if "RemoveGameplayEffect" in a["class"]]

        bounded = [move for move in catalog.values() if move.get("completionWhen")]
        assert len(bounded) == 10
        cross = catalog["move:WeaponPhase.GA_Hun_Horizontal_C.BladeWave"]
        cross_timeline = next(t for t in records[cross["abilityClass"].split(".")[0]]["timelines"] if t["name"] == "Main")
        actual_cross = [a for a in cross_timeline["actions"] if a.get("directAttackAction")
                        and a["startTime"] + a["duration"] >= cross["entryArguments"][3]
                        and a["startTime"] < cross["completionWhen"]["minimum_time"]]
        assert len(actual_cross) == 2 and all("DelayDeflectProjectileBullet" in a["class"] for a in actual_cross)
        assert {ref["path"].rsplit("/", 1)[-1] for a in actual_cross for ref in a["resourceReferences"]} == {
            "B_Hun_Horizontal_C_04", "B_Hun_Horizontal_C_05"}

        shadow = read("docs/complete-move-shadow-projectile-phases.json")
        assert len(shadow["recipes"]) == 2
        symbols = combat_art.ScriptObjects(archive.paks)
        for item in shadow["recipes"]:
            move, proof = item["recipe"], item["evidence"]
            assert catalog[move["id"]] == move and move["activateByClass"]
            package = combat_art.e.Package(archive.resource(archive.packages[proof["package"].casefold()]))
            start = struct.unpack_from("<I", package.data, 32)[0]
            classes = [symbols.resolve(package, struct.unpack_from("<Q", package.data, start + i * 72 + 32)[0])
                       for i in range(len(package.exports))]
            index = next(i for i, (name, _) in enumerate(package.exports)
                         if name == "Main" and classes[i] == "/Script/LegoAbility.LGATimeline")
            timeline = combat_art.timeline(package, index)
            fee = [action for action in timeline["actions"] if classes[action["actionPointer"] - 1].endswith("/GAA_ShachiBulletInstant")]
            assert len(fee) == 1 and fee[0]["duration"] == 0
            assert fee[0]["startTime"] < move["entryWhen"]["minimum_time"] < move["entryArguments"][3]
            launches = [action for action in timeline["actions"]
                        if "DelayDeflectProjectileBullet" in classes[action["actionPointer"] - 1]]
            retained = [action for action in launches if action["startTime"] >= move["entryArguments"][3]]
            assert len(launches) == 4 and len(retained) == 2
            assert {action["action"] for action in retained} == {
                action["action"] for action in proof["timelineEvidence"][0]["retainedAttackActions"]}
            assert move["phaseWindow"]["attack_start"] == min(action["startTime"] for action in retained)
            assert all(action["startTime"] + action["duration"] < move["entryArguments"][3]
                       for action in launches if action not in retained)
            # The second strum keeps its own turn/motion task and both resource-qualified projectiles.
            assert any("RotationMode" in classes[action["actionPointer"] - 1]
                       and move["entryArguments"][3] < action["startTime"] < move["phaseWindow"]["attack_start"]
                       for action in timeline["actions"])

        # These are single native attacks with effect chains, rather than omitted
        # motions. Their exact-class entrances preserve every effect event.
        for leaf, expected in {"GA_Hun_XieShan_C": 1, "GA_Hun_ZiMingJian_New_C": 1,
                               "GA_Hun_ZiMingJian_New_CCC": 1}.items():
            record = next(r for r in records.values() if r["package"].endswith("/" + leaf))
            main = next(t for t in record["timelines"] if t["name"] == "Main")
            assert sum(bool(a.get("directAttackAction")) for a in main["actions"]) == expected
        jigsaw = next(r for r in records.values() if r["package"].endswith("/GA_Hun_Jigsaw_C"))
        package = combat_art.e.Package(archive.resource(archive.packages[jigsaw["package"].casefold()]))
        assert {"GAA_JumpTimelineByHitResult_C", "Bullet.HitResult.DirectHit", "HasSawHit"} <= set(package.names)
        shuang = next(r for r in records.values() if r["package"].endswith("/GA_Hun_ShuangYue_C"))
        attacks = [a for t in shuang["timelines"] if t["name"] == "Main" for a in t["actions"] if a.get("directAttackAction")]
        assert len(attacks) == 42  # Fourteen native attack positions, three resource-qualified variants each.
        release = catalog["move:WeaponPhase.GA_Hun_ChiZheCanJian_C_ToZui.Release"]
        assert release["entryWhen"]["object_variable"]["key"] == "JiuTan"
        to_zui = records[release["abilityClass"].split(".")[0]]
        package = combat_art.e.Package(archive.resource(archive.packages[to_zui["package"].casefold()]))
        main = next(t for t in to_zui["timelines"] if t["name"] == "Main")
        event = next(a for a in main["actions"] if a["action"] == "GAA_GameplayEvent_C_0")
        raw = package.exports[event["actionPointer"] - 1][1]
        assert package.names[struct.unpack_from("<I", raw, 4)[0]] == "GameplayEvent.Ability.HuaQianXiao.Drink.ToZui"
        assert release["entryWhen"]["minimum_time"] > event["startTime"]
        prepared = [a for a in main["actions"] if "ReadyBullet" in a["class"]]
        assert prepared and all(a["startTime"] <= release["entryWhen"]["minimum_time"] for a in prepared)
        assert any(ref["path"] + "." + ref["path"].rsplit("/", 1)[-1]
                   == release["entryWhen"]["object_variable"]["object"]
                   for action in prepared for ref in action["resourceReferences"])
        crow = catalog["move:WeaponPhase.GA_Hun_Double_JueYing_C.Crow"]
        record = records[crow["abilityClass"].split(".")[0]]
        package = combat_art.e.Package(archive.resource(archive.packages[record["package"].casefold()]))
        for timeline in record["timelines"]:
            if timeline["name"] not in ("Main", "NearTimeline"):
                continue
            for action in timeline["actions"]:
                if action["action"] in ("GAA_GameplayEvent_C_0", "GAA_GameplayEvent_C_1"):
                    raw = package.exports[action["actionPointer"] - 1][1]
                    tag = package.names[struct.unpack_from("<I", raw, 4)[0]]
                    assert tag in ("GameplayEvent.Weapon.JueYing.SpawnCrow", "GameplayEvent.Weapon.JueYing.DeattachCrow")
                    assert crow["entryArguments"][3] < action["startTime"]
        for leaf in ("GA_Hun_Double_JueYing_C", "GA_Hun_Double_JueYing_C_Air"):
            final = catalog["move:WeaponPhase." + leaf + ".Final"]
            crow = catalog["move:WeaponPhase." + leaf + ".Crow"]
            assert final["activateByClass"] and final["entryArguments"][3] == 1.82
            assert final["entryWhen"]["minimum_time"] == .001 and not final.get("completionWhen")
            branches = final["entryWhen"].get("timelines", [final["entryWhen"].get("timeline")])
            assert branches == (["Main"] if leaf.endswith("_Air") else ["Main", "NearTimeline"])
            record = records[final["abilityClass"].split(".")[0]]
            for branch in branches:
                timeline = next(t for t in record["timelines"] if t["name"] == branch)
                armed = [a for a in timeline["actions"] if "ChangeArmed" in a["class"]]
                assert len(armed) == 1 and armed[0]["startTime"] < final["entryWhen"]["minimum_time"]
                attacks = [a for a in timeline["actions"] if a.get("directAttackAction")
                           and a["startTime"] >= final["entryArguments"][3]]
                assert len(attacks) == 1
                assert any(ref["path"].endswith("/BD_GA_Hun_JueYing_C_End") for ref in attacks[0]["resourceReferences"])
                assert crow["completionWhen"]["minimum_time"] < final["entryArguments"][3] < attacks[0]["startTime"]
                assert all(a["startTime"] < final["entryArguments"][3] for a in timeline["actions"]
                           if "GameplayEvent" in a["class"])
        zhan = catalog["move:WeaponPhase.GA_Hun_ZhanMa_C.GrabFinal"]
        record = records[zhan["abilityClass"].split(".")[0]]
        grab = next(t for t in record["timelines"] if t["name"] == "GrabSuccess")
        heal = [a for a in grab["actions"] if any(ref["path"].endswith("/GE_Hun_ZhanMa_RestoreHealth_CA_Grab")
                                                for ref in a["resourceReferences"])]
        assert len(heal) == 1 and heal[0]["startTime"] < zhan["entryWhen"]["minimum_time"]
        assert not evidence["runtimeVerified"]
        print("PASS: 35 equipped combat-art classes, 24 weapon and 2 shadow phase entries, retained projectile groups, costs and preparation")
    finally:
        archive.close()


if __name__ == "__main__":
    main()
