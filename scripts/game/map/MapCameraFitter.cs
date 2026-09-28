using System;
using Godot;

namespace EquestriaStar.Game.Map;

public readonly record struct MapCameraFitPlan(
    Vector3 Target,
    Vector3 CameraPosition,
    float OrthographicSize,
    float ProjectedWidth,
    float ProjectedHeight
);

public static class MapCameraFitter
{
    private static readonly Vector3 ViewOffsetDirection = new Vector3(0.0f, 1.35f, 0.92f).Normalized();

    public static MapCameraFitPlan Calculate(Aabb bounds, float viewportAspect, float padding = 1.12f)
    {
        var safeAspect = Math.Max(0.1f, viewportAspect);
        var safePadding = Math.Max(1.0f, padding);
        var target = bounds.GetCenter();
        var distance = Math.Max(12.0f, Math.Max(bounds.Size.X, bounds.Size.Z) * 1.65f);
        var cameraPosition = target + ViewOffsetDirection * distance;
        var viewDirection = (target - cameraPosition).Normalized();
        var right = viewDirection.Cross(Vector3.Up).Normalized();
        var screenUp = right.Cross(viewDirection).Normalized();

        var halfWidth = 0.0f;
        var halfHeight = 0.0f;
        foreach (var corner in GetCorners(bounds))
        {
            var relative = corner - target;
            halfWidth = Math.Max(halfWidth, Math.Abs(relative.Dot(right)));
            halfHeight = Math.Max(halfHeight, Math.Abs(relative.Dot(screenUp)));
        }

        var projectedWidth = halfWidth * 2.0f;
        var projectedHeight = halfHeight * 2.0f;
        var orthographicSize = Math.Max(projectedHeight, projectedWidth / safeAspect) * safePadding;
        return new MapCameraFitPlan(target, cameraPosition, orthographicSize, projectedWidth, projectedHeight);
    }

    public static bool ContainsBounds(MapCameraFitPlan plan, float viewportAspect, float tolerance = 0.001f)
    {
        var safeAspect = Math.Max(0.1f, viewportAspect);
        return plan.OrthographicSize + tolerance >= plan.ProjectedHeight
            && plan.OrthographicSize * safeAspect + tolerance >= plan.ProjectedWidth;
    }

    private static Vector3[] GetCorners(Aabb bounds)
    {
        var min = bounds.Position;
        var max = bounds.End;
        return
        [
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z)
        ];
    }
}
