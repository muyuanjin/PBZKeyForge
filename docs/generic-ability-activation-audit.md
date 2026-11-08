# 通用能力入口核查

CL-302256 的原生输入表包含 236 个能力 TriggerTag。完整 AssetRegistry 的 5,728 个能力类 CDO 与导出名称核查发现，其中九个标签没有任何能力类引用；原程序没有这些标签的 UTF-8／UTF-16 字面量，原配置也没有对应重定向。直接能力目录保留其余 227 项，98 个原生 InputAction 和 1,250 个具体招式入口全部保留。

这九项仍存在于当前装备或角色使用的输入表，属于输入路线记录。输入表中出现 TriggerTag 不等于存在能用该标签激活的能力。

| 不可直接激活的标签 | 当前输入表所属配置 |
| --- | --- |
| `Ability.Action.Hun.ChiZheCanJian.CToZui` | 痴者残剑／痴者醉剑战技 |
| `Ability.Action.Hun.Heavy.BronzeSword.DB` | 天玄青铜剑重击 |
| `Ability.Action.Hun.Heavy.SlitheringSword.DB2` | 灵蛇软剑重击 |
| `Ability.Action.Hun.Light.GuiTouDao.DA` | 红妆鬼斩轻击 |
| `Ability.Action.Hun.ShachiAbility.HuangJinHuoChong.Common_03` | 黄金火铳普通连招 |
| `Ability.Action.Hun.ShachiAbility.JinGua.SprintAir` | 长柄金瓜空中输入 |
| `Ability.Action.Hun.ShachiAbility.ZhuoBiDun.Parry` | 拙壁盾输入／推击 |
| `Ability.SpecialMove.JumpOut.Left` | 角色跳跃输入 |
| `Ability.SpecialMove.JumpOut.Right` | 角色跳跃输入 |

原生 `TryActivateExternalActionAbilityInterruptLowerByTag` 从角色已授予的能力列表中匹配实际能力或主实例的 `AbilityTags`，再尝试激活匹配句柄。它不会按输入 TriggerTag 创建能力。静态调用链、逐项匹配指令和 UHT 的 `AbilityTags` 偏移 `0xa8` 一致，详见[机器可读证据](generic-ability-activation-audit.json)。

`Ability.Native.Crouch` 在原程序中有原生实现标签，保留为可选入口。其余 CDO 标签匹配用于定位能力来源；它本身不代替精确 `AbilityTags`、装备授予及释放资格证据。

九条失效路线的原始来源保留在 `actions.json` 的 `unsupportedInputRoutes`，用于审计。已有配置仍可读取、编辑和保存，原标签保持不变；若仍绑定这些路线，编译与应用会明确报告当前游戏未提供该能力，提示选择原生输入或具体招式。它们不会被自动替换成其他攻击。

`build/audit-generic-ability-activation.py` 读取保留的全类核查和重定向证据，重新核对当前安装中的九项输入表、装备引用、原程序字面量、激活指令与 UHT 偏移，并生成 `actions.json` 的可用能力与排除记录。它接受输入提取器的完整 236 标签结果，也接受已经分组的目录。提供 `--class-scan`、`--redirect-evidence` 可使用新的完整观察，默认使用本证据文件中保留的 CL-302256 观察。

输入资源重新提取后，依次运行：

```powershell
python build/audit-generic-ability-activation.py --game-exe $gameExe --paks $paks --oodle $oodle
python build/extract-native-move-inputs.py --game-exe $gameExe --paks $paks --oodle $oodle
python build/merge-move-restrictions.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/build.ps1
```

首步生成 227 个直接能力与九条排除记录；持续输入与限制数据再从该目录生成，随后构建应用。更新游戏基线时先重新取得完整能力类与重定向观察，再执行同一顺序。

## 验证边界

当前结论来自原生资源与静态执行路径，未启动游戏验证动画、命中或扣血表现。
