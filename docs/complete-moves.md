# 完整招式

完整招式绑定直接进入指定的出招阶段，省去前置蓄力或连招输入。它与输入动作、能力入口分别保留身份，可以在动作视图或按键详情中添加一条普通按下绑定。

## 已接入

| 功能 | 技术标识 | 游戏入口 |
| --- | --- | --- |
| 天玄青铜剑 · 重攻击 · 蓄力 · 释放 | `BHold · Release2` | 正常激活 `Ability.Action.Hun.Heavy.BronzeSword.BHold` 后，在当前角色的能力实例上调用 `EndSuccess()`。 |
| 轻攻击 · 空中 · 2 | `Air · AA` | 正常激活 `Ability.Action.Hun.Light.Air.AA`，保留空中要求。 |
| 轻攻击 · 空中 · 3 | `Air · AAA` | 正常激活 `Ability.Action.Hun.Light.Air.AAA`，保留空中要求。 |
| 重攻击 · 派生 · N3H3 | `N3H3` | 正常激活 `Ability.Action.Hun.Heavy.ComboAtk_N3H3`，保留地面要求和原生杀气费用。 |
| 火流星 · 攻击 · AAAA · 释放 | `AAAA · Final` | 正常激活 `Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA` 后，确认本次资源强化成功，再调用当前实例的 `ToRelease()`。 |
| 红妆鬼斩 · 重攻击 · 蓄力 · 释放 | `BHold · Release` | 正常激活 `Ability.Action.Hun.Heavy.GuiTouDao.BHold.Start` 后，通过当前实例的原生蓄力成功回调选择强化或未强化释放。 |

青铜剑释放使用 `move:Ability.Action.Hun.Heavy.BronzeSword.BHold.Release2` 作为配置身份。名称沿用原厂武器与动作名称，再附加蓄力和释放阶段。输入目录中的 `BHold` 仍表示原能力入口，`Nsp` 仍表示独立的剑气派生能力。

绑定保留各入口的地面或空中要求、原输入优先级，以及游戏正常激活时的装备、解锁、资源和动作资格检查。带释放入口的能力已在执行时不重入；释放入口在激活前解析，阶段调用失败时取消本次激活，避免留下等待输入的蓄力能力。

## 执行与保存

定义位于 `assets/key-enhancement/complete-moves.json`，记录所属能力、原输入动作、必要状态和释放入口。配置使用 `Kind=move`，编译时解析为已登记的目标，不接受未知身份或强制激活。

自包含的连招阶段通过游戏原生能力入口执行，由当前装备授予的能力决定具体武器版本。带释放入口的招式只从本地玩家的能力系统中查找已授予的实例：激活前确认实例存在且未在执行，正常激活返回后再次确认同一实例处于执行状态，再调用释放入口。能力模板和其他角色的实例不参与阶段调用。

`ActivatableAbilities`、spec 的 `Ability/ActiveCount/NonReplicatedInstances/ReplicatedInstances` 与能力的 `bIsActive` 均为反射属性。CL-302256 的 `SGameLegoAbility` 类注册 `0x145e74900` 经构造跳板进入 `0x157f8a690`，在 `0x157f8a6ec` 将 `InstancingPolicy`（偏移 `0xcf`）置为 `InstancedPerActor`。青铜剑、火流星与红妆鬼斩的完整 Blueprint 继承链未覆盖该字段。引擎 `GiveAbility` 会在授予这类能力时创建实例并加入 spec 的实例数组，因而阶段调用可以在激活前定位已授予的角色实例。

装备标签由玩家能力系统计数。原生装备路径 `0x145ed820b` 读取 `GetEquipTags`，`0x145ed8226` 将返回集合以 `+1` 加入同一能力系统；卸装路径 `0x145ed7870/0x145ed7899` 使用 `-1`。两者均调用 `0x144ca8d90`，与 `AbilityUtils.AddLooseGameplayTagsForSystem` / `RemoveLooseGameplayTagsForSystem` 的原生目标一致。能力系统接收者来自装备组件的弱引用，并用于同一路径的能力授予；因此 `GetGameplayTagCount` 可检查当前装备条件。

键鼠、手柄、JSON、预设分享和 ZIP 使用相同的完整招式身份。新增绑定不写游戏存档。首次使用需要安装包含 `complete-moves.lua` 的新版键位增强；运行中的旧版 Mod 无法加载该依赖时会拒绝配置，保留原有绑定。

## 青铜剑资源依据

