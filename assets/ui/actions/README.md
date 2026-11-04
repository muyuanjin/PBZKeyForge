# 动作图标

21 枚透明 PNG，文件名与游戏动作 ID 对应。画布为 128 × 128，图形保持比例并居中于 96 × 96 范围内；颜色为 `#EEEAE1`，适用于深色界面。界面推荐以 32 × 32 或 40 × 40 显示，并保留动作文字标签。

图标用于表达动作，动作名与 `src/I18n.cs` 一致。影之武装系列统一采用大斧意象；图形是编辑器的动作标识。

| 文件名 | 动作 | 图形 |
| --- | --- | --- |
| `IA_LightAttack.png` | 轻攻击 | 细剑与短斩弧 |
| `IA_HeavyAttack.png` | 重攻击 | 宽剑与落地冲击 |
| `IA_Guard.png` | 架势/格挡 | 交剑招架与碰撞火花 |
| `IA_Dodge.png` | 闪避 | 后仰避让人形与弯曲箭头 |
| `IA_Jump.png` | 跳跃/特殊移动 | 腾空人形与上箭头 |
| `IA_Crouch.png` | 下蹲 | 低重心蹲姿 |
| `IA_MoveFast.png` | 高速移动 | 前倾疾跑人形与速度线 |
| `IA_MoveSlow.png` | 低速移动 | 直立慢走人形 |
| `IA_LockTarget.png` | 锁定敌人 | 四角准星与中心菱形 |
| `IA_Interact.png` | 对话/交互 | 伸出的手掌 |
| `IA_Execute.png` | 处决敌人 | 向下短刃与冲击 |
| `IA_ShadowArm.png` | 影之武装 | 长柄大斧 |
| `IA_ShadowArm_LongPress.png` | 影之武装（长按） | 同款大斧与蓄力光芒 |
| `IA_SwitchWeapon.png` | 更换武器 | 双剑与切换箭头 |
| `IA_SwitchShadowArm.png` | 更换影之武装 | 同款大斧与循环箭头 |
| `IA_UltimateSkill.png` | 绝技 | 剑与放射光芒 |
| `IA_Sense_Default.png` | 澄心洞照 | 眼睛与感知波纹 |
| `IA_OpenCharacterMenu.png` | 打开角色菜单 | 角色半身像 |
| `IA_OpenMapMenu.png` | 打开地图菜单 | 折叠地图与标记 |
| `IA_UseTopItem.png` | 使用上方药 | 药瓶与上箭头 |
| `IA_UseBottomItem.png` | 使用下方药 | 药瓶与下箭头 |

源图位于 `sources/`：`action-atlas.png` 为动作图集，`detail-atlas.png` 为影之武装、格挡与闪避图集，`slow-walk.png` 为慢走源图，`interact.png` 为交互源图。图集处理脚本为 `build/prepare-action-icons.py`，清理柔光、统一颜色并输出透明图标与全套预览。图集使用各自的行列和动作顺序，按 `--columns`、`--actions` 和 `--row-boundaries` 指定：

```powershell
python build\prepare-action-icons.py <atlas.png>
```

局部图集按行指定动作，只替换指定文件。例如三列两行的五枚图集，末格留空：

```powershell
python build\prepare-action-icons.py <detail-atlas.png> --columns 3 --actions IA_ShadowArm,IA_ShadowArm_LongPress,IA_SwitchShadowArm,IA_Guard,IA_Dodge
python build\prepare-action-icons.py --preview-only
```

慢走与交互源图单独处理：

```powershell
python build\prepare-action-icons.py assets\ui\actions\sources\slow-walk.png --columns 1 --actions IA_MoveSlow
python build\prepare-action-icons.py assets\ui\actions\sources\interact.png --columns 1 --actions IA_Interact
```

预览输出到 `dist/action-icons-preview.png`，同时展示 128px 与 32px 图形。
