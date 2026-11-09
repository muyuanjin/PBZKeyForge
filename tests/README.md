# Local verification

Build with Windows PowerShell 5.1 and use a new isolated directory for each integration run. Resource tests never launch the game or modify its installation.

Run GUI harnesses on a separate Windows desktop to keep their windows and activation away from the working desktop:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:dist\RunGuiTest.exe /r:System.Core.dll build\RunGuiTest.cs
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /win32manifest:build\app.manifest /out:dist\WindowRestoreTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\WindowRestoreTests.cs
.\dist\RunGuiTest.exe dist\WindowRestoreTests.exe dist\window-restore.log dist\window-restore-check
Get-Content dist\window-restore.log
```

`RunGuiTest` never switches the visible desktop, redirects test output to the specified log and returns the test's exit code. It terminates tests that exceed three minutes. `WindowRestoreTests` checks repeated minimize/restore cycles in key, action and controller views, normal and maximized windows, stable child bounds while minimized, and resizing after restoration. It records restore timings with animation enabled and captures the restored application with `DrawToBitmap`. Tests that sample physical desktop pixels require a separately scheduled interactive run.

GUI harnesses that retain unapplied drafts must call `Dispose()` during cleanup. Use `Close()` only when deliberately testing the player's close-confirmation flow; otherwise the confirmation dialog blocks unattended runs. Async Apply harnesses must start a real `Application.Run()` message loop and schedule their runner through `Application.Idle`, with `Control.CheckForIllegalCrossThreadCalls` enabled. `Application.DoEvents()` alone does not establish the WinForms synchronization context needed for representative continuation and timing behavior.

Check scheme-menu dismissal, selection and dirty indicators, and deployment comparisons:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeMenuTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeMenuTests.cs
.\dist\SchemeMenuTests.exe dist\scheme-menu-check
& $compiler /nologo /out:dist\DeploymentComparisonTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\DeploymentComparisonTests.cs
.\dist\DeploymentComparisonTests.exe
```

`SchemeMenuTests` checks background and keyboard dismissal, native mouse selection, cancelled rename, menu disposal with the editor and per-scheme versus shared dirty indicators. It captures the editor and records twelve-scheme refresh timing. `DeploymentComparisonTests` compares deployment output with the full-copy reference, including inactive drafts, shared bindings and enabled-state overrides, while preserving the edited configuration.

`DialogPositionTests` opens the binding editor through the modeless presentation path on every connected monitor. It checks centering, working-area containment near screen edges, maximized owners, nested windows, modal dialogs and manual positioning, and captures the displayed editors. It uses offline fixtures.

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /win32manifest:build\app.manifest /out:dist\DialogPositionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\DialogPositionTests.cs
.\dist\DialogPositionTests.exe dist\dialog-position-check
```

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

The main-window harness checks the default-off footer checkbox, adjacent gameplay badge and component-menu help, pending changes, Apply, Undo, warning, unavailable state and screenshots. It uses an isolated synthetic game fixture and does not launch the game.

These checks cover data, containers and editor installation behavior. The previous full cancellation effect has user gameplay confirmation; this five-package numeric container still needs its own game-loading and combat check.

Integration checks use Simplified Chinese by default. Full language sweeps are opt-in and reserved for targeted localization testing: append `--all-languages` to the relevant command, for example `.\dist\SchemeDiffViewTests.exe samples\vanilla-base.sav dist\diff-language-check --all-languages`. The dedicated `I18nTranslationTests` is also run separately only for localization work; it is not part of routine verification.

Check visual anchors across editing states at minimum, compact and large window sizes:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /out:dist\VisualPositionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\VisualPositionTests.cs
.\dist\VisualPositionTests.exe dist\visual-position-check
```

The checks cover diagram position and scale, fixed binding commands, extra-input wrapping, footer feedback, action filtering, status indicators, trigger selection, binding-detail scroll and focus, and the expanded differences review. Screenshots accompany geometry assertions; all editing uses offline fixtures.

Check intermediate geometry during repeated refreshes and popup round trips:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\GuiStabilityTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\GuiStabilityTests.cs
.\dist\GuiStabilityTests.exe dist\gui-stability-check
```

`GuiStabilityTests` runs a WinForms message loop at three window sizes. It rejects transient size/location changes during refresh, preserves the selected action and viewport when the catalog changes, cycles all trigger modes, edits tuning values and repeatedly opens/closes the live binding window. The action tree must preserve its completed frame during native background erasure, including after handle recreation. Native mouse messages verify that animation continues during mouse movement. It records editing times and screenshots; timing output describes the current machine. The fixture never writes game settings.

Check flame cadence with the regular status timer running, mouse movement every two seconds and continuous mouse movement:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\FlameCadenceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\FlameCadenceTests.cs
.\dist\FlameCadenceTests.exe dist\flame-cadence-check
```

`FlameCadenceTests` records presented frames over four ten-second intervals and rejects animation gaps of 180 ms or longer, including a stalled tail after the last frame. It covers offline editing and a closed-game session using an isolated save fixture, and captures the final window. Append `--measure` to collect timings without cadence assertions.

For the flame sampling, transparency, motion and height checks independently of the main-window footer assertions, run `KeyEnhancementFlameOverlayTests` with `--field-only` after the output directory.

The full flame harness assumes a footer no taller than 88 logical pixels; the main window uses 96 in its regular layout and 84 in its compact layout. Its footer assertions need to distinguish these layouts. `VisualPositionTests` covers both window layouts.

For complete-move bindings, verify catalog labels, keyboard/controller binding, configuration persistence, native qualification, preparation and release dispatch:

`AllMoveBindingContractTests.cs` binds every move and generic ability on both devices with four independent combinations of resource/restriction bypass and damage/startup/recovery values. Share and JSON roundtrips must retain every identity and setting. Compile it against the built application with the standard WinForms references, then pass an output directory. `AllMoveBindingContractTests.lua` reads that directory through `PBZ_ALL_MOVE_CONFIG` (default `dist/all-move-audit/contracts`) and exercises each compiled output through the real scheduler, including held repetition and release. `AllMoveRestrictionTests.lua` consumes its separate full-restriction fixtures.

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /out:dist\CompleteMoveCatalogTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll tests\CompleteMoveCatalogTests.cs
.\dist\CompleteMoveCatalogTests.exe dist\complete-move-catalog-check
& $compiler /nologo /out:dist\BindingTriggerContractTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\BindingTriggerContractTests.cs
.\dist\BindingTriggerContractTests.exe dist\trigger-contract-check
python tests\input-router\run_lua.py tests\input-router\CompleteMoveTests.lua
python tests\input-router\run_lua.py tests\input-router\OnPressMoveTests.lua
python tests\input-router\run_lua.py tests\input-router\ExactClassMoveTests.lua
python tests\input-router\run_lua.py tests\input-router\ActiveShieldMoveTests.lua
$env:PBZ_COMPLETE_MOVE_CONFIG = 'dist/complete-move-catalog-check/complete-move.lua'
python tests\input-router\run_lua.py tests\input-router\CompiledCompleteMoveTests.lua
python tests\input-router\run_lua.py tests\input-router\CompiledWeaponSpecialMoveTests.lua
Remove-Item Env:\PBZ_COMPLETE_MOVE_CONFIG
```

The checks reject airborne use of ground moves, missing live instances, active phase-entry abilities and resource-failure branches, and verify cancellation after release failure. Default and on-press move bindings keep requesting normal activation while held, repeat after the current move ends and native qualification allows it, and stop on release without canceling an attack in progress. Multiple sources for one move merge per frame only when their resource and multiplier settings agree. Cloning and preset sharing preserve the selected trigger. Tap and release remain single requests. Mixed on-press rules keep ordinary abilities, actions and scripts single-shot while complete moves continue requesting.

`CompleteMoveCatalogTests` also writes `phase-moves.lua` and captures the weapon/accessory catalog labels. `BindingTriggerContractTests` writes mixed ability/move fixtures, checks every catalog default, named custom output types, selection order, sharing and copying. `CompiledCompleteMoveTests` loads those fixtures through `CompiledPhaseMoveTests` and exercises the actual backend and move executor for native FName/time entry, prepared projectiles, missed charge windows, input release and mixed continuous/one-shot bindings on both devices. Pending tests cover shared virtual Hold ownership, qualification loss, context reset and natural attack progress without replay. Accessory callbacks reuse active granted instances and never activate or cancel their persistent effects. These fixtures cover editor/runtime dispatch, not in-game animation or damage. Equipment and phase evidence is recorded in [complete moves](../docs/complete-moves.md).

Check the equipment-specific catalog, keyboard/controller sharing, binding effects and picker screenshots:

`GameMoveNameTests` checks official names, related upgrade searches and every shared action ID in all 15 languages. Each official name must be a separate searchable picker row, including generated weapon prefixes and expanded placeholder variants; selecting every name for one ID must still create one binding rule and output. Distinct actions with the same label must remain separate. The harness also captures the keyboard/controller picker for the One-Eyed Bow's Thousand-Eyed Gaze, with the upgrade attached to its real laser entrance. Run this targeted localization check after generating `assets/key-enhancement/game-move-names.json`:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /win32manifest:build\app.manifest /out:dist\GameMoveNameTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll tests\GameMoveNameTests.cs
.\dist\GameMoveNameTests.exe dist\game-move-name-check
```

`SearchOptimizationTests` checks multi-term ranking across labels and technical fields, order-independent queries, cache invalidation and the search path over the full catalog. Compile it against the built application with `System.Core.dll` and run without arguments:

```powershell
& $compiler /nologo /out:dist\SearchOptimizationTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\SearchOptimizationTests.cs
.\dist\SearchOptimizationTests.exe
```

