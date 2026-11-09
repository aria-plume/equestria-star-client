# ES 后端完整 API 文档

## 2026-11-04 普通任务主动提交（已部署，客户端待联调）

接口文档已统一迁入本目录，入口为[交接索引](README.md)。本次客户端接入范围和准确请求/回执见[道具系统与任务详情](道具系统与任务详情接入交接.md)。根据2026-11-04正式部署报告，已完成生成列/唯一索引增量并上线候选1b20c8d0，新鲜公网视图确认questSubmissionVersion=1及movementRulesVersion=2；实际道具操作、任务领奖与Godot UI不因此视为已验收。

EVENT_V2已开放普通任务改为 `IN_PROGRESS → READY_TO_SUBMIT → COMPLETED`；达标只锁定目标和奖励，仍占栏，不自动发奖或抽奖励卡。
使用 `SUBMIT_QUEST`，payload仅 `{ "playerQuestId": 任务实例ID }`，沿用actions的requestId/playerId/stateVersion。只允许本人MIDDLE且无待骰/奖励阻塞；END达标下一个人回合可提交。
独立能力字段为 `player-view.data.engineState.questSubmissionVersion=1`，不能以移动版本判断。旧对局适用，不改历史回执。
查询与questUpdates新增 `submission`（达成回合、最早提交回合、canSubmit、稳定原因、冻结预览与已付奖励）；提交响应新增 `questSettlement` 实际收益和余额。
结算页确认仅打开已存在的FIFO pendingReward，不再次提交；未领取奖励不能显示为已获得道具。
本节覆盖下文对EVENT_V2普通任务“达标立即/END自动领奖”的旧说明，不改变其他关闭系统。
详细DTO、错误、迁移与结算页流程见[本批正式交接](普通任务主动提交与结算页面交接.md)。Godot源码未修改。


更新时间：2026-11-05。本文是本客户端目录当前接口契约的主文档，替代旧版房间、连接、行动和投骰示例。精简入口见 [API接口清单](API接口清单.md)，接入顺序见 [前端接口文档](小马国之星_前端接口文档.md)。

