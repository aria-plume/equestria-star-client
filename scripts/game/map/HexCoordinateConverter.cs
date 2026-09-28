using System;
using Godot;

namespace EquestriaStar.Game.Map;

public static class HexCoordinateConverter
{
    public static Vector3 ToBaseWorld(int q, int r, float radius)
    {
        var safeRadius = Math.Max(0.01f, radius);
        var x = Mathf.Sqrt(3.0f) * safeRadius * (q + r / 2.0f);
        var z = 1.5f * safeRadius * r;
        return new Vector3(x, 0.0f, z);
    }

    public static Vector3 ToWorld(int q, int r, string boardCode, float radius)
    {
        if (!MapLayoutConfig.TryGetBoardOffset(boardCode, out var offset))
        {
            throw new ArgumentException($"未知板块编码：{boardCode}", nameof(boardCode));
        }

        var basePosition = ToBaseWorld(q, r, radius);
        return basePosition + new Vector3(offset.X * radius, 0.0f, offset.Y * radius);
    }
}