`SearchDialogPerformanceTests` measures the debounced picker under a typing burst and verifies the final list still selects the intended move. Compile with the same WinForms references and DPI manifest as `UnifiedBindingDialogTests`, then run without arguments:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SearchDialogPerformanceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\SearchDialogPerformanceTests.cs
.\dist\SearchDialogPerformanceTests.exe
```

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\WeaponMoveCatalogTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll tests\WeaponMoveCatalogTests.cs
.\dist\WeaponMoveCatalogTests.exe dist\weapon-move-check
python tests\input-router\run_lua.py tests\input-router\CompiledWeaponCatalogTests.lua
python tests\input-router\run_lua.py tests\input-router\WeaponCatalogTuningTests.lua
python tests\input-router\run_lua.py tests\input-router\CompiledWeaponChargeMoveTests.lua
python tests\input-router\run_lua.py tests\input-router\CompiledWeaponPhaseTests.lua
python tests\input-router\run_lua.py tests\input-router\FinisherSpeedTests.lua
```

`WeaponMoveCatalogTests` checks ordinary attacks in `weapon-moves.json` and ChiRen's protected charge releases in `complete-moves.json`. It writes untuned and tuned configurations for every new precise entry on both devices. Runtime checks exercise the scheduler, exact granted-class activation, native refusal and equipment qualification. Tuning checks cover damage, separate startup/recovery speeds, actual resource maxima, native continuations, projectile ownership and cleanup.

`NativeMoveInputTests` also runs each compiled Hold entrance with 130 granted abilities for 120 held frames. Unrelated grants must remain inside native selection. `MoveRestrictionRuntimeTests` counts restriction-template reads across explicit/native hook nesting and bounds extra tag queries for inactive policies, unrelated containers and removed blockers. `ScriptContextTests` and `ScriptContextNativeTests` verify interface-address filtering, nested skipped callbacks and idle calls without name resolution.

`MoveRestrictionCatalogTests.py` verifies every catalog identity against the input and move sources, both qualification modes for direct abilities, conflicting native route alternatives, and the reviewed equipment and preparation boundaries. Coverage includes native Hold entrances, weapon-specific shared inputs, real projectile/strength readiness tags and branches that support independent initialization. After `BindingRestrictionTests` writes its compiled fixtures, run:

```powershell
python tests\MoveRestrictionCatalogTests.py
python tests\input-router\run_lua.py tests\input-router\MoveRestrictionRuntimeTests.lua
python tests\input-router\run_lua.py tests\input-router\AllMoveRestrictionTests.lua
python tests\input-router\run_lua.py tests\input-router\MoveEquipmentTests.lua
python tests\input-router\run_lua.py tests\input-router\NativeMoveInputTests.lua
python tests\input-router\run_lua.py tests\input-router\IndependentPhasePreparationTests.lua
cl.exe /nologo /O2 /MD /EHsc /std:c++17 /Fo:dist\WeaponEquipmentNativeTests.obj /Fe:dist\WeaponEquipmentNativeTests.exe tests\input-router\WeaponEquipmentNativeTests.cpp
.\dist\WeaponEquipmentNativeTests.exe
```

Set `PBZ_NATIVE_HOLD_CONFIG`, `PBZ_NATIVE_HOLD_VARIANT_CONFIG` and `PBZ_INDEPENDENT_PHASE_CONFIG` when using another fixture directory; defaults are under `dist/restrictions-final-check`. The native-input harness exercises every compiled Hold entrance on keyboard and controller: native readiness, activation refusal, reset before and after activation, natural full-charge completion, early physical release, missed release windows and independent bindings sharing one input. Shared entrances select the current weapon's original input; other weapons retain ordinary activation. It also checks each binding's resource, restriction and speed/damage settings through the preparation handoff. `CompiledPhaseMoveTests` covers automatic phase preparation on both devices, including projectile readiness, input-release acknowledgement, refusal, reset and expiry. Set `PBZ_PHASE_MOVE_CONFIG` to select its `phase-moves.lua` fixture. `WeaponCatalogTuningTests` runs prepared native inputs through the real tuning executor and checks resource maxima, native continuations and retained projectile damage. The equipment bridge uses real Lua C calls with reflected engine fixtures; these checks do not launch the game.

`MoveRestrictionRuntimeTests` gives each primary ability instance independent required/blocked tag containers and checks activation against that instance, as GAS does. It verifies synchronous restoration for both instance arrays, duplicate instance references, exceptions and native InputAction activation, plus the long-blade uppercut's three-stack entrance and unrelated abilities retaining their restrictions. Avatar and ability-component single/any/all tag queries retain the binding's selected categories while requiring real target and projectile state.

`AllMoveRestrictionTests.lua` reads the `all-restrictions-keyboard.lua` and `all-restrictions-pad.lua` fixtures written by `AllMoveBindingContractTests`. Set `PBZ_ALL_MOVE_CONTRACTS` to another fixture directory; its default is `dist/all-move-audit/contracts`. Every compiled ability/move entrance runs all six categories separately against its native template and primary instance, including synchronous restoration, native readiness and opposite route alternatives. Coverage and static resource evidence are recorded in `docs/all-move-restriction-validation.md`.

Run `python tests\CompleteMoveSignatureResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle` to check 44 native UHT signatures, 23 post-hook return contracts and all 88 complete-move entry signatures against the installed game's metadata. It verifies resource getter and ability handoff parameters/returns, both timeline playback signatures, projectile initialization, damage-source interface versus tag parameters, native input observation, and the actual declaring class of every Blueprint callback, including inherited functions. Continuous Hold preparation checks the exact native struct identity shared by `MakeInputActionValueOfType` and `StartContinuousInputInjectionForAction`; this opaque `FInputActionValue` differs from the vector accepted by ordinary injection. `ShadowMovePhaseResourceTests.py` verifies their grants, costs and preparation separately.

`OpaqueValueNativeTests.cpp` verifies actual hidden input bytes and gameplay-effect context ownership across native `ProcessEvent`, including failure cleanup and consumed effect handles. `ScriptContextTests.lua` checks the Lua bridge contract. `ColdNativePreparationTests.lua` checks that weapon registration precedes Hold readiness and that a refused request is attempted at most once per binding per frame. `SnakePreparationTests.lua` checks real projectile preparation, native phase waits, independent binding settings and cancellation.

`NativeStatePreparationTests.cs` writes `native-state-effects.lua` after checking effect metadata, keyboard/controller persistence and sharing. Set `PBZ_NATIVE_STATE_CONFIG` to that file and run `CompiledNativeStatePreparationTests.lua` to exercise its bindings through the real backend and phase executor. `NativeStatePreparationTests.lua` separately checks original effects, shared handle ownership, native continuations, qualification failure and cleanup retries. Long-blade preparation replaces the player's lower tier during the attack and restores its native spec and remaining duration afterward, including partial failure and pawn destruction. These checks verify configuration and execution contracts without starting the game.

For static checks against the installed game, set `$gameExe`, `$paks` and `$oodle` to the native executable, Content/Paks directory and an available Oodle library. Set `$registry` to a full AssetRegistry export for the same build when running the source test, then run:

```powershell
python tests\CombatArtPhaseResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\WeaponMoveEntryResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\ShadowMoveEntryResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\WeaponChargeMoveResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\AdditionalWeaponPhaseResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\NativeMoveInputResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\MoveRestrictionResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\WeaponReleaseResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\WeaponSpecialEntryResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\ShadowMovePhaseResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\AccessoryMoveResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\CounterContextResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\WeaponExecutionResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle --registry $registry
python tests\ShadowTrainingMoveResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
python tests\AbilitySourceResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle --registry $registry
```

The combat-art test reads all 40 actual equipment grant arrays and all inherited timelines of their 35 combat-art classes, and compares each registered combat-art recipe with setup, damage boundaries and native reflection. The main-weapon release test checks early release, original continuous storm charge costs and sword-blossom preparation/cleanup. The shadow test independently reads equipment grants, callbacks, state/fee events, release windows and projectile identities. The training test checks 8 official follow-ups and 10 original conditional routes, including predecessor/upgrade tags and native attack resources. The accessory test reads all three natural GhostFire grant components, inherited targeting wrappers and qualified event sources. The source test checks the full registry ability universe, soft/hard source paths and actual effect grants across weapons, accessories, training and character configurations. These checks never launch or modify the game. `build/extract-combat-art-phases.py` accepts the same three paths plus `--output <json>`; pass `--registry <AssetRegistry-export.json>` to re-enumerate config classes for another build, together with its `--build`.

The weapon-execution test decodes 123 typed entries from all 86 main, temporary and shadow equipment arrays, including all three SpecialAttack entries. It checks Iron/Jigsaw recipes against their native channel, executor conditions, attack initialization and original heavy-input route. `CompiledPhaseMoveTests` exercises Tiger Counter, White Shadow assault and both break attacks through the real router and executor: each requires its own resolved execution type, equipment and upgrade tags, and retains native activation refusal.

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
& $compiler /nologo /main:EditorSessionTests /out:dist\EditorSessionTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:third_party\protobuf-net\protobuf-net.dll /resource:third_party\protobuf-net\protobuf-net.dll,PBZKeyForge.protobuf-net /resource:samples\vanilla-base.sav,PBZKeyForge.vanilla-base.sav $sessionSources tests\EditorSessionTests.cs
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

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\ComparisonMenuTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\ComparisonMenuTests.cs
.\dist\ComparisonMenuTests.exe dist\comparison-menu-check
```

`ComparisonMenuTests` opens the actual More menu and comparison submenu. It checks visible labels, selection changes and reopening after a change from the comparison view, and rendered enabled/disabled text and arrow colors. Screenshots capture both comparison choices and a disabled action in Simplified Chinese.

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

The share checks use an independent Protobuf byte vector to verify stable IDs and packed encoding, raw/DEFLATE selection, skipped metadata, unknown semantic rejection, literal engine keys/family names, frozen resource defaults, explicit empty scopes, and keyboard/controller-family isolation in all three application modes. The model checks save/reload, Unicode names containing colons, pure unbinding, fixed Ctrl plus three editable keys, duplicate names, builtin deletion rejection and source-file protection. Ordinary export includes only the selected scheme's draft; a pending global reset includes all schemes. The GUI harness opens the preset manager with only a library, checks full preset contents from imports and saved entries, renaming, optional favorites, scoped copying and batch deletion. Editing a code immediately clears its prior contents; selecting the library cancels a pending parse. Regular/minimum screenshots check contents and action visibility. The manager returns the chosen preset without changing an editing session. `PresetPreferenceTests` checks independent access without an open save or during reset, main-window load confirmation and cancellation, and the separate Save as preset dialog. The native export file picker is not automated.

```powershell
& $compiler /nologo /out:dist\PresetImpactTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\PresetImpactTests.cs
.\dist\PresetImpactTests.exe
```

Checks actual current/result binding differences in all three modes, native overrides, restoring unlisted bindings, controller scope, trigger parameters, global scripts and input blocks. Main-window acceptance uses the same reviewed plan and preserves cancellation checkpoints for imported presets that have no library ID.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\EnhancedPresetShareTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\EnhancedPresetShareTests.cs
.\dist\EnhancedPresetShareTests.exe dist\enhanced-preset-share-check
```

