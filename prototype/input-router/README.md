# PBZ 键位增强原型

面向 Steam CL-302256、UE 5.7.4，提供资源编译器、架势瞬身迁移补丁和 UE4SS 运行时路由器。三者可分别使用。PBZKeyForge 的“键位增强”管理运行时安装和规则；资源编译器与瞬身迁移补丁保留独立入口。运行时不写键位存档。

## 能力

| 目标 | 实现 |
| --- | --- |
| LB＋RB 不再触发架势瞬身，改为按住 LB 再按 A | 纯资源补丁，迁移 `AIM_Hun_Dash` 的 SpecialDash 组到 `AIM_Hun_Jump`，保留原跳跃、普通闪避和能力资格判定 |
| 在动作详情改绑瞬身并关闭原组合 | 添加 SpecialDash 绑定，取消“保留游戏原有触发方式”；运行时同时屏蔽 SpecialDash、DeflectDash 原生输入入口 |
| 一个动作绑定超过存档的 7 个槽 | 直接增加 IMC 资源映射，或在 Lua 中增加规则，无固定规则数量上限 |
| 点按、释放、长按、连点、组合键、顺序输入、周期触发、开关、模拟轴 | Lua 路由核心；自定义 `evaluate` 可实现其他有状态识别器 |
| 一键多功能、多键同功能、组合抢占单键 | `outputs`、动作值聚合、`priority` 和 `consume` |
| 分开输出点按防御和持续架势 | 独立注入 IA_Deflect、IA_Stance；释放时输出零值 |
| 调用动作、能力、事件、函数、属性或自定义代码 | `action`、`ability`、`event`、`function`、`property`、`lua` 目标 |
| 修改可反射判定的返回值 | UE4SS 返回值 hook，支持条件和 Lua 回调 |

“任意动作”在这里是可配置目标和代码扩展接口：实际能力仍要求存在、角色拥有、参数正确，并满足引擎的激活条件。对全部菜单输入及任意原生判定的无条件键位增强不属于已实现范围，具体边界见文末。

## 安装与移除

退出游戏后操作。`PBZInputRouter-prototype.zip` 分成两个独立目录：

- `resources/`：把其中的 `ProjectPBZ` 合并到游戏根目录，只安装瞬身迁移资源即可满足 LB＋A 的需求，不需要 UE4SS。
- `runtime/`：把其中的 `platform` 合并到游戏根目录，启用 Lua 输入路由器。完整发行包附带 PBZ 专用 UE4SS；已有 UE4SS 时保留现有加载器和 Mod 列表，只添加 `Mods/PBZInputRouter` 并启用它。

资源补丁和其他覆盖 `AIM_Hun_Dash`、`AIM_Hun_Jump` 的 Mod 不能独立叠加，须合并资源。此组合跟随当前架势键和跳跃键；LB、A 是原厂方案的示例。

运行时配置位于 `platform/steam/ue4ss/Mods/PBZInputRouter/Scripts/router-config.lua`：

| 默认键 | 行为 |
| --- | --- |
| F7 | 重读配置，非法配置保留旧配置 |
| F8 | 切换键位增强；关闭时撤销按键屏蔽并释放额外输出；异常暂停后重试 |

默认配置关闭且没有附加规则。`examples/demo.lua` 提供 F9 跳跃、F10 点按防御／持续架势、F11 请求 SpecialDash、LB＋A 跳跃的示例；复制或导入示例才会添加这些绑定。应用内的规则源保存在 Mod 目录的 `configuration.json`，`router-config.lua` 由它生成，修改源配置后应用即可重建。

应用内默认 `RuntimeHotkeys=false`，F7/F8 不注册快捷键，仍可自由绑定。显式设为 true 才启用表中的诊断快捷键。独立 Lua 配置使用 `runtime_hotkeys` 字段。

