using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Game.Dice;

public static class DiceMeshFactory
{
    private static readonly Dictionary<(int Size, int Radius, int Segments), ArrayMesh> MeshCache = new();
    private static readonly StandardMaterial3D DefaultMaterial = new()
    {
        AlbedoColor = new Color(0.96f, 0.97f, 0.98f),
        Roughness = 0.4f,
        Metallic = 0.015f,
        CullMode = BaseMaterial3D.CullModeEnum.Back
    };

    public static ArrayMesh GetMesh(float size, float cornerRadius, int roundSegments)
    {
        var safeSize = Mathf.Max(0.2f, size);
        var safeRadius = Mathf.Clamp(cornerRadius, 0.01f, safeSize * 0.24f);
        var safeSegments = Mathf.Clamp(roundSegments, 2, 10);
        var key = (
            Mathf.RoundToInt(safeSize * 10000.0f),
            Mathf.RoundToInt(safeRadius * 10000.0f),
            safeSegments
        );
        if (MeshCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var mesh = BuildMesh(safeSize, safeRadius, safeSegments);
        MeshCache.Add(key, mesh);
        return mesh;
    }

    private static ArrayMesh BuildMesh(float size, float cornerRadius, int roundSegments)
    {
        var half = size * 0.5f;
        var core = half - cornerRadius;
        var coordinates = BuildFaceCoordinates(half, core, roundSegments);
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        AddRoundedFace(vertices, normals, uvs, 0, 1.0f, coordinates, half, core);
        AddRoundedFace(vertices, normals, uvs, 0, -1.0f, coordinates, half, core);
        AddRoundedFace(vertices, normals, uvs, 1, 1.0f, coordinates, half, core);
        AddRoundedFace(vertices, normals, uvs, 1, -1.0f, coordinates, half, core);
        AddRoundedFace(vertices, normals, uvs, 2, 1.0f, coordinates, half, core);
        AddRoundedFace(vertices, normals, uvs, 2, -1.0f, coordinates, half, core);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, DefaultMaterial);
        return mesh;
    }

    private static float[] BuildFaceCoordinates(float half, float core, int roundSegments)
    {
        var coordinates = new List<float> { -half };
        var cornerRadius = half - core;
        for (var index = 1; index < roundSegments; index++)
        {
            coordinates.Add(-half + cornerRadius * index / roundSegments);
        }

        coordinates.Add(-core);
        coordinates.Add(core);
        for (var index = 1; index <= roundSegments; index++)
        {
            coordinates.Add(core + cornerRadius * index / roundSegments);
        }

        return coordinates.ToArray();
    }

    private static void AddRoundedFace(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        int fixedAxis,
        float sign,
        IReadOnlyList<float> coordinates,
        float half,
        float core)
    {
        var maximumIndex = coordinates.Count - 1;
        for (var first = 0; first < maximumIndex; first++)
        {
            for (var second = 0; second < maximumIndex; second++)
            {
                var a = RoundedVertex(fixedAxis, sign, coordinates[first], coordinates[second], half, core);
                var b = RoundedVertex(fixedAxis, sign, coordinates[first + 1], coordinates[second], half, core);
                var c = RoundedVertex(fixedAxis, sign, coordinates[first + 1], coordinates[second + 1], half, core);
                var d = RoundedVertex(fixedAxis, sign, coordinates[first], coordinates[second + 1], half, core);
                AddTriangle(vertices, normals, uvs, a, b, c,
                    new Vector2((float)first / maximumIndex, (float)second / maximumIndex),
                    new Vector2((float)(first + 1) / maximumIndex, (float)second / maximumIndex),
                    new Vector2((float)(first + 1) / maximumIndex, (float)(second + 1) / maximumIndex));
                AddTriangle(vertices, normals, uvs, a, c, d,
                    new Vector2((float)first / maximumIndex, (float)second / maximumIndex),
                    new Vector2((float)(first + 1) / maximumIndex, (float)(second + 1) / maximumIndex),
                    new Vector2((float)first / maximumIndex, (float)(second + 1) / maximumIndex));
            }
        }
    }

    private static (Vector3 Position, Vector3 Normal) RoundedVertex(
        int fixedAxis,
        float sign,
        float first,
        float second,
        float half,
        float core)
    {
        var source = fixedAxis switch
        {
            0 => new Vector3(sign * half, first, second),
            1 => new Vector3(first, sign * half, second),
            _ => new Vector3(first, second, sign * half)
        };
        var nearestCorePoint = new Vector3(
            Mathf.Clamp(source.X, -core, core),
            Mathf.Clamp(source.Y, -core, core),
            Mathf.Clamp(source.Z, -core, core)
        );
        var offset = source - nearestCorePoint;
        var normal = offset.Normalized();
        var position = nearestCorePoint + normal * (half - core);
        return (position, normal);
    }

    private static void AddTriangle(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        (Vector3 Position, Vector3 Normal) a,
        (Vector3 Position, Vector3 Normal) b,
        (Vector3 Position, Vector3 Normal) c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        var geometricNormal = (b.Position - a.Position).Cross(c.Position - a.Position);
        var averageVertexNormal = (a.Normal + b.Normal + c.Normal).Normalized();
        if (geometricNormal.Dot(averageVertexNormal) > 0.0f)
        {
            // Godot 将顺时针三角形视为正面，因此叉积方向应与外法线相反。
            (b, c) = (c, b);
            (uvB, uvC) = (uvC, uvB);
        }

        vertices.Add(a.Position);
        vertices.Add(b.Position);
        vertices.Add(c.Position);
        normals.Add(a.Normal);
        normals.Add(b.Normal);
        normals.Add(c.Normal);
        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
    }
}
