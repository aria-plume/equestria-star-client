# ITEM奖励队首与候选准确契约

更新时间：2026-11-05。入口：[交接索引](README.md)。本文件补齐现有接口说明，不新增字段、不修改玩法、服务端或客户端代码。

## 1. 本次结论与证据

后端已经为ITEM候选返回顶层`cardId`和`itemId`；此前交接未完整定义候选结构，是文档缺漏，不是缺少领奖接口。

- 生成：`D:/ES/src/main/java/com/ariaplume/es/game/engine/reward/EngineRewards.java`的itemChoice。
- 查询：`D:/ES/src/main/java/com/ariaplume/es/game/engine/EngineQueries.java`的rewardView。
- 领取：EngineRewards.resolve、acquireItem；统一请求由EngineGameFacade承接。
- 抽牌摘要：`D:/ES/src/main/java/com/ariaplume/es/game/vo/DeckDrawResultVO.java`。
- 完整样例依据：`D:/ES/target/verification-quest-submit/http-052-mysql.json`中的`submitted.playerViewState.engineState.pendingReward`，为此前隔离MySQL真实HTTP结果，不是本轮公网抓取。

部署状态沿用11月4日交付记录；本轮没有重新访问公网、发心跳、提交任务或领取奖励。补齐契约不等于当前Godot奖励闭环已经验收。

## 2. 从哪里读取

- 查询：`GET /api/v1/games/{gameId}/player-view`的`data.engineState.pendingReward`。
- 行动成功回执：`data.playerViewState.engineState.pendingReward`。
- 最终投骰成功回执如含playerViewState，同样从该路径读取，不把骰子结果视作第二次发奖命令。
- 没有队首时pendingReward为null；不存在新建的奖励详情URL。队首属于其他玩家时仍可有对象，但私有candidates为`[]`。
- 当前任务提交成功先展示questSettlement，结算页确认只打开已生成的队首；不要再次调用SUBMIT_QUEST。领取完成后才会生成自己的playerItemId。

## 3. 队首字段（pendingReward对象）

| 字段 | JSON类型/可空 | 含义与处理 |
|---|---|---|
| rewardId | integer，非空 | 当前待办编号；领取/放弃使用，与cardId不同 |
| playerId | integer，非空 | 待办所属局内玩家ID，不是userId |
| kind | string，非空 | 本文只定义ITEM；不能把PARTNER/TREASURE/其他kind强行按ITEM解析 |
| sourceType | string，正常非空 | 来源，任务道具奖励为QUEST_REWARD；不是新的actionType |
| canResolve | boolean，非空 | 是否属于当前查看者；不是“现在可以领取” |
| hasAuthority | boolean，非空 | 当前与canResolve同为归属判断；不能代替canExecute |
| capacitySatisfied | boolean/null | 本人：是否存在容量满足的候选；其他查看者为null，不能当true |
| requiresReplacement | boolean，非空 | ITEM当前为false；伙伴替换语义不适用于道具 |
| canExecute | boolean，非空 | 队首全局领取资格，包括归属、容量、运行状态和待骰等 |
| executeUnavailableReason | string/null | 不能执行的显示说明；可执行时为null，不是稳定枚举码 |
| unavailableReason | string/null | 当前同上，保留兼容；不根据中文文本实现状态机 |
| canAbandon | boolean，非空 | 当前能否放弃；ITEM支持放弃，但仍受对局状态、待骰及归属限制 |
| abandonUnavailableReason | string/null | 放弃不可用说明；允许时为null |
| candidates | array，非null | 本人可见候选，非本人为[]；空数组不能擅自补抽或猜身份 |
| drawResult | object，可缺失 | 仅本人且该待办保存过抽牌摘要时出现；旧待办或其他查看者可没有此字段 |

drawResult字段为`drawBatchId:string`、`deckType:string`、`source:string`、`requestedCount:integer`、`actualCount:integer`、`shortage:boolean`、`shortageCode:string/null`、`message:string/null`。牌库不足时shortageCode为DECK_INSUFFICIENT；摘要不含候选身份，不能用它代替candidates。

## 4. ITEM候选完整外层结构

以下要求限定于本人队首`kind=ITEM`的正常候选，不推广到其他kind。

| 字段 | JSON类型/可空 | 含义与客户端用途 |
|---|---|---|
| cardId | integer，非空、正数 | 本局game_item_card实卡ID，领取提交此值；不要提交itemId或配置id |
| itemId | integer，非空、正数 | 道具配置逻辑ID，用于CardArtCatalog匹配卡图；不是持有实例ID |
| canExecute | boolean，非空 | 本候选自身执行条件；当前ITEM主要为容量。还须同时满足队首canExecute |
| executeUnavailableReason | string/null | 本候选不可执行说明；可执行时null |
| capacitySatisfied | boolean，非空 | 本候选的容量条件是否满足 |
| configuration | object，正常非空 | 本局冻结的道具配置；原始数据库字段snake_case，不是PlayerItemVO |

