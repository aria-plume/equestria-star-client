using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Reviewed art-space landing anchors and component transforms. No logical map movement.</summary>
public static class PawnArtLayout
{
    public sealed class Profile
    {
        public string Fingerprint {get;set;}="";
        public float ContentScale {get;set;}=1;
        public float[] ContentOffsetArt {get;set;}=[0,0,0];
        public float[] AnchorArt {get;set;}=[0,0,0];
    }
    public sealed class Catalog
    {
        public int SchemaVersion {get;set;}=1;
        public string Status {get;set;}="provisional-token-envelope-review";
        public float BodyHalfWidthRatio {get;set;}=.22f;
        public float BodyHeightRatio {get;set;}=.72f;
        public Dictionary<string,Profile> Assets {get;set;}=new();
        public Dictionary<string,float[]> CellAnchors {get;set;}=new();
    }
    private static Catalog? _catalog;
    public static Catalog Data
    {
        get
        {
            if(_catalog!=null)return _catalog;
            const string path="res://assets/internal/map_baking/pr1/token-layout.json";
            _catalog=FileAccess.FileExists(path)?JsonSerializer.Deserialize<Catalog>(FileAccess.GetFileAsString(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new():new();
            return _catalog;
        }
    }
    public static string Fingerprint(OrigamiArtCatalog.Entry entry)=>string.Join(":",entry.Components.OrderBy(p=>p.Key).Select(p=>p.Value.Sha256));
    public static Vector3 Anchor(HexCell3D cell)=>cell.GetNode<Node3D>("TokenRoot").Position;
    public static Vector3 WorldAnchor(HexCell3D cell)=>cell.GetNode<Node3D>("TokenRoot").GlobalPosition;
    public static void Apply(HexCell3D cell,OrigamiArtCatalog.Entry? entry)
    {
        var anchor=Vector3.Zero;
        if(entry!=null && Data.Assets.TryGetValue(entry.Id,out var profile) && profile.Fingerprint==Fingerprint(entry))
        {
            TransformContent(cell,profile.ContentScale,Vec(profile.ContentOffsetArt));
            var visualRoot=cell.GetNode<Node3D>("VisualRoot/CustomVisualRoot");
            // 导入资源缺失时仍保留格子中心锚点，不让静态制图器崩溃。
            if(visualRoot.GetChildCount()>0)
            {
                var visual=visualRoot.GetChild<Node3D>(0);
                anchor=visual.Basis*Vec(profile.AnchorArt);
            }
        }
        else if(cell.Data!=null && Data.CellAnchors.TryGetValue(cell.Data.CellCode,out var value))anchor=Vec(value)*cell.Radius;
        cell.GetNode<Node3D>("TokenRoot").Position=anchor;
    }
    public static void TransformContent(HexCell3D cell,float scale,Vector3 offsetArt)
    {
        var visual=cell.GetNodeOrNull<Node3D>("VisualRoot/CustomVisualRoot/OrigamiAsset");
        if(visual==null)return;
        foreach(var name in new[]{"Building","Decoration"})
        {
            var part=visual.GetNodeOrNull<Node3D>(name);if(part==null)continue;
            if(!part.HasMeta("original_art_transform"))part.SetMeta("original_art_transform",part.Transform);
            var original=part.GetMeta("original_art_transform").AsTransform3D();
            part.Transform=new Transform3D(original.Basis.Scaled(Vector3.One*scale),original.Origin*scale+offsetArt);
        }
    }
    public static Vector3 Vec(float[] values)=>new(values[0],values[1],values[2]);
    public static float[] Array(Vector3 point)=>[point.X,point.Y,point.Z];
}
