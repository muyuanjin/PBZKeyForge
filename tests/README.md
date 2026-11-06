# Local verification

Build with Windows PowerShell 5.1 and use a new isolated directory for each integration run. Resource tests never launch the game or modify its installation.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
python tests\PerfectDeflectNoRapidClickPenaltyModBuildTests.py
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\PerfectDeflectNoRapidClickPenaltyModTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll tests\PerfectDeflectNoRapidClickPenaltyModTests.cs
.\dist\PerfectDeflectNoRapidClickPenaltyModTests.exe dist\perfect-deflect-no-rapid-click-penalty-check
& $compiler /nologo /out:dist\PerfectDeflectNoRapidClickPenaltyEmbeddedResourceTests.exe /r:System.Core.dll tests\PerfectDeflectNoRapidClickPenaltyEmbeddedResourceTests.cs
.\dist\PerfectDeflectNoRapidClickPenaltyEmbeddedResourceTests.exe dist\PBZKeyForge.exe assets\perfect-deflect-no-rapid-click-penalty
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\MainResourceModTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\MainResourceModTests.cs
.\dist\MainResourceModTests.exe 'dist\perfect-deflect-no-rapid-click-penalty-check\游戏 with spaces' dist\main-resource-mod-check
```

The generator suite checks the five difficulty data assets and their numeric-only cooldown change. The installation harness checks enable/disable, no-op, incomplete repair, modified/foreign-file conflicts, the running-game guard, failure/retry and preservation of unrelated files. Embedded-resource comparison ensures the application installs the checked containers.

The main-window harness checks the default-off footer checkbox, adjacent gameplay badge and click-to-open help, pending changes, Apply, Undo, warning, unavailable state and screenshots. It uses an isolated synthetic game fixture and does not launch the game.

These checks cover data, containers and editor installation behavior. The previous full cancellation effect has user gameplay confirmation; this five-package numeric container still needs its own game-loading and combat check.

Integration checks use Simplified Chinese by default. Full language sweeps are opt-in and reserved for targeted localization testing: append `--all-languages` to the relevant command, for example `.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-language-check --all-languages`. The dedicated `I18nTranslationTests` is also run separately only for localization work; it is not part of routine verification.

Check visual anchors across editing states at minimum, compact and large window sizes:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /out:dist\VisualPositionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\VisualPositionTests.cs
.\dist\VisualPositionTests.exe dist\visual-position-check
```

The checks cover diagram position and scale, fixed binding commands, extra-input wrapping, footer feedback, action filtering, status indicators, trigger selection, binding-detail scroll and focus, and the expanded differences review. Screenshots accompany geometry assertions; all editing uses offline fixtures.

To check the desktop's native scaling, compile this harness with `/win32manifest:build\app.manifest` and run it in a separate output directory. Add `--desktop-shots` to capture the trigger editor from the desktop as well as `DrawToBitmap`; use this flag only with the DPI-aware build.

