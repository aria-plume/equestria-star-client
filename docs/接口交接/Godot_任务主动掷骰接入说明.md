# Godot 主动投骰接入说明

## 2026-11-04 普通任务主动提交（已部署，客户端待联调）

本目录是统一交接入口，见[索引](README.md)。本次道具与任务详情接入见[专项](道具系统与任务详情接入交接.md)。任务最终判定成功可进入READY_TO_SUBMIT，并不自动发普通任务奖励，结算页等SUBMIT_QUEST响应。

EVENT_V2已开放普通任务达标仅进入READY_TO_SUBMIT，不自动发奖/弃牌；需本人MIDDLE通过actions提交SUBMIT_QUEST，payload仅playerQuestId，沿用requestId和stateVersion。END达标下一个人回合可提交。
以新鲜player-view的engineState.questSubmissionVersion=1判断能力，不使用movementRulesVersion。查询及questUpdates的submission给出canSubmit、最早个人回合和冻结预览；questSettlement单列实际收益及余额。
先展示结算页，其确认仅打开现有FIFO pendingReward，不再次提交。旧历史回执不改写，Godot源码未修改。
本节优先于下文普通任务自动领奖旧说明。完整DTO和流程见[普通任务主动提交与结算页面交接](普通任务主动提交与结算页面交接.md)。

更新时间：2026-11-03。已部署部分适用 `EVENT_V2 / CORE_9_PUBLIC_SHOP`。主契约见 [ES完整API文档](ES完整API文档.md) 第9～11节。本文替代旧版“失败后由/roll返回另一次WAITING_ROLL”的重投时序。

