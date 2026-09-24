using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunCharacterSelectTests : SceneTree
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
        session.SetAuth(new LoginResponseDto
        {
            AccessToken = "test-token",
            User = new UserDto { UserId = 1, Username = "", Nickname = "当前玩家" }
        });
        session.SetCurrentRoom(10, "选角测试房间");

        var transport = new CharacterSelectTransport { Detail = CreateRoom(DateTime.Now.AddMinutes(1)) };
        apiClient.SetTransportForTests(transport);

        var screen = await CreateScreenAsync(transport);
        var leftButton = screen.GetNode<Button>("RootMargin/Layout/CarouselFrame/LeftButton");
        var confirmButton = screen.GetNode<Button>("RootMargin/Layout/ConfirmRow/ConfirmButton");
        var cardNameLabel = screen.GetNode<Label>("RootMargin/Layout/CardNameLabel");
        var selectionStateLabel = screen.GetNode<Label>("RootMargin/Layout/SelectionStateLabel");
        var networkStatusLabel = screen.GetNode<Label>("RootMargin/Layout/NetworkStatusLabel");
        var scroll = screen.GetNode<ScrollContainer>("RootMargin/Layout/CarouselFrame/CarouselScroll");

        AssertEqual(cardNameLabel.Text, "随机角色", "未确认角色时应默认定位到卡背");
        AssertEqual(confirmButton.Text, "选择随机", "卡背按钮应显示选择随机");
        AssertEqual(selectionStateLabel.Text, "倒计时结束后随机分配", "卡背应说明由服务端随机分配");

        leftButton.EmitSignal(Button.SignalName.Pressed);
        AssertEqual(cardNameLabel.Text, "大布莱恩", "左方向按钮应切换到相邻角色");
        AssertTrue(selectionStateLabel.Text.Contains("其他玩家"), "被占用角色应显示占用玩家昵称");
        AssertTrue(confirmButton.Disabled, "被占用角色不能确认");

        screen._UnhandledInput(new InputEventKey { Keycode = Key.Left, Pressed = true });
        AssertEqual(cardNameLabel.Text, "骰子", "键盘左键应切换角色");

        scroll.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown,
            Pressed = true,
            Position = new Vector2(400, 120)
        });
        AssertEqual(cardNameLabel.Text, "大布莱恩", "鼠标滚轮应切换角色");

        scroll.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(300, 120)
        });
        scroll.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion
        {
            Position = new Vector2(520, 120),
            Relative = new Vector2(220, 0)
        });
        scroll.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = new Vector2(520, 120)
        });
        AssertEqual(cardNameLabel.Text, "骰子", "鼠标拖动后应吸附到相邻角色");
        await WaitFramesAsync(20);
        AssertCarouselCentered(screen, "拖动结束后当前卡牌应吸附到中央");

        transport.NextSelectErrorCode = "ROOM_CHARACTER_DUPLICATED";
        var beforeConflict = transport.SelectCharacterRequestCount;
        confirmButton.EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => transport.SelectCharacterRequestCount > beforeConflict && !transport.RequestInProgress);
        await WaitUntilAsync(() => networkStatusLabel.Text == "该角色刚刚被其他玩家选择");
        AssertTrue(transport.DetailRequestCount >= 2, "角色冲突后应刷新房间详情");
        AssertFalse(confirmButton.Disabled, "冲突刷新后应恢复可用角色确认");

        var beforeSuccess = transport.SelectCharacterRequestCount;
        confirmButton.EmitSignal(Button.SignalName.Pressed);
        await WaitUntilAsync(() => transport.SelectCharacterRequestCount > beforeSuccess && selectionStateLabel.Text == "你已选择该角色");
        AssertEqual(confirmButton.Text, "已确认", "房间详情确认后按钮应显示已确认");
        AssertTrue(confirmButton.Disabled, "确认具体角色后必须锁定");
        screen.Stop();
        screen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);

        transport.Detail = CreateRoom(DateTime.Now.AddMinutes(1));
        session.SetCurrentRoom(10, "选角测试房间");
        var randomScreen = await CreateScreenAsync(transport);
        var randomConfirmButton = randomScreen.GetNode<Button>("RootMargin/Layout/ConfirmRow/ConfirmButton");
        var randomStateLabel = randomScreen.GetNode<Label>("RootMargin/Layout/SelectionStateLabel");
        var randomCardName = randomScreen.GetNode<Label>("RootMargin/Layout/CardNameLabel");
        var beforeRandom = transport.SelectCharacterRequestCount;
        randomConfirmButton.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertEqual(transport.SelectCharacterRequestCount, beforeRandom, "选择卡背不得调用 select-character");
        AssertEqual(transport.Detail.Members![0].SelectedCharacterId, (long?)null, "选择卡背必须保持服务端 selectedCharacterId=null");
        AssertEqual(randomStateLabel.Text, "已选择随机，等待开局", "卡背确认后应显示等待随机状态");
        AssertEqual(randomCardName.Text, "随机角色", "随机玩家在选角阶段不能提前看到最终角色");
        AssertTrue(randomConfirmButton.Disabled, "选择随机后应锁定轮播和确认按钮");
        randomScreen.Stop();
        randomScreen.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);

        transport.Detail = CreateRoom(DateTime.Now.AddSeconds(-1));
        session.SetCurrentRoom(10, "选角测试房间");
        var expiredScreen = await CreateScreenAsync(transport);
        var expiredCountdown = expiredScreen.GetNode<Label>("RootMargin/Layout/CountdownLabel");
        var expiredState = expiredScreen.GetNode<Label>("RootMargin/Layout/SelectionStateLabel");
        var expiredConfirm = expiredScreen.GetNode<Button>("RootMargin/Layout/ConfirmRow/ConfirmButton");
        AssertEqual(expiredCountdown.Text, "正在等待服务端开始游戏", "倒计时归零后应等待服务端开局");
        AssertEqual(expiredState.Text, "选角结束，正在等待游戏开始", "截止后应保持当前画面并显示等待状态");
        AssertTrue(expiredConfirm.Disabled, "截止后必须禁用提交");
        expiredScreen.Stop();
        expiredScreen.QueueFree();

        apiClient.ClearTransportForTests();
        session.ClearAuth();

        if (_failures.Count == 0)
        {
            GD.Print("正式选角场景集成测试全部通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
    }

    private async Task<CharacterSelectScreen> CreateScreenAsync(CharacterSelectTransport transport)
    {
        var scene = GD.Load<PackedScene>("res://scenes/screens/CharacterSelectScreen.tscn");
        var screen = scene.Instantiate<CharacterSelectScreen>();
        Root.AddChild(screen);
        var characterRequests = transport.CharacterRequestCount;
        var detailRequests = transport.DetailRequestCount;
        screen.Start();
        await WaitUntilAsync(() =>
            transport.CharacterRequestCount > characterRequests
            && transport.DetailRequestCount > detailRequests
            && screen.GetNode<HBoxContainer>("RootMargin/Layout/CarouselFrame/CarouselScroll/CarouselContent").GetChildCount() == 5
        );
        screen.GetNode<Timer>("PollTimer").Stop();
        await WaitFramesAsync(3);
        return screen;
    }

    private void AssertCarouselCentered(CharacterSelectScreen screen, string message)
    {
        var scroll = screen.GetNode<ScrollContainer>("RootMargin/Layout/CarouselFrame/CarouselScroll");
        var content = screen.GetNode<HBoxContainer>("RootMargin/Layout/CarouselFrame/CarouselScroll/CarouselContent");
        CharacterCardView? currentCard = null;
        var currentName = screen.GetNode<Label>("RootMargin/Layout/CardNameLabel").Text;
        foreach (var child in content.GetChildren())
        {
            if (child is CharacterCardView card && card.Option.Name == currentName)
            {
                currentCard = card;
                break;
            }
        }

        if (currentCard == null)
        {
            _failures.Add(message);
            return;
        }

        var viewportCenter = scroll.ScrollHorizontal + scroll.Size.X * 0.5f;
        var cardCenter = currentCard.Position.X + currentCard.Size.X * 0.5f;
        AssertTrue(Math.Abs(viewportCenter - cardCenter) <= 6.0f, message);
    }

    private async Task WaitFramesAsync(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var frame = 0; frame < 240 && !condition(); frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        AssertTrue(condition(), "正式选角场景异步测试等待超时");
    }

    private static RoomDetailDto CreateRoom(DateTime deadline)
    {
        return new RoomDetailDto
        {
            RoomId = 10,
            RoomName = "选角测试房间",
            OwnerUserId = 1,
            OwnerNickname = "当前玩家",
            Status = "CHARACTER_SELECTING",
            CurrentPlayers = 2,
            MaxPlayers = 4,
            CharacterSelectionDeadline = deadline.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
            Members =
            [
                new RoomMemberDto { UserId = 1, Nickname = "当前玩家", SeatNo = 1, MemberRole = "OWNER", SelectedCharacterId = null },
                new RoomMemberDto { UserId = 2, Nickname = "其他玩家", SeatNo = 2, MemberRole = "MEMBER", SelectedCharacterId = 102 }
            ]
        };
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

    private sealed class CharacterSelectTransport : IApiTransport
    {
        public RoomDetailDto Detail { get; set; } = new();
        public int CharacterRequestCount { get; private set; }
        public int DetailRequestCount { get; private set; }
        public int SelectCharacterRequestCount { get; private set; }
        public bool RequestInProgress { get; private set; }
        public string? NextSelectErrorCode { get; set; }

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            RequestInProgress = true;
            await Task.Delay(5);
            RequestInProgress = false;

            if (path == "/characters" && typeof(T) == typeof(List<CharacterDto>))
            {
                CharacterRequestCount++;
                var characters = new List<CharacterDto>
                {
                    new() { CharacterId = 101, Code = "INITIAL_CHANCE_DICE", Name = "骰子", CharacterType = "INITIAL" },
                    new() { CharacterId = 102, Code = "INITIAL_BIG_BRIAN", Name = "大布莱恩", CharacterType = "INITIAL" }
                };
                return (ApiResult<T>)(object)ApiResult<List<CharacterDto>>.Success(characters, "success", 200);
            }

            if (path == "/rooms/10" && method == HttpClient.Method.Get && typeof(T) == typeof(RoomDetailDto))
            {
                DetailRequestCount++;
                return (ApiResult<T>)(object)ApiResult<RoomDetailDto>.Success(Detail, "success", 200);
            }

            if (path == "/rooms/10/select-character" && method == HttpClient.Method.Post)
            {
                SelectCharacterRequestCount++;
                if (!string.IsNullOrWhiteSpace(NextSelectErrorCode))
                {
                    var errorCode = NextSelectErrorCode;
                    NextSelectErrorCode = null;
                    return ApiResult<T>.Failure("角色选择失败", errorCode, 409);
                }

                var property = body?.GetType().GetProperty("selectedCharacterId");
                var selectedId = property?.GetValue(body) is long value ? value : (long?)null;
                Detail.Members![0].SelectedCharacterId = selectedId;
                return ApiResult<T>.Success(default, "success", 200);
            }

            return ApiResult<T>.Failure("测试未处理的接口", "UNHANDLED_TEST_REQUEST", 500);
        }
    }
}
