using System;
using System.Collections.Generic;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Offline art inspection: triangle/box SAT and an upward roof ray, not game physics.</summary>
public sealed class MeshClearanceQuery
{
    private sealed record Geometry(Aabb Bounds,Vector3[] Faces);
    private static readonly Dictionary<ulong,Vector3[]> LocalFaces=new();
    private readonly List<Geometry> _geometry=[];
    public MeshClearanceQuery(Node root)=>Collect(root);
    private void Collect(Node node)
    {
        if(node is Node3D spatial && !spatial.Visible)return;
        if(node is MeshInstance3D mesh && mesh.Mesh!=null)
        {
            var id=mesh.Mesh.GetInstanceId();
            if(!LocalFaces.TryGetValue(id,out var faces)){faces=mesh.Mesh.GetFaces();LocalFaces[id]=faces;}
            var world=new Vector3[faces.Length];var transform=mesh.GlobalTransform;
            for(var i=0;i<faces.Length;i++)world[i]=transform*faces[i];
            _geometry.Add(new Geometry(transform*mesh.GetAabb(),world));
        }
        foreach(var child in node.GetChildren())Collect(child);
    }
    public bool IsFree(Vector3 feet,float radius,float halfWidthRatio=.22f,float heightRatio=.72f,bool roof=true)
    {
        var half=radius*halfWidthRatio;
        var box=new Aabb(feet+new Vector3(-half,radius*.12f,-half),new Vector3(half*2,radius*heightRatio,half*2));
        var rayOrigin=feet+Vector3.Up*radius*.30f;
        foreach(var mesh in _geometry)
        {
            var body=mesh.Bounds.Intersects(box);
            var ray=roof && rayOrigin.X>=mesh.Bounds.Position.X && rayOrigin.X<=mesh.Bounds.End.X &&
                rayOrigin.Z>=mesh.Bounds.Position.Z && rayOrigin.Z<=mesh.Bounds.End.Z && mesh.Bounds.End.Y>rayOrigin.Y;
            if(!body && !ray)continue;
            for(var i=0;i+2<mesh.Faces.Length;i+=3)
            {
                var a=mesh.Faces[i];var b=mesh.Faces[i+1];var c=mesh.Faces[i+2];
                if(body && TriangleBox(a,b,c,box))return false;
                if(ray && RayTriangle(rayOrigin,Vector3.Up,a,b,c))return false;
            }
        }
        return true;
    }
    private static bool RayTriangle(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c)
    {
        var e1=b-a;var e2=c-a;var p=direction.Cross(e2);var det=e1.Dot(p);
        if(Math.Abs(det)<.0000001f)return false;
        var inverse=1/det;var t=origin-a;var u=t.Dot(p)*inverse;
        if(u<0 || u>1)return false;
        var q=t.Cross(e1);var v=direction.Dot(q)*inverse;
        if(v<0 || u+v>1)return false;
        return e2.Dot(q)*inverse>.0001f;
    }
    private static bool TriangleBox(Vector3 a,Vector3 b,Vector3 c,Aabb box)
    {
        var centre=box.GetCenter();var half=box.Size*.5f;a-=centre;b-=centre;c-=centre;
        var min=a.Min(b).Min(c);var max=a.Max(b).Max(c);
        if(min.X>half.X || max.X< -half.X || min.Y>half.Y || max.Y< -half.Y || min.Z>half.Z || max.Z< -half.Z)return false;
        bool Separated(Vector3 axis)
        {
            if(axis.LengthSquared()<.0000000001f)return false;
            var x=a.Dot(axis);var y=b.Dot(axis);var z=c.Dot(axis);
            var r=half.X*Math.Abs(axis.X)+half.Y*Math.Abs(axis.Y)+half.Z*Math.Abs(axis.Z);
            return Math.Min(x,Math.Min(y,z))>r || Math.Max(x,Math.Max(y,z))< -r;
        }
        var e1=b-a;var e2=c-b;var e3=a-c;
        if(Separated(e1.Cross(e2)))return false;
        foreach(var edge in new[]{e1,e2,e3})
            if(Separated(edge.Cross(Vector3.Right)) || Separated(edge.Cross(Vector3.Up)) || Separated(edge.Cross(Vector3.Back)))return false;
        return true;
    }
}
