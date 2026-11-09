using System;
using System.Collections.Generic;
using System.Linq;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

public sealed record StaticMapCellAnchor(
    string CellCode,
    int Q,
    int R,
    Transform3D CellTransform,
    Transform3D TokenTransform
)
{
    /// <summary>当前棋子以格子中心排列，仅保留烘焙挂点的高度与朝向供未来逐格配置。</summary>
    public Transform3D CenteredTokenTransform => new(
        TokenTransform.Basis,
        new Vector3(0.0f, TokenTransform.Origin.Y, 0.0f)
    );
}

public sealed record StaticMapVisualContract(
    long MapId,
    int CellCount,
    int EdgeCount,
    Aabb VisualBounds,
    IReadOnlyDictionary<string, StaticMapCellAnchor> Anchors,
    int StaticVisualCellCount
);

public sealed record StaticMapContractResult(bool Ok, string Error, StaticMapVisualContract? Contract)
{
    public static StaticMapContractResult Failure(string error) => new(false, error, null);
}

/// <summary>验证服务端正式地图与本地静态视觉资源使用完全相同的拓扑锚点。</summary>
public static class StaticMapVisualContractValidator
{
    public const long ExpectedMapId = 1;

    public static StaticMapContractResult Validate(Node3D staticRoot, GameMapDto map)
    {
        var dataValidation = MapDataValidator.Validate(map);
        if (!dataValidation.IsValid)
        {
            return StaticMapContractResult.Failure(dataValidation.Error);
        }

        if (map.MapId != ExpectedMapId)
        {
            return StaticMapContractResult.Failure($"静态地图只接受 mapId={ExpectedMapId}，服务端返回 {map.MapId}。");
        }

        var metadata = staticRoot.GetNodeOrNull<Node>("BakeMetadata");
        var anchorsRoot = staticRoot.GetNodeOrNull<Node3D>("CellAnchors");
        var cellVisuals = staticRoot.GetNodeOrNull<Node3D>("BoardVisual/CellVisuals");
        if (metadata == null || anchorsRoot == null || cellVisuals == null)
        {
            return StaticMapContractResult.Failure("静态地图缺少 BakeMetadata、CellAnchors 或 CellVisuals。");
        }

        var bakedMapId = metadata.GetMeta("map_id", 0).AsInt64();
        var bakedCellCount = metadata.GetMeta("cell_count", 0).AsInt32();
        var bakedEdgeCount = metadata.GetMeta("edge_count", 0).AsInt32();
        if (bakedMapId != ExpectedMapId || bakedCellCount != MapDataValidator.ExpectedCellCount || bakedEdgeCount != MapDataValidator.ExpectedEdgeCount)
        {
            return StaticMapContractResult.Failure(
                $"静态地图元数据不匹配：mapId={bakedMapId} cells={bakedCellCount} edges={bakedEdgeCount}。"
            );
        }

        var position = metadata.GetMeta("visual_bounds_position", Vector3.Zero).AsVector3();
        var size = metadata.GetMeta("visual_bounds_size", Vector3.Zero).AsVector3();
        if (size.X <= 0.0f || size.Z <= 0.0f)
        {
            return StaticMapContractResult.Failure("静态地图 visualBounds 无效。");
        }

        var serverCells = map.Cells!.ToDictionary(cell => cell.CellCode, StringComparer.Ordinal);
        var anchors = new Dictionary<string, StaticMapCellAnchor>(StringComparer.Ordinal);
        foreach (var child in anchorsRoot.GetChildren())
        {
            if (child is not Node3D anchor)
            {
                return StaticMapContractResult.Failure("CellAnchors 只能包含 Node3D 锚点。");
            }

            var code = anchor.GetMeta("cell_code", anchor.Name.ToString()).AsString();
            if (string.IsNullOrWhiteSpace(code) || !anchors.TryAdd(code, new StaticMapCellAnchor(
                    code,
                    anchor.GetMeta("q", int.MinValue).AsInt32(),
                    anchor.GetMeta("r", int.MinValue).AsInt32(),
                    anchor.Transform,
                    anchor.GetNodeOrNull<Node3D>("TokenAnchor")?.Transform ?? Transform3D.Identity
                )))
            {
                return StaticMapContractResult.Failure($"静态地图存在空或重复锚点：{code}。");
            }

            if (anchor.GetNodeOrNull<Node3D>("TokenAnchor") == null)
            {
                return StaticMapContractResult.Failure($"静态地图锚点 {code} 缺少 TokenAnchor。");
            }
        }

        if (anchors.Count != MapDataValidator.ExpectedCellCount || cellVisuals.GetChildCount() != MapDataValidator.ExpectedCellCount)
        {
            return StaticMapContractResult.Failure(
                $"静态地图数量不匹配：anchors={anchors.Count} visuals={cellVisuals.GetChildCount()}。"
            );
        }

        var missing = serverCells.Keys.Except(anchors.Keys, StringComparer.Ordinal).Order().ToArray();
        var extra = anchors.Keys.Except(serverCells.Keys, StringComparer.Ordinal).Order().ToArray();
        if (missing.Length > 0 || extra.Length > 0)
        {
            return StaticMapContractResult.Failure(
                $"静态地图 cellCode 集合不一致：缺少 [{string.Join(',', missing)}]，多出 [{string.Join(',', extra)}]。"
            );
        }

        foreach (var (code, serverCell) in serverCells)
        {
            var anchor = anchors[code];
            if (serverCell.Q != anchor.Q || serverCell.R != anchor.R)
            {
                return StaticMapContractResult.Failure(
                    $"格子 {code} 的 QR 不一致：服务端 ({serverCell.Q},{serverCell.R})，静态锚点 ({anchor.Q},{anchor.R})。"
                );
            }
        }

        if (CountNodes<WorldEnvironment>(staticRoot) != 1
            || CountNodes<DirectionalLight3D>(staticRoot) != 2
            || staticRoot.GetNodeOrNull<DirectionalLight3D>("Lighting/DirectionalLight3D") == null
            || staticRoot.GetNodeOrNull<DirectionalLight3D>("Lighting/PaperBounce") == null)
        {
            return StaticMapContractResult.Failure("静态地图必须包含唯一 WorldEnvironment 以及 P9 主光和补光。");
        }

        if (CountNodes<CollisionObject3D>(staticRoot) != 0)
        {
            return StaticMapContractResult.Failure("静态视觉 PCK 不得包含碰撞对象。");
        }

        return new StaticMapContractResult(
            true,
            "",
            new StaticMapVisualContract(
                bakedMapId,
                bakedCellCount,
                bakedEdgeCount,
                new Aabb(position, size),
                anchors,
                cellVisuals.GetChildCount()
            )
        );
    }

    private static int CountNodes<T>(Node node) where T : Node
    {
        var count = node is T ? 1 : 0;
        foreach (var child in node.GetChildren())
        {
            count += CountNodes<T>(child);
        }

        return count;
    }
}
