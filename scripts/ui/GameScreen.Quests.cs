using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.UI;
using EquestriaStar.Game.Map;

namespace EquestriaStar.UI;

public partial class GameScreen
{
    private int _questJournalSequence;
    private long _questJournalStateVersion = -1;
    private bool _questJournalRequestInProgress;
    private Task _questSystemTask = Task.CompletedTask;
    private PlayerQuestListResponseDto? _questJournalSnapshot;
    private PlayerItemsResponseDto? _inventorySnapshot;
    private GameActionRequestDto? _unresolvedQuestAction;
    private long _rewardValidationRefreshStateVersion = -1;

    public bool IsQuestSystemSubmitting => _questSystemSubmitting;
    public bool IsQuestJournalRequestInProgress => _questJournalRequestInProgress;
    public GameActionRequestDto? UnresolvedQuestAction => _unresolvedQuestAction;
    public PlayerItemsResponseDto? QuestInventorySnapshot => _inventorySnapshot;

    private void InitializeQuestSystem()
    {
        _questJournal.Closed += OnQuestJournalClosed;
        _questJournal.RetryRequested += OnQuestJournalRetryRequested;
        _questJournal.SubmitRequested += OnQuestSubmitRequested;
        _questJournal.AbandonRequested += OnQuestAbandonRequested;
        _questJournal.RewardResolveRequested += OnRewardResolveRequested;
        _questJournal.RewardAbandonRequested += OnRewardAbandonRequested;
        _questJournal.InventoryRequested += OnInventoryRequested;
        _questJournal.DiscardRequested += OnDiscardRequested;
        _questJournal.RewardDataInvalid += OnRewardDataInvalid;
    }

    private void DisposeQuestSystem()
    {
        if (!GodotObject.IsInstanceValid(_questJournal))
        {
            return;
        }
        _questJournal.Closed -= OnQuestJournalClosed;
        _questJournal.RetryRequested -= OnQuestJournalRetryRequested;
        _questJournal.SubmitRequested -= OnQuestSubmitRequested;
        _questJournal.AbandonRequested -= OnQuestAbandonRequested;
        _questJournal.RewardResolveRequested -= OnRewardResolveRequested;
        _questJournal.RewardAbandonRequested -= OnRewardAbandonRequested;
        _questJournal.InventoryRequested -= OnInventoryRequested;
        _questJournal.DiscardRequested -= OnDiscardRequested;
        _questJournal.RewardDataInvalid -= OnRewardDataInvalid;
    }

    private void ResetQuestSystemForStart()
    {
        _questJournalSequence = 0;
        _questJournalStateVersion = -1;
        _questJournalRequestInProgress = false;
        _questSystemSubmitting = false;
        _questJournalSnapshot = null;
        _inventorySnapshot = null;
        _unresolvedQuestAction = null;
        _rewardValidationRefreshStateVersion = -1;
    }

    public Task OpenQuestJournalAsync()
    {
        if (!_active || _questSystemSubmitting || _itemSystemSubmitting)
        {
            return _questSystemTask;
        }
        if (!TryGetCurrentPlayer(out var player))
        {
            _questJournal.ShowError("玩家视图中缺少有效的局内 playerId，无法读取个人任务。", QuestJournalRetryKind.Tasks);
            return Task.CompletedTask;
        }

        _questJournal.OpenLoading(QuestJournalRetryKind.Tasks);
        _questSystemTask = LoadQuestJournalAsync(_gameId, player.PlayerId, _generation, ++_questJournalSequence);
        return _questSystemTask;
    }

    private async Task LoadQuestJournalAsync(long gameId, long playerId, int generation, int sequence)
    {
        if (_questJournalRequestInProgress)
        {
            return;
        }
        _questJournalRequestInProgress = true;
        ApiResult<PlayerQuestListResponseDto> result;
        try
        {
            result = await ApiClient.GetPlayerQuestsAsync(gameId, playerId);
        }
        catch (Exception exception)
        {
            result = ApiResult<PlayerQuestListResponseDto>.Failure(
                $"个人任务请求异常：{exception.Message}",
                "PLAYER_QUESTS_REQUEST_EXCEPTION"
            );
        }
        finally
        {
            _questJournalRequestInProgress = false;
        }

        if (!IsQuestJournalResponseCurrent(generation, sequence))
        {
            return;
        }
        if (!result.Ok || result.Data == null)
        {
            _questJournal.ShowError(FirstNonEmpty(result.Message, null, "个人任务加载失败，请重试。"), QuestJournalRetryKind.Tasks);
            return;
        }
        if (!ValidatePlayerQuests(result.Data, gameId, playerId, out var validationError))
        {
            _questJournal.ShowError(validationError, QuestJournalRetryKind.Tasks);
            return;
        }

        _questJournalSnapshot = result.Data;
        _questJournalStateVersion = _latestPlayerView?.StateVersion ?? -1;
        ShowQuestTasks(result.Data);
    }

