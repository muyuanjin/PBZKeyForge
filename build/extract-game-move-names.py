"""Resolve catalog move names and related training effects from game localization.

Entries reference existing catalog identities. Training effects describe native
upgrades of those entries and never introduce an additional activation route.
"""
import argparse
import importlib.util
import json
import pathlib
import re
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("shadow_entries", ROOT / "build/extract-shadow-move-entries.py")
shadow = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(shadow)


def read_json(path):
    return json.loads(path.read_text(encoding="utf8"))


def choose_variant(text, rank):
    return re.sub(r"\{([^{}]+)\}", lambda match: match[1].split("|")[min(rank, len(match[1].split("|")) - 1)], text)


def readable_description(text, language, native):
    # Cost placeholders are values supplied by the running game's UI. The
    # description here retains the upgrade's behavior, without incomplete costs.
    pieces = re.split(r"(?<=[。.!?！？])\s*", text)
    text = " ".join(piece for piece in pieces if "<dtv " not in piece and "<img " not in piece)
    inputs = {"Stance": "UI_Setting_312", "ShaChiAttack": "UI_Setting_315", "LightAttack": "UI_Setting_307",
              "HeavyAttack": "UI_Setting_308", "Execution": "UI_Setting_309", "Dodge": "UI_Setting_313",
              "SpecialAttack": "UI_Setting_314", "SwitchWeapon": "UI_Setting_316"}
    def action(match):
        key = inputs.get(match[1])
        return native[language].get(("Default", key), "") if key else ""
    text = re.sub(r'<InputAction\s+id="([^"]+)"[^>]*/>', action, text)
    text = re.sub(r"<[^>]+>", "", text)
    return re.sub(r"\s+", " ", text).strip()


def shadow_names(native):
    languages = list(native)
    def labels(key, rank=0):
        return {language: choose_variant(data[("Default", key)], rank) for language, data in native.items()}
    def full(weapon, move=None):
        return {language: labels(weapon)[language] + (" · " + labels(move)[language] if move else "") for language in languages}
    def upgrade(name, description, tag, rank=0):
        return {"key": name, "kind": "upgrade", "labels": labels(name, rank), "upgradeTags": [tag],
                "descriptionLabels": {language: readable_description(choose_variant(native[language][("Default", description)], rank), language, native) for language in languages}}
    prefix = "CharacterState.WeaponUpgrade.DuYanBow."
    laser_effects = [upgrade("PhantomEdges_FunctionName_109_15", "PhantomEdges_Function_109_15", prefix + "LaserV2"),
                     upgrade("PhantomEdges_FunctionName_109_25", "PhantomEdges_Function_109_25", prefix + "LaserEyes")]
    laser_effects += [upgrade("PhantomEdges_FunctionName_109_10", "PhantomEdges_Function_109_10", prefix + "LaserDamageFalloff.V" + str(rank + 1), rank) for rank in range(3)]
    normal_effects = [upgrade("PhantomEdges_FunctionName_109_Re", "PhantomEdges_FunctionDes_109_Re", prefix + "Evolution")]
    normal_class = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/DuYanBow/GA_DuYan_Bow_Normal.GA_DuYan_Bow_Normal_C"
    laser_class = normal_class.replace("Normal", "Laser")
    entries = {}
    def entry(identity, move_key, effects, ability_class, parts=None):
        item = {"labels": full("PhantomEdges_109", move_key),
                "aliases": {language: list(dict.fromkeys(effect["labels"][language] for effect in effects)) for language in languages},
                "effects": effects, "evidence": {"nameKeys": ["PhantomEdges_109"] + ([move_key] if move_key else []),
                "abilityClasses": [ability_class], "source": "/Game/PBZContent/DataTable/UI/DT_ShaqiWeaponData:独眼弓"}}
        if parts:
            item["labelParts"] = parts
        entries[identity] = item
    entry("ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser", "PhantomEdges_FunctionName_109_5", laser_effects, laser_class)
    entry("move:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser.Shoot", "PhantomEdges_FunctionName_109_15", laser_effects, laser_class, ["moves.finisher"])
    entry("move:Shadow.DuYanBow.GA_DuYan_Bow_Normal", None, normal_effects, normal_class, ["catalog.charge"])
    for rank in range(1, 4):
        entry("move:Ability.Action.Hun.ShachiAbility.Default.DuYanBow.Shoot.Stage" + str(rank), None,
              normal_effects, normal_class, ["catalog.charge", str(rank), "moves.release"])
    return entries


