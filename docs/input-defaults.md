# 默认键与无绑定

“无绑定”在目标槽写入 `Key = None`，保留显式覆盖。“默认”和“清除覆盖”删除键鼠覆盖，或恢复手柄记录的 `DefaultKey`。解绑一个槽不代表解绑整个动作；其他槽仍可提供输入。

默认值来自游戏输入映射资源及原生注册规则，区分动作的有效原厂键集合与可重绑的逐槽默认值。“澄心洞照”对应 `IA_Sense_Default`；地图默认键包含 M 和 Tab。两版影之武装槽 2 的归属不同，不能仅凭资源条目或旧存档中的 `DefaultKey` 判断新版投影。两版程序、方案认证路径和交互改键的验证边界见[游戏版本兼容](game-version-compatibility.md)。

## 从安装资源提取

`build/extract-input-defaults.py` 直接读取原生游戏 EXE 和 IOStore 资源，不启动游戏，不读取或修改真实存档。依赖 Python、`pycryptodome` 和调用者提供的 Oodle 8 DLL：

```powershell
python build\extract-input-defaults.py --game-exe <platform\steam\PBZ.exe> --paks <ProjectPBZ\Content\Paks> --oodle <oo2core_8_win64.dll> --output dist\input-defaults.json
```

旧版维护数据为 [`assets/input-defaults.json`](../assets/input-defaults.json)，新版为 [`assets/input-defaults-302256.json`](../assets/input-defaults-302256.json)。生成新版精简数据时使用 `--build CL-302256 --compact --output assets\input-defaults-302256.json`。精简模式与完整研究输出使用同一次资源解析结果，保留构建、三个方案、`bindings` 中的 60 条资源注册输入、`effectiveBindings` 中的 59 条最终默认及其来源，以及按 InputAction、设备和按键合并的固定映射；不包含内部属性头、导出序号或完整目录数据。

提取器默认使用 CL-301524 的位置；新版添加 `--build CL-302256`，旧版可显式添加 `--build CL-301524`。两版资源解密位置分别登记，不能混用。

脚本只输出输入映射字段及资源来源，不输出资源密钥、解密后的资源包、完整资源目录或 `.uasset`。密钥在内存中从支持 EXE 的固定指令立即数位置读取，用解密后的目录挂载点验证。映射资产采用 Zen 包格式和无版本属性；脚本完整解析根对象的属性头与数据，遇到未支持的字段即停止，避免把未解析的方案覆盖误当作不存在。

新版 AssetRegistry 按类枚举得到 20 个 IMC、98 个 InputAction。20 个 IMC 共 173 条映射；正确解析自身或继承的可重绑设置后，71 条来源映射可重绑、102 条固定。注册来源去重为 60 条，按原生身份合并后为 59 条最终默认；固定来源去重为 86 组。各 IMC 只序列化 `DefaultKeyMappings`，没有 `MappingProfileOverrides`；各可重绑设置没有方案限制。因此三个方案使用相同原厂默认，方案中的自定义覆盖分别保存。

`SettingsBehavior = 0` 使用 InputAction 的 `PlayerMappableKeySettings`，1 使用映射自身的设置，2 不提供可重绑设置。`IMC_OnlyMap`、`IMC_OnlySense`、`IMC_OnlyChangeWeapon` 有 7 条继承注册来源；不能仅凭没有映射自身的 Override 将其归为固定。提取器枚举全目录的 `IMC_` 资源，新版结果与 AssetRegistry 的 InputMappingContext 类集合一致；`IMC_QTE` 的 25 条固定来源也包含在内。

## CL-301524 的逐槽默认值

槽号从 0 开始。`First` 至 `Seventh` 对应 0 至 6；7 是 `Unspecified`，8 是 `Max`，不能作为额外显式槽分配。表中列出最终 profile 默认；多槽具有同一按键时仍保留全部槽号。

