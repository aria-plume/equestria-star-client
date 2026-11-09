using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Internal.MapBaking.Pr1;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Low, purposeful site surfaces; no cells, collision proxies or logical connections.</summary>
public static class PaperPlaceComposition
{
    public sealed record Site(string Cell,string Asset,string Relation,int Flowers,float MaxHeight);
    public static Node3D Build(Node3D parent,IReadOnlyCollection<HexCell3D> cells)
    {
        var root=new Node3D {Name="PaperPlaceComposition"};parent.AddChild(root);
        var paper=new SurfaceTool();paper.Begin(Mesh.PrimitiveType.Triangles);
        paper.SetMaterial(new StandardMaterial3D {AlbedoColor=Colors.White,Roughness=.94f,VertexColorUseAsAlbedo=true,CullMode=BaseMaterial3D.CullModeEnum.Disabled});
        var sites=new List<Site>();
        void Tri(Vector3 a,Vector3 b,Vector3 c,Color color)
        {var normal=(b-a).Cross(c-a).Normalized();if(normal.Y<0){(b,c)=(c,b);normal=-normal;}paper.SetNormal(normal);paper.SetColor(color.SrgbToLinear());paper.AddVertex(a);paper.AddVertex(c);paper.AddVertex(b);}
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color col){Tri(a,b,c,col);Tri(a,c,d,col);}
        void Ribbon(Vector3[] points,float width,Color color)
        {
            for(var i=0;i<points.Length-1;i++)
            {var d=(points[i+1]-points[i]).Normalized();var side=new Vector3(-d.Z,0,d.X)*width*.5f;Quad(points[i]-side,points[i]+side,points[i+1]+side,points[i+1]-side,color);}
        }
        void Bank(Vector3 centre,Vector3 along,float length,float width,float height,Color col)
        {
            var across=new Vector3(-along.Z,0,along.X);var p=centre+Vector3.Up*height;
            var corners=new[]{centre-along*length*.5f-across*width*.5f,centre+along*length*.5f-across*width*.45f,centre+along*length*.45f+across*width*.5f,centre-along*length*.45f+across*width*.5f};
            for(var i=0;i<4;i++)Tri(p,corners[i],corners[(i+1)%4],i%2==0?col:col.Darkened(.055f));
        }
        foreach(var cell in cells)
        {
            var asset=OrigamiArtCatalog.Resolve(cell.Data!);if(asset==null || cell.Data!.BoardCode=="L")continue;
            var id=asset.Id;var r=cell.Radius;var d=r*2;var centre=cell.GlobalPosition;var seed=OrigamiMapPresentation.Seed(cell.Data.CellCode);
            if(id=="everfree")
            {
                // Follow the existing fixed tree pockets. No new tall trees in the token lane.
                var vegetation=cell.GetNodeOrNull<Node3D>("VisualRoot/DecorationRoot/LocalVegetation");
                if(vegetation!=null)foreach(var tree in vegetation.GetChildren().OfType<Node3D>().Where(n=>n.HasMeta("zoom_main_tree")))
                {var p=tree.GlobalPosition;p.Y=.006f*d;var tangent=new Vector3(-p.Z+centre.Z,0,p.X-centre.X).Normalized();Bank(p,tangent,d*.20f,d*.09f,d*.018f,new Color("426d43"));}
                continue;
            }
            if(id is "grass-light" or "grass-dark" or "sand" or "stone" or "snow" or "crystal-ground" or "rainbow-house")continue;
            var visual=cell.GetNodeOrNull<Node3D>("VisualRoot/CustomVisualRoot/OrigamiAsset");if(visual==null)continue;
            var forward=visual.GlobalBasis*Vector3.Back;forward.Y=0;forward=forward.Normalized();var side=new Vector3(-forward.Z,0,forward.X);
            var forest=id is "zecora" or "old-castle" or "harmony-tree";
            var urban=id is "manehattan-district" or "liberty-statue" or "hotel-candidate";
            var crystal=id is "crystal-castle" or "crystal-empire" or "crystal-market" or "crystal-stadium";
            var dry=id is "appleloosa" or "baltimare" or "griffonstone";
            var harsh=id is "dragon-lands" or "tartarus";
            var snow=id=="yakyakistan";
            var pathColor=new Color(harsh?"706b79":dry?"c6ab76":snow?"d1dce0":urban?"bdc7c5":forest?"a9a97b":crystal?"c9dce6":id=="canterlot"?"eee2bd":"e2d6ad");
            // Visual approach follows the source assembly's front; remains inside its original hex.
            Vector3 P(float along,float across,float height=.007f)=>centre+forward*(r*along)+side*(r*across)+Vector3.Up*(d*height);
            var turn=id=="zecora"?.18f:id=="old-castle"?-.13f:0;
            Ribbon([P(.82f,turn),P(.60f,-turn),P(.35f,0)],d*(urban?.18f:id=="fashion-shop"?.15f:.095f),pathColor);
            var flowers=0;
            var maximumHeight=d*.044f;
            var count=forest || dry || harsh || snow?3:crystal?4:urban?2:6;
            for(var i=0;i<count;i++)
            {
                var sign=i%2==0?-1:1;var p=P(.40f+(i/2)*.13f,sign*(id=="fashion-shop"?.30f:.27f));
                // Keep meaningful low edges off the stable pawn footprint.
                if(new Vector2(p.X-PawnArtLayout.WorldAnchor(cell).X,p.Z-PawnArtLayout.WorldAnchor(cell).Z).Length()<r*.32f)continue;
                Bank(p,forward,d*.105f,d*.065f,d*.015f,new Color(harsh?"746876":dry?"9b9057":snow?"b1cbd4":forest?"3a6246":crystal?"b4d2df":"72975a"));
                if(!urban && !crystal && !forest && !dry && !harsh && !snow)
                    for(var j=0;j<3;j++)
                    {
                        var flower=p+side*(j-1)*d*.026f+Vector3.Up*d*.020f;var col=new Color((i+j)%3==0?"f3d979":(i+j)%3==1?"f2b6d2":"ece9d8");
                        for(var k=0;k<4;k++){var a=k*Mathf.Tau/4;var v=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*d*.021f;var t=new Vector3(-v.Z,0,v.X)*.45f;Tri(flower+Vector3.Up*d*.009f,flower+v-t,flower+v+t,col);}flowers++;
                    }
            }
            if(urban)
            {
                // Street block paving and corners, not invented station buildings.
                Ribbon([P(-.65f,.66f),P(.18f,.66f),P(.68f,.54f)],d*.12f,new Color("b1bcba"));
                for(var i=0;i<4;i++)Ribbon([P(.34f+i*.12f,-.18f),P(.34f+i*.12f,.18f)],d*.012f,new Color("d5d7c8"));
            }
            if(id=="fluttershy")
            {Bank(P(-.30f,.48f,0),forward,d*.40f,d*.18f,d*.028f,new Color("8daa59"));Ribbon([P(-.51f,-.56f),P(-.18f,-.61f),P(.12f,-.55f)],d*.055f,new Color("91c7c6"));}
            if(forest)
            {
                // Three distinct clearings, always below the body collision envelope.
                var direction=id=="harmony-tree"?side:forward;
                Bank(P(-.45f,-.25f,0),direction,d*(id=="old-castle"?.38f:.27f),d*.12f,d*.035f,new Color(id=="old-castle"?"7b8985":"53785b"));
                if(id=="old-castle")for(var i=0;i<3;i++)Bank(P(-.35f+i*.16f,.55f),side,d*.10f,d*.07f,d*.035f,new Color("b2b3a4"));
            }
            if(crystal)
            {for(var i=0;i<4;i++){var p=P(-.55f+i*.30f,-.54f,0);Bank(p,side,d*.16f,d*.10f,d*(.018f+i%2*.012f),new Color(i%2==0?"8dbbd7":"b8addd"));}}
            if(id is "canterlot" or "canterlot-market")
            {
                Ribbon([P(-.65f,-.45f),P(-.65f,.45f)],d*.19f,new Color("e6d7ad"));
                var wall=P(-.72f,0,0);
                var anchor=PawnArtLayout.WorldAnchor(cell);
                if(new Vector2(wall.X-anchor.X,wall.Z-anchor.Z).Length()>r*.60f)
                {Bank(wall,side,d*.67f,d*.17f,d*.40f,new Color("a4a9c0"));maximumHeight=d*.40f;}
            }
            sites.Add(new Site(cell.Data.CellCode,id,forest?id+"-clearing":urban?"street-block":crystal?"crystal-core":dry?"dry-approach":harsh?"basalt-approach":snow?"snow-entry":id=="fluttershy"?"meadow-to-forest":id is "canterlot" or "canterlot-market"?"mountain-platform":"entrance-garden",flowers,maximumHeight));
        }
        root.AddChild(new MeshInstance3D {Name="LowFoldedSiteSurfaces",Mesh=paper.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        root.SetMeta("sites_json",System.Text.Json.JsonSerializer.Serialize(sites));root.SetMeta("site_count",sites.Count);
        ConceptPaperStyle.Apply(root);return root;
    }
}
