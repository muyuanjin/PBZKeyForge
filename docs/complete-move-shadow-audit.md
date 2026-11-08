# 影之武装完整招式审计

CL-302256 的输入能力入口与内部攻击阶段分别清点。当前已接入火流星 Final，以及巨凶斧、金瓜、弓、虎炮、拳套和若干持续能力的有限释放/结束攻击；配置以 [完整招式目录](../assets/key-enhancement/complete-moves.json) 为准。

## 资源范围

AssetRegistry 共 267,026 个资产，包含 46 个 `PBZ_SubWeaponConfig_C`、26 个配置家族；原生 UI 表 `DT_ShaqiWeaponData` 有 28 行。初始静态审计覆盖影之武装基类的 100 个继承类、配置引用和父类/动作/条件依赖，共 348 个包、1,329 个函数。29 个相关输入绑定的触发 tag 均在输入目录中，但这不代表内部阶段已经覆盖。

全来源检查还跟随能力修行、升级效果、角色、饰品与道具的数据表及 hard/soft 引用。普通 Blueprint 能力、原生父类及文件夹外的真实引用同样属于候选；攻击节点、时间线和独立招式分别记录。

## 已接入的有限阶段

| 装备 | 独立阶段 | 原生准备与消费 |
| --- | --- | --- |
| 巨凶斧 | 三档普通释放、两档冲刺释放 | 原生 `To Release` 按实际蓄力值和 SprintCharge 选择；保留升级、蓄力费用与档位窗口。 |
| 火流星 | AAAA.Final | 正常激活与本次强化资格后调用 ToRelease；Final 自带武器展开、动画与攻击。 |
| 长柄金瓜 | 三档释放 | 等待原生 Charge1/2/3 标签后 To Release，保留 Main 中的费用。 |
| 羽弓、游龙叉 | 普通、完美、满蓄力投射 | Main 自然备弹，核对 Variables、BulletData 与 Ready 状态后原 OnRelease；错过精确档位窗口取消。 |
| 独眼弓 | 三档普通投射、Laser.Shoot | 普通投射核对各档实际备弹；激光正常准备到 Loop，再 Force Release，保留升级、已有激光销毁、发射费用和瞄准清理。 |
| 虎炮 | 普通/快速蓄力的普通、Great、Perfect 发射 | Normal 与 QuickCharge 是同时授予的不同类和 tag；分别使用各自 Main 的真实窗口与解锁资格。 |
| 铁拳套 | C/CC/CCC 三段完美蓄力释放 | 原生 CanCharge 与 PerfectTime；先停止虚拟 Hold，等真实松开，再沿原 Tick 进入 AttackStartTime，已经自然进攻时不重放。 |
| 烈焰狮头 | End 的两段实际攻击 | 保留 Main 首笔费用，原 OnRelease 结束输入和资源观察任务，End 自带攻击。 |
| 催心琵琶 | Skill.End 的四个投射攻击 | Main 自然完成进入 Loop 并执行首笔费用后 ToRelease，保留原计时器与结束清理。 |
| 妙手飞刀 | DB.End 的四个投射攻击 | 保留 Main 费用与本次强化，原 PlayEnd 结束 ForceSprint 和自定义移动。 |
| 贪狼 | FastEnd 的近身与投射攻击 | 正常进入 Main 后停止 Hold，等待真实松开，再原事件选择 FastEnd；已经自然进入结束段时不重复。 |
| 拙壁盾 | Push_F、Rush_F、CounterAttack | Push 保留原生举盾到 Loop 后的费用回调；Rush 保留升级和冲刺或奔跑资格；反伤冲击只释放已在 Loop 且真实吸收量足够的盾，第二击由原生 Counterx2 升级选择。 |
| 雷霆枪 | DeflectCounter_Normal | 装备直接授予的独立能力；正常启动自身费用与攻击，仅在真实完美闪避事件发生时转入 Perfect。 |
| 虎炮 | Counter | 原生反击资格成立时请求 ShachiWeapon 处决通道，由原生通道初始化真实目标、费用和攻击，见[防反上下文](complete-move-counter-contexts.md)。 |

精确类、反射入口、回调参数、时间窗、费用帧、准备弹体和官方 15 语言名称键保存在 [释放阶段证据](complete-move-shadow-recipes.json)。`tests/ShadowMovePhaseResourceTests.py` 从安装的原容器重新核对这些关系，不启动游戏。

## 条件攻击与其他来源

