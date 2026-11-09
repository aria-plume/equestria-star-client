using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Deterministic visual layout only. Never changes logical cells, paths or picking.</summary>
public static class OrigamiMapPresentation
{
    // Multiples of 60 keep the hex footprint aligned, including terrain-mounted details.
    // Stations retain their orientation until their platform ports have been surveyed.
    private static readonly Dictionary<string, float> Yaws = new()
    {
        ["sugarcube"] = -60, ["sweet-apple-acres"] = 60, ["fluttershy"] = 60,
        ["fashion-shop"] = -60, ["town-hall"] = 0, ["rainbow-house"] = 60, ["twilight-castle"] = 0,
        ["friendship-school"] = 60, ["ponyville-market"] = -60, ["old-castle"] = 60,
        ["zecora"] = -60, ["harmony-tree"] = 120, ["crystal-castle"] = 60,
        ["crystal-empire"] = -60, ["crystal-stadium"] = 60, ["yakyakistan"] = -60,
        ["las-pegasus"] = -60, ["cloudsdale"] = 60, ["weather-factory"] = -60,
        ["equality-town"] = 60, ["rainbow-market"] = -60, ["rainbow-falls"] = 60,
        ["liberty-statue"] = -60, ["canterlot"] = -60, ["canterlot-market"] = 60,
        ["griffonstone"] = 60, ["baltimare"] = -60, ["dragon-lands"] = 120,
        ["hippogriff"] = -60, ["appleloosa"] = 60, ["tartarus"] = -60
    };
    private sealed class Layout { public Dictionary<string,float> LandmarkYawOverrides {get;set;} = new(); }
    private const string LayoutPath="res://assets/internal/map_baking/pr1/map-presentation.json";
    private static readonly Dictionary<string,float> Overrides = new();
    static OrigamiMapPresentation()
    {
        if(!FileAccess.FileExists(LayoutPath))return;
        try
        {
            var layout=JsonSerializer.Deserialize<Layout>(FileAccess.GetFileAsString(LayoutPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
            foreach(var pair in layout?.LandmarkYawOverrides ?? new()) SetOrientation(pair.Key,pair.Value);
        }
        catch(Exception exception){GD.PushWarning("本地朝向配置读取失败："+exception.Message);}
    }
    public static void SetOrientation(string id,float degrees)
    {
        var snapped=Mathf.Round(degrees/60)*60;
        Overrides[id]=snapped;
    }
    public static Error SaveOrientations()
    {
        using var file=FileAccess.Open(LayoutPath,FileAccess.ModeFlags.Write);
        if(file==null)return FileAccess.GetOpenError();
        file.StoreString(JsonSerializer.Serialize(new {schemaVersion=1,status="local-visual-review",landmarkYawOverrides=Overrides},new JsonSerializerOptions{WriteIndented=true}));
        return Error.Ok;
    }

    public static uint Seed(string value)
    {
        uint hash = 2166136261;
        foreach (var ch in value) { hash ^= ch; hash = unchecked(hash * 16777619); }
        return hash;
    }

    public static float Orientation(OrigamiArtCatalog.Entry entry, HexCellData data) =>
        Overrides.TryGetValue(entry.Id,out var yaw) ? yaw : entry.Id == "everfree" ? Seed(data.CellCode) % 6 * 60 : Yaws.GetValueOrDefault(entry.Id);

    public static void Decorate(HexCell3D cell, OrigamiArtCatalog.Entry? entry,bool deferNearDetails=false)
    {
        if (cell.Data == null || EnvironmentArtCatalog.Entries.Count == 0) return;
        var seed = Seed(cell.Data.CellCode);
        var forest = entry?.Id == "everfree";
        if (forest)
        {
            var old = cell.GetNodeOrNull<Node3D>("VisualRoot/CustomVisualRoot/OrigamiAsset/Decoration");
            if (old != null)
                foreach (var node in old.FindChildren("Everfree_Tree_*", "Node3D", true, false))
                    ((Node3D)node).Visible = false;
        }
        // Existing landmarks already carry large decorations. Don't scatter blindly through them.
        if (entry != null && !forest) return;
        var root = new Node3D { Name = "LocalVegetation" };
        cell.GetNode<Node3D>("VisualRoot/DecorationRoot").AddChild(root);
        var board = cell.Data.BoardCode;
        var count = forest ? 2 + (int)(seed % 2) : 1 + (int)(seed % 2);
        for (var i = 0; i < count; i++)
        {
            // Edge pockets rather than a uniform ring; the centre remains a reserved token lane.
            var angle = Mathf.DegToRad((seed % 360) + i * (forest ? 360f/count : 137.5f) + (seed >> (i+1))%23);
            var distance = cell.Radius * (forest ? .74f : .69f);
            var position = new Vector3(Mathf.Cos(angle) * distance, .035f * cell.Radius, Mathf.Sin(angle) * distance);
            // Everfree has its own bent trunks and broad teal paper crowns. Never substitute pines.
            var id = forest ? $"VEG{6 + ((seed >> 3) + i) % 4:000}" : board switch
            {
                "C" => "VEG001",
                "D" or "G" => "TER003",
                "H" => "DEC002",
                "J" => "TER003",
                "L" => "VEG001",
                "E" => "VEG004",
                _ => (seed + i) % 3 == 0 ? "VEG005" : "VEG003"
            };
            // Size by footprint, not height: no foliage reaches the reserved 0.30r centre disk.
            if (!EnvironmentArtCatalog.Entries.TryGetValue(id, out var asset)) continue;
            var bound = asset.Components.Values.First().BoundsBlender;
            var halfWidth = Math.Max(Math.Abs(bound.Min[0]), Math.Abs(bound.Max[0]));
            var halfDepth = Math.Max(Math.Abs(bound.Min[1]), Math.Abs(bound.Max[1]));
            var footprintRadius = MathF.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth);
            var scale = cell.Radius * ((forest ? .31f : .22f) + (seed + i * 11) % 7 * .006f) / Math.Max(.01f, footprintRadius);
            Node3D? placed;
            if(board=="C" && !forest && EnvironmentArtCatalog.Entries.ContainsKey("VEG103"))
                placed=EnvironmentArtCatalog.PlaceParts(root,"VEG103",["part02","part03","part04"],position,cell.Radius*.34f,(seed+i*67)%360);
            else placed = EnvironmentArtCatalog.Place(root, id, position, scale, (seed + i * 67) % 360);
            if (placed == null) continue;
            placed.SetMeta("zoom_main_tree",true);
            var halfWidthReserved = cell.Radius*.30f;
            var reserve = new Aabb(cell.Position+new Vector3(-halfWidthReserved,.045f,-halfWidthReserved),
                new Vector3(halfWidthReserved*2,cell.Radius*.75f,halfWidthReserved*2));
            // GLB local mesh bounds may be looser than the catalog's assembly bounds after yaw.
            // Check actual instances before accepting their placement.
            for (var attempt=0; attempt<12 && VisualClearance.Intersects(placed,reserve); attempt++)
                placed.Scale *= .88f;
        }
        // Current delivery includes complete mushroom, stone and flower groups as independent parts.
        // Use those parts instead of a whole display-kit layout on each cell.
        if(deferNearDetails)return;
        foreach(var spec in NearProps(cell))
        {
            var prop=EnvironmentArtCatalog.PlaceParts(root,spec.Id,spec.Parts,spec.Position,spec.Scale,spec.Yaw);
            if(prop==null)continue;
            prop.SetMeta("zoom_detail_min",2);
            var reserve=new Aabb(cell.Position+new Vector3(-cell.Radius*.30f,.045f,-cell.Radius*.30f),new Vector3(cell.Radius*.60f,cell.Radius*.75f,cell.Radius*.60f));
            for(var attempt=0;attempt<12 && VisualClearance.Intersects(prop,reserve);attempt++)prop.Scale*=.88f;
        }
    }

    public sealed record NearProp(string Id,string[] Parts,Vector3 Position,float Scale,float Yaw);
    public static NearProp[] NearProps(HexCell3D cell)
    {
        var entry=OrigamiArtCatalog.Resolve(cell.Data!);var forest=entry?.Id=="everfree";
        if(entry!=null && !forest)return [];
        var board=cell.Data!.BoardCode;var seed=Seed(cell.Data.CellCode);
        if(forest)
        {
            Vector3 Pocket(float offset,float distance)
            {var a=Mathf.DegToRad(seed%360+offset);return new Vector3(Mathf.Cos(a)*cell.Radius*distance,.024f,Mathf.Sin(a)*cell.Radius*distance);}
            return [
                new NearProp("DEC101",["part0"+(7+seed%3)],Pocket(70,.67f),cell.Radius*.82f,seed%360),
                new NearProp("VEG104",["part0"+(1+seed%5)],Pocket(185,.73f),cell.Radius*.55f,(seed+115)%360),
                new NearProp("VEG104",["part0"+(1+(seed+2)%5)],Pocket(305,.70f),cell.Radius*.43f,(seed+245)%360)
            ];
        }
        var count=forest?1:board is "A" or "K"?(seed%3==0?1:0):board is "C" or "D" or "G" or "H" or "J"?1:0;
        if(count==0)return [];
        var angle=Mathf.DegToRad(seed%360+70);
        var point=new Vector3(Mathf.Cos(angle)*cell.Radius*.65f,.024f,Mathf.Sin(angle)*cell.Radius*.65f);
        var id=forest?"DEC101":board is "C" or "D" or "G" or "H" or "J"?"DEC103":"DEC102";
        var parts=forest?new[]{"part0"+(7+seed%3)}:id=="DEC103"?new[]{board=="C"?"part05":board=="J"?"part04":"part01"}:new[]{"part07","part08"};
        return [new NearProp(id,parts,point,cell.Radius*(forest?1.0f:.70f),seed%360)];
    }

    public static Aabb AddContinent(Node3D parent, IReadOnlyCollection<HexCell3D> cells, Aabb bounds, float radius,IEnumerable<EquestriaStar.Api.MapEdgeDto>? edges=null) =>
        ContinentalSceneryBuilder.Build(parent, cells, bounds, radius,edges);

    public static StandardMaterial3D PaperMaterial(Color color) => new() { AlbedoColor=color, Roughness=.94f,
        CullMode=BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo=true };

    public static void AddPaperIsland(Node3D root,string name,Vector2[] outline,float y,float thickness,Color color)
    {
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(PaperMaterial(color));
        void Vertex(Vector2 p,float h) => surface.AddVertex(new Vector3(p.X,h,p.Y));
        var triangles = Geometry2D.TriangulatePolygon(outline);
        if (triangles.Length == 0) throw new InvalidOperationException("Invalid scenery polygon: " + name);
        for (var i=0; i<triangles.Length; i+=3)
        {
            surface.SetColor(new Color(1-i%9*.0012f,1-i%9*.0007f,1-i%9*.001f));
            surface.SetNormal(Vector3.Up);
            Vertex(outline[triangles[i]],y);Vertex(outline[triangles[i+(PaperWorldBackground.Enabled?1:2)]],y);Vertex(outline[triangles[i+(PaperWorldBackground.Enabled?2:1)]],y);
        }
        for(var i=0;i<outline.Length;i++)
        {
            var p=outline[i];var q=outline[(i+1)%outline.Length];
            surface.SetColor(new Color(.86f,.88f,.83f));
            surface.SetNormal(new Vector3(q.Y-p.Y,0,p.X-q.X).Normalized());
            Vertex(p,y);Vertex(q,y);Vertex(q,y-thickness);
            Vertex(p,y);Vertex(q,y-thickness);Vertex(p,y-thickness);
        }
        root.AddChild(new MeshInstance3D { Name=name, Mesh=surface.Commit() });
    }
}
