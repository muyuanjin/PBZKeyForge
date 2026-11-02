# Supported interface languages

PBZKeyForge provides all 15 interface languages listed for **Phantom Blade Zero** by Steam on **2026-11-03**. Steam app **4115450** was identified from its application manifest. The language list comes from the official [`supported_languages` field](https://store.steampowered.com/api/appdetails?appids=4115450&l=english&filters=basic), rather than Unreal Engine's general culture directories. This verifies the published list; it does not claim the installed game's language menu was opened.

| Language | Code |
| --- | --- |
| English | `en` |
| 简体中文 | `zh-Hans` |
| Français | `fr` |
| Deutsch | `de` |
| Español (España) | `es-ES` |
| Русский | `ru` |
| 日本語 | `ja` |
| 繁體中文 | `zh-Hant` |
| Português (Brasil) | `pt-BR` |
| 한국어 | `ko` |
| Italiano | `it` |
| ไทย | `th` |
| Tiếng Việt | `vi` |
| Português (Portugal) | `pt-PT` |
| Español (Latinoamérica) | `es-419` |

`src/I18n.cs` owns language selection, OS detection and the original action names. `src/I18n.Editor.cs` owns the original five editor translations. `src/I18n.Extended.cs` adds ten languages and the two regional variants' terminology; those variants have full dictionaries. Existing official action strings are preserved. New translations are application translations, not an assertion that every phrase was extracted from the game.

When adding or changing a string, update every language, preserve its format placeholders, and check narrow layouts. French, German and Portuguese labels can be wider; Thai requires an appropriate Windows font fallback.

Run the independent dictionary, placeholder and region-detection checks from the repository root:

```powershell
$translationOutput = Join-Path $env:TEMP 'pbz-editor-model-tests'
New-Item -ItemType Directory -Path $translationOutput -Force | Out-Null
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo "/out:$translationOutput\I18nTranslationTests.exe" /r:System.Core.dll src\I18n.cs src\I18n.Editor.cs src\I18n.Extended.cs tests\I18nTranslationTests.cs
& "$translationOutput\I18nTranslationTests.exe"
```

The test checks each dictionary directly so English fallback cannot hide a missing translation. It covers every string key, including dialogs, presets, legacy entries and window controls.
