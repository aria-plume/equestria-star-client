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
            && !screen.IsPlayerViewRequestInProgress
            && !screen.IsAvailableActionsRequestInProgress
            && screen.Hud.EndTurnVisible
            && !screen.Hud.EndTurnDisabled);
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
        AssertEqual(transport.AvailableActionsRequestCount, 1, "当前用户回合时应查询一次可用行动");
        AssertTrue(transport.Paths.Contains("/games/77/map"), "必须请求对局绑定地图");
        AssertTrue(transport.Paths.Contains("/games/77/player-view"), "必须请求完整玩家视图");
        AssertTrue(transport.Paths.Contains("/games/77/available-actions"), "必须请求当前玩家可用行动");
        AssertFalse(transport.Paths.Contains("/maps/main"), "正式场景不得回退到主地图接口");
        AssertEqual(transport.LastAvailableActionsPlayerId, "1001", "可用行动查询必须使用 playerId");
        AssertEqual(transport.LastAvailableActionsMethod, HttpClient.Method.Get, "可用行动必须使用 GET");
        AssertTrue(transport.LastAvailableActionsAuthorized, "可用行动请求必须携带认证");
        AssertEqual(screen.Hud.TurnNoticeText, "当前是你的回合", "自己回合时应显示明确提示");
        VerifyMapInputRoutingMetadata(screen);
        VerifyTurnHudStates(screen, transport);

        // 可视检查模式保留正式组合场景，便于人工验证地图与 HUD。
        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            GD.Print("正式游戏场景可视检查已就绪。");
            return;
        }

        await VerifyPollingAndRefreshAsync(screen, transport);
        await VerifyEndTurnSuccessAndVersionGuardAsync(screen, transport);
        await VerifyEndTurnErrorsAsync(screen, transport);
        await VerifyFailureAndRetryAsync(screen, transport);
        await VerifyFreedScreenIgnoresResponseAsync(screen, transport);

        apiClient.ClearTransportForTests();
        session.ClearAuth();
        Finish();
    }

    private void VerifyTurnHudStates(GameScreen screen, GameScreenTransport transport)
    {
        var otherTurnView = GameScreenTransport.CloneView(transport.View);
        otherTurnView.CurrentTurnUserId = 2;
        screen.Hud.RefreshPlayerView(otherTurnView, 1);
        AssertEqual(screen.Hud.TurnNoticeText, "当前是 GPT2 的回合", "他人回合时应显示对应昵称");
        AssertFalse(screen.Hud.EndTurnVisible, "他人回合不得显示结束回合按钮");

        var ownTurnView = GameScreenTransport.CloneView(transport.View);
        screen.Hud.RefreshPlayerView(ownTurnView, 1);
        screen.Hud.SetAvailableActions(
            [new AvailableActionDto { ActionType = "END_TURN", Available = false, ReasonText = "仍有必须处理的效果" }],
            ownTurnView.StateVersion
        );
        AssertTrue(screen.Hud.EndTurnVisible, "自己回合应显示结束回合按钮");
        AssertTrue(screen.Hud.EndTurnDisabled, "END_TURN 不可用时按钮必须禁用");
        AssertEqual(screen.Hud.EndTurnTooltip, "仍有必须处理的效果", "禁用按钮应展示 reasonText");

        screen.Hud.SetAvailableActions(
            [new AvailableActionDto { ActionType = "END_TURN", Available = true }],
            ownTurnView.StateVersion
        );
        AssertFalse(screen.Hud.EndTurnDisabled, "END_TURN 可用时按钮应恢复可点击");
    }

    private void VerifyMapInputRoutingMetadata(GameScreen screen)
    {
        AssertEqual(screen.MouseFilter, Control.MouseFilterEnum.Ignore, "GameScreen 全屏布局必须忽略鼠标命中");
        var hudRoot = screen.Hud.GetNode<Control>("HudRoot");
        AssertEqual(hudRoot.MouseFilter, Control.MouseFilterEnum.Ignore, "HudRoot 透明覆盖必须忽略鼠标命中");

        var otherPlayersPanel = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel");
        var currentPlayerPanel = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel");
        var placeholderActions = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/PlaceholderActions");
        var endTurnPanel = screen.Hud.GetNode<Control>("HudRoot/EndTurnAnchor/EndTurnPanel");
        var turnNoticeLabel = screen.Hud.GetNode<Label>(
            "HudRoot/TurnNoticeAnchor/TurnNoticePanel/TurnNoticeMargin/TurnNoticeLabel"
        );
        AssertTrue(otherPlayersPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "其他玩家面板必须标记为地图输入拦截区");
        AssertTrue(currentPlayerPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "当前玩家面板必须标记为地图输入拦截区");
        AssertTrue(placeholderActions.IsInGroup(MapCameraController.MapInputBlockerGroup), "HUD 操作区必须标记为地图输入拦截区");
        AssertTrue(endTurnPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "结束回合按钮区域必须拦截地图输入");
        AssertFalse(MapCameraController.IsMapInputBlocked(turnNoticeLabel), "顶部回合提示不得阻止地图输入");
        AssertTrue(MapCameraController.IsMapInputBlocked(
            screen.Hud.GetNode<Button>("HudRoot/EndTurnAnchor/EndTurnPanel/EndTurnMargin/EndTurnButton")
        ), "结束回合按钮必须通过父节点标记阻止地图输入");
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
        transport.DelayMilliseconds = 2;
    }

    private async Task VerifyEndTurnSuccessAndVersionGuardAsync(GameScreen screen, GameScreenTransport transport)
    {
        var oldView = GameScreenTransport.CloneView(transport.View);
        var playerViewGate = new TaskCompletionSource<ApiResult<PlayerViewResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        transport.PlayerViewGate = playerViewGate;
        var stalePoll = screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => transport.PlayerViewInFlight == 1);

        var actionGate = new TaskCompletionSource<ApiResult<GameActionResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        transport.ActionGate = actionGate;
        var endTurnButton = screen.Hud.GetNode<Button>(
            "HudRoot/EndTurnAnchor/EndTurnPanel/EndTurnMargin/EndTurnButton"
        );
        endTurnButton.EmitSignal(Button.SignalName.Pressed);
        endTurnButton.EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => transport.GameActionInFlight == 1);

        AssertEqual(transport.GameActionRequestCount, 1, "连续点击结束回合只能产生一次请求");
        AssertEqual(transport.LastGameActionMethod, HttpClient.Method.Post, "统一行动必须使用 POST");
        AssertTrue(transport.LastGameActionAuthorized, "统一行动请求必须携带认证");
        AssertTrue(screen.IsEndTurnSubmitting, "提交期间应处于结束回合请求状态");
        AssertTrue(screen.Hud.EndTurnDisabled, "提交期间结束回合按钮必须禁用");
        AssertEqual(
            endTurnButton.Text,
            "正在结束……",
            "提交期间按钮应显示正在结束"
        );

        var request = transport.LastActionRequest;
        AssertTrue(request != null, "统一行动接口必须收到强类型请求体");
        if (request != null)
        {
            AssertEqual(request.PlayerId, 1001L, "结束回合请求必须使用 playerId，不能使用 userId");
            AssertEqual(request.StateVersion, 1L, "结束回合请求必须使用最新 stateVersion");
            AssertEqual(request.ActionType, "END_TURN", "结束回合 actionType 必须正确");
            AssertEqual(request.Payload.Count, 0, "结束回合 payload 必须是空对象");
        }

        transport.View.StateVersion = 2;
        transport.View.CurrentTurnUserId = 2;
        transport.View.CurrentTurnSeq = 2;
        var nextView = GameScreenTransport.CloneView(transport.View);
        actionGate.SetResult(ApiResult<GameActionResponseDto>.Success(
            new GameActionResponseDto
            {
                GameId = 77,
                OldStateVersion = 1,
                NewStateVersion = 2,
                ActionType = "END_TURN",
                ActionTypeText = "结束回合",
                PlayerFacingMessage = "回合已结束",
                PlayerViewState = nextView,
                NextAvailableActions = []
            },
            "success",
            200
        ));
        await WaitUntilAsync(() => !screen.IsEndTurnSubmitting && screen.LatestStateVersion == 2);

        AssertFalse(screen.IsEndTurnSubmitting, "成功后必须清理提交中状态");
        AssertEqual(screen.LatestStateVersion, 2L, "行动响应应立即更新到新 stateVersion");
        AssertEqual(screen.Hud.TurnNoticeText, "当前是 GPT2 的回合", "行动成功后应立即显示下一位玩家");
        AssertFalse(screen.Hud.EndTurnVisible, "当前用户失去回合后应立即隐藏结束回合按钮");

        playerViewGate.SetResult(ApiResult<PlayerViewResponseDto>.Success(oldView, "success", 200));
        await stalePoll;
        AssertEqual(screen.LatestStateVersion, 2L, "较旧轮询响应不得覆盖行动响应");
        AssertEqual(screen.Hud.TurnNoticeText, "当前是 GPT2 的回合", "旧响应不得回滚顶部回合提示");
    }

    private async Task VerifyEndTurnErrorsAsync(GameScreen screen, GameScreenTransport transport)
    {
        await SetServerTurnAsync(screen, transport, 3, 1, true);
        transport.ActionFailureCode = "GAME_STATE_VERSION_EXPIRED";
        transport.ActionFailureMessage = "状态版本已过期";
        transport.ActionRequestCallback = () =>
        {
            transport.View.StateVersion = 4;
            transport.View.CurrentTurnUserId = 1;
        };
        var beforeExpiredRefresh = transport.PlayerViewRequestCount;
        await screen.SubmitEndTurnAsync();
        await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
        AssertTrue(transport.PlayerViewRequestCount > beforeExpiredRefresh, "状态版本过期后必须刷新玩家视图");
        AssertTrue(screen.LastPlayerViewError.Contains("状态已更新", StringComparison.Ordinal), "状态版本过期应显示状态更新提示");
        AssertFalse(screen.IsEndTurnSubmitting, "状态版本错误后必须恢复提交状态");

        await SetServerTurnAsync(screen, transport, 5, 1, true);
        transport.ActionFailureCode = "GAME_NOT_CURRENT_PLAYER";
        transport.ActionFailureMessage = "当前不是你的回合";
        transport.ActionRequestCallback = () =>
        {
            transport.View.StateVersion = 6;
            transport.View.CurrentTurnUserId = 2;
        };
        var beforeNotCurrentRefresh = transport.PlayerViewRequestCount;
        await screen.SubmitEndTurnAsync();
        AssertTrue(transport.PlayerViewRequestCount > beforeNotCurrentRefresh, "非当前玩家错误后必须刷新玩家视图");
        AssertEqual(screen.LastPlayerViewError, "当前不是你的回合", "非当前玩家错误提示必须明确");
        AssertFalse(screen.Hud.EndTurnVisible, "服务端确认已非当前回合时必须隐藏按钮");

        await SetServerTurnAsync(screen, transport, 7, 1, true);
        transport.ActionFailureCode = "ACTION_NOT_AVAILABLE";
        transport.ActionFailureMessage = "服务端拒绝结束回合";
        transport.ActionRequestCallback = () =>
        {
            transport.View.StateVersion = 8;
            transport.View.CurrentTurnUserId = 1;
            transport.SetEndTurnAvailability(false, "仍有必须处理的效果");
        };
        var beforeUnavailableRefresh = transport.PlayerViewRequestCount;
        var beforeUnavailableActions = transport.AvailableActionsRequestCount;
        await screen.SubmitEndTurnAsync();
        await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
        AssertTrue(transport.PlayerViewRequestCount > beforeUnavailableRefresh, "行动不可用后必须刷新玩家视图");
        AssertTrue(transport.AvailableActionsRequestCount > beforeUnavailableActions, "行动不可用后必须刷新可用行动");
        AssertTrue(screen.Hud.EndTurnVisible && screen.Hud.EndTurnDisabled, "行动不可用后应显示禁用按钮");
        AssertEqual(screen.Hud.EndTurnTooltip, "仍有必须处理的效果", "行动不可用后应展示最新 reasonText");
        AssertTrue(screen.LastPlayerViewError.Contains("服务端拒绝结束回合", StringComparison.Ordinal), "行动不可用应优先显示后端 msg");

        await SetServerTurnAsync(screen, transport, 9, 1, true);
        transport.ActionFailureCode = "NETWORK_ERROR";
        transport.ActionFailureMessage = "无法连接服务器，请检查网络。";
        transport.ActionRequestCallback = null;
        var beforeNetworkRefresh = transport.PlayerViewRequestCount;
        await screen.SubmitEndTurnAsync();
        AssertEqual(transport.PlayerViewRequestCount, beforeNetworkRefresh, "网络错误不应擅自切换或重发行动");
        AssertFalse(screen.IsEndTurnSubmitting, "网络错误后必须恢复按钮状态");
        AssertTrue(screen.Hud.EndTurnVisible && !screen.Hud.EndTurnDisabled, "网络错误后应允许玩家重试");
        AssertTrue(screen.LastPlayerViewError.Contains("无法连接服务器", StringComparison.Ordinal), "网络错误应保留可读提示");

        transport.ActionFailureCode = null;
        transport.ActionFailureMessage = "";
        transport.ActionRequestCallback = null;
    }

    private async Task SetServerTurnAsync(
        GameScreen screen,
        GameScreenTransport transport,
        long stateVersion,
        long currentTurnUserId,
        bool endTurnAvailable)
    {
        transport.View.StateVersion = stateVersion;
        transport.View.CurrentTurnUserId = currentTurnUserId;
        transport.View.CurrentTurnSeq++;
        transport.SetEndTurnAvailability(endTurnAvailable, endTurnAvailable ? null : "暂时无法结束回合");
        await screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
    }

    private async Task VerifyFailureAndRetryAsync(GameScreen screen, GameScreenTransport transport)
    {
        transport.FailPlayerView = true;
        var before = transport.PlayerViewRequestCount;
        await screen.RefreshPlayerViewAsync();
        AssertTrue(screen.PlayerViewErrorVisible, "玩家视图失败应显示错误状态");
        AssertTrue(screen.LastPlayerViewError.Contains("模拟玩家视图失败", StringComparison.Ordinal), "错误状态应保留后端或网络消息");
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
        gate.SetResult(ApiResult<PlayerViewResponseDto>.Success(GameScreenTransport.CloneView(transport.View), "success", 200));
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
        public List<AvailableActionDto> AvailableActions { get; private set; } =
        [
            new AvailableActionDto { ActionType = "END_TURN", ActionTypeText = "结束回合", Available = true },
            new AvailableActionDto { ActionType = "MOVE_ONE_STEP", ActionTypeText = "移动一步", Available = true }
        ];
        public List<string> Paths { get; } = [];
        public int MapRequestCount { get; private set; }
        public int PlayerViewRequestCount { get; private set; }
        public int AvailableActionsRequestCount { get; private set; }
        public int GameActionRequestCount { get; private set; }
        public int PlayerViewInFlight { get; private set; }
        public int GameActionInFlight { get; private set; }
        public int MaxConcurrentPlayerViews { get; private set; }
        public int DelayMilliseconds { get; set; } = 2;
        public bool FailPlayerView { get; set; }
        public string? LastAvailableActionsPlayerId { get; private set; }
        public HttpClient.Method LastAvailableActionsMethod { get; private set; }
        public bool LastAvailableActionsAuthorized { get; private set; }
        public HttpClient.Method LastGameActionMethod { get; private set; }
        public bool LastGameActionAuthorized { get; private set; }
        public GameActionRequestDto? LastActionRequest { get; private set; }
        public string? ActionFailureCode { get; set; }
        public string ActionFailureMessage { get; set; } = "";
        public Action? ActionRequestCallback { get; set; }
        public TaskCompletionSource<ApiResult<PlayerViewResponseDto>>? PlayerViewGate { get; set; }
        public TaskCompletionSource<ApiResult<GameActionResponseDto>>? ActionGate { get; set; }

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
                    var gate = PlayerViewGate;
                    PlayerViewGate = null;
                    result = await gate.Task;
                }
                else
                {
                    await Task.Delay(DelayMilliseconds);
                    result = FailPlayerView
                        ? ApiResult<PlayerViewResponseDto>.Failure("模拟玩家视图失败", "NETWORK_ERROR")
                        : ApiResult<PlayerViewResponseDto>.Success(CloneView(View), "success", 200);
                }

                PlayerViewInFlight--;
                return (ApiResult<T>)(object)result;
            }

            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
            {
                AvailableActionsRequestCount++;
                LastAvailableActionsMethod = method;
                LastAvailableActionsAuthorized = authorized;
                string? playerId = null;
                query?.TryGetValue("playerId", out playerId);
                LastAvailableActionsPlayerId = playerId;
                await Task.Delay(DelayMilliseconds);
                return (ApiResult<T>)(object)ApiResult<List<AvailableActionDto>>.Success(
                    AvailableActions.Select(CloneAction).ToList(),
                    "success",
                    200
                );
            }

            if (path == "/games/77/actions" && typeof(T) == typeof(GameActionResponseDto))
            {
                GameActionRequestCount++;
                LastGameActionMethod = method;
                LastGameActionAuthorized = authorized;
                GameActionInFlight++;
                LastActionRequest = body as GameActionRequestDto;
                ActionRequestCallback?.Invoke();
                ApiResult<GameActionResponseDto> result;
                if (ActionGate != null)
                {
                    var gate = ActionGate;
                    ActionGate = null;
                    result = await gate.Task;
                }
                else if (!string.IsNullOrWhiteSpace(ActionFailureCode))
                {
                    await Task.Delay(DelayMilliseconds);
                    result = ApiResult<GameActionResponseDto>.Failure(ActionFailureMessage, ActionFailureCode);
                }
                else
                {
                    await Task.Delay(DelayMilliseconds);
                    result = ApiResult<GameActionResponseDto>.Success(
                        new GameActionResponseDto
                        {
                            GameId = 77,
                            OldStateVersion = LastActionRequest?.StateVersion ?? 0,
                            NewStateVersion = View.StateVersion,
                            ActionType = "END_TURN",
                            PlayerViewState = CloneView(View),
                            NextAvailableActions = AvailableActions.Select(CloneAction).ToList()
                        },
                        "success",
                        200
                    );
                }

                GameActionInFlight--;
                return (ApiResult<T>)(object)result;
            }

            return ApiResult<T>.Failure("测试传输层收到未知请求。", "UNEXPECTED_REQUEST");
        }

        public void SetEndTurnAvailability(bool available, string? reasonText)
        {
            AvailableActions =
            [
                new AvailableActionDto
                {
                    ActionType = "END_TURN",
                    ActionTypeText = "结束回合",
                    Available = available,
                    Reason = available ? null : "END_TURN_BLOCKED",
                    ReasonText = reasonText
                }
            ];
        }

        public static PlayerViewResponseDto CloneView(PlayerViewResponseDto source)
        {
            return new PlayerViewResponseDto
            {
                GameId = source.GameId,
                RoomId = source.RoomId,
                MapConfigId = source.MapConfigId,
                GameStatus = source.GameStatus,
                GameStatusText = source.GameStatusText,
                CurrentRound = source.CurrentRound,
                CurrentTurnUserId = source.CurrentTurnUserId,
                CurrentTurnSeq = source.CurrentTurnSeq,
                StateVersion = source.StateVersion,
                WinnerUserId = source.WinnerUserId,
                Players = source.Players?.Select(ClonePlayer).ToList()
            };
        }

        private static AvailableActionDto CloneAction(AvailableActionDto source)
        {
            return new AvailableActionDto
            {
                ActionType = source.ActionType,
                ActionTypeText = source.ActionTypeText,
                Available = source.Available,
                Reason = source.Reason,
                ReasonText = source.ReasonText
            };
        }

        private static PlayerViewPlayerDto ClonePlayer(PlayerViewPlayerDto source)
        {
            return new PlayerViewPlayerDto
            {
                PlayerId = source.PlayerId,
                UserId = source.UserId,
                Nickname = source.Nickname,
                TurnOrder = source.TurnOrder,
                CurrentCellId = source.CurrentCellId,
                CurrentCellCode = source.CurrentCellCode,
                CurrentCellName = source.CurrentCellName,
                CurrentCellNameText = source.CurrentCellNameText,
                Coins = source.Coins,
                Prestige = source.Prestige,
                CurrentSpeedPoints = source.CurrentSpeedPoints,
                CurrentSocialPoints = source.CurrentSocialPoints,
                EffectiveAttributes = source.EffectiveAttributes == null
                    ? null
                    : new EffectiveAttributesDto
                    {
                        Strength = source.EffectiveAttributes.Strength,
                        Speed = source.EffectiveAttributes.Speed,
                        Social = source.EffectiveAttributes.Social,
                        Charm = source.EffectiveAttributes.Charm,
                        Magic = source.EffectiveAttributes.Magic,
                        Mind = source.EffectiveAttributes.Mind
                    }
            };
        }

        private static PlayerViewResponseDto CreatePlayerView()
        {
            return new PlayerViewResponseDto
            {
                GameId = 77,
                RoomId = 10,
                GameStatus = "RUNNING",
                GameStatusText = "进行中",
                CurrentRound = 1,
                CurrentTurnUserId = 1,
                CurrentTurnSeq = 1,
                StateVersion = 1,
                Players =
                [
                    CreatePlayer(1001, 1, "GPT1", 1, 5, 3),
                    CreatePlayer(1002, 2, "GPT2", 2, 4, 6)
                ]
            };
        }

        private static PlayerViewPlayerDto CreatePlayer(
            long playerId,
            long userId,
            string nickname,
            int turnOrder,
            int coins,
            int prestige)
        {
            return new PlayerViewPlayerDto
            {
                PlayerId = playerId,
                UserId = userId,
                Nickname = nickname,
                TurnOrder = turnOrder,
                CurrentCellId = 100000 + turnOrder - 1,
                CurrentCellCode = $"CELL_{turnOrder:000}",
                CurrentCellName = $"Cell {turnOrder}",
                CurrentCellNameText = $"测试格 {turnOrder}",
                Coins = coins,
                Prestige = prestige,
                CurrentSpeedPoints = 3,
                CurrentSocialPoints = 3,
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
