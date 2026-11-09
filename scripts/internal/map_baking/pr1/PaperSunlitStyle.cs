using System;
using System.Collections.Generic;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>
/// P9 "阳光折纸" (sunlit papercraft) look — art profile preset 9. Claude, 2026-10-26.
/// Defaults = Claude Code concept-match pass (2026-10-26); the earlier 'p9e' values are kept in
/// artifacts/claude-sunlit/variants-p9e.json for comparison.
///
/// Target: the concept board — warm golden key light, bright pastel paper, every hex a thick
/// crumpled paper card with a cream rim, soft cool-violet shade, a sunny haze toward the far
/// edge and a warm light leak from the sun side.
///
/// Measured against the concept image the decisive differences were not "more contrast /
/// more saturation" (P8 went that way and ended cooler and more chromatic than the concept):
/// the concept is WARM (mean R-B +0.07 vs V5 0.00, P8 -0.07), LESS chromatic (0.22 vs 0.29/0.34)
/// and carries visible paper relief. This preset therefore changes:
///   material : crumpled-paper normal relief + hex card rim/bevel + concept pastel palette
///   lights   : warm sun, cool-but-bright sky fill, softer shadow opacity
///   world    : depth haze with sun scatter (far edge lighter), restrained glow
///   grade    : warm white balance, gentle saturation, warm light leak, lifted paper shade
/// It never touches geometry layout, gameplay, animation managers or the other presets.
/// </summary>
public static class PaperSunlitStyle
{
    public const int PresetIndex = 9;

    /// <summary>Concept-matched printed paper colours per board (sRGB). Only used by preset 9.</summary>
    public static readonly IReadOnlyDictionary<string, Color> Regions = new Dictionary<string, Color>
    {
        ["A"] = new("a7bf6a"), ["B"] = new("2d5628"), ["C"] = new("aca0e0"), ["D"] = new("c8aa64"),
        ["E"] = new("cec7bf"), ["F"] = new("bd95af"), ["G"] = new("ba7d6d"), ["H"] = new("daa066"),
        ["I"] = new("b3bdc1"), ["J"] = new("dcab60"), ["K"] = new("6cb692"), ["L"] = new("94cedb"),
    };

    public sealed class Settings
    {
        // Key light: warm, a little lower than V5 so paper creases and card rims read.
        public float SunPitch = -56f;
        public float SunYaw = -38f;
        public float SunEnergy = 1.85f;
        public Color SunColor = new("ffe7bf");
        // Opposite-side sky fill from ABOVE (the legacy bounce pointed upward and did nothing).
        public float FillPitch = -55f;
        public float FillYaw = 150f;
        public float FillEnergy = 0.22f;
        public Color FillColor = new("b8c4ff");
        // Sky ambient: bright and slightly violet so shade stays readable pastel, not grey.
        public float AmbientEnergy = 0.42f;
        public Color AmbientColor = new("c3c8f0");
        public float SkyContribution = 0.55f;
        public Color SkyTop = new("8fb4e6");
        public Color SkyHorizon = new("f4e7cf");
        public Color GroundBottom = new("8a7a9a");
        public float SkyEnergy = 0.70f;
        public float GroundEnergy = 0.45f;
        // Tone mapping. Filmic keeps paper white with a soft shoulder; grade handles warmth.
        public Godot.Environment.ToneMapper Tonemap = Godot.Environment.ToneMapper.Filmic;
        public float Exposure = 0.86f;
        public float White = 6.0f;
        // Shadows: crisp shape, not black.
        public float ShadowOpacity = 0.72f;
        public float ShadowBlur = 2.0f;
        public float ShadowMaxDistance = 34f;
        // SSAO: contact only.
        public float SsaoRadius = 0.30f;
        public float SsaoIntensity = 0.95f;
        public float SsaoPower = 1.25f;
        public float SsaoLightAffect = 0.10f;
        // Sunny haze toward the far edge of the board (depth fog, orthographic friendly).
        public bool Haze = false;   // engine depth fog (radial in ortho); off by default, the grade haze below is used
        public Color HazeColor = new("f3e4c8");
        public float HazeEnergy = 1.0f;
        public float HazeSunScatter = 0.35f;
        public float HazeDensity = 0.55f;   // max blend in depth mode
        public float HazeBegin = 0.62f;     // relative to camera distance, see Apply
        public float HazeEnd = 1.55f;
        public float HazeCurve = 1.6f;
        public float HazeSkyAffect = 0.0f;
        // Glow: only bright paper and sparkles, soft-light so it brightens without washing out.
        public bool Glow = true;
        public float GlowIntensity = 0.65f;
        public float GlowStrength = 1.05f;
        public float GlowBloom = 0.04f;
        public float GlowThreshold = 0.85f;

