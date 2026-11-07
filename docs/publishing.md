# Publishing checklist

Two channels ship the same build: a GitHub Release and a Nexus Mods file page.

Live page: https://www.nexusmods.com/phantombladezero/mods/402

## 1. Cut a release

Write `docs/releases/<version>.md` for GitHub, `<version>.nexus.txt` for the main Nexus download and `<version>.presets-only.nexus.txt` for the preset archive before tagging. All describe the complete difference from the previous public version. GitHub notes include implementation and validation details; Nexus file descriptions explain player-visible behavior and use for the selected package. Each Nexus description must exceed 225 characters. The release workflow uses the checked-in notes rather than generating a commit list; the Nexus workflow reads its description from the release tag unless an explicit description is provided.

```powershell
git tag -a v1.0.0 -m "PBZKeyForge 1.0.0"
git push origin v1.0.0
```

`.github/workflows/release.yml` runs on the tag and publishes:

| Asset | Contents |
| --- | --- |
| `PBZKeyForge-<tag>.zip` | GUI and CLI executables, `docs\`, `presets\*.sav`, `presets\PRESETS.md`, `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md` |
| `PBZKeyForge-<tag>-presets-only.zip` | generated `.sav` files, `PRESETS.md` and `README.md`, without an executable |

`PBZKeyForge-cli.exe` ships with the editor and also generates the presets and runs the round-trip self test in the workflow.

The workflow fails if `--selftest` does not report `roundtrip=IDENTICAL` on the shipped base file. It also verifies the PBZ2 Protobuf share format, frozen factory baseline, and device scopes with `PresetShareTests`. protobuf-net is embedded in each executable; no separate dependency DLL is shipped. `THIRD-PARTY-NOTICES.md` includes its Apache-2.0 license.

`--gen-presets` generates eight saves from `Presets.All()`: seven custom layouts and one factory layout (`vanilla`). The selected-scheme restore (`vanilla_current`) remains an editor operation and is omitted from the package. Generation removes its old output file when reusing a directory. The saves use the same factory-based preset transformation as the editor. The generated bilingual `PRESETS.md` takes names and descriptions from the application's translations and identifies the target scheme. The shipped base targets Custom 1; players must select it in game after copying a file. The preset package test compares every file with the hover preview, Replace and Preset only modes on the factory base, and saved/reopened editor output. It also checks generation from a customized input against Preset only mode and regeneration over the old file set. Any mismatch stops packaging.

## 2. Nexus Mods setup (once)

1. Create the mod page under the Phantom Blade Zero game entry, category **Tools**.
2. Create at least one file entry; its numeric ID is what the upload action needs.
3. Store the credentials on the repository:

```powershell
gh secret set NEXUSMODS_API_KEY          # paste the personal API key when prompted
gh variable set NEXUSMODS_FILE_ID --body "<file id>"
```

The page description source is `docs/nexus-description.bbcode.txt` (English; images uploaded separately).

## 3. Upload a build

The workflow is manual and takes an existing GitHub Release asset:

```powershell
gh workflow run publish-nexusmods.yml -f tag=v1.0.2 -f asset_name=PBZKeyForge-v1.0.2.zip -f version=1.0.2 -f category=main -f archive_existing_version=false
```

Run it once per file entry, for example `category=main` for the full package and
`category=optional` for the presets-only archive, passing `-f file_id=<id>` when the
second entry differs from `NEXUSMODS_FILE_ID`.

## 4. After publishing

- Confirm the file version appears on the mod page.
- Keep the previous version downloadable unless it is broken; only then set
  `archive_existing_version=true`.
- Add the release notes from the GitHub Release to the page's changelog section.
- Update the existing **Posts** sticky announcement from `docs/nexus-pinned-post.bbcode.txt`: list the supported game builds and put the new release above the historical changelog entries. Verify the public Posts tab separately from the Description and Files tabs.

## Versioned screenshots and page description

For every public version, build or select that version's executable and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\capture-screenshots.ps1
```

The script produces 20 images and a linked `README.md` index in `assets/screenshots/<version>/`, using the executable's version number. Each scene has `-en.png` and `-zh-Hans.png` variants:

| Scene | Screenshot | Description section |
| --- | --- | --- |
| `main` | Keyboard/mouse, preset dropdown expanded | Gallery |
| `main-gamepad` | Xbox, preset dropdown expanded | Gallery |
| `main-playstation` | PlayStation, preset dropdown expanded | Gallery |
| `bindings` | Keyboard/mouse actions and whole-scheme changes | See and edit your bindings |
| `bindings-gamepad` | Controller actions and whole-scheme changes | See and edit your bindings |
| `actions` | Action view with binding slots | See and edit your bindings |
| `binding-edit` | Binding editor with Default and Unbound choices | See and edit your bindings |
| `presets-save` | Name field and Save as preset | Your presets and share codes |
| `presets-share` | Saved preset and sharing device dropdown | Your presets and share codes |
| `presets-import` | Share-code preview, device scope and apply choices | Your presets and share codes / Choose how a preset changes your keys |
| `enhancement-main` | Independent enhancement schemes and key bindings | Gallery |
| `enhancement-actions` | Phantom Edge action catalog | Gallery |
| `enhancement-moves` | Complete move selection | Gallery |
| `enhancement-tuning` | Per-binding resources, damage and timing | Gallery |

