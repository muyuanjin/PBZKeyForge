# Local verification

Run from the repository root after `powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1`. These checks use Windows' bundled .NET Framework compiler:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /out:dist\EditorSessionTests.exe /r:System.Core.dll src\Gvas.cs src\GameSchemeSettings.cs src\I18n.cs src\I18n.Editor.cs src\I18n.Extended.cs src\Presets.cs src\EditorSession.cs tests\EditorSessionTests.cs
.\dist\EditorSessionTests.exe samples\vanilla-base.sav
```

The session checks create fresh temporary copies. They cover disk refresh and conflicts, per-scheme keyboard and controller drafts, isolation between profiles with the same controller family, preview replacement/cancellation, global reset/undo, unknown data preservation, and interrupted writes. No game installation is required for this command.

For authenticated activation checks, set `$gameExe`, `$sharedSave`, and `$inputSave` to files in a supported installed Steam build. Set `$activationArtifacts` to a **new** directory on the game's volume. The harness copies saves and creates a read-only executable hard link; it never edits the source saves.

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe dist\PBZKeyForge.exe $gameExe $sharedSave $inputSave $activationArtifacts
```

When the game is running, use the harness's read-only PBZS checks instead. They validate the recognized key fingerprint, reject an unknown executable and tampered containers, and verify the copied save remains unchanged; they do not activate a scheme or require closing the game:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\SchemeActivationTests.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\SchemeActivationTests.cs
.\dist\SchemeActivationTests.exe --read-only dist\PBZKeyForge.exe $gameExe $sharedSave $newReadOnlyArtifacts
```

The resulting directory is a copied installation layout. Use it as the source for session tests that inject an activation failure after bindings have been saved, then verify that retry writes only the remaining activation:

```powershell
.\dist\EditorSessionTests.exe samples\vanilla-base.sav $activationArtifacts $newSessionArtifacts
```

`$newSessionArtifacts` must also be a new directory on the same volume. To test actual GUI controls and capture all available languages at regular/minimum sizes, run:

```powershell
& $compiler /nologo /win32manifest:build\app.manifest /out:dist\EditorUiTests.exe /r:dist\PBZKeyForge.exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tests\EditorUiTests.cs
.\dist\EditorUiTests.exe "$activationArtifacts\ProjectPBZ\Saved\EnhancedInputUserSettings.sav" $screenshots
```

The GUI check intentionally edits the supplied **copy** through the real Apply button. It opens keyboard and controller binding dialogs, distinguishes left/right modifiers, switches quick configurations, and verifies navigation reads external changes while retaining drafts. After applying Custom 1, it compares the other profiles and unrelated data byte-for-byte. It uses the application's DPI manifest and checks custom window controls, centered binding headings, toolbar alignment and the footer. Windows stay transparent during automated screenshots to avoid flashing every test state on the desktop. Inspect the images for clipping and consistency; these checks do not establish actual game startup behavior or every Windows DPI setting.

To check only the custom confirmation dialog's action row without a supported game installation, pass a copy of `samples\vanilla-base.sav` and an artifact directory containing any adjacent `PBZSharedSetting.sav` file, then add `--dialog-only`. This asserts that all three buttons remain inside the client area and the Cancel action can be clicked.

For a fresh repository screenshot, pass a copy of the input fixture and output directory as above, then add `--capture`. The capture uses a synthetic active-scheme badge and the UI shows only the final two save-directory components; the full path remains available through the on-screen tooltip.

Translation checks and language provenance are documented in [docs/languages.md](../docs/languages.md). Static evidence for controller profile ownership is in [docs/gamepad-profile-evidence.md](../docs/gamepad-profile-evidence.md).
