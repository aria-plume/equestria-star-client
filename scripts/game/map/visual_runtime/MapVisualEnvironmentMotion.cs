using System;
using System.Collections.Generic;
using Godot;

namespace EquestriaStar.Game.Map.VisualRuntime;

public sealed record MapVisualMotionSnapshot(
    double Clock,
    long TickCount,
    int DriftTargets,
    int HingeTargets,
    int FlagTargets,
    int AnimatedShaderInstances,
    Vector3 FirstDriftPosition,
    Basis FirstHingeBasis);

/// <summary>只驱动静态地图中已有的漂移和纸张铰链，不创建地图或业务节点。</summary>
public sealed class MapVisualEnvironmentMotion
{
    private sealed record Drift(Node3D Node, Transform3D Rest, float Phase, bool Balloon);
    private sealed record Hinge(Node3D Node, Transform3D Rest, float Phase, Vector3 Axis, bool Flag);
    private sealed record SurfaceBinding(MeshInstance3D Mesh, int Surface, Material? Original);
    private sealed record OverrideBinding(GeometryInstance3D Geometry, Material? Original);

    private readonly List<Drift> _drifts = [];
    private readonly List<Hinge> _hinges = [];
    private readonly List<ShaderMaterial> _clockMaterials = [];
    private readonly List<SurfaceBinding> _surfaceBindings = [];
    private readonly List<OverrideBinding> _overrideBindings = [];
    private float _cellDiameter = 1.1f;

    public double Clock { get; private set; }
    public long TickCount { get; private set; }
    public bool Enabled { get; set; } = true;
    public bool DriftAnimationEnabled { get; set; } = true;
    public bool HingeAnimationEnabled { get; set; } = true;
    public bool ShaderAnimationEnabled { get; set; } = true;
    public float MotionWeight { get; set; } = 1.0f;

    public void Attach(Node3D mapRoot)
    {
        Detach();
        var metadata = mapRoot.GetNodeOrNull("BakeMetadata");
        if (metadata?.HasMeta("cell_diameter") == true) _cellDiameter = metadata.GetMeta("cell_diameter").AsSingle();
        Collect(mapRoot);
        Apply(0);
    }

    public void Advance(double delta)
    {
        if (!Enabled) return;
        Clock += Math.Max(0, delta);
        Apply(delta);
    }

    public void Detach()
    {
        if (DriftAnimationEnabled)
        foreach (var drift in _drifts)
            if (GodotObject.IsInstanceValid(drift.Node)) drift.Node.Transform = drift.Rest;
        if (HingeAnimationEnabled)
        foreach (var hinge in _hinges)
            if (GodotObject.IsInstanceValid(hinge.Node)) hinge.Node.Transform = hinge.Rest;
        _drifts.Clear();
        _hinges.Clear();
        foreach (var material in _clockMaterials)
        {
            if (!GodotObject.IsInstanceValid(material)) continue;
            material.SetShaderParameter("paper_clock", 0.0f);
            material.SetShaderParameter("paper_motion", 1.0f);
        }
        foreach (var binding in _surfaceBindings)
            if (GodotObject.IsInstanceValid(binding.Mesh)) binding.Mesh.SetSurfaceOverrideMaterial(binding.Surface, binding.Original);
        foreach (var binding in _overrideBindings)
            if (GodotObject.IsInstanceValid(binding.Geometry)) binding.Geometry.MaterialOverride = binding.Original;
        _clockMaterials.Clear();
        _surfaceBindings.Clear();
        _overrideBindings.Clear();
        Clock = 0;
        TickCount = 0;
    }

    public MapVisualMotionSnapshot Snapshot()
    {
        var firstDrift = _drifts.Count > 0 && GodotObject.IsInstanceValid(_drifts[0].Node)
            ? _drifts[0].Node.Position : Vector3.Zero;
        var firstHinge = _hinges.Count > 0 && GodotObject.IsInstanceValid(_hinges[0].Node)
            ? _hinges[0].Node.Basis : Basis.Identity;
        var flags = 0;
        foreach (var hinge in _hinges) if (hinge.Flag) flags++;
        return new(Clock, TickCount, _drifts.Count, _hinges.Count, flags, _clockMaterials.Count, firstDrift, firstHinge);
    }

