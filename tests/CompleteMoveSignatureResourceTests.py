"""Check complete-move calls against native reflection and cooked UFunction fields.

Reads the supported game's files without starting or modifying it. Native
parameters come from UHT property tables; Blueprint parameters come from the
cooked function's FProperty records, including inherited callback owners.
"""
import argparse
import importlib.util
import json
import mmap
from pathlib import Path
import struct


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("combat_art", ROOT / "build/extract-combat-art-phases.py")
combat_art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(combat_art)


class NativeFunctions:
    def __init__(self, binary):
        self.binary = binary
        pe = struct.unpack_from("<I", binary, 60)[0]
        optional = pe + 24
        self.base = struct.unpack_from("<Q", binary, optional + 24)[0]
        count = struct.unpack_from("<H", binary, pe + 6)[0]
        size = struct.unpack_from("<H", binary, pe + 20)[0]
        self.sections = [struct.unpack_from("<8sIIII", binary, optional + size + i * 40) for i in range(count)]

    def offset(self, address):
        return next(raw + address - self.base - va for _, _, va, size, raw in self.sections
                    if self.base + va <= address < self.base + va + size)

    def address(self, offset):
        return next(self.base + va + offset - raw for _, _, va, size, raw in self.sections if raw <= offset < raw + size)

    def name(self, address):
        offset = self.offset(address)
        return self.binary[offset:self.binary.find(b"\0", offset)].decode("utf-8")

    def property_records(self, name, include_return=False):
        binary, position, signatures = self.binary, 0, []
        while True:
            position = binary.find(name.encode() + b"\0", position)
            if position < 0:
                return signatures
            needle, reference = struct.pack("<Q", self.address(position)), 0
            position += 1
            while True:
                reference = binary.find(needle, reference)
                if reference < 0:
                    break
                at, reference = reference, reference + 1
                if at % 8:
                    continue
                table, count, _, object_flags, function_flags = struct.unpack_from("<QHHII", binary, at + 8)
                if count > 32 or object_flags != 0x45 or not function_flags & 0x400:
                    continue
                properties = []
                for i in range(count):
                    prop = struct.unpack_from("<Q", binary, self.offset(table) + i * 8)[0]
                    name_address, _, flags, kind = struct.unpack_from("<QQQI", binary, self.offset(prop))
                    # Native post hooks insert the return value after Context.
                    # Inner array properties are never function parameters.
                    if flags & 0x80 and (include_return or not flags & 0x400):
                        properties.append((self.name(name_address), kind, prop))
                signatures.append(properties)

    def signatures(self, name, include_return=False):
        return [[(field, kind) for field, kind, _ in signature]
                for signature in self.property_records(name, include_return)]


