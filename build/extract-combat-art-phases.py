"""Extract equipped combat-art timelines and every action from cooked resources.

The equipment universe comes from an AssetRegistry JSON export, or the checked-in
registry-backed weapon audit for its recorded build. Actual Equip/Owning arrays,
ability ancestry, timelines and actions are read again from the installed game.
Archive keys stay in memory. This command neither runs nor modifies the game.
"""
import argparse
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("input_defaults", pathlib.Path(__file__).with_name("extract-input-defaults.py"))
e = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(e)


class ScriptObjects:
    def __init__(self, paks):
        toc = (paks / "global.utoc").read_bytes()
        size = int.from_bytes(toc[161:166], "big")
        data = (paks / "global.ucas").read_bytes()[:size]
        self.names = e.name_batch(data, 0)
        count = struct.unpack_from("<I", data)[0]
        lengths_at = 16 + count * 8
        lengths = [int.from_bytes(data[lengths_at + i * 2:lengths_at + i * 2 + 2], "big") for i in range(count)]
        end = lengths_at + count * 2 + sum((length & 0x7fff) * (2 if length & 0x8000 else 1) for length in lengths)
        entries = struct.unpack_from("<I", data, end)[0]
        if end + 4 + entries * 32 != len(data):
            raise ValueError("Unsupported global script-object layout")
        self.objects = {}
        for i in range(entries):
            index, number, identity, outer, _ = struct.unpack_from("<IIQQQ", data, end + 4 + i * 32)
            name = self.names[index & 0x3fffffff] + ("_" + str(number - 1) if number else "")
            self.objects[identity] = (name, outer)

    def name(self, identity):
        name, outer = self.objects[identity]
        return self.name(outer) + "." + name if outer in self.objects else name

    def resolve(self, package, identity):
        if identity in self.objects:
            return self.name(identity)
        if identity >> 62 == 2:
            return package.imports[(identity >> 32) & 0x3fffffff]
        return "Local:" + str(identity)


def action_section(reader):
    result = {}
    for field, zero in reader.header().items():
        if zero:
            result[field] = 0
        elif field == 0:
            result[field] = reader.number("<f")
        elif field == 1:
            result[field] = reader.read(4).hex()
        else:
            raise ValueError("Unsupported action-section field " + str(field))
    return result


def timeline(package, export_index):
    name, payload = package.exports[export_index]
    reader = e.Reader(payload, package.names)
    header = reader.header()
    if 0 in header and not header[0]:
        raise ValueError("Unsupported explicit timeline tags in " + name)
    if 1 in header and not header[1] and next(field for field in header if not header[field]) != 1:
        raise ValueError("Expected timeline Actions as its first nonzero serialized field: " + name + " " + str(header))
    actions = []
    for index in range(reader.number() if 1 in header and not header[1] else 0):
        offset, values = reader.position, {}
        for field, zero in reader.header().items():
            if zero:
                values[field] = 0
            elif field <= 3:
                values[field] = reader.number("<f")
            elif field in (4, 6):
                values[field] = reader.number()
            elif field == 5:
                values[field] = reader.read(16).hex()
            elif field == 7:
                values[field] = [action_section(reader) for _ in range(reader.number())]
            else:
                raise ValueError("Unsupported timeline action-event field " + str(field))
        actions.append({"index": index, "exportByteOffset": offset,
                        "startTime": values.get(0, 0), "duration": values.get(1, 0),
                        "triggerTimeOffset": values.get(2, 0), "endTriggerTimeOffset": values.get(3, 0),
                        "actionPointer": values.get(4, 0), "action": package.object_name(values.get(4, 0)),
                        "guid": values.get(5), "trackIndex": values.get(6, 0), "sections": values.get(7, [])})
    return {"name": name, "exportIndex": export_index, "actions": actions}


def equipment_fields(package):
    reader = e.Reader(package.exports[0][1], package.names)
    header, result = reader.header(), {}
    for field in sorted(header):
        if field > 10:
            break
        if header[field]:
            value = None
        elif field in (0, 1):
            value = [reader.name() for _ in range(reader.number())]
        elif field == 4:
            value = reader.number("<d")
        elif field == 10:
            value = package.object_name(reader.number())
        else:
            value = [package.object_name(reader.number()) for _ in range(reader.number())]
        result[field] = value
    return {"equipTags": result.get(0) or [], "equipAbilities": result.get(5) or [],
            "owningAbilities": result.get(6) or []}


def object_references(package, payload):
    start = struct.unpack_from("<I", package.data, 32)[0]
    result = []
    for offset in range(len(payload) - 3):
        pointer = struct.unpack_from("<i", payload, offset)[0]
        if pointer >= 0 or -pointer * 8 > start - package.import_offset:
            continue
        reference = package.object_name(pointer)
        if reference and reference.startswith("/"):
            result.append({"offset": offset, "path": reference})
    return result


