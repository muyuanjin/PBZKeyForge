---
name: nexusmods-publishing
description: Set up or update Nexus Mods releases through GitHub Actions, configure upload credentials without exposing them to the agent, and maintain mod descriptions, sticky posts and media. Use for N网/Nexus mod publication, file IDs, upload Actions, release updates and recovery.
---

# Nexus Mods publication

Deliver the requested public version and its matching pages. Keep repository-specific game checks, media counts and copy requirements in that repository's publishing procedure; they are not universal Nexus rules. Use `gh` for GitHub and file publication, and the available `browser-skill` for the logged-in Nexus page. Read that browser skill before operating it.

Determine whether this is first-time setup, a normal update, or recovery. Reuse existing file entries, secrets and workflows. A public download version is the compatibility baseline; a local commit, pushed tag or draft alone is not a published version. A request to create tooling does not authorize a new live release or replacing an existing secret.

- **First mod, file ID, API key or workflow:** read [setup](references/setup.md). The reusable [workflow template](assets/publish-nexusmods.yml) publishes a new version of an existing file entry. The [Windows secret helper](scripts/set-github-secret.ps1) lets the user paste the key locally into a masked field and sends it directly to `gh` stdin; the agent never reads the value.
- **Routine release or interrupted update:** read [updates and recovery](references/updates.md). Check the public Files tab before dispatching an upload. Each successful upload creates a new version; Actions success alone is not the final public verification.
- **Description, sticky Posts, images or Windows picker:** read [browser operations](references/browser.md). Replace body references before deleting old images; verify gallery order and thumbnail on the public page.

When the current repository is **PBZKeyForge**, read `docs/publishing.md` from its root for the fast commands. Its `build/publish-nexus.py finish` runs the prepared release's file uploads and page/media updates; `verify` performs read-only public checks. `build/nexus-publishing.json` owns the IDs. Do not reuse PBZ IDs or its 8+1 gallery convention for another mod.

Keep credentials out of chat, tool arguments, environment dumps, screenshots, files and commits. Do not use browser DOM reads, network capture, clipboard reads or screenshots to extract an API key. Verify GitHub secret **names and timestamps**, never values. Stop a pending or uncertain mutation and inspect its result before deciding what remains; never repeat it merely because a tool timed out.

Only publish, edit the existing sticky, delete old media or replace a secret when the task authorizes that action. Existing authorization carries through the workflow; do not ask again at each stage. If login or local key entry requires the person, ask only for that missing step and continue independent preparation.
