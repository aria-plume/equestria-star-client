using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Dice;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;

namespace EquestriaStar.UI;

public partial class GameScreen
{
    private WorkDiceOverlay _workDiceOverlay = null!;
    private bool _workPreparing;
    private bool _checkSubmitting;
    private bool _workPreparationUncertain;
    private bool _workAttributeSelectionActive;
    private int _workAttributeSelectionSequence;
    private long _workAttributeSelectionPlayerId;
    private int _pendingCheckSequence;
    private long _knownWorkCheckId;
    private string _knownPendingBusinessType = "";
    private bool _resumeNormalMoveAfterForestResult;
    private PendingCheckDto? _workPendingCheck;
    private CheckRerollRequestDto? _unresolvedRerollRequest;
    private CheckRollRequestDto? _unresolvedRollRequest;
    private Task _workTask = Task.CompletedTask;
    private Task _checkTask = Task.CompletedTask;
    private Task _pendingCheckTask = Task.CompletedTask;
    private readonly HashSet<string> _processedCheckResults = [];

    public WorkDiceOverlay WorkDiceOverlay => _workDiceOverlay;
    public bool IsWorkPreparing => _workPreparing;
    public bool IsCheckSubmitting => _checkSubmitting;
    public bool IsWorkAttributeSelectionActive => _workAttributeSelectionActive;
    public bool IsWorkActionInProgress => _workPreparing
        || _questCheckPreparing
        || _checkSubmitting
        || IsWorkOverlayBlocking
        || IsQuestCheckOverlayBlocking
        || _questTargetSubmissionSubmitting
        || IsQuestTargetSubmissionOverlayBlocking;
    public bool IsWorkOverlayBlocking => GodotObject.IsInstanceValid(_workDiceOverlay)
        && _workDiceOverlay.IsOpen
        && _workDiceOverlay.State != WorkDiceOverlayState.Result;
    public bool IsQuestCheckOverlayBlocking => GodotObject.IsInstanceValid(_questCheckOverlay)
        && _questCheckOverlay.IsOpen;

    private void InitializeWorkFlow()
    {
        _workDiceOverlay = GetNode<WorkDiceOverlay>("WorkDiceOverlay");
        _hud.WorkRequested += OnWorkRequested;
        _workDiceOverlay.ConfirmRequested += OnWorkConfirmRequested;
        _workDiceOverlay.RerollRequested += OnWorkRerollRequested;
        _workDiceOverlay.RecoveryRetryRequested += OnWorkRecoveryRetryRequested;
        _workDiceOverlay.ResultDismissed += OnWorkResultDismissed;
        _workDiceOverlay.WorkAttributeSelected += OnWorkAttributeSelected;
        _workDiceOverlay.WorkAttributeSelectionCancelled += OnWorkAttributeSelectionCancelled;
    }

    private void DisposeWorkFlow()
    {
        if (GodotObject.IsInstanceValid(_hud)) _hud.WorkRequested -= OnWorkRequested;
        if (!GodotObject.IsInstanceValid(_workDiceOverlay)) return;
        _workDiceOverlay.ConfirmRequested -= OnWorkConfirmRequested;
        _workDiceOverlay.RerollRequested -= OnWorkRerollRequested;
        _workDiceOverlay.RecoveryRetryRequested -= OnWorkRecoveryRetryRequested;
        _workDiceOverlay.ResultDismissed -= OnWorkResultDismissed;
        _workDiceOverlay.WorkAttributeSelected -= OnWorkAttributeSelected;
        _workDiceOverlay.WorkAttributeSelectionCancelled -= OnWorkAttributeSelectionCancelled;
    }

    private void ResetWorkFlowForStart()
    {
        _pendingCheckSequence++;
        _workPreparing = false;
        _checkSubmitting = false;
        _workPreparationUncertain = false;
        _workAttributeSelectionActive = false;
        _workAttributeSelectionSequence++;
        _workAttributeSelectionPlayerId = 0;
        _knownWorkCheckId = 0;
        _knownPendingBusinessType = "";
        _resumeNormalMoveAfterForestResult = false;
        _workPendingCheck = null;
        _workAttributeSelectionActive = false;
        _workAttributeSelectionSequence++;
        _workAttributeSelectionPlayerId = 0;
        _knownPendingBusinessType = "";
        _resumeNormalMoveAfterForestResult = false;
        _unresolvedRerollRequest = null;
        _unresolvedRollRequest = null;
        _processedCheckResults.Clear();
        _workDiceOverlay.CloseOverlay();
        _hud.SetWorkSubmitting(false);
        _hud.SetWorkBlocked(null);
    }

