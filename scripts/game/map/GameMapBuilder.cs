using System;
using System.Collections.Generic;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

public sealed record MapBuildResult(bool Ok, string Error, int CellCount, int EdgeCount, Aabb Bounds)
{
    public static MapBuildResult Failure(string error)
    {
        return new MapBuildResult(false, error, 0, 0, default);
    }
}

public sealed class GameMapBuilder
{
    private Node3D? _generatedRoot;
    private Dictionary<long, HexCell3D> _cellsById = new();
    private Dictionary<string, HexCell3D> _cellsByCode = new(StringComparer.Ordinal);
    private IReadOnlyList<MapEdgeDto> _edges = Array.Empty<MapEdgeDto>();

    public IReadOnlyDictionary<long, HexCell3D> CellsById => _cellsById;
    public IReadOnlyDictionary<string, HexCell3D> CellsByCode => _cellsByCode;
    public IReadOnlyList<MapEdgeDto> Edges => _edges;

    public MapBuildResult Build(
        GameMapDto map,
        Node3D mapRoot,
        PackedScene cellScene,
        float radius,
        float height,
        float bevelWidth,
        bool showDebugLabels
    )
    {
        var validation = MapDataValidator.Validate(map);
        if (!validation.IsValid)
        {
            return MapBuildResult.Failure(validation.Error);
        }

        var safeRadius = Math.Max(0.1f, radius);
        var safeHeight = Math.Max(0.02f, height);
        var newRoot = new Node3D { Name = "GeneratedCells" };
        var newCellsById = new Dictionary<long, HexCell3D>();
        var newCellsByCode = new Dictionary<string, HexCell3D>(StringComparer.Ordinal);
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minZ = float.MaxValue;
        var maxZ = float.MinValue;

        try
        {
            foreach (var dto in map.Cells!)
            {
                var q = dto.Q!.Value;
                var r = dto.R!.Value;
                var cell = cellScene.Instantiate<HexCell3D>();
                cell.Name = dto.CellCode;
                cell.Radius = safeRadius;
                cell.Height = safeHeight;
                cell.BevelWidth = bevelWidth;
                cell.TopMaterial = MapLayoutConfig.GetTopMaterial(dto.BoardCode);
                cell.SideMaterial = MapLayoutConfig.GetSideMaterial(dto.BoardCode);
                cell.ShowDebugLabel = showDebugLabels;
                cell.Position = HexCoordinateConverter.ToWorld(q, r, dto.BoardCode, safeRadius);

                var tags = new Godot.Collections.Array<string>();
                if (dto.InteractionTags != null)
                {
                    foreach (var tag in dto.InteractionTags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            tags.Add(tag);
                        }
                    }
                }

                cell.BindData(new HexCellData
                {
                    CellId = dto.CellId,
                    CellCode = dto.CellCode,
                    BoardCode = dto.BoardCode,
                    CellName = dto.CellName,
                    CellNameText = dto.CellNameText,
                    Q = q,
                    R = r,
                    InteractionTags = tags
                });

                var debugLabel = cell.GetNode<Label3D>("DebugLabel");
                debugLabel.FontSize = 34;
                debugLabel.OutlineSize = 8;
                debugLabel.PixelSize = 0.0024f;
                debugLabel.Position = new Vector3(0.0f, safeHeight + 0.06f, 0.0f);

                newRoot.AddChild(cell);
                newCellsById.Add(dto.CellId, cell);
                newCellsByCode.Add(dto.CellCode, cell);
                minX = Math.Min(minX, cell.Position.X - safeRadius);
                maxX = Math.Max(maxX, cell.Position.X + safeRadius);
                minZ = Math.Min(minZ, cell.Position.Z - safeRadius);
                maxZ = Math.Max(maxZ, cell.Position.Z + safeRadius);
            }
        }
        catch (Exception exception)
        {
            newRoot.Free();
            return MapBuildResult.Failure($"实例化地图格子失败：{exception.Message}");
        }

        Clear(mapRoot);
        mapRoot.AddChild(newRoot);
        _generatedRoot = newRoot;
        _cellsById = newCellsById;
        _cellsByCode = newCellsByCode;
        _edges = map.Edges!.AsReadOnly();
        var bounds = new Aabb(
            new Vector3(minX, -safeHeight, minZ),
            new Vector3(maxX - minX, safeHeight, maxZ - minZ)
        );
        return new MapBuildResult(true, "", _cellsById.Count, _edges.Count, bounds);
    }

    public void Clear(Node3D mapRoot)
    {
        foreach (var child in mapRoot.GetChildren())
        {
            mapRoot.RemoveChild(child);
            child.QueueFree();
        }

        _generatedRoot = null;
        _cellsById = new Dictionary<long, HexCell3D>();
        _cellsByCode = new Dictionary<string, HexCell3D>(StringComparer.Ordinal);
        _edges = Array.Empty<MapEdgeDto>();
    }
}
