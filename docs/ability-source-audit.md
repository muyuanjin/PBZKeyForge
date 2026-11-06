# 能力来源审计

游戏构建 `CL-302256`。清点依据为全量 AssetRegistry、实际序列化软/硬引用及原生 GameplayEffect 组件；未进行游戏运行验证。

全表扫描覆盖 380 个 DataTable、1639 个来源根和 9531 个引用资源。能力类全集为 5728，保留所有未归属项。

| 来源分类 | 类数 |
| --- | ---: |
| NPC 来源引用 | 304 |
| 玩家与 NPC 来源均引用 | 244 |
| 玩家来源引用 | 1316 |
| 其他配置引用 | 18 |
| 来源尚未归属 | 3846 |

引用链用于发现候选，不能据此认定玩家能独立施放。目录不作为排除依据：魂的 ActorConfig 实际引用了 `Mobs/054_ZuoShang/Ability/Abilities/GA_ZuoShang_CA_03_QTE`。AI 的 RunSubtreeAbility 行为控制器不计入 GAS/Lego 能力类。

已解码 112 条真实能力授予，组件分别为 AbilitiesGameplayEffectComponent 19、GrantAndActivateAbilities 95、TargetTagsGameplayEffectComponent 738。JSON 保存授予效果、实际组件类型、能力、激活方式和最短玩家来源链。未声明为授予的对象仍只是引用候选。

武器之外的实际来源包括：

- `DT_Accessory` 的软对象路径指向饰品 GE；GE 的 GrantAndActivateAbilities 实际授予 RandomDrink、GhostFire 和原生状态类被动。饰品行为见 [饰品审计](accessory-move-audit.md)。
- `DT_AbilityNodesTable` 保留训练节点对象来源；`DT_ShaqiWeaponData` 实际软引用雷霆枪升级 GE，其组件添加 CA、DashAtk、HuiMaQiang、LightningChain 和 Lv1–3 标签，招式条件必须检查这些真实标签。
- 成年魂与幼年魂的 ActorConfig 引用不持械闪避、处决、跳跃、特殊地形动作及不同药葫芦能力。它们属于玩家来源，不能用主武器清单代表。
- `DT_InventoryData` 实际引用 `GE_SecondWind`、`GE_ShachiCore`、`BP_ReturnToBell`；药物还包含不尽水被动。现有证据对应恢复、增益和返回功能。

JSON 同时保留全部玩家引用能力候选、局部时间轴及目录标签匹配。标签存在不能证明阶段齐全：共用标签的武器大招必须继续检查时间轴、分支和消耗入口。

对未归属类再按完整继承链检查，得到 53 个玩家相关候选；反向搜索 104190 个全目录 Blueprint、能力、效果、子弹、配置、表格及 World 包（包括流送单元），49 个候选有真实序列化来源。逐项证据见 [玩家来源差集](player-ability-source-gaps.json)。其中 31 个是玩家情境完美闪避反制，9 个是被执行方，2 个是剧情/敌招中的成对执行，3 个是移动，2 个是教程/UI 换武器，3 个是被动升级，3 个是 NPC 继承的玩家动作。

31 个反制由敌招发出的实际反制帧临时授予。绑定通过原生 `TryActivateExternalActionAbilityByClass` 精确选择已有能力，保留输入和优先级；原生资格检查继续要求真实反制帧及匹配的 CounterAbility。配方不授予能力、不跳入时间轴，也不调用被执行方。详见 [情境反制配方](contextual-counter-recipes.json)。

反向搜索未找到来源的 4 项为 `GA_Hun_Double_C_JueYing_Summon`、`GA_Hun_BEx_ZuoSHang_P2_Skill_20`、`GA_Hun_Double_SWT_Tutorial2`、`GA_ASM_ClimLadder_2`，保留真实类和继承证据。游龙叉水下被动、鬼头刀花增益、狂暴被动、束缚中的冲刺及翻障已找到各自 GE 或移动 Volume 来源。

## 已知边界

全类表仍保留来源根闭包以外的类，并单独记录已核对的 53 个玩家相关差集；NPC 控制类不据目录名称推断为玩家动作。原生被动的真实事件 payload、继承默认值、DataTable 行字段与状态/消耗条件需要逐项证据。当前清点未确立可独立绑定的消耗品、坐骑或攀爬攻击终点。

生成：`build/extract-ability-sources.py` 读取游戏资源，`build/summarize-ability-sources.py --source <原始输出> --output docs/ability-source-audit.json` 生成清点与本文。资源断言：`tests/AbilitySourceResourceTests.py`。
