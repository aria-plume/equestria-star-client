using System;
using System.Collections.Generic;
using Godot;

namespace EquestriaStar.Game.Map;

public static class PlayerTokenLayout
{
    public static IReadOnlyList<Vector3> Calculate(int count, float requestedRadius, float tokenRadius)
    {
        if (count <= 0)
        {
            return Array.Empty<Vector3>();
        }

        count = Math.Min(count, 6);
        if (count == 1)
        {
            return [Vector3.Zero];
        }

        var safeTokenRadius = Math.Max(0.01f, tokenRadius);
        if (count == 2)
        {
            var halfSpacing = Math.Max(requestedRadius, safeTokenRadius * 1.15f);
            return
            [
                new Vector3(-halfSpacing, 0.0f, 0.0f),
                new Vector3(halfSpacing, 0.0f, 0.0f)
            ];
        }

        var minimumRadius = safeTokenRadius * 1.12f / MathF.Sin(MathF.PI / count);
        var radius = Math.Max(requestedRadius, minimumRadius);
        var positions = new Vector3[count];
        for (var index = 0; index < count; index++)
        {
            var angle = -MathF.PI * 0.5f + MathF.Tau * index / count;
            positions[index] = new Vector3(MathF.Cos(angle) * radius, 0.0f, MathF.Sin(angle) * radius);
        }

        return positions;
    }
}
