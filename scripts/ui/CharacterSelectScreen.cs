using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class CharacterSelectScreen : Control
{
    [Signal]
    public delegate void ReturnLobbyEventHandler();

    [Signal]
    public delegate void ReturnRoomEventHandler();

    [Signal]
    public delegate void EnterGameEventHandler();

    private readonly PackedScene _cardScene = GD.Load<PackedScene>("res://scenes/components/CharacterCardView.tscn");
    private readonly List<CharacterCardOption> _options = new();
    private readonly List<CharacterCardView> _cards = new();
    private readonly RoomNavigationGate _navigationGate = new();

    private Label _countdownLabel = null!;
    private Label _selectionStateLabel = null!;
    private Label _cardNameLabel = null!;
    private Label _networkStatusLabel = null!;
    private Control _carouselFrame = null!;
    private ScrollContainer _carouselScroll = null!;
    private HBoxContainer _carouselContent = null!;
    private Control _leftSpacer = null!;
    private Control _rightSpacer = null!;
    private Button _leftButton = null!;
    private Button _rightButton = null!;
    private Button _confirmButton = null!;
    private Timer _pollTimer = null!;
    private Timer _countdownTimer = null!;

    private RoomDetailDto? _room;
    private DateTimeOffset? _deadline;
    private long? _pendingCharacterId;
    private bool _active;
    private bool _polling;
    private bool _loadingCharacters;
    private bool _charactersLoaded;
    private bool _operationInProgress;
    private bool _deadlineExpired;
    private bool _randomLocked;
    private bool _selectionInitialized;
    private bool _dragging;
    private bool _dragMoved;
    private float _dragStartX;
    private int _dragStartScroll;
    private int _currentIndex;
    private int _generation;
    private int _requestSequence;
    private Task _pollTask = Task.CompletedTask;
    private Tween? _scrollTween;
    private Tween? _visualTween;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");
    private long CurrentUserId => Session.User?.UserId ?? 0;

    public override void _Ready()
    {
        _countdownLabel = GetNode<Label>("RootMargin/Layout/CountdownLabel");
        _selectionStateLabel = GetNode<Label>("RootMargin/Layout/SelectionStateLabel");
        _cardNameLabel = GetNode<Label>("RootMargin/Layout/CardNameLabel");
        _networkStatusLabel = GetNode<Label>("RootMargin/Layout/NetworkStatusLabel");
        _carouselFrame = GetNode<Control>("RootMargin/Layout/CarouselFrame");
        _carouselScroll = GetNode<ScrollContainer>("RootMargin/Layout/CarouselFrame/CarouselScroll");
        _carouselContent = GetNode<HBoxContainer>("RootMargin/Layout/CarouselFrame/CarouselScroll/CarouselContent");
        _leftSpacer = GetNode<Control>("RootMargin/Layout/CarouselFrame/CarouselScroll/CarouselContent/LeftSpacer");
        _rightSpacer = GetNode<Control>("RootMargin/Layout/CarouselFrame/CarouselScroll/CarouselContent/RightSpacer");
        _leftButton = GetNode<Button>("RootMargin/Layout/CarouselFrame/LeftButton");
        _rightButton = GetNode<Button>("RootMargin/Layout/CarouselFrame/RightButton");
        _confirmButton = GetNode<Button>("RootMargin/Layout/ConfirmRow/ConfirmButton");
        _pollTimer = GetNode<Timer>("PollTimer");
        _countdownTimer = GetNode<Timer>("CountdownTimer");

        _leftButton.Pressed += () => MoveSelection(-1);
        _rightButton.Pressed += () => MoveSelection(1);
        _confirmButton.Pressed += OnConfirmPressed;
        _carouselFrame.Resized += UpdateCarouselLayout;
        _carouselScroll.GuiInput += OnCarouselInput;
        _pollTimer.Timeout += () => _ = RefreshRoomAsync();
        _countdownTimer.Timeout += UpdateCountdown;
        _pollTimer.WaitTime = 1.0;
        _pollTimer.OneShot = false;
        _countdownTimer.WaitTime = 0.2;
        _countdownTimer.OneShot = false;
    }

    public void Start()
    {
        _active = true;
        _generation++;
        _navigationGate.Reset();
        _deadline = null;
        _deadlineExpired = false;
        _randomLocked = false;
        _selectionInitialized = false;
        _pendingCharacterId = null;
        _pollTimer.Start();
        _countdownTimer.Start();
        ShowNetworkStatus("正在同步角色和房间信息...", false);
        UpdateCountdown();
        _ = LoadCharactersAsync(_generation);
        _ = RefreshRoomAsync();
    }

    public void Stop()
    {
        _active = false;
        _generation++;
        _pollTimer.Stop();
        _countdownTimer.Stop();
        _scrollTween?.Kill();
        _visualTween?.Kill();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active || IsCarouselLocked())
        {
            return;
        }

        if (@event.IsActionPressed("ui_left"))
        {
            MoveSelection(-1);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_right"))
        {
            MoveSelection(1);
            GetViewport().SetInputAsHandled();
        }
    }

    private async Task LoadCharactersAsync(int generation)
    {
        if (_loadingCharacters)
        {
            return;
        }

        _loadingCharacters = true;
        var result = await ApiClient.GetCharactersAsync();
        if (!IsInsideTree() || !_active || generation != _generation)
        {
            return;
        }

        _loadingCharacters = false;
        var characters = result.Ok && result.Data != null ? result.Data : [];
        BuildCards(CharacterSelectionModel.BuildOptions(characters));
        _charactersLoaded = true;
        if (!result.Ok)
        {
            ShowNetworkStatus($"{result.Message} 当前仅可选择随机。", true);
        }
        else
        {
            ShowNetworkStatus("", false);
        }

        SynchronizeSelectionFromRoom();
    }

    private Task RefreshRoomAsync()
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
        _pollTask = RefreshRoomCoreAsync(Session.CurrentRoomId.Value, generation, sequence);
        return _pollTask;
    }

    private async Task RefreshRoomCoreAsync(long roomId, int generation, int sequence)
    {
        var result = await ApiClient.GetRoomDetailAsync(roomId);
        if (!IsInsideTree() || !_active || generation != _generation || sequence != _requestSequence)
        {
            return;
        }

        _polling = false;
        if (!result.Ok || result.Data == null)
        {
            if (RoomStateModel.TargetForError(result.ErrorCode) == RoomPageTarget.Lobby)
            {
                Navigate(RoomPageTarget.Lobby, null);
                return;
            }

            ShowNetworkStatus(result.Message, true);
            return;
        }

        var room = result.Data;
        if (CurrentUserId <= 0 || !RoomStateModel.ContainsUser(room, CurrentUserId))
        {
            Navigate(RoomPageTarget.Lobby, null);
            return;
        }

        var target = RoomStateModel.TargetForCharacterSelect(room);
        if (target != RoomPageTarget.Stay)
        {
            _room = room;
            Navigate(target, room.CurrentGameId);
            return;
        }

        _room = room;
        if (Session.CurrentRoomId != room.RoomId || !string.Equals(Session.CurrentRoomName, room.RoomName, StringComparison.Ordinal))
        {
            Session.SetCurrentRoom(room.RoomId, room.RoomName);
        }

        _deadline = CharacterSelectionModel.TryParseServerDeadline(room.CharacterSelectionDeadline, out var parsedDeadline)
            ? parsedDeadline
            : null;
        ShowNetworkStatus("", false);
        UpdateCountdown();
        SynchronizeSelectionFromRoom();
    }

    private void BuildCards(IReadOnlyList<CharacterCardOption> options)
    {
        foreach (var card in _cards)
        {
            card.QueueFree();
        }

        _cards.Clear();
        _options.Clear();
        _options.AddRange(options);

        for (var index = 0; index < _options.Count; index++)
        {
            var cardIndex = index;
            var option = _options[index];
            var card = _cardScene.Instantiate<CharacterCardView>();
            _carouselContent.AddChild(card);
            _carouselContent.MoveChild(card, _carouselContent.GetChildCount() - 2);
            card.Setup(option);
            card.GuiInput += OnCarouselInput;
            card.Pressed += () =>
            {
                if (!_dragMoved && !IsCarouselLocked())
                {
                    SelectIndex(cardIndex, true);
                }
            };
            _cards.Add(card);

            if (option.UsesFallbackAsset)
            {
                GD.PushWarning($"未知角色 code：{option.Code}，已使用角色卡背占位。");
            }
        }

        _currentIndex = Math.Clamp(_currentIndex, 0, Math.Max(0, _cards.Count - 1));
        UpdateCarouselLayout();
        Callable.From(SnapToCurrentImmediate).CallDeferred();
    }

    private void SynchronizeSelectionFromRoom()
    {
        if (_room == null || !_charactersLoaded || _options.Count == 0)
        {
            UpdateSelectionUi();
            return;
        }

        var selectedCharacterId = CharacterSelectionModel.CurrentSelectedCharacterId(_room, CurrentUserId);
        if (selectedCharacterId.HasValue)
        {
            var selectedIndex = _options.FindIndex(option => option.CharacterId == selectedCharacterId.Value);
            if (selectedIndex >= 0)
            {
                _randomLocked = false;
                _pendingCharacterId = null;
                SelectIndex(selectedIndex, false);
            }
        }
        else if (!_selectionInitialized)
        {
            var randomIndex = _options.FindIndex(option => option.IsRandom);
            if (randomIndex >= 0)
            {
                SelectIndex(randomIndex, false);
            }
        }

        _selectionInitialized = true;
        UpdateOccupancy();
        UpdateSelectionUi();
    }

    private void UpdateOccupancy()
    {
        if (_room == null)
        {
            return;
        }

        for (var index = 0; index < _options.Count; index++)
        {
            var occupancy = CharacterSelectionModel.GetOccupancy(_room, _options[index].CharacterId, CurrentUserId);
            _cards[index].SetOccupied(occupancy.IsSelectedByOther);
        }
    }

    private void UpdateCountdown()
    {
        if (_deadline == null)
        {
            _deadlineExpired = false;
            _countdownLabel.Text = "正在同步截止时间";
            _countdownLabel.AddThemeColorOverride("font_color", new Color(0.18f, 0.38f, 0.48f));
            UpdateSelectionUi();
            return;
        }

        var remaining = CharacterSelectionModel.RemainingSeconds(_deadline.Value, DateTimeOffset.Now);
        _deadlineExpired = remaining <= 0;
        if (_deadlineExpired)
        {
            _countdownLabel.Text = "正在等待服务端开始游戏";
            _countdownLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.16f, 0.11f));
        }
        else
        {
            _countdownLabel.Text = $"选择角色 {remaining / 60:00}:{remaining % 60:00}";
            _countdownLabel.AddThemeColorOverride(
                "font_color",
                remaining <= 10 ? new Color(0.78f, 0.17f, 0.1f) : new Color(0.1f, 0.15f, 0.18f)
            );
        }

        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        if (_options.Count == 0 || _currentIndex < 0 || _currentIndex >= _options.Count)
        {
            _selectionStateLabel.Text = "正在加载角色...";
            _cardNameLabel.Text = "";
            _confirmButton.Text = "确定";
            _confirmButton.Disabled = true;
            UpdateNavigationButtons();
            return;
        }

        var option = _options[_currentIndex];
        var selectedCharacterId = _room == null ? null : CharacterSelectionModel.CurrentSelectedCharacterId(_room, CurrentUserId);
        var hasConfirmed = selectedCharacterId.HasValue;
        var occupancy = _room == null
            ? CharacterOccupancy.Available
            : CharacterSelectionModel.GetOccupancy(_room, option.CharacterId, CurrentUserId);
        var hasDeadline = _deadline.HasValue;
        var canConfirm = CharacterSelectionModel.CanConfirm(
            option,
            occupancy,
            hasConfirmed,
            _randomLocked,
            _deadlineExpired,
            _operationInProgress || _pendingCharacterId.HasValue,
            hasDeadline
        );

        _cardNameLabel.Text = option.Name;
        if (_deadlineExpired)
        {
            _selectionStateLabel.Text = "选角结束，正在等待游戏开始";
        }
        else if (!hasDeadline)
        {
            _selectionStateLabel.Text = "正在同步截止时间";
        }
        else if (hasConfirmed && occupancy.IsSelectedByCurrentUser)
        {
            _selectionStateLabel.Text = "你已选择该角色";
        }
        else if (_randomLocked && option.IsRandom)
        {
            _selectionStateLabel.Text = "已选择随机，等待开局";
        }
        else if (_pendingCharacterId.HasValue && _pendingCharacterId == option.CharacterId)
        {
            _selectionStateLabel.Text = "正在等待房间状态确认";
        }
        else if (option.IsRandom)
        {
            _selectionStateLabel.Text = "倒计时结束后随机分配";
        }
        else if (occupancy.IsSelectedByOther)
        {
            _selectionStateLabel.Text = $"该角色已被 {occupancy.OtherNickname} 选择";
        }
        else
        {
            _selectionStateLabel.Text = "该角色当前可选";
        }

        if (hasConfirmed)
        {
            _confirmButton.Text = "已确认";
        }
        else if (_randomLocked)
        {
            _confirmButton.Text = "已选择随机";
        }
        else
        {
            _confirmButton.Text = option.IsRandom ? "选择随机" : "确定";
        }

        _confirmButton.Disabled = !canConfirm;
        UpdateNavigationButtons();
    }

    private void OnConfirmPressed()
    {
        if (_room == null || _options.Count == 0 || _currentIndex >= _options.Count)
        {
            return;
        }

        var option = _options[_currentIndex];
        var selectedId = CharacterSelectionModel.CurrentSelectedCharacterId(_room, CurrentUserId);
        var occupancy = CharacterSelectionModel.GetOccupancy(_room, option.CharacterId, CurrentUserId);
        var canConfirm = CharacterSelectionModel.CanConfirm(
            option,
            occupancy,
            selectedId.HasValue,
            _randomLocked,
            _deadlineExpired,
            _operationInProgress || _pendingCharacterId.HasValue,
            _deadline.HasValue
        );

        switch (CharacterSelectionModel.ConfirmActionFor(option, canConfirm))
        {
            case CharacterConfirmAction.LockRandom:
                _randomLocked = true;
                UpdateSelectionUi();
                break;
            case CharacterConfirmAction.SubmitCharacter when option.CharacterId.HasValue:
                _ = SubmitCharacterAsync(option.CharacterId.Value);
                break;
        }
    }

    private async Task SubmitCharacterAsync(long characterId)
    {
        if (_operationInProgress || !Session.CurrentRoomId.HasValue)
        {
            return;
        }

        _operationInProgress = true;
        _pendingCharacterId = characterId;
        UpdateSelectionUi();
        ShowNetworkStatus("正在确认角色...", false);
        var result = await ApiClient.SelectCharacterAsync(Session.CurrentRoomId.Value, characterId);
        if (!IsInsideTree() || !_active)
        {
            return;
        }

        _operationInProgress = false;
        if (result.Ok)
        {
            ShowNetworkStatus("已提交选择，正在确认房间状态...", false);
            await RefreshAfterSubmitAsync(characterId, false);
            return;
        }

        if (string.Equals(result.ErrorCode, "REQUEST_TIMEOUT", StringComparison.OrdinalIgnoreCase))
        {
            ShowNetworkStatus("请求超时，正在查询房间确认结果...", true);
            await RefreshAfterSubmitAsync(characterId, true);
            return;
        }

        _pendingCharacterId = null;
        switch (CharacterSelectionModel.FailureActionFor(result.ErrorCode))
        {
            case CharacterSubmitFailureAction.RefreshRoom:
                await RefreshAfterOperationAsync();
                if (_active)
                {
                    ShowNetworkStatus("该角色刚刚被其他玩家选择", true);
                }
                break;
            case CharacterSubmitFailureAction.ExpireAndWait:
                _deadlineExpired = true;
                _countdownLabel.Text = "正在等待服务端开始游戏";
                ShowNetworkStatus("选角已结束，正在等待游戏开始。", true);
                UpdateSelectionUi();
                break;
            case CharacterSubmitFailureAction.ReturnLobby:
                Navigate(RoomPageTarget.Lobby, null);
                break;
            default:
                ShowNetworkStatus(result.Message, true);
                UpdateSelectionUi();
                break;
        }
    }

    private async Task RefreshAfterSubmitAsync(long characterId, bool clearPendingIfUnconfirmed)
    {
        await RefreshAfterOperationAsync();
        if (!_active || _room == null)
        {
            return;
        }

        var selectedId = CharacterSelectionModel.CurrentSelectedCharacterId(_room, CurrentUserId);
        if (selectedId == characterId)
        {
            _pendingCharacterId = null;
            ShowNetworkStatus("", false);
            SynchronizeSelectionFromRoom();
            return;
        }

        if (clearPendingIfUnconfirmed && !selectedId.HasValue)
        {
            _pendingCharacterId = null;
            ShowNetworkStatus("房间状态未确认本次选择，请重新选择。", true);
            UpdateSelectionUi();
        }
    }

    private async Task RefreshAfterOperationAsync()
    {
        var hadPollInFlight = _polling;
        await RefreshRoomAsync();
        if (hadPollInFlight && _active)
        {
            await RefreshRoomAsync();
        }
    }

    private void OnCarouselInput(InputEvent @event)
    {
        if (!_active || IsCarouselLocked())
        {
            return;
        }

        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                MoveSelection(-1);
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                MoveSelection(1);
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed)
                {
                    _dragging = true;
                    _dragMoved = false;
                    _dragStartX = button.Position.X;
                    _dragStartScroll = _carouselScroll.ScrollHorizontal;
                    _scrollTween?.Kill();
                }
                else if (_dragging)
                {
                    _dragging = false;
                    var delta = _carouselScroll.ScrollHorizontal - _dragStartScroll;
                    if (Math.Abs(delta) > CurrentCardWidth() * 0.22f)
                    {
                        SelectIndex(_currentIndex + Math.Sign(delta), true);
                    }
                    else
                    {
                        SelectIndex(NearestIndex(), true);
                    }
                }
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            var delta = motion.Position.X - _dragStartX;
            if (Math.Abs(delta) > 4.0f)
            {
                _dragMoved = true;
            }

            _carouselScroll.ScrollHorizontal = Math.Max(0, _dragStartScroll - (int)delta);
            AcceptEvent();
        }
    }

    private void MoveSelection(int direction)
    {
        if (IsCarouselLocked() || _options.Count == 0)
        {
            return;
        }

        SelectIndex(_currentIndex + direction, true);
    }

    private void SelectIndex(int index, bool animate)
    {
        if (_cards.Count == 0)
        {
            return;
        }

        _currentIndex = Math.Clamp(index, 0, _cards.Count - 1);
        UpdateCardVisuals();
        UpdateSelectionUi();
        if (animate)
        {
            AnimateSnapToCurrent();
        }
        else
        {
            Callable.From(SnapToCurrentImmediate).CallDeferred();
        }
    }

    private void AnimateSnapToCurrent()
    {
        if (_cards.Count == 0)
        {
            return;
        }

        _scrollTween?.Kill();
        var from = (float)_carouselScroll.ScrollHorizontal;
        var target = TargetScrollForIndex(_currentIndex);
        _scrollTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _scrollTween.TweenMethod(
            Callable.From<float>(value => _carouselScroll.ScrollHorizontal = (int)Math.Round(value)),
            from,
            target,
            0.22
        );
    }

    private void SnapToCurrentImmediate()
    {
        if (!IsNodeReady() || _cards.Count == 0)
        {
            return;
        }

        _carouselScroll.ScrollHorizontal = (int)Math.Round(TargetScrollForIndex(_currentIndex));
        UpdateCardVisuals();
    }

    private float TargetScrollForIndex(int index)
    {
        var card = _cards[Math.Clamp(index, 0, _cards.Count - 1)];
        var target = card.Position.X + card.Size.X * 0.5f - _carouselScroll.Size.X * 0.5f;
        return Math.Clamp(target, 0.0f, (float)_carouselScroll.GetHScrollBar().MaxValue);
    }

    private int NearestIndex()
    {
        if (_cards.Count == 0)
        {
            return 0;
        }

        var center = _carouselScroll.ScrollHorizontal + _carouselScroll.Size.X * 0.5f;
        var bestIndex = 0;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < _cards.Count; index++)
        {
            var cardCenter = _cards[index].Position.X + _cards[index].Size.X * 0.5f;
            var distance = Math.Abs(cardCenter - center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private void UpdateCarouselLayout()
    {
        if (!IsNodeReady())
        {
            return;
        }

        var cardHeight = Math.Clamp(_carouselScroll.Size.Y - 8.0f, 220.0f, 390.0f);
        var cardWidth = cardHeight * 0.67f;
        foreach (var card in _cards)
        {
            card.CustomMinimumSize = new Vector2(cardWidth, cardHeight);
        }

        var spacerWidth = Math.Max(18.0f, (_carouselScroll.Size.X - cardWidth) * 0.5f);
        _leftSpacer.CustomMinimumSize = new Vector2(spacerWidth, 1);
        _rightSpacer.CustomMinimumSize = new Vector2(spacerWidth, 1);
        Callable.From(SnapToCurrentImmediate).CallDeferred();
    }

    private void UpdateCardVisuals()
    {
        _visualTween?.Kill();
        _visualTween = CreateTween().SetParallel();
        for (var index = 0; index < _cards.Count; index++)
        {
            var distance = Math.Abs(index - _currentIndex);
            var scale = distance == 0 ? 1.0f : distance == 1 ? 0.84f : 0.74f;
            var alpha = distance == 0 ? 1.0f : distance == 1 ? 0.76f : 0.52f;
            var card = _cards[index];
            card.PivotOffset = card.Size * 0.5f;
            _visualTween.TweenProperty(card, "scale", Vector2.One * scale, 0.18);
            _visualTween.TweenProperty(card, "modulate", new Color(1, 1, 1, alpha), 0.18);
        }
    }

    private void UpdateNavigationButtons()
    {
        var locked = IsCarouselLocked() || _cards.Count == 0;
        _leftButton.Disabled = locked || _currentIndex <= 0;
        _rightButton.Disabled = locked || _currentIndex >= _cards.Count - 1;
    }

    private bool IsCarouselLocked()
    {
        var selectedId = _room == null ? null : CharacterSelectionModel.CurrentSelectedCharacterId(_room, CurrentUserId);
        return selectedId.HasValue || _randomLocked || _deadlineExpired || _operationInProgress || _pendingCharacterId.HasValue;
    }

    private float CurrentCardWidth()
    {
        return _cards.Count == 0 ? 240.0f : _cards[_currentIndex].Size.X;
    }

    private void Navigate(RoomPageTarget target, long? gameId)
    {
        if (!_navigationGate.TryBegin(target))
        {
            return;
        }

        _active = false;
        _pollTimer.Stop();
        _countdownTimer.Stop();
        switch (target)
        {
            case RoomPageTarget.Lobby:
                Session.ClearRoom();
                EmitSignal(SignalName.ReturnLobby);
                break;
            case RoomPageTarget.Room:
                EmitSignal(SignalName.ReturnRoom);
                break;
            case RoomPageTarget.Game when gameId.HasValue:
                Session.SetCurrentGame(gameId.Value);
                EmitSignal(SignalName.EnterGame);
                break;
        }
    }

    private void ShowNetworkStatus(string message, bool isError)
    {
        _networkStatusLabel.Text = message;
        _networkStatusLabel.AddThemeColorOverride(
            "font_color",
            isError ? new Color(0.72f, 0.16f, 0.11f) : new Color(0.18f, 0.38f, 0.48f)
        );
    }
}
