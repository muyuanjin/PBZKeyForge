"""Integration assertions against installed game input resources and built IOStore."""
import argparse
import importlib.util
import json
import pathlib
import shutil
import struct
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("resource_router", ROOT / "prototype/input-router/resources.py")
r = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(r)


def check_layout(blob, symbols):
    package = r.e.Package(blob)
    count = struct.unpack_from("<I", blob, 60)[0]
    for index, name in enumerate(package.names):
        name_header = int.from_bytes(blob[76 + count * 8 + index * 2:78 + count * 8 + index * 2], "big")
        assert not name.isascii() or name_header & 0x8000 == 0, "ASCII FNames must use the narrow representation"
    _, size, _, _, _, _, hashes, imports, exports, bundles, headers, entries, names, cells, cell_exports = struct.unpack_from("<15I", blob)
    assert cells == cell_exports == bundles
    assert exports < bundles <= headers <= entries <= names < size < len(blob)
    assert (bundles - exports) % 72 == 0
    count = (bundles - exports) // 72
    assert count == len(package.exports)
    assert entries - headers == count * 20
    commands = list(struct.iter_unpack("<II", blob[bundles:headers]))
    assert sorted(commands) == sorted((index, command) for index in range(count) for command in (0, 1))
    positions = {item: index for index, item in enumerate(commands)}
    for index, (name, payload) in enumerate(package.exports):
        offset, length = struct.unpack_from("<QQ", blob, exports + index * 72)
        assert payload == blob[size + offset:size + offset + length]
        assert positions[(index, 0)] < positions[(index, 1)]
        if index:
            assert positions[(0, 0)] < positions[(index, 0)]
        first, *counts = struct.unpack_from("<5I", blob, headers + index * 20)
        assert entries + (first + sum(counts)) * 4 <= names
        deps = struct.unpack_from("<" + "i" * sum(counts), blob, entries + first * 4)
        for dependency in deps:
            assert dependency != 0
            assert dependency <= count if dependency > 0 else -dependency <= (exports - imports) // 8
    return package


