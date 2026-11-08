"""List equipment-specific shadow abilities hidden by shared input identities.

Reads actual Equip/Owning grants, generated-class ancestry and native labels.
The default ability entrance retains normal game qualification and lifecycle;
finite charge/release recipes remain in complete-moves.json.
"""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "build" / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


combat = module("shadow_combat", "extract-combat-art-phases.py")
localization = module("shadow_localization", "extract-action-localization.py")
DEFAULT = "Ability.Action.Hun.ShachiAbility.Default"
BASE = "/PBZCombatAsset/Prototypes/Abilities/Base/BP_ShachiAbility_Base"
INPUT = "/PBZInput/InputActions/IA_ShachiAbility.IA_ShachiAbility"


def configuration(package):
    reader = combat.e.Reader(package.exports[0][1], package.names)
    result = {"equipTags": [], "owningTags": [], "equipAbilities": [], "owningAbilities": []}
    for field, zero in reader.header().items():
        if field > 6:
            break
        if zero:
            continue
        if field in (0, 1):
            result["equipTags" if field == 0 else "owningTags"] = [reader.name() for _ in range(reader.number())]
        elif field in (2, 3, 5, 6):
            values = [package.object_name(reader.number()) for _ in range(reader.number())]
            if field in (5, 6):
                result["equipAbilities" if field == 5 else "owningAbilities"] = values
        else:
            raise ValueError("Unsupported shadow configuration field: " + str(field))
    return result


def read_labels(archive, languages):
    pak = localization.Pak(archive, combat.e.Reader)
    try:
        resources = {}
        for path, entry in pak.files.items():
            language = path.split("/")[-2]
            if "/ProjectPBZ/Content/Localization/Game/" in path and path.endswith("/Game.locres"):
                resources["en" if language == "en-US" else language] = localization.parse_locres(pak.read(entry), combat.e.Reader)
        return {language: resources[language] for language in languages}
    finally:
        pak.stream.close()