> 铁路/森林移动v2已于2026-11-03部署，并保留在11月4日任务提交版本内。最终契约见 [专项第8节](铁路与森林移动_待开发接口交接.md#8-最终接口契约)。文件名保留兼容链接，不代表仍待开发。实际接入仍读取新鲜player-view.engineState.movementRulesVersion=2，不能靠CORE_9名或HTTP200推断。

### 移动v2定稿增量

- movable-cells增加可选actionType：省略/MOVE_ONE_STEP普通一步，USE_RAILWAY直接邻站，未知PARAM_INVALID；data仍为数组，目标含costSpeed/costCoins/requiresRoll（普通地图不加）。
- 普通目标与提交均排除STATION_ROUTE；专用铁路逐站扣1金币+1行动值，免票停用。
- MAZE出发时MOVE_ONE_STEP准备FOREST_MOVE，先扣1行动值、不改位置；pendingCheck.preparation持久化出发/目标/QR方向/费用，属性、修正和难度为null。不使用草案movementContext字段。
- pending/reroll/roll路径不变；甜贝儿与固定骰适用，最终2～5直行、1左偏、6右偏。无合法偏移则STAYED，仍付1行动值，不生成到达或属性失败。
- movement新增costCoins/coinsAfter/movementOutcome，stayReason仅原地时提供；历史回执缺失新字段仍省略。森林roll返回movement、checkResult=null；非移动roll省略movement。费用是本次票价，余额包含任务奖励，code=1不代表发生位移。
- 原请求网络重试保持路径、requestId及完整原body（含旧stateVersion），不能把旧回执覆盖到更高版本状态。现有测试局无需重建；无新增DDL或公共配置写入，不改Godot源码。

## 1. 版本、地址和使用边界

- 服务地址：`http://182.92.234.193:8080`；API 前缀：`/api/v1`。下文接口表的路径均包含完整前缀。
- 当前新局：`EVENT_V2 / CORE_9_PUBLIC_SHOP`；地图为公共正式图1，不生成静态地图副本。其他卡牌资源仍按局冻结。
- 2026-10-31 已发布；2026-11-02 本次只读请求健康、主地图和 formal-readiness 成功，确认当前默认批次及地图138格／385边。未创建账号、房间或对局，未提交心跳、行动或掷骰。
- 本次前端接入：基础道具背包/详情及装备、使用、卸下、丢弃、出售，个人任务详情/逐项目标/主动提交与结算；公共商店、奖励、投骰、心跳及已完成移动闭环复用。**优先级不是服务端封禁清单**，不新增旧特殊道具或其他卡牌。
- 后端本批仍保留铁路、打工、伙伴、装备／使用／卸下／丢弃道具及部分角色主动技能；客户端不要因暂未接入而声称接口不存在。实际执行资格还受本局准入、回合、资源、地点和待办限制。
- 调查／宝藏玩法、Boss、偷窃和旧引擎运行路径仍不在本批开放范围；074未开放。Boss控制器虽存在，本批请求会返回 `GAME_RESOURCE_NOT_ADMITTED`。
- 示例中的账号、对局、玩家、卡牌实例和版本仅说明结构，不是可直接使用的测试数据。不要复用历史 gameId/playerId。
- 文件用途：本文为完整契约；`API接口清单.md` 为索引；`小马国之星_前端接口文档.md` 为接入指南；`Godot_任务主动掷骰接入说明.md` 为骰子专项。其他旧交接文件只保留历史及设计背景。

## 2. 通用约定

### 2.1 请求与响应

JSON 使用 UTF-8；需要认证的接口发送 `Authorization: Bearer <accessToken>`。有 JSON 请求体时发送 `Content-Type: application/json`；标为“无”的请求不构造旧业务请求体。

~~~json
{"code":1,"msg":"success","errorCode":null,"data":{}}
~~~

~~~json
{"code":0,"msg":"错误说明","errorCode":"GAME_STATE_VERSION_EXPIRED","data":null}
~~~

HTTP 200 不等于业务成功，必须检查 `code==1`；HTTP非成功、连接超时和无法解析响应也要独立处理。成功 `data` 可以是对象、数组、字符串或 `null`，不能全部按对象解析。下文除明确给出外层包装外，JSON样例均为 `data` 或指定子对象的结构。

分页为 `{"records":[],"page":1,"pageSize":20,"total":0}`。Long标识不要与数组下标或卡面编号混用。时间通常为无偏移的 ISO LocalDateTime；心跳回执额外给出服务端时区与UTC偏移，倒计时不信任客户端本地时钟。

### 2.2 身份、请求编号和版本

| 字段 | 含义 |
|---|---|
| userId | 登录账号ID |
| roomId | 房间ID，可连续开启多局 |
| 加入房间响应的 playerId | 历史命名，实际为房间成员记录ID；**不是局内玩家ID** |
| gameId | 一场对局ID，从房间 currentGameId 或 active-session 获取 |
| 局内 playerId | 从 player-view.players 中按当前 userId 匹配，之后用于连接及局内操作 |
| questId / itemId / partnerId | 配置身份；来自API，不把任务卡面编号直接当数据库主键 |
| playerQuestId / playerItemId / playerPartnerId | 本局一次实际任务／道具／伙伴实例，不能用配置ID代替 |
| shelfSlotId / rewardId / cardId / checkId | 货架记录／FIFO待办／候选实卡／判定ID，分别取对应响应 |
| stateVersion | 服务端对局状态版本，不是回合号，也不是心跳序号 |
| authorizationRound | 当前判定的投骰授权轮次，不是 stateVersion；重投授权成功后更新 |

CORE_9 的 `/actions`、`/roll`、`/reroll` 必须传 `requestId`，建议每次新业务操作生成UUID。网络重试保持同一 requestId、同一路径和完整原请求体（包括旧版本）；首次完成后重放首次结果。`REQUEST_IN_PROGRESS` 用原请求重试；`REQUEST_ID_CONFLICT` 不盲目换ID重复扣费，应先刷新并确认是否真的是新的操作。

新操作使用最新版本与新ID。明确的版本／上下文错误后先刷新，不得改旧 requestId 的内容。普通心跳不用 requestId/stateVersion，不走行动的响应重放。历史重放和较晚到达的旧查询不能覆盖当前更高版本的界面。

## 3. 完整接口索引

共39个当前Controller路由。存在路由不代表该玩法在CORE_9开放。GET无请求体，局内接口均需当前账号为该局成员；带 playerId 的私有操作还需属于当前账号。

| 方法 | 完整路径 | 认证／归属 | 请求 | data类型 | 接入分类 |
|---|---|---|---|---|---|
| GET | `/api/v1/health` | 否 | 无 | String：后端启动提示 | 诊断 |
| GET | `/api/v1/health/error` | 否 | 无 | 故障测试错误响应 | 诊断，不用于玩法 |
| GET | `/api/v1/admission/formal-readiness` | 否 | 无 | FormalAdmissionReport：ready/summaries/entries/runtimeAdmission | 诊断，只读 |
| POST | `/api/v1/auth/register` | 否 | JSON username/password/nickname | RegisterResponse | 必需 |
| POST | `/api/v1/auth/login` | 否 | JSON username/password | LoginResponse | 必需 |
| POST | `/api/v1/auth/logout` | 是 | 无 | null | 必需 |
| GET | `/api/v1/users/me` | 是 | 无 | CurrentUserResponse | 必需 |
| GET | `/api/v1/me/active-session` | 是 | 无 | ActiveSessionResponse | 必需 |
| GET | `/api/v1/characters` | 否 | 无 | CharacterConfigVO[] | 必需 |
| POST | `/api/v1/rooms` | 是 | JSON roomName/maxPlayers可选 | CreateRoomResponse | 必需 |
| GET | `/api/v1/rooms` | 否 | query status/page/pageSize均可选 | PageResponse<RoomListItemVO> | 必需 |
| GET | `/api/v1/rooms/{roomId}` | 否 | 无 | RoomDetailResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/join` | 是 | 无 | JoinRoomResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/leave` | 是 | 无 | LeaveRoomResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/ready` | 普通成员 | 无 | ReadyRoomResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/unready` | 普通成员 | 无 | UnreadyRoomResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/start-character-selection` | 房主 | 无 | StartCharacterSelectionResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/select-character` | 成员含房主 | JSON selectedCharacterId | SelectCharacterResponse | 必需 |
| POST | `/api/v1/rooms/{roomId}/finish-game` | 房主 | 无 | FinishRoomGameResponse | 管理，非正常胜利替代 |
| GET | `/api/v1/maps/main` | 否 | 无 | MapResponse | 优先 |
| GET | `/api/v1/games/{gameId}/map` | 本局成员 | 无 | MapResponse | 优先 |
| GET | `/api/v1/games/{gameId}/cells/{cellId}` | 本局成员 | 无 | CellDetailResponse | 优先 |
| GET | `/api/v1/games/{gameId}/player-view` | 本局成员 | 无 | PlayerViewResponse | 必需 |
| GET | `/api/v1/games/{gameId}/action-logs` | 本局成员 | query limit可选 | ActionLogVO[] | 日志 |
| GET | `/api/v1/games/{gameId}/check-logs` | 本局成员 | query limit可选 | CheckLogDetailVO[] | 日志 |
| GET | `/api/v1/games/{gameId}/work/current` | 本局自己的玩家 | query playerId必填 | CurrentWorkResponse或null | 暂不优先 |
| GET | `/api/v1/games/{gameId}/shop/current` | 本局自己的玩家 | query playerId必填 | CurrentShopResponse | 优先 |
| GET | `/api/v1/games/{gameId}/items` | 本局自己的玩家 | query playerId必填 | PlayerInventoryResponse | 本次接入背包/道具详情 |
| GET | `/api/v1/games/{gameId}/quests` | 本局自己的玩家 | query playerId必填 | PlayerQuestListResponse | 优先 |
| GET | `/api/v1/games/{gameId}/quest-offers` | 本局自己的玩家 | query playerId必填 | QuestOfferListResponse | 优先 |
| GET | `/api/v1/games/{gameId}/partners` | 本局自己的玩家 | query playerId必填 | PlayerPartnerListResponse | 暂不优先 |
| GET | `/api/v1/games/{gameId}/available-actions` | 本局自己的玩家 | query playerId必填 | AvailableActionVO[] | 必需 |
| GET | `/api/v1/games/{gameId}/players/{playerId}/movable-cells` | 本局自己的玩家 | 无 | MapCellVO[] | 优先 |
| POST | `/api/v1/games/{gameId}/players/{playerId}/connection` | 本局自己的玩家 | query connected必填；JSON clientInstanceId/connectionId见第7节 | PlayerViewResponse，回执在engineState.connection | 必需，周期心跳 |
| POST | `/api/v1/games/{gameId}/actions` | 本局自己的玩家 | JSON requestId/playerId/stateVersion/actionType/payload | GameActionResponse | 必需 |
| GET | `/api/v1/games/{gameId}/checks/pending` | 本局成员，只查自己 | 无，不传playerId | PendingCheckVO或null | 必需 |
| POST | `/api/v1/games/{gameId}/checks/{checkId}/roll` | 判定所属玩家 | JSON requestId/stateVersion/authorizationRound/dice | CheckRollResponse | 必需 |
| POST | `/api/v1/games/{gameId}/checks/{checkId}/reroll` | 判定所属玩家 | JSON requestId/stateVersion/authorizationRound | PendingCheckVO | 按资格开放 |
| GET | `/api/v1/games/{gameId}/bosses` | 本局成员 | 无 | 本批拒绝GAME_RESOURCE_NOT_ADMITTED | 关闭 |

`GET /api/v1/rooms/{roomId}`、房间列表、角色、主地图、健康、formal-readiness 不要求登录；房间写接口需要登录，指定房主接口还检查房主。`/health/error` 是故障测试入口，不是正常业务流程。没有 `/auth/refresh` 路由，不要因登录返回 refreshToken 就自行拼出刷新接口。

## 4. 认证、用户与活动位置

### 4.1 注册／登录／退出

~~~http
POST /api/v1/auth/register
Content-Type: application/json

{"username":"example_user","password":"example_password","nickname":"测试玩家"}
~~~

注册 data：`{"userId":1,"username":"example_user","nickname":"测试玩家"}`。用户名、密码和昵称的约束由服务端校验，客户端显示业务错误，不自动反复注册。

~~~http
POST /api/v1/auth/login
Content-Type: application/json

{"username":"example_user","password":"example_password"}
~~~

~~~json
{"accessToken":"<accessToken>","refreshToken":"<refreshToken>","expiresIn":7200,"user":{"userId":1,"username":"example_user","nickname":"测试玩家","avatarUrl":null}}
~~~

`POST /api/v1/auth/logout`：Bearer，无请求体，成功 `data=null`。退出登录不代替关闭局内连接，也不代表退出房间。令牌失效先恢复认证，不把401/认证失败当成心跳成功。

`GET /api/v1/users/me`：Bearer，data字段为 `userId, username, nickname, avatarUrl, createdAt`。

### 4.2 登录后必须查询活动位置

~~~http
GET /api/v1/me/active-session
Authorization: Bearer <accessToken>
~~~

~~~json
{"phase":"IN_GAME","roomId":10,"gameId":100,"memberRole":"MEMBER","readyStatus":"READY","selectedCharacterId":101,"characterSelectionDeadline":null}
~~~

| phase | 客户端处理 |
|---|---|
| NONE | 回大厅；roomId/gameId为空 |
| WAITING | 进入已有房间，不再次join |
| CHARACTER_SELECTING | 进入选角，按服务端 characterSelectionDeadline 展示倒计时 |
| IN_GAME | 读取 player-view，确定自己的局内 playerId，然后建立心跳并恢复待办／判定 |

登录、active-session、player-view、其他GET和普通行动都**不会自动标记在线或续约**。不要用历史对局或房间成员ID恢复局内玩家。

## 5. 房间、准备、选角及自动开局

### 5.1 常规请求及返回

| 接口 | 请求 | data主要字段 |
|---|---|---|
| POST /api/v1/rooms | `{"roomName":"测试房间","maxPlayers":4}` | roomId,roomName,ownerUserId,status,maxPlayers,currentPlayers,createdAt |
| GET /api/v1/rooms?status=WAITING&page=1&pageSize=20 | 无 | records,page,pageSize,total |
| GET /api/v1/rooms/{roomId} | 无 | 见下方RoomDetail |
| POST /api/v1/rooms/{roomId}/join | 无，Bearer | playerId（成员记录ID）,roomId,userId,nickname,ready,selectedCharacterId |
| POST /api/v1/rooms/{roomId}/leave | 无，Bearer | roomId,userId,roomDeleted |
| POST /api/v1/rooms/{roomId}/ready | 无，普通成员 | roomId,userId,ready=true |
| POST /api/v1/rooms/{roomId}/unready | 无，普通成员 | roomId,userId,ready=false |
| POST /api/v1/rooms/{roomId}/start-character-selection | 无，房主 | roomId,status,characterSelectionDeadline |
| POST /api/v1/rooms/{roomId}/select-character | `{"selectedCharacterId":101}` | roomId,userId,selectedCharacterId,selectedCharacterCode,selectedCharacterName |
| POST /api/v1/rooms/{roomId}/finish-game | 无，房主 | roomId,gameId,operatorUserId,roomStatus,gameStatus,playerFacingMessage |

`roomName` 非空、最长128；maxPlayers默认4、DTO范围2～6，但实际开局还要求本批可用角色足够（当前4种），不能仅据房间上限保证可开局。分页默认1/20，pageSize最大100。列表项为 `roomId,roomName,ownerNickname,status,currentPlayers,maxPlayers`。

RoomDetail结构示例：
~~~json
{
  "roomId":10,"roomName":"测试房间","ownerUserId":1,"ownerNickname":"玩家甲",
  "status":"CHARACTER_SELECTING","currentPlayers":2,"maxPlayers":4,"currentGameId":null,
  "characterSelectionDeadline":"2026-11-02T15:00:30.123456","createdAt":"2026-11-02T15:00:00",
  "members":[
    {"userId":1,"username":"example_user","nickname":"玩家甲","avatarUrl":null,"seatNo":1,
     "memberRole":"OWNER","readyStatus":"NOT_READY","selectedCharacterId":101,
     "selectedCharacterCode":"INITIAL_HONG_FANG","selectedCharacterName":"示例角色",
     "selectedCharacterTitle":null,"selectedCharacterSkillName":null,"selectedCharacterSkillText":null,
     "selectedCharacterSkillEffectId":null,"joinedAt":"2026-11-02T14:59:00"}
  ]
}
~~~

示例成员数组已截断，不用数组示例长度判断真实 currentPlayers。

### 5.2 准备到开局的固定顺序

1. WAITING时，普通成员只准备，不传角色；房主不需要准备。
2. 房主在其他成员全部准备且至少两人后调用 start-character-selection。
3. 服务端保存30秒截止时间；房间变为 CHARACTER_SELECTING，禁止新成员加入。
4. 包括房主在内，每人调用 select-character 确认自己的角色；前端从房间成员已选状态更新占用。
5. 截止后由服务端为未选成员分配剩余角色、初始化对局资源并自动开局。客户端**不调用旧 /rooms/{roomId}/start**，该路由不存在。
6. 轮询房间至 IN_GAME 且 currentGameId非空；读 player-view 得到局内playerId，立即建立心跳，不等待45秒宽限结束。
7. 所有人掉线不等于房间成员删除；房主掉线不阻止选角到期自动开局。

房间状态只有 WAITING、CHARACTER_SELECTING、IN_GAME。普通成员在允许离房阶段离开后删除成员；房主主动离房删除房间、不会转让房主。其他客户端收到 ROOM_NOT_FOUND 后回房间列表。IN_GAME不能直接leave，应先按现有规则结束对局；关闭窗口只是断连接。

正常结束或房主 finish-game 后，房间回WAITING，清空 currentGameId、准备及选角，可开下一局。对局本身的 FINISHED 与房间状态是不同概念；历史对局不随房间删除。

### 5.3 角色查询

`GET /api/v1/characters` 无需登录，返回数组。每项字段：`characterId,code,name,characterType,title,attrStrength,attrSpeed,attrSocial,attrCharm,attrMagic,attrMind,description,background,skillName,skillText,skillEffectId`。当前默认准入角色代码为 INITIAL_HONG_FANG、INITIAL_EQUATIONS、INITIAL_YE_LING、INITIAL_DAN_QING；使用实际响应的characterId提交选角，不硬编码示例101。

## 6. 地图与移动目标

| 接口 | 认证及data |
|---|---|
| GET /api/v1/maps/main | 不要求登录；MapResponse |
| GET /api/v1/games/{gameId}/map | Bearer＋本局成员；MapResponse，按本局 mapConfigId 读公共地图 |
| GET /api/v1/games/{gameId}/cells/{cellId} | Bearer＋本局成员及同图；CellDetailResponse |
| GET /api/v1/games/{gameId}/players/{playerId}/movable-cells | Bearer＋自己的playerId；MapCell数组，当前普通移动一步的目标，不是专用铁路目标列表 |

MapResponse示例（cells/edges已截断，真实正式图为138格、385边）：
~~~json
{
  "mapId":1,"mapName":"正式地图",
  "cells":[{"cellId":100010,"cellCode":"A10","boardCode":"A","cellName":"市政厅","cellNameText":"市政厅","q":1,"r":5,
            "interactionTags":["START","QUEST_BOARD","QUEST_POINT","CITY_HALL","NORMAL"],"investigationAvailable":null}],
  "edges":[{"edgeId":200001,"fromCellId":100001,"toCellId":100002,"edgeType":"NORMAL",
            "edgeTypeText":"普通道路","passable":true,"requiredAbility":null,"requiredAbilityText":null,"admitted":null}]
}
~~~

`cellId` 为主键，`cellCode` 为稳定格号；`boardCode` 已独立提供，可用于板块区分。q/r为尖顶六边形轴向整数；中文优先显示 cellNameText。cells、edges、interactionTags用数组。CellDetail字段为 `cellId,cellCode,cellName,cellNameText,q,r,interactionTags,playersOnCell`；playersOnCell为局内playerId数组，不能期待CellDetail必有boardCode。

保留真实道路类型NORMAL、GAP、STATION_ROUTE，不将GAP变成NORMAL。不根据拓扑或admitted自行放行。当前MOVE_ONE_STEP排除铁路边；USE_RAILWAY只到直接相连车站，固定1金币+1行动值，无免票。普通/铁路目标分别用movable-cells的actionType查询，森林普通移动按出发格MAZE决定方向骰，动态封锁及其他通行条件仍由后端校验。

地图未提供Godot模型路径、摄像机或板块间隙世界坐标；这些是客户端表现。地图查询、货架查询和重连都不重新初始化资源。

### 6.1 铁路与森林：已部署规则

- 铁路是独立USE_RAILWAY行动，只能从车站到直接铁路连接的下一站；每次1金币＋1行动值，不开放免票。多站行程由玩家逐站提交，不增加跨站直达或自动寻路。普通移动不得再借铁路边免费通行。
- 普通移动仍为一次请求走一步。森林方向骰由出发格是否属于森林区域决定，包括旧城堡、泽可拉小屋和韵律之树；外部进入不用骰，森林内部出发或离开需要骰。
- 玩家先选目标，再由前端物理投骰；最终2～5去目标，1向目标方向左偏一方向，6右偏一方向。方向相对出发格面向目标，不随摄像机改变。
- 偏移落点不存在、无道路、道路关闭、存在不可通行裂缝或动态封锁时，原地停留，仍消耗本次1行动值；没有真实位移不产生ARRIVE_CELL或重置完整停留任务。
- 允许既有甜贝儿重投链和固定骰点效果；固定6代表右偏，不代表自动到达。泽可拉/森林免疫仍不开放，不顺带开放调查、Boss、传送或跨缝。
- 规则已覆盖已有测试局，不清局、不重建卡牌副本、不改写历史回执。
- movable-cells可选actionType=MOVE_ONE_STEP/USE_RAILWAY，省略为普通。森林快照统一在pendingCheck.preparation，无movementContext字段；最终roll.checkResult=null，movement含实际落点/费用/原地原因。完整类型与省略规则以专项第8节定稿为准，不采用其前文历史草案。

## 7. 玩家视图、可用行动与连接心跳

### 7.1 GET /api/v1/games/{gameId}/player-view

Bearer，无请求体，返回完整 PlayerViewResponse。示例只列当前接入重点字段，players数组和附加业务对象已截断：
~~~json
{
  "gameId":100,"mapConfigId":1,"gameStatus":"RUNNING","gameStatusText":"进行中",
  "currentRound":1,"currentTurnUserId":1,"currentTurnSeq":1,"stateVersion":8,"winnerUserId":null,
  "players":[{"playerId":1001,"userId":1,"turnOrder":1,"isConnected":1,"currentCellId":100010,
              "coins":3,"prestige":0,"currentSpeedPoints":2,"currentSocialPoints":3,
              "activeItemEffects":[]}],
  "engineState":{
    "engineVersion":"EVENT_V2","batchId":"CORE_9_PUBLIC_SHOP","phase":"MIDDLE","phaseCursor":0,
    "currentPlayerId":1001,"personalTurnNo":1,"personalTurns":[{"playerId":1001,"personalTurnNo":1}],
    "isExtraTurn":false,"pausedReason":null,
    "connectionPolicy":{"heartbeatSeconds":5,"timeoutSeconds":15,"reconnectWaitSeconds":10,"initialGraceSeconds":45},
    "pendingReward":null,"pendingCheck":null,"finalResult":null,
    "availableActions":[{"actionType":"END_TURN","actionTypeText":"结束回合","available":true,"reason":null,"reasonText":null}],
    "publicShop":{"inventoryScope":"GAME","inventoryGameId":100,"stateVersion":8,"cellId":null,"shelfSize":3,"items":[]},
    "shops":[],"shopAccessPoints":[]
  }
}
~~~

publicShop为每局唯一库存；真实 `shops` 为该库存的一项兼容列表，不是每个商店一套。上例仅用空数组省略库存内容。玩家还包含角色代码／名字／称号／技能、当前格号／名称、昵称、effectiveAttributes等字段；不要把上述截断样例当成服务端删字段。

| engineState字段 | 客户端含义 |
|---|---|
| phase / phaseCursor | BEFORE_START、START、MIDDLE、END及服务端保存的处理游标；客户端不推进游标 |
| currentPlayerId | 当前局内玩家；不同于顶层currentTurnUserId账号ID |
| personalTurnNo / personalTurns | 查看者／各玩家的个人回合号；额外与跳过回合也按既有规则计入 |
| pausedReason | null为无全局暂停标记；ALL_OFFLINE为全员离线后交接暂停 |
| pendingReward | 全局FIFO队首，可能属于其他玩家；非队首不能操作 |
| pendingCheck | 当前查看者的待判定；最终确认前不提前结算 |
| availableActions | 当前查看者的可用行动；优先按available控制按钮 |
| finalResult | 结束结果：kind、winnerUserIds数组、ranking数组；winnerUserId仅兼容单值 |
| connection | **只有本次connection请求响应**才附带的私有连接回执，普通GET不保证存在 |

`GET /api/v1/games/{gameId}/available-actions?playerId={自己的playerId}` 返回 AvailableAction数组，与视图内结构一致。`reason` 和 `reasonText` 目前都可能为中文，不是稳定原因码；暂停展示使用pausedReason。尚无独立offlinePlayers数组，可筛选 `players[].isConnected==0`。

### 7.2 建立、续期及关闭连接

同一接口三种用法，均需Bearer。CORE_9不能只传query参数而不带JSON身份。

~~~http
POST /api/v1/games/100/players/1001/connection?connected=true
Authorization: Bearer <accessToken>
Content-Type: application/json

{"clientInstanceId":"69f9d379-f5dc-42b8-9aef-5f33802da919"}
~~~

成功后从 `data.engineState.connection` 保存本连接回执：
~~~json
{
  "clientInstanceId":"69f9d379-f5dc-42b8-9aef-5f33802da919",
  "connectionId":"dcdd4883-7970-4764-8af3-c2f198b0f00d",
  "serverTime":"2026-11-02T15:00:00.123456","lastHeartbeatAt":"2026-11-02T15:00:00.123456",
  "expiresAt":"2026-11-02T15:00:15.123456","serverTimeZone":"Asia/Shanghai","serverUtcOffset":"+08:00",
  "connectionActive":true,"playerConnected":true,
  "heartbeatSeconds":5,"timeoutSeconds":15,"reconnectWaitSeconds":10,"initialGraceSeconds":45
}
~~~

续期：默认每5秒调用同一路径 connected=true，请求体：
~~~json
{"clientInstanceId":"69f9d379-f5dc-42b8-9aef-5f33802da919","connectionId":"dcdd4883-7970-4764-8af3-c2f198b0f00d"}
~~~

关闭：改为 `connected=false`，传相同两项身份。只能关闭本连接；多窗口中还有有效连接，playerConnected仍可能为true。重复关闭不会重复产生离线转换或重置等待。

- clientInstanceId为本次窗口连接尝试ID，1～64位字母、数字、横线或下划线；推荐UUID。每次正常心跳复用，不是每5秒生成新ID，也不是永久设备ID。
- 首次建连不传connectionId；建连响应丢失时，同一尚有效的clientInstanceId可重试，续期并取回原connectionId。
- 租约过期返回CONNECTION_EXPIRED，已关闭返回CONNECTION_CLOSED；均新建clientInstanceId重连，不复活旧身份。CONNECTION_STATE_CONFLICT表示身份不匹配。缺身份返回CONNECTION_ID_REQUIRED。
- 新局isConnected初始为1；首次尚无租约时45秒宽限后判离线。已建立租约时最后有效心跳后15秒判离线，再等10秒处理既有离线兜底。后台默认每秒扫描，边界按持久化截止时间，不按扫描时刻重新计时。
- 普通续期只更新租约，不推进阶段、不补资源、不发奖、不增加stateVersion。真实1→0或0→1才增加版本并同步符合条件的待判定。
- 登录、active-session、GET、轮询、行动、打开任务／商店界面均不能代替心跳。后台等待他人回合、奖励选择、骰子动画、卡牌弹窗时也保持续期。
- 全员离线在END交接处设置ALL_OFFLINE，gameStatus仍RUNNING；任意一名玩家有效重连可清除此标记，后台再从保存游标继续，不要求全员同时回来。
- 连接过期和网络断开不删除房间成员、不退出对局。离线等待到期后的自动判定、奖励放弃／强制待办处理、结束回合是当前服务端既有行为，不由客户端自行模拟。
- 连接恢复后读取最新视图／待判定；当前玩家、版本和阶段可能已变化。不可自行把所有available改为true。

## 8. 公共任务板、玩家任务、货架和背包

以下GET均需Bearer，`playerId` 为自己的局内ID。都只读，不扣费、不抽新牌、不发奖。

### 8.1 GET /api/v1/games/{gameId}/quest-offers?playerId={playerId}

不在任务格也可查看本局公共候选；接取／刷新才检查任务板地点。任务候选不是玩家已经接取的任务，不能与 `/quests` 混用。

~~~json
{
  "gameId":100,"playerId":1001,"refreshSocialCost":1,
  "offers":[{"offerId":51,"questId":149,"slotNo":1,"questCode":"OFFICIAL_QUEST_049","questName":"蛋糕谜案",
    "background":"以服务端为准","description":"以服务端为准","rewardText":"以服务端为准","locationText":"中心城火车站",
    "difficultyLevel":7,"checkAttributeTypes":"SPEED,SOCIAL,MAGIC","triggerType":"SOCIAL_CHECK",
    "targetCount":3,"rewardCoins":3,"rewardPrestige":2,"rewardItemCount":0,
    "requiredCellId":null,"requiredCellTag":"CANTERLOT_TRAIN_STATION","requiredQuestPointCode":null,
    "requiredItemId":null,"requiredItemCategory":null,"requiredCoinCost":0,"requiredSocialCost":1,
    "requiredPrestigeCost":0,"requiredAttributeType":null,"requiredAttributeValue":0,
    "implementationStatus":"READY","implementationNote":null}]
}
~~~

这是字段结构示例，不保证动态候选包含049或示例中的原始配置枚举值。其他中文映射字段如checkAttributeTypesText、triggerTypeText按实际响应显示。开局初始化最多3张公共候选；接取、刷新后的候选需重新查询，不用客户端随机卡。返回offerId/slotNo用于展示；**CORE_9 ACCEPT_QUEST提交questId，不是slotNo**。

### 8.2 GET /api/v1/games/{gameId}/quests?playerId={playerId}

~~~json
{
  "gameId":100,"playerId":1001,
  "quests":[{"playerQuestId":401,"questId":149,"questCode":"OFFICIAL_QUEST_049","questName":"蛋糕谜案",
    "status":"IN_PROGRESS","progress":1,"targetCount":3,
    "objectives":[{"objectiveKey":"OFFICIAL_QUEST_049:SPEED","text":"速度判定成功一次","completed":true,
                   "currentValue":1,"requiredValue":1,"unit":"次","successful":true,"stateAvailable":true}],
    "conditions":[{"conditionKey":"CURRENT_LOCATION","text":"位于任务地点","satisfied":true}],
    "objectivesStatus":"SUPPORTED","objectiveNotes":[],"continuousTask":null,
    "checkAttributes":[{"attributeType":"SPEED","completed":true,"eligible":false,"available":false,"unavailableReason":"以服务端为准"}]
  }],
  "completedQuests":[]
}
~~~

目标标识、条件标识和中文内容以上游实际返回为准；上述objectives数组仅截断演示，049实际有三个独立目标。不得按progress平均划掉目标，不从一次失败尝试推导成功。仅 `objectives[].completed` 为true时划掉该目标；currentValue/requiredValue/unit展示次数、金币量等。`conditions[].satisfied` 是当前可变条件，离开地点可变false，不代表已完成目标自动清空。展示状态为UNAVAILABLE时显示objectiveNotes，不伪造目标状态。

`quests` 包含IN_PROGRESS／READY_TO_SUBMIT；旧READY_TO_SETTLE兼容映射为待提交。`completedQuests`是已完成实例快照，不重新发奖。按playerQuestId区分同名任务；提交物品/支付/判定等原目标行动继续传questId及建议的playerQuestId，唯独新SUBMIT_QUEST只接收playerQuestId。submission提供目标锁定、提交资格、冻结预览和已付快照，详见任务专项。

014可附带 `stayState`：targetCellId、acceptedPersonalTurn、currentPersonalTurn、observedPersonalTurn、entryCellId、eligibleThisTurn、eligibilityReason、lastCountedPersonalTurn、lastProcessedEndPersonalTurn。接取回合不计；完整停留两个个人回合，实际离开立即清零，同回合返回仍无资格，END处理完待办后计数。客户端不自行计时。

049可附带checkAttributes，区分目标completed、是否仍可尝试eligible、此刻可执行available；已成功属性不能再次消耗费用尝试。052按接取后在水晶集市购买道具的实际金币支出累计5，其他花钱不算；025地点唯一为A9小马谷集市，不是任意SHOP/MARKET。普通任务按规则即时结算，END类任务按服务端阶段结算。

### 8.3 GET /api/v1/games/{gameId}/shop/current?playerId={playerId}

任何地点可查看同一局公共货架；不在SHOP时 `accessAllowed=false`，不代表读取失败或没有库存。

~~~json
{
  "cellId":100010,"cellName":"市政厅","cellNameText":"市政厅",
  "refreshSocialCost":2,"shelfSize":3,
  "inventoryScope":"GAME","inventoryGameId":100,"accessAllowed":false,"stateVersion":8,
  "items":[{"shelfSlotId":31,"itemId":1017,"itemName":"药剂","itemType":"CONSUMABLE","itemTypeText":"消耗品",
            "itemCategory":"以服务端为准","buyPrice":2,"sellPrice":1,"description":"以服务端为准","itemTag":null}],
  "reservedItems":[]
}
~~~

价格及卡牌文字取实际响应，上例不是承诺药剂固定售价。cellId/cellName是查询玩家实际位置，不是库存身份；inventoryScope=GAME、inventoryGameId才表示本局公共库存。player-view.engineState.publicShop的cellId为null，表示不绑定单个入口。

所有SHOP共用本局一套最多3件货架；开局抽牌、购买后补位、刷新才改变库存。GET、打开弹窗、进入商店、重连和出售不会补货。shelfSize表示容量，不保证items总是3件。独立货架stateVersion与该库存属于同一快照；旧响应不可覆盖更高版本UI。购买提交真实shelfSlotId，不用itemId或展示序号。

### 8.4 GET /api/v1/games/{gameId}/items?playerId={playerId}

~~~json
{"gameId":100,"playerId":1001,"totalCount":1,"items":[
  {"playerItemId":801,"itemId":1017,"itemName":"药剂","itemType":"CONSUMABLE","itemTypeText":"消耗品",
   "itemCategory":"以服务端为准","buyPrice":2,"sellPrice":1,"description":"以服务端为准",
   "itemTag":null,"location":"HAND","locationText":"手牌","createdAt":null}
],"pendingChoices":[]}
~~~

当前物品查询主字段以PlayerItemVO为准，location为HAND/EQUIPPED。显示用itemId，装备／出售／提交／使用传playerItemId。本次已允许客户端接入基础道具操作，准确payload、替换/次数/容量和效果展示见[本次专项](道具系统与任务详情接入交接.md)。pendingChoices是兼容数组，当前奖励队首从engineState.pendingReward读取。

### 8.5 其他只读接口

- `GET /api/v1/games/{gameId}/work/current?playerId={playerId}`：data为null表示当前无匹配工作；有值时为cellId、cellName／cellNameText、attributeType／attributeTypeText、difficulty、socialCost、rewardCoins。固定属性直接使用服务端返回的具体枚举；attributeType=`ANY`时必须由玩家选择`STRENGTH`、`SPEED`、`SOCIAL`、`CHARM`、`MAGIC`或`MIND`之一，`ANY`本身不能提交。rewardCoins为基础成功奖励预览，实际打工收入取最终/roll的workResult.rewardCoins（第10.5～10.10节）。当前主动WORK走准备投骰，不沿用旧版“请求立即出随机结果”。
- `GET /api/v1/games/{gameId}/partners?playerId={playerId}`：data为gameId、playerId、partners数组；项包含playerPartnerId、partnerId、partnerCode、partnerName、partnerType、description、六维文本、recruitCondition、skillOne/Two、bondSkill、status等。PENDING_ACTIVE不等于ACTIVE。
- `GET /api/v1/games/{gameId}/action-logs?limit=20`：data为ActionLog数组，包含logId、gameId、stateVersion、playerId、playerName、actionType/actionTypeText、message、rawMessageText、rawMessage、createdAt。
- `GET /api/v1/games/{gameId}/check-logs?limit=20`：data为CheckLogDetail数组，字段为checkLogId、gameId、playerId、stateVersion、sourceType/sourceTypeText、dice、attributeType/attributeTypeText、attributeValue、modifier、finalValue、difficulty、resultType/resultTypeText、createdAt。只检视服务端最终判定，不生成骰点、不结算任务。limit可省略，受服务端默认及上限裁剪。

## 9. 统一行动接口

### 9.1 请求与重试

~~~http
POST /api/v1/games/100/actions
Authorization: Bearer <accessToken>
Content-Type: application/json

{"requestId":"4c7f2ae4-1d26-4da8-98d3-7a2230b20bb8","playerId":1001,"stateVersion":8,
 "actionType":"MOVE_ONE_STEP","payload":{"targetCellId":100011}}
~~~

CORE_9四项 requestId/playerId/stateVersion/actionType 必填；payload按行动传对象。当前回合、阶段、待办、判定及资源资格都由后端验证。仅available=true不能保证并发提交时仍成功；收到错误后刷新而不是绕过限制。

### 9.2 本批全部25种actionType

下表“暂不优先”不是关闭。所有行都通过同一个/actions入口，鉴权及版本规则相同。

| actionType | payload样式 | 当前前端优先级／说明 |
|---|---|---|
| MOVE_ONE_STEP | `{"targetCellId":100011}` | 已接入：先查普通movable-cells；森林方向骰见第6.1节 |
| END_TURN | `{}` | 必需：不靠资源耗尽自动结束 |
| ACCEPT_QUEST | `{"questId":149}` | 优先：公共候选中取questId，任务板地点 |
| ABANDON_QUEST | `{"questId":149,"playerQuestId":401}` | 优先：自己的任务，扣1声望 |
| SUBMIT_QUEST | `{"playerQuestId":401}` | 已部署：仅本人MIDDLE且canSubmit，正式兑现冻结奖励；不传questId |
| REFRESH_QUEST_OFFERS | `{}` | 优先：任务板地点，当前1社交 |
| SUBMIT_QUEST_ITEM | `{"questId":142,"playerQuestId":402,"playerItemId":801}` | 优先：实际持有实例，不触发道具使用效果 |
| PAY_QUEST_RESOURCE | `{"questId":107,"playerQuestId":403}` | 优先：费用由任务配置决定，不传自定金额 |
| QUEST_SOCIAL_CHECK | `{"questId":149,"playerQuestId":401,"attributeType":"SPEED"}` | 优先：准备判定，不传骰点 |
| QUEST_FULL_SOCIAL_CHECK | `{"questId":149,"playerQuestId":401,"attributeType":"SPEED"}` | 任务实际规则允许时使用，不能替代单社交规则 |
| SHOP_BUY_ITEM | `{"shelfSlotId":31}` | 优先：SHOP地点，当前1社交＋实际售价，容量检查 |
| SHOP_SELL_ITEM | `{"playerItemId":801}` | 优先：SHOP地点，当前1社交，按配置结算 |
| SHOP_REFRESH | `{}` | 优先：SHOP地点，当前2社交，公共货架整体刷新 |
| RESOLVE_TURN_START_EFFECT | `{"rewardId":71,"cardId":901}` 或 `{"rewardId":71,"abandon":true}` | 必需：通用FIFO队首处理，名字不限制只在START使用 |
| USE_RAILWAY | `{"targetCellId":100086}` | 已部署独立行动，每次仅相邻一站，1金币+1行动值；目标从铁路模式查询 |
| WORK | `{"attributeType":"SOCIAL"}` | 已接入准备与主动投骰；ANY工作先由玩家选择六维具体枚举，禁止提交ANY |
| DRAW_PARTNER_OFFER | `{}` | 暂不优先：招募点准备判定，不传中间骰点 |
| RECRUIT_PARTNER | `{"rewardId":71,"cardId":901,"conditionIndex":1}` | 暂不优先：PARTNER队首；满员合法替换另传dismissPlayerPartnerId |
| RECRUIT_PARTNER_FROM_OFFER | 同上 | 兼容同一PARTNER队首流程，不重复招募 |
| PLAY_ITEM | `{"playerItemId":801}` | 本次可接入：由类型分派装备／使用，不与具体动作重复提交 |
| EQUIP_ITEM | `{"playerItemId":801}` | 本次接入：需要替换时加replacePlayerItemId，最多2件 |
| USE_ITEM | `{"playerItemId":801}` | 本次接入：仅本批准入消耗品，不补当前剩余行动资源 |
| UNEQUIP_ITEM | `{"playerItemId":801}` | 本次接入：受个人回合手动卸下次数限制 |
| DISCARD_ITEM | `{"playerItemId":801}` | 本次接入：FIFO领取前可合法整理，不代表任意次数 |
| USE_CHARACTER_SKILL | `{}` | 暂不优先：仅本批实际有主动处理器的角色 |

原先仅传playerQuestId的放弃任务样例不适用于本批：CORE_9实际要求questId，playerQuestId是防过期实例的补充。任务编号、实体ID、候选cardId不能互换。

### 9.3 行动成功返回

完整GameActionResponse字段：
`gameId,oldStateVersion,newStateVersion,actionType,actionTypeText,resultType,resultTypeText,
movement,work,shopBuy,shopRefresh,itemUse,itemSell,partnerOffer,partnerRecruit,questCheck,investigation,bossProgress,
questUpdates,animations,logs,playerFacingLogs,playerFacingMessage,playerViewState,nextAvailableActions,pendingCheck,deckDraws,questSettlement`。
没有本次结果的子对象可为null，兼容字段不表示对应关闭玩法已开放。

~~~json
{
  "gameId":100,"oldStateVersion":8,"newStateVersion":9,"actionType":"QUEST_SOCIAL_CHECK",
  "actionTypeText":"任务判定","resultType":"CHECK_ROLL_PENDING","resultTypeText":"等待投骰",
  "movement":null,"work":null,"shopBuy":null,"shopRefresh":null,"itemUse":null,"itemSell":null,
  "partnerOffer":null,"partnerRecruit":null,"questCheck":null,"investigation":null,"bossProgress":null,
  "questUpdates":[],"animations":[],"logs":["判定已准备，请投骰并确认最终点数，尚未结算。"],
  "playerFacingLogs":[],"playerFacingMessage":"以服务端返回为准",
  "playerViewState":{"gameId":100,"stateVersion":9},
  "nextAvailableActions":[],"pendingCheck":{"checkId":501,"status":"WAITING_ROLL","stateVersion":9,"authorizationRound":0},
  "deckDraws":[]
}
~~~

playerViewState与pendingCheck在上例截断，真实响应提供完整视图和快照。状态、任务目标、奖励后资源使用本次最终响应，不为了“补显示”再次提交行动。newStateVersion不保证恰好old+1，结算链可能包含其他合法变化。

movement包含fromCellId/Code/Name、requestedTargetCellId/Code/Name、actualTargetCellId/Code/Name、costSpeed、remainingSpeed。以actualTarget为移动结果，不提前把棋子停在请求位置。
新movement另含costCoins、coinsAfter、movementOutcome；STAYED含stayReason，MOVED省略stayReason。历史响应可缺新增字段。森林最终/roll返回movement、checkResult=null，不能用属性success判断位移。SUBMIT_QUEST的questSettlement含实际收益、伙伴加成、余额和实际待选道具数；coinsGranted已含partnerBonusCoins，不能再加一次。
shopBuy包含boughtItem（playerItemId/itemId/itemName/location/locationText）、coinsAfter、newShelfItem；newShelfItem可为null。
shopRefresh包含socialCost、socialAfter、items、messages，不是remainingSocial或shelfItems。itemSell包含playerItemId、itemId、itemName、itemType/itemTypeText、locationAfter/locationAfterText、sellPrice、coinsAfter、prestigeAfter；itemUse包含playerItemId、itemId、itemName、itemType/itemTypeText、locationAfter/locationAfterText、replacedPlayerItemId、speedDelta/speedAfter、socialDelta/socialAfter、fromCellId/toCellId。只按真实返回展示，不用前端预测覆盖playerViewState。

## 10. 主动投骰、重投授权和最终确认

任务、WORK、DRAW_PARTNER_OFFER在当前批次统一为“准备 → 客户端掷骰 → 可选重投授权 → 最终确认”。确认前不结算成功／失败，不提前发奖或抽伙伴。具体操作流程另见 [投骰接入说明](Godot_任务主动掷骰接入说明.md)。

### 10.1 准备与查询

使用第9节/actions准备。任务payload选择任务与允许的attributeType；WORK选择attributeType；DRAW_PARTNER_OFFER为{}。不能在准备请求附加dice、判定成功或奖励参数。

成功 data.resultType=CHECK_ROLL_PENDING，data.pendingCheck为PendingCheckVO；恢复时：
~~~http
GET /api/v1/games/100/checks/pending
Authorization: Bearer <accessToken>
~~~

成功data可能为null，表示当前账号没有WAITING_ROLL；不要把null当异常或反复创建判定。

PendingCheckVO字段：
| 分组 | 字段及用途 |
|---|---|
| 身份 | checkId,gameId,playerId,questId；WORK/招募的questId可空 |
| 类型 | businessType=QUEST/WORK/PARTNER_RECRUIT，businessSourceId，preparation（服务端冻结的业务快照） |
| 状态 | status=WAITING_ROLL/COMPLETED/CANCELLED，rollType，attemptNo |
| 判定参数 | attributeType,attributeValue,modifier,difficulty,socialCost,remainingSocial，均由服务端决定 |
| 授权 | stateVersion,personalTurnNo,authorizationRound，最终提交必须使用当前值 |
| 能力 | canConfirm,confirmUnavailableReason,canReroll,nextRerollSocialCost,rerollUnavailableReason,rerollChain |
| 时效／结束 | expiresAt,fallbackUsed,questResolution,settlementOperationId,cancellationReason |
| 固定点数 | fixedDice可选，由有效角色效果产生；不要将客户端计算值当此字段 |

新判定常见authorizationRound=0，但客户端始终取响应，不自行推算轮次。初始及成功重投授权后的期限均按服务端返回，当前为120秒。

### 10.2 重投只申请授权，不提交骰点

~~~http
POST /api/v1/games/100/checks/501/reroll
Authorization: Bearer <accessToken>
Content-Type: application/json

{"requestId":"764ee9f6-5964-48ef-bf6c-c003795c5948","stateVersion":9,"authorizationRound":0}
~~~

成功data就是**更新后的PendingCheckVO**，不是GameActionResponse。采用新的stateVersion、authorizationRound、expiresAt、remainingSocial及能力字段后，才进行下一次物理掷骰。此请求不接受dice或其他未知业务参数。

普通玩家默认没有重投资格。有效甜贝儿每个个人回合最多绑定一条判定重投链：同一check先免费一次，再可付1社交一次；未用付费机会不能转移给另一check。成功点数也可主动重投，不要求失败。重投后只能确认最新结果，不能回选旧结果；只上传最终骰点的协议本身无法防客户端作弊。

每次新重投授权用新requestId；同一次授权网络重试用原ID及原请求体，不能重复扣费。到期后新重投授权返回CHECK_DEADLINE_EXPIRED，不扣费、不延长期限。

### 10.3 最终提交骰点

~~~http
POST /api/v1/games/100/checks/501/roll
Authorization: Bearer <accessToken>
Content-Type: application/json

{"requestId":"e16540f6-7a6b-4074-87c2-ab798b468e24","stateVersion":10,"authorizationRound":1,"dice":5}
~~~

dice必须是JSON整数1～6，不是字符串、小数、属性值或最终判定值；不传attributeType、difficulty、成功与否或奖励。stateVersion与authorizationRound取当前PendingCheck，不用过时的准备响应。

CheckRollResponse结构示例（视图、待判定和更新已截断）：
~~~json
{
  "pendingCheck":{"checkId":501,"status":"COMPLETED","stateVersion":11,"authorizationRound":1,"canConfirm":false,"canReroll":false},
  "submittedDice":5,"effectiveDice":5,
  "checkResult":{"dice":5,"attributeType":"SPEED","attributeValue":3,"modifier":0,"finalValue":8,"difficulty":7,"success":true},
  "questUpdates":[],"playerViewState":{"gameId":100,"stateVersion":11},
  "playerFacingMessage":"任务判定成功。","idempotentReplay":false,
  "logs":[],"deckDraws":[]
}
~~~

此处为QUEST任务业务示例，非WORK的workResult和无招募候选的partnerOfferId按NON_NULL省略，不是固定返回null。最终值、effectiveDice、成功与否、资源和目标全部以返回为准，角色固定点效果可能使submittedDice与effectiveDice不同。WORK完整结果及成功／失败响应见第10.5～10.10节；招募最终结果可给partnerOfferId，实际候选仍从队首读。049失败也返回已有三项目标快照，不把失败当作清空进度。

### 10.4 重连、软截止与幂等

先恢复认证及连接，再GET player-view和checks/pending，按服务端canConfirm/canReroll恢复界面；不能只依赖本地骰子动画或旧点数。客户端未保存的物理骰点无法从后端取回，因为中间点数没有上传。

本地倒计时归零不等于服务端已经完成。到期后不能授权新重投，但尚未结算、上下文合法的最终roll仍可与系统代掷竞争锁，只结算一次。系统兜底可能在倒计时到期或已有离线等待到期流程中执行。损坏上下文可能被记录为CANCELLED；收到CHECK_CANCELLED等错误时显示msg并刷新，不自行退款或领奖，也不假设GET pending能够返回已取消记录的cancellationReason，已完成／取消结果的查询边界见第10.8节。

网络重试保留原requestId及请求体，服务端重放首次响应；同一requestId换checkId会冲突。已完成check的新requestId不会再次结算，不以重复提交作为重投。心跳造成真实上下线时版本可能同步，刷新后以新ID发新请求；旧成功回执仍保留其历史版本。

### 10.5 WORK最终结算对象：完整字段与可空边界

本节于2026-11-02补齐，以已部署CORE_9的WorkResultVO、CheckRollResponse、EngineCheckFacade.roll、EngineChecks.finish、EngineBusinessChecks.settleWork为准，不新增接口或改变玩法。普通最终确认的打工结果位于 `data.workResult`，不是准备响应的 `data.work`。

WorkResultVO只有以下六个JSON字段，没有顶层success、income、coinsBefore、fallbackUsed或重投费用汇总字段：

| 字段 | JSON类型 | 含义 | 正常CORE_9 WORK最终响应中的可空情况 |
|---|---|---|---|
| socialCost | number（整数） | 本次WORK准备阶段已经扣除的基础社交费用；不包含后来付费重投的1社交，不是最终确认时再次扣费 | 非空；数值按准备快照 |
| remainingSocial | number（整数） | 本次最终确认的事件／阶段结算链处理后，玩家实际剩余社交点；可已包含重投或其他合法变化 | 非空，耗尽为0，不是null |
| rewardCoins | number（整数） | 本次打工本身实际入账的金币，包含适用的打工附加奖励；不含另一次任务完成奖励等独立收益 | 非空；打工失败明确为0 |
| coinsAfter | number（整数） | 最终确认结算链结束后该玩家金币总余额；可能同时包含任务奖励等其他合法变化 | 非空；余额为0就返回0，不代表本次奖励为0 |
| checkLog | object（CheckLogVO） | 本次最终判定，与同一响应data.checkResult字段值一致；不是再次执行的判定 | 非空，成功和失败都返回 |
| forceEndTurn | boolean | 兼容的打工结果标志；当前CORE_9构造路径固定为false，不以此模拟跳回合 | 非空、当前为false；实际阶段仍看playerViewState |

VO使用Integer/Boolean等可空Java类型，不能据此推导业务必须返回null。上述“非空”指当前CORE_9正常构造路径；客户端收到异常缺字段时不要擅自补0后当作已成功结算。

CheckLogVO的完整结构：
| 字段 | JSON类型 | 含义 |
|---|---|---|
| dice | number（整数） | 服务器采用的有效点数，与data.effectiveDice一致 |
| attributeType | string | 判定属性代码，例如MAGIC |
| attributeTypeText | string | 属性中文，例如魔法 |
| attributeValue | number（整数） | 准备时冻结的属性值 |
| modifier | number（整数） | 服务端修正值 |
| finalValue | number（整数） | 服务端计算的最终判定值 |
| difficulty | number（整数） | 服务端采用的难度 |
| success | boolean | 本次判定是否成功；false同样是一次正常业务结算 |

六个workResult字段及八个checkLog字段在下方WORK示例中完整列出。字段顺序不构成契约。

### 10.6 WORK成功和失败最终响应示例

以下均为**结构说明数据，不是真实对局回包**，不包含令牌、账号口令或其他敏感信息。假设同一类示例工作：基础费用2社交、基础成功奖励2金币、魔法属性3、修正0、难度7；准备前4社交、5金币，无重投、无伙伴加成、无额外任务反应。实际费用、难度和奖励不能硬编码成这些数字。

示例保留完整workResult与checkResult；pendingCheck和playerViewState仅列关联字段，其余视图／快照字段按本章前文定义，不能据此从DTO删除字段。partnerOfferId为空时按NON_NULL省略，不用null占位。

成功：提交5点，有效5点，最终8，成功，实际打工所得2，金币余额7：
~~~json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {
    "pendingCheck": {
      "checkId": 501,
      "gameId": 100,
      "playerId": 1001,
      "businessType": "WORK",
      "status": "COMPLETED",
      "stateVersion": 10,
      "authorizationRound": 0,
      "fallbackUsed": false,
      "questResolution": null,
      "cancellationReason": null
    },
    "submittedDice": 5,
    "effectiveDice": 5,
    "checkResult": {
      "dice": 5,
      "attributeType": "MAGIC",
      "attributeTypeText": "魔法",
      "attributeValue": 3,
      "modifier": 0,
      "finalValue": 8,
      "difficulty": 7,
      "success": true
    },
    "questUpdates": [],
    "playerViewState": {
      "gameId": 100,
      "gameStatus": "RUNNING",
      "stateVersion": 10,
      "players": [
        {
          "playerId": 1001,
          "userId": 1,
          "coins": 7,
          "currentSocialPoints": 2
        }
      ],
      "engineState": {
        "phase": "MIDDLE",
        "currentPlayerId": 1001,
        "pendingCheck": null,
        "pendingReward": null
      }
    },
    "playerFacingMessage": "打工判定成功，收入已结算。",
    "idempotentReplay": false,
    "workResult": {
      "socialCost": 2,
      "remainingSocial": 2,
      "rewardCoins": 2,
      "coinsAfter": 7,
      "checkLog": {
        "dice": 5,
        "attributeType": "MAGIC",
        "attributeTypeText": "魔法",
        "attributeValue": 3,
        "modifier": 0,
        "finalValue": 8,
        "difficulty": 7,
        "success": true
      },
      "forceEndTurn": false
    },
    "logs": [
      "打工判定成功，收入已结算。"
    ],
    "deckDraws": []
  }
}
~~~

