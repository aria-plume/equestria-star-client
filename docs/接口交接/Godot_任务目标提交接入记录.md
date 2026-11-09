# Godot 行为→提交接入记录

更新时间：2026-11-09。

## 已接入范围

- 第一层 HUD 的行为菜单新增“提交”，复用现有折叠菜单和 `blocks_map_input` 路由。
- 每次打开重新读取本人 `/quests`、`/items`、新鲜 `player-view` 和 `available-actions`。
- 仅展示 `IN_PROGRESS` 且 `triggerType=SUBMIT_ITEM` 或 `PAY_RESOURCE` 的当前任务。
- `SUBMIT_ITEM` 始终先进入选卡页；按 `requiredItemId` 与 `requiredItemCategory` 过滤，两者同时存在时按 AND 匹配。
- 匹配的 `HAND` 实例按 `playerItemId` 独立提交；匹配的 `EQUIPPED` 实例只展示“已装备”，不能绕过手牌限制。
- `PAY_RESOURCE` 展示服务端返回的金币、声望和社交点费用，确认时重新同步并复核；费用变化时要求重新确认。
- 写操作统一使用 `/actions`：
  - `SUBMIT_QUEST_ITEM {questId, playerQuestId, playerItemId}`
  - `PAY_QUEST_RESOURCE {questId, playerQuestId}`
- 行动携带新逻辑操作的 UUID `requestId`、局内 `playerId` 和最新 `stateVersion`；未知结果保留原请求上下文，不自动生成新 ID 重发。
- 成功后应用权威玩家视图并刷新任务、持有物与行动资格；`READY_TO_SUBMIT` 仍由个人任务入口另行结算，不自动提交或发奖。

## 客户端结构

- `QuestTargetSubmissionModel`：纯 C# 的任务筛选、条件/余额/行动资格合并、道具实例匹配和请求构造。
- `QuestTargetSubmissionOverlay`：任务列表、横向选卡、资源确认和错误/重试状态；不直接访问 Session 或 API。
- `GameScreen.QuestTargetSubmissions`：负责身份、请求生命周期、提交前复核、统一行动提交和成功后权威刷新。
- `InventoryItemCard`：新增只读状态显示，供已装备的匹配实例复用现有卡图而不显示提交按钮。

## 验证边界

- 本地模型和 Viewport GUI 测试覆盖指定种类、类别、任选、同名实例、已装备、资源不足、确认/取消、请求字段和输入隔离。
- 本地验证不等于公网真实服务联调；未登录、未提交真实任务目标，也未改变现有对局。
- 本批不增加背包内任务交付入口，不修改个人任务最终结算、公共任务板或完整道具玩法。

需要用户决策的点：本轮未发现。
