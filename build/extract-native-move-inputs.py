"""Read native input-task pins and project reviewed preparation onto every entrance.

Reads cooked resources only. Archive keys remain in memory; no game process or
player save is opened. Release gates retain the original charge and attack chain.
"""
import argparse
import copy
import importlib.util
import json
import pathlib
import struct


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("native_input_combat", ROOT / "build/extract-combat-art-phases.py")
combat = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat)
INPUT_PREFIX = "/Script/SGameInputSystem."
FUNCTIONS = {
    INPUT_PREFIX + "AbilityTask_WaitSInputRelease.CreateTask": "release",
    INPUT_PREFIX + "AbilityTask_WaitSInputHold.CreateTask": "hold",
    INPUT_PREFIX + "AbilityTask_WaitSInputTrigger.CreateTask": "trigger",
    INPUT_PREFIX + "SGameInputComponent.IsHolding": "holding",
}
HOLD = "/PBZInput/InputActions/IA_AttackH_Hold.IA_AttackH_Hold"
SHADOW_HOLD = "/PBZInput/InputActions/IA_ShachiAbility_Hold.IA_ShachiAbility_Hold"
NATURAL = {"native_completion": True}

# Each owner was followed through its activation, release and end graphs.
# A helper that is never called does not create an input dependency.
REVIEWED = {
    "GA_Hun_GuiTouBlade_B_Hold_Start": (HOLD, NATURAL,
        "SuccessHold selects the original reinforced/unreinforced successor; release retains Min Duration."),
    "GA_Hun_FengLeiJian_B_Hold": (HOLD, NATURAL,
        "WaitSInputHold(0.5); native tick ends successfully only after the real Thunder charge tag."),
    "GA_Hun_BronzeSword_B_Hold": (HOLD, NATURAL,
        "WaitSInputHold(0.5) and native GainCharge select the charged release."),
    "GA_Hun_Iron_BCharge": (HOLD, NATURAL,
        "WaitSInputHold(0); ChargeEnd normally transfers to BJump; early release retains ChargeMinTime."),
    "GA_Hun_Horizontal_BladeWave": (HOLD, NATURAL,
        "WaitSInputRelease observes Hold after Charging; uninterrupted Main/DashIn launches the real wave."),
    "GA_Hun_PoJun_DB": (HOLD, NATURAL,
        "MinDuration checks the held input; MaxDuration selects the native final attack and strength branch."),
    "GA_Hun_ChiZheCanJian_DrinkBase": (HOLD, NATURAL,
        "Only Drink_Continuous registers ContinDrinkCheck with a positive JumpEndTime; its finite timeline completes drinking."),
    "GA_Hun_SlitheringSword_DA": (
        "/PBZInput/InputActions/IA_AttackL_Hold.IA_AttackL_Hold", {"timeline": "Loop"},
        "Release waits for native Main to reach Loop and then calls Go End through the original task."),
    "GA_Hun_ChiRen_C": (
        "/PBZInput/InputActions/IA_CombatArt_Hold.IA_CombatArt_Hold", NATURAL,
        "GainCharge increments ChargeCount; ChargeCount >= 3 automatically calls ToRelease."),
    "GA_Hun_YanYueDao_SpecialSkill": (HOLD, NATURAL,
        "Native checkpoints increment ChargeCount; ChargeCount >= MaxCount selects FastEnd."),
    "GA_JinGua_RoAttack": (SHADOW_HOLD, NATURAL,
        "HoldCheck increments ChargeCount; ChargeCount >= MaxCount selects FastEnd instead of Loop."),
    "GA_BrutalAxe_Normal": (SHADOW_HOLD, NATURAL,
        "MinDuration checks Hold; GainCharge and ToRelease events retain the normal/sprint charge progression."),
}
UNUSED_HELPERS = {
    "GA_CaoOuSiXian_Normal_01", "GA_HuangJinHuoChong_Normal_02",
    "GA_HuangJinHuoChong_Normal_04", "GA_JianKeZhaoJiLing_Normal_01",
    "GA_JinGua_Air", "GA_KuYeSiZhuChuan_Normal_01",
    "GA_ThunderSpear_Normal_01", "GA_ThunderSpear_Normal_02",
    "GA_ThunderSpear_Normal_03", "GA_XueSeLingDang_Normal_01",
}


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def package_path(path):
    return path.split(".")[0] if path else None


def class_path(path):
    return path + "." + path.rsplit("/", 1)[-1] + "_C"


