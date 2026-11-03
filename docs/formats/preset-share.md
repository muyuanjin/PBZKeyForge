# PBZ2 预设分享格式

分享文本为 `PBZ2:名称:内容`。最后一个冒号分隔名称和内容，名称可含冒号。
名称保留明文，换行转为空格，两端空白去除，长度为 1–80 字。
内容为无 `=` 填充的 Base64URL，仅使用 ASCII 字母、数字、`-`、`_`。

## 二进制布局

Base64URL 解码后的第一个字节是编码方式：`0` 表示原始 Protobuf，`1` 表示
原始 Protobuf 经 RFC 1951 DEFLATE 压缩。其余字节为相应数据，不使用 zlib 或 gzip 包装。
导出同时取得两种表示，只有压缩数据更短才使用 `1`；长度相同时使用 `0`。

[Protobuf schema](preset-share.proto) 是跨语言解析契约；C# 使用内嵌的
protobuf-net 2.4.9，.NET Framework 4.5.2 目标，无额外 NuGet 依赖。
[DTO 与编解码实现](../../src/PresetShare.cs) 的字段编号与 schema 一致。

| 字段 | 含义 |
| --- | --- |
| `baseline` | 必须识别的原厂基线编号；当前为 `1`，对应 CL-301524。 |
| `scopes` | 明确包含的设备；即使没有改动也保留这个消息。 |
| `device` | `0` 键鼠、`1` Xbox、`2` PlayStation，设备编号在每个范围内只记录一次。 |
| `action` | 下表的永久动作编号，从 `1` 开始。 |
| `added` / `removed` | 永久按键编号的 packed uint32 数组；空数组省略。 |
| `extra_keys` / `extra_families` | 自定义引擎键名及存档中的其他家族名，UTF-8 字符串，每个不同值只记录一次。 |

动作和设备 ID 的 `0` 含义不同：动作 `0` 无效；设备 `0` 是键鼠，Protobuf 可以省略该字段，
但外层范围消息必须存在。范围不可重复，同一范围的动作不可重复。
增减键互不重叠，新增键不重复原厂键，移除键必须来自该基线，不能移除固定映射。
写出时范围按家族名、动作按编号、键按编号排序。

普通按键均通过永久编号表示。界面也支持手动输入引擎键名，存档允许其他家族名，
因此这些值使用消息内字典：ID `16384 + i` 指向相应 `extra_*` 列表的第 `i` 项。
`1–16383` 预留给永久编号；未知永久 ID 或字典索引不能按名字猜测，也不能丢弃。

## 设备范围与套用

保存当前方案时明确记录键鼠和该方案实际存在的所有手柄家族，包括尚未应用的修改。
复制时可以筛选全部所含设备、键鼠、所有控制器或一个家族；筛选不修改原预设。

- 追加按键：在所记录动作上保留已有键并补充预设结果；纯解绑不删除已有键。
- 替换按键：只将所记录动作替换为该基线加差异后的结果。
- 仅用预设：重建所有明确范围中的按键，再套用差异；未包含的设备和其他方案保持。

空范围的差异列表表示恢复该范围的原厂布局。“缺少手柄改动记录”不能推导为“未包含手柄”。
接收存档缺少范围要求的手柄家族时拒绝套用，不将一个家族改写为另一家族。
所有规划先在副本进行；任一动作容量不足时整次拒绝，不产生部分草稿。

## 编号与演进

动作、按键和设备编号独立于界面排序、翻译和可变枚举；新编号只能追加，删除后保留编号，
不复用。下表和源码中的编号具有相同含义。

基线 `1` 的动作级原厂集合冻结在 [PresetBaseline](../../src/PresetBaseline.cs)，
不从实时默认表计算。适配新的原厂布局时新增基线编号，保留旧基线定义和解析。
当前构建仅接受已实现的基线 `1`；未知基线、动作、按键、设备或编码方式提示更新程序，
整份预设拒绝导入。

未知 Protobuf 字段只允许承载不改变键位含义的可选信息，例如描述；旧解析器可以跳过。
改变改键语义必须采用新的文本协议版本，不能借未知字段悄悄扩展。
现有字段编号及类型不更改、不复用。

## 互操作样例

`PBZ2:少主剑:AAgBEgcSBQgDEgEB`

其内容解码为十六进制 `00 08 01 12 07 12 05 08 03 12 01 01`：
原始 Protobuf、基线 `1`、键鼠范围、动作 `3`（格挡）、新增键 `1`（鼠标左键）。
这条向量同时约束 packed 数组、空列表省略和设备 `0` 的表示。

## 永久动作编号

