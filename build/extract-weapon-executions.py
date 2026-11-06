"""Decode native weapon ExecutionAnims entries and their actual channel/type fields."""
import argparse
import collections
import importlib.util
import json
import pathlib
import struct


SPEC = importlib.util.spec_from_file_location("combat_art", pathlib.Path(__file__).with_name("extract-combat-art-phases.py"))
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)
e = combat_art.e

ENTRY_FIELDS = ["channel", "executionType", "executionAbility", "executedAbility",
                "executorRequiredTags", "executorIgnoredTags", "targetRequiredTags", "targetIgnoredTags",
                "requiresNavigation", "requiresEquippedWeapon", "continuousExecutionTargetingType"]


def gameplay_tag(reader):
    header = reader.header()
    if header in ({}, {0: True}):
        return None
    if header != {0: False}:
        raise ValueError("Unsupported GameplayTag layout")
    return reader.name()


def execution_entry(reader, package):
    result = {"exportOffset": reader.position}
    fields = reader.header()
    if not fields or any(field > 10 for field in fields):
        raise ValueError("Unsupported PBZExecutionAnimEntry fields")
    for field, zero in fields.items():
        name = ENTRY_FIELDS[field]
        if zero:
            result[name] = [] if field in (4, 5, 6, 7) else False if field in (8, 9) else None
        elif field == 0:
            result[name] = reader.name()
        elif field in (1, 10):
            result[name] = gameplay_tag(reader)
        elif field in (2, 3):
            pointer = reader.number()
            if pointer >= 0:
                raise ValueError("Expected imported execution ability class")
            result[name] = package.object_name(pointer)
        elif field in (4, 5, 6, 7):
            count = reader.number()
            if count < 0 or count > 64:
                raise ValueError("Unsupported execution tag count")
            result[name] = [reader.name() for _ in range(count)]
        elif field in (8, 9):
            value = reader.number("<B")
            if value not in (0, 1):
                raise ValueError("Invalid execution boolean")
            result[name] = bool(value)
    if result.get("channel") not in ("Execution", "SpecialAttack", "ShachiWeapon"):
        raise ValueError("Not an execution channel entry")
    if not (result.get("executionType") or "").startswith("Execution.Type."):
        raise ValueError("Not an execution type entry")
    if not (result.get("executionAbility") or "").startswith("/"):
        raise ValueError("Execution entry has no ability class")
    result["endOffset"] = reader.position
    return result


def execution_array(package, property_index):
    data = package.exports[0][1]
    fields = e.Reader(data, package.names).header()
    if property_index not in fields or fields[property_index]:
        return {"fieldIndex": property_index, "entries": [], "serialized": property_index in fields}
    matches = []
    # The property is known from the derived/native schemas. Locate its full
    # variable-length array structurally; names alone are insufficient. Require
    # exactly one complete array of typed PBZExecutionAnimEntry values.
    for offset in range(len(data) - 4):
        count = struct.unpack_from("<i", data, offset)[0]
        if count < 1 or count > 32:
            continue
        reader = e.Reader(data, package.names)
        reader.position = offset + 4
        try:
            entries = [execution_entry(reader, package) for _ in range(count)]
            matches.append({"fieldIndex": property_index, "arrayOffset": offset, "endOffset": reader.position,
                            "entries": entries, "serialized": True})
        except (ValueError, IndexError, struct.error):
            continue
    # A suffix also looks like a smaller array if an earlier int happens to be
    # its count. Only the containing maximal array is the native property.
    maximal = [item for item in matches if not any(other["arrayOffset"] < item["arrayOffset"] and other["endOffset"] >= item["endOffset"] for other in matches)]
    if len(maximal) != 1:
        raise ValueError("Expected one structurally decoded ExecutionAnims array, found " + str(len(maximal)))
    return maximal[0]


