using System;
using System.Collections.Generic;
using System.Linq;
using EquestriaStar.Api;

namespace EquestriaStar.State;

public enum RoomPageTarget
{
    Stay,
    Lobby,
    Room,
    CharacterSelect,
    GameTest
}

public static class RoomStateModel
{
    public static IReadOnlyList<RoomMemberDto> SortedMembers(RoomDetailDto room)
    {
        return (room.Members ?? []).OrderBy(member => member.SeatNo).ToList();
    }

    public static RoomMemberDto? CurrentMember(RoomDetailDto room, long userId)
    {
        return (room.Members ?? []).FirstOrDefault(member => member.UserId == userId);
    }

    public static bool ContainsUser(RoomDetailDto room, long userId)
    {
        return CurrentMember(room, userId) != null;
    }

    public static bool IsOwner(RoomDetailDto room, long userId)
    {
        return room.OwnerUserId == userId || string.Equals(CurrentMember(room, userId)?.MemberRole, "OWNER", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReady(RoomDetailDto room, long userId)
    {
        return string.Equals(CurrentMember(room, userId)?.ReadyStatus, "READY", StringComparison.OrdinalIgnoreCase);
    }

    public static bool AllRegularMembersReady(RoomDetailDto room)
    {
        return (room.Members ?? [])
            .Where(member => string.Equals(member.MemberRole, "MEMBER", StringComparison.OrdinalIgnoreCase))
            .All(member => string.Equals(member.ReadyStatus, "READY", StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanOwnerStart(RoomDetailDto room, long userId, bool requestInProgress)
    {
        var memberCount = Math.Max(room.CurrentPlayers, room.Members?.Count ?? 0);
        return !requestInProgress
            && string.Equals(room.Status, "WAITING", StringComparison.OrdinalIgnoreCase)
            && IsOwner(room, userId)
            && memberCount >= 2
            && AllRegularMembersReady(room);
    }

    public static string ReadyStatusAfterAction(bool ready)
    {
        return ready ? "READY" : "NOT_READY";
    }

    public static string MemberDisplayName(RoomMemberDto member)
    {
        var name = string.IsNullOrWhiteSpace(member.Nickname) ? member.Username : member.Nickname;
        var isOwner = string.Equals(member.MemberRole, "OWNER", StringComparison.OrdinalIgnoreCase);
        var isReady = string.Equals(member.ReadyStatus, "READY", StringComparison.OrdinalIgnoreCase);
        return !isOwner && isReady ? $"{name} ✓" : name;
    }

    public static RoomPageTarget TargetForRoom(RoomDetailDto room)
    {
        if (string.Equals(room.Status, "CHARACTER_SELECTING", StringComparison.OrdinalIgnoreCase))
        {
            return RoomPageTarget.CharacterSelect;
        }

        if (string.Equals(room.Status, "IN_GAME", StringComparison.OrdinalIgnoreCase) && room.CurrentGameId.HasValue)
        {
            return RoomPageTarget.GameTest;
        }

        return RoomPageTarget.Stay;
    }

    public static RoomPageTarget TargetForCharacterSelect(RoomDetailDto room)
    {
        if (string.Equals(room.Status, "WAITING", StringComparison.OrdinalIgnoreCase))
        {
            return RoomPageTarget.Room;
        }

        if (string.Equals(room.Status, "IN_GAME", StringComparison.OrdinalIgnoreCase) && room.CurrentGameId.HasValue)
        {
            return RoomPageTarget.GameTest;
        }

        return RoomPageTarget.Stay;
    }

    public static RoomPageTarget TargetForError(string? errorCode)
    {
        return string.Equals(errorCode, "ROOM_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
            ? RoomPageTarget.Lobby
            : RoomPageTarget.Stay;
    }
}

public sealed class RoomNavigationGate
{
    public bool HasTransitioned { get; private set; }
    public int TransitionCount { get; private set; }

    public bool TryBegin(RoomPageTarget target)
    {
        if (target == RoomPageTarget.Stay || HasTransitioned)
        {
            return false;
        }

        HasTransitioned = true;
        TransitionCount++;
        return true;
    }

    public void Reset()
    {
        HasTransitioned = false;
        TransitionCount = 0;
    }
}
