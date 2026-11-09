using System;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>
/// Papercraft NPR look (art profile preset 7).
///
/// Scope: light rig + sky fill + shadow quality + tone mapping + the material
/// parameters that feed <c>physical_paper_npr.gdshader</c>. It never touches
/// geometry, layout, gameplay or the animation managers, and it is bypassed
/// completely for presets 0..6 (those keep physical_paper.gdshader and the
/// original Environment values from <see cref="ArtRenderProfile"/>).
/// </summary>
public static class PaperNprStyle
{
    /// <summary>Everything that can be tuned without editing the shader.</summary>
    public sealed class Settings
    {
        // Key light (the sun). Lower pitch = longer, more readable paper shadows.
        public float SunPitch = -35.0f;
        public float SunYaw = -42.0f;
        public float SunEnergy = 1.60f;
        public Color SunColor = new("ffedd0");
        public float SunSpecular = 0.0f;

        // Cool bounce from the opposite side; keeps the shade readable without
        // raising the sky fill that flattens the whole board.
        public float BouncePitch = 41.0f;
        public float BounceYaw = 142.0f;
        public float BounceEnergy = 0.12f;
        public Color BounceColor = new("93a8e6");

        // Environment fill. This is the main lever against "washed out middle tones".
        public float AmbientEnergy = 0.30f;
        public Color AmbientColor = new("8fa2e0");
        public float SkyContribution = 0.45f;
        public float SkyTopEnergy = 0.38f;
        public float GroundEnergy = 0.30f;
        public Color SkyTop = new("6d8ec2");
        public Color SkyHorizon = new("cfdcd8");
        public Color GroundBottom = new("5e5478");

        // Tone mapping / exposure.
        public Godot.Environment.ToneMapper Tonemap = Godot.Environment.ToneMapper.Aces;
        public float Exposure = 1.02f;

        // Occlusion.
        public bool Ssao = true;
        public float SsaoRadius = 0.50f;
        public float SsaoIntensity = 1.25f;
        public float SsaoPower = 1.35f;
        public float SsaoDetail = 0.40f;
        public float SsaoLightAffect = 0.18f;

        // Shadows.
        public float ShadowOpacity = 0.95f;
        public float ShadowBlur = 1.15f;
        public float ShadowBias = 0.030f;
        public float ShadowNormalBias = 0.45f;
        public float ShadowMaxDistance = 30.0f;
        // Two splits keep the shadow-caster pass count identical to V5; four splits cost
        // roughly +80% draw calls for a shadow this crisply filtered.
        public DirectionalLight3D.ShadowMode ShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
        public bool BlendSplits = true;

        // Material NPR response (mirrors physical_paper_npr.gdshader uniforms).
        public float Wrap = 0.34f;
        public float BandLo = 0.30f;
        public float BandHi = 0.74f;
        public float Grad = 0.78f;
        public float Shade = 0.30f;
        public float LitGain = 1.0f;
        public float Fold = 0.38f;
        public float FoldScale = 1.00f;
        public float FoldLift = 0.0f;
        public float ShadeTintMix = 0.68f;
        public Color ShadeTint = new("9ab3ff");

        // Final LDR grade (paper_grade.gdshader, npr branch).
        public float GradeStrength = 1.0f;
        public float GradeSaturation = 1.24f;
        public float GradeContrast = 0.18f;
        public float GradeShadowDepth = 0.0f;
        public float GradeHighlightSoft = 0.09f;
        public Vector3 GradeShadowShift = new(-.022f, -.004f, .062f);
        public Vector3 GradeHighlightShift = new(.030f, .010f, -.020f);
        public float GradeVignette = 0.0f;

        // Restrained sunlit-paper glow (HDR buffer, before tone mapping).
        public bool Glow = false;
        public float GlowIntensity = 0.35f;
        public float GlowBloom = 0.05f;
        public float GlowThreshold = 1.15f;