def input_calls(package, symbols):
    end = struct.unpack_from("<I", package.data, 32)[0]
    pointers = { -slot - 1: symbols.resolve(package, struct.unpack_from(
        "<Q", package.data, package.import_offset + slot * 8)[0])
        for slot in range((end - package.import_offset) // 8) }
    result = []
    for pointer, function in pointers.items():
        if function not in FUNCTIONS:
            continue
        kind = FUNCTIONS[function]
        for name, payload in package.exports:
            for opcode in (0x1c, 0x46, 0x68):
                needle = bytes([opcode]) + struct.pack("<i", pointer)
                start = 0
                while True:
                    offset = payload.find(needle, start)
                    if offset < 0:
                        break
                    cursor = start = offset + len(needle)
                    row = {"export": name, "exportOffset": offset, "function": function}
                    if kind in ("release", "hold", "trigger"):
                        if payload[cursor:cursor + 1] != b"\x17":
                            raise ValueError("Unsupported input task owner: " + name)
                        cursor += 1
                    if kind == "release":
                        if payload[cursor:cursor + 1] != b"\x21":
                            raise ValueError("Unsupported release task instance name: " + name)
                        cursor += 9
                    if payload[cursor:cursor + 1] == b"\x20":
                        action = pointers[struct.unpack_from("<i", payload, cursor + 1)[0]]
                        row["inputAction"] = action + "." + action.rsplit("/", 1)[-1]
                        if kind == "hold":
                            if payload[cursor + 5:cursor + 6] != b"\x1e":
                                raise ValueError("Unsupported Hold duration: " + name)
                            row["holdSeconds"] = struct.unpack_from("<f", payload, cursor + 6)[0]
                    else:
                        row["inputArgument"] = "instance property"
                    result.append(row)
    return result


def extract(archive):
    symbols = combat.ScriptObjects(archive.paks)
    moves = {row["id"]: row for name in ("complete-moves", "weapon-moves", "shadow-moves")
             for row in read("assets/key-enhancement/" + name + ".json")["moves"]}
    weapon_audit = read("docs/complete-move-weapon-audit.json")
    shadow = read("docs/shadow-move-entries.json")
    abilities = {"ability:" + row["tag"] for row in read("prototype/input-router/actions.json")["abilities"]}
    sources = {}
    for path, row in weapon_audit["abilities"].items():
        for tag in row["effectiveTagCandidates"]:
            sources.setdefault("ability:" + tag, set()).add(path)
    for family in shadow["families"]:
        for row in family["abilities"]:
            for tag in row.get("tagCandidates", []):
                sources.setdefault("ability:" + tag, set()).add(package_path(row["abilityClass"]))
    for identity, move in moves.items():
        if move.get("nativeEntry"):
            sources[identity] = {package_path(move["abilityClass"])}
    sources = {identity: paths for identity, paths in sources.items() if identity in abilities or identity in moves}
    paths = set(weapon_audit["abilities"])
    for family in read("docs/complete-move-shadow-audit.json")["families"]:
        paths.update(row["package"] for row in family["associatedAbilities"])
    for kind, values in read("docs/ability-source-audit.json")["abilityUniverse"].items():
        if kind != "npc-referenced":
            paths.update(values)
    paths.update(package_path(row["abilityClass"]) for row in moves.values() if row.get("abilityClass"))
    cache = {}

    def inspect(path):
        if path in cache:
            return cache[path]
        if path.casefold() not in archive.packages:
            return None
        package = combat.e.Package(archive.resource(archive.packages[path.casefold()]))
        start = struct.unpack_from("<I", package.data, 32)[0]
        parent = None
        for i in range(len(package.exports)):
            _, cls, superclass = struct.unpack_from("<QQQ", package.data, start + i * 72 + 24)
            if symbols.resolve(package, cls).endswith("BlueprintGeneratedClass"):
                parent = symbols.resolve(package, superclass)
        row = {"parent": parent, "calls": input_calls(package, symbols)}
        cache[path] = row
        return row

    def chain(path):
        result = []
        while path and path not in result:
            row = inspect(path)
            if row is None:
                break
            result.append(path)
            path = row["parent"]
        return result

    for path in sorted(paths):
        if "/ActorData/Mobs/" not in path and "/ActorData/Boss" not in path:
            chain(path)
    configurations = []
    for row in read("docs/weapon-move-entries.json")["equipment"]:
        configurations.append((row["equipTags"], row["equipAbilities"] + row["owningAbilities"]))
    for row in shadow["configurations"]:
        configurations.append((row["equipTags"], row["equipAbilities"] + row["owningAbilities"]))

    def equipment_tags(path):
        tags = [set(tags) for tags, granted in configurations if path in {package_path(value) for value in granted}]
        return sorted(set.intersection(*tags)) if tags else []

    shadow_preparations = {}
    for identity, row in read("docs/move-restrictions-shadow.json")["entries"].items():
        prep = row.get("preparation", {})
        move = moves.get(identity, {})
        path = package_path(move.get("abilityClass") or prep.get("abilityClass"))
        if not path and prep.get("nativePreparationRecipeId") in moves:
            path = package_path(moves[prep["nativePreparationRecipeId"]].get("abilityClass"))
        if path and prep.get("nativeHoldInputs") and prep.get("automaticReleaseWhen"):
            shadow_preparations.setdefault(path, copy.deepcopy(prep))
    entries = {}
    associations = []
    for identity, candidates in sorted(sources.items()):
        variants = []
        for path in sorted(candidates):
            ancestry = chain(path)
            owner = next((owner for owner in ancestry if owner.rsplit("/", 1)[-1] in REVIEWED), None)
            prep = None
            if owner:
                name = owner.rsplit("/", 1)[-1]
                if name == "GA_Hun_ChiZheCanJian_DrinkBase" and not path.endswith("/GA_Hun_ChiZheCanJian_Drink_Continuous"):
                    continue
                action, condition, reason = REVIEWED[name]
                prep = {"nativeHoldInputs": [action], "automaticReleaseWhen": copy.deepcopy(condition)}
            elif path in shadow_preparations and any(cache[owner]["calls"] for owner in ancestry):
                original = shadow_preparations[path]
                prep = {key: copy.deepcopy(original[key]) for key in ("nativeHoldInputs", "automaticReleaseWhen")}
                owner = next(owner for owner in ancestry if cache[owner]["calls"])
                reason = "Original shadow preparation retains its native readiness and projectile initialization."
            if prep is None:
                continue
            prep["abilityClass"] = class_path(path)
            prep["requiredTags"] = equipment_tags(path)
            variants.append(prep)
            associations.append({"identity": identity, "ability": path, "inputOwner": owner,
                                 "ancestry": ancestry, "releaseRule": reason})
        if not variants:
            continue
        # Shared input tags must select the equipped class before starting any
        # held action. Exact-class moves and unique ability tags need no branch.
        if len(candidates) > 1:
            entries[identity] = {"preparation": {"nativeHoldVariants": variants}}
        else:
            prep = variants[0]
            prep.pop("requiredTags", None)
            entries[identity] = {"preparation": prep}
    calls = []
    for path, row in sorted(cache.items()):
        if not row["calls"]:
            continue
        name = path.rsplit("/", 1)[-1]
        status = "reviewed-native-lifecycle" if name in REVIEWED else "existing-shadow-preparation"
        if name in UNUSED_HELPERS:
            status = "unused-holding-helper"
        elif name == "GA_Hun_ChiZheCanJian_SWT":
            status = "unreachable-drink-check: literal 0 > 0"
        elif name == "BP_PlayerDodge_Base":
            status = "optional-continuous-dodge-mode"
        elif name == "GA_Hun_YanYueDao_C":
            status = "two-press-reaction-stance: FirstRelease then repress; native ToEnd exposed separately"
        elif name == "GA_Hun_Long_BCharge":
            status = "preparation-only: no independent input route; native BBB attack already exposed"
        elif any(FUNCTIONS[call["function"]] == "trigger" for call in row["calls"]) and not any(
                FUNCTIONS[call["function"]] in ("hold", "release", "holding") for call in row["calls"]):
            status = "auxiliary-trigger-event"
        calls.append({"package": path, "status": status, "calls": row["calls"]})
    return {"build": "CL-302256", "runtimeVerified": False,
            "evidenceType": "Cooked input-task arguments, complete catalog ancestry and reviewed native activation/release graphs",
            "entries": entries, "associations": associations, "inputCallAudit": calls,
            "coverage": {"scannedPackages": len(cache), "packagesWithInputCalls": len(calls),
                         "catalogEntrances": len(moves), "directAbilityTags": len(abilities),
                         "preparedIdentities": len(entries)},
            "limitations": ["Static resources establish input and lifecycle contracts; in-game animation and damage require runtime observation.",
                            "Optional dodge-mode and auxiliary interaction triggers retain their native physical-input semantics."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + option, type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "docs/native-move-inputs.json")
    args = parser.parse_args()
    archive = combat.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = extract(archive)
    finally:
        archive.close()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Read %(scannedPackages)d packages, %(packagesWithInputCalls)d input lifecycles; prepared %(preparedIdentities)d identities" % result["coverage"])


if __name__ == "__main__":
    main()