def shadow_function_records(package):
    spec = importlib.util.spec_from_file_location("weapon_name_records", ROOT / "build/extract-weapon-move-names.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return [{"offset": item["offset"], "nameKey": item["name"]["key"],
             "descriptionKey": item["description"]["key"],
             "effect": item["effect"]["package"] if item["effect"] else None}
            for item in module.function_records(package, "PhantomEdges_FunctionName_")]


def native_shadow_names(archive, native):
    table = "/Game/PBZContent/DataTable/UI/DT_ShaqiWeaponData"
    package = shadow.combat.e.Package(archive.resource(archive.packages[table.casefold()]))
    records = shadow_function_records(package)
    upgrades = {row["effect"]: row["addedTags"] for row in read_json(ROOT / "docs/ability-source-audit.json")["upgradeEffects"]}
    audit = read_json(ROOT / "docs/complete-move-shadow-audit.json")
    catalog_tags = {row["tag"] for row in read_json(ROOT / "prototype/input-router/actions.json")["abilities"]}
    moves = read_json(ROOT / "assets/key-enhancement/complete-moves.json")["moves"] + read_json(ROOT / "assets/key-enhancement/shadow-moves.json")["moves"]
    cache = {}
    def referenced_upgrade_tags(path):
        pending, visited, tags = [path], set(), set()
        while pending:
            current = pending.pop().split(".")[0]
            if current in visited or current.casefold() not in archive.packages:
                continue
            visited.add(current)
            if current not in cache:
                resource = shadow.combat.e.Package(archive.resource(archive.packages[current.casefold()]))
                serialized_tags = set()
                for index, name in enumerate(resource.names):
                    if name.startswith("CharacterState.WeaponUpgrade."):
                        value = struct.pack("<II", index, 0)
                        if any(value in data for _, data in resource.exports):
                            serialized_tags.add(name)
                links = [link for link in resource.imports if link.startswith((
                    "/PBZCombatAsset/ActorData/Players/Hun/Abilities/",
                    "/PBZCombatAsset/ActorData/Players/Hun/BulletData/",
                    "/PBZCombatAsset/Prototypes/Actions/", "/PBZCombatAsset/Prototypes/Bullet/"))]
                cache[current] = serialized_tags, links
            found, links = cache[current]
            tags.update(found)
            pending.extend(links)
        return tags, sorted(visited)
    entries, coverage = {}, []
    for family in audit["families"]:
        family_records = [record for record in records if record["effect"] and any(
            record["nameKey"] == evidence["key"] for row in family["nativeRows"] for evidence in row["localizedEvidence"])]
        weapon_key = next((key for row in family["nativeRows"] for key in row["nameKeys"]
                           if all(("Default", key) in data for data in native.values())), None)
        if not weapon_key:
            continue
        for ability in family["associatedAbilities"]:
            path = ability["package"]
            if ability["role"] != "active-or-contextual":
                continue
            tags, closure = referenced_upgrade_tags(path)
            related = [record for record in family_records if set(upgrades.get(record["effect"], [])) & tags]
            identities = [(move["id"], move) for move in moves if move.get("abilityClass", "").split(".")[0] == path]
            for tag in ability["cdoAbilityTagReferences"]:
                if tag in catalog_tags and tag.startswith("Ability.Action.Hun.ShachiAbility.") and tag != shadow.DEFAULT:
                    identities.append(("ability:" + tag, None))
            for identity, move in identities:
                routes = [route for route in family["inputRoutes"] if route["tag"] == (move.get("tag") if move else identity.removeprefix("ability:"))]
                required = set(move.get("requiredTags", []) if move else [])
                required.update(tag for route in routes for tag in route["requiredTags"])
                primary = [record for record in family_records if set(upgrades.get(record["effect"], [])) & required
                           and not record["nameKey"].endswith("_10")]
                primary_keys = list(dict.fromkeys(record["nameKey"] for record in primary))
                item = {"aliases": {language: [] for language in native}, "effects": [],
                        "evidence": {"nameKeys": [], "abilityClasses": [ability["class"]], "source": table,
                                     "referencedPackages": closure}}
                if len(primary_keys) == 1 and (move is None or move.get("nativeEntry")):
                    name_key = primary_keys[0]
                    item["labels"] = {language: data[("Default", weapon_key)] + " · " + data[("Default", name_key)] for language, data in native.items()}
                    item["evidence"]["nameKeys"] = [weapon_key, name_key]
                    if move and not move.get("nativeEntry"):
                        item["labelParts"] = move.get("labelParts", [])
                for record in related:
                    siblings = [row for row in family_records if row["nameKey"] == record["nameKey"] and row["effect"].rsplit("/", 1)[0] == record["effect"].rsplit("/", 1)[0]]
                    rank = siblings.index(record)
                    effect_labels = {language: choose_variant(data[("Default", record["nameKey"])], rank) for language, data in native.items()}
                    item["effects"].append({"key": record["nameKey"], "kind": "upgrade", "labels": effect_labels,
                        "upgradeTags": upgrades.get(record["effect"], []), "descriptionLabels": {
                            language: readable_description(choose_variant(data[("Default", record["descriptionKey"])], rank), language, native)
                            for language, data in native.items()}})
                    for language in native:
                        item["aliases"][language].append(effect_labels[language])
                if item["effects"] or item.get("labels"):
                    entries[identity] = item
                coverage.append({"id": identity, "family": family["id"], "class": path,
                                 "referencedUpgradeTags": sorted(tags), "officialNameKeys": primary_keys,
                                 "effectKeys": [record["nameKey"] for record in related]})
    families = []
    for family in audit["families"]:
        names = {row["key"]: row["text"] for source in family["nativeRows"] for row in source["localizedEvidence"] if "FunctionName_" in row["key"]}
        items = []
        for key, text in names.items():
            mapped = [row["id"] for row in coverage if row["family"] == family["id"] and key in row["officialNameKeys"] + row["effectKeys"]]
            items.append({"key": key, "nativeName": text, "catalogIds": mapped,
                          "status": "mapped-native-entrance-or-upgrade" if mapped else
                          "native-context-interaction" if key.endswith("_Terrian") else "no-proven-independent-entry-relation"})
        families.append({"family": family["id"], "names": items,
                         "status": "native-names-recorded" if items else "no-official-function-names-in-table-row"})
    laser_path = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/DuYanBow/GA_DuYan_Bow_Laser"
    laser = shadow.combat.e.Package(archive.resource(archive.packages[laser_path.casefold()]))
    eye_tag = "CharacterState.WeaponUpgrade.DuYanBow.LaserEyes"
    eye_pattern = struct.pack("<II", laser.names.index(eye_tag), 0)
    eye_actions = []
    for index, (name, data) in enumerate(laser.exports):
        if eye_pattern in data:
            eye_actions.append({"exportIndex": index, "export": name, "requiredUpgradeTag": eye_tag,
                                "tagExportOffset": data.index(eye_pattern)})
    loop_index = next(index for index, (name, _) in enumerate(laser.exports) if name == "Loop")
    loop = shadow.combat.timeline(laser, loop_index)
    for action in eye_actions:
        event = next(event for event in loop["actions"] if event["actionPointer"] == action["exportIndex"] + 1)
        action.update({"timeline": "Loop", "startTime": event["startTime"]})
    return entries, {"build": "CL-302256", "runtimeVerified": False, "table": table,
                     "typedUpgradeRecords": records, "catalogCoverage": coverage, "families": families,
                     "nativeEyeUpgrade": {"abilityClass": laser_path, "actions": eye_actions,
                                          "eyeBullet": "/PBZCombatAsset/ActorData/Players/Hun/BulletData/DuYanBow/BD_DuYan_Bow_Eye"}}


