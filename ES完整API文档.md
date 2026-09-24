# ES 后端完整 API 文档

> 根据当前源码整理。示例 ID 和业务数据仅用于说明结构；字段为 `null` 表示该动作或状态下没有对应结果。

## 1. 通用约定

- 基础地址：`http://localhost:8080/api/v1`
- 请求和响应编码：UTF-8
- JSON 请求头：`Content-Type: application/json`
- 登录接口返回访问令牌；需要认证的接口使用 `Authorization: Bearer <accessToken>`。
- 时间字段采用 Spring `LocalDateTime` 的 ISO 格式，例如 `2026-09-23T10:30:00`。

成功响应：

```json
{"code":1,"msg":"success","errorCode":null,"data":{}}
```

失败响应：

```json
{"code":0,"msg":"错误说明","errorCode":"BUSINESS_ERROR_CODE","data":null}
```

分页响应的 `data`：

```json
{"records":[],"page":1,"pageSize":20,"total":0}
```

## 2. 接口总览

| 模块 | 方法 | 路径 | 认证 | 返回 data 类型 |
|---|---|---|---|---|
| 健康 | GET | `/health` | 否 | `String` |
| 健康 | GET | `/health/error` | 否 | 始终返回错误 |
| 数据检查 | GET | `/admission/formal-readiness` | 否 | `FormalAdmissionReport` |
| 认证 | POST | `/auth/register` | 否 | `RegisterResponse` |
| 认证 | POST | `/auth/login` | 否 | `LoginResponse` |
| 认证 | POST | `/auth/logout` | 是 | `null` |
| 用户 | GET | `/users/me` | 是 | `CurrentUserResponse` |
| 用户 | GET | `/me/active-session` | 是 | `ActiveSessionResponse` |
| 角色 | GET | `/characters` | 否 | `Character[]` |
| 房间 | POST | `/rooms` | 是 | `CreateRoomResponse` |
| 房间 | GET | `/rooms` | 否 | `Page<RoomListItem>` |
| 房间 | GET | `/rooms/{roomId}` | 否 | `RoomDetailResponse` |
| 房间 | POST | `/rooms/{roomId}/join` | 是 | `JoinRoomResponse` |
| 房间 | POST | `/rooms/{roomId}/leave` | 是 | `LeaveRoomResponse`；等待阶段房主离开会删除房间 |
| 房间 | POST | `/rooms/{roomId}/finish-game` | 房主 | `FinishRoomGameResponse` |
| 房间 | POST | `/rooms/{roomId}/ready` | 是 | `ReadyRoomResponse` |
| 房间 | POST | `/rooms/{roomId}/unready` | 是 | `UnreadyRoomResponse` |
| 房间 | POST | `/rooms/{roomId}/start-character-selection` | 房主 | `StartCharacterSelectionResponse`（含截止时间） |
| 房间 | POST | `/rooms/{roomId}/select-character` | 是 | `SelectCharacterResponse` |
| 地图 | GET | `/maps/main` | 否 | `MapResponse` |
| 地图 | GET | `/games/{gameId}/map` | 否 | `MapResponse` |
| 地图 | GET | `/games/{gameId}/cells/{cellId}` | 是 | `CellDetailResponse` |
| 对局 | GET | `/games/{gameId}/player-view` | 是 | `PlayerViewResponse` |
| 对局 | GET | `/games/{gameId}/action-logs` | 是 | `ActionLog[]` |
| 对局 | GET | `/games/{gameId}/check-logs` | 是 | `CheckLogDetail[]` |
| 对局 | GET | `/games/{gameId}/work/current` | 是 | `CurrentWorkResponse` |
| 对局 | GET | `/games/{gameId}/shop/current` | 是 | `CurrentShopResponse` |
| 对局 | GET | `/games/{gameId}/items` | 是 | `PlayerInventoryResponse` |
| 对局 | GET | `/games/{gameId}/quests` | 是 | `PlayerQuestListResponse` |
| 对局 | GET | `/games/{gameId}/quest-offers` | 是 | `QuestOfferListResponse` |
| 对局 | GET | `/games/{gameId}/partners` | 是 | `PlayerPartnerListResponse` |
| 对局 | GET | `/games/{gameId}/available-actions` | 是 | `AvailableAction[]` |
| 对局 | GET | `/games/{gameId}/players/{playerId}/movable-cells` | 是 | `MapCell[]` |
| 对局 | POST | `/games/{gameId}/players/{playerId}/connection` | 是 | `PlayerViewResponse` |
| 对局 | POST | `/games/{gameId}/actions` | 是 | `GameActionResponse` |
| Boss | GET | `/games/{gameId}/bosses` | 是 | `GameBossListResponse` |

