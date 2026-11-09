using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Connection;
using Godot;

public partial class RunGameConnectionTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTests).CallDeferred();
    }

    private void BeginTests()
    {
        _ = RunTestsAsync();
    }

    private async Task RunTestsAsync()
    {
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new ConnectionTransport();
        apiClient.SetTransportForTests(transport);

        var controller = new GameConnectionLease { Name = "GameConnectionLeaseTests" };
        Root.AddChild(controller);
        await ToSignal(this, SignalName.ProcessFrame);

        var receivedViews = 0;
        var establishedCount = 0;
        controller.PlayerViewReceived += _ => receivedViews++;
        controller.ConnectionEstablished += () => establishedCount++;

        var policy = ConnectionTransport.CreatePolicy();
        await controller.StartAsync(77, 1001, policy);
        AssertTrue(controller.HasActiveConnection, "首次请求成功后必须建立连接");
        AssertEqual(transport.TrueRequests.Count, 1, "首次启动只应发送一次建连请求");
        var initialRequest = transport.TrueRequests[0];
        AssertTrue(initialRequest.Authorized, "连接请求必须携带认证");
        AssertEqual(initialRequest.Path, "/games/77/players/1001/connection", "连接路径必须使用 gameId 和局内 playerId");
        AssertEqual(initialRequest.Connected, "true", "建连 query 必须为 connected=true");
        AssertTrue(Guid.TryParse(initialRequest.Body.ClientInstanceId, out _), "clientInstanceId 必须是本窗口连接尝试的 UUID");
        AssertTrue(string.IsNullOrWhiteSpace(initialRequest.Body.ConnectionId), "首次建连不能提交 connectionId");

        var firstClientInstanceId = controller.ClientInstanceId;
        var firstConnectionId = controller.ConnectionId;
        await controller.RenewNowAsync();
        var renewal = transport.TrueRequests.Last();
        AssertEqual(renewal.Body.ClientInstanceId, firstClientInstanceId, "正常心跳必须复用 clientInstanceId");
        AssertEqual(renewal.Body.ConnectionId, firstConnectionId, "正常心跳必须复用 connectionId");
        AssertEqual(controller.HeartbeatSeconds, 5, "心跳间隔必须读取服务端 connectionPolicy");

        transport.NextErrorCode = "REQUEST_TIMEOUT";
        await controller.RenewNowAsync();
        AssertEqual(controller.ClientInstanceId, firstClientInstanceId, "网络超时不能创建新 clientInstanceId");
        AssertEqual(controller.ConnectionId, firstConnectionId, "网络超时不能丢弃原 connectionId");
        AssertEqual(controller.State, GameConnectionLeaseState.Reconnecting, "网络超时应进入重连提示状态");
        await controller.RenewNowAsync();
        AssertTrue(controller.HasActiveConnection, "网络恢复后应按原身份续约成功");

        foreach (var resetCode in new[] { "CONNECTION_EXPIRED", "CONNECTION_CLOSED", "CONNECTION_STATE_CONFLICT" })
        {
            var previousClientInstanceId = controller.ClientInstanceId;
            var previousConnectionId = controller.ConnectionId;
            transport.NextErrorCode = resetCode;
            var requestsBeforeReset = transport.TrueRequests.Count;
            await controller.RenewNowAsync();
            await WaitUntilAsync(() => transport.TrueRequests.Count >= requestsBeforeReset + 2 && controller.HasActiveConnection);
            AssertTrue(controller.ClientInstanceId != previousClientInstanceId, $"{resetCode} 后必须生成新的 clientInstanceId");
            AssertTrue(controller.ConnectionId != previousConnectionId, $"{resetCode} 后必须取得新的 connectionId");
            AssertTrue(
                string.IsNullOrWhiteSpace(transport.TrueRequests[requestsBeforeReset + 1].Body.ConnectionId),
                $"{resetCode} 重新建连的首次请求不得携带旧 connectionId"
            );
        }

        var requestsBeforeDuplicateStart = transport.TrueRequests.Count;
        await controller.StartAsync(77, 1001, policy);
        AssertEqual(transport.TrueRequests.Count, requestsBeforeDuplicateStart, "重复启动不得创建第二套心跳");

        transport.GateNextTrueRequest = true;
        var lateTask = controller.RenewNowAsync();
        var duplicateTask = controller.RenewNowAsync();
        await WaitUntilAsync(() => transport.TrueRequests.Count == requestsBeforeDuplicateStart + 1);
        AssertEqual(transport.TrueRequests.Count, requestsBeforeDuplicateStart + 1, "并发续约只能发送一个请求");
        var viewsBeforeStop = receivedViews;
        controller.Stop(false);
        transport.ReleaseGate();
        await Task.WhenAll(lateTask, duplicateTask);
        AssertEqual(controller.State, GameConnectionLeaseState.Stopped, "停止后必须保持停止状态");
        AssertEqual(receivedViews, viewsBeforeStop, "停止后的迟到响应不得更新玩家视图");

        await controller.StartAsync(88, 2001, policy);
        AssertEqual(controller.GameId, 88L, "切换对局后必须绑定新 gameId");
        AssertEqual(controller.PlayerId, 2001L, "切换对局后必须绑定新 playerId");
        AssertTrue(controller.HasActiveConnection, "切换对局后应建立新连接");
        var switchedClientInstanceId = controller.ClientInstanceId;
        var switchedConnectionId = controller.ConnectionId;
        controller.Stop(true);
        await WaitUntilAsync(() => transport.FalseRequests.Count >= 1);
        var close = transport.FalseRequests.Last();
        AssertEqual(close.Body.ClientInstanceId, switchedClientInstanceId, "主动关闭必须携带当前窗口 clientInstanceId");
        AssertEqual(close.Body.ConnectionId, switchedConnectionId, "主动关闭必须携带当前 connectionId");
        AssertEqual(close.Connected, "false", "主动关闭 query 必须为 connected=false");
        AssertTrue(establishedCount >= 5, "首次连接、三类身份失效重连和切换对局都应报告连接建立");

        controller.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        apiClient.ClearTransportForTests();
        Finish();
    }

    private async Task WaitUntilAsync(Func<bool> predicate, int maxFrames = 180)
    {
        for (var frame = 0; frame < maxFrames && !predicate(); frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        AssertTrue(predicate(), "等待异步连接状态超时");
    }

    private void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            _failures.Add(message);
        }
    }

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}：期望 {expected}，实际 {actual}");
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("GAME_CONNECTION_TESTS_PASSED");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
    }

    private sealed record RecordedRequest(
        string Path,
        string? Connected,
        PlayerConnectionRequestDto Body,
        bool Authorized
    );

    private sealed class ConnectionTransport : IApiTransport
    {
        private readonly Dictionary<string, string> _connectionIds = new(StringComparer.Ordinal);
        private TaskCompletionSource<bool>? _gate;
        private int _connectionCounter;

        public List<RecordedRequest> TrueRequests { get; } = [];
        public List<RecordedRequest> FalseRequests { get; } = [];
        public string? NextErrorCode { get; set; }
        public bool GateNextTrueRequest { get; set; }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            if (method != HttpClient.Method.Post
                || !path.Contains("/connection", StringComparison.Ordinal)
                || typeof(T) != typeof(PlayerViewResponseDto)
                || body is not PlayerConnectionRequestDto request)
            {
                return ApiResult<T>.Failure("未处理的测试请求", "UNHANDLED_TEST_REQUEST");
            }

            var connected = query != null && query.TryGetValue("connected", out var value) ? value : null;
            var recorded = new RecordedRequest(
                path,
                connected,
                new PlayerConnectionRequestDto
                {
                    ClientInstanceId = request.ClientInstanceId,
                    ConnectionId = request.ConnectionId
                },
                authorized
            );
            if (connected == "false")
            {
                FalseRequests.Add(recorded);
                return (ApiResult<T>)(object)ApiResult<PlayerViewResponseDto>.Success(
                    CreateView(path, request, false),
                    "success",
                    200
                );
            }

            TrueRequests.Add(recorded);
            if (GateNextTrueRequest)
            {
                GateNextTrueRequest = false;
                _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _gate.Task;
            }

            if (!string.IsNullOrWhiteSpace(NextErrorCode))
            {
                var error = NextErrorCode;
                NextErrorCode = null;
                return ApiResult<T>.Failure("模拟连接失败", error);
            }

            return (ApiResult<T>)(object)ApiResult<PlayerViewResponseDto>.Success(
                CreateView(path, request, true),
                "success",
                200
            );
        }

        public void ReleaseGate()
        {
            _gate?.TrySetResult(true);
        }

        public static GameConnectionPolicyDto CreatePolicy()
        {
            return new GameConnectionPolicyDto
            {
                HeartbeatSeconds = 5,
                TimeoutSeconds = 15,
                ReconnectWaitSeconds = 10,
                InitialGraceSeconds = 45
            };
        }

        private PlayerViewResponseDto CreateView(string path, PlayerConnectionRequestDto request, bool active)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var gameId = long.Parse(segments[1]);
            var playerId = long.Parse(segments[3]);
            if (!_connectionIds.TryGetValue(request.ClientInstanceId, out var connectionId))
            {
                connectionId = $"connection-{++_connectionCounter}";
                _connectionIds[request.ClientInstanceId] = connectionId;
            }

            return new PlayerViewResponseDto
            {
                GameId = gameId,
                GameStatus = "RUNNING",
                StateVersion = _connectionCounter,
                Players =
                [
                    new PlayerViewPlayerDto
                    {
                        PlayerId = playerId,
                        UserId = playerId == 1001 ? 1 : 2,
                        Nickname = "连接测试玩家",
                        IsConnected = 1
                    }
                ],
                EngineState = new EngineStateDto
                {
                    ConnectionPolicy = CreatePolicy(),
                    Connection = new GameConnectionInfoDto
                    {
                        ClientInstanceId = request.ClientInstanceId,
                        ConnectionId = connectionId,
                        ServerTime = "2026-11-01T12:00:00.000000",
                        LastHeartbeatAt = "2026-11-01T12:00:00.000000",
                        ExpiresAt = "2026-11-01T12:00:15.000000",
                        ServerTimeZone = "Asia/Shanghai",
                        ServerUtcOffset = "+08:00",
                        ConnectionActive = active,
                        PlayerConnected = true,
                        HeartbeatSeconds = 5,
                        TimeoutSeconds = 15,
                        ReconnectWaitSeconds = 10,
                        InitialGraceSeconds = 45
                    }
                }
            };
        }
    }
}
