---
name: phantom-blade-zero-save-modding
description: 为《影之刃零》开发键位编辑与资源 Mod 功能，提取动作与逐槽原厂映射，无损读写 EnhancedInputUserSettings.sav，认证读写 PBZSharedSetting.sav 中的方案选择，并管理完美防御无连点惩罚资源补丁。
---

# 影之刃零：键位与方案存档开发

本技能记录 PBZKeyForge 的输入映射、存档格式和资源 Mod。普通改键与方案选择写存档，完美防御无连点惩罚使用独立资源容器；静态研究本身不隐含启动、注入或安装 Mod 的授权。

## 游戏与 UE4SS 更新

涉及游戏兼容性、加载器、构建或发布时，先运行 `build/check-runtime-updates.ps1`；用户反馈游戏更新或更新后失效时加 `-Force`，指定安装时传 `-GameDirectory`。脚本同时查询 UE4SS 正式 PBZ 包与 Steam 公共构建，检测同一标签下重新上传的资源。出现 `NeedsReview` 后按[更新与适配](references/runtime-updates.md)继续取得候选、验证并修正适配，不能停在“发现新版”的通知。发布使用 `-Release` 重新查询，不把旧缓存或安装测试当作新游戏的运行证据。

## 先区分修改对象

| 目标 | 实际落点 | 关键区别 |
| --- | --- | --- |
| 键鼠绑定 | `EnhancedInputUserSettings.sav` 属性段之后的共享 profile records 中，原厂归属键鼠的槽 | Blob 不限于键鼠；按 `ProfileId`、mapping、真实原始槽和硬件身份定位。 |
| 手柄绑定 | 同文件 `GamepadFamilyProfiles` → `(ProfileId, Family)` → `Keys` | 同为 Xbox 的记录也可能属于不同方案；不能全部一起改。 |
| 原厂绑定与继承后的有效按键 | 游戏 IMC 按方案选择的映射 → 注册槽位；手柄另有保存的 `DefaultKey` | 原厂集合、逐槽默认与当前覆盖是三种数据，不能相互冒充。 |
| 游戏采用哪个方案 | `PBZSharedSetting.sav` 认证解密后的 JSON `KeyBindingScheme` | 单改 GVAS 的 `CurrentProfileIdentifierString` 不能代替它。 |
| 取消完美防御连点惩罚 | `ProjectPBZ/Content/Paks/~mods/PBZKeyForge_PerfectDeflectNoRapidClickPenalty/` | 独立资源 Mod，所有方案、难度及防御按键共享；主页面勾选后随“应用更改”安装或移除，不改存档。 |

已研究的 Steam 安装中，两份存档在游戏根目录的 `ProjectPBZ/Saved/`，实际原生程序在 `platform/steam/PBZ.exe`。不要把较小启动器当作该程序，也不要把此路径推广到未经检查的其他发行版。

## 按任务读取资料

- 读写动作、槽位、手柄记录、制作一键攻防：[键位格式与编辑](references/input-bindings.md)。
- 排查攻防同键的完美防御表现、解释绑定列或分离点按与架势：[防御输入与战斗行为](references/deflect-behavior.md)。
- 提取或展示原厂键、解释默认继承、计算差异与功能数：[默认映射与有效绑定](references/default-bindings.md)。
- 读取当前生效方案、实现启用功能、处理 PBZS 加密：[生效方案与认证容器](references/scheme-activation.md)。
- 判断旧指引是否可信、适配游戏更新、说明验证程度：[研究依据与版本边界](references/research-evidence.md)。
- 增加独立招式、拆分战技或修复持续出招：[完整招式](../../docs/complete-moves.md)及其武器、影之武装资源证据。
- 启用/关闭完美防御无连点惩罚、资源来源与跨版本限制：[资源 Mod](../../docs/perfect-deflect-no-rapid-click-penalty-mod.md)、[内置补丁与重建](../../assets/perfect-deflect-no-rapid-click-penalty/README.md)。

## 实施入口

从明确的目标 `ProfileId` 开始。用户要求编辑“游戏当前方案”时，先读取 `GameSchemeSettings.ProfileId`；读取失败就报告无法确定，不以 `SaveFile.ActiveProfile` 或第一条记录冒充真实选择。

先按目标区分绑定与战斗行为：增加防御按键仍编辑 `IA_Guard` 的有效集合；点按防御与持续架势共用这个可重绑名称，不能靠分配不同保存槽拆开。连点导致完美防御窗口衰减时，按[防御输入与战斗行为](references/deflect-behavior.md)核对共用计数与输入间隔；真正分离点按和架势需要检查输入释放事件及两层能力状态，不能承诺通过存档改键完成。

支持构建的默认数据入口是 [InputDefaults](../../src/KeyboardDefaults.cs)，资源提取入口是 [extract-input-defaults.py](../../build/extract-input-defaults.py)。60 条资源注册输入按原生身份合并为 59 条最终默认，动作级集合由最终默认和固定映射生成。先按所属安装的原生注册规则确定槽归属，再合并对应方案的共享覆盖及手柄家族覆盖，不能按当前按键类别归属槽，也不能将旧存档的默认字段推广为新版注册结果。CL-302256 的影之武装槽 2 为键鼠槽 0 的镜像；CL-301524 的槽 2 属于手柄。适配新构建时同时检查资源自身设置、InputAction 继承设置及实际虚表派发的注册路径，不能由存档缺少键鼠 `DefaultKey` 推断默认值不可得。完整性应以 AssetRegistry 的类枚举核对，路径或文件名前缀筛选的结果不能单独证明无遗漏。

