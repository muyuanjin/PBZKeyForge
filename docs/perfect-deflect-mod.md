# 完美防御无连点惩罚

原版连续点击防御会缩短、甚至停止刷新完美防御窗口。攻击和防御同绑左键时，连点攻击也会触发该惩罚。**完美防御无连点惩罚** 取消这一连点惩罚，每次点击仍使用游戏当前计算的基础窗口。

主页面底部右侧勾选 **完美防御无连点惩罚**，再点 **应用更改** 安装。默认关闭；取消勾选后应用即可恢复原规则。旁边的 **玩法 MOD** 标识说明它会改变战斗规则，**?** 点击打开说明。勾选只暂存更改，应用前请退出游戏，下次启动生效。所有方案、难度与防御按键共同生效。

## 修改范围

补丁覆盖 `/PBZCombatAsset/Config/` 下五个难度配置：

- `GlobalConfig_Combat_Easy`
- `GlobalConfig_Combat_Normal`
- `GlobalConfig_Combat_Hard`
- `GlobalConfig_Combat_Nightmare`
- `GlobalConfig_Combat_Handicapped`

每包仅将 `Deflect Quickclick Punish Cooldown` 的一个 8 字节 double 从 `0.4` 改为 `0`，五个字段共占 40 字节存储空间。其余包内字节保持不变。原 Combined 配置 getter、Passive、Base、Player 及其他战斗函数均保持原样。

游戏原逻辑会在达到该冷却间隔后重置连点计数，冷却为零使每次点击重新使用未受连点惩罚的窗口。基础帧数仍由难度、饰品和破防状态决定，按游戏逻辑帧率换算；普通防御窗口、角色和武器资格沿用原规则。窗口本身不保证满足所有完美防御条件。

## 安装与恢复

容器内置于程序，使用时不需要 Python、retoc、Oodle 或联网。工具管理以下三个文件：

```text
ProjectPBZ/Content/Paks/~mods/PBZKeyForge_PerfectDeflect/
    PBZKeyForge_PerfectDeflect_P.pak
    PBZKeyForge_PerfectDeflect_P.utoc
    PBZKeyForge_PerfectDeflect_P.ucas
```

启用时准备并验证容器，再安装到独立目录；关闭时只移除本工具的补丁文件。游戏原始容器、程序和存档保持不变。应用可以补齐内容仍属于本版本的残缺安装。其他文件或内容不同的补丁会显示冲突并保留；若安装过旧测试版本，请先用安装它的旧程序取消勾选并应用，再启用本版本。

键位、方案和玩法 Mod 分别写入。部分成功时，提示会指出未完成部分，再次应用只重试未完成更改。

## 已知限制

- 源资源来自 Steam 1.0302256 / CL-302256，使用 Unreal Engine 5.7.4。其他覆盖这五个配置包的 Mod 会冲突。
- 工具按所需资源的 IOStore chunk 身份识别，不以精确版本号设门槛。资源身份存在不能证明后续版本的包内容相同，完整包覆盖也不会自动合并游戏更新。
- 旧的完整取消连点惩罚效果曾获用户实机确认。本次纯数值五包容器仍需单独实机复测；离线容器校验、数据比较和安装测试不能证明游戏加载或战斗稳定性。界面“已启用”表示本工具文件已安装。

原游戏连点规则见[调查依据](deflect-input-investigation.md)。开发入口见[资源说明](../assets/perfect-deflect-mod/README.md)。
