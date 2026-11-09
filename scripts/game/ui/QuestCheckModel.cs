using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public sealed record QuestCheckRowView(
    string AttributeType,
    bool IsChoice,
    IReadOnlyList<string> ChoiceAttributes,
    int? Difficulty,
    bool Completed,
    bool CanExecute,
    string UnavailableReason);

public sealed record QuestCheckTaskView(
    long QuestId,
    long PlayerQuestId,
    string QuestName,
    string ActionType,
    string TriggerType,
    bool ReadyToSubmit,
    IReadOnlyList<QuestCheckRowView> Rows);

/// <summary>
/// 将任务快照、玩家视图和行动资格合并为可测试的判定列表模型。
/// 服务端仍是最终权威；本模型只负责阻止明显过期或不完整的提交。
/// </summary>
public static class QuestCheckModel
{
    public const string SocialCheck = "SOCIAL_CHECK";
    public const string FullSocialCheck = "FULL_SOCIAL_CHECK";
    public const string SocialCheckAction = "QUEST_SOCIAL_CHECK";
    public const string FullSocialCheckAction = "QUEST_FULL_SOCIAL_CHECK";
    public const string SupportedBatchId = "CORE_9_PUBLIC_SHOP";

    private static readonly string[] AttributeOrder =
    [
        "STRENGTH", "SPEED", "SOCIAL", "CHARM", "MAGIC", "MIND"
    ];

