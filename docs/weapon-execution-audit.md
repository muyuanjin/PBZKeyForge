# 武器处决与特殊攻击入口

CL-302256 的 40 个主/临时武器配置及全部 46 个影之武装配置，共解出 123 条原生 `ExecutionAnims` 条目。按原生 `PBZExecutionAnimEntry` 的 11 个字段读取真实 Channel、ExecutionType、执行/被执行能力及双方标签条件。逐条数据见 [JSON](weapon-execution-audit.json)。

| 入口 | 条目 | 覆盖 |
| --- | ---: | --- |
| Execution 普通近/远处决 | 91 | 原 IA_Execution / Execution 通道 |
| SpecialAttack 专属攻击 | 3 | 白影突袭、残钢刃破招、断肢锯破招 |
| ShachiWeapon 普通/条件处决 | 28 | 原影之武装输入与 ShachiWeapon 通道 |
| ShachiWeapon 虎炮反击 | 1 | 单独要求 Counter 类型 |

SpecialAttack 的三项分别为白影 `Execution.Type.CombatFar.Assault.BaiYing`、残钢刃 `Execution.Type.Combat.BreakAct.Iron`、断肢锯 `Execution.Type.Combat.BreakAct.Jigsaw`。它们通过原 `GA_PerformSpecialAttack`、`IA_AttackH_Press`、优先级 -5 请求真实目标，并要求已解析类型与绑定一致；普通 Execution 通道无法替代。残钢刃另要求 `CharacterState.WeaponUpgrade.Iron.CanBreakAct`；白影另要求升级及目标冰冻条件。

Execution 的 91 条均为普通近/远成对处决，执行方与被执行方身份均记录，原通道保留目标选择、导航与初始化。4 个临时武器没有自有 ExecutionAnims 条目。

影之武装 29 条全部使用 ShachiWeapon 通道，包含飞刀近/远处决、血滴子普通/煞气处决、游龙叉水下/煞气及互动变体；它们由原通道的实际类型选择与目标条件决定。虎炮 Counter 与普通 Execution 类型不同，专属绑定明确检查 Counter。

## 已知边界

数组按照原生字段 schema 完整解析，并要求结构定位唯一。来源使用现有游戏资源；尚未启动游戏验证。生成入口为 `build/extract-weapon-executions.py`；不把资源条目数当作独立招式数。
