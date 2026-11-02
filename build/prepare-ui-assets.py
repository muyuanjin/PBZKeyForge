"""Prepare the local official PBZ artwork and existing app icon for the dark UI.

Requires Pillow. No network requests or image-generation services are used.
"""

import argparse
from pathlib import Path

from PIL import Image, ImageChops


ROOT = Path(__file__).resolve().parents[1]


def prepare_background(source: Image.Image, output: Path) -> None:
    # The sponsor strip starts near y3380; the official wordmark overlaps the
    # painting from y3223. Stop above both instead of leaving a clipped logo.
    painting = source.crop((0, 0, 6000, 3200))
    painting = painting.resize((2560, 1365), Image.Resampling.LANCZOS)
    painting.save(output / "wallpaper.jpg", quality=95, subsampling=0, optimize=True)


def prepare_wordmark(source: Image.Image, output: Path) -> None:
    # Includes all Chinese brush tips and the small PHANTOM BLADE caption.
    region = source.crop((5000, 3200, 5900, 3620)).convert("RGB")
    gray = region.convert("L")
    # The white logo is over a dark painting / black strip. A continuous alpha
    # ramp removes the backdrop while retaining intermediate antialias levels.
    alpha = gray.point(lambda value: max(0, min(255, round((value - 100) * 255 / 155))))
    # Reject chromatic background pixels; neutral white brush edges remain.
    red, green, blue = region.split()
    spread = ImageChops.lighter(ImageChops.difference(red, green), ImageChops.difference(red, blue))
    neutral = spread.point(lambda value: max(0, min(255, 255 - max(0, value - 12) * 12)))
    alpha = ImageChops.multiply(alpha, neutral)
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError("No white game wordmark found in the expected crop.")
    # Transparent padding protects the very thin right-hand and lower strokes.
    left, top, right, bottom = bounds
    padded = (max(0, left - 12), max(0, top - 12), min(region.width, right + 12), min(region.height, bottom + 12))
    wordmark = Image.new("RGBA", region.size, (247, 245, 240, 0))
    wordmark.putalpha(alpha)
    wordmark.crop(padded).save(output / "game-wordmark.png", optimize=True)


def prepare_app_mark(source: Image.Image, output: Path) -> None:
    source = source.convert("RGB")
    if source.size != (512, 512):
        raise ValueError("The existing app icon must be the 512px source.")
    pixels = []
    for red, green, blue in source.getdata():
        # Paper is light and slightly warm. Remove it with a smooth alpha ramp;
        # convert the black ink to warm white and retain the red diagonal ink.
        is_red = red > green + 25 and red > blue + 25
        if is_red:
            alpha = max(0, min(255, round((225 - min(green, blue)) * 255 / 210)))
            pixels.append((190, 43, 35, alpha))
        else:
            brightness = round(0.2126 * red + 0.7152 * green + 0.0722 * blue)
            alpha = max(0, min(255, round((225 - brightness) * 255 / 210)))
            pixels.append((239, 235, 224, alpha))
    mark = Image.new("RGBA", source.size)
    mark.putdata(pixels)
    mark.save(output / "app-mark.png", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scene", type=Path, required=True, help="Path to the original 6000x3700 wallpaper image")
    parser.add_argument("--icon", type=Path, default=ROOT / "assets" / "icon.png")
    parser.add_argument("--output", type=Path, default=ROOT / "assets" / "ui")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    with Image.open(args.scene) as source:
        if source.size != (6000, 3700):
            raise ValueError("Expected the original 6000x3700 scene3.jpg; crop coordinates must be reviewed for other images.")
        prepare_background(source.convert("RGB"), args.output)
        prepare_wordmark(source, args.output)
    with Image.open(args.icon) as icon:
        prepare_app_mark(icon, args.output)
    for name in ("wallpaper.jpg", "game-wordmark.png", "app-mark.png"):
        path = args.output / name
        with Image.open(path) as image:
            print("{}: {}x{} {} ({} bytes)".format(path, image.width, image.height, image.mode, path.stat().st_size))


if __name__ == "__main__":
    main()