        /// <summary>Lookdev only: keep the light rig but fall back to the original V5 paper shader.</summary>
        public bool PlainMaterial = false;

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    public static Settings Current { get; private set; } = new();

    public static void Use(Settings settings) => Current = settings;

    /// <summary>
    /// Preset 8: the same rig pushed to a cinematic intensity. Lower, warmer sun;
    /// a real cool fill from the opposite sky side (the old bounce lit from below and
    /// did almost nothing); crisper shadows; much stronger paper-sheet folds.
    /// </summary>
    public static Settings Cinema() => new()
    {
        SunPitch = -33.0f,
        SunEnergy = 1.75f,
        SunColor = new Color("ffd79a"),

        BouncePitch = -48.0f,
        BounceYaw = 135.0f,
        BounceEnergy = 0.55f,
        BounceColor = new Color("9db4ff"),

        AmbientEnergy = 0.30f,
        AmbientColor = new Color("8aa0ff"),
        SkyContribution = 0.55f,
        SkyTopEnergy = 0.32f,
        GroundEnergy = 0.24f,
        SkyTop = new Color("5f81bd"),
        GroundBottom = new Color("4d446a"),

        Exposure = 1.16f,

        SsaoRadius = 0.55f,
        SsaoIntensity = 1.50f,
        SsaoPower = 1.50f,
        SsaoDetail = 0.45f,
        SsaoLightAffect = 0.25f,

        ShadowOpacity = 0.97f,
        ShadowBlur = 0.72f,
        ShadowMaxDistance = 28.0f,

        Wrap = 0.22f,
        BandLo = 0.38f,
        BandHi = 0.80f,
        Grad = 0.85f,
        Shade = 0.22f,
        ShadeTint = new Color("8fa8ff"),
        ShadeTintMix = 0.75f,
        Fold = 0.28f,
        FoldScale = 1.10f,
        FoldLift = 0.05f,

        Glow = true,
        GlowIntensity = 0.35f,
        GlowBloom = 0.05f,
        GlowThreshold = 1.15f,
        GradeVignette = 0.18f,

        GradeSaturation = 1.38f,
        GradeContrast = 0.28f,
        GradeShadowDepth = 0.04f,
        GradeHighlightSoft = 0.12f,
        GradeShadowShift = new Vector3(-.030f, -.006f, .080f),
        GradeHighlightShift = new Vector3(.045f, .014f, -.026f),
    };

