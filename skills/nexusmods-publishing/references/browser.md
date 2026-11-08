# Browser and media operations

Read browser-skill and the relevant cooperation, uploads, native dialogs and interaction references. Use a task-owned session in the explicitly selected logged-in browser. No credential extraction or alternate browser backend is needed. A new task tab starts at about:blank: perform its initial navigation before syncing an observable web page. Navigation on an existing document requires the current sync context and its top-level observation receipt. Do not reuse session IDs, refs or contexts from another run.

## Images and the Windows picker

Use the repository's native file-selection helper after clicking **Add image(s)**. Chromium exposes its Open dialog below the owning Chrome window; it may not be a desktop top-level window. Wait for it. Some filename/Open controls expose only native Edit/Button handles, without usable UIA patterns. PBZ's `build/select-upload-files.ps1` selects quoted full paths through native controls, verifies WM_GETTEXT and clicks Open. Unicode paths need the PowerShell 5.1 script saved as UTF-8 with BOM.

Keep the same upload page open until selection finishes. A closed picker proves only file selection: wait for uploaded image previews. Multi-upload completion order can differ from file order, and Nexus may assign every image **No title**. Do not bind URLs by list position. PBZ's publisher uploads one image at a time, records the URL immediately, then sets its title; this is deterministic and resumable. If bulk uploads are used, match the actual pixels or inspect the images before recording the mapping. Nexus can recompress PNG files, so binary equality is insufficient.

Use full application screenshots for the main gallery when that is the project's media contract. Dialog illustrations belong under Hidden images when the body needs them. Keep public original-size image URLs for body embedding. A project's gallery count, languages and order come from its own instructions.

## Editor controls

The current Nexus editor routes are `/games/<game>/mods/<mod-id>/edit/general` and `/edit/media`. Observe their live controls before adapting selectors. The public mod page may still use the older `/<game>/mods/<id>?tab=...` routes.

- On General, change version and summary, switch **View source (Ctrl+Shift+S)**, fill the visible source textarea, switch back and save. The editor can contain both a hidden original textarea and a visible source textarea; target the visible source. General and Media have duplicate Save controls; use the observed fixed-header control.
- Edit the **existing sticky**, identified by its stable comment ID. Click Edit post, toggle **[BBCODE]**, fill its textarea, toggle back and Submit. Verify the public post after reloading, including sticky status and retained history. Do not create a second announcement post as a substitute.
- Image menus contain Edit title, Set as thumbnail and Hide from gallery. Floating menus can relocate while the page scrolls. Keyboard Enter on an observed menuitem avoids pointer drift. A click/drag delivered successfully is not evidence that Nexus accepted the resulting change.
- Use a sufficiently tall desktop viewport for gallery drags: bsk drag does not auto-scroll while held. Inspect order after each drag and save it. PBZ's publisher emulates a desktop 1692×1800 task viewport. Close a preview/sidebar overlay if it covers the controls; do not change the user's browser-wide settings.

## Replacement and completion

Upload the needed new images, set their titles and visibility, then update body URLs and save the description. Verify the public body no longer references old images **before deleting them**. Delete only the obsolete version's mapped images, retain shared banners, then save the gallery order and thumbnail. Unexpected untracked gallery images require inspection rather than automatic deletion.

Reload the public page and confirm: expected version and both file categories; the original sticky with current content; gallery IDs in the intended order; `og:image` pointing at the intended thumbnail; the body images matching the source and loading with nonzero natural dimensions. Visible post text may be `Sticky` even when its CSS renders uppercase. Exclude hidden editor textareas and scripts from checks of public content.

If debugging a website failure, start browser-skill capture before reproduction, inspect its result, and export locally under ignored release output. Debug exports can contain user content; they are not release assets. Stop capture and the owned session when finished. Generic CORS noise from unrelated site widgets is not evidence that an upload failed when the previews and public images are present.
