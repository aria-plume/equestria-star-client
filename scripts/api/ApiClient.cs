using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.State;

namespace EquestriaStar.Api;

public interface IApiTransport
{
    Task<ApiResult<T>> RequestJsonAsync<T>(
        HttpClient.Method method,
        string path,
        object? body,
        bool authorized,
        IReadOnlyDictionary<string, string?>? query
    );
}

public partial class ApiClient : Node
{
    [Signal]
    public delegate void AuthInvalidatedEventHandler(string message);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IApiTransport? _transportOverride;

    private Session Session => GetNode<Session>("/root/Session");

    public void SetTransportForTests(IApiTransport transport)
    {
        _transportOverride = transport;
    }

    public void ClearTransportForTests()
    {
        _transportOverride = null;
    }

    public Task<ApiResult<T>> GetJsonAsync<T>(string path, IReadOnlyDictionary<string, string?>? query = null, bool authorized = true)
    {
        return RequestJsonAsync<T>(HttpClient.Method.Get, path, null, authorized, query);
    }

    public Task<ApiResult<T>> PostJsonAsync<T>(string path, object? body = null, bool authorized = true, IReadOnlyDictionary<string, string?>? query = null)
    {
        return RequestJsonAsync<T>(HttpClient.Method.Post, path, body ?? new { }, authorized, query);
    }

    public async Task<ApiResult<T>> RequestJsonAsync<T>(
        HttpClient.Method method,
        string path,
        object? body = null,
        bool authorized = true,
        IReadOnlyDictionary<string, string?>? query = null
    )
    {
        if (_transportOverride != null)
        {
            var testResult = await _transportOverride.RequestJsonAsync<T>(method, path, body, authorized, query);
            HandleAuthInvalidation(testResult);
            return testResult;
        }

        using var request = new HttpRequest();
        request.Timeout = ApiConfig.RequestTimeoutSeconds;
        AddChild(request);

        var headers = new List<string> { "Content-Type: application/json", "Accept: application/json" };
        if (authorized && !string.IsNullOrWhiteSpace(Session.AccessToken))
        {
            headers.Add($"Authorization: Bearer {Session.AccessToken}");
        }

        var payload = method == HttpClient.Method.Get || body == null ? "" : JsonSerializer.Serialize(body, JsonOptions);
        var startError = request.Request(ApiConfig.BuildUrl(path, query), headers.ToArray(), method, payload);
        if (startError != Error.Ok)
        {
            request.QueueFree();
            return ApiResult<T>.Failure("无法发起请求，请检查网络连接。", startError.ToString());
        }

        var completed = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
        request.QueueFree();

        var result = (HttpRequest.Result)(int)completed[0];
        var httpStatus = (int)completed[1];
        var bodyBytes = (byte[])completed[3];

        var parsed = ParseHttpResult<T>(result, httpStatus, bodyBytes);
        HandleAuthInvalidation(parsed);

        return parsed;
    }

    public static ApiResult<T> ParseHttpResult<T>(HttpRequest.Result result, int httpStatus, byte[] body)
    {
        if (result == HttpRequest.Result.Timeout)
        {
            return ApiResult<T>.Failure("请求超时，请稍后再试。", "REQUEST_TIMEOUT", httpStatus);
        }

        if (result != HttpRequest.Result.Success)
        {
            return ApiResult<T>.Failure("无法连接服务器，请检查网络。", "NETWORK_ERROR", httpStatus);
        }

        var text = Encoding.UTF8.GetString(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ApiResult<T>.Failure("服务器返回为空。", "EMPTY_RESPONSE", httpStatus);
        }

        ApiEnvelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return ApiResult<T>.Failure("服务器返回格式不是有效 JSON。", "INVALID_JSON", httpStatus);
        }

        if (envelope == null)
        {
            return ApiResult<T>.Failure("服务器返回结构异常。", "INVALID_ENVELOPE", httpStatus);
        }

        var message = string.IsNullOrWhiteSpace(envelope.Msg) ? "操作失败。" : envelope.Msg!;
        var isAuthError = httpStatus == 401 || envelope.ErrorCode is "AUTH_REQUIRED" or "AUTH_TOKEN_EXPIRED" or "USER_NOT_FOUND";

