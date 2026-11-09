using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Internal.MapBaking.Pr1;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Small opaque root seats against measured terrain; never a black floating shadow plane.</summary>
public static class PaperGroundContact
{
    public static void Apply(HexCell3D cell)
    {
        var vegetation=cell.GetNodeOrNull<Node3D>("VisualRoot/DecorationRoot/LocalVegetation");
        if(vegetation==null || cell.Data!.BoardCode=="L")return;
        var terrain=cell.GetNodeOrNull<Node3D>("VisualRoot/CustomVisualRoot/OrigamiAsset/Terrain");
        foreach(var tree in vegetation.GetChildren().OfType<Node3D>().Where(n=>n.HasMeta("zoom_main_tree")).ToArray())
        {
            var point=tree.GlobalPosition;var height=Height(terrain,point,cell.GlobalPosition.Y);
            var gap=point.Y-height;
            tree.SetMeta("paper_ground_gap_before",gap);
            if(gap<.001f || gap>cell.Radius*.08f)continue;
            var mesh=new CylinderMesh {TopRadius=cell.Radius*.035f,BottomRadius=cell.Radius*.065f,Height=gap+.002f,RadialSegments=6,Rings=1};
            var color=ConceptPaperStyle.Regions.GetValueOrDefault(cell.Data.BoardCode,new Color("557052")).Darkened(.05f);
            var seat=new MeshInstance3D {Name="PaperRootSeat",Mesh=mesh,MaterialOverride=OrigamiMapPresentation.PaperMaterial(color),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            vegetation.AddChild(seat);seat.GlobalPosition=new Vector3(point.X,height+gap*.5f,point.Z);seat.SetMeta("verified_receiver_height",height);tree.SetMeta("paper_ground_seated",true);
        }
    }
    private static float Height(Node3D? terrain,Vector3 point,float fallback)
    {
        if(terrain==null)return fallback;
        var best=float.NegativeInfinity;
        foreach(var mesh in terrain.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())
        {
            if(mesh.Mesh==null)continue;var faces=mesh.Mesh.GetFaces();
            for(var i=0;i+2<faces.Length;i+=3)
            {
                var a=mesh.GlobalTransform*faces[i];var b=mesh.GlobalTransform*faces[i+1];var c=mesh.GlobalTransform*faces[i+2];
                var v0=new Vector2(b.X-a.X,b.Z-a.Z);var v1=new Vector2(c.X-a.X,c.Z-a.Z);var v2=new Vector2(point.X-a.X,point.Z-a.Z);
                var det=v0.Cross(v1);if(Math.Abs(det)<.0000001f)continue;
                var u=v2.Cross(v1)/det;var v=v0.Cross(v2)/det;if(u<-.001f || v<-.001f || u+v>1.001f)continue;
                var y=a.Y+(b.Y-a.Y)*u+(c.Y-a.Y)*v;
                if(y<=point.Y+.001f)best=Math.Max(best,y);
            }
        }
        return float.IsNegativeInfinity(best)?fallback:best;
    }
}
