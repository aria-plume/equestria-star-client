using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class RegisterScreen : Control
{
    [Signal]
    public delegate void BackToLoginEventHandler(string username);

    [Signal]
    public delegate void RegisterSucceededEventHandler(string username);

    private LineEdit _usernameEdit = null!;
    private LineEdit _passwordEdit = null!;
    private LineEdit _confirmPasswordEdit = null!;
    private LineEdit _nicknameEdit = null!;
    private Button _registerButton = null!;
    private Button _backButton = null!;
    private Label _statusLabel = null!;
    private bool _submitting;
    private string _pendingPrefillUsername = "";

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");

    public override void _Ready()
    {
        _usernameEdit = GetNode<LineEdit>("CenterContainer/RegisterPanel/FormMargin/Form/UsernameEdit");
        _passwordEdit = GetNode<LineEdit>("CenterContainer/RegisterPanel/FormMargin/Form/PasswordEdit");
        _confirmPasswordEdit = GetNode<LineEdit>("CenterContainer/RegisterPanel/FormMargin/Form/ConfirmPasswordEdit");
        _nicknameEdit = GetNode<LineEdit>("CenterContainer/RegisterPanel/FormMargin/Form/NicknameEdit");
        _registerButton = GetNode<Button>("CenterContainer/RegisterPanel/FormMargin/Form/RegisterButton");
        _backButton = GetNode<Button>("CenterContainer/RegisterPanel/FormMargin/Form/BackButton");
        _statusLabel = GetNode<Label>("CenterContainer/RegisterPanel/FormMargin/Form/StatusLabel");

        _passwordEdit.Secret = true;
        _confirmPasswordEdit.Secret = true;
        _registerButton.Pressed += OnRegisterPressed;
        _backButton.Pressed += () => EmitSignal(SignalName.BackToLogin, _usernameEdit.Text.Trim());
        _statusLabel.Text = "";
        _usernameEdit.Text = _pendingPrefillUsername;
    }

    public void SetPrefillUsername(string username)
    {
        _pendingPrefillUsername = username;
        if (IsNodeReady())
        {
            _usernameEdit.Text = username;
        }
    }

    private async void OnRegisterPressed()
    {
        if (_submitting)
        {
            return;
        }

        var username = _usernameEdit.Text.Trim();
        var password = _passwordEdit.Text;
        var confirmPassword = _confirmPasswordEdit.Text;
        var nickname = _nicknameEdit.Text.Trim();
        var validationMessage = ValidateForm(username, password, confirmPassword, nickname);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            ShowStatus(validationMessage, true);
            return;
        }

        SetLoading(true);
        var result = await ApiClient.RegisterAsync(username, password, nickname);
        if (!IsInsideTree())
        {
            return;
        }

        SetLoading(false);
        if (!result.Ok)
        {
            ShowStatus(result.Message, true);
            return;
        }

        Session.SetRememberedUsername(username);
        EmitSignal(SignalName.RegisterSucceeded, username);
    }

    private static string ValidateForm(string username, string password, string confirmPassword, string nickname)
    {
        if (username.Length is < 3 or > 64)
        {
            return "账号长度需要为 3～64 个字符。";
        }

        if (password.Length is < 8 or > 64)
        {
            return "密码长度需要为 8～64 个字符。";
        }

        if (nickname.Length is < 1 or > 64)
        {
            return "昵称长度需要为 1～64 个字符。";
        }

        return password != confirmPassword ? "两次输入的密码不一致。" : "";
    }

    private void SetLoading(bool loading)
    {
        _submitting = loading;
        _registerButton.Disabled = loading;
        _backButton.Disabled = loading;
        _usernameEdit.Editable = !loading;
        _passwordEdit.Editable = !loading;
        _confirmPasswordEdit.Editable = !loading;
        _nicknameEdit.Editable = !loading;
        ShowStatus(loading ? "正在注册，请稍候..." : "", false);
    }

    private void ShowStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride("font_color", isError ? new Color(0.72f, 0.16f, 0.11f) : new Color(0.18f, 0.38f, 0.48f));
    }
}
