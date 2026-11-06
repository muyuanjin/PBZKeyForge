# 条件反击与原生执行上下文

CL-302256 的虎炮反击使用原生 `ShachiWeapon` 处决通道。绑定只在 `ExecutionComponent.GetResolvedExecutionType(FName("ShachiWeapon"))` 返回 `Execution.Type.Combat.ShachiWeapon.TigerCannon.Counter` 时请求原通道；普通虎炮处决类型不匹配。完整记录见 [资源证据](complete-move-counter-contexts.json)。

升级效果实际授予并激活 `GA_TigerCannon_CounterPassive`。该原生被动订阅玩家的真实防御组件，读取未禁用、未消费的防御帧，检查弹体 `CanDodgeAfterDeflect` 或 `RedEye` 标签，从真实伤害源取得目标，维护 `TigerCannon.CanBeExecutionTarget` 并发送 `GameplayEvent.Execution.ForceRefresh`。被动结束会移除自己维护的目标标签。

原通道能力继续执行 `CanPerformExecution(Channel)`、角色状态判断和 `PerformExecution(Channel)`。随后 `GA_ExecutionAbilityBase.InitializeExecutionInfo` 从组件的 `CurrentExecutionInfo` 初始化真正的执行者、目标和时间线。直接请求 `TigerCannon.Counter` 能力标签不能建立这份上下文。

原 `ExecutionTimeline` 在 0.180128 秒执行 `GAA_ShachiBulletInstant`，其 `ExplicitCost=1`；动作保留原生 `SpiritShadow` 扣费及力量专精、免耗天赋修饰。0.202248 秒生成原 `B_TigerCannon_Counter` 弹体。反击能力沿原生命周期设置并恢复临时锁定，不跳过初始化、费用或结束清理。

雷枪反击通过原 `ThunderSpear.DeflectCounter` 能力正常激活，普通反击时间线自行订阅真实完美闪避事件并切换对应阶段。其资源、费用和资格见 [影之武装配方](complete-move-shadow-recipes.json)。

白影突袭同样继承 `GA_ExecutionAbilityBase`，武器 `ExecutionAnims` 的实际条目指定 `SpecialAttack` 通道及 `Execution.Type.CombatFar.Assault.BaiYing` 类型，要求已装备白影、`CanAssault` 升级与目标处于冰霜状态。绑定通过原重攻击输入，以原优先级 −5 请求 `GA_PerformSpecialAttack`，只接受这个真实类型。原突袭能力读取已初始化的执行目标、播放原攻击，并向原锁定目标施加 `GE_BaiYing_AssaultTriggered`。

白蟒赤练 `A_Deflect` 是装备直接授予的独立轻攻击类。其激活只进入 `BP_LightAttack_SnakeDualCombo_Base`，由原轻攻击基类播放 `Main` 并设置连段倍率效果；无需处决目标或外部事件载荷。绑定按精确原类正常激活，保留 `SnakeDual.Normal` 武器形态与原生资格。原输入表中的 `A_Deflect -> AA` 只把它用作连段前驱，没有直接触发它的 `TriggerTag`，因此初始输入入口目录没有列出该攻击。

别离残钩抓取属于 `DB` 的自动反击分支。原 `Impact` 事件取得真实弹体和伤害源；非投射弹体具备 `CanDodgeAfterDeflect` 或 `PowerAttack`，且真实目标为非驾驶中的人形角色时，原能力自动进入抓取。`DB` 继续监听 `RequestGrabTarget`，把真实防反目标交给 `GEx`；`GrabSuccess` 只由原生 `HitGrab.ToGrabSuccess` 事件触发。原 `GA_ExecutionAbilityBase_Grab` 开始尝试时清空旧执行信息，成功后再刷新实际抓取数据。该分支保留在原 `DB` 内，不提供跳过真实防反事件的直接抓取绑定。

## 验证范围

原生代码、反射签名及安装包资源为静态证据。资源检查核对授予、通道、上下文读取和费用/攻击动作；Lua 测试核对编译后的绑定在类型不匹配、无执行组件、原通道拒绝和真实反击类型之间的行为。尚未启动游戏验证实战表现。
