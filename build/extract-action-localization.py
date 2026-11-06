"""Refresh action labels from the installed game's native Game.locres files.

The JSON keeps the audited action-to-text relationships and their provenance.
This command reads the 15 native language resources and refreshes their labels.
Archive keys remain in memory. The game does not need to be stopped.
"""
import argparse
import ctypes
import importlib.util
import json
import pathlib
import re
import struct


def load_reader():
    path = pathlib.Path(__file__).with_name("extract-input-defaults.py")
    spec = importlib.util.spec_from_file_location("input_defaults", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class Pak:
    def __init__(self, archive, reader):
        self.archive = archive
        self.Reader = reader
        self.stream = (archive.paks / "ProjectPBZ-Windows.pak").open("rb")
        self.stream.seek(-256, 2)
        footer = self.stream.read()
        position = footer.index(bytes.fromhex("e1126f5a"))
        version, offset, size = struct.unpack_from("<IQQ", footer, position + 4)
        if version != 12:
            raise ValueError("Unsupported PAK version")
        self.stream.seek(offset)
        data = reader(archive.cipher.decrypt(self.stream.read(size)))
        mount = data.string()
        data.number()
        data.number("<Q")
        data.number()
        data.read(36)
        data.number()
        directory_offset = data.number("<Q")
        directory_size = data.number("<Q")
        data.read(20)
        self.encoded = data.read(data.number())
        self.stream.seek(directory_offset)
        directory = reader(archive.cipher.decrypt(self.stream.read(directory_size)))
        directory.string = lambda: directory.read(directory.number()).rstrip(b"\0").decode("utf-8")
        self.files = {}
        for _ in range(directory.number()):
            dirname = directory.string()
            for _ in range(directory.number()):
                filename = directory.string()
                self.files[mount + dirname + filename] = directory.number()

    def read(self, entry):
        encoded = self.encoded
        bits = struct.unpack_from("<I", encoded, entry)[0]
        position = entry + 4 + (4 if bits & 63 == 63 else 0)
        offset = struct.unpack_from("<I" if bits >> 31 & 1 else "<Q", encoded, position)[0]
        self.stream.seek(offset)
        record = self.Reader(self.stream.read(48))
        record.read(8)
        compressed = record.number("<Q")
        size = record.number("<Q")
        method = record.number()
        record.read(20)
        blocks = []
        if method:
            count = struct.unpack("<I", self.stream.read(4))[0]
            blocks = [struct.unpack("<QQ", self.stream.read(16)) for _ in range(count)]
        flags = self.stream.read(1)[0]
        block_size = struct.unpack("<I", self.stream.read(4))[0]
        if not method:
            raw = self.stream.read((compressed + 15) // 16 * 16 if flags & 1 else compressed)
            return self.archive.cipher.decrypt(raw)[:compressed] if flags & 1 else raw
        result = []
        for index, (start, end) in enumerate(blocks):
            self.stream.seek(offset + start)
            raw = self.stream.read((end - start + 15) // 16 * 16)
            data = self.archive.cipher.decrypt(raw)[:end - start] if flags & 1 else raw[:end - start]
            expected = min(block_size, size - index * block_size)
            destination = ctypes.create_string_buffer(expected)
            source = ctypes.create_string_buffer(data)
            written = self.archive.decompress(source, len(data), destination, expected,
                                              1, 0, 0, None, 0, None, None, None, 0, 3)
            if written != expected:
                raise ValueError("PAK localization decompression failed")
            result.append(destination.raw)
        return b"".join(result)


def parse_locres(raw, reader):
    data = reader(raw)
    data.read(16)
    if data.number("<B") != 3:
        raise ValueError("Unsupported localization resource version")
    string_offset = data.number("<q")
    expected_count = data.number("<I")
    rows = []
    for _ in range(data.number("<I")):
        data.number("<I")
        namespace = data.string()
        for _ in range(data.number("<I")):
            data.number("<I")
            key = data.string()
            data.number("<I")
            rows.append((namespace, key, data.number()))
    if data.position != string_offset or len(rows) != expected_count:
        raise ValueError("Localization resource entry table is incomplete")
    strings = []
    for _ in range(data.number()):
        strings.append(data.string())
        data.number()
    if data.position != len(raw):
        raise ValueError("Localization resource has unread data")
    return {(namespace, key): strings[index] for namespace, key, index in rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", required=True, type=pathlib.Path)
    parser.add_argument("--paks", required=True, type=pathlib.Path)
    parser.add_argument("--oodle", required=True, type=pathlib.Path)
    parser.add_argument("--build", default="CL-302256")
    parser.add_argument("--output", type=pathlib.Path,
                        default=pathlib.Path("assets/key-enhancement/action-localization.json"))
    args = parser.parse_args()
    engine = load_reader()
    document = json.loads(args.output.read_text(encoding="utf-8"))
    if document["build"] != args.build:
        raise ValueError("The action relationships belong to a different game build")
    archive = engine.Archive(args.game_exe, args.paks, args.oodle, args.build)
    pak = Pak(archive, engine.Reader)
    try:
        resources = {}
        for path, entry in pak.files.items():
            if "/ProjectPBZ/Content/Localization/Game/" in path and path.endswith("/Game.locres"):
                resources[path.split("/")[-2]] = parse_locres(pak.read(entry), engine.Reader)
        for entry in document["entries"].values():
            if entry["source"] == "unresolved":
                continue
            references = [ref for ref in entry["evidence"] if "key" in ref]
            entry["labels"] = {
                language: " · ".join(resources["en-US" if language == "en" else language]
                                      [(ref["namespace"], ref["key"])] for ref in references)
                for language in document["languages"]
            }
            if entry.get("labelPart") == "after-colon":
                entry["labels"] = {language: re.sub(r"^[^:：]*[:：]\s*", "", label)
                                   for language, label in entry["labels"].items()}
        args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        named = sum(bool(entry["labels"]) for entry in document["entries"].values())
        print("Native labels: {0}/{1}; languages: {2}".format(named, len(document["entries"]), len(document["languages"])))
    finally:
        pak.stream.close()
        archive.close()


if __name__ == "__main__":
    main()
