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
