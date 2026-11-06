"""Read resource families and native input-to-ability routes from CL-302256."""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)


def extract(archive, universe):
    symbols, records = c.ScriptObjects(archive.paks), {}

    def read(path):
        key = path.split(".")[0].casefold()
        if key in records:
            return records[key]
        if key not in archive.packages:
            return None
        package = c.e.Package(archive.resource(archive.packages[key]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        parent, class_name = None, None
        for index, (name, _) in enumerate(package.exports):
            cls, superclass = struct.unpack_from("<QQ", package.data, start + index * 72 + 32)
            if symbols.resolve(package, cls).endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, superclass)
                # Package names have no fixed position in the local name batch.
                owner = next(n for n in package.names if n.startswith("/") and n.casefold() == key)
                class_name = owner + "." + name
        row = {"class": class_name, "parent": parent, "ownTags": c.cdo_tag_candidates(package), "resources": []}
        records[key] = row
        inherited = read(parent) if parent else None
        resources = set(inherited["resources"] if inherited else [])
        if key.endswith("/bp_combatart_base"):
            resources.add("ultimate")
        if key.endswith(("/bp_heavyattack_base", "/bp_playerdodge_base")):
            resources.add("shachi")
        if key.endswith("/bp_shachiability_base"):
            resources.update(("spirit", "shachi"))
        row["resources"] = sorted(resources)
        row["tags"] = row["ownTags"] or (inherited["tags"] if inherited else [])
        return row

    for group in ("player-referenced", "player-and-npc-referenced"):
        for path in universe[group]:
            read(path)
    tags = {}
    for row in records.values():
        for tag in row["tags"]:
            tags[tag] = sorted(set(tags.get(tag, [])) | set(row["resources"]))
    path = "/PBZCombatAsset/ActorData/Players/Hun/InputConfig_Hun"
    package = c.e.Package(archive.resource(archive.packages[path.casefold()]))
    reader = c.e.Reader(package.exports[0][1], package.names)
    if reader.header() != {0: False, 1: False, 2: False}:
        raise ValueError("Unsupported InputConfig_Hun fields")
    for _ in range(reader.number()):
        reader.number()
    if reader.number() != 0:
        raise ValueError("Unexpected removed input binding keys")
    pairs = [(package.object_name(reader.number()), package.object_name(reader.number())) for _ in range(reader.number())]
    catalog = json.loads((ROOT / "prototype/input-router/actions.json").read_text(encoding="utf-8"))
    input_tags = {action: sorted({entry["tag"] for entry in catalog["abilities"]
                                if any(source["asset"] == binding for source in entry["sources"])})
                  for action, binding in pairs if binding}
    moves = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]
    for move in moves:
        if move.get("activeAbilityClasses") or not move.get("inputAction"):
            continue
        action = move["inputAction"].split(".")[0]
        input_tags[action] = sorted(set(input_tags.get(action, [])) | {move["tag"]})
    return {"build": "CL-302256", "runtimeVerified": False,
            "evidenceType": "Cooked player ability ancestry and InputConfig_Hun action/binding map",
            "classes": {r["class"]: r["resources"] for r in records.values() if r["class"] and r["resources"]},
            "tags": {tag: kinds for tag, kinds in tags.items() if kinds}, "inputTags": input_tags}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "assets/key-enhancement/resource-costs.json")
    args = parser.parse_args()
    universe = json.loads((ROOT / "docs/ability-source-audit.json").read_text(encoding="utf-8"))["abilityUniverse"]
    archive = c.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = extract(archive, universe)
    finally:
        archive.close()
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Resource families: %d classes, %d tags, %d native input routes" %
          (len(result["classes"]), len(result["tags"]), len(result["inputTags"])))


if __name__ == "__main__":
    main()
