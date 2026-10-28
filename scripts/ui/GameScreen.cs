using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class GameScreen : Control
{
    private GameMapScene _mapScene = null!;
    private GameHud _hud = null!;
    private Timer _playerViewTimer = null!;
    private Control _hudLoadingState = null!;
    private Label _hudLoadingLabel = null!;
    private Control _hudErrorState = null!;
    private Label _hudErrorLabel = null!;
    private Button _hudRetryButton = null!;
    private Control _fatalState = null!;
    private Label _fatalLabel = null!;

    private bool _active;
    private bool _playerViewRequestInProgress;
    private bool _availableActionsRequestInProgress;
    private bool _endTurnSubmitting;
    private bool _movableCellsRequestInProgress;
    private bool _hasPlayerView;
    private int _generation;
    private int _requestSequence;
    private int _availableActionsSequence;
    private int _moveRequestGeneration;
    private long _gameId;
    private long _currentUserId;
    private long _stateVersionFloor;
    private long _availableActionsStateVersion = -1;
    private PlayerViewResponseDto? _latestPlayerView;
    private List<AvailableActionDto> _latestAvailableActions = [];
    private Task _playerViewTask = Task.CompletedTask;
    private Task _availableActionsTask = Task.CompletedTask;
    private Task _endTurnTask = Task.CompletedTask;
    private Task _movableCellsTask = Task.CompletedTask;
    private Task _moveActionTask = Task.CompletedTask;
    private readonly HashSet<long> _movableCellIds = [];
    private PlayerMoveState _moveState = PlayerMoveState.Idle;
    private long _movePlayerId;
    private long? _moveCurrentCellId;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");

    public bool IsActive => _active;
    public bool IsPlayerViewRequestInProgress => _playerViewRequestInProgress;
    public bool IsAvailableActionsRequestInProgress => _availableActionsRequestInProgress;
    public bool IsEndTurnSubmitting => _endTurnSubmitting;
    public bool IsMovableCellsRequestInProgress => _movableCellsRequestInProgress;
    public bool PlayerViewErrorVisible => GodotObject.IsInstanceValid(_hudErrorState) && _hudErrorState.Visible;
    public string LastPlayerViewError { get; private set; } = "";
    public long GameId => _gameId;
    public long LatestStateVersion => _latestPlayerView?.StateVersion ?? 0;
    public PlayerViewResponseDto? LatestPlayerView => _latestPlayerView;
    public GameMapScene MapScene => _mapScene;
    public GameHud Hud => _hud;
    public PlayerMoveState MoveState => _moveState;
    public IReadOnlySet<long> MovableCellIds => _movableCellIds;

    public override void _Ready()
    {
        _mapScene = GetNode<GameMapScene>("GameMap");
        _hud = GetNode<GameHud>("GameHud");
        _playerViewTimer = GetNode<Timer>("PlayerViewTimer");
        _hudLoadingState = GetNode<Control>("StatusLayer/HudLoadingState");
        _hudLoadingLabel = GetNode<Label>("StatusLayer/HudLoadingState/LoadingPanel/LoadingMargin/LoadingLabel");
        _hudErrorState = GetNode<Control>("StatusLayer/HudErrorState");
        _hudErrorLabel = GetNode<Label>("StatusLayer/HudErrorState/ErrorPanel/ErrorMargin/ErrorContent/ErrorLabel");
        _hudRetryButton = GetNode<Button>("StatusLayer/HudErrorState/ErrorPanel/ErrorMargin/ErrorContent/RetryButton");
        _fatalState = GetNode<Control>("StatusLayer/FatalState");
        _fatalLabel = GetNode<Label>("StatusLayer/FatalState/FatalPanel/FatalMargin/FatalLabel");

        _playerViewTimer.WaitTime = 2.0;
        _playerViewTimer.OneShot = false;
        _playerViewTimer.Timeout += OnPlayerViewTimerTimeout;
        _hudRetryButton.Pressed += OnHudRetryPressed;
        _hud.EndTurnRequested += OnEndTurnRequested;
        _mapScene.MapCellClicked += OnMapCellClicked;
        _mapScene.MapEmptyClicked += OnMapEmptyClicked;
        _mapScene.PlayerTokenClicked += OnPlayerTokenClicked;
        _mapScene.MapReloading += OnMapReloading;
        _hudLoadingState.Visible = false;
        _hudErrorState.Visible = false;
        _fatalState.Visible = false;
    }

    public override void _ExitTree()
    {
        Stop();
        if (GodotObject.IsInstanceValid(_playerViewTimer))
        {
            _playerViewTimer.Timeout -= OnPlayerViewTimerTimeout;
        }

        if (GodotObject.IsInstanceValid(_hudRetryButton))
        {
            _hudRetryButton.Pressed -= OnHudRetryPressed;
        }

        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.EndTurnRequested -= OnEndTurnRequested;
        }

        if (GodotObject.IsInstanceValid(_mapScene))
        {
            _mapScene.MapCellClicked -= OnMapCellClicked;
            _mapScene.MapEmptyClicked -= OnMapEmptyClicked;
            _mapScene.PlayerTokenClicked -= OnPlayerTokenClicked;
            _mapScene.MapReloading -= OnMapReloading;
        }
    }

    public void Start()
    {
        if (_active)
        {
            return;
        }

        _active = true;
        _generation++;
        _requestSequence = 0;
        _availableActionsSequence = 0;
        _hasPlayerView = false;
        _latestPlayerView = null;
        _latestAvailableActions = [];
        _stateVersionFloor = 0;
        _availableActionsStateVersion = -1;
        _endTurnSubmitting = false;
        _movableCellsRequestInProgress = false;
        _moveRequestGeneration = 0;
        _moveState = PlayerMoveState.Idle;
        _movePlayerId = 0;
        _moveCurrentCellId = null;
        _movableCellIds.Clear();
        _hud.ClearPlayerView();
        _hud.SetActionControlsLocked(false);
        _hudErrorState.Visible = false;
        _fatalState.Visible = false;
        LastPlayerViewError = "";

        var gameId = Session.CurrentGameId;
        var currentUserId = Session.User?.UserId ?? 0;
        if (!gameId.HasValue || gameId.Value <= 0)
        {
            ShowFatalError("缺少有效的对局 ID，无法进入游戏。");
            return;
        }

        if (currentUserId <= 0)
        {
            ShowFatalError("缺少当前玩家信息，请重新登录。");
            return;
        }

        _gameId = gameId.Value;
        _currentUserId = currentUserId;
        _mapScene.GameId = _gameId;
        _mapScene.AutoLoadOnReady = false;
        _mapScene.RequireGameId = true;
        _hudLoadingLabel.Text = "正在加载玩家信息……";
        _hudLoadingState.Visible = true;
        _playerViewTimer.Start();

        _ = LoadMapSafelyAsync(_generation);
        _ = RefreshPlayerViewAsync(true);
    }

    public void Stop()
    {
        ExitMoveMode(true);
        _active = false;
        _generation++;
        _requestSequence++;
        _availableActionsSequence++;
        _playerViewRequestInProgress = false;
        _availableActionsRequestInProgress = false;
        _endTurnSubmitting = false;
        _movableCellsRequestInProgress = false;
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.SetEndTurnSubmitting(false);
            _hud.SetActionControlsLocked(false);
        }

        if (GodotObject.IsInstanceValid(_playerViewTimer))
        {
            _playerViewTimer.Stop();
        }
    }

    public Task RefreshPlayerViewAsync(bool initial = false)
    {
        if (!_active || _gameId <= 0 || _currentUserId <= 0)
        {
            return Task.CompletedTask;
        }

        if (_playerViewRequestInProgress)
        {
            return _playerViewTask;
        }

        _playerViewRequestInProgress = true;
        var generation = _generation;
        var sequence = ++_requestSequence;
        if (initial && !_hasPlayerView)
        {
            _hudLoadingState.Visible = true;
        }

        _hudRetryButton.Disabled = true;
        _playerViewTask = RefreshPlayerViewCoreAsync(_gameId, _currentUserId, generation, sequence);
        return _playerViewTask;
    }

    public Task RefreshAvailableActionsAsync(bool force = false)
    {
        if (!_active || !TryGetCurrentTurnPlayer(out var currentPlayer) || _latestPlayerView == null)
        {
            return Task.CompletedTask;
        }

        var stateVersion = _latestPlayerView.StateVersion;
        if (!force && _availableActionsStateVersion == stateVersion)
        {
            return Task.CompletedTask;
        }

        if (_availableActionsRequestInProgress)
        {
            return _availableActionsTask;
        }

        _availableActionsRequestInProgress = true;
        var generation = _generation;
        var sequence = ++_availableActionsSequence;
        _availableActionsTask = RefreshAvailableActionsCoreAsync(
            _gameId,
            currentPlayer.PlayerId,
            stateVersion,
            generation,
            sequence
        );
        return _availableActionsTask;
    }

    public Task SubmitEndTurnAsync()
    {
        if (!_active || _endTurnSubmitting || _latestPlayerView == null)
        {
            return _endTurnTask;
        }

        if (!TryGetCurrentTurnPlayer(out var currentPlayer)
            || _availableActionsStateVersion != _latestPlayerView.StateVersion
            || !_latestAvailableActions.Any(action =>
                string.Equals(action.ActionType, "END_TURN", StringComparison.OrdinalIgnoreCase)
                && action.Available))
        {
            return Task.CompletedTask;
        }

        _endTurnSubmitting = true;
        ExitMoveMode();
        _hud.SetEndTurnSubmitting(true);
        _hudErrorState.Visible = false;
        var request = new GameActionRequestDto
        {
            PlayerId = currentPlayer.PlayerId,
            StateVersion = _latestPlayerView.StateVersion,
            ActionType = "END_TURN",
            Payload = new JsonObject()
        };
        _endTurnTask = SubmitEndTurnCoreAsync(_gameId, request, _generation);
        return _endTurnTask;
    }

    public bool TryEnterMoveMode(long playerId)
    {
        if (!_active || _moveState != PlayerMoveState.Idle || _endTurnSubmitting || _latestPlayerView == null)
        {
            return false;
        }

        if (!TryGetCurrentTurnPlayer(out var currentPlayer)
            || currentPlayer.PlayerId != playerId
            || !currentPlayer.CurrentCellId.HasValue
            || currentPlayer.CurrentSpeedPoints <= 0
            || !IsActionAvailable("MOVE_ONE_STEP"))
        {
            return false;
        }

        _movePlayerId = currentPlayer.PlayerId;
        _moveCurrentCellId = currentPlayer.CurrentCellId;
        _moveState = PlayerMoveState.LoadingTargets;
        _movableCellIds.Clear();
        _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, true);
        _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
        _hudErrorState.Visible = false;
        _ = RefreshMovableCellsAsync();
        return true;
    }

    public Task RefreshMovableCellsAsync()
    {
        if (!_active
            || _moveState is PlayerMoveState.Idle or PlayerMoveState.SubmittingMove
            || _movePlayerId <= 0)
        {
            return Task.CompletedTask;
        }

        if (_movableCellsRequestInProgress)
        {
            return _movableCellsTask;
        }

        _moveState = PlayerMoveState.LoadingTargets;
        _movableCellsRequestInProgress = true;
        var screenGeneration = _generation;
        var moveGeneration = ++_moveRequestGeneration;
        _movableCellsTask = RefreshMovableCellsCoreAsync(
            _gameId,
            _movePlayerId,
            screenGeneration,
            moveGeneration
        );
        return _movableCellsTask;
    }

    public Task SubmitMoveAsync(long targetCellId)
    {
        if (!_active
            || _moveState != PlayerMoveState.SelectingTarget
            || !_movableCellIds.Contains(targetCellId)
            || _latestPlayerView == null
            || _endTurnSubmitting
            || !TryGetCurrentTurnPlayer(out var currentPlayer)
            || currentPlayer.PlayerId != _movePlayerId)
        {
            return _moveActionTask;
        }

        _moveState = PlayerMoveState.SubmittingMove;
        _hud.SetActionControlsLocked(true);
        _hudErrorState.Visible = false;
        var request = new GameActionRequestDto
        {
            PlayerId = currentPlayer.PlayerId,
            StateVersion = _latestPlayerView.StateVersion,
            ActionType = "MOVE_ONE_STEP",
            Payload = new JsonObject { ["targetCellId"] = targetCellId }
        };
        _moveActionTask = SubmitMoveCoreAsync(_gameId, request, _generation, ++_moveRequestGeneration);
        return _moveActionTask;
    }

    public void ExitMoveMode(bool immediate = false)
    {
        _moveRequestGeneration++;
        _movableCellsRequestInProgress = false;
        if (_movePlayerId > 0 && GodotObject.IsInstanceValid(_mapScene))
        {
            _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, false, immediate);
        }

        if (GodotObject.IsInstanceValid(_mapScene))
        {
            _mapScene.ClearMovementHighlights();
        }

        _movableCellIds.Clear();
        _moveState = PlayerMoveState.Idle;
        _movePlayerId = 0;
        _moveCurrentCellId = null;
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.SetActionControlsLocked(false);
        }
    }

    private async Task RefreshMovableCellsCoreAsync(
        long gameId,
        long playerId,
        int screenGeneration,
        int moveGeneration)
    {
        ApiResult<List<MapCellDto>> result;
        try
        {
            result = await ApiClient.GetMovableCellsAsync(gameId, playerId);
        }
        catch (Exception exception)
        {
            result = ApiResult<List<MapCellDto>>.Failure(
                $"可移动格请求异常：{exception.Message}",
                "MOVABLE_CELLS_REQUEST_EXCEPTION"
            );
        }

        if (!IsMoveResponseCurrent(screenGeneration, moveGeneration))
        {
            return;
        }

        _movableCellsRequestInProgress = false;
        if (!result.Ok || result.Data == null)
        {
            var message = FirstNonEmpty(result.Message, null, "无法取得可移动格，请重试。");
            ExitMoveMode();
            ShowGameStatusError(message);
            return;
        }

        _movableCellIds.Clear();
        foreach (var cell in result.Data)
        {
            if (cell.CellId > 0
                && cell.CellId != _moveCurrentCellId
                && _mapScene.CellsById.ContainsKey(cell.CellId))
            {
                _movableCellIds.Add(cell.CellId);
            }
        }

        if (_movableCellIds.Count == 0)
        {
            ExitMoveMode();
            return;
        }

        _moveState = PlayerMoveState.SelectingTarget;
        _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
    }

    private async Task SubmitMoveCoreAsync(
        long gameId,
        GameActionRequestDto request,
        int screenGeneration,
        int moveGeneration)
    {
        ApiResult<GameActionResponseDto> result;
        try
        {
            result = await ApiClient.SubmitGameActionAsync(gameId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<GameActionResponseDto>.Failure(
                $"移动请求异常：{exception.Message}",
                "MOVE_REQUEST_EXCEPTION"
            );
        }

        try
        {
            if (!IsMoveResponseCurrent(screenGeneration, moveGeneration, PlayerMoveState.SubmittingMove))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                await HandleMoveFailureAsync(result);
                return;
            }

            if (result.Data.GameId > 0 && result.Data.GameId != gameId)
            {
                ExitMoveMode();
                ShowGameStatusError("服务器返回了不匹配的移动结果。");
                return;
            }

            _stateVersionFloor = Math.Max(_stateVersionFloor, result.Data.NewStateVersion);
            InvalidateAvailableActions();
            var responseView = result.Data.PlayerViewState;
            if (responseView != null
                && ValidatePlayerView(responseView, gameId, _currentUserId, out _)
                && !IsOlderPlayerView(responseView))
            {
                ApplyPlayerView(responseView, false);
            }
            else
            {
                await RefreshPlayerViewToVersionFloorAsync();
            }

            if (_moveState == PlayerMoveState.Idle || !TryGetCurrentTurnPlayer(out var currentPlayer))
            {
                ExitMoveMode();
                return;
            }

            if (result.Data.NextAvailableActions != null && _latestPlayerView != null)
            {
                ApplyAvailableActions(result.Data.NextAvailableActions, _latestPlayerView.StateVersion);
            }
            else
            {
                await RefreshAvailableActionsAsync(true);
            }

            if (_latestPlayerView == null
                || _moveState == PlayerMoveState.Idle
                || currentPlayer.CurrentSpeedPoints <= 0
                || !IsActionAvailable("MOVE_ONE_STEP")
                || !currentPlayer.CurrentCellId.HasValue)
            {
                ExitMoveMode();
                return;
            }

            _moveCurrentCellId = currentPlayer.CurrentCellId;
            _movableCellIds.Clear();
            _moveState = PlayerMoveState.LoadingTargets;
            _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, true);
            _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
            LastPlayerViewError = "";
            _hudErrorState.Visible = false;
            await RefreshMovableCellsAsync();
        }
        finally
        {
            if (IsResponseCurrent(screenGeneration))
            {
                _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task HandleMoveFailureAsync(ApiResult<GameActionResponseDto> result)
    {
        var errorCode = result.ErrorCode ?? "";
        ExitMoveMode();
        InvalidateAvailableActions();
        await RefreshPlayerViewAsync();
        await RefreshAvailableActionsAsync(true);

        var fallback = errorCode switch
        {
            "GAME_STATE_VERSION_EXPIRED" => "状态已更新，请重新选择移动。",
            "GAME_NOT_CURRENT_PLAYER" => "当前不是你的回合",
            "ACTION_NOT_AVAILABLE" => "当前无法移动。",
            "CELL_NOT_ADJACENT" => "目标格不相邻，无法移动。",
            "EDGE_NOT_PASSABLE" => "当前道路无法通行。",
            _ => "移动结果无法确认，已刷新当前位置。"
        };
        ShowGameStatusError(FirstNonEmpty(result.Message, null, fallback));
    }

    private bool IsMoveResponseCurrent(
        int screenGeneration,
        int moveGeneration,
        PlayerMoveState? requiredState = null)
    {
        return IsResponseCurrent(screenGeneration)
            && moveGeneration == _moveRequestGeneration
            && (!requiredState.HasValue || _moveState == requiredState.Value);
    }

    private async Task LoadMapSafelyAsync(int generation)
    {
        try
        {
            await _mapScene.LoadMapAsync();
        }
        catch (Exception exception)
        {
            if (IsResponseCurrent(generation))
            {
                GD.PushError($"对局地图加载出现未处理异常：{exception.Message}");
            }
        }
    }

    private async Task RefreshPlayerViewCoreAsync(long gameId, long currentUserId, int generation, int sequence)
    {
        ApiResult<PlayerViewResponseDto> result;
        try
        {
            result = await ApiClient.GetPlayerViewAsync(gameId);
        }
        catch (Exception exception)
        {
            result = ApiResult<PlayerViewResponseDto>.Failure(
                $"玩家信息请求异常：{exception.Message}",
                "PLAYER_VIEW_REQUEST_EXCEPTION"
            );
        }

        if (!IsResponseCurrent(generation, sequence))
        {
            return;
        }

        _playerViewRequestInProgress = false;
        _hudRetryButton.Disabled = false;
        _hudLoadingState.Visible = false;

        if (!result.Ok || result.Data == null)
        {
            ShowPlayerViewError(result.Message);
            return;
        }

        if (!ValidatePlayerView(result.Data, gameId, currentUserId, out var validationError))
        {
            ShowPlayerViewError(validationError);
            return;
        }

        if (IsOlderPlayerView(result.Data))
        {
            return;
        }

        ApplyPlayerView(result.Data);
    }

    private async Task RefreshAvailableActionsCoreAsync(
        long gameId,
        long playerId,
        long stateVersion,
        int generation,
        int sequence)
    {
        ApiResult<List<AvailableActionDto>> result;
        try
        {
            result = await ApiClient.GetAvailableActionsAsync(gameId, playerId);
        }
        catch (Exception exception)
        {
            result = ApiResult<List<AvailableActionDto>>.Failure(
                $"可用行动请求异常：{exception.Message}",
                "AVAILABLE_ACTIONS_REQUEST_EXCEPTION"
            );
        }

        if (!IsResponseCurrent(generation))
        {
            return;
        }

        _availableActionsRequestInProgress = false;
        if (sequence != _availableActionsSequence
            || _latestPlayerView == null
            || _latestPlayerView.StateVersion != stateVersion
            || !TryGetCurrentTurnPlayer(out var currentPlayer)
            || currentPlayer.PlayerId != playerId)
        {
            _ = RefreshAvailableActionsAsync();
            return;
        }

        if (!result.Ok || result.Data == null)
        {
            _latestAvailableActions = [];
            _availableActionsStateVersion = -1;
            _hud.ClearAvailableActions();
            ShowGameStatusError($"可用行动同步失败：{result.Message}");
            return;
        }

        ApplyAvailableActions(result.Data, stateVersion);
    }

    private async Task SubmitEndTurnCoreAsync(long gameId, GameActionRequestDto request, int generation)
    {
        ApiResult<GameActionResponseDto> result;
        try
        {
            result = await ApiClient.SubmitGameActionAsync(gameId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<GameActionResponseDto>.Failure(
                $"结束回合请求异常：{exception.Message}",
                "END_TURN_REQUEST_EXCEPTION"
            );
        }

        try
        {
            if (!IsResponseCurrent(generation))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                await HandleEndTurnFailureAsync(result);
                return;
            }

            if (result.Data.GameId > 0 && result.Data.GameId != gameId)
            {
                ShowGameStatusError("服务器返回了不匹配的行动结果。");
                return;
            }

            _stateVersionFloor = Math.Max(_stateVersionFloor, result.Data.NewStateVersion);
            InvalidateAvailableActions();
            var responseView = result.Data.PlayerViewState;
            if (responseView != null
                && ValidatePlayerView(responseView, gameId, _currentUserId, out _)
                && !IsOlderPlayerView(responseView))
            {
                ApplyPlayerView(responseView, false);
                if (TryGetCurrentTurnPlayer(out _))
                {
                    if (result.Data.NextAvailableActions != null)
                    {
                        ApplyAvailableActions(result.Data.NextAvailableActions, responseView.StateVersion);
                    }
                    else
                    {
                        _ = RefreshAvailableActionsAsync();
                    }
                }
            }
            else
            {
                await RefreshPlayerViewToVersionFloorAsync();
            }

            LastPlayerViewError = "";
            _hudErrorState.Visible = false;
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _endTurnSubmitting = false;
                _hud.SetEndTurnSubmitting(false);
            }
        }
    }

    private async Task HandleEndTurnFailureAsync(ApiResult<GameActionResponseDto> result)
    {
        var reasonText = _latestAvailableActions
            .FirstOrDefault(action => string.Equals(action.ActionType, "END_TURN", StringComparison.OrdinalIgnoreCase))
            ?.ReasonText;
        var errorCode = result.ErrorCode ?? "";
        switch (errorCode)
        {
            case "GAME_STATE_VERSION_EXPIRED":
                InvalidateAvailableActions();
                await RefreshPlayerViewAsync();
                await RefreshAvailableActionsAsync();
                ShowGameStatusError("状态已更新，请重试。");
                break;
            case "GAME_NOT_CURRENT_PLAYER":
                InvalidateAvailableActions();
                await RefreshPlayerViewAsync();
                ShowGameStatusError("当前不是你的回合");
                break;
            case "ACTION_NOT_AVAILABLE":
                InvalidateAvailableActions();
                await RefreshPlayerViewAsync();
                await RefreshAvailableActionsAsync(true);
                ShowGameStatusError(FirstNonEmpty(result.Message, reasonText, "当前无法结束回合。"));
                break;
            default:
                ShowGameStatusError(FirstNonEmpty(result.Message, reasonText, "结束回合失败，请重试。"));
                break;
        }
    }

    private async Task RefreshPlayerViewToVersionFloorAsync()
    {
        await RefreshPlayerViewAsync();
        if (_active && (_latestPlayerView == null || _latestPlayerView.StateVersion < _stateVersionFloor))
        {
            await RefreshPlayerViewAsync();
        }
    }

    private void ApplyPlayerView(PlayerViewResponseDto playerView, bool refreshAvailableActions = true)
    {
        var previousMoveCellId = _moveCurrentCellId;
        _latestPlayerView = playerView;
        _stateVersionFloor = Math.Max(_stateVersionFloor, playerView.StateVersion);
        _hud.RefreshPlayerView(playerView, _currentUserId);
        _mapScene.SyncPlayerTokens(playerView, _currentUserId);
        _hasPlayerView = true;
        LastPlayerViewError = "";
        _hudErrorState.Visible = false;

        if (_moveState != PlayerMoveState.Idle)
        {
            var movingPlayer = playerView.Players?.FirstOrDefault(player => player.PlayerId == _movePlayerId);
            if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
                || playerView.CurrentTurnUserId != _currentUserId
                || movingPlayer == null
                || !movingPlayer.CurrentCellId.HasValue
                || movingPlayer.CurrentSpeedPoints <= 0)
            {
                ExitMoveMode();
            }
            else if (_moveState != PlayerMoveState.SubmittingMove
                     && previousMoveCellId.HasValue
                     && previousMoveCellId != movingPlayer.CurrentCellId)
            {
                ExitMoveMode();
            }
            else
            {
                _moveCurrentCellId = movingPlayer.CurrentCellId;
                _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, true);
                _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
            }
        }

        if (!TryGetCurrentTurnPlayer(out _))
        {
            InvalidateAvailableActions();
            return;
        }

        if (_availableActionsStateVersion != playerView.StateVersion)
        {
            InvalidateAvailableActions();
            if (refreshAvailableActions)
            {
                _ = RefreshAvailableActionsAsync();
            }
        }
    }

    private void ApplyAvailableActions(IEnumerable<AvailableActionDto> actions, long stateVersion)
    {
        if (_latestPlayerView == null || stateVersion != _latestPlayerView.StateVersion)
        {
            return;
        }

        _latestAvailableActions = actions.ToList();
        _availableActionsStateVersion = stateVersion;
        _hud.SetAvailableActions(_latestAvailableActions, stateVersion);
        LastPlayerViewError = "";
        _hudErrorState.Visible = false;
        if (_moveState != PlayerMoveState.Idle && !IsActionAvailable("MOVE_ONE_STEP"))
        {
            ExitMoveMode();
        }
    }

    private void InvalidateAvailableActions()
    {
        _availableActionsSequence++;
        _latestAvailableActions = [];
        _availableActionsStateVersion = -1;
        _hud.ClearAvailableActions();
    }

    private bool TryGetCurrentTurnPlayer(out PlayerViewPlayerDto player)
    {
        player = null!;
        if (_latestPlayerView == null
            || !string.Equals(_latestPlayerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
            || _latestPlayerView.CurrentTurnUserId != _currentUserId)
        {
            return false;
        }

        var match = _latestPlayerView.Players?.FirstOrDefault(candidate => candidate.UserId == _currentUserId);
        if (match == null || match.PlayerId <= 0)
        {
            return false;
        }

        player = match;
        return true;
    }

    private bool IsActionAvailable(string actionType)
    {
        return _latestPlayerView != null
            && _availableActionsStateVersion == _latestPlayerView.StateVersion
            && _latestAvailableActions.Any(action =>
                string.Equals(action.ActionType, actionType, StringComparison.OrdinalIgnoreCase)
                && action.Available);
    }

    private bool IsOlderPlayerView(PlayerViewResponseDto playerView)
    {
        return playerView.StateVersion < _stateVersionFloor
            || (_latestPlayerView != null && playerView.StateVersion < _latestPlayerView.StateVersion);
    }

    private static bool ValidatePlayerView(
        PlayerViewResponseDto playerView,
        long gameId,
        long currentUserId,
        out string error)
    {
        if (playerView.GameId > 0 && playerView.GameId != gameId)
        {
            error = "服务器返回了不匹配的对局数据。";
            return false;
        }

        if (playerView.Players == null || !playerView.Players.Exists(player => player.UserId == currentUserId))
        {
            error = "玩家视图中未找到当前玩家。";
            return false;
        }

        error = "";
        return true;
    }

    private bool IsResponseCurrent(int generation, int? sequence = null)
    {
        return IsInsideTree()
            && _active
            && generation == _generation
            && (!sequence.HasValue || sequence.Value == _requestSequence);
    }

    private void ShowPlayerViewError(string message)
    {
        ShowGameStatusError($"玩家信息同步失败：{FirstNonEmpty(message, null, "请稍后重试。")}");
    }

    private void ShowGameStatusError(string message)
    {
        LastPlayerViewError = string.IsNullOrWhiteSpace(message) ? "操作失败，请重试。" : message;
        _hudErrorLabel.Text = LastPlayerViewError;
        _hudErrorState.Visible = true;
        _hudRetryButton.Disabled = false;
    }

    private void ShowFatalError(string message)
    {
        _active = false;
        _playerViewTimer.Stop();
        _hudLoadingState.Visible = false;
        _hudErrorState.Visible = false;
        _fatalLabel.Text = message;
        _fatalState.Visible = true;
    }

    private void OnPlayerViewTimerTimeout()
    {
        _ = RefreshPlayerViewAsync();
    }

    private void OnHudRetryPressed()
    {
        _ = RefreshPlayerViewAsync(!_hasPlayerView);
    }

    private void OnEndTurnRequested()
    {
        ExitMoveMode();
        _ = SubmitEndTurnAsync();
    }

    private void OnPlayerTokenClicked(long playerId)
    {
        if (_moveState != PlayerMoveState.Idle)
        {
            if (playerId == _movePlayerId && _moveState != PlayerMoveState.SubmittingMove)
            {
                ExitMoveMode();
            }

            return;
        }

        TryEnterMoveMode(playerId);
    }

    private void OnMapCellClicked(long cellId)
    {
        if (_moveState == PlayerMoveState.SelectingTarget && _movableCellIds.Contains(cellId))
        {
            _ = SubmitMoveAsync(cellId);
            return;
        }

        if (_moveState is PlayerMoveState.LoadingTargets or PlayerMoveState.SelectingTarget)
        {
            ExitMoveMode();
        }
    }

    private void OnMapEmptyClicked()
    {
        if (_moveState is PlayerMoveState.LoadingTargets or PlayerMoveState.SelectingTarget)
        {
            ExitMoveMode();
        }
    }

    private void OnMapReloading()
    {
        ExitMoveMode(true);
    }

    private static string FirstNonEmpty(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return string.IsNullOrWhiteSpace(secondary) ? fallback : secondary.Trim();
    }
}
