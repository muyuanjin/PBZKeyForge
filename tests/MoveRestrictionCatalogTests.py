"""Check the reviewed qualification and native preparation boundaries."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def main():
    data = read("assets/key-enhancement/move-restrictions.json")
    entries = data["entries"]
    native = [move for name in ("weapon-moves", "shadow-moves")
              for move in read("assets/key-enhancement/" + name + ".json")["moves"]]
    for move in native:
        prep = entries[move["id"]]["preparation"]
        assert prep["equipmentConfigs"], move["id"]
        assert prep["equipmentSlot"] in ("Weapon.Slot.Main", "Weapon.Slot.ShaChi"), move["id"]
    for identity, entry in entries.items():
        groups, prep = entry["groups"], entry["preparation"]
        assert set(groups) == {"equipment", "training", "space", "stance", "predecessor", "cooldown"}
        for group in groups.values():
            assert not set(group["requiredTags"]) & set(group["ignoredTags"]), identity
        if prep.get("nativeHoldInputs"):
            assert prep.get("automaticReleaseWhen"), identity
    holds = {identity: entry["preparation"] for identity, entry in entries.items()
             if entry["preparation"].get("nativeHoldInputs")}
    assert len(holds) == 14
    for fragment in ("CaoOuSiXian", "JianKeZhaoJiLing", "KuYeSiZhuChuan", "XueSeLingdang", "HuangJinHuoChong_Normal_02"):
        assert not any(fragment in identity for identity in holds), fragment
    readiness = "CharacterState.AbilityState.HuoLiuXing.CanJumpTimeline"
    assert readiness not in data["classificationTags"]
    assert data["classificationTags"]["CharacterState.Native.Attitude.Sprinting"] == "stance"
    assert data["classificationTags"]["CharacterState.AbilityState.GlobalActionComboAvaliable"] == "predecessor"
    assert data["classificationTags"]["CharacterState.AbilityState.Double.CloneCooldown"] == "cooldown"
    assert "CharacterState.AbilityState.Double.BlockClone" not in data["classificationTags"]
    assert data["classificationTags"]["CharacterState.WeaponUpgrade.KuYeSiZhuChuan.ShachiReflectCooldown"] == "cooldown"
    for tag in ("CharacterState.AbilityState.Dash.Forbid", "CharacterState.Stand.Break"):
        assert data["classificationTags"].get(tag) != "cooldown"
    long_buff = "CharacterState.AbilityState.Long.Buff.Lv3"
    assert data["classificationTags"][long_buff] == "predecessor"
    assert data["classificationTags"]["CharacterState.WeaponUpgrade.Iron.BJump"] == "training"
    for identity in ("ability:Ability.Action.Hun.Heavy.Long.BSup",
                     "move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_BSup",
                     "move:Weapon.Weapon_Hun_Long.GA_Hun_Long_BSup"):
        entry = entries[identity]
        assert long_buff in entry["groups"]["predecessor"]["requiredTags"], identity
        assert long_buff not in entry["preparation"].get("unclassifiedRequiredTags", []), identity
        effects = entry["preparation"]["nativeStateEffects"]
        assert all(effect["effectClass"].endswith("GE_Weapon_Long_Buff_Lv3_C") for effect in effects), identity
        for effect in effects:
            assert effect["requirementScopes"][0]["application"] and not effect["requirementScopes"][0].get("removal"), identity
            assert len(effect["requirementScopes"]) == 1, identity
            assert len(effect["replacedEffectClasses"]) == 2, identity
            assert effect["replacedEffectClasses"][0].endswith("GE_Weapon_Long_Buff_Lv1_C"), identity
            assert effect["replacedEffectClasses"][1].endswith("GE_Weapon_Long_Buff_Lv2_C"), identity
    assert len(entries["ability:Ability.Action.Hun.Heavy.Long.BSup"]["preparation"]["nativeStateEffects"]) == 2
    assert entries["move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_BSup"]["preparation"]["nativeStateEffects"][0]["equipmentTag"] == "Weapon.Type.LongBlade.ChiRen"
    for tag, identities in {
        "CharacterState.AbilityState.BronzeSword.Deflect": (
            "ability:Ability.Action.Hun.Heavy.BronzeSword.Deflect.B",
            "move:Weapon.Weapon_Hun_TianXuanQingTongJian.GA_Hun_BronzeSword_Stance_B"),
        "CharacterState.AbilityState.JigSaw.PerfectDeflectReinforcement": (
            "ability:Ability.Action.Hun.Light.JigSaw.SA",
            "ability:Ability.Action.Hun.Heavy.Jigsaw.SB",
            "move:Weapon.Weapon_Hun_Jigsaw.GA_Hun_Jigsaw_SA",
            "move:Weapon.Weapon_Hun_Jigsaw.GA_Hun_Jigsaw_SB"),
    }.items():
        assert data["classificationTags"][tag] == "predecessor", tag
        for identity in identities:
            entry = entries[identity]
            assert tag in entry["groups"]["predecessor"]["requiredTags"], identity
            assert tag not in entry["preparation"].get("unclassifiedRequiredTags", []), identity
    critical = "CharacterState.AbilityState.YuuBlade.CriticalBuff"
    assert critical not in data["classificationTags"]
    for identity in ("ability:Ability.Action.Hun.Heavy.Atk01",
                     "move:Weapon.Weapon_Hun_YuuBlade.GA_Hun_YuuBlade_B"):
        entry = entries[identity]
        assert critical in entry["groups"]["predecessor"]["requiredTags"], identity
        assert critical not in entry["preparation"].get("unclassifiedRequiredTags", []), identity
    final = entries["move:GA_Hun_YuuBlade_C.CombatArt.Final"]
    assert critical in final["preparation"]["nativeStateTags"]["requiredTags"]
    assert all(critical not in group["requiredTags"] for group in final["groups"].values())
    assert data["classificationTags"]["CharacterState.AbilityState.SnakeDual.Thrown"] == "predecessor"
    for tag in ("CharacterState.AbilityState.Horizontal.Overcharged",):
        assert tag not in data["classificationTags"], tag
    assert sum("armedState" in entry["preparation"] for entry in entries.values()) > 300
    assert holds["ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA"]["automaticReleaseWhen"]["required_tags"] == [readiness]
    assert holds["move:Shadow.NiTouChe.GA_HorseCart_Charge"]["automaticReleaseWhen"]["minimum_time"] >= 1.8427888155
    assert holds["move:Shadow.YouLongHead.GA_YouLongHead_Water"]["automaticReleaseWhen"]["minimum_time"] >= 1.1827852726
    prepared = {identity for identity, entry in entries.items() if entry["preparation"].get("nativeActivation")}
    assert prepared == {"move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitStage2",
                        "move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitFinal",
                        "move:WeaponPhase.GA_Hun_ZhanMa_C.NormalFinal",
                        "move:GA_Hun_YuuBlade_C.CombatArt.Final"}
    assert final["preparation"]["nativeActivation"]["abilityClass"].endswith("GA_Hun_YuuBlade_C.GA_Hun_YuuBlade_C_C")
    assert final["preparation"]["nativeStateEffects"][0]["requiredBypassKinds"] == ["stance"]
    for identity, entry in entries.items():
        for effect in entry["preparation"].get("nativeStateEffects", []):
            assert effect["qualificationTag"] and effect["equipmentTag"], identity
            assert effect["effectClass"].endswith("_C") or effect["effectClass"].startswith("/Script/"), identity
            assert set(effect["requiredBypassKinds"]) <= set(entry["groups"]), identity
    for fragment in ("GA_Hun_Jigsaw_RA", "GA_Hun_Jigsaw_RB"):
        for move in native:
            if fragment in move["id"]:
                effects = entries[move["id"]]["preparation"].get("nativeStateEffects", [])
                if move["id"].endswith(("_RA", "_RAA", "_RAAA", "_RB", "_RBB")):
                    assert any(effect["effectClass"].endswith("GE_JigSaw_BloodSaw_C") for effect in effects), move["id"]
    snake = {move["id"] for move in native if "/SnakeDual/ReleaseAttack/" in move.get("abilityClass", "")}
    assert len(snake) == 15
    assert {identity for identity, entry in entries.items() if entry["preparation"].get("snakeProjectile")} == snake
    assert data["classificationTags"]["CharacterState.AbilityState.LuanYing.SnapSoul"] == "predecessor"
    assert data["classificationTags"]["CharacterState.AbilityState.KillBounsSnap"] == "predecessor"
    horizontal = entries["move:Weapon.Weapon_Hun_Horizontal.GA_Hun_Horizontal_CounterAttack_01"]["preparation"]
    assert horizontal["nativeStateEffects"][0]["effectClass"] == "/Script/PBZCombatAsset.GE_Horizontal_Overcharge"
    for identity, recipe in read("docs/complete-move-restriction-audit.json")["recipes"].items():
        if recipe["fullEffectsNeedExternalCombatContext"]:
            assert identity not in prepared, identity
    print("MoveRestrictionCatalogTests PASS: %d targets, %d native equipment entrances, 14 native preparations" %
          (len(entries), len(native)))


if __name__ == "__main__":
    main()
