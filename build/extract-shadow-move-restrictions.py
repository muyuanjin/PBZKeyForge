"""Extract shadow move qualifications and exact native input-release task pins."""
import argparse
import importlib.util
import json
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("shadow_names", ROOT / "build/extract-game-move-names.py")
names = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(names)
combat = names.shadow.combat


def release_tasks(archive, symbols, path):
    package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
    end = struct.unpack_from("<I", package.data, 32)[0]
    pointers = {}
    for slot in range((end - package.import_offset) // 8):
        pointer = -slot - 1
        pointers[pointer] = symbols.resolve(package, struct.unpack_from("<Q", package.data, package.import_offset + slot * 8)[0])
    functions = ("/Script/SGameInputSystem.AbilityTask_WaitSInputRelease.CreateTask",
                 "/Script/SGameInputSystem.SGameInputComponent.IsHolding")
    result = []
    for pointer, resolved in pointers.items():
        if resolved not in functions:
            continue
        for export, payload in package.exports:
            for opcode in (0x1c, 0x46, 0x68):
                needle = bytes([opcode]) + struct.pack("<i", pointer)
                start = 0
                while True:
                    at = payload.find(needle, start)
                    if at < 0:
                        break
                    start = at + len(needle)
                    cursor = start
                    task_name = None
                    if resolved == functions[0]:
                        # CreateTask(Self, FName TaskInstanceName, InputAction).
                        if payload[cursor:cursor + 2] != b"\x17\x21":
                            raise ValueError("Unsupported native release task arguments: " + path + ":" + export)
                        name_index, name_number = struct.unpack_from("<II", payload, cursor + 2)
                        task_name = package.names[name_index]
                        cursor += 10
                    if payload[cursor] != 0x20 or payload[cursor + 5] != 0x16:
                        raise ValueError("Expected InputAction pin in native release task: " + path)
                    action_pointer = struct.unpack_from("<i", payload, cursor + 1)[0]
                    action = pointers[action_pointer]
                    if not action.startswith("/") or "/Input" not in action:
                        raise ValueError("Expected native InputAction resource: " + action)
                    result.append({"package": path, "export": export, "exportOffset": at,
                                   "function": resolved, "taskInstanceName": task_name,
                                   "inputAction": action + "." + action.rsplit("/", 1)[-1],
                                   "inputPointer": action_pointer})
    return result


def classify(tag):
    if tag.startswith("CharacterState.WeaponUpgrade."):
        return "training"
    if tag.startswith(("Weapon.", "Ability.Property.Weapon.")):
        return "equipment"
    if tag.startswith("CharacterState.Space."):
        return "space"
    if "Stance" in tag or "HoldingStance" in tag:
        return "stance"
    return "prerequisite"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "docs/move-restrictions-shadow.json")
    args = parser.parse_args()
    load = lambda path: json.loads((ROOT / path).read_text(encoding="utf8"))
    audit = load("docs/complete-move-shadow-audit.json")
    catalog_tags = {row["tag"] for row in load("prototype/input-router/actions.json")["abilities"]}
    native_moves = load("assets/key-enhancement/shadow-moves.json")["moves"]
    recipes = load("assets/key-enhancement/complete-moves.json")["moves"]
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, audit["build"])
    symbols = combat.ScriptObjects(args.paks)
    result, task_evidence = {}, []
    try:
        for family in audit["families"]:
            for ability in family["associatedAbilities"]:
                if ability["role"] != "active-or-contextual":
                    continue
                path = ability["package"]
                tasks = release_tasks(archive, symbols, path)
                task_evidence.extend(tasks)
                tasks = [task for task in tasks if task["inputAction"].endswith("IA_ShachiAbility_Hold.IA_ShachiAbility_Hold")]
                moves = [move for move in native_moves if move["abilityClass"].split(".")[0] == path]
                identities = [(move["id"], move["tag"]) for move in moves]
                # CDO tag references also include blocked and prerequisite
                # abilities. Reviewed exact-class recipes establish which
                # direct identity owns this input lifecycle.
                reviewed = {move["tag"] for move in recipes if move.get("abilityClass", "").split(".")[0] == path and move.get("tag")}
                identities += [("ability:" + tag, tag) for tag in ability["cdoAbilityTagReferences"] if tag in catalog_tags and tag in reviewed and tag.startswith("Ability.Action.Hun.ShachiAbility.") and tag != names.shadow.DEFAULT]
                for identity, tag in identities:
                    routes = [row for row in family["inputRoutes"] if row["tag"] == tag]
                    groups = {}
                    for mode in ("requiredTags", "ignoredTags"):
                        for route_tag in dict.fromkeys(tag for row in routes for tag in row[mode]):
                            group = groups.setdefault(classify(route_tag), {"requiredTags": [], "ignoredTags": []})
                            group[mode].append(route_tag)
                    item = {"groups": groups, "preparation": {}, "evidence": {"abilityClasses": [ability["class"]], "family": family["id"]}}
                    if tasks:
                        item["preparation"]["nativeHoldInputs"] = list(dict.fromkeys(task["inputAction"] for task in tasks))
                        compatible = [move for move in recipes if move.get("abilityClass", "").split(".")[0] == path
                                      and move.get("entryWhen") and move.get("prepareInput")]
                        if compatible:
                            first = min(compatible, key=lambda row: row["entryWhen"].get("minimum_time", 0))
                            item["preparation"]["automaticReleaseWhen"] = first["entryWhen"]
                            item["preparation"]["nativePreparationRecipeId"] = first["id"]
                    if identity in result:
                        previous = result[identity]
                        previous["evidence"]["abilityClasses"] = list(dict.fromkeys(previous["evidence"]["abilityClasses"] + [ability["class"]]))
                        for key, values in groups.items():
                            group = previous["groups"].setdefault(key, {"requiredTags": [], "ignoredTags": []})
                            for mode in ("requiredTags", "ignoredTags"):
                                group[mode] = list(dict.fromkeys(group[mode] + values[mode]))
                        preparation = previous["preparation"]
                        if tasks:
                            preparation["nativeHoldInputs"] = list(dict.fromkeys(preparation.get("nativeHoldInputs", []) + item["preparation"]["nativeHoldInputs"]))
                        if preparation.get("automaticReleaseWhen") != item["preparation"].get("automaticReleaseWhen"):
                            preparation.pop("automaticReleaseWhen", None)
                            preparation.pop("nativePreparationRecipeId", None)
                    else:
                        result[identity] = item
    finally:
        archive.close()
    output = {"schemaVersion": 1, "build": audit["build"], "entries": result,
              "nativeReleaseTaskEvidence": task_evidence}
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print("Shadow restrictions: %d entries, %d native input-release tasks" % (len(result), len(task_evidence)))


if __name__ == "__main__":
    main()
