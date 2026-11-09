using System.Diagnostics;
using EquestriaStar.Game.Map.VisualRuntime;
using Godot;

namespace EquestriaStar.Internal.MapBaking;

/// <summary>独立加载 PCK 中的静态地图，不访问 API 或正式场景。</summary>
public partial class StaticMapPckLoader : Node3D
{
    [Export] public string PackPath { get; set; } = "res://artifacts/static-map/official-map-visual.pck";
    [Export] public string ScenePath { get; set; } = "res://assets/maps/official/OfficialMapVisual.tscn";
    [Export] public bool LoadFromPack { get; set; } = true;
    public Node3D? LoadedMap { get; private set; }
    public MapVisualRuntime? VisualRuntime { get; private set; }
    private int _uiTestClicks;

    public override async void _Ready()
    {
        VisualRuntime = GetNodeOrNull<MapVisualRuntime>("MapVisualRuntime");
        GetNodeOrNull<Button>("Overlay/UiInputTestButton")?.Connect(Button.SignalName.Pressed, Callable.From(OnUiInputTestPressed));
        var loadWatch = Stopwatch.StartNew();
        if (LoadFromPack)
        {
            var absolute = ProjectSettings.GlobalizePath(PackPath);
            if (!ProjectSettings.LoadResourcePack(absolute, true)) { Fail("无法加载静态地图 PCK。"); return; }
        }
        if (!TryInstantiateStaticMap()) { Fail("PCK 中缺少或无法实例化 OfficialMapVisual.tscn。"); return; }
        loadWatch.Stop();
        var loadedMap = LoadedMap!;
        var anchors = loadedMap.GetNodeOrNull<Node3D>("CellAnchors")?.GetChildCount() ?? 0;
        if (anchors != 138) { Fail($"静态地图审计失败：anchors={anchors}。"); return; }
        FitCamera(loadedMap);
        VisualRuntime?.AttachMap(loadedMap);
        var renderer = VisualRuntime?.ActualRenderingMethod ?? RenderingServer.GetCurrentRenderingMethod();
        GetNode<Label>("Overlay/StatusPanel/Margin/StatusLabel").Text = LoadFromPack
            ? $"PCK 独立加载成功  锚点：{anchors}  {loadWatch.ElapsedMilliseconds} ms\n渲染器：{renderer} · P9 后处理与环境动画已启用"
            : $"静态烘焙场景加载成功  锚点：{anchors}  {loadWatch.ElapsedMilliseconds} ms\n渲染器：{renderer} · P9 后处理与环境动画已启用";
        GD.Print($"STATIC_MAP_LOAD_OK source={(LoadFromPack ? "pck" : "local")} anchors={anchors} elapsedMs={loadWatch.ElapsedMilliseconds} renderer={renderer}");
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--static-map-smoke") >= 0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CleanupLoadedMap();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
    }

    private void FitCamera(Node3D map)
    {
        var metadata = map.GetNodeOrNull("BakeMetadata");
        if (metadata != null && metadata.HasMeta("visual_bounds_position") && metadata.HasMeta("visual_bounds_size"))
        {
            ApplyCameraPlan(new Aabb(
                metadata.GetMeta("visual_bounds_position").AsVector3(),
                metadata.GetMeta("visual_bounds_size").AsVector3()));
            return;
        }
        var anchors = map.GetNodeOrNull<Node3D>("CellAnchors");
        if (anchors == null || anchors.GetChildCount() == 0) return;
        var bounds = new Aabb(); var initialized = false;
        foreach (var anchor in anchors.GetChildren())
        {
            if (anchor is not Node3D point) continue;
            var position = point.GlobalPosition;
            var aabb = new Aabb(position - new Vector3(.55f, .15f, .55f), new Vector3(1.1f, 3.8f, 1.1f));
            bounds = initialized ? bounds.Merge(aabb) : aabb;
            initialized = true;
        }
        if (!initialized) return;
        // 只用可玩格子边界取景，避免 200×200 海面让地图缩成小点。
        bounds = bounds.Grow(3.0f);
        ApplyCameraPlan(bounds);
    }

    private void ApplyCameraPlan(Aabb bounds)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var plan = Pr1.MapCameraFitter.Calculate(bounds, size.Y > 0 ? size.X / size.Y : 16f / 9f, 1.08f);
        var rig = GetNode<Node3D>("CameraRig"); var camera = rig.GetNode<Camera3D>("Camera3D");
        rig.Position = new Vector3(plan.Target.X, 0, plan.Target.Z);
        camera.Position = plan.CameraPosition - rig.Position; camera.LookAt(plan.Target, Vector3.Up); camera.Size = plan.OrthographicSize;
    }

    private void Fail(string message)
    {
        GetNode<Label>("Overlay/StatusPanel/Margin/StatusLabel").Text = message;
        GD.PushError(message);
    }

    private void CleanupLoadedMap()
    {
        VisualRuntime?.DetachMap();
        if (!GodotObject.IsInstanceValid(LoadedMap)) { LoadedMap = null; return; }
        LoadedMap!.QueueFree();
        LoadedMap = null;
    }

    private bool TryInstantiateStaticMap()
    {
        var scene = GD.Load<PackedScene>(ScenePath);
        if (scene == null) return false;
        LoadedMap = scene.InstantiateOrNull<Node3D>();
        if (LoadedMap == null) return false;
        GetNode<Node3D>("MapRoot").AddChild(LoadedMap);
        return true;
    }

    private void OnUiInputTestPressed()
    {
        _uiTestClicks++;
        GetNode<Label>("Overlay/StatusPanel/Margin/StatusLabel").Text += $"\nUI 输入测试：{_uiTestClicks}";
        GD.Print($"STATIC_MAP_UI_INPUT_OK clicks={_uiTestClicks}");
    }
}