失败：提交1点，有效1点，最终4，失败，实际打工所得0，金币余额仍5；费用已在准备阶段消耗，不退回：
~~~json
{
  "code": 1,
  "msg": "success",
  "errorCode": null,
  "data": {
    "pendingCheck": {
      "checkId": 502,
      "gameId": 100,
      "playerId": 1001,
      "businessType": "WORK",
      "status": "COMPLETED",
      "stateVersion": 10,
      "authorizationRound": 0,
      "fallbackUsed": false,
      "questResolution": null,
      "cancellationReason": null
    },
    "submittedDice": 1,
    "effectiveDice": 1,
    "checkResult": {
      "dice": 1,
      "attributeType": "MAGIC",
      "attributeTypeText": "魔法",
      "attributeValue": 3,
      "modifier": 0,
      "finalValue": 4,
      "difficulty": 7,
      "success": false
    },
    "questUpdates": [],
    "playerViewState": {
      "gameId": 100,
      "gameStatus": "RUNNING",
      "stateVersion": 10,
      "players": [
        {
          "playerId": 1001,
          "userId": 1,
          "coins": 5,
          "currentSocialPoints": 2
        }
      ],
      "engineState": {
        "phase": "MIDDLE",
        "currentPlayerId": 1001,
        "pendingCheck": null,
        "pendingReward": null
      }
    },
    "playerFacingMessage": "打工判定失败，没有打工收入。",
    "idempotentReplay": false,
    "workResult": {
      "socialCost": 2,
      "remainingSocial": 2,
      "rewardCoins": 0,
      "coinsAfter": 5,
      "checkLog": {
        "dice": 1,
        "attributeType": "MAGIC",
        "attributeTypeText": "魔法",
        "attributeValue": 3,
        "modifier": 0,
        "finalValue": 4,
        "difficulty": 7,
        "success": false
      },
      "forceEndTurn": false
    },
    "logs": [
      "打工判定失败，没有打工收入。"
    ],
    "deckDraws": []
  }
}
~~~