## 3. 健康与数据检查

### 3.1 健康检查

```http
GET /api/v1/health
```

```json
{"code":1,"msg":"success","errorCode":null,"data":"EquestriaStar 后端启动成功"}
```

### 3.2 异常响应测试

```http
GET /api/v1/health/error
```

该接口主动抛出“不是当前行动玩家”的业务异常，用于验证全局异常处理。

```json
{"code":0,"msg":"当前不是你的回合","errorCode":"GAME_NOT_CURRENT_PLAYER","data":null}
```

### 3.3 正式数据完整性检查

```http
GET /api/v1/admission/formal-readiness
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{
    "ready":true,
    "summaries":[{"resourceType":"ITEM","totalCount":20,"readyCount":20,"blockedCount":0,"warningCount":0}],
    "entries":[{
      "resourceType":"ITEM","resourceId":1,"resourceCode":"ITEM_001","resourceName":"示例道具",
      "deckType":"ITEM","status":"READY","ready":true,"blockers":[],"warnings":[]
    }]
  }
}
```

## 4. 认证与用户

### 4.1 注册

```http
POST /api/v1/auth/register
Content-Type: application/json

{"username":"xiaoming","password":"123456","nickname":"小明"}
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"userId":1,"username":"xiaoming","nickname":"小明"}}
```

### 4.2 登录

```http
POST /api/v1/auth/login
Content-Type: application/json

{"username":"xiaoming","password":"123456"}
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{
    "accessToken":"access-token-value","refreshToken":"refresh-token-value","expiresIn":7200,
    "user":{"userId":1,"username":"xiaoming","nickname":"小明","avatarUrl":null}
  }
}
```

### 4.3 退出登录

```http
POST /api/v1/auth/logout
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":null}
```

### 4.4 当前用户

```http
GET /api/v1/users/me
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"userId":1,"username":"xiaoming","nickname":"小明","avatarUrl":null,"createdAt":"2026-09-23T10:00:00"}
}
```

### 4.5 查询当前有效房间或对局

登录完成后、客户端重启恢复有效令牌后，首先调用：

```http
GET /api/v1/me/active-session
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,
  "msg":"success",
  "errorCode":null,
  "data":{
    "phase":"IN_GAME",
    "roomId":100,
    "gameId":200,
    "memberRole":"MEMBER",
    "readyStatus":"READY",
    "selectedCharacterId":101,
    "characterSelectionDeadline":null
  }
}
```

固定状态契约：

| phase | roomId | gameId | 跳转 |
|---|---:|---:|---|
| `NONE` | null | null | 大厅 |
| `WAITING` | 有值 | null | 房间等待页，不重新 join |
| `CHARACTER_SELECTING` | 有值 | null | 选角页，并使用 characterSelectionDeadline |
| `IN_GAME` | 有值 | 有值 | 对局页，然后获取 player-view |

判断依据为未离开的 `room_member`、当前 `room.status/current_game_id`、运行中的 `game_session` 以及该用户在当前局中的 `game_player`。历史对局不能用于恢复。房间删除或玩家主动离开后返回 `NONE`；对局结束并恢复房间后返回 `WAITING`；关闭客户端和网络掉线不会删除成员。

认证错误：`AUTH_REQUIRED`、`AUTH_TOKEN_EXPIRED`、`USER_NOT_FOUND`。数据异常：`ACTIVE_SESSION_DATA_CONFLICT`，表示同一用户存在多个有效房间，或游戏中归属缺少对应运行对局/玩家记录。