def extract(archive, symbols, audit, catalog, details, native_labels, complete_moves):
    cache = {}

    def info(path):
        path = path.split(".")[0]
        if path in cache:
            return cache[path]
        package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        parent = None
        for index, _ in enumerate(package.exports):
            _, cls, superclass = struct.unpack_from("<QQQ", package.data, start + index * 72 + 24)
            if symbols.resolve(package, cls).endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, superclass)
                break
        result = {"package": package, "parent": parent, "tags": combat.cdo_tag_candidates(package)}
        cache[path] = result
        return result

    def ancestry(path):
        chain = []
        while path and path.casefold() in archive.packages:
            path = path.split(".")[0]
            if path in chain:
                raise ValueError("Cyclic shadow ability inheritance")
            chain.append(path)
            path = info(path)["parent"]
        return chain

    catalog_tags = {row["tag"] for row in catalog["abilities"]}
    moves, families, configurations = [], [], []
    for family in audit["families"]:
        grants, config_paths = set(), []
        for variant in family["configurationVariants"]:
            path = variant["package"]
            fields = configuration(info(path)["package"])
            configurations.append(dict({"equipmentId": path, "family": family["id"]}, **fields))
            grants.update(fields["equipAbilities"] + fields["owningAbilities"])
            config_paths.append(path)
        row = {"family": family["id"], "equipment": config_paths, "grantedAbilities": sorted(grants), "abilities": []}
        keys = list(dict.fromkeys(key for native in family["nativeRows"] for key in native["nameKeys"]))
        name_key = next((key for key in keys if all(("Default", key) in lang for lang in native_labels.values())), None)
        fallback_name = "BloodBell" if family["id"] == "XueSeLingDang" else family["id"]
        labels = ({language: data[("Default", name_key)] for language, data in native_labels.items()} if name_key
                  else {language: fallback_name for language in native_labels})
        row["nativeNameStatus"] = "resolved" if name_key else "unresolved"
        if not name_key:
            row["displayIdentity"] = fallback_name
        category_key = name_key or family["id"]
        for ability in sorted(grants):
            path = ability.split(".")[0]
            if path.casefold() not in archive.packages:
                row["abilities"].append({"abilityClass": ability, "status": "native-class"})
                continue
            chain = ancestry(path)
            candidate = {"abilityClass": ability, "ancestry": chain}
            row["abilities"].append(candidate)
            if BASE not in chain:
                candidate["status"] = "separate-passive-execution-or-context-lifecycle"
                continue
            tag_owner = next((owner for owner in chain if info(owner)["tags"]), None)
            tags = info(tag_owner)["tags"] if tag_owner else []
            candidate["tagOwner"], candidate["tagCandidates"] = tag_owner, tags
            active_tags = [tag for tag in tags if tag.startswith("Ability.Action.Hun.ShachiAbility.")]
            covered = [move for move in complete_moves if move.get("abilityClass", "").split(".")[0] == path
                       and not move.get("entryPoint") and not move.get("entryWhen")
                       and not move.get("prepareInput") and not move.get("activationAnyTags")]
            if covered:
                candidate["status"] = "existing-equipment-exact-entry"
                candidate["catalogIds"] = [move["id"] for move in covered]
                continue
            distinct = [tag for tag in active_tags if tag != DEFAULT and tag in catalog_tags]
            if distinct:
                candidate["status"] = "existing-equipment-input-entry"
                candidate["catalogIds"] = ["ability:" + tag for tag in distinct]
                continue
            if not active_tags:
                candidate["status"] = "no-shadow-input-identity"
                continue
            tag = DEFAULT if DEFAULT in active_tags else active_tags[0]
            leaf = path.rsplit("/", 1)[-1]
            identity = "move:Shadow." + family["id"] + "." + leaf
            own_timelines = next((item["ownTimelines"] for item in family["associatedAbilities"] if item["package"] == path), [])
            code = leaf.removeprefix("GA_Hun_").removeprefix("GA_")
            parts = ["catalog.normal"]
            if family["id"] in ("DuYanBow", "UnnamedBow", "YouLongCha"):
                parts = ["catalog.charge"]
            elif family["id"] == "Mojian":
                parts = ["catalog.attack", "AA"]
            elif family["id"] == "ShachiHook":
                parts = ["catalog.attack", leaf.rsplit("_", 1)[-1]]
            if tag != DEFAULT and "ability:" + tag in details["entries"]:
                parts = [part.removeprefix("@") for part in details["entries"]["ability:" + tag].get("parts", [])] or parts
            move = {"id": identity, "sourceId": "ability:" + (tag if tag in catalog_tags else DEFAULT),
                    "tag": tag, "abilityClass": path + "." + leaf + "_C", "activateByClass": True, "nativeEntry": True,
                    "inputAction": INPUT, "requiredTags": [], "priority": -5, "code": code,
                    "usageKey": "moves.nativeentrance.usage",
                    "labelParts": parts, "weaponLabels": labels,
                    "category": "shadow/" + category_key + "/IA_ShadowArm",
                    "searchAliases": list(dict.fromkeys([family["id"], path, leaf, code] + list(labels.values()) + own_timelines))}
            moves.append(move)
            candidate["status"] = "equipment-entry-added"
            candidate["moveId"] = identity
            candidate["lifecycle"] = "Normal exact-class activation of a granted ability; native charge, aim, summon and release behavior retained."
        families.append(row)
    return {"build": audit["build"], "runtimeVerified": False,
            "evidenceType": "Static actual Equip/Owning arrays, generated-class inheritance and CDO input identities",
            "configurations": configurations, "families": families, "moves": moves,
            "limitations": ["Ability entrance coverage does not establish complete internal phase coverage.",
                            "Native unlock, resource, target, aim, summon and release qualification remains in the game."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "assets/key-enhancement/shadow-moves.json")
    parser.add_argument("--evidence", type=pathlib.Path, default=ROOT / "docs/shadow-move-entries.json")
    args = parser.parse_args()
    load = lambda name: json.loads((ROOT / name).read_text(encoding="utf-8-sig"))
    audit = load("docs/complete-move-shadow-audit.json")
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, audit["build"])
    try:
        evidence = extract(archive, combat.ScriptObjects(args.paks), audit,
                           load("prototype/input-router/actions.json"), load("assets/key-enhancement/action-details.json"),
                           read_labels(archive, load("assets/key-enhancement/action-localization.json")["languages"]),
                           load("assets/key-enhancement/complete-moves.json")["moves"])
    finally:
        archive.close()
    args.output.write_text(json.dumps({"build": evidence["build"], "moves": evidence["moves"]}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.evidence.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Read %d shadow configurations in %d families; added %d equipment-specific entrances" %
          (len(evidence["configurations"]), len(evidence["families"]), len(evidence["moves"])))


if __name__ == "__main__":
    main()
