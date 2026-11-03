# 手柄方案记录的静态证据

本记录区分存档结构、游戏二进制静态行为与运行实测。结论是：
`GamepadFamilyProfiles` 的记录由 `(ProfileId, Family)` 共同区分。同为
`Xbox` 的三个记录不能视为三个设备家族，也不能作为跨方案共享键位整体修改。

## 对象和方法

- 游戏实际原生主体：安装目录下的 `platform/steam/PBZ.exe`。
  SHA-256：`7b4fcd286a350780faf0824d73aa150b21cbf1bccde883e6a96386cbe09973b4`。
- 基线：`samples/vanilla-base.sav`，SHA-256：
  `3782031244b8d71d94e84c18d066d6f5e84ff745c81b4e68567fc87694b3ce37`。
- 下文地址均是该 PE 的首选虚拟地址，ImageBase 为 `0x140000000`。
  `.xcode` 文件偏移 `0x400` 对应 RVA `0x1000`。
- 证据来自既有 `pbz-native-probe.py` 的 pefile/Capstone 只读解码，以及
  `pbz-dump/dump.exe` 对基线 GVAS 的结构转储。它们不是 REA Evidence，
  也不是恢复的源代码。没有启动游戏、附加游戏进程或修改真实存档。
- 已安装 REA 6.4.0 CLI 可从缓存调用；当前会话没有 REA MCP，历史
  `pbz-set-scheme-rea.json` 是空文件，不能作为已有反编译结果使用。

## 存档结构观察

基线的 `GamepadFamilyProfiles` 有三个记录：

| ProfileId | Family |
| --- | --- |
| `InputUserSettings.Profiles.Default` | `Xbox` |
| `PBZ.KeyProfile.Custom1` | `Xbox` |
| `PBZ.KeyProfile.Custom2` | `Xbox` |

基线 `CurrentProfileIdentifierString` 为 `PBZ.KeyProfile.Custom1`。
这些是 fixture 的结构事实，不能证明当前游戏进程采用了这些值。

## 原生结构和匹配条件

游戏反射描述表给出 `PBZGamepadFamilyProfile` 的成员；原生处理代码使用同样布局：

| 成员 | 反射描述表地址 | 实例内偏移 |
| --- | --- | --- |
| `ProfileId`，FString | `0x14c1aac68` | `0x00` |
| `Family`，FName | `0x14c1aaca0` | `0x10` |
| `Keys`，数组 | `0x14c1aad20` | `0x18` |

记录步长为 `0x28`。用户设置对象中记录数组位于 `+0x188`，计数位于
`+0x190`。此布局并非仅从字符串名称猜测：下面的实际比较与数组步进也使用它。

函数 `0x146884350` 是手柄家族投影路径。其用于检查投影是否变化的匹配循环：

- `0x146884500` 至 `0x14688450b` 比较记录 `+0x10` 与目标家族。
- 家族相同时，`0x14688450d` 至 `0x146884535` 比较该记录开头的
  `ProfileId` FString 与来源 profile 的字符串。长度/内容不等就继续下一记录。
- `0x146884537` 将记录指针增加 `0x28`。

实际应用键位的另一循环在 `0x146884690` 至 `0x1468846f7` 重复相同条件：
先比较 `Family`，再比较 `ProfileId`，找到匹配记录后读取其 `Keys`。
所以这个结论不依赖诊断字符串，也不只是未执行的结构声明。

存储更新函数 `0x146885710` 从 `0x14688fd20` 获取按 profile 构建的家族投影，
在 `0x146885791` 调用 `0x1468814c0` 清除已有对应记录，再添加新的 `0x28`
字节记录。清除函数在 `0x146881526` 比较 `Family`，在 `0x146881530`
至 `0x146881566` 比较 `ProfileId`；后续记录沿用相同条件。
不同 ProfileId、相同 Family 的记录因而不属于同一个替换目标。

