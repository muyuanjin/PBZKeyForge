# 默认映射与有效绑定

## 数据与提取入口

支持构建为 CL-301524 和 1.0302256 / CL-302256；两版影之武装槽 2 的设备归属不同，见[游戏版本兼容](../../../docs/game-version-compatibility.md)。逐槽默认及原生注册证据见[默认键表](../../../docs/input-defaults.md)，数据分别保存在 [input-defaults.json](../../../assets/input-defaults.json) 和 [input-defaults-302256.json](../../../assets/input-defaults-302256.json)。`bindings` 保存 60 条资源注册输入，`effectiveBindings` 保存按原生身份合并的 59 条最终默认。默认查询必须使用当前存档的规则实例，提供动作集合、逐槽默认、已注册槽、可独立编辑槽及逻辑列到原始槽的转换；`FixedForAction` 保留不经过可重绑注册的动作输入。预览、克隆、预设和导出必须继承同一实例；不可全局切换静态表来处理不同安装。

[提取器](../../../build/extract-input-defaults.py) 枚举安装包全目录中的 IMC，输出默认条目、固定映射、资源路径、InputAction 和触发器。新版的 20 个 IMC 已与 AssetRegistry 的 InputMappingContext 类集合独立核对一致。它不启动游戏、不读取玩家存档。依赖 Python、`pycryptodome` 和调用者提供的 Oodle 8 DLL，从仓库根目录运行：

```powershell
python build\extract-input-defaults.py --game-exe <platform\steam\PBZ.exe> --paks <ProjectPBZ\Content\Paks> --oodle <oo2core_8_win64.dll> --output dist\input-defaults.json
```

提取新版时添加 `--build CL-302256`，默认仍为 `CL-301524`。新版输出含 60 条资源注册输入、59 条最终默认，以及 `contexts[].mappings` 中的 102 条固定来源映射。每个注册输入保留 IMC 和 mappingIndex 来源。完整研究输出保留原始映射细节。资源密钥只在进程内存中使用，输出不含密钥、完整目录或解密资源包。

## 方案、设备与槽号

各 IMC 的完整根属性只有 `DefaultKeyMappings`，没有 `MappingProfileOverrides`；可重绑设置没有 `SupportedKeyProfiles` 限制。因此本构建的三个方案共用原厂表，当前覆盖仍分别归属各方案。适配新构建时重新检查这些资源字段，不能仅由 profile 类或构造函数相同推出默认相同。

显式原始槽为 `EPlayerMappableKeySlot::First` 至 `Seventh`（0 至 6），7 是 `Unspecified`，8 是 `Max`。每个 IMC 注册时重新计数，同一 mapping name 与硬件标识的键鼠和手柄条目按资源顺序共用计数；不能用奇偶判断设备。当前硬件标识为 None。手柄家族投影复制原始槽和默认键，Family 不改变原厂键，但各家族的保存覆盖仍须分别定位。

共享 profile 的注册身份由 mapping name、raw slot 和硬件 Primary 决定。原生 `0x144860ad1–0x144860ae7` 比较槽与硬件 Primary，`0x144850970` 返回全零硬件描述符；键鼠和手柄资源键本身不构成独立身份。影之武装的两个资源槽 2 因此合并。旧版基线三个方案共同保存的 `DefaultKey = Gamepad_RightTriggerAxis` 确定旧版最终归属；新版虚表派发到额外注册规范，将槽 2 改为 LeftControl、当前键复制槽 0。原生初始化会枚举全部 IMC 库，而非只注册当前输入模式；完整地址与路径见默认键表。

资源 Mod 改变可重绑名称时，以实际修改后的输入资源重新提取动作身份，并按注册顺序重建默认槽和手柄家族记录。例如将 Stance 的名称从 `IA_Guard` 改为 `IA_Guard_0` 后，原厂 Guard 槽表不能直接用于新名称，也不能只修改界面文字；原 Guard 覆盖的保留和新身份的初始化分别处理。FName 的 number 会影响解析后的名称，核对时包含该部分。此规则用于输入身份变更；“完美防御无连点惩罚”只修改五个难度战斗配置中的冷却数值，不改变绑定身份。相关原型与消费路径见[防御输入与战斗行为](deflect-behavior.md)。

| 情形 | 真实映射与处理 |
| --- | --- |
| 格挡 | 键鼠原厂槽 0、2 均为右键；手柄槽 1、3 均为 LB。键鼠槽 4 没有原厂绑定。 |
| 蹲伏 | 键鼠槽 1 为 C，手柄槽 0 为左摇杆按下；菜单和药品也使用键鼠槽 1、手柄槽 0。 |
| 地图 | 键鼠槽 0 为 M、槽 2 为 Tab，动作集合保留两个不同键。 |
| 影之武装 | 旧版键鼠槽 0 为 Left Ctrl、手柄槽 1、2、3 为 Right Trigger Axis；新版键鼠槽 2 镜像槽 0，手柄仅槽 1、3。两版独立键鼠编辑列均保留 0、4、5。 |
| 影之武装长按 | 键鼠 Left Ctrl 是带 Hold 触发器的固定映射，没有独立可重绑槽；手柄 Right Trigger 在槽 0。 |

`save.Defaults.Slots(action, gamepad, existingSlots, otherDeviceSlots)` 将已注册槽和已有保存槽映射到工具的三个逻辑编辑列，并选择可用的额外槽。调用 `EditorSession.GetKeyboardRawSlot` 或 `GetGamepadRawSlot` 获取当前列的真实槽号；不要将列号当 raw slot，也不要套用固定的 `0/2/4` 或 `1/3/5`。静态 `InputDefaults` 仅代表旧版及离线基线。

