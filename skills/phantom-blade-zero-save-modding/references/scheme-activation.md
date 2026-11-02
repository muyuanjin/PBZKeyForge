# 生效方案与认证容器

## 两个“当前方案”不是同一字段

GVAS 的 `CurrentProfileIdentifierString` 是 Enhanced Input 元数据。已研究安装中曾出现它指向 Custom1、而共享设置选择 Custom2 的情况。`SaveFile.SetActiveProfile()` 只更新前者，不能作为游戏启用功能。

真正控制方案选择的是相邻 `PBZSharedSetting.sav` 的 JSON `KeyBindingScheme`：

| JSON 字符串 | 键鼠及手柄的 ProfileId |
| --- | --- |
| `DefaultPreset` | `InputUserSettings.Profiles.Default` |
| `Custom1` | `PBZ.KeyProfile.Custom1` |
| `Custom2` | `PBZ.KeyProfile.Custom2` |

JSON 中是上述字符串；游戏内部枚举为 `0 / 1 / 2`，不要直接把数值写入 JSON。未知值、重复字段或无法认证时拒绝修改。

## 优先复用公开接口

实现位于 [src/GameSchemeSettings.cs](../../../src/GameSchemeSettings.cs)：

```csharp
// 读取；这两种入口都不修改存档。
var settings = GameSchemeSettings.LoadForInputSave(inputSavePath);
string activeProfile = settings.ProfileId;
// 或显式提供共享存档及原生游戏 EXE：
settings = GameSchemeSettings.Load(sharedSavePath, nativeGameExePath);

// 写入：只在获准修改的目标或隔离副本上调用，游戏必须退出。
string backupPath = settings.Activate("PBZ.KeyProfile.Custom1");
// 已是该方案时返回 null，不重新加密、不更新文件时间。
```

`LoadForInputSave()` 在当前 Steam 布局中从存档目录向上寻找 `platform/steam/PBZ.exe`。读取前优先用 EXE SHA-256 识别精确构建；对未登记哈希，只尝试已验证过的密钥定位方式，且派生密钥 SHA-256 必须与该构建的已核实密钥指纹一致。密钥指纹不是密钥，工具不保存或分发 AES 密钥。随后仍必须通过目标 PBZS 的 GCM 认证。孤立复制一个 `.sav` 可能无法自动找 EXE；副本测试应使用显式 `Load()` 或保留安装相对布局。

CLI 的 `--schemes` 读取真实方案并以 `*` 标记；读取不到时不把 GVAS 元数据标为真实当前。普通 CLI 预设写出功能不等于安全安装，建议指定完整 `--scheme`，不要依赖回退选择：

```powershell
.\dist\PBZKeyForge-cli.exe --schemes $inputCopy
.\dist\PBZKeyForge-cli.exe --apply-preset $inputCopy $outputCopy onekey --scheme PBZ.KeyProfile.Custom1
# 下一条会修改 inputCopy 相邻的 PBZSharedSetting.sav；必须是隔离副本。
.\dist\PBZKeyForge-cli.exe --activate $inputCopy PBZ.KeyProfile.Custom1
```

## PBZS v1/1 布局

该共享存档是 AES-256-GCM 认证容器，不是 GVAS，也不是确认过的 Oodle 压缩数据。不要沿旧压缩猜测去解包。

| 文件偏移 | 长度 | 内容 |
| --- | --- | --- |
| `0x00` | 8 | 固定头 `50 42 5A 53 01 01 00 00`，即 PBZS + 当前版本/标志字节 |
| `0x08` | 12 | nonce |
| `0x14` | 16 | GCM authentication tag |
| `0x24` | 至文件末尾 | ciphertext |

当前实现没有 AAD；头部单独按固定字节校验。常见密码库 API 要求传入 `ciphertext || tag`，但**磁盘顺序是 tag 在 ciphertext 前面**，不能混用。

通过认证后，明文为一个正长度 UTF-8 FString：

```text
int32 littleEndianLength = JSON的UTF8字节数 + 1
JSON UTF8 bytes
0x00
```

