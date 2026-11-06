# 影之武装完整招式审计

CL-302256 的原生资产中，输入入口已经覆盖的能力仍有尚未独立接入的攻击阶段。确认的阶段包括巨凶斧五种释放、火流星 `AAAA.Final`、长柄金瓜 `Release3`、羽弓与游龙叉的两种投射、独眼弓普通投射、虎炮两种发射，以及铁拳套三段攻击的实际起点。

火流星 `AAAA.Final` 已通过现成 `ToRelease` 接入完整招式目录；其余候选需要保留各自的状态、费用帧与结束清理。命中触发的血滴子 `Saw`、盾的吸伤反击、复活和处决不能按蓄力等待处理。

## 覆盖边界

从安装游戏的 267,026 条 AssetRegistry 记录枚举全部 46 个 `PBZ_SubWeaponConfig_C`，归入 26 个配置目录家族；另核对 `DT_ShaqiWeaponData` 的 28 个原生 UI 行及简体中文 locres。扩展范围包括影之武装基类的 100 个继承类、实际配置导入、家族能力目录及父类/动作/条件依赖，共 348 个包、1,329 个函数，函数解码错误为 0。

29 个相关 InputActionBinding 的实际触发 tag 均可在现有输入目录找到。该结论只表示输入 tag 已收录；完整攻击阶段要按能力内部的控制流、实例动作和状态分支单独确认。timeline 数与 tag 数都不作为招式总数。

原生表名与显示名分别保存：`铁狂拳套` 的显示文字是“铁拳套”，`操偶丝线` 的显示文字是“操偶铃铛”，该行另含“深渊铃铛”；催心琵琶行另含“幻术琵琶”。`火流星` 与 `火流星_0` 是不同 FName 行，不计成两套装备。`HanBingPiPa` 仅存在交互配置；“霸王链球”“魔手”有原生 UI 行，但尚未关联实际配置。“血色铃铛”有普通/进阶配置，其显示翻译和玩家可达性未确定。

## 可接入的释放阶段

| 装备显示名 | 实际类 / 攻击阶段 | 入口与必要状态 | 费用位置 |
| --- | --- | --- | --- |
| 巨凶斧 | `GA_BrutalAxe_Normal`：`Release1/2/3`、`SprintRelease1/2` | `To Release` 检查蓄力 timeline，并按 `Is Sprint` / `Current Charge` 选择；冲刺二段起点 1.6 秒 | 保留原始激活和蓄力费用；原生强行蓄力另有额外消耗 |
| 火流星 | `GA_Hun_HuoLiuXing_AAAA.Final` | 正常激活成功后 `ToRelease()`；该函数无条件 `PlayTimeline(Final,1,0,-1,None)` | 父类初始费用；Main/Final 均无已枚举的三种影之武装费用动作 |
| 长柄金瓜 | `GA_JinGua_Normal.Release3` | `To Release` 要求 `Is Charging`，并从 0.2 秒开始 | `GAA_ShachiBulletInstant` 在 Main；立即跳转前须确认费用帧 |
| 羽弓 | `GA_UnnamedBow.Shoot/ShootMax` | Main、CanRelease、Perfect 状态控制释放 | 两个 Shoot 各含 `GAA_ShachiBulletInstant` |
| 游龙叉 | `GA_YouLongCha_Normal.Shoot/ShootMax` | Main、CanRelease、Perfect 状态控制释放 | 两个 Shoot 各含 `GAA_ShachiBulletInstant` |
| 独眼弓 | `GA_DuYan_Bow_Normal.Shoot` | Main 与 CanRelease 控制释放；Laser 另有持续状态 | Shoot 含 `GAA_ShachiBulletInstant` |
| 虎炮 | `GA_TigerCannon.Launch/LaunchPerfect` | Main、Ready、Great/Perfect 状态；QuickCharge 继承释放逻辑 | 两个 Launch 各含 `GAA_ShachiBulletInstant` |
| 铁拳套 | `GA_TieKuangQuanTao_Charge_A/AA/AAA.Main@AttackStartTime` | ChargeBase 在蓄力/按住/完美判定后进入实际攻击起点 | 须同时核对起点、拳意效果与原生费用时序 |

### 火流星的最小调用契约

