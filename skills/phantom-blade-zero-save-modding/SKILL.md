---
name: phantom-blade-zero-save-modding
description: 为《影之刃零》开发存档型键位 mod 或编辑工具，提取动作与逐槽原厂映射，解析无损写回 EnhancedInputUserSettings.sav，并认证读写 PBZSharedSetting.sav 中真正生效的方案选择。适用于默认键信息、多动作同键、新增绑定及方案切换排查。
---

# 影之刃零：键位与方案存档开发

本技能记录 PBZKeyForge 的输入映射、存档格式和实现方法。默认值研究包含读取游戏资源及静态程序分析；修改落点仍是存档，不隐含启动、注入或修改游戏程序的授权。

## 先区分修改对象

| 目标 | 实际落点 | 关键区别 |
| --- | --- | --- |
| 键鼠绑定 | `EnhancedInputUserSettings.sav` 属性段之后的共享 profile records 中，原厂归属键鼠的槽 | Blob 不限于键鼠；按 `ProfileId`、mapping、真实原始槽和硬件身份定位。 |
| 手柄绑定 | 同文件 `GamepadFamilyProfiles` → `(ProfileId, Family)` → `Keys` | 同为 Xbox 的记录也可能属于不同方案；不能全部一起改。 |
| 原厂绑定与继承后的有效按键 | 游戏 IMC 按方案选择的映射 → 注册槽位；手柄另有保存的 `DefaultKey` | 原厂集合、逐槽默认与当前覆盖是三种数据，不能相互冒充。 |
| 游戏采用哪个方案 | `PBZSharedSetting.sav` 认证解密后的 JSON `KeyBindingScheme` | 单改 GVAS 的 `CurrentProfileIdentifierString` 不能代替它。 |

已研究的 Steam 安装中，两份存档在游戏根目录的 `ProjectPBZ/Saved/`，实际原生程序在 `platform/steam/PBZ.exe`。不要把较小启动器当作该程序，也不要把此路径推广到未经检查的其他发行版。

## 按任务读取资料

- 读写动作、槽位、手柄记录、制作一键攻防：[键位格式与编辑](references/input-bindings.md)。
- 提取或展示原厂键、解释默认继承、计算差异与功能数：[默认映射与有效绑定](references/default-bindings.md)。
- 读取当前生效方案、实现启用功能、处理 PBZS 加密：[生效方案与认证容器](references/scheme-activation.md)。
- 判断旧指引是否可信、适配游戏更新、说明验证程度：[研究依据与版本边界](references/research-evidence.md)。

## 实施入口

从明确的目标 `ProfileId` 开始。用户要求编辑“游戏当前方案”时，先读取 `GameSchemeSettings.ProfileId`；读取失败就报告无法确定，不以 `SaveFile.ActiveProfile` 或第一条记录冒充真实选择。

支持构建的默认数据入口是 [InputDefaults](../../src/KeyboardDefaults.cs)，资源提取入口是 [extract-input-defaults.py](../../build/extract-input-defaults.py)。60 条资源注册输入按原生身份合并为 59 条最终默认，动作级集合由最终默认和固定映射生成。先按所属安装的原生注册规则确定槽归属，再合并对应方案的共享覆盖及手柄家族覆盖，不能按当前按键类别归属槽，也不能将旧存档的默认字段推广为新版注册结果。CL-302256 的影之武装槽 2 为键鼠槽 0 的镜像；CL-301524 的槽 2 属于手柄。适配新构建时同时检查资源自身设置、InputAction 继承设置及实际虚表派发的注册路径，不能由存档缺少键鼠 `DefaultKey` 推断默认值不可得。完整性应以 AssetRegistry 的类枚举核对，路径或文件名前缀筛选的结果不能单独证明无遗漏。

Xbox 的标准保存 Family 为 `Xbox`，PlayStation 为 `PS`；显示名称不直接充当
保存身份。设备编辑选项不以已连接硬件或已有家族记录为条件。没有游戏或存档
时可从内置原厂基线编辑预设并导出；创建家族记录的字段与静态证据见
[默认映射与有效绑定](references/default-bindings.md) 的手柄家族部分。

尽量复用仓库的 `SaveFile`、`GameSchemeSettings` 和 `EditorSession`。`SaveFile.Save()` 只是序列化写文件，不提供备份、并发检查或游戏运行保护；安装目录的安全写入参考 `EditorSession.Apply()`。移植到其他语言时保留同样的格式约束和验证条件。

先在副本进行零修改往返，必须逐字节一致，再编辑目标动作。写回前确认游戏已退出，检测文件是否被外部更新，备份到游戏目录外，写同目录临时文件并回读验证后原子替换。游戏键位页面点应用或恢复默认会重新保存设置，可能覆盖 mod；不能据此声称文件修改永久无效。

## 验收与交付

- 单方案修改后，另外两套键鼠及手柄记录、其他动作、未知字段和尾部数据保持不变；全局恢复默认是明确的例外操作。
- “新增左键防御”必须保留右键，手柄对应保留 LB 并新增 X。槽满时拒绝，不能静默替换不同的已有键；“移动防御到左键”则是另一种操作。手柄普通编辑只使用已注册槽；没有投影的额外家族条目不会在方案激活时应用。
- 默认按键、实际功能数、原厂差异、恢复操作共用同一映射来源。逐槽恢复按具体默认值执行；缺失覆盖从原厂槽继承，显式 `None` 保持解绑。已确认的新增关系独立显示为自定义，不能被其他槽的未知状态吞掉。
- 方案启用要重新认证解密目标文件，检查 `KeyBindingScheme`，并比较其他 JSON 内容不变；不能只检查 GVAS 或写盘成功。
- 同时改键位和方案不是跨文件事务。准确报告部分成功，重试只处理未完成部分。
- 分开报告格式往返、静态程序证据与实际进游戏验证。测试通过不等于已验证游戏运行效果。

从仓库根目录构建后可运行 `dist/PBZKeyForge-cli.exe --selftest samples/vanilla-base.sav`；其余验证入口见 [tests/README.md](../../tests/README.md)。测试只用副本，不把游戏密钥、解密后的完整玩家设置或本地会话原文收入技能。
