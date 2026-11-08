"""Verify the native interfaces used for temporary runtime weapon preparation."""
import argparse
import json
import mmap
import pathlib
import struct

from CompleteMoveSignatureResourceTests import NativeFunctions

ROOT = pathlib.Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    args = parser.parse_args()
    expected = {
        "SetSlotWeapon": [("SlotTag", 0x19), ("Index", 0x3), ("Config", 0x13)],
        "SetArmed": [("SlotTag", 0x19), ("Armed", 0x4c)],
        "IsArmed": [("SlotTag", 0x19), ("ReturnValue", 0x4c)],
        "GetSlotWeapon": [("SlotTag", 0x19), ("Index", 0x3), ("WeaponData", 0x19), ("ReturnValue", 0x4c)],
        "GetCurrentWeapon": [("SlotTag", 0x19), ("WeaponData", 0x19), ("WithPending", 0x4c), ("CurrentIndex", 0x3)],
        "GetCurrentWeaponIndex": [("SlotTag", 0x19), ("WithPending", 0x4c), ("ReturnValue", 0x3)],
        "SetCurrentWeapon": [("SlotTag", 0x19), ("Index", 0x3), ("ReturnValue", 0x4c)],
        "SetWeaponRemoveOnUnequip": [("SlotTag", 0x19), ("SlotIndex", 0x3), ("Remove", 0x4c)],
        "Conv_InterfaceToObject": [("Interface", 0x13), ("ReturnValue", 0x12)],
    }
    evidence = json.loads((ROOT / "docs/weapon-equipment-preparation.json").read_text(encoding="utf8"))
    with args.game_exe.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native = NativeFunctions(binary)
        for name, parameters in expected.items():
            assert native.signatures(name, include_return=True) == [parameters], name
        # GetSlotWeapon's native StructProperty references this exact 0x120-byte
        # RuntimeWeaponSingleData schema. Inner array records are excluded.
        properties = []
        table = 0x14bd48438
        for index in (0, 2, 4):
            pointer = struct.unpack_from("<Q", binary, native.offset(table) + index * 8)[0]
            at = native.offset(pointer)
            name_pointer = struct.unpack_from("<Q", binary, at)[0]
            kind = struct.unpack_from("<I", binary, at + 24)[0]
            offset = struct.unpack_from("<H", binary, at + 50)[0]
            properties.append((native.name(name_pointer), kind, offset))
        assert properties == [("WeaponConfig", 0x13, 0), ("WeaponActors", 0x16, 16), ("Scabbards", 0x16, 32)]
        assert [(x["name"], x["offset"]) for x in evidence["runtimeWeaponData"]["properties"]] == [(name, offset) for name, _, offset in properties]
        assert evidence["runtimeWeaponData"]["size"] == 0x120
        interface = native.offset(0x14dc30608)
        getter, pointer_offset, implemented_by_k2 = struct.unpack_from("<QIB", binary, interface)
        assert (getter, pointer_offset, implemented_by_k2) == (0x145e4ccd0, 0x30, 0)
        assert evidence["configurationInterface"]["implementedInterface"]["pointerOffset"] == pointer_offset
    print("PASS: native runtime-slot interfaces, out parameters and original-config restoration schema")


if __name__ == "__main__":
    main()
