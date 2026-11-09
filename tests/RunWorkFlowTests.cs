using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Dice;
using EquestriaStar.Game.Map;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunWorkFlowTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTests).CallDeferred();
    }

    private void BeginTests() => _ = RunTestsAsync();

    private async Task RunTestsAsync()
    {
        VerifyDtoSerialization();
        var session = Root.GetNode<Session>("Session");
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new WorkTransport();
        apiClient.SetTransportForTests(transport);
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "work-flow-token",
            User = new UserDto { UserId = 1, Username = "gpt1", Nickname = "GPT1" }
        });
        session.SetCurrentRoom(10, "打工测试房");
        session.SetCurrentGame(77);

        var screen = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn").Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        await WaitUntilAsync(() => screen.IsGameConnectionReady
            && screen.LatestPlayerView != null
            && !screen.IsAvailableActionsRequestInProgress
            && !screen.Hud.WorkDisabled);
        GD.Print("WORK_TEST_PHASE=ready");
        screen.GetNode<Timer>("PlayerViewTimer").Stop();

        AssertTrue(screen.Hud.ActionMenuButton.IsInGroup("blocks_map_input"), "行为按钮必须阻止地图输入");
        AssertTrue(screen.Hud.WorkButton.GetParent().GetParent().GetParent().IsInGroup("blocks_map_input"), "行为菜单实际面板必须阻止地图输入");
        AssertFalse(screen.WorkDiceOverlay.IsOpen, "普通进入游戏且 pending=null 不得无故打开投骰层");

        await DispatchViewportClickAsync(screen.Hud.ActionMenuButton);
        AssertTrue(screen.Hud.IsActionMenuOpen, "真实 GUI 点击应展开行为菜单");
        var actionBefore = transport.WorkActionRequestCount;
        await DispatchViewportClickAsync(screen.Hud.WorkButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        GD.Print("WORK_TEST_PHASE=fixed_pending");
        AssertEqual(transport.WorkActionRequestCount, actionBefore + 1, "打工按钮真实点击只应提交一次 WORK");
        AssertFalse(screen.Hud.IsActionMenuOpen, "选择打工后应收起行为菜单");
        AssertEqual(transport.LastWorkRequest?.PlayerId, 1001L, "WORK 必须使用局内 playerId");
        AssertEqual(transport.LastWorkRequest?.StateVersion, 5L, "WORK 必须使用最新权威版本");
        AssertTrue(Guid.TryParseExact(transport.LastWorkRequest?.RequestId, "D", out _), "WORK 必须携带 UUID requestId");
        AssertEqual(transport.LastWorkRequest?.Payload.Count, 1, "WORK payload 只能包含服务端返回的属性");
        AssertEqual(transport.LastWorkRequest?.Payload["attributeType"]?.GetValue<string>(), "MAGIC", "WORK 属性必须来自 work/current");
        AssertEqual(transport.RollRequestCount, 0, "物理骰停止前不得上传点数");

        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            GD.Print("WORK_VISUAL_CHECK_READY：拖动投骰，检查确认/重投按钮、固定骰盘视角与地图输入隔离。");
            return;
        }

        CompletePhysicalRoll(screen.WorkDiceOverlay);
        AssertEqual(screen.WorkDiceOverlay.State, WorkDiceOverlayState.AwaitingDecision, "骰子停止后应等待玩家确认或重投");
        AssertEqual(transport.RollRequestCount, 0, "显示物理点数时仍不得上传点数");
        AssertTrue(screen.WorkDiceOverlay.ConfirmEnabled, "canConfirm=true 时确认按钮应可用");
        AssertTrue(screen.WorkDiceOverlay.RerollEnabled, "canReroll=true 时重投按钮应可用");

        var firstPhysicalDice = screen.WorkDiceOverlay.PhysicalDice;
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.RerollButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        AssertEqual(transport.RerollRequestCount, 1, "重投按钮只应提交一次授权请求");
        AssertEqual(transport.LastRerollRequest?.AuthorizationRound, 0, "首次重投必须使用当前授权轮次");
        AssertEqual(transport.LastRerollRequest?.StateVersion, 6L, "重投必须使用 pendingCheck 版本");
        AssertEqual(screen.WorkDiceOverlay.PhysicalDice, 0, "重投授权成功后旧物理点数必须失效");
        AssertTrue(firstPhysicalDice is >= 1 and <= 6, "首次物理骰应得到有效点数");

        CompletePhysicalRoll(screen.WorkDiceOverlay);
        var finalDice = screen.WorkDiceOverlay.PhysicalDice;
        transport.FailNextRollWithTimeout = true;
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.ConfirmButton);
        await WaitUntilAsync(() => !screen.IsCheckSubmitting
            && screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingDecision);
        AssertEqual(transport.RollRequestCount, 1, "确认超时后不得自动重发结算请求");
        var timedOutRequestId = transport.RollRequestIds.Single();
        AssertEqual(transport.LastRollRequest?.Dice, finalDice, "最终确认必须提交最新物理点数");
        await DelayFramesAsync(4);
        AssertEqual(transport.RollRequestCount, 1, "等待期间不得创建新 requestId 或自动重试");

        await DispatchViewportClickAsync(screen.WorkDiceOverlay.ConfirmButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.Result);
        GD.Print("WORK_TEST_PHASE=fixed_result");
        AssertEqual(transport.RollRequestCount, 2, "玩家明确重试后才允许再次发送原确认请求");
        AssertTrue(transport.RollRequestIds.All(id => id == timedOutRequestId), "同一次确认重放必须复用原 requestId");
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("打工成功"), "结果页必须按 checkResult.success 显示成败");
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("本次收入：4 金币"), "结果页必须使用 workResult.rewardCoins");
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("服务端有效点数：6"), "固定点数时必须展示服务端 effectiveDice");
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("金币余额：12"), "结果页必须使用 coinsAfter");
        AssertTrue(screen.WorkDiceOverlay.ResultText.Contains("社交余额：2"), "结果页必须使用 remainingSocial");

        await DispatchViewportClickAsync(new Vector2(30, 30));
        await WaitUntilAsync(() => !screen.WorkDiceOverlay.IsOpen);
        AssertTrue(screen.IsGameConnectionReady, "独立投骰期间连接租约必须保持活动");
        AssertTrue(transport.ConnectionRequestCount >= 1, "流程必须先建立对局连接");

        transport.WorkAvailable = false;
        await screen.RefreshAvailableActionsAsync(true);
        AssertTrue(screen.Hud.WorkDisabled, "服务端 WORK.available=false 时打工必须置灰");
        var actionsBeforeQuestCheck = transport.WorkActionRequestCount;
        await DispatchViewportClickAsync(screen.Hud.ActionMenuButton);
        await DispatchViewportClickAsync(screen.Hud.GetNode<Button>(
            "HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/QuestCheckButton"));
        await WaitUntilAsync(() => screen.QuestCheckOverlay.IsOpen);
        AssertEqual(transport.WorkActionRequestCount, actionsBeforeQuestCheck, "打开判定列表不得提交 WORK 或其他业务写请求");
        screen.QuestCheckOverlay.CloseOverlay();
        await DelayFramesAsync(2);

        transport.WorkAvailable = true;
        await screen.RefreshAvailableActionsAsync(true);
        transport.CurrentWorkReturnsNull = true;
        var actionsBeforeNoWork = transport.WorkActionRequestCount;
        await screen.BeginWorkAsync();
        AssertEqual(transport.WorkActionRequestCount, actionsBeforeNoWork, "work/current data=null 时不得提交 WORK");
        AssertTrue(
            screen.LastPlayerViewError.Contains("没有可用的打工项目"),
            $"data=null 应显示无匹配工作而不是查询失败；实际={screen.LastPlayerViewError}"
        );

        transport.CurrentWorkReturnsNull = false;
        transport.WorkActionFailureCode = "ACTION_NOT_AVAILABLE";
        var actionsBeforeFailure = transport.WorkActionRequestCount;
        await screen.BeginWorkAsync();
        AssertEqual(transport.WorkActionRequestCount, actionsBeforeFailure + 1, "准备失败只能提交一次 WORK");
        AssertFalse(screen.WorkDiceOverlay.IsOpen, "打工准备失败不得进入投骰层");
        transport.WorkActionFailureCode = null;

        transport.SetPendingForRecovery(9);
        await screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        transport.FailPendingQuery = true;
        await screen.RefreshPendingWorkCheckAsync(true);
        AssertEqual(screen.WorkDiceOverlay.State, WorkDiceOverlayState.RecoveryError, "查询失败必须与 data=null 区分并保留重试入口");
        transport.FailPendingQuery = false;
        transport.ClearPending(10);
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.RecoveryRetryButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.Result);
        AssertTrue(
            screen.WorkDiceOverlay.ResultText.Contains("原判定已不再待处理，无法恢复详细结果"),
            "已知 WORK 不再 pending 时必须展示准确恢复边界"
        );
        await DispatchViewportClickAsync(new Vector2(30, 30));
        await WaitUntilAsync(() => !screen.WorkDiceOverlay.IsOpen);

        transport.ReturnIncompleteWorkResult = true;
        await screen.RefreshPlayerViewAsync();
        await screen.RefreshAvailableActionsAsync(true);
        await screen.BeginWorkAsync();
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        CompletePhysicalRoll(screen.WorkDiceOverlay);
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.ConfirmButton);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.Result);
        AssertFalse(screen.WorkDiceOverlay.ResultText.Contains("本次收入：0"), "缺失 workResult 不能伪装成失败 0 金币");
        AssertTrue(
            screen.WorkDiceOverlay.ResultText.Contains("原判定已不再待处理，无法恢复详细结果"),
            "完整结果缺失且 pending 已消失时必须明确无法恢复详细结果"
        );
        transport.ReturnIncompleteWorkResult = false;
        GD.Print("WORK_TEST_PHASE=legacy_cases_done");

        await DispatchViewportClickAsync(new Vector2(30, 30));
        await WaitUntilAsync(() => !screen.WorkDiceOverlay.IsOpen);
        await VerifyAnyAttributeSelectionAsync(screen, transport);
        GD.Print("WORK_TEST_PHASE=any_cases_done");

        screen.Stop();
        screen.QueueFree();
        apiClient.ClearTransportForTests();
        session.ClearAuth();
        await ToSignal(this, SignalName.ProcessFrame);
        Finish();
    }

    private static void VerifyDtoSerialization()
    {
        var reroll = JsonSerializer.Serialize(new CheckRerollRequestDto
        {
            RequestId = "reroll-id",
            StateVersion = 9,
            AuthorizationRound = 1
        });
        var roll = JsonSerializer.Serialize(new CheckRollRequestDto
        {
            RequestId = "roll-id",
            StateVersion = 10,
            AuthorizationRound = 2,
            Dice = 5
        });
        using var rerollJson = JsonDocument.Parse(reroll);
        using var rollJson = JsonDocument.Parse(roll);
        AssertStatic(rerollJson.RootElement.EnumerateObject().Count() == 3, "重投请求不得附加 dice 或业务字段");
        AssertStatic(!rerollJson.RootElement.TryGetProperty("dice", out _), "重投授权不得上传点数");
        AssertStatic(rollJson.RootElement.GetProperty("dice").GetInt32() == 5, "确认请求必须把 dice 序列化为 JSON 整数");
        AssertStatic(
            WorkAttributeSelectionModel.AttributeTypes.SequenceEqual(
                new[] { "STRENGTH", "SPEED", "SOCIAL", "CHARM", "MAGIC", "MIND" }),
            "ANY 打工必须只允许服务端定义的六个具体属性枚举");
        AssertStatic(!WorkAttributeSelectionModel.TryNormalizeSelectable("ANY", out _), "ANY 不能作为最终选择提交");
    }

    private async Task VerifyAnyAttributeSelectionAsync(GameScreen screen, WorkTransport transport)
    {
        GD.Print("WORK_TEST_ANY_PHASE=start");
        transport.CurrentWorkAttributeType = "ANY";
        transport.ExpectedWorkAttributeType = "STRENGTH";
        await screen.RefreshPlayerViewAsync();
        await screen.RefreshAvailableActionsAsync(true);

        var beforeCancel = transport.WorkActionRequestCount;
        await screen.BeginWorkAsync();
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.SelectingAttribute);
        AssertEqual(transport.WorkActionRequestCount, beforeCancel, "ANY 出现选择器前不得提交 WORK");
        AssertEqual(screen.WorkDiceOverlay.AttributeButtons.Count, 6, "ANY 选择器必须显示六个属性按钮");
        AssertTrue(screen.WorkDiceOverlay.GetNode<Control>("OverlayRoot").IsInGroup("blocks_map_input"),
            "属性选择遮罩必须阻止地图输入");
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.AttributeCancelButton);
        await WaitUntilAsync(() => !screen.WorkDiceOverlay.IsOpen);
        AssertEqual(transport.WorkActionRequestCount, beforeCancel, "取消 ANY 属性选择不得发送写请求");
        AssertFalse(screen.IsWorkActionInProgress, "取消属性选择后必须释放其他业务行动锁");
        GD.Print("WORK_TEST_ANY_PHASE=cancelled");

        // 新鲜资格失效时，选择点击只做同步和提示，不允许提交旧版本行动。
        await screen.BeginWorkAsync();
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.SelectingAttribute);
        transport.WorkAvailable = false;
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.AttributeButtons["STRENGTH"]);
        await WaitUntilAsync(() => !screen.IsWorkPreparing && !screen.WorkDiceOverlay.IsOpen);
        AssertEqual(transport.WorkActionRequestCount, beforeCancel, "资格失效后不得提交 WORK");
        transport.WorkAvailable = true;
        await screen.RefreshAvailableActionsAsync(true);
        GD.Print("WORK_TEST_ANY_PHASE=qualification_checked");

        transport.WorkActionFailureCode = "ACTION_NOT_AVAILABLE";
        var idsBefore = transport.WorkRequestIds.Count;
        foreach (var attribute in WorkAttributeSelectionModel.AttributeTypes)
        {
            GD.Print($"WORK_TEST_ANY_PHASE=attribute_{attribute}");
            transport.ExpectedWorkAttributeType = attribute;
            var before = transport.WorkActionRequestCount;
            await screen.BeginWorkAsync();
            await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.SelectingAttribute);
            await DispatchViewportClickAsync(screen.WorkDiceOverlay.AttributeButtons[attribute]);
            await DispatchViewportClickAsync(screen.WorkDiceOverlay.AttributeButtons[attribute]);
            await WaitUntilAsync(() => !screen.IsWorkPreparing && !screen.WorkDiceOverlay.IsOpen);
            AssertEqual(transport.WorkActionRequestCount, before + 1, $"{attribute} 快速重复点击只能提交一次 WORK");
            AssertEqual(
                transport.LastWorkRequest?.Payload["attributeType"]?.GetValue<string>(),
                attribute,
                $"{attribute} 必须使用准确服务端枚举");
        }
        var selectionIds = transport.WorkRequestIds.Skip(idsBefore).ToList();
        AssertEqual(selectionIds.Count, 6, "六个属性各自应形成一个新逻辑行动");
        AssertEqual(selectionIds.Distinct().Count(), 6, "不同属性逻辑行动必须使用不同 requestId");

        transport.WorkActionFailureCode = null;
        transport.CurrentWorkAttributeType = "UNKNOWN_ATTRIBUTE";
        var beforeUnknown = transport.WorkActionRequestCount;
        await screen.BeginWorkAsync();
        await WaitUntilAsync(() => !screen.IsWorkPreparing);
        AssertEqual(transport.WorkActionRequestCount, beforeUnknown, "未知打工属性不得回退或提交");
        AssertTrue(screen.LastPlayerViewError.Contains("未知打工属性"), "未知属性必须显示明确错误");
        GD.Print("WORK_TEST_ANY_PHASE=unknown_checked");

        transport.CurrentWorkAttributeType = "ANY";
        transport.ExpectedWorkAttributeType = "MIND";
        await screen.BeginWorkAsync();
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.SelectingAttribute);
        await DispatchViewportClickAsync(screen.WorkDiceOverlay.AttributeButtons["MIND"]);
        await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
        AssertEqual(transport.LastWorkRequest?.Payload["attributeType"]?.GetValue<string>(), "MIND", "ANY 成功准备不得提交 ANY");
        AssertEqual(screen.WorkDiceOverlay.CurrentBusinessType, "WORK", "ANY 成功准备后必须进入既有 WORK 投骰流程");
    }

    private static void CompletePhysicalRoll(WorkDiceOverlay overlay)
    {
        if (overlay.Dice.RollState != DiceRollState.AwaitingDrag)
        {
            throw new InvalidOperationException("物理骰尚未进入 AwaitingDrag。" );
        }
        var thrown = overlay.Dice.TryThrow(new Vector2(360, -180), 0.24, overlay.Controller.Camera);
        if (!thrown) throw new InvalidOperationException("测试物理骰未成功投出。" );
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

    private async Task DelayFramesAsync(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task WaitUntilAsync(Func<bool> condition, int frames = 900)
    {
        for (var i = 0; i < frames; i++)
        {
            if (condition()) return;
            await ToSignal(this, SignalName.ProcessFrame);
        }
        _failures.Add("等待条件超时。");
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("WORK_FLOW_TESTS_PASSED");
            Quit(0);
            return;
        }
        foreach (var failure in _failures) GD.PushError(failure);
        Quit(1);
    }

    private void AssertTrue(bool condition, string message)
    {
        if (!condition) _failures.Add(message);
    }

    private void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            _failures.Add($"{message}；期望={expected}，实际={actual}");
    }

    private static void AssertStatic(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class WorkTransport : IApiTransport
    {
        public GameMapDto Map { get; } = RunGameMapTests.CreateValidMap();
        public PlayerViewResponseDto View { get; private set; }
        public PendingCheckDto? Pending { get; private set; }
        public int ConnectionRequestCount { get; private set; }
        public int WorkActionRequestCount { get; private set; }
        public int RerollRequestCount { get; private set; }
        public int RollRequestCount { get; private set; }
        public bool FailNextRollWithTimeout { get; set; }
        public bool WorkAvailable { get; set; } = true;
        public bool CurrentWorkReturnsNull { get; set; }
        public bool FailPendingQuery { get; set; }
        public bool ReturnIncompleteWorkResult { get; set; }
        public string CurrentWorkAttributeType { get; set; } = "MAGIC";
        public string ExpectedWorkAttributeType { get; set; } = "MAGIC";
        public string? WorkActionFailureCode { get; set; }
        public GameActionRequestDto? LastWorkRequest { get; private set; }
        public CheckRerollRequestDto? LastRerollRequest { get; private set; }
        public CheckRollRequestDto? LastRollRequest { get; private set; }
        public List<string> RollRequestIds { get; } = [];
        public List<string> WorkRequestIds { get; } = [];

        public WorkTransport()
        {
            var cell = Map.Cells![0];
            View = new PlayerViewResponseDto
            {
                GameId = 77,
                MapConfigId = 1,
                GameStatus = "RUNNING",
                CurrentRound = 1,
                CurrentTurnUserId = 1,
                CurrentTurnSeq = 1,
                StateVersion = 5,
                EngineState = new EngineStateDto
                {
                    BatchId = "CORE_9_PUBLIC_SHOP",
                    ConnectionPolicy = NewPolicy()
                },
                Players =
                [
                    new PlayerViewPlayerDto
                    {
                        PlayerId = 1001, UserId = 1, Nickname = "GPT1", TurnOrder = 1,
                        CurrentCellId = cell.CellId, CurrentCellCode = cell.CellCode,
                        CurrentCellName = cell.CellName, CurrentCellNameText = cell.CellNameText,
                        CurrentSpeedPoints = 3, CurrentSocialPoints = 4, Coins = 8,
                        EffectiveAttributes = new EffectiveAttributesDto { Magic = 3 }
                    },
                    new PlayerViewPlayerDto { PlayerId = 1002, UserId = 2, Nickname = "GPT2", TurnOrder = 2 }
                ]
            };
        }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            await Task.Delay(1);
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
                return Cast<T, GameMapDto>(ApiResult<GameMapDto>.Success(Map, "success", 200));

            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
                return Cast<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(Clone(View), "success", 200));

            if (path == "/games/77/players/1001/connection" && typeof(T) == typeof(PlayerViewResponseDto))
            {
                ConnectionRequestCount++;
                var response = Clone(View)!;
                response.EngineState ??= new EngineStateDto();
                response.EngineState.ConnectionPolicy = NewPolicy();
                response.EngineState.Connection = new GameConnectionInfoDto
                {
                    ClientInstanceId = (body as PlayerConnectionRequestDto)?.ClientInstanceId,
                    ConnectionId = (body as PlayerConnectionRequestDto)?.ConnectionId ?? "work-connection",
                    ConnectionActive = true,
                    PlayerConnected = true,
                    HeartbeatSeconds = 5,
                    TimeoutSeconds = 15
                };
                return Cast<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(response, "success", 200));
            }

            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
            {
                var workAvailable = WorkAvailable && Pending == null;
                return Cast<T, List<AvailableActionDto>>(ApiResult<List<AvailableActionDto>>.Success(
                    [
                        new AvailableActionDto { ActionType = "END_TURN", Available = workAvailable },
                        new AvailableActionDto { ActionType = "MOVE_ONE_STEP", Available = workAvailable },
                        new AvailableActionDto { ActionType = "WORK", Available = workAvailable, ReasonText = workAvailable ? null : "已有待判定" }
                    ], "success", 200));
            }

            if (path == "/games/77/work/current" && typeof(T) == typeof(CurrentWorkResponseDto))
            {
                AssertQueryPlayer(query);
                if (CurrentWorkReturnsNull)
                    return Cast<T, CurrentWorkResponseDto>(ApiResult<CurrentWorkResponseDto>.Success(null, "success", 200));
                return Cast<T, CurrentWorkResponseDto>(ApiResult<CurrentWorkResponseDto>.Success(new CurrentWorkResponseDto
                {
                    CellId = View.Players![0].CurrentCellId,
                    AttributeType = CurrentWorkAttributeType,
                    AttributeTypeText = WorkAttributeSelectionModel.TryGetLabel(CurrentWorkAttributeType, out var label)
                        ? label
                        : CurrentWorkAttributeType,
                    Difficulty = 7,
                    SocialCost = 2,
                    RewardCoins = 3
                }, "success", 200));
            }

            if (path == "/games/77/actions" && typeof(T) == typeof(GameActionResponseDto))
            {
                LastWorkRequest = body as GameActionRequestDto;
                ValidateWorkRequest(LastWorkRequest);
                WorkActionRequestCount++;
                WorkRequestIds.Add(LastWorkRequest!.RequestId);
                if (!string.IsNullOrWhiteSpace(WorkActionFailureCode))
                    return ApiResult<T>.Failure("模拟打工准备失败", WorkActionFailureCode);
                var oldVersion = LastWorkRequest!.StateVersion;
                var pendingVersion = oldVersion + 1;
                Pending = NewPending(pendingVersion, 0, true, ExpectedWorkAttributeType);
                UpdateViewPending(Pending, pendingVersion);
                return Cast<T, GameActionResponseDto>(ApiResult<GameActionResponseDto>.Success(new GameActionResponseDto
                {
                    GameId = 77,
                    OldStateVersion = oldVersion,
                    NewStateVersion = pendingVersion,
                    ActionType = "WORK",
                    ResultType = "CHECK_ROLL_PENDING",
                    PendingCheck = Clone(Pending),
                    PlayerViewState = Clone(View)
                }, "success", 200));
            }

            if (path == "/games/77/checks/pending" && typeof(T) == typeof(PendingCheckDto))
            {
                if (FailPendingQuery)
                    return ApiResult<T>.Failure("模拟待判定查询失败", "NETWORK_ERROR");
                return Cast<T, PendingCheckDto>(ApiResult<PendingCheckDto>.Success(Clone(Pending), "success", 200));
            }

            if (path == "/games/77/checks/501/reroll" && typeof(T) == typeof(PendingCheckDto))
            {
                LastRerollRequest = body as CheckRerollRequestDto;
                ValidateRerollRequest(LastRerollRequest, Pending);
                RerollRequestCount++;
                var nextVersion = Pending!.StateVersion!.Value + 1;
                var nextRound = Pending.AuthorizationRound!.Value + 1;
                Pending = NewPending(nextVersion, nextRound, false, Pending.AttributeType ?? "MAGIC");
                UpdateViewPending(Pending, nextVersion);
                return Cast<T, PendingCheckDto>(ApiResult<PendingCheckDto>.Success(Clone(Pending), "success", 200));
            }

            if (path == "/games/77/checks/501/roll" && typeof(T) == typeof(CheckRollResponseDto))
            {
                LastRollRequest = body as CheckRollRequestDto;
                ValidateRollRequest(LastRollRequest, Pending);
                RollRequestCount++;
                RollRequestIds.Add(LastRollRequest!.RequestId);
                if (FailNextRollWithTimeout)
                {
                    FailNextRollWithTimeout = false;
                    return ApiResult<T>.Failure("模拟确认超时", "REQUEST_TIMEOUT");
                }
                var submitted = LastRollRequest.Dice;
                var completed = Clone(Pending)!;
                completed.Status = "COMPLETED";
                completed.CanConfirm = false;
                completed.CanReroll = false;
                var completedVersion = Pending!.StateVersion!.Value + 1;
                Pending = null;
                UpdateViewPending(null, completedVersion);
                View.Players![0].Coins = 12;
                View.Players[0].CurrentSocialPoints = 2;
                var check = new CheckResultDto
                {
                    Dice = 6, AttributeType = "MAGIC", AttributeTypeText = "魔法",
                    AttributeValue = 3, Modifier = 0, FinalValue = 9, Difficulty = 7, Success = true
                };
                var response = new CheckRollResponseDto
                {
                    PendingCheck = completed,
                    SubmittedDice = submitted,
                    EffectiveDice = 6,
                    CheckResult = check,
                    WorkResult = ReturnIncompleteWorkResult ? null : new WorkResultDto
                    {
                        SocialCost = 2, RemainingSocial = 2, RewardCoins = 4, CoinsAfter = 12,
                        CheckLog = check, ForceEndTurn = false
                    },
                    PlayerViewState = Clone(View),
                    PlayerFacingMessage = "打工判定成功。",
                    DeckDraws = []
                };
                return Cast<T, CheckRollResponseDto>(ApiResult<CheckRollResponseDto>.Success(response, "success", 200));
            }

            return ApiResult<T>.Failure($"严格模拟未定义请求：{method} {path}", "UNEXPECTED_REQUEST");
        }

        private void UpdateViewPending(PendingCheckDto? pending, long version)
        {
            View.StateVersion = version;
            View.EngineState ??= new EngineStateDto();
            View.EngineState.PendingCheck = pending;
        }

        public void SetPendingForRecovery(long version)
        {
            Pending = NewPending(version, 0, false);
            UpdateViewPending(Pending, version);
        }

        public void ClearPending(long version)
        {
            Pending = null;
            UpdateViewPending(null, version);
        }

        private static PendingCheckDto NewPending(long version, int round, bool canReroll, string attributeType = "MAGIC") => new()
        {
            CheckId = 501,
            GameId = 77,
            PlayerId = 1001,
            BusinessType = "WORK",
            Status = "WAITING_ROLL",
            AttributeType = attributeType,
            AttributeValue = 3,
            Difficulty = 7,
            SocialCost = 2,
            RemainingSocial = 2,
            StateVersion = version,
            AuthorizationRound = round,
            CanConfirm = true,
            CanReroll = canReroll,
            NextRerollSocialCost = canReroll ? 0 : null,
            RerollUnavailableReason = canReroll ? null : "本回合重投机会已用尽",
            ExpiresAt = DateTimeOffset.Now.AddMinutes(2).ToString("O")
        };

        private static GameConnectionPolicyDto NewPolicy() => new()
        {
            HeartbeatSeconds = 5,
            TimeoutSeconds = 15,
            ReconnectWaitSeconds = 10,
            InitialGraceSeconds = 45
        };

        private void ValidateWorkRequest(GameActionRequestDto? request)
        {
            if (request == null || request.ActionType != "WORK" || request.PlayerId != 1001
                || request.StateVersion <= 0 || !Guid.TryParseExact(request.RequestId, "D", out _)
                || request.Payload.Count != 1
                || request.Payload["attributeType"]?.GetValue<string>() != ExpectedWorkAttributeType
                || request.Payload["attributeType"]?.GetValue<string>() == "ANY")
                throw new InvalidOperationException("WORK 请求不符合严格契约。" );
        }

        private static void ValidateRerollRequest(CheckRerollRequestDto? request, PendingCheckDto? pending)
        {
            if (request == null || pending == null
                || request.StateVersion != pending.StateVersion
                || request.AuthorizationRound != pending.AuthorizationRound
                || !Guid.TryParseExact(request.RequestId, "D", out _))
                throw new InvalidOperationException("重投授权请求不符合严格契约。" );
        }

        private static void ValidateRollRequest(CheckRollRequestDto? request, PendingCheckDto? pending)
        {
            if (request == null || pending == null
                || request.StateVersion != pending.StateVersion
                || request.AuthorizationRound != pending.AuthorizationRound
                || request.Dice is < 1 or > 6 || !Guid.TryParseExact(request.RequestId, "D", out _))
                throw new InvalidOperationException("最终确认请求不符合严格契约。" );
        }

        private static void AssertQueryPlayer(IReadOnlyDictionary<string, string?>? query)
        {
            if (query == null || !query.TryGetValue("playerId", out var playerId) || playerId != "1001")
                throw new InvalidOperationException("work/current 必须使用局内 playerId。" );
        }

        private static ApiResult<TTarget> Cast<TTarget, TSource>(ApiResult<TSource> result) =>
            (ApiResult<TTarget>)(object)result;

        private static T? Clone<T>(T? value)
        {
            if (value == null) return default;
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        }
    }
}
