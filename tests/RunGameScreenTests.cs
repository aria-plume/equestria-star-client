using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;
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
        VerifyGameActionRequestSerialization();
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
        var visualCheck = OS.GetCmdlineUserArgs().Contains("--visual-check");
        if (!visualCheck)
        {
            screen.GetNode<Timer>("PlayerViewTimer").Stop();
        }

        AssertEqual(screen.GameId, 77L, "GameScreen 必须使用 Session.CurrentGameId");
        AssertEqual(screen.MapScene.GameId, 77L, "地图场景必须先取得 gameId");
        AssertFalse(screen.MapScene.AutoLoadOnReady, "正式地图不得在 Ready 时抢先加载");
        AssertTrue(screen.MapScene.RequireGameId, "正式地图必须禁止主地图回退");
        AssertEqual(screen.MapScene.GeneratedCellCount, 138, "正式场景应生成全部地图格子");
        AssertEqual(screen.MapScene.Edges.Count, 385, "正式场景应保留全部地图边");
        AssertTrue(screen.MapScene.IsUsingStaticVisual, "正式场景必须挂载静态 PCK 视觉");
        AssertEqual(screen.MapScene.StaticVisualCellCount, 138, "静态视觉必须包含 138 个格子");
        AssertEqual(screen.MapScene.StaticVisualRoot.GetChildCount(), 1, "正式场景只能保留一个静态地图实例");
        AssertEqual(screen.MapScene.InteractionRoot.GetChildCount(), 1, "正式场景只能保留一个交互层实例");
        AssertTrue(screen.MapScene.VisualRuntime.Diagnostics != null, "正式场景必须附加 P9 视觉运行层");
        AssertFalse(screen.MapScene.HasNode("MapEnvironmentRoot"), "正式场景不得保留旧环境根");
        AssertEqual(screen.Hud.BoundCurrentUserId, 1L, "HUD 应按当前登录 userId 识别自己");
        AssertEqual(screen.Hud.OtherPlayerCount, 1, "HUD 应仅显示另一名玩家");
        AssertEqual(transport.MapRequestCount, 1, "重复 Start 不得重复加载地图");
        AssertEqual(transport.PlayerViewRequestCount, 2, "首次建连后应额外同步一次玩家视图，重复 Start 不得新增流程");
        AssertEqual(transport.ConnectionRequestCount, 1, "重复 Start 不得创建第二套连接租约");
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
        VerifyFairytaleHudLayout(screen);
        VerifyTurnHudStates(screen, transport);

        // 可视检查模式保留正式组合场景，便于人工验证地图与 HUD。
        if (visualCheck)
        {
            transport.AdvanceOtherPlayerStatsOnPoll = true;
            transport.ActionRequestCallback = () =>
            {
                transport.View.StateVersion++;
                transport.View.CurrentTurnUserId = 2;
                transport.SetEndTurnAvailability(false, "当前不是你的回合");
                GD.Print($"VISUAL_END_TURN_REQUEST count={transport.GameActionRequestCount}");
            };
            var camera = screen.MapScene.CameraController.Camera;
            var townHallCell = screen.MapScene.CellsById.Values.Single(cell => cell.Data?.CellCode == "A10");
            GD.Print($"VISUAL_A10_SCREEN={camera.UnprojectPosition(townHallCell.GlobalPosition)}");
            foreach (var token in screen.MapScene.TokenManager.TokensByPlayerId.Values.OrderBy(token => token.PlayerId))
            {
                GD.Print($"VISUAL_TOKEN_{token.PlayerId}_SCREEN={camera.UnprojectPosition(token.GlobalPosition)}");
            }
            GD.Print("正式游戏场景可视检查已就绪。");
            return;
        }

        await VerifyPollingAndRefreshAsync(screen, transport);
        await VerifyEndTurnSuccessAndVersionGuardAsync(screen, transport);
        await VerifyEndTurnErrorsAsync(screen, transport);
        await VerifyAllOfflineAndHeartbeatVersionGuardAsync(screen, transport);
        await VerifyFailureAndRetryAsync(screen, transport);
        await VerifyFreedScreenIgnoresResponseAsync(screen, transport);

        apiClient.ClearTransportForTests();
        session.ClearAuth();
        Finish();
    }

    private void VerifyGameActionRequestSerialization()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var json = JsonSerializer.Serialize(new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = 1001,
            StateVersion = 9,
            ActionType = "END_TURN",
            Payload = new JsonObject()
        });
        using var document = JsonDocument.Parse(json);
        AssertEqual(
            document.RootElement.GetProperty("requestId").GetString(),
            requestId,
            "GameActionRequestDto 必须把 requestId 序列化到请求体顶层"
        );
    }

    private void VerifyTurnHudStates(GameScreen screen, GameScreenTransport transport)
    {
        var ownTurnView = GameScreenTransport.CloneView(transport.View);
        ownTurnView.EngineState ??= new EngineStateDto();
        ownTurnView.EngineState.Phase = "MIDDLE";
        screen.Hud.RefreshPlayerView(ownTurnView, 1);
        screen.Hud.SetAvailableActions(
            [new AvailableActionDto { ActionType = "END_TURN", Available = true }],
            ownTurnView.StateVersion
        );
        AssertTrue(screen.Hud.EndTurnVisible, "自己回合应显示结束回合按钮");
        AssertFalse(screen.Hud.EndTurnDisabled, "END_TURN 可用时按钮应恢复可点击");

        screen.Hud.ActionMenuButton.EmitSignal(Button.SignalName.Pressed);
        AssertTrue(screen.Hud.IsActionMenuOpen, "回合状态切换测试期间行为菜单应保持展开");

        var restrictedPhaseView = GameScreenTransport.CloneView(ownTurnView);
        restrictedPhaseView.StateVersion = ownTurnView.StateVersion + 1;
        restrictedPhaseView.EngineState!.Phase = "TURN_START";
        screen.Hud.RefreshPlayerView(restrictedPhaseView, 1);
        AssertEqual(screen.Hud.TurnNoticeText, "当前是你的回合", "阶段受限时仍必须刷新回合横幅");
        AssertTrue(screen.Hud.EndTurnVisible && screen.Hud.EndTurnDisabled,
            "阶段受限时不得沿用上一帧可点击的结束回合状态");
        AssertTrue(screen.Hud.EndTurnTooltip.Contains("阶段", System.StringComparison.Ordinal),
            "阶段受限时应提供明确原因");
        AssertTrue(screen.Hud.IsActionMenuOpen, "阶段更新不应收起已展开的行为菜单");

        var pendingRewardView = GameScreenTransport.CloneView(ownTurnView);
        pendingRewardView.StateVersion = ownTurnView.StateVersion + 2;
        pendingRewardView.EngineState!.PendingReward = new PendingRewardDto
        {
            RewardId = 9001,
            Kind = "ITEM",
            PlayerId = 1001
        };
        screen.Hud.RefreshPlayerView(pendingRewardView, 1);
        AssertEqual(screen.Hud.TurnNoticeText, "当前是你的回合", "出现奖励队首时仍必须刷新回合横幅");
        AssertTrue(screen.Hud.EndTurnVisible && screen.Hud.EndTurnDisabled,
            "奖励队首存在时不得沿用旧的结束回合资格");
        AssertTrue(screen.Hud.EndTurnTooltip.Contains("奖励", System.StringComparison.Ordinal),
            "奖励队首应显示对应禁用原因");

        var otherTurnView = GameScreenTransport.CloneView(ownTurnView);
        otherTurnView.StateVersion = ownTurnView.StateVersion + 3;
        otherTurnView.CurrentTurnUserId = 2;
        screen.Hud.RefreshPlayerView(otherTurnView, 1);
        AssertEqual(screen.Hud.TurnNoticeText, "当前是 GPT2 的回合", "他人回合时应显示对应昵称");
        AssertFalse(screen.Hud.EndTurnVisible, "他人回合不得显示结束回合按钮");
        AssertTrue(screen.Hud.IsActionMenuOpen, "他人回合更新不应收起行为菜单");

        var restoredView = GameScreenTransport.CloneView(ownTurnView);
        restoredView.StateVersion = ownTurnView.StateVersion + 4;
        restoredView.EngineState!.Phase = "MIDDLE";
        restoredView.EngineState.PendingReward = null;
        screen.Hud.RefreshPlayerView(restoredView, 1);
        screen.Hud.SetAvailableActions(
            [new AvailableActionDto { ActionType = "END_TURN", Available = true }],
            restoredView.StateVersion
        );
        AssertEqual(screen.Hud.TurnNoticeText, "当前是你的回合", "恢复可行动时应恢复本人回合提示");
        AssertTrue(screen.Hud.EndTurnVisible && !screen.Hud.EndTurnDisabled,
            "恢复可行动且最新 END_TURN 可用时应恢复按钮");
        AssertTrue(screen.Hud.IsActionMenuOpen, "恢复资格时行为菜单应保持展开");
        screen.Hud.CloseActionMenu();

        // 状态转换用例只检查 HUD，结束时恢复 GameScreen 当前的权威版本。
        screen.Hud.RefreshPlayerView(GameScreenTransport.CloneView(transport.View), 1);
        screen.Hud.SetAvailableActions(transport.AvailableActions, transport.View.StateVersion);
    }

    private void VerifyMapInputRoutingMetadata(GameScreen screen)
    {
        AssertEqual(screen.MouseFilter, Control.MouseFilterEnum.Ignore, "GameScreen 全屏布局必须忽略鼠标命中");
        var hudRoot = screen.Hud.GetNode<Control>("HudRoot");
        AssertEqual(hudRoot.MouseFilter, Control.MouseFilterEnum.Ignore, "HudRoot 透明覆盖必须忽略鼠标命中");

        var otherPlayersPanel = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel");
        var currentPlayerPanel = screen.Hud.GetNode<Control>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel");
        var inventoryButton = screen.Hud.GetNode<Control>("HudRoot/BottomActionsAnchor/Buttons/ItemsButton");
        var actionMenuPanel = screen.Hud.GetNode<Control>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel");
        var turnNoticeLabel = screen.Hud.GetNode<Label>(
            "HudRoot/TurnNoticeAnchor/TurnNoticePanel/TurnNoticeLabel"
        );
        var otherPlayersScroll = screen.Hud.GetNode<ScrollContainer>(
            "HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll");
        var avatarBlocker = screen.Hud.GetNode<Control>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/AvatarInputBlocker");
        var statusBlocker = screen.Hud.GetNode<Control>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/StatusInputBlocker");
        AssertFalse(MapCameraController.IsMapInputBlocked(otherPlayersPanel),
            "其他玩家的大矩形布局父节点不得整体拦截地图输入");
        AssertTrue(otherPlayersScroll.IsInGroup(MapCameraController.MapInputBlockerGroup),
            "实际可滚动的玩家列表区必须拦截地图输入");
        AssertTrue(otherPlayersScroll.Size.Y <= 77.0f + 0.5f,
            "单个其他玩家时滚动拦截区不得延伸到未填满空白");
        AssertFalse(MapCameraController.IsMapInputBlocked(currentPlayerPanel),
            "当前玩家的大矩形布局父节点不得整体拦截地图输入");
        AssertTrue(avatarBlocker.IsInGroup(MapCameraController.MapInputBlockerGroup),
            "当前玩家头像的实际可见区必须拦截地图输入");
        AssertTrue(statusBlocker.IsInGroup(MapCameraController.MapInputBlockerGroup),
            "当前玩家状态条的实际可见区必须拦截地图输入");
        AssertTrue(inventoryButton.IsInGroup(MapCameraController.MapInputBlockerGroup), "右下图标按钮必须标记为地图输入拦截区");
        AssertTrue(actionMenuPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "行为菜单必须拦截地图输入");
        AssertFalse(MapCameraController.IsMapInputBlocked(turnNoticeLabel), "顶部回合提示不得阻止地图输入");
        AssertTrue(MapCameraController.IsMapInputBlocked(
            screen.Hud.GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/EndTurnSlot/EndTurnButton")
        ), "结束回合按钮必须通过父节点标记阻止地图输入");
        AssertFalse(MapCameraController.IsMapInputBlocked(hudRoot), "HUD 透明根节点不得拦截地图输入");

        var playerEntry = screen.Hud
            .GetNode<VBoxContainer>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll/OtherPlayersList")
            .GetChildren()
            .OfType<Control>()
            .First(child => child != screen.Hud.GetNode<Label>(
                "HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll/OtherPlayersList/EmptyLabel"
            ));
        AssertTrue(playerEntry.IsInGroup(MapCameraController.MapInputBlockerGroup), "其他玩家条目必须标记为地图输入拦截区");

        var errorPanel = screen.GetNode<Control>("StatusLayer/HudErrorState/ErrorPanel");
        var fatalState = screen.GetNode<Control>("StatusLayer/FatalState");
        AssertTrue(errorPanel.IsInGroup(MapCameraController.MapInputBlockerGroup), "错误提示面板必须拦截地图输入");
        AssertTrue(fatalState.IsInGroup(MapCameraController.MapInputBlockerGroup), "致命错误遮罩必须拦截地图输入");
    }

    private void VerifyFairytaleHudLayout(GameScreen screen)
    {
        var iconButtons = new[]
        {
            screen.Hud.QuestBoardButton,
            screen.Hud.ShopShelfButton,
            screen.Hud.InventoryButton,
            screen.Hud.PersonalQuestsButton,
            screen.Hud.EquipmentButton,
            screen.Hud.PartnersButton,
            screen.Hud.ActionMenuButton
        };
        AssertTrue(iconButtons.All(button => button.Icon != null), "七个童话风入口必须绑定独立图标资源");
        AssertTrue(iconButtons.All(button => button.IsInGroup(MapCameraController.MapInputBlockerGroup)),
            "七个实际入口必须全部阻止地图输入");
        AssertTrue(screen.Hud.PartnersButton.Disabled, "伙伴入口必须保持禁用占位");
        AssertTrue(screen.Hud.GetNode<TextureRect>("HudRoot/TurnNoticeAnchor/TurnNoticePanel/BannerTexture").Texture != null,
            "顶部回合横幅必须使用独立童话风素材");
        AssertTrue(screen.Hud.GetNode<TextureRect>("HudRoot/TopRightAnchor/TopRight/MapIndicator/Texture").Texture != null,
            "右上正式地图标识必须使用独立底板素材");
        AssertFalse(screen.MapScene.GetNode<Control>("CanvasLayer/MapInfoMargin").Visible,
            "GameScreen 组合时必须隐藏地图场景旧标识，避免与童话 HUD 重叠");
        AssertTrue(screen.Hud.GetNode("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel").IsAncestorOf(screen.Hud.EndTurnButton),
            "结束回合必须只保留在行为菜单内");
        AssertFalse(screen.Hud.HasNode("HudRoot/EndTurnAnchor"), "旧的独立结束回合入口不得残留");
    }

    private async Task VerifyPollingAndRefreshAsync(GameScreen screen, GameScreenTransport transport)
    {
        var otherPlayersList = screen.Hud.GetNode<VBoxContainer>(
            "HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll/OtherPlayersList");
        var entryBeforeRefresh = otherPlayersList.GetChildren().OfType<OtherPlayerHudEntry>().Single();
        await DispatchViewportMoveAsync(entryBeforeRefresh.GetGlobalRect().GetCenter());
        var detailPanel = screen.Hud.GetNode<PanelContainer>("HudRoot/PlayerDetailPanel");
        AssertTrue(detailPanel.Visible, "鼠标停在其他玩家条目时应显示详情");

        transport.DelayMilliseconds = 30;
        transport.View.Players![0].Coins = 17;
        transport.View.Players[1].Coins = 29;
        var before = transport.PlayerViewRequestCount;
        var first = screen.RefreshPlayerViewAsync();
        var second = screen.RefreshPlayerViewAsync();
        await Task.WhenAll(first, second);

        AssertEqual(transport.PlayerViewRequestCount, before + 1, "轮询进行中必须跳过重入请求");
        AssertEqual(transport.MaxConcurrentPlayerViews, 1, "玩家视图请求不得重叠");
        var coinsLabel = screen.Hud.GetNode<Label>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/StatusContent/CoinsBox/CoinsValue"
        );
        AssertEqual(coinsLabel.Text, "17", "刷新后应更新当前玩家数据");
        var entryAfterRefresh = otherPlayersList.GetChildren().OfType<OtherPlayerHudEntry>().Single();
        AssertTrue(ReferenceEquals(entryBeforeRefresh, entryAfterRefresh),
            "玩家视图轮询必须复用同一其他玩家条目");
        AssertTrue(detailPanel.Visible, "鼠标持续停留时详情不得因轮询消失");
        AssertEqual(
            screen.Hud.GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/ResourceRow/CoinsValue").Text,
            "29",
            "轮询应在不重建条目的前提下更新悬停详情数值"
        );

        transport.View.Players[1].Coins = 31;
        await screen.RefreshPlayerViewAsync();
        AssertTrue(ReferenceEquals(entryBeforeRefresh,
                otherPlayersList.GetChildren().OfType<OtherPlayerHudEntry>().Single()),
            "多次轮询不得替换鼠标所在的玩家条目");
        AssertEqual(
            screen.Hud.GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/ResourceRow/CoinsValue").Text,
            "31",
            "多次轮询后悬停详情应保持最新数值"
        );

        var withoutOtherPlayer = GameScreenTransport.CloneView(transport.View);
        withoutOtherPlayer.Players = withoutOtherPlayer.Players!
            .Where(player => player.UserId == 1)
            .ToList();
        screen.Hud.RefreshPlayerView(withoutOtherPlayer, 1);
        AssertFalse(detailPanel.Visible, "悬停玩家被移除时必须清理详情");
        screen.Hud.RefreshPlayerView(GameScreenTransport.CloneView(transport.View), 1);
        await DispatchViewportMoveAsync(new Vector2(960, 540));
        AssertFalse(detailPanel.Visible, "鼠标移出玩家条目后必须清理详情");
        transport.DelayMilliseconds = 2;
    }

    private async Task DispatchViewportMoveAsync(Vector2 position)
    {
        Root.PushInput(new InputEventMouseMotion
        {
            Position = position,
            GlobalPosition = position
        }, true);
        await ToSignal(this, SignalName.ProcessFrame);
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
            "HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/EndTurnSlot/EndTurnButton"
        );
        AssertTrue(screen.Hud.EndTurnVisible, "提交前结束回合入口必须可见");
        AssertFalse(screen.Hud.EndTurnDisabled,
            $"提交前结束回合必须可点击，当前原因：{screen.Hud.EndTurnTooltip}");
        await DispatchViewportClickAsync(screen.Hud.ActionMenuButton);
        AssertTrue(screen.Hud.IsActionMenuOpen, "真实 GUI 点击后行为菜单必须展开");
        AssertTrue(endTurnButton.IsVisibleInTree(), "行为菜单展开后结束回合按钮必须在场景树中可见");
        await DispatchViewportClickAsync(endTurnButton);
        await DispatchViewportClickAsync(endTurnButton);
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
            AssertTrue(Guid.TryParseExact(request.RequestId, "D", out _), "结束回合必须携带标准 UUID requestId");
            AssertTrue(request.RequestId.Length <= 64, "结束回合 requestId 不得超过服务端 64 字符上限");
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
        var beforeNetworkActions = transport.GameActionRequestCount;
        await screen.SubmitEndTurnAsync();
        await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
        AssertTrue(transport.PlayerViewRequestCount > beforeNetworkRefresh, "网络错误后必须只读刷新权威玩家视图");
        AssertEqual(transport.GameActionRequestCount, beforeNetworkActions + 1, "网络错误不得自动生成新 requestId 并重发 END_TURN");
        AssertFalse(screen.IsEndTurnSubmitting, "网络错误后必须恢复按钮状态");
        AssertTrue(screen.Hud.EndTurnVisible && !screen.Hud.EndTurnDisabled, "刷新确认仍是本人回合后才允许发起新行动");
        AssertTrue(screen.LastPlayerViewError.Contains("无法连接服务器", StringComparison.Ordinal), "网络错误应保留可读提示");
        AssertEqual(
            transport.ActionRequestIds.Distinct(StringComparer.Ordinal).Count(),
            transport.ActionRequestIds.Count,
            "每次新的结束回合逻辑行动必须使用不同 requestId"
        );

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

    private async Task VerifyAllOfflineAndHeartbeatVersionGuardAsync(
        GameScreen screen,
        GameScreenTransport transport)
    {
        transport.View.StateVersion = 20;
        transport.View.CurrentTurnUserId = 1;
        transport.View.EngineState ??= new EngineStateDto { ConnectionPolicy = GameScreenTransport.CreateConnectionPolicy() };
        transport.View.EngineState.PausedReason = "ALL_OFFLINE";
        foreach (var player in transport.View.Players ?? [])
        {
            player.IsConnected = 0;
        }

        await screen.ConnectionLease.RenewNowAsync();
        AssertEqual(screen.LatestStateVersion, 20L, "心跳响应中的较新玩家视图应更新界面");
        AssertEqual(screen.Hud.TurnNoticeText, "全员离线暂停，正在等待服务端恢复……", "必须按 pausedReason 识别 ALL_OFFLINE");
        AssertFalse(screen.Hud.EndTurnVisible, "全员离线暂停时不得开放结束回合");

        transport.View.StateVersion = 21;
        transport.View.EngineState.PausedReason = null;
        foreach (var player in transport.View.Players ?? [])
        {
            player.IsConnected = 1;
        }

        await screen.ConnectionLease.RenewNowAsync();
        await WaitUntilAsync(() => screen.LatestStateVersion == 21 && !screen.IsAvailableActionsRequestInProgress);
        AssertEqual(screen.Hud.TurnNoticeText, "当前是你的回合", "服务端解除暂停后应恢复回合提示");
        AssertTrue(screen.Hud.EndTurnVisible && !screen.Hud.EndTurnDisabled, "恢复后应重新取得可用行动");

        transport.View.StateVersion = 20;
        await screen.RefreshPlayerViewAsync();
        AssertEqual(screen.LatestStateVersion, 21L, "较旧 player-view 轮询不得覆盖较新的心跳响应");
        transport.View.StateVersion = 21;
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

    private async Task DispatchViewportClickAsync(Control control)
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var position = control.GetGlobalRect().GetCenter();
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
        public int ConnectionRequestCount { get; private set; }
        public int GameActionRequestCount { get; private set; }
        public int PlayerViewInFlight { get; private set; }
        public int GameActionInFlight { get; private set; }
        public int MaxConcurrentPlayerViews { get; private set; }
        public int DelayMilliseconds { get; set; } = 2;
        public bool FailPlayerView { get; set; }
        public bool AdvanceOtherPlayerStatsOnPoll { get; set; }
        public string? LastAvailableActionsPlayerId { get; private set; }
        public HttpClient.Method LastAvailableActionsMethod { get; private set; }
        public bool LastAvailableActionsAuthorized { get; private set; }
        public HttpClient.Method LastGameActionMethod { get; private set; }
        public bool LastGameActionAuthorized { get; private set; }
        public GameActionRequestDto? LastActionRequest { get; private set; }
        public List<string> ActionRequestIds { get; } = [];
        public string? ActionFailureCode { get; set; }
        public string ActionFailureMessage { get; set; } = "";
        public Action? ActionRequestCallback { get; set; }
        public TaskCompletionSource<ApiResult<PlayerViewResponseDto>>? PlayerViewGate { get; set; }
        public TaskCompletionSource<ApiResult<GameActionResponseDto>>? ActionGate { get; set; }

        public GameScreenTransport()
        {
            // 视觉检查使用真实静态锚点，确保两枚棋子实际落在市政厅格并验证置顶与居中。
            var townHall = Map.Cells?.FirstOrDefault(cell => cell.CellCode == "A10")
                ?? throw new InvalidOperationException("测试地图缺少 A10 市政厅格。" );
            foreach (var player in View.Players ?? [])
            {
                player.CurrentCellId = townHall.CellId;
                player.CurrentCellCode = townHall.CellCode;
                player.CurrentCellName = townHall.CellName;
                player.CurrentCellNameText = townHall.CellNameText;
            }
        }

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
                if (AdvanceOtherPlayerStatsOnPoll
                    && View.Players?.FirstOrDefault(player => player.UserId == 2) is { } otherPlayer)
                {
                    otherPlayer.Coins++;
                    otherPlayer.Prestige++;
                }
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

            if (path == "/games/77/players/1001/connection"
                && typeof(T) == typeof(PlayerViewResponseDto)
                && body is PlayerConnectionRequestDto connectionRequest)
            {
                ConnectionRequestCount++;
                var response = CloneView(View);
                response.EngineState ??= new EngineStateDto();
                response.EngineState.ConnectionPolicy = CreateConnectionPolicy();
                response.EngineState.Connection = new GameConnectionInfoDto
                {
                    ClientInstanceId = connectionRequest.ClientInstanceId,
                    ConnectionId = connectionRequest.ConnectionId ?? "game-screen-connection",
                    ServerTime = "2026-11-01T12:00:00.000000",
                    LastHeartbeatAt = "2026-11-01T12:00:00.000000",
                    ExpiresAt = "2026-11-01T12:00:15.000000",
                    ServerTimeZone = "Asia/Shanghai",
                    ServerUtcOffset = "+08:00",
                    ConnectionActive = true,
                    PlayerConnected = true,
                    HeartbeatSeconds = 5,
                    TimeoutSeconds = 15,
                    ReconnectWaitSeconds = 10,
                    InitialGraceSeconds = 45
                };
                return (ApiResult<T>)(object)ApiResult<PlayerViewResponseDto>.Success(response, "success", 200);
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

            if (path == "/games/77/players/1001/movable-cells" && typeof(T) == typeof(List<MapCellDto>))
            {
                var target = Map.Cells?.FirstOrDefault(cell => cell.CellCode != "A10")
                    ?? throw new InvalidOperationException("测试地图缺少可移动目标格。" );
                return (ApiResult<T>)(object)ApiResult<List<MapCellDto>>.Success(
                    [target],
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
                if (LastActionRequest == null
                    || !Guid.TryParseExact(LastActionRequest.RequestId, "D", out _)
                    || LastActionRequest.RequestId.Length > 64)
                {
                    GameActionInFlight--;
                    return ApiResult<T>.Failure("测试行动缺少有效 requestId。", "PARAM_INVALID");
                }

                ActionRequestIds.Add(LastActionRequest.RequestId);
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
                EngineState = source.EngineState == null
                    ? null
                    : new EngineStateDto
                    {
                        BatchId = source.EngineState.BatchId,
                        PausedReason = source.EngineState.PausedReason,
                        Phase = source.EngineState.Phase,
                        CurrentPlayerId = source.EngineState.CurrentPlayerId,
                        PendingReward = source.EngineState.PendingReward,
                        PendingCheck = source.EngineState.PendingCheck,
                        MovementRulesVersion = source.EngineState.MovementRulesVersion,
                        QuestSubmissionVersion = source.EngineState.QuestSubmissionVersion,
                        ConnectionPolicy = source.EngineState.ConnectionPolicy == null
                            ? null
                            : CreateConnectionPolicy()
                    },
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
                IsConnected = source.IsConnected,
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
                EngineState = new EngineStateDto { ConnectionPolicy = CreateConnectionPolicy() },
                Players =
                [
                    CreatePlayer(1001, 1, "GPT1", 1, 5, 3),
                    CreatePlayer(1002, 2, "GPT2", 2, 4, 6)
                ]
            };
        }

        public static GameConnectionPolicyDto CreateConnectionPolicy()
        {
            return new GameConnectionPolicyDto
            {
                HeartbeatSeconds = 5,
                TimeoutSeconds = 15,
                ReconnectWaitSeconds = 10,
                InitialGraceSeconds = 45
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