Use a new output directory. Checks compact enhanced shares against an independent byte vector, every shareable configuration field, custom arguments and scripts, and representative code lengths. Exercises live native overrides in all application modes, keyboard-only preservation of controller rules, independent content previews, invalidated imports, and enabled overflow when the incoming preset was saved with enhancement disabled. Load planning and rejection are separate from library browsing. Pass `--dialogs-only` as the second argument to run only the UI cases. UI captures use Simplified Chinese.

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

## Key enhancement installation

```powershell
& $compiler /nologo /out:dist\KeyEnhancementConfigurationTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\KeyEnhancementConfigurationTests.cs
.\dist\KeyEnhancementConfigurationTests.exe dist\key-enhancement-test-config.lua
python tests\input-router\run_lua.py tests\input-router\CompiledConfigurationTests.lua
python tests\input-router\run_lua.py tests\input-router\RouterTests.lua
python tests\input-router\run_lua.py tests\input-router\StanceInputPerformanceTests.lua
python tests\input-router\run_lua.py tests\input-router\MovementInputTests.lua
python tests\input-router\run_lua.py tests\input-router\BackendTests.lua
python tests\input-router\run_lua.py tests\input-router\PlayerSessionTests.lua
python tests\input-router\run_lua.py tests\input-router\UnifiedBackendTests.lua
python tests\input-router\run_lua.py tests\input-router\ObjectPoolTests.lua
python tests\input-router\run_lua.py tests\input-router\NativeInputTests.lua
python tests\input-router\run_lua.py tests\input-router\PhysicalInputTests.lua
python tests\input-router\run_lua.py tests\input-router\InputResumeTests.lua
python tests\input-router\run_lua.py tests\input-router\MainInputRecoveryTests.lua
python tests\input-router\run_lua.py tests\input-router\NativeAbilityTests.lua
python tests\input-router\run_lua.py tests\input-router\MainLifecycleTests.lua
python tests\input-router\run_lua.py tests\input-router\RecoveryTests.lua
python tests\input-router\run_lua.py tests\input-router\RuntimeIsolationTests.lua
python tests\input-router\run_lua.py tests\input-router\RuntimeRecoveryTests.lua
python tests\input-router\run_lua.py tests\input-router\HotkeyTests.lua
```

`KeyEnhancementConfigurationTests` also compiles every one of the 227 ability catalog entries and requires each output to retain its original GameplayTag and ability target. A same-input-family route, ordinary InputAction, or other ability cannot silently replace it.

```powershell
& $compiler /nologo /out:dist\CatalogInputPipelineTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\CatalogInputPipelineTests.cs
.\dist\CatalogInputPipelineTests.exe dist\catalog-input-pipelines
$env:PBZ_SCHEME_FIXTURES = (Resolve-Path dist\catalog-input-pipelines).Path
python tests\input-router\run_lua.py tests\input-router\NativeInputTests.lua
python tests\input-router\run_lua.py tests\input-router\CompiledAbilityRepeatTests.lua
Remove-Item Env:\PBZ_SCHEME_FIXTURES
```

`CatalogInputPipelineTests` checks all 98 catalog action defaults against the 79 mapped actions and exports real compiler fixtures for both devices, sharing, legacy catalog rules and explicit raw continuous output. The expanded native harness exercises all three compiled schemes against 20 serialized CL-302256 contexts and 173 mapping sources, including inherited, overridden and ignored player-mappable settings. It checks Esc, touchpad and scene fixed sources, editable slot capacity, restoration and real backend Pressed/Released dispatch. A 640-case sweep covers every registered gameplay action on both devices during startup and live validation with chord, blocker, cooldown and repeated-tap dependencies.

The catalog harness also exports shared default and explicit single-press bindings for every direct ability on keyboard and controller. `CompiledAbilityRepeatTests` runs those fixtures through the real router and backend with native eligibility mocked: a held default retries rejected requests and starts another action after completion, release stops requests, and explicit presses remain single. The sweep includes ZhanMa charge slash and retains compiled tags, input actions and priorities. It checks continuous, hold, axis, toggle, pulse, tap, release, multi-tap and sequence policies, mixed outputs, and waiting for a fresh press after pause/resume. These checks establish scheduling behavior rather than actual game animation timing.

Guard fixtures pass keyboard and controller bindings through capture, share encoding, decoding and Lua compilation. Run `.\dist\CatalogInputPipelineTests.exe dist\catalog-input-pipelines --guard-only` to regenerate only the focused source fixtures: `keyboard-guard-sources.lua` and `pad-guard-sources.lua` contain independent Deflect, independent Stance, combined Guard and ordinary light/heavy attacks. The `{keyboard,pad}-{deflect,stance,guard}-{light,heavy}.lua` fixtures pair each guard mode with an attack on the same key; the corresponding `-attack-first.lua` fixtures reverse the output order. Assertions preserve input keys, output order, default versus explicit triggers, device identity, independent single-action outputs and combined Guard's two-action output.

The native harness models the CL-302256 Passive input order and both defense key states, checking alternating one-frame presses/releases, defense windows, stop-on-release, overlapping inputs, original sources and cancellation. Independent Deflect retains the native Stance pipeline needed for release, while its paired Stance does not contribute the `HoldingStanceKey` used for combat-art eligibility. Explicit Stance and ordinary combined Guard retain that contribution. Same-key light/heavy attack checks use ten cooked AIM route conditions, including SnakeDual DB and ChaosBlade stance attacks, in both output orders over held frames; ordinary SnakeDual AB remains available and Deflect still opens its native defense window. The runtime aggregates sources before dispatch; releasing explicit Stance while Deflect remains active removes combat-art eligibility without clearing defense windows. Explicit single presses and configured pulse intervals retain their trigger policy. These model checks do not establish in-game animation timing.

`StanceInputPerformanceTests` runs 2000 stable frames against 64 native coverage entries. It requires no repeated coverage scan, static object/library lookup or native contribution preparation after warmup, and requires the trigger path to sample exactly once per frame without re-registering its observation. Set `PBZ_STANCE_BENCHMARK_BASELINE` to an earlier `stance-input.lua` to print the model comparison. These are call-count bounds, not game frame-time measurements.

`MovementInputTests` exercises the noncombat ledge policy through `Backend.refresh`, input preparation, shutdown and pawn retirement. Dodge input and active dodge abilities enable the movement exception when walking off is permitted or jumping is allowed. Walking alone, combat, pause, blocked movement and disabled routing retain native protection. Checks cover both eligibility branches, native/object-backed/held/direct dodge requests, action completion, native counter resets, other native owners, replacement, respawn and restoration retry. `LedgeProtectionNativeTests.cpp` checks local movement overrides, both protection counters during walking calculations, restoration after every calculation, repeated frames starting from a cleared disable counter, other actors and native layout rejection. Lua leaves native counters unchanged; the native override scopes their changes to each physics call. `MovementInputResourceTests` checks native interfaces and tags against installed resources. `NativeInputTests` retains jump, dodge press and dodge hold through keyboard and controller mappings. `MoveEquipmentTests` also checks independent flash step drawing the current weapon after each sheathing without replacing equipment or repeatedly drawing. These checks simulate engine objects; game behavior is verified separately.

The model checks cover JSON isolation, mapping identities and generated Lua. With `unified_native`, runtime mocks check each active mapping row's execution owner: supported registered gameplay inputs enter the unified scheduler and their native execution is masked, while menu/text inputs and unsupported sources remain native. `NativeInputTests.lua` checks action/key suppression, consume locks through physical release, obsolete wrapper rejection after mapping-array rebuilds, independent clones of native trigger/modifier configuration, and coverage records with reasons. Unbound devices retain their own default output pipelines for enhanced bindings without restoring native input; current runtime pipelines take precedence on each device. Binding coexistence and native duplicate prevention are separate from partial or full key consumption. Consumed keys remain sampled across configuration generations even when removed from the router's ordinary sampling list. Script watch inputs obey consumption, skip callbacks while occupied, and rearm after physical release.

`PlayerSessionTests.lua` verifies generation ownership, Pawn replacement invalidation, per-generation ability component caching and zero repeated Pawn/component lookups across 1,000 dispatches. `MainLifecycleTests.lua` verifies EndPlay, Possess and UnPossess hooks install once, invalidate immediately and are removed on reload or stop.

`UnifiedBackendTests.lua` exercises PBZ CommonUI Game/Menu/All input modes, unavailable or restored local-player routers, UI-only state, and keyboard/user focus in all four editable-text widget classes. Routing must yield to UI/text and restore native mappings, then resume only when gameplay owns input. `PhysicalInputTests.lua` checks independent keyboard/mouse-button sampling. `InputResumeTests.lua`, `MainInputRecoveryTests.lua` and the main-loop lifecycle checks distinguish continuous state recovery from discrete attacks, jumps, taps, pulses and toggles: character movement, sprint, sailing, stance and enhanced continuous axes resume, while a held one-shot input waits for release rather than generating a fresh press. The native harness covers keyboard/controller sailing, independent stance and catalog camera/target axes across recovery, condition changes and returning contexts. Explicit continuous raw axes retain signed values and reject temporal pipelines for automatic recovery. Stance and camera recovery remain independent of character movement blocking; recovering combined guard must restore both stance key states without refreshing perfect deflect, and independent deflect must not replay its paired stance pulse. These mocks do not establish actual game behavior. `run_lua.py` accepts `--lua-dll` for an installed Lua 5.4 runtime.

