# Changelog

## 1.3.2 — 2026-11-09

- Search the expanded move catalog with separate terms in any order. Weapon, move, upgrade and technical names can be combined, and equivalent consecutive searches reuse their results.
- Show every official name shared by one action as its own picker row. The chosen name is retained while adding or editing the binding, and selecting several names for the same action creates one output.
- Show attack insight for bindable moves: physical or elemental coefficients, poise coefficients, active conditional ranges and timeline attack nodes. Coefficients also participate in search. The tooltips distinguish these values from final health damage and actual hit counts.
- Preview saved presets and review their target scheme, binding changes and script or input effects before loading.
- Preserve explicit stance and combined-guard eligibility while keeping paired Deflect state independent. Stabilize player-session recovery, scheme menus, window restoration and deployment comparison.
- Keep edits to an inactive enhancement scheme attached to that scheme, and make the scheme notice use the runtime's expected name value.

## 1.3.1 — 2026-11-08

- Reduced repeated ability-binding, input-pipeline, ledge-protection and temporary-equipment work while continuing to check input and scene changes every frame.
- Expanded weapon and Phantom Edge entries with searchable localized move names, weapon chains, charged releases and follow-up phases.
- Fixed One-Eyed Bow preparation and held input, repeated activation for default ability bindings, and overlapping independently held Deflect inputs.
- Added separate restriction bypasses for equipment, training, ground/air, stance, predecessor and cooldown, with real preparation and restoration.
- Added full-resource release effects, super armor and invincibility, and retained independent Deflect and Stance behavior.

## 1.2.3 — 2026-11-05

- Add optional “Perfect guard: no rapid-click penalty” in the footer, off by default. Check it and Apply changes to install; uncheck and apply to restore the original rules. It applies across schemes, difficulties and guard keys while retaining the game's base timing and eligibility rules.
- Support Steam patch 1.0302256 (`CL-302256`) alongside the launch build, including scheme activation and the updated Phantom Edge binding slots. Existing built-in layouts and PBZ2 share codes keep the same baseline.
- Remember the last opened save and use Steam's installation records to find the current game folder. Discover compatible scheme settings in later builds in the background, preserving bindings confirmed while editing.
- Reduce startup, binding refresh, preset preview and preset-library overhead.
- Improve keyboard/mouse and action-table layout at smaller window sizes, refresh the interaction icon, and prevent overlapping text while scrolling scheme comparisons.
- Show explicit exit-game guidance on Apply while the game is running; stabilize save progress feedback and avoid a spinner flash for brief writes.

The optional gameplay patch is included only in the editor, not the presets-only archive.

## 1.2.2 — 2026-11-04

- Load and authenticate the detected save while building the interface, then display the completed first frame.
- Batch initial layouts, defer controller artwork until first use, and use premultiplied images for drawing.
- Reduce repeated binding scans and sorting during interface refreshes; buffer executable reads for build authentication.
- Add startup readiness and clean-session assertions to the visible window checks.

## 1.2.0 — 2026-11-04

- Reorganized preset management around saving the current scheme, browsing personal presets and importing share codes; added name-field focus, placeholder and interactive empty-name guidance.
- Replaced the cycling apply-mode button with three directly selectable options beside the preset picker; Preset only is the initial mode and the application remembers the selection.
- Moved Apply off the UI thread, added a progress spinner and retained drafts after failed writes; cached unchanged executable identity checks to reduce repeated application time.
- Added gray crossed-out removed actions and visible per-action binding capacity, including a clear explanation when another key cannot be added.
- Added the mouse attack/guard and automatic-sprint layouts: the latter uses W/A/D for sprint and Space for dodge/jump, retaining F heavy attack and Shift dodge.

- Added named user presets, batch deletion, game-file export and compact PBZ2 share codes using Protobuf, stable binding IDs and explicit keyboard/controller scopes.
- Added sharing device selection and live code lengths; Preset only preserves devices outside the preset's scope.
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




