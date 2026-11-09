using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Dice;

public partial class RunWorkAttributeSelectionTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTests).CallDeferred();
    }

    private void BeginTests() => _ = RunTestsAsync();

    private async Task RunTestsAsync()
    {
        VerifyRequestFactory();

        var overlay = GD.Load<PackedScene>("res://scenes/game/dice/WorkDiceOverlay.tscn")
            .Instantiate<WorkDiceOverlay>();
        Root.AddChild(overlay);
        await ToSignal(this, SignalName.ProcessFrame);

        if (OS.GetCmdlineUserArgs().Contains("--visual-check")
            || string.Equals(System.Environment.GetEnvironmentVariable("ES_WORK_ATTRIBUTE_VISUAL_CHECK"), "1", StringComparison.Ordinal))
        {
            overlay.WorkAttributeSelected += attribute => overlay.OpenPending(new PendingCheckDto
            {
                CheckId = 501,
                GameId = 77,
                PlayerId = 1001,
                BusinessType = "WORK",
                Status = "WAITING_ROLL",
                AttributeType = attribute,
                AttributeValue = 3,
                Difficulty = 7,
                StateVersion = 18,
                AuthorizationRound = 0,
                CanConfirm = true,
                CanReroll = true,
                NextRerollSocialCost = 0,
                ExpiresAt = DateTimeOffset.Now.AddMinutes(2).ToString("O")
            }, false);
            overlay.WorkAttributeSelectionCancelled += overlay.CloseOverlay;
            overlay.OpenAttributeSelection(NewAnyWork());
            GD.Print("WORK_ATTRIBUTE_VISUAL_CHECK_READY");
            return;
        }

        var selected = new List<string>();
        var cancelled = 0;
        overlay.WorkAttributeSelected += attribute =>
        {
            selected.Add(attribute);
            overlay.SetAttributeSelectionSubmitting();
        };
        overlay.WorkAttributeSelectionCancelled += () => cancelled++;

        foreach (var attribute in WorkAttributeSelectionModel.AttributeTypes)
        {
            selected.Clear();
            overlay.OpenAttributeSelection(NewAnyWork());
            await ToSignal(this, SignalName.ProcessFrame);
            AssertEqual(overlay.State, WorkDiceOverlayState.SelectingAttribute, "ANY 应打开属性选择态");
            await DispatchViewportClickAsync(overlay.AttributeButtons[attribute]);
            await DispatchViewportClickAsync(overlay.AttributeButtons[attribute]);
            AssertEqual(selected.Count, 1, $"{attribute} 快速重复点击只能产生一次选择事件");
            AssertEqual(selected.SingleOrDefault(), attribute, $"{attribute} 必须保留准确服务端枚举");
            overlay.CloseOverlay();
        }

        selected.Clear();
        overlay.OpenAttributeSelection(NewAnyWork());
        await ToSignal(this, SignalName.ProcessFrame);
        await DispatchViewportClickAsync(overlay.AttributeCancelButton);
        AssertEqual(cancelled, 1, "取消按钮必须发出一次取消事件");
        AssertEqual(selected.Count, 0, "取消不得产生属性选择或 WORK 请求");
        AssertEqual(overlay.State, WorkDiceOverlayState.SelectingAttribute,
            "展示层只发出取消信号，由 GameScreen 统一释放业务状态");
        AssertTrue(overlay.GetNode<Control>("OverlayRoot").IsInGroup("blocks_map_input"),
            "属性选择遮罩必须保持 blocks_map_input 语义");

        overlay.CloseOverlay();
        overlay.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        Finish();
    }

    private static void VerifyRequestFactory()
    {
        var ids = new HashSet<string>();
        foreach (var attribute in WorkAttributeSelectionModel.AttributeTypes)
        {
            var requestId = Guid.NewGuid().ToString("D");
            ids.Add(requestId);
            var request = WorkAttributeSelectionModel.CreateWorkRequest(1001, 17, attribute, requestId);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
            AssertStatic(json.RootElement.GetProperty("actionType").GetString() == "WORK",
                "请求 actionType 必须为 WORK");
            AssertStatic(json.RootElement.GetProperty("playerId").GetInt64() == 1001,
                "请求必须使用局内 playerId");
            AssertStatic(json.RootElement.GetProperty("stateVersion").GetInt64() == 17,
                "请求必须使用权威 stateVersion");
            AssertStatic(json.RootElement.GetProperty("payload").EnumerateObject().Count() == 1,
                "WORK payload 只能包含 attributeType");
            AssertStatic(json.RootElement.GetProperty("payload").GetProperty("attributeType").GetString() == attribute,
                $"{attribute} 序列化不正确");
        }
        AssertStatic(ids.Count == 6, "六个新逻辑行动必须能使用不同 requestId");

        var rejectedAny = false;
        try
        {
            WorkAttributeSelectionModel.CreateWorkRequest(1001, 17, "ANY", Guid.NewGuid().ToString("D"));
        }
        catch (ArgumentException)
        {
            rejectedAny = true;
        }
        AssertStatic(rejectedAny, "请求工厂必须拒绝 ANY，防止绕过 UI 直接提交");
    }

    private static CurrentWorkResponseDto NewAnyWork() => new()
    {
        CellId = 100010,
        CellNameText = "测试打工点",
        AttributeType = "ANY",
        AttributeTypeText = "任选属性",
        Difficulty = 7,
        SocialCost = 1,
        RewardCoins = 3
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
            GD.Print("WORK_ATTRIBUTE_SELECTION_TESTS_PASSED");
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

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}；期望={expected}，实际={actual}");
        }
    }

    private static void AssertStatic(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