构建投影函数 `0x14688fd20` 遍历用户设置对象 `+0xd8` 的 saved-profile map，
只接纳三个游戏 profile。`0x146890036` 至 `0x146890082` 复制 map 的 profile
字符串至输出记录开头，`0x146890087` 写入目标 `Family` 至记录 `+0x10`。
这是三组 `Xbox` 记录分别附着于三个方案的静态旁证。

## 设备名称与存储家族

游戏使用规范化后的家族名称定位上述记录，设备显示名称不直接充当存储身份。
函数 `0x14688ebc0` 将输入 FName 转为小写字符串，再按下表产生输出 FName：

| 输入名称条件 | 存储 Family | 输出常量地址 |
| --- | --- | --- |
| 等于 `ps`，或包含 `ps4`、`ps5`、`playstation` | `PS` | `0x14aa7a604` |
| 等于 `ns`，或包含 `switch`、`nintendo` | `NS` | `0x14c1ab850` |
| 包含 `xbox` 或 `xinput` | `Xbox` | `0x14aa520c8` |
| 其余名称 | `Other` | `0x14a6c4260` |

这里的 `PS` 是 UTF-16 字符串常量，不是把 `PlayStation` 缩写用于界面。
`0x14688eda7` 选择该常量，`0x14688edae–0x14688edb1` 构造输出 FName。
Xbox、NS、Other 分支分别在 `0x14688ed95`、`0x14688ed9e`、
`0x14688ed8c` 选择对应常量。

规范化结果直接进入家族投影：`0x146882a53` 调用 `0x14688ebc0`，
`0x146882a5b` 取输出 FName，`0x146882a5e` 将其传入
`0x146884350`。另一条路径在 `0x14688ab3a–0x14688ab45` 执行相同操作。
结合该投影对 `Family` 的精确比较，PlayStation 的标准保存身份是 `PS`，
不能仅因工具可以解析 `PlayStation` 或 `DualSense` 就把它们当作标准保存身份。

Steam Input 另有 `SteamInput_PS3`、`SteamInput_PS4`、`SteamInput_PS5`
设备名称；函数 `0x14688c440` 明确识别这三种名称。它们也符合上述 PS
规范化条件。原生程序中的 `pbz.SteamInput.PSTouch` 说明区分 Steam Input
与原生 PlayStation 的触摸板路径；这不改变存储家族名称。静态分析没有观察
具体玩家的 Steam Input 配置、连接事件或实际输入，不据此保证所有虚拟设备
都会暴露物理控制器的品牌。

## 无硬件或存档时的编辑基线

家族记录不包含手柄序列号或连接认证信息。反射描述表中的
`PBZGamepadStoredKey` 只有 `MappingName`、`Slot`、`DefaultKey`、`Key`
四个成员，对应描述地址为 `0x14c1aaac8`、`0x14c1aab40`、
`0x14c1aab80`、`0x14c1aabc0`。投影构建代码在
`0x146890449–0x14689049e` 分别复制动作、原始槽、默认键和当前键。
Family 只在外层记录中设置，不参与原厂键值的选择。

因此编辑器可以独立提供 Xbox 和 PlayStation：以随程序发布的基线保留 GVAS
标签结构，按内置原厂表恢复当前键，为目标方案设置 `ProfileId`，为目标家族
设置 `Xbox` 或 `PS`，并为每个已注册手柄槽保存对应的四个字段。
`DefaultKey` 来自该动作和 raw slot 的原厂表；新建家族的 `Key` 从相同默认
开始，不能继承另一家族的自定义键。外层还需保留
`GamepadFamilyStorageVersion = 1`，避免进入旧存储迁移分支。

版本字段具有实际行为。用户设置构造路径 `0x146881380` 在
`0x1468813a7` 将 eax 清零，`0x1468813a9–0x1468813b0` 初始化空家族数组，
`0x1468813f2` 将存储版本 `+0x180` 初始化为 0。反序列化未提供版本字段时
保留该初始值；显式保存 0 也走相同分支。