It opens an offline scheme by default, previews `mouse_guard` for keyboard/mouse and `souls` for controllers without applying them, and selects the editor's Xbox or PlayStation family directly. No game installation, save or connected controller is required. Saving demonstrations use an isolated temporary preset library, removed afterwards; the user's library and clipboard are untouched. With `-SavePath`, it reads the specified save without writing it and omits personal names and local paths. Run on an unlocked Windows desktop with enough room for the editor.

To capture a different build or input:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\capture-screenshots.ps1 `
  -ApplicationPath dist\PBZKeyForge.exe -SavePath samples\vanilla-base.sav `
  -Preset mouse_guard -OutputDir assets\screenshots\1.2.0
```

Use `-ControllerPreset <id>` to select a different controller preview. Use `-Shots main,main-gamepad,main-playstation` for only the six main screenshots, or select individual scene names to recapture a feature. `-Languages en` limits a capture to English.

Version 1.3.0 uses `-Shots enhancement-main,enhancement-actions,enhancement-moves,enhancement-tuning`: eight gallery images in English and Simplified Chinese. These scenes enable enhancement only in memory. Capture preferences disable animation and companion hotkeys; game files and personal preferences remain untouched. Use the English enhancement main view as the thumbnail, retain only eight current visible images, and embed three matching English feature images in the description. Hide outdated gallery images; preserve any still referenced in descriptions or posts.

Inspect the PNGs for readable text, correct language and absence of local information. The six main screenshots must show the preset list open; the other scenes show the feature being described. Upload them to the mod's **Manage → Media → Add image(s)** gallery with titles `PBZKeyForge <version> — <language> — <scene>`. Save the media changes and verify the images on the public Images tab. Place the six main screenshots first: English keyboard/mouse, Xbox, PlayStation, then Simplified Chinese keyboard/mouse, Xbox, PlayStation. Set the English keyboard/mouse screenshot as the thumbnail. Keep the banner after these six. Hide outdated UI images from the gallery without deleting them.

Keep installation, the in-game binding warning and preset apply-mode explanations ahead of feature illustrations. The description uses three images: editing by key, saving a personal preset, and sharing with a device scope. Do not repeat the six gallery screenshots in the body or embed the entire screenshot collection. Use each image's full-size public `staticdelivery.nexusmods.com` URL in `[center][img]URL[/img][/center]` below its matching explanation. Other feature images stay under **Hidden images**, retaining their public URLs for later use. Record the gallery order, thumbnail and local-file-to-media mapping in `docs/nexus-media-<version>.md`. Confirm each embedded image shows the described feature and version before saving the page.

Read the live Description tab before editing the homepage. Update `docs/nexus-description.bbcode.txt` to reflect current player-visible features and instructions, then paste it through **Manage → Mod details → Full description → View source** and save. Update the homepage version and short description along with it. Point the English and Chinese README sections to the matching versioned screenshots. File-upload release notes are maintained separately in `docs/releases/`.


## Verified Nexus wiring

- Mod page: https://www.nexusmods.com/phantombladezero/mods/402 (mod id 402)
- File entry: File ID `8297694` (Advanced panel on the Files tab), stored as the `NEXUSMODS_FILE_ID` repository variable.
- Upload goes through `publish-nexusmods.yml`; the API accepts a long file description, without the short-description limit on the manual upload form.

```powershell
gh variable set NEXUSMODS_FILE_ID --body 8297694 --repo muyuanjin/PBZKeyForge
gh secret set NEXUSMODS_API_KEY --repo muyuanjin/PBZKeyForge   # paste the key from Account settings -> API Access

gh workflow run publish-nexusmods.yml --repo muyuanjin/PBZKeyForge `
  -f tag=v1.0.2 -f asset_name=PBZKeyForge-v1.0.2.zip -f version=1.0.2 `
  -f category=main -f archive_existing_version=false -f "description=<long text>"
```


## Second file entry: presets only

- `PBZKeyForge V1.0.2 Presets Only` - File ID `8297925`. Category **Optional** so it sits under Main files and the editor stays first.
- Mod manager download stays disabled for this entry: Vortex cannot install a save file.
- Publishing another version of it:

```powershell
gh workflow run publish-nexusmods.yml --repo muyuanjin/PBZKeyForge `
  -f tag=v1.0.3 -f asset_name=PBZKeyForge-v1.0.3-presets-only.zip -f file_id=8297925 `
  -f version=1.0.3 -f category=optional -f archive_existing_version=false `
  -f "description=<preset list>"
```

## Correcting a published file description

The file form limits description length; the API supports the full version notes. Re-running the publish workflow creates another file version, so inspect existing versions before retrying and remove an obsolete duplicate only after confirming the new upload succeeded.

## Mod manager download

`publish-nexusmods.yml` passes `allow_mod_manager_download: false` on every upload. The action defaults it to `true`, so an API upload silently re-enables the Vortex button even after it was turned off in the web UI, and turning it off again in the UI means opening the file form, whose description field has a short length limit.
