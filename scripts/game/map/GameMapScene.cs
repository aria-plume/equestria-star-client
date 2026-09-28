using System.Collections.Generic;
using System.Threading.Tasks;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

public partial class GameMapScene : Node3D
{
    [Signal]
    public delegate void MapLoadedEventHandler(int cellCount, int edgeCount);

    [Signal]
    public delegate void MapLoadFailedEventHandler(string message);

    [ExportGroup("地图来源")]
    [Export]
    public long GameId { get; set; }

    [Export]
    public bool AutoLoadOnReady { get; set; } = true;

    [Export]
    public bool RequireGameId { get; set; }

    [Export]
    public PackedScene? CellScene { get; set; }

    [ExportGroup("格子显示")]
    [Export(PropertyHint.Range, "0.2,2.0,0.01")]
    public float CellRadius { get; set; } = 0.55f;

    [Export(PropertyHint.Range, "0.02,1.0,0.01")]
    public float CellHeight { get; set; } = 0.16f;

    [Export(PropertyHint.Range, "0.0,0.2,0.005")]
    public float CellBevelWidth { get; set; } = 0.035f;

    [Export]
    public bool ShowQrCoordinates { get; set; } = true;

    private readonly GameMapBuilder _builder = new();
    private Node3D _mapRoot = null!;
    private MapCameraController _cameraController = null!;
    private MeshInstance3D _floor = null!;
    private Control _loadingState = null!;
    private Control _errorState = null!;
    private Label _loadingLabel = null!;
    private Label _errorLabel = null!;
    private Label _mapInfoLabel = null!;
    private Button _retryButton = null!;
    private bool _isLoading;
    private int _loadGeneration;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");

    public bool IsLoading => _isLoading;
    public bool ErrorVisible => GodotObject.IsInstanceValid(_errorState) && _errorState.Visible;
    public string LastError { get; private set; } = "";
    public int GeneratedCellCount => _builder.CellsById.Count;
    public IReadOnlyDictionary<long, HexCell3D> CellsById => _builder.CellsById;
    public IReadOnlyDictionary<string, HexCell3D> CellsByCode => _builder.CellsByCode;
    public IReadOnlyList<MapEdgeDto> Edges => _builder.Edges;
    public MapCameraController CameraController => _cameraController;

    public override void _Ready()
    {
        _mapRoot = GetNode<Node3D>("MapRoot");
        _cameraController = GetNode<MapCameraController>("CameraRig");
        _floor = GetNode<MeshInstance3D>("Floor");
        _loadingState = GetNode<Control>("CanvasLayer/LoadingState");
        _errorState = GetNode<Control>("CanvasLayer/ErrorState");
        _loadingLabel = GetNode<Label>("CanvasLayer/LoadingState/LoadingPanel/LoadingMargin/LoadingLabel");
        _errorLabel = GetNode<Label>("CanvasLayer/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/ErrorLabel");
        _retryButton = GetNode<Button>("CanvasLayer/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/RetryButton");
        _mapInfoLabel = GetNode<Label>("CanvasLayer/MapInfoMargin/MapInfoLabel");

        _retryButton.Pressed += OnRetryPressed;
        _cameraController.DragStarted += OnCameraDragStarted;
        _errorState.Visible = false;
        _mapInfoLabel.Visible = false;

        if (AutoLoadOnReady)
        {
            _ = LoadMapAsync();
        }
        else
        {
            _loadingState.Visible = false;
        }
    }

    public override void _ExitTree()
    {
        _loadGeneration++;
        if (GodotObject.IsInstanceValid(_retryButton))
        {
            _retryButton.Pressed -= OnRetryPressed;
        }

        if (GodotObject.IsInstanceValid(_cameraController))
        {
            _cameraController.DragStarted -= OnCameraDragStarted;
        }
    }

    public async Task LoadMapAsync()
    {
        if (_isLoading || !IsInsideTree())
        {
            return;
        }

        _isLoading = true;
        var generation = ++_loadGeneration;
        LastError = "";
        _loadingLabel.Text = GameId > 0 ? "正在加载对局地图……" : "正在加载正式地图……";
        _loadingState.Visible = true;
        _errorState.Visible = false;
        _retryButton.Disabled = true;

        if (RequireGameId && GameId <= 0)
        {
            _isLoading = false;
            _retryButton.Disabled = false;
            ShowError("缺少有效的 gameId，无法加载对局地图。");
            return;
        }

        ApiResult<GameMapDto> result;
        try
        {
            result = GameId > 0
                ? await ApiClient.GetGameMapAsync(GameId)
                : await ApiClient.GetMainMapAsync();
        }
        catch (System.Exception exception)
        {
            if (IsInsideTree() && generation == _loadGeneration)
            {
                _isLoading = false;
                _retryButton.Disabled = false;
                ShowError($"地图请求异常：{exception.Message}");
            }

            return;
        }

        if (!IsInsideTree() || generation != _loadGeneration)
        {
            return;
        }

        _isLoading = false;
        _retryButton.Disabled = false;
        if (!result.Ok)
        {
            ShowError($"地图请求失败：{result.Message}");
            return;
        }

        if (CellScene == null)
        {
            ShowError("地图格子场景未配置。");
            return;
        }

        var buildResult = _builder.Build(
            result.Data!,
            _mapRoot,
            CellScene,
            CellRadius,
            CellHeight,
            CellBevelWidth,
            ShowQrCoordinates
        );
        if (!buildResult.Ok)
        {
            ShowError($"地图数据校验失败：{buildResult.Error}");
            return;
        }

        foreach (var cell in _builder.CellsById.Values)
        {
            cell.CellClicked += OnCellClicked;
        }

        UpdateFloor(buildResult.Bounds);
        _cameraController.InitializeMap(buildResult.Bounds);
        _loadingState.Visible = false;
        _errorState.Visible = false;
        _mapInfoLabel.Text = $"{result.Data!.MapName}  ·  {buildResult.CellCount} 格  ·  {buildResult.EdgeCount} 边";
        _mapInfoLabel.Visible = true;
        EmitSignal(SignalName.MapLoaded, buildResult.CellCount, buildResult.EdgeCount);
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

    private void UpdateFloor(Aabb bounds)
    {
        _floor.Position = new Vector3(bounds.GetCenter().X, -CellHeight - 0.025f, bounds.GetCenter().Z);
        if (_floor.Mesh is PlaneMesh plane)
        {
            plane.Size = new Vector2(bounds.Size.X + CellRadius * 20.0f, bounds.Size.Z + CellRadius * 20.0f);
        }
    }

    private void OnCellClicked(long cellId)
    {
        if (_builder.CellsById.TryGetValue(cellId, out var cell) && cell.Data != null)
        {
            GD.Print($"地图格子点击：{cell.Data.CellCode} / {cell.Data.DisplayName} / ({cell.Data.Q},{cell.Data.R})");
        }
    }

    private void OnRetryPressed()
    {
        _ = LoadMapAsync();
    }

    private void OnCameraDragStarted()
    {
        foreach (var cell in _builder.CellsById.Values)
        {
            cell.CancelPendingClick();
        }
    }
}
