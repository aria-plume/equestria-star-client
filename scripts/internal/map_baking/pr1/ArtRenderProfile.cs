using System;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Fixed papercraft lighting; Forward+ adds contact occlusion and subtle diffuse bounce.</summary>
public static class ArtRenderProfile
{
    public enum Look { V3Sunny=0, Clear=1, Afternoon=2, Fairy=3, FairyVivid=4, WorldD=5, WorldDPlus=6, PapercraftNpr=7, PapercraftCinema=8, SunlitPapercraft=9 }
    public static int CurrentPreset {get;private set;}=5;
    public static bool IsFairy=>CurrentPreset>=3;
    public static bool IsNpr=>CurrentPreset is 7 or 8;
    public static bool IsSunlit=>CurrentPreset==PaperSunlitStyle.PresetIndex;
    public const int MaxPreset=9;
    public static string Apply(Node3D stage)
    {
        var args=OS.GetCmdlineUserArgs();var preset=System.Array.IndexOf(args,"--paper-preset");
        var index=preset>=0 && preset+1<args.Length && int.TryParse(args[preset+1],out var parsed)?Math.Clamp(parsed,0,MaxPreset):5;
        return Preset(stage,index);
    }
    public static string Preset(Node3D stage,int index,bool applyLook=true,bool useCurrentStyle=false)
    {
        index=Math.Clamp(index,0,MaxPreset);
        var method=RenderingServer.GetCurrentRenderingMethod();
        // Compatibility has no sky radiance path for this look and no SSAO; the NPR presets
        // would only produce a dark, oversaturated board there. Explicit fallback to V5.
        var fallback=index>=7 && method=="gl_compatibility";
        if(fallback)index=5;
        if(applyLook){CurrentPreset=index;stage.SetMeta("paper_light_preset",index);}
        var world=stage.GetNode<WorldEnvironment>("Environment");
        var environment=new Godot.Environment();world.Environment=environment;
        environment.BackgroundMode=Godot.Environment.BGMode.Color;environment.BackgroundColor=new Color("86bdc5");
        environment.AmbientLightSource=Godot.Environment.AmbientSource.Sky;
        environment.AmbientLightEnergy=.30f;environment.AmbientLightColor=new Color("b9c5e8");environment.AmbientLightSkyContribution=.65f;
        var skyMaterial=new ProceduralSkyMaterial {SkyTopColor=new Color("779dc7"),SkyHorizonColor=new Color("d5e7e5"),GroundBottomColor=new Color("665b75"),GroundHorizonColor=new Color("a1b3b7"),SkyEnergyMultiplier=index==1?.65f:.55f,GroundEnergyMultiplier=.40f};
        // GL uses constant fill below. Never allocate transient radiance cubemaps there.
        if(method!="gl_compatibility")environment.Sky=new Sky {SkyMaterial=skyMaterial};
        environment.ReflectedLightSource=Godot.Environment.ReflectionSource.Disabled;
        environment.TonemapMode=Godot.Environment.ToneMapper.Filmic;environment.TonemapExposure=1;
        environment.GlowEnabled=false;environment.SsilEnabled=false;environment.SdfgiEnabled=false;environment.SsrEnabled=false;
        environment.FogEnabled=false;environment.VolumetricFogEnabled=false;environment.AdjustmentEnabled=false;
        var sun=stage.GetNode<DirectionalLight3D>("DirectionalLight3D");
        sun.RotationDegrees=new Vector3(index==2?-38:-48,-42,0);
        sun.LightColor=new Color(index==1?"f4f7ff":index==2?"ffe9d2":"fff4e4");sun.LightEnergy=index==0?1.15f:1.10f;
        sun.LightSpecular=.10f;sun.ShadowEnabled=true;sun.ShadowOpacity=.90f;sun.ShadowBlur=1.6f;
        sun.ShadowBias=.035f;sun.ShadowNormalBias=.65f;
        sun.DirectionalShadowMode=DirectionalLight3D.ShadowMode.Parallel2Splits;sun.DirectionalShadowBlendSplits=true;
        sun.DirectionalShadowMaxDistance=42;
        sun.SkyMode=DirectionalLight3D.SkyModeEnum.LightOnly;
        var bounce=stage.GetNodeOrNull<DirectionalLight3D>("PaperBounce");
        if(bounce==null){bounce=new DirectionalLight3D {Name="PaperBounce"};stage.AddChild(bounce);}
        bounce.RotationDegrees=new Vector3(48,138,0);bounce.LightColor=new Color("c6cff4");bounce.LightEnergy=index==0?.11f:index==1?.12f:.13f;
        bounce.LightSpecular=0;bounce.ShadowEnabled=false;bounce.SkyMode=DirectionalLight3D.SkyModeEnum.LightOnly;
        var advanced=method=="forward_plus";
        RenderingServer.DirectionalSoftShadowFilterSetQuality(advanced?RenderingServer.ShadowQuality.SoftMedium:RenderingServer.ShadowQuality.SoftLow);
        environment.SsaoEnabled=advanced;
        if(advanced)
        {
            environment.SsaoRadius=.11f;environment.SsaoIntensity=.65f;environment.SsaoPower=1.10f;
            environment.SsaoDetail=.25f;environment.SsaoLightAffect=0;
            // Fixed PCF filtering keeps tiny paper foliage crisp; PCSS produced stippling at this scale.
            sun.LightAngularDistance=0;
        }
        if(method=="gl_compatibility")
        {
            environment.TonemapMode=Godot.Environment.ToneMapper.Linear;
            environment.AmbientLightSource=Godot.Environment.AmbientSource.Color;
            environment.AmbientLightSkyContribution=0;environment.AmbientLightEnergy=.22f;environment.Sky=null;
            sun.LightEnergy*=.45f;bounce.LightEnergy*=.70f;
            // GL shadow depth precision needs its own bias at the board's small scale.
            sun.ShadowBias=.10f;sun.ShadowNormalBias=2f;
            sun.DirectionalShadowMode=DirectionalLight3D.ShadowMode.Orthogonal;
        }
        if(index>=3)
        {
            sun.RotationDegrees=new Vector3(-50,-42,0);
            sun.LightColor=new Color("fff1dc");sun.LightEnergy=index>=5?(index==6?1.40f:1.36f):index==3?1.40f:1.50f;
            bounce.LightColor=new Color("a8b8f4");bounce.LightEnergy=.09f;
            environment.AmbientLightColor=new Color("a9b5e7");environment.AmbientLightEnergy=.27f;
            skyMaterial.SkyTopColor=new Color("708fc1");skyMaterial.GroundBottomColor=new Color("61577f");
            skyMaterial.SkyEnergyMultiplier=.50f;skyMaterial.GroundEnergyMultiplier=.34f;
            if(method=="gl_compatibility")
            {sun.LightEnergy*=.43f;bounce.LightEnergy*=.70f;environment.AmbientLightEnergy=.22f;}
        }
        if(index==7)
        {
            // Papercraft NPR look: stronger key, longer paper shadows, wide smooth material
            // response, unified tone chain. Overrides the D/D+ values set above.
            PaperNprStyle.Apply(stage,useCurrentStyle?PaperNprStyle.Current.Clone():new PaperNprStyle.Settings());
        }
        if(index==PaperSunlitStyle.PresetIndex)
        {
            // Sunlit papercraft (Claude): warm key, bright violet sky fill, crumpled paper cards,
            // warm grade + light leak. Overrides the D/D+ values set above.
            PaperSunlitStyle.Apply(stage,useCurrentStyle?PaperSunlitStyle.Current.Clone():new PaperSunlitStyle.Settings());
        }
        if(index==8)
        {
            // Cinematic intensity: lower warmer sun, a real cool sky fill from the opposite
            // side, crisper shadows, much stronger paper-sheet folds and a deeper grade.
            PaperNprStyle.Apply(stage,useCurrentStyle?PaperNprStyle.Current.Clone():PaperNprStyle.Cinema());
        }
        // 内部制图器只烘焙静态视觉，不挂接正式场景的屏幕后处理或环境动画控制器。
        return $"{method} · {(fallback?"P7/P8/P9需Forward+/Mobile · 已回退V5":index==9?"P9 阳光折纸":index==8?"P8 纸艺电影光影":index==7?"P7 纸艺强光影 NPR":index==6?"D+纸艺氛围":index==5?"V5 D纸艺世界":index==4?"V4 D强表现":index==3?"V4 D纸艺童话":index==0?"V3/A晴天":index==1?"B清透":"C午后")}";
    }

