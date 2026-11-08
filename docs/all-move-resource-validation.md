# 招式资源覆盖核对

依据 CL-302256 的 cooked 能力、弹体和原生 UHT/函数派发数据。本次参数属于实际激活的绑定；同类能力的其他绑定、原操作和已发射弹体各自保留参数。资源绕过使用满资源的释放分支和效果，不填充真实资源。

| 资源/费用 | 实际处理入口 | 保留的其他行为 |
| --- | --- | --- |
| 精纯杀气、杀气、影之武装弹量 | 当前玩家 AttributeSet 的对应上限；本次能力的 Blueprint 属性读取、费用和按资源比例计算效果 | 普通增益、生命修改、其他能力的资源和资格 |
| 纯杀气 GameplayEffect | 灵蛇强化连击、混沌切换、全局大招杀气清空、紫冥剑费用；分别覆盖 Ability 应用、Ability 创建 Spec、ASC 应用 | 有攻击、状态或资源增益的其他效果 |
| 天煞双钺终气 | 当前活跃被动两个 ChargeObject 的 GetMax 之和；原生 ConsumeValue 分档 | 普通版和切换版各自阶段、真实闪避事件、实际终气 |
| 白山风雷剑风、雷能量 | 本次能力实例的 LGAAction 原生条件；BulletEvent_FengLeiConsumeEnergy；风雷 Blueprint 标签分支 | 非资源条件、发射时弹体参数、普通弹体复用、真实能量 |
| 灼臂盾闪避、防反、推击、冲撞费用 | IsShaqiEnough 的实际 Shachi 属性查询；HandleDodgeCost、HandleParryCost、HandlePushCost、HandleRushCost 的负向属性修改 | 吸伤、实际命中/防反和原盾状态 |

白山风雷剑的资源条件涉及弹体、拖尾、音效、震动和镜头，不能只放开 Blueprint 费用。已检查该能力族的 1,112 个 cooked 动作，资源正条件为雷 172 处、风 125 处，资源负条件为雷 90 处、风 61 处。这些条件均序列化在 RequireTags/IgnoreTags 中，没有使用资源 TagQuery。保留的其他标签仍交由原生动作资格检查。

风雷 Hold 的持键准备回调 `Added_1CC942FE4424EBA0DBE0A292363B2DFA` 在检测到满雷时提前退出，实际充能 Tick `OnTick_F49F244647F3D911EDC36F8CD34569D0` 则在检测到满雷时进入 EndSuccess。绕过仅在本次实例的这两个精确上下文分别提供准备和成功释放所需的状态。原生充能、结束任务和释放时间线继续执行；普通费用模式保持真实能量判断。

原生资源桥从当前反射 UFunction 和 PE unwind 函数边界取得 LGAAction.GetAbility 的 owner getter，以及 LegoAbility.ExecuteAction 的 CanActivateAction 虚派发。条件字段与 GameplayTagContainer 布局从反射读取。只拦截已核对的纯能量消费事件；其原生本体仅取得当前风雷被动并调用两个 ChargeObject 消费入口。

`GE_ShieldShaqiCost` 资源包含周期配置与状态标签，但当前灼臂盾能力未引用它；四条实际费用路径均使用属性查询和负向修改。该资源不加入纯费用效果表。

本地验证：ResourceCostTests 848 项、WeaponResourceTests 35 项、ChargeClockTests 121 项；WeaponResourceNativeTests 验证真实 Lua C 调用、动作虚派发、非资源条件、实例隔离、能力结束后的弹体继承、普通复用与清理。ResourceCapacityResourceTests 核对八类资源及盾的四条费用路径，WeaponResourceResourceTests 核对上述原生入口与全部风雷资源分支。

## 已知限制

上述结果属于资源与原生静态核对、本地执行验证，尚未在实际战斗中验收。普通库存道具数量不属于招式资源绕过范围。
