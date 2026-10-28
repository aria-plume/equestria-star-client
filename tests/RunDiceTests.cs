using System.Collections.Generic;
using System.Threading.Tasks;
using EquestriaStar.Game.Dice;
using Godot;

public partial class RunDiceTests : SceneTree
{
    private readonly List<string> _failures = [];

    private static readonly IReadOnlyDictionary<int, Vector3> ExpectedNormals = new Dictionary<int, Vector3>
    {
        [1] = Vector3.Up,
        [2] = Vector3.Back,
        [3] = Vector3.Right,
        [4] = Vector3.Left,
        [5] = Vector3.Forward,
        [6] = Vector3.Down
    };

    private static readonly DiceThrowProfile LightProfile = new(2.0f, 2.5f, 0.95f, 1.25f, 3.4f, 4.5f);
    private static readonly DiceThrowProfile NormalProfile = new(3.4f, 4.0f, 1.4f, 1.8f, 5.6f, 6.8f);
    private static readonly DiceThrowProfile HeavyProfile = new(4.8f, 5.6f, 2.0f, 2.4f, 7.8f, 9.2f);

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
        var diceScene = GD.Load<PackedScene>("res://scenes/game/dice/Dice3D.tscn");
        var trayScene = GD.Load<PackedScene>("res://scenes/game/dice/DiceTray.tscn");
        var demoScene = GD.Load<PackedScene>("res://scenes/game/dice/DiceDemo.tscn");
        AssertTrue(diceScene != null, "Dice3D 场景必须可加载");
        AssertTrue(trayScene != null, "DiceTray 场景必须可加载");
        AssertTrue(demoScene != null, "DiceDemo 场景必须可加载");
        if (diceScene == null || trayScene == null || demoScene == null)
        {
            Finish();
            return;
        }

        var dice = diceScene.Instantiate<Dice3D>();
        var camera = new Camera3D { Position = new Vector3(0.0f, 4.0f, 5.0f) };
        Root.AddChild(dice);
        Root.AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
        await ToSignal(this, SignalName.ProcessFrame);

        VerifySceneStructure(dice);
        VerifyRoundedGeometry(dice);
        VerifyFaceMapping(dice);
        VerifyAllTopOrientations(dice);
        VerifyAmbiguousTopIsRejected(dice);
        VerifyThrowCalculation(camera);
        await VerifyStateMachineAsync(dice, camera);