def blueprint_properties(package):
    data = package.exports[0][1]
    result = []
    for position in range(len(data) - 47):
        type_index, number, name_index, name_number = struct.unpack_from("<IIII", data, position)
        if type_index >= len(package.names) or name_index >= len(package.names) or number or name_number:
            continue
        property_type = package.names[type_index]
        if not property_type.endswith("Property") or struct.unpack_from("<I", data, position + 16)[0] != 1:
            continue
        if data[position + 28:position + 31] == bytes.fromhex("050001"):
            result.append({"name": package.names[name_index], "type": property_type, "schemaOffset": position})
    return result


def extract(archive, equipment):
    result = []
    for config in equipment:
        path = config["equipmentId"]
        package = e.Package(archive.resource(archive.packages[path.casefold()]))
        is_temporary = config.get("registryClass") == "PBZ_TempWeaponConfig_C" or "/TempWeapon/" in path
        # Main has 25 Blueprint properties, temporary has 24. Native
        # SoftCollisionConfig precedes the ExecutionAnims array.
        index = 20 if config.get("registryClass") == "PBZ_SubWeaponConfig_C" else 25 if is_temporary else 26
        array = execution_array(package, index)
        for entry in array["entries"]:
            entry["role"] = ("ordinary-combat-execution" if entry["channel"] == "Execution" and entry["executionType"] in ("Execution.Type.Combat", "Execution.Type.CombatFar")
                             else "special-attack" if entry["channel"] == "SpecialAttack"
                             else "conditional-counter" if entry["executionType"].endswith("TigerCannon.Counter")
                             else "native-shadow-execution")
        result.append({"equipmentId": path, "registryClass": config.get("registryClass"), "executionAnims": array})
    return {"build": "CL-302256", "runtimeVerified": False,
            "evidenceType": "Native PBZExecutionAnimEntry property schema and complete serialized typed arrays",
            "schema": {"entryFields": ENTRY_FIELDS, "nativePropertyArray": "0x14c7248e0",
                       "nativeStructConstructor": "0x147d6b7b0", "nativeStructParameters": "0x14dc2f618",
                       "weaponNativeProperties": "0x14c73ce80; SoftCollisionConfig then ExecutionAnims"},
            "counts": {"equipment": len(result), "entries": sum(len(row["executionAnims"]["entries"]) for row in result)},
            "equipment": result}


def break_attack_recipes(result, catalog):
    recipes = []
    for row in result["equipment"]:
        for entry in row["executionAnims"]["entries"]:
            if entry["executionType"] not in ("Execution.Type.Combat.BreakAct.Iron", "Execution.Type.Combat.BreakAct.Jigsaw"):
                continue
            weapon = entry["executionType"].rsplit(".", 1)[-1]
            labels = next(move["weaponLabels"] for move in catalog["moves"]
                          if move.get("abilityClass", "").split(".", 1)[0].endswith("/GA_Hun_" + weapon + "_C"))
            recipes.append({"id": "move:Ability.Action.Hun." + weapon + ".BreakAct",
                            "sourceId": "ability:Ability.Action.Execution.Channel.SpecialAttack",
                            "tag": "Ability.Action.Execution.Channel.SpecialAttack",
                            "executionChannel": entry["channel"], "executionType": entry["executionType"],
                            "inputAction": "/PBZInput/InputActions/IA_AttackH_Press.IA_AttackH_Press",
                            "requiredTags": ["Weapon.Type." + weapon] + entry.get("executorRequiredTags", []),
                            "priority": -5, "code": weapon + " · BreakAct",
                            "labelParts": ["catalog.specialattack"], "weaponLabels": labels,
                            "category": "weapons/special", "usageKey": "moves.specialexecution.usage",
                            "searchAliases": [weapon, "BreakAct", entry["executionType"], entry["executionAbility"].rsplit("/", 1)[-1]]})
    assert len(recipes) == 2
    return recipes


