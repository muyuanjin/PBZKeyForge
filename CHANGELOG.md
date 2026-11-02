## 1.0.2

- The download no longer ships `PBZKeyForge-cli.exe`. It stays in the build for CI (preset generation and round-trip self tests) but players only get the editor and the preset save files.
- Release notes now point at the source and state that the binaries are built and uploaded by GitHub Actions.

## 1.0.1

- Action names in the UI now match the in-game key bindings screen (zh-Hans, English, Japanese, Korean, Russian).
- Fixed: the scheme list rebuilt itself while a save was loading and wiped the grid, so an existing save looked empty.
- Added `--gui <save>` to open the editor on a file, and `--shot <save> <png>` to render the window to an image.
- Assets: real UI screenshot.

# Changelog

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


