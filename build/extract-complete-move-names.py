"""Associate protected phase recipes with proven native move names.

The weapon and shadow extractors own table/upgrade/ability relationships. This
projection follows their exact granted classes and keeps phase descriptions
separate from official move names.
"""
import argparse
import copy
import importlib.util
import json
import pathlib


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("shadow_names", ROOT / "build/extract-shadow-move-entries.py")
shadow = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(shadow)


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def phase_parts(move, named):
    parts = [part for part in move.get("labelParts", []) if "." in part or part.isdigit()]
    if move["id"].startswith("move:WeaponPhase."):
        # The original entrance's combo index is unrelated to the requested
        # independent phase. Keep only this phase's actual descriptive suffix.
        anchors = ("catalog.followup", "moves.finisher", "moves.release")
        first = next((i for i, part in enumerate(parts) if part in anchors), len(parts))
        suffix = parts[first:]
        for modifier in ("catalog.air", "moves.perfect", "catalog.counter"):
            if modifier in parts[:first]:
                suffix.insert(0, modifier)
        parts = suffix
    if named:
        parts = [part for part in parts if part != "moves.combatart"]
    if move["id"].startswith("move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHit"):
        parts = ["catalog.onhit" if part == "catalog.counter" else part for part in parts]
    return parts


def extract(recipes, contributions, training, localization):
    languages = list(localization)
    sources = {}
    for contribution in contributions:
        assert contribution["build"] == "CL-302256"
        sources.update(contribution["entries"])
    by_class = {}
    for identity, item in sources.items():
        if item.get("evidence", {}).get("recipeId"):
            continue
        for ability in item.get("evidence", {}).get("abilityClasses", []):
            by_class.setdefault(ability, []).append((identity, item))

    # Every language must refer to the same native FText key. Merely finding a
    # Chinese string elsewhere is not a name-to-ability relationship.
    def text_keys(labels):
        if not labels or set(labels) != set(languages):
            return []
        return sorted(key for (namespace, key), value in localization["zh-Hans"].items()
                      if namespace == "Default" and labels["zh-Hans"] == value
                      and all(localization[lang].get((namespace, key)) == labels[lang] for lang in languages))

    training_by_class = {entry["ability"]: entry for entry in training["trainingFollowups"]}
    result, coverage = {}, []
    for move in recipes:
        identity, ability = move["id"], move.get("abilityClass")
        matches = by_class.get(ability, [])
        weapon = move.get("weaponLabels", {})
        owned = [(key, value) for key, value in matches if value.get("labels") and weapon
                 and all(value["labels"].get(lang, "").split(" · ")[0] == weapon.get(lang, "").split(" · ")[0]
                         for lang in languages)]
        if owned:
            matches = owned
        entry = None
        relationship = ""
        explicit = sources.get(identity)
        if explicit and explicit.get("evidence", {}).get("recipeId"):
            explicit = None
        if explicit and explicit.get("labels") and ability not in training_by_class:
            entry = copy.deepcopy(sources[identity])
            relationship = "Explicit phase-to-native function relationship"
        elif ability in training_by_class:
            original = training_by_class[ability]
            name = original["localization"]
            assert all(localization[lang][("Default", name["key"])] == name["labels"][lang] for lang in languages)
            entry = {"labels": {lang: original["weaponLabels"][lang] + " · " + name["labels"][lang] for lang in languages},
                     "aliases": {lang: [name["labels"][lang]] for lang in languages},
                     "effects": [], "evidence": {"nameKeys": [name["key"]], "abilityClasses": [ability],
                                                    "source": "docs/complete-move-shadow-training.json"}}
            relationship = "Exact normally granted training ability and its original qualified route"
        elif matches:
            labels = [item["labels"] for _, item in matches if item.get("labels")]
            if labels and all(label == labels[0] for label in labels):
                selected = next(item for _, item in matches if item.get("labels"))
                entry = copy.deepcopy(selected)
                relationship = "Same exact granted ability class as the native entrance"
                entry.pop("labelParts", None)
        if entry is None:
            labels = move.get("weaponLabels")
            keys = text_keys(labels)
            entry = {"aliases": {}, "effects": [], "evidence": {"nameKeys": keys,
                     "abilityClasses": [ability] if ability else [], "source": "Existing recipe equipment identity and original locres"}}
            if keys:
                entry["labels"] = labels
                relationship = "Official equipment name; no independent proper move name established"
            else:
                relationship = "No unique official proper move name established"
        related = ([explicit] if explicit else []) + [value[1] for value in matches]
        for source in related:
            for effect in source.get("effects", []):
                if effect not in entry.setdefault("effects", []):
                    entry["effects"].append(copy.deepcopy(effect))
            for language, aliases in source.get("aliases", {}).items():
                current = entry.setdefault("aliases", {}).setdefault(language, [])
                current.extend(alias for alias in aliases if alias not in current)
        keys = entry.get("evidence", {}).get("nameKeys", [])
        labels = entry.get("labels", {})
        named = any("FunctionName" in key and all(localization[lang].get(("Default", key))
                    in labels.get(lang, "").split(" · ") for lang in languages) for key in keys)
        # Explicit contributions may already define the more precise phase
        # suffix (for example, the One-Eyed Bow's Gaze finisher).
        if not explicit or not explicit.get("labels") or ability in training_by_class:
            entry["labelParts"] = phase_parts(move, named)
        evidence = entry.setdefault("evidence", {})
        evidence["recipeId"] = identity
        evidence["relationship"] = relationship
        if matches:
            evidence["nativeEntranceIds"] = [value[0] for value in matches]
        entry.setdefault("effects", [])
        entry.setdefault("aliases", {})
        result[identity] = entry
        coverage.append({"id": identity, "relationship": relationship,
                         "nameKeys": keys, "hasNativeMainName": named})
    return {"schemaVersion": 1, "build": "CL-302256", "entries": result, "coverage": coverage}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    parser.add_argument("--source", type=pathlib.Path, action="append", required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "docs/game-move-names-combat.json")
    args = parser.parse_args()
    details = read(ROOT / "assets/key-enhancement/action-localization.json")
    archive = shadow.combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        localization = shadow.read_labels(archive, details["languages"])
    finally:
        archive.close()
    result = extract(read(ROOT / "assets/key-enhancement/complete-moves.json")["moves"],
                     [read(path) for path in args.source], read(ROOT / "docs/complete-move-shadow-training.json"), localization)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Associated %d protected recipes; %d official move names" %
          (len(result["entries"]), sum(item["hasNativeMainName"] for item in result["coverage"])))


if __name__ == "__main__":
    main()
