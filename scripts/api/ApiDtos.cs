using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace EquestriaStar.Api;

public sealed class ApiEnvelope<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public sealed class ApiResult<T>
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }
    public int HttpStatus { get; init; }
    public bool IsAuthError { get; init; }

    public static ApiResult<T> Success(T? data, string? message, int httpStatus)
    {
        return new ApiResult<T>
        {
            Ok = true,
            Data = data,
            Message = message ?? "",
            HttpStatus = httpStatus
        };
    }

    public static ApiResult<T> Failure(string message, string? errorCode = null, int httpStatus = 0, T? data = default, bool isAuthError = false)
    {
        return new ApiResult<T>
        {
            Ok = false,
            Message = string.IsNullOrWhiteSpace(message) ? "操作失败。" : message,
            ErrorCode = errorCode,
            HttpStatus = httpStatus,
            Data = data,
            IsAuthError = isAuthError
        };
    }
}

public sealed class UserDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
}

public sealed class LoginResponseDto
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("user")]
    public UserDto? User { get; set; }
}

public sealed class RegisterResponseDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";
}

public sealed class ActiveSessionResponseDto
{
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("roomId")]
    public long? RoomId { get; set; }

    [JsonPropertyName("gameId")]
    public long? GameId { get; set; }

    [JsonPropertyName("memberRole")]
    public string? MemberRole { get; set; }

    [JsonPropertyName("readyStatus")]
    public string? ReadyStatus { get; set; }

    [JsonPropertyName("selectedCharacterId")]
    public long? SelectedCharacterId { get; set; }

    [JsonPropertyName("characterSelectionDeadline")]
    public string? CharacterSelectionDeadline { get; set; }
}

public sealed class CharacterDto
{
    [JsonPropertyName("characterId")]
    public long CharacterId { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("characterType")]
    public string CharacterType { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("attrStrength")]
    public int AttrStrength { get; set; }

    [JsonPropertyName("attrSpeed")]
    public int AttrSpeed { get; set; }

    [JsonPropertyName("attrSocial")]
    public int AttrSocial { get; set; }

    [JsonPropertyName("attrCharm")]
    public int AttrCharm { get; set; }

    [JsonPropertyName("attrMagic")]
    public int AttrMagic { get; set; }

    [JsonPropertyName("attrMind")]
    public int AttrMind { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("skillName")]
    public string? SkillName { get; set; }

    [JsonPropertyName("skillText")]
    public string? SkillText { get; set; }

    [JsonPropertyName("skillEffectId")]
    public string? SkillEffectId { get; set; }
}

public sealed class RoomDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("roomName")]
    public string RoomName { get; set; } = "";

    [JsonPropertyName("ownerUserId")]
    public long? OwnerUserId { get; set; }

    [JsonPropertyName("ownerNickname")]
    public string? OwnerNickname { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("statusText")]
    public string? StatusText { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("currentPlayers")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
}

public sealed class RoomPageDto
{
    [JsonPropertyName("records")]
    public List<RoomDto>? Records { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public sealed class JoinRoomResponseDto
{
    [JsonPropertyName("playerId")]
    public long PlayerId { get; set; }

    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    [JsonPropertyName("selectedCharacterId")]
    public long? SelectedCharacterId { get; set; }
}

public sealed class RoomDetailDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("roomName")]
    public string RoomName { get; set; } = "";

    [JsonPropertyName("ownerUserId")]
    public long OwnerUserId { get; set; }

    [JsonPropertyName("ownerNickname")]
    public string OwnerNickname { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("currentPlayers")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("currentGameId")]
    public long? CurrentGameId { get; set; }

    [JsonPropertyName("characterSelectionDeadline")]
    public string? CharacterSelectionDeadline { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("members")]
    public List<RoomMemberDto>? Members { get; set; }
}

public sealed class RoomMemberDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("seatNo")]
    public int SeatNo { get; set; }

    [JsonPropertyName("memberRole")]
    public string MemberRole { get; set; } = "";

    [JsonPropertyName("readyStatus")]
    public string ReadyStatus { get; set; } = "";

    [JsonPropertyName("selectedCharacterId")]
    public long? SelectedCharacterId { get; set; }

    [JsonPropertyName("selectedCharacterCode")]
    public string? SelectedCharacterCode { get; set; }

    [JsonPropertyName("selectedCharacterName")]
    public string? SelectedCharacterName { get; set; }

    [JsonPropertyName("selectedCharacterTitle")]
    public string? SelectedCharacterTitle { get; set; }

    [JsonPropertyName("selectedCharacterSkillName")]
    public string? SelectedCharacterSkillName { get; set; }

    [JsonPropertyName("selectedCharacterSkillText")]
    public string? SelectedCharacterSkillText { get; set; }

    [JsonPropertyName("selectedCharacterSkillEffectId")]
    public string? SelectedCharacterSkillEffectId { get; set; }

    [JsonPropertyName("joinedAt")]
    public string? JoinedAt { get; set; }
}

public sealed class ReadyRoomResponseDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }
}

public sealed class LeaveRoomResponseDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("roomDeleted")]
    public bool RoomDeleted { get; set; }
}

public sealed class StartCharacterSelectionResponseDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("characterSelectionDeadline")]
    public string? CharacterSelectionDeadline { get; set; }
}
