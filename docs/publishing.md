# Publishing PBZKeyForge

The reusable [Nexus publishing skill](../skills/nexusmods-publishing/SKILL.md) covers initial mod/file creation, stable v3 file IDs, masked secret entry, Action setup and browser recovery. This procedure owns PBZ commands, artifacts and completion. Read the skill before Nexus work and browser-skill before browser operations.

## Prepared release fast path

Python uses only its standard library. Windows PowerShell 5.1, the existing build tools, authenticated `gh`, and connected browser-skill are required. `build/nexus-publishing.json` owns the repository, Nexus URLs, sticky ID, shared banner and stable upload file IDs. Do not copy revision IDs from download URLs into it.

Set the candidate version and the previous version actually published to users:

```powershell
$version = '1.3.2'
$previous = '1.3.1'
```

1. Run `build/check-runtime-updates.ps1` before build work; its successful cached observation can be reused under the project rules. Complete feature work and its relevant tests. Write `docs/releases/<version>.md`, `<version>.nexus.txt` and `<version>.presets-only.nexus.txt`, update the application version and README, then build a stable candidate. Nexus descriptions must exceed 225 characters. The upload workflow reads notes from the release tag. Its explicit `description` input is available for a permitted correction.
2. Check current game/loader compatibility with `build/check-runtime-updates.ps1 -Release`. `NeedsReview` or a failed query requires the adaptation procedure; it is not current compatibility evidence. If the running editor locks the default output, build with `-OutputDir dist/release-<version>/app`; preserve the user's running game/editor.
3. Capture exactly the needed views and prepare a resumable media manifest:

```powershell
& .\build\prepare-release-media.ps1 -Version $version -PreviousVersion $previous `
  -ApplicationPath "dist/release-$version/app/PBZKeyForge.exe"
```

This captures eight full enhancement views and five English feature illustrations. The executable's actual version must match. Inspect readability, languages and privacy, then update `docs/nexus-description.bbcode.txt`, `docs/nexus-short-description.txt` and the existing changelog in `docs/nexus-pinned-post.bbcode.txt`. The body keeps new-user order: value, installation, ordinary editing and presets, enhancement and removal. Keep at most eight illustrations; the current body uses six. Leave the previous image URLs in the body until the publisher replaces them. The new manifest is `docs/nexus-media-<version>.json`; it starts with empty URLs and must not be recreated over upload checkpoints.

4. Commit only the authorized release files. Inspect every outgoing commit/file version and packaged artifacts before pushing:

```powershell
python build/check-release-privacy.py --base origin/main `
  --artifact "dist/release-$version/app/PBZKeyForge.exe" `
  --artifact "dist/release-$version/app/PBZKeyForge-cli.exe" `
  --artifact "dist/release-$version/app/key-enhancement-runtime.zip" `
  --report "dist/publishing/$version/privacy.json"