`NativeInputTests.lua` also runs the real main frame loop, backend refresh, native pipeline preparation and router together with mocked engine objects. It exercises scene appearance/removal, empty mapping arrays, live reload, scheme switching, menu return and pawn replacement while movement is held. Scene interaction waits for a fresh press; shutdown restores the current native rows. Native overrides retain cooldown/chord pipelines during live validation, while custom enhanced outputs still reject unsupported pipelines. Mixed scheduled/native mappings must reject retained-slot overflow without changing any native row, then accept a valid retry.

`python tests/input-router/run_lua.py tests/input-router/AbilitySelectionTests.lua` checks indexed grant/instance selection, class-address comparisons, all/any tag semantics, replacement classes and instances, and classes loaded inside native activation. Set `PBZ_ABILITY_SELECTION_BASELINE` to an earlier `resource-costs.lua` to print the optional model comparison. `NativeInputTests` also bounds unrelated target checks, shared action path/trigger reads, and unbound-template comparisons during rebuilding.

`AbilitySelectionNativeTests` checks the reflected grant/tag layouts, class identity, tag boundaries and duplicates, grant replacement, and the batch selector through actual Lua C calls, including argument consumption and error returns. It uses engine fixtures; its optional timing output is not a game benchmark.

```powershell
cl.exe /nologo /O2 /MD /EHsc /std:c++17 /Fo:dist\AbilitySelectionNativeTests.obj /Fe:dist\AbilitySelectionNativeTests.exe tests\input-router\AbilitySelectionNativeTests.cpp
.\dist\AbilitySelectionNativeTests.exe
```

Performance regressions require exactly one runtime-row inspection per unchanged unified frame and no extra inspection during consumption. In-place key edits, trigger eligibility changes and action-level trigger replacement must invalidate cached ownership. Equivalent continuous movement sources inject once, while different directions, temporal triggers and stateful modifiers remain distinct. `ObjectPoolTests` checks initial discovery, creation notifications, duplicates, invalid objects and late asset classes. Focus changes are checked on the next frame without repeating global discovery; vector components share one physical read per frame. See [runtime performance](../docs/key-enhancement-performance.md) for the optional before/after model benchmark.

Suppressed native sources retain their native masks without permanently inactive scheduler rules; changing suppression rebuilds those rules. `RouterTests` checks that reconfiguration samples the replacement keys once and rearms held discrete inputs after release. `ScriptContextTests` rejects a global Lua UObject walk during damage initialization and requires native queries to include derived instances and class defaults. `AbilityTuningChainTests` checks 120 idle activations without scanning granted abilities, foreign activation isolation and cleanup before ordinary reuse. `TuningActivationTests` checks lazy tuning preparation. `InputActionProvenanceTests` checks activation-time native source participation, gesture recognition, stale frame and player isolation, mapping rebuild and restoration. The optional native benchmark also reports rule counts and combined input preparation/router time.

`RuntimeIsolationTests` exercises the real backend and frame scheduler with failing function targets and hooks. Incorrect receiver classes must be rejected before calling the configured native function; inherited methods and implemented interfaces remain valid. Target retries use exponential backoff while healthy bindings continue. Move cleanup releases only its owning binding's input and ability, and cleanup failures propagate to shared input restoration. Stable held requests must not rebuild static request identities. `RuntimeRecoveryTests` verifies same-key and different-key bindings of one target, repaired held bindings, predicate and tuning failures, deduplicated move alternatives, deferred cleanup ownership, shared cleanup retries, diagnostic throttling and held-input recovery. `NativeInputTests` checks indexed mapping reads outside map callbacks and restoration after partial remapping. These tests simulate engine objects. `CompleteMoveTests` additionally covers deferred callback isolation; `ObjectPoolTests` requires stable frames to reuse their object list.

```powershell
& $compiler /nologo /out:dist\KeyEnhancementInstallationTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.IO.Compression.dll tests\KeyEnhancementInstallationTests.cs
.\dist\KeyEnhancementInstallationTests.exe dist\key-enhancement-installation-check
```

Use a new output directory. The harness creates fake game installations and never launches a game. It checks enabling, disabling while retaining configuration and shared loaders, unchanged reapplication, missing-file repair, generated Lua rebuilding, external JSON changes and recovery, unknown-file preservation, missing source JSON conflicts and recovery, existing UE4SS settings and mod lists, explicit loader-level disable entries, and cleanup after failed installation. Managed upgrades replace old scripts and remove obsolete managed directories without changing schemes or bindings. Changed files recorded by the installer are backed up with the complete Mod directory before replacement; unknown files remain conflicts. Uninstall/reopen/reinstall and manual Mod deletion restore the full configuration from the copy outside the Mod directory; locked copies must roll back installation changes, and external edits must block stale reinstallation. Complete uninstall removes owned runtime files and enable entries, retains personal configuration, removes an unchanged loader created by this tool when no other Mod needs it, and preserves shared or modified files. The UI path is the … button beside Key enhancement → Uninstall key enhancement → Apply changes. These assertions verify installation and persistence; they do not establish that the game loaded or executed the Mod.

The component menu is now beside the key enhancement switch. The installer uses `mods.txt` and removes the Router's `enabled.txt`; an owned standalone loader is parked while enhancement is disabled. Existing loaders remain untouched. Check complete third-party packages and preservation of activation lists with:

```powershell
& $compiler /nologo /out:dist\KeyEnhancementCoexistenceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.IO.Compression.dll tests\KeyEnhancementCoexistenceTests.cs
.\dist\KeyEnhancementCoexistenceTests.exe dist\key-enhancement-coexistence-check 'D:\Downloads\Vercadi-runtime.zip'
```

The optional second argument is a local Vercadi runtime ZIP. Without it, CI checks the downloaded Kintaro package, custom mod directories and controlling lists, standalone disable/re-enable, newly installed user mods, and preserving edits made between applies. The Vercadi package comparison verifies file installation and preservation; runtime behavior still needs game testing on that build.

```powershell
& $compiler /nologo /out:dist\Ue4ssUpdateTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.IO.Compression.dll tests\Ue4ssUpdateTests.cs
.\dist\Ue4ssUpdateTests.exe dist\ue4ss-update-check
```

Update checks use isolated packages and installations to verify release ordering, preservation of settings and user Mods, runtime backups, locked files, rollback after a partial replacement, invalid ZIP rejection, and disable/uninstall after an update. The main enhancement UI harness supplies release metadata without network access and verifies that Apply offers an update and continues with the existing version when declined.

Cross-device prerequisite and build checks:

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\RuntimeUpdateTests.ps1 -OutputDirectory dist\runtime-update-check` verifies development cache reuse, forced refreshes, same-version asset replacement, local/public game changes, unknown copied installations, offline release rejection and recovery. It uses synthetic Steam manifests and intercepted API responses; it does not launch a game or access the network. Release and scheduled-update workflows run these assertions before querying current upstream data.

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

While a real game is running, append `--inspect-only` to inspect the current runtime payload without installing. It rejects generated Lua without its source JSON and checks that all required input modules are embedded. Full Apply and uninstall checks require the game to be closed; the global running-game guard remains active for fixtures.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\MainKeyEnhancementTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.Web.Extensions.dll tests\MainKeyEnhancementTests.cs
.\dist\MainKeyEnhancementTests.exe samples\vanilla-base.sav dist\main-key-enhancement-check
.\dist\MainKeyEnhancementTests.exe samples\vanilla-base.sav dist\key-enhancement-reinstall-ui-check --reinstall-only
```

The main-window harness creates its own fake installation. It checks staged toggles, the left-hand mode switch, Undo, shared bindings across scheme changes, Apply without save changes, partial apply and retry, retaining bindings when disabled, explicit installation repair, and offline export. A disabled incomplete installation must remain a clean gray state. It captures disabled, pending, installed, minimum-size, and offline states. No actual game installation or save is used.

