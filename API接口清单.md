# ES 项目 API 接口清单

## 基础信息

本地服务基础地址：

```text
http://localhost:8080/api/v1
```

需要登录的接口应携带访问令牌：

```http
Authorization: Bearer <accessToken>
```

统一响应格式：

```json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {}
}
```

## 1. 基础接口

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/health` | 健康检查 |
| `GET` | `/health/error` | 测试异常处理 |
| `GET` | `/admission/formal-readiness` | 检查正式游戏数据是否准备完整 |

## 2. 认证接口

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| `POST` | `/auth/register` | 否 | 注册用户 |
| `POST` | `/auth/login` | 否 | 登录并获取 Token |
| `POST` | `/auth/logout` | 是 | 退出登录 |

注册请求：

```json
{
  "username": "xiaoming",
  "password": "123456",
  "nickname": "小明"
}
```

登录请求：

```json
{
  "username": "xiaoming",
  "password": "123456"
}
```

## 3. 用户与角色接口

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| `GET` | `/users/me` | 是 | 查询当前登录用户 |
| `GET` | `/me/active-session` | 是 | 查询当前有效房间或对局，用于登录和重启恢复 |
| `GET` | `/characters` | 否 | 查询可用角色列表 |

## 3.1 登录后恢复定位

```http
GET /api/v1/me/active-session
Authorization: Bearer <accessToken>
```

返回字段：`phase`、`roomId`、`gameId`、`memberRole`、`readyStatus`、`selectedCharacterId`、`characterSelectionDeadline`。

- `NONE`：进入大厅。
- `WAITING`：根据 roomId 回到房间，不要重新 join。
- `CHARACTER_SELECTING`：根据 roomId 回到选角页，使用服务端截止时间。
- `IN_GAME`：根据 gameId 进入对局并获取 player-view。

接口只信任服务端当前数据，不使用客户端缓存的 roomId，不从历史对局恢复。错误码包括 `AUTH_REQUIRED`、`AUTH_TOKEN_EXPIRED`、`USER_NOT_FOUND`、`ACTIVE_SESSION_DATA_CONFLICT`。

## 4. 房间接口

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| `POST` | `/rooms` | 是 | 创建房间 |
| `GET` | `/rooms` | 否 | 分页查询房间 |
| `GET` | `/rooms/{roomId}` | 否 | 查询房间详情 |
| `POST` | `/rooms/{roomId}/join` | 是 | 加入房间 |
| `POST` | `/rooms/{roomId}/leave` | 是 | 离开房间；WAITING/CHARACTER_SELECTING 阶段房主离开会删除整个房间 |
| `POST` | `/rooms/{roomId}/ready` | 普通成员 | 准备，无请求体，不再提交角色 |
| `POST` | `/rooms/{roomId}/unready` | 是 | 取消准备 |
| `POST` | `/rooms/{roomId}/start-character-selection` | 房主 | 进入 30 秒服务端选角阶段，返回截止时间 |
| `POST` | `/rooms/{roomId}/select-character` | 是 | 截止前为自己确认初始角色 |
| `POST` | `/rooms/{roomId}/finish-game` | 房主 | 主动结束对局 |

创建房间：

```json
{
  "roomName": "测试房间",
  "maxPlayers": 4
}
```

查询房间：

```http
GET /api/v1/rooms?status=WAITING&page=1&pageSize=20
```

准备请求没有请求体。角色选择使用独立接口：

```json
{
  "selectedCharacterId": 3
}
```

离开房间：

```http
POST /api/v1/rooms/10/leave
Authorization: Bearer <accessToken>
```

普通成员离开等待中的房间时，只删除该成员，房间继续存在：

```json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {
    "roomId": 10,
    "userId": 2,
    "roomDeleted": false
  }
}
```

房主离开尚未开始游戏的房间时，不再转让房主。后端会删除该房间的全部成员，再物理删除房间：

```json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {
    "roomId": 10,
    "userId": 1,
    "roomDeleted": true
  }
}
```

其他客户端应在轮询房间详情得到 `ROOM_NOT_FOUND` 后返回房间列表页。房间删除不会删除已经结束的对局历史。`IN_GAME` 阶段禁止通过房间离开接口退出。

房间状态仅有 `WAITING`、`CHARACTER_SELECTING`、`IN_GAME`。对局结束后房间回到 `WAITING`，清空 `currentGameId`、准备状态和选角，同一房间可继续开启下一局。

服务端选角与自动开局流程：

1. 房主调用 `POST /rooms/{roomId}/start-character-selection`，服务端保存 30 秒后的 `characterSelectionDeadline`。
2. 所有成员在截止前调用 `POST /rooms/{roomId}/select-character` 为自己确认角色；同一角色并发选择只有一人成功。
3. 截止后服务端自动为未选玩家随机分配剩余角色并创建对局，无需再调用 `/start`。
4. 客户端轮询房间详情；仅在 `status=IN_GAME` 且 `currentGameId` 非空时进入游戏。
5. 网络掉线不移除成员；选角期主动离房导致不足两人时回到 `WAITING`；房主主动离房则删除房间。

`start-character-selection` 成功响应示例：

```json
{"code":1,"msg":"success","errorCode":null,"data":{"roomId":10,"status":"CHARACTER_SELECTING","characterSelectionDeadline":"2026-09-24T08:30:30.123456"}}
```

## 5. 地图接口

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| `GET` | `/maps/main` | 否 | 查询主地图 |
| `GET` | `/games/{gameId}/map` | 否 | 查询指定对局地图 |
| `GET` | `/games/{gameId}/cells/{cellId}` | 是 | 查询对局中的格子详情 |

地图由格子 `MapCell` 和连接边 `MapEdge` 组成。

## 6. 对局查询接口

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| `GET` | `/games/{gameId}/player-view` | 是 | 获取完整玩家视图 |
| `GET` | `/games/{gameId}/action-logs` | 是 | 查询行动日志 |
| `GET` | `/games/{gameId}/check-logs` | 是 | 查询检定日志 |
| `GET` | `/games/{gameId}/available-actions?playerId={id}` | 是 | 查询当前可执行行动 |
| `GET` | `/games/{gameId}/players/{playerId}/movable-cells` | 是 | 查询可移动格子 |

日志接口支持限制返回条数：

```http
GET /api/v1/games/100/action-logs?limit=20
GET /api/v1/games/100/check-logs?limit=20
```

## 7. 玩法状态接口

| 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|
| `GET` | `/games/{gameId}/work/current?playerId={id}` | 是 | 查询当前位置工作 |
| `GET` | `/games/{gameId}/shop/current?playerId={id}` | 是 | 查询当前位置商店 |
| `GET` | `/games/{gameId}/items?playerId={id}` | 是 | 查询玩家道具 |
| `GET` | `/games/{gameId}/quests?playerId={id}` | 是 | 查询玩家任务 |
| `GET` | `/games/{gameId}/quest-offers?playerId={id}` | 是 | 查询可领取任务 |
| `GET` | `/games/{gameId}/partners?playerId={id}` | 是 | 查询玩家伙伴 |
| `GET` | `/games/{gameId}/bosses` | 是 | 查询本局 Boss |

这些接口负责读取玩法状态。改变游戏状态的操作统一通过行动接口提交。

## 8. 玩家连接接口

```http
POST /api/v1/games/{gameId}/players/{playerId}/connection?connected=true
Authorization: Bearer <accessToken>
```

| `connected` | 含义 |
|---|---|
| `true` | 玩家已连接 |
| `false` | 玩家已断开 |

该接口返回更新后的玩家视图。

## 9. 统一行动接口

```http
POST /api/v1/games/{gameId}/actions
Authorization: Bearer <accessToken>
Content-Type: application/json
```

请求格式：

```json
{
  "playerId": 1001,
  "stateVersion": 1,
  "actionType": "MOVE_ONE_STEP",
  "payload": {
    "targetCellId": 25
  }
}
```

| 字段 | 说明 |
|---|---|
| `playerId` | 执行动作的对局玩家 ID |
| `stateVersion` | 客户端当前持有的对局状态版本 |
| `actionType` | 动作类型 |
| `payload` | 不同动作需要的参数 |

### 主要行动类型

| 类型 | 说明 |
|---|---|
| `MOVE_ONE_STEP` | 移动一步 |
| `USE_RAILWAY` | 使用铁路 |
| `END_TURN` | 结束回合 |
| `WORK` | 工作 |
| `SHOP_BUY_ITEM` | 购买道具 |
| `SHOP_SELL_ITEM` | 出售道具 |
| `SHOP_REFRESH` | 刷新商店 |
| `PLAY_ITEM` | 打出道具 |
| `EQUIP_ITEM` | 装备道具 |
| `USE_ITEM` | 使用道具 |
| `UNEQUIP_ITEM` | 卸下道具 |
| `DISCARD_ITEM` | 丢弃道具 |
| `STEAL` | 偷窃 |
| `USE_CHARACTER_SKILL` | 使用角色技能 |
| `USE_PARTNER_SKILL` | 使用伙伴技能 |
| `DRAW_PARTNER_OFFER` | 抽取伙伴候选 |
| `RECRUIT_PARTNER_FROM_OFFER` | 招募候选伙伴 |
| `RECRUIT_PARTNER` | 招募伙伴 |
| `ACCEPT_QUEST` | 接受任务 |
| `REFRESH_QUEST_OFFERS` | 刷新任务 |
| `RESOURCE_EXCHANGE` | 交换资源 |
| `INVESTIGATE` | 调查 |
| `RESOLVE_INVESTIGATION_CARD` | 处理调查卡 |
| `SUBMIT_BOSS_PROGRESS` | 提交 Boss 进度 |
| `USE_BOSS_SKILL` | 使用 Boss 相关技能 |
| `RESOLVE_TURN_START_EFFECT` | 处理回合开始效果 |
| `RESOLVE_BOSS_PENDING_EFFECT` | 处理 Boss 待定效果 |

不同 `actionType` 使用不同的 `payload`，但都通过这一接口提交。

## 10. 常见调用顺序

```text
注册或登录
  -> 查询角色
  -> 创建或加入房间
  -> 普通成员准备（无请求体）
  -> 房主开启角色选择
  -> 玩家选择角色
  -> 倒计时结束后，房主前端刷新房间详情并为未选玩家随机分配
  -> 房主提交完整 userId 到 selectedCharacterId 映射并正式开始游戏
  -> 获取玩家视图
  -> 查询可用行动
  -> 提交行动
  -> 获取最新玩家视图
  -> 对局结束后房间恢复 WAITING，可继续下一局或离开
```

## 11. Controller 源码位置

- `src/main/java/com/ariaplume/es/admission/controller/FormalAdmissionController.java`
- `src/main/java/com/ariaplume/es/auth/controller/AuthController.java`
- `src/main/java/com/ariaplume/es/boss/controller/BossController.java`
- `src/main/java/com/ariaplume/es/character/controller/CharacterController.java`
- `src/main/java/com/ariaplume/es/common/health/HealthController.java`
- `src/main/java/com/ariaplume/es/game/controller/GameController.java`
- `src/main/java/com/ariaplume/es/map/controller/MapController.java`
- `src/main/java/com/ariaplume/es/room/controller/RoomController.java`
- `src/main/java/com/ariaplume/es/user/controller/UserController.java`
