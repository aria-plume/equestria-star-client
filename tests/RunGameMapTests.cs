using System;
using System.Collections.Generic;
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
        TestCoordinateConversion();
        TestLayoutConfiguration();
        TestValidationFailures();
        TestMapInputBlockerTraversal();
        await TestBuilderAndCameraAsync();
        await TestErrorAndRetryAsync();
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
        var map = CreateValidMap();
        var firstBuild = builder.Build(map, mapRoot, cellScene, 0.55f, 0.16f, 0.035f, true);
        AssertTrue(firstBuild.Ok, $"有效地图应成功构建：{firstBuild.Error}");
        AssertEqual(firstBuild.CellCount, 138, "构建器应生成全部 138 个格子");
        AssertEqual(firstBuild.EdgeCount, 385, "构建器应保留全部 385 条边");
        AssertEqual(builder.CellsById.Count, 138, "应建立完整 cellId 字典");
        AssertEqual(builder.CellsByCode.Count, 138, "应建立完整 cellCode 字典");
        AssertEqual(builder.Edges.Count, 385, "构建器应保留边 DTO");

        var aCell = builder.CellsByCode["A10"];
        var bCell = builder.CellsByCode["B24"];
        var aMaterial = aCell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh").GetSurfaceOverrideMaterial(0);
        var bMaterial = bCell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh").GetSurfaceOverrideMaterial(0);
        AssertFalse(ReferenceEquals(aMaterial, bMaterial), "A/B 格子应使用不同的板块材质");
        aCell.SetHovered(true);
        aCell.SetHovered(false);
        AssertEqual(aCell.VisualState, HexCellVisualState.Normal, "悬停结束后应恢复 Normal 状态");
        AssertTrue(
            ReferenceEquals(aMaterial, aCell.GetNode<MeshInstance3D>("VisualRoot/BaseMesh").GetSurfaceOverrideMaterial(0)),
            "状态高亮结束后应恢复原板块材质"
        );

        var oldGeneratedRoot = mapRoot.GetChild(0);
        var oldCell = aCell;
        var secondBuild = builder.Build(CreateValidMap(), mapRoot, cellScene, 0.55f, 0.16f, 0.035f, false);
        AssertTrue(secondBuild.Ok, "重复加载有效地图应成功");
        AssertEqual(mapRoot.GetChildCount(), 1, "重复加载后 MapRoot 只能保留一个生成根节点");
        AssertFalse(ReferenceEquals(mapRoot.GetChild(0), oldGeneratedRoot), "重复加载应替换旧生成根节点");
        AssertFalse(oldCell.IsInsideTree(), "旧格子应立即离开场景树");

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
        AssertEqual(transport.CallCount, 2, "重试只应重新发送一次地图请求");
        AssertEqual(transport.Paths[0], "/maps/main", "默认场景应请求正式主地图相对路径");
        AssertFalse(transport.AuthorizedValues[0], "正式主地图请求应允许未登录独立运行");

        await VerifyCameraInteractionAsync(scene);

        scene.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        apiClient.ClearTransportForTests();
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
        cell.CellClicked += _ => clickCount++;
        cell.BeginPointerClick();
        controller.BeginPointerGesture(new Vector2(200, 200), Vector3.Zero);
        controller.UpdatePointerGesture(new Vector2(203, 202), new Vector3(-0.1f, 0.0f, 0.1f));
        controller.EndPointerGesture();
        AssertTrue(cell.CompletePointerClick(), "小于阈值的操作仍应允许点击格子");
        AssertEqual(clickCount, 1, "小幅移动后释放应发送 CellClicked");

        cell.BeginPointerClick();
        controller.BeginPointerGesture(new Vector2(200, 200), Vector3.Zero);
        controller.UpdatePointerGesture(new Vector2(220, 220), new Vector3(-1.0f, 0.0f, 1.0f));
        controller.EndPointerGesture();
        AssertFalse(cell.CompletePointerClick(), "从格子表面开始拖动后不得确认点击");
        AssertEqual(clickCount, 1, "拖动地图不得误发 CellClicked");

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

    public static GameMapDto CreateValidMap()
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
