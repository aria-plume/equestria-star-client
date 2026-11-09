# API接口清单

## 2026-11-04 普通任务主动提交（已部署，客户端待联调）

统一入口已迁至[交接索引](README.md)。本次道具与任务详情接入见[专项](道具系统与任务详情接入交接.md)；依据正式部署报告已确认questSubmissionVersion=1、movementRulesVersion=2，不等于实际领奖/道具/Godot已经验收。

EVENT_V2已开放普通任务改为 `IN_PROGRESS → READY_TO_SUBMIT → COMPLETED`；达标只锁定目标和奖励，仍占栏，不自动发奖或抽奖励卡。
使用 `SUBMIT_QUEST`，payload仅 `{ "playerQuestId": 任务实例ID }`，沿用actions的requestId/playerId/stateVersion。只允许本人MIDDLE且无待骰/奖励阻塞；END达标下一个人回合可提交。
独立能力字段为 `player-view.data.engineState.questSubmissionVersion=1`，不能以移动版本判断。旧对局适用，不改历史回执。
查询与questUpdates新增 `submission`（达成回合、最早提交回合、canSubmit、稳定原因、冻结预览与已付奖励）；提交响应新增 `questSettlement` 实际收益和余额。
结算页确认仅打开已存在的FIFO pendingReward，不再次提交；未领取奖励不能显示为已获得道具。
本节覆盖下文对EVENT_V2普通任务“达标立即/END自动领奖”的旧说明，不改变其他关闭系统。
详细DTO、错误、迁移与结算页流程见[本批正式交接](普通任务主动提交与结算页面交接.md)。Godot源码未修改。


更新时间：2026-11-05。当前接口主文档：[ES完整API文档](ES完整API文档.md)；客户端接入顺序：[前端接口文档](小马国之星_前端接口文档.md)。

