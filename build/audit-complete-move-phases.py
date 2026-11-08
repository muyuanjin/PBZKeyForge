"""Record each complete move's native attack set and independent phase bounds."""
import argparse
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("phase_combat", ROOT / "build/extract-combat-art-phases.py")
combat = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat)


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def extract(archive):
    symbols, cache = combat.ScriptObjects(archive.paks), {}
    catalog = read("assets/key-enhancement/complete-moves.json")["moves"]
    weapon_sources = read("docs/complete-move-weapon-audit.json")["abilities"]
    executions = read("docs/weapon-execution-audit.json")["equipment"]

    def inspect(path):
        if path in cache:
            return cache[path]
        package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        classes = [symbols.resolve(package, struct.unpack_from("<Q", package.data, start + i * 72 + 32)[0])
                   for i in range(len(package.exports))]
        functions, timelines, parent = [], {}, None
        for i, (name, _) in enumerate(package.exports):
            if classes[i].endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, struct.unpack_from("<Q", package.data, start + i * 72 + 40)[0])
            if classes[i] == "/Script/CoreUObject.Function":
                functions.append(name)
            if classes[i] != "/Script/LegoAbility.LGATimeline":
                continue
            timeline = combat.timeline(package, i)
            attacks = []
            for action in timeline["actions"]:
                pointer = action["actionPointer"]
                if pointer <= 0:
                    continue
                kind = classes[pointer - 1]
                if not any(token in kind for token in ("Bullet", "Damage", "HitBox")) or "ShachiBulletInstant" in kind:
                    continue
                refs = combat.object_references(package, package.exports[pointer - 1][1])
                attacks.append({"action": action["action"], "class": kind,
                                "startTime": action["startTime"], "endTime": action["startTime"] + action["duration"],
                                "resources": list(dict.fromkeys(ref["path"] for ref in refs))})
            timelines[name] = {"owner": path, "timeline": name, "attacks": attacks}
        result = {"parent": parent, "functions": functions, "timelines": timelines}
        cache[path] = result
        return result

    def ancestry(path):
        chain = []
        while path and path.casefold() in archive.packages:
            assert path not in [owner for owner, _ in chain], path
            row = inspect(path)
            chain.append((path, row))
            path = row["parent"]
        return chain

    rows = []
    for move in catalog:
        if move.get("abilityClass"):
            paths = [move["abilityClass"].split(".")[0]]
        elif move.get("executionType"):
            paths = sorted({entry["executionAbility"] for equipment in executions
                            for entry in equipment["executionAnims"]["entries"]
                            if entry["executionType"] == move["executionType"] and entry["channel"] == move["executionChannel"]})
        else:
            paths = sorted(path for path, row in weapon_sources.items() if move["tag"] in row["effectiveTagCandidates"])
        assert paths, move["id"]
        windows = move.get("phaseWindows", [move["phaseWindow"]] if move.get("phaseWindow") else [])
        kind = ("bounded-timeline-phase" if move.get("completionWhen") else "timeline-suffix" if windows else
                "native-callback" if move.get("entryPoint") else "execution-channel" if move.get("executionType") else "native-activation")
        row = {"id": move["id"], "selection": kind, "abilityPackages": paths,
               "entryPoint": move.get("entryPoint"), "entryWhen": move.get("entryWhen"),
               "completionWhen": move.get("completionWhen"), "completionEffects": move.get("completionEffects", []),
               "attackSelection": "Exact retained interval" if windows else "Native activation or callback selects its original conditional timeline and attack branch",
               "attackSets": []}
        for path in paths:
            chain, timelines = ancestry(path), {}
            for owner, info in reversed(chain):
                timelines.update(info["timelines"])
            endpoint = move.get("entryPoint")
            if endpoint and not endpoint.startswith("/Script/"):
                callback = endpoint.rsplit(":", 1)[-1]
                assert any(callback in info["functions"] for _, info in chain), (move["id"], endpoint)
            for window in windows:
                assert window["timeline"] in timelines, (move["id"], window)
            for name, timeline in sorted(timelines.items()):
                if not timeline["attacks"]:
                    continue
                window = next((window for window in windows if window["timeline"] == name), None)
                if windows and window is None:
                    continue
                completion = move.get("completionWhen", {})
                cutoff = completion.get("minimum_time", float("inf")) if name in completion.get(
                    "timelines", [completion.get("timeline")]) else float("inf")
                entry = window["entry_time"] if window else 0
                retained = [attack for attack in timeline["attacks"] if attack["endTime"] >= entry and attack["startTime"] < cutoff]
                if windows:
                    assert retained, (move["id"], name)
                    assert abs(max(attack["endTime"] for attack in retained) - window["attack_end"]) < .00001, (move["id"], name)
                    assert abs(min(max(entry, attack["startTime"]) for attack in retained) - window["attack_start"]) < .00001, (move["id"], name)
                row["attackSets"].append({"ability": path, "owner": timeline["owner"], "timeline": name,
                    "retainedAttacks": retained,
                    "omittedEarlierAttacks": [attack for attack in timeline["attacks"] if attack["endTime"] < entry],
                    "omittedLaterAttacks": [attack for attack in timeline["attacks"] if attack["startTime"] >= cutoff]})
        rows.append(row)
    separated_finals = []
    for row in rows:
        if not row["completionWhen"]:
            continue
        for branch in row["attackSets"]:
            omitted = branch["omittedLaterAttacks"]
            if not omitted:
                continue
            entrances = []
            for candidate in rows:
                if candidate["selection"] != "timeline-suffix":
                    continue
                for other in candidate["attackSets"]:
                    if all(other[key] == branch[key] for key in ("ability", "owner", "timeline")) and all(
                            attack in other["retainedAttacks"] for attack in omitted):
                        entrances.append(candidate["id"])
            assert entrances, (row["id"], branch["timeline"], "Missing independent entrance for omitted later attacks")
            separated_finals.append({"phase": row["id"], "timeline": branch["timeline"],
                                     "omittedAttackCount": len(omitted), "independentEntrances": entrances})
    return {"build": "CL-302256", "runtimeVerified": False,
            "evidenceType": "All complete-move classes, inherited cooked timelines, original callbacks and attack resources",
            "coverage": {"recipes": len(rows), "boundedRecipes": sum(bool(row["completionWhen"]) for row in rows),
                         "boundedBranches": sum(len(row["completionWhen"].get("timelines", [row["completionWhen"].get("timeline")]))
                                                for row in rows if row["completionWhen"]),
                         "readPackages": len(cache), "localTimelines": sum(len(row["timelines"]) for row in cache.values())},
            "recipes": rows,
            "separatedFinals": separated_finals,
            "limitations": ["Native activation/callback attack sets enumerate conditional native branches; they do not claim every branch executes simultaneously.",
                            "Timeline nodes are not all damage callbacks, and are not proof of in-game animation or final health damage."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = extract(archive)
    finally:
        archive.close()
    output = ROOT / "docs/all-move-phase-validation.json"
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Audited %(recipes)d recipes, %(boundedRecipes)d bounded phases, %(boundedBranches)d finite native branches" % result["coverage"])


if __name__ == "__main__":
    main()