def run(args):
    archive = r.e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    symbols = r.Symbols(args.paks)
    jump = "/PBZInput/InputActions/IA_Jump"
    dodge = "/PBZDeflectSystem/Input/IA_Dodge"
    path = "/PBZInput/IMC_Default"
    try:
        source = archive.resource(archive.packages[path.casefold()])
        config = {"contexts": [{"context": path, "mode": "replace", "bindings": [
            {"action": jump, "keys": ["F" + str(index) for index in range(1, 25)], "trigger": "pressed"},
            {"action": dodge, "key": "Gamepad_FaceButton_Bottom", "trigger": {"type": "pressed", "chord": ["Gamepad_LeftShoulder"]}},
            {"action": jump, "key": "G", "trigger": {"type": "hold", "seconds": 0.75, "once": True}},
            {"action": jump, "key": "H", "trigger": "released"},
            {"action": jump, "key": "J", "trigger": "down"},
            {"action": jump, "key": "K", "trigger": {"type": "tap", "seconds": 0.125}}
        ]}]}
        context = r.ZenContext(source, archive, symbols, path)
        context.configure(config["contexts"][0])
        blob = context.build()
        package = check_layout(blob, symbols)
        decoded = r.ContextPackage(blob, path).context(path, archive.input_action_settings)
        jump_mappings = [mapping for mapping in decoded["mappings"] if mapping["inputAction"] == jump]
        assert len(jump_mappings) == 28
        assert all(mapping["action"] is None and "rawSlot" not in mapping and mapping["settingsBehavior"] == 2 for mapping in decoded["mappings"])
        assert {"F" + str(index) for index in range(1, 25)} | {"G", "H", "J", "K"} == {mapping["key"] for mapping in jump_mappings}
        assert dodge.casefold() in [name.casefold() for name in package.imports]
        assert len(context.added_packages) == 1
        classes = [struct.unpack_from("<Q", blob, struct.unpack_from("<I", blob, 32)[0] + index * 72 + 32)[0] for index in range(len(package.exports))]
        assert classes.count(symbols.get("InputTriggerChordAction")) == 1
        chord_index = classes.index(symbols.get("InputTriggerChordAction"))
        chord_payload = package.exports[chord_index][1]
        reader = r.e.Reader(chord_payload, package.names)
        assert reader.header() == {0: False}
        helper_pointer = reader.number()
        assert classes[helper_pointer - 1] == symbols.get("InputAction")
        assert reader.read(4) == bytes(4) and reader.position == len(chord_payload)
        helper_mapping = next(mapping for mapping in decoded["mappings"] if ":PBZKeyForge_InputRouter_" in mapping["inputAction"])
        assert helper_mapping["key"] == "Gamepad_LeftShoulder"
        hold_index = next(index for index in context.new_export_indices if classes[index] == symbols.get("InputTriggerHold"))
        reader = r.e.Reader(package.exports[hold_index][1], package.names)
        assert reader.header() == {0: False, 1: False}
        assert reader.number("<f") == 0.75 and reader.read(1) == b"\x01"
        tap_index = next(index for index in context.new_export_indices if classes[index] == symbols.get("InputTriggerTap"))
        reader = r.e.Reader(package.exports[tap_index][1], package.names)
        assert reader.header() == {0: False}
        assert reader.number("<f") == 0.125
        assert reader.read(4) == bytes(4) and reader.position == len(reader.data)
        append = r.ZenContext(source, archive, symbols, path)
        append.configure({"bindings": [{"action": jump, "key": "F8"}], "remove_actions": ["/PBZDeflectSystem/Input/IA_PerfectDodge"]})
        append_decoded = r.ContextPackage(append.build(), path).context(path, archive.input_action_settings)
        assert all(mapping["inputAction"] != "/PBZDeflectSystem/Input/IA_PerfectDodge" for mapping in append_decoded["mappings"])
        original = r.e.Package(source).context(path, archive.input_action_settings)
        original_other = [mapping for mapping in original["mappings"] if mapping["inputAction"] != "/PBZDeflectSystem/Input/IA_PerfectDodge"]
        assert len(append_decoded["mappings"]) == len(original_other) + 1
        for before, after in zip(original_other, append_decoded["mappings"]):
            for field in ("inputAction", "key", "triggers", "modifiers", "settingsBehavior", "action"):
                assert before.get(field) == after.get(field)
        with tempfile.TemporaryDirectory(prefix="input-router-resource-tests-", dir=args.output) as temporary:
            folder = pathlib.Path(temporary)
            toc, report = r.compile_config(config, archive, symbols, folder, args.retoc)
            retained = args.output / "compiled"
            retained.mkdir(exist_ok=True)
            for suffix in (".pak", ".utoc", ".ucas"):
                shutil.copyfile(toc.with_suffix(suffix), retained / toc.with_suffix(suffix).name)
            manifest = json.loads((folder / "raw/manifest.json").read_text(encoding="utf8"))
            assert len(manifest["chunk_paths"]) == 1
            if args.container_audit:
                audit = folder / "audit"
                subprocess.run([str(args.container_audit), "--container", str(toc), str(audit)], check=True)
                extracted = list(audit.glob("*.uasset"))
                assert len(extracted) == 1 and extracted[0].read_bytes() == blob
            saved = args.output / "resource-test-result.json"
            saved.write_text(json.dumps({"mappingCount": decoded["mappingCount"], "jumpBindings": len(jump_mappings),
                    "newExports": len(context.new_export_indices), "externalActionImports": len(context.added_packages),
                    "retocVerified": True, "independentContainerRead": bool(args.container_audit), "runtimeVerified": False}, indent=2), encoding="utf8")
        print("PASS: 28 fixed jump bindings, physical chord, external action import, hold/tap/released/down exports, removal, container")
    finally:
        archive.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--retoc", type=pathlib.Path, required=True)
    parser.add_argument("--build", choices=r.e.KEY_PART_OFFSETS, default="CL-302256")
    parser.add_argument("--container-audit", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "dist/input-router/resource-tests")
    arguments = parser.parse_args()
    arguments.output.mkdir(parents=True, exist_ok=True)
    run(arguments)
