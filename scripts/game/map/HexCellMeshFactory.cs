using System;
using System.Collections.Generic;
using Godot;

namespace EquestriaStar.Game.Map;

internal sealed class HexCellGeometryResources
{
    public required ArrayMesh BaseMesh { get; init; }
    public required ArrayMesh HighlightMesh { get; init; }
    public required ConvexPolygonShape3D CollisionShape { get; init; }
}

internal static class HexCellMeshFactory
{
    private const int SideCount = 6;
    private static readonly Dictionary<GeometryKey, HexCellGeometryResources> GeometryCache = new();
    private static readonly Dictionary<uint, StandardMaterial3D> HighlightMaterialCache = new();

    public static StandardMaterial3D DefaultTopMaterial { get; } = CreateSurfaceMaterial(new Color("91b7a6"), 0.88f);
    public static StandardMaterial3D DefaultSideMaterial { get; } = CreateSurfaceMaterial(new Color("476d68"), 0.76f);

    public static HexCellGeometryResources GetGeometry(float radius, float height, float bevelWidth)
    {
        var safeRadius = Math.Max(0.05f, radius);
        var safeHeight = Math.Max(0.02f, height);
        var safeBevel = Math.Clamp(bevelWidth, 0.0f, Math.Min(safeRadius * 0.45f, safeHeight * 0.45f));
        var key = GeometryKey.From(safeRadius, safeHeight, safeBevel);
        if (GeometryCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var geometry = new HexCellGeometryResources
        {
            BaseMesh = BuildBaseMesh(safeRadius, safeHeight, safeBevel),
            HighlightMesh = BuildHighlightMesh(safeRadius + 0.025f),
            CollisionShape = BuildCollisionShape(safeRadius, safeHeight)
        };
        GeometryCache.Add(key, geometry);
        return geometry;
    }

    public static StandardMaterial3D GetHighlightMaterial(Color color)
    {
        var key = color.ToAbgr32();
        if (HighlightMaterialCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1.0f,
            RenderPriority = 1
        };
        HighlightMaterialCache.Add(key, material);
        return material;
    }

    private static ArrayMesh BuildBaseMesh(float radius, float height, float bevel)
    {
        var innerRadius = Math.Max(0.01f, radius - bevel);
        var topInner = BuildRing(innerRadius, 0.0f);
        var upperOuter = BuildRing(radius, -bevel);
        var lowerOuter = BuildRing(radius, -height + bevel);
        var bottomInner = BuildRing(innerRadius, -height);
        var mesh = new ArrayMesh();

        var topSurface = new SurfaceTool();
        topSurface.Begin(Mesh.PrimitiveType.Triangles);
        for (var side = 0; side < SideCount; side++)
        {
            var next = (side + 1) % SideCount;
            AddVertex(topSurface, Vector3.Zero, Vector3.Up, TopUv(Vector3.Zero, radius));
            AddVertex(topSurface, topInner[side], Vector3.Up, TopUv(topInner[side], radius));
            AddVertex(topSurface, topInner[next], Vector3.Up, TopUv(topInner[next], radius));

            var bevelNormal = SegmentNormal(side, 1.0f);
            AddQuad(topSurface, topInner[side], topInner[next], upperOuter[side], upperOuter[next], bevelNormal, radius);
        }
        topSurface.Commit(mesh);

        var sideSurface = new SurfaceTool();
        sideSurface.Begin(Mesh.PrimitiveType.Triangles);
        for (var side = 0; side < SideCount; side++)
        {
            var next = (side + 1) % SideCount;
            var sideNormal = SegmentNormal(side, 0.0f);
            AddSideQuad(sideSurface, upperOuter[side], upperOuter[next], lowerOuter[side], lowerOuter[next], sideNormal, side);

            var lowerBevelNormal = SegmentNormal(side, -1.0f);
            AddSideQuad(sideSurface, lowerOuter[side], lowerOuter[next], bottomInner[side], bottomInner[next], lowerBevelNormal, side);

            AddVertex(sideSurface, new Vector3(0.0f, -height, 0.0f), Vector3.Down, TopUv(Vector3.Zero, radius));
            AddVertex(sideSurface, bottomInner[next], Vector3.Down, TopUv(bottomInner[next], radius));
            AddVertex(sideSurface, bottomInner[side], Vector3.Down, TopUv(bottomInner[side], radius));
        }
        sideSurface.Commit(mesh);
        return mesh;
    }

    private static ArrayMesh BuildHighlightMesh(float radius)
    {
        var ring = BuildRing(radius, 0.0f);
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (var side = 0; side < SideCount; side++)
        {
            var next = (side + 1) % SideCount;
            AddVertex(surface, Vector3.Zero, Vector3.Up, TopUv(Vector3.Zero, radius));
            AddVertex(surface, ring[side], Vector3.Up, TopUv(ring[side], radius));
            AddVertex(surface, ring[next], Vector3.Up, TopUv(ring[next], radius));
        }

        return surface.Commit() as ArrayMesh ?? new ArrayMesh();
    }

    private static ConvexPolygonShape3D BuildCollisionShape(float radius, float height)
    {
        var points = new Vector3[SideCount * 2];
        var top = BuildRing(radius, 0.0f);
        var bottom = BuildRing(radius, -height);
        for (var side = 0; side < SideCount; side++)
        {
            points[side] = top[side];
            points[side + SideCount] = bottom[side];
        }

        return new ConvexPolygonShape3D { Points = points };
    }

    private static Vector3[] BuildRing(float radius, float y)
    {
        var ring = new Vector3[SideCount];
        for (var side = 0; side < SideCount; side++)
        {
            var angle = Mathf.DegToRad(-90.0f + side * 60.0f);
            ring[side] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        return ring;
    }

    private static Vector3 SegmentNormal(int side, float vertical)
    {
        var angle = Mathf.DegToRad(-60.0f + side * 60.0f);
        return new Vector3(Mathf.Cos(angle), vertical, Mathf.Sin(angle)).Normalized();
    }

    private static void AddQuad(
        SurfaceTool surface,
        Vector3 innerCurrent,
        Vector3 innerNext,
        Vector3 outerCurrent,
        Vector3 outerNext,
        Vector3 normal,
        float radius)
    {
        AddVertex(surface, innerCurrent, normal, TopUv(innerCurrent, radius));
        AddVertex(surface, outerCurrent, normal, TopUv(outerCurrent, radius));
        AddVertex(surface, innerNext, normal, TopUv(innerNext, radius));
        AddVertex(surface, innerNext, normal, TopUv(innerNext, radius));
        AddVertex(surface, outerCurrent, normal, TopUv(outerCurrent, radius));
        AddVertex(surface, outerNext, normal, TopUv(outerNext, radius));
    }

    private static void AddSideQuad(
        SurfaceTool surface,
        Vector3 topCurrent,
        Vector3 topNext,
        Vector3 bottomCurrent,
        Vector3 bottomNext,
        Vector3 normal,
        int side)
    {
        var u0 = side / (float)SideCount;
        var u1 = (side + 1) / (float)SideCount;
        AddVertex(surface, topCurrent, normal, new Vector2(u0, 0.0f));
        AddVertex(surface, bottomCurrent, normal, new Vector2(u0, 1.0f));
        AddVertex(surface, topNext, normal, new Vector2(u1, 0.0f));
        AddVertex(surface, topNext, normal, new Vector2(u1, 0.0f));
        AddVertex(surface, bottomCurrent, normal, new Vector2(u0, 1.0f));
        AddVertex(surface, bottomNext, normal, new Vector2(u1, 1.0f));
    }

    private static void AddVertex(SurfaceTool surface, Vector3 position, Vector3 normal, Vector2 uv)
    {
        surface.SetNormal(normal);
        surface.SetUV(uv);
        surface.AddVertex(position);
    }

    private static Vector2 TopUv(Vector3 position, float radius)
    {
        var diameter = Math.Max(0.001f, radius * 2.0f);
        return new Vector2(position.X / diameter + 0.5f, position.Z / diameter + 0.5f);
    }

    private static StandardMaterial3D CreateSurfaceMaterial(Color color, float roughness)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = 0.03f
        };
    }

    private readonly record struct GeometryKey(int Radius, int Height, int Bevel)
    {
        public static GeometryKey From(float radius, float height, float bevel)
        {
            return new GeometryKey(Quantize(radius), Quantize(height), Quantize(bevel));
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value * 10000.0f);
        }
    }
}