> 森林FOREST_MOVE已部署并保留在11月4日版本；[专项第8节](铁路与森林移动_待开发接口交接.md#8-最终接口契约)覆盖第11节历史草案。新鲜movementRulesVersion=2才支持。普通任务主动提交另看questSubmissionVersion=1，不改变WORK/招募结算语义；客户端源码本轮未修改。

森林不是属性判定：pendingCheck的attributeType/attributeValue/modifier/difficulty均null，方向快照在preparation，不存在movementContext。准备只扣一次1行动值，速度到0仍可确认；不提前位移。既有甜贝儿先免费再付1社交、同个人回合同check链及120秒授权期限保持；固定6就是右偏。最终roll.checkResult=null，submittedDice/effectiveDice为整数，movement返回实际落点或STAYED。仅最终上传1～6点，重投不上传中间点数；按服务端实际位置更新，不自行叠六维或比较难度。

## 1. 谁决定什么

- 前端：物理骰子表现、取得当前骰点、玩家选择确认或申请重投。
- 后端：准备业务及属性／难度快照、检查重投资格与扣费、校验授权、计算最终值及成功与否、唯一结算。
- 最终确认前不上传中间骰点。重投请求不提交点数；授权成功后再掷新骰，只能确认最后一次。
- 普通玩家默认没有重投；成功的点数也可在有资格时重投。只传最终点数不具备完整防作弊保证。

## 2. 前置流程

必须先登录，并用active-session定位当前gameId，再用player-view匹配自身局内playerId，接入connection心跳。等待骰子动画／玩家选择时心跳仍按默认5秒持续调用；GET与/actions不能代替它。

## 3. 准备判定

~~~http
POST /api/v1/games/100/actions
Authorization: Bearer <accessToken>
Content-Type: application/json

{"requestId":"914763df-952e-4fc4-b65a-31fb345b6de3","playerId":1001,"stateVersion":8,
 "actionType":"QUEST_SOCIAL_CHECK","payload":{"questId":149,"playerQuestId":401,"attributeType":"SPEED"}}
~~~

- QUEST_SOCIAL_CHECK／QUEST_FULL_SOCIAL_CHECK：按任务实际规则选择，attributeType只选允许的属性，建议携带playerQuestId防止实例变化。
- WORK：payload为`{"attributeType":"SOCIAL"}`等允许的具体属性。`work/current.attributeType=ANY`时先让玩家从`STRENGTH`、`SPEED`、`SOCIAL`、`CHARM`、`MAGIC`、`MIND`中明确选择；固定属性不弹选择层，任何路径都不得把`ANY`提交给/actions。
- DRAW_PARTNER_OFFER：payload为`{}`。
- 不传dice、success、finalValue、difficulty、modifier或自定奖励。

成功data是GameActionResponse，`resultType=CHECK_ROLL_PENDING`，`data.pendingCheck` 是准备快照。费用在准备阶段按规则扣除，但判定成功／失败、打工收入或伙伴抽牌还未结算。

## 4. 保存待判定快照

重点字段：checkId、status、businessType、attributeType、attributeValue、modifier、difficulty、stateVersion、authorizationRound、expiresAt、canConfirm、canReroll、nextRerollSocialCost、rerollUnavailableReason、fixedDice及rerollChain。

businessType为QUEST／WORK／PARTNER_RECRUIT。新授权常为0，但应取响应，不自行硬编码。canConfirm与canReroll是不同能力，不要因不能重投而禁用确认。

## 5. 玩家申请重投

~~~http
POST /api/v1/games/100/checks/501/reroll
Authorization: Bearer <accessToken>
Content-Type: application/json

{"requestId":"764ee9f6-5964-48ef-bf6c-c003795c5948","stateVersion":9,"authorizationRound":0}
~~~

成功data直接是更新后的PendingCheckVO，不是GameActionResponse；保存新的stateVersion、authorizationRound、expiresAt和资源／资格后，再触发新的物理掷骰。禁止附加dice等未知字段。

甜贝儿当前规则：有效伙伴每个个人回合最多绑定同一个check的一条重投链，先免费一次，再可支付1社交一次；未用付费机会不能用于另一check。按钮按服务端canReroll、nextRerollSocialCost及原因控制，不由客户端自行认定扣费成功。每次成功授权重新给予120秒，按新expiresAt展示。

每次新重投授权用新requestId；同一次网络重试保留原ID和原请求体，不能重复消耗次数和资源。

## 6. 玩家最终确认

~~~http
POST /api/v1/games/100/checks/501/roll
Authorization: Bearer <accessToken>
Content-Type: application/json

{"requestId":"e16540f6-7a6b-4074-87c2-ab798b468e24","stateVersion":10,"authorizationRound":1,"dice":5}
~~~

只传最终dice（JSON整数1～6）和当前授权／版本／请求ID。不提交判定属性或最终值；属性已在准备时冻结。不能传旧准备版本来确认已经重投后的骰子。

成功data为CheckRollResponse：
~~~json
{
  "pendingCheck":{"checkId":501,"status":"COMPLETED","authorizationRound":1},
  "submittedDice":5,"effectiveDice":5,
  "checkResult":{"dice":5,"attributeType":"SPEED","attributeValue":3,"modifier":0,"finalValue":8,"difficulty":7,"success":true},
  "questUpdates":[],"playerViewState":{"gameId":100,"stateVersion":11},
  "playerFacingMessage":"任务判定成功。","idempotentReplay":false,
  "logs":[],"deckDraws":[]
}
~~~

上述是QUEST任务业务示例，视图和pendingCheck已截断；非WORK的workResult及无招募候选的partnerOfferId按NON_NULL省略，不是保证为null。渲染服务端effectiveDice及checkResult；固定点数效果可令submittedDice与effectiveDice不同。显示questUpdates和最新playerViewState，不自己发奖；WORK详细结构见下节，招募读partnerOfferId及实际pendingReward。049失败仍保留已有目标，不能清空全部进度。

### 6.1 WORK结果页：不能把准备预览或余额当成本次奖励

WORK的正常最终/roll无论判定成功还是失败，都含完整 `data.workResult`。失败时rewardCoins明确为0，并非省略整个对象。完整成功／失败外层响应见 [ES完整API文档](ES完整API文档.md) 第10.5～10.10节；下方仅为非敏感结构示例，不是真实玩家数据：

~~~json
{
  "socialCost":2,
  "remainingSocial":2,
  "rewardCoins":2,
  "coinsAfter":7,
  "checkLog":{
    "dice":5,"attributeType":"MAGIC","attributeTypeText":"魔法",
    "attributeValue":3,"modifier":0,"finalValue":8,"difficulty":7,"success":true
  },
  "forceEndTurn":false
}
~~~

| 字段 | JSON类型 | 结果页含义 |
|---|---|---|
| socialCost | number（整数） | 准备时已扣的WORK基础费用，不含后来重投费用，不再次扣除 |
| remainingSocial | number（整数） | 最终确认反应链处理后的剩余社交点 |
| rewardCoins | number（整数） | 本次打工本身实际所得，失败为0；已包含适用打工加成，不含另一次任务奖励 |
| coinsAfter | number（整数） | 结算链结束后金币总余额，不是本次收入 |
| checkLog | object | 与同一响应data.checkResult值一致，含dice、attributeType、attributeTypeText、attributeValue、modifier、finalValue、difficulty、success |
| forceEndTurn | boolean | 当前CORE_9构造固定false；实际阶段／待办仍看playerViewState |

上述字段在正常已结算WORK路径均非空；Java包装类型可空不代表缺字段就是0。不要虚构workResult.success、income、coinsBefore、fallbackUsed等内部字段。

- **成功／失败**：读取data.checkResult.success或workResult.checkLog.success，不用code=1、COMPLETED或中文文案判断。
- **本次获得金币**：读取workResult.rewardCoins；work/current.rewardCoins只是工作配置的基础成功奖励预览。
- **最终余额**：读取workResult.coinsAfter与同一playerViewState中自己的coins；可能包含任务等其他收益，不用前后余额差推算打工所得。
- **没有收入**：已结算WORK且rewardCoins=0，显示0金币／无打工收入。请求错误data=null、取消、非WORK缺workResult不是同一情况。
- **固定骰点**：submittedDice可与effectiveDice不同，checkResult.dice和workResult.checkLog.dice取有效点数；这不是系统代掷，普通确认fallbackUsed仍false。
- **取消**：不结算WORK收入；/roll是错误响应而非一个成功且rewardCoins=0的WorkResult。
- **幂等重放**：原requestId返回首次保存的结果、余额和版本；当前idempotentReplay也保持首次false，不能靠它去重展示，不覆盖更高版本UI。
- **系统代掷／离线兜底**：后台会实际结算，但不主动推送CheckRollResponse。当前没有按checkId查询已完成WORK完整workResult的接口；GET pending=null仅表示没有未决判定，不能区分成功、失败、取消或代掷，也不能重建该次精确rewardCoins。
- **附加待办**：WORK直接入金币，不另弹“领打工金币”。相关任务反应链可能另产生questUpdates、deckDraws或playerViewState.engineState.pendingReward；有队首先按FIFO处理，不能重复领取workResult.rewardCoins或直接结束回合。没有抽牌时CORE_9的deckDraws通常为[]；少抽提示不是重试WORK的理由。

普通WORK确认和原请求重放已有完整展示数据；“系统代掷／完成后重连也能恢复单次精确收益”的查询能力当前缺失，如需补齐需用户另行确认，本次只记录现状，不新增接口或改变规则。

## 7. 超时和取消

初始期限及成功授权后的期限当前为120秒，以expiresAt为准。期限到达后，新的重投授权返回CHECK_DEADLINE_EXPIRED；但未结算、上下文合法的最终确认仍可与系统代掷竞争锁，只结算一次。本地倒计时归零不等于服务端已经COMPLETED。

服务端取消损坏或失效判定时会保存CANCELLED及原因；客户端收到CHECK_CANCELLED等错误后显示msg并刷新，不自行退款或补奖。当前GET pending不返回已取消记录，不能假设刷新后必能取得cancellationReason；同样不能用data=null推断打工成功或失败，详见第6.1节。离线兜底按现有后端流程处理；前端不新增托管、跳回合或退出规则。

## 8. 断线、响应丢失和重连

~~~http
GET /api/v1/games/100/checks/pending
Authorization: Bearer <accessToken>
~~~

成功data=null表示没有WAITING_ROLL，不是请求失败。恢复认证和心跳后刷新player-view与pending-check，按真实状态及能力恢复界面；客户端丢失的中间物理骰点未上传，不能从后端恢复出来。

actions／reroll／roll各自网络重试保留原requestId、路径与完整原请求体。新业务操作才生成新ID，不能为“刷新版本”修改旧ID的请求内容。同一requestId用于其他checkId会返回REQUEST_ID_CONFLICT。历史重放不可覆盖当前更高版本UI。

## 9. 错误码

| errorCode | 处理 |
|---|---|
| CHECK_ROLL_PENDING | 先恢复已有判定，不再创建普通行动 |
| CHECK_AUTHORIZATION_EXPIRED / CHECK_STATE_CONFLICT | 刷新当前授权及上下文 |
| GAME_STATE_VERSION_EXPIRED | 刷新；新操作新ID，网络未知结果原请求重试 |
| CHECK_REROLL_UNAVAILABLE / RESOURCE_NOT_ENOUGH | 禁用当前重投，显示服务端原因 |
| CHECK_DEADLINE_EXPIRED | 不再重投授权，刷新确认／系统结算状态 |
| CHECK_CANCELLED / CHECK_NOT_FOUND | 结束旧判定UI并刷新 |
| REQUEST_IN_PROGRESS | 原请求稍后重试 |
| REQUEST_ID_CONFLICT | 核对首次操作，不盲目新ID重复扣费 |
| CONNECTION_EXPIRED / CONNECTION_CLOSED | 新clientInstanceId重连，恢复版本 |
| AUTH_REQUIRED / AUTH_TOKEN_EXPIRED | 先恢复认证，不把认证失败当作心跳成功 |

## 10. 验证边界

接口依据为当前EngineCheckFacade、EngineRerolls、PendingCheckVO及CORE_9准入。本次只更新说明，不修改骰子场景或C#，不发公网准备、重投、确认或建连请求，不代表Godot真实物理投骰已验收。

需要用户决策的点：本轮未发现阻断本次文档更新的事项。代掷／完成后重连时恢复单次精确收益的查询能力已按用户要求暂缓，不在本批开发范围，本次未新增接口。

## 11. 森林方向骰（历史草案，已由专项第8节替代）

已确认的完整规则及技术草案见 [铁路与森林移动待开发交接](铁路与森林移动_待开发接口交接.md)。

- 预计复用actions准备、checks/pending、reroll、roll，不新增第二套投骰接口；具体字段在后端验收后定稿。
- 出发格属于森林区域时，普通移动先选择相邻合法目标，再准备方向骰；外部进入森林直接移动。森林建筑格同样参与区域判断。
- 2～5到所选目标；1为面向目标左偏一方向，6为右偏一方向，摄像机旋转不改变结果。偏移目标非法就原地停留，仍扣本次行动值。
- 这是方向骰，不加六维属性，不与难度比较，不用checkResult.success解释移动结果；技术草案业务类型为FOREST_MOVE，最终响应新增movement。
- 允许既有甜贝儿重投链，仍是每个人回合一条、绑定同一次check、先免费一次再可花1社交一次。移动准备只扣一次行动值，重投不重复扣移动费用。
- 固定骰点适用。客户端点数与effectiveDice不一致时，以后者计算偏移；固定6就是右偏。固定效果按既有准备冻结规则处理，不重新消耗。
- 最终到达及原地停留均以服务端回执为准；原地停留不生成ARRIVE_CELL。方向骰正常结算但没有位移，不等于HTTP请求失败。
- 当前服务尚无FOREST_MOVE准备/结算分支，不能直接把现有任务check改业务类型后调用；不在本次文档工作中修改Godot代码或真实对局。
