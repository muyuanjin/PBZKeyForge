# 防御输入与战斗行为

原厂逻辑的证据来自 Steam 1.0302256 / CL-302256 的输入资源、原生机器码与 Blueprint 字节码，取证位置见文末。

## 先确定修改层

增加、替换或移除防御按键时，继续使用 `IA_Guard`，按[键位格式与编辑](input-bindings.md)处理目标方案及设备。`IA_Deflect` 使用 Pressed、`IA_Stance` 使用 Down，但它们共用 `IA_Guard` 可重绑名称；游戏以 `Unspecified` 查询这个名称下匹配基本按键类别的全部槽。保存槽和编辑列都不提供两个 InputAction 的独立开关。

用户要求“点按防御，不因按住而保持架势”时，需要检查资源及能力逻辑。`IA_Deflect.Triggered` 会设置 Passive 的按下状态、刷新完美防御窗口并激活架势能力；现有释放入口来自 `IA_Stance.Completed`。架势能力又维护自己的按下状态，并据此进入持续架势。实现分离须同时处理输入来源与释放事件、Passive 的状态与激活、架势能力的状态及动画循环；只移除 Stance 输入会丢失释放入口，只改 Passive 状态也无法消除能力内部的持续状态。

## 连点完美防御的诊断

原厂 `BP_PlayerDeflectPassive.Give Perfect Window` 使用共用计数。相邻防御点按不足 0.4 秒时递增，达到冷却后重置；每次点按都会更新最后点击时间。第二次使用较低倍率，连续第三次起不再刷新完美窗口；成功 Perfect 或 PerfectCounter 会触发计数与最后点击时间的重置。

攻防同键使攻击点击也进入防御计数。新增独立防御键不会新建计数，左键仍不断触发防御时，换用另一个防御键不能排除连点惩罚。对照时移除攻击键上的防御，或停止所有防御输入至少 0.4 秒后单次点按；记录游戏构建、实际方案、点击间隔、装备/天赋、战斗状态和结果。

窗口按配置的 30 Hz 逻辑帧率换算，不能使用显示刷新率或渲染 FPS。基础帧数与倍率随难度变化；饰品和破防状态也会影响帧数。惩罚时长不大于零时保留旧 PerfectWindow，不代表立即清除尚未结束的窗口。`NormalWindow` 是代码字段名，不能直接等同于普通架势的全部有效时间；窗口恢复也不保证满足其他完美防御条件。

解除连点窗口惩罚需要独立的战斗资源 Mod。本工具仅把五个难度配置 `GlobalConfig_Combat_{Easy,Normal,Hard,Nightmare,Handicapped}` 中的 `Deflect Quickclick Punish Cooldown` 从 `0.4` 改为 `0`，使原逻辑在每次点击时重置连点计数。只把倍率数组全部改为 1 不充分，因为计数越界仍返回零。此修改影响战斗规则，不能通过存档改键或拆分 Stance 名称代替。

每包只改一个 double，其余字节保持原样，Combined getter、Passive、Base、Player 和其他战斗函数均不变。难度、饰品、破防状态与逻辑帧率继续决定每次动态基础窗口，普通防御和武器资格也保留原规则。主页面勾选“完美防御无连点惩罚”后应用安装，取消勾选后应用移除，默认关闭。范围和验证边界见[资源 Mod](../../../docs/perfect-deflect-mod.md)。

## 输入资源改名后的模型

将 Stance 可重绑名称改为 `IA_Guard_0` 可使两个 InputAction 查询不同按键集合；它只分离可重绑身份，不完成上述能力状态和释放逻辑的分离。FName 的 number 也是最终名称的一部分。

工具支持修改后的输入资源时，从实际资源重新提取身份、按注册顺序分配默认槽，并据此初始化手柄家族记录。原 Guard 的保存覆盖和新 Stance 身份分别处理，预设、恢复与有效关系查询使用同一份修改后数据；不能沿用原厂 Guard 的槽位表并仅改显示名称。只取消连点惩罚的数值补丁不需要重建输入身份。

## 验证边界

上述原厂机制只对 CL-302256 完成静态核对，旧版及后续版本需重新确认。输入身份分离原型完成了容器校验和独立回读，未验证游戏挂载及玩法效果。战斗补丁另行验收；格式往返、容器有效、文件已安装、游戏实际加载和战斗行为不能互相代替。

## 取证位置

原生地址为此构建的 VA：映射重建调用 `0x144888b6b`，profile vtable `0x14b576368`，查询入口 `0x14487fdd0`，按名称遍历 `0x144887c80`，匹配谓词 `0x14487b3a0`。查询入口在 `0x14487fe30` 写槽 7，谓词在 `0x14487b491–0x14487b499` 跳过槽号过滤。

`/PBZDeflectSystem/Abilities/BP_PlayerDeflectPassive` 的 `Give Perfect Window` 在函数内 VM 偏移 `0x0067–0x0100`、`0x073c–0x0781` 处理计数，`0x0492–0x054a` 按配置帧率换算 NormalWindow，`0x05b8–0x06bf` 仅在惩罚后时长大于零时刷新 PerfectWindow。该 Passive 的 Ubergraph 从 `IA_Deflect.Triggered` 的 `0x000f` 进入，在 `0x00c2` 设置按下状态，`0x01ea` 刷新窗口，`0x01f8` 激活架势能力。

窗口与冷却来自 `/PBZCombatAsset/Config/GlobalConfig_Combat_*`，逻辑帧率来自 `GlobalConfig_Mechanic`，各难度 Combined 配置读取对应字段。`/PBZInput/IMC_Deflect` 的 `PlayerMappableKeySettings_2`、`_3` 属于 Stance；身份分离原型只修改两者 FName 的 number，原厂按键、触发器和修饰器保持不变。