    private void Collect(Node node)
    {
        if (node is Node3D node3D)
        {
            if (node3D.HasMeta("source_drift_amplitude"))
            {
                var phase = node3D.GetMeta("source_drift_phase", 0.0f).AsSingle();
                _drifts.Add(new Drift(node3D, node3D.Transform, phase,
                    node3D.Name.ToString().Contains("Balloon", StringComparison.OrdinalIgnoreCase)));
            }
            if (node3D.HasMeta("paper_wind_hinge"))
            {
                var path = node3D.GetPath().ToString();
                var phase = StablePhase(path);
                var localAxis = (node3D.GlobalBasis.Inverse() * new Vector3(-0.25f, 0, 1).Normalized()).Normalized();
                _hinges.Add(new Hinge(node3D, node3D.Transform, phase, localAxis,
                    node3D.HasMeta("paper_wind_flag")));
            }
        }
        if (ShaderAnimationEnabled && node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            var duplicates = new Dictionary<ulong, ShaderMaterial>();
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not ShaderMaterial material || !IsAnimated(material)) continue;
                if (!duplicates.TryGetValue(material.GetInstanceId(), out var local))
                {
                    local = (ShaderMaterial)material.Duplicate(false);
                    local.ResourceName = material.ResourceName + "_RuntimeMotion";
                    duplicates.Add(material.GetInstanceId(), local);
                    _clockMaterials.Add(local);
                }
                _surfaceBindings.Add(new SurfaceBinding(mesh, surface, mesh.GetSurfaceOverrideMaterial(surface)));
                mesh.SetSurfaceOverrideMaterial(surface, local);
            }
        }
        else if (ShaderAnimationEnabled && node is MultiMeshInstance3D multi && multi.MaterialOverride is ShaderMaterial multiMaterial && IsAnimated(multiMaterial))
        {
            var local = (ShaderMaterial)multiMaterial.Duplicate(false);
            local.ResourceName = multiMaterial.ResourceName + "_RuntimeMotion";
            _overrideBindings.Add(new OverrideBinding(multi, multi.MaterialOverride));
            multi.MaterialOverride = local;
            _clockMaterials.Add(local);
        }
        foreach (var child in node.GetChildren()) Collect(child);
    }

    private static bool IsAnimated(ShaderMaterial material)
    {
        var path = material.Shader?.ResourcePath ?? string.Empty;
        if (path.EndsWith("paper_ocean.gdshader", StringComparison.Ordinal)
            || path.EndsWith("paper_ambient.gdshader", StringComparison.Ordinal)
            || path.EndsWith("paper_local_halo.gdshader", StringComparison.Ordinal)) return true;
        if (path.EndsWith("sunlit_paper.gdshader", StringComparison.Ordinal)
            || path.EndsWith("physical_paper.gdshader", StringComparison.Ordinal)
            || path.EndsWith("physical_paper_npr.gdshader", StringComparison.Ordinal))
            return material.GetShaderParameter("water_flow").AsSingle() > 0.5f;
        return false;
    }

    private void Apply(double delta)
    {
        TickCount++;
        var time = (float)Clock;
        var intensity = Math.Max(0, MotionWeight);
        foreach (var drift in _drifts)
        {
            if (!GodotObject.IsInstanceValid(drift.Node)) continue;
            var angle = time * Mathf.Tau / (drift.Balloon ? 6.5f : 38.0f) + drift.Phase;
            var world = drift.Balloon
                ? new Vector3(Mathf.Sin(angle * 0.43f) * _cellDiameter * 0.012f,
                    Mathf.Sin(angle) * _cellDiameter * 0.045f,
                    Mathf.Sin(angle * 0.31f) * _cellDiameter * 0.008f)
                : new Vector3(Mathf.Sin(angle) * _cellDiameter * 0.20f,
                    Mathf.Sin(angle * 0.77f) * _cellDiameter * 0.012f,
                    Mathf.Cos(angle * 0.61f) * _cellDiameter * 0.035f);
            var parentBasis = (drift.Node.GetParentOrNull<Node3D>()?.GlobalBasis ?? Basis.Identity).Inverse();
            var localOffset = parentBasis * world * intensity;
            var rotation = drift.Balloon ? new Vector3(0, 0, Mathf.Sin(angle * 0.71f) * Mathf.DegToRad(1.6f)) : Vector3.Zero;
            drift.Node.Transform = new Transform3D(drift.Rest.Basis * Basis.FromEuler(rotation * intensity), drift.Rest.Origin + localOffset);
        }

        foreach (var hinge in _hinges)
        {
            if (!GodotObject.IsInstanceValid(hinge.Node)) continue;
            var angle = Mathf.DegToRad(hinge.Flag ? 3.2f : 2.1f) * intensity
                * Mathf.Sin(time * (hinge.Flag ? 1.85f : 1.3f) + hinge.Phase);
            hinge.Node.Transform = new Transform3D(hinge.Rest.Basis * new Basis(hinge.Axis, angle), hinge.Rest.Origin);
        }

        foreach (var material in _clockMaterials)
        {
            if (!GodotObject.IsInstanceValid(material)) continue;
            material.SetShaderParameter("paper_clock", time);
            material.SetShaderParameter("paper_motion", intensity);
            if (material.Shader?.ResourcePath.EndsWith("paper_ambient.gdshader", StringComparison.Ordinal) == true)
                material.SetShaderParameter("paper_detail", 1.0f);
        }
    }

    private static float StablePhase(string text)
    {
        uint hash = 2166136261;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(text)) hash = (hash ^ value) * 16777619;
        return (hash % 1000) / 1000.0f * Mathf.Tau;
    }
}
