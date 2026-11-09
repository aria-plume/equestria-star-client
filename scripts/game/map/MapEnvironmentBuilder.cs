using Godot;
using System;

namespace EquestriaStar.Game.Map;

public sealed record MapEnvironmentBuildResult(
    bool Ok,
    string Error,
    int ModuleCount,
    int SceneInstanceCount,
    int MissingAssetCount
)
{
    public static MapEnvironmentBuildResult Failure(string error)
    {
        return new MapEnvironmentBuildResult(false, error, 0, 0, 0);
    }
}

/// <summary>
/// 根据可玩地图边界构建独立环境层。环境不参与地图碰撞、输入和摄像机边界计算。
/// </summary>
public sealed class MapEnvironmentBuilder
{
    private const float EnvironmentScaleReference = 14.0f;
    private const float PlayableBoundsClearance = 0.28f;

    private readonly EnvironmentArtCatalog _catalog = new();
    private Node3D? _generatedRoot;
    private StandardMaterial3D? _groundMaterial;
    private StandardMaterial3D? _waterMaterial;

    public int ModuleCount { get; private set; }
    public int SceneInstanceCount { get; private set; }
    public int MissingAssetCount { get; private set; }
    public int CachedSceneCount => _catalog.CachedSceneCount;
    public int ResourceLoadCount => _catalog.ResourceLoadCount;

    public MapEnvironmentBuildResult Build(
        Aabb mapBounds,
        Node3D environmentRoot,
        MeshInstance3D groundPlane
    )
    {
        if (!_catalog.EnsureLoaded())
        {
            return MapEnvironmentBuildResult.Failure(_catalog.LastError);
        }

        Clear(environmentRoot);
        ConfigureGround(mapBounds, groundPlane);

        var newRoot = new Node3D { Name = "GeneratedEnvironment" };
        var moduleCount = 0;
        var sceneInstanceCount = 0;
        var missingAssetCount = 0;
        try
        {
            AddWaterPlane(mapBounds, newRoot);
            var center = mapBounds.GetCenter();
            var mapScale = Math.Max(
                0.25f,
                Math.Min(mapBounds.Size.X, mapBounds.Size.Z) / EnvironmentScaleReference
            );
            var groundY = mapBounds.Position.Y - 0.02f;

            foreach (var placement in _catalog.Placements)
            {
                if (!_catalog.TryGetAsset(placement.AssetId, out var asset))
                {
                    missingAssetCount++;
                    continue;
                }

                var scenes = _catalog.LoadScenes(asset);
                if (scenes.Count == 0)
                {
                    missingAssetCount++;
                    continue;
                }

                var moduleRoot = new Node3D { Name = $"{asset.AssetId}_{moduleCount + 1:00}" };

                foreach (var scene in scenes)
                {
                    var instance = scene.Instantiate();
                    DisableEnvironmentInteraction(instance);
                    moduleRoot.AddChild(instance);
                    sceneInstanceCount++;
                }

                if (!TryGetVisualBounds(moduleRoot, out var visualBounds))
                {
                    moduleRoot.Free();
                    missingAssetCount++;
                    continue;
                }

                var uniformScale = Math.Max(0.01f, placement.Scale * mapScale);
                moduleRoot.RotationDegrees = new Vector3(0.0f, placement.RotationYDegrees, 0.0f);
                moduleRoot.Scale = Vector3.One * uniformScale;
                moduleRoot.Position = new Vector3(
                    center.X + placement.NormalizedX * mapBounds.Size.X,
                    groundY + placement.YOffset - visualBounds.Position.Y * uniformScale,
                    center.Z + placement.NormalizedZ * mapBounds.Size.Z
                );
                MoveOutsidePlayableBounds(moduleRoot, visualBounds, mapBounds);

                newRoot.AddChild(moduleRoot);
                moduleCount++;
            }
        }
        catch (Exception exception)
        {
            newRoot.Free();
            return MapEnvironmentBuildResult.Failure($"构建地图环境失败：{exception.Message}");
        }

        environmentRoot.AddChild(newRoot);
        _generatedRoot = newRoot;
        ModuleCount = moduleCount;
        SceneInstanceCount = sceneInstanceCount;
        MissingAssetCount = missingAssetCount;
        return new MapEnvironmentBuildResult(true, "", moduleCount, sceneInstanceCount, missingAssetCount);
    }

    public void Clear(Node3D environmentRoot)
    {
        if (GodotObject.IsInstanceValid(_generatedRoot) && _generatedRoot!.GetParent() == environmentRoot)
        {
            environmentRoot.RemoveChild(_generatedRoot);
            _generatedRoot.QueueFree();
        }

        _generatedRoot = null;
        ModuleCount = 0;
        SceneInstanceCount = 0;
        MissingAssetCount = 0;
    }