- 能力类：`/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/HuoLiuXing/GA_Hun_HuoLiuXing_AAAA.GA_Hun_HuoLiuXing_AAAA_C`。
- 原生 tag：`Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA`；输入：`/PBZInput/InputActions/IA_ShachiAbility.IA_ShachiAbility`；原生绑定优先级 3。
- 保留地面、`CharacterState.WeaponUpgrade.HuoLiuXing.AAAA` 与 `Weapon.Type.ShaqiWeapon.HuoLiuXing` 装备资格；完整入口跳过前序 `AAA`。
- 先走当前装备授予能力的正常激活；本次能力仍 active 且 `Current Shachi Reinforced=true` 时调用 `ToRelease`。
- `BP_ShachiAbility_Base.K2_ActivateAbility` 根据 `(Has Enough Shachi && Has Enough Bullet) || GetIsFreeUse` 写入该字段，随后自然设置 `Ability.Action.ShaqiReinforced`。不要求空闲时预先持有前序强化 tag。
- 保留初始杀气消耗、弹量判断、免费饰品效果、武器组件、CombatAim 和锁定目标初始化。`Main` 正在播放不足以证明资源成功：失败配置也可能回退普通 Main。

`GA_Hun_ShachiHook_Base → BP_ShachiAbility_Base_Martial → BP_ShachiAbility_Base` 是该类的实际继承链。Main 与 Final 都具有 `GAA_ActivateChain` 及武器/蒙太奇/伤害动作；直接调用应继续使用 Final 自身的动作生命周期。

`PBZ_SubWeaponConfig` 的字段 5 是 `Equip Abilities`，字段 6 是 `Owning Abilities`；`GetEquipAbilities` / `GetOwningAbilities` 分别原样返回对应字段。`Weapon_HuoLiuXing` 的字段 5 明确包含 A/AA/AAA/AAAA/A_Short/DB，字段 6 没有覆盖值；字段 8 的拥有被动则是锁镰与火流星被动。AAAA 属于装备授予的主动能力。字段 0 `Weapon Equip Tags` 包含实际的 `Weapon.Type.ShaqiWeapon.HuoLiuXing`，因此入口显式检查该装备 tag，避免以 ASC 中是否仍持有实例代替当前装备判断。

原生装备路径将 `GetEquipTags` 返回集合以 `+1` 加入玩家 ASC，卸装时以 `-1` 移除；计数函数 `0x144ca8d90` 与 `AbilityUtils.AddLooseGameplayTagsForSystem` 的目标一致。`GA_ASM_UpRunExecution` 与 `GAA_ShachiBulletInstant` 也直接在玩家 ASC 上查询同体系的武器装备标签。完整指令及字段来源保存在 JSON 的 `equipTagsAbilitySystemEvidence`。

### 羽弓、游龙叉、独眼弓的普通释放依赖

三者都已有可反射调用的 `OnRelease(TimeHeld)` 回调。`CanRelease` 只控制原生等待松键 task 的创建；直接调用回调只检查 Main 正在播放，随后执行原生释放、费用与清理。然而 Main 会在后续帧才创建待发投射物，普通 Shoot 只从 Variables 取出并启动它。激活后立即回调会先扣弹，随后因没有投射物而无法射出。

| 装备 | 普通备弹 Main 时刻 | Variables 键 | Shoot 扣弹时刻 | Shoot 发射时刻 | 停止瞄准延迟 |
| --- | ---: | --- | ---: | ---: | ---: |
| 羽弓 | 0.257308602 | `UnnamedBow` | 0.031186514 | 0.040873636 | 0.2 |
| 游龙叉 | 1.025599241 | `UnnamedBow` | 0.031186514 | 0.212641791 | 0.8 |
| 独眼弓 | 0.785894096 | `DuYanBow_1` | 0.031186514 | 0.040873636 | 0.4 |

表中时刻是各自原生 timeline 的编辑秒数；Main 与 Shoot 分别计时。普通备弹均为 `GAAction_ReadyBulletWithAim_C_2`，分别使用 `B_UnnamedBow_Normal`、`B_YouLongCha_Normal`、`BD_GA_DuYan_Bow_Normal_P1_01`。Shoot 内的 `GAA_ShachiBulletInstant` 调用原生费用计算和免费效果；`GAAction_ReleaseBullet` 只有找到 Variables 项且实现 BulletInterface 时才发射。

可调用的实际反射函数是：

- 羽弓：`OnRelease_248DF536488D39C7A1E0B6984801C51E(0.0)`。
- 游龙叉：`OnRelease_477B35FD43F429386880739A65262F2A(0.0)`。
- 独眼弓：`OnRelease_613C82514C82F322FA98A9B9A7C5EC90(0.0)`。

三个回调都保留本类正常释放清理；羽弓与独眼弓停止瞄准移动并恢复默认动画层，之后按各自延迟停止 CombatAim；结束回调移除瞄准/强化状态并处理未释放弹体。游龙叉沿自身的 TargetingHandler 与结束流程清理。羽弓、游龙叉的回调还会读取 Perfect 状态，普通配方必须保持真实分支资格。

