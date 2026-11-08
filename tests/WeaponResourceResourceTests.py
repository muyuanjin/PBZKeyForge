"""Check native wind/thunder dispatch and every cooked wind/thunder condition."""
import argparse
import bisect
from collections import Counter
import mmap
from pathlib import Path
import re
import struct

from CompleteMoveSignatureResourceTests import NativeFunctions, combat_art


class NativeCode(NativeFunctions):
    def __init__(self, binary):
        super().__init__(binary)
        pe = struct.unpack_from("<I", binary, 60)[0]
        rva, size = struct.unpack_from("<II", binary, pe + 24 + 112 + 24)
        offset = self.offset(self.base + rva)
        self.extents = [struct.unpack_from("<III", binary, offset + i) for i in range(0, size, 12)]
        self.starts = [entry[0] for entry in self.extents]

    def code(self, address):
        entry = self.extents[bisect.bisect_right(self.starts, address - self.base) - 1]
        assert self.base + entry[0] == address, "Native dispatch must start at a complete unwind extent"
        offset = self.offset(address)
        return self.binary[offset:offset + entry[1] - entry[0]]

    def bindings(self, name):
        position, addresses = 0, set()
        while True:
            position = self.binary.find(name.encode() + b"\0", position)
            if position < 0:
                return addresses
            needle, reference = struct.pack("<Q", self.address(position)), 0
            position += 1
            while True:
                reference = self.binary.find(needle, reference)
                if reference < 0:
                    break
                at, reference = reference, reference + 1
                if at % 8:
                    continue
                address = struct.unpack_from("<Q", self.binary, at + 8)[0]
                if address - self.base not in self.starts:
                    continue
                addresses.add(address)

    def relative(self, address, displacement_offset):
        offset = self.offset(address) + displacement_offset
        return address + displacement_offset + 4 + struct.unpack_from("<i", self.binary, offset)[0]


def native_checks(binary):
    native = NativeCode(binary)
    getters = []
    for address in native.bindings("GetAbility"):
        code = native.code(address)
        matches = list(re.finditer(b"\xe8....\x48\x89\x03", code, re.DOTALL))
        if len(matches) == 1:
            getters.append(native.relative(address, matches[0].start() + 1))
    assert len(getters) == 1, "LGAAction.GetAbility must have one native owner getter"
    executions = []
    pattern = b"\x4c\x8d\x44\x24\x38\x48\x8b\xce\xe8....\x48\x8b\x4c\x24\x30"
    for address in native.bindings("ExecuteAction"):
        matches = list(re.finditer(re.escape(pattern[:9]) + b"...." + re.escape(pattern[13:]), native.code(address), re.DOTALL))
        if len(matches) == 1:
            executions.append(native.relative(address, matches[0].start() + 9))
    assert len(executions) == 1, "LegoAbility.ExecuteAction must have one qualified action dispatcher"
    matches = list(re.finditer(b"\x48\x8b\x02\x48\x8b\xca\xff\x90....\x84\xc0", native.code(executions[0]), re.DOTALL))
    assert len(matches) == 1
    slot = struct.unpack_from("<I", matches[0].group(), 8)[0]
    assert slot == 0x318, "The supported action qualification virtual slot changed"
    assert [("ReturnValue", 0x16)] in native.signatures("GetAllTimelineNames", include_return=True)
    # This class's shipped vtable is followed by its native class name. Its last
    # virtual is BulletEventBase.Trigger; only this concrete event is suppressed.
    name = b"\0\0" + "BulletEvent_FengLeiConsumeEnergy".encode("utf-16le") + b"\0\0"
    at = binary.find(name)
    assert at >= 0 and binary.find(name, at + 1) < 0
    at += 2
    trigger = struct.unpack_from("<Q", binary, at - 16)[0]
    start = native.offset(trigger)
    end = binary.find(b"\xc3\xcc", start)
    body = binary[start:end + 1]
    calls = [native.relative(trigger, item.start() + 1) for item in re.finditer(b"\xe8....", body, re.DOTALL)]
    assert len(calls) == 3 and calls[1] == calls[2], "The resource event must only resolve the passive and consume two charge objects"
    assert b"\x80\x79\x28\x00" in body and b"\x80\x79\x29\x00" in body
    assert b"\x48\x8b\x88\xb8\x03\x00\x00" in body and b"\x48\x8b\x8b\xc0\x03\x00\x00" in body


