"""Check the conditional accessory release against installed CL-302256 resources.

Reads actual grant components, inheritance and Blueprint event wrappers. This
does not launch the game or establish observed combat behavior.
"""
import argparse
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("ability_sources", ROOT / "build/extract-ability-sources.py")
sources = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sources)
combat_art = sources.combat_art


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    args = parser.parse_args()
    evidence = json.loads((ROOT / "docs/accessory-move-audit.json").read_text(encoding="utf-8"))
    catalog = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))
    assert evidence["build"] == catalog["build"] == "CL-302256"
    expected = evidence["ghostFire"]
    move = next(item for item in catalog["moves"] if item["id"] == expected["id"])
    assert move["activeAbilityClasses"] == expected["activeAbilityClasses"]
    assert move["abilityClass"] + ":" + move["entryPoint"] == expected["entryPoint"]
    assert not any(move.get(field) for field in ("tag", "inputAction", "prepareInput", "entryWhen"))
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, evidence["build"])
    symbols = combat_art.ScriptObjects(args.paks)
    base = "/PBZCombatAsset/Prototypes/GameplayEffects/Accessories/New/"

    def package(path):
        return combat_art.e.Package(archive.resource(archive.packages[path.split(".")[0].casefold()]))

    def parent(pkg):
        start = struct.unpack_from("<I", pkg.data, 32)[0]
        for index in range(len(pkg.exports)):
            _, cls, superclass = struct.unpack_from("<QQQ", pkg.data, start + index * 72 + 24)
            if symbols.resolve(pkg, cls).endswith("BlueprintGeneratedClass"):
                return symbols.resolve(pkg, superclass)
        raise AssertionError("No generated ability class")

    def grants(pkg, ability):
        components, unknowns = sources.effect_components(pkg, symbols)
        assert not unknowns, unknowns
        grant = next(component for component in components if component.get("activation") == "grant-and-activate")
        assert [item["ability"] for item in grant["grants"]] == [ability]

    def wrapper(pkg, callback, entry):
        payload = next(payload for name, payload in pkg.exports if name == callback)
        ubergraph = next(index + 1 for index, (name, _) in enumerate(pkg.exports)
                         if name.startswith("ExecuteUbergraph_"))
        code = b"\x46" + struct.pack("<i", ubergraph) + b"\x1d" + struct.pack("<i", entry) + b"\x16\x04\x0b\x53"
        assert struct.pack("<II", 18, len(code)) + code in payload, callback

    try:
        for level, ability in enumerate(expected["activeAbilityClasses"], 1):
            pkg = package(ability)
            if level > 1:
                assert parent(pkg) == expected["activeAbilityClasses"][0].split(".")[0]
            grants(package(base + "GE_Accessory_RandomDrink_GhostFire_Lv" + str(level)), ability.split(".")[0])
            grants(package(base + "GE_Accessory_RandomDrink_Lv" + str(level)),
                   base + "GA_Accessory_RandomDrink_Lv" + str(level))
        ghost = package(expected["activeAbilityClasses"][0])
        for callback, entry in expected["wrappers"].items():
            wrapper(ghost, callback, int(entry, 16))
        random_drink = package(base + "GA_Accessory_RandomDrink_Lv1")
        assert "GameplayEvent.Medicine.PostActive" in random_drink.names
        assert base + "GE_Accessory_RandomDrink_GhostFire_Lv1" in random_drink.imports
        assert base + "GE_Accessory_RandomDrink_FireBreathe_Lv1" in random_drink.imports
        for family in ("Electrified", "Ignite"):
            passive = package(base + "BP_GA_Accessory_" + family + "_Passive")
            assert parent(passive) == "/Script/PBZCombatAsset.GA_Accessory" + family + "Passive"
        evade = package(base + "BP_GA_Accessory_EvadePhantom_Passive_Lv1")
        assert "GameplayEvent.Ability.Action.Deflect.Consume.Dodge.Defender" in evade.names
        assert "HandledDodgeHandles" in evade.names
        fire_breathe = package(base + "GE_Accessory_RandomDrink_FireBreathe_Lv1")
        assert "BulletGameplayEffectComponent" in fire_breathe.names
        assert not any(name == "Targeting Tick" for name, _ in fire_breathe.exports)
        print("AccessoryMoveResourceTests PASS: three natural grants, inherited targeting wrappers and event qualification")
    finally:
        archive.close()


if __name__ == "__main__":
    main()