接入所缺的条件已明确为普通待发弹体的原生初始化：需要先通过 action 生命周期执行备弹并确认对应 Variables 项，再调用现有 OnRelease。直接重调 Received_ActionBegin、seek Main 或只播放 Shoot 的上下文/时序尚未证明，因此三者暂不接入。

## 逐装备覆盖

| 配置家族 | 原生显示名 / 表行 | 配置数 | 能力资产数 | 已确认阶段与待闭合边界 |
| --- | --- | ---: | ---: | --- |
| BrutalAxe | 巨凶斧 | 2 | 5 | 已确认五个释放攻击分支；Main/Sprinting/Combo 的输入入口仍会进入蓄力过程。 逐分支核对蓄力局部状态、Main/SprintCharge 费用帧与结束清理后，才能给出完整直接调用配方。 |
| CaoOuSiXian | 操偶铃铛 | 2 | 3 | Normal_01 为单一 Main 召唤；普通与进阶召唤被动分别由配置授予。 需保留杀气浓郁区域、召唤寿命和替换已有召唤物等原生资格；当前未确认额外蓄力释放。 |
| DuYanBow | 独眼弓 | 1 | 3 | Normal.Shoot 是已确认的释放攻击阶段；Laser 另有 Main/Loop/Shoot 状态机。 普通弹体由 Main 的 ReadyBulletWithAim_C_2 在 0.785894 秒备入 Variables；立即 Shoot 会扣弹而没有投射物。Laser 仍需闭合持续消耗边界。 |
| HanBingPiPa | 交互配置，无独立 UI 行 | 1 | 1 | 只有 Weapon_HanBingPiPa_Interact 配置，引用破障能力。 需有战斗配置或 UI 行的实际引用才能把它列为第 26 套可装备战斗招式。 |
| HuangJinHuoChong | 黄金火铳 | 2 | 6 | Normal_02/04 各有 Main；Dash/DeflectCounter 为方向或防反分支。 当前未确认新增蓄力释放；内部防反与方向条件尚不支持精确独立招式总数。 |
| HuoLiuXing | 火流星 | 1 | 9 | AAAA 的 Final 已有完整攻击阶段入口，静态证据已闭合到现成 ToRelease。 需要实际游戏核验正常激活后进入 Final 的伤害、结束清理和资源变化。 |
| JianKeZhaoJiLing | 剑客召集令 | 2 | 3 | Normal_01 为单一 Main 召唤；普通与进阶召唤被动分别由配置授予。 需保留杀气浓郁区域、召唤寿命和替换已有召唤物等原生资格；当前未确认额外蓄力释放。 |
| JinGua | 长柄金瓜 | 2 | 6 | Normal 的 Release3 是已确认的释放攻击阶段缺口。 解出 Main 费用帧与 Current Charge 对攻击数值的作用，确定跳过等待时仍执行一次原生费用。 |
| KuYeSiZhuChuan | 苦叶寺珠串 | 2 | 3 | Normal_01 为单一 Main 召唤；普通与进阶召唤被动分别由配置授予。 需保留杀气浓郁区域、召唤寿命和替换已有召唤物等原生资格；当前未确认额外蓄力释放。 |
| LionHead | 烈焰狮头 | 3 | 6 | 喷火主能力 Main→End；FireBall、DoubleFireBall、Dash 另有能力入口。 需核对持续消费与 End 动作是否具有独立攻击语义；现有证据未确认额外蓄力释放。 |
| Lute | 催心琵琶 | 2 | 7 | A/AA/AAA 是独立能力；SKill 为 Main/Loop/End 的持续演奏。 需确认 End 是否有独立伤害、Loop 是否属于同一持续招式，以及起止时的消费和事件资格。 |
| MiaoShouFeiDao | 妙手飞刀 | 3 | 12 | Normal_01/02/03、Escape_01/02/03 为独立能力；DB 内含 Main/Walk/Escape/End。 DB 内部阶段的转入条件与弹体消费尚需收口，不能把每个 timeline 当独立招式。 |
| Mojian | 墨血无痕 | 1 | 9 | A/AShort/AA/AAA 有独立输入入口，延迟爆炸由既存光轮与被动状态驱动。 需核对光轮存续、引爆消费及处决目标初始化；当前未确认额外无条件蓄力释放。 |
| NiTouChe | 泥头车 | 1 | 2 | 实际配置引用 HorseTest 的骑乘攻击，不能以 Test 路径排除。 需串联已生成车辆、驾驶与转向状态，再判定 TurnAttack 是否可独立进入；不得略过车辆生成与持续消耗。 |
| ShachiHook | 追命锁镰 | 3 | 16 | C/CA/CAA/CB/CBB/CC/CCC/CCCC/CD/CDD 有独立能力；Drag 与 HookMove 是目标/链状态机。 需保留链、目标和空间条件，不能在无锚点时直接调某个移动阶段。 |
| TanLang | 贪狼 | 1 | 5 | Normal 的 Main/Loop/FastEnd 属于持续抡动状态；ExtraAttack、SpeicalAttack 为独立能力。 需确认结束/循环的伤害与资源边界；当前不能宣称该家族所有完整招式均已覆盖。 |
| ThunderSpear | 雷霆枪 | 1 | 9 | 三段 Normal、DodgeAttack、SpecialAttack 已是独立能力；SpecialAttack 包含近身分支。 近身、方向和完美防反分支的状态与 native 动作未完全收口；当前未确认新增蓄力释放。 |
| TieKuangQuanTao | 铁拳套 | 1 | 6 | Charge_A/AA/AAA 各自在 Main 中有等待与实际攻击起点，输入 tag 没有消除该等待。 需解出三个 CDO 的 AttackStartTime 字段及效果/费用时序；PerfectLogic 是攻击修饰状态，不能作为一招。 |
| TigerCannon | 虎炮 | 2 | 7 | Launch、LaunchPerfect 是已确认的释放攻击分支，QuickCharge 继承该逻辑。 普通发射可沿用攻击阶段费用；Great/Perfect 的数值、资格及 QuickCharge 差异仍需逐一核对。 |
| UnnamedBow | 羽弓 | 2 | 5 | Main 后的 Shoot、ShootMax 是已确认的释放攻击分支。 普通弹体由 Main 的 ReadyBulletWithAim_C_2 在 0.257309 秒备入 Variables；立即 Shoot 会扣弹而没有投射物，需先执行原生备弹生命周期。 |
| XuKongHuaShan | 虚空华扇 | 1 | 2 | Skill 有单一 Main；Reborn 为复活上下文能力。 需保留死亡/复活状态及相关升级；当前未确认可跳过蓄力的额外攻击。 |
| XueDiZi | 血滴子 | 2 | 10 | A/AA/AAA 各有 Main→Saw 的命中条件攻击分支。 直接 Saw 需要真实命中对象、弹体与抓取状态的初始化证据；它不是无条件可提前执行的蓄力释放。 |
| XueSeLingDang | 表行“血色铃铛”，显示翻译缺失 | 2 | 3 | Normal_01 为单一 Main 召唤；普通与进阶召唤被动分别由配置授予。 原生显示翻译缺失，且是否向玩家开放未确定；需追踪实际装备/解锁入口和召唤对象。 |
| YouLongCha | 游龙叉 | 2 | 6 | Normal 的 Shoot、ShootMax 是已确认的释放攻击分支。 普通弹体由 Main 的 ReadyBulletWithAim_C_2 在 1.025599 秒备入 Variables；立即 Shoot 会扣弹而没有投射物，需先执行原生备弹生命周期。 |
| YouLongHead | 游龙头 | 2 | 4 | Water 为 Main→End 持续喷水；WaterCannon 有 Main/End/End_V2。 WaterCannon 的两个 End 与处决 Cannon 的条件未完全收口，不能按 timeline 数计招式。 |
| ZhuoBiDun | 拙壁盾 | 2 | 6 | 主能力内有 Release、CounterAttack、Push、Rush 等攻击/姿态分支，尚无完整阶段配方。 沿 HandleInputAction/ToRelease 的状态机与费用入口收口；跳过举盾不能凭空制造已吸收伤害或反击资格。 |

