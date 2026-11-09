using System;
using System.Collections.Generic;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

/// <summary>按静态锚点创建仅含输入、高亮、名称和棋子挂点的轻量逻辑格。</summary>
public sealed class StaticMapInteractionBuilder
{
    private Node3D? _generatedRoot;
    private IReadOnlyDictionary<long, HexCell3D> _cellsById = new Dictionary<long, HexCell3D>();
    private IReadOnlyDictionary<string, HexCell3D> _cellsByCode = new Dictionary<string, HexCell3D>(StringComparer.Ordinal);
    private IReadOnlyList<MapEdgeDto> _edges = Array.Empty<MapEdgeDto>();

    public IReadOnlyDictionary<long, HexCell3D> CellsById => _cellsById;
    public IReadOnlyDictionary<string, HexCell3D> CellsByCode => _cellsByCode;
    public IReadOnlyList<MapEdgeDto> Edges => _edges;

    public MapBuildResult Build(
        GameMapDto map,
        StaticMapVisualContract contract,
        Node3D interactionRoot,
        PackedScene cellScene,
        float radius,
        float height,
        float bevelWidth,
        bool showCellNames
    )
    {
        var generated = new Node3D { Name = "GeneratedInteractionCells" };
        var byId = new Dictionary<long, HexCell3D>();
        var byCode = new Dictionary<string, HexCell3D>(StringComparer.Ordinal);
        try
        {
            foreach (var dto in map.Cells!)
            {
                var anchor = contract.Anchors[dto.CellCode];
                var cell = cellScene.Instantiate<HexCell3D>();
                cell.Name = dto.CellCode;
                cell.Transform = anchor.CellTransform;
                cell.ConfigureGeometry(radius, height, bevelWidth);
                cell.ShowCellNames = showCellNames;
                cell.CellNameHeight = height + 0.06f;
                cell.LogicVisualOnly = true;

                var tags = new Godot.Collections.Array<string>();
                foreach (var tag in dto.InteractionTags ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                }

                cell.BindData(new HexCellData
                {
                    CellId = dto.CellId,
                    CellCode = dto.CellCode,
                    BoardCode = dto.BoardCode,
                    CellName = dto.CellName,
                    CellNameText = dto.CellNameText,
                    Q = dto.Q!.Value,
                    R = dto.R!.Value,
                    InteractionTags = tags
                });
                generated.AddChild(cell);
                // 本轮不应用地标避让的水平偏移；多人布局始终围绕所属格中心。
                cell.TokenRoot.Transform = anchor.CenteredTokenTransform;
                var label = cell.GetNode<Label3D>("CellNameLabel");
                label.FontSize = 34;
                label.OutlineSize = 8;
                label.PixelSize = 0.0024f;
                byId.Add(dto.CellId, cell);
                byCode.Add(dto.CellCode, cell);
            }
        }
        catch (Exception exception)
        {
            generated.Free();
            return MapBuildResult.Failure($"创建静态地图交互层失败：{exception.Message}");
        }

        Clear(interactionRoot);
        interactionRoot.AddChild(generated);
        _generatedRoot = generated;
        _cellsById = byId;
        _cellsByCode = byCode;
        _edges = map.Edges!.AsReadOnly();
        return new MapBuildResult(true, "", byId.Count, _edges.Count, contract.VisualBounds);
    }

    public void Clear(Node3D interactionRoot)
    {
        foreach (var child in interactionRoot.GetChildren())
        {
            interactionRoot.RemoveChild(child);
            child.QueueFree();
        }

        _generatedRoot = null;
        _cellsById = new Dictionary<long, HexCell3D>();
        _cellsByCode = new Dictionary<string, HexCell3D>(StringComparer.Ordinal);
        _edges = Array.Empty<MapEdgeDto>();
    }
}
