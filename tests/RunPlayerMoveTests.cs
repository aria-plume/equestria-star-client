using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.State;
using EquestriaStar.UI;
using Godot;

public partial class RunPlayerMoveTests : SceneTree
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
        var transport = new MoveTransport();
        apiClient.SetTransportForTests(transport);
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "move-test-token",
            User = new UserDto { UserId = 1, Username = "gpt1", Nickname = "GPT1" }
        });
        session.SetCurrentRoom(10, "移动测试房");
        session.SetCurrentGame(77);

        var scene = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn");
        var screen = scene.Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        await WaitUntilAsync(() => screen.MapScene.GeneratedCellCount == 138
            && screen.MapScene.TokenManager.TokenCount == 2
            && !screen.IsAvailableActionsRequestInProgress);
        screen.GetNode<Timer>("PlayerViewTimer").Stop();
        GD.Print("移动测试：初始化完成");

        var playerToken = screen.MapScene.TokenManager.TokensByPlayerId[1001];
        var otherToken = screen.MapScene.TokenManager.TokensByPlayerId[1002];
        AssertFalse(otherToken.TryActivate(), "其他玩家棋子不得进入移动模式");
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "点击其他玩家棋子后应保持 Idle");

        var camera = screen.MapScene.CameraController;
        AssertTrue(playerToken.BeginPointerClick(), "当前玩家棋子按下时应记录待确认点击");
        camera.BeginPointerGesture(new Vector2(200, 200), Vector3.Zero);
        camera.UpdatePointerGesture(new Vector2(240, 230), new Vector3(-0.8f, 0.0f, 0.6f));
        camera.EndPointerGesture();
        AssertFalse(playerToken.CompletePointerClick(), "从棋子表面开始拖动后不得确认棋子点击");
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "拖动棋子下方地图不得误进入移动模式");

        AssertTrue(playerToken.TryActivate(), "当前玩家棋子应能进入移动模式");
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        VerifyHighlights(screen, transport.StartCellId, transport.FirstTargetCellId);
        AssertEqual(transport.LastMovableMethod, HttpClient.Method.Get, "movable-cells 必须使用 GET");
        AssertTrue(transport.LastMovableAuthorized, "movable-cells 请求必须携带认证");
        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            GD.Print("玩家棋子与移动高亮可视检查已就绪。");
            return;
        }

        VerifyCameraGesturesDoNotExit(screen);
        GD.Print("移动测试：首次进入与摄像机交互完成");

        var actionCount = transport.ActionRequestCount;
        screen.MapScene.CellsById[transport.NonTargetCellId].TryActivate();
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "点击非目标格必须退出移动模式");
        AssertEqual(transport.ActionRequestCount, actionCount, "点击非目标格不得提交移动请求");
        GD.Print("移动测试：非目标格退出完成");

        playerToken.TryActivate();
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        playerToken.TryActivate();
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "再次点击自己的棋子必须退出移动模式");

        playerToken.TryActivate();
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        screen.MapScene.EmitSignal(GameMapScene.SignalName.MapEmptyClicked);
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "地图空白单击必须退出移动模式");
        GD.Print("移动测试：重复棋子与空白退出完成");

        var movableGate = new TaskCompletionSource<ApiResult<List<MapCellDto>>>();
        transport.MovableGate = movableGate;
        playerToken.TryActivate();
        AssertEqual(screen.MoveState, PlayerMoveState.LoadingTargets, "请求目标期间应处于 LoadingTargets");
        screen.ExitMoveMode(true);
        movableGate.SetResult(ApiResult<List<MapCellDto>>.Success(
            [transport.Cell(transport.FirstTargetCellId)], "success", 200));
        await WaitFramesAsync(3);
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "退出后迟到的 movable-cells 响应不得恢复移动模式");
        AssertFalse(screen.MapScene.CellsById[transport.FirstTargetCellId].VisualState == HexCellVisualState.Movable,
            "退出后迟到响应不得重新点亮格子");
        GD.Print("移动测试：迟到响应完成");

        transport.MovableCells = [transport.Cell(transport.FirstTargetCellId), transport.Cell(transport.ActualTargetCellId)];
        playerToken.TryActivate();
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        var oldParent = playerToken.GetParent();
        var actionGate = new TaskCompletionSource<ApiResult<GameActionResponseDto>>();
        transport.ActionGate = actionGate;
        var firstSubmit = screen.SubmitMoveAsync(transport.FirstTargetCellId);
        var duplicateSubmit = screen.SubmitMoveAsync(transport.FirstTargetCellId);
        AssertTrue(ReferenceEquals(firstSubmit, duplicateSubmit), "连续点击目标格必须复用同一个提交任务");
        AssertEqual(transport.ActionRequestCount, actionCount + 1, "连续点击只能产生一次 MOVE_ONE_STEP 请求");
        AssertTrue(playerToken.GetParent() == oldParent, "服务端成功前不得预测移动棋子");
        VerifyMoveRequest(transport.LastActionRequest, transport.FirstTargetCellId, 1);
        AssertEqual(transport.LastActionMethod, HttpClient.Method.Post, "统一行动接口必须使用 POST");
        AssertTrue(transport.LastActionAuthorized, "统一行动请求必须携带认证");
        GD.Print("移动测试：首次提交已挂起");

        transport.MovableCells = [transport.Cell(transport.NextTargetCellId)];
        actionGate.SetResult(transport.BuildMoveSuccess(
            transport.FirstTargetCellId,
            transport.ActualTargetCellId,
            2,
            2,
            includeMoveAction: true
        ));
        await firstSubmit;
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        playerToken = screen.MapScene.TokenManager.TokensByPlayerId[1001];
        AssertTrue(playerToken.GetParent() == screen.MapScene.CellsById[transport.ActualTargetCellId].TokenRoot,
            "移动成功后必须使用 playerViewState 的实际落点");
        AssertTrue(playerToken.IsLifted, "成功后仍有可移动目标时棋子必须保持抬起");
        AssertTrue(transport.MovableRequestCount >= 2, "移动成功后必须重新请求下一步可移动格");
        VerifyHighlights(screen, transport.ActualTargetCellId, transport.NextTargetCellId);
        GD.Print("移动测试：首次移动成功完成");

        var secondActionCount = transport.ActionRequestCount;
        transport.ImmediateMoveResult = transport.BuildMoveSuccess(
            transport.NextTargetCellId,
            transport.NextTargetCellId,
            0,
            3,
            includeMoveAction: false
        );
        var exhaustedSubmit = screen.SubmitMoveAsync(transport.NextTargetCellId);
        await exhaustedSubmit;
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.Idle);
        AssertEqual(transport.ActionRequestCount, secondActionCount + 1, "第二次移动应只提交一次请求");
        AssertFalse(screen.MapScene.TokenManager.TokensByPlayerId[1001].IsLifted, "速度耗尽后必须退出并落下棋子");
        GD.Print("移动测试：速度耗尽完成");

        transport.ResetForCurrentPlayer(4, 2, transport.NextTargetCellId);
        await screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
        transport.MovableCells = [];
        screen.MapScene.TokenManager.TokensByPlayerId[1001].TryActivate();
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.Idle);
        AssertEqual(screen.MovableCellIds.Count, 0, "没有合法目标时必须自动退出移动模式");
        GD.Print("移动测试：无目标退出完成");

        transport.MovableCells = [transport.Cell(transport.NonTargetCellId)];
        screen.MapScene.TokenManager.TokensByPlayerId[1001].TryActivate();
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        transport.ActionFailureCode = "NETWORK_ERROR";
        transport.ActionFailureMessage = "模拟移动超时";
        var callsBeforeNetworkFailure = transport.ActionRequestCount;
        var viewsBeforeNetworkFailure = transport.PlayerViewRequestCount;
        await screen.SubmitMoveAsync(transport.NonTargetCellId);
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "网络错误后必须退出移动模式");
        AssertEqual(transport.ActionRequestCount, callsBeforeNetworkFailure + 1, "网络错误不得自动重发 MOVE_ONE_STEP");
        AssertTrue(transport.PlayerViewRequestCount > viewsBeforeNetworkFailure, "网络错误后必须刷新 player-view 确认位置");
        AssertTrue(screen.PlayerViewErrorVisible, "移动失败必须显示玩家可读提示");
        transport.ActionFailureCode = null;
        GD.Print("移动测试：网络失败完成");

        await VerifyMoveBusinessErrorsAsync(screen, transport);

        transport.View.CurrentTurnUserId = 2;
        transport.View.StateVersion = 20;
        await screen.RefreshPlayerViewAsync();
        AssertFalse(screen.TryEnterMoveMode(1001), "非自己回合不能进入移动模式");

        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        apiClient.ClearTransportForTests();
        session.ClearAuth();
        Finish();
    }

    private async Task VerifyMoveBusinessErrorsAsync(GameScreen screen, MoveTransport transport)
    {
        var cases = new[]
        {
            ("GAME_STATE_VERSION_EXPIRED", "状态版本已过期"),
            ("GAME_NOT_CURRENT_PLAYER", "当前不是你的回合"),
            ("ACTION_NOT_AVAILABLE", "当前行动不可用"),
            ("CELL_NOT_ADJACENT", "目标格不相邻"),
            ("EDGE_NOT_PASSABLE", "道路不可通行")
        };

        for (var index = 0; index < cases.Length; index++)
        {
            transport.ResetForCurrentPlayer(10 + index, 2, transport.NextTargetCellId);
            await screen.RefreshPlayerViewAsync();
            await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
            transport.MovableCells = [transport.Cell(transport.NonTargetCellId)];
            screen.MapScene.TokenManager.TokensByPlayerId[1001].TryActivate();
            await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);

            transport.ActionFailureCode = cases[index].Item1;
            transport.ActionFailureMessage = cases[index].Item2;
            var actionsBefore = transport.ActionRequestCount;
            var viewsBefore = transport.PlayerViewRequestCount;
            await screen.SubmitMoveAsync(transport.NonTargetCellId);
            AssertEqual(screen.MoveState, PlayerMoveState.Idle, $"{cases[index].Item1} 后必须退出移动模式");
            AssertEqual(transport.ActionRequestCount, actionsBefore + 1, $"{cases[index].Item1} 不得自动重发行动");
            AssertTrue(transport.PlayerViewRequestCount > viewsBefore, $"{cases[index].Item1} 后必须刷新 player-view");
            AssertTrue(screen.LastPlayerViewError.Contains(cases[index].Item2, StringComparison.Ordinal),
                $"{cases[index].Item1} 应优先显示后端消息");
            transport.ActionFailureCode = null;
        }

        GD.Print("移动测试：业务错误恢复完成");
    }

    private void VerifyCameraGesturesDoNotExit(GameScreen screen)
    {
        var camera = screen.MapScene.CameraController;
        var rotation = camera.Camera.Rotation;
        camera.ApplyZoomStep(true);
        AssertEqual(screen.MoveState, PlayerMoveState.SelectingTarget, "滚轮缩放不得退出移动模式");
        camera.BeginPointerGesture(new Vector2(100, 100), Vector3.Zero);
        camera.UpdatePointerGesture(new Vector2(150, 140), new Vector3(0.4f, 0.0f, 0.2f));
        camera.EndPointerGesture();
        AssertEqual(screen.MoveState, PlayerMoveState.SelectingTarget, "拖动地图不得退出移动模式");
        AssertEqual(camera.Camera.Rotation, rotation, "拖动不得改变摄像机旋转");
    }

    private void VerifyHighlights(GameScreen screen, long currentCellId, long movableCellId)
    {
        AssertEqual(screen.MapScene.CellsById[currentCellId].VisualState, HexCellVisualState.Current,
            "当前所在格必须显示黄色 Current 高亮");
        AssertEqual(screen.MapScene.CellsById[movableCellId].VisualState, HexCellVisualState.Movable,
            "合法目标格必须显示蓝色 Movable 高亮");
    }

    private void VerifyMoveRequest(GameActionRequestDto? request, long targetCellId, long stateVersion)
    {
        AssertTrue(request != null, "移动请求体必须存在");
        if (request == null)
        {
            return;
        }

        AssertEqual(request.PlayerId, 1001L, "移动请求必须使用 game playerId");
        AssertEqual(request.StateVersion, stateVersion, "移动请求必须使用最新 stateVersion");
        AssertEqual(request.ActionType, "MOVE_ONE_STEP", "移动行动类型必须正确");
        AssertTrue(request.Payload.TryGetPropertyValue("targetCellId", out var targetNode), "移动 payload 必须包含 targetCellId");
        AssertEqual(targetNode?.GetValue<long>() ?? 0, targetCellId, "移动 payload 目标格必须正确");
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
        for (var frame = 0; frame < count; frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("玩家逐格移动测试全部通过。");
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

    private void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }

    private sealed class MoveTransport : IApiTransport
    {
        public GameMapDto Map { get; } = RunGameMapTests.CreateValidMap();
        public PlayerViewResponseDto View { get; }
        public List<AvailableActionDto> AvailableActions { get; private set; } = CreateAvailableActions(true);
        public List<MapCellDto> MovableCells { get; set; }
        public int PlayerViewRequestCount { get; private set; }
        public int MovableRequestCount { get; private set; }
        public int ActionRequestCount { get; private set; }
        public HttpClient.Method LastMovableMethod { get; private set; }
        public bool LastMovableAuthorized { get; private set; }
        public HttpClient.Method LastActionMethod { get; private set; }
        public bool LastActionAuthorized { get; private set; }
        public GameActionRequestDto? LastActionRequest { get; private set; }
        public string? ActionFailureCode { get; set; }
        public string ActionFailureMessage { get; set; } = "";
        public TaskCompletionSource<ApiResult<List<MapCellDto>>>? MovableGate { get; set; }
        public TaskCompletionSource<ApiResult<GameActionResponseDto>>? ActionGate { get; set; }
        public ApiResult<GameActionResponseDto>? ImmediateMoveResult { get; set; }
        public long StartCellId => Map.Cells![0].CellId;
        public long FirstTargetCellId => Map.Cells![1].CellId;
        public long ActualTargetCellId => Map.Cells![2].CellId;
        public long NextTargetCellId => Map.Cells![3].CellId;
        public long NonTargetCellId => Map.Cells![10].CellId;

        public MoveTransport()
        {
            View = new PlayerViewResponseDto
            {
                GameId = 77,
                RoomId = 10,
                GameStatus = "RUNNING",
                GameStatusText = "进行中",
                CurrentTurnUserId = 1,
                StateVersion = 1,
                Players =
                [
                    CreatePlayer(1001, 1, "GPT1", 1, StartCellId, 3),
                    CreatePlayer(1002, 2, "GPT2", 2, StartCellId, 3)
                ]
            };
            MovableCells = [Cell(FirstTargetCellId), Cell(ActualTargetCellId)];
        }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
            {
                return (ApiResult<T>)(object)ApiResult<GameMapDto>.Success(Map, "success", 200);
            }

            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
            {
                PlayerViewRequestCount++;
                return (ApiResult<T>)(object)ApiResult<PlayerViewResponseDto>.Success(CloneView(View), "success", 200);
            }

            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
            {
                return (ApiResult<T>)(object)ApiResult<List<AvailableActionDto>>.Success(
                    AvailableActions.Select(CloneAction).ToList(), "success", 200);
            }

            if (path == "/games/77/players/1001/movable-cells" && typeof(T) == typeof(List<MapCellDto>))
            {
                MovableRequestCount++;
                LastMovableMethod = method;
                LastMovableAuthorized = authorized;
                if (MovableGate != null)
                {
                    var gate = MovableGate;
                    MovableGate = null;
                    return (ApiResult<T>)(object)await gate.Task;
                }

                return (ApiResult<T>)(object)ApiResult<List<MapCellDto>>.Success(
                    MovableCells.Select(CloneCell).ToList(), "success", 200);
            }

            if (path == "/games/77/actions" && typeof(T) == typeof(GameActionResponseDto))
            {
                ActionRequestCount++;
                LastActionMethod = method;
                LastActionAuthorized = authorized;
                LastActionRequest = body as GameActionRequestDto;
                if (ActionGate != null)
                {
                    var gate = ActionGate;
                    ActionGate = null;
                    return (ApiResult<T>)(object)await gate.Task;
                }

                if (!string.IsNullOrWhiteSpace(ActionFailureCode))
                {
                    return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure(
                        ActionFailureMessage, ActionFailureCode);
                }

                if (ImmediateMoveResult != null)
                {
                    var result = ImmediateMoveResult;
                    ImmediateMoveResult = null;
                    return (ApiResult<T>)(object)result;
                }
            }

            return ApiResult<T>.Failure("测试传输层收到未知请求。", "UNEXPECTED_REQUEST");
        }

        public ApiResult<GameActionResponseDto> BuildMoveSuccess(
            long requestedTargetCellId,
            long actualTargetCellId,
            int remainingSpeed,
            long newStateVersion,
            bool includeMoveAction)
        {
            var current = View.Players!.Single(player => player.PlayerId == 1001);
            var fromCellId = current.CurrentCellId ?? 0;
            current.CurrentCellId = actualTargetCellId;
            current.CurrentSpeedPoints = remainingSpeed;
            View.StateVersion = newStateVersion;
            AvailableActions = CreateAvailableActions(includeMoveAction);
            return ApiResult<GameActionResponseDto>.Success(
                new GameActionResponseDto
                {
                    GameId = 77,
                    OldStateVersion = newStateVersion - 1,
                    NewStateVersion = newStateVersion,
                    ActionType = "MOVE_ONE_STEP",
                    Movement = new GameMovementResultDto
                    {
                        FromCellId = fromCellId,
                        RequestedTargetCellId = requestedTargetCellId,
                        ActualTargetCellId = actualTargetCellId,
                        CostSpeed = 1,
                        RemainingSpeed = remainingSpeed
                    },
                    PlayerViewState = CloneView(View),
                    NextAvailableActions = AvailableActions.Select(CloneAction).ToList()
                },
                "success",
                200
            );
        }

        public void ResetForCurrentPlayer(long stateVersion, int speed, long cellId)
        {
            var current = View.Players!.Single(player => player.PlayerId == 1001);
            current.CurrentCellId = cellId;
            current.CurrentSpeedPoints = speed;
            View.CurrentTurnUserId = 1;
            View.StateVersion = stateVersion;
            AvailableActions = CreateAvailableActions(true);
        }

        public MapCellDto Cell(long cellId) => CloneCell(Map.Cells!.Single(cell => cell.CellId == cellId));

        private static List<AvailableActionDto> CreateAvailableActions(bool includeMove)
        {
            var actions = new List<AvailableActionDto>
            {
                new() { ActionType = "END_TURN", ActionTypeText = "结束回合", Available = true }
            };
            if (includeMove)
            {
                actions.Add(new AvailableActionDto
                {
                    ActionType = "MOVE_ONE_STEP",
                    ActionTypeText = "移动一步",
                    Available = true
                });
            }

            return actions;
        }

        private static PlayerViewPlayerDto CreatePlayer(
            long playerId,
            long userId,
            string nickname,
            int turnOrder,
            long cellId,
            int speed)
        {
            return new PlayerViewPlayerDto
            {
                PlayerId = playerId,
                UserId = userId,
                Nickname = nickname,
                TurnOrder = turnOrder,
                CurrentCellId = cellId,
                CurrentSpeedPoints = speed,
                CurrentSocialPoints = 3,
                EffectiveAttributes = new EffectiveAttributesDto()
            };
        }

        private static PlayerViewResponseDto CloneView(PlayerViewResponseDto source)
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
                Players = source.Players?.Select(player => new PlayerViewPlayerDto
                {
                    PlayerId = player.PlayerId,
                    UserId = player.UserId,
                    Nickname = player.Nickname,
                    TurnOrder = player.TurnOrder,
                    CurrentCellId = player.CurrentCellId,
                    CurrentSpeedPoints = player.CurrentSpeedPoints,
                    CurrentSocialPoints = player.CurrentSocialPoints,
                    EffectiveAttributes = player.EffectiveAttributes
                }).ToList()
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

        private static MapCellDto CloneCell(MapCellDto source)
        {
            return new MapCellDto
            {
                CellId = source.CellId,
                CellCode = source.CellCode,
                BoardCode = source.BoardCode,
                CellName = source.CellName,
                CellNameText = source.CellNameText,
                Q = source.Q,
                R = source.R,
                InteractionTags = source.InteractionTags?.ToList()
            };
        }
    }
}
