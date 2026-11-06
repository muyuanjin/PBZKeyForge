"""Check conditional shadow followups against installed native routes and phases.

Requires original upgrades, predecessors and state; checks native timeline actions
and official Chinese move names. This does not launch the game.
"""
import argparse
import importlib.util
import json
import pathlib
import struct
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "prototype/input-router"))
import special_dash


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


combat = module("training_combat", ROOT / "build/extract-combat-art-phases.py")
localization = module("training_localization", ROOT / "build/extract-action-localization.py")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", type=pathlib.Path, required=True)
    parser.add_argument("--paks", type=pathlib.Path, required=True)
    parser.add_argument("--oodle", type=pathlib.Path, required=True)
    args = parser.parse_args()
    evidence = json.loads((ROOT / "docs/complete-move-shadow-training.json").read_text(encoding="utf-8"))
    catalog = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))
    assert evidence["build"] == catalog["build"] == "CL-302256"
    moves = {move["id"]: move for move in catalog["moves"]}
    assert len(evidence["trainingFollowups"]) == 8
    assert sum(len(row["recipes"]) for row in evidence["trainingFollowups"]) == 10
    archive = special_dash.e.Archive(args.game_exe, args.paks, args.oodle, evidence["build"])
    pak = localization.Pak(archive, special_dash.e.Reader)
    symbols, cache = combat.ScriptObjects(args.paks), {}

    def package(path):
        path = path.split(".")[0]
        if path not in cache:
            cache[path] = special_dash.e.Package(archive.resource(archive.packages[path.casefold()]))
        return cache[path]

    try:
        resource = next(entry for path, entry in pak.files.items()
                        if path.endswith("/Localization/Game/zh-Hans/Game.locres"))
        texts = localization.parse_locres(pak.read(resource), special_dash.e.Reader)
        for row in evidence["trainingFollowups"]:
            reference = row["localization"]
            assert texts[(reference["namespace"], reference["key"])] == row["officialName"]
            for route, expected in zip(row["routes"], row["recipes"]):
                binding = special_dash.BindingDocument(package(route["binding"]).exports[0][1],
                                                       package(route["binding"]).names)
                group = binding.items[route["group"]]
                assert group["ActionPriority"] == route["priority"]
                native = group["NormalData"][route["route"]]
                assert any(item["TriggerTag"] == route["tag"]
                           and item["RequiredTags"] == route["requiredTags"]
                           and item["IgnoredTags"] == route["ignoredTags"] for item in native), expected["id"]
                assert expected["requiredTags"] == route["requiredTags"]
                assert expected["ignoredTags"] == route["ignoredTags"]
                assert any(tag.startswith("CharacterState.WeaponUpgrade.") for tag in expected["requiredTags"])
                assert any(tag.startswith("Ability.Action.Hun.ShachiAbility.") for tag in expected["requiredTags"])
                actual = moves[expected["id"]]
                activation_tags = [tag for tag in expected["requiredTags"] if tag.startswith("Ability.Action.")]
                continuing_tags = [tag for tag in expected["requiredTags"] if not tag.startswith("Ability.Action.")]
                assert actual.get("activationRequiredTags", []) == activation_tags, expected["id"]
                assert actual["requiredTags"] == continuing_tags, expected["id"]
                assert set(actual["requiredTags"] + actual.get("activationRequiredTags", [])) == set(route["requiredTags"])
                for key in ("sourceId", "tag", "abilityClass", "inputAction", "ignoredTags", "priority",
                            "entryPoint", "activeBooleanProperties"):
                    assert actual.get(key) == expected.get(key), (expected["id"], key)
                assert not any(key in actual for key in ("cost", "bulletCost", "payCost", "grantTags", "setProperties"))
            asset = package(row["ability"])
            for expected in row["timelineEvidence"]:
                actual = combat.timeline(asset, expected["exportIndex"])
                assert actual["name"] == expected["name"]
                assert len(actual["actions"]) == len(expected["actions"])
                for action, previous in zip(actual["actions"], expected["actions"]):
                    assert json.loads(json.dumps(action)) == {key: value for key, value in previous.items() if key != "class"}
                    start = struct.unpack_from("<I", asset.data, 32)[0]
                    cls = struct.unpack_from("<Q", asset.data, start + (action["actionPointer"] - 1) * 72 + 32)[0]
                    assert symbols.resolve(asset, cls) == previous["class"]
            if row["weapon"] == "HuoLiuXing":
                assert "ToRelease" in [name for name, _ in asset.exports]
            if row["weapon"] == "ThunderSpear":
                branch = row["nativeBranch"]
                assert branch["callback"] in [name for name, _ in asset.exports]
                assert branch["event"] in asset.names and "NearDistanceGate" in asset.names
                assert set(row["nativeTimelines"]) == {"Main", "NearTimeline"}
                assert "Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_03" in row["recipes"][0]["requiredTags"]
                assert "Ability.Action.ShaqiReinforced" in row["recipes"][0]["requiredTags"]
            if row["weapon"] == "ShachiHook" and row["tag"].endswith(".Drag"):
                predecessors = [{tag for tag in recipe["requiredTags"]
                                 if tag.startswith("Ability.Action.Hun.ShachiAbility.ShachiHook.")}
                                for recipe in row["recipes"]]
                assert predecessors == [{"Ability.Action.Hun.ShachiAbility.ShachiHook." + stage}
                                        for stage in ("C", "CC", "CCC")]
        print("PASS: 8 official training followups, 10 native conditional routes, predecessor alternatives and attack timelines")
    finally:
        pak.stream.close()
        archive.close()


if __name__ == "__main__":
    main()
