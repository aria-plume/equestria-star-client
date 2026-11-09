using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Separate only the authored Twilight banner cloth. Preserve all original slots, UVs and poles.</summary>
public static class PaperFlagMotion
{
    private sealed record Piece(ArrayMesh Mesh,Vector3 Hinge);
    private sealed record Split(ArrayMesh Fixed,Piece[] Cloth);
    private sealed record Face(int Surface,int A,int B,int C,int Vertex);
    private static readonly Dictionary<ulong,Split> Cache=new();
    public static void Clear()=>Cache.Clear();
    public static void Prepare(Node root)
    {
        foreach(var mesh in root.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().ToArray())
        {
            if(mesh.Mesh==null || !mesh.Name.ToString().Contains("Twilight_FoldedBanners"))continue;
            var source=mesh.Mesh;var key=source.GetInstanceId();
            if(!Cache.TryGetValue(key,out var split)){split=Extract(source);Cache[key]=split;}
            if(split.Cloth.Length==0)continue;
            mesh.SetMeta("paper_source_mesh",source);mesh.Mesh=split.Fixed.GetSurfaceCount()>0?split.Fixed:null;
            for(var i=0;i<split.Cloth.Length;i++)
            {
                var part=split.Cloth[i];var hinge=new Node3D {Name="PaperFlagWind_"+i,Position=part.Hinge};mesh.AddChild(hinge);
                hinge.SetMeta("paper_wind_hinge",true);hinge.SetMeta("paper_wind_flag",true);
                hinge.AddChild(new MeshInstance3D {Name="OriginalBannerCloth",Mesh=part.Mesh,Position=-part.Hinge,ExtraCullMargin=.035f});
            }
        }
    }
    private static Split Extract(Mesh source)
    {
        var fixedMesh=new ArrayMesh();var arrays=new Dictionary<int,Godot.Collections.Array>();
        var points=new List<Vector3>();var parents=new List<int>();var keys=new Dictionary<(int,int,int),int>();var faces=new List<Face>();
        int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
        int Index(Vector3 p){var k=((int)Mathf.Round(p.X*10000),(int)Mathf.Round(p.Y*10000),(int)Mathf.Round(p.Z*10000));if(keys.TryGetValue(k,out var n))return n;n=points.Count;keys[k]=n;points.Add(p);parents.Add(n);return n;}
        for(var s=0;s<source.GetSurfaceCount();s++)
        {
            var a=source.SurfaceGetArrays(s);var material=source.SurfaceGetMaterial(s);
            if(material==null || !material.ResourceName.ToString().Contains("Paper_Flag"))
            {fixedMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,a);fixedMesh.SurfaceSetMaterial(fixedMesh.GetSurfaceCount()-1,material);continue;}
            arrays[s]=a;var vertices=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            if(indices.Length==0)indices=Enumerable.Range(0,vertices.Length).ToArray();
            for(var i=0;i<indices.Length;i+=3)
            {var x=Index(vertices[indices[i]]);var y=Index(vertices[indices[i+1]]);var z=Index(vertices[indices[i+2]]);parents[Find(y)]=Find(x);parents[Find(z)]=Find(x);faces.Add(new Face(s,indices[i],indices[i+1],indices[i+2],x));}
        }
        var pieces=new List<Piece>();
        foreach(var group in faces.GroupBy(f=>Find(f.Vertex)))
        {
            var output=new ArrayMesh();
            foreach(var slot in group.GroupBy(f=>f.Surface))
            {
                var a=arrays[slot.Key];var vertices=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var uv=a[(int)Mesh.ArrayType.TexUV].VariantType==Variant.Type.Nil?[]:a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var color=a[(int)Mesh.ArrayType.Color].VariantType==Variant.Type.Nil?[]:a[(int)Mesh.ArrayType.Color].AsColorArray();
                var t=new SurfaceTool();t.Begin(Mesh.PrimitiveType.Triangles);t.SetMaterial(source.SurfaceGetMaterial(slot.Key));
                foreach(var face in slot)foreach(var index in new[]{face.A,face.B,face.C})
                {if(normals.Length>index)t.SetNormal(normals[index]);if(uv.Length>index)t.SetUV(uv[index]);if(color.Length>index)t.SetColor(color[index]);t.AddVertex(vertices[index]);}
                t.Commit(output);
            }
            var box=output.GetAabb();var pivot=box.GetCenter();pivot.Y=box.End.Y;
            pieces.Add(new Piece(output,pivot));
        }
        return new Split(fixedMesh,pieces.ToArray());
    }
}