## 5. 角色

### 5.1 查询可用角色

```http
GET /api/v1/characters
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":[{
    "characterId":1,"code":"TWILIGHT","name":"Twilight","characterType":"INITIAL","title":"示例称号",
    "attrStrength":2,"attrSpeed":3,"attrSocial":3,"attrCharm":2,"attrMagic":4,"attrMind":4,
    "description":"角色说明","background":"角色背景","skillName":"技能名","skillText":"技能说明","skillEffectId":"SKILL_EFFECT"
  }]
}
```

## 6. 房间

### 6.1 创建房间

`roomName` 必填且不超过 128 字符；`maxPlayers` 可省略，默认 4，范围 2～6。

```http
POST /api/v1/rooms
Authorization: Bearer <accessToken>
Content-Type: application/json

{"roomName":"测试房间","maxPlayers":4}
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"roomId":10,"roomName":"测试房间","ownerUserId":1,"status":"WAITING","maxPlayers":4,"currentPlayers":1,"createdAt":"2026-09-23T10:10:00"}
}
```

### 6.2 查询房间列表

```http
GET /api/v1/rooms?status=WAITING&page=1&pageSize=20
```

`status` 可省略；`page` 默认 1；`pageSize` 默认 20、最大 100。

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"records":[{"roomId":10,"roomName":"测试房间","ownerNickname":"小明","status":"WAITING","currentPlayers":1,"maxPlayers":4}],"page":1,"pageSize":20,"total":1}
}
```

### 6.3 查询房间详情

```http
GET /api/v1/rooms/10
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{
    "roomId":10,"roomName":"测试房间","ownerUserId":1,"ownerNickname":"小明","status":"WAITING",
    "currentPlayers":1,"maxPlayers":4,"currentGameId":null,"createdAt":"2026-09-23T10:10:00",
    "members":[{
      "userId":1,"username":"xiaoming","nickname":"小明","avatarUrl":null,"seatNo":1,"memberRole":"OWNER",
      "readyStatus":"NOT_READY","selectedCharacterId":null,"selectedCharacterCode":null,"selectedCharacterName":null,
      "selectedCharacterTitle":null,"selectedCharacterSkillName":null,"selectedCharacterSkillText":null,
      "selectedCharacterSkillEffectId":null,"joinedAt":"2026-09-23T10:10:00"
    }]
  }
}
```

### 6.4 加入房间

```http
POST /api/v1/rooms/10/join
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"playerId":22,"roomId":10,"userId":2,"nickname":"小红","ready":false,"selectedCharacterId":null}}
```

此处 `playerId` 实际表示房间成员记录 ID；开始游戏后会生成新的对局玩家 ID。

### 6.5 离开房间

```http
POST /api/v1/rooms/10/leave
Authorization: Bearer <accessToken>
```

#### 普通成员离开等待中的房间

只物理删除当前成员的 `room_member` 记录，房间及房主不变：

```json
{"code":1,"msg":"success","errorCode":null,"data":{"roomId":10,"userId":2,"roomDeleted":false}}
```

#### 房主离开等待中的房间

房主不会转让给其他成员。后端在同一事务中先删除该房间的全部 `room_member` 记录，再物理删除 `room` 记录：

```json
{"code":1,"msg":"success","errorCode":null,"data":{"roomId":10,"userId":1,"roomDeleted":true}}
```

响应返回时数据库中的房间已经被删除。之后调用 `GET /api/v1/rooms/10` 会返回 `ROOM_NOT_FOUND`，房间列表也不会再出现该房间。

房间内其他客户端需要根据以下任一条件返回房间列表页：

- 轮询房间详情时收到 `ROOM_NOT_FOUND`；
- 房间同步逻辑发现原房间已经不存在；
- 当前房主客户端收到 `roomDeleted=true`。

#### 已有对局历史的房间

对局历史已经与房间、房间成员解耦。房间处于 `IN_GAME` 时不能直接离开；需先结束对局。对局结束后房间回到 `WAITING`，此时删除房间不会影响历史对局。

### 6.6 普通成员准备

```http
POST /api/v1/rooms/10/ready
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"roomId":10,"userId":2,"ready":true}
}
```

### 6.7 取消准备

```http
POST /api/v1/rooms/10/unready
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"roomId":10,"userId":2,"ready":false}}
```

### 6.8 开启角色选择

仅房主可调用。所有非房主成员准备完成、房间至少两人且可用初始角色足够时，服务端进入 `CHARACTER_SELECTING` 并持久化 30 秒后的截止时间；该阶段禁止新成员加入。

```http
POST /api/v1/rooms/10/start-character-selection
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"roomId":10,"status":"CHARACTER_SELECTING","characterSelectionDeadline":"2026-09-24T08:30:30.123456"}}
```

房间详情同步返回 `characterSelectionDeadline`，前端以此展示倒计时，刷新和重连后重新获取即可恢复。

### 6.9 确认角色

```http
POST /api/v1/rooms/10/select-character
Authorization: Bearer <accessToken>
Content-Type: application/json