长度包括末尾 NUL，不包括前四字节；必须满足 `length == plaintext.Length - 4`。修改时只替换唯一 `KeyBindingScheme` 值对应的文本范围，重算 UTF-8 字节数。重新序列化整个 JSON 会产生不必要的重排、空白或数值变化。

写回每次生成新的随机 12 字节 nonce 和新的 tag。除显式 no-op 外，不要求密文与原文件相同；要求认证解密后的 JSON 仅目标字段发生变化。

## 密钥来源与版本支持

项目从用户本地原生 EXE 推导 32 字节密钥，不在工具或技能中分发密钥。当前 `ReadKey()` 对已研究 Steam 构建使用四段各 16 字节常量：

```text
key[0:16]  = EXE[0x0c1c7b48 : +16] XOR EXE[0x0c1c7b28 : +16]
key[16:32] = EXE[0x0c1c7b38 : +16] XOR EXE[0x0c1c7b58 : +16]
```

这些是**文件偏移**，不是 RVA 或虚拟地址；它们在 `CL-301524_JKS-17219` 原生 EXE 上取证验证，且对应派生密钥指纹为 `65b75c306a77eb2df3ed5a7e6b3f8dbef360f6abefa74c465196cfa19af9637c`。当前仓库只有这一条已验证定位路径。不同 EXE 哈希仍可在相同位置派生出相同的已验证密钥；指纹匹配后还必须让实际存档通过 GCM。读取策略按顺序尝试登记路径；每条都要匹配自己的容器头并通过 AES-GCM 认证。认证失败才可继续后备路径；一条路径认证成功后若明文格式无效，则直接拒绝，不用其他方式重新解释明文。

当前没有第二个构建或第二条密钥定位算法的认证证据，因此未知哈希的后备方案仅复用上述定位方式，并要求密钥指纹及目标容器认证同时通过。若更新移动了常量、改变密钥或 PBZS 格式，当前路径会失败；新定位路径只有在独立取证确认构建身份、密钥定位/组合及 PBZS 布局，并用隔离副本完成认证读写往返后才能登记。密钥指纹不匹配或全部路径认证失败时明确报告无法验证，提示用户在游戏内手动切换方案；不会修改原文件。不要因文件头、可读 JSON、偏移处有数据或可解析的版本字符串而猜测兼容。

适配新构建时，从 `FPBZSaveCrypto`、PBZS 容器和 `SetKeyBindingScheme` 路径追踪实际实现，重新定位常量及密钥组合方式；登记 EXE 哈希、派生密钥指纹及该构建的主/后备读取路径，再做副本认证往返和无关设置保持验证。密钥只在内存中短暂使用，用后清理；日志和交付物记录构建身份、认证结果，不记录密钥或完整玩家设置。

Windows 的 .NET Framework 无需额外运行时即可通过 CNG `bcrypt.dll` 使用 AES-GCM。本项目用 `BCryptOpenAlgorithmProvider("AES")`、`ChainingModeGCM`、`BCryptGenerateSymmetricKey` 及 `BCryptEncrypt/Decrypt`；移植可使用其他具备认证校验的 AESGCM 库，无需复制 CNG 结构体到非 Windows 平台。

## 写入次序与失败恢复

单文件流程：认证读取 → 验证目标方案 → 检查游戏已退出及原文件版本 → 修改明文 → 新 nonce 加密 → 临时文件回读认证 → 再检查版本 → 外部备份 → 同卷原子替换。认证失败、目标未知或文件在此期间变化时保留原文件。

同时修改键位和方案时先验证两份输入，再写键位，最后启用方案。如果后者失败，键位可能已经保存：向用户说明部分成功，保留待启用意图，重试时仅修改共享设置。不要声称两个 `File.Replace` 构成一次原子事务。

游戏运行中不写盘；可以展示当前已读磁盘值和待编辑草稿，但不要把磁盘值当成正在运行的游戏内存状态。退出游戏后重新读取再应用。
