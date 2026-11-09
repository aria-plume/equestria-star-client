using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;

namespace EquestriaStar.UI;

public partial class GameScreen
{
    private int _questTargetSubmissionSequence;
    private bool _questTargetSubmissionLoading;
    private bool _questTargetSubmissionSubmitting;
    private long _questTargetSubmissionStateVersion = -1;
    private Task _questTargetSubmissionTask = Task.CompletedTask;
    private PlayerQuestListResponseDto? _questTargetQuestSnapshot;
    private PlayerItemsResponseDto? _questTargetItemSnapshot;
    private IReadOnlyList<QuestTargetSubmissionTaskView> _questTargetTasks = Array.Empty<QuestTargetSubmissionTaskView>();
    private GameActionRequestDto? _unresolvedQuestTargetAction;

    public bool IsQuestTargetSubmissionLoading => _questTargetSubmissionLoading;
    public bool IsQuestTargetSubmissionSubmitting => _questTargetSubmissionSubmitting;
    public bool IsQuestTargetSubmissionOverlayBlocking =>
        GodotObject.IsInstanceValid(_questTargetSubmissionOverlay) && _questTargetSubmissionOverlay.IsOpen;
    public GameActionRequestDto? UnresolvedQuestTargetAction => _unresolvedQuestTargetAction;

    private void InitializeQuestTargetSubmissions()
    {
        _hud.QuestSubmissionRequested += OnQuestTargetSubmissionRequested;
        _questTargetSubmissionOverlay.Closed += OnQuestTargetSubmissionClosed;
        _questTargetSubmissionOverlay.RetryRequested += OnQuestTargetSubmissionRetryRequested;
        _questTargetSubmissionOverlay.ItemSubmitRequested += OnQuestTargetItemSubmitRequested;
        _questTargetSubmissionOverlay.ResourceSubmitRequested += OnQuestTargetResourceSubmitRequested;
    }

