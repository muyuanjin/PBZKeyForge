# 连点攻防与点按/架势分离

研究对象为 Steam 1.0302256 / CL-302256。证据来自已安装程序的静态机器码、输入资源和 Blueprint 字节码；未启动游戏，未修改安装目录或真实存档。

## 连点会消耗完美防御窗口

`/PBZDeflectSystem/Abilities/BP_PlayerDeflectPassive` 的 `Give Perfect Window` 实现了连续点按惩罚。攻防同键时，每次攻击点击也触发 `IA_Deflect`，因此会走这套计数和窗口更新逻辑。

其逻辑为：

```text
now = 游戏时间
if now >= LastDeflectClick + QuickclickPunishCooldown:
    counter = 0
else:
    counter += 1

duration = 完美防御帧数 / 配置帧率
NormalWindow = now + duration
punished = duration * QuickclickPunishMultiplier(counter)
if punished > 0:
    PerfectWindow = now + punished
LastDeflectClick = now
```

饰品可增加基础帧数，破防状态可选择另一基础帧数。`NormalWindow` 不使用连点倍率；倍率不大于零时，`PerfectWindow` 保留旧时间戳，不再随点击刷新。这里的 `NormalWindow` 是代码字段名，不能仅凭名字等同于整个普通架势的有效时间。

配置使用 30 Hz 逻辑帧率，不是显示器刷新率或当前渲染帧率。默认资源数值如下，表中毫秒数不含饰品和破防状态的调整：

| 战斗配置资源 | 基础帧数 | 第一次点按 | 间隔不足 0.4 秒的第二次 | 连续第三次起 |
| --- | ---: | ---: | ---: | --- |
| Normal、Hard | 8 | 266.7 ms | 80 ms（30%） | 不刷新完美窗口 |
| Easy | 10 | 333.3 ms | 100 ms（30%） | 不刷新完美窗口 |
| Handicapped | 8 | 266.7 ms | 133.3 ms（50%） | 不刷新完美窗口 |
| Nightmare | 6 | 200 ms | 60 ms（30%） | 不刷新完美窗口 |

各配置的连点冷却均为 0.4 秒；每次点击都会更新最后点击时间，因而持续连点不会因为距离第一次点击已过 0.4 秒就自动恢复。倍率数组越界也返回零。成功得到 Perfect 或 PerfectCounter 结果会发送重置事件，清零计数与最后点击时间。

数值来自 `/PBZCombatAsset/Config/GlobalConfig_Combat_*`，属性顺序由 `PDA_GlobalCombatConfig_C` 的 FProperty 表确定；各难度 Combined 配置引用对应 Combat 配置，并共同引用 `GlobalConfig_Mechanic`。后者的 `Logic Frame Rate` 为 30。Combined 的 getter 直接读取这些字段。Normal 的冷却位于对象导出内 `+0x78`，倍率数组位于 `+0x54`；这是对象内偏移，不是整个包或 EXE 的文件偏移。

这与“持续点击攻击时普通防御仍然有效、完美防御很难触发”的现象相符。第三次点击不刷新窗口，不表示立即清除前一次尚未结束的窗口。独立防御键也会进入同一个 Passive，并不创建独立惩罚计数；左键仍绑定防御时，继续连点左键仍会累积惩罚。可用于对照的操作是移除攻击键上的防御，或停止所有防御输入至少 0.4 秒后，再单次点按。

分类代码位于 `BP_StanceAction_Base_Player.Get Deflect Result`：`0x0392–0x04e4` 要求 `max(HitGameTime, now) <= PerfectWindow`，并检查提前命中的时间条件；不满足时进入 Normal/Great 分支。父类将该返回 tag 传入原生 `DeflectComponent.GetSelfResultData`，它参与实际结果处理。`Try Deflect` 的 `0x0204–0x02b6` 和 `Refresh Deflect Result` 的 `0x0289–0x02bf` 是成功完美防御的重置事件来源。

关键字节码位置（函数内 VM 偏移）：

