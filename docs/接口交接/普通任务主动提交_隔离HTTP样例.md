# 普通任务主动提交：隔离HTTP真实样例

后端随后已于2026-11-04部署，但本文件仍只记录当日隔离MySQL实际HTTP返回data的字段节选，不是公网响应或Godot验收，也没有直接修改运行位置、进度或金币。交接入口见[索引](README.md)。
注册、选角、移动、任务接取、投骰及提交均走认证HTTP，持续真实心跳。完整原始回执保留在D:/ES/target/verification-quest-submit/中，账号令牌不列入样例。

## 014 在END达成，下一个个人回合提交

{"requests":283,"heartbeatRequests":14,"independentRestartJvms":5}

```json
{
  "achieved014": {
    "playerQuestId": 3,
    "questId": 114,
    "status": "READY_TO_SUBMIT",
    "progress": 2,
    "targetCount": 2,
    "objectives": [
      {
        "objectiveKey": "OFFICIAL_QUEST_014:MAIN",
        "text": "在市政厅完整停留两个个人回合（接取回合不计，实际离开清零）",
        "completed": true,
        "currentValue": 2,
        "requiredValue": 2,
        "unit": "完整个人回合",
        "successful": null,
        "stateAvailable": true
      }
    ],
    "conditions": [
      {
        "conditionKey": "CURRENT_LOCATION",
        "text": "当前位于接取后核定的市政厅格",
        "satisfied": true
      },
      {
        "conditionKey": "EXCLUDE_ACCEPTING_TURN",
        "text": "接取任务的个人回合不计",
        "satisfied": true
      },
      {
        "conditionKey": "COMPLETE_STAY_ELIGIBLE",
        "text": "本个人回合从进入时起未离开市政厅，等待结束核验",
        "satisfied": false
      }
    ],
    "submission": {
      "protocolVersion": 1,
      "targetsLocked": true,
      "achievedPersonalTurn": 5,
      "earliestSubmitPersonalTurn": 6,
      "canSubmit": false,
      "unavailableReason": "NOT_OWN_TURN",
      "rewardPreview": {
        "coins": 0,
        "prestige": 2,
        "partnerCoins": 0,
        "itemCount": 0
      },
      "rewardPaid": null
    }
  },
  "submitted014": {
    "gameId": 3,
    "oldStateVersion": 15,
    "newStateVersion": 16,
    "actionType": "SUBMIT_QUEST",
    "resultType": "SUBMIT_QUEST",
    "resultTypeText": "任务已提交结算",
    "questSettlement": {
      "playerQuestId": 3,
      "questId": 114,
      "coinsGranted": 0,
      "prestigeGranted": 2,
      "partnerBonusCoins": 0,
      "coinsAfter": 0,
      "prestigeAfter": 2,
      "pendingItemRewardCount": 0
    },
    "questUpdates": [
      {
        "conditions": [
          {
            "conditionKey": "CURRENT_LOCATION",
            "text": "当前位于接取后核定的市政厅格",
            "satisfied": true
          },
          {
            "conditionKey": "EXCLUDE_ACCEPTING_TURN",
            "text": "接取任务的个人回合不计",
            "satisfied": true
          },
          {
            "conditionKey": "COMPLETE_STAY_ELIGIBLE",
            "text": "本个人回合从进入时起未离开市政厅，等待结束核验",
            "satisfied": false
          }
        ],
        "continuousTask": null,
        "objectiveNotes": [],
        "objectives": [
          {
            "objectiveKey": "OFFICIAL_QUEST_014:MAIN",
            "text": "在市政厅完整停留两个个人回合（接取回合不计，实际离开清零）",
            "completed": true,
            "currentValue": 2,
            "requiredValue": 2,
            "unit": "完整个人回合",
            "successful": null,
            "stateAvailable": true
          }
        ],
        "objectivesStatus": "SUPPORTED",
        "playerQuestId": 3,
        "progress": 2,
        "questId": 114,
        "questName": "萍琪成灾",
        "rewardCoins": 0,
        "rewardItemCount": 0,
        "rewardItemNames": [],
        "rewardPrestige": 2,
        "stateSnapshot": false,
        "status": "COMPLETED",
        "statusText": "已完成",
        "stayState": {
          "targetCellId": 90000101,
          "acceptedPersonalTurn": 1,
          "currentPersonalTurn": 6,
          "observedPersonalTurn": 5,
          "entryCellId": 90000101,
          "eligibleThisTurn": false,
          "eligibilityReason": "QUEST_FINALIZED",
          "lastCountedPersonalTurn": 5,
          "lastProcessedEndPersonalTurn": 5
        },
        "submission": {
          "protocolVersion": 1,
          "targetsLocked": true,
          "achievedPersonalTurn": 5,
          "earliestSubmitPersonalTurn": 6,
          "canSubmit": false,
          "unavailableReason": "QUEST_FINALIZED",
          "rewardPreview": {
            "coins": 0,
            "prestige": 2,
            "partnerCoins": 0,
            "itemCount": 0
          },
          "rewardPaid": {
            "coins": 0,
            "prestige": 2,
            "partnerCoins": 0,
            "pendingItemRewardCount": 0
          }
        },
        "targetCount": 2
      }
    ],
    "deckDraws": []
  },
  "completedAfterRestart": {
    "playerQuestId": 3,
    "status": "COMPLETED",
    "progress": 2,
    "submission": {
      "protocolVersion": 1,
      "targetsLocked": true,
      "achievedPersonalTurn": 5,
      "earliestSubmitPersonalTurn": 6,
      "canSubmit": false,
      "unavailableReason": "QUEST_FINALIZED",
      "rewardPreview": {
        "coins": 0,
        "prestige": 2,
        "partnerCoins": 0,
        "itemCount": 0
      },
      "rewardPaid": {
        "coins": 0,
        "prestige": 2,
        "partnerCoins": 0,
        "pendingItemRewardCount": 0
      }
    }
  }
}
```

