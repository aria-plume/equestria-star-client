using System;
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
    private bool _hasPlayerView;
    private int _generation;
    private int _requestSequence;
    private long _gameId;
    private long _currentUserId;
    private Task _playerViewTask = Task.CompletedTask;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");

    public bool IsActive => _active;
    public bool IsPlayerViewRequestInProgress => _playerViewRequestInProgress;
    public bool PlayerViewErrorVisible => GodotObject.IsInstanceValid(_hudErrorState) && _hudErrorState.Visible;
    public string LastPlayerViewError { get; private set; } = "";
    public long GameId => _gameId;
    public GameMapScene MapScene => _mapScene;
    public GameHud Hud => _hud;

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
        _hasPlayerView = false;
        _hud.ClearPlayerView();
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
        _active = false;
        _generation++;
        _requestSequence++;
        _playerViewRequestInProgress = false;
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

        if (result.Data.GameId > 0 && result.Data.GameId != gameId)
        {
            ShowPlayerViewError("服务器返回了不匹配的对局数据。");
            return;
        }

        if (result.Data.Players == null || !result.Data.Players.Exists(player => player.UserId == currentUserId))
        {
            ShowPlayerViewError("玩家视图中未找到当前玩家。");
            return;
        }

        _hud.RefreshPlayerView(result.Data, currentUserId);
        _hasPlayerView = true;
        LastPlayerViewError = "";
        _hudErrorState.Visible = false;
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
        LastPlayerViewError = string.IsNullOrWhiteSpace(message) ? "玩家信息同步失败，请重试。" : message;
        _hudErrorLabel.Text = $"玩家信息同步失败：{LastPlayerViewError}";
        _hudErrorState.Visible = true;
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
}
