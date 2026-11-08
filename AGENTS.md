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

## Release Workflow and Compatibility Baseline

For Nexus setup, file IDs, credentials, release updates or recovery, read [the Nexus publishing skill](skills/nexusmods-publishing/SKILL.md). Follow [the project publishing procedure](docs/publishing.md) and use its maintained scripts. After the tested GitHub release is public, `build/publish-nexus.py finish --version <version> --browser <instance>` publishes missing files and completes the Nexus pages/media; `verify` performs read-only public verification. Do not recreate per-session upload/title/delete scripts or reuse old browser session IDs. Preserve the versioned media manifest and pending-effect checkpoints when resuming.

For an authorized full release, commit the release changes, inspect everything about to be pushed for privacy, push only after it is clear, then publish the new version on both GitHub and Nexus Mods. `build/check-release-privacy.py` inspects every outgoing file version and selected packaged assets; visually inspect screenshots as well. Inspect for credentials, game encryption keys, personal saves, local paths and other private information. If private content is found, remove it from the outgoing content before pushing; never reproduce secrets in reports.

Only a version actually published to users counts as an old version. Uncommitted work, local commits, pushed commits, tags and release drafts without a public release do not establish a historical compatibility requirement. Base compatibility decisions and changelogs on actual public releases; revise unpublished formats and implementations directly instead of adding legacy handling for them. Preserve compatibility required by real published versions.

Use the CLI/GitHub Actions path for Nexus file publication and file descriptions, through `.github/workflows/publish-nexusmods.yml` and `docs/releases/<version>*.nexus.txt`. Keep file descriptions on that path when correcting them as well. Inspect the existing file version before retrying: rerunning the upload action creates another version.

After the Nexus file is published, use `$browser-skill` to update the mod homepage description, the existing Posts sticky changelog, media, gallery order and thumbnail. Read the skill's upload guidance before uploading images. Image uploads open a native Windows file picker; handle it with Windows UI automation as part of the upload. Browser automation alone cannot complete that dialog.

A release is complete only when both platforms expose the intended version and downloads, and the public Nexus Description, Files, Posts and Images tabs show the corresponding text and media. A successful push or upload workflow alone does not complete the release.

## Nexus Copy and Media

Maintain the homepage in `docs/nexus-description.bbcode.txt`, its summary in `docs/nexus-short-description.txt`, the sticky post in `docs/nexus-pinned-post.bbcode.txt`, and file descriptions in `docs/releases/`. `docs/nexus-media-<version>.json` owns image mappings and upload checkpoints; the publisher generates the matching Markdown mapping.

Write the homepage, file descriptions and sticky announcement from the player's perspective. Open with the value the mod brings to the user, then explain the relevant behavior and use. Derive each release's emphasis from the actual changes since the previous public version. Describe the actual shipped improvements rather than implementation activity.

Organize the homepage around a new user's complete usage sequence: understand the benefits, install and get started, edit bindings and use presets, then explore enhancement features. Place feature illustrations beside their explanations. Integrate new features into that sequence rather than prepending each update; repair an existing update-first structure when editing the page. Keep the sticky changelog consistent with the style and approximate length of neighboring release entries, covering changes since the previous public version.

The visible homepage gallery must contain exactly eight complete application screenshots plus one banner. Use four English images followed by four Simplified Chinese images; within each language, order them as PC key view, Xbox key view, PlayStation key view, then action view. Put the banner last and use the English PC key view as the thumbnail. Show the current version's enhancement mode and its extra actions in the English and Chinese screenshots. These must be full application views, not isolated dialogs.

The homepage body uses at most eight images to explain the software's features. Its illustrations need not match the visible gallery: reuse gallery images where helpful and add only the feature illustrations the text needs. Keep any dialog screenshots referenced by the body under Hidden images, outside the main visible gallery. Upload only images needed for the gallery or body. Replace old image references with current ones and delete outdated version images. Verify the public gallery order, visibility, thumbnail and embedded images after saving.
