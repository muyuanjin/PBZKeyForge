"""Check weapon charge releases against the original equipped ability and selector."""
import argparse
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)


def script(payload):
    candidates = []
    for offset in range(len(payload) - 19):
        memory, stored = struct.unpack_from("<II", payload, offset)
        if (0 < memory < 10000000 and 0 < stored <= memory * 2
                and offset + 8 + stored == len(payload) - 12
                and payload[offset + 7 + stored] == 0x53):
            candidates.append(payload[offset + 8:offset + 8 + stored])
    assert len(candidates) == 1
    return candidates[0]


def selector(package, code):
    def field(offset):
        assert code[offset] in (0, 1)
        assert struct.unpack_from("<i", code, offset + 1)[0] == 1
        index, number = struct.unpack_from("<II", code, offset + 5)
        assert number == 0
        return package.names[index], offset + 17

    candidates = []
    for offset in range(len(code) - 8):
        if code[offset] != 0x69 or struct.unpack_from("<H", code, offset + 1)[0] != 4:
            continue
        position = offset + 7
        name, position = field(position)
        if name != "Temp_int_Variable":
            continue
        cases = {}
        for _ in range(4):
            assert code[position] == 0x1d
            charge = struct.unpack_from("<i", code, position + 1)[0]
            name, position = field(position + 9)
            cases[charge] = name
        candidates.append(cases)
    assert len(candidates) == 1
    return candidates[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    evidence = json.loads((ROOT / "docs/complete-move-weapon-charge-recipes.json").read_text(encoding="utf-8"))
    catalog = {move["id"]: move for move in json.loads(
        (ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]}
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, evidence["build"])
    try:
        def package(path):
            return combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))

        path = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/LongBlade/ChiRen/GA_Hun_ChiRen_C"
        ability = package(path)
        weapon = package("/PBZCombatAsset/ActorData/Players/Hun/Weapon/Long/Weapon_Hun_ChiRen")
        assert path in combat_art.equipment_fields(weapon)["equipAbilities"]
        functions = dict(ability.exports)
        graph = script(functions["ExecuteUbergraph_GA_Hun_ChiRen_C"])
        assert selector(ability, graph) == {0: "Release_01", 1: "Release_02", 2: "Release_03", 3: "Release_03"}
        graph_index = next(index + 1 for index, (name, _) in enumerate(ability.exports)
                           if name == "ExecuteUbergraph_GA_Hun_ChiRen_C")
        assert bytes([0x46]) + struct.pack("<i", graph_index) + bytes([0x1d]) + struct.pack("<i", 995) in script(functions["ToRelease"])
        timeline = combat_art.timeline(ability, next(index for index, (name, _) in enumerate(ability.exports) if name == "Main"))
        event_times = []
        for index in range(4):
            name = "GAA_GameplayEvent_C_" + str(index)
            raw = functions[name]
            tag_index, number = struct.unpack_from("<II", raw, 4)
            assert number == 0
            assert ability.names[tag_index] == "GameplayEvent.Ability.Action.ChiRen." + ("CanRelease" if index == 0 else "GainCharge")
            event_times.append(next(action["startTime"] for action in timeline["actions"] if action["action"] == name))
        assert len(evidence["recipes"]) == 3
        for index, item in enumerate(evidence["recipes"]):
            move = catalog[item["recipe"]["id"]]
            assert move == item["recipe"]
            assert move["activateByClass"] and move["abilityClass"] == path + ".GA_Hun_ChiRen_C_C"
            assert move["prepareInput"] == "/PBZInput/InputActions/IA_CombatArt_Hold.IA_CombatArt_Hold"
            assert move["inputAction"] == "/PBZInput/InputActions/IA_CombatArt.IA_CombatArt"
            assert move["entryPoint"] == "ToRelease" and not move.get("force")
            assert move["requiredTags"] == ["CharacterState.Space.Ground", "Weapon.Type.LongBlade.ChiRen"]
            assert move["activeBooleanProperties"] == ["Current Shachi Reinforced"]
            gate = move["entryWhen"]
            assert gate["timeline"] == "Main" and gate["property_minimum"] == {"ChargeCount": index}
            assert event_times[index] < gate["minimum_time"] < event_times[index] + 0.00001
            if index < 2:
                assert gate["before_time"] == event_times[index + 1] and gate["cancel_on_expire"]
            release_name = "Release_" + str(index + 1).zfill(2)
            release = combat_art.timeline(ability, next(i for i, (name, _) in enumerate(ability.exports) if name == release_name))
            bullets = [reference["path"] for action in release["actions"]
                       for reference in combat_art.object_references(ability, ability.exports[action["actionPointer"] - 1][1])
                       if action["actionPointer"] > 0]
            assert item["evidence"]["releaseBullet"] in bullets
            assert move["category"] == "weapons/Weapon_014/IA_UltimateSkill"
        print("PASS: ChiRen equipment grant, native charge selector, original event windows and all three qualified release recipes")
    finally:
        archive.close()


if __name__ == "__main__":
    main()
