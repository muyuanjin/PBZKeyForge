"""Check resource-view APIs and independent weapon costs against cooked data."""
import argparse
import importlib.util
import json
import mmap
from pathlib import Path
import struct

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
        "GetMax": [("ReturnValue", 0xa)],
        "MakeOutgoingGameplayEffectSpec": [("GameplayEffectClass", 0x11), ("Level", 0xa), ("ReturnValue", 0x19)],
        "BP_ApplyGameplayEffectToSelf": [("GameplayEffectClass", 0x11), ("Level", 0xa), ("EffectContext", 0x19), ("ReturnValue", 0x19)]
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
        effects = [
            "/PBZCombatAsset/ActorData/Players/Hun/Weapon/SlitheringSword/UpgradeData/GE_SlitheringSword_EvalutionCombo_CostShachi",
            "/PBZCombatAsset/Prototypes/Abilities/Base/GE/GE_Cost_Shachi_ToZero",
            "/PBZCombatAsset/ActorData/Players/Hun/Weapon/ZiMingJian/GameEffects/GE_ZiMingJian_CostShaChi"
        ]
        original = None
        for path in effects:
            package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
            payload = next(value for name, value in package.exports if name.startswith("Default__"))
            reader = combat.e.Reader(payload, package.names)
            assert reader.header() == {6: False}, path
            assert reader.number() == 1, "A suppressed pure cost must contain exactly one modifier"
            assert original is None or payload == original, "The global and Zi Ming costs must match the pure Shachi reset"
            original = payload
            assert b"Shachi\0" in payload
            assert len(package.exports) == 2, "A suppressed pure cost must not contain gameplay components"
        global_path = "/MagCombatAbilities/Abilities/BP_GlobalActionAbility_Base"
        package = combat.e.Package(archive.resource(archive.packages[global_path.casefold()]))
        graph = next(value for name, value in package.exports if name == "ExecuteUbergraph_BP_GlobalActionAbility_Base")
        assert any(ref["path"] == effects[1] for ref in combat.object_references(package, graph))
        assert "GameplayEvent.Character.GlobalAbility.CostShachi" in package.names
        zi_path = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZiMingJian/New/GA_Hun_ZiMingJian_New_DB"
        package = combat.e.Package(archive.resource(archive.packages[zi_path.casefold()]))
        action = next(value for name, value in package.exports if name == "GAA_GameplayEffectFrame_C_0")
        assert any(ref["path"] == effects[2] for ref in combat.object_references(package, action))
        helper = "/MagCombatAbilities/Actions/GAA_GameplayEffectFrame"
        package = combat.e.Package(archive.resource(archive.packages[helper.casefold()]))
        graph = next(value for name, value in package.exports if name == "Apply Gameplay Effect")
        symbols = combat.ScriptObjects(args.paks)
        start = struct.unpack_from("<I", package.data, 32)[0]
        functions = []
        for index in range((start - package.import_offset) // 8):
            if struct.pack("<i", -index - 1) not in graph:
                continue
            identity = struct.unpack_from("<Q", package.data, package.import_offset + index * 8)[0]
            functions.append(symbols.resolve(package, identity))
        assert "/Script/GameplayAbilities.GameplayAbility.MakeOutgoingGameplayEffectSpec" in functions
        shield = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/ZhuoBiDun/GA_ZhuoBiDun"
        package = combat.e.Package(archive.resource(archive.packages[shield.casefold()]))
        assert "shachi" in data["classes"][shield + ".GA_ZhuoBiDun_C"]
        imported = {}
        start = struct.unpack_from("<I", package.data, 32)[0]
        for index in range((start - package.import_offset) // 8):
            identity = struct.unpack_from("<Q", package.data, package.import_offset + index * 8)[0]
            imported[-index - 1] = symbols.resolve(package, identity)
        query = next(pointer for pointer, name in imported.items()
                     if name == "/Script/SGameAttributes.AttributeUtils.GetCharacterAttributeValue")
        modify = next(pointer for pointer, name in imported.items()
                      if name == "/Script/SGameAttributes.AttributeUtils.ModifyCharacterAttributeValue")
        payload = next(value for name, value in package.exports if name == "IsShaqiEnough")
        assert struct.pack("<i", query) in payload and b"BasicAttributeSet:Shachi" in payload
        for function in ("HandleDodgeCost", "HandleParryCost", "HandlePushCost", "HandleRushCost"):
            payload = next(value for name, value in package.exports if name == function)
            assert struct.pack("<i", modify) in payload and b"BasicAttributeSet:Shachi" in payload, function
        assert not any("GE_ShieldShaqiCost" in value for value in package.imports)
    finally:
        archive.close()
    print("ResourceCapacityResourceTests PASS: native capacity/query/spec signatures, 8 cooked resource families, 4 shield costs and global/Zi Ming pure costs")


if __name__ == "__main__":
    main()