    private void DisposeQuestTargetSubmissions()
    {
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.QuestSubmissionRequested -= OnQuestTargetSubmissionRequested;
        }
        if (!GodotObject.IsInstanceValid(_questTargetSubmissionOverlay)) return;
        _questTargetSubmissionOverlay.Closed -= OnQuestTargetSubmissionClosed;
        _questTargetSubmissionOverlay.RetryRequested -= OnQuestTargetSubmissionRetryRequested;
        _questTargetSubmissionOverlay.ItemSubmitRequested -= OnQuestTargetItemSubmitRequested;
        _questTargetSubmissionOverlay.ResourceSubmitRequested -= OnQuestTargetResourceSubmitRequested;
    }

    private void ResetQuestTargetSubmissionsForStart()
    {
        _questTargetSubmissionSequence = 0;
        _questTargetSubmissionLoading = false;
        _questTargetSubmissionSubmitting = false;
        _questTargetSubmissionStateVersion = -1;
        _questTargetQuestSnapshot = null;
        _questTargetItemSnapshot = null;
        _questTargetTasks = Array.Empty<QuestTargetSubmissionTaskView>();
        _unresolvedQuestTargetAction = null;
    }

    private void StopQuestTargetSubmissions()
    {
        _questTargetSubmissionSequence++;
        _questTargetSubmissionLoading = false;
        _questTargetSubmissionSubmitting = false;
        _questTargetSubmissionStateVersion = -1;
        _questTargetQuestSnapshot = null;
        _questTargetItemSnapshot = null;
        _questTargetTasks = Array.Empty<QuestTargetSubmissionTaskView>();
        _unresolvedQuestTargetAction = null;
        if (GodotObject.IsInstanceValid(_questTargetSubmissionOverlay))
        {
            _questTargetSubmissionOverlay.CloseOverlay(false);
        }
    }

    public Task OpenQuestTargetSubmissionsAsync()
    {
        if (!_active || _questTargetSubmissionLoading || _questTargetSubmissionSubmitting)
        {
            return _questTargetSubmissionTask;
        }
        _questTargetSubmissionOverlay.ShowLoading();
        _questTargetSubmissionTask = LoadQuestTargetSubmissionContextAsync(
            _generation,
            ++_questTargetSubmissionSequence,
            "");
        return _questTargetSubmissionTask;
    }

    private async Task LoadQuestTargetSubmissionContextAsync(int generation, int sequence, string successMessage)
    {
        if (_questTargetSubmissionLoading) return;
        _questTargetSubmissionLoading = true;
        try
        {
            await RefreshPlayerViewAsync();
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            await RefreshAvailableActionsAsync(true);
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            if (!TryGetCurrentPlayer(out var player))
            {
                _questTargetSubmissionOverlay.ShowError("玩家视图中缺少有效的局内 playerId，无法读取任务提交信息。");
                return;
            }

            var questsTask = ApiClient.GetPlayerQuestsAsync(_gameId, player.PlayerId);
            var itemsTask = ApiClient.GetPlayerItemsAsync(_gameId, player.PlayerId);
            await Task.WhenAll(questsTask, itemsTask);
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            var questResult = await questsTask;
            var itemResult = await itemsTask;
            if (!questResult.Ok || questResult.Data == null)
            {
                _questTargetSubmissionOverlay.ShowError(FirstNonEmpty(
                    questResult.Message, null, "任务列表加载失败，请重试。"));
                return;
            }
            if (!itemResult.Ok || itemResult.Data == null)
            {
                _questTargetSubmissionOverlay.ShowError(FirstNonEmpty(
                    itemResult.Message, null, "持有道具加载失败，请重试。"));
                return;
            }
            if (!ValidatePlayerQuests(questResult.Data, _gameId, player.PlayerId, out var questError))
            {
                _questTargetSubmissionOverlay.ShowError(questError);
                return;
            }
            if (!ValidatePlayerItems(itemResult.Data, _gameId, player.PlayerId, out var itemError))
            {
                _questTargetSubmissionOverlay.ShowError(itemError);
                return;
            }

            _questTargetQuestSnapshot = questResult.Data;
            _questTargetItemSnapshot = itemResult.Data;
            RebuildQuestTargetSubmissionTasks(successMessage);
        }
        catch (Exception exception)
        {
            if (IsQuestTargetSubmissionResponseCurrent(generation, sequence))
            {
                _questTargetSubmissionOverlay.ShowError($"任务提交信息请求异常：{exception.Message}");
            }
        }
        finally
        {
            _questTargetSubmissionLoading = false;
        }
    }

    private void RebuildQuestTargetSubmissionTasks(string message = "", bool preserveView = false)
    {
        if (_latestPlayerView == null || !TryGetCurrentPlayer(out var player)
            || _questTargetQuestSnapshot == null || _questTargetItemSnapshot == null)
        {
            _questTargetSubmissionOverlay.ShowError("任务、道具或玩家状态尚未完整同步。");
            return;
        }
        _questTargetTasks = QuestTargetSubmissionModel.BuildTasks(
            _questTargetQuestSnapshot.Quests,
            _latestPlayerView,
            player,
            _latestAvailableActions,
            _availableActionsStateVersion,
            IsGameConnectionReady,
            IsOtherQuestTargetWriteBusy());
        _questTargetSubmissionStateVersion = _latestPlayerView.StateVersion;
        if (preserveView)
        {
            _questTargetSubmissionOverlay.RefreshContent(_questTargetTasks, _questTargetItemSnapshot, message);
        }
        else
        {
            _questTargetSubmissionOverlay.ShowTasks(_questTargetTasks, _questTargetItemSnapshot, message);
        }
    }

    public Task SubmitQuestTargetItemAsync(long questId, long playerQuestId, long playerItemId)
    {
        if (!CanStartQuestTargetWrite()) return _questTargetSubmissionTask;
        _questTargetSubmissionTask = RevalidateAndSubmitQuestTargetAsync(
            questId, playerQuestId, playerItemId, false, _generation, ++_questTargetSubmissionSequence);
        return _questTargetSubmissionTask;
    }

    public Task PayQuestTargetResourceAsync(long questId, long playerQuestId)
    {
        if (!CanStartQuestTargetWrite()) return _questTargetSubmissionTask;
        _questTargetSubmissionTask = RevalidateAndSubmitQuestTargetAsync(
            questId, playerQuestId, null, true, _generation, ++_questTargetSubmissionSequence);
        return _questTargetSubmissionTask;
    }

    private async Task RevalidateAndSubmitQuestTargetAsync(
        long questId,
        long playerQuestId,
        long? playerItemId,
        bool resourcePayment,
        int generation,
        int sequence)
    {
        _questTargetSubmissionLoading = true;
        var previousTask = _questTargetTasks.FirstOrDefault(task =>
            task.QuestId == questId && task.PlayerQuestId == playerQuestId);
        try
        {
            _questTargetSubmissionOverlay.ShowLoading("正在复核最新任务、持有物与操作资格……");
            await RefreshPlayerViewAsync();
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            await RefreshAvailableActionsAsync(true);
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            if (!TryGetCurrentPlayer(out var player))
            {
                _questTargetSubmissionOverlay.ShowError("当前局内玩家身份已经失效。");
                return;
            }

            var questResult = await ApiClient.GetPlayerQuestsAsync(_gameId, player.PlayerId);
            var itemResult = await ApiClient.GetPlayerItemsAsync(_gameId, player.PlayerId);
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            var questError = "";
            if (!questResult.Ok || questResult.Data == null
                || !ValidatePlayerQuests(questResult.Data, _gameId, player.PlayerId, out questError))
            {
                _questTargetSubmissionOverlay.ShowError(FirstNonEmpty(
                    questResult.Message, questError, "任务资格复核失败。"));
                return;
            }
            var itemError = "";
            if (!itemResult.Ok || itemResult.Data == null
                || !ValidatePlayerItems(itemResult.Data, _gameId, player.PlayerId, out itemError))
            {
                _questTargetSubmissionOverlay.ShowError(FirstNonEmpty(
                    itemResult.Message, itemError, "持有道具复核失败。"));
                return;
            }

            _questTargetQuestSnapshot = questResult.Data;
            _questTargetItemSnapshot = itemResult.Data;
            _questTargetTasks = QuestTargetSubmissionModel.BuildTasks(
                questResult.Data.Quests,
                _latestPlayerView,
                player,
                _latestAvailableActions,
                _availableActionsStateVersion,
                IsGameConnectionReady,
                false);
            _questTargetSubmissionStateVersion = _latestPlayerView!.StateVersion;
            var task = _questTargetTasks.FirstOrDefault(candidate =>
                candidate.QuestId == questId && candidate.PlayerQuestId == playerQuestId);
            if (task == null)
            {
                _questTargetSubmissionOverlay.ShowTasks(_questTargetTasks, itemResult.Data, "该任务已不再处于可提交目标列表。已刷新权威状态。");
                return;
            }

            GameActionRequestDto request;
            var error = "";
            if (resourcePayment)
            {
                if (previousTask == null
                    || previousTask.RequiredCoinCost != task.RequiredCoinCost
                    || previousTask.RequiredPrestigeCost != task.RequiredPrestigeCost
                    || previousTask.RequiredSocialCost != task.RequiredSocialCost)
                {
                    _questTargetSubmissionOverlay.ShowTasks(
                        _questTargetTasks,
                        itemResult.Data,
                        "任务费用已变化，请按最新费用重新确认。");
                    return;
                }
                if (!QuestTargetSubmissionModel.TryCreateResourceRequest(
                        task,
                        player.PlayerId,
                        _latestPlayerView!.StateVersion,
                        CreateGameActionRequestId(),
                        out request,
                        out error))
                {
                    _questTargetSubmissionOverlay.ShowTasks(_questTargetTasks, itemResult.Data, error);
                    return;
                }
            }
            else
            {
                var item = QuestTargetSubmissionModel.BuildMatchingItems(task, itemResult.Data.Items)
                    .FirstOrDefault(candidate => candidate.PlayerItemId == playerItemId);
                if (item == null || !QuestTargetSubmissionModel.TryCreateItemRequest(
                        task,
                        item,
                        player.PlayerId,
                        _latestPlayerView!.StateVersion,
                        CreateGameActionRequestId(),
                        out request,
                        out error))
                {
                    _questTargetSubmissionOverlay.ShowTasks(
                        _questTargetTasks,
                        itemResult.Data,
                        item == null ? "所选道具已不在符合条件的持有实例中。" : error);
                    return;
                }
            }

            BeginQuestTargetWrite(request);
            await SubmitQuestTargetWriteAsync(request, generation, sequence);
        }
        catch (Exception exception)
        {
            if (IsQuestTargetSubmissionResponseCurrent(generation, sequence))
            {
                _questTargetSubmissionOverlay.ShowError($"任务提交复核异常：{exception.Message}");
            }
        }
        finally
        {
            _questTargetSubmissionLoading = false;
        }
    }

    private void BeginQuestTargetWrite(GameActionRequestDto request)
    {
        _questTargetSubmissionSubmitting = true;
        _unresolvedQuestTargetAction = null;
        ExitMoveMode();
        _hud.SetActionControlsLocked(true);
        _questTargetSubmissionOverlay.SetSubmitting(
            request.ActionType == QuestTargetSubmissionModel.SubmitItemAction
                ? "正在提交任务道具……"
                : "正在支付任务资源……");
    }

    private async Task SubmitQuestTargetWriteAsync(GameActionRequestDto request, int generation, int sequence)
    {
        ApiResult<GameActionResponseDto> result;
        try
        {
            result = await ApiClient.SubmitGameActionAsync(_gameId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<GameActionResponseDto>.Failure(
                $"任务目标提交请求异常：{exception.Message}",
                "QUEST_TARGET_ACTION_EXCEPTION");
        }

        if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
        try
        {
            if (!result.Ok || result.Data == null)
            {
                if (result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_ERROR" or "QUEST_TARGET_ACTION_EXCEPTION")
                {
                    // 未知结果只保存原请求上下文，不自动更换 requestId 重发。
                    _unresolvedQuestTargetAction = request;
                }
                await RefreshPlayerViewAsync();
                await RefreshAvailableActionsAsync(true);
                if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
                var prefix = _unresolvedQuestTargetAction == null
                    ? "任务目标提交失败"
                    : "任务目标提交结果未知，已同步权威状态且不会自动重发";
                await ReloadQuestTargetSnapshotsAfterWriteAsync(
                    request.PlayerId,
                    generation,
                    sequence,
                    $"{prefix}：{FirstNonEmpty(result.Message, null, "请重试查询。")}");
                return;
            }

            if (result.Data.GameId != _gameId
                || !string.Equals(result.Data.ActionType, request.ActionType, StringComparison.OrdinalIgnoreCase))
            {
                _questTargetSubmissionOverlay.ShowError("行动回执身份与本次任务目标提交不匹配。");
                return;
            }
            _unresolvedQuestTargetAction = null;
            _stateVersionFloor = Math.Max(_stateVersionFloor, result.Data.NewStateVersion);
            InvalidateAvailableActions();
            if (result.Data.PlayerViewState != null
                && ValidatePlayerView(result.Data.PlayerViewState, _gameId, _currentUserId, out _)
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
            if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
            await ReloadQuestTargetSnapshotsAfterWriteAsync(
                request.PlayerId,
                generation,
                sequence,
                request.ActionType == QuestTargetSubmissionModel.SubmitItemAction
                    ? "道具交付已由服务端确认。任务达到 READY_TO_SUBMIT 时仍需在个人任务中另行结算。"
                    : "资源支付已由服务端确认。任务达到 READY_TO_SUBMIT 时仍需在个人任务中另行结算。");
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _questTargetSubmissionSubmitting = false;
                _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task ReloadQuestTargetSnapshotsAfterWriteAsync(
        long playerId,
        int generation,
        int sequence,
        string message)
    {
        var questResult = await ApiClient.GetPlayerQuestsAsync(_gameId, playerId);
        var itemResult = await ApiClient.GetPlayerItemsAsync(_gameId, playerId);
        if (!IsQuestTargetSubmissionResponseCurrent(generation, sequence)) return;
        var questError = "";
        var itemError = "";
        if (!questResult.Ok || questResult.Data == null
            || !ValidatePlayerQuests(questResult.Data, _gameId, playerId, out questError)
            || !itemResult.Ok || itemResult.Data == null
            || !ValidatePlayerItems(itemResult.Data, _gameId, playerId, out itemError))
        {
            var refreshError = FirstNonEmpty(
                questResult.Message,
                itemResult.Message,
                FirstNonEmpty(questError, itemError, "响应结构异常。"));
            _questTargetSubmissionOverlay.ShowError(
                $"{message}\n后续刷新失败：{refreshError}");
            return;
        }
        _questTargetQuestSnapshot = questResult.Data;
        _questTargetItemSnapshot = itemResult.Data;
        RebuildQuestTargetSubmissionTasks(message);
    }

    private bool CanStartQuestTargetWrite() =>
        _active
        && IsGameConnectionReady
        && !_questTargetSubmissionLoading
        && !_questTargetSubmissionSubmitting
        && !_questSystemSubmitting
        && !_itemSystemSubmitting
        && !_questAcceptSubmitting
        && !_endTurnSubmitting
        && !_workPreparing
        && !_questCheckPreparing
        && !_checkSubmitting
        && _moveState != PlayerMoveState.SubmittingMove
        && _latestPlayerView != null;

    private bool IsOtherQuestTargetWriteBusy() =>
        _questSystemSubmitting
        || _itemSystemSubmitting
        || _questAcceptSubmitting
        || _endTurnSubmitting
        || _workPreparing
        || _questCheckPreparing
        || _checkSubmitting
        || _moveState == PlayerMoveState.SubmittingMove;

    private bool IsQuestTargetSubmissionResponseCurrent(int generation, int sequence) =>
        IsResponseCurrent(generation)
        && sequence == _questTargetSubmissionSequence
        && GodotObject.IsInstanceValid(_questTargetSubmissionOverlay)
        && _questTargetSubmissionOverlay.IsOpen;

    private void HandlePlayerViewForQuestTargetSubmissions(PlayerViewResponseDto playerView)
    {
        if (!IsQuestTargetSubmissionOverlayBlocking
            || _questTargetSubmissionLoading
            || _questTargetSubmissionSubmitting
            || playerView.StateVersion == _questTargetSubmissionStateVersion
            || _questTargetQuestSnapshot == null
            || _questTargetItemSnapshot == null)
        {
            return;
        }
        RebuildQuestTargetSubmissionTasks(_questTargetSubmissionOverlay.StatusText, true);
    }

    private void OnQuestTargetSubmissionRequested() => _ = OpenQuestTargetSubmissionsAsync();
    private void OnQuestTargetSubmissionRetryRequested() => _ = OpenQuestTargetSubmissionsAsync();
    private void OnQuestTargetItemSubmitRequested(long questId, long playerQuestId, long playerItemId) =>
        _ = SubmitQuestTargetItemAsync(questId, playerQuestId, playerItemId);
    private void OnQuestTargetResourceSubmitRequested(long questId, long playerQuestId) =>
        _ = PayQuestTargetResourceAsync(questId, playerQuestId);

    private void OnQuestTargetSubmissionClosed()
    {
        _questTargetSubmissionSequence++;
        _questTargetSubmissionLoading = false;
        _questTargetSubmissionStateVersion = -1;
        _questTargetQuestSnapshot = null;
        _questTargetItemSnapshot = null;
        _questTargetTasks = Array.Empty<QuestTargetSubmissionTaskView>();
    }
}