{"selectedCharacterId":101}
```

每位成员（包括房主）只能为自己确认角色。角色必须存在、启用、属于初始角色且未被其他成员占用。同一角色的并发确认由房间行锁与数据库唯一约束共同保护，只有一个请求成功。截止后请求返回 `ROOM_CHARACTER_SELECTION_EXPIRED`。

### 6.10 服务端自动开局

客户端不再调用 `POST /api/v1/rooms/{roomId}/start`，该入口已移除。截止时间到达后，服务端扫描数据库中的到期房间并在一个事务内完成：

1. 锁定房间并重新读取当前成员和已确认角色。
2. 人数不足 2 人时回退到 `WAITING`，清理本轮准备和选角数据。
3. 从剩余可用初始角色中为未选择成员随机分配不重复角色。
4. 创建唯一的 `GameSession`、全部 `GamePlayer`、开局牌堆与商店数据。
5. 将房间更新为 `IN_GAME` 并写入 `currentGameId`。

应用重启后仍会继续处理数据库中已到期的房间；多实例和重复扫描通过事务房间锁及状态条件保证不会创建第二局。某一步失败时事务整体回滚，过期房间保留供下次扫描重试。一个异常房间不会阻塞其他房间。

客户端轮询房间详情：`status=IN_GAME` 且 `currentGameId` 非空后，所有玩家进入该对局。网络掉线不会删除成员，房主掉线也不影响自动开局。主动离房仍遵循房间规则；房主删除房间后，扫描任务安全跳过。

### 6.11 房主主动结束对局

```http
POST /api/v1/rooms/10/finish-game
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"roomId":10,"gameId":100,"operatorUserId":1,"roomStatus":"WAITING","gameStatus":"FINISHED","playerFacingMessage":"房主已结束本局游戏，房间已恢复为等待状态。"}}
```

## 7. 地图

### 7.1 主地图

```http
GET /api/v1/maps/main
```

### 7.2 对局地图

```http
GET /api/v1/games/100/map
```

以上两个接口使用相同响应结构：

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{
    "mapId":1,"mapName":"Main Map",
    "cells":[{"cellId":1,"cellCode":"START","cellName":"Start","cellNameText":"起点","q":0,"r":0,"interactionTags":["START"]}],
    "edges":[{"edgeId":1,"fromCellId":1,"toCellId":2,"edgeType":"NORMAL","edgeTypeText":"普通道路","passable":true,"requiredAbility":null,"requiredAbilityText":null}]
  }
}
```

### 7.3 格子详情

```http
GET /api/v1/games/100/cells/1
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"cellId":1,"cellCode":"START","cellName":"Start","cellNameText":"起点","q":0,"r":0,"interactionTags":["START"],"playersOnCell":[1001]}}
```

## 8. 对局读取接口

### 8.1 玩家视图