`--reinstall-only` checks the component menu and real Apply button independently: uninstall retains all enhanced schemes, bindings and custom functions, refresh keeps those settings, and enabling again installs the same configuration without changing the native input save. Screenshots capture the retained configuration and restored installation.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\UnifiedBindingFlowTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll tests\UnifiedBindingFlowTests.cs
.\dist\UnifiedBindingFlowTests.exe samples\vanilla-base.sav dist\unified-binding-flow-check
```

The unified-flow harness starts with a partial current runtime payload in a fake installation. It uses the ordinary key's Add Function entry to add beyond native capacity, edits a hold binding through ordinary action details, verifies disabled extra bindings remain gray and editable, and edits additional functions while enhancement remains disabled without losing the selection. Apply and disable retain the unified binding data. Screenshots cover these interactions; no game is launched.

Append `--edit-only` to run the ordinary editing and screenshot paths while a real game is running. This skips Apply and disable installation checks.

```powershell
& $compiler /nologo /out:dist\BindingWorkspaceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\BindingWorkspaceTests.cs
.\dist\BindingWorkspaceTests.exe
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\UnifiedBindingDialogTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\UnifiedBindingDialogTests.cs
.\dist\UnifiedBindingDialogTests.exe dist\unified-dialogs-check
```

Workspace assertions cover shared-output edits, replacement ownership, unlimited extra bindings, output vectors, hook-path conflicts and restoring native ability triggers. Physical trigger restoration covers both button and axis names, suppression cleanup, atomic capacity failure and retry. Dialog checks cover every trigger, per-step combinations, partial key consumption, priority, axis directions, clearing hidden chord conditions when switching to an axis, typed recursive values and the native ability trigger checkbox. Layout captures use 150% DPI and do not write game data.

`ExpandedBindingUxTests` covers device-scoped restoration, cross-device editing, persistent unbound custom definitions and explicit deletion, trigger normalization, consumption remapping, watched-input consumption, disabling native replacements, duplicate addition with full slots, enhanced whole-key restoration, preset overflow and preview cancellation. Compile with the same references and DPI manifest as `UnifiedBindingDialogTests`, then run with the default output directory. `python tests\input-router\run_lua.py tests\input-router\EditedBindingTests.lua` reads its generated axis and watch configurations to verify that edited axes do not consume old sequence keys and watched-input consumption reaches the router. `BundleImportTests` also preserves unbound custom definitions.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\ResourceBindingTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\ResourceBindingTests.cs
.\dist\ResourceBindingTests.exe dist\resource-binding-check
python tests\input-router\run_lua.py tests\input-router\ResourceCostTests.lua
python tests\input-router\run_lua.py tests\input-router\ActiveMoveTuningTests.lua
python tests\input-router\run_lua.py tests\input-router\AbilityTuningChainTests.lua
python tests\input-router\run_lua.py tests\input-router\BindingTuningLifecycleTests.lua
python tests\input-router\run_lua.py tests\input-router\FinisherSpeedTests.lua
python tests\input-router\run_lua.py tests\input-router\ZhanMaChargeSpeedTests.lua
python tests\input-router\run_lua.py tests\input-router\TimelineSpeedLifecycleTests.lua
python tests\input-router\run_lua.py tests\input-router\ChargeClockTests.lua
```

`ResourceBindingTests` queries all ordinary mappings and edits both menu mappings on keyboard and controller with enhancement enabled and disabled. It checks resource lookup and the visible option for the tag-only N3H3 move and the direct, switch-leaf and wrapper dual-axe entries, then uses Ctrl+Z in the binding editor and saves another change to verify both directions of the resource checkbox. Damage, startup and recovery settings are edited through the dialog and retained through copying, JSON, share codes and ZIP export. The targeted translation checks cover the three new tuning labels in all languages. It generates the configuration consumed by `ResourceCostTests`; set `PBZ_RESOURCE_BINDING_FIXTURES` to the C# output directory when using a different path. The Lua test runs simultaneous ordinary and bypass bindings through the router and resource hooks with an empty resource bank, in both rule orders. It verifies continuous charges, preservation of nominal fees, gains and other actors, cleanup on failure, native ability damage settings before projectile creation, independent startup/recovery rates and restoration, and exclusion of non-timeline abilities. These checks use offline sessions and simulated abilities.

`ActiveMoveTuningTests` consumes `ResourceBindingTests`' compiled active-move targets and uses the real executor and resource hooks. It verifies shield release damage and timeline speeds, prepared ghost-fire launch snapshots, callback cleanup and failure, independent later bindings, and preservation of naturally active effects. The C# checks also capture the ghost-fire editor and reject phase speeds on its callback-only entry. Lua damage checks verify the ability setting before native initialization and the explicit ghost-fire launch override; the native harness verifies calculator output scaling through direct virtual calls.

Native projectile interface hooks receive an interface subobject. The native context bridge resolves its owning UObject through `_getUObject()` before Lua consumes it. `ScriptContextTests` rejects reads of interface Context, checks capture registration order, nested initialization and projectile launch, and registers new damage payloads and calculators before their first synchronous hit. Both loaded class defaults and new instances cover `BulletActorBase` and `BulletObjectBase`, using the matching base for native interface discovery. `ResourceCostTests` checks ability settings before initialization and rejects Lua post-hook replacement of the native launch snapshot. Menu, focus and configuration lifecycle checks preserve launched-projectile snapshots while releasing input ownership; leaving the pawn or stopping the Mod clears them.

`TuningActivationTests` verifies that explicit 1x defaults create no tuning context, each of resource bypass, speed and damage installs only its own hooks, repeated requests do not duplicate registration, and enhanced actions retain their complete policy while native source participation is decided at activation. Constructor, feature-registration and timeline-restoration failures must still attempt native context shutdown and removal of earlier hooks. Failed rate, damage and Hook cleanup remains owned for retry; completed operations must not repeat. The tests inject simultaneous cleanup failures through backend shutdown and verify that a second shutdown restores the remaining state while native input restoration proceeds on both attempts. `ScriptContextTests` rejects any damage-object access before enabling damage and after reset, and checks cleanup after a newly encountered class rejects registration. The native harness checks restored calculator and both bullet virtual slots, then re-enables them through the public exports. `MainLifecycleTests` rejects player refresh, native mapping preparation and hook registration while enhancement is disabled.

The native harness keeps inherited `Modules`, `Bullet` and `DamageSource` fields separate from directly declared fields. Direct lookup must fail for these fixtures; chain lookup must let both bullet layouts scale damage inside native initialization, retain launch ownership and report registered hooks and payloads. This matches the installed UE4SS distinction between `GetPropertyByName` and `GetPropertyByNameInChain`.

`ScriptContextTests` rejects `FindAllOf("Class")`, supplies class discovery through `ForEachUObject`, and checks existing bullet registration and invalidation. The native harness prepares Initialize before any bullet exists, replaces its exec bytes as a UFunction hook does, and then initializes both bullet families through the saved slot. `BackendTests` injects simultaneous move, tuning and ability restoration failures and requires native input and captured contexts to be restored; retry must not duplicate context registrations. Asynchronous failures retain their original cause even if cleanup also fails.

`ResourceCostTests` covers seven additional resource qualification sites, all four attribute query APIs, zero-resource continuous costs, nominal air-heavy consumption and full-capacity resource-based effects. Current AttributeSet limits, changed and zero maxima, other actors and ordinary abilities distinguish the resource view from a fixed unlimited value. Two recovery abilities that override Shachi retain their real resource only for their owning bypass binding; idle hooks resolve no game objects. Two pure cost GameplayEffects are suppressed only for their actual bypass instance; unrelated effects, other abilities, unchecked bindings and subsequent ordinary operations retain the effect class. Native evidence establishes that a null class skips application and returns an invalid handle; these Blueprint call sites do not consume that handle.

Class parameter fixtures preserve the UE4SS distinction between a nil setter, which leaves the class unchanged, and a typed null UClass, which writes a null pointer. The native bridge creates that userdata through the pinned `push_classproperty(Get)` API after the original class parameter has established its metatable. The native harness checks the public pusher layout and one-userdata return through Lua C calls. Backend reload checks distinguish healthy, closed tuning and closed native contexts; failed cleanup remains retryable while healthy launched-projectile snapshots survive a reload.

`AbilityTuningChainTests` covers seven native class/tag handoff APIs, multiple handoffs, first-fee bypass, independent child timeline speeds and cleanup, and rejection of failed, inactive, unrelated or callback-only transfers. `BindingTuningLifecycleTests` also covers the plural tag activation API, synchronous and delayed releases, instances created inside native activation, projectile snapshots after handoff, and shared ability settings across ordinary and tuned bindings. It checks distinct InputActions and modifier pipelines in both binding orders, reduced damage multipliers, priority between conflicting parameter groups, and vector cancellation. Resource getter tests read the native result slot after the post hook, including zero-resource qualification. Ordinary reads, other abilities on the same player, other actors and non-resource attributes retain their original results. Ability damage snapshots are registered before activation callbacks and restored after failed handoffs and temporary callbacks; destroyed abilities are cleared by their saved address.

`BindingTuningLifecycleTests` additionally consumes the compiled red ghost-blade release and three weapon-switch entries from `ResourceBindingTests`. It checks the original executor callbacks and delayed release, and verifies independent settings between ordinary abilities, complete moves and InputActions in both binding orders. Only the actual activation request owns its complete parameter set; a rejected ordinary request cannot prevent a later eligible tuned request. Multi-tag activation retains all-tag matching, and a rejected continuation restores its previous state.

Attached super armor and invincibility use the same compiled routes. The lifecycle fixture checks independent flags, inherited continuations, overlapping native tag counts, preexisting buffs, canceled and failed requests, partial setup failure, cleanup retry and pawn replacement. Stable protected frames must perform no repeated tag reads or writes. `ResourceBindingTests` also covers the two checkboxes above phase speeds and their JSON, share and ZIP roundtrips.

Same-frame instance reuse must restore the completed action's speed and use the next activation's parameters without waiting for a frame update. An InputAction with a remaining native wheel or touchpad source retains default fees and multipliers; removing or taking ownership of that source restores action tuning. Explicit move and ability requests keep their own settings in both cases.

`TimelineSpeedLifecycleTests` exercises all 17 compiled finisher windows, eight native activation APIs, native autoplay and the three reflected playback entry points. It covers original-to-enhanced and enhanced-to-original restarts on an active instance, including an ordinary native request without a binding policy, fractional native rates, independent recovery speed, InputAction ownership, nested activation, failed requests and matching instances whose tasks did not restart. `BindingTuningLifecycleTests` also checks damage snapshots and subsequent costs after the same native restart. Ordinary native restarts clear the old policy while retaining damage on already launched projectiles. `PlayTimelineByName` must preserve the restarted execution's initial rate even when the previous original execution has no tuning scope.

Run `python tests\TimelineSpeedResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle --output dist\timeline-speed-resource-check.json` against an explicitly selected installation. It rereads all player main and temporary weapon combat-art timelines, checks 46 configured startup/attack/recovery windows across 43 independent phases against retained attack actions, and checks the Zhan Ma charge boundary. Branches retain their own windows, including immediate attacks at phase entry and instantaneous damage events. These resource checks complement the Lua lifecycle fixtures; they do not observe animations in game.

`FinisherSpeedTests` consumes the compiled `phase-moves.lua` from `CompleteMoveCatalogTests`. It checks all 46 phase windows, the Bronze finisher's cooked startup/attack/recovery boundaries, unchanged rates outside its selected timeline, and ordinary later activations. `TimelineSpeedLifecycleTests` executes all 46 windows through native activation and autoplay, including branch-specific timing and instantaneous damage events. Set `PBZ_PHASE_MOVE_CONFIG` when using a different fixture path.

