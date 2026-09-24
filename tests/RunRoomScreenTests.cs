using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunRoomScreenTests : SceneTree
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
            User = new UserDto { UserId = 1, Username = "", Nickname = "房主" }
        });
        session.SetCurrentRoom(10, "测试房间");

        var transport = new RoomFlowTransport { Detail = CreateReadyRoom() };
        apiClient.SetTransportForTests(transport);

        var roomScene = GD.Load<PackedScene>("res://scenes/screens/RoomScreen.tscn");
        var roomScreen = roomScene.Instantiate<RoomScreen>();
        Root.AddChild(roomScreen);
        var characterTransitions = 0;
        roomScreen.EnterCharacterSelect += () => characterTransitions++;
        roomScreen.Start();

        await WaitUntilAsync(() => transport.DetailRequestCount >= 1 && !transport.RequestInProgress);
        roomScreen.GetNode<Timer>("PollTimer").Stop();

        var actionButton = roomScreen.GetNode<Button>("RootMargin/Layout/BottomBar/ActionButton");
        var memberList = roomScreen.GetNode<VBoxContainer>("RootMargin/Layout/MemberScroll/MemberList");
        AssertEqual(actionButton.Text, "开始游戏", "房主应显示开始游戏按钮");
        AssertFalse(actionButton.Disabled, "所有普通成员准备后开始按钮应启用");
        AssertEqual(memberList.GetChildCount(), 2, "房间成员应平稳渲染为两行");

        transport.Detail = CreateReadyRoom();
        transport.Detail.Members![1].ReadyStatus = "NOT_READY";
        var firstRow = memberList.GetChild(0);
        await TriggerPollAsync(roomScreen.GetNode<Timer>("PollTimer"), transport);
        AssertTrue(actionButton.Disabled, "普通成员未准备时开始按钮应禁用");
        AssertTrue(memberList.GetChild(0) == firstRow, "刷新成员状态时应复用现有成员行");

        transport.Detail = CreateReadyRoom();
        transport.Detail.Status = "CHARACTER_SELECTING";
        await TriggerPollAsync(roomScreen.GetNode<Timer>("PollTimer"), transport);
        AssertEqual(characterTransitions, 1, "选角状态只能触发一次页面跳转");
        roomScreen.GetNode<Timer>("PollTimer").EmitSignal(Timer.SignalName.Timeout);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertEqual(characterTransitions, 1, "重复轮询不能重复触发选角跳转");
        roomScreen.Stop();
        roomScreen.QueueFree();

        session.SetCurrentRoom(10, "测试房间");
        transport.Detail = CreateReadyRoom();
        transport.Detail.Status = "WAITING";
        var characterScene = GD.Load<PackedScene>("res://scenes/screens/CharacterSelectScreen.tscn");
        var characterScreen = characterScene.Instantiate<CharacterSelectScreen>();
        Root.AddChild(characterScreen);
        var roomTransitions = 0;
        characterScreen.ReturnRoom += () => roomTransitions++;
        characterScreen.Start();
        await WaitUntilAsync(() => roomTransitions == 1);
        AssertEqual(roomTransitions, 1, "WAITING 应从选角测试页返回房间");
        characterScreen.Stop();
        characterScreen.QueueFree();

        session.SetCurrentRoom(10, "测试房间");
        transport.Detail = CreateReadyRoom();
        transport.Detail.Status = "IN_GAME";
        transport.Detail.CurrentGameId = 88;
        var gameCharacterScreen = characterScene.Instantiate<CharacterSelectScreen>();
        Root.AddChild(gameCharacterScreen);
        var gameTransitions = 0;
        gameCharacterScreen.EnterGameTest += () => gameTransitions++;
        gameCharacterScreen.Start();
        await WaitUntilAsync(() => gameTransitions == 1);
        AssertEqual(session.CurrentGameId, 88L, "进入游戏测试页前应保存 gameId");
        gameCharacterScreen.Stop();
        gameCharacterScreen.QueueFree();

        await VerifyApiPathsAsync(apiClient, transport);
        await VerifyLobbyRoutesToRoomAsync(session, transport);
        apiClient.ClearTransportForTests();
        session.ClearAuth();

        if (_failures.Count == 0)
        {
            GD.Print("房间场景集成测试全部通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
    }

    private async Task TriggerPollAsync(Timer timer, RoomFlowTransport transport)
    {
        var previousCount = transport.DetailRequestCount;
        timer.EmitSignal(Timer.SignalName.Timeout);
        await WaitUntilAsync(() => transport.DetailRequestCount > previousCount && !transport.RequestInProgress);
    }

    private async Task VerifyApiPathsAsync(ApiClient apiClient, RoomFlowTransport transport)
    {
        await apiClient.GetRoomDetailAsync(10);
        AssertEqual(transport.LastPath, "/rooms/10", "房间详情接口路径应正确");
        await apiClient.ReadyRoomAsync(10);
        AssertEqual(transport.LastPath, "/rooms/10/ready", "准备接口路径应正确");
        await apiClient.UnreadyRoomAsync(10);
        AssertEqual(transport.LastPath, "/rooms/10/unready", "取消准备接口路径应正确");
        await apiClient.LeaveRoomAsync(10);
        AssertEqual(transport.LastPath, "/rooms/10/leave", "离房接口路径应正确");
        await apiClient.StartCharacterSelectionAsync(10);
        AssertEqual(transport.LastPath, "/rooms/10/start-character-selection", "开启选角接口路径应正确");
    }

    private async Task VerifyLobbyRoutesToRoomAsync(Session session, RoomFlowTransport transport)
    {
        session.SetCurrentRoom(10, "测试房间");
        transport.Detail = CreateReadyRoom();
        var appScene = GD.Load<PackedScene>("res://scenes/app/App.tscn");
        var app = appScene.Instantiate<App>();
        Root.AddChild(app);
        var pageHost = app.GetNode<Control>("MainLayout/PageHost");

        app.ShowLobby();
        var createLobby = pageHost.GetChild(-1) as LobbyScreen;
        createLobby?.EmitSignal(LobbyScreen.SignalName.EnterRoom);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertTrue(pageHost.GetChild(-1) is RoomScreen, "创建房间成功后应进入正式 RoomScreen");

        app.ShowLobby();
        var joinLobby = pageHost.GetChild(-1) as LobbyScreen;
        joinLobby?.EmitSignal(LobbyScreen.SignalName.EnterRoom);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertTrue(pageHost.GetChild(-1) is RoomScreen, "加入房间成功后应进入正式 RoomScreen");

        app.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var frame = 0; frame < 180 && !condition(); frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        AssertTrue(condition(), "异步场景测试等待超时");
    }

    private static RoomDetailDto CreateReadyRoom()
    {
        return new RoomDetailDto
        {
            RoomId = 10,
            RoomName = "测试房间",
            OwnerUserId = 1,
            OwnerNickname = "房主",
            Status = "WAITING",
            CurrentPlayers = 2,
            MaxPlayers = 4,
            Members =
            [
                new RoomMemberDto { UserId = 1, Username = "owner", Nickname = "房主", SeatNo = 1, MemberRole = "OWNER", ReadyStatus = "NOT_READY" },
                new RoomMemberDto { UserId = 2, Username = "member", Nickname = "成员", SeatNo = 2, MemberRole = "MEMBER", ReadyStatus = "READY" }
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

    private sealed class RoomFlowTransport : IApiTransport
    {
        public RoomDetailDto Detail { get; set; } = new();
        public int DetailRequestCount { get; private set; }
        public bool RequestInProgress { get; private set; }
        public string LastPath { get; private set; } = "";

        public async Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query)
        {
            LastPath = path;
            RequestInProgress = true;
            await Task.Delay(5);
            RequestInProgress = false;

            if (typeof(T) == typeof(RoomDetailDto))
            {
                DetailRequestCount++;
                return (ApiResult<T>)(object)ApiResult<RoomDetailDto>.Success(Detail, "success", 200);
            }

            if (typeof(T) == typeof(RoomPageDto))
            {
                var page = new RoomPageDto { Records = [], Page = 1, PageSize = 20, Total = 0 };
                return (ApiResult<T>)(object)ApiResult<RoomPageDto>.Success(page, "success", 200);
            }

            return ApiResult<T>.Success(default, "success", 200);
        }
    }
}