    /// <summary>Apply the look to an already built stage. Preset() has run its V5 base first.</summary>
    public static void Apply(Node3D stage, Settings s)
    {
        Current = s;
        var world = stage.GetNode<WorldEnvironment>("Environment");
        var env = world.Environment ?? new Godot.Environment();
        world.Environment = env;

        env.TonemapMode = s.Tonemap;
        env.TonemapExposure = s.Exposure;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        env.AmbientLightColor = s.AmbientColor;
        env.AmbientLightEnergy = s.AmbientEnergy;
        env.AmbientLightSkyContribution = s.SkyContribution;
        if (env.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = s.SkyTop;
            sky.SkyHorizonColor = s.SkyHorizon;
            sky.GroundBottomColor = s.GroundBottom;
            sky.SkyEnergyMultiplier = s.SkyTopEnergy;
            sky.GroundEnergyMultiplier = s.GroundEnergy;
        }

        env.GlowEnabled = s.Glow;
        if (s.Glow)
        {
            env.GlowIntensity = s.GlowIntensity;
            env.GlowBloom = s.GlowBloom;
            env.GlowHdrThreshold = s.GlowThreshold;
            env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen;
            env.GlowHdrScale = 1.0f;
            env.GlowStrength = 1.0f;
        }
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;
        env.SsrEnabled = false;
        env.FogEnabled = false;
        env.VolumetricFogEnabled = false;
        env.AdjustmentEnabled = false;
        env.SsaoEnabled = s.Ssao;
        if (s.Ssao)
        {
            env.SsaoRadius = s.SsaoRadius;
            env.SsaoIntensity = s.SsaoIntensity;
            env.SsaoPower = s.SsaoPower;
            env.SsaoDetail = s.SsaoDetail;
            env.SsaoLightAffect = s.SsaoLightAffect;
        }

        var sun = stage.GetNode<DirectionalLight3D>("DirectionalLight3D");
        sun.RotationDegrees = new Vector3(s.SunPitch, s.SunYaw, 0);
        sun.LightColor = s.SunColor;
        sun.LightEnergy = s.SunEnergy;
        sun.LightSpecular = s.SunSpecular;
        sun.LightAngularDistance = 0;
        sun.ShadowEnabled = true;
        sun.ShadowOpacity = s.ShadowOpacity;
        sun.ShadowBlur = s.ShadowBlur;
        sun.ShadowBias = s.ShadowBias;
        sun.ShadowNormalBias = s.ShadowNormalBias;
        sun.DirectionalShadowMode = s.ShadowMode;
        sun.DirectionalShadowBlendSplits = s.BlendSplits;
        sun.DirectionalShadowMaxDistance = s.ShadowMaxDistance;
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;

        var bounce = stage.GetNodeOrNull<DirectionalLight3D>("PaperBounce");
        if (bounce == null)
        {
            bounce = new DirectionalLight3D { Name = "PaperBounce" };
            stage.AddChild(bounce);
        }
        bounce.RotationDegrees = new Vector3(s.BouncePitch, s.BounceYaw, 0);
        bounce.LightColor = s.BounceColor;
        bounce.LightEnergy = s.BounceEnergy;
        bounce.LightSpecular = 0;
        bounce.ShadowEnabled = false;
        bounce.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;

        if (RenderingServer.GetCurrentRenderingMethod() == "forward_plus")
            RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
    }

    /// <summary>Push the material-side parameters onto every paper material of the preset-7 set.</summary>
    public static void ApplyMaterial(ShaderMaterial material, bool terrain, Settings? overrideSettings = null)
    {
        var s = overrideSettings ?? Current;
        material.SetShaderParameter("npr_wrap", s.Wrap);
        material.SetShaderParameter("npr_band_lo", s.BandLo);
        material.SetShaderParameter("npr_band_hi", s.BandHi);
        material.SetShaderParameter("npr_grad", s.Grad);
        material.SetShaderParameter("npr_shade", s.Shade);
        material.SetShaderParameter("npr_lit_gain", s.LitGain);
        material.SetShaderParameter("npr_shade_tint", new Vector3(s.ShadeTint.R, s.ShadeTint.G, s.ShadeTint.B));
        material.SetShaderParameter("npr_shade_tint_mix", s.ShadeTintMix);
        material.SetShaderParameter("npr_fold", terrain ? s.Fold : 0.0f);
        material.SetShaderParameter("npr_fold_scale", s.FoldScale);
        material.SetShaderParameter("npr_fold_lift", terrain ? s.FoldLift : 0.0f);
    }

    public static string Describe(Settings s) =>
        $"NPR sun {s.SunEnergy:0.00} @{s.SunPitch:0}deg | ambient {s.AmbientEnergy:0.00} sky {s.SkyContribution:0.00} | " +
        $"{s.Tonemap} x{s.Exposure:0.00} | ssao r{s.SsaoRadius:0.00} i{s.SsaoIntensity:0.00} la{s.SsaoLightAffect:0.00} | " +
        $"shadow {s.ShadowOpacity:0.00}/{s.ShadowBlur:0.00}/{s.ShadowMaxDistance:0}m | " +
        $"mat wrap{s.Wrap:0.00} band{s.BandLo:0.00}-{s.BandHi:0.00} grad{s.Grad:0.00} shade{s.Shade:0.00} fold{s.Fold:0.00}";
}