```http
GET /api/v1/games/100/player-view
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{
    "gameId":100,"roomId":10,"mapConfigId":1,"gameStatus":"RUNNING","gameStatusText":"进行中","currentRound":1,
    "currentTurnUserId":1,"currentTurnSeq":1,"stateVersion":1,"winnerUserId":null,
    "players":[{
      "playerId":1001,"userId":1,"initialCharacterId":1,"initialCharacterCode":"TWILIGHT","initialCharacterName":"Twilight",
      "initialCharacterTitle":"示例称号","characterSkillName":"技能名","characterSkillText":"技能说明","characterSkillEffectId":"SKILL_EFFECT",
      "turnOrder":1,"currentCellId":1,"currentCellCode":"START","currentCellName":"Start","currentCellNameText":"起点",
      "coins":0,"prestige":0,"equivalentPrestige":0,"currentSpeedPoints":3,"currentSocialPoints":3,"isConnected":1
    }]
  }
}
```

### 8.2 行动日志

```http
GET /api/v1/games/100/action-logs?limit=20
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":[{"logId":1,"gameId":100,"stateVersion":2,"playerId":1001,"playerName":"小明","actionType":"MOVE_ONE_STEP","actionTypeText":"移动一步","message":"行动完成","rawMessageText":"行动完成","rawMessage":"action completed","createdAt":"2026-09-23T10:30:00"}]}
```

### 8.3 检定日志

```http
GET /api/v1/games/100/check-logs?limit=20
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":[{"checkLogId":1,"gameId":100,"playerId":1001,"stateVersion":2,"sourceType":"WORK","sourceTypeText":"工作","dice":4,"attributeType":"SOCIAL","attributeTypeText":"社交","attributeValue":3,"modifier":0,"finalValue":7,"difficulty":6,"resultType":"CHECK_SUCCESS","resultTypeText":"检定成功","createdAt":"2026-09-23T10:31:00"}]}
```

### 8.4 当前工作

```http
GET /api/v1/games/100/work/current?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"cellId":8,"cellName":"Library","cellNameText":"图书馆","attributeType":"MIND","attributeTypeText":"心智","difficulty":6,"socialCost":1,"rewardCoins":2}}
```

### 8.5 当前商店

```http
GET /api/v1/games/100/shop/current?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"cellId":9,"cellName":"Shop","cellNameText":"商店","refreshSocialCost":1,"shelfSize":3,
    "items":[{"shelfSlotId":11,"itemId":5,"itemName":"示例道具","itemType":"ACTIVE","itemTypeText":"主动道具","itemCategory":"TOOL","buyPrice":3,"sellPrice":1,"description":"说明","itemTag":"TAG"}],
    "reservedItems":[{"reservedItemId":21,"itemId":6,"itemName":"预留道具","itemType":"ACTIVE","itemTypeText":"主动道具","itemCategory":"TOOL","buyPrice":3,"sellPrice":1,"description":"说明","itemTag":"TAG"}]}
}
```

### 8.6 玩家道具

```http
GET /api/v1/games/100/items?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"gameId":100,"playerId":1001,"totalCount":1,
    "items":[{"playerItemId":31,"itemId":5,"itemName":"示例道具","itemType":"ACTIVE","itemTypeText":"主动道具","itemCategory":"TOOL","buyPrice":3,"sellPrice":1,"description":"说明","itemTag":"TAG","location":"HAND","locationText":"手牌","createdAt":"2026-09-23T10:20:00"}],
    "pendingChoices":[]}
}
```

### 8.7 玩家任务

```http
GET /api/v1/games/100/quests?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"gameId":100,"playerId":1001,"quests":[{"playerQuestId":41,"questId":4,"questCode":"QUEST_004","questName":"示例任务","description":"任务说明","status":"ACTIVE","statusText":"进行中","progress":1,"targetCount":3,"rewardCoins":2,"rewardPrestige":1}]}}
```

任务对象会继续携带任务配置中的条件、地点、检定属性、资源消耗和奖励字段；字段是否有值取决于任务类型。

### 8.8 任务候选

