"""Check configured speed windows against the installed cooked attack actions."""
import argparse
import importlib.util
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_phases", ROOT / "build/extract-combat-art-phases.py")
phases = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(phases)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=Path, required=True)
    parser.add_argument("--paks", type=Path, required=True)
    parser.add_argument("--oodle", type=Path, required=True)
    parser.add_argument("--build", default="CL-302256")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    catalog = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))
    audit = json.loads((ROOT / "docs/complete-move-weapon-audit.json").read_text(encoding="utf-8"))
    if catalog["build"] != args.build or audit["build"] != args.build:
        raise ValueError("Catalog and equipment index must belong to the selected resource build")
    targets = [move for move in catalog["moves"] if move.get("phaseWindow") or move.get("phaseWindows")]
    assert len(targets) == 36, "The combat-art speed-window inventory changed"
    assert sum(len(move.get("phaseWindows", [move.get("phaseWindow")])) for move in targets) == 38
    archive = phases.e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        equipment = [item["equipmentId"] for item in audit["equipment"]]
        extracted = phases.extract(archive, equipment)
        abilities = {item["package"]: item for item in extracted["records"]}
        results = []
        for move in targets:
            ability = abilities[move["abilityClass"].rsplit(".", 1)[0]]
            for window in move.get("phaseWindows", [move.get("phaseWindow")]):
                timeline = next(item for item in ability["timelines"] if item["name"] == window["timeline"])
                attacks = [item for item in timeline["actions"]
                           if item.get("directAttackAction")
                           and item["startTime"] + item["duration"] >= window["entry_time"]]
                assert attacks, "No retained attack actions: " + move["id"]
                start = min(max(window["entry_time"], item["startTime"]) for item in attacks)
                end = max(item["startTime"] + item["duration"] for item in attacks)
                assert window["entry_time"] <= start, "Entry starts after retained attacks: " + move["id"]
                assert abs(start - window["attack_start"]) < 1e-6, "Startup boundary differs from cooked attacks: " + move["id"]
                assert abs(end - window["attack_end"]) < 1e-6, "Recovery boundary differs from cooked attacks: " + move["id"]
                results.append({"id": move["id"], "ability": ability["package"], "timeline": timeline["name"],
                                "attackStart": start, "attackEnd": end, "retainedAttackActions": len(attacks)})
        zhanma = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_DB"
        package = phases.e.Package(archive.resource(archive.packages[zhanma.casefold()]))
        index = next(i for i, (name, _) in enumerate(package.exports) if name == "Main")
        timeline = phases.timeline(package, index)
        attacks = [item for item in timeline["actions"] if "Bullet" in item["action"]]
        start = min(item["startTime"] for item in attacks)
        assert abs(start - 1.3485949039459229) < 1e-9, "Zhan Ma charge startup boundary changed"
        results.append({"id": "ability:Ability.Action.Hun.Heavy.ZhanMa.DB", "ability": zhanma,
                        "timeline": "Main", "attackStart": start, "retainedAttackActions": len(attacks)})
    finally:
        archive.close()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"build": args.build, "evidenceType": "installed cooked timelines and attack actions",
                                      "runtimeVerified": False, "windows": results}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("TimelineSpeedResourceTests PASS: 38 combat-art windows across 36 phases and Zhan Ma charge match installed attack actions")


if __name__ == "__main__":
    main()
