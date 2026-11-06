"""Inventory cooked ability-source references from a complete AssetRegistry export.

All DataTable classes are enumerated, including tables whose rows have not been
decoded. Player classes, equipment, usable-item configs and inventory assets are
also roots. Hard references and serialized soft paths are followed through
abilities, effects, action helpers and attack resources. References are discovery
evidence; this command does not label every referenced ability as granted or
every attack resource as an independently playable move.
"""
import argparse
import collections
import importlib.util
import json
import pathlib
import struct


SPEC = importlib.util.spec_from_file_location("combat_art", pathlib.Path(__file__).with_name("extract-combat-art-phases.py"))
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)
e = combat_art.e

PROVIDER_CLASSES = {
    "DataTable", "InventoryData", "PBZ_UseableItemConfig_C", "PDA_ActorConfig_C",
    "PBZ_MainWeaponConfig_C", "PBZ_SubWeaponConfig_C", "PBZ_TempWeaponConfig_C",
    "PDA_GlobalCombatConfig_C", "PDA_GlobalMechanicConfig_C", "PDA_GlobalConfig_Combined_C",
}
PLAYER_ROWS = {
    "/Script/ProjectPBZ.AccessoryData", "/Script/ProjectPBZ.AbilityNodeStruct",
    "/Script/ProjectPBZ.SkillDataStruct", "/Script/ProjectPBZ.SkillData",
    "/Script/ProjectPBZ.UsablePropsItem", "/Script/ProjectPBZ.PBZItemDataStruct",
    "/Script/ProjectPBZ.InitItemStruct", "/Game/PBZContent/Blueprint/Struct/DS_Player.DS_Player",
    "/Script/ProjectPBZ.WeaponData", "/Script/ProjectPBZ.ShaqiWeaponData",
    "/Script/ProjectPBZ.TempWeaponRow", "/Script/ProjectPBZ.InitWeaponData",
    "/Game/PBZContent/Blueprint/Struct/DS_Weapon.DS_Weapon",
}


def registry_index(assets):
    result = {}
    for asset in assets:
        key = asset["package"].casefold()
        record = result.setdefault(key, {"package": asset["package"], "classes": [], "tags": {}})
        if asset["assetClass"] not in record["classes"]:
            record["classes"].append(asset["assetClass"])
        record["tags"].update(asset.get("tags", {}))
    return result


def kind(record):
    classes, parent = record["classes"], record["tags"].get("NativeParentClass", "")
    if any(cls in classes for cls in ("LegoAbilityBlueprintGeneratedClass", "GameplayAbilityBlueprintGeneratedClass")) or any(text in parent for text in ("GameplayAbility'", "SGameGameplayAbility'", "SGameLegoAbility'", "/Script/PBZCombatAsset.GA_")):
        return "ability"
    if "GameplayEffect" in classes or "GameplayEffect'" in parent or ".GE_" in parent:
        return "effect"
    if "PBZCharacter_Player'" in parent or "PBZPlayerController'" in parent:
        return "player"
    if "PBZCharacter_NPC'" in parent:
        return "npc"
    if any(cls in PROVIDER_CLASSES for cls in classes):
        return "provider"
    if "AbilityNodeHelpObj'" in parent or "LGAAction" in parent:
        return "helper"
    if any("BulletData" in cls for cls in classes) or "BulletActor" in parent or "BulletBase" in parent:
        return "attack-resource"
    return "other"


def occurrences(data, needle):
    offset = data.find(needle)
    while offset >= 0:
        yield offset
        offset = data.find(needle, offset + 1)


