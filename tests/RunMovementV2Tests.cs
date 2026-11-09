using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Dice;
using EquestriaStar.Game.Map;
using EquestriaStar.State;
using EquestriaStar.UI;
using Godot;

public partial class RunMovementV2Tests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize() => Callable.From(BeginTests).CallDeferred();
    private void BeginTests() => _ = RunTestsAsync();

    private async Task RunTestsAsync()
    {
        var userArgs = OS.GetCmdlineUserArgs();
        var visualRailway = userArgs.Contains("--visual-railway", StringComparer.Ordinal);
        var visualForest = userArgs.Contains("--visual-forest", StringComparer.Ordinal);
        var session = Root.GetNode<Session>("Session");
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new MovementV2Transport();
        apiClient.SetTransportForTests(transport);
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "movement-v2-token",
            User = new UserDto { UserId = 1, Username = "gpt1", Nickname = "GPT1" }
        });
        session.SetCurrentRoom(10, "移动 v2 测试房");
        session.SetCurrentGame(77);

        var screen = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn").Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        await WaitUntilAsync(() => screen.MapScene.GeneratedCellCount == 138
            && screen.IsGameConnectionReady
            && !screen.IsAvailableActionsRequestInProgress);
        screen.GetNode<Timer>("PlayerViewTimer").Stop();

        // 即使旧服务返回了同名行动，也不能在能力字段缺失时启用铁路。
        AssertFalse(screen.SupportsMovementV2, "缺少 movementRulesVersion 时不得启用移动 v2");
        AssertTrue(screen.Hud.RailwayButton.Disabled, "能力未确认时铁路按钮必须置灰");
        AssertFalse(screen.TryEnterRailwayMode(), "能力未确认时不得请求铁路目标");

        transport.EnableMovementV2(2, speed: 3, coins: 3);
        await screen.RefreshPlayerViewAsync();
        await screen.RefreshAvailableActionsAsync(true);
        AssertTrue(screen.SupportsMovementV2, "新鲜 player-view 返回整数2后应启用移动 v2");
        AssertFalse(screen.Hud.RailwayButton.Disabled, "USE_RAILWAY 可用时铁路按钮应亮起");

        if (visualRailway)
        {
            GD.Print("MOVEMENT_V2_VISUAL_RAILWAY_READY");
            return;
        }

        if (visualForest)
        {
            transport.NormalCells = [transport.Cell(transport.ForestRequestedTargetCellId, requiresRoll: true)];
            var visualToken = screen.MapScene.TokenManager.TokensByPlayerId[1001];
            visualToken.TryActivate();
            await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
            var visualOrigin = transport.CurrentPlayer.CurrentCellId!.Value;
            transport.NextActionResult = transport.BuildForestPending(
                3, visualOrigin, transport.ForestRequestedTargetCellId, speedAfter: 0);
            await screen.SubmitMoveAsync(transport.ForestRequestedTargetCellId);
            await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
            transport.AutoForestVisualFinal = true;
            GD.Print("MOVEMENT_V2_VISUAL_FOREST_READY");
            return;
        }

        // 走真实 Viewport GUI 分发，不直接 EmitSignal。
        await DispatchViewportClickAsync(screen.Hud.ActionMenuButton);
        await DispatchViewportClickAsync(screen.Hud.RailwayButton);
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        AssertEqual(screen.MoveMode, PlayerMoveMode.Railway, "铁路按钮必须进入独立铁路模式");
        AssertEqual(transport.LastMovableActionType, "USE_RAILWAY", "铁路目标查询必须携带 actionType=USE_RAILWAY");
        AssertTrue(screen.MovableCellIds.SetEquals([transport.RailTargetCellId]), "铁路高亮不能混入普通移动目标");
        AssertEqual(screen.MovableTargets[transport.RailTargetCellId].CostSpeed, 1, "铁路目标应解析行动值成本");
        AssertEqual(screen.MovableTargets[transport.RailTargetCellId].CostCoins, 1, "铁路目标应解析金币成本");

        transport.NextActionResult = transport.BuildMovedAction(
            "USE_RAILWAY", transport.RailTargetCellId, transport.RailTargetCellId, 2, 2, 3);
        await screen.SubmitMoveAsync(transport.RailTargetCellId);
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        AssertEqual(transport.LastActionRequest?.ActionType, "USE_RAILWAY", "铁路提交必须使用 USE_RAILWAY");
        AssertPayloadOnlyTarget(transport.LastActionRequest, transport.RailTargetCellId, "铁路");
        AssertEqual(screen.MoveMode, PlayerMoveMode.Railway, "铁路成功后应保持铁路选择并重新查询邻站");
        AssertTrue(transport.RailwayMovableRequestCount >= 2, "铁路成功后必须重新请求下一批直接邻站");

        screen.MapScene.CellsById[transport.NonTargetCellId].TryActivate();
        AssertEqual(screen.MoveState, PlayerMoveState.Idle, "铁路模式点击非目标格必须退出");
        AssertEqual(screen.MoveMode, PlayerMoveMode.None, "退出铁路后不得残留铁路模式");

        transport.EnableMovementV2(3, speed: 2, coins: 0);
        await screen.RefreshPlayerViewAsync();
        await screen.RefreshAvailableActionsAsync(true);
        AssertTrue(screen.Hud.RailwayButton.Disabled, "服务端资源不足资格必须让铁路按钮置灰");

        // requiresRoll=false 的普通一步必须直接采用 movement，不能误开森林方向骰。
        transport.EnableMovementV2(4, speed: 1, coins: 3, railwayAvailable: true);
        transport.NormalCells = [transport.Cell(transport.ForestRequestedTargetCellId, requiresRoll: false)];
        await screen.RefreshPlayerViewAsync();
        await screen.RefreshAvailableActionsAsync(true);
        var token = screen.MapScene.TokenManager.TokensByPlayerId[1001];
        AssertTrue(token.TryActivate(), "普通一步应能从自己的棋子进入");
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        transport.NextActionResult = transport.BuildMovedAction(
            "MOVE_ONE_STEP", transport.ForestRequestedTargetCellId, transport.ForestRequestedTargetCellId, 0, 5, 3);
        await screen.SubmitMoveAsync(transport.ForestRequestedTargetCellId);
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.Idle);
        AssertFalse(screen.WorkDiceOverlay.IsOpen, "requiresRoll=false 的普通移动不得误入森林方向骰");
        AssertEqual(transport.CurrentPlayer.CurrentCellId, transport.ForestRequestedTargetCellId, "普通一步应采用服务端实际落点");

        // 普通目标查询不得携带铁路 actionType，目标元数据由服务端决定是否需要骰子。
        transport.EnableMovementV2(6, speed: 1, coins: 3, railwayAvailable: true);
        transport.NormalCells = [transport.Cell(transport.ForestRequestedTargetCellId, requiresRoll: true)];
        await screen.RefreshPlayerViewAsync();
        await screen.RefreshAvailableActionsAsync(true);
        token = screen.MapScene.TokenManager.TokensByPlayerId[1001];
        AssertTrue(token.TryActivate(), "普通移动仍应从自己的棋子进入");
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        AssertTrue(string.IsNullOrEmpty(transport.LastMovableActionType), "普通目标查询不得冒充铁路模式");
        AssertEqual(screen.MoveMode, PlayerMoveMode.Normal, "普通移动与铁路模式必须互斥");

        var originCellId = transport.CurrentPlayer.CurrentCellId!.Value;
        transport.NextActionResult = transport.BuildForestPending(7, originCellId, transport.ForestRequestedTargetCellId, speedAfter: 0);
        await screen.SubmitMoveAsync(transport.ForestRequestedTargetCellId);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        AssertEqual(transport.CurrentPlayer.CurrentCellId, originCellId, "森林准备成功不得提前移动棋子");
        AssertEqual(transport.CurrentPlayer.CurrentSpeedPoints, 0, "森林准备应采用服务端已扣行动值状态");
        AssertEqual(screen.WorkDiceOverlay.CurrentBusinessType, "FOREST_MOVE", "森林 pending 不能误当 WORK");
        AssertPayloadOnlyTarget(transport.LastActionRequest, transport.ForestRequestedTargetCellId, "森林准备");
        AssertEqual(transport.LastActionRequest?.ActionType, "MOVE_ONE_STEP", "森林准备仍使用 MOVE_ONE_STEP");

        CompletePhysicalRoll(screen.WorkDiceOverlay);
        AssertTrue(screen.WorkDiceOverlay.ConfirmEnabled, "准备扣到0行动值后仍应按 canConfirm 允许确认");
        var firstDice = screen.WorkDiceOverlay.PhysicalDice;
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.RerollButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        AssertEqual(screen.WorkDiceOverlay.PhysicalDice, 0, "森林重投授权成功后旧点数必须失效");
        AssertEqual(transport.LastRerollRequest?.AuthorizationRound, 0, "重投必须使用当前授权轮次");
        CompletePhysicalRoll(screen.WorkDiceOverlay);
        var finalDice = screen.WorkDiceOverlay.PhysicalDice;

        transport.NextRollResult = transport.BuildForestFinal(
            submittedDice: finalDice,
            effectiveDice: 6,
            actualTargetCellId: originCellId,
            outcome: "STAYED",
            stayReason: "ROAD_CLOSED",
            includeV2CostFields: true);
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.ConfirmButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.Result);
        var forestResultText = screen.WorkDiceOverlay.ResultText;
        AssertEqual(transport.RollRequestCount, 1, "森林最终确认只能提交一次");
        AssertEqual(transport.LastRollRequest?.Dice, finalDice, "最终确认只能上传最新物理点数");
        AssertTrue(forestResultText.Contains("服务端有效点数：6"), $"固定骰必须展示服务端 effectiveDice，实际：{forestResultText}");
        AssertTrue(forestResultText.Contains("原地停留"), $"STAYED(code=1)必须显示原地停留，实际：{forestResultText}");
        AssertTrue(forestResultText.Contains("道路不可通行"), $"STAYED必须展示可靠原因，实际：{forestResultText}");
        AssertFalse(forestResultText.Contains("本次收入"), "森林结果不得显示打工奖励面板");
        AssertEqual(transport.CurrentPlayer.CurrentCellId, originCellId, "STAYED 必须按 actualTarget 保持原位");

        await WaitUntilAsync(() => !screen.IsCheckSubmitting);
        transport.ConfigureAfterForestResult(10, speed: 1, moveAvailable: true);
        transport.NormalCells = [transport.Cell(transport.ForestRequestedTargetCellId, requiresRoll: false)];
        var actionCountAfterRoll = transport.ActionRequestCount;
        await DispatchViewportClickAsync(new Vector2(30, 30));
        await WaitUntilAsync(() => !screen.WorkDiceOverlay.IsOpen);
        AssertEqual(transport.ActionRequestCount, actionCountAfterRoll, "关闭森林结果的点击不得穿透提交新移动");
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        AssertEqual(screen.MoveMode, PlayerMoveMode.Normal, "森林结果返回后应按最新资格恢复普通移动选择");
        screen.ExitMoveMode(true);

        // 历史回执缺少金币字段只能显示未知，不能默认为0。
        var historical = transport.BuildForestFinal(
            submittedDice: 3,
            effectiveDice: 3,
            actualTargetCellId: transport.ForestRequestedTargetCellId,
            outcome: "MOVED",
            stayReason: null,
            includeV2CostFields: false).Data!;
        screen.WorkDiceOverlay.ShowForestMoveResult(historical);
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("历史回执未提供"), "历史缺字段不得伪装成0费用或0余额");
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("已抵达"), "MOVED 森林回执必须展示实际抵达");
        screen.WorkDiceOverlay.CloseOverlay();

        // 重连只恢复同一个 FOREST_MOVE pending，不重新提交 MOVE_ONE_STEP 准备。
        var actionCountBeforeRecovery = transport.ActionRequestCount;
        transport.RestoreForestPending(11, transport.CurrentPlayer.CurrentCellId!.Value, transport.ForestRequestedTargetCellId);
        await screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        AssertEqual(screen.WorkDiceOverlay.CurrentBusinessType, "FOREST_MOVE", "重连应恢复 FOREST_MOVE 而不是误当 WORK");
        AssertEqual(transport.ActionRequestCount, actionCountBeforeRecovery, "恢复森林 pending 不得重新提交移动准备");
        screen.WorkDiceOverlay.CloseOverlay();

        // 连接心跳节点在整个独立投骰期间仍保持运行，没有暂停 SceneTree。
        AssertTrue(screen.IsGameConnectionReady, "森林投骰期间连接租约必须持续运行");
        AssertFalse(Paused, "独立投骰不得暂停整个 SceneTree");

        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        apiClient.ClearTransportForTests();
        session.ClearAuth();
        Finish();
    }

    private static void AssertPayloadOnlyTarget(GameActionRequestDto? request, long targetCellId, string label)
    {
        if (request == null) throw new InvalidOperationException($"{label}请求不存在。");
        if (request.Payload.Count != 1
            || !request.Payload.TryGetPropertyValue("targetCellId", out var node)
            || node?.GetValue<long>() != targetCellId)
        {
            throw new InvalidOperationException($"{label} payload 必须且只能包含 targetCellId。");
        }
        if (!Guid.TryParseExact(request.RequestId, "D", out _))
            throw new InvalidOperationException($"{label}必须携带标准 UUID requestId。");
    }

    private static void CompletePhysicalRoll(WorkDiceOverlay overlay)
    {
        var thrown = overlay.Dice.TryThrow(new Vector2(360, -180), 0.24, overlay.Controller.Camera);
        if (!thrown) throw new InvalidOperationException("测试物理骰未成功投出。");
        overlay.Dice.Freeze = true;
        overlay.Dice.LinearVelocity = Vector3.Zero;
        overlay.Dice.AngularVelocity = Vector3.Zero;
        overlay.Dice.Quaternion = Quaternion.Identity;
        overlay.Dice.AdvanceSettlement(0.8);
    }

    private async Task DispatchViewportClickAsync(Control control)
    {
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchViewportClickAsync(control.GetGlobalRect().GetCenter());
    }

    private async Task DispatchViewportClickAsync(Vector2 position)
    {
        Root.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
        await ToSignal(this, SignalName.ProcessFrame);
        Root.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left,
            Pressed = true,
            Position = position,
            GlobalPosition = position
        }, true);
        await ToSignal(this, SignalName.ProcessFrame);
        Root.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = position,
            GlobalPosition = position
        }, true);
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var frame = 0; frame < 900 && !condition(); frame++)
            await ToSignal(this, SignalName.ProcessFrame);
        if (!condition()) _failures.Add("等待异步条件超时。");
    }

    private void AssertTrue(bool value, string message) { if (!value) _failures.Add(message); }
    private void AssertFalse(bool value, string message) => AssertTrue(!value, message);
    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("MOVEMENT_V2_TESTS_PASSED");
            Quit(0);
            return;
        }
        foreach (var failure in _failures) GD.PushError(failure);
        Quit(1);
    }

    private sealed class MovementV2Transport : IApiTransport
    {
        public GameMapDto Map { get; } = RunGameMapTests.CreateValidMap();
        public PlayerViewResponseDto View { get; }
        public List<AvailableActionDto> AvailableActions { get; private set; } = [];
        public List<MapCellDto> NormalCells { get; set; } = [];
        public List<MapCellDto> RailwayCells { get; set; } = [];
        public ApiResult<GameActionResponseDto>? NextActionResult { get; set; }
        public ApiResult<CheckRollResponseDto>? NextRollResult { get; set; }
        public bool AutoForestVisualFinal { get; set; }
        public PendingCheckDto? Pending { get; private set; }
        public string? LastMovableActionType { get; private set; }
        public int RailwayMovableRequestCount { get; private set; }
        public int ActionRequestCount { get; private set; }
        public int RollRequestCount { get; private set; }
        public GameActionRequestDto? LastActionRequest { get; private set; }
        public CheckRerollRequestDto? LastRerollRequest { get; private set; }
        public CheckRollRequestDto? LastRollRequest { get; private set; }

        public long StartCellId => Map.Cells![0].CellId;
        public long RailTargetCellId => Map.Cells![1].CellId;
        public long ForestRequestedTargetCellId => Map.Cells![2].CellId;
        public long NonTargetCellId => Map.Cells![10].CellId;
        public PlayerViewPlayerDto CurrentPlayer => View.Players!.Single(player => player.PlayerId == 1001);

        public MovementV2Transport()
        {
            View = new PlayerViewResponseDto
            {
                GameId = 77,
                RoomId = 10,
                GameStatus = "RUNNING",
                GameStatusText = "进行中",
                CurrentTurnUserId = 1,
                StateVersion = 1,
                EngineState = new EngineStateDto
                {
                    Phase = "MIDDLE",
                    CurrentPlayerId = 1001,
                    ConnectionPolicy = ConnectionPolicy()
                },
                Players =
                [
                    Player(1001, 1, "GPT1", 1, StartCellId, 3, 3),
                    Player(1002, 2, "GPT2", 2, StartCellId, 3, 3)
                ]
            };
            AvailableActions = Actions(move: true, railway: true);
            NormalCells = [Cell(ForestRequestedTargetCellId, requiresRoll: false)];
            RailwayCells = [Cell(RailTargetCellId, requiresRoll: false, costCoins: 1)];
        }

        public void EnableMovementV2(long version, int speed, int coins, bool railwayAvailable = true)
        {
            View.StateVersion = version;
            View.EngineState!.MovementRulesVersion = 2;
            View.EngineState.PendingCheck = null;
            CurrentPlayer.CurrentSpeedPoints = speed;
            CurrentPlayer.Coins = coins;
            CurrentPlayer.CurrentCellId = StartCellId;
            AvailableActions = Actions(move: speed > 0, railway: railwayAvailable && speed > 0 && coins > 0);
        }

        public void ConfigureAfterForestResult(long version, int speed, bool moveAvailable)
        {
            View.StateVersion = version;
            View.EngineState!.MovementRulesVersion = 2;
            View.EngineState.PendingCheck = null;
            CurrentPlayer.CurrentSpeedPoints = speed;
            Pending = null;
            AvailableActions = Actions(move: moveAvailable, railway: false);
        }

        public void RestoreForestPending(long version, long fromCellId, long requestedTargetCellId)
        {
            View.StateVersion = version;
            View.EngineState!.MovementRulesVersion = 2;
            Pending = ForestPending(version, 0, fromCellId, requestedTargetCellId);
            View.EngineState.PendingCheck = Clone(Pending);
            AvailableActions = [];
        }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
                return Cast<T, GameMapDto>(ApiResult<GameMapDto>.Success(Map, "success", 200));

            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
                return Cast<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(Clone(View), "success", 200));

            if (path == "/games/77/players/1001/connection" && typeof(T) == typeof(PlayerViewResponseDto)
                && body is PlayerConnectionRequestDto connection)
            {
                var response = Clone(View)!;
                response.EngineState!.ConnectionPolicy = ConnectionPolicy();
                response.EngineState.Connection = ConnectionInfo(connection);
                return Cast<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(response, "success", 200));
            }

            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
                return Cast<T, List<AvailableActionDto>>(ApiResult<List<AvailableActionDto>>.Success(Clone(AvailableActions), "success", 200));

            if (path == "/games/77/players/1001/movable-cells" && typeof(T) == typeof(List<MapCellDto>))
            {
                LastMovableActionType = query != null && query.TryGetValue("actionType", out var value) ? value : null;
                var railway = string.Equals(LastMovableActionType, "USE_RAILWAY", StringComparison.Ordinal);
                if (railway) RailwayMovableRequestCount++;
                var cells = railway ? RailwayCells : NormalCells;
                return Cast<T, List<MapCellDto>>(ApiResult<List<MapCellDto>>.Success(Clone(cells), "success", 200));
            }

            if (path == "/games/77/actions" && typeof(T) == typeof(GameActionResponseDto))
            {
                ActionRequestCount++;
                LastActionRequest = body as GameActionRequestDto;
                ValidateGameAction(LastActionRequest);
                var result = NextActionResult ?? ApiResult<GameActionResponseDto>.Failure("未配置行动回执", "TEST_MISSING_RESULT");
                NextActionResult = null;
                return Cast<T, GameActionResponseDto>(result);
            }

            if (path == "/games/77/checks/pending" && typeof(T) == typeof(PendingCheckDto))
                return Cast<T, PendingCheckDto>(ApiResult<PendingCheckDto>.Success(Clone(Pending), "success", 200));

            if (path == "/games/77/checks/501/reroll" && typeof(T) == typeof(PendingCheckDto))
            {
                LastRerollRequest = body as CheckRerollRequestDto;
                if (Pending == null || LastRerollRequest == null
                    || LastRerollRequest.AuthorizationRound != Pending.AuthorizationRound)
                    return ApiResult<T>.Failure("重投请求不匹配", "PARAM_INVALID");
                Pending.StateVersion = (Pending.StateVersion ?? 0) + 1;
                Pending.AuthorizationRound = (Pending.AuthorizationRound ?? 0) + 1;
                Pending.CanReroll = false;
                View.StateVersion = Pending.StateVersion.Value;
                View.EngineState!.PendingCheck = Clone(Pending);
                return Cast<T, PendingCheckDto>(ApiResult<PendingCheckDto>.Success(Clone(Pending), "success", 200));
            }

            if (path == "/games/77/checks/501/roll" && typeof(T) == typeof(CheckRollResponseDto))
            {
                RollRequestCount++;
                LastRollRequest = body as CheckRollRequestDto;
                if (AutoForestVisualFinal && Pending != null && LastRollRequest != null)
                {
                    NextRollResult = BuildForestFinal(
                        submittedDice: LastRollRequest.Dice,
                        effectiveDice: 6,
                        actualTargetCellId: Pending.Preparation!.FromCellId!.Value,
                        outcome: "STAYED",
                        stayReason: "ROAD_CLOSED",
                        includeV2CostFields: true);
                    AutoForestVisualFinal = false;
                }
                var expectedAuthorizationRound = Pending?.AuthorizationRound
                    ?? NextRollResult?.Data?.PendingCheck?.AuthorizationRound;
                if (LastRollRequest == null
                    || expectedAuthorizationRound == null
                    || LastRollRequest.AuthorizationRound != expectedAuthorizationRound)
                    return ApiResult<T>.Failure("确认请求不匹配", "PARAM_INVALID");
                var result = NextRollResult ?? ApiResult<CheckRollResponseDto>.Failure("未配置最终回执", "TEST_MISSING_RESULT");
                if (result.Ok && result.Data != null)
                {
                    result.Data.SubmittedDice = LastRollRequest.Dice;
                }
                NextRollResult = null;
                return Cast<T, CheckRollResponseDto>(result);
            }

            await Task.Yield();
            return ApiResult<T>.Failure($"未知测试请求：{method} {path}", "UNEXPECTED_REQUEST");
        }

        public ApiResult<GameActionResponseDto> BuildMovedAction(
            string actionType,
            long requested,
            long actual,
            int speedAfter,
            long version,
            int coinsAfter)
        {
            var from = CurrentPlayer.CurrentCellId!.Value;
            CurrentPlayer.CurrentCellId = actual;
            CurrentPlayer.CurrentSpeedPoints = speedAfter;
            CurrentPlayer.Coins = coinsAfter;
            View.StateVersion = version;
            // 行动回执故意不携带能力字段，不能反向关闭新鲜 GET 已确认的能力。
            var responseView = Clone(View)!;
            responseView.EngineState!.MovementRulesVersion = null;
            RailwayCells = [Cell(ForestRequestedTargetCellId, requiresRoll: false, costCoins: 1)];
            return ApiResult<GameActionResponseDto>.Success(new GameActionResponseDto
            {
                GameId = 77,
                OldStateVersion = version - 1,
                NewStateVersion = version,
                ActionType = actionType,
                ResultType = "MOVED",
                Movement = new GameMovementResultDto
                {
                    FromCellId = from,
                    RequestedTargetCellId = requested,
                    ActualTargetCellId = actual,
                    CostSpeed = 1,
                    RemainingSpeed = speedAfter,
                    CostCoins = actionType == "USE_RAILWAY" ? 1 : 0,
                    CoinsAfter = coinsAfter,
                    MovementOutcome = "MOVED"
                },
                PlayerViewState = responseView,
                NextAvailableActions = Clone(AvailableActions)
            }, "success", 200);
        }

        public ApiResult<GameActionResponseDto> BuildForestPending(
            long version,
            long fromCellId,
            long requestedTargetCellId,
            int speedAfter)
        {
            CurrentPlayer.CurrentSpeedPoints = speedAfter;
            View.StateVersion = version;
            Pending = ForestPending(version, 0, fromCellId, requestedTargetCellId);
            View.EngineState!.PendingCheck = Clone(Pending);
            AvailableActions = [];
            return ApiResult<GameActionResponseDto>.Success(new GameActionResponseDto
            {
                GameId = 77,
                OldStateVersion = version - 1,
                NewStateVersion = version,
                ActionType = "MOVE_ONE_STEP",
                ResultType = "CHECK_ROLL_PENDING",
                Movement = null,
                PendingCheck = Clone(Pending),
                PlayerViewState = Clone(View),
                NextAvailableActions = []
            }, "success", 200);
        }

        public ApiResult<CheckRollResponseDto> BuildForestFinal(
            int submittedDice,
            int effectiveDice,
            long actualTargetCellId,
            string outcome,
            string? stayReason,
            bool includeV2CostFields)
        {
            var from = Pending?.Preparation?.FromCellId ?? CurrentPlayer.CurrentCellId!.Value;
            var requested = Pending?.Preparation?.RequestedTargetCellId ?? ForestRequestedTargetCellId;
            CurrentPlayer.CurrentCellId = actualTargetCellId;
            View.StateVersion = Math.Max(View.StateVersion + 1, 7);
            View.EngineState!.PendingCheck = null;
            AvailableActions = [];
            var completed = Clone(Pending) ?? ForestPending(View.StateVersion, 1, from, requested);
            completed.Status = "COMPLETED";
            completed.StateVersion = View.StateVersion;
            completed.CanConfirm = false;
            completed.CanReroll = false;
            Pending = null;
            return ApiResult<CheckRollResponseDto>.Success(new CheckRollResponseDto
            {
                PendingCheck = completed,
                SubmittedDice = submittedDice,
                EffectiveDice = effectiveDice,
                CheckResult = null,
                Movement = new GameMovementResultDto
                {
                    FromCellId = from,
                    FromCellCode = "B19",
                    FromCellName = "泽可拉小屋",
                    RequestedTargetCellId = requested,
                    RequestedTargetCellCode = "B20",
                    RequestedTargetCellName = "无尽之森",
                    ActualTargetCellId = actualTargetCellId,
                    ActualTargetCellCode = outcome == "STAYED" ? "B19" : "B20",
                    ActualTargetCellName = outcome == "STAYED" ? "泽可拉小屋" : "无尽之森",
                    CostSpeed = 1,
                    RemainingSpeed = CurrentPlayer.CurrentSpeedPoints,
                    CostCoins = includeV2CostFields ? 0 : null,
                    CoinsAfter = includeV2CostFields ? CurrentPlayer.Coins : null,
                    MovementOutcome = outcome,
                    StayReason = stayReason
                },
                PlayerViewState = Clone(View),
                PlayerFacingMessage = outcome == "STAYED" ? "偏移方向无法通行，原地停留。" : "森林移动完成。"
            }, "success", 200);
        }

        public MapCellDto Cell(long cellId, bool requiresRoll, int costCoins = 0)
        {
            var source = Map.Cells!.Single(cell => cell.CellId == cellId);
            return new MapCellDto
            {
                CellId = source.CellId,
                CellCode = source.CellCode,
                BoardCode = source.BoardCode,
                CellName = source.CellName,
                CellNameText = source.CellNameText,
                Q = source.Q,
                R = source.R,
                InteractionTags = source.InteractionTags == null ? null : [.. source.InteractionTags],
                CostSpeed = 1,
                CostCoins = costCoins,
                RequiresRoll = requiresRoll
            };
        }

        private static PendingCheckDto ForestPending(long version, int round, long from, long requested) => new()
        {
            CheckId = 501,
            GameId = 77,
            PlayerId = 1001,
            BusinessType = "FOREST_MOVE",
            BusinessSourceId = from,
            Status = "WAITING_ROLL",
            StateVersion = version,
            AuthorizationRound = round,
            CanConfirm = true,
            CanReroll = true,
            NextRerollSocialCost = 0,
            FixedDice = 6,
            ExpiresAt = DateTimeOffset.Now.AddMinutes(2).ToString("O"),
            Preparation = new PendingCheckPreparationDto
            {
                FromCellId = from,
                RequestedTargetCellId = requested,
                FromQ = 0,
                FromR = 0,
                TargetQ = 1,
                TargetR = 0,
                DirectionIndex = 0,
                CostSpeed = 1,
                CostCoins = 0,
                SourceId = from,
                CellId = from,
                SocialCost = 0,
                BusinessType = "FOREST_MOVE",
                OperationId = Guid.NewGuid().ToString("D")
            }
        };

        private static void ValidateGameAction(GameActionRequestDto? request)
        {
            if (request == null || !Guid.TryParseExact(request.RequestId, "D", out _)
                || request.PlayerId != 1001 || request.StateVersion <= 0)
                throw new InvalidOperationException("移动 v2 请求身份不完整。");
        }

        private static List<AvailableActionDto> Actions(bool move, bool railway)
        {
            var result = new List<AvailableActionDto>();
            if (move) result.Add(new AvailableActionDto { ActionType = "MOVE_ONE_STEP", Available = true });
            result.Add(new AvailableActionDto
            {
                ActionType = "USE_RAILWAY",
                Available = railway,
                Reason = railway ? null : "RESOURCE_NOT_ENOUGH",
                ReasonText = railway ? null : "金币不足"
            });
            return result;
        }

        private static PlayerViewPlayerDto Player(long playerId, long userId, string name, int order, long cell, int speed, int coins) => new()
        {
            PlayerId = playerId,
            UserId = userId,
            Nickname = name,
            TurnOrder = order,
            CurrentCellId = cell,
            CurrentSpeedPoints = speed,
            CurrentSocialPoints = 3,
            Coins = coins,
            IsConnected = 1,
            EffectiveAttributes = new EffectiveAttributesDto()
        };

        private static GameConnectionPolicyDto ConnectionPolicy() => new()
        {
            HeartbeatSeconds = 5,
            TimeoutSeconds = 15,
            ReconnectWaitSeconds = 10,
            InitialGraceSeconds = 45
        };

        private static GameConnectionInfoDto ConnectionInfo(PlayerConnectionRequestDto request) => new()
        {
            ClientInstanceId = request.ClientInstanceId,
            ConnectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? "movement-v2-connection" : request.ConnectionId,
            ConnectionActive = true,
            PlayerConnected = true,
            HeartbeatSeconds = 5,
            TimeoutSeconds = 15,
            ReconnectWaitSeconds = 10,
            InitialGraceSeconds = 45,
            ExpiresAt = DateTimeOffset.Now.AddSeconds(15).ToString("O")
        };

        private static T? Clone<T>(T? value)
        {
            if (value == null) return default;
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        }

        private static ApiResult<T> Cast<T, TSource>(ApiResult<TSource> source) =>
            (ApiResult<T>)(object)source;
    }
}
