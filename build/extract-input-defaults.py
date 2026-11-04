"""Read CL-301524 or CL-302256 input defaults without running the game.

Requires Python, pycryptodome, and a caller-supplied Oodle 8 DLL. Archive keys
remain in memory. Only input mapping metadata is written to the output JSON.
"""
import argparse
import collections
import ctypes
import json
import pathlib
import struct

from Crypto.Cipher import AES


KEY_PART_OFFSETS = {
    "CL-301524": (
        0x1964332C, 0x19643333, 0x1964333A, 0x19643341,
        0x1964334C, 0x19643353, 0x1964335A, 0x19643361,
    ),
    "CL-302256": (
        0x18DC40CC, 0x18DC40D3, 0x18DC40DA, 0x18DC40E1,
        0x18DC40EC, 0x18DC40F3, 0x18DC40FA, 0x18DC4101,
    ),
}
PROFILES = (
    "InputUserSettings.Profiles.Default",
    "PBZ.KeyProfile.Custom1", "PBZ.KeyProfile.Custom2",
)


class Reader:
    def __init__(self, data, names=()):
        self.data = data
        self.position = 0
        self.names = names

    def read(self, size):
        result = self.data[self.position:self.position + size]
        if len(result) != size:
            raise ValueError("Truncated input resource")
        self.position += size
        return result

    def number(self, fmt="<i"):
        return struct.unpack(fmt, self.read(struct.calcsize(fmt)))[0]

    def string(self):
        length = self.number()
        if length >= 0:
            return self.read(length)[:-1].decode("utf-8") if length else ""
        return self.read(-length * 2)[:-2].decode("utf-16le")

    def name(self):
        index, number = self.number("<I"), self.number("<I")
        return self.names[index] + ("_" + str(number - 1) if number else "")

    def header(self):
        fragments = []
        index = masked = 0
        while True:
            value = self.number("<H")
            skip, zero, last, count = value & 127, bool(value & 128), bool(value & 256), value >> 9
            index += skip
            fragments.append((index, count, zero))
            index += count
            if zero:
                masked += count
            if last:
                break
        mask_size = 1 if masked <= 8 else 2 if masked <= 16 else ((masked + 31) // 32) * 4
        mask = int.from_bytes(self.read(mask_size), "little") if masked else 0
        result = {}
        bit = 0
        for index, count, zero in fragments:
            for offset in range(count):
                result[index + offset] = bool(mask & (1 << bit)) if zero else False
                if zero:
                    bit += 1
        return result

    def object_array(self):
        return [self.number() for _ in range(self.number())]


def name_batch(data, position):
    count = struct.unpack_from("<I", data, position)[0]
    if not count:
        return []
    position += 16 + count * 8
    lengths = [int.from_bytes(data[position + i * 2:position + i * 2 + 2], "big") for i in range(count)]
    position += count * 2
    names = []
    for length in lengths:
        wide = bool(length & 0x8000)
        size = (length & 0x7fff) * (2 if wide else 1)
        names.append(data[position:position + size].decode("utf-16le" if wide else "utf-8"))
        position += size
    return names


class Package:
    def __init__(self, data):
        self.data = data
        self.names = name_batch(data, 60)
        self.imports = name_batch(data, struct.unpack_from("<I", data, 48)[0])
        self.import_offset = struct.unpack_from("<I", data, 28)[0]
        header = struct.unpack_from("<I", data, 4)[0]
        start, end = struct.unpack_from("<II", data, 32)
        self.exports = []
        for position in range(start, end, 72):
            offset, size, index, number = struct.unpack_from("<QQII", data, position)
            name = self.names[index] + ("_" + str(number - 1) if number else "")
            self.exports.append((name, data[header + offset:header + offset + size]))

    def input_action(self, pointer):
        if pointer >= 0:
            raise ValueError("Expected imported input action")
        value = struct.unpack_from("<Q", self.data, self.import_offset + (-pointer - 1) * 8)[0]
        if value >> 62 != 2:
            raise ValueError("Expected package input action import")
        return self.imports[(value >> 32) & 0x3fffffff]

    def object_name(self, pointer):
        if pointer > 0:
            return self.exports[pointer - 1][0]
        if pointer == 0:
            return None
        value = struct.unpack_from("<Q", self.data, self.import_offset + (-pointer - 1) * 8)[0]
        return self.imports[(value >> 32) & 0x3fffffff] if value >> 62 == 2 else "ScriptObject:" + hex(value)

    def settings(self, pointer):
        if not pointer:
            return None
        _, data = self.exports[pointer - 1]
        reader = Reader(data, self.names)
        fields = reader.header()
        if fields not in ({1: False}, {1: False, 2: False}):
            raise ValueError("Unsupported player mappable key settings")
        return reader.name()

    def input_action_settings(self):
        reader = Reader(self.exports[0][1], self.names)
        fields = reader.header()
        if fields.get(10, True):
            return None
        if any(field < 10 and not zero for field, zero in fields.items()):
            raise ValueError("Unsupported InputAction properties before PlayerMappableKeySettings")
        return self.settings(reader.number())

    def context(self, path, resolve_action_settings):
        reader = Reader(self.exports[0][1], self.names)
        # UInputMappingContext.DefaultKeyMappings / FInputMappingProfile.Mappings.
        # Any MappingProfileOverrides or added schema field requires decoding.
        if reader.header() != {1: False} or reader.header() != {0: False}:
            raise ValueError("Unsupported IMC properties: " + path)
        count = reader.number()
        mappings = []
        counters = collections.Counter()
        for index in range(count):
            fields = reader.header()
            mapping = {"mappingIndex": index, "settingsBehavior": 0}
            settings_pointer = 0
            for field, zero in fields.items():
                if field in (0, 1):
                    values = [] if zero else reader.object_array()
                    mapping["triggers" if field == 0 else "modifiers"] = [self.object_name(value) for value in values]
                elif field == 2:
                    mapping["inputAction"] = None if zero else self.input_action(reader.number())
                elif field == 3:
                    if zero:
                        mapping["key"] = "None"
                    else:
                        if reader.header() != {0: False}:
                            raise ValueError("Unsupported FKey")
                        mapping["key"] = reader.name()
                elif field in (4, 5, 6):
                    value = 0 if zero else reader.number("<B")
                    if field == 6:
                        mapping["settingsBehavior"] = value
                elif field == 7:
                    settings_pointer = 0 if zero else reader.number()
                else:
                    raise ValueError("Unsupported mapping property: " + str(field))
            behavior = mapping["settingsBehavior"]
            if behavior == 0:
                action = mapping.get("inputAction")
                mapping["action"] = resolve_action_settings(action) if action else None
            elif behavior == 1:
                mapping["action"] = self.settings(settings_pointer)
            elif behavior == 2:
                mapping["action"] = None
            else:
                raise ValueError("Unsupported mapping settings behavior: " + str(behavior))
            key = mapping["key"]
            mapping["device"] = "Gamepad" if key.startswith(("Gamepad_", "PBZ_Touchpad_")) else "KeyboardMouse"
            if mapping["action"]:
                mapping["rawSlot"] = counters[mapping["action"]]
                counters[mapping["action"]] += 1
            mappings.append(mapping)
        if reader.read(4) != bytes(4) or reader.position != len(reader.data):
            raise ValueError("Unexpected trailing IMC data: " + path)
        return {"context": path, "mappingCount": count, "profileOverrides": {}, "mappings": mappings}


class Archive:
    def __init__(self, game_exe, paks, oodle, build):
        with game_exe.open("rb") as stream:
            parts = []
            for offset in KEY_PART_OFFSETS[build]:
                stream.seek(offset)
                parts.append(stream.read(4))
        if any(len(part) != 4 for part in parts):
            raise ValueError("Unsupported game executable")
        self.cipher = AES.new(b"".join(parts), AES.MODE_ECB)
        self.paks = paks
        self.toc = (paks / "ProjectPBZ-Windows.utoc").read_bytes()
        self.entries, blocks = struct.unpack_from("<II", self.toc, 24)
        self.block_size = struct.unpack_from("<I", self.toc, 44)[0]
        directory_size = struct.unpack_from("<I", self.toc, 48)[0]
        self.partition_size = struct.unpack_from("<Q", self.toc, 88)[0]
        seed_count = struct.unpack_from("<I", self.toc, 84)[0]
        if self.toc[:16] != b"-==--==--==--==-" or self.toc[16] != 8:
            raise ValueError("Unsupported IOStore version")
        self.block_base = 144 + self.entries * 22 + seed_count * 4
        directory_offset = self.block_base + blocks * 12 + 32
        directory = self.cipher.decrypt(self.toc[directory_offset:directory_offset + directory_size])
        if directory[:13] != b"\x0a\0\0\0../../../":
            raise ValueError("Unsupported executable or archive encryption")
        self.files = self.directory(directory)
        self.packages = self.package_paths(self.files)
        self.action_settings = {}
        self.handles = {}
        self.blocks = {}
        self.dll = ctypes.WinDLL(str(oodle))
        self.decompress = self.dll.OodleLZ_Decompress
        self.decompress.restype = ctypes.c_longlong
        self.decompress.argtypes = [ctypes.c_void_p, ctypes.c_longlong, ctypes.c_void_p, ctypes.c_longlong,
                                   ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_void_p,
                                   ctypes.c_longlong, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                   ctypes.c_longlong, ctypes.c_int]

    @staticmethod
    def directory(data):
        reader = Reader(data)
        mount = reader.string()
        directories = [tuple(reader.number("<I") for _ in range(4)) for _ in range(reader.number())]
        files = [tuple(reader.number("<I") for _ in range(3)) for _ in range(reader.number())]
        names = [reader.string() for _ in range(reader.number())]
        result = {}

        def walk(index, path):
            name, child, _, file_index = directories[index]
            if name != 0xffffffff:
                path += names[name] + "/"
            while file_index != 0xffffffff:
                name, file_index, entry = files[file_index]
                result[mount + path + names[name]] = entry
            while child != 0xffffffff:
                walk(child, path)
                child = directories[child][2]

        walk(0, "")
        return result

    @staticmethod
    def package_paths(files):
        result = {}
        for path, entry in files.items():
            prefix, separator, relative = path.partition("/Content/")
            if not separator or not relative.endswith(".uasset"):
                continue
            mount = "Game" if prefix == "../../../ProjectPBZ" else prefix.rsplit("/", 1)[-1]
            package = ("/" + mount + "/" + relative[:-7]).casefold()
            if package in result and result[package] != entry:
                raise ValueError("Duplicate archive package: " + package)
            result[package] = entry
        return result

    def input_action_settings(self, package):
        identity = package.casefold()
        if identity not in self.action_settings:
            if identity not in self.packages:
                raise ValueError("InputAction package not found: " + package)
            data = self.resource(self.packages[identity])
            self.action_settings[identity] = Package(data).input_action_settings()
        return self.action_settings[identity]

    def block(self, index):
        if index in self.blocks:
            return self.blocks[index]
        position = self.block_base + index * 12
        data = self.toc[position:position + 12]
        offset = int.from_bytes(data[:5], "little")
        compressed = int.from_bytes(data[5:8], "little")
        size = int.from_bytes(data[8:11], "little")
        partition, offset = divmod(offset, self.partition_size)
        if partition not in self.handles:
            suffix = "_s" + str(partition) if partition else ""
            self.handles[partition] = (self.paks / ("ProjectPBZ-Windows" + suffix + ".ucas")).open("rb")
        stream = self.handles[partition]
        stream.seek(offset)
        data = self.cipher.decrypt(stream.read((compressed + 15) // 16 * 16))[:compressed]
        if data and self.toc[position + 11]:
            destination = ctypes.create_string_buffer(size)
            source = ctypes.create_string_buffer(data)
            written = self.decompress(source, compressed, destination, size, 1, 0, 0, None, 0, None, None, None, 0, 3)
            if written != size:
                raise ValueError("Oodle decompression failed")
            data = destination.raw
        if len(data) != size:
            raise ValueError("Unexpected IOStore block size")
        self.blocks[index] = data
        return data

    def resource(self, entry):
        position = 144 + self.entries * 12 + entry * 10
        data = self.toc[position:position + 10]
        offset, size = int.from_bytes(data[:5], "big"), int.from_bytes(data[5:], "big")
        first, last = offset // self.block_size, (offset + size + self.block_size - 1) // self.block_size
        data = b"".join(self.block(index) for index in range(first, last))
        offset %= self.block_size
        return data[offset:offset + size]

    def close(self):
        for stream in self.handles.values():
            stream.close()


def effective_bindings(bindings, build="CL-301524"):
    groups = collections.defaultdict(list)
    for binding in bindings:
        groups[(binding["action"], binding["rawSlot"])].append(binding)
    result = []
    for identity, candidates in groups.items():
        if len(candidates) == 1:
            result.extend(candidates)
        elif identity == ("IA_ShadowArm", 2) and {(b["device"], b["key"]) for b in candidates} == {
                ("KeyboardMouse", "LeftControl"), ("Gamepad", "Gamepad_RightTriggerAxis")}:
            # CL-302256 RegisterIMC mirrors slot 0 into slot 2 after base registration.
            device = "KeyboardMouse" if build == "CL-302256" else "Gamepad"
            result.extend(b for b in candidates if b["device"] == device)
        else:
            raise ValueError("Unresolved shared registration: " + str(identity))
    return sorted(result, key=lambda item: (item["action"], item["device"], item["rawSlot"]))


def extract(game_exe, paks, oodle, build="CL-301524"):
    archive = Archive(game_exe, paks, oodle, build)
    contexts = []
    try:
        for path, entry in sorted(archive.files.items()):
            if not path.endswith(".uasset") or not path.rsplit("/", 1)[-1].startswith("IMC_"):
                continue
            contexts.append(Package(archive.resource(entry)).context(path.removeprefix("../../../"), archive.input_action_settings))
    finally:
        archive.close()
    bindings = {}
    for context in contexts:
        for mapping in context["mappings"]:
            action = mapping["action"]
            if not action:
                continue
            identity = (action, mapping["rawSlot"], mapping["device"])
            if identity not in bindings:
                bindings[identity] = {"action": action, "rawSlot": mapping["rawSlot"], "key": mapping["key"],
                                      "device": mapping["device"], "sources": []}
            if bindings[identity]["key"] != mapping["key"]:
                raise ValueError("Conflicting context defaults: " + str(identity))
            bindings[identity]["sources"].append({"context": context["context"], "mappingIndex": mapping["mappingIndex"]})
    return {"build": build, "profiles": PROFILES, "profileDefaultsIdentical": True,
            "hardwareIdentifier": "None", "contexts": contexts,
            "bindings": sorted(bindings.values(), key=lambda item: (item["action"], item["device"], item["rawSlot"])),
            "effectiveBindings": effective_bindings(bindings.values(), build),
            "projectionEvidence": ("CL-302256 RegisterIMC override mirrors Shadow Arm raw slot 0 into slot 2; docs/input-defaults.md"
                                   if build == "CL-302256" else
                                   "CL-301524 native registration identity and the three captured profile DefaultKey tables; docs/input-defaults.md")}


def compact(result):
    aliases = collections.defaultdict(set)
    for context in result["contexts"]:
        for mapping in context["mappings"]:
            if mapping["action"]:
                aliases[mapping["inputAction"]].add(mapping["action"])
    fixed = {}
    for context in result["contexts"]:
        for mapping in context["mappings"]:
            if mapping["action"]:
                continue
            identity = (mapping["inputAction"], mapping["device"], mapping["key"])
            if identity not in fixed:
                fixed[identity] = {"inputAction": mapping["inputAction"], "actions": sorted(aliases[mapping["inputAction"]]),
                                   "device": mapping["device"], "key": mapping["key"], "sources": []}
            triggers = []
            for trigger in mapping.get("triggers", []):
                name, _, suffix = trigger.rpartition("_")
                triggers.append(name if suffix.isdigit() else trigger)
            fixed[identity]["sources"].append({"context": context["context"], "mappingIndex": mapping["mappingIndex"], "triggers": triggers})
    return {"build": result["build"], "profiles": result["profiles"],
            "profileDefaultsIdentical": result["profileDefaultsIdentical"],
            "bindings": result["bindings"],
            "effectiveBindings": result["effectiveBindings"],
            "projectionEvidence": result["projectionEvidence"],
            "fixedMappings": sorted(fixed.values(), key=lambda item: (item["inputAction"], item["device"], item["key"]))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--build", choices=KEY_PART_OFFSETS, default="CL-301524")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--compact", action="store_true", help="Write the maintained mapping dataset without per-context decoding details")
    args = parser.parse_args()
    result = extract(args.game_exe, args.paks, args.oodle, args.build)
    output = compact(result) if args.compact else result
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Extracted", len(result["contexts"]), "contexts and", len(result["bindings"]), "default bindings")


if __name__ == "__main__":
    main()
