"""Merge reviewed native qualification and preparation evidence for catalog entries."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KINDS = ("equipment", "training", "space", "stance", "predecessor", "cooldown")

# These native route tags classify entrance eligibility. Effects, held inputs
# and object state retain the native preparation described below.
ENTRANCE_TAGS = {
    "CharacterState.AbilityState.Double.CloneCooldown": "cooldown",
    "CharacterState.AbilityState.Dash.Cooldown": "cooldown",
    "CharacterState.AbilityState.Dash.SpecialCooldown": "cooldown",
    "CharacterState.WeaponUpgrade.KuYeSiZhuChuan.ShachiReflectCooldown": "cooldown",
    "CharacterState.Native.Attitude.Sprinting": "stance",
    "CharacterState.AirState.AirAtk1": "space",
    "CharacterState.AbilityState.FinalLightAttack": "predecessor",
    "CharacterState.AbilityState.GlobalActionComboAvaliable": "predecessor",
    "CharacterState.AbilityState.DualHook.CanPursure": "predecessor",
    "CharacterState.AbilityState.DualHook.DB.Heavy": "predecessor",
    "CharacterState.AbilityState.GuiTouBlade.InBHold": "predecessor",
    "CharacterState.AbilityState.Horizontal.CantCriticalHitWindow": "predecessor",
    "CharacterState.AbilityState.SlitheringSword.RandomDB.DB1": "predecessor",
    "CharacterState.AbilityState.SlitheringSword.RandomDB.DB3": "predecessor",
    "CharacterState.AbilityState.Long.Buff.Lv3": "predecessor",
    "CharacterState.AbilityState.BronzeSword.Deflect": "predecessor",
    "CharacterState.AbilityState.JigSaw.PerfectDeflectReinforcement": "predecessor",
    "CharacterState.AbilityState.JigSaw.BloodSaw": "stance",
    "CharacterState.AbilityState.GuiTouDao.Buff": "predecessor",
    "CharacterState.AbilityState.LuanYing.SnapSoul": "predecessor",
    "CharacterState.AbilityState.SnakeDual.Thrown": "predecessor",
    "CharacterState.AbilityState.KillBounsSnap": "predecessor",
    "GameplayCue.Buff.YanYueDao.SkillReinforced": "predecessor",
    "CharacterState.WeaponUpgrade.Iron.BJump": "training",
}
# YuuBlade B has a self-contained Main attack. Its C Final instead selects
# Exit from the original buff, so the two entrances must retain separate rules.
SCOPED_ENTRANCE_TAGS = {
    "CharacterState.AbilityState.YuuBlade.CriticalBuff": {
        "kind": "predecessor",
        "entries": ("ability:Ability.Action.Hun.Heavy.Atk01",
                    "move:Weapon.Weapon_Hun_YuuBlade.GA_Hun_YuuBlade_B"),
    },
}
NATIVE_STATE_TAGS = {
    "CharacterState.Armed",
    "CharacterState.AbilityState.Horizontal.Overcharged",
    "CharacterState.AbilityState.InSpecialPerform",
    "CharacterState.AbilityState.SnakeDual.Normal",
    "CharacterState.AbilityState.YuuBlade.CriticalBuff",
}

# These original effects provide attack modifiers, weapon form or native cues
# in addition to qualifying the entrance. Shared input identities retain their
# weapon condition so one weapon cannot acquire another weapon's preparation.
NATIVE_STATE_EFFECTS = {
    "CharacterState.AbilityState.Long.Buff.Lv3": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/Long/UpgradeData/GE_Weapon_Long_Buff_Lv3.GE_Weapon_Long_Buff_Lv3_C",
        "equipmentTags": ["Weapon.Type.LongBlade.Default", "Weapon.Type.LongBlade.ChiRen"],
        "requiredBypassKinds": ["predecessor"],
        "replacedEffectClasses": [
            "/PBZCombatAsset/ActorData/Players/Hun/Weapon/Long/UpgradeData/GE_Weapon_Long_Buff_Lv1.GE_Weapon_Long_Buff_Lv1_C",
            "/PBZCombatAsset/ActorData/Players/Hun/Weapon/Long/UpgradeData/GE_Weapon_Long_Buff_Lv2.GE_Weapon_Long_Buff_Lv2_C",
        ],
        "requirementScopes": [
            {"effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/Long/UpgradeData/GE_Weapon_Long_Buff_Lv3.GE_Weapon_Long_Buff_Lv3_C",
             "application": True},
        ],
    },
    "CharacterState.AbilityState.BronzeSword.Deflect": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/BronzeSword/GE_BronzeSword_DeflectReinforced.GE_BronzeSword_DeflectReinforced_C",
        "equipmentTag": "Weapon.Type.BronzeSword", "requiredBypassKinds": ["predecessor"],
    },
    "CharacterState.AbilityState.Horizontal.Overcharged": {
        "effectClass": "/Script/PBZCombatAsset.GE_Horizontal_Overcharge",
        "equipmentTag": "Weapon.Type.Horizontal", "requiredBypassKinds": ["predecessor"],
    },
    "CharacterState.AbilityState.JigSaw.PerfectDeflectReinforcement": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/Jigsaw/UpgradeData/GE_JigSaw_PerfectDeflectReinforcement.GE_JigSaw_PerfectDeflectReinforcement_C",
        "equipmentTag": "Weapon.Type.Jigsaw", "requiredBypassKinds": ["predecessor"],
    },
    "CharacterState.AbilityState.JigSaw.BloodSaw": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/Jigsaw/UpgradeData/GE_JigSaw_BloodSaw.GE_JigSaw_BloodSaw_C",
        "equipmentTag": "Weapon.Type.Jigsaw", "requiredBypassKinds": ["stance"],
    },
    "CharacterState.AbilityState.YuuBlade.CriticalBuff": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/YuuBlade/GE_Hun_YuuBlade_Buff.GE_Hun_YuuBlade_Buff_C",
        "equipmentTag": "Weapon.Type.DoubleBlade.YuuBlade", "requiredBypassKinds": ["predecessor"],
    },
    "CharacterState.AbilityState.GuiTouDao.Buff": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/GuiTouBlade/UpgradeData/GE_Hun_GuiTouBlade_HasBuff_Base.GE_Hun_GuiTouBlade_HasBuff_Base_C",
        "equipmentTag": "Weapon.Type.GuiTouBlade", "requiredBypassKinds": ["predecessor"],
    },
    "GameplayCue.Buff.YanYueDao.SkillReinforced": {
        "effectClass": "/PBZCombatAsset/ActorData/Players/Hun/Weapon/YanYueDao/Filed/GE_YanYueDao_SpeicalSkill_CanReinforced.GE_YanYueDao_SpeicalSkill_CanReinforced_C",
        "qualificationTag": "CharacterState.AbilityState.YanYueDao.Reinforced.On",
        "equipmentTag": "Weapon.Type.YanYueDao", "requiredBypassKinds": ["predecessor"],
    },
}


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def package_path(path):
    return path.split(".")[0] if path else None


def equipment_sources():
    configs, classes, identities, executions, bindings = {}, {}, {}, {}, {}

    def connect(table, identity, config):
        table.setdefault(identity, set()).add(config)

    weapons = read("docs/weapon-move-entries.json")
    for equipment in weapons["equipment"]:
        config = equipment["configId"]
        configs[config] = {"slot": "Weapon.Slot.Main", "tags": set(equipment["equipTags"])}
        for ability in equipment["equipAbilities"] + equipment["owningAbilities"]:
            connect(classes, package_path(ability), config)
        for entry in equipment["entries"]:
            connect(identities, entry["id"], config)
            if entry.get("tag"):
                connect(identities, "ability:" + entry["tag"], config)
            route = entry.get("nativeInputRoute") or {}
            if route.get("inputAction"):
                connect(identities, "action:" + package_path(route["inputAction"]), config)
        for mapping in equipment["inputBindingMaps"]:
            for action, binding in mapping["pairs"]:
                if binding:
                    connect(bindings, package_path(binding), config)
                if action:
                    connect(identities, "action:" + package_path(action), config)
    special = read("docs/complete-move-weapon-special-entries.json")
    for config, fields in special["equipmentEvidence"].items():
        if config in configs:
            for field in ("8", "9"):
                for ability in fields.get(field) or []:
                    connect(classes, package_path(ability), config)
                    if field == "9":
                        for reference in special["abilityEvidence"].get(package_path(ability), {}).get("cdoTags", []):
                            if reference["tag"].startswith("Ability.Action.Hun.Weapon.Switch."):
                                connect(identities, "ability:" + reference["tag"], config)
    shadow = read("docs/shadow-move-entries.json")
    for equipment in shadow["configurations"]:
        config = equipment["equipmentId"]
        configs[config] = {"slot": "Weapon.Slot.ShaChi", "tags": set(equipment["equipTags"])}
        for ability in equipment["equipAbilities"] + equipment["owningAbilities"]:
            connect(classes, package_path(ability), config)
    for family in shadow["families"]:
        for ability in family["abilities"]:
            owning = classes.get(package_path(ability["abilityClass"]), set())
            for identity in ability.get("catalogIds", []) + ([ability["moveId"]] if ability.get("moveId") else []):
                identities.setdefault(identity, set()).update(owning)
            # Shared default input remains associated with every configuration
            # that actually grants an ability carrying that input identity.
            for tag in ability.get("tagCandidates", []):
                if tag == "Ability.Action.Hun.ShachiAbility.Default":
                    identities.setdefault("ability:" + tag, set()).update(owning)
    for family in read("docs/complete-move-shadow-audit.json")["families"]:
        active_classes = {row["package"] for row in family["associatedAbilities"] if row["role"] == "active-or-contextual"}
        active_configs = {config for ability in active_classes for config in classes.get(ability, [])}
        for route in family["inputRoutes"]:
            if route["binding"].startswith("/PBZCombatAsset/ActorData/Players/Hun/InputMap/"):
                continue
            identities.setdefault("ability:" + route["tag"], set()).update(active_configs)
            bindings.setdefault(route["binding"], set()).update(active_configs)
        for variant in family["configurationVariants"]:
            for action in variant["inputActions"]:
                connect(identities, "action:" + package_path(action), variant["package"])
    for equipment in read("docs/weapon-execution-audit.json")["equipment"]:
        config = equipment["equipmentId"]
        if config not in configs:
            continue
        for animation in equipment["executionAnims"]["entries"]:
            connect(executions, (animation["channel"], animation["executionType"]), config)
            connect(classes, package_path(animation["executionAbility"]), config)
            connect(identities, "ability:Ability.Action.Execution.Channel." + animation["channel"], config)
    for ability in read("prototype/input-router/actions.json")["abilities"]:
        for source in ability["sources"]:
            identities.setdefault("ability:" + ability["tag"], set()).update(bindings.get(source["asset"], set()))
            required = {tag for tag in source["requiredTags"] if any(tag in row["tags"] for row in configs.values())}
            if required:
                identities["ability:" + ability["tag"]].update(config for config, row in configs.items() if required <= row["tags"])
    return configs, classes, identities, executions


def add_equipment(entries, moves, phase_audit):
    configs, classes, identities, executions = equipment_sources()
    tag_universe = set(tag for config in configs.values() for tag in config["tags"])
    contexts = {row["id"]: row["equipment"] for row in read("docs/complete-move-counter-contexts.json").values()
                if isinstance(row, dict) and row.get("id") and row.get("equipment")}

    def constrained(candidates, item):
        required = set(item["groups"]["equipment"]["requiredTags"]) & tag_universe
        ignored = set(item["groups"]["equipment"]["ignoredTags"])
        return {config for config in candidates if config in configs
                and required <= configs[config]["tags"] and not ignored.intersection(configs[config]["tags"])}

    # Exact classes own phase recipes even when their source is a shared C,
    # heavy-attack or shadow input identity.
    for identity, item in entries.items():
        prep = item["preparation"]
        existing = set(prep.get("equipmentConfigs", []))
        move = moves.get(identity)
        related = set(identities.get(identity, set()))
        if move:
            exact = classes.get(package_path(move.get("abilityClass")), set()).copy()
            for path in move.get("activeAbilityClasses", []):
                exact.update(classes.get(package_path(path), set()))
            if move.get("executionChannel"):
                exact.update(executions.get((move["executionChannel"], move.get("executionType")), set()))
            related.update(exact)
            if identity in contexts:
                related = {contexts[identity]}
            if not related:
                source = entries.get(move.get("sourceId"), {})
                related.update(source.get("preparation", {}).get("equipmentConfigs", []))
                related.update(identities.get(move.get("sourceId"), set()))
        if not related and set(item["groups"]["equipment"]["requiredTags"]) & tag_universe:
            related = set(configs)
        related = constrained(related, item)
        if existing or related:
            prep["equipmentConfigs"] = sorted(existing | related)
            slots = {configs[config]["slot"] for config in prep["equipmentConfigs"] if config in configs}
            if len(slots) == 1:
                prep["equipmentSlot"] = next(iter(slots))
            elif slots:
                prep["equipmentSlotConfigs"] = {slot: [config for config in prep["equipmentConfigs"]
                    if config in configs and configs[config]["slot"] == slot] for slot in sorted(slots)}

    # A source recipe may have obtained its configuration through its exact
    # class above. Resolve dependent identities after all exact owners exist.
    for identity, move in moves.items():
        prep = entries[identity]["preparation"]
        if prep.get("equipmentConfigs"):
            continue
        source = entries.get(move.get("sourceId"), {}).get("preparation", {})
        candidates = constrained(set(source.get("equipmentConfigs", [])), entries[identity])
        if candidates:
            prep["equipmentConfigs"] = sorted(candidates)
            slots = {configs[config]["slot"] for config in candidates}
            if len(slots) == 1:
                prep["equipmentSlot"] = next(iter(slots))
    return configs


def add_independent_preparations(entries, moves, phase_audit):
    branches = {"move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitStage2": "HasSawHit",
                "move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitFinal": "HasSawHit",
                "move:WeaponPhase.GA_Hun_ZhanMa_C.NormalFinal": "ExecutionTimeline",
                "move:GA_Hun_YuuBlade_C.CombatArt.Final": "Exit"}
    for identity, branch in branches.items():
        reviewed, move = phase_audit["recipes"][identity], moves[identity]
        if (not reviewed.get("canPrepareFromNormalActivation") and branch != "Exit") \
                or reviewed["fullEffectsNeedExternalCombatContext"]:
            continue
        prep = entries[identity]["preparation"]
        if not prep.get("equipmentConfigs") or not move.get("abilityClass"):
            continue
        prep["nativeActivation"] = {"abilityClass": move["abilityClass"], "tag": move["tag"],
            "inputAction": move["inputAction"], "activateByClass": True, "priority": move.get("priority", -5)}
        prep["nativeEntry"] = {"function": move["entryPoint"], "arguments": move["entryArguments"],
                               "entryWhen": move["entryWhen"]}
        if branch == "HasSawHit":
            prep["nativePreparationEntry"] = {
                "function": "/Script/LegoAbility.LegoAbility:PlayTimelineByName",
                "arguments": [{"fname": branch}, 1.0, 0.0, 0.0],
                "entryWhen": {"timeline": "Main", "minimum_time": 0.001},
                "requiredBypassKinds": ["predecessor"]}


def refine_native_holds(entries, moves):
    # These exact abilities carry an unused Is Holding Blueprint helper.
    # Neither their graphs nor their inherited activation graphs call it;
    # Main completes its native attack/summon without a release task.
    unused_helpers = {
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/CaoOuSiXian/GA_CaoOuSiXian_Normal_01",
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/HuangJinHuoChong/GA_HuangJinHuoChong_Normal_02",
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/JianKeZhaoJiLing/GA_JianKeZhaoJiLing_Normal_01",
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/KuYeSiZhuChuan/GA_KuYeSiZhuChuan_Normal_01",
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/XueSeLingDang/GA_XueSeLingDang_Normal_01"}
    for identity, move in moves.items():
        if package_path(move.get("abilityClass")) in unused_helpers:
            entries[identity]["preparation"].pop("nativeHoldInputs", None)
    ready = {
        "ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA": {
            "condition": {"timeline": "Main", "required_tags": ["CharacterState.AbilityState.HuoLiuXing.CanJumpTimeline"]},
            "nativeGate": "OnTick checks Is Holding and HuoLiuXing.CanJumpTimeline before calling ToRelease; Final initializes its own chain and weapon attacks."},
        "move:Shadow.NiTouChe.GA_HorseCart_Charge": {
            "condition": {"timeline": "Spawn", "minimum_time": 1.843},
            "nativeEvent": "GameplayEvent.Ability.NiTouChe.CanRelease", "eventTime": 1.842788815498352,
            "eventAction": "GAA_GameplayEvent_C_3"},
        "move:Shadow.YouLongHead.GA_YouLongHead_Water": {
            "condition": {"timeline": "Main", "minimum_time": 1.183},
            "nativeEvent": "GameplayEvent.Ability.LionHead.CanRelease", "eventTime": 1.1827852725982666,
            "eventAction": "GAA_GameplayEvent_C_1"}}
    for identity, evidence in ready.items():
        if entries[identity]["preparation"].get("nativeHoldInputs"):
            entries[identity]["preparation"]["automaticReleaseWhen"] = evidence["condition"]
    return {"unusedHelperClasses": sorted(unused_helpers),
            "unusedHelperReason": "Is Holding has no caller in the ability or inherited activation graphs; Main completes the native attack or summon without waiting for release.",
            "releaseReadiness": ready}


def main():
    moves = {item["id"]: item for file in ("complete-moves", "weapon-moves", "shadow-moves")
             for item in read("assets/key-enhancement/" + file + ".json")["moves"]}
    abilities = {"ability:" + item["tag"]: item for item in read("prototype/input-router/actions.json")["abilities"]}
    abilities.update({"action:" + item["path"]: item for item in read("prototype/input-router/actions.json")["inputActions"]})
    entries = {identity: {"groups": {kind: {"requiredTags": [], "ignoredTags": []} for kind in KINDS},
                          "preparation": {}} for identity in {**moves, **abilities}}
    classifications = {}
    weapons = read("docs/move-restrictions-weapons.json")
    for kind, tags in weapons["classificationTags"].items():
        for tag in tags:
            classifications[tag] = kind
    for contribution in (weapons, read("docs/move-restrictions-shadow.json")):
        for identity, item in contribution["entries"].items():
            if identity not in entries:
                raise ValueError("Unknown restriction target: " + identity)
            current = entries[identity]
            for kind, group in item.get("groups", {}).items():
                kind = "predecessor" if kind == "prerequisite" else kind
                if kind not in KINDS:
                    continue
                group = {"requiredTags": group, "ignoredTags": []} if isinstance(group, list) else group
                for mode in ("requiredTags", "ignoredTags"):
                    current["groups"][kind][mode] = sorted(set(current["groups"][kind][mode] + group.get(mode, [])))
                    for tag in group.get(mode, []):
                        classifications.setdefault(tag, kind)
            current["preparation"].update(item.get("preparation", {}))
    phase_audit = read("docs/complete-move-restriction-audit.json")
    for identity, recipe in phase_audit["recipes"].items():
        current = entries[identity]
        for gate in recipe["eligibilityGates"]:
            kind = gate["category"]
            if kind not in KINDS:
                continue
            mode = "ignoredTags" if gate["field"] == "ignoredTags" else "requiredTags"
            current["groups"][kind][mode] = sorted(set(current["groups"][kind][mode] + [gate["tag"]]))
            classifications.setdefault(gate["tag"], kind)
        current["preparation"]["steps"] = recipe["prepareSteps"]
        current["preparation"]["externalContext"] = recipe["fullEffectsNeedExternalCombatContext"]
        current["preparation"]["reason"] = recipe.get("reason", "")
    for identity, item in entries.items():
        prep = item["preparation"]
        if "nativePreparationRecipeId" in prep:
            recipe = moves[prep["nativePreparationRecipeId"]]
            prep["abilityClass"] = recipe.get("abilityClass")
        if identity in moves and moves[identity].get("prepareInput"):
            prep.pop("nativeHoldInputs", None)
            prep.pop("automaticReleaseWhen", None)
        for tag in item["groups"]["predecessor"]["requiredTags"]:
            classifications.setdefault(tag, "predecessor")
    add_equipment(entries, moves, phase_audit)
    add_independent_preparations(entries, moves, phase_audit)
    for tag, kind in ENTRANCE_TAGS.items():
        classifications[tag] = kind
        for entry in entries.values():
            for mode in ("requiredTags", "ignoredTags"):
                present = any(tag in group[mode] for group in entry["groups"].values())
                raw = entry["preparation"].get("unclassified" + mode[0].upper() + mode[1:], [])
                if present or tag in raw:
                    for group in entry["groups"].values():
                        group[mode] = [value for value in group[mode] if value != tag]
                    entry["groups"][kind][mode].append(tag)
                    if tag in raw:
                        raw.remove(tag)
    for tag in NATIVE_STATE_TAGS:
        classifications.pop(tag, None)
        for entry in entries.values():
            for mode in ("requiredTags", "ignoredTags"):
                for group in entry["groups"].values():
                    if tag in group[mode]:
                        group[mode].remove(tag)
                        state = entry["preparation"].setdefault("nativeStateTags", {"requiredTags": [], "ignoredTags": []})
                        if tag not in state[mode]:
                            state[mode].append(tag)
    for tag, rule in SCOPED_ENTRANCE_TAGS.items():
        for identity in rule["entries"]:
            entry = entries[identity]
            for mode in ("requiredTags", "ignoredTags"):
                raw = entry["preparation"].get("unclassified" + mode[0].upper() + mode[1:], [])
                if tag in raw:
                    raw.remove(tag)
                    entry["groups"][rule["kind"]][mode].append(tag)
    for identity, entry in entries.items():
        prep = entry["preparation"]
        required = set(prep.get("unclassifiedRequiredTags", []))
        required.update(prep.get("nativeStateTags", {}).get("requiredTags", []))
        for group in entry["groups"].values():
            required.update(group["requiredTags"])
        effects = []
        for tag, rule in NATIVE_STATE_EFFECTS.items():
            if tag in required:
                for equipment_tag in rule.get("equipmentTags", [rule.get("equipmentTag")]):
                    equipment = entry["groups"]["equipment"]["requiredTags"]
                    if identity.startswith("move:") and equipment and equipment_tag not in equipment:
                        continue
                    if identity == "move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_BSup" \
                            and equipment_tag != "Weapon.Type.LongBlade.ChiRen":
                        continue
                    effect = dict(rule)
                    effect.pop("equipmentTags", None)
                    effect["equipmentTag"] = equipment_tag
                    effect.setdefault("qualificationTag", tag)
                    if identity == "move:GA_Hun_YuuBlade_C.CombatArt.Final":
                        effect["requiredBypassKinds"] = ["stance"]
                    effects.append(effect)
        if effects:
            prep["nativeStateEffects"] = effects
    for identity, move in moves.items():
        if "/SnakeDual/ReleaseAttack/" in move.get("abilityClass", ""):
            entries[identity]["preparation"]["snakeProjectile"] = True
    for identity in ("move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_B",
                     "move:Ability.Action.Hun.Light.SnakeDual.A_Deflect"):
        entries[identity]["preparation"]["snakeNormal"] = True
    for entry in entries.values():
        prep = entry["preparation"]
        flags = []
        for mode, value in (("requiredTags", True), ("ignoredTags", False)):
            if "CharacterState.Armed" in prep.get("unclassified" + mode[0].upper() + mode[1:], []) \
                    or "CharacterState.Armed" in prep.get("nativeStateTags", {}).get(mode, []):
                flags.append(value)
        if len(flags) == 1:
            prep["armedState"] = {"slot": "Weapon.Slot.Main", "armed": flags[0]}
    hold_audit = refine_native_holds(entries, moves)
    output = {"schemaVersion": 1, "build": "CL-302256", "runtimeVerified": False,
              "classificationTags": classifications, "entries": dict(sorted(entries.items())),
              "nativeHoldInputAudit": hold_audit}
    path = ROOT / "assets/key-enhancement/move-restrictions.json"
    path.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Move restrictions: %d entrances; %d native input preparations" %
          (len(entries), sum(bool(item["preparation"].get("nativeHoldInputs")) for item in entries.values())))
    for title, identities in (("Protected recipes", phase_audit["recipes"]),
                              ("Native moves", {key for key, move in moves.items() if move.get("nativeEntry")}),
                              ("Direct abilities", {key for key in abilities if key.startswith("ability:")})):
        configured = sum(bool(entries[key]["preparation"].get("equipmentConfigs")) for key in identities)
        slots = sum(bool(entries[key]["preparation"].get("equipmentSlot")) for key in identities)
        print("%s: %d/%d equipment configurations; %d unique slots" % (title, configured, len(identities), slots))


if __name__ == "__main__":
    main()
