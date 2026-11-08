# 招式绑定附加效果

增强绑定编辑器在前后摇倍率上方提供“附加霸体效果”和“附加无敌效果”。两项独立勾选，默认关闭，随当前绑定保存；复制、预设分享、JSON 和 ZIP 保留设置。普通招式、输入触发的战斗能力及完整招式使用同一套激活归属。

霸体增加原生 `CharacterState.Hitreact.Endure.Stand.Ultimate` 状态，无敌增加原生 `CharacterState.Stand.Invulnerable.Debug` 状态。运行时通过 `AbilityUtils.AddLooseGameplayTagForSystem` 为本次激活各增加一个计数，结束、取消、激活失败及运行时清理时通过对应 Remove 函数移除该计数。其他能力、装备或原厂招式提供的同名状态不清零。原生后续招式继承本次绑定的选择，其他绑定沿用自己的设置。

效果复用现有资源与倍率的能力实例追踪。稳定帧不查询或重写效果标签，不新增逐帧钩子；只在激活和清理时更新计数。角色的杀气、减伤属性及原始 GameplayEffect 模板不改动。

## 原厂依据

静态资源与原生程序证据来自 Steam CL-302256：

- `GE_Endure_Ultimate_Temporary` 的 Target Tags 包含 `CharacterState.Hitreact.Endure.Stand.Ultimate`，同时还配置 `Shachi_Min` 和物理减伤属性。附加霸体只使用状态标签，不附带这些数值修改。
- 原生受击抗打断查询 `0x147e02d60` 在检测到 Ultimate 状态时返回最高分支值 9；该标签的原生存储地址为 `0x14e0cd888`。受击处理在 `0x147d8a22c` 直接检查同一状态。
- `GE_FlowInvulnerable` 的 Target Tags 包含 `CharacterState.Stand.Invulnerable.Debug`。`BPL_PBZCombatUtils.Get Invulnerable Tags` 将该状态列入原厂无敌集合，原生集合初始化 `0x147e48ced` 也包含该标签。
- 两个 ForSystem 函数的原生参数为 `AbilitySystem`、`GameplayTag`、`bShouldReplicate`，计数增减与装备标签使用相同原生能力系统入口。

## 验证与边界

配置、界面和生命周期测试覆盖独立选项、分享与导出、原生后续、同键不同绑定、失败与取消、重叠计数、原有状态保留及清理重试。静态取证和模型回归不代替游戏内受击验证。

效果持续时间跟随实际能力实例，不提供固定秒数；能力结束由现有帧调度观察并清理。无敌沿用游戏原生伤害与命中流程，不改写剧情、场景或其他脚本直接执行的状态变化。