两者外层code都为1，表示最终确认请求处理成功；不能用code=1、pendingCheck.status=COMPLETED或非空workResult判断打工成功。root pendingCheck保留本次COMPLETED快照，而playerViewState.engineState.pendingCheck为null表示没有未决判定，两者不矛盾。

### 10.7 结果页字段读取与奖励口径

| 界面内容 | 准确来源 | 不应使用的替代值 |
|---|---|---|
| 打工成功／失败 | data.checkResult.success；等价于data.workResult.checkLog.success | 外层code、COMPLETED状态、workResult是否存在、中文消息匹配 |
| 本次打工“获得N金币” | data.workResult.rewardCoins | work/current.rewardCoins、coinsAfter、前后金币差值 |
| 结算后的金币余额 | data.workResult.coinsAfter；同一响应playerViewState中按playerId匹配玩家的coins应对应 | 不将余额显示为本次奖励，不把旧重放余额覆盖新视图 |
| 结算后的社交余额 | data.workResult.remainingSocial；对应同一最终视图的currentSocialPoints | 不只按准备前社交减socialCost推算，付费重投可能另扣费 |
| 没有本次打工收入 | 已结算WORK且rewardCoins=0，显示“本次打工获得0金币”或“本次无打工收入” | 不把workResult缺失、data=null或请求失败都当成失败0金币 |
| 判定点数及数值说明 | submittedDice显示上报原点，effectiveDice/checkResult.dice显示实际采用值，属性／修正／最终值从checkResult取 | 不由客户端重新计算并覆盖服务端结果 |
| 提示文案 | playerFacingMessage用于直接呈现；logs用于补充日志 | 文本不是稳定成功码，不能解析中文决定扣费／奖励 |

