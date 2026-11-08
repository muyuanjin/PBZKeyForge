# 玩家武器完整招式审计

CL-302256 的资源全集包含 36 个玩家主武器配置和 4 个临时武器配置。全部 40 件均已建立能力、继承、内部 timeline 与现有目录映射。逐装备表记录资源结构与输入能力的对应关系；完整阶段接入见[完整招式](complete-moves.md)和[战技配方证据](complete-move-combat-art-recipes.json)。

本报告使用安装包的 AssetRegistry、Cooked 导出表、CDO 引用和 Blueprint 字节码。结构化清单见 [complete-move-weapon-audit.json](complete-move-weapon-audit.json)。

战技清点从实际 `Equip Abilities` / `Owning Abilities` 重新读取，不按 Light/Heavy 角色分类或等待输入特征过滤。40 个配置授予的 35 个战技类共有 84 条有效时间线、2707 个动作。已接入 40 项独立战技阶段，包含 18 项后段或原生结束攻击、炽刃三档释放和 19 项内部阶段；其余时间线保留为结构与条件分支，不能按伤害节点数量直接计作招式。完整覆盖见[战技阶段覆盖](complete-move-combat-art-coverage.md)。维护入口为 `build/extract-combat-art-phases.py`，原容器验证为 `tests/CombatArtPhaseResourceTests.py`。

## 全集边界

AssetRegistry 共 267,026 个资产，其中 5,462 个 LegoAbilityBlueprintGeneratedClass。203 个主武器配置中，36 个属于玩家、167 个属于敌人；另有 4 个玩家临时武器。能力读取包含 1,551 个玩家能力及引用/继承类，装备直接关联 1118 个能力类。通过类枚举确定集合，再跟随配置引用与 ParentClass，不以文件名前缀证明完整性。

## 已补入完整招式目录的能力入口

| 精确 tag | 具体能力 | 正常资格与入口 |
| --- | --- | --- |
| `Ability.Action.Hun.Light.Air.AA` | `GA_Hun_Air_Tang_AA`、`GA_Hun_Air_Double_AA` | 保留空中状态；正常激活继承的空中轻击模板。 |
| `Ability.Action.Hun.Light.Air.AAA` | `GA_Hun_Air_Double_AAA` | 保留空中状态；正常激活空中轻击落地模板。 |
| `Ability.Action.Hun.Heavy.ComboAtk_N3H3` | `GA_Hun_Double_AAABBB` | 保留地面状态；正常重击基类处理杀气和强化。 |

这四个具体类均无本地 UFunction 覆盖。tag 来自其直接连招基类的 CDO；激活链在本次能力实例内初始化动作、角色、落地和播放任务，没有读取前一段能力对象，也没有等待原攻击键保持或释放。唐刀空中 AA 的资源虽位于 Archive 目录，仍被七个玩家装备配置实际引用。

## 内部阶段与持续输入缺口

| 能力 | 已有目录入口的行为 | 完整招式处理 |
| --- | --- | --- |
| 青铜剑 `B_Hold` | 原能力建立 `IA_AttackH_Hold` 0.5 秒等待。 | `EndSuccess` 明确播放 `Release2`；必须先走正常激活和资格，不能由技能文字推断剑气视觉。`Release1` 已作为提前释放单独接入。 |
| 白山风雷剑 `B_Hold` | 原能力每 tick 消耗 `350 × DeltaTime` 杀气，并累计雷能。 | 已接入原生持续蓄力：保持真实重击 Hold，保留每秒 350 杀气消耗与雷势累积，由原 Tick 取得 Thunder 后进入 End_Success，再停止虚拟 Hold。 |
| 残钢刃 `BCharge` | 仍等待原重击键与最短蓄力事件。 | 后续目标为 `BJump`；目标已有目录，蓄力入口本身不等于完整出招。 |
| 长形黑伤 `BCharge` | 等待原重击释放或 Combo 阶段。 | 原回调请求 `Ability.Action.Hun.Heavy.Long.BBB`，该 tag 尚未在当前目录建立。已单列的普通第三段使用实际授予类与 `Ability.Action.Hun.Heavy.Atk03`；它不证明旧蓄力回调能够完成转招。 |
| 红妆鬼斩 `B_Hold_Start` | 等待保持/释放和 SuccessHold 事件。 | 完整释放与提前释放分别接入。提前释放调用本次 Start 的原 OnRelease(0)，原 MinDuration 和 Current Reinforced 选择 BB_UnCharge 或强化分支，保留已支付费用。 |
| 灵蛇软剑 `DA` | 轻击保持决定 `Main → Loop → End`。 | 已接入有限释放：正常进入 Main/Loop 的真实攻击后释放原轻击 Hold，原回调进入 End 并结束 Tick；End 本身为收招。 |

青铜剑候选的独立核对：`EndSuccess` 进入 ubergraph 内存偏移 `0x8c4`，检查并设置 `Ended` 后，`0x8de` 调用 `PlayTimeline(Release2,1,0,0,None)`；`EndFail` 在 `0x928` 播放 `Release1`。原重击基类仍处理正常杀气扣除与强化，不应替换为直接播放资源。

