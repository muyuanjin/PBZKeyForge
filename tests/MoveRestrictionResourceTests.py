"""Check native avatar qualification and real projectile-state resources.

Reads cooked CL-302256 packages without launching the game. Binding policy and
query results are exercised separately by MoveRestrictionRuntimeTests.
"""
import argparse
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("restriction_resources", ROOT / "build/extract-combat-art-phases.py")
combat = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=pathlib.Path, required=True)
    args = parser.parse_args()
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    symbols = combat.ScriptObjects(args.paks)

    def package(path):
        return combat.e.Package(archive.resource(archive.packages[path.casefold()]))

    def named_call(pkg, payload, name):
        pin = struct.pack("<II", pkg.names.index(name), 0)
        return any(bytes([opcode]) + pin in payload for opcode in (0x1b, 0x45))

    def literal(pkg, payload, name):
        return b"\x21" + struct.pack("<II", pkg.names.index(name), 0) in payload

    def imported_call(pkg, payload, name):
        end = struct.unpack_from("<I", pkg.data, 32)[0]
        for slot in range((end - pkg.import_offset) // 8):
            identity = struct.unpack_from("<Q", pkg.data, pkg.import_offset + slot * 8)[0]
            if symbols.resolve(pkg, identity) == name:
                return any(bytes([opcode]) + struct.pack("<i", -slot - 1) in payload for opcode in (0x1c, 0x46, 0x68))
        return False

    try:
        path = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZiMingJian/New/GA_Hun_ZiMingJian_New_DB"
        pkg = package(path)
        graph = next(payload for name, payload in pkg.exports if name == "ExecuteUbergraph_GA_Hun_ZiMingJian_New_DB")
        assert named_call(pkg, graph, "HasAllMatchingGameplayTags")
        assert imported_call(pkg, graph, "/Script/GameplayAbilities.GameplayAbility.GetAvatarActorFromActorInfo")
        for tag in ("CharacterState.AbilityState.Stance.HoldingStanceKey", "CharacterState.WeaponUpgrade.ZiMingJian.CanBuJu"):
            assert literal(pkg, graph, tag), tag
        data = json.loads((ROOT / "assets/key-enhancement/move-restrictions.json").read_text(encoding="utf8"))
        move = data["entries"]["move:Weapon.Weapon_Hun_ZiMingJian.GA_Hun_ZiMingJian_New_DB"]
        assert "CharacterState.AbilityState.Stance.HoldingStanceKey" in move["groups"]["stance"]["requiredTags"]
        assert "CharacterState.WeaponUpgrade.ZiMingJian.CanBuJu" in move["groups"]["training"]["requiredTags"]

        pkg = package("/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/MoJian/GA_Hun_Mojian_Passive")
        graph = next(payload for name, payload in pkg.exports if name == "ExecuteUbergraph_GA_Hun_Mojian_Passive")
        assert "Mojian Bullets" in pkg.names
        assert imported_call(pkg, graph, "/Script/Engine.KismetArrayLibrary.Array_Length")
        for tag in ("CharacterState.AbilityState.Mojian.HasProjectile", "CharacterState.AbilityState.Mojian.ProjectileFull"):
            assert literal(pkg, graph, tag), tag
            assert tag not in data["classificationTags"], tag
            assert tag in data["entries"]["ability:Ability.Action.Hun.ShachiAbility.MoJi.AAA"]["preparation"]["nativeStateTags"]["requiredTags"]
    finally:
        archive.close()
    print("MoveRestrictionResourceTests PASS: avatar all-tag qualifications and real Mojian projectile state")


if __name__ == "__main__":
    main()
