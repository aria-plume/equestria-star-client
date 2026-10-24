using System;
using System.Linq;
using System.Text.Json;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;

namespace EquestriaStar.Art;

/// <summary>Offline inspection scene. No login, API writes, or invented gameplay.</summary>
public partial class OrigamiGallery : Node3D
{
    private GameMapScene _stage = null!;
    private Node3D _root = null!;
    private Label _description = null!;
    private OptionButton _picker = null!;
    private readonly GameMapBuilder _builder = new();
    private string? _capturePath;
    private int _captureFrames;

    public override void _Ready()
    {
        _stage = GD.Load<PackedScene>("res://scenes/game/map/GameMapScene.tscn").Instantiate<GameMapScene>();
        _stage.AutoLoadOnReady = false;
        AddChild(_stage);
        _root = _stage.GetNode<Node3D>("MapRoot");
        var floor = _stage.GetNode<MeshInstance3D>("Floor");
        ((PlaneMesh)floor.Mesh).Size = new Vector2(150, 150);
        floor.Position = new Vector3(0,-0.23f,0);
        var layer = new CanvasLayer { Layer = 30 };
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(24,24), Theme = GD.Load<Theme>("res://assets/theme/client_theme.tres") };
        panel.AddToGroup("blocks_map_input");
        layer.AddChild(panel);
        var content = new VBoxContainer(); panel.AddChild(content);
        content.AddChild(new Label { Text = "小马国之星 · 折纸美术", Theme = panel.Theme });
        content.AddChild(new Label { Text = "滚轮缩放 · 鼠标拖动棋盘 · 点击地块检查选中状态" });
        _picker = new OptionButton { CustomMinimumSize = new Vector2(360,42) };
        content.AddChild(_picker);
        _picker.AddItem("素材总览（43组）");
        _picker.AddItem("正式地图快照（离线检查）");
        foreach (var entry in OrigamiArtCatalog.Entries)
            _picker.AddItem(entry.Name + (entry.Status == "candidate" ? " · 候选" : ""));
        _picker.ItemSelected += ShowSelection;
        _description = new Label { CustomMinimumSize = new Vector2(360,0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(_description);
        var buttons = new HBoxContainer(); content.AddChild(buttons);
        var reset = new Button { Text = "重置镜头" }; buttons.AddChild(reset);
        reset.Pressed += () => ShowSelection(_picker.Selected);
        var args = OS.GetCmdlineUserArgs();
        var selection = 0;
        for (var i=0; i<args.Length-1; i++)
        {
            if (args[i] == "--art-capture") _capturePath = args[i+1];
            if (args[i] == "--art-view") int.TryParse(args[i+1], out selection);
        }
        selection = Math.Clamp(selection,0,_picker.ItemCount-1);
        _picker.Select(selection);
        ShowSelection(selection);
    }

    private void ShowSelection(long selection)
    {
        _builder.Clear(_root);
        if (selection == 1)
        {
            using var document = JsonDocument.Parse(FileAccess.GetFileAsString("res://tests/fixtures/main-map-20261024.json"));
            var map = JsonSerializer.Deserialize<GameMapDto>(document.RootElement.GetProperty("data").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var result = _builder.Build(map,_root,GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn"),.55f,.11f,.025f,false);
            if (!result.Ok) { _description.Text = result.Error; return; }
            foreach (var cell in _builder.CellsById.Values) WireCell(cell);
            _stage.CameraController.InitializeMap(result.Bounds);
            var installed = _builder.CellsById.Values.Count(c=>c.UsesCustomVisual);
            _description.Text = $"2026-10-24 后端快照 · {result.CellCount} 格\n{installed} 格已接入模型；候选稿与未标准化市政厅保留基础地块。\n此页仅检查美术，不代表在线对局。";
            return;
        }
        var entries = selection == 0 ? OrigamiArtCatalog.Entries.ToArray() : new[] { OrigamiArtCatalog.Entries[(int)selection-2] };
        var cellScene = GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn");
        float maxHeight = 0;
        for (var i=0; i<entries.Length; i++)
        {
            var entry=entries[i];
            var cell=cellScene.Instantiate<HexCell3D>();
            cell.ConfigureGeometry(1,.2f,.025f);
            cell.BindData(new HexCellData { CellId=i+1,CellCode=entry.Id,CellName=entry.Name });
            cell.Position=new Vector3((i%8)*2.5f,0,(i/8)*2.7f);
            maxHeight=Math.Max(maxHeight,OrigamiArtCatalog.Apply(cell,entry));
            _root.AddChild(cell);
            WireCell(cell);
            if (selection==0)
            {
                var label=new Label3D { Text=entry.Name.Replace("_候选方案","*"),FontSize=36,PixelSize=.0028f,
                    Modulate=new Color("32473f"),OutlineModulate=new Color("fff6df"),OutlineSize=6,
                    Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Position=new Vector3(0,.15f,1.05f),NoDepthTest=true };
                cell.AddChild(label);
            }
        }
        _stage.CameraController.InitializeMap(new Aabb(new Vector3(-1,-.2f,-1),new Vector3(selection==0?19.5f:2,maxHeight+.2f,selection==0?15.5f:2)));
        if(selection!=0) _stage.GetNode<Camera3D>("CameraRig/Camera3D").Size=5;
        _description.Text = selection==0 ? "43组当前交付 · 地块 / 建筑 / 装饰独立\n候选稿仅在此展示；几何细节保持原制作版本。" :
            $"{entries[0].Name}\n{entries[0].Components.Count}个独立组件 · 纸边和折面保留\n"+(entries[0].Status=="candidate"?"候选方案，未自动放入正式地图。":"已接入；造型仍以原交付审阅状态为准。");
    }

    private static void WireCell(HexCell3D cell) => cell.CellClicked += _ => cell.SetSelected(cell.VisualState != HexCellVisualState.Selected);

    public override void _Process(double delta)
    {
        if (_capturePath == null || ++_captureFrames < 60) return;
        GetViewport().GetTexture().GetImage().SavePng(_capturePath);
        GD.Print($"ART_CAPTURE {_capturePath}");
        GetTree().Quit();
    }
}
