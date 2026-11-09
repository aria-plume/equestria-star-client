using System;
using System.Collections.Generic;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public sealed record QuestObjectiveDisplay(
    string Text,
    bool Completed,
    string ProgressText,
    bool ProgressAvailable
);

public sealed record QuestTaskDisplay(
    long PlayerQuestId,
    long QuestId,
    string QuestName,
    IReadOnlyList<QuestObjectiveDisplay> Objectives,
    bool CanSubmit,
    string SubmitReason,
    bool CanAbandon,
    string AbandonReason
);

public sealed record ItemRewardCandidateDisplay(
    long RewardId,
    long CardId,
    long ItemId,
    string ItemName,
    string ItemType,
    bool CanExecute,
    string ExecuteReason,
    bool CapacitySatisfied
);

public static class QuestSystemModel
{
    public static bool TryBuildTask(
        PlayerQuestDto source,
        bool submissionProtocolEnabled,
        int prestige,
        out QuestTaskDisplay task,
        out string error)
    {
        task = null!;
        if (!source.PlayerQuestId.HasValue || source.PlayerQuestId.Value <= 0
            || !source.QuestId.HasValue || source.QuestId.Value <= 0)
        {
            error = "任务实例缺少 playerQuestId 或 questId。";
            return false;
        }

        if (source.Objectives == null)
        {
            error = $"任务「{DisplayName(source)}」缺少 objectives，无法可靠展示进度。";
            return false;
        }

        var objectives = new List<QuestObjectiveDisplay>(source.Objectives.Count);
        foreach (var objective in source.Objectives)
        {
            var text = string.IsNullOrWhiteSpace(objective.Text)
                ? "未命名目标"
                : objective.Text.Trim();
            if (objective.CurrentValue.HasValue && objective.RequiredValue.HasValue)
            {
                objectives.Add(new QuestObjectiveDisplay(
                    text,
                    objective.Completed,
                    $"（{objective.CurrentValue.Value}/{objective.RequiredValue.Value}）",
                    true
                ));
                continue;
            }

            // 缺少累计字段时不能擅自按一次性目标降级；直接标明服务端数据不可用。
            objectives.Add(new QuestObjectiveDisplay(
                text,
                objective.Completed,
                "（进度数据不可用）",
                false
            ));
        }

        var submission = source.Submission;
        var canSubmit = submissionProtocolEnabled && submission?.CanSubmit == true;
        var submitReason = submissionProtocolEnabled
            ? canSubmit
                ? ""
                : FirstNonEmpty(submission?.UnavailableReason, "服务端未允许提交该任务。")
            : "当前对局未启用任务主动提交协议。";
        var canAbandon = prestige >= 1;
        var abandonReason = canAbandon ? "" : "声望不足，放弃任务需要 1 声望。";

        task = new QuestTaskDisplay(
            source.PlayerQuestId.Value,
            source.QuestId.Value,
            DisplayName(source),
            objectives,
            canSubmit,
            submitReason,
            canAbandon,
            abandonReason
        );
        error = "";
        return true;
    }

    public static bool IsOwnPendingReward(PendingRewardDto? reward, long playerId)
    {
        return reward?.RewardId is > 0 && reward.PlayerId == playerId;
    }

    public static bool TryBuildItemRewardCandidate(
        PendingRewardDto reward,
        RewardCandidateDto candidate,
        long currentPlayerId,
        out ItemRewardCandidateDisplay display,
        out string error)
    {
        display = null!;
        if (reward.RewardId is null or <= 0 || reward.PlayerId != currentPlayerId)
        {
            error = "奖励队首缺少有效 rewardId 或不属于当前玩家。";
            return false;
        }
        if (!string.Equals(reward.Kind, "ITEM", StringComparison.OrdinalIgnoreCase))
        {
            error = $"当前奖励类型「{FirstNonEmpty(reward.Kind, "未知")}」尚未支持。";
            return false;
        }
        if (reward.CanResolve != true || reward.HasAuthority != true)
        {
            error = "本人奖励队首的 canResolve/hasAuthority 与 playerId 归属冲突。";
            return false;
        }
        if (candidate.CardId is null or <= 0 || candidate.ItemId is null or <= 0)
        {
            error = "ITEM 候选缺少有效 cardId 或 itemId，两者不能互相替代。";
            return false;
        }
        var configuration = candidate.Configuration;
        if (configuration == null || configuration.Id is null or <= 0)
        {
            error = $"ITEM 候选 {candidate.CardId.Value} 缺少有效 configuration。";
            return false;
        }
        if (configuration.Id.Value != candidate.ItemId.Value)
        {
            error = $"ITEM 候选 {candidate.CardId.Value} 的 configuration.id 与 itemId 冲突。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(configuration.ItemName)
            || string.IsNullOrWhiteSpace(configuration.ItemType))
        {
            error = $"ITEM 候选 {candidate.CardId.Value} 的 configuration 缺少 item_name 或 item_type。";
            return false;
        }
        if (!candidate.CapacitySatisfied.HasValue)
        {
            error = $"ITEM 候选 {candidate.CardId.Value} 缺少 capacitySatisfied。";
            return false;
        }

        var canExecute = reward.CanExecute == true && candidate.CanExecute == true;
        var reason = canExecute
            ? ""
            : FirstNonEmpty(
                candidate.ExecuteUnavailableReason,
                FirstNonEmpty(
                    reward.ExecuteUnavailableReason,
                    FirstNonEmpty(reward.UnavailableReason, "当前不能领取该候选。")
                )
            );
        if (!candidate.CanExecute.HasValue)
        {
            reason = "候选缺少 canExecute，不能按可领取处理。";
        }
        else if (!reward.CanExecute.HasValue)
        {
            reason = "奖励队首缺少 canExecute，不能按可领取处理。";
        }

        display = new ItemRewardCandidateDisplay(
            reward.RewardId.Value,
            candidate.CardId.Value,
            candidate.ItemId.Value,
            configuration.ItemName.Trim(),
            configuration.ItemType.Trim(),
            canExecute,
            reason,
            candidate.CapacitySatisfied.Value
        );
        error = "";
        return true;
    }

    public static bool CanResolveItemCandidate(
        PendingRewardDto reward,
        RewardCandidateDto candidate,
        long currentPlayerId,
        out string reason)
    {
        if (!TryBuildItemRewardCandidate(reward, candidate, currentPlayerId, out var display, out var error))
        {
            reason = error;
            return false;
        }
        reason = display.ExecuteReason;
        return display.CanExecute;
    }

    public static string RewardExecuteReason(PendingRewardDto reward) =>
        FirstNonEmpty(
            reward.ExecuteUnavailableReason,
            FirstNonEmpty(reward.UnavailableReason, "当前不能领取该奖励。")
        );

    public static string RewardAbandonReason(PendingRewardDto reward) =>
        FirstNonEmpty(reward.AbandonUnavailableReason, "当前不能放弃该奖励。");

    private static string DisplayName(PlayerQuestDto source) =>
        string.IsNullOrWhiteSpace(source.QuestName) ? "未命名任务" : source.QuestName.Trim();

    private static string FirstNonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
