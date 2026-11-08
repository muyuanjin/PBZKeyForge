"""Extract attack coefficients for every bindable complete move.

The catalog identities and native timelines are read from the checked-in move
resources. BulletData payloads and their nested projectile resources are read
again from the installed game. Archive keys stay in memory. This command does
not run or modify the game.
"""
import argparse
import importlib.util
import json
import math
import pathlib
import re
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("phase_combat", ROOT / "build/extract-combat-art-phases.py")
combat = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat)
e = combat.e

DAMAGE_PREFIX = "Damage.Attack.Target."
DAMAGE_FIELDS = {
    "PhysicalDamage": "physical",
    "PoiseDamage": "poise",
    "ElementDamage": "element",
    "AngerDamage": "anger",
    "ChaosDamage": "chaos",
    "LightningDamage": "lightning",
    "FireDamage": "fire",
    "IceDamage": "ice",
    "TrueDamage": "true",
}


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def package_path(reference, packages, action=None):
    candidate = reference.partition(".")[0]
    if candidate.casefold() in packages:
        return candidate
    stem, separator, leaf = reference.rpartition(".")
    numbered = stem + "_" + leaf if separator else reference
    if numbered.casefold() in packages:
        return numbered
    if action:
        suffix = action.rsplit("_", 1)[-1]
        numbered = candidate + "_" + suffix
        if suffix.isdigit() and numbered.casefold() in packages:
            return numbered
    return candidate


def unique_values(values):
    return sorted({round(value, 6) for value in values})


def bullet_roots(package, path):
    name = path.rsplit("/", 1)[-1].casefold()
    candidates = {name, name + "_c"}
    return [payload for export, payload in package.exports
            if export.casefold() in candidates and not export.startswith("Default__")]


def variant_sort_key(path):
    match = re.search(r"_(\d+)(_e)?$", path, re.IGNORECASE)
    if match:
        return (0, int(match.group(1)), 1 if match.group(2) else 0, path)
    if path.casefold().endswith("_e"):
        return (1, 0, 0, path)
    return (2, 0, 0, path)


def bullet_variants(path, packages):
    folder, _, leaf = path.rpartition("/")
    prefix = folder + "/" + leaf + "_"
    result = []
    for key in packages:
        if not key.startswith(prefix.casefold()):
            continue
        suffix = key[len(prefix):]
        if re.match(r"^(?:\d+)(?:_e)?$", suffix, re.IGNORECASE):
            result.append(path[:len(path) - len(leaf)] + key[len(folder) + 1:])
    return sorted(result, key=variant_sort_key)


def damage_fields(package, payload):
    wanted = {index: DAMAGE_FIELDS[name[len(DAMAGE_PREFIX):]]
              for index, name in enumerate(package.names)
              if name.startswith(DAMAGE_PREFIX) and name[len(DAMAGE_PREFIX):] in DAMAGE_FIELDS}
    result = {}
    for index, category in wanted.items():
        marker = b"\x03" + struct.pack("<I", index)
        position = 0
        while True:
            position = payload.find(marker, position)
            if position < 0:
                break
            if position + 13 <= len(payload) and struct.unpack_from("<I", payload, position + 5)[0] == 0:
                value = struct.unpack_from("<f", payload, position + 9)[0]
                if math.isfinite(value) and value >= 0:
                    result.setdefault(category, []).append(value)
            position += 1
    return result