    private void StopWorkFlow()
    {
        _pendingCheckSequence++;
        _workPreparing = false;
        _checkSubmitting = false;
        _workPendingCheck = null;
        _unresolvedRerollRequest = null;
        _unresolvedRollRequest = null;
        if (GodotObject.IsInstanceValid(_workDiceOverlay)) _workDiceOverlay.CloseOverlay();
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.SetWorkSubmitting(false);
            _hud.SetWorkBlocked(null);
        }
    }

    public Task BeginWorkAsync()
    {
        if (!_active
            || !IsGameConnectionReady
            || _workPreparing
            || _checkSubmitting
            || _workPreparationUncertain
            || _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || _moveState == PlayerMoveState.SubmittingMove
            || _latestPlayerView == null
            || _latestPlayerView.EngineState?.PendingCheck != null
            || !TryGetCurrentTurnPlayer(out var currentPlayer)
            || !IsActionAvailable("WORK"))
        {
            return _workTask;
        }

        _workPreparing = true;
        ExitMoveMode();
        _hud.SetWorkSubmitting(true);
        _hud.SetActionControlsLocked(true);
        _hudErrorState.Visible = false;
        _workTask = PrepareWorkCoreAsync(
            _gameId,
            currentPlayer.PlayerId,
            _latestPlayerView.StateVersion,
            _generation);
        return _workTask;
    }

    public Task RefreshPendingWorkCheckAsync(bool knownRecovery = false)
    {
        if (!_active || _gameId <= 0)
        {
            return Task.CompletedTask;
        }

        if (!_pendingCheckTask.IsCompleted)
        {
            return _pendingCheckTask;
        }

        var sequence = ++_pendingCheckSequence;
        _pendingCheckTask = RefreshPendingWorkCheckCoreAsync(_gameId, _generation, sequence, knownRecovery);
        return _pendingCheckTask;
    }

    public Task SubmitSelectedWorkAttributeAsync(string attributeType)
    {
        if (!_active
            || !IsGameConnectionReady
            || _workPreparing
            || _checkSubmitting
            || !_workAttributeSelectionActive
            || _workDiceOverlay.State != WorkDiceOverlayState.SelectingAttribute
            || !WorkAttributeSelectionModel.TryNormalizeSelectable(attributeType, out var normalized))
        {
            return _workTask;
        }

        _workPreparing = true;
        _hud.SetWorkSubmitting(true);
        _hud.SetActionControlsLocked(true);
        _hudErrorState.Visible = false;
        _workDiceOverlay.SetAttributeSelectionSubmitting();
        _workTask = SubmitSelectedWorkAttributeCoreAsync(
            normalized,
            _workAttributeSelectionSequence,
            _generation);
        return _workTask;
    }

    public void CancelWorkAttributeSelection()
    {
        if (!_workAttributeSelectionActive
            || _workPreparing
            || _workDiceOverlay.State != WorkDiceOverlayState.SelectingAttribute)
        {
            return;
        }

        CloseWorkAttributeSelection();
    }

    public Task SubmitWorkRerollAsync()
    {
        if (!_active
            || !IsGameConnectionReady
            || _checkSubmitting
            || _workPendingCheck == null
            || _workPendingCheck.CheckId is not > 0
            || _workPendingCheck.StateVersion == null
            || _workPendingCheck.AuthorizationRound == null
            || _workDiceOverlay.State != WorkDiceOverlayState.AwaitingDecision
            || _workPendingCheck.CanReroll != true)
        {
            return _checkTask;
        }

        _checkSubmitting = true;
        _hud.SetActionControlsLocked(true);
        _workDiceOverlay.SetSubmitting("正在申请服务端重投授权……");
        var request = _unresolvedRerollRequest ?? new CheckRerollRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            StateVersion = _workPendingCheck.StateVersion.Value,
            AuthorizationRound = _workPendingCheck.AuthorizationRound.Value
        };
        _unresolvedRerollRequest = request;
        _checkTask = SubmitWorkRerollCoreAsync(
            _gameId,
            _workPendingCheck.CheckId.Value,
            request,
            _generation);
        return _checkTask;
    }

    public Task SubmitWorkRollAsync(int dice)
    {
        if (!_active
            || !IsGameConnectionReady
            || _checkSubmitting
            || dice is < 1 or > 6
            || _workPendingCheck == null
            || _workPendingCheck.CheckId is not > 0
            || _workPendingCheck.StateVersion == null
            || _workPendingCheck.AuthorizationRound == null
            || _workPendingCheck.CanConfirm != true
            || _workDiceOverlay.State != WorkDiceOverlayState.AwaitingDecision)
        {
            return _checkTask;
        }

        _checkSubmitting = true;
        _hud.SetActionControlsLocked(true);
        _workDiceOverlay.SetSubmitting("正在提交最终点数并等待服务端结算……");
        var request = _unresolvedRollRequest;
        if (request == null)
        {
            request = new CheckRollRequestDto
            {
                RequestId = CreateGameActionRequestId(),
                StateVersion = _workPendingCheck.StateVersion.Value,
                AuthorizationRound = _workPendingCheck.AuthorizationRound.Value,
                Dice = dice
            };
            _unresolvedRollRequest = request;
        }
        else if (request.Dice != dice)
        {
            _checkSubmitting = false;
            _workDiceOverlay.RestoreDecision(_workPendingCheck, "原确认请求的点数不能改变，请刷新判定状态。");
            return Task.CompletedTask;
        }

        _checkTask = SubmitWorkRollCoreAsync(
            _gameId,
            _workPendingCheck.CheckId.Value,
            request,
            _generation);
        return _checkTask;
    }

    private async Task PrepareWorkCoreAsync(long gameId, long playerId, long stateVersion, int generation)
    {
        try
        {
            ApiResult<CurrentWorkResponseDto> currentWork;
            try
            {
                currentWork = await ApiClient.GetCurrentWorkAsync(gameId, playerId);
            }
            catch (Exception exception)
            {
                currentWork = ApiResult<CurrentWorkResponseDto>.Failure(
                    $"打工信息请求异常：{exception.Message}", "WORK_CURRENT_REQUEST_EXCEPTION");
            }

            if (!IsResponseCurrent(generation)) return;
            if (!currentWork.Ok)
            {
                ShowGameStatusError(FirstNonEmpty(currentWork.Message, null, "无法查询当前位置的打工信息。"));
                return;
            }

            if (currentWork.Data == null)
            {
                await RefreshAvailableActionsAsync(true);
                ShowGameStatusError("当前位置没有可用的打工项目。");
                return;
            }

            if (!TryValidateCurrentWork(currentWork.Data, out var normalizedAttribute, out var validationError))
            {
                ShowGameStatusError(validationError);
                return;
            }

            if (WorkAttributeSelectionModel.IsAny(normalizedAttribute))
            {
                _workAttributeSelectionActive = true;
                _workAttributeSelectionPlayerId = playerId;
                _workAttributeSelectionSequence++;
                _workDiceOverlay.OpenAttributeSelection(currentWork.Data);
                return;
            }

            await SubmitWorkPreparationActionAsync(gameId, playerId, stateVersion, normalizedAttribute, generation);
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _workPreparing = false;
                _hud.SetWorkSubmitting(false);
                if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task SubmitSelectedWorkAttributeCoreAsync(
        string selectedAttribute,
        int selectionSequence,
        int generation)
    {
        try
        {
            await RefreshPlayerViewAsync();
            await RefreshAvailableActionsAsync(true);
            if (!IsWorkSelectionResponseCurrent(generation, selectionSequence)) return;

            if (!IsGameConnectionReady
                || _latestPlayerView == null
                || _latestPlayerView.EngineState?.PendingCheck != null
                || !TryGetCurrentTurnPlayer(out var currentPlayer)
                || currentPlayer.PlayerId != _workAttributeSelectionPlayerId
                || !IsActionAvailable("WORK"))
            {
                CloseWorkAttributeSelection();
                ShowGameStatusError("打工资格或对局状态已经变化，未提交打工行动。请同步后重试。");
                return;
            }

            ApiResult<CurrentWorkResponseDto> currentWork;
            try
            {
                currentWork = await ApiClient.GetCurrentWorkAsync(_gameId, currentPlayer.PlayerId);
            }
            catch (Exception exception)
            {
                currentWork = ApiResult<CurrentWorkResponseDto>.Failure(
                    $"打工信息请求异常：{exception.Message}", "WORK_CURRENT_REQUEST_EXCEPTION");
            }

            if (!IsWorkSelectionResponseCurrent(generation, selectionSequence)) return;
            if (!currentWork.Ok || currentWork.Data == null)
            {
                CloseWorkAttributeSelection();
                ShowGameStatusError(currentWork.Ok
                    ? "当前位置已没有可用的打工项目，未提交打工行动。"
                    : FirstNonEmpty(currentWork.Message, null, "无法重新核对当前位置的打工信息。"));
                return;
            }
            if (!TryValidateCurrentWork(currentWork.Data, out var refreshedAttribute, out var validationError))
            {
                CloseWorkAttributeSelection();
                ShowGameStatusError(validationError);
                return;
            }
            if (!WorkAttributeSelectionModel.IsAny(refreshedAttribute))
            {
                CloseWorkAttributeSelection();
                ShowGameStatusError("打工配置在选择期间已经变化，未提交打工行动。请重新打开打工菜单。");
                return;
            }

            // work/current 查询期间玩家视图可能被轮询更新；提交前再次把资格绑定到最新版本。
            await RefreshAvailableActionsAsync(true);
            if (!IsWorkSelectionResponseCurrent(generation, selectionSequence)
                || _latestPlayerView == null
                || !TryGetCurrentTurnPlayer(out currentPlayer)
                || currentPlayer.PlayerId != _workAttributeSelectionPlayerId
                || !IsActionAvailable("WORK"))
            {
                CloseWorkAttributeSelection();
                ShowGameStatusError("打工资格或版本已经变化，未提交打工行动。请同步后重试。");
                return;
            }

            await SubmitWorkPreparationActionAsync(
                _gameId,
                currentPlayer.PlayerId,
                _latestPlayerView.StateVersion,
                selectedAttribute,
                generation);
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _workPreparing = false;
                _hud.SetWorkSubmitting(false);
                if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task SubmitWorkPreparationActionAsync(
        long gameId,
        long playerId,
        long stateVersion,
        string attributeType,
        int generation)
    {
        var request = WorkAttributeSelectionModel.CreateWorkRequest(
            playerId,
            stateVersion,
            attributeType,
            CreateGameActionRequestId());

        ApiResult<GameActionResponseDto> action;
        try
        {
            action = await ApiClient.SubmitGameActionAsync(gameId, request);
        }
        catch (Exception exception)
        {
            action = ApiResult<GameActionResponseDto>.Failure(
                $"打工准备请求异常：{exception.Message}", "WORK_REQUEST_EXCEPTION");
        }

        if (!IsResponseCurrent(generation)) return;
        if (!action.Ok || action.Data == null)
        {
            var selectionSubmission = _workDiceOverlay.State == WorkDiceOverlayState.SubmittingAttribute;
            await HandleWorkPreparationFailureAsync(action, generation);
            if (selectionSubmission
                && IsResponseCurrent(generation)
                && _workDiceOverlay.State == WorkDiceOverlayState.SubmittingAttribute)
            {
                CloseWorkAttributeSelection();
            }
            return;
        }

        var error = "";
        PendingCheckDto pending;
        if (!string.Equals(action.Data.ActionType, "WORK", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(action.Data.ResultType, "CHECK_ROLL_PENDING", StringComparison.OrdinalIgnoreCase)
            || !TryValidatePendingWork(action.Data.PendingCheck, gameId, playerId, out pending, out error))
        {
            CloseWorkAttributeSelection();
            ShowGameStatusError(string.IsNullOrWhiteSpace(error)
                ? "打工准备响应与当前操作不匹配。"
                : error);
            return;
        }

        _stateVersionFloor = Math.Max(_stateVersionFloor, action.Data.NewStateVersion);
        if (action.Data.PlayerViewState != null
            && ValidatePlayerView(action.Data.PlayerViewState, gameId, _currentUserId, out _)
            && !IsOlderPlayerView(action.Data.PlayerViewState))
        {
            ApplyPlayerView(action.Data.PlayerViewState, false);
        }

        _workAttributeSelectionActive = false;
        _workAttributeSelectionPlayerId = 0;
        OpenPendingWork(pending, false);
        InvalidateAvailableActions();
        await RefreshPlayerViewToVersionFloorAsync();
        await RefreshAvailableActionsAsync(true);
    }

    private async Task HandleWorkPreparationFailureAsync(ApiResult<GameActionResponseDto> result, int generation)
    {
        var unknown = result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_ERROR" or "WORK_REQUEST_EXCEPTION" or "REQUEST_IN_PROGRESS";
        var existingPending = result.ErrorCode == "CHECK_ROLL_PENDING";
        if (unknown || existingPending)
        {
            var recovered = await QueryAndApplyPendingWorkAsync(generation, true);
            if (!recovered && IsResponseCurrent(generation))
            {
                _workPreparationUncertain = unknown;
                _hud.SetWorkBlocked(unknown ? "上一次打工准备结果无法确认，请勿重复提交。" : null);
                ShowGameStatusError(FirstNonEmpty(
                    result.Message,
                    null,
                    unknown
                        ? "打工准备结果无法确认，已查询待判定状态；请勿重复发起打工。"
                        : "已有待判定，但暂时无法恢复。"));
            }
            return;
        }

        InvalidateAvailableActions();
        await RefreshPlayerViewAsync();
        await RefreshAvailableActionsAsync(true);
        ShowGameStatusError(FirstNonEmpty(result.Message, null, "打工准备失败。"));
    }

    private async Task SubmitWorkRerollCoreAsync(
        long gameId,
        long checkId,
        CheckRerollRequestDto request,
        int generation)
    {
        ApiResult<PendingCheckDto> result;
        try
        {
            result = await ApiClient.RerollCheckAsync(gameId, checkId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<PendingCheckDto>.Failure(
                $"重投授权请求异常：{exception.Message}", "REROLL_REQUEST_EXCEPTION");
        }

        try
        {
            if (!IsResponseCurrent(generation)) return;
            var validationError = "";
            if (result.Ok
                && TryValidatePendingBusiness(
                    result.Data,
                    gameId,
                    null,
                    _workPendingCheck?.BusinessType,
                    out var pending,
                    out validationError)
                && pending.CheckId == checkId
                && pending.AuthorizationRound > request.AuthorizationRound)
            {
                _unresolvedRerollRequest = null;
                _unresolvedRollRequest = null;
                _workPendingCheck = pending;
                _stateVersionFloor = Math.Max(_stateVersionFloor, pending.StateVersion ?? 0);
                _workDiceOverlay.ApplyRerollAuthorization(pending);
                await RefreshPlayerViewAsync();
                await RefreshAvailableActionsAsync(true);
                return;
            }

            if (result.Ok)
            {
                ShowGameStatusError(string.IsNullOrWhiteSpace(validationError)
                    ? "服务端返回的重投授权未推进授权轮次。"
                    : validationError);
                _unresolvedRerollRequest = null;
            }
            else
            {
                var unknown = result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_ERROR" or "REROLL_REQUEST_EXCEPTION" or "REQUEST_IN_PROGRESS";
                if (!unknown) _unresolvedRerollRequest = null;
                var recovered = await QueryAndApplyPendingWorkAsync(generation, true, preserveLocalDice: true);
                if (recovered && _workPendingCheck != null && _workDiceOverlay.State != WorkDiceOverlayState.AwaitingRoll)
                {
                    _workDiceOverlay.RestoreDecision(
                        _workPendingCheck,
                        unknown
                            ? "重投授权结果无法确认；若再次提交，将复用原请求身份。"
                            : FirstNonEmpty(result.Message, null, "重投授权失败。"));
                }
                else if (!recovered)
                {
                    ShowGameStatusError(FirstNonEmpty(result.Message, null, "重投授权失败。"));
                }
            }
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _checkSubmitting = false;
                if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task SubmitWorkRollCoreAsync(
        long gameId,
        long checkId,
        CheckRollRequestDto request,
        int generation)
    {
        ApiResult<CheckRollResponseDto> result;
        try
        {
            result = await ApiClient.SubmitCheckRollAsync(gameId, checkId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<CheckRollResponseDto>.Failure(
                $"最终确认请求异常：{exception.Message}", "ROLL_REQUEST_EXCEPTION");
        }

        try
        {
            if (!IsResponseCurrent(generation)) return;
            if (result.Ok && result.Data != null)
            {
                var businessType = (_workPendingCheck?.BusinessType ?? _knownPendingBusinessType)
                    ?.Trim().ToUpperInvariant() ?? "";
                var forestMove = businessType == "FOREST_MOVE";
                var questCheck = businessType == "QUEST";
                var valid = forestMove
                    ? TryValidateForestMoveResult(result.Data, checkId, out var validationError)
                    : questCheck
                        ? TryValidateQuestResult(result.Data, checkId, out validationError)
                        : TryValidateWorkResult(result.Data, checkId, out validationError);
                if (!valid)
                {
                    _unresolvedRollRequest = null;
                    await QueryAndApplyPendingWorkAsync(generation, true, preserveLocalDice: true);
                    if (_workDiceOverlay.State != WorkDiceOverlayState.Result)
                    {
                        _workDiceOverlay.ShowRecoveryError(validationError);
                    }
                    return;
                }

                var resultKey = $"{checkId}:{request.RequestId}";
                if (!_processedCheckResults.Add(resultKey)) return;
                _unresolvedRollRequest = null;
                _unresolvedRerollRequest = null;
                _workPendingCheck = null;
                _knownWorkCheckId = checkId;
                _knownPendingBusinessType = forestMove ? "FOREST_MOVE" : questCheck ? "QUEST" : "WORK";
                var view = result.Data.PlayerViewState;
                if (view != null
                    && ValidatePlayerView(view, gameId, _currentUserId, out _)
                    && !IsOlderPlayerView(view))
                {
                    ApplyPlayerView(view, false);
                }

                if (forestMove)
                {
                    _resumeNormalMoveAfterForestResult = true;
                    _workDiceOverlay.ShowForestMoveResult(result.Data);
                }
                else if (questCheck)
                {
                    _returnToQuestChecksAfterResult = true;
                    _workDiceOverlay.ShowQuestResult(result.Data);
                }
                else
                {
                    _workDiceOverlay.ShowWorkResult(result.Data);
                }
                InvalidateAvailableActions();
                await RefreshPlayerViewToVersionFloorAsync();
                await RefreshAvailableActionsAsync(true);
                return;
            }

            var unknown = result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_ERROR" or "ROLL_REQUEST_EXCEPTION" or "REQUEST_IN_PROGRESS";
            if (!unknown) _unresolvedRollRequest = null;
            var recovered = await QueryAndApplyPendingWorkAsync(generation, true, preserveLocalDice: true);
            if (recovered && _workPendingCheck != null)
            {
                _workDiceOverlay.RestoreDecision(
                    _workPendingCheck,
                    unknown
                        ? "确认结果无法确认；再次确认将复用原请求身份，不会创建新请求。"
                        : FirstNonEmpty(result.Message, null, "最终确认失败，请按最新状态重试。"));
            }
            else if (!recovered && _workDiceOverlay.State != WorkDiceOverlayState.Result)
            {
                ShowGameStatusError(FirstNonEmpty(result.Message, null, "最终确认结果无法确认。"));
            }
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _checkSubmitting = false;
                if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task RefreshPendingWorkCheckCoreAsync(
        long gameId,
        int generation,
        int sequence,
        bool knownRecovery)
    {
        await QueryAndApplyPendingWorkAsync(generation, knownRecovery, sequence: sequence);
    }

    private async Task<bool> QueryAndApplyPendingWorkAsync(
        int generation,
        bool knownRecovery,
        bool preserveLocalDice = false,
        int? sequence = null)
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

        if (!IsResponseCurrent(generation)
            || (sequence.HasValue && sequence.Value != _pendingCheckSequence))
        {
            return false;
        }

        if (!result.Ok)
        {
            if (knownRecovery || IsWorkOverlayBlocking)
            {
                EnsureWorkOverlayForRecovery();
                _workDiceOverlay.ShowRecoveryError(
                    $"无法确认当前判定状态：{FirstNonEmpty(result.Message, null, "请重试同步。")}");
            }
            return false;
        }

        if (result.Data == null)
        {
            _workPendingCheck = null;
            if (knownRecovery
                && (_knownWorkCheckId > 0 || _workPreparationUncertain || _workDiceOverlay.IsOpen))
            {
                EnsureWorkOverlayForRecovery();
                _workDiceOverlay.ShowRecoveryUnavailable("原判定已不再待处理，无法恢复详细结果");
                await RefreshPlayerViewAsync();
                await RefreshAvailableActionsAsync(true);
            }
            return false;
        }

        var error = "玩家视图中未找到当前玩家。";
        if (!TryGetCurrentPlayer(out var player)
            || !TryValidatePendingBusiness(
                result.Data,
                _gameId,
                player.PlayerId,
                expectedBusinessType: null,
                out var pending,
                out error))
        {
            if (knownRecovery || IsWorkOverlayBlocking)
            {
                EnsureWorkOverlayForRecovery();
                _workDiceOverlay.ShowRecoveryError(error);
            }
            return false;
        }

        if (preserveLocalDice
            && _workPendingCheck?.CheckId == pending.CheckId
            && _workDiceOverlay.PhysicalDice is >= 1 and <= 6
            && pending.AuthorizationRound == _workPendingCheck!.AuthorizationRound)
        {
            _workPendingCheck = pending;
            _knownWorkCheckId = pending.CheckId!.Value;
            _workDiceOverlay.UpdatePendingCapabilities(pending);
        }
        else
        {
            OpenPendingWork(pending, true);
        }
        return true;
    }

    private void HandlePlayerViewForWork(PlayerViewResponseDto playerView)
    {
        var pending = playerView.EngineState?.PendingCheck;
        if (pending != null
            && (string.Equals(pending.BusinessType, "WORK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pending.BusinessType, "QUEST", StringComparison.OrdinalIgnoreCase)
                || (SupportsMovementV2
                    && string.Equals(pending.BusinessType, "FOREST_MOVE", StringComparison.OrdinalIgnoreCase)))
            && TryGetCurrentPlayer(out var player)
            && TryValidatePendingBusiness(
                pending,
                _gameId,
                player.PlayerId,
                pending.BusinessType,
                out var validated,
                out _))
        {
            if (IsGameConnectionReady)
            {
                if (!_workDiceOverlay.IsOpen)
                {
                    OpenPendingWork(validated, true);
                }
                else if (_workDiceOverlay.State is not (WorkDiceOverlayState.Result or WorkDiceOverlayState.Submitting))
                {
                    _workPendingCheck = validated;
                    _knownWorkCheckId = validated.CheckId!.Value;
                    _workDiceOverlay.UpdatePendingCapabilities(validated);
                }
            }
            return;
        }

        if (_knownWorkCheckId > 0
            && IsWorkOverlayBlocking
            && !_checkSubmitting
            && _pendingCheckTask.IsCompleted)
        {
            _ = RefreshPendingWorkCheckAsync(true);
        }
    }

    private void OpenPendingWork(PendingCheckDto pending, bool recovered)
    {
        if (GodotObject.IsInstanceValid(_questCheckOverlay))
        {
            _questCheckOverlay.CloseOverlay(false);
        }
        _workAttributeSelectionActive = false;
        _workAttributeSelectionPlayerId = 0;
        _workPendingCheck = pending;
        _knownWorkCheckId = pending.CheckId!.Value;
        _knownPendingBusinessType = pending.BusinessType?.Trim().ToUpperInvariant() ?? "";
        if (string.Equals(_knownPendingBusinessType, "FOREST_MOVE", StringComparison.Ordinal))
        {
            _resumeNormalMoveAfterForestResult = true;
        }
        if (string.Equals(_knownPendingBusinessType, "QUEST", StringComparison.Ordinal))
        {
            _expectedQuestPlayerQuestId = pending.BusinessSourceId ?? 0;
            _returnToQuestChecksAfterResult = true;
        }
        _unresolvedRerollRequest = null;
        _unresolvedRollRequest = null;
        _workPreparationUncertain = false;
        _hud.SetWorkBlocked(null);
        ExitMoveMode();
        _hud.SetActionControlsLocked(true);
        _workDiceOverlay.OpenPending(pending, recovered);
    }

    private void CloseWorkAttributeSelection()
    {
        _workAttributeSelectionActive = false;
        _workAttributeSelectionPlayerId = 0;
        _workAttributeSelectionSequence++;
        if (GodotObject.IsInstanceValid(_workDiceOverlay)
            && _workDiceOverlay.State is WorkDiceOverlayState.SelectingAttribute
                or WorkDiceOverlayState.SubmittingAttribute)
        {
            _workDiceOverlay.CloseOverlay();
        }
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.SetWorkSubmitting(false);
            _hud.SetActionControlsLocked(false);
        }
    }

    private void HandleWorkConnectionInterrupted()
    {
        if (_workDiceOverlay.State == WorkDiceOverlayState.SelectingAttribute)
        {
            CloseWorkAttributeSelection();
            ShowGameStatusError("对局连接已中断，本次属性选择已取消，未提交打工行动。");
        }
    }

    private bool IsWorkSelectionResponseCurrent(int generation, int selectionSequence) =>
        IsResponseCurrent(generation)
        && _workAttributeSelectionActive
        && selectionSequence == _workAttributeSelectionSequence
        && _workDiceOverlay.State == WorkDiceOverlayState.SubmittingAttribute;

    private static bool TryValidateCurrentWork(
        CurrentWorkResponseDto currentWork,
        out string normalizedAttribute,
        out string error)
    {
        normalizedAttribute = currentWork.AttributeType?.Trim().ToUpperInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedAttribute)
            || currentWork.Difficulty == null
            || currentWork.SocialCost == null
            || currentWork.RewardCoins == null)
        {
            error = "服务端返回的打工配置不完整，未提交打工行动。";
            return false;
        }

        if (!WorkAttributeSelectionModel.IsAny(normalizedAttribute)
            && !WorkAttributeSelectionModel.TryNormalizeSelectable(normalizedAttribute, out normalizedAttribute))
        {
            error = $"服务端返回了未知打工属性“{currentWork.AttributeType?.Trim()}”，未提交打工行动。";
            return false;
        }

        error = "";
        return true;
    }

    private void EnsureWorkOverlayForRecovery()
    {
        if (_workDiceOverlay.IsOpen) return;
        _hud.SetActionControlsLocked(true);
        _workDiceOverlay.OpenPending(_workPendingCheck ?? new PendingCheckDto
        {
            CheckId = _knownWorkCheckId > 0 ? _knownWorkCheckId : null,
            BusinessType = string.IsNullOrWhiteSpace(_knownPendingBusinessType)
                ? "WORK"
                : _knownPendingBusinessType,
            Status = "WAITING_ROLL"
        }, true);
    }

    private bool TryHandleForestMovePreparation(
        GameActionResponseDto response,
        long gameId,
        long playerId,
        out string error)
    {
        error = "";
        if (!SupportsMovementV2)
        {
            error = "新鲜 player-view 未声明 movementRulesVersion=2，未启用森林移动判定。";
            return false;
        }
        if (!string.Equals(response.ActionType, "MOVE_ONE_STEP", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(response.ResultType, "CHECK_ROLL_PENDING", StringComparison.OrdinalIgnoreCase)
            || !TryValidatePendingForestMove(response.PendingCheck, gameId, playerId, out var pending, out error))
        {
            if (string.IsNullOrWhiteSpace(error)) error = "森林移动准备响应与当前操作不匹配。";
            return false;
        }

        _stateVersionFloor = Math.Max(_stateVersionFloor, response.NewStateVersion);
        _resumeNormalMoveAfterForestResult = true;
        OpenPendingWork(pending, false);
        if (response.PlayerViewState != null
            && ValidatePlayerView(response.PlayerViewState, gameId, _currentUserId, out _)
            && !IsOlderPlayerView(response.PlayerViewState))
        {
            ApplyPlayerView(response.PlayerViewState, false);
        }
        error = "";
        return true;
    }

    private bool TryValidatePendingBusiness(
        PendingCheckDto? source,
        long gameId,
        long? playerId,
        string? expectedBusinessType,
        out PendingCheckDto pending,
        out string error)
    {
        var type = source?.BusinessType?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedBusinessType)
            && !string.Equals(type, expectedBusinessType, StringComparison.OrdinalIgnoreCase))
        {
            pending = source!;
            error = "服务端返回的待判定业务类型发生变化。";
            return false;
        }

        if (string.Equals(type, "WORK", StringComparison.OrdinalIgnoreCase))
        {
            return TryValidatePendingWork(source, gameId, playerId, out pending, out error);
        }
        if (string.Equals(type, "FOREST_MOVE", StringComparison.OrdinalIgnoreCase))
        {
            if (!SupportsMovementV2)
            {
                pending = source!;
                error = "服务端未通过新鲜 player-view 启用森林移动 v2。";
                return false;
            }
            return TryValidatePendingForestMove(source, gameId, playerId, out pending, out error);
        }
        if (string.Equals(type, "QUEST", StringComparison.OrdinalIgnoreCase))
        {
            return QuestCheckModel.TryValidatePendingQuest(
                source,
                gameId,
                playerId,
                expectedPlayerQuestId: null,
                expectedQuestId: null,
                out pending,
                out error);
        }

        pending = source!;
        error = "当前待判定不是客户端已接入的 QUEST、WORK 或 FOREST_MOVE。";
        return false;
    }

    private static bool TryValidatePendingForestMove(
        PendingCheckDto? source,
        long gameId,
        long? playerId,
        out PendingCheckDto pending,
        out string error)
    {
        pending = source!;
        if (source == null
            || source.CheckId is not > 0
            || source.StateVersion == null
            || source.AuthorizationRound == null
            || !string.Equals(source.BusinessType, "FOREST_MOVE", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.Status, "WAITING_ROLL", StringComparison.OrdinalIgnoreCase))
        {
            error = "服务端返回的森林待判定缺少必要身份、版本或状态字段。";
            return false;
        }
        if (source.GameId is > 0 && source.GameId != gameId)
        {
            error = "服务端返回了其他对局的森林待判定。";
            return false;
        }
        if (playerId.HasValue && source.PlayerId is > 0 && source.PlayerId != playerId)
        {
            error = "服务端返回了其他玩家的森林待判定。";
            return false;
        }

        var preparation = source.Preparation;
        if (preparation?.FromCellId is not > 0
            || preparation.RequestedTargetCellId is not > 0
            || preparation.FromQ == null
            || preparation.FromR == null
            || preparation.TargetQ == null
            || preparation.TargetR == null
            || preparation.DirectionIndex is not (>= 0 and <= 5)
            || preparation.CostSpeed == null
            || preparation.CostCoins == null
            || preparation.SourceId is not > 0
            || preparation.CellId is not > 0
            || preparation.SocialCost == null
            || !string.Equals(preparation.BusinessType, "FOREST_MOVE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(preparation.OperationId))
        {
            error = "服务端返回的森林 preparation 不完整，不能推断方向或费用。";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryValidatePendingWork(
        PendingCheckDto? source,
        long gameId,
        long? playerId,
        out PendingCheckDto pending,
        out string error)
    {
        pending = source!;
        if (source == null)
        {
            error = "服务端未返回待判定快照。";
            return false;
        }
        if (source.CheckId is not > 0
            || source.StateVersion == null
            || source.AuthorizationRound == null
            || string.IsNullOrWhiteSpace(source.BusinessType)
            || string.IsNullOrWhiteSpace(source.Status))
        {
            error = "服务端返回的待判定快照缺少必要字段。";
            return false;
        }
        if (!string.Equals(source.BusinessType, "WORK", StringComparison.OrdinalIgnoreCase))
        {
            error = "当前待判定不是打工判定，本界面不会处理。";
            return false;
        }
        if (!string.Equals(source.Status, "WAITING_ROLL", StringComparison.OrdinalIgnoreCase))
        {
            error = "当前打工判定已不处于等待投骰状态。";
            return false;
        }
        if (source.GameId is > 0 && source.GameId != gameId)
        {
            error = "服务端返回了其他对局的待判定。";
            return false;
        }
        if (playerId.HasValue && source.PlayerId is > 0 && source.PlayerId != playerId)
        {
            error = "服务端返回了其他玩家的待判定。";
            return false;
        }
        error = "";
        return true;
    }

    private static bool TryValidateWorkResult(CheckRollResponseDto response, long checkId, out string error)
    {
        if (response.PendingCheck?.CheckId != checkId
            || !string.Equals(response.PendingCheck.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            error = "服务端最终回执缺少匹配的已完成判定。";
            return false;
        }
        if (response.SubmittedDice is not (>= 1 and <= 6)
            || response.EffectiveDice is not (>= 1 and <= 6)
            || response.CheckResult?.Success == null)
        {
            error = "服务端最终回执缺少有效骰点或判定结果。";
            return false;
        }
        var work = response.WorkResult;
        var log = work?.CheckLog;
        if (work?.SocialCost == null
            || work.RemainingSocial == null
            || work.RewardCoins == null
            || work.CoinsAfter == null
            || work.ForceEndTurn == null
            || log?.Dice == null
            || string.IsNullOrWhiteSpace(log.AttributeType)
            || string.IsNullOrWhiteSpace(log.AttributeTypeText)
            || log.AttributeValue == null
            || log.Modifier == null
            || log.FinalValue == null
            || log.Difficulty == null
            || log.Success == null)
        {
            error = "服务端 WORK 结算回执字段不完整，不能把缺失值当作失败或 0 金币。";
            return false;
        }
        if (log.Success != response.CheckResult.Success || log.Dice != response.EffectiveDice)
        {
            error = "服务端 WORK 结算日志与判定结果不一致。";
            return false;
        }
        error = "";
        return true;
    }

    private static bool TryValidateForestMoveResult(
        CheckRollResponseDto response,
        long checkId,
        out string error)
    {
        if (response.PendingCheck?.CheckId != checkId
            || !string.Equals(response.PendingCheck.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            error = "服务端森林最终回执缺少匹配的已完成判定。";
            return false;
        }
        if (response.SubmittedDice is not (>= 1 and <= 6)
            || response.EffectiveDice is not (>= 1 and <= 6)
            || response.CheckResult != null
            || response.Movement == null)
        {
            error = "服务端森林最终回执缺少有效骰点、位移，或错误返回了属性判定。";
            return false;
        }

        var movement = response.Movement;
        var moved = string.Equals(movement.MovementOutcome, "MOVED", StringComparison.OrdinalIgnoreCase);
        var stayed = string.Equals(movement.MovementOutcome, "STAYED", StringComparison.OrdinalIgnoreCase);
        if (movement.FromCellId <= 0
            || movement.RequestedTargetCellId <= 0
            || movement.ActualTargetCellId <= 0
            || (!moved && !stayed)
            || (stayed && movement.ActualTargetCellId != movement.FromCellId))
        {
            error = "服务端森林 movement 缺少合法身份或 MOVED/STAYED 结果。";
            return false;
        }

        error = "";
        return true;
    }

    private bool TryValidateQuestResult(
        CheckRollResponseDto response,
        long checkId,
        out string error)
    {
        if (response.PendingCheck?.CheckId != checkId
            || !string.Equals(response.PendingCheck.BusinessType, "QUEST", StringComparison.OrdinalIgnoreCase)
            || response.PendingCheck.BusinessSourceId is not > 0
            || response.PendingCheck.QuestId is not > 0
            || !string.Equals(response.PendingCheck.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            error = "服务端任务最终回执缺少匹配的已完成判定身份。";
            return false;
        }
        if (_expectedQuestPlayerQuestId > 0
            && response.PendingCheck.BusinessSourceId != _expectedQuestPlayerQuestId)
        {
            error = "服务端任务最终回执不属于当前任务实例。";
            return false;
        }
        if (response.SubmittedDice is not (>= 1 and <= 6)
            || response.EffectiveDice is not (>= 1 and <= 6)
            || response.CheckResult?.Success == null)
        {
            error = "服务端任务最终回执缺少有效骰点或判定结果。";
            return false;
        }
        error = "";
        return true;
    }

    private void OnWorkRequested() => _ = BeginWorkAsync();
    private void OnWorkAttributeSelected(string attributeType) => _ = SubmitSelectedWorkAttributeAsync(attributeType);
    private void OnWorkAttributeSelectionCancelled() => CancelWorkAttributeSelection();
    private void OnWorkConfirmRequested(int dice) => _ = SubmitWorkRollAsync(dice);
    private void OnWorkRerollRequested() => _ = SubmitWorkRerollAsync();
    private void OnWorkRecoveryRetryRequested() => _ = RefreshPendingWorkCheckAsync(true);

    private void OnWorkResultDismissed()
    {
        var resumeForestMove = _resumeNormalMoveAfterForestResult
            && string.Equals(_knownPendingBusinessType, "FOREST_MOVE", StringComparison.OrdinalIgnoreCase);
        var returnToQuestChecks = _returnToQuestChecksAfterResult
            && string.Equals(_knownPendingBusinessType, "QUEST", StringComparison.OrdinalIgnoreCase);
        _workDiceOverlay.CloseOverlay();
        _workPendingCheck = null;
        _knownWorkCheckId = 0;
        _knownPendingBusinessType = "";
        _unresolvedRerollRequest = null;
        _unresolvedRollRequest = null;
        _workPreparationUncertain = false;
        _resumeNormalMoveAfterForestResult = false;
        _returnToQuestChecksAfterResult = false;
        _expectedQuestPlayerQuestId = 0;
        _hud.SetWorkBlocked(null);
        _hud.SetActionControlsLocked(false);
        // 结果层可能在最终回执后的只读同步结束前就被点击关闭。
        // 先等待本次确认任务完整收尾，再依据最新权威状态恢复普通移动，避免一次竞态永久丢失恢复机会。
        _ = ResumeAfterCheckResultAsync(resumeForestMove, returnToQuestChecks, _generation);
    }

    private async Task ResumeAfterCheckResultAsync(bool resumeForestMove, bool returnToQuestChecks, int generation)
    {
        while (_checkSubmitting && IsResponseCurrent(generation))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!IsResponseCurrent(generation))
        {
            return;
        }
        await RefreshPlayerViewAsync();
        await RefreshAvailableActionsAsync(true);
        if (returnToQuestChecks && IsResponseCurrent(generation))
        {
            await ReturnToQuestChecksAfterResultAsync(generation);
            return;
        }
        if (!resumeForestMove || !IsResponseCurrent(generation) || !SupportsMovementV2)
        {
            return;
        }
        if (TryGetCurrentTurnPlayer(out var player)
            && player.CurrentCellId.HasValue
            && IsActionAvailable("MOVE_ONE_STEP"))
        {
            TryEnterMoveMode(player.PlayerId);
        }
    }
}