        // Material (sunlit_paper.gdshader)
        public float CrumpleTop = 0.30f;
        public float CrumpleOther = 0.22f;
        public float CrumpleScale = 2.4f;
        public float CrumpleObjectScale = 1.1f;
        public float CrumpleInk = 0.08f;
        public float RimWidth = 0.10f;
        public float RimTilt = 0.0f;
        public float RimLift = 0.0f;
        public Color RimColor = new("fbf2dc");
        public float RimCrease = 0.0f;
        public float SideTint = 0.85f;
        public float AlbedoSaturation = 1.03f;
        public float AlbedoLift = 0.0f;
        public float PropSaturation = 1.04f;
        public float PropLift = 0.03f;
        public float Wrap = 0.30f;
        public float BandLo = 0.16f;
        public float BandHi = 0.80f;
        public float Grad = 0.50f;
        public float ShadeFloor = 0.10f;
        public Color ShadeTint = new("a8a0ff");
        public float ShadeTintMix = 0.55f;
        public float Sheen = 0.10f;
        public Color SeamColor = new("f6eed8");
        public float SeamWidth = 0.020f;
        // Claude Code (concept match): fine cardstock grain, one crisp white seam, coloured landmark frames.
        public float GrainScale = 1.0f;
        public float GrainNormal = 0.60f;
        public float GrainAlbedo = 0.40f;
        public float SeamLine = 0.022f;          // shader seam, fraction of hex radius per card
        public Color SeamLineColor = new("f7f2e6");
        public bool LandmarkFrames = true;
        public float FrameInner = 0.885f;         // fraction of radius
        public float FrameHeight = 0.07f;       // fraction of radius above the card top
        public bool ForestRetint = true;         // Everfree umbrella trees: same shapes, greener paper

        // Final LDR grade (paper_grade.gdshader, sunlit branch)
        public float GradeStrength = 1.0f;
        public Vector3 WhiteBalance = new(1.025f, 1.0f, 0.95f);
        public float GradeSaturation = 1.32f;
        public float CyanDesaturate = 0.08f;
        public float GradeContrast = 0.38f;
        public float ShadeLift = 0.0f;
        public Color ShadeColor = new("6a5a8c");
        public float LeakStrength = 0.20f;
        public Color LeakColor = new("ffd89a");
        public Vector2 LeakCentre = new(-0.05f, -0.10f);
        public float LeakRadius = 0.95f;
        public float HighlightSoft = 0.10f;
        public float GradeHaze = 0.02f;
        public Color GradeHazeColor = new("f6ead6");
        public float GradeHazeTop = 0.40f;

        // Ocean
        public float OceanSun = 1.0f;

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    public static Settings Current { get; private set; } = new();
    public static void Use(Settings s) => Current = s;

    private static Texture2D? _crumple;
    public static Texture2D? Crumple => _crumple ??= ResourceLoader.Exists("res://assets/art/textures/paper/paper_crumple.png")
        ? GD.Load<Texture2D>("res://assets/art/textures/paper/paper_crumple.png") : null;
    private static Texture2D? _grain;
    public static Texture2D? Grain => _grain ??= ResourceLoader.Exists("res://assets/art/textures/paper/paper_felt.png")
        ? GD.Load<Texture2D>("res://assets/art/textures/paper/paper_felt.png") : null;
    public static void ReleaseAtShutdown() { _crumple = null; _grain = null; }

