using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Very small low foliage keeps its geometry and receiving light, but skips shadow-map passes.</summary>
public static class SceneryShadowBudget
{
    public static int Apply(Node node,float radius)
    {
        var count=0;
        if(node is MeshInstance3D mesh && mesh.Mesh!=null && mesh.IsVisibleInTree() &&
            mesh.CastShadow!=GeometryInstance3D.ShadowCastingSetting.Off)
        {
            var size=(mesh.GlobalTransform*mesh.GetAabb()).Size;
            if(size.Y<radius*.14f && new Vector2(size.X,size.Z).Length()<radius*.90f)
            {mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;count++;}
        }
        foreach(var child in node.GetChildren())count+=Apply(child,radius);
        return count;
    }
}