`ZhanMaChargeSpeedTests` checks the Zhan Ma charged slash through tag and exact-class activation, including native autoplay with no reflected playback hook. Autoplay uses the engine's initial rate of one; explicit playback rates take precedence, and an unknown rate on another timeline stays untouched. Charge uses startup speed until its first cooked bullet action; both attacks, their interval and the native recovery phase retain separate rates. It covers slower charge, recovery-only tuning, timeline changes, cancellation, subsequent ordinary activation and unrelated abilities on the same actor.

`ChargeClockTests` checks Bronze Sword and Storm Sword preparation timelines and independent hold thresholds at 0.5x, 2x and 10x. It covers active-instance restarts, failed requests, ordinary native input, other actors, release attack/recovery, ending and temporary callback restoration. `python tests\ChargeClockResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle` verifies the native hold signature, the Storm Sword's float `DeltaTime` callback, charge-only Main timelines and original consumption/progress coefficients against installed resources. The native context harness checks scaling before the tick body, restoring the input after execution, nested ordinary callbacks, exact function/instance ownership, cleanup and rejection of an incompatible parameter layout.

The context bridge has a standalone native harness. In the configured Visual C++ environment, run:

```powershell
cl.exe /nologo /O2 /MD /EHsc /std:c++17 /Fo:dist\ScriptContextNativeTests.obj /Fe:dist\ScriptContextNativeTests.exe tests\input-router\ScriptContextNativeTests.cpp
.\dist\ScriptContextNativeTests.exe
python tests\input-router\run_lua.py tests\input-router\ScriptContextTests.lua
cl.exe /nologo /O2 /MD /EHsc /std:c++17 /Fo:dist\InputTriggerNativeTests.obj /Fe:dist\InputTriggerNativeTests.exe tests\input-router\InputTriggerNativeTests.cpp
.\dist\InputTriggerNativeTests.exe
cl.exe /nologo /O2 /MD /EHsc /std:c++17 /Fo:dist\InputObservationNativeTests.obj /Fe:dist\InputObservationNativeTests.exe tests\input-router\InputObservationNativeTests.cpp
.\dist\InputObservationNativeTests.exe
```

`InputObservationNativeTests` checks 2000 stable frames, mapping and context edits, key settings, trigger thresholds, action triggers, stopping before retired allocations, unsupported layouts and empty input contexts. It also observes `InputActionBinding` normal and conditional trigger arrays, sparse map slots, independent assets and retired arrays/maps. Its ability observation exports run through actual Lua C calls to check consuming arguments, the unchanged sentinel, handles and error cleanup; 2000 stable frames must perform no reflection queries. `NativeAbilityTests` bounds stable Lua enumeration and checks in-place tag edits, configuration changes, array replacement and restoration. `NativeInputTests` bounds continuous source comparisons and shared trigger class reflection during rebuilding. `MoveEquipmentTests` requires one Lua class read per frame for one active ability among 130 grants, and exact equipment restoration on completion. `MovementInputTests` requires one active selector call per idle frame while retaining native activation and intent changes. These are model checks, not game frame-time measurements.

`UnifiedBackendTests` exercises a Blueprint UI-mode call followed by native gameplay restoration without a `SetInputMode_GameOnly` call; G must resume after release while a key held through the menu stays suppressed.

It checks nested Blueprint object/function identity, native interface pointers distinct from their owning UObjects, unchanged native receivers, nested interface calls, stack unwinding, capture reset and registration reuse. Projectile initialization runs through direct interface virtual calls without a reflected function callback. Actor and Object fixtures use distinct interface offsets of 0x2b0 and 0x28 and separate reflected Modules offsets, including the first hit inside initialization, 100x damage, cross-family parent snapshots, ordinary reuse and cleanup after disabling. The observation export reports calculator input/output values and clears on reset. Damage inputs belong to independent payload maps; their current source determines the multiplier, including repeated hits, nested hits and calculator forwarding without double scaling. Every context export runs through an actual Lua C call, including consuming argument reads, empty returns, context result ordering, calculator/payload registration, 11x and fractional damage, unsupported getter diagnostics and error returns. Pass the Lua DLL path as the native executable's first argument when it is outside the default Wireshark directory. Engine objects and exec thunks are fixtures; the production bridge links against the pinned UE4SS public exports. These tests do not load the game.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingIdentityUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingIdentityUiTests.cs
.\dist\BindingIdentityUiTests.exe dist\binding-identity-check
```

`BindingIdentityUiTests` adds the weapon input action and ability through the picker. It checks their distinct behavior labels and identities in bound key rows, current-key and other-key assignments, repeated binding, inactive chords, removal and device isolation. Screenshots cover bound rows, the picker and its minimum size. All changes remain in an offline session.

```powershell
& $compiler /nologo /out:dist\MovementBindingTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\MovementBindingTests.cs
.\dist\MovementBindingTests.exe
python tests\input-router\run_lua.py tests\input-router\MovementBindingsTests.lua
```

Movement checks cover the common catalog order, WASD and arrow-key sources, directional replacement and restoration, JSON identities, unchanged save bytes, and single-press sheathing. The generated Lua is dispatched through the router to check the CL-302256 native keyboard axes: W/S use positive/negative X, A/D use negative/positive Y. UI captures include WASD and sheathing searches in the action grid and ordinary function picker. These checks do not establish actual game behavior.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\InputOwnershipUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\InputOwnershipUiTests.cs
.\dist\InputOwnershipUiTests.exe dist\ownership-ui-check
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BundleImportTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll tests\BundleImportTests.cs
.\dist\BundleImportTests.exe dist\bundle-import-check
```

The ownership UI harness distinguishes save/rule provenance from planned scheduler/native/inactive/uncovered execution. It checks native masking separately from coexistence and partial consume rules, known unsupported wheel/axis/touchpad sources, concrete coverage gaps, all 15 ownership dictionaries, and readable action-detail layout. Its screenshot describes the current draft's plan after applying changes, not a running Mod.

The bundle harness invokes the existing export/import methods on offline fixture sessions. Both supported default sets must preserve the selected profile, ordinary key changes, complex rules, the complete serialized save and unknown trailing bytes. ZIP imports remain offline. Global reset restores imported keyboard and controller bindings in every scheme, retains unknown trailing data and allows editing immediately. Missing configuration, invalid JSON and invalid save bytes must leave the previous session and configuration intact. It never installs components or writes a game save. `MainKeyEnhancementTests` also checks that undoing global reset restores both ordinary drafts and the complete enhancement configuration, including unbound custom functions; a second Undo returns to the saved baseline.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\UnifiedActionViewTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\UnifiedActionViewTests.cs
.\dist\UnifiedActionViewTests.exe dist\action-grid-check
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\KeyEnhancementFlameOverlayTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\KeyEnhancementFlameOverlayTests.cs
.\dist\KeyEnhancementFlameOverlayTests.exe dist\key-enhancement-flame-check
```

The action-grid harness checks mode-dependent groups, native weapon branches, cross-group name/code search, compact rows, virtual scrolling, and the ordinary details/edit/add events. It records render and group-switch times and captures the grid at normal and minimum sizes. The flame harness checks the compact shared footer row, vertically centered Apply button, scheme status placement, 64-pixel puppet artwork with grayscale when disabled, help-button accessibility, background-only rendering without a foreground window, unchanged text/icon/button pixels, client-area containment, the default bottom 30% limit, a continuous burning base without empty bands, translucent purple bodies with bright cores, animated borders, motion direction and inertia, distinct session/size distributions, persisted appearance settings independent of bindings, animation lifecycle, owner movement, resize, minimize/restore, continued display and animation after focus loss, and disposal. Its screenshots use `DrawToBitmap`, which includes the actual background flame frame and foreground content in their ordinary paint order. Both harnesses use offline workspaces and never install a Mod.

Appearance dialog checks cover live previews, saving all five controls including hue, cancel rollback, effect enable/disable, and persisted preferences independent of game bindings. Fixed-time renderer comparisons verify brightness and hue independently from transparency and enforce the selected height. An opaque coordinate fixture verifies action cells, tree blank space, and outer padding before and after scrolling. `appearance-dialog.png` captures the settings window. Rapid slider sweeps must coalesce into a single latest-value callback; renderer buffers are reused without changing the procedural sampling.

The flame harness also captures fixed-time `flow-idle`, `flow-left`, and `flow-right` frames against the real interface, with full-resolution footer crops. Inspect these captures and the motion preview for flame structure, transparency, sampling artifacts, and wind deformation; the functional assertions do not establish visual similarity to the game reference.

The function catalog contains 98 InputActions and 236 input-reachable ability tags, plus four directional movement entries. Labels use matched native move titles, descriptive input names and short stage codes in all 15 UI languages. Shared abilities retain their shared weapon scope. Targeted localization checks preserve native titles before stage details and verify every semantic vocabulary reference in the selected language dictionary. The [name and evidence inventory](../docs/key-enhancement-action-names.md) records each target. The advanced function/value dialogs use typed scalar controls and recursive struct/array editing for parameters, event payloads and property values.

```powershell
& $compiler /nologo /out:dist\ActionCatalogTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll tests\ActionCatalogTests.cs
.\dist\ActionCatalogTests.exe
& $compiler /nologo /out:dist\KeyEnhancementLocalizationTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Web.Extensions.dll tests\KeyEnhancementLocalizationTests.cs
.\dist\KeyEnhancementLocalizationTests.exe
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\FlameBackgroundLayerTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\FlameBackgroundLayerTests.cs
.\dist\FlameBackgroundLayerTests.exe dist\flame-background-layer-check
```

Catalog assertions cover all 334 targets, the 21 ordinary entry points, exact parity of standard controls with ordinary mode, complete categories by player purpose, mode/device ordering, shared search, distinguishable variants, scene/debug classification and contiguous weapon branches. The flame layer harness enables key enhancement through its checkbox and compares the backgrounds of nested and direct footer controls across two translucent frames. It also checks foreground preservation, continuous backgrounds across headers and tree indentation, scrolling alignment, scrollbars and native tree mouse/keyboard interaction. It also compares legend labels and dots across flame frames and reads the window surface during painting to detect visible foreground erasure. These checks use offline fixtures.


```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\FlamePreviewPerformanceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\FlamePreviewPerformanceTests.cs
.\dist\FlamePreviewPerformanceTests.exe dist\flame-preview-performance
```

The preview performance harness moves the hue slider with a native UI timer in both key and action views at 30% and 100% height. It records presented frames, input callback gaps and per-control paint costs. Run it separately from other GUI harnesses. Frame progress is asserted; timing output describes the current machine and is not a fixed frame-rate guarantee.

For offline editing measurements and real-installation Apply measurements:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\InteractionPerformanceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\InteractionPerformanceTests.cs
.\dist\InteractionPerformanceTests.exe dist\interaction-performance
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\RealGamePerformanceTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\RealGamePerformanceTests.cs
.\dist\RealGamePerformanceTests.exe $gameRoot dist\real-ordinary-performance
.\dist\RealGamePerformanceTests.exe $gameRoot dist\real-live-performance --live
```

