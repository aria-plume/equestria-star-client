using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

public static class EnvironmentArtCatalog
{
    public sealed class Port
    {
        public string Id {get;set;}="";
        [JsonPropertyName("position_m")] public float[] Position {get;set;}=[];
        [JsonPropertyName("outward_direction")] public float[] Direction {get;set;}=[];
        [JsonPropertyName("width_m")] public float Width {get;set;}
        public Vector3 PositionGodot=>new(Position[0],Position[2],-Position[1]);
        public Vector3 DirectionGodot=>new(Direction[0],Direction[2],-Direction[1]);
    }
    public sealed class Entry
    {
        public string Id {get;set;}="";
        public string Name {get;set;}="";
        public Dictionary<string,OrigamiArtCatalog.Component> Components {get;set;}=new();
        public Port[] Ports {get;set;}=[];
    }
    private sealed class Catalog { public Entry[] Assets { get; set; } = []; }
    private static readonly Dictionary<string, PackedScene> Scenes = new();
    private static Dictionary<string, Entry>? _entries;
    public static void ReleaseSceneCache()=>Scenes.Clear();
    public static IReadOnlyDictionary<string, Entry> Entries
    {
        get
        {
            if (_entries != null) return _entries;
            _entries = new();
            const string path = "res://assets/internal/map_baking/pr1/environment-catalog.json";
            if (!FileAccess.FileExists(path)) return _entries;
            try
            {
                var catalog = JsonSerializer.Deserialize<Catalog>(FileAccess.GetFileAsString(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (var entry in catalog?.Assets ?? []) _entries.Add(entry.Id, entry);
            }
            catch(Exception exception)
            {
                _entries.Clear();
                GD.PushWarning("环境资产目录不可用，保留原地图："+exception.Message);
            }
            return _entries;
        }
    }

    public static Node3D? Place(Node3D parent, string id, Vector3 position, float scale, float yaw = 0)
    {
        if (!Entries.ContainsKey(id)) return null;
        if (!Scenes.TryGetValue(id, out var scene))
        {
            var path=$"res://scenes/internal/map_baking/pr1/environment/{id}/visual.tscn";
            if(!ResourceLoader.Exists(path))return null;
            scene = GD.Load<PackedScene>(path);
            if (scene == null) return null;
            Scenes.Add(id, scene);
        }
        var node = scene.Instantiate<Node3D>();
        node.Name = id;
        node.Position = position;
        node.Scale = Vector3.One * scale;
        // Environment assets have no hex base. Y-up is already handled by GLB export.
        node.RotationDegrees = new Vector3(0, yaw, 0);
        node.SetMeta("environment_asset", id);
        parent.AddChild(node);
        EquestriaStar.Internal.MapBaking.Pr1.PaperMaterialLifetime.Collect(node);
        return node;
    }

    public static Node3D? PlaceParts(Node3D parent,string id,string[] parts,Vector3 position,float scale,float yaw=0,Func<string,PackedScene?>? residentScene=null)
    {
        if(!Entries.TryGetValue(id,out var entry) || parts.Length==0 || parts.Any(p=>!entry.Components.ContainsKey(p)))return null;
        var low=new Vector3(float.MaxValue,float.MaxValue,float.MaxValue);
        var high=new Vector3(float.MinValue,float.MinValue,float.MinValue);
        foreach(var key in parts)
        {
            var bounds=entry.Components[key].BoundsBlender;
            low=low.Min(new Vector3(bounds.Min[0],bounds.Min[1],bounds.Min[2]));
            high=high.Max(new Vector3(bounds.Max[0],bounds.Max[1],bounds.Max[2]));
        }
        // Recenter only the independently placed prop group. Keep its children in assembly space.
        var centre=new Vector3((low.X+high.X)*.5f,low.Z,-(low.Y+high.Y)*.5f);
        var wrapper=new Node3D {Name=id+"_Prop",Position=position,Scale=Vector3.One*scale,RotationDegrees=new Vector3(0,yaw,0)};
        wrapper.SetMeta("environment_asset",id);parent.AddChild(wrapper);
        foreach(var key in parts)
        {
            var path=entry.Components[key].Path;
            PackedScene? scene;
            if(residentScene!=null)scene=residentScene(path);
            else if(!Scenes.TryGetValue(path,out scene)){scene=GD.Load<PackedScene>(path);Scenes[path]=scene;}
            if(scene==null){wrapper.Free();return null;}
            var instance=scene.Instantiate<Node3D>();instance.Position-=centre;wrapper.AddChild(instance);
        }
        // Capture source handles before clearance may reject the prop or detail-cache
        // eviction removes it. Gallery releases these only after all instances detach.
        EquestriaStar.Internal.MapBaking.Pr1.PaperMaterialLifetime.Collect(wrapper);
        return wrapper;
    }
}
