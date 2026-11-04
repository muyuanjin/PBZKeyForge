"""Focused checks for input-resource schema and archive package resolution."""
import importlib.util
import pathlib
import struct
import unittest
from unittest.mock import patch


SOURCE = pathlib.Path(__file__).resolve().parents[1] / "build" / "extract-input-defaults.py"
SPEC = importlib.util.spec_from_file_location("input_defaults_extractor", SOURCE)
extractor = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(extractor)


def integer(value):
    return struct.pack("<i", value)


def header(fields, masked=()):
    if not fields:
        return struct.pack("<H", 0x100)
    fragments = []
    mask_bits = []
    previous = 0
    for offset, (field, zero) in enumerate(sorted(fields.items())):
        has_mask = zero or field in masked
        fragment = field - previous | (0x80 if has_mask else 0) | 0x200
        if offset == len(fields) - 1:
            fragment |= 0x100
        fragments.append(struct.pack("<H", fragment))
        if has_mask:
            mask_bits.append(zero)
        previous = field + 1
    mask = sum(int(value) << bit for bit, value in enumerate(mask_bits))
    return b"".join(fragments) + (bytes([mask]) if mask_bits else b"")


def fname(index):
    return struct.pack("<II", index, 0)


def package(exports, names=("IA_Registered",)):
    result = extractor.Package.__new__(extractor.Package)
    result.names = list(names)
    result.exports = [("Export" + str(i), data) for i, data in enumerate(exports)]
    return result


def setting(index):
    return header({1: False}) + fname(index) + bytes(4)


def mapping(key_index, action_pointer=-1, behavior=0, settings_pointer=0, masked_behavior=False):
    fields = {0: True, 1: False, 2: False, 3: False, 4: False, 5: True, 6: behavior == 0, 7: settings_pointer == 0}
    result = header(fields, masked=(6,) if masked_behavior else ())
    result += integer(0)  # Full modifiers array remains consumed.
    result += integer(action_pointer) + header({0: False}) + fname(key_index) + bytes([0])
    if behavior:
        result += bytes([behavior])
    if settings_pointer:
        result += integer(settings_pointer)
    return result


def context(mappings, names=("E", "Gamepad_FaceButton_Right", "IA_Registered")):
    data = header({1: False}) + header({0: False}) + integer(len(mappings)) + b"".join(mappings) + bytes(4)
    result = package([data, setting(2)], names)
    result.input_action = lambda pointer: "/Game/Actions/IA_Test"
    return result


