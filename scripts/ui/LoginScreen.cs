using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class LoginScreen : Control
{
    [Signal]
    public delegate void RecoverySucceededEventHandler(int target);

    [Signal]
    public delegate void RequestRegisterEventHandler(string username);

    private LineEdit _usernameEdit = null!;
    private LineEdit _passwordEdit = null!;
    private Button _togglePasswordButton = null!;
    private Button _loginButton = null!;
    private Button _registerButton = null!;
    private Button _retryRecoveryButton = null!;
    private Label _statusLabel = null!;
    private bool _submitting;
    private bool _recoveryInFlight;
    private bool _recoveryPending;
    private int _requestGeneration;
    private string _pendingPrefillUsername = "";

    private ApiClient ApiClient => GetNode<ApiClient>("/root/ApiClient");
    private Session Session => GetNode<Session>("/root/Session");

    public override void _Ready()
    {
        _usernameEdit = GetNode<LineEdit>("CenterContainer/LoginPanel/FormMargin/Form/UsernameEdit");
        _passwordEdit = GetNode<LineEdit>("CenterContainer/LoginPanel/FormMargin/Form/PasswordRow/PasswordEdit");
        _togglePasswordButton = GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/PasswordRow/TogglePasswordButton");
        _loginButton = GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/LoginButton");
        _registerButton = GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/RegisterButton");
        _retryRecoveryButton = GetNode<Button>("CenterContainer/LoginPanel/FormMargin/Form/RetryRecoveryButton");
        _statusLabel = GetNode<Label>("CenterContainer/LoginPanel/FormMargin/Form/StatusLabel");

        _passwordEdit.Secret = true;
        _togglePasswordButton.Pressed += OnTogglePassword;
        _loginButton.Pressed += OnLoginPressed;
        _registerButton.Pressed += () => EmitSignal(SignalName.RequestRegister, _usernameEdit.Text.Trim());
        _retryRecoveryButton.Pressed += OnRetryRecoveryPressed;
        _retryRecoveryButton.Visible = false;
        _statusLabel.Text = "";
        _usernameEdit.Text = _pendingPrefillUsername;
    }

    public override void _ExitTree()
    {
        _requestGeneration++;
    }

    public void SetPrefillUsername(string username)
    {
        _pendingPrefillUsername = username;
        if (IsNodeReady())
        {
            _usernameEdit.Text = username;
        }
    }

    private void OnTogglePassword()
    {
        _passwordEdit.Secret = !_passwordEdit.Secret;
        _togglePasswordButton.Text = _passwordEdit.Secret ? "显示" : "隐藏";
    }

    private async void OnLoginPressed()
    {
        if (_submitting || _recoveryInFlight || _recoveryPending)
        {
            return;
        }

        var username = _usernameEdit.Text.Trim();
        var password = _passwordEdit.Text;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowStatus("请输入账号和密码。", true);
            return;
        }

        var generation = ++_requestGeneration;
        SetCredentialControlsEnabled(false);
        _submitting = true;
        _retryRecoveryButton.Visible = false;
        ShowStatus("正在验证账号……", false);
        var loginResult = await ApiClient.LoginAsync(username, password);
        if (!CanApplyAsyncResult(generation))
        {
            return;
        }

        if (!loginResult.Ok || loginResult.Data == null)
        {
            FinishLoginFailure(loginResult.Message);
            return;
        }

        Session.SetAuth(loginResult.Data);
        ShowStatus("正在获取用户信息……", false);
        var meResult = await ApiClient.MeAsync();
        if (!CanApplyAsyncResult(generation))
        {
            return;
        }

        if (!meResult.Ok || meResult.Data == null)
        {
            if (Session.IsLoggedIn())
            {
                Session.ClearAuth();
            }

            FinishLoginFailure(meResult.Message);
            return;
        }

        Session.UpdateUser(meResult.Data);
        Session.SetRememberedUsername(username);
        _submitting = false;
        await RecoverActiveSessionAsync(generation);
    }

    private async void OnRetryRecoveryPressed()
    {
        if (!_recoveryPending || _recoveryInFlight || !Session.IsLoggedIn())
        {
            return;
        }

        await RecoverActiveSessionAsync(++_requestGeneration);
    }

    private async Task RecoverActiveSessionAsync(int generation)
    {
        if (_recoveryInFlight)
        {
            return;
        }

        _recoveryInFlight = true;
        _recoveryPending = false;
        _retryRecoveryButton.Visible = true;
        _retryRecoveryButton.Disabled = true;
        SetCredentialControlsEnabled(false);
        ShowStatus("正在恢复上次进度……", false);

        var result = await ApiClient.GetActiveSessionAsync();
        if (!CanApplyAsyncResult(generation))
        {
            return;
        }

        _recoveryInFlight = false;
        if (!result.Ok)
        {
            if (!Session.IsLoggedIn())
            {
                _recoveryPending = false;
                _retryRecoveryButton.Visible = false;
                SetCredentialControlsEnabled(true);
                ShowStatus(result.Message, true);
                return;
            }

            ShowRecoveryFailure();
            return;
        }

        var resolution = ActiveSessionModel.Resolve(result.Data);
        if (!resolution.IsValid)
        {
            GD.PushWarning(resolution.Error);
            ShowRecoveryFailure();
            return;
        }

        Session.ApplyActiveSession(resolution);
        _recoveryPending = false;
        _retryRecoveryButton.Visible = false;
        EmitSignal(SignalName.RecoverySucceeded, (int)resolution.Target);
    }

    private void ShowRecoveryFailure()
    {
        _recoveryPending = true;
        _retryRecoveryButton.Visible = true;
        _retryRecoveryButton.Disabled = false;
        SetCredentialControlsEnabled(false);
        ShowStatus("无法确认当前房间状态，请重试", true);
    }

    private void FinishLoginFailure(string message)
    {
        _submitting = false;
        _recoveryInFlight = false;
        _recoveryPending = false;
        _retryRecoveryButton.Visible = false;
        SetCredentialControlsEnabled(true);
        ShowStatus(message, true);
    }

    private void SetCredentialControlsEnabled(bool enabled)
    {
        _loginButton.Disabled = !enabled;
        _registerButton.Disabled = !enabled;
        _togglePasswordButton.Disabled = !enabled;
        _usernameEdit.Editable = enabled;
        _passwordEdit.Editable = enabled;
    }

    private bool CanApplyAsyncResult(int generation)
    {
        return GodotObject.IsInstanceValid(this)
            && IsInsideTree()
            && !IsQueuedForDeletion()
            && generation == _requestGeneration;
    }

    private void ShowStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride("font_color", isError ? new Color(0.72f, 0.16f, 0.11f) : new Color(0.18f, 0.38f, 0.48f));
    }
}
