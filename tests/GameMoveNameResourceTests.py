"""Check official names, native upgrade identity and the eye-spawn lifecycle."""
import argparse
import importlib.util
import json
import pathlib
import re
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("game_names", ROOT / "build/extract-game-move-names.py")
names = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(names)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=pathlib.Path, required=True)
    args = parser.parse_args()
    load = lambda path: json.loads((ROOT / path).read_text(encoding="utf8"))
    data = load("assets/key-enhancement/game-move-names.json")
    evidence = load("docs/game-move-name-shadow-evidence.json")
    restrictions = load("docs/move-restrictions-shadow.json")
    languages = load("assets/key-enhancement/action-localization.json")["languages"]
    actions = load("prototype/input-router/actions.json")
    identities = {"ability:" + row["tag"] for row in actions["abilities"]}
    for file in ("complete-moves", "shadow-moves", "weapon-moves"):
        identities.update(row["id"] for row in load("assets/key-enhancement/" + file + ".json")["moves"])
    assert set(data["entries"]) <= identities
    assert len(evidence["families"]) == 26
    laser_id = "ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser"
    laser = data["entries"][laser_id]
    assert laser["labels"]["zh-Hans"] == "独眼弓 · 死光"
    assert not any("LaserEyes" in identity for identity in identities)
    eyes = next(effect for effect in laser["effects"] if effect["key"] == "PhantomEdges_FunctionName_109_25")
    assert eyes["kind"] == "upgrade" and eyes["upgradeTags"] == ["CharacterState.WeaponUpgrade.DuYanBow.LaserEyes"]
    assert "眼球" in eyes["descriptionLabels"]["zh-Hans"]
    archive = names.shadow.combat.e.Archive(args.game_exe, args.paks, args.oodle, data["build"])
    try:
        native = names.shadow.read_labels(archive, languages)
        for item in data["entries"].values():
            if "labels" in item:
                assert set(item["labels"]) == set(languages)
            effect_ids = [(effect["key"], tuple(effect.get("upgradeTags", []))) for effect in item.get("effects", [])]
            assert len(effect_ids) == len(set(effect_ids))
            for effect in item.get("effects", []):
                assert set(effect["labels"]) == set(languages)
                for language, label in effect["labels"].items():
                    source = native[language][("Default", effect["key"])]
                    assert label in [names.choose_variant(source, rank) for rank in range(3)], (effect["key"], language, label)
                assert all(not re.search(r"<[^>]*>|\{[^}]*\}|/PBZCombatAsset/", text) for text in effect.get("descriptionLabels", {}).values())
        source = evidence["nativeEyeUpgrade"]
        path = source["abilityClass"]
        package = names.shadow.combat.e.Package(archive.resource(archive.packages[path.casefold()]))
        for action in source["actions"]:
            export_name, payload = package.exports[action["exportIndex"]]
            assert export_name == action["export"]
            pattern = struct.pack("<II", package.names.index(action["requiredUpgradeTag"]), 0)
            assert payload[action["tagExportOffset"]:action["tagExportOffset"] + 8] == pattern
        assert len(source["actions"]) == 2 and max(action["startTime"] for action in source["actions"]) < .056
        assert source["eyeBullet"] in package.imports
        for call in restrictions["nativeReleaseTaskEvidence"]:
            package = names.shadow.combat.e.Package(archive.resource(archive.packages[call["package"].casefold()]))
            payload = next(payload for export, payload in package.exports if export == call["export"])
            pin = b"\x20" + struct.pack("<i", call["inputPointer"]) + b"\x16"
            assert pin in payload[call["exportOffset"]:call["exportOffset"] + 32]
            assert package.object_name(call["inputPointer"]) == call["inputAction"].split(".")[0]
        for identity in (laser_id, "move:Shadow.DuYanBow.GA_DuYan_Bow_Normal"):
            preparation = restrictions["entries"][identity]["preparation"]
            assert preparation["nativeHoldInputs"] == ["/PBZInput/InputActions/IA_ShachiAbility_Hold.IA_ShachiAbility_Hold"]
            assert "automaticReleaseWhen" in preparation
        normal = restrictions["entries"]["move:Shadow.DuYanBow.GA_DuYan_Bow_Normal"]["preparation"]["automaticReleaseWhen"]
        assert normal["minimum_time"] > 1 and normal["object_variable"]["status"] == 1
    finally:
        archive.close()
    print("Official names and native eye upgrade: PASS (%d entries)" % len(data["entries"]))


if __name__ == "__main__":
    main()
