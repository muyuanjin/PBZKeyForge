"""Move the stance-dependent dash routes from dodge to jump in cooked assets."""
import argparse
import copy
import json
import pathlib
import struct

import resources

e = resources.e
DASH = "/PBZCombatAsset/ActorData/Players/Hun/InputMap/AIM_Hun_Dash"
JUMP = "/PBZCombatAsset/ActorData/Players/Hun/InputMap/AIM_Hun_Jump"
HOLDING = "CharacterState.AbilityState.Stance.HoldingStanceKey"
SPECIAL = "Ability.Action.Hun.SpecialDash"
DEFLECT = "Ability.Action.Hun.DeflectDash"
SCHEMAS = {
    "item": [("ActionPriority", "int"), ("Description", "string"),
             ("NormalData", "data"), ("TagConditionData", "map")],
    "data": [("Actions", "array"), ("Combos", "array"), ("InterruptLower", "array")],
    "trigger": [("Description", "string"), ("RequiredTags", "tags"),
                ("IgnoredTags", "tags"), ("TriggerTag", "tag"), ("DebugAbility", "int")],
}


class BindingDocument:
    def __init__(self, payload, names):
        self.names = list(names)
        self.name_numbers = {}
        self.tag_headers = {}
        self.reader = e.Reader(payload, names)
        begin = self.reader.position
        self.root_fields = self.reader.header()
        if self.root_fields not in ({0: False}, {}, {0: True}):
            raise ValueError("Expected InputActionBinding.Bindings")
        self.header = payload[begin:self.reader.position]
        self.items = ([self.read_struct("item") for _ in range(self.reader.number())]
                      if self.root_fields == {0: False} else [])
        self.tail = payload[self.reader.position:]
        if self.tail != bytes(4):
            raise ValueError("Unexpected InputActionBinding trailing data")

    def read_name(self):
        index, number = self.reader.number("<I"), self.reader.number("<I")
        base = self.names[index]
        name = base + ("_" + str(number - 1) if number else "")
        self.name_numbers[name] = (base, number)
        return name

    def read_tag(self):
        begin = self.reader.position
        fields = self.reader.header()
        if fields == {0: False}:
            name = self.read_name()
            self.tag_headers[name] = self.reader.data[begin:self.reader.position - 8]
            return name
        elif fields in ({}, {0: True}):
            self.tag_headers[None] = self.reader.data[begin:self.reader.position]
            return None
        else:
            raise ValueError("Unexpected GameplayTag fields")

    def read_struct(self, kind):
        reader = self.reader
        begin = reader.position
        fields = reader.header()
        value = {"_kind": kind, "_fields": fields,
                 "_header": reader.data[begin:reader.position].hex(), "_strings": {}}
        schema = SCHEMAS[kind]
        for index, zero in fields.items():
            name, typ = schema[index]
            if zero:
                value[name] = None
            elif typ == "int":
                value[name] = reader.number()
            elif typ == "string":
                start = reader.position
                value[name] = reader.string()
                value["_strings"][name] = reader.data[start:reader.position].hex()
            elif typ == "data":
                value[name] = self.read_struct("data")
            elif typ == "array":
                value[name] = [self.read_struct("trigger") for _ in range(reader.number())]
            elif typ == "tags":
                value[name] = [self.read_name() for _ in range(reader.number())]
            elif typ == "tag":
                value[name] = self.read_tag()
            elif typ == "map":
                removed = [self.read_tag() for _ in range(reader.number())]
                pairs = [{"tag": self.read_tag(), "value": self.read_struct("data")}
                         for _ in range(reader.number())]
                value[name] = {"removed": removed, "pairs": pairs}
        return value

    def fname(self, name):
        base, number = self.name_numbers.get(name, (name, 0))
        if base not in self.names:
            self.names.append(base)
        return struct.pack("<II", self.names.index(base), number)

    def write_tag(self, name):
        header = self.tag_headers.get(name, b"\x00\x01" if name is None else b"\x00\x03")
        return header if name is None else header + self.fname(name)

    def write_struct(self, value):
        output = bytearray.fromhex(value["_header"])
        for index, zero in value["_fields"].items():
            name, typ = SCHEMAS[value["_kind"]][int(index)]
            if zero:
                continue
            item = value[name]
            if typ == "int":
                output += struct.pack("<i", item)
            elif typ == "string":
                raw = bytes.fromhex(value["_strings"][name])
                original = e.Reader(raw).string()
                if original != item:
                    raw = item.encode("utf-8") + b"\0"
                    raw = struct.pack("<i", len(raw)) + raw
                output += raw
            elif typ == "data":
                output += self.write_struct(item)
            elif typ == "array":
                output += struct.pack("<i", len(item))
                for entry in item:
                    output += self.write_struct(entry)
            elif typ == "tags":
                output += struct.pack("<i", len(item))
                for tag in item:
                    output += self.fname(tag)
            elif typ == "tag":
                output += self.write_tag(item)
            elif typ == "map":
                output += struct.pack("<i", len(item["removed"]))
                for tag in item["removed"]:
                    output += self.write_tag(tag)
                output += struct.pack("<i", len(item["pairs"]))
                for pair in item["pairs"]:
                    output += self.write_tag(pair["tag"]) + self.write_struct(pair["value"])
        return bytes(output)

    def encode(self):
        if self.root_fields != {0: False}:
            if self.items:
                raise ValueError("Cannot add entries to a zero-valued Bindings root")
            return self.header + self.tail
        return (self.header + struct.pack("<i", len(self.items))
                + b"".join(self.write_struct(item) for item in self.items) + self.tail)


