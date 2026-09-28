using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;
using EquestriaStar.UI;

public partial class RunUnitTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        TestRoomPageCount();
        TestRoomMergeDeduplicates();
        TestRoomSearch();
        TestCanJoin();
        TestApiResponseParsing();
        TestCreateRoomDialogVisibility();
        TestRoomStartConditions();
        TestReadyStateAndMemberDisplay();
        TestRoomNavigationTargets();
        TestSessionRoomClearing();
        TestDuplicateNavigationIsBlocked();
        TestCharacterCardMappings();
        TestCharacterOccupancyAndConfirmation();
        TestCharacterRandomAndDeadlineRules();
        TestCharacterSubmissionFailureRules();
        TestActiveSessionResolution();
        TestActiveSessionSessionUpdates();

        if (_failures.Count == 0)
        {
            GD.Print("全部离线单元测试通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }

        Quit(1);
    }

    private void TestRoomPageCount()
    {
        AssertEqual(RoomListModel.PageCountFromTotal(0, 20, 50), 1, "空列表也只请求第一页");
        AssertEqual(RoomListModel.PageCountFromTotal(41, 20, 50), 3, "total 应换算为页数");
        AssertEqual(RoomListModel.PageCountFromTotal(2000, 20, 50), 50, "页数应受最大页数保护");
    }

    private void TestRoomMergeDeduplicates()
    {
        var merged = RoomListModel.MergeUniqueByRoomId([
            [
                new RoomDto { RoomId = 1, RoomName = "暮光图书馆" },
                new RoomDto { RoomId = 2, RoomName = "苹果园" }
            ],
            [
                new RoomDto { RoomId = 2, RoomName = "重复苹果园" },
                new RoomDto { RoomId = 3, RoomName = "云中城" }
            ]
        ]);

        AssertEqual(merged.Count, 3, "应按 roomId 去重");
        AssertEqual(merged[1].RoomName, "苹果园", "重复 roomId 保留先到记录");
    }

    private void TestRoomSearch()
    {
        var rooms = new List<RoomDto>
        {
            new() { RoomId = 1, RoomName = "Pony Test" },
            new() { RoomId = 2, RoomName = "中文房间" }
        };

        AssertEqual(RoomListModel.FilterRooms(rooms, " pony ").Count, 1, "英文搜索应忽略大小写并裁剪空格");
        AssertEqual(RoomListModel.FilterRooms(rooms, "中文").Count, 1, "中文搜索应直接包含匹配");
        AssertEqual(RoomListModel.FilterRooms(rooms, "").Count, 2, "清空搜索应恢复全部房间");
    }

    private void TestCanJoin()
    {
        AssertTrue(RoomListModel.CanJoin(new RoomDto { Status = "WAITING", CurrentPlayers = 1, MaxPlayers = 4 }), "等待且未满可加入");
        AssertFalse(RoomListModel.CanJoin(new RoomDto { Status = "PLAYING", CurrentPlayers = 1, MaxPlayers = 4 }), "游戏中不可加入");
        AssertFalse(RoomListModel.CanJoin(new RoomDto { Status = "WAITING", CurrentPlayers = 4, MaxPlayers = 4 }), "满员不可加入");
    }

    private void TestApiResponseParsing()
    {
        var okBody = Encoding.UTF8.GetBytes("{\"code\":1,\"msg\":\"success\",\"errorCode\":null,\"data\":{\"roomId\":7,\"roomName\":\"测试房\",\"status\":\"WAITING\",\"maxPlayers\":4,\"currentPlayers\":1}}");
        var ok = ApiClient.ParseHttpResult<RoomDto>(HttpRequest.Result.Success, 200, okBody);
        AssertTrue(ok.Ok, "code == 1 应解析成功");
        AssertEqual(ok.Data?.RoomId, 7L, "成功时应返回 data");

        var failBody = Encoding.UTF8.GetBytes("{\"code\":0,\"msg\":\"请先登录\",\"errorCode\":\"AUTH_REQUIRED\",\"data\":null}");
        var fail = ApiClient.ParseHttpResult<object>(HttpRequest.Result.Success, 200, failBody);
        AssertFalse(fail.Ok, "code != 1 应解析失败");
        AssertTrue(fail.IsAuthError, "认证错误码应标记登录失效");
        AssertEqual(fail.Message, "请先登录", "失败提示优先使用后端中文 msg");

        var missingUserBody = Encoding.UTF8.GetBytes("{\"code\":0,\"msg\":\"用户不存在\",\"errorCode\":\"USER_NOT_FOUND\",\"data\":null}");
        var missingUser = ApiClient.ParseHttpResult<object>(HttpRequest.Result.Success, 200, missingUserBody);
        AssertTrue(missingUser.IsAuthError, "USER_NOT_FOUND 应标记为认证失效");

        var invalid = ApiClient.ParseHttpResult<object>(HttpRequest.Result.Success, 200, Encoding.UTF8.GetBytes("not json"));
        AssertFalse(invalid.Ok, "非 JSON 应失败");
        AssertEqual(invalid.ErrorCode, "INVALID_JSON", "非 JSON 应有明确错误码");
    }

    private void TestCreateRoomDialogVisibility()
    {
        var scene = GD.Load<PackedScene>("res://scenes/components/CreateRoomDialog.tscn");
        var dialog = scene.Instantiate<CreateRoomDialog>();
        AssertFalse(dialog.Visible, "创建房间弹窗实例化后必须保持隐藏");
        dialog.Free();
    }

    private void TestRoomStartConditions()
    {
        var ownerOnly = CreateRoomDetail(
            "WAITING",
            new RoomMemberDto { UserId = 1, Nickname = "房主", SeatNo = 1, MemberRole = "OWNER", ReadyStatus = "NOT_READY" }
        );
        AssertFalse(RoomStateModel.CanOwnerStart(ownerOnly, 1, false), "一人房间不能开始");
        AssertTrue(RoomStateModel.IsOwner(ownerOnly, 1), "应识别当前用户为房主");

        var memberNotReady = CreateRoomDetail(
            "WAITING",
            new RoomMemberDto { UserId = 1, Nickname = "房主", SeatNo = 1, MemberRole = "OWNER", ReadyStatus = "NOT_READY" },
            new RoomMemberDto { UserId = 2, Nickname = "成员", SeatNo = 2, MemberRole = "MEMBER", ReadyStatus = "NOT_READY" }
        );
        AssertFalse(RoomStateModel.CanOwnerStart(memberNotReady, 1, false), "普通成员未准备不能开始");

        memberNotReady.Members![1].ReadyStatus = "READY";
        AssertTrue(RoomStateModel.AllRegularMembersReady(memberNotReady), "所有普通成员准备后应通过准备检查");
        AssertTrue(RoomStateModel.CanOwnerStart(memberNotReady, 1, false), "所有普通成员准备后可以开始");
        AssertTrue(RoomStateModel.CanOwnerStart(memberNotReady, 1, false), "房主 NOT_READY 不影响开始条件");
        AssertFalse(RoomStateModel.CanOwnerStart(memberNotReady, 1, true), "请求进行中不能重复开始");
    }

    private void TestReadyStateAndMemberDisplay()
    {
        AssertEqual(RoomStateModel.ReadyStatusAfterAction(true), "READY", "准备操作应切换为 READY");
        AssertEqual(RoomStateModel.ReadyStatusAfterAction(false), "NOT_READY", "取消准备应切换为 NOT_READY");

        var readyMember = new RoomMemberDto { Nickname = "云宝", MemberRole = "MEMBER", ReadyStatus = "READY" };
        var waitingMember = new RoomMemberDto { Nickname = "柔柔", MemberRole = "MEMBER", ReadyStatus = "NOT_READY" };
        var owner = new RoomMemberDto { Nickname = "暮光", MemberRole = "OWNER", ReadyStatus = "READY" };
        AssertEqual(RoomStateModel.MemberDisplayName(readyMember), "云宝 ✓", "普通成员准备后名字应显示对勾");
        AssertEqual(RoomStateModel.MemberDisplayName(waitingMember), "柔柔", "普通成员未准备时不显示叉号");
        AssertEqual(RoomStateModel.MemberDisplayName(owner), "暮光", "房主不显示准备对勾");
    }

    private void TestRoomNavigationTargets()
    {
        AssertEqual(RoomStateModel.TargetForError("ROOM_NOT_FOUND"), RoomPageTarget.Lobby, "ROOM_NOT_FOUND 应返回大厅");
        AssertEqual(RoomStateModel.TargetForRoom(CreateRoomDetail("CHARACTER_SELECTING")), RoomPageTarget.CharacterSelect, "选角状态应进入正式选角场景");
        AssertEqual(RoomStateModel.TargetForCharacterSelect(CreateRoomDetail("WAITING")), RoomPageTarget.Room, "选角取消后应返回正式房间");

        var inGame = CreateRoomDetail("IN_GAME");
        inGame.CurrentGameId = 9001;
        AssertEqual(RoomStateModel.TargetForRoom(inGame), RoomPageTarget.Game, "IN_GAME 且存在 gameId 应进入正式游戏场景");
        AssertEqual(RoomStateModel.TargetForCharacterSelect(inGame), RoomPageTarget.Game, "选角页应进入同一个正式游戏场景");

        inGame.CurrentGameId = null;
        AssertEqual(RoomStateModel.TargetForRoom(inGame), RoomPageTarget.Stay, "缺少 gameId 时不能进入正式游戏场景");
    }

    private void TestSessionRoomClearing()
    {
        AssertSessionClear("离开后应清理 Session");
        AssertSessionClear("关闭房间后应清理 Session");
    }

    private void TestDuplicateNavigationIsBlocked()
    {
        var gate = new RoomNavigationGate();
        AssertTrue(gate.TryBegin(RoomPageTarget.CharacterSelect), "首次状态跳转应执行");
        AssertFalse(gate.TryBegin(RoomPageTarget.CharacterSelect), "重复轮询响应不能造成重复跳转");
        AssertFalse(gate.TryBegin(RoomPageTarget.Game), "页面切换开始后不能再触发第二个目标");
        AssertEqual(gate.TransitionCount, 1, "重复响应只能产生一次跳转");
    }

    private void TestCharacterCardMappings()
    {
        var expected = new Dictionary<string, string>
        {
            ["INITIAL_CHANCE_DICE"] = "res://assets/characters/cards/骰子.png",
            ["INITIAL_BIG_BRIAN"] = "res://assets/characters/cards/大布莱恩.jpg",
            ["INITIAL_ORANGE_FEATHER"] = "res://assets/characters/cards/橙羽.jpg",
            ["INITIAL_XUN_YU"] = "res://assets/characters/cards/循雨.jpg",
            ["INITIAL_HONG_FANG"] = "res://assets/characters/cards/hf.jpg",
            ["INITIAL_EGG_WHITE"] = "res://assets/characters/cards/蛋黄.jpg",
            ["INITIAL_TURBID_STAR"] = "res://assets/characters/cards/浊星.jpg",
            ["INITIAL_EQUATIONS"] = "res://assets/characters/cards/正负等式.jpg",
            ["INITIAL_YE_LING"] = "res://assets/characters/cards/夜灵.jpg",
            ["INITIAL_DAN_QING"] = "res://assets/characters/cards/丹青.jpg",
            ["RANDOM"] = CharacterSelectionModel.CardBackPath
        };

        foreach (var pair in expected)
        {
            var path = CharacterSelectionModel.ResolveCardPath(pair.Key, out var fallback);
            AssertEqual(path, pair.Value, $"角色 {pair.Key} 应映射正确素材");
            AssertFalse(fallback, $"已知角色 {pair.Key} 不应使用占位图");
        }

        var unknownPath = CharacterSelectionModel.ResolveCardPath("UNKNOWN_CHARACTER", out var unknownFallback);
        AssertEqual(unknownPath, CharacterSelectionModel.CardBackPath, "未知 code 应使用卡背占位");
        AssertTrue(unknownFallback, "未知 code 应明确标记为占位资源");

        var options = CharacterSelectionModel.BuildOptions([
            new CharacterDto { CharacterId = 1, Code = "INITIAL_CHANCE_DICE", Name = "骰子", CharacterType = "INITIAL" },
            new CharacterDto { CharacterId = 2, Code = "LATER_CHARACTER", Name = "后续角色", CharacterType = "OTHER" }
        ]);
        AssertEqual(options.Count, 2, "只应展示 INITIAL 角色并追加 RANDOM");
        AssertTrue(options[1].IsRandom, "角色列表末尾应追加 RANDOM 虚拟选项");
    }

    private void TestCharacterOccupancyAndConfirmation()
    {
        var room = CreateRoomDetail(
            "CHARACTER_SELECTING",
            new RoomMemberDto { UserId = 1, Nickname = "当前玩家", MemberRole = "MEMBER", SelectedCharacterId = 101 },
            new RoomMemberDto { UserId = 2, Nickname = "其他玩家", MemberRole = "MEMBER", SelectedCharacterId = 202 }
        );
        var current = CharacterSelectionModel.GetOccupancy(room, 101, 1);
        var occupied = CharacterSelectionModel.GetOccupancy(room, 202, 1);
        var available = CharacterSelectionModel.GetOccupancy(room, 303, 1);

        AssertTrue(current.IsSelectedByCurrentUser, "应识别当前玩家已确认的角色");
        AssertTrue(occupied.IsSelectedByOther, "应识别被其他玩家占用的角色");
        AssertEqual(occupied.OtherNickname, "其他玩家", "占用信息应包含玩家昵称");
        AssertFalse(available.IsSelectedByOther, "未被选择的角色应保持可用");

        var option = new CharacterCardOption { CharacterId = 202, Code = "ROLE", Name = "角色" };
        AssertFalse(
            CharacterSelectionModel.CanConfirm(option, occupied, false, false, false, false, true),
            "被其他玩家占用的角色不能确认"
        );
        AssertFalse(
            CharacterSelectionModel.CanConfirm(option, available, true, false, false, false, true),
            "当前玩家已确认角色后必须锁定"
        );
    }

    private void TestCharacterRandomAndDeadlineRules()
    {
        var random = new CharacterCardOption
        {
            CharacterId = null,
            Code = CharacterSelectionModel.RandomCode,
            Name = "随机角色",
            AssetPath = CharacterSelectionModel.CardBackPath,
            IsRandom = true
        };
        var canConfirmRandom = CharacterSelectionModel.CanConfirm(
            random,
            CharacterOccupancy.Available,
            false,
            false,
            false,
            false,
            true
        );
        AssertTrue(canConfirmRandom, "卡背在截止前应允许选择随机");
        AssertEqual(CharacterSelectionModel.ConfirmActionFor(random, canConfirmRandom), CharacterConfirmAction.LockRandom, "卡背确认只能锁定本地随机状态");
        AssertEqual(random.CharacterId, (long?)null, "卡背状态必须保持 selectedCharacterId=null");
        AssertFalse(random.CharacterId.HasValue, "选角阶段不能提前展示随机结果");

        var real = new CharacterCardOption { CharacterId = 1, Code = "ROLE", Name = "角色" };
        AssertFalse(
            CharacterSelectionModel.CanConfirm(real, CharacterOccupancy.Available, false, false, true, false, true),
            "截止后禁止提交角色"
        );
        AssertFalse(
            CharacterSelectionModel.CanConfirm(real, CharacterOccupancy.Available, false, false, false, false, false),
            "缺少截止时间时禁止提交角色"
        );

        var now = new DateTimeOffset(2026, 9, 24, 8, 30, 0, TimeSpan.FromHours(8));
        AssertEqual(CharacterSelectionModel.RemainingSeconds(now.AddMilliseconds(100), now), 1, "倒计时应向上取整");
        AssertEqual(CharacterSelectionModel.RemainingSeconds(now.AddSeconds(-1), now), 0, "过期倒计时不得为负数");
        AssertTrue(CharacterSelectionModel.TryParseServerDeadline("2026-09-24T08:30:30+08:00", out _), "应解析带时区的截止时间");
        AssertFalse(CharacterSelectionModel.TryParseServerDeadline(null, out _), "缺少截止时间时应保持同步状态");
    }

    private void TestCharacterSubmissionFailureRules()
    {
        AssertEqual(
            CharacterSelectionModel.FailureActionFor("ROOM_CHARACTER_DUPLICATED"),
            CharacterSubmitFailureAction.RefreshRoom,
            "角色冲突后应刷新房间详情"
        );
        AssertEqual(
            CharacterSelectionModel.FailureActionFor("ROOM_CHARACTER_SELECTION_EXPIRED"),
            CharacterSubmitFailureAction.ExpireAndWait,
            "选角过期后应等待自动开局"
        );
        AssertEqual(
            CharacterSelectionModel.FailureActionFor("ROOM_NOT_FOUND"),
            CharacterSubmitFailureAction.ReturnLobby,
            "房间不存在时应返回大厅"
        );
    }

    private void TestActiveSessionResolution()
    {
        var none = ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "NONE" });
        AssertEqual(none.Target, ActiveSessionTarget.Lobby, "NONE 应进入大厅");

        var waiting = ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "WAITING", RoomId = 10 });
        AssertEqual(waiting.Target, ActiveSessionTarget.Room, "WAITING 应进入房间");
        AssertEqual(waiting.RoomId, 10L, "WAITING 应保留 roomId");

        var selecting = ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "CHARACTER_SELECTING", RoomId = 11 });
        AssertEqual(selecting.Target, ActiveSessionTarget.CharacterSelect, "CHARACTER_SELECTING 应进入选角场景");

        var game = ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "IN_GAME", RoomId = 12, GameId = 20 });
        AssertEqual(game.Target, ActiveSessionTarget.Game, "IN_GAME 应进入正式游戏场景");
        AssertEqual(game.GameId, 20L, "IN_GAME 应保留 gameId");

        AssertEqual(
            ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "UNKNOWN" }).Target,
            ActiveSessionTarget.Invalid,
            "未知 phase 必须拒绝跳转"
        );
        AssertEqual(
            ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "WAITING" }).Target,
            ActiveSessionTarget.Invalid,
            "WAITING 缺少 roomId 必须报错"
        );
        AssertEqual(
            ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "IN_GAME", RoomId = 10 }).Target,
            ActiveSessionTarget.Invalid,
            "IN_GAME 缺少 gameId 必须报错"
        );
        AssertEqual(
            ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "NONE", RoomId = 10 }).Target,
            ActiveSessionTarget.Invalid,
            "NONE 携带 roomId 必须报错"
        );
    }

    private void TestActiveSessionSessionUpdates()
    {
        var session = new Session();
        session.SetCurrentRoom(99, "旧房间");
        session.SetCurrentGame(199);
        session.ApplyActiveSession(ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "NONE" }));
        AssertEqual(session.CurrentRoomId, (long?)null, "NONE 应清除旧 roomId");
        AssertEqual(session.CurrentRoomName, "", "NONE 应清除旧房间名");
        AssertEqual(session.CurrentGameId, (long?)null, "NONE 应清除旧 gameId");

        session.SetCurrentGame(200);
        session.ApplyActiveSession(ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "WAITING", RoomId = 20 }));
        AssertEqual(session.CurrentRoomId, 20L, "WAITING 应保存 roomId");
        AssertEqual(session.CurrentGameId, (long?)null, "WAITING 应清除旧 gameId");

        session.ApplyActiveSession(ActiveSessionModel.Resolve(new ActiveSessionResponseDto { Phase = "IN_GAME", RoomId = 30, GameId = 40 }));
        AssertEqual(session.CurrentRoomId, 30L, "IN_GAME 应原子保存 roomId");
        AssertEqual(session.CurrentGameId, 40L, "IN_GAME 应原子保存 gameId");
        session.Free();
    }

    private void AssertSessionClear(string message)
    {
        var session = new Session();
        session.SetCurrentRoom(10, "测试房间");
        session.SetCurrentGame(20);
        session.ClearRoom();
        AssertEqual(session.CurrentRoomId, (long?)null, message);
        AssertEqual(session.CurrentRoomName, "", message);
        AssertEqual(session.CurrentGameId, (long?)null, message);
        session.Free();
    }

    private static RoomDetailDto CreateRoomDetail(string status, params RoomMemberDto[] members)
    {
        return new RoomDetailDto
        {
            RoomId = 10,
            RoomName = "测试房间",
            OwnerUserId = 1,
            OwnerNickname = "房主",
            Status = status,
            CurrentPlayers = members.Length,
            MaxPlayers = 4,
            Members = new List<RoomMemberDto>(members)
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
        if (value)
        {
            _failures.Add(message);
        }
    }

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }
}