def cdo_tag_candidates(package):
    wanted = {i: name for i, name in enumerate(package.names) if name.startswith("Ability.Action.")}
    result = []
    for name, payload in package.exports:
        if not name.startswith("Default__"):
            continue
        for offset in range(len(payload) - 7):
            index, number = struct.unpack_from("<II", payload, offset)
            if index in wanted and number == 0 and wanted[index] not in result:
                result.append(wanted[index])
    return result


def extract(archive, equipment_paths):
    symbols, cache = ScriptObjects(archive.paks), {}

    def package_info(path):
        key = path.casefold()
        if key in cache:
            return cache[key]
        package = e.Package(archive.resource(archive.packages[key]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        exports, parent = [], None
        for index, (name, payload) in enumerate(package.exports):
            _, cls, superclass = struct.unpack_from("<QQQ", package.data, start + index * 72 + 24)
            class_name = symbols.resolve(package, cls)
            exports.append({"index": index, "name": name, "class": class_name})
            if class_name.endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, superclass)
        result = {"package": package, "exports": exports, "parent": parent,
                  "ownTagCandidates": cdo_tag_candidates(package)}
        cache[key] = result
        return result

    def ancestry(path):
        chain = []
        while path and path.casefold() in archive.packages:
            if path.casefold() in {entry[0].casefold() for entry in chain}:
                raise ValueError("Cyclic ability ancestry: " + path)
            info = package_info(path)
            chain.append((path, info))
            path = info["parent"]
        return chain

    equipment, owners = [], {}
    for path in equipment_paths:
        info = package_info(path)
        fields = equipment_fields(info["package"])
        equipment.append(dict({"package": path}, **fields))
        for ability in fields["equipAbilities"] + fields["owningAbilities"]:
            if ability and ability.casefold() in archive.packages:
                owners.setdefault(ability, []).append(path)
    records = []
    for path in sorted(owners):
        chain = ancestry(path)
        tag_owner = next(((owner, info["ownTagCandidates"]) for owner, info in chain if info["ownTagCandidates"]), None)
        if tag_owner is None or not any(tag.startswith("Ability.Action.Hun.CombatArt.") for tag in tag_owner[1]):
            continue
        timelines = {}
        for owner, info in reversed(chain):
            package = info["package"]
            for row in info["exports"]:
                if row["class"] != "/Script/LegoAbility.LGATimeline":
                    continue
                value = timeline(package, row["index"])
                value["owner"] = owner
                for event in value["actions"]:
                    if event["actionPointer"] <= 0:
                        event["class"] = package.object_name(event["actionPointer"])
                        continue
                    action = info["exports"][event["actionPointer"] - 1]
                    event["class"] = action["class"]
                    event["directAttackAction"] = any(part in action["class"] for part in ("Bullet", "Damage", "HitBox"))
                    event["resourceReferences"] = object_references(package, package.exports[action["index"]][1])
                timelines[value["name"]] = value
        records.append({"package": path, "equipment": owners[path], "tagCandidates": tag_owner[1],
                        "tagEvidenceOwner": tag_owner[0], "ancestry": [owner for owner, _ in chain],
                        "timelines": list(timelines.values())})
    return {"equipment": equipment, "combatArtAbilities": len(records), "records": records}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--build", default="CL-302256")
    parser.add_argument("--registry", type=pathlib.Path)
    parser.add_argument("--weapon-audit", type=pathlib.Path, default=ROOT / "docs/complete-move-weapon-audit.json")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    if args.registry:
        registry = json.loads(args.registry.read_text(encoding="utf-8"))
        paths = sorted(asset["package"] for asset in registry["assets"]
                       if asset["assetClass"] in ("PBZ_MainWeaponConfig_C", "PBZ_TempWeaponConfig_C")
                       and "/Players/" in asset["package"])
        boundary = {"source": "AssetRegistry class enumeration", "configCount": len(paths)}
    else:
        audit = json.loads(args.weapon_audit.read_text(encoding="utf-8"))
        if audit["build"] != args.build:
            raise ValueError("The registry-backed equipment index belongs to another build")
        paths = sorted(item["equipmentId"] for item in audit["equipment"])
        boundary = {"source": str(args.weapon_audit), "configCount": len(paths),
                    "scope": "Recorded registry-backed equipment universe for this build"}
    archive = e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        result = extract(archive, paths)
    finally:
        archive.close()
    result.update({"build": args.build, "runtimeVerified": False, "boundary": boundary,
                   "evidenceType": "Static cooked Equip/Owning grants, inheritance, LGATimeline and action resources",
                   "method": "Read every combat-art timeline and every action; no role or wait-input filter",
                   "limitations": ["CDO tag candidates do not identify every native GAS property.",
                                   "Attack action nodes are not automatically separate complete moves.",
                                   "Resource references retain byte offsets; individual action property schemas are not all decoded."]})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Read %d equipment configurations, %d equipped combat-art abilities, %d effective timelines" %
          (len(result["equipment"]), result["combatArtAbilities"], sum(len(record["timelines"]) for record in result["records"])))


if __name__ == "__main__":
    main()
