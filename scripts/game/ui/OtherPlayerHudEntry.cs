using System;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public partial class OtherPlayerHudEntry : PanelContainer
{
    [Export]
    public StyleBox? NormalStyle { get; set; }

    [Export]
    public StyleBox? HoverStyle { get; set; }

    private Label _turnOrderLabel = null!;
    private Label _nicknameLabel = null!;

    public event Action<OtherPlayerHudEntry, bool>? HoverChanged;

    public PlayerViewPlayerDto? Player { get; private set; }

    public override void _Ready()
    {
        _turnOrderLabel = GetNode<Label>("Margin/Row/TurnOrderLabel");
        _nicknameLabel = GetNode<Label>("Margin/Row/NicknameLabel");
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;

        if (Player != null)
        {
            ApplyPlayer();
        }
    }

    public override void _ExitTree()
    {
        MouseEntered -= OnMouseEntered;
        MouseExited -= OnMouseExited;
    }

    public void BindPlayer(PlayerViewPlayerDto player)
    {
        Player = player;
        if (IsNodeReady())
        {
            ApplyPlayer();
        }
    }

    private void ApplyPlayer()
    {
        if (Player == null)
        {
            return;
        }

        _turnOrderLabel.Text = Player.TurnOrder.ToString();
        _nicknameLabel.Text = DisplayName(Player);
    }

    private void OnMouseEntered()
    {
        if (HoverStyle != null)
        {
            AddThemeStyleboxOverride("panel", HoverStyle);
        }

        HoverChanged?.Invoke(this, true);
    }

    private void OnMouseExited()
    {
        if (NormalStyle != null)
        {
            AddThemeStyleboxOverride("panel", NormalStyle);
        }

        HoverChanged?.Invoke(this, false);
    }

    private static string DisplayName(PlayerViewPlayerDto player)
    {
        return string.IsNullOrWhiteSpace(player.Nickname) ? $"玩家 {player.UserId}" : player.Nickname.Trim();
    }
}
