using Godot;

namespace EquestriaStar.Game.Map.VisualRuntime;

/// <summary>静态地图的纯视觉运行层：渲染诊断、P9 后处理和确定性环境动画。</summary>
public partial class MapVisualRuntime : Node
{
    [Export] public bool EnvironmentAnimationEnabled { get; set; } = true;
    [Export] public bool DriftAnimationEnabled { get; set; } = true;
    [Export] public bool HingeAnimationEnabled { get; set; } = true;
    [Export] public bool PaperShaderAnimationEnabled { get; set; } = true;

    private readonly MapVisualEnvironmentMotion _motion = new();
    private Node3D? _mapRoot;
    private PaperAtmosphere? _atmosphere;

    public string ActualRenderingMethod { get; private set; } = "unknown";
    public bool IsForwardPlus => ActualRenderingMethod == MapVisualP9Profile.RequiredRenderingMethod;
    public MapVisualP9Diagnostics? Diagnostics { get; private set; }
    public MapVisualMotionSnapshot MotionSnapshot => _motion.Snapshot();
    public PaperAtmosphere? Atmosphere => _atmosphere;

    public override void _Ready()
    {
        _atmosphere = GetNodeOrNull<PaperAtmosphere>("PaperAtmosphere");
        SetProcess(false);
    }

    public void AttachMap(Node3D mapRoot)
    {
        DetachMap();
        _mapRoot = mapRoot;
        ActualRenderingMethod = RenderingServer.GetCurrentRenderingMethod();
        MapVisualP9Profile.RestoreGlobalShadowQuality(ActualRenderingMethod);
        Diagnostics = MapVisualP9Profile.Inspect(mapRoot);
        if (!IsForwardPlus)
            GD.PushWarning($"MAP_VISUAL_RENDERER_FALLBACK expected=forward_plus actual={ActualRenderingMethod}");
        GD.Print($"MAP_VISUAL_RUNTIME renderer={ActualRenderingMethod} p9={Diagnostics.Summary}");
        if (_atmosphere?.GradeMaterial != null) MapVisualP9Profile.ApplyGrade(_atmosphere.GradeMaterial);
        _motion.Enabled = EnvironmentAnimationEnabled;
        _motion.DriftAnimationEnabled = DriftAnimationEnabled;
        _motion.HingeAnimationEnabled = HingeAnimationEnabled;
        _motion.ShaderAnimationEnabled = PaperShaderAnimationEnabled;
        if (EnvironmentAnimationEnabled) _motion.Attach(mapRoot);
        var motion = _motion.Snapshot();
        GD.Print($"MAP_VISUAL_MOTION drifts={motion.DriftTargets} hinges={motion.HingeTargets} flags={motion.FlagTargets} shaderInstances={motion.AnimatedShaderInstances}");
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _motion.Enabled = EnvironmentAnimationEnabled;
        _motion.DriftAnimationEnabled = DriftAnimationEnabled;
        _motion.HingeAnimationEnabled = HingeAnimationEnabled;
        _motion.Advance(delta);
    }

    public void AdvanceForTesting(double delta) => _motion.Advance(delta);

    public void DetachMap()
    {
        SetProcess(false);
        _motion.Detach();
        _mapRoot = null;
        Diagnostics = null;
    }

    public override void _ExitTree() => DetachMap();
}
