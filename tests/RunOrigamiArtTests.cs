using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;

public partial class RunOrigamiArtTests : SceneTree
{
    private readonly List<string> _failures = [];
    public override void _Initialize() => Callable.From(Run).CallDeferred();
    private void Check(bool ok,string message) { if(!ok) _failures.Add(message); }
    private void Run()
    {
        try
        {
            var entries=OrigamiArtCatalog.Entries;
            Check(entries.Count==43,"43 current asset entries must load");
            var cellScene=GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn");
            foreach(var entry in entries)
            {
                var cell=cellScene.Instantiate<HexCell3D>();
                cell.ConfigureGeometry(.55f,.11f,.025f);
                cell.BindData(new HexCellData {CellId=999,CellCode="UNRELATED",CellName=entry.Name});
                OrigamiArtCatalog.Apply(cell,entry);
                Root.AddChild(cell);
                var visual=cell.GetNode<Node3D>("VisualRoot/CustomVisualRoot").GetChild<Node3D>(0);
                Check(visual.HasNode("Terrain"),entry.Id+": terrain missing");
                Check(visual.Scale.IsEqualApprox(Vector3.One*.55f),entry.Id+": incorrect uniform scale");
                Check(Math.Abs(visual.RotationDegrees.Y-30)<.001f,entry.Id+": incorrect hex orientation");
                Check(!cell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh").Visible,entry.Id+": duplicate base");
                foreach(var kind in entry.Components.Keys)
                    Check(visual.HasNode(char.ToUpperInvariant(kind[0])+kind[1..]),entry.Id+": component missing "+kind);
                cell.SetSelected(true);
                Check(cell.GetNode<MeshInstance3D>("HighlightMesh").Visible,entry.Id+": selection highlight hidden");
                var activated=false;
                cell.CellClicked+=id=>activated=id==999;
                cell.TryActivate();
                Check(activated,entry.Id+": identity changed during art replacement");
                cell.Free();
            }
            Check(OrigamiArtCatalog.Resolve(new HexCellData {CellName="方糖屋"})?.Id=="sugarcube","Live map alias mismatch");
            Check(OrigamiArtCatalog.Resolve(new HexCellData {CellName="水晶集市"})==null,"Candidate enabled in live map");
            Check(OrigamiArtCatalog.Resolve(new HexCellData {CellName="水晶集市"},true)?.Id=="crystal-market","Candidate unavailable in gallery");
            Check(OrigamiArtCatalog.Resolve(new HexCellData {CellId=66,CellName="unknown"})==null,"Art must not infer identity from numeric ID");
            using var doc=JsonDocument.Parse(FileAccess.GetFileAsString("res://tests/fixtures/main-map-20261024.json"));
            var map=JsonSerializer.Deserialize<GameMapDto>(doc.RootElement.GetProperty("data").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var root=new Node3D();Root.AddChild(root);
            var builder=new GameMapBuilder();
            var result=builder.Build(map,root,cellScene,.55f,.11f,.025f,false);
            Check(result.Ok,"Public map snapshot build failed: "+result.Error);
            Check(result.Bounds.Size.Y>.5f,"Camera bounds omit tall landmarks");
            foreach(var dto in map.Cells!)
            {
                var cell=builder.CellsById[dto.CellId];
                Check(cell.Position.IsEqualApprox(HexCoordinateConverter.ToWorld(dto.Q!.Value,dto.R!.Value,dto.BoardCode,.55f)),dto.CellCode+": backend position changed");
            }
            GD.Print($"ART_TEST mapped={builder.CellsById.Values.Count(c=>c.UsesCustomVisual)} total={result.CellCount} assets={entries.Count}");
            root.Free();
        }
        catch(Exception ex) {_failures.Add(ex.ToString());}
        foreach(var failure in _failures)GD.PushError(failure);
        GD.Print(_failures.Count==0?"Origami art tests passed.":$"Origami art tests failed: {_failures.Count}");
        Quit(_failures.Count==0?0:1);
    }
}
