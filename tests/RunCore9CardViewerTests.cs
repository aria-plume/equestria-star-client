using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;
using EquestriaStar.State;
using EquestriaStar.UI;
using Godot;

public partial class RunCore9CardViewerTests : SceneTree
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
        VerifyDtoParsing();
        VerifyAcceptQuestSerialization();
        VerifyItemActionSerialization();
        VerifyArtCatalog();

        var session = Root.GetNode<Session>("Session");
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new Core9Transport();
        apiClient.SetTransportForTests(transport);
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "core9-card-token",
            User = new UserDto { UserId = 1, Username = "gpt1", Nickname = "GPT1" }
        });
        session.SetCurrentRoom(10, "CORE_9 卡牌测试房");
        session.SetCurrentGame(77);

        var scene = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn");
        var screen = scene.Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        await WaitUntilAsync(() => screen.MapScene.GeneratedCellCount == 138
            && screen.LatestPlayerView != null
            && screen.IsGameConnectionReady
            && screen.Hud.EndTurnVisible
            && !screen.IsAvailableActionsRequestInProgress);
        screen.GetNode<Timer>("PlayerViewTimer").Stop();

        VerifyInputRouting(screen);
        if (OS.GetCmdlineUserArgs().Contains("--item-visual-check"))
        {
            await screen.OpenInventoryAsync();
            GD.Print("道具系统 1920×1080 视觉检查已就绪：背包横向滚动、卡图比例及操作按钮应完整可见。");
            return;
        }

        await VerifyQuestBoardAsync(screen, transport);
        await VerifyTurnEligibilityAsync(screen, transport);
        await VerifyQualificationFailureAsync(screen, transport);
        await VerifyShopShelfAsync(screen, transport);
        await VerifyItemSystemAsync(screen, transport);
        VerifyCardCounts(screen.CardViewer);
        await VerifyLateResponseAndRetryAsync(screen, transport);
        await VerifyMissingArtErrorsAsync(screen, transport);
        await VerifyPersonalQuestsAsync(screen, transport);
        await VerifyMoveStatePreservedAsync(screen);
        await VerifyRealGuiActionInputAsync(screen, transport);

        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            await screen.OpenQuestBoardAsync();
            GD.Print("CORE_9 公共任务板真实鼠标检查已就绪；点击亮起的接取按钮应刷新候选且保持弹层打开。");
            return;
        }

        await VerifyAcceptQuestAsync(screen, transport);
        await VerifyAcceptRefreshFailureAsync(screen, transport);
        await VerifyAcceptTimeoutAndLateResponseAsync(screen, transport);

        screen.Stop();
        screen.QueueFree();
        apiClient.ClearTransportForTests();
        session.ClearAuth();
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Finish();
    }

    private void VerifyDtoParsing()
    {
        const string json = """
        {
          "code": 1,
          "msg": "success",
          "data": {
            "gameId": 77,
            "mapConfigId": 1,
            "gameStatus": "RUNNING",
            "currentRound": 1,
            "currentTurnUserId": 1,
            "currentTurnSeq": 1,
            "stateVersion": 5,
            "players": [],
            "engineState": {
              "batchId": "CORE_9_PUBLIC_SHOP",
              "publicShop": {
                "items": [{ "shelfSlotId": 31, "itemId": 1003 }]
              }
            }
          }
        }
        """;
        var parsed = ApiClient.ParseHttpResult<PlayerViewResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(json)
        );
        AssertTrue(parsed.Ok && parsed.Data != null, "player-view CORE_9 DTO 应可解析");
        AssertEqual(parsed.Data?.StateVersion, 5L, "player-view 应解析 stateVersion");
        AssertEqual(parsed.Data?.EngineState?.BatchId, "CORE_9_PUBLIC_SHOP", "player-view 应解析 batchId");
        AssertEqual(parsed.Data?.EngineState?.PublicShop?.Items?[0].ShelfSlotId, 31L, "player-view 应解析同一 publicShop DTO");

        const string questJson = """
        {
          "code": 1,
          "msg": "success",
          "data": {
            "gameId": 77,
            "playerId": 1001,
            "quests": [
              {"playerQuestId": 401, "questId": 6, "questCode": "OFFICIAL_QUEST_006", "status": "IN_PROGRESS"}
            ],
            "completedQuests": [
              {"playerQuestId": 900, "questId": 10, "questCode": "OFFICIAL_QUEST_010", "status": "COMPLETED"}
            ]
          }
        }
        """;
        var quests = ApiClient.ParseHttpResult<PlayerQuestListResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(questJson)
        );
        AssertEqual(quests.Data?.Quests?[0].PlayerQuestId, 401L, "个人任务 DTO 应解析 playerQuestId");
        AssertEqual(quests.Data?.CompletedQuests?[0].PlayerQuestId, 900L, "个人任务 DTO 应独立解析 completedQuests");
    }

    private void VerifyAcceptQuestSerialization()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var request = new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = 1001,
            StateVersion = 8,
            ActionType = "ACCEPT_QUEST",
            Payload = new JsonObject { ["questId"] = 149 }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request));
        var root = document.RootElement;
        AssertEqual(root.GetProperty("requestId").GetString(), requestId, "接取 JSON 应序列化 requestId");
        AssertEqual(root.GetProperty("playerId").GetInt64(), 1001L, "接取 JSON 应序列化局内 playerId");
        AssertEqual(root.GetProperty("stateVersion").GetInt64(), 8L, "接取 JSON 应序列化 stateVersion");
        AssertEqual(root.GetProperty("actionType").GetString(), "ACCEPT_QUEST", "接取 JSON 行动类型应正确");
        var payload = root.GetProperty("payload");
        AssertEqual(payload.EnumerateObject().Count(), 1, "接取 JSON payload 只能包含 questId");
        AssertEqual(payload.GetProperty("questId").GetInt64(), 149L, "接取 JSON 必须发送 questId");
    }

    private void VerifyItemActionSerialization()
    {
        var buy = ItemSystemModel.CreateShopBuyRequest(1001, 8, 31, Guid.NewGuid().ToString("D"));
        var use = ItemSystemModel.CreateItemRequest(1001, 8, "USE_ITEM", 2001, null, Guid.NewGuid().ToString("D"));
        var equip = ItemSystemModel.CreateItemRequest(1001, 8, "EQUIP_ITEM", 2004, 2010, Guid.NewGuid().ToString("D"));
        var unequip = ItemSystemModel.CreateItemRequest(1001, 8, "UNEQUIP_ITEM", 2011, null, Guid.NewGuid().ToString("D"));

        AssertEqual(buy.ActionType, "SHOP_BUY_ITEM", "购买应使用明确行动类型");
        AssertEqual(buy.Payload.Count, 1, "购买 payload 只能包含 shelfSlotId");
        AssertEqual(buy.Payload["shelfSlotId"]?.GetValue<long>(), 31L, "购买必须使用货架实例 ID");
        AssertEqual(use.Payload.Count, 1, "使用 payload 只能包含 playerItemId");
        AssertEqual(use.Payload["playerItemId"]?.GetValue<long>(), 2001L, "使用必须使用持有实例 ID");
        AssertEqual(equip.Payload.Count, 2, "满槽装备 payload 应包含实例和替换实例");
        AssertEqual(equip.Payload["replacePlayerItemId"]?.GetValue<long>(), 2010L, "替换必须使用用户选择的装备实例");
        AssertEqual(unequip.Payload.Count, 1, "卸下 payload 不得附带未确认字段");
        AssertTrue(new[] { buy, use, equip, unequip }.All(request => Guid.TryParse(request.RequestId, out _)),
            "所有道具写行动都必须携带有效 requestId");

        const string responseJson = """
        {
          "code":1,
          "msg":"success",
          "data":{
            "gameId":77,
            "oldStateVersion":8,
            "newStateVersion":9,
            "actionType":"SHOP_BUY_ITEM",
            "shopBuy":{
              "boughtItem":{"playerItemId":3001,"itemId":1003,"itemName":"苹果","location":"HAND","locationText":"背包"},
              "coinsAfter":7,
              "newShelfItem":null
            },
            "itemUse":null
          }
        }
        """;
        var parsed = ApiClient.ParseHttpResult<GameActionResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(responseJson)
        );
        AssertEqual(parsed.Data?.ShopBuy?.BoughtItem?.PlayerItemId, 3001L, "购买回执应解析真实入包实例 ID");
        AssertEqual(parsed.Data?.ShopBuy?.CoinsAfter, 7, "购买回执应解析权威金币余额");

        const string itemUseJson = """
        {
          "code":1,
          "msg":"success",
          "data":{
            "gameId":77,
            "oldStateVersion":9,
            "newStateVersion":10,
            "actionType":"EQUIP_ITEM",
            "itemUse":{
              "playerItemId":2004,
              "itemId":1013,
              "itemName":"魔法书",
              "itemType":"EQUIPMENT",
              "itemTypeText":"装备",
              "locationAfter":"EQUIPPED",
              "locationAfterText":"已装备",
              "replacedPlayerItemId":2010,
              "speedDelta":null,
              "speedAfter":null,
              "socialDelta":null,
              "socialAfter":null,
              "fromCellId":null,
              "toCellId":null
            }
          }
        }
        """;
        var itemUseParsed = ApiClient.ParseHttpResult<GameActionResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(itemUseJson)
        );
        AssertEqual(itemUseParsed.Data?.ItemUse?.PlayerItemId, 2004L, "道具结果应解析持有实例 ID");
        AssertEqual(itemUseParsed.Data?.ItemUse?.ReplacedPlayerItemId, 2010L, "装备结果应解析被替换实例 ID");
        AssertEqual(itemUseParsed.Data?.ItemUse?.LocationAfter, "EQUIPPED", "道具结果应解析权威持有位置");
    }

    private void VerifyArtCatalog()
    {
        AssertEqual(CardArtCatalog.QuestArtPaths.Count, 24, "任务卡映射必须正好包含 24 张");
        AssertEqual(CardArtCatalog.ItemArtPaths.Count, 5, "道具卡映射必须正好包含 5 张");
        AssertEqual(CardArtCatalog.QuestArtPaths["OFFICIAL_QUEST_023"], "res://assets/cards/quests/24.jpg", "023 必须映射 24.jpg");
        AssertEqual(CardArtCatalog.ItemArtPaths[1018], "res://assets/cards/items/蹄铁新.jpg", "1018 必须映射蹄铁新.jpg");

        foreach (var questCode in CardArtCatalog.QuestArtPaths.Keys)
        {
            AssertTrue(
                CardArtCatalog.TryGetQuestTexture(questCode, out var texture, out _) && texture != null,
                $"任务卡 {questCode} 应解析到存在的 Texture2D"
            );
        }

        foreach (var itemId in CardArtCatalog.ItemArtPaths.Keys)
        {
            AssertTrue(
                CardArtCatalog.TryGetItemTexture(itemId, out var texture, out _) && texture != null,
                $"道具卡 {itemId} 应解析到存在的 Texture2D"
            );
        }
    }

    private void VerifyInputRouting(GameScreen screen)
    {
        AssertEqual(screen.MouseFilter, Control.MouseFilterEnum.Ignore, "GameScreen 透明布局仍应忽略地图输入");
        AssertEqual(screen.Hud.GetNode<Control>("HudRoot").MouseFilter, Control.MouseFilterEnum.Ignore, "HUD 透明根仍应忽略地图输入");
        AssertTrue(screen.Hud.QuestBoardButton.IsInGroup("blocks_map_input"), "公共任务按钮应阻止地图输入");
        AssertTrue(screen.Hud.ShopShelfButton.IsInGroup("blocks_map_input"), "公共货架按钮应阻止地图输入");
        AssertTrue(screen.CardViewer.InputBlocker.IsInGroup("blocks_map_input"), "全屏查看层应加入 blocks_map_input");
        AssertEqual(screen.CardViewer.InputBlocker.MouseFilter, Control.MouseFilterEnum.Stop, "查看层应消费鼠标输入");
        AssertTrue(screen.CardViewer.GetActionArea(0).IsInGroup("blocks_map_input"), "卡牌操作区域应阻止地图输入");
        AssertEqual(screen.CardViewer.GetActionArea(0).MouseFilter, Control.MouseFilterEnum.Stop, "卡牌操作区域应消费鼠标输入");
    }

    private async Task VerifyQuestBoardAsync(GameScreen screen, Core9Transport transport)
    {
        var before = transport.QuestRequestCount;
        var actionsBefore = transport.AvailableActionsRequestCount;
        await screen.OpenQuestBoardAsync();
        AssertEqual(transport.QuestRequestCount, before + 1, "每次打开任务栏都应重新 GET");
        AssertEqual(transport.LastQuestMethod, HttpClient.Method.Get, "任务板必须使用 GET");
        AssertEqual(transport.LastQuestPlayerId, "1001", "任务板必须使用局内 playerId");
        AssertTrue(transport.LastQuestAuthorized, "任务板请求必须携带 Bearer 认证");
        AssertSequence(screen.CardViewer.DisplayedKeys, ["OFFICIAL_QUEST_006", "OFFICIAL_QUEST_007", "OFFICIAL_QUEST_023"], "任务卡应按 slotNo 排序");
        AssertEqual(screen.CardViewer.VisibleCardCount, 3, "任务板应显示三张服务端卡牌");
        AssertEqual(screen.CardViewer.DisplayedCards[0].QuestId, 6L, "任务卡按钮必须绑定 questId");
        AssertEqual(screen.CardViewer.DisplayedCards[0].OfferId, 11L, "任务卡按钮必须绑定 offerId");
        AssertTrue(screen.CardViewer.IsActionButtonEnabled(0), "自己回合且 ACCEPT_QUEST 可用时接取按钮应亮起");
        AssertTrue(transport.AvailableActionsRequestCount > actionsBefore, "打开任务板应同步读取最新 available-actions");

        screen.CloseCardViewer();
        await screen.OpenQuestBoardAsync();
        AssertEqual(transport.QuestRequestCount, before + 2, "重新打开任务栏必须再次 GET");
        screen.CloseCardViewer();
    }

    private async Task VerifyTurnEligibilityAsync(GameScreen screen, Core9Transport transport)
    {
        transport.View.CurrentTurnUserId = 2;
        transport.View.StateVersion++;
        transport.ShopResponse.StateVersion = transport.View.StateVersion;
        await screen.RefreshPlayerViewAsync();
        await screen.OpenQuestBoardAsync();
        AssertFalse(screen.CardViewer.IsActionButtonEnabled(0), "他人回合时接取按钮应灰显");
        AssertTrue(screen.CardViewer.GetActionButtonTooltip(0).Contains("不是你的回合", StringComparison.Ordinal), "他人回合应显示明确原因");
        var writesBefore = transport.WriteRequestCount;
        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        AssertTrue(screen.CardViewer.IsOpen, "点击灰色按钮区域应保持弹层打开");
        AssertEqual(transport.WriteRequestCount, writesBefore, "灰色操作按钮不得发送写请求");
        screen.CloseCardViewer();

        transport.View.CurrentTurnUserId = 1;
        transport.View.StateVersion++;
        transport.ShopResponse.StateVersion = transport.View.StateVersion;
        await screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => !screen.IsAvailableActionsRequestInProgress);
    }

    private async Task VerifyQualificationFailureAsync(GameScreen screen, Core9Transport transport)
    {
        transport.FailAvailableActions = true;
        await screen.OpenQuestBoardAsync();
        AssertEqual(screen.CardViewer.State, CardViewerState.Content, "资格请求失败时仍应保留只读卡牌内容");
        AssertFalse(screen.CardViewer.IsActionButtonEnabled(0), "资格请求失败时不得误显示可操作");
        AssertTrue(screen.CardViewer.GetActionButtonTooltip(0).Contains("失败", StringComparison.Ordinal), "资格失败应提供明确提示");
        screen.CloseCardViewer();

        transport.FailAvailableActions = false;
        await screen.OpenQuestBoardAsync();
        AssertTrue(screen.CardViewer.IsActionButtonEnabled(0), "重新打开并同步成功后应恢复按钮资格");
        screen.CloseCardViewer();
    }

    private async Task VerifyShopShelfAsync(GameScreen screen, Core9Transport transport)
    {
        transport.ShopResponse.AccessAllowed = false;
        await screen.OpenShopShelfAsync();
        AssertEqual(transport.LastShopMethod, HttpClient.Method.Get, "公共货架必须使用 GET");
        AssertEqual(transport.LastShopPlayerId, "1001", "货架必须使用局内 playerId");
        AssertTrue(transport.LastShopAuthorized, "公共货架请求必须携带 Bearer 认证");
        AssertSequence(screen.CardViewer.DisplayedKeys, ["1003", "1003", "1018"], "货架应保留服务端顺序和重复 itemId");
        AssertEqual(screen.CardViewer.VisibleCardCount, 3, "accessAllowed=false 仍应显示全部库存");
        AssertEqual(screen.CardViewer.DisplayedCards[0].ShelfSlotId, 31L, "重复商品必须绑定各自 shelfSlotId");
        AssertEqual(screen.CardViewer.DisplayedCards[1].ShelfSlotId, 32L, "重复商品不得丢失第二个 shelfSlotId");
        AssertFalse(screen.CardViewer.IsActionButtonEnabled(0), "accessAllowed=false 时购买按钮应灰显");
        AssertTrue(screen.CardViewer.GetActionButtonTooltip(0).Contains("位置", StringComparison.Ordinal), "地点不符应显示可靠提示");
        var actionWritesBefore = transport.ActionRequestCount;
        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        AssertEqual(transport.ActionRequestCount, actionWritesBefore, "地点资格不满足时购买按钮不得提交行动");
        AssertTrue(screen.CardViewer.IsOpen, "点击灰色购买按钮应保持弹层打开");

        var beforeQualificationRefresh = transport.ShopRequestCount;
        transport.ShopResponse.AccessAllowed = true;
        transport.View.StateVersion++;
        transport.ShopResponse.StateVersion = transport.View.StateVersion;
        await screen.RefreshPlayerViewAsync();
        await WaitUntilAsync(() => transport.ShopRequestCount > beforeQualificationRefresh
            && !screen.IsCardRequestInProgress
            && screen.CardViewer.IsActionButtonEnabled(0));
        AssertTrue(screen.CardViewer.IsActionButtonEnabled(0), "位置状态变化后应按最新只读资格更新购买按钮");
        screen.CloseCardViewer();

        transport.ShopBuyAvailable = false;
        transport.ActionReasonText = "金币不足";
        await screen.OpenShopShelfAsync();
        AssertFalse(screen.CardViewer.IsActionButtonEnabled(0), "SHOP_BUY_ITEM 不可用时购买按钮应灰显");
        AssertEqual(screen.CardViewer.GetActionButtonTooltip(0), "金币不足", "灰显按钮应展示后端 reasonText");
        screen.CloseCardViewer();
        transport.ShopBuyAvailable = true;
        transport.ActionReasonText = null;

        transport.ShopResponse.StateVersion = screen.LatestStateVersion - 1;
        await screen.OpenShopShelfAsync();
        AssertEqual(screen.CardViewer.State, CardViewerState.Error, "旧 stateVersion 货架响应不得覆盖当前状态");
        AssertTrue(screen.CardViewer.ErrorText.Contains("状态已更新", StringComparison.Ordinal), "旧货架响应应提示重试最新库存");
        screen.CloseCardViewer();
        transport.ShopResponse.StateVersion = screen.LatestStateVersion;
    }

    private async Task VerifyItemSystemAsync(GameScreen screen, Core9Transport transport)
    {
        // 公共货架购买：使用真实 GUI 按下/释放，只提交一次并保持货架打开。
        await screen.OpenShopShelfAsync();
        var purchaseCard = screen.CardViewer.DisplayedCards[0];
        var writesBeforePurchase = transport.ActionRequestCount;
        var shopGetsBefore = transport.ShopRequestCount;
        var itemGetsBeforePurchase = transport.ItemsRequestCount;
        transport.ActionGate = new TaskCompletionSource<bool>();
        await DispatchViewportClickAsync(screen.CardViewer.GetActionButton(0));
        await WaitUntilAsync(() => screen.IsItemSystemSubmitting
            && transport.ActionRequestCount == writesBeforePurchase + 1);
        await DispatchViewportClickAsync(screen.CardViewer.GetActionButton(0));
        AssertEqual(transport.ActionRequestCount, writesBeforePurchase + 1,
            "购买提交期间快速重复点击不得产生第二次写请求");
        transport.ActionGate.SetResult(true);
        await WaitUntilAsync(() => !screen.IsItemSystemSubmitting && screen.CardViewer.State == CardViewerState.Content);
        transport.ActionGate = null;
        AssertEqual(transport.LastActionRequest?.ActionType, "SHOP_BUY_ITEM", "货架按钮必须提交购买行动");
        AssertEqual(transport.LastActionRequest?.Payload.Count, 1, "购买 payload 不得附加 itemId 等字段");
        AssertEqual(transport.LastActionRequest?.Payload["shelfSlotId"]?.GetValue<long>(), purchaseCard.ShelfSlotId,
            "购买必须使用所点击卡牌的 shelfSlotId");
        AssertTrue(Guid.TryParse(transport.LastActionRequest?.RequestId, out _), "购买必须携带新 requestId");
        AssertTrue(screen.CardViewer.IsOpen, "购买成功后货架应保持打开");
        AssertEqual(transport.ShopRequestCount, shopGetsBefore + 1, "购买成功后应重新 GET 公共货架");
        AssertEqual(transport.ItemsRequestCount, itemGetsBeforePurchase + 1, "购买成功后应只 GET 一次背包确认入包实例");
        AssertFalse(screen.CardViewer.DisplayedCards.Any(card => card.ShelfSlotId == purchaseCard.ShelfSlotId),
            "购买成功后应按服务端最新库存移除已购买货架实例");
        screen.CloseCardViewer();

        // 背包每次打开重新 GET；重复同名实例不得合并，且超过三张可以滚动查看。
        var itemsBeforeOpen = transport.ItemsRequestCount;
        await DispatchViewportClickAsync(screen.Hud.InventoryButton);
        await WaitUntilAsync(() => screen.CardViewer.Kind == CardViewerKind.Inventory
            && screen.CardViewer.State == CardViewerState.Content);
        AssertEqual(transport.ItemsRequestCount, itemsBeforeOpen + 1, "每次打开背包必须重新 GET /items");
        AssertTrue(screen.CardViewer.VisibleCardCount > 3, "背包不得受三张卡上限限制");
        AssertTrue(screen.CardViewer.DisplayedCards.Count(card => card.ItemId == 1003) >= 2,
            "同名道具实例必须按 playerItemId 分别展示");
        AssertEqual(
            screen.CardViewer.DisplayedCards.Select(card => card.PlayerItemId).Distinct().Count(),
            screen.CardViewer.DisplayedCards.Count,
            "背包展示不得合并不同 playerItemId"
        );

        // 消耗品使用必须先二次确认；取消零写请求，确认后只提交一次。
        var consumable = screen.CardViewer.ItemCards.First(card => card.PlayerItemId == 2001);
        var writesBeforeUse = transport.ActionRequestCount;
        await DispatchViewportClickAsync(consumable.ActionButton);
        AssertTrue(screen.CardViewer.DecisionVisible, "点击消耗品使用应打开二次确认");
        AssertEqual(transport.ActionRequestCount, writesBeforeUse, "打开使用确认不得提前发送写请求");
        await DispatchViewportClickAsync(screen.CardViewer.GetNode<Button>(
            "OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Actions/CancelButton"));
        AssertFalse(screen.CardViewer.DecisionVisible, "取消应关闭使用确认");
        AssertEqual(transport.ActionRequestCount, writesBeforeUse, "取消使用必须保持零写请求");

        consumable = screen.CardViewer.ItemCards.First(card => card.PlayerItemId == 2001);
        await DispatchViewportClickAsync(consumable.ActionButton);
        await DispatchViewportClickAsync(screen.CardViewer.GetNode<Button>(
            "OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Actions/ConfirmButton"));
        await WaitUntilAsync(() => !screen.IsItemSystemSubmitting
            && screen.CardViewer.State == CardViewerState.Content
            && transport.ActionRequestCount == writesBeforeUse + 1);
        AssertEqual(transport.LastActionRequest?.ActionType, "USE_ITEM", "确认使用应提交 USE_ITEM");
        AssertEqual(transport.LastActionRequest?.Payload.Count, 1, "使用 payload 只能包含 playerItemId");
        AssertEqual(transport.LastActionRequest?.Payload["playerItemId"]?.GetValue<long>(), 2001L,
            "使用必须绑定所选持有实例");
        AssertFalse(screen.CardViewer.DisplayedCards.Any(card => card.PlayerItemId == 2001),
            "使用成功后应按重新查询结果移除已消耗实例");

        // 装备栏已满时必须由玩家选择替换实例，取消不提交。
        var equipment = screen.CardViewer.ItemCards.First(card => card.PlayerItemId == 2004);
        var writesBeforeEquip = transport.ActionRequestCount;
        await DispatchViewportClickAsync(equipment.ActionButton);
        AssertTrue(screen.CardViewer.DecisionVisible, "满两件装备时应打开替换选择");
        var replacementOptions = screen.CardViewer.GetNode<VBoxContainer>(
            "OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/ReplacementOptions");
        AssertEqual(replacementOptions.GetChildCount(), 2, "替换选择应列出两件当前装备");
        await DispatchViewportClickAsync(screen.CardViewer.GetNode<Button>(
            "OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Actions/CancelButton"));
        AssertEqual(transport.ActionRequestCount, writesBeforeEquip, "取消替换不得发送写请求");

        equipment = screen.CardViewer.ItemCards.First(card => card.PlayerItemId == 2004);
        await DispatchViewportClickAsync(equipment.ActionButton);
        replacementOptions = screen.CardViewer.GetNode<VBoxContainer>(
            "OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/ReplacementOptions");
        await DispatchViewportClickAsync(replacementOptions.GetChild<Button>(0));
        await WaitUntilAsync(() => !screen.IsItemSystemSubmitting
            && screen.CardViewer.State == CardViewerState.Content
            && transport.ActionRequestCount == writesBeforeEquip + 1);
        AssertEqual(transport.LastActionRequest?.ActionType, "EQUIP_ITEM", "替换应提交 EQUIP_ITEM");
        AssertEqual(transport.LastActionRequest?.Payload["playerItemId"]?.GetValue<long>(), 2004L,
            "装备必须使用手牌实例 ID");
        AssertEqual(transport.LastActionRequest?.Payload["replacePlayerItemId"]?.GetValue<long>(), 2010L,
            "替换必须发送玩家实际选择的装备实例 ID");
        screen.CloseCardViewer();

        // 装备区每次打开重新查询，只显示 EQUIPPED；卸下成功后实例回到背包。
        var itemsBeforeEquipment = transport.ItemsRequestCount;
        await DispatchViewportClickAsync(screen.Hud.EquipmentButton);
        await WaitUntilAsync(() => screen.CardViewer.Kind == CardViewerKind.Equipment
            && screen.CardViewer.State == CardViewerState.Content);
        AssertEqual(transport.ItemsRequestCount, itemsBeforeEquipment + 1, "每次打开装备区必须重新 GET /items");
        AssertTrue(screen.CardViewer.DisplayedCards.All(card => string.Equals(card.Location, "EQUIPPED", StringComparison.Ordinal)),
            "装备区只能显示 EQUIPPED 实例");
        var equippedCard = screen.CardViewer.ItemCards.First(card => card.PlayerItemId == 2004);
        var writesBeforeUnequip = transport.ActionRequestCount;
        await DispatchViewportClickAsync(equippedCard.ActionButton);
        await WaitUntilAsync(() => !screen.IsItemSystemSubmitting
            && screen.CardViewer.State == CardViewerState.Content
            && transport.ActionRequestCount == writesBeforeUnequip + 1);
        AssertEqual(transport.LastActionRequest?.ActionType, "UNEQUIP_ITEM", "装备区操作必须提交 UNEQUIP_ITEM");
        AssertFalse(screen.CardViewer.DisplayedCards.Any(card => card.PlayerItemId == 2004),
            "卸下成功后该实例不应继续出现在装备区");
        screen.CloseCardViewer();

        // 行动级资格置灰时，真实鼠标点击不能产生请求，也不能关闭弹层或穿透地图。
        transport.ItemActionsAvailable = false;
        transport.ActionReasonText = "服务端限制本回合只能调整一次装备";
        await screen.OpenInventoryAsync();
        var disabledCard = screen.CardViewer.ItemCards.First();
        AssertTrue(disabledCard.ActionButton.Disabled, "道具行动不可用时按钮应灰显");
        var writesBeforeDisabled = transport.ActionRequestCount;
        await DispatchViewportClickAsync(disabledCard.ActionButton);
        AssertEqual(transport.ActionRequestCount, writesBeforeDisabled, "灰色道具按钮真实点击不得提交请求");
        AssertTrue(screen.CardViewer.IsOpen, "灰色道具按钮区域不得关闭底层弹层");
        screen.CloseCardViewer();
        transport.ItemActionsAvailable = true;
        transport.ActionReasonText = null;

        // 网络未知结果只允许同步权威状态，不得以新 requestId 自动重发写行动。
        await screen.OpenInventoryAsync();
        var unknownCard = screen.CardViewer.ItemCards.First(card => card.PlayerItemId == 2002);
        var writesBeforeUnknown = transport.ActionRequestCount;
        transport.FailNextItemAction = true;
        await DispatchViewportClickAsync(unknownCard.ActionButton);
        await DispatchViewportClickAsync(screen.CardViewer.GetNode<Button>(
            "OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Actions/ConfirmButton"));
        await WaitUntilAsync(() => !screen.IsItemSystemSubmitting && screen.CardViewer.State == CardViewerState.Error);
        AssertEqual(transport.ActionRequestCount, writesBeforeUnknown + 1,
            "网络未知结果不得自动创建新 requestId 重发道具行动");
        AssertTrue(screen.CardViewer.ErrorText.Contains("结果未知", StringComparison.Ordinal),
            "网络未知结果应明确提示只同步、不自动重发");
        var writesBeforeRetry = transport.ActionRequestCount;
        screen.CardViewer.RequestRetry();
        await WaitUntilAsync(() => screen.CardViewer.State == CardViewerState.Content);
        AssertEqual(transport.ActionRequestCount, writesBeforeRetry, "错误重试只能重新 GET 列表，不得重发道具行动");
        screen.CloseCardViewer();

        // 购买已经确认成功但后续货架 GET 失败时，必须与“购买失败”明确区分。
        await screen.OpenShopShelfAsync();
        var writesBeforeRefreshFailure = transport.ActionRequestCount;
        transport.FailNextShopRequest = true;
        await DispatchViewportClickAsync(screen.CardViewer.GetActionButton(0));
        await WaitUntilAsync(() => !screen.IsItemSystemSubmitting && screen.CardViewer.State == CardViewerState.Error);
        AssertEqual(transport.ActionRequestCount, writesBeforeRefreshFailure + 1,
            "购买成功后的刷新失败不得再次提交购买");
        AssertTrue(screen.CardViewer.ErrorText.Contains("购买已成功", StringComparison.Ordinal)
            && screen.CardViewer.ErrorText.Contains("货架刷新失败", StringComparison.Ordinal),
            "购买成功但 GET 失败时必须分别提示行动结果与刷新结果");
        writesBeforeRetry = transport.ActionRequestCount;
        screen.CardViewer.RequestRetry();
        await WaitUntilAsync(() => screen.CardViewer.State == CardViewerState.Content);
        AssertEqual(transport.ActionRequestCount, writesBeforeRetry, "购买后刷新错误的重试只能重新 GET");
        screen.CloseCardViewer();
    }

    private void VerifyCardCounts(CardViewerOverlay overlay)
    {
        CardArtCatalog.TryGetQuestTexture("OFFICIAL_QUEST_006", out var texture, out _);
        var cards = Enumerable.Range(0, 3)
            .Select(index => new CardViewerCard($"Q{index}", texture!))
            .ToList();
        for (var count = 0; count <= 3; count++)
        {
            overlay.ShowCards(CardViewerKind.QuestBoard, cards.Take(count).ToList());
            AssertEqual(overlay.VisibleCardCount, count, $"{count} 张卡牌应稳定显示对应数量");
            if (count > 0)
            {
                var aspect = overlay.GetNode<AspectRatioContainer>($"OverlayRoot/Center/Layout/ContentState/CardRow/CardSlot1/CardAspect");
                AssertTrue(aspect.CustomMinimumSize.Y > 0 && aspect.Ratio > 1.0f, "任务卡比例容器应独立于按钮保持横向比例");
            }
        }

        overlay.CloseOverlay();
    }

    private async Task VerifyLateResponseAndRetryAsync(GameScreen screen, Core9Transport transport)
    {
        var gate = new TaskCompletionSource<ApiResult<QuestOfferListResponseDto>>();
        transport.QuestGate = gate;
        var pending = screen.OpenQuestBoardAsync();
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        screen.CloseCardViewer();
        gate.SetResult(ApiResult<QuestOfferListResponseDto>.Success(transport.QuestResponse, "success", 200));
        await pending;
        AssertFalse(screen.CardViewer.IsOpen, "关闭后的迟到响应不得重新打开弹层");
        transport.QuestGate = null;

        transport.FailQuest = true;
        await screen.OpenQuestBoardAsync();
        AssertEqual(screen.CardViewer.State, CardViewerState.Error, "请求失败应进入错误状态");
        AssertTrue(screen.CardViewer.RetryVisible, "错误状态应显示重试按钮");
        transport.FailQuest = false;
        var beforeRetry = transport.QuestRequestCount;
        screen.CardViewer.RequestRetry();
        await WaitUntilAsync(() => screen.CardViewer.State == CardViewerState.Content);
        AssertTrue(screen.CardViewer.IsOpen, "重试按钮不得关闭弹层");
        AssertEqual(transport.QuestRequestCount, beforeRetry + 1, "重试应重新调用对应 GET");
        screen.CardViewer.CloseFromBackdrop();
        AssertFalse(screen.CardViewer.IsOpen, "点击重试按钮以外位置应关闭弹层");
    }

    private async Task VerifyMissingArtErrorsAsync(GameScreen screen, Core9Transport transport)
    {
        var quest = transport.QuestResponse.Offers![0];
        var originalQuestCode = quest.QuestCode;
        quest.QuestCode = "OFFICIAL_QUEST_UNKNOWN";
        await screen.OpenQuestBoardAsync();
        AssertEqual(screen.CardViewer.State, CardViewerState.Error, "任务卡缺少映射时必须显示错误");
        AssertTrue(screen.CardViewer.ErrorText.Contains("没有本地卡图映射", StringComparison.Ordinal), "任务卡映射错误应明确可读");
        screen.CloseCardViewer();
        quest.QuestCode = originalQuestCode;

        var item = transport.ShopResponse.Items![0];
        var originalItemId = item.ItemId;
        item.ItemId = 999999;
        await screen.OpenShopShelfAsync();
        AssertEqual(screen.CardViewer.State, CardViewerState.Error, "道具卡缺少映射时必须显示错误");
        AssertTrue(screen.CardViewer.ErrorText.Contains("没有本地卡图映射", StringComparison.Ordinal), "道具卡映射错误应明确可读");
        screen.CloseCardViewer();
        item.ItemId = originalItemId;
    }

    private async Task VerifyPersonalQuestsAsync(GameScreen screen, Core9Transport transport)
    {
        var before = transport.PlayerQuestRequestCount;
        screen.Hud.PersonalQuestsButton.EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Tasks);
        AssertEqual(transport.PlayerQuestRequestCount, before + 1, "点击左下角任务按钮应重新 GET 个人任务");
        AssertEqual(transport.LastPlayerQuestMethod, HttpClient.Method.Get, "个人任务必须使用 GET");
        AssertEqual(transport.LastPlayerQuestPlayerId, "1001", "个人任务必须使用局内 playerId");
        AssertTrue(transport.LastPlayerQuestAuthorized, "个人任务请求必须携带 Bearer 认证");
        AssertEqual(screen.QuestJournal.DisplayedTaskCount, 5, "个人任务不得受三张公共候选上限限制");
        AssertTrue(screen.QuestJournal.GetNode<ScrollContainer>("Root/Center/Panel/Margin/Content/TaskScroll").IsInGroup("blocks_map_input")
            || screen.QuestJournal.GetNode<Control>("Root").IsInGroup("blocks_map_input"), "个人任务滚动区域应处于输入阻断层内");
        screen.QuestJournal.ForceClose();

        var originalQuests = transport.PlayerQuestResponse.Quests;
        transport.PlayerQuestResponse.Quests = [];
        await screen.OpenPersonalQuestsAsync();
        AssertEqual(screen.QuestJournal.State, QuestJournalState.Tasks, "空个人任务应是正常内容状态");
        AssertEqual(screen.QuestJournal.DisplayedTaskCount, 0, "空个人任务不得伪造任务");
        screen.QuestJournal.ForceClose();
        transport.PlayerQuestResponse.Quests = originalQuests;
    }

    private async Task VerifyMoveStatePreservedAsync(GameScreen screen)
    {
        AssertTrue(screen.TryEnterMoveMode(1001), "测试玩家应能进入移动模式");
        await WaitUntilAsync(() => screen.MoveState == PlayerMoveState.SelectingTarget);
        var targets = screen.MovableCellIds.ToArray();
        var cameraPosition = screen.MapScene.CameraController.Position;

        await screen.OpenQuestBoardAsync();
        AssertEqual(screen.MoveState, PlayerMoveState.SelectingTarget, "打开查看层不得退出移动模式");
        AssertSequence(screen.MovableCellIds.OrderBy(id => id), targets.OrderBy(id => id), "打开查看层不得清除移动高亮");
        screen.CloseCardViewer();
        AssertEqual(screen.MoveState, PlayerMoveState.SelectingTarget, "关闭查看层应保留移动模式");
        AssertEqual(screen.MapScene.CameraController.Position, cameraPosition, "查看层打开关闭不得改变摄像机位置");
        screen.ExitMoveMode(true);
    }

    private async Task VerifyRealGuiActionInputAsync(GameScreen screen, Core9Transport transport)
    {
        await screen.OpenQuestBoardAsync();
        var selectedCard = screen.CardViewer.DisplayedCards[0];
        var actionCount = transport.ActionRequestCount;
        var mapInputCount = 0;
        var cameraPosition = screen.MapScene.CameraController.Position;
        transport.ActionGate = new TaskCompletionSource<bool>();

        void OnMapPointerClicked(Vector2 _) => mapInputCount++;
        void OnMapCellClicked(long _) => mapInputCount++;
        void OnMapEmptyClicked() => mapInputCount++;

        screen.MapScene.CameraController.MapPointerClicked += OnMapPointerClicked;
        screen.MapScene.MapCellClicked += OnMapCellClicked;
        screen.MapScene.MapEmptyClicked += OnMapEmptyClicked;
        try
        {
            // 通过 Viewport 的正常 GUI 分发路径送入按下和释放，不能用 EmitSignal(Pressed) 掩盖输入故障。
            await DispatchViewportClickAsync(screen.CardViewer.GetActionButton(0));
            await WaitUntilAsync(() => transport.ActionRequestCount == actionCount + 1 && screen.IsQuestAcceptSubmitting);
            await DispatchViewportClickAsync(screen.CardViewer.GetActionButton(0));
            AssertEqual(transport.ActionRequestCount, actionCount + 1, "真实 GUI 快速重复点击只能提交一次接取行动");
            AssertTrue(screen.CardViewer.IsOpen, "亮起按钮真实点击后弹层必须保持打开");
            AssertEqual(mapInputCount, 0, "操作按钮真实点击不得穿透为地图点击");
            AssertEqual(screen.MapScene.CameraController.Position, cameraPosition, "操作按钮真实点击不得移动摄像机");

            transport.ActionGate.SetResult(true);
            await WaitUntilAsync(() => !screen.IsQuestAcceptSubmitting
                && screen.CardViewer.State == CardViewerState.Content
                && screen.CardViewer.DisplayedCards.All(card => card.QuestId != selectedCard.QuestId));
            transport.ActionGate = null;

            await screen.OpenPersonalQuestsAsync();
            AssertTrue(
                screen.QuestJournal.DisplayedTaskCount > 5,
                "模拟接取成功后 GET /quests 应显示对应个人任务实例"
            );
            screen.QuestJournal.ForceClose();

            transport.AcceptQuestAvailable = false;
            transport.ActionReasonText = "当前地点不能接取任务";
            await screen.OpenQuestBoardAsync();
            AssertFalse(screen.CardViewer.IsActionButtonEnabled(0), "灰色按钮真实输入测试前应处于禁用状态");
            var disabledActionCount = transport.ActionRequestCount;
            await DispatchViewportClickAsync(screen.CardViewer.GetActionButton(0));
            AssertEqual(transport.ActionRequestCount, disabledActionCount, "灰色按钮真实点击不得提交行动");
            AssertTrue(screen.CardViewer.IsOpen, "灰色按钮区域真实点击不得关闭弹层");
            AssertEqual(mapInputCount, 0, "灰色按钮真实点击不得穿透为地图点击");

            await DispatchViewportClickAsync(new Vector2(20.0f, 20.0f));
            AssertFalse(screen.CardViewer.IsOpen, "按钮之外的背景真实点击仍应关闭弹层");
            AssertEqual(mapInputCount, 0, "背景关闭点击不得穿透为地图点击");
        }
        finally
        {
            transport.ActionGate = null;
            transport.AcceptQuestAvailable = true;
            transport.ActionReasonText = null;
            screen.MapScene.CameraController.MapPointerClicked -= OnMapPointerClicked;
            screen.MapScene.MapCellClicked -= OnMapCellClicked;
            screen.MapScene.MapEmptyClicked -= OnMapEmptyClicked;
            screen.CloseCardViewer();
        }
    }

    private async Task DispatchViewportClickAsync(Control control)
    {
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        await DispatchViewportClickAsync(control.GetGlobalRect().GetCenter());
    }

    private async Task DispatchViewportClickAsync(Vector2 viewportPosition)
    {
        Root.PushInput(new InputEventMouseMotion
        {
            Position = viewportPosition,
            GlobalPosition = viewportPosition
        }, true);
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);

        Root.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left,
            Pressed = true,
            Position = viewportPosition,
            GlobalPosition = viewportPosition
        }, true);
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);

        Root.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = viewportPosition,
            GlobalPosition = viewportPosition
        }, true);
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);
    }

    private async Task VerifyAcceptQuestAsync(GameScreen screen, Core9Transport transport)
    {
        await screen.OpenQuestBoardAsync();
        var selectedCard = screen.CardViewer.DisplayedCards[0];
        var actionCount = transport.ActionRequestCount;
        var questGets = transport.QuestRequestCount;
        var personalGets = transport.PlayerQuestRequestCount;
        var playerViewGets = transport.PlayerViewRequestCount;
        var actionGets = transport.AvailableActionsRequestCount;
        var stateVersion = screen.LatestStateVersion;
        transport.ActionGate = new TaskCompletionSource<bool>();

        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => transport.ActionRequestCount == actionCount + 1 && screen.IsQuestAcceptSubmitting);
        AssertEqual(transport.ActionRequestCount, actionCount + 1, "连续点击接取只能提交一次行动");
        var request = transport.LastActionRequest;
        AssertTrue(request != null, "接取请求应被测试传输层捕获");
        AssertTrue(Guid.TryParse(request?.RequestId, out _), "接取请求必须使用新的 UUID requestId");
        AssertEqual(request?.PlayerId, 1001L, "接取请求必须使用局内 playerId");
        AssertEqual(request?.StateVersion, stateVersion, "接取请求必须使用最新权威 stateVersion");
        AssertEqual(request?.ActionType, "ACCEPT_QUEST", "接取行动类型必须正确");
        AssertEqual(request?.Payload.Count, 1, "接取 payload 只能包含 questId");
        AssertEqual(request?.Payload["questId"]?.GetValue<long>(), selectedCard.QuestId, "接取 payload 必须使用卡牌 questId");

        transport.ActionGate.SetResult(true);
        await WaitUntilAsync(() => !screen.IsQuestAcceptSubmitting
            && screen.CardViewer.State == CardViewerState.Content
            && screen.CardViewer.DisplayedCards.All(card => card.QuestId != selectedCard.QuestId));
        AssertTrue(screen.CardViewer.IsOpen, "接取成功后公共任务板应保持打开");
        AssertEqual(transport.QuestRequestCount, questGets + 1, "接取成功后公共候选应只刷新一次");
        AssertEqual(transport.PlayerQuestRequestCount, personalGets + 1, "接取成功后个人任务应只同步一次");
        AssertEqual(transport.PlayerViewRequestCount, playerViewGets + 1, "接取成功后 player-view 应只主动刷新一次");
        AssertEqual(transport.AvailableActionsRequestCount, actionGets + 1, "接取成功后 available-actions 应只刷新一次");
        AssertTrue(transport.PlayerQuestResponse.Quests!.Any(quest => quest.QuestId == selectedCard.QuestId), "接取成功后的个人任务快照应包含新实例");
        transport.ActionGate = null;
        screen.CloseCardViewer();
    }

    private async Task VerifyAcceptRefreshFailureAsync(GameScreen screen, Core9Transport transport)
    {
        await screen.OpenQuestBoardAsync();
        var actionCount = transport.ActionRequestCount;
        var previousRequestId = transport.LastActionRequest?.RequestId;
        transport.FailNextQuestRequest = true;
        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => !screen.IsQuestAcceptSubmitting && screen.CardViewer.State == CardViewerState.Error);
        AssertEqual(transport.ActionRequestCount, actionCount + 1, "候选刷新失败不能改变已完成的接取提交次数");
        AssertTrue(
            !string.Equals(previousRequestId, transport.LastActionRequest?.RequestId, StringComparison.Ordinal),
            "两次不同接取行动必须生成不同 requestId"
        );
        AssertTrue(screen.CardViewer.ErrorText.Contains("任务已接取", StringComparison.Ordinal), "行动成功但刷新失败必须明确区分");
        var actionCountBeforeRetry = transport.ActionRequestCount;
        screen.CardViewer.RequestRetry();
        await WaitUntilAsync(() => screen.CardViewer.State == CardViewerState.Content);
        AssertEqual(transport.ActionRequestCount, actionCountBeforeRetry, "刷新失败后的重试只能 GET，不能重新接取");
        screen.CloseCardViewer();
    }

    private async Task VerifyAcceptTimeoutAndLateResponseAsync(GameScreen screen, Core9Transport transport)
    {
        await screen.OpenQuestBoardAsync();
        var actionCount = transport.ActionRequestCount;
        transport.FailAcceptAction = true;
        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => !screen.IsQuestAcceptSubmitting);
        AssertEqual(transport.ActionRequestCount, actionCount + 1, "网络超时不得自动创建新身份重发接取行动");
        transport.FailAcceptAction = false;
        screen.CloseCardViewer();

        await screen.OpenQuestBoardAsync();
        transport.ActionGate = new TaskCompletionSource<bool>();
        var personalGetsBeforeLateResponse = transport.PlayerQuestRequestCount;
        screen.CardViewer.GetActionButton(0).EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => screen.IsQuestAcceptSubmitting);
        screen.CloseCardViewer();
        transport.ActionGate.SetResult(true);
        await WaitUntilAsync(() => !screen.IsQuestAcceptSubmitting);
        AssertFalse(screen.CardViewer.IsOpen, "关闭后到达的接取响应不得重新打开弹层");
        AssertEqual(
            transport.PlayerQuestRequestCount,
            personalGetsBeforeLateResponse + 1,
            "主动关闭弹层不应取消已发出的行动及其安全状态同步"
        );
        transport.ActionGate = null;
    }

    private async Task WaitUntilAsync(Func<bool> predicate, int frames = 600)
    {
        for (var index = 0; index < frames; index++)
        {
            if (predicate())
            {
                return;
            }

            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        }

        _failures.Add("等待异步状态超时。");
    }

    private void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            _failures.Add(message);
        }
    }

    private void AssertFalse(bool condition, string message)
    {
        AssertTrue(!condition, message);
    }

    private void AssertEqual<T>(T? actual, T? expected, string message)
    {
        if (!EqualityComparer<T?>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}；实际={actual}，期望={expected}");
        }
    }

    private void AssertSequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string message)
    {
        if (!actual.SequenceEqual(expected))
        {
            _failures.Add($"{message}；实际=[{string.Join(",", actual)}]，期望=[{string.Join(",", expected)}]");
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("CORE_9_CARD_VIEWER_TESTS_PASSED");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
    }

    private sealed class Core9Transport : IApiTransport
    {
        public GameMapDto Map { get; } = RunGameMapTests.CreateValidMap();
        public PlayerViewResponseDto View { get; }
        public QuestOfferListResponseDto QuestResponse { get; }
        public PlayerQuestListResponseDto PlayerQuestResponse { get; }
        public CurrentShopResponseDto ShopResponse { get; }
        public PlayerItemsResponseDto ItemsResponse { get; }
        public int QuestRequestCount { get; private set; }
        public int ShopRequestCount { get; private set; }
        public int PlayerQuestRequestCount { get; private set; }
        public int ItemsRequestCount { get; private set; }
        public int ActionRequestCount { get; private set; }
        public HttpClient.Method LastQuestMethod { get; private set; }
        public HttpClient.Method LastShopMethod { get; private set; }
        public HttpClient.Method LastPlayerQuestMethod { get; private set; }
        public string? LastQuestPlayerId { get; private set; }
        public string? LastShopPlayerId { get; private set; }
        public string? LastPlayerQuestPlayerId { get; private set; }
        public bool LastQuestAuthorized { get; private set; }
        public bool LastShopAuthorized { get; private set; }
        public bool LastPlayerQuestAuthorized { get; private set; }
        public bool FailQuest { get; set; }
        public bool FailAvailableActions { get; set; }
        public bool FailNextQuestRequest { get; set; }
        public bool FailAcceptAction { get; set; }
        public bool AcceptQuestAvailable { get; set; } = true;
        public bool ShopBuyAvailable { get; set; } = true;
        public bool ItemActionsAvailable { get; set; } = true;
        public bool FailNextItemAction { get; set; }
        public bool FailNextShopRequest { get; set; }
        public string? ActionReasonText { get; set; }
        public int AvailableActionsRequestCount { get; private set; }
        public int PlayerViewRequestCount { get; private set; }
        public int WriteRequestCount { get; private set; }
        public GameActionRequestDto? LastActionRequest { get; private set; }
        public TaskCompletionSource<ApiResult<QuestOfferListResponseDto>>? QuestGate { get; set; }
        public TaskCompletionSource<bool>? ActionGate { get; set; }

        public Core9Transport()
        {
            var startCell = Map.Cells![0];
            var targetCell = Map.Cells[1];
            ShopResponse = new CurrentShopResponseDto
            {
                InventoryScope = "GAME",
                InventoryGameId = 77,
                AccessAllowed = true,
                StateVersion = 5,
                ShelfSize = 3,
                Items =
                [
                    NewItem(31, 1003),
                    NewItem(32, 1003),
                    NewItem(33, 1018)
                ]
            };
            QuestResponse = new QuestOfferListResponseDto
            {
                GameId = 77,
                PlayerId = 1001,
                Offers =
                [
                    NewQuest(13, 23, 3, "OFFICIAL_QUEST_023"),
                    NewQuest(11, 6, 1, "OFFICIAL_QUEST_006"),
                    NewQuest(12, 7, 2, "OFFICIAL_QUEST_007"),
                    NewQuest(14, 10, 4, "OFFICIAL_QUEST_010")
                ]
            };
            PlayerQuestResponse = new PlayerQuestListResponseDto
            {
                GameId = 77,
                PlayerId = 1001,
                Quests =
                [
                    NewPlayerQuest(401, 6, "OFFICIAL_QUEST_006"),
                    NewPlayerQuest(402, 6, "OFFICIAL_QUEST_006"),
                    NewPlayerQuest(403, 7, "OFFICIAL_QUEST_007"),
                    NewPlayerQuest(404, 23, "OFFICIAL_QUEST_023"),
                    NewPlayerQuest(405, 25, "OFFICIAL_QUEST_025")
                ],
                CompletedQuests = [NewPlayerQuest(900, 10, "OFFICIAL_QUEST_010")]
            };
            ItemsResponse = new PlayerItemsResponseDto
            {
                GameId = 77,
                PlayerId = 1001,
                TotalCount = 6,
                Items =
                [
                    NewPlayerItem(2001, 1003, "苹果", "CONSUMABLE", "HAND"),
                    NewPlayerItem(2002, 1003, "苹果", "CONSUMABLE", "HAND"),
                    NewPlayerItem(2003, 1017, "药剂", "CONSUMABLE", "HAND"),
                    NewPlayerItem(2004, 1013, "魔法书", "EQUIPMENT", "HAND"),
                    NewPlayerItem(2010, 1018, "蹄铁", "EQUIPMENT", "EQUIPPED"),
                    NewPlayerItem(2011, 1005, "花束", "EQUIPMENT", "EQUIPPED")
                ]
            };
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
                    PublicShop = ShopResponse,
                    QuestSubmissionVersion = 1,
                    ConnectionPolicy = CreateConnectionPolicy()
                },
                Players =
                [
                    NewPlayer(1001, 1, "GPT1", 1, startCell.CellId),
                    NewPlayer(1002, 2, "GPT2", 2, targetCell.CellId)
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
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
            {
                return (ApiResult<T>)(object)ApiResult<GameMapDto>.Success(Map, "success", 200);
            }

            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
            {
                PlayerViewRequestCount++;
                return (ApiResult<T>)(object)ApiResult<PlayerViewResponseDto>.Success(View, "success", 200);
            }

            if (path == "/games/77/players/1001/connection"
                && typeof(T) == typeof(PlayerViewResponseDto)
                && body is PlayerConnectionRequestDto connectionRequest)
            {
                var response = new PlayerViewResponseDto
                {
                    GameId = View.GameId,
                    MapConfigId = View.MapConfigId,
                    GameStatus = View.GameStatus,
                    CurrentRound = View.CurrentRound,
                    CurrentTurnUserId = View.CurrentTurnUserId,
                    CurrentTurnSeq = View.CurrentTurnSeq,
                    StateVersion = View.StateVersion,
                    Players = View.Players,
                    EngineState = new EngineStateDto
                    {
                        BatchId = View.EngineState?.BatchId,
                        PublicShop = View.EngineState?.PublicShop,
                        ConnectionPolicy = CreateConnectionPolicy(),
                        Connection = CreateConnectionInfo(connectionRequest)
                    }
                };
                return (ApiResult<T>)(object)ApiResult<PlayerViewResponseDto>.Success(response, "success", 200);
            }

            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
            {
                AvailableActionsRequestCount++;
                if (FailAvailableActions)
                {
                    return (ApiResult<T>)(object)ApiResult<List<AvailableActionDto>>.Failure(
                        "模拟资格网络错误",
                        "NETWORK_ERROR"
                    );
                }

                return (ApiResult<T>)(object)ApiResult<List<AvailableActionDto>>.Success(
                    [
                        new AvailableActionDto { ActionType = "END_TURN", Available = true },
                        new AvailableActionDto { ActionType = "MOVE_ONE_STEP", Available = true },
                        new AvailableActionDto
                        {
                            ActionType = "ACCEPT_QUEST",
                            Available = AcceptQuestAvailable,
                            ReasonText = AcceptQuestAvailable ? null : ActionReasonText
                        },
                        new AvailableActionDto
                        {
                            ActionType = "SHOP_BUY_ITEM",
                            Available = ShopBuyAvailable,
                            ReasonText = ShopBuyAvailable ? null : ActionReasonText
                        },
                        new AvailableActionDto
                        {
                            ActionType = "USE_ITEM",
                            Available = ItemActionsAvailable,
                            ReasonText = ItemActionsAvailable ? null : ActionReasonText
                        },
                        new AvailableActionDto
                        {
                            ActionType = "EQUIP_ITEM",
                            Available = ItemActionsAvailable,
                            ReasonText = ItemActionsAvailable ? null : ActionReasonText
                        },
                        new AvailableActionDto
                        {
                            ActionType = "UNEQUIP_ITEM",
                            Available = ItemActionsAvailable,
                            ReasonText = ItemActionsAvailable ? null : ActionReasonText
                        }
                    ],
                    "success",
                    200
                );
            }

            if (path == "/games/77/players/1001/movable-cells" && typeof(T) == typeof(List<MapCellDto>))
            {
                return (ApiResult<T>)(object)ApiResult<List<MapCellDto>>.Success([Map.Cells![1]], "success", 200);
            }

            if (path == "/games/77/quest-offers" && typeof(T) == typeof(QuestOfferListResponseDto))
            {
                QuestRequestCount++;
                LastQuestMethod = method;
                LastQuestPlayerId = query != null && query.TryGetValue("playerId", out var playerId) ? playerId : null;
                LastQuestAuthorized = authorized;
                if (QuestGate != null)
                {
                    return (ApiResult<T>)(object)await QuestGate.Task;
                }

                if (FailNextQuestRequest)
                {
                    FailNextQuestRequest = false;
                    return (ApiResult<T>)(object)ApiResult<QuestOfferListResponseDto>.Failure(
                        "模拟接取成功后的候选刷新失败",
                        "NETWORK_ERROR"
                    );
                }

                return FailQuest
                    ? (ApiResult<T>)(object)ApiResult<QuestOfferListResponseDto>.Failure("模拟任务板网络错误", "NETWORK_ERROR")
                    : (ApiResult<T>)(object)ApiResult<QuestOfferListResponseDto>.Success(QuestResponse, "success", 200);
            }

            if (path == "/games/77/quests" && typeof(T) == typeof(PlayerQuestListResponseDto))
            {
                PlayerQuestRequestCount++;
                LastPlayerQuestMethod = method;
                LastPlayerQuestPlayerId = query != null && query.TryGetValue("playerId", out var playerId)
                    ? playerId
                    : null;
                LastPlayerQuestAuthorized = authorized;
                return (ApiResult<T>)(object)ApiResult<PlayerQuestListResponseDto>.Success(
                    PlayerQuestResponse,
                    "success",
                    200
                );
            }

            if (path == "/games/77/shop/current" && typeof(T) == typeof(CurrentShopResponseDto))
            {
                ShopRequestCount++;
                LastShopMethod = method;
                LastShopPlayerId = query != null && query.TryGetValue("playerId", out var playerId) ? playerId : null;
                LastShopAuthorized = authorized;
                if (FailNextShopRequest)
                {
                    FailNextShopRequest = false;
                    return (ApiResult<T>)(object)ApiResult<CurrentShopResponseDto>.Failure(
                        "模拟购买成功后的货架刷新失败",
                        "NETWORK_ERROR"
                    );
                }
                return (ApiResult<T>)(object)ApiResult<CurrentShopResponseDto>.Success(ShopResponse, "success", 200);
            }

            if (path == "/games/77/items" && typeof(T) == typeof(PlayerItemsResponseDto))
            {
                ItemsRequestCount++;
                return (ApiResult<T>)(object)ApiResult<PlayerItemsResponseDto>.Success(ItemsResponse, "success", 200);
            }

            if (path == "/games/77/actions"
                && method == HttpClient.Method.Post
                && typeof(T) == typeof(GameActionResponseDto)
                && body is GameActionRequestDto actionRequest)
            {
                WriteRequestCount++;
                ActionRequestCount++;
                LastActionRequest = actionRequest;
                if (ActionGate != null)
                {
                    await ActionGate.Task;
                }

                if (FailAcceptAction)
                {
                    return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure(
                        "模拟网络超时",
                        "NETWORK_TIMEOUT"
                    );
                }

                if (FailNextItemAction && actionRequest.ActionType is "SHOP_BUY_ITEM" or "USE_ITEM" or "EQUIP_ITEM" or "UNEQUIP_ITEM")
                {
                    FailNextItemAction = false;
                    return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure(
                        "模拟道具行动网络超时",
                        "NETWORK_TIMEOUT"
                    );
                }

                var oldVersion = View.StateVersion;
                GameActionResponseDto response;
                switch (actionRequest.ActionType)
                {
                    case "ACCEPT_QUEST":
                    {
                        var questId = actionRequest.Payload["questId"]?.GetValue<long>() ?? 0;
                        var acceptedOffer = QuestResponse.Offers?.FirstOrDefault(offer => offer.QuestId == questId);
                        if (acceptedOffer == null)
                        {
                            return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure(
                                "候选任务不存在",
                                "ACTION_NOT_AVAILABLE"
                            );
                        }
                        QuestResponse.Offers!.Remove(acceptedOffer);
                        var playerQuests = PlayerQuestResponse.Quests!;
                        var nextPlayerQuestId = playerQuests.Max(quest => quest.PlayerQuestId ?? 0) + 1;
                        playerQuests.Add(NewPlayerQuest(nextPlayerQuestId, acceptedOffer.QuestId!.Value, acceptedOffer.QuestCode!));
                        response = NewActionResponse(oldVersion, "ACCEPT_QUEST");
                        break;
                    }
                    case "SHOP_BUY_ITEM":
                    {
                        var shelfSlotId = actionRequest.Payload["shelfSlotId"]?.GetValue<long>() ?? 0;
                        var shelfItem = ShopResponse.Items?.FirstOrDefault(item => item.ShelfSlotId == shelfSlotId);
                        if (shelfItem?.ItemId is null or <= 0)
                        {
                            return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure("货架实例不存在", "ACTION_NOT_AVAILABLE");
                        }
                        ShopResponse.Items!.Remove(shelfItem);
                        var playerItems = ItemsResponse.Items!;
                        var playerItemId = playerItems.Max(item => item.PlayerItemId ?? 0) + 1;
                        var bought = NewPlayerItem(playerItemId, shelfItem.ItemId.Value, shelfItem.ItemName ?? "道具", ItemTypeFor(shelfItem.ItemId.Value), "HAND");
                        playerItems.Add(bought);
                        ItemsResponse.TotalCount = playerItems.Count;
                        response = NewActionResponse(oldVersion, "SHOP_BUY_ITEM");
                        response.ShopBuy = new ShopBuyResultDto
                        {
                            BoughtItem = new ShopBoughtItemDto
                            {
                                PlayerItemId = playerItemId,
                                ItemId = shelfItem.ItemId,
                                ItemName = shelfItem.ItemName,
                                Location = "HAND",
                                LocationText = "背包"
                            },
                            CoinsAfter = 7,
                            NewShelfItem = null
                        };
                        break;
                    }
                    case "USE_ITEM":
                    case "EQUIP_ITEM":
                    case "UNEQUIP_ITEM":
                    {
                        var playerItemId = actionRequest.Payload["playerItemId"]?.GetValue<long>() ?? 0;
                        var item = ItemsResponse.Items?.FirstOrDefault(candidate => candidate.PlayerItemId == playerItemId);
                        if (item == null)
                        {
                            return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure("持有实例不存在", "ACTION_NOT_AVAILABLE");
                        }
                        long? replacedId = null;
                        if (actionRequest.ActionType == "USE_ITEM")
                        {
                            ItemsResponse.Items!.Remove(item);
                            item.Location = "DISCARDED";
                        }
                        else if (actionRequest.ActionType == "EQUIP_ITEM")
                        {
                            if (actionRequest.Payload["replacePlayerItemId"] is JsonNode replacementNode)
                            {
                                replacedId = replacementNode.GetValue<long>();
                                var replacement = ItemsResponse.Items!.First(candidate => candidate.PlayerItemId == replacedId);
                                replacement.Location = "HAND";
                            }
                            item.Location = "EQUIPPED";
                        }
                        else
                        {
                            item.Location = "HAND";
                        }
                        ItemsResponse.TotalCount = ItemsResponse.Items!.Count;
                        response = NewActionResponse(oldVersion, actionRequest.ActionType);
                        response.ItemUse = new ItemUseResultDto
                        {
                            PlayerItemId = playerItemId,
                            ItemId = item.ItemId,
                            ItemName = item.ItemName,
                            ItemType = item.ItemType,
                            LocationAfter = item.Location,
                            ReplacedPlayerItemId = replacedId
                        };
                        break;
                    }
                    default:
                        return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Failure(
                            $"测试传输层不支持行动 {actionRequest.ActionType}",
                            "UNEXPECTED_ACTION"
                        );
                }
                return (ApiResult<T>)(object)ApiResult<GameActionResponseDto>.Success(response, "success", 200);
            }

            if (method != HttpClient.Method.Get)
            {
                WriteRequestCount++;
            }

            return ApiResult<T>.Failure($"未处理测试请求：{method} {path}", "UNEXPECTED_REQUEST");
        }

        private static QuestOfferDto NewQuest(long offerId, long questId, int slotNo, string questCode)
        {
            return new QuestOfferDto
            {
                OfferId = offerId,
                QuestId = questId,
                SlotNo = slotNo,
                QuestCode = questCode,
                QuestName = questCode
            };
        }

        private static ShopShelfItemDto NewItem(long shelfSlotId, long itemId)
        {
            return new ShopShelfItemDto
            {
                ShelfSlotId = shelfSlotId,
                ItemId = itemId,
                ItemName = $"道具 {itemId}",
                ItemType = ItemTypeFor(itemId)
            };
        }

        private GameActionResponseDto NewActionResponse(long oldVersion, string actionType)
        {
            View.StateVersion++;
            ShopResponse.StateVersion = View.StateVersion;
            return new GameActionResponseDto
            {
                GameId = 77,
                OldStateVersion = oldVersion,
                NewStateVersion = View.StateVersion,
                ActionType = actionType,
                PlayerViewState = View
            };
        }

        private static PlayerItemDto NewPlayerItem(
            long playerItemId,
            long itemId,
            string itemName,
            string itemType,
            string location)
        {
            return new PlayerItemDto
            {
                PlayerItemId = playerItemId,
                ItemId = itemId,
                ItemName = itemName,
                ItemType = itemType,
                ItemTypeText = itemType == "CONSUMABLE" ? "消耗品" : "装备",
                Location = location,
                LocationText = location == "HAND" ? "背包" : "已装备"
            };
        }

        private static string ItemTypeFor(long itemId) => itemId is 1003 or 1017 ? "CONSUMABLE" : "EQUIPMENT";

        private static PlayerQuestDto NewPlayerQuest(long playerQuestId, long questId, string questCode)
        {
            return new PlayerQuestDto
            {
                PlayerQuestId = playerQuestId,
                QuestId = questId,
                QuestCode = questCode,
                QuestName = questCode,
                Objectives =
                [
                    new QuestObjectiveDto
                    {
                        ObjectiveKey = "TEST",
                        Text = "完成测试目标",
                        Completed = false,
                        CurrentValue = 0,
                        RequiredValue = 1
                    }
                ],
                Submission = new QuestSubmissionDto
                {
                    ProtocolVersion = 1,
                    CanSubmit = false,
                    UnavailableReason = "目标尚未完成"
                }
            };
        }

        private static PlayerViewPlayerDto NewPlayer(long playerId, long userId, string nickname, int turnOrder, long cellId)
        {
            return new PlayerViewPlayerDto
            {
                PlayerId = playerId,
                UserId = userId,
                Nickname = nickname,
                TurnOrder = turnOrder,
                CurrentCellId = cellId,
                CurrentSpeedPoints = 3,
                Prestige = 2,
                IsConnected = 1,
                EffectiveAttributes = new EffectiveAttributesDto()
            };
        }

        private static GameConnectionPolicyDto CreateConnectionPolicy()
        {
            return new GameConnectionPolicyDto
            {
                HeartbeatSeconds = 5,
                TimeoutSeconds = 15,
                ReconnectWaitSeconds = 10,
                InitialGraceSeconds = 45
            };
        }

        private static GameConnectionInfoDto CreateConnectionInfo(PlayerConnectionRequestDto request)
        {
            return new GameConnectionInfoDto
            {
                ClientInstanceId = request.ClientInstanceId,
                ConnectionId = request.ConnectionId ?? "core9-card-viewer-connection",
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
        }
    }
}
