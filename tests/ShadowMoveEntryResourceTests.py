"""Verify newly listed shadow entrances against native grants and class ancestry."""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("entry_combat", ROOT / "build/extract-combat-art-phases.py")
combat = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    args = parser.parse_args()
    load = lambda name: json.loads((ROOT / name).read_text(encoding="utf-8-sig"))
    evidence = load("docs/shadow-move-entries.json")
    catalog = load("assets/key-enhancement/shadow-moves.json")
    previous = load("assets/key-enhancement/complete-moves.json")
    audit = load("docs/complete-move-shadow-audit.json")
    languages = set(load("assets/key-enhancement/action-localization.json")["languages"])
    resources = load("assets/key-enhancement/resource-costs.json")
    assert evidence["build"] == catalog["build"] == audit["build"] == "CL-302256"
    assert len(evidence["configurations"]) == 46 and len(evidence["families"]) == 26
    assert {row["equipmentId"] for row in evidence["configurations"]} == {
        variant["package"] for family in audit["families"] for variant in family["configurationVariants"]}
    moves = {move["id"]: move for move in catalog["moves"]}
    assert len(moves) == len(catalog["moves"]) and not set(moves) & {move["id"] for move in previous["moves"]}
    expected = {
        "CaoOuSiXian.GA_CaoOuSiXian_Normal_01", "JianKeZhaoJiLing.GA_JianKeZhaoJiLing_Normal_01",
        "KuYeSiZhuChuan.GA_KuYeSiZhuChuan_Normal_01", "XuKongHuaShan.GA_XuKongHuaShan_Skill",
        "HuangJinHuoChong.GA_HuangJinHuoChong_Normal_02", "Mojian.GA_Hun_Mojian_AA",
        "ShachiHook.GA_Hun_ShachiHook_CA", "ShachiHook.GA_Hun_ShachiHook_CB", "ShachiHook.GA_Hun_ShachiHook_CD",
        "XueSeLingDang.GA_XueSeLingDang_Normal_01"}
    assert {"move:Shadow." + suffix for suffix in expected} <= set(moves)
    blood_bell = moves["move:Shadow.XueSeLingDang.GA_XueSeLingDang_Normal_01"]
    assert set(blood_bell["weaponLabels"].values()) == {"BloodBell"}
    assert next(family for family in evidence["families"] if family["family"] == "XueSeLingDang")["nativeNameStatus"] == "unresolved"
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, catalog["build"])
    symbols = combat.ScriptObjects(args.paks)
    grants = set()
    try:
        for config in evidence["configurations"]:
            package = combat.e.Package(archive.resource(archive.packages[config["equipmentId"].casefold()]))
            reader = combat.e.Reader(package.exports[0][1], package.names)
            actual = {5: [], 6: []}
            for field, zero in reader.header().items():
                if field > 6:
                    break
                if zero:
                    continue
                if field in (0, 1):
                    for _ in range(reader.number()):
                        reader.name()
                elif field in (2, 3, 5, 6):
                    entries = [package.object_name(reader.number()) for _ in range(reader.number())]
                    if field in actual:
                        actual[field] = entries
                else:
                    raise AssertionError("Unknown native shadow configuration property " + str(field))
            assert actual[5] == config["equipAbilities"] and actual[6] == config["owningAbilities"]
            grants.update(actual[5] + actual[6])
        input_package = "/PBZInput/InputActions/IA_ShachiAbility"
        assert input_package.casefold() in archive.packages
        package = combat.e.Package(archive.resource(archive.packages[input_package.casefold()]))
        assert any(name == "IA_ShachiAbility" for name, _ in package.exports)
        for move in moves.values():
            assert move["abilityClass"].split(".")[0] in grants, move["id"]
            assert move["activateByClass"] is True and not move.get("entryPoint") and not move.get("prepareInput")
            assert move["inputAction"] == input_package + ".IA_ShachiAbility"
            assert set(move["weaponLabels"]) == languages and all(move["weaponLabels"].values())
            assert set(resources["classes"][move["abilityClass"]]) == {"shachi", "spirit"}, move["id"]
            path, seen = move["abilityClass"].split(".")[0], set()
            while path and path.casefold() in archive.packages:
                assert path not in seen
                seen.add(path)
                package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
                start = struct.unpack_from("<I", package.data, 32)[0]
                parent = None
                for index, _ in enumerate(package.exports):
                    _, cls, superclass = struct.unpack_from("<QQQ", package.data, start + index * 72 + 24)
                    if symbols.resolve(package, cls).endswith("BlueprintGeneratedClass"):
                        parent = symbols.resolve(package, superclass)
                        break
                path = parent
            assert "/PBZCombatAsset/Prototypes/Abilities/Base/BP_ShachiAbility_Base" in seen, move["id"]
        assert not any("ThunderSpear_DeflectCounter" in move["id"] for move in moves.values()), "Existing exact counter must not be duplicated"
    finally:
        archive.close()
    print("ShadowMoveEntryResourceTests PASS: 46 configurations, %d precise entrances" % len(moves))


if __name__ == "__main__":
    main()
