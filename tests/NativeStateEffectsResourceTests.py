"""Verify prepared native effects against the installed cooked resources."""
import argparse
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / "build" / file)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args()
    special = module("native_effect_special", "extract-weapon-special-entries.py")
    sources = module("native_effect_sources", "extract-ability-sources.py")
    data = json.loads((ROOT / "assets/key-enhancement/move-restrictions.json").read_text(encoding="utf-8"))
    effects = {effect["effectClass"]: effect for entry in data["entries"].values()
               for effect in entry["preparation"].get("nativeStateEffects", [])}
    archive = special.combat_art.e.Archive(args.game_exe, args.paks, args.oodle, data["build"])
    try:
        symbols = special.combat_art.ScriptObjects(archive.paks)
        for path, effect in effects.items():
            if path.startswith("/Script/"):
                names = {symbols.name(identity) for identity in symbols.objects}
                assert path in names and path.replace(".GE_", ".Default__GE_") + ".Target Tags" in names, path
                continue
            package_path, class_name = path.split(".")
            package, exports, parent = special.inspect(archive, symbols, package_path)
            assert any(row["name"] == class_name and row["class"] == "/Script/Engine.BlueprintGeneratedClass"
                       for row in exports), path
            components, unknown = sources.effect_components(package, symbols)
            assert not unknown, (path, unknown)
            granted = {tag for component in components for tag in component.get("targetTags", {}).get("addedTags", [])}
            assert effect["qualificationTag"] in granted, (path, effect["qualificationTag"], granted)
            classes = {row["class"] for row in exports}
            if "PerfectDeflect" in path:
                assert "/Script/SGameAttributes.PreCalculationModificationGameplayEffectComponent" in classes
                assert "Damage.Attack.Target.AngerDamage" in package.names
            if "BloodSaw" in path:
                assert "/Script/SGameCombatSystem.WeaponBuffGameplayEffectComponent" in classes
                assert "GameplayCue.Weapon.JigSaw.BloodSaw" in package.names
            if "GE_Weapon_Long_Buff_Lv3" in path:
                assert {"/Script/SGameCombatSystem.WeaponBuffGameplayEffectComponent",
                        "/Script/PBZCombatAsset.ChargeObjectGameplayEffectComponent"} <= classes
                assert {"Damage.Attack.Target.PhysicalDamage", "Damage.Attack.Target.AngerDamage", "LongCharge"} <= set(package.names)
                assert len(effect["requirementScopes"]) == 1
                for scope in effect["requirementScopes"]:
                    scope_package, scope_exports, _ = special.inspect(archive, symbols, scope["effectClass"].split(".")[0])
                    assert any(row["class"] == "/Script/GameplayAbilities.TargetTagRequirementsGameplayEffectComponent"
                               for row in scope_exports), scope["effectClass"]
                for replaced in effect["replacedEffectClasses"]:
                    replaced_package, _, _ = special.inspect(archive, symbols, replaced.split(".")[0])
                    replaced_components, _ = sources.effect_components(replaced_package, symbols)
                    assert any(component.get("targetTags", {}).get("addedTags", [])
                               for component in replaced_components), replaced
            if "YuuBlade_Buff" in path:
                assert {"Attack", "ShachiAttack", "UltimateChargeGainRateMultiplier", "CharacterState.GhostTrail"} <= set(package.names)
            if "YanYueDao" in path:
                assert "GameplayCue.Buff.YanYueDao.SkillReinforced" in package.names
        # SnapSoul is a native route marker with no damage/cue/object component.
        package, exports, parent = special.inspect(archive, symbols,
            "/PBZCombatAsset/ActorData/Players/Hun/Weapon/LuanYing/UpgradeData/GE_LuanYing_SnapSoul")
        components, unknown = sources.effect_components(package, symbols)
        assert parent == "/Script/GameplayAbilities.GameplayEffect"
        assert len(components) == 1 and not unknown
        assert components[0]["targetTags"]["addedTags"] == ["CharacterState.AbilityState.LuanYing.SnapSoul"]
        print("NativeStateEffectsResourceTests PASS: %d original effects, modifiers, native cues and pure route state" % len(effects))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
