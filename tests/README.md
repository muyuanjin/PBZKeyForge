# Local verification

Integration checks use Simplified Chinese by default. Full language sweeps are opt-in and reserved for targeted localization testing: append `--all-languages` to the relevant command, for example `.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-language-check --all-languages`. The dedicated `I18nTranslationTests` is also run separately only for localization work; it is not part of routine verification.

Run from the repository root after `powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1`. These checks use Windows' bundled .NET Framework compiler:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /out:dist\EditorSessionTests.exe /r:System.Core.dll src\Gvas.cs src\GameSchemeSettings.cs src\I18n.cs src\I18n.Editor.cs src\I18n.Extended.cs src\KeyboardDefaults.cs src\Presets.cs src\EditorSession.cs tests\EditorSessionTests.cs
.\dist\EditorSessionTests.exe samples\vanilla-base.sav
```

The session checks create fresh temporary copies. They cover disk refresh and conflicts, per-scheme keyboard and controller drafts, isolation between profiles with the same controller family, preview replacement/cancellation, global reset/undo, unknown data preservation, interrupted writes, resource-based raw slots for crouch/menu/items, all three Shadow Arm controller slots, shared Blob ownership, and rejection of unregistered controller slots. No game installation is required for this command.

Check the mouse attack/guard preset independently, including the retained F heavy attack binding:

```powershell
& $compiler /nologo /out:dist\MouseGuardPresetTests.exe /r:System.Core.dll src\Gvas.cs src\GameSchemeSettings.cs src\I18n.cs src\I18n.Editor.cs src\I18n.Extended.cs src\KeyboardDefaults.cs src\Presets.cs src\EditorSession.cs tests\MouseGuardPresetTests.cs
.\dist\MouseGuardPresetTests.exe samples\vanilla-base.sav
```

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

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingViewsTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingViewsTests.cs
.\dist\BindingViewsTests.exe samples\vanilla-base.sav dist\binding-view-check
```

Checks navigation without writes, shared relationship editing, duplicate removal, resource inheritance, controller family isolation, and raw record edits. Physical keyboard and all five mouse hotspots are clicked. Badge checks cover gray/red/amber transitions when applying, undoing, restoring factory mappings and removing the last action. Controller triggers aggregate axis and digital inputs; face-button and D-pad badges spread outward. Whole-scheme comparisons include both devices and use saved or factory baselines; extra keycaps wrap within the canvas. Screenshots cover both perspectives in Simplified Chinese, minimum windows, and representative saved/pending/factory differences. Add `--all-languages` for the full language sweep.

Check the bottom comparison panel's four presentation modes:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeDiffViewTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeDiffViewTests.cs
.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-mode-check
```

This invokes each row's mouse-click handler at content/action coordinates and sends keyboard messages to the control. It checks combined/additions-first content, gray struck-through removals, changes-only/result-only/before modes, stable result ordering, empty bindings, record-only changes, incomplete baselines, saved/pending colors and mode retention across schemes and comparisons. Display-only interactions must leave the copied save and draft unchanged. Screenshots include all four modes at minimum size in Simplified Chinese (`--all-languages` enables all 15 languages); the test does not measure physical mouse hit detection through overlapping desktop windows.

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
