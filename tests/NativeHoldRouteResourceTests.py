"""Verify prepared Hold inputs track native state without starting another attack."""
import argparse
import importlib.util
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("hold_routes", ROOT / "build/extract-weapon-move-entries.py")
weapons = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(weapons)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    restrictions = weapons.read_json(ROOT / "assets/key-enhancement/move-restrictions.json")
    moves = weapons.read_json(ROOT / "assets/key-enhancement/complete-moves.json")["moves"]
    holds = {path.split(".")[0] for entry in restrictions["entries"].values()
             for path in entry["preparation"].get("nativeHoldInputs", [])}
    holds.update(move["prepareInput"].split(".")[0] for move in moves if move.get("prepareInput"))
    assert holds == {"/PBZInput/InputActions/IA_ShachiAbility_Hold", "/PBZInput/InputActions/IA_AttackH_Hold",
                     "/PBZInput/InputActions/IA_AttackL_Hold", "/PBZInput/InputActions/IA_CombatArt_Hold"}
    archive = weapons.combat.e.Archive(args.game_exe, args.paks, args.oodle, restrictions["build"])
    symbols = weapons.combat.ScriptObjects(args.paks)
    cache = {}

    def inspect(path):
        if path not in cache:
            cache[path] = weapons.special.inspect(archive, symbols, path)
        return cache[path]

    try:
        package = inspect("/PBZCombatAsset/ActorData/Players/Hun/InputConfig_Hun")[0]
        reader = weapons.combat.e.Reader(package.exports[0][1], package.names)
        header = reader.header()
        assert header[0] is False and header[1] is False
        input_sets = [package.object_name(reader.number()) for _ in range(reader.number())]
        assert input_sets and reader.number() == 0
        bindings = {}
        for _ in range(reader.number()):
            action, binding = reader.number(), reader.number()
            bindings[package.object_name(action)] = binding
        for action in holds:
            assert action in bindings and bindings[action] == 0, "Hold has a native attack binding: " + action
        configs = {path for entry in restrictions["entries"].values()
                   if entry["preparation"].get("nativeHoldInputs")
                   for path in entry["preparation"].get("equipmentConfigs", [])}
        for move in moves:
            if move.get("prepareInput"):
                configs.update(restrictions["entries"][move["id"]]["preparation"].get("equipmentConfigs", []))
        override_count = 0
        for config in sorted(configs):
            for mapping in weapons.input_binding_maps(inspect(config)[0], inspect):
                for action, binding in mapping["pairs"]:
                    assert action not in holds, "Equipment adds an attack handler to a prepared Hold: " + config + ": " + binding
                    override_count += 1
        print("PASS: %d prepared Hold inputs have null native action bindings; %d equipment configurations and %d override routes add no competing Hold activation"
              % (len(holds), len(configs), override_count))
    finally:
        archive.close()


if __name__ == "__main__":
    main()
