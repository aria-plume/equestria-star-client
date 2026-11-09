using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map.VisualRuntime;
using Godot;

namespace EquestriaStar.Game.Map;

public partial class GameMapScene : Node3D
{
    [Signal] public delegate void MapLoadedEventHandler(int cellCount, int edgeCount);
    [Signal] public delegate void MapLoadFailedEventHandler(string message);
    [Signal] public delegate void MapReloadingEventHandler();
    [Signal] public delegate void MapCellClickedEventHandler(long cellId);
    [Signal] public delegate void MapEmptyClickedEventHandler();
    [Signal] public delegate void PlayerTokenClickedEventHandler(long playerId);

    [ExportGroup("地图来源")]
    [Export] public long GameId { get; set; }
    [Export] public bool AutoLoadOnReady { get; set; } = true;
    [Export] public bool RequireGameId { get; set; }
    [Export] public PackedScene? CellScene { get; set; }

    [ExportGroup("交互格")]
    [Export(PropertyHint.Range, "0.2,2.0,0.01")] public float CellRadius { get; set; } = 0.55f;
    [Export(PropertyHint.Range, "0.02,1.0,0.01")] public float CellHeight { get; set; } = 0.16f;
    [Export(PropertyHint.Range, "0.0,0.2,0.005")] public float CellBevelWidth { get; set; } = 0.035f;
    [Export] public bool ShowCellNames { get; set; } = true;

    private readonly StaticMapInteractionBuilder _interactionBuilder = new();
    private Node3D _staticVisualRoot = null!;
    private Node3D _interactionRoot = null!;
    private Node3D? _staticMapInstance;
    private StaticMapVisualContract? _staticContract;
    private MapVisualRuntime _visualRuntime = null!;
    private MapCameraController _cameraController = null!;
    private PlayerTokenManager _tokenManager = null!;
    private Control _loadingState = null!;
    private Control _errorState = null!;
    private Label _loadingLabel = null!;
    private Label _errorLabel = null!;
    private Label _mapInfoLabel = null!;
    private Button _retryButton = null!;
    private bool _isLoading;
    private int _loadGeneration;
    private long _contentClickSerial;
    private ulong _lastContentClickFrame = ulong.MaxValue;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");

    public bool IsLoading => _isLoading;
    public bool ErrorVisible => GodotObject.IsInstanceValid(_errorState) && _errorState.Visible;
    public string LastError { get; private set; } = "";
    public int GeneratedCellCount => _interactionBuilder.CellsById.Count;
    public int StaticVisualCellCount => _staticContract?.StaticVisualCellCount ?? 0;
    public bool IsUsingStaticVisual => GodotObject.IsInstanceValid(_staticMapInstance);
    public IReadOnlyDictionary<long, HexCell3D> CellsById => _interactionBuilder.CellsById;
    public IReadOnlyDictionary<string, HexCell3D> CellsByCode => _interactionBuilder.CellsByCode;
    public IReadOnlyList<MapEdgeDto> Edges => _interactionBuilder.Edges;
    public MapCameraController CameraController => _cameraController;
    public PlayerTokenManager TokenManager => _tokenManager;
    public Node3D StaticVisualRoot => _staticVisualRoot;
    public Node3D InteractionRoot => _interactionRoot;
    public Node3D? StaticMapInstance => _staticMapInstance;
    public MapVisualRuntime VisualRuntime => _visualRuntime;
    public StaticMapVisualContract? StaticContract => _staticContract;

    public override void _Ready()
    {
        _staticVisualRoot = GetNode<Node3D>("StaticVisualRoot");
        _interactionRoot = GetNode<Node3D>("InteractionRoot");
        _visualRuntime = GetNode<MapVisualRuntime>("MapVisualRuntime");
        _cameraController = GetNode<MapCameraController>("CameraRig");
        _tokenManager = GetNode<PlayerTokenManager>("PlayerTokenManager");
        _loadingState = GetNode<Control>("CanvasLayer/LoadingState");
        _errorState = GetNode<Control>("CanvasLayer/ErrorState");
        _loadingLabel = GetNode<Label>("CanvasLayer/LoadingState/LoadingPanel/LoadingMargin/LoadingLabel");
        _errorLabel = GetNode<Label>("CanvasLayer/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/ErrorLabel");
        _retryButton = GetNode<Button>("CanvasLayer/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/RetryButton");
        _mapInfoLabel = GetNode<Label>("CanvasLayer/MapInfoMargin/MapInfoLabel");

        _retryButton.Pressed += OnRetryPressed;
        _cameraController.DragStarted += OnCameraDragStarted;
        _cameraController.MapPointerClicked += OnMapPointerClicked;
        _tokenManager.TokenClicked += OnPlayerTokenClicked;
        _tokenManager.BindCells(_interactionBuilder.CellsById);
        _errorState.Visible = false;
        _mapInfoLabel.Visible = false;

        if (AutoLoadOnReady) _ = LoadMapAsync();
        else _loadingState.Visible = false;
    }

