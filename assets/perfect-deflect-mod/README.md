# Resources for “Perfect guard: no rapid-click penalty”

`PBZKeyForge_PerfectDeflect_P.pak`, `.utoc` and `.ucas` contain the unencrypted, uncompressed IOStore patch embedded in both executables. The companion PAK is empty. `layout.json` records the source build, required chunk identities and cooldown offsets; it is embedded for installation checks. `companion.pak` is the empty PAK used when rebuilding.

The patch replaces only five data assets under `/PBZCombatAsset/Config/`: `GlobalConfig_Combat_Easy`, `Normal`, `Hard`, `Nightmare` and `Handicapped`. Each package changes only the eight-byte double `Deflect Quickclick Punish Cooldown` from `0.4` to `0`: five eight-byte fields (40 bytes of field storage). All other source package bytes remain unchanged. Combined getters, Passive, Base, Player and other combat scripts retain their original code.

The source build is Steam 1.0302256 / CL-302256, using Unreal Engine 5.7.4. Zero cooldown lets the original game logic reset its rapid-click counter on each input. Difficulty, accessories, guard-break state, logic frame rate, normal window and native eligibility continue to determine the result.

Regenerate from a local source:

```powershell
python build/make-perfect-deflect-mod.py --game-exe <native-PBZ.exe> --paks <Paks-directory> --oodle <Oodle-DLL> --retoc <retoc.exe> --build CL-302256
python tests/PerfectDeflectModBuildTests.py
```

The generator imports the existing package reader from `build/extract-input-defaults.py`. It reads the local source, locates the serialized cooldown, changes only that double and packs the five packages with their original imports. Relative paths resolve against the caller; temporary files use a separate directory. `--output` selects a different output directory. No encryption keys are stored.

For an independent IOStore extraction with CUE4Parse:

```powershell
dotnet build tests/DeflectSchemaAudit/DeflectSchemaAudit.csproj
dotnet tests/DeflectSchemaAudit/bin/Debug/net10.0/DeflectSchemaAudit.dll --container assets/perfect-deflect-mod/PBZKeyForge_PerfectDeflect_P.utoc dist/deflect-container-read
```

The reader uses `GAME_UE5_7`. Its container mode extracts raw chunks and verifies the empty companion PAK; it does not deserialize Zen headers or run the game. Source-byte comparison and the generator tests check the five changed doubles and preserved package data. Installation has no Python, retoc, Oodle or network dependency. See [usage and limits](../../docs/perfect-deflect-mod.md).
