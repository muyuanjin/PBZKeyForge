"""Build exact-class counter recipes from individually inspected source-gap resources."""
import argparse
import json
import pathlib


def extract(gaps):
    recipes = []
    for candidate in gaps["candidates"]:
        if candidate["reviewGroup"] not in ("perfect-dodge-paired-counter", "perfect-dodge-special-counter"):
            continue
        assert candidate["reverseSources"], candidate["package"]
        assert any(row.get("deflectFrameActions") for row in candidate["reverseSources"]), candidate["package"]
        path = candidate["package"]
        name = path.rsplit("/", 1)[-1]
        code = name.removeprefix("GA_Hun_").removeprefix("GA_")
        recipe = {
            "id": "move:ContextualCounter." + name,
            "sourceId": "ability:Ability.Action.Hun.PerfectDodge",
            "tag": "Ability.Action.Hun.PerfectDodge",
            "abilityClass": path + "." + name + "_C",
            "activateByClass": True,
            "inputAction": "/PBZDeflectSystem/Input/IA_PerfectDodge.IA_PerfectDodge",
            "priority": -5,
            "requiredTags": ["CharacterState.Space.Ground"],
            "category": "defense/contextual",
            "usageKey": "moves.contextcounter.usage",
            "code": code,
            "labelParts": ["catalog.perfectdodge", "catalog.counter"],
            "searchAliases": [name, code, "情境反制", "特殊完美闪避"],
        }
        recipes.append({"recipe": recipe, "parentChain": candidate["parentChain"],
                        "sources": candidate["reverseSources"], "timelines": candidate["timelines"]})
    return {
        "build": gaps["build"], "runtimeVerified": False,
        "evidenceType": "Cooked counter sources and Blueprint qualification, plus static native frame grant and exact-class input activation",
        "activationContract": {
            "grant": "The game's DeflectComponent temporarily grants the received frame's existing CounterAbility class; the binding never grants abilities.",
            "normalEntry": "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass",
            "arguments": ["existing granted AbilityClass", "IA_PerfectDodge", "priority -5"],
            "nativeQualification": "BP_PlayerPerfectDodge_Base requires a real current or nearest valid received frame. Special and paired-counter bases require frame.CounterAbility == this exact class.",
            "payload": "Normal activation consumes the real frame handle and preserves source avatar, bullet, OnDodge and paired target execution setup.",
            "instancing": "Normal native activation owns instancing; recipes do not locate an instance or call a stage endpoint.",
        },
        "nativeEvidence": {
            "frameReceiver": "0x14601c400; CounterAbility passed to grant helper at 0x14601c64e",
            "grantHelper": "0x14601a2f0..0x14601a4bd; class refcount +0x240; existing-spec lookup 0x144c8fe90; spec constructor 0x144cd7270; GiveAbility 0x144c96170",
            "inputWrapper": "0x145e2f470..0x145e2f60e; preserves external priority checks",
            "exactClassHelper": "0x145e29300..0x145e29400; existing-spec lookup at 0x145e2938c; absent spec returns false at 0x145e29397; normal TryActivateAbility at 0x145e293d9",
            "noGrantInExactClassHelper": True,
            "blueprintClassMatch": "BP_PlayerPerfectDodge_Base_Special and BP_Hun_PerfectDodge_ExecutionBase: Get Valid Perfect Dodge Timelines offset 0x15 compares Counter Ability with GetObjectClass(self)",
            "blueprintFrameQualification": "BP_PlayerPerfectDodge_Base.K2_CanActivateAbility uses PerfectDodgeResultData or GetNearestValidReceivedFrame; returns false when no qualifying timeline exists",
        },
        "recipes": recipes,
        "excludedReceiverClasses": [row["package"] for row in gaps["candidates"] if row["reviewGroup"] == "paired-execution-receiver"],
        "limitations": ["Bindings are available only while the game has granted the matching contextual counter and its normal qualification passes.",
                        "Static resource and simulation assertions do not verify game runtime timing."]
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gaps", type=pathlib.Path, default=pathlib.Path("docs/player-ability-source-gaps.json"))
    parser.add_argument("--output", type=pathlib.Path, default=pathlib.Path("docs/contextual-counter-recipes.json"))
    args = parser.parse_args()
    result = extract(json.loads(args.gaps.read_text(encoding="utf-8")))
    assert len(result["recipes"]) == 31 and len(result["excludedReceiverClasses"]) == 9
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Wrote 31 contextual counter recipes; excluded 9 execution receivers")


if __name__ == "__main__":
    main()