def clean(value):
    if isinstance(value, dict):
        return {key: clean(item) for key, item in value.items() if not key.startswith("_")}
    if isinstance(value, list):
        return [clean(item) for item in value]
    return value


def triggers(item):
    data = item.get("NormalData")
    if data:
        for name in ("Actions", "Combos", "InterruptLower"):
            yield from data.get(name) or []
    conditional = item.get("TagConditionData")
    if conditional:
        for pair in conditional["pairs"]:
            for name in ("Actions", "Combos", "InterruptLower"):
                yield from pair["value"].get(name) or []


def move_special_dash(dash_source, jump_source):
    dash_pkg, jump_pkg = e.Package(dash_source), e.Package(jump_source)
    if len(dash_pkg.exports) != 1 or len(jump_pkg.exports) != 1:
        raise ValueError("Expected single-export input binding assets")
    dash = BindingDocument(dash_pkg.exports[0][1], dash_pkg.names)
    jump = BindingDocument(jump_pkg.exports[0][1], jump_pkg.names)
    if dash.encode() != dash_pkg.exports[0][1] or jump.encode() != jump_pkg.exports[0][1]:
        raise ValueError("Input binding reader/writer must roundtrip unchanged resources")
    original_dash, original_jump = copy.deepcopy(dash.items), copy.deepcopy(jump.items)
    selected = [item for item in dash.items
                if any(trigger.get("TriggerTag") == SPECIAL for trigger in triggers(item))]
    if len(selected) != 1:
        raise ValueError("Expected one stance-dependent SpecialDash binding group")
    moved = selected[0]
    if any(trigger.get("TriggerTag") not in (SPECIAL, DEFLECT) for trigger in triggers(moved)):
        raise ValueError("SpecialDash group contains an unrelated capability")
    dash.items.remove(moved)
    moved = copy.deepcopy(moved)
    # Every new jump route requires holding guard, including the deflect-start route.
    for trigger in triggers(moved):
        required = trigger.get("RequiredTags") or []
        if HOLDING not in required:
            required.append(HOLDING)
        trigger["RequiredTags"] = required
    jump.items.insert(0, moved)
    jump.name_numbers.update(dash.name_numbers)
    jump.tag_headers.update(dash.tag_headers)
    dash_payload, jump_payload = dash.encode(), jump.encode()
    changed = [resources.rebuild_exports(dash_source, {dash_pkg.exports[0][0]: dash_payload}, dash.names),
               resources.rebuild_exports(jump_source, {jump_pkg.exports[0][0]: jump_payload}, jump.names)]
    after_dash = e.Package(changed[0])
    after_jump = e.Package(changed[1])
    dash_check = BindingDocument(after_dash.exports[0][1], after_dash.names)
    jump_check = BindingDocument(after_jump.exports[0][1], after_jump.names)
    if any(t.get("TriggerTag") in (SPECIAL, DEFLECT) for item in dash_check.items for t in triggers(item)):
        raise ValueError("Original dodge input still dispatches stance dash")
    if clean(dash_check.items) != clean([item for item in original_dash if item != selected[0]]):
        raise ValueError("Unrelated dodge binding changed")
    if clean(jump_check.items[1:]) != clean(original_jump):
        raise ValueError("Original jump binding changed")
    if any(HOLDING not in (t.get("RequiredTags") or []) for t in triggers(jump_check.items[0])):
        raise ValueError("A migrated jump route does not require guard")
    return changed, {"source": DASH, "destination": JUMP,
                     "movedPriority": moved["ActionPriority"],
                     "movedRoutes": [clean(t) for t in triggers(moved)],
                     "remainingDodgeGroups": [item.get("Description") for item in dash_check.items],
                     "originalJumpGroups": len(original_jump), "runtimeVerified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--retoc", type=pathlib.Path, required=True)
    parser.add_argument("--build", choices=e.KEY_PART_OFFSETS, default="CL-302256")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    archive = e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        entries = [archive.packages[path.casefold()] for path in (DASH, JUMP)]
        source = [archive.resource(entry) for entry in entries]
        changed, report = move_special_dash(*source)
        header = archive.resource(next(index for index in range(archive.entries)
                                       if archive.toc[144 + index * 12 + 11] == 6))
        packages = [{"chunk": archive.toc[144 + entry * 12:156 + entry * 12], "data": data,
                     "imports": resources.source_imports(archive, header, entry),
                     "file": next(name for name, number in archive.files.items() if number == entry)}
                    for entry, data in zip(entries, changed)]
        args.output.mkdir(parents=True, exist_ok=True)
        resources.STEM = "PBZKeyForge_StanceDashOnJump_P"
        toc = resources.pack(args.output.resolve(), args.retoc.resolve(), packages)
        report["container"] = str(toc)
        (args.output / "special-dash-report.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"container": str(toc), "packages": 2,
                          "movedRoutes": len(report["movedRoutes"]), "runtimeVerified": False}))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