work/current.rewardCoins是**该局工作配置的基础成功奖励预览**，不是最终实际入账值。准备时冻结所选规则；成功时按该规则计算基础奖励，并按适用的现有打工附加规则修正，失败为0。结果页只采用最终workResult.rewardCoins。

金币结算顺序是先算打工收入、发布判定／打工及资源事件，再处理相关任务等反应链，最后EngineCheckFacade重新读取coinsAfter和remainingSocial。因此实际打工收入与本次总余额增量不一定相等。任务另发的金币、声望或道具看questUpdates及最新视图／队首，不叠加回workResult.rewardCoins，不重复发奖。

forceEndTurn=false仅是当前WORK返回标志；不保证其他结算链、对局结束或离线维护不会改变阶段。阶段及后续可操作性以最新playerViewState、pendingReward和availableActions为准。

### 10.8 普通确认、固定点、代掷、取消及重放的真实差异

| 情况 | HTTP／对象现状 | 客户端处理 |
|---|---|---|
| 普通玩家成功提交/roll | code=1；WORK不论成功失败都含完整workResult、checkResult；pendingCheck.fallbackUsed=false | 用success和rewardCoins展示，不再次扣费或领奖 |
| 角色效果固定点数 | 同样的结构；例如submittedDice=1、effectiveDice=6，checkResult.dice和workResult.checkLog.dice均为6；pendingCheck可含fixedDice=6 | 固定点不等于系统代掷，fallbackUsed仍false；实际成败仍取success |
| 后台到期／离线系统代掷 | EngineChecks.finish(...,true)确实结算WORK并保存fallback标记，但维护入口不创建或推送CheckRollResponse | **没有一份自动送到客户端的/roll成功回执**，不能虚构workResult或示例为data.workResult=null |
| 玩家确认与后台代掷竞争 | 同一对局锁内只结算一次；若玩家先完成，按普通确认回执；若后台先完成，迟到提交可能版本过期，或在版本匹配时返回CHECK_ALREADY_COMPLETED | 刷新视图和日志，不以新requestId强行再结算 |
| 判定被系统取消 | 不执行WORK收入结算；取消原因保存在判定中。/roll可能先返回版本错误，版本匹配后可返回CHECK_CANCELLED，外层失败data=null | 不是一次“打工失败且奖励0”的成功回执；不构造workResult、退款或补奖 |
| 原成功requestId网络重试 | 反序列化并重放首次保存的CheckRollResponse，含原workResult与原视图／余额 | 不重复播放发奖动画；旧版本不能覆盖新视图 |
| 非WORK的最终确认 | CheckRollResponse.workResult使用NON_NULL：当前序列化通常**省略该键**，不是保证返回workResult:null | 依据businessType识别业务，兼容缺失或null，不显示打工结果 |