def markdown(result):
    rows = result["equipment"]
    counts = collections.Counter(entry["role"] for row in rows for entry in row["executionAnims"]["entries"])
    lines = ["# 武器处决与特殊攻击入口", "", "CL-302256 的 40 个主/临时武器配置及全部 46 个影之武装配置，共解出 123 条原生 `ExecutionAnims` 条目。按原生 `PBZExecutionAnimEntry` 的 11 个字段读取真实 Channel、ExecutionType、执行/被执行能力及双方标签条件。逐条数据见 [JSON](weapon-execution-audit.json)。", "",
             "| 入口 | 条目 | 覆盖 |", "| --- | ---: | --- |",
             "| Execution 普通近/远处决 | %d | 原 IA_Execution / Execution 通道 |" % counts["ordinary-combat-execution"],
             "| SpecialAttack 专属攻击 | %d | 白影突袭、残钢刃破招、断肢锯破招 |" % counts["special-attack"],
             "| ShachiWeapon 普通/条件处决 | %d | 原影之武装输入与 ShachiWeapon 通道 |" % counts["native-shadow-execution"],
             "| ShachiWeapon 虎炮反击 | %d | 单独要求 Counter 类型 |" % counts["conditional-counter"], "",
             "SpecialAttack 的三项分别为白影 `Execution.Type.CombatFar.Assault.BaiYing`、残钢刃 `Execution.Type.Combat.BreakAct.Iron`、断肢锯 `Execution.Type.Combat.BreakAct.Jigsaw`。它们通过原 `GA_PerformSpecialAttack`、`IA_AttackH_Press`、优先级 -5 请求真实目标，并要求已解析类型与绑定一致；普通 Execution 通道无法替代。残钢刃另要求 `CharacterState.WeaponUpgrade.Iron.CanBreakAct`；白影另要求升级及目标冰冻条件。", "",
             "Execution 的 91 条均为普通近/远成对处决，执行方与被执行方身份均记录，原通道保留目标选择、导航与初始化。4 个临时武器没有自有 ExecutionAnims 条目。", "",
             "影之武装 29 条全部使用 ShachiWeapon 通道，包含飞刀近/远处决、血滴子普通/煞气处决、游龙叉水下/煞气及互动变体；它们由原通道的实际类型选择与目标条件决定。虎炮 Counter 与普通 Execution 类型不同，专属绑定明确检查 Counter。", "",
             "## 已知边界", "", "数组按照原生字段 schema 完整解析，并要求结构定位唯一。来源使用现有游戏资源；尚未启动游戏验证。生成入口为 `build/extract-weapon-executions.py`；不把资源条目数当作独立招式数。", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=pathlib.Path, required=True)
    parser.add_argument("--weapon-audit", type=pathlib.Path, default=pathlib.Path("docs/complete-move-weapon-audit.json"))
    parser.add_argument("--registry", type=pathlib.Path, help="Include every native shadow-weapon config from this full registry")
    parser.add_argument("--output", type=pathlib.Path, default=pathlib.Path("docs/weapon-execution-audit.json"))
    parser.add_argument("--catalog", type=pathlib.Path, default=pathlib.Path("assets/key-enhancement/complete-moves.json"))
    args = parser.parse_args()
    equipment = json.loads(args.weapon_audit.read_text(encoding="utf-8"))["equipment"]
    if args.registry:
        assets = json.loads(args.registry.read_text(encoding="utf-8"))["assets"]
        equipment += [{"equipmentId": row["package"], "registryClass": row["assetClass"]} for row in assets if row["assetClass"] == "PBZ_SubWeaponConfig_C"]
    archive = e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    try:
        result = extract(archive, equipment)
        result["blueprintSchemas"] = {}
        for name, count in (("Main", 25), ("Temp", 24), ("Sub", 19)):
            path = "/PBZCombatAsset/Prototypes/Config/PBZ_" + name + "WeaponConfig"
            properties = blueprint_properties(e.Package(archive.resource(archive.packages[path.casefold()])))
            assert len(properties) == count, (name, len(properties))
            result["blueprintSchemas"][name] = properties
    finally:
        archive.close()
    result["breakAttackRecipes"] = break_attack_recipes(result, json.loads(args.catalog.read_text(encoding="utf-8")))
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.output.with_suffix(".md").write_text(markdown(result), encoding="utf-8")
    print("Decoded %d native execution entries for %d equipment configs" % (result["counts"]["entries"], result["counts"]["equipment"]))


if __name__ == "__main__":
    main()
