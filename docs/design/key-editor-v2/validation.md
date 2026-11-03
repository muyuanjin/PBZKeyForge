# 当前构建与验证

2026-11-03，Windows，使用系统自带 .NET Framework 编译器及 Windows PowerShell 5.1 构建。GUI 位于 `dist/PBZKeyForge.exe`，CLI 位于 `dist/PBZKeyForge-cli.exe`；版本字段为 1.1.1，新功能记录在 [Unreleased](../../../CHANGELOG.md)。

## 功能状态

- 按键视图同屏展示键盘与鼠标，点击按键或数字徽标在右侧编辑功能；手柄面键与十字键徽标向外分布。
- 动作视图展示原厂键、真实可编辑槽及原始记录，与按键视图共用草稿。
- 灰、橙、红分别表示原厂一致、已保存自定义及待应用；底部全方案差异支持修改前／原厂基准和四种行展示方式。
- 支持明确解绑、继承原厂、键鼠捕获与左右修饰键，以及保留 F 重攻击的左右键攻防预设。
- 默认数据包含 60 条资源注册输入、59 条最终逐槽默认及固定映射；界面包含 21 枚动作图标和 15 种语言。

## 本轮验证

所有界面检查均使用简体中文。完整语言遍历仅通过 `--all-languages` 显式启用，词典测试独立运行。

| 检查 | 结果 |
| --- | --- |
| GUI / CLI 构建 | 通过 |
| GVAS 基线往返 | `roundtrip=IDENTICAL`，51,335 字节 |
| `EditorSessionTests` | 1,222 条断言通过 |
| `InputDefaultsTests` | 618 项通过，资源注册与 90 条样本默认记录一致 |
| `MouseGuardPresetTests` | 预览、F 保留、保存重开、范围隔离及幂等性通过 |
| `BindingViewsTests` | 181 项通过 |
| `SchemeDiffViewTests` | 33 项通过 |
| `BindingDialogInteractionTests` | 35 项通过 |
| `EditorUiTests --bindings-only` | 1,336 项通过，打开并取消 63 个键鼠及 30 个手柄绑定格 |
| `WindowRenderingTests` | 30 项通过，包含实际桌面窗口截图 |
| 预设生成 | 8 份存档生成成功 |

日志及截图集中保存在 `dist/verification/`。构建脚本生成的程序与预设均为忽略产物；素材源图、资源数据、提取脚本、设计说明及许可证由 Git 管理。Python 缓存已加入忽略规则。

## 已知限制

默认资源适用于 CL-301524，未知动作仍保留原始记录编辑及“未确认”默认状态。影之武装长按的固定 Ctrl 映射独立于可编辑槽。

本轮验证使用仓库样本及其副本，未执行真实安装上的认证方案启用检查，也未启动游戏验证组合技能的时序和出招。窗口截图覆盖本机显示配置，不代表不同 DPI 显示器间动态切换。