**候选顶层当前没有`itemName`、`playerItemId`、`location`、`itemTypeText`。**

- 卡图取顶层itemId；名称取`configuration.item_name`，不要强制要求顶层itemName。
- 类型取`configuration.item_type`，不是configuration.itemType。
- configuration.id为道具配置ID，正常等于候选itemId；仍直接用顶层itemId映射卡图，不能用cardId兜底。
- 字段缺失、非正数或配置身份不一致属于异常，保留原始诊断并刷新权威视图，不自动代选/放弃/重新提交任务。
- 后端找不到合法本局配置会返回业务错误，并非正常返回configuration=null。客户端可用可空DTO防御旧数据，但不能把null当正常可领取候选。

### configuration已知字段

这是本局配置原始Map的透传，不是固定字段数的独立VO；客户端忽略不使用的额外字段，不要求所有版本恰好有相同键集。领取仅需rewardId/cardId，不回传整个配置。

| 字段 | 类型/空值 | 用途 |
|---|---|---|
| id | integer，正常非空 | 配置ID，与顶层itemId一致 |
| item_name、item_type | string，正常非空 | 名称、CONSUMABLE/EQUIPMENT |
| item_category、description、item_tag | string/null | 分类、卡面文字、标签；缺失文字可显示占位，不解析文字生成效果 |
| buy_price、sell_price | integer/null | 配置价格，不是本次领取费用或实际收益 |
| attr_strength_delta、attr_speed_delta、attr_social_delta、attr_charm_delta、attr_magic_delta、attr_mind_delta | integer，当前配置非空 | 配置属性增量；不是立即补充的剩余行动点 |
| attribute_duration_type | string/null | 持续规则；装备可无持续回合值，药剂本局规范化为TURN |
| attribute_duration_value | integer/null | 配置持续值，不是当前剩余回合数 |
| special_effect_id、effect_type、effect_attribute | string/null | 原始效果元数据，不代表旧特殊效果已开放 |
| effect_value | integer/null | 原始效果参数 |
| effect_config | string/null | 原始TEXT配置；不要强制当作JSON object |
| purchasable、enabled | integer，当前0/1 | 原始配置标志，不能要求JSON boolean |
| deck_type | string | 原始牌堆分类配置，当前样例SHOP |
| deck_copy_count | integer | 配置实卡数量，不是本次候选数量 |
| source_row | integer/null | 原始配置行信息，客户端无需展示 |
| created_at、updated_at | string，可防御缺失/null | 原始配置时间，不是领奖时间，无需作为领取条件 |
| source_attribute_duration_type、duration_basis、duration_rule | string，可缺失 | 药剂规范化附加元数据，并非每种道具都有 |
| includes_use_turn | boolean，可缺失 | 药剂规范化标志，不是所有道具共同必填字段 |

## 5. 完整ITEM队首JSON（隔离真实HTTP样例）

以下是上述隔离MySQL验收文件中的队首对象，省略外层统一响应与无关player-view字段；对象内部字段完整，键顺序不构成契约。ID、批次号和时间仅是隔离测试记录，不能用于公网请求。该样例特意展示“满包不能领取、但可以放弃”，不是领取成功回执。

