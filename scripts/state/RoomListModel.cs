using EquestriaStar.Api;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EquestriaStar.State;

public static class RoomListModel
{
    public static int PageCountFromTotal(int total, int pageSize, int maxPages)
    {
        if (total <= 0)
        {
            return 1;
        }

        var pages = (int)Math.Ceiling(total / (double)pageSize);
        return Math.Clamp(pages, 1, maxPages);
    }

    public static bool TotalExceedsPageLimit(int total, int pageSize, int maxPages)
    {
        return total > pageSize * maxPages;
    }

    public static List<RoomDto> MergeUniqueByRoomId(IEnumerable<IEnumerable<RoomDto>> pages)
    {
        var seen = new HashSet<long>();
        var result = new List<RoomDto>();
        foreach (var page in pages)
        {
            foreach (var room in page)
            {
                if (seen.Add(room.RoomId))
                {
                    result.Add(room);
                }
            }
        }

        return result;
    }

    public static string NormalizeSearchKeyword(string keyword)
    {
        return keyword.Trim().ToLowerInvariant();
    }

    public static List<RoomDto> FilterRooms(IEnumerable<RoomDto> rooms, string keyword)
    {
        var normalized = NormalizeSearchKeyword(keyword);
        if (string.IsNullOrEmpty(normalized))
        {
            return rooms.ToList();
        }

        return rooms
            .Where(room => room.RoomName.ToLowerInvariant().Contains(normalized))
            .ToList();
    }

    public static bool CanJoin(RoomDto room)
    {
        return room.Status == "WAITING" && room.CurrentPlayers < room.MaxPlayers;
    }

    public static string StatusLabel(RoomDto room)
    {
        if (!string.IsNullOrWhiteSpace(room.StatusText))
        {
            return room.StatusText!;
        }

        return room.Status switch
        {
            "WAITING" => "等待中",
            "PLAYING" => "游戏中",
            "FINISHED" => "已结束",
            "" => "未知状态",
            _ => room.Status
        };
    }
}
