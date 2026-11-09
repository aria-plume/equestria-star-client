using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EquestriaStar.Api;
using EquestriaStar.Internal.MapBaking.Pr1;
using Godot;

namespace EquestriaStar.Internal.MapBaking;

public sealed record StaticMapBakeResult(bool Ok, string Error, int CellCount, int AnchorCount, int NodeCount, int MeshCount, int MaterialCount, long ElapsedMilliseconds);

/// <summary>把隔离生成结果转换为不带脚本、碰撞与业务状态的静态地图。</summary>
public static class StaticMapBaker
{
    public const string ScenePath = "res://assets/maps/official/OfficialMapVisual.tscn";
    public const string MetadataPath = "res://assets/maps/official/OfficialMapVisual.metadata.json";
    public const string PckPath = "res://artifacts/static-map/official-map-visual.pck";

    public static StaticMapBakeResult Bake(Node3D stage, Node3D generatedRoot, IReadOnlyDictionary<string, HexCell3D> cells, GameMapDto map, Aabb visualBounds, long elapsedMilliseconds)
    {
        var root = new Node3D { Name = "OfficialMapVisual" };
        var world = new Node3D { Name = "World" };
        var board = new Node3D { Name = "BoardVisual" };
        var cellVisuals = new Node3D { Name = "CellVisuals" };
        var landmarks = new Node3D { Name = "Landmarks" };
        var railways = new Node3D { Name = "Railways" };
        var roads = new Node3D { Name = "Roads" };
        var anchors = new Node3D { Name = "CellAnchors" };
        var lighting = new Node3D { Name = "Lighting" };
        var metadataNode = new Node { Name = "BakeMetadata" };
        root.AddChild(world); root.AddChild(board); root.AddChild(anchors); root.AddChild(lighting); root.AddChild(metadataNode);
        board.AddChild(cellVisuals); board.AddChild(landmarks); board.AddChild(roads); board.AddChild(railways);

        foreach (var pair in cells.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var sourceCell = pair.Value;
            var cellRoot = new Node3D { Name = pair.Key, Transform = sourceCell.Transform };
            var visual = sourceCell.GetNode<Node3D>("VisualRoot").Duplicate() as Node3D;
            if (visual != null) { visual.Name = "Visual"; StripRuntime(visual); cellRoot.AddChild(visual); }
            cellVisuals.AddChild(cellRoot);
            var anchor = new Node3D { Name = pair.Key, Transform = sourceCell.Transform };
            anchor.SetMeta("cell_code", pair.Key);
            anchor.SetMeta("q", sourceCell.Data?.Q ?? 0);
            anchor.SetMeta("r", sourceCell.Data?.R ?? 0);
            var sourceTokenRoot = sourceCell.GetNodeOrNull<Node3D>("TokenRoot");
            var tokenAnchor = new Node3D
            {
                Name = "TokenAnchor",
                Transform = sourceTokenRoot?.Transform ?? Transform3D.Identity
            };
            anchor.AddChild(tokenAnchor);
            anchors.AddChild(anchor);
        }

        foreach (var child in generatedRoot.GetChildren().OfType<Node3D>())
        {
            if (cells.Values.Contains(child)) continue;
            var duplicate = child.Duplicate() as Node3D;
            if (duplicate == null) continue;
            StripRuntime(duplicate);
            if (child.Name == "ContinentalScenery") world.AddChild(duplicate);
            else if (child.Name.ToString().Contains("Rail", StringComparison.OrdinalIgnoreCase)) railways.AddChild(duplicate);
            else roads.AddChild(duplicate);
        }

        DuplicateIfPresent<WorldEnvironment>(stage, "Environment", lighting);
        DuplicateIfPresent<DirectionalLight3D>(stage, "DirectionalLight3D", lighting);
        DuplicateIfPresent<DirectionalLight3D>(stage, "PaperBounce", lighting);
        var (explicitDefaultMaterialSurfaces, clearedConflictingSurfaceOverrides) = EnsureMaterialCompleteness(root);

        var fixture = "res://tests/fixtures/main-map-20261024.json";
        var sourceHash = Sha256(ProjectSettings.GlobalizePath(fixture));
        var visualHash = ComputeVisualHash(cells);
        metadataNode.SetMeta("map_id", map.MapId);
        var renderer = RenderingServer.GetCurrentRenderingMethod();
        var driftCount = CountMetadata(root, "source_drift_amplitude");
        var hingeCount = CountMetadata(root, "paper_wind_hinge");
        var flagHingeCount = CountMetadata(root, "paper_wind_flag");
        metadataNode.SetMeta("resource_version", "pr1-static-v2-forward-plus");
        metadataNode.SetMeta("cell_count", map.Cells?.Count ?? 0);
        metadataNode.SetMeta("edge_count", map.Edges?.Count ?? 0);
        metadataNode.SetMeta("token_anchor_count", anchors.GetChildCount());
        metadataNode.SetMeta("fixture_sha256", sourceHash);
        metadataNode.SetMeta("visual_hash", visualHash);
        metadataNode.SetMeta("visual_bounds_position", visualBounds.Position);
        metadataNode.SetMeta("visual_bounds_size", visualBounds.Size);
        metadataNode.SetMeta("rail_status", "reference_draft_not_topology_verified");
        metadataNode.SetMeta("rendering_method", renderer);
        metadataNode.SetMeta("paper_preset", 9);
        metadataNode.SetMeta("cell_diameter", 1.1f);
        metadataNode.SetMeta("motion_schema_version", 1);
        metadataNode.SetMeta("drift_target_count", driftCount);
        metadataNode.SetMeta("hinge_target_count", hingeCount);
        metadataNode.SetMeta("flag_hinge_count", flagHingeCount);
        metadataNode.SetMeta("explicit_default_material_surfaces", explicitDefaultMaterialSurfaces);
        metadataNode.SetMeta("cleared_conflicting_surface_overrides", clearedConflictingSurfaceOverrides);
        metadataNode.SetMeta("baked_utc", DateTime.UtcNow.ToString("O"));

        SetOwnerRecursive(root, root);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://assets/maps/official"));
        var packed = new PackedScene();
        var packError = packed.Pack(root);
        if (packError != Error.Ok) return new(false, $"静态场景打包失败：{packError}", 0, 0, 0, 0, 0, elapsedMilliseconds);
        var saveError = ResourceSaver.Save(packed, ScenePath);
        if (saveError != Error.Ok) return new(false, $"静态场景保存失败：{saveError}", 0, 0, 0, 0, 0, elapsedMilliseconds);

        var stats = Count(root);
        var metadata = new
        {
            schemaVersion = 2, mapId = map.MapId, mapName = map.MapName, resourceVersion = "pr1-static-v2-forward-plus",
            cellCount = map.Cells?.Count ?? 0, edgeCount = map.Edges?.Count ?? 0,
            fixture, fixtureSha256 = sourceHash, visualHash,
            bakedUtc = DateTime.UtcNow.ToString("O"), tool = "EquestriaStar.Internal.MapBaking.StaticMapBaker",
            railStatus = "reference_draft_not_topology_verified",
            renderingMethod = renderer, paperPreset = 9, cellDiameter = 1.1f,
            motionSchemaVersion = 1, driftTargetCount = driftCount, hingeTargetCount = hingeCount, flagHingeCount,
            tokenAnchorCount = anchors.GetChildCount(),
            explicitDefaultMaterialSurfaces, clearedConflictingSurfaceOverrides,
            visualBoundsPosition = new[] { visualBounds.Position.X, visualBounds.Position.Y, visualBounds.Position.Z },
            visualBoundsSize = new[] { visualBounds.Size.X, visualBounds.Size.Y, visualBounds.Size.Z },
            nodeCount = stats.Nodes, meshCount = stats.Meshes, materialCount = stats.Materials,
            elapsedMilliseconds
        };
        File.WriteAllText(ProjectSettings.GlobalizePath(MetadataPath), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        var anchorCount = anchors.GetChildCount();
        root.Free();
        return new(true, "", map.Cells?.Count ?? 0, anchorCount, stats.Nodes, stats.Meshes, stats.Materials, elapsedMilliseconds);
    }

    private static void DuplicateIfPresent<T>(Node stage, string path, Node parent) where T : Node
    {
        var source = stage.GetNodeOrNull<T>(path);
        if (source?.Duplicate() is not T duplicate) return;
        StripRuntime(duplicate);
        parent.AddChild(duplicate);
    }

    private static void StripRuntime(Node node)
    {
        foreach (var child in node.GetChildren().ToArray())
        {
            if (child is Area3D or CollisionObject3D || child.GetScript().VariantType != Variant.Type.Nil)
            {
                node.RemoveChild(child); child.Free(); continue;
            }
            StripRuntime(child);
        }
        node.ProcessMode = Node.ProcessModeEnum.Disabled;
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }

    private static (int Nodes, int Meshes, int Materials) Count(Node root)
    {
        var nodes = 1; var meshes = 0; var materialIds = new HashSet<ulong>();
        void Visit(Node node)
        {
            foreach (var child in node.GetChildren()) { nodes++; Visit(child); }
            if (node is not MeshInstance3D instance || instance.Mesh == null) return;
            meshes++;
            for (var surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
            {
                var material = instance.GetActiveMaterial(surface);
                if (material != null) materialIds.Add(material.GetInstanceId());
            }
        }
        Visit(root);
        return (nodes, meshes, materialIds.Count);
    }

    private static int CountMetadata(Node root, string key)
    {
        var count = root.HasMeta(key) ? 1 : 0;
        foreach (var child in root.GetChildren()) count += CountMetadata(child, key);
        return count;
    }

    private static (int DefaultMaterials, int ClearedConflictingSurfaceOverrides) EnsureMaterialCompleteness(Node root)
    {
        var fallback = new StandardMaterial3D
        {
            ResourceName = "StaticMapDefaultPaper",
            AlbedoColor = Colors.White,
            Roughness = 1.0f,
            Metallic = 0.0f
        };
        var defaults = 0;
        var clearedOverrides = 0;
        void Visit(Node node)
        {
            if (node is MeshInstance3D mesh && mesh.Mesh != null)
            {
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                {
                    // Godot Forward+ 在同一实例同时存在全局材质覆盖和表面覆盖时，
                    // 变换更新会触发空材质依赖错误。全局覆盖本来就优先，清理冗余表面覆盖不改变外观。
                    if (mesh.MaterialOverride != null && mesh.GetSurfaceOverrideMaterial(surface) != null)
                    {
                        mesh.SetSurfaceOverrideMaterial(surface, null);
                        clearedOverrides++;
                    }
                    var active = mesh.GetActiveMaterial(surface);
                    if (active == null)
                    {
                        mesh.Mesh.SurfaceSetMaterial(surface, fallback);
                        active = fallback;
                        defaults++;
                    }
                    if (mesh.Mesh.SurfaceGetMaterial(surface) == null)
                    {
                        mesh.Mesh.SurfaceSetMaterial(surface, active);
                        defaults++;
                    }
                }
            }
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root);
        return (defaults, clearedOverrides);
    }

    private static string ComputeVisualHash(IReadOnlyDictionary<string, HexCell3D> cells)
    {
        var text = string.Join("\n", cells.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value.Position.X:F5},{pair.Value.Position.Y:F5},{pair.Value.Position.Z:F5}:{OrigamiArtCatalog.Resolve(pair.Value.Data!)?.Id ?? "base"}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