| 函数 | 位置 | 行为 |
| --- | --- | --- |
| `Give Perfect Window` | `0x0067–0x0100`、`0x073c–0x0781` | 读取冷却，选择清零或递增计数 |
| `Give Perfect Window` | `0x0492–0x054a` | 按配置帧率换算并刷新 NormalWindow |
| `Get Punished Perfect Window` | `0x0000–0x006e` | 查询计数对应倍率，乘以输入窗口 |
| `Give Perfect Window` | `0x05b8–0x06bf` | 仅在惩罚后时长大于零时刷新 PerfectWindow |
| `Give Perfect Window` | `0x06da–0x0709` | 记录点击时间并发送窗口更新事件 |

## 保存槽不区分点按与架势

`IA_Deflect` 使用 Pressed 触发器，`IA_Stance` 使用 Down 触发器，但原厂可重绑名称都是 `IA_Guard`。

游戏重建映射时，使用 mapping name 查询当前按键，查询槽为 `7 = Unspecified`。查询会遍历该名称下匹配基本按键类别的所有槽，而不是只取原厂 InputAction 注册时的槽。因此 Guard 的键鼠 raw 0、2 以及新增 raw 4 都能为两个 InputAction 提供按键。把绑定 1、2 当作点按和长按两个独立功能不符合这条消费路径。

- 保存格式的显式槽为 0–6；7 是 Unspecified，8 是 Max。
- Guard 原厂键鼠注册 raw 0、2，手柄注册 raw 1、3。
- 编辑器三个键鼠绑定列通常对应 raw 0、2、4；列号不是 InputAction 身份。
- 界面名称中的 `/` 只是文字，不能据此判断底层动作关系。

原生锚点：重建调用 `0x144888b6b`；profile vtable `0x14b576368`；查询入口 `0x14487fdd0`；按 mapping name 遍历 `0x144887c80`；匹配谓词 `0x14487b3a0`。查询入口在 `0x14487fe30` 写槽 7，谓词在 `0x14487b491–0x14487b499` 跳过槽号过滤。

## 拆开资源名称还不等于拆开玩法

`BP_PlayerDeflectPassive` 注册了三个输入事件：`IA_Stance.Completed`、`IA_Stance.Triggered` 和 `IA_Deflect.Triggered`。

`IA_Deflect.Triggered` 在 Ubergraph `0x000f` 进入，随后设置 `Stance Key Down = True`（`0x00c2`）、调用 `Give Perfect Window`（`0x01ea`）并激活架势能力（`0x01f8`）。Passive 的 Tick 也会根据这个状态继续尝试激活架势。松键处理来自 `IA_Stance.Completed`，负责清除状态并发送 KeyUp。

`BP_StanceAction_Base_Player` 还维护自己的按下状态：能力激活时设为 True；收到窗口更新事件时又调用 `On Stance Key Down`；Enter/Shake 完成后根据该状态进入持续架势。它不是 Passive 中同名字段的别名。

所以真正实现“点按防御，不因按住而保持架势”，需要同时处理：

1. 输入来源与松键事件；
2. Passive 的按下状态与能力激活；
3. 架势能力自己的按下状态、动画循环与有效窗口检测。

只移除 Stance 输入会丢失现有释放入口；只让 Passive 不写 True，也无法消除架势能力内部的 True。仅靠存档改键不能完成这种分离。

## 资源 Mod 的具体落点

`/PBZInput/IMC_Deflect` 的 `PlayerMappableKeySettings_2` 和 `_3` 属于 Stance。将两者可重绑名称从 `IA_Guard` 改为 `IA_Guard_0`，可让两个 InputAction 查询不同的按键集合。

当前构建可只修改 FName 的 number，从 0 改为 1：3183 字节 Zen 包内的偏移分别为 3105、3119。两个 InputAction、原厂按键、触发器和修饰器都保持原值。这是可重绑身份分离原型，不是完美防御修复或完整玩法分离。

本地已生成小型 `.pak/.utoc/.ucas` 原型，通过 retoc 容器校验和 CUE4Parse 独立回读；只包含修改后的输入包及其必要容器信息。未安装，游戏发现、挂载优先级和实际输入行为尚未验证。