> 铁路逐站收费及森林方向骰已部署，11月4日版本继续保留。[专项第8节为最终契约](铁路与森林移动_待开发接口交接.md#8-最终接口契约)：movable-cells可选actionType=MOVE_ONE_STEP/USE_RAILWAY，目标追加costSpeed/costCoins/requiresRoll；铁路固定1金币+1行动值，普通移动排除铁路。MAZE出发准备FOREST_MOVE，统一pending/reroll/roll，方向快照在pendingCheck.preparation，最终roll含movement且checkResult=null。通过新鲜movementRulesVersion=2识别，code=1可以STAYED。没有新HTTP路径；本轮未改Godot。

## 基础信息

- 地址：`http://182.92.234.193:8080`，当前默认新局 `EVENT_V2 / CORE_9_PUBLIC_SHOP`。
- 2026-11-02只读核对健康、主地图和formal-readiness成功；2026-11-03只读地图核对12个车站、13条铁路连接。不表示完成了全部认证玩法测试。
- 接口存在、批次准入、当前可执行、前端优先接入是四个不同概念。
- 本批39个Controller路由如下；所有JSON请求使用UTF-8，认证用Bearer。成功必须同时检查HTTP和业务code。
- 除明确的公开GET及注册登录外，按表发送认证；局内私有playerId必须取本局player-view，不能用加入房间返回的成员ID。

## 全部接口

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
| GET | `/api/v1/games/{gameId}/items` | 本局自己的玩家 | query playerId必填 | PlayerInventoryResponse | 本次背包及道具详情 |
| GET | `/api/v1/games/{gameId}/quests` | 本局自己的玩家 | query playerId必填 | PlayerQuestListResponse | 优先 |
| GET | `/api/v1/games/{gameId}/quest-offers` | 本局自己的玩家 | query playerId必填 | QuestOfferListResponse | 优先 |
| GET | `/api/v1/games/{gameId}/partners` | 本局自己的玩家 | query playerId必填 | PlayerPartnerListResponse | 暂不优先 |
| GET | `/api/v1/games/{gameId}/available-actions` | 本局自己的玩家 | query playerId必填 | AvailableActionVO[] | 必需 |
| GET | `/api/v1/games/{gameId}/players/{playerId}/movable-cells` | 本局自己的玩家 | 可选actionType，省略为MOVE_ONE_STEP | MapCellVO[]，普通/铁路目标 | 已部署；USE_RAILWAY只查直接邻站 |
| POST | `/api/v1/games/{gameId}/players/{playerId}/connection` | 本局自己的玩家 | query connected必填；JSON clientInstanceId/connectionId见第7节 | PlayerViewResponse，回执在engineState.connection | 必需，周期心跳 |
| POST | `/api/v1/games/{gameId}/actions` | 本局自己的玩家 | JSON requestId/playerId/stateVersion/actionType/payload | GameActionResponse | 必需 |
| GET | `/api/v1/games/{gameId}/checks/pending` | 本局成员，只查自己 | 无，不传playerId | PendingCheckVO或null | 必需 |
| POST | `/api/v1/games/{gameId}/checks/{checkId}/roll` | 判定所属玩家 | JSON requestId/stateVersion/authorizationRound/dice | CheckRollResponse | 必需 |
| POST | `/api/v1/games/{gameId}/checks/{checkId}/reroll` | 判定所属玩家 | JSON requestId/stateVersion/authorizationRound | PendingCheckVO | 按资格开放 |
| GET | `/api/v1/games/{gameId}/bosses` | 本局成员 | 无 | 本批拒绝GAME_RESOURCE_NOT_ADMITTED | 关闭 |

## 本轮优先接入的行动

这些行动都走 `POST /api/v1/games/{gameId}/actions`，不是独立的新URL：

| actionType | payload |
|---|---|
| MOVE_ONE_STEP | `{"targetCellId":100011}` |
| END_TURN | `{}` |
| ACCEPT_QUEST | `{"questId":149}` |
| ABANDON_QUEST | `{"questId":149,"playerQuestId":401}` |
| SUBMIT_QUEST | `{"playerQuestId":401}`，只在canSubmit时提交，不传questId |
| EQUIP_ITEM / USE_ITEM / UNEQUIP_ITEM / DISCARD_ITEM / SHOP_SELL_ITEM | `{"playerItemId":801}`；装备替换另传replacePlayerItemId |
| REFRESH_QUEST_OFFERS | `{}` |
| SUBMIT_QUEST_ITEM | `{"questId":142,"playerQuestId":402,"playerItemId":801}` |
| PAY_QUEST_RESOURCE | `{"questId":107,"playerQuestId":403}` |
| QUEST_SOCIAL_CHECK / QUEST_FULL_SOCIAL_CHECK | `{"questId":149,"playerQuestId":401,"attributeType":"SPEED"}`，按任务规则选择行动 |
| SHOP_BUY_ITEM | `{"shelfSlotId":31}` |
| SHOP_SELL_ITEM | `{"playerItemId":801}` |
| SHOP_REFRESH | `{}` |
| RESOLVE_TURN_START_EFFECT | `{"rewardId":71,"cardId":901}` 或 `{"rewardId":71,"abandon":true}`，先检查队首能力 |

统一外层：`{"requestId":"每次新业务操作的UUID","playerId":1001,"stateVersion":8,"actionType":"END_TURN","payload":{}}`。
上述ID和版本都是示例，必须取实时API；完整25种后端行动见主文档第9节。装备／使用／卸下／丢弃及任务详情已列入本次客户端接入，非新增后端封禁；Boss、调查、偷窃等仍遵守既有准入限制。

## 五项必须同步的规则

1. 房主不准备，普通成员ready无请求体；开始按钮调用start-character-selection。所有人select-character后仍等服务端截止自动开局，旧/rooms/{roomId}/start已不存在。
2. 进入对局读player-view确定自己的playerId，立即以JSON clientInstanceId建连，之后按默认5秒用clientInstanceId＋connectionId续期。只传connected=true不够；GET和登录不续期。
3. 首次宽限45秒、租约15秒、离线等待10秒；ALL_OFFLINE暂停由任一有效重连解除。用engineState.pausedReason判断，不匹配中文reasonText。
4. 公共任务板与货架任何位置可看，接取／刷新／买卖才检查地点。每次打开都GET；任务候选用quest-offers，已接任务用quests，公共货架用shop/current。
5. 任务／打工／招募准备不传骰点；reroll只申请授权，最终roll传requestId、stateVersion、authorizationRound和JSON整数dice(1～6)。网络重试原ID原请求体；新操作新ID。

## 实现依据与文档边界

路径和方法已对照D:\ES当前39个Controller路由；CORE_9语义按EngineGameFacade、EngineQueries、连接／阶段／投骰实现核对。完整请求、响应、错误和验证范围以主文档为准。旧映射、重构和历史同步文档保留参考，不再作为当前接口契约。

## 已部署移动契约

USE_RAILWAY逐站提交，每次1金币＋1行动值、无免票；普通移动不再免费利用铁路边。森林按出发格决定方向骰，支持既有重投和固定骰点，非法偏移原地扣资源。以新鲜movementRulesVersion=2识别；不能从旧回执推定当前资格。

专项第8节是movable-cells模式、FOREST_MOVE和movement扩展的最终契约，前1～7节仅保留历史草案，不采用其中movementContext建议。既有URL不增加重复入口。

需要用户决策的点：本轮未发现新增玩法项。本次整理不修改服务端规则或客户端代码，玩法/UI实际联调另行验收。
