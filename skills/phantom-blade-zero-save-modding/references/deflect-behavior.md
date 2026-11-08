# 防御输入与战斗行为

原厂逻辑的证据来自 Steam 1.0302256 / CL-302256 的输入资源、原生机器码与 Blueprint 字节码，取证位置见文末。

## 先确定修改层

增加、替换或移除防御按键时，继续使用 `IA_Guard`，按[键位格式与编辑](input-bindings.md)处理目标方案及设备。`IA_Deflect` 使用 Pressed、`IA_Stance` 使用 Down，但它们共用 `IA_Guard` 可重绑名称；游戏以 `Unspecified` 查询这个名称下匹配基本按键类别的全部槽。保存槽和编辑列都不提供两个 InputAction 的独立开关。

用户要求“点按防御，不因按住而保持架势”时，需要检查资源及能力逻辑。`IA_Deflect.Triggered` 会设置 Passive 的按下状态、刷新完美防御窗口并激活架势能力；现有释放入口来自 `IA_Stance.Completed`。架势能力又维护自己的按下状态，并据此进入持续架势。实现分离须同时处理输入来源与释放事件、Passive 的状态与激活、架势能力的状态及动画循环；只移除 Stance 输入会丢失释放入口，只改 Passive 状态也无法消除能力内部的持续状态。

增强运行时对独立 Deflect 输出生成一帧 Deflect 与 Stance 的原厂输入脉冲，随后通过 Stance.Completed 释放两层状态。默认持续按住时按帧交替按下与释放，循环发起点按防御，松开立即停止追加；显式单次触发及连发间隔保留各自语义。独立 Stance 跟随按住与释放。普通 IA_Guard 同时包含两者，仍保留原厂长按。运行时不写两层窗口字段、不新增计时器；回归以普通 IA_Guard 连续短按的输入及状态轨迹为对照，并覆盖键鼠、手柄、重叠来源与取消。该模型验证不代替游戏内的防御判定与动画实测。

点防的防御状态与武技派生资格分别处理。原生 Passive 的按下／释放入口同时增减 `HoldingStanceKey`，能力树通过 `CharacterState.AbilityState.Stance.HoldingStanceKey` 判断武技与部分架势派生。运行时从各输出的原始 `actions` 集合区分来源：独立 Deflect 的配对 Stance 只负责防御释放，不提供该 tag；显式 Stance 与普通 IA_Guard 的 Stance 则提供持续架势资格。C# 目录、配置与分享使用相同的独立单动作及原 Guard 双动作身份。

每帧在注入输入前聚合有效架势来源，原生桥只同步本地角色 Passive 所拥有的这份 `HoldingStanceKey` 贡献，并在该 Passive 的按下／释放调用内抑制同一 ASC、同一 tag 的重复增减。其他角色、其他能力和其他 tag 继续沿原逻辑执行。点防的两层防御按下状态、NormalWindow、PerfectWindow 和防反流程保持原生行为。独立架势与点防重叠时保留武技资格；松开架势而仅保留点防后移除该资格。原 Guard 同时按住时继续提供资格。暂停与配置切换清理失效来源；运行时归还原生执行时按当前 Passive 状态恢复原生 tag 贡献关系。

架势贡献遵守原厂 `Break Forbid Stance` 资格：破防禁止架势时不新增 `HoldingStanceKey` 贡献；此前已合法建立且仍持续按住的贡献保留，直到相应架势来源释放。

同键攻防按输入来源验收：独立 Deflect 与普通轻／重攻击同时绑定一个键时，保留原生攻击输入和点防窗口，不因该点防提供架势组合武技资格；普通 IA_Guard 或独立 Stance 与攻击同键时仍提供该资格。绑定中的输出顺序不改变结果。白蟒赤练架势重击 `SnakeDual.DB` 的三个攻击路由均要求 `HoldingStanceKey`；`Ability.Action.Stance` 可接入的 `SnakeDual.AB` 属于普通重击连招，成功防御后的铜剑追击与拼刀强化也保留各自原生条件。

多个独立 Deflect 来源按共享原生动作统一安排释放帧，不按每条绑定分别交替。释放帧内新到达的显式单次请求延至下一帧执行一次；持续来源仍在松开后停止追加。独立 Stance 和普通 Guard 的持续贡献不受脉冲释放影响，重置或取消会清理待执行脉冲。

从菜单、失焦或配置重载恢复时，仍按住的独立 Stance 或普通 IA_Guard 恢复持续架势，IA_Deflect 保留等待新按下的语义，不额外刷新完美窗口。独立 Deflect 的配对 Stance 属于同次点按脉冲，也等待新按下；不能按持续架势自动恢复该脉冲。架势的持续恢复资格与 Controller 的角色移动屏蔽分别处理。

