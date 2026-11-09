using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using EquestriaStar.Internal.MapBaking.Pr1;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Exterior-only, full 3D scenic proposal. No gameplay topology, physics or mesh LOD.</summary>
public static class PaperWorldBackground
{
    public static bool Enabled=true;
    public static void Add(Node3D scenery,IReadOnlyCollection<HexCell3D> cells,Aabb bounds,float r,Vector2[] outline,Vector2 centre)
    {
        if(!Enabled)return;
        var root=new Node3D {Name="V5WorldBackground"};scenery.AddChild(root);
        root.SetMeta("art_status","provisional_exterior_scenography_not_canonical_geography");
        var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetMaterial(new StandardMaterial3D {VertexColorUseAsAlbedo=true,Roughness=1});
        var groups=new List<object>();
        void Tri(Vector3 a,Vector3 b,Vector3 c,Color color)
        {var normal=(b-a).Cross(c-a).Normalized();if(normal.Y<0){(b,c)=(c,b);normal=-normal;}tool.SetNormal(normal);tool.SetColor(color.SrgbToLinear());tool.AddVertex(a);tool.AddVertex(c);tool.AddVertex(b);}
        void Ridge(Vector3 p,Vector3 dir,float width,float depth,float height,Color color,bool snow=false)
        {
            var side=new Vector3(dir.Z,0,-dir.X);var peak=p+dir*depth*.12f+Vector3.Up*height;
            var ring=Enumerable.Range(0,7).Select(i=>{var a=i*Mathf.Tau/7;return p+side*Mathf.Cos(a)*width+dir*Mathf.Sin(a)*depth;}).ToArray();
            for(var i=0;i<ring.Length;i++)
            {var tint=i%3==0?color.Lightened(.12f):i%3==1?color:color.Darkened(.07f);var j=(i+1)%ring.Length;
             if(snow){var a=peak.Lerp(ring[i],.36f);var b=peak.Lerp(ring[j],.36f);Tri(peak,a,b,new Color("e1e9e8"));Tri(a,ring[i],ring[j],tint);Tri(a,ring[j],b,tint);}
             else Tri(peak,ring[i],ring[j],tint);}
        }
        void LandShelf(Vector3 p,Vector3 dir,float width,float depth,Color color)
        {
            var side=new Vector3(dir.Z,0,-dir.X);var inner=new Vector3[10];var outer=new Vector3[10];
            for(var i=0;i<10;i++)
            {var a=i*Mathf.Tau/10;var offset=side*Mathf.Cos(a)*width+dir*Mathf.Sin(a)*depth;inner[i]=p+offset*.82f;inner[i].Y=-r*.24f;outer[i]=p+offset;outer[i].Y=-r*.94f;}
            var middle=p;middle.Y=-r*.24f;
            for(var i=0;i<10;i++){var j=(i+1)%10;Tri(middle,inner[i],inner[j],color);Tri(inner[i],outer[i],outer[j],color.Darkened(.08f));Tri(inner[i],outer[j],inner[j],color);}
        }
        Vector3 Exterior(Vector3 p,Vector3 dir,float footprint)
        {for(var i=0;i<32 && cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Z).Length()<r+footprint);i++)p+=dir*r*.55f;return p;}
        // A low folded apron outside the existing outline joins paper core, beaches and scenery.
        for(var i=0;i<outline.Length;i++)
        {
            var j=(i+1)%outline.Length;var a=outline[i];var b=outline[j];var da=(a-centre).Normalized();var db=(b-centre).Normalized();
            Vector3 P(Vector2 p,float y)=>new(p.X,y*r,p.Y);
            var col=ArtRenderProfile.IsSunlit?new Color(i/8%3==0?"568a45":i/8%3==1?"6a9750":"4d8044"):new Color(i/8%3==0?"8eac79":i/8%3==1?"a8ba84":"98b694");
            Tri(P(a,-.24f),P(b,-.24f),P(b+db*r*.6f,-.67f),col);Tri(P(a,-.24f),P(b+db*r*.6f,-.67f),P(a+da*r*.6f,-.67f),col);
        }
        foreach(var region in cells.GroupBy(c=>c.Data!.BoardCode))
        {
            var ids=region.Select(c=>OrigamiArtCatalog.Resolve(c.Data!)?.Id??"").ToHashSet();
            var kind=ids.Contains("dragon-lands")?"volcanic":ids.Contains("appleloosa")||ids.Contains("baltimare")?"dry":ids.Contains("yakyakistan")||ids.Contains("crystal-castle")?"snow":ids.Contains("canterlot")?"city-cliff":ids.Contains("manehattan-district")?"city-coast":ids.Contains("hippogriff")?"island":region.Key=="B"?"everfree":"meadow";
            if(ArtRenderProfile.IsSunlit && ids.Contains("baltimare") && !ids.Contains("appleloosa"))kind="city-coast";   // Baltimare is a harbour, not desert
            if(kind=="meadow" && region.Key!="A")continue;
            if(ArtRenderProfile.IsSunlit && kind=="island")continue;   // P9: the Hippogriff sea (AddThemedSurroundings) replaces the grey shelf
            var cluster=region.Aggregate(Vector3.Zero,(sum,c)=>sum+c.Position)/region.Count();
            var dir=new Vector3(cluster.X-centre.X,0,cluster.Z-centre.Y).Normalized();if(dir.LengthSquared()<.1f)dir=Vector3.Back;
            var side=new Vector3(dir.Z,0,-dir.X);var angle=Mathf.Atan2(dir.Z,dir.X);if(angle<0)angle+=Mathf.Tau;
            var point=outline[((int)Mathf.Round(angle/Mathf.Tau*outline.Length))%outline.Length];
            var near=Exterior(new Vector3(point.X,-r*.24f,point.Y)+dir*r*.65f,dir,r*1.45f);
            var color=ArtRenderProfile.IsSunlit
                ?new Color(kind switch {"snow"=>"dfe6f0","dry"=>"eab27c","volcanic"=>"5e4a55","city-cliff"=>"a894d2","city-coast"=>"8fbfb2","island"=>"8d9a8f","everfree"=>"4f8a5c",_=>"7fae5c"})
                :new Color(kind switch {"snow"=>"a5bad4","dry"=>"c7a071","volcanic"=>"82718d","city-cliff"=>"ada7c6","city-coast"=>"a5bcb5","everfree"=>"638f8f",_=>"96b48b"});
            var high=dir.Z<.1f?1.8f:.65f;
            if(kind=="city-cliff")high*=1.25f;
            if(ArtRenderProfile.IsSunlit && kind is "city-coast" or "island")high*=.5f;   // let the harbour towns and Mt. Aris read
            LandShelf(near,dir,r*3.0f,r*2.2f,color.Darkened(.10f));
            Ridge(near,dir,r*1.45f,r*1.2f,r*high,color,kind=="snow");
            Ridge(near+side*r*1.20f+dir*r*.50f,dir,r*.90f,r,r*high*.62f,color.Darkened(.06f),kind=="snow");
            Ridge(near-side*r*1.45f+dir*r*.40f,dir,r*.88f,r,r*high*.48f,color.Lightened(.04f),false);
            // A separate desaturated far ridge is real geometry from every camera azimuth.
            var far=near+dir*r*3.3f;far.Y=-r*.94f;var farColor=color.Lerp(new Color("9cb8c6"),ArtRenderProfile.IsSunlit?.22f:.40f);
            Ridge(far,dir,r*2.15f,r*1.15f,r*high*.85f,farColor,kind=="snow");
            Ridge(far+side*r*2.4f+dir*r*.5f,dir,r*1.8f,r,r*high*.59f,farColor.Lightened(.06f));
            groups.Add(new {board=region.Key,kind,near=near.ToString(),far=far.ToString(),high,policy="outside cells; artistic shoreline/ridge proposal; no shared river claim"});
            if(kind is "everfree" or "meadow" or "snow")
            {
                var tree=kind=="everfree"?"VEG006":kind=="snow"?"VEG103":"VEG003";
                if(!EnvironmentArtCatalog.Entries.ContainsKey(tree))tree="VEG002";
                for(var i=0;i<(kind=="everfree"?9:5);i++)
                {
                    var at=near-dir*r*.75f+side*r*(i-(kind=="everfree"?4:2))*.55f;
                    at=Exterior(at,dir,r*.40f);at.Y=-r*.24f;
                    var treeRoot=EnvironmentArtCatalog.Place(root,tree,at,r*(.28f+(i%3)*.055f),i*59);
                    if(treeRoot!=null)PaperFoldGeometry.Apply(treeRoot);
                }
            }
        }
        if(ArtRenderProfile.IsSunlit)AddNorthernRange(root,cells,bounds,r,Tri,Ridge);
        if(ArtRenderProfile.IsSunlit)AddSouthernLands(root,cells,bounds,r,Tri,Ridge);
        if(ArtRenderProfile.IsSunlit)AddThemedSurroundings(root,cells,centre,r,Tri,Ridge);
        if(ArtRenderProfile.IsSunlit)
        {
            // P9 (concept): the whole coast is ringed by small pastel paper peaks. Exterior only.
            string[] palette=["e9a7aa","f2c08c","f4ede0","9fd1c0","c4b1e4","e6957e","d9e2ec","b7d98a"];
            var rng=new RandomNumberGenerator {Seed=20261026};
            for(var i=0;i<outline.Length;i+=3)
            {
                var o=(outline[i]-centre).Normalized();var dir=new Vector3(o.X,0,o.Y);
                for(var k=0;k<2;k++)
                {
                    var p=new Vector3(outline[i].X,-r*.24f,outline[i].Y)+dir*r*(k==0?rng.RandfRange(-.15f,.35f):rng.RandfRange(.9f,1.7f))
                        +new Vector3(dir.Z,0,-dir.X)*r*rng.RandfRange(-.5f,.5f);
                    if(cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Z).Length()<r*1.75f))continue;
                    if(p.Z<bounds.Position.Z+bounds.Size.Z*.30f || k==1 && rng.Randf()<.5f)continue;   // north half is land now
                    if(k==1)p.Y=-r*.5f;
                    var size=r*rng.RandfRange(.30f,.62f)*(k==1?1.25f:1f);
                    var spin=Vector3.Forward.Rotated(Vector3.Up,rng.RandfRange(0,Mathf.Tau));
                    Ridge(p,spin,size,size*rng.RandfRange(.75f,1.05f),size*rng.RandfRange(1.0f,1.9f),new Color(palette[rng.RandiRange(0,palette.Length-1)]),rng.Randf()<.18f);
                }
            }
        }
        root.AddChild(new MeshInstance3D {Name="RegionalNearAndFarFoldedLandforms",Mesh=tool.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        // Ocean is the actual visible lower plane, not the Environment clear colour or skybox.
        var sea=scenery.GetNode<MeshInstance3D>("Sea");
        var ocean=new ShaderMaterial {Shader=GD.Load<Shader>("res://assets/internal/map_baking/pr1/shaders/paper_ocean.gdshader")};
        ocean.SetShaderParameter("map_centre",centre);ocean.SetShaderParameter("hex_diameter",r*2);
        if(ArtRenderProfile.IsSunlit)ocean.SetShaderParameter("sun_mix",PaperSunlitStyle.Current.OceanSun);
        sea.MaterialOverride=ocean;sea.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        // Deliberate back-side cloud banks; all assets are authored full 3D folds.
        for(var i=0;i<4;i++)
        {
            var p=new Vector3(centre.X+(i-1.5f)*r*7,r*(1.15f+i%2*.65f),bounds.Position.Z-r*(4.0f+i%2));
            var cloud=EnvironmentArtCatalog.Place(root,"BG"+(103+i%3),p,r*(.5f+i%3*.16f),i*23);
            if(cloud==null)continue;
            // 静态地图只记录原版漂移参数，不挂载运行时脚本。
            cloud.SetMeta("source_drift_amplitude",r*.05f);
            cloud.SetMeta("source_drift_speed",.2f);
            cloud.SetMeta("source_drift_phase",i*1.7f);
            foreach(var m in cloud.FindChildren("*","GeometryInstance3D",true,false).OfType<GeometryInstance3D>())m.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
        root.SetMeta("groups_json",JsonSerializer.Serialize(groups));root.SetMeta("region_count",groups.Count);
        root.SetMeta("new_landform_draws",1);root.SetMeta("ocean_transparent_layers",0);
    }

    /// <summary>
    /// P9 only: the land continues north of the board into an unbroken mountain range
    /// (concept board), instead of open sea on every side. Rows rise and fade with distance;
    /// the far rows carry snow. Exterior scenery only — nothing within reach of a cell.
    /// </summary>
    private static void AddNorthernRange(Node3D root,IReadOnlyCollection<HexCell3D> cells,Aabb bounds,float r,
        Action<Vector3,Vector3,Vector3,Color> tri,Action<Vector3,Vector3,float,float,float,Color,bool> ridge)
    {
        var west=bounds.Position.X-r*16;var east=bounds.End.X+r*16;
        var north=bounds.Position.Z;var shoreZ=bounds.Position.Z+bounds.Size.Z*.30f;var far=north-r*16;
        var rng=new RandomNumberGenerator {Seed=4207};
        // Land shelf under the range: a faceted sheet of slightly different greens (no stripes).
        // Its southern edge slopes into the sea, east and west of the board.
        Color[] grass=[new("4a7d3f"),new("528744"),new("45773b"),new("578a47"),new("4d8141")];
        const int columns=30,rowsN=12;
        float Shore(float x)=>shoreZ+Mathf.Sin(x*.37f/r)*r*.9f+Mathf.Sin(x*.13f/r+1.7f)*r*1.3f;
        for(var i=0;i<columns;i++)
        {
            float X(int k)=>west+(east-west)*k/columns;
            var x0=X(i);var x1=X(i+1);
            Vector3 G(float x,int j){var z0=Shore(x);var t=j/(float)rowsN;return new Vector3(x,-r*.26f,Mathf.Lerp(z0,far,t));}
            for(var j=0;j<rowsN;j++)
            {
                var tundra=Mathf.SmoothStep(.35f,1f,j/(float)rowsN)*.45f;   // paler, cooler green toward the range
                tri(G(x0,j+1),G(x1,j+1),G(x1,j),grass[rng.RandiRange(0,grass.Length-1)].Lerp(new Color("8db69a"),tundra));
                tri(G(x0,j+1),G(x1,j),G(x0,j),grass[rng.RandiRange(0,grass.Length-1)].Lerp(new Color("8db69a"),tundra));
            }
            var c=grass[i%grass.Length].Darkened(.10f);
            tri(new Vector3(x0,-r*.26f,Shore(x0)),new Vector3(x1,-r*.26f,Shore(x1)),new Vector3(x1,-r*.86f,Shore(x1)+r*.9f),c);
            tri(new Vector3(x0,-r*.26f,Shore(x0)),new Vector3(x1,-r*.86f,Shore(x1)+r*.9f),new Vector3(x0,-r*.86f,Shore(x0)+r*.9f),c);
        }
        // Official-map layout: a green plain with dark forest patches and winding rivers; the
        // mountains are only a thin, distant blue band with snow along the horizon.
        string[] pines=EnvironmentArtCatalog.Entries.ContainsKey("VEG002")?["VEG002","VEG001","VEG002"]:["VEG003"];
        bool NearCell(Vector3 p,float d)=>cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Z).Length()<d);
        var plainY=-r*.245f;
        for(var k=0;k<16;k++)
        {
            var centre=new Vector3(rng.RandfRange(west+r*2,east-r*2),plainY,rng.RandfRange(north-r*6.5f,Mathf.Min(shoreZ-r*1.5f,north+r*1.5f)));
            if(NearCell(centre,r*3.2f)){k--;continue;}
            var size=r*rng.RandfRange(1.4f,2.6f);var ring=new Vector3[9];
            for(var i=0;i<ring.Length;i++){var t=i*Mathf.Tau/ring.Length;ring[i]=centre+new Vector3(Mathf.Cos(t),0,Mathf.Sin(t)*.7f)*size*rng.RandfRange(.7f,1.15f);}
            var wood=new Color(k%2==0?"356f3c":"2f6837");
            for(var i=0;i<ring.Length;i++)tri(centre,ring[i],ring[(i+1)%ring.Length],i%2==0?wood:wood.Lightened(.04f));
            for(var t=0;t<7;t++)
            {
                var at=centre+new Vector3(rng.RandfRange(-.6f,.6f),0,rng.RandfRange(-.4f,.4f))*size;
                if(NearCell(at,r*2.2f))continue;
                var tree=EnvironmentArtCatalog.Place(root,pines[rng.RandiRange(0,pines.Length-1)],at,r*rng.RandfRange(.22f,.32f),rng.RandfRange(0,360));
                if(tree!=null){PaperFoldGeometry.Apply(tree);foreach(var m in tree.FindChildren("*","GeometryInstance3D",true,false).OfType<GeometryInstance3D>())m.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;}
            }
        }
        // Rivers: meltwater from the range, meandering south to the coast on either side of the board.
        var water=new Color("63c4d6");
        foreach(var (startX,endX,phase) in new[]{(west+r*5,bounds.Position.X-r*3.5f,.4f),(east-r*4,bounds.End.X+r*3.5f,2.1f),(bounds.GetCenter().X+r*2,bounds.GetCenter().X-r*1,4.0f)})
        {
            var start=new Vector3(startX,plainY+r*.002f,far+r*4);var end=new Vector3(endX,plainY+r*.002f,startX==bounds.GetCenter().X+r*2?north+r*.5f:shoreZ+r*.6f);
            Vector3? prevL=null,prevR=null;
            for(var i=0;i<=40;i++)
            {
                var t=i/40f;var p=start.Lerp(end,t)+new Vector3(Mathf.Sin(t*9.5f+phase)*r*1.1f*(1-t*.5f),0,0);
                var q=start.Lerp(end,Mathf.Min(1,t+.025f))+new Vector3(Mathf.Sin(Mathf.Min(1,t+.025f)*9.5f+phase)*r*1.1f*(1-t*.5f),0,0);
                var along=(q-p).Normalized();var side=new Vector3(-along.Z,0,along.X)*r*(.10f+.16f*t);
                var l=p-side;var rr=p+side;
                if(prevL!=null){tri(prevL.Value,prevR!.Value,rr,water);tri(prevL.Value,rr,l,water.Lightened(.05f));}
                prevL=l;prevR=rr;
            }
        }
        var sky=new Color("b9cde6");
        // row: distance behind the board's north edge, peak height, spacing, palette, snow chance
        (float Depth,float Height,float Spacing,string[] Palette,float Snow)[] rows=
        [
            (8.5f,.75f,4.2f,["4f8f6a","5c9c74","66a07c"],0f),
            (11.0f,1.35f,1.5f,["3f78b0","4f88c0","5b8fb8","6aa0cf"],.9f),
            (13.5f,1.9f,1.7f,["5f8fc4","7aa6d4","8db4dc"],1f),
        ];
        for(var row=0;row<rows.Length;row++)
        {
            var spec=rows[row];var fade=row/(float)(rows.Length-1)*.25f;
            for(var x=west+rng.RandfRange(0,spec.Spacing*r);x<east;x+=spec.Spacing*r*rng.RandfRange(.7f,1.15f))
            {
                var p=new Vector3(x,-r*.26f,north-spec.Depth*r+rng.RandfRange(-.8f,.8f)*r);
                if(NearCell(p,r*(2.5f+spec.Height)))continue;
                var h=spec.Height*r*rng.RandfRange(.7f,1.3f);var w=h*rng.RandfRange(.9f,1.3f)*(row==0?1.6f:1f);
                var color=new Color(spec.Palette[rng.RandiRange(0,spec.Palette.Length-1)]).Lerp(sky,fade);
                var dir=Vector3.Forward.Rotated(Vector3.Up,rng.RandfRange(-.5f,.5f));
                ridge(p,dir,w,w*rng.RandfRange(.6f,.85f),h,color,rng.Randf()<spec.Snow);
            }
        }
    }

    /// <summary>
    /// P9 only, after the official map: south of the board the land runs on into a sandy desert
    /// with low mesas (west), a ring of purple-red badlands around an ochre basin (middle) and a
    /// dark jungle edge (east). Everything stays low so the board keeps the eye.
    /// </summary>
    /// <summary>
    /// P9 only: the south stays a proper coast and open ocean (official map). Themed land sits on
    /// small coastal shelves hugging the boards: Appleloosa's desert (J) and the Dragon Lands
    /// volcanoes (H). Everything low, nothing near a cell.
    /// </summary>
    private static void AddSouthernLands(Node3D root,IReadOnlyCollection<HexCell3D> cells,Aabb bounds,float r,
        Action<Vector3,Vector3,Vector3,Color> tri,Action<Vector3,Vector3,float,float,float,Color,bool> ridge)
    {
        var rng=new RandomNumberGenerator {Seed=1867};
        var centre=new Vector2(bounds.GetCenter().X,bounds.GetCenter().Z);
        bool NearCell(Vector3 p,float d)=>cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Z).Length()<d);
        var top=-r*.215f;   // sits just above the continental paper so the shelf reads
        (Vector3 At,Vector3 Out)? Shelf(string board,float push)
        {
            var g=cells.Where(c=>c.Data!.BoardCode==board).ToArray();if(g.Length==0)return null;
            var cl=g.Aggregate(Vector3.Zero,(a,c)=>a+c.Position)/g.Length;
            var o=new Vector3(cl.X-centre.X,0,cl.Z-centre.Y).Normalized();
            var at=cl+o*(g.Max(c=>(c.Position-cl).Dot(o))+push);at.Y=top;return (at,o);
        }
        // a land shelf: flat top, beach skirt down to the sea
        void Land(Vector3 c,float rx,float rz,Color topColor,Color beach)
        {
            const int n=16;var ring=new Vector3[n];var foot=new Vector3[n];
            for(var i=0;i<n;i++){var t=i*Mathf.Tau/n;var k=rng.RandfRange(.88f,1.08f);ring[i]=c+new Vector3(Mathf.Cos(t)*rx,0,Mathf.Sin(t)*rz)*k;foot[i]=c+new Vector3(Mathf.Cos(t)*rx,0,Mathf.Sin(t)*rz)*(k+.28f)+Vector3.Down*r*.6f;}
            for(var i=0;i<n;i++){var j=(i+1)%n;tri(c,ring[i],ring[j],i%2==0?topColor:topColor.Darkened(.04f));tri(ring[i],ring[j],foot[j],beach);tri(ring[i],foot[j],foot[i],beach.Darkened(.05f));}
        }
        void Mesa(Vector3 p,float radius,float height,Color color)
        {
            var n=6;var ring=new Vector3[n];var tp=new Vector3[n];
            for(var i=0;i<n;i++){var t=i*Mathf.Tau/n+rng.RandfRange(-.2f,.2f);ring[i]=p+new Vector3(Mathf.Cos(t),0,Mathf.Sin(t))*radius*rng.RandfRange(.8f,1.15f);tp[i]=p+(ring[i]-p)*.72f+Vector3.Up*height;}
            var cap=p+Vector3.Up*height;
            for(var i=0;i<n;i++){var k=(i+1)%n;var side=i%2==0?color:color.Darkened(.10f);tri(cap,tp[i],tp[k],color.Lightened(.10f));tri(ring[i],ring[k],tp[k],side);tri(ring[i],tp[k],tp[i],side);}
        }
        // J  Appleloosa: desert shelf with dunes and mesas.
        if(Shelf("J",r*2.2f) is var (dAt,dOut))
        {
            Land(dAt,r*2.6f,r*1.9f,new Color("ecc778"),new Color("e6d39a"));
            for(var k=0;k<14;k++)
            {
                var p=dAt+new Vector3(rng.RandfRange(-2.1f,2.1f),.002f,rng.RandfRange(-1.5f,1.5f))*r;
                if(NearCell(p,r*1.9f))continue;
                if(k%3==0)Mesa(p,r*rng.RandfRange(.28f,.5f),r*rng.RandfRange(.22f,.45f),new Color(k%2==0?"d9773f":"e08a48"));
                else ridge(p,Vector3.Forward.Rotated(Vector3.Up,rng.RandfRange(-.4f,.4f)),r*rng.RandfRange(.6f,1.0f),r*rng.RandfRange(.2f,.32f),r*rng.RandfRange(.06f,.12f),new Color("f2d38c"),false);
            }
        }
        // H  Dragon Lands: dark volcanic shelf with three lava volcanoes.
        if(Shelf("H",r*2.3f) is var (vAt,vOut))
        {
            var lava=new Color("ff8a2e");
            Land(vAt,r*2.7f,r*2.0f,new Color("5a4450"),new Color("4a3a42"));
            for(var i=0;i<5;i++)
            {
                var a0=vAt+new Vector3(rng.RandfRange(-1f,1f),r*.006f,rng.RandfRange(-.6f,.6f))*r;var t=rng.RandfRange(0,Mathf.Tau);
                var a1=a0+new Vector3(Mathf.Cos(t)*1.2f,0,Mathf.Sin(t))*r*1.1f;var w=(a1-a0).Cross(Vector3.Up).Normalized()*r*.06f;
                tri(a0-w,a0+w,a1,lava);
            }
            void Volcano(Vector3 p,float radius,float height)
            {
                const int n=9;var tp=radius*.3f;
                for(var i=0;i<n;i++)
                {
                    var t0=i*Mathf.Tau/n;var t1=(i+1)*Mathf.Tau/n;
                    Vector3 B(float t)=>p+new Vector3(Mathf.Cos(t),0,Mathf.Sin(t))*radius*(i%2==0?1.05f:.95f);
                    Vector3 T(float t)=>p+new Vector3(Mathf.Cos(t),0,Mathf.Sin(t))*tp+Vector3.Up*height;
                    var rock=new Color(i%3==0?"4a3a42":i%3==1?"5c4652":"3f3239");
                    tri(B(t0),B(t1),T(t1),rock);tri(B(t0),T(t1),T(t0),rock.Lightened(.05f));
                }
                var crater=p+Vector3.Up*(height-r*.02f);
                for(var i=0;i<n;i++){var t0=i*Mathf.Tau/n;var t1=(i+1)*Mathf.Tau/n;tri(crater,crater+new Vector3(Mathf.Cos(t0),0,Mathf.Sin(t0))*tp*.95f,crater+new Vector3(Mathf.Cos(t1),0,Mathf.Sin(t1))*tp*.95f,lava);}
                var lip=p+new Vector3(.25f,0,1).Normalized()*tp+Vector3.Up*height;var foot=p+new Vector3(.4f,0,1).Normalized()*radius*1.15f+Vector3.Up*r*.01f;
                var w=(foot-lip).Cross(Vector3.Up).Normalized()*r*.06f;
                tri(lip-w*.6f,lip+w*.6f,foot+w,lava);tri(lip-w*.6f,foot+w,foot-w,lava.Darkened(.08f));
                var steam=EnvironmentArtCatalog.Place(root,"BG105",p+Vector3.Up*(height+r*.35f),r*.22f*radius/(r*.7f),rng.RandfRange(0,360));
                if(steam!=null)foreach(var m in steam.FindChildren("*","GeometryInstance3D",true,false).OfType<GeometryInstance3D>())m.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            }
            Volcano(vAt+vOut*r*.5f,r*.8f,r*.85f);
            Volcano(vAt+vOut*r*.2f+new Vector3(vOut.Z,0,-vOut.X)*r*1.5f,r*.55f,r*.55f);
            Volcano(vAt+vOut*r*.3f-new Vector3(vOut.Z,0,-vOut.X)*r*1.6f,r*.5f,r*.5f);
        }
    }

    /// <summary>
    /// P9 only: each themed board gets its own low landmark scenery just outside its outer edge,
    /// following the printed board and the official map (snow/crystals, griffon crags, clouds and
    /// a rainbow, rainbow falls, harbour city, Canterlot falls, Baltimare and Tartarus, Mt. Aris).
    /// Purely exterior; every piece keeps clear of the cells.
    /// </summary>
    private static void AddThemedSurroundings(Node3D root,IReadOnlyCollection<HexCell3D> cells,Vector2 centre,float r,
        Action<Vector3,Vector3,Vector3,Color> tri,Action<Vector3,Vector3,float,float,float,Color,bool> ridge)
    {
        var rng=new RandomNumberGenerator {Seed=31415};
        const float ground=-.235f;
        bool NearCell(Vector3 p,float d)=>cells.Any(c=>new Vector2(c.Position.X-p.X,c.Position.Z-p.Z).Length()<d);
        Color[] rainbow=[new("ff6b6b"),new("ffb347"),new("ffe066"),new("7bd389"),new("6ec6ff"),new("b28dff")];
        // anchor just outside a board, along its outward direction from the map centre
        (Vector3 At,Vector3 Out,Vector3 Side)? Anchor(string board,float clearance,float turn=0)
        {
            var g=cells.Where(c=>c.Data!.BoardCode==board).ToArray();if(g.Length==0)return null;
            var cl=g.Aggregate(Vector3.Zero,(a,c)=>a+c.Position)/g.Length;
            var o=new Vector3(cl.X-centre.X,0,cl.Z-centre.Y).Normalized().Rotated(Vector3.Up,turn);
            var edge=g.Max(c=>(c.Position-cl).Dot(o));
            var at=cl+o*(edge+clearance);at.Y=r*ground;
            for(var i=0;i<20 && NearCell(at,clearance);i++)at+=o*r*.4f;
            return (at,o,new Vector3(o.Z,0,-o.X));
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color col){tri(a,b,c,col);tri(a,c,d,col);}
        void Disc(Vector3 c,float rad,Color col,int n=10,float squash=1){for(var i=0;i<n;i++){var t0=i*Mathf.Tau/n;var t1=(i+1)*Mathf.Tau/n;tri(c,c+new Vector3(Mathf.Cos(t0),0,Mathf.Sin(t0)*squash)*rad,c+new Vector3(Mathf.Cos(t1),0,Mathf.Sin(t1)*squash)*rad,col);}}
        void Box(Vector3 p,float w,float d,float h,Color wall,Color roof)
        {
            Vector3 P(float x,float y,float z)=>p+new Vector3(x,y,z);
            Quad(P(-w,h,-d),P(w,h,-d),P(w,h,d),P(-w,h,d),roof);
            Quad(P(-w,0,d),P(w,0,d),P(w,h,d),P(-w,h,d),wall);Quad(P(w,0,-d),P(w,0,d),P(w,h,d),P(w,h,-d),wall.Darkened(.12f));
            Quad(P(-w,0,-d),P(-w,0,d),P(-w,h,d),P(-w,h,-d),wall.Darkened(.08f));Quad(P(-w,0,-d),P(w,0,-d),P(w,h,-d),P(-w,h,-d),wall.Darkened(.04f));
        }
        void Shard(Vector3 p,float h,float w,Color col)
        {
            var tip=p+new Vector3(rng.RandfRange(-.15f,.15f)*h,h,rng.RandfRange(-.15f,.15f)*h);
            for(var i=0;i<5;i++){var t0=i*Mathf.Tau/5;var t1=(i+1)*Mathf.Tau/5;
                tri(tip,p+new Vector3(Mathf.Cos(t0),0,Mathf.Sin(t0))*w,p+new Vector3(Mathf.Cos(t1),0,Mathf.Sin(t1))*w,i%2==0?col:col.Lightened(.18f));}
        }
        void Fall(Vector3 top,Vector3 outward,float height,float width,Color[] bands)
        {
            var side=new Vector3(outward.Z,0,-outward.X);var bottom=top+outward*height*.35f-Vector3.Up*height;
            for(var i=0;i<bands.Length;i++)
            {
                var a=-width*.5f+width*i/bands.Length;var b=-width*.5f+width*(i+1)/bands.Length;
                Quad(top+side*a,top+side*b,bottom+side*b*1.3f,bottom+side*a*1.3f,bands[i]);
            }
            Disc(bottom+Vector3.Up*r*.01f,width*.95f,new Color("7fd3e0"),10,.6f);
        }
        void Pine(Vector3 at,string id="VEG002",float s=.28f)
        {
            if(NearCell(at,r*2f) || !EnvironmentArtCatalog.Entries.ContainsKey(id))return;
            var t=EnvironmentArtCatalog.Place(root,id,at,r*s*rng.RandfRange(.85f,1.15f),rng.RandfRange(0,360));
            if(t!=null){PaperFoldGeometry.Apply(t);foreach(var m in t.FindChildren("*","GeometryInstance3D",true,false).OfType<GeometryInstance3D>())m.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;}
        }

        // C  Yakyakistan / Crystal Empire: snowfield with crystal clusters and snowy pines.
        if(Anchor("C",r*2.6f) is var (cAt,cOut,cSide))
        {
            Disc(cAt+Vector3.Up*r*.01f,r*2.4f,new Color("eef2f6"),12,.7f);
            for(var i=0;i<6;i++)
            {
                var c=cAt+cSide*r*rng.RandfRange(-1.8f,1.8f)+cOut*r*rng.RandfRange(-.4f,.8f);
                if(NearCell(c,r*2f))continue;
                for(var k=0;k<4;k++)Shard(c+new Vector3(rng.RandfRange(-.25f,.25f),0,rng.RandfRange(-.25f,.25f))*r,r*rng.RandfRange(.45f,.95f),r*rng.RandfRange(.09f,.15f),new Color(k%2==0?"9fdcf0":"e2b8f0"));
            }
            for(var i=0;i<6;i++)Pine(cAt+cSide*r*rng.RandfRange(-2.2f,2.2f)+cOut*r*rng.RandfRange(-.2f,1.2f),"VEG103",.32f);
        }
        // D  Griffonstone: weathered grey-brown crags.
        if(Anchor("D",r*2.4f) is var (dAt,dOut,dSide))
        {
            Disc(dAt+dOut*r*.5f+Vector3.Up*r*.008f,r*2.3f,new Color("a39478"),12,.7f);   // dry rocky ground
            for(var i=0;i<12;i++)
            {
                var p=dAt+dSide*r*rng.RandfRange(-2.0f,2.0f)+dOut*r*rng.RandfRange(-.2f,1.5f);
                if(NearCell(p,r*2f))continue;
                ridge(p,dOut,r*rng.RandfRange(.32f,.5f),r*rng.RandfRange(.28f,.42f),r*rng.RandfRange(.7f,1.3f),new Color(i%3==0?"9a8a78":i%3==1?"857766":"aa9a84"),false);
            }
            var crag=dAt+dOut*r*.9f;
            ridge(crag,dOut,r*.6f,r*.5f,r*1.1f,new Color("8c7c6a"),false);
            Box(crag+Vector3.Up*r*1.0f,r*.10f,r*.10f,r*.35f,new Color("b9ad98"),new Color("7a5a4a"));   // ruined griffon tower
        }
        // L  Cloudsdale / Las Pegasus / Weather Factory: low cloud banks.
        if(Anchor("L",r*2.6f,-.35f) is var (lAt,lOut,lSide))
            for(var i=0;i<3;i++)
            {
                var p=lAt+lSide*r*(i-1)*1.4f+Vector3.Up*r*rng.RandfRange(.55f,.9f);
                var cloud=EnvironmentArtCatalog.Place(root,"BG"+(103+i%3),p,r*rng.RandfRange(.5f,.7f),rng.RandfRange(0,360));
                if(cloud!=null)foreach(var m in cloud.FindChildren("*","GeometryInstance3D",true,false).OfType<GeometryInstance3D>())m.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            }
        // K  Rainbow Falls: a flat-topped green cliff with a striped rainbow waterfall and pool.
        if(Anchor("K",r*2.6f) is var (kAt,kOut,kSide))
        {
            var h=r*.75f;var w=r*.9f;var top=kAt+Vector3.Up*h;
            Vector3 C(float sx,float sz,float y)=>kAt+kSide*sx*w+kOut*sz*w+Vector3.Up*y;
            Quad(C(-1,-1,h),C(1,-1,h),C(1,1,h),C(-1,1,h),new Color("6fae6a"));
            Quad(C(-1,1,0),C(1,1,0),C(1,1,h),C(-1,1,h),new Color("8a9b7c"));Quad(C(-1,-1,0),C(-1,1,0),C(-1,1,h),C(-1,-1,h),new Color("7d8d70"));
            Quad(C(1,-1,0),C(1,1,0),C(1,1,h),C(1,-1,h),new Color("7d8d70"));Quad(C(-1,-1,0),C(1,-1,0),C(1,-1,h),C(-1,-1,h),new Color("8a9b7c"));
            Fall(kAt+Vector3.Back*w*1.0f+Vector3.Up*(h+r*.01f),Vector3.Back,h,r*.6f,rainbow);
            for(var i=0;i<4;i++)Pine(C(rng.RandfRange(-.8f,.8f),rng.RandfRange(-.8f,.4f),h),"VEG003",.22f);
        }
        // E  Manehattan: harbour skyline of low towers along the coast, with a pier.
        if(Anchor("E",r*2.3f) is var (eAt,eOut,eSide))
        {
            string[] walls=["cfd6de","b7c3cf","e3d9c9","aebfd0","d8c6d6"];
            Quad(eAt-eOut*r*.4f-eSide*r*2.2f+Vector3.Up*r*.008f,eAt-eOut*r*.4f+eSide*r*2.2f+Vector3.Up*r*.008f,eAt+eOut*r*1.3f+eSide*r*2.2f+Vector3.Up*r*.008f,eAt+eOut*r*1.3f-eSide*r*2.2f+Vector3.Up*r*.008f,new Color("c9c4bc"));   // paved quarter
            for(var row=0;row<3;row++)
                for(var i=0;i<8;i++)
                {
                    var p=eAt+eSide*r*(i-3.5f)*.52f+eOut*r*(row*.45f-.1f+rng.RandfRange(-.08f,.08f));
                    if(NearCell(p,r*1.9f))continue;
                    var tall=row==1?1.25f:1f;
                    Box(p,r*rng.RandfRange(.15f,.21f),r*rng.RandfRange(.15f,.21f),r*rng.RandfRange(.35f,.85f)*tall,new Color(walls[(i+row)%walls.Length]),new Color(i%3==0?"6f86a0":"8fa3b8"));
                }
            Quad(eAt+eOut*r*1.3f-eSide*r*1.2f,eAt+eOut*r*1.3f+eSide*r*1.2f,eAt+eOut*r*2.1f+eSide*r*1.2f,eAt+eOut*r*2.1f-eSide*r*1.2f,new Color("b89a76"));   // pier
        }
        // F  Canterlot: the purple mountain with a white waterfall down its face.
        if(Anchor("F",r*2.8f) is var (fAt,fOut,fSide))
        {
            ridge(fAt+fOut*r*.4f,fOut,r*1.0f,r*.8f,r*1.5f,new Color("9a86c8"),true);
            ridge(fAt+fSide*r*1.1f+fOut*r*.6f,fOut,r*.7f,r*.6f,r*.9f,new Color("8b78b8"),false);
            Fall(fAt+fOut*r*.4f+Vector3.Back*r*.55f+Vector3.Up*r*.8f,Vector3.Back,r*.8f,r*.24f,[new("f4f7fb"),new("dff1f8"),new("f4f7fb")]);
        }
        // G  Baltimare: a harbour town of red roofs.
        if(Anchor("G",r*2.3f,.3f) is var (gAt,gOut,gSide))
        {
            string[] houses=["f0d8b8","e6c3a3","f2e6d0","d9b49a"];
            for(var i=0;i<14;i++)
            {
                var p=gAt+gSide*r*(i%7-3)*.40f+gOut*r*(i/7*.45f+rng.RandfRange(-.08f,.08f));
                if(NearCell(p,r*1.9f))continue;
                Box(p,r*.13f,r*.13f,r*rng.RandfRange(.2f,.38f),new Color(houses[i%houses.Length]),new Color(i%2==0?"c8574a":"b5483f"));
            }
        }
        // G  Tartarus: a dark rift with a red glow.
        if(Anchor("G",r*2.4f,-.55f) is var (tAt,tOut,tSide))
        {
            var crack=new Vector3[9];
            for(var i=0;i<crack.Length;i++)crack[i]=tAt+tSide*r*(i-4)*.32f+tOut*r*(i%2==0?.18f:-.18f)+Vector3.Up*r*.012f;
            for(var i=0;i+1<crack.Length;i++)
            {
                var a=crack[i];var b=crack[i+1];var o=tOut*r*.22f;
                Quad(a-o,b-o,b+o,a+o,new Color("2b2030"));
                Quad(a-o*.3f+Vector3.Up*r*.003f,b-o*.3f+Vector3.Up*r*.003f,b+o*.3f+Vector3.Up*r*.003f,a+o*.3f+Vector3.Up*r*.003f,new Color("d2433f"));
            }
        }
        // I  Hippogriff kingdom: Mt. Aris and rock-spire islands standing in the open sea.
        if(Anchor("I",r*3.4f) is var (seaAt,seaOut,seaSide))
        {
            var seaY=-r*.82f;
            void Island(Vector3 p,float size,float height,bool snow)
            {
                p.Y=seaY;Disc(p+Vector3.Up*r*.02f,size*1.3f,new Color("efe0a8"),9,.85f);
                Disc(p+Vector3.Up*r*.05f,size,new Color("7fc06a"),9,.85f);
                ridge(p+Vector3.Up*r*.05f,seaOut,size*.65f,size*.55f,height,new Color("8a9b96"),snow);
            }
            Island(seaAt+seaOut*r*.6f,r*.75f,r*2.2f,true);   // Mt. Aris
            for(var i=0;i<6;i++)
            {
                var t=i*Mathf.Tau/6+.3f;var p=seaAt+seaOut*r*.6f+(seaSide*Mathf.Cos(t)+seaOut*Mathf.Sin(t)*.8f)*r*rng.RandfRange(1.8f,2.7f);
                if(NearCell(p,r*2.2f))continue;
                Island(p,r*rng.RandfRange(.3f,.45f),r*rng.RandfRange(.6f,1.1f),false);
            }
        }
        // Rainbow arc beside Rainbow Falls (K).
        if(Anchor("K",r*3.6f,.5f) is var (aAt,aOut,aSide))
        {
            var arcCentre=aAt+Vector3.Up*r*.05f;
            for(var band=0;band<rainbow.Length;band++)
            {
                var r0=r*(1.5f-band*.09f);var r1=r0-r*.09f;
                for(var i=0;i<12;i++)
                {
                    Vector3 P(float rad,int k)=>arcCentre+Vector3.Right*Mathf.Cos(Mathf.Pi*k/12)*rad+Vector3.Up*Mathf.Sin(Mathf.Pi*k/12)*rad;
                    Quad(P(r0,i),P(r0,i+1),P(r1,i+1),P(r1,i),rainbow[band]);
                }
            }
        }
    }
}
