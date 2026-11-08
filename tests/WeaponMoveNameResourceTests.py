"""Verify official weapon names against native localization, effects and routes."""
import argparse
import importlib.util
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("weapon_names", ROOT / "build/extract-weapon-move-names.py")
names = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(names)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for argument in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + argument, required=True, type=pathlib.Path)
    args = parser.parse_args()
    load = names.entries.read_json
    contribution = load(ROOT / "docs/game-move-names-weapons.json")
    evidence = load(ROOT / "docs/game-move-names-weapons-evidence.json")
    catalog = {x["id"]: x for x in load(ROOT / "assets/key-enhancement/weapon-moves.json")["moves"]}
    archive = names.entries.combat.e.Archive(args.game_exe, args.paks, args.oodle, contribution["build"])
    try:
        symbols = names.entries.combat.ScriptObjects(args.paks)
        table = names.entries.special.inspect(archive, symbols, "/Game/PBZContent/DataTable/UI/DT_WeaponData")[0]
        native_records = names.function_records(table, "Weapon_FunctionName_")
        localization = names.entries.module("name_test_shadow", "extract-shadow-move-entries.py")
        languages = load(ROOT / "assets/key-enhancement/action-localization.json")["languages"]
        localized = localization.read_labels(archive, languages)
        for record in evidence["records"]:
            assert any(x["name"] == record["name"] and x["effect"] == record["effect"] for x in native_records)
            for language, text in record["labels"].items():
                assert localized[language][("Default", record["name"]["key"])] == text
        native = {x["name"]["key"]: x for x in evidence["records"]}
        assert native["Weapon_FunctionName_002_15"]["labels"]["zh-Hans"] == "血刃蓄力"
        assert native["Weapon_FunctionName_002_15"]["effect"]["package"].endswith("/GE_Weapon_Iron_BCharge")
        assert native["Weapon_FunctionName_012_15"]["labels"]["zh-Hans"] == "横扫刀劲"
        assert native["Weapon_FunctionName_012_25"]["labels"]["zh-Hans"] == "风回云卷"
        assert native["Weapon_FunctionName_012_15"]["upgradeTags"] == ["CharacterState.WeaponUpgrade.Long.BBB"]
        for association in evidence["associations"]:
            value = contribution["entries"][association["id"]]
            move = catalog[association["id"]]
            assert value["evidence"]["abilityClasses"] == [move["abilityClass"]]
            key = association["primaryKey"]
            if key:
                assert len(value["labels"]) == 15
                for language in languages:
                    assert value["labels"][language] == move["weaponLabels"][language] + " · " + localized[language][("Default", key)]
                if key.endswith("_Skill"):
                    assert move["tag"].startswith("Ability.Action.Hun.CombatArt.")
                else:
                    record = native[key]
                    assert set(record["upgradeTags"]) & set(association["nativeRouteRequiredTags"]) or move["abilityClass"].split(".")[0] in record["grantedAbilities"]
            for effect in value["effects"]:
                assert effect["labels"] == native[effect["key"]]["labels"]
        plain = next(x for x in catalog if x.endswith("Weapon_Hun_ChiRen.GA_Hun_ChiRen_A"))
        assert plain not in contribution["entries"], "Ordinary attacks must not acquire unrelated training titles"
    finally:
        archive.close()
    print("PASS: official weapon names, 15 native languages, exact unlock routes and passive-title separation")


if __name__ == "__main__":
    main()
