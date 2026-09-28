using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunGameScreenTests : SceneTree
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
        var session = Root.GetNode<Session>("Session");
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new GameScreenTransport();
        apiClient.SetTransportForTests(transport);
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "game-screen-token",
            User = new UserDto { UserId = 1, Username = "gpt1", Nickname = "GPT1" }
        });
        session.SetCurrentRoom(10, "正式游戏测试房");
        session.SetCurrentGame(77);

        var scene = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn");
        var screen = scene.Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        screen.Start();
        await WaitUntilAsync(() => screen.MapScene.GeneratedCellCount == 138
            && screen.Hud.BoundCurrentUserId == 1
            && !screen.IsPlayerViewRequestInProgress);
        screen.GetNode<Timer>("PlayerViewTimer").Stop();

        AssertEqual(screen.GameId, 77L, "GameScreen 必须使用 Session.CurrentGameId");
        AssertEqual(screen.MapScene.GameId, 77L, "地图场景必须先取得 gameId");
        AssertFalse(screen.MapScene.AutoLoadOnReady, "正式地图不得在 Ready 时抢先加载");
        AssertTrue(screen.MapScene.RequireGameId, "正式地图必须禁止主地图回退");
        AssertEqual(screen.MapScene.GeneratedCellCount, 138, "正式场景应生成全部地图格子");
        AssertEqual(screen.MapScene.Edges.Count, 385, "正式场景应保留全部地图边");
        AssertEqual(screen.Hud.BoundCurrentUserId, 1L, "HUD 应按当前登录 userId 识别自己");
        AssertEqual(screen.Hud.OtherPlayerCount, 1, "HUD 应仅显示另一名玩家");
        AssertEqual(transport.MapRequestCount, 1, "重复 Start 不得重复加载地图");
        AssertEqual(transport.PlayerViewRequestCount, 1, "重复 Start 不得重复加载玩家视图");
        AssertTrue(transport.Paths.Contains("/games/77/map"), "必须请求对局绑定地图");
        AssertTrue(transport.Paths.Contains("/games/77/player-view"), "必须请求完整玩家视图");
        AssertFalse(transport.Paths.Contains("/maps/main"), "正式场景不得回退到主地图接口");
        VerifyMapInputRoutingMetadata(screen);

        // 可视检查模式保留正式组合场景，便于人工验证地图与 HUD 的输入路由。
        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            GD.Print("正式游戏场景可视检查已就绪。");
            return;
        }

        await VerifyPollingAndRefreshAsync(screen, transport);
        await VerifyFailureAndRetryAsync(screen, transport);
        await VerifyFreedScreenIgnoresResponseAsync(screen, transport);

        apiClient.ClearTransportForTests();
        session.ClearAuth();
        Finish();
    }

    private void VerifyMapInputRoutingMetadata(GameScreen screen)
    {
        AssertEqual(screen.MouseFilter, Control.MouseFilterEnum.Ignore, "GameScreen 全屏布局必须忽略鼠标命中");
        var hudRoot = screen.Hud.GetNode<Control>("HudRoot");
        AssertEqual(hudRoot.MouseFilter, Control.MouseFilterEnum.Ignore, "HudRoot 透明覆盖必须忽略鼠标命中");

        var otherPlayersPanel = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel");
        var currentPlayerPanel = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel");
        var placeholderActions = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/PlaceholderActions");
        var nestedNickname = screen.Hud.GetNode<Label>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/NicknameLabel"
        );
        AssertTrue(otherPlayersPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "其他玩家面板必须标记为地图输入拦截区");
        AssertTrue(currentPlayerPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "当前玩家面板必须标记为地图输入拦截区");
        AssertTrue(placeholderActions.IsInGroup(MapCameraController.MapInputBlockerGroup), "HUD 操作区必须标记为地图输入拦截区");
        AssertTrue(MapCameraController.IsMapInputBlocked(nestedNickname), "HUD 面板内的子控件必须通过祖先标记拦截地图输入");
        AssertFalse(MapCameraController.IsMapInputBlocked(hudRoot), "HUD 透明根节点不得拦截地图输入");

        var playerEntry = screen.Hud
            .GetNode<VBoxContainer>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/ContentMargin/Content/OtherPlayersScroll/OtherPlayersList")
            .GetChildren()
            .OfType<Control>()
            .First(child => child != screen.Hud.GetNode<Label>(
                "HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/ContentMargin/Content/OtherPlayersScroll/OtherPlayersList/EmptyLabel"
            ));
        AssertTrue(playerEntry.IsInGroup(MapCameraController.MapInputBlockerGroup), "其他玩家条目必须标记为地图输入拦截区");

        var errorPanel = screen.GetNode<Control>("StatusLayer/HudErrorState/ErrorPanel");
        var fatalState = screen.GetNode<Control>("StatusLayer/FatalState");
        AssertTrue(errorPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "错误提示面板必须拦截地图输入");
        AssertTrue(fatalState.IsInGroup(MapCameraController.MapInputBlockerGroup), "致命错误遮罩必须拦截地图输入");
    }

    private async Task VerifyPollingAndRefreshAsync(GameScreen screen, GameScreenTransport transport)
    {
        transport.DelayMilliseconds = 30;
        transport.View.Players![0].Coins = 17;
        var before = transport.PlayerViewRequestCount;
        var first = screen.RefreshPlayerViewAsync();
        var second = screen.RefreshPlayerViewAsync();
        await Task.WhenAll(first, second);

        AssertEqual(transport.PlayerViewRequestCount, before + 1, "轮询进行中必须跳过重入请求");
        AssertEqual(transport.MaxConcurrentPlayerViews, 1, "玩家视图请求不得重叠");
        var coinsLabel = screen.Hud.GetNode<Label>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/ResourceRow/CoinsValue"
        );
        AssertEqual(coinsLabel.Text, "17", "刷新后应更新当前玩家数据");
    }

    private async Task VerifyFailureAndRetryAsync(GameScreen screen, GameScreenTransport transport)
    {
        transport.FailPlayerView = true;
        var before = transport.PlayerViewRequestCount;
        await screen.RefreshPlayerViewAsync();
        AssertTrue(screen.PlayerViewErrorVisible, "玩家视图失败应显示可读错误");
        AssertTrue(screen.LastPlayerViewError.Contains("模拟玩家视图失败", StringComparison.Ordinal), "错误提示应保留失败原因");
        AssertEqual(screen.Hud.BoundCurrentUserId, 1L, "临时失败必须保留最后一次成功 HUD 数据");

        transport.FailPlayerView = false;
        screen.GetNode<Button>("StatusLayer/HudErrorState/ErrorPanel/ErrorMargin/ErrorContent/RetryButton")
            .EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => transport.PlayerViewRequestCount > before + 1 && !screen.IsPlayerViewRequestInProgress);
        AssertFalse(screen.PlayerViewErrorVisible, "重试成功后应隐藏错误状态");
    }

    private async Task VerifyFreedScreenIgnoresResponseAsync(GameScreen screen, GameScreenTransport transport)
    {
        var gate = new TaskCompletionSource<ApiResult<PlayerViewResponseDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.PlayerViewGate = gate;
        _ = screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => transport.PlayerViewInFlight == 1);
        screen.Stop();
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        gate.SetResult(ApiResult<PlayerViewResponseDto>.Success(transport.View, "success", 200));
        await WaitUntilAsync(() => transport.PlayerViewInFlight == 0);
        await WaitFramesAsync(2);
        AssertFalse(GodotObject.IsInstanceValid(screen), "页面退出后旧响应不得重新激活场景");
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var frame = 0; frame < 600 && !condition(); frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        if (!condition())
        {
            _failures.Add("等待异步条件超时。");
        }
    }

    private async Task WaitFramesAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("正式游戏场景测试全部通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
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

    private sealed class GameScreenTransport : IApiTransport
    {
        public GameMapDto Map { get; } = RunGameMapTests.CreateValidMap();
        public PlayerViewResponseDto View { get; } = CreatePlayerView();
        public List<string> Paths { get; } = [];
        public int MapRequestCount { get; private set; }
        public int PlayerViewRequestCount { get; private set; }
        public int PlayerViewInFlight { get; private set; }
        public int MaxConcurrentPlayerViews { get; private set; }
        public int DelayMilliseconds { get; set; } = 2;
        public bool FailPlayerView { get; set; }
        public TaskCompletionSource<ApiResult<PlayerViewResponseDto>>? PlayerViewGate { get; set; }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            Paths.Add(path);
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
            {
                MapRequestCount++;
                await Task.Delay(DelayMilliseconds);
                return (ApiResult<T>)(object)ApiResult<GameMapDto>.Success(Map, "success", 200);
            }

            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
            {
                PlayerViewRequestCount++;
                PlayerViewInFlight++;
                MaxConcurrentPlayerViews = Math.Max(MaxConcurrentPlayerViews, PlayerViewInFlight);
                ApiResult<PlayerViewResponseDto> result;
                if (PlayerViewGate != null)
                {
                    result = await PlayerViewGate.Task;
                    PlayerViewGate = null;
                }
                else
                {
                    await Task.Delay(DelayMilliseconds);
                    result = FailPlayerView
                        ? ApiResult<PlayerViewResponseDto>.Failure("模拟玩家视图失败", "NETWORK_ERROR")
                        : ApiResult<PlayerViewResponseDto>.Success(View, "success", 200);
                }

                PlayerViewInFlight--;
                return (ApiResult<T>)(object)result;
            }

            return ApiResult<T>.Failure("测试传输层收到未知请求。", "UNEXPECTED_REQUEST");
        }

        private static PlayerViewResponseDto CreatePlayerView()
        {
            return new PlayerViewResponseDto
            {
                GameId = 77,
                RoomId = 10,
                GameStatus = "RUNNING",
                GameStatusText = "进行中",
                Players =
                [
                    CreatePlayer(1001, 1, "GPT1", 1, 5, 3),
                    CreatePlayer(1002, 2, "GPT2", 2, 4, 6)
                ]
            };
        }

        private static PlayerViewPlayerDto CreatePlayer(long playerId, long userId, string nickname, int turnOrder, int coins, int prestige)
        {
            return new PlayerViewPlayerDto
            {
                PlayerId = playerId,
                UserId = userId,
                Nickname = nickname,
                TurnOrder = turnOrder,
                Coins = coins,
                Prestige = prestige,
                EffectiveAttributes = new EffectiveAttributesDto
                {
                    Strength = 2,
                    Speed = 4,
                    Social = 3,
                    Charm = 2,
                    Magic = 5,
                    Mind = 4
                }
            };
        }
    }
}