```json
{
  "rewardId": 1,
  "playerId": 5,
  "kind": "ITEM",
  "sourceType": "QUEST_REWARD",
  "drawResult": {
    "source": "QUEST_REWARD",
    "message": null,
    "deckType": "ITEM",
    "shortage": false,
    "actualCount": 1,
    "drawBatchId": "715e6568-7b5a-4fbe-8037-691abe483397",
    "shortageCode": null,
    "requestedCount": 1
  },
  "canResolve": true,
  "hasAuthority": true,
  "capacitySatisfied": false,
  "requiresReplacement": false,
  "canExecute": false,
  "executeUnavailableReason": "负重已满，请先弃置旧道具或放弃奖励",
  "unavailableReason": "负重已满，请先弃置旧道具或放弃奖励",
  "canAbandon": true,
  "abandonUnavailableReason": null,
  "candidates": [
    {
      "cardId": 131,
      "itemId": 1017,
      "canExecute": false,
      "executeUnavailableReason": "负重已满，请先弃置旧道具或放弃奖励",
      "capacitySatisfied": false,
      "configuration": {
        "id": 1017,
        "enabled": 1,
        "item_tag": null,
        "buy_price": 2,
        "deck_type": "SHOP",
        "item_name": "药剂",
        "item_type": "CONSUMABLE",
        "created_at": "2026-07-19T18:28:15",
        "sell_price": 1,
        "source_row": 18,
        "updated_at": "2026-07-19T18:28:15",
        "description": "使用后你的团队所有面板数值+1持续两回合",
        "effect_type": null,
        "purchasable": 1,
        "effect_value": null,
        "duration_rule": "POTION_PERSONAL_TURNS_V1",
        "effect_config": null,
        "item_category": "法器",
        "duration_basis": "PERSONAL_TURN",
        "attr_mind_delta": 1,
        "deck_copy_count": 2,
        "attr_charm_delta": 1,
        "attr_magic_delta": 1,
        "attr_speed_delta": 1,
        "effect_attribute": null,
        "attr_social_delta": 1,
        "includes_use_turn": true,
        "special_effect_id": null,
        "attr_strength_delta": 1,
        "attribute_duration_type": "TURN",
        "attribute_duration_value": 2,
        "source_attribute_duration_type": "ROUND"
      }
    }
  ]
}
```

允许领取时以新鲜响应中的队首canExecute和候选canExecute均为true为准；不能在客户端修改上述满包样例后向服务器提交。其他查看者的candidates=[]、capacitySatisfied=null、canResolve/hasAuthority/canExecute/canAbandon=false，不要求其提供私有configuration或drawResult。没有待办时整体pendingReward=null，不是伪造空ITEM待办。

## 6. 领取、放弃与入包

均为`POST /api/v1/games/{gameId}/actions`，Bearer身份、JSON请求、requestId与stateVersion沿用现有契约。结构示意如下，身份与编号并非公网可用值：

```json
{
  "requestId": "fbb955f3-4d4c-40fc-a685-a19d1fc5eeaf",
  "playerId": 1001,
  "stateVersion": 12,
  "actionType": "RESOLVE_TURN_START_EFFECT",
  "payload": {"rewardId": 71, "cardId": 901}
}
```

放弃整批候选使用同一路径和actionType，payload改为`{"rewardId":71,"abandon":true}`，作为新操作使用新requestId。不传itemId、configuration或自定奖励金额。

领取按钮须同时确认：本人队首、kind=ITEM、顶层canExecute=true、该候选canExecute=true、有效cardId/itemId，且本地没有正在提交同一操作。仅canResolve=true不能启用领取；候选canExecute也不能绕过队首对局已结束/待骰等限制。放弃按钮独立取顶层canAbandon。

领取成功后应用回执新版本与playerViewState，再查询`GET /api/v1/games/{gameId}/items?playerId={自己}`获得真实playerItemId；不能把cardId变成playerItemId。队首可能切换到另一奖励或其他玩家，必须按新鲜回执重新路由，不能自行把本地候选全部清空后默认结束。

满包时保留候选展示和合法整理/放弃入口。可读说明按候选原因、队首原因依次展示；当前原因字段是文本，不虚构新增稳定原因码。网络结果不明保留原requestId、路径和完整body重试，不能第二次SUBMIT_QUEST。

## 7. 客户端应核对与验收的内容

当前RewardCandidateDto已有cardId、itemId，但顶层itemName不是后端此结构的正式字段；请增加/复用configuration解析及候选canExecute、executeUnavailableReason、capacitySatisfied，保留未知字段兼容。无需把现有正确的两个ID改成别的名字。

本轮只补契约，未修改客户端DTO或界面。客户端开发应验证：

1. 完整样例反序列化成功：cardId=131，itemId=1017；名称来自configuration.item_name，卡图按1017查找。
2. 满包样例仍显示药剂候选，领取禁用、放弃可用；不因canResolve=true错误启用领取。
3. 本地模拟合法可领取快照时，仅发送rewardId/cardId，不发送itemId；领取后刷新背包，以新playerItemId操作持有物。
4. 其他玩家队首、null队首、缺失drawResult、可空配置值及额外配置字段不会误弹或崩溃。
5. 缺少必要ID时保留异常提示，不相互替代、不自动放弃；候选条件不满足不能仅凭队首可用启用按钮。
6. 重试不重复结算，处理后按新队首继续FIFO，实际Godot交互与公网联调单独记录，不能以文档或样例解析通过替代。

打工回归未完成属于另一个待办，本次不宣称已定位或通过，也不改打工等待时间。

需要用户决策的点：本轮未发现。本次是明确现有契约，不新增玩法或接口。
