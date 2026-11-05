# Unreal Engine 源码缓存

## 结论

《影之刃零》（Phantom Blade Zero）使用 **Unreal Engine 5.7.4**。

本地源码缓存：`vendor/UnrealEngine-5.7.4`（`vendor/` 已加入 `.gitignore`，不随仓库提交）。

| 项目 | 值 |
| --- | --- |
| 官方仓库 | `https://github.com/EpicGames/UnrealEngine` |
| tag | `5.7.4-release` |
| commit | `260bb2e1c5610b31c63a36206eedd289409c5f11` |
| `Engine/Build/Build.version` | `MajorVersion 5` / `MinorVersion 7` / `PatchVersion 4` / `BranchName UE5` |
| CompatibleChangelist | `47537391` |
| 本地稀疏目录 | `Engine/Source`、`Engine/Config`、`Engine/Plugins`、`Engine/Build`、`Engine/Shaders` |

## 版本依据

游戏构建 `CL-301524_JKS-17219`（Steam 首发）和 `CL-302256_JKS-17280` / 1.0302256 均对应 UE 5.7.4：

- 多个 PC 技术测评记录为 Unreal Engine 5.7.4，例如 DSOGaming 的基准测试与画质分析、Wccftech 的 PC 调优指南。
- Nexus 的 PBZ 映射文件名为 `ProjectPBZ-5.7.4-301524-full.usmap`，说明其由 UE4SS 从 5.7.4 游戏中导出；同一说明指出 S-Game 对引擎核心布局做了自定义修改。
- 安装中的 `PBZ.exe` 没有可读的 `++UE5+Release-5.x` 版本串，但包含 `C:\UnrealEngine\Engine\Source\...` 和 `Z:\UEVFS\...` 构建路径，表明是 S-Game 自行编译的引擎。
- 本仓库此前出现的 `EGame.GAME_UE5_6` 是 CUE4Parse/UAssetGUI 的兼容枚举上限，不是游戏引擎版本；UAssetGUI 1.1.0 最多识别 UE 5.6，读取 5.7.4 资产时仍使用 `VER_UE5_6`。

## 获取方式

官方仓库需要 GitHub 账号与 Epic Games 账号关联后才能访问。本次使用部分克隆加稀疏检出，只拉取源码相关目录：

```powershell
git clone --filter=blob:none --no-checkout --depth 1 `
  --branch 5.7.4-release `
  https://github.com/EpicGames/UnrealEngine.git vendor\UnrealEngine-5.7.4
cd vendor\UnrealEngine-5.7.4
git config core.longpaths true
git sparse-checkout init --cone
git sparse-checkout set Engine/Source Engine/Config Engine/Plugins Engine/Build Engine/Shaders
git checkout
```

`git` 提示 `refs/tags/5.7.4-release ... is not a commit` 是正常的：`5.7.4-release` 是附注 tag，`git rev-parse HEAD` 指向其解引用后的 commit。

## 常用入口

| 用途 | 路径 |
| --- | --- |
| 引擎版本宏 | `Engine/Source/Runtime/Launch/Resources/Version.h` |
| 蓝图字节码序列化 | `Engine/Source/Runtime/CoreUObject/Public/UObject/ScriptSerialization.h` |
| 包摘要与文件版本 | `Engine/Source/Runtime/CoreUObject/Private/UObject/PackageFileSummary.cpp` |
| 存档头与版本 | `Engine/Source/Runtime/Engine/Private/GameplayStatics.cpp`、`SaveGameSystem.cpp` |
| Enhanced Input | `Engine/Plugins/EnhancedInput` |
| 构建版本 | `Engine/Build/Build.version` |

## 注意

- 这是 **原版 5.7.4 源码**。S-Game 修改了引擎核心布局，原版源码只能作为行为与格式的参考基线，不能假定与游戏二进制逐行一致。
- 源码使用受 Unreal Engine EULA 约束；缓存仅保存在本地，不应随仓库或发布包分发。
- 更新到其他 5.7.4 之后的版本时，建议删除缓存后按上面的命令重新克隆，或在现有仓库中 `git fetch --depth 1 origin tag <tag>` 后重新执行 sparse checkout。