        if (httpStatus < 200 || httpStatus >= 300)
        {
            return ApiResult<T>.Failure(message, envelope.ErrorCode ?? "HTTP_ERROR", httpStatus, envelope.Data, isAuthError);
        }

        if (envelope.Code != 1)
        {
            return ApiResult<T>.Failure(message, envelope.ErrorCode, httpStatus, envelope.Data, isAuthError);
        }

        return ApiResult<T>.Success(envelope.Data, envelope.Msg, httpStatus);
    }

    public Task<ApiResult<LoginResponseDto>> LoginAsync(string username, string password)
    {
        return PostJsonAsync<LoginResponseDto>("/auth/login", new { username, password }, false);
    }

    public Task<ApiResult<RegisterResponseDto>> RegisterAsync(string username, string password, string nickname)
    {
        return PostJsonAsync<RegisterResponseDto>("/auth/register", new { username, password, nickname }, false);
    }

    public Task<ApiResult<object>> LogoutAsync()
    {
        return PostJsonAsync<object>("/auth/logout", new { }, true);
    }

    public Task<ApiResult<UserDto>> MeAsync()
    {
        return GetJsonAsync<UserDto>("/users/me");
    }

    public Task<ApiResult<ActiveSessionResponseDto>> GetActiveSessionAsync()
    {
        return GetJsonAsync<ActiveSessionResponseDto>("/me/active-session");
    }

    public Task<ApiResult<List<CharacterDto>>> GetCharactersAsync()
    {
        return GetJsonAsync<List<CharacterDto>>("/characters", authorized: false);
    }

    public Task<ApiResult<RoomPageDto>> RoomsPageAsync(int page, int pageSize)
    {
        return GetJsonAsync<RoomPageDto>("/rooms", new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        });
    }

    public Task<ApiResult<RoomDto>> CreateRoomAsync(string roomName, int maxPlayers)
    {
        return PostJsonAsync<RoomDto>("/rooms", new { roomName, maxPlayers });
    }

    public Task<ApiResult<JoinRoomResponseDto>> JoinRoomAsync(long roomId)
    {
        return PostJsonAsync<JoinRoomResponseDto>($"/rooms/{roomId}/join", new { });
    }

    public Task<ApiResult<RoomDetailDto>> GetRoomDetailAsync(long roomId)
    {
        return GetJsonAsync<RoomDetailDto>($"/rooms/{roomId}", authorized: false);
    }

    public Task<ApiResult<ReadyRoomResponseDto>> ReadyRoomAsync(long roomId)
    {
        return PostJsonAsync<ReadyRoomResponseDto>($"/rooms/{roomId}/ready", new { });
    }

    public Task<ApiResult<ReadyRoomResponseDto>> UnreadyRoomAsync(long roomId)
    {
        return PostJsonAsync<ReadyRoomResponseDto>($"/rooms/{roomId}/unready", new { });
    }

    public Task<ApiResult<LeaveRoomResponseDto>> LeaveRoomAsync(long roomId)
    {
        return PostJsonAsync<LeaveRoomResponseDto>($"/rooms/{roomId}/leave", new { });
    }

    public Task<ApiResult<StartCharacterSelectionResponseDto>> StartCharacterSelectionAsync(long roomId)
    {
        return PostJsonAsync<StartCharacterSelectionResponseDto>($"/rooms/{roomId}/start-character-selection", new { });
    }

    public Task<ApiResult<object>> SelectCharacterAsync(long roomId, long selectedCharacterId)
    {
        return PostJsonAsync<object>($"/rooms/{roomId}/select-character", new { selectedCharacterId });
    }

    private void HandleAuthInvalidation<T>(ApiResult<T> result)
    {
        var isAuthError = result.IsAuthError
            || result.HttpStatus == 401
            || result.ErrorCode is "AUTH_REQUIRED" or "AUTH_TOKEN_EXPIRED" or "USER_NOT_FOUND";
        if (!isAuthError)
        {
            return;
        }

        Session.ClearAuth();
        EmitSignal(SignalName.AuthInvalidated, result.Message);
    }
}
