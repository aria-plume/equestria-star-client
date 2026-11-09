using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map.VisualRuntime;
using EquestriaStar.Internal.MapBaking;
using EquestriaStar.Internal.MapBaking.Pr1;
using Godot;

public partial class RunStaticMapBakeTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize() => Callable.From(Begin).CallDeferred();

    private void Begin() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            TestRendererConfiguration();
            TestFixtureContract();
            TestStaticScene();
            TestMetadataAndDeterminism();
            TestMapVisualRuntime();
            TestPckLoad();
        }
        catch (Exception exception) { _failures.Add("测试执行异常：" + exception); }
        OrigamiArtCatalog.ReleaseSceneCache();
        EnvironmentArtCatalog.ReleaseSceneCache();
        ConceptPaperStyle.ReleaseAtShutdown();
        MapLayoutConfig.ReleaseAtShutdown();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
        foreach (var failure in _failures) GD.PushError("[StaticMapBakeTests] " + failure);
        GD.Print(_failures.Count == 0 ? "[StaticMapBakeTests] ALL PASSED" : $"[StaticMapBakeTests] FAILED: {_failures.Count}");
        Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void TestRendererConfiguration()
    {
        Assert(ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString() == "forward_plus",
            "PC 默认渲染器必须配置为 Forward+");
        Assert(ProjectSettings.GetSetting("rendering/renderer/rendering_method.mobile").AsString() == "mobile",
            "移动平台覆盖配置必须保持 Mobile");
        var actual = RenderingServer.GetCurrentRenderingMethod();
        GD.Print($"[StaticMapBakeTests] renderer configured=forward_plus actual={actual} display={DisplayServer.GetName()}");
        if (DisplayServer.GetName() != "headless") Assert(actual == "forward_plus", "窗口验证时实际渲染器必须为 Forward+");
    }

    private void TestFixtureContract()
    {
        var envelope = JsonSerializer.Deserialize<ApiEnvelope<GameMapDto>>(
            Godot.FileAccess.GetFileAsString("res://tests/fixtures/main-map-20261024.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert(envelope?.Data != null, "固定 fixture 必须可解析");
        if (envelope?.Data == null) return;
        var validation = MapDataValidator.Validate(envelope.Data);
        Assert(validation.IsValid, "fixture 必须通过 138/385 正式地图契约：" + validation.Error);
        var cells = envelope.Data.Cells!;
        var resolved = cells.Count(dto =>
        {
            var data = new HexCellData { CellName = dto.CellName, CellNameText = dto.CellNameText, CellCode = dto.CellCode };
            return OrigamiArtCatalog.Resolve(data) != null;
        });
        Assert(resolved == 64, $"原版专属美术匹配应为 64，实际 {resolved}");
        Assert(cells.Count - resolved == 74, "其余 74 格应使用原版基础纸板视觉");
    }

    private void TestStaticScene()
    {
        var packed = GD.Load<PackedScene>(StaticMapBaker.ScenePath);
        Assert(packed != null, "静态地图场景必须存在并可加载");
        if (packed == null) return;
        var map = packed.Instantiate<Node3D>(); Root.AddChild(map);
        var anchors = map.GetNodeOrNull<Node3D>("CellAnchors");
        var visuals = map.GetNodeOrNull<Node3D>("BoardVisual/CellVisuals");
        Assert(anchors?.GetChildCount() == 138, "必须有 138 个格子锚点");
        Assert(visuals?.GetChildCount() == 138, "必须有 138 个静态格子视觉");
        if (anchors != null && visuals != null)
        {
            var names = anchors.GetChildren().Select(node => node.Name.ToString()).ToHashSet(StringComparer.Ordinal);
            Assert(names.Count == 138, "锚点 cellCode 必须唯一");
            Assert(!names.Contains("B34") && names.Contains("A10") && names.Contains("B24") && names.Contains("I1"), "关键 cellCode 集合必须正确");
            foreach (var anchor in anchors.GetChildren().OfType<Node3D>())
            {
                var visual = visuals.GetNodeOrNull<Node3D>(anchor.Name.ToString());
                Assert(visual != null && visual.Position.IsEqualApprox(anchor.Position), $"锚点 {anchor.Name} 必须与格子中心一致");
                Assert(!anchor.HasMeta("cell_id"), $"锚点 {anchor.Name} 不得依赖临时 cellId");
                Assert(anchor.GetNodeOrNull<Node3D>("TokenAnchor") != null, $"锚点 {anchor.Name} 必须包含稳定 TokenAnchor");
            }
        }
        Assert(map.GetNodeOrNull("World") != null && map.GetNodeOrNull("BoardVisual/Landmarks") != null && map.GetNodeOrNull("Lighting") != null && map.GetNodeOrNull("BakeMetadata") != null, "静态地图必须具有清晰的世界、版图、灯光和元数据结构");
        Assert(!ContainsRuntime(map), "静态地图不得包含脚本、Area3D 或碰撞体");
        TestP9Lighting(map);
        var metadata = map.GetNode("BakeMetadata");
        Assert(metadata.GetMeta("rendering_method", "").AsString() == "forward_plus", "静态场景必须记录 Forward+ 烘焙方式");
        Assert(metadata.GetMeta("paper_preset", 0).AsInt32() == 9, "静态场景必须记录 P9 配置");
        Assert(metadata.GetMeta("drift_target_count", 0).AsInt32() > 0, "静态场景必须保留云层漂移目标元数据");
        Assert(metadata.GetMeta("hinge_target_count", 0).AsInt32() > 0, "静态场景必须保留植被或旗帜铰链元数据");
        Assert(metadata.GetMeta("flag_hinge_count", 0).AsInt32() > 0, "静态场景必须保留旗帜铰链元数据");
        Assert(metadata.GetMeta("token_anchor_count", 0).AsInt32() == 138, "静态场景必须记录 138 个 TokenAnchor");
        Assert(MapVisualP9Profile.Inspect(map).MateriallessSurfaces == 0, "Forward+ 静态场景不得包含无材质表面");
        var rail = map.GetNodeOrNull("BoardVisual/Railways/ReferenceRailDraft");
        Assert(rail?.GetMeta("contract_status", "").AsString() == "visual_reference_only_not_topology_verified", "原版铁路草案必须保留非契约状态标记");
        map.Free();
    }

    private void TestP9Lighting(Node3D map)
    {
        var world = map.GetNodeOrNull<WorldEnvironment>("Lighting/Environment");
        var sun = map.GetNodeOrNull<DirectionalLight3D>("Lighting/DirectionalLight3D");
        var fill = map.GetNodeOrNull<DirectionalLight3D>("Lighting/PaperBounce");
        Assert(world?.Environment != null, "静态地图必须包含 P9 WorldEnvironment");
        if (world?.Environment != null)
        {
            var environment = world.Environment;
            Assert(environment.SsaoEnabled, "Forward+ 烘焙的 P9 环境必须启用 SSAO");
            Assert(Near(environment.SsaoRadius, 0.30f) && Near(environment.SsaoIntensity, 0.95f)
                && Near(environment.SsaoPower, 1.25f) && Near(environment.SsaoLightAffect, 0.10f), "P9 SSAO 参数必须保持原值");
            Assert(environment.GlowEnabled && Near(environment.GlowIntensity, 0.65f)
                && Near(environment.GlowStrength, 1.05f) && Near(environment.GlowBloom, 0.04f), "P9 Glow 参数必须保持原值");
            Assert(environment.TonemapMode == Godot.Environment.ToneMapper.Filmic
                && Near(environment.TonemapExposure, 0.86f) && Near(environment.TonemapWhite, 6.0f), "P9 色调映射必须保持原值");
        }
        Assert(sun != null && sun.ShadowEnabled && Near(sun.LightEnergy, 1.85f)
            && Near(sun.RotationDegrees.X, -56f) && Near(sun.RotationDegrees.Y, -38f), "P9 主光与阴影参数必须保持原值");
        Assert(fill != null && !fill.ShadowEnabled && Near(fill.LightEnergy, 0.22f)
            && Near(fill.RotationDegrees.X, -55f) && Near(fill.RotationDegrees.Y, 150f), "P9 补光参数必须保持原值");
    }

    private void TestMetadataAndDeterminism()
    {
        var json = File.ReadAllText(ProjectSettings.GlobalizePath(StaticMapBaker.MetadataPath));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert(root.GetProperty("cellCount").GetInt32() == 138 && root.GetProperty("edgeCount").GetInt32() == 385, "烘焙元数据必须记录 138/385");
        Assert(root.GetProperty("visualHash").GetString()?.Length == 64, "烘焙元数据必须包含稳定视觉哈希");
        Assert(root.GetProperty("railStatus").GetString() == "reference_draft_not_topology_verified", "元数据必须标记铁路草案边界");
        Assert(root.GetProperty("renderingMethod").GetString() == "forward_plus" && root.GetProperty("paperPreset").GetInt32() == 9,
            "元数据必须记录 Forward+ 与 P9");
        Assert(root.GetProperty("driftTargetCount").GetInt32() > 0 && root.GetProperty("hingeTargetCount").GetInt32() > 0,
            "元数据必须记录动画目标数量");
        Assert(root.GetProperty("tokenAnchorCount").GetInt32() == 138, "元数据必须记录 138 个 TokenAnchor");
    }

    private void TestMapVisualRuntime()
    {
        var mapScene = GD.Load<PackedScene>(StaticMapBaker.ScenePath);
        var runtimeScene = GD.Load<PackedScene>("res://scenes/game/map/visual_runtime/MapVisualRuntime.tscn");
        Assert(mapScene != null && runtimeScene != null, "静态地图与 MapVisualRuntime 必须可加载");
        if (mapScene == null || runtimeScene == null) return;
        var map = mapScene.Instantiate<Node3D>();
        var runtime = runtimeScene.Instantiate<MapVisualRuntime>();
        Root.AddChild(map);
        Root.AddChild(runtime);
        runtime.AttachMap(map);
        var before = runtime.MotionSnapshot;
        runtime.AdvanceForTesting(0.75);
        var middle = runtime.MotionSnapshot;
        runtime.AdvanceForTesting(0.75);
        var after = runtime.MotionSnapshot;
        Assert(before.DriftTargets > 0 && before.HingeTargets > 0 && before.FlagTargets > 0, "运行层必须识别漂移、植被和旗帜动画目标");
        Assert(before.AnimatedShaderInstances > 0, "运行层必须缓存需要时钟驱动的既有渲染实例");
        Assert(after.TickCount > before.TickCount && after.Clock > before.Clock, "环境动画必须跨帧推进统一时钟");
        Assert(!middle.FirstDriftPosition.IsEqualApprox(after.FirstDriftPosition), "云层或环境漂移必须产生多帧位置变化");
        Assert(!middle.FirstHingeBasis.IsEqualApprox(after.FirstHingeBasis), "植被或旗帜铰链必须产生多帧姿态变化");
        Assert(runtime.Atmosphere?.GradeMaterial != null, "PaperAtmosphere 必须创建 P9 分级材质");
        if (runtime.Atmosphere?.GradeMaterial is { } grade)
        {
            Assert(Near(grade.GetShaderParameter("sun_strength").AsSingle(), 1.0f), "P9 后处理必须启用 sunlit 分支");
            Assert(Near(grade.GetShaderParameter("sun_saturation").AsSingle(), MapVisualP9Profile.GradeSaturation), "P9 饱和度必须保持原值");
            Assert(Near(grade.GetShaderParameter("sun_leak").AsSingle(), MapVisualP9Profile.LeakStrength), "P9 暖色漏光必须保持原值");
        }
        runtime.DetachMap();
        Assert(runtime.MotionSnapshot.DriftTargets == 0 && runtime.MotionSnapshot.HingeTargets == 0, "运行层退出时必须解除动画目标引用");
        runtime.Free();
        map.Free();
    }

    private void TestPckLoad()
    {
        var absolute = ProjectSettings.GlobalizePath(StaticMapBaker.PckPath);
        Assert(File.Exists(absolute) && new FileInfo(absolute).Length > 0, "本地静态地图 PCK 必须存在");
        Assert(ProjectSettings.LoadResourcePack(absolute, true), "PCK 必须能独立挂载");
        var scene = GD.Load<PackedScene>(StaticMapBaker.ScenePath);
        Assert(scene != null, "挂载 PCK 后必须能加载主静态场景");
        var map = scene?.Instantiate<Node3D>();
        if (map != null)
        {
            Assert(map.GetNodeOrNull<Node3D>("CellAnchors")?.GetChildCount() == 138, "PCK 场景必须保留 138 锚点");
            Assert(!ContainsRuntime(map), "PCK 场景不得携带生成器脚本或交互碰撞");
            map.Free();
        }
    }

    private static bool ContainsRuntime(Node node)
    {
        if (node.GetScript().VariantType != Variant.Type.Nil || node is Area3D or CollisionObject3D or CollisionShape3D) return true;
        foreach (var child in node.GetChildren()) if (ContainsRuntime(child)) return true;
        return false;
    }

    private static bool Near(float left, float right) => Mathf.IsEqualApprox(left, right);

    private void Assert(bool condition, string message) { if (!condition) _failures.Add(message); }
}
