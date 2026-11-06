"""Inspect unassigned player ability candidates and reverse-search actor/story sources."""
import argparse
import collections
import importlib.util
import json
import pathlib
import struct


SPEC = importlib.util.spec_from_file_location("sources", pathlib.Path(__file__).with_name("extract-ability-sources.py"))
sources = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sources)
e = sources.e


def parent_path(row):
    text = row["tags"].get("ParentClass", "")
    path = text.split("'", 1)[-1].rstrip("'")
    return path if path.startswith("/Script/") else path.split(".", 1)[0]


def ancestry(path, index):
    result = []
    while path.casefold() in index and path not in result:
        result.append(path)
        path = parent_path(index[path.casefold()])
    if path:
        result.append(path)
    return result


def resource_prefix(archive, entry, wanted):
    position = 144 + archive.entries * 12 + entry * 10
    offset = int.from_bytes(archive.toc[position:position + 5], "big")
    size = min(wanted, int.from_bytes(archive.toc[position + 5:position + 10], "big"))
    first, last = offset // archive.block_size, (offset + size + archive.block_size - 1) // archive.block_size
    data = b"".join(archive.block(index) for index in range(first, last))
    relative = offset % archive.block_size
    return data[relative:relative + size]


def classify(path, parents):
    if any("GA_ExecutedAbilityBase" in parent for parent in parents):
        return "paired-execution-receiver"
    if any("BP_Hun_PerfectDodge_ExecutionBase" in parent for parent in parents):
        return "perfect-dodge-paired-counter"
    if any("BP_PlayerPerfectDodge_Base_Special" in parent for parent in parents):
        return "perfect-dodge-special-counter"
    if "PerfectDodge" in " ".join(parents) or "/ExE/" in path:
        return "contextual-perfect-dodge-or-paired-execution"
    if "SWT_Tutorial" in path or "SWT_UI" in path:
        return "tutorial-or-ui-weapon-switch"
    if any(text in path for text in ("ClimLadder", "CrossObstacle", "DashInRoot")):
        return "traversal-or-root-movement"
    if "/Mobs/" in path:
        return "npc-derived-from-player-ability"
    return "passive-or-upgrade"