运行时默认仅在游戏前台、角色存在、未暂停、未显示鼠标光标时处理额外绑定；其他状态撤销屏蔽并释放额外输出。原生上下文始终保留，未修改的按键继续由游戏处理。

应用内通过“更多 → 完整卸载键位增强 → 应用更改”卸载脚本、配置及启用项。安装器记录自己创建的加载器文件；只有内容未改且没有其他 Mod 使用时才一并删除，原有共享加载器和未知修改保留。手动安装可在退出游戏后移除 `Mods/PBZInputRouter`，并移除自己添加的启用项。移除 `~mods/PBZKeyForge_StanceDashOnJump` 内的三个容器可卸载资源补丁。

## 配置接口

`targets` 是命名输出，`bindings` 是规则数组。数量由内存和每帧处理成本决定，不经过存档槽位。

```lua
return {
    enabled = true,
    dynamic_capture = true,
    require_foreground = true,
    targets = {
        jump = {kind="action", path="/PBZInput/InputActions/IA_Jump.IA_Jump", value_type="bool"},
        call = {kind="function", receiver="pawn", path="/Script/Engine.Character:Jump"},
        custom = {kind="lua", run=function(backend, event)
            -- event.value、event.binding、event.phase、event.state 可供自定义处理。
            return backend:invoke(backend.pawn, "/Script/Engine.Character:Jump")
        end}
    },
    bindings = {
        {id="jump-chord", keys={"LeftControl","J"}, trigger="press",
            priority=100, consume={"J"}, outputs={"jump"}},
        {id="hold-call", keys={"H"}, trigger="hold", seconds=0.5, outputs={"call"}},
        {id="sequence", trigger="sequence", steps={"I","O","I"},
            interval=0.5, outputs={"custom"}}
    }
}
```

按键采用 UE `FKey` 名称。`keys` 中所有键同时成立；`axis` 指定轴键，二维／三维键分量用 `@1`、`@2`、`@3` 后缀。规则按优先级、组合键数量、配置顺序处理。`consume=true` 保留该规则的全部 `keys`，数组形式只保留指定键；保留期间低优先级规则不能输出这些键，原始输入采样不变。点按与持续规则共享一键时，点按规则不应消费持续规则所需的键，F10 示例展示了此配置。

`native-input.lua` 保留原生上下文，不生成原生映射的逐帧重放。`suppressions` 中明确指定的动作／按键关系及规则消费的物理键，会将对应运行映射的 `Key` 临时改为 `None`。组合结束后，消费键仍锁到物理释放；上下文变化不会把仍按住的键当作释放。运行映射数组重建时使用当次有效对象，恢复不会写旧包装对象。

`ability_suppressions={"Ability.Action.Hun.SpecialDash","Ability.Action.Hun.DeflectDash"}` 关闭原生瞬身组合入口，保留 Dash 的普通闪避。`native-abilities.lua` 遍历 InputActionBinding 的普通和条件 Actions、Combos、InterruptLower，仅清除匹配 TriggerTag；额外 ability 输出通过独立入口激活。列表清空及键位增强停用时恢复原标签。应用在能力详情提供对应复选框，关闭 SpecialDash 时包含 DeflectDash 变体。静态核对覆盖 CL-302256 全部 114 个资产、907 条触发记录，DebugAbility 均为空；当前实现不处理直接能力引用非空的其他版本或外部修改。

`mapping` 目标将原生可重绑名称扩展为当前有效 InputAction 管线；完全解绑时使用已应用上下文的默认模板。额外输出克隆原生触发器、修饰器配置，使用独立实例保存状态，来源不变时复用实例。原生按键及其触发器状态保持由游戏处理。

`trigger` 支持 `down`、`press`、`release`、`tap`、`hold`、`pulse`、`multi_tap`、`sequence`、`axis`、`toggle`。`seconds` 是 tap/hold 门槛，`interval` 是周期或连点／顺序步骤超时，`count` 是连点次数，`threshold` 是轴门槛。`evaluate(now, snapshot, context, state)` 可替代内置识别，返回 `{active=..., pulse=..., value=..., reserve=...}`，额外采样键写入 `watch`。

