# Dark UI artwork

These are display assets prepared from the user's local official wallpaper collection and this repository's existing app icon. The game artwork and wordmark are official Phantom Blade Zero material, not original artwork or a new logo created for PBZKeyForge. The original files are unchanged.

| Output | Size | Source and processing |
| --- | --- | --- |
| `wallpaper.jpg` | 2560 × 1365, RGB | Maintainer-supplied official game wallpaper, originally 6000 × 3700. Crop `(0, 0, 6000, 3200)`, then Lanczos resize and JPEG quality 95 with no chroma subsampling. The source file is not part of this repository. |
| `game-wordmark.png` | 819 × 378, RGBA | Extract the official lower-right wordmark from scene3 crop `(5000, 3200, 5900, 3620)`. A continuous luminance alpha ramp and neutral-color mask remove the dark background; 12px transparent padding retains thin brush tips and the small English caption. |
| `app-mark.png` | 512 × 512, RGBA | Adapt `assets/icon.png`: remove its light paper background with continuous alpha, change black ink to warm white, and retain the diagonal red ink in cinnabar. Preserve the source size and original icon file. |

The source scene's black sponsor strip starts near y3380, but its wordmark overlaps the painting from about y3223. Stopping at y3200 removes both and avoids clipped white lettering in the background. This retains the red prayer tree and swordsman composition, with about 5% of the painting's bottom edge excluded. The background is not stretched, darkened, or repainted; the UI applies its own cover crop and dark gradients.

Rebuild from the repository root with Python and Pillow:

```powershell
python build\prepare-ui-assets.py --scene "<path-to-source-wallpaper>"
```

To use another copy of the same original image:

```powershell
python build\prepare-ui-assets.py --scene "<path-to-source-wallpaper>"
```

The script validates source dimensions because its extraction coordinates apply to the 6000 × 3700 original. `--scene` must be supplied explicitly; `--icon` and `--output` can override their repository-relative defaults. Generating these assets uses local files only.
