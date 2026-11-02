# PBZKeyForge

**English** · [简体中文](#简体中文)

A key binding editor for **Phantom Blade Zero** that writes the game's own input settings file directly, so a single key can drive several actions at once — something the in-game rebinding screen refuses to save because it reports a conflict.

`PBZKeyForge.exe` · single file · no installer · no .NET runtime download · Windows 10/11

---

## Why this exists

Phantom Blade Zero stores your key bindings in a plain GVAS file:

```
<Steam>\steamapps\common\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav
```

The in-game screen validates on **save**, not at runtime, and it rejects any binding where two actions share a key. The engine itself has no such limitation: the mapping data is a list of per-action overrides, and duplicate keys simply fire both actions. This tool edits those overrides directly, which is exactly the edit the settings screen will not let you make.

## Features

- **One key, several actions.** Guard on the attack key, dodge on the guard key, whatever you want.
- **Scheme aware.** Pick which scheme you are editing — `Default`, `Custom 1`, `Custom 2` — and switch the game to it from the tool.
- **7 presets**, from "one-key attack & guard" to a Souls-style gamepad layout and Space-to-sprint.
- **5 languages** — English, 简体中文, 日本語, 한국어, Русский. Auto-detected from Windows, switchable in the toolbar.
- **No overrides lost.** Keys you leave empty keep the game default; everything you do not touch is written back byte-for-byte.
- **Safe writes.** The game must be closed, a timestamped backup is written first, and the file is re-read and verified after saving.

## Install

1. Close Phantom Blade Zero completely.
2. Download the release zip and extract it anywhere.
3. Back up your own file first:

```
<Steam>\steamapps\common\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav
```

Two ways to use it:

**A. The GUI** — run `PBZKeyForge.exe`, press *Auto-detect* (or browse to the `.sav`), choose a scheme, apply a preset or type keys, then *Save to file*.

**B. A ready-made preset** — copy one file from `presets\` over `EnhancedInputUserSettings.sav`, then in game select the scheme named in the file name. These are plain settings files, no executable involved; pick this if you would rather not run a tool.

## Presets

| Preset | What it does |
| --- | --- |
| `onekey` | Attack key also guards (left mouse / X). Nothing else changes. |
| `onekey_dodge` | The above, plus the freed guard key becomes dodge + sprint. |
| `souls` | Souls-style gamepad: light RB, heavy RT, guard LB, dodge/sprint B, interact/execute Y. |
| `space_sprint` | Adds Space as a second sprint key; Shift stays. |
| `space_dodge` | Adds Space as a second dodge key; Shift stays. |
| `undo_onekey` | Puts guard back on right mouse / LB. |
| `vanilla` | Removes every override and returns to the shipped defaults. |

## Command line

```
PBZKeyForge-cli.exe --selftest <sav>
PBZKeyForge-cli.exe --schemes <sav>
PBZKeyForge-cli.exe --list <sav>
PBZKeyForge-cli.exe --apply-preset <in.sav> <out.sav> <presetId> [--scheme <profileId>] [--activate]
PBZKeyForge-cli.exe --activate <in.sav> <out.sav> <profileId>
PBZKeyForge-cli.exe --make-vanilla <in.sav> <out.sav>
PBZKeyForge-cli.exe --gen-presets <in.sav> <outDir>
```

`--selftest` re-encodes the file without changes and compares it byte-for-byte with the input; it is the acceptance test this project is built around.

## Important

- **Do not open the in-game keybind screen and press apply or reset after using this tool.** The game validates on its own save and rewrites the whole file, discarding these bindings. If you do, just re-apply.
- Only one mod that replaces `EnhancedInputUserSettings.sav` can be active at a time.
- Editing a scheme that is not active is fine, but the bindings only apply once that scheme is the active one — the tool has an *Activate scheme* button for exactly that.
- Tested against the Steam launch build (`CL-301524`). A game update may change the file layout; the tool refuses to write if it cannot parse the file.

## Build from source

No SDK required — the .NET Framework compiler that ships with Windows is enough.

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Outputs `dist\PBZKeyForge.exe` (GUI) and `dist\PBZKeyForge-cli.exe` (console).

## License

MIT. Not affiliated with S-Game. Use at your own risk, keep the backup.

---

## 简体中文

直接改写《影之刃零》自己的按键设置文件，让一个键同时触发多个动作——游戏内的改键界面会因为"按键冲突"拒绝保存，而引擎本身并不限制这件事。

`PBZKeyForge.exe` · 单文件 · 免安装 · 不需要额外下载 .NET 运行时 · Windows 10/11

### 为什么需要它

游戏把键位存在一个明文 GVAS 文件里：

```
<Steam>\steamapps\common\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav
```

游戏界面只在**保存时**校验，而运行时并不拦；数据本身就是一组"按动作覆盖"的记录，同一个键写进两个动作就会同时触发。这个工具改的就是这些记录——也正是游戏界面不让你做的那一步。

### 功能

- **一个键多个动作**：防御并到攻击键、闪避并到防御键，随便配。
- **认方案**：可选编辑 `默认 / 自定义方案 1 / 自定义方案 2`，并能在工具里直接把某个方案设为游戏当前生效方案。
- **7 个预设**：一键攻防、一键攻防+防御键改闪避、魂系手柄布局、空格冲刺、空格闪避、撤销一键攻防、恢复默认。
- **5 种语言**：中文、English、日本語、한국어、Русский，自动识别系统语言，也能在工具栏切换。
- **不丢键位**：留空的格子保持游戏默认；没碰到的内容按原字节写回。
- **写入安全**：游戏必须退出、先打时间戳备份、写完回读校验。

### 安装

1. 完全退出游戏。
2. 解压发布包。
3. 先备份你自己的 `EnhancedInputUserSettings.sav`。

两种用法：

- **图形界面**：运行 `PBZKeyForge.exe` → 自动查找（或手动选 `.sav`）→ 选方案 → 套预设或直接填键 → 保存到存档。
- **现成预设文件**：把 `presets\` 里对应文件覆盖到 `EnhancedInputUserSettings.sav`，进游戏选文件名里的那个方案。纯配置、不含可执行文件，介意杀毒的同学用这个。

### 重要提醒

- **不要**在用完之后进游戏打开键位界面点"应用"或"恢复默认"，游戏会整份重写、把这些键位冲掉；真冲掉了重新套一次即可。
- 同类会替换 `EnhancedInputUserSettings.sav` 的 mod 只能同时装一个。
- 编辑非当前方案也可以，但要生效必须把该方案设为当前方案——工具里有"启用该方案"按钮。
- 针对 Steam 首发版本（`CL-301524`）验证；游戏大更新后文件结构可能变化，工具解析不了时会拒绝写入。

### 从源码构建

不需要 SDK，Windows 自带的 .NET Framework 编译器就够：

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

产物：`dist\PBZKeyForge.exe`（界面）与 `dist\PBZKeyForge-cli.exe`（命令行）。

### 许可

MIT。与 S-Game 无关，使用风险自负，记得留备份。
