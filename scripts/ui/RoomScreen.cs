using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class RoomScreen : Control
{
    [Signal]
    public delegate void ReturnLobbyEventHandler();

    [Signal]
    public delegate void EnterCharacterSelectEventHandler();

    [Signal]
    public delegate void EnterGameEventHandler();

    private readonly PackedScene _memberRowScene = GD.Load<PackedScene>("res://scenes/components/RoomMemberRow.tscn");
    private readonly Dictionary<long, RoomMemberRow> _memberRows = new();
    private readonly RoomNavigationGate _navigationGate = new();

    private Button _backButton = null!;
    private Label _roomNameLabel = null!;
    private Label _roomMetaLabel = null!;
    private Label _ownerLabel = null!;
    private Label _roomStatusLabel = null!;
    private Label _syncStatusLabel = null!;
    private VBoxContainer _memberList = null!;
    private Button _actionButton = null!;
    private Timer _pollTimer = null!;
    private Control _leaveConfirm = null!;
    private Label _leaveConfirmMessage = null!;
    private Label _leaveConfirmStatus = null!;
    private Button _leaveYesButton = null!;
    private Button _leaveNoButton = null!;

    private RoomDetailDto? _room;
    private bool _active;
    private bool _polling;
    private bool _operationInProgress;
    private int _generation;
    private int _requestSequence;
    private Task _pollTask = Task.CompletedTask;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");
    private long CurrentUserId => Session.User?.UserId ?? 0;

    public override void _Ready()
    {
        _backButton = GetNode<Button>("RootMargin/Layout/Header/BackButton");
        _roomNameLabel = GetNode<Label>("RootMargin/Layout/Header/TitleBox/RoomNameLabel");
        _roomMetaLabel = GetNode<Label>("RootMargin/Layout/Header/TitleBox/RoomMetaLabel");
        _ownerLabel = GetNode<Label>("RootMargin/Layout/InfoBar/OwnerLabel");
        _roomStatusLabel = GetNode<Label>("RootMargin/Layout/InfoBar/RoomStatusLabel");
        _syncStatusLabel = GetNode<Label>("RootMargin/Layout/BottomBar/SyncStatusLabel");
        _memberList = GetNode<VBoxContainer>("RootMargin/Layout/MemberScroll/MemberList");
        _actionButton = GetNode<Button>("RootMargin/Layout/BottomBar/ActionButton");
        _pollTimer = GetNode<Timer>("PollTimer");
        _leaveConfirm = GetNode<Control>("LeaveConfirm");
        _leaveConfirmMessage = GetNode<Label>("LeaveConfirm/Center/Panel/Margin/Layout/MessageLabel");
        _leaveConfirmStatus = GetNode<Label>("LeaveConfirm/Center/Panel/Margin/Layout/StatusLabel");
        _leaveYesButton = GetNode<Button>("LeaveConfirm/Center/Panel/Margin/Layout/ButtonRow/YesButton");
        _leaveNoButton = GetNode<Button>("LeaveConfirm/Center/Panel/Margin/Layout/ButtonRow/NoButton");

        _backButton.Pressed += OpenLeaveConfirm;
        _actionButton.Pressed += OnActionPressed;
        _leaveYesButton.Pressed += OnLeaveConfirmed;
        _leaveNoButton.Pressed += CloseLeaveConfirm;
        _pollTimer.Timeout += () => _ = RefreshRoomAsync(false);
        _pollTimer.WaitTime = 2.0;
        _pollTimer.OneShot = false;
        _leaveConfirm.Hide();
    }

    public void Start()
    {
        _active = true;
        _generation++;
        _navigationGate.Reset();
        _roomNameLabel.Text = string.IsNullOrWhiteSpace(Session.CurrentRoomName) ? "房间" : Session.CurrentRoomName;
        _roomMetaLabel.Text = $"房间 ID：{Session.CurrentRoomId?.ToString() ?? "-"}";
        ShowStatus("正在同步房间信息...", false);
        _pollTimer.Start();
        _ = RefreshRoomAsync(true);
    }

    public void Stop()
    {
        _active = false;
        _generation++;
        _pollTimer.Stop();
        _leaveConfirm.Hide();
    }

    private Task RefreshRoomAsync(bool initial)
    {
        if (!_active || !Session.CurrentRoomId.HasValue)
        {
            return Task.CompletedTask;
        }

        if (_polling)
        {
            return _pollTask;
        }

        _polling = true;
        var generation = _generation;
        var sequence = ++_requestSequence;
        _pollTask = RefreshRoomCoreAsync(Session.CurrentRoomId.Value, generation, sequence, initial);
        return _pollTask;
    }

    private async Task RefreshRoomCoreAsync(long roomId, int generation, int sequence, bool initial)
    {
        var result = await ApiClient.GetRoomDetailAsync(roomId);
        if (!IsResponseCurrent(generation, sequence))
        {
            return;
        }

        _polling = false;
        if (!result.Ok || result.Data == null)
        {
            if (RoomStateModel.TargetForError(result.ErrorCode) == RoomPageTarget.Lobby)
            {
                NavigateToLobby();
                return;
            }

            ShowStatus(result.Message, true);
            return;
        }

        var room = result.Data;
        if (CurrentUserId <= 0 || !RoomStateModel.ContainsUser(room, CurrentUserId))
        {
            NavigateToLobby();
            return;
        }

        _room = room;
        if (Session.CurrentRoomId != room.RoomId || !string.Equals(Session.CurrentRoomName, room.RoomName, StringComparison.Ordinal))
        {
            Session.SetCurrentRoom(room.RoomId, room.RoomName);
        }

        ApplyRoom(room);
        ShowStatus("", false);
        NavigateForTarget(RoomStateModel.TargetForRoom(room));
    }

    private bool IsResponseCurrent(int generation, int sequence)
    {
        return IsInsideTree() && _active && generation == _generation && sequence == _requestSequence;
    }

    private void ApplyRoom(RoomDetailDto room)
    {
        _roomNameLabel.Text = room.RoomName;
        _roomMetaLabel.Text = $"房间 ID：{room.RoomId}    人数：{room.CurrentPlayers}/{room.MaxPlayers}";
        _ownerLabel.Text = $"房主：{room.OwnerNickname}";
        _roomStatusLabel.Text = $"状态：{RoomStatusText(room.Status)}";
        UpdateMemberRows(room);
        UpdateActionButton();
    }

    private void UpdateMemberRows(RoomDetailDto room)
    {
        var sortedMembers = RoomStateModel.SortedMembers(room);
        var currentIds = new HashSet<long>();

        for (var index = 0; index < sortedMembers.Count; index++)
        {
            var member = sortedMembers[index];
            currentIds.Add(member.UserId);
            if (!_memberRows.TryGetValue(member.UserId, out var row))
            {
                row = _memberRowScene.Instantiate<RoomMemberRow>();
                _memberList.AddChild(row);
                _memberRows[member.UserId] = row;
            }

            row.UpdateMember(member);
            _memberList.MoveChild(row, index);
        }

        foreach (var pair in new List<KeyValuePair<long, RoomMemberRow>>(_memberRows))
        {
            if (currentIds.Contains(pair.Key))
            {
                continue;
            }

            _memberRows.Remove(pair.Key);
            pair.Value.QueueFree();
        }
    }

    private void UpdateActionButton()
    {
        if (_room == null)
        {
            _actionButton.Disabled = true;
            return;
        }

        if (RoomStateModel.IsOwner(_room, CurrentUserId))
        {
            _actionButton.Text = "开始游戏";
            _actionButton.Disabled = !RoomStateModel.CanOwnerStart(_room, CurrentUserId, _operationInProgress);
            return;
        }

        var isReady = RoomStateModel.IsReady(_room, CurrentUserId);
        _actionButton.Text = isReady ? "取消准备" : "准备";
        _actionButton.Disabled = _operationInProgress || !string.Equals(_room.Status, "WAITING", StringComparison.OrdinalIgnoreCase);
    }

    private void OnActionPressed()
    {
        if (_room == null || _operationInProgress)
        {
            return;
        }

        if (RoomStateModel.IsOwner(_room, CurrentUserId))
        {
            _ = StartCharacterSelectionAsync();
        }
        else
        {
            _ = ToggleReadyAsync();
        }
    }

    private async Task ToggleReadyAsync()
    {
        if (_room == null || !string.Equals(_room.Status, "WAITING", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var makeReady = !RoomStateModel.IsReady(_room, CurrentUserId);
        SetOperationInProgress(true);
        var roomId = _room.RoomId;
        var result = makeReady
            ? await ApiClient.ReadyRoomAsync(roomId)
            : await ApiClient.UnreadyRoomAsync(roomId);

        if (!IsInsideTree() || !_active)
        {
            return;
        }

        SetOperationInProgress(false);
        if (!result.Ok || result.Data == null)
        {
            ShowStatus(result.Message, true);
            return;
        }

        var member = RoomStateModel.CurrentMember(_room, CurrentUserId);
        if (member != null)
        {
            member.ReadyStatus = RoomStateModel.ReadyStatusAfterAction(result.Data.Ready);
            ApplyRoom(_room);
        }

        ShowStatus(result.Data.Ready ? "已准备。" : "已取消准备。", false);
        await RefreshAfterOperationAsync();
    }

    private async Task StartCharacterSelectionAsync()
    {
        if (_room == null || !RoomStateModel.CanOwnerStart(_room, CurrentUserId, _operationInProgress))
        {
            return;
        }

        SetOperationInProgress(true);
        var result = await ApiClient.StartCharacterSelectionAsync(_room.RoomId);
        if (!IsInsideTree() || !_active)
        {
            return;
        }

        SetOperationInProgress(false);
        if (result.Ok && result.Data != null && string.Equals(result.Data.Status, "CHARACTER_SELECTING", StringComparison.OrdinalIgnoreCase))
        {
            NavigateForTarget(RoomPageTarget.CharacterSelect);
            return;
        }

        if (string.Equals(result.ErrorCode, "REQUEST_TIMEOUT", StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("开始请求超时，正在重新确认房间状态...", true);
            await RefreshAfterOperationAsync();
            return;
        }

        ShowStatus(result.Message, true);
    }

    private void OpenLeaveConfirm()
    {
        if (_room == null || _operationInProgress)
        {
            return;
        }

        var isOwner = RoomStateModel.IsOwner(_room, CurrentUserId);
        _leaveConfirmMessage.Text = isOwner
            ? "离开将关闭并删除房间，确定关闭房间吗？"
            : "确定退出该房间吗？";
        _leaveConfirmStatus.Text = "";
        SetLeaveConfirmLoading(false);
        _leaveConfirm.Show();
        _leaveNoButton.GrabFocus();
    }

    private void CloseLeaveConfirm()
    {
        if (!_operationInProgress)
        {
            _leaveConfirm.Hide();
        }
    }

    private async void OnLeaveConfirmed()
    {
        if (_room == null || _operationInProgress)
        {
            return;
        }

        var wasOwner = RoomStateModel.IsOwner(_room, CurrentUserId);
        SetOperationInProgress(true);
        SetLeaveConfirmLoading(true);
        var result = await ApiClient.LeaveRoomAsync(_room.RoomId);
        if (!IsInsideTree() || !_active)
        {
            return;
        }

        if (result.Ok && result.Data != null && (!wasOwner || result.Data.RoomDeleted))
        {
            NavigateToLobby();
            return;
        }

        if (string.Equals(result.ErrorCode, "REQUEST_TIMEOUT", StringComparison.OrdinalIgnoreCase)
            || (result.Ok && wasOwner && result.Data?.RoomDeleted != true))
        {
            _leaveConfirmStatus.Text = "正在重新确认离房结果...";
            SetOperationInProgress(false);
            SetLeaveConfirmLoading(false);
            await RefreshAfterOperationAsync();
            if (_active)
            {
                _leaveConfirmStatus.Text = "服务端尚未确认离房，请稍后重试。";
            }

            return;
        }

        SetOperationInProgress(false);
        SetLeaveConfirmLoading(false);
        _leaveConfirmStatus.Text = result.Message;
    }

    private async Task RefreshAfterOperationAsync()
    {
        var hadPollInFlight = _polling;
        await RefreshRoomAsync(false);
        if (hadPollInFlight && _active)
        {
            await RefreshRoomAsync(false);
        }
    }

    private void SetOperationInProgress(bool inProgress)
    {
        _operationInProgress = inProgress;
        _backButton.Disabled = inProgress;
        UpdateActionButton();
    }

    private void SetLeaveConfirmLoading(bool loading)
    {
        _leaveYesButton.Disabled = loading;
        _leaveNoButton.Disabled = loading;
        if (loading)
        {
            _leaveConfirmStatus.Text = "正在处理...";
        }
    }

    private void NavigateForTarget(RoomPageTarget target)
    {
        if (!_navigationGate.TryBegin(target))
        {
            return;
        }

        _active = false;
        _pollTimer.Stop();
        if (target == RoomPageTarget.CharacterSelect)
        {
            EmitSignal(SignalName.EnterCharacterSelect);
        }
        else if (target == RoomPageTarget.Game && _room?.CurrentGameId is long gameId)
        {
            Session.SetCurrentGame(gameId);
            EmitSignal(SignalName.EnterGame);
        }
    }

    private void NavigateToLobby()
    {
        if (!_navigationGate.TryBegin(RoomPageTarget.Lobby))
        {
            return;
        }

        _active = false;
        _pollTimer.Stop();
        Session.ClearRoom();
        EmitSignal(SignalName.ReturnLobby);
    }

    private void ShowStatus(string message, bool isError)
    {
        _syncStatusLabel.Text = message;
        _syncStatusLabel.AddThemeColorOverride(
            "font_color",
            isError ? new Color(0.72f, 0.16f, 0.11f) : new Color(0.18f, 0.38f, 0.48f)
        );
    }

    private static string RoomStatusText(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "WAITING" => "等待中",
            "CHARACTER_SELECTING" => "选角中",
            "IN_GAME" => "游戏中",
            _ => "未知"
        };
    }
}
