"""Read native weapon function names and their typed upgrade-effect relation."""
import argparse
import collections
import importlib.util
import json
import pathlib
import re
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("weapon_names_entries", ROOT / "build/extract-weapon-move-entries.py")
entries = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(entries)


def soft_path(reader):
    package, asset, subobject = reader.name(), reader.name(), reader.string()
    if package != "None" and not package.startswith("/"):
        raise ValueError("Expected soft object package")
    return {"package": package, "asset": asset, "subobject": subobject}


def text_reference(reader):
    flags, history = reader.number("<I"), reader.number("<b")
    if history == -1:
        return None
    if history == 0 and reader.number("<B") == 7:
        history = 7
    if history != 7 or flags not in (0, 1):
        raise ValueError("Unsupported native localized table text")
    return {"namespace": reader.string(), "key": reader.string()}


def function_records(package, key_prefix):
    """Decode the typed effect/name prefix of native six-field upgrade records."""
    data, result = package.exports[0][1], []
    pattern = re.escape(key_prefix.encode()) + rb"[A-Za-z0-9_]+\x00"
    for match in re.finditer(pattern, data):
        expected_key = match.group()[:-1].decode()
        candidates = []
        for start in range(max(0, match.start() - 160), match.start() - 20):
            first = struct.unpack_from("<H", data, start)[0]
            if not first >> 9 or first & 127:
                continue
            reader = entries.combat.e.Reader(data, package.names)
            reader.position = start
            try:
                header = reader.header()
                if set(header) != set(range(6)) or header[3]:
                    continue
                icon = None if header[0] else soft_path(reader)
                effect = None if header[1] else soft_path(reader)
                if not header[2] and reader.header():
                    continue
                name = text_reference(reader)
                if not name or name["key"] != expected_key or reader.position != match.end():
                    continue
                expected_descriptions = {expected_key.replace("FunctionName_", suffix) for suffix in ("Function_", "FunctionDes_")}
                needle = b"\x08\x00\x00\x00Default\x00"
                namespace_at = data.find(needle, reader.position, reader.position + 96)
                if namespace_at < 0:
                    continue
                description_reader = entries.combat.e.Reader(data, package.names)
                description_reader.position = namespace_at
                description = {"namespace": description_reader.string(), "key": description_reader.string()}
                if description["key"] not in expected_descriptions:
                    continue
                candidates.append({"offset": start, "name": name, "description": description,
                                   "effect": effect, "icon": icon, "header": header,
                                   "nameEndOffset": reader.position, "descriptionOffset": namespace_at})
            except (ValueError, IndexError, struct.error, UnicodeError):
                continue
        if len(candidates) > 1:
            raise ValueError("Ambiguous native upgrade record: " + expected_key)
        result.extend(candidates)
    return result


