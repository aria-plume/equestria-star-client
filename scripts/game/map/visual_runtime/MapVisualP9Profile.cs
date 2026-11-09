using System;
using System.Linq;
using Godot;

namespace EquestriaStar.Game.Map.VisualRuntime;

public sealed record MapVisualP9Diagnostics(
    bool WorldFound,
    bool SunFound,
    bool FillFound,
    bool SsaoEnabled,
    int SunlitPaperMaterials,
    int OceanMaterials,
    int MateriallessSurfaces,
    int BaseMateriallessSurfaces,
    string Summary);

/// <summary>集中保存原版 P9 的运行期渲染参数，不依赖内部地图生成器。</summary>
public static class MapVisualP9Profile
{
    public const string RequiredRenderingMethod = "forward_plus";

    public static readonly Vector3 WhiteBalance = new(1.025f, 1.0f, 0.95f);
    public const float GradeSaturation = 1.32f;
    public const float CyanDesaturate = 0.08f;
    public const float GradeContrast = 0.38f;
    public const float ShadeLift = 0.0f;
    public static readonly Color ShadeColor = new("6a5a8c");
    public const float LeakStrength = 0.20f;
    public static readonly Color LeakColor = new("ffd89a");
    public static readonly Vector2 LeakCentre = new(-0.05f, -0.10f);
    public const float LeakRadius = 0.95f;
    public const float HighlightSoft = 0.10f;
    public const float GradeHaze = 0.02f;
    public static readonly Color GradeHazeColor = new("f6ead6");
    public const float GradeHazeTop = 0.40f;

    public static void RestoreGlobalShadowQuality(string renderingMethod)
    {
        if (renderingMethod == RequiredRenderingMethod)
            RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
    }

    public static void ApplyGrade(ShaderMaterial material)
    {
        material.SetShaderParameter("strength", 1.0f);
        material.SetShaderParameter("npr_strength", 0.0f);
        material.SetShaderParameter("sun_strength", 1.0f);
        material.SetShaderParameter("sun_white_balance", WhiteBalance);
        material.SetShaderParameter("sun_saturation", GradeSaturation);
        material.SetShaderParameter("sun_cyan_desat", CyanDesaturate);
        material.SetShaderParameter("sun_contrast", GradeContrast);
        material.SetShaderParameter("sun_shade_lift", ShadeLift);
        material.SetShaderParameter("sun_shade_color", new Vector3(ShadeColor.R, ShadeColor.G, ShadeColor.B));
        material.SetShaderParameter("sun_leak", LeakStrength);
        material.SetShaderParameter("sun_leak_color", new Vector3(LeakColor.R, LeakColor.G, LeakColor.B));
        material.SetShaderParameter("sun_leak_centre", LeakCentre);
        material.SetShaderParameter("sun_leak_radius", LeakRadius);
        material.SetShaderParameter("sun_highlight_soft", HighlightSoft);
        material.SetShaderParameter("sun_haze", GradeHaze);
        material.SetShaderParameter("sun_haze_color", new Vector3(GradeHazeColor.R, GradeHazeColor.G, GradeHazeColor.B));
        material.SetShaderParameter("sun_haze_top", GradeHazeTop);
    }

    public static MapVisualP9Diagnostics Inspect(Node mapRoot)
    {
        var world = Find<WorldEnvironment>(mapRoot, "Environment");
        var sun = Find<DirectionalLight3D>(mapRoot, "DirectionalLight3D");
        var fill = Find<DirectionalLight3D>(mapRoot, "PaperBounce");
        var sunlit = 0;
        var ocean = 0;
        var materialless = 0;
        var baseMaterialless = 0;
        foreach (var mesh in mapRoot.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            for (var surface = 0; mesh.Mesh != null && surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) == null) baseMaterialless++;
                var active = mesh.GetActiveMaterial(surface);
                if (active == null) { materialless++; continue; }
                if (active is not ShaderMaterial material || material.Shader == null) continue;
                var path = material.Shader.ResourcePath;
                if (path.EndsWith("sunlit_paper.gdshader", StringComparison.Ordinal)) sunlit++;
                if (path.EndsWith("paper_ocean.gdshader", StringComparison.Ordinal)) ocean++;
            }
        }

        var ssao = world?.Environment?.SsaoEnabled == true;
        var summary = $"world={world != null} sun={sun != null} fill={fill != null} ssao={ssao} sunlit={sunlit} ocean={ocean} materialless={materialless} baseMaterialless={baseMaterialless}";
        return new(world != null, sun != null, fill != null, ssao, sunlit, ocean, materialless, baseMaterialless, summary);
    }

    public static T? Find<T>(Node root, string name) where T : Node
    {
        if (root is T own && root.Name == name) return own;
        return root.FindChild(name, true, false) as T;
    }
}
