"""Verify native input pins, inherited entrances and generated preparation against cooked resources."""
import argparse
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("native_inputs", ROOT / "build/extract-native-move-inputs.py")
native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(native)


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    archive = native.combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = native.extract(archive)
        assert result == read("docs/native-move-inputs.json"), "Regenerate the native input contribution"
        calls = {row["package"]: row for row in result["inputCallAudit"]}
        entries = result["entries"]
        generated = read("assets/key-enhancement/move-restrictions.json")["entries"]
        for association in result["associations"]:
            identity, owner = association["identity"], association["inputOwner"]
            prep = entries[identity]["preparation"]
            options = prep.get("nativeHoldVariants", [prep])
            selected = next(row for row in options if row["abilityClass"].split(".")[0] == association["ability"])
            actions = {call.get("inputAction") for call in calls[owner]["calls"]}
            assert set(selected["nativeHoldInputs"]) <= actions, (identity, actions)
            for action in selected["nativeHoldInputs"]:
                assert action.split(".")[0].casefold() in archive.packages, action
            for key, value in prep.items():
                assert generated[identity]["preparation"][key] == value, (identity, key)

        # Both public entrances must obey the same real native Hold dependency.
        for identity in ("ability:Ability.Action.Hun.Heavy.Horizontal.BladeWave",
                         "move:Weapon.Weapon_Hun_Horizontal.GA_Hun_Horizontal_BladeWave"):
            prep = entries[identity]["preparation"]
            assert prep["nativeHoldInputs"] == ["/PBZInput/InputActions/IA_AttackH_Hold.IA_AttackH_Hold"]
            assert prep["automaticReleaseWhen"] == {"native_completion": True}
        owners = {row["inputOwner"].rsplit("/", 1)[-1] for row in result["associations"]}
        assert {"GA_Hun_BronzeSword_B_Hold", "GA_Hun_FengLeiJian_B_Hold", "GA_Hun_Iron_BCharge",
                "GA_Hun_GuiTouBlade_B_Hold_Start", "GA_Hun_PoJun_DB",
                "GA_Hun_SlitheringSword_DA", "GA_Hun_ChiRen_C", "GA_Hun_YanYueDao_SpecialSkill",
                "GA_JinGua_RoAttack", "GA_BrutalAxe_Normal", "GA_Hun_ChiZheCanJian_DrinkBase"} <= owners
        for leaf in ("GA_Hun_BronzeSword_B_Hold", "GA_Hun_FengLeiJian_B_Hold"):
            row = next(row for path, row in calls.items() if path.endswith("/" + leaf))
            assert any(call.get("holdSeconds") == .5 for call in row["calls"])
        shared = entries["ability:Ability.Action.Hun.CombatArt.Atk01"]["preparation"]["nativeHoldVariants"]
        assert len(shared) == 1 and "ChiRen" in shared[0]["abilityClass"]
        assert all("IA_AttackH_Press" not in str(row["preparation"]) for row in entries.values())
        assert not any("YanYueDao_C." in str(row["preparation"]) for row in entries.values())
        for row in calls.values():
            if row["status"] == "unused-holding-helper":
                assert row["package"].rsplit("/", 1)[-1] not in owners
        assert result["coverage"]["scannedPackages"] > 1600
        print("NativeMoveInputResourceTests PASS: %d packages, %d input lifecycles, %d prepared entrances" % (
            result["coverage"]["scannedPackages"], len(calls), len(entries)))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