`outputs` 可以是目标名，或 `{target="move", value={x=0,y=1,z=0}}`。同名 `action` 输出逐帧聚合，提供 started/triggered/completed，`value_type="bool"` 归一化，`clamp=1` 限幅。其他目标只在识别器给出 pulse 时调用。

`when` 接受状态字段名、字段表或函数；context 含 `gameplay`、`paused`、`foreground`、`pawn`、`controller`、`subsystem` 和 `backend`。函数可查询实际角色状态，接收者与参数必须适合目标函数。

目标格式：

- `ability`：`tag`、`priority`、`input_action`，可用 `force=true` 选择游戏另一条激活入口；force 不承诺绕过能力内部资格判断。
- `event`：`tag` 和 `payload`，向当前 pawn 发送 GameplayEvent。
- `function`：`receiver`、UFunction 的完整 `path` 和 `args`。
- `property`：`receiver`、`property` 和 `value`。
- `lua`：`run(backend, event)`，可组合任意可访问的游戏函数与自有状态。

`receiver` 可为 `pawn`、`controller`、`subsystem`、`ability_input`、`ability_system`、完整对象路径或 `function(backend, context)`。`args`／属性值支持普通值、嵌套结构、`{object="路径",class="类名"}`、`{receiver="pawn"}`、`{fname="名字"}`、`{tag="Gameplay.Tag"}`、`{value=true}`、`{phase=true}` 和 `function(backend, event)`。

`hooks` 中每条规则为 `{path="完整UFunction路径", value=false, when=...}`，或以 `evaluate(...)` 计算返回值。原函数仍会执行，原有副作用不会撤销。

动作目录见 `actions.json`：98 个 InputAction、114 个 InputActionBinding、236 个输入可达能力 tag，含原始条件和来源。它是输入资源目录，不是全部 GameplayAbility 或全部游戏功能目录。更多识别器配置见 `examples/triggers.lua`。

应用的功能选择同时显示名称与代码。`assets/key-enhancement/action-localization.json` 为 306 个目录目标保存 15 语言原生功能名或武器／动作类别及资源引用；28 项没有原生显示名对应的内部输入使用应用语义名称。名称、技术路径及别名均可搜索，包括“瞬身”与 `SpecialDash`。`build/extract-action-localization.py` 从游戏 PAK 只读刷新译文。

“添加自定义功能”提供函数、事件、属性、返回值 hook 和脚本入口。参数及值使用布尔、数字、文本、对象等类型控件，结构和数组支持递归编辑。自定义条件、返回表达式和 Lua 输出保留代码编辑入口。

## 纯资源编译

`resources.py` 修改已加载 IMC 的 cooked Zen 数据，支持 `append`、`replace`、`remove_actions`；可跨包引用既有 InputAction。Pressed、Down、Released、Hold、Tap 触发器及物理组合键由新增非消费 helper InputAction 实现，无需保存额外槽位。

```powershell
python prototype/input-router/resources.py prototype/input-router/examples/resource.json `
  --game-exe $gameExe --paks $paks --oodle $oodle --retoc $retoc `
  --output dist/input-router/resources

python prototype/input-router/special_dash.py `
  --game-exe $gameExe --paks $paks --oodle $oodle --retoc $retoc `
  --output dist/input-router/stance-dash-final
```

参数指向本机实际游戏原生 EXE、Paks、Oodle DLL 和 retoc。编译器不安装、不改存档、不把游戏密钥写入输出。瞬身的资源关系见 `special-dash.md`。

