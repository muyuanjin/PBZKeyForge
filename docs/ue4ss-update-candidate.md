# UE4SS and game compatibility candidate

UE4SS: v4.0.0
Asset: 741668054, updated 11/13/2026 00:38:23
Release: https://github.com/KintaroModding/ue4ss/releases/tag/v4.0.0
Public Steam build: 26367565
Reviewed Steam build: 26367565
Changes: ue4ss_asset_changed

Candidate build and installation checks: success.
Game startup, UE4SS loading and physical input: pending verification.

Follow [the adaptation procedure](../skills/phantom-blade-zero-save-modding/references/runtime-updates.md) before publishing compatibility claims.

## Upstream release notes

The official UE4SS release `v4.0.0` (2026-11-12, commit
[`30afe5ff`](https://github.com/UE4SS-RE/RE-UE4SS/commit/30afe5fff94a94c2313e220b2002059a080da9a0)), republished
unchanged, with a ready package for Phantom Blade Zero.

What is new for Phantom Blade Zero:

- **UE4SS 4.0.0, the first official UE4SS release since 2024.** For this game it is the build 1195 of our last
  release plus the UE4SS team's fixes of the last weeks: in a game install only `dwmapi.dll`, `ue4ss\UE4SS.dll` and
  BPModLoaderMod's `main.lua` change. See the [UE4SS release notes](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/v4.0.0)
  for everything new, and the UE4SS upgrade guide if you write mods.
- **Our config is now part of UE4SS.** The UE4SS team merged our Phantom Blade Zero config
  ([UE4SS-RE/RE-UE4SS#1484](https://github.com/UE4SS-RE/RE-UE4SS/pull/1484)) on 2026-11-12, a few hours after the
  v4.0.0 release, so the official v4.0.0 zip does not contain it yet. This package adds it; the files are the same.
  The next official UE4SS release will include it.
- **Tested in game (2026-11-13, Steam build 26367565):** game start, save load, our two signatures found their
  functions, a UE4SS C++ mod and a script pak mod worked. The only error lines are the known ones (below).

Files:

- `phantombladezero_ue4ss_v4.0.0.zip`: for Phantom Blade Zero. Unpack it into `platform\steam\`, next to the large
  `PBZ.exe` (not `steam\`, not `ProjectPBZ\Binaries\Win64\`). The official UE4SS v4.0.0 zip with our config for the
  game in `ue4ss\`: two signature files from the UE4SS team's own configs and `MemberVariableLayout.ini` (the game's
  own layout of several engine structures). To update from an earlier package, unpack it over the old files; the old
  `ue4ss\Mods\mods.json` is no longer used and can stay.
- `dawnwalker_ue4ss_v3.0.1-1181-g83330635.zip`, `expedition33_ue4ss_v3.0.1-1181-g83330635.zip`,
  `mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip`: unchanged from the release `v3.0.1-1181-g83330635`.
- `UE4SS_v4.0.0.zip`, `UE4SS_v3.0.1-1181-g83330635.zip` and `zCustomGameConfigs.zip`: the official files, unchanged.
- `SHA256SUMS.txt`: the SHA-256 of the zips.

Known issues in Phantom Blade Zero: UE4SS could not hook `ProcessConsoleExec` (console commands that UE4SS mods add
may not work, not tested). A real Blueprint mod is still not tested.

UE4SS is made by the UE4SS-RE team and its contributors (MIT license): https://github.com/UE4SS-RE/RE-UE4SS. Report
problems with UE4SS itself there. What this fork adds: `FORK.md`; how to install and use it: `openmods.md`.

