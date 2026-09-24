using System;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class RoomMemberRow : PanelContainer
{
    private Label _seatLabel = null!;
    private Label _nameLabel = null!;
    private Label _roleLabel = null!;
    private Label _readyLabel = null!;

    public long UserId { get; private set; }

    public override void _Ready()
    {
        _seatLabel = GetNode<Label>("Margin/Row/SeatLabel");
        _nameLabel = GetNode<Label>("Margin/Row/NameLabel");
        _roleLabel = GetNode<Label>("Margin/Row/RoleLabel");
        _readyLabel = GetNode<Label>("Margin/Row/ReadyLabel");
    }

    public void UpdateMember(RoomMemberDto member)
    {
        UserId = member.UserId;
        _seatLabel.Text = member.SeatNo.ToString();
        _nameLabel.Text = RoomStateModel.MemberDisplayName(member);

        var isOwner = string.Equals(member.MemberRole, "OWNER", StringComparison.OrdinalIgnoreCase);
        var isReady = string.Equals(member.ReadyStatus, "READY", StringComparison.OrdinalIgnoreCase);
        _roleLabel.Text = isOwner ? "房主" : "";
        _roleLabel.Visible = isOwner;
        _readyLabel.Text = isOwner ? "无需准备" : isReady ? "已准备" : "未准备";
        _readyLabel.AddThemeColorOverride(
            "font_color",
            isReady && !isOwner ? new Color(0.12f, 0.46f, 0.28f) : new Color(0.35f, 0.44f, 0.48f)
        );
    }
}