    public static IReadOnlyList<QuestCheckTaskView> BuildTasks(
        IEnumerable<PlayerQuestDto>? quests,
        PlayerViewResponseDto? playerView,
        PlayerViewPlayerDto? player,
        IReadOnlyList<AvailableActionDto>? availableActions,
        long availableActionsStateVersion,
        bool connectionReady,
        bool writeBusy)
    {
        var result = new List<QuestCheckTaskView>();
        if (quests == null)
        {
            return result;
        }

        foreach (var quest in quests)
        {
            var trigger = Normalize(quest.TriggerType);
            if (trigger is not (SocialCheck or FullSocialCheck))
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

    public static bool TryCreateRequest(
        QuestCheckTaskView task,
        string attributeType,
        long playerId,
        long stateVersion,
        string requestId,
        out GameActionRequestDto request,
        out string error)
    {
        request = new GameActionRequestDto();
        if (task.QuestId <= 0 || task.PlayerQuestId <= 0 || playerId <= 0 || stateVersion < 0)
        {
            error = "任务、玩家或版本身份无效。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestId)
            || !TryNormalizeAttribute(attributeType, out var normalizedAttribute)
            || task.Rows.All(row => row.IsChoice
                ? !row.ChoiceAttributes.Contains(normalizedAttribute, StringComparer.OrdinalIgnoreCase)
                : !string.Equals(row.AttributeType, normalizedAttribute, StringComparison.OrdinalIgnoreCase)))
        {
            error = "未选择任务允许的具体判定属性。";
            return false;
        }

        request = new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = playerId,
            StateVersion = stateVersion,
            ActionType = task.ActionType,
            Payload = new JsonObject
            {
                ["questId"] = task.QuestId,
                ["playerQuestId"] = task.PlayerQuestId,
                ["attributeType"] = normalizedAttribute
            }
        };
        error = "";
        return true;
    }

    public static string AttributeName(string attributeType) => Normalize(attributeType) switch
    {
        "STRENGTH" => "力量",
        "SPEED" => "速度",
        "SOCIAL" => "社交",
        "CHARM" => "魅力",
        "MAGIC" => "魔法",
        "MIND" => "心智",
        _ => string.IsNullOrWhiteSpace(attributeType) ? "未知属性" : attributeType.Trim()
    };

    public static bool TryNormalizeAttribute(string? attributeType, out string normalized)
    {
        normalized = Normalize(attributeType);
        return AttributeOrder.Contains(normalized, StringComparer.Ordinal);
    }

    public static bool TryValidatePendingQuest(
        PendingCheckDto? source,
        long gameId,
        long? playerId,
        long? expectedPlayerQuestId,
        long? expectedQuestId,
        out PendingCheckDto pending,
        out string error)
    {
        pending = source!;
        if (source == null
            || source.CheckId is not > 0
            || source.StateVersion == null
            || source.AuthorizationRound == null
            || source.BusinessSourceId is not > 0
            || source.QuestId is not > 0
            || !string.Equals(source.BusinessType, "QUEST", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.Status, "WAITING_ROLL", StringComparison.OrdinalIgnoreCase))
        {
            error = "服务端任务待判定缺少必要身份、版本或状态字段。";
            return false;
        }
        if (source.GameId is > 0 && source.GameId != gameId)
        {
            error = "服务端返回了其他对局的任务待判定。";
            return false;
        }
        if (playerId.HasValue && source.PlayerId is > 0 && source.PlayerId != playerId)
        {
            error = "服务端返回了其他玩家的任务待判定。";
            return false;
        }
        if (expectedPlayerQuestId.HasValue && source.BusinessSourceId != expectedPlayerQuestId)
        {
            error = "任务待判定的 businessSourceId 与任务实例不匹配。";
            return false;
        }
        if (expectedQuestId.HasValue && source.QuestId != expectedQuestId)
        {
            error = "任务待判定的 questId 与本次任务不匹配。";
            return false;
        }
        error = "";
        return true;
    }

    private static QuestCheckTaskView BuildTask(
        PlayerQuestDto quest,
        string trigger,
        PlayerViewResponseDto? playerView,
        PlayerViewPlayerDto? player,
        IReadOnlyList<AvailableActionDto>? actions,
        long actionStateVersion,
        bool connectionReady,
        bool writeBusy)
    {
        var actionType = trigger == FullSocialCheck ? FullSocialCheckAction : SocialCheckAction;
        var readyToSubmit = IsReadyToSubmit(quest);
        var commonReason = BuildCommonBlockReason(
            quest,
            actionType,
            playerView,
            player,
            actions,
            actionStateVersion,
            connectionReady,
            writeBusy,
            readyToSubmit);
        var allowedAttributes = ParseAttributes(quest.CheckAttributeTypes);
        var hasRequiredAttributeList = !string.IsNullOrWhiteSpace(quest.RequiredSuccessAttributes);
        var requiredAttributes = ParseAttributes(quest.RequiredSuccessAttributes);
        var rows = new List<QuestCheckRowView>();

        QuestCheckRowView BuildAttributeRow(QuestCheckAttributeDto checkAttribute, string attribute)
        {
            var known = TryNormalizeAttribute(attribute, out var normalizedAttribute);
            var completed = checkAttribute.Completed == true;
            var rowReason = completed
                ? "该属性已经判定成功。"
                : FirstNonEmpty(
                    checkAttribute.UnavailableReason,
                    checkAttribute.Eligible == true && checkAttribute.Available == true ? commonReason : null,
                    checkAttribute.Eligible != true || checkAttribute.Available != true
                        ? "该属性的服务端判定资格不可用。"
                        : null,
                    known ? commonReason : "服务端返回了未知判定属性。");
            var executable = known
                && !completed
                && checkAttribute.Eligible == true
                && checkAttribute.Available == true
                && string.IsNullOrWhiteSpace(commonReason);
            return new QuestCheckRowView(
                known ? normalizedAttribute : Normalize(attribute),
                false,
                Array.Empty<string>(),
                quest.DifficultyLevel,
                completed,
                executable,
                executable ? "" : rowReason);
        }

        if (hasRequiredAttributeList)
        {
            if (requiredAttributes.Count == 0)
            {
                rows.Add(new QuestCheckRowView(
                    "",
                    false,
                    Array.Empty<string>(),
                    quest.DifficultyLevel,
                    false,
                    false,
                    "服务端返回了无法识别的必需成功属性清单。"));
            }
            else
            {
                foreach (var requiredAttribute in requiredAttributes)
                {
                    var checkAttribute = quest.CheckAttributes?.FirstOrDefault(candidate =>
                        string.Equals(Normalize(candidate.AttributeType), requiredAttribute, StringComparison.Ordinal));
                    if (checkAttribute == null)
                    {
                        rows.Add(new QuestCheckRowView(
                            requiredAttribute,
                            false,
                            Array.Empty<string>(),
                            quest.DifficultyLevel,
                            false,
                            false,
                            "该属性的逐项判定状态尚未同步，不能猜测完成或可执行状态。"));
                        continue;
                    }
                    rows.Add(BuildAttributeRow(checkAttribute, requiredAttribute));
                }
            }
        }
        else if (quest.CheckAttributes is { Count: > 0 })
        {
            foreach (var checkAttribute in quest.CheckAttributes)
            {
                rows.Add(BuildAttributeRow(checkAttribute, Normalize(checkAttribute.AttributeType)));
            }
        }
        else if (allowedAttributes.Count == 1)
        {
            var executable = string.IsNullOrWhiteSpace(commonReason);
            rows.Add(new QuestCheckRowView(
                allowedAttributes[0],
                false,
                Array.Empty<string>(),
                quest.DifficultyLevel,
                false,
                executable,
                executable ? "" : commonReason));
        }
        else if (allowedAttributes.Count > 1)
        {
            var executable = string.IsNullOrWhiteSpace(commonReason);
            rows.Add(new QuestCheckRowView(
                "",
                true,
                allowedAttributes,
                quest.DifficultyLevel,
                false,
                executable,
                executable ? "" : commonReason));
        }
        else
        {
            rows.Add(new QuestCheckRowView(
                "",
                false,
                Array.Empty<string>(),
                quest.DifficultyLevel,
                false,
                false,
                "服务端未返回可用的具体判定属性。"));
        }

        return new QuestCheckTaskView(
            quest.QuestId ?? 0,
            quest.PlayerQuestId ?? 0,
            FirstNonEmpty(quest.QuestName, quest.QuestCode, "未命名任务"),
            actionType,
            trigger,
            readyToSubmit,
            rows);
    }

    private static string BuildCommonBlockReason(
        PlayerQuestDto quest,
        string actionType,
        PlayerViewResponseDto? playerView,
        PlayerViewPlayerDto? player,
        IReadOnlyList<AvailableActionDto>? actions,
        long actionStateVersion,
        bool connectionReady,
        bool writeBusy,
        bool readyToSubmit)
    {
        if (quest.QuestId is not > 0 || quest.PlayerQuestId is not > 0)
        {
            return "任务身份字段无效。";
        }
        if (readyToSubmit)
        {
            return "任务目标已达标，请在个人任务中提交结算。";
        }
        if (!string.Equals(quest.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            return FirstNonEmpty(quest.StatusText, "当前任务状态不能继续判定。");
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
        if (!string.Equals(playerView.EngineState?.BatchId, SupportedBatchId, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(playerView.EngineState?.BatchId)
                ? "对局批次尚未同步。"
                : "当前对局批次尚未接入独立任务判定。";
        }
        if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return "当前对局阶段不能准备任务判定。";
        }
        if (string.Equals(playerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            return "对局处于全员离线暂停状态。";
        }
        if (playerView.EngineState?.PendingCheck != null)
        {
            return "已有待完成判定。";
        }
        if (playerView.CurrentTurnUserId != player.UserId)
        {
            return "当前不是你的回合。";
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
            return "正在同步任务判定资格。";
        }
        var action = actions.FirstOrDefault(candidate =>
            string.Equals(candidate.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
        if (action == null)
        {
            return "服务端未返回对应任务判定资格。";
        }
        if (!action.Available)
        {
            return FirstNonEmpty(action.ReasonText, action.Reason, "当前不能执行该任务判定。");
        }

        if (string.Equals(quest.TriggerType, FullSocialCheck, StringComparison.OrdinalIgnoreCase))
        {
            if (player.EffectiveAttributes == null)
            {
                return "有效六维尚未同步。";
            }
            if (player.CurrentSocialPoints != player.EffectiveAttributes.Social)
            {
                return "当前社交点必须保持满值。";
            }
        }
        else
        {
            if (quest.RequiredSocialCost == null)
            {
                return "任务社交费用尚未同步。";
            }
            if (player.CurrentSocialPoints < quest.RequiredSocialCost.Value)
            {
                return $"社交点不足，需要 {quest.RequiredSocialCost.Value} 点。";
            }
        }

        return "";
    }

    private static List<string> ParseAttributes(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(attribute => AttributeOrder.Contains(attribute, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(attribute => Array.IndexOf(AttributeOrder, attribute))
            .ToList();
    }

    private static bool IsReadyToSubmit(PlayerQuestDto quest) =>
        string.Equals(quest.Status, "READY_TO_SUBMIT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(quest.Status, "READY_TO_SETTLE", StringComparison.OrdinalIgnoreCase)
        || quest.Submission?.CanSubmit == true;

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return "";
    }
}
