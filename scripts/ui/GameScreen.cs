using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Connection;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class GameScreen : Control
{
    private GameMapScene _mapScene = null!;
    private GameHud _hud = null!;
    private CardViewerOverlay _cardViewer = null!;
    private QuestJournalOverlay _questJournal = null!;
    private QuestTargetSubmissionOverlay _questTargetSubmissionOverlay = null!;
    private GameConnectionLease _connectionLease = null!;
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
    private bool _questAcceptSubmitting;
    private bool _questSystemSubmitting;
    private bool _connectionResyncPending;
    private bool _movableCellsRequestInProgress;
    private bool _hasPlayerView;
    private int _generation;
    private int _requestSequence;
    private int _availableActionsSequence;
    private int _moveRequestGeneration;
    private int _cardRequestSequence;
    private int _cardRequestsInFlight;
    private long _gameId;
    private long _currentUserId;
    private long _stateVersionFloor;
    private long _availableActionsStateVersion = -1;
    private long _shopStateVersionFloor;
    private long _cardQualificationStateVersion = -1;
    private int? _movementRulesVersion;
    private int? _questSubmissionVersion;
    private bool? _openShopAccessAllowed;
    private PlayerViewResponseDto? _latestPlayerView;
    private List<AvailableActionDto> _latestAvailableActions = [];
    private Task _playerViewTask = Task.CompletedTask;
    private Task _availableActionsTask = Task.CompletedTask;
    private Task _endTurnTask = Task.CompletedTask;
    private Task _questAcceptTask = Task.CompletedTask;
    private Task _movableCellsTask = Task.CompletedTask;
    private Task _moveActionTask = Task.CompletedTask;
    private Task _cardRequestTask = Task.CompletedTask;
    private readonly HashSet<long> _movableCellIds = [];
    private readonly Dictionary<long, MapCellDto> _movableTargets = [];
    private PlayerMoveState _moveState = PlayerMoveState.Idle;
    private PlayerMoveMode _moveMode = PlayerMoveMode.None;
    private long _movePlayerId;
    private long? _moveCurrentCellId;
    private CardViewerKind _cardViewerKind;
    private QuestOfferListResponseDto? _latestQuestOffers;
    private PlayerQuestListResponseDto? _latestPersonalQuests;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");

    public bool IsActive => _active;
    public bool IsPlayerViewRequestInProgress => _playerViewRequestInProgress;
    public bool IsAvailableActionsRequestInProgress => _availableActionsRequestInProgress;
    public bool IsEndTurnSubmitting => _endTurnSubmitting;
    public bool IsQuestAcceptSubmitting => _questAcceptSubmitting;
    public bool IsMovableCellsRequestInProgress => _movableCellsRequestInProgress;
    public bool PlayerViewErrorVisible => GodotObject.IsInstanceValid(_hudErrorState) && _hudErrorState.Visible;
    public string LastPlayerViewError { get; private set; } = "";
    public long GameId => _gameId;
    public long LatestStateVersion => _latestPlayerView?.StateVersion ?? 0;
    public PlayerViewResponseDto? LatestPlayerView => _latestPlayerView;
    public GameMapScene MapScene => _mapScene;
    public GameHud Hud => _hud;
    public CardViewerOverlay CardViewer => _cardViewer;
    public QuestJournalOverlay QuestJournal => _questJournal;
    public QuestTargetSubmissionOverlay QuestTargetSubmissionOverlay => _questTargetSubmissionOverlay;
    public GameConnectionLease ConnectionLease => _connectionLease;
    public bool IsGameConnectionReady => GodotObject.IsInstanceValid(_connectionLease) && _connectionLease.HasActiveConnection;
    public PlayerMoveState MoveState => _moveState;
    public IReadOnlySet<long> MovableCellIds => _movableCellIds;
    public IReadOnlyDictionary<long, MapCellDto> MovableTargets => _movableTargets;
    public PlayerMoveMode MoveMode => _moveMode;
    public bool SupportsMovementV2 => _movementRulesVersion == 2;
    public bool SupportsQuestSubmission => _questSubmissionVersion == 1;
    public bool IsCardRequestInProgress => _cardRequestsInFlight > 0;
    public long ShopStateVersionFloor => _shopStateVersionFloor;

    public override void _Ready()
    {
        _mapScene = GetNode<GameMapScene>("GameMap");
        // 正式组合场景使用童话 HUD 的地图标识，独立地图场景仍保留自身诊断标签。
        if (_mapScene.GetNodeOrNull<Control>("CanvasLayer/MapInfoMargin") is { } embeddedMapInfo)
        {
            embeddedMapInfo.Visible = false;
        }
        _hud = GetNode<GameHud>("GameHud");
        _cardViewer = GetNode<CardViewerOverlay>("CardViewerOverlay");
        _questJournal = GetNode<QuestJournalOverlay>("QuestJournalOverlay");
        _questTargetSubmissionOverlay = GetNode<QuestTargetSubmissionOverlay>("QuestTargetSubmissionOverlay");
        _connectionLease = GetNode<GameConnectionLease>("GameConnectionLease");
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
        _hud.QuestBoardRequested += OnQuestBoardRequested;
        _hud.ShopShelfRequested += OnShopShelfRequested;
        _hud.PersonalQuestsRequested += OnPersonalQuestsRequested;
        _hud.InventoryRequested += OnHudInventoryRequested;
        _hud.EquipmentRequested += OnEquipmentRequested;
        _hud.RailwayRequested += OnRailwayRequested;
        _cardViewer.Closed += OnCardViewerClosed;
        _cardViewer.RetryRequested += OnCardViewerRetryRequested;
        _cardViewer.CardActionPressed += OnCardActionPressed;
        InitializeQuestSystem();
        InitializeQuestChecks();
        InitializeQuestTargetSubmissions();
        _mapScene.MapCellClicked += OnMapCellClicked;
        _mapScene.MapEmptyClicked += OnMapEmptyClicked;
        _mapScene.PlayerTokenClicked += OnPlayerTokenClicked;
        _mapScene.MapReloading += OnMapReloading;
        _connectionLease.StatusChanged += OnConnectionStatusChanged;
        _connectionLease.PlayerViewReceived += OnConnectionPlayerViewReceived;
        _connectionLease.ConnectionEstablished += OnConnectionEstablished;
        InitializeWorkFlow();
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
            _hud.QuestBoardRequested -= OnQuestBoardRequested;
            _hud.ShopShelfRequested -= OnShopShelfRequested;
            _hud.PersonalQuestsRequested -= OnPersonalQuestsRequested;
            _hud.InventoryRequested -= OnHudInventoryRequested;
            _hud.EquipmentRequested -= OnEquipmentRequested;
            _hud.RailwayRequested -= OnRailwayRequested;
        }

        if (GodotObject.IsInstanceValid(_cardViewer))
        {
            _cardViewer.Closed -= OnCardViewerClosed;
            _cardViewer.RetryRequested -= OnCardViewerRetryRequested;
            _cardViewer.CardActionPressed -= OnCardActionPressed;
        }

        DisposeQuestSystem();
        DisposeQuestChecks();
        DisposeQuestTargetSubmissions();

        if (GodotObject.IsInstanceValid(_mapScene))
        {
            _mapScene.MapCellClicked -= OnMapCellClicked;
            _mapScene.MapEmptyClicked -= OnMapEmptyClicked;
            _mapScene.PlayerTokenClicked -= OnPlayerTokenClicked;
            _mapScene.MapReloading -= OnMapReloading;
        }

        if (GodotObject.IsInstanceValid(_connectionLease))
        {
            _connectionLease.StatusChanged -= OnConnectionStatusChanged;
            _connectionLease.PlayerViewReceived -= OnConnectionPlayerViewReceived;
            _connectionLease.ConnectionEstablished -= OnConnectionEstablished;
        }
        DisposeWorkFlow();
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
        _questAcceptSubmitting = false;
        _questSystemSubmitting = false;
        _connectionResyncPending = false;
        _movableCellsRequestInProgress = false;
        _moveRequestGeneration = 0;
        _cardRequestSequence = 0;
        _cardRequestsInFlight = 0;
        _cardQualificationStateVersion = -1;
        _openShopAccessAllowed = null;
        _moveState = PlayerMoveState.Idle;
        _moveMode = PlayerMoveMode.None;
        _movePlayerId = 0;
        _moveCurrentCellId = null;
        _movableCellIds.Clear();
        _movableTargets.Clear();
        _movementRulesVersion = null;
        _questSubmissionVersion = null;
        _cardViewerKind = CardViewerKind.None;
        _latestQuestOffers = null;
        _latestPersonalQuests = null;
        _shopStateVersionFloor = 0;
        ResetItemSystemForStart();
        ResetQuestSystemForStart();
        ResetWorkFlowForStart();
        ResetQuestChecksForStart();
        ResetQuestTargetSubmissionsForStart();
        _cardViewer.CloseOverlay();
        _questJournal.ForceClose();
        _questTargetSubmissionOverlay.CloseOverlay(false);
        _hud.ClearPlayerView();
        _hud.SetConnectionReady(false);
        _hud.SetConnectionStatus("正在获取连接策略……");
        _hud.SetActionControlsLocked(false);
        _hud.SetRailwaySelecting(false);
        _hud.SetMovementRulesVersion(null);
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
        StopWorkFlow();
        StopQuestChecks();
        StopQuestTargetSubmissions();
        StopItemSystem();
        _active = false;
        if (GodotObject.IsInstanceValid(_connectionLease))
        {
            _connectionLease.Stop(true);
        }
        _generation++;
        _requestSequence++;
        _availableActionsSequence++;
        _cardRequestSequence++;
        _playerViewRequestInProgress = false;
        _availableActionsRequestInProgress = false;
        _endTurnSubmitting = false;
        _questAcceptSubmitting = false;
        _questSystemSubmitting = false;
        _connectionResyncPending = false;
        _movableCellsRequestInProgress = false;
        _cardRequestsInFlight = 0;
        _cardViewerKind = CardViewerKind.None;
        _cardQualificationStateVersion = -1;
        _openShopAccessAllowed = null;
        if (GodotObject.IsInstanceValid(_cardViewer))
        {
            _cardViewer.CloseOverlay();
        }
        if (GodotObject.IsInstanceValid(_questJournal))
        {
            _questJournal.ForceClose();
        }
        if (GodotObject.IsInstanceValid(_questTargetSubmissionOverlay))
        {
            _questTargetSubmissionOverlay.CloseOverlay(false);
        }
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.SetEndTurnSubmitting(false);
            _hud.SetActionControlsLocked(false);
            _hud.SetConnectionReady(false);
            _hud.SetConnectionStatus(null);
            _hud.SetRailwaySelecting(false);
            _hud.SetMovementRulesVersion(null);
        }
        _movementRulesVersion = null;
        _questSubmissionVersion = null;

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
        if (!_active
            || !IsGameConnectionReady
            || !TryGetCurrentTurnPlayer(out var currentPlayer)
            || _latestPlayerView == null)
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

    public Task OpenQuestBoardAsync()
    {
        return OpenCardViewerAsync(CardViewerKind.QuestBoard);
    }

    public Task OpenShopShelfAsync()
    {
        return OpenCardViewerAsync(CardViewerKind.ShopShelf);
    }

    public Task OpenPersonalQuestsAsync()
    {
        return OpenQuestJournalAsync();
    }

    public Task OpenInventoryAsync()
    {
        return OpenCardViewerAsync(CardViewerKind.Inventory);
    }

    public Task OpenEquipmentAsync()
    {
        return OpenCardViewerAsync(CardViewerKind.Equipment);
    }

    public Task RetryCardViewerAsync()
    {
        return _cardViewerKind == CardViewerKind.None
            ? Task.CompletedTask
            : OpenCardViewerAsync(_cardViewerKind);
    }

    public void CloseCardViewer()
    {
        if (GodotObject.IsInstanceValid(_cardViewer))
        {
            _cardViewer.CloseOverlay();
        }
    }

    private Task OpenCardViewerAsync(CardViewerKind kind)
    {
        if (!_active || kind == CardViewerKind.None)
        {
            return Task.CompletedTask;
        }

        _cardViewerKind = kind;
        _openShopAccessAllowed = null;
        _cardQualificationStateVersion = _latestPlayerView?.StateVersion ?? -1;
        _cardViewer.OpenLoading(kind);
        var requestSequence = ++_cardRequestSequence;
        var generation = _generation;

        if (!TryGetCurrentPlayer(out var currentPlayer))
        {
            _cardViewer.ShowError(kind, "玩家视图中缺少有效的局内 playerId，无法读取公共卡牌。");
            return Task.CompletedTask;
        }

        var needsActionQualification = kind is CardViewerKind.QuestBoard
            or CardViewerKind.ShopShelf
            or CardViewerKind.Inventory
            or CardViewerKind.Equipment;
        if (needsActionQualification)
        {
            // 每次打开都重新读取资格；同步完成前按钮保持灰色，避免沿用旧回合或旧位置权限。
            InvalidateAvailableActions();
        }

        _cardRequestsInFlight++;
        var cardLoadTask = kind switch
        {
            CardViewerKind.QuestBoard => LoadQuestBoardCoreAsync(_gameId, currentPlayer.PlayerId, generation, requestSequence),
            CardViewerKind.ShopShelf => LoadShopShelfCoreAsync(_gameId, currentPlayer.PlayerId, generation, requestSequence),
            CardViewerKind.PersonalQuests => LoadPersonalQuestsCoreAsync(_gameId, currentPlayer.PlayerId, generation, requestSequence),
            CardViewerKind.Inventory or CardViewerKind.Equipment =>
                LoadPlayerItemsForViewerCoreAsync(_gameId, currentPlayer.PlayerId, kind, generation, requestSequence),
            _ => Task.CompletedTask
        };
        var qualificationTask = needsActionQualification
            ? RefreshAvailableActionsAsync(true)
            : Task.CompletedTask;
        _cardRequestTask = Task.WhenAll(cardLoadTask, qualificationTask);
        return _cardRequestTask;
    }

    public Task SubmitEndTurnAsync()
    {
        if (!_active
            || !IsGameConnectionReady
            || _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || IsWorkActionInProgress
            || _moveState == PlayerMoveState.SubmittingMove
            || _latestPlayerView == null)
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
            RequestId = CreateGameActionRequestId(),
            PlayerId = currentPlayer.PlayerId,
            StateVersion = _latestPlayerView.StateVersion,
            ActionType = "END_TURN",
            Payload = new JsonObject()
        };
        _endTurnTask = SubmitEndTurnCoreAsync(_gameId, request, _generation);
        return _endTurnTask;
    }

    public Task SubmitAcceptQuestAsync(long questId, long offerId)
    {
        if (!_active
            || !IsGameConnectionReady
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || _endTurnSubmitting
            || IsWorkActionInProgress
            || _moveState == PlayerMoveState.SubmittingMove
            || _latestPlayerView == null
            || _cardViewerKind != CardViewerKind.QuestBoard
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content
            || !TryGetCurrentTurnPlayer(out var currentPlayer)
            || !BuildCardActionAvailability(CardViewerKind.QuestBoard).Enabled)
        {
            return _questAcceptTask;
        }

        var displayedOffer = _cardViewer.DisplayedCards.FirstOrDefault(card =>
            card.QuestId == questId && card.OfferId == offerId);
        if (displayedOffer == null || questId <= 0)
        {
            return _questAcceptTask;
        }

        _questAcceptSubmitting = true;
        ExitMoveMode();
        _hud.SetActionControlsLocked(true);
        _cardViewer.UpdateActionAvailability(new CardActionAvailability(false, "正在接取任务……"));
        _hudErrorState.Visible = false;
        var request = new GameActionRequestDto
        {
            RequestId = CreateGameActionRequestId(),
            PlayerId = currentPlayer.PlayerId,
            StateVersion = _latestPlayerView.StateVersion,
            ActionType = "ACCEPT_QUEST",
            Payload = new JsonObject { ["questId"] = questId }
        };
        _questAcceptTask = SubmitAcceptQuestCoreAsync(_gameId, currentPlayer.PlayerId, request, _generation);
        return _questAcceptTask;
    }

    public bool TryEnterMoveMode(long playerId)
    {
        if (!_active
            || !IsGameConnectionReady
            || _moveState != PlayerMoveState.Idle
            || _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || IsWorkActionInProgress
            || _latestPlayerView == null)
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
        _moveMode = PlayerMoveMode.Normal;
        _moveState = PlayerMoveState.LoadingTargets;
        _movableCellIds.Clear();
        _movableTargets.Clear();
        _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, true);
        _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
        _hudErrorState.Visible = false;
        _ = RefreshMovableCellsAsync();
        return true;
    }

    public bool TryEnterRailwayMode()
    {
        if (!_active
            || !SupportsMovementV2
            || !IsGameConnectionReady
            || _moveState != PlayerMoveState.Idle
            || _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || IsWorkActionInProgress
            || _latestPlayerView == null
            || !TryGetCurrentTurnPlayer(out var currentPlayer)
            || !currentPlayer.CurrentCellId.HasValue
            || !IsActionAvailable("USE_RAILWAY"))
        {
            return false;
        }

        _movePlayerId = currentPlayer.PlayerId;
        _moveCurrentCellId = currentPlayer.CurrentCellId;
        _moveMode = PlayerMoveMode.Railway;
        _moveState = PlayerMoveState.LoadingTargets;
        _movableCellIds.Clear();
        _movableTargets.Clear();
        _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, true);
        _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
        _hud.SetRailwaySelecting(true);
        _hudErrorState.Visible = false;
        _ = RefreshMovableCellsAsync();
        return true;
    }

    public Task RefreshMovableCellsAsync()
    {
        if (!_active
            || !IsGameConnectionReady
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
            _moveMode,
            screenGeneration,
            moveGeneration
        );
        return _movableCellsTask;
    }

    public Task SubmitMoveAsync(long targetCellId)
    {
        if (!_active
            || !IsGameConnectionReady
            || _moveState != PlayerMoveState.SelectingTarget
            || !_movableCellIds.Contains(targetCellId)
            || _latestPlayerView == null
            || _endTurnSubmitting
            || _questAcceptSubmitting
            || _questSystemSubmitting
            || _itemSystemSubmitting
            || IsWorkActionInProgress
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
            RequestId = CreateGameActionRequestId(),
            PlayerId = currentPlayer.PlayerId,
            StateVersion = _latestPlayerView.StateVersion,
            ActionType = MoveActionType(_moveMode),
            Payload = new JsonObject { ["targetCellId"] = targetCellId }
        };
        _moveActionTask = SubmitMoveCoreAsync(
            _gameId,
            request,
            _moveMode,
            _generation,
            ++_moveRequestGeneration);
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
        _movableTargets.Clear();
        _moveState = PlayerMoveState.Idle;
        _moveMode = PlayerMoveMode.None;
        _movePlayerId = 0;
        _moveCurrentCellId = null;
        if (GodotObject.IsInstanceValid(_hud))
        {
            _hud.SetActionControlsLocked(false);
            _hud.SetRailwaySelecting(false);
        }
    }

    private async Task RefreshMovableCellsCoreAsync(
        long gameId,
        long playerId,
        PlayerMoveMode moveMode,
        int screenGeneration,
        int moveGeneration)
    {
        ApiResult<List<MapCellDto>> result;
        try
        {
            result = await ApiClient.GetMovableCellsAsync(
                gameId,
                playerId,
                moveMode == PlayerMoveMode.Railway ? "USE_RAILWAY" : null);
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
        _movableTargets.Clear();
        foreach (var cell in result.Data)
        {
            if (cell.CellId > 0
                && cell.CellId != _moveCurrentCellId
                && _mapScene.CellsById.ContainsKey(cell.CellId))
            {
                _movableCellIds.Add(cell.CellId);
                _movableTargets[cell.CellId] = cell;
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
        PlayerMoveMode submittedMode,
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

            var expectedActionType = MoveActionType(submittedMode);
            if (!string.Equals(result.Data.ActionType, expectedActionType, StringComparison.OrdinalIgnoreCase))
            {
                ExitMoveMode();
                ShowGameStatusError("服务器返回了不匹配的移动行动类型。");
                return;
            }

            if (submittedMode == PlayerMoveMode.Normal
                && string.Equals(result.Data.ResultType, "CHECK_ROLL_PENDING", StringComparison.OrdinalIgnoreCase))
            {
                var forestError = "";
                if (result.Data.Movement != null
                    || !TryHandleForestMovePreparation(result.Data, gameId, request.PlayerId, out forestError))
                {
                    ExitMoveMode();
                    ShowGameStatusError(string.IsNullOrWhiteSpace(forestError)
                        ? "森林移动准备响应结构异常。"
                        : forestError);
                }
                else
                {
                    InvalidateAvailableActions();
                    await RefreshPlayerViewToVersionFloorAsync();
                    await RefreshAvailableActionsAsync(true);
                }
                return;
            }

            if (result.Data.Movement == null)
            {
                ExitMoveMode();
                ShowGameStatusError("服务端移动成功回执缺少 movement，无法确认实际位置。");
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
                || _moveMode != submittedMode
                || !IsActionAvailable(expectedActionType)
                || !currentPlayer.CurrentCellId.HasValue)
            {
                ExitMoveMode();
                return;
            }

            _moveCurrentCellId = currentPlayer.CurrentCellId;
            _movableCellIds.Clear();
            _movableTargets.Clear();
            _moveState = PlayerMoveState.LoadingTargets;
            _mapScene.TokenManager.SetPlayerLifted(_movePlayerId, true);
            _mapScene.SetMovementHighlights(_moveCurrentCellId, _movableCellIds);
            LastPlayerViewError = "";
            _hudErrorState.Visible = false;
            await RefreshMovableCellsAsync();
        }
        finally
        {
            if (IsResponseCurrent(screenGeneration) && !IsWorkOverlayBlocking)
            {
                _hud.SetActionControlsLocked(false);
            }
        }
    }

    private async Task SubmitAcceptQuestCoreAsync(
        long gameId,
        long playerId,
        GameActionRequestDto request,
        int generation)
    {
        ApiResult<GameActionResponseDto> result;
        try
        {
            result = await ApiClient.SubmitGameActionAsync(gameId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<GameActionResponseDto>.Failure(
                $"接取任务请求异常：{exception.Message}",
                "ACCEPT_QUEST_REQUEST_EXCEPTION"
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
                await HandleAcceptQuestFailureAsync(result, gameId, playerId, generation);
                return;
            }

            if ((result.Data.GameId > 0 && result.Data.GameId != gameId)
                || (!string.IsNullOrWhiteSpace(result.Data.ActionType)
                    && !string.Equals(result.Data.ActionType, "ACCEPT_QUEST", StringComparison.OrdinalIgnoreCase)))
            {
                ShowGameStatusError("服务器返回了不匹配的接取任务结果。");
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

            // 即使行动响应带视图，仍按契约重新同步权威视图与可用行动。
            await RefreshPlayerViewToVersionFloorAsync();
            await RefreshAvailableActionsAsync();
            var refreshErrors = await RefreshQuestDataAfterActionAsync(gameId, playerId, generation, true);
            if (refreshErrors.Count > 0)
            {
                ShowGameStatusError($"任务已接取，但{string.Join("；", refreshErrors)}");
            }
            else
            {
                LastPlayerViewError = "";
                _hudErrorState.Visible = false;
            }
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _questAcceptSubmitting = false;
                _hud.SetActionControlsLocked(false);
                RefreshOpenCardActionAvailability();
            }
        }
    }

    private async Task HandleAcceptQuestFailureAsync(
        ApiResult<GameActionResponseDto> result,
        long gameId,
        long playerId,
        int generation)
    {
        InvalidateAvailableActions();
        await RefreshPlayerViewAsync();
        await RefreshAvailableActionsAsync();
        var refreshErrors = await RefreshQuestDataAfterActionAsync(gameId, playerId, generation, false);
        var fallback = result.ErrorCode switch
        {
            "GAME_STATE_VERSION_EXPIRED" => "任务状态已更新，请重新选择候选。",
            "GAME_NOT_CURRENT_PLAYER" => "当前不是你的回合。",
            "ACTION_NOT_AVAILABLE" => "当前不能接取该任务。",
            "REQUEST_IN_PROGRESS" => "接取请求仍在处理中，已刷新任务状态，请勿重复提交。",
            "REQUEST_ID_CONFLICT" => "接取请求身份冲突，已刷新任务状态，请勿重复提交。",
            _ => "接取结果无法确认，已刷新任务状态，请勿直接重复提交。"
        };
        var message = FirstNonEmpty(result.Message, null, fallback);
        if (refreshErrors.Count > 0)
        {
            message = $"{message}；{string.Join("；", refreshErrors)}";
        }

        ShowGameStatusError(message);
    }

    private async Task<List<string>> RefreshQuestDataAfterActionAsync(
        long gameId,
        long playerId,
        int generation,
        bool actionConfirmed)
    {
        var errors = new List<string>();
        var displayKind = GodotObject.IsInstanceValid(_cardViewer) && _cardViewer.IsOpen
            ? _cardViewerKind
            : CardViewerKind.None;
        var displaySequence = -1;
        if (displayKind is CardViewerKind.QuestBoard or CardViewerKind.PersonalQuests)
        {
            displaySequence = ++_cardRequestSequence;
            _cardViewer.OpenLoading(displayKind);
        }

        _cardRequestsInFlight += 2;
        var offerTask = RefreshQuestOffersSnapshotAsync(
            gameId,
            playerId,
            generation,
            displayKind == CardViewerKind.QuestBoard ? displaySequence : -1,
            actionConfirmed
        );
        var personalTask = RefreshPersonalQuestsSnapshotAsync(
            gameId,
            playerId,
            generation,
            displayKind == CardViewerKind.PersonalQuests ? displaySequence : -1,
            actionConfirmed
        );
        var results = await Task.WhenAll(offerTask, personalTask);
        errors.AddRange(results.Where(error => !string.IsNullOrWhiteSpace(error)).Select(error => error!));
        return errors;
    }

    private async Task<string?> RefreshQuestOffersSnapshotAsync(
        long gameId,
        long playerId,
        int generation,
        int displaySequence,
        bool actionConfirmed)
    {
        try
        {
            var result = await ApiClient.GetQuestOffersAsync(gameId, playerId);
            if (!IsResponseCurrent(generation))
            {
                return null;
            }

            string? error = null;
            List<CardViewerCard>? cards = null;
            if (!result.Ok || result.Data == null)
            {
                error = FirstNonEmpty(result.Message, null, "公共任务板刷新失败。");
            }
            else if (!ValidateQuestOffers(result.Data, gameId, playerId, out var validationError))
            {
                error = validationError;
            }
            else if (!TryBuildQuestOfferCards(result.Data, out cards, out var cardError))
            {
                error = cardError;
            }
            else
            {
                _latestQuestOffers = result.Data;
            }

            if (displaySequence >= 0
                && IsCardResponseCurrent(generation, displaySequence, CardViewerKind.QuestBoard))
            {
                if (error != null)
                {
                    _cardViewer.ShowError(
                        CardViewerKind.QuestBoard,
                        actionConfirmed ? $"任务已接取，但{error}" : error
                    );
                }
                else
                {
                    _cardQualificationStateVersion = _latestPlayerView?.StateVersion ?? -1;
                    _cardViewer.ShowCards(
                        CardViewerKind.QuestBoard,
                        cards!,
                        BuildCardActionAvailability(CardViewerKind.QuestBoard)
                    );
                }
            }

            return error == null ? null : $"公共任务板刷新失败：{error}";
        }
        catch (Exception exception)
        {
            var error = $"公共任务板刷新异常：{exception.Message}";
            if (displaySequence >= 0
                && IsCardResponseCurrent(generation, displaySequence, CardViewerKind.QuestBoard))
            {
                _cardViewer.ShowError(
                    CardViewerKind.QuestBoard,
                    actionConfirmed ? $"任务已接取，但{error}" : error
                );
            }

            return error;
        }
        finally
        {
            _cardRequestsInFlight = Math.Max(0, _cardRequestsInFlight - 1);
        }
    }

    private async Task<string?> RefreshPersonalQuestsSnapshotAsync(
        long gameId,
        long playerId,
        int generation,
        int displaySequence,
        bool actionConfirmed)
    {
        try
        {
            var result = await ApiClient.GetPlayerQuestsAsync(gameId, playerId);
            if (!IsResponseCurrent(generation))
            {
                return null;
            }

            string? error = null;
            List<CardViewerCard>? cards = null;
            if (!result.Ok || result.Data == null)
            {
                error = FirstNonEmpty(result.Message, null, "个人任务刷新失败。");
            }
            else if (!ValidatePlayerQuests(result.Data, gameId, playerId, out var validationError))
            {
                error = validationError;
            }
            else if (!TryBuildPersonalQuestCards(result.Data, out cards, out var cardError))
            {
                error = cardError;
            }
            else
            {
                _latestPersonalQuests = result.Data;
            }

            if (displaySequence >= 0
                && IsCardResponseCurrent(generation, displaySequence, CardViewerKind.PersonalQuests))
            {
                if (error != null)
                {
                    _cardViewer.ShowError(
                        CardViewerKind.PersonalQuests,
                        actionConfirmed ? $"任务已接取，但{error}" : error
                    );
                }
                else
                {
                    _cardViewer.ShowCards(CardViewerKind.PersonalQuests, cards!);
                }
            }

            return error == null ? null : $"个人任务同步失败：{error}";
        }
        catch (Exception exception)
        {
            var error = $"个人任务同步异常：{exception.Message}";
            if (displaySequence >= 0
                && IsCardResponseCurrent(generation, displaySequence, CardViewerKind.PersonalQuests))
            {
                _cardViewer.ShowError(
                    CardViewerKind.PersonalQuests,
                    actionConfirmed ? $"任务已接取，但{error}" : error
                );
            }

            return error;
        }
        finally
        {
            _cardRequestsInFlight = Math.Max(0, _cardRequestsInFlight - 1);
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
            "RESOURCE_NOT_ENOUGH" => "资源不足，无法完成本次移动。",
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

    private async Task LoadQuestBoardCoreAsync(long gameId, long playerId, int generation, int requestSequence)
    {
        try
        {
            ApiResult<QuestOfferListResponseDto> result;
            try
            {
                result = await ApiClient.GetQuestOffersAsync(gameId, playerId);
            }
            catch (Exception exception)
            {
                result = ApiResult<QuestOfferListResponseDto>.Failure(
                    $"公共任务板请求异常：{exception.Message}",
                    "QUEST_OFFERS_REQUEST_EXCEPTION"
                );
            }

            if (!IsCardResponseCurrent(generation, requestSequence, CardViewerKind.QuestBoard))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                _cardViewer.ShowError(CardViewerKind.QuestBoard, FirstNonEmpty(result.Message, null, "公共任务板加载失败，请重试。"));
                return;
            }

            if (!ValidateQuestOffers(result.Data, gameId, playerId, out var validationError))
            {
                _cardViewer.ShowError(CardViewerKind.QuestBoard, validationError);
                return;
            }

            if (!TryBuildQuestOfferCards(result.Data, out var cards, out var cardError))
            {
                _cardViewer.ShowError(CardViewerKind.QuestBoard, cardError);
                return;
            }

            _latestQuestOffers = result.Data;
            _cardQualificationStateVersion = _latestPlayerView?.StateVersion ?? -1;
            _cardViewer.ShowCards(
                CardViewerKind.QuestBoard,
                cards,
                BuildCardActionAvailability(CardViewerKind.QuestBoard)
            );
        }
        finally
        {
            _cardRequestsInFlight = Math.Max(0, _cardRequestsInFlight - 1);
        }
    }

    private async Task LoadShopShelfCoreAsync(long gameId, long playerId, int generation, int requestSequence)
    {
        try
        {
            ApiResult<CurrentShopResponseDto> result;
            try
            {
                result = await ApiClient.GetCurrentShopAsync(gameId, playerId);
            }
            catch (Exception exception)
            {
                result = ApiResult<CurrentShopResponseDto>.Failure(
                    $"公共货架请求异常：{exception.Message}",
                    "SHOP_CURRENT_REQUEST_EXCEPTION"
                );
            }

            if (!IsCardResponseCurrent(generation, requestSequence, CardViewerKind.ShopShelf))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                _cardViewer.ShowError(CardViewerKind.ShopShelf, FirstNonEmpty(result.Message, null, "公共货架加载失败，请重试。"));
                return;
            }

            if (!ValidateCurrentShop(result.Data, gameId, out var validationError))
            {
                _cardViewer.ShowError(CardViewerKind.ShopShelf, validationError);
                return;
            }

            var responseVersion = result.Data.StateVersion!.Value;
            var currentVersionFloor = Math.Max(_shopStateVersionFloor, _latestPlayerView?.StateVersion ?? 0);
            if (responseVersion < currentVersionFloor)
            {
                _cardViewer.ShowError(CardViewerKind.ShopShelf, "公共货架状态已更新，请重试读取最新库存。");
                return;
            }

            var cards = new List<CardViewerCard>();
            foreach (var item in result.Data.Items!)
            {
                var itemId = item.ItemId!.Value;
                if (!CardArtCatalog.TryGetItemTexture(itemId, out var texture, out var artError) || texture == null)
                {
                    _cardViewer.ShowError(CardViewerKind.ShopShelf, artError);
                    return;
                }

                // 保留服务端顺序和重复 itemId，不做客户端去重。
                cards.Add(new CardViewerCard(
                    itemId.ToString(),
                    texture,
                    ShelfSlotId: item.ShelfSlotId,
                    ItemId: item.ItemId
                ));
            }

            _shopStateVersionFloor = Math.Max(_shopStateVersionFloor, responseVersion);
            _openShopAccessAllowed = result.Data.AccessAllowed;
            _cardQualificationStateVersion = _latestPlayerView?.StateVersion ?? responseVersion;
            _cardViewer.ShowCards(
                CardViewerKind.ShopShelf,
                cards,
                BuildCardActionAvailability(CardViewerKind.ShopShelf)
            );
        }
        finally
        {
            _cardRequestsInFlight = Math.Max(0, _cardRequestsInFlight - 1);
        }
    }

    private async Task LoadPersonalQuestsCoreAsync(long gameId, long playerId, int generation, int requestSequence)
    {
        try
        {
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

            if (!IsCardResponseCurrent(generation, requestSequence, CardViewerKind.PersonalQuests))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                _cardViewer.ShowError(
                    CardViewerKind.PersonalQuests,
                    FirstNonEmpty(result.Message, null, "个人任务加载失败，请重试。")
                );
                return;
            }

            if (!ValidatePlayerQuests(result.Data, gameId, playerId, out var validationError))
            {
                _cardViewer.ShowError(CardViewerKind.PersonalQuests, validationError);
                return;
            }

            if (!TryBuildPersonalQuestCards(result.Data, out var cards, out var cardError))
            {
                _cardViewer.ShowError(CardViewerKind.PersonalQuests, cardError);
                return;
            }

            _latestPersonalQuests = result.Data;
            _cardViewer.ShowCards(CardViewerKind.PersonalQuests, cards);
        }
        finally
        {
            _cardRequestsInFlight = Math.Max(0, _cardRequestsInFlight - 1);
        }
    }

    private bool IsCardResponseCurrent(int generation, int requestSequence, CardViewerKind kind)
    {
        return IsResponseCurrent(generation)
            && requestSequence == _cardRequestSequence
            && _cardViewerKind == kind
            && GodotObject.IsInstanceValid(_cardViewer)
            && _cardViewer.IsOpen;
    }

    private static bool ValidateQuestOffers(
        QuestOfferListResponseDto response,
        long gameId,
        long playerId,
        out string error)
    {
        if (!response.GameId.HasValue || response.GameId.Value != gameId)
        {
            error = "公共任务板响应缺少匹配的 gameId。";
            return false;
        }

        if (!response.PlayerId.HasValue || response.PlayerId.Value != playerId)
        {
            error = "公共任务板响应缺少匹配的 playerId。";
            return false;
        }

        if (response.Offers == null)
        {
            error = "公共任务板响应缺少 offers。";
            return false;
        }

        foreach (var offer in response.Offers)
        {
            if (!offer.OfferId.HasValue || offer.OfferId.Value <= 0
                || !offer.QuestId.HasValue || offer.QuestId.Value <= 0
                || !offer.SlotNo.HasValue || offer.SlotNo.Value <= 0
                || string.IsNullOrWhiteSpace(offer.QuestCode))
            {
                error = "公共任务板卡牌缺少必要身份字段。";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool ValidatePlayerQuests(
        PlayerQuestListResponseDto response,
        long gameId,
        long playerId,
        out string error)
    {
        if (!response.GameId.HasValue || response.GameId.Value != gameId)
        {
            error = "个人任务响应缺少匹配的 gameId。";
            return false;
        }

        if (!response.PlayerId.HasValue || response.PlayerId.Value != playerId)
        {
            error = "个人任务响应缺少匹配的 playerId。";
            return false;
        }

        if (response.Quests == null)
        {
            error = "个人任务响应缺少 quests。";
            return false;
        }

        foreach (var quest in response.Quests)
        {
            if (!quest.PlayerQuestId.HasValue || quest.PlayerQuestId.Value <= 0
                || !quest.QuestId.HasValue || quest.QuestId.Value <= 0
                || string.IsNullOrWhiteSpace(quest.QuestCode))
            {
                error = "个人任务缺少 playerQuestId、questId 或 questCode。";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool TryBuildQuestOfferCards(
        QuestOfferListResponseDto response,
        out List<CardViewerCard> cards,
        out string error)
    {
        cards = [];
        foreach (var offer in response.Offers!
                     .OrderBy(offer => offer.SlotNo)
                     .ThenBy(offer => offer.OfferId))
        {
            if (!CardArtCatalog.TryGetQuestTexture(offer.QuestCode, out var texture, out var artError) || texture == null)
            {
                error = artError;
                return false;
            }

            cards.Add(new CardViewerCard(
                offer.QuestCode!.Trim(),
                texture,
                QuestId: offer.QuestId,
                OfferId: offer.OfferId
            ));
        }

        error = "";
        return true;
    }

    private static bool TryBuildPersonalQuestCards(
        PlayerQuestListResponseDto response,
        out List<CardViewerCard> cards,
        out string error)
    {
        cards = [];
        foreach (var quest in response.Quests!)
        {
            if (!CardArtCatalog.TryGetQuestTexture(quest.QuestCode, out var texture, out var artError) || texture == null)
            {
                error = artError;
                return false;
            }

            // 保留服务端顺序和重复配置；playerQuestId 才是玩家实际任务实例身份。
            cards.Add(new CardViewerCard(
                quest.PlayerQuestId!.Value.ToString(),
                texture,
                QuestId: quest.QuestId,
                PlayerQuestId: quest.PlayerQuestId
            ));
        }

        error = "";
        return true;
    }

    private static bool ValidateCurrentShop(CurrentShopResponseDto response, long gameId, out string error)
    {
        if (!string.Equals(response.InventoryScope, "GAME", StringComparison.OrdinalIgnoreCase)
            || !response.InventoryGameId.HasValue
            || response.InventoryGameId.Value != gameId)
        {
            error = "公共货架响应缺少匹配的本局库存身份。";
            return false;
        }

        if (!response.StateVersion.HasValue || response.StateVersion.Value < 0)
        {
            error = "公共货架响应缺少 stateVersion。";
            return false;
        }

        if (!response.AccessAllowed.HasValue)
        {
            error = "公共货架响应缺少 accessAllowed。";
            return false;
        }

        if (response.Items == null)
        {
            error = "公共货架响应缺少 items。";
            return false;
        }

        foreach (var item in response.Items)
        {
            if (!item.ShelfSlotId.HasValue || item.ShelfSlotId.Value <= 0
                || !item.ItemId.HasValue || item.ItemId.Value <= 0)
            {
                error = "公共货架卡牌缺少 shelfSlotId 或 itemId。";
                return false;
            }
        }

        error = "";
        return true;
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

        // 能力只从本次直接 GET player-view 读取，行动回执和历史重放不用于探测。
        _movementRulesVersion = result.Data.EngineState?.MovementRulesVersion;
        _questSubmissionVersion = result.Data.EngineState?.QuestSubmissionVersion;
        _hud.SetMovementRulesVersion(_movementRulesVersion);
        if (!SupportsMovementV2 && _moveMode == PlayerMoveMode.Railway)
        {
            ExitMoveMode();
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
            RefreshOpenCardActionAvailability();
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
            case "REQUEST_TIMEOUT":
            case "NETWORK_ERROR":
            case "END_TURN_REQUEST_EXCEPTION":
                // 响应丢失不代表行动未执行；只刷新权威状态，不自动重发原行动。
                InvalidateAvailableActions();
                await RefreshPlayerViewAsync();
                await RefreshAvailableActionsAsync(true);
                ShowGameStatusError(FirstNonEmpty(result.Message, null, "结束回合结果无法确认，已刷新对局状态。"));
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
        EnsureGameConnection(playerView);
        if (playerView.EngineState?.PublicShop != null)
        {
            // player-view 中的 publicShop 与 shop/current 是同一库存，只提升同一个版本门槛。
            _shopStateVersionFloor = Math.Max(_shopStateVersionFloor, playerView.StateVersion);
        }
        _hud.RefreshPlayerView(playerView, _currentUserId);
        _mapScene.SyncPlayerTokens(playerView, _currentUserId);
        HandlePlayerViewForWork(playerView);
        HandlePlayerViewForQuestChecks(playerView);
        HandlePlayerViewForQuestSystem(playerView);
        HandlePlayerViewForItemSystem(playerView);
        HandlePlayerViewForQuestTargetSubmissions(playerView);
        _hasPlayerView = true;
        LastPlayerViewError = "";
        _hudErrorState.Visible = false;

        var allOfflinePaused = string.Equals(
            playerView.EngineState?.PausedReason,
            "ALL_OFFLINE",
            StringComparison.Ordinal
        );
        if (allOfflinePaused)
        {
            ExitMoveMode();
            InvalidateAvailableActions();
            _hud.SetConnectionStatus(
                IsGameConnectionReady
                    ? "已重新连接，正在等待服务端恢复对局……"
                    : "全员离线暂停，正在重新连接……"
            );
            return;
        }

        if (!IsGameConnectionReady)
        {
            ExitMoveMode();
            InvalidateAvailableActions();
            return;
        }

        _hud.SetConnectionStatus(null);
        RefreshOpenShopQualificationIfNeeded(playerView.StateVersion);

        if (_moveState != PlayerMoveState.Idle)
        {
            var movingPlayer = playerView.Players?.FirstOrDefault(player => player.PlayerId == _movePlayerId);
            if ((_moveMode == PlayerMoveMode.Railway && !SupportsMovementV2)
                || !string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
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
        if (!IsGameConnectionReady
            || _latestPlayerView == null
            || stateVersion != _latestPlayerView.StateVersion)
        {
            return;
        }

        _latestAvailableActions = actions.ToList();
        _availableActionsStateVersion = stateVersion;
        _hud.SetAvailableActions(_latestAvailableActions, stateVersion);
        RefreshOpenCardActionAvailability();
        LastPlayerViewError = "";
        _hudErrorState.Visible = false;
        if (_moveState != PlayerMoveState.Idle
            && !IsActionAvailable(MoveActionType(_moveMode)))
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
        RefreshOpenCardActionAvailability();
    }

    private CardActionAvailability BuildCardActionAvailability(CardViewerKind kind)
    {
        if (!_active || !IsGameConnectionReady)
        {
            return new CardActionAvailability(false, "对局连接尚未就绪。");
        }

        if (_latestPlayerView == null)
        {
            return new CardActionAvailability(false, "正在同步玩家状态……");
        }

        if (!string.Equals(_latestPlayerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return new CardActionAvailability(false, "当前对局阶段不允许操作公共卡牌。");
        }

        if (string.Equals(_latestPlayerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            return new CardActionAvailability(false, "对局处于全员离线暂停状态。");
        }

        if (_latestPlayerView.CurrentTurnUserId != _currentUserId)
        {
            return new CardActionAvailability(false, "当前不是你的回合。");
        }

        if (_questAcceptSubmitting)
        {
            return new CardActionAvailability(false, "正在接取任务……");
        }

        if (_itemSystemSubmitting)
        {
            return new CardActionAvailability(false, "正在处理道具操作……");
        }

        if (_questSystemSubmitting
            || _endTurnSubmitting
            || _moveState == PlayerMoveState.SubmittingMove
            || IsWorkActionInProgress)
        {
            return new CardActionAvailability(false, "正在处理其他业务行动……");
        }

        if (_availableActionsStateVersion != _latestPlayerView.StateVersion)
        {
            return new CardActionAvailability(
                false,
                _availableActionsRequestInProgress ? "正在同步操作资格……" : "操作资格同步失败，请稍后重试。"
            );
        }

        var actionType = kind == CardViewerKind.QuestBoard ? "ACCEPT_QUEST" : "SHOP_BUY_ITEM";
        var action = _latestAvailableActions.FirstOrDefault(candidate =>
            string.Equals(candidate.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
        if (action == null)
        {
            return new CardActionAvailability(false, "服务端未返回对应操作资格。");
        }

        if (!action.Available)
        {
            return new CardActionAvailability(
                false,
                FirstNonEmpty(action.ReasonText, action.Reason, "当前不满足操作条件。")
            );
        }

        if (kind == CardViewerKind.ShopShelf)
        {
            if (!_openShopAccessAllowed.HasValue)
            {
                return new CardActionAvailability(false, "正在同步货架地点资格……");
            }

            if (!_openShopAccessAllowed.Value)
            {
                return new CardActionAvailability(false, "当前位置不能购买公共货架商品。");
            }

            return new CardActionAvailability(
                true,
                "当前满足通用购买资格；具体商品价格、资源与容量仍以服务端提交校验为准。"
            );
        }

        return new CardActionAvailability(
            true,
            "当前满足通用接取资格；具体任务仍以服务端提交校验为准。"
        );
    }

    private void RefreshOpenCardActionAvailability()
    {
        if (_cardViewerKind is CardViewerKind.Inventory or CardViewerKind.Equipment)
        {
            RefreshOpenItemActionAvailability();
            return;
        }

        if (_cardViewerKind is CardViewerKind.None or CardViewerKind.PersonalQuests
            || !GodotObject.IsInstanceValid(_cardViewer)
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content)
        {
            return;
        }

        _cardViewer.UpdateActionAvailability(BuildCardActionAvailability(_cardViewerKind));
    }

    private void RefreshOpenShopQualificationIfNeeded(long stateVersion)
    {
        if (_cardViewerKind != CardViewerKind.ShopShelf
            || !GodotObject.IsInstanceValid(_cardViewer)
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content
            || _itemSystemSubmitting
            || _cardQualificationStateVersion == stateVersion
            || _cardRequestsInFlight > 0
            || !TryGetCurrentPlayer(out var currentPlayer))
        {
            return;
        }

        // 玩家位置或对局版本变化后重新读取同一公共货架，只更新只读地点资格和展示数据。
        // 这里不会调用 SHOP_REFRESH，也不会改变库存或抽牌结果。
        _cardQualificationStateVersion = stateVersion;
        _openShopAccessAllowed = null;
        RefreshOpenCardActionAvailability();
        var requestSequence = ++_cardRequestSequence;
        var generation = _generation;
        _cardRequestsInFlight++;
        _cardRequestTask = LoadShopShelfCoreAsync(_gameId, currentPlayer.PlayerId, generation, requestSequence);
    }

    private bool TryGetCurrentTurnPlayer(out PlayerViewPlayerDto player)
    {
        player = null!;
        if (_latestPlayerView == null
            || !IsGameConnectionReady
            || !string.Equals(_latestPlayerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_latestPlayerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal)
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

    private bool TryGetCurrentPlayer(out PlayerViewPlayerDto player)
    {
        player = null!;
        var match = _latestPlayerView?.Players?
            .FirstOrDefault(candidate => candidate.UserId == _currentUserId && candidate.PlayerId > 0);
        if (match == null)
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

    private void OnQuestBoardRequested()
    {
        _ = OpenQuestBoardAsync();
    }

    private void OnShopShelfRequested()
    {
        _ = OpenShopShelfAsync();
    }

    private void OnPersonalQuestsRequested()
    {
        _ = OpenPersonalQuestsAsync();
    }

    private void OnHudInventoryRequested()
    {
        _ = OpenInventoryAsync();
    }

    private void OnEquipmentRequested()
    {
        _ = OpenEquipmentAsync();
    }

    private void OnCardActionPressed(int kind, long primaryId, long secondaryId)
    {
        switch ((CardViewerKind)kind)
        {
            case CardViewerKind.QuestBoard:
                _ = SubmitAcceptQuestAsync(primaryId, secondaryId);
                break;
            case CardViewerKind.ShopShelf:
                _ = SubmitShopPurchaseAsync(primaryId, secondaryId);
                break;
            case CardViewerKind.Inventory:
                _ = SubmitInventoryItemActionAsync(primaryId, secondaryId);
                break;
            case CardViewerKind.Equipment:
                _ = SubmitUnequipItemAsync(primaryId);
                break;
        }
    }

    private void OnCardViewerClosed()
    {
        _cardRequestSequence++;
        _cardViewerKind = CardViewerKind.None;
        _cardQualificationStateVersion = -1;
        _openShopAccessAllowed = null;
    }

    private void OnCardViewerRetryRequested()
    {
        _ = RetryCardViewerAsync();
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

    private void OnRailwayRequested()
    {
        TryEnterRailwayMode();
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

    private static string MoveActionType(PlayerMoveMode mode) =>
        mode == PlayerMoveMode.Railway ? "USE_RAILWAY" : "MOVE_ONE_STEP";

    private void EnsureGameConnection(PlayerViewResponseDto playerView)
    {
        var currentPlayer = playerView.Players?
            .FirstOrDefault(player => player.UserId == _currentUserId && player.PlayerId > 0);
        if (currentPlayer == null || !GodotObject.IsInstanceValid(_connectionLease))
        {
            return;
        }

        _connectionLease.UpdatePolicy(playerView.EngineState?.ConnectionPolicy);
        if (_connectionLease.GameId == _gameId && _connectionLease.PlayerId == currentPlayer.PlayerId)
        {
            return;
        }

        _hud.SetConnectionReady(false);
        _hud.SetConnectionStatus("正在建立对局连接……");
        _ = _connectionLease.StartAsync(_gameId, currentPlayer.PlayerId, playerView.EngineState?.ConnectionPolicy);
    }

    private void OnConnectionStatusChanged(GameConnectionLeaseState state, string message)
    {
        if (!_active || !GodotObject.IsInstanceValid(_hud))
        {
            return;
        }

        var connected = state == GameConnectionLeaseState.Connected;
        _hud.SetConnectionReady(connected);
        if (connected)
        {
            if (!string.Equals(_latestPlayerView?.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
            {
                _hud.SetConnectionStatus(null);
            }
            return;
        }

        ExitMoveMode();
        HandleWorkConnectionInterrupted();
        InvalidateAvailableActions();
        _hud.SetConnectionStatus(
            string.IsNullOrWhiteSpace(message) ? "对局连接已停止。" : message,
            state == GameConnectionLeaseState.Failed
        );
    }

    private void OnConnectionPlayerViewReceived(PlayerViewResponseDto playerView)
    {
        if (!_active
            || !ValidatePlayerView(playerView, _gameId, _currentUserId, out _)
            || IsOlderPlayerView(playerView))
        {
            return;
        }

        ApplyPlayerView(playerView, !_connectionResyncPending);
    }

    private void OnConnectionEstablished()
    {
        if (_active)
        {
            _connectionResyncPending = true;
            _ = ResynchronizeAfterConnectionAsync(_generation);
        }
    }

    private async Task ResynchronizeAfterConnectionAsync(int generation)
    {
        await RefreshPlayerViewAsync();
        if (!IsResponseCurrent(generation))
        {
            return;
        }

        _connectionResyncPending = false;
        if (!IsGameConnectionReady)
        {
            return;
        }

        // RefreshPlayerViewAsync 内部的 ApplyPlayerView 已按最新版本触发行动刷新。
        await RefreshPendingWorkCheckAsync(false);
    }

    private static string FirstNonEmpty(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return string.IsNullOrWhiteSpace(secondary) ? fallback : secondary.Trim();
    }

    private static string CreateGameActionRequestId()
    {
        // 服务端上限为 64 字符；标准 UUID 文本长度为 36，且每个新逻辑行动只生成一次。
        return Guid.NewGuid().ToString("D");
    }
}