红妆完整蓄力释放的静态入口为 `EventReceived_7594CBDE4CE41BEBFC9ABFBA4E2D1DF1({})`。先以 `IA_AttackH_PressLong`、优先级 2 正常激活已授予的 `B_Hold_Start`，保留地面、`GuiTouBlade.CanCharge`、未在 `InBHold` 及原生 GAS 资格；在该次激活且仍活跃的同一实例上调用一次。wrapper 的事件数据只被复制，未参与分支；它结束释放等待任务，再按本次正常重击生成的 `Current Reinforced`，经原 `ForceActivateExternalActionAbilityByClass` 激活 `B_Hold` 或 `B_Hold_Unreinforced`，输入仍为 `IA_AttackH_Press`、优先级 2。Start 和释放类各自原有的重击费用均保留。正常等待图没有按时长扣费或累积资源的逻辑。无物理持键时，`OnAlreadyRelease` 不清除 `Holding`，也不触发提前释放分支。

两件装备的身份与授予字段已从 `PBZ_MainWeaponConfig` 类 schema 直接解析：index 0 是 `Weapon Equip Tags`，index 5 是 `Equip Abilities`，元素类型为 `GameplayAbility` 类。天玄青铜剑的装备标签包含 `Weapon.Type.BronzeSword`，41 个 Equip Abilities 含 `GA_Hun_BronzeSword_B_Hold` 和唐刀空中 AA；红妆鬼斩的装备标签包含 `Weapon.Type.GuiTouBlade`，39 个 Equip Abilities 含 `B_Hold_Start` 与强化 `B_Hold`。未强化释放由原有按类激活分支取得，不需要额外授予。完整字段值及 getter 字节码保存在结构化清单中。

白影突袭通过原生处决通道精确类型执行；白蟒赤练防御派生使用现有授予的精确类正常激活。别离残钩抓取由原 DB 的真实受击事件自动选择目标并进入，不额外构造命中或抓取事件。被动、受处决者能力、属性标签、架势与攻击分别列出。

## 特殊重击与切换攻击

| 武器 | 已接入入口 | 保留的原生关系 |
| --- | --- | --- |
| 红妆鬼斩 | 蓄力提前释放 | Start 正常处理杀气后，原释放与最短时长事件选择两种 BB_UnCharge。强化子类原三项费用为零，绑定由已付费的 Start 进入。 |
| 刺骨混沌 | 火、冰、物理形态的 SWT 攻击；切换武器攻击 | 三个叶能力分别保留 Fire、Ice、Normal 必要状态与 Hitreact.Phase.Combo 阻止条件；切换绑定按真实武器槽转入原攻击，允许已持有目标武器及切换冷却期间请求。 |
| 别离残钩、天煞双钺 | 各自 DB_SWT 及原生切换武器入口 | 精确激活装备授予的原类，保留防反、真实受击对象与攻击选择；换武器由原 wrapper 解析配置、槽位和目标索引。 |
| 荡寇凶刃 | BladeWave 的 NoCostB 收招 | 正常激活前必须存在真实 Horizontal.Charging；沿用剑气已支付的费用，状态在原转招时可以结束。 |

这 10 项配方使用正常按类激活，只读取游戏已授予的能力。叶攻击来自 `Equip Abilities`（字段 5）；三个切换 wrapper 来自 `Change Weapon Abilities`（字段 9），通过 `GetChangeWeaponAbilityWeapon` 取得原生映射。wrapper 的 `Ability Tag` 是后续攻击目标，激活身份仍是 `Weapon.Switch.Normal/Dash`；两者分别记录。证据与 15 语言原厂武器名称见[特殊入口](complete-move-weapon-special-entries.json)，原容器检查为 `tests/WeaponSpecialEntryResourceTests.py`。

## 逐装备覆盖