    /// <summary>
    /// Everfree paper colours for P9 (sRGB). The delivered navy bark / blue-teal canopy read grey
    /// against the green forest cards; shapes stay, only the printed paper changes. Index = _0.._3 variant.
    /// </summary>
    private static readonly (string Prefix, string[] Colors)[] ForestPaper =
    [
        ("MAT_Paper_EverfreeCanopy", ["4f6b2a", "3d6a4a", "5a7430", "2f5f4f"]),
        ("MAT_Paper_EverfreeBark", ["2f2a33", "3a3038", "28252e", "352c35"]),
        ("MAT_Paper_Fern", ["4f9a4c", "5aa553", "478f46", "62ab5a"]),
        ("MAT_Paper_ForestMoss", ["5c9a48", "68a64f"]),
    ];
    public static Color Retint(string materialName, Color source)
    {
        if (!Current.ForestRetint || string.IsNullOrEmpty(materialName)) return source;
        foreach (var (prefix, colors) in ForestPaper)
            if (materialName.StartsWith(prefix, StringComparison.Ordinal))
            {
                var tail = materialName.Length > prefix.Length + 1 ? materialName[^1] - '0' : 0;
                return new Color(colors[Math.Clamp(tail, 0, colors.Length - 1)]);
            }
        return source;
    }

    /// <summary>Landmark frame accent per board (concept: gold / violet card frames, red for Tartarus).</summary>
    public static Color FrameColor(string board, string landmarkId) => landmarkId == "tartarus" ? new Color("c63b3b") : board switch
    {
        "A" or "C" or "E" or "I" or "K" or "L" => new Color("d9a531"),
        _ => new Color("8a63c9")
    };

    /// <summary>Light rig, sky, haze, glow and shadow. Called after ArtRenderProfile built its V5 base.</summary>
    public static void Apply(Node3D stage, Settings s)
    {
        Current = s;
        var method = RenderingServer.GetCurrentRenderingMethod();
        var world = stage.GetNode<WorldEnvironment>("Environment");
        var env = world.Environment ?? new Godot.Environment();
        world.Environment = env;

        env.TonemapMode = s.Tonemap;
        env.TonemapExposure = s.Exposure;
        env.TonemapWhite = s.White;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        env.AmbientLightColor = s.AmbientColor;
        env.AmbientLightEnergy = s.AmbientEnergy;
        env.AmbientLightSkyContribution = s.SkyContribution;
        if (env.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = s.SkyTop;
            sky.SkyHorizonColor = s.SkyHorizon;
            sky.GroundHorizonColor = s.SkyHorizon;
            sky.GroundBottomColor = s.GroundBottom;
            sky.SkyEnergyMultiplier = s.SkyEnergy;
            sky.GroundEnergyMultiplier = s.GroundEnergy;
        }
        env.BackgroundColor = new Color("9fd0d0");

        env.GlowEnabled = s.Glow;
        if (s.Glow)
        {
            env.GlowIntensity = s.GlowIntensity;
            env.GlowStrength = s.GlowStrength;
            env.GlowBloom = s.GlowBloom;
            env.GlowHdrThreshold = s.GlowThreshold;
            env.GlowHdrScale = 1.6f;
            env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;
        }
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;
        env.SsrEnabled = false;
        env.VolumetricFogEnabled = false;
        env.AdjustmentEnabled = false;
        env.SsaoEnabled = method == "forward_plus";
        if (env.SsaoEnabled)
        {
            env.SsaoRadius = s.SsaoRadius;
            env.SsaoIntensity = s.SsaoIntensity;
            env.SsaoPower = s.SsaoPower;
            env.SsaoDetail = 0.35f;
            env.SsaoLightAffect = s.SsaoLightAffect;
        }

        var sun = stage.GetNode<DirectionalLight3D>("DirectionalLight3D");
        sun.RotationDegrees = new Vector3(s.SunPitch, s.SunYaw, 0);
        sun.LightColor = s.SunColor;
        sun.LightEnergy = s.SunEnergy;
        sun.LightSpecular = 0.35f;
        sun.LightAngularDistance = 0;
        sun.ShadowEnabled = true;
        sun.ShadowOpacity = s.ShadowOpacity;
        sun.ShadowBlur = s.ShadowBlur;
        sun.ShadowBias = 0.03f;
        sun.ShadowNormalBias = 0.5f;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
        sun.DirectionalShadowBlendSplits = true;
        sun.DirectionalShadowMaxDistance = s.ShadowMaxDistance;
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;

        var fill = stage.GetNodeOrNull<DirectionalLight3D>("PaperBounce");
        if (fill == null) { fill = new DirectionalLight3D { Name = "PaperBounce" }; stage.AddChild(fill); }
        fill.RotationDegrees = new Vector3(s.FillPitch, s.FillYaw, 0);
        fill.LightColor = s.FillColor;
        fill.LightEnergy = s.FillEnergy;
        fill.LightSpecular = 0;
        fill.ShadowEnabled = false;
        fill.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;

        env.FogEnabled = s.Haze;
        if (s.Haze)
        {
            env.FogMode = Godot.Environment.FogModeEnum.Depth;
            env.FogLightColor = s.HazeColor;
            env.FogLightEnergy = s.HazeEnergy;
            env.FogSunScatter = s.HazeSunScatter;
            env.FogDensity = s.HazeDensity;
            env.FogSkyAffect = s.HazeSkyAffect;
            env.FogAerialPerspective = 0;
            env.FogHeightDensity = 0;
            env.FogDepthCurve = s.HazeCurve;
            UpdateHaze(stage);
        }
        if (method == "forward_plus")
            RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
    }