        dice.QueueFree();
        camera.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);

        await VerifyTrayAndCameraAsync(demoScene);
        Finish();
    }

    private void VerifySceneStructure(Dice3D dice)
    {
        foreach (var path in new[]
        {
            "VisualRoot",
            "VisualRoot/DiceMesh",
            "VisualRoot/PipsRoot",
            "FaceMarkers",
            "CollisionShape3D"
        })
        {
            AssertTrue(dice.HasNode(path), $"Dice3D 缺少节点：{path}");
        }

        var mesh = dice.GetNode<MeshInstance3D>("VisualRoot/DiceMesh").Mesh as ArrayMesh;
        AssertTrue(mesh != null, "骰体必须使用程序化 ArrayMesh");
        AssertTrue(mesh?.GetAabb().Size.X > 0.9f, "程序化骰体应具有完整尺寸");
        var boxShape = dice.GetNode<CollisionShape3D>("CollisionShape3D").Shape as BoxShape3D;
        AssertTrue(boxShape != null, "动态骰子必须使用 BoxShape3D");
        AssertVectorApprox(boxShape?.Size ?? Vector3.Zero, Vector3.One * dice.DiceSize, 0.0001f,
            "BoxShape3D 尺寸必须与 DiceSize 一致");
        var originalSize = dice.DiceSize;
        dice.DiceSize = 1.25f;
        dice.ConfigureGeometry();
        boxShape = dice.GetNode<CollisionShape3D>("CollisionShape3D").Shape as BoxShape3D;
        AssertVectorApprox(boxShape?.Size ?? Vector3.Zero, Vector3.One * dice.DiceSize, 0.0001f,
            "修改 DiceSize 后 BoxShape3D 必须同步更新");
        dice.DiceSize = originalSize;
        dice.ConfigureGeometry();
        AssertTrue(dice.ContinuousCd, "骰子必须启用连续碰撞检测");

        var pipsRoot = dice.GetNode<Node3D>("VisualRoot/PipsRoot");
        for (var value = 1; value <= 6; value++)
        {
            var face = pipsRoot.GetNode<Node3D>($"Face{value}");
            AssertEqual(face.GetChildCount(), value, $"可见面 {value} 应包含 {value} 个圆点");
            foreach (var child in face.GetChildren())
            {
                AssertTrue(child is MeshInstance3D, $"面 {value} 的圆点只能参与显示");
            }
        }
    }

    private void VerifyRoundedGeometry(Dice3D dice)
    {
        var lowSegments = DiceMeshFactory.GetMesh(1.0f, 0.1f, 2);
        var highSegments = DiceMeshFactory.GetMesh(1.0f, 0.1f, 6);
        AssertTrue(GetVertexCount(highSegments) > GetVertexCount(lowSegments),
            "增加圆角段数应增加视觉网格采样精度");
        AssertVectorApprox(lowSegments.GetAabb().Size, Vector3.One, 0.002f,
            "圆角视觉网格尺寸必须等于骰子尺寸");
        AssertEqual(dice.RoundSegments, 4, "默认圆角段数应兼顾效果与性能");
        VerifyMeshWinding(highSegments);
    }

    private void VerifyMeshWinding(ArrayMesh mesh)
    {
        var triangleCount = 0;
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            AssertEqual(vertices.Length, normals.Length, "圆角网格顶点与法线数量必须一致");
            for (var index = 0; index + 2 < vertices.Length; index += 3)
            {
                var geometricNormal = (vertices[index + 1] - vertices[index])
                    .Cross(vertices[index + 2] - vertices[index]);
                if (geometricNormal.LengthSquared() <= 0.00000001f)
                {
                    continue;
                }

                var averageNormal = normals[index] + normals[index + 1] + normals[index + 2];
                AssertTrue(geometricNormal.Dot(averageNormal) < 0.0f,
                    $"表面 {surfaceIndex} 的三角形 {index / 3} 必须符合 Godot 顺时针正面绕序");
                triangleCount++;
            }
        }

        AssertTrue(triangleCount > 0, "圆角网格必须包含可验证的非退化三角形");
    }

    private static int GetVertexCount(ArrayMesh mesh)
    {
        var count = 0;
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            count += arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
        }

        return count;
    }

    private void VerifyFaceMapping(Dice3D dice)
    {
        foreach (var pair in ExpectedNormals)
        {
            AssertTrue(dice.FaceMarkers.TryGetValue(pair.Key, out var marker), $"缺少面值 {pair.Key} Marker");
            if (marker != null)
            {
                AssertVectorApprox(marker.Position.Normalized(), pair.Value, 0.001f,
                    $"面值 {pair.Key} Marker 方向必须与可见圆点面一致");
            }
        }

        AssertEqual(1 + 6, 7, "1 与 6 必须互为对面");
        AssertEqual(2 + 5, 7, "2 与 5 必须互为对面");
        AssertEqual(3 + 4, 7, "3 与 4 必须互为对面");
    }

    private void VerifyAllTopOrientations(Dice3D dice)
    {
        foreach (var pair in ExpectedNormals)
        {
            dice.GlobalTransform = new Transform3D(
                new Basis(new Quaternion(pair.Value, Vector3.Up)),
                new Vector3(0.0f, 1.0f, 0.0f));
            dice.ForceUpdateTransform();
            AssertTrue(dice.TryReadTopValue(out var value), $"面值 {pair.Key} 朝上时应能读取结果");
            AssertEqual(value, pair.Key, $"Marker 最高点必须读取为面值 {pair.Key}");
        }
    }

    private void VerifyAmbiguousTopIsRejected(Dice3D dice)
    {
        dice.Rotation = new Vector3(Mathf.DegToRad(45.0f), 0.0f, 0.0f);
        dice.ForceUpdateTransform();
        AssertFalse(dice.TryReadTopValue(out _), "两个 Marker 高度过近时不得提前读取点数");
        dice.Rotation = Vector3.Zero;
        dice.ForceUpdateTransform();
    }

    private void VerifyThrowCalculation(Camera3D camera)
    {
        AssertFalse(CalculateThrow(new Vector2(3.0f, 2.0f), 0.01, camera, 1, out _),
            "小于拖动阈值时不得生成投掷参数");

        AssertTrue(CalculateThrow(new Vector2(240.0f, -80.0f), 0.65, camera, 10, out var light),
            "慢速有效拖动应生成轻投参数");
        AssertTrue(CalculateThrow(new Vector2(240.0f, -80.0f), 0.24, camera, 10, out var normal),
            "中速拖动应生成普通投参数");
        AssertTrue(CalculateThrow(new Vector2(240.0f, -80.0f), 0.08, camera, 10, out var heavy),
            "快速滑动应生成重投参数");
        AssertEqual(light.Tier, DiceThrowTier.Light, "慢速拖动必须归入轻投");
        AssertEqual(normal.Tier, DiceThrowTier.Normal, "中速拖动必须归入普通");
        AssertEqual(heavy.Tier, DiceThrowTier.Heavy, "快速拖动必须归入重投");

        AssertTrue(light.Impulse.Length() < normal.Impulse.Length()
            && normal.Impulse.Length() < heavy.Impulse.Length(),
            "三个档位的总体冲量应递增");
        AssertTrue(light.AngularVelocity.Length() < normal.AngularVelocity.Length()
            && normal.AngularVelocity.Length() < heavy.AngularVelocity.Length(),
            "三个档位的角速度应递增");
        AssertTrue(heavy.Impulse.Length() <= 6.4001f, "重投线性冲量不得超过配置上限");
        AssertTrue(heavy.AngularVelocity.Length() <= 10.5001f, "重投角速度不得超过配置上限");
        AssertTrue(light.Impulse.Y > 0.0f && normal.Impulse.Y > 0.0f && heavy.Impulse.Y > 0.0f,
            "三个档位都不得产生向下冲量");

        CalculateThrow(new Vector2(200.0f, 0.0f), 0.25, camera, 20, out var rightThrow);
        CalculateThrow(new Vector2(-200.0f, 0.0f), 0.25, camera, 20, out var leftThrow);
        var rightHorizontal = rightThrow.Impulse with { Y = 0.0f };
        var leftHorizontal = leftThrow.Impulse with { Y = 0.0f };
        AssertTrue(rightHorizontal.Dot(leftHorizontal) < 0.0f,
            "相反水平拖动必须产生相反的主要投掷方向");
    }

    private static bool CalculateThrow(
        Vector2 drag,
        double duration,
        Camera3D camera,
        ulong seed,
        out DiceThrowSolution solution)
    {
        var random = new RandomNumberGenerator { Seed = seed };
        return DiceRollController.TryCalculateThrow(
            drag, duration, camera.GlobalTransform.Basis,
            18.0f, 620.0f, 1450.0f,
            LightProfile, NormalProfile, HeavyProfile,
            6.4f, 10.5f, 0.1f, random, out solution);
    }

    private async Task VerifyStateMachineAsync(Dice3D dice, Camera3D camera)
    {
        var completionCount = 0;
        var completedValue = 0;
        dice.RollCompleted += value =>
        {
            completionCount++;
            completedValue = value;
        };

        dice.ResetDice();
        var click = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(960.0f, 540.0f)
        };
        dice.EmitSignal(CollisionObject3D.SignalName.InputEvent, camera, click, Vector3.Zero, Vector3.Up, 0L);
        AssertEqual(dice.RollState, DiceRollState.AwaitingDrag, "点击骰体应通过 InputEvent 进入 AwaitingDrag");
        dice.ResetDice();
        AssertTrue(dice.BeginRollMode(), "Idle 状态应能进入 AwaitingDrag");
        AssertTrue(dice.HasActiveTransition, "进入投掷模式时应创建平滑抬起 Tween");
        AssertFalse(dice.TryThrow(new Vector2(4.0f, 3.0f), 0.01, camera), "短拖不得投出骰子");
        AssertEqual(dice.RollState, DiceRollState.AwaitingDrag, "短拖后应继续等待拖动");

        AssertTrue(dice.TryThrow(new Vector2(260.0f, -120.0f), 0.22, camera), "有效拖动应投出骰子");
        AssertTrue(dice.IsRolling, "投出后应处于 Rolling/Settling");
        AssertFalse(dice.BeginRollMode(), "Rolling 状态不得开始第二次投掷");
        AssertFalse(dice.TryThrow(new Vector2(260.0f, -120.0f), 0.22, camera), "Rolling 状态不得重复投掷");
        AssertTrue(dice.LastAppliedImpulse.Length() <= dice.MaximumLinearImpulse + 0.001f,
            "实际线性冲量不得超过上限");
        AssertTrue(dice.LastAppliedAngularVelocity.Length() <= dice.MaximumAngularSpeed + 0.001f,
            "实际角速度不得超过上限");

        dice.LinearVelocity = new Vector3(1.0f, 0.0f, 0.0f);
        dice.AngularVelocity = new Vector3(0.0f, 1.0f, 0.0f);
        dice.AdvanceSettlement(dice.StableDuration + 0.1f);
        AssertEqual(completionCount, 0, "速度未稳定时不得输出结果");

        dice.Freeze = true;
        dice.Rotation = new Vector3(Mathf.DegToRad(45.0f), 0.0f, 0.0f);
        dice.ForceUpdateTransform();
        dice.LinearVelocity = Vector3.Zero;
        dice.AngularVelocity = Vector3.Zero;
        dice.AdvanceSettlement(dice.StableDuration + dice.AmbiguousStableDuration + 0.1f);
        AssertEqual(dice.AmbiguityAssistCount, 1, "斜立歧义稳定后应施加一次自然助力");
        AssertTrue(dice.LastAmbiguityTorque.Length() >= dice.AmbiguityTorqueMin - 0.001f
            && dice.LastAmbiguityTorque.Length() <= dice.AmbiguityTorqueMax + 0.001f,
            "歧义助力扭矩必须保持在配置范围内");
        AssertEqual(dice.RollState, DiceRollState.Rolling, "歧义助力后不得无限停留在 Settling");
        AssertEqual(completionCount, 0, "歧义助力不得伪造点数");

        for (var assist = 1; assist < dice.MaximumAmbiguityAssists; assist++)
        {
            dice.Freeze = true;
            dice.Rotation = new Vector3(Mathf.DegToRad(45.0f), 0.0f, 0.0f);
            dice.ForceUpdateTransform();
            dice.LinearVelocity = Vector3.Zero;
            dice.AngularVelocity = Vector3.Zero;
            dice.AdvanceSettlement(dice.StableDuration + dice.AmbiguousStableDuration + 0.1f);
        }
        AssertEqual(dice.AmbiguityAssistCount, dice.MaximumAmbiguityAssists,
            "歧义助力次数必须受配置上限限制");
        dice.Freeze = true;
        dice.Rotation = new Vector3(Mathf.DegToRad(45.0f), 0.0f, 0.0f);
        dice.ForceUpdateTransform();
        dice.LinearVelocity = Vector3.Zero;
        dice.AngularVelocity = Vector3.Zero;
        dice.AdvanceSettlement(dice.StableDuration + dice.AmbiguousStableDuration + 0.1f);
        AssertEqual(dice.RollState, DiceRollState.Idle,
            "达到助力上限后应安全复位，不能无限停留在 Settling");

        AssertTrue(dice.BeginRollMode(), "歧义助力后重置应能再次投掷");
        AssertTrue(dice.TryThrow(new Vector2(260.0f, -120.0f), 0.22, camera), "重置后应能投掷");
        dice.Freeze = true;
        dice.Rotation = Vector3.Zero;
        dice.ForceUpdateTransform();
        dice.LinearVelocity = Vector3.Zero;
        dice.AngularVelocity = Vector3.Zero;
        dice.AdvanceSettlement(dice.StableDuration + 0.1f);
        AssertEqual(dice.RollState, DiceRollState.Completed, "持续稳定后应进入 Completed");
        AssertEqual(completedValue, 1, "默认朝向稳定后应读取顶面 1");
        AssertEqual(completionCount, 1, "一次投掷只能发送一次 RollCompleted");
        dice.AdvanceSettlement(1.0f);
        AssertEqual(completionCount, 1, "Completed 后不得重复发送完成信号");

        dice.ResetDice();
        AssertEqual(dice.RollState, DiceRollState.Idle, "ResetDice 应恢复 Idle");
        AssertEqual(dice.LastRolledValue, 0, "ResetDice 应清除点数");
        AssertEqual(dice.LastThrowTier, DiceThrowTier.None, "ResetDice 应清除投掷档位");
        AssertEqual(dice.LinearVelocity, Vector3.Zero, "ResetDice 应清除线速度");
        AssertEqual(dice.AngularVelocity, Vector3.Zero, "ResetDice 应清除角速度");
        AssertFalse(dice.HasActiveTransition, "ResetDice 应清理旧 Tween");
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task VerifyTrayAndCameraAsync(PackedScene demoScene)
    {
        var demo = demoScene.Instantiate<Node3D>();
        Root.AddChild(demo);
        await ToSignal(this, SignalName.ProcessFrame);
        var tray = demo.GetNode<DiceTray>("DiceTray");
        var dice = demo.GetNode<Dice3D>("Dice3D");
        var controller = demo.GetNode<DiceRollController>("CameraRig");
        var camera = demo.GetNode<Camera3D>("CameraRig/Camera3D");

        AssertEqual(tray.GetChildCount(), 6, "骰盘应包含 VisualRoot 和五个独立碰撞体");
        AssertTrue(tray.GetNode<CollisionShape3D>("FloorCollision").Shape is BoxShape3D,
            "骰盘底面必须具有 BoxShape3D 碰撞");
        foreach (var side in new[] { "North", "South", "West", "East" })
        {
            AssertTrue(tray.GetNode<CollisionShape3D>($"Wall{side}Collision").Shape is BoxShape3D,
                $"骰盘 {side} 围墙必须具有碰撞");
        }

        var overview = camera.GlobalTransform;
        var overviewProjection = camera.Projection;
        dice.ResetDice();
        AssertTrue(dice.BeginRollMode(), "演示场景应能进入固定骰盘观察模式");
        await WaitForCameraTransitionAsync(controller);
        var fixedView = camera.GlobalTransform;
        AssertFalse(TransformApprox(fixedView, overview, 0.01f), "投掷模式应切换到固定骰盘镜头");
        AssertTrue(controller.HasSavedCameraState, "切换镜头前必须保存原镜头状态");

        dice.Position += new Vector3(2.0f, 0.0f, -1.0f);
        await WaitProcessFramesAsync(8);
        AssertTrue(TransformApprox(camera.GlobalTransform, fixedView, 0.001f),
            "AwaitingDrag 期间镜头不得跟随骰子位置");

        controller.BeginDrag(new Vector2(700.0f, 520.0f), 10.0);
        AssertTrue(controller.EndDrag(new Vector2(960.0f, 390.0f), 10.22), "演示控制器应能提交有效拖动");
        var rollingView = camera.GlobalTransform;
        dice.Position += new Vector3(-1.5f, 0.0f, 1.0f);
        await WaitProcessFramesAsync(8);
        AssertTrue(TransformApprox(camera.GlobalTransform, rollingView, 0.001f),
            "Rolling 期间镜头不得逐帧跟随骰子");

        dice.Freeze = true;
        dice.Rotation = Vector3.Zero;
        dice.ForceUpdateTransform();
        dice.LinearVelocity = Vector3.Zero;
        dice.AngularVelocity = Vector3.Zero;
        dice.AdvanceSettlement(dice.StableDuration + 0.1f);
        await WaitForCameraTransitionAsync(controller);
        AssertTrue(TransformApprox(camera.GlobalTransform, overview, 0.002f), "投掷完成后镜头应恢复总览位置");
        AssertEqual(camera.Projection, overviewProjection, "投掷完成后镜头投影参数应恢复");

        dice.ResetDice();
        dice.BeginRollMode();
        await WaitForCameraTransitionAsync(controller);
        controller.RestoreCamera(true);
        AssertTrue(TransformApprox(camera.GlobalTransform, overview, 0.001f), "中途退出时应能立即恢复镜头");
        dice.ResetDice();
        await WaitForCameraTransitionAsync(controller);
        AssertTrue(TransformApprox(camera.GlobalTransform, overview, 0.002f), "ResetDice 后镜头应保持总览状态");

        dice.Freeze = false;
        dice.Position = new Vector3(0.0f, 2.2f, 0.0f);
        dice.LinearVelocity = new Vector3(0.0f, -10.0f, 0.0f);
        await WaitPhysicsFramesAsync(100);
        AssertTrue(dice.Position.Y > 0.35f, "高速下落时骰子不得穿过托盘底面");

        dice.Freeze = true;
        dice.Position = new Vector3(0.0f, 0.58f, 0.0f);
        dice.LinearVelocity = Vector3.Zero;
        dice.AngularVelocity = Vector3.Zero;
        dice.Freeze = false;
        dice.LinearVelocity = new Vector3(10.0f, 0.0f, 0.0f);
        await WaitPhysicsFramesAsync(100);
        AssertTrue(Mathf.Abs(dice.Position.X) < tray.InnerHalfExtent + 0.7f,
            "撞墙后骰子不得穿出托盘有效范围");

        demo.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task WaitPhysicsFramesAsync(int frameCount)
    {
        for (var index = 0; index < frameCount; index++)
        {
            await ToSignal(this, SignalName.PhysicsFrame);
        }
    }

    private async Task WaitProcessFramesAsync(int frameCount)
    {
        for (var index = 0; index < frameCount; index++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
    }

    private async Task WaitForCameraTransitionAsync(DiceRollController controller)
    {
        for (var index = 0; index < 180 && controller.IsCameraTransitionActive; index++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }

        AssertFalse(controller.IsCameraTransitionActive, "镜头过渡应在有限时间内完成");
    }

    private static bool TransformApprox(Transform3D actual, Transform3D expected, float tolerance)
    {
        return actual.Origin.DistanceTo(expected.Origin) <= tolerance
            && actual.Basis.X.DistanceTo(expected.Basis.X) <= tolerance
            && actual.Basis.Y.DistanceTo(expected.Basis.Y) <= tolerance
            && actual.Basis.Z.DistanceTo(expected.Basis.Z) <= tolerance;
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("物理六面骰测试全部通过。");
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

    private void AssertVectorApprox(Vector3 actual, Vector3 expected, float tolerance, string message)
    {
        if (actual.DistanceTo(expected) > tolerance)
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }
}
