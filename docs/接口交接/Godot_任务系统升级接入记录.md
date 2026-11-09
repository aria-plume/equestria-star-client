# Godot 个人任务系统升级接入记录

更新时间：2026-11-06。

本记录只描述客户端当前接入边界，准确字段与玩法仍以主 API 文档及任务专项为准。

## 已接入范围

- 左下角个人任务改为纵向信息卡，使用 `GET /games/{gameId}/quests?playerId=...`。
- 按 `playerQuestId` 保留服务端顺序和重复实例；不展示 `completedQuests`。
- 目标只读 `objectives`。累计进度只在 `currentValue/requiredValue` 同时存在时展示；缺字段显示“进度数据不可用”，不伪造数值。
- 只有新鲜 `player-view.engineState.questSubmissionVersion == 1` 才开放 `SUBMIT_QUEST`；按钮资格以 `submission.canSubmit` 为准。
- 放弃任务使用 `ABANDON_QUEST`，确认后发送 `questId + playerQuestId`；不在本地预扣声望或删除任务。
- 提交成功只用 `questSettlement` 展示实际结算；`coinsGranted` 已包含 `partnerBonusCoins`。
- 若存在本人 FIFO 队首，直接进入奖励处理；领取/放弃均使用 `RESOLVE_TURN_START_EFFECT`。
- ITEM 候选按准确契约解析：`cardId` 是领取实卡 ID，`itemId` 只用于卡图映射，名称和类型分别取冻结配置 `configuration.item_name/item_type`。
- 领取按钮和提交入口复用同一资格模型：本人 ITEM 队首、队首 `canExecute=true`、候选 `canExecute=true`，并校验候选必要 ID、配置身份及本地写操作互斥。
- 领取成功应用权威视图后重新 `GET /items`，持有实例只认接口返回的 `playerItemId`；不把 `cardId` 转换为背包实例。
- 仅为满包领奖接入 `GET /items` 与 `DISCARD_ITEM`；操作使用 `playerItemId`，不聚合重复 `itemId`。
- 本任务系统自身仍不扩展出售、任务历史和其他任务目标行动。基础购买、背包使用及装备/卸下已由独立的[Godot 道具购买与装备接入记录](Godot_道具购买与装备接入记录.md)接入；奖励满包整理仍复用本记录的 `DISCARD_ITEM` 边界。

## 生命周期与权威边界

- 所有写操作沿用 `requestId + playerId + stateVersion`，新逻辑操作生成一次 UUID。
- 网络未知结果保留原请求身份与完整请求，不自动以新 ID 重发。
- 成功后以 `playerViewState`、`nextAvailableActions` 和重新 GET 的列表为准；`questUpdates` 缺项不代表任务删除。
- 弹层继续遵守 `blocks_map_input`，不暂停心跳，不修改地图、棋子和摄像机状态。

## ITEM 候选契约状态

客户端已按 [ITEM奖励候选准确契约](ITEM奖励候选准确契约.md) 接入候选级资格与 `configuration` 冻结配置，不再依赖不存在的顶层 `itemName`。缺配置、必要 ID 非法、`configuration.id` 与 `itemId` 冲突或资格字段缺失时，界面显示明确异常并刷新权威视图；不会用 `cardId` 兜底、自动代选或连带禁用服务端允许的放弃奖励。

## GET quests 解析修正

- `objectiveNotes` 按可空字符串数组解析；文档隔离样例中的 `[]`、契约允许的非空说明数组和 `null` 均有本地测试。当前没有取得本次线上原始响应，不能据此断言线上不存在其他结构问题。
- `submission.rewardPreview` 与 `submission.rewardPaid` 使用不同 DTO：预览读取 `coins/prestige/partnerCoins/itemCount`；已付快照读取 `coins/prestige/partnerCoins/pendingItemRewardCount`。两者不使用仅属于 `questSettlement` 的 `partnerBonusCoins`。
- `ApiClient` 先检查 JSON 语法，再执行强类型反序列化。语法错误保留 `INVALID_JSON`；合法 JSON 的字段类型错误返回 `RESPONSE_TYPE_MISMATCH`，界面继续进入错误/重试状态，不显示为空任务列表。
- 解析诊断日志只包含相对接口路径、HTTP 状态、异常字段路径及行/字节位置，不记录认证信息或完整响应正文。

本记录不代表真实服务写操作已经验收；真实提交、放弃、领取和丢弃由用户手动测试。
