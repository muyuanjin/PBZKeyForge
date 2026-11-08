"""Classify native weapon input-route restrictions without fabricating game state."""
import argparse
import importlib.util
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("weapon_limit_entries", ROOT / "build/extract-weapon-move-entries.py")
weapons = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(weapons)


def extract(archive, symbols):
    audit = weapons.read_json(ROOT / "docs/weapon-move-entries.json")
    catalog = {x["id"]: x for x in weapons.read_json(ROOT / "assets/key-enhancement/weapon-moves.json")["moves"]}
    names = weapons.read_json(ROOT / "docs/game-move-names-weapons-evidence.json")
    training = {tag for record in names["records"] for tag in record["upgradeTags"]}
    sources = weapons.read_json(ROOT / "docs/ability-source-audit.json")
    training_sources = []
    for effect in sources["upgradeEffects"]:
        if not any(tag.startswith("CharacterState.WeaponUpgrade.") for tag in effect["addedTags"]):
            continue
        package = weapons.special.inspect(archive, symbols, effect["effect"])[0]
        components, _ = weapons.ability_sources.effect_components(package, symbols)
        tags = sorted({tag for component in components for tag in component.get("targetTags", {}).get("addedTags", [])
                       if tag.startswith("CharacterState.WeaponUpgrade.")})
        training.update(tags)
        training_sources.append({"effect": effect["effect"], "addedTags": tags})
    equipment_tags = {tag for config in audit["equipment"] for tag in config["equipTags"]}
    groups = ("equipment", "training", "space", "stance", "predecessor")
    cache = {}
    def cdo_references(path):
        if path not in cache:
            package = weapons.special.inspect(archive, symbols, path)[0]
            cache[path] = [{"export": name, "references": weapons.special.tag_references(package, payload)}
                           for name, payload in package.exports if name.startswith("Default__")]
        return cache[path]
    def group(tag):
        if tag in equipment_tags or tag in ("Weapon.Type.Global.SingleHanded", "Weapon.Type.Global.DualHanded"):
            return "equipment"
        if tag in training:
            return "training"
        if tag in ("CharacterState.Space.Ground", "CharacterState.Space.Air"):
            return "space"
        if tag == "CharacterState.AbilityState.Stance.HoldingStanceKey":
            return "stance"
        if tag.startswith("Ability.Action."):
            return "predecessor"
        return None
    result = {}
    associations = [(config, row) for config in audit["equipment"] for row in config["entries"]]
    associations += [(None, row) for row in audit["effectEntries"]]
    for config, row in associations:
        move = catalog[row["id"]]
        entry = result.setdefault(row["id"], {"groups": {g: {"requiredTags": [], "ignoredTags": []} for g in groups},
                                              "preparation": {"equipmentConfigs": [], "unclassifiedRequiredTags": [], "unclassifiedIgnoredTags": []},
                                              "nativeRoutes": [], "inheritedCdoTagReferences": []})
        if config and config["configId"] not in entry["preparation"]["equipmentConfigs"]:
            entry["preparation"]["equipmentConfigs"].append(config["configId"])
        route = row.get("nativeInputRoute")
        if route and route not in entry["nativeRoutes"]:
            entry["nativeRoutes"].append(route)
        requirements = {"requiredTags": list(move.get("requiredTags", [])) + (route or {}).get("requiredTags", []),
                        "ignoredTags": (route or {}).get("ignoredTags", [])}
        for kind, tags in requirements.items():
            for tag in tags:
                category = group(tag)
                target = entry["groups"][category][kind] if category else entry["preparation"]["unclassified" + kind[0].upper() + kind[1:]]
                if tag not in target:
                    target.append(tag)
        if not entry["inheritedCdoTagReferences"]:
            entry["inheritedCdoTagReferences"] = [{"owner": path, "exports": cdo_references(path)} for path in row["ancestry"]]
        entry["preparation"]["requiresGrantedAbility"] = move["abilityClass"]
    bronze = weapons.read_json(ROOT / "docs/complete-move-weapon-audit.json")["bronzeActivationRequiredTagsEvidence"]
    for identity, entry in result.items():
        if catalog[identity]["abilityClass"].split(".")[0] == bronze["package"]:
            entry["decodedGasQualification"] = {"ActivationRequiredTags": bronze["tags"], "evidence": bronze}
            for tag in bronze["tags"]:
                if tag not in entry["groups"]["training"]["requiredTags"]:
                    entry["groups"]["training"]["requiredTags"].append(tag)
    projected = {}
    for identity, entry in result.items():
        source = catalog[identity].get("sourceId")
        if not source:
            continue
        value = projected.setdefault(source, {"groups": {g: {"requiredTags": [], "ignoredTags": []} for g in groups},
                                              "concreteEntries": [], "nativeRoutes": [],
                                              "preparation": {"equipmentConfigs": [], "unclassifiedRequiredTags": [], "unclassifiedIgnoredTags": []}})
        value["concreteEntries"].append(identity)
        for category in groups:
            for kind in ("requiredTags", "ignoredTags"):
                value["groups"][category][kind] = sorted(set(value["groups"][category][kind] + entry["groups"][category][kind]))
        for kind in value["preparation"]:
            value["preparation"][kind] = sorted(set(value["preparation"][kind] + entry["preparation"][kind]))
        for route in entry["nativeRoutes"]:
            if route not in value["nativeRoutes"]:
                value["nativeRoutes"].append(route)
    result.update(projected)
    return {"build": audit["build"], "runtimeVerified": False, "entries": result,
            "trainingEffectSources": training_sources,
            "classificationTags": {"training": sorted(training), "equipment": sorted(equipment_tags),
                                   "space": ["CharacterState.Space.Ground", "CharacterState.Space.Air"],
                                   "stance": ["CharacterState.AbilityState.Stance.HoldingStanceKey"]},
            "semantics": {"nativeRoutes": "Alternatives remain separate; grouped tags are the union for classification, not a combined activation predicate.",
                          "inheritedCdoTagReferences": "Raw serialized CDO references are candidates, not decoded ActivationRequiredTags or ActivationBlockedTags.",
                          "preparation": "Actual granted spec, equipment/config objects and special state remain required unless prepared through their native lifecycle."}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, required=True, type=pathlib.Path)
    args = parser.parse_args()
    archive = weapons.combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = extract(archive, weapons.combat.ScriptObjects(args.paks))
        (ROOT / "docs/move-restrictions-weapons.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
        print("Weapon restriction records:", len(result["entries"]))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
