# Updates and recovery

## Normal update

Read the repository's release entrypoint and inspect its existing changes. Select the previous **public** version on GitHub and Nexus; write the complete user-visible difference from it. Lead summaries and file descriptions with what the player gains. Keep the mod homepage in a new user's usage order, integrating new features where they belong. Keep the existing sticky changelog's style and approximate entry length.

Prepare a stable tested candidate, versioned screenshots and notes through the repository's own build and capture commands. Inspect screenshot content, language and local information. Commit the authorized release files; inspect **all outgoing commits and file versions**, screenshots and packaged binaries/archives for privacy before pushing. A clean final diff cannot remove a secret from an earlier outgoing commit. Do not print a matched credential when reporting a finding.

Publish GitHub through the existing release workflow, then inspect its public asset names and sizes. Publish Nexus **files and file descriptions through CLI/Actions**, using the existing file entry per package. Before each dispatch, inspect the public Files version and existing run. After both downloads are public, update the homepage, the original sticky post and current media through browser-skill. Verify public Description, Files, Posts and Images separately.

For PBZ, run the prepared release's `build/publish-nexus.py finish --version <version> --browser <instance>`; the project procedure explains preparation and recovery. It creates its own task session and closes it, and writes public verification to ignored `dist/publishing/<version>/verified.json`. A missing report or failed stage is not completion.

## Decide from the actual remaining effect

| Observed state | Next action |
| --- | --- |
| Correct package/version already public | Skip upload; continue unfinished page/media work. |
| Exact upload run queued or running | Continue monitoring that run and public Files; do not dispatch another. |
| Dispatch timed out before returning a run ID | Locate the run by tag/category/title and time; inspect Files. Preserve the pending checkpoint. |
| Run failed before uploading | Diagnose the failed step. Retry only after confirming the version was not uploaded. |
| Run failed or disconnected during/after upload | Check Files and the upload result; a failed job can still have published. |
| Upload succeeded but public page is stale | Refresh and wait for that version; do not create another upload. |
| Native picker still open | Complete or inspect that same dialog; keep its page open. Do not click Add image(s) again. |
| Image uploaded, title or later step failed | Reuse its recorded URL or identify the new image; resume title/visibility work. |
| Pending image upload has zero or multiple candidate URLs | Inspect Media and the picker. Resolve the mapping in the manifest; do not guess from asynchronous upload order. |
| Browser action timed out or lost control | Inspect the application result and follow browser-skill synchronization. Never replay an uncertain submission. |

PBZ stores media URLs and a pending upload's before/after URL set in `docs/nexus-media-<version>.json`. A resume preserves these checkpoints. Its ignored `file-uploads.json` preserves pending dispatch and run IDs. Clear an unresolved pending record only after the website and Actions prove that its external effect did not occur. A still-missing dependency, login or credential entry does not invalidate completed stages.

For a copy-only correction after publication, use the source owners and permitted CLI route rather than uploading the same archive again by habit. An extra source-document commit does not require a new application version. Preserve the public release tag's application build.
