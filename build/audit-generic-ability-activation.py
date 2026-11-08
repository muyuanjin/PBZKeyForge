"""Audit direct input tags using retained full-class evidence and installed resources."""
import argparse
import importlib.util
import json
import mmap
from pathlib import Path
import struct
import sys

import capstone


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "prototype/input-router"))
import special_dash

SPEC = importlib.util.spec_from_file_location("native_signatures", ROOT / "tests/CompleteMoveSignatureResourceTests.py")
signatures = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(signatures)


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def extract(args):
    catalog = read(args.catalog)
    scan, redirects = read(args.class_scan), read(args.redirect_evidence)
    scan = scan.get("retainedClassScan", scan)
    if isinstance(redirects, dict):
        redirects = redirects["retainedRedirectEvidence"]
    routes = list({row["tag"]: row for row in catalog.get("unsupportedInputRoutes", []) + catalog["abilities"]}.values())
    tags = {row["tag"] for row in routes}
    universe = read(ROOT / "docs/ability-source-audit.json")
    assert scan["classes"] == sum(map(len, universe["abilityUniverse"].values())) == 5728
    assert set(scan["tags"]) == tags and len(tags) == 236
    excluded = sorted(tag for tag, classes in scan["tags"].items() if not classes and tag != "Ability.Native.Crouch")
    assert len(excluded) == 9
    assert all(not scan["allExportNameMatches"][tag] for tag in excluded)
    assert not any(tag in line for row in redirects for line in row["redirects"] for tag in excluded)
    archive = special_dash.e.Archive(args.game_exe, args.paks, args.oodle, catalog["build"])
    owner_paths = {row["equipmentId"] for row in read(ROOT / "docs/complete-move-weapon-audit.json")["equipment"]}
    owner_paths.update(row["equipmentId"] for row in read(ROOT / "docs/shadow-move-entries.json")["configurations"])
    owner_paths.add("/PBZCombatAsset/ActorData/Players/Hun/InputConfig_Hun")
    unsupported, owners = [], {}
    try:
        for path in sorted(owner_paths):
            package = special_dash.e.Package(archive.resource(archive.packages[path.casefold()]))
            import_end = struct.unpack_from("<I", package.data, 32)[0]
            names = {package.object_name(-i - 1) for i in range((import_end - package.import_offset) // 8)}
            for binding in names:
                owners.setdefault(binding, []).append(path)
        for tag in excluded:
            source = next(row for row in routes if row["tag"] == tag)
            found = []
            for path in sorted({item["asset"] for item in source["sources"]}):
                package = special_dash.e.Package(archive.resource(archive.packages[path.casefold()]))
                document = special_dash.BindingDocument(package.exports[0][1], package.names)
                assert owners.get(path), (tag, path, "Binding is not referenced by a current configuration")
                for group, item in enumerate(document.items):
                    blocks = [("normal", item.get("NormalData"))] + [
                        (pair["tag"], pair["value"]) for pair in (item.get("TagConditionData") or {}).get("pairs", [])]
                    for branch, data in blocks:
                        for kind in ("Actions", "Combos", "InterruptLower"):
                            for trigger in (data or {}).get(kind) or []:
                                if trigger.get("TriggerTag") == tag:
                                    assert not trigger.get("DebugAbility"), tag
                                    found.append({"asset": path, "owners": owners[path], "group": group,
                                                  "branch": branch, "kind": kind, "priority": item.get("ActionPriority"),
                                                  "trigger": special_dash.clean(trigger)})
            assert found, tag
            unsupported.append({"tag": tag, "inputRoutes": found, "abilityClassReferences": [],
                                "nativeLiteralFound": False, "redirectFound": False,
                                "disposition": "Input route retained as evidence; unavailable as a direct ability target"})
    finally:
        archive.close()
    with args.game_exe.open("rb") as file, mmap.mmap(file.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native = signatures.NativeFunctions(binary)
        for tag in excluded:
            assert binary.find(tag.encode()) < 0 and binary.find(tag.encode("utf-16le")) < 0, tag
        assert binary.find(b"Ability.Native.Crouch") >= 0 or binary.find("Ability.Native.Crouch".encode("utf-16le")) >= 0
        decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        ranges = {"inputActivationThunk": (0x145e2f7b0, 0x145e2f932),
                  "matchingAbilityActivation": (0x145e29530, 0x145e29789),
                  "grantedSpecTagSelection": (0x144c902b0, 0x144c903d2),
                  "abilityTagsGetter": (0x141ac1f00, 0x141ac1f08)}
        instructions = {}
        for name, (begin, end) in ranges.items():
            decoded = list(decoder.disasm(binary[native.offset(begin):native.offset(end)], begin))
            assert decoded and decoded[-1].address + decoded[-1].size == end
            instructions[name] = [{"address": hex(item.address), "instruction": item.mnemonic + " " + item.op_str}
                                  for item in decoded]
        calls = lambda name: {row["instruction"] for row in instructions[name]}
        assert "call 0x145e29530" in calls("inputActivationThunk")
        assert "call 0x144c902b0" in calls("matchingAbilityActivation")
        assert "call 0x144ca8430" in calls("matchingAbilityActivation")
        assert "mov rbx, qword ptr [rbp + 0x520]" in calls("grantedSpecTagSelection")
        assert "call 0x141ac1f00" in calls("grantedSpecTagSelection")
        assert "lea rax, [rcx + 0xa8]" in calls("abilityTagsGetter")
        record = native.offset(0x14b684e20)
        assert native.name(struct.unpack_from("<Q", binary, record)[0]) == "AbilityTags"
        assert struct.unpack_from("<I", binary, record + 24)[0] == 0x19
        assert struct.unpack_from("<H", binary, record + 48)[0] == 1
        assert struct.unpack_from("<H", binary, record + 50)[0] == 0xa8
    return {"build": catalog["build"], "runtimeVerified": False,
            "evidenceType": "Retained complete registry CDO/name scan, current cooked input routes and native activation instructions",
            "coverage": {"registryAbilityClasses": 5728, "inputRouteTags": 236, "directAbilityTags": 227, "unsupportedInputRouteTags": 9},
            "nativeSelection": {"abilityTagsOffset": "0xa8", "uhtPropertyRecord": "0x14b684e20",
                "behavior": "The external input method selects already granted ability specs by their actual ability/primary-instance AbilityTags, then tries the matching handle. It does not create an ability from an input TriggerTag.",
                "instructions": instructions},
            "retainedClassScan": scan, "retainedRedirectEvidence": redirects,
            "unsupportedInputRoutes": unsupported,
            "nativeExceptions": [{"tag": "Ability.Native.Crouch", "nativeLiteralFound": True,
                                  "disposition": "Retained native input-system implementation"}],
            "preserved": {"inputActions": len(catalog["inputActions"]), "concreteMoveEntrances": 1250},
            "limitations": ["A CDO FName match is discovery evidence; it is not by itself an exact AbilityTags or grant qualification proof.",
                            "Static native selection and resource evidence do not establish animation, hit or final health behavior in a running game."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("class-scan", "redirect-evidence"):
        parser.add_argument("--" + name, type=Path, default=ROOT / "docs/generic-ability-activation-audit.json")
    parser.add_argument("--catalog", type=Path, default=ROOT / "prototype/input-router/actions.json")
    parser.add_argument("--output", type=Path, default=ROOT / "docs/generic-ability-activation-audit.json")
    args = parser.parse_args()
    result = extract(args)
    catalog = read(args.catalog)
    routes = {row["tag"]: row for row in catalog.get("unsupportedInputRoutes", []) + catalog["abilities"]}
    excluded = {row["tag"] for row in result["unsupportedInputRoutes"]}
    catalog["abilities"] = [row for tag, row in sorted(routes.items()) if tag not in excluded]
    catalog["unsupportedInputRoutes"] = [row for tag, row in sorted(routes.items()) if tag in excluded]
    catalog["coverage"]["directlyActivatableAbilityTags"] = len(catalog["abilities"])
    catalog["coverage"]["unsupportedInputRouteTags"] = len(excluded)
    args.catalog.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Audited 236 input-route tags: 227 direct abilities, 9 unavailable routes, 5728 retained class observations")


if __name__ == "__main__":
    main()
