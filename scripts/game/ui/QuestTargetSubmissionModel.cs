using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public sealed record QuestTargetSubmissionTaskView(
    long QuestId,
    long PlayerQuestId,
    string QuestName,
    string TriggerType,
    long? RequiredItemId,
    string? RequiredItemCategory,
    int? RequiredCoinCost,
    int? RequiredPrestigeCost,
    int? RequiredSocialCost,
    string RequirementText,
    bool CanExecute,
    string UnavailableReason);

public sealed record QuestTargetItemView(
    long PlayerItemId,
    long ItemId,
    string ItemName,
    string Location,
    bool CanSubmit,
    string UnavailableReason);

/// <summary>
/// 将任务目标、持有实例和权威行动资格合并为可测试的只读提交模型。
/// 客户端只阻止明显过期或不完整的提交，最终结果仍由服务端决定。
/// </summary>
public static class QuestTargetSubmissionModel
{
    public const string SubmitItemTrigger = "SUBMIT_ITEM";
    public const string PayResourceTrigger = "PAY_RESOURCE";
    public const string SubmitItemAction = "SUBMIT_QUEST_ITEM";
    public const string PayResourceAction = "PAY_QUEST_RESOURCE";

    public static IReadOnlyList<QuestTargetSubmissionTaskView> BuildTasks(
        IEnumerable<PlayerQuestDto>? quests,
        PlayerViewResponseDto? playerView,
        PlayerViewPlayerDto? player,
        IReadOnlyList<AvailableActionDto>? availableActions,
        long availableActionsStateVersion,
        bool connectionReady,
        bool writeBusy)
    {
        var result = new List<QuestTargetSubmissionTaskView>();
        if (quests == null)
        {
            return result;
        }

        foreach (var quest in quests)
        {
            var trigger = Normalize(quest.TriggerType);
            if (!string.Equals(quest.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)
                || trigger is not (SubmitItemTrigger or PayResourceTrigger))
            {
                continue;
            }

            result.Add(BuildTask(
                quest,
                trigger,
                playerView,
                player,
                availableActions,
                availableActionsStateVersion,
                connectionReady,
                writeBusy));
        }
        return result;
    }

    public static IReadOnlyList<QuestTargetItemView> BuildMatchingItems(
        QuestTargetSubmissionTaskView task,
        IEnumerable<PlayerItemDto>? items)
    {
        if (task.TriggerType != SubmitItemTrigger || items == null)
        {
            return Array.Empty<QuestTargetItemView>();
        }

        var result = new List<QuestTargetItemView>();
        foreach (var item in items)
        {
            if (item.PlayerItemId is not > 0 || item.ItemId is not > 0 || !Matches(task, item))
            {
                continue;
            }

            var location = Normalize(item.Location);
            if (location is not ("HAND" or "EQUIPPED"))
            {
                continue;
            }
            var hand = location == "HAND";
            var reason = hand
                ? task.UnavailableReason
                : "该实例已装备，不能直接交付。";
            result.Add(new QuestTargetItemView(
                item.PlayerItemId.Value,
                item.ItemId.Value,
                FirstNonEmpty(item.ItemName, $"道具 {item.ItemId.Value}"),
                location,
                hand && task.CanExecute,
                hand && task.CanExecute ? "" : FirstNonEmpty(reason, "当前不能提交该道具。")));
        }
        return result;
    }

