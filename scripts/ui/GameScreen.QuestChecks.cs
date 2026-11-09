using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;

namespace EquestriaStar.UI;

public partial class GameScreen
{
    private enum QuestPendingRecoveryState
    {
        Recovered,
        None,
        Failed
    }

    private QuestCheckOverlay _questCheckOverlay = null!;
    private bool _questCheckLoading;
    private bool _questCheckPreparing;
    private bool _questPreparationUncertain;
    private int _questCheckSequence;
    private long _questCheckBoundStateVersion = -1;
    private long _expectedQuestPlayerQuestId;
    private long _expectedQuestId;
    private bool _questPreparationReplayConfirmed;
    private bool _returnToQuestChecksAfterResult;
    private GameActionRequestDto? _unresolvedQuestPreparationRequest;
    private IReadOnlyList<QuestCheckTaskView> _latestQuestCheckTasks = Array.Empty<QuestCheckTaskView>();
    private Task _questCheckTask = Task.CompletedTask;

    public QuestCheckOverlay QuestCheckOverlay => _questCheckOverlay;
    public bool IsQuestCheckPreparing => _questCheckPreparing;
    public bool IsQuestPreparationUncertain => _questPreparationUncertain;

    private void InitializeQuestChecks()
    {
        _questCheckOverlay = GetNode<QuestCheckOverlay>("QuestCheckOverlay");
        _hud.QuestCheckRequested += OnQuestCheckRequested;
        _questCheckOverlay.Closed += OnQuestCheckOverlayClosed;
        _questCheckOverlay.RetryRequested += OnQuestCheckRetryRequested;
        _questCheckOverlay.CheckRequested += OnQuestCheckSubmitRequested;
    }

    private void DisposeQuestChecks()
    {
        if (GodotObject.IsInstanceValid(_hud)) _hud.QuestCheckRequested -= OnQuestCheckRequested;
        if (!GodotObject.IsInstanceValid(_questCheckOverlay)) return;
        _questCheckOverlay.Closed -= OnQuestCheckOverlayClosed;
        _questCheckOverlay.RetryRequested -= OnQuestCheckRetryRequested;
        _questCheckOverlay.CheckRequested -= OnQuestCheckSubmitRequested;
    }

    private void ResetQuestChecksForStart()
    {
        _questCheckSequence++;
        _questCheckLoading = false;
        _questCheckPreparing = false;
        _questPreparationUncertain = false;
        _questCheckBoundStateVersion = -1;
        _expectedQuestPlayerQuestId = 0;
        _expectedQuestId = 0;
        _questPreparationReplayConfirmed = false;
        _returnToQuestChecksAfterResult = false;
        _unresolvedQuestPreparationRequest = null;
        _latestQuestCheckTasks = Array.Empty<QuestCheckTaskView>();
        _questCheckOverlay.CloseOverlay(false);
    }

    private void StopQuestChecks()
    {
        _questCheckSequence++;
        _questCheckLoading = false;
        _questCheckPreparing = false;
        _questPreparationUncertain = false;
        _questCheckBoundStateVersion = -1;
        _expectedQuestPlayerQuestId = 0;
        _expectedQuestId = 0;
        _questPreparationReplayConfirmed = false;
        _returnToQuestChecksAfterResult = false;
        _unresolvedQuestPreparationRequest = null;
        _latestQuestCheckTasks = Array.Empty<QuestCheckTaskView>();
        if (GodotObject.IsInstanceValid(_questCheckOverlay)) _questCheckOverlay.CloseOverlay(false);
    }

    public Task OpenQuestChecksAsync()
    {
        if (!_active || _gameId <= 0 || _currentUserId <= 0 || _questCheckLoading || _questCheckPreparing)
        {
            return _questCheckTask;
        }

        if (_workDiceOverlay.IsOpen)
        {
            ShowGameStatusError("请先完成当前待判定。");
            return _questCheckTask;
        }

        _questCheckLoading = true;
        var sequence = ++_questCheckSequence;
        var generation = _generation;
        _questCheckOverlay.ShowLoading();
        _questCheckTask = LoadQuestCheckListCoreAsync(generation, sequence, "");
        return _questCheckTask;
    }

