using System;
using System.Threading.Tasks;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Connection;

public enum GameConnectionLeaseState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
    Stopped
}

/// <summary>维护当前窗口在一局对局中的独立连接租约。</summary>
public partial class GameConnectionLease : Node
{
    private const double IdentityResetRetryDelaySeconds = 0.05;

    private Timer _heartbeatTimer = null!;
    private ApiClient _apiClient = null!;
    private Task _requestTask = Task.CompletedTask;
    private bool _active;
    private bool _requestInProgress;
    private int _generation;
    private int _requestSequence;
    private int _lastAppliedLeaseSequence;
    private long _gameId;
    private long _playerId;
    private string? _clientInstanceId;
    private string? _connectionId;
    private GameConnectionPolicyDto? _policy;

    public event Action<GameConnectionLeaseState, string>? StatusChanged;
    public event Action<PlayerViewResponseDto>? PlayerViewReceived;
    public event Action? ConnectionEstablished;

    public GameConnectionLeaseState State { get; private set; } = GameConnectionLeaseState.Idle;
    public string StatusMessage { get; private set; } = "";
    public bool HasActiveConnection => _active && State == GameConnectionLeaseState.Connected && !string.IsNullOrWhiteSpace(_connectionId);
    public bool IsRequestInProgress => _requestInProgress;
    public long GameId => _gameId;
    public long PlayerId => _playerId;
    public string? ClientInstanceId => _clientInstanceId;
    public string? ConnectionId => _connectionId;
    public string? ServerTime { get; private set; }
    public string? ExpiresAt { get; private set; }
    public int HeartbeatSeconds => _policy?.HeartbeatSeconds ?? 0;
    public Task CurrentRequestTask => _requestTask;

    public override void _Ready()
    {
        _apiClient = GetNode<ApiClient>("/root/ApiClient");
        _heartbeatTimer = GetNodeOrNull<Timer>("HeartbeatTimer") ?? new Timer { Name = "HeartbeatTimer" };
        if (_heartbeatTimer.GetParent() == null)
        {
            AddChild(_heartbeatTimer);
        }

        _heartbeatTimer.OneShot = true;
        _heartbeatTimer.Timeout += OnHeartbeatTimerTimeout;
    }

    public override void _ExitTree()
    {
        Stop(true);
        if (GodotObject.IsInstanceValid(_heartbeatTimer))
        {
            _heartbeatTimer.Timeout -= OnHeartbeatTimerTimeout;
        }
    }

