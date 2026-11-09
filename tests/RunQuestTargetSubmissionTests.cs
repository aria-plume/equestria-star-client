using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.UI;

public partial class RunQuestTargetSubmissionTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize() => Callable.From(BeginTests).CallDeferred();
    private void BeginTests() => _ = RunTestsAsync();

    private async Task RunTestsAsync()
    {
        VerifyDtoAndModel();
        VerifyRequests();
        var overlay = GD.Load<PackedScene>("res://scenes/game/ui/QuestTargetSubmissionOverlay.tscn")
            .Instantiate<QuestTargetSubmissionOverlay>();
        Root.AddChild(overlay);
        await ToSignal(this, SignalName.ProcessFrame);
        if (OS.GetCmdlineUserArgs().Contains("--visual-check")
            || string.Equals(System.Environment.GetEnvironmentVariable("ES_QUEST_TARGET_VISUAL_CHECK"), "1", StringComparison.Ordinal))
        {
            var context = NewContext();
            var tasks = QuestTargetSubmissionModel.BuildTasks(
                [
                    NewItemQuest(423, 123, "023｜提交魔法类道具", null, "MAGIC"),
                    NewItemQuest(442, 142, "042｜提交指定药剂", 1017, "MAGIC"),
                    NewPayQuest()
                ],
                context.View,
                context.Player,
                context.Actions,
                context.View.StateVersion,
                true,
                false);
            overlay.ShowTasks(tasks, new PlayerItemsResponseDto
            {
                GameId = 77,
                PlayerId = 1001,
                Items =
                [
                    NewItem(801, 1017, "药剂一", "MAGIC", "HAND"),
                    NewItem(802, 1017, "药剂二", "MAGIC", "HAND"),
                    NewItem(803, 1017, "已装备药剂", "MAGIC", "EQUIPPED")
                ]
            }, "本地视觉验收：类别交付、指定交付与资源支付");
            GD.Print("QUEST_TARGET_SUBMISSION_VISUAL_READY");
            return;
        }
        await VerifyRealGuiInputAsync(overlay);
        overlay.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        Finish();
    }

    private void VerifyDtoAndModel()
    {
        const string json = """
        {"code":1,"msg":"success","errorCode":null,"data":{"gameId":77,"playerId":1001,
         "quests":[
          {"playerQuestId":423,"questId":123,"questCode":"OFFICIAL_QUEST_023","questName":"类别交付",
           "status":"IN_PROGRESS","triggerType":"SUBMIT_ITEM","requiredItemId":null,"requiredItemCategory":"MAGIC",
           "requiredCoinCost":0,"requiredPrestigeCost":0,"requiredSocialCost":0,"conditions":[]},
          {"playerQuestId":442,"questId":142,"questCode":"OFFICIAL_QUEST_042","questName":"指定交付",
           "status":"IN_PROGRESS","triggerType":"SUBMIT_ITEM","requiredItemId":1017,"requiredItemCategory":"MAGIC",
           "requiredCoinCost":0,"requiredPrestigeCost":0,"requiredSocialCost":0,"conditions":[]},
          {"playerQuestId":426,"questId":126,"questCode":"OFFICIAL_QUEST_026","questName":"支付资源",
           "status":"IN_PROGRESS","triggerType":"PAY_RESOURCE","requiredItemId":null,"requiredItemCategory":null,
           "requiredCoinCost":3,"requiredPrestigeCost":1,"requiredSocialCost":2,"conditions":[]}
         ],"completedQuests":[]}}
        """;
        var parsed = ApiClient.ParseHttpResult<PlayerQuestListResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(json),
            "/games/77/quests");
        AssertTrue(parsed.Ok && parsed.Data?.Quests?.Count == 3, "完整任务目标字段必须通过 ApiClient 包装解析");
        var specified = parsed.Data!.Quests!.Single(quest => quest.QuestCode == "OFFICIAL_QUEST_042");
        AssertEqual(specified.RequiredItemId, 1017L, "requiredItemId 解析错误");
        AssertEqual(specified.RequiredItemCategory, "MAGIC", "requiredItemCategory 解析错误");
        var pay = parsed.Data.Quests!.Single(quest => quest.QuestCode == "OFFICIAL_QUEST_026");
        AssertEqual(pay.RequiredCoinCost, 3, "requiredCoinCost 解析错误");
        AssertEqual(pay.RequiredPrestigeCost, 1, "requiredPrestigeCost 解析错误");
        AssertEqual(pay.RequiredSocialCost, 2, "requiredSocialCost 解析错误");

        var context = NewContext();
        var tasks = QuestTargetSubmissionModel.BuildTasks(
            parsed.Data.Quests,
            context.View,
            context.Player,
            context.Actions,
            context.View.StateVersion,
            true,
            false);
        AssertEqual(tasks.Count, 3, "必须识别 SUBMIT_ITEM 与 PAY_RESOURCE 的 IN_PROGRESS 任务");
        var categoryTask = tasks.Single(task => task.PlayerQuestId == 423);
        var specifiedTask = tasks.Single(task => task.PlayerQuestId == 442);
        var items = new List<PlayerItemDto>
        {
            NewItem(801, 1017, "药剂一", "MAGIC", "HAND"),
            NewItem(802, 1017, "药剂二", "MAGIC", "HAND"),
            NewItem(803, 1017, "已装备药剂", "MAGIC", "EQUIPPED"),
            NewItem(804, 1017, "类别不匹配", "FOOD", "HAND"),
            NewItem(805, 1003, "类别匹配", "MAGIC", "HAND")
        };
        var categoryMatches = QuestTargetSubmissionModel.BuildMatchingItems(categoryTask, items);
        AssertEqual(categoryMatches.Count, 4, "类别任务必须保留同名实例及匹配的已装备实例");
        var exactMatches = QuestTargetSubmissionModel.BuildMatchingItems(specifiedTask, items);
        AssertEqual(exactMatches.Count, 3, "同时要求 itemId 与类别时必须按 AND 匹配");
        AssertEqual(exactMatches.Count(item => item.CanSubmit), 2, "只有匹配的 HAND 实例可提交");
        AssertFalse(exactMatches.Single(item => item.PlayerItemId == 803).CanSubmit,
            "匹配的 EQUIPPED 实例只能展示，不能提交");

        var anyQuest = NewItemQuest(430, 130, "任选道具", null, null);
        var anyTask = QuestTargetSubmissionModel.BuildTasks(
            [anyQuest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertEqual(QuestTargetSubmissionModel.BuildMatchingItems(anyTask, items).Count, 5,
            "服务端明确没有 itemId 与类别要求时才按任选处理");

        var payTask = tasks.Single(task => task.PlayerQuestId == 426);
        AssertTrue(payTask.CanExecute, "余额和行动资格满足时资源支付应可确认");
        context.Player.Coins = 2;
        var insufficient = QuestTargetSubmissionModel.BuildTasks(
            [pay], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertFalse(insufficient.CanExecute, "金币不足时资源支付必须置灰");
        AssertTrue(insufficient.UnavailableReason.Contains("金币不足"), "资源不足应显示明确原因");
    }

    private void VerifyRequests()
    {
        var context = NewContext();
        var itemTask = QuestTargetSubmissionModel.BuildTasks(
            [NewItemQuest(423, 123, "类别交付", null, "MAGIC")],
            context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        var item = QuestTargetSubmissionModel.BuildMatchingItems(
            itemTask, [NewItem(801, 1017, "药剂", "MAGIC", "HAND")]).Single();
        var itemRequestId = Guid.NewGuid().ToString("D");
        AssertTrue(QuestTargetSubmissionModel.TryCreateItemRequest(
            itemTask, item, 1001, 12, itemRequestId, out var itemRequest, out _),
            "合法手牌应生成 SUBMIT_QUEST_ITEM 请求");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(itemRequest)))
        {
            var root = json.RootElement;
            AssertEqual(root.GetProperty("requestId").GetString(), itemRequestId, "道具交付 requestId 错误");
            AssertEqual(root.GetProperty("playerId").GetInt64(), 1001L, "道具交付必须使用局内 playerId");
            AssertEqual(root.GetProperty("stateVersion").GetInt64(), 12L, "道具交付必须使用最新 stateVersion");
            AssertEqual(root.GetProperty("actionType").GetString(), "SUBMIT_QUEST_ITEM", "道具交付 actionType 错误");
            var payload = root.GetProperty("payload");
            AssertEqual(payload.EnumerateObject().Count(), 3, "道具交付 payload 只能包含三个契约字段");
            AssertEqual(payload.GetProperty("questId").GetInt64(), 123L, "道具交付 questId 错误");
            AssertEqual(payload.GetProperty("playerQuestId").GetInt64(), 423L, "道具交付 playerQuestId 错误");
            AssertEqual(payload.GetProperty("playerItemId").GetInt64(), 801L, "道具交付必须使用持有实例 ID");
        }

        var payQuest = NewPayQuest();
        var payTask = QuestTargetSubmissionModel.BuildTasks(
            [payQuest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertTrue(QuestTargetSubmissionModel.TryCreateResourceRequest(
            payTask, 1001, 12, Guid.NewGuid().ToString("D"), out var payRequest, out _),
            "合法资源任务应生成 PAY_QUEST_RESOURCE 请求");
        AssertEqual(payRequest.Payload.Count, 2, "资源支付 payload 不得携带客户端自定金额");
        AssertTrue(payRequest.Payload.ContainsKey("questId") && payRequest.Payload.ContainsKey("playerQuestId"),
            "资源支付 payload 必须只定位任务定义和任务实例");
    }

    private async Task VerifyRealGuiInputAsync(QuestTargetSubmissionOverlay overlay)
    {
        var context = NewContext();
        var itemQuest = NewItemQuest(423, 123, "类别交付", null, "MAGIC");
        var payQuest = NewPayQuest();
        var tasks = QuestTargetSubmissionModel.BuildTasks(
            [itemQuest, payQuest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false);
        var items = new PlayerItemsResponseDto
        {
            GameId = 77,
            PlayerId = 1001,
            Items =
            [
                NewItem(801, 1017, "药剂", "MAGIC", "HAND"),
                NewItem(802, 1017, "装备中的药剂", "MAGIC", "EQUIPPED")
            ]
        };
        var itemWrites = 0;
        var resourceWrites = 0;
        overlay.ItemSubmitRequested += (_, _, _) => itemWrites++;
        overlay.ResourceSubmitRequested += (_, _) => resourceWrites++;
        overlay.ShowTasks(tasks, items);
        await ToSignal(this, SignalName.ProcessFrame);

        await DispatchViewportClickAsync(overlay.TaskActionButtons[0]);
        AssertEqual(overlay.State, QuestTargetSubmissionOverlayState.ItemSelection,
            "即使只有一张可交手牌，道具任务也必须先打开选卡页");
        AssertEqual(overlay.ItemCards.Count, 2, "选卡页必须同时展示匹配的 HAND 与 EQUIPPED 实例");
        var handCard = overlay.ItemCards.Single(card => card.PlayerItemId == 801);
        var equippedCard = overlay.ItemCards.Single(card => card.PlayerItemId == 802);
        AssertFalse(handCard.ActionButton.Disabled, "符合资格的 HAND 实例应显示可用提交按钮");
        AssertFalse(equippedCard.GetNode<Control>("ActionArea").Visible,
            "匹配的 EQUIPPED 实例不得显示提交按钮");
        await DispatchViewportClickAsync(handCard.ActionButton);
        AssertEqual(itemWrites, 1, "真实 GUI 按下/释放必须恰好产生一次道具交付请求事件");
        AssertTrue(overlay.IsOpen, "道具提交按钮不得关闭弹层或穿透地图");

        overlay.ShowTasks(tasks, items);
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchViewportClickAsync(overlay.TaskActionButtons[1]);
        AssertEqual(overlay.State, QuestTargetSubmissionOverlayState.ConfirmingResource,
            "资源支付必须先进入确认层");
        await DispatchViewportClickAsync(overlay.CancelButton);
        AssertEqual(resourceWrites, 0, "取消资源支付必须产生零写请求");
        await DispatchViewportClickAsync(overlay.TaskActionButtons[1]);
        await DispatchViewportClickAsync(overlay.ConfirmButton);
        AssertEqual(resourceWrites, 1, "确认资源支付必须通过真实 GUI 恰好发出一次请求事件");
        AssertTrue(overlay.GetNode<Control>("OverlayRoot").IsInGroup("blocks_map_input"),
            "任务提交弹层必须保持 blocks_map_input 输入隔离");
        overlay.CloseOverlay(false);
    }

    private static (PlayerViewResponseDto View, PlayerViewPlayerDto Player, List<AvailableActionDto> Actions) NewContext()
    {
        var player = new PlayerViewPlayerDto
        {
            PlayerId = 1001,
            UserId = 1,
            Coins = 8,
            Prestige = 3,
            CurrentSocialPoints = 4
        };
        var view = new PlayerViewResponseDto
        {
            GameId = 77,
            GameStatus = "RUNNING",
            CurrentTurnUserId = 1,
            StateVersion = 12,
            EngineState = new EngineStateDto { Phase = "MIDDLE", CurrentPlayerId = 1001 },
            Players = [player]
        };
        var actions = new List<AvailableActionDto>
        {
            new() { ActionType = QuestTargetSubmissionModel.SubmitItemAction, Available = true },
            new() { ActionType = QuestTargetSubmissionModel.PayResourceAction, Available = true }
        };
        return (view, player, actions);
    }

    private static PlayerQuestDto NewItemQuest(
        long playerQuestId, long questId, string name, long? requiredItemId, string? category) => new()
    {
        PlayerQuestId = playerQuestId,
        QuestId = questId,
        QuestName = name,
        Status = "IN_PROGRESS",
        TriggerType = QuestTargetSubmissionModel.SubmitItemTrigger,
        RequiredItemId = requiredItemId,
        RequiredItemCategory = category,
        RequiredCoinCost = 0,
        RequiredPrestigeCost = 0,
        RequiredSocialCost = 0,
        Conditions = []
    };

    private static PlayerQuestDto NewPayQuest() => new()
    {
        PlayerQuestId = 426,
        QuestId = 126,
        QuestName = "资源支付",
        Status = "IN_PROGRESS",
        TriggerType = QuestTargetSubmissionModel.PayResourceTrigger,
        RequiredCoinCost = 3,
        RequiredPrestigeCost = 1,
        RequiredSocialCost = 2,
        Conditions = []
    };

    private static PlayerItemDto NewItem(
        long playerItemId, long itemId, string name, string category, string location) => new()
    {
        PlayerItemId = playerItemId,
        ItemId = itemId,
        ItemName = name,
        ItemCategory = category,
        Location = location
    };

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

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("QUEST TARGET SUBMISSION TESTS PASSED");
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

    private void AssertEqual<T>(T? actual, T? expected, string message)
    {
        if (!EqualityComparer<T?>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}：expected={expected}, actual={actual}");
        }
    }
}