```

The scanner includes nested ZIP entries and reports locations/kinds without matched secret values. Resolve findings and visually inspect screenshots; no text scanner proves image privacy. Never publish a secret present in an earlier outgoing commit even if a later commit removes it. Once clear and authorized:

```powershell
git push origin main
git tag -a "v$version" -m "PBZKeyForge $version"
git push origin "v$version"
```

5. Monitor the exact GitHub Release run. It builds both archives and requires `roundtrip=IDENTICAL`, preset/package checks and current runtime compatibility. Do not dispatch it again because a build is still running. Confirm a public final release with both nonempty assets:

```powershell
gh release view "v$version" --repo muyuanjin/PBZKeyForge --json url,isDraft,isPrerelease,assets
```

6. Read browser-skill, run its daemon/status/browser discovery and select the intended logged-in instance. Run the prepared release's file publication and complete Nexus update with one command:

```powershell
python build/publish-nexus.py finish --version $version --browser <instance-id-or-verified-label>
```

`finish` rechecks public file categories, publishes missing main/optional packages through the Action, waits for their exact runs and public versions, uploads only missing screenshots through the Windows picker, assigns titles/visibility/thumbnail, replaces body URLs, saves General and the original sticky, removes mapped previous images, saves gallery order, then checks all public tabs. It creates and closes its own browser-skill session. It never reads the Nexus key. The script is the entrypoint after the GitHub release is public; it does not write release notes, judge screenshot content, commit, tag or push.

After success, inspect and commit the updated media JSON, generated mapping Markdown and description URLs. Check that follow-up commit's outgoing content before any authorized push. This source-only follow-up leaves the published application tag intact.

## Resume and verify

Stages can run separately with the same `--version` and `--browser`: `files`, `media`, `pages`, `cleanup`, `verify`. `verify` is read-only and also checks GitHub. Its report is `dist/publishing/<version>/verified.json`; page evidence and dispatch checkpoints stay under ignored `dist/publishing/`.

```powershell
python build/publish-nexus.py verify --version 1.3.1 --browser <instance-id-or-verified-label>
python tests/PublishingTests.py
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/PublishingWindowsTests.ps1
```

Re-running `finish` reuses public files and image URLs. It never automatically reruns a failed or uncertain upload. For a dispatch without a returned run ID, find its `Nexus v<version> / <category>` run and inspect Files. An upload that already reached Nexus must be reused even if a later workflow step failed. The old upload Action may have a generic run title; the public Files tab remains authoritative.

`pending_upload` records one source key and the complete pre-upload URL set before clicking Add image(s). On resume, one new URL is adopted and saved before title work. If zero or several candidates remain, inspect the picker and Media; assign the verified URL to that item's `url` and remove `pending_upload` only after resolving its external effect. Never clear it just to force another upload. If no upload occurred, confirm that on the website before clearing the record. Unknown gallery images are not automatically deleted.

The publisher replaces only the previous manifest's mapped images. It first verifies that the public body no longer references them. The shared banner remains. Missing buttons, changed markup, lost control and uncertain writes stop the current stage and close the owned session; follow the skill's recovery guidance and resume the unfinished stage after inspecting the result. A script exit alone is not a completed release.

## Media and picker

Gallery order is English PC, Xbox, PlayStation, action view; Simplified Chinese in the same order; the shared banner last. English PC is the thumbnail. All eight are complete application views. Five body illustrations stay under Hidden images; the sixth body image reuses English PC. Upload only images needed by the gallery or body.

The media JSON owns local source, title, URL and visibility. `docs/nexus-media-<version>.md` is generated from it. The public verification checks exact order, thumbnail metadata, all embedded URLs and nonzero loaded dimensions, current main/optional file versions and the existing sticky content/status.

For an individual native picker operation after Add image(s):

```powershell
& .\build\select-upload-files.ps1 -Files @(
  'assets/screenshots/1.3.1/enhancement-main-en.png',
  'assets/screenshots/1.3.1/enhancement-main-zh-Hans.png'
)
```

It waits for the Open/打开/開啟 child dialog owned by the exact Chrome window, fills quoted full paths, verifies WM_GETTEXT and clicks Open. Use `-WindowTitle` for another owner title. Keep this page open; do not navigate or click Add image(s) again while its picker is pending. After it closes, inspect actual upload previews. `prepare-release-media.ps1` and the publisher avoid asynchronous multi-upload URL ambiguity by uploading and checkpointing one image at a time.

## Setup and file descriptions

Current stable upload IDs are main `8297694` and optional `8297925`. The sticky ID is `179027229`. Initial credential setup and Action installation belong to the [setup reference](../skills/nexusmods-publishing/references/setup.md); do not expose the API key to the agent.

Nexus files and their descriptions use `.github/workflows/publish-nexusmods.yml` only. Each entry is uploaded once, preserves previous versions and explicitly disables Vortex downloads: these packages are manual installs. The presets-only archive contains eight factory-based saves, `PRESETS.md` and README, with no executable or gameplay mod. Users copy one save and select Custom 1. The main archive includes GUI/CLI, documentation, presets and the bundled enhancement runtime.

A description correction uses the permitted CLI/Action route. Re-running an upload creates a new version; inspect existing uploads before choosing it. Do not use the web file form to overwrite a long Action-managed description. The homepage and existing sticky use browser-skill separately.
