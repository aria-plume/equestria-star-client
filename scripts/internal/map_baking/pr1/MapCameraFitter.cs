using System;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

public readonly record struct MapCameraFitPlan(Vector3 Target, Vector3 CameraPosition, float OrthographicSize);

public static class MapCameraFitter
{
    private static readonly Vector3 ViewOffsetDirection = new Vector3(0.14f, 1.05f, 1.22f).Normalized();

    public static MapCameraFitPlan Calculate(Aabb bounds, float viewportAspect, float padding = 1.12f)
    {
        var target = bounds.GetCenter();
        var distance = Math.Max(12.0f, Math.Max(bounds.Size.X, bounds.Size.Z) * 1.65f);
        var cameraPosition = target + ViewOffsetDirection * distance;
        var viewDirection = (target - cameraPosition).Normalized();
        var right = viewDirection.Cross(Vector3.Up).Normalized();
        var screenUp = right.Cross(viewDirection).Normalized();
        var halfWidth = 0.0f;
        var halfHeight = 0.0f;
        foreach (var corner in Corners(bounds))
        {
            var relative = corner - target;
            halfWidth = Math.Max(halfWidth, Math.Abs(relative.Dot(right)));
            halfHeight = Math.Max(halfHeight, Math.Abs(relative.Dot(screenUp)));
        }
        var aspect = Math.Max(0.1f, viewportAspect);
        var size = Math.Max(halfHeight * 2.0f, halfWidth * 2.0f / aspect) * Math.Max(1.0f, padding);
        return new MapCameraFitPlan(target, cameraPosition, size);
    }

    private static Vector3[] Corners(Aabb bounds)
    {
        var min = bounds.Position;
        var max = bounds.End;
        return
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z)
        ];
    }
}