当前CORE_9首次正常/roll构造 `idempotentReplay=false`，幂等服务重放时**不会把它改成true**，而是保持首次值。该字段不能可靠识别本批网络重放；客户端按requestId／checkId追踪已处理结果并去重展示。首次和重放的金币余额都是首次完成时的快照，不是重试时的实时余额。

系统代掷有一项现有接口边界：GET /checks/pending和player-view.engineState.pendingCheck只查询WAITING_ROLL，后台完成或取消后通常变成null；**null本身不能区分成功、失败、代掷或取消**。当前没有按checkId查询已完成WORK的完整workResult接口。check-logs可查看最终判定，player-view可查看当前余额，但它们不提供该次精确rewardCoins或完整workResult；不能用余额差或work/current预览反推。

因此本版能完整展示普通最终确认及其原请求重放的打工结果；如果客户端在系统代掷或完成后丢失了首次成功回执，仅靠现有公开查询不能保证重建同一结果页。此处如实记录限制，不新增查询接口、不更改兜底规则。

### 10.9 WORK后是否还要处理奖励队列／抽牌摘要

WORK本身直接结算金币，不直接创建“打工金币待领取”窗口，也不因成功就必然抽牌；但WORK_RESOLVED／资源事件可以推进相关任务，统一反应链可按实际任务配置产生附加奖励、抽牌或待办。

