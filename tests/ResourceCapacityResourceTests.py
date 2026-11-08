"""Check resource-view APIs and independent weapon costs against cooked data."""
import argparse
import importlib.util
import json
import mmap
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("move_signatures", ROOT / "tests/CompleteMoveSignatureResourceTests.py")
signatures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(signatures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    expected = {
        "GetAttributeSet": [("AttributeSetClass", 0x11), ("ReturnValue", 0x12)],
        "GetCharacterAttributeValue": [("Character", 0x12), ("Attribute", 0x19), ("ReturnValue", 0xa)],
        "GetFloatAttribute": [("Actor", 0x12), ("Attribute", 0x19), ("bSuccessfullyFoundAttribute", 0x4c), ("ReturnValue", 0xa)],
        "SendGameplayEvent": [("EventTag", 0x19), ("Payload", 0x19)],
        "GetMax": [("ReturnValue", 0xa)]
    }
    with args.game_exe.open("rb") as file, mmap.mmap(file.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native = signatures.NativeFunctions(binary)
        for name, fields in expected.items():
            assert fields in native.signatures(name, include_return=True), (name, fields)

    data = json.loads((ROOT / "assets/key-enhancement/resource-costs.json").read_text(encoding="utf-8"))
    checks = {
        "/PBZCombatAsset/Prototypes/Abilities/Base/BP_HeavyAttack_Air_Base": ("shachi", "Get Shachi Cost", "Shachi_Max"),
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_C": ("ultimate", None, "UltimateChargeCount"),
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/SlitheringSword/Attack/GA_Hun_SlitheringSword_DA": ("shachi", None, "Shachi"),
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/SnakeDual/Attack/GA_Hun_SnakeDual_DA": ("shachi", None, "Shachi"),
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/HeiMoShang/GA_Hun_HeiMoShang_Reborn": ("shachi", None, "Shachi"),
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/XuKongHuaShan/GA_XuKongHuaShan_Reborn": ("shachi", None, "Shachi"),
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShuangYue/Default/GA_Hun_ShuangYue_DB": ("weapon_charge", None, None)
    }
    combat = signatures.combat_art
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        for path, (kind, function, attribute) in checks.items():
            package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
            leaf = path.rsplit("/", 1)[-1]
            assert kind in data["classes"][path + "." + leaf + "_C"], path
            function = function or "ExecuteUbergraph_" + leaf
            payload = next(value for name, value in package.exports if name == function)
            if attribute:
                assert ("/Script/CharacterBasics.BasicAttributeSet:" + attribute).encode() in payload, path
            else:
                assert "GameplayEvent.Ability.TianShaShuangYue.ConsumeAll" in package.names
        for tag in ("Ability.Action.Hun.Weapon.Switch.Normal", "Ability.Action.Hun.Weapon.Switch.Dash"):
            assert "weapon_charge" in data["tags"][tag], tag
    finally:
        archive.close()
    print("ResourceCapacityResourceTests PASS: native capacity/query/event signatures and 7 cooked resource families")


if __name__ == "__main__":
    main()
