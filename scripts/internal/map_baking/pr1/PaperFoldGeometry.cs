using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>One permanent presentation adaptation, never a distance-based mesh replacement.</summary>
public static class PaperFoldGeometry
{
    private static readonly Dictionary<ulong,ArrayMesh> Crowns=new();
    private static readonly Dictionary<(ulong,bool),ArrayMesh> Ridges=new();
    private static readonly Dictionary<ulong,(ArrayMesh Bark,ArrayMesh Leaves)> WindParts=new();
    public static bool Enabled=true;
    public static void Clear(){Crowns.Clear();Ridges.Clear();WindParts.Clear();}
    public static void PrepareWind(Node root)
    {
        foreach(var mesh in root.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().ToArray())
        {
            if(mesh.Mesh==null || !mesh.Mesh.HasMeta("paper_canopy_first_surface") || mesh.HasNode("PaperCanopyWind"))continue;
            var source=mesh.Mesh;var key=source.GetInstanceId();
            if(!WindParts.TryGetValue(key,out var parts))
            {
                var bark=new ArrayMesh();var leaves=new ArrayMesh();var first=source.GetMeta("paper_canopy_first_surface").AsInt32();
                for(var i=0;i<source.GetSurfaceCount();i++)
                {var to=i<first?bark:leaves;to.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,source.SurfaceGetArrays(i));to.SurfaceSetMaterial(to.GetSurfaceCount()-1,source.SurfaceGetMaterial(i));}
                parts=(bark,leaves);WindParts[key]=parts;
            }
            var bounds=parts.Leaves.GetAabb();var pivot=bounds.GetCenter();pivot.Y=bounds.Position.Y;
            mesh.Mesh=parts.Bark;
            var hinge=new Node3D {Name="PaperCanopyWind",Position=pivot};mesh.AddChild(hinge);
            hinge.SetMeta("paper_wind_hinge",true);
            hinge.AddChild(new MeshInstance3D {Name="LargePaperFolds",Mesh=parts.Leaves,Position=-pivot,ExtraCullMargin=.10f});
        }
    }
    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {if(node is MeshInstance3D own && own.Mesh!=null)yield return own;foreach(var n in node.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())if(n.Mesh!=null)yield return n;}
    public static void Apply(Node root)
    {
        if(!Enabled)return;
        foreach(var node in new[]{root}.Concat(root.FindChildren("*","Node3D",true,false).Cast<Node>()))
        {
            if(!node.HasMeta("environment_asset") || node.HasMeta("paper_fold_adapted"))continue;
            var id=node.GetMeta("environment_asset").AsString();
            if(id is "VEG006" or "VEG007" or "VEG008" or "VEG009")
                foreach(var mesh in Meshes(node))
                {
                    var original=mesh.Mesh;var key=original.GetInstanceId();
                    if(!Crowns.TryGetValue(key,out var adapted)){adapted=Crown(original);Crowns[key]=adapted;}
                    mesh.SetMeta("paper_source_mesh",original);mesh.Mesh=adapted;
                }
            else if(id is "TER101" or "TER102" or "TER103")
            {
                var primary=node.GetNodeOrNull("part01");if(primary==null)continue;
                foreach(var mesh in Meshes(primary))
                {
                    var original=mesh.Mesh;var key=(original.GetInstanceId(),id=="TER103");
                    if(!Ridges.TryGetValue(key,out var adapted)){adapted=Ridge(original,id=="TER103");Ridges[key]=adapted;}
                    mesh.SetMeta("paper_source_mesh",original);mesh.Mesh=adapted;
                }
                if(id=="TER103" && node.GetNodeOrNull<Node3D>("part02") is { } oldCap)oldCap.Visible=false;
            }
            else continue;
            node.SetMeta("paper_fold_adapted",true);
        }
    }
    private static ArrayMesh Crown(Mesh source)
    {
        var output=new ArrayMesh();var vertices=new List<Vector3>();var parent=new List<int>();
        var lookup=new Dictionary<(int,int,int),int>();Material? leaf=null;
        int Find(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        int Key(Vector3 p)
        {
            var key=((int)Mathf.Round(p.X*10000),(int)Mathf.Round(p.Y*10000),(int)Mathf.Round(p.Z*10000));
            if(lookup.TryGetValue(key,out var i))return i;i=vertices.Count;lookup[key]=i;vertices.Add(p);parent.Add(i);return i;
        }
        for(var s=0;s<source.GetSurfaceCount();s++)
        {
            var material=source.SurfaceGetMaterial(s);var a=source.SurfaceGetArrays(s);
            if(material==null || !material.ResourceName.Contains("EverfreeCanopy"))
            {output.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,a);output.SurfaceSetMaterial(output.GetSurfaceCount()-1,material);continue;}
            leaf??=material;var points=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            if(indices.Length==0)indices=Enumerable.Range(0,points.Length).ToArray();
            for(var i=0;i<indices.Length;i+=3)
            {var x=Key(points[indices[i]]);var y=Key(points[indices[i+1]]);var z=Key(points[indices[i+2]]);parent[Find(y)]=Find(x);parent[Find(z)]=Find(x);}
        }
        if(vertices.Count==0)return (ArrayMesh)source;
        var back=new StandardMaterial3D {AlbedoColor=(leaf as BaseMaterial3D)?.AlbedoColor.Lightened(.16f)??new Color("548a88"),Roughness=.88f};
        var top=Tool(leaf);var underside=Tool(back);
        foreach(var group in Enumerable.Range(0,vertices.Count).GroupBy(Find))
        {
            var pts=group.Select(i=>vertices[i]).ToArray();var lo=pts.Aggregate((a,b)=>a.Min(b));var hi=pts.Aggregate((a,b)=>a.Max(b));var size=hi-lo;var centre=(lo+hi)*.5f;
            var ring=new[]{new Vector2(-.5f,0),new Vector2(-.28f,-.5f),new Vector2(.26f,-.47f),new Vector2(.5f,.03f),new Vector2(.24f,.48f),new Vector2(-.25f,.5f)}
                .Select(p=>new Vector3(centre.X+p.X*size.X,lo.Y+size.Y*.30f,centre.Z+p.Y*size.Z)).ToArray();
            var peak=new Vector3(centre.X-size.X*.06f,hi.Y,centre.Z-size.Z*.02f);var bottom=new Vector3(centre.X,lo.Y,centre.Z);
            for(var i=0;i<ring.Length;i++)
            {
                var a=ring[i];var b=ring[(i+1)%ring.Length];var down=Vector3.Down*size.Y*.065f;
                Face(top,peak,a,b,centre);Face(underside,a,b,a+down,centre);Face(underside,b,b+down,a+down,centre);Face(underside,b+down,bottom,a+down,centre);
            }
        }
        output.SetMeta("paper_canopy_first_surface",output.GetSurfaceCount());
        top.Commit(output);underside.Commit(output);return output;
    }
    private static ArrayMesh Ridge(Mesh source,bool snow)
    {
        var box=source.GetAabb();var lo=box.Position;var size=box.Size;var c=box.GetCenter();
        var top=Tool(source.SurfaceGetMaterial(0));var snowTop=Tool(new StandardMaterial3D {AlbedoColor=new Color("dce7ed"),Roughness=.9f});
        var xs=new[]{-.5f,-.25f,0,.27f,.5f};var heights=new[]{.08f,1f,.48f,.76f,.10f};var widths=new[]{.32f,.90f,1f,.86f,.30f};
        for(var i=0;i<4;i++)
        {
            Vector3 Peak(int j)=>new(c.X+xs[j]*size.X,lo.Y+heights[j]*size.Y,c.Z+(j%2==0?.06f:-.05f)*size.Z);
            foreach(var sign in new[]{-1,1})
            {
                var a=Peak(i);var b=Peak(i+1);var d=new Vector3(a.X,lo.Y,c.Z+sign*size.Z*.5f*widths[i]);var e=new Vector3(b.X,lo.Y,c.Z+sign*size.Z*.5f*widths[i+1]);
                if(snow)
                {
                    var sa=a.Lerp(d,.34f);var sb=b.Lerp(e,.34f);
                    Face(snowTop,a,b,sa,c);Face(snowTop,b,sb,sa,c);Face(top,sa,sb,d,c);Face(top,sb,e,d,c);
                }
                else {Face(top,a,b,d,c);Face(top,b,e,d,c);}
            }
        }
        var mesh=new ArrayMesh();top.Commit(mesh);if(snow)snowTop.Commit(mesh);return mesh;
    }
    private static SurfaceTool Tool(Material? material)
    {var t=new SurfaceTool();t.Begin(Mesh.PrimitiveType.Triangles);if(material!=null)t.SetMaterial(material);return t;}
    private static void Face(SurfaceTool t,Vector3 a,Vector3 b,Vector3 c,Vector3 inside)
    {
        var n=(b-a).Cross(c-a).Normalized();if(n.Dot((a+b+c)/3-inside)<0){(b,c)=(c,b);n=-n;}
        t.SetNormal(n);t.AddVertex(a);t.AddVertex(c);t.AddVertex(b);
    }
}
