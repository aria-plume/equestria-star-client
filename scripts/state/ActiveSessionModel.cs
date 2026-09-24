using System;
using EquestriaStar.Api;

namespace EquestriaStar.State;

public enum ActiveSessionTarget
{
    Invalid,
    Lobby,
    Room,
    CharacterSelect,
    GameTest
}

public sealed class ActiveSessionResolution
{
    public ActiveSessionTarget Target { get; init; }
    public long? RoomId { get; init; }
    public long? GameId { get; init; }
    public string Error { get; init; } = "";
    public bool IsValid => Target != ActiveSessionTarget.Invalid;
}

public static class ActiveSessionModel
{
    public static ActiveSessionResolution Resolve(ActiveSessionResponseDto? session)
    {
        if (session == null)
        {
            return Invalid("活动会话响应缺少 data。");
        }

        var phase = session.Phase?.Trim().ToUpperInvariant() ?? "";
        return phase switch
        {
            "NONE" => ResolveNone(session),
            "WAITING" => ResolveRoomPhase(session, ActiveSessionTarget.Room),
            "CHARACTER_SELECTING" => ResolveRoomPhase(session, ActiveSessionTarget.CharacterSelect),
            "IN_GAME" => ResolveGame(session),
            _ => Invalid(string.IsNullOrWhiteSpace(phase)
                ? "活动会话 phase 为空。"
                : $"未知活动会话 phase：{session.Phase}。")
        };
    }

    private static ActiveSessionResolution ResolveNone(ActiveSessionResponseDto session)
    {
        if (session.RoomId.HasValue || session.GameId.HasValue)
        {
            return Invalid("NONE 状态不应包含 roomId 或 gameId。");
        }

        return new ActiveSessionResolution { Target = ActiveSessionTarget.Lobby };
    }

    private static ActiveSessionResolution ResolveRoomPhase(ActiveSessionResponseDto session, ActiveSessionTarget target)
    {
        if (!IsPositive(session.RoomId))
        {
            return Invalid($"{session.Phase} 状态缺少有效 roomId。");
        }

        if (session.GameId.HasValue)
        {
            return Invalid($"{session.Phase} 状态不应包含 gameId。");
        }

        return new ActiveSessionResolution
        {
            Target = target,
            RoomId = session.RoomId
        };
    }

    private static ActiveSessionResolution ResolveGame(ActiveSessionResponseDto session)
    {
        if (!IsPositive(session.RoomId))
        {
            return Invalid("IN_GAME 状态缺少有效 roomId。");
        }

        if (!IsPositive(session.GameId))
        {
            return Invalid("IN_GAME 状态缺少有效 gameId。");
        }

        return new ActiveSessionResolution
        {
            Target = ActiveSessionTarget.GameTest,
            RoomId = session.RoomId,
            GameId = session.GameId
        };
    }

    private static bool IsPositive(long? value)
    {
        return value.HasValue && value.Value > 0;
    }

    private static ActiveSessionResolution Invalid(string error)
    {
        return new ActiveSessionResolution
        {
            Target = ActiveSessionTarget.Invalid,
            Error = error
        };
    }
}
