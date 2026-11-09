# Godot 独立任务判定接入记录

更新时间：2026-11-07。

## 接入范围

- 行为菜单中的“判定”打开独立任务判定列表。
- 列表只读取 `GET /games/{gameId}/quests?playerId=...` 的 `data.quests`，仅展示 `SOCIAL_CHECK` 与 `FULL_SOCIAL_CHECK`；不混入公共候选或 `completedQuests`。
- 固定属性显示单行；多个可选属性显示“任选”并要求玩家明确选择。`requiredSuccessAttributes` 按可空逗号字符串解析，例如 049 为 `SPEED,SOCIAL,MAGIC`；该清单存在时逐项状态只读取 `checkAttributes`，缺项置灰并提示未同步，不从总进度推断，也不降级成任选。
- 准备行动分别使用 `QUEST_SOCIAL_CHECK` 与 `QUEST_FULL_SOCIAL_CHECK`，payload 只有 `questId`、`playerQuestId`、具体 `attributeType`。
- 成功准备后复用现有独立物理骰、授权轮次、重投、最终确认及 `/checks/pending` 恢复流程。

## 权威状态与身份

- 仅在新鲜 `player-view.engineState.batchId=CORE_9_PUBLIC_SHOP` 时按本记录契约开放提交；简写 `CORE_9` 和其他批次均显示不可用原因，不通过删字段重试探测能力。
- `pendingCheck.businessType=QUEST`；`businessSourceId` 对应 `playerQuestId`，不能与 `questId` 混用。
- 列表资格合并任务状态、匹配行动资格、适用的 `CURRENT_LOCATION` / `WAIT_OWN_TURN` 条件、社交点与逐属性 `available`。
- 普通判定费用读取 `requiredSocialCost`；满社交判定要求 `currentSocialPoints == effectiveAttributes.social`。
- `conditions` 或逐属性状态未同步时保持禁用，不猜测条件、不把缺失字段当作满足。

## 未知结果与刷新

- 新逻辑操作生成一次 requestId。准备响应未知时先查询 `/checks/pending`；查询失败不重提。
- pending 明确为 null 后，用户主动恢复才使用原路径、原 requestId、原 stateVersion、原 actionType 与完整 payload 重放，不生成新身份。
- 幂等重放返回的 `WAITING_ROLL` 只视为首次保存的历史回执；客户端不会直接打开骰子，而是再次 `GET /checks/pending`。只有查询到同一 `checkId`、本人、`businessType=QUEST` 且任务实例身份一致的当前待判定才恢复投骰。
- 重放后当前 pending 为 null 时不打开旧骰子、不猜测成败或退款，刷新权威视图与任务列表并提示“原判定已不再待处理”；复查失败时保留原请求恢复上下文，后续重试只查 pending，不再次重放行动。
- 最终结果展示 `checkResult.success`、`submittedDice` 与 `effectiveDice`。`questUpdates` 仅视为增量提示，不覆盖完整任务列表。
- 关闭结果页后重新查询任务、玩家视图与 available-actions，再返回判定列表。判定成功不表示任务已经正式提交或奖励已经入账。

## 本地验证边界

- `RunQuestCheckTests` 使用 `CORE_9_PUBLIC_SHOP`、完整 Result 包装的 `/quests` JSON 和真实字符串字段覆盖 049、038、固定属性、满社交、个人任务共享 DTO、判定入口、pending 身份以及 Viewport 鼠标按下/释放。
- 恢复测试覆盖响应丢失后原请求幂等重放、重放后再次查询当前 pending、当前待判定仍存在、已不再待处理、查询失败后可恢复，以及不生成第二个 requestId／请求体。
- 本地模拟与 headless 验证不代表真实服务联调；本轮没有登录或修改真实对局。
