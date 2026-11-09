using System;
using System.Linq;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Continuous folded shore following the actual background outline, without gameplay cells.</summary>
public static class CoastalShoreBuilder
{
    public static Node3D Add(Node3D parent,Vector2[] outline,Vector2 centre,float radius)
    {
        var root=new Node3D {Name="FoldedCoast"};parent.AddChild(root);
        var n=outline.Length;
        var shore=new Vector3[n];var sand=new Vector3[n];var foam=new Vector3[n];var shallows=new Vector3[n];
        for(var i=0;i<n;i++)
        {
            var direction=(outline[i]-centre).Normalized();var angle=i*Mathf.Tau/n;
            // Broad beaches alternate with narrow headlands; joins share the same vertices.
            var beach=radius*(.30f+.20f*Mathf.Sin(angle*3+.8f)+.07f*Mathf.Sin(angle*11));
            var reef=radius*(.65f+.19f*Mathf.Sin(angle*5-.3f));
            Vector3 Point(float distance,float y)=>new(outline[i].X+direction.X*distance,y*radius,outline[i].Y+direction.Y*distance);
            shore[i]=Point(.012f*radius,-.45f);
            sand[i]=Point(beach,-.72f);
            foam[i]=Point(beach+radius*.035f,-.725f);
            shallows[i]=Point(beach+reef,-.815f);
        }
        Band(root,"SlopingSand",shore,sand,new Color("d8c68d"),.075f);
        Band(root,"ShoreFoam",sand,foam,new Color("e8edce"),.025f);
        Band(root,"ShallowFacets",foam,shallows,new Color("6ba89d"),.08f);
        for(var i=3;i<n;i+=11)
        {
            var p=shore[i].Lerp(sand[i],.55f);p.Y+=radius*.035f;
            EnvironmentArtCatalog.PlaceParts(root,"DEC103",[i%2==0?"part02":"part04"],p,radius*(.6f+i%3*.13f),i*43);
        }
        root.SetMeta("shore_samples",n);
        return root;
    }

    private static void Band(Node3D root,string name,Vector3[] inner,Vector3[] outer,Color color,float variation)
    {
        if(inner.Length!=outer.Length || inner.Length<3)throw new ArgumentException("Invalid closed coast ring");
        var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetMaterial(OrigamiMapPresentation.PaperMaterial(color));
        void Triangle(Vector3 a,Vector3 b,Vector3 c,int seed)
        {
            var normal=(b-a).Cross(c-a).Normalized();
            if(normal.Y<0){(b,c)=(c,b);normal=-normal;}
            var tint=1+Mathf.Sin(seed*2.31f)*variation;
            tool.SetColor(new Color(tint,tint,tint));tool.SetNormal(normal);
            tool.AddVertex(a);tool.AddVertex(PaperWorldBackground.Enabled?c:b);tool.AddVertex(PaperWorldBackground.Enabled?b:c);
        }
        for(var i=0;i<inner.Length;i++)
        {
            var j=(i+1)%inner.Length;
            Triangle(inner[i],outer[i],inner[j],i*2);
            Triangle(inner[j],outer[i],outer[j],i*2+1);
        }
        root.AddChild(new MeshInstance3D {Name=name,Mesh=tool.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }
}
