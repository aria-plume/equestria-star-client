using System;
using System.Collections.Generic;
using System.Linq;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

public sealed record Pr1MapBuildResult(bool Ok, string Error, int CellCount, int EdgeCount, Aabb PlayableBounds, Aabb VisualBounds);

/// <summary>PR #1 视觉系统的隔离复刻器，只消费固定地图 DTO。</summary>
public sealed class Pr1VisualMapBuilder
{
    private readonly Dictionary<string, HexCell3D> _cellsByCode = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, HexCell3D> CellsByCode => _cellsByCode;

    public Pr1MapBuildResult Build(GameMapDto map, Node3D mapRoot, PackedScene cellScene, float radius, float height, float bevelWidth)
    {
        var validation = MapDataValidator.Validate(map);
        if (!validation.IsValid) return new(false, validation.Error, 0, 0, default, default);
        var generated = new Node3D { Name = "GeneratedVisualMap" };
        // 原版样式会查询 GlobalTransform，因此生成期根节点必须已进入场景树。
        mapRoot.AddChild(generated);
        var cells = new List<HexCell3D>();
        var minX = float.MaxValue; var maxX = float.MinValue;
        var minZ = float.MaxValue; var maxZ = float.MinValue;
        var maxHeight = 0.0f;
        try
        {
            foreach (var dto in map.Cells!)
            {
                var cell = cellScene.Instantiate<HexCell3D>();
                cell.Name = dto.CellCode;
                cell.Radius = radius;
                cell.Height = height;
                cell.BevelWidth = bevelWidth;
                cell.TopMaterial = ConceptPaperStyle.Ground(dto.BoardCode);
                cell.SideMaterial = MapLayoutConfig.GetSideMaterial(dto.BoardCode);
                cell.ShowDebugLabel = false;
                cell.Position = HexCoordinateConverter.ToWorld(dto.Q!.Value, dto.R!.Value, dto.BoardCode, radius);
                var tags = new Godot.Collections.Array<string>();
                foreach (var tag in dto.InteractionTags ?? []) if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                cell.BindData(new HexCellData
                {
                    CellId = dto.CellId, CellCode = dto.CellCode, BoardCode = dto.BoardCode,
                    CellName = dto.CellName, CellNameText = dto.CellNameText,
                    Q = dto.Q.Value, R = dto.R.Value, InteractionTags = tags
                });
                var art = OrigamiArtCatalog.Resolve(cell.Data!);
                if (art != null)
                {
                    cell.CustomVisualYawDegrees = OrigamiMapPresentation.Orientation(art, cell.Data!);
                    maxHeight = Math.Max(maxHeight, OrigamiArtCatalog.Apply(cell, art));
                }
                generated.AddChild(cell);
                cells.Add(cell);
                _cellsByCode.Add(dto.CellCode, cell);
                minX = Math.Min(minX, cell.Position.X - radius); maxX = Math.Max(maxX, cell.Position.X + radius);
                minZ = Math.Min(minZ, cell.Position.Z - radius); maxZ = Math.Max(maxZ, cell.Position.Z + radius);
            }

            foreach (var cell in cells)
            {
                var art = OrigamiArtCatalog.Resolve(cell.Data!);
                OrigamiMapPresentation.Decorate(cell, art, deferNearDetails: false);
                PawnArtLayout.Apply(cell, art);
                PaperFoldGeometry.Apply(cell.GetNode<Node3D>("VisualRoot"));
                if (ArtRenderProfile.IsFairy)
                {
                    // 烘焙期只生成稳定铰链和元数据；运行时动画脚本不会进入静态 PCK。
                    PaperFoldGeometry.PrepareWind(cell.GetNode<Node3D>("VisualRoot"));
                    PaperFlagMotion.Prepare(cell.GetNode<Node3D>("VisualRoot"));
                }
                PaperGroundContact.Apply(cell);
                ConceptPaperStyle.ApplyCell(cell);
                ConceptPaperStyle.Apply(cell.GetNode<Node3D>("VisualRoot/DecorationRoot"));
            }

            var playable = new Aabb(new Vector3(minX, -height, minZ), new Vector3(maxX - minX, height + maxHeight, maxZ - minZ));
            var visual = OrigamiMapPresentation.AddContinent(generated, cells, playable, radius, map.Edges);
            var scenery = generated.GetNodeOrNull<Node3D>("ContinentalScenery");
            if (scenery != null) ConceptPaperStyle.Apply(scenery, scenery: true);
            PaperPlaceComposition.Build(generated, cells.ToArray());
            ReferenceRailDraft.Add(generated, _cellsByCode);
            var rail = generated.GetNodeOrNull<Node3D>("ReferenceRailDraft");
            if (rail != null) rail.SetMeta("contract_status", "visual_reference_only_not_topology_verified");
            var shadowCount = 0;
            foreach (var cell in cells)
            {
                var imported = cell.GetNodeOrNull<Node3D>("VisualRoot/CustomVisualRoot/OrigamiAsset/Decoration");
                var added = cell.GetNodeOrNull<Node3D>("VisualRoot/DecorationRoot/LocalVegetation");
                if (imported != null) shadowCount += SceneryShadowBudget.Apply(imported, radius);
                if (added != null) shadowCount += SceneryShadowBudget.Apply(added, radius);
            }
            generated.SetMeta("tiny_shadow_casters_disabled", shadowCount);
            generated.SetMeta("source_fixture", "res://tests/fixtures/main-map-20261024.json");
            generated.SetMeta("deterministic_seed", "fnv1a-cell-code");
            return new(true, "", cells.Count, map.Edges!.Count, playable, visual);
        }
        catch (Exception exception)
        {
            if (GodotObject.IsInstanceValid(generated)) generated.QueueFree();
            _cellsByCode.Clear();
            return new(false, $"隔离视觉地图生成失败：{exception.Message}", 0, 0, default, default);
        }
    }
}
