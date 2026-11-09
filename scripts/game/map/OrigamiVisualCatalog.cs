using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquestriaStar.Game.Map;

public sealed class OrigamiVisualCatalogDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("entries")]
    public List<OrigamiVisualEntry> Entries { get; set; } = [];

    [JsonPropertyName("boardTerrainMappings")]
    public Dictionary<string, string> BoardTerrainMappings { get; set; } = new(StringComparer.Ordinal);
}

public enum OrigamiVisualMatchKind
{
    None,
    Dedicated,
    GenericTerrain
}

public sealed class OrigamiVisualEntry
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("scenePath")]
    public string ScenePath { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("rotationYDegrees")]
    public float RotationYDegrees { get; set; } = 30.0f;

    [JsonPropertyName("scale")]
    public float Scale { get; set; } = 1.0f;

    [JsonPropertyName("tokenOffsetX")]
    public float TokenOffsetX { get; set; }

    [JsonPropertyName("tokenOffsetY")]
    public float TokenOffsetY { get; set; }

    [JsonPropertyName("tokenOffsetZ")]
    public float TokenOffsetZ { get; set; }

    public Vector3 TokenOffset => new(TokenOffsetX, TokenOffsetY, TokenOffsetZ);
}

/// <summary>
/// 读取本项目维护的折纸美术映射，并按需缓存实际使用的包装场景。
/// </summary>
public sealed class OrigamiVisualCatalog
{
    public const string DefaultCatalogPath = "res://assets/art/mappings/origami-cell-visuals.json";

    private readonly Dictionary<string, OrigamiVisualEntry> _entriesByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrigamiVisualEntry> _entriesByAssetId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _genericTerrainAssetIdByBoardCode =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _genericTerrainAssetIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackedScene> _scenesByAssetId =
        new(StringComparer.Ordinal);
    private bool _loaded;

    public int CachedSceneCount => _scenesByAssetId.Count;
    public int EntryCount { get; private set; }
    public IReadOnlyDictionary<string, string> BoardTerrainMappings => _genericTerrainAssetIdByBoardCode;
    public string LastError { get; private set; } = "";

    public bool TryResolve(string? cellName, string? cellNameText, out OrigamiVisualEntry entry)
    {
        EnsureLoaded();
        if (TryResolveName(cellName, out entry))
        {
            return true;
        }

        return TryResolveName(cellNameText, out entry);
    }

    public PackedScene? LoadScene(OrigamiVisualEntry entry)
    {
        if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.AssetId) || string.IsNullOrWhiteSpace(entry.ScenePath))
        {
            return null;
        }

        if (_scenesByAssetId.TryGetValue(entry.AssetId, out var cachedScene))
        {
            return cachedScene;
        }

        var scene = ResourceLoader.Load<PackedScene>(entry.ScenePath);
        if (scene == null)
        {
            GD.PushWarning($"无法加载格子美术包装场景：{entry.AssetId} ({entry.ScenePath})，将使用默认格子视觉。");
            return null;
        }

        _scenesByAssetId.Add(entry.AssetId, scene);
        return scene;
    }

    public bool TryResolveForCell(
        string? cellName,
        string? cellNameText,
        string? boardCode,
        out OrigamiVisualEntry entry,
        out OrigamiVisualMatchKind matchKind
    )
    {
        EnsureLoaded();
        if (TryResolve(cellName, cellNameText, out var dedicatedEntry)
            && dedicatedEntry.Enabled
            && !_genericTerrainAssetIds.Contains(dedicatedEntry.AssetId))
        {
            entry = dedicatedEntry;
            matchKind = OrigamiVisualMatchKind.Dedicated;
            return true;
        }

        if (TryResolveGenericTerrain(boardCode, out var genericEntry))
        {
            entry = genericEntry;
            matchKind = OrigamiVisualMatchKind.GenericTerrain;
            return true;
        }

        entry = null!;
        matchKind = OrigamiVisualMatchKind.None;
        return false;
    }

    public bool TryResolveGenericTerrain(string? boardCode, out OrigamiVisualEntry entry)
    {
        EnsureLoaded();
        if (!string.IsNullOrWhiteSpace(boardCode)
            && _genericTerrainAssetIdByBoardCode.TryGetValue(boardCode.Trim(), out var assetId)
            && _entriesByAssetId.TryGetValue(assetId, out var mappedEntry)
            && mappedEntry.Enabled)
        {
            entry = mappedEntry;
            return true;
        }

        entry = null!;
        return false;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!FileAccess.FileExists(DefaultCatalogPath))
        {
            LastError = $"缺少格子美术映射：{DefaultCatalogPath}";
            GD.PushWarning(LastError);
            return;
        }

        using var file = FileAccess.Open(DefaultCatalogPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            LastError = $"无法读取格子美术映射：{DefaultCatalogPath}";
            GD.PushWarning(LastError);
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<OrigamiVisualCatalogDocument>(
                file.GetAsText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (document == null)
            {
                LastError = "格子美术映射为空。";
                GD.PushWarning(LastError);
                return;
            }

            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.AssetId) || string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    continue;
                }

                EntryCount++;
                if (!_entriesByAssetId.TryAdd(entry.AssetId, entry))
                {
                    GD.PushWarning($"格子美术 assetId 重复：{entry.AssetId}，保留首个条目。");
                }
                RegisterName(entry.DisplayName, entry);
                foreach (var alias in entry.Aliases)
                {
                    RegisterName(alias, entry);
                }
            }

            foreach (var pair in document.BoardTerrainMappings)
            {
                var boardCode = pair.Key.Trim();
                var assetId = pair.Value.Trim();
                if (!MapLayoutConfig.IsKnownBoard(boardCode))
                {
                    GD.PushWarning($"通用地形映射包含未知板块：{boardCode}。");
                    continue;
                }

                if (!_entriesByAssetId.TryGetValue(assetId, out var terrainEntry) || !terrainEntry.Enabled)
                {
                    GD.PushWarning($"板块 {boardCode} 的通用地形不可用：{assetId}。");
                    continue;
                }

                _genericTerrainAssetIdByBoardCode[boardCode] = assetId;
                _genericTerrainAssetIds.Add(assetId);
            }
        }
        catch (JsonException exception)
        {
            LastError = $"格子美术映射格式无效：{exception.Message}";
            GD.PushWarning(LastError);
        }
    }

    private bool TryResolveName(string? name, out OrigamiVisualEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && _entriesByName.TryGetValue(name.Trim(), out var matchedEntry))
        {
            entry = matchedEntry;
            return true;
        }

        entry = null!;
        return false;
    }

    private void RegisterName(string? name, OrigamiVisualEntry entry)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var normalizedName = name.Trim();
        if (_entriesByName.TryGetValue(normalizedName, out var existingEntry)
            && !ReferenceEquals(existingEntry, entry))
        {
            GD.PushWarning($"格子美术映射名称重复：{normalizedName}，保留 {existingEntry.AssetId}。");
            return;
        }

        _entriesByName[normalizedName] = entry;
    }
}
