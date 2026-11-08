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
        assert len(catalog) == 141 and len(evidence["recipes"]) == 19
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
                attacks = [a for a in timeline["actions"] if a.get("directAttackAction")
                           and a["startTime"] + a["duration"] >= args[3]]
                assert json.loads(json.dumps(attacks)) == retained["retainedAttackActions"], move["id"]
                assert window == {"timeline": name, "entry_time": args[3],
                                  "attack_start": min(max(args[3], a["startTime"]) for a in attacks),
                                  "attack_end": max(a["startTime"] + a["duration"] for a in attacks)}
                assert all(a["startTime"] <= gate["minimum_time"] for a in timeline["actions"]
                           if "ChangeArmed" in a["class"] and a["duration"] == 0 and a["startTime"] < args[3])

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
        zhan = catalog["move:WeaponPhase.GA_Hun_ZhanMa_C.GrabFinal"]
        record = records[zhan["abilityClass"].split(".")[0]]
        grab = next(t for t in record["timelines"] if t["name"] == "GrabSuccess")
        heal = [a for a in grab["actions"] if any(ref["path"].endswith("/GE_Hun_ZhanMa_RestoreHealth_CA_Grab")
                                                for ref in a["resourceReferences"])]
        assert len(heal) == 1 and heal[0]["startTime"] < zhan["entryWhen"]["minimum_time"]
        assert not evidence["runtimeVerified"]
        print("PASS: 35 equipped combat-art classes, 19 native phase entries, retained attacks, preparation and branch qualifications")
    finally:
        archive.close()


if __name__ == "__main__":
    main()
