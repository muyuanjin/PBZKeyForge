"""Check native combat queries and jump eligibility tags for noncombat ledge protection."""
import argparse
import mmap
from pathlib import Path

from CompleteMoveSignatureResourceTests import NativeFunctions, combat_art


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    expected = {
        "/Script/GameplayAbilities.AbilitySystemComponent.GetGameplayTagCount": [("GameplayTag", 0x19), ("ReturnValue", 3)],
    }
    symbols = combat_art.ScriptObjects(args.paks)
    names = {symbols.name(identity) for identity in symbols.objects}
    with args.game_exe.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native = NativeFunctions(binary)
        for path, parameters in expected.items():
            assert path in names, "Missing native function: " + path
            assert parameters in native.signatures(path.rsplit(".", 1)[1], include_return=True), path
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        expected_tags = {
            "/Game/PBZContent/Blueprint/Character/Player/BP_Player_Base": ["CharacterState.InCombat"],
            "/PBZCombatAsset/ActorData/Players/Hun/InputMap/AIM_Hun_Jump": ["CharacterState.Forbid.Jump"],
        }
        for path, tags in expected_tags.items():
            package = combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))
            assert set(tags) <= set(package.names), path
    finally:
        archive.close()
    print("MovementInputResourceTests PASS: native combat query, combat tag and jump eligibility")


if __name__ == "__main__":
    main()