- 每次/roll成功后仍检查 `data.questUpdates` 和 `data.playerViewState.engineState.pendingReward`。若有队首，根据归属、kind、canExecute、canAbandon按第11节处理；它不是再领一次workResult.rewardCoins。
- 有队首时不能直接END_TURN，结果页可以先展示打工结果，再进入现有FIFO交互；奖励全部处理完再按最新阶段／行动资格继续。
- `data.deckDraws` 在当前CORE_9最终roll中会设置为当前操作的抽牌摘要；没有抽牌通常为[]。它是数量／不足提示，不是领取命令，也不保证存在pendingReward。
- shortage=true时按actualCount显示；零张不等待空窗口，不自行退费、补抽或重新提交WORK。队首所属玩家可从pendingReward.drawResult恢复对应抽牌摘要。
- 对系统代掷，后台仍保存实际状态和队首，但没有额外HTTP推送；客户端通过最新视图恢复当前队首，不能假设能查询到一份历史deckDraws或workResult回执。

### 10.10 本次核对差异与验证范围

- 旧示例是任务判定，却总写workResult:null／partnerOfferId:null，容易误解为WORK也没有结果；按当前NON_NULL改为省略，并增加上面两份专门WORK示例。
- 旧文档未定义workResult六项、checkLog八项，以及基础预览／实际打工收入／最终余额的区别；本次已补齐。
- 明确当前幂等回放的idempotentReplay仍可为false、后台代掷没有主动/roll回执，以及已完成WORK结果查询的缺口，不为凑齐示例增加不存在的字段或接口。
- 已只读核对线上JAR哈希与本地已验收包一致（c104084cae9bda9393ff105d6f9e7d6220de142690499795cd2e45b557f8366b），相关9份源码与该包源码清单一致；读取既有隔离HTTP证据，并对已验收DTO编译类做本地序列化／反序列化检查。不将旧隔离证据或纯DTO检查描述成今日公网打工测试。
- 本次仅更新本文件及投骰专项说明；未注册、建局、建连、打工、投骰或查询真实对局，未写数据库、改业务源码或重新部署。

## 11. FIFO奖励、牌堆和结束回合

### 11.1 队首奖励

ITEM候选的完整字段、可空规则和隔离真实HTTP JSON已补齐至[ITEM奖励候选准确契约](ITEM奖励候选准确契约.md)。本人ITEM候选包含cardId、itemId、canExecute、executeUnavailableReason、capacitySatisfied、configuration；名称为configuration.item_name（snake_case），没有顶层itemName。卡图取itemId，领取提交cardId。领取同时要求队首与候选canExecute，不只检查canResolve；其他玩家私有candidates为[]，没有队首则pendingReward=null。configuration透传本局配置，不是PlayerItemVO，不能以驼峰字段猜测。

