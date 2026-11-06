# CLI 与 AI Agent 改键

`PBZKeyForge-cli.exe` 与界面程序放在同一目录。普通绑定、增强规则和高级功能使用与 GUI 相同的编辑模型。GUI 的 **AI / CLI** 按钮动态生成发给 Agent 的提示词，带入实际程序路径、当前文件、方案、设备和运行状态。用户说明在提示词外显示，“复制提示词”只复制 Agent 内容。有未应用编辑或离线配置时，会导出当前草稿的临时 ZIP，供 Agent 编辑后导出新 ZIP 再从 GUI 打开。

## 检查与预览

```powershell
.\PBZKeyForge-cli.exe --agent-help
.\PBZKeyForge-cli.exe --inspect "E:\Games\PBZ\ProjectPBZ\Saved\EnhancedInputUserSettings.sav" --json
.\PBZKeyForge-cli.exe --catalog --query "瞬身" --json
.\PBZKeyForge-cli.exe --keys --json
.\PBZKeyForge-cli.exe --presets --json
```

新命令的标准输出均为一个 UTF-8 JSON 对象，失败也输出 JSON。命令拒绝不适用的选项、重复选项和缺失的参数值。`--agent-help` 返回命令、操作、完整字段结构、触发方式和返回码；`--inspect` 返回实际生效方案、选中方案、各设备、完整功能目录、当前绑定 ID、原生容量、增强配置、执行归属及未覆盖输入。`--query` 同时搜索名称、别名和技术标识；不指定时返回完整目录。`--device` 使用 `keyboard` 或检查结果中的手柄家族名称，例如 `Xbox`、`PS`。

直接运行 CLI、不带参数也会输出帮助并退出。CLI 不弹窗、不询问确认；`--json` 可省略。未知命令返回 JSON 错误及退出码 `2`。请求字段拼写错误会明确指出位置，不会默默使用默认值。错误中的 `stage` 指出失败阶段；`operationIndex` 为失败操作的零起始下标，批次规划失败时不写入任何文件。

## 版本与对应源码

```powershell
.\PBZKeyForge-cli.exe --version --json
```

`version` 是当前程序版本，`source.revision` 是构建时的 Git 提交；`source.browse` 和 `source.archive` 分别打开该提交源码和下载源码 ZIP。也可执行 `source.clone`，进入仓库后执行 `source.checkout`，读取同一版本的 `docs/cli.md`、`src/Cli.Agent.cs`、`src/Cli.Components.cs` 及 `prototype/input-router/ue4ss/Scripts/`。`--agent-help` 也包含这些源码信息和本地文档路径。

只有 `source.exact=true` 才表示链接指向本次构建使用的完整源码。`modified=true` 表示构建包含本地修改，链接对应其基础提交，需要向构建者获取修改后的工作目录。没有 Git 信息的源码包构建会返回 `recorded=false`，应向发布者索取对应 release tag；不要用最新版分支代替当前程序源码。下载源码不会改变程序或游戏状态。

输入支持 `.sav`、GUI 导出的 `.zip`、增强配置 `.json` 或 `new`（离线原厂基线）。ZIP 保留普通存档与增强配置，适合 Agent 与 GUI 之间交接。配置 JSON 不包含普通存档修改。读取失败时 CLI 不会把缺失的游戏方案猜成存档内的 profile 标识；修改未认证方案的 SAV 必须明确指定 `--scheme`，可选值来自检查结果。ZIP 和 `new` 使用包内或离线选择的方案。

把修改写成请求文件，例如 `request.json`：

```json
{
  "operations": [
    {
      "op": "add",
      "function": "IA_Jump",
      "device": "keyboard",
      "binding": {
        "Keys": ["RightControl", "J"],
        "Trigger": "hold",
        "Seconds": 0.4,
        "Rule": { "ConsumeKeys": ["J"] }
      }
    }
  ]
}
```

```powershell
# 仅预览，所有修改都留在此次调用的内存中
.\PBZKeyForge-cli.exe --edit "input.zip" --request "request.json" --json
# 导出完整配置，可从 GUI 的“选择存档”打开
.\PBZKeyForge-cli.exe --edit "input.zip" --request "request.json" --out "edited.zip" --json
```

`--request -` 从标准输入读取 UTF-8 JSON。Windows PowerShell 5.1 管道容易受 `$OutputEncoding` 影响；优先用 UTF-8 请求文件（支持 BOM），例如 `$request | Set-Content request.json -Encoding UTF8`。所有操作按顺序规划，某一步失败就不进入导出或应用。请求里的 `function` 使用功能 ID；操作已有绑定时，用最新 `--inspect` 返回的 `Id`，不要根据显示名称或序号推测 ID。新建规则的 ID 在每次独立预览中生成，应用请求后重新检查。