    /// <summary>
    /// Depth haze is distance based; the orthographic map camera sits at a fixed height, so the
    /// begin/end distances follow the camera's distance to the board centre. Call after camera moves
    /// far (zoom changes) — cheap, only two floats.
    /// </summary>
    public static void UpdateHaze(Node3D stage)
    {
        if (ArtRenderProfile.CurrentPreset != PresetIndex) return;
        var env = stage.GetNodeOrNull<WorldEnvironment>("Environment")?.Environment;
        var camera = stage.GetViewport()?.GetCamera3D();
        if (env == null || camera == null || !env.FogEnabled) return;
        var s = Current;
        var forward = -camera.GlobalBasis.Z;
        // distance from camera to the ground plane (y=0) along the view axis
        var dist = forward.Y < -0.05f ? camera.GlobalPosition.Y / -forward.Y : 30f;
        var span = Math.Max(camera.Size, 4f);
        env.FogDepthBegin = dist + span * (s.HazeBegin - 1.0f) * 0.9f + span * 0.25f;
        env.FogDepthEnd = env.FogDepthBegin + span * (s.HazeEnd - s.HazeBegin) * 1.2f;
    }

    /// <summary>Push the material-side parameters onto a sunlit paper material.</summary>
    public static void ApplyMaterial(ShaderMaterial m, bool terrain, string board = "", Settings? overrideSettings = null)
    {
        var s = overrideSettings ?? Current;
        var crumple = Crumple;
        m.SetShaderParameter("use_crumple", crumple != null);
        if (crumple != null) m.SetShaderParameter("crumple_texture", crumple);
        m.SetShaderParameter("crumple_top", s.CrumpleTop);
        m.SetShaderParameter("crumple_other", s.CrumpleOther);
        m.SetShaderParameter("crumple_scale", s.CrumpleScale);
        m.SetShaderParameter("crumple_object_scale", s.CrumpleObjectScale);
        m.SetShaderParameter("crumple_ink", s.CrumpleInk);
        var grain = Grain;
        m.SetShaderParameter("use_grain", grain != null);
        if (grain != null) m.SetShaderParameter("grain_texture", grain);
        m.SetShaderParameter("grain_scale", s.GrainScale);
        m.SetShaderParameter("grain_normal", s.GrainNormal);
        m.SetShaderParameter("grain_albedo", s.GrainAlbedo);
        m.SetShaderParameter("seam_width", s.SeamLine);
        m.SetShaderParameter("seam_color", s.SeamLineColor);
        m.SetShaderParameter("rim_width", s.RimWidth);
        m.SetShaderParameter("rim_tilt", s.RimTilt);
        m.SetShaderParameter("rim_lift", s.RimLift);
        m.SetShaderParameter("rim_color", s.RimColor);
        m.SetShaderParameter("rim_crease", s.RimCrease);
        m.SetShaderParameter("side_tint", s.SideTint);
        m.SetShaderParameter("albedo_saturation", terrain ? s.AlbedoSaturation : s.PropSaturation);
        m.SetShaderParameter("albedo_lift", terrain ? s.AlbedoLift : s.PropLift);
        m.SetShaderParameter("sun_wrap", s.Wrap);
        m.SetShaderParameter("sun_band_lo", s.BandLo);
        m.SetShaderParameter("sun_band_hi", s.BandHi);
        m.SetShaderParameter("sun_grad", s.Grad);
        m.SetShaderParameter("sun_shade_floor", s.ShadeFloor);
        m.SetShaderParameter("sun_shade_tint", new Vector3(s.ShadeTint.R, s.ShadeTint.G, s.ShadeTint.B));
        m.SetShaderParameter("sun_shade_tint_mix", s.ShadeTintMix);
        m.SetShaderParameter("sun_sheen", s.Sheen);
        if (terrain && Regions.TryGetValue(board, out var region)) m.SetShaderParameter("region_color", region);
    }

