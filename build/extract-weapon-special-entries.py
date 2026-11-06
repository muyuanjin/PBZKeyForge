"""Read native grants and attack resources for CL-302256 special weapon entries.

The curated routes retain normal activation, original early-release selection,
and native weapon-switch ownership. This command does not launch the game.
"""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)


def inspect(archive, symbols, path):
    package = combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))
    start = struct.unpack_from("<I", package.data, 32)[0]
    exports, parent = [], None
    for index, (name, payload) in enumerate(package.exports):
        _, cls, superclass = struct.unpack_from("<QQQ", package.data, start + index * 72 + 24)
        cls = symbols.resolve(package, cls)
        exports.append({"name": name, "class": cls, "index": index})
        if cls.endswith("BlueprintGeneratedClass"):
            parent = symbols.resolve(package, superclass)
    return package, exports, parent


def configuration(package):
    reader = combat_art.e.Reader(package.exports[0][1], package.names)
    fields = {}
    for field, zero in reader.header().items():
        if field > 10:
            break
        if zero:
            value = None
        elif field in (0, 1):
            value = [reader.name() for _ in range(reader.number())]
        elif field == 4:
            value = reader.number("<d")
        elif field == 10:
            value = package.object_name(reader.number())
        else:
            value = [package.object_name(reader.number()) for _ in range(reader.number())]
        fields[str(field)] = value
    return fields


def tag_references(package, payload):
    result = []
    for offset in range(len(payload)-7):
        index, number = struct.unpack_from("<II",payload,offset)
        if number == 0 and index < len(package.names) and package.names[index].startswith(("Ability.","CharacterState.")):
            result.append({"exportOffset":offset,"tag":package.names[index]})
    return result


def read_resources(archive, symbols, paths):
    result, pending = {}, list(paths)
    while pending:
        path = pending.pop().split(".")[0]
        if path in result or path.casefold() not in archive.packages:
            continue
        package, exports, parent = inspect(archive,symbols,path)
        cdo = next((payload for name,payload in package.exports if name.startswith("Default__")),b"")
        functions = []
        imports_end = struct.unpack_from("<I",package.data,32)[0]
        imports = [(slot,symbols.resolve(package,struct.unpack_from("<Q",package.data,package.import_offset+slot*8)[0]))
                   for slot in range((imports_end-package.import_offset)//8)]
        for row in exports:
            if row["class"] != "/Script/CoreUObject.Function":
                continue
            payload = package.exports[row["index"]][1]
            calls = [name for slot,name in imports if any(bytes([opcode])+struct.pack("<i",-slot-1) in payload
                     for opcode in (0x1c,0x46,0x68))]
            functions.append({"name":row["name"],"calls":calls,"tags":tag_references(package,payload)})
        timelines = []
        for row in exports:
            if row["class"] != "/Script/LegoAbility.LGATimeline":
                continue
            timeline = combat_art.timeline(package,row["index"])
            for event in timeline["actions"]:
                event["class"] = exports[event["actionPointer"]-1]["class"]
            timelines.append({"name":row["name"],"actions":timeline["actions"]})
        result[path] = {"parent":parent,"cdoHeader":combat_art.e.Reader(cdo,package.names).header() if cdo else {},
                        "cdoTags":tag_references(package,cdo),"functions":functions,"timelines":timelines}
        if parent:
            pending.append(parent)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe",type=pathlib.Path,required=True)
    parser.add_argument("--paks",type=pathlib.Path,required=True)
    parser.add_argument("--oodle",type=pathlib.Path,required=True)
    parser.add_argument("--output",type=pathlib.Path,required=True)
    args = parser.parse_args()
    archive = combat_art.e.Archive(args.game_exe,args.paks,args.oodle,"CL-302256")
    symbols = combat_art.ScriptObjects(args.paks)
    evidence = json.loads((ROOT/"docs/complete-move-weapon-special-entries.json").read_text(encoding="utf8"))
    try:
        evidence["equipmentEvidence"] = {path:configuration(inspect(archive,symbols,path)[0])
                                          for path in evidence["equipmentEvidence"]}
        evidence["abilityEvidence"] = read_resources(archive,symbols,evidence["abilityEvidence"])
        args.output.write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
    finally:
        archive.close()


if __name__ == "__main__":
    main()