    private void ConfigureGround(Aabb bounds, MeshInstance3D groundPlane)
    {
        groundPlane.Position = new Vector3(bounds.GetCenter().X, bounds.Position.Y - 0.02f, bounds.GetCenter().Z);
        if (groundPlane.Mesh is PlaneMesh plane)
        {
            plane.Size = new Vector2(
                bounds.Size.X + _catalog.GroundPadding * 2.0f,
                bounds.Size.Z + _catalog.GroundPadding * 2.0f
            );
        }

        _groundMaterial ??= CreatePaperMaterial(_catalog.GroundColor, 0.94f);
        groundPlane.MaterialOverride = _groundMaterial;
    }

    private void AddWaterPlane(Aabb bounds, Node3D root)
    {
        _waterMaterial ??= CreatePaperMaterial(_catalog.WaterColor, 0.88f);
        var water = new MeshInstance3D
        {
            Name = "WaterBackground",
            Position = new Vector3(bounds.GetCenter().X, bounds.Position.Y - 0.055f, bounds.GetCenter().Z),
            Mesh = new PlaneMesh
            {
                Size = new Vector2(
                    bounds.Size.X + _catalog.WaterPadding * 2.0f,
                    bounds.Size.Z + _catalog.WaterPadding * 2.0f
                ),
                Material = _waterMaterial
            }
        };
        root.AddChild(water);
    }

    private static StandardMaterial3D CreatePaperMaterial(string colorValue, float roughness)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(colorValue),
            Roughness = roughness,
            Metallic = 0.0f
        };
    }

    private static bool TryGetVisualBounds(Node3D root, out Aabb bounds)
    {
        bounds = default;
        var hasBounds = false;
        CollectVisualBounds(root, Transform3D.Identity, ref bounds, ref hasBounds);
        return hasBounds;
    }

    private static void CollectVisualBounds(
        Node node,
        Transform3D parentTransform,
        ref Aabb bounds,
        ref bool hasBounds
    )
    {
        var transform = parentTransform;
        if (node is Node3D node3D)
        {
            transform = parentTransform * node3D.Transform;
        }

        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            var transformedBounds = TransformBounds(meshInstance.GetAabb(), transform);
            bounds = hasBounds ? bounds.Merge(transformedBounds) : transformedBounds;
            hasBounds = true;
        }

        foreach (var child in node.GetChildren())
        {
            CollectVisualBounds(child, transform, ref bounds, ref hasBounds);
        }
    }

    private static Aabb TransformBounds(Aabb bounds, Transform3D transform)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (var x = 0; x <= 1; x++)
        {
            for (var y = 0; y <= 1; y++)
            {
                for (var z = 0; z <= 1; z++)
                {
                    var corner = bounds.Position + new Vector3(
                        bounds.Size.X * x,
                        bounds.Size.Y * y,
                        bounds.Size.Z * z
                    );
                    var transformed = transform * corner;
                    minimum = minimum.Min(transformed);
                    maximum = maximum.Max(transformed);
                }
            }
        }

        return new Aabb(minimum, maximum - minimum);
    }

    private static void MoveOutsidePlayableBounds(Node3D moduleRoot, Aabb localBounds, Aabb mapBounds)
    {
        var visualBounds = TransformBounds(localBounds, moduleRoot.Transform);
        var playableMinX = mapBounds.Position.X - PlayableBoundsClearance;
        var playableMaxX = mapBounds.End.X + PlayableBoundsClearance;
        var playableMinZ = mapBounds.Position.Z - PlayableBoundsClearance;
        var playableMaxZ = mapBounds.End.Z + PlayableBoundsClearance;
        if (visualBounds.End.X <= playableMinX
            || visualBounds.Position.X >= playableMaxX
            || visualBounds.End.Z <= playableMinZ
            || visualBounds.Position.Z >= playableMaxZ)
        {
            return;
        }

        var direction = new Vector2(
            moduleRoot.Position.X - mapBounds.GetCenter().X,
            moduleRoot.Position.Z - mapBounds.GetCenter().Z
        );
        if (direction.IsZeroApprox())
        {
            direction = Vector2.Down;
        }

        var position = moduleRoot.Position;
        if (Math.Abs(direction.X) >= Math.Abs(direction.Y))
        {
            position.X += direction.X < 0.0f
                ? playableMinX - visualBounds.End.X
                : playableMaxX - visualBounds.Position.X;
        }
        else
        {
            position.Z += direction.Y < 0.0f
                ? playableMinZ - visualBounds.End.Z
                : playableMaxZ - visualBounds.Position.Z;
        }

        moduleRoot.Position = position;
    }

    private static void DisableEnvironmentInteraction(Node node)
    {
        node.ProcessMode = Node.ProcessModeEnum.Disabled;
        if (node.IsInGroup(MapCameraController.MapInputBlockerGroup))
        {
            node.RemoveFromGroup(MapCameraController.MapInputBlockerGroup);
        }

        if (node is CollisionObject3D collisionObject)
        {
            collisionObject.CollisionLayer = 0;
            collisionObject.CollisionMask = 0;
            collisionObject.InputRayPickable = false;
        }

        if (node is CollisionShape3D collisionShape)
        {
            collisionShape.Disabled = true;
        }

        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        foreach (var child in node.GetChildren())
        {
            DisableEnvironmentInteraction(child);
        }
    }
}