`--out` 生成独立副本，不能写入 Mod 的 `PBZInputRouter/configuration.json`；修改运行时配置请以安装存档为源，使用 `--apply` 或 `--live`。普通导出只包含所选方案的草稿；执行 `reset-all` 后导出 SAV 或 ZIP，则包含全部方案、全部设备的重置结果。

ZIP 和 JSON 配置源会先完成增强配置的编译验证；缺少 ability Tag、无效 hook 等语义错误在导入时即被拒绝。实时规划也会拒绝同一动作上互不一致的原始记录草稿与运行时覆盖，保留原状态；预设中明确声明的两类绑定按各自用途保留。发送前重新读取并认证当前生效方案，不能依赖编辑开始时的状态。

## 应用

```powershell
# 游戏关闭：备份并写回原生存档及增强配置
.\PBZKeyForge-cli.exe --edit "installed.sav" --request "request.json" --apply --json
# 游戏运行且新版键位增强已连接：等待 Mod 确认，不写游戏存档
.\PBZKeyForge-cli.exe --runtime-status "installed.sav" --json
# 按实时模式规划但不发送、不保存；同一请求再去掉 --dry-run 才真正应用
.\PBZKeyForge-cli.exe --edit "installed.sav" --request "request.json" --live --dry-run --json
.\PBZKeyForge-cli.exe --edit "installed.sav" --request "request.json" --live --json
.\PBZKeyForge-cli.exe --runtime-status "installed.sav" --json
```

`--out`、`--apply`、`--live` 三选一；不指定就是预览。`--dry-run` 可以单独使用或与 `--live` 一起使用，不能与 `--out`、`--apply` 混用。原生存档与 Mod 配置不构成跨文件事务，失败结果的 `bindingsSaved`、`activationSaved`、`enhancementSaved` 逐项说明已经完成的部分，`inputBackup`、`activationBackup` 给出存档备份路径。`liveApplied=true` 和 `liveState=confirmed` 表示游戏已确认且配置已保存；`unconfirmed` 表示此次调用没有确认最终成功，需检查错误及当前配置后再处理。CLI 返回码 `0` 为成功，`1` 为操作失败或部分成功，`2` 为无效请求。重试时先检查现状，仅重做未完成的操作。

`--inspect` 与 `--runtime-status` 的 `readiness` 提供 `canApply`、`canLiveApply`、`liveBlockedBy` 和下一步建议。常见阻塞值为 `game_closed`、`enhancement_disabled`、`waiting_for_runtime`、`scheme_unknown`、`scheme_not_active`、`not_installed_source`。状态检查本身成功不代表实时编辑已连接，必须读取 `canLiveApply`；加载期间等待后重新查询。实时请求最多等待约 6 秒，超时取消请求，避免进入场景后延迟生效。