    public override void _ExitTree()
    {
        _loadGeneration++;
        if (GodotObject.IsInstanceValid(_retryButton)) _retryButton.Pressed -= OnRetryPressed;
        if (GodotObject.IsInstanceValid(_cameraController))
        {
            _cameraController.DragStarted -= OnCameraDragStarted;
            _cameraController.MapPointerClicked -= OnMapPointerClicked;
        }
        if (GodotObject.IsInstanceValid(_tokenManager)) _tokenManager.TokenClicked -= OnPlayerTokenClicked;
        ClearLoadedMap();
    }

    public async Task LoadMapAsync()
    {
        if (_isLoading || !IsInsideTree()) return;

        _isLoading = true;
        var generation = ++_loadGeneration;
        LastError = "";
        _loadingLabel.Text = GameId > 0 ? "正在加载对局地图……" : "正在加载正式地图……";
        _loadingState.Visible = true;
        _errorState.Visible = false;
        _retryButton.Disabled = true;

        if (RequireGameId && GameId <= 0)
        {
            CompleteLoadFailure("缺少有效的 gameId，无法加载对局地图。");
            return;
        }

        ApiResult<GameMapDto> result;
        try
        {
            result = GameId > 0
                ? await ApiClient.GetGameMapAsync(GameId)
                : await ApiClient.GetMainMapAsync();
        }
        catch (Exception exception)
        {
            if (IsInsideTree() && generation == _loadGeneration)
                CompleteLoadFailure($"地图请求异常：{exception.Message}");
            return;
        }

        if (!IsInsideTree() || generation != _loadGeneration) return;
        if (!result.Ok || result.Data == null)
        {
            CompleteLoadFailure($"地图请求失败：{result.Message}");
            return;
        }
        if (CellScene == null)
        {
            CompleteLoadFailure("地图交互格场景未配置。");
            return;
        }

        var visualResult = OfficialMapStaticVisualLoader.Load(result.Data);
        if (!visualResult.Ok || visualResult.Instance == null || visualResult.Contract == null)
        {
            CompleteLoadFailure($"静态地图契约校验失败：{visualResult.Error}");
            return;
        }

        if (_interactionBuilder.CellsById.Count > 0) EmitSignal(SignalName.MapReloading);
        ClearLoadedMap();
        _staticMapInstance = visualResult.Instance;
        _staticContract = visualResult.Contract;
        _staticVisualRoot.AddChild(_staticMapInstance);

        var buildResult = _interactionBuilder.Build(
            result.Data, _staticContract, _interactionRoot, CellScene,
            CellRadius, CellHeight, CellBevelWidth, ShowCellNames
        );
        if (!buildResult.Ok)
        {
            ClearLoadedMap();
            CompleteLoadFailure(buildResult.Error);
            return;
        }

        foreach (var cell in _interactionBuilder.CellsById.Values) cell.CellClicked += OnCellClicked;
        _cameraController.InitializeMap(_staticContract.VisualBounds);
        _tokenManager.BindCells(_interactionBuilder.CellsById);
        _tokenManager.NotifyMapLoaded();
        _visualRuntime.AttachMap(_staticMapInstance);

        _isLoading = false;
        _retryButton.Disabled = false;
        _loadingState.Visible = false;
        _errorState.Visible = false;
        _mapInfoLabel.Text = $"{result.Data.MapName}  ·  {buildResult.CellCount} 格  ·  {buildResult.EdgeCount} 边";
        _mapInfoLabel.Visible = true;
        EmitSignal(SignalName.MapLoaded, buildResult.CellCount, buildResult.EdgeCount);
    }

