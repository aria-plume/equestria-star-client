using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.UI;
using EquestriaStar.State;
using EquestriaStar.UI;
using Godot;

public partial class RunQuestSystemTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize() => Callable.From(BeginTests).CallDeferred();
    private void BeginTests() => _ = RunTestsAsync();

    private async Task RunTestsAsync()
    {
        try
        {
            await RunTestsCoreAsync();
        }
        catch (Exception exception)
        {
            GD.PushError($"Quest system test exception: {exception}");
            Quit(1);
        }
    }

    private async Task RunTestsCoreAsync()
    {
        VerifyModelsAndSerialization();

        var session = Root.GetNode<Session>("Session");
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new QuestSystemTransport();
        apiClient.SetTransportForTests(transport);
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "quest-system-token",
            User = new UserDto { UserId = 1, Username = "gpt1", Nickname = "GPT1" }
        });
        session.SetCurrentRoom(10, "任务系统测试房");
        session.SetCurrentGame(77);

        var screen = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn").Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        await WaitUntilAsync(() => screen.LatestPlayerView != null);
        await screen.ConnectionLease.CurrentRequestTask;
        await screen.RefreshAvailableActionsAsync(true);
        AssertTrue(screen.IsGameConnectionReady,
            $"测试连接应建立，实际状态={screen.ConnectionLease.State}，消息={screen.ConnectionLease.StatusMessage}");
        screen.GetNode<Timer>("PlayerViewTimer").Stop();

        AssertTrue(screen.SupportsQuestSubmission, "新鲜 player-view 的 questSubmissionVersion=1 应启用提交协议");
        await screen.OpenQuestJournalAsync();
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Tasks);
        AssertEqual(screen.QuestJournal.DisplayedTaskCount, 2, "个人任务应保留服务端顺序与任务实例");
        AssertTrue(screen.QuestJournal.GetNode<Control>("Root").IsInGroup("blocks_map_input"), "任务弹层必须阻止地图输入");

        // 结构解析失败必须保留错误与重试状态，不能伪装成空任务列表。
        screen.QuestJournal.ForceClose();
        transport.FailNextQuestReadWithTypeMismatch = true;
        await screen.OpenQuestJournalAsync();
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Error);
        var parseErrorText = screen.QuestJournal
            .GetNode<Label>("Root/Center/Panel/Margin/Content/Error/ErrorLabel").Text;
        AssertTrue(parseErrorText.Contains("接口结构不匹配") && parseErrorText.Contains("objectiveNotes"),
            "任务结构类型错误必须显示明确字段路径，不能显示空任务列表");
        if (OS.GetCmdlineUserArgs().Contains("--visual-parse-error-check"))
        {
            GD.Print("QUEST_PARSE_ERROR_VISUAL_CHECK_READY");
            return;
        }
        screen.QuestJournal.ForceClose();
        await screen.OpenQuestJournalAsync();
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Tasks);

        var taskList = screen.QuestJournal.GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/TaskScroll/TaskList");
        var firstCard = taskList.GetChild<QuestTaskCard>(0);
        var objectiveRows = firstCard.GetNode<VBoxContainer>("Margin/Content/Objectives");
        var objectiveText = objectiveRows.GetChild<HBoxContainer>(0).GetChild<Label>(1).Text;
        var checkText = objectiveRows.GetChild<HBoxContainer>(0).GetChild<Label>(0).Text;
        AssertTrue(objectiveText.Contains("（2/5）"), "累计目标必须展示服务端 currentValue/requiredValue");
        AssertEqual(checkText, "☑", "completed=true 必须显示红色勾选框");

        if (OS.GetCmdlineUserArgs().Contains("--visual-reward-check"))
        {
            transport.RestoreFullBagReward();
            await screen.RefreshPlayerViewAsync();
            screen.QuestJournal.ShowReward(transport.View.EngineState!.PendingReward!, 1001);
            GD.Print("ITEM_REWARD_VISUAL_CHECK_READY");
            return;
        }

        if (OS.GetCmdlineUserArgs().Contains("--visual-check"))
        {
            GD.Print("QUEST_SYSTEM_VISUAL_CHECK_READY");
            return;
        }

        var submitButton = firstCard.GetNode<Button>("Margin/Content/Actions/SubmitButton");
        AssertFalse(submitButton.Disabled, "submission.canSubmit=true 时提交按钮应可用");
        var actionBefore = transport.ActionCount;
        await DispatchViewportClickAsync(submitButton);
        await DispatchViewportClickAsync(submitButton);
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Reward);
        AssertEqual(transport.ActionCount, actionBefore + 1, "真实 GUI 快速重复点击只能提交一次任务行动");
        AssertEqual(transport.LastAction?.ActionType, "SUBMIT_QUEST", "任务提交必须使用 SUBMIT_QUEST");
        AssertPayload(transport.LastAction, "playerQuestId", 401, "提交任务 payload 只能使用 playerQuestId");
        AssertValidRequestIdentity(transport.LastAction, 1001, "任务提交");
        var settlementText = screen.QuestJournal.GetNode<Label>("Root/Center/Panel/Margin/Content/Settlement/SettlementLabel").Text;
        AssertTrue(settlementText.Contains("实际金币：5"), "结算页必须使用 questSettlement.coinsGranted");
        AssertTrue(settlementText.Contains("伙伴金币加成（已包含）：2"), "伙伴加成只能作为已包含明细展示");
        AssertTrue(settlementText.Contains("金币余额：15"), "结算页必须使用 coinsAfter，不用余额差推算");

        var rewardList = screen.QuestJournal.GetNode<HBoxContainer>("Root/Center/Panel/Margin/Content/Settlement/RewardScroll/RewardList");
        var rewardButton = rewardList.GetChild<VBoxContainer>(0).GetChildren().OfType<Button>().Single();
        await DispatchViewportClickAsync(rewardButton);
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Reward
            && transport.View.EngineState!.PendingReward?.RewardId == 72);
        AssertEqual(transport.LastAction?.ActionType, "RESOLVE_TURN_START_EFFECT", "领取必须复用 FIFO 处理行动");
        AssertPayload(transport.LastAction, "rewardId", 71, "领取必须携带当前队首 rewardId");
        AssertPayload(transport.LastAction, "cardId", 901, "领取必须携带候选 cardId，不能用 itemId 代替");
        AssertEqual(transport.LastAction?.Payload.Count, 2, "领取 payload 只能包含 rewardId/cardId");
        AssertEqual(transport.ItemsRequestCount, 1, "领取成功后必须重新 GET /items");
        AssertTrue(screen.QuestInventorySnapshot?.Items?.Any(item => item.PlayerItemId == 8801 && item.ItemId == 1003) == true,
            "领取后的真实 playerItemId 必须来自 GET /items，不能使用 cardId");

        // 处理完本人队首后必须立即切换到新鲜 FIFO 队首，而不是本地清空。
        var secondRewardButton = rewardList.GetChild<VBoxContainer>(0).GetChildren().OfType<Button>().Single();
        await DispatchViewportClickAsync(secondRewardButton);
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Tasks);
        AssertPayload(transport.LastAction, "rewardId", 72, "连续 FIFO 必须使用新鲜队首 rewardId");
        AssertPayload(transport.LastAction, "cardId", 902, "连续 FIFO 必须使用新鲜候选 cardId");
        AssertEqual(transport.ItemsRequestCount, 2, "每次领取成功都必须重新 GET /items");
        AssertTrue(screen.QuestInventorySnapshot?.Items?.Any(item => item.PlayerItemId == 8802 && item.ItemId == 1017) == true,
            "第二个同流程奖励也必须采用背包接口返回的真实实例 ID");

        var remainingCard = screen.QuestJournal.GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/TaskScroll/TaskList")
            .GetChild<QuestTaskCard>(0);
        var abandonButton = remainingCard.GetNode<Button>("Margin/Content/Actions/AbandonButton");
        AssertFalse(abandonButton.Disabled, "声望大于等于1时放弃按钮应亮起");
        abandonButton.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(this, SignalName.ProcessFrame);
        var confirmYes = screen.QuestJournal.GetNode<Button>("Root/ConfirmLayer/Center/Panel/Margin/Content/Actions/YesButton");
        AssertTrue(screen.QuestJournal.GetNode<Control>("Root/ConfirmLayer").Visible, "放弃任务必须先显示二次确认");
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
        // Dummy 渲染器下隐藏容器的全局坐标恒为零，使用 Viewport 焦点键事件验证确认链路。
        // 任务卡的提交按钮仍通过 Viewport 真实鼠标按下/释放验证。
        await DispatchFocusedAcceptAsync(confirmYes);
        await WaitUntilAsync(
            () => screen.QuestJournal.State == QuestJournalState.Tasks && screen.QuestJournal.DisplayedTaskCount == 0,
            $"放弃确认后未刷新任务；状态={screen.QuestJournal.State}，数量={screen.QuestJournal.DisplayedTaskCount}，提交中={screen.IsQuestSystemSubmitting}，最后行动={transport.LastAction?.ActionType ?? "<null>"}"
        );
        AssertEqual(transport.LastAction?.ActionType, "ABANDON_QUEST", "确认后才可提交 ABANDON_QUEST");
        AssertPayload(transport.LastAction, "questId", 14, "放弃必须携带 questId");
        AssertPayload(transport.LastAction, "playerQuestId", 402, "放弃必须携带 playerQuestId");

        // 配置身份冲突必须显示异常并刷新权威视图，不能退回 cardId/itemId 兜底。
        transport.RestoreInvalidReward();
        await screen.RefreshPlayerViewAsync();
        var beforeInvalidRefresh = transport.PlayerViewRequestCount;
        var beforeInvalidActions = transport.ActionCount;
        screen.QuestJournal.ShowReward(transport.View.EngineState!.PendingReward!, 1001);
        await WaitUntilAsync(() => transport.PlayerViewRequestCount > beforeInvalidRefresh);
        AssertEqual(transport.ActionCount, beforeInvalidActions, "异常候选刷新不得提交奖励行动");

        // 满包整理只读取实例列表，重复 itemId 不得聚合；丢弃使用 playerItemId。
        transport.RestoreFullBagReward();
        await screen.RefreshPlayerViewAsync();
        screen.QuestJournal.ShowReward(transport.View.EngineState!.PendingReward!, 1001);
        var fullRewardColumn = screen.QuestJournal
            .GetNode<HBoxContainer>("Root/Center/Panel/Margin/Content/Settlement/RewardScroll/RewardList")
            .GetChild<VBoxContainer>(0);
        var fullRewardName = fullRewardColumn.GetChildren().OfType<Label>().Single().Text;
        var fullRewardButton = fullRewardColumn.GetChildren().OfType<Button>().Single();
        var abandonRewardButton = screen.QuestJournal.GetNode<Button>("Root/Center/Panel/Margin/Content/Settlement/RewardActions/AbandonRewardButton");
        AssertTrue(fullRewardName.Contains("药剂"), "满包候选仍须显示 configuration.item_name");
        AssertTrue(fullRewardButton.Disabled, "候选 canExecute=false 时领取必须禁用");
        AssertFalse(abandonRewardButton.Disabled, "候选领取禁用不能连带禁用服务端允许的放弃奖励");
        var beforeDisabledClick = transport.ActionCount;
        await DispatchViewportClickAsync(fullRewardButton);
        AssertEqual(transport.ActionCount, beforeDisabledClick, "灰色领取按钮不得提交行动");
        AssertEqual(screen.QuestJournal.State, QuestJournalState.Reward, "灰色领取按钮点击后应保留奖励弹层");
        await screen.ResolveRewardAsync(73, 131);
        AssertEqual(transport.ActionCount, beforeDisabledClick, "提交入口也必须复用候选资格，不能绕过灰色按钮");
        var inventoryButton = screen.QuestJournal.GetNode<Button>("Root/Center/Panel/Margin/Content/Settlement/RewardActions/InventoryButton");
        inventoryButton.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(this, SignalName.ProcessFrame);
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Inventory);
        var inventoryList = screen.QuestJournal.GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/InventoryScroll/InventoryList");
        AssertEqual(inventoryList.GetChildCount(), 2, "同 itemId 的两个持有实例不得聚合");
        var discardButton = inventoryList.GetChild<HBoxContainer>(0).GetChild<Button>(inventoryList.GetChild<HBoxContainer>(0).GetChildCount() - 1);
        discardButton.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchFocusedAcceptAsync(confirmYes);
        await WaitUntilAsync(
            () => !screen.IsQuestSystemSubmitting && transport.LastAction?.ActionType == "DISCARD_ITEM",
            $"丢弃确认后未提交；状态={screen.QuestJournal.State}，提交中={screen.IsQuestSystemSubmitting}，最后行动={transport.LastAction?.ActionType ?? "<null>"}"
        );
        AssertPayload(transport.LastAction, "playerItemId", 801, "丢弃必须使用持有实例 playerItemId");
        AssertEqual(transport.Items.Items!.Count, 1, "丢弃成功后应重新 GET 背包并采用服务端列表");

        // 超时不得自动生成新 requestId 或重发。
        transport.FailNextActionWithTimeout = true;
        transport.RestoreOneSubmittableQuest();
        screen.QuestJournal.ForceClose();
        await screen.RefreshPlayerViewAsync();
        await screen.OpenQuestJournalAsync();
        var beforeTimeout = transport.ActionCount;
        await screen.SubmitQuestAsync(403);
        await WaitUntilAsync(() => screen.QuestJournal.State == QuestJournalState.Error);
        AssertEqual(transport.ActionCount, beforeTimeout + 1, "网络超时不得自动重发任务行动");
        AssertTrue(screen.UnresolvedQuestAction?.RequestId == transport.LastAction?.RequestId, "未知结果必须保留原 requestId 和完整请求");

        screen.Stop();
        screen.QueueFree();
        apiClient.ClearTransportForTests();
        session.ClearAuth();
        await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Finish();
    }

    private void VerifyModelsAndSerialization()
    {
        VerifyDocumentedQuestResponseParsing();

        var missingProgress = new PlayerQuestDto
        {
            PlayerQuestId = 1,
            QuestId = 2,
            QuestName = "字段缺失任务",
            Objectives = [new QuestObjectiveDto { Text = "未知类型目标", Completed = false }],
            Submission = new QuestSubmissionDto { CanSubmit = false, UnavailableReason = "未完成" }
        };
        AssertTrue(QuestSystemModel.TryBuildTask(missingProgress, true, 0, out var display, out _), "缺少累计字段仍应显示任务，而不是伪造进度");
        AssertEqual(display.Objectives[0].ProgressText, "（进度数据不可用）", "缺少累计字段不得伪造0或降级成0/1");
        AssertFalse(display.CanAbandon, "声望不足时放弃按钮应置灰");

        var request = new GameActionRequestDto
        {
            RequestId = Guid.NewGuid().ToString("D"),
            PlayerId = 1001,
            StateVersion = 8,
            ActionType = "SUBMIT_QUEST",
            Payload = new JsonObject { ["playerQuestId"] = 401 }
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
        AssertEqual(json.RootElement.GetProperty("payload").EnumerateObject().Count(), 1, "SUBMIT_QUEST payload 只能包含 playerQuestId");
        AssertTrue(Guid.TryParse(json.RootElement.GetProperty("requestId").GetString(), out _), "任务行动 requestId 必须是有效 UUID");

        var fixturePath = ProjectSettings.GlobalizePath("res://tests/fixtures/item_reward_candidate_full.json");
        var fixture = JsonSerializer.Deserialize<PendingRewardDto>(File.ReadAllText(fixturePath));
        AssertTrue(fixture?.Candidates?.Count == 1, "专项第5节完整 ITEM JSON 必须可原样解析");
        var fixtureCandidate = fixture!.Candidates![0];
        AssertEqual(fixtureCandidate.CardId, 131, "领取实卡 ID 必须读取 cardId");
        AssertEqual(fixtureCandidate.ItemId, 1017, "卡图种类必须读取 itemId");
        AssertEqual(fixtureCandidate.Configuration?.ItemName, "药剂", "名称必须来自 configuration.item_name");
        AssertEqual(fixtureCandidate.Configuration?.ItemType, "CONSUMABLE", "类型必须来自 configuration.item_type");
        AssertEqual(fixtureCandidate.Configuration?.Enabled, 1, "enabled 必须按整数解析");
        AssertEqual(fixtureCandidate.Configuration?.Purchasable, 1, "purchasable 必须按整数解析");
        AssertTrue(fixtureCandidate.Configuration?.EffectConfig == null, "effect_config=null 必须按可空字符串解析");
        AssertTrue(CardArtCatalog.TryGetItemTexture(1017, out var potionTexture, out _) && potionTexture != null,
            "药剂卡图必须按 itemId=1017 映射");
        AssertTrue(QuestSystemModel.TryBuildItemRewardCandidate(fixture, fixtureCandidate, 5, out var fixtureDisplay, out _),
            "完整满包样例结构应有效并可展示");
        AssertEqual(fixtureDisplay.ItemName, "药剂", "候选显示名称必须使用冻结配置");
        AssertFalse(fixtureDisplay.CanExecute, "满包样例不能因 canResolve=true 错误启用领取");
        AssertTrue(fixture?.CanAbandon == true, "满包领取禁用时服务端允许的放弃仍须保留");

        var queueTrueCandidateFalse = CloneRewardForEligibility(true, false);
        AssertFalse(QuestSystemModel.CanResolveItemCandidate(
            queueTrueCandidateFalse,
            queueTrueCandidateFalse.Candidates![0],
            5,
            out _), "队首 true、候选 false 时不得领取");
        var queueFalseCandidateTrue = CloneRewardForEligibility(false, true);
        AssertFalse(QuestSystemModel.CanResolveItemCandidate(
            queueFalseCandidateTrue,
            queueFalseCandidateTrue.Candidates![0],
            5,
            out _), "队首 false、候选 true 时不得领取");

        var missingDrawResult = CloneRewardForEligibility(true, true);
        missingDrawResult.DrawResult = null;
        AssertTrue(QuestSystemModel.CanResolveItemCandidate(
            missingDrawResult,
            missingDrawResult.Candidates![0],
            5,
            out _), "drawResult 缺失不能影响结构完整的 ITEM 候选领取资格");
        var missingCandidateCapacity = CloneRewardForEligibility(true, true);
        missingCandidateCapacity.Candidates![0].CapacitySatisfied = null;
        AssertFalse(QuestSystemModel.TryBuildItemRewardCandidate(
            missingCandidateCapacity,
            missingCandidateCapacity.Candidates[0],
            5,
            out _,
            out _), "本人候选 capacitySatisfied 缺失必须按异常处理");

        var conflict = CloneRewardForEligibility(true, true);
        conflict.Candidates![0].Configuration!.Id = 9999;
        AssertFalse(QuestSystemModel.TryBuildItemRewardCandidate(conflict, conflict.Candidates[0], 5, out _, out _),
            "configuration.id 与 itemId 冲突必须拒绝显示和提交");
        var invalidIdentity = CloneRewardForEligibility(true, true);
        invalidIdentity.Candidates![0].CardId = 0;
        AssertFalse(QuestSystemModel.TryBuildItemRewardCandidate(invalidIdentity, invalidIdentity.Candidates[0], 5, out _, out _),
            "非法必要 ID 不得用其他 ID 兜底");

        var otherPlayer = new PendingRewardDto
        {
            RewardId = 9,
            PlayerId = 99,
            Kind = "ITEM",
            CanResolve = false,
            HasAuthority = false,
            CapacitySatisfied = null,
            CanExecute = false,
            CanAbandon = false,
            Candidates = [],
            DrawResult = null
        };
        AssertFalse(QuestSystemModel.IsOwnPendingReward(otherPlayer, 5), "其他玩家空候选队首不能误认成本人奖励");
        AssertFalse(QuestSystemModel.IsOwnPendingReward(null, 5), "pendingReward=null 必须表示没有队首");
    }

    private void VerifyDocumentedQuestResponseParsing()
    {
        var documentPath = ProjectSettings.GlobalizePath("res://docs/接口交接/普通任务主动提交_隔离HTTP样例.md");
        var markdown = File.ReadAllText(documentPath, Encoding.UTF8);
        const string fenceStart = "```json";
        var start = markdown.IndexOf(fenceStart, StringComparison.Ordinal);
        var end = start < 0 ? -1 : markdown.IndexOf("```", start + fenceStart.Length, StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "隔离 HTTP 文档必须包含可读取的原样 JSON 代码块");
        if (start < 0 || end <= start)
        {
            return;
        }

        var documentedJson = markdown[(start + fenceStart.Length)..end].Trim();
        var documentedRoot = JsonNode.Parse(documentedJson)?.AsObject();
        var currentQuest = documentedRoot?["achieved014"]?.DeepClone();
        var completedQuest = documentedRoot?["submitted014"]?["questUpdates"]?[0]?.DeepClone();
        AssertTrue(currentQuest != null && completedQuest != null, "隔离 HTTP 原样 JSON 应包含当前及完成任务快照");
        if (currentQuest == null || completedQuest == null)
        {
            return;
        }

        var parsed = ParseQuestEnvelope(currentQuest, completedQuest, "/games/77/quests");
        AssertTrue(parsed.Ok && parsed.Data != null, "文档原样任务对象必须通过 ApiClient 包装解析链路");
        AssertEqual(parsed.Data?.Quests?.Count, 1, "当前任务数组应完整解析");
        AssertEqual(parsed.Data?.CompletedQuests?.Count, 1, "已完成任务数组应完整解析");
        AssertEqual(parsed.Data?.Quests?[0].Objectives?[0].CurrentValue, 2, "当前任务 objectives 进度应解析");
        AssertEqual(parsed.Data?.Quests?[0].Conditions?.Count, 3, "当前任务 conditions 应解析");
        AssertFalse(parsed.Data?.Quests?[0].Submission?.CanSubmit ?? true, "当前任务 submission.canSubmit 应解析");
        var completed = parsed.Data?.CompletedQuests?[0];
        AssertEqual(completed?.ObjectiveNotes?.Count, 0, "objectiveNotes 空数组必须解析为空字符串列表");
        AssertEqual(completed?.Submission?.RewardPreview?.PartnerCoins, 0, "rewardPreview 必须解析 partnerCoins");
        AssertEqual(completed?.Submission?.RewardPreview?.ItemCount, 0, "rewardPreview 必须解析 itemCount");
        AssertEqual(completed?.Submission?.RewardPaid?.PartnerCoins, 0, "rewardPaid 必须解析 partnerCoins");
        AssertEqual(completed?.Submission?.RewardPaid?.PendingItemRewardCount, 0,
            "rewardPaid 必须解析 pendingItemRewardCount，不能与预览 itemCount 混用");

        var notesQuest = completedQuest.DeepClone();
        notesQuest!["objectiveNotes"] = new JsonArray("说明一", "说明二");
        var notesParsed = ParseQuestEnvelope(currentQuest.DeepClone(), notesQuest, "/games/77/quests");
        AssertEqual(notesParsed.Data?.CompletedQuests?[0].ObjectiveNotes?[1], "说明二",
            "objectiveNotes 非空字符串数组必须按顺序解析");

        var nullNotesQuest = completedQuest.DeepClone();
        nullNotesQuest!["objectiveNotes"] = null;
        var nullNotesParsed = ParseQuestEnvelope(currentQuest.DeepClone(), nullNotesQuest, "/games/77/quests");
        AssertTrue(nullNotesParsed.Ok && nullNotesParsed.Data?.CompletedQuests?[0].ObjectiveNotes == null,
            "objectiveNotes=null 必须作为允许的可空值解析");

        var wrongTypeQuest = completedQuest.DeepClone();
        wrongTypeQuest!["objectiveNotes"] = "错误类型";
        var wrongType = ParseQuestEnvelope(currentQuest.DeepClone(), wrongTypeQuest, "/games/77/quests");
        AssertFalse(wrongType.Ok, "合法 JSON 中字段类型错误必须解析失败");
        AssertEqual(wrongType.ErrorCode, "RESPONSE_TYPE_MISMATCH", "字段类型错误必须区别于 JSON 语法错误");
        AssertTrue(wrongType.Message.Contains("$.data.completedQuests[0].objectiveNotes"),
            "结构错误提示必须保留异常字段路径");

        var syntaxError = ApiClient.ParseHttpResult<PlayerQuestListResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes("{\"code\":1,"),
            "/games/77/quests"
        );
        AssertFalse(syntaxError.Ok, "真正的 JSON 语法错误必须解析失败");
        AssertEqual(syntaxError.ErrorCode, "INVALID_JSON", "语法错误必须保留独立错误码");
        AssertFalse(syntaxError.Message.Contains("接口结构不匹配"), "语法错误不得误报为字段类型不匹配");
    }

    private static ApiResult<PlayerQuestListResponseDto> ParseQuestEnvelope(
        JsonNode currentQuest,
        JsonNode completedQuest,
        string path)
    {
        var quests = new JsonArray();
        quests.Add(currentQuest);
        var completed = new JsonArray();
        completed.Add(completedQuest);
        var envelope = new JsonObject
        {
            ["code"] = 1,
            ["msg"] = "success",
            ["errorCode"] = null,
            ["data"] = new JsonObject
            {
                ["gameId"] = 77,
                ["playerId"] = 1001,
                ["quests"] = quests,
                ["completedQuests"] = completed
            }
        };
        return ApiClient.ParseHttpResult<PlayerQuestListResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(envelope.ToJsonString()),
            path
        );
    }

    private static PendingRewardDto CloneRewardForEligibility(bool queueCanExecute, bool candidateCanExecute) => new()
    {
        RewardId = 1,
        PlayerId = 5,
        Kind = "ITEM",
        CanResolve = true,
        HasAuthority = true,
        CapacitySatisfied = queueCanExecute,
        CanExecute = queueCanExecute,
        ExecuteUnavailableReason = queueCanExecute ? null : "队首不可执行",
        CanAbandon = true,
        Candidates =
        [
            new RewardCandidateDto
            {
                CardId = 131,
                ItemId = 1017,
                CanExecute = candidateCanExecute,
                ExecuteUnavailableReason = candidateCanExecute ? null : "候选不可执行",
                CapacitySatisfied = candidateCanExecute,
                Configuration = new ItemRewardConfigurationDto
                {
                    Id = 1017,
                    ItemName = "药剂",
                    ItemType = "CONSUMABLE"
                }
            }
        ]
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

    private async Task DispatchFocusedAcceptAsync(Button button)
    {
        button.GrabFocus();
        await ToSignal(this, SignalName.ProcessFrame);
        Root.PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = true }, true);
        await ToSignal(this, SignalName.ProcessFrame);
        Root.PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = false }, true);
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private static void AssertPayload(GameActionRequestDto? request, string key, long expected, string message)
    {
        var actual = request?.Payload[key]?.GetValue<long>();
        if (actual != expected) throw new InvalidOperationException($"{message}；期望={expected}，实际={actual}");
    }

    private void AssertValidRequestIdentity(GameActionRequestDto? request, long playerId, string label)
    {
        AssertTrue(request != null && Guid.TryParse(request.RequestId, out _), $"{label}必须携带有效 requestId");
        AssertEqual(request?.PlayerId, playerId, $"{label}必须使用局内 playerId");
        AssertTrue(request?.StateVersion > 0, $"{label}必须携带最新 stateVersion");
    }

    private async Task WaitUntilAsync(Func<bool> predicate, string? detail = null, int maxFrames = 900)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (predicate()) return;
            await ToSignal(this, SignalName.ProcessFrame);
        }
        throw new TimeoutException($"等待任务系统测试状态超时。{detail}");
    }

    private void AssertTrue(bool condition, string message)
    {
        if (!condition) _failures.Add(message);
    }

    private void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}；期望={expected}，实际={actual}");
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("Quest system tests passed.");
            Quit();
            return;
        }
        foreach (var failure in _failures) GD.PushError(failure);
        Quit(1);
    }

    private sealed class QuestSystemTransport : IApiTransport
    {
        public GameMapDto Map { get; } = RunGameMapTests.CreateValidMap();
        public PlayerViewResponseDto View { get; }
        public PlayerQuestListResponseDto Quests { get; }
        public PlayerItemsResponseDto Items { get; }
        public int ActionCount { get; private set; }
        public int ItemsRequestCount { get; private set; }
        public int PlayerViewRequestCount { get; private set; }
        public GameActionRequestDto? LastAction { get; private set; }
        public bool FailNextActionWithTimeout { get; set; }
        public bool FailNextQuestReadWithTypeMismatch { get; set; }
        private int _claimedItemCount;

        public QuestSystemTransport()
        {
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
                    QuestSubmissionVersion = 1,
                    ConnectionPolicy = new GameConnectionPolicyDto
                    {
                        HeartbeatSeconds = 5,
                        TimeoutSeconds = 15,
                        ReconnectWaitSeconds = 10,
                        InitialGraceSeconds = 45
                    }
                },
                Players =
                [
                    new PlayerViewPlayerDto
                    {
                        PlayerId = 1001,
                        UserId = 1,
                        Nickname = "GPT1",
                        TurnOrder = 1,
                        CurrentCellId = Map.Cells![0].CellId,
                        CurrentSpeedPoints = 3,
                        CurrentSocialPoints = 3,
                        Coins = 10,
                        Prestige = 2,
                        IsConnected = 1,
                        EffectiveAttributes = new EffectiveAttributesDto()
                    }
                ]
            };
            Quests = new PlayerQuestListResponseDto
            {
                GameId = 77,
                PlayerId = 1001,
                Quests =
                [
                    NewQuest(401, 13, "累计任务", true, 2, 5, true),
                    NewQuest(402, 14, "一次性任务", false, 0, 1, false)
                ],
                CompletedQuests = []
            };
            Items = new PlayerItemsResponseDto
            {
                GameId = 77,
                PlayerId = 1001,
                TotalCount = 2,
                Items =
                [
                    new PlayerItemDto { PlayerItemId = 801, ItemId = 1003, ItemName = "苹果", Location = "HAND", LocationText = "手牌" },
                    new PlayerItemDto { PlayerItemId = 802, ItemId = 1003, ItemName = "苹果", Location = "HAND", LocationText = "手牌" }
                ]
            };
        }

        public Task<ApiResult<T>> RequestJsonAsync<T>(HttpClient.Method method, string path, object? body, bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
                return Return<T, GameMapDto>(ApiResult<GameMapDto>.Success(Map, "success", 200));
            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
            {
                PlayerViewRequestCount++;
                return Return<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(View, "success", 200));
            }
            if (path == "/games/77/players/1001/connection" && typeof(T) == typeof(PlayerViewResponseDto)
                && body is PlayerConnectionRequestDto request)
            {
                var response = CloneView();
                response.EngineState!.Connection = new GameConnectionInfoDto
                {
                    ClientInstanceId = request.ClientInstanceId,
                    ConnectionId = request.ConnectionId ?? "quest-connection",
                    ConnectionActive = true,
                    PlayerConnected = true
                };
                return Return<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(response, "success", 200));
            }
            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
            {
                var actions = new List<AvailableActionDto>
                {
                    new() { ActionType = "END_TURN", Available = true },
                    new() { ActionType = "MOVE_ONE_STEP", Available = true },
                    new() { ActionType = "DISCARD_ITEM", Available = true }
                };
                return Return<T, List<AvailableActionDto>>(ApiResult<List<AvailableActionDto>>.Success(actions, "success", 200));
            }
            if (path == "/games/77/quests" && typeof(T) == typeof(PlayerQuestListResponseDto))
            {
                if (FailNextQuestReadWithTypeMismatch)
                {
                    FailNextQuestReadWithTypeMismatch = false;
                    return Return<T, PlayerQuestListResponseDto>(ApiResult<PlayerQuestListResponseDto>.Failure(
                        "服务器返回 JSON 与接口结构不匹配（字段：$.data.quests[0].objectiveNotes）。",
                        "RESPONSE_TYPE_MISMATCH",
                        200
                    ));
                }
                return Return<T, PlayerQuestListResponseDto>(ApiResult<PlayerQuestListResponseDto>.Success(Quests, "success", 200));
            }
            if (path == "/games/77/items" && typeof(T) == typeof(PlayerItemsResponseDto))
            {
                ItemsRequestCount++;
                return Return<T, PlayerItemsResponseDto>(ApiResult<PlayerItemsResponseDto>.Success(Items, "success", 200));
            }
            if (path == "/games/77/actions" && method == HttpClient.Method.Post
                && typeof(T) == typeof(GameActionResponseDto) && body is GameActionRequestDto action)
            {
                ActionCount++;
                LastAction = action;
                if (FailNextActionWithTimeout)
                {
                    FailNextActionWithTimeout = false;
                    return Return<T, GameActionResponseDto>(ApiResult<GameActionResponseDto>.Failure("模拟超时", "REQUEST_TIMEOUT"));
                }
                return Return<T, GameActionResponseDto>(HandleAction(action));
            }
            return Task.FromResult(ApiResult<T>.Failure($"未处理测试请求：{method} {path}", "UNEXPECTED_REQUEST"));
        }

        public void RestoreFullBagReward()
        {
            Items.Items =
            [
                new PlayerItemDto { PlayerItemId = 801, ItemId = 1003, ItemName = "苹果", Location = "HAND", LocationText = "手牌" },
                new PlayerItemDto { PlayerItemId = 802, ItemId = 1003, ItemName = "苹果", Location = "HAND", LocationText = "手牌" }
            ];
            Items.TotalCount = Items.Items.Count;
            View.EngineState!.PendingReward = BuildReward(
                false,
                rewardId: 73,
                cardId: 131,
                itemId: 1017,
                itemName: "药剂",
                itemType: "CONSUMABLE"
            );
            View.StateVersion++;
        }

        public void RestoreInvalidReward()
        {
            View.EngineState!.PendingReward = BuildReward(true, rewardId: 74, cardId: 903);
            View.EngineState.PendingReward.Candidates![0].Configuration!.Id = 9999;
            View.StateVersion++;
        }

        public void RestoreOneSubmittableQuest()
        {
            Quests.Quests = [NewQuest(403, 15, "超时任务", true, 1, 1, true)];
            View.EngineState!.PendingReward = null;
            View.StateVersion++;
        }

        private ApiResult<GameActionResponseDto> HandleAction(GameActionRequestDto action)
        {
            var oldVersion = View.StateVersion++;
            var response = new GameActionResponseDto
            {
                GameId = 77,
                OldStateVersion = oldVersion,
                NewStateVersion = View.StateVersion,
                ActionType = action.ActionType,
                PlayerViewState = View,
                NextAvailableActions =
                [
                    new AvailableActionDto { ActionType = "END_TURN", Available = true },
                    new AvailableActionDto { ActionType = "DISCARD_ITEM", Available = true }
                ]
            };
            switch (action.ActionType)
            {
                case "SUBMIT_QUEST":
                    var submittedId = action.Payload["playerQuestId"]!.GetValue<long>();
                    Quests.Quests!.RemoveAll(quest => quest.PlayerQuestId == submittedId);
                    View.EngineState!.PendingReward = BuildReward(true);
                    response.QuestSettlement = new QuestSettlementDto
                    {
                        PlayerQuestId = submittedId,
                        QuestId = 13,
                        CoinsGranted = 5,
                        PrestigeGranted = 1,
                        PartnerBonusCoins = 2,
                        CoinsAfter = 15,
                        PrestigeAfter = 3,
                        PendingItemRewardCount = 1
                    };
                    response.DeckDraws = [new DeckDrawDto { RequestedCount = 1, ActualCount = 1, Shortage = false }];
                    break;
                case "ABANDON_QUEST":
                    var abandonedId = action.Payload["playerQuestId"]!.GetValue<long>();
                    Quests.Quests!.RemoveAll(quest => quest.PlayerQuestId == abandonedId);
                    break;
                case "RESOLVE_TURN_START_EFFECT":
                    if (action.Payload.ContainsKey("cardId"))
                    {
                        var claimedRewardId = action.Payload["rewardId"]!.GetValue<long>();
                        var cardId = action.Payload["cardId"]!.GetValue<long>();
                        var candidate = View.EngineState!.PendingReward?.Candidates?.SingleOrDefault(entry => entry.CardId == cardId);
                        _claimedItemCount++;
                        Items.Items!.Add(new PlayerItemDto
                        {
                            PlayerItemId = 8800 + _claimedItemCount,
                            ItemId = candidate?.ItemId,
                            ItemName = candidate?.Configuration?.ItemName,
                            Location = "HAND",
                            LocationText = "手牌"
                        });
                        Items.TotalCount = Items.Items.Count;
                        View.EngineState.PendingReward = claimedRewardId == 71
                            ? BuildReward(
                                true,
                                rewardId: 72,
                                cardId: 902,
                                itemId: 1017,
                                itemName: "药剂",
                                itemType: "CONSUMABLE"
                            )
                            : null;
                    }
                    else
                    {
                        View.EngineState!.PendingReward = null;
                    }
                    break;
                case "DISCARD_ITEM":
                    var playerItemId = action.Payload["playerItemId"]!.GetValue<long>();
                    Items.Items!.RemoveAll(item => item.PlayerItemId == playerItemId);
                    Items.TotalCount = Items.Items.Count;
                    break;
            }
            response.PlayerViewState = View;
            return ApiResult<GameActionResponseDto>.Success(response, "success", 200);
        }

        private PendingRewardDto BuildReward(
            bool capacitySatisfied,
            long rewardId = 71,
            long cardId = 901,
            long itemId = 1003,
            string itemName = "苹果",
            string itemType = "CONSUMABLE") => new()
        {
            RewardId = rewardId,
            PlayerId = 1001,
            Kind = "ITEM",
            SourceType = "QUEST_REWARD",
            CanResolve = true,
            HasAuthority = true,
            CapacitySatisfied = capacitySatisfied,
            CanExecute = capacitySatisfied,
            ExecuteUnavailableReason = capacitySatisfied ? null : "负重已满，请先弃置旧道具或放弃奖励",
            CanAbandon = true,
            Candidates =
            [
                new RewardCandidateDto
                {
                    CardId = cardId,
                    ItemId = itemId,
                    CanExecute = capacitySatisfied,
                    ExecuteUnavailableReason = capacitySatisfied ? null : "负重已满，请先弃置旧道具或放弃奖励",
                    CapacitySatisfied = capacitySatisfied,
                    Configuration = new ItemRewardConfigurationDto
                    {
                        Id = itemId,
                        ItemName = itemName,
                        ItemType = itemType
                    }
                }
            ],
            DrawResult = new DeckDrawDto { RequestedCount = 1, ActualCount = 1, Shortage = false }
        };

        private PlayerViewResponseDto CloneView() => new()
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
                QuestSubmissionVersion = View.EngineState?.QuestSubmissionVersion,
                ConnectionPolicy = View.EngineState?.ConnectionPolicy,
                PendingReward = View.EngineState?.PendingReward
            }
        };

        private static PlayerQuestDto NewQuest(long playerQuestId, long questId, string name, bool completed,
            int current, int required, bool canSubmit) => new()
        {
            PlayerQuestId = playerQuestId,
            QuestId = questId,
            QuestCode = "OFFICIAL_QUEST_006",
            QuestName = name,
            Status = canSubmit ? "READY_TO_SUBMIT" : "IN_PROGRESS",
            Objectives =
            [
                new QuestObjectiveDto
                {
                    ObjectiveKey = "TEST",
                    Text = "测试目标",
                    Completed = completed,
                    CurrentValue = current,
                    RequiredValue = required
                }
            ],
            Submission = new QuestSubmissionDto
            {
                ProtocolVersion = 1,
                TargetsLocked = completed,
                CanSubmit = canSubmit,
                UnavailableReason = canSubmit ? null : "最快在下一个自己的个人回合提交"
            }
        };

        private static Task<ApiResult<TOuter>> Return<TOuter, TInner>(ApiResult<TInner> result) =>
            Task.FromResult((ApiResult<TOuter>)(object)result);
    }
}