```http
GET /api/v1/games/100/quest-offers?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"gameId":100,"playerId":1001,"offers":[{"offerId":51,"questId":4,"slotNo":1,"questCode":"QUEST_004","questName":"示例任务","background":"背景","description":"说明","rewardText":"奖励说明","locationText":"地点","difficultyLevel":2,"checkAttributeTypes":"SOCIAL","checkAttributeTypesText":"社交","triggerType":"MOVE_ONE_STEP","triggerTypeText":"移动","targetCount":3,"rewardCoins":2,"rewardPrestige":1,"rewardItemCount":0}]}}
```

### 8.9 玩家伙伴

```http
GET /api/v1/games/100/partners?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":{"gameId":100,"playerId":1001,"partners":[{"playerPartnerId":61,"partnerId":3,"partnerCode":"PARTNER_003","partnerName":"示例伙伴","partnerType":"NORMAL","partnerTypeText":"普通伙伴","description":"说明","attrStrength":"+1","attrSpeed":null,"attrMagic":null,"attrCharm":null,"attrMind":null,"attrSocial":null,"recruitCondition":"招募条件","skillOne":"技能一","skillTwo":"技能二","bondSkill":"羁绊技能","status":"ACTIVE","statusText":"已激活"}]}}
```

### 8.10 可用行动

```http
GET /api/v1/games/100/available-actions?playerId=1001
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":[{"actionType":"MOVE_ONE_STEP","actionTypeText":"移动一步","available":true,"reason":null,"reasonText":null},{"actionType":"WORK","actionTypeText":"工作","available":false,"reason":"no work at current cell","reasonText":"当前位置没有工作"}]}
```

### 8.11 可移动格子

```http
GET /api/v1/games/100/players/1001/movable-cells
Authorization: Bearer <accessToken>
```

```json
{"code":1,"msg":"success","errorCode":null,"data":[{"cellId":2,"cellCode":"CELL_002","cellName":"Ponyville","cellNameText":"小马谷","q":1,"r":0,"interactionTags":["SHOP"]}]}
```

### 8.12 更新连接状态

```http
POST /api/v1/games/100/players/1001/connection?connected=true
Authorization: Bearer <accessToken>
```

返回内容与“玩家视图”相同，即 `Result<PlayerViewResponse>`。

## 9. Boss

### 9.1 查询本局 Boss

```http
GET /api/v1/games/100/bosses
Authorization: Bearer <accessToken>
```

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{"gameId":100,"bosses":[{
    "gameBossId":71,"bossId":1,"bossCode":"BOSS_001","bossName":"示例 Boss","releasedByPlayerId":1001,
    "sourceInvestigationCardId":null,"status":"RELEASED","statusText":"已释放","progressCount":1,"requiredProgress":3,
    "convertedPartnerId":null,"releasedAt":"2026-09-23T10:40:00","resolvedAt":null,
    "suppressionSkill":"压制技能","benefitSkill":"奖励技能","attrStrength":"5","attrSpeed":"4","attrSocial":"4",
    "attrMagic":"5","attrCharm":"3","attrMind":"4","mainQuestName":"主线任务","mainQuestDescription":"任务说明",
    "rewardDescription":"奖励说明","releaseCondition":"释放条件","suppressionEffectId":"SUPPRESS_EFFECT",
    "benefitEffectId":"BENEFIT_EFFECT","mainQuestType":"CHECK","mainQuestTypeText":"检定","defeatRewardPrestige":3,"defeatRewardCoins":2
  }]}
}
```

## 10. 统一行动接口

### 10.1 请求

```http
POST /api/v1/games/100/actions
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "playerId":1001,
  "stateVersion":1,
  "actionType":"MOVE_ONE_STEP",
  "payload":{"targetCellId":2}
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `playerId` | Long | 是 | 对局玩家 ID，不是用户 ID 或房间成员 ID |
| `stateVersion` | Long | 是 | 客户端持有的最新状态版本；过期版本会被拒绝 |
| `actionType` | String | 是 | 行动类型 |
| `payload` | Object | 按行动决定 | 行动参数；无参数时传 `{}` |

### 10.2 主要行动请求样式