def extract(archive, assets, source):
    index = sources.registry_index(assets)
    markers = ("BP_Hun_", "BP_Player", "BP_HeavyAttack", "BP_Shachi", "BP_CombatArt", "BP_LightAttack", "BP_NormalAttack")
    candidates = []
    symbols = sources.combat_art.ScriptObjects(archive.paks)
    for row in source["abilityUniverse"]:
        if row["classification"] != "source-unassigned":
            continue
        path = row["package"]
        parents = ancestry(path, index)
        if "/Players/" not in path and not any(marker in " ".join(parents[1:]) for marker in markers):
            continue
        package = e.Package(archive.resource(archive.packages[path.casefold()]))
        exports_start = struct.unpack_from("<I", package.data, 32)[0]
        timelines, functions = [], []
        for i, (name, payload) in enumerate(package.exports):
            identity = struct.unpack_from("<Q", package.data, exports_start + i * 72 + 32)[0]
            cls = symbols.resolve(package, identity)
            if cls == "/Script/CoreUObject.Function":
                functions.append(name)
            if cls == "/Script/LegoAbility.LGATimeline":
                timeline = sources.combat_art.timeline(package, i)
                actions = []
                for action in timeline["actions"]:
                    pointer = action["actionPointer"]
                    if pointer <= 0:
                        continue
                    identity = struct.unpack_from("<Q", package.data, exports_start + (pointer - 1) * 72 + 32)[0]
                    action_class = symbols.resolve(package, identity)
                    if any(text in action_class.rsplit("/", 1)[-1] for text in ("Bullet", "Damage", "HitBox", "GameplayEvent", "Execution")):
                        actions.append({"action": package.exports[pointer - 1][0], "class": action_class, "startTime": action["startTime"]})
                timelines.append({"name": name, "actionCount": len(timeline["actions"]), "attackOrEventActions": actions})
        candidates.append({"package": path, "parentChain": parents, "reviewGroup": classify(path, parents),
                           "abilityTagCandidates": sources.combat_art.cdo_tag_candidates(package),
                           "eventAndStateNames": [name for name in package.names if name.startswith(("GameplayEvent.", "CharacterState.", "Ability."))],
                           "functions": functions, "timelines": timelines,
                           "references": sources.references(package, index), "reverseSources": []})
    by_path = {row["package"].casefold(): row for row in candidates}
    # Search all registry Blueprint, world and config packages, including streamed
    # world cells. Read names/import headers first, then prove matched references
    # against export payloads. Actor directories are not used as a source filter.
    scan = [row for row in index.values() if sources.kind(row) != "other" or any(
        cls in ("Blueprint", "World", "DataAsset") or "Config" in cls or "DataAsset" in cls or "DataTable" in cls
        for cls in row["classes"])]
    entries = dict(archive.packages)
    for file_path, entry in archive.files.items():
        prefix, separator, relative = file_path.partition("/Content/")
        if separator and relative.endswith(".umap"):
            mount = "Game" if prefix == "../../../ProjectPBZ" else prefix.rsplit("/", 1)[-1]
            entries[("/" + mount + "/" + relative[:-5]).casefold()] = entry
    scanned, missing = 0, []
    for row in scan:
        path = row["package"].casefold()
        if path not in entries:
            missing.append(row["package"])
            continue
        entry = entries[path]
        prefix = resource_prefix(archive, entry, 60)
        header_size = struct.unpack_from("<I", prefix, 4)[0]
        header = resource_prefix(archive, entry, header_size)
        names = e.name_batch(header, 60) + e.name_batch(header, struct.unpack_from("<I", header, 48)[0])
        matches = {name.split(".", 1)[0].casefold() for name in names if name.startswith("/")} & by_path.keys()
        if matches:
            package = e.Package(archive.resource(entry))
            refs = sources.references(package, index)
            for ref in refs:
                if ref["package"].casefold() in matches and ref["package"].casefold() != path:
                    frame_actions = []
                    export_start = struct.unpack_from("<I", package.data, 32)[0]
                    for i, (name, payload) in enumerate(package.exports):
                        cls = symbols.resolve(package, struct.unpack_from("<Q", package.data, export_start + i * 72 + 32)[0])
                        if cls not in ("/Script/PBZCombatAsset.LGAAction_DurationBulletWithDeflect", "/PBZDeflectSystem/Actions/GAA_DelayDeflectProjectileBullet"):
                            continue
                        for position in range(len(payload) - 3):
                            pointer = struct.unpack_from("<i", payload, position)[0]
                            if pointer < 0 and -pointer * 8 <= export_start - package.import_offset and package.object_name(pointer) == ref["package"]:
                                frame_actions.append({"action": name, "class": cls, "counterClassPointerOffset": position})
                    by_path[ref["package"].casefold()]["reverseSources"].append({"source": row["package"], "sourceClasses": row["classes"], "reference": ref,
                                                                             "deflectFrameActions": frame_actions})
        scanned += 1
        if scanned % 200 == 0:
            archive.blocks.clear()
        if scanned % 10000 == 0:
            print("Reverse-searched %d/%d source headers" % (scanned, len(scan)), flush=True)
    return {"build": source["build"], "runtimeVerified": False, "candidateCount": len(candidates),
            "candidateGroups": dict(collections.Counter(row["reviewGroup"] for row in candidates)),
            "reverseSourcePackagesScanned": scanned, "missingSourcePackages": missing, "candidates": candidates,
            "limitations": ["Player-path or player-base ancestry selects review candidates, not proof of availability.",
                            "Reverse references prove resource use, not independent activation or a valid gameplay payload.",
                            "Source-unassigned remains the original source-root classification even when a contextual story/world reference is found."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle", "registry", "source", "output"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    source = json.loads(args.source.read_text(encoding="utf-8"))
    assets = json.loads(args.registry.read_text(encoding="utf-8"))["assets"]
    archive = e.Archive(args.game_exe, args.paks, args.oodle, source["build"])
    try:
        result = extract(archive, assets, source)
    finally:
        archive.close()
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Reviewed %d candidates across %d source headers" % (result["candidateCount"], result["reverseSourcePackagesScanned"]))


if __name__ == "__main__":
    main()