提交仍走actions，actionType=SUBMIT_QUEST、payload.playerQuestId；每次新提交使用新requestId和最新stateVersion。同一网络请求重试保持原请求不变。

## 038部分进度、达标和042提交法器

以下为另一局隔离MySQL的实际HTTP data字段节选；SUBMIT_QUEST_ITEM仅达成042，随后SUBMIT_QUEST才入账。两种提交动作不要混淆。

```json
{
  "requests": 352,
  "heartbeatRequests": 10,
  "partial038": {
    "playerQuestId": 3,
    "questId": 138,
    "status": "IN_PROGRESS",
    "progress": 1,
    "targetCount": 3,
    "objectives": [
      {
        "objectiveKey": "OFFICIAL_QUEST_038:MAIN",
        "text": "在方糖屋成功完成3次难度7的任意属性判定（可重复属性）",
        "completed": false,
        "currentValue": 1,
        "requiredValue": 3,
        "unit": "次",
        "successful": null,
        "stateAvailable": true
      }
    ],
    "conditions": [
      {
        "conditionKey": "CURRENT_LOCATION",
        "text": "当前位于方糖屋",
        "satisfied": true
      }
    ],
    "submission": {
      "protocolVersion": 1,
      "targetsLocked": false,
      "achievedPersonalTurn": null,
      "earliestSubmitPersonalTurn": null,
      "canSubmit": false,
      "unavailableReason": "TARGETS_NOT_ACHIEVED",
      "rewardPreview": null,
      "rewardPaid": null
    }
  },
  "achieved038": {
    "playerQuestId": 3,
    "questId": 138,
    "status": "READY_TO_SUBMIT",
    "progress": 3,
    "submission": {
      "protocolVersion": 1,
      "targetsLocked": true,
      "achievedPersonalTurn": 3,
      "earliestSubmitPersonalTurn": 3,
      "canSubmit": true,
      "unavailableReason": null,
      "rewardPreview": {
        "coins": 3,
        "prestige": 1,
        "partnerCoins": 0,
        "itemCount": 0
      },
      "rewardPaid": null
    }
  },
  "settled038": {
    "playerQuestId": 3,
    "questId": 138,
    "coinsGranted": 3,
    "prestigeGranted": 1,
    "partnerBonusCoins": 0,
    "coinsAfter": 3,
    "prestigeAfter": 1,
    "pendingItemRewardCount": 0
  },
  "submittedItem042": {
    "playerQuestId": 4,
    "questId": 142,
    "status": "READY_TO_SUBMIT",
    "progress": 1,
    "objectives": [
      {
        "objectiveKey": "OFFICIAL_QUEST_042:MAIN",
        "text": "在暮光城堡提交1件法器（消耗道具，不发动使用效果）",
        "completed": true,
        "currentValue": 1,
        "requiredValue": 1,
        "unit": "件",
        "successful": null,
        "stateAvailable": true
      }
    ],
    "conditions": [
      {
        "conditionKey": "CURRENT_LOCATION",
        "text": "当前位于暮光城堡",
        "satisfied": true
      }
    ],
    "submission": {
      "protocolVersion": 1,
      "targetsLocked": true,
      "achievedPersonalTurn": 6,
      "earliestSubmitPersonalTurn": 6,
      "canSubmit": true,
      "unavailableReason": null,
      "rewardPreview": {
        "coins": 3,
        "prestige": 2,
        "partnerCoins": 0,
        "itemCount": 0
      },
      "rewardPaid": null
    }
  },
  "settled042": {
    "playerQuestId": 4,
    "questId": 142,
    "coinsGranted": 3,
    "prestigeGranted": 2,
    "partnerBonusCoins": 0,
    "coinsAfter": 6,
    "prestigeAfter": 3,
    "pendingItemRewardCount": 0
  }
}
```

