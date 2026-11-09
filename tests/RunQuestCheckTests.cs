using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Dice;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunQuestCheckTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTests).CallDeferred();
    }

    private void BeginTests() => _ = RunTestsAsync();

    private async Task RunTestsAsync()
    {
        VerifyDtoParsing();
        VerifyFixedAndChoiceModels();
        VerifyMultiAttributeAndEligibilityModels();
        VerifyRequestAndPendingIdentity();

        var overlay = GD.Load<PackedScene>("res://scenes/game/ui/QuestCheckOverlay.tscn")
            .Instantiate<QuestCheckOverlay>();
        Root.AddChild(overlay);
        await ToSignal(this, SignalName.ProcessFrame);
        if (OS.GetCmdlineUserArgs().Contains("--visual-check")
            || string.Equals(System.Environment.GetEnvironmentVariable("ES_QUEST_CHECK_VISUAL_CHECK"), "1", StringComparison.Ordinal))
        {
            var context = NewContext();
            var multi = NewQuest(403, 149, "蛋糕谜案", QuestCheckModel.SocialCheck, "SPEED,SOCIAL,MAGIC");
            multi.RequiredSuccessAttributes = "SPEED,SOCIAL,MAGIC";
            multi.CheckAttributes =
            [
                new QuestCheckAttributeDto { AttributeType = "SPEED", Completed = true, Eligible = false, Available = false, UnavailableReason = "该属性已完成" },
                new QuestCheckAttributeDto { AttributeType = "SOCIAL", Completed = false, Eligible = true, Available = true },
                new QuestCheckAttributeDto { AttributeType = "MAGIC", Completed = false, Eligible = true, Available = false, UnavailableReason = "当前位置不可用" }
            ];
            var visualTasks = QuestCheckModel.BuildTasks(
                [
                    NewQuest(401, 138, "任选属性判定", QuestCheckModel.SocialCheck, "STRENGTH,MAGIC,MIND"),
                    multi,
                    NewQuest(402, 150, "固定速度判定", QuestCheckModel.SocialCheck, "SPEED")
                ],
                context.View,
                context.Player,
                context.Actions,
                context.View.StateVersion,
                true,
                false);
            overlay.ShowContent(visualTasks, "本地视觉验收：可用、已完成和不可用状态");
            GD.Print("QUEST_CHECK_VISUAL_READY");
            return;
        }
        await VerifyRealGuiInputAsync(overlay);

        var dice = GD.Load<PackedScene>("res://scenes/game/dice/WorkDiceOverlay.tscn")
            .Instantiate<WorkDiceOverlay>();
        Root.AddChild(dice);
        await ToSignal(this, SignalName.ProcessFrame);
        await VerifyQuestDicePresentationAsync(dice);

        dice.QueueFree();
        overlay.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        await VerifyPreparationReplayRecoveryAsync();
        Finish();
    }

    private void VerifyDtoParsing()
    {
        const string json = """
        {
          "code":1,"msg":"success","errorCode":null,
          "data":{"gameId":77,"playerId":1001,
            "quests":[
              {"playerQuestId":401,"questId":149,"questCode":"OFFICIAL_QUEST_049","questName":"蛋糕谜案",
               "status":"IN_PROGRESS","triggerType":"SOCIAL_CHECK","checkAttributeTypes":"SPEED,SOCIAL,MAGIC",
               "requiredSuccessAttributes":"SPEED,SOCIAL,MAGIC","difficultyLevel":7,"requiredSocialCost":1,
               "objectives":[{"objectiveKey":"OFFICIAL_QUEST_049:SPEED","text":"速度判定成功一次","completed":true,"currentValue":1,"requiredValue":1}],
               "conditions":[{"conditionKey":"CURRENT_LOCATION","text":"位于任务地点","satisfied":true}],
               "checkAttributes":[
                 {"attributeType":"SPEED","completed":true,"eligible":false,"available":false,"unavailableReason":"该属性已完成"},
                 {"attributeType":"SOCIAL","completed":false,"eligible":true,"available":true,"unavailableReason":null},
                 {"attributeType":"MAGIC","completed":false,"eligible":true,"available":false,"unavailableReason":"当前位置不可用"}
               ]},
              {"playerQuestId":402,"questId":138,"questCode":"OFFICIAL_QUEST_038","questName":"任选判定",
               "status":"IN_PROGRESS","triggerType":"SOCIAL_CHECK","checkAttributeTypes":"STRENGTH,MAGIC,MIND",
               "requiredSuccessAttributes":null,"difficultyLevel":6,"requiredSocialCost":1,
               "objectives":[],"conditions":[],"checkAttributes":null}
            ],
            "completedQuests":[
              {"playerQuestId":399,"questId":150,"questCode":"OFFICIAL_QUEST_050","questName":"已完成任务",
               "status":"COMPLETED","requiredSuccessAttributes":null,"objectiveNotes":[]}
            ]}
        }
        """;
        var parsed = ApiClient.ParseHttpResult<PlayerQuestListResponseDto>(
            HttpRequest.Result.Success,
            200,
            Encoding.UTF8.GetBytes(json),
            "/games/77/quests");
        AssertTrue(parsed.Ok && parsed.Data != null, "完整 /quests Result 必须通过 ApiClient 包装解析链路");
        var quest = parsed.Data?.Quests?.SingleOrDefault(candidate => candidate.QuestCode == "OFFICIAL_QUEST_049");
        AssertTrue(quest != null, "完整任务 JSON 必须能解析 049 当前任务");
        AssertEqual(quest?.TriggerType, "SOCIAL_CHECK", "triggerType 解析错误");
        AssertEqual(quest?.CheckAttributeTypes, "SPEED,SOCIAL,MAGIC", "checkAttributeTypes 必须保留逗号字符串");
        AssertEqual(quest?.RequiredSuccessAttributes, "SPEED,SOCIAL,MAGIC", "requiredSuccessAttributes 必须解析为属性清单字符串");
        AssertEqual(quest?.DifficultyLevel, 7, "difficultyLevel 解析错误");
        AssertEqual(quest?.RequiredSocialCost, 1, "requiredSocialCost 解析错误");
        AssertEqual(quest?.CheckAttributes?.Count, 3, "checkAttributes 对象数组解析错误");
        var choice = parsed.Data?.Quests?.SingleOrDefault(candidate => candidate.QuestCode == "OFFICIAL_QUEST_038");
        AssertTrue(choice != null && choice.RequiredSuccessAttributes == null, "038 的 requiredSuccessAttributes=null 必须正常解析");
        AssertEqual(parsed.Data?.CompletedQuests?.Count, 1, "已完成任务也不能拖垮完整响应解析");

        QuestTaskDisplay? personalTask = null;
        if (quest != null && QuestSystemModel.TryBuildTask(quest, true, 2, out var builtPersonalTask, out _))
        {
            personalTask = builtPersonalTask;
        }
        AssertTrue(personalTask != null, "个人任务入口必须继续使用共享 PlayerQuestDto 构建信息卡");
        AssertEqual(personalTask?.PlayerQuestId, 401L, "个人任务入口解析了错误任务实例");

        var context = NewContext();
        var checkTasks = QuestCheckModel.BuildTasks(
            parsed.Data?.Quests,
            context.View,
            context.Player,
            context.Actions,
            context.View.StateVersion,
            true,
            false);
        AssertEqual(checkTasks.Count, 2, "判定入口必须从同一完整响应识别 049 与 038");
        AssertEqual(checkTasks.Single(task => task.PlayerQuestId == 401).Rows.Count, 3, "049 必须按属性清单分三行");
        AssertTrue(checkTasks.Single(task => task.PlayerQuestId == 402).Rows.Single().IsChoice,
            "038 requiredSuccessAttributes=null 必须保持任选流程");
    }

    private void VerifyFixedAndChoiceModels()
    {
        var context = NewContext();
        var fixedQuest = NewQuest(401, 149, "固定速度", QuestCheckModel.SocialCheck, "SPEED");
        var fixedTask = QuestCheckModel.BuildTasks(
            [fixedQuest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertEqual(fixedTask.Rows.Count, 1, "固定属性任务只显示一行");
        AssertEqual(fixedTask.Rows[0].AttributeType, "SPEED", "固定属性任务必须使用准确枚举");
        AssertTrue(fixedTask.Rows[0].CanExecute, "满足资格的固定属性任务应可判定");

        var choiceQuest = NewQuest(402, 138, "任选判定", QuestCheckModel.SocialCheck, "STRENGTH,MAGIC,MIND");
        var choiceTask = QuestCheckModel.BuildTasks(
            [choiceQuest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertEqual(choiceTask.Rows.Count, 1, "任选任务只显示一行");
        AssertTrue(choiceTask.Rows[0].IsChoice, "多个允许属性且无逐属性进度时必须进入任选流程");
        AssertEqual(choiceTask.Rows[0].ChoiceAttributes.Count, 3, "任选属性必须来自 checkAttributeTypes");
        AssertFalse(choiceTask.Rows[0].ChoiceAttributes.Contains("ANY"), "客户端不得生成或提交 ANY");
    }

    private void VerifyMultiAttributeAndEligibilityModels()
    {
        var context = NewContext();
        var quest = NewQuest(403, 149, "蛋糕谜案", QuestCheckModel.SocialCheck, "SPEED,SOCIAL,MAGIC");
        quest.RequiredSuccessAttributes = "SPEED,SOCIAL,MAGIC";
        quest.CheckAttributes =
        [
            new QuestCheckAttributeDto { AttributeType = "SPEED", Completed = true, Eligible = false, Available = false, UnavailableReason = "已完成" },
            new QuestCheckAttributeDto { AttributeType = "SOCIAL", Completed = false, Eligible = true, Available = true },
            new QuestCheckAttributeDto { AttributeType = "MAGIC", Completed = false, Eligible = true, Available = false, UnavailableReason = "当前位置不可用" }
        ];
        var task = QuestCheckModel.BuildTasks(
            [quest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertEqual(task.Rows.Count, 3, "049 一类分别成功任务必须按 checkAttributes 分行");
        AssertTrue(task.Rows[0].Completed && !task.Rows[0].CanExecute, "已成功属性必须置灰且不能重复收费判定");
        AssertTrue(task.Rows[1].CanExecute, "逐属性 available=true 时应保留可执行资格");
        AssertFalse(task.Rows[2].CanExecute, "逐属性 available=false 时必须置灰");

        quest.CheckAttributes =
        [
            new QuestCheckAttributeDto { AttributeType = "SPEED", Completed = true, Eligible = false, Available = false }
        ];
        var missing = QuestCheckModel.BuildTasks(
            [quest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertEqual(missing.Rows.Count, 3, "逐属性状态缺失时仍按 requiredSuccessAttributes 展示全部必需属性");
        AssertFalse(missing.Rows[1].CanExecute, "缺失的逐属性状态不能猜测为可执行");
        AssertTrue(missing.Rows[1].UnavailableReason.Contains("尚未同步"), "缺失逐属性状态必须显示可靠原因");

        quest.CheckAttributes = null;
        var allMissing = QuestCheckModel.BuildTasks(
            [quest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertTrue(allMissing.Rows.All(row => !row.CanExecute && !row.IsChoice),
            "逐属性数组整体缺失时必须全部置灰，不能降级为任选");

        var locationBlocked = NewQuest(404, 138, "异地任务", QuestCheckModel.SocialCheck, "SOCIAL");
        locationBlocked.Conditions =
        [
            new QuestConditionDto { ConditionKey = "CURRENT_LOCATION", Text = "请前往任务地点", Satisfied = false }
        ];
        var blocked = QuestCheckModel.BuildTasks(
            [locationBlocked], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertFalse(blocked.Rows[0].CanExecute, "CURRENT_LOCATION=false 必须置灰");
        AssertTrue(blocked.Rows[0].UnavailableReason.Contains("任务地点"), "应优先展示服务端条件原因");

        var full = NewQuest(405, 150, "满社交任务", QuestCheckModel.FullSocialCheck, "CHARM");
        context.Actions.Add(new AvailableActionDto { ActionType = QuestCheckModel.FullSocialCheckAction, Available = true });
        context.Player.CurrentSocialPoints = context.Player.EffectiveAttributes!.Social - 1;
        var fullTask = QuestCheckModel.BuildTasks(
            [full], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertFalse(fullTask.Rows[0].CanExecute, "满社交任务必须要求当前社交点等于有效社交属性");

        var ready = NewQuest(406, 151, "待提交任务", QuestCheckModel.SocialCheck, "MIND");
        ready.Status = "READY_TO_SUBMIT";
        var readyTask = QuestCheckModel.BuildTasks(
            [ready], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertTrue(readyTask.ReadyToSubmit, "待提交任务必须标记已达标");
        AssertFalse(readyTask.Rows[0].CanExecute, "待提交任务不得再次准备判定");

        var allBlocked = QuestCheckModel.BuildTasks(
            [locationBlocked, ready], context.View, context.Player, context.Actions, context.View.StateVersion, true, false);
        AssertEqual(allBlocked.Count, 2, "全部不可用时仍必须展示匹配任务列表");

        context.View.EngineState!.BatchId = "CORE_9";
        var unsupportedBatch = QuestCheckModel.BuildTasks(
            [NewQuest(407, 152, "旧批次任务", QuestCheckModel.SocialCheck, "SPEED")],
            context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertFalse(unsupportedBatch.Rows[0].CanExecute, "简写 CORE_9 不得绕过正式批次保护");
        AssertTrue(unsupportedBatch.Rows[0].UnavailableReason.Contains("批次"), "未支持批次必须给出明确提示");
    }

    private void VerifyRequestAndPendingIdentity()
    {
        var context = NewContext();
        var task = QuestCheckModel.BuildTasks(
            [NewQuest(401, 149, "固定速度", QuestCheckModel.SocialCheck, "SPEED")],
            context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        var requestId = Guid.NewGuid().ToString("D");
        AssertTrue(QuestCheckModel.TryCreateRequest(
            task, "SPEED", context.Player.PlayerId, context.View.StateVersion, requestId, out var request, out _),
            "合法任务判定应能生成请求");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
        var root = json.RootElement;
        AssertEqual(root.GetProperty("requestId").GetString(), requestId, "准备请求必须携带本次逻辑行动 requestId");
        AssertEqual(root.GetProperty("playerId").GetInt64(), 1001L, "准备请求必须使用局内 playerId");
        AssertEqual(root.GetProperty("stateVersion").GetInt64(), 12L, "准备请求必须使用最新 stateVersion");
        AssertEqual(root.GetProperty("actionType").GetString(), QuestCheckModel.SocialCheckAction, "普通任务应使用 QUEST_SOCIAL_CHECK");
        var payload = root.GetProperty("payload");
        AssertEqual(payload.EnumerateObject().Count(), 3, "CORE_9 任务判定 payload 只能包含三个契约字段");
        AssertEqual(payload.GetProperty("questId").GetInt64(), 149L, "payload questId 错误");
        AssertEqual(payload.GetProperty("playerQuestId").GetInt64(), 401L, "payload playerQuestId 错误");
        AssertEqual(payload.GetProperty("attributeType").GetString(), "SPEED", "payload 必须提交具体属性");

        context.Actions.Add(new AvailableActionDto
        {
            ActionType = QuestCheckModel.FullSocialCheckAction,
            Available = true
        });
        var fullTask = QuestCheckModel.BuildTasks(
            [NewQuest(402, 150, "满社交魅力", QuestCheckModel.FullSocialCheck, "CHARM")],
            context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        AssertTrue(QuestCheckModel.TryCreateRequest(
            fullTask, "CHARM", context.Player.PlayerId, context.View.StateVersion,
            Guid.NewGuid().ToString("D"), out var fullRequest, out _),
            "满社交任务判定应能生成请求");
        AssertEqual(fullRequest.ActionType, QuestCheckModel.FullSocialCheckAction,
            "满社交任务必须使用 QUEST_FULL_SOCIAL_CHECK");

        var pending = NewPendingQuest();
        AssertTrue(QuestCheckModel.TryValidatePendingQuest(pending, 77, 1001, 401, 149, out _, out _),
            "QUEST pending 应按 businessSourceId=playerQuestId 恢复");
        AssertFalse(QuestCheckModel.TryValidatePendingQuest(pending, 77, 1001, 149, 149, out _, out _),
            "不得把 questId 当作 businessSourceId");
    }

    private async Task VerifyRealGuiInputAsync(QuestCheckOverlay overlay)
    {
        var context = NewContext();
        var fixedTask = QuestCheckModel.BuildTasks(
            [NewQuest(401, 149, "固定速度", QuestCheckModel.SocialCheck, "SPEED")],
            context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        var events = new List<string>();
        overlay.CheckRequested += (_, _, _, attribute) => events.Add(attribute);
        overlay.ShowContent([fixedTask]);
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchViewportClickAsync(overlay.ActionButtons.Single());
        AssertEqual(events.Count, 1, "真实 GUI 按下/释放必须恰好产生一次任务判定请求事件");
        AssertEqual(events[0], "SPEED", "固定属性 GUI 点击枚举错误");
        AssertTrue(overlay.IsOpen, "点击任务判定按钮不得关闭列表或穿透地图");

        var blockedQuest = NewQuest(402, 150, "不可用", QuestCheckModel.SocialCheck, "MIND");
        blockedQuest.Conditions = [new QuestConditionDto { ConditionKey = "CURRENT_LOCATION", Satisfied = false, Text = "不在地点" }];
        var blockedTask = QuestCheckModel.BuildTasks(
            [blockedQuest], context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        overlay.ShowContent([blockedTask]);
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchViewportClickAsync(overlay.ActionButtons.Single());
        AssertEqual(events.Count, 1, "灰色判定按钮必须产生零新增请求");
        AssertTrue(overlay.IsOpen, "灰色按钮区域不得关闭弹层");

        var choiceTask = QuestCheckModel.BuildTasks(
            [NewQuest(403, 138, "任选", QuestCheckModel.SocialCheck, "STRENGTH,MAGIC")],
            context.View, context.Player, context.Actions, context.View.StateVersion, true, false).Single();
        overlay.ShowContent([choiceTask]);
        await ToSignal(this, SignalName.ProcessFrame);
        var choose = FindButtonByText(overlay, "选择属性");
        AssertTrue(choose != null, "任选任务必须提供属性选择按钮");
        if (choose != null) await DispatchViewportClickAsync(choose);
        AssertEqual(overlay.State, QuestCheckOverlayState.SelectingAttribute, "任选按钮应进入选择页");
        await DispatchViewportClickAsync(overlay.AttributeButtons["MAGIC"]);
        AssertEqual(overlay.State, QuestCheckOverlayState.Content, "选择具体属性后应回到任务列表");
        await DispatchViewportClickAsync(overlay.ActionButtons.Single());
        AssertEqual(events.LastOrDefault(), "MAGIC", "任选任务必须提交用户明确选择的具体枚举");
        AssertTrue(overlay.GetNode<Control>("OverlayRoot").IsInGroup("blocks_map_input"),
            "判定弹层必须保持 blocks_map_input 输入隔离");
        overlay.CloseOverlay(false);
    }

    private async Task VerifyQuestDicePresentationAsync(WorkDiceOverlay overlay)
    {
        var pending = NewPendingQuest();
        overlay.OpenPending(pending, true);
        var title = overlay.GetNode<Label>("OverlayRoot/InfoAnchor/InfoPanel/InfoMargin/Info/TitleLabel");
        AssertEqual(title.Text, "恢复任务判定", "QUEST pending 必须进入现有任务投骰分支而非打工分支");

        var retryCount = 0;
        overlay.RecoveryRetryRequested += () => retryCount++;
        overlay.ShowRecoveryError("模拟待判定查询失败");
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchViewportClickAsync(overlay.RecoveryRetryButton);
        AssertEqual(retryCount, 1, "恢复重试按钮必须通过正常 GUI 按下/释放恰好触发一次");

        var response = new CheckRollResponseDto
        {
            PendingCheck = new PendingCheckDto
            {
                CheckId = pending.CheckId,
                QuestId = pending.QuestId,
                BusinessType = "QUEST",
                BusinessSourceId = pending.BusinessSourceId,
                Status = "COMPLETED"
            },
            SubmittedDice = 4,
            EffectiveDice = 6,
            CheckResult = new CheckResultDto { Success = true },
            PlayerFacingMessage = "速度判定成功"
        };

        var dismissedCount = 0;
        overlay.ResultDismissed += () => dismissedCount++;
        overlay.ShowQuestResult(response);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertTrue(overlay.ResultText.Contains("服务端有效点数：6"), "结果页必须展示服务端实际采用骰点");
        AssertTrue(overlay.ResultText.Contains("不等于任务已提交"), "判定成功不得冒充任务结算或奖励到账");
        await DispatchViewportClickAsync(overlay.GetNode<Control>("OverlayRoot/ResultDismissArea"));
        AssertEqual(dismissedCount, 1, "结果页必须接受出现后的新 GUI 点击并恰好返回一次");
        overlay.CloseOverlay();
    }

    private static (PlayerViewResponseDto View, PlayerViewPlayerDto Player, List<AvailableActionDto> Actions) NewContext()
    {
        var player = new PlayerViewPlayerDto
        {
            PlayerId = 1001,
            UserId = 1,
            Nickname = "P1",
            CurrentSocialPoints = 4,
            EffectiveAttributes = new EffectiveAttributesDto { Strength = 2, Speed = 4, Social = 4, Charm = 3, Magic = 5, Mind = 3 }
        };
        var view = new PlayerViewResponseDto
        {
            GameId = 77,
            GameStatus = "RUNNING",
            CurrentTurnUserId = 1,
            StateVersion = 12,
            EngineState = new EngineStateDto { BatchId = QuestCheckModel.SupportedBatchId },
            Players = [player]
        };
        var actions = new List<AvailableActionDto>
        {
            new() { ActionType = QuestCheckModel.SocialCheckAction, Available = true }
        };
        return (view, player, actions);
    }

    private static PlayerQuestDto NewQuest(
        long playerQuestId,
        long questId,
        string name,
        string triggerType,
        string attributes) => new()
    {
        PlayerQuestId = playerQuestId,
        QuestId = questId,
        QuestName = name,
        Status = "IN_PROGRESS",
        TriggerType = triggerType,
        CheckAttributeTypes = attributes,
        DifficultyLevel = 7,
        RequiredSocialCost = 1,
        Conditions = []
    };

    private static PendingCheckDto NewPendingQuest() => new()
    {
        CheckId = 501,
        GameId = 77,
        PlayerId = 1001,
        QuestId = 149,
        BusinessType = "QUEST",
        BusinessSourceId = 401,
        Status = "WAITING_ROLL",
        AttributeType = "SPEED",
        AttributeValue = 4,
        Difficulty = 7,
        StateVersion = 13,
        AuthorizationRound = 0,
        CanConfirm = true,
        CanReroll = true,
        NextRerollSocialCost = 1,
        ExpiresAt = DateTimeOffset.Now.AddMinutes(2).ToString("O")
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

    private async Task VerifyPreparationReplayRecoveryAsync()
    {
        var session = Root.GetNode<Session>("Session");
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "quest-check-test-token",
            User = new UserDto { UserId = 1, Username = "quest-check", Nickname = "判定测试" }
        });
        session.SetCurrentRoom(10, "判定测试房");
        session.SetCurrentGame(77);

        await VerifyPreparationReplayScenarioAsync(apiClient, QuestReplayOutcome.CurrentPending);
        await VerifyPreparationReplayScenarioAsync(apiClient, QuestReplayOutcome.NoPendingAfterQueryFailure);

        apiClient.ClearTransportForTests();
        session.ClearAuth();
    }

    private async Task VerifyPreparationReplayScenarioAsync(ApiClient apiClient, QuestReplayOutcome outcome)
    {
        var transport = new QuestReplayTransport(outcome);
        apiClient.SetTransportForTests(transport);
        var screen = GD.Load<PackedScene>("res://scenes/screens/GameScreen.tscn").Instantiate<GameScreen>();
        Root.AddChild(screen);
        screen.Start();
        await WaitUntilAsync(() => screen.IsGameConnectionReady
            && screen.LatestPlayerView != null
            && !screen.IsAvailableActionsRequestInProgress);
        screen.GetNode<Timer>("PlayerViewTimer").Stop();

        await screen.OpenQuestChecksAsync();
        await WaitUntilAsync(() => screen.QuestCheckOverlay.State == QuestCheckOverlayState.Content
            && screen.QuestCheckOverlay.ActionButtons.Count == 1);
        var row = screen.QuestCheckOverlay.Tasks.Single().Rows.Single();
        AssertTrue(row.CanExecute, $"恢复场景前置任务必须可提交：{row.UnavailableReason}");
        await screen.SubmitQuestCheckAsync(
            149,
            401,
            QuestCheckModel.SocialCheckAction,
            "SPEED");
        await WaitUntilAsync(() => screen.IsQuestPreparationUncertain
            && screen.QuestCheckOverlay.State == QuestCheckOverlayState.Error);
        AssertEqual(transport.ActionRequestCount, 1, "准备响应丢失后不能自动重放行动");

        await screen.RetryQuestCheckRecoveryAsync();
        if (outcome == QuestReplayOutcome.CurrentPending)
        {
            await WaitUntilAsync(() => screen.WorkDiceOverlay.State == WorkDiceOverlayState.AwaitingRoll);
            AssertEqual(transport.ActionRequestCount, 2, "用户恢复时只允许使用原请求进行一次幂等重放");
            AssertEqual(transport.ActionRequestIds.Distinct().Count(), 1, "准备重放必须复用原 requestId");
            AssertEqual(transport.ActionBodies.Distinct().Count(), 1, "准备重放必须保留原 stateVersion 和完整请求体");
            AssertTrue(transport.PendingQueryCount >= 3, "历史回执后必须再次查询当前 /checks/pending");
        }
        else
        {
            await WaitUntilAsync(() => transport.ActionRequestCount == 2
                && screen.QuestCheckOverlay.State == QuestCheckOverlayState.Error
                && screen.IsQuestPreparationUncertain);
            AssertEqual(transport.ActionRequestCount, 2, "待判定复查失败不能创建新的逻辑行动");
            AssertFalse(screen.WorkDiceOverlay.IsOpen, "重放后的当前 pending 查询失败时不得打开历史骰子");

            await screen.RetryQuestCheckRecoveryAsync();
            await WaitUntilAsync(() => screen.QuestCheckOverlay.State == QuestCheckOverlayState.Content
                && screen.QuestCheckOverlay.StatusText.Contains("不再待处理"));
            AssertEqual(transport.ActionRequestCount, 2, "重放已确认后再次恢复只能查询 pending，不能再次提交行动");
            AssertFalse(screen.WorkDiceOverlay.IsOpen, "当前 pending=null 时不得打开历史 WAITING_ROLL 快照");
        }

        screen.Stop();
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task WaitUntilAsync(Func<bool> predicate, int maxFrames = 600)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (predicate()) return;
            await ToSignal(this, SignalName.ProcessFrame);
        }
        AssertTrue(false, "等待任务判定恢复条件超时");
    }

    private enum QuestReplayOutcome
    {
        CurrentPending,
        NoPendingAfterQueryFailure
    }

    private sealed class QuestReplayTransport(QuestReplayOutcome outcome) : IApiTransport
    {
        private readonly GameMapDto _map = RunGameMapTests.CreateValidMap();
        private readonly QuestReplayOutcome _outcome = outcome;
        private PlayerViewResponseDto _view = null!;
        private PendingCheckDto? _currentPending;
        private bool _postReplayQueryFailed;

        public int ActionRequestCount { get; private set; }
        public int PendingQueryCount { get; private set; }
        public List<string> ActionRequestIds { get; } = [];
        public List<string> ActionBodies { get; } = [];

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            await Task.Delay(1);
            EnsureView();
            if (path == "/games/77/map" && typeof(T) == typeof(GameMapDto))
                return Cast<T, GameMapDto>(ApiResult<GameMapDto>.Success(Clone(_map), "success", 200));
            if (path == "/games/77/player-view" && typeof(T) == typeof(PlayerViewResponseDto))
                return Cast<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(Clone(_view), "success", 200));
            if (path == "/games/77/players/1001/connection" && typeof(T) == typeof(PlayerViewResponseDto))
            {
                var response = Clone(_view)!;
                response.EngineState ??= new EngineStateDto();
                response.EngineState.ConnectionPolicy = NewPolicy();
                response.EngineState.Connection = new GameConnectionInfoDto
                {
                    ClientInstanceId = (body as PlayerConnectionRequestDto)?.ClientInstanceId,
                    ConnectionId = (body as PlayerConnectionRequestDto)?.ConnectionId ?? "quest-check-connection",
                    ConnectionActive = true,
                    PlayerConnected = true,
                    HeartbeatSeconds = 5,
                    TimeoutSeconds = 15
                };
                return Cast<T, PlayerViewResponseDto>(ApiResult<PlayerViewResponseDto>.Success(response, "success", 200));
            }
            if (path == "/games/77/available-actions" && typeof(T) == typeof(List<AvailableActionDto>))
            {
                return Cast<T, List<AvailableActionDto>>(ApiResult<List<AvailableActionDto>>.Success(
                    [new AvailableActionDto { ActionType = QuestCheckModel.SocialCheckAction, Available = true }],
                    "success",
                    200));
            }
            if (path == "/games/77/quests" && typeof(T) == typeof(PlayerQuestListResponseDto))
            {
                if (query == null || !query.TryGetValue("playerId", out var playerId) || playerId != "1001")
                    throw new InvalidOperationException("/quests 必须使用局内 playerId。");
                return Cast<T, PlayerQuestListResponseDto>(ApiResult<PlayerQuestListResponseDto>.Success(
                    new PlayerQuestListResponseDto
                    {
                        GameId = 77,
                        PlayerId = 1001,
                        Quests =
                        [
                            new PlayerQuestDto
                            {
                                PlayerQuestId = 401,
                                QuestId = 149,
                                QuestCode = "OFFICIAL_QUEST_049",
                                QuestName = "蛋糕谜案",
                                Status = "IN_PROGRESS",
                                TriggerType = QuestCheckModel.SocialCheck,
                                CheckAttributeTypes = "SPEED",
                                RequiredSuccessAttributes = null,
                                DifficultyLevel = 7,
                                RequiredSocialCost = 1,
                                Conditions = []
                            }
                        ],
                        CompletedQuests = []
                    },
                    "success",
                    200));
            }
            if (path == "/games/77/actions" && typeof(T) == typeof(GameActionResponseDto))
            {
                var request = body as GameActionRequestDto
                    ?? throw new InvalidOperationException("任务判定行动缺少请求体。");
                ValidateAction(request);
                ActionRequestCount++;
                ActionRequestIds.Add(request.RequestId);
                ActionBodies.Add(JsonSerializer.Serialize(request));
                if (ActionRequestCount == 1)
                {
                    return ApiResult<T>.Failure("模拟准备响应丢失", "REQUEST_TIMEOUT");
                }

                var historicalPending = NewPending();
                var historicalView = Clone(_view)!;
                historicalView.StateVersion = 6;
                historicalView.EngineState!.PendingCheck = Clone(historicalPending);
                if (_outcome == QuestReplayOutcome.CurrentPending)
                {
                    _currentPending = Clone(historicalPending);
                    _view = Clone(historicalView)!;
                }
                else
                {
                    _currentPending = null;
                    _view.StateVersion = 7;
                    _view.EngineState!.PendingCheck = null;
                }
                return Cast<T, GameActionResponseDto>(ApiResult<GameActionResponseDto>.Success(
                    new GameActionResponseDto
                    {
                        GameId = 77,
                        OldStateVersion = request.StateVersion,
                        NewStateVersion = 6,
                        ActionType = request.ActionType,
                        ResultType = "CHECK_ROLL_PENDING",
                        PendingCheck = historicalPending,
                        PlayerViewState = historicalView
                    },
                    "success",
                    200));
            }
            if (path == "/games/77/checks/pending" && typeof(T) == typeof(PendingCheckDto))
            {
                PendingQueryCount++;
                if (ActionRequestCount >= 2
                    && _outcome == QuestReplayOutcome.NoPendingAfterQueryFailure
                    && !_postReplayQueryFailed)
                {
                    _postReplayQueryFailed = true;
                    return ApiResult<T>.Failure("模拟重放后的待判定查询失败", "NETWORK_ERROR");
                }
                return Cast<T, PendingCheckDto>(ApiResult<PendingCheckDto>.Success(Clone(_currentPending), "success", 200));
            }
            return ApiResult<T>.Failure($"严格模拟未定义请求：{method} {path}", "UNEXPECTED_REQUEST");
        }

        private void EnsureView()
        {
            if (_view != null) return;
            var cell = _map.Cells![0];
            _view = new PlayerViewResponseDto
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
                    BatchId = QuestCheckModel.SupportedBatchId,
                    ConnectionPolicy = NewPolicy()
                },
                Players =
                [
                    new PlayerViewPlayerDto
                    {
                        PlayerId = 1001,
                        UserId = 1,
                        Nickname = "判定测试",
                        TurnOrder = 1,
                        CurrentCellId = cell.CellId,
                        CurrentCellCode = cell.CellCode,
                        CurrentCellName = cell.CellName,
                        CurrentCellNameText = cell.CellNameText,
                        CurrentSpeedPoints = 3,
                        CurrentSocialPoints = 4,
                        EffectiveAttributes = new EffectiveAttributesDto { Speed = 4, Social = 4 }
                    }
                ]
            };
        }

        private static PendingCheckDto NewPending() => new()
        {
            CheckId = 501,
            GameId = 77,
            PlayerId = 1001,
            QuestId = 149,
            BusinessType = "QUEST",
            BusinessSourceId = 401,
            Status = "WAITING_ROLL",
            AttributeType = "SPEED",
            AttributeValue = 4,
            Difficulty = 7,
            SocialCost = 1,
            RemainingSocial = 3,
            StateVersion = 6,
            AuthorizationRound = 0,
            CanConfirm = true,
            CanReroll = false,
            ExpiresAt = DateTimeOffset.Now.AddMinutes(2).ToString("O")
        };

        private static GameConnectionPolicyDto NewPolicy() => new()
        {
            HeartbeatSeconds = 5,
            TimeoutSeconds = 15,
            ReconnectWaitSeconds = 10,
            InitialGraceSeconds = 45
        };

        private static void ValidateAction(GameActionRequestDto request)
        {
            if (request.ActionType != QuestCheckModel.SocialCheckAction
                || request.PlayerId != 1001
                || request.StateVersion != 5
                || !Guid.TryParseExact(request.RequestId, "D", out _)
                || request.Payload.Count != 3
                || request.Payload["questId"]?.GetValue<long>() != 149
                || request.Payload["playerQuestId"]?.GetValue<long>() != 401
                || request.Payload["attributeType"]?.GetValue<string>() != "SPEED")
            {
                throw new InvalidOperationException("任务判定准备请求不符合严格契约。");
            }
        }

        private static ApiResult<TTarget> Cast<TTarget, TSource>(ApiResult<TSource> result) =>
            (ApiResult<TTarget>)(object)result;

        private static T? Clone<T>(T? value)
        {
            if (value == null) return default;
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        }
    }

    private static Button? FindButtonByText(Node root, string text)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button && button.Text == text) return button;
            var nested = FindButtonByText(child, text);
            if (nested != null) return nested;
        }
        return null;
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("QUEST_CHECK_TESTS_PASSED");
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
        {
            _failures.Add($"{message}；期望={expected}，实际={actual}");
        }
    }
}