| 家族/类型 | 必须保留的上下文 |
| --- | --- |
| 雷霆枪等修行追击 | 已接入 8 项官方追击、10 条原条件路线，见[修行追击证据](complete-move-shadow-training.json)。保留真实前驱、强化与解锁；雷霆再临仍由 Main 的原事件判断锁定目标与近身分支。 |
| 血滴子 Saw | A/AA/AAA 的原生 DirectHit 事件立即转入 Saw，三个动作实例均覆盖 `Jump On End=false`；该阶段随真实命中自动执行，原攻击入口已包含此分支。 |
| 拙壁盾吸伤反击 | 已接入反伤冲击：仅对正在举盾的原实例调用 ToRelease，真实 PhysicalTotal+ShachiTotal 必须严格大于 Thredhold；保留吸收量决定的伤害、升级第二击和收招。 |
| 游龙头水炮 | 原生持续喷水与 End/End_V2 分支、结束射击费用和已有弹体。 |
| 追命锁镰 | 链、真实目标、锚点和空间状态；独立能力与 Drag/HookMove 上下文分别处理。 |
| 墨血无痕 | 已存光轮、延迟引爆和处决对象；被动效果不自动等同单次攻击。 |
| 泥头车 | 配置实际引用 HorseTest 的车辆能力；生成、驾驶、转向和持续费用均属准备。 |
| 召唤类 | 操偶铃铛、剑客召集令、苦叶寺珠串等保留召唤区域、寿命、替换与已有对象状态。 |
| 虚空华扇复活与处决类 | 死亡/复活状态、目标和相应解锁；不是空闲状态的自由攻击。 |

黄金火铳、雷霆枪的普通连段与闪避等特有输入能力保留原目录。雷霆枪 DeflectCounter 没有原输入映射，已按装备授予单独接入完整招式。寒冰琵琶只有交互配置；“霸王链球”“魔手”有 UI 行而未关联实际配置。血色铃铛的普通与进阶配置均授予召唤能力；目录用中性资源身份 `BloodBell` 显示该入口，原厂显示名标记为未解析。

## 按装备列出的原生能力入口

全部 46 个配置的实际 `Equip Abilities` 与 `Owning Abilities` 数组、能力继承和 CDO 输入身份保存在[装备入口证据](shadow-move-entries.json)。其中 63 个能力已有装备特有的输入入口，8 个已有精确类招式入口；另外 18 个入口分属 16 个家族，写入 [shadow-moves.json](../assets/key-enhancement/shadow-moves.json)，不再只通过共享 `ShachiAbility.Default` 寻找。

| 家族 | 新列出的入口 |
| --- | --- |
| 操偶铃铛、剑客召集令、苦叶寺珠串、虚空华扇 | 各自实际授予的普通能力 |
| BloodBell | 血色铃铛普通与进阶配置授予的召唤能力，保留中性资源显示身份 |
| 黄金火铳、烈焰狮头、催心琵琶、泥头车、游龙头、拙壁盾 | 各自基础能力的精确类入口 |
| 独眼弓、羽弓、游龙叉 | 原生蓄力入口；有限释放档位沿用完整招式配方 |
| 墨血无痕 | `AA`，原输入目录没有该能力的独立 tag 条目 |
| 追命锁镰 | `CA`、`CB`、`CD`，分别保持自身能力身份 |

新入口只请求当前角色已获得的精确能力类，保留原生资源、解锁、目标及生命周期；蓄力、瞄准、持续输入与释放仍按原规则执行。18 类均由 `BP_ShachiAbility_Base` 继承杀气与影之武装能量资源关系，费用选项按本次精确类匹配，不将共享 tag 的其他能力混入。`tests/ShadowMoveEntryResourceTests.py` 实读原容器检查授予、继承、原始 InputAction 和逐类资源元数据。

饰品的鬼火发射仍使用现有活跃实例配方；饮药随机效果、闪避幻影、感电与点燃连锁依赖真实来源事件，未作为空闲时可自由激活的能力入口。证据与边界见[饰品攻击](accessory-move-audit.md)。

## 官方名称与持续输入

[官方名称资源](../assets/key-enhancement/game-move-names.json)按现有目录身份保存 15 语言显示名、检索别名及相关修行效果。原生输入路线的解锁标签通过数据表中实际序列化的升级效果引用关联官方名称；能力、弹体和攻击动作的升级标签引用用于关联效果。全部 26 家族的官方名称及已建立或尚未建立的入口关系见[名称证据](game-move-name-shadow-evidence.json)。

独眼弓的激光入口使用官方名称“死光”。“凝视”是持续发射升级，“千目凝视”在凝视两侧生成眼球，“透射”分三档改变穿透后的伤害；这些名称可以检索到原生激光入口。凝视结束攻击保留 Loop 中 0.0533988997 秒和 0.0559388995 秒的两个眼球事件，在 0.056 秒以后进入原释放回调。

[持续输入证据](move-restrictions-shadow.json)记录实际 `WaitSInputRelease.CreateTask` 与 `SGameInputComponent.IsHolding` 的 InputAction 实参。独眼弓普通蓄力需持续注入实际 `IA_ShachiAbility_Hold`，直到 Main 的首档弹体备妥；释放虚拟输入后仍由原生回调发射。CDO 中出现的其他能力标签不作为输入归属依据。

## 证据与限制

[完整初始资源清单](complete-move-shadow-audit.json) 保存 26 家族、实际配置与导入、继承、原生 UI 行和指令偏移。`cdoAbilityTagReferences` 表示 CDO 中出现的标签，不一概解释为激活资格。

静态资源、原生程序与本地执行器测试分别提供证据，尚未进行游戏内动画、伤害和消费验证。精确独立招式总数保持未知；资产清点完成不代表每个条件攻击均已有独立绑定配方。