| actionType | payload 示例 |
|---|---|
| `MOVE_ONE_STEP` | `{"targetCellId":2}` |
| `USE_RAILWAY` | `{"targetCellId":20}` |
| `END_TURN` | `{}` |
| `WORK` | `{}`，部分效果可能增加可选参数 |
| `SHOP_BUY_ITEM` | `{"shelfSlotId":11}` |
| `SHOP_BUY_RESERVED_ITEM` | `{"reservedItemId":21}` |
| `SHOP_SELL_ITEM` | `{"playerItemId":31}` |
| `SHOP_REFRESH` | `{}` |
| `PLAY_ITEM` | `{"playerItemId":31}`，具体道具可要求额外目标参数 |
| `EQUIP_ITEM` | `{"playerItemId":31}` |
| `USE_ITEM` | `{"playerItemId":31}`，具体道具可要求额外目标参数 |
| `UNEQUIP_ITEM` | `{"playerItemId":31}` |
| `DISCARD_ITEM` | `{"playerItemId":31}` |
| `STEAL` | 目标参数由偷窃规则决定 |
| `DRAW_PARTNER_OFFER` | `{}` |
| `RECRUIT_PARTNER_FROM_OFFER` | `{"offerId":81,"partnerCardId":91,"dismissPlayerPartnerId":null}` |
| `RECRUIT_PARTNER` | `{"partnerId":3}` |
| `USE_PARTNER_SKILL` | 伙伴技能参数由技能规则决定 |
| `USE_CHARACTER_SKILL` | 角色技能参数由技能规则决定 |
| `ACCEPT_QUEST` | `{"slotNo":1}` |
| `REFRESH_QUEST_OFFERS` | `{}` |
| `QUEST_FULL_SOCIAL_CHECK` | 任务参数由任务规则决定 |
| `QUEST_SOCIAL_CHECK` | 任务参数由任务规则决定 |
| `SUBMIT_QUEST_ITEM` | 任务及道具参数由任务规则决定 |
| `PAY_QUEST_RESOURCE` | 任务及资源参数由任务规则决定 |
| `QUEST_EXCHANGE` | 任务交换参数由任务规则决定 |
| `QUEST_DUEL` | 对手及任务参数由任务规则决定 |
| `QUEST_RELEASE_BOSS` | 任务及 Boss 参数由任务规则决定 |
| `RESOURCE_EXCHANGE` | `{"direction":"COINS_TO_PRESTIGE"}` 或规则支持的反向值 |
| `INVESTIGATE` | 调查参数由当前位置和调查规则决定 |
| `RESOLVE_INVESTIGATION_CARD` | 调查卡及选项参数由卡牌规则决定 |
| `SUBMIT_BOSS_PROGRESS` | Boss 及检定参数由 Boss 规则决定 |
| `USE_BOSS_SKILL` | Boss 技能参数由具体 Boss 决定 |
| `CLAIM_SOMBRA_CRYSTAL_HEART` | `{"targetCellId":2}` |
| `SET_CHRYSALIS_INFILTRATION` | `{"partnerIds":[61,62]}` |
| `CHRYSALIS_SWAP_ROYAL_PARTNER` | `{"targetBossPlayerPartnerId":61,"offeredPlayerPartnerId":62}` |
| `RESOLVE_BOSS_PENDING_EFFECT` | `{"pendingEffectId":101}`，具体效果可能要求 `targetCellId` 或选择值 |
| `RESOLVE_TURN_START_EFFECT` | `{"pendingEffectId":111,"decision":"ACCEPT"}`，决定值取决于效果 |

`QUEST_COMPLETED` 和 `GAME_FINISHED` 是服务端日志/结果类型，不应由普通客户端主动提交。

### 10.3 行动成功响应

