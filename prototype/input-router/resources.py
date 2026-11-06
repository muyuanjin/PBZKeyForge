"""Compile fixed Enhanced Input mappings into a PBZ Zen resource container.

Bindings bypass PlayerMappableKeySettings and its seven save slots. The compiler
patches existing, already loaded mapping contexts; it does not install a mod.
"""
import argparse
import importlib.util
import json
import pathlib
import shutil
import struct
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("pbz_input_resources", ROOT / "build/extract-input-defaults.py")
e = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(e)
NULL = 0xffffffffffffffff
STEM = "PBZKeyForge_InputRouter_P"


def header(fields):
    """Serialize explicit nonzero-mask fields of an unversioned property header."""
    fragments = []
    cursor = 0
    for index in sorted(fields):
        gap = index - cursor
        while gap > 127:
            fragments.append(127)
            gap -= 127
        fragments.append(gap | 512)
        cursor = index + 1
    if not fragments:
        fragments = [0]
    fragments[-1] |= 256
    return b"".join(struct.pack("<H", value) for value in fragments)


def properties(values):
    return header(values) + b"".join(values[index] for index in sorted(values))


def name_batch(names):
    if not names:
        return struct.pack("<I", 0)
    wide = [not name.isascii() for name in names]
    encoded = [name.encode("utf-16le" if is_wide else "ascii") for name, is_wide in zip(names, wide)]
    # Hash version zero selects UE's supported LoadNameBatch rehash path.
    return (struct.pack("<IIQ", len(names), sum(map(len, encoded)), 0)
            + bytes(8 * len(names))
            + b"".join(((0x8000 | (len(value) // 2)) if is_wide else len(value)).to_bytes(2, "big")
                       for value, is_wide in zip(encoded, wide))
            + b"".join(encoded))


def rebuild_exports(source, replacements, names=None):
    """Replace existing export payloads; optionally append names to the name map."""
    package = e.Package(source)
    old_header_size = struct.unpack_from("<I", source, 4)[0]
    result = bytearray(source[:old_header_size])
    if names is not None:
        if list(names[:len(package.names)]) != package.names:
            raise ValueError("Existing FName indices must remain unchanged")
        count, size = struct.unpack_from("<II", source, 60)
        old_end = 76 + count * 10 + size
        batch = name_batch(names)
        delta = len(batch) - (old_end - 60)
        result[60:old_end] = batch
        for offset in (4, 24, 28, 32, 36, 40, 44, 48, 52, 56):
            struct.pack_into("<I", result, offset, struct.unpack_from("<I", source, offset)[0] + delta)
    export_map = struct.unpack_from("<I", result, 32)[0]
    payload = bytearray()
    for index, (name, original) in enumerate(package.exports):
        value = replacements.get(index, replacements.get(name, original))
        struct.pack_into("<QQ", result, export_map + index * 72, len(payload), len(value))
        payload += value
    return bytes(result + payload)


class Symbols:
    def __init__(self, paks):
        toc = (paks / "global.utoc").read_bytes()
        size = int.from_bytes(toc[161:166], "big")
        blob = (paks / "global.ucas").read_bytes()[:size]
        names = e.name_batch(blob, 0)
        count = len(names)
        lengths = blob[16 + count * 8:16 + count * 10]
        end = 16 + count * 10
        for index in range(count):
            length = int.from_bytes(lengths[index * 2:index * 2 + 2], "big")
            end += (length & 0x7fff) * (2 if length & 0x8000 else 1)
        records = struct.unpack_from("<I", blob, end)[0]
        objects = {}
        for index in range(records):
            ni, number, identity, outer, _ = struct.unpack_from("<IIQQQ", blob, end + 4 + index * 32)
            objects[identity] = (names[ni & 0x3fffffff] + ("_" + str(number - 1) if number else ""), outer)
        def resolve(identity):
            name, outer = objects[identity]
            return resolve(outer) + "." + name if outer in objects else name
        self.by_name = {resolve(identity): identity for identity in objects}

    def get(self, name):
        return self.by_name["/Script/EnhancedInput." + name]


class ContextPackage(e.Package):
    def __init__(self, data, path):
        super().__init__(data)
        self.path = path

    def input_action(self, pointer):
        if pointer > 0:
            return self.path + ":" + self.exports[pointer - 1][0]
        return super().input_action(pointer)


def parse_mappings(package):
    reader = e.Reader(package.exports[0][1], package.names)
    if reader.header() != {1: False} or reader.header() != {0: False}:
        raise ValueError("Only default-mapping IMCs without profile overrides are supported")
    count = reader.number()
    result = []
    for _ in range(count):
        start = reader.position
        fields = reader.header()
        action = 0
        for field, zero in fields.items():
            if zero:
                continue
            if field in (0, 1):
                reader.object_array()
            elif field == 2:
                action = reader.number()
            elif field == 3:
                if reader.header() != {0: False}:
                    raise ValueError("Unsupported FKey serialization")
                reader.name()
            elif field in (4, 5, 6):
                reader.read(1)
            elif field == 7:
                reader.read(4)
            else:
                raise ValueError("Unsupported mapping property " + str(field))
        result.append((package.input_action(action) if action < 0 else None,
                       reader.data[start:reader.position]))
    if reader.read(4) != bytes(4) or reader.position != len(reader.data):
        raise ValueError("Unexpected data after IMC mappings")
    return result


class ZenContext:
    def __init__(self, source, archive, symbols, path):
        self.source, self.archive, self.symbols, self.path = source, archive, symbols, path
        self.package = e.Package(source)
        self.names = list(self.package.names)
        self.import_names = list(self.package.imports)
        values = struct.unpack_from("<15I", source)
        if values[0] != 0 or values[13] != values[9] or values[14] != values[9]:
            raise ValueError("Unsupported versioned or cell-enabled Zen package")
        self.original_header = values
        self.hashes = list(struct.unpack_from("<" + "Q" * ((values[7] - values[6]) // 8), source, values[6]))
        self.imports = list(struct.unpack_from("<" + "Q" * ((values[8] - values[7]) // 8), source, values[7]))
        self.exports = [bytearray(source[pos:pos + 72]) for pos in range(values[8], values[9], 72)]
        self.payloads = [blob for _, blob in self.package.exports]
        self.bundle = list(struct.iter_unpack("<II", source[values[9]:values[10]]))
        self.dependencies = []
        for index in range(len(self.exports)):
            first, *counts = struct.unpack_from("<5I", source, values[10] + index * 20)
            all_entries = list(struct.unpack_from("<" + "i" * sum(counts), source, values[11] + first * 4))
            groups = []
            for count in counts:
                groups.append(all_entries[:count])
                del all_entries[:count]
            self.dependencies.append(groups)
        self.new_export_indices = []
        self.added_packages = []
        self.mappings = parse_mappings(self.package)
        self.helpers = {}

    def name(self, text):
        if text not in self.names:
            self.names.append(text)
        return struct.pack("<II", self.names.index(text), 0)

    def add_import(self, identity):
        if identity not in self.imports:
            self.imports.append(identity)
        return -self.imports.index(identity) - 1

    def action(self, package_path):
        for index, identity in enumerate(self.imports):
            if identity >> 62 == 2 and self.import_names[(identity >> 32) & 0x3fffffff].casefold() == package_path.casefold():
                return -index - 1
        entry = self.archive.packages.get(package_path.casefold())
        if entry is None:
            raise ValueError("InputAction package not found: " + package_path)
        source = self.archive.resource(entry)
        action_package = e.Package(source)
        export_start = struct.unpack_from("<I", source, 32)[0]
        class_id = struct.unpack_from("<Q", source, export_start + 32)[0]
        if class_id != self.symbols.get("InputAction"):
            raise ValueError("Target package is not an InputAction: " + package_path)
        public_hash = struct.unpack_from("<Q", source, export_start + 56)[0]
        if not public_hash:
            raise ValueError("InputAction has no public export identity: " + package_path)
        package_name = action_package.names[struct.unpack_from("<I", source, 8)[0] & 0x3fffffff]
        if package_name in self.import_names:
            package_index = self.import_names.index(package_name)
        else:
            package_index = len(self.import_names)
            self.import_names.append(package_name)
            self.added_packages.append(struct.unpack_from("<Q", self.archive.toc, 144 + entry * 12)[0])
        if public_hash not in self.hashes:
            self.hashes.append(public_hash)
        identity = (2 << 62) | (package_index << 32) | self.hashes.index(public_hash)
        pointer = self.add_import(identity)
        self.dependencies[0][2].append(pointer)
        return pointer

    def export(self, class_name, payload, dependencies=()):
        index = len(self.exports)
        object_name = "PBZKeyForge_InputRouter_" + str(index)
        record = bytearray(72)
        struct.pack_into("<II", record, 16, self.names.index(object_name) if object_name in self.names else len(self.names), 0)
        self.name(object_name)
        struct.pack_into("<QQQQQIB3s", record, 24, 0, self.symbols.get(class_name), NULL,
                         self.symbols.get("Default__" + class_name), 0, 9, 0, bytes(3))
        self.exports.append(record)
        self.payloads.append(payload)
        self.dependencies.append([[1], [], list(dependencies), []])
        self.dependencies[0][2].append(index + 1)
        self.new_export_indices.append(index)
        return index + 1

    def trigger(self, kind, spec):
        classes = {"pressed": "InputTriggerPressed", "down": "InputTriggerDown", "released": "InputTriggerReleased",
                   "hold": "InputTriggerHold", "tap": "InputTriggerTap", "chord": "InputTriggerChordAction"}
        if kind not in classes:
            raise ValueError("Resource trigger must be pressed, down, released, hold, or tap; use chord for modifiers")
        values = {}
        if kind == "hold":
            seconds = float(spec.get("seconds", 0.3))
            if seconds < 0:
                raise ValueError("Hold duration must be nonnegative")
            values = {0: struct.pack("<f", seconds), 1: bytes([bool(spec.get("once", True))])}
        elif kind == "tap":
            seconds = float(spec.get("seconds", 0.2))
            if seconds < 0:
                raise ValueError("Tap duration must be nonnegative")
            values = {0: struct.pack("<f", seconds)}
        elif kind == "chord":
            values = {0: struct.pack("<i", spec["action_pointer"])}
        payload = properties(values) + bytes(4)
        return self.export(classes[kind], payload, [spec["action_pointer"]] if kind == "chord" else [])

    def mapping(self, action, key, triggers):
        values = {0: struct.pack("<i", len(triggers)) + b"".join(struct.pack("<i", value) for value in triggers),
                  2: struct.pack("<i", action), 3: properties({0: self.name(key)}), 6: bytes([2])}
        return properties(values)

    def modifier_action(self, key):
        if key not in self.helpers:
            # IA_PerfectDodge and IA_Dodge contain this same bConsumeInput=false
            # field (InputAction schema index 2); modifiers must not swallow LB.
            action = self.export("InputAction", properties({2: bytes([0])}) + bytes(4))
            self.helpers[key] = action
            self.mappings.append((None, self.mapping(action, key, [])))
        return self.helpers[key]

    def configure(self, spec):
        mode = spec.get("mode", "append")
        if mode not in ("append", "replace"):
            raise ValueError("Context mode must be append or replace")
        removed = {text.casefold() for text in spec.get("remove_actions", [])}
        self.mappings = [] if mode == "replace" else [(name, blob) for name, blob in self.mappings if not name or name.casefold() not in removed]
        for binding in spec["bindings"]:
            action = self.action(binding["action"])
            trigger_spec = binding.get("trigger", "pressed")
            if isinstance(trigger_spec, str):
                trigger_spec = {"type": trigger_spec}
            kind = trigger_spec.get("type", "pressed")
            triggers = [self.trigger(kind, trigger_spec)]
            for key in trigger_spec.get("chord", []):
                helper = self.modifier_action(key)
                triggers.append(self.trigger("chord", {"action_pointer": helper}))
            for path in trigger_spec.get("chord_actions", []):
                triggers.append(self.trigger("chord", {"action_pointer": self.action(path)}))
            keys = binding.get("keys", [binding.get("key")])
            if not keys or any(not isinstance(key, str) or not key for key in keys):
                raise ValueError("Each binding requires key or keys")
            for key in keys:
                self.mappings.append((binding["action"], self.mapping(action, key, triggers)))
        root = properties({1: properties({0: struct.pack("<i", len(self.mappings)) + b"".join(blob for _, blob in self.mappings)})}) + bytes(4)
        self.payloads[0] = root

    def build(self):
        summary = bytearray(self.source[:60])
        original = self.original_header
        old_count, old_bytes = struct.unpack_from("<II", self.source, 60)
        old_names_end = 76 + old_count * 10 + old_bytes
        bulk_and_padding = self.source[old_names_end:original[6]]
        data = bytearray(summary + name_batch(self.names) + bulk_and_padding)
        offsets = {}
        offsets[24] = len(data)
        data += b"".join(struct.pack("<Q", value) for value in self.hashes)
        offsets[28] = len(data)
        data += b"".join(struct.pack("<Q", value) for value in self.imports)
        offsets[32] = len(data)
        payload = bytearray()
        for record, value in zip(self.exports, self.payloads):
            record = bytearray(record)
            struct.pack_into("<QQ", record, 0, len(payload), len(value))
            data += record
            payload += value
        offsets[36] = len(data)
        first_serialize = next(index for index, (_, command) in enumerate(self.bundle) if command == 1)
        bundle = self.bundle[:first_serialize] + [(index, 0) for index in self.new_export_indices] + self.bundle[first_serialize:]
        # New mapping subobjects serialize before the owning context.
        root_serialize = bundle.index((0, 1))
        bundle[root_serialize:root_serialize] = [(index, 1) for index in self.new_export_indices]
        data += b"".join(struct.pack("<II", *entry) for entry in bundle)
        offsets[40] = len(data)
        dependency_entries = []
        for groups in self.dependencies:
            data += struct.pack("<5I", len(dependency_entries), *(len(group) for group in groups))
            for group in groups:
                dependency_entries.extend(group)
        offsets[44] = len(data)
        data += b"".join(struct.pack("<i", value) for value in dependency_entries)
        offsets[48] = len(data)
        data += name_batch(self.import_names) + bytes(4 * len(self.import_names))
        # The package's original imported-name area ends at HeaderSize.
        offsets[4] = len(data)
        offsets[52] = offsets[56] = offsets[36]
        for offset, value in offsets.items():
            struct.pack_into("<I", data, offset, value)
        return bytes(data + payload)


def source_imports(archive, source_header, entry):
    count = struct.unpack_from("<I", source_header, 16)[0]
    ids = struct.unpack_from("<" + "Q" * count, source_header, 20)
    package_id = struct.unpack_from("<Q", archive.toc, 144 + entry * 12)[0]
    record = 24 + count * 8 + ids.index(package_id) * 16
    size, relative, shaders, _ = struct.unpack_from("<iiii", source_header, record)
    if shaders:
        raise ValueError("An input context unexpectedly contains shader dependencies")
    return list(struct.unpack_from("<" + "Q" * size, source_header, record + relative))


def pack(output, retoc, packages):
    raw = output / "raw"
    chunks = raw / "chunks"
    chunks.mkdir(parents=True, exist_ok=True)
    # Keep one invocation's source separate from prior containers.
    if any(chunks.iterdir()):
        raise ValueError("Output raw/chunks must be empty; choose a fresh output directory")
    manifest = {"chunk_paths": {item["chunk"].hex(): item["file"] for item in packages},
                "version": "ReplaceIoChunkHashWithIoHash", "mount_point": "../../../"}
    (raw / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf8")
    toc = output / (STEM + ".utoc")
    def run(*args):
        subprocess.run([str(retoc), *map(str, args)], check=True)
    run("pack-raw", raw, toc)
    container_id = toc.read_bytes()[56:64]
    tail = bytearray()
    stores = bytearray()
    for index, item in enumerate(packages):
        imports = item["imports"]
        stores += struct.pack("<iiii", len(imports), len(packages) * 16 + len(tail) - index * 16, 0, 0)
        tail += b"".join(struct.pack("<Q", value) for value in imports)
        (chunks / item["chunk"].hex()).write_bytes(item["data"])
    store = stores + tail
    container_header = (struct.pack("<II", 0x496f436e, 5) + container_id
                        + struct.pack("<I", len(packages))
                        + b"".join(item["chunk"][:8] for item in packages)
                        + struct.pack("<I", len(store)) + store
                        + struct.pack("<IIIIIqq", 0, 0, 0, 0, 0, 0, 0))
    (chunks / (container_id + bytes([0, 0, 0, 6])).hex()).write_bytes(container_header)
    run("pack-raw", raw, toc)
    shutil.copyfile(ROOT / "assets/perfect-deflect-no-rapid-click-penalty/companion.pak", toc.with_suffix(".pak"))
    toc_data = toc.read_bytes()
    entries, blocks = struct.unpack_from("<II", toc_data, 24)
    seeds = struct.unpack_from("<I", toc_data, 84)[0]
    overflow = struct.unpack_from("<I", toc_data, 96)[0]
    block_base = 144 + entries * 22 + seeds * 4 + overflow * 4
    read_end = max(int.from_bytes(toc_data[block_base + index * 12:block_base + index * 12 + 5], "little")
                   + ((int.from_bytes(toc_data[block_base + index * 12 + 5:block_base + index * 12 + 8], "little") + 15) // 16) * 16
                   for index in range(blocks))
    with toc.with_suffix(".ucas").open("ab") as stream:
        stream.write(bytes(max(0, read_end - stream.tell())))
    run("verify", toc)
    return toc


def compile_config(config, archive, symbols, output, retoc):
    if not config.get("contexts"):
        raise ValueError("Configuration requires contexts")
    source_header = archive.resource(next(index for index in range(archive.entries) if archive.toc[144 + index * 12 + 11] == 6))
    packages, report = [], []
    identities = set()
    for spec in config["contexts"]:
        path = spec["context"]
        if path.casefold() in identities:
            raise ValueError("Duplicate context: " + path)
        identities.add(path.casefold())
        entry = archive.packages[path.casefold()]
        source = archive.resource(entry)
        context = ZenContext(source, archive, symbols, path)
        context.configure(spec)
        modified = context.build()
        # Independent reader from the existing game resource extractor.
        decoded = ContextPackage(modified, path).context(path, archive.input_action_settings)
        if decoded["mappingCount"] != len(context.mappings):
            raise ValueError("Compiled mapping count differs from configuration")
        chunks = archive.toc[144 + entry * 12:156 + entry * 12]
        imports = source_imports(archive, source_header, entry) + context.added_packages
        if len(imports) != len(context.import_names):
            raise ValueError("Package-store imports differ from imported package names")
        packages.append({"chunk": chunks, "data": modified, "imports": imports,
                         "file": next(name for name, index in archive.files.items() if index == entry)})
        report.append({"context": path, "mappingCount": decoded["mappingCount"], "addedExports": len(context.new_export_indices),
                       "addedPackageImports": len(context.added_packages), "bindings": decoded["mappings"]})
    output.mkdir(parents=True, exist_ok=True)
    toc = pack(output, retoc, packages)
    (output / "resources-report.json").write_text(json.dumps({"container": str(toc), "runtimeVerified": False,
            "contexts": report}, ensure_ascii=False, indent=2), encoding="utf8")
    return toc, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=pathlib.Path)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--retoc", type=pathlib.Path, required=True)
    parser.add_argument("--build", choices=e.KEY_PART_OFFSETS, default="CL-302256")
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "dist/input-router/resources")
    args = parser.parse_args()
    archive = e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        toc, report = compile_config(json.loads(args.config.read_text(encoding="utf-8-sig")), archive,
                                     Symbols(args.paks), args.output.resolve(), args.retoc.resolve())
        print(json.dumps({"container": str(toc), "contexts": len(report), "mappings": sum(item["mappingCount"] for item in report),
                          "runtimeVerified": False}, indent=2))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
