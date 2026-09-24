using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunActiveSessionTests : SceneTree
{
    private readonly List<string> _failures = [];
    private Session _session = null!;
    private ApiClient _apiClient = null!;

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
        _session = Root.GetNode<Session>("Session");
        _apiClient = Root.GetNode<ApiClient>("ApiClient");

        await VerifySuccessfulRoutesAsync();
        await VerifyInvalidPhaseAsync();
        await VerifyConflictAndRetryAsync();
        await VerifyNetworkFailureAsync();
        await VerifyAuthExpirationAsync();
        await VerifyFreedScreenIgnoresResponseAsync();
        await VerifyAppRoutesAsync();

        _apiClient.ClearTransportForTests();
        _session.ClearAuth();

        if (_failures.Count == 0)
        {
            GD.Print("活动会话恢复测试全部通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
    }

    private async Task VerifySuccessfulRoutesAsync()
    {
        var cases = new[]
        {
            (new ActiveSessionResponseDto { Phase = "NONE" }, ActiveSessionTarget.Lobby),
            (new ActiveSessionResponseDto { Phase = "WAITING", RoomId = 10 }, ActiveSessionTarget.Room),
            (new ActiveSessionResponseDto { Phase = "CHARACTER_SELECTING", RoomId = 11 }, ActiveSessionTarget.CharacterSelect),
            (new ActiveSessionResponseDto { Phase = "IN_GAME", RoomId = 12, GameId = 22 }, ActiveSessionTarget.GameTest)
        };

        foreach (var (activeSession, expectedTarget) in cases)
        {
            _session.ClearAuth();
            _session.SetCurrentRoom(99, "遗留房间");
            _session.SetCurrentGame(199);
            var transport = new ActiveSessionTransport
            {
                ActiveResult = ApiResult<ActiveSessionResponseDto>.Success(activeSession, "success", 200),
                DelayMilliseconds = 12
            };
            _apiClient.SetTransportForTests(transport);

            var (screen, targets) = await CreateLoginScreenAsync();
            var loginButton = screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton");
            loginButton.EmitSignal(Button.SignalName.Pressed);
            loginButton.EmitSignal(Button.SignalName.Pressed);
            await WaitUntilAsync(() => targets.Count == 1);

            AssertEqual(targets.Count, 1, $"{activeSession.Phase} 只能触发一次恢复跳转");
            AssertEqual(targets.Count > 0 ? targets[0] : ActiveSessionTarget.Invalid, expectedTarget, $"{activeSession.Phase} 页面目标应正确");
            AssertEqual(transport.LoginCount, 1, "快速重复登录不能发送并发登录请求");
            AssertEqual(transport.MeCount, 1, "登录后应只查询一次用户资料");
            AssertEqual(transport.ActiveCount, 1, "登录后应只查询一次活动会话");
            AssertEqual(transport.JoinCount, 0, "恢复活动房间绝不能调用 join");
            AssertEqual(transport.MaxConcurrentActive, 1, "活动会话恢复请求不得重叠");

            if (expectedTarget == ActiveSessionTarget.Lobby)
            {
                AssertEqual(_session.CurrentRoomId, (long?)null, "NONE 应清理 roomId");
                AssertEqual(_session.CurrentGameId, (long?)null, "NONE 应清理 gameId");
            }
            else
            {
                AssertEqual(_session.CurrentRoomId, activeSession.RoomId, $"{activeSession.Phase} 应保存 roomId");
                AssertEqual(_session.CurrentGameId, activeSession.GameId, $"{activeSession.Phase} 的 gameId 应正确");
            }

            screen.QueueFree();
            await ToSignal(this, SignalName.ProcessFrame);
        }
    }

    private async Task VerifyInvalidPhaseAsync()
    {
        var transport = new ActiveSessionTransport
        {
            ActiveResult = ApiResult<ActiveSessionResponseDto>.Success(
                new ActiveSessionResponseDto { Phase = "SOMETHING_NEW", RoomId = 10 },
                "success",
                200)
        };
        _apiClient.SetTransportForTests(transport);
        var (screen, targets) = await CreateLoginScreenAsync();
        screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton").EmitSignal(Button.SignalName.Pressed);
        var retryButton = screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/RetryRecoveryButton");
        await WaitUntilAsync(() => retryButton.Visible && !retryButton.Disabled);

        AssertEqual(targets.Count, 0, "未知 phase 不得触发页面跳转");
        AssertTrue(_session.IsLoggedIn(), "未知 phase 不得清除有效认证");
        AssertEqual(
            screen.GetNode<Label>("CenterContainer/LoginPanel/FormMargin/Form/StatusLabel").Text,
            "无法确认当前房间状态，请重试",
            "未知 phase 应显示统一恢复失败提示"
        );
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task VerifyConflictAndRetryAsync()
    {
        var transport = new ActiveSessionTransport
        {
            ActiveResult = ApiResult<ActiveSessionResponseDto>.Failure("活动会话冲突", "ACTIVE_SESSION_DATA_CONFLICT", 409),
            DelayMilliseconds = 12
        };
        _apiClient.SetTransportForTests(transport);
        var (screen, targets) = await CreateLoginScreenAsync();
        screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton").EmitSignal(Button.SignalName.Pressed);
        var retryButton = screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/RetryRecoveryButton");
        await WaitUntilAsync(() => retryButton.Visible && !retryButton.Disabled);

        AssertEqual(targets.Count, 0, "数据冲突时必须留在恢复页面");
        AssertTrue(_session.IsLoggedIn(), "数据冲突不得清除有效认证");
        transport.ActiveResult = ApiResult<ActiveSessionResponseDto>.Success(
            new ActiveSessionResponseDto { Phase = "WAITING", RoomId = 30 },
            "success",
            200);
        retryButton.EmitSignal(Button.SignalName.Pressed);
        retryButton.EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => targets.Count == 1);

        AssertEqual(transport.ActiveCount, 2, "重试只能额外调用一次 active-session");
        AssertEqual(transport.LoginCount, 1, "恢复重试不得重新登录");
        AssertEqual(transport.MeCount, 1, "恢复重试不得重复获取用户资料");
        AssertEqual(transport.MaxConcurrentActive, 1, "连续点击重试不得产生并发恢复请求");
        AssertEqual(targets[0], ActiveSessionTarget.Room, "重试成功后应进入房间");
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task VerifyNetworkFailureAsync()
    {
        var transport = new ActiveSessionTransport
        {
            ActiveResult = ApiResult<ActiveSessionResponseDto>.Failure("无法连接服务器", "NETWORK_ERROR")
        };
        _apiClient.SetTransportForTests(transport);
        var (screen, targets) = await CreateLoginScreenAsync();
        screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton").EmitSignal(Button.SignalName.Pressed);
        var retryButton = screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/RetryRecoveryButton");
        await WaitUntilAsync(() => retryButton.Visible && !retryButton.Disabled);

        AssertEqual(targets.Count, 0, "网络失败不得默认进入大厅");
        AssertTrue(_session.IsLoggedIn(), "网络失败不得清除有效认证");
        AssertEqual(transport.JoinCount, 0, "网络失败期间不得调用 join");
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task VerifyAuthExpirationAsync()
    {
        var invalidatedCount = 0;
        void OnInvalidated(string _) => invalidatedCount++;
        _apiClient.AuthInvalidated += OnInvalidated;
        var transport = new ActiveSessionTransport
        {
            ActiveResult = ApiResult<ActiveSessionResponseDto>.Failure("登录已过期", "AUTH_TOKEN_EXPIRED", 401)
        };
        _apiClient.SetTransportForTests(transport);
        var (screen, targets) = await CreateLoginScreenAsync();
        screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton").EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => !_session.IsLoggedIn());

        AssertEqual(invalidatedCount, 1, "认证过期应触发一次 AuthInvalidated");
        AssertEqual(targets.Count, 0, "认证过期不得触发业务页面跳转");
        AssertFalse(screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/RetryRecoveryButton").Visible, "认证过期后不应显示恢复重试");
        _apiClient.AuthInvalidated -= OnInvalidated;
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task VerifyFreedScreenIgnoresResponseAsync()
    {
        var gate = new TaskCompletionSource<ApiResult<ActiveSessionResponseDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ActiveSessionTransport { ActiveGate = gate };
        _apiClient.SetTransportForTests(transport);
        var (screen, targets) = await CreateLoginScreenAsync();
        _session.SetCurrentRoom(77, "释放前状态");
        screen.GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton").EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => transport.ActiveCount == 1 && transport.ActiveInFlight == 1);
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        gate.SetResult(ApiResult<ActiveSessionResponseDto>.Success(
            new ActiveSessionResponseDto { Phase = "WAITING", RoomId = 88 },
            "success",
            200));
        await WaitFramesAsync(5);

        AssertEqual(targets.Count, 0, "页面释放后的异步响应不得触发导航");
        AssertEqual(_session.CurrentRoomId, 77L, "页面释放后的异步响应不得覆盖 Session");
    }

    private async Task VerifyAppRoutesAsync()
    {
        var transport = new ActiveSessionTransport();
        _apiClient.SetTransportForTests(transport);
        _session.SetAuth(transport.LoginData);
        _session.UpdateUser(transport.UserData);
        var appScene = GD.Load<PackedScene>("res://scenes/app/App.tscn");
        var app = appScene.Instantiate<App>();
        Root.AddChild(app);
        var host = app.GetNode<Control>("MainLayout/PageHost");

        await EmitRouteAndAssertAsync(app, host, ActiveSessionTarget.Lobby, typeof(LobbyScreen));
        _session.ApplyActiveSession(ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "WAITING", RoomId = 10 }));
        transport.RoomStatus = "WAITING";
        await EmitRouteAndAssertAsync(app, host, ActiveSessionTarget.Room, typeof(RoomScreen));
        _session.ApplyActiveSession(ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "CHARACTER_SELECTING", RoomId = 10 }));
        transport.RoomStatus = "CHARACTER_SELECTING";
        await EmitRouteAndAssertAsync(app, host, ActiveSessionTarget.CharacterSelect, typeof(CharacterSelectScreen));
        _session.ApplyActiveSession(ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "IN_GAME", RoomId = 10, GameId = 20 }));
        await EmitRouteAndAssertAsync(app, host, ActiveSessionTarget.GameTest, typeof(GameTestScreen));

        app.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task EmitRouteAndAssertAsync(App app, Control host, ActiveSessionTarget target, Type expectedType)
    {
        app.ShowLogin();
        var login = host.GetChild(-1) as LoginScreen;
        login?.EmitSignal(LoginScreen.SignalName.RecoverySucceeded, (int)target);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertTrue(expectedType.IsInstanceOfType(host.GetChild(-1)), $"App 应将 {target} 路由到 {expectedType.Name}");
    }

    private async Task<(LoginScreen Screen, List<ActiveSessionTarget> Targets)> CreateLoginScreenAsync()
    {
        var scene = GD.Load<PackedScene>("res://scenes/screens/LoginScreen.tscn");
        var screen = scene.Instantiate<LoginScreen>();
        Root.AddChild(screen);
        await ToSignal(this, SignalName.ProcessFrame);
        screen.GetNode<LineEdit>("CenterContainer/LoginPanel/FormMargin/Form/UsernameEdit").Text = "tester";
        screen.GetNode<LineEdit>("CenterContainer/LoginPanel/FormMargin/Form/PasswordRow/PasswordEdit").Text = "password123";
        var targets = new List<ActiveSessionTarget>();
        screen.RecoverySucceeded += target => targets.Add((ActiveSessionTarget)target);
        return (screen, targets);
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var frame = 0; frame < 360 && !condition(); frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        AssertTrue(condition(), "活动会话异步测试等待超时");
    }

    private async Task WaitFramesAsync(int count)
    {
        for (var frame = 0; frame < count; frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
    }

    private void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            _failures.Add(message);
        }
    }

    private void AssertFalse(bool value, string message)
    {
        AssertTrue(!value, message);
    }

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }

    private sealed class ActiveSessionTransport : IApiTransport
    {
        public LoginResponseDto LoginData { get; } = new()
        {
            AccessToken = "active-session-token",
            RefreshToken = "refresh-token",
            ExpiresIn = 3600,
            User = new UserDto { UserId = 1, Username = "tester", Nickname = "测试玩家" }
        };

        public UserDto UserData { get; } = new() { UserId = 1, Username = "tester", Nickname = "测试玩家" };
        public ApiResult<ActiveSessionResponseDto> ActiveResult { get; set; } = ApiResult<ActiveSessionResponseDto>.Success(
            new ActiveSessionResponseDto { Phase = "NONE" },
            "success",
            200);
        public TaskCompletionSource<ApiResult<ActiveSessionResponseDto>>? ActiveGate { get; set; }
        public string RoomStatus { get; set; } = "WAITING";
        public int DelayMilliseconds { get; set; } = 2;
        public int LoginCount { get; private set; }
        public int MeCount { get; private set; }
        public int ActiveCount { get; private set; }
        public int JoinCount { get; private set; }
        public int ActiveInFlight { get; private set; }
        public int MaxConcurrentActive { get; private set; }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            if (path.EndsWith("/join", StringComparison.Ordinal))
            {
                JoinCount++;
            }

            if (path == "/auth/login" && typeof(T) == typeof(LoginResponseDto))
            {
                LoginCount++;
                await Task.Delay(DelayMilliseconds);
                return (ApiResult<T>)(object)ApiResult<LoginResponseDto>.Success(LoginData, "success", 200);
            }

            if (path == "/users/me" && typeof(T) == typeof(UserDto))
            {
                MeCount++;
                await Task.Delay(DelayMilliseconds);
                return (ApiResult<T>)(object)ApiResult<UserDto>.Success(UserData, "success", 200);
            }

            if (path == "/me/active-session" && typeof(T) == typeof(ActiveSessionResponseDto))
            {
                ActiveCount++;
                ActiveInFlight++;
                MaxConcurrentActive = Math.Max(MaxConcurrentActive, ActiveInFlight);
                var result = ActiveGate != null
                    ? await ActiveGate.Task
                    : await DelayedActiveResultAsync();
                ActiveInFlight--;
                return (ApiResult<T>)(object)result;
            }

            if (path == "/rooms" && typeof(T) == typeof(RoomPageDto))
            {
                return (ApiResult<T>)(object)ApiResult<RoomPageDto>.Success(
                    new RoomPageDto { Records = [], Page = 1, PageSize = 20, Total = 0 },
                    "success",
                    200);
            }

            if (path == "/rooms/10" && typeof(T) == typeof(RoomDetailDto))
            {
                return (ApiResult<T>)(object)ApiResult<RoomDetailDto>.Success(new RoomDetailDto
                {
                    RoomId = 10,
                    RoomName = "恢复测试房间",
                    OwnerUserId = 1,
                    OwnerNickname = "测试玩家",
                    Status = RoomStatus,
                    CurrentPlayers = 1,
                    MaxPlayers = 4,
                    CharacterSelectionDeadline = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                    Members = [new RoomMemberDto { UserId = 1, Nickname = "测试玩家", MemberRole = "OWNER", SeatNo = 1 }]
                }, "success", 200);
            }

            if (path == "/characters" && typeof(T) == typeof(List<CharacterDto>))
            {
                return (ApiResult<T>)(object)ApiResult<List<CharacterDto>>.Success([], "success", 200);
            }

            return ApiResult<T>.Success(default, "success", 200);
        }

        private async Task<ApiResult<ActiveSessionResponseDto>> DelayedActiveResultAsync()
        {
            await Task.Delay(DelayMilliseconds);
            return ActiveResult;
        }
    }
}
