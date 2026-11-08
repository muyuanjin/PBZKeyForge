"""Check attack insight coverage and reviewed coefficients for every complete move."""
import json
import math
import pathlib


ROOT = pathlib.Path(__file__).resolve().parents[1]


def load(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8"))


def close(actual, expected):
    return len(actual) == len(expected) and all(math.isclose(a, b, abs_tol=1e-6) for a, b in zip(actual, expected))


def main():
    rows = load("assets/key-enhancement/move-insights.json")
    catalog = []
    for name in ("complete-moves", "weapon-moves", "shadow-moves"):
        catalog.extend(load("assets/key-enhancement/" + name + ".json")["moves"])
    identities = [move["id"] for move in catalog]
    insights = {row["id"]: row for row in rows["moves"]}

    assert len(identities) == 1250
    assert len(identities) == len(set(identities))
    assert set(insights) == set(identities)
    assert rows["coverage"] == {
        "moves": 1250,
        "movesWithAttackNodes": 1234,
        "movesWithCoefficients": 1229,
        "physicalCoefficients": 1225,
        "poiseCoefficients": 1197,
        "movesWithUnresolvedBulletData": 3,
        "readBulletPackages": 2387,
    }

    no_nodes = [row for row in rows["moves"] if row["attackNodes"] == 0]
    assert len(no_nodes) == 16
    assert all("physical" not in row and "poise" not in row for row in no_nodes)
    assert len([row for row in rows["moves"] if not row.get("physical")
                and any(row.get(key) for key in ("element", "fire", "ice", "lightning", "chaos", "anger", "true"))]) == 4

    baiying = insights["move:Ability.Action.Hun.BaiYing.Assault"]
    assert baiying["attackNodes"] == 1
    assert close(baiying["physical"], [1.8]) and close(baiying["poise"], [100])

    snake = insights["move:Ability.Action.Hun.Light.SnakeDual.A_Deflect"]
    assert snake["attackNodes"] == 1
    assert close(snake["physical"], [1]) and close(snake["poise"], [100])

    bronze = insights["move:Ability.Action.Hun.Heavy.BronzeSword.BHold.Release2"]
    assert bronze["attackNodes"] == 3
    assert close(bronze["physical"], [0.852632, 1.08, 1.172369])
    assert close(bronze["poise"], [25.263159, 34.736843, 40])

    jade = insights["move:GA_Hun_Jade_C_Range.CombatArt.Final"]
    assert jade["attackNodes"] == 2
    assert close(jade["physical"], [0.883436, 1.932515])
    assert close(jade["poise"], [49.079754, 107.361961])

    unresolved = {
        "move:Weapon.Weapon_Hun_ShuangYue.GA_Hun_ShuangYue_DB",
        "move:Weapon.Weapon_Hun_ShuangYue.GA_Hun_ShuangYue_DB_SWT",
        "move:WeaponSpecial.GA_Hun_ShuangYue_DB_SWT",
    }
    assert {row["id"] for row in rows["moves"] if row.get("unresolvedBulletData")} == unresolved
    assert all(row["unresolvedBulletData"] ==
               ["/PBZCombatAsset/ActorData/Players/Hun/BulletData/ShuangYue/B_Hun_ShuangYue_DB_Projectile_Phase1"]
               for row in rows["moves"] if row["id"] in unresolved)
    print("Move insight resource: PASS (%d moves, %d attack nodes)" %
          (len(insights), rows["coverage"]["movesWithAttackNodes"]))


if __name__ == "__main__":
    main()