`RealGamePerformanceTests` requires an explicitly selected real installation with key enhancement already enabled. The ordinary run requires the game closed; the live run requires the updated Mod connected in a loaded game scene. It temporarily applies three hold-F10 Jump configurations, then restores the original configuration through the installation API. It saves the original JSON/Lua in the new output directory, checks byte preservation of the input and shared saves, records complete Apply duration and maximum UI timer gap, and captures the final interface. Failed runs also attempt configuration restoration. These runs modify the real Mod configuration and must run separately from other GUI harnesses; they measure application and acknowledgment, not the physical action's gameplay effect. `--no-effects` disables flame effects for a diagnostic comparison.

`InteractionPerformanceTests` uses offline fixtures for catalog/snapshot construction, editor opening and repeated view refreshes. It does not measure disk application or live game response. Append `--layout-only` to `UnifiedBindingDialogTests` to run its focused 150% layout check without the editing suite.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingEditingTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll tests\BindingEditingTests.cs
.\dist\BindingEditingTests.exe dist\binding-editing-check
```

Binding editing checks cover atomic rejection and retry of key restoration with full native slots, UE keypad identities, left/right modifier chords, ordinary chord recording, direct unbinding, separate rules on one physical key, disabled-rule visibility and reactivation, ability trigger replacement, duration-only differences, enhanced preset save/share roundtrips, preset preview cancellation and switching, and the complete factory reset. The key picker opens the same binding editor as the action view. Screenshots cover individual key bindings, disabled bindings, binding/sequence editors and the reset result. The dialog harness additionally checks that all five sequence commands fit at 150% scale. These harnesses use offline fixtures.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\KeyViewBindingSettingsTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\KeyViewBindingSettingsTests.cs
.\dist\KeyViewBindingSettingsTests.exe dist\key-view-binding-settings-check
```

`KeyViewBindingSettingsTests` exercises original-trigger settings through quick addition, configured addition, cancellation and reopening on keyboard and controller, with enhancement enabled and disabled. Batch additions preserve mixed policies until explicitly changed. Main-window checks click each binding's settings command and its card, edit separate and shared outputs, retain disabled bindings, open native/fixed bindings, and check minimum-window layout. Screenshots cover the picker, existing-binding editor and key view. These checks use offline drafts and never modify a game installation.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingRestoreUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\BindingRestoreUiTests.cs
.\dist\BindingRestoreUiTests.exe dist\binding-restore-ui
```

The restore UI harness clicks the removed Sprint relationship with native mouse messages. It covers free and full native slots, confirms that enhancement bindings provide restoration when native slots are full, and preserves the other Sprint keys and Shift Dodge. Screenshots capture the removed and restored states. Run GUI harnesses sequentially without other applications taking focus.

It also replaces Space with J through runtime editing, then clicks the individual factory restore entry in Space's details. Both keys must remain active without changing save bytes. Workspace checks cover the same restoration for keyboard and controller overrides, repeated restores, and live unbinding/replacement of all four fixed movement directions while preserving their arrow-key sources.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\LiveEditingTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll tests\LiveEditingTests.cs
.\dist\LiveEditingTests.exe dist\live-editing-check
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\LiveBindingWindowTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\LiveBindingWindowTests.cs
.\dist\LiveBindingWindowTests.exe dist\live-binding-window-check
python tests\CliAgentTests.py dist\PBZKeyForge-cli.exe dist\cli-agent-check
python tests\CliLifecycleTests.py dist\PBZKeyForge-cli.exe dist\cli-lifecycle-check
python tests\input-router\run_lua.py tests\input-router\LiveConfigurationTests.lua
```

`LiveEditingTests` covers disabled enhancement drafts, paired tap/hold reservations, multiple outputs, binding copy, whole-key movement including fixed WASD sources, native runtime overrides beyond save capacity, device-filtered sharing, append conflicts, native disabling, full-reset undo, compact warning cards and complete error dialogs. Its fake Mod acknowledges or rejects file requests and tests recovery from suspension, timeout cancellation, persistence failure with runtime rollback even when disk restoration fails, external-change rejection, and byte preservation of both game saves. Runtime heartbeat errors use one feedback card; checks cover full multiline stack traces, clipboard copying, dismissal, recovery and retained application errors. Screenshots cover error-panel layout and runtime details. Run the GUI harness separately from other GUI tests and use a fresh output directory. Append `--all-languages` for the targeted dictionary check.

`LiveBindingWindowTests` opens the live modeless editor through the main window. Close, Escape, caption close, an unchanged binding and an acknowledged apply must close it and restore the main controls. A rejected apply retains the window and draft; retry accepts the multiplier and closes it. Action details also close when returning to the game. Apply availability checks cover incomplete, missing and unreadable scheme reports, confirmed runtime switches, real edits, reverts, suspension recovery, Mod update labels, stable apply availability and full-width error notices; append `--apply-availability-only` to run those checks separately. The harness runs a console process fixture named `PBZ.exe` and a simulated file responder, preserves both copied game saves, and captures the rejection state and apply button states. Run it separately from other GUI tests with a fresh output directory.

`CliAgentTests` invokes the public executable and parses JSON. It covers the full catalog/schema, key identities, ordinary plus enhanced batch editing, dry runs, ZIP roundtrip, share import/export, device scope filtering, copy, key movement, full reset, explicit scheme selection and invalid batches producing no output. It uses copies and never applies to a game installation. `LiveConfigurationTests` covers stale requests, confirmation, rejection, cancellation, invalid Lua, failed native pipeline preparation, retaining the previous router and suppressing a held press after hot reload. It also verifies serialized game-frame polling with no background Lua worker, idle frames without file access, recovery polling during suspension and acknowledgment retries without applying twice after open/write failures. `LiveEditingTests` additionally accepts a response delayed beyond six seconds and ignores partial acknowledgments. Create `dist/live-edit` before running this Lua harness. Native input checks additionally cover controller device reconstruction after unbinding, retained native remapping/restoration and explicit rejection of insufficient native runtime rows.

Live tests validate the protocol and simulated input behavior; they do not establish actual game compatibility or exclusive-fullscreen overlay behavior.

CLI export checks reject runtime configuration targets for new, ZIP and JSON sources, including custom Mod directories, normalized paths and missing configuration files. Rejection preserves the JSON/Lua pair; ordinary JSON export remains available. Global reset exports are reopened as SAV and ZIP to verify all three schemes' keyboard and controller defaults without changing the source save.

`CliLifecycleTests` covers unattended startup, version/source discovery, UTF-8 stdin and BOM files, invalid-field diagnostics, gameplay Mod enable/disable/conflicts, enhancement lifecycle, local runtime update, ordinary Apply with backups, running-game refusal and partial uninstall reporting. It requires the real game to be closed. For authenticated scheme activation and live acknowledgement/rejection/timeout checks, pass the real game root as a third argument and use a new test directory on that game's volume, for example `python tests\CliLifecycleTests.py dist\PBZKeyForge-cli.exe X:\PBZKeyForge-cli-check X:\Games\PBZ`. It hard-links the native executable and copies both saves; the originals are checked unchanged. A temporary process named PBZ and simulated heartbeat/acknowledgements exercise the CLI protocol. These checks do not establish actual game input behavior, launch a game or stop an existing game process.

`EnhancedUsabilityTests` checks storage and scope after turning enhancement off, explicit inactive override edits, keyboard/controller additions, whole-key movement, live-mode changes with an editor already open, labels and validation text at system DPI, controller-first selection, related action navigation, companion shortcut conflicts and persisted preferences, suspended/disabled/recovered status, and the action viewport at default and minimum window sizes. Status checks cover partial heartbeat writes, expired connections during loading, automatic recovery with an intact draft, and consistent header/footer/button presentation. It captures the editor, linked action details and main window. Append `--status-only` to run only connection checks, or `--all-languages` for its targeted new-string check.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\EnhancedUsabilityTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll tests\EnhancedUsabilityTests.cs
.\dist\EnhancedUsabilityTests.exe dist\enhanced-usability-check
```

`BindingSuppressionTests` checks keyed suppression movement and swaps in ordinary and live drafts, save byte preservation during live editing, and fixed movement source suppression. It generates Lua configurations for keyboard/controller overrides, hold replacement, disabled blocking/nonblocking rules, action-wide suppression and partial output suppression. Run the native input simulation against these generated configurations to verify actual dispatch timing and preserved outputs:

```powershell
& $compiler /nologo /out:dist\BindingSuppressionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\BindingSuppressionTests.cs
.\dist\BindingSuppressionTests.exe dist\binding-suppression-check
$env:PBZ_SUPPRESSION_FIXTURES = (Resolve-Path dist\binding-suppression-check).Path
python tests\input-router\run_lua.py tests\input-router\NativeInputTests.lua
Remove-Item Env:\PBZ_SUPPRESSION_FIXTURES
```

CLI preset checks also cover keyboard-only and controller-only Replace/OnlyPreset operations. The other device's rules, native overrides and keyed suppressions must survive export and reimport.

`BindingReviewTests` checks that a preset's explicit runtime override takes precedence over its ordinary save binding, while other devices still project their save edits. It imports an ordinary-only ZIP, restores factory bindings, checks the visible Undo control and restores the exact imported bytes. Activation checkpoints must retain their original target and never enable a different browsed scheme. The native input harness also checks startup capacity rejection, native restoration, and retry after reducing the requested keys.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingReviewTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll tests\BindingReviewTests.cs
.\dist\BindingReviewTests.exe dist\binding-review-check
python tests\input-router\run_lua.py tests\input-router\NativeInputTests.lua
```

