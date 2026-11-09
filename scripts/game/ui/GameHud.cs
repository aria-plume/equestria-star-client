using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public partial class GameHud : CanvasLayer
{
    private const float RightButtonScale = 1.75f;

    [Export(PropertyHint.Range, "0.5,1.75,0.025")]
    public float HudScale { get; set; } = 0.875f;

    private Control _hudRoot = null!;

    [Signal]
    public delegate void EndTurnRequestedEventHandler();

    [Signal]
    public delegate void QuestBoardRequestedEventHandler();

    [Signal]
    public delegate void ShopShelfRequestedEventHandler();

    [Signal]
    public delegate void PersonalQuestsRequestedEventHandler();

    [Signal]
    public delegate void InventoryRequestedEventHandler();

    [Signal]
    public delegate void EquipmentRequestedEventHandler();

    [Signal]
    public delegate void WorkRequestedEventHandler();

    [Signal]
    public delegate void RailwayRequestedEventHandler();

    [Signal]
    public delegate void QuestCheckRequestedEventHandler();

    [Signal]
    public delegate void QuestSubmissionRequestedEventHandler();

    [Export]
    public PackedScene? OtherPlayerEntryScene { get; set; }

    private Label _currentNicknameLabel = null!;
    private Label _currentAvatarNameLabel = null!;
    private Label _coinsValueLabel = null!;
    private Label _prestigeValueLabel = null!;
    private Label _emptyOtherPlayersLabel = null!;
    private ScrollContainer _otherPlayersScroll = null!;
    private VBoxContainer _otherPlayersList = null!;
    private PanelContainer _detailPanel = null!;
    private Label _detailNicknameLabel = null!;
    private Label _detailCoinsValueLabel = null!;
    private Label _detailPrestigeValueLabel = null!;
    private Control _turnNoticeAnchor = null!;
    private Label _turnNoticeLabel = null!;
    private Control _connectionNoticeAnchor = null!;
    private Label _connectionNoticeLabel = null!;
    private Control _endTurnAnchor = null!;
    private Button _endTurnButton = null!;
    private Button _questBoardButton = null!;
    private Button _shopShelfButton = null!;
    private Button _personalQuestsButton = null!;
    private Button _inventoryButton = null!;
    private Button _equipmentButton = null!;
    private Button _partnersButton = null!;
    private Control _actionMenuPanel = null!;
    private Button _actionMenuButton = null!;
    private Button _workButton = null!;
    private Button _railwayButton = null!;
    private Button _questCheckButton = null!;
    private Button _questSubmissionButton = null!;
    private readonly Dictionary<string, Label> _currentAttributeLabels = [];
    private readonly Dictionary<string, Label> _detailAttributeLabels = [];
    private PlayerViewResponseDto? _pendingPlayerView;
    private long _pendingCurrentUserId;
    private List<AvailableActionDto> _availableActions = [];
    private long _availableActionsStateVersion = -1;
    private bool _endTurnSubmitting;
    private bool _actionControlsLocked;
    private bool _connectionReady;
    private bool _workSubmitting;
    private bool _railwaySelecting;
    private int? _movementRulesVersion;
    private string? _workBlockReason;
    private readonly Dictionary<long, OtherPlayerHudEntry> _otherPlayerEntries = [];
    private long? _hoveredOtherPlayerKey;

    public long? BoundCurrentUserId { get; private set; }
    public int OtherPlayerCount { get; private set; }
    public string TurnNoticeText => GodotObject.IsInstanceValid(_turnNoticeLabel) ? _turnNoticeLabel.Text : "";
    public string ConnectionStatusText => GodotObject.IsInstanceValid(_connectionNoticeLabel) ? _connectionNoticeLabel.Text : "";
    public bool ConnectionStatusVisible => GodotObject.IsInstanceValid(_connectionNoticeAnchor) && _connectionNoticeAnchor.Visible;
    public bool EndTurnVisible => GodotObject.IsInstanceValid(_endTurnAnchor) && _endTurnAnchor.Visible;
    public bool EndTurnDisabled => !GodotObject.IsInstanceValid(_endTurnButton) || _endTurnButton.Disabled;
    public string EndTurnTooltip => GodotObject.IsInstanceValid(_endTurnButton) ? _endTurnButton.TooltipText : "";
    public bool IsEndTurnSubmitting => _endTurnSubmitting;
    public Button QuestBoardButton => _questBoardButton;
    public Button ShopShelfButton => _shopShelfButton;
    public Button PersonalQuestsButton => _personalQuestsButton;
    public Button InventoryButton => _inventoryButton;
    public Button EquipmentButton => _equipmentButton;
    public Button PartnersButton => _partnersButton;
    public Button EndTurnButton => _endTurnButton;
    public Button WorkButton => _workButton;
    public Button RailwayButton => _railwayButton;
    public Button QuestCheckButton => _questCheckButton;
    public Button QuestSubmissionButton => _questSubmissionButton;
    public Button ActionMenuButton => _actionMenuButton;
    public bool WorkDisabled => !GodotObject.IsInstanceValid(_workButton) || _workButton.Disabled;
    public bool IsActionMenuOpen => GodotObject.IsInstanceValid(_actionMenuPanel) && _actionMenuPanel.Visible;

    public override void _Ready()
    {
        _hudRoot = GetNode<Control>("HudRoot");
        _hudRoot.AnchorRight = 1.0f / HudScale;
        _hudRoot.AnchorBottom = 1.0f / HudScale;
        _hudRoot.Scale = Vector2.One * HudScale;
        KeepRightButtonScale(_hudRoot.GetNode<Control>("TopRightAnchor"));
        KeepRightButtonScale(_hudRoot.GetNode<Control>("ActionMenuAnchor"));
        KeepRightButtonScale(_hudRoot.GetNode<Control>("BottomActionsAnchor"));

        _currentNicknameLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/NameRibbon/NicknameLabel");
        _currentAvatarNameLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/AvatarPlaceholder/AvatarNameLabel");
        _coinsValueLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/StatusContent/CoinsBox/CoinsValue");
        _prestigeValueLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/StatusContent/PrestigeBox/PrestigeValue");
        _emptyOtherPlayersLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll/OtherPlayersList/EmptyLabel");
        _otherPlayersScroll = GetNode<ScrollContainer>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll");
        _otherPlayersList = GetNode<VBoxContainer>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/OtherPlayersScroll/OtherPlayersList");
        _detailPanel = GetNode<PanelContainer>("HudRoot/PlayerDetailPanel");
        _detailNicknameLabel = GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/NicknameLabel");
        _detailCoinsValueLabel = GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/ResourceRow/CoinsValue");
        _detailPrestigeValueLabel = GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/ResourceRow/PrestigeValue");
        _turnNoticeAnchor = GetNode<Control>("HudRoot/TurnNoticeAnchor");
        _turnNoticeLabel = GetNode<Label>("HudRoot/TurnNoticeAnchor/TurnNoticePanel/TurnNoticeLabel");
        _connectionNoticeAnchor = GetNode<Control>("HudRoot/ConnectionNoticeAnchor");
        _connectionNoticeLabel = GetNode<Label>("HudRoot/ConnectionNoticeAnchor/ConnectionNoticePanel/ConnectionNoticeLabel");
        _endTurnAnchor = GetNode<Control>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/EndTurnSlot");
        _endTurnButton = GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/EndTurnSlot/EndTurnButton");
        _questBoardButton = GetNode<Button>("HudRoot/TopRightAnchor/TopRight/PublicButtons/QuestBoardButton");
        _shopShelfButton = GetNode<Button>("HudRoot/TopRightAnchor/TopRight/PublicButtons/ShopShelfButton");
        _personalQuestsButton = GetNode<Button>("HudRoot/BottomActionsAnchor/Buttons/QuestsButton");
        _inventoryButton = GetNode<Button>("HudRoot/BottomActionsAnchor/Buttons/ItemsButton");
        _equipmentButton = GetNode<Button>("HudRoot/BottomActionsAnchor/Buttons/EquipmentButton");
        _partnersButton = GetNode<Button>("HudRoot/BottomActionsAnchor/Buttons/PartnersButton");
        _actionMenuPanel = GetNode<Control>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel");
        _actionMenuButton = GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/ActionMenuButton");
        _workButton = GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/WorkButton");
        _railwayButton = GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/RailwayButton");
        _questCheckButton = GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/QuestCheckButton");
        _questSubmissionButton = GetNode<Button>("HudRoot/ActionMenuAnchor/ActionMenu/MenuPanel/MenuMargin/Menu/QuestSubmissionButton");
        _endTurnButton.Pressed += OnEndTurnPressed;
        _questBoardButton.Pressed += OnQuestBoardPressed;
        _shopShelfButton.Pressed += OnShopShelfPressed;
        _personalQuestsButton.Pressed += OnPersonalQuestsPressed;
        _inventoryButton.Pressed += OnInventoryPressed;
        _equipmentButton.Pressed += OnEquipmentPressed;
        _actionMenuButton.Pressed += OnActionMenuPressed;
        _workButton.Pressed += OnWorkPressed;
        _railwayButton.Pressed += OnRailwayPressed;
        _questCheckButton.Pressed += OnQuestCheckPressed;
        _questSubmissionButton.Pressed += OnQuestSubmissionPressed;

        BindAttributeLabels(_currentAttributeLabels, "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/StatusContent/AttributesGrid");
        BindAttributeLabels(_detailAttributeLabels, "HudRoot/PlayerDetailPanel/ContentMargin/Content/AttributesGrid");

        if (_pendingPlayerView != null)
        {
            ApplyPlayerView(_pendingPlayerView, _pendingCurrentUserId);
        }
        else
        {
            ClearPlayerView();
        }
    }

    private void KeepRightButtonScale(Control group)
    {
        // 保持按钮组与屏幕锚点的距离，内部布局仍按原尺寸计算。
        var ratio = RightButtonScale / HudScale;
        var width = group.OffsetRight - group.OffsetLeft;
        var height = group.OffsetBottom - group.OffsetTop;
        var left = group.OffsetLeft * ratio;
        var top = group.OffsetTop * ratio;
        group.OffsetLeft = left;
        group.OffsetTop = top;
        group.OffsetRight = left + width;
        group.OffsetBottom = top + height;
        group.Scale = Vector2.One * ratio;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_endTurnButton))
        {
            _endTurnButton.Pressed -= OnEndTurnPressed;
        }

        if (GodotObject.IsInstanceValid(_questBoardButton))
        {
            _questBoardButton.Pressed -= OnQuestBoardPressed;
        }

        if (GodotObject.IsInstanceValid(_shopShelfButton))
        {
            _shopShelfButton.Pressed -= OnShopShelfPressed;
        }

        if (GodotObject.IsInstanceValid(_personalQuestsButton))
        {
            _personalQuestsButton.Pressed -= OnPersonalQuestsPressed;
        }

        if (GodotObject.IsInstanceValid(_inventoryButton)) _inventoryButton.Pressed -= OnInventoryPressed;
        if (GodotObject.IsInstanceValid(_equipmentButton)) _equipmentButton.Pressed -= OnEquipmentPressed;

        if (GodotObject.IsInstanceValid(_actionMenuButton)) _actionMenuButton.Pressed -= OnActionMenuPressed;
        if (GodotObject.IsInstanceValid(_workButton)) _workButton.Pressed -= OnWorkPressed;
        if (GodotObject.IsInstanceValid(_railwayButton)) _railwayButton.Pressed -= OnRailwayPressed;
        if (GodotObject.IsInstanceValid(_questCheckButton)) _questCheckButton.Pressed -= OnQuestCheckPressed;
        if (GodotObject.IsInstanceValid(_questSubmissionButton)) _questSubmissionButton.Pressed -= OnQuestSubmissionPressed;
    }

    public void BindPlayerView(PlayerViewResponseDto playerView, long currentUserId)
    {
        RefreshPlayerView(playerView, currentUserId);
    }

    public void RefreshPlayerView(PlayerViewResponseDto playerView, long currentUserId)
    {
        ArgumentNullException.ThrowIfNull(playerView);
        _pendingPlayerView = playerView;
        _pendingCurrentUserId = currentUserId;

        if (IsNodeReady())
        {
            ApplyPlayerView(playerView, currentUserId);
        }
    }

    public void ClearPlayerView()
    {
        _pendingPlayerView = null;
        _pendingCurrentUserId = 0;
        BoundCurrentUserId = null;
        OtherPlayerCount = 0;
        _availableActions = [];
        _availableActionsStateVersion = -1;
        _endTurnSubmitting = false;
        _actionControlsLocked = false;
        _connectionReady = false;
        _workSubmitting = false;
        _workBlockReason = null;

        if (!IsNodeReady())
        {
            return;
        }

        HidePlayerDetails();
        ClearOtherPlayerEntries();
        _emptyOtherPlayersLabel.Text = "暂无其他玩家";
        _emptyOtherPlayersLabel.Visible = true;
        _currentNicknameLabel.Text = "尚未绑定玩家";
        _currentAvatarNameLabel.Text = "玩家";
        _coinsValueLabel.Text = "-";
        _prestigeValueLabel.Text = "-";
        SetAttributes(_currentAttributeLabels, null);
        _turnNoticeAnchor.Visible = false;
        _turnNoticeLabel.Text = "正在同步回合信息……";
        _connectionNoticeAnchor.Visible = false;
        _connectionNoticeLabel.Text = "";
        _endTurnAnchor.Visible = false;
        _endTurnButton.Disabled = true;
        _endTurnButton.Text = "结束回合";
        _endTurnButton.TooltipText = "";
        _actionMenuPanel.Visible = false;
        RefreshWorkPresentation();
    }

    public void SetAvailableActions(IEnumerable<AvailableActionDto>? actions, long stateVersion)
    {
        _availableActions = actions?.ToList() ?? [];
        _availableActionsStateVersion = stateVersion;
        if (IsNodeReady())
        {
            RefreshTurnPresentation();
        }
    }

    public void ClearAvailableActions()
    {
        _availableActions = [];
        _availableActionsStateVersion = -1;
        if (IsNodeReady())
        {
            RefreshTurnPresentation();
        }
    }

    public void SetEndTurnSubmitting(bool submitting)
    {
        _endTurnSubmitting = submitting;
        if (IsNodeReady())
        {
            RefreshTurnPresentation();
        }
    }

    public void SetActionControlsLocked(bool locked)
    {
        _actionControlsLocked = locked;
        if (IsNodeReady())
        {
            RefreshTurnPresentation();
        }
    }

    public void SetConnectionReady(bool ready)
    {
        _connectionReady = ready;
        if (IsNodeReady())
        {
            RefreshTurnPresentation();
        }
    }

    public void SetWorkSubmitting(bool submitting)
    {
        _workSubmitting = submitting;
        if (IsNodeReady())
        {
            RefreshWorkPresentation();
        }
    }

    public void SetWorkBlocked(string? reason)
    {
        _workBlockReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (IsNodeReady())
        {
            RefreshWorkPresentation();
        }
    }

    public void SetRailwaySelecting(bool selecting)
    {
        _railwaySelecting = selecting;
        if (IsNodeReady()) RefreshWorkPresentation();
    }

    public void SetMovementRulesVersion(int? version)
    {
        _movementRulesVersion = version;
        if (IsNodeReady()) RefreshWorkPresentation();
    }

    public void CloseActionMenu()
    {
        if (!IsNodeReady())
        {
            return;
        }

        _actionMenuPanel.Visible = false;
        _actionMenuButton.TooltipText = "展开行为菜单";
    }

    public void SetConnectionStatus(string? message, bool isError = false)
    {
        if (!IsNodeReady())
        {
            return;
        }

        _connectionNoticeAnchor.Visible = !string.IsNullOrWhiteSpace(message);
        _connectionNoticeLabel.Text = message?.Trim() ?? "";
        _connectionNoticeLabel.Modulate = isError
            ? new Color("b54d4d")
            : new Color("1e404c");
    }

    private void ApplyPlayerView(PlayerViewResponseDto playerView, long currentUserId)
    {
        if (BoundCurrentUserId.HasValue && BoundCurrentUserId.Value != currentUserId)
        {
            HidePlayerDetails();
            ClearOtherPlayerEntries();
        }

        BoundCurrentUserId = currentUserId;

        var players = playerView.Players ?? [];
        var currentPlayer = players.FirstOrDefault(player => player.UserId == currentUserId);
        if (currentPlayer == null)
        {
            _currentNicknameLabel.Text = "未找到当前玩家";
            _currentAvatarNameLabel.Text = "未找到玩家";
            _coinsValueLabel.Text = "-";
            _prestigeValueLabel.Text = "-";
            SetAttributes(_currentAttributeLabels, null);
        }
        else
        {
            var currentDisplayName = DisplayName(currentPlayer);
            _currentNicknameLabel.Text = currentDisplayName;
            _currentAvatarNameLabel.Text = currentDisplayName;
            _coinsValueLabel.Text = currentPlayer.Coins.ToString();
            _prestigeValueLabel.Text = currentPlayer.Prestige.ToString();
            SetAttributes(_currentAttributeLabels, currentPlayer.EffectiveAttributes);
        }

        var otherPlayers = players
            .Where(player => player.UserId != currentUserId)
            .OrderBy(player => player.TurnOrder)
            .ThenBy(player => player.UserId)
            .ToList();

        OtherPlayerCount = otherPlayers.Count;
        _emptyOtherPlayersLabel.Visible = otherPlayers.Count == 0;
        _emptyOtherPlayersLabel.Text = otherPlayers.Count == 0 ? "暂无其他玩家" : "";
        SynchronizeOtherPlayerEntries(otherPlayers);
        ResizeOtherPlayersScroll(otherPlayers.Count);

        RefreshTurnPresentation();
    }

    private void RefreshTurnPresentation()
    {
        RefreshWorkPresentation();
        RefreshTurnNoticePresentation();
        RefreshEndTurnPresentation();
    }

    private void RefreshTurnNoticePresentation()
    {
        var playerView = _pendingPlayerView;
        if (playerView == null || _pendingCurrentUserId <= 0)
        {
            _turnNoticeAnchor.Visible = false;
            return;
        }

        _turnNoticeAnchor.Visible = true;
        if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            _turnNoticeLabel.Text = string.IsNullOrWhiteSpace(playerView.GameStatusText)
                ? "对局当前未进行。"
                : playerView.GameStatusText!.Trim();
            return;
        }

        if (string.Equals(playerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            _turnNoticeLabel.Text = "全员离线暂停，正在等待服务端恢复……";
            return;
        }

        if (playerView.CurrentTurnUserId == _pendingCurrentUserId)
        {
            _turnNoticeLabel.Text = "当前是你的回合";
        }
        else
        {
            var currentTurnPlayer = playerView.Players?
                .FirstOrDefault(player => player.UserId == playerView.CurrentTurnUserId);
            _turnNoticeLabel.Text = currentTurnPlayer == null
                ? "正在同步回合信息……"
                : $"当前是 {DisplayName(currentTurnPlayer)} 的回合";
        }
    }

    private void RefreshEndTurnPresentation()
    {
        var playerView = _pendingPlayerView;
        _endTurnButton.Text = "结束回合";
        _endTurnButton.Disabled = true;
        _endTurnButton.TooltipText = "";

        if (playerView == null
            || _pendingCurrentUserId <= 0
            || !string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(playerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            _endTurnAnchor.Visible = false;
            return;
        }

        _endTurnAnchor.Visible = playerView.CurrentTurnUserId == _pendingCurrentUserId;
        if (!_endTurnAnchor.Visible)
        {
            return;
        }

        if (_endTurnSubmitting)
        {
            _endTurnButton.Text = "正在结束……";
            _endTurnButton.Disabled = true;
            _endTurnButton.TooltipText = "正在提交结束回合请求。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(playerView.EngineState?.Phase)
            && !string.Equals(playerView.EngineState.Phase, "MIDDLE", StringComparison.OrdinalIgnoreCase))
        {
            _endTurnButton.TooltipText = "当前服务端阶段不能结束回合。";
            return;
        }

        if (playerView.EngineState?.PendingReward != null)
        {
            _endTurnButton.TooltipText = "请先处理服务端奖励队首。";
            return;
        }

        if (!_connectionReady)
        {
            _endTurnButton.Disabled = true;
            _endTurnButton.TooltipText = "正在建立对局连接……";
            return;
        }

        if (_actionControlsLocked)
        {
            _endTurnButton.Disabled = true;
            _endTurnButton.TooltipText = "正在处理其他行动。";
            return;
        }

        var endTurnAction = _availableActionsStateVersion == playerView.StateVersion
            ? _availableActions.FirstOrDefault(action => string.Equals(action.ActionType, "END_TURN", StringComparison.OrdinalIgnoreCase))
            : null;
        if (endTurnAction == null)
        {
            _endTurnButton.Disabled = true;
            _endTurnButton.TooltipText = "正在同步可用行动……";
            return;
        }

        _endTurnButton.Disabled = !endTurnAction.Available;
        _endTurnButton.TooltipText = endTurnAction.Available
            ? ""
            : FirstNonEmpty(endTurnAction.ReasonText, endTurnAction.Reason, "当前无法结束回合。");
    }

    private void RefreshWorkPresentation()
    {
        if (!IsNodeReady())
        {
            return;
        }

        _actionMenuButton.TooltipText = _actionMenuPanel.Visible ? "收起行为菜单" : "展开行为菜单";
        // 判定列表即使全部任务不可执行也允许打开，逐任务原因由列表展示。
        _questCheckButton.Disabled = false;
        _questCheckButton.TooltipText = "查看当前已接任务中的主动判定。";
        _questSubmissionButton.Disabled = false;
        _questSubmissionButton.TooltipText = "查看当前已接任务中的道具交付与资源支付。";
        _workButton.Text = _workSubmitting ? "正在准备……" : "打工";
        _railwayButton.Text = _railwaySelecting ? "铁路（选择中）" : "铁路";
        RefreshRailwayPresentation();

        if (!string.IsNullOrWhiteSpace(_workBlockReason))
        {
            SetWorkUnavailable(_workBlockReason);
            return;
        }

        var playerView = _pendingPlayerView;
        if (playerView == null || _pendingCurrentUserId <= 0)
        {
            SetWorkUnavailable("正在同步玩家状态……");
            return;
        }

        if (!_connectionReady)
        {
            SetWorkUnavailable("正在建立对局连接……");
            return;
        }

        if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            SetWorkUnavailable("当前对局阶段不能打工。");
            return;
        }

        if (string.Equals(playerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            SetWorkUnavailable("对局处于全员离线暂停状态。");
            return;
        }

        if (!string.IsNullOrWhiteSpace(playerView.EngineState?.Phase)
            && !string.Equals(playerView.EngineState.Phase, "MIDDLE", StringComparison.OrdinalIgnoreCase))
        {
            SetWorkUnavailable("当前服务端阶段不能打工。");
            return;
        }

        if (playerView.EngineState?.PendingReward != null)
        {
            SetWorkUnavailable("请先处理服务端奖励队首。");
            return;
        }

        if (playerView.CurrentTurnUserId != _pendingCurrentUserId)
        {
            SetWorkUnavailable("当前不是你的回合。");
            return;
        }

        var currentPlayer = playerView.Players?.FirstOrDefault(player => player.UserId == _pendingCurrentUserId);
        if (playerView.EngineState?.CurrentPlayerId is > 0
            && currentPlayer != null
            && playerView.EngineState.CurrentPlayerId != currentPlayer.PlayerId)
        {
            SetWorkUnavailable("当前局内行动玩家尚未同步为自己。");
            return;
        }

        if (playerView.EngineState?.PendingCheck != null)
        {
            SetWorkUnavailable("当前已有待完成判定。");
            return;
        }

        if (_workSubmitting)
        {
            SetWorkUnavailable("正在准备打工判定……");
            return;
        }

        if (_actionControlsLocked)
        {
            SetWorkUnavailable("正在处理其他业务行动……");
            return;
        }

        if (_availableActionsStateVersion != playerView.StateVersion)
        {
            SetWorkUnavailable("正在同步打工资格……");
            return;
        }

        var work = _availableActions.FirstOrDefault(action =>
            string.Equals(action.ActionType, "WORK", StringComparison.OrdinalIgnoreCase));
        if (work == null)
        {
            SetWorkUnavailable("服务端未返回打工资格。");
            return;
        }

        _workButton.Disabled = !work.Available;
        _workButton.TooltipText = work.Available
            ? "查询当前位置工作并准备服务端判定。"
            : FirstNonEmpty(work.ReasonText, work.Reason, "当前不能打工。");
    }

    private void RefreshRailwayPresentation()
    {
        var playerView = _pendingPlayerView;
        if (playerView == null || _pendingCurrentUserId <= 0)
        {
            SetRailwayUnavailable("正在同步玩家状态……");
            return;
        }
        if (_movementRulesVersion != 2)
        {
            SetRailwayUnavailable("服务端尚未启用铁路与森林移动 v2。");
            return;
        }
        if (!_connectionReady)
        {
            SetRailwayUnavailable("正在建立对局连接……");
            return;
        }
        if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(playerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(playerView.EngineState?.Phase)
                && !string.Equals(playerView.EngineState.Phase, "MIDDLE", StringComparison.OrdinalIgnoreCase)))
        {
            SetRailwayUnavailable("当前对局阶段不能乘坐铁路。");
            return;
        }
        if (playerView.CurrentTurnUserId != _pendingCurrentUserId)
        {
            SetRailwayUnavailable("当前不是你的回合。");
            return;
        }
        if (playerView.EngineState?.PendingReward != null || playerView.EngineState?.PendingCheck != null)
        {
            SetRailwayUnavailable("请先处理当前服务端待办。");
            return;
        }
        if (_railwaySelecting)
        {
            SetRailwayUnavailable("正在选择相邻车站。");
            return;
        }
        if (_actionControlsLocked)
        {
            SetRailwayUnavailable("正在处理其他业务行动……");
            return;
        }
        if (_availableActionsStateVersion != playerView.StateVersion)
        {
            SetRailwayUnavailable("正在同步铁路资格……");
            return;
        }

        var railway = _availableActions.FirstOrDefault(action =>
            string.Equals(action.ActionType, "USE_RAILWAY", StringComparison.OrdinalIgnoreCase));
        if (railway == null)
        {
            SetRailwayUnavailable("服务端未返回铁路资格。");
            return;
        }

        _railwayButton.Disabled = !railway.Available;
        _railwayButton.TooltipText = railway.Available
            ? "查询服务端返回的直接相邻可达车站。"
            : FirstNonEmpty(railway.ReasonText, railway.Reason, "当前不能乘坐铁路。");
    }

    private void SetRailwayUnavailable(string reason)
    {
        _railwayButton.Disabled = true;
        _railwayButton.TooltipText = reason;
    }

    private void SetWorkUnavailable(string reason)
    {
        _workButton.Disabled = true;
        _workButton.TooltipText = reason;
    }

    private void SynchronizeOtherPlayerEntries(IReadOnlyList<PlayerViewPlayerDto> players)
    {
        var retainedKeys = new HashSet<long>();
        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            var key = OtherPlayerKey(player);
            retainedKeys.Add(key);

            if (!_otherPlayerEntries.TryGetValue(key, out var entry)
                || !GodotObject.IsInstanceValid(entry))
            {
                entry = AddOtherPlayerEntry(player);
                if (entry == null)
                {
                    continue;
                }

                _otherPlayerEntries[key] = entry;
            }
            else
            {
                entry.BindPlayer(player);
            }

            _otherPlayersList.MoveChild(entry, index + 1);
            if (_hoveredOtherPlayerKey == key)
            {
                ShowPlayerDetails(player, entry);
            }
        }

        foreach (var staleKey in _otherPlayerEntries.Keys.Where(key => !retainedKeys.Contains(key)).ToList())
        {
            var staleEntry = _otherPlayerEntries[staleKey];
            staleEntry.HoverChanged -= OnOtherPlayerHoverChanged;
            _otherPlayerEntries.Remove(staleKey);
            if (_hoveredOtherPlayerKey == staleKey)
            {
                HidePlayerDetails();
            }

            _otherPlayersList.RemoveChild(staleEntry);
            staleEntry.QueueFree();
        }
    }

    private OtherPlayerHudEntry? AddOtherPlayerEntry(PlayerViewPlayerDto player)
    {
        if (OtherPlayerEntryScene?.Instantiate<OtherPlayerHudEntry>() is not { } entry)
        {
            GD.PushError("GameHud 缺少可用的 OtherPlayerEntryScene。");
            return null;
        }

        entry.BindPlayer(player);
        entry.HoverChanged += OnOtherPlayerHoverChanged;
        _otherPlayersList.AddChild(entry);
        return entry;
    }

    private void ClearOtherPlayerEntries()
    {
        foreach (var child in _otherPlayersList.GetChildren())
        {
            if (child == _emptyOtherPlayersLabel)
            {
                continue;
            }

            if (child is OtherPlayerHudEntry entry)
            {
                entry.HoverChanged -= OnOtherPlayerHoverChanged;
            }

            _otherPlayersList.RemoveChild(child);
            child.QueueFree();
        }

        _otherPlayerEntries.Clear();
        _hoveredOtherPlayerKey = null;
    }

    private void OnOtherPlayerHoverChanged(OtherPlayerHudEntry entry, bool hovered)
    {
        if (entry.Player == null)
        {
            return;
        }

        var key = OtherPlayerKey(entry.Player);
        if (!hovered)
        {
            if (_hoveredOtherPlayerKey == key)
            {
                HidePlayerDetails();
            }
            return;
        }

        _hoveredOtherPlayerKey = key;
        ShowPlayerDetails(entry.Player, entry);
    }

    private void ResizeOtherPlayersScroll(int playerCount)
    {
        const float entryHeight = 77.0f;
        const float separation = 5.0f;
        const float emptyHeight = 66.0f;
        const float maximumHeight = 255.0f;
        var contentHeight = playerCount <= 0
            ? emptyHeight
            : playerCount * entryHeight + Math.Max(0, playerCount - 1) * separation;
        _otherPlayersScroll.Size = new Vector2(
            _otherPlayersScroll.Size.X,
            Math.Min(maximumHeight, contentHeight)
        );
    }

    private static long OtherPlayerKey(PlayerViewPlayerDto player)
    {
        return player.PlayerId > 0 ? player.PlayerId : -Math.Max(1, player.UserId);
    }

    private void ShowPlayerDetails(PlayerViewPlayerDto player, Control anchor)
    {
        _detailNicknameLabel.Text = DisplayName(player);
        _detailCoinsValueLabel.Text = player.Coins.ToString();
        _detailPrestigeValueLabel.Text = player.Prestige.ToString();
        SetAttributes(_detailAttributeLabels, player.EffectiveAttributes);
        _detailPanel.Visible = true;

        var viewportSize = _hudRoot.Size;
        var anchorRect = anchor.GetGlobalRect();
        var toHud = _hudRoot.GetGlobalTransform().AffineInverse();
        var anchorPosition = toHud * anchorRect.Position;
        var anchorEnd = toHud * anchorRect.End;
        var panelSize = _detailPanel.Size.Max(_detailPanel.CustomMinimumSize);
        const float screenMargin = 16.0f;
        const float panelGap = 12.0f;

        var x = anchorEnd.X + panelGap;
        if (x + panelSize.X > viewportSize.X - screenMargin)
        {
            x = anchorPosition.X - panelSize.X - panelGap;
        }

        var y = Mathf.Clamp(
            anchorPosition.Y,
            screenMargin,
            Mathf.Max(screenMargin, viewportSize.Y - panelSize.Y - screenMargin)
        );
        _detailPanel.Position = new Vector2(Mathf.Max(screenMargin, x), y);
    }

    private void HidePlayerDetails()
    {
        _detailPanel.Visible = false;
        _hoveredOtherPlayerKey = null;
    }

    private void OnEndTurnPressed()
    {
        if (!_endTurnAnchor.Visible || _endTurnButton.Disabled || _endTurnSubmitting)
        {
            return;
        }

        EmitSignal(SignalName.EndTurnRequested);
    }

    private void OnQuestBoardPressed()
    {
        EmitSignal(SignalName.QuestBoardRequested);
    }

    private void OnShopShelfPressed()
    {
        EmitSignal(SignalName.ShopShelfRequested);
    }

    private void OnPersonalQuestsPressed()
    {
        EmitSignal(SignalName.PersonalQuestsRequested);
    }

    private void OnInventoryPressed()
    {
        EmitSignal(SignalName.InventoryRequested);
    }

    private void OnEquipmentPressed()
    {
        EmitSignal(SignalName.EquipmentRequested);
    }

    private void OnActionMenuPressed()
    {
        _actionMenuPanel.Visible = !_actionMenuPanel.Visible;
        _actionMenuButton.TooltipText = _actionMenuPanel.Visible ? "收起行为菜单" : "展开行为菜单";
    }

    private void OnWorkPressed()
    {
        if (_workButton.Disabled || _workSubmitting)
        {
            return;
        }

        CloseActionMenu();
        EmitSignal(SignalName.WorkRequested);
    }

    private void OnRailwayPressed()
    {
        if (_railwayButton.Disabled) return;
        CloseActionMenu();
        EmitSignal(SignalName.RailwayRequested);
    }

    private void OnQuestCheckPressed()
    {
        CloseActionMenu();
        EmitSignal(SignalName.QuestCheckRequested);
    }

    private void OnQuestSubmissionPressed()
    {
        CloseActionMenu();
        EmitSignal(SignalName.QuestSubmissionRequested);
    }

    private void BindAttributeLabels(Dictionary<string, Label> target, string gridPath)
    {
        target["strength"] = GetNode<Label>($"{gridPath}/StrengthValue");
        target["speed"] = GetNode<Label>($"{gridPath}/SpeedValue");
        target["social"] = GetNode<Label>($"{gridPath}/SocialValue");
        target["charm"] = GetNode<Label>($"{gridPath}/CharmValue");
        target["magic"] = GetNode<Label>($"{gridPath}/MagicValue");
        target["mind"] = GetNode<Label>($"{gridPath}/MindValue");
    }

    private static void SetAttributes(Dictionary<string, Label> labels, EffectiveAttributesDto? attributes)
    {
        labels["strength"].Text = attributes?.Strength.ToString() ?? "-";
        labels["speed"].Text = attributes?.Speed.ToString() ?? "-";
        labels["social"].Text = attributes?.Social.ToString() ?? "-";
        labels["charm"].Text = attributes?.Charm.ToString() ?? "-";
        labels["magic"].Text = attributes?.Magic.ToString() ?? "-";
        labels["mind"].Text = attributes?.Mind.ToString() ?? "-";
    }

    private static string DisplayName(PlayerViewPlayerDto player)
    {
        return string.IsNullOrWhiteSpace(player.Nickname) ? $"玩家 {player.UserId}" : player.Nickname.Trim();
    }

    private static string FirstNonEmpty(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return string.IsNullOrWhiteSpace(secondary) ? fallback : secondary.Trim();
    }
}
