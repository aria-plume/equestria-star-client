using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public partial class GameHud : CanvasLayer
{
    [Signal]
    public delegate void EndTurnRequestedEventHandler();

    [Export]
    public PackedScene? OtherPlayerEntryScene { get; set; }

    private Label _currentNicknameLabel = null!;
    private Label _coinsValueLabel = null!;
    private Label _prestigeValueLabel = null!;
    private Label _emptyOtherPlayersLabel = null!;
    private VBoxContainer _otherPlayersList = null!;
    private PanelContainer _detailPanel = null!;
    private Label _detailNicknameLabel = null!;
    private Label _detailCoinsValueLabel = null!;
    private Label _detailPrestigeValueLabel = null!;
    private Control _turnNoticeAnchor = null!;
    private Label _turnNoticeLabel = null!;
    private Control _endTurnAnchor = null!;
    private Button _endTurnButton = null!;
    private readonly Dictionary<string, Label> _currentAttributeLabels = [];
    private readonly Dictionary<string, Label> _detailAttributeLabels = [];
    private PlayerViewResponseDto? _pendingPlayerView;
    private long _pendingCurrentUserId;
    private List<AvailableActionDto> _availableActions = [];
    private long _availableActionsStateVersion = -1;
    private bool _endTurnSubmitting;
    private bool _actionControlsLocked;

    public long? BoundCurrentUserId { get; private set; }
    public int OtherPlayerCount { get; private set; }
    public string TurnNoticeText => GodotObject.IsInstanceValid(_turnNoticeLabel) ? _turnNoticeLabel.Text : "";
    public bool EndTurnVisible => GodotObject.IsInstanceValid(_endTurnAnchor) && _endTurnAnchor.Visible;
    public bool EndTurnDisabled => !GodotObject.IsInstanceValid(_endTurnButton) || _endTurnButton.Disabled;
    public string EndTurnTooltip => GodotObject.IsInstanceValid(_endTurnButton) ? _endTurnButton.TooltipText : "";
    public bool IsEndTurnSubmitting => _endTurnSubmitting;

    public override void _Ready()
    {
        _currentNicknameLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/NicknameLabel");
        _coinsValueLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/ResourceRow/CoinsValue");
        _prestigeValueLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/ResourceRow/PrestigeValue");
        _emptyOtherPlayersLabel = GetNode<Label>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/ContentMargin/Content/OtherPlayersScroll/OtherPlayersList/EmptyLabel");
        _otherPlayersList = GetNode<VBoxContainer>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/ContentMargin/Content/OtherPlayersScroll/OtherPlayersList");
        _detailPanel = GetNode<PanelContainer>("HudRoot/PlayerDetailPanel");
        _detailNicknameLabel = GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/NicknameLabel");
        _detailCoinsValueLabel = GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/ResourceRow/CoinsValue");
        _detailPrestigeValueLabel = GetNode<Label>("HudRoot/PlayerDetailPanel/ContentMargin/Content/ResourceRow/PrestigeValue");
        _turnNoticeAnchor = GetNode<Control>("HudRoot/TurnNoticeAnchor");
        _turnNoticeLabel = GetNode<Label>("HudRoot/TurnNoticeAnchor/TurnNoticePanel/TurnNoticeMargin/TurnNoticeLabel");
        _endTurnAnchor = GetNode<Control>("HudRoot/EndTurnAnchor");
        _endTurnButton = GetNode<Button>("HudRoot/EndTurnAnchor/EndTurnPanel/EndTurnMargin/EndTurnButton");
        _endTurnButton.Pressed += OnEndTurnPressed;

        BindAttributeLabels(_currentAttributeLabels, "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/AttributesGrid");
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

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_endTurnButton))
        {
            _endTurnButton.Pressed -= OnEndTurnPressed;
        }
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

        if (!IsNodeReady())
        {
            return;
        }

        HidePlayerDetails();
        ClearOtherPlayerEntries();
        _emptyOtherPlayersLabel.Text = "暂无其他玩家";
        _emptyOtherPlayersLabel.Visible = true;
        _currentNicknameLabel.Text = "尚未绑定玩家";
        _coinsValueLabel.Text = "-";
        _prestigeValueLabel.Text = "-";
        SetAttributes(_currentAttributeLabels, null);
        _turnNoticeAnchor.Visible = false;
        _turnNoticeLabel.Text = "正在同步回合信息……";
        _endTurnAnchor.Visible = false;
        _endTurnButton.Disabled = true;
        _endTurnButton.Text = "结束回合";
        _endTurnButton.TooltipText = "";
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

    private void ApplyPlayerView(PlayerViewResponseDto playerView, long currentUserId)
    {
        HidePlayerDetails();
        ClearOtherPlayerEntries();
        BoundCurrentUserId = currentUserId;

        var players = playerView.Players ?? [];
        var currentPlayer = players.FirstOrDefault(player => player.UserId == currentUserId);
        if (currentPlayer == null)
        {
            _currentNicknameLabel.Text = "未找到当前玩家";
            _coinsValueLabel.Text = "-";
            _prestigeValueLabel.Text = "-";
            SetAttributes(_currentAttributeLabels, null);
        }
        else
        {
            _currentNicknameLabel.Text = DisplayName(currentPlayer);
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

        foreach (var player in otherPlayers)
        {
            AddOtherPlayerEntry(player);
        }

        RefreshTurnPresentation();
    }

    private void RefreshTurnPresentation()
    {
        var playerView = _pendingPlayerView;
        if (playerView == null || _pendingCurrentUserId <= 0)
        {
            _turnNoticeAnchor.Visible = false;
            _endTurnAnchor.Visible = false;
            return;
        }

        _turnNoticeAnchor.Visible = true;
        if (!string.Equals(playerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            _turnNoticeLabel.Text = string.IsNullOrWhiteSpace(playerView.GameStatusText)
                ? "对局当前未进行。"
                : playerView.GameStatusText!.Trim();
            _endTurnAnchor.Visible = false;
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

        _endTurnAnchor.Visible = playerView.CurrentTurnUserId == _pendingCurrentUserId;
        if (!_endTurnAnchor.Visible)
        {
            _endTurnButton.Text = "结束回合";
            _endTurnButton.Disabled = true;
            _endTurnButton.TooltipText = "";
            return;
        }

        if (_endTurnSubmitting)
        {
            _endTurnButton.Text = "正在结束……";
            _endTurnButton.Disabled = true;
            _endTurnButton.TooltipText = "正在提交结束回合请求。";
            return;
        }

        _endTurnButton.Text = "结束回合";
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

    private void AddOtherPlayerEntry(PlayerViewPlayerDto player)
    {
        if (OtherPlayerEntryScene?.Instantiate<OtherPlayerHudEntry>() is not { } entry)
        {
            GD.PushError("GameHud 缺少可用的 OtherPlayerEntryScene。");
            return;
        }

        entry.BindPlayer(player);
        entry.HoverChanged += OnOtherPlayerHoverChanged;
        _otherPlayersList.AddChild(entry);
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
    }

    private void OnOtherPlayerHoverChanged(OtherPlayerHudEntry entry, bool hovered)
    {
        if (!hovered || entry.Player == null)
        {
            HidePlayerDetails();
            return;
        }

        ShowPlayerDetails(entry.Player, entry);
    }

    private void ShowPlayerDetails(PlayerViewPlayerDto player, Control anchor)
    {
        _detailNicknameLabel.Text = DisplayName(player);
        _detailCoinsValueLabel.Text = player.Coins.ToString();
        _detailPrestigeValueLabel.Text = player.Prestige.ToString();
        SetAttributes(_detailAttributeLabels, player.EffectiveAttributes);
        _detailPanel.Visible = true;

        var viewportSize = GetViewport().GetVisibleRect().Size;
        var anchorRect = anchor.GetGlobalRect();
        var panelSize = _detailPanel.Size.Max(_detailPanel.CustomMinimumSize);
        const float screenMargin = 16.0f;
        const float panelGap = 12.0f;

        var x = anchorRect.End.X + panelGap;
        if (x + panelSize.X > viewportSize.X - screenMargin)
        {
            x = anchorRect.Position.X - panelSize.X - panelGap;
        }

        var y = Mathf.Clamp(
            anchorRect.Position.Y,
            screenMargin,
            Mathf.Max(screenMargin, viewportSize.Y - panelSize.Y - screenMargin)
        );
        _detailPanel.Position = new Vector2(Mathf.Max(screenMargin, x), y);
    }

    private void HidePlayerDetails()
    {
        _detailPanel.Visible = false;
    }

    private void OnEndTurnPressed()
    {
        if (!_endTurnAnchor.Visible || _endTurnButton.Disabled || _endTurnSubmitting)
        {
            return;
        }

        EmitSignal(SignalName.EndTurnRequested);
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
