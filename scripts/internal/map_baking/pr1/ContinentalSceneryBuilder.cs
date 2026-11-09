using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Art-direction proposal around the supplied board, with no added gameplay cells.</summary>
public static class ContinentalSceneryBuilder
{
    private sealed record Mountain(string Id, Vector3 Position, float Scale, float Yaw, string Board = "");
    public static Aabb Build(Node3D parent, IReadOnlyCollection<HexCell3D> cells, Aabb bounds, float radius,IEnumerable<EquestriaStar.Api.MapEdgeDto>? edges=null)
    {
        if (cells.Count < 20 || EnvironmentArtCatalog.Entries.Count == 0) return bounds;
        var root = new Node3D { Name="ContinentalScenery" };
        parent.AddChild(root);
        var centre = new Vector2(bounds.GetCenter().X,bounds.GetCenter().Z);
        var islands = cells.Select(c=>(new Vector2(c.Position.X,c.Position.Z),radius*1.65f)).ToList();
        var mountains = new List<Mountain>();
        foreach (var group in cells.GroupBy(c=>c.Data!.BoardCode))
        {
            if (group.Key is "A" or "B") continue;
            var cluster = group.Aggregate(Vector3.Zero,(sum,c)=>sum+c.Position)/group.Count();
            var outward=(new Vector2(cluster.X,cluster.Z)-centre).Normalized();
            var direction=new Vector3(outward.X,0,outward.Y);
            var tangent=new Vector3(direction.Z,0,-direction.X);
            var edge=group.OrderByDescending(c=>c.Position.Dot(direction)).First().Position;
            var id=group.Key switch {"C"=>"TER103","D"=>"TER102","G"=>"TER104","H" or "J"=>"TER105",_=>"TER101"};
            var seed=OrigamiMapPresentation.Seed(group.Key);
            var anchor=edge+direction*radius*1.9f;
            anchor.Y=-radius*.21f;
            var yaw=Mathf.RadToDeg(Mathf.Atan2(outward.X,outward.Y))+90;
            mountains.Add(new Mountain(id,anchor,radius*(.96f+seed%5*.04f),yaw,group.Key));
            mountains.Add(new Mountain(id,anchor+tangent*radius*1.55f+direction*radius*.4f,radius*.64f,yaw+57,group.Key));
            mountains.Add(new Mountain(id,anchor-tangent*radius*1.35f+direction*radius*.65f,radius*.89f,yaw-38,group.Key));
        }
        foreach(var m in mountains) islands.Add((new Vector2(m.Position.X,m.Position.Z),radius*1.55f));
        // Reserve an outlying southern pocket for the provisional lake and its shore plants.
        var lake = new Vector2(centre.X-radius*.3f,bounds.End.Z+radius*1.35f);
        islands.Add((lake,radius*2.45f));
        var outline = RadialCoast(centre,islands,radius);
        Vector2[] Expand(float amount) => outline.Select(p=>p+(p-centre).Normalized()*amount).ToArray();
        OrigamiMapPresentation.AddPaperIsland(root,"ContinentalPaper",outline,-radius*.23f,radius*.19f,new Color(EquestriaStar.Internal.MapBaking.Pr1.ArtRenderProfile.IsSunlit?"4a7c3e":PaperWorldBackground.Enabled?"537b68":"829d59"));
        OrigamiMapPresentation.AddPaperIsland(root,"CreamPaperCore",Expand(radius*.035f),-radius*.42f,radius*.055f,new Color("eee0ba"));
        CoastalShoreBuilder.Add(root,outline,centre,radius);
        root.AddChild(new MeshInstance3D {Name="Sea",Position=new Vector3(centre.X,-radius*.83f,centre.Y),
            Mesh=new PlaneMesh {Size=new Vector2(200,200),Material=OrigamiMapPresentation.PaperMaterial(new Color("3e7c86"))}});
        foreach(var m in mountains)
        {
            // P9: these boards get their own themed scenery (PaperWorldBackground); the coast outline still uses the slots.
            if(EquestriaStar.Internal.MapBaking.Pr1.ArtRenderProfile.IsSunlit && m.Board is "D" or "E" or "F" or "G" or "H" or "I")continue;
            var model=EnvironmentArtCatalog.Place(root,m.Id,m.Position,m.Scale,m.Yaw);
            if(model==null)continue;
            model.Scale*=new Vector3(1,.85f+(OrigamiMapPresentation.Seed(model.Name)%5)*.09f,1);
            if(m.Id=="TER101" && !EquestriaStar.Internal.MapBaking.Pr1.ArtRenderProfile.IsSunlit)ToneHills(model);
            EquestriaStar.Internal.MapBaking.Pr1.PaperFoldGeometry.Apply(model);
        }

        // Vegetation occupies coast pockets only, independent of board interaction and token lanes.
        for(var i=0;i<outline.Length;i++)
        {
            var p=outline[i]-(outline[i]-centre).Normalized()*radius*.36f;
            if(cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Y).Length()<radius*1.4f))continue;
            if(mountains.Any(m=>new Vector2(m.Position.X-p.X,m.Position.Z-p.Y).Length()<radius*.9f))continue;
            EnvironmentArtCatalog.Place(root,i%4==0?"VEG003":i%2==0?"VEG001":"VEG002",new Vector3(p.X,-radius*.21f,p.Y),radius*(.28f+i%4*.055f),i*37);
            if(i%3==0)
                EnvironmentArtCatalog.Place(root,"VEG002",new Vector3(p.X+radius*.45f,-radius*.21f,p.Y+radius*.38f),radius*.27f,i*53);
        }
        BoardSeamFoundations.Add(root,cells,edges??[],radius);
        var west=new Vector3(bounds.Position.X-radius*3.5f,-radius*.81f,centre.Y+radius*2.5f);
        var east=new Vector3(bounds.End.X+radius*3.1f,-radius*.81f,centre.Y+radius*3);
        for(var i=0;i<3;i++)
        {
            EnvironmentArtCatalog.Place(root,"WAT002",west+new Vector3(radius*.2f,0,radius*(i-1)*2),radius*(.55f+i*.1f),25);
            EnvironmentArtCatalog.Place(root,"WAT002",east+new Vector3(radius*.2f,0,radius*(i-1)*2),radius*.6f,-10);
        }
        var boat=EnvironmentArtCatalog.Place(root,"BG005",west+new Vector3(-radius,0,0),radius*.82f,-25);
        var newSky=EnvironmentArtCatalog.Entries.ContainsKey("BG101");
        var balloon=EnvironmentArtCatalog.Place(root,newSky?"BG102":"BG004",new Vector3(bounds.End.X+radius*.1f,radius*2.2f,bounds.Position.Z+bounds.Size.Z*.23f),radius*(newSky?.60f:.65f),-18);
        if(balloon!=null)balloon.Name="BalloonEast";
        var westBalloon=EnvironmentArtCatalog.Place(root,newSky?"BG101":"BG004",new Vector3(bounds.Position.X-radius*.6f,radius*1.5f,bounds.Position.Z+bounds.Size.Z*.29f),radius*(newSky?.55f:1.05f),25);
        if(westBalloon!=null)westBalloon.Name="BalloonWest";
        var farBalloon=EnvironmentArtCatalog.Place(root,"BG004",new Vector3(centre.X+radius,radius*.8f,bounds.Position.Z-radius*1.3f),radius*.4f,-30);
        if(farBalloon!=null)farBalloon.Name="BalloonDistant";
        var birds=EnvironmentArtCatalog.Place(root,newSky?"BG108":"BG006",new Vector3(bounds.Position.X-radius,radius*2.3f,bounds.Position.Z+radius*3),radius*(newSky?.40f:.75f),15);
        var cloud=EnvironmentArtCatalog.Place(root,newSky?"BG103":"BG003",new Vector3(centre.X-radius*5,radius*1.8f,bounds.Position.Z-radius*.8f),radius*(newSky?.50f:.65f),0);
        AddDrift(boat,radius*.012f,1.1f,.2f);AddDrift(balloon,radius*.16f,.4f,1.0f);
        AddDrift(westBalloon,radius*.13f,.35f,2.1f);AddDrift(farBalloon,radius*.07f,.29f,3.2f);
        DisableShadows(balloon);DisableShadows(westBalloon);DisableShadows(farBalloon);
        AddDrift(birds,radius*.09f,.7f,.5f);AddDrift(cloud,radius*.035f,.25f,2);
        // Individual clouds avoid the display-kit arrangement and do not darken playable cells.
        for(var i=0;i<5;i++)
        {
            var angle=Mathf.Pi+(i/4f)*Mathf.Pi;
            var p=new Vector3(centre.X+Mathf.Cos(angle)*bounds.Size.X*.53f,radius*(1.0f+i%3*.30f),centre.Y+Mathf.Sin(angle)*bounds.Size.Z*.50f);
            for(var attempt=0;attempt<8 && cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Z).Length()<radius*1.9f);attempt++)
            {p.X=centre.X+(p.X-centre.X)*1.07f;p.Z=centre.Y+(p.Z-centre.Y)*1.07f;}
            var puff=newSky?EnvironmentArtCatalog.Place(root,"BG"+(103+i%3),p,radius*((i%3) switch {0=>.30f,1=>.40f,_=>.75f}),6+i%2*5):
                EnvironmentArtCatalog.PlaceParts(root,"BG003",["part0"+(i%3+1)],p,radius*(.7f+i%2*.28f),6+i%2*5);
            DisableShadows(puff);AddDrift(puff,radius*.045f,.20f+i*.025f,i);
        }
        DisableShadows(cloud);DisableShadows(birds);
        AddLake(root,lake,radius);
        PaperWorldBackground.Add(root,cells,bounds,radius,outline,centre);
        var snowCells=cells.Where(c=>c.Data!.BoardCode=="C").ToArray();
        if(snowCells.Length>0)
            EnvironmentArtCatalog.Place(root,"VEG103",new Vector3(snowCells.Average(c=>c.Position.X),-radius*.21f,
                snowCells.Min(c=>c.Position.Z)-radius*1.30f),radius*.70f,0);
        var forestCells=cells.Where(c=>c.Data!.BoardCode=="B").ToArray();
        if(forestCells.Length>0)
        {
            var side=forestCells.OrderBy(c=>c.Position.X).First().Position;
            EnvironmentArtCatalog.Place(root,"VEG102",new Vector3(side.X-radius*1.35f,-radius*.21f,side.Z),radius*.53f,90);
        }
        EnvironmentArtCatalog.Place(root,"VEG101",new Vector3(lake.X-radius*.7f,-radius*.21f,lake.Y+radius*1.6f),radius*.60f,10);
        EnvironmentArtCatalog.Place(root,"WAT008",new Vector3(bounds.Position.X-radius*1.6f,-radius*.83f,centre.Y),radius*.68f,125);
        var minX=Math.Min(bounds.Position.X-radius*4,outline.Min(p=>p.X)-radius);
        var maxX=Math.Max(bounds.End.X+radius*4,outline.Max(p=>p.X)+radius);
        var minZ=Math.Min(bounds.Position.Z-radius*3,outline.Min(p=>p.Y)-radius);
        var maxZ=Math.Max(bounds.End.Z+radius*4.1f,outline.Max(p=>p.Y)+radius);
        return new Aabb(new Vector3(minX,-radius*.9f,minZ),new Vector3(maxX-minX,Math.Max(bounds.Size.Y,radius*4.6f)+radius*.9f,maxZ-minZ));
    }

    private static void DisableShadows(Node? node)
    {
        if(node==null)return;
        if(node is GeometryInstance3D mesh)mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        foreach(var child in node.GetChildren())DisableShadows(child);
    }

    private static void ToneHills(Node node)
    {
        // The delivered lime-green folds are very bright at board scale. Keep all faceted
        // geometry and material variation, adapting only the instantiated material palette.
        if(node is MeshInstance3D mesh && mesh.Mesh!=null)
            for(var i=0;i<mesh.Mesh.GetSurfaceCount();i++)
                if(mesh.GetActiveMaterial(i) is StandardMaterial3D source)
                {
                    var material=(StandardMaterial3D)source.Duplicate();var c=source.AlbedoColor;
                    material.AlbedoColor=Color.FromHsv(c.H,c.S*.57f,c.V*.82f,c.A);
                    mesh.SetSurfaceOverrideMaterial(i,material);
                }
        foreach(var child in node.GetChildren())ToneHills(child);
    }

    private static void AddBoardChannels(Node3D root,IReadOnlyCollection<HexCell3D> cells,float radius)
    {
        // Visual water in existing gaps between regional boards; no logical cells/edges added.
        var all=cells.ToArray();var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(OrigamiMapPresentation.PaperMaterial(new Color("478e95")));
        float Support(Vector3 direction)
        {
            var best=0f;
            for(var k=0;k<6;k++){var a=Mathf.DegToRad(k*60+30);best=Math.Max(best,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)).Dot(direction)*radius);}
            return best;
        }
        var segments=0;
        for(var i=0;i<all.Length;i++)for(var j=i+1;j<all.Length;j++)
        {
            if(all[i].Data!.BoardCode==all[j].Data!.BoardCode)continue;
            var delta=all[j].Position-all[i].Position;var distance=delta.Length();
            if(distance>radius*2.9f || distance<radius*1.6f)continue;
            var direction=delta.Normalized();var gap=distance-Support(direction)*2;
            if(gap<radius*.07f)continue;
            var middle=(all[i].Position+all[j].Position)*.5f;middle.Y=-radius*.12f;
            var tangent=new Vector3(-direction.Z,0,direction.X)*radius*.68f;
            var across=direction*(gap*.5f+radius*.05f);
            var a=middle-tangent-across;var b=middle+tangent-across;var c=middle-tangent+across;var d=middle+tangent+across;
            surface.SetNormal(Vector3.Up);surface.AddVertex(a);surface.AddVertex(b);surface.AddVertex(c);surface.AddVertex(c);surface.AddVertex(b);surface.AddVertex(d);segments++;
        }
        if(segments>0)root.AddChild(new MeshInstance3D {Name="BoardSeamWater",Mesh=surface.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    private static Vector2[] RadialCoast(Vector2 centre,List<(Vector2 Position,float Radius)> islands,float radius)
    {
        var points=new List<Vector2>();
        for(var i=0;i<96;i++)
        {
            var angle=i*Mathf.Tau/96;
            var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            var extent=radius;
            foreach(var (position,r) in islands)
            {
                var delta=position-centre;
                var projection=delta.Dot(direction);
                var perpendicularSquared=delta.LengthSquared()-projection*projection;
                if(perpendicularSquared>r*r || projection<0)continue;
                extent=Math.Max(extent,projection+Mathf.Sqrt(Math.Max(0,r*r-perpendicularSquared)));
            }
            // Broad bays come from the board envelope; small irregular folds soften straight edges.
            extent+=radius*(.10f+.09f*Mathf.Sin(angle*7+.4f)+.055f*Mathf.Sin(angle*13));
            points.Add(centre+direction*extent);
        }
        return points.ToArray();
    }

    private static void AddLake(Node3D parent,Vector2 centre,float radius)
    {
        var root=new Node3D {Name="LakePlacementDraft",Position=new Vector3(centre.X,-radius*.20f,centre.Y)};
        root.SetMeta("art_status","provisional_background_lake");parent.AddChild(root);
        if(EnvironmentArtCatalog.Place(root,"WAT009",Vector3.Zero,radius*1.7f,0)!=null)
        {
            EnvironmentArtCatalog.Place(root,"VEG003",new Vector3(-radius*1.4f,0,-radius),radius*.28f,25);
            return;
        }
        var ring=Enumerable.Range(0,18).Select(i=>
        {
            var a=i*Mathf.Tau/18;var wobble=1+.10f*Mathf.Sin(i*2.3f);
            return new Vector2(Mathf.Cos(a)*radius*1.95f*wobble,Mathf.Sin(a)*radius*1.03f*wobble);
        }).ToArray();
        OrigamiMapPresentation.AddPaperIsland(root,"LakeBank",ring,0,radius*.04f,new Color("e8dbb3"));
        OrigamiMapPresentation.AddPaperIsland(root,"LakeWater",ring.Select(p=>p*.90f).ToArray(),radius*.012f,radius*.025f,new Color("88c2cc"));
        EnvironmentArtCatalog.Place(root,"VEG003",new Vector3(-radius*1.4f,0,-radius),radius*.28f,25);
        EnvironmentArtCatalog.Place(root,"VEG001",new Vector3(radius*1.25f,0,radius),radius*.22f,-20);
        EnvironmentArtCatalog.Place(root,"WAT002",new Vector3(0,radius*.03f,0),radius*.30f,15);
    }

    private static void AddDrift(Node3D? node,float amplitude,float speed,float phase)
    {
        // 静态烘焙不保留漂移脚本；保留参数作为可追溯的美术元数据。
        if(node==null)return;
        node.SetMeta("source_drift_amplitude",amplitude);
        node.SetMeta("source_drift_speed",speed);
        node.SetMeta("source_drift_phase",phase);
    }
}