Xbox 的标准保存 Family 为 `Xbox`，PlayStation 为 `PS`；显示名称不直接充当
保存身份。设备编辑选项不以已连接硬件或已有家族记录为条件。没有游戏或存档
时可从内置原厂基线编辑预设并导出；创建家族记录的字段与静态证据见
[默认映射与有效绑定](references/default-bindings.md) 的手柄家族部分。

招式发现从角色获得能力的全部来源进入：角色初始配置、主武器、临时武器、影之武装、饰品、能力修行、升级效果、道具和实际引用闭包。枚举实际类与数据表行结构，跟随 hard 引用、序列化 soft path、继承、效果授予和原生父类，不以文件夹或 LegoAbility 类型限制全集。输入映射的 tag 集合只确定输入入口；已收录 tag、没有持续输入等待或只有一条 `Main` 时间线，都不能结束内部攻击阶段的核对。逐条跟随时间线中的伤害、弹体、蒙太奇和原生回调，区分同一招的多次判定、状态修饰、条件追击与可独立进入的攻击阶段；结束段也须查实际伤害。为每个候选保留初始化、费用、解锁、命中/目标/防反资格和结束清理的证据。处决类必须读取装备实际 `ExecutionAnims` 的通道、类型及执行者/目标条件，再请求对应原生通道；同一配置中出现某个类型或通道名称不足以证明它们对应。输入与准备动作须核对实际 InputAction 资源及其映射，不从映射名称推造资源路径。`CombatArt` 不得因归入其他能力而被排除，无输入 tag 的活跃饰品效果也不能遗漏。资产清点、已发现阶段和可执行配方分别记录覆盖状态，只有阶段均已核对才宣告该来源招式完整。

完整招式绑定在默认或“按下时”触发下保留持续按键状态：按住时持续请求，游戏原生资格决定何时接下一招；松开停止下一次请求，正在执行的一招沿原生命周期结束。同规则的普通动作和能力在“按下时”仍只请求一次；短按、松开及序列触发也保留各自的一次性语义。不能在保存时丢弃默认触发与显式触发的区别；验证须经过配置编译、真实调度器及能力执行器，并覆盖键鼠、手柄、混合输出与分享往返。费用和倍率属于本次实际激活的绑定，不能合并到同键其他绑定或共享能力模板中；同次动作的原生后续和弹体保留该次参数，其他绑定与原操作使用各自参数。

尽量复用仓库的 `SaveFile`、`GameSchemeSettings` 和 `EditorSession`。`SaveFile.Save()` 只是序列化写文件，不提供备份、并发检查或游戏运行保护；安装目录的安全写入参考 `EditorSession.Apply()`。移植到其他语言时保留同样的格式约束和验证条件。

先在副本进行零修改往返，必须逐字节一致，再编辑目标动作。写回前确认游戏已退出，检测文件是否被外部更新，备份到游戏目录外，写同目录临时文件并回读验证后原子替换。游戏键位页面点应用或恢复默认会重新保存设置，可能覆盖 mod；不能据此声称文件修改永久无效。

## 验收与交付

- 完美防御无连点惩罚仅覆盖 `/PBZCombatAsset/Config/GlobalConfig_Combat_{Easy,Normal,Hard,Nightmare,Handicapped}` 五个配置包，各将 `Deflect Quickclick Punish Cooldown` 的 double 从 `0.4` 改为 `0`。每包其余字节须与源资源相同；Combined getter、Passive、Base、Player 与其他函数保持原样。通过原逻辑取消连点惩罚，保留动态基础窗口、普通防御窗口及原生资格判定。
- 重建入口为 `build/make-perfect-deflect-no-rapid-click-penalty-mod.py`，复用 `build/extract-input-defaults.py` 的资源读取器；运行 `tests/PerfectDeflectNoRapidClickPenaltyModBuildTests.py` 验证五包范围、数值与容器关系。需要独立提取容器时使用 `DeflectSchemaAudit --container`，它只提取原始 chunk，不验证游戏实际加载。不得把实验分支的新增函数、曲线或概率测试重新作为主线验收条件。
- `PerfectDeflectNoRapidClickPenaltyMod` 管理独立目录，默认关闭。主页面勾选仅暂存，点击“应用更改”安装，取消勾选后应用移除；启用/关闭前要求退出游戏。按目标资源身份识别，不设精确版本号门槛；资源存在不等于未来数据兼容。只管理本工具独立目录中的文件，未知或被修改的文件报冲突并保留。原始容器与存档不替换。
- 单方案修改后，另外两套键鼠及手柄记录、其他动作、未知字段和尾部数据保持不变；全局恢复默认是明确的例外操作。
- “新增左键防御”必须保留右键，手柄对应保留 LB 并新增 X。槽满时拒绝，不能静默替换不同的已有键；“移动防御到左键”则是另一种操作。手柄普通编辑只使用已注册槽；没有投影的额外家族条目不会在方案激活时应用。
- 默认按键、实际功能数、原厂差异、恢复操作共用同一映射来源。逐槽恢复按具体默认值执行；缺失覆盖从原厂槽继承，显式 `None` 保持解绑。已确认的新增关系独立显示为自定义，不能被其他槽的未知状态吞掉。
- 方案启用要重新认证解密目标文件，检查 `KeyBindingScheme`，并比较其他 JSON 内容不变；不能只检查 GVAS 或写盘成功。
- 同时改键位和方案不是跨文件事务。准确报告部分成功，重试只处理未完成部分。
- 分开报告格式往返、静态程序证据与实际进游戏验证。测试通过不等于已验证游戏运行效果。

从仓库根目录构建后可运行 `dist/PBZKeyForge-cli.exe --selftest samples/vanilla-base.sav`；其余验证入口见 [tests/README.md](../../tests/README.md)。测试只用副本，不把游戏密钥、解密后的完整玩家设置或本地会话原文收入技能。