## 构建与验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File prototype/input-router/native/build.ps1
python tests/input-router/run_lua.py tests/input-router/RouterTests.lua
python tests/input-router/run_lua.py tests/input-router/BackendTests.lua
python tests/input-router/run_lua.py tests/input-router/NativeInputTests.lua
python tests/input-router/run_lua.py tests/input-router/NativeAbilityTests.lua
python tests/input-router/run_lua.py tests/input-router/MainLifecycleTests.lua
powershell.exe -NoProfile -ExecutionPolicy Bypass -File prototype/input-router/stage.ps1 `
  -LoaderRoot dist/input-router-runtime-research/loader
```

Lua 检查需要 Lua 5.4 DLL，可传 `--lua-dll`。资源集成入口为 `ResourceRouterTests.py`、`SpecialDashTests.py`，参数见各脚本 `--help`。前者验证跨包动作、超过槽数的绑定、组合触发及真实容器；后者验证全部 114 个绑定资源往返和 11 条瞬身路径迁移。

原生上下文保留、选择性屏蔽、消费锁释放、数组重建及独立触发器／修饰器通过 `NativeInputTests.lua` 模拟验证；该运行时方案尚未完成实机验证。

`NativeAbilityTests.lua` 验证能力标签的选择性屏蔽、晚加载、数组重建、外部修改及恢复；`BackendTests.lua` 覆盖配置列表原地清空后的恢复，并断言无关 E 键和原生上下文保持不变。`SpecialDashTests.py` 读取全部输入绑定资产，确认没有绕过标签的直接能力引用。

CL-302256 既有实机证据包含资源 IMC_Default 加载 47 条映射，其中 12 条额外 F 键；动作注入驱动跳跃和架势；实际 Windows F9/F10 输入驱动路由；UFunction 布尔返回 hook 安装和移除改变／恢复结果。瞬身资源的游戏内回读确认 Dash 剩余 2 组、Jump 包含 2 组及全部 11 条迁移条件。“架势＋闪避”没有激活 SpecialDash，“架势＋跳跃”激活 SpecialDash 并扣除约 205 杀气，普通 Jump/Dash 保留。详见 `special-dash.md` 和 `runtime-evidence.json`。

加载器来源：KintaroModding/ue4ss 的 `v3.0.1-1195-g1ceefac8` PBZ 发行包，附原始 LICENSE。源码与发行信息：<https://github.com/KintaroModding/ue4ss/releases/tag/v3.0.1-1195-g1ceefac8>。

## 已知限制

- 运行时截获本地玩家 Enhanced Input 游戏输入。Slate/CommonUI、原生窗口快捷键和不经过该输入系统的功能需要各自入口，未实现全局覆盖。任意原生函数、任意碰撞和判定不能仅靠返回 hook 全部键位增强。
- 自定义函数、属性与回调直接操作游戏状态，需要正确类型和时机。只有实际测试过的目标才有运行证明；目录中的其他能力没有逐个验收。能力调用返回 false 表示游戏拒绝激活。
- Lua 参数表只能写反射可见的结构字段。`GameplayTagContainer.GameplayTags` 在当前构建不能通过普通参数表有效填充；需要传已有原生容器或使用接受单个 GameplayTag 的游戏 helper。`LiveSpecialDash.lua` 使用后一种入口验证临时天赋并清理。
- 按游戏帧采样，短于采样间隔的按下／释放可能丢失；多键同帧的原生状态先后顺序仍影响能力调度。
- 原生上下文由游戏继续管理。额外输入的注入、选择性屏蔽与采样时序尚需实机验证，特殊菜单、过场、复杂 chord blocker、切换角色和设备行为仍需专门覆盖。
- 两个配置目标若指向同一个 InputAction，底层仍共享该动作的原生状态；不同 target 名不能创建独立的原生动作实例。
- 修改 IA_Deflect／IA_Stance 的输入不能自动拆开能力内部的架势激活与释放状态机；彻底只防御不架势还需改对应能力逻辑。
- 架势瞬身仍受天赋、杀气、冷却、受击、动作优先级等原资格约束；资源迁移不等于每个状态均可激活。游戏更新后需重新提取和验证资源及反射入口。