    public void ClearLoadedMap()
    {
        if (GodotObject.IsInstanceValid(_tokenManager)) _tokenManager.NotifyMapReloading();
        if (GodotObject.IsInstanceValid(_visualRuntime)) _visualRuntime.DetachMap();
        if (GodotObject.IsInstanceValid(_cameraController)) _cameraController.ClearMap();
        foreach (var cell in _interactionBuilder.CellsById.Values)
        {
            if (GodotObject.IsInstanceValid(cell)) cell.CellClicked -= OnCellClicked;
        }
        if (GodotObject.IsInstanceValid(_interactionRoot)) _interactionBuilder.Clear(_interactionRoot);
        if (GodotObject.IsInstanceValid(_tokenManager)) _tokenManager.BindCells(_interactionBuilder.CellsById);
        if (GodotObject.IsInstanceValid(_staticMapInstance))
        {
            _staticMapInstance!.GetParent()?.RemoveChild(_staticMapInstance);
            _staticMapInstance.QueueFree();
        }
        _staticMapInstance = null;
        _staticContract = null;
    }

    private void CompleteLoadFailure(string message)
    {
        _isLoading = false;
        _retryButton.Disabled = false;
        ShowError(message);
    }

    private void ShowError(string message)
    {
        LastError = message;
        _loadingState.Visible = false;
        _errorState.Visible = true;
        _errorLabel.Text = message;
        _mapInfoLabel.Visible = false;
        GD.PushError(message);
        EmitSignal(SignalName.MapLoadFailed, message);
    }

    private void OnCellClicked(long cellId)
    {
        _contentClickSerial++;
        _lastContentClickFrame = Engine.GetProcessFrames();
        EmitSignal(SignalName.MapCellClicked, cellId);
        if (_interactionBuilder.CellsById.TryGetValue(cellId, out var cell) && cell.Data != null)
            GD.Print($"地图格子点击：{cell.Data.CellCode} / {cell.Data.DisplayName} / ({cell.Data.Q},{cell.Data.R})");
    }

    private void OnRetryPressed() => _ = LoadMapAsync();

    private void OnCameraDragStarted()
    {
        foreach (var cell in _interactionBuilder.CellsById.Values) cell.CancelPendingClick();
        _tokenManager.CancelPendingClicks();
        // 地图拖动是明确的空白操作，正式移动模式应立即退出。
        EmitSignal(SignalName.MapEmptyClicked);
    }

    public void SyncPlayerTokens(PlayerViewResponseDto playerView, long currentUserId) =>
        _tokenManager.SyncPlayerView(playerView, currentUserId);

    public void SetMovementHighlights(long? currentCellId, IReadOnlySet<long> movableCellIds)
    {
        foreach (var pair in _interactionBuilder.CellsById)
        {
            pair.Value.SetCurrent(currentCellId.HasValue && pair.Key == currentCellId.Value);
            pair.Value.SetMovable(movableCellIds.Contains(pair.Key));
        }
    }

    public void ClearMovementHighlights()
    {
        foreach (var cell in _interactionBuilder.CellsById.Values)
        {
            cell.SetCurrent(false);
            cell.SetMovable(false);
        }
    }

    private void OnPlayerTokenClicked(long playerId)
    {
        _contentClickSerial++;
        _lastContentClickFrame = Engine.GetProcessFrames();
        foreach (var cell in _interactionBuilder.CellsById.Values) cell.CancelPendingClick();
        EmitSignal(SignalName.PlayerTokenClicked, playerId);
    }

    private void OnMapPointerClicked(Vector2 screenPosition)
    {
        var serialAtRelease = _contentClickSerial;
        var releaseFrame = Engine.GetProcessFrames();
        Callable.From(() => EmitEmptyClickIfUnclaimed(serialAtRelease, releaseFrame)).CallDeferred();
    }

    private void EmitEmptyClickIfUnclaimed(long serialAtRelease, ulong releaseFrame)
    {
        if (IsInsideTree() && serialAtRelease == _contentClickSerial && _lastContentClickFrame != releaseFrame)
            EmitSignal(SignalName.MapEmptyClicked);
    }
}
