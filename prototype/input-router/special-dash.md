# 架势瞬身移到跳跃输入

`special_dash.py` 构建两个资源覆盖包：将 `AIM_Hun_Dash` 中的架势闪避组迁移到 `AIM_Hun_Jump`。默认键位下，LB＋RB 不再进入该组，按住 LB 再按 A 请求瞬身；单独 RB 仍走原闪避，单独 A 仍走原跳跃。组合跟随当前防御、跳跃绑定，不硬编码物理 LB、A。

## 触发链

CL-302256 的 `InputConfig_Hun` 将 `/PBZDeflectSystem/Input/IA_Dodge` 连接到 `AIM_Hun_Dash`，将 `/PBZInput/InputActions/IA_Jump` 连接到 `AIM_Hun_Jump`。`IA_DodgeHold` 在该表中的动作绑定为空。

`AIM_Hun_Dash` 按序包含三个绑定组：完美闪避，优先级 −5；SpecialDash，优先级 −5；普通闪避，优先级 −3。SpecialDash 组包含 11 条路径：普通启动 1 条、连段切换 8 条、中断低优先级动作 2 条。主要条件是地面状态和 `CharacterState.AbilityState.Stance.HoldingStanceKey`，目标是 `Ability.Action.Hun.SpecialDash`；架势防御起始阶段另有 `Ability.Action.Hun.DeflectDash` 路径。

原组合由“闪避输入＋架势状态”产生。仅改 `IA_PerfectDodge` 的物理按键不能消除这个入口。

`BP_SpecialDash_Base` 的 `K2_CanActivateAbility` 检查 `CharacterState.Talent.EnableGhostStep`、受击阶段等资格；激活后进入 `BP_PlayerDodge_Base`。该父类 Ubergraph 在 VM `0x774–0x823` 将 `-Shaqi Cost` 加到 `Shachi`。资源补丁保留这些能力、时间线、碰撞和扣费逻辑。

## 补丁范围

从 Dash 表移除完整的 SpecialDash 组，将其放到 Jump 表首位；迁移后的全部路径都要求保持架势键，包括原 `DeflectDash` 的防御起始分支。Dash 的完美闪避组、普通闪避组和 Jump 原绑定组保持原值。两个包不新增导出或导入，只为 Jump 包追加原 Dash 组使用的名字。

编译器从目标安装读取资源。解析器支持 `ActionAbilityBindingItem`、`ActionAbilityBindingData`、`ActionAbilityTriggerData`、原生 GameplayTagContainer、FName 的 number 和未版本化字段零值。字段关系由当前原生 UHT 注册表确认：`ActionAbilityBindingItem` 构造入口 `0x145e20670`，参数记录 `0x14db9f3b8`；触发结构参数记录 `0x14db9f3f0`。地址均为 CL-302256 的静态 VA。

```powershell
python prototype/input-router/special_dash.py `
  --game-exe $gameExe --paks $paks --oodle $oodle --retoc $retoc `
  --output dist/input-router/stance-dash-on-jump
```

输出 `PBZKeyForge_StanceDashOnJump_P.pak/.utoc/.ucas` 和 `special-dash-report.json`。编译不安装补丁。

## 验证范围

原构建的 114 个 InputActionBinding 资源全部通过字节一致的解析／序列化往返。编译时回读修改后的两个完整 Zen 包，确认 Dash 不再含 SpecialDash／DeflectDash 目标、其余 Dash 组和原 Jump 组不变，迁移路径全部要求保持架势键；容器通过 retoc verify。

CL-302256 游戏内回读确认 Dash 剩余两个原组，Jump 包含迁移组和原组，全部 11 条迁移路径要求保持架势键。加载后的 FName comparison identity 与游戏既有名称相同。

在可操控角色场景中，通过实际 Enhanced Input 注入架势和动作，结果为：

| 场景 | 实际结果 |
| --- | --- |
| 普通跳跃 | Jump tag 激活，高度增加约 122.35 |
| 普通闪避 | Dash tag 激活，位移约 226.42，未激活 SpecialDash |
| 保持架势＋闪避 | Dash tag 激活，未激活 SpecialDash |
| 保持架势＋跳跃 | SpecialDash 实例激活，位移约 145.65，杀气减少约 205.32，未激活 Jump |

测试角色未解锁瞬身天赋，运行验证临时添加 `EnableGhostStep` loose tag，并在结束后移除，计数恢复为零；未写存档。路由器移除原输入上下文后执行这些输入，结束恢复原上下文。此测试证明动作／状态组合的实际调度，未使用实体手柄验证物理 LB、RB、A。

## 已知限制

战技、武器技能、连段、防御起始、杀气不足、冷却和受击状态未逐一完成实战验收。能力拒绝迁移请求时，原跳跃可作为后续候选；当前未启用天赋的测试已观察到回落到 Jump。保持架势仍可能消耗其自身的杀气，旧组合不再瞬身不代表组合期间杀气完全不变。
