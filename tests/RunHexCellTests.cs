using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Game.Map;

public partial class RunHexCellTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTests).CallDeferred();
    }

    private void BeginTests()
    {
        _ = RunTestsAsync();
    }

    private async Task RunTestsAsync()
    {
        TestDataModel();
        var scene = GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn");
        AssertTrue(scene != null, "HexCell3D 场景必须可加载");
        if (scene == null)
        {
            Finish();
            return;
        }

        var first = scene.Instantiate<HexCell3D>();
        var second = scene.Instantiate<HexCell3D>();
        Root.AddChild(first);
        Root.AddChild(second);
        await ToSignal(this, SignalName.ProcessFrame);

        VerifySceneStructure(first);
        VerifyGeometry(first);
        VerifySharedResources(first, second);
        VerifyDataBinding(first);
        VerifyStatePriorityAndSignals(first);
        var customCell = scene.Instantiate<HexCell3D>();
        var customMesh = new BoxMesh();
        customCell.CustomMesh = customMesh;
        Root.AddChild(customCell);
        await ToSignal(this, SignalName.ProcessFrame);
        await VerifyCustomVisualSourcesAsync(customCell, customMesh);

        first.QueueFree();
        second.QueueFree();
        customCell.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        Finish();
    }

    private void TestDataModel()
    {
        var data = new HexCellData
        {
            CellId = 10,
            CellCode = "A10",
            CellName = "后备名称",
            Q = 1,
            R = 5,
            InteractionTags = ["SHOP", "STATION"]
        };
        AssertEqual(data.DisplayName, "后备名称", "缺少 CellNameText 时应回退 CellName");
        data.CellNameText = "玩家名称";
        AssertEqual(data.DisplayName, "玩家名称", "展示名称应优先使用 CellNameText");
        AssertTrue(data.HasInteractionTag("shop"), "交互标签查询应忽略英文大小写");
        AssertFalse(data.HasInteractionTag("WORK"), "不存在的交互标签应返回 false");
    }

    private void VerifySceneStructure(HexCell3D cell)
    {
        foreach (var path in new[]
        {
            "VisualRoot",
            "VisualRoot/BaseMesh",
            "VisualRoot/CustomMeshInstance",
            "VisualRoot/CustomVisualRoot",
            "VisualRoot/DecorationRoot",
            "HighlightMesh",
            "InteractionArea",
            "InteractionArea/CollisionShape3D",
            "ContentAnchor",
            "TokenRoot",
            "IconAnchor",
            "DebugLabel"
        })
        {
            AssertTrue(cell.HasNode(path), $"HexCell3D 缺少节点：{path}");
        }

        AssertEqual(cell.GetNode<Node3D>("ContentAnchor").Position, Vector3.Zero, "ContentAnchor 应位于顶面逻辑原点");
        AssertEqual(cell.GetNode<Node3D>("TokenRoot").Position, Vector3.Zero, "TokenRoot 应位于顶面逻辑原点");
        AssertEqual(cell.GetNode<Node3D>("IconAnchor").Position, Vector3.Zero, "IconAnchor 应位于顶面逻辑原点");
        AssertEqual(cell.GetNode<Area3D>("InteractionArea").CollisionLayer, 128u, "默认输入碰撞层应独立且可配置");
    }

    private void VerifyGeometry(HexCell3D cell)
    {
        var mesh = cell.GeneratedMesh as ArrayMesh;
        AssertTrue(mesh != null, "默认视觉应生成 ArrayMesh");
        if (mesh == null)
        {
            return;
        }

        AssertEqual(mesh.GetSurfaceCount(), 2, "顶面与侧面应使用两个独立表面");
        var bounds = mesh.GetAabb();
        AssertApprox(bounds.Position.Y, -0.2f, 0.001f, "六边形底面应位于 -Height");
        AssertApprox(bounds.End.Y, 0.0f, 0.001f, "逻辑原点应位于格子顶面中心");
        AssertApprox(bounds.Size.Z, 2.0f, 0.001f, "默认尖顶六边形直径应为 2");

        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            AssertTrue(vertices.Length > 0, $"表面 {surfaceIndex} 应包含顶点");
            AssertEqual(normals.Length, vertices.Length, $"表面 {surfaceIndex} 每个顶点都应有法线");
            AssertEqual(uvs.Length, vertices.Length, $"表面 {surfaceIndex} 每个顶点都应有 UV");
        }

        var baseMesh = cell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh");
        AssertTrue(baseMesh.GetSurfaceOverrideMaterial(0) != null, "顶面应绑定材质");
        AssertTrue(baseMesh.GetSurfaceOverrideMaterial(1) != null, "侧面应绑定材质");
        AssertFalse(
            ReferenceEquals(baseMesh.GetSurfaceOverrideMaterial(0), baseMesh.GetSurfaceOverrideMaterial(1)),
            "顶面和侧面材质必须可独立替换"
        );

        var collision = cell.InteractionShape as ConvexPolygonShape3D;
        AssertTrue(collision != null, "交互碰撞应使用 ConvexPolygonShape3D");
        if (collision != null)
        {
            AssertEqual(collision.Points.Length, 12, "六边形柱体碰撞应由上下各六个点组成");
        }
    }

    private void VerifySharedResources(HexCell3D first, HexCell3D second)
    {
        AssertTrue(ReferenceEquals(first.GeneratedMesh, second.GeneratedMesh), "同尺寸格子必须共享 Mesh 资源");
        AssertTrue(ReferenceEquals(first.InteractionShape, second.InteractionShape), "同尺寸格子必须共享碰撞体资源");
        var originalMesh = first.GeneratedMesh;
        first.ConfigureGeometry(1.2f, 0.24f, 0.05f);
        AssertFalse(ReferenceEquals(originalMesh, first.GeneratedMesh), "不同尺寸应取得不同缓存 Mesh");
        first.ConfigureGeometry(1.0f, 0.2f, 0.05f);
        AssertTrue(ReferenceEquals(originalMesh, first.GeneratedMesh), "恢复相同尺寸时应复用已有缓存 Mesh");
    }

    private void VerifyDataBinding(HexCell3D cell)
    {
        cell.ShowDebugLabel = true;
        cell.BindData(new HexCellData
        {
            CellId = 88,
            CellCode = "A10",
            CellName = "测试格",
            CellNameText = "星辉广场",
            Q = 1,
            R = 5
        });
        AssertEqual(cell.Data?.DisplayName, "星辉广场", "绑定后应保留玩家展示名称");
        AssertEqual(cell.GetNode<Label3D>("DebugLabel").Text, "A10\n(1,5)", "调试标签应显示两行编码和坐标");
        AssertTrue(cell.GetNode<Label3D>("DebugLabel").Visible, "ShowDebugLabel=true 时标签应显示");
    }

    private void VerifyStatePriorityAndSignals(HexCell3D cell)
    {
        var clickedId = 0L;
        var clickCount = 0;
        var hoverCount = 0;
        cell.CellClicked += id =>
        {
            clickedId = id;
            clickCount++;
        };
        cell.CellHoverChanged += (_, _) => hoverCount++;

        cell.ClearStateFlags();
        AssertEqual(cell.VisualState, HexCellVisualState.Normal, "清理状态后应为 Normal");
        AssertFalse(cell.GetNode<MeshInstance3D>("HighlightMesh").Visible, "普通状态不应显示覆盖层");
        cell.SetCurrent(true);
        AssertEqual(cell.VisualState, HexCellVisualState.Current, "Current 状态应生效");
        cell.SetHovered(true);
        AssertEqual(cell.VisualState, HexCellVisualState.Hovered, "Hovered 优先于 Current");
        cell.SetMovable(true);
        AssertEqual(cell.VisualState, HexCellVisualState.Movable, "Movable 优先于 Hovered");
        cell.SetSelected(true);
        AssertEqual(cell.VisualState, HexCellVisualState.Selected, "Selected 优先于 Movable");
        var selectedMaterial = cell.GetNode<MeshInstance3D>("HighlightMesh").MaterialOverride;
        cell.SetSelected(true);
        AssertTrue(ReferenceEquals(selectedMaterial, cell.GetNode<MeshInstance3D>("HighlightMesh").MaterialOverride), "重复状态更新不得创建新材质");
        cell.SetDisabled(true);
        AssertEqual(cell.VisualState, HexCellVisualState.Disabled, "Disabled 应具有最高优先级");
        AssertFalse(cell.TryActivate(), "禁用状态不得发送有效点击");
        AssertEqual(clickCount, 0, "禁用状态点击信号计数应保持不变");

        cell.SetDisabled(false);
        AssertTrue(cell.TryActivate(), "启用状态应允许激活");
        AssertEqual(clickedId, 88L, "点击信号应携带绑定的 cellId");
        AssertEqual(clickCount, 1, "一次有效激活应发送一次点击信号");

        cell.BeginPointerClick();
        AssertEqual(clickCount, 1, "左键按下时不应立即确认格子点击");
        AssertTrue(cell.CompletePointerClick(), "未取消的释放操作应确认格子点击");
        AssertEqual(clickCount, 2, "左键释放后应发送一次点击信号");
        cell.BeginPointerClick();
        cell.CancelPendingClick();
        AssertFalse(cell.CompletePointerClick(), "取消待确认点击后释放不得触发格子");
        AssertEqual(clickCount, 2, "取消的点击不得增加信号计数");
        cell.SetHovered(false);
        AssertEqual(hoverCount, 2, "悬停进入和离开应各发送一次信号");
    }

    private async Task VerifyCustomVisualSourcesAsync(HexCell3D cell, Mesh customMesh)
    {
        var baseMesh = cell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh");
        var customMeshInstance = cell.GetNode<MeshInstance3D>("VisualRoot/CustomMeshInstance");
        var customVisualRoot = cell.GetNode<Node3D>("VisualRoot/CustomVisualRoot");
        var highlightMesh = cell.GetNode<MeshInstance3D>("HighlightMesh");

        AssertTrue(cell.UsesCustomVisual, "设置 CustomMesh 后应进入自定义视觉模式");
        AssertTrue(ReferenceEquals(customMeshInstance.Mesh, customMesh), "Ready 前设置的 CustomMesh 必须保留原资源引用");
        AssertTrue(customMeshInstance.Visible, "CustomMesh 模式应显示 CustomMeshInstance");
        AssertFalse(baseMesh.Visible, "CustomMesh 模式应隐藏默认 BaseMesh");

        var originalCollision = cell.InteractionShape;
        var originalHighlight = highlightMesh.Mesh;
        cell.ConfigureGeometry(1.35f, 0.3f, 0.06f);
        AssertTrue(ReferenceEquals(customMeshInstance.Mesh, customMesh), "ConfigureGeometry 不得覆盖 CustomMesh");
        AssertFalse(ReferenceEquals(cell.InteractionShape, originalCollision), "自定义模式下仍应更新碰撞体");
        AssertFalse(ReferenceEquals(highlightMesh.Mesh, originalHighlight), "自定义模式下仍应更新 HighlightMesh");

        var clickedId = 0L;
        cell.CellClicked += id => clickedId = id;
        cell.BindData(new HexCellData
        {
            CellId = 301,
            CellCode = "C301",
            CellName = "自定义模型测试格",
            Q = 3,
            R = 1
        });
        cell.SetSelected(true);
        AssertEqual(cell.VisualState, HexCellVisualState.Selected, "自定义素材不得影响状态切换");
        AssertTrue(cell.TryActivate(), "自定义素材不得影响格子点击");
        AssertEqual(clickedId, 301L, "自定义素材模式的点击应携带绑定数据");

        var firstScene = CreatePackedVisual("FirstCustomVisual");
        cell.CustomVisualScene = firstScene;
        AssertTrue(cell.UsesCustomVisual, "设置 CustomVisualScene 后应保持自定义视觉模式");
        AssertFalse(baseMesh.Visible, "CustomVisualScene 模式应隐藏默认 BaseMesh");
        AssertFalse(customMeshInstance.Visible, "CustomVisualScene 的优先级应高于 CustomMesh");
        AssertEqual(customVisualRoot.GetChildCount(), 1, "CustomVisualScene 应实例化到 CustomVisualRoot");
        var oldInstance = customVisualRoot.GetChild(0);
        AssertEqual(oldInstance.Name.ToString(), "FirstCustomVisual", "应实例化指定的 CustomVisualScene");

        var secondScene = CreatePackedVisual("SecondCustomVisual");
        cell.CustomVisualScene = secondScene;
        AssertEqual(customVisualRoot.GetChildCount(), 1, "切换 CustomVisualScene 时不得残留旧实例");
        AssertFalse(ReferenceEquals(customVisualRoot.GetChild(0), oldInstance), "切换后应使用新的自定义场景实例");
        AssertEqual(customVisualRoot.GetChild(0).Name.ToString(), "SecondCustomVisual", "应显示切换后的 CustomVisualScene");
        await ToSignal(this, SignalName.ProcessFrame);
        AssertFalse(GodotObject.IsInstanceValid(oldInstance), "旧 CustomVisualScene 实例应被释放");

        cell.CustomVisualScene = null;
        AssertTrue(customMeshInstance.Visible, "清除 CustomVisualScene 后应回退到仍配置的 CustomMesh");
        AssertTrue(ReferenceEquals(customMeshInstance.Mesh, customMesh), "从 CustomVisualScene 回退时不得替换 CustomMesh");
        AssertEqual(customVisualRoot.GetChildCount(), 0, "清除 CustomVisualScene 后承载节点应为空");

        cell.ClearCustomVisual();
        AssertFalse(cell.UsesCustomVisual, "清除自定义配置后应恢复默认视觉模式");
        AssertTrue(baseMesh.Visible, "默认视觉模式应显示 BaseMesh");
        AssertFalse(customMeshInstance.Visible, "默认视觉模式应隐藏 CustomMeshInstance");
        AssertEqual(customMeshInstance.Mesh, (Mesh?)null, "清除自定义配置后不应保留 CustomMesh 引用");
        AssertTrue(cell.GeneratedMesh is ArrayMesh, "清除自定义配置后应恢复程序化 ArrayMesh");
        AssertApprox(cell.GeneratedMesh!.GetAabb().Size.Z, 2.7f, 0.001f, "恢复默认模型时应使用当前逻辑半径");
    }

    private PackedScene CreatePackedVisual(string rootName)
    {
        var source = new Node3D { Name = rootName };
        var packedScene = new PackedScene();
        var result = packedScene.Pack(source);
        source.Free();
        AssertEqual(result, Error.Ok, $"测试自定义场景 {rootName} 应可打包");
        return packedScene;
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("HexCell3D 测试全部通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }
        Quit(1);
    }

    private void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            _failures.Add(message);
        }
    }

    private void AssertFalse(bool value, string message)
    {
        AssertTrue(!value, message);
    }

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }

    private void AssertApprox(float actual, float expected, float tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            _failures.Add($"{message}，期望：{expected}±{tolerance}，实际：{actual}");
        }
    }
}