def references(package, index):
    result = {}
    def relevant(target):
        return target in index and (kind(index[target]) != "other" or "AnimMontage" in index[target]["classes"])
    for name_index, name in enumerate(package.names):
        if not name.startswith("/"):
            continue
        target = name.partition(".")[0].casefold()
        if not relevant(target):
            continue
        uses = []
        for export_name, data in package.exports:
            for offset in occurrences(data, struct.pack("<II", name_index, 0)):
                typed = False
                if offset + 20 <= len(data):
                    asset_index, number, subpath_length = struct.unpack_from("<IIi", data, offset + 8)
                    if asset_index < len(package.names) and number == 0 and subpath_length == 0:
                        asset_name = package.names[asset_index]
                        typed = asset_name in (name.rsplit("/", 1)[-1], name.rsplit("/", 1)[-1] + "_C")
                uses.append({"export": export_name, "offset": offset,
                             "kind": "soft-object-path" if typed else "package-name-reference-candidate"})
        if uses:
            result.setdefault(target, []).extend(uses)
    import_end = struct.unpack_from("<I", package.data, 32)[0]
    for slot in range((import_end - package.import_offset) // 8):
        identity = struct.unpack_from("<Q", package.data, package.import_offset + slot * 8)[0]
        if identity >> 62 != 2:
            continue
        target = package.imports[(identity >> 32) & 0x3fffffff].casefold()
        if not relevant(target):
            continue
        uses = [{"export": export_name, "offset": offset, "kind": "hard-object-reference-candidate"}
                for export_name, data in package.exports
                for offset in occurrences(data, struct.pack("<i", -slot - 1))]
        if uses:
            result.setdefault(target, []).extend(uses)
    return [{"package": index[target]["package"], "assetKind": kind(index[target]),
             "referenceKinds": sorted({use["kind"] for use in uses}), "referenceExamples": uses[:4]}
            for target, uses in sorted(result.items())]


def scalable_float(reader):
    result = {}
    for field, zero in reader.header().items():
        if zero:
            continue
        if field == 0:
            result["value"] = reader.number("<f")
        elif field == 1:
            for curve_field, curve_zero in reader.header().items():
                if curve_zero:
                    continue
                if curve_field == 0:
                    result["curveTablePointer"] = reader.number()
                elif curve_field == 1:
                    result["curveRow"] = reader.name()
                else:
                    raise ValueError("Unsupported scalable-float curve field")
        elif field == 2:
            for registry_field, registry_zero in reader.header().items():
                if not registry_zero:
                    if registry_field != 0:
                        raise ValueError("Unsupported data-registry type field")
                    result["registryType"] = reader.name()
        else:
            raise ValueError("Unsupported scalable-float field")
    return result


def effect_components(package, symbols):
    result, unknowns = [], []
    start = struct.unpack_from("<I", package.data, 32)[0]
    for export_index, (name, data) in enumerate(package.exports):
        cls = struct.unpack_from("<Q", package.data, start + export_index * 72 + 32)[0]
        class_name = symbols.resolve(package, cls)
        if class_name not in ("/Script/GameplayAbilities.TargetTagsGameplayEffectComponent",
                              "/Script/GameplayAbilities.AbilitiesGameplayEffectComponent",
                              "/Script/SGameCombatSystem.GrantAndActivateAbilities"):
            continue
        try:
            reader = e.Reader(data, package.names)
            header = reader.header()
            component = {"export": name, "class": class_name}
            if header in ({}, {0: True}):
                component["inheritedProperties"] = True
                result.append(component)
                continue
            if header != {0: False}:
                raise ValueError("Unsupported component property layout")
            if class_name.endswith("TargetTagsGameplayEffectComponent"):
                tags = {}
                for field, zero in reader.header().items():
                    if field not in (0, 1, 2):
                        raise ValueError("Unsupported inherited-tag-container field")
                    tags[{0: "combinedTags", 1: "addedTags", 2: "removedTags"}[field]] = [] if zero else [reader.name() for _ in range(reader.number())]
                component["targetTags"] = tags
            else:
                grants = []
                for _ in range(reader.number()):
                    grant = {}
                    for field, zero in reader.header().items():
                        if zero:
                            continue
                        if field == 0:
                            pointer = reader.number()
                            grant["ability"] = package.object_name(pointer)
                            if pointer < 0:
                                identity = struct.unpack_from("<Q", package.data, package.import_offset + (-pointer - 1) * 8)[0]
                                grant["ability"] = symbols.resolve(package, identity)
                        elif field == 1:
                            grant["level"] = scalable_float(reader)
                        elif field == 2:
                            grant["inputId"] = reader.number()
                        elif field == 3:
                            grant["removalPolicy"] = reader.number("<B")
                        else:
                            raise ValueError("Unsupported ability-grant field " + str(field))
                    grants.append(grant)
                component["grants"] = grants
                component["activation"] = "grant-and-activate" if class_name.endswith(".GrantAndActivateAbilities") else "grant"
            result.append(component)
        except (ValueError, IndexError, struct.error) as error:
            unknowns.append({"export": name, "class": class_name, "reason": str(error)})
    return result, unknowns


def extract(archive, assets):
    index = registry_index(assets)
    roots = {path for path, row in index.items() if kind(row) in ("provider", "player", "npc")}
    symbols = combat_art.ScriptObjects(archive.paks)
    queue, seen, records, missing = collections.deque(sorted(roots)), set(), [], []
    while queue:
        path = queue.popleft()
        if path in seen:
            continue
        seen.add(path)
        if path not in archive.packages:
            missing.append(index[path]["package"])
            continue
        row = index[path]
        package = e.Package(archive.resource(archive.packages[path]))
        refs = references(package, index)
        record = {"package": row["package"], "assetKind": kind(row), "registryClasses": row["classes"],
                  "nativeParentClass": row["tags"].get("NativeParentClass"),
                  "rowStructure": row["tags"].get("RowStructure"),
                  "isRoot": path in roots,
                  "playerRoot": kind(row) == "player" or row["tags"].get("RowStructure") in PLAYER_ROWS,
                  "npcRoot": kind(row) == "npc",
                  "tagNames": [name for name in package.names if name.startswith(("Ability.", "CharacterState.", "GameplayEvent.", "Weapon."))],
                  "references": refs}
        if kind(row) == "effect":
            record["components"], record["componentUnknowns"] = effect_components(package, symbols)
        if kind(row) == "ability":
            record["abilityTagCandidates"] = combat_art.cdo_tag_candidates(package)
            record["eventNames"] = [name for name in package.names if name.startswith("GameplayEvent.")]
            record["stateNames"] = [name for name in package.names if name.startswith(("CharacterState.", "Ability.Passive.", "Ability.Special."))]
            record["timelines"], record["timelineUnknowns"] = [], []
            start = struct.unpack_from("<I", package.data, 32)[0]
            for export_index, (export_name, _) in enumerate(package.exports):
                cls = struct.unpack_from("<Q", package.data, start + export_index * 72 + 32)[0]
                if symbols.resolve(package, cls) != "/Script/LegoAbility.LGATimeline":
                    continue
                try:
                    timeline = combat_art.timeline(package, export_index)
                    record["timelines"].append({"name": export_name, "actionCount": len(timeline["actions"])})
                except ValueError as error:
                    record["timelineUnknowns"].append({"name": export_name, "reason": str(error)})
        records.append(record)
        for reference in refs:
            target = reference["package"].casefold()
            if target != path and kind(index[target]) in ("ability", "effect", "helper", "provider", "player", "npc", "attack-resource"):
                queue.append(target)
        # Cooked blocks are reusable within a batch; the inventory keeps only
        # metadata after each package has been inspected.
        if len(records) % 200 == 0:
            archive.blocks.clear()
    by_path = {row["package"].casefold(): row for row in records}
    player_reached, frontier = set(), collections.deque(row["package"].casefold() for row in records if row["playerRoot"])
    while frontier:
        path = frontier.popleft()
        if path in player_reached:
            continue
        player_reached.add(path)
        if path in by_path:
            frontier.extend(ref["package"].casefold() for ref in by_path[path]["references"]
                            if ref["package"].casefold() in by_path)
    for row in records:
        row["referencedFromPlayerRoot"] = row["package"].casefold() in player_reached
    npc_reached, frontier = set(), collections.deque(row["package"].casefold() for row in records if row["npcRoot"])
    while frontier:
        path = frontier.popleft()
        if path in npc_reached:
            continue
        npc_reached.add(path)
        if path in by_path:
            frontier.extend(ref["package"].casefold() for ref in by_path[path]["references"]
                            if ref["package"].casefold() in by_path)
    universe = []
    for path, row in sorted(index.items()):
        if kind(row) != "ability":
            continue
        player, npc = path in player_reached, path in npc_reached
        classification = "player-and-npc-referenced" if player and npc else "player-referenced" if player else "npc-referenced" if npc else "provider-only" if path in by_path else "source-unassigned"
        universe.append({"package": row["package"], "nativeParentClass": row["tags"].get("NativeParentClass"),
                         "classification": classification, "inspected": path in by_path})
    for row in records:
        row["referencedFromNpcRoot"] = row["package"].casefold() in npc_reached
    return {"registryAssets": len(assets), "registryAbilityClasses": sum(kind(row) == "ability" for row in index.values()),
            "dataTablesEnumerated": sum("DataTable" in row["classes"] for row in index.values()),
            "rootPackages": len(roots), "inspectedPackages": len(records), "missingPackages": missing,
            "abilityUniverse": universe, "records": records}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle", "registry", "output"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    parser.add_argument("--build", default="CL-302256")
    args = parser.parse_args()
    registry = json.loads(args.registry.read_text(encoding="utf-8"))
    archive = e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        result = extract(archive, registry["assets"])
    finally:
        archive.close()
    result.update({"build": args.build, "runtimeVerified": False,
                   "evidenceType": "AssetRegistry class/row-structure enumeration and cooked hard/soft resource-reference candidates",
                   "limitations": ["References do not establish whether an ability is granted, automatically triggered, or independently activatable.",
                                   "Player-root reachability is a discovery boundary, not proof that every branch belongs to a playable character.",
                                   "DataTable row/property semantics and native passive callbacks require individual decoding."]})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Read %d roots and %d referenced packages across %d registry DataTables; registry ability universe=%d" %
          (result["rootPackages"], result["inspectedPackages"], result["dataTablesEnumerated"], result["registryAbilityClasses"]))


if __name__ == "__main__":
    main()