投影入口 `0x1468843c1` 检查版本是否为 0。为 0 且存在可投影的手柄槽时，
`0x146884430` 先调用存储函数 `0x146885710`，按本次目标 Family 从共享
profile 的当前键构建家族记录，随后 `0x14688444a` 才将版本设为 1。
存储函数按 `(ProfileId, Family)` 删除旧记录并写入新投影，因此不能把版本 0
与新写的家族覆盖混用：首次投影到同一家族会替换这些覆盖。若首次目标是
Xbox，只重建 Xbox 并置版本 1，其他家族则不在这次替换范围。

实际输出手柄改动时应同时写入版本 1，既覆盖新建家族，也覆盖已有空家族的
编辑。只浏览、未编辑导出或仅编辑键鼠时保留原版本。构造代码证明了初始
版本 0 和空家族数组的状态；本记录未将字段是否被某次游戏存盘省略当作
运行观察。

缺失硬件、存档或游戏安装不妨碍这类离线编辑和文件导出。将导出文件应用到
游戏是单独的操作，仍需目标游戏和存档满足正常写入要求。上述结构和静态
匹配规则支持标准记录的构造；没有把构造、往返测试当成实际手柄运行验收。

## 当前方案到引擎 profile 的链

共享设置的 `SetKeyBindingScheme` 路径位于 `0x1466edb60`。
`0x1466edb71` 写入共享设置对象 `+0x484` 的方案枚举；
`0x1466edc59` 读取该枚举，调用 `0x1468940a0` 转为 profile 字符串，
再经 `UPBZInputUserSettings` vtable `+0x338` 调用 profile 选择方法。

转换函数的静态分支与字符串初始化共同确定：

| 枚举 | profile |
| --- | --- |
| `0` | `InputUserSettings.Profiles.Default` |
| `1` | `PBZ.KeyProfile.Custom1` |
| `2` | `PBZ.KeyProfile.Custom2` |

字符串全局分别为 `0x14e00f690`、`0x14e00f6a0`、`0x14e00f6b0`；
初始化函数分别在 `0x141069430`、`0x1410693d0`、`0x141069400`。

`UPBZInputUserSettings` vtable 位于 `0x14c1ade08`，`+0x338` 指向
`0x1448640f0`。该引擎方法在 `0x1448641cb` 从对象 `+0xd8` 查找目标
profile，成功后在 `0x144864209` 复制选中 profile 字符串到对象 `+0x78`，
并调用 profile 事件/回调。其 vtable `+0x348` getter 指向 `0x1412e7b30`，
直接返回对象 `+0x78`。

这支持“游戏方案枚举决定 active profile；手柄各家族记录还须按该 profile 区分”。
它不能把单改 GVAS 的 `CurrentProfileIdentifierString` 等同于修改游戏的启动方案源。
此前 PBZS 取证已通过认证解密确认真实选择字段在 `PBZSharedSetting.sav`
的 JSON `KeyBindingScheme` 中；该独立结论不需要在此记录任何加密密钥。

## 证据边界和验证方案

本轮没有游戏运行实测，不能声称某次控制器连接、热切换或输入事件已经走过上述路径。
函数命名一部分依据引用的源文件/日志线索，成员身份另由反射表和实际访问交叉支持。
安装目录 `steam/PBZ.exe` 是较小启动器；已分析的上述 `platform/steam/PBZ.exe`
包含 `UPBZInputUserSettings`、其 vtable、手柄存储与投影的直接机器码。

工具修正可先在基线副本验证：只编辑 Custom1 对应 `(ProfileId, Family)` 的一个
手柄键，序列化后重新解析，检查目标记录变化、Default/Custom2 的完整记录不变，
并保留未识别字段及其它 family。另做同一 profile 的多 family 副本，确认目标
family 的区分没有丢失。原始 fixture 的不编辑往返必须仍为 byte-identical。
这些检查验证工具的数据隔离，不冒充游戏运行验收。

若后续需要游戏运行验收，可在独立存档副本和隔离的存档根目录下为两个自定义
profile 设置不同的合法手柄键，分别选中方案并观察编辑页和实际输入；是否能把
本地/共享设置完全隔离必须先核对。未隔离前不使用真实存档做探针。
