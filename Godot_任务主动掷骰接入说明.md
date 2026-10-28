# 任务主动掷骰接入说明

本文只描述网络契约和状态机，不依赖 Godot、C#、GDScript 或其他具体语言。

## 适用范围

仅以下玩家主动任务判定改为两阶段：

- `QUEST_SOCIAL_CHECK`
- `QUEST_FULL_SOCIAL_CHECK`

打工、防守、Boss、偷窃、调查、伙伴招募等现有判定仍由服务端使用安全随机数自动掷骰。

## 基本规则

- 骰子为普通六面骰，玩家提交的 `dice` 必须是 1 到 6 的整数。
- 服务端计算：`有效骰点 + 属性值 + 修正值 >= 难度` 时成功。
- 客户端只能提交骰点和状态版本，不能提交成功与否、最终值、属性、修正值、难度或奖励。
- 角色的固定骰点效果优先于玩家提交值。响应同时返回 `submittedDice` 和 `effectiveDice`。
- 免费或付费重掷是新的待掷骰阶段。客户端必须再次提交新的骰点，不能复用上一次点数。
- 只有最终判定会写入 `game_check_log`、发布判定完成事件、推进任务和发放奖励。

## 状态流程

1. 客户端按原格式调用 `POST /api/v1/games/{gameId}/actions`。
2. 成功响应的 `data.resultType` 为 `CHECK_ROLL_PENDING`，并包含 `data.pendingCheck`。
3. 客户端显示掷骰界面，使用 `pendingCheck.checkId` 和 `pendingCheck.stateVersion` 提交骰点。
4. 调用 `POST /api/v1/games/{gameId}/checks/{checkId}/roll`。
5. 若返回的 `pendingCheck.status` 仍为 `WAITING_ROLL`，根据 `rollType` 显示免费或付费重掷，再提交一次新骰点。
6. 若状态为 `COMPLETED`，使用 `checkResult`、`questUpdates` 和 `playerViewState` 更新界面。

## 断线恢复

进入对局或重连后调用：

`GET /api/v1/games/{gameId}/checks/pending`

- `data = null`：没有待判定。
- `data.status = WAITING_ROLL`：恢复掷骰界面。

待判定存在时，普通行动列表为空，提交其他行动会返回 `CHECK_ROLL_PENDING`。

## 超时、取消与掉线

- 每个掷骰阶段默认等待 120 秒。
- 玩家掉线不会取消判定，也不会退还已支付资源；重连后可继续提交。
- 为避免通过取消规避失败结果，客户端没有主动取消接口。
- 超时后服务端使用 `SecureRandom.nextInt(6) + 1` 代掷一次，并在记录中标记 `fallbackUsed=true`。
- 若代掷触发重掷，会创建新的待掷阶段并重新计时；再次超时会继续代掷，直至最终结算。
- 若对局已结束、回合已改变或玩家已不属于该局，超时任务会把待判定标记为 `CANCELLED`，避免永久阻塞。

## 公平性说明

客户端自报骰点无法证明随机公平性。本方案用于玩家主动操作和演示体验，不具备防作弊保证。若以后需要可信随机，应改用服务端生成或可验证随机协议。

## 接口样例

### 创建待判定

```http
POST /api/v1/games/15/actions
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "playerId": 31,
  "stateVersion": 8,
  "actionType": "QUEST_SOCIAL_CHECK",
  "payload": {
    "questId": 74,
    "attributeType": "SOCIAL",
    "sweetiePaidRerollIfNeeded": true,
    "continueOnFailure": false,
    "rewardChoice": null
  }
}
```

关键响应：

```json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {
    "oldStateVersion": 8,
    "newStateVersion": 9,
    "resultType": "CHECK_ROLL_PENDING",
    "playerFacingMessage": "任务判定已准备，请掷出六面骰并提交点数。",
    "pendingCheck": {
      "checkId": 1001,
      "gameId": 15,
      "playerId": 31,
      "questId": 74,
      "status": "WAITING_ROLL",
      "rollType": "INITIAL",
      "attemptNo": 1,
      "attributeType": "SOCIAL",
      "attributeValue": 4,
      "modifier": 1,
      "difficulty": 7,
      "socialCost": 1,
      "remainingSocial": 2,
      "stateVersion": 9,
      "expiresAt": "2026-09-30T10:30:00"
    }
  }
}
```

### 提交骰点

```http
POST /api/v1/games/15/checks/1001/roll
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "dice": 5,
  "stateVersion": 9
}
```

最终响应关键字段：

```json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {
    "submittedDice": 5,
    "effectiveDice": 5,
    "pendingCheck": {
      "checkId": 1001,
      "status": "COMPLETED",
      "stateVersion": 10,
      "fallbackUsed": false
    },
    "checkResult": {
      "dice": 5,
      "attributeType": "SOCIAL",
      "attributeValue": 4,
      "modifier": 1,
      "finalValue": 10,
      "difficulty": 7,
      "success": true
    },
    "questUpdates": [],
    "playerViewState": {},
    "playerFacingMessage": "任务判定成功。",
    "idempotentReplay": false
  }
}
```

## 错误码

| errorCode | 含义 |
|---|---|
| `PARAM_INVALID` | `dice` 不在 1 到 6，或请求字段无效 |
| `CHECK_NOT_FOUND` | `checkId` 不存在或不属于该对局 |
| `AUTH_PERMISSION_DENIED` | 判定不属于当前登录玩家 |
| `CHECK_ROLL_PENDING` | 玩家仍有待完成判定，不能执行其他行动 |
| `CHECK_STATE_CONFLICT` | 回合、位置、对局或待判定状态已经变化 |
| `GAME_STATE_VERSION_EXPIRED` | 提交的状态版本不是当前待判定版本 |
