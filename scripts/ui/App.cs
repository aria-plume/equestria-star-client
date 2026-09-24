using Godot;
using EquestriaStar.State;
using EquestriaStar.Api;

namespace EquestriaStar.UI;

public partial class App : Control
{
    private readonly PackedScene _loginScene = GD.Load<PackedScene>("res://scenes/screens/LoginScreen.tscn");
    private readonly PackedScene _registerScene = GD.Load<PackedScene>("res://scenes/screens/RegisterScreen.tscn");
    private readonly PackedScene _lobbyScene = GD.Load<PackedScene>("res://scenes/screens/LobbyScreen.tscn");
    private readonly PackedScene _roomScene = GD.Load<PackedScene>("res://scenes/screens/RoomScreen.tscn");
    private readonly PackedScene _characterSelectScene = GD.Load<PackedScene>("res://scenes/screens/CharacterSelectScreen.tscn");
    private readonly PackedScene _gameTestScene = GD.Load<PackedScene>("res://scenes/screens/GameTestScreen.tscn");

    private Control _pageHost = null!;
    private Label _footerLabel = null!;
    private Node? _currentScreen;
    private bool _handlingAuthClear;
    private Session _session = null!;
    private ApiClient _apiClient = null!;

    private Session Session => _session;
    private ApiClient ApiClient => _apiClient;

    public override void _Ready()
    {
        _pageHost = GetNode<Control>("MainLayout/PageHost");
        _footerLabel = GetNode<Label>("MainLayout/Footer/FooterMargin/FooterLabel");
        _session = GetNode<Session>("/root/Session");
        _apiClient = GetNode<ApiClient>("/root/ApiClient");
        ApiClient.AuthInvalidated += OnAuthInvalidated;
        Session.Changed += RefreshFooter;
        Session.AuthCleared += OnSessionCleared;
        RefreshFooter();
        ShowLogin(Session.RememberedUsername);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_apiClient))
        {
            _apiClient.AuthInvalidated -= OnAuthInvalidated;
        }

        if (GodotObject.IsInstanceValid(_session))
        {
            _session.Changed -= RefreshFooter;
            _session.AuthCleared -= OnSessionCleared;
        }
    }

    public void ShowLogin(string prefillUsername = "")
    {
        var screen = SwitchTo<LoginScreen>(_loginScene);
        screen.RecoverySucceeded += OnRecoverySucceeded;
        screen.RequestRegister += OnRequestRegister;
        screen.SetPrefillUsername(prefillUsername);
    }

    public void ShowRegister(string prefillUsername = "")
    {
        var screen = SwitchTo<RegisterScreen>(_registerScene);
        screen.BackToLogin += ShowLogin;
        screen.RegisterSucceeded += ShowLogin;
        screen.SetPrefillUsername(prefillUsername);
    }

    public void ShowLobby()
    {
        var screen = SwitchTo<LobbyScreen>(_lobbyScene);
        screen.EnterRoom += ShowRoom;
        screen.LoggedOut += OnLoggedOut;
        screen.Start();
    }

    public void ShowRoom()
    {
        var screen = SwitchTo<RoomScreen>(_roomScene);
        screen.ReturnLobby += ShowLobby;
        screen.EnterCharacterSelect += ShowCharacterSelect;
        screen.EnterGameTest += ShowGameTest;
        screen.Start();
    }

    public void ShowCharacterSelect()
    {
        var screen = SwitchTo<CharacterSelectScreen>(_characterSelectScene);
        screen.ReturnLobby += ShowLobby;
        screen.ReturnRoom += ShowRoom;
        screen.EnterGameTest += ShowGameTest;
        screen.Start();
    }

    public void ShowGameTest()
    {
        var screen = SwitchTo<GameTestScreen>(_gameTestScene);
        screen.RefreshFromSession();
    }

    private T SwitchTo<T>(PackedScene scene) where T : Node
    {
        if (_currentScreen != null)
        {
            if (_currentScreen is LobbyScreen lobby)
            {
                lobby.Stop();
            }
            else if (_currentScreen is RoomScreen room)
            {
                room.Stop();
            }
            else if (_currentScreen is CharacterSelectScreen characterSelect)
            {
                characterSelect.Stop();
            }

            _currentScreen.QueueFree();
        }

        var next = scene.Instantiate<T>();
        _pageHost.AddChild(next);
        _currentScreen = next;
        return next;
    }

    private void OnRequestRegister(string username)
    {
        ShowRegister(username);
    }

    private void OnRecoverySucceeded(int targetValue)
    {
        switch ((ActiveSessionTarget)targetValue)
        {
            case ActiveSessionTarget.Lobby:
                ShowLobby();
                break;
            case ActiveSessionTarget.Room:
                ShowRoom();
                break;
            case ActiveSessionTarget.CharacterSelect:
                ShowCharacterSelect();
                break;
            case ActiveSessionTarget.GameTest:
                ShowGameTest();
                break;
            default:
                GD.PushWarning($"忽略无效的活动会话页面目标：{targetValue}。");
                break;
        }
    }

    private void OnLoggedOut()
    {
        ShowLogin(Session.RememberedUsername);
    }

    private void OnAuthInvalidated(string _message)
    {
        if (_handlingAuthClear || _currentScreen is LoginScreen)
        {
            return;
        }

        ShowLogin(Session.RememberedUsername);
    }

    private void OnSessionCleared()
    {
        _handlingAuthClear = true;
        ShowLogin(Session.RememberedUsername);
        _handlingAuthClear = false;
    }

    private void RefreshFooter()
    {
        _footerLabel.Text = Session.IsLoggedIn() ? $"当前玩家：{Session.GetPlayerName()}" : "未登录";
    }
}