| ID | 动作 |
| --- | --- |
| 1 | `IA_LightAttack` |
| 2 | `IA_HeavyAttack` |
| 3 | `IA_Guard` |
| 4 | `IA_Dodge` |
| 5 | `IA_Jump` |
| 6 | `IA_Crouch` |
| 7 | `IA_MoveFast` |
| 8 | `IA_MoveSlow` |
| 9 | `IA_LockTarget` |
| 10 | `IA_Interact` |
| 11 | `IA_Execute` |
| 12 | `IA_ShadowArm` |
| 13 | `IA_ShadowArm_LongPress` |
| 14 | `IA_SwitchWeapon` |
| 15 | `IA_SwitchShadowArm` |
| 16 | `IA_UltimateSkill` |
| 17 | `IA_Sense_Default` |
| 18 | `IA_OpenCharacterMenu` |
| 19 | `IA_OpenMapMenu` |
| 20 | `IA_UseTopItem` |
| 21 | `IA_UseBottomItem` |

## 永久按键编号

| ID | 引擎键名 |
| --- | --- |
| 1 | `LeftMouseButton` |
| 2 | `RightMouseButton` |
| 3 | `MiddleMouseButton` |
| 4 | `ThumbMouseButton` |
| 5 | `ThumbMouseButton2` |
| 6 | `LeftShift` |
| 7 | `RightShift` |
| 8 | `LeftControl` |
| 9 | `RightControl` |
| 10 | `LeftAlt` |
| 11 | `RightAlt` |
| 12 | `SpaceBar` |
| 13 | `Tab` |
| 14 | `CapsLock` |
| 15 | `Escape` |
| 16 | `Enter` |
| 17 | `BackSpace` |
| 18 | `LeftBracket` |
| 19 | `RightBracket` |
| 20 | `Semicolon` |
| 21 | `Quote` |
| 22 | `Comma` |
| 23 | `Period` |
| 24 | `Slash` |
| 25 | `Backslash` |
| 26 | `Tilde` |
| 27 | `A` |
| 28 | `B` |
| 29 | `C` |
| 30 | `D` |
| 31 | `E` |
| 32 | `F` |
| 33 | `G` |
| 34 | `H` |
| 35 | `I` |
| 36 | `J` |
| 37 | `K` |
| 38 | `L` |
| 39 | `M` |
| 40 | `N` |
| 41 | `O` |
| 42 | `P` |
| 43 | `Q` |
| 44 | `R` |
| 45 | `S` |
| 46 | `T` |
| 47 | `U` |
| 48 | `V` |
| 49 | `W` |
| 50 | `X` |
| 51 | `Y` |
| 52 | `Z` |
| 53 | `Zero` |
| 54 | `One` |
| 55 | `Two` |
| 56 | `Three` |
| 57 | `Four` |
| 58 | `Five` |
| 59 | `Six` |
| 60 | `Seven` |
| 61 | `Eight` |
| 62 | `Nine` |
| 63 | `F1` |
| 64 | `F2` |
| 65 | `F3` |
| 66 | `F4` |
| 67 | `F5` |
| 68 | `F6` |
| 69 | `F7` |
| 70 | `F8` |
| 71 | `F9` |
| 72 | `F10` |
| 73 | `F11` |
| 74 | `F12` |
| 75 | `Up` |
| 76 | `Down` |
| 77 | `Left` |
| 78 | `Right` |
| 79 | `Home` |
| 80 | `End` |
| 81 | `PageUp` |
| 82 | `PageDown` |
| 83 | `Insert` |
| 84 | `Delete` |
| 85 | `NumPadZero` |
| 86 | `NumPadOne` |
| 87 | `NumPadTwo` |
| 88 | `NumPadThree` |
| 89 | `NumPadFour` |
| 90 | `NumPadFive` |
| 91 | `NumPadSix` |
| 92 | `NumPadSeven` |
| 93 | `NumPadEight` |
| 94 | `NumPadNine` |
| 95 | `Gamepad_FaceButton_Bottom` |
| 96 | `Gamepad_FaceButton_Right` |
| 97 | `Gamepad_FaceButton_Left` |
| 98 | `Gamepad_FaceButton_Top` |
| 99 | `Gamepad_LeftShoulder` |
| 100 | `Gamepad_RightShoulder` |
| 101 | `Gamepad_LeftTrigger` |
| 102 | `Gamepad_RightTrigger` |
| 103 | `Gamepad_LeftTriggerAxis` |
| 104 | `Gamepad_RightTriggerAxis` |
| 105 | `Gamepad_LeftThumbstick` |
| 106 | `Gamepad_RightThumbstick` |
| 107 | `Gamepad_DPad_Up` |
| 108 | `Gamepad_DPad_Down` |
| 109 | `Gamepad_DPad_Left` |
| 110 | `Gamepad_DPad_Right` |
| 111 | `Gamepad_Special_Left` |
| 112 | `Gamepad_Special_Right` |
| 113 | `Multiply` |
| 114 | `Add` |
| 115 | `Subtract` |
| 116 | `Decimal` |
| 117 | `Divide` |
| 118 | `NumLock` |
| 119 | `ScrollLock` |
| 120 | `Pause` |
| 121 | `PrintScreen` |
| 122 | `LeftWindows` |
| 123 | `RightWindows` |
