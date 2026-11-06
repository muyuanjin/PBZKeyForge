"""Verify stance-dash migration against installed resources without packing or installing."""
import argparse
import json
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "prototype/input-router"))
import special_dash as s


def root_export(archive, path):
    return archive.resource(archive.packages[path.casefold()])


def document(blob):
    package = s.e.Package(blob)
    return s.BindingDocument(package.exports[0][1], package.names)


def verify(args):
    archive = s.e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        if args.catalog:
            assets = json.loads(args.catalog.read_text(encoding="utf-8-sig"))["inputAssets"]
        else:
            assets = json.loads((ROOT / "dist/binding-recheck/resources/asset-registry-input.json")
                                .read_text(encoding="utf-8-sig"))["inputAssets"]
        binding_paths = [asset["package"] for asset in assets if asset["assetClass"] == "InputActionBinding"]
        assert binding_paths, "InputActionBinding class catalog is empty"
        assert len(binding_paths) == len(set(binding_paths)), "InputActionBinding catalog repeats a package"
        if args.build == "CL-302256":
            assert len(binding_paths) == 114, "CL-302256 requires the full 114-package class catalog"
        trigger_rows, direct_abilities = 0, []
        for path in binding_paths:
            blob = root_export(archive, path)
            package = s.e.Package(blob)
            parsed = s.BindingDocument(package.exports[0][1], package.names)
            assert parsed.encode() == package.exports[0][1], "Roundtrip changed " + path
            for item in parsed.items:
                for trigger in s.triggers(item):
                    trigger_rows += 1
                    if trigger.get("DebugAbility"):
                        direct_abilities.append({"asset": path, "tag": trigger.get("TriggerTag")})
        assert not direct_abilities, "Native trigger suppression also requires direct ability handling: " + str(direct_abilities)
        sources = [root_export(archive, path) for path in (s.DASH, s.JUMP)]
        original_dash, original_jump = [document(blob) for blob in sources]
        migrated = [item for item in original_dash.items
                    if any(t.get("TriggerTag") == s.SPECIAL for t in s.triggers(item))]
        assert len(migrated) == 1
        expected_routes = list(s.triggers(migrated[0]))
        assert len(expected_routes) == 11, "Current source stance dash group differs from CL-302256"
        changed, report = s.move_special_dash(*sources)
        dash, jump = [document(blob) for blob in changed]
        old_remaining = [item for item in original_dash.items if item not in migrated]
        assert s.clean(dash.items) == s.clean(old_remaining)
        assert s.clean(jump.items[1:]) == s.clean(original_jump.items)
        assert len(dash.items) == len(original_dash.items) - 1
        assert len(jump.items) == len(original_jump.items) + 1
        assert not any(t.get("TriggerTag") in (s.SPECIAL, s.DEFLECT)
                       for item in dash.items for t in s.triggers(item))
        new_routes = list(s.triggers(jump.items[0]))
        assert len(new_routes) == 11
        assert sum(t["TriggerTag"] == s.SPECIAL for t in new_routes) == 10
        assert sum(t["TriggerTag"] == s.DEFLECT for t in new_routes) == 1
        assert [t["TriggerTag"] for t in new_routes] == [t["TriggerTag"] for t in expected_routes]
        assert all(s.HOLDING in t["RequiredTags"] for t in new_routes)
        assert jump.items[0]["ActionPriority"] == migrated[0]["ActionPriority"] == -5
        for source, modified in zip(sources, changed):
            before, after = s.e.Package(source), s.e.Package(modified)
            assert after.names[:len(before.names)] == before.names
            assert after.imports == before.imports
            assert [name for name, _ in after.exports] == [name for name, _ in before.exports]
            begin = struct.unpack_from("<I", modified, 32)[0]
            header = struct.unpack_from("<I", modified, 4)[0]
            for index, (_, payload) in enumerate(after.exports):
                offset, size = struct.unpack_from("<QQ", modified, begin + index * 72)
                assert size == len(payload)
                assert modified[header + offset:header + offset + size] == payload
        jump_package = s.e.Package(changed[1])
        assert len(jump_package.names) > len(s.e.Package(sources[1]).names)

        # A legal numbered FName must retain its base identity across name-map growth.
        legal = document(sources[0])
        numbered = "Ability.Action.Hun.SpecialDash_7"
        legal.name_numbers[numbered] = (s.SPECIAL, 8)
        first = next(s.triggers(legal.items[1]))
        first["TriggerTag"] = numbered
        first["RequiredTags"].append("PBZKeyForge.InputRouter.AddedCondition")
        encoded = legal.encode()
        numbered_blob = s.resources.rebuild_exports(
            sources[0], {s.e.Package(sources[0]).exports[0][0]: encoded}, legal.names)
        legal_check = document(numbered_blob)
        assert next(s.triggers(legal_check.items[1]))["TriggerTag"] == numbered
        assert legal_check.name_numbers[numbered] == (s.SPECIAL, 8)
        assert legal_check.encode() == encoded

        # Unreal serializes empty binding arrays and zero tags through different headers.
        for zero in (b"\x00\x01", b"\x80\x03\x01", b"\x02\x01"):
            payload = zero + bytes(4)
            legal_zero = s.BindingDocument(payload, ["Example"])
            assert legal_zero.items == [] and legal_zero.encode() == payload
        value = document(sources[0])
        trigger = next(s.triggers(value.items[0]))
        trigger["TriggerTag"] = None
        value.tag_headers[None] = b"\x80\x03\x01"
        encoded = value.encode()
        parsed = s.BindingDocument(encoded, value.names)
        assert next(s.triggers(parsed.items[0]))["TriggerTag"] is None
        assert parsed.encode() == encoded
        result = {"build": args.build, "roundtripBindings": len(binding_paths), "triggerRows": trigger_rows,
                  "directAbilityRows": direct_abilities, "movedRoutes": len(new_routes),
                  "oldStanceDashRemoved": True, "newRoutesRequireGuard": True,
                  "unrelatedGroupsPreserved": True, "numberedFNameAndZeroMasks": True,
                  "runtimeVerified": False}
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print("PASS: " + str(len(binding_paths)) + " binding roundtrips; 11 migrated routes; preserved jump/dodge; FName numbers and zero masks")
    finally:
        archive.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    parser.add_argument("--build", choices=s.e.KEY_PART_OFFSETS, default="CL-302256")
    parser.add_argument("--catalog", type=pathlib.Path, help="AssetRegistry class catalog from the input resource audit")
    parser.add_argument("--output", type=pathlib.Path)
    verify(parser.parse_args())