def extract(archive, symbols):
    shadow = entries.module("weapon_names_shadow", "extract-shadow-move-entries.py")
    languages = entries.read_json(ROOT / "assets/key-enhancement/action-localization.json")["languages"]
    loc = shadow.read_labels(archive, languages)
    def labels(key):
        return {lang: loc[lang][("Default", key)] for lang in languages if ("Default", key) in loc[lang]}
    table = entries.special.inspect(archive, symbols, "/Game/PBZContent/DataTable/UI/DT_WeaponData")[0]
    records = function_records(table, "Weapon_FunctionName_")
    unique = {}
    for record in records:
        key = record["name"]["key"]
        record["labels"] = labels(key)
        record["descriptionLabels"] = labels(record["description"]["key"])
        effect = record.get("effect") or {}
        path = effect.get("package", "None")
        record["upgradeTags"], record["grantedAbilities"] = [], []
        if path.casefold() in archive.packages:
            package = entries.special.inspect(archive, symbols, path)[0]
            components, unknown = entries.ability_sources.effect_components(package, symbols)
            record["effectComponents"] = components
            record["upgradeTags"] = sorted({tag for c in components for tag in c.get("targetTags", {}).get("addedTags", [])})
            record["grantedAbilities"] = sorted({g["ability"] for c in components for g in c.get("grants", [])})
        if key not in unique or (not unique[key]["upgradeTags"] and record["upgradeTags"]):
            unique[key] = record
    audit = entries.read_json(ROOT / "docs/weapon-move-entries.json")
    weapon_audit = entries.read_json(ROOT / "docs/complete-move-weapon-audit.json")
    identities = {x["equipmentId"]: x for x in weapon_audit["equipment"]}
    row_offsets = sorted({x["rowOffset"] for row in weapon_audit["equipment"] for x in row.get("nameSources", [])
                          if x.get("table") == "/Game/PBZContent/DataTable/UI/DT_WeaponData"})
    catalog = entries.read_json(ROOT / "assets/key-enhancement/weapon-moves.json")["moves"]
    by_id = {x["id"]: x for x in catalog}
    result, proof = {}, []
    cache = {}
    tag_cache = {}
    def inherited_tags(path):
        if path not in tag_cache:
            package, _, parent = entries.special.inspect(archive, symbols, path)
            tag_cache[path] = set(entries.combat.cdo_tag_candidates(package))
            if parent and parent.split(".")[0].casefold() in archive.packages:
                tag_cache[path] |= inherited_tags(parent.split(".")[0])
        return tag_cache[path]
    def refs(path):
        if path not in cache:
            package = entries.special.inspect(archive, symbols, path)[0]
            cache[path] = set(package.names)
        return cache[path]
    for equipment in audit["equipment"]:
        identity = identities[equipment["configId"]]
        source = next((x for x in identity.get("nameSources", []) if x.get("key", "").startswith("Weapon_")), None)
        start = source["rowOffset"] if source else 0
        end = next((offset for offset in row_offsets if offset > start), len(table.exports[0][1]))
        row_skill_keys = sorted({x.decode() for x in re.findall(rb"Weapon_FunctionName_[A-Za-z0-9_]+_Skill(?:_2)?", table.exports[0][1][start:end])}) if source else []
        # Variant weapon rows may reuse another weapon's function keys.
        skill_key = row_skill_keys[0] if len(row_skill_keys) == 1 else None
        skill_present = bool(skill_key)
        if skill_key and labels(skill_key):
            for path in equipment["equipAbilities"] + equipment["owningAbilities"]:
                if not path or path.casefold() not in archive.packages:
                    continue
                tags = inherited_tags(path)
                if any(tag.startswith("Ability.Action.Hun.CombatArt.") for tag in tags):
                    ability_class = path + "." + path.rsplit("/", 1)[-1] + "_C"
                    key = "weapon-art:" + equipment["configId"].rsplit("/", 1)[-1] + "." + path.rsplit("/", 1)[-1]
                    result[key] = {"labels": {lang: labels(source["key"])[lang] + " · " + text for lang, text in labels(skill_key).items()},
                                   "aliases": {lang: [text] for lang, text in labels(skill_key).items()},
                                   "evidence": {"nameKeys": [skill_key], "abilityClasses": [ability_class], "equipmentId": equipment["configId"],
                                                "source": "Native DT_WeaponData row Skill text and exact equipped CombatArt-tagged class"}}
        for move in equipment["entries"]:
            target = by_id.get(move["id"])
            if target is None:
                continue
            path = move["ability"]
            route_tags = set((move.get("nativeInputRoute") or {}).get("requiredTags", []))
            references = set().union(*(refs(owner) for owner in move["ancestry"]))
            matches = [record for record in unique.values() if record["labels"] and
                       (set(record["upgradeTags"]) & (route_tags | references) or path in record["grantedAbilities"])]
            primary = [record for record in matches if set(record["upgradeTags"]) & route_tags or path in record["grantedAbilities"]]
            is_art = move.get("tag", "").startswith("Ability.Action.Hun.CombatArt.")
            name_key = skill_key if is_art and skill_present and labels(skill_key) else primary[0]["name"]["key"] if len(primary) == 1 else None
            if not name_key and not matches:
                continue
            value = {"aliases": {lang: [] for lang in languages}, "effects": [],
                     "evidence": {"nameKeys": [], "abilityClasses": [target["abilityClass"]],
                                  "source": "DT_WeaponData native equipment/function record; exact granted class and input-route upgrade tags"}}
            if name_key:
                display = labels(name_key)
                value["labels"] = {lang: target.get("weaponLabels", {}).get(lang, "") + " · " + text for lang, text in display.items()}
                value["evidence"]["nameKeys"].append(name_key)
                for lang, text in display.items():
                    value["aliases"][lang].append(text)
            for record in matches:
                key = record["name"]["key"]
                value["effects"].append({"key": key, "labels": record["labels"], "kind": "upgrade",
                                         "upgradeTags": record["upgradeTags"], "descriptionLabels": record["descriptionLabels"]})
                value["evidence"]["nameKeys"].append(key)
                for lang, text in record["labels"].items():
                    if text not in value["aliases"][lang]:
                        value["aliases"][lang].append(text)
            value["evidence"]["nameKeys"] = sorted(set(value["evidence"]["nameKeys"]))
            result[move["id"]] = value
            proof.append({"id": move["id"], "abilityClass": target["abilityClass"], "nativeRouteRequiredTags": sorted(route_tags),
                          "primaryKey": name_key, "effectKeys": [x["name"]["key"] for x in matches],
                          "equipmentNameSource": source})
    # Existing action identities get a name only when every concrete named target agrees.
    groups = collections.defaultdict(list)
    for move in catalog:
        if move["id"] in result and result[move["id"]].get("labels"):
            groups[move.get("sourceId")].append(result[move["id"]])
    all_sources = collections.Counter(x.get("sourceId") for x in catalog)
    for source, values in groups.items():
        if source and len(values) == all_sources[source] and all(x["labels"] == values[0]["labels"] for x in values):
            result[source] = dict(values[0])
            result[source]["evidence"] = dict(values[0]["evidence"])
            result[source]["evidence"]["abilityClasses"] = sorted({p for x in values for p in x["evidence"]["abilityClasses"]})
    return {"schemaVersion": 1, "build": audit["build"], "entries": result}, {
        "build": audit["build"], "runtimeVerified": False, "records": list(unique.values()), "associations": proof,
        "classification": "An exact native input-route unlock or direct grant names the move; other referenced upgrades are effects."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", required=True, type=pathlib.Path)
    parser.add_argument("--paks", required=True, type=pathlib.Path)
    parser.add_argument("--oodle", required=True, type=pathlib.Path)
    args = parser.parse_args()
    archive = entries.combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        symbols = entries.combat.ScriptObjects(args.paks)
        catalog, evidence = extract(archive, symbols)
        for path, document in (("docs/game-move-names-weapons.json", catalog), ("docs/game-move-names-weapons-evidence.json", evidence)):
            (ROOT / path).write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
        print("Official weapon-name associations:", len(catalog["entries"]))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