原厂释放后的检测不只依赖架势能力的 held Tick。Passive 的 `Can Activate Ability On Frame` 在 `Stance Key Down || Normal Window > now` 时仍允许激活；Passive Tick 和接收攻击帧均调用 `Try Activate Stance Ability On Frame`。玩家架势能力的 `On Receive Frame` 对非 HoldDeflect 攻击继续调用 `Try Deflect`，后者同样允许正常窗口尚未结束的松键状态，并把 NormalWindow 作为 MaxImpactTime。`On Stance Key Release` 与能力的 `On Stance Key Up` 均不清除窗口。因此独立点按沿用正常短按的窗口与后续检测，不因下一帧释放而取消已有窗口；持续架势及 HoldDeflect 等特殊攻击仍由原厂能力分支处理。

## 连点完美防御的诊断

原厂 `BP_PlayerDeflectPassive.Give Perfect Window` 使用共用计数。相邻防御点按不足 0.4 秒时递增，达到冷却后重置；每次点按都会更新最后点击时间。第二次使用较低倍率，连续第三次起不再刷新完美窗口；成功 Perfect 或 PerfectCounter 会触发计数与最后点击时间的重置。

攻防同键使攻击点击也进入防御计数。新增独立防御键不会新建计数，左键仍不断触发防御时，换用另一个防御键不能排除连点惩罚。对照时移除攻击键上的防御，或停止所有防御输入至少 0.4 秒后单次点按；记录游戏构建、实际方案、点击间隔、装备/天赋、战斗状态和结果。

窗口按配置的 30 Hz 逻辑帧率换算，不能使用显示刷新率或渲染 FPS。基础帧数与倍率随难度变化；饰品和破防状态也会影响帧数。惩罚时长不大于零时保留旧 PerfectWindow，不代表立即清除尚未结束的窗口。`NormalWindow` 是代码字段名，不能直接等同于普通架势的全部有效时间；窗口恢复也不保证满足其他完美防御条件。

解除连点窗口惩罚需要独立的战斗资源 Mod。本工具仅把五个难度配置 `GlobalConfig_Combat_{Easy,Normal,Hard,Nightmare,Handicapped}` 中的 `Deflect Quickclick Punish Cooldown` 从 `0.4` 改为 `0`，使原逻辑在每次点击时重置连点计数。只把倍率数组全部改为 1 不充分，因为计数越界仍返回零。此修改影响战斗规则，不能通过存档改键或拆分 Stance 名称代替。

每包只改一个 double，其余字节保持原样，Combined getter、Passive、Base、Player 和其他战斗函数均不变。难度、饰品、破防状态与逻辑帧率继续决定每次动态基础窗口，普通防御和武器资格也保留原规则。主页面勾选“完美防御无连点惩罚”后应用安装，取消勾选后应用移除，默认关闭。范围和验证边界见[资源 Mod](../../../docs/perfect-deflect-no-rapid-click-penalty-mod.md)。

## 输入资源改名后的模型

将 Stance 可重绑名称改为 `IA_Guard_0` 可使两个 InputAction 查询不同按键集合；它只分离可重绑身份，不完成上述能力状态和释放逻辑的分离。FName 的 number 也是最终名称的一部分。

工具支持修改后的输入资源时，从实际资源重新提取身份、按注册顺序分配默认槽，并据此初始化手柄家族记录。原 Guard 的保存覆盖和新 Stance 身份分别处理，预设、恢复与有效关系查询使用同一份修改后数据；不能沿用原厂 Guard 的槽位表并仅改显示名称。只取消连点惩罚的数值补丁不需要重建输入身份。

## 验证边界

上述原厂机制只对 CL-302256 完成静态核对，旧版及后续版本需重新确认。输入身份分离原型完成了容器校验和独立回读，未验证游戏挂载及玩法效果。战斗补丁另行验收；格式往返、容器有效、文件已安装、游戏实际加载和战斗行为不能互相代替。

## 取证位置

原生地址为此构建的 VA：映射重建调用 `0x144888b6b`，profile vtable `0x14b576368`，查询入口 `0x14487fdd0`，按名称遍历 `0x144887c80`，匹配谓词 `0x14487b3a0`。查询入口在 `0x14487fe30` 写槽 7，谓词在 `0x14487b491–0x14487b499` 跳过槽号过滤。

`/PBZDeflectSystem/Abilities/BP_PlayerDeflectPassive` 的 `Give Perfect Window` 在函数内 VM 偏移 `0x0067–0x0100`、`0x073c–0x0781` 处理计数，`0x0492–0x054a` 按配置帧率换算 NormalWindow，`0x05b8–0x06bf` 仅在惩罚后时长大于零时刷新 PerfectWindow。该 Passive 的 Ubergraph 从 `IA_Deflect.Triggered` 的 `0x000f` 进入，在 `0x00c2` 设置按下状态，`0x01ea` 刷新窗口，`0x01f8` 激活架势能力。

窗口与冷却来自 `/PBZCombatAsset/Config/GlobalConfig_Combat_*`，逻辑帧率来自 `GlobalConfig_Mechanic`，各难度 Combined 配置读取对应字段。`/PBZInput/IMC_Deflect` 的 `PlayerMappableKeySettings_2`、`_3` 属于 Stance；身份分离原型只修改两者 FName 的 number，原厂按键、触发器和修饰器保持不变。