| Mapping name | 键鼠槽与默认键 | 手柄槽与默认键 |
| --- | --- | --- |
| `IA_LightAttack` | 0 / 2 / 4：鼠标左键 | 1 / 3 / 5：FaceButton Left |
| `IA_HeavyAttack` | 0 / 2 / 4：F | 1 / 3 / 5：FaceButton Top |
| `IA_Guard` | 0 / 2：鼠标右键 | 1 / 3：Left Shoulder |
| `IA_Dodge` | 0 / 2：Left Shift | 1 / 3：Right Shoulder |
| `IA_Jump` | 0：Space | 1：FaceButton Bottom |
| `IA_Crouch` | 1：C | 0：Left Thumbstick |
| `IA_MoveFast` | 0：Left Shift | 1：Right Shoulder |
| `IA_MoveSlow` | 0：Left Alt | 无 |
| `IA_LockTarget` | 0：鼠标中键 | 1：Right Thumbstick |
| `IA_Interact` | 0：E | 1：FaceButton Right |
| `IA_Execute` | 0：E | 1：FaceButton Right |
| `IA_ShadowArm` | 0：Left Ctrl | 1 / 2 / 3：Right Trigger Axis |
| `IA_ShadowArm_LongPress` | 无独立可重绑槽 | 0：Right Trigger |
| `IA_SwitchWeapon` | 0 / 2：1 | 1 / 3：D-pad Left |
| `IA_SwitchShadowArm` | 0：2 | 1：D-pad Right |
| `IA_UltimateSkill` | 0 / 2：Q | 1 / 3：Left Trigger Axis |
| `IA_Sense_Default` | 0：V | 1：Special Left |
| `IA_OpenCharacterMenu` | 1：I | 0：Special Right |
| `IA_OpenMapMenu` | 0：M；2：Tab | 1：Special Left |
| `IA_UseTopItem` | 1：T | 0：D-pad Up |
| `IA_UseBottomItem` | 1：R | 0：D-pad Down |

`IMC_Default` 的第 31 条映射将 Left Ctrl 以 `InputTriggerHold` 连接到 `IA_ShachiAbility_LongPress`，没有可重绑设置；第 32 条将 Right Trigger 连接到同一 InputAction，并提供 `IA_ShadowArm_LongPress` 的手柄设置。因此长按影之武装的动作级键鼠默认包含 Left Ctrl，但该固定映射不属于独立的键鼠可重绑槽。`IMC_Locomotion` 的 WASD、方向键、Mouse2D 和手柄摇杆等固定映射同样保留在提取 JSON 的 `contexts[].mappings` 中。

`IA_ShadowArm` 的手柄槽 2 注册输入来自 `IMC_Interaction_ShaChiBroken`，槽 1 / 3 来自 `IMC_Default`；`IMC_Default` 同时提供键鼠槽 2 注册输入。两者共享原生槽身份，不能并存为两个 profile 映射。基线三个方案的槽 2 均保存不可变的 `DefaultKey = Gamepad_RightTriggerAxis`，据此最终投影保留手柄槽 2；键鼠 Ctrl 仍由槽 0 提供。该结果依据真实保存的注册结果，不假设 AssetRegistry 的枚举顺序。

### CL-302256 的影之武装槽

新版注册器在基类注册后，将 `IA_ShadowArm` 槽 2 的原厂键设为 Left Ctrl，当前键复制槽 0。全部上下文注册完成后，键鼠注册槽为 0、2，手柄为 1、3；槽 2 是槽 0 的镜像，不是独立可编辑来源。编辑器保留键鼠独立列 0、4、5，手柄列为 1、3。槽 0 显式解绑后，槽 2 不会重新提供 Ctrl；额外键鼠槽 4、5 仍可独立使用。

存档中的旧手柄槽 2 保留原记录，但不计入新版有效手柄关系。新版加载存档后才执行注册规范，单独保存不同于槽 0 的键鼠槽 2 也会被游戏覆盖。程序按每份存档所属安装的原生注册器识别规则，预览和导出保留该规则；不会用全局版本开关影响另一份存档。对应的 vtable、注册和加载地址见[游戏版本兼容](game-version-compatibility.md)。

## 键鼠槽与游戏界面

