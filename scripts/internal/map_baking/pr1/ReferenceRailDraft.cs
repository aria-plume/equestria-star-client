using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using EquestriaStar.Internal.MapBaking.Pr1;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Paper-board image trace for offline art review. Never used by the live map builder.</summary>
public static class ReferenceRailDraft
{
    public static bool QuietStyle=true;
    private sealed class Trace { public string[][] Routes {get;set;}=[]; }
    public static void Add(Node3D parent,IReadOnlyDictionary<string,HexCell3D> cells)
    {
        var trace=JsonSerializer.Deserialize<Trace>(FileAccess.GetFileAsString("res://assets/internal/map_baking/pr1/reference-rail-draft.json"),new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;
        var root=new Node3D {Name="ReferenceRailDraft"};root.SetMeta("art_status","image_trace_station_ports_not_verified");parent.AddChild(root);
        var radius=cells.Values.First().Radius;
        var fairy=QuietStyle && ArtRenderProfile.IsFairy;
        var rails=new SurfaceTool();rails.Begin(Mesh.PrimitiveType.Triangles);
        var ties=new SurfaceTool();ties.Begin(Mesh.PrimitiveType.Triangles);
        var decks=new SurfaceTool();decks.Begin(Mesh.PrimitiveType.Triangles);decks.SetMaterial(OrigamiMapPresentation.PaperMaterial(new Color("838c78")));
        var railMaterial=OrigamiMapPresentation.PaperMaterial(new Color(fairy?"aaa38e":QuietStyle?"b6b49e":"fff0c9"));
        var tieMaterial=OrigamiMapPresentation.PaperMaterial(new Color(QuietStyle?"848873":"b98f75"));
        rails.SetMaterial(railMaterial);ties.SetMaterial(tieMaterial);
        var tieBox=new BoxMesh {Size=new Vector3(radius*(QuietStyle?.18f:.45f),radius*.018f,radius*(QuietStyle?.040f:.085f))};
        var visited=new HashSet<string>();var clearance=new List<Vector3>();var centerlines=new List<Vector3>();var deckCount=0;
        Vector3 Lateral(Vector3 d)=>new(-d.Z,0,d.X);
        Vector3 Join(Vector3 before,Vector3 after)
        {var n=(Lateral(before)+Lateral(after)).Normalized();if(n.LengthSquared()<.01f)n=Lateral(after);return n*(radius*(QuietStyle?.045f:.11f)/Math.Max(.5f,Math.Abs(n.Dot(Lateral(after)))));}
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
        {
            void Triangle(Vector3 x,Vector3 y,Vector3 z)
            {if(fairy && (y-x).Cross(z-x).Dot(normal)>0)(y,z)=(z,y);rails.SetNormal(normal);rails.AddVertex(x);rails.AddVertex(y);rails.AddVertex(z);}
            Triangle(a,c,b);Triangle(b,c,d);
        }
        bool OnHex(Vector3 p)=>cells.Values.Any(c=>
        {var q=new Vector2(p.X-c.Position.X,p.Z-c.Position.Z);return Geometry2D.IsPointInPolygon(q,Enumerable.Range(0,6).Select(k=>new Vector2(Mathf.Cos(Mathf.DegToRad(30+k*60)),Mathf.Sin(Mathf.DegToRad(30+k*60)))*radius).ToArray());});
        foreach(var route in trace.Routes)
        {
            var points=route.Select(code=>
            {
                var cell=cells[code];var p=PawnArtLayout.WorldAnchor(cell);p.Y=radius*.018f;return p;
            }).ToArray();
            for(var segment=0;segment<points.Length-1;segment++)
            {
                var key=string.CompareOrdinal(route[segment],route[segment+1])<0?route[segment]+":"+route[segment+1]:route[segment+1]+":"+route[segment];
                if(!visited.Add(key))continue;
                var a=points[segment];var b=points[segment+1];var delta=b-a;var direction=delta.Normalized();
                var lateral=new Vector3(-direction.Z,0,direction.X);
                centerlines.Add(a);centerlines.Add(b);
                var start=Join(segment>0?(a-points[segment-1]).Normalized():direction,direction);
                var end=Join(direction,segment+2<points.Length?(points[segment+2]-b).Normalized():direction);
                foreach(var side in new[]{-1,1})
                {
                    var x=a+start*side+Vector3.Up*radius*.0445f;var y=b+end*side+Vector3.Up*radius*.0445f;
                    var w=lateral*radius*(fairy?.014f:QuietStyle?.010f:.0325f);var down=Vector3.Down*radius*(fairy?.035f:.025f);
                    Quad(x-w,y-w,x+w,y+w,Vector3.Up);
                    Quad(x-w,x-w+down,y-w,y-w+down,-lateral);Quad(x+w,y+w,x+w+down,y+w+down,lateral);
                }
                // Support only the portions actually outside the existing hex receivers.
                var samples=Math.Max(4,(int)Math.Ceiling(delta.Length()/(radius*.08f)));int? run=null;
                for(var j=0;j<=samples;j++)
                {
                    var gap=j<samples && !OnHex(a.Lerp(b,(j+.5f)/samples));
                    if(gap && run==null)run=j;
                    if(!gap && run!=null)
                    {
                        var from=a.Lerp(b,Math.Max(0,(run.Value-.2f)/samples));var to=a.Lerp(b,Math.Min(1,(j+.2f)/samples));var middle=(from+to)*.5f;middle.Y=-radius*.012f;
                        decks.AppendFrom(new BoxMesh {Size=new Vector3(radius*.19f,radius*.050f,(to-from).Length())},0,new Transform3D(new Basis(Vector3.Up,Mathf.Atan2(direction.X,direction.Z)),middle));deckCount++;run=null;
                    }
                }
                var count=Math.Max(1,(int)Math.Ceiling(delta.Length()/(radius*(fairy?.38f:QuietStyle?.24f:.50f))));
                var tieBasis=new Basis(Vector3.Up,Mathf.Atan2(direction.X,direction.Z));
                for(var i=0;i<=count;i++)
                {
                    var p=a.Lerp(b,i/(float)count);ties.AppendFrom(tieBox,0,new Transform3D(tieBasis,p));clearance.Add(p);
                }
            }
        }
        root.AddChild(new MeshInstance3D {Name="CreamDoubleRails",Mesh=rails.Commit()});
        root.AddChild(new MeshInstance3D {Name="PaperSleepers",Mesh=ties.Commit()});
        if(deckCount>0)root.AddChild(new MeshInstance3D {Name="GapBridgeDecks_Draft",Mesh=decks.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        root.SetMeta("centerline_segments",centerlines.ToArray());root.SetMeta("bridge_deck_spans",deckCount);
        root.SetMeta("rail_gauge_diameter_ratio",.045f);root.SetMeta("rail_width_diameter_ratio",fairy?.014f:.010f);
        root.SetMeta("reserved_points",clearance.ToArray());
        // Preview-only pruning of newly scattered trees, leaving visible source landmarks intact.
        foreach(var cell in cells.Values)
        {
            var props=cell.GetNodeOrNull<Node3D>("VisualRoot/DecorationRoot/LocalVegetation");if(props==null)continue;
            foreach(var prop in props.GetChildren().OfType<Node3D>())
                if(clearance.Any(p=>new Vector2(p.X-prop.GlobalPosition.X,p.Z-prop.GlobalPosition.Z).Length()<radius*.40f))prop.Visible=false;
        }
        ConceptPaperStyle.Apply(root);
    }
}
