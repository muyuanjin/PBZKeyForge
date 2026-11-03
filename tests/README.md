# Local verification

Integration checks use Simplified Chinese by default. Full language sweeps are opt-in and reserved for targeted localization testing: append `--all-languages` to the relevant command, for example `.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-language-check --all-languages`. The dedicated `I18nTranslationTests` is also run separately only for localization work; it is not part of routine verification.

Run from the repository root after `powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1`. These checks use Windows' bundled .NET Framework compiler:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sessionSources = @(Get-ChildItem src -Filter *.cs | ForEach-Object { $_.FullName })
& $compiler /nologo /main:EditorSessionTests /out:dist\EditorSessionTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:third_party\protobuf-net\protobuf-net.dll /resource:third_party\protobuf-net\protobuf-net.dll,PBZKeyForge.protobuf-net $sessionSources tests\EditorSessionTests.cs
.\dist\EditorSessionTests.exe samples\vanilla-base.sav
```

The session checks create fresh temporary copies. They cover disk refresh and conflicts, per-scheme keyboard and controller drafts, isolation between profiles with the same controller family, preview replacement/cancellation, global reset/undo, unknown data preservation, interrupted writes, resource-based raw slots for crouch/menu/items, all three Shadow Arm controller slots, shared Blob ownership, and rejection of unregistered controller slots. No game installation is required for this command.

Check the mouse attack/guard preset independently, including the retained F heavy attack binding:

```powershell
& $compiler /nologo /out:dist\MouseGuardPresetTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\MouseGuardPresetTests.cs
.\dist\MouseGuardPresetTests.exe samples\vanilla-base.sav
```

Verify the files shipped in both release packages against the editor catalog, bilingual names/descriptions, factory hover previews and saved editor results:

```powershell
.\dist\PBZKeyForge-cli.exe --gen-presets samples\vanilla-base.sav presets
& $compiler /nologo /out:dist\PresetPackageTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\PresetPackageTests.cs
.\dist\PresetPackageTests.exe samples\vanilla-base.sav presets dist\preset-package-check
```

This checks all eight packaged presets, keyboard and each controller family in every scheme, Replace and Preset only modes on the factory base, and Preset only mode on a customized input. The package contains seven custom layouts and one factory layout; regenerating removes the old selected-scheme restore file. The mouse + Space preset also has explicit assertions for light attack, heavy attack, guard, dodge, sprint and jump, including retained F and Shift. Generated saves must round-trip byte-for-byte; unrelated saved customizations must not enter the preset's target layout. `PRESETS.md` must contain the application's current English and Simplified Chinese names and descriptions. Append intentionally preserves additional existing bindings and is covered by `PresetApplyModeTests`. The release workflow runs the package check before packaging; it does not run a language sweep or open the game.

For authenticated activation checks, set `$gameExe`, `$sharedSave`, and `$inputSave` to files in a supported installed Steam build. Set `$activationArtifacts` to a **new** directory on the game's volume. The harness copies saves and creates a read-only executable hard link; it never edits the source saves.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe dist\PBZKeyForge.exe $gameExe $sharedSave $inputSave $activationArtifacts
```

When the game is running, use the harness's read-only PBZS checks instead. They validate the recognized key fingerprint, reject an unknown executable and tampered containers, and verify the copied save remains unchanged; they do not activate a scheme or require closing the game:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe --read-only dist\PBZKeyForge.exe $gameExe $sharedSave $newReadOnlyArtifacts
```

The resulting directory is a copied installation layout. Use it as the source for session tests that inject an activation failure after bindings have been saved, then verify that retry writes only the remaining activation:

```powershell
.\dist\EditorSessionTests.exe samples\vanilla-base.sav $activationArtifacts $newSessionArtifacts
```

`$newSessionArtifacts` must also be a new directory on the same volume. To test actual GUI controls and capture regular/minimum sizes in Simplified Chinese, run:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\EditorUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\EditorUiTests.cs
.\dist\EditorUiTests.exe "$activationArtifacts\ProjectPBZ\Saved\EnhancedInputUserSettings.sav" $screenshots
```