def merge_contribution(entries, contribution):
    for identity, item in contribution["entries"].items():
        if identity.startswith("weapon-art:"):
            continue
        if identity not in entries:
            entries[identity] = item
            continue
        current = entries[identity]
        if not current.get("labels") and item.get("labels"):
            current["labels"] = item["labels"]
            if "labelParts" in item:
                current["labelParts"] = item["labelParts"]
            current.setdefault("evidence", {}).setdefault("nameKeys", [])
            current["evidence"]["nameKeys"] = list(dict.fromkeys(current["evidence"]["nameKeys"] + item.get("evidence", {}).get("nameKeys", [])))
        # More specific reviewed name records own the display name; other
        # records may extend search aliases and documented native effects.
        for language, values in item.get("aliases", {}).items():
            current.setdefault("aliases", {}).setdefault(language, [])
            current["aliases"][language] = list(dict.fromkeys(current["aliases"][language] + values))
        for effect in item.get("effects", []):
            if not any(old["key"] == effect["key"] and old.get("upgradeTags", []) == effect.get("upgradeTags", [])
                       for old in current.setdefault("effects", [])):
                current["effects"].append(effect)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "assets/key-enhancement/game-move-names.json")
    parser.add_argument("--contribution", action="append", type=pathlib.Path, default=[])
    args = parser.parse_args()
    languages = read_json(ROOT / "assets/key-enhancement/action-localization.json")["languages"]
    archive = shadow.combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        native = shadow.read_labels(archive, languages)
        entries = shadow_names(native)
        additional, evidence = native_shadow_names(archive, native)
        merge_contribution(entries, {"entries": additional})
    finally:
        archive.close()
    for path in args.contribution:
        merge_contribution(entries, read_json(path))
    phases = {
        "Ability.Action.Hun.CombatArt.Atk02": ["catalog.stage", "2"],
        "Ability.Action.Hun.CombatArt.Atk03": ["catalog.stage", "3"],
        "Ability.Action.Hun.Heavy.ShuangYue.PB": ["catalog.stage", "1"],
        "Ability.Action.Hun.Heavy.ShuangYue.PBB": ["catalog.stage", "2"],
        "Ability.Action.Hun.Light.DualHook.Pursure": ["act.IA_LightAttack"],
        "Ability.Action.Hun.Heavy.DualHook.Pursure": ["act.IA_HeavyAttack"],
    }
    for stage in range(1, 4):
        phases["Ability.Action.Hun.ShachiAbility.TieKuangQuanTao.Combo.%s" % ("C" * stage)] = ["catalog.stage", str(stage)]
        phases["Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Common.%02d" % stage] = ["catalog.stage", str(stage)]
        phases["Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Escape.%02d" % stage] = ["catalog.stage", str(stage)]
    for tag, parts in phases.items():
        identity = "ability:" + tag
        if identity in entries:
            entries[identity]["labelParts"] = parts
    for item in entries.values():
        for effect in item.get("effects", []):
            level = next((re.search(r"(?:Lv|V|Level|\.)([123])$", tag) for tag in effect.get("upgradeTags", [])
                          if re.search(r"(?:Lv|V|Level|\.)([123])$", tag)), None)
            rank = int(level[1]) - 1 if level else 0
            for language, label in effect.get("labels", {}).items():
                effect["labels"][language] = choose_variant(label, rank)
            for language, description in effect.get("descriptionLabels", {}).items():
                effect["descriptionLabels"][language] = readable_description(choose_variant(description, rank), language, native)
    family_audit = read_json(ROOT / "docs/complete-move-shadow-audit.json")
    for family in evidence["families"]:
        source = next(row for row in family_audit["families"] if row["id"] == family["family"])
        classes = {row["class"] for row in source["associatedAbilities"]}
        for name in family["names"]:
            mapped = []
            for identity, item in entries.items():
                if not classes.intersection(item.get("evidence", {}).get("abilityClasses", [])):
                    continue
                keys = item.get("evidence", {}).get("nameKeys", []) + [effect["key"] for effect in item.get("effects", [])]
                if name["key"] in keys:
                    mapped.append(identity)
            if mapped:
                name.update({"catalogIds": mapped, "status": "mapped-native-entrance-or-upgrade"})
    (ROOT / "docs/game-move-name-shadow-evidence.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    output = {"schemaVersion": 1, "build": "CL-302256", "entries": dict(sorted(entries.items()))}
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print("game move names: " + str(len(entries)))


if __name__ == "__main__":
    main()