    public Task StartAsync(long gameId, long playerId, GameConnectionPolicyDto? policy)
    {
        if (gameId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameId));
        }

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }

        if (_active && _gameId == gameId && _playerId == playerId)
        {
            UpdatePolicy(policy);
            return _requestInProgress ? _requestTask : Task.CompletedTask;
        }

        if (_active)
        {
            Stop(true);
        }

        _generation++;
        _active = true;
        _gameId = gameId;
        _playerId = playerId;
        _policy = MergePolicy(policy, null);
        BeginNewIdentity();
        SetState(GameConnectionLeaseState.Connecting, "正在建立对局连接……");
        return BeginRequest();
    }

    public Task RenewNowAsync()
    {
        if (!_active || _gameId <= 0 || _playerId <= 0)
        {
            return Task.CompletedTask;
        }

        return _requestInProgress ? _requestTask : BeginRequest();
    }

    public void UpdatePolicy(GameConnectionPolicyDto? policy)
    {
        if (policy == null)
        {
            return;
        }

        var merged = MergePolicy(policy, null);
        if (!TryValidatePolicy(merged, out _))
        {
            return;
        }

        _policy = merged;
    }

    public void Stop(bool notifyServer = true)
    {
        var apiClient = GodotObject.IsInstanceValid(_apiClient) ? _apiClient : null;
        var gameId = _gameId;
        var playerId = _playerId;
        var clientInstanceId = _clientInstanceId;
        var connectionId = _connectionId;

        _active = false;
        _generation++;
        _requestSequence++;
        _requestInProgress = false;
        if (GodotObject.IsInstanceValid(_heartbeatTimer))
        {
            _heartbeatTimer.Stop();
        }

        _gameId = 0;
        _playerId = 0;
        _clientInstanceId = null;
        _connectionId = null;
        _policy = null;
        ServerTime = null;
        ExpiresAt = null;
        SetState(GameConnectionLeaseState.Stopped, "");

        if (notifyServer
            && apiClient != null
            && gameId > 0
            && playerId > 0
            && !string.IsNullOrWhiteSpace(clientInstanceId)
            && !string.IsNullOrWhiteSpace(connectionId))
        {
            // 退出回调不等待网络；请求由 Autoload ApiClient 尽力发送，租约超时负责兜底。
            _ = CloseBestEffortAsync(apiClient, gameId, playerId, clientInstanceId!, connectionId!);
        }
    }

    private Task BeginRequest()
    {
        _requestInProgress = true;
        var generation = _generation;
        var sequence = ++_requestSequence;
        var request = new PlayerConnectionRequestDto
        {
            ClientInstanceId = _clientInstanceId ?? "",
            ConnectionId = _connectionId
        };
        _requestTask = RequestCoreAsync(_gameId, _playerId, request, generation, sequence);
        return _requestTask;
    }

    private async Task RequestCoreAsync(
        long gameId,
        long playerId,
        PlayerConnectionRequestDto request,
        int generation,
        int sequence)
    {
        // 即使测试传输层同步完成，也避免在 ApplyPlayerView 调用栈中重入界面状态更新。
        await Task.Yield();
        ApiResult<PlayerViewResponseDto> result;
        try
        {
            result = await _apiClient.UpdateGameConnectionAsync(gameId, playerId, true, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<PlayerViewResponseDto>.Failure(
                $"连接请求异常：{exception.Message}",
                "CONNECTION_REQUEST_EXCEPTION"
            );
        }

        if (!IsCurrent(generation, sequence))
        {
            return;
        }

        _requestInProgress = false;
        if (!result.Ok || result.Data == null)
        {
            HandleFailure(result);
            return;
        }

        if (!TryApplySuccess(result.Data, request, sequence, out var error))
        {
            SetState(GameConnectionLeaseState.Failed, error);
            return;
        }

        ScheduleNextHeartbeat(HeartbeatSeconds);
    }

    private bool TryApplySuccess(
        PlayerViewResponseDto playerView,
        PlayerConnectionRequestDto request,
        int sequence,
        out string error)
    {
        if (playerView.GameId > 0 && playerView.GameId != _gameId)
        {
            error = "连接接口返回了不匹配的对局数据。";
            return false;
        }

        if (playerView.Players == null || !playerView.Players.Exists(player => player.PlayerId == _playerId))
        {
            error = "连接接口返回的玩家视图缺少当前局内玩家。";
            return false;
        }

        var engineState = playerView.EngineState;
        var connection = engineState?.Connection;
        if (connection == null)
        {
            error = "连接接口响应缺少 engineState.connection。";
            return false;
        }

        if (!string.Equals(connection.ClientInstanceId, request.ClientInstanceId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(connection.ConnectionId)
            || connection.ConnectionActive != true
            || connection.PlayerConnected != true)
        {
            error = "连接接口返回的连接身份无效。";
            return false;
        }

        var policy = MergePolicy(engineState?.ConnectionPolicy, connection);
        if (!TryValidatePolicy(policy, out var policyError))
        {
            error = policyError;
            return false;
        }

        if (sequence <= _lastAppliedLeaseSequence)
        {
            error = "收到过期的连接租约响应。";
            return false;
        }

        var wasConnected = HasActiveConnection;
        _lastAppliedLeaseSequence = sequence;
        _policy = policy;
        _connectionId = connection.ConnectionId;
        ServerTime = connection.ServerTime;
        ExpiresAt = connection.ExpiresAt;
        SetState(GameConnectionLeaseState.Connected, "");
        if (!wasConnected)
        {
            ConnectionEstablished?.Invoke();
        }

        PlayerViewReceived?.Invoke(playerView);

        error = "";
        return true;
    }

    private void HandleFailure(ApiResult<PlayerViewResponseDto> result)
    {
        var errorCode = result.ErrorCode ?? "";
        if (errorCode is "CONNECTION_EXPIRED" or "CONNECTION_CLOSED" or "CONNECTION_STATE_CONFLICT")
        {
            BeginNewIdentity();
            SetState(GameConnectionLeaseState.Reconnecting, "连接身份已失效，正在重新连接……");
            ScheduleNextHeartbeat(IdentityResetRetryDelaySeconds);
            return;
        }

        var message = string.IsNullOrWhiteSpace(result.Message) ? "对局连接失败。" : result.Message.Trim();
        if (errorCode == "ACTION_NOT_AVAILABLE")
        {
            SetState(GameConnectionLeaseState.Failed, message);
            return;
        }

        // 网络失败或其他未声明错误都保留本次身份；只有文档明确的三种错误才更换身份。
        SetState(
            HasActiveConnection ? GameConnectionLeaseState.Reconnecting : GameConnectionLeaseState.Failed,
            $"{message} 正在按原连接身份重试。"
        );
        if (TryValidatePolicy(_policy, out _))
        {
            ScheduleNextHeartbeat(HeartbeatSeconds);
        }
    }

    private void BeginNewIdentity()
    {
        _clientInstanceId = Guid.NewGuid().ToString("D");
        _connectionId = null;
        ServerTime = null;
        ExpiresAt = null;
    }

    private void ScheduleNextHeartbeat(double seconds)
    {
        if (!_active || !GodotObject.IsInstanceValid(_heartbeatTimer))
        {
            return;
        }

        _heartbeatTimer.Start(Math.Max(0.01, seconds));
    }

    private bool IsCurrent(int generation, int sequence)
    {
        return IsInsideTree()
            && _active
            && generation == _generation
            && sequence == _requestSequence;
    }

    private void SetState(GameConnectionLeaseState state, string message)
    {
        if (State == state && string.Equals(StatusMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        State = state;
        StatusMessage = message;
        StatusChanged?.Invoke(state, message);
    }

    private void OnHeartbeatTimerTimeout()
    {
        if (_active && !_requestInProgress)
        {
            _ = BeginRequest();
        }
    }

    private static GameConnectionPolicyDto? MergePolicy(
        GameConnectionPolicyDto? policy,
        GameConnectionInfoDto? connection)
    {
        if (policy == null && connection == null)
        {
            return null;
        }

        return new GameConnectionPolicyDto
        {
            HeartbeatSeconds = policy?.HeartbeatSeconds ?? connection?.HeartbeatSeconds,
            TimeoutSeconds = policy?.TimeoutSeconds ?? connection?.TimeoutSeconds,
            ReconnectWaitSeconds = policy?.ReconnectWaitSeconds ?? connection?.ReconnectWaitSeconds,
            InitialGraceSeconds = policy?.InitialGraceSeconds ?? connection?.InitialGraceSeconds
        };
    }

    private static bool TryValidatePolicy(GameConnectionPolicyDto? policy, out string error)
    {
        if (policy?.HeartbeatSeconds is not > 0
            || policy.TimeoutSeconds is not > 0
            || policy.ReconnectWaitSeconds is not > 0
            || policy.InitialGraceSeconds is not > 0)
        {
            error = "连接响应缺少有效的 engineState.connectionPolicy。";
            return false;
        }

        if (policy.HeartbeatSeconds >= policy.TimeoutSeconds
            || policy.InitialGraceSeconds < policy.TimeoutSeconds)
        {
            error = "服务端返回的连接计时策略不合法。";
            return false;
        }

        error = "";
        return true;
    }

    private static async Task CloseBestEffortAsync(
        ApiClient apiClient,
        long gameId,
        long playerId,
        string clientInstanceId,
        string connectionId)
    {
        try
        {
            await apiClient.UpdateGameConnectionAsync(
                gameId,
                playerId,
                false,
                new PlayerConnectionRequestDto
                {
                    ClientInstanceId = clientInstanceId,
                    ConnectionId = connectionId
                }
            );
        }
        catch (Exception exception)
        {
            GD.PushWarning($"关闭对局连接未送达，将由服务端租约超时兜底：{exception.Message}");
        }
    }
}
