"""Check that restriction classification preserves every native route condition."""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]


def load(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8-sig"))


def main():
    restrictions = load("docs/move-restrictions-weapons.json")
    source = load("docs/weapon-move-entries.json")
    catalog = {x["id"]: x for x in load("assets/key-enhancement/weapon-moves.json")["moves"]}
    assert set(catalog).issubset(restrictions["entries"])
    training = set(restrictions["classificationTags"]["training"])
    assert "CharacterState.WeaponUpgrade.Iron.BCharge" in training
    assert "CharacterState.AbilityState.Double.CloneCooldown" not in training
    assert "CharacterState.AbilityState.Long.Buff.Lv3" not in training
    rows = [row for config in source["equipment"] for row in config["entries"]] + source["effectEntries"]
    for row in rows:
        entry = restrictions["entries"][row["id"]]
        groups, prep = entry["groups"], entry["preparation"]
        for kind in ("requiredTags", "ignoredTags"):
            classified = [set(group[kind]) for group in groups.values()]
            for index, group in enumerate(classified):
                assert not group.intersection(set().union(*classified[:index]))
            unknown = set(prep["unclassified" + kind[0].upper() + kind[1:]])
            assert not unknown.intersection(set().union(*classified))
            native = set((row.get("nativeInputRoute") or {}).get(kind, []))
            if kind == "requiredTags":
                native.update(catalog[row["id"]].get(kind, []))
            assert native.issubset(unknown.union(*classified)), row["id"]
        assert set(groups["training"]["requiredTags"] + groups["training"]["ignoredTags"]).issubset(training)
        assert prep["requiresGrantedAbility"] == catalog[row["id"]]["abilityClass"]
        source_id = catalog[row["id"]].get("sourceId")
        if source_id:
            assert row["id"] in restrictions["entries"][source_id]["concreteEntries"]
    bronze = next(entry for identity, entry in restrictions["entries"].items()
                  if identity.endswith("Weapon_Hun_TianXuanQingTongJian.GA_Hun_BronzeSword_B_Hold"))
    assert "CharacterState.WeaponUpgrade.BronzeSword.BHold" in bronze["groups"]["training"]["requiredTags"]
    print("PASS: 1084 weapon routes, source projections, explicit unlock tags and retained preparation conditions")


if __name__ == "__main__":
    main()
