# Repository Guidelines

For game input mappings, default bindings, save formats, or scheme activation, read [the project Mod development skill](skills/phantom-blade-zero-save-modding/SKILL.md) and its relevant references before changing the model or collecting game evidence.

## Game and UE4SS Updates

Before compatibility, loader, build or release work, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\check-runtime-updates.ps1
```

Pass `-GameDirectory <game-root>` when the task names an installation. Successful observations can be reused for 24 hours; a changed local Steam build forces a new query. Add `-Force` when the user reports a game update, a loader failure after an update, or an upstream fix. Release work uses `-Release`, which always queries both UE4SS and the public Steam build and rejects unresolved changes or failed queries.

When `NeedsReview` is true, continue the adaptation work: read the release notes, obtain the candidate, run the relevant checks and document compatibility evidence. Follow [the update procedure](skills/phantom-blade-zero-save-modding/references/runtime-updates.md). An update notification alone does not complete that work. Keep game execution and external publishing within the current task's authorization. A network failure permits local development with the existing pin, but does not establish current compatibility or permit publication.

## Project Structure & Module Organization

- `src/`: C# application code. `MainForm.cs` owns WinForms UI; `Cli.cs` provides console commands; `Gvas.cs` reads/writes input saves; `GameSchemeSettings.cs` handles the game's actual scheme selector. `Presets.cs` and `I18n.cs` define presets and multilingual text.
- `tests/`: standalone integration checks against the built application.
- `build/`: compiler script and icon generation. `assets/` contains icons, banners, and screenshots.
- `samples/vanilla-base.sav`: baseline fixture. `dist/` and generated `presets/` are ignored outputs.
- `docs/` and `.github/workflows/`: publishing documentation and release automation.

## Build, Test, and Development Commands

Run from the repository root on Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
.\dist\PBZKeyForge.exe
.\dist\PBZKeyForge-cli.exe --selftest samples\vanilla-base.sav
.\dist\PBZKeyForge-cli.exe --shot samples\vanilla-base.sav dist\preview.png
```

These commands build both executables, launch the GUI, verify byte-identical GVAS serialization, and capture the UI. Building uses Windows' .NET Framework compiler; no SDK or NuGet restore is required. Keep `powershell.exe` available for Windows PowerShell 5.1 compatibility.

## Coding Style & Naming Conventions

Use four-space indentation, braces on separate lines, the `PBZKeyForge` namespace, PascalCase types/methods, and camelCase fields/locals. Keep syntax compatible with the bundled .NET Framework compiler. Add user-facing strings to all 15 supported languages in `I18n.cs` and its partial files; preserve official game action names. No formatter or linter is configured.

## Testing Guidelines

Routine checks use one language (Simplified Chinese). Run all 15 languages only for a targeted localization test: append `--all-languages` to the relevant integration harness, or invoke the dedicated `I18nTranslationTests` for dictionary checks. Do not include either sweep in routine verification.

The release workflow requires `roundtrip=IDENTICAL`. For scheme changes, compile and run the standalone assertion harness; set the path variables first:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe dist\PBZKeyForge.exe $gameExe $sharedSave $inputSave $newTestDir
```

Use a new test directory on the game's volume; the harness creates executable hard links and edits copied saves. Name test classes `*Tests`. No percentage coverage threshold is configured. Check GUI changes with screenshots; distinguish fixture verification from actual game behavior.

## Save-File Safety

Scheme selection belongs to `PBZSharedSetting.sav`, not the profile identifier in `EnhancedInputUserSettings.sav`. Preserve unrelated settings, reject unsupported or unauthenticated data, require the game to be closed, back up outside the game directory, and verify replacements before writing. Never commit credentials or game encryption keys.

## Commit & Pull Request Guidelines

History uses short subjects prefixed with `feat:`, `fix:`, `docs:`, `ci:`, or `assets:`. Describe concrete behavior and validation in PRs, link relevant issues, and attach screenshots for UI changes. Preserve existing uncommitted work. Commit, tag, push, and publish only when explicitly authorized.
