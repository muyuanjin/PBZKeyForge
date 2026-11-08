"""List equipped weapon attacks by their exact granted class.

Reads the registry-backed equipment universe, actual Equip/Owning arrays and
inherited ability resources. The game is neither started nor modified.
"""
import argparse
import collections
import importlib.util
import json
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "build" / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


combat = module("weapon_combat", "extract-combat-art-phases.py")
special = module("weapon_special", "extract-weapon-special-entries.py")
localization = module("weapon_localization", "extract-action-localization.py")
ability_sources = module("weapon_sources", "extract-ability-sources.py")
sys.path.insert(0, str(ROOT / "prototype/input-router"))
import special_dash


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def input_binding_maps(package, inspect):
    """Read complete typed InputAction/Binding maps from weapon config data."""
    data = package.exports[0][1]
    import_count = (struct.unpack_from("<I", package.data, 32)[0] - package.import_offset) // 8
    matches = []
    for offset in range(len(data) - 8):
        removed, count = struct.unpack_from("<ii", data, offset)
        if removed or not 1 <= count <= 32 or offset + 8 + count * 8 > len(data):
            continue
        pairs = []
        for index in range(count):
            left, right = struct.unpack_from("<ii", data, offset + 8 + index * 8)
            if not (-import_count <= left < 0 and -import_count <= right < 0):
                break
            action, binding = package.object_name(left), package.object_name(right)
            if not (action.startswith("/") and binding.startswith("/")):
                break
            _, action_exports, _ = inspect(action)
            _, binding_exports, _ = inspect(binding)
            if not any(row["class"].endswith(".InputAction") for row in action_exports):
                break
            if not any(row["class"].endswith(".InputActionBinding") for row in binding_exports):
                break
            pairs.append((action, binding))
        if len(pairs) == count:
            matches.append({"offset": offset, "endOffset": offset + 8 + count * 8, "pairs": pairs})
    return [row for row in matches if not any(other["offset"] < row["offset"] and other["endOffset"] >= row["endOffset"] for other in matches)]


def binding_routes(pairs, inspect):
    routes = collections.defaultdict(list)
    for action, binding in pairs:
        package, _, _ = inspect(binding)
        document = special_dash.BindingDocument(package.exports[0][1], package.names)
        for item in document.items:
            blocks = [(None, item.get("NormalData"))] + [(pair["tag"], pair["value"]) for pair in (item.get("TagConditionData") or {}).get("pairs", [])]
            for condition, block in blocks:
                if not block:
                    continue
                for kind in ("Actions", "Combos", "InterruptLower"):
                    for trigger in block.get(kind) or []:
                        tag = trigger.get("TriggerTag")
                        if tag:
                            value = {"inputAction": action + "." + action.rsplit("/", 1)[-1], "binding": binding,
                                     "priority": item.get("ActionPriority") or 0,
                                     "requiredTags": list(dict.fromkeys((trigger.get("RequiredTags") or []) + ([condition] if condition else []))),
                                     "ignoredTags": trigger.get("IgnoredTags") or []}
                            if value not in routes[tag]:
                                routes[tag].append(value)
    return routes


