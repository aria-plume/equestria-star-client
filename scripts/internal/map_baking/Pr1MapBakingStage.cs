using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map.VisualRuntime;
using EquestriaStar.Internal.MapBaking.Pr1;
using Godot;

namespace EquestriaStar.Internal.MapBaking;

/// <summary>可独立运行的 PR #1 地图视觉复刻与静态烘焙工具。</summary>
public partial class Pr1MapBakingStage : Node3D
{
    [Export] public string FixturePath { get; set; } = "res://tests/fixtures/main-map-20261024.json";
    [Export] public PackedScene? CellScene { get; set; }
    [Export] public float CellRadius { get; set; } = 0.55f;
    [Export] public float CellHeight { get; set; } = 0.11f;
    [Export] public float CellBevelWidth { get; set; } = 0.025f;
    [Export] public int PaperPreset { get; set; } = 9;

    public Pr1MapBuildResult? LastBuildResult { get; private set; }
    public StaticMapBakeResult? LastBakeResult { get; private set; }
    public StaticMapPackager.PackResult? LastPackResult { get; private set; }
    public GameMapDto? LoadedMap { get; private set; }

    private Node3D _mapRoot = null!;
    private Camera3D _camera = null!;
    private Label _statusLabel = null!;
    private Pr1VisualMapBuilder? _builder;

    public override async void _Ready()
    {
        _mapRoot = GetNode<Node3D>("MapRoot");
        _camera = GetNode<Camera3D>("CameraRig/Camera3D");
        _statusLabel = GetNode<Label>("Overlay/StatusPanel/Margin/StatusLabel");
        await BuildAsync();
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--bake-static-map") >= 0 && LastBuildResult?.Ok == true)
        {
            if (RenderingServer.GetCurrentRenderingMethod() != "forward_plus")
            {
                GD.PushError($"静态地图必须在 Forward+ 下烘焙，当前实际渲染器为 {RenderingServer.GetCurrentRenderingMethod()}。");
                if (Array.IndexOf(args, "--quit-after-bake") >= 0) GetTree().Quit(1);
                return;
            }
            BakeAndPackage();
            if (Array.IndexOf(args, "--quit-after-bake") >= 0) GetTree().Quit(LastBakeResult?.Ok == true && LastPackResult?.Ok == true ? 0 : 1);
        }
    }

    public async Task<bool> BuildAsync()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            _statusLabel.Text = "正在加载隔离地图数据……";
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<GameMapDto>>(FileAccess.GetFileAsString(FixturePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (envelope?.Code != 1 || envelope.Data == null) throw new InvalidOperationException("固定地图数据响应无效。");
            var validation = MapDataValidator.Validate(envelope.Data);
            if (!validation.IsValid) throw new InvalidOperationException(validation.Error);
            if (CellScene == null) throw new InvalidOperationException("未配置隔离格子场景。");
            LoadedMap = envelope.Data;
            PaperWorldBackground.Enabled = true;
            var renderProfile = ArtRenderProfile.Preset(this, PaperPreset);
            GD.Print($"STATIC_MAP_RENDERER method={RenderingServer.GetCurrentRenderingMethod()} profile={renderProfile}");
            _builder = new Pr1VisualMapBuilder();
            LastBuildResult = _builder.Build(LoadedMap, _mapRoot, CellScene, CellRadius, CellHeight, CellBevelWidth);
            if (!LastBuildResult.Ok) throw new InvalidOperationException(LastBuildResult.Error);
            FitCamera(LastBuildResult.VisualBounds);
            watch.Stop();
            _mapRoot.SetMeta("build_elapsed_ms", watch.ElapsedMilliseconds);
            _statusLabel.Text = $"隔离复刻完成  {LastBuildResult.CellCount} 格 / {LastBuildResult.EdgeCount} 边  {watch.ElapsedMilliseconds} ms\n实际渲染器：{RenderingServer.GetCurrentRenderingMethod()} · P{PaperPreset}";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--bake-static-map") < 0)
                GetNodeOrNull<MapVisualRuntime>("MapVisualRuntime")?.AttachMap(this);
            return true;
        }
        catch (Exception exception)
        {
            watch.Stop();
            LastBuildResult = new(false, exception.Message, 0, 0, default, default);
            _statusLabel.Text = "隔离地图生成失败：" + exception.Message;
            GD.PushError(_statusLabel.Text);
            return false;
        }
    }

    public bool BakeAndPackage()
    {
        if (LoadedMap == null || _builder == null || LastBuildResult?.Ok != true) return false;
        var generated = _mapRoot.GetNode<Node3D>("GeneratedVisualMap");
        var elapsed = _mapRoot.GetMeta("build_elapsed_ms", 0).AsInt64();
        LastBakeResult = StaticMapBaker.Bake(this, generated, _builder.CellsByCode, LoadedMap, LastBuildResult.VisualBounds, elapsed);
        if (!LastBakeResult.Ok) { GD.PushError(LastBakeResult.Error); return false; }
        LastPackResult = StaticMapPackager.Build(StaticMapBaker.ScenePath, StaticMapBaker.MetadataPath, StaticMapBaker.PckPath);
        if (!LastPackResult.Ok) { GD.PushError(LastPackResult.Error); return false; }
        _statusLabel.Text = $"烘焙完成：{LastBakeResult.AnchorCount} 锚点，{LastBakeResult.NodeCount} 节点，{LastBakeResult.MeshCount} Mesh\nPCK：{LastPackResult.ResourceCount} 资源，{LastPackResult.Bytes / 1024.0 / 1024.0:F1} MiB";
        GD.Print($"STATIC_MAP_BAKE_OK cells={LastBakeResult.CellCount} anchors={LastBakeResult.AnchorCount} nodes={LastBakeResult.NodeCount} meshes={LastBakeResult.MeshCount} materials={LastBakeResult.MaterialCount} pckResources={LastPackResult.ResourceCount} pckSha256={LastPackResult.Sha256}");
        return true;
    }

    private void FitCamera(Aabb bounds)
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var aspect = viewport.Y > 0 ? viewport.X / viewport.Y : 16f / 9f;
        var plan = MapCameraFitter.Calculate(bounds, aspect, 1.08f);
        var rig = GetNode<Node3D>("CameraRig");
        rig.Position = new Vector3(plan.Target.X, 0, plan.Target.Z);
        _camera.Position = plan.CameraPosition - rig.Position;
        _camera.LookAt(plan.Target, Vector3.Up);
        _camera.Size = Math.Max(0.001f, plan.OrthographicSize);
    }
}
