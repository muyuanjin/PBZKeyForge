# Publishing checklist

Two channels ship the same build: a GitHub Release and a Nexus Mods file page.

Live page: https://www.nexusmods.com/phantombladezero/mods/402

## 1. Cut a release

```powershell
git tag -a v1.0.0 -m "PBZKeyForge 1.0.0"
git push origin v1.0.0
```

`.github/workflows/release.yml` runs on the tag and publishes:

| Asset | Contents |
| --- | --- |
| `PBZKeyForge-<tag>.zip` | `PBZKeyForge.exe`, `presets\*.sav`, `README.md`, `LICENSE` |
| `PBZKeyForge-<tag>-presets-only.zip` | only the generated `.sav` files, for players who do not want to run an executable |

`PBZKeyForge-cli.exe` is built in the same pipeline but is not shipped to players: the workflow uses it to generate the presets and to run the round-trip self test.

The workflow fails if `--selftest` does not report `roundtrip=IDENTICAL` on the shipped base file.

## 2. Nexus Mods setup (once)

1. Create the mod page under the Phantom Blade Zero game entry, category **Tools**.
2. Create at least one file entry; its numeric ID is what the upload action needs.
3. Store the credentials on the repository:

```powershell
gh secret set NEXUSMODS_API_KEY          # paste the personal API key when prompted
gh variable set NEXUSMODS_FILE_ID --body "<file id>"
```

The page body lives in `docs/nexus-description.md` (BBCode, English only, images uploaded separately).

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


## Verified Nexus wiring

- Mod page: https://www.nexusmods.com/phantombladezero/mods/402 (mod id 402)
- File entry: File ID `8297694` (Advanced panel on the Files tab), stored as the `NEXUSMODS_FILE_ID` repository variable.
- Upload goes through `publish-nexusmods.yml`; the API accepts a long file description, unlike the 255 character limit on the manual upload form.

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