## 手柄家族与离线基线

支持构建的标准存储家族是 `Xbox`、`PS`、`NS`、`Other`。PlayStation
界面名称和 `SteamInput_PS4` / `SteamInput_PS5` 等设备名称经原生
`0x14688ebc0` 规范化为 `PS` 后进入投影；不要把 `PlayStation`、`PS4`、
`PS5` 或 `DualSense` 直接当作标准 Family 写入。具体分支、常量地址及进入
保存投影的调用链见[手柄方案证据](../../../docs/gamepad-profile-evidence.md)。

设备选择不应依赖已连接硬件或存档是否已有家族记录。工具可以从内置原厂表
提供键鼠、Xbox、PlayStation 的编辑基线；缺少游戏、存档或手柄只影响应用到
游戏，不影响预设编辑与导出。缺少的家族记录可从随程序发布的基线克隆 GVAS
结构，设置目标 `ProfileId` 与标准 Family，以原厂表的已注册手柄 raw slot
建立 `MappingName`、`Slot`、`DefaultKey`、`Key`，初始 `Key` 等于该槽
`DefaultKey`。不能把另一家族的当前自定义键当作新家族的原厂键。
实际输出手柄改动时将 `GamepadFamilyStorageVersion` 写为 1，包括新增家族和
已有空家族的编辑；已有记录的未知字段保持原样。原生构造先将版本初始化为
0、家族数组初始化为空。版本缺失或为 0 时，首次投影会先以共享当前键重写
目标家族，再将版本置 1，因此不能只补家族数组而漏掉版本。仅浏览、无编辑
导出或只编辑键鼠时保留原版本。构造和替换分支的地址见上述证据文档。

## 合并默认与覆盖

先按 `(ProfileId, mapping, rawSlot, hardware)` 定位共享 profile 覆盖，再按最终原厂注册确定键鼠／手柄槽归属。Blob 的 X/Y/Pad5 保存硬件描述符，不能用当前 `Key` 的类别代替归属。对原厂键鼠槽，存在共享覆盖则使用覆盖 `Key`，否则使用该槽默认；保留固定映射。显式 `None` 表示解绑，不能当成缺失覆盖。额外保存条目仍保留，已知动作未注册的槽默认是 `None`，未知动作的默认值为未知。

原厂手柄槽按 `(ProfileId, Family, mapping, rawSlot, DefaultKey)` 匹配家族覆盖。原生 `0x1468903f9–0x146890408` 按 DefaultKey 筛选手柄投影，`0x14688472f–0x14688475e` 选择家族 CurrentKey，缺少家族条目则回该槽 DefaultKey；`0x144858838–0x14485884a` 将选中键写回共享 CurrentKey。家族值优先于 Blob 覆盖，删掉家族条目不代表保留 Blob 值。例如 Crouch 槽 0 的 Blob 为 F8，家族为左摇杆按下，激活后仍为左摇杆按下；改 C 应操作原厂键鼠槽 1。

从合并结果去重得到动作当前按键集合，再反向得到每键的不同动作集合。多个槽提供同动作同键只计一次。移除关系要处理全部提供该关系的覆盖及默认来源；删除一个覆盖会恢复该槽默认，不等于解绑。恢复整个动作只清除可重绑覆盖，不能取消固定映射。

只把 Guard 键鼠槽 0 设为左键时，槽 2 仍继承右键，有效集合为左键＋右键；左键格挡是自定义关系。将两个原厂右键槽都设为 `None` 后才不再由它们提供右键。Crouch 的首列对应原始槽 1，改槽 0 不能代替改 C 的实际注册槽。长按影之武装的 Ctrl 始终来自固定映射，不能承诺通过写一个独立长按键鼠槽将其移走或解绑。

补齐已注册的手柄保存条目时，可从同方案、家族、动作的模板保留结构，但 `DefaultKey` 必须按目标 raw slot 从原厂表设置，不能复制其他槽的默认。已有手柄记录的默认字段及未知元数据保持原样。已知动作的 `ForRawSlot` 返回 `None` 表示该设备无原厂注册；新增这类 family-only 条目没有对应的激活投影，不会被应用，普通编辑不得创建。已有额外条目保留在原始记录入口，不计入有效功能。

## 验证默认数据

旧版最终 30 条手柄默认槽与基线三个方案中的全部 90 条 `(mapping name, slot, DefaultKey)` 一致；新版最终有 29 条手柄槽，旧基线仅提供保留未知字段的结构模板，不能直接拿其槽 2 当新版投影。动作级原厂集合必须包含固定映射，并与最终逐槽表去重后的结果一致；游戏页面的主／副列是按原厂键分组的显示结果，不能反推 raw slot。

默认数据变更后检查槽继承、显式解绑、同键重复槽、地图多键、蹲伏原始槽 1、影之武装跨 IMC 同槽合并和固定 Ctrl 长按。保存后继续新增或移除关系，应同时更新功能数、三色徽标、修改前／原厂差异与恢复行为。零修改序列化检查保护存档格式，不能单独证明默认表正确。

## 已知限制

旧版资源容器当前不可用，旧资源比较以已提取数据为界；旧版影之武装槽 2 的最终默认以三个方案保存的同一 DefaultKey 为依据。新版槽 2 以实际虚表派发、加载顺序和注册代码为依据，不受 IMC 枚举顺序影响。PBZS 认证成功不证明输入注册规则相同；输入规则识别只验证上述两版，未知程序回退旧规则。资源与原生静态证据不代替实际游戏输入验证。