For a targeted translation dictionary check across all 15 languages:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$i18nSources = @(Get-ChildItem src -Filter 'I18n*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /out:dist\I18nTranslationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll @i18nSources tests\I18nTranslationTests.cs
.\dist\I18nTranslationTests.exe
```

Run from the repository root after `powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1`. These checks use Windows' bundled .NET Framework compiler:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sessionSources = @(Get-ChildItem src -Filter *.cs | ForEach-Object { $_.FullName })
& $compiler /nologo /main:EditorSessionTests /out:dist\EditorSessionTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:third_party\protobuf-net\protobuf-net.dll /resource:third_party\protobuf-net\protobuf-net.dll,PBZKeyForge.protobuf-net /resource:samples\vanilla-base.sav,PBZKeyForge.vanilla-base.sav $sessionSources tests\EditorSessionTests.cs
.\dist\EditorSessionTests.exe samples\vanilla-base.sav
```

The session checks create fresh temporary copies. They cover disk refresh and conflicts, per-scheme keyboard and controller drafts, isolation between profiles with the same controller family, preview replacement/cancellation, global reset/undo, unknown data preservation, interrupted writes, resource-based raw slots for crouch/menu/items, all three Shadow Arm controller slots, shared Blob ownership, and rejection of unregistered controller slots. No game installation is required for this command.

Check remembered paths and Steam discovery with temporary libraries and save copies:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SaveDiscoveryTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SaveDiscoveryTests.cs
.\dist\SaveDiscoveryTests.exe samples\vanilla-base.sav dist\save-discovery-check
```

These checks cover custom install names, Unicode and spaces, manifest precedence over old folders, unreadable or malformed Steam metadata, absent registry roots, disk fallback, missing or corrupt remembered files, early return before disk enumeration, manual selection, fresh rediscovery and unwritable preferences. The UI startup opens a remembered fixture before its first frame; screenshot helpers preserve the player's preference. Source saves remain unchanged.

Check offline editing and controller families without device records:

```powershell
& $compiler /nologo /out:dist\OfflineControllerTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\OfflineControllerTests.cs
.\dist\OfflineControllerTests.exe samples\vanilla-base.sav dist\offline-controller-check
```

The checks cover offline keyboard/mouse, Xbox and PlayStation editing; missing controller arrays and empty key lists; per-family and per-scheme isolation; built-in presets and scoped share codes; exports and reopening; factory reset; source preservation; and refusal to overwrite game settings through export. Navigation and keyboard-only edits do not add controller records. Controller records use the game's canonical `Xbox` and `PS` family names, with new entries constructed from the embedded factory template.

Check responsive application and recovery using fixture copies:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\ApplyResponsivenessTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\ApplyResponsivenessTests.cs
.\dist\ApplyResponsivenessTests.exe samples\vanilla-base.sav dist\apply-responsiveness-check
```

The check pauses a background write at the replacement boundary while pumping UI messages. It verifies the spinner advances, editing and closing remain disabled, a second Apply cannot start, and the file stays untouched until the writer continues. It forces button paints during enabled-state transitions to check that entry and completion retain the busy appearance. An interrupted write retains the draft and restores controls; retry saves it. A screenshot captures the busy state.

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

Run the same built application against both `CL-301524_JKS-17219` and `CL-302256_JKS-17280` (1.0302256) when checking version compatibility. The authenticated session checks also edit and restore interaction on keyboard and PlayStation, check raw slots and inherited defaults, and preserve the other schemes. Close the game before write checks, including checks against copies.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe dist\PBZKeyForge.exe $gameExe $sharedSave $inputSave $activationArtifacts
```

The first authentication must read less than 1 MB through process I/O, including initial runtime work, so a complete EXE scan fails the harness. Both modes reject a full-sized executable without a supported key-derivation function as well as truncated executables and tampered containers. The reported reader identity must not claim an exact executable build.

When the game is running, use the harness's read-only PBZS checks instead. They validate the registered fast reader, reject unsupported executables and tampered containers, and verify the copied save remains unchanged; they do not activate a scheme or require closing the game:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe --read-only dist\PBZKeyForge.exe $gameExe $sharedSave $newReadOnlyArtifacts
```

For future-build discovery and GUI responsiveness, run the synthetic x64 PE checks:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeDiscoveryTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeDiscoveryTests.cs
.\dist\SchemeDiscoveryTests.exe --read-only samples\vanilla-base.sav dist\scheme-discovery-check
```

These checks build executable/data sections with unrelated raw offsets and RVAs, signed RIP references, a function crossing a scan block, an earlier incorrect candidate, nonexecutable decoys and new keys. They exercise cache invalidation, fresh authentication, damaged-save recovery and same-path EXE updates. GUI checks run in the WinForms message loop and cover a timer heartbeat, profile/draft preservation, stale-session results, automatic discovery after an EXE version change, suppression of repeated failures for the same EXE, explicit retry and completion deferred during Apply. Real binding dialogs remain open until discovery completes; confirming must edit the original scheme, while cancellation leaves no draft. Closing either dialog consumes the deferred result. The synthetic EXEs are fixtures, not launched games. Remove `--read-only` after closing the game to also test activation with a replaced key and with a relocated function using the same key; all writes remain inside the new artifact directory, with ordinary backups outside it.

Use `$activationArtifacts`, the copied installation created by `SchemeActivationTests`, as the source for session tests that inject an activation failure after bindings have been saved, then verify that retry writes only the remaining activation:

```powershell
.\dist\EditorSessionTests.exe samples\vanilla-base.sav $activationArtifacts $newSessionArtifacts
```

`$newSessionArtifacts` must also be a new directory on the same volume. To test actual GUI controls and capture regular/minimum sizes in Simplified Chinese, run:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\EditorUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\EditorUiTests.cs
.\dist\EditorUiTests.exe "$activationArtifacts\ProjectPBZ\Saved\EnhancedInputUserSettings.sav" $screenshots
```

The GUI check intentionally edits the supplied **copy** through the real Apply button. It runs inside the WinForms main message loop so modal dialogs and asynchronous Apply use the same UI synchronization context as the application. It opens and cancels every editable keyboard/controller binding cell after scrolling it into view, verifies unavailable slots are disabled, checks clear-override actions, distinguishes left/right modifiers, switches quick configurations, and verifies navigation reads external changes while retaining drafts. Scheme/device tabs, Refresh, Undo and Apply use their control events. Conflict resolution exercises Cancel, Use game and Keep draft through the link's click event and real dialog buttons; this does not test physical mouse hit detection on that link. After applying Custom 1, it compares the other profiles and unrelated data byte-for-byte and verifies controller-family isolation. It uses the application's DPI manifest and checks custom window controls, centered binding headings, toolbar alignment and the footer. Windows stay transparent during automated screenshots to avoid flashing every test state on the desktop. Inspect the images for clipping and consistency; these checks do not establish actual game startup behavior or every Windows DPI setting. Add `--controls-only` to skip screenshot capture while retaining control assertions. Language selection is independent: `--all-languages` explicitly enables all 15 languages, including with `--controls-only`.

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

Check the editor without a game, save, or connected controller:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\OfflineEditorUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\OfflineEditorUiTests.cs
.\dist\OfflineEditorUiTests.exe dist\offline-ui-check
```

This enters the normal offline workflow with no save path, checks preset access, export availability and the absence of game activation, then edits Xbox and PlayStation independently. It keeps PlayStation selected across Default, Custom 1 and Custom 2, edits through the UI event, and checks family/scheme isolation after export and reopening. Screenshots capture keyboard/mouse and both controller families. It does not automate the native export file picker or write game files.

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

Checks Add keys, Replace keys and Preset only against existing custom keys; mode switching and cancellation use the same checkpoint. It covers duplicate no-ops, save/reopen, other-scheme isolation, controller families, atomic capacity rejection and retained mode selection after a failed click. Screenshots show all three directly selectable modes beside the preset selector at regular and minimum window sizes. Restore factory defaults disables the mode switch. All writes use fixture copies.

Check saved user presets, share codes and game-file export:

```powershell
& $compiler /nologo /out:dist\PresetShareTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\PresetShareTests.cs
.\dist\PresetShareTests.exe samples\vanilla-base.sav dist\preset-share-check
& $compiler /nologo /out:dist\UserPresetTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\UserPresetTests.cs
.\dist\UserPresetTests.exe samples\vanilla-base.sav dist\user-preset-check
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\UserPresetUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\UserPresetUiTests.cs
.\dist\UserPresetUiTests.exe samples\vanilla-base.sav dist\user-preset-ui-check
```

The share checks use an independent Protobuf byte vector to verify stable IDs and packed encoding, raw/DEFLATE selection, skipped metadata, unknown semantic rejection, literal engine keys/family names, frozen resource defaults, explicit empty scopes, and keyboard/controller-family isolation in all three application modes. The model checks save/reload, Unicode names containing colons, pure unbinding, fixed Ctrl plus three editable keys, duplicate names, builtin deletion rejection and source-file protection. Export includes only the selected scheme's draft. The GUI harness uses an isolated preset library and real control events for save, preview, scoped copying with live lengths, import, confirmed/cancelled batch deletion, direct mode selection and staging. Selected rows drive previews; deletion checks do not interrupt browsing. Screenshots cover regular/minimum windows, name edits across resizing, horizontal fit, and a preset affecting the second controller family. Name guidance checks cover initial focus, neutral interactive saving, empty-name clicks, whitespace and ready-state styling. Add `--native-guidance` to move the real pointer over the gray save action and capture its native tooltip. Export serialization is checked through the session API; the native export file picker is not automated.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingViewsTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingViewsTests.cs
.\dist\BindingViewsTests.exe samples\vanilla-base.sav dist\binding-view-check
```

Append `--startup-only` to check the initial binding cards before any refresh and capture the first displayed frame for both offline and saved schemes. The check rejects collapsed cards and verifies that startup leaves the save unchanged.

Checks navigation without writes, shared relationship editing, duplicate removal, resource inheritance, controller family isolation, and raw record edits. Physical keyboard and all five mouse hotspots are clicked. Badge checks cover gray/yellow/blue transitions when applying, undoing, restoring factory mappings and removing the last action. Controller triggers aggregate axis and digital inputs; face-button and D-pad badges spread outward. Whole-scheme comparisons include both devices and use saved or factory baselines; extra keycaps wrap within the canvas. Screenshots cover both perspectives in Simplified Chinese, minimum windows, and representative saved/pending/factory differences. Add `--all-languages` for the full language sweep.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\DeviceAlignmentTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\DeviceAlignmentTests.cs
.\dist\DeviceAlignmentTests.exe dist\device-alignment-check
```

Checks all 124 physical targets across keyboard, mouse, Xbox and PlayStation diagrams at three canvas sizes. Independent artwork measurements cover staggered rows, wide keys, navigation keys, mouse wheel, side buttons, sticks and shoulder bands. Clicks each target's center and edges with all badges visible, checks shoulder/trigger separation and scrolling, and captures every selected key in contact sheets. Preview captures include complete artwork and wrapped extra keys without scrollbars. Multi-digit counts resize their badges on refresh. Enhanced previews combine trigger axis and digital inputs on one physical button and include sequence and watched keys with the same function counts as the main view.

Check the bottom comparison panel's four presentation modes:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeDiffViewTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeDiffViewTests.cs
.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-mode-check
```

This invokes each row's mouse-click handler at content/action coordinates and sends keyboard messages to the control. It checks combined/additions-first content, gray struck-through removals, changes-only/result-only/before modes, stable result ordering, empty bindings, omission of unchanged effective bindings, factory restore without pending edits, incomplete baselines, saved/pending colors and mode retention across schemes and comparisons. All four modes must preserve partial-paint clipping. Visible topmost windows exercise both the isolated list and the actual composited MainForm. Native line/page/top/bottom messages, bidirectional wheel messages and rapid wheel bursts must move the list without overlapping row bounds; desktop captures after scrolling must match a full repaint, allowing one channel level of rounding at scaled icon edges. The native Form host comparison excludes narrow side margins outside row text to omit system corner composition. MainForm also refreshes the session while scrolled. Display-only interactions must leave the copied save and draft unchanged. Screenshots include all four modes at minimum size in Simplified Chinese (`--all-languages` enables all 15 languages); the test does not measure physical mouse hit detection through overlapping desktop windows.

Compare both levels of factory data with the extracted resources and all 90 recorded controller defaults:

```powershell
& $compiler /nologo /out:dist\InputDefaultsTests.exe /r:System.Core.dll /r:System.Web.Extensions.dll /r:dist\PBZKeyForge.exe tests\InputDefaultsTests.cs
.\dist\InputDefaultsTests.exe assets\input-defaults.json samples\vanilla-base.sav
.\dist\InputDefaultsTests.exe assets\input-defaults-302256.json samples\vanilla-base.sav
```

The launch dataset preserves its recorded 19 contexts. The patch dataset includes all 20 contexts, InputAction-inherited mapping names and the native Shadow Arm mirror rule. Both contain 60 unique registration rows and 59 effective defaults; provenance retains repeated registrations across contexts. The captured launch fixture remains a raw 90-record comparison, not evidence for the patch's runtime projection.

Check native layout identification, independent session defaults, mirrored-slot editing and installation updates:

```powershell
& $compiler /nologo /out:dist\NativeInputDefaultsTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:dist\PBZKeyForge.exe tests\NativeInputDefaultsTests.cs
.\dist\NativeInputDefaultsTests.exe samples\vanilla-base.sav dist\native-input-check $oldInputSave $newInputSave
python tests\InputDefaultsExtractionTests.py
```

Use the input saves adjacent to each verified native installation for the last two arguments; omit them for synthetic-only checks. The native harness maps constructor, vtable and RegisterIMC fixtures to unrelated PE raw offsets, rejects incomplete evidence, checks both real installations when supplied, keeps simultaneous sessions isolated, preserves inactive raw records, and verifies keyboard additions/restoration, preset previews and new controller families. Same-path native updates retain compatible drafts and reject an obsolete independent-slot draft. All changes use generated files in the new artifact directory; source saves stay unchanged. The Python checks cover inherited, overridden and disabled mappings, zero masks, virtual mounts, slot counting, all-context discovery and build-specific effective selection.

## Visible window rendering

Check refresh scope and action-table creation using fixture copies:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\RefreshScopeTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\RefreshScopeTests.cs
.\dist\RefreshScopeTests.exe samples\vanilla-base.sav dist\refresh-scope-check
```

This check starts a temporary process named `PBZ` to exercise game-state detection without running the game. It verifies that rendering uses the last detected state, timer updates leave binding data intact, selecting keys preserves relationship data and hit targets, action tables are created on first use, and returning to a hidden table refreshes its contents. Tables first opened before or after showing the window must retain correct DPI sizing and aligned columns after resizing. It captures the game-running Apply button, checks exit guidance despite unavailable scheme authentication, and verifies that game exit restores the ordinary button and draft application. It also checks offline export status and source-save preservation.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\WindowRenderingTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\WindowRenderingTests.cs
.\dist\WindowRenderingTests.exe samples\vanilla-base.sav dist\window-rendering-check
```

This check briefly shows a topmost window and captures the actual desktop pixels. It checks that input-save discovery and known PBZS fast paths complete before the initial window is shown, reading installed saves when available and using offline mode otherwise. Unknown reader locations are discovered in the background by `SchemeDiscoveryTests`. It verifies stable device controls and keyboard focus across refreshes, key selection, perspective changes and modal return; reused relationship buttons must edit the currently selected action and key. It exercises nonclient activation/frame drawing, samples the border for native white chrome, and checks resize hit testing, maximize bounds and restore size. Screenshots cover startup, the main window and a dialog. Bindings are edited only in memory against a copied fixture; neither input file is written. These checks complement the transparent layout captures and do not measure every transient frame during repainting.

## Binding dialog and explanations

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingDialogInteractionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingDialogInteractionTests.cs
.\dist\BindingDialogInteractionTests.exe samples\vanilla-base.sav dist\binding-dialog-check
```

Checks the visible dropdown arrow through mouse messages, text entry through the native edit control, capture activation without recording its first click, capture focus isolation, Tab navigation, and explicit unbinding in Simplified Chinese. Add `--all-languages` for targeted localization testing. State samples compare factory, saved custom, pending, and empty bindings. Details must summarize remaining effective keys when another entry is unbound and include inherited entries without duplicates. The question button opens a wrapping, scrollable example tutorial. Layout screenshots use `DrawToBitmap`; this test does not measure transient repaint frames. Input saves remain unchanged.

## Binding capacity and removed actions

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingFeedbackTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingFeedbackTests.cs
.\dist\BindingFeedbackTests.exe samples\vanilla-base.sav dist\binding-feedback-check
```

Checks a fourth-key rejection without losing existing bindings, visible current keys and capacity, action-specific controller limits, disabled full-capacity confirmation, and gray removed action names/icons/status. Screenshots capture the full-capacity picker, action details and removed relationship. Saves are not written.

An optional independent IOStore extraction uses the development-only CUE4Parse harness:

```powershell
dotnet build tests\DeflectSchemaAudit\DeflectSchemaAudit.csproj
dotnet tests\DeflectSchemaAudit\bin\Debug\net10.0\DeflectSchemaAudit.dll --container assets\perfect-deflect-no-rapid-click-penalty\PBZKeyForge_PerfectDeflectNoRapidClickPenalty_P.utoc dist\deflect-container-read
```

The harness uses `GAME_UE5_7` and requires an empty companion PAK. Container mode extracts raw chunks without deserializing their Zen headers. It is separate from the dependency-free application runtime and does not establish game loading or combat behavior.

## CLI and agent verification

```powershell
python tests\CliAgentTests.py dist\PBZKeyForge-cli.exe dist\cli-agent-check
python tests\CliLifecycleTests.py dist\PBZKeyForge-cli.exe dist\cli-lifecycle-check
```

`CliAgentTests` invokes the public executable and parses JSON. It covers the full catalog/schema, key identities, ordinary plus enhanced batch editing, dry runs, ZIP roundtrip, share import/export, device scope filtering, copy, key movement, full reset, explicit scheme selection and invalid batches producing no output. It uses copies and never applies to a game installation.

CLI export checks reject runtime configuration targets for new, ZIP and JSON sources, including custom Mod directories, normalized paths and missing configuration files. Rejection preserves the JSON/Lua pair; ordinary JSON export remains available. Global reset exports are reopened as SAV and ZIP to verify all three schemes' keyboard and controller defaults without changing the source save.

`CliLifecycleTests` covers unattended startup, version/source discovery, UTF-8 stdin and BOM files, invalid-field diagnostics, gameplay Mod enable/disable/conflicts, enhancement lifecycle, local runtime update, ordinary Apply with backups, running-game refusal and partial uninstall reporting. It requires the real game to be closed. For authenticated scheme activation and live acknowledgement/rejection/timeout checks, pass the real game root as a third argument and use a new test directory on that game's volume, for example `python tests\CliLifecycleTests.py dist\PBZKeyForge-cli.exe G:\PBZKeyForge-cli-check G:\SteamLibrary\steamapps\common\PBZ`. It hard-links the native executable and copies both saves; the originals are checked unchanged. A temporary process named PBZ and simulated heartbeat/acknowledgements exercise the CLI protocol. These checks do not establish actual game input behavior, launch a game or stop an existing game process.

CLI preset checks also cover keyboard-only and controller-only Replace/OnlyPreset operations. The other device's rules, native overrides and keyed suppressions must survive export and reimport.

Cross-device prerequisite and build checks:


```powershell
& $compiler /nologo /out:dist\KeyEnhancementPrerequisitesTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.IO.Compression.dll tests\KeyEnhancementPrerequisitesTests.cs
.\dist\KeyEnhancementPrerequisitesTests.exe dist\prerequisites-check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Ue4ssBuildTests.ps1 -OutputDirectory dist\ue4ss-build-check
```

The prerequisite harness checks missing CRT imports, missing exports, damaged app-local runtimes, rejection before installation writes, repair/retry, and disable/uninstall with unavailable prerequisites. It uses copied runtime files and never launches UE4SS or the game. Compile it with `/platform:x86` to verify the architecture diagnostic and that disabling/uninstalling remain available. Append `--all-languages` for a targeted check of the two prerequisite messages. The PowerShell harness requires the pinned ZIP in the build cache; all network calls are intercepted. It verifies offline cache reuse, refusal to substitute another cached release, and explicit pin refresh with success/failure persistence.

```powershell
& $compiler /nologo /out:dist\KeyEnhancementPrerequisitesPerformanceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Web.Extensions.dll tests\KeyEnhancementPrerequisitesPerformanceTests.cs
.\dist\KeyEnhancementPrerequisitesPerformanceTests.exe dist\prerequisite-performance
```

The performance harness records three fresh-process checks, 30 checks of in-memory update candidates, 30 checks of mapped installed DLLs and 20 unchanged Apply operations. A full-file-read control provides an allocation and I/O comparison. It rejects checks allocating more than 1 MiB per call and unchanged Apply operations explicitly reading at least half the native DLL size; timings describe the local machine and have no fixed pass threshold. The JSON report includes median, p95, CPU time, managed allocations, explicit read bytes and GC counts. File mappings page in only accessed data; mapped page faults are not included in the explicit read counter. These runs use isolated fixtures with the game's global closed-process guard.
