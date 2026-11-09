using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquestriaStar.Game.Map;

public sealed class EnvironmentArtCatalogDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("groundColor")]
    public string GroundColor { get; set; } = "d8dfd2";

    [JsonPropertyName("waterColor")]
    public string WaterColor { get; set; } = "7fb6bf";

    [JsonPropertyName("groundPadding")]
    public float GroundPadding { get; set; } = 2.5f;

    [JsonPropertyName("waterPadding")]
    public float WaterPadding { get; set; } = 8.0f;

    [JsonPropertyName("assets")]
    public List<EnvironmentArtAssetEntry> Assets { get; set; } = [];

    [JsonPropertyName("placements")]
    public List<EnvironmentArtPlacement> Placements { get; set; } = [];
}

public sealed class EnvironmentArtAssetEntry
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("scenePaths")]
    public List<string> ScenePaths { get; set; } = [];
}

public sealed class EnvironmentArtPlacement
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; set; } = "";

    [JsonPropertyName("normalizedX")]
    public float NormalizedX { get; set; }

    [JsonPropertyName("normalizedZ")]
    public float NormalizedZ { get; set; }

    [JsonPropertyName("yOffset")]
    public float YOffset { get; set; }

    [JsonPropertyName("scale")]
    public float Scale { get; set; } = 1.0f;

    [JsonPropertyName("rotationYDegrees")]
    public float RotationYDegrees { get; set; }
}

/// <summary>
/// 读取确定性的地图环境配置，并按资源路径缓存 PackedScene。
/// </summary>
public sealed class EnvironmentArtCatalog
{
    public const string DefaultCatalogPath = "res://assets/art/mappings/map-environment.json";

    private readonly Dictionary<string, EnvironmentArtAssetEntry> _assetsById =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackedScene> _scenesByPath =
        new(StringComparer.Ordinal);
    private EnvironmentArtCatalogDocument _document = new();
    private bool _loaded;

    public string GroundColor => _document.GroundColor;
    public string WaterColor => _document.WaterColor;
    public float GroundPadding => _document.GroundPadding;
    public float WaterPadding => _document.WaterPadding;
    public IReadOnlyList<EnvironmentArtPlacement> Placements => _document.Placements;
    public int CachedSceneCount => _scenesByPath.Count;
    public int ResourceLoadCount { get; private set; }
    public string LastError { get; private set; } = "";

    public bool EnsureLoaded()
    {
        if (_loaded)
        {
            return string.IsNullOrEmpty(LastError);
        }

        _loaded = true;
        if (!FileAccess.FileExists(DefaultCatalogPath))
        {
            LastError = $"缺少地图环境配置：{DefaultCatalogPath}";
            return false;
        }

        using var file = FileAccess.Open(DefaultCatalogPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            LastError = $"无法读取地图环境配置：{DefaultCatalogPath}";
            return false;
        }

        try
        {
            _document = JsonSerializer.Deserialize<EnvironmentArtCatalogDocument>(
                file.GetAsText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new EnvironmentArtCatalogDocument();

            foreach (var asset in _document.Assets)
            {
                if (string.IsNullOrWhiteSpace(asset.AssetId) || asset.ScenePaths.Count == 0)
                {
                    continue;
                }

                _assetsById.TryAdd(asset.AssetId, asset);
            }

            return true;
        }
        catch (JsonException exception)
        {
            LastError = $"地图环境配置格式无效：{exception.Message}";
            return false;
        }
    }

    public bool TryGetAsset(string assetId, out EnvironmentArtAssetEntry entry)
    {
        EnsureLoaded();
        return _assetsById.TryGetValue(assetId, out entry!);
    }

    public IReadOnlyList<PackedScene> LoadScenes(EnvironmentArtAssetEntry entry)
    {
        var scenes = new List<PackedScene>(entry.ScenePaths.Count);
        foreach (var path in entry.ScenePaths)
        {
            if (_scenesByPath.TryGetValue(path, out var cachedScene))
            {
                scenes.Add(cachedScene);
                continue;
            }

            ResourceLoadCount++;
            var scene = ResourceLoader.Load<PackedScene>(path);
            if (scene == null)
            {
                GD.PushWarning($"无法加载地图环境资源：{entry.AssetId} ({path})。");
                continue;
            }

            _scenesByPath[path] = scene;
            scenes.Add(scene);
        }

        return scenes;
    }
}