    public static bool TryCreateItemRequest(
        QuestTargetSubmissionTaskView task,
        QuestTargetItemView item,
        long playerId,
        long stateVersion,
        string requestId,
        out GameActionRequestDto request,
        out string error)
    {
        request = new GameActionRequestDto();
        if (task.TriggerType != SubmitItemTrigger || !task.CanExecute || !item.CanSubmit)
        {
            error = FirstNonEmpty(item.UnavailableReason, task.UnavailableReason, "当前不能提交该道具。");
            return false;
        }
        if (task.QuestId <= 0 || task.PlayerQuestId <= 0 || item.PlayerItemId <= 0
            || playerId <= 0 || stateVersion < 0 || string.IsNullOrWhiteSpace(requestId))
        {
            error = "任务、道具、玩家或版本身份无效。";
            return false;
        }

        request = new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = playerId,
            StateVersion = stateVersion,
            ActionType = SubmitItemAction,
            Payload = new JsonObject
            {
                ["questId"] = task.QuestId,
                ["playerQuestId"] = task.PlayerQuestId,
                ["playerItemId"] = item.PlayerItemId
            }
        };
        error = "";
        return true;
    }

    public static bool TryCreateResourceRequest(
        QuestTargetSubmissionTaskView task,
        long playerId,
        long stateVersion,
        string requestId,
        out GameActionRequestDto request,
        out string error)
    {
        request = new GameActionRequestDto();
        if (task.TriggerType != PayResourceTrigger || !task.CanExecute)
        {
            error = FirstNonEmpty(task.UnavailableReason, "当前不能支付该任务资源。");
            return false;
        }
        if (task.QuestId <= 0 || task.PlayerQuestId <= 0 || playerId <= 0
            || stateVersion < 0 || string.IsNullOrWhiteSpace(requestId))
        {
            error = "任务、玩家或版本身份无效。";
            return false;
        }

        request = new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = playerId,
            StateVersion = stateVersion,
            ActionType = PayResourceAction,
            Payload = new JsonObject
            {
                ["questId"] = task.QuestId,
                ["playerQuestId"] = task.PlayerQuestId
            }
        };
        error = "";
        return true;
    }

    public static bool Matches(QuestTargetSubmissionTaskView task, PlayerItemDto item)
    {
        if (item.ItemId is not > 0)
        {
            return false;
        }
        if (task.RequiredItemId.HasValue && item.ItemId != task.RequiredItemId)
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(task.RequiredItemCategory)
            && !string.Equals(item.ItemCategory?.Trim(), task.RequiredItemCategory.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    private static QuestTargetSubmissionTaskView BuildTask(
        PlayerQuestDto quest,
        string trigger,
        PlayerViewResponseDto? playerView,
        PlayerViewPlayerDto? player,
        IReadOnlyList<AvailableActionDto>? availableActions,
        long availableActionsStateVersion,
        bool connectionReady,
        bool writeBusy)
    {
        var requirement = trigger == SubmitItemTrigger
            ? BuildItemRequirement(quest)
            : BuildResourceRequirement(quest);
        var reason = BuildCommonBlockReason(
            quest,
            trigger == SubmitItemTrigger ? SubmitItemAction : PayResourceAction,
            playerView,
            player,
            availableActions,
            availableActionsStateVersion,
            connectionReady,
            writeBusy);

        if (string.IsNullOrWhiteSpace(reason) && trigger == PayResourceTrigger)
        {
            reason = BuildResourceBlockReason(quest, player);
        }

        return new QuestTargetSubmissionTaskView(
            quest.QuestId ?? 0,
            quest.PlayerQuestId ?? 0,
            FirstNonEmpty(quest.QuestName, quest.QuestCode, "未命名任务"),
            trigger,
            quest.RequiredItemId,
            string.IsNullOrWhiteSpace(quest.RequiredItemCategory) ? null : quest.RequiredItemCategory.Trim(),
            quest.RequiredCoinCost,
            quest.RequiredPrestigeCost,
            quest.RequiredSocialCost,
            requirement,
            string.IsNullOrWhiteSpace(reason),
            reason);
    }

    private static string BuildCommonBlockReason(
        PlayerQuestDto quest,
        string actionType,
        PlayerViewResponseDto? playerView,
        PlayerViewPlayerDto? player,
        IReadOnlyList<AvailableActionDto>? actions,
        long actionStateVersion,
        bool connectionReady,
        bool writeBusy)
    {
        if (quest.QuestId is not > 0 || quest.PlayerQuestId is not > 0)
        {
            return "任务身份字段无效。";
        }
        if (!connectionReady)
        {
            return "对局连接尚未就绪。";
        }
        if (writeBusy)
        {
            return "正在处理其他业务行动。";
        }
        if (playerView == null || player == null)
        {
            return "玩家状态尚未同步。";
        }
        if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return "当前对局状态不能提交任务目标。";
        }
        if (string.Equals(playerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            return "对局处于全员离线暂停状态。";
        }
        if (!string.IsNullOrWhiteSpace(playerView.EngineState?.Phase)
            && !string.Equals(playerView.EngineState.Phase, "MIDDLE", StringComparison.OrdinalIgnoreCase))
        {
            return "当前服务端阶段不能提交任务目标。";
        }
        if (playerView.EngineState?.PendingReward != null || playerView.EngineState?.PendingCheck != null)
        {
            return "请先处理当前服务端待办。";
        }
        if (playerView.CurrentTurnUserId != player.UserId)
        {
            return "当前不是你的回合。";
        }
        if (playerView.EngineState?.CurrentPlayerId is > 0
            && playerView.EngineState.CurrentPlayerId != player.PlayerId)
        {
            return "当前局内行动玩家尚未同步为自己。";
        }
        if (quest.Conditions == null)
        {
            return "任务条件尚未同步。";
        }
        foreach (var condition in quest.Conditions)
        {
            var key = Normalize(condition.ConditionKey);
            if ((key is "CURRENT_LOCATION" or "WAIT_OWN_TURN") && condition.Satisfied != true)
            {
                return FirstNonEmpty(condition.Text, "当前任务条件尚未满足。");
            }
        }
        if (actionStateVersion != playerView.StateVersion || actions == null)
        {
            return "正在同步任务提交资格。";
        }
        var action = actions.FirstOrDefault(candidate =>
            string.Equals(candidate.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
        if (action == null)
        {
            return "服务端未返回对应任务提交资格。";
        }
        if (!action.Available)
        {
            return FirstNonEmpty(action.ReasonText, action.Reason, "当前不能提交该任务目标。");
        }
        return "";
    }

    private static string BuildResourceBlockReason(PlayerQuestDto quest, PlayerViewPlayerDto? player)
    {
        if (!quest.RequiredCoinCost.HasValue
            || !quest.RequiredPrestigeCost.HasValue
            || !quest.RequiredSocialCost.HasValue)
        {
            return "任务资源费用尚未完整同步。";
        }
        if (quest.RequiredCoinCost < 0 || quest.RequiredPrestigeCost < 0 || quest.RequiredSocialCost < 0)
        {
            return "服务端返回了非法任务资源费用。";
        }
        if (player == null)
        {
            return "玩家资源尚未同步。";
        }
        if (player.Coins < quest.RequiredCoinCost)
        {
            return $"金币不足，需要 {quest.RequiredCoinCost.Value}。";
        }
        if (player.Prestige < quest.RequiredPrestigeCost)
        {
            return $"声望不足，需要 {quest.RequiredPrestigeCost.Value}。";
        }
        if (player.CurrentSocialPoints < quest.RequiredSocialCost)
        {
            return $"社交点不足，需要 {quest.RequiredSocialCost.Value}。";
        }
        return "";
    }

    private static string BuildItemRequirement(PlayerQuestDto quest)
    {
        var parts = new List<string>();
        if (quest.RequiredItemId.HasValue)
        {
            parts.Add($"道具种类 ID {quest.RequiredItemId.Value}");
        }
        if (!string.IsNullOrWhiteSpace(quest.RequiredItemCategory))
        {
            parts.Add($"类别 {quest.RequiredItemCategory.Trim()}");
        }
        return parts.Count == 0 ? "任意手牌道具 1 件" : string.Join(" 且 ", parts) + "，1 件";
    }

    private static string BuildResourceRequirement(PlayerQuestDto quest)
    {
        if (!quest.RequiredCoinCost.HasValue
            || !quest.RequiredPrestigeCost.HasValue
            || !quest.RequiredSocialCost.HasValue)
        {
            return "资源费用尚未完整同步";
        }
        var parts = new List<string>();
        if (quest.RequiredCoinCost.Value > 0) parts.Add($"金币 {quest.RequiredCoinCost.Value}");
        if (quest.RequiredPrestigeCost.Value > 0) parts.Add($"声望 {quest.RequiredPrestigeCost.Value}");
        if (quest.RequiredSocialCost.Value > 0) parts.Add($"社交点 {quest.RequiredSocialCost.Value}");
        return parts.Count == 0 ? "无需支付资源" : string.Join("、", parts);
    }

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return "";
    }
}
