# Nexus Mods page fields

Copy-paste values for the mod page. Everything here is English only, matching the site.

## Mod page

| Field | Value |
| --- | --- |
| Game | Phantom Blade Zero |
| Name | `PBZKeyForge` |
| Summary | `One key can drive several actions. Edits Phantom Blade Zero's own key binding file, so the bindings the in-game screen rejects actually work.` |
| Category | `Utilities` (fallback: `Gameplay`) |
| Description | paste the contents of `docs/nexus-description.md` into the rich text editor |

Suggested tags: `Utilities`, `Control`, `Configuration`, `No DLL`, `English`.

## File entries

Create two entries on the Files tab, both attached to the same mod page.

| Entry | Category | Name | Version | Upload |
| --- | --- | --- | --- | --- |
| Main package | Main files | `PBZKeyForge 1.0.0` | `1.0.0` | `PBZKeyForge-v1.0.0.zip` |
| Save files only | Optional files | `PBZKeyForge 1.0.0 - preset save files only` | `1.0.0` | `PBZKeyForge-v1.0.0-presets-only.zip` |

File descriptions:

- Main: `GUI and CLI, seven preset save files included. Extract anywhere, run PBZKeyForge.exe.`
- Optional: `Only the ready-made EnhancedInputUserSettings.sav presets. No executable. Replace your file with one of these and pick that scheme in game.`

Do **not** upload the `.sav` files as loose attachments; keep the zips so the folder structure survives.

## After the page exists

Note the numeric file ID of each entry (`...&file_id=1234` in the Files tab URL), then:

```powershell
gh secret set NEXUSMODS_API_KEY                 # paste the API key when prompted
gh variable set NEXUSMODS_FILE_ID --body "<main file id>"

gh workflow run publish-nexusmods.yml -f tag=v1.0.0 -f category=main -f archive_existing_version=false
gh workflow run publish-nexusmods.yml -f tag=v1.0.0 -f file_id=<optional file id> `
  -f asset_name=PBZKeyForge-v1.0.0-presets-only.zip -f category=optional `
  -f display_name="PBZKeyForge 1.0.0 - preset save files only"
```

The second run overrides `file_id` and `asset_name`; everything else defaults.