实时修改的原生键位存入增强配置的 `NativeBindings`，各方案及手柄共用；原存档保留。实时应用只允许编辑当前认证的生效方案，不能执行方案切换、组件安装卸载、关闭增强、资源 Mod 或全局重置。首次使用需关闭游戏并应用新版 Mod，再启动游戏。详见[实时编辑](key-enhancement.md#实时编辑与游戏伴随窗口)。

## 编辑操作

每条操作可指定 `device`，覆盖命令级设备。字段大小写与示例一致。

| `op` | 参数与行为 |
| --- | --- |
| `add` | `function`, `binding`；新增输入，保留原有绑定 |
| `add-functions` | `functions` 数组, `binding`；一个输入同时执行多个动作 |
| `replace` | `id`, `binding`；替换指定绑定 |
| `unbind` | `id`；移除一条输入，保留其他输入和自定义功能定义 |
| `enable` / `disable` | `id`；启停一条绑定，保留其设置 |
| `restore` | `id` 恢复一条输入，或 `function` 恢复该动作的原厂配置 |
| `restore-key` | `key`；恢复该物理键，扳机同时处理按钮与轴 |
| `restore-bindings` | 恢复键鼠与所选手柄类型的普通原厂绑定，移除增强规则及屏蔽；保留增强开关、自定义功能、脚本和 hook，可实时应用，不改存档 |
| `copy` | `id`, `function`；将输入与触发设置复制给另一动作 |
| `move-key` / `swap-keys` | `from`, `to`；移动或交换整键操作，包含固定移动方向 |
| `native-trigger` | `function`, `enabled`；保留或屏蔽能力的原有输入方式 |
| `custom-add` | `name`, `target`；创建未绑定的自定义功能，结果目录返回新 ID |
| `custom-edit` | `function`, `name`, `target`；修改定义及引用它的绑定 |
| `custom-delete` | `function`；删除自定义功能和引用它的绑定 |
| `hook-set` | `hook`, 可选 `index`；新增或更新返回值 hook |
| `hook-delete` | `index`；删除 hook（从零开始） |
| `enhancement` | `enabled`；启停增强配置，关闭游戏后应用 |
| `configuration` | `value`；替换完整增强配置，适用于高级脚本、条件、全局屏蔽和原生覆盖 |
| `preset` | `preset` ID 或 `share` 分享码，`mode=Append/Replace/OnlyPreset`；追加合并增强规则 |
| `reset-all` | 恢复全部方案原厂，清空增强配置并关闭增强，需退出游戏 |
| `activate` | 将 `--scheme` 所选方案设为游戏生效方案，需退出游戏 |

`binding.Keys` 是同时按下的组合。`Trigger` 支持 `default`、`down`、`press`、`release`、`tap`、`hold`、`pulse`、`multi_tap`、`sequence`、`axis`、`toggle`、`custom`。默认触发沿用动作输入方式；时长用秒，`Count` 为次数，`Priority` 为处理优先级。

- 轴输入设置 `Axis`，并保持 `Keys=[]`；`Rule.AxisDirection` 为 `both`、`positive`、`negative`。正负方向使用标量轴，如 `Gamepad_LeftX`；二维轴使用 `both`。
- 顺序输入设置 `Rule.Steps`，例如 `[["J"],["RightControl","K"]]`。每步支持组合键。
- `Rule.Consume=true` 消耗全部声明输入，`ConsumeKeys` 只消耗其中一部分。脚本通过 `Rule.Watch` 声明读取的按键和轴，同样遵守消耗规则。
- `Rule.WhenLua` 为条件函数；`EvaluateLua` 为自定义识别器。完整字段及函数签名见 `--agent-help`。输出方向可用 `OutputValue={"x":1,"y":0,"z":0}`。
- 高级输出 `Kind` 支持 `action`、`mapping`、`ability`、`event`、`function`、`property`、`lua`；对象引用使用 `{"object":"对象路径"}`，可以嵌入参数或复合返回值。hook 采用 `{"path":"UFunction路径","value":返回值}`，也支持 `when` 与 `evaluate`。

## 分享与组件

```powershell
.\PBZKeyForge-cli.exe --share "edited.zip" --name "我的配置" --scope keyboard --json
.\PBZKeyForge-cli.exe --component "installed.sav" --action status --json
.\PBZKeyForge-cli.exe --component "installed.sav" --action enable --json
.\PBZKeyForge-cli.exe --component "installed.sav" --action disable --json
.\PBZKeyForge-cli.exe --component "installed.sav" --action uninstall --json
# 显式检查 / 更新共享 UE4SS，也支持已下载的本地包
.\PBZKeyForge-cli.exe --component "installed.sav" --action check-update --json
.\PBZKeyForge-cli.exe --component "installed.sav" --action update --json
.\PBZKeyForge-cli.exe --component "installed.sav" --action update --archive "phantombladezero_ue4ss.zip" --json
# 独立的玩法 Mod：完美防御无连点惩罚
.\PBZKeyForge-cli.exe --gameplay-mod "installed.sav" --action status --json
.\PBZKeyForge-cli.exe --gameplay-mod "installed.sav" --action enable --json
.\PBZKeyForge-cli.exe --gameplay-mod "installed.sav" --action disable --json
```

`--scope` 可选 `all`、`keyboard` 或检查结果中的手柄家族。设备筛选排除其他设备及混合输入规则；无设备归属的自定义定义、全局脚本、能力屏蔽与返回值 hook 仍属于共用设置。组件变更要求游戏关闭。旧的 `--apply-preset`、`--make-vanilla` 仅生成输出副本；写安装存档应使用带备份与验证的 `--edit --apply`。

`--component` 管理键位增强；关闭保留配置，卸载返回 `uninstall.RemovedFiles`、`RetainedFiles`、`SharedLoaderRetained`，保留文件时退出码为 `1`，请据此判断卸载范围。启用使用程序内置运行时，不联网、不弹确认窗口；`check-update` 和不带 `--archive` 的 `update` 才会联网，更新返回备份路径。

`--gameplay-mod` 独立管理完美防御无连点惩罚，覆盖全部方案和难度，`disable` 与 `uninstall` 都移除本工具管理的资源文件。`state=Enabled` 表示文件已安装，下次启动游戏生效。它不能实时切换，也不会被普通键位请求或增强开关连带启停。两种组件命令均可使用包含 `platform/steam/PBZ.exe` 的游戏根目录替代 SAV，因此首次尚未创建存档时也能管理组件。文件冲突会报错并保留现状，不能当成卸载成功。

把 GUI/Agent 导出的 ZIP 应用到安装时，先 `--share edited.zip` 取得分享码，再以安装 SAV 为源提交 `{"operations":[{"op":"preset","share":"分享码","mode":"Replace"}]}`，预览后用 `--apply` 或 `--live` 应用。ZIP 中的方案、分享范围和当前生效方案需要相符；玩法 Mod 单独管理。
