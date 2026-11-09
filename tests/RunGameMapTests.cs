using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using Godot;

public partial class RunGameMapTests : SceneTree
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
        try
        {
            GD.Print("[GameMapTests] 基础纯逻辑检查");
            TestCoordinateConversion();
            TestLayoutConfiguration();
            TestValidationFailures();
            TestStaticVisualContract();
            TestMapInputBlockerTraversal();
            GD.Print("[GameMapTests] 构建器与摄像机检查");
            await TestBuilderAndCameraAsync();
            GD.Print("[GameMapTests] 折纸美术集成检查");
            await TestOrigamiVisualIntegrationAsync();
            GD.Print("[GameMapTests] 错误与重试检查");
            await TestErrorAndRetryAsync();
        }
        catch (Exception exception)
        {
            _failures.Add($"测试执行异常：{exception}");
        }

        Finish();
    }

    private void TestCoordinateConversion()
    {
        var basePosition = HexCoordinateConverter.ToBaseWorld(1, 5, 1.0f);
        AssertApprox(basePosition.X, Mathf.Sqrt(3.0f) * 3.5f, 0.0001f, "QR 转换的 X 公式应正确");
        AssertApprox(basePosition.Z, 7.5f, 0.0001f, "QR 转换的 Z 公式应正确");

        var a10 = HexCoordinateConverter.ToWorld(1, 5, "A", 1.0f);
        AssertVectorApprox(a10, basePosition, 0.0001f, "A10 不应附加板块偏移");

        foreach (var (q, r) in new[] { (-6, 11), (-3, 11), (-6, 12), (-4, 12) })
        {
            var position = HexCoordinateConverter.ToWorld(q, r, "I", 1.0f);
            var expected = HexCoordinateConverter.ToBaseWorld(q, r, 1.0f) + new Vector3(0.645f, 0.0f, 0.616f);
            AssertVectorApprox(position, expected, 0.0001f, $"I 板块坐标 ({q},{r}) 应应用固定偏移");
        }
    }

    private void TestLayoutConfiguration()
    {
        AssertTrue(MapLayoutConfig.TryGetBoardOffset("A", out var aOffset), "应存在 A 板块偏移");
        AssertTrue(MapLayoutConfig.TryGetBoardOffset("B", out var bOffset), "应存在 B 板块偏移");
        AssertEqual(aOffset, bOffset, "A/B 应使用同一物理偏移");
        AssertFalse(MapLayoutConfig.GetBoardColor("A").IsEqualApprox(MapLayoutConfig.GetBoardColor("B")), "A/B 应使用不同颜色");
        AssertTrue(MapLayoutConfig.TryGetBoardOffset("C", out var cOffset), "应存在 C 板块偏移");
        AssertVector2Approx(cOffset, new Vector2(-0.074f, -1.082f), 0.0001f, "C 板块偏移应使用确认值");
        AssertEqual(MapLayoutConfig.BoardOffsets.Count, 12, "应集中配置 A-L 共 12 个板块偏移");
        AssertEqual(MapLayoutConfig.BoardColors.Count, 12, "应集中配置 A-L 共 12 种颜色");
    }

    private void TestValidationFailures()
    {
        var duplicateQr = CreateValidMap();
        duplicateQr.Cells![20].Q = duplicateQr.Cells[19].Q;
        duplicateQr.Cells[20].R = duplicateQr.Cells[19].R;
        AssertInvalidContains(duplicateQr, "重复 QR", "重复 QR 坐标必须被拒绝");

        var missingCoordinate = CreateValidMap();
        missingCoordinate.Cells![25].Q = null;
        AssertInvalidContains(missingCoordinate, "缺少 q/r", "q/r 为空必须被拒绝");

        var unknownBoard = CreateValidMap();
        unknownBoard.Cells![30].BoardCode = "Z";
        unknownBoard.Cells[30].CellCode = "Z999";
        AssertInvalidContains(unknownBoard, "未知 boardCode", "未知板块必须被拒绝");

        var missingEdgeCell = CreateValidMap();
        missingEdgeCell.Edges![0].ToCellId = 9999999;
        AssertInvalidContains(missingEdgeCell, "不存在的格子", "边引用未知格子必须被拒绝");

        var duplicateCode = CreateValidMap();
        duplicateCode.Cells![1].CellCode = duplicateCode.Cells[0].CellCode;
        AssertInvalidContains(duplicateCode, "重复", "重复 cellCode 必须被拒绝");
    }

    private void TestMapInputBlockerTraversal()
    {
        var layout = new Control { Name = "IgnoreLayout", MouseFilter = Control.MouseFilterEnum.Ignore };
        var blocker = new PanelContainer { Name = "BlockerPanel" };
        var nestedLabel = new Label { Name = "NestedLabel", Text = "测试" };
        var ordinaryPanel = new PanelContainer { Name = "OrdinaryPanel" };
        Root.AddChild(layout);
        layout.AddChild(blocker);
        blocker.AddChild(nestedLabel);
        layout.AddChild(ordinaryPanel);
        blocker.AddToGroup(MapCameraController.MapInputBlockerGroup);

        AssertFalse(MapCameraController.IsMapInputBlocked(layout), "全屏 Ignore 布局不得阻止地图输入");
        AssertFalse(MapCameraController.IsMapInputBlocked(ordinaryPanel), "未标记的 Control 不得仅因 MouseFilter 阻止地图输入");
        AssertTrue(MapCameraController.IsMapInputBlocked(blocker), "标记面板必须阻止地图输入");
        AssertTrue(MapCameraController.IsMapInputBlocked(nestedLabel), "拦截面板的子控件必须通过父节点 Group 阻止地图输入");
        AssertFalse(MapCameraController.IsMapInputBlocked(null), "没有悬停控件时必须允许地图输入");

        layout.QueueFree();
    }

    private void TestStaticVisualContract()
    {
        var valid = OfficialMapStaticVisualLoader.Load(CreateValidMap());
        AssertTrue(valid.Ok && valid.Instance != null && valid.Contract != null,
            $"正式 PCK 与服务端地图夹具应满足静态契约：{valid.Error}");
        if (valid.Contract != null)
        {
            AssertEqual(valid.Contract.Anchors.Count, 138, "静态契约必须包含 138 个唯一锚点");
            AssertEqual(valid.Contract.StaticVisualCellCount, 138, "静态契约必须包含 138 个视觉格");
            AssertEqual(valid.Contract.Anchors["A10"].Q, 1, "A10 锚点 q 必须为 1");
            AssertEqual(valid.Contract.Anchors["A10"].R, 5, "A10 锚点 r 必须为 5");
            AssertEqual(valid.Contract.Anchors["B24"].Q, -6, "B24 锚点 q 必须为 -6");
            AssertEqual(valid.Contract.Anchors["B24"].R, 10, "B24 锚点 r 必须为 10");
            AssertEqual(valid.Contract.Anchors["I1"].Q, -6, "I1 锚点 q 必须为 -6");
            AssertEqual(valid.Contract.Anchors["I1"].R, 11, "I1 锚点 r 必须为 11");
        }
        valid.Instance?.Free();

        var wrongMapId = CreateValidMap();
        wrongMapId.MapId = 2;
        var wrongMapIdResult = OfficialMapStaticVisualLoader.Load(wrongMapId);
        AssertFalse(wrongMapIdResult.Ok, "mapId 不匹配时必须硬失败");

        var wrongCodeSet = CreateValidMap();
        var ordinary = wrongCodeSet.Cells!.First(cell => cell.CellCode == "A1");
        ordinary.CellCode = "A99";
        var wrongCodeResult = OfficialMapStaticVisualLoader.Load(wrongCodeSet);
        AssertFalse(wrongCodeResult.Ok && wrongCodeResult.Instance != null, "cellCode 集合不一致时必须硬失败");

        var missingCell = CreateValidMap();
        missingCell.Cells!.RemoveAt(0);
        AssertFalse(OfficialMapStaticVisualLoader.Load(missingCell).Ok, "服务端缺少格子时必须硬失败");

        var extraCell = CreateValidMap();
        extraCell.Cells!.Add(new MapCellDto
        {
            CellId = 999999,
            CellCode = "A999",
            BoardCode = "A",
            CellName = "额外格子",
            CellNameText = "额外格子",
            Q = 999,
            R = 999,
            InteractionTags = []
        });
        AssertFalse(OfficialMapStaticVisualLoader.Load(extraCell).Ok, "服务端多出格子时必须硬失败");

        var wrongQr = CreateValidMap();
        wrongQr.Cells!.First(cell => cell.CellCode == "A10").Q = 2;
        var wrongQrResult = OfficialMapStaticVisualLoader.Load(wrongQr);
        AssertFalse(wrongQrResult.Ok, "服务端 QR 与锚点不一致时必须硬失败");
        AssertEqual(OfficialMapStaticVisualLoader.MountCount, 1, "静态 PCK 在进程内只能挂载一次");
    }

    private async Task TestBuilderAndCameraAsync()
    {
        var cellScene = GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn");
        AssertTrue(cellScene != null, "地图构建测试必须能加载 HexCell3D");
        if (cellScene == null)
        {
            return;
        }

        var mapRoot = new Node3D { Name = "TestMapRoot" };
        Root.AddChild(mapRoot);
        var builder = new GameMapBuilder();
        var map = CreateGenericBuilderMap();
        var firstBuild = builder.Build(map, mapRoot, cellScene, 0.55f, 0.16f, 0.035f, true);
        AssertTrue(firstBuild.Ok, $"有效地图应成功构建：{firstBuild.Error}");
        AssertEqual(firstBuild.CellCount, 138, "构建器应生成全部 138 个格子");
        AssertEqual(firstBuild.EdgeCount, 385, "构建器应保留全部 385 条边");
        AssertEqual(builder.CellsById.Count, 138, "应建立完整 cellId 字典");
        AssertEqual(builder.CellsByCode.Count, 138, "应建立完整 cellCode 字典");
        AssertEqual(builder.Edges.Count, 385, "构建器应保留边 DTO");
        foreach (var cell in builder.CellsById.Values)
        {
            var nameLabel = cell.GetNode<Label3D>("CellNameLabel");
            AssertTrue(nameLabel.Visible, $"格子 {cell.Data?.CellCode} 应默认显示名称");
            AssertEqual(
                nameLabel.Text.Replace("\n", ""),
                cell.Data?.DisplayName ?? "",
                $"格子 {cell.Data?.CellCode} 应绑定统一 DisplayName"
            );
            AssertFalse(nameLabel.Text.Contains('('), $"格子 {cell.Data?.CellCode} 不得显示 QR 坐标");
        }

        var aCell = builder.CellsByCode["A10"];
        var bCell = builder.CellsByCode["B24"];
        var aMaterial = aCell.CustomVisualMaterialOverride;
        var bMaterial = bCell.CustomVisualMaterialOverride;
        AssertTrue(aMaterial != null && bMaterial != null, "普通格应使用板块色通用地形材质");
        AssertFalse(ReferenceEquals(aMaterial, bMaterial), "A/B 格子应使用不同的板块材质");
        aCell.SetHovered(true);
        aCell.SetHovered(false);
        AssertEqual(aCell.VisualState, HexCellVisualState.Normal, "悬停结束后应恢复 Normal 状态");
        AssertTrue(
            ReferenceEquals(aMaterial, aCell.CustomVisualMaterialOverride),
            "状态高亮结束后应恢复原板块材质"
        );

        var oldGeneratedRoot = mapRoot.GetChild(0);
        var oldCell = aCell;
        var secondBuild = builder.Build(CreateGenericBuilderMap(), mapRoot, cellScene, 0.55f, 0.16f, 0.035f, false);
        AssertTrue(secondBuild.Ok, "重复加载有效地图应成功");
        AssertEqual(mapRoot.GetChildCount(), 1, "重复加载后 MapRoot 只能保留一个生成根节点");
        AssertFalse(ReferenceEquals(mapRoot.GetChild(0), oldGeneratedRoot), "重复加载应替换旧生成根节点");
        AssertFalse(oldCell.IsInsideTree(), "旧格子应立即离开场景树");
        AssertFalse(
            builder.CellsByCode["A10"].GetNode<Label3D>("CellNameLabel").Visible,
            "构建时关闭 ShowCellNames 应隐藏全部名称"
        );

        var plan = MapCameraFitter.Calculate(secondBuild.Bounds, 16.0f / 9.0f);
        AssertTrue(MapCameraFitter.ContainsBounds(plan, 16.0f / 9.0f), "1920x1080 正交取景应包含全部格子");
        AssertTrue(plan.OrthographicSize > 0.0f, "摄像机正交尺寸必须为正数");

        mapRoot.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task TestErrorAndRetryAsync()
    {
        var apiClient = Root.GetNode<ApiClient>("ApiClient");
        var transport = new MapTransport(CreateValidMap());
        apiClient.SetTransportForTests(transport);

        var packedScene = GD.Load<PackedScene>("res://scenes/game/map/GameMapScene.tscn");
        AssertTrue(packedScene != null, "GameMapScene 应可加载");
        if (packedScene == null)
        {
            apiClient.ClearTransportForTests();
            return;
        }

        var scene = packedScene.Instantiate<GameMapScene>();
        scene.AutoLoadOnReady = false;
        Root.AddChild(scene);
        await ToSignal(this, SignalName.ProcessFrame);

        await scene.LoadMapAsync();
        AssertTrue(scene.ErrorVisible, "API 失败时应显示错误状态");
        AssertTrue(scene.LastError.Contains("模拟网络失败", StringComparison.Ordinal), "错误状态应保留后端或网络消息");
        AssertEqual(scene.GeneratedCellCount, 0, "API 失败时不得生成残缺地图");
        var retryButton = scene.GetNode<Button>("CanvasLayer/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/RetryButton");
        AssertTrue(retryButton.Visible && !retryButton.Disabled, "失败后重试按钮应可用");

        retryButton.EmitSignal(Button.SignalName.Pressed);
        for (var frame = 0; frame < 8 && (scene.IsLoading || scene.GeneratedCellCount == 0); frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        AssertFalse(scene.ErrorVisible, "重试成功后应隐藏错误状态");
        AssertEqual(scene.GeneratedCellCount, 138, "重试成功后应生成完整地图");
        AssertTrue(scene.IsUsingStaticVisual, "正式场景必须使用静态 PCK 视觉");
        AssertEqual(scene.StaticVisualCellCount, 138, "静态视觉必须包含 138 个格子");
        AssertEqual(scene.StaticVisualRoot.GetChildCount(), 1, "静态视觉根只能包含一个地图实例");
        AssertEqual(scene.InteractionRoot.GetChildCount(), 1, "交互根只能包含一个生成根节点");
        AssertEqual(scene.Edges.Count, 385, "正式场景必须保留服务端 385 条边");
        AssertFalse(ContainsCollisionObject(scene.StaticMapInstance!), "静态地图视觉不得包含碰撞对象");
        AssertFalse(ContainsInputBlocker(scene.StaticMapInstance!), "静态地图视觉不得阻塞地图输入");
        AssertTrue(scene.VisualRuntime.Diagnostics != null, "静态地图必须接入 MapVisualRuntime");
        AssertEqual(CountNodes<WorldEnvironment>(scene.StaticMapInstance!), 1, "正式场景只能存在一套 WorldEnvironment");
        AssertEqual(CountNodes<DirectionalLight3D>(scene.StaticMapInstance!), 2, "正式场景只能存在 P9 主光和补光");
        AssertTrue(scene.CellsById.Values.All(cell => !cell.GetNode<Node3D>("VisualRoot").Visible), "轻量交互格不得重复显示格子本体");
        AssertTrue(scene.CellsById.Values.All(cell => cell.HasNode("HighlightMesh") && cell.HasNode("TokenRoot")), "交互格必须保留高亮与棋子挂点");
        AssertTrue(scene.CellsByCode.All(pair => pair.Value.Transform.IsEqualApprox(scene.StaticContract!.Anchors[pair.Key].CellTransform)),
            "所有逻辑格子必须直接使用静态锚点 Transform");
        AssertTrue(
            scene.CellsByCode.Values.All(cell =>
                Mathf.IsZeroApprox(cell.TokenRoot.Position.X)
                && Mathf.IsZeroApprox(cell.TokenRoot.Position.Z)),
            "静态地图棋子挂点不得叠加建筑避让的水平偏移"
        );
        var townHallAnchor = scene.StaticContract!.Anchors["A10"];
        var townHallTokenRoot = scene.CellsByCode["A10"].TokenRoot;
        AssertApprox(townHallTokenRoot.Position.Y, townHallAnchor.TokenTransform.Origin.Y, 0.0001f,
            "居中棋子挂点仍应保留烘焙高度");
        AssertEqual(transport.CallCount, 2, "重试只应重新发送一次地图请求");
        AssertEqual(transport.Paths[0], "/maps/main", "默认场景应请求正式主地图相对路径");
        AssertFalse(transport.AuthorizedValues[0], "正式主地图请求应允许未登录独立运行");

        var mountCount = OfficialMapStaticVisualLoader.MountCount;
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
        AssertEqual(OfficialMapStaticVisualLoader.MountCount, mountCount, "静态 PCK 不得在每帧重复挂载");

        await VerifyCameraInteractionAsync(scene);
        AssertEqual(OfficialMapStaticVisualLoader.MountCount, mountCount, "地图重载必须复用已挂载 PCK");
        AssertEqual(scene.StaticVisualRoot.GetChildCount(), 1, "地图重载后不得残留旧静态实例");
        AssertEqual(scene.InteractionRoot.GetChildCount(), 1, "地图重载后不得残留旧交互实例");

        scene.ClearLoadedMap();
        await ToSignal(this, SignalName.ProcessFrame);
        AssertEqual(scene.GeneratedCellCount, 0, "清理后不得保留逻辑格子");
        AssertEqual(scene.StaticVisualRoot.GetChildCount(), 0, "清理后不得保留静态地图实例");
        AssertEqual(scene.InteractionRoot.GetChildCount(), 0, "清理后不得保留交互根实例");
        AssertTrue(scene.VisualRuntime.Diagnostics == null, "清理时 MapVisualRuntime 必须 Detach");

        scene.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        apiClient.ClearTransportForTests();
    }

    private async Task TestOrigamiVisualIntegrationAsync()
    {
        var cellScene = GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn");
        AssertTrue(cellScene != null, "折纸美术测试必须能加载 HexCell3D");
        if (cellScene == null)
        {
            return;
        }

        var catalog = new OrigamiVisualCatalog();
        AssertTrue(catalog.TryResolve("市政厅", "不会使用的后备名称", out var townHallEntry), "应按 cellName 匹配市政厅");
        AssertEqual(townHallEntry.AssetId, "town-hall", "cellName 的匹配优先级应高于 cellNameText");
        AssertTrue(catalog.TryResolve("未知名称", "时装店", out var fashionEntry), "cellName 未匹配时应回退 cellNameText");
        AssertEqual(fashionEntry.AssetId, "fashion-shop", "cellNameText 应匹配时装店素材");
        AssertTrue(catalog.TryResolve("水晶集市", "水晶集市", out var crystalMarketEntry), "候选素材也应保留结构化映射");
        AssertFalse(crystalMarketEntry.Enabled, "水晶集市候选素材本阶段不得启用");
        AssertEqual(catalog.EntryCount, 44, "当前项目映射应覆盖全部 44 组折纸素材");
        var expectedTerrainMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["A"] = "grass-light",
            ["B"] = "grass-dark",
            ["C"] = "crystal-ground",
            ["D"] = "snow",
            ["E"] = "grass-light",
            ["F"] = "stone",
            ["G"] = "snow",
            ["H"] = "stone",
            ["I"] = "sand",
            ["J"] = "sand",
            ["K"] = "grass-light",
            ["L"] = "grass-dark"
        };
        AssertEqual(catalog.BoardTerrainMappings.Count, 12, "A-L 全部板块都必须配置通用地形");
        foreach (var pair in expectedTerrainMappings)
        {
            AssertTrue(
                catalog.BoardTerrainMappings.TryGetValue(pair.Key, out var assetId) && assetId == pair.Value,
                $"板块 {pair.Key} 应映射到 {pair.Value}"
            );
            AssertTrue(catalog.TryResolveGenericTerrain(pair.Key, out var terrainEntry), $"板块 {pair.Key} 的通用地形应可解析");
            AssertEqual(terrainEntry.AssetId, pair.Value, $"板块 {pair.Key} 的通用地形 assetId");
        }

        var firstTownHallScene = catalog.LoadScene(townHallEntry);
        var secondTownHallScene = catalog.LoadScene(townHallEntry);
        AssertTrue(firstTownHallScene != null, "市政厅包装场景必须可加载");
        AssertTrue(ReferenceEquals(firstTownHallScene, secondTownHallScene), "相同 assetId 必须复用 PackedScene 缓存");

        var mapRoot = new Node3D { Name = "OrigamiMapRoot" };
        Root.AddChild(mapRoot);
        var builder = new GameMapBuilder();
        var artMap = CreateArtMappedMap();
        var result = builder.Build(artMap, mapRoot, cellScene, 0.55f, 0.16f, 0.035f, true);
        AssertTrue(result.Ok, $"折纸美术地图应成功构建：{result.Error}");
        await ToSignal(this, SignalName.ProcessFrame);

        AssertEqual(result.CellCount, 138, "美术替换后仍应生成 138 个逻辑格子");
        AssertEqual(result.EdgeCount, 385, "美术替换后仍应保留 385 条边");
        AssertEqual(builder.DedicatedVisualCellCount, 64, "正式地标素材应精确应用到 64 个格子");
        AssertEqual(builder.GenericTerrainCellCount, 74, "其余 74 个格子应使用所属板块的通用地形");
        AssertEqual(builder.FallbackVisualCellCount, 0, "资源完整时不应出现程序化六边形兜底");
        AssertEqual(builder.AppliedVisualCellCount, 138, "全部 138 个格子都应获得美术视觉");
        AssertEqual(builder.CachedVisualSceneCount, 41, "运行时应缓存 35 种地标和 6 种通用地形场景");
        AssertTrue(builder.CellsByCode.Values.All(cell => cell.UsesCustomVisual), "正常情况下所有格子都应使用自定义美术视觉");

        foreach (var code in new[] { "A10", "A16", "A2", "B24" })
        {
            AssertTrue(builder.CellsByCode[code].UsesCustomVisual, $"代表性地标 {code} 应使用折纸素材");
        }

        var candidateTerrainByCode = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["C9"] = "crystal-ground",
            ["E2"] = "grass-light",
            ["F3"] = "stone"
        };
        foreach (var pair in candidateTerrainByCode)
        {
            var code = pair.Key;
            var candidateCell = builder.CellsByCode[code];
            AssertTrue(candidateCell.UsesCustomVisual, $"候选格子 {code} 应回退所属板块通用地形");
            AssertTrue(
                candidateCell.CustomVisualScene?.ResourcePath.Contains($"/{pair.Value}/", StringComparison.Ordinal) == true,
                $"候选格子 {code} 应使用 {pair.Value}，不得启用候选地标"
            );
            AssertFalse(candidateCell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh").Visible, $"候选格子 {code} 不应显示旧程序化六边形");
        }

        var ordinaryCell = builder.CellsByCode.Values.First(cell => cell.Data?.CellName == "普通格");
        AssertTrue(ordinaryCell.UsesCustomVisual, "普通格应按 boardCode 使用通用地形");
        AssertTrue(ordinaryCell.CustomVisualMaterialOverride != null, "普通格通用地形应应用板块色材质");
        AssertFalse(ordinaryCell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh").Visible, "普通格不应显示旧程序化六边形");

        var genericCells = builder.CellsByCode.Values
            .Where(cell => cell.CustomVisualMaterialOverride != null)
            .ToList();
        AssertEqual(genericCells.Count, 74, "应有 74 个格子使用板块色通用地形");
        AssertEqual(genericCells.Select(cell => cell.Data?.BoardCode).Distinct().Count(), 12, "通用地形应覆盖 A-L 全部板块");
        foreach (var cell in genericCells)
        {
            AssertTrue(
                ReferenceEquals(cell.CustomVisualMaterialOverride, MapLayoutConfig.GetGenericTerrainMaterial(cell.Data!.BoardCode)),
                $"格子 {cell.Data.CellCode} 应复用所属板块的缓存材质"
            );
            AssertTrue(
                cell.CustomVisualMaterialOverride is StandardMaterial3D terrainMaterial
                    && terrainMaterial.AlbedoTexture == null,
                $"格子 {cell.Data.CellCode} 的通用地形不得误用纸张法线纹理作为 Albedo"
            );
        }

        var townHall = builder.CellsByCode["A10"];
        var townHallVisualRoot = townHall.GetNode<Node3D>("VisualRoot/CustomVisualRoot");
        AssertEqual(townHallVisualRoot.GetChildCount(), 1, "包装场景只能实例化一次");
        AssertApprox(townHallVisualRoot.Scale.X, 0.55f, 0.001f, "源半径 1 的美术应跟随逻辑半径缩放");
        AssertApprox(townHallVisualRoot.RotationDegrees.Y, 30.0f, 0.001f, "折纸素材应统一绕 Y 轴旋转 30 度");
        AssertFalse(ContainsCollisionObject(townHallVisualRoot.GetChild(0)), "纯美术包装不得增加碰撞对象");
        AssertTrue(townHall.HasNode("InteractionArea/CollisionShape3D"), "替换视觉后应保留逻辑碰撞体");
        AssertTrue(townHall.HasNode("HighlightMesh"), "替换视觉后应保留高亮层");
        AssertTrue(townHall.HasNode("TokenRoot"), "替换视觉后应保留棋子挂点");
        townHall.SetCurrent(true);
        AssertEqual(townHall.VisualState, HexCellVisualState.Current, "折纸素材不得影响当前位置高亮");
        townHall.SetMovable(true);
        AssertEqual(townHall.VisualState, HexCellVisualState.Movable, "折纸素材不得影响可移动高亮");
        townHall.SetMovable(false);
        townHall.SetCurrent(false);
        var clickedTownHallId = 0L;
        townHall.CellClicked += cellId => clickedTownHallId = cellId;
        AssertTrue(townHall.TryActivate(), "折纸素材格仍应允许点击");
        AssertEqual(clickedTownHallId, townHall.Data?.CellId ?? 0L, "折纸素材格点击应返回原逻辑 cellId");

        ordinaryCell.SetHovered(true);
        AssertEqual(ordinaryCell.VisualState, HexCellVisualState.Hovered, "通用地形格应支持悬停高亮");
        ordinaryCell.SetHovered(false);
        ordinaryCell.SetMovable(true);
        AssertEqual(ordinaryCell.VisualState, HexCellVisualState.Movable, "通用地形格应支持移动高亮");
        ordinaryCell.SetMovable(false);
        var clickedOrdinaryId = 0L;
        ordinaryCell.CellClicked += cellId => clickedOrdinaryId = cellId;
        AssertTrue(ordinaryCell.TryActivate(), "通用地形格仍应允许点击");
        AssertEqual(clickedOrdinaryId, ordinaryCell.Data?.CellId ?? 0L, "通用地形格点击应返回原逻辑 cellId");
        AssertFalse(
            ContainsCollisionObject(ordinaryCell.GetNode<Node3D>("VisualRoot/CustomVisualRoot")),
            "通用地形包装不得增加碰撞对象"
        );

        var forests = builder.CellsByCode.Values.Where(cell => cell.Data?.CellName == "无尽之森").ToList();
        AssertEqual(forests.Count, 30, "测试地图应包含 30 个无尽之森实例");
        AssertTrue(forests.All(cell => ReferenceEquals(cell.CustomVisualScene, forests[0].CustomVisualScene)), "重复森林格应共享同一 PackedScene");

        mapRoot.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task VerifyCameraInteractionAsync(GameMapScene scene)
    {
        var controller = scene.CameraController;
        var camera = controller.Camera;
        var initialSize = controller.InitialSize;
        var initialPosition = controller.Position;
        var initialCameraRotation = camera.Rotation;
        var initialCameraHeight = camera.GlobalPosition.Y;
        AssertApprox(camera.Size, initialSize, 0.001f, "地图生成后应处于初始全图 Size");
        AssertTrue(initialSize > 0.0f, "初始正交 Size 必须大于零");

        var inputLayer = scene.GetNode<CanvasLayer>("CanvasLayer");
        var ignoreLayout = new Control { Name = "InputTestLayout", MouseFilter = Control.MouseFilterEnum.Ignore };
        var blockerPanel = new PanelContainer { Name = "InputTestBlocker" };
        var blockerChild = new Label { Text = "阻止地图输入" };
        inputLayer.AddChild(ignoreLayout);
        inputLayer.AddChild(blockerPanel);
        blockerPanel.AddChild(blockerChild);
        blockerPanel.AddToGroup(MapCameraController.MapInputBlockerGroup);
        var viewportCenter = scene.GetViewport().GetVisibleRect().Size * 0.5f;
        var wheelUp = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp,
            Pressed = true,
            Position = viewportCenter
        };
        var sizeBeforeBlockedWheel = controller.TargetSize;
        controller.RouteInput(wheelUp, blockerChild);
        AssertApprox(controller.TargetSize, sizeBeforeBlockedWheel, 0.0001f, "拦截面板上的滚轮不得缩放地图");
        controller.RouteInput(wheelUp, ignoreLayout);
        AssertTrue(controller.TargetSize < sizeBeforeBlockedWheel, "Ignore 布局上的滚轮必须能够缩放地图");
        controller.ResetToInitialView();

        controller.BeginPointerGesture(viewportCenter, Vector3.Zero);
        controller.UpdatePointerGesture(viewportCenter + new Vector2(20, 20), new Vector3(-1.0f, 0.0f, 1.0f));
        AssertTrue(controller.IsDragging, "进入 HUD 前应已开始地图拖动");
        var positionAtBlocker = controller.Position;
        controller.RouteInput(new InputEventMouseMotion { Position = viewportCenter + new Vector2(30, 30) }, blockerChild);
        AssertFalse(controller.IsDragging, "拖动进入拦截面板时必须立即结束拖动");
        AssertFalse(controller.IsPointerDown, "拖动进入拦截面板时必须清理按下状态");
        controller.RouteInput(new InputEventMouseMotion { Position = viewportCenter + new Vector2(60, 60) }, ignoreLayout);
        AssertVectorApprox(controller.Position, positionAtBlocker, 0.0001f, "离开 UI 后不得恢复旧拖动手势");

        controller.RouteInput(
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = viewportCenter
            },
            blockerChild
        );
        AssertFalse(controller.IsPointerDown, "从拦截面板按下时不得开始地图手势");
        ignoreLayout.QueueFree();
        blockerPanel.QueueFree();

        controller.ApplyZoomStep(true);
        AssertTrue(controller.TargetSize < initialSize, "滚轮向上应减小目标 Size");
        for (var index = 0; index < 80; index++)
        {
            controller.ApplyZoomStep(true);
        }
        AssertApprox(controller.TargetSize, controller.MinimumSize, 0.001f, "缩放目标不得小于最小 Size");

        for (var index = 0; index < 160; index++)
        {
            controller.ApplyZoomStep(false);
        }
        AssertApprox(controller.TargetSize, controller.MaximumSize, 0.001f, "滚轮向下目标不得大于最大 Size");
        AssertTrue(controller.TargetSize > initialSize, "滚轮向下应增大目标 Size");
        controller._Process(1.0);
        controller.MoveTo(new Vector3(9999, controller.Position.Y, -9999));
        AssertApprox(controller.PanMinimum.X, controller.PanMaximum.X, 0.001f, "视口宽于地图时 X 轴应自动居中");
        AssertApprox(controller.PanMinimum.Y, controller.PanMaximum.Y, 0.001f, "视口高于地图时 Z 轴应自动居中");
        AssertApprox(controller.Position.X, controller.PanMinimum.X, 0.001f, "宽视口下 X 位置应保持地图中心");
        AssertApprox(controller.Position.Z, controller.PanMinimum.Y, 0.001f, "高视口下 Z 位置应保持地图中心");

        controller.ResetToInitialView();
        for (var index = 0; index < 8; index++)
        {
            controller.ApplyZoomStep(true);
        }
        controller._Process(1.0);
        var beforeDrag = controller.Position;
        controller.BeginPointerGesture(new Vector2(100, 100), Vector3.Zero);
        controller.UpdatePointerGesture(new Vector2(103, 103), new Vector3(-0.2f, 0.0f, 0.2f));
        AssertFalse(controller.IsDragging, "小于拖动阈值时不应进入拖动状态");
        AssertVectorApprox(controller.Position, beforeDrag, 0.0001f, "小于阈值时摄像机不得移动");
        controller.UpdatePointerGesture(new Vector2(120, 118), new Vector3(-0.8f, 0.0f, 0.7f));
        AssertTrue(controller.IsDragging, "超过拖动阈值后应进入拖动状态");
        AssertTrue(
            Math.Abs(controller.Position.X - beforeDrag.X) > 0.0001f
                || Math.Abs(controller.Position.Z - beforeDrag.Z) > 0.0001f,
            "拖动应改变 CameraRig 的 X/Z"
        );
        AssertApprox(controller.Position.Y, beforeDrag.Y, 0.0001f, "拖动不得改变 CameraRig 的 Y");
        AssertVectorApprox(camera.Rotation, initialCameraRotation, 0.0001f, "拖动不得改变摄像机旋转");
        AssertApprox(camera.GlobalPosition.Y, initialCameraHeight, 0.0001f, "拖动不得改变摄像机高度");
        controller.EndPointerGesture();

        controller.MoveTo(new Vector3(9999, controller.Position.Y, -9999));
        AssertTrue(controller.Position.X <= controller.PanMaximum.X + 0.001f, "平移 X 不得超过最大边界");
        AssertTrue(controller.Position.X >= controller.PanMinimum.X - 0.001f, "平移 X 不得超过最小边界");
        AssertTrue(controller.Position.Z <= controller.PanMaximum.Y + 0.001f, "平移 Z 不得超过最大边界");
        AssertTrue(controller.Position.Z >= controller.PanMinimum.Y - 0.001f, "平移 Z 不得超过最小边界");

        controller.ResetToInitialView();
        var cell = scene.CellsByCode["A10"];
        var clickCount = 0;
        var emptyClickCount = 0;
        scene.MapEmptyClicked += () => emptyClickCount++;
        controller.BeginPointerGesture(new Vector2(180, 180), Vector3.Zero);
        controller.EndPointerGesture(new Vector2(180, 180));
        await ToSignal(this, SignalName.ProcessFrame);
        AssertEqual(emptyClickCount, 1, "未命中格子的短按释放应发送地图空白点击");

        cell.CellClicked += _ => clickCount++;
        cell.BeginPointerClick();
        controller.BeginPointerGesture(new Vector2(200, 200), Vector3.Zero);
        controller.UpdatePointerGesture(new Vector2(203, 202), new Vector3(-0.1f, 0.0f, 0.1f));
        controller.EndPointerGesture(new Vector2(203, 202));
        AssertTrue(cell.CompletePointerClick(), "小于阈值的操作仍应允许点击格子");
        await ToSignal(this, SignalName.ProcessFrame);
        AssertEqual(clickCount, 1, "小幅移动后释放应发送 CellClicked");
        AssertEqual(emptyClickCount, 1, "格子点击不得同时发送地图空白点击");

        cell.BeginPointerClick();
        controller.BeginPointerGesture(new Vector2(200, 200), Vector3.Zero);
        controller.UpdatePointerGesture(new Vector2(220, 220), new Vector3(-1.0f, 0.0f, 1.0f));
        controller.EndPointerGesture(new Vector2(220, 220));
        await ToSignal(this, SignalName.ProcessFrame);
        AssertFalse(cell.CompletePointerClick(), "从格子表面开始拖动后不得确认点击");
        AssertEqual(clickCount, 1, "拖动地图不得误发 CellClicked");
        AssertEqual(emptyClickCount, 2, "拖动开始时应发送一次空白操作以退出移动模式");

        controller.BeginPointerGesture(new Vector2(50, 50), Vector3.Zero);
        controller.UpdatePointerGesture(new Vector2(70, 70), new Vector3(-1.0f, 0.0f, 1.0f));
        AssertTrue(controller.IsDragging, "失焦测试前应处于拖动状态");
        controller._Notification((int)Node.NotificationWMWindowFocusOut);
        AssertFalse(controller.IsDragging, "窗口失去焦点后应取消拖动");
        AssertFalse(controller.IsPointerDown, "窗口失去焦点后应清理左键状态");

        var uiButton = new Button { Text = "测试 UI" };
        scene.GetNode<CanvasLayer>("CanvasLayer").AddChild(uiButton);
        var sizeBeforeUi = controller.TargetSize;
        var positionBeforeUi = controller.Position;
        uiButton.EmitSignal(Button.SignalName.Pressed);
        AssertApprox(controller.TargetSize, sizeBeforeUi, 0.0001f, "操作 UI 按钮不得缩放地图");
        AssertVectorApprox(controller.Position, positionBeforeUi, 0.0001f, "操作 UI 按钮不得移动地图");
        uiButton.QueueFree();

        controller.ApplyZoomStep(true);
        controller.MoveTo(new Vector3(controller.PanMaximum.X, controller.Position.Y, controller.PanMaximum.Y));
        await scene.LoadMapAsync();
        AssertApprox(controller.TargetSize, controller.InitialSize, 0.001f, "地图重新加载后应恢复初始全图 Size");
        AssertVectorApprox(controller.Position, initialPosition, 0.001f, "地图重新加载后应恢复初始中心位置");
    }

    private static GameMapDto CreateArtMappedMap()
    {
        var map = CreateGenericBuilderMap();
        var formalNames = new List<string>
        {
            "市政厅", "无尽之森", "骏鹰王国", "时装店", "暮光城堡", "方糖屋", "甜苹果园", "集市", "小蝶家", "小马谷火车站",
            "友谊学院", "云宝家", "旧城堡", "泽可拉小屋", "韵律之树", "耗耗斯坦", "水晶帝国", "运动场", "水晶城堡",
            "狮鹫岩", "马哈顿火车站", "马哈顿", "中心城火车站", "皇城", "商业街", "地狱", "巴尔的马", "巨龙之地",
            "苹果鲁萨", "平等镇", "彩虹瀑布镇", "彩虹瀑布", "云中城", "天马维加斯", "天气工厂"
        };
        while (formalNames.Count < 64)
        {
            formalNames.Add("无尽之森");
        }

        foreach (var cell in map.Cells!)
        {
            cell.CellName = "普通格";
            cell.CellNameText = "普通格";
        }

        var formalIndices = new List<int> { 0, 1, 5 };
        formalIndices.AddRange(Enumerable.Range(6, 61));
        for (var index = 0; index < formalNames.Count; index++)
        {
            var cell = map.Cells[formalIndices[index]];
            cell.CellName = formalNames[index];
            cell.CellNameText = formalNames[index];
        }

        SetCellIdentity(map.Cells, 0, "A10", "A", "市政厅");
        SetCellIdentity(map.Cells, 1, "B24", "B", "无尽之森");
        SetCellIdentity(map.Cells, 5, "I7", "I", "骏鹰王国");
        SetCellIdentity(map.Cells, 6, "A16", "A", "时装店");
        SetCellIdentity(map.Cells, 7, "A2", "A", "暮光城堡");
        SetCellIdentity(map.Cells, 67, "C9", "C", "水晶集市");
        SetCellIdentity(map.Cells, 68, "E2", "E", "高档场所");
        SetCellIdentity(map.Cells, 69, "F3", "F", "宴会厅");
        return map;
    }

    private static void SetCellIdentity(List<MapCellDto> cells, int index, string code, string board, string name)
    {
        cells[index].CellCode = code;
        cells[index].BoardCode = board;
        cells[index].CellName = name;
        cells[index].CellNameText = name;
    }

    private static bool ContainsCollisionObject(Node node)
    {
        if (node is CollisionObject3D)
        {
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (ContainsCollisionObject(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInputBlocker(Node node)
    {
        if (node.IsInGroup(MapCameraController.MapInputBlockerGroup))
        {
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (ContainsInputBlocker(child))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountNodes<T>(Node node) where T : Node
    {
        var count = node is T ? 1 : 0;
        foreach (var child in node.GetChildren()) count += CountNodes<T>(child);
        return count;
    }

    private void VerifyEnvironmentOutsidePlayableCells(GameMapScene scene)
    {
        const float clearance = 0.27f;
        var cells = scene.CellsById.Values.ToList();
        var playableMinX = cells.Min(cell => cell.GlobalPosition.X - cell.Radius) - clearance;
        var playableMaxX = cells.Max(cell => cell.GlobalPosition.X + cell.Radius) + clearance;
        var playableMinZ = cells.Min(cell => cell.GlobalPosition.Z - cell.Radius) - clearance;
        var playableMaxZ = cells.Max(cell => cell.GlobalPosition.Z + cell.Radius) + clearance;
        var generatedEnvironment = scene.GetNode<Node3D>("MapEnvironmentRoot/GeneratedEnvironment");

        foreach (var child in generatedEnvironment.GetChildren())
        {
            if (child is not Node3D module || module.Name == "WaterBackground")
            {
                continue;
            }

            AssertTrue(TryGetGlobalVisualBounds(module, out var visualBounds), $"环境模块 {module.Name} 应包含可见模型");
            var overlapsPlayableArea = visualBounds.End.X > playableMinX
                && visualBounds.Position.X < playableMaxX
                && visualBounds.End.Z > playableMinZ
                && visualBounds.Position.Z < playableMaxZ;
            AssertFalse(overlapsPlayableArea, $"环境模块 {module.Name} 不得穿插可玩格子区域");
        }
    }

    private static bool TryGetGlobalVisualBounds(Node node, out Aabb bounds)
    {
        bounds = default;
        var hasBounds = false;
        CollectGlobalVisualBounds(node, ref bounds, ref hasBounds);
        return hasBounds;
    }

    private static void CollectGlobalVisualBounds(Node node, ref Aabb bounds, ref bool hasBounds)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            var visualBounds = TransformBounds(meshInstance.GetAabb(), meshInstance.GlobalTransform);
            bounds = hasBounds ? bounds.Merge(visualBounds) : visualBounds;
            hasBounds = true;
        }

        foreach (var child in node.GetChildren())
        {
            CollectGlobalVisualBounds(child, ref bounds, ref hasBounds);
        }
    }

    private static Aabb TransformBounds(Aabb bounds, Transform3D transform)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (var x = 0; x <= 1; x++)
        {
            for (var y = 0; y <= 1; y++)
            {
                for (var z = 0; z <= 1; z++)
                {
                    var corner = bounds.Position + new Vector3(
                        bounds.Size.X * x,
                        bounds.Size.Y * y,
                        bounds.Size.Z * z
                    );
                    var transformed = transform * corner;
                    minimum = new Vector3(
                        Math.Min(minimum.X, transformed.X),
                        Math.Min(minimum.Y, transformed.Y),
                        Math.Min(minimum.Z, transformed.Z)
                    );
                    maximum = new Vector3(
                        Math.Max(maximum.X, transformed.X),
                        Math.Max(maximum.Y, transformed.Y),
                        Math.Max(maximum.Z, transformed.Z)
                    );
                }
            }
        }

        return new Aabb(minimum, maximum - minimum);
    }

    public static GameMapDto CreateValidMap()
    {
        var json = FileAccess.GetFileAsString("res://tests/fixtures/main-map-20261024.json");
        var envelope = JsonSerializer.Deserialize<ApiEnvelope<GameMapDto>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        return envelope?.Data ?? throw new InvalidOperationException("无法读取正式地图测试夹具。");
    }

    private static GameMapDto CreateGenericBuilderMap()
    {
        var cells = new List<MapCellDto>();
        AddCell(cells, "A10", "A", 1, 5);
        AddCell(cells, "B24", "B", -6, 10);
        AddCell(cells, "I1", "I", -6, 11);
        AddCell(cells, "I4", "I", -3, 11);
        AddCell(cells, "I5", "I", -6, 12);
        AddCell(cells, "I7", "I", -4, 12);

        for (var index = 0; cells.Count < 138; index++)
        {
            var board = ((char)('A' + index % 12)).ToString();
            AddCell(cells, $"{board}{100 + index}", board, 20 + index % 22, 20 + index / 22);
        }

        var edges = new List<MapEdgeDto>();
        for (var from = 0; from < cells.Count && edges.Count < 385; from++)
        {
            for (var to = from + 1; to < cells.Count && edges.Count < 385; to++)
            {
                var edgeType = edges.Count switch
                {
                    < 188 => "NORMAL",
                    < 274 => "EVERFREE",
                    < 287 => "STATION_ROUTE",
                    _ => "GAP"
                };
                edges.Add(new MapEdgeDto
                {
                    EdgeId = 200000 + edges.Count,
                    FromCellId = cells[from].CellId,
                    ToCellId = cells[to].CellId,
                    EdgeType = edgeType,
                    EdgeTypeText = edgeType,
                    Passable = edgeType != "GAP",
                    RequiredAbility = edgeType == "GAP" ? "GAP_JUMP" : null
                });
            }
        }

        return new GameMapDto
        {
            MapId = 1,
            MapName = "离线测试地图",
            Cells = cells,
            Edges = edges
        };
    }

    private static void AddCell(List<MapCellDto> cells, string code, string board, int q, int r)
    {
        cells.Add(new MapCellDto
        {
            CellId = 100000 + cells.Count,
            CellCode = code,
            BoardCode = board,
            CellName = $"格子 {code}",
            CellNameText = $"测试格 {code}",
            Q = q,
            R = r,
            InteractionTags = []
        });
    }

    private void AssertInvalidContains(GameMapDto map, string expectedText, string message)
    {
        var result = MapDataValidator.Validate(map);
        AssertFalse(result.IsValid, message);
        AssertTrue(result.Error.Contains(expectedText, StringComparison.Ordinal), $"{message}，实际错误：{result.Error}");
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("3D 地图离线测试全部通过。");
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

    private void AssertVectorApprox(Vector3 actual, Vector3 expected, float tolerance, string message)
    {
        if (!actual.IsEqualApprox(expected) && actual.DistanceTo(expected) > tolerance)
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }

    private void AssertVector2Approx(Vector2 actual, Vector2 expected, float tolerance, string message)
    {
        if (actual.DistanceTo(expected) > tolerance)
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }

    private sealed class MapTransport(GameMapDto map) : IApiTransport
    {
        public int CallCount { get; private set; }
        public List<string> Paths { get; } = [];
        public List<bool> AuthorizedValues { get; } = [];

        public Task<ApiResult<T>> RequestJsonAsync<T>(
            HttpClient.Method method,
            string path,
            object? body,
            bool authorized,
            IReadOnlyDictionary<string, string?>? query
        )
        {
            CallCount++;
            Paths.Add(path);
            AuthorizedValues.Add(authorized);
            if (CallCount == 1)
            {
                return Task.FromResult(ApiResult<T>.Failure("模拟网络失败", "NETWORK_ERROR"));
            }

            if (typeof(T) == typeof(GameMapDto))
            {
                return Task.FromResult((ApiResult<T>)(object)ApiResult<GameMapDto>.Success(map, "success", 200));
            }

            return Task.FromResult(ApiResult<T>.Failure("测试传输层收到未知 DTO。", "UNEXPECTED_TYPE"));
        }
    }
}