| 装备身份 | 原厂名称/变体 | 关联能力 | timeline 结构 | 原输入目录缺失 tag 能力 | 阶段/持续输入缺口 |
| --- | --- | ---: | ---: | ---: | ---: |
| `Weapon_Hun_BaiShanFengLeiJian` | 白山风雷剑 | 49 | 142 | 1 | 1 |
| `Weapon_Hun_BaiYing` | 白影 | 40 | 127 | 1 | 0 |
| `Weapon_Hun_TianXuanQingTongJian` | 天玄青铜剑 | 51 | 159 | 2 | 1 |
| `Weapon_Hun_CiGuHunDunJian` | 刺骨混沌 | 76 | 206 | 5 | 0 |
| `Weapon_Hun_ChiZheCanJian` | 痴者残剑 | 59 | 197 | 1 | 0 |
| `Weapon_Hun_ChiZheZuiJian` | 痴者醉剑 | 59 | 197 | 1 | 0 |
| `Weapon_Hun_Double` | 黑伤双刃 | 57 | 161 | 4 | 0 |
| `Weapon_Hun_JueYing` | 绝影 | 64 | 169 | 4 | 0 |
| `Weapon_Hun_DualHook` | 别离残钩 | 52 | 157 | 3 | 0 |
| `Weapon_Hun_GuiTouBlade` | 红妆鬼斩 | 49 | 154 | 4 | 1 |
| `Weapon_Hun_HeiMoShang` | 黑魔伤 / 黑伤 | 55 | 166 | 1 | 0 |
| `Weapon_Hun_HeiMoShang_Prologue` | 黑魔伤 / 黑伤 | 55 | 166 | 1 | 0 |
| `Weapon_Hun_Horizontal` | 荡寇凶刃 | 45 | 143 | 2 | 0 |
| `Weapon_Hun_Iron` | 残钢刃 | 50 | 154 | 1 | 1 |
| `Weapon_Hun_JadeDual` | 玉双环 | 57 | 164 | 4 | 0 |
| `Weapon_Hun_JadeDual_Evaluation` | 玉双环 | 58 | 164 | 4 | 0 |
| `Weapon_Hun_Jigsaw` | 断肢锯 | 45 | 130 | 1 | 0 |
| `Weapon_Hun_ChiRen` | 炽刃 | 58 | 159 | 3 | 1 |
| `Weapon_Hun_Long` | 长形黑伤 | 48 | 142 | 3 | 1 |
| `Weapon_Hun_LuanYing_Evolution` | 乱莺剑 | 54 | 163 | 2 | 0 |
| `Weapon_Hun_MoQie` | 未建立原厂显示名 | 36 | 102 | 0 | 0 |
| `Weapon_Hun_XieHuo` | 炽刃 | 36 | 102 | 0 | 0 |
| `Weapon_Hun_ZhuGuangBaoQi` | 未建立原厂显示名 | 46 | 149 | 1 | 0 |
| `Weapon_Hun_PoJun` | 破军 | 41 | 141 | 1 | 0 |
| `Weapon_Hun_ShuangYue` | 天煞双钺 | 51 | 156 | 3 | 0 |
| `Weapon_Hun_SlitheringSword` | 灵蛇软剑 | 55 | 177 | 2 | 1 |
| `Weapon_Hun_SnakeDual` | 白蟒赤练 | 83 | 218 | 2 | 0 |
| `Weapon_Hun_Tang` | 黑伤 | 51 | 156 | 2 | 0 |
| `Weapon_Hun_Tang_HorseCartCombat` | 黑伤 | 29 | 108 | 0 | 0 |
| `Weapon_Hun_Temp_HouHouHouHead` | 未建立原厂显示名 | 0 | 0 | 0 | 0 |
| `Weapon_Hun_Temp_MaleDoll_Arm` | 未建立原厂显示名 | 17 | 81 | 0 | 0 |
| `Weapon_Hun_Temp_MaleThug_Club` | 未建立原厂显示名 | 15 | 78 | 0 | 0 |
| `Weapon_Hun_Temp_MaleThugTwinAxe` | 未建立原厂显示名 | 16 | 74 | 0 | 0 |
| `Weapon_Hun_WoodenSword` | 木制黑伤 | 44 | 137 | 1 | 0 |
| `Weapon_Hun_Young_WoodenSword` | 木制黑伤 | 43 | 110 | 1 | 0 |
| `Weapon_Hun_XieShan` | 圣母邪扇 | 45 | 140 | 1 | 0 |
| `Weapon_Hun_YanYueDao` | 偃月刀 | 37 | 131 | 1 | 0 |
| `Weapon_Hun_YuuBlade` | 妖锯 | 47 | 141 | 1 | 0 |
| `Weapon_Hun_ZhanMaBlade` | 亡命斩马刀 | 43 | 155 | 1 | 0 |
| `Weapon_Hun_ZiMingJian` | 紫冥剑 | 49 | 166 | 2 | 0 |

名称来自原厂 `DT_WeaponData` 中完整 SoftObjectPath 与简体中文 `Game.locres` 的关联；同一行存在普通/觉醒或序章配置时，JSON 保存各条来源及偏移。未取得翻译的占位和临时配置保留资源身份。表中 timeline 数是继承覆盖后的结构数量，不代表招式数量。

## 已知缺口

每件装备的具体能力路径、内部 timeline、ActionPhase 对象、输入目录映射和持续输入证据在 JSON 中逐项记录；全量结构清点不等同于所有时间线都可独立进入。全部 40 个配置的 Equip/Owning 授予和 35 个战技类已重新读取，精确阶段以[战技配方](complete-move-combat-art-recipes.json)和[完整招式目录](../assets/key-enhancement/complete-moves.json)为准。主武器提前释放、原生蓄力、剑花如盾准备与清理由 `tests/WeaponReleaseResourceTests.py` 实读原资源检查。

原生 GAS 资格、解锁、装备、目标和资源条件未全部逐字段还原；已支持的普通能力候选应保留正常激活检查。未启动或注入游戏，静态结论不代表实机输入与视觉效果验证。
