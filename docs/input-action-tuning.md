# 输入来源与参数

原生滚轮、触板或其他保留给游戏处理的映射，不会取消同一 InputAction 增强绑定的资源绕过、伤害倍率、前后摇倍率、保护和释放限制设置。激活时只检查实际参与本帧输入的原生来源；存在原生参与时，该次共享合成使用整组默认参数。原生来源闲置后，下一次增强输入立即恢复绑定自己的参数。

配置中的原生映射可以将重击等战斗输入改为滚轮，因此这项规则也适用于战斗 InputAction。来源检查使用运行时实际按键，已屏蔽的原生映射不参与判断。

## 触发与参数归属

无触发器的原生映射读取玩家控制器实际原始按键向量。带触发器的映射使用本玩家、本引擎帧真实执行的 `UpdateState` 返回值，按原生显式、隐式和阻断结果组合。未识别或仍在识别中的手势不算完成输入；松开类触发器可以在原始值为零时正常参与。观察不再次执行触发器，也不改变其原始返回值。

原生观察记录与具体触发器对象关联，映射重建会清除旧记录。解除增强时恢复原始虚函数入口。玩家与帧身份在原生查询中同时匹配，其他玩家、克隆触发器和上帧状态不会取得当前绑定的参数归属。

## Steam CL-302256 依据

`UpdateState` 的真实反射参数是 `PlayerInput`、按值 `FInputActionValue`、`float DeltaTime`，返回 `ETriggerState`。反射 thunk `0x14489B240` 调用虚槽 `0x2B8`；`GetTriggerType` thunk `0x14343E2B0` 调用虚槽 `0x2B0`。

实际 `FTriggerStateTracker::EvaluateTriggers` 位于 `0x14487D1B0`。它按解析出的函数标记选择 Blueprint 的 `ProcessEvent` 路径或原生实现；原生路径在 `0x14487D289` 直接调用虚槽 `0x2B8`。该路径不会经过普通 UFunction hook，因此观察安装在真实原生虚槽。原生循环随后调用类型虚槽 `0x2B0` 和 `IsBlocking` 虚槽 `0x2C8`，观察使用相同实现计算类型与阻断状态。

20 个原厂输入上下文的 115 个触发器对象全部属于 11 个 `/Script/` 原生类，包括触板手势类 `/Script/ProjectPBZ.PBZInputTriggerDefaultTouchpadSwipe`。`InputTriggerNativeResourceTests.py` 从本机可执行文件和 cooked 资源重新核对这些签名、调用路径及对象类。

## 验证边界

`InputActionProvenanceTests.lua` 通过实际能力输入转移 hook 验证参数选择、滚轮参与、手势识别、松开触发、组合条件、帧与玩家隔离、映射重建及恢复。`BindingTuningLifecycleTests.lua` 验证所选参数实际进入费用、伤害和时间线状态。原生测试通过 Lua 导出调用与真实虚函数验证观察和清理。这些验证没有启动游戏，尚不构成实机战斗效果验证。
