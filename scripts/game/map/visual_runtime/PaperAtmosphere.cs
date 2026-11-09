using Godot;

namespace EquestriaStar.Game.Map.VisualRuntime;

/// <summary>位于三维画面之后、普通 UI 之前的 P9 纸张颜色分级。</summary>
public partial class PaperAtmosphere : CanvasLayer
{
    private BackBufferCopy? _copy;
    private ColorRect? _gradeRect;

    public ShaderMaterial? GradeMaterial { get; private set; }
    public bool IsActive => Visible && !GetViewport().UseHdr2D && _gradeRect?.Visible == true;

    public override void _Ready()
    {
        Layer = -10;
        _copy = new BackBufferCopy { Name = "ExplicitSceneCopy", CopyMode = BackBufferCopy.CopyModeEnum.Viewport };
        AddChild(_copy);
        GradeMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/internal/map_baking/pr1/shaders/paper_grade.gdshader")
        };
        MapVisualP9Profile.ApplyGrade(GradeMaterial);
        _gradeRect = new ColorRect
        {
            Name = "PaperGrade",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = GradeMaterial
        };
        AddChild(_gradeRect);
        _gradeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        RefreshVisibility();
    }

    public override void _Process(double delta) => RefreshVisibility();

    private void RefreshVisibility()
    {
        if (_gradeRect == null || _copy == null) return;
        var active = Visible && !GetViewport().UseHdr2D;
        _gradeRect.Visible = active;
        _copy.CopyMode = active ? BackBufferCopy.CopyModeEnum.Viewport : BackBufferCopy.CopyModeEnum.Disabled;
    }

    public override void _ExitTree()
    {
        GradeMaterial = null;
        _gradeRect = null;
        _copy = null;
    }
}
