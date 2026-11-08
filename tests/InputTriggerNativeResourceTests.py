"""Check native trigger evaluation dispatch and observation ABI in the shipped executable."""
import argparse
import importlib.util
import json
import mmap
from pathlib import Path
import struct

from capstone import Cs, CS_ARCH_X86, CS_MODE_64

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("input_signatures", ROOT / "tests/CompleteMoveSignatureResourceTests.py")
signatures = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(signatures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=Path, required=True)
    args = parser.parse_args()
    with args.game_exe.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native = signatures.NativeFunctions(binary)
        expected = {
            "UpdateState": [("PlayerInput", 0x12), ("ModifiedValue", 0x19), ("DeltaTime", 0xa), ("ReturnValue", 0x1e)],
            "GetTriggerType": [("ReturnValue", 0x1e)],
            "GetFrameCount": [("ReturnValue", 0x4)],
            "GetInputVectorKeyState": [("Key", 0x19), ("ReturnValue", 0x19)],
        }
        for name, fields in expected.items():
            assert native.signatures(name, True) == [fields], name
        _, _, code_va, code_size, code_raw = native.sections[0]
        code_start, code_end = native.base + code_va, native.base + code_va + code_size
        disassembler = Cs(CS_ARCH_X86, CS_MODE_64)

        def reflected_exec(name, slot):
            matches, position = set(), 0
            while True:
                position = binary.find(name.encode() + b"\0", position)
                if position < 0:
                    break
                pointer, position = struct.pack("<Q", native.address(position)), position + 1
                reference = 0
                while True:
                    reference = binary.find(pointer, reference)
                    if reference < 0:
                        break
                    candidate = struct.unpack_from("<Q", binary, reference + 8)[0]
                    reference += 1
                    if not code_start <= candidate < code_end:
                        continue
                    offset = native.offset(candidate)
                    for instruction in disassembler.disasm(binary[offset:offset + 512], candidate):
                        if instruction.mnemonic == "call" and instruction.op_str == "qword ptr [rax + " + hex(slot) + "]":
                            matches.add(candidate)
                        if instruction.mnemonic == "ret":
                            break
            assert len(matches) == 1, (name, matches)
            return matches.pop()

        update = reflected_exec("UpdateState", 0x2b8)
        trigger_type = reflected_exec("GetTriggerType", 0x2b0)
        code = binary[code_raw:code_raw + code_size]
        update_call = b"\x41\xff\x91" + struct.pack("<I", 0x2b8)
        type_call = b"\x41\xff\x91" + struct.pack("<I", 0x2b0)
        blocking_call = b"\xff\x90" + struct.pack("<I", 0x2c8)
        positions, position = [], 0
        while True:
            position = code.find(update_call, position)
            if position < 0:
                break
            before, after = code[max(0, position - 100):position], code[position:position + 200]
            # Native BlueprintNativeEvent implementations bypass ProcessEvent;
            # Blueprint overrides use the other branch in the same evaluator.
            if (b"\xf6\x80\x24\x01\x00\x00\x80" in before
                    and b"\x41\xff\x91\x60\x02\x00\x00" in before
                    and type_call in after and blocking_call in after):
                positions.append(native.base + code_va + position)
            position += 1
        assert len(positions) == 1, positions
        assert binary.find("PBZInputTriggerDefaultTouchpadSwipe\0".encode("utf-16le")) >= 0
    combat = signatures.combat_art
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    symbols = combat.ScriptObjects(args.paks)
    defaults = json.loads((ROOT / "assets/input-defaults-302256.json").read_text(encoding="utf-8-sig"))
    contexts = {source["context"] for group in ("bindings", "fixedMappings")
                for row in defaults[group] for source in row["sources"]}
    classes, trigger_count = set(), 0
    try:
        for path in sorted(contexts):
            prefix, _, relative = path.partition("/Content/")
            mount = "Game" if prefix == "ProjectPBZ" else prefix.rsplit("/", 1)[-1]
            identity = ("/" + mount + "/" + relative[:-7]).casefold()
            package = combat.e.Package(archive.resource(archive.packages[identity]))
            start = struct.unpack_from("<I", package.data, 32)[0]
            for index, (name, _) in enumerate(package.exports):
                if "Trigger" not in name:
                    continue
                trigger_class = symbols.resolve(package, struct.unpack_from("<Q", package.data, start + index * 72 + 32)[0])
                assert trigger_class.startswith("/Script/"), (path, name, trigger_class)
                classes.add(trigger_class)
                trigger_count += 1
        assert "/Script/ProjectPBZ.PBZInputTriggerDefaultTouchpadSwipe" in classes
    finally:
        archive.close()
    print("InputTriggerNativeResourceTests PASS: 4 signatures; UpdateState exec", hex(update),
          "; GetTriggerType exec", hex(trigger_type), "; native evaluator", hex(positions[0]),
          ";", len(contexts), "contexts;", trigger_count, "native trigger objects;", len(classes), "native classes")


if __name__ == "__main__":
    main()