`FeedbackAndUpdateTests` checks the compact notice, single component-help entry, AI prompt clipboard content and draft ZIP, project links and update fallback, release asset selection, staged update versions, file replacement and rollback after a locked CLI. It uses copied executables and synthetic targets; it does not replace a running user installation or publish a release.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\FeedbackAndUpdateTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll tests\FeedbackAndUpdateTests.cs
.\dist\FeedbackAndUpdateTests.exe dist\feedback-update-check
python tests\ApplicationUpdateProcessTests.py dist\PBZKeyForge.exe dist\update-process-check
```

`ApplicationUpdateProcessTests` launches the actual update helper against temporary executable stand-ins. It verifies waiting for the old process, replacing both executables, restarting the new GUI and preserving user presets. It does not install a published release or modify the user's application directory.


`BindingWorkflowTests` checks the complete live editing loop: deferred components, binding summaries and device markers, apply/undo/reapply, rejection and retry, direct-editor field restoration, detail-window persistence across companion hide/show, binding-only factory restoration, concrete close choices and byte preservation of both saves. It uses an inert temporary `PBZ.exe`, synthetic authenticated scheme fixtures and a fake Mod responder; it never starts the game or writes to a real installation. Recording tests feed controller samples and exercise chord/axis editing; physical controller and actual game behavior require hardware validation. GUI checks run sequentially with bounded waits. Screenshots include the compact details, editor, pending summary and close prompt.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingWorkflowTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll tests\BindingWorkflowTests.cs tests\AuthenticatedSchemeFixture.cs
.\dist\BindingWorkflowTests.exe dist\binding-workflow-check
```


Live review regressions preserve explicit preset override precedence after unrelated edits and undo, reject independent raw-record changes without clearing drafts, and reject live sends after an authenticated scheme switch or authentication failure while an editor stays open. Recovery must apply the retained draft; earlier conflicting undo checkpoints must survive a later successful preset application. Bundle import tests reject semantically invalid ability/hook outputs before replacing the current session, configuration or unsaved draft. CLI document imports use the same compilation validation.

## Independent enhancement schemes

```powershell
& $compiler /nologo /out:dist\EnhancementSchemesTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll tests\EnhancementSchemesTests.cs
.\dist\EnhancementSchemesTests.exe dist\enhancement-schemes\checks
python tests\input-router\run_lua.py tests\input-router\SchemeSwitchTests.lua
python tests\CliSchemeTests.py dist\PBZKeyForge-cli.exe dist\cli-schemes
```

`EnhancementSchemesTests` checks complete native key materialization, isolation of ordinary bindings/rules/suppressions, save byte preservation, copying, JSON/ZIP reopen, current-scheme presets and preset overwrite, sparse preset application, and independent mode drafts. Its screenshots cover expanded tabs, overflow access and minimum-window visibility of the selected tab and fixed add/save controls. It writes the compiled `dist/enhancement-schemes/checks/schemes.lua` fixture used by `SchemeSwitchTests`; run the C# harness first and create `dist/enhancement-schemes/runtime/Scripts` for the Lua runtime fixture.

`SchemeSwitchTests` simulates dispatch on different schemes, release of old continuous outputs, movement recovery, suppression of held discrete inputs, switch-key latching, failed-target recovery, actual-scheme persistence/restart and deferred custom-script initialization. `CliSchemeTests` covers offline enable/create/select/edit/rename/delete, ZIP reopen, native save byte preservation, shared switch bindings, single-scheme sharing, status identities and disabled-mode rejection. These checks do not run the game or modify an installation.

Scheme regressions also cover factory restoration of enhanced native bindings, unchanged other schemes/save bytes, deletion of a scheme referenced by the comparison baseline, and bounded sampling while a switch key is held. To validate the generated full factory library against simulated native pipelines in both startup and live modes, run `EnhancementSchemesTests` first, set `$env:PBZ_SCHEME_FIXTURES = (Resolve-Path dist\enhancement-schemes\checks).Path`, run `python tests\input-router\run_lua.py tests\input-router\NativeInputTests.lua`, then `Remove-Item Env:\PBZ_SCHEME_FIXTURES`. In all three compiled factory schemes, the tests retire and restore every configured native action individually, use cooldown/chord pipelines for crouch and keyboard weapon switching, and remove/restore all runtime mappings. Scene actions absent from both active mappings and applied context templates remain dormant until their context returns. The tests also remove and restore interaction mappings without interrupting movement or replaying held interaction keys. Empty overrides for absent devices are accepted; active actions with populated overrides but no device pipeline, and retained mappings with insufficient slots, remain rejected.


`SchemePresetWorkflowTests` uses an isolated installation with synthetic authenticated settings. It exercises preset cancellation before apply, installation failure and retry, cancellation after successful offline/live acceptance, native-draft rejection in the GUI, and byte preservation of both game saves. The game must be closed; the harness never launches it.

```powershell
& $compiler /nologo /out:dist\SchemePresetWorkflowTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemePresetWorkflowTests.cs tests\AuthenticatedSchemeFixture.cs
.\dist\SchemePresetWorkflowTests.exe dist\scheme-preset-workflow
```

Independent scheme checks also reject live planning with ordinary drafts without clearing them and initialize the full library when the first enhanced preset is applied. The CLI harness applies an enhanced share to `new`, adds a scheme in the same batch and reopens its ZIP while verifying native save byte preservation.

`ResourceCapacityResourceTests.py` takes `--game-exe`, `--paks` and `--oodle`. It checks native attribute-set, actor-query, capacity and event signatures against UHT metadata and verifies seven resource families against cooked ability graphs. `CompiledWeaponSpecialMoveTests.lua` also consumes the keyboard/controller ability fixtures from `CatalogInputPipelineTests` (`PBZ_SCHEME_FIXTURES` overrides `dist/catalog-input-pipelines`). It covers equipped same-weapon requests, cooldown, unrelated abilities, activation failure, repeated holds and hook cleanup through the real router and backend. `BindingTuningLifecycleTests` checks ordinary and switch dual-axe payloads against dynamic charge-object maxima while preserving real energy and ordinary event delivery. These are offline checks, not in-game combat verification.

`BindingRestrictionTests` checks independent resource/restriction switches, all six restriction categories, legacy defaults, JSON/share/ZIP roundtrips, keyboard/controller collection, copying and recorded-key editing. Screenshots retain the selected categories after reopening the editor. Compiled fixtures include equipment configuration/slot and armed-state metadata, twelve independent bow bindings in `native-inputs.lua`, every registered native hold preparation on both devices in `native-holds.lua`, and ordinary-activation branch preparation plus external-target controls in `independent-phases.lua`. Append `--fixtures-only` to refresh just these compiled fixtures; the router/backend harness supplies runtime simulation separately.

Weapon resource checks use the production Lua context/resource modules and the native bridge:

```powershell
python tests\input-router\run_lua.py tests\input-router\WeaponResourceTests.lua
cl.exe /nologo /O2 /MD /EHsc /std:c++17 /Fo:dist\WeaponResourceNativeTests.obj /Fe:dist\WeaponResourceNativeTests.exe tests\input-router\WeaponResourceNativeTests.cpp
.\dist\WeaponResourceNativeTests.exe
python tests\WeaponResourceResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
```

`WeaponResourceTests` checks full wind/thunder state for ordinary attacks, the held-state preparation and Tick branches at zero/partial/full energy, exact instance/function ownership, failure, ordinary reuse and cleanup. `WeaponResourceNativeTests` exercises real Lua C exports and direct native virtual calls, including unrelated conditions, source isolation, projectile inheritance after ability completion, reused bullets and hook restoration. `WeaponResourceResourceTests` reads the installed native dispatcher and cost event and checks all 1,112 cooked actions in the wind/thunder family, including positive and negative resource conditions. `ResourceCapacityResourceTests` additionally checks all four current shield fee functions, pure global/Zi Ming effects and native effect-spec/application signatures. These checks do not start the game.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\BindingRestrictionTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll tests\BindingRestrictionTests.cs
.\dist\BindingRestrictionTests.exe dist\binding-restrictions
```

`InputActionProvenanceTests` exercises the actual resource transfer hook with idle/active native wheel sources, runtime key replacement and masking, gesture None/Ongoing/Triggered states, chord and blocker conditions, foreign players, stale observations and mapping restoration. `InputTriggerNativeTests.cpp` exercises the native observer through Lua exports and actual virtual calls; `ScriptContextTests` checks argument forwarding, blocking-value conversion and independent cleanup after failure.

```powershell
python tests/input-router/run_lua.py tests/input-router/InputActionProvenanceTests.lua
python tests/InputTriggerNativeResourceTests.py --game-exe $gameExe --paks $paks --oodle $oodle
```

The resource test resolves UpdateState/GetTriggerType reflection thunks, checks the native evaluator's direct virtual dispatch and IsBlocking slot, and rereads the 20 shipped mapping contexts to verify that all 115 trigger objects use native classes. This is static compatibility evidence, not an in-game input test.
