using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.State;

public partial class Session : Node
{
    [Signal]
    public delegate void ChangedEventHandler();

    [Signal]
    public delegate void AuthClearedEventHandler();

    private const string SettingsPath = "user://settings.cfg";

    public string AccessToken { get; private set; } = "";
    public string RefreshToken { get; private set; } = "";
    public int ExpiresIn { get; private set; }
    public UserDto? User { get; private set; }
    public long? CurrentRoomId { get; private set; }
    public string CurrentRoomName { get; private set; } = "";
    public long? CurrentGameId { get; private set; }
    public string RememberedUsername { get; private set; } = "";

    public override void _Ready()
    {
        LoadSettings();
    }

    public bool IsLoggedIn()
    {
        return !string.IsNullOrWhiteSpace(AccessToken);
    }

    public void SetAuth(LoginResponseDto loginData)
    {
        AccessToken = loginData.AccessToken;
        RefreshToken = loginData.RefreshToken;
        ExpiresIn = loginData.ExpiresIn;
        User = loginData.User;
        if (!string.IsNullOrWhiteSpace(User?.Username))
        {
            SetRememberedUsername(User.Username);
        }

        EmitSignal(SignalName.Changed);
    }

    public void UpdateUser(UserDto user)
    {
        User = user;
        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            SetRememberedUsername(user.Username);
        }

        EmitSignal(SignalName.Changed);
    }

    public void ClearAuth()
    {
        AccessToken = "";
        RefreshToken = "";
        ExpiresIn = 0;
        User = null;
        ClearRoomState();
        EmitSignal(SignalName.Changed);
        EmitSignal(SignalName.AuthCleared);
    }

    public void ClearRoom()
    {
        ClearRoomState();
        EmitSignal(SignalName.Changed);
    }

    public void SetCurrentRoom(long roomId, string roomName)
    {
        CurrentRoomId = roomId;
        CurrentRoomName = roomName;
        CurrentGameId = null;
        EmitSignal(SignalName.Changed);
    }

    public void SetCurrentGame(long gameId)
    {
        CurrentGameId = gameId;
        EmitSignal(SignalName.Changed);
    }

    public void ApplyActiveSession(ActiveSessionResolution resolution)
    {
        if (!resolution.IsValid)
        {
            throw new System.ArgumentException("不能应用无效的活动会话结果。", nameof(resolution));
        }

        switch (resolution.Target)
        {
            case ActiveSessionTarget.Lobby:
                ClearRoomState();
                break;
            case ActiveSessionTarget.Room:
            case ActiveSessionTarget.CharacterSelect:
                CurrentRoomId = resolution.RoomId;
                CurrentRoomName = "";
                CurrentGameId = null;
                break;
            case ActiveSessionTarget.Game:
                CurrentRoomId = resolution.RoomId;
                CurrentRoomName = "";
                CurrentGameId = resolution.GameId;
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(resolution), "未知的活动会话页面目标。");
        }

        EmitSignal(SignalName.Changed);
    }

    public string GetPlayerName()
    {
        if (!string.IsNullOrWhiteSpace(User?.Nickname))
        {
            return User.Nickname;
        }

        if (!string.IsNullOrWhiteSpace(User?.Username))
        {
            return User.Username;
        }

        return "未登录";
    }

    public void SetRememberedUsername(string username)
    {
        RememberedUsername = username;
        var config = new ConfigFile();
        config.SetValue("login", "username", RememberedUsername);
        config.Save(SettingsPath);
    }

    private void LoadSettings()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) == Error.Ok)
        {
            RememberedUsername = config.GetValue("login", "username", "").AsString();
        }
    }

    private void ClearRoomState()
    {
        CurrentRoomId = null;
        CurrentRoomName = "";
        CurrentGameId = null;
    }
}
