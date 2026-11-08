# 主武器战技阶段覆盖

CL-302256 的 40 个玩家主武器与临时武器配置实际授予 35 个战技类，共 84 条继承合并后的时间线。40 项独立战技阶段包含 18 项终结或原生结束攻击、炽刃三档释放和 19 项内部阶段；每个战技类同时保留装备精确授予类的完整原入口。

独立阶段按可分开的攻击动作、蒙太奇及准备事件区分。同一刀的多次判定、效果弹体和资源条件变体由同一次原生动作处理。正常激活的阶段保留费用与初始化；依赖真实命中、防反、处决或抓取的阶段，只接受当前已进入对应原生时间线的实例。

| 能力 | 原生时间线 | 独立阶段 | 效果及资格 |
| --- | --- | --- | --- |
| `GA_Hun_GuiTouBlade_C` | Main, Failed | 完整原入口 | 单次刀击及花球爆发保留在完整原入口；增益与手动爆炸仍检查真实武器状态。 |
| `GA_Hun_FengLeiJian_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_BaiYing_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_BronzeSword_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_Chaos_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_ChiZheCanJian_C` | Main, Failed | Stage2 | 二段保留备坛、饮药与抛坛事件。 |
| `GA_Hun_ChiZheCanJian_C_ToZui` | Main, Failed | Release | 抛坛等待真实饮药事件及 Variables.JiuTan 原备弹。 |
| `GA_Hun_ChiZheCanJian_C_Zui` | Main, Failed | Final | 终结保留两次攻击与醒酒事件。 |
| `GA_Hun_Double_C` | Main, Failed | Followup | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_YuuBlade_C` | Main, Failed, Exit | Final | CriticalBuff 选择 Main / Exit，终结等待原生 Exit。 |
| `GA_Hun_DualHook_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_HeiMoShang_C1` | Main, Failed, Exit | Final | Climax 或 HeiMoShang.Buff 选择 Exit，终结保留该选择。 |
| `GA_Hun_Horizontal_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_Iron_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_Jade_C_Range` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_Double_JueYing_C` | Main, Failed, NearTimeline | Crow | Main / NearTimeline 由真实锁定目标距离选择；保留乌鸦生成与脱离事件。 |
| `GA_Hun_Double_JueYing_C_Air` | Main, Failed | Crow | 保留乌鸦生成、地面检测与脱离事件。 |
| `GA_Hun_ChiRen_C` | Main, Failed, NewTimeline, Release_01, Release_02, Release_03 | Release1、Release2、Release3 | 真实保持输入与 ChargeCount 选择三档释放；不写入蓄力数。 |
| `GA_Hun_Long_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_Jigsaw_C` | Main, Failed, HasSawHit | Final、HasSawHitStage2、HasSawHitFinal | HasSawHit 阶段只接受原 DirectHit 跳转后已在执行的真实实例。 |
| `GA_Hun_LuanYing_CA` | Main, Failed | Stage2 | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_LuanYing_P2_CA` | Main, Failed | Stage2、Final | 二段与终结保留 TianMiaoShown 持续标签。 |
| `GA_Hun_PoJun_C` | Main, Failed | Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_ShuangYue_C` | Main, Failed | Spin、Final | 十四处攻击的普通 / 强化 / 超强化三组条件弹体完整保留。 |
| `GA_Hun_SlitheringSword_C` | Main, Failed, Perfect | Stage2、PerfectFinal | Main / Perfect 保留真实防反事件与目标弹体；Perfect 终结只复用原生活跃分支。 |
| `GA_Hun_SnakeDual_C` | Main, Failed | Final | 正常初始化保留 StartRevive 事件。 |
| `GA_Hun_Tang_C` | Main, Failed | Followup | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_WoodenSword_C` | Main, Failed | Followup | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_Young_C` | Main, Failed | Followup | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_XieShan_C` | Main, Failed | 完整原入口 | 单次斩击与紧随的随机属性获取保留在完整原入口；0 时刻霸体效果不跳过。 |
| `GA_Hun_YanYueDao_C` | Main, Failed, NormalEnd, Reaction_L1, Reaction_R1 | NormalEnd | ToEnd 结束原 Tick / 等待任务并进入带伤害的 NormalEnd；Reaction 沿原生反应分支。 |
| `GA_Hun_ZhanMa_C` | ExecutionTimeline, GrabSuccess, Failed, GrabSuccess_NonMeat | NormalFinal、GrabFinal | 终结只复用真实处决 / 抓取分支；抓取终结等待原恢复生命事件；非肉体分支走完整原入口。 |
| `GA_Hun_ZiMingJian_New_C` | Main, Failed | 完整原入口 | 单次弹体攻击、命中闪避事件监听与结束输入缓冲保留在完整原入口。 |
| `GA_Hun_ZiMingJian_New_CC` | Main, Failed | Stage2、Final | 保留原初始化、战技费用资格、各次攻击及结束清理。 |
| `GA_Hun_ZiMingJian_New_CCC` | Main, Failed | 完整原入口 | 单次攻击及地面探测、IK 状态切换保留在完整原入口。 |

## 炽刃释放

正常激活 `GA_Hun_ChiRen_C` 经 `BP_CombatArt_Base` 支付战技费用并初始化 `CanRelease=false`、`ChargeCount=0`。保持真实 `IA_CombatArt_Hold` 时，Main 的 `GainCharge` 事件递增计数；`ToRelease()` 按 0 / 1 / 2或3 分别进入 `Release_01` / `Release_02` / `Release_03`，使用不同的 `BD_GA_Hun_ChiRen_C_01/02/03` 弹体。

独立绑定先准备保持输入，按本次实例的时间与真实 ChargeCount 等待对应档位，再调用原 ToRelease。前两档错过窗口会取消本次请求。激活拒绝、上下文重置及取消会释放虚拟保持输入；正常释放后保留原生攻击生命周期。

## 分支与参数

绝影远近分支、灵蛇软剑防反分支使用原生当前 Timeline 对象进入对应阶段，各分支保留自己的攻击边界。阶段伤害、资源绕过及独立前后摇倍率属于本次能力实例，结束后恢复；原生后续与弹体沿用本次参数。抓取终结与防反终结不会制造目标、命中或防反事件。

痴者残剑抛坛等待饮药事件和实际已初始化的酒坛；绝影从乌鸦生成之前进入后段；双钺保留三种资源资格的全部条件弹体；斩马抓取终结在原恢复生命事件之后进入。这些效果与对应攻击共用原生回调。

资源证据见[原战技终结](complete-move-combat-art-recipes.json)、[炽刃释放](complete-move-weapon-charge-recipes.json)和[内部战技阶段](complete-move-additional-weapon-phases.json)。原容器检查为 `CombatArtPhaseResourceTests.py`、`WeaponChargeMoveResourceTests.py`、`AdditionalWeaponPhaseResourceTests.py`；编译后的真实执行器与绑定参数检查为 `CompiledWeaponChargeMoveTests.lua`、`CompiledWeaponPhaseTests.lua` 和 `FinisherSpeedTests.lua`。

## 已知问题

资源与字节码核对属于静态证据，Lua 检查使用模拟原生对象；尚未进行实际游戏输入、动画和最终战斗效果验证。