def extract(archive):
    symbols = combat.ScriptObjects(archive.paks)
    package_cache = {}
    bullet_cache = {}
    catalog = sum((read("assets/key-enhancement/" + name + ".json")["moves"]
                   for name in ("complete-moves", "weapon-moves", "shadow-moves")), [])
    weapon_sources = read("docs/complete-move-weapon-audit.json")["abilities"]
    executions = read("docs/weapon-execution-audit.json")["equipment"]

    def inspect(path):
        key = path.casefold()
        if key in package_cache:
            return package_cache[key]
        package = e.Package(archive.resource(archive.packages[key]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        classes = [symbols.resolve(package, struct.unpack_from("<Q", package.data, start + index * 72 + 32)[0])
                   for index in range(len(package.exports))]
        functions, timelines, parent = [], {}, None
        for index, (name, _) in enumerate(package.exports):
            if classes[index].endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, struct.unpack_from("<Q", package.data, start + index * 72 + 40)[0])
            if classes[index] == "/Script/CoreUObject.Function":
                functions.append(name)
            if classes[index] != "/Script/LegoAbility.LGATimeline":
                continue
            timeline = combat.timeline(package, index)
            attacks = []
            for action in timeline["actions"]:
                pointer = action["actionPointer"]
                if pointer <= 0 or pointer > len(classes):
                    continue
                kind = classes[pointer - 1]
                if not any(token in kind for token in ("Bullet", "Damage", "HitBox")) or "ShachiBulletInstant" in kind:
                    continue
                payload = package.exports[pointer - 1][1]
                resources = []
                for resource_index, reference in enumerate(combat.object_references(package, payload)):
                    if "/BulletData/" not in reference["path"] or "/Prototypes/" in reference["path"]:
                        continue
                    resources.append({
                        "action": action["action"],
                        "index": resource_index,
                        "path": package_path(reference["path"], archive.packages, action["action"]),
                        "reference": reference["path"],
                    })
                attacks.append({
                    "action": action["action"],
                    "actionIndex": action["index"],
                    "class": kind,
                    "startTime": action["startTime"],
                    "endTime": action["startTime"] + action["duration"],
                    "resources": resources,
                })
            timelines[name] = {"owner": path, "timeline": name, "attacks": attacks}
        result = {"parent": parent, "functions": functions, "timelines": timelines}
        package_cache[key] = result
        return result

    def ancestry(path):
        chain = []
        while path and path.casefold() in archive.packages:
            if path.casefold() in {owner.casefold() for owner, _ in chain}:
                raise ValueError("Cyclic ability ancestry: " + path)
            row = inspect(path)
            chain.append((path, row))
            path = row["parent"]
        return chain

    def nested_bullets(path):
        key = path.casefold()
        if key not in archive.packages:
            return []
        package = e.Package(archive.resource(archive.packages[key]))
        result = []
        for _, payload in package.exports:
            for reference in combat.object_references(package, payload):
                target = package_path(reference["path"], archive.packages)
                if ("/BulletData/" in target and "/Prototypes/" not in target
                        and target.casefold() in archive.packages
                        and target.casefold() != key):
                    result.append(target)
        return list(dict.fromkeys(result))

    def bullet_insight(path):
        if path in bullet_cache:
            return bullet_cache[path]
        package = e.Package(archive.resource(archive.packages[path.casefold()]))
        roots = bullet_roots(package, path)
        if not roots:
            raise ValueError("BulletData root export not found: " + path)
        fields = {}
        for payload in roots:
            for category, values in damage_fields(package, payload).items():
                fields.setdefault(category, []).extend(values)
        for child in nested_bullets(path):
            child_fields = bullet_insight(child)
            for category, values in child_fields.items():
                fields.setdefault(category, []).extend(values)
        result = {category: unique_values(values) for category, values in fields.items()}
        bullet_cache[path] = result
        return result

    rows, seen = [], set()
    for move in catalog:
        if move["id"] in seen:
            raise ValueError("Duplicate move identity: " + move["id"])
        seen.add(move["id"])
        if move.get("abilityClass"):
            paths = [move["abilityClass"].split(".")[0]]
        elif move.get("executionType"):
            paths = sorted({
                entry["executionAbility"]
                for equipment in executions
                for entry in equipment["executionAnims"]["entries"]
                if entry["executionType"] == move["executionType"]
                and entry["channel"] == move["executionChannel"]
            })
        else:
            paths = sorted(path for path, row in weapon_sources.items()
                           if move["tag"] in row["effectiveTagCandidates"])
        if not paths:
            raise ValueError("Move has no ability package: " + move["id"])

        windows = move.get("phaseWindows", [move["phaseWindow"]] if move.get("phaseWindow") else [])
        retained = []
        for ability in paths:
            chain, timelines = ancestry(ability), {}
            for _, info in reversed(chain):
                timelines.update(info["timelines"])
            endpoint = move.get("entryPoint")
            if endpoint and not endpoint.startswith("/Script/"):
                callback = endpoint.rsplit(":", 1)[-1]
                if not any(callback in info["functions"] for _, info in chain):
                    raise ValueError("Move callback not found: %s %s" % (move["id"], endpoint))
            for window in windows:
                if window["timeline"] not in timelines:
                    raise ValueError("Move timeline not found: %s %s" % (move["id"], window["timeline"]))
            for name, timeline in timelines.items():
                window = next((window for window in windows if window["timeline"] == name), None)
                if windows and window is None:
                    continue
                completion = move.get("completionWhen", {})
                branches = completion.get("timelines", [completion.get("timeline")])
                cutoff = completion.get("minimum_time", float("inf")) if name in branches else float("inf")
                entry = window["entry_time"] if window else 0
                retained.extend(dict(attack, ability=ability, timeline=name, timelineAttacks=timeline["attacks"])
                                for attack in timeline["attacks"]
                                if attack["endTime"] >= entry and attack["startTime"] < cutoff)

        fields = {}
        unresolved = []
        variant_cache = {}
        def variant_for(attack, resource):
            base = resource["path"]
            cache_key = (attack["ability"].casefold(), attack["timeline"], base.casefold())
            if cache_key in variant_cache:
                return variant_cache[cache_key]
            variants = bullet_variants(base, archive.packages)
            exact = {item["path"].casefold() for candidate in attack["timelineAttacks"]
                     for item in candidate["resources"] if item["path"].casefold() != base.casefold()}
            remaining = [variant for variant in variants if variant.casefold() not in exact]
            occurrences = [(candidate["actionIndex"], item["index"])
                           for candidate in attack["timelineAttacks"]
                           for item in candidate["resources"]
                           if item["path"].casefold() == base.casefold()]
            result = {}
            if len(remaining) == len(occurrences):
                result = {occurrence: variant for occurrence, variant in zip(occurrences, remaining)}
            variant_cache[cache_key] = result
            return result
        for attack in retained:
            for resource in attack["resources"]:
                path = resource["path"]
                if path.casefold() not in archive.packages:
                    resolved = variant_for(attack, resource).get((attack["actionIndex"], resource["index"]))
                    if resolved is None:
                        if resource["reference"] not in unresolved:
                            unresolved.append(resource["reference"])
                        continue
                    path = resolved
                for category, values in bullet_insight(path).items():
                    fields.setdefault(category, []).extend(values)
        row = {"id": move["id"], "attackNodes": len(retained)}
        if unresolved:
            row["unresolvedBulletData"] = sorted(unresolved)
        for category, values in sorted(fields.items()):
            row[category] = unique_values(values)
        rows.append(row)

    rows.sort(key=lambda row: row["id"])
    damage_rows = [row for row in rows if any(category in row for category in DAMAGE_FIELDS.values())]
    return {
        "schema": 1,
        "build": "CL-302256",
        "source": "Inherited LGATimeline attack nodes and their referenced BulletData damage coefficients",
        "runtimeVerified": False,
        "coverage": {
            "moves": len(rows),
            "movesWithAttackNodes": sum(row["attackNodes"] > 0 for row in rows),
            "movesWithCoefficients": len(damage_rows),
            "physicalCoefficients": sum("physical" in row for row in rows),
            "poiseCoefficients": sum("poise" in row for row in rows),
            "movesWithUnresolvedBulletData": sum("unresolvedBulletData" in row for row in rows),
            "readBulletPackages": len(bullet_cache),
        },
        "moves": rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    parser.add_argument("--build", default="CL-302256")
    parser.add_argument("--output", type=pathlib.Path,
                        default=ROOT / "assets/key-enhancement/move-insights.json")
    args = parser.parse_args()
    archive = e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        result = extract(archive)
    finally:
        archive.close()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Extracted attack insight for %(moves)d moves, %(movesWithAttackNodes)d with attack nodes, "
          "%(movesWithCoefficients)d with coefficients" % result["coverage"])


if __name__ == "__main__":
    main()