GVAS 的 Blob 记录保存共享 profile 的变化覆盖，不是固有的键鼠表，也没有逐槽默认键。逐槽默认值由资源补齐，槽的原始设备归属由注册默认决定，不能根据当前 Key 属于键盘还是手柄判断。游戏界面按原厂键合并内部映射，因此主／副列不能直接解释为原始槽 `0 / 2 / 4`。键鼠槽号也不具有统一的奇偶规律：蹲伏、菜单和药品的键鼠槽均为 1。

支持构建 CL-301524 的静态代码证据（地址为原生主体的首选 VA）：

- `0x146688210` 初始化键鼠绑定行，调用 `0x146691cc0` 获取显示按键。
- `0x14667c010` 按 DefaultKey 合并映射；`0x14667c2dd–0x14667c2e9` 比较现有组和当前映射的 DefaultKey。
- `0x140d76ce0` 返回映射 `+0x30` 的 DefaultKey，`0x14149f950` 读取 `+0x28` 的槽号。
- `0x14667c338–0x14667c367` 将槽号追加到对应按键组。

初始化用户设置的 `0x1468867a0` 在 `0x14688682f`、`0x14688687c` 为自定义 1／2 调用同一个 `0x146881ff0`；该构造路径使用相同的默认参数与 profile 类，分别设置方案标识和显示名。`0x144860040` 遍历方案并为每个方案注册同一个输入映射上下文。注册函数 `0x1448606a0` 在 `0x14486077a` 按方案标识查询 IMC 的 `MappingProfileOverrides`，不存在时在 `0x144860784` 使用默认数组；资源解析已确认本构建的输入 IMC 没有这些覆盖。

初始化的上下文库来自 AssetRegistry：`0x146966e88` 在 `0x146966fad–0x146966fdc` 取得全部 IMC 资产，`0x146967189` 发起加载；回调 `0x1469632e0` 安装上下文库后在 `0x146963423` 初始化用户设置。`0x1468868be` 经 `0x146957ad0` 枚举该库，在 `0x1468868e9` 注册所有返回的 IMC，不仅限于当前输入模式。

`DA_DefaultInputMode` 引用 `IMC_Default`、`IMC_Deflect`、`IMC_Locomotion`、`IMC_ChangeWeapon`、`IMC_Execution`、`IMC_Debug`、`IMC_HUDInput` 和 `IMC_HUDInput_Meidicne`。交互输入模式另引用相应的 `IMC_Interaction*`；提取结果保留上下文来源和资源中的映射顺序。

## 默认值与覆盖的合并

`0x1448606a0` 按映射名称、注册槽号和硬件描述符的 Primary 字段查找已有保存映射，逐槽合并资源默认值，保存一个槽不会替换整个动作的其他槽。硬件 Primary 不是根据当前按键推导的键鼠／手柄类别。

- `0x144860ad1–0x144860ae7` 比较槽号和硬件 Primary，查找对应条目。
- `0x144860b45–0x144860b74` 将资源键更新到已有条目的 `DefaultKey`（`+0x30`），保留 `CurrentKey`（`+0x48`）。`0x144860b79–0x144860b83` 比较两者，相同时清除自定义标志。
- 未找到条目时，`0x144860c0f` 调用 `0x14484a400` 创建默认映射。构造函数在 `0x14484a4d9` 写入槽号，并在 `0x14484a4dd–0x14484a512` 将资源键同时写入 `DefaultKey` 和 `CurrentKey`。
- `0x144860d55–0x144860d77` 按注册顺序递增槽号，资源中的注册顺序决定逐槽默认值。

每次注册一个 IMC 时，`0x14486075e` 调用 `0x1448622b0` 清空临时计数表；计数表按硬件标识和 mapping name 分桶。此构建的硬件查询 `0x144850970` 返回统一的 None 标识，因此同一 mapping name 的键鼠和手柄映射按资源顺序共用计数，不按设备分别从 0 开始。`IMC_Deflect` 的 Guard 顺序为右键、LB、右键、LB，对应原始槽 0、1、2、3；另一个 IMC 会重新从 0 计数。

