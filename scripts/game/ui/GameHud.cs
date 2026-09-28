using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public partial class GameHud : CanvasLayer
{
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
    private readonly Dictionary<string, Label> _currentAttributeLabels = [];
    private readonly Dictionary<string, Label> _detailAttributeLabels = [];
    private PlayerViewResponseDto? _pendingPlayerView;
    private long _pendingCurrentUserId;

    public long? BoundCurrentUserId { get; private set; }
    public int OtherPlayerCount { get; private set; }

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
}
