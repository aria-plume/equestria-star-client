using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class CreateRoomDialog : Control
{
    [Signal]
    public delegate void RoomCreatedEventHandler();

    private LineEdit _roomNameEdit = null!;
    private SpinBox _maxPlayersSpin = null!;
    private Button _createButton = null!;
    private Button _cancelButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;
    private bool _submitting;

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");

    public override void _Ready()
    {
        _roomNameEdit = GetNode<LineEdit>("Center/Panel/Margin/Form/Fields/RoomNameEdit");
        _maxPlayersSpin = GetNode<SpinBox>("Center/Panel/Margin/Form/Fields/PlayerRow/MaxPlayersSpin");
        _createButton = GetNode<Button>("Center/Panel/Margin/Form/ButtonRow/CreateButton");
        _cancelButton = GetNode<Button>("Center/Panel/Margin/Form/ButtonRow/CancelButton");
        _closeButton = GetNode<Button>("Center/Panel/Margin/Form/Header/CloseButton");
        _statusLabel = GetNode<Label>("Center/Panel/Margin/Form/StatusLabel");

        _createButton.Pressed += OnCreatePressed;
        _cancelButton.Pressed += CloseDialog;
        _closeButton.Pressed += CloseDialog;
        _roomNameEdit.TextSubmitted += _ => OnCreatePressed();
        _maxPlayersSpin.MinValue = 2;
        _maxPlayersSpin.MaxValue = 6;
        _maxPlayersSpin.Step = 1;
        _maxPlayersSpin.Value = 4;
        _statusLabel.Text = "";
        Hide();
    }

    public void OpenDialog()
    {
        _roomNameEdit.Text = "";
        _maxPlayersSpin.Value = 4;
        _statusLabel.Text = "";
        SetLoading(false);
        Show();
        _roomNameEdit.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && !_submitting && @event.IsActionPressed("ui_cancel"))
        {
            CloseDialog();
            GetViewport().SetInputAsHandled();
        }
    }

    private async void OnCreatePressed()
    {
        if (_submitting)
        {
            return;
        }

        var roomName = _roomNameEdit.Text.Trim();
        if (string.IsNullOrWhiteSpace(roomName))
        {
            ShowStatus("请输入房间名称。", true);
            return;
        }

        SetLoading(true);
        var result = await ApiClient.CreateRoomAsync(roomName, (int)_maxPlayersSpin.Value);
        if (!IsInsideTree())
        {
            return;
        }

        SetLoading(false);
        if (!result.Ok || result.Data == null)
        {
            ShowStatus(result.Message, true);
            return;
        }

        Session.SetCurrentRoom(result.Data.RoomId, string.IsNullOrWhiteSpace(result.Data.RoomName) ? roomName : result.Data.RoomName);
        CloseDialog();
        EmitSignal(SignalName.RoomCreated);
    }

    private void CloseDialog()
    {
        if (_submitting)
        {
            return;
        }

        Hide();
    }

    private void SetLoading(bool loading)
    {
        _submitting = loading;
        _createButton.Disabled = loading;
        _cancelButton.Disabled = loading;
        _closeButton.Disabled = loading;
        _roomNameEdit.Editable = !loading;
        _maxPlayersSpin.Editable = !loading;
        ShowStatus(loading ? "正在创建房间..." : "", false);
    }

    private void ShowStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride("font_color", isError ? new Color(0.72f, 0.16f, 0.11f) : new Color(0.18f, 0.38f, 0.48f));
    }
}