    private void ShowQuestTasks(PlayerQuestListResponseDto snapshot, string status = "")
    {
        if (!TryGetCurrentPlayer(out var player))
        {
            _questJournal.ShowError("当前玩家身份已经变化，请重新同步。", QuestJournalRetryKind.Tasks);
            return;
        }

        var tasks = new List<QuestTaskDisplay>();
        foreach (var quest in snapshot.Quests ?? [])
        {
            if (!QuestSystemModel.TryBuildTask(
                    quest,
                    SupportsQuestSubmission,
                    player.Prestige,
                    out var task,
                    out var error))
            {
                _questJournal.ShowError(error, QuestJournalRetryKind.Tasks);
                return;
            }

            if (!IsGameConnectionReady)
            {
                task = task with
                {
                    CanSubmit = false,
                    SubmitReason = "对局连接尚未就绪。",
                    CanAbandon = false,
                    AbandonReason = "对局连接尚未就绪。"
                };
            }
            tasks.Add(task);
        }
        _questJournal.ShowTasks(tasks, status);
    }

    public Task SubmitQuestAsync(long playerQuestId)
    {
        if (!TryBeginQuestWrite(playerQuestId, out var player, out var quest))
        {
            return _questSystemTask;
        }
        if (!SupportsQuestSubmission || quest.Submission?.CanSubmit != true)
        {
            return Task.CompletedTask;
        }

        var request = new GameActionRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            PlayerId = player.PlayerId,
            StateVersion = _latestPlayerView!.StateVersion,
            ActionType = "SUBMIT_QUEST",
            Payload = new JsonObject { ["playerQuestId"] = playerQuestId }
        };
        return StartQuestWrite(request, QuestWriteKind.SubmitQuest);
    }

    public Task AbandonQuestAsync(long playerQuestId, long questId)
    {
        if (!TryBeginQuestWrite(playerQuestId, out var player, out var quest)
            || quest.QuestId != questId
            || player.Prestige < 1)
        {
            return _questSystemTask;
        }

        var request = new GameActionRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            PlayerId = player.PlayerId,
            StateVersion = _latestPlayerView!.StateVersion,
            ActionType = "ABANDON_QUEST",
            Payload = new JsonObject
            {
                ["questId"] = questId,
                ["playerQuestId"] = playerQuestId
            }
        };
        return StartQuestWrite(request, QuestWriteKind.AbandonQuest);
    }

    public Task ResolveRewardAsync(long rewardId, long cardId)
    {
        if (!TryBeginRewardWrite(rewardId, out var player, out var reward) || cardId <= 0)
        {
            return _questSystemTask;
        }
        var matches = reward.Candidates?.Where(candidate => candidate.CardId == cardId).ToList() ?? [];
        if (matches.Count != 1)
        {
            _questJournal.SetStatusMessage("候选 cardId 缺失或重复，已拒绝提交并重新同步。");
            OnRewardDataInvalid();
            return _questSystemTask;
        }
        if (!QuestSystemModel.TryBuildItemRewardCandidate(
                reward,
                matches[0],
                player.PlayerId,
                out var display,
                out var validationError))
        {
            _questJournal.SetStatusMessage(validationError);
            OnRewardDataInvalid();
            return _questSystemTask;
        }
        if (!display.CanExecute)
        {
            _questJournal.SetStatusMessage(display.ExecuteReason);
            return _questSystemTask;
        }
        var request = new GameActionRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            PlayerId = player.PlayerId,
            StateVersion = _latestPlayerView!.StateVersion,
            ActionType = "RESOLVE_TURN_START_EFFECT",
            Payload = new JsonObject { ["rewardId"] = rewardId, ["cardId"] = cardId }
        };
        return StartQuestWrite(request, QuestWriteKind.ResolveReward);
    }

    public Task AbandonRewardAsync(long rewardId)
    {
        if (!TryBeginRewardWrite(rewardId, out var player, out var reward)
            || reward.CanAbandon != true)
        {
            return _questSystemTask;
        }
        var request = new GameActionRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            PlayerId = player.PlayerId,
            StateVersion = _latestPlayerView!.StateVersion,
            ActionType = "RESOLVE_TURN_START_EFFECT",
            Payload = new JsonObject { ["rewardId"] = rewardId, ["abandon"] = true }
        };
        return StartQuestWrite(request, QuestWriteKind.AbandonReward);
    }

    public Task DiscardItemAsync(long playerItemId)
    {
        if (!CanStartQuestWrite()
            || playerItemId <= 0
            || !TryGetCurrentPlayer(out var player)
            || _inventorySnapshot?.Items?.Any(item => item.PlayerItemId == playerItemId) != true
            || !IsActionAvailable("DISCARD_ITEM"))
        {
            return _questSystemTask;
        }
        var request = new GameActionRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            PlayerId = player.PlayerId,
            StateVersion = _latestPlayerView!.StateVersion,
            ActionType = "DISCARD_ITEM",
            Payload = new JsonObject { ["playerItemId"] = playerItemId }
        };
        return StartQuestWrite(request, QuestWriteKind.DiscardItem);
    }

    public Task OpenQuestInventoryAsync()
    {
        if (!_active || _questSystemSubmitting || !TryGetCurrentPlayer(out var player))
        {
            return _questSystemTask;
        }
        _questJournal.OpenLoading(QuestJournalRetryKind.Inventory, "正在读取持有道具……");
        _questSystemTask = LoadQuestInventoryAsync(_gameId, player.PlayerId, _generation, ++_questJournalSequence);
        return _questSystemTask;
    }

    private async Task LoadQuestInventoryAsync(long gameId, long playerId, int generation, int sequence)
    {
        ApiResult<PlayerItemsResponseDto> result;
        try
        {
            result = await ApiClient.GetPlayerItemsAsync(gameId, playerId);
        }
        catch (Exception exception)
        {
            result = ApiResult<PlayerItemsResponseDto>.Failure($"背包请求异常：{exception.Message}", "PLAYER_ITEMS_REQUEST_EXCEPTION");
        }
        if (!IsQuestJournalResponseCurrent(generation, sequence))
        {
            return;
        }
        if (!result.Ok || result.Data == null)
        {
            _questJournal.ShowError(FirstNonEmpty(result.Message, null, "背包读取失败，请重试。"), QuestJournalRetryKind.Inventory);
            return;
        }
        if (!ValidatePlayerItems(result.Data, gameId, playerId, out var validationError))
        {
            _questJournal.ShowError(validationError, QuestJournalRetryKind.Inventory);
            return;
        }
        _inventorySnapshot = result.Data;
        var discard = _latestAvailableActions.FirstOrDefault(action =>
            string.Equals(action.ActionType, "DISCARD_ITEM", StringComparison.OrdinalIgnoreCase));
        var available = _availableActionsStateVersion == _latestPlayerView?.StateVersion && discard?.Available == true;
        var reason = available ? "" : FirstNonEmpty(discard?.ReasonText, discard?.Reason, "当前不能丢弃道具。");
        _questJournal.ShowInventory(result.Data, available, reason);
    }

    private Task StartQuestWrite(GameActionRequestDto request, QuestWriteKind kind)
    {
        _questSystemSubmitting = true;
        _unresolvedQuestAction = null;
        ExitMoveMode();
        _hud.SetActionControlsLocked(true);
        _questJournal.SetBusy(true, "正在提交操作……");
        _questSystemTask = SubmitQuestWriteAsync(_gameId, request, kind, _generation);
        return _questSystemTask;
    }

    private async Task SubmitQuestWriteAsync(long gameId, GameActionRequestDto request, QuestWriteKind kind, int generation)
    {
        ApiResult<GameActionResponseDto> result;
        try
        {
            result = await ApiClient.SubmitGameActionAsync(gameId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<GameActionResponseDto>.Failure($"任务操作请求异常：{exception.Message}", "QUEST_ACTION_REQUEST_EXCEPTION");
        }

        if (!IsResponseCurrent(generation))
        {
            return;
        }
        try
        {
            if (!result.Ok || result.Data == null)
            {
                if (result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_ERROR" or "QUEST_ACTION_REQUEST_EXCEPTION")
                {
                    // 未知结果保留原 requestId、路径和完整请求体，且不自动重发。
                    _unresolvedQuestAction = request;
                }
                await RecoverAfterQuestActionFailureAsync(result, kind);
                return;
            }

            _unresolvedQuestAction = null;
            _stateVersionFloor = Math.Max(_stateVersionFloor, result.Data.NewStateVersion);
            InvalidateAvailableActions();
            if (result.Data.PlayerViewState != null
                && ValidatePlayerView(result.Data.PlayerViewState, gameId, _currentUserId, out _)
                && !IsOlderPlayerView(result.Data.PlayerViewState))
            {
                ApplyPlayerView(result.Data.PlayerViewState, false);
            }
            else
            {
                await RefreshPlayerViewToVersionFloorAsync();
            }
            if (result.Data.NextAvailableActions != null && _latestPlayerView != null)
            {
                ApplyAvailableActions(result.Data.NextAvailableActions, _latestPlayerView.StateVersion);
            }
            else
            {
                await RefreshAvailableActionsAsync(true);
            }

            switch (kind)
            {
                case QuestWriteKind.SubmitQuest:
                    await HandleQuestSettlementAsync(result.Data, request.PlayerId, generation);
                    break;
                case QuestWriteKind.AbandonQuest:
                    await RefreshQuestTasksAfterConfirmedActionAsync(request.PlayerId, generation, "任务已放弃。", false);
                    break;
                case QuestWriteKind.ResolveReward:
                    var inventoryRefreshError = await RefreshInventorySnapshotAfterRewardAsync(
                        gameId,
                        request.PlayerId,
                        generation
                    );
                    await HandleRewardQueueAfterActionAsync(request.PlayerId, generation, inventoryRefreshError);
                    break;
                case QuestWriteKind.AbandonReward:
                    await HandleRewardQueueAfterActionAsync(request.PlayerId, generation, null);
                    break;
                case QuestWriteKind.DiscardItem:
                    await RefreshAvailableActionsAsync(true);
                    await LoadQuestInventoryAsync(gameId, request.PlayerId, generation, ++_questJournalSequence);
                    break;
            }
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _questSystemSubmitting = false;
                _hud.SetActionControlsLocked(false);
                _questJournal.SetBusy(false, "");
            }
        }
    }

    private async Task HandleQuestSettlementAsync(GameActionResponseDto response, long playerId, int generation)
    {
        var settlement = response.QuestSettlement;
        if (settlement == null
            || !settlement.PlayerQuestId.HasValue || !settlement.QuestId.HasValue
            || !settlement.CoinsGranted.HasValue || !settlement.PrestigeGranted.HasValue
            || !settlement.PartnerBonusCoins.HasValue || !settlement.CoinsAfter.HasValue
            || !settlement.PrestigeAfter.HasValue || !settlement.PendingItemRewardCount.HasValue)
        {
            _questJournal.ShowError("任务提交已返回成功，但缺少完整 questSettlement，无法可靠展示实际结算。", QuestJournalRetryKind.Tasks);
            // 后台列表仍可同步，但不能用刷新后的任务页覆盖结算契约错误。
            await RefreshQuestTasksSnapshotOnlyAsync(playerId, generation);
            return;
        }

        var pending = response.PlayerViewState?.EngineState?.PendingReward ?? _latestPlayerView?.EngineState?.PendingReward;
        _questJournal.ShowSettlement(settlement, pending, playerId, response.DeckDraws);
        await RefreshQuestTasksSnapshotOnlyAsync(playerId, generation);
    }

    private async Task HandleRewardQueueAfterActionAsync(long playerId, int generation, string? inventoryRefreshError)
    {
        await RefreshPlayerViewAsync();
        if (!IsResponseCurrent(generation))
        {
            return;
        }
        var next = _latestPlayerView?.EngineState?.PendingReward;
        if (next != null)
        {
            _questJournal.ShowReward(next, playerId);
            if (!string.IsNullOrWhiteSpace(inventoryRefreshError))
            {
                _questJournal.SetStatusMessage($"上一份奖励已确认，但背包刷新失败：{inventoryRefreshError}");
            }
            return;
        }
        if (!string.IsNullOrWhiteSpace(inventoryRefreshError))
        {
            _questJournal.ShowError(
                $"奖励已确认，但背包刷新失败：{inventoryRefreshError}",
                QuestJournalRetryKind.Inventory
            );
            return;
        }
        await RefreshQuestTasksAfterConfirmedActionAsync(playerId, generation, "奖励队首已处理。", false);
    }

    private async Task<string?> RefreshInventorySnapshotAfterRewardAsync(long gameId, long playerId, int generation)
    {
        ApiResult<PlayerItemsResponseDto> result;
        try
        {
            result = await ApiClient.GetPlayerItemsAsync(gameId, playerId);
        }
        catch (Exception exception)
        {
            return $"背包请求异常：{exception.Message}";
        }
        if (!IsResponseCurrent(generation))
        {
            return "页面或对局已切换，本次背包结果已丢弃。";
        }
        if (!result.Ok || result.Data == null)
        {
            return FirstNonEmpty(result.Message, null, "背包读取失败。");
        }
        if (!ValidatePlayerItems(result.Data, gameId, playerId, out var validationError))
        {
            return validationError;
        }
        _inventorySnapshot = result.Data;
        return null;
    }

    private async Task RefreshQuestTasksAfterConfirmedActionAsync(
        long playerId,
        int generation,
        string successMessage,
        bool showErrorOnFailure)
    {
        var result = await ApiClient.GetPlayerQuestsAsync(_gameId, playerId);
        if (!IsResponseCurrent(generation))
        {
            return;
        }
        var validationError = "";
        if (!result.Ok || result.Data == null
            || !ValidatePlayerQuests(result.Data, _gameId, playerId, out validationError))
        {
            var detail = result.Ok ? validationError : result.Message;
            _questJournal.ShowError(
                $"{successMessage}后续任务刷新失败：{detail}",
                QuestJournalRetryKind.Tasks
            );
            return;
        }
        _questJournalSnapshot = result.Data;
        _questJournalStateVersion = _latestPlayerView?.StateVersion ?? -1;
        ShowQuestTasks(result.Data, successMessage);
    }

    private async Task RefreshQuestTasksSnapshotOnlyAsync(long playerId, int generation)
    {
        var result = await ApiClient.GetPlayerQuestsAsync(_gameId, playerId);
        if (IsResponseCurrent(generation) && result.Ok && result.Data != null
            && ValidatePlayerQuests(result.Data, _gameId, playerId, out _))
        {
            _questJournalSnapshot = result.Data;
            _questJournalStateVersion = _latestPlayerView?.StateVersion ?? -1;
        }
    }

    private async Task RecoverAfterQuestActionFailureAsync(ApiResult<GameActionResponseDto> result, QuestWriteKind kind)
    {
        InvalidateAvailableActions();
        await RefreshPlayerViewAsync();
        await RefreshAvailableActionsAsync(true);
        var prefix = result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_ERROR" or "QUEST_ACTION_REQUEST_EXCEPTION"
            ? "操作结果未知，已保留原请求身份且不会自动重发。"
            : "操作未完成。";
        _questJournal.ShowError($"{prefix}{FirstNonEmpty(result.Message, null, "请刷新后重试。")}",
            kind == QuestWriteKind.DiscardItem ? QuestJournalRetryKind.Inventory : QuestJournalRetryKind.Tasks);
    }

    private bool TryBeginQuestWrite(
        long playerQuestId,
        out PlayerViewPlayerDto player,
        out PlayerQuestDto quest)
    {
        player = null!;
        quest = null!;
        if (!CanStartQuestWrite()
            || !TryGetCurrentPlayer(out player)
            || _questJournalSnapshot?.Quests == null)
        {
            return false;
        }
        var match = _questJournalSnapshot.Quests.FirstOrDefault(candidate => candidate.PlayerQuestId == playerQuestId);
        if (match == null)
        {
            return false;
        }
        quest = match;
        return true;
    }

    private bool TryBeginRewardWrite(long rewardId, out PlayerViewPlayerDto player, out PendingRewardDto reward)
    {
        player = null!;
        reward = null!;
        if (!CanStartQuestWrite() || !TryGetCurrentPlayer(out player))
        {
            return false;
        }
        var current = _latestPlayerView?.EngineState?.PendingReward;
        if (!QuestSystemModel.IsOwnPendingReward(current, player.PlayerId) || current!.RewardId != rewardId)
        {
            return false;
        }
        reward = current;
        return true;
    }

    private bool CanStartQuestWrite() =>
        _active
        && IsGameConnectionReady
        && !_questSystemSubmitting
        && !_itemSystemSubmitting
        && !_questAcceptSubmitting
        && !_endTurnSubmitting
        && !IsWorkActionInProgress
        && _moveState != PlayerMoveState.SubmittingMove
        && _latestPlayerView != null;

    private bool IsQuestJournalResponseCurrent(int generation, int sequence) =>
        IsResponseCurrent(generation)
        && sequence == _questJournalSequence
        && GodotObject.IsInstanceValid(_questJournal)
        && _questJournal.IsOpen;

    private void HandlePlayerViewForQuestSystem(PlayerViewResponseDto playerView)
    {
        if (!GodotObject.IsInstanceValid(_questJournal) || !_questJournal.IsOpen)
        {
            return;
        }

        if (_questJournal.State == QuestJournalState.Reward
            && TryGetCurrentPlayer(out var currentPlayer)
            && playerView.EngineState?.PendingReward is { } reward)
        {
            _questJournal.ShowReward(reward, currentPlayer.PlayerId);
            return;
        }

        if (_questJournal.State == QuestJournalState.Tasks
            && !_questJournalRequestInProgress
            && !_questSystemSubmitting
            && playerView.StateVersion != _questJournalStateVersion
            && TryGetCurrentPlayer(out var player))
        {
            _questJournal.OpenLoading(QuestJournalRetryKind.Tasks, "任务状态已更新，正在刷新……");
            _questSystemTask = LoadQuestJournalAsync(_gameId, player.PlayerId, _generation, ++_questJournalSequence);
        }
    }

    private void OnQuestJournalClosed()
    {
        _questJournalSequence++;
        _questJournalRequestInProgress = false;
    }

    private void OnQuestJournalRetryRequested(int kind)
    {
        if ((QuestJournalRetryKind)kind == QuestJournalRetryKind.Inventory)
        {
            _ = OpenQuestInventoryAsync();
        }
        else
        {
            _ = OpenQuestJournalAsync();
        }
    }

    private void OnQuestSubmitRequested(long playerQuestId) => _ = SubmitQuestAsync(playerQuestId);
    private void OnQuestAbandonRequested(long playerQuestId, long questId) => _ = AbandonQuestAsync(playerQuestId, questId);
    private void OnRewardResolveRequested(long rewardId, long cardId) => _ = ResolveRewardAsync(rewardId, cardId);
    private void OnRewardAbandonRequested(long rewardId) => _ = AbandonRewardAsync(rewardId);
    private void OnInventoryRequested() => _ = OpenQuestInventoryAsync();
    private void OnDiscardRequested(long playerItemId) => _ = DiscardItemAsync(playerItemId);

    private void OnRewardDataInvalid()
    {
        if (!_active || _latestPlayerView == null)
        {
            return;
        }

        // 同一权威版本只自动刷新一次，避免异常响应导致刷新循环。
        var stateVersion = _latestPlayerView.StateVersion;
        if (_rewardValidationRefreshStateVersion == stateVersion)
        {
            return;
        }

        _rewardValidationRefreshStateVersion = stateVersion;
        var generation = _generation;
        Callable.From(() =>
        {
            if (IsResponseCurrent(generation))
            {
                _ = RefreshPlayerViewAsync();
            }
        }).CallDeferred();
    }

    private static bool ValidatePlayerItems(
        PlayerItemsResponseDto response,
        long gameId,
        long playerId,
        out string error)
    {
        if (response.GameId != gameId || response.PlayerId != playerId)
        {
            error = "背包响应缺少匹配的 gameId 或 playerId。";
            return false;
        }

        if (response.Items == null)
        {
            error = "背包响应缺少 items。";
            return false;
        }

        if (response.Items.Any(item => item.PlayerItemId is null or <= 0 || item.ItemId is null or <= 0))
        {
            error = "背包响应包含非法 playerItemId 或 itemId。";
            return false;
        }

        error = "";
        return true;
    }

    private enum QuestWriteKind
    {
        SubmitQuest,
        AbandonQuest,
        ResolveReward,
        AbandonReward,
        DiscardItem
    }
}
