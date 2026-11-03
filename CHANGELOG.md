# Changelog

## Unreleased

- Added shared key and action views, clickable keyboard/mouse/controller diagrams, and factory/saved/pending status badges.
- Added whole-scheme comparisons against saved or factory bindings, with four row presentation modes.
- Added explicit unbinding, concrete factory keys in binding dialogs, and resource-based action and raw-slot defaults for CL-301524.
- Added the mouse attack/guard preset while retaining F heavy attack; corrected shared slot ownership and registered controller slot handling.
- Added 21 action icons, device artwork sources, and third-party license notices; corrected the Chinese Mind's Eye action name.
- Made full language sweeps opt-in with `--all-languages`; routine integration checks use Simplified Chinese.

## 1.1.1 — 2026-11-03

- Fixed an unhandled `ContextMenuStrip` disposal exception after clicking More-menu actions, including Open backup folder.
- Added mouse-message regression checks for every More-menu action, repeated openings, cancellations, and all interface languages.

## 1.1.0 — 2026-11-03

- Rebuilt the editor around scheme-scoped keyboard and gamepad drafts, with refresh, preview, conflict handling, undo, and explicit apply.
- Added verified game-scheme activation through the game's shared settings file, with PBZS build recognition, key fingerprint checks, AES-GCM authentication, backups, and rejection of unauthenticated or unsupported data.
- Added current-scheme default restoration while keeping the separate all-schemes reset.
- Reworked the window frame, DPI-aware layouts, localized controls, and dialog action layout; removed ambiguous quick-setup labels.
- Expanded the interface to 15 languages and added focused session, UI, scheme-activation, and translation checks.

## 1.0.4

- The tool finds the save by itself on startup; no more hunting for the file.
- A four-step strip at the top shows where you are: find the save, pick a preset, apply, save.
- One primary button, `One-key guard`, applies guard-on-attack for you. The last step is still Save.
- Key slots that have no override show `-` in grey instead of an empty box.

## 1.0.3

- The action grid scrolls. Before this, a window shorter than the 20-row table simply cut the lower actions off with no way to reach them.
- Minimum window size is 700x480, so it still works on a small screen.

## 1.0.2

- The download no longer ships `PBZKeyForge-cli.exe`. It stays in the build for CI (preset generation and round-trip self tests) but players only get the editor and the preset save files.
- Release notes now point at the source and state that the binaries are built and uploaded by GitHub Actions.

## 1.0.1

- Action names in the UI now match the in-game key bindings screen (zh-Hans, English, Japanese, Korean, Russian).
- Fixed: the scheme list rebuilt itself while a save was loading and wiped the grid, so an existing save looked empty.
- Added `--gui <save>` to open the editor on a file, and `--shot <save> <png>` to render the window to an image.
- Assets: real UI screenshot.

## 1.0.0 — 2026-11-02

First release.

- Reads and writes `EnhancedInputUserSettings.sav` directly, so one key can drive several
  actions; the in-game screen rejects that combination, the file format does not.
- Scheme aware: edit `Default`, `Custom 1` or `Custom 2`, and switch the game to any of
  them from the tool by writing `CurrentProfileIdentifierString`.
- Seven presets: one-key attack & guard, one-key plus guard key becomes dodge, Souls-style
  gamepad layout, Space = sprint, Space = dodge, undo, and vanilla.
- Five languages (English, Simplified Chinese, Japanese, Korean, Russian) with automatic
  detection and a toolbar switch.
- Writes a timestamped backup, refuses to run while the game is open, and re-reads the file
  after saving.
- CLI (`PBZKeyForge-cli.exe`) with `--selftest`, `--schemes`, `--list`, `--apply-preset`,
  `--activate`, `--make-vanilla` and `--gen-presets`.
- Release ships both the executables and a preset-only archive for players who prefer not
  to run a binary.

Verified against the Steam launch build `CL-301524`: byte-identical GVAS round-trip, and
preset writes validated against a strict structural checker.




