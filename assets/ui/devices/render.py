from pathlib import Path
import argparse
import json

from PIL import Image


ROOT = Path(__file__).resolve().parent
CROPS = {
    'keyboard': (43, 119, 1540, 691),
    'mouse': (1661, 31, 2118, 776),
    'xbox': (66, 770, 1124, 1496),
    'playstation': (1179, 777, 2282, 1488),
}
POINTS = {
    'mouse': {
        'LeftMouseButton': (1780, 220), 'RightMouseButton': (2000, 220),
        'MiddleMouseButton': (1888, 200), 'ThumbMouseButton': (1722, 475),
        'ThumbMouseButton2': (1705, 350),
    },
    'xbox': {
        'Gamepad_LeftTrigger': (332, 825), 'Gamepad_RightTrigger': (860, 825),
        'Gamepad_LeftShoulder': (335, 868), 'Gamepad_RightShoulder': (850, 868),
        'Gamepad_FaceButton_Bottom': (877, 1086), 'Gamepad_FaceButton_Right': (940, 1020),
        'Gamepad_FaceButton_Left': (809, 1020), 'Gamepad_FaceButton_Top': (877, 956),
        'Gamepad_LeftThumbstick': (328, 1028), 'Gamepad_RightThumbstick': (730, 1178),
        'Gamepad_DPad_Up': (463, 1139), 'Gamepad_DPad_Down': (463, 1233),
        'Gamepad_DPad_Left': (413, 1183), 'Gamepad_DPad_Right': (512, 1183),
        'Gamepad_Special_Left': (521, 1025), 'Gamepad_Special_Right': (672, 1025),
    },
    'playstation': {
        'Gamepad_LeftTrigger': (1425, 820), 'Gamepad_RightTrigger': (2035, 820),
        'Gamepad_LeftShoulder': (1425, 861), 'Gamepad_RightShoulder': (2030, 861),
        'Gamepad_FaceButton_Bottom': (2050, 1095), 'Gamepad_FaceButton_Right': (2123, 1025),
        'Gamepad_FaceButton_Left': (1980, 1025), 'Gamepad_FaceButton_Top': (2050, 956),
        'Gamepad_LeftThumbstick': (1575, 1168), 'Gamepad_RightThumbstick': (1890, 1168),
        'Gamepad_DPad_Up': (1410, 970), 'Gamepad_DPad_Down': (1410, 1078),
        'Gamepad_DPad_Left': (1353, 1025), 'Gamepad_DPad_Right': (1460, 1025),
        'Gamepad_Special_Left': (1520, 926), 'Gamepad_Special_Right': (1945, 926),
    },
}


def keyboard_points():
    points = {}

    def row(names, xs, y):
        for name, x in zip(names.split(), xs):
            points[name] = (x, y)

    row('Escape F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 PrintScreen ScrollLock Pause',
        [137, 278, 356, 431, 507, 613, 690, 765, 839, 946, 1025, 1099, 1180, 1286.5, 1364.5, 1442.5], 208)
    row('Tilde One Two Three Four Five Six Seven Eight Nine Zero Hyphen Equals BackSpace',
        [136, 211, 286, 361, 438, 513, 588, 663, 738, 812, 887, 961, 1037, 1152], 307)
    row('Tab Q W E R T Y U I O P LeftBracket RightBracket Backslash',
        [154, 249, 324, 399, 473, 548, 623, 698, 773, 849, 924, 999, 1074, 1165], 382)
    row('CapsLock A S D F G H J K L Semicolon Quote Enter',
        [163, 264, 340, 416, 492, 568, 644, 720, 796, 872, 948, 1024, 1138], 454)
    row('LeftShift Z X C V B N M Comma Period Slash RightShift',
        [177, 295, 369, 444, 520, 596, 672, 747, 823, 899, 975, 1123], 526)
    row('LeftControl LeftCommand LeftAlt SpaceBar RightAlt RightCommand Menu RightControl',
        [146, 239, 330, 611, 896, 987, 1080, 1177], 598)
    row('Insert Home PageUp', [1286.5, 1364.5, 1442.5], 310)
    row('Delete End PageDown', [1286.5, 1364.5, 1442.5], 381)
    row('Left Down Right', [1286.5, 1364.5, 1442.5], 594.5)
    points['Up'] = (1364.5, 522)
    return points


def main():
    parser = argparse.ArgumentParser(description='Crop the 2304 x 1536 device atlas and export normalized button centers.')
    parser.add_argument('atlas', type=Path)
    args = parser.parse_args()
    atlas = Image.open(args.atlas).convert('RGBA')
    if atlas.size != (2304, 1536):
        parser.error('The device atlas must be 2304 x 1536 pixels.')
    POINTS['keyboard'] = keyboard_points()
    hotspots = {}
    for name, box in CROPS.items():
        device = atlas.crop(box)
        device.save(ROOT / (name + '.png'))
        width, height = device.size
        hotspots[name] = {
            'canvas': [width, height],
            'points': {key: [round((x - box[0]) / width, 5), round((y - box[1]) / height, 5)]
                for key, (x, y) in POINTS[name].items()},
        }
    (ROOT / 'hotspots.json').write_text(json.dumps(hotspots, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
