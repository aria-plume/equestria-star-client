using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Game.Dice;

public enum DiceRollState
{
    Idle,
    AwaitingDrag,
    Rolling,
    Settling,
    Completed
}

public partial class Dice3D : RigidBody3D
{
    [Signal] public delegate void RollStartedEventHandler();
    [Signal] public delegate void RollCompletedEventHandler(int value);
    [Signal] public delegate void RollStateChangedEventHandler(int state);

    [ExportGroup("模型")]
    [Export(PropertyHint.Range, "0.4,2.0,0.01")] public float DiceSize { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0.01,0.24,0.005")] public float CornerRadius { get; set; } = 0.1f;
    [Export(PropertyHint.Range, "2,10,1")] public int RoundSegments { get; set; } = 4;
    [Export(PropertyHint.Range, "0.001,0.1,0.001")] public float MarkerOffset { get; set; } = 0.025f;

    [ExportGroup("位置")]
    [Export] public Vector3 ResetPosition { get; set; } = new(0.0f, 0.56f, 0.0f);
    [Export] public Vector3 ReadyPosition { get; set; } = new(0.0f, 1.45f, 0.0f);
    [Export(PropertyHint.Range, "0.05,1.5,0.01")] public float LiftDuration { get; set; } = 0.28f;

    [ExportGroup("投掷判定")]
    [Export(PropertyHint.Range, "2.0,100.0,1.0")] public float MinimumDragDistance { get; set; } = 18.0f;
    [Export(PropertyHint.Range, "100.0,2000.0,10.0")] public float LightMaximumPixelsPerSecond { get; set; } = 620.0f;
    [Export(PropertyHint.Range, "300.0,4000.0,10.0")] public float HeavyMinimumPixelsPerSecond { get; set; } = 1450.0f;

    [ExportGroup("轻投")]
    [Export] public float LightHorizontalMin { get; set; } = 2.0f;
    [Export] public float LightHorizontalMax { get; set; } = 2.5f;
    [Export] public float LightUpwardMin { get; set; } = 0.95f;
    [Export] public float LightUpwardMax { get; set; } = 1.25f;
    [Export] public float LightAngularMin { get; set; } = 3.4f;
    [Export] public float LightAngularMax { get; set; } = 4.5f;

    [ExportGroup("普通")]
    [Export] public float NormalHorizontalMin { get; set; } = 3.4f;
    [Export] public float NormalHorizontalMax { get; set; } = 4.0f;
    [Export] public float NormalUpwardMin { get; set; } = 1.4f;
    [Export] public float NormalUpwardMax { get; set; } = 1.8f;
    [Export] public float NormalAngularMin { get; set; } = 5.6f;
    [Export] public float NormalAngularMax { get; set; } = 6.8f;

    [ExportGroup("重投")]
    [Export] public float HeavyHorizontalMin { get; set; } = 4.8f;
    [Export] public float HeavyHorizontalMax { get; set; } = 5.6f;
    [Export] public float HeavyUpwardMin { get; set; } = 2.0f;
    [Export] public float HeavyUpwardMax { get; set; } = 2.4f;
    [Export] public float HeavyAngularMin { get; set; } = 7.8f;
    [Export] public float HeavyAngularMax { get; set; } = 9.2f;

    [ExportGroup("投掷限制")]
    [Export(PropertyHint.Range, "1.0,12.0,0.1")] public float MaximumLinearImpulse { get; set; } = 6.4f;
    [Export(PropertyHint.Range, "1.0,30.0,0.1")] public float MaximumAngularSpeed { get; set; } = 10.5f;
    [Export(PropertyHint.Range, "0.0,0.5,0.01")] public float RotationAxisJitter { get; set; } = 0.1f;

    [ExportGroup("静止判定")]
    [Export(PropertyHint.Range, "0.01,1.0,0.01")] public float LinearStopThreshold { get; set; } = 0.1f;
    [Export(PropertyHint.Range, "0.01,2.0,0.01")] public float AngularStopThreshold { get; set; } = 0.14f;
    [Export(PropertyHint.Range, "0.1,2.0,0.05")] public float StableDuration { get; set; } = 0.55f;
    [Export(PropertyHint.Range, "2.0,20.0,0.5")] public float MaximumRollDuration { get; set; } = 8.0f;
    [Export(PropertyHint.Range, "0.001,0.3,0.001")] public float MinimumMarkerHeightDifference { get; set; } = 0.08f;
    [Export(PropertyHint.Range, "0.05,1.0,0.05")] public float AmbiguousStableDuration { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "1,8,1")] public int MaximumAmbiguityAssists { get; set; } = 3;
    [Export(PropertyHint.Range, "0.01,1.0,0.01")] public float AmbiguityTorqueMin { get; set; } = 0.08f;
    [Export(PropertyHint.Range, "0.01,1.0,0.01")] public float AmbiguityTorqueMax { get; set; } = 0.18f;
    [Export(PropertyHint.Range, "0.1,3.0,0.1")] public float MaximumAmbiguityAngularSpeed { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0.5,12.0,0.1")] public float AssistedLinearDamp { get; set; } = 5.0f;
    [Export(PropertyHint.Range, "0.5,12.0,0.1")] public float AssistedAngularDamp { get; set; } = 6.0f;

    [ExportGroup("安全范围")]
    [Export(PropertyHint.Range, "1.0,20.0,0.1")] public float SafeHalfExtent { get; set; } = 4.2f;
    [Export(PropertyHint.Range, "-10.0,0.0,0.1")] public float MinimumSafeY { get; set; } = -1.5f;

    private static readonly IReadOnlyDictionary<int, Vector3> FaceNormals = new Dictionary<int, Vector3>
    {
        [1] = Vector3.Up,
        [2] = Vector3.Back,
        [3] = Vector3.Right,
        [4] = Vector3.Left,
        [5] = Vector3.Forward,
        [6] = Vector3.Down
    };

    private readonly Dictionary<int, Marker3D> _faceMarkers = new();
    private readonly RandomNumberGenerator _random = new();
    private MeshInstance3D _diceMesh = null!;
    private Node3D _pipsRoot = null!;
    private CollisionShape3D _collisionShape = null!;
    private Tween? _positionTween;
    private double _stableElapsed;
    private double _ambiguousStableElapsed;
    private double _rollElapsed;
    private float _normalLinearDamp;
    private float _normalAngularDamp;
    private bool _completionSent;

    public int LastRolledValue { get; private set; }
    public bool IsRolling => RollState is DiceRollState.Rolling or DiceRollState.Settling;
    public DiceRollState RollState { get; private set; } = DiceRollState.Idle;
    public DiceThrowTier LastThrowTier { get; private set; } = DiceThrowTier.None;
    public float LastThrowPixelsPerSecond { get; private set; }
    public Vector3 LastAppliedImpulse { get; private set; }
    public Vector3 LastAppliedAngularVelocity { get; private set; }
    public Vector3 LastAmbiguityTorque { get; private set; }
    public int AmbiguityAssistCount { get; private set; }
    public bool HasActiveTransition => GodotObject.IsInstanceValid(_positionTween) && _positionTween!.IsRunning();
    public IReadOnlyDictionary<int, Marker3D> FaceMarkers => _faceMarkers;

    public override void _Ready()
    {
        _diceMesh = GetNode<MeshInstance3D>("VisualRoot/DiceMesh");
        _pipsRoot = GetNode<Node3D>("VisualRoot/PipsRoot");
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
        for (var value = 1; value <= 6; value++)
        {
            _faceMarkers[value] = GetNode<Marker3D>($"FaceMarkers/Face{value}Marker");
        }

        _random.Randomize();
        _normalLinearDamp = LinearDamp;
        _normalAngularDamp = AngularDamp;
        InputRayPickable = true;
        InputEvent += OnInputEvent;
        ConfigureGeometry();
        ResetDice();
    }

    public override void _ExitTree()
    {
        KillPositionTween();
        InputEvent -= OnInputEvent;
    }

    public override void _PhysicsProcess(double delta)
    {
        AdvanceSettlement(delta);
    }

    public bool BeginRollMode()
    {
        if (RollState != DiceRollState.Idle)
        {
            return false;
        }

        KillPositionTween();
        Freeze = true;
        Sleeping = false;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        ClearRollResult();
        RestoreDamping();
        SetRollState(DiceRollState.AwaitingDrag);
        _positionTween = CreateTween();
        _positionTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _positionTween.TweenProperty(this, "position", ReadyPosition, LiftDuration);
        return true;
    }

    public bool TryThrow(Vector2 screenDrag, double durationSeconds, Camera3D camera)
    {
        var lightProfile = new DiceThrowProfile(LightHorizontalMin, LightHorizontalMax,
            LightUpwardMin, LightUpwardMax, LightAngularMin, LightAngularMax);
        var normalProfile = new DiceThrowProfile(NormalHorizontalMin, NormalHorizontalMax,
            NormalUpwardMin, NormalUpwardMax, NormalAngularMin, NormalAngularMax);
        var heavyProfile = new DiceThrowProfile(HeavyHorizontalMin, HeavyHorizontalMax,
            HeavyUpwardMin, HeavyUpwardMax, HeavyAngularMin, HeavyAngularMax);
        if (RollState != DiceRollState.AwaitingDrag
            || !DiceRollController.TryCalculateThrow(
                screenDrag, durationSeconds, camera.GlobalTransform.Basis,
                MinimumDragDistance, LightMaximumPixelsPerSecond, HeavyMinimumPixelsPerSecond,
                lightProfile, normalProfile, heavyProfile,
                MaximumLinearImpulse, MaximumAngularSpeed, RotationAxisJitter, _random,
                out var solution))
        {
            return false;
        }

        KillPositionTween();
        Freeze = false;
        Sleeping = false;
        RestoreDamping();
        LastThrowTier = solution.Tier;
        LastThrowPixelsPerSecond = solution.PixelsPerSecond;
        LastAppliedImpulse = solution.Impulse;
        LastAppliedAngularVelocity = solution.AngularVelocity;
        ApplyCentralImpulse(solution.Impulse);
        AngularVelocity = solution.AngularVelocity;
        _stableElapsed = 0.0;
        _ambiguousStableElapsed = 0.0;
        _rollElapsed = 0.0;
        SetRollState(DiceRollState.Rolling);
        EmitSignal(SignalName.RollStarted);
        return true;
    }

    public void ResetDice()
    {
        KillPositionTween();
        Freeze = true;
        Sleeping = false;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Position = ResetPosition;
        Quaternion = Quaternion.Identity;
        ClearRollResult();
        RestoreDamping();
        SetRollState(DiceRollState.Idle);
    }

    public bool TryReadTopValue(out int value)
    {
        value = 0;
        if (_faceMarkers.Count != 6)
        {
            return false;
        }

        var ordered = _faceMarkers
            .Select(pair => (Value: pair.Key, Height: pair.Value.GlobalPosition.Y))
            .OrderByDescending(item => item.Height)
            .ToArray();
        if (ordered[0].Height - ordered[1].Height < MinimumMarkerHeightDifference)
        {
            return false;
        }

        value = ordered[0].Value;
        return true;
    }

    public void AdvanceSettlement(double delta)
    {
        if (!IsRolling)
        {
            return;
        }

        if (IsOutsideSafeArea())
        {
            GD.PushWarning("骰子已离开骰盘安全范围，执行安全复位。");
            ResetDice();
            return;
        }

        _rollElapsed += delta;
        if (_rollElapsed >= MaximumRollDuration)
        {
            LinearDamp = Mathf.Max(LinearDamp, AssistedLinearDamp);
            AngularDamp = Mathf.Max(AngularDamp, AssistedAngularDamp);
        }

        var stable = LinearVelocity.Length() <= LinearStopThreshold
            && AngularVelocity.Length() <= AngularStopThreshold;
        if (!stable)
        {
            _stableElapsed = 0.0;
            _ambiguousStableElapsed = 0.0;
            SetRollState(DiceRollState.Rolling);
            return;
        }

        SetRollState(DiceRollState.Settling);
        _stableElapsed += delta;
        if (_stableElapsed < StableDuration)
        {
            return;
        }

        if (TryReadTopValue(out var value))
        {
            CompleteRoll(value);
            return;
        }

        _ambiguousStableElapsed += delta;
        if (_ambiguousStableElapsed < AmbiguousStableDuration)
        {
            return;
        }

        if (AmbiguityAssistCount >= MaximumAmbiguityAssists)
        {
            GD.PushWarning("骰子多次自然助力后仍无法确定顶面，执行安全复位。");
            ResetDice();
            return;
        }

        ApplyAmbiguityAssist();
    }

    public void ConfigureGeometry()
    {
        _diceMesh.Mesh = DiceMeshFactory.GetMesh(DiceSize, CornerRadius, RoundSegments);
        if (_collisionShape.Shape is not BoxShape3D boxShape)
        {
            boxShape = new BoxShape3D();
            _collisionShape.Shape = boxShape;
        }

        boxShape.Size = Vector3.One * DiceSize;
        _pipsRoot.Scale = Vector3.One * DiceSize;

        var markerDistance = DiceSize * 0.5f + MarkerOffset;
        foreach (var pair in FaceNormals)
        {
            _faceMarkers[pair.Key].Position = pair.Value * markerDistance;
        }
    }

    private void ApplyAmbiguityAssist()
    {
        var horizontal = new Vector3(
            _random.RandfRange(-1.0f, 1.0f), 0.0f,
            _random.RandfRange(-1.0f, 1.0f));
        if (horizontal.LengthSquared() < 0.001f)
        {
            horizontal = Vector3.Right;
        }

        var magnitude = _random.RandfRange(
            Mathf.Min(AmbiguityTorqueMin, AmbiguityTorqueMax),
            Mathf.Max(AmbiguityTorqueMin, AmbiguityTorqueMax));
        LastAmbiguityTorque = horizontal.Normalized() * magnitude;
        Freeze = false;
        Sleeping = false;
        ApplyTorqueImpulse(LastAmbiguityTorque);
        if (AngularVelocity.Length() > MaximumAmbiguityAngularSpeed)
        {
            AngularVelocity = AngularVelocity.Normalized() * MaximumAmbiguityAngularSpeed;
        }

        AmbiguityAssistCount++;
        _stableElapsed = 0.0;
        _ambiguousStableElapsed = 0.0;
        SetRollState(DiceRollState.Rolling);
    }

    private void CompleteRoll(int value)
    {
        if (_completionSent)
        {
            return;
        }

        _completionSent = true;
        LastRolledValue = value;
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        SetRollState(DiceRollState.Completed);
        EmitSignal(SignalName.RollCompleted, value);
    }

    private bool IsOutsideSafeArea()
    {
        return Mathf.Abs(Position.X - ResetPosition.X) > SafeHalfExtent
            || Mathf.Abs(Position.Z - ResetPosition.Z) > SafeHalfExtent
            || Position.Y < MinimumSafeY;
    }

    private void ClearRollResult()
    {
        LastRolledValue = 0;
        LastThrowTier = DiceThrowTier.None;
        LastThrowPixelsPerSecond = 0.0f;
        LastAppliedImpulse = Vector3.Zero;
        LastAppliedAngularVelocity = Vector3.Zero;
        LastAmbiguityTorque = Vector3.Zero;
        AmbiguityAssistCount = 0;
        _stableElapsed = 0.0;
        _ambiguousStableElapsed = 0.0;
        _rollElapsed = 0.0;
        _completionSent = false;
    }

    private void RestoreDamping()
    {
        LinearDamp = _normalLinearDamp;
        AngularDamp = _normalAngularDamp;
    }

    private void SetRollState(DiceRollState state)
    {
        if (RollState == state)
        {
            return;
        }

        RollState = state;
        EmitSignal(SignalName.RollStateChanged, (int)state);
    }

    private void KillPositionTween()
    {
        if (GodotObject.IsInstanceValid(_positionTween))
        {
            _positionTween!.Kill();
        }

        _positionTween = null;
    }

    private void OnInputEvent(Node camera, InputEvent inputEvent, Vector3 eventPosition, Vector3 normal, long shapeIndex)
    {
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            BeginRollMode();
        }
    }
}
