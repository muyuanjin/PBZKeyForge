"""Assert the ability-source boundary and real grant/tag fields in cooked resources."""
import argparse
import collections
import importlib.util
import json
import pathlib
import struct
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("sources", ROOT / "build/extract-ability-sources.py")
sources = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sources)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle", "registry"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    audit = json.loads((ROOT / "docs/ability-source-audit.json").read_text(encoding="utf-8"))
    assets = json.loads(args.registry.read_text(encoding="utf-8"))["assets"]
    index = sources.registry_index(assets)
    # The independently exported registry is the boundary, rather than a
    # directory list or only the packages reached from known weapons.
    expected = {row["package"] for row in index.values() if sources.kind(row) == "ability"}
    actual = [path for paths in audit["abilityUniverse"].values() for path in paths]
    assert len(actual) == len(set(actual)) == 5728
    assert set(actual) == expected
    assert sum("DataTable" in row["classes"] for row in index.values()) == audit["counts"]["dataTablesEnumerated"] == 380
    assert all(len(paths) == audit["counts"]["abilityClassification"][key] for key, paths in audit["abilityUniverse"].items())
    assert not audit["runtimeVerified"] and not audit["decodeUnknowns"] and not audit["missingPackages"]
    moves = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]
    for move in moves:
        for field in ("inputAction", "prepareInput"):
            if not move.get(field):
                continue
            path = move[field].split(".")[0]
            assert path.casefold() in index and "InputAction" in index[path.casefold()]["classes"], (move["id"], field, path)
        for path in ([move["abilityClass"]] if move.get("abilityClass") else []) + move.get("activeAbilityClasses", []):
            path = path.split(".")[0]
            assert path.casefold() in index and sources.kind(index[path.casefold()]) == "ability", (move["id"], path)
    archive = sources.e.Archive(args.game_exe, args.paks, args.oodle, audit["build"])
    symbols = sources.combat_art.ScriptObjects(args.paks)
    try:
        def package(path):
            return sources.e.Package(archive.resource(archive.packages[path.casefold()]))
        def refs(path):
            return {row["package"]: row for row in sources.references(package(path), index)}
        weapon = "/PBZCombatAsset/ActorData/Players/Hun/Weapon/BronzeSword/Weapon_Hun_TianXuanQingTongJian"
        assert sources.kind(index[weapon.casefold()]) == "provider"
        assert any(path.endswith("/GA_Hun_BronzeSword_C") for path in refs(weapon))
        accessory = "/Game/PBZContent/DataTable/UI/DT_Accessory"
        effects = "/PBZCombatAsset/Prototypes/GameplayEffects/Accessories/New/"
        effect = effects + "GE_Accessory_RandomDrink_Lv1"
        assert "soft-object-path" in refs(accessory)[effect]["referenceKinds"]
        components, unknowns = sources.effect_components(package(effect), symbols)
        assert not unknowns
        grants = [component for component in components if "grants" in component]
        assert len(grants) == 1 and grants[0]["activation"] == "grant-and-activate"
        assert [grant["ability"] for grant in grants[0]["grants"]] == [effects + "GA_Accessory_RandomDrink_Lv1"]
        ghost = effects + "GE_Accessory_RandomDrink_GhostFire_Lv1"
        ghost_paths = [path for path in refs(effects + "GA_Accessory_RandomDrink_Lv1") if "GhostFire" in path]
        assert ghost_paths, "RandomDrink must retain its real GhostFire source reference"
        components, unknowns = sources.effect_components(package(ghost), symbols)
        assert not unknowns
        assert any(grant.get("ability") == effects + "GA_Accessory_RandomDrink_GhostFire_Lv1"
                   for component in components for grant in component.get("grants", []))
        training = "/Game/PBZContent/DataTable/UI/DT_AbilityNodesTable"
        assert index[training.casefold()]["tags"]["RowStructure"] == "/Script/ProjectPBZ.AbilityNodeStruct"
        assert any(row["referenceKinds"] == ["soft-object-path"] for row in refs(training).values())
        upgrades = "/PBZCombatAsset/ActorData/Players/Hun/ShaqiWeapon/ThunderSpear/UpgradeData/"
        shachi = refs("/Game/PBZContent/DataTable/UI/DT_ShaqiWeaponData")
        for suffix, tag in (("CA", "CharacterState.WeaponUpgrade.ThunderSpear.CA"),
                            ("HuiMaQiang", "CharacterState.WeaponUpgrade.ThunderSpear.HuiMaQiang"),
                            ("ThunderChain", "CharacterState.AbilityState.ThunderSpear.LightningChain")):
            effect = upgrades + "GE_ShachiWeapon_Upgrade_" + suffix
            assert "soft-object-path" in shachi[effect]["referenceKinds"]
            components, unknowns = sources.effect_components(package(effect), symbols)
            assert not unknowns
            assert [tag] in [component.get("targetTags", {}).get("addedTags") for component in components]
        actor = "/PBZCombatAsset/ActorData/Players/Hun/ActorConfig_Hun"
        actual_refs = refs(actor)
        assert any("/Unarm/" in path for path in actual_refs)
        assert "/PBZCombatAsset/ActorData/Mobs/054_ZuoShang/Ability/Abilities/GA_ZuoShang_CA_03_QTE" in actual_refs
        counters = json.loads((ROOT / "docs/contextual-counter-recipes.json").read_text(encoding="utf-8"))
        sys.path.insert(0, str(ROOT / "prototype/input-router"))
        import special_dash
        dash_path = "/PBZCombatAsset/ActorData/Players/Hun/InputMap/AIM_Hun_Dash"
        dash = package(dash_path)
        dash_map = special_dash.BindingDocument(dash.exports[0][1], dash.names)
        assert any(group["ActionPriority"] == -5 and route.get("TriggerTag") == "Ability.Action.Hun.PerfectDodge"
                   and "CharacterState.Space.Ground" in route.get("RequiredTags", [])
                   for group in dash_map.items
                   for block in [group.get("NormalData")] + [pair["value"] for pair in (group.get("TagConditionData") or {}).get("pairs", [])]
                   for kind in ("Actions", "Combos", "InterruptLower") for route in (block or {}).get(kind) or [])
        catalog = {row["id"]: row for row in json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]}
        assert len(counters["recipes"]) == 31 and len(counters["excludedReceiverClasses"]) == 9
        normal_entry = "/Script/SGameInputSystem.AbilityInputComponent.TryActivateExternalActionAbilityByClass"
        assert normal_entry in {symbols.name(identity) for identity in symbols.objects}
        special_base = "/PBZDeflectSystem/Abilities/BP_PlayerPerfectDodge_Base_Special"
        execution_base = "/PBZDeflectSystem/Abilities/BP_Hun_PerfectDodge_ExecutionBase"
        for base in (special_base, execution_base):
            current = package(base)
            assert "Get Valid Perfect Dodge Timelines" in {name for name, _ in current.exports}
            assert "Counter Ability" in current.names and "CallFunc_GetObjectClass_ReturnValue" in current.names and "CallFunc_EqualEqual_ClassClass_ReturnValue" in current.names
        for entry in counters["recipes"]:
            recipe = entry["recipe"]
            assert all(catalog[recipe["id"]][key] == value for key, value in recipe.items()), recipe["id"]
            path, generated = recipe["abilityClass"].split(".", 1)
            assert generated == path.rsplit("/", 1)[-1] + "_C"
            assert recipe["activateByClass"] and "entryPoint" not in recipe
            assert "CharacterState.Space.Ground" in recipe["requiredTags"]
            assert recipe["inputAction"] == "/PBZDeflectSystem/Input/IA_PerfectDodge.IA_PerfectDodge"
            assert special_base in entry["parentChain"] or execution_base in entry["parentChain"]
            assert not any("GA_ExecutedAbilityBase" in parent for parent in entry["parentChain"])
            assert path in refs(entry["sources"][0]["source"])
            assert package(path).exports
            # A generic ability reference is insufficient: the selected class
            # must occur in an actual deflect-frame-producing action export.
            frame_sources = []
            for source in entry["sources"]:
                current = package(source["source"])
                export_start = struct.unpack_from("<I", current.data, 32)[0]
                for i, (name, payload) in enumerate(current.exports):
                    cls = symbols.resolve(current, struct.unpack_from("<Q", current.data, export_start + i * 72 + 32)[0])
                    if cls not in ("/Script/PBZCombatAsset.LGAAction_DurationBulletWithDeflect",
                                   "/PBZDeflectSystem/Actions/GAA_DelayDeflectProjectileBullet"):
                        continue
                    for position in range(len(payload) - 3):
                        pointer = struct.unpack_from("<i", payload, position)[0]
                        if pointer < 0 and -pointer * 8 <= export_start - current.import_offset and current.object_name(pointer) == path:
                            frame_sources.append((source["source"], name, cls, position))
            assert frame_sources, (path, "No matching class on an actual deflect frame action")
        gaps = json.loads((ROOT / "docs/player-ability-source-gaps.json").read_text(encoding="utf-8"))
        assert gaps["candidateCount"] == 53 and not gaps["missingSourcePackages"]
        assert sum(bool(row["reverseSources"]) for row in gaps["candidates"]) == 49
        assert gaps["reverseSourcePackagesScanned"] == 104190
    finally:
        archive.close()
    print("PASS: 5728-class registry universe, 380 tables, weapon/accessory/training/character source fields and actual GE grants/tags")


if __name__ == "__main__":
    main()