```json
{
  "code":1,"msg":"success","errorCode":null,
  "data":{
    "gameId":100,"oldStateVersion":1,"newStateVersion":2,
    "actionType":"MOVE_ONE_STEP","actionTypeText":"移动一步","resultType":"NORMAL_MOVE","resultTypeText":"普通移动",
    "movement":{"fromCellId":1,"fromCellCode":"START","fromCellName":"Start","requestedTargetCellId":2,"requestedTargetCellCode":"CELL_002","requestedTargetCellName":"Ponyville","actualTargetCellId":2,"actualTargetCellCode":"CELL_002","actualTargetCellName":"Ponyville","costSpeed":1,"remainingSpeed":2},
    "work":null,"shopBuy":null,"shopRefresh":null,"itemUse":null,"itemSell":null,"partnerOffer":null,"partnerRecruit":null,
    "questCheck":null,"investigation":null,"bossProgress":null,
    "questUpdates":[],
    "animations":[{"type":"MOVE","typeText":"移动","playerId":1001,"fromCellId":1,"toCellId":2}],
    "logs":["action completed"],"playerFacingLogs":["行动完成"],"playerFacingMessage":"行动完成",
    "playerViewState":{"gameId":100,"roomId":10,"mapConfigId":1,"gameStatus":"RUNNING","gameStatusText":"进行中","currentRound":1,"currentTurnUserId":1,"currentTurnSeq":1,"stateVersion":2,"winnerUserId":null,"players":[]},
    "nextAvailableActions":[{"actionType":"MOVE_ONE_STEP","actionTypeText":"移动一步","available":true,"reason":null,"reasonText":null}]
  }
}
```

`GameActionResponse` 是联合响应：只有当前行动相关的结果对象非空。

| 字段 | 对应行动结果 |
|---|---|
| `movement` | 移动或铁路 |
| `work` | 工作：社交消耗、剩余社交、金币奖励、检定和是否强制结束回合 |
| `shopBuy` | 购买结果、剩余金币和补充到货架的道具 |
| `shopRefresh` | 刷新消耗、剩余社交、货架和消息 |
| `itemUse` | 道具位置变化、属性变化和可能发生的移动 |
| `itemSell` | 出售道具、售价、剩余金币和声望 |
| `partnerOffer` | 伙伴候选结果 |
| `partnerRecruit` | 伙伴招募结果 |
| `questCheck` | 任务检定结果 |
| `investigation` | 调查卡及处理结果 |
| `bossProgress` | Boss 进度、检定、奖励和效果日志 |
| `questUpdates` | 此行动触发的任务进度与奖励 |
| `playerViewState` | 行动后的完整对局视图 |
| `nextAvailableActions` | 行动后的合法操作列表 |

## 11. 常用状态值

- 房间：`WAITING`、`CHARACTER_SELECTING`、`IN_GAME`
- 房间角色：`OWNER`、`MEMBER`
- 准备：`READY`、`NOT_READY`
- 对局：`RUNNING`、`FINISHED`
- `stateVersion`：每次改变对局状态后递增。客户端应始终使用最近一次响应中的版本。

## 12. 推荐调用流程

```text
POST /auth/register 或 POST /auth/login
GET  /me/active-session（按 phase 恢复大厅、房间、选角或对局）
GET  /characters
POST /rooms 或 POST /rooms/{roomId}/join
POST /rooms/{roomId}/ready（仅普通成员，无请求体）
POST /rooms/{roomId}/start-character-selection（房主）
POST /rooms/{roomId}/select-character
客户端根据 characterSelectionDeadline 展示倒计时并轮询房间详情
服务端到期后自动补齐角色并创建对局
房间变为 IN_GAME 且 currentGameId 非空后进入游戏
GET  /games/{gameId}/player-view
GET  /games/{gameId}/available-actions?playerId=...
POST /games/{gameId}/actions
重复查询可用行动并提交行动
对局结束后房间自动回到 WAITING，可重复上述流程开启下一局
POST /rooms/{roomId}/leave
```

## 13. 源码依据

接口以以下 Controller 为准：

- `admission/controller/FormalAdmissionController.java`
- `auth/controller/AuthController.java`
- `boss/controller/BossController.java`
- `character/controller/CharacterController.java`
- `common/health/HealthController.java`
- `game/controller/GameController.java`
- `map/controller/MapController.java`
- `room/controller/RoomController.java`
- `user/controller/UserController.java`

请求和响应字段以各模块的 `dto`、`vo` 类为准。