def cooked_checks(args):
    combat = combat_art
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    branches = Counter()
    actions = 0
    try:
        symbols = combat.ScriptObjects(args.paks)
        for key, entry in archive.packages.items():
            if "/abilities/baishanfengleijian/" not in key:
                continue
            package = combat.e.Package(archive.resource(entry))
            assert not any("GetThunderEnergyValue" in value or "GetWindEnergyValue" in value for value in package.names), key
            resource_names = {index: name.rsplit(".", 1)[-1] for index, name in enumerate(package.names)
                              if name in ("CharacterState.AbilityState.BaiShanFengLeiJian.Wind",
                                          "CharacterState.AbilityState.BaiShanFengLeiJian.Thunder")}
            start = struct.unpack_from("<I", package.data, 32)[0]
            for index, (name, payload) in enumerate(package.exports):
                identity = struct.unpack_from("<Q", package.data, start + index * 72 + 32)[0]
                kind = symbols.resolve(package, identity)
                if not kind.startswith("/") or not any(part in kind for part in ("/Actions/", "/Action/")):
                    continue
                actions += 1
                occurrences = []
                for position in range(len(payload) - 7):
                    tag, number = struct.unpack_from("<II", payload, position)
                    if tag in resource_names and number == 0:
                        occurrences.append((position, resource_names[tag]))
                if not occurrences:
                    continue
                # FGameplayTagRequirements is the final inherited action field.
                # Both positive and negative resource branches serialize exactly
                # one requirement field; a TagQuery requires a different header.
                requirements = []
                for begin in range(len(payload) - 17):
                    if payload[begin:begin + 2] not in (b"\0\3", b"\1\3"):
                        continue
                    count = struct.unpack_from("<I", payload, begin + 2)[0]
                    if begin + 6 + count * 8 + 4 != len(payload) or payload[-4:] != b"\0\0\0\0":
                        continue
                    reader = combat.e.Reader(payload[begin:], package.names)
                    header = reader.header()
                    reader.number()
                    names = [reader.name() for _ in range(count)]
                    requirements.append((begin, next(iter(header)), names))
                if not requirements:
                    # This sound action's SoundTag itself selects the energy
                    # sound; it has no serialized ActivationRequirements.
                    assert kind == "/SGameCombatSystem/Weapon/Action/GAA_PlaySound_Opt"
                    assert combat.e.Reader(payload, package.names).header() == {3: False, 18: False, 22: False}
                    assert len(payload) == 19 and occurrences[0][0] == 11 and len(occurrences) == 1, (key, name)
                    continue
                assert len(requirements) == 1, (key, name)
                begin, field, names = requirements[0]
                assert all(position >= begin + 6 for position, _ in occurrences), (key, name)
                for value in names:
                    if value.rsplit(".", 1)[-1] in ("Wind", "Thunder"):
                        branches[(field, value.rsplit(".", 1)[-1])] += 1
        assert actions > 0 and all(branches[(field, tag)] for field in (0, 1) for tag in ("Wind", "Thunder")), branches
    finally:
        archive.close()
    return actions, branches


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    with args.game_exe.open("rb") as file, mmap.mmap(file.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native_checks(binary)
    actions, branches = cooked_checks(args)
    print("WeaponResourceResourceTests PASS: native owner/action/cost dispatch;", actions, "cooked actions;", dict(branches))


if __name__ == "__main__":
    main()
