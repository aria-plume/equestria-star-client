using Godot;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class GameTestScreen : Control
{
    private Label _roomIdLabel = null!;
    private Label _gameIdLabel = null!;

    private Session Session => GetNode<Session>("/root/Session");

    public override void _Ready()
    {
        _roomIdLabel = GetNode<Label>("Center/Content/RoomIdLabel");
        _gameIdLabel = GetNode<Label>("Center/Content/GameIdLabel");
        RefreshFromSession();
    }

    public void RefreshFromSession()
    {
        if (!IsNodeReady())
        {
            return;
        }

        _roomIdLabel.Text = $"房间 ID：{Session.CurrentRoomId?.ToString() ?? "-"}";
        _gameIdLabel.Text = $"gameId：{Session.CurrentGameId?.ToString() ?? "-"}";
    }
}