Steam CL-302256 的 `GA_Hun_BronzeSword_B_Hold` 在正常激活后进入 `Main`，将 `Ended` 置为 false。`EndSuccess()` 将其置为 true，播放 `Release2`；`EndFail()` 播放 `Release1`。原生 `PlayTimeline` 会结束当前时间线任务后启动新的任务。

该能力列在青铜剑配置的 `Equip Abilities` 中；完整入口还显式检查该配置 `Weapon Equip Tags` 中的 `Weapon.Type.BronzeSword`。

青铜剑 CDO 的 `ActivationRequiredTags` 明确包含 `CharacterState.WeaponUpgrade.BronzeSword.BHold`（schema 索引 82，tag 数据偏移 144）。正常激活保留这一解锁条件；它与 `Nsp` 升级分别登记。

`Release1` 的近战判定使用 `BD_Hun_BronzeSword_HoldB_Release_01_01`。`Release2` 使用 `_02_02`，并包含独立的 `GAA_DelayDeflectProjectileBullet` 动作，引用 `_02_03` 弹体。该动作的 `Charge2` 是弹体权重分组名，不是持续输入条件。

`Main` 从 0.4 秒开始，`MinDuration` 事件位于 0.502267 秒；事件触发后才建立 `IA_AttackH_Hold` 的等待任务。因此，阶段调用紧接正常激活在同一同步调用内执行，先于原持续输入检查。

## 连招阶段依据

`Air.AA` 对应装备配置引用的 `GA_Hun_Air_Tang_AA` 和 `GA_Hun_Air_Double_AA`；`Air.AAA` 对应 `GA_Hun_Air_Double_AAA`；`N3H3` 对应 `GA_Hun_Double_AAABBB`。这些具体类没有本地函数覆盖，继承的激活链会初始化当前角色、身法和落地任务，并启动自身时间线，不读取前一段能力实例，也不等待前置物理输入。`N3H3` 仍经过 `BP_HeavyAttack_Base` 的原生资源处理。

逐装备范围、资源引用与尚未接入的阶段见[武器复查](complete-move-weapon-audit.md)和[影之武装复查](complete-move-shadow-audit.md)。

## 火流星资源依据

`GA_Hun_HuoLiuXing_AAAA.ToRelease()` 直接播放 `Final`。`Main` 和 `Final` 都包含武器展开、拖尾、动画与伤害动作；费用由正常激活链的 `BP_ShachiAbility_Base` 处理，不依赖被跳过的 `Main` 时间线扣除。

入口保留地面状态、`CharacterState.WeaponUpgrade.HuoLiuXing.AAAA` 解锁条件与 `Weapon.Type.ShaqiWeapon.HuoLiuXing` 装备标签。`Weapon_HuoLiuXing` 将该能力列在 `Equip Abilities`，该武器标签列在 `Weapon Equip Tags`。正常激活根据杀气、弹药与原生免费使用资格计算 `Current Shachi Reinforced`；只有当前实例该字段为 true 才进入 `Final`，失败时取消本次能力。原连招中的前段 `AAA` 和前段产生的强化标签由本次原生激活替代。

## 红妆鬼斩资源依据

`GA_Hun_GuiTouBlade_B_Hold_Start` 的正常激活经过 `BP_HeavyAttack_Base` 的费用与强化判定。入口保留地面、`CharacterState.WeaponUpgrade.GuiTouBlade.CanCharge` 和排除 `CharacterState.AbilityState.GuiTouBlade.InBHold` 的原生输入条件，并检查实际配置授予的 `Weapon.Type.GuiTouBlade` 装备标签。Start 和强化释放类都列在配置的 `Equip Abilities` 中；未强化释放类由原生分支按类激活。

完整释放调用当前实例的 `EventReceived_7594CBDE4CE41BEBFC9ABFBA4E2D1DF1`，传入空的 `GameplayEventData`。该回调不读取 payload 内容；它结束本次输入释放等待任务，再按本次 `Current Reinforced` 选择原有强化或未强化释放能力，保留游戏自身的能力切换与费用链。无原重击持键时的 `OnAlreadyRelease` 只返回，不会在激活的同步调用中抢先进入提前释放。

## 已知限制

当前执行路径、时间线和弹体关系已完成静态核对，目录与保存、调度及失败处理已通过本地测试，尚未进行游戏内效果验证。

其他招式的持续资源消耗、强化选择、目标状态和阶段变量需要分别确认。仅有能力标签或时间线名称，不能证明完整出招已经被独立绑定。