## 052达标、正式提交及满背包待领

购买累计到5金币仅锁定目标；正式提交后3声望入账，道具仍在FIFO等待容量整理和领取。

```json
{
  "ready": {
    "playerQuestId": 3,
    "questId": 152,
    "status": "READY_TO_SUBMIT",
    "progress": 5,
    "targetCount": 5,
    "objectives": [
      {
        "objectiveKey": "OFFICIAL_QUEST_052:MAIN",
        "text": "在水晶集市购买道具累计消费5金币",
        "completed": true,
        "currentValue": 5,
        "requiredValue": 5,
        "unit": "金币",
        "successful": null,
        "stateAvailable": true
      }
    ],
    "conditions": [
      {
        "conditionKey": "CURRENT_LOCATION",
        "text": "当前位于水晶集市商店（购买时必须在此）",
        "satisfied": true
      }
    ],
    "submission": {
      "protocolVersion": 1,
      "targetsLocked": true,
      "achievedPersonalTurn": 6,
      "earliestSubmitPersonalTurn": 6,
      "canSubmit": true,
      "unavailableReason": null,
      "rewardPreview": {
        "coins": 0,
        "prestige": 3,
        "partnerCoins": 0,
        "itemCount": 1
      },
      "rewardPaid": null
    }
  },
  "settled": {
    "playerQuestId": 3,
    "questId": 152,
    "coinsGranted": 0,
    "prestigeGranted": 3,
    "partnerBonusCoins": 0,
    "coinsAfter": 1,
    "prestigeAfter": 3,
    "pendingItemRewardCount": 1
  },
  "playerFacingMessage": "你完成了任务「水晶洗礼」，获得3声望，另有1项道具奖励待选择（尚未领取）。",
  "fullBagPending": {
    "rewardId": 1,
    "playerId": 5,
    "kind": "ITEM",
    "sourceType": "QUEST_REWARD",
    "canExecute": false,
    "executeUnavailableReason": "负重已满，请先弃置旧道具或放弃奖励",
    "canAbandon": true,
    "capacitySatisfied": false,
    "drawResult": {
      "source": "QUEST_REWARD",
      "message": null,
      "deckType": "ITEM",
      "shortage": false,
      "actualCount": 1,
      "drawBatchId": "715e6568-7b5a-4fbe-8037-691abe483397",
      "shortageCode": null,
      "requestedCount": 1
    }
  }
}
```
