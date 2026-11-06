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
BOUNDS = {'keyboard': {'Escape': [67, 62, 52, 53],
              'F1': [211, 62, 52, 53],
              'F2': [287, 62, 53, 53],
              'F3': [364, 62, 52, 53],
              'F4': [441, 62, 53, 53],
              'F5': [545, 62, 52, 53],
              'F6': [622, 62, 52, 53],
              'F7': [699, 62, 52, 53],
              'F8': [775, 62, 53, 53],
              'F9': [879, 62, 53, 53],
              'F10': [958, 62, 53, 53],
              'F11': [1035, 62, 52, 53],
              'F12': [1112, 62, 54, 53],
              'PrintScreen': [1217, 62, 54, 53],
              'ScrollLock': [1295, 62, 54, 53],
              'Pause': [1373, 62, 53, 53],
              'Tilde': [67, 159, 52, 54],
              'One': [144, 159, 50, 54],
              'Two': [219, 159, 50, 54],
              'Three': [294, 159, 50, 54],
              'Four': [369, 159, 50, 54],
              'Five': [444, 159, 50, 54],
              'Six': [520, 159, 50, 54],
              'Seven': [595, 159, 50, 54],
              'Eight': [670, 159, 50, 54],
              'Nine': [745, 159, 50, 54],
              'Zero': [820, 159, 50, 54],
              'Hyphen': [895, 159, 50, 54],
              'Equals': [970, 159, 50, 54],
              'BackSpace': [1046, 159, 121, 54],
              'Tab': [67, 233, 89, 54],
              'Q': [181, 233, 50, 54],
              'W': [256, 233, 50, 54],
              'E': [332, 233, 50, 54],
              'R': [407, 233, 50, 54],
              'T': [482, 233, 51, 54],
              'Y': [558, 233, 50, 54],
              'U': [633, 233, 50, 54],
              'I': [708, 233, 50, 54],
              'O': [782, 233, 50, 54],
              'P': [857, 233, 51, 54],
              'LeftBracket': [933, 233, 50, 54],
              'RightBracket': [1008, 233, 50, 54],
              'Backslash': [1084, 233, 83, 54],
              'CapsLock': [67, 305, 106, 53],
              'A': [198, 305, 50, 53],
              'S': [273, 305, 51, 53],
              'D': [348, 305, 51, 53],
              'F': [424, 305, 50, 53],
              'G': [500, 305, 51, 53],
              'H': [575, 305, 51, 53],
              'J': [651, 305, 50, 53],
              'K': [725, 305, 51, 53],
              'L': [800, 305, 50, 53],
              'Semicolon': [875, 305, 50, 53],
              'Quote': [950, 305, 50, 53],
              'Enter': [1025, 305, 142, 53],
              'LeftShift': [67, 377, 144, 53],
              'Z': [236, 377, 50, 53],
              'X': [311, 377, 51, 53],
              'C': [387, 377, 50, 53],
              'V': [462, 377, 50, 53],
              'B': [537, 377, 51, 53],
              'N': [613, 377, 50, 53],
              'M': [688, 377, 50, 53],
              'Comma': [762, 377, 50, 53],
              'Period': [837, 377, 51, 53],
              'Slash': [912, 377, 51, 53],
              'RightShift': [989, 377, 178, 53],
              'LeftControl': [67, 449, 71, 53],
              'LeftCommand': [164, 449, 66, 53],
              'LeftAlt': [255, 449, 65, 53],
              'SpaceBar': [346, 449, 448, 53],
              'RightAlt': [819, 449, 68, 53],
              'RightCommand': [912, 449, 68, 53],
              'Menu': [1005, 449, 65, 53],
              'RightControl': [1097, 449, 70, 53],
              'Insert': [1217, 159, 54, 54],
              'Home': [1295, 159, 54, 54],
              'PageUp': [1373, 159, 53, 54],
              'Delete': [1217, 233, 54, 54],
              'End': [1295, 233, 54, 54],
              'PageDown': [1373, 233, 53, 54],
              'Up': [1295, 375, 54, 54],
              'Left': [1217, 449, 54, 53],
              'Down': [1295, 449, 54, 53],
              'Right': [1373, 449, 53, 53]},
 'mouse': {'LeftMouseButton': [66, 65, 125, 235],
           'RightMouseButton': [263, 65, 126, 235],
           'MiddleMouseButton': [204, 108, 43, 129],
           'ThumbMouseButton': [48, 391, 35, 108],
           'ThumbMouseButton2': [26, 275, 40, 102]},
 'xbox': {'Gamepad_LeftTrigger': [221, 25, 113, 43],
          'Gamepad_RightTrigger': [727, 25, 113, 43],
          'Gamepad_LeftShoulder': [205, 83, 152, 35],
          'Gamepad_RightShoulder': [699, 83, 152, 35],
          'Gamepad_FaceButton_Bottom': [782, 285, 61, 61],
          'Gamepad_FaceButton_Right': [845, 218, 61, 61],
          'Gamepad_FaceButton_Left': [712, 218, 61, 61],
          'Gamepad_FaceButton_Top': [783, 154, 61, 61],
          'Gamepad_LeftThumbstick': [203, 194, 119, 119],
          'Gamepad_RightThumbstick': [604, 342, 119, 119],
          'Gamepad_DPad_Up': [377, 340, 41, 49],
          'Gamepad_DPad_Down': [377, 436, 41, 44],
          'Gamepad_DPad_Left': [325, 391, 51, 42],
          'Gamepad_DPad_Right': [420, 391, 49, 42],
          'Gamepad_Special_Left': [434, 234, 43, 43],
          'Gamepad_Special_Right': [586, 234, 43, 43]},
 'playstation': {'Gamepad_LeftTrigger': [190, 26, 119, 35],
                 'Gamepad_RightTrigger': [797, 26, 119, 35],
                 'Gamepad_LeftShoulder': [184, 70, 126, 24],
                 'Gamepad_RightShoulder': [794, 70, 126, 24],
                 'Gamepad_FaceButton_Bottom': [844, 286, 62, 62],
                 'Gamepad_FaceButton_Right': [913, 218, 62, 62],
                 'Gamepad_FaceButton_Left': [770, 218, 62, 62],
                 'Gamepad_FaceButton_Top': [844, 150, 62, 62],
                 'Gamepad_LeftThumbstick': [336, 333, 117, 117],
                 'Gamepad_RightThumbstick': [652, 333, 117, 117],
                 'Gamepad_DPad_Up': [208, 168, 46, 59],
                 'Gamepad_DPad_Down': [208, 271, 46, 56],
                 'Gamepad_DPad_Left': [151, 225, 57, 46],
                 'Gamepad_DPad_Right': [254, 225, 57, 46],
                 'Gamepad_Special_Left': [329, 122, 24, 53],
                 'Gamepad_Special_Right': [755, 122, 24, 53]}}


def render(atlas, output):
    output.mkdir(parents=True, exist_ok=True)
    hotspots = {}
    for name, box in CROPS.items():
        device = atlas.crop(box)
        device.save(output / (name + '.png'))
        hotspots[name] = {
            'canvas': list(device.size),
            'bounds': BOUNDS[name],
        }
    (output / 'hotspots.json').write_text(json.dumps(hotspots, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description='Crop the 2304 x 1536 device atlas and export button face bounds in cropped-image pixels.')
    parser.add_argument('atlas', type=Path)
    args = parser.parse_args()
    atlas = Image.open(args.atlas).convert('RGBA')
    if atlas.size != (2304, 1536):
        parser.error('The device atlas must be 2304 x 1536 pixels.')
    render(atlas, ROOT)


if __name__ == '__main__':
    main()
