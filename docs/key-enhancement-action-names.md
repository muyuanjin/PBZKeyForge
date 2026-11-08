# 增强功能名称与资源依据

适用游戏资源：CL-302256。共 338 项：98 个输入动作、236 个能力、4 个移动方向。名称和分类对应工具的实际显示。

有明确对应关系的招式使用游戏原文及原厂译名；描述名依据输入分支、能力描述和触发条件。共用能力不归于单把武器，同名招式的独立阶段保留短段码。

输入条目发送指定 InputAction，能力条目激活指定能力标签。绑定一个能力不会自动重放前置连招。持续、长按、设备和方向词用于区分输入分支。

[逐项资源路径、原始描述与条件](key-enhancement-action-names.json)。原厂文本关系见 [action-localization.json](../assets/key-enhancement/action-localization.json)，显示词与分类见 [action-details.json](../assets/key-enhancement/action-details.json)。

## 名称目录

| 名称 | 目标 | 主要依据 |
| --- | --- | --- |
| 向前移动 | `movement:forward` | /SGameLocomotionDriver/Input/IA_KeyboardMoveInput |
| 向后移动 | `movement:backward` | /SGameLocomotionDriver/Input/IA_KeyboardMoveInput |
| 向左移动 | `movement:left` | /SGameLocomotionDriver/Input/IA_KeyboardMoveInput |
| 向右移动 | `movement:right` | /SGameLocomotionDriver/Input/IA_KeyboardMoveInput |
| 键盘移动（二维轴） | `action:/SGameLocomotionDriver/Input/IA_KeyboardMoveInput` | UI_Setting_299 |
| 摇杆移动（二维轴） | `action:/SGameLocomotionDriver/Input/IA_JoystickMoveInput` | UI_Setting_299 |
| 移动轴输入 | `action:/Game/PBZContent/Input/InputActions/IA_MoveInput` | /Game/PBZContent/Input/InputActions/IA_MoveInput |
| 高速移动 · 持续 | `action:/SGameLocomotionDriver/Input/IA_Sprint` | UI_Setting_301 |
| 高速移动 · 切换输入 | `action:/SGameLocomotionDriver/Input/IA_SprintSwitch` | UI_Setting_301 |
| 行走输入 | `action:/Game/PBZContent/Input/InputActions/IA_Walk` | /Game/PBZContent/Input/InputActions/IA_Walk |
| 慢速移动 | `action:/SGameLocomotionDriver/Input/IA_Walk` | UI_Setting_300 |
| 跳跃 | `ability:Ability.Action.Hun.Jump` | AIM_Hun_Jump |
| 跳跃 | `action:/PBZInput/InputActions/IA_Jump` | /PBZInput/InputActions/IA_Jump |
| 下蹲 | `ability:Ability.Native.Crouch` | UI_Setting_401；AIM_Hun_Crouch |
| 下蹲 | `action:/SGameLocomotionDriver/Input/IA_Crouch` | UI_Setting_401 |
| 下蹲 · 键盘 · 内部输入 | `action:/SGameLocomotionDriver/Input/IA_KeyboardCrouch` | UI_Setting_401 |
| 锁定敌人 | `action:/SGameCombatSystem/Targeting/IA_LockTarget` | UI_Setting_305 |
| 下一个 · 锁定目标 | `action:/SGameCombatSystem/Targeting/IA_MouseChangeLockTargetNext` | /SGameCombatSystem/Targeting/IA_MouseChangeLockTargetNext |
| 上一个 · 锁定目标 | `action:/SGameCombatSystem/Targeting/IA_MouseChangeLockTargetPrevious` | /SGameCombatSystem/Targeting/IA_MouseChangeLockTargetPrevious |
| 改变锁定对象 · 鼠标 · 轴输入 | `action:/SGameCombatSystem/Targeting/IA_MouseChangeLockTargetAxis` | UI_Setting_306 |
| 改变锁定对象 · 摇杆 · 轴输入 | `action:/SGameCombatSystem/Targeting/IA_JoystickChangeLockTargetAxis` | UI_Setting_306 |
| 旋转摄像机 · 鼠标 · 轴输入 | `action:/SGameLocomotionDriver/Input/IA_MouseLookInput` | UI_Setting_304 |
| 旋转摄像机 · 摇杆 · 轴输入 | `action:/SGameLocomotionDriver/Input/IA_JoyStickLookInput` | UI_Setting_304 |
| 视角轴输入 | `action:/Game/PBZContent/Input/InputActions/IA_LookInput` | /Game/PBZContent/Input/InputActions/IA_LookInput |
| 轻攻击 · A | `ability:Ability.Action.Hun.Light.A` | UI_Setting_307；AIM_Tang_HorseCartCombat_LightAttack |
| 轻攻击 · 地面 · 拔刀 · 1 | `ability:Ability.Action.Hun.Light.A.Draw` | UI_Setting_307；AIM_Hun_LightAttack；AIM_LionHead_LightAttack；AIM_ShachiHook_LightAttack |
| 轻攻击 · 地面 · 1 | `ability:Ability.Action.Hun.Light.A.Normal` | UI_Setting_307；AIM_Hun_LightAttack；AIM_FengLeiJian_LightAttack；AIM_Double_LightAttack；AIM_DualHook_LightAttack；AIM_LuanYing_LightAttack；AIM_SnakeDual_LightAttack；AIM_YuuBlade_LightAttack |
| 轻攻击 · 地面 · 2 | `ability:Ability.Action.Hun.Light.AA` | UI_Setting_307；AIM_Hun_LightAttack；AIM_FengLeiJian_LightAttack；AIM_BaiYing_LightAttack；AIM_BronzeSword_LightAttack；AIM_Chaos_LightAttack；AIM_ChiZheCanJian_HeavyAttack；AIM_ChiZheCanJian_LightAttack；AIM_Double_LightAttack；AIM_DualHook_LightAttack；AIM_GuiTouBlade_LightAttack；AIM_Iron_LightAttack；AIM_Jigsaw_LightAttack；AIM_Long_LightAttack；AIM_LuanYing_LightAttack；AIM_SlitheringSword_LightAttack；AIM_SnakeDual_LightAttack；AIM_Tang_LightAttack；AIM_TempWeapon_LightAttack；AIM_YanYueDao_LightAttack；AIM_YuuBlade_LightAttack；AIM_ZhanMa_LightAttack；AIM_ZiMingJian_LightAttack |
| 轻攻击 · 地面 · 3 | `ability:Ability.Action.Hun.Light.AAA` | UI_Setting_307；AIM_Hun_LightAttack；AIM_YanYueDao_LightAttack |
| 轻攻击 · 地面 · 4 | `ability:Ability.Action.Hun.Light.AAAA` | UI_Setting_307；AIM_Hun_LightAttack |
| 轻攻击 · 地面 · 5 | `ability:Ability.Action.Hun.Light.AAAAA` | UI_Setting_307；AIM_FengLeiJian_LightAttack；AIM_BaiYing_LightAttack；AIM_Chaos_LightAttack；AIM_ChiZheCanJian_LightAttack；AIM_Double_LightAttack；AIM_DualHook_LightAttack；AIM_Iron_LightAttack；AIM_LuanYing_LightAttack；AIM_SlitheringSword_LightAttack；AIM_Tang_HorseCartCombat_LightAttack；AIM_Tang_LightAttack；AIM_TempWeapon_LightAttack；AIM_YuuBlade_LightAttack；AIM_ZiMingJian_LightAttack |
| 轻攻击 · 地面 · 6 | `ability:Ability.Action.Hun.Light.AAAAAA` | UI_Setting_307；AIM_FengLeiJian_LightAttack；AIM_Double_LightAttack；AIM_DualHook_LightAttack |
| 轻攻击 · 击杀吸附 | `ability:Ability.Action.Hun.Light.Snap` | UI_Setting_307；AIM_Hun_LightAttack |
| 轻攻击 · 按下 | `action:/PBZInput/InputActions/IA_AttackL_Press` | UI_Setting_307 |
| 轻攻击 · 持续 | `action:/PBZInput/InputActions/IA_AttackL_Hold` | UI_Setting_307 |
| 轻攻击 · 长按 | `action:/PBZInput/InputActions/IA_AttackL_PressLong` | UI_Setting_307 |
| 轻攻击 · 冲刺 · 拔刀 · 1 | `ability:Ability.Action.Hun.Light.ADash.Draw` | UI_Setting_307；AIM_Hun_LightAttack；AIM_Mojian_LightAttack；AIM_FengLeiJian_LightAttack；AIM_Double_LightAttack |
| 轻攻击 · 冲刺 · 1 | `ability:Ability.Action.Hun.Light.ADash.Normal` | UI_Setting_307；AIM_Hun_LightAttack |
| 轻攻击 · 空中 · 拔刀 · 1 | `ability:Ability.Action.Hun.Light.Air.A.Draw` | UI_Setting_307；AIM_Hun_LightAttack |
| 轻攻击 · 空中 · 1 | `ability:Ability.Action.Hun.Light.Air.A.Normal` | UI_Setting_307；AIM_Hun_LightAttack |
| 重攻击 · 地面 · 1 | `ability:Ability.Action.Hun.Heavy.Atk01` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_BaiYing_HeavyAttack；AIM_DualHook_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_Tang_HorseCartCombat_HeavyAttack；AIM_TempWeapon_HeavyAttack；AIM_YanYueDao_HeavyAttack；AIM_YuuBlade_HeavyAttack |
| 重攻击 · 地面 · 2 | `ability:Ability.Action.Hun.Heavy.Atk02` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_BaiYing_HeavyAttack；AIM_ChiZheCanJian_HeavyAttack；AIM_DualHook_HeavyAttack；AIM_Horizontal_HeavyAttack；AIM_Iron_HeavyAttack；AIM_Jade_HeavyAttack；AIM_Jigsaw_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_YanYueDao_HeavyAttack；AIM_YuuBlade_HeavyAttack；AIM_ZhanMa_HeavyAttack |
| 重攻击 · 地面 · 3 | `ability:Ability.Action.Hun.Heavy.Atk03` | UI_Setting_308；AIM_FengLeiJian_HeavyAttack；AIM_BaiYing_HeavyAttack；AIM_BronzeSword_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_ChiZheCanJian_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_DualHook_HeavyAttack；AIM_GuiTouBlade_HeavyAttack；AIM_HeiMoShang_HeavyAttack；AIM_Iron_HeavyAttack；AIM_Jade_HeavyAttack；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_ShuangYue_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_Tang_HeavyAttack；AIM_Tang_HorseCartCombat_HeavyAttack；AIM_TempWeapon_HeavyAttack；AIM_XieShan_HeavyAttack；AIM_YuuBlade_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 重攻击 · 地面 · 4 | `ability:Ability.Action.Hun.Heavy.Atk04` | UI_Setting_308；AIM_FengLeiJian_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_DualHook_HeavyAttack；AIM_Jade_HeavyAttack；AIM_ShuangYue_HeavyAttack；AIM_TempWeapon_HeavyAttack；AIM_XieShan_HeavyAttack；AIM_YuuBlade_HeavyAttack |
| 重攻击 · 地面 · 拔刀 · 1 | `ability:Ability.Action.Hun.Heavy.DrawAtk01` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_LionHead_HeavyAttack；AIM_ShachiHook_HeavyAttack |
| 重攻击 · 击杀吸附 | `ability:Ability.Action.Hun.Heavy.Snap` | UI_Setting_308；AIM_Hun_HeavyAttack |
| 重攻击 · 按下 | `action:/PBZInput/InputActions/IA_AttackH_Press` | UI_Setting_308 |
| 重攻击 · 持续 | `action:/PBZInput/InputActions/IA_AttackH_Hold` | UI_Setting_308 |
| 重攻击 · 长按 | `action:/PBZInput/InputActions/IA_AttackH_PressLong` | UI_Setting_308 |
| 重攻击 · 冲刺 · 1 | `ability:Ability.Action.Hun.Heavy.DashAtk01` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_Long_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 重攻击 · 冲刺 · 拔刀 · 1 | `ability:Ability.Action.Hun.Heavy.DrawDashAtk01` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_Mojian_HeavyAttack |
| 重攻击 · 空中 · 1 | `ability:Ability.Action.Hun.Heavy.AirAtk01` | UI_Setting_308；AIM_Hun_HeavyAttack |
| 重攻击 · 空中 · 拔刀 · 1 | `ability:Ability.Action.Hun.Heavy.DrawAirAtk01` | UI_Setting_308；AIM_Hun_HeavyAttack |
| 重攻击 · 派生 · N1H1 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N1H1` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_FengLeiJian_HeavyAttack；AIM_BronzeSword_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_DualHook_HeavyAttack；AIM_GuiTouBlade_HeavyAttack；AIM_HeiMoShang_HeavyAttack；AIM_Horizontal_HeavyAttack；AIM_Iron_HeavyAttack；AIM_Jade_HeavyAttack；AIM_Jigsaw_HeavyAttack；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_PoJun_HeavyAttack；AIM_ShuangYue_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_Tang_HeavyAttack；AIM_XieShan_HeavyAttack；AIM_YuuBlade_HeavyAttack；AIM_ZhanMa_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 重攻击 · 派生 · N1H2 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N1H2` | UI_Setting_308；AIM_FengLeiJian_HeavyAttack；AIM_BronzeSword_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_DualHook_HeavyAttack；AIM_GuiTouBlade_HeavyAttack；AIM_HeiMoShang_HeavyAttack；AIM_Iron_HeavyAttack；AIM_Jade_HeavyAttack；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_ShuangYue_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_Tang_HeavyAttack；AIM_XieShan_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 重攻击 · 派生 · N2H1 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N2H1` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_FengLeiJian_HeavyAttack；AIM_SlitheringSword_HeavyAttack |
| 重攻击 · 派生 · N2H2 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N2H2` | UI_Setting_308；AIM_FengLeiJian_HeavyAttack；AIM_BronzeSword_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_DualHook_HeavyAttack；AIM_GuiTouBlade_HeavyAttack；AIM_HeiMoShang_HeavyAttack；AIM_Iron_HeavyAttack；AIM_Jade_HeavyAttack；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_ShuangYue_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_Tang_HeavyAttack；AIM_XieShan_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 重攻击 · 派生 · N3H1 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N3H1` | UI_Setting_308；AIM_Hun_HeavyAttack；AIM_FengLeiJian_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack |
| 重攻击 · 派生 · N3H2 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N3H2` | UI_Setting_308；AIM_FengLeiJian_HeavyAttack；AIM_BronzeSword_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_DualHook_HeavyAttack；AIM_GuiTouBlade_HeavyAttack；AIM_HeiMoShang_HeavyAttack；AIM_Iron_HeavyAttack；AIM_Jade_HeavyAttack；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_ShuangYue_HeavyAttack；AIM_SlitheringSword_HeavyAttack；AIM_SnakeDual_HeavyAttack；AIM_Tang_HeavyAttack；AIM_XieShan_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 重攻击 · 派生 · N4H1 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N4H1` | UI_Setting_308；AIM_HeiMoShang_HeavyAttack；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack；AIM_YuuBlade_HeavyAttack |
| 重攻击 · 派生 · N4H2 | `ability:Ability.Action.Hun.Heavy.ComboAtk_N4H2` | UI_Setting_308；AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack |
| 重攻击 · 蓄力 | `ability:Ability.Action.Hun.Heavy.Atk01_Hold` | UI_Setting_308；AIM_FengLeiJian_HeavyAttackLongPress；AIM_ChaosBlade_HeavyAttack_Hold |
| 处决敌人 | `ability:Ability.Action.Execution.Channel.Execution` | UI_Setting_309；AIM_Hun_Execution |
| 处决敌人 · 影之武装 | `ability:Ability.Action.Execution.Channel.ShachiWeapon` | UI_Setting_309；AIM_Hun_ShachiAbility |
| 处决敌人 | `action:/PBZInput/InputActions/IA_Execution` | UI_Setting_309 |
| 绝技 · 连招 · 1 | `ability:Ability.Action.Hun.CombatArt.Atk01` | UI_Setting_314；AIM_Hun_CombatArt；AIM_JueYing_CombatArt |
| 绝技 · 连招 · 2 | `ability:Ability.Action.Hun.CombatArt.Atk02` | UI_Setting_314；AIM_Hun_CombatArt；AIM_JueYing_CombatArt |
| 绝技 · 连招 · 3 | `ability:Ability.Action.Hun.CombatArt.Atk03` | UI_Setting_314；AIM_Hun_CombatArt；AIM_JueYing_CombatArt |
| 绝技 · 按下 | `action:/PBZInput/InputActions/IA_CombatArt` | UI_Setting_314 |
| 绝技 · 持续 | `action:/PBZInput/InputActions/IA_CombatArt_Hold` | UI_Setting_314 |
| 绝技 · 空中 | `ability:Ability.Action.Hun.CombatArt.JueYing.Air` | UI_Setting_314；AIM_JueYing_CombatArt |
| 特殊攻击 | `ability:Ability.Action.Execution.Channel.SpecialAttack` | AIM_Hun_HeavyAttack；AIM_BaiYing_HeavyAttack |
| 点按防御 | `action:/PBZDeflectSystem/Input/IA_Deflect` | IA_Deflect.Triggered：刷新防御窗口并启动架势；完美判定由游戏决定 |
| 架势状态 | `action:/PBZDeflectSystem/Input/IA_Stance` | Tutorial_Defense_Title_1 |
| 闪避 · 普通 | `ability:Ability.Action.Hun.Dash` | UI_Setting_313；AIM_Hun_Dash；AIM_Mojian_Dash；AIM_ShachiHook_Dash；AIM_JadeDual_Dash；AIM_PoJun_Dash；AIM_Tang_HorseCartCombat_Dodge |
| 瞬身 | `ability:Ability.Action.Hun.SpecialDash` | UI_AbilityName_23；AIM_Hun_Dash |
| 闪避输入 | `action:/Game/PBZContent/Input/InputActions/IA_Dash` | /Game/PBZContent/Input/InputActions/IA_Dash |
| 闪避 · 按下 | `action:/PBZDeflectSystem/Input/IA_Dodge` | UI_Setting_313 |
| 完美闪避 | `action:/PBZDeflectSystem/Input/IA_PerfectDodge` | /PBZDeflectSystem/Input/IA_PerfectDodge |
| 闪避 · 持续 | `action:/PBZDeflectSystem/Input/IA_DodgeHold` | UI_Setting_313 |
| 空中 · 闪避 | `ability:Ability.Action.Hun.AirDash` | AIM_Hun_Dash |
| 连续闪避 · 2 | `ability:Ability.Action.Hun.ContinuousDash.Dash_02` | AIM_Hun_Dash |
| 连续闪避 · 3 | `ability:Ability.Action.Hun.ContinuousDash.Dash_03` | AIM_Hun_Dash |
| 完美闪避 | `ability:Ability.Action.Hun.PerfectDodge` | AIM_Hun_Dash |
| 瞬身 · 拼刀起始 | `ability:Ability.Action.Hun.DeflectDash` | UI_AbilityName_23；AIM_Hun_Dash |
| 切换攻击 · 普通 | `ability:Ability.Action.Hun.Weapon.Switch.Normal` | AIM_Hun_ChangeWeapon |
| 收/拔武器 | `ability:Ability.UpperBody.Hun.ChangeArmed` | UI_Setting_405；AIM_Hun_ChangeArmed |
| 收起武器 | `action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed` | /SGameCombatSystem/Weapon/Input/IA_ChangeArmed |
| 更换武器 | `action:/SGameCombatSystem/Weapon/Input/IA_ChangeWeapon` | UI_Setting_316 |
| 更换武器 · 持续 | `action:/SGameCombatSystem/Weapon/Input/IA_ChangingWeapon` | UI_Setting_316 |
| 切换攻击 · 派生 · Dash | `ability:Ability.Action.Hun.Weapon.Switch.Dash` | AIM_Hun_ChangeWeapon |
| 切换攻击 · 空中 | `ability:Ability.Action.Hun.Weapon.Switch.Air` | AIM_Hun_ChangeWeapon |
| 丢弃临时武器 | `ability:Ability.Action.Hun.TempWeapon.Ultimate` | AIM_TempWeapon_Ultimate |
| 残钢刃 · 重攻击 · BJump | `ability:Ability.Action.Hun.Heavy.Iron.BJump` | Weapon_002；UI_Setting_308；AIM_Iron_HeavyAttack |
| 残钢刃 · 血刃蓄力 | `ability:Ability.Action.Hun.Heavy.Iron.BCharge` | Weapon_002；Weapon_FunctionName_002_15；AIM_Iron_HeavyAttackLongPress |
| 残钢刃 · 癫狂血刃 | `ability:Ability.Action.Hun.Heavy.Iron.Buff` | Weapon_002；Weapon_FunctionName_002_25；AIM_Iron_HeavyAttack |
| 乱莺剑 · 鸣剑 | `ability:Ability.Action.Hun.LuanYing.Buff` | Weapon_003；AIM_LuanYing_HeavyAttack |
| 乱莺剑 · 鸣剑 · 铸魂 | `ability:Ability.Action.Hun.LuanYing.BuffP2` | Weapon_003；AIM_LuanYing_HeavyAttack |
| 刺骨混沌 · 冰形态 | `ability:Ability.Action.Hun.Heavy.ChaosBlade.ToBing` | Weapon_004；UI_Setting_308；AIM_Chaos_HeavyAttack |
| 刺骨混沌 · 切换混沌形态 | `ability:Ability.Action.Hun.Heavy.ChaosBlade.ToHunDun` | Weapon_004；UI_Setting_308；AIM_Chaos_HeavyAttack |
| 刺骨混沌 · 切换钢刃形态 | `ability:Ability.Action.Hun.Heavy.ChaosBlade.ToWuLi` | Weapon_004；UI_Setting_308；AIM_Chaos_HeavyAttack |
| 黑魔伤 · 重攻击 · AAAAABB | `ability:Ability.Action.Hun.Heavy.Tang.AAAAABB` | Weapon_005；UI_Setting_308；AIM_HeiMoShang_HeavyAttack |
| 天玄青铜剑 · 剑气派生 · Nsp | `ability:Ability.Action.Hun.Heavy.BronzeSword.Nsp` | Weapon_007；UI_Setting_308；AIM_BronzeSword_LightAttack |
| 天玄青铜剑 · 重攻击 · DB | `ability:Ability.Action.Hun.Heavy.BronzeSword.DB` | Weapon_007；UI_Setting_308；AIM_BronzeSword_HeavyAttack |
| 天玄青铜剑 · 特殊格挡反击 · B | `ability:Ability.Action.Hun.Heavy.BronzeSword.Deflect.B` | Weapon_007；UI_Setting_308；AIM_BronzeSword_HeavyAttack |
| 天玄青铜剑 · 特殊格挡反击 · BB | `ability:Ability.Action.Hun.Heavy.BronzeSword.Deflect.BB` | Weapon_007；UI_Setting_308；AIM_BronzeSword_HeavyAttack |
| 天玄青铜剑 · 重攻击 · 蓄力 · BHold | `ability:Ability.Action.Hun.Heavy.BronzeSword.BHold` | Weapon_007；UI_Setting_308；AIM_BronzeSword_HeavyAttackLongPress |
| 痴者残剑 · 喝酒 | `ability:Ability.Action.Hun.Heavy.ChiZheCanJian.Drink.Normal` | Weapon_008；UI_Setting_308；AIM_ChiZheCanJian_HeavyAttack |
| 痴者残剑 · CToZui | `ability:Ability.Action.Hun.ChiZheCanJian.CToZui` | Weapon_008；AIM_Hun_ChiZheCanJian_CA |
| 痴者残剑 · 武器动作 · Zui | `ability:Ability.Action.Hun.ChiZheCanJian.ZuiChangeArmed` | Weapon_008；AIM_Hun_ChiZheCanJian_ChangeArmed_Zui |
| 灵蛇软剑 · 剑花如盾 | `ability:Ability.Action.Hun.Light.SlitheringSword.DA` | Weapon_009；Weapon_FunctionName_009_15；AIM_SlitheringSword_LightAttack |
| 灵蛇软剑 · 后发先至 · DB1 | `ability:Ability.Action.Hun.Heavy.SlitheringSword.DB1` | Weapon_009；Weapon_FunctionName_009_EX；AIM_SlitheringSword_HeavyAttack |
| 灵蛇软剑 · 后发先至 · DB2 | `ability:Ability.Action.Hun.Heavy.SlitheringSword.DB2` | Weapon_009；Weapon_FunctionName_009_EX；AIM_SlitheringSword_HeavyAttack |
| 灵蛇软剑 · 后发先至 · DB3 | `ability:Ability.Action.Hun.Heavy.SlitheringSword.DB3` | Weapon_009；Weapon_FunctionName_009_EX；AIM_SlitheringSword_HeavyAttack |
| 灵蛇软剑 · 变体 · EvalutionCombo | `ability:Ability.Action.Hun.Special.SlitheringSword.EvalutionCombo` | Weapon_009；UI_Setting_310；AIM_SlitheringSword_HeavyAttack_Press |
| 白山风雷剑 · 攻击 · AABA | `ability:Ability.Action.Hun.FengLeiJian.AABA` | Weapon_010；AIM_FengLeiJian_LightAttack |
| 白山风雷剑 · 攻击 · AABAB | `ability:Ability.Action.Hun.FengLeiJian.AABAB` | Weapon_010；AIM_FengLeiJian_HeavyAttack |
| 白山风雷剑 · 闪电风暴 | `ability:Ability.Action.Hun.FengLeiJian.FengLeiState.SpecialAttack01` | Weapon_010；Weapon_FunctionName_010_EX_2；AIM_FengLeiJian_HeavyAttack |
| 紫冥剑 · 布局 · DB.FirstTime | `ability:Ability.Action.Hun.Heavy.ZiMingJian.DB.FirstTime` | Weapon_011；UI_Setting_308；AIM_ZiMingJian_HeavyAttack |
| 长形黑伤 · 轻攻击 · AAAAAA | `ability:Ability.Action.Hun.Light.Long.AAAAAA` | Weapon_012；UI_Setting_307；AIM_Long_LightAttack |
| 亡命斩马刀 · 轻攻击 · SA | `ability:Ability.Action.Hun.Light.ZhanMa.SA` | Weapon_013；UI_Setting_307；AIM_ZhanMa_LightAttack |
| 亡命斩马刀 · 亡命蓄斩 | `ability:Ability.Action.Hun.Heavy.ZhanMa.DB` | Weapon_013；Weapon_FunctionName_013_5；AIM_ZhanMa_HeavyAttack |
| 炽刃 · 重攻击 · 派生 · CounterB | `ability:Ability.Action.Hun.Heavy.Long.CounterB` | Weapon_014；UI_Setting_308；AIM_ChiRen_HeavyAttack |
| 断肢锯 · 血锯 · RA | `ability:Ability.Action.Hun.Light.JigSaw.RA` | Weapon_015；Weapon_FunctionName_015_15；AIM_Jigsaw_LightAttack |
| 断肢锯 · 血锯 · RAA | `ability:Ability.Action.Hun.Light.JigSaw.RAA` | Weapon_015；Weapon_FunctionName_015_15；AIM_Jigsaw_LightAttack |
| 断肢锯 · 血锯 · RAAA | `ability:Ability.Action.Hun.Light.JigSaw.RAAA` | Weapon_015；Weapon_FunctionName_015_15；AIM_Jigsaw_LightAttack |
| 断肢锯 · 轻攻击 · SA | `ability:Ability.Action.Hun.Light.JigSaw.SA` | Weapon_015；UI_Setting_307；AIM_Jigsaw_LightAttack |
| 断肢锯 · 血锯 · RB | `ability:Ability.Action.Hun.Heavy.Jigsaw.RB` | Weapon_015；Weapon_FunctionName_015_15；AIM_Jigsaw_HeavyAttack |
| 断肢锯 · 血锯 · RBB | `ability:Ability.Action.Hun.Heavy.Jigsaw.RBB` | Weapon_015；Weapon_FunctionName_015_15；AIM_Jigsaw_HeavyAttack |
| 断肢锯 · 重攻击 · SB | `ability:Ability.Action.Hun.Heavy.Jigsaw.SB` | Weapon_015；UI_Setting_308；AIM_Jigsaw_HeavyAttack |
| 白影 · 寒冰击 | `ability:Ability.Action.Hun.BaiYing.BuffAtk.01` | Weapon_016；Weapon_FunctionName_016_5；AIM_BaiYing_HeavyAttack |
| 红妆鬼斩 · 轻攻击 · DA | `ability:Ability.Action.Hun.Light.GuiTouDao.DA` | Weapon_017；UI_Setting_307；AIM_GuiTouBlade_LightAttack |
| 红妆鬼斩 · 重攻击 · 蓄力 · BHold.Start | `ability:Ability.Action.Hun.Heavy.GuiTouDao.BHold.Start` | Weapon_017；UI_Setting_308；AIM_GuiTouBlade_HeavyAttack_Hold |
| 破军 · 强力冲锋 | `ability:Ability.Action.Hun.Heavy.PoJun.DB` | Weapon_018；Weapon_FunctionName_018_5；AIM_PoJun_HeavyAttack |
| 破军 · 重攻击 · DB2 | `ability:Ability.Action.Hun.Heavy.PoJun.DB2` | Weapon_018；UI_Setting_308；AIM_PoJun_HeavyAttack |
| 荡寇凶刃 · 重攻击 · 刀波 | `ability:Ability.Action.Hun.Heavy.Horizontal.BladeWave` | Weapon_019；UI_Setting_308；AIM_Horizontal_HeavyAttack；AIM_Horizontal_HeavyAttack_LongPress |
| 荡寇凶刃 · 强化重击 · CriticalAttack | `ability:Ability.Action.Hun.Heavy.Horizontal.CriticalAttack` | Weapon_019；UI_Setting_308；AIM_Horizontal_HeavyAttack_LongPress |
| 荡寇凶刃 · 赤轮斩 | `ability:Ability.Action.Hun.Heavy.Horizontal.Overcharge` | Weapon_019；Weapon_FunctionName_019_EX；AIM_Horizontal_HeavyAttack |
| 偃月刀 · 青龙斩 | `ability:Ability.Action.Hun.Heavy.YanYueDao.Reinforced.01` | Weapon_020；Weapon_FunctionName_020_EX_2；AIM_YanYueDao_HeavyAttack |
| 偃月刀 · 偃月风暴 | `ability:Ability.Action.Hun.Heavy.YanYueDao.Special01` | Weapon_020；Weapon_FunctionName_020_EX；AIM_YanYueDao_HeavyAttack |
| 妖锯 · 蓄势连斩 | `ability:Ability.Action.Hun.Light.YuuBlade.FastAttack` | Weapon_023；Weapon_FunctionName_023_5；AIM_YuuBlade_LightAttack_Hold |
| 妖锯 · 蓄势狂斩 | `ability:Ability.Action.Hun.Heavy.YuuBlade.FastAttack` | Weapon_023；Weapon_FunctionName_023_15；AIM_YuuBlade_HeavyAttack_Hold |
| 绝影 · 化影 | `ability:Ability.Action.Hun.Special.Double.Clone` | Weapon_024；Weapon_FunctionName_024_EX；AIM_Double_HeavyAttack_Evalution |
| 天煞双钺 · 群煞击 | `ability:Ability.Action.Hun.Heavy.ShuangYue.DB` | Weapon_026；Weapon_FunctionName_026_15；AIM_ShuangYue_HeavyAttack |
| 天煞双钺 · 断肠击 · PB | `ability:Ability.Action.Hun.Heavy.ShuangYue.PB` | Weapon_026；Weapon_FunctionName_026_25；AIM_ShuangYue_HeavyAttack |
| 天煞双钺 · 断肠击 · PBB | `ability:Ability.Action.Hun.Heavy.ShuangYue.PBB` | Weapon_026；Weapon_FunctionName_026_25；AIM_ShuangYue_HeavyAttack |
| 白蟒赤练 · 轻攻击 · ABA | `ability:Ability.Action.Hun.Light.SnakeDual.ABA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · ABAA | `ability:Ability.Action.Hun.Light.SnakeDual.ABAA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · ABABA | `ability:Ability.Action.Hun.Light.SnakeDual.ABABA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · ABABAA | `ability:Ability.Action.Hun.Light.SnakeDual.ABABAA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · A_Fast | `ability:Ability.Action.Hun.Light.SnakeDual.A_Fast` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · BA | `ability:Ability.Action.Hun.Light.SnakeDual.BA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · BAA | `ability:Ability.Action.Hun.Light.SnakeDual.BAA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · BABA | `ability:Ability.Action.Hun.Light.SnakeDual.BABA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 轻攻击 · BABAA | `ability:Ability.Action.Hun.Light.SnakeDual.BABAA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_LightAttack |
| 白蟒赤练 · 重攻击 · AB | `ability:Ability.Action.Hun.Heavy.SnakeDual.AB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · ABAB | `ability:Ability.Action.Hun.Heavy.SnakeDual.ABAB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · ABABB | `ability:Ability.Action.Hun.Heavy.SnakeDual.ABABB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · ABB | `ability:Ability.Action.Hun.Heavy.SnakeDual.ABB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · BAB | `ability:Ability.Action.Hun.Heavy.SnakeDual.BAB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · BABAB | `ability:Ability.Action.Hun.Heavy.SnakeDual.BABAB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · BABABB | `ability:Ability.Action.Hun.Heavy.SnakeDual.BABABB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · BABB | `ability:Ability.Action.Hun.Heavy.SnakeDual.BABB` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 重攻击 · B_Fast | `ability:Ability.Action.Hun.Heavy.SnakeDual.B_Fast` | Weapon_027；UI_Setting_308；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 赤练环身 | `ability:Ability.Action.Hun.Heavy.SnakeDual.DB` | Weapon_027；Weapon_FunctionName_027_EX；AIM_SnakeDual_HeavyAttack |
| 白蟒赤练 · 投掷 · DA | `ability:Ability.Action.Hun.Light.SnakeDual.DA` | Weapon_027；UI_Setting_307；AIM_SnakeDual_Execution |
| 别离残钩 · 轻攻击 · 派生 · Pursure | `ability:Ability.Action.Hun.Light.DualHook.Pursure` | Weapon_028；UI_Setting_307；AIM_DualHook_LightAttack |
| 别离残钩 · 反制 | `ability:Ability.Action.Hun.Heavy.DualHook.DB` | Weapon_028；Weapon_FunctionName_028_EX；AIM_DualHook_HeavyAttack |
| 别离残钩 · 重攻击 · 派生 · Pursure | `ability:Ability.Action.Hun.Heavy.DualHook.Pursure` | Weapon_028；UI_Setting_308；AIM_DualHook_HeavyAttack |
| 玉双环 · 蓄力飞环 | `ability:Ability.Action.Hun.Special.Jade.DB` | Weapon_029；Weapon_FunctionName_029_5；AIM_Jade_HeavyAttack |
| 圣母邪扇 · 印记爆发 | `ability:Ability.Action.Hun.Heavy.XieShan.DB` | Weapon_031；Weapon_FunctionName_031_EX_2；AIM_XieShan_HeavyAttack |
| 双持武器 · 重攻击 · ATB | `ability:Ability.Action.Hun.Heavy.Double.ATB` | AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_Jade_HeavyAttack；AIM_XieShan_HeavyAttack |
| 双持武器 · 重攻击 · ATBB | `ability:Ability.Action.Hun.Heavy.Double.ATBB` | AIM_FengLeiJian_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_Jade_HeavyAttack；AIM_XieShan_HeavyAttack |
| 双持武器 · 重攻击 · ATBBB | `ability:Ability.Action.Hun.Heavy.Double.ATBBB` | AIM_FengLeiJian_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_Jade_HeavyAttack；AIM_XieShan_HeavyAttack |
| 武器增益 · Long.BD | `ability:Ability.Action.Hun.Heavy.Long.BD` | AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack |
| 重攻击 · Long.BSup | `ability:Ability.Action.Hun.Heavy.Long.BSup` | AIM_ChiRen_HeavyAttack；AIM_Long_HeavyAttack |
| 单手武器 · 重攻击 · AAAAAB | `ability:Ability.Action.Hun.Heavy.Tang.AAAAAB` | AIM_HeiMoShang_HeavyAttack；AIM_Tang_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 单手武器 · 重攻击 · BBBB | `ability:Ability.Action.Hun.Heavy.Tang.BBBB` | AIM_HeiMoShang_HeavyAttack；AIM_Tang_HeavyAttack；AIM_ZiMingJian_HeavyAttack |
| 双持武器 · 轻攻击 · BTA | `ability:Ability.Action.Hun.Light.Double.BTA` | AIM_FengLeiJian_LightAttack；AIM_Double_LightAttack |
| 轻攻击 · Long.AAAAA | `ability:Ability.Action.Hun.Light.Long.AAAAA` | AIM_BaiYing_LightAttack；AIM_Long_HeavyAttack；AIM_Long_LightAttack |
| 单手武器 · 轻攻击 · AAAAAA | `ability:Ability.Action.Hun.Light.Tang.AAAAAA` | AIM_Chaos_LightAttack；AIM_Iron_LightAttack；AIM_Tang_LightAttack；AIM_ZiMingJian_LightAttack |
| 影杀 · C | `ability:Ability.Action.Hun.Special.Double.YingSha.C` | AIM_Hun_Execution |
| 影杀 · CB | `ability:Ability.Action.Hun.Special.Double.YingSha.CB` | AIM_Hun_HeavyAttack；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution；AIM_Jade_HeavyAttack；AIM_XieShan_HeavyAttack |
| 裂斩 · C | `ability:Ability.Action.Hun.Special.Long.LieZhan.C` | AIM_Hun_Execution |
| 裂斩 · CB | `ability:Ability.Action.Hun.Special.Long.LieZhan.CB` | AIM_Hun_HeavyAttack |
| 瞬杀 · C | `ability:Ability.Action.Hun.Special.Tang.ShunSha.C` | AIM_Hun_Execution |
| 瞬杀 · CB | `ability:Ability.Action.Hun.Special.Tang.ShunSha.CB` | AIM_Hun_HeavyAttack；AIM_Chaos_HeavyAttack；AIM_HeiMoShang_HeavyAttack；AIM_LuanYing_HeavyAttack；AIM_Tang_HeavyAttack |
| 凝气于刃 · Double | `ability:Ability.Action.Hun.Special.Double.Buff` | Weapon_FunctionName_022_5；AIM_FengLeiJian_Execution；AIM_Double_HeavyAttack；AIM_Double_HeavyAttack_Evalution |
| 凝气于刃 · Tang | `ability:Ability.Action.Hun.Special.Tang.Buff` | Weapon_FunctionName_001_5；AIM_HeiMoShang_HeavyAttack；AIM_Tang_HeavyAttack |
| 武器特殊能力 · GlobalAbility | `ability:Ability.Action.Hun.Special.Tang.GlobalAbility` | AIM_Iron_Execution |
| 影之武装 · 通用 | `ability:Ability.Action.Hun.ShachiAbility.Default` | UI_Setting_315；AIM_Hun_ShachiAbility |
| 影之武装 · 按下 | `action:/PBZInput/InputActions/IA_ShachiAbility` | UI_Setting_315 |
| 更换影之武装 | `action:/SGameCombatSystem/Weapon/Input/IA_ChangeShachiWeapon` | UI_Setting_317 |
| 影之武装 · 持续 | `action:/PBZInput/InputActions/IA_ShachiAbility_Hold` | UI_Setting_315 |
| 影之武装 · 长按 | `action:/PBZInput/InputActions/IA_ShachiAbility_LongPress` | UI_Setting_315 |
| 巨凶斧 · 普通 | `ability:Ability.Action.Hun.ShachiAbility.BrutalAxe.Main` | PhantomEdges_001；UI_Setting_315；AIM_BrutalAxe_ShachiAbility |
| 巨凶斧 · 疾行斧 | `ability:Ability.Action.Hun.ShachiAbility.BrutalAxe.Sprinting` | PhantomEdges_001；PhantomEdges_FunctionName_001_15；AIM_BrutalAxe_ShachiAbility |
| 巨凶斧 · 派生 | `ability:Ability.Action.Hun.ShachiAbility.BrutalAxe.Combo` | PhantomEdges_001；UI_Setting_315；AIM_BrutalAxe_ShachiAbility |
| 长柄金瓜 · 普通 | `ability:Ability.Action.Hun.ShachiAbility.JinGua.Main` | PhantomEdges_002；UI_Setting_315；AIM_JinGua_ShachiAbility |
| 长柄金瓜 · 旋锤猛进 | `ability:Ability.Action.Hun.ShachiAbility.JinGua.RotateAttack` | PhantomEdges_002；PhantomEdges_FunctionName_002_Re；AIM_JinGua_ShachiAbility |
| 长柄金瓜 · 天降金瓜 | `ability:Ability.Action.Hun.ShachiAbility.JinGua.Air` | PhantomEdges_002；PhantomEdges_FunctionName_002_5；AIM_JinGua_ShachiAbility |
| 长柄金瓜 · 空中攻击 · SprintAir | `ability:Ability.Action.Hun.ShachiAbility.JinGua.SprintAir` | PhantomEdges_002；UI_Setting_315；AIM_JinGua_ShachiAbility |
| 长柄金瓜 · 追加攻击 | `ability:Ability.Action.Hun.ShachiAbility.JinGua.ExtraAttack` | PhantomEdges_002；UI_Setting_315；AIM_JinGua_ShachiAbility |
| 拙壁盾 · 滑行 | `ability:Ability.Action.Hun.ShachiAbility.ZhuoBiDun.Slide` | PhantomEdges_004；UI_Setting_315；AIM_ZhuoBiDun_ShachiAbility |
| 拙壁盾 · 推击 | `ability:Ability.Action.Hun.ShachiAbility.ZhuoBiDun.Parry` | PhantomEdges_004；UI_Setting_315；AIM_ZhuoBiDun_ShachiAbility；AIM_ZhuoBiDun_StancePush |
| 贪狼 · 普通 | `ability:Ability.Action.Hun.ShachiAbility.TanLang.Normal` | PhantomEdges_005；UI_Setting_315；AIM_TanLang_ShachiAbility |
| 贪狼 · 贪狼穿心 | `ability:Ability.Action.Hun.ShachiAbility.TanLang.SpecialAttack` | PhantomEdges_005；PhantomEdges_FunctionName_005_25；AIM_TanLang_ShachiAbility |
| 贪狼 · 全力一击 | `ability:Ability.Action.Hun.ShachiAbility.TanLang.ExtraAttack` | PhantomEdges_005；PhantomEdges_FunctionName_005_15；AIM_TanLang_ShachiAbility |
| 虎炮 · 普通 | `ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Normal` | PhantomEdges_006；UI_Setting_315；AIM_TigerCannon_ShachiAbility |
| 虎炮 · 虎步 | `ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Dash` | PhantomEdges_006；PhantomEdges_FunctionName_006_5；AIM_TigerCannon_ShachiAbility |
| 虎炮 · 快速蓄力 | `ability:Ability.Action.Hun.ShachiAbility.TigerCannon.QuickCharge` | PhantomEdges_006；UI_Setting_315；AIM_TigerCannon_ShachiAbility |
| 虎炮 · 变体 · Ultimate | `ability:Ability.Action.Hun.ShachiAbility.TigerCannon.Ultimate` | PhantomEdges_006；UI_Setting_315；AIM_TigerCannon_ShachiAbility |
| 黄金火铳 · 攻击 · 2 | `ability:Ability.Action.Hun.ShachiAbility.HuangJinHuoChong.Common_02` | PhantomEdges_008；UI_Setting_315；AIM_HuangJinHuoChong_ShachiAbility |
| 黄金火铳 · 攻击 · 3 | `ability:Ability.Action.Hun.ShachiAbility.HuangJinHuoChong.Common_03` | PhantomEdges_008；UI_Setting_315；AIM_HuangJinHuoChong_ShachiAbility |
| 黄金火铳 · 移动射击 | `ability:Ability.Action.Hun.ShachiAbility.HuangJinHuoChong.Dash` | PhantomEdges_008；UI_Setting_315；AIM_HuangJinHuoChong_ShachiAbility |
| 黄金火铳 · 拼刀反击 | `ability:Ability.Action.Hun.ShachiAbility.HuangJinHuoChong.DeflectCounter` | PhantomEdges_008；UI_Setting_315；AIM_HuangJinHuoChong_ShachiAbility |
| 羽弓 · 移动射击 | `ability:Ability.Action.Hun.ShachiAbility.Bow.Dash` | PhantomEdges_009；UI_Setting_315；AIM_Bow_ShachiAbility |
| 羽弓 · 反击 | `ability:Ability.Action.Hun.ShachiAbility.Bow.DeflectCounter` | PhantomEdges_009；UI_Setting_315；AIM_Bow_ShachiAbility；AIM_ZhuoBiDun_ShachiAbility |
| 妙手飞刀 · 攻击 · 1 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Common_01` | PhantomEdges_010；UI_Setting_315；AIM_MiaoShouFeiDao_ShachiAbility |
| 妙手飞刀 · 攻击 · 2 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Common_02` | PhantomEdges_010；UI_Setting_315；AIM_MiaoShouFeiDao_ShachiAbility |
| 妙手飞刀 · 攻击 · 3 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Common_03` | PhantomEdges_010；UI_Setting_315；AIM_MiaoShouFeiDao_ShachiAbility |
| 妙手飞刀 · 疾风步 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.DB` | PhantomEdges_010；PhantomEdges_FunctionName_010_25；AIM_MiaoShouFeiDao_ShachiAbility |
| 妙手飞刀 · 探花步 · 01 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Escape.01` | PhantomEdges_010；PhantomEdges_FunctionName_010_5；AIM_MiaoShouFeiDao_Dash |
| 妙手飞刀 · 探花步 · 02 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Escape.02` | PhantomEdges_010；PhantomEdges_FunctionName_010_5；AIM_MiaoShouFeiDao_Dash |
| 妙手飞刀 · 探花步 · 03 | `ability:Ability.Action.Hun.ShachiAbility.MiaoShouFeiDao.Escape.03` | PhantomEdges_010；PhantomEdges_FunctionName_010_5；AIM_MiaoShouFeiDao_Dash |
| 游龙头 · 水炮 | `ability:Ability.Action.Hun.ShachiAbility.YouLongTou.WaterCannon` | PhantomEdges_011；PhantomEdges_FunctionName_011_5；AIM_YouLongHead_ShachiAbility |
| 烈焰狮头 · 火球速射 | `ability:Ability.Action.Hun.ShachiAbility.LionHead.FireBall` | PhantomEdges_012；PhantomEdges_FunctionName_012_5；AIM_LionHead_ShachiAbility |
| 烈焰狮头 · 双重火球 | `ability:Ability.Action.Hun.ShachiAbility.LionHead.DoubleFireBall` | PhantomEdges_012；PhantomEdges_FunctionName_012_25；AIM_LionHead_ShachiAbility |
| 烈焰狮头 · 狮头突进 | `ability:Ability.Action.Hun.ShachiAbility.LionHead.Dash` | PhantomEdges_012；PhantomEdges_FunctionName_012_15；AIM_LionHead_ShachiAbility |
| 追命锁镰 · 攻击 · C | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.C` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 攻击 · CAA | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CAA` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_HeavyAttack；AIM_ShachiHook_LightAttack；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 攻击 · CBB | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CBB` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_HeavyAttack；AIM_ShachiHook_LightAttack；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 攻击 · CC | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CC` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 攻击 · CCC | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CCC` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 攻击 · CCCC | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CCCC` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 派生 · CDD | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CDD` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_HeavyAttack；AIM_ShachiHook_LightAttack；AIM_ShachiHook_ShachiAbility |
| 追命锁镰 · 拖拽 | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.Drag` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_HeavyAttack；AIM_ShachiHook_LightAttack |
| 追命锁镰 · 钩索移动 | `ability:Ability.Action.Hun.ShachiAbility.ShachiHook.HookMove` | PhantomEdges_014；UI_Setting_315；AIM_ShachiHook_ShachiAbility |
| 火流星 · 攻击 · A | `ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.A` | PhantomEdges_015；UI_Setting_315；AIM_HuoLiuXing_ShachiAbility |
| 火流星 · 攻击 · AA | `ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AA` | PhantomEdges_015；UI_Setting_315；AIM_HuoLiuXing_ShachiAbility |
| 火流星 · 攻击 · AAA | `ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAA` | PhantomEdges_015；UI_Setting_315；AIM_HuoLiuXing_ShachiAbility |
| 火流星 · 攻击 · AAAA | `ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA` | PhantomEdges_015；UI_Setting_315；AIM_HuoLiuXing_ShachiAbility |
| 火流星 · 变体 · AShort | `ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AShort` | PhantomEdges_015；UI_Setting_315；AIM_HuoLiuXing_ShachiAbility |
| 火流星 · 磷火龙卷 | `ability:Ability.Action.Hun.ShachiAbility.HuoLiuXing.DB` | PhantomEdges_015；PhantomEdges_FunctionName_015_25；AIM_HuoLiuXing_ShachiAbility |
| 游龙叉 · 派生 · Dash | `ability:Ability.Action.Hun.ShachiAbility.YouLongCha.Dash` | PhantomEdges_016；UI_Setting_315；AIM_YouLongCha_ShachiAbility |
| 铁拳套 · 攻击 · C | `ability:Ability.Action.Hun.ShachiAbility.TieKuangQuanTao.Combo.C` | PhantomEdges_017；UI_Setting_315；AIM_TieKuangQuanTao_ShachiAbility |
| 铁拳套 · 攻击 · CC | `ability:Ability.Action.Hun.ShachiAbility.TieKuangQuanTao.Combo.CC` | PhantomEdges_017；UI_Setting_315；AIM_TieKuangQuanTao_ShachiAbility |
| 铁拳套 · 攻击 · CCC | `ability:Ability.Action.Hun.ShachiAbility.TieKuangQuanTao.Combo.CCC` | PhantomEdges_017；UI_Setting_315；AIM_TieKuangQuanTao_ShachiAbility |
| 铁拳套 · 拳挡四方 | `ability:Ability.Action.Hun.ShachiAbility.TieKuangQuanTao.GpAttack.Normal` | PhantomEdges_017；PhantomEdges_FunctionName_017_15；AIM_TieKuangQuanTao_ShachiAbility |
| 墨血无痕 · 普通 · A | `ability:Ability.Action.Hun.ShachiAbility.MoJi.A.Default` | PhantomEdges_018；UI_Setting_315；AIM_Mojian_ShachiAbility |
| 墨血无痕 · 攻击 · A · Short | `ability:Ability.Action.Hun.ShachiAbility.MoJi.A.Short` | PhantomEdges_018；UI_Setting_315；AIM_Mojian_ShachiAbility |
| 墨血无痕 · 攻击 · AA | `ability:Ability.Action.Hun.ShachiAbility.MoJi.AA` | PhantomEdges_018；UI_Setting_315；AIM_Mojian_ShachiAbility |
| 墨血无痕 · 攻击 · AAA | `ability:Ability.Action.Hun.ShachiAbility.MoJi.AAA` | PhantomEdges_018；UI_Setting_315；AIM_Mojian_ShachiAbility |
| 雷霆枪 · 攻击 · 1 | `ability:Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_01` | PhantomEdges_019；UI_Setting_315；AIM_ThunderSpear_ShachiAbility |
| 雷霆枪 · 攻击 · 2 | `ability:Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_02` | PhantomEdges_019；UI_Setting_315；AIM_ThunderSpear_ShachiAbility |
| 雷霆枪 · 攻击 · 3 | `ability:Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_03` | PhantomEdges_019；UI_Setting_315；AIM_ThunderSpear_ShachiAbility |
| 雷霆枪 · 雷霆再临 | `ability:Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_04` | PhantomEdges_019；PhantomEdges_FunctionName_019_25；AIM_ThunderSpear_ShachiAbility |
| 雷霆枪 · 回马枪 | `ability:Ability.Action.Hun.ShachiAbility.ThunderSpear.Dodge` | PhantomEdges_019；PhantomEdges_FunctionName_019_15；AIM_ThunderSpear_ShachiAbility |
| 雷霆枪 · 雷枪突刺 | `ability:Ability.Action.Hun.ShachiAbility.ThunderSpear.Dash` | PhantomEdges_019；PhantomEdges_FunctionName_019_5；AIM_ThunderSpear_ShachiAbility |
| 血滴子 · 攻击 · A | `ability:Ability.Action.Hun.ShachiAbility.XueDiZi.A` | PhantomEdges_020；UI_Setting_315；AIM_XueDiZi_ShachiAbility |
| 血滴子 · 攻击 · AA | `ability:Ability.Action.Hun.ShachiAbility.XueDiZi.AA` | PhantomEdges_020；UI_Setting_315；AIM_XueDiZi_ShachiAbility |
| 血滴子 · 攻击 · AAA | `ability:Ability.Action.Hun.ShachiAbility.XueDiZi.AAA` | PhantomEdges_020；UI_Setting_315；AIM_XueDiZi_ShachiAbility |
| 血滴子 · 变体 · EvolutionAttack | `ability:Ability.Action.Hun.ShachiAbility.XueDiZi.EvolutionAttack` | PhantomEdges_020；UI_Setting_315；AIM_XueDiZi_ShachiAbility |
| 血滴子 · 血滴穿空 | `ability:Ability.Action.Hun.ShachiAbility.XueDiZi.SpAttack` | PhantomEdges_020；PhantomEdges_FunctionName_020_15；AIM_XueDiZi_ShachiAbility |
| 催心琵琶 · 攻击 · AA | `ability:Ability.Action.Hun.ShachiAbility.PiPa.AA` | PhantomEdges_024；UI_Setting_315；AIM_Lute_ShachiAbility |
| 催心琵琶 · 攻击 · AAA | `ability:Ability.Action.Hun.ShachiAbility.PiPa.AAA` | PhantomEdges_024；UI_Setting_315；AIM_Lute_ShachiAbility |
| 催心琵琶 · 破阵曲 | `ability:Ability.Action.Hun.ShachiAbility.PiPa.Skill` | PhantomEdges_024；PhantomEdges_FunctionName_024_25；AIM_Lute_ShachiAbility |
| 催心琵琶 · 移动音刃 | `ability:Ability.Action.Hun.ShachiAbility.PiPa.Dash` | PhantomEdges_024；UI_Setting_315；AIM_Lute_ShachiAbility |
| 独眼弓 · 激光 | `ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser` | PhantomEdges_109；UI_Setting_315；AIM_DuYanBow_ShachiAbility |
| 对话/交互 | `action:/Game/PBZContent/Input/InputActions/IA_Interaction` | UI_Setting_303 |
| 影之武装交互 | `action:/Game/PBZContent/Input/InputActions/IA_Interaction_ShaChiAttactkBroken` | /Game/PBZContent/Input/InputActions/IA_Interaction_ShaChiAttactkBroken |
| 特殊移动交互 | `action:/Game/PBZContent/Input/InputActions/IA_Interaction_SpecialMove` | /Game/PBZContent/Input/InputActions/IA_Interaction_SpecialMove |
| 澄心洞照 | `action:/Game/PBZContent/Input/InputActions/IA_Sense_Default` | UI_Setting_327 |
| 澄心洞照 · DualSense | `action:/Game/PBZContent/Input/InputActions/IA_Sense_DualSense` | UI_Setting_327 |
| 澄心洞照 · 触摸板触碰 | `action:/Game/PBZContent/Input/InputActions/IA_Sense_TouchPad_Touch` | UI_Setting_327 |
| 澄心洞照 · 内部输入 · EagleVision | `action:/PBZInput/InputActions/IA_EagleVision` | UI_Setting_327 |
| 使用药品 · 普通 | `ability:Ability.Action.Hun.UseItem` | AIM_Medicine_Common |
| 使用药品 · 杆上 | `ability:Ability.SpecialMove.Pole.UseItem` | AIM_Medicine_Common |
| 使用药品 · 上半身 | `ability:Ability.UpperBody.Hun.UseItem` | AIM_Medicine_Common |
| 药葫芦 · 内部输入 | `action:/Game/PBZContent/Blueprint/Item/Ability/Medicine/YaoHuLu/IA_YaoHuLu` | /Game/PBZContent/Blueprint/Item/Ability/Medicine/YaoHuLu/IA_YaoHuLu |
| 使用下方药 | `action:/Game/PBZContent/Input/InputActions/IA_UseBottomItem` | UI_Setting_318 |
| 使用上方药 | `action:/Game/PBZContent/Input/InputActions/IA_UseTopItem` | UI_Setting_319 |
| 攀爬 · 轴输入 | `action:/Game/PBZContent/Input/InputActions/IA_ClimblMove` | /Game/PBZContent/Input/InputActions/IA_ClimblMove |
| 攀爬 · 轴输入 · 手柄 | `action:/Game/PBZContent/Input/InputActions/IA_ClimbMoveGamePad` | /Game/PBZContent/Input/InputActions/IA_ClimbMoveGamePad |
| 攀爬 · 冲刺 | `action:/Game/PBZContent/Input/InputActions/IA_ClimbDash` | /Game/PBZContent/Input/InputActions/IA_ClimbDash |
| 攀爬 · 下行 · 冲刺 | `action:/Game/PBZContent/Input/InputActions/IA_ClimbDashDown` | /Game/PBZContent/Input/InputActions/IA_ClimbDashDown |
| 攀爬 · 下行 · 冲刺 · 手柄 | `action:/Game/PBZContent/Input/InputActions/IA_ClimbDashDownGamePad` | /Game/PBZContent/Input/InputActions/IA_ClimbDashDownGamePad |
| 爬梯 | `ability:Ability.SpecialMove.ClimbLadder` | AIM_Hun_Jump |
| 下滑 | `ability:Ability.SpecialMove.DownSliding` | AIM_Hun_Jump |
| 向左跳出 | `ability:Ability.SpecialMove.JumpOut.Left` | AIM_Hun_Jump |
| 向右跳出 | `ability:Ability.SpecialMove.JumpOut.Right` | AIM_Hun_Jump |
| 荡绳 | `ability:Ability.SpecialMove.RopeSwing` | AIM_Hun_Jump |
| 上跑 | `ability:Ability.SpecialMove.UpRunning` | AIM_Hun_Jump |
| 横跑 | `ability:Ability.Action.Hun.SpecialMove.SideRunning.Ground` | AIM_Hun_Jump |
| 横跑反跳 | `ability:Ability.Action.Hun.SpecialMove.SideRunning.FromJump` | AIM_Hun_Jump |
| 船只 · 向前移动 | `action:/Game/PBZContent/Input/InputActions/IA_ShipForward` | /Game/PBZContent/Input/InputActions/IA_ShipForward |
| 船只 · 向后移动 | `action:/Game/PBZContent/Input/InputActions/IA_ShipBackword` | /Game/PBZContent/Input/InputActions/IA_ShipBackword |
| 船只 · 横向移动 · 轴输入 | `action:/Game/PBZContent/Input/InputActions/IA_ShipRight` | /Game/PBZContent/Input/InputActions/IA_ShipRight |
| 旋转摄像机 · 船只 · 轴输入 | `action:/Game/PBZContent/Input/InputActions/IA_ShipLook` | UI_Setting_304 |
| 打开地图菜单 | `action:/Game/PBZContent/Input/InputActions/IA_Map` | UI_Setting_321 |
| 打开角色菜单 | `action:/Game/PBZContent/Input/InputActions/IA_PushGameSystemMenu` | UI_Setting_320 |
| 下一个 · 菜单标签页 | `action:/Game/PBZContent/Input/InputActions/IA_NextTab` | /Game/PBZContent/Input/InputActions/IA_NextTab |
| 上一个 · 菜单标签页 | `action:/Game/PBZContent/Input/InputActions/IA_PreviousTab` | /Game/PBZContent/Input/InputActions/IA_PreviousTab |
| 跳过 · 过场 | `action:/Game/PBZContent/Input/InputActions/IA_SkipSequence` | UI_Complete_04 |
| 跳过 · 教程 | `action:/Game/PBZContent/Input/InputActions/IA_SkipTutorialUI` | UI_Complete_04 |
| 跳过 · 教程 · 变体 · 2 | `action:/Game/PBZContent/Input/InputActions/IA_SkipTutorialUI2` | UI_Complete_04 |
| 过场按键输入 | `action:/PBZInput/InputActions/IA_Sequence_Press` | /PBZInput/InputActions/IA_Sequence_Press |
| 过场按键输入 · A_Cross | `action:/PBZInput/InputActions/IA_Sequence_Press_A_Cross` | /PBZInput/InputActions/IA_Sequence_Press_A_Cross |
| 过场按键输入 · B_Sphere | `action:/PBZInput/InputActions/IA_Sequence_Press_B_Sphere` | /PBZInput/InputActions/IA_Sequence_Press_B_Sphere |
| 过场按键输入 · LB_L1 | `action:/PBZInput/InputActions/IA_Sequence_Press_LB_L1` | /PBZInput/InputActions/IA_Sequence_Press_LB_L1 |
| 过场按键输入 · LT_L2 | `action:/PBZInput/InputActions/IA_Sequence_Press_LT_L2` | /PBZInput/InputActions/IA_Sequence_Press_LT_L2 |
| 过场按键输入 · RB_R1 | `action:/PBZInput/InputActions/IA_Sequence_Press_RB_R1` | /PBZInput/InputActions/IA_Sequence_Press_RB_R1 |
| 过场按键输入 · RT_R2 | `action:/PBZInput/InputActions/IA_Sequence_Press_RT_R2` | /PBZInput/InputActions/IA_Sequence_Press_RT_R2 |
| 过场按键输入 · X_Cube | `action:/PBZInput/InputActions/IA_Sequence_Press_X_Cube` | /PBZInput/InputActions/IA_Sequence_Press_X_Cube |
| 过场按键输入 · Y_TriAngle | `action:/PBZInput/InputActions/IA_Sequence_Press_Y_TriAngle` | /PBZInput/InputActions/IA_Sequence_Press_Y_TriAngle |
| 拍照输入 | `action:/PBZInput/InputActions/IA_TakePhoto` | /PBZInput/InputActions/IA_TakePhoto |
| 调试输入 · 下行 | `action:/Game/PBZContent/Input/InputActions/IA_MoveDown_GM` | /Game/PBZContent/Input/InputActions/IA_MoveDown_GM |
| 调试输入 · MoveInput_GM | `action:/Game/PBZContent/Input/InputActions/IA_MoveInput_GM` | /Game/PBZContent/Input/InputActions/IA_MoveInput_GM |
| 调试输入 · 上行 | `action:/Game/PBZContent/Input/InputActions/IA_MoveUp_GM` | /Game/PBZContent/Input/InputActions/IA_MoveUp_GM |
| 调试输入 · StatusInflictionDebug | `action:/PBZCombatAsset/Input/IA_StatusInflictionDebug` | /PBZCombatAsset/Input/IA_StatusInflictionDebug |
| 调试输入 · DeflectDebug | `action:/PBZDeflectSystem/Input/IA_DeflectDebug` | /PBZDeflectSystem/Input/IA_DeflectDebug |
| 调试输入 · DebugSlomo | `action:/PBZInput/InputActions/IA_DebugSlomo` | /PBZInput/InputActions/IA_DebugSlomo |
| 调试输入 · DebugUI | `action:/PBZInput/InputActions/IA_DebugUI` | /PBZInput/InputActions/IA_DebugUI |
| 调试输入 · DebugUI2 | `action:/PBZInput/InputActions/IA_DebugUI2` | /PBZInput/InputActions/IA_DebugUI2 |
| 调试输入 · NegativeFrameDebug | `action:/PBZInput/InputActions/IA_NegativeFrameDebug` | /PBZInput/InputActions/IA_NegativeFrameDebug |
| 调试输入 · TargetingDebug | `action:/PBZInput/InputActions/IA_TargetingDebug` | /PBZInput/InputActions/IA_TargetingDebug |
| 调试输入 · AttributeDebug | `action:/SGameAttributes/Input/IA_AttributeDebug` | /SGameAttributes/Input/IA_AttributeDebug |
| 调试输入 · BulletDebug | `action:/SGameCombatSystem/Bullet/Input/IA_BulletDebug` | /SGameCombatSystem/Bullet/Input/IA_BulletDebug |
| 调试输入 · ExecutionDebug | `action:/SGameExecution/Input/IA_ExecutionDebug` | /SGameExecution/Input/IA_ExecutionDebug |
| 调试输入 · DebugHold | `action:/SGameUtils/Input/IA_DebugHold` | /SGameUtils/Input/IA_DebugHold |
| 测试按键输入 · Han/Debug | `action:/Game/Test/Han/IA_Debug` | /Game/Test/Han/IA_Debug |
| 测试按键输入 · Han/FaceBottom_Hold | `action:/Game/Test/Han/IA_FaceBottom_Hold` | /Game/Test/Han/IA_FaceBottom_Hold |
| 测试按键输入 · Han/FaceLeft_Hold | `action:/Game/Test/Han/IA_FaceLeft_Hold` | /Game/Test/Han/IA_FaceLeft_Hold |
| 测试按键输入 · Han/FaceRight_Hold | `action:/Game/Test/Han/IA_FaceRight_Hold` | /Game/Test/Han/IA_FaceRight_Hold |
| 测试按键输入 · Han/FaceUp_Hold | `action:/Game/Test/Han/IA_FaceUp_Hold` | /Game/Test/Han/IA_FaceUp_Hold |
| 测试按键输入 · PBZInput/FaceUp_Hold | `action:/PBZInput/InputActions/IA_FaceUp_Hold` | /PBZInput/InputActions/IA_FaceUp_Hold |
| 慢动作调试输入 | `action:/Game/PBZContent/Input/OldInputActions/DebugActions/IA_Slomo` | /Game/PBZContent/Input/OldInputActions/DebugActions/IA_Slomo |
| 行走输入 · EnableWalk | `action:/Game/PBZContent/Input/OldInputActions/IA_EnableWalk` | /Game/PBZContent/Input/OldInputActions/IA_EnableWalk |

## 已知边界

依据包含静态资源、输入绑定与能力条件，以及 IA_ChangeArmed 的玩家实测反馈。未逐项进行实机动作测试。开发描述未能确定独立效果的分支使用中性名称和短代码。

资源刷新入口为 [extract-action-localization.py](../build/extract-action-localization.py) 和 [extract-action-details.py](../build/extract-action-details.py)。文本刷新按逐项文本关系读取，不按能力标签中的武器族自动补入武器名。目录排序由独立顺序字段控制。
