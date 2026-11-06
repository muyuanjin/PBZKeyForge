# 键位增强运行组件

`pbzinput-focus.dll` 提供前台游戏窗口检查。源代码与重建脚本位于 `prototype/input-router/native/`，使用 Windows SDK 与 MSVC 编译。

`legacy-runtime.zip` 保存已发布原型的 Lua 文件，用于识别并升级本工具的旧版安装。安装器逐字节比较，保留未知修改。

构建由 `build/make-key-enhancement-runtime.ps1` 将 `prototype/input-router/ue4ss/Scripts/` 与构建时下载的 Kintaro PBZ UE4SS 打包，排除演示规则。应用内配置生成 `router-config.lua`；`key-enhancement.json` 为可编辑源。

`action-localization.json` 对应 CL-302256 的 98 个 InputAction 与 236 个输入可达能力标签。15 种语言取自游戏 `Localization/Game/{language}/Game.locres`，英文 UI 的 `en` 对应游戏资源 `en-US`。每项保留文本来源与资源引用：`native` 为原生功能文案，`native-category` 为通过武器、输入绑定或招式说明关联的原厂文本，`unresolved` 表示尚无可证明的原生显示文本关系。独立阶段通过短代码区分。

招式标题按逐项关系读取 `Weapon_FunctionName_*` 或 `PhantomEdges_FunctionName_*` 原文；共用能力不按标签中的武器族补入单把武器名。`action-details.json` 保存描述词、分类及独立的目录顺序，文本刷新保留这些字段。[完整名称与资源依据](../../docs/key-enhancement-action-names.md) 覆盖全部 338 个增强入口。

瞬身使用 `Default::UI_AbilityName_23`；对应的 `UI_AbilityNode_23` 描述架势加闪避的特殊闪避，与 `AIM_Hun_Dash` 中的 SpecialDash 组对应。完美格挡与架势状态分别使用原生教学标题 `Tutorial_Defense_Title_2`、`Tutorial_Defense_Title_1`。

原生设置文案描述的组合操作不一定等于独立输入的功能。`IA_ChangeArmed` 的独立绑定经玩家实机确认只能收起武器，使用 `action-details.json` 中的语义名称“收起武器”；`Ability.UpperBody.Hun.ChangeArmed` 保留“收/拔武器”。语言资源刷新保留原始文案，展示时按语义名称区分。

`build/extract-action-localization.py` 从安装目录只读提取语言资源，按 JSON 中保留的关系刷新译文，无需结束游戏：

```powershell
python build/extract-action-localization.py --game-exe $gameExe --paks $paksDirectory --oodle $oodleDll
```