## 证据索引

机器可读的[逐装备证据](complete-move-shadow-audit.json)保存全部配置路径、实际导入能力、继承关系、原生名称/本地化键与表内偏移、输入路线及 required/ignored tags、各能力自身的 timeline、费用动作、已确认阶段的函数与指令偏移。`cdoAbilityTagReferences` 只表示 CDO 中出现过的 tag，不一概视为激活 tag。

`/Game/Test/HorseTest/GA_HorseCart_Charge` 和 `GA_Passive_NiToucheNavMeshCheck` 被泥头车实际配置引用，属于本次覆盖范围；巨凶斧 Archive 下的 Combo 同样由实际配置引用。

血滴子三段的 `GAA_JumpTimelineByHitResult` 实例均指定 `Saw` 与 `Bullet.HitResult.DirectHit`。共享动作的 `SetBulletHitResult` 先执行 `MatchesAnyTags`，成功后才直接跳转或在 action 结束时跳转；Saw 是命中结果分支，完整入口必须保留真实命中和目标状态。

## 已知限制

证据来自 CL-302256 的已安装 cooked 资产，未进行游戏运行验证。脚本函数已全部解码；native action 的内部实现、全部 timeline 帧字段、派生类默认数值和资源/状态闭环仍有逐装备列出的缺口。独立招式精确总数保持未知，不据此宣称全部完整招式已经接入。