The GUI check intentionally edits the supplied **copy** through the real Apply button. It opens and cancels every editable keyboard/controller binding cell after scrolling it into view, verifies unavailable slots are disabled, checks clear-override actions, distinguishes left/right modifiers, switches quick configurations, and verifies navigation reads external changes while retaining drafts. Scheme/device tabs, Refresh, Undo and Apply use their control events. Conflict resolution exercises Cancel, Use game and Keep draft through the link's click event and real dialog buttons; this does not test physical mouse hit detection on that link. After applying Custom 1, it compares the other profiles and unrelated data byte-for-byte. It uses the application's DPI manifest and checks custom window controls, centered binding headings, toolbar alignment and the footer. Windows stay transparent during automated screenshots to avoid flashing every test state on the desktop. Inspect the images for clipping and consistency; these checks do not establish actual game startup behavior or every Windows DPI setting. Add `--controls-only` to skip screenshot capture while retaining control assertions. Language selection is independent: `--all-languages` explicitly enables all 15 languages, including with `--controls-only`.

To check only the custom confirmation dialog's action row without a supported game installation, pass a copy of `samples\vanilla-base.sav` and an artifact directory containing any adjacent `PBZSharedSetting.sav` file, then add `--dialog-only`. This asserts that all three buttons remain inside the client area and the Cancel action can be clicked.

For a fresh repository screenshot, pass a copy of the input fixture and output directory as above, then add `--capture`. The capture uses a synthetic active-scheme badge and the UI shows only the final two save-directory components; the full path remains available through the on-screen tooltip.

Translation checks and language provenance are documented in [docs/languages.md](../docs/languages.md). Static evidence for controller profile ownership is in [docs/gamepad-profile-evidence.md](../docs/gamepad-profile-evidence.md).

For binding controls without a game installation, pass a fixture copy and any adjacent `PBZSharedSetting.sav`, then add `--bindings-only`. This checks Default with each slot’s concrete factory key versus Unbound, reopening/cancellation, keyboard and mouse capture, and modifier sides. It captures the default tables and binding dialogs; save persistence is covered by `EditorSessionTests`. Add `--all-languages` only when testing localization.

## More-menu regression checks

Run against a copy of `samples\vanilla-base.sav`; no installed game is needed:

```powershell
Copy-Item samples\vanilla-base.sav dist\menu-test-input.sav
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\EditorMenuTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\EditorMenuTests.cs
.\dist\EditorMenuTests.exe dist\menu-test-input.sav
```

These checks send mouse messages through the real menu's click and auto-close path. They exercise all five More-menu actions, repeated backup-folder openings, file-picker cancellation, reset confirmation/cancellation/undo, pending-draft navigation cancellation, menu labels in the selected test language, and disposal with the owning window. Add `--all-languages` for a targeted menu localization check. Opening the backup folder launches Explorer; rediscovery only reads the detected save. The input copy must remain byte-identical. The suite reproduces the v1.1.0 `ContextMenuStrip` disposal failure; directly calling `ToolStripItem.PerformClick()` does not cover that failure path.

## Binding perspectives

Quick configuration hover previews use an in-memory snapshot and never select or apply the hovered preset. The harness opens the native dropdown with mouse messages, pauses over its header, then exercises item hover, leaving and returning, switching items, and cancellation through the list window. It checks that the popup remains beside the open list without covering options and captures both from the desktop; it does not invoke private hover callbacks. It also checks draft isolation, replacement of an existing preview, mini diagrams, and factory comparison text:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\PresetHoverPreviewTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\PresetHoverPreviewTests.cs
.\dist\PresetHoverPreviewTests.exe samples\vanilla-base.sav dist\preset-preview-check
```

The preview shows the preset on factory defaults, excluding unrelated saved and draft bindings. Changed mappings use blue for both device types; no save-status colors appear. Add `--render-only` to check rendering, preset colors and draft isolation without exercising native mouse hover.

Check the three visible quick-configuration application modes:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\PresetApplyModeTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\PresetApplyModeTests.cs
.\dist\PresetApplyModeTests.exe samples\vanilla-base.sav dist\quick-mode-check
```

Checks Add keys, Replace keys and Preset only against existing custom keys; mode switching and cancellation use the same checkpoint. It covers duplicate no-ops, save/reopen, other-scheme isolation, controller families, atomic capacity rejection and retained mode selection after a failed click. Screenshots show the compact mode switch beside the preset selector at regular and minimum window sizes. Restore factory defaults disables the mode switch. All writes use fixture copies.

Check saved user presets, share codes and game-file export:

```powershell
& $compiler /nologo /out:dist\PresetShareTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\PresetShareTests.cs
.\dist\PresetShareTests.exe samples\vanilla-base.sav dist\preset-share-check
& $compiler /nologo /out:dist\UserPresetTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\UserPresetTests.cs
.\dist\UserPresetTests.exe samples\vanilla-base.sav dist\user-preset-check
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\UserPresetUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\UserPresetUiTests.cs
.\dist\UserPresetUiTests.exe samples\vanilla-base.sav dist\user-preset-ui-check
```

