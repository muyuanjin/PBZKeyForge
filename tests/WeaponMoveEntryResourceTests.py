"""Verify exact weapon entries against installed grants, maps and inheritance."""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("weapon_entries", ROOT / "build/extract-weapon-move-entries.py")
entries = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(entries)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", required=True, type=pathlib.Path)
    parser.add_argument("--paks", required=True, type=pathlib.Path)
    parser.add_argument("--oodle", required=True, type=pathlib.Path)
    args = parser.parse_args()
    evidence = entries.read_json(ROOT / "docs/weapon-move-entries.json")
    catalog = entries.read_json(ROOT / "assets/key-enhancement/weapon-moves.json")
    audit = entries.read_json(ROOT / "docs/complete-move-weapon-audit.json")
    costs = entries.read_json(ROOT / "assets/key-enhancement/resource-costs.json")
    moves = {move["id"]: move for move in catalog["moves"]}
    assert len(moves) == len(catalog["moves"])
    assert evidence["moves"] == catalog["moves"]
    assert {row["configId"] for row in evidence["equipment"]} == {row["equipmentId"] for row in audit["equipment"]}
    archive = entries.combat.e.Archive(args.game_exe, args.paks, args.oodle, catalog["build"])
    symbols, cache = entries.combat.ScriptObjects(args.paks), {}

    def inspect(path):
        path = path.split(".")[0]
        if path not in cache:
            cache[path] = entries.special.inspect(archive, symbols, path)
        return cache[path]

    def ancestors(path):
        chain = []
        while path and path.casefold() in archive.packages:
            chain.append(path)
            parent = inspect(path)[2]
            path = parent.split(".")[0] if parent else None
        return chain

    resource_roots = {"bp_heavyattack_base": {"shachi"}, "bp_heavyattack_air_base": {"shachi"},
                      "bp_combatart_base": {"ultimate"}, "bp_shachiability_base": {"shachi", "spirit"},
                      "ga_hun_shuangyue_db": {"shachi", "weapon_charge"}}
    try:
        association_count = 0
        for config in evidence["equipment"]:
            package = inspect(config["configId"])[0]
            actual = entries.combat.equipment_fields(package)
            for field in ("equipTags", "equipAbilities", "owningAbilities"):
                assert actual[field] == config[field], (config["configId"], field)
            granted = {path for path in actual["equipAbilities"] + actual["owningAbilities"] if path}
            classified = {row["ability"] for row in config["entries"] + config["excluded"]}
            assert granted == classified, config["configId"]
            maps = entries.input_binding_maps(package, inspect)
            assert json.loads(json.dumps(maps)) == config["inputBindingMaps"]
            for mapping in maps:
                removed, count = struct.unpack_from("<ii", package.exports[0][1], mapping["offset"])
                assert removed == 0 and count == len(mapping["pairs"])
            for record in config["entries"]:
                move = moves[record["id"]]
                association_count += 1
                assert move["abilityClass"].split(".")[0] == record["ability"]
                assert record["ability"] in granted
                assert move["activateByClass"] and not move.get("force")
                assert move["requiredTags"] and set(move["requiredTags"]).issubset(actual["equipTags"])
                assert move["usageKey"] == "moves.nativeentrance.usage"
                assert len(move["weaponLabels"]) == 15
                assert ancestors(record["ability"]) == record["ancestry"]
                assert any(row["name"] == move["abilityClass"].rsplit(".", 1)[-1] for row in inspect(record["ability"])[1])
                assert record["tag"] in entries.combat.cdo_tag_candidates(inspect(record["tagEvidenceOwner"])[0])
                assert move["inputAction"].split(".")[0].casefold() in archive.packages
                route = record["nativeInputRoute"]
                if route:
                    document = entries.special_dash.BindingDocument(inspect(route["binding"])[0].exports[0][1], inspect(route["binding"])[0].names)
                    found = False
                    for item in document.items:
                        blocks = [item.get("NormalData")] + [pair["value"] for pair in (item.get("TagConditionData") or {}).get("pairs", [])]
                        for block in blocks:
                            if block:
                                found |= any(trigger.get("TriggerTag") == record["tag"] for kind in ("Actions", "Combos", "InterruptLower") for trigger in block.get(kind) or [])
                    assert found, (record["ability"], record["tag"])
                    assert move["inputAction"] == route["inputAction"] and move["priority"] == route["priority"]
                    assert set(move["activationRequiredTags"]) == {tag for tag in route["requiredTags"] if not tag.startswith("Ability.Action.")}
                expected = set()
                for owner in record["ancestry"]:
                    expected.update(resource_roots.get(owner.rsplit("/", 1)[-1].lower(), set()))
                assert expected.issubset(costs["classes"].get(move["abilityClass"], [])), (move["id"], expected)
        chi = next(config for config in evidence["equipment"] if config["configId"].endswith("/Weapon_Hun_ChiRen"))
        names = {row["ability"].rsplit("/", 1)[-1] for row in chi["entries"]}
        assert {"GA_Hun_ChiRen_" + suffix for suffix in ("A", "AA", "AAA", "AAAA", "B", "BB", "BBB", "AB", "ABB", "AAB", "AABB", "AAAB", "AAABB", "AAAAB", "AAAABB", "BD", "BSup")}.issubset(names)
        assert "BP_SwitchAttack_DefaultUI" not in names and "GA_Hun_ChiRen_C" not in names
        assert len(evidence["effectEntries"]) == 2
        for row in evidence["effectEntries"]:
            move = moves[row["id"]]
            components, unknowns = entries.ability_sources.effect_components(inspect(row["effect"])[0], symbols)
            assert not unknowns and components == row["components"]
            assert row["grant"] in [grant for component in components for grant in component.get("grants", [])]
            assert ancestors(row["ability"]) == row["ancestry"]
            assert move["requiredTags"] == row["nativeInputRoute"]["requiredTags"]
            assert "CharacterState.AbilityState.GlobalActionComboAvaliable" in move["requiredTags"]
            assert move["activateByClass"] and move["nativeEntry"]
            assert costs["classes"][move["abilityClass"]] == ["shachi"]
            package, exports, _ = inspect(row["ability"])
            assert not any(export["class"] == "/Script/CoreUObject.Function" for export in exports)
            assert any("Bullet" in export["class"] for export in exports)
        assert len(evidence["effectExclusions"]) == 3
        for row in evidence["effectExclusions"]:
            actual = entries.special.read_resources(archive, symbols, [row["ability"]])[row["ability"]]
            assert actual["parent"] == row["parent"] and actual["functions"] == row["functions"]
            if row["reason"] == "requires native damage event and target payload":
                assert any(function["name"] == "K2_ActivateAbilityFromEvent" for function in row["functions"])
                assert any("DamageTakenPayload" in call for function in row["functions"] for call in function["calls"])
            else:
                assert any("WaitGameplayEvent" in call for function in row["functions"] for call in function["calls"])
        for row in evidence["resourceCostChecks"]:
            assert ancestors(row["ability"]) == row["ancestry"]
            assert inspect(row["ability"])[2] == row["parent"]
        charge = next(row for row in evidence["resourceCostChecks"] if row["ability"].endswith("/GA_Hun_Long_BCharge"))
        assert any(ref["tag"] == "Ability.Action.Hun.Heavy.Long.BBB" for function in charge["localFunctions"] for ref in function["tagReferences"])
    finally:
        archive.close()
    print("PASS: %d exact weapon entries, %d equipment associations, native maps, unlock conditions and resource inheritance" % (len(moves), association_count))


if __name__ == "__main__":
    main()
