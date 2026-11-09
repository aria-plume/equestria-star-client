using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

public sealed record StaticMapLoadResult(bool Ok, string Error, Node3D? Instance, StaticMapVisualContract? Contract)
{
    public static StaticMapLoadResult Failure(string error) => new(false, error, null, null);
}

/// <summary>进程内只挂载一次正式静态地图 PCK，并为每次页面加载创建独立场景实例。</summary>
public static class OfficialMapStaticVisualLoader
{
    public const string PackPath = "res://artifacts/static-map/official-map-visual.pck";
    public const string ScenePath = "res://assets/maps/official/OfficialMapVisual.tscn";

    private static bool _packMounted;
    public static int MountCount { get; private set; }

    public static StaticMapLoadResult Load(GameMapDto map)
    {
        if (!EnsurePackMounted(out var mountError))
        {
            return StaticMapLoadResult.Failure(mountError);
        }

        var packed = ResourceLoader.Load<PackedScene>(ScenePath);
        if (packed == null)
        {
            return StaticMapLoadResult.Failure($"无法从静态地图 PCK 加载场景：{ScenePath}");
        }

        Node3D? instance = null;
        try
        {
            instance = packed.Instantiate<Node3D>();
            var validation = StaticMapVisualContractValidator.Validate(instance, map);
            if (!validation.Ok)
            {
                instance.Free();
                return StaticMapLoadResult.Failure(validation.Error);
            }

            return new StaticMapLoadResult(true, "", instance, validation.Contract);
        }
        catch (System.Exception exception)
        {
            instance?.Free();
            return StaticMapLoadResult.Failure($"实例化静态地图失败：{exception.Message}");
        }
    }

    private static bool EnsurePackMounted(out string error)
    {
        error = "";
        if (_packMounted)
        {
            return true;
        }

        var absolutePath = ProjectSettings.GlobalizePath(PackPath);
        if (!FileAccess.FileExists(absolutePath))
        {
            error = $"正式静态地图 PCK 不存在：{PackPath}";
            return false;
        }

        if (!ProjectSettings.LoadResourcePack(absolutePath, true))
        {
            error = $"正式静态地图 PCK 挂载失败：{PackPath}";
            return false;
        }

        _packMounted = true;
        MountCount++;
        GD.Print($"STATIC_MAP_PCK_MOUNTED path={PackPath}");
        return true;
    }
}