The share checks use an independent Protobuf byte vector to verify stable IDs and packed encoding, raw/DEFLATE selection, skipped metadata, unknown semantic rejection, literal engine keys/family names, frozen resource defaults, explicit empty scopes, and keyboard/controller-family isolation in all three application modes. The model checks save/reload, Unicode names containing colons, pure unbinding, fixed Ctrl plus three editable keys, duplicate names, builtin deletion rejection and source-file protection. Export includes only the selected scheme's draft. The GUI harness uses an isolated preset library and real control events for save, preview, scoped copying with live lengths, import, confirmed/cancelled batch deletion, local mode switching and staging. Screenshots cover regular/minimum windows, name edits across resizing, horizontal fit, and a preset affecting the second controller family. Export serialization is checked through the session API; the native export file picker is not automated.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingViewsTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingViewsTests.cs
.\dist\BindingViewsTests.exe samples\vanilla-base.sav dist\binding-view-check
```

Checks navigation without writes, shared relationship editing, duplicate removal, resource inheritance, controller family isolation, and raw record edits. Physical keyboard and all five mouse hotspots are clicked. Badge checks cover gray/yellow/blue transitions when applying, undoing, restoring factory mappings and removing the last action. Controller triggers aggregate axis and digital inputs; face-button and D-pad badges spread outward. Whole-scheme comparisons include both devices and use saved or factory baselines; extra keycaps wrap within the canvas. Screenshots cover both perspectives in Simplified Chinese, minimum windows, and representative saved/pending/factory differences. Add `--all-languages` for the full language sweep.

Check the bottom comparison panel's four presentation modes:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeDiffViewTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeDiffViewTests.cs
.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-mode-check
```

This invokes each row's mouse-click handler at content/action coordinates and sends keyboard messages to the control. It checks combined/additions-first content, gray struck-through removals, changes-only/result-only/before modes, stable result ordering, empty bindings, omission of unchanged effective bindings, factory restore without pending edits, incomplete baselines, saved/pending colors and mode retention across schemes and comparisons. Display-only interactions must leave the copied save and draft unchanged. Screenshots include all four modes at minimum size in Simplified Chinese (`--all-languages` enables all 15 languages); the test does not measure physical mouse hit detection through overlapping desktop windows.

Compare both levels of factory data with the extracted resources and all 90 recorded controller defaults:

```powershell
& $compiler /nologo /out:dist\InputDefaultsTests.exe /r:System.Core.dll /r:System.Web.Extensions.dll src\Gvas.cs src\KeyboardDefaults.cs tests\InputDefaultsTests.cs
.\dist\InputDefaultsTests.exe assets\input-defaults.json samples\vanilla-base.sav
```

The dataset retains 60 resource registration inputs and 59 effective profile defaults. Extraction and native registration evidence are documented in [docs/input-defaults.md](../docs/input-defaults.md).

## Visible window rendering

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\WindowRenderingTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\WindowRenderingTests.cs
.\dist\WindowRenderingTests.exe samples\vanilla-base.sav dist\window-rendering-check
```

This check briefly shows a topmost window and captures the actual desktop pixels. It verifies stable device controls and keyboard focus across refreshes, key selection, perspective changes and modal return; reused relationship buttons must edit the currently selected action and key. It exercises nonclient activation/frame drawing, samples the border for native white chrome, and checks resize hit testing, maximize bounds and restore size. Screenshots cover the main window and a dialog. Bindings are edited only in memory against a copied fixture; neither input file is written. These checks complement the transparent layout captures and do not measure every transient frame during repainting.

## Binding dialog and explanations

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingDialogInteractionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingDialogInteractionTests.cs
.\dist\BindingDialogInteractionTests.exe samples\vanilla-base.sav dist\binding-dialog-check
```

Checks the visible dropdown arrow through mouse messages, text entry through the native edit control, capture activation without recording its first click, capture focus isolation, Tab navigation, and explicit unbinding in Simplified Chinese. Add `--all-languages` for targeted localization testing. State samples compare factory, saved custom, pending, and empty bindings. Details must summarize remaining effective keys when another entry is unbound and include inherited entries without duplicates. The question button opens a wrapping, scrollable example tutorial. Layout screenshots use `DrawToBitmap`; this test does not measure transient repaint frames. Input saves remain unchanged.
