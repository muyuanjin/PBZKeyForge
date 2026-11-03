"""Extract named action symbols from an atlas and preview the complete icon set."""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ACTIONS = [
    ("IA_LightAttack", "Light attack"),
    ("IA_HeavyAttack", "Heavy attack"),
    ("IA_Guard", "Block"),
    ("IA_Dodge", "Dodge"),
    ("IA_Jump", "Jump"),
    ("IA_Crouch", "Crouch"),
    ("IA_MoveFast", "Sprint"),
    ("IA_MoveSlow", "Walk"),
    ("IA_LockTarget", "Lock on"),
    ("IA_Interact", "Interact / talk"),
    ("IA_Execute", "Execute"),
    ("IA_ShadowArm", "Phantom Edges"),
    ("IA_ShadowArm_LongPress", "Phantom Edges (hold)"),
    ("IA_SwitchWeapon", "Switch weapon"),
    ("IA_SwitchShadowArm", "Switch Phantom Edge"),
    ("IA_UltimateSkill", "Power Surge"),
    ("IA_Sense_Default", "Mind's Eye"),
    ("IA_OpenCharacterMenu", "Character"),
    ("IA_OpenMapMenu", "Map"),
    ("IA_UseTopItem", "Upper item"),
    ("IA_UseBottomItem", "Lower item"),
]


def write_preview(output, destination):
    preview = Image.new("RGB", (960, ((len(ACTIONS) + 3) // 4) * 164), "#191D20")
    draw = ImageDraw.Draw(preview)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 17)
    small_font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 11)
    for index, (action, label) in enumerate(ACTIONS):
        column, row = index % 4, index // 4
        icon = Image.open(output / (action + ".png")).convert("RGBA")
        x, y = column * 240, row * 164
        preview.paste(icon, (x + 20, y + 3), icon)
        tiny = icon.resize((32, 32), Image.Resampling.LANCZOS)
        preview.paste(tiny, (x + 177, y + 47), tiny)
        draw.text((x + 15, y + 125), label, font=font, fill="#EEEAE1")
        draw.text((x + 15, y + 147), action, font=small_font, fill="#ADB2B1")
    destination.parent.mkdir(parents=True, exist_ok=True)
    preview.save(destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("atlas", type=Path, nargs="?")
    parser.add_argument("--output", type=Path, default=Path("assets/ui/actions"))
    parser.add_argument("--preview", type=Path, default=Path("dist/action-icons-preview.png"))
    parser.add_argument("--preview-only", action="store_true")
    parser.add_argument("--alpha-floor", type=int, default=220,
                        help="Discard soft halo alpha below this value before extracting the glyphs.")
    parser.add_argument("--columns", type=int, default=4)
    parser.add_argument("--actions", default="",
                        help="Comma-separated action IDs in row-major atlas order; only these files are replaced.")
    parser.add_argument("--row-boundaries", default="",
                        help="Comma-separated pixel positions delimiting every atlas row.")
    args = parser.parse_args()
    if args.preview_only:
        write_preview(args.output, args.preview)
        return
    if args.atlas is None:
        parser.error("an atlas is required unless --preview-only is used")
    if not 0 <= args.alpha_floor < 250:
        parser.error("--alpha-floor must be between 0 and 249")
    actions = args.actions.split(",") if args.actions else [action for action, label in ACTIONS]
    known = {action for action, label in ACTIONS}
    if any(action not in known for action in actions) or args.columns < 1:
        parser.error("use known action IDs and at least one atlas column")
    rows = (len(actions) + args.columns - 1) // args.columns
    atlas = Image.open(args.atlas).convert("RGBA")
    alpha = atlas.getchannel("A").point(
        lambda value: max(0, min(255, (value - args.alpha_floor) * 255 // (250 - args.alpha_floor))))
    atlas = Image.new("RGBA", atlas.size, (238, 234, 225, 0))
    atlas.putalpha(alpha)
    row_boundaries = ([int(value) for value in args.row_boundaries.split(",")]
                      if args.row_boundaries else [round(index * atlas.height / rows) for index in range(rows + 1)])
    if len(row_boundaries) != rows + 1 or any(left >= right for left, right in zip(row_boundaries, row_boundaries[1:])):
        parser.error("--row-boundaries requires one increasing pixel position per row edge")
    args.output.mkdir(parents=True, exist_ok=True)
    for index, action in enumerate(actions):
        column, row = index % args.columns, index // args.columns
        cell = atlas.crop((round(column * atlas.width / args.columns), row_boundaries[row],
                           round((column + 1) * atlas.width / args.columns), row_boundaries[row + 1]))
        bounds = cell.getchannel("A").point(lambda value: 255 if value > 12 else 0).getbbox()
        if bounds is None:
            raise ValueError("Empty atlas cell: " + action)
        symbol = cell.crop(bounds)
        symbol.thumbnail((96, 96), Image.Resampling.LANCZOS)
        icon = Image.new("RGBA", (128, 128))
        icon.alpha_composite(symbol, ((128 - symbol.width) // 2, (128 - symbol.height) // 2))
        icon.save(args.output / (action + ".png"), optimize=True)
    write_preview(args.output, args.preview)
    print("Prepared {0} action icons in {1}".format(len(actions), args.output))


if __name__ == "__main__":
    main()