    private async Task LoadQuestCheckListCoreAsync(int generation, int sequence, string successMessage)
    {
        try
        {
            await RefreshPlayerViewAsync();
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;
            if (!TryGetCurrentPlayer(out var player) || _latestPlayerView == null)
            {
                _questCheckOverlay.ShowError("无法识别当前局内玩家，请重试同步。");
                return;
            }

            if (_latestPlayerView.EngineState?.PendingCheck != null)
            {
                _questCheckOverlay.CloseOverlay(false);
                await QueryAndApplyPendingWorkAsync(generation, true);
                return;
            }

            await RefreshAvailableActionsAsync(true);
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;

            ApiResult<PlayerQuestListResponseDto> result;
            try
            {
                result = await ApiClient.GetPlayerQuestsAsync(_gameId, player.PlayerId);
            }
            catch (Exception exception)
            {
                result = ApiResult<PlayerQuestListResponseDto>.Failure(
                    $"任务判定列表请求异常：{exception.Message}", "QUEST_CHECK_LIST_EXCEPTION");
            }
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;
            if (!result.Ok || result.Data == null)
            {
                _questCheckOverlay.ShowError(FirstNonEmpty(result.Message, null, "任务判定列表加载失败，请重试。"));
                return;
            }
            if (result.Data.GameId is > 0 && result.Data.GameId != _gameId
                || result.Data.PlayerId is > 0 && result.Data.PlayerId != player.PlayerId)
            {
                _questCheckOverlay.ShowError("服务端返回了其他对局或玩家的任务列表。");
                return;
            }

            _latestQuestCheckTasks = BuildQuestCheckTasks(result.Data.Quests, player, false);
            _questCheckBoundStateVersion = _latestPlayerView.StateVersion;
            _questCheckOverlay.ShowContent(_latestQuestCheckTasks, successMessage);
        }
        finally
        {
            if (IsQuestCheckResponseCurrent(generation, sequence))
            {
                _questCheckLoading = false;
            }
        }
    }

    private IReadOnlyList<QuestCheckTaskView> BuildQuestCheckTasks(
        IEnumerable<PlayerQuestDto>? quests,
        PlayerViewPlayerDto player,
        bool includeCurrentPreparationLock)
    {
        var writeBusy = _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || _moveState == PlayerMoveState.SubmittingMove
            || _workPreparing
            || _checkSubmitting
            || (includeCurrentPreparationLock && _questCheckPreparing);
        return QuestCheckModel.BuildTasks(
            quests,
            _latestPlayerView,
            player,
            _latestAvailableActions,
            _availableActionsStateVersion,
            IsGameConnectionReady,
            writeBusy);
    }

    public Task SubmitQuestCheckAsync(long questId, long playerQuestId, string actionType, string attributeType)
    {
        if (!_active
            || !_questCheckOverlay.IsOpen
            || _questCheckPreparing
            || _questPreparationUncertain
            || _workPreparing
            || _checkSubmitting
            || _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || _moveState == PlayerMoveState.SubmittingMove)
        {
            return _questCheckTask;
        }

        _questCheckPreparing = true;
        _expectedQuestPlayerQuestId = playerQuestId;
        _expectedQuestId = questId;
        _questPreparationReplayConfirmed = false;
        _questCheckOverlay.SetSubmitting(playerQuestId, attributeType);
        _hud.SetActionControlsLocked(true);
        var generation = _generation;
        var sequence = ++_questCheckSequence;
        _questCheckTask = SubmitQuestCheckCoreAsync(
            questId,
            playerQuestId,
            actionType,
            attributeType,
            generation,
            sequence);
        return _questCheckTask;
    }

