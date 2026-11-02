# 键位格式与编辑

## 文件与数据模型

`<游戏根目录>/ProjectPBZ/Saved/EnhancedInputUserSettings.sav` 是本项目已支持版本的明文 GVAS，没有 PBZS 加密封装。不要因此推断所有 `.sav` 都是明文。

```text
GVAS header（原样保存）
顶层 tagged properties，以 FString("None") 结束
  CurrentProfileIdentifierString     # Enhanced Input 元数据
  GamepadFamilyProfiles[]
    ProfileId / Family / Keys[]
子对象块
  BlobA / BlobB / recordCount
  records[]                         # 各方案键鼠覆盖
尾部对象属性与 ObjectEnd（原样保存）
```

`SaveFile.Header`、`Props`、`Records`、`BlobRest` 分别保留这些部分，入口是 [src/Gvas.cs](../../../src/Gvas.cs)。`GamepadProfiles` 保留方案和设备家族关系；`GamepadKeysFor(profileId)` 返回指定方案的手柄键表。只需要某一种设备家族时，还应筛选 `Family`。

## 键鼠覆盖

三个已知 profile：

| 显示含义 | ProfileId |
| --- | --- |
| 默认预设 | `InputUserSettings.Profiles.Default` |
| 自定义 1 | `PBZ.KeyProfile.Custom1` |
| 自定义 2 | `PBZ.KeyProfile.Custom2` |

子对象块在样本中以三个小端 int32 开始：`0, 2, recordCount`。保留前两值而非对所有版本硬编码。每条 record：

```text
classPath: FString
objectName: FString
entryCount: int32
entries[entryCount]:
  actionName: FString
  keyName: FString
  X: FString
  Y: FString
  opaquePadding: 5 bytes
  slot: uint8
profileId: FString
```

样本中 `X/Y` 常为 `"None"`、padding 常为零；修改已有记录时保留它们，不把“常见值”当成重建所有记录的理由。尾部不是无用垃圾，不能截掉。

键鼠使用物理槽 `0 / 2 / 4`，手柄使用 `1 / 3 / 5`。这是 `EPlayerMappableKeySlot` 枚举，不是数组下标。工具界面的“绑定 1 / 2 / 3”和部分 API 的 `slotIndex=0/1/2` 是逻辑编号，需做转换。

常用动作及键名：

| 动作 | 标识 |
| --- | --- |
| 轻攻击 / 重攻击 | `IA_LightAttack` / `IA_HeavyAttack` |
| 防御 / 闪避 | `IA_Guard` / `IA_Dodge` |
| 高速移动 / 跳跃 | `IA_MoveFast` / `IA_Jump` |
| 交互 / 处决 | `IA_Interact` / `IA_Execute` |

常用键名为 `LeftMouseButton`、`RightMouseButton`、`LeftShift`、`SpaceBar`、`F8`。其余动作和键名优先从原存档、`MainForm.Actions`、`KeyNames` 读取；不要按显示翻译猜标识。

有动作覆盖时，不能假定未显式保留的原按键仍然有效。“左键也能防御”应显式保留 `RightMouseButton` 并增加 `LeftMouseButton`；仅将防御覆盖成左键是替换语义。不同动作共用同一个键是 mod 的目标能力，不能按游戏页面的冲突检查把它自动拆开。具体动作能否同时执行还受游戏状态和输入处理影响。

精确修改一个已有条目的 C# 示例（只在内存修改，重复条目要先明确选择）：

```csharp
var save = SaveFile.Load(copyPath);
string profile = "PBZ.KeyProfile.Custom1";
var record = save.RecordFor(profile);
if (record == null) throw new InvalidDataException("Missing profile");
var entry = record.Entries.Single(e => e.Name == "IA_Jump" && e.Slot == 0);
entry.Key = "F8";
byte[] output = save.ToBytes();
// 验证范围和回读后，才写副本或进入安全替换流程。
```

新建覆盖时用 `BlobEntry`，按原记录形态设置 `Name/Key/X/Y/Pad/Slot`。删除覆盖表示回落到游戏默认，不等于把动作设为“没有按键”；不要用显示文字“默认”作键名。

## 手柄记录必须按方案定位

