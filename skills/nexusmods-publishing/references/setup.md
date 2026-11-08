# First-time setup

## Mod page and file entry

In the user's logged-in Nexus browser, select the correct game and create the mod page with its name, category, summary, installation instructions and initial archive. Inspect the upload form's real required fields; do not invent placeholder metadata just to advance it. A mod page and at least one uploaded **file entry** are prerequisites for the upload Action. Create separate entries for packages that users choose separately, such as a full tool and presets-only archive.

The v3 upload Action needs the stable **file ID**, not the mod-page ID or a revision's old download URL ID. On the public **Files** tab, turn on **Advanced** to reveal IDs. Alternatively open the file's edit menu under **Manage → Files**. Record the displayed file ID for each entry and its category in the repository's publishing configuration. Public `?file_id=...` download links and HTML `data-id` revision numbers can differ from the upload API's ID; do not substitute them.

Source: [Nexus-Mods/upload-action — finding IDs](https://github.com/Nexus-Mods/upload-action#how-to-find-the-file-id--mod-id). If the UI changes, inspect that documentation and the current Files controls before changing the mapping.

## API key without exposing its value

The uploader's key link is [Nexus API keys](https://www.nexusmods.com/settings/api-keys). Have the user obtain their personal API key there. Do not read or capture that page's key fields. Do not ask the user to paste a key in the conversation.

First inspect the intended GitHub repository and existing secret **metadata**:

```powershell
gh secret list --repo owner/repository --json name,updatedAt
```

An existing usable `NEXUSMODS_API_KEY` needs no replacement. For a newly requested setup or authorized rotation, launch:

```powershell
& <skill-directory>\scripts\set-github-secret.ps1 -Repository owner/repository
```

The helper shows a local masked field. The user pastes the key and chooses **Save secret**. It pipes the value to `gh secret set NEXUSMODS_API_KEY --repo owner/repository` using redirected stdin; it creates no credential file, command-line argument or environment variable containing the value. The helper clears the field afterwards and outputs only success/cancellation. Do not inspect the form's value through UI Automation or attach a debugger. After completion, use `gh secret list` to verify the name. User cancellation leaves the existing repository secret unchanged.

On another OS, the person can run `gh secret set NEXUSMODS_API_KEY --repo owner/repository` in their own interactive terminal and paste at its hidden prompt. Do not run the command with the secret as `--body`, shell interpolation or a recorded agent input.

Store nonsecret IDs as repository variables or project configuration:

```powershell
gh variable set NEXUSMODS_FILE_ID --repo owner/repository --body <stable-file-id>
```

## GitHub upload Action

Start from [assets/publish-nexusmods.yml](../assets/publish-nexusmods.yml), copy it to `.github/workflows/publish-nexusmods.yml`, and adapt package names, version notes and mod-manager settings to the project. It downloads an already public GitHub release asset and uploads it through `Nexus-Mods/upload-action@v1.0.0-beta.8`, the version verified by the PBZ workflow. Check upstream when upgrading the Action. Put the key only in `${{ secrets.NEXUSMODS_API_KEY }}`.

Keep `file_id` explicit when publishing several packages. Decide whether Vortex can actually install the package: the upstream Action defaults mod-manager downloads to **true**, so explicitly set this input for standalone tools, save files and manual installers. Keep previous versions available unless they are broken. Use `archive_existing_version` only for that intentional choice.

Commit the workflow and release notes, inspect outgoing content for privacy, and push within the authorized scope. GitHub must have the dispatch workflow on the default branch. In PBZ, the workflow checks out the release tag for notes; edits on main alone do not alter the file description uploaded from an older tag. Use the workflow's explicit description input for a permitted correction; inspect existing versions first because an upload creates another version.

Dispatch only after the GitHub release and intended archive are public:

```powershell
gh workflow run publish-nexusmods.yml --repo owner/repository `
  -f tag=v1.2.3 -f version=1.2.3 -f asset_name=MyMod-v1.2.3.zip `
  -f file_id=<stable-file-id> -f category=main `
  -f description_file=docs/releases/1.2.3.nexus.txt
```

Watch the exact resulting run. The template's display title includes tag and category to make recovery unambiguous. Verify the uploaded version, package and description on the public Nexus Files tab before moving to page updates. A failed run after the upload step may still have published a file; inspect before retrying.