`FPlayerKeyMapping` 的槽在 `+0x28`，DefaultKey 在 `+0x30`，CurrentKey 在 `+0x48`，24 字节硬件描述符在 `+0x60`。序列化函数 `0x144863090` 保存变化的 CurrentKey、硬件描述符和槽号；工具中称为 X/Y/Pad5 的字段属于硬件描述符。Blob 中 Crouch 槽 0 的当前键即使是 F8，仍覆盖原来属于手柄的槽，不会另建一个键鼠槽 0。

手柄家族投影 `0x14688fd20` 在 `0x1468903f9–0x146890408` 按 **DefaultKey 是否手柄键**筛选共享映射，在 `0x146890453–0x14689049e` 复制原始槽、DefaultKey 和 CurrentKey。Family 不改变原厂键；各家族的自定义覆盖仍由 `(ProfileId, Family)` 分别保存。

注册后，`0x146886ab4` 经 `0x14688a7d0` 到 `0x146884350` 激活家族。`0x14688472f–0x146884748` 按 mapping name、槽号及 DefaultKey 匹配家族条目；匹配时在 `0x146884755` 选家族 CurrentKey，缺失时在 `0x14688475e` 选投影 DefaultKey。`0x146884858` 经 vtable `+0x2f8` 调用 `0x144858770`，在 `0x144858838–0x14485884a` 写共享 CurrentKey。因此已注册手柄默认槽中，家族值优先于 Blob 覆盖；缺少家族条目时回默认，不保留 Blob 的当前键。例如 Crouch 槽 0 的 Blob 为 F8、家族为左摇杆按下，激活结果仍为左摇杆按下；删除家族条目也回到该原厂键。

激活只遍历具有手柄 DefaultKey 的已注册投影。额外 family-only 条目没有对应投影，不会被应用；普通编辑因此只补齐或修改已注册的手柄槽。已有额外条目保留在原始记录入口，不计入有效功能。`ForRawSlot` 返回 `None` 表示该设备在该槽没有原厂注册。

由此可知，保存中未出现的槽仍会由资源补齐；仅根据一个已保存的键不能确认同一动作的全部有效键。

## 验证范围

旧版已提取数据覆盖 19 个 IMC，30 条手柄默认槽与 `samples/vanilla-base.sav` 三个方案中的全部 90 条 `(mapping name, slot, DefaultKey)` 一致。新版重新读取完整 20 个 IMC，逐个检查映射根对象完整消费，并以原生注册规范确定 29 条最终手柄槽；不能把旧版 90 条记录直接当成新版默认表。

独立检查将运行时代码的 60 条资源注册输入和 59 条最终默认与对应构建的数据比较，核对动作集合去重、地图双键、蹲伏原始槽、影之武装槽 2 合并和固定 Ctrl 长按。旧版另只读对照基线三方案的 90 条手柄记录：

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /out:dist\InputDefaultsTests.exe /r:System.Core.dll /r:System.Web.Extensions.dll /r:dist\PBZKeyForge.exe tests\InputDefaultsTests.cs
.\dist\InputDefaultsTests.exe assets\input-defaults.json samples\vanilla-base.sav
.\dist\InputDefaultsTests.exe assets\input-defaults-302256.json samples\vanilla-base.sav
```

集成检查覆盖 `None` 的保存、重开、撤销、恢复默认及方案隔离。界面检查覆盖默认与无绑定的选择、键鼠捕获和截图布局，默认只测简体中文；15 种语言遍历仅在针对性测试时通过 `--all-languages` 启用。

## 已知限制

两版原生规则和新版完整资源枚举均为静态证据，不能代替游戏内解绑实测。旧版资源容器当前不在本机，旧资源比较仅覆盖已提取数据；旧版影之武装槽 2 的最终归属由三个基线方案共同记录的 DefaultKey 确认。新版槽 2 的规范由实际虚表派发和存档加载后的调用顺序确认，不依赖资源枚举顺序。CommonUI 菜单、相机、QTE 和调试等固定输入不提供 GVAS 的可重绑槽，不能通过添加同名保存记录伪造支持。
