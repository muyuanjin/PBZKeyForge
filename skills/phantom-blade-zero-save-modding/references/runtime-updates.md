# 游戏与 UE4SS 更新

本工具功能的历史兼容范围以 GitHub Releases 实际发布内容为准。未发布的增强模式和原型不视为旧版，不为其添加历史配置推断、迁移或兼容层。

## 取得当前变化

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\check-runtime-updates.ps1 -GameDirectory <game-root>
```

没有本机安装时省略 `-GameDirectory`；脚本仍查询 Steam 公共构建。未指定目录时按 Steam 注册表和库清单查找安装。成功的远端观察保留 24 小时，缓存只保存观察数据，每次调用都与当前固定版本和本机安装重新比较。本机 Steam 构建改变时重新查询。用户反馈游戏刚更新、更新后失效或上游已有修复时使用 `-Force`。脚本只查询版本信息，不下载运行时，不修改固定版本、游戏或存档。

`Ue4ss.Latest` 给出正式 PBZ 包，`Ue4ss.ReleaseNotes` 为发布说明；版本、资源编号或更新时间任一变化都会产生 `ue4ss_asset_changed`。`steam_public_build_changed` 表示公共版本与 `Game.ReviewedSteamBuildId` 不同，即使本机尚未更新也需要处理。`installed_game_build_changed` 表示本机安装比该基线更新；保留旧版安装不会单独阻挡公共版本的发布复查，也不由此新增对所有旧版的兼容声明。复制到 Steam 库外且无法从清单识别的安装标记为 `installed_game_build_unknown`，不能假定它就是当前公共构建。

查询失败写入 `Errors`。普通开发可以继续使用固定版本；失败不刷新成功缓存，也不构成“没有更新”的证据。发布模式始终重新查询，失败或仍有未处理变化时终止。

Actions 使用 `GITHUB_TOKEN` 查询 GitHub；本地若未设置该变量，会尝试复用已登录 `gh` 的凭据，否则使用公开查询。凭据只发送给 GitHub 官方 API，不写入报告、缓存或代理请求。Steam 公共构建由 `api.steamcmd.net` 提供的应用信息查询取得，该服务不可用时报告查询失败。

## 发现变化后的动作

阅读发布说明与对应游戏补丁，确定签名、引擎接口、代理加载或输入管线是否变化。优先检查与当前游戏更新、加载失败和输入失效直接有关的修复。游戏已更新但 UE4SS 尚无新版时，也要检查现有加载器在新游戏上的兼容性，不能只等待版本号变化。

通过既有入口取得最新候选并更新工作区固定版本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\download-ue4ss.ps1 -OutputDirectory dist -UpdatePinnedRelease
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
```

构建后运行 `KeyEnhancementPrerequisitesTests`、`KeyEnhancementInstallationTests`、`KeyEnhancementCoexistenceTests` 和 `Ue4ssUpdateTests`，编译及运行方法见 [tests/README.md](../../../tests/README.md)。测试使用隔离副本。若任务已有其他兼容性检查要求，一并完成；不要因本次只改加载器而遗漏游戏更新改变的输入注册或存档规则。

签名、钩子或输入接口变化还需要对应游戏的启动、UE4SS 加载日志和物理输入观察。游戏运行与实际安装须在当前授权内进行。没有实机条件时保留候选及待验证结论，继续其他可完成工作；不能用安装断言替代运行效果，也不能据此宣称发布兼容。测试失败时修复或保留失败证据，不把未经确认的候选作为已验证版本发布。

在 [docs/game-version-compatibility.md](../../../docs/game-version-compatibility.md) 记录游戏构建、UE4SS 资源身份和实际验证范围。审阅新游戏构建后更新 `build/ue4ss-sources.json` 的 `Game.ReviewedSteamBuildId`；验证运行时候选后，将 `Game.ReviewedUe4ssAssetId` 和 `Game.ReviewedUe4ssUpdated` 更新为已审阅的资源身份。只修改 `Bundled` 会保留 `ue4ss_review_pending`，不能直接解除发布门槛。这些字段记录版本复查基线，不表示所有功能均经实机验证；固定版本和兼容性记录一起进入同一变更。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\check-runtime-updates.ps1 -Release
```

发布模式检查当前固定包是否与上游正式 PBZ 包及已审阅资源身份一致、公共与本机游戏是否仍在复查基线内，以及查询是否成功。`CanRelease` 只表示这项版本复查门槛通过，不代替其他发布检查或文档声明的游戏行为验证；对应运行证据仍由上述适配任务完成。发现上游新版本不兼容时保持草稿，记录问题并修复或等待可用修复，不能只更新基线来绕过失败。

## 无 agent 会话时

`.github/workflows/runtime-updates.yml` 每六小时检查一次，并支持手动运行。发现变化后，在固定的自动化分支准备候选版本、执行安装回归，并创建或更新一个草稿 PR。游戏更新但 UE4SS 未变时也会创建复查草稿。候选测试失败仍保留草稿及结果，默认分支的固定版本保持原样。

后续 agent 接手兼容性、加载器、构建或发布任务时运行同一个检查入口，并处理候选。草稿 PR 不会自行启动 agent；无人值守的适配执行需要另行配置 agent 任务。网络失败通过失败的工作流报告，不会改写成“当前无需复查”。
