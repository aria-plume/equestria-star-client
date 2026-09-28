using System;
using System.Collections.Generic;
using Godot;

namespace EquestriaStar.Game.Map;

public static class MapLayoutConfig
{
    private static readonly IReadOnlyDictionary<string, Vector2> Offsets = new Dictionary<string, Vector2>(StringComparer.Ordinal)
    {
        ["A"] = new(0.000f, 0.000f),
        ["B"] = new(0.000f, 0.000f),
        ["C"] = new(-0.074f, -1.082f),
        ["D"] = new(0.551f, -1.154f),
        ["E"] = new(0.319f, -0.495f),
        ["F"] = new(0.736f, -0.179f),
        ["G"] = new(0.977f, 0.404f),
        ["H"] = new(-0.249f, 0.621f),
        ["I"] = new(0.645f, 0.616f),
        ["J"] = new(-0.800f, 0.217f),
        ["K"] = new(-0.795f, -0.304f),
        ["L"] = new(-0.340f, -0.528f)
    };

    private static readonly IReadOnlyDictionary<string, Color> Palette = new Dictionary<string, Color>(StringComparer.Ordinal)
    {
        ["A"] = new("77a9a5"),
        ["B"] = new("b78b5c"),
        ["C"] = new("668db8"),
        ["D"] = new("a7739b"),
        ["E"] = new("79a765"),
        ["F"] = new("c17468"),
        ["G"] = new("8a7fbd"),
        ["H"] = new("5f9e82"),
        ["I"] = new("ba8f9f"),
        ["J"] = new("7196c7"),
        ["K"] = new("a59a60"),
        ["L"] = new("8b9d72")
    };

    private static readonly Dictionary<string, StandardMaterial3D> TopMaterials = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StandardMaterial3D> SideMaterials = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, Vector2> BoardOffsets => Offsets;
    public static IReadOnlyDictionary<string, Color> BoardColors => Palette;

    public static bool TryGetBoardOffset(string? boardCode, out Vector2 offset)
    {
        return Offsets.TryGetValue(boardCode ?? "", out offset);
    }

    public static bool IsKnownBoard(string? boardCode)
    {
        return boardCode != null && Offsets.ContainsKey(boardCode);
    }

    public static Color GetBoardColor(string boardCode)
    {
        if (!Palette.TryGetValue(boardCode, out var color))
        {
            throw new ArgumentException($"未知板块编码：{boardCode}", nameof(boardCode));
        }

        return color;
    }

    public static StandardMaterial3D GetTopMaterial(string boardCode)
    {
        if (TopMaterials.TryGetValue(boardCode, out var material))
        {
            return material;
        }

        var color = GetBoardColor(boardCode);
        material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.86f,
            Metallic = 0.025f
        };
        TopMaterials.Add(boardCode, material);
        return material;
    }

    public static StandardMaterial3D GetSideMaterial(string boardCode)
    {
        if (SideMaterials.TryGetValue(boardCode, out var material))
        {
            return material;
        }

        var color = GetBoardColor(boardCode).Darkened(0.34f);
        material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.78f,
            Metallic = 0.04f
        };
        SideMaterials.Add(boardCode, material);
        return material;
    }
}
