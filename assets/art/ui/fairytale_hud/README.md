# 童话风游戏 HUD 素材

共 21 张独立透明素材。7 个 PNG 圆形按钮保留定稿图标，14 个 SVG 用于空白容器和装饰。文件可从 Godot 的 `res://assets/art/ui/fairytale_hud/` 直接引用。

| 文件 | 用途 |
| --- | --- |
| `button_public_quests.png` | 右上左侧，公共任务栏 |
| `button_public_shop.png` | 右上右侧，货架 |
| `button_inventory.png` | 右下第 1 个，道具 |
| `button_personal_quests.png` | 右下第 2 个，个人任务 |
| `button_equipment.png` | 右下第 3 个，装备 |
| `button_companions.png` | 右下第 4 个，伙伴 |
| `button_action_menu.png` | 右侧中部，收起的行为菜单 |
| `turn_banner.svg` | 顶部回合提示横幅 |
| `map_indicator.svg` | 右上地图提示底板 |
| `turn_order_heading.svg` | 左上回合顺序标题底板 |
| `turn_order_portrait_frame.svg` | 其他玩家的横向头像框，内侧透明 |
| `turn_order_badge.svg` | 空白回合顺序号徽章 |
| `turn_order_line.svg` | 回合顺序竖线，可按列表高度拉伸 |
| `turn_order_arrow.svg` | 回合顺序竖线的下端箭头 |
| `self_status_bar.svg` | 左下玩家状态底板 |
| `self_avatar_frame.svg` | 自身圆形头像框，内侧透明 |
| `self_name_ribbon.svg` | 玩家名空白缎带 |
| `status_divider.svg` | 金币、声望与六维之间的可复用分隔线 |
| `status_coin.svg` | 金币图标 |
| `status_reputation.svg` | 声望图标 |
| `decoration_sparkle.svg` | 头像或状态底板旁可复用的星光 |

- 所有演示文字和数字均已移除。回合提示、地图提示、玩家名、顺序编号、金币、声望及六维请使用独立的 `Label`。
- 两种头像框都没有角色图。正式立绘放到框下，自身头像按圆形裁切，其他玩家立绘按横向圆角矩形裁切。
- 七个按钮使用统一的 112 × 112 透明画布。以定稿的 1536 × 1024 画面作为参照，75% 版圆钮可从约 84 × 84 的控件尺寸开始排版，再按实际窗口调整。
