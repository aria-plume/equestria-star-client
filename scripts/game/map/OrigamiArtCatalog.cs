using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace EquestriaStar.Game.Map;

/// <summary>Art identities follow explicit names, never server cell IDs or guessed coordinates.</summary>
public static class OrigamiArtCatalog
{
    public sealed class Component
    {
        public string Path { get; set; } = "";
        public Bounds BoundsBlender { get; set; } = new();
    }
    public sealed class Bounds
    {
        public float[] Min { get; set; } = [0, 0, 0];
        public float[] Max { get; set; } = [0, 0, 0];
    }
    public sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string[] Aliases { get; set; } = [];
        public Dictionary<string, Component> Components { get; set; } = new();
        public string ScenePath => $"res://assets/art/origami/{Id}/visual.tscn";
        public float Height
        {
            get
            {
                var height = 0.0f;
                foreach (var component in Components.Values)
                    height = Math.Max(height, component.BoundsBlender.Max[2]);
                return height;
            }
        }
    }
    private sealed class Catalog { public Entry[] Assets { get; set; } = []; }
    private static readonly Dictionary<string, Entry> ByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PackedScene> Scenes = new(StringComparer.Ordinal);
    private static Entry[]? _entries;
    public static IReadOnlyList<Entry> Entries { get { EnsureLoaded(); return _entries!; } }

    private static void EnsureLoaded()
    {
        if (_entries != null) return;
        try
        {
            var json = FileAccess.GetFileAsString("res://assets/art/origami/catalog.json");
            _entries = JsonSerializer.Deserialize<Catalog>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Assets ?? [];
            foreach (var entry in _entries)
                foreach (var alias in entry.Aliases)
                    if (!ByName.TryAdd(alias, entry))
                        throw new InvalidOperationException($"Duplicate art alias: {alias}");
        }
        catch (Exception exception)
        {
            _entries = [];
            ByName.Clear();
            GD.PushWarning($"折纸资产目录不可用，保留基础地块：{exception.Message}");
        }
    }

    public static Entry? Resolve(HexCellData data, bool includeCandidates = false)
    {
        EnsureLoaded();
        foreach (var name in new[] { data.CellName, data.CellNameText })
        {
            if (ByName.TryGetValue(name.Trim(), out var entry))
                return entry.Status != "candidate" || includeCandidates ? entry : null;
        }
        return null;
    }

    public static PackedScene? LoadScene(Entry entry)
    {
        if (Scenes.TryGetValue(entry.Id, out var cached)) return cached;
        if (!ResourceLoader.Exists(entry.ScenePath)) return null;
        var scene = GD.Load<PackedScene>(entry.ScenePath);
        if (scene != null) Scenes[entry.Id] = scene;
        return scene;
    }

    public static float Apply(HexCell3D cell, Entry entry)
    {
        var scene = LoadScene(entry);
        if (scene == null) return 0;
        cell.CustomVisualScale = Vector3.One * cell.Radius;
        cell.CustomTerrainHeightScale = cell.Height / (0.2f * cell.Radius);
        cell.CustomVisualScene = scene;
        return entry.Height * cell.Radius;
    }
}