`player-view.engineState.pendingReward` 可能在START、行动结算链或END出现，不是新的抽牌接口。常用字段：
`rewardId,playerId,kind,sourceType,candidates,canResolve,hasAuthority,capacitySatisfied,requiresReplacement,
canExecute,executeUnavailableReason,unavailableReason,canAbandon,abandonUnavailableReason`，所属玩家还可见drawResult。其他玩家看不到私有候选。

领取与放弃通过/actions，必须使用当前队首rewardId：
~~~json
{"requestId":"098f5301-90e6-4d61-8e1c-11fc49393809","playerId":1001,"stateVersion":12,"actionType":"RESOLVE_TURN_START_EFFECT","payload":{"rewardId":71,"cardId":901}}
~~~

~~~json
{"requestId":"482a172c-df8f-45d6-87c7-42d6f4d0308a","playerId":1001,"stateVersion":12,"actionType":"RESOLVE_TURN_START_EFFECT","payload":{"rewardId":71,"abandon":true}}
~~~

cardId来自该队首candidate，不是itemId。canExecute=false时禁止领取，展示原因并可查看背包；canAbandon=true时可放弃。FORCED_TARGET等强制待办不可主动放弃。不要只用canResolve判断“领取可点击”，它表示归属权，不代表容量和执行条件满足。

有队首时不能移动、交易、发动普通技能或结束回合，只能处理队首及服务端允许的合法整理；整理仍受资源、当前回合和次数约束，不保证一定可做。没有通用独立遣散伙伴接口；不要从招募替换参数推导出可随时遣散。

奖励按FIFO逐个处理。END产生奖励时，会阻塞交接到下一玩家，处理完后服务端继续原游标；客户端不能手动跳过该阶段。

### 11.2 牌堆抽取不足

CORE_9每局任务／道具／伙伴／宝藏牌序独立持久化；存在四类牌堆能力不表示宝藏调查已开放。牌堆不足时先取现有牌，再洗回弃牌继续补抽；仍不足就少抽，原费用不变、不补偿以后新牌。未选择的奖励按规则回牌堆并与现有牌完整洗混；完成／放弃等实际弃牌进入弃牌堆，可在耗尽后洗回。

行动及最终投骰可附 `deckDraws[]`，所属玩家队首可附drawResult：
~~~json
{"drawBatchId":"example-batch","deckType":"ITEM","source":"示例来源","requestedCount":3,"actualCount":1,"shortage":true,"shortageCode":"DECK_INSUFFICIENT","message":"牌库不足，已抽取现有卡牌"}
~~~

shortage是成功响应里的业务提醒，不是重复扣费重抽的理由。少抽按actualCount渲染，零张不等待空队首。重复任务允许新实例，不能用旧完成记录覆盖新任务。

### 11.3 生命周期与结算

服务端四阶段：BEFORE_START → START → MIDDLE → END。客户端只在可用行动允许时提交；不自行刷新行动点、累计停留、倒计时任务、发奖或切玩家。END_TURN提交payload={}；结束阶段待办完成后才检查普通胜利并交接。

普通胜利在结束阶段比较全场分数，丹青只在胜利比较时减2，原声望不扣；并列最高者可有多个winnerUserIds。特殊直接胜利仍可立即结束。最终结果从engineState.finalResult读，不仅依赖winnerUserId。额外回合、跳过回合和离线强制结束均由服务端阶段处理。

## 12. 当前测试资源及范围

当前默认清单由 `GET /api/v1/admission/formal-readiness` 的 `data.runtimeAdmission` 给出；当前既有局的实际能力仍以本局快照与availableActions为准。formal-readiness是诊断，不代替某局的资源／回合／地点资格；`code=1`也不代表其`data.ready`必为true。

- 任务24条：006、007、010、013、014、015、016、017、018、021、022、023、025、026、038、042、045、046、047、049、050、051、052、066；完整代码前缀为OFFICIAL_QUEST_。这些是卡面编号，不是questId。
- 道具5种：1003苹果、1005花束、1013魔法书、1017药剂、1018蹄铁。卡面图以真实名称映射；服务端itemId不是一次持有实例。
- 伙伴代码：PRINCESS_LUNA、BIG_MAC、SWEETIE_BELLE；并非全量旧伙伴均已开放。
- 1017在后端已实现独立叠加、六维各+1、含使用当回合的两个个人回合，到期在END处理，不补当前行动点、不改变负重；不因此要求本轮前端开放使用。
- 010、018、050仍在本批任务池，不因前端暂不接伙伴／打工而被服务端移除。
- 016、018、047部分地点受既有森林通行条件限制；不通过改地图或伪造可达来绕过。
- 074、调查／宝藏、Boss、偷窃与未准入旧卡牌不在本次联调范围。

## 13. 错误处理清单

| errorCode／稳定状态 | 客户端处理 |
|---|---|
| AUTH_REQUIRED / AUTH_TOKEN_EXPIRED / AUTH_PERMISSION_DENIED | 登录或检查归属，不当心跳成功，不替其他playerId操作 |
| ACTIVE_SESSION_DATA_CONFLICT | 停止自行join／创建新房间，报告恢复数据冲突 |
| ROOM_NOT_FOUND | 房间不存在，刷新列表／active-session；不要继续轮询旧房间 |
| ROOM_NOT_WAITING / ROOM_MEMBER_NOT_READY / ROOM_CHARACTER_SELECTION_EXPIRED | 刷新房间；具体错误码以实际响应为准，不伪造准备或选角状态 |
| GAME_NOT_FOUND | 刷新活动位置，不复用被清理的旧对局ID |
| GAME_STATE_VERSION_EXPIRED | 刷新视图及待判定，新操作用新ID；网络未知结果先按原请求重试 |
| REQUEST_IN_PROGRESS | 原ID＋原请求体稍后重试 |
| REQUEST_ID_CONFLICT | 同一ID被用于不同内容／路径判定；先核对首次操作，不盲目再次扣费 |
| GAME_RESOURCE_NOT_ADMITTED | 当前局／批次未开放，不换旧路径绕过 |
| GAME_PHASE_BLOCKED / CHECK_ROLL_PENDING | 读phase、队首、pendingCheck，不只看gameStatus=RUNNING |
| CONNECTION_ID_REQUIRED | 补clientInstanceId，关闭还须connectionId |
| CONNECTION_EXPIRED / CONNECTION_CLOSED / CONNECTION_STATE_CONFLICT | 新建连接尝试，不复活旧租约 |
| CHECK_AUTHORIZATION_EXPIRED / CHECK_STATE_CONFLICT | 刷新当前待判定版本及authorizationRound |
| CHECK_REROLL_UNAVAILABLE / RESOURCE_NOT_ENOUGH | 按能力／原因禁用重投或行动，不本地减资源后继续 |
| CHECK_DEADLINE_EXPIRED | 不再授权重投；刷新判定，最终确认是否可用看服务端 |
| CHECK_CANCELLED / CHECK_NOT_FOUND | 结束旧判定UI并刷新，不能自行领奖或退款 |
| GAME_REWARD_NOT_HEAD / GAME_REWARD_CAPACITY | 刷新队首及资源，保持FIFO，不跳下一奖励 |
| GAME_REWARD_ABANDON_FORBIDDEN / GAME_REWARD_TARGET_INVALID | 不允许放弃强制项／刷新合法候选 |
| QUEST_EVENT_CONFIG_MISSING / GAME_ENGINE_SCHEMA_REQUIRED / GAME_SNAPSHOT_MISSING | 报告后端配置或结构问题；不让玩家反复提交同一扣费操作 |
| MAP_CONFIG_MISSING / MAP_DATA_INVALID / MAP_SCHEMA_MISMATCH | 报告地图配置／结构错误，不客户端补造地图 |
| SYSTEM_ERROR | 提示服务异常，保留请求上下文，按幂等规则重试，不显示内部堆栈 |
| engineState.pausedReason=ALL_OFFLINE | 稳定暂停原因，接入心跳并刷新；不是errorCode或新的gameStatus |
| deckDraws[].shortageCode=DECK_INSUFFICIENT | 成功少抽提示，按actualCount显示，不自动重抽 |

本表不穷举全部业务校验错误；未知errorCode显示msg并保留诊断上下文，不按中文msg做业务分支。

## 14. 文档维护和验证范围

- 本版只修改客户端目录的Markdown文档；不修改Godot/C#、服务端源码、数据库或部署环境。
- 接口依据为D:\ES当前Controller、EngineGameFacade、EngineQueries、EngineConnections、EnginePhases、EngineCheckFacade及DTO/VO；当前部署依据为2026-11-04运维报告及候选SHA-256 `1b20c8d03dab2df00986fe4b516fee5f947f4e970a62f58f4426af9403125f35`。10月31日c104和11月3日3c2129版本属于历史。
- 11月2日公开GET、此前发布55次请求和11月1日gameId=5只读取证是历史证据，不当作本轮完整玩法／Godot／真实心跳联调已通过。本轮只整理文档和检查链接，不操作运行库或公网业务。
- 历史文档检查覆盖39个Controller路由及JSON示例；11月2日客户端dotnet build成功（0警告、0错误），当时static_check存在渲染配置基线失败，非Mono Godot无法加载C#连接测试。本轮未修改project.godot、未安装环境，也没有把当时结果当成本轮重新执行的全量测试。
- 示例字段、可选null及截断范围已标注；所有资源身份和资格取实际API，不硬编码文档示例。
- 后续接口变更先更新本文，再同步清单、接入指南和投骰专项；旧文档不再追加互相冲突的“最新”段落。新增玩法、费用或时机未确认时交用户决策，不用文档代替业务决策。

需要用户决策的点：本轮未发现新增玩法项。WORK异常恢复和已完成结果查询继续暂缓；移动v2与普通任务主动提交已部署，本次按现有能力接入基础道具和任务详情，不扩展玩法。