def extract(archive, audit, details):
    symbols = combat.ScriptObjects(archive.paks)
    cache = {}

    def inspect(path):
        path = path.split(".")[0]
        if path not in cache:
            package, exports, parent = special.inspect(archive, symbols, path)
            cache[path] = (package, exports, parent)
        return cache[path]

    def ancestry(path):
        result = []
        while path and path.casefold() in archive.packages:
            package, exports, parent = inspect(path)
            result.append((path, package, exports))
            path = parent.split(".")[0] if parent else None
        return result

    existing = {"ability:" + row["tag"]: row for row in read_json(ROOT / "prototype/input-router/actions.json")["abilities"]}
    languages = read_json(ROOT / "assets/key-enhancement/action-localization.json")["languages"]
    # Native input identities are taken from the already audited move catalog.
    known_inputs = {}
    for move in read_json(ROOT / "assets/key-enhancement/complete-moves.json")["moves"]:
        if move.get("tag") and move.get("inputAction"):
            known_inputs.setdefault(move["tag"], move["inputAction"])
    base_inputs = {
        "Light": "/PBZInput/InputActions/IA_AttackL_Press.IA_AttackL_Press",
        "Heavy": "/PBZInput/InputActions/IA_AttackH_Press.IA_AttackH_Press",
        "CombatArt": "/PBZInput/InputActions/IA_CombatArt.IA_CombatArt",
    }
    protected = {}
    for row in read_json(ROOT / "docs/complete-move-weapon-special-entries.json")["recipes"]:
        if row.get("activationKind") == "native-early-release":
            for target in row["nativeSelection"]["targets"]:
                protected[target] = row["recipe"]["id"]
    for move in read_json(ROOT / "assets/key-enhancement/complete-moves.json")["moves"]:
        if move.get("prepareInput"):
            protected[move["abilityClass"].split(".")[0]] = move["id"]
    for path in base_inputs.values():
        if path.split(".")[0].casefold() not in archive.packages:
            raise ValueError("Missing native input resource: " + path)
    native_config, _, _ = inspect("/PBZCombatAsset/ActorData/Players/Hun/InputConfig_Hun")
    reader = combat.e.Reader(native_config.exports[0][1], native_config.names)
    if reader.header() != {0: False, 1: False, 2: False}:
        raise ValueError("Unsupported InputConfig_Hun fields")
    for _ in range(reader.number()):
        reader.number()
    if reader.number() != 0:
        raise ValueError("Unexpected removed input binding keys")
    default_pairs = [(native_config.object_name(reader.number()), native_config.object_name(reader.number())) for _ in range(reader.number())]
    default_pairs = [(action, binding) for action, binding in default_pairs if binding]
    pak = localization.Pak(archive, combat.e.Reader)
    try:
        resources = {}
        for path, index in pak.files.items():
            if "/ProjectPBZ/Content/Localization/Game/" in path and path.endswith("/Game.locres"):
                resources[path.split("/")[-2]] = localization.parse_locres(pak.read(index), combat.e.Reader)
        equipment, moves = [], []
        for config in audit["equipment"]:
            path = config["equipmentId"]
            package, _, _ = inspect(path)
            fields = combat.equipment_fields(package)
            record = dict(configId=path, **fields, entries=[], excluded=[])
            maps = input_binding_maps(package, inspect)
            input_pairs = [pair for binding_map in maps for pair in binding_map["pairs"]] + default_pairs
            routes = binding_routes(dict.fromkeys(input_pairs), inspect)
            record["inputBindingMaps"] = maps
            source = next((row for row in config.get("nameSources", []) if row.get("key")), None)
            key = (source["namespace"], source["key"]) if source else None
            fallback = resources.get("en-US", {}).get(key, source.get("text", path.rsplit("/", 1)[-1]) if source else path.rsplit("/", 1)[-1])
            labels = {lang: resources["en-US" if lang == "en" else lang].get(key, fallback)
                      for lang in languages}
            record["missingNameLanguages"] = [lang for lang in languages if key not in resources["en-US" if lang == "en" else lang]]
            category_id = source["key"] if source else path.rsplit("/", 1)[-1]
            record["nameSource"] = source
            record["weaponLabels"] = labels
            tags = [tag for tag in fields["equipTags"] if tag.startswith("Weapon.Type.") and not tag.startswith("Weapon.Type.Global.")]
            for ability in dict.fromkeys(fields["equipAbilities"] + fields["owningAbilities"]):
                if ability is None:
                    continue
                ability = ability.split(".")[0]
                if ability.casefold() not in archive.packages:
                    record["excluded"].append({"ability": ability, "reason": "native ability without a decoded Blueprint activation contract"})
                    continue
                chain = ancestry(ability)
                all_tags = next((combat.cdo_tag_candidates(pkg) for _, pkg, _ in chain if combat.cdo_tag_candidates(pkg)), [])
                attack_tags = [tag for tag in all_tags if tag.startswith(tuple("Ability.Action.Hun." + part + "." for part in (*base_inputs, "Special")))]
                execution = any("Execution" in owner or "Executed" in owner for owner, _, _ in chain)
                audit_ability = audit["abilities"].get(ability, {})
                if ability in protected:
                    record["excluded"].append({"ability": ability, "reason": "native preparation or paid source required", "coveredBy": protected[ability]})
                    continue
                if execution or audit_ability.get("role") in ("passive", "executed-target", "execution"):
                    record["excluded"].append({"ability": ability, "reason": "execution lifecycle or passive ability", "ancestry": [owner for owner, _, _ in chain]})
                    continue
                if any(tag.startswith("Ability.Action.Hun.Weapon.Switch.") for tag in all_tags):
                    record["excluded"].append({"ability": ability, "reason": "native weapon-switch wrapper", "tagCandidates": all_tags})
                    continue
                if not attack_tags:
                    record["excluded"].append({"ability": ability, "reason": "no light, heavy or combat-art activation tag", "tagCandidates": all_tags,
                                               "ancestry": [owner for owner, _, _ in chain]})
                    continue
                tag = next((candidate for candidate in attack_tags if routes.get(candidate)), attack_tags[0])
                kind = tag.split(".")[3]
                source_id = "ability:" + tag
                detail = details["entries"].get(source_id, {})
                parts = [part[1:] if part.startswith("@") else part for part in detail.get("parts", [])]
                category = {"Light": "IA_LightAttack", "Heavy": "IA_HeavyAttack", "CombatArt": "IA_UltimateSkill", "Special": "special"}[kind]
                if not parts:
                    parts = [{"Light": "act.IA_LightAttack", "Heavy": "act.IA_HeavyAttack", "CombatArt": "moves.combatart", "Special": "catalog.weaponbuff"}[kind], tag.split(".")[-1]]
                elif not any(part.startswith(("act.", "moves.combatart")) for part in parts):
                    parts.insert(0, {"Light": "act.IA_LightAttack", "Heavy": "act.IA_HeavyAttack", "CombatArt": "moves.combatart", "Special": "catalog.weaponbuff"}[kind])
                leaf = ability.rsplit("/", 1)[-1]
                route = next(iter(routes.get(tag, [])), None)
                input_action = route["inputAction"] if route else known_inputs.get(tag)
                if not input_action:
                    record["excluded"].append({"ability": ability, "reason": "no independently established input route", "tagCandidates": all_tags,
                                               "ancestry": [owner for owner, _, _ in chain]})
                    continue
                move = {"id": "move:Weapon." + path.rsplit("/", 1)[-1] + "." + leaf,
                        "sourceId": source_id if source_id in existing else "action:" + input_action.split(".")[0],
                        "tag": tag, "abilityClass": ability + "." + leaf + "_C", "activateByClass": True, "nativeEntry": True,
                        "inputAction": input_action, "requiredTags": tags, "priority": route["priority"] if route else -5,
                        "code": leaf.removeprefix("GA_Hun_"), "labelParts": parts,
                        "weaponLabels": labels, "category": "weapons/" + category_id + "/" + category,
                        "usageKey": "moves.nativeentrance.usage",
                        "searchAliases": list(dict.fromkeys([leaf, ability, tag] + list(labels.values()) + detail.get("aliases", [])))}
                if route:
                    move["activationRequiredTags"] = [value for value in route["requiredTags"] if not value.startswith("Ability.Action.")]
                    move["ignoredTags"] = route["ignoredTags"]
                record["entries"].append({"id": move["id"], "ability": ability, "tag": tag, "tagCandidates": all_tags,
                                           "nativeInputRoute": route,
                                           "tagEvidenceOwner": next(owner for owner, pkg, _ in chain if combat.cdo_tag_candidates(pkg)),
                                           "ancestry": [owner for owner, _, _ in chain]})
                moves.append(move)
            equipment.append(record)
        canonical, aliases = {}, {}
        for move in moves:
            signature = (move["abilityClass"], move["inputAction"], tuple(sorted(move["requiredTags"])),
                         tuple(sorted(move.get("activationRequiredTags", []))), tuple(sorted(move.get("ignoredTags", []))),
                         move["priority"], tuple(move["weaponLabels"].values()))
            if signature in canonical:
                aliases[move["id"]] = canonical[signature]["id"]
            else:
                canonical[signature] = move
        moves = list(canonical.values())
        duplicate_labels = collections.Counter((move["id"].split(".")[1], move["category"], tuple(move["weaponLabels"].values()), tuple(move["labelParts"])) for move in moves)
        for move in moves:
            if duplicate_labels[(move["id"].split(".")[1], move["category"], tuple(move["weaponLabels"].values()), tuple(move["labelParts"]))] > 1:
                move["labelParts"].append(move["code"])
        for record in equipment:
            for entry in record["entries"]:
                entry["id"] = aliases.get(entry["id"], entry["id"])
        effect_entries = []
        effect_audit = read_json(ROOT / "docs/ability-source-audit.json")
        for grant in effect_audit["actualAbilityGrants"]:
            candidate_tags = grant.get("abilityTagCandidates") or []
            target_tag = next((tag for tag in candidate_tags if tag in (
                "Ability.Action.Hun.Special.Double.YingSha.CB", "Ability.Action.Hun.Special.Tang.ShunSha.CB")), None)
            if not target_tag:
                continue
            effect = grant["effect"]
            components, unknowns = ability_sources.effect_components(inspect(effect)[0], symbols)
            if unknowns:
                raise ValueError("Unresolved effect grant component: " + effect)
            ability = grant["grant"]["ability"].split(".")[0]
            actual_grant = next(row for component in components for row in component.get("grants", [])
                                if (row.get("ability") or "").split(".")[0] == ability)
            route = binding_routes(default_pairs, inspect)[target_tag][0]
            leaf = ability.rsplit("/", 1)[-1]
            source_id = "ability:" + target_tag
            detail = details["entries"][source_id]
            move = {"id": "move:Upgrade." + effect.rsplit("/", 1)[-1] + "." + leaf,
                    "sourceId": source_id, "tag": target_tag, "abilityClass": ability + "." + leaf + "_C",
                    "activateByClass": True, "nativeEntry": True, "inputAction": route["inputAction"],
                    "requiredTags": route["requiredTags"], "ignoredTags": route["ignoredTags"], "priority": route["priority"],
                    "code": leaf.removeprefix("GA_GlobalActionAbility_"),
                    "labelParts": [part.removeprefix("@") for part in detail["parts"]], "category": "weapons/special",
                    "usageKey": "moves.nativeentrance.usage", "searchAliases": [leaf, ability, effect] + detail["aliases"]}
            moves.append(move)
            effect_entries.append({"effect": effect, "grant": actual_grant, "id": move["id"], "ability": ability,
                                   "components": components, "ancestry": [owner for owner, _, _ in ancestry(ability)],
                                   "nativeInputRoute": route})
        equipment_ids = {config["configId"] for config in equipment}
        effect_exclusions = {}
        for grant in effect_audit["actualAbilityGrants"]:
            if not equipment_ids.intersection(grant.get("sourceChain") or []) or grant.get("abilityTagCandidates"):
                continue
            ability = (grant.get("grant") or {}).get("ability")
            if not ability:
                continue
            ability = ability.split(".")[0]
            if ability not in effect_exclusions:
                resources = special.read_resources(archive, symbols, [ability])[ability]
                functions = resources["functions"]
                event_activated = any(row["name"] == "K2_ActivateAbilityFromEvent" for row in functions)
                effect_exclusions[ability] = {"ability": ability, "effects": [], "parent": resources["parent"],
                    "reason": "requires native damage event and target payload" if event_activated else "passive gameplay-event listener",
                    "functions": functions}
            effect_exclusions[ability]["effects"].append(grant["effect"])
        costs = read_json(ROOT / "assets/key-enhancement/resource-costs.json")
        check_paths = {move["abilityClass"].split(".")[0] for move in moves
                       if move["abilityClass"] not in costs["classes"] and ".Light." not in move["tag"]}
        check_paths.update(row["ability"] for config in equipment for row in config["excluded"]
                           if row["reason"] == "no independently established input route")
        raw_checks = special.read_resources(archive, symbols, check_paths)
        resource_checks = []
        for ability in sorted(check_paths):
            item = raw_checks[ability]
            checks = []
            for function in item["functions"]:
                calls = [call for call in function["calls"] if any(word in call for word in ("Activate", "Cost", "Shachi", "Resource", "GameplayEvent"))]
                tag_refs = [row for row in function["tags"] if row["tag"].startswith("Ability.Action.")]
                if calls or tag_refs:
                    checks.append({"name": function["name"], "calls": calls, "tagReferences": tag_refs})
            resource_checks.append({"ability": ability, "ancestry": [owner for owner, _, _ in ancestry(ability)],
                                    "parent": item["parent"], "localFunctions": checks})
        return {"build": audit["build"], "runtimeVerified": False,
                "evidenceType": "Static actual equipment grants, inherited CDO tag candidates, typed native input bindings and weapon localization",
                "equipment": equipment, "effectEntries": effect_entries, "effectExclusions": list(effect_exclusions.values()),
                "moves": moves, "resourceCostChecks": resource_checks,
                "coverage": {"equipment": len(equipment), "entries": len(moves), "equipmentEntries": sum(len(row["entries"]) for row in equipment), "exactClasses": len({move["abilityClass"] for move in moves}),
                             "excluded": sum(len(row["excluded"]) for row in equipment)},
                "limitations": ["Entries retain normal native activation and are not proof that each internal attack phase is independently selectable.",
                                "Execution, passive, native-only and tag-unresolved grants remain explicitly classified; they are not activated as ordinary attacks.",
                                "No game execution or visual combat verification."]}
    finally:
        pak.stream.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", required=True, type=pathlib.Path)
    parser.add_argument("--paks", required=True, type=pathlib.Path)
    parser.add_argument("--oodle", required=True, type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "docs/weapon-move-entries.json")
    parser.add_argument("--catalog-output", type=pathlib.Path, default=ROOT / "assets/key-enhancement/weapon-moves.json")
    args = parser.parse_args()
    audit = read_json(ROOT / "docs/complete-move-weapon-audit.json")
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, audit["build"])
    try:
        document = extract(archive, audit, read_json(ROOT / "assets/key-enhancement/action-details.json"))
    finally:
        archive.close()
    args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.catalog_output.write_text(json.dumps({"build": document["build"], "moves": document["moves"]}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(document["coverage"]))


if __name__ == "__main__":
    main()