    private async Task SubmitQuestCheckCoreAsync(
        long questId,
        long playerQuestId,
        string actionType,
        string attributeType,
        int generation,
        int sequence)
    {
        try
        {
            await RefreshPlayerViewAsync();
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;
            await RefreshAvailableActionsAsync(true);
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;
            if (!TryGetCurrentPlayer(out var player) || _latestPlayerView == null)
            {
                _questCheckOverlay.ShowInlineError("无法识别当前局内玩家，未提交判定。");
                return;
            }

            ApiResult<PlayerQuestListResponseDto> list;
            try
            {
                list = await ApiClient.GetPlayerQuestsAsync(_gameId, player.PlayerId);
            }
            catch (Exception exception)
            {
                list = ApiResult<PlayerQuestListResponseDto>.Failure(
                    $"任务判定资格复核异常：{exception.Message}", "QUEST_CHECK_VERIFY_EXCEPTION");
            }
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;
            if (!list.Ok || list.Data == null)
            {
                _questCheckOverlay.ShowInlineError(FirstNonEmpty(list.Message, null, "无法复核最新任务资格，未提交判定。"));
                return;
            }

            var tasks = BuildQuestCheckTasks(list.Data.Quests, player, false);
            var task = tasks.FirstOrDefault(candidate => candidate.QuestId == questId
                && candidate.PlayerQuestId == playerQuestId
                && string.Equals(candidate.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
            var row = task?.Rows.FirstOrDefault(candidate => candidate.IsChoice
                ? candidate.ChoiceAttributes.Contains(attributeType, StringComparer.OrdinalIgnoreCase)
                : string.Equals(candidate.AttributeType, attributeType, StringComparison.OrdinalIgnoreCase));
            if (task == null || row?.CanExecute != true)
            {
                _latestQuestCheckTasks = tasks;
                _questCheckBoundStateVersion = _latestPlayerView.StateVersion;
                _questCheckOverlay.ShowContent(tasks, FirstNonEmpty(
                    row?.UnavailableReason,
                    null,
                    "任务资格已变化，未提交判定。"));
                return;
            }

            if (!QuestCheckModel.TryCreateRequest(
                    task,
                    attributeType,
                    player.PlayerId,
                    _latestPlayerView.StateVersion,
                    CreateGameActionRequestId(),
                    out var request,
                    out var requestError))
            {
                _questCheckOverlay.ShowInlineError(requestError);
                return;
            }
            _unresolvedQuestPreparationRequest = request;
            _knownPendingBusinessType = "QUEST";

            ApiResult<GameActionResponseDto> result;
            try
            {
                result = await ApiClient.SubmitGameActionAsync(_gameId, request);
            }
            catch (Exception exception)
            {
                result = ApiResult<GameActionResponseDto>.Failure(
                    $"任务判定准备请求异常：{exception.Message}", "QUEST_CHECK_PREPARE_EXCEPTION");
            }
            if (!IsQuestCheckResponseCurrent(generation, sequence)) return;

            if (!result.Ok || result.Data == null)
            {
                await HandleQuestCheckPreparationFailureAsync(result, generation, sequence);
                return;
            }
            if (!await ApplyQuestPreparationSuccessAsync(
                    result.Data,
                    actionType,
                    player.PlayerId,
                    playerQuestId,
                    questId,
                    generation))
            {
                return;
            }
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _questCheckPreparing = false;
                if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task HandleQuestCheckPreparationFailureAsync(
        ApiResult<GameActionResponseDto> result,
        int generation,
        int sequence)
    {
        var unknown = result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_TIMEOUT" or "NETWORK_ERROR"
            or "QUEST_CHECK_PREPARE_EXCEPTION" or "REQUEST_IN_PROGRESS";
        var existingPending = result.ErrorCode == "CHECK_ROLL_PENDING";
        if (unknown || existingPending)
        {
            var recovery = await QueryPendingQuestPreparationAsync(generation);
            if (recovery == QuestPendingRecoveryState.Recovered)
            {
                _questCheckOverlay.CloseOverlay(false);
                return;
            }
            if (IsQuestCheckResponseCurrent(generation, sequence))
            {
                _questPreparationUncertain = unknown;
                _questCheckOverlay.ShowError(unknown
                    ? "任务判定准备结果无法确认。已保留原 requestId 和完整请求；重试只查询待判定，不会重新扣费。"
                    : FirstNonEmpty(result.Message, null, "服务端提示已有待判定，但暂时无法恢复。"));
            }
            return;
        }

        _unresolvedQuestPreparationRequest = null;
        _questPreparationUncertain = false;
        _knownPendingBusinessType = "";
        _expectedQuestPlayerQuestId = 0;
        _expectedQuestId = 0;
        _questPreparationReplayConfirmed = false;
        await ReloadQuestChecksAfterRejectionAsync(generation, FirstNonEmpty(result.Message, null, "任务判定准备失败。"));
    }

    private async Task<bool> ApplyQuestPreparationSuccessAsync(
        GameActionResponseDto response,
        string expectedActionType,
        long playerId,
        long playerQuestId,
        long questId,
        int generation)
    {
        if (!TryApplyQuestPreparationSnapshot(
                response,
                expectedActionType,
                playerId,
                playerQuestId,
                questId,
                true,
                out var pending,
                out var pendingError))
        {
            _unresolvedQuestPreparationRequest = null;
            _questPreparationUncertain = false;
            _questPreparationReplayConfirmed = false;
            _questCheckOverlay.ShowInlineError(FirstNonEmpty(
                pendingError,
                null,
                "任务判定准备响应与本次任务不匹配。"));
            return false;
        }

        _unresolvedQuestPreparationRequest = null;
        _questPreparationUncertain = false;
        _questPreparationReplayConfirmed = false;
        _questCheckOverlay.CloseOverlay(false);
        OpenPendingWork(pending, false);
        InvalidateAvailableActions();
        await RefreshPlayerViewToVersionFloorAsync();
        await RefreshAvailableActionsAsync(true);
        return IsResponseCurrent(generation);
    }

    private bool TryApplyQuestPreparationSnapshot(
        GameActionResponseDto response,
        string expectedActionType,
        long playerId,
        long playerQuestId,
        long questId,
        bool applyPlayerView,
        out PendingCheckDto pending,
        out string error)
    {
        pending = null!;
        if (!string.Equals(response.ActionType, expectedActionType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(response.ResultType, "CHECK_ROLL_PENDING", StringComparison.OrdinalIgnoreCase))
        {
            error = "任务判定准备响应的行动或结果类型不匹配。";
            return false;
        }
        if (!QuestCheckModel.TryValidatePendingQuest(
                response.PendingCheck,
                _gameId,
                playerId,
                playerQuestId,
                questId,
                out pending,
                out error))
        {
            return false;
        }

        _stateVersionFloor = Math.Max(_stateVersionFloor, response.NewStateVersion);
        if (applyPlayerView
            && response.PlayerViewState != null
            && ValidatePlayerView(response.PlayerViewState, _gameId, _currentUserId, out _)
            && !IsOlderPlayerView(response.PlayerViewState))
        {
            ApplyPlayerView(response.PlayerViewState, false);
        }
        error = "";
        return true;
    }

    private async Task<QuestPendingRecoveryState> QueryPendingQuestPreparationAsync(int generation)
    {
        ApiResult<PendingCheckDto> result;
        try
        {
            result = await ApiClient.GetPendingCheckAsync(_gameId);
        }
        catch (Exception exception)
        {
            result = ApiResult<PendingCheckDto>.Failure(
                $"待判定查询异常：{exception.Message}", "PENDING_CHECK_REQUEST_EXCEPTION");
        }
        if (!IsResponseCurrent(generation)) return QuestPendingRecoveryState.Failed;
        if (!result.Ok)
        {
            _questCheckOverlay.ShowError(
                $"无法确认原任务判定状态：{FirstNonEmpty(result.Message, null, "请重试同步。")}");
            return QuestPendingRecoveryState.Failed;
        }
        if (result.Data == null)
        {
            return QuestPendingRecoveryState.None;
        }
        var error = "无法识别当前局内玩家。";
        if (!TryGetCurrentPlayer(out var player)
            || !QuestCheckModel.TryValidatePendingQuest(
                result.Data,
                _gameId,
                player.PlayerId,
                _expectedQuestPlayerQuestId > 0 ? _expectedQuestPlayerQuestId : null,
                _expectedQuestId > 0 ? _expectedQuestId : null,
                out var pending,
                out error))
        {
            _questCheckOverlay.ShowError(FirstNonEmpty(error, null, "待判定身份与当前任务不匹配。"));
            return QuestPendingRecoveryState.Failed;
        }
        _questCheckOverlay.CloseOverlay(false);
        _unresolvedQuestPreparationRequest = null;
        _questPreparationUncertain = false;
        _questPreparationReplayConfirmed = false;
        OpenPendingWork(pending, true);
        return QuestPendingRecoveryState.Recovered;
    }

    private async Task ReloadQuestChecksAfterRejectionAsync(int generation, string message)
    {
        if (!IsResponseCurrent(generation)) return;
        _questCheckLoading = true;
        var sequence = ++_questCheckSequence;
        _questCheckOverlay.ShowLoading(message);
        await LoadQuestCheckListCoreAsync(generation, sequence, message);
    }

    private void HandlePlayerViewForQuestChecks(PlayerViewResponseDto playerView)
    {
        if (!_questCheckOverlay.IsOpen
            || _questCheckLoading
            || _questCheckPreparing
            || _questPreparationUncertain
            || _questCheckOverlay.State is not QuestCheckOverlayState.Content
            || playerView.StateVersion == _questCheckBoundStateVersion)
        {
            return;
        }

        _questCheckLoading = true;
        var sequence = ++_questCheckSequence;
        _questCheckTask = LoadQuestCheckListCoreAsync(_generation, sequence, "任务状态已更新。");
    }

    private async Task ReturnToQuestChecksAfterResultAsync(int generation)
    {
        if (!IsResponseCurrent(generation)) return;
        await OpenQuestChecksAsync();
    }

    private bool IsQuestCheckResponseCurrent(int generation, int sequence) =>
        IsResponseCurrent(generation) && sequence == _questCheckSequence;

    private void OnQuestCheckRequested() => _ = OpenQuestChecksAsync();

    private void OnQuestCheckSubmitRequested(long questId, long playerQuestId, string actionType, string attributeType) =>
        _ = SubmitQuestCheckAsync(questId, playerQuestId, actionType, attributeType);

    private void OnQuestCheckRetryRequested()
    {
        if (_questPreparationUncertain)
        {
            _ = RetryQuestCheckRecoveryAsync();
            return;
        }
        _ = OpenQuestChecksAsync();
    }

    public Task RetryQuestCheckRecoveryAsync()
    {
        if (!_questPreparationUncertain || _questCheckPreparing)
        {
            return _questCheckTask;
        }
        _questCheckTask = RetryUncertainQuestPreparationAsync(_generation);
        return _questCheckTask;
    }

    private async Task RetryUncertainQuestPreparationAsync(int generation)
    {
        if (_questCheckPreparing) return;
        _questCheckPreparing = true;
        _hud.SetActionControlsLocked(true);
        _questCheckOverlay.ShowLoading("正在查询原任务判定状态……", allowClose: false);
        var recovery = await QueryPendingQuestPreparationAsync(generation);
        if (!IsResponseCurrent(generation)) return;
        if (recovery == QuestPendingRecoveryState.Recovered)
        {
            _questPreparationUncertain = false;
            _questCheckOverlay.CloseOverlay(false);
            _questCheckPreparing = false;
            return;
        }
        if (recovery == QuestPendingRecoveryState.Failed)
        {
            _questCheckPreparing = false;
            _hud.SetActionControlsLocked(false);
            return;
        }

        if (_questPreparationReplayConfirmed)
        {
            await HandleQuestPreparationNoLongerPendingAsync(generation);
            _questCheckPreparing = false;
            if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
            return;
        }

        var request = _unresolvedQuestPreparationRequest;
        if (request == null || _expectedQuestPlayerQuestId <= 0 || _expectedQuestId <= 0)
        {
            _questCheckPreparing = false;
            _hud.SetActionControlsLocked(false);
            _questCheckOverlay.ShowError("原任务判定请求上下文不完整，不能生成新请求重试。");
            return;
        }

        _questCheckOverlay.ShowLoading("未发现待判定，正在用原 requestId 恢复原响应……", allowClose: false);
        ApiResult<GameActionResponseDto> replay;
        try
        {
            // 幂等恢复必须复用原 requestId、原 stateVersion、原 actionType 和完整 payload。
            replay = await ApiClient.SubmitGameActionAsync(_gameId, request);
        }
        catch (Exception exception)
        {
            replay = ApiResult<GameActionResponseDto>.Failure(
                $"任务判定原请求恢复异常：{exception.Message}", "QUEST_CHECK_REPLAY_EXCEPTION");
        }
        if (!IsResponseCurrent(generation)) return;
        if (replay.Ok && replay.Data != null && TryGetCurrentPlayer(out var player))
        {
            if (!TryApplyQuestPreparationSnapshot(
                replay.Data,
                request.ActionType,
                player.PlayerId,
                _expectedQuestPlayerQuestId,
                _expectedQuestId,
                false,
                out _,
                out var replayError))
            {
                _questCheckOverlay.ShowError(FirstNonEmpty(
                    replayError,
                    null,
                    "原任务判定回执与请求身份不匹配，未打开骰子。"));
            }
            else
            {
                // 幂等回执可能只是首次保存的历史快照，必须再次查询当前 WAITING_ROLL。
                _questPreparationReplayConfirmed = true;
                var currentRecovery = await QueryPendingQuestPreparationAsync(generation);
                if (currentRecovery == QuestPendingRecoveryState.None)
                {
                    await HandleQuestPreparationNoLongerPendingAsync(generation);
                }
                else if (currentRecovery == QuestPendingRecoveryState.Failed)
                {
                    _questPreparationUncertain = true;
                }
            }
        }
        else
        {
            _questCheckOverlay.ShowError(FirstNonEmpty(
                replay.Message,
                null,
                "原任务判定请求仍无法恢复；不会生成新 requestId。"));
        }
        _questCheckPreparing = false;
        if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
    }

    private async Task HandleQuestPreparationNoLongerPendingAsync(int generation)
    {
        _unresolvedQuestPreparationRequest = null;
        _questPreparationUncertain = false;
        _questPreparationReplayConfirmed = false;
        _knownPendingBusinessType = "";
        _expectedQuestPlayerQuestId = 0;
        _expectedQuestId = 0;
        await ReloadQuestChecksAfterRejectionAsync(
            generation,
            "原判定已不再待处理，无法恢复详细结果。已同步最新任务状态。");
    }

    private void OnQuestCheckOverlayClosed()
    {
        if (_questCheckPreparing) return;
        _questCheckSequence++;
        _questCheckLoading = false;
        _questCheckBoundStateVersion = -1;
        _latestQuestCheckTasks = Array.Empty<QuestCheckTaskView>();
    }
}