    /// <summary>Grade parameters for PaperAtmosphere (sunlit branch of paper_grade.gdshader).</summary>
    public static void ApplyGrade(ShaderMaterial g, Settings? overrideSettings = null)
    {
        var s = overrideSettings ?? Current;
        g.SetShaderParameter("sun_strength", s.GradeStrength);
        g.SetShaderParameter("sun_white_balance", s.WhiteBalance);
        g.SetShaderParameter("sun_saturation", s.GradeSaturation);
        g.SetShaderParameter("sun_cyan_desat", s.CyanDesaturate);
        g.SetShaderParameter("sun_contrast", s.GradeContrast);
        g.SetShaderParameter("sun_shade_lift", s.ShadeLift);
        g.SetShaderParameter("sun_shade_color", new Vector3(s.ShadeColor.R, s.ShadeColor.G, s.ShadeColor.B));
        g.SetShaderParameter("sun_leak", s.LeakStrength);
        g.SetShaderParameter("sun_leak_color", new Vector3(s.LeakColor.R, s.LeakColor.G, s.LeakColor.B));
        g.SetShaderParameter("sun_leak_centre", s.LeakCentre);
        g.SetShaderParameter("sun_leak_radius", s.LeakRadius);
        g.SetShaderParameter("sun_highlight_soft", s.HighlightSoft);
        g.SetShaderParameter("sun_haze", s.GradeHaze);
        g.SetShaderParameter("sun_haze_color", new Vector3(s.GradeHazeColor.R, s.GradeHazeColor.G, s.GradeHazeColor.B));
        g.SetShaderParameter("sun_haze_top", s.GradeHazeTop);
    }

    public static string Describe(Settings s) =>
        $"P9 sun {s.SunEnergy:0.00}@{s.SunPitch:0}/{s.SunYaw:0} fill {s.FillEnergy:0.00} amb {s.AmbientEnergy:0.00} | {s.Tonemap} x{s.Exposure:0.00} | " +
        $"crumple {s.CrumpleTop:0.00}/{s.CrumpleOther:0.00} rim {s.RimWidth:0.000}/{s.RimLift:0.00} | haze {(s.Haze ? s.HazeDensity : 0):0.00} glow {(s.Glow ? s.GlowIntensity : 0):0.00} | " +
        $"wb {s.WhiteBalance.X:0.000}/{s.WhiteBalance.Z:0.000} sat {s.GradeSaturation:0.00} leak {s.LeakStrength:0.00}";
}
