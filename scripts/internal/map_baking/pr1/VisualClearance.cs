using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Conservative broad-phase check used by art placement and inspection only.</summary>
public static class VisualClearance
{
    public static bool Intersects(Node node,Aabb volume)
    {
        if(node is Node3D spatial && !spatial.Visible)return false;
        if(node is MeshInstance3D mesh && mesh.Mesh!=null)
            if((mesh.GlobalTransform*mesh.GetAabb()).Intersects(volume))return true;
        foreach(var child in node.GetChildren())if(Intersects(child,volume))return true;
        return false;
    }
}
