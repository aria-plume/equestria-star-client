using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Release managed material handles in order, after all scene instances detach.</summary>
public static class PaperMaterialLifetime
{
    private static readonly Dictionary<ulong,Material> Handles=new();
    private static bool _active;
    public static void BeginGallerySession()=>_active=true;
    public static void Remember(Material? material)
    {if(_active && material!=null && GodotObject.IsInstanceValid(material))Handles.TryAdd(material.GetInstanceId(),material);}
    private static void MeshMaterials(Mesh? mesh)
    {if(mesh!=null)for(var s=0;s<mesh.GetSurfaceCount();s++)Remember(mesh.SurfaceGetMaterial(s));}
    public static void Collect(Node root)
    {
        if(!_active)return;
        foreach(var node in root.FindChildren("*","Node",true,false).Prepend(root))
        {
            if(node is GeometryInstance3D geometry){Remember(geometry.MaterialOverride);Remember(geometry.MaterialOverlay);}
            if(node is MeshInstance3D mesh && mesh.Mesh!=null)
            {MeshMaterials(mesh.Mesh);for(var s=0;s<mesh.Mesh.GetSurfaceCount();s++)Remember(mesh.GetActiveMaterial(s));}
            if(node is MultiMeshInstance3D instances)MeshMaterials(instances.Multimesh?.Mesh);
            if(node.HasMeta("paper_source_mesh"))MeshMaterials(node.GetMeta("paper_source_mesh").AsGodotObject() as Mesh);
        }
    }
    public static int ReleaseDetached()
    {
        // Exit only: native scene objects have freed and render work has completed. Dispose
        // each managed wrapper once; native mesh owners retain any still-shared resource.
        var materials=Handles.Values.ToArray();Handles.Clear();_active=false;var count=0;
        foreach(var material in materials)if(GodotObject.IsInstanceValid(material)){material.Dispose();count++;}
        return count;
    }
}