    public static void PhotoFocus(Camera3D camera,bool enabled)
    {
        if(!enabled){camera.Attributes=null;return;}
        if(camera.Attributes is not CameraAttributesPractical)camera.Attributes=new CameraAttributesPractical();
        var settings=(CameraAttributesPractical)camera.Attributes;
        var direction=-camera.GlobalBasis.Z;
        var focus=camera.GlobalPosition.Y/Math.Max(.1f,-direction.Y);
        settings.DofBlurFarEnabled=true;settings.DofBlurFarDistance=focus+2.8f;settings.DofBlurFarTransition=2.7f;
        settings.DofBlurNearEnabled=true;settings.DofBlurNearDistance=Math.Max(.1f,focus-3.5f);settings.DofBlurNearTransition=2.7f;
        settings.DofBlurAmount=.13f;
    }
    // Inspection only: restore the documented v2 lighting with geometry/material/camera unchanged.
    public static void LegacyLighting(Node3D stage)
    {
        var e=stage.GetNode<WorldEnvironment>("Environment").Environment;
        e.Sky=null;e.AmbientLightSource=Godot.Environment.AmbientSource.Color;e.AmbientLightColor=new Color("bbc1df");e.AmbientLightEnergy=.32f;e.AmbientLightSkyContribution=0;
        e.GlowEnabled=true;e.GlowIntensity=.11f;e.GlowBloom=.015f;e.GlowHdrThreshold=1.35f;
        var advanced=RenderingServer.GetCurrentRenderingMethod()=="forward_plus";e.SsaoEnabled=advanced;e.SsilEnabled=advanced;
        e.SsaoRadius=.16f;e.SsaoIntensity=1.30f;e.SsaoPower=1.15f;e.SsaoDetail=.45f;e.SsaoLightAffect=.08f;e.SsilRadius=.75f;e.SsilIntensity=.30f;e.SsilSharpness=.85f;
        var sun=stage.GetNode<DirectionalLight3D>("DirectionalLight3D");sun.RotationDegrees=new Vector3(-48,-42,0);sun.LightColor=new Color("fff0d6");sun.LightEnergy=1.12f;sun.LightSpecular=1;
        sun.ShadowOpacity=.72f;sun.ShadowBlur=2.2f;sun.ShadowBias=.1f;sun.ShadowNormalBias=2;sun.DirectionalShadowMode=DirectionalLight3D.ShadowMode.Parallel4Splits;sun.DirectionalShadowMaxDistance=40;
        stage.GetNode<DirectionalLight3D>("PaperBounce").LightEnergy=0;
    }
}
