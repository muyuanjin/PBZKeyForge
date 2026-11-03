# Device artwork

The application uses four transparent PNG diagrams with matching graphite bodies and fine grey outlines. Key labels and binding indicators are drawn by the interface.

| File | Canvas | Layout |
| --- | --- | --- |
| `keyboard.png` | 1497 × 572 | 87-key tenkeyless keyboard |
| `mouse.png` | 457 × 745 | Five buttons, including two left-side thumb buttons |
| `xbox.png` | 1058 × 726 | Asymmetric sticks |
| `playstation.png` | 1103 × 711 | Symmetric sticks and central touchpad |

`hotspots.json` gives button centers normalized to each complete image's canvas, with the upper-left as `(0, 0)` and lower-right as `(1, 1)`. Preserve the image's aspect ratio before mapping these points into the control rectangle. Controller triggers, shoulders, D-pad directions and stick clicks have separate points. Mouse button 4 is the lower thumb button; button 5 is the upper one.

`sources/device-atlas.png` is the 2304 × 1536 master atlas. `render.py` crops it and writes the PNGs and hotspot coordinates. It requires Pillow:

```powershell
python assets\ui\devices\render.py assets\ui\devices\sources\device-atlas.png
```

## SVG alternatives

`mouse.svg` adapts Lucide's mouse outline with a divided button area, scroll wheel and two thumb buttons. The original outline and ISC license are retained in `sources/`.

- Copyright (c) 2026 Lucide Icons and Contributors.
- License: ISC; see `sources/lucide-LICENSE.txt`.
- Source: https://github.com/lucide-icons/lucide/blob/7bb15b80a3c885c68d2c65a11381f25920bbf1ef/icons/mouse.svg

`xbox.svg` and `playstation.svg` compose the white Xbox One and DualShock 4 skins from e7d/gamepad-viewer. Source SVG layers, sprite coordinates, CSS and the MIT license are retained in `sources/`. The controller illustrations use these skins as geometry references.

- Copyright (c) 2017-2020 Michaël "e7d" Ferrand.
- License: MIT; see `sources/gamepad-viewer-LICENSE.txt`. Include this license when distributing the controller artwork, including bitmap copies.
- Source: https://github.com/e7d/gamepad-viewer/tree/563552931253a2e558cf6fd5fc7fd254eefd9b2f/templates
- The source project credits mrmcpowned's gamepadviewer.com artwork as its starting point.

`render-svg.py` rebuilds the SVG alternatives and renders them as `mouse-svg.png`, `xbox-svg.png` and `playstation-svg.png` using ImageMagick.