def blueprint_parameters(package, data):
    # Cooked UFunction: unversioned UObject header, SuperStruct, Children,
    # ChildProperties. These callbacks have no UField children or UObject values.
    assert data[:2] == b"\x05\x01" and struct.unpack_from("<I", data, 6)[0] == 0
    assert struct.unpack_from("<I", data, 10)[0] == 0
    count, position, parameters = struct.unpack_from("<I", data, 14)[0], 18, []

    def field():
        nonlocal position
        kind_index, kind_number, name_index, number = struct.unpack_from("<IIII", data, position)
        assert kind_number == 0
        kind, name = package.names[kind_index], package.names[name_index]
        if number:
            name += "_" + str(number - 1)
        flags = struct.unpack_from("<Q", data, position + 28)[0]
        position += 47
        if kind == "BoolProperty":
            position += 6
        elif kind in ("ObjectProperty", "ClassProperty", "StructProperty", "InterfaceProperty", "DelegateProperty"):
            position += 4
        elif kind == "ArrayProperty":
            field()
        elif kind not in ("FloatProperty", "DoubleProperty", "IntProperty", "NameProperty", "ByteProperty", "StrProperty"):
            raise AssertionError("Unsupported function property: " + kind)
        return name, kind, flags

    for _ in range(count):
        name, kind, flags = field()
        if flags & 0x80 and not flags & 0x400:
            parameters.append((name, kind))
    return parameters


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    moves = json.loads((ROOT / "assets/key-enhancement/complete-moves.json").read_text(encoding="utf-8"))["moves"]
    expected_native = {
        "PlayTimelineByName": [("TimelineName", 0x14), ("PlayRate", 0xa), ("SyncPoint", 0xa), ("StartTimeSeconds", 0xa)],
        "GetPlayTimelineTask": [], "GetTimelineByName": [("TimelineName", 0x14)],
        "TryActivateExternalActionAbilityByClass": [("Class", 0x11), ("InputAction", 0x12), ("ActionPriority", 0x3)],
        "ForceActivateExternalActionAbilityByClass": [("Class", 0x11), ("InputAction", 0x12), ("ActionPriority", 0x3)],
        "ForceActivateExternalActionAbilityByClassWithEvent": [("Class", 0x11), ("InputAction", 0x12), ("ActionPriority", 0x3), ("EventData", 0x19)],
        "TryActivateExternalActionAbilityByTag": [("AbilityTag", 0x19), ("InputAction", 0x12), ("ActionPriority", 0x3)],
        "ForceActivateExternalActionAbilityByTag": [("AbilityTag", 0x19), ("InputAction", 0x12), ("ActionPriority", 0x3)],
        "TryActivateExternalActionAbilityInterruptLowerByTag": [("AbilityTag", 0x19), ("InputAction", 0x12), ("ActionPriority", 0x3)],
        "TryActivateAbilityByClass": [("InAbilityToActivate", 0x11), ("bAllowRemoteActivation", 0x4c)],
        "TryActivateAbilitiesByTag": [("GameplayTagContainer", 0x19), ("bAllowRemoteActivation", 0x4c)],
        "GetGameplayAttributeValue": [("Attribute", 0x19), ("bFound", 0x4c)],
        "BP_ApplyGameplayEffectToSelf": [("GameplayEffectClass", 0x11), ("Level", 0xa), ("EffectContext", 0x19)],
        "MakeOutgoingSpec": [("GameplayEffectClass", 0x11), ("Level", 0xa), ("Context", 0x19)],
        "BP_ApplyGameplayEffectSpecToSelf": [("SpecHandle", 0x19)],
        "RemoveActiveGameplayEffect": [("Handle", 0x19), ("StacksToRemove", 0x3)],
        "RemoveActiveGameplayEffectBySourceEffect": [("GameplayEffect", 0x11),
            ("InstigatorAbilitySystemComponent", 0x12), ("StacksToRemove", 0x3)],
        "GetFloatAttributeFromAbilitySystemComponent": [("AbilitySystem", 0x12), ("Attribute", 0x19), ("bSuccessfullyFoundAttribute", 0x4c)],
        "ModifyCharacterAttributeValue": [("Character", 0x12), ("Attribute", 0x19), ("ModiferOp", 0x0), ("Value", 0xa)],
        "IsHolding": [("InputAction", 0x12)],
        "MakeInputActionValueOfType": [("X", 0xb), ("Y", 0xb), ("Z", 0xb), ("ValueType", 0x1e)],
        "Conv_InputActionValueToBool": [("InValue", 0x19)],
        "StartContinuousInputInjectionForAction": [("Action", 0x12), ("RawValue", 0x19), ("Modifiers", 0x16), ("Triggers", 0x16)],
        "StopContinuousInputInjectionForAction": [("Action", 0x12)],
        "GetResolvedExecutionType": [("Channel", 0x14)],
        "GetGameplayTagCount": [("GameplayTag", 0x19)], "K2_CancelAbility": [],
        "GetSGameInputComponent": [("Actor", 0x12)],
        "GetComponentByClass": [("ComponentClass", 0x11)],
        "CalculateDamage": [("AttackerAttributes", 0x17), ("AttackerAbilitySystem", 0x12),
                            ("TargetAbilitySystem", 0x12), ("AttackSourceTags", 0x19),
                            ("AttackData", 0x17), ("DamageSource", 0x19), ("DamageData", 0x17)],
        "TakeDamage": [("DamageSource", 0x13), ("DamageCause", 0x19), ("HitLocation", 0x19),
                       ("HitBody", 0x19), ("AttackerAttributes", 0x17), ("AttackData", 0x17),
                       ("CustomCalculator", 0x12), ("AttackDirection", 0x19),
                       ("SweepDirection", 0x19), ("FinalDamageData", 0x17)],
        "DealDamageToAvatar": [("TargetAvatar", 0x12), ("HitLocation", 0x19), ("HitBody", 0x19),
                               ("AttackDirection", 0x19), ("SweepDirection", 0x19)],
        "DealDamageCustom": [("TargetAvatar", 0x12), ("HitLocation", 0x19), ("HitBody", 0x19),
                             ("CustomDamageCause", 0x19), ("CustomCalculator", 0x12),
                             ("AttackDirection", 0x19), ("SweepDirection", 0x19)],
        "GetDamageSourceActor": [], "GetDamageSourceAbility": [], "GetSourceAbility": [],
        "UpdateState": [("PlayerInput", 0x12), ("ModifiedValue", 0x19), ("DeltaTime", 0xa)],
        "GetTriggerType": [], "GetFrameCount": [], "GetInputVectorKeyState": [("Key", 0x19)],
    }
    expected_returns = {
        name: 0x4c for name in expected_native
        if name.startswith(("TryActivate", "ForceActivate"))
    }
    expected_returns.update({"GetGameplayAttributeValue": 0xa,
                             "GetFloatAttributeFromAbilitySystemComponent": 0xa,
                             "CalculateDamage": None,
                             "MakeInputActionValueOfType": 0x19,
                             "Conv_InputActionValueToBool": 0x4c})
    expected_returns.update({"BP_ApplyGameplayEffectToSelf": 0x19,"RemoveActiveGameplayEffect": 0x4c,
                             "MakeOutgoingSpec": 0x19,"BP_ApplyGameplayEffectSpecToSelf": 0x19,
                             "GetGameplayTagCount": 0x3})
    expected_returns.update({"UpdateState": 0x1e, "GetTriggerType": 0x1e,
                             "GetFrameCount": 0x4, "GetInputVectorKeyState": 0x19})
    with args.game_exe.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        native = NativeFunctions(binary)
        def struct_getter(function, parameter, parameters=None):
            properties = native.property_records(function, include_return=True)
            if parameters is not None:
                properties = [signature for signature in properties
                              if [(name, kind) for name, kind, _ in signature if name != "ReturnValue"] == parameters]
            matches = [pointer for signature in properties for name, kind, pointer in signature
                       if name == parameter and kind == 0x19]
            assert len(matches) == 1, (function, parameter)
            return struct.unpack_from("<Q", binary, native.offset(matches[0]) + 56)[0]

        input_value = struct_getter("MakeInputActionValueOfType", "ReturnValue")
        assert input_value == struct_getter("StartContinuousInputInjectionForAction", "RawValue")
        assert input_value == struct_getter("Conv_InputActionValueToBool", "InValue")
        assert input_value != struct_getter("InjectInputVectorForAction", "Value"), "FInputActionValue is not FVector"
        assert native.signatures("MakeEffectContext", include_return=True).count([("ReturnValue", 0x19)]) == 1
        effect_context = struct_getter("MakeEffectContext", "ReturnValue", [])
        assert effect_context == struct_getter("MakeOutgoingSpec", "Context")
        assert struct_getter("MakeOutgoingSpec", "ReturnValue") == struct_getter("BP_ApplyGameplayEffectSpecToSelf", "SpecHandle")
        assert struct_getter("BP_ApplyGameplayEffectToSelf", "ReturnValue") == struct_getter("BP_ApplyGameplayEffectSpecToSelf", "ReturnValue")
        assert effect_context == struct_getter("BP_ApplyGameplayEffectToSelf", "EffectContext")
        effect_handle = struct_getter("BP_ApplyGameplayEffectToSelf", "ReturnValue")
        assert effect_handle == struct_getter("RemoveActiveGameplayEffect", "Handle")
        assert effect_context != effect_handle, "Effect contexts and active-effect handles are distinct native structs"
        for name, expected in expected_native.items():
            full = native.signatures(name, include_return=True)
            signatures = [[(n, kind) for n, kind in signature if n != "ReturnValue"] for signature in full]
            assert signatures == [expected], (name, signatures, expected)
            if name in expected_returns:
                returns = [[kind for n, kind in signature if n == "ReturnValue"] for signature in full]
                expected_return = expected_returns[name]
                assert returns == [[] if expected_return is None else [expected_return]], (name, returns, expected_return)
        assert [("DamageTag", 0x19), ("ModifyType", 0x1e), ("ModifyValue", 0xa),
                ("AddIfNotPresent", 0x4c), ("bApplyToChildren", 0x4c)] in native.signatures("ApplyModify")
        expected_timelines = [
            [("Timeline", 0x12), ("PlayRate", 0xa), ("SyncPoint", 0xa), ("StartTimeSeconds", 0xa), ("PreviousTimlineName", 0x14)],
            [("OwningAbility", 0x12), ("TaskInstanceName", 0x14), ("InTimeline", 0x12), ("PlayRate", 0xa),
             ("SyncPoint", 0xa), ("StartTimeSeconds", 0xa), ("InPreviousTimelineName", 0x14)],
        ]
        assert native.signatures("PlayTimeline") == expected_timelines
        # Bullet initialization is void: its post callback starts with Parent,
        # not a return-value slot. Other classes also declare Initialize.
        initialize = [("Parent", 0x13), ("OwnerAvatar", 0x12), ("SourceAbility", 0x12),
                      ("Data", 0x12), ("bDebug", 0x4c)]
        assert initialize in native.signatures("Initialize", include_return=True)
        assert [("ModifiedData", 0x17)] in native.signatures("GetModifiedAttackData", include_return=True)
        assert [("PlayRate", 0xa)] in native.signatures("SetPlayRate")
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    symbols, cache, checked = combat_art.ScriptObjects(args.paks), {}, 0
    def package(path):
        path = path.split(".")[0]
        if path not in cache:
            cache[path] = combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))
        return cache[path]
    try:
        execution_base = package("/PBZCombatAsset/Prototypes/Abilities/Base/PBZ_ShachiWeaponExecutionBase")
        execution_graph = dict(execution_base.exports)["ExecuteUbergraph_PBZ_ShachiWeaponExecutionBase"]
        # Native lethal override: Override(3), 999999, add-if-missing, exact tag.
        assert execution_graph.count(b"\x24\x03\x1e" + struct.pack("<f", 999999) + b"\x27\x28\x16") == 1
        assert "Damage.Damage.Target.GeneralDamage.Physical" in execution_base.names
        for move in moves:
            endpoint = move.get("entryPoint")
            if not endpoint:
                continue
            if endpoint.startswith("/Script/"):
                count = 5 if endpoint == "/Script/LegoAbility.LegoAbility:PlayTimeline" else len(expected_native[endpoint.split(":")[1]])
                assert len(move.get("entryArguments", [])) == count, move["id"]
            else:
                declaring_owner, callback = endpoint.rsplit(":", 1) if ":" in endpoint else (move["abilityClass"], endpoint)
                owner = move["abilityClass"]
                while True:
                    pkg = package(owner)
                    matches = [data for name, data in pkg.exports if name == callback]
                    if matches:
                        assert declaring_owner.split(".")[0] == owner.split(".")[0], (move["id"], "UFunction lookup must use its declaring class", owner)
                        parameters = blueprint_parameters(pkg, matches[0])
                        arguments = move.get("entryArguments", [])
                        assert len(parameters) == len(arguments), (move["id"], parameters, arguments)
                        for (_, kind), argument in zip(parameters, arguments):
                            assert (kind == "StructProperty" and isinstance(argument, dict)
                                    or kind in ("FloatProperty", "DoubleProperty") and isinstance(argument, (int, float))), (move["id"], parameters, arguments)
                        break
                    start = struct.unpack_from("<I", pkg.data, 32)[0]
                    parents = [symbols.resolve(pkg, struct.unpack_from("<Q", pkg.data, start + i * 72 + 40)[0])
                               for i in range(len(pkg.exports))
                               if symbols.resolve(pkg, struct.unpack_from("<Q", pkg.data, start + i * 72 + 32)[0]).endswith("BlueprintGeneratedClass")]
                    assert len(parents) == 1 and not parents[0].startswith("/Script/"), (move["id"], owner, endpoint)
                    owner = parents[0]
            checked += 1
    finally:
        archive.close()
    print("CompleteMoveSignatureResourceTests PASS:", len(expected_native) + 4, "native signatures;",
          len(expected_returns) + 1, "post-hook return contracts;", checked, "entry signatures")


if __name__ == "__main__":
    main()
