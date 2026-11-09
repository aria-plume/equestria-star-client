using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Only physical neighbors with existing land edges receive a low visual substrate.</summary>
public static class BoardSeamFoundations
{
    public static void Add(Node3D parent,IReadOnlyCollection<HexCell3D> cells,IEnumerable<MapEdgeDto> edges,float radius)
    {
        var root=new Node3D {Name="VerifiedLandSeams"};parent.AddChild(root);
        var byId=cells.ToDictionary(c=>c.Data!.CellId);var used=new HashSet<(long,long)>();var filled=new List<string>();var reserved=new List<string>();
        float Support(Vector3 d)=>Enumerable.Range(0,6).Max(k=>new Vector3(Mathf.Cos(Mathf.DegToRad(30+60*k)),0,Mathf.Sin(Mathf.DegToRad(30+60*k))).Dot(d))*radius;
        foreach(var edge in edges)
        {
            if(!byId.TryGetValue(edge.FromCellId,out var a) || !byId.TryGetValue(edge.ToCellId,out var b))continue;
            var key=(Math.Min(edge.FromCellId,edge.ToCellId),Math.Max(edge.FromCellId,edge.ToCellId));if(!used.Add(key))continue;
            var label=a.Data!.CellCode+":"+b.Data!.CellCode;
            if(edge.EdgeType is not ("NORMAL" or "EVERFREE") || a.Data.BoardCode=="L" || b.Data.BoardCode=="L") {reserved.Add(label+":"+edge.EdgeType);continue;}
            var delta=b.Position-a.Position;var length=delta.Length();if(length>radius*2.9f || length<radius*1.5f)continue;
            var d=delta.Normalized();var gap=length-Support(d)*2;if(gap<radius*.025f)continue;
            var centre=(a.Position+b.Position)*.5f;var across=d*(gap*.5f+radius*.07f);var side=new Vector3(-d.Z,0,d.X)*radius*.52f;
            Vector2 Xz(Vector3 p)=>new(p.X,p.Z);
            var board=a.Data.BoardCode==b.Data.BoardCode?a.Data.BoardCode:"A";
            var color=EquestriaStar.Internal.MapBaking.Pr1.ConceptPaperStyle.Regions.GetValueOrDefault(board,new Color("78915c")).Darkened(.07f);
            OrigamiMapPresentation.AddPaperIsland(root,"Seam_"+a.Data.CellCode+"_"+b.Data.CellCode,[Xz(centre-side-across),Xz(centre+side-across),Xz(centre+side+across),Xz(centre-side+across)],-radius*.045f,radius*.16f,color);
            filled.Add(label);
        }
        root.SetMeta("filled_existing_edges",filled.ToArray());root.SetMeta("reserved_edges",reserved.ToArray());
    }
}
