using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class RoomCard : PanelContainer
{
    [Signal]
    public delegate void JoinRequestedEventHandler(RoomCard card);

    private Label _roomNameLabel = null!;
    private Label _ownerLabel = null!;
    private Label _playersLabel = null!;
    private Label _statusLabel = null!;
    private Button _joinButton = null!;
    private RoomDto _room = new();

    public RoomDto Room => _room;

    public override void _Ready()
    {
        _roomNameLabel = GetNode<Label>("Margin/Row/MainInfo/RoomNameLabel");
        _ownerLabel = GetNode<Label>("Margin/Row/MainInfo/MetaRow/OwnerLabel");
        _playersLabel = GetNode<Label>("Margin/Row/MainInfo/MetaRow/PlayersLabel");
        _statusLabel = GetNode<Label>("Margin/Row/MainInfo/MetaRow/StatusLabel");
        _joinButton = GetNode<Button>("Margin/Row/JoinButton");
        _joinButton.Pressed += () => EmitSignal(SignalName.JoinRequested, this);
    }

    public void Setup(RoomDto room)
    {
        _room = room;
        _roomNameLabel.Text = string.IsNullOrWhiteSpace(room.RoomName) ? "未命名房间" : room.RoomName;
        _ownerLabel.Text = $"房主：{(string.IsNullOrWhiteSpace(room.OwnerNickname) ? "未知" : room.OwnerNickname)}";
        _playersLabel.Text = $"人数：{room.CurrentPlayers}/{room.MaxPlayers}";
        _statusLabel.Text = RoomListModel.StatusLabel(room);
        var joinable = RoomListModel.CanJoin(room);
        _joinButton.Disabled = !joinable;
        _joinButton.Text = joinable ? "加入" : "不可加入";
    }

    public void SetJoinLoading(bool loading)
    {
        var joinable = RoomListModel.CanJoin(_room);
        _joinButton.Disabled = loading || !joinable;
        _joinButton.Text = loading ? "加入中..." : joinable ? "加入" : "不可加入";
    }
}