工具若支持这种 Mod，必须按实际补丁后的资源身份重新建立动作、默认槽及手柄家族记录；不能沿用原厂 Guard 的槽位表并仅修改显示名称。已有 Guard 覆盖与新增 Stance 名称的初始化也需要分别处理。

程序提供[完美防御无连点惩罚](perfect-deflect-no-rapid-click-penalty-mod.md)：仅将五个难度配置中的 `Deflect Quickclick Punish Cooldown` 从 `0.4` 改为 `0`，使原逻辑在每次点击时重置连点计数。每次点击仍使用游戏当前计算的基础窗口，保留难度、破防、饰品、配置帧率与普通防御资格。Combined getter、Passive、Base、Player 及其他战斗函数均保持原样。

旧完整取消连点惩罚的窗口效果已有用户实测。本次五配置纯数值容器仍需独立游戏加载与战斗复测。仅拆分 Stance 名称不能避免左键继续触发 Deflect 的连点计数。

## 验证边界与本地产物

Blueprint 解码检查完整消费序列化字节，并核对重建后的 VM 内存长度与函数元数据相等。原生代码使用 pefile/Capstone；资源使用项目的 Archive/Package 解析器。该证据证明当前构建所包含的处理逻辑，不是游戏内输入时序实测。

本地诊断保存在 `dist/guard-binding-investigation/`，不随程序发布：

- `kismet.py`、`perfect-window-bytecode.{json,txt}`：窗口计算。
- `REPORT.md` 及原生反汇编：Guard 查询与槽位关系。
- `input-dispatch/`：输入事件、两层架势状态、判定链。
- `resource-split/`：独立映射原型、配方和封装验证。
- `native-combat/GLOBAL-CONFIG.md`、`deflect-config-values.json`：各难度数值、字段身份和修改落点；同目录还包含原生防御帧组件证据。

游戏内仍需对照原厂分键与攻防同键，记录点击间隔、当前装备/天赋、战斗状态和实际结果；独立防御键的对照不能在左键仍不断触发防御时用于排除连点惩罚。

其他已确认的分类限制包括 `Shachi <= 0`、`Frost_Accumulation > Infliction_Max × 0.5` 时直接返回 Normal；调试 AlwaysPerfect 状态另有旁路。窗口恢复不等于这些条件也已满足。当前玩家的难度与状态未读取。

## 完美防御无连点惩罚的资源依据

补丁只包含 `/PBZCombatAsset/Config/GlobalConfig_Combat_{Easy,Normal,Hard,Nightmare,Handicapped}` 五个数据包。各包仅改 `Deflect Quickclick Punish Cooldown` 的一个 double，五个八字节字段共占 40 字节存储空间，其余包内字节与源资源一致。原包的名字、导入、导出、字段、函数和依赖关系均不改变。

源资源的 FProperty 顺序确定字段身份，各难度 Combined getter 读取各自 Combat 配置中的冷却。`Give Perfect Window` 比较 `now >= LastDeflectClick + QuickclickPunishCooldown`，因此冷却为零时每次点击回到计数零，原倍率数组的首项仍提供未受连点惩罚的动态基础窗口。普通窗口、结果分类、角色与武器资格沿用原逻辑。

开发端由 `build/make-perfect-deflect-no-rapid-click-penalty-mod.py` 复用 `build/extract-input-defaults.py` 的 Archive/Package 读取器，定位并替换这一 double，再打包五个原包及其必要容器信息。运行端安装程序内置的完成容器。验收入口为 `tests/PerfectDeflectNoRapidClickPenaltyModBuildTests.py`、`tests/PerfectDeflectNoRapidClickPenaltyModTests.cs` 和 `tests/PerfectDeflectNoRapidClickPenaltyEmbeddedResourceTests.cs`；独立原始 IOStore 提取可使用 `tests/DeflectSchemaAudit/`。数值、容器和安装验证各自覆盖对应对象，不代替实际游戏加载和战斗结果。