class InputDefaultsExtractionTests(unittest.TestCase):
    def test_inherited_action_root_settings_and_zero_mask(self):
        root = header({2: True, 10: False}) + integer(2) + bytes(4)
        action = package([root, setting(0)])
        self.assertEqual("IA_Registered", action.input_action_settings())
        # A zero-mask group can contain a nonzero field as well as a zero one.
        action.exports[0] = ("Root", header({2: True, 10: False}, masked=(10,)) + integer(2))
        self.assertEqual("IA_Registered", action.input_action_settings())
        action.exports[0] = ("Root", header({10: True}))
        self.assertIsNone(action.input_action_settings())
        action.exports[0] = ("Root", header({2: False}) + integer(42))
        self.assertIsNone(action.input_action_settings())

    def test_unparsed_nonzero_property_before_settings_is_rejected(self):
        action = package([header({2: False, 10: False}) + integer(42) + integer(2), setting(0)])
        with self.assertRaisesRegex(ValueError, "before PlayerMappableKeySettings"):
            action.input_action_settings()

    def test_behavior_selects_inherited_override_or_disabled_and_counts_slots(self):
        # Inherited and override names share a counter across keyboard/controller;
        # disabled rows have neither an action nor a raw slot.
        decoded = context([
            mapping(0, behavior=0), mapping(1, behavior=1, settings_pointer=2),
            mapping(0, behavior=2, settings_pointer=2), mapping(0, behavior=0),
        ])
        calls = []
        def resolve(path):
            calls.append(path)
            return "IA_Registered"
        rows = decoded.context("ProjectPBZ/Content/Input/IMC_Test.uasset", resolve)["mappings"]
        self.assertEqual(["IA_Registered", "IA_Registered", None, "IA_Registered"], [r["action"] for r in rows])
        self.assertEqual([0, 1, None, 2], [r.get("rawSlot") for r in rows])
        self.assertEqual(["KeyboardMouse", "Gamepad", "KeyboardMouse", "KeyboardMouse"], [r["device"] for r in rows])
        self.assertEqual(2, len(calls))
        self.assertEqual([], rows[0]["triggers"])
        self.assertEqual([], rows[0]["modifiers"])

    def test_default_behavior_and_absent_inherited_settings_remain_fixed(self):
        decoded = context([mapping(0)])
        rows = decoded.context("IMC_Test.uasset", lambda _: None)["mappings"]
        self.assertIsNone(rows[0]["action"])
        self.assertNotIn("rawSlot", rows[0])
        # Remove the explicitly zero behavior field: the UObject default is 0.
        fields = {2: False, 3: False}
        data = header({1: False}) + header({0: False}) + integer(1)
        data += header(fields) + integer(-1) + header({0: False}) + fname(0) + bytes(4)
        decoded.exports[0] = ("Root", data)
        self.assertEqual("IA_Inherited", decoded.context("IMC_Default.uasset", lambda _: "IA_Inherited")["mappings"][0]["action"])

    def test_mapping_schema_and_trailing_data_still_reject_unknowns(self):
        decoded = context([mapping(0, behavior=3)])
        with self.assertRaisesRegex(ValueError, "settings behavior"):
            decoded.context("IMC_Test.uasset", lambda _: "IA_Registered")
        decoded = context([mapping(0)])
        decoded.exports[0] = ("Root", decoded.exports[0][1] + b"unexpected")
        with self.assertRaisesRegex(ValueError, "trailing IMC"):
            decoded.context("IMC_Test.uasset", lambda _: "IA_Registered")

    def test_virtual_package_mounts_are_case_insensitive_and_not_basename_based(self):
        files = {
            "../../../ProjectPBZ/Content/Actions/IA_Walk.uasset": 1,
            "../../../ProjectPBZ/Plugins/GameFeatures/PBZInput/Content/Actions/IA_Walk.uasset": 2,
            "../../../ProjectPBZ/Plugins/Combat/SGameCombatSystem/Content/Weapon/Input/IA_ChangeWeapon.uasset": 3,
            "../../../ProjectPBZ/Content/Characters/MoJian/IA_Test.uasset": 4,
        }
        paths = extractor.Archive.package_paths(files)
        self.assertEqual(1, paths["/game/actions/ia_walk"])
        self.assertEqual(2, paths["/pbzinput/actions/ia_walk"])
        self.assertEqual(3, paths["/sgamecombatsystem/weapon/input/ia_changeweapon"])
        self.assertEqual(4, paths["/game/characters/mojian/ia_test"])
        archive = extractor.Archive.__new__(extractor.Archive)
        archive.packages, archive.action_settings = paths, {}
        calls = []
        archive.resource = lambda entry: calls.append(entry) or b"package"
        action = package([header({10: False}) + integer(2), setting(0)])
        with patch.object(extractor, "Package", return_value=action):
            self.assertEqual("IA_Registered", archive.input_action_settings("/Game/Characters/Mojian/IA_Test"))
            self.assertEqual("IA_Registered", archive.input_action_settings("/GAME/Characters/MoJian/IA_Test"))
        self.assertEqual([4], calls)
        with self.assertRaisesRegex(ValueError, "package not found"):
            archive.input_action_settings("/Game/Missing/IA_Walk")

    def test_shadow_arm_projection_is_build_specific(self):
        candidates = [
            {"action": "IA_ShadowArm", "rawSlot": 2, "device": "KeyboardMouse", "key": "LeftControl"},
            {"action": "IA_ShadowArm", "rawSlot": 2, "device": "Gamepad", "key": "Gamepad_RightTriggerAxis"},
        ]
        self.assertEqual([candidates[1]], extractor.effective_bindings(candidates, "CL-301524"))
        self.assertEqual([candidates[0]], extractor.effective_bindings(candidates, "CL-302256"))

    def test_extraction_includes_contexts_outside_input_directories(self):
        main = "../../../ProjectPBZ/Plugins/PBZInput/Content/IMC_Main.uasset"
        qte = "../../../ProjectPBZ/Content/QTE/IMC_QTE.uasset"
        class FakeArchive:
            def __init__(self, *args):
                self.files = {main: 1, qte: 2, "../../../ProjectPBZ/Content/Actions/IA_Test.uasset": 3}
                self.closed = False
            def resource(self, entry):
                return entry
            def input_action_settings(self, path):
                return None
            def close(self):
                self.closed = True
        archive = FakeArchive()
        decoded = context([mapping(0, behavior=2)])
        with patch.object(extractor, "Archive", return_value=archive), patch.object(extractor, "Package", return_value=decoded):
            result = extractor.extract(None, None, None)
        self.assertEqual([qte.removeprefix("../../../"), main.removeprefix("../../../")], [row["context"] for row in result["contexts"]])
        self.assertEqual([], result["bindings"])
        self.assertEqual(2, len(extractor.compact(result)["fixedMappings"][0]["sources"]))
        self.assertTrue(archive.closed)


if __name__ == "__main__":
    unittest.main()