原始样本的三条 `GamepadFamilyProfiles` **全部为 Xbox**，分别属于默认、自定义 1、自定义 2。记录身份至少包含 `(ProfileId, Family)`；不得把这三条当成三种手柄，也不得因属性名带 `Family` 就认为跨方案共享。

`Keys` 条目含 `MappingName`、`Slot`、`DefaultKey`、`Key`。`Key` 是修改落点；`DefaultKey` 和其余元数据保持原样。槽名分别为：

| 逻辑绑定 | 物理枚举字符串 |
| --- | --- |
| 1 | `EPlayerMappableKeySlot::Second` |
| 2 | `EPlayerMappableKeySlot::Fourth` |
| 3 | `EPlayerMappableKeySlot::Sixth` |

```csharp
var owner = save.GamepadProfiles.Single(p =>
    p.ProfileId == profile && p.Family == "Xbox");
var item = owner.Keys.Items.Single(x =>
    SaveFile.Field(x, "MappingName") == "IA_Guard" &&
    SaveFile.Field(x, "Slot") == "EPlayerMappableKeySlot::Second");
SaveFile.SetField(item, "Key", "Gamepad_LeftShoulder");
```

常用按钮：X 为 `Gamepad_FaceButton_Left`，A 为 `Gamepad_FaceButton_Bottom`，LB 为 `Gamepad_LeftShoulder`，RB 为 `Gamepad_RightShoulder`，RT 为 `Gamepad_RightTrigger`。这些是引擎键名；显示层可另映射为 PlayStation 符号。

目标槽不存在时，克隆**相同 profile、相同 family、相同动作**的已有条目，使用 `SaveFile.CloneNode` 深拷贝，仅改变 `Slot` 和 `Key`。没有可用模板或缺失归属字段时不能猜结构。不要删除未识别字段，或认为所有手柄条目都必以 `Gamepad_` 开头。

## 新增、替换和重置分开实现

本项目 `Presets.Apply()` 是 GUI 与 CLI 共用的预设规则，见 [src/Presets.cs](../../../src/Presets.cs)。普通预设只作用于所选 profile 的键鼠及对应手柄记录；`vanilla` 是显式全局重置例外。

- `onekey`：增加左键 / X 防御，保留右键 / LB 和其他不同的已有防御键。
- `onekey_dodge`：把防御移到左键 / X，再把右键 / LB 用于闪避与高速移动；它会替换相关动作绑定，不是前一项的简单叠加。
- 恢复默认：键鼠移除目标覆盖；手柄将 `Key` 恢复为对应 `DefaultKey`。全局操作必须明确范围。

新增绑定先规划再提交。三个槽不一定是一槽一条；同槽可能有多条记录。既有不同键都必须保留，空槽或冗余重复槽才可复用。简单贪心会误判容量：槽 0 为 F8、槽 2 为 F9、槽 4 同时含 F8/F9，可保留槽 4 并复用另两槽。当前 planner 枚举三个槽的少量分配，选保留全部既有键及目标键、改动最少的方案；任一目标设备容量不足时整组拒绝。

## GVAS 移植注意

完整读写布局以 `Gvas.cs` 为准，这是样本支持实现，不是通用 Unreal SaveGame 库：

- 小端整数；`FString` 正长度包含末尾 NUL，负长度表示 UTF-16，零表示空串。
- 普通标签是 `name / type / 特定类型元数据 / index:int32 / size:int32 / hasGuid:uint8 / 可选16字节GUID / value`。
- `None` 终止符只有 FString，没有后续 type。
- `ArrayProperty` 的元数据含 inner type、struct name 和 outer；值为 count 加若干以 None 结束的属性列表。`StructProperty` 的值也是属性列表。
- 本格式 `EnumProperty` 是特殊包装，元数据后跟一个完整的空名 `ByteProperty` 子标签，包装本身没有普通标签的 index/size；`Slot` 值存于嵌套节点。
- 改变字符串长度或数组条数后必须重算包围结构的 size/count；不能拿十六进制编辑器原位覆盖不同长度字符串。
- 当前 writer 输出 UTF-8 FString；不能声称所有含 UTF-16 的未知存档都能字节无损往返。零修改重建不一致时先扩展格式支持，不写回该文件。

检查修改范围可将解析后的目标记录还原为修改前版本，再序列化比较完整字节；这样允许目标长度变化，同时能发现其他方案、未知数据或尾部被误改。
